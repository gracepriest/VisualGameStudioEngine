using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Net;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// Spec §6.5 requires ONE ambient-namespace set shared by the C# backend
/// (<see cref="ImprovedCSharpCodeGenerator"/> — the type in the file <c>CSharpBackend.cs</c>;
/// there is no type literally named <c>CSharpBackend</c> anywhere in the codebase) and
/// <see cref="NetTypeResolver"/>. Without this, spec §6.3's "valid programs behave identically on
/// both backends" is false: a namespace the C# backend auto-imports but the resolver does not know
/// about means a program that compiles on the C# backend becomes a BL6016 natively.
/// </summary>
[TestFixture]
public class NetAmbientNamespaceTests
{
    [Test]
    public void CSharpBackendAndResolverShareOneAmbientSet()
    {
        Assert.That(ImprovedCSharpCodeGenerator.AmbientNamespacesForTest, Is.EquivalentTo(NetAmbientNamespaces.All),
            "The C# backend's auto-imported namespaces and the resolver's ambient set drifted — " +
            "update NetAmbientNamespaces, not a parallel list. If they differ, a program that " +
            "compiles on the C# backend becomes BL6016 natively and spec §6.3's equal-behavior " +
            "claim is false.");
    }

    [Test]
    public void AmbientSetContainsTheSeventeenKnownNamespaces()
    {
        // The plan that drove this task claimed 17 without having counted — verified by reading
        // CSharpBackend.cs:171-187 directly, which does add exactly 17 namespaces unconditionally.
        // If this count ever changes, update NetAmbientNamespaces.All (the source of truth), not
        // this assertion, and explain in NetAmbientNamespaces.cs's remarks why the set grew or
        // shrank.
        Assert.That(NetAmbientNamespaces.All, Has.Length.EqualTo(17),
            "NetAmbientNamespaces.All no longer has 17 entries. This is not necessarily wrong, but " +
            "update this assertion (and explain the change in NetAmbientNamespaces.cs) rather than " +
            "silently accepting a different count — the C# backend and NetTypeResolver both depend " +
            "on this exact set.");
        Assert.That(NetAmbientNamespaces.All, Does.Contain("System"));
        Assert.That(NetAmbientNamespaces.All, Does.Contain("System.Text.RegularExpressions"));
    }
}
