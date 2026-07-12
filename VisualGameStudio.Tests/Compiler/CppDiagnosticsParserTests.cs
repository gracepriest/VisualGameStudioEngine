using BasicLang.Compiler.ProjectSystem;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

[TestFixture]
public class CppDiagnosticsParserTests
{
    [Test]
    public void Parses_ClangError_WithColumn()
    {
        var output = @"C:\proj\main.cpp:12:5: error: use of undeclared identifier 'foo'";
        var diags = CppDiagnosticsParser.Parse(output, @"C:\proj");
        Assert.That(diags, Has.Count.EqualTo(1));
        Assert.That(diags[0].FilePath, Is.EqualTo(@"C:\proj\main.cpp"));
        Assert.That(diags[0].Line, Is.EqualTo(12));
        Assert.That(diags[0].Column, Is.EqualTo(5));
        Assert.That(diags[0].IsWarning, Is.False);
        Assert.That(diags[0].Code, Is.EqualTo("CPP1001"));
        Assert.That(diags[0].Message, Is.EqualTo("use of undeclared identifier 'foo'"));
    }

    [Test]
    public void Parses_GccWarning_AndFatalError()
    {
        var output = "util.cc:3:10: warning: unused variable 'x' [-Wunused-variable]\n"
                   + "main.cpp:1:10: fatal error: missing.h: No such file or directory";
        var diags = CppDiagnosticsParser.Parse(output, @"C:\proj");
        Assert.That(diags, Has.Count.EqualTo(2));
        Assert.That(diags[0].IsWarning, Is.True);
        Assert.That(diags[0].Code, Is.EqualTo("CPP1002"));
        Assert.That(diags[0].FilePath, Is.EqualTo(Path.Combine(@"C:\proj", "util.cc")), "relative paths resolve against the working dir");
        Assert.That(diags[1].IsWarning, Is.False);
        Assert.That(diags[1].Message, Does.Contain("missing.h"));
    }

    [Test]
    public void Parses_MsvcError_LineOnly_KeepsCompilerCode()
    {
        var output = @"C:\proj\main.cpp(5): error C2065: 'x': undeclared identifier";
        var diags = CppDiagnosticsParser.Parse(output, @"C:\proj");
        Assert.That(diags, Has.Count.EqualTo(1));
        Assert.That(diags[0].Line, Is.EqualTo(5));
        Assert.That(diags[0].Column, Is.EqualTo(0));
        Assert.That(diags[0].Code, Is.EqualTo("C2065"));
    }

    [Test]
    public void Parses_MsvcWarning_WithLineAndColumn()
    {
        var output = @"main.cpp(7,12): warning C4189: 'y': local variable is initialized but not referenced";
        var diags = CppDiagnosticsParser.Parse(output, @"C:\proj");
        Assert.That(diags, Has.Count.EqualTo(1));
        Assert.That(diags[0].IsWarning, Is.True);
        Assert.That(diags[0].Line, Is.EqualTo(7));
        Assert.That(diags[0].Column, Is.EqualTo(12));
    }

    [Test]
    public void Parses_LinkerError_WithoutLine()
    {
        var output = @"main.obj : error LNK2019: unresolved external symbol Framework_Initialize referenced in function main";
        var diags = CppDiagnosticsParser.Parse(output, @"C:\proj");
        Assert.That(diags, Has.Count.EqualTo(1));
        Assert.That(diags[0].Code, Is.EqualTo("LNK2019"));
        Assert.That(diags[0].Line, Is.EqualTo(0));
    }

    [Test]
    public void Ignores_Notes_CaretLines_AndChatter()
    {
        var output = "main.cpp:12:5: error: no matching function for call to 'f'\n"
                   + "main.cpp:3:6: note: candidate function not viable\n"
                   + "    f(1, 2);\n"
                   + "    ^\n"
                   + "1 error generated.\n"
                   + "Microsoft (R) C/C++ Optimizing Compiler";
        var diags = CppDiagnosticsParser.Parse(output, @"C:\proj");
        Assert.That(diags, Has.Count.EqualTo(1), "only the error line is a diagnostic");
    }

    [Test]
    public void EmptyOrNullOutput_ReturnsEmptyList()
    {
        Assert.That(CppDiagnosticsParser.Parse(null, @"C:\proj"), Is.Empty);
        Assert.That(CppDiagnosticsParser.Parse("", @"C:\proj"), Is.Empty);
    }

    [Test]
    public void FormatNormalized_EmitsMsBuildStyle()
    {
        var d = new CppDiagnostic
        {
            FilePath = @"C:\proj\main.cpp", Line = 12, Column = 5,
            IsWarning = false, Code = "CPP1001", Message = "boom"
        };
        Assert.That(CppDiagnosticsParser.FormatNormalized(d),
            Is.EqualTo(@"C:\proj\main.cpp(12,5): error CPP1001: boom"));
    }
}
