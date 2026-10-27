using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BasicLang.Forms;

/// <summary>
/// Γ¢öΓ¢ö A Canvas page's RUN-TIME re-docking (owner decision 2026-09-27). When user code shows or hides a docked control
/// (or a strip) ΓÇö <c>el.style.display</c>, <c>el.hidden</c>, a class, or piece 2's portable <c>Visible</c> ΓÇö WinForms
/// re-docks the siblings; the page does too, in BOTH states. The stylesheet is the page's first state
/// (<see cref="FormDockMode.Runtime"/>); this script runs only on a change.
///
/// <para>Γ¢öΓ¢ö <see cref="Core"/> is a MIRROR of <see cref="FormDockLayout.ResolveSiblings"/>, <c>FormDockLayout.Walk</c>
/// (Runtime) and <see cref="FormAnchorCss.Docked"/>, in JavaScript: a mirrored pair across two languages ΓÇö the
/// <c>Tracks</c>/<c>ParseTracks</c> scar, a third time, unless it is gated. <c>FormDockScriptTests</c> runs both over one
/// fixture table under node and requires byte-identical answers. Change both in ONE commit.</para>
///
/// <para>The hook is a <c>MutationObserver</c> on <c>.vgs-form</c> (attributes <c>style</c>/<c>class</c>/<c>hidden</c>,
/// subtree): there is no <c>Visible</c> setter on the web to hook ΓÇö a control's field is a DOM <c>Element</c> and user
/// code writes the DOM directly (measured). Its callback is a microtask, so the re-dock lands before the next paint.
/// The live rules go into ONE <c>&lt;style id="vgs-dock-live"&gt;</c> in <c>&lt;head&gt;</c> (later in the cascade
/// than the page's stylesheet, same specificity), inside <c>@media (width &gt;= breakpoint)</c> so the phone column is
/// never overridden.</para>
/// </summary>
public static class FormDockScript
{
    /// <summary>The pure resolver, mirrored from C#. ΓÜá Line endings normalised: a raw literal takes the source file's.</summary>
    public static readonly string Core = ResolverCore(true);
    private static readonly string LegacyCore = ResolverCore(false);

    // One template: the legacy expansion stays byte-identical, while portable nodes carry
    // the catalog-resolved client metrics. There is no JavaScript kind/border lookup table.
    private static string ResolverCore(bool clientAreas) => $$"""
        // vgs-dock: re-docks a Canvas page when a docked control is shown or hidden at run time, as WinForms does.
        // MIRROR of BasicLang FormDockLayout.ResolveSiblings / Walk (Runtime) and FormAnchorCss.Docked.
        // FormDockScriptTests runs this and the C# over one table; change both in one commit.
        function vgsDockSiblings(nodes, width, height, hidden{{(clientAreas ? ", padding" : "")}}) {
          var cw = Math.max(0, width), ch = Math.max(0, height);
          var x = 0, y = 0, w = cw, h = ch, placed = [];{{(clientAreas ? "\n  padding = padding || [0, 0, 0, 0]; x = padding[0]; y = padding[1]; w -= padding[0] + padding[2]; h -= padding[1] + padding[3];" : "")}}
          for (var n = 0; n < nodes.length; n++) {
            var node = nodes[n];
            if (node.e === null || hidden(node)) continue;
            var aw = Math.max(0, w), ah = Math.max(0, h), b;
            if (node.e === "Top") { b = [x, y, aw, node.h]; y += node.h; h -= node.h; }
            else if (node.e === "Bottom") { b = [x, y + h - node.h, aw, node.h]; h -= node.h; }
            else if (node.e === "Left") { b = [x, y, node.w, ah]; x += node.w; w -= node.w; }
            else if (node.e === "Right") { b = [x + w - node.w, y, node.w, ah]; w -= node.w; }
            else { b = [x, y, aw, ah]; }
            placed.push({ n: node, e: node.e, b: b, cw: cw, ch: ch });
          }
          return placed;
        }
        function vgsDockWalk(nodes, width, height, hidden, out{{(clientAreas ? ", padding" : "")}}) {
          var placed = vgsDockSiblings(nodes, width, height, hidden{{(clientAreas ? ", padding" : "")}});
          for (var p = 0; p < placed.length; p++) out.push(placed[p]);
          for (var n = 0; n < nodes.length; n++) {
            var node = nodes[n];
            if (!node.c || hidden(node)) continue;
            var iw = node.w, ih = node.h;
            for (var q = 0; q < placed.length; q++) {
              if (placed[q].n === node) { iw = placed[q].b[2]; ih = placed[q].b[3]; break; }
            }
            {{(clientAreas ? "var inset = node.a || 0; iw = Math.max(0, iw - 2 * inset); ih = Math.max(0, ih - 2 * inset);\n    " : "")}}if (node.k.length > 0) vgsDockWalk(node.k, iw, ih, hidden, out{{(clientAreas ? ", node.p" : "")}});
          }
        }
        function vgsDockCss(d) {
          var b = d.b;
          function px(v) { return v + "px"; }
          function st(v) { return v < 0 ? "calc(100% + " + (-v) + "px)" : "calc(100% - " + v + "px)"; }
          var right = px(d.cw - (b[0] + b[2])), bottom = px(d.ch - (b[1] + b[3]));
          var width = st(d.cw - b[2]), height = st(d.ch - b[3]);
          if (d.e === "Top") return [["left", px(b[0])], ["right", right], ["width", width], ["top", px(b[1])], ["height", px(b[3])]];
          if (d.e === "Bottom") return [["left", px(b[0])], ["right", right], ["width", width], ["bottom", bottom], ["height", px(b[3])]];
          if (d.e === "Left") return [["left", px(b[0])], ["width", px(b[2])], ["top", px(b[1])], ["bottom", bottom], ["height", height]];
          if (d.e === "Right") return [["right", right], ["width", px(b[2])], ["top", px(b[1])], ["bottom", bottom], ["height", height]];
          return [["left", px(b[0])], ["right", right], ["width", width], ["top", px(b[1])], ["bottom", bottom], ["height", height]];
        }{{(clientAreas ? "\nfunction vgsDockComplete(decls) {\n  var values = {}, names = [\"left\", \"right\", \"width\", \"top\", \"bottom\", \"height\"], out = [];\n  for (var i = 0; i < decls.length; i++) values[decls[i][0]] = decls[i][1];\n  for (var n = 0; n < names.length; n++) out.push([names[n], values[names[n]] === undefined ? \"auto\" : values[names[n]]]);\n  return out;\n}\n" : "")}}

        """.ReplaceLineEndings("\n");

    /// <summary>The DOM glue: observe, re-walk the live tree, write the live rules.</summary>
    public static readonly string Bootstrap = """
        function vgsDockStart(root, breakpoint) {
          var form = document.querySelector(".vgs-form");
          if (!form) return;
          var live = null, last = null, pending = false;
          function vgsDockLive(form) {
            function num(el, name, fallback) {
              var raw = el.getAttribute(name);
              if (raw === null || raw === "") return fallback;
              var n = Number(raw);
              return isNaN(n) ? fallback : n;
            }
            function hidden(el) {
              if (el.hidden) return true;
              try { return getComputedStyle(el).display === "none"; } catch (e) { return false; }
            }
            function pad(el) {
              var raw = el.getAttribute("data-vgs-pad");
              if (!raw) return [0, 0, 0, 0];
              var p = raw.split(",");
              return [Number(p[0]) || 0, Number(p[1]) || 0, Number(p[2]) || 0, Number(p[3]) || 0];
            }
            function walk(el) {
              var out = [], kids = el.children || [];
              for (var i = 0; i < kids.length; i++) {
                var child = kids[i];
                if (!child || !child.getAttribute || child.getAttribute("data-vgs-control") === null) continue;
                var area = child.getAttribute("data-vgs-cw") !== null && child.getAttribute("data-vgs-ch") !== null;
                var edge = child.getAttribute("data-vgs-dock");
                if (edge === "None" || edge === "") edge = null;
                out.push({
                  i: child.id || "",
                  e: edge,
                  w: num(child, "data-vgs-w", area ? num(child, "data-vgs-cw", 0) : 0),
                  h: num(child, "data-vgs-h", area ? num(child, "data-vgs-ch", 0) : 0),
                  c: area,
                  hd: hidden(child),
                  k: area ? walk(child) : [],
                  a: area ? num(child, "data-vgs-inset", 0) : 0,
                  p: area ? pad(child) : null
                });
              }
              return out;
            }
            var w = form.clientWidth || num(form, "data-vgs-cw", 0);
            var h = form.clientHeight || num(form, "data-vgs-ch", 0);
            return { i: "", e: null, w: w, h: h, c: true, hd: false, k: walk(form), a: 0, p: [0, 0, 0, 0] };
          }
          function compute() {
            var tree = vgsDockLive(form);
            // The emitted JSON seeds the first picture; a page without live markers keeps it.
            if (tree.k.length === 0 && root.k.length > 0) tree = root;
            var out = [];
            vgsDockWalk(tree.k, tree.w, tree.h, function (n) { return n.hd; }, out, tree.p);
            var rules = "";
            for (var r = 0; r < out.length; r++) {
              var decls = vgsDockComplete(vgsDockCss(out[r])), text = "";
              for (var d = 0; d < decls.length; d++) text += decls[d][0] + ":" + decls[d][1] + ";";
              rules += "#" + out[r].n.i + "{" + text + "}";
            }
            var css = breakpoint > 0 ? "@media (width >= " + breakpoint + "px){" + rules + "}" : rules;
            if (css === last) return;
            last = css;
            if (!live) {
              live = document.createElement("style");
              live.id = "vgs-dock-live";
              document.head.appendChild(live);
            }
            live.textContent = css;
          }
          function reflow() { pending = false; compute(); }
          function request() {
            if (pending) return;
            pending = true;
            if (typeof queueMicrotask === "function") queueMicrotask(reflow);
            else Promise.resolve().then(reflow);
          }
          window.vgsDock = { request: request, reflow: reflow };
          if (typeof MutationObserver !== "undefined") {
            new MutationObserver(request).observe(form, { attributes: true, subtree: true, attributeFilter: ["style", "class", "hidden"] });
          }
        }

        """.ReplaceLineEndings("\n");

    /// <summary>Piece 1's glue, frozen for DOM-style pages (spec 7.7).</summary>
    private static readonly string LegacyBootstrap = """
        function vgsDockStart(root, breakpoint) {
          var form = document.querySelector(".vgs-form");
          if (!form || typeof MutationObserver === "undefined") return;
          var live = null, last = null;
          function hidden(node) {
            var el = document.getElementById(node.i);
            return el ? getComputedStyle(el).display === "none" : node.hd;
          }
          function reflow() {
            var out = [];
            vgsDockWalk(root.k, root.w, root.h, hidden, out);
            var rules = "";
            for (var r = 0; r < out.length; r++) {
              var decls = vgsDockCss(out[r]), text = "";
              for (var d = 0; d < decls.length; d++) text += decls[d][0] + ":" + decls[d][1] + ";";
              rules += "#" + out[r].n.i + "{" + text + "}";
            }
            var css = breakpoint > 0 ? "@media (width >= " + breakpoint + "px){" + rules + "}" : rules;
            if (css === last) return;
            last = css;
            if (!live) {
              live = document.createElement("style");
              live.id = "vgs-dock-live";
              document.head.appendChild(live);
            }
            live.textContent = css;
          }
          new MutationObserver(reflow).observe(form, { attributes: true, subtree: true, attributeFilter: ["style", "class", "hidden"] });
        }

        """.ReplaceLineEndings("\n");

    /// <summary>
    /// The inline classic script for <paramref name="form"/>'s page, or null when nothing on it docks. An IIFE, so
    /// nothing leaks into the page's globals beside App.js.
    /// </summary>
    public static string? PageScript(FormDocument form, bool clientAreas = false)
    {
        ArgumentNullException.ThrowIfNull(form);

        var root = Root(form, clientAreas);
        if (!clientAreas && !Docks(root.Children))
        {
            return null;
        }

        var breakpoint = form.Layout?.EffectiveMobileBreakpoint ?? FormLayout.DefaultMobileBreakpoint;
        var sb = new StringBuilder();
        sb.Append("<script data-vgs=\"dock\">\n(function () {\n");
        sb.Append(clientAreas ? Core : LegacyCore).Append(clientAreas ? Bootstrap : LegacyBootstrap);
        sb.Append("vgsDockStart(").Append(JsonSerializer.Serialize(root)).Append(", ")
          .Append(breakpoint.ToString(CultureInfo.InvariantCulture)).Append(");\n");
        sb.Append("})();\n</script>\n");
        return sb.ToString();
    }

    /// <summary>
    /// The script's data for <paramref name="form"/>: the root client size (<see cref="FormDocument.DesignSize"/>) and,
    /// per sibling list in DOCUMENT order, everything that docks or holds something that does ΓÇö each node's edge
    /// (<see cref="FormDockLayout.EdgeOf"/>), own size (<see cref="FormDockLayout.OwnSizeOf"/>), whether it lays
    /// children out (<see cref="FormDockLayout.HasClientArea"/>) and whether it is designed hidden. Γ¢ö The resolver's
    /// own rules, never copies. ΓÜá JSON-escaped, so no id can close the <c>&lt;script&gt;</c>.
    /// </summary>
    public static string DataJson(FormDocument form, bool clientAreas = true)
    {
        ArgumentNullException.ThrowIfNull(form);
        return JsonSerializer.Serialize(Root(form, clientAreas));
    }

    private static FormDockNode Root(FormDocument form, bool clientAreas)
    {
        var (width, height) = form.DesignSize;
        return new FormDockNode("", null, width, height, true, false, Nodes(form.Controls, clientAreas));
    }

    private static List<FormDockNode> Nodes(IReadOnlyList<FormControl> siblings, bool clientAreas)
    {
        var nodes = new List<FormDockNode>();
        foreach (var control in siblings)
        {
            var edge = FormDockLayout.EdgeOf(control);
            var hasClientArea = FormDockLayout.HasClientArea(control);
            var children = hasClientArea ? Nodes(control.Children, clientAreas) : new List<FormDockNode>();

            // Neither docks nor holds anything that does: it consumes nothing, so the script need not know it.
            if (edge == null && children.Count == 0)
            {
                continue;
            }

            var (width, height) = FormDockLayout.OwnSizeOf(control);
            var area = FormClientArea.Of(control);
            var pad = area.DockPadding;
            nodes.Add(new FormDockNode(control.Id, edge?.ToString(), width, height, hasClientArea, control.IsHidden, children,
                clientAreas ? area.OriginInset : null,
                clientAreas ? new[] { pad.Left, pad.Top, pad.Right, pad.Bottom } : null));
        }

        return nodes;
    }

    private static bool Docks(IEnumerable<FormDockNode> nodes) =>
        nodes.Any(n => n.Edge != null || Docks(n.Children));
}

/// <summary>One node of <see cref="FormDockScript.DataJson"/>. Short names: this ships in every docked page.</summary>
internal sealed record FormDockNode(
    [property: JsonPropertyName("i")] string Id,
    [property: JsonPropertyName("e")] string? Edge,
    [property: JsonPropertyName("w")] int Width,
    [property: JsonPropertyName("h")] int Height,
    [property: JsonPropertyName("c")] bool HasClientArea,
    [property: JsonPropertyName("hd")] bool DesignHidden,
    [property: JsonPropertyName("k")] IReadOnlyList<FormDockNode> Children,
    [property: JsonPropertyName("a"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? OriginInset = null,
    [property: JsonPropertyName("p"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int[]? DockPadding = null);
