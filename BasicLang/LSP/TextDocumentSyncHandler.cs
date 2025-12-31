using System.Threading;
using System.Threading.Tasks;
using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;

namespace BasicLang.Compiler.LSP
{
    /// <summary>
    /// Handles text document synchronization (open, change, close, save)
    /// </summary>
    public class TextDocumentSyncHandler : TextDocumentSyncHandlerBase
    {
        private readonly DocumentManager _documentManager;
        private readonly DiagnosticsService _diagnosticsService;
        private readonly ILanguageServerFacade _server;

        public TextDocumentSyncHandler(
            DocumentManager documentManager,
            DiagnosticsService diagnosticsService,
            ILanguageServerFacade server)
        {
            _documentManager = documentManager;
            _diagnosticsService = diagnosticsService;
            _server = server;
        }

        public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri)
        {
            return new TextDocumentAttributes(uri, "basiclang");
        }

        public override Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken cancellationToken)
        {
            var uri = request.TextDocument.Uri;
            var content = request.TextDocument.Text;

            var state = _documentManager.UpdateDocument(uri, content);
            _diagnosticsService.PublishDiagnostics(_server, state);

            return Unit.Task;
        }

        public override Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken cancellationToken)
        {
            var uri = request.TextDocument.Uri;

            // Get the full content from the change events
            // For full sync, there's only one change containing the full content
            foreach (var change in request.ContentChanges)
            {
                var state = _documentManager.UpdateDocument(uri, change.Text);
                _diagnosticsService.PublishDiagnostics(_server, state);
            }

            return Unit.Task;
        }

        public override Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken cancellationToken)
        {
            var uri = request.TextDocument.Uri;
            _documentManager.CloseDocument(uri);

            // Clear diagnostics for closed document
            _server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
            {
                Uri = uri,
                Diagnostics = new Container<OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic>()
            });

            return Unit.Task;
        }

        public override Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken cancellationToken)
        {
            // Re-analyze on save if needed
            var uri = request.TextDocument.Uri;
            var state = _documentManager.GetDocument(uri);

            if (state != null && request.Text != null)
            {
                state = _documentManager.UpdateDocument(uri, request.Text);
                _diagnosticsService.PublishDiagnostics(_server, state);
            }

            return Unit.Task;
        }

        protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(
            TextSynchronizationCapability capability,
            ClientCapabilities clientCapabilities)
        {
            return new TextDocumentSyncRegistrationOptions
            {
                DocumentSelector = TextDocumentSelector.ForLanguage("basiclang"),
                Change = TextDocumentSyncKind.Full,
                Save = new SaveOptions { IncludeText = true }
            };
        }
    }
}
