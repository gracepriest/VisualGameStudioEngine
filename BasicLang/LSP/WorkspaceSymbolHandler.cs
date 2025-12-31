using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BasicLang.Compiler.AST;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace BasicLang.Compiler.LSP
{
    /// <summary>
    /// Handles workspace symbol requests (find symbols across all documents)
    /// </summary>
    public class WorkspaceSymbolHandler : WorkspaceSymbolsHandlerBase
    {
        private readonly DocumentManager _documentManager;

        public WorkspaceSymbolHandler(DocumentManager documentManager)
        {
            _documentManager = documentManager;
        }

        public override Task<Container<WorkspaceSymbol>?> Handle(
            WorkspaceSymbolParams request,
            CancellationToken cancellationToken)
        {
            var query = request.Query ?? string.Empty;
            var symbols = new List<WorkspaceSymbol>();

            // Search through all open documents
            foreach (var document in _documentManager.GetAllDocuments())
            {
                if (document?.AST == null)
                    continue;

                // Search through all declarations in the document
                foreach (var declaration in document.AST.Declarations)
                {
                    var documentSymbols = GetSymbolsFromDeclaration(document, declaration, query);
                    symbols.AddRange(documentSymbols);
                }
            }

            return Task.FromResult<Container<WorkspaceSymbol>?>(new Container<WorkspaceSymbol>(symbols));
        }

        private List<WorkspaceSymbol> GetSymbolsFromDeclaration(
            DocumentState document,
            ASTNode node,
            string query)
        {
            var symbols = new List<WorkspaceSymbol>();

            switch (node)
            {
                case FunctionNode func:
                    if (MatchesQuery(func.Name, query))
                    {
                        symbols.Add(new WorkspaceSymbol
                        {
                            Name = func.Name,
                            Kind = SymbolKind.Function,
                            Location = new Location
                            {
                                Uri = document.Uri,
                                Range = CreateRange(func.Line, func.Column, func.Name.Length)
                            },
                            ContainerName = null
                        });
                    }

                    // Add parameters as symbols
                    foreach (var param in func.Parameters)
                    {
                        if (MatchesQuery(param.Name, query))
                        {
                            symbols.Add(new WorkspaceSymbol
                            {
                                Name = param.Name,
                                Kind = SymbolKind.Variable,
                                Location = new Location
                                {
                                    Uri = document.Uri,
                                    Range = CreateRange(param.Line, param.Column, param.Name.Length)
                                },
                                ContainerName = func.Name
                            });
                        }
                    }
                    break;

                case SubroutineNode sub:
                    if (MatchesQuery(sub.Name, query))
                    {
                        symbols.Add(new WorkspaceSymbol
                        {
                            Name = sub.Name,
                            Kind = SymbolKind.Method,
                            Location = new Location
                            {
                                Uri = document.Uri,
                                Range = CreateRange(sub.Line, sub.Column, sub.Name.Length)
                            },
                            ContainerName = null
                        });
                    }

                    // Add parameters as symbols
                    foreach (var param in sub.Parameters)
                    {
                        if (MatchesQuery(param.Name, query))
                        {
                            symbols.Add(new WorkspaceSymbol
                            {
                                Name = param.Name,
                                Kind = SymbolKind.Variable,
                                Location = new Location
                                {
                                    Uri = document.Uri,
                                    Range = CreateRange(param.Line, param.Column, param.Name.Length)
                                },
                                ContainerName = sub.Name
                            });
                        }
                    }
                    break;

                case ClassNode cls:
                    if (MatchesQuery(cls.Name, query))
                    {
                        symbols.Add(new WorkspaceSymbol
                        {
                            Name = cls.Name,
                            Kind = SymbolKind.Class,
                            Location = new Location
                            {
                                Uri = document.Uri,
                                Range = CreateRange(cls.Line, cls.Column, cls.Name.Length)
                            },
                            ContainerName = null
                        });
                    }

                    // Search through class members
                    foreach (var member in cls.Members)
                    {
                        var memberSymbols = GetClassMemberSymbols(document, member, cls.Name, query);
                        symbols.AddRange(memberSymbols);
                    }
                    break;

                case VariableDeclarationNode varDecl:
                    if (MatchesQuery(varDecl.Name, query))
                    {
                        symbols.Add(new WorkspaceSymbol
                        {
                            Name = varDecl.Name,
                            Kind = SymbolKind.Variable,
                            Location = new Location
                            {
                                Uri = document.Uri,
                                Range = CreateRange(varDecl.Line, varDecl.Column, varDecl.Name.Length)
                            },
                            ContainerName = null
                        });
                    }
                    break;

                case ConstantDeclarationNode constDecl:
                    if (MatchesQuery(constDecl.Name, query))
                    {
                        symbols.Add(new WorkspaceSymbol
                        {
                            Name = constDecl.Name,
                            Kind = SymbolKind.Constant,
                            Location = new Location
                            {
                                Uri = document.Uri,
                                Range = CreateRange(constDecl.Line, constDecl.Column, constDecl.Name.Length)
                            },
                            ContainerName = null
                        });
                    }
                    break;
            }

            return symbols;
        }

        private List<WorkspaceSymbol> GetClassMemberSymbols(
            DocumentState document,
            ASTNode member,
            string className,
            string query)
        {
            var symbols = new List<WorkspaceSymbol>();

            switch (member)
            {
                case FunctionNode func:
                    if (MatchesQuery(func.Name, query))
                    {
                        symbols.Add(new WorkspaceSymbol
                        {
                            Name = func.Name,
                            Kind = SymbolKind.Method,
                            Location = new Location
                            {
                                Uri = document.Uri,
                                Range = CreateRange(func.Line, func.Column, func.Name.Length)
                            },
                            ContainerName = className
                        });
                    }
                    break;

                case SubroutineNode sub:
                    if (MatchesQuery(sub.Name, query))
                    {
                        symbols.Add(new WorkspaceSymbol
                        {
                            Name = sub.Name,
                            Kind = SymbolKind.Method,
                            Location = new Location
                            {
                                Uri = document.Uri,
                                Range = CreateRange(sub.Line, sub.Column, sub.Name.Length)
                            },
                            ContainerName = className
                        });
                    }
                    break;

                case PropertyNode prop:
                    if (MatchesQuery(prop.Name, query))
                    {
                        symbols.Add(new WorkspaceSymbol
                        {
                            Name = prop.Name,
                            Kind = SymbolKind.Property,
                            Location = new Location
                            {
                                Uri = document.Uri,
                                Range = CreateRange(prop.Line, prop.Column, prop.Name.Length)
                            },
                            ContainerName = className
                        });
                    }
                    break;

                case VariableDeclarationNode varDecl:
                    if (MatchesQuery(varDecl.Name, query))
                    {
                        symbols.Add(new WorkspaceSymbol
                        {
                            Name = varDecl.Name,
                            Kind = SymbolKind.Field,
                            Location = new Location
                            {
                                Uri = document.Uri,
                                Range = CreateRange(varDecl.Line, varDecl.Column, varDecl.Name.Length)
                            },
                            ContainerName = className
                        });
                    }
                    break;

                case ConstantDeclarationNode constDecl:
                    if (MatchesQuery(constDecl.Name, query))
                    {
                        symbols.Add(new WorkspaceSymbol
                        {
                            Name = constDecl.Name,
                            Kind = SymbolKind.Constant,
                            Location = new Location
                            {
                                Uri = document.Uri,
                                Range = CreateRange(constDecl.Line, constDecl.Column, constDecl.Name.Length)
                            },
                            ContainerName = className
                        });
                    }
                    break;
            }

            return symbols;
        }

        /// <summary>
        /// Check if a symbol name matches the query
        /// </summary>
        private bool MatchesQuery(string symbolName, string query)
        {
            if (string.IsNullOrEmpty(query))
                return true;

            // Case-insensitive substring match
            return symbolName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Create a range from line, column, and length
        /// </summary>
        private LspRange CreateRange(int line, int column, int length)
        {
            // AST nodes use 1-based line/column, LSP uses 0-based
            return new LspRange(
                new Position(line - 1, column - 1),
                new Position(line - 1, column - 1 + length));
        }

        protected override WorkspaceSymbolRegistrationOptions CreateRegistrationOptions(
            WorkspaceSymbolCapability capability,
            ClientCapabilities clientCapabilities)
        {
            return new WorkspaceSymbolRegistrationOptions();
        }
    }
}
