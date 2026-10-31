# 05 - Content Data

Scope: how every number and name of the game gets from a spreadsheet-shaped CSV into an immutable `ContentDb` that the sim, the web client and the bots all read. This implements `specs/00` contract 3 ("content is data"). The table definitions live with the system that reads them; this spec is the pipeline, the conventions and the error codes. If a value here disagrees with a system spec, the system spec wins.

## 1. Pipeline

```
content_src/<table>.csv                 committed; the source of truth (edit these)
   |  python3 tools/content_build.py    validates, converts, hashes
   v
Content/manifest.json + <table>.json    committed; byte-identical for identical input
   |  copied into web/wasm/dist (web/wasm/pack-content.js)
   v
ContentDb.Load(IContentSource)          at startup, once; verifies every file's sha256
   +--> IdleRng.Sim (battle, training, account, gauntlet)
   +--> IdleRng.Bots, IdleRng.Web
```

The CSVs and the JSON are both committed so CI can rebuild `Content/` offline and fail when the committed JSON is stale (`content_build.py --check`, part of `./check.sh`). `./check.sh` also compares the build with the `Content/` at the merge base with `origin/main` (`--baseline-ref`, section 4) so an id cannot be removed by accident. Any change to a `content_src/` file, even one string, changes `content_hash`, so the golden files embed a new hash and are regenerated (`specs/09` section 3).

## 2. Tables

| Table | Rows describe | Spec |
|---|---|---|
| `constants` | every scalar: `key`, `type` (`int`, `double`, `bool`), `value` | the system specs list their constants: `14`, `15`, `16`, `18`, `19` |
| `elements` | the five elements and the wheel (`strong_vs`) | `16` section 5.3 |
| `species` | element, base stats, growth multipliers, two native trees, two chase trees (in-run, book-gated) | `14` section 3, `15` |
| `drill_tiers` | fail, success, super gain ranges | `14` section 4 |
| `fight_ranks` | the rank fight opponents, E to S | `14` section 5 |
| `stat_milestones` | the thresholds that open tree drafts | `14` section 6 |
| `monster_skills` | skills tree nodes can grant | `15` section 2 |
| `trees`, `tree_nodes` | skill trees and their nodes | `15` section 2 |
| `chase_conditions` | what unlocks each chase tree | `15` section 5 |
| `skill_books` | books and the book tree each opens | `15` section 4 |
| `talents` | the account talent list | `18` section 4 |
| `vendor_rarities` | the post-run vendor's rarities: ranks, price multiplier, weight by grade | `18` section 4 |
| `achievements` | account achievements and the species they unlock | `18` section 8 |
| `gauntlet_enemies`, `legacy_titles` | gauntlet enemies and title bands | `19` |
| `tower_bosses`, `boss_phases`, `book_drops` | Dungeon bosses (regular, marquee, add), their phases and Skill Book drops | `21` section 5 |
| `strings` (optional) | `key`, `en`: every player-visible name and description | section 5 |

`tools/content_build.py` (`SCHEMA`, `REQUIRED_CONSTANTS`, the `validate_*` functions) and `ContentDb` (`Build*`) hold the exact columns and rules; both must change together. `REQUIRED_CONSTANTS` is the complete constant list: an unknown or missing constant is an error.

## 3. Authoring conventions

- Tab name = table name = CSV name = JSON name. Row 1 is the header; column names are `snake_case`, any order.
- A column whose header starts with `#` is a designer note and is ignored (`#notes`). Blank rows are skipped. Cells are trimmed. The decimal separator is `.`.
- Row order is preserved in the JSON and matters only where a rule says so (`sort_order`, a rotation through enemies, node `index`).

| Type | CSV form | Notes |
|---|---|---|
| `int` | `50` | integer literal only |
| `double` | `1.07`, `10` | IEEE double, emitted shortest-round-trip |
| `bool` | `true` / `false` | lowercase |
| `string` | any text | not for player-visible text (use `key`) |
| `id` | `slime` | `^[a-z][a-z0-9_]{0,63}$`; primary key; never reused or renamed once shipped |
| `enum` | `native` | one of the values in `ENUMS` in the builder |
| `key` | `species.slime.name` | localisation key `^[a-z0-9_]+(\.[a-z0-9_]+)+$`, resolved through `strings` (`ContentDb.Text`) |
| `color` | `#E8622C` | `#RRGGBB` uppercase |

Nullable columns accept an empty cell and emit `null`; any other empty cell is `E011`. Enums: `ConstantType`, `SkillKind`, `SkillTarget`, `TreeKind`, `NodeKind`, `FightTrigger`, `ConditionType`, `TalentEffect`, `AchievementKind`. Adding an enum value is a converter change, a `ContentDb` change and a MINOR version bump.

## 4. JSON and the manifest

One file per table, shape `{ "table": "...", "rows": [ ... ] }`: UTF-8 without BOM, 2-space indent, `\n`, trailing newline, keys in schema column order, rows in CSV order, doubles via Python `repr`, `null` for empty nullable cells. `strings.json` rows are `{ "key", "text" }`.

`manifest.json` lists `schema_version` (the layout version; the loader refuses unknown values, `E901`), `content_version`, `source_revision`, every table's file and sha256, and `content_hash` (sha256 over the table hashes in name order). There is no timestamp, so rebuilding the same CSVs gives the same bytes. The loader verifies every file against the manifest (`E900`).

`content_version` is `MAJOR.MINOR` (`content_src/meta.csv`). The current version is **3.15**: MAJOR 3 at the removal of the idle game's tables; 3.1 added the tooltip strings and made skill descriptions templates (`specs/16` section 9); 3.2 added `STAT_FLOOR_FRAC` and dropped the unused `monster_skills.desc_key` column; 3.3 gave each species two chase trees and a skill book of its own (`specs/15` section 5.1); 3.4 added `VAULT_CAP` and `fight_ranks.dismantle_shards` (`specs/18` section 7); 3.5 added the `achievements` and `vendor_rarities` tables and `VENDOR_OFFERS` (`specs/18` sections 4 and 8); 3.6 added the Dungeon tables `tower_bosses`, `boss_phases` and `book_drops` and the `TOWER_*`, `ENTROPY_*` and `CORRUPT_*` constants (`specs/21` section 5); 3.7 gave each chase tree a capstone of its own, four new rule modifiers and their `MON_TIDE_*`, `MON_DRAIN_FRAC`, `MON_FIRE_PIERCE` and `MON_DOUBLE_TURNS` constants (`specs/16` section 6.2); 3.8 added `EFFECT_STACK_CAP` (`specs/16` section 3); 3.9 made every gauntlet floor boss level (`GAUNTLET_BASE` 72, `GAUNTLET_BOSS_MULT` 1.0, normal enemy weights raised to the boss total, `specs/19` section 2); 3.10 added the Dungeon playback tooltip strings (`tower.entropy.*`, `tower.corruption.*`, `vent.*`; `specs/21` section 8); 3.11 made the heal AI wait for a full-value heal (`AI_HEAL_BELOW` 0.65 to 0.25, now only the fallback for heals too big to land in full; `tip.ai.heal`; `specs/16` section 5.2, issue #94); 3.12 added `INJURY_SKIP_CYCLES` and `INJURY_RECOVER_ENERGY` (an injury skips cycles instead of ending the run, `specs/14` section 4, issue #89); 3.13 made a vendor rarity the talent's own tier (`talents.rarity`) and dropped `vendor_rarities.ranks` and `price_mult` (`specs/18` section 4, issue #102); 3.14 made Skill Books uber rare (`TOWER_BOOK_CHANCE` 0.15 to 0.0005, `TOWER_BOOK_CHANCE_PER_FLOOR` 0.01 to 0.0003; `specs/21` section 5, kj 2026-10-31, issue #112); 3.15 added Dungeon tickets (`TOWER_TICKET_CAP`, `TOWER_TICKET_PIECES`, `TOWER_ATTEMPTS_PER_DAY` 3 to 1 as free tickets a day, the `fight_ranks.ticket_pieces` column; `specs/21` section 7, kj 2026-10-31). Bump MINOR for any value or addition, or for dropping a column no code reads; bump MAJOR when ids or tables are removed or renamed. `ContentDb.IsCompatibleWith(v)` is true when the MAJOR matches.

**Previous-build rules.** Saves and Vault entries refer to ids (species, trees, nodes, skills, books, talents, enemies), so removing or renaming one breaks them. `content_build.py --baseline DIR` (an earlier `Content/`) or `--baseline-ref REF` (a git ref) compares the fresh build with it: `E090` when an id, or a whole table, is gone and MAJOR was not raised; `E091` when `content_version` is malformed or went backwards. `strings` keys are exempt (a missing key shows as itself, section 5). A MAJOR bump is the author saying "old saves may not fit": it comes with the decision in `specs/06` section 5 on what happens to them. Adding ids needs no bump. The tests are `tools/test_content_build.py`.

## 5. Strings

Every player-visible name and description is a `key` into `strings` (en only for now). `ContentDb.Text(key)` returns the text, or the key itself when missing so a gap is visible rather than blank. A name key follows `<table singular>.<id>.<field>` (`species.slime.name`, `tree.pyromancy.name`, `mskill.flame_breath.desc`). `ContentDbTests.EveryNameKey_HasText` checks that every name key in content has text.

**Templates.** A string may hold placeholders, filled by `TextTemplate.Fill` so a number in a text comes from the data it describes: `{name}`, or `{name:pct}` for a percentage (`0.05` reads `5%`). Numbers print with up to two decimals. An UPPER_CASE name is a constant (`{MON_DEF_K}`, from `ContentDb.ConstantValues`); a lower-case name is a value of the thing described, and which names exist depends on the string family (`specs/16` section 9 lists the stat, skill and rule ones; `{killer}` in `legacy_titles`). A placeholder nothing resolves stays as written, so the gap is visible. `BattleTextTests` checks that every stat, skill, rule and tooltip string in the shipped content resolves; the builder does not parse templates.

## 6. Error codes

One line per error on stderr: `CONTENT <code> <location>: <message>`, where the location is `<table>`, `<table>[<row id>]` or `constants[<key>]`. The converter reports every error it finds; the loader throws one `ContentLoadException` with the list.

| Code | Meaning |
|---|---|
| `E001` | Required table file missing |
| `E002`, `E003` | Required column absent / unknown column |
| `E010`, `E011` | Cell does not parse / required cell empty |
| `E020`, `E021`, `E022` | Duplicate key / bad enum value / unresolved reference |
| `E023` | A name in a cell is not an id of the table it must reference (element, stat, skill, tree, node, rule, species tree, gauntlet element, book tree) |
| `E030`, `E031` | Reserved (sums and thresholds of the removed idle tables) |
| `E032` | An ordered column is not strictly increasing (`stat_milestones`, `legacy_titles`) |
| `E040`, `E041`, `E042` | Constant missing / wrong type / unknown |
| `E043` | A value is out of its range (each constant's range is in its system spec) |
| `E044` | A table lacks required rows (a species, a rank, an enemy or boss, `fail`/`success`/`super`, a tree with fewer than 2 nodes, a vendor rarity, an achievement unlocking each non-starter species) |
| `E045` | `legacy_titles` must start at floor 1 |
| `E047` | `sort_order` must be exactly `0..N-1` (`elements`, `fight_ranks`, `vendor_rarities`) |
| `E060` | Node `index` must be `0..N-1` contiguous per tree |
| `E064` | The last node of a tree must be a skill, trigger or rule (the chase trait) |
| `E065` | A chase tree has no condition |
| `E070`, `E071` | Bad colour / bad id or key format |
| `E090` | An id (or a table) of the previous build is gone and MAJOR was not raised |
| `E091` | `content_version` is malformed or lower than the previous build's |
| `E092`, `E093` | Reserved for further version-bump checks |
| `E900`, `E901`, `E902` | Loader: file hash mismatch / unknown schema version / manifest and files disagree |

## 7. `ContentDb`

`ContentDb.LoadFromDirectory(dir)` or `ContentDb.Load(IContentSource, verifyHashes)`. Read-only after load and safe to share. Tables are exposed as typed lists and dictionaries (`SpeciesList`, `Species`, `FightRanks`, `DrillTiers`, `StatMilestones`, `Trees`, `TreeList`, `TreeNodes`, `MonsterSkills`, `SkillBooks`, `TalentList`, `Talents`, `AchievementList`, `Achievements`, `VendorRarities`, `StarterSpecies` (the first species), `GauntletEnemies`, `GauntletBosses`, `LegacyTitles`, `ElementList`, `Elements`, `Strings`) and constants through `Constants` (one typed property per constant; property name = PascalCase of the key). `ConstantValues` maps every numeric constant's key to its value, for text templates (section 5); rules read the typed properties. `ElementMult(attacker, defender)` is the wheel. `IContentSource` has `FileContentSource` (a directory) and `DictionaryContentSource` (in memory: tests and the browser's content pack).

## 8. Tests

`ContentDbTests`: the shipped content loads and has every table, the element wheel, every name key has text, a hash mismatch is `E900`, loading is culture-invariant, `IsCompatibleWith`. The builder's own validation is exercised by `./check.sh` (`content_build.py --check`) and by deliberately breaking a CSV when a rule changes. The previous-build rules have unit tests in `tools/test_content_build.py` (`python3 tools/test_content_build.py`, a `check.sh` step).

## 9. Open

- Version-bump checks beyond `E090` and `E091` (`E092`, `E093`), for example requiring a MINOR bump whenever a value changes.
- A scripted Google Sheet export (`tools/content_pull.py`); content is edited as CSV for now.
- Localisation beyond `en`.
