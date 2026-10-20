# Task 31 pre-flight: first kinds

Plan: `docs/superpowers/plans/2026-09-29-portable-control-library.md`, "### Task 31".
Spec: `docs/superpowers/specs/2026-09-29-portable-control-library-design.md` §5.4-§5.6.

Follow this document instead of the plan's Task 31 section where they disagree.

## A. Anchors
- `BasicLang/lib/js/forms/Button.bas` etc are shells inheriting `Control`.
- `Control.bas` already implements common properties/events and DOM attachment.
- `PortableMembers.cs` currently lacks Task 31 owned methods.
- Catalog rows define the first kinds' properties/events.

## B. Decisions
1. Implement first-kind behavior in the library `.bas` files, inheriting `Control`.
2. Each kind overrides kind-specific properties/events and adds owned methods.
3. Add Task 31 owned methods to `PortableMembers.Manifest`.
4. Keep common Control behavior unchanged.

## C. Gate
- BasicLang build 0 errors.
- `PortableMembersManifestTests` pass.
- Full fast subset baseline (six known failures only).
No push.