using System;
using System.Collections.Generic;
using BasicLang.Compiler;

namespace BasicLang.Demo
{
    /// <summary>
    /// Demonstrates the improved error messaging system
    /// </summary>
    public class ErrorMessagesDemo
    {
        public static void Main_Disabled(string[] args)
        {
            Console.WriteLine("BasicLang Compiler - Error Message Improvements Demo");
            Console.WriteLine("====================================================\n");

            DemoLexerErrors();
            Console.WriteLine("\n" + new string('=', 60) + "\n");

            DemoParserErrors();
            Console.WriteLine("\n" + new string('=', 60) + "\n");

            DemoSemanticErrors();
        }

        static void DemoLexerErrors()
        {
            Console.WriteLine("LEXER ERROR EXAMPLES");
            Console.WriteLine("--------------------\n");

            // Example 1: Unterminated String
            Console.WriteLine("1. Unterminated String Error:");
            var source1 = @"Function Greet(name As String) As String
    Return ""Hello,
End Function";

            var error1 = ErrorFormatter.FormatLexerError(
                ErrorCode.BL1001_UnterminatedString,
                "Unterminated string literal - missing closing quote",
                line: 2,
                column: 12,
                source1,
                "Add a closing quote (\") at the end of the string");

            Console.WriteLine(error1);
            Console.WriteLine();

            // Example 2: Invalid Character
            Console.WriteLine("2. Invalid Character Error:");
            var source2 = @"Dim x As Integer
x = 10 @ 5
End Sub";

            var error2 = ErrorFormatter.FormatLexerError(
                ErrorCode.BL1003_InvalidCharacter,
                "Invalid character '@' (Unicode: U+0040)",
                line: 2,
                column: 8,
                source2,
                "Remove or replace this character with a valid symbol");

            Console.WriteLine(error2);
            Console.WriteLine();

            // Example 3: Number Overflow
            Console.WriteLine("3. Number Overflow Error:");
            var source3 = @"Function Calculate() As Integer
    Dim x As Integer
    x = 2147483648
    Return x
End Function";

            var error3 = ErrorFormatter.FormatLexerError(
                ErrorCode.BL1004_InvalidNumberFormat,
                "Number '2147483648' is too large for Integer type",
                line: 3,
                column: 9,
                source3,
                $"Use 'L' suffix for Long type (e.g., 2147483648L) or reduce the number. Maximum Integer value is {int.MaxValue}");

            Console.WriteLine(error3);
        }

        static void DemoParserErrors()
        {
            Console.WriteLine("PARSER ERROR EXAMPLES");
            Console.WriteLine("---------------------\n");

            // Example 1: Missing 'Then'
            Console.WriteLine("1. Missing 'Then' After If:");
            var source1 = @"Function CheckAge(age As Integer) As Boolean
    If age >= 18
        Return True
    End If
    Return False
End Function";

            var token = new Token(TokenType.Newline, "\n", null, 2, 16);
            var error1 = ErrorFormatter.FormatParserError(
                ErrorCode.BL2002_MissingToken,
                "Missing 'Then' after if condition",
                token,
                source1,
                "'Then'",
                "Add 'Then' after the condition, before the statement body");

            Console.WriteLine(error1);
            Console.WriteLine();

            // Example 2: Block Mismatch
            Console.WriteLine("2. Block Mismatch Error:");
            var source2 = @"Function Calculate() As Integer
    Dim result As Integer
    result = 10 + 20
    Return result
End Sub";

            var error2 = ErrorFormatter.FormatBlockMismatchError(
                "Function",
                "End Sub",
                openLine: 1,
                closeLine: 5,
                closeColumn: 1,
                source2);

            Console.WriteLine(error2);
            Console.WriteLine();

            // Example 3: Common Typo
            Console.WriteLine("3. Common Typo Detection:");
            var source3 = @"Module MathOperations
    funciton Add(a As Integer, b As Integer) As Integer
        Return a + b
    End Function
End Module";

            var token3 = new Token(TokenType.Identifier, "funciton", null, 2, 5);
            var error3 = ErrorFormatter.FormatParserError(
                ErrorCode.BL2003_MissingKeyword,
                "Unrecognized keyword 'funciton'",
                token3,
                source3,
                "Function",
                $"Did you mean 'Function'?");

            Console.WriteLine(error3);
        }

        static void DemoSemanticErrors()
        {
            Console.WriteLine("SEMANTIC ERROR EXAMPLES");
            Console.WriteLine("-----------------------\n");

            // Example 1: Undefined Symbol with Suggestions
            Console.WriteLine("1. Undefined Symbol with Suggestions:");
            var source1 = @"Sub ProcessUser()
    Dim userName As String
    Dim userAge As Integer

    usrName = ""John""
End Sub";

            var error1 = ErrorFormatter.FormatUndefinedSymbolError(
                "usrName",
                line: 5,
                column: 5,
                source1,
                new[] { "userName", "userAge" });

            Console.WriteLine(error1);
            Console.WriteLine();

            // Example 2: Type Mismatch
            Console.WriteLine("2. Type Mismatch Error:");
            var source2 = @"Function GetAge() As Integer
    Dim age As Integer
    age = ""25""
    Return age
End Function";

            var error2 = ErrorFormatter.FormatTypeMismatchError(
                expectedType: "Integer",
                actualType: "String",
                line: 3,
                column: 5,
                source2,
                "Assignment");

            Console.WriteLine(error2);
            Console.WriteLine();

            // Example 3: Wrong Number of Arguments
            Console.WriteLine("3. Wrong Number of Arguments:");
            var source3 = @"Function Add(a As Integer, b As Integer) As Integer
    Return a + b
End Function

Sub Main()
    Dim result As Integer
    result = Add(5)
End Sub";

            var error3 = ErrorFormatter.FormatArgumentCountError(
                functionName: "Add",
                expected: 2,
                actual: 1,
                line: 7,
                column: 14,
                source3);

            Console.WriteLine(error3);
            Console.WriteLine();

            // Example 4: Type Mismatch with Conversion Suggestion
            Console.WriteLine("4. Type Mismatch with Conversion Suggestion:");
            var source4 = @"Sub ProcessData()
    Dim count As Integer
    Dim message As String
    message = ""Total: ""
    count = message + 10
End Sub";

            var error4 = ErrorFormatter.FormatSemanticError(
                ErrorCode.BL3001_TypeMismatch,
                "Arithmetic operator '+' requires numeric operands, but found 'String' and 'Integer'",
                line: 5,
                column: 13,
                source4,
                "Ensure both operands are numeric types (Integer, Long, Single, Double)");

            Console.WriteLine(error4);
        }

        static void DemoSimilarNameDetection()
        {
            Console.WriteLine("\nSIMILAR NAME DETECTION DEMO");
            Console.WriteLine("---------------------------\n");

            var testCases = new[]
            {
                ("usrName", new[] { "userName", "username", "UserName", "userAge" }),
                ("calcTotal", new[] { "calculateTotal", "calcTotals", "totalCalc" }),
                ("proces", new[] { "process", "processor", "processing" }),
                ("intiialize", new[] { "initialize", "initial", "init" })
            };

            foreach (var (typo, candidates) in testCases)
            {
                Console.WriteLine($"Typo: '{typo}'");
                Console.Write("Suggestions: ");

                // Simulate similar name detection
                var suggestions = new List<string>();
                foreach (var candidate in candidates)
                {
                    int distance = LevenshteinDistance(typo.ToLower(), candidate.ToLower());
                    if (distance <= 3)
                    {
                        suggestions.Add(candidate);
                    }
                }

                Console.WriteLine(string.Join(", ", suggestions.Take(3).Select(s => $"'{s}'")));
                Console.WriteLine();
            }
        }

        static int LevenshteinDistance(string s, string t)
        {
            if (string.IsNullOrEmpty(s)) return string.IsNullOrEmpty(t) ? 0 : t.Length;
            if (string.IsNullOrEmpty(t)) return s.Length;

            int[,] d = new int[s.Length + 1, t.Length + 1];

            for (int i = 0; i <= s.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= t.Length; j++) d[0, j] = j;

            for (int j = 1; j <= t.Length; j++)
            {
                for (int i = 1; i <= s.Length; i++)
                {
                    int cost = (s[i - 1] == t[j - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }

            return d[s.Length, t.Length];
        }
    }
}
