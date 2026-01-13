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
    /// Handles type hierarchy prepare requests (show class inheritance)
    /// </summary>
    public class TypeHierarchyPrepareHandler : ITypeHierarchyPrepareHandler
    {
        private readonly DocumentManager _documentManager;
        private TypeHierarchyCapability _capability;

        public TypeHierarchyPrepareHandler(DocumentManager documentManager)
        {
            _documentManager = documentManager;
        }

        public Guid Id { get; } = Guid.NewGuid();

        public void SetCapability(TypeHierarchyCapability capability, ClientCapabilities clientCapabilities)
        {
            _capability = capability;
        }

        public Task<Container<TypeHierarchyItem>?> Handle(TypeHierarchyPrepareParams request, CancellationToken cancellationToken)
        {
            var state = _documentManager.GetDocument(request.TextDocument.Uri);
            if (state == null || state.AST == null)
            {
                return Task.FromResult<Container<TypeHierarchyItem>>(null);
            }

            // Get the word at the cursor position
            var word = state.GetWordAtPosition((int)request.Position.Line, (int)request.Position.Character);
            if (string.IsNullOrEmpty(word))
            {
                return Task.FromResult<Container<TypeHierarchyItem>>(null);
            }

            // Find the class/interface at this position
            var typeNode = FindTypeAtPosition(state, word);
            if (typeNode == null)
            {
                return Task.FromResult<Container<TypeHierarchyItem>>(null);
            }

            var item = CreateTypeHierarchyItem(state.Uri, typeNode);
            if (item == null)
            {
                return Task.FromResult<Container<TypeHierarchyItem>>(null);
            }

            return Task.FromResult(new Container<TypeHierarchyItem>(item));
        }

        private ASTNode FindTypeAtPosition(DocumentState state, string name)
        {
            foreach (var decl in state.AST.Declarations)
            {
                var typeNode = FindTypeInNode(decl, name);
                if (typeNode != null)
                    return typeNode;
            }
            return null;
        }

        private ASTNode FindTypeInNode(ASTNode node, string name)
        {
            switch (node)
            {
                case ClassNode cls when cls.Name.Equals(name, StringComparison.OrdinalIgnoreCase):
                    return cls;

                case InterfaceNode iface when iface.Name.Equals(name, StringComparison.OrdinalIgnoreCase):
                    return iface;

                case StructureNode structure when structure.Name.Equals(name, StringComparison.OrdinalIgnoreCase):
                    return structure;

                case ModuleNode module:
                    foreach (var member in module.Members)
                    {
                        var typeNode = FindTypeInNode(member, name);
                        if (typeNode != null)
                            return typeNode;
                    }
                    break;
            }

            return null;
        }

        private TypeHierarchyItem CreateTypeHierarchyItem(DocumentUri uri, ASTNode node)
        {
            switch (node)
            {
                case ClassNode cls:
                    return new TypeHierarchyItem
                    {
                        Name = cls.Name,
                        Kind = SymbolKind.Class,
                        Detail = string.IsNullOrEmpty(cls.BaseClass) ? "class" : $"class : {cls.BaseClass}",
                        Uri = uri,
                        Range = new LspRange(
                            new Position(cls.Line - 1, 0),
                            new Position(cls.Line + 10, 0)),
                        SelectionRange = new LspRange(
                            new Position(cls.Line - 1, cls.Column - 1),
                            new Position(cls.Line - 1, cls.Column - 1 + cls.Name.Length))
                    };

                case InterfaceNode iface:
                    return new TypeHierarchyItem
                    {
                        Name = iface.Name,
                        Kind = SymbolKind.Interface,
                        Detail = "interface",
                        Uri = uri,
                        Range = new LspRange(
                            new Position(iface.Line - 1, 0),
                            new Position(iface.Line + 10, 0)),
                        SelectionRange = new LspRange(
                            new Position(iface.Line - 1, iface.Column - 1),
                            new Position(iface.Line - 1, iface.Column - 1 + iface.Name.Length))
                    };

                case StructureNode structure:
                    return new TypeHierarchyItem
                    {
                        Name = structure.Name,
                        Kind = SymbolKind.Struct,
                        Detail = "structure",
                        Uri = uri,
                        Range = new LspRange(
                            new Position(structure.Line - 1, 0),
                            new Position(structure.Line + 10, 0)),
                        SelectionRange = new LspRange(
                            new Position(structure.Line - 1, structure.Column - 1),
                            new Position(structure.Line - 1, structure.Column - 1 + structure.Name.Length))
                    };
            }

            return null;
        }

        public TypeHierarchyRegistrationOptions GetRegistrationOptions(
            TypeHierarchyCapability capability,
            ClientCapabilities clientCapabilities)
        {
            return new TypeHierarchyRegistrationOptions
            {
                DocumentSelector = TextDocumentSelector.ForLanguage("basiclang")
            };
        }
    }

    /// <summary>
    /// Handles type hierarchy supertypes requests (show base classes)
    /// </summary>
    public class TypeHierarchySupertypesHandler : ITypeHierarchySupertypesHandler
    {
        private readonly DocumentManager _documentManager;
        private TypeHierarchyCapability _capability;

        public TypeHierarchySupertypesHandler(DocumentManager documentManager)
        {
            _documentManager = documentManager;
        }

        public Guid Id { get; } = Guid.NewGuid();

        public void SetCapability(TypeHierarchyCapability capability, ClientCapabilities clientCapabilities)
        {
            _capability = capability;
        }

        public Task<Container<TypeHierarchyItem>?> Handle(TypeHierarchySupertypesParams request, CancellationToken cancellationToken)
        {
            var supertypes = new List<TypeHierarchyItem>();

            // Get base class information from the item
            var baseClassName = GetBaseClassName(request.Item);
            if (string.IsNullOrEmpty(baseClassName))
            {
                return Task.FromResult<Container<TypeHierarchyItem>>(null);
            }

            // Search through all documents to find the base class
            foreach (var document in _documentManager.GetAllDocuments())
            {
                if (document.AST == null)
                    continue;

                var baseClass = FindClassByName(document, baseClassName);
                if (baseClass != null)
                {
                    var item = CreateTypeHierarchyItem(document.Uri, baseClass);
                    if (item != null)
                    {
                        supertypes.Add(item);
                    }
                }
            }

            if (supertypes.Count == 0)
            {
                return Task.FromResult<Container<TypeHierarchyItem>>(null);
            }

            return Task.FromResult(new Container<TypeHierarchyItem>(supertypes));
        }

        private string GetBaseClassName(TypeHierarchyItem item)
        {
            // Try to extract base class from detail string
            if (!string.IsNullOrEmpty(item.Detail) && item.Detail.Contains(":"))
            {
                var parts = item.Detail.Split(':');
                if (parts.Length > 1)
                {
                    return parts[1].Trim().Split(',')[0].Trim();
                }
            }

            return null;
        }

        private ClassNode FindClassByName(DocumentState state, string className)
        {
            foreach (var decl in state.AST.Declarations)
            {
                var cls = FindClassInNode(decl, className);
                if (cls != null)
                    return cls;
            }
            return null;
        }

        private ClassNode FindClassInNode(ASTNode node, string className)
        {
            switch (node)
            {
                case ClassNode cls when cls.Name.Equals(className, StringComparison.OrdinalIgnoreCase):
                    return cls;

                case ModuleNode module:
                    foreach (var member in module.Members)
                    {
                        var cls = FindClassInNode(member, className);
                        if (cls != null)
                            return cls;
                    }
                    break;
            }

            return null;
        }

        private TypeHierarchyItem CreateTypeHierarchyItem(DocumentUri uri, ClassNode cls)
        {
            return new TypeHierarchyItem
            {
                Name = cls.Name,
                Kind = SymbolKind.Class,
                Detail = string.IsNullOrEmpty(cls.BaseClass) ? "class" : $"class : {cls.BaseClass}",
                Uri = uri,
                Range = new LspRange(
                    new Position(cls.Line - 1, 0),
                    new Position(cls.Line + 10, 0)),
                SelectionRange = new LspRange(
                    new Position(cls.Line - 1, cls.Column - 1),
                    new Position(cls.Line - 1, cls.Column - 1 + cls.Name.Length))
            };
        }

        public TypeHierarchyRegistrationOptions GetRegistrationOptions(
            TypeHierarchyCapability capability,
            ClientCapabilities clientCapabilities)
        {
            return new TypeHierarchyRegistrationOptions
            {
                DocumentSelector = TextDocumentSelector.ForLanguage("basiclang")
            };
        }
    }

    /// <summary>
    /// Handles type hierarchy subtypes requests (show derived classes)
    /// </summary>
    public class TypeHierarchySubtypesHandler : ITypeHierarchySubtypesHandler
    {
        private readonly DocumentManager _documentManager;
        private TypeHierarchyCapability _capability;

        public TypeHierarchySubtypesHandler(DocumentManager documentManager)
        {
            _documentManager = documentManager;
        }

        public Guid Id { get; } = Guid.NewGuid();

        public void SetCapability(TypeHierarchyCapability capability, ClientCapabilities clientCapabilities)
        {
            _capability = capability;
        }

        public Task<Container<TypeHierarchyItem>?> Handle(TypeHierarchySubtypesParams request, CancellationToken cancellationToken)
        {
            var subtypes = new List<TypeHierarchyItem>();
            var className = request.Item.Name;

            // Search through all documents to find derived classes
            foreach (var document in _documentManager.GetAllDocuments())
            {
                if (document.AST == null)
                    continue;

                var derivedClasses = FindDerivedClasses(document, className);
                foreach (var derivedClass in derivedClasses)
                {
                    var item = CreateTypeHierarchyItem(document.Uri, derivedClass);
                    if (item != null)
                    {
                        subtypes.Add(item);
                    }
                }
            }

            if (subtypes.Count == 0)
            {
                return Task.FromResult<Container<TypeHierarchyItem>>(null);
            }

            return Task.FromResult(new Container<TypeHierarchyItem>(subtypes));
        }

        private List<ClassNode> FindDerivedClasses(DocumentState state, string baseClassName)
        {
            var derivedClasses = new List<ClassNode>();

            foreach (var decl in state.AST.Declarations)
            {
                FindDerivedClassesInNode(decl, baseClassName, derivedClasses);
            }

            return derivedClasses;
        }

        private void FindDerivedClassesInNode(ASTNode node, string baseClassName, List<ClassNode> derivedClasses)
        {
            switch (node)
            {
                case ClassNode cls:
                    if (!string.IsNullOrEmpty(cls.BaseClass) &&
                        cls.BaseClass.Equals(baseClassName, StringComparison.OrdinalIgnoreCase))
                    {
                        derivedClasses.Add(cls);
                    }
                    break;

                case ModuleNode module:
                    foreach (var member in module.Members)
                    {
                        FindDerivedClassesInNode(member, baseClassName, derivedClasses);
                    }
                    break;
            }
        }

        private TypeHierarchyItem CreateTypeHierarchyItem(DocumentUri uri, ClassNode cls)
        {
            return new TypeHierarchyItem
            {
                Name = cls.Name,
                Kind = SymbolKind.Class,
                Detail = string.IsNullOrEmpty(cls.BaseClass) ? "class" : $"class : {cls.BaseClass}",
                Uri = uri,
                Range = new LspRange(
                    new Position(cls.Line - 1, 0),
                    new Position(cls.Line + 10, 0)),
                SelectionRange = new LspRange(
                    new Position(cls.Line - 1, cls.Column - 1),
                    new Position(cls.Line - 1, cls.Column - 1 + cls.Name.Length))
            };
        }

        public TypeHierarchyRegistrationOptions GetRegistrationOptions(
            TypeHierarchyCapability capability,
            ClientCapabilities clientCapabilities)
        {
            return new TypeHierarchyRegistrationOptions
            {
                DocumentSelector = TextDocumentSelector.ForLanguage("basiclang")
            };
        }
    }
}
