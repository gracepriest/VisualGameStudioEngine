title: Extension host
lede: A Node-based host that runs real VS Code extensions inside the IDE — and an honest account of what does not work yet.
---
The IDE can install and activate VS Code extensions. Extensions run **out of process** in
Node, exactly as they do in VS Code, and talk to the IDE over a JSON-RPC protocol.

> [trap] The host tier is <span class="pill warn">unproven end to end</span>. Everything upstream
> is proven — install, extract, discover, manifest parse, static contributions, theme and grammar
> registration, `onLanguage` firing — but no test ever starts the real host. The extension-host
> fixtures are source guards and payload-binding unit tests; `ExtensionHostStartupTests` says so
> outright ("exercising these paths for real starts a Node.js child process"), none of them carries
> `[Category("Integration")]`, and nothing in the suite constructs an `ExtensionHost`. No extension
> with a `main` has been observed activating through this IDE's UI, and there is no end-to-end
> integration test. Read "the tests are green" as "the wiring has not regressed", not as "the host
> works".

## Pieces

| Piece | Where |
|---|---|
| Host process management | `VisualGameStudio.ProjectSystem/Services/ExtensionHost.cs` |
| Host implementation (JavaScript) | `Services/ExtensionHost/` — 16 files, 7,288 lines, entry point `main.js` (`rpc.js`, `document-manager.js`, `provider-registry.js`, `vscode-api/`, `utils/`). `Services/ExtensionHostMain.js` is the superseded monolith — still last in the probe chain, but not deployed |
| Node discovery | `BasicLang/Runtime/NodeLocator.cs` |
| Extension lifecycle | `ExtensionService.cs`, `ExtensionManager.cs` |
| VSIX installation | `VsixInstaller.cs` (with `SafeZip` for archive safety) |
| Extension file system and workspace views | `ExtensionFileSystem.cs`, `ExtensionWorkspace.cs` |
| Marketplace | `OpenVsxClient.cs` — Open VSX, a DI singleton shared with `VsixInstaller` and used directly by `ExtensionsViewModel`. `MarketplaceService.cs` is dead code (no consumer, fictional endpoint) |
| Contribution points | `ExtensionService` — `LoadContributionsAsync` loads themes, grammars and snippets and registers contributed commands/keybindings with the IDE; `ParseContributedCommands` / `ParseContributedKeybindings` / `ParseContributedMenus` run during manifest parse. The `IContributionService` interface has no implementation and no consumer |
| UI | `ExtensionsViewModel.cs` / `ExtensionItemViewModel.cs` |

Design documents: `docs/superpowers/plans/2026-03-20-nodejs-extension-host.md`,
`docs/superpowers/specs/2026-03-20-nodejs-extension-host-design.md`, and
`docs/superpowers/specs/2026-08-05-extension-manifest-binding-design.md`.

## Installing an extension

Open the **Extensions** panel, search the Open VSX marketplace, install. A `.vsix` can
also be installed from disk through `IExtensionService.InstallFromFileAsync` — the service API and
its tests exist, but no Extensions-panel control calls it yet. The manifest is parsed, each
`contributes` section binds in isolation (a section that fails to bind is reported and skipped
rather than killing the extension), contribution points are registered,
and the extension is activated on its declared activation events.

## What is not implemented

This is tracked rather than hidden. `ExtensionHostRequestCoverageTests.KnownUnimplemented`
enumerates exactly **21 unimplemented requests**, and a second test fails as soon as one of
them *is* implemented — so the list can only shrink, never quietly grow.

Three failure modes to know, because they look nothing alike:

- **A missing `sendNotification` handler is a silent no-op.** 33 of the 45 notification kinds the
  host can send have no handler, and nothing enumerates them — the coverage test's regex matches
  `sendRequest(` only, so the whole `terminal/*`, `tasks/*`, `debug/*`, `webviewView/*` and
  `window/withProgress` surface is untracked. The extension keeps running
  and simply never gets what it asked for.
- **A missing `sendRequest` handler rejects inside `activate()`** and kills the extension
  outright.
- **A registered handler can still be a stub, and that is the worst of the three.**
  `workspace/applyEdit` logs a line, returns `true`, and discards the edit
  (`ExtensionHost.cs:1118-1122`, body `return true; // TODO: apply workspace edit to IDE`).
  An extension that formats or refactors is told it succeeded while nothing changed.
  `ExtensionHostRequestCoverageTests.ApplyEditIsRegisteredButStillAStub` fails if the TODO
  disappears, so implementing it forces a decision rather than passing silently.

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
