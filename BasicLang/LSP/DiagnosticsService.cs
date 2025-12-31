using System.Collections.Generic;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace BasicLang.Compiler.LSP
{
    /// <summary>
    /// Service for publishing diagnostics (errors, warnings) to the client
    /// </summary>
    public class DiagnosticsService
    {
        /// <summary>
        /// Publish diagnostics for a document
        /// </summary>
        public void PublishDiagnostics(ILanguageServerFacade server, DocumentState state)
        {
            var diagnostics = new List<OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic>();

            foreach (var diag in state.Diagnostics)
            {
                diagnostics.Add(new OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic
                {
                    Range = new LspRange(
                        new Position(diag.Line - 1, diag.Column - 1),
                        new Position(diag.EndLine > 0 ? diag.EndLine - 1 : diag.Line - 1,
                                    diag.EndColumn > 0 ? diag.EndColumn - 1 : diag.Column + 10)),
                    Severity = MapSeverity(diag.Severity),
                    Source = "basiclang",
                    Message = diag.Message
                });
            }

            server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
            {
                Uri = state.Uri,
                Diagnostics = new Container<OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic>(diagnostics)
            });
        }

        private OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity MapSeverity(DiagnosticSeverity severity)
        {
            return severity switch
            {
                DiagnosticSeverity.Error => OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Error,
                DiagnosticSeverity.Warning => OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Warning,
                DiagnosticSeverity.Information => OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Information,
                DiagnosticSeverity.Hint => OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Hint,
                _ => OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Error
            };
        }
    }
}
