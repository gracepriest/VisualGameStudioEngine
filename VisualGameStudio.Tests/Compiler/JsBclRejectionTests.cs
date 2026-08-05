using System;
using NUnit.Framework;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.JavaScript;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Plan task 12 — BL7007, .NET BCL types. There is no BCL in a browser.
///
/// <para><b>This is the only rejection with a real design choice, and it is an ALLOW-LIST.</b>
/// A deny-list cannot work: the BCL is ~10,000 public types plus every NuGet package, and
/// nothing in-tree enumerates it. The shape is copied from
/// <c>CppCapabilityChecker.CheckType</c>, which runs the same gate in production — match
/// supported names FIRST, fall back to a Kind test LAST.</para>
///
/// <para><b>Why name-first is not a style choice.</b> <c>Task</c>, <c>Action</c>, <c>Func</c>
/// and <c>Exception</c> all reach the IR as <c>Kind=Class</c> — byte-for-byte
/// indistinguishable from <c>Stream</c>. They can only be rescued by name. Kind is also
/// hard-coded in IRBuilder fallbacks (delegate returns forced Primitive, interface properties
/// forced Class), so a Kind-only gate misses whole containers.</para>
///
/// <para><b>Which error to prefer.</b> A false negative here is the exact bug class this
/// backend exists to prevent: an unrecognised type emits <c>let s = null;</c> (the type
/// mapper's default), the build goes GREEN, and the user gets
/// "TypeError: Cannot read properties of null" in a browser at runtime. A false positive is a
/// build error with a name in it. Prefer the false positive.</para>
/// </summary>
[TestFixture]
public class JsBclRejectionTests
{
    private static string Reject(string source)
    {
        var module = JsTestSupport.BuildModule(source);
        var ex = Assert.Throws<ForeignFeatureException>(
            () => new JavaScriptCodeGenerator().Generate(module));
        return ex!.Message;
    }

    private static void Accept(string source)
    {
        var module = JsTestSupport.BuildModule(source);

        Exception caught = null;
        try { new JavaScriptCodeGenerator().Generate(module); }
        catch (Exception e) { caught = e; }

        Assert.That(caught, Is.Not.InstanceOf<ForeignFeatureException>(),
            "this is legal on the JavaScript backend and must not be rejected");
    }

    [TestCase("Sub Main()\nDim s As Stream\nEnd Sub", TestName = "Stream")]
    [TestCase("Sub Main()\nDim f As FileInfo\nEnd Sub", TestName = "FileInfo")]
    [TestCase("Sub Main()\nDim d As DirectoryInfo\nEnd Sub", TestName = "DirectoryInfo")]
    [TestCase("Sub Main()\nDim u As Uri\nEnd Sub", TestName = "Uri")]
    [TestCase("Sub Main()\nDim sb As StringBuilder\nEnd Sub", TestName = "StringBuilder")]
    [TestCase("Sub F(s As Stream)\nEnd Sub\nSub Main()\nEnd Sub", TestName = "Stream_AsParameter")]
    [TestCase("Sub Main()\nDim l As New List(Of Stream)()\nEnd Sub", TestName = "Stream_AsGenericArgument")]
    public void BclTypes_AreRejected(string source)
    {
        var message = Reject(source);
        Assert.That(message, Does.Contain("BL7007"));
    }

    /// <summary>
    /// The supported surface. Every one of these reaches the IR looking exactly like a BCL
    /// type, so each is a name the allow-list must carry explicitly.
    /// </summary>
    [TestCase("Sub Main()\nDim l As New List(Of Integer)()\nEnd Sub", TestName = "List")]
    [TestCase("Sub Main()\nDim d As New Dictionary(Of String, Integer)()\nEnd Sub", TestName = "Dictionary")]
    [TestCase("Sub Main()\nConsole.WriteLine(\"x\")\nEnd Sub", TestName = "Console")]
    public void SupportedLibraryTypes_AreAccepted(string source) => Accept(source);

    /// <summary>
    /// <c>Object</c> is IRBuilder's UNIVERSAL FALLBACK type — synthesized whenever a type is
    /// unknown, and <c>IRParameter.TypeName</c> literally defaults to it. A BL7007 that
    /// rejects Object fires on programs containing no BCL type whatsoever, so this control
    /// guards the single most damaging false positive available.
    /// </summary>
    [Test]
    public void Object_IsAccepted_BecauseItIsTheCompilersOwnFallbackType()
        => Accept("Sub Main()\nDim o As Object\nEnd Sub");

    /// <summary>User-declared types are the whole point and must never be rejected.</summary>
    [TestCase("Class Player\nPublic HP As Integer\nEnd Class\nSub Main()\nDim p As Player\nEnd Sub",
        TestName = "UserClass")]
    [TestCase("Interface IShape\nSub Draw()\nEnd Interface\nSub Main()\nEnd Sub",
        TestName = "UserInterface")]
    [TestCase("Enum Color\nRed\nGreen\nEnd Enum\nSub Main()\nDim c As Color\nEnd Sub",
        TestName = "UserEnum")]
    [TestCase("Class Box(Of T)\nPublic V As T\nEnd Class\nSub Main()\nEnd Sub",
        TestName = "GenericTypeParameter")]
    public void UserDeclaredTypes_AreAccepted(string source) => Accept(source);

    /// <summary>
    /// Exceptions are allowed by suffix rather than by a fixed list: under erasure every
    /// BasicLang exception becomes a JS Error, so a broad rule costs nothing, while a
    /// 12-name list would reject `Catch ex As FileNotFoundException` on a program that runs
    /// perfectly well.
    /// </summary>
    [TestCase("Sub Main()\nTry\nCatch ex As Exception\nEnd Try\nEnd Sub", TestName = "Exception")]
    [TestCase("Sub Main()\nTry\nCatch ex As InvalidOperationException\nEnd Try\nEnd Sub",
        TestName = "NamedException")]
    public void ExceptionTypes_AreAccepted(string source) => Accept(source);

    /// <summary>
    /// Long and Char must keep their OWN diagnostics. If BL7007 ran first they would draw a
    /// generic "no JavaScript equivalent" message instead of "use Integer" / "use String",
    /// which is the actionable part.
    /// </summary>
    [TestCase("Sub Main()\nDim n As Long\nEnd Sub", "BL7003", TestName = "Long_KeepsBL7003")]
    [TestCase("Sub Main()\nDim c As Char\nEnd Sub", "BL7004", TestName = "Char_KeepsBL7004")]
    public void BannedPrimitives_KeepTheirSpecificDiagnostic(string source, string expected)
    {
        Assert.That(Reject(source), Does.Contain(expected));
    }
}
