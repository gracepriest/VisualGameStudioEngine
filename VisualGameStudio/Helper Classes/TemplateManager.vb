Imports System.IO
Imports System.Text.Json



Public Class TemplateManager
    Public Function ReadJsonFile(Of T)(filePath As String) As T
        Dim jsonString As String = File.ReadAllText(filePath)
        Dim options As New JsonSerializerOptions With {
            .PropertyNameCaseInsensitive = True,
            .ReadCommentHandling = JsonCommentHandling.Skip,
            .AllowTrailingCommas = True
          }

        Return JsonSerializer.Deserialize(Of T)(jsonString, options)
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
        MsgBox(engineTemplatePath)
        Try

            engineTemplates = ReadJsonFile(Of List(Of Template))(engineTemplatePath)

        Catch ex As FileNotFoundException
            ' Handle the case where the engine-template.json file does not exist

            Throw New FileNotFoundException("The engine-template.json file was not found.", engineTemplatePath)
        End Try
        Return engineTemplates
    End Function
    'load the manifest file for a template
    Public Function LoadTemplateManifest(template As Template) As TemplateManifest
        Dim templateManifestPath As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                 "Resource Files", "Template", "console", "manifest.json")
        Dim templateMan As TemplateManifest
        Try
            templateMan = ReadJsonFile(Of TemplateManifest)(templateManifestPath)
        Catch ex As FileNotFoundException
            ' Handle the case where the manifest file does not exist
            Throw New FileNotFoundException("The template manifest file was not found.", templateManifestPath)
        End Try
        Return templateMan
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
    Public Property Id As String
    Public Property Name As String
    Public Property Description As String
    Public Property Category As String
    Public Property Version As String
    Public Property Language As List(Of String)
    Public Property thumbnail As String
    Public Property ProjectType As String
    Public Property Defaults As TemplateDefaults
    Public Property Scaffold As Dictionary(Of String, List(Of ScaffoldItem))
End Class

Public Class ScaffoldItem
    Public Property source As String
    Public Property destination As String
End Class

Public Class TemplateDefaults
    Public Property outputType As String
    Public Property WindowHeight As Integer
    Public Property WindowWidth As Integer
    ' Public Property FullScreen As Boolean
    'Public Property VSync As Boolean
    'Public Property TargetFPS As Integer
End Class