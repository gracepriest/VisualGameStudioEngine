using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace BasicLang.Compiler.LSP
{
    /// <summary>
    /// Handles go-to-definition requests
    /// </summary>
    public class DefinitionHandler : DefinitionHandlerBase
    {
        private readonly DocumentManager _documentManager;
        private readonly SymbolService _symbolService;

        public DefinitionHandler(DocumentManager documentManager, SymbolService symbolService)
        {
            _documentManager = documentManager;
            _symbolService = symbolService;
        }

        public override Task<LocationOrLocationLinks> Handle(DefinitionParams request, CancellationToken cancellationToken)
        {
            var state = _documentManager.GetDocument(request.TextDocument.Uri);
            if (state == null)
            {
                return Task.FromResult<LocationOrLocationLinks>(null);
            }

            // Get the word at the cursor position
            var word = state.GetWordAtPosition(request.Position.Line, request.Position.Character);
            if (string.IsNullOrEmpty(word))
            {
                return Task.FromResult<LocationOrLocationLinks>(null);
            }

            // Find definition
            var location = _symbolService.FindDefinition(state, word);
            if (location == null)
            {
                return Task.FromResult<LocationOrLocationLinks>(null);
            }

            return Task.FromResult<LocationOrLocationLinks>(new LocationOrLocationLinks(location));
        }

        protected override DefinitionRegistrationOptions CreateRegistrationOptions(
            DefinitionCapability capability,
            ClientCapabilities clientCapabilities)
        {
            return new DefinitionRegistrationOptions
            {
                DocumentSelector = TextDocumentSelector.ForLanguage("basiclang")
            };
        }
    }
}
