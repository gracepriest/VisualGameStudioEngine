using System.Linq;
using System.Threading;
using BasicLang.Compiler.LSP;
using NUnit.Framework;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace VisualGameStudio.Tests.LSP;

/// <summary>
/// #123: VB's <c>If(cond, a, b)</c> is an expression the call hierarchy must look INTO. <c>CallHierarchyHandler</c>
/// walks expressions with two hand-written switches (incoming and outgoing) that silently skip a node type they do not
/// name, so a call whose only appearance is an operand of <c>If()</c> would vanish from both directions. Every AST
/// visitor is forced to add the new node by the interface; these two switches are not, which is why they are pinned.
/// </summary>
[TestFixture]
public class ConditionalCallHierarchyTests
{
    private const string Source = """
        Function Hit() As Integer
            Return 1
        End Function
        Function Miss() As Integer
            Return 2
        End Function
        Function Other() As Integer
            Return 3
        End Function
        Sub Main()
            Dim t As Boolean = True
            Dim r = If(t, Hit(), Miss())
            Dim q = If(Other() > 0, 1, 2)
        End Sub
        """;

    private static readonly DocumentUri Uri = DocumentUri.From("file:///tmp/t123-conditional-call-hierarchy.bas");

    private static DocumentManager Open()
    {
        var manager = new DocumentManager();
        var state = manager.UpdateDocument(Uri, Source);
        Assert.That(state.ParseSuccessful, Is.True, "the document parses");
        Assert.That(state.SemanticSuccessful, Is.True, "the document analyzes");
        return manager;
    }

    private static CallHierarchyItem Item(string name) => new() { Name = name, Uri = Uri, Kind = SymbolKind.Function };

    /// <summary>The callees of <c>Main</c> include a call in an operand and a call in the CONDITION of an <c>If()</c>.</summary>
    [Test]
    public void OutgoingCalls_SeeCallsInsideTheOperandsAndTheConditionOfAConditional()
    {
        var calls = new CallHierarchyOutgoingHandler(Open())
            .Handle(new CallHierarchyOutgoingCallsParams { Item = Item("Main") }, CancellationToken.None).Result;

        Assert.That(calls, Is.Not.Null);
        Assert.That(calls!.Select(c => c.To.Name), Is.EquivalentTo(new[] { "Hit", "Miss", "Other" }));
    }

    /// <summary>Each of the three is called by <c>Main</c>, and only there.</summary>
    [TestCase("Hit")]
    [TestCase("Miss")]
    [TestCase("Other")]
    public void IncomingCalls_SeeACallerWhoseOnlyCallIsInsideAConditional(string callee)
    {
        var calls = new CallHierarchyIncomingHandler(Open())
            .Handle(new CallHierarchyIncomingCallsParams { Item = Item(callee) }, CancellationToken.None).Result;

        Assert.That(calls, Is.Not.Null, $"{callee} is called from Main inside an If()");
        Assert.That(calls!.Select(c => c.From.Name), Is.EqualTo(new[] { "Main" }));
    }
}
