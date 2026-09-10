using NUnit.Framework;
using BasicLang.Compiler.CodeGen;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A typed <c>Catch</c> selects by TYPE on the JavaScript backend — the mirror of
/// <see cref="CppCatchDiscriminationTests"/>, and the same two miscompiles.
///
/// <para><b>MEASURED before the fix, through the CLI:</b></para>
/// <code>
///   Throw New ArgumentException("boom")
///   Catch e As FormatException  → "INNER-WRONG"   the clause SWALLOWED a type it excludes
///   two Catch clauses           → refused           NotYet("multiple Catch clauses")
///   Class MyError : Inherits Exception
///       MyBase.New(msg)         → parse error       "Expected 'New' after MyBase. but found Identifier"
/// </code>
///
/// <para>⛔ <b>There was no exception type at all.</b> <c>Throw New ArgumentException("boom")</c>
/// emitted <c>new ArgumentException("boom")</c> VERBATIM, and nothing defines that in JavaScript.
/// The line threw a <c>ReferenceError</c> instead — which the single untyped <c>catch (e)</c>
/// then caught, so every existing "throw is caught" test passed for the wrong reason: the
/// exception that was caught was never the one the program threw. A test that prints
/// <c>e.Message</c> is what separates the two, which is why several below do.</para>
///
/// <para><b>The lowering.</b> The backend now emits the .NET exception hierarchy it needs
/// (the transitive closure of the names the program mentions) as real JS classes rooted at
/// <c>class Exception extends Error</c>, so <c>instanceof</c> is a faithful type test, a user
/// <c>Class MyError : Inherits Exception</c> slots into it, and a <c>Catch</c> ladder is
/// <c>catch (_ex) { if (_ex instanceof A) { … } else if (_ex instanceof B) { … } else throw _ex; }</c>.
/// <c>Catch e As Exception</c> is the catch-all — it also takes a native JS error, wrapped so
/// <c>e.Message</c> works — exactly as <c>catch (Exception)</c> catches everything in .NET.</para>
///
/// <para>⛔ <b>ORDER-INDEPENDENCE AND OVER-CATCH ARE THE TESTS THAT MATTER.</b> A positive
/// subclass match passes even against "first clause wins". The ones that can fail are a thrown
/// type that must SKIP a clause, and both orderings agreeing.</para>
/// </summary>
[TestFixture]
[Category("Integration")]   // spawns node
public class JavaScriptCatchDiscriminationTests
{
    private static string Run(string source) => JavaScriptExecutionTests.RunJs(source);

    private const string OrderedF =
        "Function F() As String\n" +
        "Try\nThrow New ArgumentException(\"boom\")\n" +
        "Catch e As InvalidOperationException\nReturn \"IOE\"\n" +
        "Catch b As Exception\nReturn \"EX\"\n" +
        "End Try\nEnd Function\n";

    // ---------------------------------------------------------------- the core property

    /// <summary>FormatException / InvalidOperationException / ArgumentException are SIBLINGS; one must not catch another.</summary>
    [Test]
    public void AClauseWhoseTypeDoesNotMatch_IsSkipped()
        => Assert.That(Run(OrderedF + "Sub Main()\nConsole.WriteLine(F())\nEnd Sub"),
            Is.EqualTo("EX"),
            "IOE means the first clause caught an exception its declared type excludes.");

    [Test]
    public void TheAnswerDoesNotDependOnClauseOrder()
        => Assert.That(Run(
            OrderedF +
            "Function Swapped() As String\n" +
            "Try\nThrow New ArgumentException(\"boom\")\n" +
            "Catch b As Exception\nReturn \"EX\"\n" +
            "Catch e As InvalidOperationException\nReturn \"IOE\"\n" +
            "End Try\nEnd Function\n" +
            "Sub Main()\nConsole.WriteLine(F())\nConsole.WriteLine(Swapped())\nEnd Sub"),
            Is.EqualTo("EX\nEX"),
            "'IOE\\nEX' is the signature of position deciding instead of type.");

    /// <summary>
    /// OVER-CATCH, on a SINGLE clause — the shape that shipped. A non-matching inner clause must
    /// not steal the exception from the correct outer handler.
    /// </summary>
    [Test]
    public void ANonMatchingSingleClause_DoesNotStealFromAnOuterHandler()
        => Assert.That(Run(
            "Function F() As String\n" +
            "Try\nTry\nThrow New ArgumentException(\"boom\")\n" +
            "Catch e As FormatException\nReturn \"INNER-WRONG\"\nEnd Try\n" +
            "Catch b As Exception\nReturn \"OUTER\"\nEnd Try\n" +
            "Return \"fell-through\"\nEnd Function\n" +
            "Sub Main()\nConsole.WriteLine(F())\nEnd Sub"),
            Is.EqualTo("OUTER"),
            "INNER-WRONG means the inner clause swallowed an exception its type excludes.");

    [Test]
    public void ABaseClassClause_StillCatchesADerivedThrow()
        => Assert.That(Run(
            "Function F() As String\n" +
            "Try\nThrow New ArgumentNullException(\"boom\")\n" +
            "Catch e As ArgumentException\nReturn \"SUBCLASS\"\n" +
            "Catch b As Exception\nReturn \"EX\"\n" +
            "End Try\nEnd Function\n" +
            "Sub Main()\nConsole.WriteLine(F())\nEnd Sub"),
            Is.EqualTo("SUBCLASS"));

    // ---------------------------------------------------------------- the exception is REAL

    /// <summary>
    /// THE test that fails on the old backend even with one clause: the message can only
    /// survive if the object caught is the one that was thrown, not a ReferenceError.
    /// </summary>
    [Test]
    public void Message_SurvivesTheThrow()
        => Assert.That(Run(
            "Sub Main()\nTry\nThrow New InvalidOperationException(\"boom\")\n" +
            "Catch e As Exception\nConsole.WriteLine(e.Message)\nEnd Try\nEnd Sub"),
            Is.EqualTo("boom"));

    [Test]
    public void Message_IsReadableThroughATypedClause()
        => Assert.That(Run(
            "Sub Main()\nTry\nThrow New FormatException(\"bad format\")\n" +
            "Catch e As FormatException\nConsole.WriteLine(\"format: \" & e.Message)\nEnd Try\nEnd Sub"),
            Is.EqualTo("format: bad format"));

    [Test]
    public void CatchWithoutAVariable_StillCatches()
        => Assert.That(Run(
            "Sub Main()\nTry\nThrow New Exception(\"x\")\nCatch\nConsole.WriteLine(\"caught\")\nEnd Try\nEnd Sub"),
            Is.EqualTo("caught"));

    // ---------------------------------------------------------------- rethrow

    /// <summary>A bare <c>Throw</c> must rethrow the ORIGINAL object, whatever the clause called it.</summary>
    [Test]
    public void BareThrow_RethrowsTheSameExceptionToTheOuterHandler()
        => Assert.That(Run(
            "Sub Main()\nTry\nTry\nThrow New ArgumentException(\"inner\")\n" +
            "Catch e As ArgumentException\nConsole.WriteLine(\"inner: \" & e.Message)\nThrow\nEnd Try\n" +
            "Catch o As Exception\nConsole.WriteLine(\"outer: \" & o.Message)\nEnd Try\nEnd Sub"),
            Is.EqualTo("inner: inner\nouter: inner"));

    [Test]
    public void BareThrow_FromALadderArm_Rethrows()
        => Assert.That(Run(
            "Sub Main()\nTry\nTry\nThrow New ArgumentException(\"a\")\n" +
            "Catch e As ArgumentException\nThrow\n" +
            "Catch f As FormatException\nConsole.WriteLine(\"wrong\")\nEnd Try\n" +
            "Catch o As Exception\nConsole.WriteLine(\"outer\")\nEnd Try\nEnd Sub"),
            Is.EqualTo("outer"));

    // ---------------------------------------------------------------- user-defined exception types

    /// <summary>A user class that Inherits Exception is a real subtype: caught by its own name, skipped by a sibling's.</summary>
    [Test]
    public void UserExceptionClass_IsCaughtByItsOwnType_AndSkippedByASibling()
        => Assert.That(Run(
            "Class MyError\nInherits Exception\nEnd Class\n" +
            "Class OtherError\nInherits Exception\nEnd Class\n" +
            "Sub Main()\nTry\nThrow New MyError()\n" +
            "Catch o As OtherError\nConsole.WriteLine(\"other\")\n" +
            "Catch m As MyError\nConsole.WriteLine(\"mine\")\n" +
            "End Try\nEnd Sub"),
            Is.EqualTo("mine"));

    [Test]
    public void UserExceptionClass_IsCaughtByTheExceptionClause()
        => Assert.That(Run(
            "Class MyError\nInherits Exception\nEnd Class\n" +
            "Sub Main()\nTry\nThrow New MyError()\n" +
            "Catch e As Exception\nConsole.WriteLine(\"caught\")\nEnd Try\nEnd Sub"),
            Is.EqualTo("caught"));

    /// <summary>
    /// The message reaches the base through <c>MyBase.New(msg)</c>, which had never parsed
    /// (see <see cref="BaseConstructorCallTests"/>). Reading it back proves the whole chain:
    /// parse → base-constructor args → <c>super(msg)</c> → <c>Exception.Message</c>.
    /// </summary>
    [Test]
    public void UserExceptionClass_PassesItsMessageToTheBase()
        => Assert.That(Run(
            "Class MyError\nInherits Exception\n" +
            "Public Sub New(msg As String)\nMyBase.New(msg)\nEnd Sub\nEnd Class\n" +
            "Sub Main()\nTry\nThrow New MyError(\"custom\")\n" +
            "Catch e As Exception\nConsole.WriteLine(\"caught: \" & e.Message)\nEnd Try\nEnd Sub"),
            Is.EqualTo("caught: custom"));

    /// <summary>
    /// A two-level USER chain: a clause naming the user base catches the user derived type.
    /// (Deriving from a provided subtype such as <c>ArgumentException</c> is refused by the
    /// analyzer on every backend — "Unknown base class" — so only <c>Exception</c> can root a
    /// user hierarchy today; that is a front-end limit, not a JavaScript one.)
    /// </summary>
    [Test]
    public void UserExceptionChain_ABaseClauseCatchesTheDerivedThrow()
        => Assert.That(Run(
            "Class AppError\nInherits Exception\nEnd Class\n" +
            "Class ConfigError\nInherits AppError\nEnd Class\n" +
            "Sub Main()\nTry\nThrow New ConfigError()\n" +
            "Catch f As FormatException\nConsole.WriteLine(\"format\")\n" +
            "Catch a As AppError\nConsole.WriteLine(\"app\")\n" +
            "End Try\nEnd Sub"),
            Is.EqualTo("app"));

    // ---------------------------------------------------------------- native JS errors

    /// <summary>
    /// <c>Catch e As Exception</c> is the catch-all, as <c>catch (Exception)</c> is in .NET —
    /// a JavaScript-native error (a TypeError from a null dereference, say) must not slip past
    /// it, and its message must be readable through the BasicLang surface.
    /// </summary>
    [Test]
    public void NativeJsError_IsCaughtByTheExceptionClause_WithItsMessage()
        => Assert.That(Run(
            "Sub Main()\nTry\njavascript{ throw new TypeError(\"native\"); }\n" +
            "Catch e As Exception\nConsole.WriteLine(\"caught: \" & e.Message)\nEnd Try\nEnd Sub"),
            Is.EqualTo("caught: native"));

    /// <summary>…but a TYPED clause must not claim it: a TypeError is not an ArgumentException.</summary>
    [Test]
    public void NativeJsError_IsNotCaughtByATypedClause()
        => Assert.That(Run(
            "Sub Main()\nTry\nTry\njavascript{ throw new TypeError(\"native\"); }\n" +
            "Catch e As ArgumentException\nConsole.WriteLine(\"wrong\")\nEnd Try\n" +
            "Catch o As Exception\nConsole.WriteLine(\"outer\")\nEnd Try\nEnd Sub"),
            Is.EqualTo("outer"));

    // ---------------------------------------------------------------- with Finally / in loops

    [Test]
    public void Finally_RunsAfterALadderArm()
        => Assert.That(Run(
            "Sub Main()\nTry\nThrow New FormatException(\"f\")\n" +
            "Catch a As ArgumentException\nConsole.WriteLine(\"arg\")\n" +
            "Catch f As FormatException\nConsole.WriteLine(\"fmt\")\n" +
            "Finally\nConsole.WriteLine(\"finally\")\nEnd Try\nEnd Sub"),
            Is.EqualTo("fmt\nfinally"));

    [Test]
    public void Finally_RunsWhenNoArmMatches_AndTheExceptionPropagates()
        => Assert.That(Run(
            "Sub Main()\nTry\nTry\nThrow New FormatException(\"f\")\n" +
            "Catch a As ArgumentException\nConsole.WriteLine(\"wrong\")\n" +
            "Finally\nConsole.WriteLine(\"finally\")\nEnd Try\n" +
            "Catch o As Exception\nConsole.WriteLine(\"outer\")\nEnd Try\nEnd Sub"),
            Is.EqualTo("finally\nouter"));

    [Test]
    public void Ladder_InsideALoop_SelectsPerIteration()
        => Assert.That(Run(
            "Sub Main()\nFor i As Integer = 1 To 3\nTry\n" +
            "If i = 1 Then\nThrow New ArgumentException(\"a\")\nEnd If\n" +
            "If i = 2 Then\nThrow New FormatException(\"f\")\nEnd If\n" +
            "Console.WriteLine(\"ok\")\n" +
            "Catch a As ArgumentException\nConsole.WriteLine(\"arg\")\n" +
            "Catch f As FormatException\nConsole.WriteLine(\"fmt\")\n" +
            "End Try\nNext\nEnd Sub"),
            Is.EqualTo("arg\nfmt\nok"));

    // ---------------------------------------------------------------- the SHIPPING IR

    [Test]
    public void Optimized_TheAnswerDoesNotDependOnClauseOrder()
        => Assert.That(JavaScriptOptimizedExecutionTests.RunOptimized(
            OrderedF +
            "Function Swapped() As String\n" +
            "Try\nThrow New ArgumentException(\"boom\")\n" +
            "Catch b As Exception\nReturn \"EX\"\n" +
            "Catch e As InvalidOperationException\nReturn \"IOE\"\n" +
            "End Try\nEnd Function\n" +
            "Sub Main()\nConsole.WriteLine(F())\nConsole.WriteLine(Swapped())\nEnd Sub"),
            Is.EqualTo("EX\nEX"));

    [Test]
    public void Optimized_MessageSurvivesTheThrow()
        => Assert.That(JavaScriptOptimizedExecutionTests.RunOptimized(
            "Sub Main()\nTry\nThrow New InvalidOperationException(\"boom\")\n" +
            "Catch e As Exception\nConsole.WriteLine(e.Message)\nEnd Try\nEnd Sub"),
            Is.EqualTo("boom"));
}

/// <summary>
/// The exception hierarchy's codegen-side contract — no Node needed.
/// </summary>
[TestFixture]
public class JavaScriptExceptionCodeGenTests
{
    /// <summary>
    /// Every exception the program can name must EXIST in the output, or a Throw is a
    /// ReferenceError from a green build. Under the governing rule that means an unknown
    /// <c>…Exception</c> name is REJECTED, not erased — the earlier suffix rule admitted any
    /// spelling on the promise that the generator erased it to <c>Error</c>, and the generator
    /// never did.
    /// </summary>
    [Test]
    public void ThrowingAnUndeclaredExceptionType_IsRejected_BL7012()
    {
        var ex = Assert.Throws<ForeignFeatureException>(() => JsTestSupport.Compile(
            "Sub Main()\nThrow New FooException(\"x\")\nEnd Sub"));

        Assert.That(ex!.Message, Does.Contain("BL7012"));
        Assert.That(ex.Message, Does.Contain("FooException"));
        Assert.That(ex.Message, Does.Contain("Inherits Exception"), "the message must say how to declare one");
    }

    [Test]
    public void CatchingAnUndeclaredExceptionType_IsRejected_BL7012()
    {
        var ex = Assert.Throws<ForeignFeatureException>(() => JsTestSupport.Compile(
            "Sub Main()\nTry\nConsole.WriteLine(1)\nCatch e As FooException\nConsole.WriteLine(2)\nEnd Try\nEnd Sub"));

        Assert.That(ex!.Message, Does.Contain("BL7012"));
        Assert.That(ex.Message, Does.Contain("FooException"));
    }

    /// <summary>A user-declared …Exception class is not "undeclared" — the rejection must consult the module.</summary>
    [Test]
    public void ADeclaredExceptionClass_IsNotRejected()
        => Assert.That(JsTestSupport.Compile(
                "Class FooException\nInherits Exception\nEnd Class\n" +
                "Sub Main()\nThrow New FooException()\nEnd Sub"),
            Does.Contain("class FooException extends Exception"));

    /// <summary>
    /// The hierarchy is emitted on demand — hello world must not carry twenty exception
    /// classes — and only the closure the program needs: a program that throws
    /// ArgumentNullException needs ArgumentException, SystemException and Exception, not
    /// FormatException.
    /// </summary>
    [Test]
    public void Prelude_IsAbsentWhenNoExceptionIsMentioned()
        => Assert.That(JsTestSupport.Compile("Sub Main()\nConsole.WriteLine(\"hi\")\nEnd Sub"),
            Does.Not.Contain("class Exception"));

    [Test]
    public void Prelude_IsTheTransitiveClosureOfTheNamesUsed()
    {
        var js = JsTestSupport.Compile("Sub Main()\nThrow New ArgumentNullException(\"p\")\nEnd Sub");

        Assert.That(js, Does.Contain("class Exception extends Error"));
        Assert.That(js, Does.Contain("class SystemException extends Exception"));
        Assert.That(js, Does.Contain("class ArgumentException extends SystemException"));
        Assert.That(js, Does.Contain("class ArgumentNullException extends ArgumentException"));
        Assert.That(js, Does.Not.Contain("class FormatException"));
    }

    /// <summary>A Catch clause is a use too — the program never constructs the type but tests against it.</summary>
    [Test]
    public void Prelude_IncludesTypesNamedOnlyInACatch()
        => Assert.That(JsTestSupport.Compile(
                "Sub Main()\nTry\nConsole.WriteLine(1)\nCatch e As FormatException\nConsole.WriteLine(2)\nEnd Try\nEnd Sub"),
            Does.Contain("class FormatException extends SystemException"));

    /// <summary>The classes are declared BEFORE any user class — `extends Exception` on a user class hits the TDZ otherwise.</summary>
    [Test]
    public void Prelude_PrecedesUserClasses()
    {
        var js = JsTestSupport.Compile(
            "Class MyError\nInherits Exception\nEnd Class\n" +
            "Sub Main()\nThrow New MyError()\nEnd Sub");

        var prelude = js.IndexOf("class Exception extends Error", System.StringComparison.Ordinal);
        var user = js.IndexOf("class MyError extends Exception", System.StringComparison.Ordinal);

        // Both must EXIST — an IndexOf of -1 on either side would make the comparison pass vacuously.
        Assert.That(prelude, Is.GreaterThanOrEqualTo(0), "the Exception root is not emitted");
        Assert.That(user, Is.GreaterThanOrEqualTo(0), "the user class is not emitted");
        Assert.That(prelude, Is.LessThan(user));
    }

    /// <summary>
    /// BasicLang is case-insensitive; JavaScript is not. Any spelling must reach the ONE
    /// canonical class. <c>Exception</c> is the analyzer's built-in (case-insensitive) root —
    /// the other .NET names resolve case-SENSITIVELY in the front-end on every backend, so the
    /// root is the spelling that can actually arrive here in the wrong case.
    /// </summary>
    [Test]
    public void ExceptionNames_AreCanonicalisedRegardlessOfSourceCasing()
    {
        var js = JsTestSupport.Compile(
            "Class MyError\nInherits exception\nEnd Class\n" +
            "Sub Main()\nTry\nThrow New EXCEPTION(\"x\")\nCatch e As exception\nConsole.WriteLine(1)\nEnd Try\nEnd Sub");

        Assert.That(js, Does.Contain("new Exception("));
        Assert.That(js, Does.Contain("class MyError extends Exception"));
        Assert.That(js, Does.Not.Contain("EXCEPTION"));
        Assert.That(js, Does.Not.Contain("extends exception"));
    }

    /// <summary>Redeclaring a provided exception type would emit two `class ArgumentException` — a SyntaxError. Refuse like BL7011 does for Console.</summary>
    [Test]
    public void DeclaringAClassNamedLikeAProvidedException_IsRejected()
    {
        var ex = Assert.Throws<ForeignFeatureException>(() => JsTestSupport.Compile(
            "Class ArgumentException\nEnd Class\nSub Main()\nEnd Sub"));

        Assert.That(ex!.Message, Does.Contain("BL7011"));
        Assert.That(ex.Message, Does.Contain("ArgumentException"));
    }

    /// <summary>Single typed clause: the guard must be present in the text, so the over-catch cannot come back silently.</summary>
    [Test]
    public void ASingleTypedClause_EmitsAnInstanceofGuard()
    {
        var js = JsTestSupport.Compile(
            "Sub Main()\nTry\nConsole.WriteLine(1)\nCatch e As FormatException\nConsole.WriteLine(2)\nEnd Try\nEnd Sub");

        Assert.That(js, Does.Contain("instanceof FormatException"));
        Assert.That(js, Does.Contain("throw _ex;"), "a non-matching exception must be rethrown, not swallowed");
    }

    /// <summary>
    /// The catch-all clause needs no guard and no rethrow. (The prelude's own <c>Wrap</c>
    /// helper contains an <c>instanceof</c>; the assertion is on the CATCH's test, <c>_ex
    /// instanceof</c>, which only a ladder arm emits.)
    /// </summary>
    [Test]
    public void AnExceptionClause_HasNoGuard()
    {
        var js = JsTestSupport.Compile(
            "Sub Main()\nTry\nConsole.WriteLine(1)\nCatch e As Exception\nConsole.WriteLine(2)\nEnd Try\nEnd Sub");

        Assert.That(js, Does.Not.Contain("_ex instanceof"));
        Assert.That(js, Does.Not.Contain("throw _ex;"));
        Assert.That(js, Does.Contain("const e = Exception.Wrap(_ex);"));
    }
}
