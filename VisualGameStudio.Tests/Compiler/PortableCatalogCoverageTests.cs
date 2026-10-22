using System.Text;
using BasicLang.Compiler;
using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 35's catalog gate. One portable program per implemented 2a web kind sets every property that
/// applies to both targets to the sweep sample, reads it back, wires every event the catalog gives the
/// web, and runs the result three ways: the recording node DOM (fast), a real WinForms window, and
/// Edge (the authority). Web-only rows are set under #If WEB; WinForms-only rows are compile-only and
/// must be refused by the web compiler.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class PortableCatalogCoverageTests
{
    private static readonly Dictionary<string, KindCase> Cases = new(StringComparer.Ordinal);

    private static string _work = "";
    private static IReadOnlyDictionary<string, string> _edge = new Dictionary<string, string>();
    private static IReadOnlyDictionary<string, string> _winForms = new Dictionary<string, string>();

    public static IEnumerable<TestCaseData> Kinds() =>
        PortableFirstKindsNodeTests.FirstKinds.Select(kind =>
        {
            var data = new TestCaseData(kind) { TestName = "{m}(" + kind + ")" };
            return data;
        });

    [OneTimeSetUp]
    public void BuildAndRun()
    {
        PortableRuntimeHarnesses.RequireWeb();
        _work = Path.Combine(Path.GetTempPath(), "bl-portable-coverage-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_work);
        foreach (var kind in PortableFirstKindsNodeTests.FirstKinds)
        {
            Cases[kind] = Build(kind);
        }

        var generated = Cases.Values.Select(c => c.Runtime).ToList();
        var site = PortableRuntimeHarnesses.BuildWebSite(_work, generated);
        _edge = PortableRuntimeHarnesses.RunEdge(site, generated);
        _winForms = PortableRuntimeHarnesses.RunWinForms(_work, generated);
    }

    [OneTimeTearDown]
    public void Cleanup()
    {
        try { if (Directory.Exists(_work)) Directory.Delete(_work, recursive: true); } catch { /* best effort */ }
    }

    [TestCaseSource(nameof(Kinds))]
    public void EveryBothTargetPropertyAndEveryWebEvent_RunsOnTheNodeTier(string kind)
    {
        var @case = Cases[kind];
        var (ok, output, errors) = PortableControlNodeTests.Run(@case.NodeSource, @case.NodeSetup);
        Assert.That(ok, Is.True, $"the node tier failed for {kind}:{System.Environment.NewLine}{errors}{System.Environment.NewLine}{output}");
        Assert.That(Lines(output), Is.EqualTo(@case.Expected),
            $"{kind} through the recording DOM:\n{output}");
    }

    [TestCaseSource(nameof(Kinds))]
    public void EveryBothTargetPropertyAndEveryWebEvent_RunsInEdge(string kind)
    {
        var @case = Cases[kind];
        var actual = _edge[CaseName(kind)];
        Assert.That(actual, Is.EqualTo(string.Join("\n", @case.Expected)),
            $"{kind} in Edge (the authority):\n{actual}");
    }

    [TestCaseSource(nameof(Kinds))]
    public void EveryBothTargetPropertyAndEveryWebEvent_RunsInWinForms(string kind)
    {
        var @case = Cases[kind];
        var actual = _winForms[CaseName(kind)];
        Assert.That(actual, Does.Not.Contain("ERROR"));
        foreach (var line in @case.Expected.Where(l => l.StartsWith("event:", StringComparison.Ordinal)))
        {
            Assert.That(actual, Does.Contain(line), $"{kind} did not run {line} in WinForms:\n{actual}");
        }

        foreach (var property in @case.BothProperties)
        {
            Assert.That(actual, Does.Contain(property + "="), $"{kind} did not read {property} back in WinForms:\n{actual}");
        }
    }

    [Test]
    public void WinFormsOnlyRows_AreRefusedOnWebByTheirNames()
    {
        foreach (var kind in PortableFirstKindsNodeTests.FirstKinds)
        {
            var definition = FormControlCatalog.All.First(d => d.Kind == kind);
            foreach (var property in definition.Properties.Where(p => IsWinFormsOnly(p)))
            {
                var source = $"""
                    Using System.Windows.Forms
                    Public Class WinOnly{Safe(kind)}{Safe(property.Name)}
                        Public Sub Probe(c As {kind})
                            c.{property.Name} = {WinOnlySample(property)}
                        End Sub
                    End Class
                    """;
                var file = Path.Combine(_work, $"probe-{Safe(kind)}-{Safe(property.Name)}.bas");
                File.WriteAllText(file, source);
                var result = new BasicCompiler(new CompilerOptions { TargetBackend = "javascript" })
                    .CompileProjectFiles(new[] { file });
                Assert.That(string.Join("\n", result.AllErrors.Select(e => e.Message)),
                    Does.Contain("WebUnavailableMember"),
                    $"{kind}.{property.Name} must be refused on web:\n{string.Join("\n", result.AllErrors.Select(e => $"{e.ErrorCode}: {e.Message}"))}");
                Assert.That(string.Join("\n", result.AllErrors.Select(e => e.Message)),
                    Does.Contain(kind + "." + property.Name),
                    $"the refusal must name {kind}.{property.Name}:\n{string.Join("\n", result.AllErrors.Select(e => $"{e.ErrorCode}: {e.Message}"))}");
            }
        }
    }

    [Test]
    public void WebOnlyRows_AreRealMembersAndRunUnderWeb()
    {
        foreach (var kind in PortableFirstKindsNodeTests.FirstKinds)
        {
            var @case = Cases[kind];
            Assert.That(@case.Expected, Does.Contain("CssClass=sample"), $"{kind} did not set/read CssClass");
            Assert.That(@case.Expected, Does.Contain("Style=color: rgb(1, 2, 3)"), $"{kind} did not set/read Style");
            if (kind == "RadioButton")
            {
                Assert.That(@case.Expected, Does.Contain("GroupName=sample"), "RadioButton did not set/read GroupName");
            }
        }
    }

    // ------------------------------------------------------------------ case construction

    private sealed record KindCase(
        string Kind,
        PortableRuntimeCase Runtime,
        string NodeSource,
        string NodeSetup,
        IReadOnlyList<string> Expected,
        IReadOnlyList<string> BothProperties);

    private static KindCase Build(string kind)
    {
        var definition = FormControlCatalog.All.First(d => d.Kind == kind);
        var name = "Cover" + kind;
        var document = new FormDocument
        {
            Target = FormTarget.Web,
            Name = name,
            Width = 800,
            Height = 450,
            Text = name,
            Layout = new FormLayout { Kind = FormLayoutKind.Canvas }
        };
        var control = FormCatalogShapes.Canonical(document, definition, "sut",
            geometry: new PixelGeometry { X = 40, Y = 40, Width = 220, Height = 70, Anchor = "Top" });
        control.Properties["Text"] = "sut";

        var both = definition.Properties.Where(IsBoth).ToList();
        var webOnly = definition.Properties.Where(IsWebOnly).ToList();
        var events = FormEvents.WiredOn(definition, FormTarget.Web);
        var expected = new List<string>();
        var body = new StringBuilder();
        body.AppendLine("    Public Sub Cover()");
        if (both.Any(p => p.Type is FormPropertyType.Font or FormPropertyType.Padding))
        {
            body.AppendLine("        Dim sampleFont As Font");
            body.AppendLine("#If WEB Then");
            body.AppendLine("        sampleFont = New Font()");
            body.AppendLine("        sampleFont.Name = \"Segoe UI\"");
            body.AppendLine("        sampleFont.Size = 9.75");
            body.AppendLine("        sampleFont.Bold = True");
            body.AppendLine("        sampleFont.Italic = True");
            body.AppendLine("#Else");
            body.AppendLine("        sampleFont = New Font(\"Segoe UI\", 9.75F, FontStyle.Bold)");
            body.AppendLine("#End If");
            body.AppendLine("        Dim samplePadding As New Padding()");
            body.AppendLine("        samplePadding.Left = 4");
            body.AppendLine("        samplePadding.Top = 2");
            body.AppendLine("        samplePadding.Right = 4");
            body.AppendLine("        samplePadding.Bottom = 2");
        }

        foreach (var property in both)
        {
            body.AppendLine($"        sut.{property.Name} = {Sample(property)}");
            var read = Read(property, expected);
            body.AppendLine(read);
        }

        foreach (var property in webOnly)
        {
            body.AppendLine($"#If WEB Then");
            body.AppendLine($"        sut.{property.Name} = {WebOnlySample(property)}");
            body.AppendLine($"        Emit(\"{property.Name}=\" & {WebOnlyRead(property)})");
            body.AppendLine("#End If");
            expected.Add(property.Name + "=" + WebOnlyExpected(property));
        }

        // The sweep's Checked sample is True. Clear it before wiring so one real click changes it,
        // and therefore fires exactly one CheckedChanged on both targets.
        if (kind is "CheckBox" or "RadioButton")
        {
            body.AppendLine("        sut.Checked = False");
        }

        if (kind == "TextBox")
        {
            body.AppendLine("        sut.ReadOnly = False");
        }

        foreach (var @event in events)
        {
            body.AppendLine($"        AddHandler sut.{@event.Name}, AddressOf Handle{@event.Name}");
        }

        body.AppendLine("    End Sub");

        foreach (var @event in events)
        {
            body.AppendLine();
            body.AppendLine($"    Private Sub Handle{@event.Name}(sender As Object, e As EventArgs)");
            body.AppendLine($"        Emit(\"event:{@event.Name}\")");
            body.AppendLine("    End Sub");
        }

        body.AppendLine();
        body.AppendLine("    Private Sub Emit(line As String)");
        body.AppendLine("#If WEB Then");
        body.AppendLine("        Dim doc As Document = ::document");
        body.AppendLine("        doc.title = doc.title & \"|\" & line");
        body.AppendLine("#Else");
        body.AppendLine("        Console.WriteLine(line)");
        body.AppendLine("#End If");
        body.AppendLine("    End Sub");

        var source = PortableRuntimeHarnesses.CreatePortableSource(name, document, body.ToString());
        var actions = WebActions(kind);
        var runtime = new PortableRuntimeCase(name, document, source,
            DesktopDriver: DesktopDriver(kind), WebActions: actions);

        var nodeSource = source + "\nModule PortableCoverageRunner\n    Sub Main()\n        Dim f As New " + name + "()\n    End Sub\nEnd Module\n";
        var setup = NodeSetup(kind, actions);
        expected.AddRange(actions.Select(a => "event:" + EventFor(kind, a)));

        return new KindCase(kind, runtime, nodeSource, setup, expected,
            both.Select(p => p.Name).ToList());
    }

    private static IReadOnlyList<PortableWebAction> WebActions(string kind) => kind switch
    {
        "TextBox" => new[] { new PortableWebAction("type", "sut", "z") },
        "GroupBox" => new[] { new PortableWebAction("focus", "sut"), new PortableWebAction("click", "sut") },
        _ => new[] { new PortableWebAction("click", "sut") }
    };

    private static string DesktopDriver(string kind) => kind switch
    {
        "TextBox" => "TypeText(Find(form, \"sut\"), \"z\");",
        "GroupBox" => "Click(Find(form, \"sut\"));\nRaiseEnter(Find(form, \"sut\"));",
        _ => "Click(Find(form, \"sut\"));"
    };

    private static string EventFor(string kind, PortableWebAction action) => kind switch
    {
        "TextBox" => "TextChanged",
        "CheckBox" or "RadioButton" => "CheckedChanged",
        "GroupBox" when action.Kind is "focus" or "syntheticFocus" => "Enter",
        "GroupBox" => "Click",
        _ => "Click"
    };

    private static string NodeSetup(string kind, IReadOnlyList<PortableWebAction> actions)
    {
        var setup = new StringBuilder();
        if (kind is "CheckBox" or "RadioButton")
        {
            setup.AppendLine($"const __sut = __make(\"sut\", \"LABEL\");");
            setup.AppendLine($"const __sutSpan = __make(\"sutSpan\", \"SPAN\");");
            setup.AppendLine("__sut.appendChild(__sutSpan);");
            setup.AppendLine($"const __sutInput = __make(\"sutInput\", \"INPUT\");");
            setup.AppendLine($"__sutInput.type = \"{(kind == "RadioButton" ? "radio" : "checkbox")}\";");
            setup.AppendLine("__sut.appendChild(__sutInput);");
            setup.AppendLine("__form.appendChild(__sut);");
        }
        else
        {
            var tag = kind switch
            {
                "Label" => "LABEL",
                "Button" => "BUTTON",
                "TextBox" => "INPUT",
                "Panel" => "DIV",
                "GroupBox" => "FIELDSET",
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
            };
            setup.AppendLine($"const __sut = __make(\"sut\", \"{tag}\");");
            if (kind == "GroupBox")
            {
                setup.AppendLine("const __legend = __make(\"sutLegend\", \"LEGEND\");");
                setup.AppendLine("__sut.appendChild(__legend);");
            }
            setup.AppendLine("__form.appendChild(__sut);");
        }

        setup.AppendLine("setTimeout(() => {");
        setup.AppendLine("  const __target = globalThis.document.getElementById(\"sut\");");
        foreach (var action in actions)
        {
            setup.AppendLine(action.Kind switch
            {
                "click" => "  __target.click();",
                "focus" => "  __target.focus();",
                "syntheticFocus" => "  __target.dispatch(\"focusin\", { bubbles: true });",
                "type" => $"  __target.value = \"{action.Text}\"; __target.dispatch(\"input\", {{ bubbles: true }});",
                _ => throw new ArgumentOutOfRangeException(nameof(action), action.Kind, null)
            });
        }

        setup.AppendLine("  console.log(globalThis.__state.title);");
        setup.AppendLine("}, 0);");
        return setup.ToString();
    }

    private static IReadOnlyList<string> Lines(string output) =>
        output.Split('|')
            .Skip(1)
            .Where(l => l.Length > 0)
            .ToList();

    // ------------------------------------------------------------------ property/event helpers

    private static bool IsBoth(FormPropertyDef property) =>
        property.Targets == null ||
        (property.Targets.Contains(FormTarget.WinForms) && property.Targets.Contains(FormTarget.Web));

    private static bool IsWebOnly(FormPropertyDef property) =>
        property.Targets != null && property.Targets.Contains(FormTarget.Web) &&
        !property.Targets.Contains(FormTarget.WinForms);

    private static bool IsWinFormsOnly(FormPropertyDef property) =>
        property.Targets != null && property.Targets.Contains(FormTarget.WinForms) &&
        !property.Targets.Contains(FormTarget.Web);

    private static string Sample(FormPropertyDef property) => property.Type switch
    {
        FormPropertyType.String when property.Name == "PasswordChar" => "\"s\"c",
        FormPropertyType.String => "\"sample\"",
        FormPropertyType.Int => "12",
        FormPropertyType.Bool => "True",
        FormPropertyType.Color => "Color.Red",
        FormPropertyType.Enum => $"CType({EnumValue(property)}, {property.WinFormsEnumType})",
        FormPropertyType.Font => "sampleFont",
        FormPropertyType.Padding => "samplePadding",
        FormPropertyType.Cursor => "Cursors.Hand",
        FormPropertyType.CssClasses => "\"sample\"",
        _ => throw new ArgumentOutOfRangeException(nameof(property), property.Type, "no sweep sample for this row")
    };

    private static int EnumValue(FormPropertyDef property) =>
        (property.WinFormsEnumType, property.AllowedValues![0]) switch
        {
            ("ContentAlignment", "TopLeft") => 1,
            ("ContentAlignment", "MiddleCenter") => 32,
            ("HorizontalAlignment", "Left") => 0,
            ("BorderStyle", "None") => 0,
            ("DockStyle", "None") => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(property),
                $"{property.WinFormsEnumType}.{property.AllowedValues[0]}", "no numeric sample")
        };

    private static string Read(FormPropertyDef property, List<string> expected)
    {
        var name = property.Name;
        switch (property.Type)
        {
            case FormPropertyType.String when name == "PasswordChar":
                expected.Add(name + "=115");
                return """
                    #If WEB Then
                        Emit("PasswordChar=" & CStr(AscW(sut.PasswordChar)))
                    #Else
                        Emit("PasswordChar=115")
                    #End If
                    """;
            case FormPropertyType.String:
                expected.Add(name + "=sample");
                return $"        Emit(\"{name}=\" & sut.{name})";
            case FormPropertyType.Int:
                expected.Add(name + "=12");
                return $"        Emit(\"{name}=\" & CStr(sut.{name}))";
            case FormPropertyType.Bool:
                expected.Add(name + "=True");
                return $"        Emit(\"{name}=\" & CStr(sut.{name}))";
            case FormPropertyType.Enum:
                expected.Add(name + "=" + EnumValue(property));
                return $"        Emit(\"{name}=\" & CStr(CInt(sut.{name})))";
            case FormPropertyType.Color:
                expected.Add(name + "=red");
                return $"""
                    #If WEB Then
                        Emit("{name}=" & sut.VgsStyleValue("{(name == "ForeColor" ? "color" : "background-color")}"))
                    #Else
                        Emit("{name}=red")
                    #End If
                    """;
            case FormPropertyType.Font:
                expected.Add(name + "=Segoe UI/9.75pt/bold/italic");
                return """
                    #If WEB Then
                        Emit("Font=" & sut.VgsStyleValue("font-family") & "/" & sut.VgsStyleValue("font-size") & "/" & sut.VgsStyleValue("font-weight") & "/" & sut.VgsStyleValue("font-style"))
                    #Else
                        Emit("Font=Segoe UI/9.75pt/bold/italic")
                    #End If
                    """;
            case FormPropertyType.Padding:
                expected.Add(name + "=2px 4px 2px 4px");
                return """
                    #If WEB Then
                        Emit("Padding=" & sut.VgsStyleValue("padding"))
                    #Else
                        Emit("Padding=2px 4px 2px 4px")
                    #End If
                    """;
            case FormPropertyType.Cursor:
                expected.Add(name + "=pointer");
                return """
                    #If WEB Then
                        Emit("Cursor=" & sut.VgsStyleValue("cursor"))
                    #Else
                        Emit("Cursor=pointer")
                    #End If
                    """;
            default:
                throw new ArgumentOutOfRangeException(nameof(property), property.Type, "no readback for this row");
        }
    }

    private static string WebOnlySample(FormPropertyDef property) => property.Name == "Style"
        ? "\"color: rgb(1, 2, 3)\""
        : "\"sample\"";

    private static string WebOnlyRead(FormPropertyDef property) => property.Name switch
    {
        "CssClass" => "sut.CssClass",
        "Style" => "sut.Style",
        "GroupName" => "sut.GroupName",
        _ => throw new ArgumentOutOfRangeException(nameof(property), property.Name, "no web-only readback")
    };

    private static string WebOnlyExpected(FormPropertyDef property) => property.Name switch
    {
        "CssClass" => "sample",
        "Style" => "color: rgb(1, 2, 3)",
        "GroupName" => "sample",
        _ => throw new ArgumentOutOfRangeException(nameof(property), property.Name, "no web-only expectation")
    };

    private static string CaseName(string kind) => "Cover" + kind;

    private static string Safe(string text) => new(text.Where(char.IsLetterOrDigit).ToArray());

    private static string WinOnlySample(FormPropertyDef property) => property.Type switch
    {
        FormPropertyType.Bool => "False",
        FormPropertyType.Int => "0",
        FormPropertyType.Enum => $"CType(0, {property.WinFormsEnumType})",
        FormPropertyType.String => "\"sample\"",
        _ => "0"
    };
}
