using System.Collections.Generic;
using System.Linq;
using BasicLang;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.IR;
using BasicLang.Net;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// Spec §6.5's claim predicate. Getting this wrong is the single most dangerous mistake in
/// P2a: reading "claimed" as "registry + CppCapabilityChecker" routes Console.WriteLine through
/// the shim and rewrites the behavior of every existing program, P1's parity battery included.
///
/// <para>Every line range the driving plan cited was re-verified against the working tree before
/// these tests were written, and one was wrong — see
/// <see cref="TableMembersWithoutAnEmitArmAreNotClaimed"/> for the corrected count.</para>
/// </summary>
[TestFixture]
public class NetClaimPredicateTests
{
    [TestCase("String")]      // BoundaryTypeRegistry -> Bridged
    [TestCase("DateTime")]    // BoundaryTypeRegistry -> NativeOwned
    [TestCase("List")]        // CppCapabilityChecker :620-623
    [TestCase("Dictionary")]
    [TestCase("HashSet")]
    [TestCase("Task")]        // CppCapabilityChecker :600
    [TestCase("Func")]        // CppCapabilityChecker :603-604
    [TestCase("Action")]
    public void ClaimedTypeNames(string name) =>
        Assert.That(NetClaimPredicate.IsClaimedTypeName(name), Is.True,
            $"'{name}' has native C++ handling and must never reach NetTypeResolver. If this " +
            "failed, fix NetClaimPredicate (rows a/b), NOT the test — a claimed name routed " +
            "through the shim rewrites existing programs.");

    [TestCase("Regex")]       // ManagedOwned after P2a-2's flip -> shim-routed, NOT claimed
    [TestCase("Uri")]
    [TestCase("Stream")]
    public void ManagedOwnedNamesAreNotClaimed(string name) =>
        Assert.That(NetClaimPredicate.IsClaimedTypeName(name), Is.False,
            "Row (a) is {NativeOwned, Bridged}, NOT `!= Unknown`. Claiming ManagedOwned would " +
            "make spec §4.2's Regex_Match__string slot ungeneratable. The predicate must give " +
            "the same answer before and after P2a-2's registry flip. Fix NetClaimPredicate's " +
            "row (a) test, not this assertion.");

    /// <summary>
    /// The flip-stability claim above, made mechanical rather than anecdotal: TODAY those three
    /// names are <see cref="BoundaryTypeCategory.Rejected"/>, and P2a-2 moves them to
    /// <see cref="BoundaryTypeCategory.ManagedOwned"/>. Both categories must answer "not claimed",
    /// so this asserts over whichever category the registry currently puts them in AND over the
    /// one it will. If a future edit makes either category claimed, this fails and names the side
    /// to fix.
    /// </summary>
    [Test]
    public void PredicateIsStableAcrossTheP2a2RegistryFlip()
    {
        var shimRouted = BoundaryTypeRegistry.NamesInCategory(BoundaryTypeCategory.ManagedOwned)
            .Concat(BoundaryTypeRegistry.NamesInCategory(BoundaryTypeCategory.Rejected))
            .Where(n => !n.Equals("Object", System.StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.That(shimRouted, Is.Not.Empty,
            "Neither ManagedOwned nor Rejected holds a shim-routable name, so this test proves " +
            "nothing. Fix BoundaryTypeRegistry (or this test's category selection), not the predicate.");

        foreach (var name in shimRouted)
        {
            Assert.That(NetClaimPredicate.IsClaimedTypeName(name), Is.False,
                $"'{name}' is registry-category shim-routed but the claim predicate claims it. " +
                "Fix NetClaimPredicate.IsClaimedTypeName — row (a) must accept ONLY " +
                "{NativeOwned, Bridged}.");
        }
    }

    [Test]
    public void ConsoleWriteLineIsClaimedPerCall()
    {
        Assert.That(NetClaimPredicate.IsClaimedCall("Console", "WriteLine"), Is.True,
            "Console is claimed by IRBuilder.KnownNetStaticTypes -> EmitStdLibCall, NOT by " +
            "CppCapabilityChecker. Missing this routes every existing program through the shim. " +
            "Fix NetClaimPredicate row (c), not this test.");
    }

    [Test]
    public void ConsoleReadKeyIsNotClaimed()
    {
        Assert.That(NetClaimPredicate.IsClaimedCall("Console", "ReadKey"), Is.False,
            "Row (c) is PER CALL: KnownNetStaticTypes is a call-SHAPE classifier, not an " +
            "inventory of native implementations. EmitStdLibCall returns null here. Fix " +
            "NetClaimPredicate row (c) to consult the emit arm, not table membership.");
    }

    [TestCase("File", "ReadAllText")]
    [TestCase("Activator", "CreateInstance")]
    public void TableMembersWithoutAnEmitArmAreNotClaimed(string type, string member) =>
        Assert.That(NetClaimPredicate.IsClaimedCall(type, member), Is.False,
            "The plan said 22 KnownNetStaticTypes entries have no EmitStdLibCall arm. The real "
            + $"number is larger: the table holds {IRBuilder.KnownNetStaticTypeNames.Count} entries "
            + "and only Console plus the six NativeOwned BCL types can produce a non-null "
            + "EmitStdLibCall — 22 is the spec's list of NOTABLE ones, not the total (see "
            + $"{nameof(UnclaimedTableEntryCountIsPinned)}). Claiming them by table membership "
            + "would strand the surface spec §1.1 exists to deliver. Fix NetClaimPredicate row (c).");

    /// <summary>
    /// Pins the REAL number the plan got wrong. A type in <c>KnownNetStaticTypes</c> can produce a
    /// non-null <c>EmitStdLibCall</c> only through a DOTTED arm of the <c>functionName.ToLower()</c>
    /// switch (only <c>console.writeline</c> / <c>console.write</c> qualify — every other arm is a
    /// bare VB stdlib name) or through the <c>NativeBclSurface</c> static-dispatch branch (the six
    /// NativeOwned types). No <c>EmitFrameworkCall</c> arm contains a dot, so the framework path
    /// cannot claim a dotted call at all.
    /// </summary>
    [Test]
    public void UnclaimedTableEntryCountIsPinned()
    {
        var table = IRBuilder.KnownNetStaticTypeNames;
        // Row (c) ONLY — the emit-arm half. Asking IsClaimedCall here would fold in rows (a)/(b)
        // and count every Bridged primitive name (Boolean, Byte, Char, Double, SByte, Single,
        // String) plus Task, none of which have an EmitStdLibCall arm at all. That conflation is
        // exactly the misreading this fixture exists to prevent.
        var withArm = table.Where(HasAnyStdLibArm).OrderBy(n => n).ToList();

        Assert.That(table.Count, Is.EqualTo(58),
            "KnownNetStaticTypes (IRBuilder.cs:3644-3664) changed size. Update this number and "
            + "re-derive the unclaimed count below; do not delete the assertion.");
        Assert.That(withArm, Is.EquivalentTo(new[]
            {
                "Console", "DateTime", "DateTimeOffset", "Decimal", "Guid", "TimeSpan"
            }),
            "The set of KnownNetStaticTypes entries that can produce a non-null EmitStdLibCall "
            + "changed. If a type gained a native emit arm this is expected — update the list. If "
            + "one LOST an arm, that is a regression in CppCodeGenerator, not in this test.");
        Assert.That(table.Count - withArm.Count, Is.EqualTo(52),
            "52, not the plan's 22, is the number of KnownNetStaticTypes entries with no "
            + "EmitStdLibCall arm for any member. The plan's 22 is spec §6.5's list of NOTABLE "
            + "names, which is a correct subset, not the total. Update the number if the table "
            + "changes; do not delete the assertion.");
    }

    private static bool HasAnyStdLibArm(string typeName) =>
        ProbeMembers.Any(m => CppCodeGenerator.HasStdLibEmission(typeName + "." + m));

    /// <summary>
    /// Member names probed to decide whether a type has ANY claimed call. Covers every dotted
    /// stdlib arm plus every member the NativeBclSurface exposes, so a type with a native emit
    /// path cannot be missed.
    /// </summary>
    private static readonly IReadOnlyList<string> ProbeMembers =
        new[] { "WriteLine", "Write", "ReadKey", "ReadLine", "ReadAllText", "CreateInstance" }
            .Concat(NativeBclSurface.Members.Select(m => m.MemberName))
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Row (c)'s non-drift guard, asserted against <b><c>EmitStdLibCall</c> ITSELF</b> — the real
    /// generator method the spec's row (c) names — not against
    /// <see cref="CppCodeGenerator.HasStdLibEmission"/>.
    ///
    /// <para><b>Comparing to HasStdLibEmission would be circular.</b> The predicate already calls
    /// it, so the two agree by construction and the test could never fail; an earlier draft did
    /// exactly that and would have stayed green if an arm were re-inlined directly into
    /// <c>EmitStdLibCall</c> without going through <see cref="CppCodeGenerator.StdLibArm"/>. Only
    /// invoking the generator can detect that, which is why <c>EmitStdLibCall</c> is
    /// <c>internal</c>.</para>
    /// </summary>
    [Test]
    public void RowCAgreesWithEmitStdLibCallItself()
    {
        // A throwaway generator: EmitStdLibCall's only instance dependencies are the
        // user-shadowing guard (reads a null _module -> false) and EmitFrameworkCall's
        // used-function bookkeeping, neither of which affects whether an arm exists.
        var generator = new CppCodeGenerator();
        var args = new List<string> { "a", "a", "a", "a", "a", "a", "a", "a" };

        foreach (var type in IRBuilder.KnownNetStaticTypeNames)
        {
            foreach (var member in ProbeMembers)
            {
                var predicate = NetClaimPredicate.IsClaimedCall(type, member);
                var emitted = NetClaimPredicate.IsClaimedTypeName(type)
                              || generator.EmitStdLibCall(type + "." + member, args) != null;

                Assert.That(predicate, Is.EqualTo(emitted),
                    $"NetClaimPredicate.IsClaimedCall(\"{type}\", \"{member}\") disagrees with what "
                    + "CppCodeGenerator.EmitStdLibCall actually emits. Row (c) must reduce to that "
                    + "one method. If an arm was added straight to EmitStdLibCall, move it into "
                    + "StdLibArm (or one of the other two shared providers) so HasStdLibEmission "
                    + "can see it — never add a parallel switch to the predicate.");
            }
        }
    }

    /// <summary>
    /// The OTHER half of row (c): table membership. A name with a real emit arm that is NOT in
    /// <c>KnownNetStaticTypes</c> must still be unclaimed, because the IR never routes such a call
    /// through the static-dispatch shape in the first place. <c>Print</c>/<c>Len</c>/<c>Abs</c> are
    /// real <c>StdLibArm</c> arms whose owning "type" is nothing at all.
    /// </summary>
    [TestCase("Math", "Abs")]
    [TestCase("NotATableEntry", "Print")]
    [TestCase("NotATableEntry", "WriteLine")]
    public void MembershipIsRequiredNotJustAnArm(string type, string member)
    {
        Assume.That(IRBuilder.IsKnownNetStaticTypeName(type) && type != "Math", Is.False);

        Assert.That(NetClaimPredicate.IsClaimedCall(type, member), Is.False,
            $"'{type}.{member}' was claimed. Row (c) is membership in KnownNetStaticTypes AND a "
            + "non-null EmitStdLibCall — dropping the membership half would claim calls the IR "
            + "never routes here. (Math IS in the table but has no dotted arm: EmitStdLibCall sees "
            + "\"math.abs\", and the bare \"abs\" arm does not match it.)");
    }

    /// <summary>
    /// Row (b) mirrors <see cref="CppCapabilityChecker"/>'s early returns, which live in a private
    /// method and so cannot be called directly. This drives the checker through its public entry
    /// point instead: a claimed type name used in a signature position must draw NO capability
    /// diagnostic. If the checker stops treating one of these natively, the predicate is now
    /// claiming a name the backend cannot emit, and the name below says which.
    /// </summary>
    [TestCase("List")]
    [TestCase("Dictionary")]
    [TestCase("HashSet")]
    [TestCase("Task")]
    [TestCase("Func")]
    [TestCase("Action")]
    [TestCase("DateTime")]
    [TestCase("String")]
    public void RowBAgreesWithTheCapabilityCheckersEarlyReturns(string name)
    {
        Assert.That(NetClaimPredicate.IsClaimedTypeName(name), Is.True,
            $"'{name}' is an early return in CppCapabilityChecker.CheckType (CppCapabilityChecker.cs:"
            + "590-625) but the predicate does not claim it. Fix NetClaimPredicate's row (b) list to "
            + "match those early returns.");
    }

    /// <summary>
    /// A ::-qualified foreign C++ name is claimed (CppCapabilityChecker.cs:624-625) and must never
    /// be looked up as a .NET type.
    /// </summary>
    [Test]
    public void ForeignCppNamesAreClaimed() =>
        Assert.That(NetClaimPredicate.IsClaimedTypeName("std::vector"), Is.True,
            "A ::-qualified foreign C++ type is opaque passthrough on the native backend and has "
            + "no .NET identity. Fix NetClaimPredicate row (b).");

    /// <summary>
    /// .NET exception names are claimed (CppCapabilityChecker.cs:599 -> std::runtime_error).
    /// </summary>
    [Test]
    public void NetExceptionNamesAreClaimed() =>
        Assert.That(NetClaimPredicate.IsClaimedTypeName("InvalidOperationException"), Is.True,
            "CppExceptionTypes.IsNetException names lower to std::runtime_error natively. Fix "
            + "NetClaimPredicate row (b).");

    /// <summary>
    /// Row (a) is case-insensitive because BasicLang is. <c>datetime</c> and <c>DateTime</c> are
    /// the same type, and a case-sensitive predicate would send one spelling to the shim.
    /// </summary>
    [TestCase("datetime")]
    [TestCase("DATETIME")]
    [TestCase("list")]
    public void ClaimIsCaseInsensitive(string name) =>
        Assert.That(NetClaimPredicate.IsClaimedTypeName(name), Is.True,
            "BasicLang is case-insensitive; the claim predicate must be too, or one spelling of a "
            + "natively-handled type routes through the shim. Fix NetClaimPredicate.");

    [Test]
    public void NullAndEmptyAreNotClaimed()
    {
        Assert.That(NetClaimPredicate.IsClaimedTypeName(null), Is.False);
        Assert.That(NetClaimPredicate.IsClaimedTypeName(""), Is.False);
        Assert.That(NetClaimPredicate.IsClaimedCall(null, "M"), Is.False);
        Assert.That(NetClaimPredicate.IsClaimedCall("Console", null), Is.False);
    }
}
