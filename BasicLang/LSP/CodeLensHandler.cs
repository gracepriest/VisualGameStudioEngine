using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BasicLang.Compiler.AST;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Newtonsoft.Json.Linq;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace BasicLang.Compiler.LSP
{
    /// <summary>
    /// Handles code lens requests (showing reference counts, run/debug buttons)
    /// </summary>
    public class CodeLensHandler : CodeLensHandlerBase
    {
        private readonly DocumentManager _documentManager;

        public CodeLensHandler(DocumentManager documentManager)
        {
            _documentManager = documentManager;
        }

        protected override CodeLensRegistrationOptions CreateRegistrationOptions(
            CodeLensCapability capability,
            ClientCapabilities clientCapabilities)
        {
            return new CodeLensRegistrationOptions
            {
                DocumentSelector = TextDocumentSelector.ForLanguage("basiclang"),
                ResolveProvider = false
            };
        }

        public override Task<CodeLensContainer?> Handle(CodeLensParams request, CancellationToken cancellationToken)
        {
            var state = _documentManager.GetDocument(request.TextDocument.Uri);
            if (state == null || state.AST == null)
            {
                return Task.FromResult(new CodeLensContainer());
            }

            var codeLenses = new List<CodeLens>();

            // Add code lenses for functions and subs
            foreach (var declaration in state.AST.Declarations)
            {
                AddCodeLensesForNode(state, declaration, codeLenses);
            }

            return Task.FromResult(new CodeLensContainer(codeLenses));
        }

        public override Task<CodeLens> Handle(CodeLens request, CancellationToken cancellationToken)
        {
            // Resolve code lens if needed
            return Task.FromResult(request);
        }

        private void AddCodeLensesForNode(DocumentState state, ASTNode node, List<CodeLens> codeLenses)
        {
            switch (node)
            {
                case FunctionNode func:
                    AddFunctionCodeLens(state, func, codeLenses);
                    break;

                case SubroutineNode sub:
                    AddSubroutineCodeLens(state, sub, codeLenses);
                    break;

                case ClassNode cls:
                    AddClassCodeLens(state, cls, codeLenses);
                    // Add code lenses for class members
                    foreach (var member in cls.Members)
                    {
                        AddCodeLensesForNode(state, member, codeLenses);
                    }
                    break;

                case ModuleNode module:
                    // Add code lenses for module members
                    foreach (var member in module.Members)
                    {
                        AddCodeLensesForNode(state, member, codeLenses);
                    }
                    break;
            }
        }

        private void AddFunctionCodeLens(DocumentState state, FunctionNode func, List<CodeLens> codeLenses)
        {
            var range = new LspRange(
                new Position(func.Line - 1, 0),
                new Position(func.Line - 1, 100));

            // Count references to this function
            int refCount = CountReferences(state, func.Name);

            // Add reference count lens
            codeLenses.Add(new CodeLens
            {
                Range = range,
                Command = new Command
                {
                    Title = refCount == 1 ? "1 reference" : $"{refCount} references",
                    Name = "basiclang.showReferences",
                    Arguments = JArray.FromObject(new object[] { state.Uri.ToString(), func.Name })
                }
            });

            // Add "Run" code lens for Main function
            if (func.Name.Equals("Main", StringComparison.OrdinalIgnoreCase))
            {
                codeLenses.Add(new CodeLens
                {
                    Range = range,
                    Command = new Command
                    {
                        Title = "Run",
                        Name = "basiclang.run",
                        Arguments = JArray.FromObject(new object[] { state.Uri.ToString() })
                    }
                });

                codeLenses.Add(new CodeLens
                {
                    Range = range,
                    Command = new Command
                    {
                        Title = "Debug",
                        Name = "basiclang.debug",
                        Arguments = JArray.FromObject(new object[] { state.Uri.ToString() })
                    }
                });
            }
        }

        private void AddSubroutineCodeLens(DocumentState state, SubroutineNode sub, List<CodeLens> codeLenses)
        {
            var range = new LspRange(
                new Position(sub.Line - 1, 0),
                new Position(sub.Line - 1, 100));

            // Count references to this subroutine
            int refCount = CountReferences(state, sub.Name);

            // Add reference count lens
            codeLenses.Add(new CodeLens
            {
                Range = range,
                Command = new Command
                {
                    Title = refCount == 1 ? "1 reference" : $"{refCount} references",
                    Name = "basiclang.showReferences",
                    Arguments = JArray.FromObject(new object[] { state.Uri.ToString(), sub.Name })
                }
            });

            // Add "Run" code lens for Main subroutine
            if (sub.Name.Equals("Main", StringComparison.OrdinalIgnoreCase))
            {
                codeLenses.Add(new CodeLens
                {
                    Range = range,
                    Command = new Command
                    {
                        Title = "Run",
                        Name = "basiclang.run",
                        Arguments = JArray.FromObject(new object[] { state.Uri.ToString() })
                    }
                });

                codeLenses.Add(new CodeLens
                {
                    Range = range,
                    Command = new Command
                    {
                        Title = "Debug",
                        Name = "basiclang.debug",
                        Arguments = JArray.FromObject(new object[] { state.Uri.ToString() })
                    }
                });
            }
        }

        private void AddClassCodeLens(DocumentState state, ClassNode cls, List<CodeLens> codeLenses)
        {
            var range = new LspRange(
                new Position(cls.Line - 1, 0),
                new Position(cls.Line - 1, 100));

            // Count references to this class
            int refCount = CountReferences(state, cls.Name);

            // Add reference count lens
            codeLenses.Add(new CodeLens
            {
                Range = range,
                Command = new Command
                {
                    Title = refCount == 1 ? "1 reference" : $"{refCount} references",
                    Name = "basiclang.showReferences",
                    Arguments = JArray.FromObject(new object[] { state.Uri.ToString(), cls.Name })
                }
            });

            // Show inheritance info
            if (!string.IsNullOrEmpty(cls.BaseClass))
            {
                codeLenses.Add(new CodeLens
                {
                    Range = range,
                    Command = new Command
                    {
                        Title = $"Inherits {cls.BaseClass}",
                        Name = "basiclang.goToDefinition",
                        Arguments = JArray.FromObject(new object[] { state.Uri.ToString(), cls.BaseClass })
                    }
                });
            }
        }

        private int CountReferences(DocumentState state, string name)
        {
            if (state?.Tokens == null || string.IsNullOrEmpty(name))
                return 0;

            int count = 0;

            foreach (var token in state.Tokens)
            {
                if (token.Type == TokenType.Identifier &&
                    string.Equals(token.Lexeme, name, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            }

            // Subtract 1 for the declaration itself
            return Math.Max(0, count - 1);
        }
    }
}
