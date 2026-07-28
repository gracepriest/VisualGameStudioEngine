using System;
using System.Collections.Generic;
using System.Linq;

namespace BasicLang
{
    /// <summary>Kind of a P1 native-BCL surface member (spec §4).</summary>
    public enum NativeBclMemberKind
    {
        InstanceMethod,
        Property,
        StaticMethod,
        StaticProperty,
        Constructor,
        /// <summary>
        /// Operator semantics do not live in <see cref="NativeBclSurface.Members"/> —
        /// they are keyed (LeftType, Op, RightType) → Result and live in
        /// <see cref="NativeBclSurface.Operators"/> / <see cref="NativeBclSurface.UnaryOperators"/>.
        /// </summary>
        Operator,
    }

    /// <summary>One implemented member of a P1 native BCL type (spec §5).</summary>
    public sealed class NativeBclMember
    {
        public string TypeName { get; }
        public string MemberName { get; }
        public NativeBclMemberKind Kind { get; }
        /// <summary>
        /// Accepted argument counts (one entry per overload). Empty for
        /// properties. For params-array members (StringBuilder.AppendFormat)
        /// the listed counts cover the supported v1 arities.
        /// </summary>
        public int[] ParamCounts { get; }
        public string ReturnTypeName { get; }
        /// <summary>
        /// v1 divergence flag (spec §5): the BL surface type is Integer while
        /// the real .NET member returns an enum, so the C# backend must wrap
        /// the member access in an explicit (int) cast (otherwise csc fails
        /// CS0266 on the typed temp, and WriteLine would print the enum name —
        /// a parity diff vs C++'s numeric value). Exactly DateTime.DayOfWeek
        /// and DateTime.Kind carry it.
        /// </summary>
        public bool RequiresCSharpIntCast { get; }
        /// <summary>
        /// The C++ runtime member name when it must differ from the BL name;
        /// null means "same name". Set for exactly DateTimeOffset's BL
        /// <c>DateTime</c> property (→ <c>ClockDateTime</c>; a C++ member cannot
        /// share its enclosing type's name) and its <c>Ticks</c> property
        /// (→ <c>TicksValue</c>). Consumed by Task 9/10 codegen.
        /// </summary>
        public string CppName { get; }

        public NativeBclMember(string typeName, string memberName, NativeBclMemberKind kind,
            int[] paramCounts, string returnTypeName,
            bool requiresCSharpIntCast = false, string cppName = null)
        {
            TypeName = typeName;
            MemberName = memberName;
            Kind = kind;
            ParamCounts = paramCounts;
            ReturnTypeName = returnTypeName;
            RequiresCSharpIntCast = requiresCSharpIntCast;
            CppName = cppName;
        }
    }

    /// <summary>
    /// A binary operator row from the spec §6.1 cross-type result table,
    /// keyed (LeftType, Op, RightType) → Result. Rows are DIRECTIONAL:
    /// DateTime + TimeSpan exists, TimeSpan + DateTime deliberately does not.
    /// </summary>
    public sealed class NativeBclOperatorRow
    {
        public string LeftTypeName { get; }
        /// <summary>Canonical op token: + - * / Mod = &lt;&gt; &lt; &lt;= &gt; &gt;=.</summary>
        public string Op { get; }
        public string RightTypeName { get; }
        public string ResultTypeName { get; }

        public NativeBclOperatorRow(string leftTypeName, string op, string rightTypeName, string resultTypeName)
        {
            LeftTypeName = leftTypeName;
            Op = op;
            RightTypeName = rightTypeName;
            ResultTypeName = resultTypeName;
        }
    }

    /// <summary>
    /// The single-source member table for the P1 native BCL types (spec §4,
    /// docs/superpowers/specs/2026-07-27-p1-native-bcl-types-design.md) — the
    /// boundary-contract philosophy applied to members. One table, five
    /// consumers:
    ///  1. SemanticAnalyzer member typing (LookupNetTypeMember consults this
    ///     FIRST for the P1 type names, before the LSP's reflection
    ///     TypeRegistry) and the binary/unary/compound operator gates (Task 5).
    ///  2. CppCapabilityChecker's member-level pass — unknown member on a
    ///     NativeOwned receiver → clean BL diagnostic (Task 10).
    ///  3. C++ codegen dispatch — property→method bridge, static dispatch
    ///     table, constructor lowering (Task 9).
    ///  4. C# backend (int)-cast wiring for the divergent-typed members
    ///     (RequiresCSharpIntCast; Task 5).
    ///  5. Drift tests — surface ↔ BoundaryTypeRegistry.NativeOwned coherence
    ///     in both directions (Task 5 pins the six names; Task 10 re-points at
    ///     the registry).
    /// Notes pinned by spec §5: DayOfWeek/Kind are typed Integer (native BCL
    /// enums are out of P1 scope); TryParse is on NO surface (its out
    /// parameter is inexpressible); Guid has no ToByteArray on the BL surface;
    /// SByte is Bridged and has NO entries here.
    /// </summary>
    public static class NativeBclSurface
    {
        private const string DT = "DateTime";
        private const string TS = "TimeSpan";
        private const string GD = "Guid";
        private const string SB = "StringBuilder";
        private const string DEC = "Decimal";
        private const string DTO = "DateTimeOffset";

        private const string CtorName = ".ctor";

        private static readonly int[] NoParams = Array.Empty<int>();
        private static int[] P(params int[] counts) => counts;

        private static NativeBclMember M(string type, string name, NativeBclMemberKind kind,
            int[] paramCounts, string returns, bool intCast = false, string cppName = null)
            => new NativeBclMember(type, name, kind, paramCounts, returns, intCast, cppName);

        /// <summary>The v1 member surfaces, verbatim from spec §5.</summary>
        public static readonly IReadOnlyList<NativeBclMember> Members = new[]
        {
            // ---------------- DateTime ----------------
            M(DT, "Now", NativeBclMemberKind.StaticProperty, NoParams, DT),
            M(DT, "UtcNow", NativeBclMemberKind.StaticProperty, NoParams, DT),
            M(DT, "Today", NativeBclMemberKind.StaticProperty, NoParams, DT),
            M(DT, "MinValue", NativeBclMemberKind.StaticProperty, NoParams, DT),
            M(DT, "MaxValue", NativeBclMemberKind.StaticProperty, NoParams, DT),
            M(DT, "Parse", NativeBclMemberKind.StaticMethod, P(1), DT),
            M(DT, "IsLeapYear", NativeBclMemberKind.StaticMethod, P(1), "Boolean"),
            M(DT, "DaysInMonth", NativeBclMemberKind.StaticMethod, P(2), "Integer"),
            M(DT, CtorName, NativeBclMemberKind.Constructor, P(3, 6), DT),
            M(DT, "Year", NativeBclMemberKind.Property, NoParams, "Integer"),
            M(DT, "Month", NativeBclMemberKind.Property, NoParams, "Integer"),
            M(DT, "Day", NativeBclMemberKind.Property, NoParams, "Integer"),
            M(DT, "Hour", NativeBclMemberKind.Property, NoParams, "Integer"),
            M(DT, "Minute", NativeBclMemberKind.Property, NoParams, "Integer"),
            M(DT, "Second", NativeBclMemberKind.Property, NoParams, "Integer"),
            M(DT, "Millisecond", NativeBclMemberKind.Property, NoParams, "Integer"),
            // v1 divergence (spec §5): Integer, not the .NET enums; numeric
            // values match .NET exactly (Sunday=0…Saturday=6; Unspecified=0,
            // Utc=1, Local=2), so a later native-enum upgrade is
            // value-compatible. The flag drives the C# backend's (int) cast.
            M(DT, "DayOfWeek", NativeBclMemberKind.Property, NoParams, "Integer", intCast: true),
            M(DT, "Kind", NativeBclMemberKind.Property, NoParams, "Integer", intCast: true),
            M(DT, "DayOfYear", NativeBclMemberKind.Property, NoParams, "Integer"),
            M(DT, "Ticks", NativeBclMemberKind.Property, NoParams, "Long"),
            M(DT, "Date", NativeBclMemberKind.Property, NoParams, DT),
            M(DT, "AddDays", NativeBclMemberKind.InstanceMethod, P(1), DT),
            M(DT, "AddHours", NativeBclMemberKind.InstanceMethod, P(1), DT),
            M(DT, "AddMinutes", NativeBclMemberKind.InstanceMethod, P(1), DT),
            M(DT, "AddSeconds", NativeBclMemberKind.InstanceMethod, P(1), DT),
            M(DT, "AddMilliseconds", NativeBclMemberKind.InstanceMethod, P(1), DT),
            M(DT, "AddMonths", NativeBclMemberKind.InstanceMethod, P(1), DT),
            M(DT, "AddYears", NativeBclMemberKind.InstanceMethod, P(1), DT),
            M(DT, "AddTicks", NativeBclMemberKind.InstanceMethod, P(1), DT),
            M(DT, "Add", NativeBclMemberKind.InstanceMethod, P(1), DT),
            // The surface carries ONE return type per member name, so
            // Subtract is pinned to the dt.Subtract(dt) overload → TimeSpan
            // (mirroring the dt - dt operator row); dt - ts is the operator.
            M(DT, "Subtract", NativeBclMemberKind.InstanceMethod, P(1), TS),
            M(DT, "ToLocalTime", NativeBclMemberKind.InstanceMethod, NoParams, DT),
            M(DT, "ToUniversalTime", NativeBclMemberKind.InstanceMethod, NoParams, DT),
            M(DT, "ToString", NativeBclMemberKind.InstanceMethod, P(0, 1), "String"),
            M(DT, "CompareTo", NativeBclMemberKind.InstanceMethod, P(1), "Integer"),

            // ---------------- TimeSpan ----------------
            M(TS, "FromDays", NativeBclMemberKind.StaticMethod, P(1), TS),
            M(TS, "FromHours", NativeBclMemberKind.StaticMethod, P(1), TS),
            M(TS, "FromMinutes", NativeBclMemberKind.StaticMethod, P(1), TS),
            M(TS, "FromSeconds", NativeBclMemberKind.StaticMethod, P(1), TS),
            M(TS, "FromMilliseconds", NativeBclMemberKind.StaticMethod, P(1), TS),
            M(TS, "FromTicks", NativeBclMemberKind.StaticMethod, P(1), TS),
            M(TS, "Parse", NativeBclMemberKind.StaticMethod, P(1), TS),
            M(TS, "Zero", NativeBclMemberKind.StaticProperty, NoParams, TS),
            M(TS, "MinValue", NativeBclMemberKind.StaticProperty, NoParams, TS),
            M(TS, "MaxValue", NativeBclMemberKind.StaticProperty, NoParams, TS),
            M(TS, CtorName, NativeBclMemberKind.Constructor, P(3, 4), TS),
            M(TS, "Days", NativeBclMemberKind.Property, NoParams, "Integer"),
            M(TS, "Hours", NativeBclMemberKind.Property, NoParams, "Integer"),
            M(TS, "Minutes", NativeBclMemberKind.Property, NoParams, "Integer"),
            M(TS, "Seconds", NativeBclMemberKind.Property, NoParams, "Integer"),
            M(TS, "Milliseconds", NativeBclMemberKind.Property, NoParams, "Integer"),
            M(TS, "TotalDays", NativeBclMemberKind.Property, NoParams, "Double"),
            M(TS, "TotalHours", NativeBclMemberKind.Property, NoParams, "Double"),
            M(TS, "TotalMinutes", NativeBclMemberKind.Property, NoParams, "Double"),
            M(TS, "TotalSeconds", NativeBclMemberKind.Property, NoParams, "Double"),
            M(TS, "TotalMilliseconds", NativeBclMemberKind.Property, NoParams, "Double"),
            M(TS, "Ticks", NativeBclMemberKind.Property, NoParams, "Long"),
            M(TS, "Add", NativeBclMemberKind.InstanceMethod, P(1), TS),
            M(TS, "Subtract", NativeBclMemberKind.InstanceMethod, P(1), TS),
            M(TS, "Negate", NativeBclMemberKind.InstanceMethod, NoParams, TS),
            M(TS, "Duration", NativeBclMemberKind.InstanceMethod, NoParams, TS),
            M(TS, "ToString", NativeBclMemberKind.InstanceMethod, P(0), "String"),
            M(TS, "CompareTo", NativeBclMemberKind.InstanceMethod, P(1), "Integer"),

            // ---------------- Guid ----------------
            // NO ToByteArray on the BL surface (spec §5: its Byte() return has
            // no pinned C++ mapping in v1; the native out-param form exists in
            // the runtime header only).
            M(GD, "NewGuid", NativeBclMemberKind.StaticMethod, P(0), GD),
            M(GD, "Parse", NativeBclMemberKind.StaticMethod, P(1), GD),
            M(GD, "Empty", NativeBclMemberKind.StaticProperty, NoParams, GD),
            M(GD, CtorName, NativeBclMemberKind.Constructor, P(1), GD),
            M(GD, "ToString", NativeBclMemberKind.InstanceMethod, P(0, 1), "String"),
            M(GD, "CompareTo", NativeBclMemberKind.InstanceMethod, P(1), "Integer"),

            // ---------------- StringBuilder ----------------
            // These rows are copied IDENTICALLY from
            // SemanticAnalyzer.GetCommonMethodReturnType's StringBuilder table
            // (spec §5: "exactly the surface GetCommonMethodReturnType already
            // types"); the analyzer fallback stays in place and must agree.
            M(SB, CtorName, NativeBclMemberKind.Constructor, P(0, 1), SB),
            M(SB, "Append", NativeBclMemberKind.InstanceMethod, P(1), SB),
            M(SB, "AppendLine", NativeBclMemberKind.InstanceMethod, P(0, 1), SB),
            M(SB, "AppendFormat", NativeBclMemberKind.InstanceMethod, P(2, 3, 4), SB),
            M(SB, "Insert", NativeBclMemberKind.InstanceMethod, P(2), SB),
            M(SB, "Remove", NativeBclMemberKind.InstanceMethod, P(2), SB),
            M(SB, "Replace", NativeBclMemberKind.InstanceMethod, P(2), SB),
            M(SB, "Clear", NativeBclMemberKind.InstanceMethod, P(0), SB),
            M(SB, "ToString", NativeBclMemberKind.InstanceMethod, P(0), "String"),
            M(SB, "Length", NativeBclMemberKind.Property, NoParams, "Integer"),
            M(SB, "Capacity", NativeBclMemberKind.Property, NoParams, "Integer"),

            // ---------------- Decimal ----------------
            M(DEC, "Round", NativeBclMemberKind.StaticMethod, P(1, 2), DEC),
            M(DEC, "Truncate", NativeBclMemberKind.StaticMethod, P(1), DEC),
            M(DEC, "Floor", NativeBclMemberKind.StaticMethod, P(1), DEC),
            M(DEC, "Ceiling", NativeBclMemberKind.StaticMethod, P(1), DEC),
            M(DEC, "Parse", NativeBclMemberKind.StaticMethod, P(1), DEC),
            M(DEC, "Compare", NativeBclMemberKind.StaticMethod, P(2), "Integer"),
            M(DEC, "MinValue", NativeBclMemberKind.StaticProperty, NoParams, DEC),
            M(DEC, "MaxValue", NativeBclMemberKind.StaticProperty, NoParams, DEC),
            M(DEC, "Zero", NativeBclMemberKind.StaticProperty, NoParams, DEC),
            M(DEC, "One", NativeBclMemberKind.StaticProperty, NoParams, DEC),
            M(DEC, "ToString", NativeBclMemberKind.InstanceMethod, P(0), "String"),
            M(DEC, "CompareTo", NativeBclMemberKind.InstanceMethod, P(1), "Integer"),

            // ---------------- DateTimeOffset ----------------
            M(DTO, "Now", NativeBclMemberKind.StaticProperty, NoParams, DTO),
            M(DTO, "UtcNow", NativeBclMemberKind.StaticProperty, NoParams, DTO),
            M(DTO, "FromUnixTimeSeconds", NativeBclMemberKind.StaticMethod, P(1), DTO),
            M(DTO, "FromUnixTimeMilliseconds", NativeBclMemberKind.StaticMethod, P(1), DTO),
            M(DTO, CtorName, NativeBclMemberKind.Constructor, P(1, 2), DTO),
            // The two divergent C++ member names (spec §5): a C++ member
            // cannot be named after its enclosing type, and Ticks collides
            // with the runtime struct's field naming — pinned here so Task 9
            // codegen maps by table, not by name convention.
            M(DTO, "DateTime", NativeBclMemberKind.Property, NoParams, DT, cppName: "ClockDateTime"),
            M(DTO, "UtcDateTime", NativeBclMemberKind.Property, NoParams, DT),
            M(DTO, "LocalDateTime", NativeBclMemberKind.Property, NoParams, DT),
            M(DTO, "Offset", NativeBclMemberKind.Property, NoParams, TS),
            M(DTO, "Ticks", NativeBclMemberKind.Property, NoParams, "Long", cppName: "TicksValue"),
            M(DTO, "ToOffset", NativeBclMemberKind.InstanceMethod, P(1), DTO),
            M(DTO, "ToUniversalTime", NativeBclMemberKind.InstanceMethod, NoParams, DTO),
            M(DTO, "ToLocalTime", NativeBclMemberKind.InstanceMethod, NoParams, DTO),
            M(DTO, "ToUnixTimeSeconds", NativeBclMemberKind.InstanceMethod, NoParams, "Long"),
            M(DTO, "ToUnixTimeMilliseconds", NativeBclMemberKind.InstanceMethod, NoParams, "Long"),
            M(DTO, "ToString", NativeBclMemberKind.InstanceMethod, P(0), "String"),
            M(DTO, "CompareTo", NativeBclMemberKind.InstanceMethod, P(1), "Integer"),
        };

        /// <summary>
        /// The spec §6.1 cross-type binary operator result table. Same-type
        /// ordering + equality exist for the four comparable value types
        /// (DateTime, TimeSpan, Decimal, DateTimeOffset — DateTimeOffset
        /// comparisons compare the UTC instant, a runtime semantic; the front
        /// end just types Boolean); Guid and StringBuilder carry '=' and '&lt;&gt;'
        /// ONLY (StringBuilder equality is shared_ptr reference equality on
        /// C++). Decimal-op-integral is NOT in the table — integral operands
        /// widen through GetCommonType's numeric machinery.
        /// </summary>
        public static readonly IReadOnlyList<NativeBclOperatorRow> Operators = BuildOperatorRows();

        /// <summary>Unary rows (spec §6.1): '-' on TimeSpan and Decimal only.</summary>
        public static readonly IReadOnlyList<NativeBclOperatorRow> UnaryOperators = new[]
        {
            new NativeBclOperatorRow(null, "-", TS, TS),
            new NativeBclOperatorRow(null, "-", DEC, DEC),
        };

        private static IReadOnlyList<NativeBclOperatorRow> BuildOperatorRows()
        {
            var rows = new List<NativeBclOperatorRow>
            {
                // Directional DateTime/TimeSpan arithmetic (spec §6.1 table).
                new NativeBclOperatorRow(DT, "-", DT, TS),
                new NativeBclOperatorRow(DT, "+", TS, DT),
                new NativeBclOperatorRow(DT, "-", TS, DT),
                new NativeBclOperatorRow(TS, "+", TS, TS),
                new NativeBclOperatorRow(TS, "-", TS, TS),
            };

            // Decimal same-type arithmetic (integral operands widen to Decimal
            // via GetCommonType before/without consulting these rows).
            foreach (var op in new[] { "+", "-", "*", "/", "Mod" })
                rows.Add(new NativeBclOperatorRow(DEC, op, DEC, DEC));

            // Same-type ordering + equality for the comparable value types.
            foreach (var type in new[] { DT, TS, DEC, DTO })
                foreach (var op in new[] { "=", "<>", "<", "<=", ">", ">=" })
                    rows.Add(new NativeBclOperatorRow(type, op, type, "Boolean"));

            // Equality ONLY for Guid and StringBuilder.
            foreach (var type in new[] { GD, SB })
                foreach (var op in new[] { "=", "<>" })
                    rows.Add(new NativeBclOperatorRow(type, op, type, "Boolean"));

            return rows;
        }

        /// <summary>The member-bearing surface type names (SByte is Bridged: absent).</summary>
        public static readonly IReadOnlyCollection<string> TypeNames =
            new HashSet<string>(Members.Select(m => m.TypeName), StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> TypeNameSet =
            new HashSet<string>(TypeNames, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Surface types that are NOT part of the numeric promotion machinery
        /// (everything but Decimal): these need the surface operator gates at
        /// all, while Decimal rides the IsNumeric/GetCommonType path.
        /// </summary>
        private static readonly HashSet<string> NonNumericTypeNameSet =
            new HashSet<string>(TypeNames.Where(t => !t.Equals(DEC, StringComparison.OrdinalIgnoreCase)),
                StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<(string Type, string Member), NativeBclMember> MemberIndex =
            BuildMemberIndex();

        private static Dictionary<(string, string), NativeBclMember> BuildMemberIndex()
        {
            var index = new Dictionary<(string, string), NativeBclMember>();
            foreach (var member in Members)
            {
                // Lower-cased composite key: lookups are case-insensitive.
                index[(member.TypeName.ToLowerInvariant(), member.MemberName.ToLowerInvariant())] = member;
            }
            return index;
        }

        private static readonly Dictionary<(string, string, string), string> OperatorIndex =
            Operators.ToDictionary(
                r => (r.LeftTypeName.ToLowerInvariant(), r.Op, r.RightTypeName.ToLowerInvariant()),
                r => r.ResultTypeName);

        public static bool IsSurfaceType(string typeName)
            => typeName != null && TypeNameSet.Contains(Normalize(typeName));

        /// <summary>Surface type other than Decimal (see NonNumericTypeNameSet).</summary>
        public static bool IsNonNumericSurfaceType(string typeName)
            => typeName != null && NonNumericTypeNameSet.Contains(Normalize(typeName));

        public static bool TryGetMember(string typeName, string memberName, out NativeBclMember member)
        {
            member = null;
            if (typeName == null || memberName == null) return false;
            return MemberIndex.TryGetValue(
                (Normalize(typeName).ToLowerInvariant(), memberName.ToLowerInvariant()), out member);
        }

        /// <summary>
        /// Declared return type of a surface member reachable in member-access
        /// position (constructors are excluded — they are keyed ".ctor" and
        /// never collide with member names anyway).
        /// </summary>
        public static bool TryGetMemberReturnType(string typeName, string memberName, out string returnTypeName)
        {
            returnTypeName = null;
            if (!TryGetMember(typeName, memberName, out var member) ||
                member.Kind == NativeBclMemberKind.Constructor)
            {
                return false;
            }
            returnTypeName = member.ReturnTypeName;
            return true;
        }

        public static bool RequiresCSharpIntCast(string typeName, string memberName)
            => TryGetMember(typeName, memberName, out var member) && member.RequiresCSharpIntCast;

        /// <summary>
        /// Look up the spec §6.1 result of <c>left op right</c>. Accepts the
        /// analyzer's operator spellings (== / != / IsEqual / %) and
        /// normalizes them to the canonical table tokens.
        /// </summary>
        public static bool TryGetBinaryOperatorResult(string leftTypeName, string op, string rightTypeName,
            out string resultTypeName)
        {
            resultTypeName = null;
            if (leftTypeName == null || op == null || rightTypeName == null) return false;
            return OperatorIndex.TryGetValue(
                (Normalize(leftTypeName).ToLowerInvariant(), NormalizeOp(op), Normalize(rightTypeName).ToLowerInvariant()),
                out resultTypeName);
        }

        /// <summary>Unary '-' rows only (spec §6.1): TimeSpan and Decimal.</summary>
        public static bool TryGetUnaryOperatorResult(string op, string operandTypeName, out string resultTypeName)
        {
            resultTypeName = null;
            if (op == null || operandTypeName == null) return false;
            var normalizedType = Normalize(operandTypeName);
            var row = UnaryOperators.FirstOrDefault(r =>
                r.Op == op && r.RightTypeName.Equals(normalizedType, StringComparison.OrdinalIgnoreCase));
            if (row == null) return false;
            resultTypeName = row.ResultTypeName;
            return true;
        }

        /// <summary>
        /// Strips a System-namespace qualifier ("System.DateTime" /
        /// "System.Text.StringBuilder" → last segment) so both spellings hit
        /// the table; anything else passes through untouched.
        /// </summary>
        private static string Normalize(string typeName)
        {
            if (typeName.StartsWith("System.", StringComparison.OrdinalIgnoreCase))
            {
                var lastDot = typeName.LastIndexOf('.');
                return typeName.Substring(lastDot + 1);
            }
            return typeName;
        }

        private static string NormalizeOp(string op) => op switch
        {
            "==" or "IsEqual" => "=",
            "!=" => "<>",
            "%" => "Mod",
            _ => op,
        };
    }
}
