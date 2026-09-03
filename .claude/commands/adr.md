---
description: Create the next-numbered ADR from the repo template and index it
argument-hint: "<kebab-case-title>"
---

Create a new Architecture Decision Record for: **$ARGUMENTS**

1. Find the highest existing number in `docs/architecture/adr/` and use the next one,
   zero-padded to four digits.
2. Create `docs/architecture/adr/NNNN-$ARGUMENTS.md` following the exact section order
   used by every existing ADR — read one first, for example
   `docs/architecture/adr/0007-optimistic-by-default.md`.
3. Fill in what you can from the codebase and the conversation. Leave a clear `TODO:`
   marker anywhere you would be guessing rather than a plausible-sounding invention.
4. These three sections are mandatory and must be substantive — a `PostToolUse` hook
   warns if any heading is missing, but only you can tell whether the content is real:
   - **Why not the others** — the specific disqualifier for each rejected option.
   - **Trade-offs accepted** — what this decision makes *worse*. If you cannot name a
     downside, you have not made a decision, you have written a justification.
   - **Interview angle** — the 60-second defence, then the follow-up a good interviewer
     asks next.
5. Add the ADR to the index table in `docs/architecture/README.md`.
6. Add a row to `docs/architecture/trade-off-matrix.md`, including its
   **"breaks down when…"** entry.

Report which files you changed and anything you marked TODO.
