using System;
using System.Collections.Generic;
using System.Text;
using BasicLang.Compiler.Interpreter;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;

namespace BasicLang.Compiler.Repl
{
    /// <summary>
    /// Interactive REPL (Read-Eval-Print-Loop) for BasicLang
    /// </summary>
    public class BasicLangRepl
    {
        private readonly IRInterpreter _interpreter;
        private readonly List<string> _history;
        private int _historyIndex;
        private bool _running;

        public BasicLangRepl()
        {
            _interpreter = new IRInterpreter();
            _history = new List<string>();
            _historyIndex = 0;
            _running = false;
        }

        /// <summary>
        /// Start the REPL
        /// </summary>
        public void Run()
        {
            _running = true;
            PrintBanner();

            while (_running)
            {
                try
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write("> ");
                    Console.ResetColor();

                    var input = ReadLine();
                    if (string.IsNullOrWhiteSpace(input))
                        continue;

                    // Add to history
                    _history.Add(input);
                    _historyIndex = _history.Count;

                    // Check for special commands
                    if (HandleCommand(input))
                        continue;

                    // Evaluate the input
                    Evaluate(input);
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Error: {ex.Message}");
                    Console.ResetColor();
                }
            }
        }

        /// <summary>
        /// Print the welcome banner
        /// </summary>
        private void PrintBanner()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║           BasicLang Interactive REPL v1.0                    ║");
            Console.WriteLine("║  Type 'help' for commands, 'exit' to quit                    ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.ResetColor();
            Console.WriteLine();
        }

        /// <summary>
        /// Handle special REPL commands
        /// </summary>
        private bool HandleCommand(string input)
        {
            var cmd = input.Trim().ToLower();

            switch (cmd)
            {
                case "exit":
                case "quit":
                case ":q":
                    _running = false;
                    Console.WriteLine("Goodbye!");
                    return true;

                case "help":
                case ":h":
                case "?":
                    PrintHelp();
                    return true;

                case "clear":
                case "cls":
                case ":c":
                    Console.Clear();
                    PrintBanner();
                    return true;

                case "reset":
                case ":r":
                    _interpreter.Clear();
                    Console.WriteLine("State cleared.");
                    return true;

                case "vars":
                case ":v":
                    PrintVariables();
                    return true;

                case "history":
                case ":hist":
                    PrintHistory();
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Print help information
        /// </summary>
        private void PrintHelp()
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\nBasicLang REPL Commands:");
            Console.WriteLine("─────────────────────────────────────────");
            Console.ResetColor();
            Console.WriteLine("  help, ?      - Show this help");
            Console.WriteLine("  exit, quit   - Exit the REPL");
            Console.WriteLine("  clear, cls   - Clear the screen");
            Console.WriteLine("  reset        - Clear all variables and state");
            Console.WriteLine("  vars         - Show all defined variables");
            Console.WriteLine("  history      - Show command history");
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Examples:");
            Console.WriteLine("─────────────────────────────────────────");
            Console.ResetColor();
            Console.WriteLine("  > 5 + 3                    ' Expression evaluation");
            Console.WriteLine("  > Dim x As Integer = 10    ' Variable declaration");
            Console.WriteLine("  > x * 2                    ' Use variable");
            Console.WriteLine("  > PrintLine(\"Hello\")       ' Print output");
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Multi-line input:");
            Console.WriteLine("─────────────────────────────────────────");
            Console.ResetColor();
            Console.WriteLine("  End line with '_' to continue on next line");
            Console.WriteLine("  Or use : to separate statements");
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
                Console.WriteLine("No variables defined.");
                return;
            }

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\nDefined Variables:");
            Console.WriteLine("─────────────────────────────────────────");
            Console.ResetColor();

            foreach (var kvp in vars)
            {
                var typeName = kvp.Value?.GetType().Name ?? "Nothing";
                var value = FormatValue(kvp.Value);
                Console.WriteLine($"  {kvp.Key} : {typeName} = {value}");
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
                Console.WriteLine("No history.");
                return;
            }

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\nCommand History:");
            Console.WriteLine("─────────────────────────────────────────");
            Console.ResetColor();

            for (int i = 0; i < _history.Count; i++)
            {
                Console.WriteLine($"  {i + 1}: {_history[i]}");
            }
            Console.WriteLine();
        }

        /// <summary>
        /// Read a line with support for multi-line input
        /// </summary>
        private string ReadLine()
        {
            var sb = new StringBuilder();
            bool continuing = false;

            do
            {
                if (continuing)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGreen;
                    Console.Write("  ");
                    Console.ResetColor();
                }

                var line = Console.ReadLine();
                if (line == null)
                {
                    _running = false;
                    return "";
                }

                // Check for line continuation
                if (line.TrimEnd().EndsWith("_"))
                {
                    sb.Append(line.TrimEnd().TrimEnd('_'));
                    sb.Append(" ");
                    continuing = true;
                }
                else
                {
                    sb.Append(line);
                    continuing = false;
                }
            } while (continuing);

            return sb.ToString();
        }

        /// <summary>
        /// Evaluate a BasicLang expression or statement
        /// </summary>
        private void Evaluate(string input)
        {
            // Wrap bare expressions in a function for evaluation
            string code = WrapInput(input);

            try
            {
                // Phase 1: Lexical Analysis
                var lexer = new Lexer(code);
                var tokens = lexer.Tokenize();

                // Phase 2: Parsing
                var parser = new Parser(tokens);
                var ast = parser.Parse();

                // Phase 3: Semantic Analysis
                var semanticAnalyzer = new SemanticAnalyzer();
                bool success = semanticAnalyzer.Analyze(ast);

                if (!success)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    foreach (var error in semanticAnalyzer.Errors)
                    {
                        Console.WriteLine($"Error: {error}");
                    }
                    Console.ResetColor();
                    return;
                }

                // Phase 4: IR Generation
                var irBuilder = new IRBuilder(semanticAnalyzer);
                var irModule = irBuilder.Build(ast, "repl");

                // Phase 5: Interpretation
                var result = _interpreter.Execute(irModule);

                // Print result if it's an expression
                if (result != null && IsExpression(input))
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"= {FormatValue(result)}");
                    Console.ResetColor();
                }
            }
            catch (ParseException ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Parse error: {ex.Message}");
                Console.ResetColor();
            }
            catch (InterpreterException ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Runtime error: {ex.Message}");
                Console.ResetColor();
            }
        }

        /// <summary>
        /// Wrap input in appropriate code structure
        /// </summary>
        private string WrapInput(string input)
        {
            var trimmed = input.Trim();

            // If it's already a declaration, return as-is
            if (trimmed.StartsWith("Function ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Sub ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Class ", StringComparison.OrdinalIgnoreCase))
            {
                return input;
            }

            // If it's a Dim statement, wrap in a Sub
            if (trimmed.StartsWith("Dim ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Const ", StringComparison.OrdinalIgnoreCase))
            {
                return $@"
Sub __repl__()
    {input}
End Sub
";
            }

            // If it looks like an expression, wrap as function returning the value
            if (IsExpression(trimmed))
            {
                return $@"
Function __repl__() As Object
    Return {input}
End Function
";
            }

            // Otherwise wrap as statement
            return $@"
Sub __repl__()
    {input}
End Sub
";
        }

        /// <summary>
        /// Check if input looks like an expression
        /// </summary>
        private bool IsExpression(string input)
        {
            var trimmed = input.Trim().ToLower();

            // Statements that are NOT expressions
            if (trimmed.StartsWith("dim ") ||
                trimmed.StartsWith("const ") ||
                trimmed.StartsWith("if ") ||
                trimmed.StartsWith("for ") ||
                trimmed.StartsWith("while ") ||
                trimmed.StartsWith("do ") ||
                trimmed.StartsWith("select ") ||
                trimmed.StartsWith("try ") ||
                trimmed.StartsWith("printline") ||
                trimmed.StartsWith("print ") ||
                trimmed.Contains(" = ") && !trimmed.Contains("=="))
            {
                return false;
            }

            return true;
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

            if (value is Array arr)
            {
                var sb = new StringBuilder();
                sb.Append("{");
                for (int i = 0; i < Math.Min(arr.Length, 10); i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(FormatValue(arr.GetValue(i)));
                }
                if (arr.Length > 10)
                    sb.Append(", ...");
                sb.Append("}");
                return sb.ToString();
            }

            return value.ToString();
        }
    }
}
