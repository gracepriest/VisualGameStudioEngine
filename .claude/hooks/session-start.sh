#!/bin/bash
# SessionStart hook — Claude Code on the web.
#
# Why this exists: every plan in docs/superpowers/plans/ is written to be driven by the
# superpowers workflows (superpowers:executing-plans, superpowers:subagent-driven-development,
# superpowers:test-driven-development). A local machine installs those once and keeps them.
# A cloud session gets a fresh container every time, so without this hook the plugin is simply
# absent — and `/plugin` and `/reload-plugins` are BOTH refused over a remote connection
# ("isn't available over a remote connection in this session"), so there is no in-session way
# to recover. The `claude plugin` SHELL commands are not gated the same way, and they are what
# this hook uses.
#
# Both commands are idempotent: re-adding the marketplace reports "already on disk" and
# re-installing reports "already installed", each exiting 0. Verified, not assumed.

set -euo pipefail

# A developer's own machine manages its own plugins; don't touch their install.
if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

# ⛔ Never fail the session over a plugin. A session that won't start is worse than a session
# without superpowers — but a SILENT failure is worse than both, because the plans reference
# skills that would then be missing with no explanation. So: report loudly, exit 0 regardless.
if ! claude plugin marketplace add obra/superpowers-marketplace; then
  echo "session-start: could not add the superpowers marketplace (network?)." >&2
  echo "session-start: docs/superpowers plans reference superpowers: skills that will be MISSING." >&2
  exit 0
fi

if ! claude plugin install superpowers@superpowers-marketplace; then
  echo "session-start: marketplace added but the superpowers plugin did not install." >&2
  echo "session-start: retry by hand with 'claude plugin install superpowers@superpowers-marketplace'." >&2
  exit 0
fi

echo "session-start: superpowers ready."
