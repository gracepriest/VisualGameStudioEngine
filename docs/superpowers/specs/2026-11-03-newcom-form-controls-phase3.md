# Phase 3 — NewCOM Controls on Forms (Design Spec)

Status: **Complete** — runtime host (`NewCom.Runtime`: `NewComLoader` + `NewComControlHost` with vtable marshalling for SetBounds/SetVisible), control-mode component emission (IL2CPP emits `kind: control` manifest + GetHwnd/SetParent/SetBounds/SetVisible thunks), and designer foundation (FormSchematic.NewComControl enum + glyph + render branch + catalog row "NewComControl") are implemented. Remaining: window parenting (HWND), Clsid storage + constructor-arg emission (DONE), toolbox population from manifests, property grid, typed events.

## Goal

A NewCOM component that implements a *control contract* can be dropped onto a form in the Visual Game Studio designer, persisted, code-generated, and run — like an ActiveX control, but cross-platform and language-agnostic.

## Contract

A component opts into being a control by adding `"kind": "control"` to its `newcom.json` manifest and implementing an additional vtable interface:

```cpp
struct INewComControlVtbl {
    // INewUnknown base
    int32_t  (*QueryInterface)(INewUnknown*, const NewComGUID*, void**);
    uint32_t (*AddRef)(INewUnknown*);
    uint32_t (*Release)(INewUnknown*);
    // Control contract
    void*    (*GetHwnd)(INewUnknown* self);            // native window handle (HWND / X11 / NSView)
    void     (*SetParent)(INewUnknown* self, void* parentHwnd);
    void     (*SetBounds)(INewUnknown* self, int x, int y, int w, int h);
    void     (*SetVisible)(INewUnknown* self, bool v);
    // Properties are accessed via a generic GetProperty/SetProperty pair (string-keyed)
    // or via manifest-declared typed slots; events fire through a host-provided callback.
};
```

The manifest gains:
```json
{ "name": "MyWidget", "kind": "control", "clsid": "...", "library": "MyWidget_newcom.dll", "factory": "MyWidget_Create" }
```

## Discovery → Toolbox

1. The IDE's `FormControlCatalog` gains a *dynamic* section populated at startup by scanning `./components/`, `~/.newcom/`, `$NEWCOM_PATH/` for `newcom.json` manifests with `"kind": "control"`.
2. Each such manifest becomes a `FormControlDef`-equivalent row:
   - `Place = Positioned` (has geometry, lives in `Controls`, gets `Controls.Add`).
   - `WinFormsType = "VisualGameStudio.Runtime.NewComControlHost"` (a generic host; see Runtime Host).
   - `SupportsTarget(WinForms) = true`, `SupportsTarget(Web) = false` initially.
   - `Schematic` = a new `FormSchematic.NewComControl` glyph (`(*)`-style).
3. The toolbox shows them under a "NewCOM Controls" category, draggable onto the canvas.

## Runtime host (the AxHost equivalent)

A managed class shipped with the SDK/runtime:

```csharp
public class NewComControlHost : System.Windows.Forms.Control
{
    private IntPtr _native;          // INewUnknown* (as IntPtr)
    private readonly string _clsid;

    public NewComControlHost(string clsid) {
        _clsid = clsid;
        _native = NewComLoader.Create(clsid);   // P/Invoke newcom_loader
        // parent the native window into this Control's handle via SetParent contract
    }
    protected override void WndProc(ref Message m) { /* forward to native */ base.WndProc(ref m); }
    protected override void OnResize(EventArgs e) { /* SetBounds */ base.OnResize(e); }
    protected override void Dispose(bool disposing) { /* Release */ base.Dispose(disposing); }
}
```

`NewComLoader` is a small P/Invoke wrapper over `newcom_loader.cpp` (already exists). The generated form code instantiates `New NewComControlHost("<clsid>")` instead of a concrete type.

## Persistence

The `.blform` XML gains nothing new: a NewCOM control is just a control row with `Kind="NewComControl"` and an attribute `Clsid="<guid>"`. The existing `ReadControl`/`ApplyControl` machinery handles geometry, tab index, properties, and binds generically. Only the `DeclaredType` resolution in `RegionWriter` needs a branch: `if (control.Definition.IsNewComControl) return "NewComControlHost"` (plus a Clsid constructor arg).

## Code generation

`RegionWriter` already emits `Dim x As New <Type>()` + `Me.Controls.Add(x)` for positioned controls. For a NewCOM control it emits:

```vb
Dim x As New NewComControlHost("<clsid>")
x.Location = New Point(...)
x.Size = New Size(...)
Me.Controls.Add(x)
```

Properties from the manifest's property list are emitted as `x.SetProperty("Name", value)` calls (generic) or typed setters if declared.

## Events

NewCOM gains an event slot: the component calls a host-provided `INewComHost` callback interface (`OnEvent(name, args)`). The form's generated code subscribes via `AddHandler x.EventName, AddressOf handler` only if the manifest declares the event; otherwise events are surfaced as a single `Event Occurred(name, args)` that the user switches on. Phase 3 ships the generic single-event form; typed events are Phase 3b.

## Manifest extensions

```json
{
  "name": "MyWidget",
  "kind": "control",
  "clsid": "...",
  "library": "MyWidget_newcom.dll",
  "factory": "MyWidget_Create",
  "properties": ["Text", "Color", "Value"],
  "events": ["Click", "ValueChanged"],
  "defaultSize": [100, 30]
}
```

## What's NOT in this spec (deferred)

- Web target hosting (script-based component, separate path).
- Typed events (generic single event first).
- Property grid editing of manifest-declared properties (needs a dynamic property descriptor).
- Designer-time rendering of the native control (designer shows a placeholder rectangle with the clsid + name until Run).

## Implementation order (for a future session)

1. `NewComControlHost` + `NewComLoader` in the SDK/runtime assembly (new project).
2. Catalog: dynamic section + `FormSchematic.NewComControl`.
3. Serialization: `Clsid` attribute read/write (generic, no new node type).
4. `RegionWriter.DeclaredType` branch + constructor-arg emission.
5. Toolbox population from manifests.
6. Property grid dynamic properties.
7. Typed events (3b).

## Why not implemented now

The runtime host is a .NET assembly that must ship with the SDK and be referenced by every WinForms project that uses a NewCOM control. Building, versioning, and deploying it is a project-creation + packaging task that touches the installer, the MSBuild targets, and the engine deployment — beyond the scope of a single session. The foundation (Phase 1 factory + Phase 2 build routing) is complete; Phase 3's runtime host is the remaining deliverable.