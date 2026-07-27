using System.Collections;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

[TestFixture]
public class NativeBclFrontEndTests
{
    private static ProgramNode Parse(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        return parser.Parse();
    }

    [Test]
    public void NumericLiteral_CarriesLexemeText()
    {
        var ast = Parse("Module M\n Sub Main()\n Dim d As Double = 1.50\n End Sub\nEnd Module");
        var lit = FindFirstNumericLiteral(ast);
        Assert.That(lit, Is.Not.Null);
        Assert.That(lit!.Text, Is.EqualTo("1.50"), "the literal's source text must survive parsing (scale is lost in the double 1.5)");
    }

    /// <summary>
    /// Small recursive walker over the AST that finds the first LiteralExpressionNode whose
    /// LiteralType is a numeric literal kind. Walks generically via reflection over every
    /// public property so it does not depend on a specific node's exact child shape.
    /// </summary>
    private static LiteralExpressionNode? FindFirstNumericLiteral(ASTNode node)
    {
        if (node is LiteralExpressionNode literal && IsNumericLiteral(literal.LiteralType))
        {
            return literal;
        }

        foreach (var property in node.GetType().GetProperties())
        {
            object? value;
            try
            {
                value = property.GetValue(node);
            }
            catch
            {
                continue;
            }

            if (value is ASTNode childNode)
            {
                var found = FindFirstNumericLiteral(childNode);
                if (found != null)
                {
                    return found;
                }
            }
            else if (value is IEnumerable enumerable and not string)
            {
                foreach (var item in enumerable)
                {
                    if (item is ASTNode childItem)
                    {
                        var found = FindFirstNumericLiteral(childItem);
                        if (found != null)
                        {
                            return found;
                        }
                    }
                }
            }
        }

        return null;
    }

    private static bool IsNumericLiteral(TokenType literalType)
    {
        return literalType is TokenType.IntegerLiteral or TokenType.LongLiteral
            or TokenType.SingleLiteral or TokenType.DoubleLiteral;
    }

    [Test]
    public void Byte_IsUnsigned_SByte_IsSigned_InAnalyzerHelpers()
    {
        Assert.That(new TypeInfo("SByte", TypeKind.Primitive).IsSigned(), Is.True);
        Assert.That(new TypeInfo("SByte", TypeKind.Primitive).IsNumeric(), Is.True);
        Assert.That(new TypeInfo("SByte", TypeKind.Primitive).IsIntegral(), Is.True);
        Assert.That(new TypeInfo("Byte", TypeKind.Primitive).IsUnsigned(), Is.True);
        Assert.That(new TypeInfo("Byte", TypeKind.Primitive).IsSigned(), Is.False);
    }

    [Test]
    [Category("Integration")]
    public void SByte_Arithmetic_PassesAnalysis_And_RunsOnCSharp()
    {
        var src = @"Module M
 Sub Main()
  Dim a As SByte = 5
  Dim b As SByte = -3
  Console.WriteLine(a + b)
 End Sub
End Module";
        Assert.That(CompileRunCSharp(src), Is.EqualTo("2"));
    }

    // ====================================================================
    // C# end-to-end helper (reused by later native-BCL tasks)
    // ====================================================================

    /// <summary>
    /// Compiles the given BasicLang source through the real CLI
    /// (`BasicLang.exe build` on the CSharp backend — the same binary the suite
    /// deploys next to the tests via the project reference, so it always carries
    /// the compiler changes under test), runs the produced executable, and
    /// returns its stdout with line endings normalized to \n and the trailing
    /// newline trimmed. Callers must be tagged [Category("Integration")].
    /// </summary>
    private static string CompileRunCSharp(string src)
    {
        if (!DotnetOnPath())
        {
            Assert.Ignore("dotnet SDK not found on PATH — the CLI's C# backend cannot build the generated project.");
        }

        var rootDir = Path.Combine(Path.GetTempPath(), "bl-nativebcl-e2e-" + Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(rootDir, "App");
        Directory.CreateDirectory(projectDir);
        try
        {
            File.WriteAllText(Path.Combine(projectDir, "Main.bas"), src);
            File.WriteAllText(Path.Combine(projectDir, "App.blproj"),
@"<?xml version=""1.0"" encoding=""utf-8""?>
<BasicLangProject Version=""1.0"">
  <PropertyGroup>
    <ProjectName>App</ProjectName>
    <OutputType>Exe</OutputType>
    <TargetBackend>CSharp</TargetBackend>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include=""Main.bas"" />
  </ItemGroup>
</BasicLangProject>
");

            // Build via the CLI. 120s: a first dotnet build in a fresh temp dir
            // includes restore, which can exceed 60s on a cold cache.
            var (buildExit, buildOut, buildErr) = RunProcess(
                CliTestHarness.CliPath(),
                new[] { "build", Path.Combine(projectDir, "App.blproj") },
                projectDir,
                timeoutMs: 120_000);
            Assert.That(buildExit, Is.EqualTo(0),
                $"CLI C# build failed.\nSTDOUT:\n{buildOut}\nSTDERR:\n{buildErr}");

            var exes = Directory.GetFiles(projectDir, "App.exe", SearchOption.AllDirectories);
            Assert.That(exes, Is.Not.Empty,
                $"CLI build claimed success but produced no App.exe.\nSTDOUT:\n{buildOut}");

            var (runExit, runOut, runErr) = RunProcess(
                exes[0], Array.Empty<string>(), Path.GetDirectoryName(exes[0])!, timeoutMs: 60_000);
            Assert.That(runExit, Is.EqualTo(0),
                $"compiled program exited nonzero ({runExit}).\nSTDOUT:\n{runOut}\nSTDERR:\n{runErr}");

            return runOut.Replace("\r\n", "\n").TrimEnd('\n');
        }
        finally
        {
            try { Directory.Delete(rootDir, recursive: true); } catch { /* best-effort temp cleanup */ }
        }
    }

    /// <summary>
    /// Spawns a process with redirected output, a hard timeout, and kill-tree on
    /// hang (BasicLang.exe spawns dotnet build — a timed-out compile must not
    /// leak child processes).
    /// </summary>
    private static (int ExitCode, string StdOut, string StdErr) RunProcess(
        string fileName, string[] args, string workingDir, int timeoutMs)
    {
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = workingDir,
            }
        };
        foreach (var a in args) process.StartInfo.ArgumentList.Add(a);
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeoutMs))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            Assert.Fail($"process timed out after {timeoutMs / 1000}s: {fileName} {string.Join(" ", args)}");
        }
        return (process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }

    private static bool DotnetOnPath()
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
        return paths.Any(p => !string.IsNullOrWhiteSpace(p) &&
            (File.Exists(Path.Combine(p.Trim(), "dotnet.exe")) || File.Exists(Path.Combine(p.Trim(), "dotnet"))));
    }
}
