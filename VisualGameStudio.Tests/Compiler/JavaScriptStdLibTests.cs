using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.StdLib;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Plan task 24 — the JavaScript standard library, one function per category.
///
/// <para>Before this task a BARE stdlib name fell through <c>CallTarget</c>'s
/// user-function passthrough and emitted a call to something that exists nowhere, so
/// <c>Sqr(16)</c> reached Node as <c>Sqr is not defined</c>. Dotted names at least threw
/// at compile time; bare ones did not.</para>
/// </summary>
[TestFixture]
[Category("Integration")]   // spawns node
public class JavaScriptStdLibTests
{
    private static string InMain(string body) =>
        JavaScriptExecutionTests.RunJs($"Sub Main()\n{body}\nEnd Sub");

    // ---------------------------------------------------------------- math

    [Test]
    public void Math_SqrAndAbs()
        => Assert.That(InMain("Console.WriteLine(Sqr(16))\nConsole.WriteLine(Abs(-3))"),
            Is.EqualTo("4\n3"));

    [Test]
    public void Math_TrigAndLogs()
        => Assert.That(InMain(
            "Console.WriteLine(Cos(0))\nConsole.WriteLine(Exp(0))\nConsole.WriteLine(Log(1))"),
            Is.EqualTo("1\n1\n0"));

    [Test]
    public void Math_MinMaxPow()
        => Assert.That(InMain(
            "Console.WriteLine(Min(3, 7))\nConsole.WriteLine(Max(3, 7))\nConsole.WriteLine(Pow(2, 10))"),
            Is.EqualTo("3\n7\n1024"));

    /// <summary>
    /// VB's <c>Int</c> FLOORS (Int(-3.5) = -4) while <c>Fix</c> truncates toward zero
    /// (Fix(-3.5) = -3). Mapping both to the same JS function is the silent bug here.
    /// </summary>
    [Test]
    public void Math_IntFloors_FixTruncates()
        => Assert.That(InMain("Console.WriteLine(Int(-3.5))\nConsole.WriteLine(Fix(-3.5))"),
            Is.EqualTo("-4\n-3"));

    /// <summary>
    /// .NET's <c>Math.Round</c> is BANKER'S rounding — 2.5 goes to 2, not 3 — where
    /// JavaScript's <c>Math.round</c> rounds half up. A bare rename is wrong on every .5.
    /// </summary>
    [Test]
    public void Math_Round_IsBankers_NotHalfUp()
        => Assert.That(InMain(
            "Console.WriteLine(Round(2.5))\nConsole.WriteLine(Round(3.5))\nConsole.WriteLine(Round(-2.5))"),
            Is.EqualTo("2\n4\n-2"));

    // ---------------------------------------------------------------- random

    /// <summary>
    /// Nondeterministic, so the RANGE is what gets asserted. Split over two prints because
    /// `And` is deliberately unmapped on this backend (VB's And is non-short-circuit and
    /// bitwise over integers, so `&amp;&amp;` would be a guess).
    /// </summary>
    [Test]
    public void Random_RndIsInTheUnitInterval()
        => Assert.That(InMain(
            "Dim r As Double = Rnd()\nConsole.WriteLine(r >= 0)\nConsole.WriteLine(r < 1)"),
            Is.EqualTo("true\ntrue"));

    // ---------------------------------------------------------------- datetime

    [Test]
    public void DateTime_NowYearIsPlausible()
        => Assert.That(InMain("Dim n = Now()\nConsole.WriteLine(Year(n) > 2000)"),
            Is.EqualTo("true"));

    /// <summary>
    /// JavaScript months are 0-BASED and VB's are 1-based. A direct <c>getMonth()</c> is
    /// off by one for every date in the year and looks entirely reasonable in output.
    /// </summary>
    [Test]
    public void DateTime_MonthIsOneBased()
        => Assert.That(InMain(
            "Dim n = Now()\nDim m = Month(n)\nConsole.WriteLine(m >= 1)\nConsole.WriteLine(m <= 12)"),
            Is.EqualTo("true\ntrue"));

    // ---------------------------------------------------------------- regex

    [Test]
    public void Regex_IsMatch()
        => Assert.That(InMain(
            "Console.WriteLine(IsMatch(\"abc123\", \"[0-9]+\"))\n" +
            "Console.WriteLine(IsMatch(\"abc\", \"[0-9]+\"))"),
            Is.EqualTo("true\nfalse"));

    [Test]
    public void Regex_ReplaceAndMatch()
        => Assert.That(InMain(
            "Console.WriteLine(RegexReplace(\"a1b2\", \"[0-9]\", \"#\"))\n" +
            "Console.WriteLine(RegexMatch(\"abc123\", \"[0-9]+\"))"),
            Is.EqualTo("a#b#\n123"));

    /// <summary>
    /// <c>RegexReplace</c> must replace EVERY occurrence, as .NET does. A JS RegExp without
    /// the <c>g</c> flag replaces only the first — right on single-match input, wrong on the
    /// input above, and it is the kind of thing a one-case test never catches.
    /// </summary>
    [Test]
    public void Regex_ReplaceIsGlobal()
        => Assert.That(InMain("Console.WriteLine(RegexReplace(\"aaa\", \"a\", \"b\"))"),
            Is.EqualTo("bbb"));

    // ---------------------------------------------------------------- roster

    /// <summary>
    /// A user function must WIN over a stdlib name of the same spelling, exactly as it
    /// already does for the string builtins.
    /// </summary>
    [Test]
    public void UserFunction_ShadowsAStdLibName()
        => Assert.That(JavaScriptExecutionTests.RunJs(
            "Function Abs(x As Integer) As Integer\nReturn 99\nEnd Function\n" +
            "Sub Main()\nConsole.WriteLine(Abs(-3))\nEnd Sub"),
            Is.EqualTo("99"));
}

/// <summary>
/// The provider's own contract — codegen only, so these run in the fast subset.
/// </summary>
[TestFixture]
public class JavaScriptStdLibProviderTests
{
    private readonly JavaScriptStdLibProvider _provider = new JavaScriptStdLibProvider();

    [TestCase("Abs")]
    [TestCase("Sqr")]
    [TestCase("Rnd")]
    [TestCase("Now")]
    [TestCase("IsMatch")]
    public void CanHandle_SupportedFunctions(string name)
        => Assert.That(_provider.CanHandle(name), Is.True);

    /// <summary>
    /// THE refusal the plan calls out by name. <c>fetch</c> is asynchronous and cannot back
    /// a synchronous <c>HttpGet(url) As String</c> signature — an emission would hand back a
    /// Promise where a String is expected, which stringifies to "[object Promise]" rather
    /// than failing. Refusing here surfaces it as an unsupported-function error instead.
    /// </summary>
    [TestCase("HttpGet")]
    [TestCase("HttpPost")]
    [TestCase("HttpDownload")]
    [TestCase("TcpConnect")]
    [TestCase("UdpCreate")]
    public void CanHandle_Networking_IsFalse(string name)
        => Assert.That(_provider.CanHandle(name), Is.False,
            "fetch is async and cannot back a synchronous signature");

    /// <summary>A browser has no filesystem, no processes and no environment.</summary>
    [TestCase("FileRead")]
    [TestCase("DirCreate")]
    [TestCase("Shell")]
    [TestCase("GetEnv")]
    public void CanHandle_HostOnlyCategories_AreFalse(string name)
        => Assert.That(_provider.CanHandle(name), Is.False);

    [Test]
    public void Registry_ResolvesTheJavaScriptProvider()
        => Assert.That(StdLibRegistry.GetProviderForFunction(TargetPlatform.JavaScript, "Abs"),
            Is.InstanceOf<JavaScriptStdLibProvider>());

    /// <summary>
    /// An unsupported name must surface as a clean compile-time diagnostic, not as a call to
    /// a function that exists nowhere in the emitted JS.
    /// </summary>
    [Test]
    public void UnsupportedFunction_IsACompileError()
        => Assert.That(Assert.Catch(() => JsTestSupport.Compile(
                "Sub Main()\nDim s = HttpGet(\"http://example.com\")\nEnd Sub")),
            Is.InstanceOf<System.NotSupportedException>());
}
