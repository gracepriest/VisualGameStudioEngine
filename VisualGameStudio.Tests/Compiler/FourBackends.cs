using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// One program, all four backends compiled AND RUN, in process. The property a fixture asserts
/// through this is "the backends agree", not "one backend prints the number".
///
/// <para>⚠ The C# leg is in-process Roslyn (emit a console assembly to memory, load it, invoke
/// the entry point, capture <c>Console.Out</c>), NOT <c>CliTestHarness.CompileRunCSharp</c>:
/// that spawns <c>BasicLang.exe</c>, a Windows apphost that is not deployed on Linux, which is
/// why the 18 <c>_CSharp</c> rows that use it sit in this machine's baseline failure set. Each
/// program loads as a fresh assembly, so one test's module globals cannot leak into the next.
/// A fixture using this must be <c>[NonParallelizable]</c>: the C# leg redirects
/// <c>Console.Out</c>.</para>
/// </summary>
internal static class FourBackends
{
    internal static string Norm(string s) => (s ?? "").Replace("\r\n", "\n").Trim();

    internal static void RunsOnEveryBackend(string program, string expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program))), Is.EqualTo(expected), "C++");
            Assert.That(Norm(JavaScriptExecutionTests.RunJs(program)), Is.EqualTo(expected), "JavaScript");
            Assert.That(Norm(Msil.MsilHarness.RunExpectingSuccess(program)), Is.EqualTo(expected), "MSIL");
            Assert.That(Norm(RunEmittedCSharp(program)), Is.EqualTo(expected), "C#");
        });
    }

    internal static string RunEmittedCSharp(string program) =>
        RunEmittedCSharpText(ReturnCoercionTests.EmitCSharpForTest(program));

    internal static string RunEmittedCSharpText(string csharp)
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToImmutableArray();

        var compilation = CSharpCompilation.Create(
            "FourBackendsProbe_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(csharp) },
            references,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication));

        using var ms = new MemoryStream();
        var emitted = compilation.Emit(ms);
        Assert.That(emitted.Success, Is.True,
            "the emitted C# does not compile:\n" + string.Join("\n",
                emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()))
            + "\n--- emitted ---\n" + csharp);

        var assembly = Assembly.Load(ms.ToArray());
        var entry = assembly.EntryPoint;
        Assert.That(entry, Is.Not.Null, "no entry point in the emitted program");

        var captured = new StringWriter();
        var original = Console.Out;
        Console.SetOut(captured);
        try
        {
            var args = entry!.GetParameters().Length == 0 ? null : new object[] { Array.Empty<string>() };
            entry.Invoke(null, args);
        }
        catch (TargetInvocationException ex)
        {
            Assert.Fail("the emitted C# threw: " + (ex.InnerException ?? ex) + "\n--- emitted ---\n" + csharp);
        }
        finally
        {
            Console.SetOut(original);
        }
        return captured.ToString();
    }
}
