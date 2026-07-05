using System.ComponentModel.Composition;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Utilities;

namespace BasicLang.VisualStudio.LanguageService;

/// <summary>
/// Content type definition for BasicLang files.
/// </summary>
public static class BasicLangContentTypeDefinition
{
    /// <summary>
    /// The BasicLang content type.
    /// </summary>
    [Export]
    [Name(BasicLangConstants.ContentTypeName)]
    [BaseDefinition(CodeRemoteContentDefinition.CodeRemoteContentTypeName)]
    public static ContentTypeDefinition? BasicLangContentType;

    /// <summary>
    /// Associates .bl files with the BasicLang content type.
    /// </summary>
    [Export]
    [FileExtension(BasicLangConstants.FileExtension)]
    [ContentType(BasicLangConstants.ContentTypeName)]
    public static FileExtensionToContentTypeDefinition? BasicLangFileExtension;

    /// <summary>
    /// Associates .bas files with the BasicLang content type.
    /// </summary>
    [Export]
    [FileExtension(BasicLangConstants.FileExtension2)]
    [ContentType(BasicLangConstants.ContentTypeName)]
    public static FileExtensionToContentTypeDefinition? BasicLangFileExtension2;

    /// <summary>
    /// Associates .cls implicit-class files with the BasicLang content type.
    /// </summary>
    [Export]
    [FileExtension(BasicLangConstants.ClassExtension)]
    [ContentType(BasicLangConstants.ContentTypeName)]
    public static FileExtensionToContentTypeDefinition? BasicLangClassExtension;

    /// <summary>
    /// Associates .mod implicit-module files with the BasicLang content type.
    /// </summary>
    [Export]
    [FileExtension(BasicLangConstants.ModuleExtension)]
    [ContentType(BasicLangConstants.ContentTypeName)]
    public static FileExtensionToContentTypeDefinition? BasicLangModuleExtension;
}
