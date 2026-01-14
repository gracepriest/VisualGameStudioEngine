using System;
using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler.AST;

namespace BasicLang.Compiler
{
    /// <summary>
    /// Recursive descent parser for BasicLang
    /// </summary>
    public class Parser
    {
        private readonly List<Token> _tokens;
        private int _current;
        private readonly List<ParseError> _errors;
        private readonly Stack<string> _context;
        private const int MaxErrors = 100;
        private bool _panicMode;

        public Parser(List<Token> tokens)
        {
            _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
            _current = 0;
            _errors = new List<ParseError>();
            _context = new Stack<string>();
            _panicMode = false;

            // Remove comments and filter unnecessary tokens
            _tokens = _tokens.Where(t => t.Type != TokenType.Comment).ToList();
        }

        /// <summary>
        /// Gets all parsing errors collected during parsing
        /// </summary>
        public IReadOnlyList<ParseError> Errors => _errors.AsReadOnly();

        /// <summary>
        /// Parse the token stream into an AST
        /// </summary>
        public ProgramNode Parse()
        {
            var program = new ProgramNode(1, 1);

            while (!IsAtEnd())
            {
                SkipNewlines();

                if (IsAtEnd())
                    break;

                try
                {
                    var declaration = ParseTopLevelDeclaration();
                    if (declaration != null)
                    {
                        program.Declarations.Add(declaration);
                    }
                }
                catch (ParseException ex)
                {
                    // Record error and attempt recovery
                    RecordError(ex.Message, ex.Token, ex.Suggestion);
                    Synchronize();
                }

                SkipNewlines();
            }

            return program;
        }

        // ====================================================================
        // Top-Level Declarations
        // ====================================================================

        private ASTNode ParseTopLevelDeclaration()
        {
            SkipNewlines();

            if (Check(TokenType.Namespace))
                return ParseNamespace();
            if (Check(TokenType.Module))
                return ParseModule();
            if (Check(TokenType.Using))
                return ParseUsing();
            if (Check(TokenType.Import))
                return ParseImport();
            if (Check(TokenType.Class))
                return ParseClass();
            if (Check(TokenType.Interface))
                return ParseInterface();
            if (Check(TokenType.Enum))
                return ParseEnum();
            if (Check(TokenType.Type))
                return ParseType();
            if (Check(TokenType.Structure))
                return ParseStructure();
            if (Check(TokenType.Union))
                return ParseUnion();
            if (Check(TokenType.Template))
                return ParseTemplate();
            if (Check(TokenType.Delegate))
                return ParseDelegate();
            if (Check(TokenType.TypeDefine))
                return ParseTypeDefine();
            if (Check(TokenType.Extern))
                return ParseExtern();
            if (Check(TokenType.Declare))
                return ParseDeclare();
            if (Check(TokenType.Extension))
                return ParseExtensionMethod();
            // Handle MustInherit modifier for classes
            if (Check(TokenType.MustInherit))
            {
                Advance(); // consume MustInherit
                if (Check(TokenType.Class))
                {
                    var cls = ParseClass();
                    cls.IsAbstract = true;
                    return cls;
                }
                throw new ParseException("Expected 'Class' after 'MustInherit'", Peek());
            }
            // Handle visibility and other modifiers for top-level declarations
            if (Check(TokenType.Public) || Check(TokenType.Private) || Check(TokenType.Friend) ||
                Check(TokenType.Async) || Check(TokenType.Iterator) || Check(TokenType.Inline) ||
                Check(TokenType.Shared))
            {
                var access = AccessModifier.Private;  // Default for top-level
                bool isAsync = false;
                bool isIterator = false;
                bool isInline = false;
                bool isStatic = false;

                // Parse modifiers in any order
                while (Check(TokenType.Public) || Check(TokenType.Private) || Check(TokenType.Friend) ||
                       Check(TokenType.Async) || Check(TokenType.Iterator) || Check(TokenType.Inline) ||
                       Check(TokenType.Shared))
                {
                    if (Match(TokenType.Public)) access = AccessModifier.Public;
                    else if (Match(TokenType.Private)) access = AccessModifier.Private;
                    else if (Match(TokenType.Friend)) access = AccessModifier.Friend;
                    else if (Match(TokenType.Async)) isAsync = true;
                    else if (Match(TokenType.Iterator)) isIterator = true;
                    else if (Match(TokenType.Inline)) isInline = true;
                    else if (Match(TokenType.Shared)) isStatic = true;
                }

                if (Check(TokenType.Function))
                {
                    var func = ParseFunction();
                    func.Access = access;
                    func.IsAsync = isAsync;
                    func.IsIterator = isIterator;
                    func.IsInline = isInline;
                    func.IsStatic = isStatic;
                    return func;
                }
                if (Check(TokenType.Sub))
                {
                    var sub = ParseSubroutine();
                    sub.Access = access;
                    sub.IsAsync = isAsync;
                    sub.IsStatic = isStatic;
                    return sub;
                }
                if (Check(TokenType.Dim))
                {
                    var statement = ParseVariableDeclaration();
                    if (statement is VariableDeclarationNode variable)
                    {
                        variable.Access = access;
                        variable.IsStatic = isStatic;
                    }
                    return statement;
                }
                if (Check(TokenType.Const))
                {
                    var statement = ParseConstantDeclaration();
                    if (statement is ConstantDeclarationNode constant)
                    {
                        constant.Access = access;
                    }
                    return statement;
                }
                throw new ParseException(
                    $"Expected Function, Sub, Dim, or Const after modifiers, got '{Peek().Lexeme}'",
                    Peek());
            }
            if (Check(TokenType.Function))
                return ParseFunction();
            if (Check(TokenType.Sub))
                return ParseSubroutine();
            if (Check(TokenType.Dim))
                return ParseVariableDeclaration();
            if (Check(TokenType.Auto))
                return ParseAutoDeclaration();
            if (Check(TokenType.Const))
                return ParseConstantDeclaration();

            throw new ParseException(
                $"Unexpected token at top level: '{Peek().Lexeme}' ({Peek().Type})",
                Peek(),
                "Top-level code must be inside a Module, Class, Function, or Sub. Valid declarations include: Module, Class, Interface, Function, Sub, Dim, Const, or Namespace.");
        }

        // ====================================================================
        // Namespaces and Modules
        // ====================================================================

        private NamespaceNode ParseNamespace()
        {
            var token = Consume(TokenType.Namespace, "Expected 'Namespace'");
            var node = new NamespaceNode(token.Line, token.Column);

            node.Name = Consume(TokenType.Identifier, "Expected namespace name").Lexeme;
            _context.Push($"Namespace '{node.Name}'");

            try
            {
                ConsumeNewlines();

                while (!Check(TokenType.EndNamespace) && !IsAtEnd())
                {
                    try
                    {
                        var member = ParseTopLevelDeclaration();
                        if (member != null)
                        {
                            node.Members.Add(member);
                        }
                    }
                    catch (ParseException ex)
                    {
                        RecordError(ex.Message, ex.Token, ex.Suggestion);
                        Synchronize();
                    }
                    SkipNewlines();
                }

                Consume(TokenType.EndNamespace, "Expected 'End Namespace'");
            }
            finally
            {
                _context.Pop();
            }

            return node;
        }

        private ModuleNode ParseModule()
        {
            var token = Consume(TokenType.Module, "Expected 'Module'");
            var node = new ModuleNode(token.Line, token.Column);

            node.Name = Consume(TokenType.Identifier, "Expected module name").Lexeme;
            ConsumeNewlines();

            while (!Check(TokenType.EndModule) && !IsAtEnd())
            {
                var member = ParseModuleMember();
                if (member != null)
                {
                    node.Members.Add(member);
                }
                SkipNewlines();
            }

            Consume(TokenType.EndModule, "Expected 'End Module'");
            return node;
        }

        private ASTNode ParseModuleMember()
        {
            // Handle attribute syntax <Extension>
            if (Check(TokenType.LessThan))
            {
                Advance(); // consume <
                if (Check(TokenType.Extension))
                {
                    Advance(); // consume Extension
                    Consume(TokenType.GreaterThan, "Expected '>' after attribute");
                    SkipNewlines();
                    return ParseExtensionMethodFromAttribute();
                }
                // Skip other unrecognized attributes
                while (!Check(TokenType.GreaterThan) && !IsAtEnd())
                    Advance();
                if (Check(TokenType.GreaterThan))
                    Advance();
                SkipNewlines();
            }

            // Handle access modifiers (Public, Private, Friend)
            var access = AccessModifier.Private; // Default
            if (Check(TokenType.Public) || Check(TokenType.Private) || Check(TokenType.Friend))
            {
                if (Match(TokenType.Public)) access = AccessModifier.Public;
                else if (Match(TokenType.Private)) access = AccessModifier.Private;
                else if (Match(TokenType.Friend)) access = AccessModifier.Friend;
                SkipNewlines(); // Skip any whitespace after access modifier
            }

            // Handle Async/Iterator modifiers
            if (Check(TokenType.Async) || Check(TokenType.Iterator))
            {
                bool isAsync = false;
                bool isIterator = false;
                while (Check(TokenType.Async) || Check(TokenType.Iterator))
                {
                    if (Match(TokenType.Async)) isAsync = true;
                    if (Match(TokenType.Iterator)) isIterator = true;
                }
                if (Check(TokenType.Function))
                {
                    var func = ParseFunction();
                    func.IsAsync = isAsync;
                    func.IsIterator = isIterator;
                    func.Access = access;
                    return func;
                }
                if (Check(TokenType.Sub))
                {
                    var sub = ParseSubroutine();
                    sub.IsAsync = isAsync;
                    sub.Access = access;
                    return sub;
                }
            }
            if (Check(TokenType.Function))
            {
                var func = ParseFunction();
                func.Access = access;
                return func;
            }
            if (Check(TokenType.Sub))
            {
                var sub = ParseSubroutine();
                sub.Access = access;
                return sub;
            }
            if (Check(TokenType.Dim))
            {
                var statement = ParseVariableDeclaration();
                if (statement is VariableDeclarationNode varDecl)
                {
                    varDecl.Access = access;
                }
                return statement;
            }
            if (Check(TokenType.Const))
            {
                var statement = ParseConstantDeclaration();
                if (statement is ConstantDeclarationNode constDecl)
                {
                    constDecl.Access = access;
                }
                return statement;
            }
            if (Check(TokenType.Extension))
                return ParseExtensionMethod();

            throw new ParseException(
                $"Unexpected token in module: '{Peek().Lexeme}' ({Peek().Type})",
                Peek(),
                "Inside a Module, valid declarations include: Function, Sub, Dim, Const, Type, Structure, or nested Class.");
        }

        private UsingDirectiveNode ParseUsing()
        {
            var token = Consume(TokenType.Using, "Expected 'Using'");
            var node = new UsingDirectiveNode(token.Line, token.Column);

            // Parse the first identifier
            var firstIdent = Consume(TokenType.Identifier, "Expected namespace name").Lexeme;

            // Check for alias syntax: Using Alias = Namespace.Name
            if (Match(TokenType.Equals))
            {
                node.Alias = firstIdent;
                firstIdent = Consume(TokenType.Identifier, "Expected namespace name after '='").Lexeme;
            }

            // Build the full namespace name (handle dotted names like System.IO.File)
            var namespaceBuilder = new System.Text.StringBuilder(firstIdent);
            while (Match(TokenType.Dot))
            {
                namespaceBuilder.Append('.');
                namespaceBuilder.Append(Consume(TokenType.Identifier, "Expected identifier after '.'").Lexeme);
            }

            node.Namespace = namespaceBuilder.ToString();

            // Detect if this is a .NET namespace
            // Common .NET namespace prefixes
            var netNamespacePrefixes = new[] { "System", "Microsoft", "Windows", "Mono" };
            node.IsNetNamespace = node.Namespace.Contains('.') ||
                                  Array.Exists(netNamespacePrefixes, prefix =>
                                      node.Namespace.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                                      node.Namespace.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase));

            ConsumeNewlines();

            return node;
        }

        private ImportDirectiveNode ParseImport()
        {
            var token = Consume(TokenType.Import, "Expected 'Import'");
            var node = new ImportDirectiveNode(token.Line, token.Column);

            // Check for direct string path: Import "path/to/lib.dll"
            if (Check(TokenType.StringLiteral))
            {
                var pathToken = Consume(TokenType.StringLiteral, "Expected library path");
                node.LibraryPath = pathToken.Lexeme;
                // Module name is derived from the library filename
                node.Module = System.IO.Path.GetFileNameWithoutExtension(pathToken.Lexeme);
            }
            else
            {
                // Import ModuleName or Import ModuleName From "path"
                node.Module = Consume(TokenType.Identifier, "Expected module name").Lexeme;

                // Check for "From" clause: Import ModuleName From "path/to/lib.dll"
                if (Check(TokenType.Identifier) && Peek().Lexeme.Equals("From", System.StringComparison.OrdinalIgnoreCase))
                {
                    Advance(); // consume "From"
                    var pathToken = Consume(TokenType.StringLiteral, "Expected library path after 'From'");
                    node.LibraryPath = pathToken.Lexeme;
                }
            }

            ConsumeNewlines();
            return node;
        }

        // ====================================================================
        // Classes and Interfaces
        // ====================================================================

        private ClassNode ParseClass()
        {
            var token = Consume(TokenType.Class, "Expected 'Class'");
            var node = new ClassNode(token.Line, token.Column);

            node.Name = Consume(TokenType.Identifier, "Expected class name").Lexeme;
            _context.Push($"Class '{node.Name}'");

            try
            {
                // Generic parameters with optional constraints
                // e.g., Class Foo(Of T As Class, U As IComparable)
                if (Match(TokenType.LeftParen) && Check(TokenType.Of))
                {
                    Consume(TokenType.Of, "Expected 'Of'");
                    do
                    {
                        var typeParam = ParseGenericTypeParameter();
                        node.GenericParameters.Add(typeParam.Name);
                        node.GenericTypeParams.Add(typeParam);
                    } while (Match(TokenType.Comma));

                    Consume(TokenType.RightParen, "Expected ')' after generic parameters");
                }

                // Allow Inherits/Implements on same line or next line(s)
                ConsumeNewlines();

                // Inheritance (can appear on its own line)
                if (Match(TokenType.Inherits))
                {
                    node.BaseClass = Consume(TokenType.Identifier, "Expected base class name").Lexeme;
                    ConsumeNewlines();
                }

                // Interfaces (can appear on its own line, supports multiple)
                while (Match(TokenType.Implements))
                {
                    do
                    {
                        node.Interfaces.Add(Consume(TokenType.Identifier, "Expected interface name").Lexeme);
                    } while (Match(TokenType.Comma));
                    ConsumeNewlines();
                }

                // Class members
                while (!Check(TokenType.EndClass) && !IsAtEnd())
                {
                    try
                    {
                        var member = ParseClassMember();
                        if (member != null)
                        {
                            node.Members.Add(member);
                        }
                    }
                    catch (ParseException ex)
                    {
                        RecordError(ex.Message, ex.Token, ex.Suggestion);
                        Synchronize();
                    }
                    SkipNewlines();
                }

                Consume(TokenType.EndClass, "Expected 'End Class'");
            }
            finally
            {
                _context.Pop();
            }

            return node;
        }

        private ASTNode ParseClassMember()
        {
            AccessModifier access = AccessModifier.Public;
            bool isStatic = false;
            bool isVirtual = false;
            bool isOverride = false;
            bool isAbstract = false;
            bool isSealed = false;
            bool isReadOnly = false;
            bool isWriteOnly = false;
            bool isAsync = false;
            bool isIterator = false;

            // Parse modifiers in any order
            while (true)
            {
                if (Check(TokenType.Public))
                {
                    Advance();
                    access = AccessModifier.Public;
                }
                else if (Check(TokenType.Private))
                {
                    Advance();
                    access = AccessModifier.Private;
                }
                else if (Check(TokenType.Protected))
                {
                    Advance();
                    access = AccessModifier.Protected;
                }
                else if (Check(TokenType.Shared))
                {
                    Advance();
                    isStatic = true;
                }
                else if (Check(TokenType.Overridable))
                {
                    Advance();
                    isVirtual = true;
                }
                else if (Check(TokenType.Overrides))
                {
                    Advance();
                    isOverride = true;
                }
                else if (Check(TokenType.MustOverride))
                {
                    Advance();
                    isAbstract = true;
                }
                else if (Check(TokenType.NotOverridable))
                {
                    Advance();
                    isSealed = true;
                }
                else if (Check(TokenType.ReadOnly))
                {
                    Advance();
                    isReadOnly = true;
                }
                else if (Check(TokenType.WriteOnly))
                {
                    Advance();
                    isWriteOnly = true;
                }
                else if (Check(TokenType.Async))
                {
                    Advance();
                    isAsync = true;
                }
                else if (Check(TokenType.Iterator))
                {
                    Advance();
                    isIterator = true;
                }
                else if (Check(TokenType.Widening))
                {
                    Advance();
                    isReadOnly = true;  // Reuse for Widening
                }
                else if (Check(TokenType.Narrowing))
                {
                    Advance();
                    isWriteOnly = true;  // Reuse for Narrowing
                }
                else
                {
                    break;
                }
            }

            // Property declaration
            if (Check(TokenType.Property))
            {
                var prop = ParseProperty();
                prop.Access = access;
                prop.IsStatic = isStatic;
                prop.IsReadOnly = isReadOnly;
                prop.IsWriteOnly = isWriteOnly;
                return prop;
            }

            // Event declaration
            if (Check(TokenType.Event))
            {
                var evt = ParseEventDeclaration();
                evt.Access = access;
                return evt;
            }

            // Operator overload declaration
            if (Check(TokenType.Operator))
            {
                var op = ParseOperatorDeclaration();
                op.Access = access;
                op.IsWidening = isReadOnly;   // Widening modifier reuses ReadOnly position
                op.IsNarrowing = isWriteOnly; // Narrowing modifier reuses WriteOnly position
                return op;
            }

            // Function declaration
            if (Check(TokenType.Function))
            {
                var func = ParseFunction();
                func.Access = access;
                func.IsStatic = isStatic;
                func.IsVirtual = isVirtual;
                func.IsOverride = isOverride;
                func.IsAbstract = isAbstract;
                func.IsSealed = isSealed;
                func.IsAsync = isAsync;
                func.IsIterator = isIterator;
                return func;
            }

            // Sub declaration - could be constructor (Sub New)
            if (Check(TokenType.Sub))
            {
                // Peek ahead to see if this is Sub New (constructor)
                if (PeekNext().Type == TokenType.New)
                {
                    var ctor = ParseConstructor();
                    ctor.Access = access;
                    return ctor;
                }

                var sub = ParseSubroutine();
                sub.Access = access;
                sub.IsStatic = isStatic;
                sub.IsVirtual = isVirtual;
                sub.IsOverride = isOverride;
                sub.IsAbstract = isAbstract;
                sub.IsSealed = isSealed;
                sub.IsAsync = isAsync;
                return sub;
            }

            // Field declaration with Dim
            if (Check(TokenType.Dim))
            {
                var field = ParseVariableDeclaration();
                if (field is VariableDeclarationNode varDecl)
                {
                    varDecl.Access = access;
                    varDecl.IsStatic = isStatic;
                }
                return field;
            }

            // Field declaration without Dim (e.g., "Private _name As String" or "Private items(10) As Integer")
            // If we see an identifier followed by As or ( or [ (array), it's a field
            if (Check(TokenType.Identifier))
            {
                var nextType = PeekNext().Type;
                if (nextType == TokenType.As || nextType == TokenType.LeftBracket || nextType == TokenType.LeftParen)
                {
                    var token = Peek();
                    var name = Advance().Value.ToString();

                    // Parse array dimensions if present (support both [] and () syntax)
                    var arrayDimensions = new List<int>();
                    if (Match(TokenType.LeftParen))
                    {
                        // VB-style array: items(100)
                        if (Check(TokenType.IntegerLiteral))
                        {
                            arrayDimensions.Add(int.Parse(Advance().Value.ToString()));
                        }
                        Consume(TokenType.RightParen, "Expected ')' after array dimension");
                    }
                    while (Match(TokenType.LeftBracket))
                    {
                        if (Check(TokenType.IntegerLiteral))
                        {
                            arrayDimensions.Add(int.Parse(Advance().Value.ToString()));
                        }
                        Consume(TokenType.RightBracket, "Expected ']' after array dimension");
                    }

                    Consume(TokenType.As, "Expected 'As' in field declaration");

                    var field = new VariableDeclarationNode(token.Line, token.Column)
                    {
                        Name = name,
                        Type = ParseTypeReference(),
                        Access = access,
                        IsStatic = isStatic
                    };

                    // Add array dimensions to type
                    if (arrayDimensions.Count > 0)
                    {
                        field.Type.ArrayDimensions = arrayDimensions;
                    }

                    // Optional initializer
                    if (Match(TokenType.Equal))
                    {
                        field.Initializer = ParseExpression();
                    }

                    return field;
                }
            }

            throw new ParseException(
                $"Unexpected token in class: '{Peek().Lexeme}' ({Peek().Type})",
                Peek(),
                "Inside a Class, valid members include: Function, Sub, Property, Event, Field declarations (Dim), Const, or nested Class.");
        }

        private ConstructorNode ParseConstructor()
        {
            var token = Consume(TokenType.Sub, "Expected 'Sub'");
            Consume(TokenType.New, "Expected 'New'");
            var node = new ConstructorNode(token.Line, token.Column);

            // Parameters
            if (Match(TokenType.LeftParen))
            {
                if (!Check(TokenType.RightParen))
                {
                    do
                    {
                        node.Parameters.Add(ParseParameter());
                    } while (Match(TokenType.Comma));
                }
                Consume(TokenType.RightParen, "Expected ')' after parameters");
            }

            ConsumeNewlines();

            // Check for MyBase.New() call as first statement
            if (Check(TokenType.MyBase))
            {
                Advance();  // consume MyBase
                Consume(TokenType.Dot, "Expected '.' after MyBase");
                Consume(TokenType.New, "Expected 'New' after MyBase.");
                Consume(TokenType.LeftParen, "Expected '(' after MyBase.New");

                if (!Check(TokenType.RightParen))
                {
                    do
                    {
                        node.BaseConstructorArgs.Add(ParseExpression());
                    } while (Match(TokenType.Comma));
                }

                Consume(TokenType.RightParen, "Expected ')' after arguments");
                ConsumeNewlines();
            }

            // Parse body
            node.Body = ParseBlock(TokenType.EndSub);
            Consume(TokenType.EndSub, "Expected 'End Sub'");

            return node;
        }

        private PropertyNode ParseProperty()
        {
            var token = Consume(TokenType.Property, "Expected 'Property'");
            var node = new PropertyNode(token.Line, token.Column);

            node.Name = Consume(TokenType.Identifier, "Expected property name").Lexeme;

            // Property type
            if (Match(TokenType.As))
            {
                node.PropertyType = ParseTypeReference();
            }

            ConsumeNewlines();

            // Parse Get and Set blocks
            while (!Check(TokenType.EndProperty) && !IsAtEnd())
            {
                if (Check(TokenType.Get))
                {
                    Consume(TokenType.Get, "Expected 'Get'");
                    ConsumeNewlines();
                    node.Getter = ParseBlock(TokenType.EndGet);
                    Consume(TokenType.EndGet, "Expected 'End Get'");
                    ConsumeNewlines();
                }
                else if (Check(TokenType.Set))
                {
                    Consume(TokenType.Set, "Expected 'Set'");

                    // Optional setter parameter: Set(value As Type)
                    if (Match(TokenType.LeftParen))
                    {
                        node.SetterParameter = ParseParameter();
                        Consume(TokenType.RightParen, "Expected ')' after setter parameter");
                    }

                    ConsumeNewlines();
                    node.Setter = ParseBlock(TokenType.EndSet);
                    Consume(TokenType.EndSet, "Expected 'End Set'");
                    ConsumeNewlines();
                }
                else
                {
                    break;
                }
            }

            Consume(TokenType.EndProperty, "Expected 'End Property'");

            return node;
        }

        private EventDeclarationNode ParseEventDeclaration()
        {
            var token = Consume(TokenType.Event, "Expected 'Event'");
            var node = new EventDeclarationNode(token.Line, token.Column);

            node.Name = Consume(TokenType.Identifier, "Expected event name").Lexeme;

            // Event can have delegate type: Event Click As EventHandler
            // Or inline signature: Event Click(sender As Object, args As String)
            if (Match(TokenType.As))
            {
                node.EventType = ParseTypeReference();
            }

            return node;
        }

        private OperatorDeclarationNode ParseOperatorDeclaration()
        {
            var token = Consume(TokenType.Operator, "Expected 'Operator'");
            var node = new OperatorDeclarationNode(token.Line, token.Column);
            node.IsShared = true;  // Operators are always Shared/static

            // Parse operator symbol: +, -, *, /, \, ^, &, Mod, Like, =, <>, <, >, <=, >=, And, Or, Xor, Not, IsTrue, IsFalse, CType
            if (Check(TokenType.Plus))
            {
                Advance();
                node.OperatorSymbol = "+";
            }
            else if (Check(TokenType.Minus))
            {
                Advance();
                node.OperatorSymbol = "-";
            }
            else if (Check(TokenType.Multiply))
            {
                Advance();
                node.OperatorSymbol = "*";
            }
            else if (Check(TokenType.Divide))
            {
                Advance();
                node.OperatorSymbol = "/";
            }
            else if (Check(TokenType.IntegerDivide))
            {
                Advance();
                node.OperatorSymbol = "\\";
            }
            else if (Check(TokenType.Caret))
            {
                Advance();
                node.OperatorSymbol = "^";
            }
            else if (Check(TokenType.Concatenate))
            {
                Advance();
                node.OperatorSymbol = "&";
            }
            else if (Check(TokenType.Modulo))
            {
                Advance();
                node.OperatorSymbol = "Mod";
            }
            else if (Check(TokenType.Equal))
            {
                Advance();
                node.OperatorSymbol = "=";
            }
            else if (Check(TokenType.NotEqual))
            {
                Advance();
                node.OperatorSymbol = "<>";
            }
            else if (Check(TokenType.LessThan))
            {
                Advance();
                node.OperatorSymbol = "<";
            }
            else if (Check(TokenType.GreaterThan))
            {
                Advance();
                node.OperatorSymbol = ">";
            }
            else if (Check(TokenType.LessThanOrEqual))
            {
                Advance();
                node.OperatorSymbol = "<=";
            }
            else if (Check(TokenType.GreaterThanOrEqual))
            {
                Advance();
                node.OperatorSymbol = ">=";
            }
            else if (Check(TokenType.And))
            {
                Advance();
                node.OperatorSymbol = "And";
            }
            else if (Check(TokenType.Or))
            {
                Advance();
                node.OperatorSymbol = "Or";
            }
            else if (Check(TokenType.BitwiseXor))
            {
                Advance();
                node.OperatorSymbol = "Xor";
            }
            else if (Check(TokenType.Not))
            {
                Advance();
                node.OperatorSymbol = "Not";
            }
            else if (Check(TokenType.Identifier))
            {
                // Handle Xor, Like, CType, IsTrue, IsFalse as identifiers
                var opName = Advance().Lexeme;
                if (opName == "Xor" || opName == "Like" || opName == "CType" || opName == "IsTrue" || opName == "IsFalse")
                {
                    node.OperatorSymbol = opName;
                }
                else
                {
                    throw new ParseException($"Unknown operator: {opName}", token);
                }
            }
            else
            {
                throw new ParseException($"Expected operator symbol after 'Operator'", Peek());
            }

            // Parse parameters
            Consume(TokenType.LeftParen, "Expected '(' after operator");
            if (!Check(TokenType.RightParen))
            {
                do
                {
                    var param = new ParameterNode(Peek().Line, Peek().Column);
                    param.Name = Consume(TokenType.Identifier, "Expected parameter name").Lexeme;
                    Consume(TokenType.As, "Expected 'As' after parameter name");
                    param.Type = ParseTypeReference();
                    node.Parameters.Add(param);
                } while (Match(TokenType.Comma));
            }
            Consume(TokenType.RightParen, "Expected ')' after parameters");

            // Parse return type
            Consume(TokenType.As, "Expected 'As' after operator parameters");
            node.ReturnType = ParseTypeReference();
            ConsumeNewlines();

            // Parse body
            node.Body = ParseBlock(TokenType.EndOperator);
            Consume(TokenType.EndOperator, "Expected 'End Operator'");

            return node;
        }

        private InterfaceNode ParseInterface()
        {
            var token = Consume(TokenType.Interface, "Expected 'Interface'");
            var node = new InterfaceNode(token.Line, token.Column);

            node.Name = Consume(TokenType.Identifier, "Expected interface name").Lexeme;
            ConsumeNewlines();

            while (!Check(TokenType.EndInterface) && !IsAtEnd())
            {
                if (Check(TokenType.Function))
                {
                    var method = ParseInterfaceFunction();
                    method.IsAbstract = true;
                    node.Methods.Add(method);
                }
                else if (Check(TokenType.Sub))
                {
                    var method = ParseInterfaceSub();
                    method.IsAbstract = true;
                    node.Methods.Add(method);
                }
                SkipNewlines();
            }

            Consume(TokenType.EndInterface, "Expected 'End Interface'");
            return node;
        }

        private FunctionNode ParseInterfaceFunction()
        {
            var token = Consume(TokenType.Function, "Expected 'Function'");
            var node = new FunctionNode(token.Line, token.Column);

            node.Name = Consume(TokenType.Identifier, "Expected function name").Lexeme;

            Consume(TokenType.LeftParen, "Expected '(' after function name");
            if (!Check(TokenType.RightParen))
            {
                do
                {
                    node.Parameters.Add(ParseParameter());
                } while (Match(TokenType.Comma));
            }
            Consume(TokenType.RightParen, "Expected ')' after parameters");

            if (Match(TokenType.As))
            {
                node.ReturnType = ParseTypeReference();
            }

            // Interface methods have no body
            ConsumeNewlines();
            return node;
        }

        private FunctionNode ParseInterfaceSub()
        {
            var token = Consume(TokenType.Sub, "Expected 'Sub'");
            var node = new FunctionNode(token.Line, token.Column);

            node.Name = Consume(TokenType.Identifier, "Expected sub name").Lexeme;

            Consume(TokenType.LeftParen, "Expected '(' after sub name");
            if (!Check(TokenType.RightParen))
            {
                do
                {
                    node.Parameters.Add(ParseParameter());
                } while (Match(TokenType.Comma));
            }
            Consume(TokenType.RightParen, "Expected ')' after parameters");

            node.ReturnType = new TypeReference("Void");

            // Interface methods have no body
            ConsumeNewlines();
            return node;
        }

        // ====================================================================
        // Enums
        // ====================================================================

        private EnumNode ParseEnum()
        {
            var token = Consume(TokenType.Enum, "Expected 'Enum'");
            var node = new EnumNode(token.Line, token.Column);

            node.Name = Consume(TokenType.Identifier, "Expected enum name").Lexeme;

            // Optional underlying type: Enum Color As Integer
            if (Match(TokenType.As))
            {
                node.UnderlyingType = ParseTypeReference();
            }

            ConsumeNewlines();

            // Parse enum members
            long nextValue = 0;
            while (!Check(TokenType.EndEnum) && !IsAtEnd())
            {
                SkipNewlines();
                if (Check(TokenType.EndEnum)) break;

                var memberToken = Consume(TokenType.Identifier, "Expected enum member name");
                var member = new EnumMemberNode(memberToken.Line, memberToken.Column)
                {
                    Name = memberToken.Lexeme
                };

                // Optional explicit value: Red = 1
                if (Match(TokenType.Assignment))
                {
                    member.Value = ParseExpression();
                }

                node.Members.Add(member);
                SkipNewlines();
            }

            Consume(TokenType.EndEnum, "Expected 'End Enum'");
            return node;
        }

        // ====================================================================
        // Types and Structures
        // ====================================================================

        private TypeNode ParseType()
        {
            var token = Consume(TokenType.Type, "Expected 'Type'");
            var node = new TypeNode(token.Line, token.Column);

            node.Name = Consume(TokenType.Identifier, "Expected type name").Lexeme;
            ConsumeNewlines();

            while (!Check(TokenType.EndType) && !IsAtEnd())
            {
                if (Check(TokenType.Identifier))
                {
                    var member = new VariableDeclarationNode(Peek().Line, Peek().Column);
                    member.Name = Consume(TokenType.Identifier, "Expected member name").Lexeme;
                    Consume(TokenType.As, "Expected 'As'");
                    member.Type = ParseTypeReference();
                    node.Members.Add(member);
                }
                SkipNewlines();
            }

            Consume(TokenType.EndType, "Expected 'End Type'");
            return node;
        }

        private StructureNode ParseStructure()
        {
            var token = Consume(TokenType.Structure, "Expected 'Structure'");
            var node = new StructureNode(token.Line, token.Column);

            node.Name = Consume(TokenType.Identifier, "Expected structure name").Lexeme;
            ConsumeNewlines();

            while (!Check(TokenType.EndStructure) && !IsAtEnd())
            {
                if (Check(TokenType.Identifier))
                {
                    var member = new VariableDeclarationNode(Peek().Line, Peek().Column);
                    member.Name = Consume(TokenType.Identifier, "Expected member name").Lexeme;
                    Consume(TokenType.As, "Expected 'As'");
                    member.Type = ParseTypeReference();
                    node.Members.Add(member);
                }
                SkipNewlines();
            }

            Consume(TokenType.EndStructure, "Expected 'End Structure'");
            return node;
        }

        private UnionNode ParseUnion()
        {
            var token = Consume(TokenType.Union, "Expected 'Union'");
            var node = new UnionNode(token.Line, token.Column);

            node.Name = Consume(TokenType.Identifier, "Expected union name").Lexeme;
            ConsumeNewlines();

            while (!Check(TokenType.EndUnion) && !IsAtEnd())
            {
                if (Check(TokenType.Identifier))
                {
                    var member = new VariableDeclarationNode(Peek().Line, Peek().Column);
                    member.Name = Consume(TokenType.Identifier, "Expected member name").Lexeme;
                    Consume(TokenType.As, "Expected 'As'");
                    member.Type = ParseTypeReference();
                    node.Members.Add(member);
                }
                SkipNewlines();
            }

            Consume(TokenType.EndUnion, "Expected 'End Union'");
            return node;
        }

        private TypeDefineNode ParseTypeDefine()
        {
            var token = Consume(TokenType.TypeDefine, "Expected 'TypeDefine'");
            var node = new TypeDefineNode(token.Line, token.Column);

            node.AliasName = Consume(TokenType.Identifier, "Expected alias name").Lexeme;
            Consume(TokenType.As, "Expected 'As'");
            node.BaseType = ParseTypeReference();

            return node;
        }

        // ====================================================================
        // Templates and Delegates
        // ====================================================================

        private TemplateDeclarationNode ParseTemplate()
        {
            var token = Consume(TokenType.Template, "Expected 'Template'");
            var node = new TemplateDeclarationNode(token.Line, token.Column);

            // Template Function or Template Class
            if (Check(TokenType.Function) || Check(TokenType.Class))
            {
                if (Check(TokenType.Function))
                {
                    Advance();
                    var func = new FunctionNode(token.Line, token.Column);
                    func.Name = Consume(TokenType.Identifier, "Expected function name").Lexeme;

                    // Generic parameters
                    if (Match(TokenType.LeftParen) && Check(TokenType.Of))
                    {
                        Consume(TokenType.Of, "Expected 'Of'");
                        do
                        {
                            node.TypeParameters.Add(Consume(TokenType.Identifier, "Expected type parameter").Lexeme);
                        } while (Match(TokenType.Comma));

                        Consume(TokenType.RightParen, "Expected ')' after generic parameters");
                    }

                    // Regular parameters
                    if (Match(TokenType.LeftParen))
                    {
                        if (!Check(TokenType.RightParen))
                        {
                            do
                            {
                                func.Parameters.Add(ParseParameter());
                            } while (Match(TokenType.Comma));
                        }
                        Consume(TokenType.RightParen, "Expected ')' after parameters");
                    }

                    // Return type
                    if (Match(TokenType.As))
                    {
                        func.ReturnType = ParseTypeReference();
                    }

                    ConsumeNewlines();
                    func.Body = ParseBlock(TokenType.EndFunction);
                    Consume(TokenType.EndFunction, "Expected 'End Function'");

                    node.Declaration = func;
                }
                else // Template Class
                {
                    node.Declaration = ParseClass();
                }
            }

            return node;
        }

        private DelegateDeclarationNode ParseDelegate()
        {
            var token = Consume(TokenType.Delegate, "Expected 'Delegate'");
            var node = new DelegateDeclarationNode(token.Line, token.Column);

            if (Match(TokenType.Function))
            {
                node.Name = Consume(TokenType.Identifier, "Expected delegate name").Lexeme;

                if (Match(TokenType.LeftParen))
                {
                    if (!Check(TokenType.RightParen))
                    {
                        do
                        {
                            node.Parameters.Add(ParseParameter());
                        } while (Match(TokenType.Comma));
                    }
                    Consume(TokenType.RightParen, "Expected ')' after parameters");
                }

                if (Match(TokenType.As))
                {
                    node.ReturnType = ParseTypeReference();
                }
            }
            else if (Match(TokenType.Sub))
            {
                node.Name = Consume(TokenType.Identifier, "Expected delegate name").Lexeme;

                if (Match(TokenType.LeftParen))
                {
                    if (!Check(TokenType.RightParen))
                    {
                        do
                        {
                            node.Parameters.Add(ParseParameter());
                        } while (Match(TokenType.Comma));
                    }
                    Consume(TokenType.RightParen, "Expected ')' after parameters");
                }
            }

            return node;
        }

        /// <summary>
        /// Parse an extern declaration with platform-specific implementations
        /// </summary>
        /// <remarks>
        /// Syntax:
        /// Extern Function Name(params) As ReturnType
        ///     CSharp: "implementation"
        ///     Cpp: "implementation"
        ///     LLVM: "implementation"
        ///     MSIL: "implementation"
        /// End Extern
        ///
        /// Or for Sub:
        /// Extern Sub Name(params)
        ///     CSharp: "implementation"
        /// End Extern
        /// </remarks>
        private ASTNode ParseExtern()
        {
            var token = Consume(TokenType.Extern, "Expected 'Extern'");

            // Check if this is an extern variable (Extern Dim)
            if (Match(TokenType.Dim))
            {
                var varNode = new VariableDeclarationNode(token.Line, token.Column);
                varNode.IsExtern = true;
                varNode.Name = Consume(TokenType.Identifier, "Expected variable name").Lexeme;

                if (Match(TokenType.As))
                {
                    varNode.Type = ParseTypeReference();
                }
                else
                {
                    // Default to Object if no type specified
                    varNode.Type = new TypeReference("Object");
                }

                ConsumeNewlines();
                return varNode;
            }

            var node = new ExternDeclarationNode(token.Line, token.Column);

            if (Match(TokenType.Function))
            {
                node.IsFunction = true;
                node.Name = Consume(TokenType.Identifier, "Expected extern function name").Lexeme;

                // Parse parameters
                if (Match(TokenType.LeftParen))
                {
                    if (!Check(TokenType.RightParen))
                    {
                        do
                        {
                            node.Parameters.Add(ParseParameter());
                        } while (Match(TokenType.Comma));
                    }
                    Consume(TokenType.RightParen, "Expected ')' after parameters");
                }

                // Parse return type
                if (Match(TokenType.As))
                {
                    node.ReturnType = ParseTypeReference();
                }
            }
            else if (Match(TokenType.Sub))
            {
                node.IsFunction = false;
                node.Name = Consume(TokenType.Identifier, "Expected extern sub name").Lexeme;

                // Parse parameters
                if (Match(TokenType.LeftParen))
                {
                    if (!Check(TokenType.RightParen))
                    {
                        do
                        {
                            node.Parameters.Add(ParseParameter());
                        } while (Match(TokenType.Comma));
                    }
                    Consume(TokenType.RightParen, "Expected ')' after parameters");
                }
            }
            else
            {
                throw new ParseException("Expected 'Function', 'Sub', or 'Dim' after 'Extern'", Peek());
            }

            SkipNewlines();

            // Parse platform implementations
            // Format: PlatformName: "implementation string"
            while (!Check(TokenType.EndExtern) && !IsAtEnd())
            {
                SkipNewlines();

                if (Check(TokenType.EndExtern))
                    break;

                // Parse platform name (identifier followed by colon)
                var platformToken = Consume(TokenType.Identifier, "Expected platform name (CSharp, Cpp, LLVM, MSIL)");
                var platformName = platformToken.Lexeme;

                Consume(TokenType.Colon, $"Expected ':' after platform name '{platformName}'");

                // Parse implementation string
                var implToken = Consume(TokenType.StringLiteral, $"Expected string literal for {platformName} implementation");
                var implementation = implToken.Lexeme;

                // Remove quotes from the string literal
                if (implementation.StartsWith("\"") && implementation.EndsWith("\""))
                {
                    implementation = implementation.Substring(1, implementation.Length - 2);
                }

                node.PlatformImplementations[platformName] = implementation;

                SkipNewlines();
            }

            Consume(TokenType.EndExtern, "Expected 'End Extern'");

            return node;
        }

        /// <summary>
        /// Parse a Declare statement for C library interop
        /// </summary>
        /// <remarks>
        /// Syntax:
        /// Declare [CDecl|StdCall] Function Name Lib "library.dll" [Alias "realname"] (params) As ReturnType
        /// Declare [CDecl|StdCall] Sub Name Lib "library.dll" [Alias "realname"] (params)
        /// </remarks>
        private DeclareNode ParseDeclare()
        {
            var token = Consume(TokenType.Declare, "Expected 'Declare'");
            var node = new DeclareNode(token.Line, token.Column);

            // Check for optional calling convention
            if (Match(TokenType.CDecl))
            {
                node.Convention = CallingConvention.CDecl;
            }
            else if (Match(TokenType.StdCall))
            {
                node.Convention = CallingConvention.StdCall;
            }
            else
            {
                node.Convention = CallingConvention.Default;
            }

            // Parse Function or Sub
            if (Match(TokenType.Function))
            {
                node.IsFunction = true;
            }
            else if (Match(TokenType.Sub))
            {
                node.IsFunction = false;
            }
            else
            {
                throw new ParseException("Expected 'Function' or 'Sub' after 'Declare'", Peek());
            }

            // Parse function/sub name
            node.Name = Consume(TokenType.Identifier, "Expected function/sub name").Lexeme;

            // Parse Lib clause
            Consume(TokenType.Lib, "Expected 'Lib' keyword");
            var libToken = Consume(TokenType.StringLiteral, "Expected library name string");
            node.LibraryName = libToken.Lexeme;
            if (node.LibraryName.StartsWith("\"") && node.LibraryName.EndsWith("\""))
            {
                node.LibraryName = node.LibraryName.Substring(1, node.LibraryName.Length - 2);
            }

            // Parse optional Alias clause
            if (Match(TokenType.Alias))
            {
                var aliasToken = Consume(TokenType.StringLiteral, "Expected alias name string");
                node.AliasName = aliasToken.Lexeme;
                if (node.AliasName.StartsWith("\"") && node.AliasName.EndsWith("\""))
                {
                    node.AliasName = node.AliasName.Substring(1, node.AliasName.Length - 2);
                }
            }

            // Parse parameters
            if (Match(TokenType.LeftParen))
            {
                if (!Check(TokenType.RightParen))
                {
                    do
                    {
                        node.Parameters.Add(ParseParameter());
                    } while (Match(TokenType.Comma));
                }
                Consume(TokenType.RightParen, "Expected ')' after parameters");
            }

            // Parse return type for functions
            if (node.IsFunction)
            {
                if (Match(TokenType.As))
                {
                    node.ReturnType = ParseTypeReference();
                }
                else
                {
                    // Default return type if not specified
                    node.ReturnType = new TypeReference("Object");
                }
            }

            return node;
        }

        private ExtensionMethodNode ParseExtensionMethod()
        {
            var token = Consume(TokenType.Extension, "Expected 'Extension'");
            var node = new ExtensionMethodNode(token.Line, token.Column);

            Consume(TokenType.Function, "Expected 'Function' after 'Extension'");

            var func = new FunctionNode(token.Line, token.Column);

            // Check for traditional syntax: Extension Function String.Reverse()
            // vs simpler syntax: Extension Function IsNullOrEmpty(s As String)
            var firstIdent = Consume(TokenType.Identifier, "Expected method name or type").Lexeme;

            if (Check(TokenType.Dot))
            {
                // Traditional syntax: TypeName.MethodName
                Advance(); // consume dot
                node.ExtendedType = firstIdent;
                func.Name = Consume(TokenType.Identifier, "Expected method name").Lexeme;
            }
            else
            {
                // Simpler syntax: MethodName(self As Type, ...)
                // The extended type comes from the first parameter
                func.Name = firstIdent;
            }

            if (Match(TokenType.LeftParen))
            {
                if (!Check(TokenType.RightParen))
                {
                    do
                    {
                        func.Parameters.Add(ParseParameter());
                    } while (Match(TokenType.Comma));
                }
                Consume(TokenType.RightParen, "Expected ')' after parameters");
            }

            // For simpler syntax, the extended type is the first parameter's type
            if (string.IsNullOrEmpty(node.ExtendedType) && func.Parameters.Count > 0)
            {
                node.ExtendedType = func.Parameters[0].Type?.Name ?? "Object";
            }

            if (Match(TokenType.As))
            {
                func.ReturnType = ParseTypeReference();
            }

            ConsumeNewlines();
            func.Body = ParseBlock(TokenType.EndFunction);
            Consume(TokenType.EndFunction, "Expected 'End Function'");

            node.Method = func;
            return node;
        }

        private ExtensionMethodNode ParseExtensionMethodFromAttribute()
        {
            // Called after <Extension> attribute is consumed
            var token = Consume(TokenType.Function, "Expected 'Function' after <Extension>");
            var node = new ExtensionMethodNode(token.Line, token.Column);

            // Parse extended type (e.g., String.Reverse()) - can be identifier or type keyword
            if (Check(TokenType.Identifier))
            {
                node.ExtendedType = Advance().Lexeme;
            }
            else if (Check(TokenType.String) || Check(TokenType.Integer) || Check(TokenType.Double) ||
                     Check(TokenType.Boolean) || Check(TokenType.Long) || Check(TokenType.Single))
            {
                node.ExtendedType = Advance().Lexeme;
            }
            else
            {
                throw new ParseException($"Expected type name but found {Peek().Type}", Peek());
            }
            Consume(TokenType.Dot, "Expected '.' after type name");

            var func = new FunctionNode(token.Line, token.Column);
            func.Name = Consume(TokenType.Identifier, "Expected method name").Lexeme;

            if (Match(TokenType.LeftParen))
            {
                if (!Check(TokenType.RightParen))
                {
                    do
                    {
                        func.Parameters.Add(ParseParameter());
                    } while (Match(TokenType.Comma));
                }
                Consume(TokenType.RightParen, "Expected ')' after parameters");
            }

            if (Match(TokenType.As))
            {
                func.ReturnType = ParseTypeReference();
            }

            ConsumeNewlines();
            func.Body = ParseBlock(TokenType.EndFunction);
            Consume(TokenType.EndFunction, "Expected 'End Function'");

            node.Method = func;
            return node;
        }

        // ====================================================================
        // Functions and Subroutines
        // ====================================================================

        private FunctionNode ParseFunction()
        {
            var token = Consume(TokenType.Function, "Expected 'Function'");
            var node = new FunctionNode(token.Line, token.Column);

            node.Name = Consume(TokenType.Identifier, "Expected function name").Lexeme;
            _context.Push($"Function '{node.Name}'");

            try
            {
                // Generic type parameters: Function Foo(Of T, U)(...)
                // With optional constraints: Function Foo(Of T As Class, U As IComparable)(...)
                if (Check(TokenType.LeftParen) && PeekNext().Type == TokenType.Of)
                {
                    Advance(); // consume '('
                    Consume(TokenType.Of, "Expected 'Of'");
                    do
                    {
                        var typeParam = ParseGenericTypeParameter();
                        node.GenericParameters.Add(typeParam.Name);
                        node.GenericTypeParams.Add(typeParam);
                    } while (Match(TokenType.Comma));
                    Consume(TokenType.RightParen, "Expected ')' after generic parameters");
                }

                // Parameters
                if (Match(TokenType.LeftParen))
                {
                    if (!Check(TokenType.RightParen))
                    {
                        do
                        {
                            node.Parameters.Add(ParseParameter());
                        } while (Match(TokenType.Comma));
                    }
                    Consume(TokenType.RightParen, "Expected ')' after parameters");
                }

                // Return type
                if (Match(TokenType.As))
                {
                    node.ReturnType = ParseTypeReference();
                }

                // Implements clause
                if (Match(TokenType.Implements))
                {
                    node.ImplementsInterface = Consume(TokenType.Identifier, "Expected interface name").Lexeme;
                    Consume(TokenType.Dot, "Expected '.'");
                    node.ImplementsInterface += "." + Consume(TokenType.Identifier, "Expected method name").Lexeme;
                }

                ConsumeNewlines();

                // Body (if not abstract)
                if (!node.IsAbstract)
                {
                    node.Body = ParseBlock(TokenType.EndFunction);
                    Consume(TokenType.EndFunction, "Expected 'End Function'");
                }
            }
            finally
            {
                _context.Pop();
            }

            return node;
        }

        private SubroutineNode ParseSubroutine()
        {
            var token = Consume(TokenType.Sub, "Expected 'Sub'");
            var node = new SubroutineNode(token.Line, token.Column);

            node.Name = Consume(TokenType.Identifier, "Expected subroutine name").Lexeme;
            _context.Push($"Sub '{node.Name}'");

            try
            {
                // Generic type parameters: Sub Foo(Of T, U)(...)
                // With optional constraints: Sub Foo(Of T As Class, U As IComparable)(...)
                if (Check(TokenType.LeftParen) && PeekNext().Type == TokenType.Of)
                {
                    Advance(); // consume '('
                    Consume(TokenType.Of, "Expected 'Of'");
                    do
                    {
                        var typeParam = ParseGenericTypeParameter();
                        node.GenericParameters.Add(typeParam.Name);
                        node.GenericTypeParams.Add(typeParam);
                    } while (Match(TokenType.Comma));
                    Consume(TokenType.RightParen, "Expected ')' after generic parameters");
                }

                // Parameters
                if (Match(TokenType.LeftParen))
                {
                    if (!Check(TokenType.RightParen))
                    {
                        do
                        {
                            node.Parameters.Add(ParseParameter());
                        } while (Match(TokenType.Comma));
                    }
                    Consume(TokenType.RightParen, "Expected ')' after parameters");
                }

                // Implements clause
                if (Match(TokenType.Implements))
                {
                    node.ImplementsInterface = Consume(TokenType.Identifier, "Expected interface name").Lexeme;
                    Consume(TokenType.Dot, "Expected '.'");
                    node.ImplementsInterface += "." + Consume(TokenType.Identifier, "Expected method name").Lexeme;
                }

                ConsumeNewlines();
                node.Body = ParseBlock(TokenType.EndSub);
                Consume(TokenType.EndSub, "Expected 'End Sub'");
            }
            finally
            {
                _context.Pop();
            }

            return node;
        }

        private ParameterNode ParseParameter()
        {
            var token = Peek();
            var node = new ParameterNode(token.Line, token.Column);

            // Check for Optional keyword
            if (Match(TokenType.Optional))
            {
                node.IsOptional = true;
            }

            // Check for ParamArray keyword
            if (Match(TokenType.ParamArray))
            {
                node.IsParamArray = true;
            }

            // Check for ByVal/ByRef keywords
            if (Match(TokenType.ByRef))
            {
                node.IsByRef = true;
            }
            else if (Match(TokenType.ByVal))
            {
                node.IsByRef = false; // Explicitly ByVal (default)
            }

            node.Name = Consume(TokenType.Identifier, "Expected parameter name").Lexeme;

            // Check for array brackets before 'As'
            bool isArray = false;
            List<int> arrayDimensions = new List<int>();

            while (Match(TokenType.LeftBracket))
            {
                isArray = true;

                if (!Check(TokenType.RightBracket))
                {
                    var sizeExpr = ParseExpression();
                    if (sizeExpr is LiteralExpressionNode literal && literal.Value is int size)
                    {
                        arrayDimensions.Add(size);
                    }
                    else
                    {
                        arrayDimensions.Add(-1); // Dynamic size
                    }
                }
                else
                {
                    arrayDimensions.Add(-1); // No size specified
                }

                Consume(TokenType.RightBracket, "Expected ']'");
            }

            Consume(TokenType.As, "Expected 'As'");
            node.Type = ParseTypeReference();

            // If we had array brackets, mark the type as an array
            if (isArray)
            {
                node.Type.IsArray = true;
                node.Type.ArrayDimensions = arrayDimensions;
            }

            // ParamArray parameters must be array types
            if (node.IsParamArray && !node.Type.IsArray)
            {
                node.Type.IsArray = true;
                node.Type.ArrayDimensions = new List<int> { -1 };
            }

            // Default value (required for Optional, not allowed for ParamArray)
            if (Match(TokenType.Assignment))
            {
                node.DefaultValue = ParseExpression();
                // If has default value, treat as optional
                if (!node.IsOptional && !node.IsParamArray)
                {
                    node.IsOptional = true;
                }
            }

            return node;
        }

        // ====================================================================
        // Variable Declarations
        // ====================================================================

        private StatementNode ParseVariableDeclaration()
        {
            var token = Consume(TokenType.Dim, "Expected 'Dim'");

            // Check for tuple deconstruction: Dim (x, y) = ...
            if (Check(TokenType.LeftParen))
            {
                return ParseTupleDeconstruction(token);
            }

            var node = new VariableDeclarationNode(token.Line, token.Column);

            node.Name = Consume(TokenType.Identifier, "Expected variable name").Lexeme;

            // Array dimensions
            if (Match(TokenType.LeftBracket))
            {
                var arrayType = new TypeReference("Array");
                arrayType.IsArray = true;

                do
                {
                    if (!Check(TokenType.RightBracket))
                    {
                        var sizeExpr = ParseExpression();
                        if (sizeExpr is LiteralExpressionNode literal && literal.Value is int size)
                        {
                            arrayType.ArrayDimensions.Add(size);
                        }
                        else
                        {
                            arrayType.ArrayDimensions.Add(-1); // Dynamic size
                        }
                    }
                    else
                    {
                        arrayType.ArrayDimensions.Add(-1); // No size specified
                    }

                    Consume(TokenType.RightBracket, "Expected ']'");
                } while (Match(TokenType.LeftBracket));

                Consume(TokenType.As, "Expected 'As'");
                var elementType = ParseTypeReference();
                elementType.IsArray = true;
                elementType.ArrayDimensions = arrayType.ArrayDimensions;
                node.Type = elementType;
            }
            else
            {
                Consume(TokenType.As, "Expected 'As'");

                // Handle "As New Type" pattern - creates initializer with New expression
                if (Match(TokenType.New))
                {
                    var newToken = Previous();
                    var newExpr = new NewExpressionNode(newToken.Line, newToken.Column);
                    newExpr.Type = ParseTypeReference();

                    // Check for constructor arguments
                    if (Match(TokenType.LeftParen))
                    {
                        if (!Check(TokenType.RightParen))
                        {
                            do
                            {
                                newExpr.Arguments.Add(ParseExpression());
                            } while (Match(TokenType.Comma));
                        }
                        Consume(TokenType.RightParen, "Expected ')' after arguments");
                    }

                    node.Type = newExpr.Type;
                    node.Initializer = newExpr;
                }
                else
                {
                    node.Type = ParseTypeReference();
                }
            }

            // Initializer (for regular "Dim x As Type = value" pattern)
            if (Match(TokenType.Assignment))
            {
                node.Initializer = ParseExpression();
            }

            return node;
        }

        private VariableDeclarationNode ParseAutoDeclaration()
        {
            var token = Consume(TokenType.Auto, "Expected 'Auto'");
            var node = new VariableDeclarationNode(token.Line, token.Column);
            node.IsAuto = true;

            node.Name = Consume(TokenType.Identifier, "Expected variable name").Lexeme;

            // Initializer is required for Auto, but parse it optionally
            // so semantic analyzer can report a better error
            if (Match(TokenType.Assignment))
            {
                node.Initializer = ParseExpression();
            }
            // If no initializer, node.Initializer remains null
            // and semantic analyzer will report error

            return node;
        }

        /// <summary>
        /// Parse tuple deconstruction: Dim (x, y) = GetPair()
        /// Or with types: Dim (x As Integer, y As String) = GetPair()
        /// </summary>
        private TupleDeconstructionNode ParseTupleDeconstruction(Token dimToken)
        {
            var node = new TupleDeconstructionNode(dimToken.Line, dimToken.Column);

            Consume(TokenType.LeftParen, "Expected '(' for tuple deconstruction");

            do
            {
                var varName = Consume(TokenType.Identifier, "Expected variable name").Lexeme;
                TypeReference varType = null;

                // Optional type: x As Integer
                if (Match(TokenType.As))
                {
                    varType = ParseTypeReference();
                }

                node.Variables.Add((varName, varType));

            } while (Match(TokenType.Comma));

            Consume(TokenType.RightParen, "Expected ')' after tuple variables");
            Consume(TokenType.Assignment, "Expected '=' after tuple deconstruction pattern");
            node.Initializer = ParseExpression();

            return node;
        }

        private ConstantDeclarationNode ParseConstantDeclaration()
        {
            var token = Consume(TokenType.Const, "Expected 'Const'");
            var node = new ConstantDeclarationNode(token.Line, token.Column);

            node.Name = Consume(TokenType.Identifier, "Expected constant name").Lexeme;
            Consume(TokenType.As, "Expected 'As'");
            node.Type = ParseTypeReference();

            // Value is required for Const, but parse it optionally
            // so semantic analyzer can report a better error
            if (Match(TokenType.Assignment))
            {
                node.Value = ParseExpression();
            }
            // If no value, node.Value remains null
            // and semantic analyzer will report error

            return node;
        }

        // ====================================================================
        // Type References
        // ====================================================================

        /// <summary>
        /// Parse a generic type parameter with optional constraints
        /// e.g., T, T As Class, T As IComparable, T As {Class, IComparable, New}
        /// </summary>
        private GenericTypeParameter ParseGenericTypeParameter()
        {
            var name = Consume(TokenType.Identifier, "Expected type parameter name").Lexeme;
            var param = new GenericTypeParameter(name);

            // Check for constraints: As Class, As Structure, As New, As TypeName
            if (Match(TokenType.As))
            {
                // Check for multiple constraints in braces: As {Class, IComparable, New}
                if (Match(TokenType.LeftBrace))
                {
                    do
                    {
                        param.Constraints.Add(ParseSingleConstraint());
                    } while (Match(TokenType.Comma));
                    Consume(TokenType.RightBrace, "Expected '}' after constraint list");
                }
                else
                {
                    // Single constraint
                    param.Constraints.Add(ParseSingleConstraint());
                }
            }

            return param;
        }

        /// <summary>
        /// Parse a single constraint: Class, Structure, New, or a type name
        /// </summary>
        private GenericConstraint ParseSingleConstraint()
        {
            if (Match(TokenType.Class))
                return new GenericConstraint(GenericConstraintKind.Class);

            if (Match(TokenType.Structure))
                return new GenericConstraint(GenericConstraintKind.Structure);

            if (Match(TokenType.New))
                return new GenericConstraint(GenericConstraintKind.New);

            // Must be a type name (interface or base class)
            var typeName = Consume(TokenType.Identifier, "Expected constraint type name").Lexeme;
            return new GenericConstraint(GenericConstraintKind.Type, typeName);
        }

        private TypeReference ParseTypeReference()
        {
            TypeReference type;

            // Tuple type: (Integer, Integer) or (x As Integer, y As String)
            if (Check(TokenType.LeftParen))
            {
                // Look ahead to determine if this is a tuple type or generic arguments
                // Tuple types have the form: (Type, Type, ...) or (name As Type, name As Type, ...)
                int startPos = _current;
                Advance(); // consume '('

                // Check if it looks like a tuple type (not (Of ...))
                if (!Check(TokenType.Of))
                {
                    var tupleType = new TypeReference("Tuple");
                    tupleType.IsTuple = true;

                    do
                    {
                        string elementName = null;

                        // Check for named element: name As Type
                        if (Check(TokenType.Identifier) && PeekNext().Type == TokenType.As)
                        {
                            elementName = Advance().Lexeme;
                            Consume(TokenType.As, "Expected 'As'");
                        }

                        var elementType = ParseTypeReference();
                        tupleType.TupleElementTypes.Add(elementType);
                        tupleType.TupleElementNames.Add(elementName);

                    } while (Match(TokenType.Comma));

                    Consume(TokenType.RightParen, "Expected ')' after tuple type elements");

                    // Nullable tuple: (Integer, String)?
                    if (Match(TokenType.QuestionMark))
                    {
                        tupleType.IsNullable = true;
                    }

                    return tupleType;
                }
                else
                {
                    // Backtrack - this is not a tuple type
                    _current = startPos;
                }
            }

            // Pointer type
            if (Match(TokenType.Pointer))
            {
                Consume(TokenType.To, "Expected 'To' after 'Pointer'");
                type = ParseTypeReference();
                type.IsPointer = true;
                return type;
            }

            // Base type - accept both type keywords and identifiers
            string typeName;
            if (Check(TokenType.Integer) || Check(TokenType.Long) || Check(TokenType.Single) ||
                Check(TokenType.Double) || Check(TokenType.String) || Check(TokenType.Boolean) ||
                Check(TokenType.Char) || Check(TokenType.Byte) || Check(TokenType.Short) ||
                Check(TokenType.UByte) || Check(TokenType.UShort) || Check(TokenType.UInteger) ||
                Check(TokenType.ULong))
            {
                typeName = Advance().Lexeme;
            }
            else if (Check(TokenType.Identifier))
            {
                // Parse potentially dotted type name (e.g., System.IO.File, System.Text.StringBuilder)
                var typeNameBuilder = new System.Text.StringBuilder(Advance().Lexeme);
                while (Match(TokenType.Dot))
                {
                    typeNameBuilder.Append('.');
                    typeNameBuilder.Append(Consume(TokenType.Identifier, "Expected type name after '.'").Lexeme);
                }
                typeName = typeNameBuilder.ToString();
            }
            else
            {
                throw new ParseException($"Expected type name but found {Peek().Type}", Peek());
            }

            type = new TypeReference(typeName);

            // Fixed-length string: String * 20
            if (typeName.Equals("String", StringComparison.OrdinalIgnoreCase) && Match(TokenType.Multiply))
            {
                if (Check(TokenType.IntegerLiteral))
                {
                    type.IsFixedLengthString = true;
                    type.FixedStringLength = int.Parse(Advance().Value.ToString());
                }
                else
                {
                    throw new ParseException("Expected integer literal after 'String *'", Peek());
                }
            }

            // Generic arguments - only if we see '(' followed by 'Of'
            // Check both before consuming the paren
            if (Check(TokenType.LeftParen) && PeekNext().Type == TokenType.Of)
            {
                Advance();  // consume '('
                Consume(TokenType.Of, "Expected 'Of'");
                do
                {
                    type.GenericArguments.Add(ParseTypeReference());
                } while (Match(TokenType.Comma));

                Consume(TokenType.RightParen, "Expected ')' after generic arguments");
            }

            // Nullable type: Integer?
            if (Match(TokenType.QuestionMark))
            {
                type.IsNullable = true;
            }

            return type;
        }

        // ====================================================================
        // Statements
        // ====================================================================

        private BlockNode ParseBlock(TokenType endToken)
        {
            var block = new BlockNode(Peek().Line, Peek().Column);

            while (!Check(endToken) && !IsAtEnd())
            {
                SkipNewlines();

                if (Check(endToken) || IsAtEnd())
                    break;

                try
                {
                    var statement = ParseStatement();
                    if (statement != null)
                    {
                        block.Statements.Add(statement);
                    }
                }
                catch (ParseException ex)
                {
                    // Error-tolerant: Record error and skip to next statement
                    // This allows IntelliSense to work with partial/incomplete code
                    RecordError(ex.Message, ex.Token, ex.Suggestion);
                    Synchronize();

                    // If we've synchronized to the end token, break out
                    if (Check(endToken))
                        break;
                }

                SkipNewlines();
            }

            return block;
        }

        private StatementNode ParseStatement()
        {
            SkipNewlines();

            if (Check(TokenType.If))
                return ParseIfStatement();
            if (Check(TokenType.Select))
                return ParseSelectStatement();
            if (Check(TokenType.For))
                return ParseForLoop();
            if (Check(TokenType.While))
                return ParseWhileLoop();
            if (Check(TokenType.Do))
                return ParseDoLoop();
            if (Check(TokenType.Try))
                return ParseTryStatement();
            if (Check(TokenType.With))
                return ParseWithStatement();
            if (Check(TokenType.Throw))
                return ParseThrowStatement();
            if (Check(TokenType.Return))
                return ParseReturnStatement();
            if (Check(TokenType.Exit))
                return ParseExitStatement();
            if (Check(TokenType.Dim))
                return ParseVariableDeclaration();
            if (Check(TokenType.Auto))
                return ParseAutoDeclaration();
            if (Check(TokenType.Const))
                return ParseConstantDeclaration();
            if (Check(TokenType.Yield))
                return ParseYieldStatement();
            if (Check(TokenType.RaiseEvent))
                return ParseRaiseEventStatement();
            if (Check(TokenType.AddHandler))
                return ParseAddHandlerStatement();
            if (Check(TokenType.RemoveHandler))
                return ParseRemoveHandlerStatement();
            if (Check(TokenType.InlineCode))
                return ParseInlineCode();

            // Assignment or expression statement
            return ParseAssignmentOrExpressionStatement();
        }

        private YieldStatementNode ParseYieldStatement()
        {
            var token = Consume(TokenType.Yield, "Expected 'Yield'");
            var node = new YieldStatementNode(token.Line, token.Column);

            // Check for Yield Break (for exiting an iterator)
            // Note: VB.NET doesn't have Yield Break, but we support it as an extension
            if (Check(TokenType.Exit))
            {
                Advance();
                node.IsBreak = true;
            }
            else if (Check(TokenType.Return))
            {
                // Yield Return value
                Advance();
                node.Value = ParseExpression();
            }
            else if (!Check(TokenType.Newline) && !IsAtEnd())
            {
                // Yield value (without Return keyword)
                node.Value = ParseExpression();
            }

            return node;
        }

        private InlineCodeNode ParseInlineCode()
        {
            var token = Consume(TokenType.InlineCode, "Expected inline code block");
            var inlineValue = token.Value as InlineCodeValue;

            var node = new InlineCodeNode(token.Line, token.Column)
            {
                Language = inlineValue?.Language ?? "unknown",
                Code = inlineValue?.Code ?? ""
            };

            return node;
        }

        private RaiseEventStatementNode ParseRaiseEventStatement()
        {
            var token = Consume(TokenType.RaiseEvent, "Expected 'RaiseEvent'");
            var node = new RaiseEventStatementNode(token.Line, token.Column);

            node.EventName = Consume(TokenType.Identifier, "Expected event name").Lexeme;

            // Optional arguments: RaiseEvent Click(sender, args)
            if (Match(TokenType.LeftParen))
            {
                if (!Check(TokenType.RightParen))
                {
                    do
                    {
                        node.Arguments.Add(ParseExpression());
                    } while (Match(TokenType.Comma));
                }
                Consume(TokenType.RightParen, "Expected ')' after event arguments");
            }

            return node;
        }

        private AddHandlerStatementNode ParseAddHandlerStatement()
        {
            var token = Consume(TokenType.AddHandler, "Expected 'AddHandler'");
            var node = new AddHandlerStatementNode(token.Line, token.Column);

            // Parse event expression: obj.Event
            node.EventExpression = ParseExpression();

            Consume(TokenType.Comma, "Expected ',' after event expression");

            // Parse handler expression: AddressOf Handler
            node.HandlerExpression = ParseExpression();

            return node;
        }

        private RemoveHandlerStatementNode ParseRemoveHandlerStatement()
        {
            var token = Consume(TokenType.RemoveHandler, "Expected 'RemoveHandler'");
            var node = new RemoveHandlerStatementNode(token.Line, token.Column);

            // Parse event expression: obj.Event
            node.EventExpression = ParseExpression();

            Consume(TokenType.Comma, "Expected ',' after event expression");

            // Parse handler expression: AddressOf Handler
            node.HandlerExpression = ParseExpression();

            return node;
        }

        private IfStatementNode ParseIfStatement()
        {
            var token = Consume(TokenType.If, "Expected 'If'");
            var node = new IfStatementNode(token.Line, token.Column);

            node.Condition = ParseExpression();

            // Check for single-line vs multi-line if
            if (Check(TokenType.Then))
            {
                Consume(TokenType.Then, "Expected 'Then'");

                // ✓ Check if this is a single-line or multi-line if BEFORE consuming newlines
                if (Check(TokenType.Newline))  // ✓ Now checks FIRST
                {
                    // Multi-line if: If condition Then [newline] ... End If
                    ConsumeNewlines();  // ✓ NOW safe to consume
                    node.ThenBlock = ParseBlock(TokenType.Else, TokenType.ElseIf, TokenType.EndIf);

                    // ✓ Handle Else and ElseIf clauses (fully implemented)
                    while (Check(TokenType.ElseIf))
                    {
                        Advance(); // consume ElseIf
                        var elseIfCondition = ParseExpression();
                        Consume(TokenType.Then, "Expected 'Then' after ElseIf condition");
                        ConsumeNewlines();
                        var elseIfBlock = ParseBlock(TokenType.Else, TokenType.ElseIf, TokenType.EndIf);

                        // Add to ElseIfClauses list
                        node.ElseIfClauses.Add((elseIfCondition, elseIfBlock));
                    }

                    // ✓ Handle Else clause (fully implemented)
                    if (Check(TokenType.Else))
                    {
                        Advance(); // consume Else
                        ConsumeNewlines();
                        node.ElseBlock = ParseBlock(TokenType.EndIf);
                    }

                    // ✓ Consume EndIf (fully implemented)
                    Consume(TokenType.EndIf, "Expected 'End If'");
                }
                else
                {
                    // Single-line if: If condition Then statement
                    var statement = ParseStatement();
                    node.ThenBlock = new BlockNode(token.Line, token.Column);
                    node.ThenBlock.Statements.Add(statement);
                }
            }
            else
            {
                throw new ParseException("Expected 'Then' after If condition", Peek(),
                    "Did you forget 'Then' after the If condition?");
            }

            return node;
        }
        private BlockNode ParseBlock(params TokenType[] endTokens)
        {
            var block = new BlockNode(Peek().Line, Peek().Column);

            while (!endTokens.Any(t => Check(t)) && !IsAtEnd())
            {
                SkipNewlines();

                if (endTokens.Any(t => Check(t)) || IsAtEnd())
                    break;

                try
                {
                    var statement = ParseStatement();
                    if (statement != null)
                    {
                        block.Statements.Add(statement);
                    }
                }
                catch (ParseException ex)
                {
                    RecordError(ex.Message, ex.Token, ex.Suggestion);
                    Synchronize();

                    // If we've synchronized to an end token, break out
                    if (endTokens.Any(t => Check(t)))
                        break;
                }

                SkipNewlines();
            }

            return block;
        }

        private SelectStatementNode ParseSelectStatement()
        {
            var token = Consume(TokenType.Select, "Expected 'Select'");
            Consume(TokenType.Case, "Expected 'Case' after 'Select'");
            var node = new SelectStatementNode(token.Line, token.Column);

            node.Expression = ParseExpression();
            ConsumeNewlines();

            while (Check(TokenType.Case) && !IsAtEnd())
            {
                var caseNode = ParseCaseClause();
                node.Cases.Add(caseNode);
                SkipNewlines();
            }

            Consume(TokenType.EndSelect, "Expected 'End Select'");
            return node;
        }

        private CaseClauseNode ParseCaseClause()
        {
            var token = Consume(TokenType.Case, "Expected 'Case'");
            var node = new CaseClauseNode(token.Line, token.Column);

            if (Match(TokenType.Else))
            {
                node.IsElse = true;
            }
            else
            {
                do
                {
                    var pattern = ParseCasePatternOrExpression();
                    if (pattern != null)
                    {
                        node.Patterns.Add(pattern);
                    }
                } while (Match(TokenType.Comma));
            }

            ConsumeNewlines();
            node.Body = ParseBlock(TokenType.Case, TokenType.EndSelect);

            return node;
        }

        /// <summary>
        /// Parse a pattern or expression in a Case clause, handling Or patterns
        /// </summary>
        private PatternNode ParseCasePatternOrExpression()
        {
            var token = Peek();
            var alternatives = new List<PatternNode>();

            // Parse first pattern or value
            var first = ParseSingleCasePattern();
            if (first == null)
            {
                // Parse as simple expression value
                var expr = ParsePrimaryExpression();
                if (expr != null)
                {
                    first = new ConstantPatternNode(expr.Line, expr.Column) { Value = expr };
                }
            }

            if (first == null)
                return null;

            alternatives.Add(first);

            // Check for Or pattern: Case 1 Or 2 Or 3
            while (Match(TokenType.Or))
            {
                var next = ParseSingleCasePattern();
                if (next == null)
                {
                    // Parse as simple expression value
                    var expr = ParsePrimaryExpression();
                    if (expr != null)
                    {
                        next = new ConstantPatternNode(expr.Line, expr.Column) { Value = expr };
                    }
                }

                if (next != null)
                {
                    alternatives.Add(next);
                }
            }

            PatternNode result;
            if (alternatives.Count == 1)
            {
                result = alternatives[0];
            }
            else
            {
                result = new OrPatternNode(token.Line, token.Column)
                {
                    Alternatives = alternatives
                };
            }

            // Parse When guard at the end
            return ParseWhenGuard(result);
        }

        /// <summary>
        /// Parse a simple primary expression (stopping at Or, When, Comma, newline)
        /// </summary>
        private ExpressionNode ParsePrimaryExpression()
        {
            // Parse only the primary part, not full binary expressions
            if (Check(TokenType.IntegerLiteral) || Check(TokenType.LongLiteral) ||
                Check(TokenType.SingleLiteral) || Check(TokenType.DoubleLiteral) ||
                Check(TokenType.StringLiteral) || Check(TokenType.CharLiteral) ||
                Check(TokenType.BooleanLiteral))
            {
                var t = Advance();
                return new LiteralExpressionNode(t.Line, t.Column) { Value = t.Value, LiteralType = t.Type };
            }
            if (Match(TokenType.Nothing))
            {
                var t = Previous();
                return new LiteralExpressionNode(t.Line, t.Column) { Value = null, LiteralType = TokenType.Nothing };
            }
            if (Check(TokenType.Identifier))
            {
                var t = Advance();
                return new IdentifierExpressionNode(t.Line, t.Column) { Name = t.Lexeme };
            }
            return null;
        }

        /// <summary>
        /// Parse a single case pattern (without Or combinations)
        /// </summary>
        private PatternNode ParseSingleCasePattern()
        {
            return ParseCasePattern();
        }

        /// <summary>
        /// Parse a pattern in a Case clause
        /// Returns null if the next tokens are not a pattern (fallback to expression)
        /// </summary>
        private PatternNode ParseCasePattern()
        {
            int startPos = _current;
            var token = Previous();

            // Case Nothing - null pattern
            if (Match(TokenType.Nothing))
            {
                var pattern = new NothingPatternNode(token.Line, token.Column);
                return ParseWhenGuard(pattern);
            }

            // Case (x, y, z) - tuple/deconstruction pattern
            if (Check(TokenType.LeftParen))
            {
                var tuplePattern = ParseTuplePattern();
                if (tuplePattern != null)
                    return ParseWhenGuard(tuplePattern);
            }

            // Case Is > 10, Case Is Integer, Case Is Nothing
            if (Match(TokenType.Is))
            {
                // Case Is Nothing
                if (Match(TokenType.Nothing))
                {
                    var pattern = new NothingPatternNode(token.Line, token.Column);
                    return ParseWhenGuard(pattern);
                }

                // Check for comparison pattern: Case Is > 10
                if (Check(TokenType.GreaterThan) || Check(TokenType.LessThan) ||
                    Check(TokenType.GreaterThanOrEqual) || Check(TokenType.LessThanOrEqual) ||
                    Check(TokenType.Assignment) || Check(TokenType.NotEqual))
                {
                    var opToken = Advance();
                    var op = opToken.Type switch
                    {
                        TokenType.GreaterThan => ">",
                        TokenType.LessThan => "<",
                        TokenType.GreaterThanOrEqual => ">=",
                        TokenType.LessThanOrEqual => "<=",
                        TokenType.Assignment => "=",
                        TokenType.NotEqual => "<>",
                        _ => "="
                    };
                    var value = ParseExpression();
                    var pattern = new ComparisonPatternNode(startPos, 0) { Operator = op, Value = value };
                    return ParseWhenGuard(pattern);
                }

                // Check for type pattern: Case Is String, Case Is Integer
                if (Check(TokenType.Identifier) || Check(TokenType.Integer) || Check(TokenType.String) ||
                    Check(TokenType.Boolean) || Check(TokenType.Double) || Check(TokenType.Single))
                {
                    var typeRef = ParseTypeReference();
                    var pattern = new TypePatternNode(startPos, 0) { MatchType = typeRef };
                    return ParseWhenGuard(pattern);
                }

                // Rollback if not a valid pattern after Is
                _current = startPos;
                return null;
            }

            // Check for range pattern: Case 1 To 10
            // We need to look ahead to see if there's a "To" keyword
            var expr = ParseExpression();
            if (Match(TokenType.To))
            {
                var upperBound = ParseExpression();
                var pattern = new RangePatternNode(startPos, 0)
                {
                    LowerBound = expr,
                    UpperBound = upperBound
                };
                return ParseWhenGuard(pattern);
            }

            // Check for type pattern with binding: Case x As Integer
            if (expr is IdentifierExpressionNode ident)
            {
                if (Match(TokenType.As))
                {
                    var typeRef = ParseTypeReference();
                    var pattern = new TypePatternNode(startPos, 0)
                    {
                        VariableName = ident.Name,
                        MatchType = typeRef
                    };
                    return ParseWhenGuard(pattern);
                }

                // Check for binding pattern with When guard: Case n When n > 0
                if (Check(TokenType.When))
                {
                    var pattern = new BindingPatternNode(ident.Line, ident.Column)
                    {
                        VariableName = ident.Name
                    };
                    return ParseWhenGuard(pattern);
                }
            }

            // Not a pattern, rollback and return null
            _current = startPos;
            return null;
        }

        /// <summary>
        /// Parse a When guard clause after a pattern: Case x When x > 0
        /// </summary>
        private PatternNode ParseWhenGuard(PatternNode pattern)
        {
            if (Match(TokenType.When))
            {
                pattern.WhenGuard = ParseExpression();
            }
            return pattern;
        }

        /// <summary>
        /// Parse a tuple/deconstruction pattern: Case (x, y, z)
        /// </summary>
        private TuplePatternNode ParseTuplePattern()
        {
            int startPos = _current;
            var token = Peek();

            if (!Match(TokenType.LeftParen))
                return null;

            var tuplePattern = new TuplePatternNode(token.Line, token.Column);

            do
            {
                // Each element can be:
                // - An identifier (capture variable)
                // - A nested pattern
                // - An underscore/discard pattern (represented as identifier "_")
                if (Check(TokenType.Identifier))
                {
                    var identToken = Advance();
                    // Check if it's a type pattern: x As Integer
                    if (Match(TokenType.As))
                    {
                        var typeRef = ParseTypeReference();
                        tuplePattern.Elements.Add(new TypePatternNode(identToken.Line, identToken.Column)
                        {
                            VariableName = identToken.Lexeme,
                            MatchType = typeRef
                        });
                    }
                    else
                    {
                        // Simple capture variable - use ConstantPatternNode with identifier
                        tuplePattern.Elements.Add(new ConstantPatternNode(identToken.Line, identToken.Column)
                        {
                            Value = new IdentifierExpressionNode(identToken.Line, identToken.Column) { Name = identToken.Lexeme }
                        });
                    }
                }
                else if (Check(TokenType.LeftParen))
                {
                    // Nested tuple pattern
                    var nested = ParseTuplePattern();
                    if (nested != null)
                        tuplePattern.Elements.Add(nested);
                }
                else
                {
                    // Constant value
                    var value = ParseExpression();
                    tuplePattern.Elements.Add(new ConstantPatternNode(token.Line, token.Column) { Value = value });
                }
            } while (Match(TokenType.Comma));

            if (!Match(TokenType.RightParen))
            {
                // Rollback if not a valid tuple pattern
                _current = startPos;
                return null;
            }

            return tuplePattern;
        }

        private StatementNode ParseForLoop()
        {
            var token = Consume(TokenType.For, "Expected 'For'");

            // Check for For Each
            if (Match(TokenType.Each))
            {
                return ParseForEachLoop(token);
            }

            var node = new ForLoopNode(token.Line, token.Column);
            node.Variable = Consume(TokenType.Identifier, "Expected loop variable").Lexeme;
            Consume(TokenType.Assignment, "Expected '='");
            node.Start = ParseExpression();
            Consume(TokenType.To, "Expected 'To'");
            node.End = ParseExpression();

            if (Match(TokenType.Step))
            {
                node.Step = ParseExpression();
            }

            ConsumeNewlines();
            node.Body = ParseBlock(TokenType.Next);
            Consume(TokenType.Next, "Expected 'Next'");

            // Optional variable after Next
            if (Check(TokenType.Identifier))
            {
                Advance();
            }

            return node;
        }

        private StatementNode ParseForEachLoop(Token startToken)
        {
            var node = new ForEachLoopNode(startToken.Line, startToken.Column);

            node.Variable = Consume(TokenType.Identifier, "Expected loop variable").Lexeme;
            Consume(TokenType.As, "Expected 'As'");
            node.VariableType = ParseTypeReference();
            Consume(TokenType.In, "Expected 'In'");
            node.Collection = ParseExpression();

            ConsumeNewlines();
            node.Body = ParseBlock(TokenType.Next);
            Consume(TokenType.Next, "Expected 'Next'");

            // Optional variable after Next
            if (Check(TokenType.Identifier))
            {
                Advance();
            }

            return node;
        }

        private WhileLoopNode ParseWhileLoop()
        {
            var token = Consume(TokenType.While, "Expected 'While'");
            var node = new WhileLoopNode(token.Line, token.Column);

            node.Condition = ParseExpression();
            ConsumeNewlines();
            node.Body = ParseBlock(TokenType.Wend);
            Consume(TokenType.Wend, "Expected 'Wend'");

            return node;
        }

        private DoLoopNode ParseDoLoop()
        {
            var token = Consume(TokenType.Do, "Expected 'Do'");
            var node = new DoLoopNode(token.Line, token.Column);

            // Check for condition at start: Do While/Until <condition>
            if (Match(TokenType.While))
            {
                node.IsConditionAtStart = true;
                node.IsWhile = true;
                node.Condition = ParseExpression();
            }
            else if (Match(TokenType.Until))
            {
                node.IsConditionAtStart = true;
                node.IsWhile = false;
                node.Condition = ParseExpression();
            }

            ConsumeNewlines();
            node.Body = ParseBlock(TokenType.Loop);
            Consume(TokenType.Loop, "Expected 'Loop'");

            // Check for condition at end: Loop While/Until <condition>
            if (Match(TokenType.While))
            {
                node.IsConditionAtStart = false;
                node.IsWhile = true;
                node.Condition = ParseExpression();
            }
            else if (Match(TokenType.Until))
            {
                node.IsConditionAtStart = false;
                node.IsWhile = false;
                node.Condition = ParseExpression();
            }

            return node;
        }

        private TryStatementNode ParseTryStatement()
        {
            var token = Consume(TokenType.Try, "Expected 'Try'");
            var node = new TryStatementNode(token.Line, token.Column);

            ConsumeNewlines();
            node.TryBlock = ParseBlock(TokenType.Catch, TokenType.Finally, TokenType.EndTry);

            while (Check(TokenType.Catch))
            {
                var catchClause = ParseCatchClause();
                node.CatchClauses.Add(catchClause);
            }

            if (Check(TokenType.Finally))
            {
                Advance(); // consume Finally
                ConsumeNewlines();
                node.FinallyBlock = ParseBlock(TokenType.EndTry);
            }

            Consume(TokenType.EndTry, "Expected 'End Try'");
            return node;
        }

        private WithStatementNode ParseWithStatement()
        {
            var token = Consume(TokenType.With, "Expected 'With'");
            var node = new WithStatementNode(token.Line, token.Column);

            node.Object = ParseExpression();
            ConsumeNewlines();

            node.Body = ParseBlock(TokenType.EndWith);
            Consume(TokenType.EndWith, "Expected 'End With'");

            return node;
        }

        private CatchClauseNode ParseCatchClause()
        {
            var token = Consume(TokenType.Catch, "Expected 'Catch'");
            var node = new CatchClauseNode(token.Line, token.Column);

            // Optional exception variable
            if (Check(TokenType.Identifier))
            {
                node.ExceptionVariable = Advance().Lexeme;
                if (Check(TokenType.As))
                {
                    Advance();
                    node.ExceptionType = ParseTypeReference();
                }
            }

            ConsumeNewlines();
            node.Body = ParseBlock(TokenType.Catch, TokenType.Finally, TokenType.EndTry);

            return node;
        }

        private ThrowStatementNode ParseThrowStatement()
        {
            var token = Consume(TokenType.Throw, "Expected 'Throw'");
            var node = new ThrowStatementNode(token.Line, token.Column);

            if (!Check(TokenType.Newline) && !IsAtEnd())
            {
                node.Exception = ParseExpression();
            }

            return node;
        }

        private ReturnStatementNode ParseReturnStatement()
        {
            var token = Consume(TokenType.Return, "Expected 'Return'");
            var node = new ReturnStatementNode(token.Line, token.Column);

            if (!Check(TokenType.Newline) && !IsAtEnd())
            {
                node.Value = ParseExpression();
            }

            return node;
        }

        private ExitStatementNode ParseExitStatement()
        {
            var token = Consume(TokenType.Exit, "Expected 'Exit'");
            var node = new ExitStatementNode(token.Line, token.Column);

            // Expect the kind of exit: For, Do, While, Sub, Function
            if (Check(TokenType.For))
            {
                Advance();
                node.Kind = ExitKind.For;
            }
            else if (Check(TokenType.Do))
            {
                Advance();
                node.Kind = ExitKind.Do;
            }
            else if (Check(TokenType.While))
            {
                Advance();
                node.Kind = ExitKind.While;
            }
            else if (Check(TokenType.Sub))
            {
                Advance();
                node.Kind = ExitKind.Sub;
            }
            else if (Check(TokenType.Function))
            {
                Advance();
                node.Kind = ExitKind.Function;
            }
            else
            {
                throw new ParseException("Expected 'For', 'Do', 'While', 'Sub', or 'Function' after 'Exit'", Peek());
            }

            return node;
        }

        private StatementNode ParseAssignmentOrExpressionStatement()
        {
            // Parse the left-hand side WITHOUT treating '=' as equality
            // This is the assignment target or the start of an expression
            var target = ParseAssignmentTarget();

            // Check for assignment operators
            if (Check(TokenType.Assignment) || Check(TokenType.PlusAssign) ||
                Check(TokenType.MinusAssign) || Check(TokenType.MultiplyAssign) ||
                Check(TokenType.DivideAssign))
            {
                var token = Advance();
                var assignment = new AssignmentStatementNode(token.Line, token.Column);
                assignment.Target = target;
                assignment.Operator = token.Lexeme;
                assignment.Value = ParseExpression();
                return assignment;
            }

            // Check for VB-style subroutine call without parentheses: PrintLine "Hello"
            // This is an identifier (or member access) followed by an argument
            if (IsVbStyleCallStatement(target))
            {
                var callExpr = new CallExpressionNode(target.Line, target.Column);
                callExpr.Callee = target;

                // Parse arguments until end of line
                while (!Check(TokenType.Newline) && !Check(TokenType.Colon) && !IsAtEnd() &&
                       !IsStatementTerminator())
                {
                    callExpr.Arguments.Add(ParseExpression());
                    if (!Match(TokenType.Comma))
                        break;
                }

                var exprStmt = new ExpressionStatementNode(callExpr.Line, callExpr.Column);
                exprStmt.Expression = callExpr;
                return exprStmt;
            }

            // Not an assignment - continue parsing as a full expression
            // The target we parsed is just the start of the expression
            var expr = ParseExpressionContinuation(target);

            // Expression statement
            var exprStmt2 = new ExpressionStatementNode(expr.Line, expr.Column);
            exprStmt2.Expression = expr;
            return exprStmt2;
        }

        /// <summary>
        /// Check if the current state indicates a VB-style call statement without parentheses.
        /// E.g., PrintLine "Hello" or Debug.Print message
        /// </summary>
        private bool IsVbStyleCallStatement(ExpressionNode target)
        {
            // Target must be an identifier or member access (potential subroutine/function name)
            if (!(target is IdentifierExpressionNode) && !(target is MemberAccessExpressionNode))
                return false;

            // If the target is already a call expression, it's not a VB-style call
            if (target is CallExpressionNode)
                return false;

            // Next token must be the start of an expression (potential argument)
            // but NOT an assignment operator, binary operator, or end of statement
            var next = Peek();

            // Skip if it's end of statement
            if (next.Type == TokenType.Newline || next.Type == TokenType.Colon ||
                next.Type == TokenType.EOF)
                return false;

            // Skip if it's an assignment operator
            if (next.Type == TokenType.Assignment || next.Type == TokenType.PlusAssign ||
                next.Type == TokenType.MinusAssign || next.Type == TokenType.MultiplyAssign ||
                next.Type == TokenType.DivideAssign)
                return false;

            // Skip if it's a binary operator (not a call, but a binary expression)
            if (IsBinaryOperator(next))
                return false;

            // Skip if it's a postfix operator (dot, paren, bracket)
            if (next.Type == TokenType.Dot || next.Type == TokenType.LeftParen ||
                next.Type == TokenType.LeftBracket)
                return false;

            // It looks like a VB-style call: identifier followed by an expression argument
            return IsExpressionStart(next);
        }

        /// <summary>
        /// Check if a token could start an expression
        /// </summary>
        private bool IsExpressionStart(Token token)
        {
            return token.Type switch
            {
                TokenType.StringLiteral => true,
                TokenType.IntegerLiteral => true,
                TokenType.LongLiteral => true,
                TokenType.SingleLiteral => true,
                TokenType.DoubleLiteral => true,
                TokenType.CharLiteral => true,
                TokenType.BooleanLiteral => true,
                TokenType.Identifier => true,
                TokenType.LeftParen => true,
                TokenType.New => true,
                TokenType.Not => true,
                TokenType.Minus => true,
                TokenType.AddressOf => true,
                TokenType.Me => true,
                TokenType.MyBase => true,
                TokenType.InterpolatedStringLiteral => true,
                _ => false
            };
        }

        /// <summary>
        /// Check if current token terminates a statement
        /// </summary>
        private bool IsStatementTerminator()
        {
            var t = Peek().Type;
            return t == TokenType.Then || t == TokenType.Else || t == TokenType.ElseIf ||
                   t == TokenType.EndIf || t == TokenType.EndSub || t == TokenType.EndFunction ||
                   t == TokenType.EndClass || t == TokenType.EndModule || t == TokenType.EndNamespace ||
                   t == TokenType.Loop || t == TokenType.Wend || t == TokenType.Next ||
                   t == TokenType.Case || t == TokenType.EndSelect || t == TokenType.Catch ||
                   t == TokenType.Finally || t == TokenType.EndTry;
        }

        /// <summary>
        /// Parse a potential assignment target: identifier, member access, array access,
        /// or prefix increment/decrement.
        /// Does NOT consume '=' as equality.
        /// </summary>
        private ExpressionNode ParseAssignmentTarget()
        {
            // Handle prefix increment/decrement as expression statements
            if (Check(TokenType.Increment) || Check(TokenType.Decrement))
            {
                var op = Advance();
                var operand = ParsePostfix();
                var unary = new UnaryExpressionNode(op.Line, op.Column);
                unary.Operator = op.Lexeme;
                unary.Operand = operand;
                unary.IsPostfix = false;
                return unary;
            }

            return ParsePostfix();
        }

        /// <summary>
        /// Continue parsing an expression given an already-parsed left side.
        /// Used when we determined something is NOT an assignment.
        /// </summary>
        private ExpressionNode ParseExpressionContinuation(ExpressionNode left)
        {
            // Continue with binary operators if present
            return ParseBinaryExpressionContinuation(left, 0);
        }

        private ExpressionNode ParseBinaryExpressionContinuation(ExpressionNode left, int minPrecedence)
        {
            while (IsBinaryOperator(Peek()) && GetPrecedence(Peek()) >= minPrecedence)
            {
                var op = Advance();
                int prec = GetPrecedence(op);
                var right = ParseUnary();

                while (IsBinaryOperator(Peek()) && GetPrecedence(Peek()) > prec)
                {
                    right = ParseBinaryExpressionContinuation(right, GetPrecedence(Peek()));
                }

                var binary = new BinaryExpressionNode(op.Line, op.Column);
                binary.Left = left;
                binary.Operator = op.Lexeme;
                binary.Right = right;
                left = binary;
            }
            return left;
        }

        private bool IsBinaryOperator(Token token)
        {
            return token.Type switch
            {
                TokenType.Plus or TokenType.Minus or TokenType.Multiply or TokenType.Divide or
                TokenType.Modulo or TokenType.And or TokenType.Or or TokenType.BitwiseXor or
                TokenType.AndAnd or TokenType.OrOr or TokenType.Assignment or
                TokenType.Equal or TokenType.NotEqual or TokenType.LessThan or
                TokenType.LessThanOrEqual or TokenType.GreaterThan or TokenType.GreaterThanOrEqual => true,
                _ => false
            };
        }

        private int GetPrecedence(Token token)
        {
            return token.Type switch
            {
                TokenType.OrOr or TokenType.Or => 1,
                TokenType.AndAnd or TokenType.And => 2,
                TokenType.Assignment or TokenType.Equal or TokenType.NotEqual => 3,
                TokenType.LessThan or TokenType.LessThanOrEqual or
                TokenType.GreaterThan or TokenType.GreaterThanOrEqual => 4,
                TokenType.Plus or TokenType.Minus => 5,
                TokenType.Multiply or TokenType.Divide or TokenType.Modulo => 6,
                TokenType.BitwiseXor => 7,
                _ => 0
            };
        }

        // ====================================================================
        // Expressions
        // ====================================================================

        private ExpressionNode ParseExpression()
        {
            return ParseLogicalOr();
        }

        private ExpressionNode ParseLogicalOr()
        {
            var expr = ParseLogicalAnd();

            while (Check(TokenType.Or) || Check(TokenType.OrOr))
            {
                var op = Advance();
                var right = ParseLogicalAnd();
                var binary = new BinaryExpressionNode(op.Line, op.Column);
                binary.Left = expr;
                binary.Operator = op.Lexeme;
                binary.Right = right;
                expr = binary;
            }

            return expr;
        }

        private ExpressionNode ParseLogicalAnd()
        {
            var expr = ParseEquality();

            while (Check(TokenType.And) || Check(TokenType.AndAnd))
            {
                var op = Advance();
                var right = ParseEquality();
                var binary = new BinaryExpressionNode(op.Line, op.Column);
                binary.Left = expr;
                binary.Operator = op.Lexeme;
                binary.Right = right;
                expr = binary;
            }

            return expr;
        }

        private ExpressionNode ParseEquality()
        {
            var expr = ParseComparison();

            while (Check(TokenType.Equal) || Check(TokenType.NotEqual) ||
                   Check(TokenType.IsEqual) || Check(TokenType.Assignment)) // Include Assignment here
            {
                var op = Advance();
                // Normalize = to == in expression context
                if (op.Type == TokenType.Assignment)
                    op = new Token(TokenType.Equal, "=", op.Value, op.Line, op.Column);

                var right = ParseComparison();
                var binary = new BinaryExpressionNode(op.Line, op.Column);
                binary.Left = expr;
                binary.Operator = op.Lexeme;
                binary.Right = right;
                expr = binary;
            }

            return expr;
        }
        private ExpressionNode ParseComparison()
        {
            var expr = ParseAdditive();

            while (Check(TokenType.LessThan) || Check(TokenType.LessThanOrEqual) ||
                   Check(TokenType.GreaterThan) || Check(TokenType.GreaterThanOrEqual))
            {
                var op = Advance();
                var right = ParseAdditive();
                var binary = new BinaryExpressionNode(op.Line, op.Column);
                binary.Left = expr;
                binary.Operator = op.Lexeme;
                binary.Right = right;
                expr = binary;
            }

            return expr;
        }

        private ExpressionNode ParseAdditive()
        {
            var expr = ParseMultiplicative();

            while (Check(TokenType.Plus) || Check(TokenType.Minus) || Check(TokenType.Concatenate))
            {
                var op = Advance();
                var right = ParseMultiplicative();
                var binary = new BinaryExpressionNode(op.Line, op.Column);
                binary.Left = expr;
                binary.Operator = op.Lexeme;
                binary.Right = right;
                expr = binary;
            }

            return expr;
        }

        private ExpressionNode ParseMultiplicative()
        {
            var expr = ParseUnary();

            while (Check(TokenType.Multiply) || Check(TokenType.Divide) ||
                   Check(TokenType.IntegerDivide) || Check(TokenType.Modulo))
            {
                var op = Advance();
                var right = ParseUnary();
                var binary = new BinaryExpressionNode(op.Line, op.Column);
                binary.Left = expr;
                binary.Operator = op.Lexeme;
                binary.Right = right;
                expr = binary;
            }

            return expr;
        }

        private ExpressionNode ParseUnary()
        {
            // Prefix operators: Not, !, -, +
            if (Check(TokenType.Not) || Check(TokenType.Bang) ||
                Check(TokenType.Minus) || Check(TokenType.Plus))
            {
                var op = Advance();
                var operand = ParseUnary();
                var unary = new UnaryExpressionNode(op.Line, op.Column);
                unary.Operator = op.Lexeme;
                unary.Operand = operand;
                unary.IsPostfix = false;
                return unary;
            }

            // Prefix increment/decrement: ++x, --y
            if (Check(TokenType.Increment) || Check(TokenType.Decrement))
            {
                var op = Advance();
                var operand = ParseUnary();
                var unary = new UnaryExpressionNode(op.Line, op.Column);
                unary.Operator = op.Lexeme;
                unary.Operand = operand;
                unary.IsPostfix = false;
                return unary;
            }

            return ParsePostfix();
        }

        private ExpressionNode ParsePostfix()
        {
            var expr = ParsePrimary();

            while (true)
            {
                if (Match(TokenType.Increment) || Match(TokenType.Decrement))
                {
                    var op = Previous();
                    var unary = new UnaryExpressionNode(op.Line, op.Column);
                    unary.Operator = op.Lexeme;
                    unary.Operand = expr;
                    unary.IsPostfix = true;
                    expr = unary;
                }
                else if (Match(TokenType.Dot))
                {
                    // Allow identifiers and keywords as member names (e.g., obj.Property, obj.Value)
                    var memberToken = Peek();
                    string member;
                    if (Check(TokenType.Identifier))
                    {
                        member = Advance().Lexeme;
                    }
                    else if (IsKeywordUsableAsMemberName(memberToken.Type))
                    {
                        member = Advance().Lexeme;
                    }
                    else
                    {
                        // Error-tolerant: If no member name after dot, create incomplete member access
                        // This allows IntelliSense to still work when user has typed "obj." and waiting for completion
                        var incompleteMemberAccess = new MemberAccessExpressionNode(expr.Line, expr.Column);
                        incompleteMemberAccess.Object = expr;
                        incompleteMemberAccess.MemberName = ""; // Empty member name indicates incomplete
                        incompleteMemberAccess.IsIncomplete = true;
                        expr = incompleteMemberAccess;
                        break; // Stop parsing this expression - it's incomplete
                    }
                    var memberAccess = new MemberAccessExpressionNode(expr.Line, expr.Column);
                    memberAccess.Object = expr;
                    memberAccess.MemberName = member;
                    expr = memberAccess;
                }
                else if (Match(TokenType.LeftParen))
                {
                    // Check if this is generic type arguments: func(Of T)(args)
                    if (Check(TokenType.Of))
                    {
                        // Parse generic type arguments
                        Advance(); // consume 'Of'
                        var genericArgs = new List<TypeReference>();
                        do
                        {
                            genericArgs.Add(ParseTypeReference());
                        } while (Match(TokenType.Comma));
                        Consume(TokenType.RightParen, "Expected ')' after generic type arguments");

                        // Now expect the actual function arguments
                        var call = new CallExpressionNode(expr.Line, expr.Column);
                        call.Callee = expr;
                        call.GenericArguments = genericArgs;

                        if (Match(TokenType.LeftParen))
                        {
                            if (!Check(TokenType.RightParen))
                            {
                                do
                                {
                                    call.Arguments.Add(ParseExpression());
                                } while (Match(TokenType.Comma));
                            }
                            Consume(TokenType.RightParen, "Expected ')' after arguments");
                        }
                        expr = call;
                    }
                    else
                    {
                        // Regular function call
                        var call = new CallExpressionNode(expr.Line, expr.Column);
                        call.Callee = expr;

                        if (!Check(TokenType.RightParen))
                        {
                            do
                            {
                                call.Arguments.Add(ParseExpression());
                            } while (Match(TokenType.Comma));
                        }

                        Consume(TokenType.RightParen, "Expected ')' after arguments");
                        expr = call;
                    }
                }
                else if (Match(TokenType.LeftBracket))
                {
                    var arrayAccess = new ArrayAccessExpressionNode(expr.Line, expr.Column);
                    arrayAccess.Array = expr;

                    do
                    {
                        arrayAccess.Indices.Add(ParseExpression());
                        Consume(TokenType.RightBracket, "Expected ']'");
                    } while (Match(TokenType.LeftBracket));

                    expr = arrayAccess;
                }
                else if (Match(TokenType.Caret))
                {
                    // Pointer dereference
                    var unary = new UnaryExpressionNode(expr.Line, expr.Column);
                    unary.Operator = "^";
                    unary.Operand = expr;
                    unary.IsPostfix = true;
                    expr = unary;
                }
                else
                {
                    break;
                }
            }

            return expr;
        }

        private ExpressionNode ParsePrimary()
        {
            // Implicit With member access: .Member inside a With block
            if (Check(TokenType.Dot))
            {
                var dotToken = Advance();
                var memberToken = Consume(TokenType.Identifier, "Expected member name after '.'");
                var node = new ImplicitWithMemberNode(dotToken.Line, dotToken.Column);
                node.MemberName = memberToken.Lexeme;
                return node;
            }

            // Literals
            if (Check(TokenType.IntegerLiteral) || Check(TokenType.LongLiteral) ||
                Check(TokenType.SingleLiteral) || Check(TokenType.DoubleLiteral) ||
                Check(TokenType.StringLiteral) || Check(TokenType.CharLiteral) ||
                Check(TokenType.BooleanLiteral))
            {
                var token = Advance();
                var literal = new LiteralExpressionNode(token.Line, token.Column);
                literal.Value = token.Value;
                literal.LiteralType = token.Type;
                return literal;
            }

            // Nothing literal (null)
            if (Match(TokenType.Nothing))
            {
                var token = Previous();
                var literal = new LiteralExpressionNode(token.Line, token.Column);
                literal.Value = null;
                literal.LiteralType = TokenType.Nothing;
                return literal;
            }

            // Interpolated string: $"Hello {name}"
            if (Check(TokenType.InterpolatedStringLiteral))
            {
                return ParseInterpolatedString();
            }

            // New expression
            if (Match(TokenType.New))
            {
                var token = Previous();
                var newExpr = new NewExpressionNode(token.Line, token.Column);
                newExpr.Type = ParseTypeReference();

                if (Match(TokenType.LeftParen))
                {
                    if (!Check(TokenType.RightParen))
                    {
                        do
                        {
                            newExpr.Arguments.Add(ParseExpression());
                        } while (Match(TokenType.Comma));
                    }
                    Consume(TokenType.RightParen, "Expected ')' after arguments");
                }

                return newExpr;
            }

            // AddressOf
            if (Match(TokenType.AddressOf))
            {
                var token = Previous();
                var unary = new UnaryExpressionNode(token.Line, token.Column);
                unary.Operator = "AddressOf";
                unary.Operand = ParsePrimary();
                unary.IsPostfix = false;
                return unary;
            }

            // Deref - dereference pointer
            if (Match(TokenType.Deref))
            {
                var token = Previous();
                var unary = new UnaryExpressionNode(token.Line, token.Column);
                unary.Operator = "Deref";
                unary.Operand = ParsePrimary();
                unary.IsPostfix = false;
                return unary;
            }

            // Await expression: Await SomeAsyncMethod()
            if (Match(TokenType.Await))
            {
                var token = Previous();
                var awaitExpr = new AwaitExpressionNode(token.Line, token.Column);
                awaitExpr.Expression = ParseUnary();  // Parse the expression to await
                return awaitExpr;
            }

            // LINQ query expression: From x In collection Where x > 0 Select x
            if (Check(TokenType.From))
            {
                return ParseLinqQuery();
            }

            // Lambda expression: Function(x) expr or Sub(x) statement
            if (Check(TokenType.Function) || Check(TokenType.Sub))
            {
                bool isFunction = Check(TokenType.Function);
                var token = Advance();

                // Check if this is a lambda (followed by open paren)
                if (Check(TokenType.LeftParen))
                {
                    return ParseLambdaExpression(token, isFunction);
                }
                else
                {
                    // Put the token back and let normal parsing handle it
                    _current--;
                }
            }

            // Me (this)
            if (Match(TokenType.Me))
            {
                var token = Previous();
                return new IdentifierExpressionNode(token.Line, token.Column) { Name = "Me" };
            }

            // MyBase (base class reference)
            if (Match(TokenType.MyBase))
            {
                var token = Previous();
                return new MyBaseExpressionNode(token.Line, token.Column);
            }

            // Identifier
            if (Check(TokenType.Identifier))
            {
                var token = Advance();
                return new IdentifierExpressionNode(token.Line, token.Column) { Name = token.Lexeme };
            }

            // Parenthesized expression or tuple literal
            if (Match(TokenType.LeftParen))
            {
                var token = Previous();
                var firstExpr = ParseExpression();

                // Check if this is a tuple literal: (expr, expr, ...)
                if (Match(TokenType.Comma))
                {
                    var tupleNode = new TupleLiteralNode(token.Line, token.Column);
                    tupleNode.Elements.Add(firstExpr);
                    tupleNode.ElementNames.Add(null); // No name for positional element

                    do
                    {
                        tupleNode.Elements.Add(ParseExpression());
                        tupleNode.ElementNames.Add(null);
                    } while (Match(TokenType.Comma));

                    Consume(TokenType.RightParen, "Expected ')' after tuple elements");
                    return tupleNode;
                }

                Consume(TokenType.RightParen, "Expected ')' after expression");
                return firstExpr;
            }

            // Collection initializer: { 1, 2, 3 }
            if (Match(TokenType.LeftBrace))
            {
                var token = Previous();
                var node = new CollectionInitializerNode(token.Line, token.Column);

                if (!Check(TokenType.RightBrace))
                {
                    do
                    {
                        node.Elements.Add(ParseExpression());
                    } while (Match(TokenType.Comma));
                }

                Consume(TokenType.RightBrace, "Expected '}' after collection initializer");
                return node;
            }

            throw new ParseException(
                $"Unexpected token in expression: '{Peek().Lexeme}' ({Peek().Type})",
                Peek(),
                "Expected a value, variable, function call, or operator. Valid expression elements include: literals, identifiers, parentheses, or operators like +, -, *, /.");
        }

        private InterpolatedStringNode ParseInterpolatedString()
        {
            var token = Advance(); // consume the InterpolatedStringLiteral token
            var node = new InterpolatedStringNode(token.Line, token.Column);

            // The token.Value contains the raw string content like "Hello {name}, age {age}"
            string content = token.Value?.ToString() ?? "";

            // Parse the content into parts
            int i = 0;
            var currentText = new System.Text.StringBuilder();

            while (i < content.Length)
            {
                if (content[i] == '{')
                {
                    // Save any accumulated text
                    if (currentText.Length > 0)
                    {
                        node.Parts.Add(currentText.ToString());
                        currentText.Clear();
                    }

                    // Find matching closing brace
                    int braceDepth = 1;
                    int start = i + 1;
                    i++;
                    while (i < content.Length && braceDepth > 0)
                    {
                        if (content[i] == '{') braceDepth++;
                        else if (content[i] == '}') braceDepth--;
                        if (braceDepth > 0) i++;
                    }

                    // Extract the expression text
                    string exprText = content.Substring(start, i - start);
                    i++; // skip the closing brace

                    // Parse the expression
                    var exprLexer = new Lexer(exprText);
                    var exprTokens = exprLexer.Tokenize();
                    var exprParser = new Parser(exprTokens);
                    var expr = exprParser.ParseExpression();
                    node.Parts.Add(expr);
                }
                else
                {
                    currentText.Append(content[i]);
                    i++;
                }
            }

            // Add any remaining text
            if (currentText.Length > 0)
            {
                node.Parts.Add(currentText.ToString());
            }

            return node;
        }

        private LambdaExpressionNode ParseLambdaExpression(Token token, bool isFunction)
        {
            var lambda = new LambdaExpressionNode(token.Line, token.Column);
            lambda.IsFunction = isFunction;

            // Parse parameters
            Consume(TokenType.LeftParen, "Expected '(' after Function/Sub in lambda");

            if (!Check(TokenType.RightParen))
            {
                do
                {
                    var param = new ParameterNode(Peek().Line, Peek().Column);
                    param.Name = Consume(TokenType.Identifier, "Expected parameter name").Lexeme;

                    if (Match(TokenType.As))
                    {
                        param.Type = ParseTypeReference();
                    }

                    lambda.Parameters.Add(param);
                } while (Match(TokenType.Comma));
            }

            Consume(TokenType.RightParen, "Expected ')' after lambda parameters");

            // Optional return type for Function lambdas
            if (isFunction && Match(TokenType.As))
            {
                lambda.ReturnType = ParseTypeReference();
            }

            // Check if this is a single-line or multi-line lambda
            SkipNewlines();

            // Multi-line lambda: check for statements after newline
            if (Check(TokenType.Dim) || Check(TokenType.If) || Check(TokenType.For) ||
                Check(TokenType.While) || Check(TokenType.Do) || Check(TokenType.Try) ||
                Check(TokenType.Return) || Check(TokenType.Throw))
            {
                // Multi-line lambda with statements
                lambda.StatementBody = new BlockNode(token.Line, token.Column);

                var endToken = isFunction ? TokenType.EndFunction : TokenType.EndSub;

                while (!Check(endToken) && !IsAtEnd())
                {
                    try
                    {
                        var stmt = ParseStatement();
                        if (stmt != null)
                        {
                            lambda.StatementBody.Statements.Add(stmt);
                        }
                    }
                    catch (ParseException ex)
                    {
                        RecordError(ex.Message, ex.Token, ex.Suggestion);
                        Synchronize();
                    }
                    SkipNewlines();
                }

                Consume(endToken, $"Expected '{(isFunction ? "End Function" : "End Sub")}' to close lambda");
            }
            else
            {
                // Single-line expression lambda
                lambda.Body = ParseExpression();
            }

            return lambda;
        }

        private LinqQueryExpressionNode ParseLinqQuery()
        {
            var token = Consume(TokenType.From, "Expected 'From'");
            var query = new LinqQueryExpressionNode(token.Line, token.Column);

            // From clause: From x In collection
            var fromClause = new FromClause { Line = token.Line, Column = token.Column };
            fromClause.VariableName = Consume(TokenType.Identifier, "Expected variable name").Lexeme;
            Consume(TokenType.In, "Expected 'In' after variable name");
            fromClause.Collection = ParseExpression();
            query.Clauses.Add(fromClause);

            // Parse additional clauses
            while (true)
            {
                if (Check(TokenType.Where))
                {
                    Advance();
                    var whereClause = new WhereClause { Line = Previous().Line, Column = Previous().Column };
                    whereClause.Condition = ParseExpression();
                    query.Clauses.Add(whereClause);
                }
                else if (Check(TokenType.OrderBy))
                {
                    Advance();
                    var orderByClause = new OrderByClause { Line = Previous().Line, Column = Previous().Column };
                    orderByClause.KeySelector = ParseExpression();
                    if (Match(TokenType.Descending))
                        orderByClause.Descending = true;
                    else
                        Match(TokenType.Ascending);  // Optional Ascending
                    query.Clauses.Add(orderByClause);
                }
                else if (Check(TokenType.GroupBy))
                {
                    Advance();
                    var groupByClause = new GroupByClause { Line = Previous().Line, Column = Previous().Column };
                    groupByClause.KeySelector = ParseExpression();

                    // Optional Into clause: Into g = Group
                    if (Match(TokenType.Into))
                    {
                        groupByClause.IntoVariable = Consume(TokenType.Identifier, "Expected variable name after Into").Lexeme;
                        Consume(TokenType.Assignment, "Expected '=' after Into variable");

                        // Check for "Group" keyword or expression
                        if (Check(TokenType.Identifier) && Peek().Lexeme.Equals("Group", StringComparison.OrdinalIgnoreCase))
                        {
                            Advance();
                            groupByClause.IsGroupKeyword = true;
                        }
                        else
                        {
                            groupByClause.ElementSelector = ParseExpression();
                        }
                    }
                    query.Clauses.Add(groupByClause);
                }
                else if (Check(TokenType.Join))
                {
                    Advance();
                    var joinClause = new JoinClause { Line = Previous().Line, Column = Previous().Column };
                    joinClause.VariableName = Consume(TokenType.Identifier, "Expected variable name").Lexeme;
                    Consume(TokenType.In, "Expected 'In' after variable name");
                    joinClause.Collection = ParseExpression();
                    Consume(TokenType.On, "Expected 'On' in join clause");
                    joinClause.OuterKeySelector = ParseExpression();
                    Consume(TokenType.Equals, "Expected 'Equals' in join clause");
                    joinClause.InnerKeySelector = ParseExpression();

                    // Optional Into for group join
                    if (Match(TokenType.Into))
                    {
                        joinClause.IntoVariable = Consume(TokenType.Identifier, "Expected variable name after Into").Lexeme;
                    }
                    query.Clauses.Add(joinClause);
                }
                else if (Check(TokenType.Aggregate))
                {
                    Advance();
                    var aggClause = new AggregateClause { Line = Previous().Line, Column = Previous().Column };
                    aggClause.VariableName = Consume(TokenType.Identifier, "Expected variable name").Lexeme;
                    Consume(TokenType.In, "Expected 'In' after variable name");
                    aggClause.Collection = ParseExpression();

                    if (Match(TokenType.Into))
                    {
                        aggClause.IntoVariable = Consume(TokenType.Identifier, "Expected variable name after Into").Lexeme;
                        Consume(TokenType.Assignment, "Expected '=' after Into variable");
                        aggClause.Selector = ParseExpression();
                    }
                    query.Clauses.Add(aggClause);
                }
                else if (Check(TokenType.Select))
                {
                    Advance();
                    var selectClause = new SelectClause { Line = Previous().Line, Column = Previous().Column };
                    selectClause.Selector = ParseExpression();
                    query.Clauses.Add(selectClause);
                    break;  // Select is usually the final clause
                }
                else if (Check(TokenType.Take))
                {
                    Advance();
                    var takeClause = new TakeClause { Line = Previous().Line, Column = Previous().Column };
                    takeClause.Count = ParseExpression();
                    query.Clauses.Add(takeClause);
                }
                else if (Check(TokenType.Skip))
                {
                    Advance();
                    var skipClause = new SkipClause { Line = Previous().Line, Column = Previous().Column };
                    skipClause.Count = ParseExpression();
                    query.Clauses.Add(skipClause);
                }
                else if (Check(TokenType.Distinct))
                {
                    Advance();
                    var distinctClause = new DistinctClause { Line = Previous().Line, Column = Previous().Column };
                    query.Clauses.Add(distinctClause);
                }
                else if (Check(TokenType.Let))
                {
                    Advance();
                    var letClause = new LetClause { Line = Previous().Line, Column = Previous().Column };
                    letClause.VariableName = Consume(TokenType.Identifier, "Expected variable name after Let").Lexeme;
                    Consume(TokenType.Assignment, "Expected '=' after variable name");
                    letClause.Value = ParseExpression();
                    query.Clauses.Add(letClause);
                }
                else
                {
                    break;  // No more clauses
                }
            }

            return query;
        }

        // ====================================================================
        // Utility Methods
        // ====================================================================

        private bool IsKeywordUsableAsMemberName(TokenType type)
        {
            // Keywords that can be used as member names after a dot
            // Only include tokens that actually exist in TokenType
            return type == TokenType.Property ||
                   type == TokenType.Type ||
                   type == TokenType.New ||
                   type == TokenType.Get ||
                   type == TokenType.Set ||
                   type == TokenType.Me ||
                   type == TokenType.Len ||
                   type == TokenType.String ||
                   type == TokenType.Integer ||
                   type == TokenType.Double ||
                   type == TokenType.Boolean;
        }

        private bool Check(TokenType type)
        {
            if (IsAtEnd()) return false;
            return Peek().Type == type;
        }

        private bool Match(params TokenType[] types)
        {
            foreach (var type in types)
            {
                if (Check(type))
                {
                    Advance();
                    return true;
                }
            }
            return false;
        }

        private Token Advance()
        {
            if (!IsAtEnd()) _current++;
            return Previous();
        }

        private Token Peek()
        {
            return _tokens[_current];
        }

        private Token PeekNext()
        {
            if (_current + 1 >= _tokens.Count) return _tokens[_current];
            return _tokens[_current + 1];
        }

        private Token Previous()
        {
            return _tokens[_current - 1];
        }

        private bool IsAtEnd()
        {
            return _current >= _tokens.Count || Peek().Type == TokenType.EOF;
        }

        private Token Consume(TokenType type, string message)
        {
            if (Check(type)) return Advance();

            // Generate suggestion based on expected token type
            string suggestion = GetSuggestionForExpectedToken(type);
            string errorMsg = message + $" but found {Peek().Type}";

            throw new ParseException(errorMsg, Peek(), suggestion);
        }

        /// <summary>
        /// Get a helpful suggestion based on the expected token type
        /// </summary>
        private string GetSuggestionForExpectedToken(TokenType expected)
        {
            switch (expected)
            {
                case TokenType.Then:
                    return "Did you forget 'Then' after the If condition?";
                case TokenType.EndIf:
                    return "Make sure your If statement is properly closed with 'End If'.";
                case TokenType.EndFunction:
                    return "Make sure your Function is properly closed with 'End Function'.";
                case TokenType.EndSub:
                    return "Make sure your Sub is properly closed with 'End Sub'.";
                case TokenType.EndClass:
                    return "Make sure your Class is properly closed with 'End Class'.";
                case TokenType.EndNamespace:
                    return "Make sure your Namespace is properly closed with 'End Namespace'.";
                case TokenType.EndModule:
                    return "Make sure your Module is properly closed with 'End Module'.";
                case TokenType.EndSelect:
                    return "Make sure your Select statement is properly closed with 'End Select'.";
                case TokenType.Wend:
                    return "Make sure your While loop is properly closed with 'Wend'.";
                case TokenType.Loop:
                    return "Make sure your Do loop is properly closed with 'Loop'.";
                case TokenType.Next:
                    return "Make sure your For loop is properly closed with 'Next'.";
                case TokenType.RightParen:
                    return "Did you forget a closing parenthesis ')'?";
                case TokenType.LeftParen:
                    return "Did you forget an opening parenthesis '('?";
                case TokenType.RightBracket:
                    return "Did you forget a closing bracket ']'?";
                case TokenType.LeftBracket:
                    return "Did you forget an opening bracket '['?";
                case TokenType.Identifier:
                    return "Expected an identifier (variable or function name).";
                case TokenType.Assignment:
                    return "Did you forget the assignment operator '='?";
                case TokenType.As:
                    return "Did you forget 'As' for type declaration?";
                case TokenType.Comma:
                    return "Multiple items should be separated by commas.";
                default:
                    return null;
            }
        }

        private void SkipNewlines()
        {
            while (Check(TokenType.Newline) && !IsAtEnd())
            {
                Advance();
            }
        }

        private void ConsumeNewlines()
        {
            if (!Check(TokenType.Newline) && !IsAtEnd())
            {
                // Optional newline consumption
                return;
            }

            while (Check(TokenType.Newline))
            {
                Advance();
            }
        }

        // ====================================================================
        // Error Recovery
        // ====================================================================

        /// <summary>
        /// Record a parsing error with context information
        /// </summary>
        private void RecordError(string message, Token token, string suggestion = null)
        {
            if (_errors.Count >= MaxErrors)
            {
                throw new TooManyErrorsException($"Too many parse errors (>{MaxErrors}). Aborting.", _errors);
            }

            if (_panicMode)
            {
                // Skip recording multiple errors while in panic mode
                return;
            }

            string contextInfo = _context.Count > 0 ? $" in {string.Join(" -> ", _context.Reverse())}" : "";
            var error = new ParseError(message, token, contextInfo, suggestion);
            _errors.Add(error);

            _panicMode = true;
        }

        /// <summary>
        /// Synchronize the parser to a known recovery point after an error
        /// </summary>
        private void Synchronize()
        {
            _panicMode = false;

            // Always advance at least one token to avoid infinite loops
            if (!IsAtEnd())
            {
                Advance();
            }

            // Skip tokens until we find a synchronization point
            while (!IsAtEnd())
            {
                // After a newline, we might be at the start of a new statement
                if (Previous().Type == TokenType.Newline)
                {
                    return;
                }

                // These tokens typically start new declarations or statements
                switch (Peek().Type)
                {
                    // Top-level synchronization points
                    case TokenType.Namespace:
                    case TokenType.Module:
                    case TokenType.Class:
                    case TokenType.Interface:
                    case TokenType.Enum:
                    case TokenType.Type:
                    case TokenType.Structure:
                    case TokenType.Function:
                    case TokenType.Sub:
                    case TokenType.Dim:
                    case TokenType.Const:
                    case TokenType.Auto:

                    // Statement synchronization points
                    case TokenType.If:
                    case TokenType.For:
                    case TokenType.While:
                    case TokenType.Do:
                    case TokenType.Select:
                    case TokenType.Try:
                    case TokenType.With:
                    case TokenType.Return:
                    case TokenType.Exit:
                    case TokenType.Throw:

                    // End tokens (block terminators)
                    case TokenType.EndIf:
                    case TokenType.EndFunction:
                    case TokenType.EndSub:
                    case TokenType.EndClass:
                    case TokenType.EndModule:
                    case TokenType.EndNamespace:
                    case TokenType.EndSelect:
                    case TokenType.Wend:
                    case TokenType.EndStructure:
                    case TokenType.EndInterface:
                    case TokenType.EndEnum:
                    case TokenType.Loop:
                    case TokenType.Next:
                    case TokenType.Else:
                    case TokenType.ElseIf:
                    case TokenType.Case:
                    case TokenType.Catch:
                    case TokenType.Finally:
                        return;
                }

                Advance();
            }
        }
    }

    /// <summary>
    /// Exception thrown during parsing
    /// </summary>
    public class ParseException : Exception
    {
        public Token Token { get; }
        public string Suggestion { get; }

        public ParseException(string message, Token token, string suggestion = null) : base(message)
        {
            Token = token;
            Suggestion = suggestion;
        }

        public override string ToString()
        {
            string result = $"Parse error at line {Token.Line}, column {Token.Column}: {Message}";
            if (!string.IsNullOrEmpty(Suggestion))
            {
                result += $"\n  Suggestion: {Suggestion}";
            }
            return result;
        }
    }

    /// <summary>
    /// Represents a parsing error with context information
    /// </summary>
    public class ParseError
    {
        public string Message { get; }
        public Token Token { get; }
        public string Context { get; }
        public string Suggestion { get; }

        public ParseError(string message, Token token, string context = null, string suggestion = null)
        {
            Message = message;
            Token = token;
            Context = context;
            Suggestion = suggestion;
        }

        public override string ToString()
        {
            string result = $"Error at line {Token.Line}, column {Token.Column}{Context}: {Message}";
            if (!string.IsNullOrEmpty(Suggestion))
            {
                result += $"\n  Suggestion: {Suggestion}";
            }
            return result;
        }
    }

    /// <summary>
    /// Exception thrown when too many errors are encountered
    /// </summary>
    public class TooManyErrorsException : Exception
    {
        public List<ParseError> CollectedErrors { get; }

        public TooManyErrorsException(string message, List<ParseError> errors = null) : base(message)
        {
            CollectedErrors = errors ?? new List<ParseError>();
        }
    }
}