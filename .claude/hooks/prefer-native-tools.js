#!/usr/bin/env node
// PreToolUse hook for the Bash tool (Windows repo: VisualGameStudioEngine).
//
// Why: reflexive Unix file/search commands (grep, cat, find, ...) should go
// through Claude Code's dedicated tools instead — they're encoding-safe (this
// repo's BOM-less UTF-8 files get mangled by shell round-trips), faster, and
// integrate with the permission UI. When a Bash command *starts with* one of
// these commands it's almost always a reflex, so deny it and name the right
// tool. Genuine shell work (git, dotnet, msbuild, real pipelines) starts with
// something else and passes through untouched.
//
// Wired up in .claude/settings.local.json under hooks.PreToolUse (matcher "Bash").

let raw = "";
process.stdin.on("data", (c) => (raw += c));
process.stdin.on("end", () => {
  let cmd = "";
  try {
    const input = JSON.parse(raw || "{}");
    cmd = (input.tool_input && input.tool_input.command) || "";
  } catch {
    process.exit(0); // unparseable stdin -> never interfere
  }

  // First "real" token: skip leading whitespace and any `VAR=val` env prefixes.
  const tokens = cmd.trim().split(/\s+/);
  let i = 0;
  while (i < tokens.length && /^[A-Za-z_][A-Za-z0-9_]*=/.test(tokens[i])) i++;
  const head = (tokens[i] || "").toLowerCase();

  // Footgun command -> the dedicated tool that replaces it.
  const REDIRECT = {
    grep: "the Grep tool",
    egrep: "the Grep tool",
    fgrep: "the Grep tool",
    rg: "the Grep tool",
    find: "the Glob tool",
    cat: "the Read tool",
    head: "the Read tool (with offset/limit)",
    tail: "the Read tool (with offset/limit)",
    sed: "the Edit tool (or Read to view)",
    awk: "the Read / Grep tools",
  };

  // Escape hatches for stream-only uses that no dedicated tool covers.
  const isFollow = head === "tail" && /\s-[A-Za-z]*[fF]\b/.test(cmd); // tail -f/-F

  const tool = REDIRECT[head];
  if (!tool || isFollow) process.exit(0); // allow

  const out = {
    hookSpecificOutput: {
      hookEventName: "PreToolUse",
      permissionDecision: "deny",
      permissionDecisionReason:
        `On this Windows repo, use ${tool} instead of running \`${head}\` through the Bash tool — ` +
        `it's encoding-safe, faster, and shows up in the permission UI. ` +
        `Reserve Bash for genuine shell work (git, dotnet, msbuild, PowerShell, real pipelines). ` +
        `If this ${head} genuinely needs shell semantics no dedicated tool provides, ` +
        `say so to the user rather than retrying it verbatim.`,
    },
  };
  process.stdout.write(JSON.stringify(out));
  process.exit(0);
});
