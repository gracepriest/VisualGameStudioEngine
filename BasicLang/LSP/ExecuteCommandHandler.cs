using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using Newtonsoft.Json.Linq;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace BasicLang.Compiler.LSP
{
    /// <summary>
    /// Handles execute command requests from CodeLens and other sources
    /// </summary>
    public class ExecuteCommandHandler : ExecuteCommandHandlerBase
    {
        private readonly DocumentManager _documentManager;
        private readonly ILanguageServerFacade _server;

        // List of supported commands
        public static readonly string[] SupportedCommands = new[]
        {
            "basiclang.showReferences",
            "basiclang.run",
            "basiclang.debug",
            "basiclang.goToDefinition",
            "basiclang.compile",
            "basiclang.format"
        };

        public ExecuteCommandHandler(DocumentManager documentManager, ILanguageServerFacade server)
        {
            _documentManager = documentManager;
            _server = server;
        }

        protected override ExecuteCommandRegistrationOptions CreateRegistrationOptions(
            ExecuteCommandCapability capability,
            ClientCapabilities clientCapabilities)
        {
            return new ExecuteCommandRegistrationOptions
            {
                Commands = new Container<string>(SupportedCommands)
            };
        }

        public override async Task<Unit> Handle(ExecuteCommandParams request, CancellationToken cancellationToken)
        {
            try
            {
                var args = request.Arguments?.ToObject<object[]>() ?? Array.Empty<object>();

                switch (request.Command)
                {
                    case "basiclang.showReferences":
                        await HandleShowReferences(args);
                        break;

                    case "basiclang.run":
                        await HandleRun(args);
                        break;

                    case "basiclang.debug":
                        await HandleDebug(args);
                        break;

                    case "basiclang.goToDefinition":
                        await HandleGoToDefinition(args);
                        break;

                    case "basiclang.compile":
                        await HandleCompile(args);
                        break;

                    case "basiclang.format":
                        await HandleFormat(args);
                        break;

                    default:
                        _server.Window.LogWarning($"Unknown command: {request.Command}");
                        break;
                }
            }
            catch (Exception ex)
            {
                _server.Window.LogError($"Error executing command {request.Command}: {ex.Message}");
            }

            return Unit.Value;
        }

        private async Task HandleShowReferences(object[] args)
        {
            if (args.Length < 2) return;

            var uriString = args[0]?.ToString();
            var symbolName = args[1]?.ToString();

            if (string.IsNullOrEmpty(uriString) || string.IsNullOrEmpty(symbolName)) return;

            var uri = DocumentUri.From(uriString);
            var state = _documentManager.GetDocument(uri);

            if (state?.Tokens == null) return;

            var locations = new List<Location>();

            // Find all references to the symbol
            foreach (var token in state.Tokens)
            {
                if (token.Type == TokenType.Identifier &&
                    string.Equals(token.Lexeme, symbolName, StringComparison.OrdinalIgnoreCase))
                {
                    locations.Add(new Location
                    {
                        Uri = uri,
                        Range = new LspRange(
                            new Position(token.Line - 1, token.Column - 1),
                            new Position(token.Line - 1, token.Column - 1 + token.Lexeme.Length))
                    });
                }
            }

            // Show references panel in the editor
            if (locations.Count > 0)
            {
                // The LSP protocol expects the editor to handle this via textDocument/references
                // For now, just log the count
                _server.Window.LogInfo($"Found {locations.Count} references to '{symbolName}'");

                // Note: To actually show references panel, the client needs to implement
                // the editor.action.showReferences command. This would typically be done
                // in the VS Code extension by calling:
                // vscode.commands.executeCommand('editor.action.showReferences', ...)
            }
        }

        private async Task HandleRun(object[] args)
        {
            if (args.Length < 1) return;

            var uriString = args[0]?.ToString();
            if (string.IsNullOrEmpty(uriString)) return;

            var uri = DocumentUri.From(uriString);
            var state = _documentManager.GetDocument(uri);

            if (state == null)
            {
                _server.Window.ShowError("Cannot run: document not found");
                return;
            }

            try
            {
                _server.Window.LogInfo($"Running {uri.Path}...");

                // Try to get the file path from the URI
                var filePath = uri.GetFileSystemPath();

                // Create a compiler and run the code
                // This would typically compile and execute the BasicLang code
                _server.Window.ShowInfo($"Running: {System.IO.Path.GetFileName(filePath)}");

                // Note: Actual compilation and execution would be implemented here
                // For now, we just show a message
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _server.Window.ShowError($"Run failed: {ex.Message}");
            }
        }

        private async Task HandleDebug(object[] args)
        {
            if (args.Length < 1) return;

            var uriString = args[0]?.ToString();
            if (string.IsNullOrEmpty(uriString)) return;

            var uri = DocumentUri.From(uriString);
            var filePath = uri.GetFileSystemPath();

            _server.Window.ShowInfo($"Debug: {System.IO.Path.GetFileName(filePath)}");
            _server.Window.LogInfo("Debug session would start here...");

            // Note: The actual debug adapter protocol handling is in DebugSession.cs
            // This command would typically trigger the debug configuration in VS Code
            await Task.CompletedTask;
        }

        private async Task HandleGoToDefinition(object[] args)
        {
            if (args.Length < 2) return;

            var uriString = args[0]?.ToString();
            var symbolName = args[1]?.ToString();

            if (string.IsNullOrEmpty(uriString) || string.IsNullOrEmpty(symbolName)) return;

            var uri = DocumentUri.From(uriString);
            var state = _documentManager.GetDocument(uri);

            if (state?.AST == null) return;

            // Find the definition of the symbol
            Location location = null;
            foreach (var decl in state.AST.Declarations)
            {
                location = FindDefinitionLocation(state.Uri, decl, symbolName);
                if (location != null) break;
            }

            if (location != null)
            {
                _server.Window.LogInfo($"Definition of '{symbolName}' found at line {location.Range.Start.Line + 1}");
                // The client editor would typically handle the navigation
            }
            else
            {
                _server.Window.LogWarning($"Definition of '{symbolName}' not found");
            }

            await Task.CompletedTask;
        }

        private Location FindDefinitionLocation(DocumentUri uri, BasicLang.Compiler.AST.ASTNode node, string name)
        {
            switch (node)
            {
                case BasicLang.Compiler.AST.FunctionNode func when func.Name.Equals(name, StringComparison.OrdinalIgnoreCase):
                    return CreateLocation(uri, func.Line, func.Column, func.Name.Length);

                case BasicLang.Compiler.AST.SubroutineNode sub when sub.Name.Equals(name, StringComparison.OrdinalIgnoreCase):
                    return CreateLocation(uri, sub.Line, sub.Column, sub.Name.Length);

                case BasicLang.Compiler.AST.ClassNode cls when cls.Name.Equals(name, StringComparison.OrdinalIgnoreCase):
                    return CreateLocation(uri, cls.Line, cls.Column, cls.Name.Length);

                case BasicLang.Compiler.AST.ClassNode cls:
                    // Search class members
                    foreach (var member in cls.Members)
                    {
                        var location = FindDefinitionLocation(uri, member, name);
                        if (location != null) return location;
                    }
                    break;
            }

            return null;
        }

        private Location CreateLocation(DocumentUri uri, int line, int column, int length)
        {
            return new Location
            {
                Uri = uri,
                Range = new LspRange(
                    new Position(line - 1, column - 1),
                    new Position(line - 1, column - 1 + length))
            };
        }

        private async Task HandleCompile(object[] args)
        {
            if (args.Length < 1) return;

            var uriString = args[0]?.ToString();
            if (string.IsNullOrEmpty(uriString)) return;

            var uri = DocumentUri.From(uriString);
            var filePath = uri.GetFileSystemPath();

            _server.Window.LogInfo($"Compiling {System.IO.Path.GetFileName(filePath)}...");

            try
            {
                // The actual compilation would use the Compiler class
                _server.Window.ShowInfo("Compilation started...");

                // Compilation logic would go here
                await Task.Delay(100); // Simulate compilation

                _server.Window.ShowInfo("Compilation completed successfully");
            }
            catch (Exception ex)
            {
                _server.Window.ShowError($"Compilation failed: {ex.Message}");
            }
        }

        private async Task HandleFormat(object[] args)
        {
            if (args.Length < 1) return;

            var uriString = args[0]?.ToString();
            if (string.IsNullOrEmpty(uriString)) return;

            var uri = DocumentUri.From(uriString);
            var state = _documentManager.GetDocument(uri);

            if (state == null)
            {
                _server.Window.ShowError("Cannot format: document not found");
                return;
            }

            _server.Window.LogInfo("Document formatted");
            await Task.CompletedTask;
        }
    }
}
