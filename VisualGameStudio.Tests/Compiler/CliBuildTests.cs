using System.Diagnostics;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// End-to-end pins for the CLI build entry point (`BasicLang.exe build`),
/// spawning the real compiler binary deployed next to the tests. The IDE and
/// CLI have separate csproj generation AND separate dotnet-build invocations —
/// bugs hide in whichever path the suite doesn't exercise (lesson learned:
/// test through BOTH entry points).
/// </summary>
[TestFixture]
[NonParallelizable]
public class CliBuildTests
{
    private string _rootDir = null!;

    [SetUp]
    public void SetUp()
    {
        _rootDir = Path.Combine(Path.GetTempPath(), "bl-cli-build-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootDir);
    }

    [TearDown]
    public void TearDown()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (Directory.Exists(_rootDir))
                    Directory.Delete(_rootDir, recursive: true);
                return;
            }
            catch
            {
                Thread.Sleep(200);
            }
        }
    }

    [Test]
    public async Task CliBuild_ProjectNameWithMsBuildSpecialChars_Builds()
    {
        // Regression: a project named ";k;lk;lkl;k;l" failed twice over on the
        // CLI path — the raw name in the generated csproj (MSB4094), and the
        // `-o "<dir with ;>"` argument, which dotnet translates into the
        // OutputPath PROPERTY whose command-line parser splits values on ';'
        // (MSB1006 "Property is not valid"). The output location now lives
        // inside the generated csproj (escaped), matching the IDE pipeline.
        const string name = ";k;lk;lkl;k;l";

        var projectDir = Path.Combine(_rootDir, name);
        Directory.CreateDirectory(projectDir);

        File.WriteAllText(Path.Combine(projectDir, "Main.bas"),
            "Sub Main()\n    PrintLine(\"cli hostile name ok\")\nEnd Sub\n");

        File.WriteAllText(Path.Combine(projectDir, name + ".blproj"),
@"<?xml version=""1.0"" encoding=""utf-8""?>
<BasicLangProject Version=""1.0"">
  <PropertyGroup>
    <ProjectName>" + System.Security.SecurityElement.Escape(name) + @"</ProjectName>
    <OutputType>Exe</OutputType>
    <TargetBackend>CSharp</TargetBackend>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include=""Main.bas"" />
  </ItemGroup>
</BasicLangProject>
");

        var cliPath = Path.Combine(AppContext.BaseDirectory, "BasicLang.exe");
        Assert.That(File.Exists(cliPath), Is.True,
            "BasicLang.exe not deployed next to the tests — project reference output changed?");

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = cliPath,
                ArgumentList = { "build", Path.Combine(projectDir, name + ".blproj") },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromMinutes(5)).Token);

        Assert.That(process.ExitCode, Is.EqualTo(0),
            $"CLI build failed for a hostile project name.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");

        var exePath = Directory.GetFiles(projectDir, name + ".exe", SearchOption.AllDirectories);
        Assert.That(exePath, Is.Not.Empty,
            $"CLI build claimed success but produced no {name}.exe.\nSTDOUT:\n{stdout}");
    }
}
