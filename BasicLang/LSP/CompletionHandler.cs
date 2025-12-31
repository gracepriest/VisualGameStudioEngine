using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace BasicLang.Compiler.LSP
{
    /// <summary>
    /// Handles completion requests (autocomplete)
    /// </summary>
    public class CompletionHandler : CompletionHandlerBase
    {
        private readonly DocumentManager _documentManager;
        private readonly CompletionService _completionService;

        public CompletionHandler(DocumentManager documentManager, CompletionService completionService)
        {
            _documentManager = documentManager;
            _completionService = completionService;
        }

        public override Task<CompletionList> Handle(CompletionParams request, CancellationToken cancellationToken)
        {
            var state = _documentManager.GetDocument(request.TextDocument.Uri);

            var completions = _completionService.GetCompletions(
                state,
                request.Position.Line,
                request.Position.Character);

            return Task.FromResult(new CompletionList(completions));
        }

        public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken cancellationToken)
        {
            // Resolve additional completion item details if needed
            return Task.FromResult(request);
        }

        protected override CompletionRegistrationOptions CreateRegistrationOptions(
            CompletionCapability capability,
            ClientCapabilities clientCapabilities)
        {
            return new CompletionRegistrationOptions
            {
                DocumentSelector = TextDocumentSelector.ForLanguage("basiclang"),
                TriggerCharacters = new Container<string>(".", "(", " "),
                ResolveProvider = true
            };
        }
    }
}
