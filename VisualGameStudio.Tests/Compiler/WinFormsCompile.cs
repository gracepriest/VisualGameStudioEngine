using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Compiles generated WinForms C# with Roslyn and reports what <c>csc</c> says about it.
///
/// <para>⛔⛔ <b>This is the type system the designer's WinForms catalog does not otherwise have.</b>
/// <c>EnableNetResolution</c> returns early for <c>UseWindowsForms</c> (<c>Compiler.cs:145</c>), the
/// resolver closure cannot reach <c>System.Windows.Forms.dll</c>, and <c>CommonNetTypes</c> carries
/// no WinForms names — so every <c>Form</c>/<c>Button</c>/<c>Point</c> member access types as
/// <c>Object</c> with no diagnostic and <b>a misspelled property name compiles green</b>. Anything
/// that claims to validate the catalog without reaching csc is claiming nothing.</para>
///
/// <para><b>Compile only — never run.</b> The reference assemblies are metadata, so they bind on any
/// OS; the implementation is Windows-only and this deliberately does not try to load it. That is not
/// a limitation to work around: every defect this exists to catch (a property a control does not
/// have, a struct return assigned through, a delegate shape that does not match) is a COMPILE
/// error, and none of them needs the program to start.</para>
/// </summary>
internal static class WinFormsCompile
{
    /// <summary>
    /// Every reference a generated WinForms file needs: the running runtime's assemblies for the
    /// base library, plus the WindowsDesktop reference pack for the desktop surface.
    ///
    /// <para>⛔ Three assemblies ship in BOTH — <c>System.Drawing</c>, <c>WindowsBase</c> and
    /// <c>Microsoft.VisualBasic</c> — and the DESKTOP copy must win, exactly as it does under the
    /// real WindowsDesktop SDK. Passing both gives Roslyn two assemblies with the same simple name;
    /// whichever it picks is then luck, and the failure it produces (a type "defined in an assembly
    /// that is not referenced") points at the innocent one.</para>
    /// </summary>
    private static readonly Lazy<List<MetadataReference>> References = new(() =>
    {
        var desktopDir = DesktopRefDirectory();

        var desktop = Directory.GetFiles(desktopDir, "*.dll");
        var desktopNames = desktop
            .Select(Path.GetFileNameWithoutExtension)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var baseAssemblies = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Where(p => !desktopNames.Contains(Path.GetFileNameWithoutExtension(p)));

        return baseAssemblies.Concat(desktop)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();
    });

    /// <summary>
    /// Where the WindowsDesktop reference pack is, as MSBuild resolved it at build time.
    ///
    /// <para>⚠ Fails rather than skipping. The pack is a <c>PackageReference</c>, so restore has
    /// already guaranteed it; if it is absent, something is wrong with the build and saying so is
    /// the only useful response. A gate that turns itself off when its tooling goes missing reports
    /// success on exactly the runs that prove nothing.</para>
    /// </summary>
    private static string DesktopRefDirectory()
    {
        var recorded = typeof(WinFormsCompile).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "WindowsDesktopRefPath")?.Value;

        Assert.That(recorded, Is.Not.Null.And.Not.Empty,
            "the test assembly carries no WindowsDesktopRefPath — the AssemblyMetadata item in " +
            "VisualGameStudio.Tests.csproj was removed or the package path property did not resolve.");

        Assert.That(Directory.Exists(recorded!), Is.True,
            $"the WindowsDesktop reference pack is not at '{recorded}'. It is a PackageReference, " +
            "so restore should have placed it there — do not skip this gate, fix the restore.");

        return recorded!;
    }

    /// <summary>Every csc ERROR in <paramref name="source"/>, empty when it compiles.</summary>
    public static IReadOnlyList<string> Errors(string source)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "WinFormsGate_" + Guid.NewGuid().ToString("N"),
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source) },
            references: References.Value,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // ⛔ GetDiagnostics(), not Emit(). Emit adds a PE-writing step that can fail for reasons
        // unrelated to the source, and every defect this gate exists to catch is already visible
        // from binding.
        return compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToList();
    }

    /// <summary>Asserts the source compiles, quoting csc and the source when it does not.</summary>
    public static void AssertCompiles(string source, string because)
    {
        var errors = Errors(source);
        Assert.That(errors, Is.Empty,
            $"{because}\ncsc rejected the generated WinForms C#:\n  {string.Join("\n  ", errors)}\n" +
            $"--- generated source ---\n{Numbered(source)}");
    }

    private static string Numbered(string source) =>
        string.Join("\n", source.Replace("\r\n", "\n").Split('\n')
            .Select((line, i) => $"{i + 1,4} | {line}"));
}
