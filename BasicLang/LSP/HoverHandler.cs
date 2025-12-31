using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace BasicLang.Compiler.LSP
{
    /// <summary>
    /// Handles hover requests (documentation on mouse hover)
    /// </summary>
    public class HoverHandler : HoverHandlerBase
    {
        private readonly DocumentManager _documentManager;
        private readonly SymbolService _symbolService;

        public HoverHandler(DocumentManager documentManager, SymbolService symbolService)
        {
            _documentManager = documentManager;
            _symbolService = symbolService;
        }

        public override Task<Hover> Handle(HoverParams request, CancellationToken cancellationToken)
        {
            var state = _documentManager.GetDocument(request.TextDocument.Uri);
            if (state == null)
            {
                return Task.FromResult<Hover>(null);
            }

            // Get the word at the cursor position
            var word = state.GetWordAtPosition(request.Position.Line, request.Position.Character);
            if (string.IsNullOrEmpty(word))
            {
                return Task.FromResult<Hover>(null);
            }

            // Get hover information
            var hoverInfo = _symbolService.GetHoverInfo(state, word);
            if (hoverInfo == null)
            {
                return Task.FromResult<Hover>(null);
            }

            return Task.FromResult(new Hover
            {
                Contents = new MarkedStringsOrMarkupContent(
                    new MarkupContent
                    {
                        Kind = MarkupKind.Markdown,
                        Value = hoverInfo
                    }
                )
            });
        }

        protected override HoverRegistrationOptions CreateRegistrationOptions(
            HoverCapability capability,
            ClientCapabilities clientCapabilities)
        {
            return new HoverRegistrationOptions
            {
                DocumentSelector = TextDocumentSelector.ForLanguage("basiclang")
            };
        }
    }
}
