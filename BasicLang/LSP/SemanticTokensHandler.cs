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

namespace BasicLang.Compiler.LSP
{
    /// <summary>
    /// Handles semantic tokens requests for enhanced syntax highlighting
    /// </summary>
    public class SemanticTokensHandler : SemanticTokensHandlerBase
    {
        private readonly DocumentManager _documentManager;

        // Token type indices
        private const int TokenTypeNamespace = 0;
        private const int TokenTypeType = 1;
        private const int TokenTypeClass = 2;
        private const int TokenTypeEnum = 3;
        private const int TokenTypeInterface = 4;
        private const int TokenTypeStruct = 5;
        private const int TokenTypeTypeParameter = 6;
        private const int TokenTypeParameter = 7;
        private const int TokenTypeVariable = 8;
        private const int TokenTypeProperty = 9;
        private const int TokenTypeEnumMember = 10;
        private const int TokenTypeFunction = 11;
        private const int TokenTypeMethod = 12;
        private const int TokenTypeKeyword = 13;
        private const int TokenTypeModifier = 14;
        private const int TokenTypeComment = 15;
        private const int TokenTypeString = 16;
        private const int TokenTypeNumber = 17;
        private const int TokenTypeOperator = 18;

        // Token modifier bit flags
        private const int ModifierDeclaration = 1 << 0;
        private const int ModifierDefinition = 1 << 1;
        private const int ModifierReadonly = 1 << 2;
        private const int ModifierStatic = 1 << 3;
        private const int ModifierDefaultLibrary = 1 << 9;

        public SemanticTokensHandler(DocumentManager documentManager)
        {
            _documentManager = documentManager;
        }

        protected override SemanticTokensRegistrationOptions CreateRegistrationOptions(
            SemanticTokensCapability capability,
            ClientCapabilities clientCapabilities)
        {
            return new SemanticTokensRegistrationOptions
            {
                DocumentSelector = TextDocumentSelector.ForLanguage("basiclang"),
                Full = new SemanticTokensCapabilityRequestFull { Delta = false },
                Range = true,
                Legend = new SemanticTokensLegend
                {
                    TokenTypes = new Container<SemanticTokenType>(
                        SemanticTokenType.Namespace,
                        SemanticTokenType.Type,
                        SemanticTokenType.Class,
                        SemanticTokenType.Enum,
                        SemanticTokenType.Interface,
                        SemanticTokenType.Struct,
                        SemanticTokenType.TypeParameter,
                        SemanticTokenType.Parameter,
                        SemanticTokenType.Variable,
                        SemanticTokenType.Property,
                        SemanticTokenType.EnumMember,
                        SemanticTokenType.Function,
                        SemanticTokenType.Method,
                        SemanticTokenType.Keyword,
                        SemanticTokenType.Modifier,
                        SemanticTokenType.Comment,
                        SemanticTokenType.String,
                        SemanticTokenType.Number,
                        SemanticTokenType.Operator
                    ),
                    TokenModifiers = new Container<SemanticTokenModifier>(
                        SemanticTokenModifier.Declaration,
                        SemanticTokenModifier.Definition,
                        SemanticTokenModifier.Readonly,
                        SemanticTokenModifier.Static,
                        SemanticTokenModifier.Deprecated,
                        SemanticTokenModifier.Abstract,
                        SemanticTokenModifier.Async,
                        SemanticTokenModifier.Modification,
                        SemanticTokenModifier.Documentation,
                        SemanticTokenModifier.DefaultLibrary
                    )
                }
            };
        }

        protected override Task<SemanticTokensDocument> GetSemanticTokensDocument(
            ITextDocumentIdentifierParams @params,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new SemanticTokensDocument(CreateRegistrationOptions(null, null).Legend));
        }

        protected override Task Tokenize(
            SemanticTokensBuilder builder,
            ITextDocumentIdentifierParams identifier,
            CancellationToken cancellationToken)
        {
            var state = _documentManager.GetDocument(identifier.TextDocument.Uri);
            if (state == null)
                return Task.CompletedTask;

            var collector = new SemanticTokenCollector(state, builder);
            collector.Collect();

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Collects semantic tokens from the AST
    /// </summary>
    internal class SemanticTokenCollector
    {
        private readonly DocumentState _state;
        private readonly SemanticTokensBuilder _builder;
        private readonly HashSet<string> _declaredVariables = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _declaredFunctions = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _declaredClasses = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _parameters = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _constants = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _properties = new(StringComparer.OrdinalIgnoreCase);

        // Token type indices (must match handler)
        private const int TokenTypeClass = 2;
        private const int TokenTypeParameter = 7;
        private const int TokenTypeVariable = 8;
        private const int TokenTypeProperty = 9;
        private const int TokenTypeFunction = 11;
        private const int TokenTypeKeyword = 13;
        private const int TokenTypeModifier = 14;
        private const int TokenTypeComment = 15;
        private const int TokenTypeString = 16;
        private const int TokenTypeNumber = 17;
        private const int TokenTypeOperator = 18;

        private const int ModifierReadonly = 1 << 2;
        private const int ModifierDefaultLibrary = 1 << 9;

        // Built-in functions for special highlighting
        private static readonly HashSet<string> BuiltInFunctions = new(StringComparer.OrdinalIgnoreCase)
        {
            "PrintLine", "Print", "ReadLine", "Len", "Left", "Right", "Mid",
            "UCase", "LCase", "Trim", "InStr", "Replace", "Abs", "Sqrt", "Pow",
            "Sin", "Cos", "Tan", "Floor", "Ceiling", "Round", "Rnd", "Min", "Max",
            "CInt", "CDbl", "CStr", "CBool", "UBound", "LBound", "Chr", "Asc"
        };

        public SemanticTokenCollector(DocumentState state, SemanticTokensBuilder builder)
        {
            _state = state;
            _builder = builder;
        }

        public void Collect()
        {
            // First pass: collect all declarations
            if (_state.AST != null)
            {
                CollectDeclarations(_state.AST);
            }

            // Second pass: tokenize based on collected info
            foreach (var token in _state.Tokens)
            {
                ProcessToken(token);
            }
        }

        private void CollectDeclarations(ProgramNode program)
        {
            foreach (var decl in program.Declarations)
            {
                CollectDeclarationsFromNode(decl);
            }
        }

        private void CollectDeclarationsFromNode(ASTNode node)
        {
            switch (node)
            {
                case FunctionNode func:
                    _declaredFunctions.Add(func.Name);
                    foreach (var param in func.Parameters)
                        _parameters.Add(param.Name);
                    if (func.Body != null)
                        CollectDeclarationsFromNode(func.Body);
                    break;

                case SubroutineNode sub:
                    _declaredFunctions.Add(sub.Name);
                    foreach (var param in sub.Parameters)
                        _parameters.Add(param.Name);
                    if (sub.Body != null)
                        CollectDeclarationsFromNode(sub.Body);
                    break;

                case ClassNode cls:
                    _declaredClasses.Add(cls.Name);
                    foreach (var member in cls.Members)
                        CollectDeclarationsFromNode(member);
                    break;

                case PropertyNode prop:
                    _properties.Add(prop.Name);
                    break;

                case VariableDeclarationNode varDecl:
                    _declaredVariables.Add(varDecl.Name);
                    break;

                case ConstantDeclarationNode constDecl:
                    _constants.Add(constDecl.Name);
                    break;

                case BlockNode block:
                    foreach (var stmt in block.Statements)
                        CollectDeclarationsFromNode(stmt);
                    break;

                case IfStatementNode ifStmt:
                    if (ifStmt.ThenBlock != null)
                        CollectDeclarationsFromNode(ifStmt.ThenBlock);
                    if (ifStmt.ElseBlock != null)
                        CollectDeclarationsFromNode(ifStmt.ElseBlock);
                    break;

                case ForLoopNode forLoop:
                    _declaredVariables.Add(forLoop.Variable);
                    if (forLoop.Body != null)
                        CollectDeclarationsFromNode(forLoop.Body);
                    break;

                case WhileLoopNode whileLoop:
                    if (whileLoop.Body != null)
                        CollectDeclarationsFromNode(whileLoop.Body);
                    break;

                case DoLoopNode doLoop:
                    if (doLoop.Body != null)
                        CollectDeclarationsFromNode(doLoop.Body);
                    break;
            }
        }

        private void ProcessToken(Token token)
        {
            if (token.Line <= 0 || token.Column <= 0)
                return;

            int line = token.Line - 1;      // 0-based
            int col = token.Column - 1;     // 0-based
            int length = token.Lexeme?.Length ?? 0;

            if (length == 0)
                return;

            int tokenType = -1;
            int modifiers = 0;

            switch (token.Type)
            {
                // Comments
                case TokenType.Comment:
                    tokenType = TokenTypeComment;
                    break;

                // Strings
                case TokenType.StringLiteral:
                    tokenType = TokenTypeString;
                    break;

                // Numbers
                case TokenType.IntegerLiteral:
                case TokenType.LongLiteral:
                case TokenType.SingleLiteral:
                case TokenType.DoubleLiteral:
                    tokenType = TokenTypeNumber;
                    break;

                // Identifiers - determine semantic meaning
                case TokenType.Identifier:
                    var name = token.Lexeme;
                    if (_declaredClasses.Contains(name))
                    {
                        tokenType = TokenTypeClass;
                    }
                    else if (_declaredFunctions.Contains(name) || BuiltInFunctions.Contains(name))
                    {
                        tokenType = TokenTypeFunction;
                        if (BuiltInFunctions.Contains(name))
                            modifiers |= ModifierDefaultLibrary;
                    }
                    else if (_parameters.Contains(name))
                    {
                        tokenType = TokenTypeParameter;
                    }
                    else if (_constants.Contains(name))
                    {
                        tokenType = TokenTypeVariable;
                        modifiers |= ModifierReadonly;
                    }
                    else if (_properties.Contains(name))
                    {
                        tokenType = TokenTypeProperty;
                    }
                    else if (_declaredVariables.Contains(name))
                    {
                        tokenType = TokenTypeVariable;
                    }
                    break;

                // Keywords - Control Flow
                case TokenType.Sub:
                case TokenType.Function:
                case TokenType.EndSub:
                case TokenType.EndFunction:
                case TokenType.If:
                case TokenType.Then:
                case TokenType.Else:
                case TokenType.ElseIf:
                case TokenType.EndIf:
                case TokenType.For:
                case TokenType.To:
                case TokenType.Step:
                case TokenType.Next:
                case TokenType.While:
                case TokenType.Wend:
                case TokenType.Do:
                case TokenType.Loop:
                case TokenType.Until:
                case TokenType.Return:
                case TokenType.Exit:
                case TokenType.Select:
                case TokenType.Case:
                case TokenType.EndSelect:
                case TokenType.Try:
                case TokenType.Catch:
                case TokenType.EndTry:
                case TokenType.New:
                case TokenType.Me:
                case TokenType.MyBase:
                case TokenType.Each:
                case TokenType.In:
                    tokenType = TokenTypeKeyword;
                    break;

                // Type keywords
                case TokenType.Class:
                case TokenType.EndClass:
                case TokenType.Interface:
                case TokenType.EndInterface:
                case TokenType.Structure:
                case TokenType.EndStructure:
                case TokenType.Type:
                case TokenType.EndType:
                    tokenType = TokenTypeKeyword;
                    break;

                // Modifiers
                case TokenType.Public:
                case TokenType.Private:
                case TokenType.Protected:
                case TokenType.Shared:
                case TokenType.Const:
                case TokenType.Dim:
                case TokenType.Overridable:
                case TokenType.Overrides:
                case TokenType.MustOverride:
                case TokenType.NotOverridable:
                case TokenType.ReadOnly:
                case TokenType.WriteOnly:
                    tokenType = TokenTypeModifier;
                    break;

                // Type names and other keywords
                case TokenType.As:
                case TokenType.Of:
                case TokenType.Inherits:
                case TokenType.Implements:
                    tokenType = TokenTypeKeyword;
                    break;

                // Operators
                case TokenType.And:
                case TokenType.Or:
                case TokenType.Not:
                    tokenType = TokenTypeOperator;
                    break;

                // Boolean literals
                case TokenType.BooleanLiteral:
                    tokenType = TokenTypeKeyword;
                    break;
            }

            if (tokenType >= 0)
            {
                _builder.Push(line, col, length, tokenType, modifiers);
            }
        }
    }
}
