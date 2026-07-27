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
- **Three type-name channels, not two.** Besides `CppTypeMapper._typeMap` and
  `CppCodeGenerator.MapTypeName`, the code generator has its OWN inherited
  `_typeMap` (`CppCodeGenerator.InitializeTypeMap`, ~lines 1744–1766) — and
  it is the LIVE channel `MapType` consults. It already contains
  `"Decimal" → "long double"` (a double-backed approximation that would
  silently defeat user decision 1) plus `Byte → uint8_t` / `SByte → int8_t`
  (already correct there). P1 REMOVES the `Decimal` entry, and the new
  NativeOwned branch in `MapType` is checked STRICTLY BEFORE this map.
- The C# backend's conversion channel is reconciled too:
  `CSharpBackend.ConvertMethodForType` maps `Byte → Convert.ToSByte` (the C#
  backend is internally split on Byte's signedness). It becomes
  `Byte → Convert.ToByte`, `SByte → Convert.ToSByte`. (A fourth, DORMANT
  channel exists: `CSharpTypeMapper` in TypeMapper.cs maps `Byte → sbyte` /
  `Decimal → decimal`; verified never consulted by any caller, but its Byte
  entry is fixed to `byte` in passing so no dead contradiction ships.)
- The parent contract spec's C1 example rows list SByte under
  NativeOwned-after-P1; those examples are updated to match this spec
  (SByte = Bridged) in the same change.
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

- The five value structs define C++ operator overloads (`+ - * / %` where
  applicable, unary `-` on TimeSpan/Decimal, `++`/`--` on Decimal,
  `== != < <= > >=`) so BL arithmetic and comparisons lower through the
  normal binary/unary-op paths with zero codegen special-casing.
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

**One table, five consumers:**

1. **Member-level capability check** — a new pass in `CppCapabilityChecker`
   walks member calls, property reads, and `New` expressions whose receiver
   type is NativeOwned; an unknown member produces
   `'DateTime' has no native member 'ToBinary' on the C++ backend`.
   The same pass extends coverage to **expression-position temporaries**,
   closing the pre-existing leak where Rejected types used only in expression
   position slipped past the checker and died as raw C++ errors (BL6006).
2. **Compile-path typing** — the table backs
   `SemanticAnalyzer.LookupNetTypeMember` / `GetCommonMethodReturnType`, so
   member chains type correctly on the compile path (today DateTime /
   TimeSpan / Guid / Decimal members degrade to `Object` outside the LSP;
   String / StringBuilder / collections already have fallback tables). The
   surface table is consulted FIRST for the seven P1 type names — before the
   LSP's reflection `TypeRegistry` — so compile-path and LSP answers are
   identical for P1 types; reflection continues to serve everything else.
3. **Front-end operator typing** — the table's Operator entries feed binary /
   unary operator validation and result typing (section 6.1) so P1-type
   arithmetic passes semantic analysis at all.
4. **Codegen dispatch** — the Property entries drive the existing
   field-access→method bridge (`.Year` → `.Year()`, generalizing the
   collections' Count/Keys/Values rewrite); the Static entries drive a
   name-keyed static-dispatch table (`DateTime.Now` →
   `BasicLang::DateTime::Now()`), replacing the one-off `IsDateTimeNowAccess`
   machinery; Constructor entries drive `New` lowering (value construction
   for value types, `std::make_shared` for StringBuilder — decided by
   registry category + surface data, **not** by the analyzer's synthetic
   `TypeKind.Class`, which is wrong for these types).
5. **Drift tests** — every NativeOwned registry entry has a surface entry and
   vice versa (mechanical, like the mapper invariant).

## 5. v1 member surfaces

Curated (~10–15 members per type). Anything outside these lists produces the
clean member diagnostic — adding a member later is additive.

- **DateTime**: statics `Now`, `UtcNow`, `Today`, `Parse`,
  `IsLeapYear`, `DaysInMonth`, `MinValue`, `MaxValue`; ctor
  `(y,m,d)` / `(y,m,d,h,mi,s)`; properties `Year Month Day Hour Minute Second
  Millisecond DayOfWeek DayOfYear Ticks Kind Date`; methods
  `AddDays AddHours AddMinutes AddSeconds AddMilliseconds AddMonths AddYears
  AddTicks Add Subtract ToLocalTime ToUniversalTime ToString([format])
  CompareTo`; operators as in section 3.
  **`DayOfWeek` and `Kind` return `Integer` in v1** (documented divergence:
  .NET returns the `DayOfWeek`/`DateTimeKind` enums; the numeric values match
  .NET exactly — Sunday=0…Saturday=6; Unspecified=0, Utc=1, Local=2 — so a
  later native-enum upgrade is value-compatible). Native BCL enum types are
  out of P1 scope (section 13). **C#-backend consequence pinned**: for
  surface members whose v1 type diverges from the real .NET member type
  (exactly these two), the C# backend emits an explicit `(int)` cast —
  without it csc fails CS0266 on the typed temp (a regression vs today's
  Object degrade), and `WriteLine(dt.DayOfWeek)` would print `Sunday` on C#
  vs `0` on C++ (a parity diff).
- **TimeSpan**: statics `FromDays FromHours FromMinutes FromSeconds
  FromMilliseconds FromTicks Parse Zero MinValue MaxValue`; ctor
  `(h,m,s)` / `(d,h,m,s)`; properties `Days Hours Minutes Seconds Milliseconds
  TotalDays TotalHours TotalMinutes TotalSeconds TotalMilliseconds Ticks`;
  methods `Add Subtract Negate Duration ToString CompareTo`; operators.
  The double-based `From*` factories round to the nearest millisecond (the
  documented .NET behavior); `FromTicks` is exact.
- **Guid**: statics `NewGuid Parse Empty`; ctor `(String)`; methods
  `ToString([format: D|N|B|P]) CompareTo`; operators `== !=`.
  Default `ToString` = lowercase "D". **`ToByteArray` is NOT on the BL v1
  surface** (its natural BL return, a `Byte()` array, has no pinned C++
  mapping in v1; the NATIVE out-param form `ToByteArray(uint8_t[16])` exists
  in the runtime header for tests and the §8 conversion pair, and the byte
  order stays pinned to .NET's mixed-endian layout — `_a,_b,_c`
  little-endian, `_d.._k` verbatim). A BL call to `ToByteArray` gets the
  clean unknown-member diagnostic.
- **StringBuilder**: ctor `()` / `(String)`; methods `Append AppendLine
  AppendFormat Insert Remove Replace Clear ToString` (Append-family returns
  the same builder for chaining); properties `Length Capacity`; operators
  `= <>` (shared_ptr reference equality — matching the 6.1 table). This is
  exactly the surface `SemanticAnalyzer.GetCommonMethodReturnType` already
  types.
- **Decimal**: operators `+ - * / Mod == != < <= > >=`; statics
  `Round(d[,digits]) Truncate Floor Ceiling Parse Compare MinValue
  MaxValue Zero One`; methods `ToString CompareTo`; conversions per
  section 10.
- **`TryParse` is dropped from ALL v1 surfaces** (DateTime, TimeSpan, Guid,
  Decimal). Its `out` parameter is inexpressible today: the surface-table
  shape carries no parameter direction, and neither backend's IR marks ByRef
  on BCL static calls — `DateTime.TryParse(s, d)` fails csc on the C# backend
  TODAY. `Parse` + `Try/Catch` covers the use case; TryParse returns when a
  ByRef/out story exists (section 13).
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

### 6.1 Front end: operator validation, result typing, conversions, literals

**This subsection exists because the semantic analyzer today HARD-REJECTS the
entire P1 operator surface on BOTH backends** (verified empirically):
`Visit(BinaryExpressionNode)` requires `IsNumeric()` operands for arithmetic
and ordering comparisons, and `TypeInfo.IsNumeric()` is a closed list
excluding Decimal, SByte, DateTime, and TimeSpan. `Dim c As Decimal = a + b`,
`d2 - d1`, `ts1 < ts2`, unary minus, and even `Dim a As Decimal = 1`
(IsAssignableFrom) all fail semantic analysis before any backend runs. The
front-end work below therefore fixes the C# backend too.

- **SByte becomes a first-class numeric primitive**: added to `IsNumeric()`,
  `IsIntegral()`, and `IsSigned()`; it participates in the existing integer
  promotion ladder like the other sized integers. `Byte` moves from
  `IsSigned()` to `IsUnsigned()` in the same change (the helpers currently
  have zero call sites, but shipping them contradicting Byte's new unsigned
  semantics would be a fresh drift trap).
- **Decimal joins `IsNumeric()`** with these promotion rules in
  `GetCommonType`: `Decimal op <any integral>` → Decimal (integrals widen to
  Decimal implicitly, matching .NET); `Decimal op Single/Double` → **compile
  error** requiring an explicit conversion (matching C#, which has no
  implicit double↔decimal in either direction). Assignment follows the same
  rules (`IsAssignableFrom`: integrals → Decimal yes; floating → Decimal no).
- **Operator validation consults the surface table's Operator entries** for
  NativeOwned operand pairs, with this cross-type result table (also used by
  `GetCommonType`/result typing):

  | Left | Op | Right | Result |
  |---|---|---|---|
  | DateTime | `-` | DateTime | TimeSpan |
  | DateTime | `+`/`-` | TimeSpan | DateTime |
  | TimeSpan | `+`/`-` | TimeSpan | TimeSpan |
  | (unary) `-` | | TimeSpan | TimeSpan |
  | Decimal | `+ - * / Mod` | Decimal (or integral, widened) | Decimal |
  | (unary) `-` | | Decimal | Decimal |
  | any P1 value type | `= <> < <= > >=` | same type | Boolean |
  | Guid / StringBuilder | `= <>` only | same type | Boolean |

  DateTimeOffset comparisons compare the UTC instant. Ordering operators on
  Guid/StringBuilder remain errors. All other P1-type operand combinations
  keep today's clean analyzer error. Integral-widening on Decimal is
  **symmetric** (`1 + d` is valid, like .NET) — unlike the deliberately
  directional DateTime rows. `++`/`--` on Decimal are accepted (they gate on
  `IsNumeric` in the analyzer's separate unary path) and the C++ struct
  provides `operator++`/`operator--` (pre/post, via ±1).
- **Compound assignment is a separate gate** (the analyzer validates `+=`
  `-=` etc. on its own path, NOT through `Visit(BinaryExpressionNode)`): it
  is wired to the SAME operator table — `x op= y` is legal iff `x op y` is
  legal AND the result type equals the target's type (`dt += ts` and
  `ts -= ts2` work; `dt += dt` errors). Without this wiring, `dt += ts`
  would keep failing with the misleading numeric-operands message after
  everything else ships.
- **`GetCommonType` wiring caveats** (it is an ordered ladder with no
  diagnostics channel): the Decimal branch is checked BEFORE the
  Double/Single/Long rungs (else `Decimal + Long` silently types Long); the
  `Decimal op Single/Double → error` is raised at the call site in
  `Visit(BinaryExpressionNode)`, not inside `GetCommonType`.
- **Decimal literals**: BasicLang has no `m` suffix. A numeric LITERAL in a
  **Decimal context** converts at COMPILE TIME using the literal's decimal
  text verbatim (no double round-trip). Decimal context = ALL of: `Dim`
  initializer, plain assignment, operand of an operator whose other operand
  is Decimal (`d * 1.08`, `total + 0.05`), argument to a Decimal parameter,
  `Return` in a Decimal function, and `For ... Step` against a Decimal loop
  variable — a literal is still a literal in operand position, and without
  this list the most common money pattern (`d * 1.08`) would be a hard
  error.
  **Pinned plumbing** (the literal's text is DISCARDED today at the parser —
  this rule is unimplementable without it): the lexeme is carried from the
  token onto `LiteralExpressionNode`; when Decimal context is established
  the analyzer converts the TEXT via `decimal.Parse(text,
  InvariantCulture)` and the IR constant carries a **`System.Decimal`
  value** (not a double). Consequences, both load-bearing: the
  `IROptimizer`'s `is double` constant-fold patterns then safely SKIP
  Decimal constants (folding them in double space would silently violate
  faithfulness on the optimizer-validated path); the C# backend emits
  `value.ToString(InvariantCulture) + "m"`, and the C++ backend emits an
  exact constant from the value's `GetBits`.
  **The explicit conversion is `CType(x, Decimal)`** — the only named escape
  for genuine non-literal Single/Double → Decimal. It is wired on BOTH
  backends (C#: `(decimal)x`; C++: the `Decimal(double)` converting ctor per
  section 10's rounding rule), and the `Decimal op Single/Double` analyzer
  error's hint names it.
- **Blast radius**: these are shared front-end changes; the full fast subset
  on the C# backend is the regression gate (section 14.1).

### 6.2 Capability checking and code generation

- **`CppCapabilityChecker`**: NativeOwned accept branch in `CheckType`; the
  new member-surface pass (section 4.1). Keep its hand-mirrored walk in sync
  with `ModuleTypeWalker` per the existing keep-in-sync comment.
- **`CppCodeGenerator.MapType`**: NativeOwned category → emit
  `BasicLang::<Name>` as a value type, EXCEPT StringBuilder →
  `std::shared_ptr<BasicLang::StringBuilder>`. Decided by name/category, not
  `TypeKind` (the analyzer's synthetic `Class` kind must not trigger the
  generic `shared_ptr` wrap). The NativeOwned branch is checked BEFORE the
  generator's own `_typeMap` (whose stale `Decimal → long double` entry is
  removed — section 2).
- **Console output lowering**: `Console.WriteLine(x)` lowers to
  `cout << x`, so the runtime header defines
  `operator<<(std::ostream&, const T&)` for the five NativeOwned value
  structs (delegating to `ToString()`), plus an overload for
  `std::shared_ptr<BasicLang::StringBuilder>` streaming its content
  (matching .NET's `WriteLine(sb)`). Without these, every WriteLine of a P1
  value is a raw C++ error — the class of failure this design forbids.
  String concatenation (`"x" & d`) is NOT extended in P1 (the non-string `&`
  gap is pre-existing and broader); it fails loudly at C++ compile — write
  `d.ToString()` — and is listed in section 13.
- **Hashing**: the runtime header defines `std::hash` specializations for the
  five NativeOwned value structs, consistent with `operator==`: DateTime
  hashes ticks only (Kind excluded, matching tick-based equality); Decimal
  hashes a scale-normalized canonical form (`1.0` and `1.00` must hash
  equal); Guid/TimeSpan hash their value bits; DateTimeOffset hashes the UTC
  instant. Without these, `Dictionary(Of Guid, ...)` — accepted by the
  checker post-P1 — dies as a raw C++ template error (a regression vs
  today's clean rejection). StringBuilder as a key uses shared_ptr identity
  (reference equality, .NET-consistent).
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
  types; the `byte` signedness entry reconciled (section 2). Default values
  pinned: zero-initialized structs give `DateTime.MinValue`, `TimeSpan.Zero`,
  `Guid.Empty`, `0D` (all matching .NET defaults); an unassigned StringBuilder
  is a null `shared_ptr` (= `Nothing`) — calling a member on it is a native
  null deref where .NET throws NullReferenceException (documented divergence,
  section 13).

## 7. VB stdlib on the C++ backend

The date category — `Now() Today() Year(d) Month(d) Day(d) Hour(d)
Minute(d) Second(d) DateAdd(d, interval, n) DateDiff(d1, d2, interval)
FormatDate(d, fmt)` — comes to the C++ backend. Signatures follow the
repo's EXISTING C# StdLib function-table signatures
(`StdLib/CSharpStdLib.cs`) verbatim: `DateAdd(DateTime, String,
Integer)` = (date, interval, number) and `DateDiff(DateTime, DateTime,
String)` = (date1, date2, interval) — NOT classic VB argument order.
`NewGuid()` emits `BasicLang::Guid::NewGuid().ToString()` (String return,
matching the existing analyzer registration). Interval-part strings match
the C# backend's accepted set (section 14.3).

**Mechanism (three coordinated pieces — `CppStdLib.cs` alone is dead code):**
1. The LIVE emission path is the hardcoded switch
   `CppCodeGenerator.EmitStdLibCall` — the date emissions land THERE
   (`StdLib/CppStdLib.cs` is consulted only by the CLI `--stdlib`
   support-matrix; it is updated too, so the matrix stops reporting the date
   category as unsupported).
2. The date functions get **analyzer registrations**
   (`RegisterStdLibFunction`) with real parameter/return types — today NONE
   of them are registered, so `Dim d = Now()` types as `Object` on BOTH
   backends (and `Object` maps to `void*` on C++). Registering them fixes
   typing for the C# backend as well.
3. C# emissions are unchanged (already live via `CSharpStdLibProvider`).

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
  Instants outside the OS conversion range (`mktime` fails pre-1970 on
  Windows) **throw** (ArgumentOutOfRange-style message) — never clamp or
  silently apply the current offset. The native tests carry a vector for it.

## 10. Decimal implementation

`CppDecimalRuntime.cs` — the one from-scratch numeric engine:

- Representation: `{uint32 lo, mid, hi; uint32 flags}` (96-bit unsigned
  significand; scale 0–28; sign). `GetBits`-compatible.
- **Add/Sub**: align scales by ×10ᵏ rescaling with 192-bit intermediates
  (uint64 limb arithmetic); result scale = max(operand scales)
  (`1.1 + 2.25 = 3.35`); overflow of the 96-bit significand drops excess
  digits with round-half-even, else throws when unrepresentable.
- **Mul**: 96×96→192-bit product; scale = sum of scales; excess digits
  rounded away (round-half-even). `12.0 * 10.0 = 120.00` (scale 1 + 1 = 2);
  `12.0 * 10 = 120.0` (scale 1 + 0 = 1, matching real .NET).
- **Div**: long division to up to 28–29 significant digits, last digit
  rounded; divide-by-zero throws.
- **Mod (remainder)**: takes the SIGN OF THE DIVIDEND (truncated division,
  the .NET rule — `3.5 Mod 1 = 0.5`, `-3.5 Mod 1 = -0.5`); result scale
  follows the max-scale rule like subtraction. The section 12 Decimal
  vector battery (testing layer 2) includes negative-dividend cases.
- **Unary negate** (`operator-()`) and **increment/decrement**
  (`operator++`/`operator--`, pre/post, via ±1) — required by 6.1's
  analyzer acceptance.
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
args (month 13, offset > 14 h), StringBuilder index out of range. **Every P1
runtime throw is `std::runtime_error` (or a subclass of it)** — never
`std::invalid_argument`/`std::out_of_range` (logic_error-derived), because
the backend lowers typed BL catches to `catch (std::runtime_error)`
(`MapCatchType`) and would miss them. They flow through the existing
Try/Catch machinery exactly as collection errors do today; no new exception
plumbing. Pre-existing limitation, unchanged: typed BL catches cannot
discriminate .NET exception kinds (FormatException vs OverflowException both
arrive as runtime_error) — the per-kind messages are observable, not
catch-selectable. The Return-inside-Try/Finally limitation is unaffected.

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
   **Parity-program discipline** (the C++ side is pinned to invariant
   culture per section 9, so undisciplined programs would diff by design):
   fixed literal dates/guids only — never `Now`/`Today`/`NewGuid` raw output
   (those are tested structurally: round-trips, ranges, version bits);
   format strings from the invariant-safe set; and the parity harness runs
   the C#-side program under forced `CultureInfo.InvariantCulture` (harness
   concern, not user-program syntax), eliminating machine-culture flakes.

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
- `TryParse` and any ByRef/out parameter on BCL static members (no IR ByRef
  marking exists for them on either backend).
- Native BCL enum types (`DayOfWeek`, `DateTimeKind` return `Integer` in v1
  with .NET-matching values).
- `Nullable` annotations on P1 types (`DateTime?` parses today but the C++
  backend ignores nullability; unchanged by P1).
- Extending `&` string concatenation to P1 types (pre-existing non-string
  concat gap; `"x" & d` fails loudly at C++ compile — use `d.ToString()`).
- Null-safety for `Nothing` StringBuilder receivers (native null deref;
  .NET throws NullReferenceException — documented divergence).
- P2 boundary code generation (the conversion pairs here are its contract).

## 14. Open items for planning-stage verification

1. **Front-end blast radius** (section 6.1 changes are shared by both
   backends): the full fast subset is the regression gate; additionally
   sweep for tests pinning today's "requires numeric operands" errors on
   these types. (Note: Decimal literals do NOT compile on the C# backend
   today — 6.1's literal rule is new behavior on both backends, not a match
   of existing C# behavior.)
2. The exact .NET double→Decimal explicit-conversion rounding rule (verify
   against real .NET output; the parity tests enforce whatever it is).
3. `DateAdd`/`DateDiff` interval-part string set accepted by the C# backend.
4. Whether conditional emission (UsesBclTypes scan) is worth it vs
   unconditional splice — measure compile-time impact of the headers.
5. `Byte → uint8_t` blast radius: grep C++-backend tests for `Byte`
   expectations pinned to `int8_t`.
6. `cout << int8_t/uint8_t` streams a CHARACTER, not a number. Verify how
   Byte console output behaves on the C++ backend today, and pin
   SByte/Byte `Console.WriteLine` to print numerically (`static_cast<int>`
   in the lowering) matching .NET.

## 15. Files touched (summary)

Create: `BasicLang/NativeBclSurface.cs`,
`BasicLang/Compiler/CodeGen/CPlusPlus/CppBclRuntime.cs`,
`BasicLang/Compiler/CodeGen/CPlusPlus/CppDecimalRuntime.cs`, new test files
(`VisualGameStudio.Tests/Blnet/` or `Compiler/` per plan).
Modify: `BoundaryTypeRegistry.cs`, `CppCapabilityChecker.cs`,
`TypeMapper.cs` (SByte/Byte incl. the dormant CSharpTypeMapper entry),
`SymbolTable.cs` (IsNumeric/IsIntegral/IsSigned/IsUnsigned/GetCommonType per
6.1), `BasicLangLexer.cs`/`Parser.cs`/`ASTNodes.cs` (literal lexeme carried
onto LiteralExpressionNode per 6.1), `IRNodes.cs`/`IRBuilder.cs`
(System.Decimal constant values; KnownNetStaticTypes), `IROptimizer.cs`
(verify `is double` folds skip Decimal constants — behavior, likely no code),
`CppCodeGenerator.cs` (+`.Split.cs`; incl. `EmitStdLibCall` date category,
`InitializeTypeMap` Decimal removal), `CppRuntimeSources.cs` (shim removal),
`StdLib/CppStdLib.cs` (support matrix), `CSharpBackend.cs` (Decimal
`m`-suffix constant emission via InvariantCulture; `(int)` casts for
divergent-typed surface members DayOfWeek/Kind; `CType(x, Decimal)`
lowering; `ConvertMethodForType` Byte/SByte), `SemanticAnalyzer.cs`
(surface-backed typing, operator + compound-assignment validation per 6.1,
Decimal-context literal conversion, stdlib date-function registrations,
DateTimeOffset), 
`docs/superpowers/specs/2026-07-26-dotnet-native-boundary-contract-design.md`
(C1 example rows: SByte → Bridged), enumerated existing tests.
