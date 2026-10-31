# Instructions for agents

## Current focus: build the monster trainer, and balance it

The game is the monster-raising design in `specs/12`; `specs/13` is the plan, the status of each phase and kj's open questions. The idle game this repo started as was removed at phase T7 (2026-10-30): do not bring it back or build idle systems (stage push, gold stat levels, gear, affixes, dungeons, uniques). Until kj says otherwise:

- Build new systems in the order of `specs/13` section 6. Do not start a phase whose blocking question in `specs/13` section 9 is unanswered.
- Balance tuning is allowed again (kj, 2026-10-31, basic prototype working). Retuning constants and curves and balance-only changes are fine. Numbers in `content_src/` are still first-pass; when adding a system pick something reasonable.
- Measure balance with the bot CLI's `balance` verb (`specs/09` section 7): grades, injuries, fights, shards, gauntlet and Daily Tower over many seeds per species x policy. Its loose sanity checks (PASS/WARN, report only) guard against "absolutely unplayable" only; do not add tight targets until kj asks (meta-progression will move the tuning, and some species are meant to be stronger). A large retune still needs targets agreed with kj first; small obvious fixes (e.g. an AI threshold) need none.
- If a change moves the golden files (any `content_src/` change does, through the content hash), regenerate them with `golden --update` and say so in the merge commit. That is expected, not a balance problem.

## Keep the specs current

The specs in `specs/` are the source of truth for this project. Start with `specs/00-overview.md`, which holds the canonical constants and cross-spec contracts.

When something is discussed and agreed with the user that diverges from what a spec says, update the spec in the same change:

- Edit the affected spec(s) so they describe the agreed design, not the original plan. Fix every spec the change touches, including constants and contracts in `specs/00-overview.md`.
- Update anything that mirrors the spec in the same change: test vectors, `golden/` files, `tools/` models, and the `web/` prototype where relevant.
- If the README's spec table no longer matches, update it too.
- Do not leave code and specs disagreeing. An existing mismatch with no agreed decision behind it is a gap: handle it with the rules below.

## When you find a gap

A gap is anything the specs, code or task leave undecided: a missing rule, an unhandled edge case, a contradiction, tech debt. Do not guess silently. Pick one:

1. **Obvious, low-stakes and easy to undo** (naming, a conventional default, a missing guard): decide, proceed, and state the decision in the PR description so the user can veto it in review.
2. **Does not block the current task:** open a GitHub issue and keep going. Search open issues first and comment on a match instead of duplicating it. Label it `agent-found`. The issue must stand alone: the gap, where you found it, options with a recommendation, and what it blocks. If you work around it, leave `TODO(#N)` at that spot.
3. **Blocks the task and the call is the user's** (game design, balance targets, monetization, anything hard to reverse, specs contradicting each other): stop and ask. Do not file an issue and guess anyway.

When unsure between 1 and 3, treat it as 3. Create issues with `gh api repos/kjew2/idle-rng/issues` (REST; `gh issue create` fails here because GraphQL is blocked).

## Run every check before calling a change done

`./check.sh` runs everything CI runs: content build drift, .NET build and tests, the golden-file check, the WebAssembly build and the headless-browser run of the trainer.

- `./check.sh --fast` for the inner loop (skips the golden tests and the golden-file check). Run the full `./check.sh` before you finish.
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
- kj tests from a phone through the artifact https://claude.ai/artifact/LpdHtJ53ujKkXPizqPkkkg. After merging a change to `web/`, `src/` or `Content/`, run `web/wasm/publish-artifact.sh` on the merged main, pass the JSON it prints to the Artifact publish call, then give kj the link (the page header shows the published commit).
- Any change to `content_src/` (even one string) changes the content hash that `golden/` embeds. Run `golden --update` through the bot CLI and say so in the merge commit.
- Removing or renaming a content id (any `id` in `content_src/`, not a string key) needs a MAJOR bump of `content_version` in `content_src/meta.csv`, or `check.sh` fails with `E090`. Prefer adding ids.
- Cloud sessions install the .NET 8 SDK and the wasm-tools workload through `.claude/hooks/session-start.sh` (SessionStart hook, remote only). If `dotnet` is still missing (hook not on the base branch yet), run that script with `CLAUDE_CODE_REMOTE=true`, then the full `./check.sh` instead of settling for `--no-dotnet`.
- The UGS spike (`server/ugs/`) needs the .NET 10 SDK, and installing it makes .NET 10 the default, so `check.sh` silently skips the WebAssembly checks (wasm-tools is installed for .NET 8). Remove `dotnet-sdk-10.0` before the full `./check.sh`.
