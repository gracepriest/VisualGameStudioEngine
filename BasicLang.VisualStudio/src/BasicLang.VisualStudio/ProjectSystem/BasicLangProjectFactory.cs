using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using IOleServiceProvider = Microsoft.VisualStudio.OLE.Interop.IServiceProvider;

namespace BasicLang.VisualStudio.ProjectSystem;

/// <summary>
/// Project factory for BasicLang projects. Creates CPS-based projects for .blproj files.
/// </summary>
[Guid(Guids.ProjectTypeGuidString)]
public class BasicLangProjectFactory : IVsProjectFactory
{
    private readonly AsyncPackage _package;
    private IOleServiceProvider? _serviceProvider;

    public BasicLangProjectFactory(AsyncPackage package)
    {
        _package = package;
    }

    public int SetSite(IOleServiceProvider psp)
    {
        _serviceProvider = psp;
        return VSConstants.S_OK;
    }

    public int CanCreateProject(string pszFilename, uint grfCreateFlags, out int pfCanCreate)
    {
        pfCanCreate = 1; // We can create any .blproj file
        return VSConstants.S_OK;
    }

    public int CreateProject(
        string pszFilename,
        string pszLocation,
        string pszName,
        uint grfCreateFlags,
        ref Guid iidProject,
        out IntPtr ppvProject,
        out int pfCanceled)
    {
        ppvProject = IntPtr.Zero;
        pfCanceled = 0;

        try
        {
            // For SDK-style projects, delegate to the Common Project System
            // The CPS will handle the actual project creation based on the SDK

            // Get the solution service to create the project through the standard mechanism
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_serviceProvider != null)
            {
                var solution = (IVsSolution)GetService(typeof(SVsSolution));
                if (solution != null)
                {
                    // Let VS handle the SDK-style project through normal CPS infrastructure
                    // The project file imports Microsoft.NET.Sdk which provides CPS integration
                    var guidCPS = new Guid("13B669BE-BB05-4DDF-9536-439F39A36129"); // CPS Guid

                    int hr = solution.CreateProject(
                        ref guidCPS,
                        pszFilename,
                        pszLocation,
                        pszName,
                        grfCreateFlags,
                        ref iidProject,
                        out ppvProject);

                    if (hr == VSConstants.S_OK)
                        return VSConstants.S_OK;
                }
            }

            // Fallback: Return S_OK and let VS handle it through extension lookup
            return VSConstants.S_OK;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"BasicLangProjectFactory.CreateProject failed: {ex.Message}");
            return VSConstants.E_FAIL;
        }
    }

    public int Close()
    {
        _serviceProvider = null;
        return VSConstants.S_OK;
    }

    private object? GetService(Type serviceType)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (_serviceProvider == null)
            return null;

        Guid guidService = serviceType.GUID;
        Guid guidInterface = serviceType.GUID;
        IntPtr pUnknown;

        int hr = _serviceProvider.QueryService(ref guidService, ref guidInterface, out pUnknown);
        if (hr != VSConstants.S_OK || pUnknown == IntPtr.Zero)
            return null;

        try
        {
            return Marshal.GetObjectForIUnknown(pUnknown);
        }
        finally
        {
            Marshal.Release(pUnknown);
        }
    }
}
