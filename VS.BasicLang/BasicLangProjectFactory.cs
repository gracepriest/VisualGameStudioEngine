using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace VS.BasicLang;

/// <summary>
/// Project factory for BasicLang projects (.blproj files)
/// Creates files from templates without full VS project management.
/// </summary>
[Guid(ProjectFactoryGuidString)]
public class BasicLangProjectFactory : IVsProjectFactory
{
    public const string ProjectFactoryGuidString = "95a8f3e1-1234-4567-8906-abcdef123456";
    public static readonly Guid ProjectFactoryGuid = new Guid(ProjectFactoryGuidString);

    private readonly AsyncPackage _package;
    private Microsoft.VisualStudio.OLE.Interop.IServiceProvider? _site;

    public BasicLangProjectFactory(AsyncPackage package)
    {
        _package = package ?? throw new ArgumentNullException(nameof(package));
    }

    public int SetSite(Microsoft.VisualStudio.OLE.Interop.IServiceProvider psp)
    {
        _site = psp;
        return VSConstants.S_OK;
    }

    public int CanCreateProject(string pszFilename, uint grfCreateFlags, out int pfCanCreate)
    {
        // We can create .blproj files
        pfCanCreate = pszFilename?.EndsWith(".blproj", StringComparison.OrdinalIgnoreCase) == true ? 1 : 0;
        return VSConstants.S_OK;
    }

    public int CreateProject(string pszFilename, string pszLocation, string pszName,
        uint grfCreateFlags, ref Guid iidProject, out IntPtr ppvProject, out int pfCanceled)
    {
        ppvProject = IntPtr.Zero;
        pfCanceled = 0;

        // BasicLang projects are folder-based with external build tools
        // The template wizard creates the files; we don't need a VS project hierarchy
        // Just ensure the project file exists
        if (!string.IsNullOrEmpty(pszFilename) && !File.Exists(pszFilename))
        {
            var dir = Path.GetDirectoryName(pszFilename);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        return VSConstants.S_OK;
    }

    public int Close()
    {
        return VSConstants.S_OK;
    }
}
