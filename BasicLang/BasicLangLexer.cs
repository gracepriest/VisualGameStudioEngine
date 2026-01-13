using System;
using System.Collections.Generic;
using System.Text;

namespace BasicLang.Compiler
{
    /// <summary>
    /// Token types for BasicLang
    /// </summary>
    public enum TokenType
    {
        // End of file
        EOF,
        
        // Literals
        IntegerLiteral,
        LongLiteral,
        SingleLiteral,
        DoubleLiteral,
        StringLiteral,
        InterpolatedStringLiteral,  // $"Hello {name}"
        CharLiteral,
        BooleanLiteral,
        
        // Identifiers
        Identifier,
        
        // Keywords - Variable Declaration
        Dim,
        Auto,
        As,
        Const,
        TypeDefine,
        
        // Keywords - Data Types
        Integer,
        Long,
        Single,
        Double,
        String,
        Boolean,
        Char,
        Byte,
        Short,
        UByte,
        UShort,
        UInteger,
        ULong,
        Pointer,
        To,
        
        // Keywords - User-Defined Types
        Type,
        Structure,
        Union,
        EndType,
        EndStructure,
        EndUnion,
        
        // Keywords - Control Flow
        If,
        Then,
        Else,
        ElseIf,
        EndIf,
        Select,
        Case,
        Is,
        TypeOf,
        EndSelect,
        For,
        Next,
        While,
        Wend,
        Do,
        Loop,
        Each,
        In,
        Step,
        Exit,
        Until,
        With,
        EndWith,

        // Keywords - Functions and Subroutines
        Sub,
        Function,
        EndSub,
        EndFunction,
        Return,

        // Keywords - Parameters
        Optional,
        ParamArray,
        ByVal,
        ByRef,
        
        // Keywords - Error Handling
        Try,
        Catch,
        Finally,
        EndTry,
        Throw,
        
        // Keywords - Templates
        Template,
        Of,
        EndTemplate,
        
        // Keywords - OOP
        Class,
        EndClass,
        Private,
        Public,
        Protected,
        Friend,
        Inherits,
        Interface,
        EndInterface,
        Enum,
        EndEnum,
        Implements,
        New,
        Me,
        MyBase,

        // Keywords - OOP Properties
        Property,
        EndProperty,
        Get,
        EndGet,
        Set,
        EndSet,
        ReadOnly,
        WriteOnly,

        // Keywords - Operator Overloading
        EndOperator,

        // Keywords - OOP Modifiers
        Shared,          // Static
        Overridable,     // Virtual
        Overrides,       // Override
        MustOverride,    // Abstract method
        MustInherit,     // Abstract class
        NotOverridable,  // Sealed
        Operator,        // For operator overloading
        Widening,        // For widening conversion
        Narrowing,       // For narrowing conversion
        Inline,          // Inline function (for headers)

        // Keywords - Namespaces and Modules
        Namespace,
        EndNamespace,
        Using,
        EndUsing,
        Module,
        EndModule,
        Import,
        
        // Keywords - Extensions
        Extension,

        // Keywords - LINQ
        From,
        Where,
        OrderBy,
        Ascending,
        Descending,
        GroupBy,
        Join,
        On,
        Equals,
        Into,
        Let,
        Aggregate,
        Take,
        Skip,
        Distinct,
        Any,
        All,
        First,
        Last,

        // Keywords - Delegates and Events
        Delegate,
        Event,
        RaiseEvent,
        AddHandler,
        RemoveHandler,
        Handles,

        // Keywords - Async
        Async,
        Await,

        // Keywords - Iterators
        Yield,
        Iterator,

        // Keywords - Pattern Matching
        When,       // Guard clause: Case x When x > 0
        Nothing,    // Null pattern: Case Nothing

        // Keywords - Platform Externs
        Extern,
        EndExtern,
        Declare,       // Declare Function/Sub for C library imports
        Lib,           // Library name in Declare statement
        Alias,         // Alias name in Declare statement
        CDecl,         // Calling convention
        StdCall,       // Calling convention

        // Keywords - Inline Code Blocks
        InlineCSharp,      // csharp{ ... }
        InlineCpp,         // cpp{ ... }
        InlineLLVM,        // llvm{ ... }
        InlineMSIL,        // msil{ ... }
        InlineCode,        // Generic inline code token with content

        // Preprocessor Directives
        PreprocessorDefine,    // #Define
        PreprocessorUndefine,  // #Undefine
        PreprocessorIf,        // #If
        PreprocessorElseIf,    // #ElseIf
        PreprocessorElse,      // #Else
        PreprocessorEndIf,     // #EndIf
        PreprocessorInclude,   // #Include
        PreprocessorConst,     // #Const
        PreprocessorRegion,    // #Region
        PreprocessorEndRegion, // #End Region

        // Keywords - Functions/Pointers
        AddressOf,
        Deref,
        Len,
        Mid,
        UBound,
        SizeOf,
        AllocateMemory,
        DeallocateMemory,
        
        // Operators - Arithmetic
        Plus,
        Minus,
        Multiply,
        Divide,
        IntegerDivide,
        Modulo,
        Power,
        
        // Operators - Comparison
        Equal,
        NotEqual,
        LessThan,
        LessThanOrEqual,
        GreaterThan,
        GreaterThanOrEqual,
        IsEqual,
        
        // Operators - Logical
        And,
        Or,
        Not,
        AndAnd,
        OrOr,
        Bang,
        
        // Operators - Assignment
        Assignment,
        PlusAssign,
        MinusAssign,
        MultiplyAssign,
        DivideAssign,
        
        // Operators - Increment/Decrement
        Increment,
        Decrement,
        
        // Operators - Bitwise
        BitwiseAnd,
        BitwiseOr,
        BitwiseXor,
        BitwiseNot,
        LeftShift,
        RightShift,
        
        // Operators - String
        Concatenate,
        
        // Punctuation
        LeftParen,
        RightParen,
        LeftBracket,
        RightBracket,
        LeftBrace,
        RightBrace,
        Comma,
        Dot,
        Colon,
        Semicolon,
        Apostrophe,
        Caret,
        QuestionMark,

        // Special
        Newline,
        Comment,
        
        // Error
        Unknown
    }
    
    /// <summary>
    /// Represents a lexical token
    /// </summary>
    public class Token
    {
        public TokenType Type { get; set; }
        public string Lexeme { get; set; }
        public object Value { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
        
        public Token(TokenType type, string lexeme, object value, int line, int column)
        {
            Type = type;
            Lexeme = lexeme;
            Value = value;
            Line = line;
            Column = column;
        }
        
        public override string ToString()
        {
            return $"[{Type}] '{Lexeme}' at ({Line}, {Column})";
        }
    }

    /// <summary>
    /// Holds the language and code content for inline code blocks
    /// </summary>
    public class InlineCodeValue
    {
        public string Language { get; }  // "csharp", "cpp", "llvm", "msil"
        public string Code { get; }

        public InlineCodeValue(string language, string code)
        {
            Language = language;
            Code = code;
        }
    }

    /// <summary>
    /// Lexer for BasicLang
    /// </summary>
    public class Lexer
    {
        private readonly string _source;
        private int _position;
        private int _line;
        private int _column;
        private readonly List<Token> _tokens;
        
        private static readonly Dictionary<string, TokenType> _keywords = new Dictionary<string, TokenType>(StringComparer.OrdinalIgnoreCase)
        {
            // Variable Declaration
            { "Dim", TokenType.Dim },
            { "Auto", TokenType.Auto },
            { "As", TokenType.As },
            { "Const", TokenType.Const },
            { "TypeDefine", TokenType.TypeDefine },
            
            // Data Types
            { "Integer", TokenType.Integer },
            { "Long", TokenType.Long },
            { "Single", TokenType.Single },
            { "Double", TokenType.Double },
            { "String", TokenType.String },
            { "Boolean", TokenType.Boolean },
            { "Char", TokenType.Char },
            { "Byte", TokenType.Byte },
            { "Short", TokenType.Short },
            { "UByte", TokenType.UByte },
            { "UShort", TokenType.UShort },
            { "UInteger", TokenType.UInteger },
            { "ULong", TokenType.ULong },
            { "Pointer", TokenType.Pointer },
            { "To", TokenType.To },
            
            // User-Defined Types
            { "Type", TokenType.Type },
            { "Structure", TokenType.Structure },
            { "Union", TokenType.Union },
            { "End Type", TokenType.EndType },
            { "End Structure", TokenType.EndStructure },
            { "End Union", TokenType.EndUnion },
            
            // Control Flow
            { "If", TokenType.If },
            { "Then", TokenType.Then },
            { "Else", TokenType.Else },
            { "ElseIf", TokenType.ElseIf },
            { "End If", TokenType.EndIf },
            { "Select", TokenType.Select },
            { "Case", TokenType.Case },
            { "Is", TokenType.Is },
            { "TypeOf", TokenType.TypeOf },
            { "End Select", TokenType.EndSelect },
            { "For", TokenType.For },
            { "Next", TokenType.Next },
            { "While", TokenType.While },
            { "Wend", TokenType.Wend },
            { "Do", TokenType.Do },
            { "Loop", TokenType.Loop },
            { "Each", TokenType.Each },
            { "In", TokenType.In },
            { "Step", TokenType.Step },
            { "Exit", TokenType.Exit },
            { "Until", TokenType.Until },
            { "With", TokenType.With },
            { "End With", TokenType.EndWith },
            { "Optional", TokenType.Optional },
            { "ParamArray", TokenType.ParamArray },
            { "ByVal", TokenType.ByVal },
            { "ByRef", TokenType.ByRef },

            // Functions and Subroutines
            { "Sub", TokenType.Sub },
            { "Function", TokenType.Function },
            { "End Sub", TokenType.EndSub },
            { "End Function", TokenType.EndFunction },
            { "Return", TokenType.Return },
            
            // Error Handling
            { "Try", TokenType.Try },
            { "Catch", TokenType.Catch },
            { "Finally", TokenType.Finally },
            { "End Try", TokenType.EndTry },
            { "Throw", TokenType.Throw },
            
            // Templates
            { "Template", TokenType.Template },
            { "Of", TokenType.Of },
            { "End Template", TokenType.EndTemplate },
            
            // OOP
            { "Class", TokenType.Class },
            { "End Class", TokenType.EndClass },
            { "Private", TokenType.Private },
            { "Public", TokenType.Public },
            { "Protected", TokenType.Protected },
            { "Friend", TokenType.Friend },
            { "Inherits", TokenType.Inherits },
            { "Interface", TokenType.Interface },
            { "End Interface", TokenType.EndInterface },
            { "Enum", TokenType.Enum },
            { "End Enum", TokenType.EndEnum },
            { "Implements", TokenType.Implements },
            { "New", TokenType.New },
            { "Me", TokenType.Me },
            { "MyBase", TokenType.MyBase },

            // OOP Properties
            { "Property", TokenType.Property },
            { "End Property", TokenType.EndProperty },
            { "Get", TokenType.Get },
            { "End Get", TokenType.EndGet },
            { "Set", TokenType.Set },
            { "End Set", TokenType.EndSet },
            { "ReadOnly", TokenType.ReadOnly },
            { "WriteOnly", TokenType.WriteOnly },

            // Operator Overloading
            { "Operator", TokenType.Operator },
            { "End Operator", TokenType.EndOperator },

            // OOP Modifiers
            { "Shared", TokenType.Shared },
            { "Overridable", TokenType.Overridable },
            { "Overrides", TokenType.Overrides },
            { "MustOverride", TokenType.MustOverride },
            { "MustInherit", TokenType.MustInherit },
            { "NotOverridable", TokenType.NotOverridable },
            { "Widening", TokenType.Widening },
            { "Narrowing", TokenType.Narrowing },
            { "Inline", TokenType.Inline },

            // Namespaces and Modules
            { "Namespace", TokenType.Namespace },
            { "End Namespace", TokenType.EndNamespace },
            { "Using", TokenType.Using },
            { "End Using", TokenType.EndUsing },
            { "Module", TokenType.Module },
            { "End Module", TokenType.EndModule },
            { "Import", TokenType.Import },
            
            // Extensions
            { "Extension", TokenType.Extension },

            // LINQ
            { "From", TokenType.From },
            { "Where", TokenType.Where },
            { "Order By", TokenType.OrderBy },
            { "Ascending", TokenType.Ascending },
            { "Descending", TokenType.Descending },
            { "Group By", TokenType.GroupBy },
            { "Join", TokenType.Join },
            { "On", TokenType.On },
            { "Equals", TokenType.Equals },
            { "Into", TokenType.Into },
            { "Let", TokenType.Let },
            { "Aggregate", TokenType.Aggregate },
            { "Take", TokenType.Take },
            { "Skip", TokenType.Skip },
            { "Distinct", TokenType.Distinct },
            { "Any", TokenType.Any },
            { "All", TokenType.All },
            { "First", TokenType.First },
            { "Last", TokenType.Last },

            // Delegates
            { "Delegate", TokenType.Delegate },
            { "Event", TokenType.Event },
            { "RaiseEvent", TokenType.RaiseEvent },
            { "AddHandler", TokenType.AddHandler },
            { "RemoveHandler", TokenType.RemoveHandler },
            { "Handles", TokenType.Handles },

            // Async
            { "Async", TokenType.Async },
            { "Await", TokenType.Await },

            // Iterators
            { "Yield", TokenType.Yield },
            { "Iterator", TokenType.Iterator },

            // Pattern Matching
            { "When", TokenType.When },
            { "Nothing", TokenType.Nothing },

            // Platform Externs
            { "Extern", TokenType.Extern },
            { "End Extern", TokenType.EndExtern },
            { "Declare", TokenType.Declare },
            { "Lib", TokenType.Lib },
            { "Alias", TokenType.Alias },
            { "CDecl", TokenType.CDecl },
            { "StdCall", TokenType.StdCall },

            // Inline Code Blocks
            { "csharp", TokenType.InlineCSharp },
            { "cpp", TokenType.InlineCpp },
            { "llvm", TokenType.InlineLLVM },
            { "msil", TokenType.InlineMSIL },

            // Built-in Functions (special syntax, not regular function calls)
            { "AddressOf", TokenType.AddressOf },
            { "Deref", TokenType.Deref },
            { "SizeOf", TokenType.SizeOf },
            { "AllocateMemory", TokenType.AllocateMemory },
            { "DeallocateMemory", TokenType.DeallocateMemory },
            // Note: Len, Mid, UBound are stdlib functions, not keywords - they parse as identifiers
            
            // Logical Operators
            { "And", TokenType.And },
            { "Or", TokenType.Or },
            { "Not", TokenType.Not },
            { "NotEqual", TokenType.NotEqual },
            { "IsEqual", TokenType.IsEqual },

            // Arithmetic Operators (keywords)
            { "Mod", TokenType.Modulo },
            
            // Boolean Literals
            { "True", TokenType.BooleanLiteral },
            { "False", TokenType.BooleanLiteral },
        };
        
        public Lexer(string source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _position = 0;
            _line = 1;
            _column = 1;
            _tokens = new List<Token>();
        }
        
        /// <summary>
        /// Tokenize the source code
        /// </summary>
        public List<Token> Tokenize()
        {
            while (!IsAtEnd())
            {
                SkipWhitespace();
                
                if (IsAtEnd())
                    break;
                    
                ScanToken();
            }
            
            AddToken(TokenType.EOF, "", null);
            return _tokens;
        }
        
        private void ScanToken()
        {
            int startLine = _line;
            int startColumn = _column;
            char c = Advance();
            
            switch (c)
            {
                // Single character tokens
                case '(': AddToken(TokenType.LeftParen, "(", null, startLine, startColumn); break;
                case ')': AddToken(TokenType.RightParen, ")", null, startLine, startColumn); break;
                case '[': AddToken(TokenType.LeftBracket, "[", null, startLine, startColumn); break;
                case ']': AddToken(TokenType.RightBracket, "]", null, startLine, startColumn); break;
                case '{': AddToken(TokenType.LeftBrace, "{", null, startLine, startColumn); break;
                case '}': AddToken(TokenType.RightBrace, "}", null, startLine, startColumn); break;
                case ',': AddToken(TokenType.Comma, ",", null, startLine, startColumn); break;
                case '.': AddToken(TokenType.Dot, ".", null, startLine, startColumn); break;
                case ':': AddToken(TokenType.Colon, ":", null, startLine, startColumn); break;
                case ';': AddToken(TokenType.Semicolon, ";", null, startLine, startColumn); break;
                case '^': AddToken(TokenType.Caret, "^", null, startLine, startColumn); break;
                case '~': AddToken(TokenType.BitwiseNot, "~", null, startLine, startColumn); break;
                case '?': AddToken(TokenType.QuestionMark, "?", null, startLine, startColumn); break;

                // Operators that can be multi-character
                case '+':
                    if (Match('+'))
                        AddToken(TokenType.Increment, "++", null, startLine, startColumn);
                    else if (Match('='))
                        AddToken(TokenType.PlusAssign, "+=", null, startLine, startColumn);
                    else
                        AddToken(TokenType.Plus, "+", null, startLine, startColumn);
                    break;
                    
                case '-':
                    if (Match('-'))
                        AddToken(TokenType.Decrement, "--", null, startLine, startColumn);
                    else if (Match('='))
                        AddToken(TokenType.MinusAssign, "-=", null, startLine, startColumn);
                    else
                        AddToken(TokenType.Minus, "-", null, startLine, startColumn);
                    break;
                    
                case '*':
                    if (Match('='))
                        AddToken(TokenType.MultiplyAssign, "*=", null, startLine, startColumn);
                    else
                        AddToken(TokenType.Multiply, "*", null, startLine, startColumn);
                    break;
                    
                case '/':
                    if (Match('='))
                        AddToken(TokenType.DivideAssign, "/=", null, startLine, startColumn);
                    else
                        AddToken(TokenType.Divide, "/", null, startLine, startColumn);
                    break;
                    
                case '\\':
                    AddToken(TokenType.IntegerDivide, "\\", null, startLine, startColumn);
                    break;
                    
                case '%':
                    AddToken(TokenType.Modulo, "%", null, startLine, startColumn);
                    break;
                    
                case '=':
                    if (Match('='))
                        AddToken(TokenType.IsEqual, "==", null, startLine, startColumn);
                    else if (Match('+'))
                        AddToken(TokenType.PlusAssign, "=+", null, startLine, startColumn);
                    else if (Match('-'))
                        AddToken(TokenType.MinusAssign, "=-", null, startLine, startColumn);
                    else
                        AddToken(TokenType.Assignment, "=", null, startLine, startColumn);
                    break;
                    
                case '<':
                    if (Match('='))
                        AddToken(TokenType.LessThanOrEqual, "<=", null, startLine, startColumn);
                    else if (Match('<'))
                        AddToken(TokenType.LeftShift, "<<", null, startLine, startColumn);
                    else if (Match('>'))
                        AddToken(TokenType.NotEqual, "<>", null, startLine, startColumn);
                    else
                        AddToken(TokenType.LessThan, "<", null, startLine, startColumn);
                    break;
                    
                case '>':
                    if (Match('='))
                        AddToken(TokenType.GreaterThanOrEqual, ">=", null, startLine, startColumn);
                    else if (Match('>'))
                        AddToken(TokenType.RightShift, ">>", null, startLine, startColumn);
                    else
                        AddToken(TokenType.GreaterThan, ">", null, startLine, startColumn);
                    break;
                    
                case '!':
                    if (Match('='))
                        AddToken(TokenType.NotEqual, "!=", null, startLine, startColumn);
                    else
                        AddToken(TokenType.Bang, "!", null, startLine, startColumn);
                    break;
                    
                case '&':
                    if (Match('&'))
                        AddToken(TokenType.AndAnd, "&&", null, startLine, startColumn);
                    else
                        AddToken(TokenType.Concatenate, "&", null, startLine, startColumn);
                    break;
                    
                case '|':
                    if (Match('|'))
                        AddToken(TokenType.OrOr, "||", null, startLine, startColumn);
                    else
                        AddToken(TokenType.BitwiseOr, "|", null, startLine, startColumn);
                    break;
                    
                case '\'':
                    // Comment until end of line
                    ScanComment(startLine, startColumn);
                    break;
                    
                case '#':
                    // Compilation directive
                    ScanDirective(startLine, startColumn);
                    break;
                    
                case '$':
                    // Check for interpolated string $"..."
                    if (Peek() == '"')
                    {
                        Advance(); // consume the opening quote
                        ScanInterpolatedString(startLine, startColumn);
                    }
                    else
                    {
                        AddToken(TokenType.Unknown, "$", null, startLine, startColumn);
                    }
                    break;

                case '"':
                    // String literal
                    ScanString(startLine, startColumn);
                    break;
                    
                case '\n':
                    AddToken(TokenType.Newline, "\\n", null, startLine, startColumn);
                    _line++;
                    _column = 1;
                    break;
                    
                case '\r':
                    if (Match('\n'))
                    {
                        AddToken(TokenType.Newline, "\\r\\n", null, startLine, startColumn);
                        _line++;
                        _column = 1;
                    }
                    break;
                    
                default:
                    if (IsDigit(c))
                    {
                        ScanNumber(c, startLine, startColumn);
                    }
                    else if (IsAlpha(c))
                    {
                        ScanIdentifierOrKeyword(c, startLine, startColumn);
                    }
                    else
                    {
                        AddToken(TokenType.Unknown, c.ToString(), null, startLine, startColumn);
                    }
                    break;
            }
        }
        
        private void ScanComment(int startLine, int startColumn)
        {
            StringBuilder sb = new StringBuilder("'");
            
            while (!IsAtEnd() && Peek() != '\n' && Peek() != '\r')
            {
                sb.Append(Advance());
            }
            
            AddToken(TokenType.Comment, sb.ToString(), null, startLine, startColumn);
        }
        
        private void ScanDirective(int startLine, int startColumn)
        {
            StringBuilder sb = new StringBuilder("#");

            while (!IsAtEnd() && IsAlphaNumeric(Peek()))
            {
                sb.Append(Advance());
            }

            string directive = sb.ToString();
            TokenType type;

            switch (directive.ToLower())
            {
                case "#if":
                    type = TokenType.PreprocessorIf;
                    break;
                case "#elseif":
                    type = TokenType.PreprocessorElseIf;
                    break;
                case "#else":
                    type = TokenType.PreprocessorElse;
                    break;
                case "#endif":
                    type = TokenType.PreprocessorEndIf;
                    break;
                case "#define":
                    type = TokenType.PreprocessorDefine;
                    break;
                case "#undef":
                case "#undefine":
                    type = TokenType.PreprocessorUndefine;
                    break;
                case "#include":
                    type = TokenType.PreprocessorInclude;
                    break;
                case "#const":
                    type = TokenType.PreprocessorConst;
                    break;
                case "#region":
                    type = TokenType.PreprocessorRegion;
                    break;
                default:
                    // Check for #End Region (with space)
                    if (directive.ToLower() == "#end")
                    {
                        SkipWhitespace();
                        if (!IsAtEnd() && IsAlpha(Peek()))
                        {
                            var nextWord = new StringBuilder();
                            while (!IsAtEnd() && IsAlphaNumeric(Peek()))
                            {
                                nextWord.Append(Advance());
                            }
                            if (nextWord.ToString().Equals("region", StringComparison.OrdinalIgnoreCase))
                            {
                                type = TokenType.PreprocessorEndRegion;
                                directive = "#End Region";
                            }
                            else
                            {
                                type = TokenType.Unknown;
                            }
                        }
                        else
                        {
                            type = TokenType.Unknown;
                        }
                    }
                    else
                    {
                        type = TokenType.Unknown;
                    }
                    break;
            }

            AddToken(type, directive, null, startLine, startColumn);
        }
        
        private void ScanString(int startLine, int startColumn)
        {
            StringBuilder sb = new StringBuilder();
            
            while (!IsAtEnd() && Peek() != '"')
            {
                if (Peek() == '\\')
                {
                    Advance(); // Consume backslash
                    if (!IsAtEnd())
                    {
                        char escaped = Advance();
                        switch (escaped)
                        {
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case '\\': sb.Append('\\'); break;
                            case '"': sb.Append('"'); break;
                            default: sb.Append(escaped); break;
                        }
                    }
                }
                else
                {
                    if (Peek() == '\n')
                    {
                        _line++;
                        _column = 0;
                    }
                    sb.Append(Advance());
                }
            }
            
            if (IsAtEnd())
            {
                throw new LexerException(
                    ErrorCode.BL1001_UnterminatedString,
                    "Unterminated string literal. Did you forget the closing quote?",
                    startLine,
                    startColumn,
                    _source);
            }
            
            Advance(); // Consume closing quote
            
            AddToken(TokenType.StringLiteral, $"\"{sb}\"", sb.ToString(), startLine, startColumn);
        }

        private void ScanInterpolatedString(int startLine, int startColumn)
        {
            // Stores the raw content of the interpolated string including {expressions}
            StringBuilder sb = new StringBuilder();

            while (!IsAtEnd() && Peek() != '"')
            {
                if (Peek() == '\\')
                {
                    Advance(); // Consume backslash
                    if (!IsAtEnd())
                    {
                        char escaped = Advance();
                        switch (escaped)
                        {
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case '\\': sb.Append('\\'); break;
                            case '"': sb.Append('"'); break;
                            case '{': sb.Append('{'); break;
                            case '}': sb.Append('}'); break;
                            default: sb.Append(escaped); break;
                        }
                    }
                }
                else if (Peek() == '{')
                {
                    sb.Append(Advance()); // Add the '{'
                    // Read until matching '}'
                    int braceDepth = 1;
                    while (!IsAtEnd() && braceDepth > 0)
                    {
                        char c = Advance();
                        sb.Append(c);
                        if (c == '{') braceDepth++;
                        else if (c == '}') braceDepth--;
                    }
                }
                else
                {
                    if (Peek() == '\n')
                    {
                        _line++;
                        _column = 0;
                    }
                    sb.Append(Advance());
                }
            }

            if (IsAtEnd())
            {
                throw new LexerException(
                    ErrorCode.BL1002_UnterminatedInterpolatedString,
                    "Unterminated interpolated string literal. Did you forget the closing quote?",
                    startLine,
                    startColumn,
                    _source);
            }

            Advance(); // Consume closing quote

            AddToken(TokenType.InterpolatedStringLiteral, $"$\"{sb}\"", sb.ToString(), startLine, startColumn);
        }

        private void ScanNumber(char firstDigit, int startLine, int startColumn)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(firstDigit);
            
            // Read integer part
            while (!IsAtEnd() && IsDigit(Peek()))
            {
                sb.Append(Advance());
            }
            
            // Check for decimal point (Single or Double)
            if (!IsAtEnd() && Peek() == '.' && _position + 1 < _source.Length && IsDigit(_source[_position + 1]))
            {
                sb.Append(Advance()); // Consume '.'
                
                while (!IsAtEnd() && IsDigit(Peek()))
                {
                    sb.Append(Advance());
                }
                
                // Check for exponent
                if (!IsAtEnd() && (Peek() == 'e' || Peek() == 'E'))
                {
                    sb.Append(Advance()); // Consume 'e' or 'E'
                    
                    if (!IsAtEnd() && (Peek() == '+' || Peek() == '-'))
                    {
                        sb.Append(Advance());
                    }
                    
                    while (!IsAtEnd() && IsDigit(Peek()))
                    {
                        sb.Append(Advance());
                    }
                }
                
                // Check for type suffix
                if (!IsAtEnd() && (Peek() == 'f' || Peek() == 'F'))
                {
                    sb.Append(Advance());
                    float value = float.Parse(sb.ToString().TrimEnd('f', 'F'));
                    AddToken(TokenType.SingleLiteral, sb.ToString(), value, startLine, startColumn);
                }
                else
                {
                    double value = double.Parse(sb.ToString());
                    AddToken(TokenType.DoubleLiteral, sb.ToString(), value, startLine, startColumn);
                }
            }
            else
            {
                // Check for type suffix
                if (!IsAtEnd() && (Peek() == 'L' || Peek() == 'l'))
                {
                    sb.Append(Advance());
                    long value = long.Parse(sb.ToString().TrimEnd('L', 'l'));
                    AddToken(TokenType.LongLiteral, sb.ToString(), value, startLine, startColumn);
                }
                else if (!IsAtEnd() && (Peek() == 'f' || Peek() == 'F'))
                {
                    sb.Append(Advance());
                    float value = float.Parse(sb.ToString().TrimEnd('f', 'F'));
                    AddToken(TokenType.SingleLiteral, sb.ToString(), value, startLine, startColumn);
                }
                else
                {
                    int value = int.Parse(sb.ToString());
                    AddToken(TokenType.IntegerLiteral, sb.ToString(), value, startLine, startColumn);
                }
            }
        }
        
        private void ScanIdentifierOrKeyword(char firstChar, int startLine, int startColumn)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(firstChar);

            while (!IsAtEnd() && IsAlphaNumeric(Peek()))
            {
                sb.Append(Advance());
            }

            string identifier = sb.ToString();

            // After a dot, treat everything as an identifier (member access)
            // This allows using keywords as member names like obj.Property
            if (_tokens.Count > 0 && _tokens[_tokens.Count - 1].Type == TokenType.Dot)
            {
                AddToken(TokenType.Identifier, identifier, identifier, startLine, startColumn);
                return;
            }

            // Check for multi-word keywords (e.g., "End If", "End Sub")
            if (identifier.Equals("End", StringComparison.OrdinalIgnoreCase))
            {
                SkipWhitespaceNoNewline();

                if (!IsAtEnd() && IsAlpha(Peek()))
                {
                    StringBuilder secondWord = new StringBuilder();
                    while (!IsAtEnd() && IsAlphaNumeric(Peek()))
                    {
                        secondWord.Append(Advance());
                    }

                    string multiWord = $"{identifier} {secondWord}";

                    if (_keywords.TryGetValue(multiWord, out TokenType keywordType))
                    {
                        AddToken(keywordType, multiWord, null, startLine, startColumn);
                        return;
                    }
                }
            }

            // Check for single-word keyword
            if (_keywords.TryGetValue(identifier, out TokenType type))
            {
                object value = null;

                // Handle boolean literals
                if (type == TokenType.BooleanLiteral)
                {
                    value = identifier.Equals("True", StringComparison.OrdinalIgnoreCase);
                }

                // Handle inline code blocks: csharp{ }, cpp{ }, llvm{ }, msil{ }
                if (type == TokenType.InlineCSharp || type == TokenType.InlineCpp ||
                    type == TokenType.InlineLLVM || type == TokenType.InlineMSIL)
                {
                    SkipWhitespace();
                    if (!IsAtEnd() && Peek() == '{')
                    {
                        Advance(); // Consume '{'
                        string code = ScanInlineCodeContent();
                        string language = identifier.ToLower();
                        AddToken(TokenType.InlineCode, $"{language}{{{code}}}", new InlineCodeValue(language, code), startLine, startColumn);
                        return;
                    }
                }

                AddToken(type, identifier, value, startLine, startColumn);
            }
            else
            {
                AddToken(TokenType.Identifier, identifier, identifier, startLine, startColumn);
            }
        }
        
        private void SkipWhitespace()
        {
            while (!IsAtEnd())
            {
                char c = Peek();
                
                if (c == ' ' || c == '\t')
                {
                    Advance();
                }
                else
                {
                    break;
                }
            }
        }
        
        private void SkipWhitespaceNoNewline()
        {
            while (!IsAtEnd())
            {
                char c = Peek();
                
                if (c == ' ' || c == '\t')
                {
                    Advance();
                }
                else
                {
                    break;
                }
            }
        }
        
        private bool IsDigit(char c)
        {
            return c >= '0' && c <= '9';
        }
        
        private bool IsAlpha(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';
        }
        
        private bool IsAlphaNumeric(char c)
        {
            return IsAlpha(c) || IsDigit(c);
        }

        private char Peek()
        {
            if (IsAtEnd())
                return '\0';
            return _source[_position];
        }
        
        private char PeekNext()
        {
            if (_position + 1 >= _source.Length)
                return '\0';
            return _source[_position + 1];
        }
        
        private bool Match(char expected)
        {
            if (IsAtEnd() || _source[_position] != expected)
                return false;
                
            Advance();
            return true;
        }
        
        private char Advance()
        {
            _column++;
            return _source[_position++];
        }
        
        private bool IsAtEnd()
        {
            return _position >= _source.Length;
        }

        private string ScanInlineCodeContent()
        {
            StringBuilder sb = new StringBuilder();
            int braceDepth = 1;

            while (!IsAtEnd() && braceDepth > 0)
            {
                char c = Advance();

                if (c == '{')
                {
                    braceDepth++;
                    sb.Append(c);
                }
                else if (c == '}')
                {
                    braceDepth--;
                    if (braceDepth > 0)
                    {
                        sb.Append(c);
                    }
                    // Don't append the final closing brace
                }
                else
                {
                    if (c == '\n')
                    {
                        _line++;
                        _column = 1;
                    }
                    sb.Append(c);
                }
            }

            if (braceDepth > 0)
            {
                throw new LexerException(
                    ErrorCode.BL2004_MismatchedBlock,
                    "Unterminated inline code block. Did you forget the closing '}'?",
                    _line,
                    _column,
                    _source);
            }

            return sb.ToString().Trim();
        }

        private void AddToken(TokenType type, string lexeme, object value)
        {
            AddToken(type, lexeme, value, _line, _column - lexeme.Length);
        }
        
        private void AddToken(TokenType type, string lexeme, object value, int line, int column)
        {
            _tokens.Add(new Token(type, lexeme, value, line, column));
        }
    }
}
