using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task #188 (fix commit <c>5e82a786</c>) — front end / IR. <c>SemanticAnalyzer.DelegateMemberCallee</c>
/// (read through its public face, <c>IsDelegateMemberInvocation</c>) decides whether a call's callee
/// is a delegate-typed FIELD or PROPERTY the call's own class (or a base, or the receiver) owns;
/// <c>IRBuilder.EmitDelegateValueInvocation</c> is the ONE lowering every TRUE case shares. No
/// process is spawned here — see <c>DelegateMemberInvocationExecutionTests</c> for the four-backend
/// runs; this fixture is pure front-end/IR/codegen-TEXT and carries no <c>[Category("Integration")]</c>
/// on purpose (CLAUDE.md's fast subset must cover it).
///
/// <para>Every IR shape asserted below was MEASURED against this exact working tree (a disposable
/// console harness printing <c>IRCall.CalleeValue</c>/<c>Name</c>/<c>FunctionName</c> for each probe)
/// before being pinned, matching the convention <c>BarePropertyLoweringStructuralIrTests</c>
/// established for ADR-0007. The codegen-text assertions were cross-checked the same way against
/// the CLI's own <c>--target=cpp</c>/<c>--target=javascript</c> output.</para>
/// </summary>
[TestFixture]
public class DelegateMemberInvocationTests
{
    // ============================================================================================
    // 0. Helpers.
    // ============================================================================================

    private static IRFunction Fn(IRModule module, string name) =>
        module.Functions.First(f => f.Name == name);

    private static List<IRCall> CallsIn(IRModule module, string functionName) =>
        Fn(module, functionName).Blocks.SelectMany(b => b.Instructions).OfType<IRCall>().ToList();

    private static string Cpp(string source) =>
        new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false })
            .Generate(JsTestSupport.BuildModule(source));

    // ============================================================================================
    // 1. TRUE — every spelling, own class or a base, Shared, Action/Func/a user Delegate.
    //    Decided by DelegateMemberCallee, observed as IRCall.CalleeValue != null (a field load —
    //    an IRVariable for a bare non-accessor member, per ADR-0007 — or an IRFieldAccess for a
    //    qualified one or an accessor-backed property).
    // ============================================================================================

    [Test]
    public void Bare_InsideOwnClass_IsDelegateMemberInvocation()
    {
        var m = JsTestSupport.BuildModule("""
            Class Holder
                Public Callback As Action
                Public Sub Fire()
                    Callback()
                End Sub
            End Class
            Sub Main()
            End Sub
            """);
        var call = CallsIn(m, "Fire").Single();
        Assert.That(call.CalleeValue, Is.Not.Null,
            "bare Callback() inside its own class must invoke the FIELD'S VALUE, not a name");
        Assert.That(call.Name, Is.Null,
            "a Sub-shaped delegate call must have NO result destination (the C++ 'assigning to "
            + "void *' defect was exactly a destination on a call like this one)");
    }

    [Test]
    public void MeQualified_IsDelegateMemberInvocation()
    {
        var m = JsTestSupport.BuildModule("""
            Class Holder
                Public Callback As Action
                Public Sub Fire()
                    Me.Callback()
                End Sub
            End Class
            Sub Main()
            End Sub
            """);
        var call = CallsIn(m, "Fire").Single();
        Assert.That(call.CalleeValue, Is.InstanceOf<IRFieldAccess>(),
            "Me.Callback() must read the field (IRFieldAccess) and invoke that value");
        Assert.That(call.Name, Is.Null);
    }

    [Test]
    public void ObjQualified_FromOutsideClass_IsDelegateMemberInvocation()
    {
        var m = JsTestSupport.BuildModule("""
            Class Holder
                Public Callback As Action
            End Class
            Sub Main()
                Dim h As New Holder()
                h.Callback()
            End Sub
            """);
        var call = CallsIn(m, "Main").Single();
        Assert.That(call.CalleeValue, Is.InstanceOf<IRFieldAccess>(),
            "h.Callback() from OUTSIDE the class must read the field through the receiver and " +
            "invoke that value, not call a method named Callback");
        Assert.That(call.Name, Is.Null);
    }

    [Test]
    public void MyBaseQualified_IsDelegateMemberInvocation()
    {
        var m = JsTestSupport.BuildModule("""
            Class BaseH
                Public Callback As Action
            End Class
            Class Derived
                Inherits BaseH
                Public Sub FireMyBase()
                    MyBase.Callback()
                End Sub
            End Class
            Sub Main()
            End Sub
            """);
        var call = CallsIn(m, "FireMyBase").Single();
        Assert.That(call.CalleeValue, Is.InstanceOf<IRFieldAccess>());
        Assert.That(call.Name, Is.Null);
    }

    [Test]
    public void Inherited_BareInSubclass_IsDelegateMemberInvocation()
    {
        // The base's field, called BARE from the DERIVED class — DelegateMemberCallee's "the
        // current class OR A BASE" clause, with no MyBase/Me qualifier at all.
        var m = JsTestSupport.BuildModule("""
            Class BaseH
                Public Callback As Action
            End Class
            Class Derived
                Inherits BaseH
                Public Sub FireBare()
                    Callback()
                End Sub
            End Class
            Sub Main()
            End Sub
            """);
        var call = CallsIn(m, "FireBare").Single();
        Assert.That(call.CalleeValue, Is.Not.Null,
            "an INHERITED field, called bare from the subclass, must still invoke its value");
        Assert.That(call.Name, Is.Null);
    }

    [Test]
    public void Shared_BareInsideSharedSub_AndClassQualified_AreBothDelegateMemberInvocations()
    {
        var m = JsTestSupport.BuildModule("""
            Class Registry
                Public Shared Hook As Action(Of Integer)
                Public Shared Sub Trigger(n As Integer)
                    Hook(n)
                    Registry.Hook(n + 1)
                End Sub
            End Class
            Sub Main()
            End Sub
            """);
        var calls = CallsIn(m, "Trigger");
        Assert.That(calls, Has.Count.EqualTo(2));
        Assert.That(calls[0].CalleeValue, Is.Not.Null, "bare Hook(n) inside the Shared Sub");
        Assert.That(calls[0].Name, Is.Null);
        Assert.That(calls[1].CalleeValue, Is.InstanceOf<IRFieldAccess>(), "Registry.Hook(n + 1)");
        Assert.That(calls[1].Name, Is.Null);
    }

    [Test]
    public void FuncField_IsDelegateMemberInvocation_TypedAsItsResult()
    {
        var m = JsTestSupport.BuildModule("""
            Class Calc
                Public Op As Func(Of Integer, Integer)
                Public Function Twice(n As Integer) As Integer
                    Return Op(n)
                End Function
            End Class
            Sub Main()
            End Sub
            """);
        var call = CallsIn(m, "Twice").Single();
        Assert.That(call.CalleeValue, Is.Not.Null);
        Assert.That(call.Name, Is.Not.Null, "a Func-shaped delegate call DOES have a result destination");
        Assert.That(call.Type?.Name, Is.EqualTo("Integer"), "typed as R, the delegate's return type");
    }

    [Test]
    public void UserDelegateField_IsDelegateMemberInvocation()
    {
        var m = JsTestSupport.BuildModule("""
            Delegate Sub Notify2(msg As String)
            Class Holder
                Public Handler As Notify2
                Public Sub Fire()
                    Handler("x")
                End Sub
            End Class
            Sub Main()
            End Sub
            """);
        var call = CallsIn(m, "Fire").Single();
        Assert.That(call.CalleeValue, Is.Not.Null,
            "a field of a user Delegate TYPE must invoke as a value exactly like Action/Func");
        Assert.That(call.Name, Is.Null);
    }

    [Test]
    public void AccessorBackedProperty_Bare_IsDelegateMemberInvocation_ViaTheReadPath()
    {
        // ADR-0007: a bare name bound to an ACCESSOR-backed property lowers to EXACTLY the node
        // its qualified Me. form produces (IRFieldAccess with a Me receiver) — kept, not bypassed,
        // by #188's lowering.
        var m = JsTestSupport.BuildModule("""
            Class Holder
                Private _cb As Action
                Public Property CB As Action
                    Get
                        Return _cb
                    End Get
                    Set(value As Action)
                        _cb = value
                    End Set
                End Property
                Public Sub Fire()
                    CB()
                End Sub
            End Class
            Sub Main()
            End Sub
            """);
        var call = CallsIn(m, "Fire").Single();
        Assert.That(call.CalleeValue, Is.InstanceOf<IRFieldAccess>(),
            "a bare accessor-backed property must read via IRFieldAccess (ADR-0007), then invoke it");
        Assert.That(call.Name, Is.Null);
    }

    // ============================================================================================
    // 2. FALSE — a method, a local, a parameter, a module variable, a module Sub of the same name.
    // ============================================================================================

    [Test]
    public void MethodOfADifferentName_IsNotDelegateMemberInvocation()
    {
        // G3's shape: a field (Op) and a method (Apply) coexist under DIFFERENT names — the
        // method call must take its own plain-procedure path, never the field's.
        var m = JsTestSupport.BuildModule("""
            Class Calc
                Public Op As Func(Of Integer, Integer)
                Public Function Apply(n As Integer) As Integer
                    Return n * 100
                End Function
                Public Function Both(n As Integer) As Integer
                    Return Apply(n) + Op(n)
                End Function
            End Class
            Sub Main()
            End Sub
            """);
        var calls = CallsIn(m, "Both");
        Assert.That(calls, Has.Count.EqualTo(2));
        Assert.That(calls[0].CalleeValue, Is.Null, "Apply(n) is a METHOD call, not a delegate member");
        Assert.That(calls[0].FunctionName, Is.EqualTo("Apply"));
        Assert.That(calls[1].CalleeValue, Is.Not.Null, "Op(n) is still the delegate field, right beside it");
    }

    [Test]
    public void LocalShadowingTheField_IsNotDelegateMemberInvocation()
    {
        var m = JsTestSupport.BuildModule("""
            Class Holder
                Public Callback As Action
                Public Sub Fire()
                    Dim Callback As Action = Sub() Console.WriteLine("local")
                    Callback()
                    Me.Callback()
                End Sub
            End Class
            Sub Main()
            End Sub
            """);
        var calls = CallsIn(m, "Fire");
        Assert.That(calls, Has.Count.EqualTo(2));
        Assert.That(calls[0].CalleeValue, Is.Null,
            "bare Callback() must bind to the LOCAL that shadows the field, kept on its own path");
        Assert.That(calls[1].CalleeValue, Is.Not.Null,
            "an explicit Me.Callback() still reaches the field despite the local shadow");
    }

    [Test]
    public void ParameterShadowingTheField_IsNotDelegateMemberInvocation()
    {
        var m = JsTestSupport.BuildModule("""
            Class Holder
                Public Callback As Action
                Public Sub FireParam(Callback As Action)
                    Callback()
                End Sub
            End Class
            Sub Main()
            End Sub
            """);
        var call = CallsIn(m, "FireParam").Single();
        Assert.That(call.CalleeValue, Is.Null,
            "a PARAMETER of the same name must shadow the field exactly like a local");
        Assert.That(call.FunctionName, Is.EqualTo("Callback"));
    }

    [Test]
    public void ModuleVariable_SameNameAsField_OutsideAnyClass_IsNotDelegateMemberInvocation()
    {
        var m = JsTestSupport.BuildModule("""
            Dim Notify As Action
            Class Holder
                Public Notify As Action
                Public Sub Fire()
                    Notify()
                End Sub
            End Class
            Sub Main()
                Notify()
            End Sub
            """);
        // Inside the class, bare Notify() is the FIELD.
        var inClass = CallsIn(m, "Fire").Single();
        Assert.That(inClass.CalleeValue, Is.Not.Null, "inside Holder, Notify() is the class's own field");

        // At Main's module scope (no enclosing class), bare Notify() is the MODULE VARIABLE —
        // DelegateMemberCallee's owner (the current class scope) is null there, so it is never
        // admitted, and the call keeps the plain-procedure path a Variable symbol takes outside a
        // class body.
        var inMain = CallsIn(m, "Main").Single();
        Assert.That(inMain.CalleeValue, Is.Null,
            "at module scope Notify() must bind to the MODULE variable, not any class's field");
    }

    [Test]
    public void ModuleSub_SameNameAsField_G13()
    {
        var m = JsTestSupport.BuildModule("""
            Sub Notify()
                Console.WriteLine("module sub")
            End Sub
            Class Holder
                Public Notify As Action
                Public Sub Fire()
                    Notify()
                End Sub
            End Class
            Sub Main()
                Dim h As New Holder()
                h.Notify()
                Notify()
            End Sub
            """);
        // Inside Holder, bare Notify() is the FIELD despite a module Sub of the same name.
        var inClass = CallsIn(m, "Fire").Single();
        Assert.That(inClass.CalleeValue, Is.Not.Null,
            "inside the class, the FIELD wins over a same-named module Sub");

        var inMain = CallsIn(m, "Main");
        Assert.That(inMain, Has.Count.EqualTo(2));
        // h.Notify() — qualified through a Holder receiver — is still the field.
        Assert.That(inMain[0].CalleeValue, Is.Not.Null, "h.Notify() is the receiver's field");
        // Bare Notify() at Main's own module scope — no class receiver at all — is the module Sub.
        Assert.That(inMain[1].CalleeValue, Is.Null,
            "bare Notify() with no enclosing class must call the MODULE SUB, never a field");
        Assert.That(inMain[1].FunctionName, Is.EqualTo("Notify"));
    }

    // ============================================================================================
    // 3. The lowering survives the STANDARD optimizer pipeline too — not only the raw front end.
    // ============================================================================================

    [Test]
    public void CalleeValueSurvivesTheStandardOptimizerPipeline()
    {
        var m = JsTestSupport.BuildModule("""
            Class Holder
                Public Callback As Action
                Public Sub Fire()
                    Callback()
                End Sub
            End Class
            Sub Main()
            End Sub
            """);
        var pipeline = new OptimizationPipeline();
        pipeline.AddStandardPasses();
        pipeline.Run(m);

        var call = Fn(m, "Fire").Blocks.SelectMany(b => b.Instructions).OfType<IRCall>().Single();
        Assert.That(call.CalleeValue, Is.Not.Null,
            "the optimizer must not strip or replace CalleeValue on a delegate-member call");
        Assert.That(call.Name, Is.Null);
    }

    // ============================================================================================
    // 4. Codegen TEXT — C++ and JavaScript render the callee VALUE, never a bare member name.
    // ============================================================================================

    [Test]
    public void Cpp_QualifiedMemberCall_ReadsTheFieldIntoATemp_ThenInvokesTheTemp()
    {
        // F2's shape. Before #188 this rendered `t0 = this->Callback()` — a METHOD call. After,
        // the field is READ into a temp and that temp is INVOKED — two statements, never a direct
        // `this->Callback(...)` call syntax.
        var cpp = Cpp("""
            Class Holder
                Public Callback As Action
                Public Sub Fire()
                    Me.Callback()
                End Sub
            End Class
            Sub Main()
            End Sub
            """);
        Assert.That(cpp, Does.Not.Contain("this->Callback()"),
            "must never call the field as a method through ->");
        Assert.That(cpp, Does.Match(@"t\d+\s*=\s*this->Callback;"),
            "must read the field's VALUE into a temp");
        Assert.That(cpp, Does.Match(@"t\d+\(\);"),
            "then invoke that temp — the VALUE — not the name Callback");
    }

    [Test]
    public void Cpp_BareVoidCall_HasNoResultDestination()
    {
        // F1's shape — the exact defect: "t0 = Callback();" is assigning to 'void *' from 'void'.
        var cpp = Cpp("""
            Class Holder
                Public Callback As Action
                Public Sub Fire()
                    Callback()
                End Sub
            End Class
            Sub Main()
            End Sub
            """);
        Assert.That(cpp, Does.Not.Match(@"=\s*Callback\(\);"),
            "a Sub-shaped delegate call must never be given a result destination");
        Assert.That(cpp, Does.Contain("Callback();"));
    }

    [Test]
    public void Js_QualifiedMemberCall_ReadsTheFieldWithThis_ThenInvokesTheValue()
    {
        var js = JsTestSupport.Compile("""
            Class Holder
                Public Callback As Action
                Public Sub Fire()
                    Me.Callback()
                End Sub
            End Class
            Sub Main()
            End Sub
            """);
        Assert.That(js, Does.Not.Contain("Callback()"),
            "must never call the field by its bare NAME — JavaScript has no implicit receiver");
        Assert.That(js, Does.Match(@"t\d+\s*=\s*this\.Callback;"),
            "must read this.Callback into a value first");
        Assert.That(js, Does.Match(@"t\d+\(\);"));
    }

    [Test]
    public void Js_ParenthesisesANonNameCallee_L1()
    {
        // L1 — an immediately-invoked lambda, (Function(x) x * 2)(5): the callee is an inline
        // arrow function, not a name/member chain, so it must be wrapped in parens or the call
        // would not even parse as JavaScript.
        var js = JsTestSupport.Compile("""
            Function Adder(n As Integer) As Func(Of Integer, Integer)
                Return Function(x As Integer) x + n
            End Function

            Sub Main()
                Console.WriteLine((Function(x As Integer) x * 2)(5))
                Console.WriteLine(Adder(3)(4))
            End Sub
            """);
        Assert.That(js, Does.Match(@"\}\)\(5\)"),
            "the inline lambda callee must be parenthesised before being invoked with (5)");
        // Adder(3)(4): Adder(3) is bound to a plain temp NAME first, so the outer call needs no
        // parens around that name.
        Assert.That(js, Does.Not.Match(@"\(Adder\(3\)\)\(4\)"),
            "a plain name/member-chain callee must NOT be parenthesised");
    }
}
