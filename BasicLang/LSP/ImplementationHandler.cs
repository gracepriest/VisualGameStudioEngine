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
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace BasicLang.Compiler.LSP
{
    /// <summary>
    /// Handles go to implementation requests
    /// </summary>
    public class ImplementationHandler : ImplementationHandlerBase
    {
        private readonly DocumentManager _documentManager;

        public ImplementationHandler(DocumentManager documentManager)
        {
            _documentManager = documentManager;
        }

        public override Task<LocationOrLocationLinks> Handle(
            ImplementationParams request,
            CancellationToken cancellationToken)
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

            var locations = new List<LocationOrLocationLink>();

            // Find what the word refers to
            var symbol = FindSymbolDefinition(state, word);
            if (symbol == null)
            {
                return Task.FromResult<LocationOrLocationLinks>(null);
            }

            // Find implementations based on symbol type
            switch (symbol)
            {
                case InterfaceInfo interfaceInfo:
                    // Find all classes that implement this interface
                    foreach (var loc in FindInterfaceImplementations(interfaceInfo.Name))
                    {
                        locations.Add(new LocationOrLocationLink(loc));
                    }
                    break;

                case BaseClassInfo baseClassInfo:
                    // Find all classes that inherit from this class
                    foreach (var loc in FindClassInheritors(baseClassInfo.Name))
                    {
                        locations.Add(new LocationOrLocationLink(loc));
                    }
                    break;

                case MethodInfo methodInfo:
                    // Find all implementations of this method (overrides)
                    foreach (var loc in FindMethodImplementations(methodInfo))
                    {
                        locations.Add(new LocationOrLocationLink(loc));
                    }
                    break;
            }

            return Task.FromResult(new LocationOrLocationLinks(locations));
        }

        private SymbolInfo FindSymbolDefinition(DocumentState state, string word)
        {
            if (state?.AST == null) return null;

            foreach (var decl in state.AST.Declarations)
            {
                var symbol = FindSymbolInDeclaration(state, decl, word);
                if (symbol != null) return symbol;
            }

            return null;
        }

        private SymbolInfo FindSymbolInDeclaration(DocumentState state, ASTNode node, string word)
        {
            switch (node)
            {
                case InterfaceNode iface when iface.Name.Equals(word, StringComparison.OrdinalIgnoreCase):
                    return new InterfaceInfo { Name = iface.Name, Uri = state.Uri, Line = iface.Line, Column = iface.Column };

                case ClassNode cls when cls.Name.Equals(word, StringComparison.OrdinalIgnoreCase):
                    // Check if it's an interface by naming convention (starts with I)
                    if (word.Length > 1 && word.StartsWith("I") && char.IsUpper(word[1]))
                    {
                        return new InterfaceInfo { Name = cls.Name, Uri = state.Uri, Line = cls.Line, Column = cls.Column };
                    }
                    return new BaseClassInfo { Name = cls.Name, Uri = state.Uri, Line = cls.Line, Column = cls.Column };

                case FunctionNode func when func.Name.Equals(word, StringComparison.OrdinalIgnoreCase):
                    // Check if it's a virtual/abstract method that could be overridden
                    if (func.IsVirtual || func.IsAbstract || func.IsOverride)
                    {
                        var containingClass = FindContainingClass(state, func);
                        return new MethodInfo
                        {
                            Name = func.Name,
                            ContainingClass = containingClass,
                            Uri = state.Uri,
                            Line = func.Line,
                            Column = func.Column
                        };
                    }
                    break;

                case ClassNode cls:
                    // Search members
                    foreach (var member in cls.Members)
                    {
                        var symbol = FindSymbolInDeclaration(state, member, word);
                        if (symbol != null)
                        {
                            if (symbol is MethodInfo methodInfo)
                            {
                                methodInfo.ContainingClass = cls.Name;
                            }
                            return symbol;
                        }
                    }
                    break;
            }

            return null;
        }

        private string FindContainingClass(DocumentState state, FunctionNode func)
        {
            if (state?.AST == null) return null;

            foreach (var decl in state.AST.Declarations)
            {
                if (decl is ClassNode cls)
                {
                    if (cls.Members.Contains(func))
                    {
                        return cls.Name;
                    }
                }
            }

            return null;
        }

        private List<Location> FindInterfaceImplementations(string interfaceName)
        {
            var locations = new List<Location>();

            foreach (var doc in _documentManager.GetAllDocuments())
            {
                if (doc?.AST == null) continue;

                foreach (var decl in doc.AST.Declarations)
                {
                    if (decl is ClassNode cls)
                    {
                        // Check if class implements the interface
                        if (cls.Interfaces != null &&
                            cls.Interfaces.Any(i =>
                                i.Equals(interfaceName, StringComparison.OrdinalIgnoreCase)))
                        {
                            locations.Add(new Location
                            {
                                Uri = doc.Uri,
                                Range = CreateRange(cls.Line, cls.Column, cls.Name.Length)
                            });
                        }
                    }
                }
            }

            return locations;
        }

        private List<Location> FindClassInheritors(string baseClassName)
        {
            var locations = new List<Location>();

            foreach (var doc in _documentManager.GetAllDocuments())
            {
                if (doc?.AST == null) continue;

                foreach (var decl in doc.AST.Declarations)
                {
                    if (decl is ClassNode cls)
                    {
                        // Check if class inherits from the base class
                        if (cls.BaseClass != null &&
                            cls.BaseClass.Equals(baseClassName, StringComparison.OrdinalIgnoreCase))
                        {
                            locations.Add(new Location
                            {
                                Uri = doc.Uri,
                                Range = CreateRange(cls.Line, cls.Column, cls.Name.Length)
                            });
                        }
                    }
                }
            }

            return locations;
        }

        private List<Location> FindMethodImplementations(MethodInfo methodInfo)
        {
            var locations = new List<Location>();

            if (string.IsNullOrEmpty(methodInfo.ContainingClass))
                return locations;

            // First, find all classes that inherit from the containing class
            var inheritors = FindAllInheritors(methodInfo.ContainingClass);

            // Then find implementations of the method in those classes
            foreach (var doc in _documentManager.GetAllDocuments())
            {
                if (doc?.AST == null) continue;

                foreach (var decl in doc.AST.Declarations)
                {
                    if (decl is ClassNode cls && inheritors.Contains(cls.Name))
                    {
                        // Look for method override
                        foreach (var member in cls.Members)
                        {
                            if (member is FunctionNode func &&
                                func.Name.Equals(methodInfo.Name, StringComparison.OrdinalIgnoreCase))
                            {
                                locations.Add(new Location
                                {
                                    Uri = doc.Uri,
                                    Range = CreateRange(func.Line, func.Column, func.Name.Length)
                                });
                            }
                        }
                    }
                }
            }

            return locations;
        }

        private HashSet<string> FindAllInheritors(string baseClassName)
        {
            var inheritors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>();
            queue.Enqueue(baseClassName);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();

                foreach (var doc in _documentManager.GetAllDocuments())
                {
                    if (doc?.AST == null) continue;

                    foreach (var decl in doc.AST.Declarations)
                    {
                        if (decl is ClassNode cls)
                        {
                            if (cls.BaseClass != null &&
                                cls.BaseClass.Equals(current, StringComparison.OrdinalIgnoreCase))
                            {
                                if (inheritors.Add(cls.Name))
                                {
                                    queue.Enqueue(cls.Name);
                                }
                            }

                            // Also check implemented interfaces
                            if (cls.Interfaces != null &&
                                cls.Interfaces.Any(i =>
                                    i.Equals(current, StringComparison.OrdinalIgnoreCase)))
                            {
                                if (inheritors.Add(cls.Name))
                                {
                                    queue.Enqueue(cls.Name);
                                }
                            }
                        }
                    }
                }
            }

            return inheritors;
        }

        private LspRange CreateRange(int line, int column, int length)
        {
            return new LspRange(
                new Position(line - 1, column - 1),
                new Position(line - 1, column - 1 + length));
        }

        protected override ImplementationRegistrationOptions CreateRegistrationOptions(
            ImplementationCapability capability,
            ClientCapabilities clientCapabilities)
        {
            return new ImplementationRegistrationOptions
            {
                DocumentSelector = TextDocumentSelector.ForLanguage("basiclang")
            };
        }

        // Helper classes for symbol information
        private abstract class SymbolInfo
        {
            public string Name { get; set; }
            public DocumentUri Uri { get; set; }
            public int Line { get; set; }
            public int Column { get; set; }
        }

        private class InterfaceInfo : SymbolInfo { }
        private class BaseClassInfo : SymbolInfo { }
        private class MethodInfo : SymbolInfo
        {
            public string ContainingClass { get; set; }
        }
    }
}
