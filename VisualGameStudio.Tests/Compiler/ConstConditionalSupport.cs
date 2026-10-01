using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// The front end run once over a source text, with everything a #123 test reads out of it: the parser's
/// errors, the analyzer's errors and warnings, every AST node of a kind, and the type the analyzer gave a node.
/// Nothing here asserts; a fixture decides what a verdict means.
/// </summary>
internal sealed class FrontEndRun
{
    internal ProgramNode Ast { get; }
    internal Parser Parser { get; }
    internal SemanticAnalyzer Analyzer { get; }
    internal bool Analyzed { get; }

    internal FrontEndRun(string source)
    {
        Parser = new Parser(new Lexer(source).Tokenize());
        Ast = Parser.Parse();
        Analyzer = new SemanticAnalyzer();
        Analyzed = Analyzer.Analyze(Ast);
    }

    internal List<string> ParseErrors => Parser.Errors.Select(e => e.Message).ToList();

    internal List<string> Errors => Analyzer.Errors
        .Where(e => e.Severity == ErrorSeverity.Error).Select(e => e.Message).ToList();

    internal List<string> Warnings => Analyzer.Errors
        .Where(e => e.Severity == ErrorSeverity.Warning).Select(e => e.Message).ToList();

    /// <summary>Every parse and analysis ERROR, first line of each.</summary>
    internal List<string> AllErrors => ParseErrors.Concat(Errors).Select(m => m.Split('\n')[0]).ToList();

    /// <summary>Every node of type <typeparamref name="T"/> anywhere under the program, in walk order.</summary>
    internal List<T> Nodes<T>() where T : ASTNode
    {
        var found = new List<T>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        Walk(Ast, found, seen);
        return found;
    }

    private static void Walk<T>(object node, List<T> found, HashSet<object> seen) where T : ASTNode
    {
        if (node == null || !seen.Add(node)) return;
        if (node is T match) found.Add(match);
        if (node is not ASTNode) return;

        foreach (var property in node.GetType().GetProperties())
        {
            if (property.GetIndexParameters().Length > 0) continue;
            var value = property.GetValue(node);
            if (value is ASTNode) Walk(value, found, seen);
            else if (value is IEnumerable sequence && value is not string)
                foreach (var item in sequence)
                    if (item is ASTNode) Walk(item, found, seen);
        }
    }

    /// <summary>The analyzer's type for <paramref name="node"/>, as the type prints (<c>Integer</c>, <c>Double</c>, <c>Animal</c>).</summary>
    internal string TypeOf(ASTNode node) => Analyzer.GetNodeType(node)?.ToString();

    /// <summary>Every <c>Const</c> declaration's name and the type the analyzer gave it, at every level.</summary>
    internal Dictionary<string, string> ConstTypes()
        => Nodes<ConstantDeclarationNode>().ToDictionary(c => c.Name, c => TypeOf(c), StringComparer.Ordinal);
}

/// <summary>Shared entry points for the #123 fast fixtures.</summary>
internal static class T123Front
{
    internal static FrontEndRun Run(string source) => new(source);

    /// <summary>The front end's errors (parse and semantic) for <paramref name="source"/>, first line of each.</summary>
    internal static List<string> Errors(string source) => new FrontEndRun(source).AllErrors;

    /// <summary>
    /// What the IR builder says about a program the front end ACCEPTS: the message of the exception it throws
    /// (module-scope initializers and Select Case guards are refused there), or null when it builds.
    /// </summary>
    internal static string IrRefusal(string source)
    {
        try
        {
            JsTestSupport.BuildModule(source);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>The IR builder's message when a program is compiled through <see cref="BasicCompiler"/>, by route.</summary>
    internal static List<string> CompilerErrors(string source, bool projectRoute, bool aggressive = false)
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bl-t123-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            var path = System.IO.Path.Combine(dir, "Main.bas");
            System.IO.File.WriteAllText(path, source);
            var compiler = new BasicCompiler(new CompilerOptions { OptimizeAggressive = aggressive });
            var result = projectRoute
                ? compiler.CompileProjectFiles(new List<string> { path })
                : compiler.CompileFile(path);
            return result.AllErrors.Select(e => e.Message).ToList();
        }
        finally
        {
            try { System.IO.Directory.Delete(dir, recursive: true); } catch { /* temp */ }
        }
    }
}
