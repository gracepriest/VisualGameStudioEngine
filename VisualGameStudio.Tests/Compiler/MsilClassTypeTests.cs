using System;
using NUnit.Framework;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A user-defined CLASS or INTERFACE used as a TYPE, on MSIL.
///
/// <para>⛔ MSIL COULD NOT COMPILE A PROGRAM THAT PASSES OBJECTS AROUND. Measured before the
/// change, with C#, JavaScript and C++ running every shape: a class-typed field stored through a
/// receiver, a function returning a class, a constructor taking one, a field typed as its own
/// class — each emitted a BARE class name in a type-SPEC position (<c>stfld Tag 'Box'::'Item'</c>)
/// and ilasm refused the whole file. 4 of 30 shapes ran.</para>
///
/// <para>⛔ TWO INDEPENDENT DEFECTS, and the controls separate them. (1) WRONG RENDERER: a spec
/// position rendered with <c>MapType</c> (bare) instead of <c>IlTypeSpec</c> (which adds the
/// <c>class</c> prefix a user type needs). Loud — it fails at ASSEMBLE time, and it needs no
/// inheritance at all. (2) WRONG SOURCE: the type taken from the VALUE at the site rather than
/// from the DECLARATION, so <c>Report(New Dog())</c> against <c>Report(a As Animal)</c> emitted a
/// call to a method nobody declared. Silent — it assembles and fails at RUN time with
/// MissingMethodException, and an exactly-typed argument hides it entirely.</para>
///
/// <para>⚠ EVERY CASE ASSERTS MSIL AGAINST C#, not against a literal alone: the property is "the
/// two .NET backends agree", which is what makes a wrong-but-consistent answer visible.</para>
///
/// <para>⚠ NOT COVERED HERE, each measured and each in a different family:
/// <c>For Each</c> over a collection (the loop variable is never declared and the body is emitted
/// twice — <c>List(Of String)</c> fails identically); <c>Dim x(n)</c> bounds (C# throws
/// IndexOutOfRange on the same program); a user <c>Delegate</c> (the FRONT END rejects it —
/// <c>AddressOf</c> yields 'Func', not the declared delegate type); and an interface PROPERTY,
/// which is broken on both .NET backends independently — C# emits an accessor-less property
/// (CS0548) and MSIL lowers the access to a FIELD load (MissingFieldException).</para>
///
/// <para>⭐ THE LAST FIVE CASES WERE WRITTEN BY MUTATION TESTING, not by inspection. A first
/// sweep of 20 discriminating mutants left 7 alive. Three were sites no accepted program
/// reaches. The other four were this fixture passing an argument whose type ALREADY EQUALLED
/// the declared parameter type — the exact blind spot the paragraph above warns about, and one
/// of them was hiding a real defect: <c>DeclaredCtorParams</c> read
/// <c>IRConstructor.Parameters</c>, which the IR builder never fills, so it always returned
/// null and EVERY constructor call silently fell back to spelling the argument types. Nulling
/// out a helper that already returns null changes nothing, which is why the mutant lived.
/// After the fixes and these cases: 21 killed, 2 alive, both unreachable.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class MsilClassTypeTests
{
    /// <summary>Both .NET backends compile AND RUN the program, and agree with each other.</summary>
    private static void MsilAgreesWithCSharp(string program, string expected)
    {
        var cs = FourBackends.Norm(FourBackends.RunEmittedCSharp(program));
        var msil = FourBackends.Norm(MsilHarness.RunExpectingSuccess(program));
        Assert.Multiple(() =>
        {
            Assert.That(cs, Is.EqualTo(expected), "C# (the reference .NET backend)");
            Assert.That(msil, Is.EqualTo(expected), "MSIL");
        });
    }

    /// <summary>field type (write). MEASURED MSIL: ILASM-FAIL "syntax error at token 'Tag' in: stfld Tag 'Box'::'Item'". D1+D2 at line 5031 -- MapType instead of IlTypeSpec, AN</summary>
    [Test]
    public void AClassTypedField_StoredThroughAReceiver()
        => MsilAgreesWithCSharp("Class Tag\n Public Text As String\nEnd Class\n\nClass Box\n Public Item As Tag\nEnd Class\n\nModule M\n Sub Main()\n  Dim t As New Tag()\n  t.Text = \"TAG\"\n  Dim b As New Box()\n  b.Item = t\n  PrintLine(b.Item.Text)\n End Sub\nEnd Module", "TAG");

    /// <summary>return type of a module-level Function. MEASURED MSIL: ILASM-FAIL "syntax error at token 'Animal' in: Animal 'Make'() cil managed". D1 at line 1911 (GenerateFun</summary>
    [Test]
    public void AFreeFunctionReturningAClass()
        => MsilAgreesWithCSharp("Class Animal\n Public Name As String\nEnd Class\n\nModule M\n Function Make() As Animal\n  Dim a As New Animal()\n  a.Name = \"CAT\"\n  Return a\n End Function\n Sub Main()\n  Dim a As Animal = Make()\n  PrintLine(a.Name)\n End Sub\nEnd Module", "CAT");

    /// <summary>return type of a class method -- a DIFFERENT emission site from the free-function case, so it is not redundant. MEASURED MSIL: ILASM-FAIL "syntax error at token</summary>
    [Test]
    public void AnInstanceMethodReturningAClass()
        => MsilAgreesWithCSharp("Class Part\n Public Code As Integer\nEnd Class\n\nClass Factory\n Public Function Build() As Part\n  Dim p As New Part()\n  p.Code = 7\n  Return p\n End Function\nEnd Class\n\nModule M\n Sub Main()\n  Dim f As New Factory()\n  Dim p As Part = f.Build()\n  PrintLine(CStr(p.Code))\n End Sub\nEnd Module", "7");

    /// <summary>parameter type -- CONTROL, passes on MSIL today. MEASURED MSIL: RUNS, prints D-X. The declaration renders the parameter with IlTypeSpec (line 1923) and the call</summary>
    [Test]
    public void AClassTypedParameter_GivenAnExactlyTypedArgument_Control()
        => MsilAgreesWithCSharp("Class Tag\n Public Text As String\nEnd Class\n\nModule M\n Function Describe(t As Tag) As String\n  Return \"D-\" & t.Text\n End Function\n Sub Main()\n  Dim t As New Tag()\n  t.Text = \"X\"\n  PrintLine(Describe(t))\n End Sub\nEnd Module", "D-X");

    /// <summary>parameter type, widening. The pure-D2 case and the quiet one -- it ASSEMBLES. MEASURED MSIL: RUN-FAIL "System.MissingMethodException: Method not found: 'System.</summary>
    [Test]
    public void ABaseTypedParameter_GivenADerivedArgument()
        => MsilAgreesWithCSharp("Class Animal\n Public Name As String\nEnd Class\n\nClass Dog\n Inherits Animal\nEnd Class\n\nModule M\n Function Report(a As Animal) As String\n  Return \"A-\" & a.Name\n End Function\n Sub Main()\n  Dim d As New Dog()\n  d.Name = \"REX\"\n  PrintLine(Report(d))\n End Sub\nEnd Module", "A-REX");

    /// <summary>parameter type in a .ctor signature at a newobj site. MEASURED MSIL: ILASM-FAIL "syntax error at token 'Tag' in: newobj instance void 'Holder'::.ctor(Tag)". D1+</summary>
    [Test]
    public void AConstructorTakingAClassTypedArgument()
        => MsilAgreesWithCSharp("Class Tag\n Public Text As String\nEnd Class\n\nClass Holder\n Public Item As Tag\n Public Sub New(t As Tag)\n  Item = t\n End Sub\nEnd Class\n\nModule M\n Sub Main()\n  Dim t As New Tag()\n  t.Text = \"HELD\"\n  Dim h As New Holder(t)\n  PrintLine(h.Item.Text)\n End Sub\nEnd Module", "HELD");

    /// <summary>parameter type in a MyBase.New signature -- a third, separate call-emission site. MEASURED MSIL: ILASM-FAIL "syntax error at token 'Tag' in: call instance void </summary>
    [Test]
    public void ABaseConstructorTakingAClassTypedArgument()
        => MsilAgreesWithCSharp("Class Tag\n Public Text As String\nEnd Class\n\nClass Base\n Public Item As Tag\n Public Sub New(t As Tag)\n  Item = t\n End Sub\nEnd Class\n\nClass Derived\n Inherits Base\n Public Sub New(t As Tag)\n  MyBase.New(t)\n End Sub\nEnd Class\n\nModule M\n Sub Main()\n  Dim t As New Tag()\n  t.Text = \"BASE-OK\"\n  Dim d As New Derived(t)\n  PrintLine(d.Item.Text)\n End Sub\nEnd Module", "BASE-OK");

    /// <summary>array element type -- CONTROL, passes on MSIL today, and the shape that guards against OVER-correcting. MEASURED MSIL: RUNS, prints 42. newarr/stelem go through</summary>
    [Test]
    public void AnArrayOfAClass_ElementsCrossIntoACall_Control()
        => MsilAgreesWithCSharp("Class Cell\n Public N As Integer\nEnd Class\n\nModule M\n Function Total(c As Cell) As Integer\n  Return c.N\n End Function\n Sub Main()\n  Dim a(2) As Cell\n  a(0) = New Cell()\n  a(0).N = 40\n  a(1) = New Cell()\n  a(1).N = 2\n  PrintLine(CStr(Total(a(0)) + Total(a(1))))\n End Sub\nEnd Module", "42");

    /// <summary>property type. MEASURED MSIL: ILASM-FAIL "syntax error at token 'Tag' in: .field private Tag '&amp;lt;Item&amp;gt;k__BackingField'". D1 at line 951 (propType = </summary>
    [Test]
    public void AClassTypedAutoProperty()
        => MsilAgreesWithCSharp("Class Tag\n Public Text As String\nEnd Class\n\nClass Box\n Public Property Item As Tag\nEnd Class\n\nModule M\n Sub Main()\n  Dim t As New Tag()\n  t.Text = \"PROP\"\n  Dim b As New Box()\n  b.Item = t\n  PrintLine(b.Item.Text)\n End Sub\nEnd Module", "PROP");

    /// <summary>local variable type -- CONTROL, passes on MSIL today. MEASURED MSIL: RUNS, prints LOCAL. The .locals init slot is already rendered with IlTypeSpec at line 2298,</summary>
    [Test]
    public void AClassTypedLocal_DeclaredWithoutAnInitializer_Control()
        => MsilAgreesWithCSharp("Class Tag\n Public Text As String\nEnd Class\n\nModule M\n Sub Main()\n  Dim t As Tag\n  t = New Tag()\n  t.Text = \"LOCAL\"\n  Dim u As Tag = t\n  PrintLine(u.Text)\n End Sub\nEnd Module", "LOCAL");

    /// <summary>CONTROL. Parameter spec position (class Animal) with an EXACTLY-typed argument. Declaration and call site agree by accident, so this passes today on MSIL — it p</summary>
    [Test]
    public void control_base_param_exact_arg()
        => MsilAgreesWithCSharp("Class Animal\n Public Name As String\nEnd Class\n\nModule M\n Sub Report(a As Animal)\n  PrintLine(a.Name)\n End Sub\n\n Sub Main()\n  Dim a As Animal = New Animal()\n  a.Name = \"ANIMAL\"\n  Report(a)\n End Sub\nEnd Module\n", "ANIMAL");

    /// <summary>D2 WRONG SOURCE at the free-function call site (line ~3474: paramTypes from call.Arguments). Declaration emits Report(class Animal); the call spells Report(clas</summary>
    [Test]
    public void derived_arg_to_base_param_free_sub()
        => MsilAgreesWithCSharp("Class Animal\n Public Function Kind() As String\n  Return \"ANIMAL\"\n End Function\nEnd Class\n\nClass Dog\n Inherits Animal\nEnd Class\n\nModule M\n Sub Report(a As Animal)\n  PrintLine(a.Kind())\n End Sub\n\n Sub Main()\n  Dim d As Dog = New Dog()\n  Report(d)\n End Sub\nEnd Module\n", "ANIMAL");

    /// <summary>D2 on the callvirt instance path (same argument-sourced paramTypes, receiver-qualified). Keeper::Describe is declared (class Animal) by GenerateClassMethod but </summary>
    [Test]
    public void derived_arg_to_base_param_instance_method()
        => MsilAgreesWithCSharp("Class Animal\n Public Tag As String\nEnd Class\n\nClass Dog\n Inherits Animal\nEnd Class\n\nClass Keeper\n Public Function Describe(a As Animal) As String\n  Return a.Tag\n End Function\nEnd Class\n\nModule M\n Sub Main()\n  Dim d As Dog = New Dog()\n  d.Tag = \"DOG\"\n  Dim k As Keeper = New Keeper()\n  PrintLine(k.Describe(d))\n End Sub\nEnd Module\n", "DOG");

    /// <summary>D1 + D2 together at the stfld (line ~5031: MapType(fieldStore.Value?.Type)). The field is declared Animal, the value is a Dog, so the store emits the VALUE's ty</summary>
    [Test]
    public void base_typed_field_holds_derived()
        => MsilAgreesWithCSharp("Class Animal\n Public Function Kind() As String\n  Return \"ANIMAL\"\n End Function\nEnd Class\n\nClass Dog\n Inherits Animal\nEnd Class\n\nClass Zoo\n Public Pet As Animal\nEnd Class\n\nModule M\n Sub Main()\n  Dim z As Zoo = New Zoo()\n  z.Pet = New Dog()\n  Dim a As Animal = z.Pet\n  PrintLine(a.Kind())\n End Sub\nEnd Module\n", "ANIMAL");

    /// <summary>Spec position in stfld/ldfld of a class-typed field, plus the callvirt token for an Overridable/Overrides pair. Distinguishes "the store assembled" from "the st</summary>
    [Test]
    public void virtual_dispatch_through_base_typed_field()
        => MsilAgreesWithCSharp("Class Animal\n Public Overridable Function Speak() As String\n  Return \"GENERIC\"\n End Function\nEnd Class\n\nClass Dog\n Inherits Animal\n Public Overrides Function Speak() As String\n  Return \"WOOF\"\n End Function\nEnd Class\n\nClass Zoo\n Public Pet As Animal\nEnd Class\n\nModule M\n Sub Main()\n  Dim z As Zoo = New Zoo()\n  z.Pet = New Dog()\n  Dim a As Animal = z.Pet\n  PrintLine(a.Speak())\n End Sub\nEnd Module\n", "WOOF");

    /// <summary>.locals entry of interface type (spec position) and callvirt on an interface token. This is the measured TypeLoadException "Method 'Speak' in type 'Dog' has no </summary>
    [Test]
    public void interface_typed_local_dispatch()
        => MsilAgreesWithCSharp("Interface ISpeaker\n Function Speak() As String\nEnd Interface\n\nClass Dog\n Implements ISpeaker\n Public Function Speak() As String\n  Return \"WOOF\"\n End Function\nEnd Class\n\nModule M\n Sub Main()\n  Dim s As ISpeaker = New Dog()\n  PrintLine(s.Speak())\n End Sub\nEnd Module\n", "WOOF");

    /// <summary>D1 + D2 on an interface-typed parameter. The declaration needs Announce(class ISpeaker); the argument-sourced call site spells Announce(class Dog), so even once</summary>
    [Test]
    public void class_arg_to_interface_param()
        => MsilAgreesWithCSharp("Interface ISpeaker\n Function Speak() As String\nEnd Interface\n\nClass Dog\n Implements ISpeaker\n Public Function Speak() As String\n  Return \"WOOF\"\n End Function\nEnd Class\n\nModule M\n Sub Announce(s As ISpeaker)\n  PrintLine(s.Speak())\n End Sub\n\n Sub Main()\n  Dim d As Dog = New Dog()\n  Announce(d)\n End Sub\nEnd Module\n", "WOOF");

    /// <summary>Field DECLARATION of interface type (.field ... class ICounter Slot) plus stfld/ldfld on it. The store's value is a Fixed, the field is declared ICounter, so th</summary>
    [Test]
    public void interface_typed_field_holds_class()
        => MsilAgreesWithCSharp("Interface ICounter\n Function Value() As Integer\nEnd Interface\n\nClass Fixed\n Implements ICounter\n Public Function Value() As Integer\n  Return 7\n End Function\nEnd Class\n\nClass Holder\n Public Slot As ICounter\nEnd Class\n\nModule M\n Sub Main()\n  Dim h As Holder = New Holder()\n  h.Slot = New Fixed()\n  Dim c As ICounter = h.Slot\n  PrintLine(c.Value())\n End Sub\nEnd Module\n", "7");

    /// <summary>D1 in RETURN-TYPE spec position (measured: Animal 'Make'() cil managed -&amp;gt; ilasm "syntax error at token 'Animal'"), plus D2 at the call site (line ~3473 r</summary>
    [Test]
    public void factory_returns_base_holding_derived()
        => MsilAgreesWithCSharp("Class Animal\n Public Overridable Function Speak() As String\n  Return \"GENERIC\"\n End Function\nEnd Class\n\nClass Dog\n Inherits Animal\n Public Overrides Function Speak() As String\n  Return \"WOOF\"\n End Function\nEnd Class\n\nModule M\n Function Make() As Animal\n  Return New Dog()\n End Function\n\n Sub Main()\n  Dim a As Animal = Make()\n  PrintLine(a.Speak())\n End Sub\nEnd Module\n", "WOOF");

    /// <summary>D1 in the newobj/.ctor signature and in MyBase.New (measured: call instance void 'Base'::.ctor(Tag) -&amp;gt; ilasm rejects), plus a class-typed field stored in</summary>
    [Test]
    public void base_ctor_takes_class_typed_arg()
        => MsilAgreesWithCSharp("Class Tag\n Public Text As String\nEnd Class\n\nClass Base\n Public Label As Tag\n Public Sub New(t As Tag)\n  Label = t\n End Sub\nEnd Class\n\nClass Derived\n Inherits Base\n Public Sub New(t As Tag)\n  MyBase.New(t)\n End Sub\nEnd Class\n\nModule M\n Sub Main()\n  Dim t As Tag = New Tag()\n  t.Text = \"TAGGED\"\n  Dim d As Derived = New Derived(t)\n  Dim b As Base = d\n  Dim g As Tag = b.Label\n  PrintLine(g.Text)\n End Sub\nEnd Module\n", "TAGGED");

    /// <summary>Field declaration spec (line ~820 IlTypeSpec — OK) vs receiver stfld at line ~5031, which derives the type from fieldStore.Value?.Type via MapType. D1 (no "clas</summary>
    [Test]
    public void self_typed_field()
        => MsilAgreesWithCSharp("Class Node\n Public Label As String\n Public Next2 As Node\nEnd Class\n\nSub Main()\n Dim a As New Node()\n a.Label = \"head\"\n Dim b As New Node()\n b.Label = \"tail\"\n a.Next2 = b\n PrintLine(a.Label & \"->\" & a.Next2.Label)\nEnd Sub", "head->tail");

    /// <summary>Field declaration spec at line ~820 for a class named BEFORE it is declared (Pet.Keeper As Owner), plus two receiver stfld stores at line ~5031 in both directio</summary>
    [Test]
    public void mutual_reference_forward_declared()
        => MsilAgreesWithCSharp("Class Pet\n Public Name As String\n Public Keeper As Owner\nEnd Class\n\nClass Owner\n Public Name As String\n Public Beast As Pet\nEnd Class\n\nSub Main()\n Dim p As New Pet()\n p.Name = \"Rex\"\n Dim o As New Owner()\n o.Name = \"Ann\"\n o.Beast = p\n p.Keeper = o\n PrintLine(o.Beast.Name & \"/\" & p.Keeper.Name)\nEnd Sub", "Rex/Ann");

    /// <summary>GenerateClassMethod at line ~1278 renders the signature return type with MapType(method.ReturnType), giving "static Dog 'Dog'::'Make'(string)" — D1, ilasm rejec</summary>
    [Test]
    public void shared_method_returns_class()
        => MsilAgreesWithCSharp("Class Dog\n Public Name As String\n Shared Function Make(n As String) As Dog\n  Dim d As New Dog()\n  d.Name = n\n  Return d\n End Function\nEnd Class\n\nSub Main()\n Dim d As Dog = Dog.Make(\"Rex\")\n PrintLine(d.Name)\nEnd Sub", "Rex");

    /// <summary>GenerateFunction at line ~1911 renders the method signature return type with MapType(function.ReturnType) — the measured "Animal 'Make'() cil managed" rejection</summary>
    [Test]
    public void free_function_returns_class()
        => MsilAgreesWithCSharp("Class Animal\n Public Name As String\nEnd Class\n\nFunction Make(n As String) As Animal\n Dim a As New Animal()\n a.Name = n\n Return a\nEnd Function\n\nSub Main()\n Dim a As Animal = Make(\"Cat\")\n PrintLine(a.Name)\nEnd Sub", "Cat");

    /// <summary>IRNewObject at line ~4709 builds the ctor signature as newObj.Arguments.Select(a =&amp;gt; MapType(a.Type)) — D1 (no "class " prefix) AND D2 (argument-sourced r</summary>
    [Test]
    public void class_passed_to_constructor()
        => MsilAgreesWithCSharp("Class Tag\n Public Text As String\nEnd Class\n\nClass Box\n Public Item As Tag\n Sub New(t As Tag)\n  Item = t\n End Sub\nEnd Class\n\nSub Main()\n Dim t As New Tag()\n t.Text = \"gift\"\n Dim b As New Box(t)\n PrintLine(b.Item.Text)\n Dim u As New Tag()\n u.Text = \"swap\"\n b.Item = u\n PrintLine(b.Item.Text)\nEnd Sub", "gift\nswap");

    /// <summary>EmitBaseCtorCall at line ~1246 renders args.Select(a =&amp;gt; MapType(a.Type)), producing the measured "call instance void 'Base'::.ctor(Tag)" rejection — D1 +</summary>
    [Test]
    public void base_ctor_takes_class_argument()
        => MsilAgreesWithCSharp("Class Tag\n Public Text As String\nEnd Class\n\nClass Base\n Public Held As Tag\n Sub New(t As Tag)\n  Held = t\n End Sub\nEnd Class\n\nClass Derived\n Inherits Base\n Public Extra As String\n Sub New(t As Tag)\n  MyBase.New(t)\n  Extra = \"d\"\n End Sub\nEnd Class\n\nSub Main()\n Dim t As New Tag()\n t.Text = \"core\"\n Dim d As New Derived(t)\n PrintLine(d.Held.Text & \"/\" & d.Extra)\nEnd Sub", "core/d");

    /// <summary>Pure D2, and the only shape here that ASSEMBLES and then dies at run time. The free-function call at line ~3474 spells paramTypes from call.Arguments, so passin</summary>
    [Test]
    public void base_typed_parameter_derived_argument()
        => MsilAgreesWithCSharp("Class Animal\n Public Name As String\nEnd Class\n\nClass Dog\n Inherits Animal\n Public Trick As String\nEnd Class\n\nSub Report(a As Animal)\n PrintLine(\"animal=\" & a.Name)\nEnd Sub\n\nSub Main()\n Dim d As New Dog()\n d.Name = \"Rex\"\n d.Trick = \"sit\"\n Report(d)\n PrintLine(\"trick=\" & d.Trick)\nEnd Sub", "animal=Rex\ntrick=sit");

    /// <summary>Interface member declaration at line ~731 uses MapType(method.ReturnType) (D1 once a member returns a class), the interface-typed local is a .locals spec positi</summary>
    [Test]
    public void interface_typed_variable_dispatch()
        => MsilAgreesWithCSharp("Interface ISpeaker\n Function Speak() As String\nEnd Interface\n\nClass Dog\n Implements ISpeaker\n Public Name As String\n Function Speak() As String Implements ISpeaker.Speak\n  Return Name & \" says woof\"\n End Function\nEnd Class\n\nSub Main()\n Dim d As New Dog()\n d.Name = \"Rex\"\n Dim s As ISpeaker\n s = d\n PrintLine(s.Speak())\nEnd Sub", "Rex says woof");

    /// <summary>
    /// ⭐ ADDED BY MUTATION: `d1-900-iface-method-return` SURVIVED the first sweep. Every other
    /// interface here returns String or Integer, for which MapType and IlTypeSpec render the SAME
    /// text — only a member returning a USER CLASS tells the two apart. ⚠ The CType is not
    /// decoration: the front end types every interface call Object and then refuses Object → Dog,
    /// naming CType in the diagnostic.
    /// </summary>
    [Test]
    public void an_interface_method_returning_a_class()
        => MsilAgreesWithCSharp("Interface IFactory\n Function Make() As Dog\nEnd Interface\n\nClass Dog\n Public Name As String\nEnd Class\n\nClass Kennel\n Implements IFactory\n Public Function Make() As Dog\n  Dim d As Dog = New Dog()\n  d.Name = \"Rex\"\n  Return d\n End Function\nEnd Class\n\nModule M\n Sub Main()\n  Dim f As IFactory = New Kennel()\n  Dim made As Dog = CType(f.Make(), Dog)\n  PrintLine(made.Name)\n End Sub\nEnd Module", "Rex");

    /// <summary>
    /// ⭐ ADDED BY MUTATION: `d2-1415-base-ctor-args` SURVIVED. The base-ctor tests all passed an
    /// EXACTLY-TYPED argument, where spelling the argument and spelling the declaration agree —
    /// the blind spot this file's own header warns about. Here the base ctor declares Animal and
    /// the derived class hands it a Dog. ⚠ A `New` expression as the base-ctor argument instead
    /// of a variable hits a SEPARATE C# backend bug (undefined temp `t0`), so it is a variable.
    /// </summary>
    [Test]
    public void base_ctor_declared_base_given_derived()
        => MsilAgreesWithCSharp("Class Animal\n Public Name As String\nEnd Class\n\nClass Dog\n Inherits Animal\nEnd Class\n\nClass Shelter\n Public Resident As Animal\n Public Sub New(a As Animal)\n  Resident = a\n End Sub\nEnd Class\n\nClass DogShelter\n Inherits Shelter\n Public Sub New(d As Dog)\n  MyBase.New(d)\n End Sub\nEnd Class\n\nModule M\n Sub Main()\n  Dim pup As Dog = New Dog()\n  pup.Name = \"Rex\"\n  Dim s As DogShelter = New DogShelter(pup)\n  PrintLine(s.Resident.Name)\n End Sub\nEnd Module", "Rex");

    /// <summary>
    /// ⭐ ADDED BY MUTATION: `d2-3651-self-call-args` SURVIVED while its sibling arm on the SAME
    /// LINE (the free-function call) died to 5 tests — splitting one ternary into two mutants is
    /// what exposed it. A bare-name call to a sibling method from INSIDE the class, with a derived
    /// argument against a base-typed parameter.
    /// </summary>
    [Test]
    public void self_call_declared_base_given_derived()
        => MsilAgreesWithCSharp("Class Animal\n Public Name As String\nEnd Class\n\nClass Dog\n Inherits Animal\nEnd Class\n\nClass Keeper\n Public Sub Feed(a As Animal)\n  PrintLine(\"fed \" & a.Name)\n End Sub\n\n Public Sub Run()\n  Dim d As Dog = New Dog()\n  d.Name = \"Rex\"\n  Feed(d)\n End Sub\nEnd Class\n\nModule M\n Sub Main()\n  Dim k As Keeper = New Keeper()\n  k.Run()\n End Sub\nEnd Module", "fed Rex");

    /// <summary>
    /// ⭐ ADDED BY MUTATION: `d2-4888-newobj-args` SURVIVED — and it was an EQUIVALENT mutant over
    /// BROKEN code. DeclaredCtorParams read IRConstructor.Parameters, which the IR builder never
    /// fills, so it always returned null and every constructor call silently fell back to spelling
    /// the ARGUMENT types. Measured before the fix:
    /// `newobj instance void 'Shelter'::.ctor(class 'Dog')` against `.ctor(class 'Animal')` —
    /// MissingMethodException. Nulling out a helper that already returned null changes nothing,
    /// which is exactly why the mutant lived.
    /// </summary>
    [Test]
    public void newobj_declared_base_given_derived()
        => MsilAgreesWithCSharp("Class Animal\n Public Name As String\nEnd Class\n\nClass Dog\n Inherits Animal\nEnd Class\n\nClass Shelter\n Public Resident As Animal\n Public Sub New(a As Animal)\n  Resident = a\n End Sub\nEnd Class\n\nModule M\n Sub Main()\n  Dim d As Dog = New Dog()\n  d.Name = \"Rex\"\n  Dim s As Shelter = New Shelter(d)\n  PrintLine(s.Resident.Name)\n End Sub\nEnd Module", "Rex");

    /// <summary>
    /// ⭐ ADDED BY MUTATION: `box-guard-ilprimitives` SURVIVED, and only a VOID-returning interface
    /// member could kill it — for string the mutant emits a `box` on a reference type, which
    /// ECMA-335 III.4.1 makes a no-op, so it is genuinely unobservable there. A Sub also exposed a
    /// real defect: the front end types an interface call Object whatever the member returns, so a
    /// store was emitted after a call that pushes nothing (`callvirt instance void ...` then
    /// `stloc.2`) — InvalidProgramException.
    /// </summary>
    [Test]
    public void an_interface_sub_returns_nothing_to_store()
        => MsilAgreesWithCSharp("Interface ISpeaker\n Sub Speak()\nEnd Interface\n\nClass Dog\n Implements ISpeaker\n Public Sub Speak()\n  PrintLine(\"WOOF\")\n End Sub\nEnd Class\n\nModule M\n Sub Main()\n  Dim s As ISpeaker = New Dog()\n  s.Speak()\n End Sub\nEnd Module", "WOOF");
}
