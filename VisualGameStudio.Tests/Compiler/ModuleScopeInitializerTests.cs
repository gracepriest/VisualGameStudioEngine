using System;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A module-scope <c>Dim</c> or <c>Const</c> whose initializer is an EXPRESSION rather than a bare
/// literal.
///
/// <para>⛔ Every such shape CRASHED THE COMPILER. Expression lowering names its temps through
/// <c>_currentFunction.GetNextTempName()</c>, and at module scope <c>_currentFunction</c> is null
/// by definition of the branch — so <c>Dim G As Integer = 40 + 2</c>, in a file with no class in it
/// at all, died with <c>Error at line 0: ... Object reference not set to an instance of an
/// object</c>. Identical on all four backends, because it happened in the IR builder before any of
/// them ran. An in-code note had blamed <c>New</c> initializers specifically; measured, the
/// crashing set was far wider.</para>
///
/// <para>⚠ TWO call sites, one root cause: the global <c>Dim</c> branch and the global <c>Const</c>
/// branch each lowered the initializer the same way. Fixing one and not the other is how half a
/// crash survives, so both go through <c>BuildModuleScopeInitializer</c>.</para>
///
/// <para>⚠ The fix FOLDS rather than refuses where it can, because a global's
/// <c>InitialValue</c> is required to be a constant — the JavaScript backend already refused a
/// non-constant one outright, and C# and MSIL would have emitted a temp name that is not in
/// scope. What genuinely needs code to run first is refused with a diagnostic instead of a
/// crash.</para>
/// </summary>
[TestFixture]
public class ModuleScopeInitializerTests
{
    private static string Program(string declaration, string print) => $"""
        Module M
         {declaration}
         Function Helper() As Integer
          Return 7
         End Function
         Sub Main()
          {print}
         End Sub
        End Module
        """;

    // ====================================================================================
    // What now FOLDS — these all crashed the compiler before.
    // ====================================================================================

    /// <summary>
    /// ⚠ RUN, not merely compiled. The whole point is that the constant reaches the emitted
    /// program; a build that succeeds while the global stays 0 is exactly the failure mode the
    /// MSIL <c>.cctor</c> comment warns about — and exactly what C++ does, see
    /// <see cref="ACppGlobalInitializer_IsStillDropped"/>.
    ///
    /// <para>⛔ THREE backends here, not four. C++ carries these too and is asserted in
    /// <see cref="AModuleScopeInitializer_ReachesTheCppProgram"/> — historically because it
    /// formatted a Double as <c>3.500000</c>; it prints <c>3.5</c> now, as .NET does.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    [TestCase("Dim G As Integer = 40 + 2", "PrintLine(CStr(G))", "42", TestName = "Fold_Arithmetic")]
    [TestCase("Dim G As String = \"a\" & \"b\"", "PrintLine(G)", "ab", TestName = "Fold_Concat")]
    [TestCase("Dim G As Integer = (1 + 2) * 3", "PrintLine(CStr(G))", "9", TestName = "Fold_Nested")]
    [TestCase("Dim G As Double = 7.0 / 2.0", "PrintLine(CStr(G))", "3.5", TestName = "Fold_RealDiv")]
    [TestCase("Dim G As Integer = 8 \\ 2", "PrintLine(CStr(G))", "4", TestName = "Fold_IntDiv")]
    [TestCase("Const C As Integer = 40 + 2", "PrintLine(CStr(C))", "42", TestName = "Fold_ConstArith")]
    [TestCase("Const C As String = \"a\" & \"b\"", "PrintLine(C)", "ab", TestName = "Fold_ConstConcat")]
    public void AComputedModuleScopeInitializer_FoldsAndReaches_EveryBackend(
        string declaration, string print, string expected)
    {
        var program = Program(declaration, print);

        Assert.Multiple(() =>
        {
            Assert.That(ReturnCoercionTests.CompileEmittedCSharpForTest(program), Is.Empty);
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo(expected + "\n"));
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo(expected));
        });
    }

    /// <summary>
    /// ⛔ The C++ backend used to DROP a module-scope global's initializer entirely.
    /// <c>CppCodeGenerator</c> emitted <c>{}</c> for every global that is not a sized array and
    /// never consulted <c>InitialValue</c>, so <c>Dim G As Integer = 42</c> became
    /// <c>int32_t G = {};</c> and the program printed <b>0</b> — a build with the right answer
    /// nowhere in it, no diagnostic and no crash. C#, MSIL and JavaScript all carried the value.
    /// Verified at the time on unmodified master: a plain LITERAL printed 0 there too, so this
    /// was never the folding path.
    ///
    /// <para>⚠ Separate from the shared list above because C++ used to FORMAT a Double
    /// differently (<c>CStr(3.5)</c> was <c>3.500000</c>). It prints <c>3.5</c> now, as C# and
    /// MSIL do (CppDoubleFormattingTests). Still an exact string, never trimmed: a test that
    /// normalised the digits would also pass if the initializer were lost and the global read
    /// 0.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    [TestCase("Dim G As Integer = 42", "PrintLine(CStr(G))", "42", TestName = "Cpp_Literal")]
    [TestCase("Dim G As Integer = 40 + 2", "PrintLine(CStr(G))", "42", TestName = "Cpp_Arithmetic")]
    [TestCase("Dim G As String = \"a\" & \"b\"", "PrintLine(G)", "ab", TestName = "Cpp_Concat")]
    [TestCase("Dim G As Integer = (1 + 2) * 3", "PrintLine(CStr(G))", "9", TestName = "Cpp_Nested")]
    [TestCase("Dim G As Boolean = True", "PrintLine(CStr(G))", "True", TestName = "Cpp_Bool")]
    [TestCase("Const C As Integer = 40 + 2", "PrintLine(CStr(C))", "42", TestName = "Cpp_Const")]
    [TestCase("Dim G As Double = 7.0 / 2.0", "PrintLine(CStr(G))", "3.5", TestName = "Cpp_Double")]
    public void AModuleScopeInitializer_ReachesTheCppProgram(
        string declaration, string print, string expected)
    {
        var program = Program(declaration, print);

        Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program)),
            Is.EqualTo(expected + "\n"));
    }

    /// <summary>
    /// ⚠ A global initialized from ANOTHER global emits the referenced global's NAME on C++
    /// (<c>int32_t I = H;</c>), not a constant. That is correct C++ only because the generator
    /// writes globals in DECLARATION ORDER and C++ initializes namespace-scope objects in that
    /// order within a translation unit — reordering that loop would break this silently, which is
    /// why the shape is asserted rather than assumed.
    ///
    /// <para>⛔ JavaScript REFUSES this shape outright — "a module-level initializer for 'G' that
    /// is not a constant" — so it is asserted on C++ and MSIL only. Worth stating plainly: the
    /// backends do NOT agree here, and JS is the strict one.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    public void AGlobalInitializedFromAnotherGlobal_ReachesCpp()
    {
        var program = """
            Module M
             Dim H As Integer = 7
             Dim I As Integer = H
             Sub Main()
              PrintLine(CStr(I))
             End Sub
            End Module
            """;

        Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program)), Is.EqualTo("7\n"));
    }

    /// <summary>
    /// ⚠ Regression: a global with NO initializer must still get <c>{}</c> on C++, and a sized
    /// array must still allocate. Only the has-an-initializer case changed; routing the others
    /// anywhere new would change every uninitialized global's emission.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void ACppGlobalWithNoInitializer_IsUnchanged()
    {
        var declarationOnly = Program("Dim G As Integer", "PrintLine(CStr(G))");
        var sizedArray = """
            Module M
             Dim G(3) As Integer
             Sub Main()
              G(0) = 5
              PrintLine(CStr(G(0)))
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(declarationOnly)),
                Is.EqualTo("0\n"));
            Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(sizedArray)),
                Is.EqualTo("5\n"));
        });
    }

    /// <summary>
    /// ⚠ A folded COMPARISON, split out from the list above because the backends used to
    /// disagree about how a Boolean PRINTS: <c>CStr(True)</c> was <c>"True"</c> on C# and MSIL
    /// and <c>"true"</c> on JavaScript.
    ///
    /// <para>That divergence had nothing to do with module scope, and master's #102 made
    /// JavaScript spell a Boolean as .NET does, so every backend now prints <c>True</c>. It is
    /// still pinned exactly rather than normalised, because a test that lowercased both sides
    /// would also pass if the fold silently produced the wrong boolean.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    public void AFoldedComparison_ReachesEveryBackend_ModuloBooleanFormatting()
    {
        var program = Program("Dim G As Boolean = 1 < 2", "PrintLine(CStr(G))");

        Assert.Multiple(() =>
        {
            Assert.That(ReturnCoercionTests.CompileEmittedCSharpForTest(program), Is.Empty);
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo("True\n"));
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("True"),
                "JavaScript spells a Boolean as .NET does since #102");
        });
    }

    /// <summary>
    /// ⚠ STRUCTURAL: the fold must reach the IR, not merely stop the crash. A global whose
    /// <c>InitialValue</c> is anything other than an <c>IRConstant</c> is the exact shape the
    /// JavaScript backend refuses and the other two miscompile, so asserting the run alone would
    /// leave the invariant itself untested.
    /// </summary>
    [Test]
    public void TheFoldedInitializer_IsAConstantInTheIR()
    {
        var module = JsTestSupport.BuildModule(Program("Dim G As Integer = (1 + 2) * 3", "PrintLine(CStr(G))"));

        var global = module.GlobalVariables["G"];

        Assert.That(global.InitialValue, Is.InstanceOf<BasicLang.Compiler.IR.IRConstant>(),
            "a non-constant global initializer is what no backend can emit");
        Assert.That(((BasicLang.Compiler.IR.IRConstant)global.InitialValue).Value, Is.EqualTo(9),
            "the whole expression must fold, not just its innermost operation");
    }

    /// <summary>
    /// ⚠ The scratch function that gives lowering somewhere to emit must NOT be registered on the
    /// module. Building it through <c>_module.CreateFunction</c> would leave every backend
    /// emitting a stray function per initialized global.
    /// </summary>
    [Test]
    public void TheScratchFunction_IsNotEmittedAsAModuleFunction()
    {
        var module = JsTestSupport.BuildModule(Program("Dim G As Integer = 40 + 2", "PrintLine(CStr(G))"));

        Assert.That(module.Functions, Has.None.Matches<BasicLang.Compiler.IR.IRFunction>(
            f => f.Name != null && f.Name.Contains("<init>")),
            "the scratch function leaked into the module's function list");
    }

    // ====================================================================================
    // What is REFUSED — and that it is a diagnostic, not a crash.
    // ====================================================================================

    /// <summary>
    /// ⛔ An initializer that needs code to RUN first cannot become a constant, and no backend has
    /// a module initializer to run it in. Refused with a diagnostic naming the variable.
    ///
    /// <para>⚠ The assertion is that the message is the REFUSAL, not merely that something threw —
    /// before this change these same programs threw too, with
    /// <c>Object reference not set to an instance of an object</c>. "It throws" would have passed
    /// on the crash.</para>
    /// </summary>
    [Test]
    [TestCase("Dim G As Integer = Helper()", TestName = "Refuse_Call")]
    [TestCase("Dim G As New List(Of Integer)()", TestName = "Refuse_New")]
    [TestCase("Const C As Integer = Helper()", TestName = "Refuse_ConstCall")]
    public void AnInitializerNeedingRuntimeCode_IsRefusedWithADiagnostic(string declaration)
    {
        var ex = Assert.Throws<Exception>(
            () => JsTestSupport.BuildModule(Program(declaration, "PrintLine(\"ok\")")));

        Assert.Multiple(() =>
        {
            Assert.That(ex.Message, Does.Contain("cannot be computed at compile time"));
            Assert.That(ex.Message, Does.Not.Contain("Object reference not set"),
                "the crash this replaced also threw — asserting only that it throws proves nothing");
        });
    }

    /// <summary>
    /// ⚠ Integer-literal division promoted to Double now FOLDS. <c>/</c> promotes both operands, so
    /// the block is <c>IRCast, IRCast, IRBinaryOp</c> and neither operand was a constant until
    /// <c>WideningCastFoldingPass</c> reduced the casts.
    ///
    /// <para>⛔ WIDENING ONLY, and the reason is measured rather than cautious: the backends
    /// DISAGREE about narrowing. On <c>CInt(7.5)</c>, <c>CInt(8.5)</c>, <c>CInt(7.9)</c>,
    /// <c>CInt(-7.5)</c>, C# prints <c>8,8,8,-8</c> (rounds — the VB answer) while MSIL,
    /// JavaScript and C++ all print <c>7,8,7,-7</c> (truncate). Any single compile-time answer
    /// would change one of them, so a folder is not allowed an opinion until the backends agree at
    /// run time. See <see cref="ANarrowingConversion_IsStillRefused"/>.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    [TestCase("Dim G As Double = 7 / 2", "3.5", TestName = "Widen_SevenOverTwo")]
    [TestCase("Dim G As Double = 1 / 2", "0.5", TestName = "Widen_OneOverTwo")]
    [TestCase("Dim G As Double = (1 + 2) / 4", "0.75", TestName = "Widen_FoldThenWiden")]
    public void IntegerDivisionPromotedToDouble_Folds(string declaration, string expected)
    {
        var program = Program(declaration, "PrintLine(CStr(G))");

        Assert.Multiple(() =>
        {
            Assert.That(ReturnCoercionTests.CompileEmittedCSharpForTest(program), Is.Empty);
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo(expected + "\n"));
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo(expected));
        });
    }

    /// <summary>
    /// ⚠ <c>(1 + 2) / 4</c> and <c>7 / 2</c> need OPPOSITE fold orders, which is why the helper
    /// alternates the two passes to a fixpoint rather than running each once: <c>7 / 2</c> needs
    /// the casts folded first (the division's operands are casts until then), while
    /// <c>(1 + 2) / 4</c> needs the addition folded first (the promoting cast's operand is the
    /// sum). Both are in the case list above; this note records why one ordering cannot serve.
    /// </summary>
    [Test]
    public void TheWidenedDivision_IsAConstantInTheIR()
    {
        var module = JsTestSupport.BuildModule(Program("Dim G As Double = 7 / 2", "PrintLine(CStr(G))"));

        var initial = module.GlobalVariables["G"].InitialValue;

        Assert.That(initial, Is.InstanceOf<BasicLang.Compiler.IR.IRConstant>());
        Assert.That(((BasicLang.Compiler.IR.IRConstant)initial).Value, Is.EqualTo(3.5));
    }

    /// <summary>
    /// ⛔ A NARROWING conversion is still refused, and this is the test that keeps it that way.
    /// <c>CInt(7.5)</c> is not even a cast — measured, <c>CInt</c>/<c>CDbl</c> lower to an
    /// <c>IRCall</c>, so neither folding pass touches them — but the refusal matters for a second,
    /// bigger reason: the four backends do not agree on what a narrowing conversion MEANS
    /// (C# rounds, MSIL/JavaScript/C++ truncate), so there is no single constant a folder could
    /// produce without changing one of them.
    ///
    /// <para>⚠ When someone settles that divergence at run time, this test is where to start —
    /// folding must follow the backends, not lead them.</para>
    /// </summary>
    [Test]
    [TestCase("Dim G As Integer = CInt(7.5)", TestName = "Refuse_NarrowingCInt")]
    [TestCase("Dim G As Double = CDbl(1 + 2)", TestName = "Refuse_ConversionCall")]
    public void ANarrowingConversion_IsStillRefused(string declaration)
    {
        var ex = Assert.Throws<Exception>(
            () => JsTestSupport.BuildModule(Program(declaration, "PrintLine(CStr(G))")));

        Assert.That(ex.Message, Does.Contain("cannot be computed at compile time"));
    }

    /// <summary>
    /// ⚠ The backends AGREE about a narrowing conversion. They did not: this test used to pin
    /// MSIL, JavaScript and C++ at <c>7,8,7,-7</c> (truncation) against C#'s <c>8,8,8,-8</c>
    /// (rounding), one language with two answers. The three were fixed to agree with C#, which
    /// was the VB-correct one.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void NarrowingConversion_AgreesAcrossBackends()
    {
        var program = """
            Module M
             Sub Main()
              Dim a As Double = 7.5
              Dim b As Double = 8.5
              Dim c As Double = 7.9
              Dim d As Double = -7.5
              PrintLine(CStr(CInt(a)) & "," & CStr(CInt(b)) & "," & CStr(CInt(c)) & "," & CStr(CInt(d)))
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo("8,8,8,-8\n"),
                "MSIL rounds half-to-even");
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("8,8,8,-8"),
                "JavaScript rounds half-to-even");
            Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program)),
                Is.EqualTo("8,8,8,-8\n"), "C++ rounds half-to-even");
        });
    }

    /// <summary>
    /// ⛔ THE MISCOMPILE THIS FIXTURE WAS OPENED ON. A module-scope declaration was never coerced
    /// to its DECLARED type — the local branch of <c>Visit(VariableDeclarationNode)</c> has always
    /// called <c>CoerceToDeclaredType</c>, the global branch never did — so a Double literal went
    /// straight into a narrower global and each backend reinterpreted its bits. It compiled clean
    /// and printed garbage, with no diagnostic anywhere.
    ///
    /// <para>⛔ Measured on MSIL before the fix, <c>Dim v As T = 7.9</c> at module scope:
    /// Byte <b>154</b>, SByte <b>-102</b>, Short and UShort an <b>empty string</b>, Integer
    /// <b>-1717986918</b>, UInteger <b>2576980378</b>, Long and ULong
    /// <b>4620580627691444634</b> — the IEEE-754 bit pattern of 7.9 read as an integer. The SAME
    /// declarations as LOCALS printed 7 throughout, which is what made the missing coercion the
    /// root cause rather than the sub-integer type table an earlier comment blamed.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    [TestCase("Byte", TestName = "Narrow_Byte")]
    [TestCase("SByte", TestName = "Narrow_SByte")]
    [TestCase("Short", TestName = "Narrow_Short")]
    [TestCase("UShort", TestName = "Narrow_UShort")]
    [TestCase("Integer", TestName = "Narrow_Integer")]
    [TestCase("UInteger", TestName = "Narrow_UInteger")]
    [TestCase("Long", TestName = "Narrow_Long")]
    public void AModuleScopeInitializer_IsCoercedToItsDeclaredType(string declaredType)
    {
        var program = Program($"Dim G As {declaredType} = 7.9", "PrintLine(CStr(G))");

        Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo("8\n"));
    }

    /// <summary>
    /// ⚠ The narrowed constant must reach EVERY backend, not just MSIL — the bug was one of
    /// emission, and three backends emitted the raw Double.
    ///
    /// <para>⛔ Byte is the case the fix was asked for. Integer is here too because it was ALSO
    /// broken at module scope (<b>-1717986918</b>) despite being a type
    /// <c>TryConvertConstant</c> has always handled — the clearest evidence the defect was the
    /// missing coercion rather than the type table.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    [TestCase("Byte", TestName = "NarrowAllBackends_Byte")]
    [TestCase("Short", TestName = "NarrowAllBackends_Short")]
    [TestCase("Integer", TestName = "NarrowAllBackends_Integer")]
    public void ANarrowedInitializer_ReachesEveryBackend(string declaredType)
    {
        var program = Program($"Dim G As {declaredType} = 7.9", "PrintLine(CStr(G))");

        Assert.Multiple(() =>
        {
            Assert.That(ReturnCoercionTests.CompileEmittedCSharpForTest(program), Is.Empty);
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo("8\n"));
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("8"));
            Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program)),
                Is.EqualTo("8\n"));
        });
    }

    /// <summary>
    /// ⚠ OUT OF RANGE is REFUSED at BOTH scopes, as of 2026-09-18 — VB's BC30439, in
    /// <c>SemanticAnalyzer</c>. Kept in this fixture because this is where the wrap it replaces
    /// was pinned; the rule itself is covered in <see cref="ConstantRangeTests"/>.
    ///
    /// <para>⛔ This test used to assert the WRAP and to agree it was the right answer: "matching
    /// the LOCAL path is the deliberate choice ... fixing the wrap belongs in the front end where
    /// both paths would get it at once." The front end now has it, and the premise the choice
    /// rested on was itself wrong. What module scope wrapped to —
    /// <c>Byte = 300</c> → <b>44</b>, <c>Byte = -1</c> → <b>255</b>, <c>SByte = 200</c> →
    /// <b>-56</b>, <c>Short = 40000</c> → <b>-25536</b>, <c>UShort = 70000</c> → <b>4464</b> —
    /// is what the LOCAL path produced only on C++ and MSIL. Re-measured for
    /// <c>Dim b As Byte = 300</c> as a local: <b>C#</b> CS0031, which does not build, and
    /// <b>JavaScript</b> <b>300</b>. There was no local answer to match.</para>
    ///
    /// <para>⚠ No longer an Integration test: it is a diagnostic, so nothing is compiled or run.
    /// The module-scope narrowing path it used to exercise is still covered for IN-RANGE values
    /// by <see cref="ANarrowedInitializer_ReachesEveryBackend"/>.</para>
    /// </summary>
    [Test]
    [TestCase("Byte", "300", TestName = "Refused_ByteOver")]
    [TestCase("Byte", "-1", TestName = "Refused_ByteUnder")]
    [TestCase("SByte", "200", TestName = "Refused_SByte")]
    [TestCase("Short", "40000", TestName = "Refused_Short")]
    [TestCase("UShort", "70000", TestName = "Refused_UShort")]
    public void AnOutOfRangeInitializer_IsRefusedAtBothScopes(string declaredType, string literal)
    {
        var moduleScope = Program($"Dim G As {declaredType} = {literal}", "PrintLine(CStr(G))");
        var local = Program("", $"Dim v As {declaredType} = {literal}\n  PrintLine(CStr(v))");

        Assert.Multiple(() =>
        {
            Assert.That(OptionalConstructorTests.Analyze(moduleScope),
                Has.Some.Contains(
                    $"Constant expression not representable in type '{declaredType}'"),
                "module scope");
            Assert.That(OptionalConstructorTests.Analyze(local),
                Has.Some.Contains(
                    $"Constant expression not representable in type '{declaredType}'"),
                "the local it must agree with");
        });
    }

    /// <summary>
    /// ⚠ A narrow initializer that is FOLDED rather than a bare literal. It leaves
    /// <c>BuildModuleScopeInitializer</c> by the OTHER return — the folded-constants one — so
    /// narrowing has to happen at both exits or this shape keeps the pre-narrowing value.
    ///
    /// <para>⛔ BOTH assertions hold the folded exit now, measured by mutation: dropping the
    /// narrowing leaves a <c>Double</c>-typed 200.7, C# is <b>CS0266</b> ("cannot implicitly
    /// convert type 'double' to 'byte'") and MSIL prints <b>102</b> — the low byte of the float64
    /// it stored. That is a change from the shape this test used to use: with
    /// <c>500 - 200</c> the un-narrowed value was an <c>Integer</c> 300, which MSIL's
    /// <c>stsfld uint8</c> truncated to 44 all by itself, so the MSIL run passed either way and
    /// only C# witnessed the missing narrowing. A FRACTIONAL fold is caught on both.</para>
    ///
    /// <para>⚠ <c>100.5 + 100.2</c> also proves the narrowing runs AFTER the fold rather than on
    /// the OPERANDS: rounding each operand first gives 100 + 100 = <b>200</b>, and rounding the
    /// sum gives <b>201</b>. The two are distinguishable, which the old <c>500 - 200</c> was only
    /// because it overflowed — that constant is refused outright now (BC30439), so the property
    /// had to be re-expressed with a value the front end accepts.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    public void AFoldedNarrowInitializer_IsNarrowedToo()
    {
        var program = """
            Module M
             Dim G As Byte = 4 + 4
             Dim H As Byte = 100.5 + 100.2
             Sub Main()
              PrintLine(CStr(G) & "," & CStr(H))
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(ReturnCoercionTests.CompileEmittedCSharpForTest(program), Is.Empty,
                "C# refuses an un-narrowed 200.7 in a byte field (CS0266)");
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo("8,201\n"));
        });
    }

    /// <summary>
    /// ⛔ ULong is REFUSED rather than narrowed. Its range does not fit the <c>long</c> the
    /// optimizer's folders can carry, and there is no representation both correct for the declared
    /// type and safe for them — measured and still true, <c>IROptimizer.CompareLt</c> answers
    /// <b>false</b> for any CLR pair outside int/long/float/double.
    ///
    /// <para>⚠ A clean refusal is the improvement: before the fix this printed
    /// <b>4620580627691444634</b>.</para>
    /// </summary>
    [Test]
    public void AULongInitializer_IsRefusedRatherThanMisrepresented()
    {
        var ex = Assert.Throws<Exception>(() => JsTestSupport.BuildModule(
            Program("Dim G As ULong = 7.9", "PrintLine(CStr(G))")));

        Assert.Multiple(() =>
        {
            Assert.That(ex.Message, Does.Contain("ULong"));
            Assert.That(ex.Message, Does.Not.Contain("Object reference not set"));
        });
    }

    /// <summary>
    /// ⚠ The narrowed constant keeps an <c>int</c>/<c>long</c> CLR value while its
    /// <c>TypeInfo</c> carries the declared narrow type. STRUCTURAL because that split IS the
    /// safety argument: hand the optimizer's folders a <c>byte</c> and <c>CompareLt</c> silently
    /// answers false.
    ///
    /// <para>⛔ Also why UInteger takes an <c>int</c> when the value fits: a <c>long</c> makes the
    /// JavaScript backend refuse the program outright (BL7003), which measurably turned
    /// <c>Dim G As UInteger = 7.9</c> into a build failure there.</para>
    /// </summary>
    [Test]
    public void TheNarrowedConstant_KeepsAFolderSafeClrValue()
    {
        var module = JsTestSupport.BuildModule(Program("Dim G As Byte = 7.9", "PrintLine(CStr(G))"));
        var unsigned = JsTestSupport.BuildModule(
            Program("Dim G As UInteger = 7.9", "PrintLine(CStr(G))"));

        var constant = (BasicLang.Compiler.IR.IRConstant)module.GlobalVariables["G"].InitialValue;

        Assert.Multiple(() =>
        {
            Assert.That(constant.Value, Is.TypeOf<int>(), "a byte would miscompile in CompareLt");
            Assert.That(constant.Value, Is.EqualTo(8), "7.9 ROUNDS to 8; it used to truncate to 7");
            Assert.That(constant.Type.Name, Is.EqualTo("Byte"), "the declared width is carried");
            Assert.That(
                ((BasicLang.Compiler.IR.IRConstant)unsigned.GlobalVariables["G"].InitialValue).Value,
                Is.TypeOf<int>(), "a long here makes JavaScript refuse the program");
        });
    }

    /// <summary>
    /// ⚠ The boundary on the other side: <c>Dim G As Single = 7 / 2</c> is rejected by the
    /// SEMANTIC ANALYZER ("Cannot assign value of type 'Double' to variable of type 'Single'")
    /// before folding is ever consulted. Recorded so the widening fold is not later blamed for
    /// it, and so that a Single/Double narrowing rule change is seen to belong in the front end.
    /// </summary>
    [Test]
    public void ADoubleToSingleInitializer_IsAFrontEndDiagnostic_NotAFoldingGap()
    {
        var errors = OptionalConstructorTests.Analyze(
            Program("Dim G As Single = 7 / 2", "PrintLine(CStr(G))"));

        Assert.That(errors, Has.Some.Contains("Single"));
    }

    // ====================================================================================
    // Regression: the shapes that already worked must keep working.
    // ====================================================================================

    /// <summary>
    /// ⚠ Every module-scope shape that built BEFORE the change, re-asserted end to end. These take
    /// the "nothing was emitted" path — the expression was already a self-contained value — and a
    /// fix that routed them through folding instead would be free to change what they mean.
    /// Measured before the change: all seven ran and printed exactly these values.
    /// </summary>
    [Test]
    [Category("Integration")]
    [TestCase("Dim G As Integer = 42", "PrintLine(CStr(G))", "42", TestName = "Unchanged_Literal")]
    [TestCase("Dim G As Integer = -5", "PrintLine(CStr(G))", "-5", TestName = "Unchanged_Negative")]
    [TestCase("Dim G As String = \"hi\"", "PrintLine(G)", "hi", TestName = "Unchanged_StringLiteral")]
    [TestCase("Dim G As Boolean = True", "PrintLine(CStr(G))", "True", TestName = "Unchanged_BoolLiteral")]
    [TestCase("Dim G As Integer = (42)", "PrintLine(CStr(G))", "42", TestName = "Unchanged_Parenthesized")]
    [TestCase("Dim G As Integer", "PrintLine(CStr(G))", "0", TestName = "Unchanged_DeclarationOnly")]
    [TestCase("Const C As Integer = 42", "PrintLine(CStr(C))", "42", TestName = "Unchanged_ConstLiteral")]
    public void AShapeThatAlreadyBuilt_StillRunsTheSame(
        string declaration, string print, string expected)
    {
        Assert.That(Msil.MsilHarness.RunExpectingSuccess(Program(declaration, print)),
            Is.EqualTo(expected + "\n"));
    }

    /// <summary>
    /// ⚠ A module-scope global initialized from ANOTHER already-constant global. It built before
    /// the change and printed 7, so it must still — this is the shape whose value comes from
    /// lowering directly rather than from folding, and returning the folded list's last entry for
    /// it would be wrong.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void AGlobalInitializedFromAnotherGlobal_IsUnchanged()
    {
        var program = """
            Module M
             Dim H As Integer = 7
             Dim G As Integer = H
             Sub Main()
              PrintLine(CStr(G))
             End Sub
            End Module
            """;

        Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo("7\n"));
    }

    /// <summary>
    /// ⚠ A module-level sized array still allocates. It has no initializer at all, so it must not
    /// be routed anywhere near the folding path — left bare it is a null reference and the first
    /// <c>g(0) = …</c> throws.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void AModuleLevelSizedArray_StillAllocates()
    {
        var program = """
            Module M
             Dim G(5) As Integer
             Sub Main()
              G(0) = 3
              PrintLine(CStr(G(0)))
             End Sub
            End Module
            """;

        Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo("3\n"));
    }

    /// <summary>
    /// ⚠ <c>Concat</c> folding was added to <c>ConstantFoldingPass</c> to make
    /// <c>Dim S As String = "a" &amp; "b"</c> foldable, and that changes the OPTIMIZER for all
    /// code, not only module scope. This asserts the fold is CORRECT there, not that it is
    /// present: measured by mutation, removing the <c>Concat</c> arm leaves this test passing,
    /// because an unfolded concat is simply computed at run time and prints the same thing. What
    /// holds the arm's presence is <c>Fold_Concat</c> / <c>Fold_ConstConcat</c> above, where an
    /// unfolded concat is not a constant and the initializer gets refused.
    ///
    /// <para>⛔ The value of this test is the MIXED case. <c>"a" &amp; 5</c> must still produce
    /// "a5": <c>FoldAdd</c> matches string+string only and returns null otherwise, so it does not
    /// fold and is computed at run time rather than guessed at — which is what VB's coercion
    /// requires. A <c>Concat</c> arm that tried to stringify the operands itself would break this
    /// while leaving every module-scope test green.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    public void ConcatFolding_IsCorrectInsideAProcedureToo()
    {
        var program = """
            Module M
             Sub Main()
              Dim a As String = "a" & "b"
              Dim b As String = "a" & CStr(5)
              PrintLine(a)
              PrintLine(b)
             End Sub
            End Module
            """;

        Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo("ab\na5\n"));
    }
}
