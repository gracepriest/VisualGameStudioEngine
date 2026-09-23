using NUnit.Framework;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// An indexed WRITE (<c>l(i) = v</c>, <c>d(k) = v</c>, and the explicit <c>l.Item(i) = v</c>
/// spelling) on a <c>List</c> or <c>Dictionary</c>, on MSIL.
///
/// <para>⛔ <c>MSILCodeGenerator</c> had NO <see cref="System.Void"/>-returning
/// <c>Visit(IRIndexerStore)</c> override. <c>CodeGeneratorBase</c>'s own is a <c>virtual { }</c>
/// no-op — one of only two on that base type, the other being <c>IRThrow</c> before it was fixed
/// — so every indexed write emitted NOTHING AT ALL: not the call, not the indices, not even the
/// evaluation of the value. The program still ASSEMBLED and RAN, because dropping an instruction
/// is not invalid IL, it is just the wrong IL. Measured: <c>l(0) = 42</c> on a
/// <c>List(Of Integer)</c> printed the OLD element (<c>1</c>), and the same write on a
/// <c>Dictionary</c> turned into a <c>KeyNotFoundException</c> at the next read of that key. An
/// array element write (<c>a(i) = v</c>) was never affected — that lowers to the unrelated,
/// <c>abstract</c> <c>IRArrayStore</c>.</para>
///
/// <para><b>The fix.</b> An indexed write is an ordinary instance call to the RECEIVER's own
/// <c>set_Item</c>, in IL's required generic-DEFINITION spelling
/// (<c>List`1&lt;T&gt;::set_Item(int32, !0)</c>, <c>Dictionary`2&lt;K,V&gt;::set_Item(!0, !1)</c>)
/// — the same <c>CollectionMembers</c> table the read side (<c>get_Item</c>) already used, so the
/// two halves of one indexer cannot disagree about the receiver's type. Operand order is
/// receiver, then every index, then the value — nothing is swapped or parked. Calling the
/// accessor Dictionary itself declares makes insert-or-update fall out for free (<c>set_Item</c>
/// adds an absent key; only <c>Add</c> would throw), and a collection member outside the table
/// (a <c>HashSet</c>, a <c>Queue</c>) is still refused with the ordinary
/// <c>ForeignFeatureException</c> rather than guessed — though nothing here exercises that
/// refusal directly: <c>IsIndexableGenericType</c> only ever routes <c>List</c>/<c>Dictionary</c>
/// (plus <c>IList</c>/<c>IReadOnlyList</c>) into <c>IRIndexerStore</c> in the first place, and
/// both already have full table rows, so the front end never hands this code a collection type
/// that could reach the refusal.</para>
///
/// <para>⚠ Most cases assert MSIL against C# (<c>MsilAgreesWithCSharp</c>) — the pattern
/// <c>MsilForEachTests</c> uses. <b>Three cannot</b>: the C# backend's own indexer-store lowering
/// allocates a temp that does not exist in the surrounding scope for a READ-MODIFY-WRITE through an
/// indexer (<c>l(0) = l(1)</c>, <c>l(i) = l(i) * 10</c>, <c>d("a") = d("a") + 1</c>) — those fail to
/// COMPILE on C# with <c>CS0103: The name 'tN' does not exist in the current context</c>, a
/// separate, pre-existing C#-backend defect, not this family's. Those cases assert MSIL against
/// JavaScript instead (<c>MsilMatchesJs</c>), each naming the C# divergence in its own docstring.</para>
///
/// <para>⛔ <b>The predicate is an OPERAND THAT IS A TEMP — not "inside a loop", and not
/// "read-modify-write" either.</b> This note has been wrong twice, each time by generalizing from
/// whichever cases happened to be in the fixture, so the measured boundary is spelled out here in
/// full. The loop was the first wrong answer (a plain <c>l(0) = n</c> inside a <c>For Each</c>
/// compiles and is correct on C# — see
/// <see cref="Write_InsideAForEach_OverADifferentCollection"/>). Read-modify-write was the second:
/// it is sufficient but NOT necessary.</para>
///
/// <para>Measured at a36262c on all four backends: <c>Visit(IRIndexerStore)</c> spells the
/// <b>Collection</b>, each <b>Index</b> and the <b>Value</b> by raw temp name, and <b>any one of
/// the three being a temp fails on its own</b>, with no read-back anywhere:
/// <c>l(0) = Five()</c> is <c>CS0103 't1'</c> (value only), <c>l(Zero()) = 5</c> is
/// <c>CS0103 't1'</c> (index only), and <c>Get1()(0) = 5</c> is <c>CS0103 't0'</c> (receiver only).
/// A constant, a declared local and a parameter are all fine, which is why <c>l(0) = 5</c> and
/// <c>l(i) = 9</c> pass. ⚠ <c>l(0) = x + 1</c> is a FALSE control — the optimizer constant-folds it
/// to <c>l[0] = 5;</c>; route the operand through a parameter to keep it unfoldable.</para>
///
/// <para>The governing rule is now recorded as ADR-0001 (<c>docs/superpowers/decisions/</c>): a
/// value that is not replicable must appear in the emitted C# exactly once, and
/// <c>GetValueName</c> may only be called for a value already materialised as a declared local.
/// These three cases are that invariant's, not a defect family of their own.</para>
///
/// <para>⚠ NOT COVERED HERE: a qualified field-of-object indexer (<c>g.Items(0)</c>, reading or
/// writing a collection FIELD reached through another object) is broken on ALL FOUR backends —
/// the front end lowers it to a plain method call <c>Items(Int32)</c> that no backend can
/// resolve. That is a front-end gap, not this family's, and using it here would make an
/// unrelated defect look like this one. A field-held collection is written UNQUALIFIED from
/// inside its own class instead (a normal, unaffected shape — see the method/constructor/setter
/// cases below), or through a local alias.</para>
///
/// <para>⚠ The optimizer runs by default in both <c>MsilHarness</c> entry points
/// (<c>optimize: true</c>) per the repo's standing rule — codegen is validated through the
/// optimizer, not only a non-optimizing helper.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class MsilIndexerStoreTests
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

    /// <summary>
    /// MSIL only, against the value JavaScript computes — for the three READ-MODIFY-WRITE shapes
    /// where the C# backend's OWN indexer-store lowering does not compile (see the fixture note),
    /// so C# cannot be the oracle.
    /// </summary>
    private static void MsilMatchesJs(string program, string expected)
        => Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(program)), Is.EqualTo(expected),
            "MSIL, against JavaScript (C# does not compile this shape — see the fixture note)");

    // ========================================================================================
    // THE HEADLINE — an indexed write is observable at all. Before the fix these printed the
    // OLD element (List) or threw KeyNotFoundException at the next read (Dictionary): the write
    // itself emitted nothing.
    // ========================================================================================

    /// <summary>
    /// Also the "index and value differ" edge: index 0 and value 42 cannot agree by accident the
    /// way <c>l(0) = 0</c> could hide an operand-order swap.
    /// </summary>
    [Test]
    public void IndexedWrite_OnListOfInteger_IsObservedOnReadBack()
        => MsilAgreesWithCSharp(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n" +
            " l(0) = 42\n" +
            " PrintLine(CStr(l(0)))\n" +
            "End Sub",
            "42");

    /// <summary>
    /// The explicit VB spelling. <c>IRBuilder</c> lowers <c>l.Item(i) = v</c> to the exact same
    /// <c>IRIndexerStore</c> node as <c>l(i) = v</c> — this pins that the alternate spelling
    /// reaches the same, now-fixed, emission rather than falling through to a field-access +
    /// array-store fallback on a member named "Item" that does not exist.
    /// </summary>
    [Test]
    public void ExplicitItemSpelling_LowersToTheSameWrite()
        => MsilAgreesWithCSharp(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n" +
            " l.Item(0) = 42\n" +
            " PrintLine(CStr(l(0)))\n" +
            "End Sub",
            "42");

    // ========================================================================================
    // DICTIONARY INSERT-OR-UPDATE — the risky pair. An Add-based lowering passes a new key and
    // throws ArgumentException on an existing one; only asserting BOTH together, in one program,
    // pins that set_Item (not Add) is what got emitted.
    // ========================================================================================

    [Test]
    public void DictionaryWrite_ExistingKeyUpdatesAndNewKeyInserts()
        => MsilAgreesWithCSharp(
            "Sub Main()\n" +
            " Dim d As New Dictionary(Of String, Integer)()\n" +
            " d.Add(\"a\", 1)\n" +
            " d(\"a\") = 99\n" +
            " d(\"b\") = 50\n" +
            " PrintLine(CStr(d(\"a\")) & \",\" & CStr(d(\"b\")))\n" +
            "End Sub",
            "99,50");

    // ========================================================================================
    // ELEMENT/KEY TYPE — a hardcoded `!0` or `int32` spelling would still pass a
    // List(Of Integer): for `!0 = int32` the two spellings coincide. A String element (and a
    // non-string Dictionary key) are what a wrong-but-coincidentally-working signature cannot
    // survive.
    // ========================================================================================

    [Test]
    public void ListOfString_TheIndexStaysInt32WhileTheElementIsString()
        => MsilAgreesWithCSharp(
            "Sub Main()\n" +
            " Dim l As New List(Of String)()\n" +
            " l.Add(\"x\")\n" +
            " l(0) = \"hello\"\n" +
            " PrintLine(l(0))\n" +
            "End Sub",
            "hello");

    /// <summary>
    /// ⚠ C++ is not used here despite being a candidate .NET-free oracle: this exact program
    /// currently fails to COMPILE on the C++ backend (<c>std::vector&lt;bool&gt;::operator[]</c>
    /// specializes to a bit-reference, which cannot bind to the generated non-const
    /// <c>T&amp;</c> return) — a separate, pre-existing C++-backend defect around
    /// <c>List(Of Boolean)</c>, not this family's.
    /// </summary>
    [Test]
    public void ListOfBoolean_ElementWidthIsNotHardcodedToInt32()
        => MsilAgreesWithCSharp(
            "Sub Main()\n" +
            " Dim l As New List(Of Boolean)()\n" +
            " l.Add(True)\n" +
            " l.Add(False)\n" +
            " l(0) = False\n" +
            " PrintLine(CStr(l(0)) & \",\" & CStr(l(1)))\n" +
            "End Sub",
            "False,False");

    /// <summary>⚠ JavaScript is not used as a second oracle here: it refuses <c>Long</c> outright
    /// (<c>BL7003</c> — a JS number is exact only to 2^53) by design, unrelated to this family.</summary>
    [Test]
    public void ListOfLong_ElementWidthIsNotHardcodedToInt32()
        => MsilAgreesWithCSharp(
            "Sub Main()\n" +
            " Dim l As New List(Of Long)()\n" +
            " Dim a As Long = 1\n" +
            " Dim b As Long = 2\n" +
            " l.Add(a)\n" +
            " l.Add(b)\n" +
            " l(0) = 99\n" +
            " PrintLine(CStr(l(0)) & \",\" & CStr(l(1)))\n" +
            "End Sub",
            "99,2");

    [Test]
    public void ListOfDouble_ElementWidthIsNotHardcodedToInt32()
        => MsilAgreesWithCSharp(
            "Sub Main()\n" +
            " Dim l As New List(Of Double)()\n" +
            " l.Add(1.5)\n" +
            " l.Add(2.5)\n" +
            " l(0) = 9.25\n" +
            " PrintLine(CStr(l(0)) & \",\" & CStr(l(1)))\n" +
            "End Sub",
            "9.25,2.5");

    /// <summary>A non-string KEY — proves the key position is <c>!0</c>, not a hardcoded
    /// <c>string</c> the ordinary <c>Dictionary(Of String, ...)</c> cases above cannot tell apart
    /// from a correct signature.</summary>
    [Test]
    public void DictionaryOfIntegerKey_KeyPositionIsNotHardcodedToString()
        => MsilAgreesWithCSharp(
            "Sub Main()\n" +
            " Dim d As New Dictionary(Of Integer, String)()\n" +
            " d.Add(1, \"a\")\n" +
            " d(1) = \"z\"\n" +
            " d(2) = \"new\"\n" +
            " PrintLine(d(1) & \",\" & d(2))\n" +
            "End Sub",
            "z,new");

    // ========================================================================================
    // CONTEXT PATHS — a class method, a constructor and a property Set each build a method
    // through InitializeMethodContext rather than the module-Sub path GenerateMethod covers
    // above, and none of the earlier cases exercise it.
    // ========================================================================================

    [Test]
    public void Write_InsideAClassInstanceMethod()
        => MsilAgreesWithCSharp(
            "Class Box\n" +
            " Public Function SetFirst(l As List(Of Integer), v As Integer) As Integer\n" +
            "  l(0) = v\n" +
            "  Return l(0)\n" +
            " End Function\n" +
            "End Class\n\n" +
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n" +
            " Dim b As New Box()\n" +
            " PrintLine(CStr(b.SetFirst(l, 42)))\n" +
            "End Sub",
            "42");

    /// <summary>
    /// ⚠ The field is seeded with <c>Items = New List(Of Integer)()</c> INSIDE the constructor
    /// body, not with an inline <c>Public Items As New List(Of Integer)()</c> field initializer —
    /// that inline form does not even PARSE as a class field declaration in this language
    /// (<c>Expected type name but found New</c>), independent of anything this family touches.
    /// </summary>
    [Test]
    public void Write_InsideAConstructor()
        => MsilAgreesWithCSharp(
            "Class Box\n" +
            " Public Items As List(Of Integer)\n" +
            " Public Sub New()\n" +
            "  Items = New List(Of Integer)()\n" +
            "  Items.Add(1)\n" +
            "  Items(0) = 42\n" +
            " End Sub\n" +
            " Public Function First() As Integer\n" +
            "  Return Items(0)\n" +
            " End Function\n" +
            "End Class\n\n" +
            "Sub Main()\n" +
            " Dim b As New Box()\n" +
            " PrintLine(CStr(b.First()))\n" +
            "End Sub",
            "42");

    [Test]
    public void Write_InsideAPropertySetter()
        => MsilAgreesWithCSharp(
            "Class Box\n" +
            " Private _items As List(Of Integer)\n" +
            " Public Sub Seed()\n" +
            "  _items = New List(Of Integer)()\n" +
            "  _items.Add(1)\n" +
            " End Sub\n" +
            " Public Property First As Integer\n" +
            "  Get\n" +
            "   Return _items(0)\n" +
            "  End Get\n" +
            "  Set(value As Integer)\n" +
            "   _items(0) = value\n" +
            "  End Set\n" +
            " End Property\n" +
            "End Class\n\n" +
            "Sub Main()\n" +
            " Dim b As New Box()\n" +
            " b.Seed()\n" +
            " b.First = 42\n" +
            " PrintLine(CStr(b.First))\n" +
            "End Sub",
            "42");

    // ========================================================================================
    // REGION-BODY EMISSION — a Try's block is a separate emission path (EmitRegionBody) from an
    // ordinary basic block, the same path that has historically double-emitted blocks for other
    // constructs on this backend.
    // ========================================================================================

    [Test]
    public void Write_InsideATry_NoExceptionThrown()
        => MsilAgreesWithCSharp(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n" +
            " Try\n" +
            "  l(0) = 42\n" +
            " Catch ex As Exception\n" +
            "  l(0) = -1\n" +
            " End Try\n" +
            " PrintLine(CStr(l(0)))\n" +
            "End Sub",
            "42");

    // ========================================================================================
    // FOR EACH BODY — writing the collection BEING enumerated legitimately throws (pinned
    // separately below), so a plain value assertion needs a write to a DIFFERENT collection.
    // ========================================================================================

    /// <summary>
    /// ⛔ This case used to assert against JavaScript, on the claim that the C# backend cannot
    /// compile ANY indexer write inside a <c>For Each</c> body. <b>That claim was our own error.</b>
    /// Re-measured at f20435d (the parent of the C#-backend <c>Exit For</c> batch) and again after
    /// it: this program compiles on C# and prints <b>8</b> both times. The C#-backend defect is
    /// specifically a READ-MODIFY-WRITE through an indexer — see the three
    /// <c>ReadModifyWrite_*</c> cases below, one of which (<c>l(0) = l(1)</c>) has no loop at all
    /// and still fails. So this is an ordinary <c>MsilAgreesWithCSharp</c> case.
    /// </summary>
    [Test]
    public void Write_InsideAForEach_OverADifferentCollection()
        => MsilAgreesWithCSharp(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n" +
            " l.Add(2)\n" +
            " Dim other As New List(Of Integer)()\n" +
            " other.Add(9)\n" +
            " other.Add(8)\n" +
            " For Each n In other\n" +
            "  l(0) = n\n" +
            " Next\n" +
            " PrintLine(CStr(l(0)))\n" +
            "End Sub",
            "8");

    // ========================================================================================
    // READ-MODIFY-WRITE THROUGH AN INDEXER — ⛔ none of these compile on the C# backend
    // (CS0103: the name 'tN' does not exist in the current context — the C# backend's OWN
    // indexer-store lowering allocates a temp outside the scope it is read back in). JavaScript
    // is the oracle for all three.
    // ========================================================================================

    /// <summary>
    /// ⭐ PROMOTED: this shape used to fail to COMPILE on C# (<c>CS0103</c>, an undeclared
    /// indexer-store temp — <c>Visit(IRIndexerStore)</c> now calls <c>EmitExpression</c> directly
    /// on each operand instead of the invalid <c>GetValueName</c>). Now asserted against C# too,
    /// via <c>MsilAgreesWithCSharp</c>.
    /// </summary>
    [Test]
    public void ReadModifyWrite_OneListElementAssignedFromAnother()
        => MsilAgreesWithCSharp(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(5)\n" +
            " l.Add(9)\n" +
            " l(0) = l(1)\n" +
            " PrintLine(CStr(l(0)) & \",\" & CStr(l(1)))\n" +
            "End Sub",
            "9,9");

    /// <summary>⭐ PROMOTED — see <see cref="ReadModifyWrite_OneListElementAssignedFromAnother"/>.</summary>
    [Test]
    public void ReadModifyWrite_ListElementMultipliedInPlaceThroughAVariableIndex()
        => MsilAgreesWithCSharp(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(2)\n" +
            " l.Add(3)\n" +
            " Dim i As Integer = 1\n" +
            " l(i) = l(i) * 10\n" +
            " PrintLine(CStr(l(0)) & \",\" & CStr(l(1)))\n" +
            "End Sub",
            "2,30");

    /// <summary>⭐ PROMOTED — see <see cref="ReadModifyWrite_OneListElementAssignedFromAnother"/>.</summary>
    [Test]
    public void ReadModifyWrite_DictionaryValueIncrementedInPlace()
        => MsilAgreesWithCSharp(
            "Sub Main()\n" +
            " Dim d As New Dictionary(Of String, Integer)()\n" +
            " d.Add(\"a\", 5)\n" +
            " d(\"a\") = d(\"a\") + 1\n" +
            " PrintLine(CStr(d(\"a\")))\n" +
            "End Sub",
            "6");

    // ========================================================================================
    // IL TEXT — the ONE property a round trip cannot see. .NET Core does not verify IL for
    // fully-trusted code, so a WRONG generic instantiation in the signature (e.g. a hardcoded
    // `List`1<int32>` regardless of the actual element type) can still assemble, run and print
    // the right answer for a List(Of Integer) — the exact blind spot "List(Of Integer) cannot
    // tell !0 from int32" describes. Modelled on MsilForEachTests's
    // LoopVariable_LocalsSlot_IsTypedFromTheElementType_NotTheCollection.
    // ========================================================================================

    [Test]
    public void SetItemSignature_IsTheGenericDefinitionsSpelling_ForListAndDictionary()
    {
        var listIl = MsilHarness.CompileToIl(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n" +
            " l(0) = 42\n" +
            " PrintLine(CStr(l(0)))\n" +
            "End Sub");

        var dictIl = MsilHarness.CompileToIl(
            "Sub Main()\n" +
            " Dim d As New Dictionary(Of String, Integer)()\n" +
            " d.Add(\"a\", 1)\n" +
            " d(\"a\") = 42\n" +
            " PrintLine(CStr(d(\"a\")))\n" +
            "End Sub");

        Assert.Multiple(() =>
        {
            Assert.That(listIl, Does.Contain(
                "callvirt instance void class [mscorlib]System.Collections.Generic.List`1<int32>::set_Item(int32, !0)"),
                "List(Of Integer)'s set_Item must keep the index int32 and the element !0: " + listIl);

            Assert.That(dictIl, Does.Contain(
                "callvirt instance void class [mscorlib]System.Collections.Generic.Dictionary`2<string, int32>::set_Item(!0, !1)"),
                "Dictionary(Of String, Integer)'s set_Item must take the key as !0 and the value as !1: " + dictIl);
        });
    }

    // ========================================================================================
    // THE BEHAVIOUR THE FIX MAKES CORRECT, NOT REGRESSED — before the fix, writing a List while
    // enumerating it ran clean (the write emitted nothing) and printed a stale answer. Now the
    // write actually reaches the List, and .NET's own enumerator invalidation throws, exactly as
    // it does on the C# backend.
    // ========================================================================================

    [Test]
    public void ModifyingAListWhileEnumeratingIt_NowThrows_MatchingCSharp()
    {
        const string program =
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n" +
            " l.Add(2)\n" +
            " For Each n In l\n" +
            "  l(0) = 99\n" +
            " Next\n" +
            " PrintLine(\"done\")\n" +
            "End Sub";

        Assert.Multiple(() =>
        {
            Assert.That(() => FourBackends.RunEmittedCSharp(program),
                Throws.InstanceOf<AssertionException>().With.Message.Contains("InvalidOperationException"),
                "C# (the reference .NET backend) also throws for this shape");

            var msil = MsilHarness.Run(program);
            Assert.That(msil.Outcome, Is.EqualTo(MsilHarness.MsilOutcome.RunFailed), msil.Report);
            Assert.That(msil.Output, Does.Contain("InvalidOperationException"), msil.Report);
        });
    }
}
