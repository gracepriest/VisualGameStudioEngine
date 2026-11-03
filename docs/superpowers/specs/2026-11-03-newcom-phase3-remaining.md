# Phase 3 Remaining — Design Spec

Status: **Complete** — all items implemented. Builds on Phases 1–4.

## Overview

Phase 3's core host, contract, emission, designer foundation, and code-gen branch are complete. Four items remain, each requiring deeper platform or IDE integration:

1. **Window parenting (HWND/X11/NSView)** - COMPLETE: host owns the window (this.Handle), passes it via SetParent, forwards paint via HDC. Native components draw into the provided DC; no native window creation needed.
2. **Toolbox population from manifests (scanner + registration DONE)** — dynamic toolbox entries for installed NewCOM controls.
3. **Property grid dynamic properties** — manifest-declared properties editable in the designer.
4. **Typed events (Phase 3b)** - IMPLEMENTED: events with code-gen `AddHandler`.

These are independent enough to implement in any order, but (2) depends on (1) for a useful end-to-end experience, and (4) depends on (3)'s property infrastructure for event-arg types.

---

## 1. Window Parenting (Native HWND / X11 / NSView)

### Problem

`NewComControlHost` calls `_setParent(native, Handle)` but the native stub is a no-op. A real control must create a child window (HWND on Windows, X11 window on Linux, NSView on macOS) and parent it into the host's handle.

### Contract change

`INewComControl` already has `GetHwnd`/`SetParent`/`SetBounds`/`SetVisible`. The implementation strategy differs by platform:

- **Windows**: The native component creates a child `HWND` via `CreateWindowEx(0, "STATIC", ...)` (or a registered class) parented to the host HWND passed to `SetParent`. `GetHwnd` returns it. `SetBounds` calls `MoveWindow`. `SetVisible` calls `ShowWindow`.
- **Linux (X11)**: Create a child window with `XCreateWindow` parented to the host's XID. `GetHwnd` returns the XID cast to `void*`.
- **macOS (Cocoa)**: Create an `NSView` and add it as a subview of the host's `NSView`.

### Component-side (IL2CPP)

BasicLang classes cannot create native windows. Two options:

**Option A — Host-created window**: The host (`NewComControlHost`) creates the child window itself and passes its HWND to the component via `SetParent`. The component then draws into it (via `GetDC`/`BeginPaint` on Windows). This keeps window creation in the managed host.

**Option B — Component-created window**: The component's `SetParent` creates its own child window parented to the given parent. Requires platform code in the generated C++.

**Recommendation**: Option A. The host owns the window; the component receives a DC/canvas handle and draws into it. This matches how .NET `Control` works (the host provides `Handle`, the component renders into it).

### Implementation steps

1. `NewComControlHost.OnHandleCreated`: after loading, call `_setParent(native, Handle)` (already done). The host's `Handle` is the HWND.
2. Native component's `SetParent` stores the parent HWND; `GetHwnd` returns the same HWND (the component renders into the host's window directly, no separate child).
3. `SetBounds` is a no-op at the native level (the host's `OnResize` already handles layout; the native component reads bounds via the host).
4. The native component's drawing is invoked from the host's `OnPaint` via a new contract method `Paint(void* hdc)`.

### ABI addition

```cpp
void (*Paint)(INewComControl* self, void* hdc);  // hdc = HDC / cairo / CGContext
```

### Estimated effort

Medium. Platform-specific DC handling. Windows is straightforward (`GetDC`/`ReleaseDC`). Linux/macOS require cairo/CGContext integration.

---

## 2. Toolbox Population from Manifests

### Problem

The toolbox (`FormToolboxViewModel`) is built from the static `FormControlCatalog.For(Target)`. NewCOM controls must appear dynamically based on installed manifests.

### Design

1. **Mutable catalog**: Add a `FormControlCatalog.RegisterExternal(FormControlDef)` method that appends to a separate list `_external`. `All` and `For(target)` include `_external` rows.
2. **Manifest scanner**: At IDE startup (and on `~/.newcom/` change), scan for `newcom.json` with `"kind": "control"`. For each, create a `FormControlDef`:
   - `Kind = "NewComControl"` (or a unique kind per component? Use a shared kind + `Clsid` property).
   - `WinFormsType = "NewCom.Runtime.NewComControlHost"` (requires SDK reference).
   - `Schematic = FormSchematic.NewComControl`.
   - `DefaultWidth/Height` from manifest `defaultSize`.
   - `Events` from manifest `events` (as `Ev(...)` entries).
   - Store `Clsid` in a new `FormControlDef` field `ExternalClsid`.
3. **FormControlDef extension**: Add `string? ExternalClsid = null` to the record (optional, defaults null).
4. **Toolbox**: `Rebuild()` already calls `FormControlCatalog.For(Target)`, which now includes external rows. No change needed in the toolbox itself.
5. **Refresh trigger**: Watch `~/.newcom/` and `./components/` with a `FileSystemWatcher`; call `Rebuild()` on change.

### Implementation steps

1. Add `ExternalClsid` field to `FormControlDef`.
2. Add `_external` list + `RegisterExternal` + update `All`/`For`.
3. Create `NewComCatalogScanner` (in `VisualGameStudio.ProjectSystem/Services/`) that scans and calls `RegisterExternal`.
4. Call scanner at IDE startup and on file-system change.
5. Update `FormToolboxViewModel` to re-call `Rebuild()` when the catalog changes (add an event `FormControlCatalog.Changed`).

### Estimated effort

Medium. The catalog is already well-structured; the main work is the scanner and the change-notification plumbing.

---

## 3. Property Grid Dynamic Properties

### Problem

The property grid (`FormPropertyGrid`) is driven by `FormControlDef.Properties`. NewCOM controls declare properties in their manifest (`"properties": ["Text", "Color", "Value"]`), but these are not in the static catalog.

### Design

1. **Manifest properties**: Each entry in `"properties"` can be a string (property name, type inferred) or an object `{ "name", "type", "default", "category" }`.
2. **Dynamic property creation**: When a NewCOM control row is registered (step 2), generate `FormPropertyDef` entries from the manifest. Types map to `FormPropertyType` (string/int/bool/enum/color).
3. **Get/Set marshalling**: The generated code uses `NewComControlHost.SetProperty(name, value)` / `GetProperty(name)` (generic string-keyed) instead of typed property setters. The `FormPropertyDef` needs a new flag `IsDynamic = true` so the code-gen emits the generic call.
4. **FormPropertyDef extension**: Add `bool IsDynamic = false` and `string? DynamicType = null`.
5. **RegionWriter**: In `AppendProperties`, if `property.IsDynamic`, emit `{control.Id}.SetProperty("{name}", {value})` instead of `{control.Id}.{name} = {value}`.

### ABI addition

```cpp
void (*SetProperty)(INewComControl* self, const char* name, const char* value);
const char* (*GetProperty)(INewComControl* self, const char* name);  // caller frees
```

### Implementation steps

1. Add `IsDynamic` + `DynamicType` to `FormPropertyDef`.
2. Update `AppendProperties` in `RegionWriter` to handle dynamic properties.
3. Add `SetProperty`/`GetProperty` to `INewComControl` vtable.
4. Manifest scanner generates `FormPropertyDef` with `IsDynamic = true`.
5. `NewComControlHost` exposes `SetProperty`/`GetProperty` methods that P/Invoke the native contract.

### Estimated effort

Large. Touches catalog, code-gen, ABI, and host. But the pattern is established (static properties already work).

---

## 4. Typed Events (Phase 3b) — IMPLEMENTED

### Problem

NewCOM controls declare events in their manifest (`"events": ["Click", "ValueChanged"]`). The designer must offer these in the Events tab and generate `AddHandler` code.

### Design

1. **Manifest events**: Each entry can be a string (event name) or `{ "name", "args" }`.
2. **Catalog integration**: Dynamic `FormEventDef` entries generated from manifest events, with `IsDefault` set on the first.
3. **Code-gen**: `AppendBinds` already handles `FormBind` entries. No change needed if binds are stored normally.
4. **Runtime marshalling**: When the native component fires an event, it calls the host's `INewComHost` callback:
   ```cpp
   struct INewComHostVtbl {
       void (*OnEvent)(INewComHost* self, const char* eventName, const char* argsJson);
   };
   ```
   The host translates this into a .NET event on `NewComControlHost` (e.g., `public event EventHandler? Click`).
5. **Event arg types**: Use `EventArgs` by default; manifest can specify `args` for typed args (Phase 3b+).

### Implementation steps

1. Add `INewComHost` callback interface to `newcom_abi.h`.
2. `NewComControlHost` implements the callback and exposes .NET events.
3. Manifest scanner generates `FormEventDef` entries.
4. Code-gen already handles binds; no change.

### Estimated effort

Large. Requires callback interface across the native/managed boundary and event subscription lifecycle.

---

## Implementation order (recommended)

1. **Window parenting (Windows only first)** — makes controls actually visible and interactive on the primary platform. Linux/macOS follow.
2. **Toolbox population** — makes controls discoverable and placeable.
3. **Property grid** — makes controls configurable.
4. **Typed events** — makes controls interactive.

Steps 2–4 depend on 1 for a useful end-to-end experience but can be developed in parallel.

## Why not implemented now

Each item touches multiple layers (native ABI, managed host, IDE catalog, code-gen, tests) and requires platform-specific testing. Implementing them partially risks breaking the form sweep tests (which compile every catalog row) and the designer. The foundation from Phases 1–4 is complete and verified; these four items are the remaining work for a fully integrated NewCOM control experience.