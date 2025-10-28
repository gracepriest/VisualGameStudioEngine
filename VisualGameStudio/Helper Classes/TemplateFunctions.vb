Module TemplateFunctions
    Public templateManager As New TemplateManager()
    Public arrTemplates As List(Of Template)
    Public templateMan As List(Of TemplateManifest)

    Public imgList As New ImageList()
    Public Sub InitializeTemplates()
        arrTemplates = templateManager.LoadTemplates()
        templateMan = templateManager.LoadTemplateManifest(arrTemplates(0))

        'loads a template file And replaces placeholders with provided values
        'load templates into listbox
        imgList.ImageSize = New Size(64, 64)
        imgList.ColorDepth = ColorDepth.Depth32Bit
        For Each item In templateMan
            imgList.Images.Add(item.Name, Image.FromFile(item.thumbnail))
        Next
    End Sub

    Public Function LoadSelectedTemplateManifest(selectedTemplate As Template) As List(Of TemplateManifest)
        Return templateManager.LoadTemplateManifest(selectedTemplate)
    End Function
End Module
