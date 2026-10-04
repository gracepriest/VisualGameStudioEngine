using System.IO.Compression;
using System.Text.RegularExpressions;
using BasicLang.Forms;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Events;
using VisualGameStudio.Shell.ViewModels.Documents;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// ⛔ Slice 4 Task 11 — RUN, not compile (spec §8 "the image copy on both targets"). A form with a PictureBox
/// (<c>Image="Resources/logo.png"</c>, a real 3×2 PNG) and a Form <c>Icon="Resources/app.ico"</c> (a real 16×16 .ico),
/// built through the REAL designer (document view model, grid rows, SaveAsync), compiled by the real CLI <c>build</c>
/// (the project route, so the build copy runs), and RUN:
/// <list type="bullet">
/// <item>WinForms — the live form prints <c>pic.Image</c>'s size and <c>Me.Icon.Width</c>, in all THREE launch shapes, each
/// from a working directory that is NOT the output folder (pre-flight M8, the D-5b kill): the exe directly (the IDE's
/// ExecutablePath), <c>dotnet App.dll</c>, and <c>BasicLang.exe run</c> (prefers the .dll, inherits the caller's cwd).</item>
/// <item>Web — the files exist under the site folder at exactly the paths the page names, and the page's script runs.</item>
/// </list>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class FormImageAcceptanceTests
{
    private string _dir = "";

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-imgaccept-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string P(string relative) => Path.Combine(_dir, relative.Replace('/', Path.DirectorySeparatorChar));

    // ==================================================================
    // Real image files, built byte by byte (no imaging library in the test project)
    // ==================================================================

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    private static uint Crc(IEnumerable<byte> bytes) =>
        bytes.Aggregate(0xFFFFFFFFu, (c, b) => CrcTable[(c ^ b) & 0xFF] ^ (c >> 8)) ^ 0xFFFFFFFFu;

    private static byte[] BigEndian(uint v) => new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };

    /// <summary>A valid RGB PNG of <paramref name="width"/>×<paramref name="height"/> (red), with real CRCs and zlib.</summary>
    internal static byte[] Png(int width, int height)
    {
        byte[] Chunk(string type, byte[] data)
        {
            var typed = System.Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
            return BigEndian((uint)data.Length).Concat(typed).Concat(BigEndian(Crc(typed))).ToArray();
        }

        var header = BigEndian((uint)width).Concat(BigEndian((uint)height)).Concat(new byte[] { 8, 2, 0, 0, 0 }).ToArray();
        var raw = new List<byte>();
        for (var y = 0; y < height; y++)
        {
            raw.Add(0); // filter: none
            for (var x = 0; x < width; x++) raw.AddRange(new byte[] { 255, 0, 0 });
        }

        using var packed = new MemoryStream();
        using (var z = new ZLibStream(packed, CompressionLevel.Optimal, leaveOpen: true))
        {
            z.Write(raw.ToArray());
        }

        return new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }
            .Concat(Chunk("IHDR", header)).Concat(Chunk("IDAT", packed.ToArray())).Concat(Chunk("IEND", Array.Empty<byte>()))
            .ToArray();
    }

    /// <summary>A classic BMP-based 16×16, 32-bit .ico — one image, a BITMAPINFOHEADER, the pixels and the AND mask.</summary>
    internal static byte[] Ico16()
    {
        const int size = 16;
        var pixels = size * size * 4;
        var mask = size * 4; // 16 bits per row, padded to 32
        var image = 40 + pixels + mask;
        using var s = new MemoryStream();
        using var w = new BinaryWriter(s);
        w.Write((short)0); w.Write((short)1); w.Write((short)1); // ICONDIR
        w.Write((byte)size); w.Write((byte)size); w.Write((byte)0); w.Write((byte)0); // ICONDIRENTRY
        w.Write((short)1); w.Write((short)32); w.Write(image); w.Write(6 + 16);
        w.Write(40); w.Write(size); w.Write(size * 2); w.Write((short)1); w.Write((short)32); // BITMAPINFOHEADER
        w.Write(0); w.Write(pixels + mask); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
        for (var i = 0; i < size * size; i++) { w.Write((byte)0); w.Write((byte)0); w.Write((byte)255); w.Write((byte)255); }
        w.Write(new byte[mask]);
        return s.ToArray();
    }

    // ==================================================================
    // The designer half
    // ==================================================================

    /// <summary>A new form, a PictureBox, its Image and the Form's Icon set through the real grid rows, saved. Returns the picture's id.</summary>
    private async Task<string> DesignAsync(FormTarget target)
    {
        var scaffold = FormScaffolder.Create("ImageForm", target, FormLayoutKind.Grid);
        File.WriteAllText(P(scaffold.DocumentFileName), scaffold.DocumentText);
        File.WriteAllText(P(scaffold.CodeFileName), scaffold.CodeText);
        Directory.CreateDirectory(P("Resources"));
        File.WriteAllBytes(P("Resources/logo.png"), Png(3, 2));
        File.WriteAllBytes(P("Resources/app.ico"), Ico16());

        var vm = new CodeEditorDocumentViewModel(new FormDesignerAcceptanceTests.DiskFiles(), new Mock<IEventAggregator>().Object)
        {
            FilePath = P(scaffold.DocumentFileName)
        };
        vm.SetContent(scaffold.DocumentText);
        Assert.That(vm.PlaceControl("PictureBox", 16, 16), Is.Null, "placing the PictureBox");
        var pic = vm.DesignDocument!.Controls.Single();

        vm.Selection.Set(pic);
        vm.PropertyGrid.Rows.Single(r => r.Name == "Image").ApplyAsset("Resources/logo.png");
        vm.Selection.Clear(); // the Form's own rows
        vm.PropertyGrid.Rows.Single(r => r.Name == "Icon").ApplyAsset("Resources/app.ico");
        Assert.That(await vm.SaveAsync(), Is.True, "the save failed");

        var document = File.ReadAllText(P(scaffold.DocumentFileName));
        Assert.That(document, Does.Contain("Image=\"Resources/logo.png\"").And.Contain("Icon=\"Resources/app.ico\""),
            "precondition: the designer wrote both");
        return pic.Id;
    }

    private (int Exit, string Out) Run(string file, string[] args, string workingDirectory)
    {
        var (exit, stdout, stderr) = CliTestHarness.RunProcess(file, args, workingDirectory, timeoutMs: 300_000);
        return (exit, stdout + "\n" + stderr);
    }

    [Test]
    public async Task WinForms_TheImageAndTheIcon_LoadInAllThreeLaunchShapes_FromAnotherWorkingDirectory()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("a WinForms window can only be run on Windows");
        }

        if (!CliTestHarness.DotnetOnPath())
        {
            Assert.Ignore("the dotnet SDK is not on PATH");
        }

        var pic = await DesignAsync(FormTarget.WinForms);

        // The user's code: report what the LIVE form holds.
        var code = File.ReadAllText(P("ImageForm.bas"));
        var end = code.LastIndexOf("End Class", StringComparison.Ordinal);
        Assert.That(end, Is.GreaterThan(0), "precondition: the code-behind is a class");
        File.WriteAllText(P("ImageForm.bas"), code[..end] +
            "    Public Sub Report()\n" +
            $"        Console.WriteLine(\"IMAGE \" & {pic}.Image.Width & \",\" & {pic}.Image.Height & \" ICON \" & Me.Icon.Width)\n" +
            "    End Sub\n" + code[end..]);
        File.WriteAllText(P("Main.bas"), "Sub Main()\n    Dim f As New ImageForm()\n    f.Report()\nEnd Sub\n");
        File.WriteAllText(P("ImgApp.blproj"), """
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>ImgApp</ProjectName>
                <OutputType>Exe</OutputType>
                <TargetBackend>CSharp</TargetBackend>
                <TargetFramework>net8.0-windows</TargetFramework>
                <UseWindowsForms>true</UseWindowsForms>
              </PropertyGroup>
            </BasicLangProject>
            """);

        var (buildExit, buildOut) = Run(CliTestHarness.CliPath(), new[] { "build", P("ImgApp.blproj") }, _dir);
        Assert.That(buildExit, Is.Zero, $"the real CLI build failed.\n{buildOut}");
        Assert.That(buildOut, Does.Not.Contain("BL8036"), "both files exist, so nothing was left uncopied");

        var outDir = P("bin/Debug/net8.0-windows");
        var exe = Path.Combine(outDir, "ImgApp.exe");
        var dll = Path.Combine(outDir, "ImgApp.dll");
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(outDir, "Resources", "logo.png")), Is.True, "the build copied the image beside the exe");
            Assert.That(File.Exists(Path.Combine(outDir, "Resources", "app.ico")), Is.True, "…and the icon");
            Assert.That(File.Exists(exe), Is.True, buildOut);
        });

        // ⛔ Every shape from a directory that is NOT the output folder: a bare relative path would resolve there and fail.
        var elsewhere = P("elsewhere");
        Directory.CreateDirectory(elsewhere);
        var shapes = new[]
        {
            ("exe directly", Run(exe, Array.Empty<string>(), elsewhere)),
            ("dotnet App.dll", Run("dotnet", new[] { dll }, elsewhere)),
            ("BasicLang.exe run", Run(CliTestHarness.CliPath(), new[] { "run", P("ImgApp.blproj") }, elsewhere))
        };

        Assert.Multiple(() =>
        {
            foreach (var (shape, (exit, output)) in shapes)
            {
                TestContext.WriteLine($"[{shape}] exit {exit}\n{output}");
                Assert.That(output, Does.Contain("IMAGE 3,2 ICON 16"), $"{shape}: the live form's image and icon\n{output}");
                Assert.That(output, Does.Not.Contain("FileNotFoundException"), shape);
                Assert.That(exit, Is.Zero, shape);
            }
        });
    }

    [Test]
    public async Task Web_TheFilesSitWhereThePageNamesThem_AndThePageRuns()
    {
        await DesignAsync(FormTarget.Web);
        File.WriteAllText(P("Main.bas"), "Sub Main()\n    Console.WriteLine(\"App loaded\")\nEnd Sub\n");
        File.WriteAllText(P("ImgApp.blproj"), """
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>ImgApp</ProjectName>
                <OutputType>Exe</OutputType>
                <TargetBackend>JavaScript</TargetBackend>
                <StartupForm>ImageForm</StartupForm>
              </PropertyGroup>
            </BasicLangProject>
            """);

        var (exit, output) = Run(CliTestHarness.CliPath(), new[] { "build", P("ImgApp.blproj") }, _dir);
        Assert.That(exit, Is.Zero, output);

        var site = P("bin/Debug/net8.0");
        var html = File.ReadAllText(Path.Combine(site, "ImageForm.html"));
        var src = Regex.Match(html, "<img[^>]*src=\"([^\"]+)\"").Groups[1].Value;
        var icon = Regex.Match(html, "<link rel=\"icon\" href=\"([^\"]+)\"").Groups[1].Value;

        Assert.Multiple(() =>
        {
            Assert.That(src, Is.EqualTo("Resources/logo.png"), html);
            Assert.That(icon, Is.EqualTo("Resources/app.ico"), html);
            Assert.That(File.ReadAllBytes(Path.Combine(site, Uri.UnescapeDataString(src))), Is.EqualTo(Png(3, 2)),
                "the image sits where the page names it");
            Assert.That(File.Exists(Path.Combine(site, Uri.UnescapeDataString(icon))), Is.True, "…and the icon");
        });

        var ran = FormDesignerAcceptanceTests.RunPageUnderNode(site, formName: "ImageForm");
        if (ran == null)
        {
            Assert.Ignore("node is not on PATH, so the emitted page cannot be executed here");
        }

        Assert.That(ran, Does.Not.Contain("ReferenceError").And.Not.Contain("LOAD ERROR").And.Contain("App loaded"), ran);
    }
}
