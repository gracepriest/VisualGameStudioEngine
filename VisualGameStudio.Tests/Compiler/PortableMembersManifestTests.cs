using System.Collections.Generic;
using System.IO;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 29 — the portable method manifest must be an exact description of the public
/// methods the library declares. Catalog-backed properties/events are not manifest rows;
/// the collection's Count is the one property row because it has no catalog home yet.
/// </summary>
[TestFixture]
public class PortableMembersManifestTests
{
    private static string? FindRepoPath(params string[] relativeParts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate) || Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    private sealed record LibraryMethod(string ClassName, string Name, int ParameterCount);

    private static IEnumerable<ASTNode> Members(IEnumerable<ASTNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            if (node is NamespaceNode ns)
                foreach (var child in Members(ns.Members)) yield return child;
            if (node is ModuleNode module)
                foreach (var child in Members(module.Members)) yield return child;
        }
    }

    private static (Dictionary<string, ClassNode> Classes, List<LibraryMethod> Methods) ReadLibrary()
    {
        var forms = FindRepoPath("BasicLang", "lib", "js", "forms");
        Assert.That(forms, Is.Not.Null, "BasicLang/lib/js/forms was not found from the test directory");

        var classes = new Dictionary<string, ClassNode>();
        var methods = new List<LibraryMethod>();
        foreach (var file in Directory.GetFiles(forms!, "*.bas").OrderBy(p => p, StringComparer.Ordinal))
        {
            var parser = new Parser(new Lexer(File.ReadAllText(file)).Tokenize());
            var program = parser.Parse();
            Assert.That(parser.Errors, Is.Empty, $"library file {Path.GetFileName(file)} must parse");

            foreach (var classNode in Members(program.Declarations).OfType<ClassNode>())
            {
                classes[classNode.Name] = classNode;
                foreach (var member in classNode.Members)
                {
                    if (member is SubroutineNode sub && sub.Access == AccessModifier.Public &&
                        !sub.Name.StartsWith("Vgs", StringComparison.Ordinal))
                    {
                        methods.Add(new LibraryMethod(classNode.Name, sub.Name, sub.Parameters.Count));
                    }
                    if (member is FunctionNode fn && fn.Access == AccessModifier.Public &&
                        !fn.Name.StartsWith("Vgs", StringComparison.Ordinal))
                    {
                        methods.Add(new LibraryMethod(classNode.Name, fn.Name, fn.Parameters.Count));
                    }
                }
            }
        }

        return (classes, methods);
    }

    [Test]
    public void EveryManifestRowHasItsDeclarationAndSampleCount()
    {
        var (classes, _) = ReadLibrary();

        foreach (var row in PortableMembers.Manifest)
        {
            Assert.That(classes.ContainsKey(row.DeclaringType), Is.True,
                $"{row.DeclaringType} is named by the manifest but not declared by the library");
            var declared = classes[row.DeclaringType].Members.Any(member =>
                row.Kind == PortableMemberKind.Method
                    ? member is SubroutineNode sub && sub.Name == row.Name && sub.Access == AccessModifier.Public
                      || member is FunctionNode fn && fn.Name == row.Name && fn.Access == AccessModifier.Public
                    : member is PropertyNode property && property.Name == row.Name && property.Access == AccessModifier.Public);
            Assert.That(declared, Is.True, $"{row.DeclaringType}.{row.Name} is missing from the library");
            Assert.That(row.WinFormsSignature, Is.Not.Empty, $"{row.DeclaringType}.{row.Name} needs its WinForms signature");

            var parameters = classes[row.DeclaringType].Members
                .Select(member => member switch
                {
                    SubroutineNode sub when sub.Name == row.Name => sub.Parameters.Count,
                    FunctionNode fn when fn.Name == row.Name => fn.Parameters.Count,
                    _ => -1
                })
                .FirstOrDefault(count => count >= 0);
            if (row.Kind == PortableMemberKind.Method)
            {
                Assert.That(row.SampleArguments.Count, Is.EqualTo(parameters),
                    $"{row.DeclaringType}.{row.Name} needs one sample argument per parameter");
            }
            else
            {
                Assert.That(row.SampleArguments, Is.Empty, $"{row.DeclaringType}.{row.Name} is a property row");
            }
        }
    }

    [Test]
    public void EveryPublicMethodHasAManifestRow()
    {
        var (_, methods) = ReadLibrary();
        var rows = PortableMembers.Manifest
            .Where(row => row.Kind == PortableMemberKind.Method)
            .Select(row => (row.DeclaringType, row.Name))
            .ToHashSet();

        foreach (var method in methods)
        {
            Assert.That(rows.Contains((method.ClassName, method.Name)), Is.True,
                $"{method.ClassName}.{method.Name} is public but has no PortableMembers row");
        }

        Assert.That(rows.Count, Is.EqualTo(methods.Count),
            "the manifest has a method row the library does not declare");
    }

    [Test]
    public void ManifestRowsAreUnique()
    {
        var keys = PortableMembers.Manifest
            .Select(row => (row.DeclaringType, row.Name, row.Kind))
            .ToList();
        Assert.That(keys, Is.Unique);
    }
}
