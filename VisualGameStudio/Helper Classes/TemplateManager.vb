Imports System.IO
Imports System.Text.Json



Public Class TemplateManager
    Public Function ReadJsonFile(Of T)(filePath As String) As T
        If Not File.Exists(filePath) Then
            Throw New FileNotFoundException("File not found: " & filePath)
        End If

        Dim json As String = File.ReadAllText(filePath)
        Dim firstNonWs As Char = json.SkipWhile(Function(c) Char.IsWhiteSpace(c)).FirstOrDefault()

        Dim options As New JsonSerializerOptions With {
        .PropertyNameCaseInsensitive = True,
        .ReadCommentHandling = JsonCommentHandling.Skip,
        .AllowTrailingCommas = True
    }

        Try
            Return JsonSerializer.Deserialize(Of T)(json, options)

        Catch ex As JsonException
            ' These three are gold for debugging
            Dim msg = $"JSON error in {filePath}" & Environment.NewLine &
                  $"Root starts with: '{firstNonWs}'" & Environment.NewLine &
                  $"Path: {ex.Path}" & Environment.NewLine &
                  $"Line: {ex.LineNumber}, Byte: {ex.BytePositionInLine}" & Environment.NewLine &
                  $"Message: {ex.Message}"
            MessageBox.Show(msg, "JSON Deserialize Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Throw
        End Try
    End Function
    'first we need a function to load all templates
    'it starts by looking in the Templates folder
    'reading engine-template.json
    'and for each template found in there, add it to a list
    'the function will return a list of templates
    Public Function LoadTemplates() As List(Of Template)
        Dim engineTemplates As List(Of Template)
        Dim engineTemplatePath As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                 "Resource Files", "Template", "engine-templates.json")

        Try

            engineTemplates = ReadJsonFile(Of List(Of Template))(engineTemplatePath)


        Catch ex As FileNotFoundException
            ' Handle the case where the engine-template.json file does not exist

            Throw New FileNotFoundException("The engine-template.json file was not found.", engineTemplatePath)
        End Try
        Return engineTemplates
    End Function
    'load the manifest file for a template
    Public Function LoadTemplateManifest(template As Template) As List(Of TemplateManifest)
        Dim templateManifestPath As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                 "Resource Files", "Template", template.Manifest)
        If Not File.Exists(templateManifestPath) Then
            Throw New FileNotFoundException($"Manifest not found: {templateManifestPath}")
        End If

        Dim templateManList As New List(Of TemplateManifest)

        Try
            templateManList = ReadJsonFile(Of List(Of TemplateManifest))(templateManifestPath)

        Catch ex As FileNotFoundException
            ' Handle the case where the manifest file does not exist
            Throw New FileNotFoundException("The template manifest file was not found.", templateManifestPath)
        End Try

        Return templateManList
    End Function
End Class

Public Class Template
    Public Property Id As String
    Public Property Name As String
    ' Public Property Description As String
    Public Property Category As String
    Public Property Manifest As String
End Class

Public Class TemplateManifest
    'manifest.json properties
    Public Property Id As Integer
    Public Property Name As String
    Public Property Description As String
    Public Property Version As String
    Public Property Languages As List(Of String)
    Public Property Thumbnail As String
    Public Property ProjectType As String
    Public Property Defaults As TemplateDefaults
    Public Property Scaffold As Dictionary(Of String, List(Of ScaffoldItem))
End Class

Public Class ScaffoldItem
    Public Property Source As String
    Public Property Destination As String
End Class

Public Class TemplateDefaults
    Public Property OutputType As String
    Public Property WindowHeight As Integer
    Public Property WindowWidth As Integer
    ' Public Property FullScreen As Boolean
    'Public Property VSync As Boolean
    'Public Property TargetFPS As Integer
End Class