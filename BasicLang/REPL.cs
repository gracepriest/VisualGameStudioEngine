using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.Interpreter;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;

namespace BasicLang.Compiler.Repl
{
    /// <summary>
    /// Enhanced REPL (Read-Eval-Print-Loop) for BasicLang with multi-line support,
    /// state management, and special commands.
    /// </summary>
    public class REPL
    {
        private readonly IRInterpreter _interpreter;
        private readonly List<string> _history;
        private readonly Dictionary<string, IRFunction> _userFunctions;
        private readonly SemanticAnalyzer _semanticAnalyzer;
        private int _historyIndex;
        private bool _running;
        private int _statementCounter;

        public REPL()
        {
            _interpreter = new IRInterpreter();
            _history = new List<string>();
            _userFunctions = new Dictionary<string, IRFunction>(StringComparer.OrdinalIgnoreCase);
            _semanticAnalyzer = new SemanticAnalyzer();
            _historyIndex = 0;
            _running = false;
            _statementCounter = 0;
        }

        /// <summary>
        /// Start the REPL interactive session
        /// </summary>
        public void Run()
        {
            _running = true;
            PrintWelcomeBanner();

            while (_running)
            {
                try
                {
                    // Read input (potentially multi-line)
                    var input = ReadInput();

                    if (string.IsNullOrWhiteSpace(input))
                        continue;

                    // Add to history
                    _history.Add(input);
                    _historyIndex = _history.Count;

                    // Handle special commands
                    if (HandleSpecialCommand(input))
                        continue;

                    // Execute BasicLang code
                    ExecuteCode(input);
                }
                catch (Exception ex)
                {
                    PrintError($"Unexpected error: {ex.Message}");
                    if (ex.StackTrace != null)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkRed;
                        Console.WriteLine(ex.StackTrace);
                        Console.ResetColor();
                    }
                }
            }

            Console.WriteLine("\nGoodbye!");
        }

        /// <summary>
        /// Print the welcome banner
        /// </summary>
        private void PrintWelcomeBanner()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("╔════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                 BasicLang Interactive REPL                     ║");
            Console.WriteLine("║                        Version 1.0                             ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════════╝");
            Console.ResetColor();
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Type :help for available commands");
            Console.ResetColor();
            Console.WriteLine();
        }

        /// <summary>
        /// Read input with multi-line support
        /// Detects incomplete statements and continues reading
        /// </summary>
        private string ReadInput()
        {
            var sb = new StringBuilder();
            var lineCount = 0;
            var needsContinuation = false;

            do
            {
                // Print prompt
                if (lineCount == 0)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write(">>> ");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.DarkGreen;
                    Console.Write("... ");
                    Console.ResetColor();
                }

                var line = ReadLineWithHistory();

                if (line == null) // EOF (Ctrl+D or Ctrl+Z)
                {
                    _running = false;
                    return "";
                }

                lineCount++;

                // Append line to buffer
                if (sb.Length > 0)
                    sb.AppendLine();
                sb.Append(line);

                // Check if we need to continue reading
                needsContinuation = NeedsContinuation(sb.ToString(), line);

            } while (needsContinuation);

            return sb.ToString();
        }

        /// <summary>
        /// Read a line with history support (up/down arrows)
        /// </summary>
        private string ReadLineWithHistory()
        {
            var input = new StringBuilder();
            int cursorPos = 0;

            while (true)
            {
                var key = Console.ReadKey(intercept: true);

                switch (key.Key)
                {
                    case ConsoleKey.Enter:
                        Console.WriteLine();
                        return input.ToString();

                    case ConsoleKey.Backspace:
                        if (cursorPos > 0)
                        {
                            input.Remove(cursorPos - 1, 1);
                            cursorPos--;
                            RedrawLine(input.ToString(), cursorPos);
                        }
                        break;

                    case ConsoleKey.Delete:
                        if (cursorPos < input.Length)
                        {
                            input.Remove(cursorPos, 1);
                            RedrawLine(input.ToString(), cursorPos);
                        }
                        break;

                    case ConsoleKey.LeftArrow:
                        if (cursorPos > 0)
                        {
                            cursorPos--;
                            Console.SetCursorPosition(Console.CursorLeft - 1, Console.CursorTop);
                        }
                        break;

                    case ConsoleKey.RightArrow:
                        if (cursorPos < input.Length)
                        {
                            cursorPos++;
                            Console.SetCursorPosition(Console.CursorLeft + 1, Console.CursorTop);
                        }
                        break;

                    case ConsoleKey.UpArrow:
                        if (_historyIndex > 0)
                        {
                            _historyIndex--;
                            input.Clear();
                            input.Append(_history[_historyIndex]);
                            cursorPos = input.Length;
                            RedrawLine(input.ToString(), cursorPos);
                        }
                        break;

                    case ConsoleKey.DownArrow:
                        if (_historyIndex < _history.Count - 1)
                        {
                            _historyIndex++;
                            input.Clear();
                            input.Append(_history[_historyIndex]);
                            cursorPos = input.Length;
                            RedrawLine(input.ToString(), cursorPos);
                        }
                        else if (_historyIndex == _history.Count - 1)
                        {
                            _historyIndex = _history.Count;
                            input.Clear();
                            cursorPos = 0;
                            RedrawLine("", 0);
                        }
                        break;

                    case ConsoleKey.Home:
                        cursorPos = 0;
                        Console.SetCursorPosition(4, Console.CursorTop); // After ">>> "
                        break;

                    case ConsoleKey.End:
                        cursorPos = input.Length;
                        Console.SetCursorPosition(4 + input.Length, Console.CursorTop);
                        break;

                    case ConsoleKey.Tab:
                        // Tab completion could be implemented here
                        input.Insert(cursorPos, "    ");
                        cursorPos += 4;
                        RedrawLine(input.ToString(), cursorPos);
                        break;

                    default:
                        if (!char.IsControl(key.KeyChar))
                        {
                            input.Insert(cursorPos, key.KeyChar);
                            cursorPos++;
                            RedrawLine(input.ToString(), cursorPos);
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// Redraw the current input line
        /// </summary>
        private void RedrawLine(string text, int cursorPos)
        {
            // Save cursor position
            var top = Console.CursorTop;

            // Clear line
            Console.SetCursorPosition(4, top); // After prompt
            Console.Write(new string(' ', Console.WindowWidth - 5));
            Console.SetCursorPosition(4, top);

            // Write text
            Console.Write(text);

            // Set cursor to correct position
            Console.SetCursorPosition(4 + cursorPos, top);
        }

        /// <summary>
        /// Determine if the input needs continuation (incomplete statement)
        /// </summary>
        private bool NeedsContinuation(string fullInput, string lastLine)
        {
            var trimmedLast = lastLine.TrimEnd();
            var trimmedFull = fullInput.Trim();

            // Explicit line continuation with underscore
            if (trimmedLast.EndsWith("_"))
                return true;

            // Check for incomplete structures
            var lower = trimmedFull.ToLower();

            // Count block starters and enders
            var keywords = new[]
            {
                ("function ", "end function"),
                ("sub ", "end sub"),
                ("class ", "end class"),
                ("if ", "end if"),
                ("for ", "next"),
                ("while ", "end while"),
                ("do ", "loop"),
                ("select ", "end select"),
                ("try ", "end try"),
                ("with ", "end with"),
                ("namespace ", "end namespace"),
                ("property ", "end property")
            };

            foreach (var (start, end) in keywords)
            {
                var startCount = CountOccurrences(lower, start);
                var endCount = CountOccurrences(lower, end);
                if (startCount > endCount)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Count occurrences of a substring
        /// </summary>
        private int CountOccurrences(string text, string pattern)
        {
            int count = 0;
            int index = 0;
            while ((index = text.IndexOf(pattern, index, StringComparison.OrdinalIgnoreCase)) != -1)
            {
                count++;
                index += pattern.Length;
            }
            return count;
        }

        /// <summary>
        /// Handle special REPL commands (starting with :)
        /// </summary>
        private bool HandleSpecialCommand(string input)
        {
            var trimmed = input.Trim();

            if (!trimmed.StartsWith(":"))
                return false;

            var parts = trimmed.Substring(1).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return false;

            var command = parts[0].ToLower();

            switch (command)
            {
                case "help":
                case "h":
                case "?":
                    PrintHelp();
                    return true;

                case "quit":
                case "q":
                case "exit":
                    _running = false;
                    return true;

                case "clear":
                case "cls":
                    Console.Clear();
                    PrintWelcomeBanner();
                    return true;

                case "reset":
                    _interpreter.Clear();
                    _userFunctions.Clear();
                    _statementCounter = 0;
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Session state cleared.");
                    Console.ResetColor();
                    return true;

                case "vars":
                case "v":
                    PrintVariables();
                    return true;

                case "funcs":
                case "f":
                    PrintFunctions();
                    return true;

                case "history":
                case "hist":
                    PrintHistory();
                    return true;

                case "load":
                case "l":
                    if (parts.Length < 2)
                    {
                        PrintError("Usage: :load <filename>");
                    }
                    else
                    {
                        LoadFile(parts[1]);
                    }
                    return true;

                case "save":
                case "s":
                    if (parts.Length < 2)
                    {
                        PrintError("Usage: :save <filename>");
                    }
                    else
                    {
                        SaveHistory(parts[1]);
                    }
                    return true;

                case "type":
                case "t":
                    if (parts.Length < 2)
                    {
                        PrintError("Usage: :type <variable>");
                    }
                    else
                    {
                        PrintVariableType(parts[1]);
                    }
                    return true;

                default:
                    PrintError($"Unknown command: :{command}");
                    Console.WriteLine("Type :help for available commands");
                    return true;
            }
        }

        /// <summary>
        /// Print help information
        /// </summary>
        private void PrintHelp()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n╔════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                    REPL Commands                               ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════════╝");
            Console.ResetColor();
            Console.WriteLine();

            var commands = new[]
            {
                (":help, :h, :?", "Show this help message"),
                (":quit, :q, :exit", "Exit the REPL"),
                (":clear, :cls", "Clear the screen"),
                (":reset", "Clear all variables and functions"),
                (":vars, :v", "Show all defined variables"),
                (":funcs, :f", "Show all defined functions"),
                (":history, :hist", "Show command history"),
                (":load <file>", "Load and execute a BasicLang file"),
                (":save <file>", "Save history to a file"),
                (":type <var>", "Show the type of a variable")
            };

            foreach (var (cmd, desc) in commands)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"  {cmd,-20}");
                Console.ResetColor();
                Console.WriteLine($" - {desc}");
            }

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("Multi-line Input:");
            Console.ResetColor();
            Console.WriteLine("  - End a line with '_' to continue on next line");
            Console.WriteLine("  - Block structures (Function, If, For, etc.) auto-continue");
            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("Examples:");
            Console.ResetColor();
            Console.WriteLine("  >>> 5 + 3");
            Console.WriteLine("  >>> Dim x As Integer = 10");
            Console.WriteLine("  >>> PrintLine(\"Hello, World!\")");
            Console.WriteLine("  >>> Function Double(n As Integer) As Integer");
            Console.WriteLine("  ...     Return n * 2");
            Console.WriteLine("  ... End Function");
            Console.WriteLine();
        }

        /// <summary>
        /// Print all defined variables
        /// </summary>
        private void PrintVariables()
        {
            var vars = _interpreter.Variables;

            if (vars.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("No variables defined.");
                Console.ResetColor();
                return;
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\nDefined Variables:");
            Console.WriteLine("─────────────────────────────────────────────────────────");
            Console.ResetColor();

            foreach (var kvp in vars.OrderBy(kv => kv.Key))
            {
                var typeName = kvp.Value?.GetType().Name ?? "Nothing";
                var value = FormatValue(kvp.Value);

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"  {kvp.Key,-20}");
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.Write($" : {typeName,-15}");
                Console.ResetColor();
                Console.WriteLine($" = {value}");
            }
            Console.WriteLine();
        }

        /// <summary>
        /// Print all defined functions
        /// </summary>
        private void PrintFunctions()
        {
            if (_userFunctions.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("No functions defined.");
                Console.ResetColor();
                return;
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\nDefined Functions:");
            Console.WriteLine("─────────────────────────────────────────────────────────");
            Console.ResetColor();

            foreach (var kvp in _userFunctions.OrderBy(kv => kv.Key))
            {
                var func = kvp.Value;
                var returnType = func.ReturnType?.Name ?? "Void";
                var parameters = string.Join(", ", func.Parameters.Select(p =>
                    $"{p.Name} As {p.Type?.Name ?? "Object"}"));

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"  {func.Name}");
                Console.ResetColor();
                Console.Write($"({parameters})");
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine($" As {returnType}");
            }
            Console.WriteLine();
        }

        /// <summary>
        /// Print command history
        /// </summary>
        private void PrintHistory()
        {
            if (_history.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("No history.");
                Console.ResetColor();
                return;
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\nCommand History:");
            Console.WriteLine("─────────────────────────────────────────────────────────");
            Console.ResetColor();

            for (int i = 0; i < _history.Count; i++)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"  {i + 1,4}: ");
                Console.ResetColor();

                // Truncate long entries
                var entry = _history[i];
                if (entry.Length > 60)
                    entry = entry.Substring(0, 57) + "...";

                Console.WriteLine(entry.Replace("\n", " "));
            }
            Console.WriteLine();
        }

        /// <summary>
        /// Load and execute a BasicLang file
        /// </summary>
        private void LoadFile(string filename)
        {
            try
            {
                if (!File.Exists(filename))
                {
                    PrintError($"File not found: {filename}");
                    return;
                }

                var code = File.ReadAllText(filename);

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Loading {filename}...");
                Console.ResetColor();

                ExecuteCode(code);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Successfully loaded {filename}");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                PrintError($"Failed to load file: {ex.Message}");
            }
        }

        /// <summary>
        /// Save command history to a file
        /// </summary>
        private void SaveHistory(string filename)
        {
            try
            {
                File.WriteAllLines(filename, _history);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"History saved to {filename}");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                PrintError($"Failed to save history: {ex.Message}");
            }
        }

        /// <summary>
        /// Print the type of a variable
        /// </summary>
        private void PrintVariableType(string varName)
        {
            if (_interpreter.Variables.TryGetValue(varName, out var value))
            {
                var typeName = value?.GetType().FullName ?? "Nothing";
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"{varName}");
                Console.ResetColor();
                Console.WriteLine($" : {typeName}");
            }
            else
            {
                PrintError($"Variable '{varName}' is not defined");
            }
        }

        /// <summary>
        /// Execute BasicLang code
        /// </summary>
        private void ExecuteCode(string code)
        {
            try
            {
                // Wrap code if necessary
                var wrappedCode = WrapCodeIfNeeded(code);

                // Phase 1: Lexical Analysis
                var lexer = new Lexer(wrappedCode);
                var tokens = lexer.Tokenize();

                // Phase 2: Parsing
                var parser = new Parser(tokens);
                var ast = parser.Parse();

                // Phase 3: Semantic Analysis
                var semanticAnalyzer = new SemanticAnalyzer();
                bool success = semanticAnalyzer.Analyze(ast);

                if (!success)
                {
                    PrintSemanticErrors(semanticAnalyzer.Errors);
                    return;
                }

                // Phase 4: IR Generation
                var irBuilder = new IRBuilder(semanticAnalyzer);
                var irModule = irBuilder.Build(ast, $"repl_{_statementCounter++}");

                // Track user-defined functions
                foreach (var func in irModule.Functions)
                {
                    if (!func.Name.StartsWith("__repl") && !func.Name.Equals("Main", StringComparison.OrdinalIgnoreCase))
                    {
                        _userFunctions[func.Name] = func;
                    }
                }

                // Phase 5: Execute
                var result = _interpreter.Execute(irModule);

                // Print result if it's an expression
                if (result != null && IsExpression(code))
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"=> {FormatValue(result)}");
                    Console.ResetColor();
                }
            }
            catch (ParseException ex)
            {
                PrintError($"Parse error: {ex.Message}");
            }
            catch (InterpreterException ex)
            {
                PrintError($"Runtime error: {ex.Message}");
            }
            catch (Exception ex)
            {
                PrintError($"Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Wrap code in appropriate structure if needed
        /// </summary>
        private string WrapCodeIfNeeded(string code)
        {
            var trimmed = code.Trim();

            // Already a complete program structure
            if (trimmed.StartsWith("Function ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Sub ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Class ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Module ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Namespace ", StringComparison.OrdinalIgnoreCase))
            {
                return code;
            }

            // Variable declarations - wrap in Sub
            if (trimmed.StartsWith("Dim ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Const ", StringComparison.OrdinalIgnoreCase))
            {
                return $@"
Sub __repl_{_statementCounter}__()
    {code}
End Sub
";
            }

            // Expression - wrap in Function returning Object
            if (IsExpression(trimmed))
            {
                return $@"
Function __repl_{_statementCounter}__() As Object
    Return {code}
End Function
";
            }

            // Statement - wrap in Sub
            return $@"
Sub __repl_{_statementCounter}__()
    {code}
End Sub
";
        }

        /// <summary>
        /// Determine if code is an expression
        /// </summary>
        private bool IsExpression(string code)
        {
            var trimmed = code.Trim().ToLower();

            // Keywords that indicate statements (not expressions)
            var statementKeywords = new[]
            {
                "dim ", "const ", "if ", "for ", "while ", "do ", "select ", "try ",
                "printline(", "print(", "return ", "exit ", "function ", "sub ",
                "class ", "end ", "module ", "namespace "
            };

            foreach (var keyword in statementKeywords)
            {
                if (trimmed.StartsWith(keyword))
                    return false;
            }

            // Assignment is a statement unless it's a comparison
            if (trimmed.Contains(" = ") && !trimmed.Contains("==") && !trimmed.Contains("<=") && !trimmed.Contains(">="))
                return false;

            return true;
        }

        /// <summary>
        /// Print semantic analysis errors with helpful formatting
        /// </summary>
        private void PrintSemanticErrors(IList<SemanticError> errors)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n{errors.Count} error(s) found:");
            Console.ResetColor();

            foreach (var error in errors)
            {
                Console.ForegroundColor = error.Severity == ErrorSeverity.Error ? ConsoleColor.Red : ConsoleColor.Yellow;
                Console.Write($"  {error.Severity}: ");
                Console.ResetColor();
                Console.WriteLine(error.Message);

                if (error.Line > 0)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"    at line {error.Line}, column {error.Column}");
                    Console.ResetColor();
                }
            }
            Console.WriteLine();
        }

        /// <summary>
        /// Print an error message
        /// </summary>
        private void PrintError(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: {message}");
            Console.ResetColor();
        }

        /// <summary>
        /// Format a value for display
        /// </summary>
        private string FormatValue(object value)
        {
            if (value == null)
                return "Nothing";

            if (value is string s)
                return $"\"{s}\"";

            if (value is bool b)
                return b ? "True" : "False";

            if (value is char c)
                return $"'{c}'";

            if (value is Array arr)
            {
                var sb = new StringBuilder();
                sb.Append("{");
                int maxDisplay = Math.Min(arr.Length, 10);
                for (int i = 0; i < maxDisplay; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(FormatValue(arr.GetValue(i)));
                }
                if (arr.Length > maxDisplay)
                    sb.Append($", ... ({arr.Length - maxDisplay} more)");
                sb.Append("}");
                return sb.ToString();
            }

            return value.ToString();
        }
    }
}
