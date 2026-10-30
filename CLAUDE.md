# Instructions for agents

## Current focus: pivot to the monster trainer, not balance

The game is pivoting to the monster-raising design in `specs/12`; `specs/13` is the transition plan and lists kj's open questions. Until kj says otherwise:

- Do not build or extend idle-only systems (stage push, gold stat levels, gear, affixes, dungeons, uniques, promotion; #55-#59, #64). Keep them building and green until `specs/13` phase T7 removes them.
- Build new systems in the order of `specs/13` section 6. Do not start a phase whose blocking question in `specs/13` section 9 is unanswered.

Balance stays on hold while the systems change:

- Do not spend effort on balance tuning: no retuning curves, constants or acceptance targets, no tuning passes on the power-curve model, no balance-only PRs. That work will be thrown away when the systems change.
- Placeholder numbers in new systems are fine. Pick something reasonable and note it as a placeholder.
- **Do not run or read balance checks.** `./check.sh` skips the balance sweep (AC1-AC5) while `BALANCE_ON_HOLD=1` at its top. Do not run `./check.sh --balance`, the `acceptance`, `sweep` or `compare` bot verbs, or the `tools/` power-curve model unless kj asks. Do not report or analyze balance numbers (stage targets, shares, walls) in PRs or specs. Determinism (AC6: tests plus `golden --check`), the other tests and the bench still block.
- If a system change moves the golden files, regenerate them (`golden --update`) and say so in the merge commit. That is expected, not a balance problem.
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

## Workflow

Standing rule for every implementation task. Run it to the end without waiting for kj's input.

1. **Review, fix, repeat.** When the change is written, review your own diff (committed and uncommitted) for bugs, edge cases, missing tests and style issues. Fix what you find, re-run the checks (`./check.sh --fast` while iterating, the full `./check.sh` at the end) and review again. Stop looping only when a review pass finds nothing.
2. **Happy path: merge without asking.** When the full `./check.sh` passes, the review is clean and the merge has no conflicts: commit with a clear message, then `git fetch origin main`, check out `main`, `git pull --ff-only origin main`, `git merge --no-ff <your branch>` (so there is a merge commit) and `git push origin main`. If `main` moved since you branched, re-run `./check.sh` on the merged result before pushing. Never force-push, rewrite pushed history or delete branches.
3. **Stop and ask kj only if:** checks still fail after three fix attempts; a merge conflict you can't resolve confidently; the change touches secrets, credentials or anything irreversible; or the review raises a design question that is kj's to decide (gap rule 3 above).
4. **Report briefly:** what changed, what the review caught, and the merge commit hash.

This path has no PR, so anything the rules above say to put in "the PR description" (gap decisions, regenerated goldens) goes in the merge commit message and the final report instead.

## Learn from mistakes

This file is how lessons reach the next agent. When the user corrects you on something that is likely to come up again (a convention, a command, a gotcha, a wrong assumption), add a short rule for it under "Lessons" below in the same change. Keep each rule to one or two lines, say what to do rather than what went wrong, and remove a rule once it no longer applies.

## Lessons

- Generated test data is large and not for reading. Never open, grep through or hand-edit `golden/` files: regenerate them through the bot CLI, never edit by hand.
- The Workflow loop applies to every task, including ones that only edit CLAUDE.md or settings. "Show me X" means merge, then show X in the report: it is not a reason to stop before merging.
- kj tests from a phone through the artifact https://claude.ai/artifact/LpdHtJ53ujKkXPizqPkkkg. After merging a change to `web/`, `src/` or `Content/`, run `web/wasm/build.sh` and republish `web/index.html` to that URL with `files` from `web/wasm/artifact-files.py` and `root` set to the repo root, then give kj the link.
- Any change to `content_src/` (even one string) changes the content hash that `golden/` and `Fixtures/Gacha/vectors.json` embed. Run `golden --update` and `gacha-vectors --update` through the bot CLI and say so in the merge commit.
- Cloud containers may lack the .NET SDK. `apt-get install -y dotnet-sdk-8.0` works through the proxy (dot.net's install script is blocked). Install it and run the full `./check.sh` instead of settling for `--no-dotnet`.
