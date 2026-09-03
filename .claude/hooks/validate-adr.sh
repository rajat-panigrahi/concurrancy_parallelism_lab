#!/usr/bin/env bash
# PostToolUse hook: warn when an ADR is missing one of its mandatory sections.
#
# This repo's own docs-review rule says every ADR needs "Why not the others",
# "Trade-offs accepted" and "Interview angle" — an ADR with no stated downside is a
# rationalisation, not a decision. This automates the heading check; only a human (or the
# concurrency-reviewer agent) can judge whether the content is real.
#
# Reads the PostToolUse JSON payload on stdin and exits 2 to surface a warning to Claude.
# On PostToolUse, exit 2 does NOT block — the edit has already happened.
set -uo pipefail

payload=$(cat)

# Prefer jq; fall back to python3 so the hook works on a bare box.
if command -v jq >/dev/null 2>&1; then
  file_path=$(printf '%s' "$payload" | jq -r '.tool_input.file_path // empty')
elif command -v python3 >/dev/null 2>&1; then
  file_path=$(printf '%s' "$payload" | python3 -c \
    'import json,sys; print(json.load(sys.stdin).get("tool_input",{}).get("file_path",""))' 2>/dev/null)
else
  exit 0   # no parser available: stay silent rather than nagging on every edit
fi

# Only ADRs. Every other edit passes straight through.
case "$file_path" in
  */docs/architecture/adr/*.md) ;;
  *) exit 0 ;;
esac

# The file may have been deleted or renamed since the tool ran.
[ -f "$file_path" ] || exit 0

# The index and template live in the same tree but are not ADRs.
case "$(basename "$file_path")" in
  README.md|_template.md) exit 0 ;;
esac

missing=()
grep -q '^## Why not the others' "$file_path"   || missing+=("## Why not the others")
grep -q '^## Trade-offs accepted' "$file_path"  || missing+=("## Trade-offs accepted")
grep -q '^## Interview angle' "$file_path"      || missing+=("## Interview angle")

if [ ${#missing[@]} -gt 0 ]; then
  {
    echo "ADR $(basename "$file_path") is missing required section(s):"
    printf '  %s\n' "${missing[@]}"
    echo
    echo "Every ADR in this repo states why the rejected options lost, what the decision"
    echo "makes worse, and how to defend it in an interview. See .claude/rules/docs-and-adrs.md."
  } >&2
  exit 2
fi

exit 0
