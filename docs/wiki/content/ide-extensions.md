title: Extension host
lede: A Node-based host that runs real VS Code extensions inside the IDE — and an honest account of what does not work yet.
---
The IDE can install and activate VS Code extensions. Extensions run **out of process** in
Node, exactly as they do in VS Code, and talk to the IDE over a JSON-RPC protocol.

## Pieces

| Piece | Where |
|---|---|
| Host process management | `VisualGameStudio.ProjectSystem/Services/ExtensionHost.cs` |
| Host implementation (JavaScript) | `Services/ExtensionHostMain.js` and `Services/ExtensionHost/` |
| Node discovery | `BasicLang/Runtime/NodeLocator.cs` |
| Extension lifecycle | `ExtensionService.cs`, `ExtensionManager.cs` |
| VSIX installation | `VsixInstaller.cs` (with `SafeZip` for archive safety) |
| Extension file system and workspace views | `ExtensionFileSystem.cs`, `ExtensionWorkspace.cs` |
| Marketplace | `MarketplaceService.cs`, `OpenVsxClient.cs` — Open VSX |
| Contribution points | `IContributionService` |
| UI | `ExtensionsViewModel.cs` / `ExtensionItemViewModel.cs` |

Design documents: `docs/superpowers/plans/2026-03-20-nodejs-extension-host.md`,
`docs/superpowers/specs/2026-03-20-nodejs-extension-host-design.md`, and
`docs/superpowers/specs/2026-08-05-extension-manifest-binding-design.md`.

## Installing an extension

Open the **Extensions** panel, search the Open VSX marketplace, install. A `.vsix` can
also be installed from disk. The manifest is parsed, contribution points are registered,
and the extension is activated on its declared activation events.

## What is not implemented

This is tracked rather than hidden. `ExtensionHostRequestCoverageTests.KnownUnimplemented`
enumerates roughly **24 unimplemented requests**, and a second test fails as soon as one of
them *is* implemented — so the list can only shrink, never quietly grow.

Two failure modes to know, because they look nothing alike:

- **A missing `sendNotification` handler is a silent no-op.** The extension keeps running
  and simply never gets what it asked for.
- **A missing `sendRequest` handler rejects inside `activate()`** and kills the extension
  outright.

Also outstanding: **webviews still render as source text.** An extension whose UI is a
webview will display markup rather than a rendered view.

Recovery work is tracked in
`docs/superpowers/specs/2026-08-05-extensions-recovery-ledger.md`.

## Why a Node host at all

Two reasons, both practical. First, VS Code extensions are the largest existing body of
editor tooling, and the API is JavaScript — reimplementing it in C# would mean
reimplementing it wrong. Second, out-of-process isolation means a misbehaving extension
cannot take the IDE down with it.

## Related: the IDE's own extensions

Separately from this host, BasicLang ships editor integrations *for other editors* — a
VS 2022 VSIX and a VS Code extension. Those are covered in
[Editor integrations](#/editors).
