# ADR 0021: Form event lists, the web wiring of each event, and the one handler-shape rule

- **Date:** 2026-10-04
- **Status:** Accepted
- **Decided by:** coordinator, under the owner's delegation (plan review of `dabbdb90`, rulings 1–11); recorded by the implementer
- **Brief:** `docs/superpowers/plans/2026-10-04-property-grid-slice5-preflight.md` §2 D-2, D-3, D-4, D-8, D-12
- **Consumer:** piece 2, the portable control library (`docs/superpowers/plans/2026-09-29-portable-control-library.md`
  Tasks 28/30/35/40, spec §5.6/§10.1/§11.1) — its coverage gate reads these lists

## Question

Property-grid slice 5 gives every catalog kind and the Form a list of events (not one), emits root binds, and gives the
grid an Events tab. Piece 2 builds a library member per listed event. What is the representation both read, what does each
WinForms event mean on a page, and where is a handler's signature decided?

## Decision

### 1. The representation (what piece 2 consumes)

- `FormControlDef.Events` / `FormControlCatalog.FormRoot.Events` — `FormEventDef(Name, WinFormsArgs, WebEvent, Category,
  Description, IsDefault, OracleExemption, IsWebDefault, WebWiring, WebFilter)`.
  - `WebEvent` is THE one list of STORED DOM identities (the bind's `Event` attribute on a `.blwebform`); null means
    WinForms-only. No two events of a row share one.
  - `FormEvents.ListenType(evt)` is the DOM type actually passed to `addEventListener`: `keydown` for a `KeyPressKeys`
    event, otherwise `WebEvent`. A library listens to the same.
- `FormEvents.WiredOn(definition, target)` — signature unchanged; the one answer to "which events are wired here".
- `FormEvents.DomInterfaceOf(webEvent)` — a GATES-ONLY table keyed by the STORED name (the emitter never reads it):
  `click dblclick mousedown mouseup mousemove mouseenter mouseleave` → `MouseEvent`; `keydown keyup keypress` →
  `KeyboardEvent` (a KeyPress is dispatched as a `keydown` carrying `key`, through `ListenType`); `focusin focusout` →
  `FocusEvent`; `input change resize load` → `Event`. **`tick` is excluded** — a Timer is a `setInterval` callback, never
  a dispatched DOM event. Slice 5's node/Edge tiers and piece 2's coverage gate read it; there is no second table.
- `WebWiring` (`FormWebWiring { Element, Window, AfterInit }`) — where the DOM source is. `Element` is the control's own
  element, and `document.body` for the Form; `Window` is `window` (Form Resize); `AfterInit` is a direct call at the end of
  `InitializeComponent` (Form Load). `Window`/`AfterInit` appear only on the Form; at most one `AfterInit`, and it is the
  default.
- `WebFilter` (`FormWebFilter { None, KeyPressKeys, FromOutside }`) — which events need a generated wrapper (§3).
  `KeyPressKeys` is on exactly the events stored `keypress`; `FromOutside` on exactly the `focusin`/`focusout` ones; a filter
  only where `WebEvent` is set. The emitter reads the field and never switches on an event name.

### 2. What each WinForms event means on a page

| WinForms | Stored `WebEvent` | Emitted listener |
|---|---|---|
| Click / DoubleClick | `click` / `dblclick` | the same |
| MouseDown / MouseUp / MouseMove | `mousedown` / `mouseup` / `mousemove` | the same |
| MouseEnter / MouseLeave | `mouseenter` / `mouseleave` | the same (non-bubbling, per element as WinForms') |
| KeyDown / KeyUp | `keydown` / `keyup` | the same |
| **KeyPress** | `keypress` | a **`keydown`**, filtered to WinForms' KeyPress keys: ONE CODE POINT (`[...key].length === 1` — an emoji key is one character and two UTF-16 units; BasicLang `::Array.from(k).length = 1`) `\|\| key === "Enter" \|\| key === "Backspace" \|\| key === "Escape"` (WinForms raises KeyPress for `\b`, Esc, and Enter as `'\r'`). Its listener is added AFTER every plain `keydown` listener of the same owner, so KeyDown runs first as in WinForms, whatever order the binds are stored in (amendment, review of Task 2) |
| **Enter / Leave** | `focusin` / `focusout` | the same type, filtered to "`relatedTarget` is outside the element" — focus moving BETWEEN two children of a Panel raises no Enter/Leave on the Panel, as in WinForms. On a leaf element the filter is a no-op |
| Form Load | `load` | **AfterInit**: `Me.<Form>_Load()` as the LAST statement of `InitializeComponent`; the handler is parameterless |
| Form Resize | `resize` | **Window**: `w.addEventListener("resize", …)` |
| Form Click / KeyDown / KeyUp / KeyPress | `click` / `keydown` / `keyup` / `keypress` | **Element** = `document.body` |
| everything else (Paint, Validating, Shown, FormClosing, …) | none | WinForms-only |

Mapping the page's `e` to a `KeyChar` (`'\r'` for Enter, `'\b'`, `ChrW(27)`) is piece 2's job; a classic handler receives
the `DomEvent`. The key set above is the definition of "character-producing `keydown`" both sides use.

### 3. The filtered listener is a generated, NAMED wrapper Sub

For web binds whose event has a `WebFilter`, the init region holds, ABOVE `Private Sub InitializeComponent()`, ONE
generated `Private Sub VgsOn_<owner>_<WinForms event>(e As DomEvent)` per (owner, filtered event) that tests the filter and
calls `Me.<handler>(e)` for each handler bound to that event, in document order; ONE listener names the wrapper. A control
whose Id is the form's own name is refused (BL8017) — its wrappers would collide with the form's. Named (never a lambda: `Me.` inside a lambda hard-errors on the
JavaScript backend, and an unqualified call is a runtime `ReferenceError`); above `InitializeComponent` (an `AddressOf` of
a later Sub erases its parameter types — BL8013's measured rule). The `VgsOn_` prefix is reserved. The body is built from
`WebFilter` by one function, never from the event's name. WinForms wires the handler directly.

### 4. ONE parameterised handler-shape rule

`FormHandlers.Shape(owner, evt, target, style)` → `FormHandlerShape(Parameters, Placement)` is the ONLY place a handler's
signature and its side of the init region are decided. The stub writer and `Fits` both read it. Today the one style is
`FormCodeStyle.Classic` (`BasicLang/Forms/FormCodeStyle.cs`), and these are CLASSIC rules:

- WinForms: `(sender As Object, e As <WinFormsArgs ?? EventArgs>)`, placed BELOW the init region. A Sub fits iff it has
  two parameters, the first `Object` (or untyped), and the second `EventArgs`, the event's args, or a .NET base of them
  (one table, `FormEvents.ArgsBases`, falsified by in-process Roslyn).
- Web: a listener event (Element/Window, filtered or not) → `(e As DomEvent)`; an AfterInit event, or an event of a row
  with `WebHandlerTakesEvent: false` (Timer) → `()`. Placed ABOVE the init region.

Piece 2's Task 30 adds `FormCodeStyle.Portable` as one arm of `Shape` — never a second signature site.

## Because

- A WinForms user's handler must keep meaning what it meant: KeyPress is not raised for Shift or arrows, and a Panel's
  Enter is not raised for focus moving inside it.
- A deprecated `keypress` listener does not fire for Backspace/Escape; `focus`/`blur` do not bubble; an unfiltered
  `focusin` fires on every child-to-child move.
- Form Load as a `window` `load` listener can register after the event has fired (the page dispatch constructs the form
  when the page is ready) and silently never run.
- One shape rule is what keeps the stub the designer writes and the handlers the drop-down offers from disagreeing.

## Recorded divergences (classic emission vs WinForms)

Each row: classic emission diverges; piece 2's library is responsible for WinForms parity.

| Behaviour | WinForms | Classic page |
|---|---|---|
| Mouse events on a DISABLED control | none raised | browser-dependent |
| A double-click | Click once, then DoubleClick | `click`, `click`, `dblclick` → Click runs TWICE |
| RadioButton CheckedChanged when it becomes UNchecked | raised on both radios | `change` fires only on the newly checked radio |
| TextChanged on a programmatic `Text` set | raised | `input` is not fired by setting `.value` |
| MouseLeave on a container when the pointer enters a child | raised | `mouseleave` does not fire |
| Form Click on a click that lands on a control | not raised | `body` receives the bubbled `click` |
| Form KeyDown/KeyUp/KeyPress while a control has focus | only with `KeyPreview=True` | always (bubbled to `body`) |
| A Load handler that throws | routed to `Application.ThreadException`; the form is still shown | the exception escapes the form's constructor (Load is called at the end of `InitializeComponent`) and the page's dispatch dies — nothing on the page runs |

## Rejected

- KeyPress WinForms-only (piece 2 implements it; game code wants it); a real `keypress` listener; `beforeinput`.
- `focus`/`blur` for Enter/Leave; unfiltered `focusin`/`focusout`.
- The form's `<div class="vgs-form">` as the Form's element — the Docked strips are page chrome outside it, and it has no id.
- Load as a `window` `load` listener; Shown on the web; FormClosing → `beforeunload`; Activated → window `focus`.
- A filter helper in `dom-core.bli` (declarations only, emits nothing); an inline lambda; filtering inside the user's Sub.
- A second signature site for a second style.

## Revisit if

Piece 2's library lands and classic emission is retired, or a real form needs a WinForms-only event on the page.
