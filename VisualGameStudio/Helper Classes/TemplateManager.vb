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
        Dim templates As New List(Of Template)()
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
    Public Property Defaults As Dictionary(Of String, Object)
    Public Property Scaffold As Dictionary(Of String, List(Of ScaffoldItem))
End Class

Public Class ScaffoldItem
    Public Property source As String
    Public Property destination As String
End Class