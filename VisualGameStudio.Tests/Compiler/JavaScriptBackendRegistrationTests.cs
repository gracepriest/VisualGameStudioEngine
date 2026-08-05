using NUnit.Framework;
using BasicLang.Compiler.CodeGen;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Plan task 1: the JavaScript backend is reachable through the same registry every
/// other backend uses, under both spellings a &lt;TargetBackend&gt; value may take.
/// </summary>
[TestFixture]
public class JavaScriptBackendRegistrationTests
{
    [Test]
    public void Registry_ResolvesJavaScriptByBothNames()
    {
        Assert.That(BackendRegistry.GetTarget("JavaScript"), Is.EqualTo(TargetPlatform.JavaScript));
        Assert.That(BackendRegistry.GetTarget("JS"), Is.EqualTo(TargetPlatform.JavaScript));
    }

    [Test]
    public void Registry_CreatesJavaScriptGenerator()
    {
        var gen = BackendRegistry.Create(TargetPlatform.JavaScript);

        Assert.That(gen.Target, Is.EqualTo(TargetPlatform.JavaScript));
        Assert.That(gen.BackendName, Is.EqualTo("JavaScript"));
        Assert.That(gen.TypeMapper, Is.Not.Null);
    }

    /// <summary>
    /// TargetPlatform values are persisted in .blproj files, so JavaScript must be
    /// APPENDED. If someone inserts a member above it the existing four shift ordinal
    /// and every stored project silently retargets.
    /// </summary>
    [Test]
    public void JavaScript_IsAppendedAfterTheExistingFour()
    {
        Assert.That((int)TargetPlatform.CSharp, Is.EqualTo(0));
        Assert.That((int)TargetPlatform.Cpp, Is.EqualTo(1));
        Assert.That((int)TargetPlatform.LLVM, Is.EqualTo(2));
        Assert.That((int)TargetPlatform.MSIL, Is.EqualTo(3));
        Assert.That((int)TargetPlatform.JavaScript, Is.EqualTo(4));
    }
}
