# Visual Studio WinForms Properties Window & Control Property Reference

Sources:
- **Reflection** over the real `System.Windows.Forms.dll` (net8.0-windows, `Microsoft.WindowsDesktop.App 8.0.23`) using `TypeDescriptor.GetProperties(instance)` / `TypeDescriptor.GetEvents(type)` filtered by `Browsable(true)`, run on an STA thread. This is the exact mechanism VS's Properties window and PropertyGrid use, so the property/category/default-value/event lists below for each control are what VS actually shows (not approximated from docs).
- Microsoft Learn docs (`learn.microsoft.com/visualstudio/ide/properties-window`, `properties-window-buttons`, `how-to-dock-and-anchor`, `designer-overview`) for Properties-window UI/feature behavior (Part A) and editor mechanics.
- Raw reflection dump: not committed here; slice 1 of `2026-09-25-property-grid-vs-parity-design.md` commits it as `VisualGameStudio.Tests/Data/winforms-metadata.json` together with the tool that regenerates it.

Reflection caveats: (1) a couple of "default value" cells reflect the constructed instance's current value rather than a `[DefaultValue]` attribute where none was declared (e.g. `DateTimePicker.Value` prints today's actual runtime timestamp) — treat those as "typical/initial", not a compiled-in constant. (2) Design-time-only pseudo-properties that don't come from `TypeDescriptor` on a bare instance — `(Name)`, `(ApplicationSettings)`, `(DataBindings)` as a special expandable node, `Modifiers`, `GenerateMember`, `Locked` — are IDE/CodeDom concepts layered on top by VS's designer host, not on the runtime object; they're covered qualitatively in Part A instead.

---

## Part A — Properties Window features

- **Object selector combobox** (top): shows the currently selected object(s); dropdown lists every named component/control on the current document, including non-visual tray components. Selecting an object here changes the grid without touching the designer surface. Grid title bar shows `TypeName ObjectName` (e.g. `Button1 System.Windows.Forms.Button`).
- **Toolbar buttons**:
  - **Categorized** — groups properties under category headers (Appearance, Behavior, Data, Layout, ...), each collapsible with a +/− box. Categories are listed alphabetically; a custom `[Category("...")]` string creates/joins a group.
  - **Alphabetical** — flat A→Z list of properties (and, mixed in, events if the Events toggle state carries over — in practice VS keeps property/alphabetical and event views separate via the lightning-bolt button).
  - **Properties** — switches the grid back to property mode (vs. Events mode); shown pressed when active.
  - **Events (lightning bolt)** — switches the grid to list the object's events instead of properties; only shown for C#/VB code-bound designers. Each row's value cell holds the name of the handler method wired to that event (blank if unwired).
  - **Property Pages** — opens a modal Property Pages / Project Designer dialog for objects that implement `ISpecifyPropertyPages` (mainly project/solution-level items, not ordinary controls); grayed out otherwise.
- **Category groups actually produced by the reflected controls** (union across all 23 types): `Accessibility`, `Appearance`, `Asynchronous` (PictureBox only — ImageLocation/ErrorImage/InitialImage/WaitOnLoad), `Behavior`, `Data`, `Focus`, `Layout`, `Misc`, `Window Style` (Form only). No reflected control surfaced a `Design` category row (that category exists mainly for `(Name)`, `Modifiers`, `Locked`, `GenerateMember`, which are IDE-injected, not `TypeDescriptor`-visible on the bare CLR type).
- **Bold = non-default**: VS bolds a property's name (and un-bolds when reset) by comparing the current value against the `[DefaultValueAttribute]` recorded on the property (or the value produced by the designer's `ShouldSerializeXxx`/`ResetXxx` pattern when there's no static default, e.g. `Font`, `Location`, `Size`). A property with no meaningful default (e.g. collections) is never bolded/reset this way.
- **Reset** (right-click context menu on a property row, or on a category) — reverts to `[DefaultValue]`/`ResetXxx()`; unbolds the row.
- **Description pane** (bottom) — shows the property's `Type` and `[Description("...")]` text for the selected row; toggleable via the context-menu "Description" item.
- **Value editors** VS chooses based on the property's type/attributes:
  - Plain text edit box — `String`, numeric types with no special editor (e.g. `TabIndex`, `MaxLength`).
  - Dropdown — `bool` (True/False), any `enum` (e.g. `DockStyle`, `BorderStyle`, `FlatStyle`, `Anchor`'s underlying flags shown as a drop-down graphical widget rather than a plain list — see below).
  - **Color picker** with three tabs — Custom (RGB/HSB swatch grid + hex/ARGB entry), Web (named web colors), System (theme colors like `Control`, `Window`, `ControlText`, `ActiveCaption`, matching what `BackColor`/`ForeColor` reflect as e.g. `Color [Control]`).
  - **Font dialog** (`...` builder button) plus an expandable composite node showing `Name`, `Size`, `Bold`, `Italic`, `Underline`, `Strikeout`, `Unit`, `GdiCharSet`, `GdiVerticalFont` as individually editable sub-rows.
  - **Size / Point / Padding** — expandable composite properties (`Width`/`Height`; `X`/`Y`; `Left`/`Top`/`Right`/`Bottom` or a single "All" when uniform) via `TypeConverter`s (`SizeConverter`, `PointConverter`, `PaddingConverter`) that also accept inline `"W, H"` text entry.
  - **Anchor** — a dedicated visual editor (`AnchorEditor` — confirmed via Learn docs and matches `System.Windows.Forms.Design.AnchorEditor`): a drop-down cross/compass graphic where clicking an arm toggles Top/Bottom/Left/Right.
  - **Dock** — a dedicated visual editor (`DockEditor`): a drop-down showing a 3x3-ish diagram (Top/Bottom/Left/Right/Fill/None buttons around a center square).
  - **Collection editors** (`...` builder) — `Items` (ComboBox/ListBox `ObjectCollection`), `Columns`/`Rows` on grid-like controls, `TabPages` (TabControl), `Items` (MenuStrip/ToolStrip/StatusStrip `ToolStripItemCollection`), `DropDownItems` (ToolStripMenuItem) — opens a modal list-add/remove/reorder/property-per-item dialog.
  - **Image/resource picker** (`...` builder, `ImageFileEditor`/resx-aware) — `Image`, `BackgroundImage`, `InitialImage`/`ErrorImage` (PictureBox), `Icon` (Form) — browse-file or "Local resource" / "Project resource file" choice, persisted through the `.resx`.
  - **(ApplicationSettings)** — pseudo-node injected by VS for bindable settings-aware properties (not present on a bare reflected instance; an IDE feature layered via `SettingsBindingEditorForm`).
  - **(DataBindings)** — expandable node listing `Text`, other bindable properties, `(Advanced...)`; backed by the real `DataBindings : ControlBindingsCollection` property every control shows (confirmed present on every reflected type).
  - **(Name)** — pseudo-property (component's field/variable name in code); not a `TypeDescriptor` property on the CLR type, injected by the designer's `IComponent`/site.
- **Events tab, double-click behavior**: double-clicking a control on the design surface (or double-clicking the value cell of its `DefaultEvent` row in Events view) generates/navigates to a handler named `<ControlName>_<EventName>` and wires it via `+=` in `InitializeComponent`. `DefaultEventAttribute` on the class picks which event this is (reflected per-control below).
- **Multi-select**: selecting several controls shows only the properties common to all of them (by type and name); editing a shared value applies it to every selected control. Events view is typically unavailable for a multi-selection.

---

## Part B — Per-control browsable properties (from live reflection)

Legend: **C** = commonly used (~20% most users touch), rest are rarely touched. Categories/defaults/types are exactly as `TypeDescriptor` reports them on a freshly constructed instance. The full reflected dump (every property with description) becomes the committed snapshot in slice 1 — this section is the curated summary.

### Form — DefaultEvent `Load`, DefaultProperty `Text` (54 browsable properties, 80 events)
| Category | Property | Type | Default | Common |
|---|---|---|---|---|
| Appearance | **Text** | String | "" | C |
| Appearance | **FormBorderStyle** | FormBorderStyle | Sizable | C |
| Appearance | Font, BackColor, ForeColor, Cursor, BackgroundImage/Layout, RightToLeft(Layout), UseWaitCursor | — | — | (Font: C) |
| Behavior | **Enabled**, ContextMenuStrip, AllowDrop, AutoValidate, ImeMode, Visible | Boolean/… | True | Enabled: C |
| Data | DataBindings, Tag | — | — | |
| Focus | CausesValidation | Boolean | True | |
| Layout | **StartPosition**, **Size**, Location, Anchor, Dock, AutoScroll(+Margin/MinSize), AutoSize(Mode), MaximumSize, MinimumSize, Padding, **WindowState** | | StartPosition=WindowsDefaultLocation | StartPosition/Size/WindowState: C |
| Misc | **AcceptButton**, **CancelButton**, KeyPreview | IButtonControl | null | AcceptButton/CancelButton: C |
| Window Style | **ControlBox**, HelpButton, **Icon**, IsMdiContainer, MainMenuStrip, **MaximizeBox**, MdiChildrenMinimizedAnchorBottom, **MinimizeBox**, Opacity, **ShowIcon**, **ShowInTaskbar**, SizeGripStyle, **TopMost**, TransparencyKey | | | ControlBox/MaximizeBox/MinimizeBox/ShowInTaskbar/TopMost: C |

### Button — DefaultEvent `Click`, DefaultProperty `Text` (45 props / 64 events)
Common: **Text**, **DialogResult**, **Image**, **TextAlign/ImageAlign**, **Enabled**, **Anchor/Dock**, **Size/Location**, **FlatStyle**, **TabIndex**. Rare: FlatAppearance sub-props, ImageList/ImageIndex/ImageKey, UseMnemonic, UseVisualStyleBackColor, UseCompatibleTextRendering, AutoEllipsis, TextImageRelation, Margin/Padding, MaximumSize/MinimumSize.

### Label — DefaultEvent `Click`, DefaultProperty `Text` (39 props / 58 events)
Common: **Text**, **AutoSize**, **TextAlign**, **BorderStyle**, Font, ForeColor/BackColor, Anchor/Dock. Rare: Image/ImageAlign/ImageList/ImageIndex/ImageKey, FlatStyle, UseMnemonic, UseCompatibleTextRendering, AutoEllipsis, LiveSetting (accessibility).

### TextBox — DefaultEvent `TextChanged`, DefaultProperty `Text` (46 props / 66 events)
Common: **Text**, **Multiline**, **ReadOnly**, **MaxLength**, **PasswordChar**/**UseSystemPasswordChar**, **ScrollBars**, **WordWrap**, **PlaceholderText**, TextAlign, Anchor/Dock. Rare: AcceptsReturn/AcceptsTab, CharacterCasing, HideSelection, ShortcutsEnabled, AutoCompleteMode/Source/CustomSource, BorderStyle, ImeMode.

### CheckBox — DefaultEvent `CheckedChanged`, DefaultProperty `Checked` (49 props / 67 events)
Common: **Checked**, **CheckState**, **Text**, **CheckAlign**, **ThreeState**, **AutoCheck**, Appearance (button-style), Anchor/Dock. Rare: FlatAppearance/FlatStyle, Image*, TextImageRelation, UseMnemonic, UseVisualStyleBackColor.

### RadioButton — DefaultEvent `CheckedChanged`, DefaultProperty `Checked` (47 props / 66 events)
Same shape as CheckBox minus ThreeState/CheckState; common: **Checked**, **Text**, **CheckAlign**, **AutoCheck**, Appearance. Rare: same image/flat-style set as CheckBox.

### ComboBox — DefaultEvent `SelectedIndexChanged`, DefaultProperty `Items` (46 props / 73 events)
Common: **Items**, **DropDownStyle**, **DataSource**/**DisplayMember**/**ValueMember**, **Text**, **Sorted**, **MaxDropDownItems**. Rare: DrawMode, DropDownHeight/Width, IntegralHeight, ItemHeight, MaxLength, AutoCompleteMode/Source/CustomSource, FormatString/FormattingEnabled.

### ListBox — DefaultEvent `SelectedIndexChanged`, DefaultProperty `Items` (44 props / 68 events)
Common: **Items**, **SelectionMode**, **DataSource/DisplayMember/ValueMember**, **Sorted**, **MultiColumn**. Rare: BorderStyle, ColumnWidth, DrawMode, HorizontalExtent/Scrollbar, IntegralHeight, ItemHeight, ScrollAlwaysVisible, UseTabStops, FormatString/FormattingEnabled.

### GroupBox — DefaultEvent `Enter`, DefaultProperty `Text` (32 props / 51 events)
Common: **Text**, Font, Anchor/Dock, Size. Rare: FlatStyle, AutoSize(Mode), Padding, ImeMode, UseCompatibleTextRendering. (No TabStop-affecting or image props — GroupBox is a plain container.)

### Panel — DefaultEvent `Paint`, DefaultProperty `BorderStyle` (35 props / 61 events)
Common: **BorderStyle**, **AutoScroll**, Anchor/Dock, Size. Rare: AutoScrollMargin/MinSize, AutoSize(Mode), BackgroundImage/Layout, ImeMode, TabStop.

### PictureBox — DefaultEvent `Click`, DefaultProperty `Image` (28 props / 53 events)
Common: **Image**, **SizeMode**, BorderStyle, Anchor/Dock. Rare (Asynchronous category, web-image loading): ErrorImage, ImageLocation, InitialImage, WaitOnLoad.

### ProgressBar — DefaultEvent `Click`, DefaultProperty `Value` (28 props / 48 events)
Common: **Value**, **Minimum/Maximum**, **Style** (Blocks/Continuous/Marquee), Anchor/Dock. Rare: MarqueeAnimationSpeed, Step, RightToLeftLayout.

### NumericUpDown — DefaultEvent `ValueChanged`, DefaultProperty `Value` (39 props / 58 events)
Common: **Value**, **Minimum/Maximum**, **Increment**, **DecimalPlaces**, **Hexadecimal**, ReadOnly, Anchor/Dock. Rare: InterceptArrowKeys, TextAlign, ThousandsSeparator, UpDownAlign, BorderStyle, ImeMode.

### DateTimePicker — DefaultEvent `ValueChanged`, DefaultProperty `Value` (40 props / 57 events)
Common: **Value**, **Format** (Long/Short/Time/Custom), **CustomFormat**, **ShowCheckBox**, **ShowUpDown**, MinDate/MaxDate, Anchor/Dock. Rare: all Calendar* color/font props, DropDownAlign, Checked, RightToLeftLayout.

### TrackBar — DefaultEvent `Scroll`, DefaultProperty `Value` (33 props / 55 events)
Common: **Value**, **Minimum/Maximum**, **Orientation**, **TickFrequency**, **TickStyle**, LargeChange/SmallChange, Anchor/Dock. Rare: AutoSize, RightToLeftLayout.

### LinkLabel — DefaultEvent `LinkClicked`, DefaultProperty `Text` (46 props / — events, ~56)
Common: **Text**, **LinkArea**, **LinkColor**, **LinkBehavior**, AutoSize, TextAlign, Anchor/Dock. Rare: ActiveLinkColor, DisabledLinkColor, VisitedLinkColor, LinkVisited, Image*, UseMnemonic, LiveSetting.

### RichTextBox — DefaultEvent `TextChanged`, DefaultProperty `Text` (43 props / 66 events)
Common: **Text**/**Lines**, **ReadOnly**, **ScrollBars** (RichTextBoxScrollBars), **DetectUrls**, **WordWrap**, Anchor/Dock. Rare: AutoWordSelection, BulletIndent, EnableAutoDragDrop, HideSelection, MaxLength, RightMargin, ShowSelectionMargin, ZoomFactor.

### TabControl — DefaultEvent `SelectedIndexChanged`, DefaultProperty `TabPages` (36 props / 64 events)
Common: **TabPages**, **Alignment**, **Appearance** (Normal/Buttons/FlatButtons), **Multiline**, **ImageList**, Anchor/Dock. Rare: DrawMode, HotTrack, ItemSize, Padding (Point), ShowToolTips, SizeMode.

### MenuStrip — DefaultEvent `ItemClicked`, DefaultProperty `Items` (40 props / 71 events)
Common: **Items** (collection editor for the whole menu tree), **Dock** (defaults Top), RenderMode, Anchor. Rare: AllowItemReorder/AllowMerge, GripStyle/GripMargin, ImageScalingSize, LayoutStyle, MdiWindowListItem, Stretch.

### ToolStrip — DefaultEvent `ItemClicked`, DefaultProperty `Items` (40 props / 69 events)
Common: **Items**, **Dock**, GripStyle (default Visible, unlike MenuStrip/StatusStrip which default Hidden), Anchor. Rare: CanOverflow, LayoutStyle, ImageScalingSize, RenderMode, Stretch, ShowItemToolTips (default True here vs False on MenuStrip).

### StatusStrip — DefaultEvent `ItemClicked`, DefaultProperty `Items` (39 props / 68 events)
Common: **Items**, **SizingGrip**, **Dock** (defaults Bottom), LayoutStyle (defaults Table — distinct from ToolStrip/MenuStrip's HorizontalStackWithOverflow). Rare: GripMargin, ImageScalingSize, RenderMode (defaults System here, vs ManagerRenderMode elsewhere), Stretch.

### ToolStripMenuItem — DefaultEvent `Click`, DefaultProperty `DropDownItems` (42 props / 29 events)
Common: **Text**, **Checked**/**CheckOnClick**, **ShortcutKeys**, **ShortcutKeyDisplayString** (null by default — must be set manually if ShortcutKeys is set and display is desired), **Image**, **DropDownItems**, Enabled/Visible. Rare: Alignment, AutoToolTip, DisplayStyle, DoubleClickEnabled, ImageScaling/ImageTransparentColor, MergeAction/MergeIndex, Overflow, RightToLeftAutoMirrorImage, TextDirection.

### Timer — DefaultEvent `Tick`, DefaultProperty `Interval` (only 3 browsable properties / 1 event — smallest surface of all 23)
| Category | Property | Type | Default |
|---|---|---|---|
| Behavior | **Enabled** | Boolean | False |
| Behavior | **Interval** | Int32 | 100 |
| Data | Tag | Object | null |

Both Enabled and Interval are "commonly used" — there's nothing else. Note `Enabled` defaults **False** (must be turned on explicitly, unlike most controls which default `Enabled=True`).

---

## Notable cross-control facts surfaced by reflection

- Every control (and `Timer`) exposes `DataBindings : ControlBindingsCollection` and `Tag : Object` — these are on `System.ComponentModel.Component`/`Control`, not control-specific.
- `ShowItemToolTips` defaults **True** on `ToolStrip` but **False** on `MenuStrip`/`StatusStrip`.
- `RenderMode` defaults `ManagerRenderMode` on `ToolStrip`/`MenuStrip` but `System` on `StatusStrip`.
- `GripStyle` defaults `Visible` on `ToolStrip` but `Hidden` on `MenuStrip`/`StatusStrip`.
- `TabStop` default varies: `True` for input controls (TextBox, ComboBox, ListBox, Button, CheckBox, NumericUpDown, TrackBar…), `False` for `Panel`, `GroupBox`(no TabStop at all — not focusable as a unit), `RadioButton` is `False` by default even though it's an input control (only the checked one in a group normally gets tab focus in practice, but the reflected default on a fresh instance is False), `LinkLabel` is `False`.
- `AccessibleRole`/`AccessibleName`/`AccessibleDescription` are browsable on every control (Accessibility category); `LiveSetting` (AutomationLiveSetting) only appears on `Label` and `LinkLabel` among the reflected set.
- `Timer` is the only reflected type with no Appearance/Layout/Focus category at all — it is a non-visual component (`IComponent`, not `Control`), so it never shows in the canvas, only in the component tray.
