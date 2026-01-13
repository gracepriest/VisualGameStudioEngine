using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace BasicLang.Compiler.LSP
{
    /// <summary>
    /// Handles document symbol requests (outline view)
    /// </summary>
    public class DocumentSymbolHandler : DocumentSymbolHandlerBase
    {
        private readonly DocumentManager _documentManager;
        private readonly SymbolService _symbolService;

        public DocumentSymbolHandler(DocumentManager documentManager, SymbolService symbolService)
        {
            _documentManager = documentManager;
            _symbolService = symbolService;
        }

        public override Task<SymbolInformationOrDocumentSymbolContainer?> Handle(
            DocumentSymbolParams request,
            CancellationToken cancellationToken)
        {
            var state = _documentManager.GetDocument(request.TextDocument.Uri);
            if (state == null)
            {
                return Task.FromResult<SymbolInformationOrDocumentSymbolContainer>(null);
            }

            var symbols = _symbolService.GetDocumentSymbols(state);

            var container = new SymbolInformationOrDocumentSymbolContainer(
                symbols.Select(s => new SymbolInformationOrDocumentSymbol(s)));

            return Task.FromResult(container);
        }

        protected override DocumentSymbolRegistrationOptions CreateRegistrationOptions(
            DocumentSymbolCapability capability,
            ClientCapabilities clientCapabilities)
        {
            return new DocumentSymbolRegistrationOptions
            {
                DocumentSelector = TextDocumentSelector.ForLanguage("basiclang")
            };
        }
    }
}
