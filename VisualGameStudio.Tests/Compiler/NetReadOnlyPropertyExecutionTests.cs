using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BasicLang.Compiler;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #222, taken to the entry points and to a running program. A write to a .NET ReadOnly property (`s.Length = 3`, `l.Count = 5`, `t.Year = 1`, `a.Message = "x"` on an ArgumentException,
//  `Environment.ProcessorCount = 1`, `u.AbsolutePath = "/q"`) is vbc's BC30526 on EVERY target. It was accepted, and each backend then failed in its own way: C# CS0200, MSIL MissingFieldException, C++ did
//  not compile, and JavaScript RAN and printed the old value (2, 1, "m"; an array write could even resize). `NetReadOnlyPropertyDiagnosticsTests` (fast) holds the per-fact-source rows and the controls; this
//  fixture holds what needs a process: the refusal through the spawned CLI, `CompileProjectFiles` and a Release .blproj build, and the ByRef copy-in that was the other half of the fix.
//
//  ⭐ THE BYREF HALF. VB passes a ReadOnly property to a ByRef parameter as a COPY and never writes it back (#209). `Change(l.Count)` prints 1 and `Change(s.Length)` prints 2: the callee's `x = 99` lands in the
//  copy. Before, C# was CS0206, MSIL refused the argument by name and C++ did not compile. They are two rows (a List and a String), each on C#, C++ and MSIL through the CLI, the CLI with `--optimize` and
//  `CompileProjectFiles`. ⚠ JavaScript has no ByRef parameter: it refuses `Change(l.Count)` with BL7002 as it refuses every ByRef (`PropertyByRefCopyOutExecutionTests.JavaScript_StillRefusesTheByRefParameter_BL7002`),
//  so no ByRef probe runs there and none is asserted.
//
//  ORACLE: vbc, via `S/t136/tools/vbv2.py`, over the probe wrapped in a VB Module. Every refusal below is `error BC30526: Property '<name>' is 'ReadOnly'.` and the ByRef rows and the controls were RUN: `1`, `2`,
//  `2 1 1 3` and `m`. The expected text of each row is vbc's output, never a backend's.
//
//  ENTRY POINTS. The refusal row goes through the spawned CLI on all four targets, the CLI with `--optimize`, `BasicCompiler.CompileProjectFiles` and a Release .blproj build (`BasicLang build`).
//  ⚠ `CompileProjectFiles` is called with .NET resolution ARMED (`CompilerOptions.EnableNetResolution()`), as the CLI and the IDE's BuildService arm it: `TempExec.Emit`'s project leg does not, and without a resolver
//  a METADATA fact (`Environment.ProcessorCount`, `Uri.AbsolutePath`) is not there to read. The ByRef rows use `TempExec` as it is: a String, a List and an array are decided by tables, not metadata.
//  ⛔ Every run is HangSafe (its C# leg runs in a child process, #256). A backend whose tool is missing (a C++ compiler, ilasm, Node) SKIPS its cells; the test is ignored only when no cell could run.
//
//  MUTANTS (each the fix plus ONE change, built from a plain source copy and run against a copy of the test output with its BasicLang.dll swapped; the cases that go red, measured):
//    M1 no String row ............................ `string_length` of `EveryFactSource_…` and `AStringsLength_PassedByRef_…` (no copy-in without the fact), plus the fast `AStringsLength_…`, `Lsp_…` and `N1_…`.
//    M2 `Year` not marked ........................ the `datetime_year` row of `EveryFactSource_…` (the other five rows stay green), plus the fast `ADateTimeAndTimeSpanProperty_IsRefused`.
//    M3 no exception inheritance ................. the `argumentexception_message` row of `EveryFactSource_…`, plus the fast `ADotNetExceptionClassesInheritedMember_…` and `ArgumentExceptionMessageWrite_…`.
//    M4 `IsGetOnly` forced false ................. `environment_processorcount` and `uri_absolutepath` of `EveryFactSource_…`, on the CLI, `CompileProjectFiles` and .blproj legs alike (its own cell list names each), plus the
//       fast `AMetadataGetOnlyProperty_…` and `NativeCpp_…`.
//    M5 the ByRef copy-in removed ................ `AListsCount_PassedByRef_…` and `AStringsLength_PassedByRef_…` ONLY (C# CS0206, C++ `no matching function for call to 'Change'`, MSIL: "an expression's value lives
//       in a temporary"); `EveryFactSource_…` and `TheReads_…` stay green.
//    The unmutated plain copy, built the same way: all 35 cases pass. No other case of the 35 went red under any mutant.
//
//  ⛔ KNOWN GAPS — listed, deliberately NOT tested (asserting one would pin a defect):
//    * `With l : .Count = 5 : End With` is refused, but with the WRONG message ("Type 'List' does not have a member 'Count'", which a READ of `.Count` gets too). Pre-existing.
//    * A member whose settability is unknown stays permissive (spec §6.3): an unresolved or Object-degraded receiver, a BasicLang type, any receiver with no resolver armed (the LSP) and a member the hand-built
//      tables have no row for (`DateTimeOffset.Year`). vbc refuses all of them.
//    * The C++ .blproj build prints the code twice ("BC30526: BC30526: ..."): #223, pinned in `PropertyAccessExecutionTests`. No row here builds a C++ project.
//    * `Change(l.Capacity)` and `Change(a.Message)` are refused ("cannot convert from 'Object' to ..."), and `Change(sb.Length)` is still CS0206 on C#: pre-existing, the same before and after #222.
//    * A C++ program that declares an `ArgumentException` does not compile ("use of undeclared identifier"), so the exception read is not run there; and JavaScript has no `New DateTime(...)` (ReferenceError), so no
//      DateTime read is run on it.
// ================================================================================================

/// <summary>#222 — a write to a .NET ReadOnly property is BC30526 on every target and entry point, and a ReadOnly one passed ByRef is copied in and never written back.</summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C++, ilasm, Node and C# child runs share the machine with the spawned CLI
public class NetReadOnlyPropertyExecutionTests
{
    // ============================================================================================
    //  THE REFUSAL — one program per fact source, every target, every entry point
    // ============================================================================================

    /// <summary>One refused write: the declaration, the statement, and the member vbc names.</summary>
    private sealed record Refusal(string Id, string Declaration, string Statement, string Property)
    {
        internal string Source => "Sub Main()\n    " + Declaration + "\n    " + Statement + "\nEnd Sub\n";
        public override string ToString() => Id;
    }

    // One per fact source (see NetReadOnlyPropertyDiagnosticsTests): a table (String, List), a surface row (DateTime), an inherited member (ArgumentException) and resolver metadata (Environment, Uri).
    private static readonly Refusal[] Refusals =
    {
        new("string_length", "Dim s As String = \"ab\"", "s.Length = 3", "Length"),
        new("list_count", "Dim l As New List(Of Integer)", "l.Count = 5", "Count"),
        new("datetime_year", "Dim t As DateTime = New DateTime(2020, 5, 6)", "t.Year = 1", "Year"),
        new("argumentexception_message", "Dim a As New ArgumentException(\"m\")", "a.Message = \"x\"", "Message"),
        new("environment_processorcount", "Dim x As Integer = 0", "Environment.ProcessorCount = 1", "ProcessorCount"),
        new("uri_absolutepath", "Dim u As New Uri(\"http://h/p\")", "u.AbsolutePath = \"/q\"", "AbsolutePath"),
    };

    private static void AssertNamesTheRefusal(string cell, string output, Refusal refusal, List<string> failures)
    {
        if (!output.Contains("BC30526")) failures.Add($"{cell}: no BC30526 in [{FirstLines(output)}]");
        else if (!output.Contains($"Property '{refusal.Property}' is 'ReadOnly'.")) failures.Add($"{cell}: vbc's message for '{refusal.Property}' is missing from [{FirstLines(output)}]");
        if (output.Contains("Compilation successful")) failures.Add($"{cell}: the CLI claimed success");
        if (output.Contains("Unhandled exception")) failures.Add($"{cell}: a stack trace");
    }

    private static string FirstLines(string text) => string.Join(" / ", text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Take(4));

    /// <summary>
    /// ⭐ The headline. Each program is refused with BC30526, vbc's message, through (a) the spawned CLI on C#, C++ (the NATIVE target: its metadata rows also say BL6017, which is not asserted either way),
    /// JavaScript and MSIL, (b) the CLI with `--optimize` on C#, (c) `BasicCompiler.CompileProjectFiles` with .NET resolution armed, on a managed and on the native target, and (d) a Release .blproj
    /// build, for the two METADATA programs (the project's own reference closure is a different resolver than the single-file one). Before the fix every cell compiled, and JavaScript printed the old value.
    /// Mutants: no String row -> `string_length`; Year not marked -> `datetime_year`; no exception inheritance -> `argumentexception_message`; IsGetOnly forced false -> both metadata rows, on every entry point.
    /// </summary>
    [Test]
    public void EveryFactSource_IsRefusedWithBc30526_OnEveryTarget_AndEveryEntryPoint()
    {
        var failures = new List<string>();
        foreach (var refusal in Refusals)
        {
            foreach (var (backend, entry) in new[]
                     {
                         (Bk.CSharp, EntryPoint.Cli), (Bk.Cpp, EntryPoint.Cli), (Bk.JavaScript, EntryPoint.Cli), (Bk.Msil, EntryPoint.Cli),
                         (Bk.CSharp, EntryPoint.CliOptimize),
                     })
            {
                var cell = $"{refusal.Id} CLI {TempExec.TargetName(backend)} {entry}";
                try { AssertNamesTheRefusal(cell, CliRefusalText(backend, entry, refusal.Source), refusal, failures); }
                catch (AssertionException ex) { failures.Add($"{cell}: {ex.Message.Split('\n')[0]}"); }
            }

            foreach (var target in new[] { "csharp", "cpp" })
            {
                var cell = $"{refusal.Id} CompileProjectFiles {target}";
                try { AssertNamesTheRefusal(cell, ProjectRefusalText(target, refusal.Source), refusal, failures); }
                catch (AssertionException ex) { failures.Add($"{cell}: {ex.Message.Split('\n')[0]}"); }
            }
        }

        foreach (var refusal in Refusals.Where(r => r.Id is "environment_processorcount" or "uri_absolutepath"))
        {
            var cell = $"{refusal.Id} Release .blproj CSharp";
            try { AssertNamesTheRefusal(cell, BlprojRefusalText(refusal.Source), refusal, failures); }
            catch (AssertionException ex) { failures.Add($"{cell}: {ex.Message.Split('\n')[0]}"); }
        }

        Assert.That(failures, Is.Empty, "a write to a .NET ReadOnly property must be refused with BC30526 on every target and entry point:\n" + string.Join("\n", failures));
    }

    /// <summary>The spawned CLI refuses the program (non-zero exit); its output is returned. A program that is NOT refused fails the calling test.</summary>
    private static string CliRefusalText(Bk backend, EntryPoint entry, string source)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-t222-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Prog.bas"), source);
            var args = new List<string> { "Prog.bas", "--target=" + TempExec.TargetName(backend) };
            if (entry == EntryPoint.CliOptimize) args.Add("--optimize");
            var (exit, stdout, stderr) = CliTestHarness.RunProcess(CliTestHarness.CliPath(), args.ToArray(), dir, timeoutMs: 120_000);
            Assert.That(exit, Is.Not.Zero, $"CLI --target={TempExec.TargetName(backend)} {entry} must refuse the program:\n{stdout}{stderr}");
            return stdout + stderr;
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
    }

    /// <summary><c>BasicCompiler.CompileProjectFiles</c> with .NET resolution armed (the header says why); the refusal's messages are returned. A program that is NOT refused fails the calling test.</summary>
    private static string ProjectRefusalText(string target, string source)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-t222-proj-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "Main.bas");
            File.WriteAllText(path, source);
            var options = new CompilerOptions { OptimizeAggressive = true, TargetBackend = target };
            options.EnableNetResolution();
            var result = new BasicCompiler(options).CompileProjectFiles(new List<string> { path });
            Assert.That(result.HasErrors, Is.True, $"CompileProjectFiles ({target}) must refuse the program");
            return string.Join("\n", result.AllErrors.Select(e => $"{e.ErrorCode}: {e.Message}"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
    }

    /// <summary>A Release C# .blproj build (<c>BasicLang build App.blproj -c Release</c>) must fail on the program; its output is returned.</summary>
    private static string BlprojRefusalText(string source)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-t222-blproj-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Main.bas"), source);
            File.WriteAllText(Path.Combine(dir, "App.blproj"),
                """
                <?xml version="1.0" encoding="utf-8"?>
                <BasicLangProject Version="1.0">
                  <PropertyGroup>
                    <ProjectName>App</ProjectName>
                    <OutputType>Exe</OutputType>
                    <TargetBackend>CSharp</TargetBackend>
                  </PropertyGroup>
                  <ItemGroup>
                    <Compile Include="Main.bas" />
                  </ItemGroup>
                </BasicLangProject>
                """);
            var (exit, stdout, stderr) = CliTestHarness.RunProcess(CliTestHarness.CliPath(), new[] { "build", Path.Combine(dir, "App.blproj"), "-c", "Release" }, dir, timeoutMs: 180_000);
            Assert.That(exit, Is.Not.Zero, $"a Release .blproj build must refuse the program:\n{stdout}{stderr}");
            return stdout + stderr;
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
    }

    // ============================================================================================
    //  THE BYREF HALF — a ReadOnly .NET property is copied in and never written back
    // ============================================================================================

    private const Bk Three = Bk.CSharp | Bk.Cpp | Bk.Msil;

    private static TempProbe P(string id, string source, string vb, Bk agrees = Bk.All) => new(id, source, vb, agrees, HangSafe: true);

    /// <summary>BRList — `Change(l.Count)`: the callee writes its copy (99), the List still has one element. vbc prints 1.</summary>
    private static readonly TempProbe ByRefListCount = P("byref_list_count", """
        Sub Change(ByRef x As Integer)
            x = 99
        End Sub

        Sub Main()
            Dim l As New List(Of Integer)
            l.Add(7)
            Change(l.Count)
            Console.WriteLine(l.Count)
        End Sub
        """, "1", Three);

    /// <summary>BRStr — `Change(s.Length)`: the string is still two characters long. vbc prints 2.</summary>
    private static readonly TempProbe ByRefStringLength = P("byref_string_length", """
        Sub Change(ByRef x As Integer)
            x = 99
        End Sub

        Sub Main()
            Dim s As String = "ab"
            Change(s.Length)
            Console.WriteLine(s.Length)
        End Sub
        """, "2", Three);

    /// <summary>
    /// One group of single-file probes, each on every backend it runs on, through every entry point. A failing cell is collected, not thrown, so every other one still reports and the failure text names the probe,
    /// the backend and the entry point. (A copy of the helper `PropertyByRefCopyOutExecutionTests` keeps: each fixture owns its own.)
    /// </summary>
    private static void AssertSingleFile(params TempProbe[] probes)
    {
        var failures = new List<string>();
        int ran = 0, skipped = 0;
        foreach (var probe in probes)
        {
            foreach (var backend in TempExec.Backends(probe.Agrees))
            {
                try
                {
                    TempExec.AssertMatchesInEveryEntryPoint(backend, probe.Source, probe.Vb, probe.Id, probe.HangSafe);
                    ran++;
                }
                catch (IgnoreException)
                {
                    skipped++;
                }
                catch (AssertionException ex)
                {
                    ran++;
                    failures.Add(ex.Message);
                }
            }
        }

        Assert.That(failures, Is.Empty, string.Join("\n", failures));
        if (ran == 0) Assert.Ignore($"no execution tool on this machine ({skipped} cells skipped).");
    }

    /// <summary>
    /// A List's Count passed ByRef is a COPY: `Change(l.Count)` prints 1 where the callee wrote 99. Before #222 C# was CS0206, MSIL refused the argument ("an expression's value lives in a temporary") and C++ did
    /// not compile. Mutant "ByRef copy-in removed": the property is passed as storage again and every cell goes red.
    /// </summary>
    [Test]
    public void AListsCount_PassedByRef_IsACopy_NeverWrittenBack()
        => AssertSingleFile(ByRefListCount);

    /// <summary>The same for a String's Length (`Change(s.Length)` prints 2): the other table, and the other mutant ("no String row" turns this red as well as the refusal).</summary>
    [Test]
    public void AStringsLength_PassedByRef_IsACopy_NeverWrittenBack()
        => AssertSingleFile(ByRefStringLength);

    // ============================================================================================
    //  THE CONTROLS, RUN — the reads still compile and print what vbc prints
    // ============================================================================================

    /// <summary>The reads as the argument of a BY-VALUE call (`Console.WriteLine(s.Length)`): the ByRef copy-in must not reach them. vbc prints `2 1 1 3`.</summary>
    private static readonly TempProbe Reads = P("reads", """
        Sub Main()
            Dim s As String = "ab"
            Dim l As New List(Of Integer)
            l.Add(7)
            Dim d As New Dictionary(Of String, Integer)
            d("a") = 1
            Dim a(2) As Integer
            Console.WriteLine(s.Length)
            Console.WriteLine(l.Count)
            Console.WriteLine(d.Count)
            Console.WriteLine(a.Length)
        End Sub
        """, "2\n1\n1\n3");

    /// <summary>The inherited member: an ArgumentException's Message. Not on C++ (the header's last gap). `New DateTime(...)` is not run at all: JavaScript has no DateTime constructor (ReferenceError, pre-existing).</summary>
    private static readonly TempProbe ReadsOfAnExceptionsMessage = P("reads_exception", """
        Sub Main()
            Dim e As New ArgumentException("m")
            Console.WriteLine(e.Message)
        End Sub
        """, "m", Bk.CSharp | Bk.JavaScript | Bk.Msil);

    /// <summary>
    /// The CONTROL that closes the loop: the same members READ (never written) still print vbc's answer on every backend each runs on. The fix makes these members `IsCopyOutPropertyArgument` for a ByRef
    /// parameter; a read passed to `Console.WriteLine` must stay an ordinary by-value argument. JavaScript runs under Node here, which is why this fixture is in the JavaScript execution roster.
    /// </summary>
    [Test]
    public void TheReads_StillPrintVbcsAnswer_OnEveryBackend()
        => AssertSingleFile(Reads, ReadsOfAnExceptionsMessage);
}
