using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Flavor;

namespace BasicLang.VisualStudio.ProjectSystem;

/// <summary>
/// Project factory for BasicLang projects.
/// </summary>
[Guid(Guids.ProjectTypeGuidString)]
public class BasicLangProjectFactory : FlavoredProjectFactoryBase
{
    private readonly AsyncPackage _package;

    public BasicLangProjectFactory(AsyncPackage package)
    {
        _package = package;
    }

    protected override object PreCreateForOuter(IntPtr outerProjectIUnknown)
    {
        return new BasicLangProject(_package);
    }
}

/// <summary>
/// Flavored project for BasicLang.
/// </summary>
public class BasicLangProject : FlavoredProjectBase
{
    private readonly AsyncPackage _package;

    public BasicLangProject(AsyncPackage package)
    {
        _package = package;
    }

    protected override void SetInnerProject(IntPtr innerIUnknown)
    {
        base.SetInnerProject(innerIUnknown);
    }
}
