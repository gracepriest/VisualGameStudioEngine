using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;

namespace BasicLang.Compiler.LSP
{
    /// <summary>
    /// Main entry point for the BasicLang Language Server
    /// </summary>
    public class BasicLangLanguageServer
    {
        private ILanguageServer _server;

        public async Task RunAsync()
        {
            try
            {
                _server = await LanguageServer.From(options =>
                {
                    options
                        .WithInput(Console.OpenStandardInput())
                        .WithOutput(Console.OpenStandardOutput())
                        .ConfigureLogging(lb => lb
                            .SetMinimumLevel(LogLevel.Warning)
                            .AddFilter("OmniSharp", LogLevel.Warning))
                        .WithServices(ConfigureServices)
                        .WithHandler<TextDocumentSyncHandler>()
                        .WithHandler<CompletionHandler>()
                        .WithHandler<HoverHandler>()
                        .WithHandler<DefinitionHandler>()
                        .WithHandler<DocumentSymbolHandler>()
                        .WithHandler<SemanticTokensHandler>()
                        .WithHandler<ReferencesHandler>()
                        .WithHandler<RenameHandler>()
                        .WithHandler<PrepareRenameHandler>()
                        .WithHandler<SignatureHelpHandler>()
                        .WithHandler<CodeActionHandler>()
                        .WithHandler<FormattingHandler>()
                        .WithHandler<RangeFormattingHandler>()
                        .WithHandler<FoldingRangeHandler>()
                        .WithHandler<InlayHintsHandler>()
                        .WithHandler<DocumentLinkHandler>()
                        .WithHandler<CallHierarchyPrepareHandler>()
                        .WithHandler<CallHierarchyIncomingHandler>()
                        .WithHandler<CallHierarchyOutgoingHandler>()
                        .WithHandler<WorkspaceSymbolHandler>()
                        .WithHandler<CodeLensHandler>()
                        .WithHandler<SelectionRangeHandler>()
                        .WithHandler<LinkedEditingRangeHandler>()
                        .WithHandler<TypeHierarchyPrepareHandler>()
                        .WithHandler<TypeHierarchySupertypesHandler>()
                        .WithHandler<TypeHierarchySubtypesHandler>()
                        .WithHandler<ExecuteCommandHandler>()
                        .WithHandler<ImplementationHandler>()
                        .WithHandler<DocumentHighlightHandler>()
                        .OnInitialize(OnInitializeAsync)
                        .OnInitialized(OnInitializedAsync);
                }).ConfigureAwait(false);

                await _server.WaitForExit.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Log to stderr so it doesn't interfere with LSP protocol
                Console.Error.WriteLine($"LSP Server Error: {ex.Message}");
                Console.Error.WriteLine(ex.StackTrace);
            }
        }

        private void ConfigureServices(IServiceCollection services)
        {
            // Register our services
            services.AddSingleton<DocumentManager>();
            services.AddSingleton<DiagnosticsService>();
            services.AddSingleton<CompletionService>();
            services.AddSingleton<SymbolService>();
        }

        private Task OnInitializeAsync(ILanguageServer server, InitializeParams request, System.Threading.CancellationToken token)
        {
            server.Window.LogInfo("BasicLang Language Server initializing...");
            return Task.CompletedTask;
        }

        private Task OnInitializedAsync(ILanguageServer server, InitializeParams request, InitializeResult response, System.Threading.CancellationToken token)
        {
            server.Window.LogInfo("BasicLang Language Server initialized!");
            return Task.CompletedTask;
        }

    }
}
