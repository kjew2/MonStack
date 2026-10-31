# 20 - Roadmap

Status: **proposal** (agent draft, 2026-10-30; kj has not reviewed it). A high-level order of fixes, features and tech work, by priority, with every open GitHub issue mapped to an item. `specs/13` stays the source for phase status; this file is the cross-cutting view. P0/P1/P2 mean what `specs/13` section 5 says. Effort sizes are rough (S = days, M = 1-2 weeks, L = several weeks). All of it is subject to kj's call.

## How to read it

- **Fixes**: known gaps and stale tracking in what exists. No open bug issues exist; these are leftovers from the pivot.
- **Features**: player-facing work.
- **Tech**: build, tooling and architecture.
- Items are in the order they should be done inside each priority.
- Issues that describe the removed idle game (stage walls, offline gold, heroes, gear, idle chest) are not deleted here. Each is mapped to the item that replaces it and marked **re-scope**, meaning the issue text must be rewritten before anyone builds from it.

## P0 - Now: close out the MVP, build T9 and T10, lay foundations

| # | Item | Kind | Size | Issues |
|---|---|---|---|---|
| R0.1 | **Close the MVP goal.** T0-T7 are done (`specs/13` section 6); the goal still lists T7 as in progress. Close it and point to #70. | Fix | S | #69 |
| R0.2 | **CI workflow running `./check.sh`** on PRs and `main`. Everything else depends on the gates being enforced. Done: `.github/workflows/check.yml` (`specs/09` section 6); making it a required check is kj's GitHub setting. | Tech | S | #38 |
| R0.3 | **Pick the game's name**, then do the one mechanical `IdleRng` rename (`specs/13` section 1). Cheaper the earlier it happens; store listing, site and community channels wait on it. | Fix / Tech | S + decision | #47 (name part) |
| R0.4 | **Answer the open design questions** in `specs/13` section 9 (what overwriting removes, one energy meter, roles and elements). Not blocking, but each is a rule the next specs build on. | Fix | decision | none |
| R0.5 | **Done.** **T9** (moved up from P1, kj 2026-10-31), **re-scoped by kj (2026-10-30): species unlock from achievements and talents come from a post-run vendor** (5 random offers, rarity by the run's grade). The species gacha and aptitude ranks are scrapped; Skill Books are the premium gate (selling them is R1.9). | Feature | M | #73 |
| R0.6 | **T10 Daily Tower** (moved up from P1, kj 2026-10-31): 4v1, boss phases, entropy gauge, environmental clock, Skill Book drops replacing the test button. Spec written: `specs/21` (kj's questions 1 and 2 there come first). | Feature | L | #74 |

## P1 - Next: the game toward 1.0

Order follows `specs/13` section 6. Each phase gets its own spec before building (T11 explicitly). T9 and T10 are P0 (R0.5, R0.6).

| # | Item | Kind | Size | Issues |
|---|---|---|---|---|
| R1.1 | **Done.** **T8 gauntlet wheel screen and Legacy Titles** in the web client (engine done, `specs/19`). Also expose the gauntlet fields in the bridge. | Feature | S-M | #72 |
| R1.3 | **First-time experience, rewritten for the trainer**: guided first run, real strings (species, trees, nodes), settings, accessibility basics, localisation decision. The existing issue is written for the idle game. | Feature | M | #48 (**re-scope**) |
| R1.5 | **Content depth for runs**: more species, native and generic trees, chase trees, books and talents beyond the placeholders; the long-term loop for a day-45 player. Numbers stay placeholders until balance resumes. | Feature | L | #51 (**re-scope**: its stage-walls, gear bag and prestige scope is the idle game; keep the endgame and live-ops-calendar questions) |
| R1.6 | **T11 Endless Tower** (4v4, mutators, gambit rulebook editor on a phone). | Feature | L | #75 |
| R1.7 | **Art and visual identity**: the largest launch gap and the longest lead time, so start the decisions early and run in parallel. Hero portraits become monster art; stage backgrounds become battle and tower backdrops. | Feature | L | #45 (**re-scope**) |
| R1.8 | **Audio and game feel**: music, SFX, haptics, volume settings. | Feature | M | #46 (**re-scope**: gacha and boss cues map to pulls, rank fights and gauntlet) |
| R1.9 | **Decide monetisation** (are gems or Skill Books sold, and how; rewarded ads; cosmetics). Blocks R2.3. | Decision | decision | #49 (decision part), #77 |

## P1 - Launch readiness (after the game systems above)

| # | Item | Kind | Size | Issues |
|---|---|---|---|---|
| R2.1 | **T12 async 4v4 PvP and the server**: accounts, server-resolved battles, cloud save, plausibility checks, golden replay fixtures, deploy pipeline. Vault entries already carry the replay log and hash. Server work is a vertical; re-scope each child issue for the trainer before starting (they describe gem ledgers, offline gold and the idle chest). | Feature / Tech | L | #76, #15 (goal), #17 backend spike, #18 server assembly, #19 auth, #21 server clock, #22 pull ledger, #24 server-credited gems, #25 cloud save, #27 analytics and plausibility, #28 integration tests and deploy, #32 replay fixtures, #33 save trust fields (**all re-scope**) |
| R2.2 | **T13 platform builds**: Steam wrapper (Tauri or Electron) and web release. | Tech | M | #77 |
| R2.3 | **Monetisation build-out**, only if R1.9 says yes: shop, receipts, ad SDK, consent. | Feature | M-L | #49, #26 receipt validation |
| R2.4 | **Compliance, legal and store accounts**: privacy policy, odds disclosure for any paid random items (the species gacha was scrapped at T9), account deletion, age rating, Steam and developer accounts. Lead times are weeks and partly human, so start accounts early. | Tech / human | M | #50 |
| R2.5 | **Store presence**: listing, screenshots, support site, community channel (after the name and art). | Feature | S-M | #47 (store part) |
| R2.6 | **QA, beta and soft launch** with analytics, on web and Steam first. | Feature | L | #53 (**re-scope**: it is written for TestFlight) |
| R2.7 | **1.0 goal tracker.** | Tracking | none | #70 |

## P2 - Later or parked

| # | Item | Kind | Size | Issues |
|---|---|---|---|---|
| R3.1 | **Live operations**: crash reporting, KPI dashboards, admin tool, support process, hotfix playbook. Becomes P1 once real players exist (before R2.6 ends). | Tech | L | #52 |
| R3.2 | **Remote config overlay** for live tuning without a build. | Tech | S-M | #29 |
| R3.3 | **iOS / Unity client and TestFlight**: the Unity project was deleted at T7 (git history has it). Reopen only if a native mobile build becomes a priority. Recommend closing these as obsolete rather than leaving them open. | Feature | L | #6, #10, #11, #23 |

## Items with no issue yet

Candidates for new issues if kj agrees; none is filed.

- **Balance harness** on the run bots (`specs/09` section 7), when balance resumes (CLAUDE.md).
- **Battle and run throughput benchmark**, when speed matters (`specs/13` section 2).
- **Phone-quality pass on the web client** (touch targets, wasm load time, save-size limits in one browser key).

## Issue map (every open issue)

| Issue | Item | Issue | Item |
|---|---|---|---|
| #6 | R3.3 | #32 | R2.1 |
| #10 | R3.3 | #33 | R2.1 |
| #11 | R3.3 | #38 | R0.2 |
| #15 | R2.1 | #45 | R1.7 |
| #17 | R2.1 | #46 | R1.8 |
| #18 | R2.1 | #47 | R0.3, R2.5 |
| #19 | R2.1 | #48 | R1.3 |
| #21 | R2.1 | #49 | R1.9, R2.3 |
| #22 | R2.1 | #50 | R2.4 |
| #23 | R3.3 | #51 | R1.5 |
| #24 | R2.1 | #52 | R3.1 |
| #25 | R2.1 | #53 | R2.6 |
| #26 | R2.3 | #69 | R0.1 |
| #27 | R2.1 | #70 | R2.7 |
| #28 | R2.1 | #72 | R1.1 |
| #29 | R3.2 | #73 | R0.5 |
| #74 | R0.6 | #75 | R1.6 |
| #76 | R2.1 | #77 | R2.2, R1.9 |

## Suggested triage changes

For kj to approve; none is applied.

- Close #69 (R0.1). Close #6, #10, #11, #23 as obsolete or keep as P2 (R3.3).
- Raise #38 to P0 and #72 to the top of P1.
- Add a `re-scope` label to the issues marked above and rewrite their text before use.
- Add `priority: P1` to #26 and keep it blocked on R1.9.
