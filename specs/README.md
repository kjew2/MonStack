# Specs

The specs are the source of truth for MonStack (the repo and code still say Idle RNG, a leftover from the idle game this repo started as, removed on 2026-10-30). Start with `00-overview.md`: it lists what exists and the constants and contracts every spec follows. The design is `12`; the plan and what is built is `13`.

Keep this table current: add a row when a spec is added, and fix the row when a spec's scope changes.

| Spec | Covers |
|---|---|
| `00-overview.md` | What exists, constant groups, vocabulary, cross-spec contracts, layout |
| `01-gacha-math.md` | Retired: rates, pity and the 10-pull guarantee of the scrapped species gacha (kept as history) |
| `03-sim-core-combat.md` | Sim design rules, numeric model, RNG and the streams in use |
| `05-content-data-schema.md` | Content pipeline, conventions, error codes, `ContentDb` |
| `06-save-and-offline.md` | Save file format, stores, loading, schema versions |
| `08-backend-seams.md` | Backend design for T12 and T13: server time, accounts (guest and Google sign-in, built for the web client), purchases, PvP calls; the UGS spike results. The account on the server is `27` |
| `09-test-harness-and-bots.md` | Run bots, the run-balance report, golden files, `check.sh` |
| `10-online-competitive.md` | Trust model for accounts, leaderboards and async PvP (design only) |
| `12-monster-trainer-design.md` | The game design, merged from kj's drafts, with the decisions recorded |
| `13-pivot-transition.md` | The pivot plan: phases T1-T13, what carries over, status, open questions |
| `14-training-run.md` | Training run: AP cycles of Drill and Rest, drill rolls, energy and injury (skips cycles), rank fights against a ladder rolled from per-rank pools, milestones |
| `15-skill-trees.md` | Skill trees: 4 slots, skill points, nodes with ~75% filler, milestone/book/chase offers, overwriting, chase unlock and re-lock |
| `16-battle.md` | Monster battle engine: sides of 1-4, eight-stat sheet, ATB, default AI, triggers, rule modifiers |
| `18-meta-progression.md` | Account: species unlocks by achievement, Vault (cap, lock, dismantle), Training Shards, the post-run talent vendor, run modifiers, abandon, account save |
| `19-gauntlet.md` | End-of-run gauntlet: 100 escalating floors, the floor and killer the wheel lands on, Legacy Titles |
| `20-roadmap.md` | Proposed roadmap: fixes, features and tech by priority, every open issue mapped |
| `21-dungeon.md` | Dungeon (T10): 4v1 boss floors with a Vault team, a daily lineup, tickets (one free a day, more from runs), entropy gauge, boss phases, environmental clock, shard and Skill Book rewards |
| `22-spike-higher-tier-trees.md` | Spike report (#110): skill trees and capstones for higher-tier creatures, measurements of capstone reach, a mechanics catalogue (games, anime, manhwa, donghua, web and light novels; *Lord of the Mysteries* in section 8) and questions for kj |
| `23-spike-point-economy.md` | Spike report (#112): point economies with variance and dynamism, a baseline of how little runs vary today, a mechanics catalogue (management, strategy, RPG, roguelite) and a first package for kj |
| `24-spike-creature-inspiration.md` | Spike report: a creature concept for every skill tree (native, generic, book, chase), inspirations from myth, games, anime, film and nature, apex forms for capstones, nature/dark species ideas and questions for kj |
| `25-spike-stat-systems.md` | Spike report: stat systems for DEX, LUK and all-in single-stat builds, a `mono-<stat>` baseline, a field survey (evasion vs magic, luck, whole stat systems), kj's answers, the content 4.6 DEX/LUK retune (graze, fortune, DEX crit damage) and the content 4.7 signature skills for all-in DEF, WIS, LUK and INT |
| `26-spike-manhwa-sprites.md` | Spike report: manhwa-style creature art, feasibility (AI, artist, vector), the most popular manhwa style families, a comparison panel of five species in four styles (drawings in `assets/26/`); a second batch of five anime and manga styles (section 7); kj chose hunter / system dark, AI-generated (`tools/art/`), for portraits and battle sprites, now in the web client (section 8) |
| `27-server-authority.md` | Server authority (design kj 2026-11-01; the sim and the server core `IdleRng.Server` built, the client and module not yet): the account partitioned by domain into Cloud Save items, results kept instead of replay logs, the client's commands re-checked at checkpoints (option E), one device at a time, a connection needed, the options against abuse with calls, latency and cost (replay measured and accepted), the local test server, build order |
| `28-endless-tower.md` | Endless Tower (T11; gambit rulebooks, mutators, the climb and the account built, the screens next): 4v4 floors without end, squad shapes that need both AoE and single-target, mutators, weekly seasons, the IF/THEN rulebook (conditions, actions, target picks), the phone editor, questions for kj |

Numbers missing from the sequence (`02`, `04`, `07`, `11`, `17`) were deleted or never written; git history holds the deleted ones. `assets/` holds images referenced by the specs.
