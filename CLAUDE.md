# Instructions for agents

## Current focus: finalize systems, not balance

The gameplay systems are being redesigned (4v1 party battle, companion builds, affix gear, uniques, daily dungeons; see issue #60). Until kj says otherwise:

- Do not spend effort on balance tuning: no retuning curves, constants or acceptance targets, no tuning passes on the power-curve model, no balance-only PRs. That work will be thrown away when the systems change.
- Placeholder numbers in new systems are fine. Pick something reasonable and note it as a placeholder.
- **Do not run or read balance checks.** `./check.sh` skips the balance sweep (AC1-AC5) while `BALANCE_ON_HOLD=1` at its top. Do not run `./check.sh --balance`, the `acceptance`, `sweep` or `compare` bot verbs, or the `tools/` power-curve model unless kj asks. Do not report or analyze balance numbers (stage targets, shares, walls) in PRs or specs. Determinism (AC6: tests plus `golden --check`), the other tests and the bench still block.
- If a system change moves the golden files, regenerate them (`golden --update`) and say so in the PR. That is expected, not a balance problem.
- When kj says balance work resumes: set `BALANCE_ON_HOLD=0` in `check.sh`, empty `advisory` in `src/IdleRng.Bots/Acceptance/acceptance-targets.json`, then fix or re-agree each failing criterion (#41 is the known AC4 one).

## Keep the specs current

The specs in `specs/` are the source of truth for this project. Start with `specs/00-overview.md`, which holds the canonical constants and cross-spec contracts.

When something is discussed and agreed with the user that diverges from what a spec says, update the spec in the same change:

- Edit the affected spec(s) so they describe the agreed design, not the original plan. Fix every spec the change touches, including constants and contracts in `specs/00-overview.md`.
- Update anything that mirrors the spec in the same change: test vectors, `golden/` files, `src/IdleRng.Bots/Acceptance/acceptance-targets.json`, `tools/` models, and the `web/` prototype where relevant.
- If the README's spec table no longer matches, update it too.
- Do not leave code and specs disagreeing. An existing mismatch with no agreed decision behind it is a gap: handle it with the rules below.

## When you find a gap

A gap is anything the specs, code or task leave undecided: a missing rule, an unhandled edge case, a contradiction, tech debt. Do not guess silently. Pick one:

1. **Obvious, low-stakes and easy to undo** (naming, a conventional default, a missing guard): decide, proceed, and state the decision in the PR description so the user can veto it in review.
2. **Does not block the current task:** open a GitHub issue and keep going. Search open issues first and comment on a match instead of duplicating it. Label it `agent-found`. The issue must stand alone: the gap, where you found it, options with a recommendation, and what it blocks. If you work around it, leave `TODO(#N)` at that spot.
3. **Blocks the task and the call is the user's** (game design, balance targets, monetization, anything hard to reverse, specs contradicting each other): stop and ask. Do not file an issue and guess anyway.

When unsure between 1 and 3, treat it as 3. Create issues with `gh api repos/kjew2/idle-rng/issues` (REST; `gh issue create` fails here because GraphQL is blocked).

## Run every check before calling a change done

`./check.sh` runs everything CI runs: content build drift, .NET build and tests, the tick benchmark, the golden-file check and the WebAssembly build (plus the 20-seed balance sweep once balance work resumes).

- `./check.sh --fast` for the inner loop (skips golden tests, the bench and the acceptance sweep). Run the full `./check.sh` before you finish.
- `./check.sh --no-dotnet` on a machine without the .NET SDK. It is a partial check: say so when you report results.
- A change is not done while any check fails. Do not weaken a test, threshold or golden file to make it pass unless the user agreed to that change (and then the spec rule above applies).
- If you add a new kind of check, wire it into `check.sh`.

## Learn from mistakes

This file is how lessons reach the next agent. When the user corrects you on something that is likely to come up again (a convention, a command, a gotcha, a wrong assumption), add a short rule for it under "Lessons" below in the same change. Keep each rule to one or two lines, say what to do rather than what went wrong, and remove a rule once it no longer applies.

## Lessons

- Generated test data is large and not for reading. Never open, grep through or hand-edit `golden/` files: regenerate them through the bot CLI, never edit by hand.
