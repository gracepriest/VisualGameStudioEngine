using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

[TestFixture]
public class LspClientServiceTests
{
    private LspClientService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _service = new LspClientService();
    }

    [TearDown]
    public void TearDown()
    {
        _service?.Dispose();
    }

    #region State Tests

    [Test]
    public void State_Initially_IsDisconnected()
    {
        Assert.That(_service.State, Is.EqualTo(LspConnectionState.Disconnected));
    }

    [Test]
    public void Capabilities_Initially_IsNull()
    {
        Assert.That(_service.Capabilities, Is.Null);
    }

    [Test]
    public void ServerInfo_Initially_IsNull()
    {
        Assert.That(_service.ServerInfo, Is.Null);
    }

    #endregion

    #region StartServerAsync Tests

    [Test]
    public async Task StartServerAsync_NonExistentPath_ReturnsFalse()
    {
        var result = await _service.StartServerAsync("nonexistent.exe", null, Environment.CurrentDirectory);
        Assert.That(result, Is.False);
        Assert.That(_service.State, Is.EqualTo(LspConnectionState.Error));
    }

    [Test]
    public async Task StartServerAsync_RaisesStateChangedEvent()
    {
        var stateChanges = new List<LspConnectionState>();
        _service.StateChanged += (s, e) => stateChanges.Add(e.NewState);

        await _service.StartServerAsync("nonexistent.exe", null, Environment.CurrentDirectory);

        Assert.That(stateChanges, Does.Contain(LspConnectionState.Connecting));
    }

    #endregion

    #region ConnectAsync Tests

    [Test]
    public async Task ConnectAsync_InvalidHost_ReturnsFalse()
    {
        var result = await _service.ConnectAsync("invalid-host-12345", 9999, Environment.CurrentDirectory);
        Assert.That(result, Is.False);
    }

    #endregion

    #region StopAsync Tests

    [Test]
    public async Task StopAsync_WhenDisconnected_DoesNotThrow()
    {
        await _service.StopAsync();
        Assert.That(_service.State, Is.EqualTo(LspConnectionState.Disconnected));
    }

    #endregion

    #region Dispose Tests

    [Test]
    public void Dispose_MultipleDispose_DoesNotThrow()
    {
        Assert.DoesNotThrow(() =>
        {
            _service.Dispose();
            _service.Dispose();
        });
    }

    #endregion

    #region LSP Types Tests

    [Test]
    public void LspConnectionState_HasExpectedValues()
    {
        var values = Enum.GetValues<LspConnectionState>();
        Assert.That(values, Does.Contain(LspConnectionState.Disconnected));
        Assert.That(values, Does.Contain(LspConnectionState.Connecting));
        Assert.That(values, Does.Contain(LspConnectionState.Initializing));
        Assert.That(values, Does.Contain(LspConnectionState.Ready));
        Assert.That(values, Does.Contain(LspConnectionState.Error));
    }

    [Test]
    public void ServerCapabilities_Defaults_AreCorrect()
    {
        var caps = new ServerCapabilities();
        Assert.That(caps.HoverProvider, Is.False);
        Assert.That(caps.DefinitionProvider, Is.False);
        Assert.That(caps.ReferencesProvider, Is.False);
    }

    [Test]
    public void CompletionList_Defaults_AreCorrect()
    {
        var list = new CompletionList();
        Assert.That(list.IsIncomplete, Is.False);
        Assert.That(list.Items, Is.Empty);
    }

    [Test]
    public void CompletionItem_Defaults_AreCorrect()
    {
        var item = new LspCompletionItem();
        Assert.That(item.Label, Is.Empty);
        Assert.That(item.Kind, Is.EqualTo(default(LspCompletionItemKind)));
        Assert.That(item.Deprecated, Is.False);
    }

    [Test]
    public void LspCompletionItemKind_HasExpectedValues()
    {
        Assert.That((int)LspCompletionItemKind.Text, Is.EqualTo(1));
        Assert.That((int)LspCompletionItemKind.Method, Is.EqualTo(2));
        Assert.That((int)LspCompletionItemKind.Function, Is.EqualTo(3));
        Assert.That((int)LspCompletionItemKind.Class, Is.EqualTo(7));
    }

    [Test]
    public void TextEdit_Defaults_AreCorrect()
    {
        var edit = new LspTextEdit();
        Assert.That(edit.NewText, Is.Empty);
        Assert.That(edit.Range, Is.Not.Null);
    }

    [Test]
    public void LspRange_Defaults_AreCorrect()
    {
        var range = new LspRange();
        Assert.That(range.Start, Is.Not.Null);
        Assert.That(range.End, Is.Not.Null);
    }

    [Test]
    public void LspPosition_Defaults_AreCorrect()
    {
        var pos = new LspPosition();
        Assert.That(pos.Line, Is.EqualTo(0));
        Assert.That(pos.Character, Is.EqualTo(0));
    }

    [Test]
    public void Location_Defaults_AreCorrect()
    {
        var loc = new Location();
        Assert.That(loc.Uri, Is.Empty);
        Assert.That(loc.Range, Is.Not.Null);
    }

    [Test]
    public void Hover_Defaults_AreCorrect()
    {
        var hover = new Hover();
        Assert.That(hover.Contents, Is.Null);
        Assert.That(hover.Range, Is.Null);
    }

    [Test]
    public void MarkupContent_Defaults_AreCorrect()
    {
        var content = new MarkupContent();
        Assert.That(content.Kind, Is.EqualTo("plaintext"));
        Assert.That(content.Value, Is.Empty);
    }

    [Test]
    public void DocumentSymbol_Defaults_AreCorrect()
    {
        var symbol = new LspDocumentSymbol();
        Assert.That(symbol.Name, Is.Empty);
        Assert.That(symbol.Kind, Is.EqualTo(default(LspSymbolKind)));
        Assert.That(symbol.Deprecated, Is.False);
    }

    [Test]
    public void LspSymbolKind_HasExpectedValues()
    {
        Assert.That((int)LspSymbolKind.File, Is.EqualTo(1));
        Assert.That((int)LspSymbolKind.Module, Is.EqualTo(2));
        Assert.That((int)LspSymbolKind.Class, Is.EqualTo(5));
        Assert.That((int)LspSymbolKind.Function, Is.EqualTo(12));
    }

    [Test]
    public void CodeAction_Defaults_AreCorrect()
    {
        var action = new LspCodeAction();
        Assert.That(action.Title, Is.Empty);
        Assert.That(action.IsPreferred, Is.False);
    }

    [Test]
    public void FormattingOptions_Defaults_AreCorrect()
    {
        var options = new LspFormattingOptions();
        Assert.That(options.TabSize, Is.EqualTo(4));
        Assert.That(options.InsertSpaces, Is.True);
    }

    [Test]
    public void SignatureHelp_Defaults_AreCorrect()
    {
        var help = new LspSignatureHelp();
        Assert.That(help.Signatures, Is.Empty);
        Assert.That(help.ActiveSignature, Is.Null);
    }

    [Test]
    public void LspDiagnostic_Defaults_AreCorrect()
    {
        var diag = new LspDiagnostic();
        Assert.That(diag.Message, Is.Empty);
        Assert.That(diag.Severity, Is.EqualTo(default(LspDiagnosticSeverity)));
    }

    [Test]
    public void DiagnosticSeverity_HasCorrectValues()
    {
        Assert.That((int)LspDiagnosticSeverity.Error, Is.EqualTo(1));
        Assert.That((int)LspDiagnosticSeverity.Warning, Is.EqualTo(2));
        Assert.That((int)LspDiagnosticSeverity.Information, Is.EqualTo(3));
        Assert.That((int)LspDiagnosticSeverity.Hint, Is.EqualTo(4));
    }

    #endregion

    #region Event Args Tests

    [Test]
    public void LspStateChangedEventArgs_StoresValues()
    {
        var args = new LspStateChangedEventArgs(LspConnectionState.Disconnected, LspConnectionState.Connecting, "test error");
        Assert.That(args.OldState, Is.EqualTo(LspConnectionState.Disconnected));
        Assert.That(args.NewState, Is.EqualTo(LspConnectionState.Connecting));
        Assert.That(args.Error, Is.EqualTo("test error"));
    }

    [Test]
    public void DiagnosticsEventArgs_StoresValues()
    {
        var diags = new List<LspDiagnostic> { new LspDiagnostic { Message = "test" } };
        var args = new LspDiagnosticsEventArgs("file://test.bl", diags);
        Assert.That(args.Uri, Is.EqualTo("file://test.bl"));
        Assert.That(args.Diagnostics, Has.Count.EqualTo(1));
    }

    [Test]
    public void LogMessageEventArgs_StoresValues()
    {
        var args = new LogMessageEventArgs(MessageType.Info, "test message");
        Assert.That(args.Type, Is.EqualTo(MessageType.Info));
        Assert.That(args.Message, Is.EqualTo("test message"));
    }

    [Test]
    public void ShowMessageEventArgs_StoresValues()
    {
        var args = new ShowMessageEventArgs(MessageType.Warning, "warning message");
        Assert.That(args.Type, Is.EqualTo(MessageType.Warning));
        Assert.That(args.Message, Is.EqualTo("warning message"));
    }

    [Test]
    public void MessageType_HasCorrectValues()
    {
        Assert.That((int)MessageType.Error, Is.EqualTo(1));
        Assert.That((int)MessageType.Warning, Is.EqualTo(2));
        Assert.That((int)MessageType.Info, Is.EqualTo(3));
        Assert.That((int)MessageType.Log, Is.EqualTo(4));
    }

    #endregion

    #region TextDocumentContentChangeEvent Tests

    [Test]
    public void TextDocumentContentChangeEvent_Defaults_AreCorrect()
    {
        var change = new TextDocumentContentChangeEvent();
        Assert.That(change.Text, Is.Empty);
        Assert.That(change.Range, Is.Null);
        Assert.That(change.RangeLength, Is.Null);
    }

    #endregion

    #region LspException Tests

    [Test]
    public void LspException_StoresCodeAndMessage()
    {
        var ex = new LspException(-32600, "Invalid Request");
        Assert.That(ex.Code, Is.EqualTo(-32600));
        Assert.That(ex.Message, Is.EqualTo("Invalid Request"));
    }

    #endregion
}
