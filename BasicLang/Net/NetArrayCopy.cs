using System;
using System.Collections.Generic;
using System.Linq;

namespace BasicLang.Net
{
    /// <summary>
    /// ONE element wire form of spec §8.6's outbound array copy: everything the three consumers
    /// need to move a native <c>std::vector&lt;T&gt;</c> across the boundary and back.
    ///
    /// <para><b>Why the native and the wire element are separate columns.</b> For the ten
    /// numeric rows they are the same string and the staging loop is a plain copy. For the three
    /// that differ it is the whole point: BasicLang's <c>Boolean</c> is a C++ <c>bool</c> whose
    /// <c>std::vector</c> is the BITSET specialization (there is no <c>.data()</c> to hand over),
    /// its <c>Char</c> is ONE byte against .NET's two (§8.3/§14.10), and its <c>String</c> is a
    /// <c>std::string</c> against a <c>const char*</c> wire with P0's transfer-buffer ownership.
    /// A single "element type" column would have been right for ten rows and silently wrong for
    /// three.</para>
    /// </summary>
    /// <param name="ElementFullName">The .NET element type this row copies — the key.</param>
    /// <param name="Suffix">
    /// The export-name suffix. Spec §8.6 spells the worked example
    /// <c>bl_net_array_new_int32</c>, so the suffix is the WIRE width rather than the BasicLang
    /// or the .NET spelling.
    /// </param>
    /// <param name="CWireIn">The C element type of the copy-IN buffer (<c>src</c>).</param>
    /// <param name="CWireOut">
    /// The C element type of the readback buffer (<c>dst</c>). Differs from
    /// <paramref name="CWireIn"/> only for <c>String</c>, exactly as §8.3's String row does:
    /// <c>const char*</c> in (borrowed), <c>char*</c> out (a <c>blnet_alloc</c>'d buffer the
    /// native side frees).
    /// </param>
    /// <param name="CsElement">
    /// The C# spelling of the managed array's element — what the shim casts the handle to.
    /// </param>
    /// <param name="NativeElement">
    /// The C++ type <c>CppCodeGenerator.MapType</c> produces for this row's BasicLang element.
    /// The call site RE-DERIVES it from <c>MapType</c> and refuses on a mismatch rather than
    /// trusting this column — see <c>CppCodeGenerator.MarshalNetArrayArgument</c>.
    /// </param>
    /// <param name="CppToWire">
    /// <c>{0}</c>-format converting one native element into its wire element, for the staging
    /// loop the generated proxy emits.
    /// </param>
    /// <param name="CppFromWire">The inverse, for the readback staging loop.</param>
    /// <param name="CsFromWire">C#: one wire element to one managed element.</param>
    /// <param name="CsToWire">C#: one managed element to one wire element.</param>
    /// <param name="ReadElementIsOwned">
    /// True when each element the readback writes is an allocation the NATIVE side must release
    /// with <c>blnet_free</c> — String only, matching P0's string-return rule. The generated
    /// proxy does the freeing so no call site can forget.
    /// </param>
    internal sealed record NetArrayCopyForm(
        string ElementFullName,
        string Suffix,
        string CWireIn,
        string CWireOut,
        string CsElement,
        string NativeElement,
        string CppToWire = "{0}",
        string CppFromWire = "{0}",
        string CsFromWire = "{0}",
        string CsToWire = "{0}",
        bool ReadElementIsOwned = false)
    {
        /// <summary>The §8.6 copy-IN export/slot/proxy name — one name, three places (§12.4).</summary>
        internal string NewExportName => "bl_net_array_new_" + Suffix;

        /// <summary>The §8.6 readback export/slot/proxy name.</summary>
        internal string ReadExportName => "bl_net_array_read_" + Suffix;

        /// <summary>The <c>T[]</c> metadata spelling this row copies.</summary>
        internal string ArrayFullName => ElementFullName + "[]";

        /// <summary>
        /// The complete C spelling of the copy-IN buffer parameter — read-only, one indirection
        /// above <see cref="CWireIn"/>.
        ///
        /// <para><b>Not <c>"const " + CWireIn + "*"</c>, which is why this is a member and not a
        /// string concat at the emitter.</b> For the String row <see cref="CWireIn"/> is already
        /// <c>const char*</c>, and prefixing another <c>const</c> yields
        /// <c>const const char**</c> — a DUPLICATE cv-qualifier. It means the right thing and it
        /// compiles, but MSVC reports C4114 ("same type qualifier used more than once") and
        /// clang/gcc have their own duplicate-decl-specifier warning, so every user building a
        /// String-array surface would get a warning out of a generated header they cannot edit —
        /// fatal under <c>-Werror</c>. The const goes on the OUTER pointer instead:
        /// <c>const char* const*</c>, which is also the more accurate type.</para>
        /// </summary>
        internal string CSourcePointer =>
            CWireIn.EndsWith("*", StringComparison.Ordinal)
                ? CWireIn + " const*"
                : "const " + CWireIn + "*";
    }

    /// <summary>
    /// THE spec-§8.6 table: which native BasicLang arrays can cross outbound by copy, under what
    /// export names, and which forms a given surface actually needs.
    ///
    /// <para><b>The representation rule this table serves, stated once (§8.6).</b> A .NET
    /// <c>T[]</c> VALUE is always a HANDLE — §8.5 governs consuming it, and
    /// <c>Dim a = obj.GetValues()</c> keeps that handle with no copy at all. Copying happens
    /// ONLY when a NATIVE array (a <c>std::vector&lt;T&gt;</c>, which has no <c>GCHandle</c> and
    /// no handle) sits on the other side of an assignment or a parameter. That is the entire
    /// reason this table exists, and it is why every entry point below is reached from a
    /// native-array test and never from a type NAME.</para>
    ///
    /// <para><b>v1 admits SIMPLE element types only.</b> A row here is a by-value row of §8.3 or
    /// <c>String</c>. Everything else — <c>List</c>/<c>Dictionary</c>/<c>HashSet</c> outbound,
    /// nested element types such as <c>List(Of List(Of Integer))</c>, and element types that are
    /// themselves handles — is BL6019 naming the parameter and the offending type. The refusals
    /// live where they can carry a source position (<c>SemanticAnalyzer</c>) with the generator's
    /// positionless layer behind them, which is the same two-layer arrangement §8.3's rows
    /// use.</para>
    ///
    /// <para><b>§12.4 holds by construction.</b> The copy helpers are ordinary
    /// <c>BlnetProxyTable</c> slots and ordinary shim exports — NOT a side channel — and both
    /// emitters derive the set from <see cref="RequiredForms"/>, so "proxy slots ≡ shim exports"
    /// is a consequence of calling one function twice rather than a property to be re-checked.
    /// (P2a-1's header called these "§12.4-exempt"; making them ordinary slots is strictly
    /// better, because the exemption would have been an invariant hole nothing tested.)</para>
    /// </summary>
    internal static class NetArrayCopy
    {
        /// <summary>
        /// §8.6's v1 element set, keyed by .NET element full name — §8.3's by-value rows plus
        /// <c>String</c>, and deliberately NOT projected from
        /// <see cref="NetMarshalTable.WireRows"/>: that table's §6.4 CONVERSION rows (DateTime,
        /// Decimal, Guid, …) are by-value rows too, and an array of them needs a per-element
        /// conversion pair on both sides that §8.6 v1 does not specify. Listing the admitted set
        /// explicitly is what keeps "v1 = simple element types" a decision instead of a
        /// side effect of another table's contents.
        /// </summary>
        internal static readonly IReadOnlyList<NetArrayCopyForm> Forms = new[]
        {
            // The one row whose native vector is NOT contiguous: std::vector<bool> is the C++
            // bitset specialization, so the generated proxy stages through a real buffer instead
            // of handing over .data(). Wire is uint8_t rather than §8.3's int32_t — an array
            // element does not need the [UnmanagedCallersOnly] blittability §8.3's scalar slot
            // does, and one byte per element beats four.
            new NetArrayCopyForm("System.Boolean", "bool", "uint8_t", "uint8_t", "bool", "bool",
                CppToWire: "({0} ? 1 : 0)", CppFromWire: "({0} != 0)",
                CsFromWire: "({0} != 0)", CsToWire: "({0} ? (byte)1 : (byte)0)"),

            new NetArrayCopyForm("System.SByte", "int8", "int8_t", "int8_t", "sbyte", "int8_t"),
            new NetArrayCopyForm("System.Byte", "uint8", "uint8_t", "uint8_t", "byte", "uint8_t"),
            new NetArrayCopyForm("System.Int16", "int16", "int16_t", "int16_t", "short", "int16_t"),
            new NetArrayCopyForm("System.UInt16", "uint16", "uint16_t", "uint16_t", "ushort", "uint16_t"),
            new NetArrayCopyForm("System.Int32", "int32", "int32_t", "int32_t", "int", "int32_t"),
            new NetArrayCopyForm("System.UInt32", "uint32", "uint32_t", "uint32_t", "uint", "uint32_t"),
            new NetArrayCopyForm("System.Int64", "int64", "int64_t", "int64_t", "long", "int64_t"),
            new NetArrayCopyForm("System.UInt64", "uint64", "uint64_t", "uint64_t", "ulong", "uint64_t"),
            new NetArrayCopyForm("System.Single", "single", "float", "float", "float", "float"),
            new NetArrayCopyForm("System.Double", "double", "double", "double", "double", "double"),

            // §8.3's Char rule, per element. The `unsigned char` hop is LOAD-BEARING and not a
            // stylistic double cast: plain `char` is signed on this backend, so a bare
            // static_cast<uint16_t> sign-extends every byte >= 0x80 into 0xFFxx. Inbound narrows
            // — §14.10's shipped divergence, per element here exactly as it is per value there.
            new NetArrayCopyForm("System.Char", "char", "uint16_t", "uint16_t", "char", "char",
                CppToWire: "static_cast<uint16_t>(static_cast<unsigned char>({0}))",
                CppFromWire: "static_cast<char>({0})",
                CsFromWire: "(char)({0})", CsToWire: "(ushort){0}"),

            // P0 string ownership, per element and in BOTH directions: the copy-IN borrows the
            // caller's buffers (the managed side copies during the call), while the readback
            // hands back blnet_alloc'd buffers the NATIVE side frees. ReadElementIsOwned is what
            // makes the generated proxy emit that free, so no call site can leak by omission.
            new NetArrayCopyForm("System.String", "string", "const char*", "char*", "string",
                "std::string",
                CppToWire: "{0}.c_str()", CppFromWire: "({0} ? std::string({0}) : std::string())",
                // Both directions guard null: a managed String[] element may be null (which
                // AllocUtf8 would dereference), and a native readback of one must produce the
                // empty String rather than a wild pointer. §8.2's Nothing rule, per element.
                CsFromWire: "(Utf8ToString({0}) ?? string.Empty)",
                CsToWire: "({0} is null ? null : AllocUtf8({0}))",
                ReadElementIsOwned: true),
        };

        private static readonly IReadOnlyDictionary<string, NetArrayCopyForm> ByElement =
            Forms.ToDictionary(f => f.ElementFullName, StringComparer.Ordinal);

        /// <summary>
        /// The row for a .NET ELEMENT full name, or false. <c>System.Int32</c> → the int32 row.
        /// </summary>
        internal static bool TryGetFormForElement(string elementFullName, out NetArrayCopyForm form)
        {
            form = null;
            return !string.IsNullOrEmpty(elementFullName)
                   && ByElement.TryGetValue(elementFullName, out form);
        }

        /// <summary>
        /// The row for a .NET ARRAY full name (<c>System.Int32[]</c>), or false. Rank &gt; 1 and
        /// jagged spellings answer false: §8.6 v1 is one-dimensional, matching §8.5's
        /// synthesized accessors, and a rank-2 copy would need a shape the wire does not carry.
        /// </summary>
        internal static bool TryGetFormForArray(string arrayFullName, out NetArrayCopyForm form)
        {
            form = null;
            if (string.IsNullOrEmpty(arrayFullName)) return false;
            if (!arrayFullName.EndsWith("[]", StringComparison.Ordinal)) return false;

            var element = arrayFullName.Substring(0, arrayFullName.Length - 2);
            if (element.EndsWith("]", StringComparison.Ordinal)) return false;   // jagged / rank > 1
            return TryGetFormForElement(element, out form);
        }

        /// <summary>
        /// The element forms <paramref name="surface"/> needs copy helpers for: every array type
        /// that appears in a collected member's PARAMETER list or RESULT, in
        /// <see cref="Forms"/> order.
        ///
        /// <para><b>Both positions, and both helpers for each form.</b> A parameter is §8.6's
        /// copy-IN (rows 3 and 4); a result is what row 2's declared-native-array materialization
        /// reads back, and a <c>ref</c>/<c>out</c> parameter needs both. Deriving the set from
        /// the SIGNATURE rather than from which call sites actually copy over-emits at worst a
        /// few small exports for a member whose array the program chose to keep as a handle —
        /// whereas under-emitting is a link failure at the very end of a ~27 s publish. The
        /// asymmetry of those two costs is the whole argument.</para>
        ///
        /// <para>Order is <see cref="Forms"/> order rather than encounter order, because the
        /// emitted export set is part of §10.2's shim cache key and an order that depended on IR
        /// walk order would produce false misses.</para>
        /// </summary>
        internal static IReadOnlyList<NetArrayCopyForm> RequiredForms(NetSurface surface)
        {
            if (surface == null) return Array.Empty<NetArrayCopyForm>();

            var needed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in surface.Members)
            {
                if (member == null) continue;
                if (TryGetFormForArray(member.TypeFullName, out var resultForm))
                    needed.Add(resultForm.ElementFullName);
                foreach (var parameter in member.Parameters ?? Array.Empty<NetParameterDescriptor>())
                    if (TryGetFormForArray(parameter?.TypeFullName, out var parameterForm))
                        needed.Add(parameterForm.ElementFullName);
            }

            return needed.Count == 0
                ? Array.Empty<NetArrayCopyForm>()
                : Forms.Where(f => needed.Contains(f.ElementFullName)).ToList();
        }

        /// <summary>
        /// Every export/slot name <see cref="RequiredForms"/> implies, in the same order — the
        /// list both emitters append to their §12.4 name sets.
        /// </summary>
        internal static IReadOnlyList<string> RequiredExportNames(NetSurface surface) =>
            RequiredForms(surface)
                .SelectMany(f => new[] { f.NewExportName, f.ReadExportName })
                .ToList();
    }
}
