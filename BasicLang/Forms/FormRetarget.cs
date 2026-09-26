using System.Text;
using System.Xml.Linq;

namespace BasicLang.Forms;

/// <summary>What a retarget produced: the new document, and every finding about what could not cross.</summary>
/// <param name="Document">A NEW document in the other format. The source is never touched.</param>
/// <param name="Diagnostics">All warnings — the conversion succeeded, and each names a loss to review.</param>
public sealed record FormRetargetResult(FormDocument Document, IReadOnlyList<DesignDiagnostic> Diagnostics);

/// <summary>
/// A retargeted FORM — the document and a code-behind for it, as <see cref="FormScaffold"/> is for a
/// new one. The code-behind is a fresh scaffold on the destination target with its designer regions
/// already generated and an empty stub for every handler that crossed; the original is never touched.
/// </summary>
public sealed record FormRetargetPair(
    string DocumentFileName,
    string DocumentText,
    string CodeFileName,
    string CodeText,
    IReadOnlyList<DesignDiagnostic> Diagnostics);

/// <summary>
/// Task 21 — one form, two targets. Converts a document between <c>.blform</c> and
/// <c>.blwebform</c>: the shared grammar (D2 — kinds, ids, tab order, catalog properties, binds,
/// unknown content) crosses losslessly, and everything that cannot is reported as a <c>BL802x</c>
/// finding rather than dropped silently.
///
/// <para>⛔⛔ <b>Absolute pixels ⇄ grid cells is the hard edge, and nothing here pretends
/// otherwise.</b> A page laid out by cell cannot hold <c>X=90</c>; a window laid out in pixels has no
/// <c>Col=1</c>. Both directions therefore DERIVE a position by a rule simple enough to state in the
/// finding — one column per distinct X and one row per distinct Y going to the web; cells pitched
/// to the largest sibling going to WinForms — and say, per control, what it had and where it landed.
/// Anything cleverer (tolerances, span inference, auto-placement emulation) would be a guess the
/// user could not review, which is the silent loss the task forbids.</para>
///
/// <para>⚠ The DOCUMENT only. The code-behind is the user's own class, whose base type and handler
/// signatures differ by target; the pair-producing entry point scaffolds a fresh one beside the new
/// document and never rewrites the original.</para>
/// </summary>
public static class FormRetarget
{
    /// <summary>Inset from the window edge, and the gap between pitched cells, going to WinForms.</summary>
    public const int Margin = 16;
    public const int Gap = 8;

    /// <summary>The scaffolder's own gap, so a retargeted page and a new page space alike.</summary>
    private const string GapCss = "8px";

    /// <summary>
    /// The window a retargeted form opens in is never smaller than a new one — 800x450 is what
    /// <see cref="FormScaffolder"/> gives a fresh form, for the same reason it gives it that.
    /// </summary>
    private const int MinimumWidth = 800;
    private const int MinimumHeight = 450;

    public static FormRetargetResult Convert(FormDocument source, FormTarget to)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.Target == to)
        {
            throw new ArgumentException(
                $"'{source.Name}' is already a {Describe(to)} document; there is nothing to retarget.",
                nameof(to));
        }

        var state = new Conversion(source, to);
        state.ConvertRoot();

        var document = state.Document;
        state.ConvertControls(source.Controls, document.Controls, "the form");

        // The tray (Task 25): the same kind/property/bind rules — a ToolTip has no web row and is
        // BL8023, a Timer's Enabled has no web meaning and is BL8024, the Tick bind becomes tick —
        // and none of the geometry pass below, because a component has no place on either side.
        state.ConvertControls(source.Components, document.Components, "the tray");

        if (to == FormTarget.Web)
        {
            state.ToCells();
        }
        else
        {
            state.ToPixels();
        }

        return new FormRetargetResult(document, state.Diagnostics);
    }

    /// <summary>
    /// The document AND a code-behind that makes it a program on the destination.
    ///
    /// <para>⛔⛔ The regions are GENERATED here, not left for the designer's first save. A retarget
    /// run from the CLI may never be opened in the IDE, and a scaffold with empty regions builds
    /// clean into a form that constructs no controls — <c>FormCodeBehind</c>'s own header records
    /// exactly that failure. Stubs go in first, each on the side of the init region its target's
    /// ordering rule requires (<see cref="FormHandlers"/>), then the regions are written around
    /// them, so the markers' hashes are right and the designer's first save finds them Canon.</para>
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The form's name cannot be a class name (<see cref="FormScaffolder.DescribeIllegalName"/>),
    /// or the source is already on <paramref name="to"/>. Nothing is produced.
    /// </exception>
    public static FormRetargetPair ConvertToPair(FormDocument source, FormTarget to)
    {
        ArgumentNullException.ThrowIfNull(source);

        var illegal = FormScaffolder.DescribeIllegalName(source.Name);
        if (illegal != null)
        {
            throw new ArgumentException(illegal, nameof(source));
        }

        var result = Convert(source, to);
        var document = result.Document;

        var scaffold = FormScaffolder.Create(document.Name, to);
        var code = scaffold.CodeText;

        foreach (var control in document.AllControls().Concat(document.AllComponents()))
        {
            // Every non-reserved bind that crossed is on the kind's DEFAULT event — that is the only
            // kind Convert lets through — so PlanDefault names exactly the Sub it wires. A control with
            // no such bind is skipped: a stub nothing wires is dead code. Components too: a web
            // Timer's tick handler is the setInterval callback, and the stub is parameterless.
            if (!control.Binds.Any(b => !b.UsesReservedDataBinding))
            {
                continue;
            }

            var plan = FormHandlers.PlanDefault(document, control, code);
            if (plan.Outcome == HandlerOutcome.Created)
            {
                code = plan.CodeText;
            }
        }

        var regions = RegionWriter.Write(scaffold.CodeFileName, code, document, scaffold.DocumentFileName);
        if (regions.Refused)
        {
            // Cannot happen on a scaffold this method just created; if it does, it is a bug here, not
            // a finding for the user.
            throw new InvalidOperationException(
                "the region writer refused the scaffold it was just handed: " +
                string.Join("; ", regions.Diagnostics.Select(d => d.Message)));
        }

        return new FormRetargetPair(
            scaffold.DocumentFileName,
            Serialization.FormDocumentWriter.Create(document),
            scaffold.CodeFileName,
            regions.Text,
            result.Diagnostics);
    }

    private static string Describe(FormTarget target) => target == FormTarget.Web ? "web" : "WinForms";

    /// <summary>One conversion's working state, so the passes can share it without a parameter list each.</summary>
    private sealed class Conversion
    {
        private readonly FormDocument _source;
        private readonly FormTarget _from;
        private readonly FormTarget _to;

        /// <summary>
        /// The geometry each NEW control was converted from, in the source vocabulary — kept apart
        /// from the control because the destination geometry replaces it, and because a hoisted child
        /// carries a TRANSLATED copy (its container's offset added), not its own.
        /// </summary>
        private readonly Dictionary<FormControl, FormGeometry?> _sourceGeometry = new();

        public Conversion(FormDocument source, FormTarget to)
        {
            _source = source;
            _from = source.Target;
            _to = to;
            Document = new FormDocument { Target = to, Name = source.Name, Version = source.Version };
        }

        public FormDocument Document { get; }

        public List<DesignDiagnostic> Diagnostics { get; } = new();

        // ==============================================================
        // The root: unknown content crosses unless the destination would READ it
        // ==============================================================

        public void ConvertRoot()
        {
            // Text is ONE vocabulary on both targets (D2, spec §2.3). ⚠ A caption EQUAL to the name is
            // written as absence on the page, whose title is Text ?? Name — the page shows the same thing
            // either way, and a round trip stays byte-identical.
            Document.Text = _to == FormTarget.Web && string.Equals(_source.Text, _source.Name, StringComparison.Ordinal)
                ? null
                : _source.Text;

            foreach (var (name, value) in _source.UnknownAttributes)
            {
                // ⛔ A .blwebform root carrying Width="400" holds it as an unknown attribute; the
                // WinForms reader would model that as the window's width. Carrying it across would
                // let a stale number overrule the value this retarget derived — Create() writes
                // unknown attributes AFTER the modelled ones and SetAttributeValue replaces.
                if (FormRootValues.RowForAttribute(name, _to) is { } modelled)
                {
                    Warn(DesignCodes.RetargetPropertyLost,
                        $"the form's '{name}=\"{value}\"' attribute is not modelled on a {Describe(_from)} " +
                        $"document but WOULD be read as 'form.{modelled.Name}' on a {Describe(_to)} one. It was " +
                        "dropped rather than allowed to overrule the value this retarget derived.");
                    continue;
                }

                // ⛔ The SOURCE's own degraded storage (an unparseable Width on a .blform is kept as an
                // unknown attribute so it round-trips — plan scope call S3). It is a FormRoot row the
                // destination does not have, not "content we do not model", so it is named, never carried.
                if (FormRootValues.RowForAttribute(name, _from) is { } row && !row.AppliesTo(_to))
                {
                    Warn(DesignCodes.RetargetPropertyLost,
                        $"'form.{row.Name}' could not be read on the {Describe(_from)} form ('{name}=\"{value}\"') " +
                        $"and does not exist on a {Describe(_to)} one, so it was dropped.");
                    continue;
                }

                Document.UnknownAttributes[name] = value;
            }

            foreach (var child in _source.UnknownChildren)
            {
                var name = child.Name.LocalName;
                if (IsRootElementModelledOn(name, _to))
                {
                    Warn(DesignCodes.RetargetPropertyLost,
                        $"the form carries a <{name}> element, which a {Describe(_from)} document does not " +
                        $"read but a {Describe(_to)} one does. It was dropped rather than duplicated beside " +
                        "the one this retarget wrote.");
                    continue;
                }

                Document.UnknownChildren.Add(new XElement(child));
            }

            ConvertRootBinds();

            // Components (Task 25) cross in ConvertComponents, with the same kind/property/bind
            // rules as controls and no geometry pass — see FormRetargetTests.
            Document.Resources.AddRange(_source.Resources.Select(e => new XElement(e)));
        }

        /// <summary>
        /// The form's own binds (spec §2.3): a bind whose event is wired on the destination crosses under
        /// the destination's name (through the SAME seam the emitter asks); any other is dropped and NAMED.
        /// ⚠ The Form has no catalog events until slice 5, so today every root bind is named.
        /// </summary>
        private void ConvertRootBinds()
        {
            foreach (var bind in _source.Binds)
            {
                // Reserved data binding is parsed and round-tripped, never interpreted — including here.
                if (bind.UsesReservedDataBinding)
                {
                    Document.Binds.Add(bind.Clone());
                    continue;
                }

                var crossing = FormEvents.WiredOn(FormControlCatalog.FormRoot, _from)
                    .FirstOrDefault(e => string.Equals(FormEvents.NameOn(e, _from), bind.Event, StringComparison.OrdinalIgnoreCase));
                var toName = crossing != null && FormEvents.WiredOn(FormControlCatalog.FormRoot, _to).Contains(crossing)
                    ? FormEvents.NameOn(crossing, _to)
                    : null;

                if (toName != null)
                {
                    Document.Binds.Add(new FormBind { Event = toName, Handler = bind.Handler });
                    continue;
                }

                Warn(DesignCodes.RetargetBindLost,
                    $"'form' wires its '{bind.Event}' event to {bind.Handler}, and the catalog knows no " +
                    $"{Describe(_to)} name for that form event. The wiring was dropped; wire {bind.Handler} " +
                    "by hand on the other side.");
            }
        }

        private static bool IsRootElementModelledOn(string name, FormTarget target) =>
            target == FormTarget.Web && name is "Layout" or "Literal";

        // ==============================================================
        // Controls: the shared grammar crosses, the rest is named
        // ==============================================================

        /// <param name="where">How to name the list in a finding: <c>the form</c> or <c>'panel1'</c>.</param>
        /// <param name="offset">
        /// A pixel offset to add to each control's source geometry — non-zero only for the children
        /// of a container that was removed, whose positions were relative to it.
        /// </param>
        public void ConvertControls(
            IEnumerable<FormControl> sources, List<FormControl> into, string where, (int X, int Y) offset = default)
        {
            foreach (var source in sources)
            {
                var definition = source.Definition;
                if (definition == null || !definition.SupportsTarget(_to))
                {
                    Hoist(source, into, where, offset);
                    continue;
                }

                var control = new FormControl { Kind = source.Kind, Id = source.Id, TabIndex = source.TabIndex };
                _sourceGeometry[control] = Translate(source.Geometry, offset);

                // "Wired means running" (Task 25 review): a script component's construct on the web
                // is what WinForms says with a property. Decided BEFORE the properties cross, because
                // it is what makes that property the wiring itself (BL8027) rather than a loss (BL8024).
                var implied = WiredRunState(source, definition);

                ConvertProperties(source, control, definition, implied);
                ConvertBinds(source, control, definition);
                CrossRunState(source, control, definition, implied);

                foreach (var unknown in source.UnknownChildren)
                {
                    control.UnknownChildren.Add(new XElement(unknown));
                }

                ConvertControls(source.Children, control.Children, $"'{source.Id}'");
                into.Add(control);
            }
        }

        /// <summary>
        /// A kind with no row on the destination is removed and its children take its place — in
        /// order, at its position among its siblings — so a TabControl's worth of buttons is not
        /// lost with the TabControl.
        /// </summary>
        private void Hoist(FormControl source, List<FormControl> into, string where, (int X, int Y) offset)
        {
            var childOffset = source.Geometry is PixelGeometry pixel
                ? (offset.X + pixel.X, offset.Y + pixel.Y)
                : offset;

            var before = into.Count;
            ConvertControls(source.Children, into, where, childOffset);
            var moved = into.Count - before;

            var message = new StringBuilder()
                .Append($"'{source.Id}' is a {source.Kind}, which has no {Describe(_to)} row in the control ")
                .Append("catalog, so it was removed.");

            if (source.Children.Count > 0)
            {
                message.Append($" Its {source.Children.Count} child control(s) were moved up into {where} in its place")
                    .Append(moved == source.Children.Count ? "." : $" ({moved} survived; the rest have no row either).");
            }

            Warn(DesignCodes.RetargetControlLost, message.ToString());
        }

        /// <param name="implied">
        /// The row's implied property when the source is wired on the event that crosses — the one
        /// property whose absence on the destination is the wiring itself, not a loss.
        /// </param>
        private void ConvertProperties(
            FormControl source, FormControl control, FormControlDef definition, FormImpliedProperty? implied)
        {
            foreach (var (name, value) in source.Properties)
            {
                var property = definition.Property(name);

                // Only an in-memory model can carry a name the catalog does not know here; the
                // reader routes those to UnknownAttributes. Not ours to judge — carry it.
                if (property != null && !property.AppliesTo(_to))
                {
                    if (implied != null &&
                        string.Equals(name, implied.Name, StringComparison.OrdinalIgnoreCase) &&
                        SameValue(property, value, implied.Value))
                    {
                        // Not a loss: CrossRunState names it as the wiring itself (BL8027). Reporting
                        // it as BL8024 told the user to "re-express it on the other side" — of nothing.
                        continue;
                    }

                    Warn(DesignCodes.RetargetPropertyLost,
                        $"'{source.Id}.{name}' = \"{value}\" does not exist on a {Describe(_to)} {source.Kind}, " +
                        "so it was dropped. Re-express it on the other side if the page or window needs it.");
                    continue;
                }

                control.Properties[name] = value;

                // The property exists on both sides but the destination refuses this VALUE (a system
                // colour with no CSS equivalent going to the web; a CSS colour name System.Drawing.Color
                // lacks going to WinForms). Preserved — it is the user's text, and the other side opens
                // it Degraded — but its meaning is lost, so it is NAMED. ⚠ The reason is the catalog's
                // own (DescribeRefusal, which throws on an accepted value — hence only after refusal).
                if (property != null && !property.Accepts(value, _to))
                {
                    Warn(DesignCodes.RetargetPropertyLost,
                        $"'{source.Id}.{name}' = \"{value}\" crosses but is not usable on a {Describe(_to)} " +
                        $"{source.Kind}: {property.DescribeRefusal(value, _to)}");
                }
            }

            foreach (var (name, value) in source.UnknownAttributes)
            {
                // ⛔ The other format's layout vocabulary. A .blform control carrying a stray Col="2"
                // holds it as an unknown attribute; carried onto the web control, Create() would write
                // it over the cell this retarget derived. Dropped and named, never silently honoured.
                if (FormControlCatalog.IsStructural(name, _to))
                {
                    Warn(DesignCodes.RetargetPropertyLost,
                        $"'{source.Id}.{name}' = \"{value}\" is not a {source.Kind} property, and a " +
                        $"{Describe(_to)} document reads '{name}' as layout. It was dropped rather than " +
                        "allowed to overrule the position this retarget derived.");
                    continue;
                }

                control.UnknownAttributes[name] = value;
            }
        }

        private void ConvertBinds(FormControl source, FormControl control, FormControlDef definition)
        {
            var fromEvent = definition.DefaultEvent(_from);
            var toEvent = definition.DefaultEvent(_to);

            foreach (var bind in source.Binds)
            {
                // Reserved data binding is parsed and round-tripped, never interpreted — including here.
                if (bind.UsesReservedDataBinding)
                {
                    control.Binds.Add(bind.Clone());
                    continue;
                }

                if (fromEvent != null && toEvent != null &&
                    string.Equals(bind.Event, fromEvent, StringComparison.OrdinalIgnoreCase))
                {
                    control.Binds.Add(new FormBind { Event = toEvent, Handler = bind.Handler });
                    continue;
                }

                Warn(DesignCodes.RetargetBindLost,
                    $"'{source.Id}' wires its '{bind.Event}' event to {bind.Handler}, and the catalog knows no " +
                    $"{Describe(_to)} name for that event on a {source.Kind} — only its default event " +
                    $"('{fromEvent}' → '{toEvent}') has a measured name on both sides. The wiring was dropped; " +
                    $"wire {bind.Handler} by hand on the other side.");
            }
        }

        // ==============================================================
        // "Wired means running" crosses by RULE, and is named (Task 25 review)
        //
        // A web Timer runs the moment it is wired; a WinForms one runs only with Enabled=True. Left
        // to the property rules alone, a wired web Timer arrived on the window with Enabled absent —
        // default False — and never fired, with no finding; and a disabled WinForms Timer arrived on
        // the page wired, and ran from load, with no finding. The catalog row states the equivalence
        // (FormWebScript.Implies); this is the one place that applies it, in both directions.
        // ==============================================================

        /// <summary>The row's implied property, when the source is wired on the event that crosses; else null.</summary>
        private FormImpliedProperty? WiredRunState(FormControl source, FormControlDef definition)
        {
            var implied = definition.WebScript?.Implies;
            var fromEvent = definition.DefaultEvent(_from);
            if (implied == null || fromEvent == null)
            {
                return null;
            }

            var wired = source.Binds.Any(b =>
                !b.UsesReservedDataBinding && !string.IsNullOrEmpty(b.Handler) &&
                string.Equals(b.Event, fromEvent, StringComparison.OrdinalIgnoreCase));

            return wired ? implied : null;
        }

        private void CrossRunState(
            FormControl source, FormControl control, FormControlDef definition, FormImpliedProperty? implied)
        {
            if (implied == null)
            {
                return;
            }

            if (_to == FormTarget.WinForms)
            {
                // The web side ran; make the window run too, and say so. Set over anything the source
                // carried: a stray Enabled="false" on a .blwebform never stopped the page's Timer.
                control.Properties[implied.Name] = implied.Value;
                Warn(DesignCodes.RetargetRunStateCrossed,
                    $"'{source.Id}' is a wired {Describe(_from)} {source.Kind}, which runs as soon as it is " +
                    $"wired, so '{implied.Name}=\"{implied.Value}\"' was set on the {Describe(_to)} side to keep " +
                    "it running. Clear it if the window should start it from code instead.");
                return;
            }

            var property = definition.Property(implied.Name);
            var held = source.Properties.TryGetValue(implied.Name, out var value) &&
                       SameValue(property, value, implied.Value);

            Warn(DesignCodes.RetargetRunStateCrossed, held
                ? $"'{source.Id}.{implied.Name}' = \"{value}\" crossed as the wiring itself: a {Describe(_to)} " +
                  $"{source.Kind} runs as soon as it is wired, and this one is."
                : $"'{source.Id}' is wired but '{implied.Name}' is not \"{implied.Value}\" on the {Describe(_from)} " +
                  $"side, so the window's {source.Kind} would not run until code started it. A {Describe(_to)} " +
                  $"{source.Kind} runs as soon as it is wired, so the page's will run from load. Remove the " +
                  "bind if it should not.");
        }

        /// <summary>Equality in the property's own terms: <c>True</c> and <c>true</c> are one Bool.</summary>
        private static bool SameValue(FormPropertyDef? property, string a, string b)
        {
            if (property?.Type == FormPropertyType.Bool &&
                bool.TryParse(a, out var x) && bool.TryParse(b, out var y))
            {
                return x == y;
            }

            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static FormGeometry? Translate(FormGeometry? geometry, (int X, int Y) offset)
        {
            if (geometry is PixelGeometry pixel && (offset.X != 0 || offset.Y != 0))
            {
                var moved = (PixelGeometry)pixel.Clone();
                moved.X += offset.X;
                moved.Y += offset.Y;
                return moved;
            }

            return geometry?.Clone();
        }

        // ==============================================================
        // WinForms → Web: one column per distinct X, one row per distinct Y
        // ==============================================================

        public void ToCells()
        {
            var (cols, rows) = DeriveCells(Document.Controls);

            Document.Layout = new FormLayout
            {
                Kind = FormLayoutKind.Grid,
                Cols = Tracks(Math.Max(1, cols)),
                Rows = Tracks(Math.Max(1, rows)),
                Gap = GapCss
            };

            var lost = new List<string>();
            if (_source.Width != null) lost.Add($"Width={_source.Width}");
            if (_source.Height != null) lost.Add($"Height={_source.Height}");

            Warn(DesignCodes.RetargetLayoutCrossed,
                (lost.Count > 0
                    ? $"the window's {string.Join(" ", lost)} have no meaning on a page and were dropped. "
                    : "the window has become a page. ") +
                $"A Grid layout of {Math.Max(1, rows)} row(s) by {Math.Max(1, cols)} column(s) was derived from " +
                "the controls' pixel positions; review it.");
        }

        /// <returns>How many distinct columns and rows the siblings used.</returns>
        private (int Cols, int Rows) DeriveCells(List<FormControl> siblings)
        {
            var pixels = siblings
                .Select(c => _sourceGeometry.GetValueOrDefault(c) as PixelGeometry)
                .Where(p => p != null)
                .Select(p => p!)
                .ToList();

            var xs = pixels.Select(p => p.X).Distinct().OrderBy(x => x).ToList();
            var ys = pixels.Select(p => p.Y).Distinct().OrderBy(y => y).ToList();

            foreach (var control in siblings)
            {
                if (_sourceGeometry.GetValueOrDefault(control) is PixelGeometry pixel)
                {
                    var col = xs.IndexOf(pixel.X);
                    var row = ys.IndexOf(pixel.Y);
                    control.Geometry = new GridGeometry { Col = col, Row = row };

                    var had = $"X={pixel.X} Y={pixel.Y} Width={pixel.Width} Height={pixel.Height}";
                    if (pixel.Anchor != null) had += $" Anchor={pixel.Anchor}";
                    if (pixel.Dock != null) had += $" Dock={pixel.Dock}";

                    Warn(DesignCodes.RetargetLayoutCrossed,
                        $"'{control.Id}' had {had} — absolute pixels, which a page laid out by cell cannot " +
                        $"express. It was placed at Col={col} Row={row} (one column per distinct X and one " +
                        "row per distinct Y among its siblings); review the cell.");
                }
                else
                {
                    // Absence stays absent, exactly as the reader decides it: a control the source
                    // never positioned is not given a cell it never had.
                    control.Geometry = null;
                }

                DeriveCells(control.Children);
            }

            return (xs.Count, ys.Count);
        }

        private static string Tracks(int count) => string.Join(",", Enumerable.Repeat("auto", count));

        // ==============================================================
        // Web → WinForms: cells pitched to the largest sibling, catalog sizes
        // ==============================================================

        public void ToPixels()
        {
            var layout = _source.Layout ?? new FormLayout();
            var (right, bottom) = Place(Document.Controls, layout);

            Document.Width = Math.Max(MinimumWidth, right + Margin);
            Document.Height = Math.Max(MinimumHeight, bottom + Margin);
            // The caption crossed in ConvertRoot; a page with none becomes a window captioned with its
            // name — which is what the page's title showed (Text ?? Name).
            Document.Text ??= _source.Name;

            var described = _source.Layout == null
                ? "the page had no <Layout>, so its controls flowed in document order"
                : $"the page's {layout.Kind} layout ({DescribeLayout(layout)}) has no meaning in a window and was dropped";

            Warn(DesignCodes.RetargetLayoutCrossed,
                $"{described}; controls were placed at pixel positions derived from their cells, in a " +
                $"{Document.Width}x{Document.Height} window captioned \"{Document.Text}\". Review them.");

            if (_source.Literal != null)
            {
                Warn(DesignCodes.RetargetLayoutCrossed,
                    $"the page's <Literal> markup ({_source.Literal.Length} character(s)) has no place in a " +
                    "window and was dropped.");
            }
        }

        private static string DescribeLayout(FormLayout layout)
        {
            var parts = new List<string>();
            if (layout.Cols != null) parts.Add($"Cols=\"{layout.Cols}\"");
            if (layout.Rows != null) parts.Add($"Rows=\"{layout.Rows}\"");
            if (layout.Gap != null) parts.Add($"Gap=\"{layout.Gap}\"");
            if (layout.Dir != null) parts.Add($"Dir=\"{layout.Dir}\"");
            return parts.Count == 0 ? "no attributes" : string.Join(" ", parts);
        }

        /// <returns>The extent the placed siblings reach: the largest right edge and bottom edge.</returns>
        private (int Right, int Bottom) Place(List<FormControl> siblings, FormLayout layout)
        {
            // ⛔ Only a Positioned control has a place to derive (Task 26). A Docked strip and its
            // Items have no geometry on EITHER side — the strip says where it sits with its Dock
            // PROPERTY, which crossed untouched — so they are given no pixels, no BL8025, and their
            // children are never walked. Excluded from the pitch too, or a strip's catalog width
            // would spread every cell. A page whose top level is a strip alone leaves nothing to
            // measure, and Max over nothing throws: hence the return BEFORE any Max is reached.
            static bool IsPositioned(FormControl c) => c.Definition?.Place is null or FormPlace.Positioned;

            foreach (var control in siblings.Where(c => !IsPositioned(c)))
            {
                control.Geometry = null;
            }

            var positioned = siblings.Where(IsPositioned).ToList();
            if (positioned.Count == 0)
            {
                return (0, 0);
            }

            // Sizes first, post-order, so a container is sized to hold the children placed inside it
            // and the pitch below is measured over the sizes the siblings will actually have.
            var sizes = new Dictionary<FormControl, (int W, int H)>();
            foreach (var control in positioned)
            {
                var (childRight, childBottom) = Place(control.Children, layout);
                var definition = control.Definition!;
                var w = definition.DefaultWidth;
                var h = definition.DefaultHeight;

                if (control.Children.Count > 0)
                {
                    w = Math.Max(w, childRight + Margin);
                    h = Math.Max(h, childBottom + Margin);
                }

                sizes[control] = (w, h);
            }

            var colPitch = sizes.Values.Max(s => s.W) + Gap;
            var rowPitch = sizes.Values.Max(s => s.H) + Gap;

            var isGrid = layout.Kind == FormLayoutKind.Grid;
            var vertical = layout.Kind == FormLayoutKind.Flow &&
                           string.Equals(layout.Dir, "Vertical", StringComparison.OrdinalIgnoreCase);

            // A Grid control with no cell is auto-placed by the browser. Emulating CSS auto-placement
            // is a guess; putting it in its own row BELOW every explicit one is not, and cannot land
            // on top of a control that was positioned.
            var lastExplicitRow = positioned
                .Select(c => _sourceGeometry.GetValueOrDefault(c) as GridGeometry)
                .Where(g => g != null)
                .Select(g => g!.Row)
                .DefaultIfEmpty(-1)
                .Max();
            var unplaced = 0;

            var right = 0;
            var bottom = 0;

            for (var i = 0; i < positioned.Count; i++)
            {
                var control = positioned[i];
                var (w, h) = sizes[control];
                var cell = _sourceGeometry.GetValueOrDefault(control) as GridGeometry;

                int col, row;
                string was;

                if (isGrid && cell != null)
                {
                    col = cell.Col;
                    row = cell.Row;
                    was = $"at Col={cell.Col} Row={cell.Row}" +
                          (cell.ColSpan != 1 ? $" ColSpan={cell.ColSpan}" : "") +
                          (cell.RowSpan != 1 ? $" RowSpan={cell.RowSpan}" : "") +
                          " in a Grid layout";
                }
                else if (isGrid)
                {
                    col = 0;
                    row = lastExplicitRow + 1 + unplaced++;
                    was = "in a Grid layout with no cell of its own";
                }
                else
                {
                    col = vertical ? 0 : i;
                    row = vertical ? i : 0;
                    was = $"item {i} of a {layout.Kind} layout ({(vertical ? "vertical" : "horizontal")})";
                }

                var x = Margin + col * colPitch;
                var y = Margin + row * rowPitch;
                control.Geometry = new PixelGeometry { X = x, Y = y, Width = w, Height = h };

                right = Math.Max(right, x + w);
                bottom = Math.Max(bottom, y + h);

                Warn(DesignCodes.RetargetLayoutCrossed,
                    $"'{control.Id}' was {was} — a cell, which a window laid out in pixels cannot express. " +
                    $"It was placed at X={x} Y={y} with Width={w} Height={h} (its catalog size, cells " +
                    "pitched to the largest sibling); review the position.");
            }

            return (right, bottom);
        }

        // ==============================================================

        private void Warn(string code, string message) =>
            Diagnostics.Add(new DesignDiagnostic(code, $"{code}: {message}", _source.SourcePath, 0, 0, IsWarning: true));
    }
}
