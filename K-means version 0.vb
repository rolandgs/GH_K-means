Option Strict Off
Option Explicit On

Imports Rhino
Imports Rhino.Geometry
Imports Rhino.DocObjects
Imports Rhino.Collections

Imports GH_IO
Imports GH_IO.Serialization
Imports Grasshopper
Imports Grasshopper.Kernel
Imports Grasshopper.Kernel.Data
Imports Grasshopper.Kernel.Types

Imports System
Imports System.IO
Imports System.Xml
Imports System.Xml.Linq
Imports System.Linq
Imports System.Data
Imports System.Drawing
Imports System.Reflection
Imports System.Collections
Imports System.Windows.Forms
Imports Microsoft.VisualBasic
Imports System.Collections.Generic
Imports System.Runtime.InteropServices



''' <summary>
''' This class will be instantiated on demand by the Script component.
''' </summary>
Public Class Script_Instance
  Inherits GH_ScriptInstance

  #Region "Utility functions"
  ''' <summary>Print a String to the [Out] Parameter of the Script component.</summary>
  ''' <param name="text">String to print.</param>
  Private Sub Print(ByVal text As String)
    __out.Add(text)
  End Sub
  ''' <summary>Print a formatted String to the [Out] Parameter of the Script component.</summary>
  ''' <param name="format">String format.</param>
  ''' <param name="args">Formatting parameters.</param>
  Private Sub Print(ByVal format As String, ByVal ParamArray args As Object())
    __out.Add(String.Format(format, args))
  End Sub
  ''' <summary>Print useful information about an object instance to the [Out] Parameter of the Script component. </summary>
  ''' <param name="obj">Object instance to parse.</param>
  Private Sub Reflect(ByVal obj As Object)
    __out.Add(GH_ScriptComponentUtilities.ReflectType_VB(obj))
  End Sub
  ''' <summary>Print the signatures of all the overloads of a specific method to the [Out] Parameter of the Script component. </summary>
  ''' <param name="obj">Object instance to parse.</param>
  Private Sub Reflect(ByVal obj As Object, ByVal method_name As String)
    __out.Add(GH_ScriptComponentUtilities.ReflectType_VB(obj, method_name))
  End Sub
#End Region
  
#Region "Members"
  ''' <summary>Gets the current Rhino document.</summary>
  Private RhinoDocument As RhinoDoc
  ''' <summary>Gets the Grasshopper document that owns this script.</summary>
  Private GrasshopperDocument as GH_Document
  ''' <summary>Gets the Grasshopper script component that owns this script.</summary>
  Private Component As IGH_Component
  ''' <summary>
  ''' Gets the current iteration count. The first call to RunScript() is associated with Iteration=0.
  ''' Any subsequent call within the same solution will increment the Iteration count.
  ''' </summary>
  Private Iteration As Integer
#End Region

  ''' <summary>
  ''' This procedure contains the user code. Input parameters are provided as ByVal arguments, 
  ''' Output parameter are ByRef arguments. You don't have to assign output parameters, 
  ''' they will have default values.
  ''' </summary>
  Private Sub RunScript(ByVal P As List(Of String), ByVal C As List(Of String), ByVal K As Integer, ByVal S As Integer, ByVal seed As Integer, ByRef Pgrouped As Object, ByRef Kcenters As Object, ByRef P_K_index As Object, ByRef Mean_Dist As Object) 
    'P input conversion and errorcheck
    Dim arrP(P.count - 1, 0)
    Dim errorP = True
    For i As Integer = 0 To P.count - 1
      Dim templist()
      templist = (Split(P.item(i), ","))
      For j As Integer = 0 To templist.count - 1
        If j > UBound(arrP, 2) Then
          ReDim Preserve arrP(UBound(arrP, 1), j)
        End If
        arrP(i, j) = templist(j)
        If Not IsNumeric(arrP(i, j)) Then
          errorP = False
        End If
      Next
    Next
    If Not errorP Then
      Print("Input P does not consist of numbers separated by comma's")
      Exit Sub
    End If

    'C input conversion and errorcheck
    Dim arrC(C.count - 1, 0)
    Dim errorC = True
    For i As Integer = 0 To C.count - 1
      Dim templist()
      templist = (Split(C.item(i), ","))
      For j As Integer = 0 To templist.count - 1
        If j > UBound(arrC, 2) Then
          ReDim Preserve arrC(UBound(arrC, 1), j)
        End If
        arrC(i, j) = templist(j)
        If Not IsNumeric(arrC(i, j)) Then
          errorC = False
        End If
      Next
    Next
    If Not errorC Then
      Print("Input C does not consist of numbers separated by comma's")
      Exit Sub
    End If

    'Dimensionality Check'
    Dim dimensions(0) As Integer
    Dim dimension As Integer
    Dim dimsame = True
    Dim dcheck As New List(Of Object)(P)
    dcheck.AddRange(C)
    For i As Integer = 0 To dcheck.Count - 1
      Dim templist()
      templist = Split(dcheck.Item(i), ",")
      ReDim Preserve dimensions(i)
      dimensions(i) = CInt(templist.count)
    Next
    For i As Integer = 1 To UBound(dimensions)
      If dimensions(i - 1) <> dimensions(i) Then
        dimsame = False
        Exit For
      End If
    Next
    If dimsame = False Then
      Print("Error, inputs have unequal number Of dimensions.")
      Exit Sub
    Else
      dimension = dimensions(UBound(dimensions))
      Print("The datapionts all have " & dimension & " dimensions.")
    End If

    'Test whether there are enough points to perform clustering
    If Not P.count >= C.count + K Then
      Print("There are too few points or too many clusters to perform clustering")
      Exit Sub
    End If

    'Check whether and where C occurs in P
    Dim indCinP As New List (Of Integer)
    For i As Integer = 0 To C.count - 1
      For j As Integer = 0 To P.count - 1
        If InStr(P(j), C(i)) <> 0 Then
          indCinP.Add(j)
        End If
      Next
    Next

    'Main variables
    Dim datapoints(P.count, dimension) As Double
    Dim coordlist() As String
    Dim clusterpoints(C.count, dimension) As Double

    'Reformat inputs
    For i As Integer = 0 To P.Count - 1
      coordlist = Split(P(i), ",")
      For j As Integer = 0 To coordlist.Count - 1
        datapoints(i, j) = CDbl(coordlist(j))
      Next
    Next

    For i As Integer = 0 To C.Count - 1
      coordlist = Split(C(i), ",")
      For j As Integer = 0 To coordlist.Count - 1
        clusterpoints(i, j) = CDbl(coordlist(j))
      Next
    Next

    'Determine points elligeble for initial cluster centers.
    Dim arrIniPts((P.count - indCinP.count - 1), dimension - 1)
    Dim indOffset = 0
    For i As Integer = 0 To P.count - 1
      If Not indCinP.Contains(i) Then
        For j As Integer = 0 To dimension - 1
          arrIniPts(i - indOffset, j) = arrP(i, j)
        Next
      Else
        indOffset += 1
      End If
    Next

    'Create randomized initiation indexes
    Dim seeduse As Integer = 0
    Rnd(-1)
    Randomize(seed)
    Dim indIniMax As Integer = UBound(arrIniPts, 1)
    Dim arrIniInd(S - 1, K - 1)
    Dim randIniInd As New Hashset(Of Integer)
    For h As Integer = 0 To S - 1
      While randIniInd.count < K
        randIniInd.Add((indIniMax) * Rnd())
      End While
      If randIniInd.count = K Then
        randIniInd.ToList
        For i As Integer = 0 To K - 1
          arrIniInd(h, i) = randIniInd(i)
        Next
        randIniInd.clear
      End If
    Next

    Dim minMeanDist As Double = 0
    Dim arrKBest(C.count + K - 1,(dimension - 1))
    Dim arrAtrBest(UBound(arrP,1)) As Integer
    'ALGORITHM INI------------------------------------------------------------
    'sample iterations
    For h As Integer = 0 To S - 1

      'Create initial centerlist
      Dim arrK (C.count + K - 1,(dimension - 1))
      For i As Integer = 0 To K - 1
        For j As Integer = 0 To dimension - 1
          arrK(i, j) = arrP(arrIniInd(h, i), j).clone
        Next
      Next
      For i As Integer = K To C.count + K - 1
        For j As Integer = 0 To dimension - 1
          arrK(i, j) = arrC(i - K, j).clone
        Next
      Next

      Dim iteration As Integer = 0
      Dim maxIteration As Integer = 180
      Dim terminate = False
      'ALGORITHM ITERATIONS----------------------------------------------------
      Do While iteration < maxIteration And Not terminate

        'Reference clusterpoints for termination
        Dim arrKRef(C.count + K - 1,(dimension - 1))
        arrKRef = arrK.clone

        'Determine which cluster is closest to each point
        Dim shrtDist As Double
        Dim arrAtr(UBound(arrP,1)) As Integer
        Dim arrPDist(UBound(arrP,1)) As Double
        Dim arrKDist(UBound(arrK,1)) As Double
        Dim arrKCount(UBound(arrK,1)) As Integer
        For i As Integer = 0 To UBound(arrP, 1)
          For l As Integer = 0 To UBound(arrK, 1)
            If l = 0 Or NDimDist(arrP, arrK, i, l) < shrtDist Then
              shrtDist = NDimDist(arrP, arrK, i, l)
              arrAtr(i) = l
              arrPDist(i) = shrtDist
            End If
          Next
        Next

        'Determine clustercount and distance
        For i As Integer = 0 To UBound(arrP, 1)
          For l As Integer = 0 To UBound(arrK, 1)
            If arrAtr(i) = l Then
              arrKCount(l) += 1
              arrKDist(l) += arrPDist(i)
            End If
          Next
        Next

        'Calculate meanDist
        Dim meanDist As Double = 0
        For l As Integer = 0 To UBound(arrK, 1)
          meanDist += arrKDist(l)
        Next
        meanDist = meanDist / UBound(arrP, 1)



        'Determine average point of each luster
        Dim currentId As Integer = 0
        Dim newPtK(UBound(arrK, 1),UBound(arrK, 2))
        For l As Integer = 0 To UBound(arrK, 1)
          Dim arrPtsK(arrKCount(l),dimension -1)
          For i As Integer = 0 To arrAtr.count - 1
            If arrAtr(i) = l Then
              For j As Integer = 0 To dimension - 1
                arrPtsK(currentId, j) = arrP(i, j).clone
              Next
              currentId += 1
            End If
          Next
          If arrKCount(l) > 0 And l <= K Then
            For j As Integer = 0 To dimension - 1
              arrK(l, j) = NDimMean(arrPtsK)(j)
            Next
          End If
          currentId = 0
        Next


        'Test Convergence
        Dim kDist As Double = 0
        For l As Integer = 0 To UBound(arrK, 1)
          kDist += NDimDist(arrK, arrKRef, l, l)
        Next
        If kDist > 0 Then
          'Print("Still changing, keep going. kDist: " & kDist)
        ElseIf kDist = 0 Then
          Print("Converged, stop iterating. meanDist: " & meanDist)
          terminate = True
        Else
          Print("kDist < 0, this should not happen")
        End If

        iteration += 1
        'STORE CURRENT BEST SOLUTION
        If h = 0 Or meanDist < minMeanDist
          minMeanDist = meanDist
          arrKBest = arrK.Clone
          arrAtrBest = arrAtr.Clone
        End If
      Loop
      'END OF ITERATIONS--------------------------------------------------------


    Next
    'END OF SAMPLES-----------------------------------------------------------

    'Create Kcenter output treeK
    Dim kList As New List (Of String)
    Dim treeK As New List (Of String) '(UBound(arrKBest, 1))
    For i As Integer = 0 To UBound(arrKBest, 1)
      For j As Integer = 0 To UBound(arrKBest, 2)
        kList.add(arrKBest(i, j))
      Next
      treeK.Add(String.Join(",", kList))
      kList.clear

    Next

    'Put points into branches
    Dim treeP As New List (Of String)
    For l As Integer = 0 To UBound(arrKBest, 1)
      'Dim pth As New GH_Path(l)
      For i As Integer = 0 To P.count - 1
        If l = arrAtrBest(i) Then
          treeP.add(p(i))
        End If
      Next
    Next

    'Store attribution indexes
    Dim treeAtr As New List (Of Integer)
    For i As Integer = 0 To P.count - 1
      treeAtr.Add(arrAtrBest(i))
    Next


    'Outputs
    Pgrouped = treeP
    Kcenters = treeK
    P_K_index = treeAtr
    Mean_Dist = minMeanDist
  End Sub 

  '<Custom additional code> 
  Function NDimDist(ByRef Pt1(,), ByRef Pt2(,), i, l)
    Dim distSqrt As Double = 0, dist As Double = 0
    For j As Integer = 0 To UBound(Pt1, 2)
      distSqrt = distSqrt + ((Pt1(i, j) - Pt2(l, j)) ^ 2)
    Next
    dist = distSqrt 'Math.Sqrt(distSqrt)
    Return dist
    distSqrt = 0
    dist = 0
  End Function

  Function NDimMean(ByRef Pts(,))
    Dim jDimMean As Double = 0, ptMean(UBound(Pts, 2)) As Double
    For j As Integer = 0 To UBound(Pts, 2)
      For i As Integer = 0 To UBound(Pts, 1)
        jDimMean = jDimMean + Pts(i, j)
      Next
      ptMean(j) = jDimMean / (UBound(Pts, 1))
      jDimMean = 0
    Next
    Return ptMean
  End Function


  '</Custom additional code> 

  Private __err As New List(Of String)
  Private __out As New List(Of String)
  Private doc As RhinoDoc = RhinoDoc.ActiveDoc            'Legacy field.
  Private owner As Grasshopper.Kernel.IGH_ActiveObject    'Legacy field.
  Private runCount As Int32                               'Legacy field.
  
  Public Overrides Sub InvokeRunScript(ByVal owner As IGH_Component, _
                                       ByVal rhinoDocument As Object, _
                                       ByVal iteration As Int32, _
                                       ByVal inputs As List(Of Object), _
                                       ByVal DA As IGH_DataAccess) 
    'Prepare for a new run...
    '1. Reset lists
    Me.__out.Clear()
    Me.__err.Clear()

    'Current field assignments.
    Me.Component = owner
    Me.Iteration = iteration
    Me.GrasshopperDocument = owner.OnPingDocument()
    Me.RhinoDocument = TryCast(rhinoDocument, Rhino.RhinoDoc)

    'Legacy field assignments
    Me.owner = Me.Component
    Me.runCount = Me.Iteration
    Me.doc = Me.RhinoDocument

    '2. Assign input parameters
    Dim P As List(Of String) = Nothing
    If (inputs(0) IsNot Nothing) Then
      P = GH_DirtyCaster.CastToList(Of String)(inputs(0))
    End If

    Dim C As List(Of String) = Nothing
    If (inputs(1) IsNot Nothing) Then
      C = GH_DirtyCaster.CastToList(Of String)(inputs(1))
    End If

    Dim K As Integer = Nothing
    If (inputs(2) IsNot Nothing) Then
      K = DirectCast(inputs(2), Integer)
    End If

    Dim S As Integer = Nothing
    If (inputs(3) IsNot Nothing) Then
      S = DirectCast(inputs(3), Integer)
    End If

    Dim seed As Integer = Nothing
    If (inputs(4) IsNot Nothing) Then
      seed = DirectCast(inputs(4), Integer)
    End If



    '3. Declare output parameters
  Dim Pgrouped As System.Object = Nothing
  Dim Kcenters As System.Object = Nothing
  Dim P_K_index As System.Object = Nothing
  Dim Mean_Dist As System.Object = Nothing


    '4. Invoke RunScript
    Call RunScript(P, C, K, S, seed, Pgrouped, Kcenters, P_K_index, Mean_Dist)

    Try
      '5. Assign output parameters to component...
      If (Pgrouped IsNot Nothing) Then
        If (GH_Format.TreatAsCollection(Pgrouped)) Then
          Dim __enum_Pgrouped As IEnumerable = DirectCast(Pgrouped, IEnumerable)
          DA.SetDataList(1, __enum_Pgrouped)
        Else
          If (TypeOf Pgrouped Is Grasshopper.Kernel.Data.IGH_DataTree) Then
            'merge tree
            DA.SetDataTree(1, DirectCast(Pgrouped, Grasshopper.Kernel.Data.IGH_DataTree))
          Else
            'assign direct
            DA.SetData(1, Pgrouped)
          End If
        End If
      Else
        DA.SetData(1, Nothing)
      End If
      If (Kcenters IsNot Nothing) Then
        If (GH_Format.TreatAsCollection(Kcenters)) Then
          Dim __enum_Kcenters As IEnumerable = DirectCast(Kcenters, IEnumerable)
          DA.SetDataList(2, __enum_Kcenters)
        Else
          If (TypeOf Kcenters Is Grasshopper.Kernel.Data.IGH_DataTree) Then
            'merge tree
            DA.SetDataTree(2, DirectCast(Kcenters, Grasshopper.Kernel.Data.IGH_DataTree))
          Else
            'assign direct
            DA.SetData(2, Kcenters)
          End If
        End If
      Else
        DA.SetData(2, Nothing)
      End If
      If (P_K_index IsNot Nothing) Then
        If (GH_Format.TreatAsCollection(P_K_index)) Then
          Dim __enum_P_K_index As IEnumerable = DirectCast(P_K_index, IEnumerable)
          DA.SetDataList(3, __enum_P_K_index)
        Else
          If (TypeOf P_K_index Is Grasshopper.Kernel.Data.IGH_DataTree) Then
            'merge tree
            DA.SetDataTree(3, DirectCast(P_K_index, Grasshopper.Kernel.Data.IGH_DataTree))
          Else
            'assign direct
            DA.SetData(3, P_K_index)
          End If
        End If
      Else
        DA.SetData(3, Nothing)
      End If
      If (Mean_Dist IsNot Nothing) Then
        If (GH_Format.TreatAsCollection(Mean_Dist)) Then
          Dim __enum_Mean_Dist As IEnumerable = DirectCast(Mean_Dist, IEnumerable)
          DA.SetDataList(4, __enum_Mean_Dist)
        Else
          If (TypeOf Mean_Dist Is Grasshopper.Kernel.Data.IGH_DataTree) Then
            'merge tree
            DA.SetDataTree(4, DirectCast(Mean_Dist, Grasshopper.Kernel.Data.IGH_DataTree))
          Else
            'assign direct
            DA.SetData(4, Mean_Dist)
          End If
        End If
      Else
        DA.SetData(4, Nothing)
      End If

    Catch ex As Exception
      __err.Add(String.Format("Script exception: {0}", ex.Message))
    Finally
      'Add errors and messages...
      If (owner.Params.Output.Count > 0) Then
        If (TypeOf owner.Params.Output(0) Is Grasshopper.Kernel.Parameters.Param_String) Then
          Dim __errors_plus_messages As New List(Of String)
          If (Me.__err IsNot Nothing) Then __errors_plus_messages.AddRange(Me.__err)
          If (Me.__out IsNot Nothing) Then __errors_plus_messages.AddRange(Me.__out)
          If (__errors_plus_messages.Count > 0) Then
            DA.SetDataList(0, __errors_plus_messages)
          End If
        End If
      End If
    End Try
  End Sub 
End Class