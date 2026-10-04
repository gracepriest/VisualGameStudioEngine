using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// ⛔ WinForms' run-time truth about a TRANSLUCENT BackColor, measured, and the catalog pinned to it.
///
/// <para><c>Control.BackColor</c>'s setter throws <c>ArgumentException</c> ("Control does not support transparent background
/// colors") for a colour with alpha &lt; 255 unless the control sets <c>ControlStyles.SupportsTransparentBackColor</c> — so
/// <c>BackColor="#80FF0000"</c> (or <c>Transparent</c>) on a TextBox builds green and dies when the form is constructed.
/// This program constructs EVERY catalog kind with a WinForms BackColor row (and the Form itself) and sets a translucent
/// colour on it, by reflection; the kinds that throw must be EXACTLY the kinds whose BackColor row the catalog refuses a
/// translucent value on. A new kind, or a catalog row that drifts, turns this red.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class WinFormsTranslucentBackColorRunTests
{
    private const string Translucent = "#80FF0000";

    private static IEnumerable<(string Kind, string Type, FormPropertyDef Row)> KindsWithABackColor() =>
        FormControlCatalog.All.Append(FormControlCatalog.FormRoot)
            .Where(k => k.SupportsTarget(FormTarget.WinForms))
            .Select(k => (k, Row: k.Properties.FirstOrDefault(p =>
                p.Name == "BackColor" && p.Type == FormPropertyType.Color && p.AppliesTo(FormTarget.WinForms))))
            .Where(x => x.Row != null)
            .Select(x => (x.k.Kind, x.k.WinFormsType!, x.Row!));

    [Test]
    public void EveryKindThatThrowsOnATranslucentBackColor_IsExactlyAKindWhoseCatalogRowRefusesIt()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("WinForms runs only on Windows");
        }

        var kinds = KindsWithABackColor().ToList();
        Assert.That(kinds, Has.Count.GreaterThan(10), "precondition: the catalog's BackColor kinds were found");

        var dir = Path.Combine(Path.GetTempPath(), "bl-translucent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "probe.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net8.0-windows</TargetFramework>
                    <UseWindowsForms>true</UseWindowsForms>
                    <Nullable>disable</Nullable>
                    <AssemblyName>TranslucentProbe</AssemblyName>
                  </PropertyGroup>
                </Project>
                """);

            var table = string.Join(",\n", kinds.Select(k => $"            new[] {{ \"{k.Kind}\", \"{k.Type}\" }}"));
            File.WriteAllText(Path.Combine(dir, "Probe.cs"), $$"""
                using System;
                using System.Drawing;
                using System.Reflection;
                using System.Windows.Forms;

                internal static class Probe
                {
                    [STAThread]
                    private static void Main()
                    {
                        var kinds = new[]
                        {
                {{table}}
                        };
                        foreach (var k in kinds)
                        {
                            var name = k[1].Contains(".") ? k[1] : "System.Windows.Forms." + k[1];
                            var type = typeof(Control).Assembly.GetType(name) ?? Type.GetType(name);
                            if (type == null) { Console.WriteLine(k[0] + "=notype"); continue; }
                            object o;
                            try { o = Activator.CreateInstance(type); }
                            catch (Exception e) { Console.WriteLine(k[0] + "=noctor " + e.GetType().Name); continue; }
                            Console.WriteLine(k[0] + "=" + Set(o, Color.FromArgb(128, 255, 0, 0)) + "," + Set(o, Color.Transparent));
                        }
                        Console.WriteLine("DONE");
                    }

                    private static string Set(object o, Color c)
                    {
                        try
                        {
                            o.GetType().GetProperty("BackColor").SetValue(o, c);
                            return "ok";
                        }
                        catch (TargetInvocationException e) when (e.InnerException is ArgumentException)
                        {
                            return "throw";
                        }
                    }
                }
                """);

            var (buildExit, buildOut, buildErr) = CliTestHarness.RunProcess(
                "dotnet", new[] { "build", "-c", "Release", "--nologo" }, dir, timeoutMs: 300_000);
            Assert.That(buildExit, Is.Zero, $"the probe did not build.\n{buildOut}\n{buildErr}");

            var exe = Directory.GetFiles(dir, "TranslucentProbe.exe", SearchOption.AllDirectories).Single();
            var (runExit, runOut, runErr) = CliTestHarness.RunProcess(exe, Array.Empty<string>(), dir, timeoutMs: 120_000);
            TestContext.WriteLine(runOut);
            Assert.That(runExit, Is.Zero, $"the probe crashed.\n{runOut}\n{runErr}");
            Assert.That(runOut, Does.Contain("DONE"));

            var measured = runOut.Replace("\r\n", "\n").Split('\n')
                .Where(l => l.Contains('=') && !l.StartsWith("DONE", StringComparison.Ordinal))
                .Select(l => l.Split('='))
                .ToDictionary(p => p[0], p => p[1].Trim());

            Assert.Multiple(() =>
            {
                foreach (var (kind, _, row) in kinds)
                {
                    var result = measured.GetValueOrDefault(kind);
                    Assert.That(result, Is.EqualTo("ok,ok").Or.EqualTo("throw,throw"),
                        $"{kind}: constructed and set both ways (a translucent colour and Transparent agree)");
                    var throws = result == "throw,throw";
                    Assert.That(row.Accepts(Translucent, FormTarget.WinForms), Is.EqualTo(!throws),
                        $"{kind}: WinForms {(throws ? "THROWS on" : "accepts")} a translucent BackColor — the catalog must " +
                        (throws ? "refuse" : "accept") + " it");
                    Assert.That(row.Accepts("Transparent", FormTarget.WinForms), Is.EqualTo(!throws),
                        $"{kind}: the named Transparent follows the same rule");
                    Assert.That(row.Accepts(Translucent, FormTarget.Web), Is.True, $"{kind}: the web keeps alpha (rgba)");
                }
            });
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
