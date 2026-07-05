using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
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

            // VS Code contract: the FULL candidate set for the context is
            // returned (the client filters in place), so the list is complete
            return Task.FromResult(new CompletionList(completions, isIncomplete: false));
        }

        public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken cancellationToken)
        {
            // completionItem/resolve: attach documentation lazily from the
            // type+member stashed in CompletionItem.Data at list-creation time
            try
            {
                if (request.Documentation == null && request.Data is JObject data)
                {
                    var typeName = data["type"]?.ToString();
                    var memberName = data["member"]?.ToString();

                    if (!string.IsNullOrEmpty(typeName) && !string.IsNullOrEmpty(memberName))
                    {
                        var netType = _documentManager.TypeRegistry?.GetType(typeName);
                        var member = netType?.Members?.FirstOrDefault(m =>
                            m.Name.Equals(memberName, StringComparison.OrdinalIgnoreCase));

                        if (member != null)
                        {
                            request = request with
                            {
                                Documentation = CompletionService.BuildMemberDocumentation(member, typeName),
                                Detail = request.Detail ?? member.GetSignature()
                            };
                        }
                    }
                }
            }
            catch
            {
                // Resolution is best-effort — echo the item back unchanged
            }

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
