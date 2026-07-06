using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Compiles generated C# source in-memory with Roslyn, loads the resulting assembly, invokes
/// its entry point, and returns whatever it wrote to stdout. Used by the Task 3 portability
/// test to prove the SAME BasicLang source that runs on the C++ backend also runs on the C#
/// backend and produces identical output.
/// </summary>
public static class CSharpRun
{
    /// <summary>
    /// Compile <paramref name="generatedCSharp"/> to an in-memory assembly, run its entry point
    /// (Main), and return captured stdout (newlines normalized to "\n"). Throws an assertion
    /// failure with the Roslyn diagnostics if compilation fails, or if no entry point is found.
    /// </summary>
    public static string CompileAndRun(string generatedCSharp)
    {
        var tree = CSharpSyntaxTree.ParseText(generatedCSharp);

        // Reference the core runtime assemblies (System.Runtime, Console, System.Collections,
        // Linq, ...) resolved from the trusted-platform-assemblies list so List<T>,
        // Dictionary<K,V>, Console.WriteLine, etc. all bind.
        var tpa = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var references = tpa
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();

        var compilation = CSharpCompilation.Create(
            assemblyName: "PortabilityRun_" + Guid.NewGuid().ToString("N"),
            syntaxTrees: new[] { tree },
            references: references,
            options: new CSharpCompilationOptions(
                OutputKind.ConsoleApplication,
                optimizationLevel: OptimizationLevel.Release,
                // The generated program's entry point may be a plain Main; let Roslyn find it.
                allowUnsafe: false));

        using var peStream = new MemoryStream();
        var emit = compilation.Emit(peStream);
        if (!emit.Success)
        {
            var diags = string.Join("\n", emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString()));
            Assert.Fail($"C# compilation of generated code failed:\n{diags}\n--- source ---\n{generatedCSharp}");
        }

        peStream.Seek(0, SeekOrigin.Begin);
        var asm = Assembly.Load(peStream.ToArray());
        var entry = asm.EntryPoint;
        Assert.That(entry, Is.Not.Null, "generated C# assembly has no entry point");

        var originalOut = Console.Out;
        var sw = new StringWriter();
        try
        {
            Console.SetOut(sw);
            var parameters = entry!.GetParameters();
            var args = parameters.Length == 0 ? null : new object[] { Array.Empty<string>() };
            entry.Invoke(null, args);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        return sw.ToString().Replace("\r\n", "\n");
    }
}
