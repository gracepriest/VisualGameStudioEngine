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

    // Guards against re-entrant CreateProject calls: delegating to
    // IVsSolution.CreateProject with the CPS GUID can route back into this
    // factory via ProjectTypeGuids, which would recurse indefinitely.
    [ThreadStatic]
    private static bool _isDelegatingToCps;

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
        pfCanCreate = pszFilename != null && pszFilename.EndsWith(".blproj", StringComparison.OrdinalIgnoreCase)
            ? 1
            : 0;
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

        // Fail fast if the CPS delegation below routed back into this factory
        // instead of recursing.
        if (_isDelegatingToCps)
            return VSConstants.VS_E_INCOMPATIBLEPROJECT;

        try
        {
            // For SDK-style projects, delegate to the Common Project System
            // The CPS will handle the actual project creation based on the SDK

            // Get the solution service to create the project through the standard mechanism
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_serviceProvider != null)
            {
                var solution = GetService(typeof(SVsSolution)) as IVsSolution;
                if (solution != null)
                {
                    // Let VS handle the SDK-style project through normal CPS infrastructure
                    // The project file imports Microsoft.NET.Sdk which provides CPS integration
                    var guidCPS = new Guid("13B669BE-BB05-4DDF-9536-439F39A36129"); // CPS Guid

                    int hr;
                    _isDelegatingToCps = true;
                    try
                    {
                        hr = solution.CreateProject(
                            ref guidCPS,
                            pszFilename,
                            pszLocation,
                            pszName,
                            grfCreateFlags,
                            ref iidProject,
                            out ppvProject);
                    }
                    finally
                    {
                        _isDelegatingToCps = false;
                    }

                    if (hr == VSConstants.S_OK && ppvProject != IntPtr.Zero)
                        return VSConstants.S_OK;

                    // Delegation failed: propagate its HRESULT. Never return S_OK
                    // with a null ppvProject (IVsProjectFactory contract violation).
                    ppvProject = IntPtr.Zero;
                    return hr < 0 ? hr : VSConstants.E_FAIL;
                }
            }

            // No solution service available — we cannot produce a project.
            return VSConstants.E_FAIL;
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
        Guid guidInterface = VSConstants.IID_IUnknown;
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
