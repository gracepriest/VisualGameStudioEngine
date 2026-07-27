# P1: Native BCL Types for the C++ Backend — Design

**Date:** 2026-07-27
**Status:** Draft, pre-review
**Phase:** P1 of the contract → P1 → P2 sequence
(contract: `2026-07-26-dotnet-native-boundary-contract-design.md`, implemented `9d805ca`→`18af614`)

## 1. Goal and scope

Give BasicLang Native (BL+C++) projects real, pure-C++ implementations of the
BCL types the C++ backend rejects today, so `Dim d As DateTime`,
`d.AddDays(1).Year`, Decimal money math, `Guid.NewGuid()`, and `StringBuilder`
chaining all work when compiling to native — with no managed runtime involved
(that is P2's job).

**Types:** `DateTime`, `TimeSpan`, `Guid`, `StringBuilder`, `Decimal`,
`SByte`, and `DateTimeOffset` (the contract spec requires P1 to assign its
category; it is `NativeOwned` alongside `DateTime`).

**User decisions recorded (2026-07-27):**
1. **Decimal is faithful in P1** — real 96-bit base-10 semantics, not a
   double-backed approximation and not deferred.
2. **Unknown members produce clean BasicLang diagnostics** — a member-level
   capability check, not String-style raw C++ passthrough errors.
3. **DateTime local time is OS-backed** — `localtime`/`mktime`-family
   conversions; no C++20 `<chrono>` tzdb dependency.
4. **The VB-style global date functions and `NewGuid()` are wired on the C++
   backend in P1**, closing the current C#-only asymmetry.
5. **Approach A** — one single-source member-surface table feeding checker,
   typing, codegen, and tests (the boundary-contract philosophy applied to
   members).

## 2. Registry changes (contract C1)

| Type | Today | After P1 |
|---|---|---|
| DateTime, TimeSpan, Guid, StringBuilder, Decimal, DateTimeOffset | Rejected | **NativeOwned** |
| SByte | Rejected | **Bridged** (`int8_t`) |
| Object, Regex, Uri, Stream, FileInfo, DirectoryInfo | Rejected | Rejected (unchanged; P2 territory) |

- SByte is a plain primitive: it joins `CppTypeMapper._typeMap` as
  `SByte → int8_t`. The mechanical mapper invariant **keeps its exact form**
  (`_typeMap` keys == Bridged + Object) — Bridged just grows by one.
- NativeOwned types **never enter `_typeMap`**. `CppCodeGenerator.MapType`
  handles them by registry category (section 6), so the invariant stays clean.
- **Byte signedness fix rides along.** Today the C++ backend maps `Byte` to
  *signed* `int8_t` while the live C# backend maps it to *unsigned* `byte`
  (the .NET semantics) — a live cross-backend divergence
  (`TypeMapper.cs:217` vs `CSharpBackend.cs:136`). P1 changes the C++ mapping
  to `Byte → uint8_t`; `SByte → int8_t` is the signed one. The parallel
  `CppCodeGenerator.MapTypeName` channel (which already says
  `byte → uint8_t`) and the default-value switches are reconciled in the same
  change.
- The registry doc comment ("SByte and Decimal are NOT mapped by CppTypeMapper
  and must stay Rejected") is rewritten to describe the post-P1 invariant.
- `CppCapabilityChecker.CheckType` gains an explicit
  `category == NativeOwned → accept` branch. **The registry flip alone is NOT
  sufficient** — without the branch, NativeOwned class-kind types still hit
  the final unknown-class rejection, and primitive-kind ones silently
  miscompile (verified during recon).

## 3. Type designs

All native implementations live in `namespace BasicLang` and use .NET member
names, so instance calls dispatch through the backend's existing raw-passthrough
emission (the collections precedent).

| Type | C++ shape | Semantics |
|---|---|---|
| `BasicLang::DateTime` | struct; one `uint64_t _dateData` — low 62 bits ticks (100 ns since 0001-01-01, proleptic Gregorian), top 2 bits Kind (Unspecified=0, Utc=1, Local=2). .NET's exact layout. | value |
| `BasicLang::TimeSpan` | struct; one `int64_t _ticks` | value |
| `BasicLang::Guid` | struct; 16 bytes in .NET's field layout (`int32 _a; int16 _b; int16 _c; uint8 _d.._k`) | value |
| `BasicLang::Decimal` | struct `{uint32_t lo, mid, hi; uint32_t flags}` — .NET's GetBits layout (scale in flags bits 16–23, sign bit 31) | value |
| `BasicLang::DateTimeOffset` | struct `{DateTime utcDateTime; int16_t offsetMinutes}` (offset ±14:00, whole minutes) | value |
| `BasicLang::StringBuilder` | class over `std::string`; **reference type**: mapped as `std::shared_ptr<BasicLang::StringBuilder>`; inherits `std::enable_shared_from_this`; `Append`-family returns `shared_from_this()` so chains emit uniformly with `->` | reference |

- The six value types define C++ operator overloads (`+ - * /` where
  applicable, `== != < <= > >=`) so BL arithmetic and comparisons lower
  through the normal binary-op path with zero codegen special-casing.
  DateTime/TimeSpan cross-type operators follow .NET: `dt + ts → DateTime`,
  `dt - dt → TimeSpan`, `dt - ts → DateTime`.
- DateTime semantics rules preserved from .NET: arithmetic and comparison
  operate on ticks only (Kind is metadata); `AddMonths`/`AddYears` are
  calendar ops with day clamping (Jan 31 + 1 month = Feb 28/29);
  `IsLeapYear`/`DaysInMonth` implement the Gregorian rules.
- DateTimeOffset equality/ordering compare the **UTC instant** (the .NET
  rule): `10:00+02:00 == 09:00+01:00`.
- Guid `NewGuid()` is UUID v4 from the **OS CSPRNG** (`BCryptGenRandom` on
  Windows, `getrandom`/`/dev/urandom` elsewhere) — never `rand()`/`mt19937`.
  Version/variant bits forced per RFC 4122.
- StringBuilder is the only reference type; two BL variables holding the same
  builder observe each other's mutations, exactly as in .NET.

## 4. `NativeBclSurface` — the single source for members

New file `BasicLang/NativeBclSurface.cs`: for each P1 type, a table of
implemented members — `(Name, MemberKind, Arity/Overloads, ReturnTypeName)`
with `MemberKind ∈ {InstanceMethod, Property, StaticMethod, StaticProperty,
Constructor, Operator}`.

**One table, four consumers:**

1. **Member-level capability check** — a new pass in `CppCapabilityChecker`
   walks member calls, property reads, and `New` expressions whose receiver
   type is NativeOwned; an unknown member produces
   `'DateTime' has no native member 'ToBinary' on the C++ backend`.
   The same pass extends coverage to **expression-position temporaries**,
   closing the pre-existing leak where Rejected types used only in expression
   position slipped past the checker and died as raw C++ errors (BL6006).
2. **Compile-path typing** — the table backs
   `SemanticAnalyzer.LookupNetTypeMember` / `GetCommonMethodReturnType`, so
   member chains type correctly on the compile path (today every BCL member
   types as `Object` outside the LSP). The LSP chain-typing inherits this
   automatically through the existing `ResolveNetMemberType` hook — no LSP
   fork.
3. **Codegen dispatch** — the Property entries drive the existing
   field-access→method bridge (`.Year` → `.Year()`, generalizing the
   collections' Count/Keys/Values rewrite); the Static entries drive a
   name-keyed static-dispatch table (`DateTime.Now` →
   `BasicLang::DateTime::Now()`), replacing the one-off `IsDateTimeNowAccess`
   machinery; Constructor entries drive `New` lowering (value construction
   for value types, `std::make_shared` for StringBuilder — decided by
   registry category + surface data, **not** by the analyzer's synthetic
   `TypeKind.Class`, which is wrong for these types).
4. **Drift tests** — every NativeOwned registry entry has a surface entry and
   vice versa (mechanical, like the mapper invariant).

## 5. v1 member surfaces

Curated (~10–15 members per type). Anything outside these lists produces the
clean member diagnostic — adding a member later is additive.

- **DateTime**: statics `Now`, `UtcNow`, `Today`, `Parse`, `TryParse`,
  `IsLeapYear`, `DaysInMonth`, `MinValue`, `MaxValue`; ctor
  `(y,m,d)` / `(y,m,d,h,mi,s)`; properties `Year Month Day Hour Minute Second
  Millisecond DayOfWeek DayOfYear Ticks Kind Date`; methods
  `AddDays AddHours AddMinutes AddSeconds AddMilliseconds AddMonths AddYears
  AddTicks Add Subtract ToLocalTime ToUniversalTime ToString([format])
  CompareTo`; operators as in section 3.
- **TimeSpan**: statics `FromDays FromHours FromMinutes FromSeconds
  FromMilliseconds FromTicks Parse TryParse Zero MinValue MaxValue`; ctor
  `(h,m,s)` / `(d,h,m,s)`; properties `Days Hours Minutes Seconds Milliseconds
  TotalDays TotalHours TotalMinutes TotalSeconds TotalMilliseconds Ticks`;
  methods `Add Subtract Negate Duration ToString CompareTo`; operators.
  The double-based `From*` factories round to the nearest millisecond (the
  documented .NET behavior); `FromTicks` is exact.
- **Guid**: statics `NewGuid Parse TryParse Empty`; ctor `(String)`; methods
  `ToString([format: D|N|B|P]) ToByteArray CompareTo`; operators `== !=`.
  Default `ToString` = lowercase "D". `ToByteArray` is pinned to .NET's
  mixed-endian layout (`_a,_b,_c` little-endian, `_d.._k` verbatim).
- **StringBuilder**: ctor `()` / `(String)`; methods `Append AppendLine
  AppendFormat Insert Remove Replace Clear ToString` (Append-family returns
  the same builder for chaining); properties `Length Capacity`. This is
  exactly the surface `SemanticAnalyzer.GetCommonMethodReturnType` already
  types.
- **Decimal**: operators `+ - * / Mod == != < <= > >=`; statics
  `Round(d[,digits]) Truncate Floor Ceiling Parse TryParse Compare MinValue
  MaxValue Zero One`; methods `ToString CompareTo`; conversions per
  section 10.
- **DateTimeOffset**: statics `Now UtcNow FromUnixTimeSeconds
  FromUnixTimeMilliseconds`; ctors `(DateTime)` / `(DateTime, TimeSpan)`;
  properties `DateTime UtcDateTime LocalDateTime Offset Ticks`; methods
  `ToOffset ToUniversalTime ToLocalTime ToUnixTimeSeconds
  ToUnixTimeMilliseconds ToString CompareTo`; operators (UTC-instant
  comparison).
- **SByte**: Bridged primitive — no surface entries; arithmetic and
  conversions are ordinary `int8_t` operations.

`DateTimeOffset` is added to `SemanticAnalyzer.CommonNetTypes` and
`IRBuilder.KnownNetStaticTypes` (it is absent from both today).

## 6. Compiler changes

- **`CppCapabilityChecker`**: NativeOwned accept branch in `CheckType`; the
  new member-surface pass (section 4.1). Keep its hand-mirrored walk in sync
  with `ModuleTypeWalker` per the existing keep-in-sync comment.
- **`CppCodeGenerator.MapType`**: NativeOwned category → emit
  `BasicLang::<Name>` as a value type, EXCEPT StringBuilder →
  `std::shared_ptr<BasicLang::StringBuilder>`. Decided by name/category, not
  `TypeKind` (the analyzer's synthetic `Class` kind must not trigger the
  generic `shared_ptr` wrap).
- **`Visit(IRNewObject)`**: NativeOwned value types → value construction
  (`result = BasicLang::DateTime(args);`, the user-`Structure` path);
  StringBuilder → `std::make_shared`.
- **`MemberAccessOp`**: value P1 types → `.`; StringBuilder → `->`.
- **Static dispatch**: `IRFieldAccess`/`IRCall` on a receiver that is a
  NativeOwned type NAME (statics arrive as field access on an `IRVariable`
  literally named e.g. "DateTime") routes through the surface table to
  `BasicLang::<Type>::<Member>(...)`.
- **Shim dismantling**: remove the `std::time_t` DateTime machinery —
  `_dateTimeValues`, `IsDateTimeNowAccess`, the `EmitToStringShim` datetime
  case, `MapTypeName["datetime"] → std::time_t`, and
  `CppRuntimeSources.DotNetSurfaceHelpers`' `Now`/`FormatTime` (the .NET
  format-token → strftime conversion logic is REUSED inside
  `BasicLang::DateTime::ToString`).
- **`EmitToStringShim`**: P1 value types route `ToString` to the native
  member; numerics/Boolean/String behavior unchanged.
- **`MapTypeName` / default-value switches**: entries added for the seven
  types; the `byte` signedness entry reconciled (section 2).

## 7. VB stdlib on the C++ backend

`CppStdLib` gains the date category mirroring `CSharpStdLib`'s list:
`Now() Today() Year(d) Month(d) Day(d) Hour(d) Minute(d) Second(d)
DateAdd(part, n, d) DateDiff(part, a, b) FormatDate(d, fmt)` — each a
one-line emission onto `BasicLang::DateTime`. `NewGuid()` emits
`BasicLang::Guid::NewGuid().ToString()` (String return, matching the existing
analyzer registration). Interval-part strings for `DateAdd`/`DateDiff` match
the C# backend's accepted set.

## 8. Conversion pairs (contract C1 obligation)

The value representation each type presents at the P2 boundary — part of the
type's definition, pinned here:

| Type | Boundary representation |
|---|---|
| DateTime | `uint64` dateData (ticks &#124; kind<<62) — .NET's internal layout |
| TimeSpan | `int64` ticks |
| Guid | 16 bytes in .NET `ToByteArray` order (mixed-endian pinned) |
| Decimal | `int32[4]` in .NET `GetBits` order `{lo, mid, hi, flags}` |
| SByte | `int8` |
| DateTimeOffset | `{int64 utcTicks, int16 offsetMinutes}` |
| StringBuilder | UTF-8 string **snapshot** — explicitly lossy: a copy crosses; mutations never propagate across the boundary |

## 9. Culture and encoding rules

- All `ToString`/`Parse` surfaces are pinned to **invariant culture**.
  Defaults: DateTime `ToString()` uses the invariant "G" pattern
  (`MM/dd/yyyy HH:mm:ss`); TimeSpan uses "c" (already invariant in .NET);
  Guid "D" lowercase; Decimal invariant numeric. Divergence from .NET's
  current-culture defaults is documented, not emulated.
- `DateTime.Parse` v1 accepts the invariant round-trip ("O"), sortable ("s"),
  and invariant "G"/date-only forms — not .NET's full culture-flexible parse.
- StringBuilder `Length`/`Insert`/`Remove` indices count **UTF-8 bytes**,
  consistent with the backend's `std::string`-based String. Divergence from
  .NET's UTF-16 code-unit counts on non-ASCII text is documented.
- Local time (`Now`, `ToLocalTime`, `ToUniversalTime`, `Today`) uses the OS
  conversion functions (`localtime_s`/`mktime` family). DST for contemporary
  dates is handled by the OS; historic-date DST fidelity is out of scope.

## 10. Decimal implementation

`CppDecimalRuntime.cs` — the one from-scratch numeric engine:

- Representation: `{uint32 lo, mid, hi; uint32 flags}` (96-bit unsigned
  significand; scale 0–28; sign). `GetBits`-compatible.
- **Add/Sub**: align scales by ×10ᵏ rescaling with 192-bit intermediates
  (uint64 limb arithmetic); result scale = max(operand scales)
  (`1.1 + 2.25 = 3.35`); overflow of the 96-bit significand drops excess
  digits with round-half-even, else throws when unrepresentable.
- **Mul**: 96×96→192-bit product; scale = sum of scales; excess digits
  rounded away (round-half-even). `12.0 * 10 = 120.00` (scale 2).
- **Div**: long division to up to 28–29 significant digits, last digit
  rounded; divide-by-zero throws.
- **Round** defaults to banker's rounding (`MidpointRounding.ToEven`);
  `Truncate`/`Floor`/`Ceiling` per .NET.
- **ToString** is scale-preserving (`1.10` prints `"1.10"`); **Parse**
  preserves scale and round-trips (`Parse(x.ToString()) == x` including
  scale).
- Equality is value-based across scales (`1.0 == 1.00` is true) and
  consistent with ordering.
- **Conversions**: from int/Long/Integer — exact, implicit; double ↔ Decimal
  follows .NET's observable rounding behavior. The exact .NET rule (and how
  `Dim d As Decimal = 1.5` lowers on BOTH backends today) is a
  **planning-stage verification item**; whatever the rule, the cross-backend
  parity tests pin it (section 12).

## 11. Error handling

Native types throw C++ exceptions carrying .NET-style messages: arithmetic
overflow, Decimal divide-by-zero, invalid `Parse` input, out-of-range ctor
args (month 13, offset > 14 h), StringBuilder index out of range. They flow
through the backend's existing Try/Catch machinery exactly as collection
errors do today; no new exception plumbing. The backend's known
Return-inside-Try/Finally limitation is unaffected.

## 12. Delivery, emission, and testing

**Delivery**: `CppBclRuntime` + `CppDecimalRuntime` sources are spliced into
`BasicLangRuntime.g.h` in BOTH emission modes (split `EmitRuntimeHeader` AND
combined `Generate` — the dual-mode wiring is the classic drift trap; both
modes get tests). Emission may be conditional on use via the existing
`ModuleTypeWalker` scan pattern (like collections) — the plan decides
per-mode consistency; correctness must not depend on it.

**Testing — four layers plus the parity oracle:**
1. **Fast drift tests**: surface ↔ registry coherence both directions;
   runtime-source content pins; mapper invariant (unchanged formula) stays
   green with SByte added.
2. **Native runtime tests** (Integration): compile the C++ constants directly
   via `CppCompile.CompileAndRun` and hammer behavior (the collections
   "emitted == tested" pattern). Decimal gets a dedicated vector battery:
   scale propagation, banker's rounding, `0.1 + 0.2 == 0.3`, `19.99 * 100`,
   thousand-iteration cent-accumulation, ToString/Parse round-trips incl.
   scale, division digit counts, overflow throws. DateTime gets calendar
   vectors (leap years, month clamping, DayOfWeek/DayOfYear, tick
   round-trips); Guid gets format/parse/byte-order vectors and a v4
   version/variant check.
3. **BL end-to-end** (Integration): BL programs per type through
   `CompileToCppOptimized` + compile-and-run with exact stdout asserts, plus
   CLI (`BasicLang.exe --target=cpp`) coverage — honoring the repo law that
   codegen is validated through the optimizer AND the CLI.
4. **Member-diagnostic tests**: unknown member on each type → the clean
   BL-level error; Rejected-type expression-position leak now caught.
5. **Cross-backend parity oracle**: the same BL programs run through the C#
   backend (real .NET) and the C++ backend must print byte-identical output —
   DateTime arithmetic/formatting, Decimal money loops, Guid string
   round-trips, StringBuilder chains. Divergence from .NET semantics shows up
   as a diff with no hand-computed expectations.

**Known test churn** (updated deliberately, TDD): `BoundaryTypeRegistryTests`
Rejected pins shrink; `NativeOwnedAndManagedOwned_StartEmpty_PreP1` is
replaced by populated-set pins; `CategorizeIsCaseInsensitive` re-targets a
still-rejected name; 5 `*_StillRejected` tests in `CppCollectionTests` swap
to still-rejected types (`Regex`/`Stream`); `CppBackendTests`'
`Cpp_InterfaceReturn_FuncOfUnmappedArg_ThrowsCapabilityError` re-targets;
`Cpp_ConsoleTemplateSurface_LowersToValidCpp` re-pins to the real-DateTime
lowering. The registry doc comment is rewritten.

## 13. Out of scope

- LSP completion filtering (reflection currently offers the full .NET member
  surface; builds fail cleanly via the member check; filtering completions by
  the surface table is a later nicety).
- Historic-date DST/timezone-database fidelity; named time zones.
- UTF-16 length semantics for String/StringBuilder.
- `Regex`, `Uri`, `Stream`, `FileInfo`, `DirectoryInfo` — stay Rejected
  (P2 candidates via the managed boundary).
- .NET's culture-sensitive formatting/parsing surfaces.
- P2 boundary code generation (the conversion pairs here are its contract).

## 14. Open items for planning-stage verification

1. How `Dim d As Decimal = 1.5` lowers today on the C# backend (literal
   suffix? conversion?) — pin the C++ lowering to match observable behavior.
2. The exact .NET double→Decimal rounding rule (verify against real .NET
   output; the parity tests enforce whatever it is).
3. `DateAdd`/`DateDiff` interval-part string set accepted by the C# backend.
4. Whether conditional emission (UsesBclTypes scan) is worth it vs
   unconditional splice — measure compile-time impact of the headers.
5. `Byte → uint8_t` blast radius: grep C++-backend tests for `Byte`
   expectations pinned to `int8_t`.

## 15. Files touched (summary)

Create: `BasicLang/NativeBclSurface.cs`,
`BasicLang/Compiler/CodeGen/CPlusPlus/CppBclRuntime.cs`,
`BasicLang/Compiler/CodeGen/CPlusPlus/CppDecimalRuntime.cs`, new test files
(`VisualGameStudio.Tests/Blnet/` or `Compiler/` per plan).
Modify: `BoundaryTypeRegistry.cs`, `CppCapabilityChecker.cs`,
`TypeMapper.cs` (SByte/Byte), `CppCodeGenerator.cs` (+`.Split.cs`),
`CppRuntimeSources.cs` (shim removal), `StdLib/CppStdLib.cs`,
`SemanticAnalyzer.cs` (surface-backed typing + DateTimeOffset),
`IRBuilder.cs` (KnownNetStaticTypes), enumerated existing tests.
