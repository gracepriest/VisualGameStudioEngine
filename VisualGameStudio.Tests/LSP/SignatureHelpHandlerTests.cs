using System.Linq;
using System.Threading;
using NUnit.Framework;
using BasicLang.Compiler.LSP;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace VisualGameStudio.Tests.LSP;

/// <summary>
/// Tests for the full LSP server's SignatureHelpHandler: user functions
/// declared inside Module blocks, active parameter tracking, and
/// dotted-name fallback (Console.WriteLine).
/// </summary>
[TestFixture]
public class SignatureHelpHandlerTests
{
    private DocumentManager _documentManager = null!;
    private SignatureHelpHandler _handler = null!;
    private DocumentUri _uri = null!;

    // Line 11 (0-based): "        Dim result As Integer = AddNums(x, 2)"
    // Line 12 (0-based): "        Console.WriteLine(result)"
    private const string Source = "Module Main\r\n" +                                        // 0
        "    Function AddNums(a As Integer, b As Integer) As Integer\r\n" +                  // 1
        "        Return a + b\r\n" +                                                         // 2
        "    End Function\r\n" +                                                             // 3
        "\r\n" +                                                                             // 4
        "    Sub Greet(name As String)\r\n" +                                                // 5
        "        Console.WriteLine(name)\r\n" +                                              // 6
        "    End Sub\r\n" +                                                                  // 7
        "\r\n" +                                                                             // 8
        "    Sub Main()\r\n" +                                                               // 9
        "        Dim x As Integer = 5\r\n" +                                                 // 10
        "        Dim result As Integer = AddNums(x, 2)\r\n" +                                // 11
        "        Console.WriteLine(result)\r\n" +                                            // 12
        "        Greet(\"hi\")\r\n" +                                                        // 13
        "    End Sub\r\n" +                                                                  // 14
        "End Module\r\n";                                                                    // 15

    [SetUp]
    public void SetUp()
    {
        _documentManager = new DocumentManager();
        _handler = new SignatureHelpHandler(_documentManager);
        _uri = DocumentUri.From("file:///sighelp-test.bas");
        _documentManager.UpdateDocument(_uri, Source);
    }

    private SignatureHelp? GetSignatureHelp(int line, int character)
    {
        var request = new SignatureHelpParams
        {
            TextDocument = new TextDocumentIdentifier(_uri),
            Position = new Position(line, character)
        };
        return _handler.Handle(request, CancellationToken.None).Result;
    }

    [Test]
    public void SignatureHelp_FunctionInModule_AfterOpenParen_ReturnsSignature()
    {
        // "        Dim result As Integer = AddNums(" - cursor right after '('
        var openParen = "        Dim result As Integer = AddNums(".Length;
        var result = GetSignatureHelp(11, openParen);

        Assert.That(result, Is.Not.Null);
        var signature = result!.Signatures.First();
        Assert.That(signature.Label, Does.Contain("AddNums(a As Integer, b As Integer)"));
        Assert.That(signature.Parameters!.Count(), Is.EqualTo(2));
        Assert.That(result.ActiveParameter, Is.EqualTo(0));
    }

    [Test]
    public void SignatureHelp_FunctionInModule_AfterComma_AdvancesActiveParameter()
    {
        // "        Dim result As Integer = AddNums(x," - cursor right after ','
        var afterComma = "        Dim result As Integer = AddNums(x,".Length;
        var result = GetSignatureHelp(11, afterComma);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ActiveParameter, Is.EqualTo(1));
    }

    [Test]
    public void SignatureHelp_SubroutineInModule_ReturnsSignature()
    {
        // "        Greet(" - cursor right after '('
        var openParen = "        Greet(".Length;
        var result = GetSignatureHelp(13, openParen);

        Assert.That(result, Is.Not.Null);
        var signature = result!.Signatures.First();
        Assert.That(signature.Label, Does.Contain("Greet(name As String)"));
        Assert.That(signature.Parameters!.Count(), Is.EqualTo(1));
    }

    [Test]
    public void SignatureHelp_DottedName_FallsBackToMemberName()
    {
        // "        Console.WriteLine(" - cursor right after '('
        var openParen = "        Console.WriteLine(".Length;
        var result = GetSignatureHelp(12, openParen);

        Assert.That(result, Is.Not.Null);
        var signature = result!.Signatures.First();
        Assert.That(signature.Label, Does.Contain("WriteLine"));
    }

    [Test]
    public void SignatureHelp_NotInsideCall_ReturnsNull()
    {
        var result = GetSignatureHelp(10, 8);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void SignatureHelp_UnknownDocument_ReturnsNull()
    {
        var request = new SignatureHelpParams
        {
            TextDocument = new TextDocumentIdentifier(DocumentUri.From("file:///does-not-exist.bas")),
            Position = new Position(0, 0)
        };
        var result = _handler.Handle(request, CancellationToken.None).Result;

        Assert.That(result, Is.Null);
    }
}
