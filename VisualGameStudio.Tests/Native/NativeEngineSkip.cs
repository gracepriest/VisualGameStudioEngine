using System;
using System.IO;
using System.Runtime.InteropServices;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Why a P/Invoke into the native engine could not bind, phrased for the situation it ACTUALLY
/// happened in.
///
/// <para>⛔ Every one of these fixtures used to answer <c>DllNotFoundException</c> with the single
/// fixed line "<c>… not staged next to the test binary; refresh IDE\ first</c>". That is right on
/// Windows with the DLL missing and WRONG everywhere else — most visibly on Linux and macOS, where
/// <c>VisualGameStudioEngine.dll</c> is a Windows PE that can never load no matter how carefully it
/// is staged. A developer reading that skip reason on a non-Windows box is told to fix something
/// that is not broken, and the thing that IS true — these tests only run on Windows — is never
/// said. 47 sites across 39 fixtures repeated it.</para>
///
/// <para>The <c>EntryPointNotFoundException</c> messages ("predates the … exports") are left as
/// they were: that exception can only be raised once the library has LOADED, so the platform is
/// already known good and "refresh IDE\" is genuinely the right advice.</para>
/// </summary>
internal static class NativeEngineSkip
{
    /// <summary>
    /// The skip reason for a <see cref="DllNotFoundException"/> binding <paramref name="dll"/>,
    /// read off the current environment.
    /// </summary>
    public static string DllNotFound(string dll)
    {
        var stagedPath = Path.Combine(AppContext.BaseDirectory, dll);
        return DllNotFoundReason(
            dll,
            isWindows: RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            stagedPath: stagedPath,
            isStaged: File.Exists(stagedPath),
            osDescription: RuntimeInformation.OSDescription.Trim());
    }

    /// <summary>
    /// The decision, separated from the environment that feeds it.
    ///
    /// <para>Split out because only ONE of these three branches can ever run on a given host, so a
    /// skip observed on one platform proves nothing about the other two. Taking the environment as
    /// arguments lets all three be exercised in a test instead of asserted to be right.</para>
    /// </summary>
    internal static string DllNotFoundReason(
        string dll, bool isWindows, string stagedPath, bool isStaged, string osDescription)
    {
        if (!isWindows)
        {
            return $"{dll} is a Windows PE and this is {osDescription}; the native engine fixtures "
                   + "only run on Windows. Nothing is mis-staged — there is no build of this "
                   + "library for the current platform.";
        }

        if (!isStaged)
        {
            return $"{dll} is not staged next to the test binary ({stagedPath}); "
                   + "rebuild the engine and refresh IDE\\ first.";
        }

        // Present and still unloadable: a missing transitive dependency or an architecture
        // mismatch. Saying "not staged" here sends the reader to look at a file that is sitting
        // exactly where it belongs.
        return $"{dll} IS present at {stagedPath} but the loader refused it — most often a missing "
               + "dependency next to it, or an x86/x64 mismatch with the test host.";
    }
}
