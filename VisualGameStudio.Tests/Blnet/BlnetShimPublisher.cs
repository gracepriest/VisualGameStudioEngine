using BasicLang.Compiler.CodeGen.Net;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// Thin test-only wrapper over <see cref="NetShimPublisher"/>: publishes the BlnetTestShim
/// project with Native AOT (NativeLib=Shared) exactly once per test run and hands out the
/// resulting native DLL path. Multiple fixtures share the single publish via
/// <see cref="PublishOnce"/>; the captured publish output is exposed for warning-scan
/// assertions. Preserves the pre-move throwing behavior (rather than the product's
/// <c>NetShimPublishResult</c>) since existing fixtures assert on catching these exceptions.
/// </summary>
public static class BlnetShimPublisher
{
    private static readonly Lazy<(string DllPath, string Output)> Published =
        new(PublishCore, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Publishes on first call (cached for the rest of the run); returns the native DLL path.</summary>
    public static string PublishOnce() => Published.Value.DllPath;

    /// <summary>Combined stdout+stderr of the publish. Forces the publish if it has not run yet.</summary>
    public static string PublishOutput => Published.Value.Output;

    private static (string DllPath, string Output) PublishCore()
    {
        var repoRoot = FindRepoRoot();
        var csproj = Path.Combine(repoRoot, "VisualGameStudio.Tests", "TestAssets", "BlnetTestShim", "BlnetTestShim.csproj");

        // Per-run scratch dir: the Lazy dedupes within this test host; the pid keeps
        // concurrent test runs from clobbering each other's publish output.
        var scratch = Path.Combine(Path.GetTempPath(), $"blnet_shim_{Environment.ProcessId}");

        var result = NetShimPublisher.Publish(csproj, scratch, workingDirectory: repoRoot);

        if (!result.Success)
        {
            if (result.ExitCode != 0)
                throw new InvalidOperationException($"dotnet publish of BlnetTestShim failed (exit {result.ExitCode}).\n{result.Output}");

            var expectedDll = Path.Combine(scratch, "BlnetTestShim.dll");
            throw new FileNotFoundException($"Publish succeeded but the native library is missing: {expectedDll}\n{result.Output}");
        }

        return (result.DllPath, result.Output);
    }

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "VisualGameStudioEngine.sln")))
                return dir.FullName;
        throw new DirectoryNotFoundException(
            $"VisualGameStudioEngine.sln not found in any parent of {TestContext.CurrentContext.TestDirectory}");
    }
}
