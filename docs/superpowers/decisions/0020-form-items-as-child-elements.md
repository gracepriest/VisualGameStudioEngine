# ADR 0020: A form control's Items are stored as `<Item>` child elements

- **Date:** 2026-10-04
- **Status:** Accepted
- **Decided by:** coordinator, under the owner's delegation (plan review of `88a74491`); recorded by the implementer
- **Brief:** `docs/superpowers/plans/2026-10-04-property-grid-slice4-preflight.md` §2 D-6

## Question

How does a `.blform` / `.blwebform` store a ComboBox / ListBox / CheckedListBox item list, given that the legacy
`Items="a, b"` attribute splits on commas and so cannot hold an item containing a comma (`Smith, John`)?

## Decision

Child elements, one per item, in document order; the model keeps ONE in-memory encoding (`Properties["Items"]`, the items
joined by LF).

```xml
<ComboBox Id="cmb" X="16" Y="16" Width="121" Height="23" TabIndex="0">
  <Item>Smith, John</Item>
  <Item>Beta</Item>
</ComboBox>
```

## Because

- An item may contain a comma; only a per-item container keeps it without an escaping scheme every hand-editor must learn.
- XML already escapes `&` and `<` inside element text, so nothing is escaped by hand.
- Every existing file keeps its meaning: a legacy attribute is read with the OLD rule, and a no-op save leaves it untouched.

## Contract

- **Reader**: for an `IsItemCollection` row, `<Item>` children are read verbatim (no trim), in order. A legacy `Items=`
  attribute is converted with the old rule (`FormItems.FromLegacy`: comma split, trim, drop empties). Both land in the model
  as LF-joined text.
- **Degraded** (never coerced): an `<Item>` whose text holds CR or LF, or a control carrying BOTH the attribute and `<Item>`
  children. Items is left OUT of `Properties`, the raw `<Item>` elements go into `UnknownChildren` and the attribute into
  `UnknownAttributes`, so every path that already carries unknown content carries them (Apply, Create, clone, retarget,
  clipboard). The writer's Items path never touches such a control.
- **`FormItems.Split`** (every consumer: the region writer, the asset emitter, the editor) splits on LF, strips a trailing CR,
  drops empty or whitespace-only entries and keeps spaces around non-blank text. `FormItems.Join` is its inverse.
- **Writer (Apply)**: a legacy attribute whose comma split equals the model's list is LEFT ALONE, and so are `<Item>`
  children that read (through `Split`) as the model's list — so a no-op save is byte-identical on both formats, `<Item/>`
  blanks included. Otherwise the attribute is removed and the `<Item>` run rewritten, first among the element's children.
  Items absent from the model removes both forms. **Create** writes `<Item>` children, never the attribute.
- **FormClipboard** keeps copying the model string into an attribute between two models; XML carries the LF as `&#xA;`.

## Obligations

- The generic property loops in the writer (set, and the dropped-property sweep) skip `IsItemCollection` rows; both ask
  `FormItems.IsCollection`.

## Rejected

- An escape inside the attribute (`Smith\, John`) — every hand-editor learns it; a legacy `C:\a,b` changes meaning.
- LF inside the attribute (`Items="Smith, John&#xA;Beta"`) — a one-item list with a comma is indistinguishable from a legacy
  two-item list without a sentinel, and the file shows `&#xA;` to anyone reading it.
- Commas forever — an item containing a comma cannot round-trip.

## Costs, recorded

1. An EMPTY-string item (`Items.Add("")`, legal in WinForms) is unrepresentable: `Split` drops it.
2. On the web, `<option>` text collapses leading, trailing and repeated spaces, so `"  padded "` shows as `padded` on the
   page while WinForms keeps the spaces.
3. An item containing a line break cannot be created in the editor, and a hand-written one is Degraded.

## Revisit if

A real form needs an empty-string item or an item with a line break.
