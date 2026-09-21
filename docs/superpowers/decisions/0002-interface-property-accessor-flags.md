# ADR 0002: interface property accessor flags

- **Date:** 2026-09-21
- **Status:** Accepted
- **Decided by:** architect role, ruling made by Opus — same consultation as
  ADR-0001; see that ADR's Provenance section for why the ruling model was
  not the pinned Fable 5.1.
- **Brief:** [`0001-brief.md`](0001-brief.md) §Q2 — same consultation as
  ADR-0001, preserved in full beside these ADRs.

## Question

`IRBuilder.cs:1689-1697` builds `IRInterfaceProperty` and sets
`HasGetter = prop.Getter != null`, `HasSetter = prop.Setter != null`. A bare
`Property Name As String` in an interface has no accessor bodies, so both are
always false, and all three consumer backends fail to compile the property.
Is this a two-line fix at that one construction site, or does it require a
cross-backend semantics change?

## Decision

**Confirmed** as a two-line change at `IRBuilder.cs:1689-1697`, plus
populating `IsReadOnly`/`IsWriteOnly` from the AST at that same site.

## Because

- The "has a body" reading has no readers. All three consumers —
  `CSharpBackend.cs:751-752`, `MSILBackend.cs:1026-1043`,
  `CppCodeGenerator.cs:1117-1119` — want "has this accessor," and only the
  interface construction site can ever produce a body-less accessor.
- Leaving `IsReadOnly`/`IsWriteOnly` unset at this site keeps a silently-false
  flag for the next reader to trip over.

## Contract

- On `IRInterfaceProperty`, `HasGetter`/`HasSetter` mean *declares this
  accessor* — `Getter != null || !IsWriteOnly`, and the mirror for the
  setter.
- `IsReadOnly`/`IsWriteOnly` are the source of truth and must be set at this
  site.
- Class-property construction (`IRBuilder.cs:1332-1333`) is untouched; the
  reading change is confined to interfaces.

## Assumption (stated, not verified)

These flags are read only through the interface path. If a shared node type
later exposes them to class properties too, the edit is still safe because
it is scoped to one construction site — but this has not been checked beyond
the three named consumers.

## Obligations

None on the backends — they already read `HasGetter`/`HasSetter` as "has this
accessor."

## Rejected

- **Renaming `HasGetter`/`HasSetter`** — out of scope, implementer's call;
  update the doc comment instead.
- **Deriving accessor presence independently in each backend** — three
  copies of one rule.

## Revisit if

A fourth reader appears that needs the "has a body" reading (e.g. default
interface members) — then the two meanings need two separate fields.
