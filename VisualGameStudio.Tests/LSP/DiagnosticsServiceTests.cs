using NUnit.Framework;
using BasicLang.Compiler.LSP;
using OmniSharp.Extensions.LanguageServer.Protocol;

namespace VisualGameStudio.Tests.LSP;

[TestFixture]
public class DiagnosticTests
{
    [Test]
    public void Diagnostic_DefaultValues()
    {
        var diagnostic = new Diagnostic();

        Assert.That(diagnostic.Message, Is.Null);
        Assert.That(diagnostic.Line, Is.EqualTo(0));
        Assert.That(diagnostic.Column, Is.EqualTo(0));
        Assert.That(diagnostic.EndLine, Is.EqualTo(0));
        Assert.That(diagnostic.EndColumn, Is.EqualTo(0));
    }

    [Test]
    public void Diagnostic_CanSetMessage()
    {
        var diagnostic = new Diagnostic
        {
            Message = "Undefined variable 'x'"
        };

        Assert.That(diagnostic.Message, Is.EqualTo("Undefined variable 'x'"));
    }

    [Test]
    public void Diagnostic_CanSetLineAndColumn()
    {
        var diagnostic = new Diagnostic
        {
            Line = 10,
            Column = 5,
            EndLine = 10,
            EndColumn = 15
        };

        Assert.That(diagnostic.Line, Is.EqualTo(10));
        Assert.That(diagnostic.Column, Is.EqualTo(5));
        Assert.That(diagnostic.EndLine, Is.EqualTo(10));
        Assert.That(diagnostic.EndColumn, Is.EqualTo(15));
    }

    [Test]
    public void Diagnostic_CanSetSeverity_Error()
    {
        var diagnostic = new Diagnostic
        {
            Severity = DiagnosticSeverity.Error
        };

        Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Error));
    }

    [Test]
    public void Diagnostic_CanSetSeverity_Warning()
    {
        var diagnostic = new Diagnostic
        {
            Severity = DiagnosticSeverity.Warning
        };

        Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Warning));
    }

    [Test]
    public void Diagnostic_CanSetSeverity_Information()
    {
        var diagnostic = new Diagnostic
        {
            Severity = DiagnosticSeverity.Information
        };

        Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Information));
    }

    [Test]
    public void Diagnostic_CanSetSeverity_Hint()
    {
        var diagnostic = new Diagnostic
        {
            Severity = DiagnosticSeverity.Hint
        };

        Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Hint));
    }

    [Test]
    public void Diagnostic_CompleteError()
    {
        var diagnostic = new Diagnostic
        {
            Message = "Syntax error: Expected ')'",
            Line = 5,
            Column = 20,
            EndLine = 5,
            EndColumn = 21,
            Severity = DiagnosticSeverity.Error
        };

        Assert.That(diagnostic.Message, Is.EqualTo("Syntax error: Expected ')'"));
        Assert.That(diagnostic.Line, Is.EqualTo(5));
        Assert.That(diagnostic.Column, Is.EqualTo(20));
        Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Error));
    }
}

[TestFixture]
public class DiagnosticSeverityTests
{
    [Test]
    public void DiagnosticSeverity_HasCorrectValues()
    {
        Assert.That((int)DiagnosticSeverity.Error, Is.EqualTo(1));
        Assert.That((int)DiagnosticSeverity.Warning, Is.EqualTo(2));
        Assert.That((int)DiagnosticSeverity.Information, Is.EqualTo(3));
        Assert.That((int)DiagnosticSeverity.Hint, Is.EqualTo(4));
    }

    [Test]
    public void DiagnosticSeverity_ErrorIsHighestPriority()
    {
        Assert.That(DiagnosticSeverity.Error, Is.LessThan(DiagnosticSeverity.Warning));
        Assert.That(DiagnosticSeverity.Warning, Is.LessThan(DiagnosticSeverity.Information));
        Assert.That(DiagnosticSeverity.Information, Is.LessThan(DiagnosticSeverity.Hint));
    }
}

[TestFixture]
public class CachedParseResultTests
{
    [Test]
    public void CachedParseResult_CanBeCreated()
    {
        var cached = new CachedParseResult();

        Assert.That(cached, Is.Not.Null);
    }

    [Test]
    public void CachedParseResult_CanSetTokens()
    {
        var cached = new CachedParseResult
        {
            Tokens = new List<BasicLang.Compiler.Token>()
        };

        Assert.That(cached.Tokens, Is.Not.Null);
        Assert.That(cached.Tokens, Is.Empty);
    }

    [Test]
    public void CachedParseResult_CanSetAST()
    {
        var cached = new CachedParseResult
        {
            AST = null!
        };

        Assert.That(cached.AST, Is.Null);
    }

    [Test]
    public void CachedParseResult_CanSetCachedAt()
    {
        var now = DateTime.Now;
        var cached = new CachedParseResult
        {
            CachedAt = now
        };

        Assert.That(cached.CachedAt, Is.EqualTo(now));
    }
}
