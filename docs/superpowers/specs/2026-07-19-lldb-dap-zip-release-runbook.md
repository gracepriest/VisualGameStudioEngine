# lldb-dap Zip Release Runbook — build, gate, publish, pin

**Date:** 2026-07-19
**Status:** Runbook — authored with Phase 4 (Task 13), executed at release time. A
phase-end chip tracks executing it; until then the placeholder pins stand and the
download UX stays gated (§4).
**Parent spec:** `2026-07-19-cpp-lldb-dap-phase4-design.md` §5 — the zip is a one-time
build-pipeline deliverable, deliberately off the phase's critical path (the dev
machine's winlibs lldb-dap 19.1.7 unblocks all development and e2e today).
**Consumers:** `VisualGameStudio.ProjectSystem/Services/LldbDapInstaller.cs` (the four
release pins, §4), `LldbDapLocator.FindInToolsRoot` (the layout contract, §2),
`VisualGameStudio.Shell/Services/LldbDapDownloadFlow.cs` (the release-pin gate, §4).

**Why self-hosted at all:** the official LLVM Windows installer (434 MB) is broken on
clean machines — its `liblldb` links a python DLL the installer does not ship (LLVM
issues #85764 / #58095 / #74073). Building with `LLDB_ENABLE_PYTHON=OFF` removes the
dependency entirely; the zip is self-contained by construction and the fresh-VM gate
(§3) proves it.

This runbook is self-sufficient: a fresh agent executes it top to bottom without the
Phase 4 plan.

## 1. Source + toolchain

- **Source:** LLVM release source, 22.x series (see §5 for why 22). Clone
  `https://github.com/llvm/llvm-project.git` at tag `llvmorg-<version>` (e.g.
  `llvmorg-22.1.0`, `--depth 1`), or unpack the matching release source tarball.
- **Host toolchain:** VS 2022 MSVC x64 + CMake + Ninja, driven from an
  **x64 Native Tools Command Prompt**. This is a stock LLVM-on-Windows build setup.
- **Configure** (these flags are the contract — `LLDB_ENABLE_PYTHON=OFF` is the whole
  point; `clang` is in the project list because liblldb embeds clang for expression
  evaluation):

```cmd
cmake -G Ninja -S llvm-project\llvm -B build ^
  -DLLVM_ENABLE_PROJECTS="clang;lldb" ^
  -DLLDB_ENABLE_PYTHON=OFF ^
  -DLLDB_ENABLE_LUA=OFF ^
  -DLLDB_ENABLE_LIBEDIT=OFF ^
  -DCMAKE_BUILD_TYPE=Release
```

- **Build only the adapter — never the world:** `ninja -C build lldb-dap`. Outputs land
  in `build\bin\`: `lldb-dap.exe` and `liblldb.dll`. If `build\bin\lldb-argdumper.exe`
  is not present afterwards, `ninja -C build lldb-argdumper` too — it is a *runtime*
  helper of liblldb (process launching), not a link-time dependency, so the lldb-dap
  target alone may skip it, and the zip requires it (§2). Expect hours of build time
  even for this narrow target; it is still far cheaper than a full LLVM build.
- **CRT reality check.** A Release MSVC build links the *dynamic* VC++ runtime by
  default (`vcruntime140.dll`, `msvcp140.dll`, `vcruntime140_1.dll`) — DLLs a clean VM
  may not have. Before shipping anything to the VM, run
  `dumpbin /dependents build\bin\lldb-dap.exe` (and the same on `liblldb.dll`): every
  import must be either (a) a file in the zip or (b) OS-shipped
  (`kernel32`/`ws2_32`/`bcrypt`/`ntdll`/`ucrtbase`/`api-ms-win-*`, …). Anything
  `python*` means the configure flags were wrong — stop and reconfigure. For the VC
  runtime imports, either copy the redist DLLs into the zip's `bin/` beside the exe, or
  reconfigure with `-DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded` (static CRT) and
  rebuild. The fresh-VM gate (§3), not this paragraph, is the authority on whether the
  choice worked.

## 2. Zip layout — the locator's and installer's contract

One root folder named exactly `LldbDapInstaller.InstalledDirName`, binaries under `bin/`:

```
lldb-dap_<version>/
  bin/
    lldb-dap.exe
    liblldb.dll
    lldb-argdumper.exe
  LICENSE.TXT          ← llvm-project/llvm/LICENSE.TXT (Apache-2.0 WITH LLVM-exception)
```

Why this exact shape:

- **The installer extracts the zip WHOLE** into a staging dir, verifies
  `<InstalledDirName>/bin/lldb-dap.exe` exists (`StepLayout` failure otherwise), then
  swaps `~/.vgs/tools/<InstalledDirName>` into place. The zip's root folder name and
  the `InstalledDirName` constant must match byte-for-byte.
- **`LldbDapLocator.FindInToolsRoot`** scans `lldb-dap*` directories under the tools
  root, ranks them by the *numeric* version parsed from the `lldb-dap_` suffix, and
  probes `bin/lldb-dap.exe` first. `lldb-dap_<version>` with a parseable version is
  what ranks correctly against past and future installs.
- **All three binaries are load-bearing:** `lldb-dap.exe` alone cannot start
  (`liblldb.dll` is its core), and `lldb-argdumper.exe` is liblldb's launch helper.
- Extra files (the license) are harmless — the checks are existence probes, not
  manifests. Shipping `LICENSE.TXT` keeps the asset visibly redistribution-clean.

Create it from the folder's parent (PowerShell):
`Compress-Archive -Path lldb-dap_22.1.0 -DestinationPath lldb-dap-windows-22.1.0.zip`.
(Compress-Archive writes backslash entry separators; fine here — the installer is
Windows-only and `ZipFile.ExtractToDirectory` accepts them.)

## 3. Fresh-VM acceptance — the gate

The claim being proven: **on a clean Windows VM with no LLVM, no python, and no VC
toolchain, the zip alone debugs a real MSVC executable.** No installer, no redist, no
PATH edits — unzip and go.

Setup:

1. Clean Windows 10/11 x64 VM from a fresh image (never had LLVM or python on it).
2. Copy in: the zip; a test program built **on the dev machine** with MSVC debug info —
   `cl /Zi /Od test.cpp` — copying **both** `test.exe` and `test.pdb` (the PDB rides
   beside the exe; without it nothing binds). Give it a handful of locals and enough
   statements to step through.
3. Copy in a **self-contained** DAP driver built on the dev machine (a trimmed console
   exe speaking `Content-Length`-framed JSON over stdio; the repo's e2e harness,
   `IdeInAngerTests.cs`, is the shape to crib). Do **not** install python or node on
   the VM to drive the test — a runtime installer that drops the VC redist system-wide
   would silently satisfy lldb-dap's CRT imports and void the gate.
4. Unzip. Nothing else.

⚠ **Never probe `lldb-dap.exe --version`** — it parks on stdin and hangs. This is the
locator's standing rule (file-existence checks only) and it applies on the VM too.

Drive one full session over raw DAP — the sequence and its ordering landmines are spec
§3.3.1 of the parent design:

1. `initialize` request → read the response.
2. Send `launch` (`program` = absolute path to `test.exe`) — do **not** await its
   response (lldb-dap emits `initialized` only while processing launch; waiting first
   deadlocks).
3. Await the `initialized` event.
4. `setBreakpoints` on a known line → the response reports the breakpoint `verified`
   at the right line (**bind**).
5. `configurationDone` → the deferred `launch` response now arrives.
6. Await `stopped` with reason `breakpoint` (**stop**).
7. `next` using the `stopped` event's `threadId` → next `stopped` lands on the
   following line (**step**).
8. `stackTrace` → `scopes` → `variables` → the locals appear with values (**locals**).
9. `disconnect` with `terminateDebuggee: true` → the debuggee process tree exits.

Pass criteria — all of:

- bind → stop → step → locals observed exactly as above;
- no missing-DLL dialog or loader error at any point — every DLL `lldb-dap.exe` loads
  comes from the zip or ships with Windows;
- python absent from the VM throughout.

Any failure → fix (missing-DLL failures per §1's CRT note), rebuild, re-zip, and rerun
the **whole** gate on a **re-cleaned** VM — a VM that has seen a redist install is no
longer clean.

## 4. Measure + publish — filling the pins

Only after §3 passes, and from the *exact bytes* that passed:

1. **Measure:**
   - `Get-FileHash -Algorithm SHA256 .\lldb-dap-windows-22.1.0.zip` — the installer
     compares ordinal-ignore-case, so the uppercase hex it prints is fine as-is.
   - `(Get-Item .\lldb-dap-windows-22.1.0.zip).Length` — the exact byte count.
2. **Publish** as a GitHub release asset on the VGS repo
   (`gracepriest/VisualGameStudioEngine`). The pinned `DownloadUrl` implies the naming
   contract: asset **`lldb-dap-windows-<version>.zip`** under tag
   **`lldb-dap-<version>`**. E.g.:
   `gh release create lldb-dap-22.1.0 .\lldb-dap-windows-22.1.0.zip --title "lldb-dap 22.1.0 (Windows x64)" --notes "<built from llvmorg-22.1.0, LLDB_ENABLE_PYTHON=OFF; Apache-2.0 WITH LLVM-exception; SHA-256 <hash>>"`.
   Expected size 40–80 MB (spec §5) — comfortably inside the installer's 15-minute
   `DownloadDeadline` and GitHub's asset limit.
3. **Fill the four pins** in
   `VisualGameStudio.ProjectSystem/Services/LldbDapInstaller.cs` — placeholders today:

   | Constant | Placeholder value today | Fill with |
   |---|---|---|
   | `DownloadUrl` | `"https://github.com/gracepriest/VisualGameStudioEngine/releases/download/lldb-dap-22.1.0/lldb-dap-windows-22.1.0.zip"` | already real if 22.1.0 ships; otherwise the same shape with the shipped version |
   | `ExpectedSha256` | `"REPLACE-AT-RELEASE-TIME"` | the measured 64-hex-char SHA-256 |
   | `ExpectedSizeBytes` | `0` | the measured exact byte count |
   | `InstalledDirName` | `"lldb-dap_22.1.0"` | the zip's root folder name (§2), matching the shipped version |

   Fill **all four in one commit**: `IsReleasePinned` is literally
   `!ExpectedSha256.StartsWith("REPLACE")`, so writing the SHA flips the gate — a
   partial fill would arm real downloads against a stale size or URL.

   What flips automatically once the SHA is real:
   - `LldbDapInstallerTests.ReleasePins_MatchTheRunbookOnceFilled`
     (`VisualGameStudio.Tests/Services/LldbDapInstallerTests.cs`) stops
     `Assert.Ignore`-ing and holds the pins to measured-release shape (64-hex SHA,
     positive size). It asserts `DownloadUrl` and `InstalledDirName` by equality
     *unconditionally* — a version bump edits those assertions in the same commit (§5).
   - `LldbDapDownloadFlow` stops showing its "not published yet" toast (Task 12's
     release-pin gate): until this commit lands, **Tools → Download C++ Debugger** and
     the first-F5 offer toast report that the download is not yet published and point
     at the `cpp.lldbDap.path` override; after it, they actually download.
4. **Verify:** run the full suite (the pins test now bites), then one live
   end-to-end on a machine where the locator finds nothing (or after renaming
   `~/.vgs/tools/lldb-dap_*`): Tools → Download C++ Debugger → live progress toast →
   "lldb-dap installed — press F5 to debug." → F5 a native project.

## 5. Version policy

- **Ship 22.x.** The mature native-PDB era (LLDB 21/22) is what makes MSVC/PDB
  debugging first-class. 22.1.0 is the placeholder assumption; any newer 22.x patch
  available at execution time is fine.
- **The dev machine's winlibs 19.1.7 is the development debugger only.** It predates
  the mature native-PDB era; any MSVC/PDB quirk observed on 19 is re-checked on the
  shipped 22.x before being filed as our bug (parent spec §10).
- **A version bump moves in lockstep:** the git tag, the asset file name, the zip's
  root folder name (§2), `DownloadUrl`, `InstalledDirName`, and the two unconditional
  equality assertions in `ReleasePins_MatchTheRunbookOnceFilled`. `ExpectedSha256` /
  `ExpectedSizeBytes` are per-asset by nature. The locator needs no change — its
  `lldb-dap*` scan with numeric ranking picks the newest install automatically.
