# 23 - Spike: point economies with variance and dynamism

Status: **spike report (agent, 2026-10-31; issue #112); kj answered section 6 the same day.** Only the answers in section 6 are agreed design; the rest is research. kj asked for research on point and currency economies in general (management, strategy, RPG and roguelite games). The question: what can add variance and dynamism to the skill point (SP) economy, so that runs diverge through skill expression, RNG or both? This follows the higher-tier spike (`specs/22`). Section 6 lists the decisions for kj.

## 1. Why

- kj (2026-10-31): runs are pretty deterministic. Runs need features that make them diverge.
- The capstone targets in `specs/15` section 10 need a spread:
  - the in-run chase capstone in about the top 10% of runs;
  - the book chase capstone in about 50% of runs that bring the own book.
  
  A run-to-run spread this narrow cannot produce a top 10% that is different from the median.
- Today the SP economy is flat: `SP_PER_CYCLE` (2) every cycle plus `SP_PER_WIN` (2) per win (`specs/15` section 1). Nothing a player does with SP except spend it in order.

## 2. Baseline: how much runs vary today

The throwaway harness of `specs/22` section 2 on content 3.13: 200 seeds per species x bot policy, with no books. "Builds" counts distinct sets of slotted trees with nodes bought. "Top-stat sd" is the standard deviation of the highest stat.

| Species | Policy | SP mean (sd, range) | Grades | Distinct builds | Top-stat sd |
|---|---|---|---|---|---|
| slime | balanced | 30.0 (0.0, 30-30) | B 100% | 1 | 8 |
| slime | focus | 35.8 (2.0, 32-40) | A 1%, S 98% | 13 | 72 |
| slime | hoarder | 35.1 (1.0, 34-36) | S 100% | 7 | 66 |
| drake | balanced | 26.0 (0.1, 26-28) | D 99% | 1 | 5 |
| drake | focus | 29.0 (1.0, 28-30) | C 51%, B 49% | 8 | 73 |
| drake | hoarder | 28.3 (0.8, 26-30) | C 81%, B 17% | 12 | 65 |
| sprite | balanced | 28.0 (0.0, 28-28) | C 100% | 1 | 3 |
| sprite | focus | 29.9 (0.5, 28-30) | B 93% | 14 | 72 |
| sprite | hoarder | 29.6 (0.8, 28-30) | B 79% | 4 | 66 |
| any | reckless | 18.5-20.4 (0.9-2.0) | spread over 2-4 grades | 11-17 | 75-78 |

- **SP barely moves.** The standard deviation is 0 to 2 SP on a total near 30. Only wins change it, and wins hardly vary for a given species and policy.
- **Outcomes cluster.** Most species x policy cells end on one or two grades. The balanced bot builds the same monster in all 200 runs.
- **The RNG is in the drills,** where it moves the top stat by about 70 points (sd). It doesn't reach the economy or the build.
- Bots are deterministic policies, so a human varies more. But the rules give a player almost nothing that varies to react to. That is the problem kj named.

## 3. Research digest

Two research passes, through web search: management, strategy and board games; RPG, roguelite, autobattler and raising games. Sources are wikis, guides and news pages. Mechanics not confirmed online are marked (unverified). The MonStack ideas are sketches with placeholder numbers.

### 3.1 Income variance

- **Europa Universalis IV random rulers** ([acoup](https://acoup.blog/2021/05/28/collections-teaching-paradox-europa-universalis-iv-part-iv-why-europe/)): monarch-point income depends on a ruler with near-random stats. **Temperament:** each run rolls a visible income profile, e.g. Studious (3 SP per cycle, 1 per win) or Scrappy (1 per cycle, 3 per win).
- **Stardew Valley daily luck** ([wiki](https://stardewvalleywiki.com/Daily_Luck)): a daily roll that a TV channel reveals. **Form:** a visible per-cycle roll. A good-form cycle raises the super-drill chance and adds +1 SP for a win. Drill hard on good days, Rest on bad ones.
- **Crusader Kings III lifestyle XP** (unverified): XP from a focus converts into perk points. **Drill SP:** a super drill pays +1 SP, so income follows drill luck and focus, not only the clock.
- **Football Manager scouting ranges** ([fmscout](https://www.fmscout.com/a-black-stars-in-scouting-reports.html)): attributes show as ranges until scouted. **Scouting:** the next opponent's stats show as a range, and spending an action or SP narrows it.

### 3.2 Banking, interest, loans and streaks

- **TFT interest and streaks** ([metabot](https://metabot.gg/en/TFT/guides/tft-economy-gold-interest-streaks-explained)): +1 gold per 10 banked, capped at +5, and win or loss streaks pay extra. **SP interest:** +1 SP per 4-5 SP banked at cycle end, capped at +2. The designed point dump (`specs/12` section 5.4) becomes a choice with a cost: banked SP isn't winning fights now.
- **Balatro interest cap vouchers** ([games.gg](https://games.gg/balatro/guides/balatro-economy-guide/)): vouchers raise the cap. **Economy tree:** a tree or a book that raises the interest cap. Some runs open with it, others skip it.
- **Loss-streak pay (TFT):** after 2 straight rank-fight losses, +1 SP per further loss. A bad start becomes a plan.
- **Brass loans** (unverified amounts): money now, income lowered for good. **Borrow:** take +4 SP now, and flat income drops by 1 for the rest of the run. Rushing a capstone becomes a gamble you choose.
- **Through the Ages corruption** ([statelyplay](https://statelyplay.com/2017/09/26/strategy-101-through-the-ages-corruption-edition/)): surplus causes trouble. The opposite of interest: banking above 8 SP raises fail or injury risk. A species trait could pick which one applies (Patient earns interest, Restless suffers corruption).
- **Fire Emblem bonus EXP** ([wiki](https://fireemblemwiki.org/wiki/Bonus_experience)): banked XP spent on any unit. A win pays "trainer XP", spendable as drill gains on any stat. It aims at milestones on purpose.

### 3.3 Discounts, hints and spillover

- **Umamusume skill hints** ([deltia](https://deltiasgaming.com/?p=281560), [wiki](https://umamusu.wiki/Skills)): each hint discounts a skill, capped at about 40%, so waiting for hints before buying is standard play. **Node hints:** a drill result (super above all) drops a hint on the next node of a slotted tree tied to that stat, -1 SP to a minimum of 1. RNG decides where hints land; skill decides buy now or wait.
- **Umamusume bond gauge** ([deltia](https://deltiasgaming.com/?p=285549)): a full bond makes training on that facility spike. **Tree bond:** drilling a tree's stat fills its gauge, and a full gauge makes those drills pay +1 SP.
- **Final Fantasy Tactics JP spillover** (unverified rate): nearby units earn a share. **Spill:** 25% of SP spent in one tree spills into the tree in the next slot, so slot order matters.
- **Civilization VI production overflow** ([civfanatics](https://forums.civfanatics.com/threads/mechanics-testing.608494/)): leftover production carries over. **Overflow:** SP left over past a tree's last node buys the first node of another tree.

### 3.4 Rerolls, shops and scaling costs

- **TFT roll-down:** spend the whole bank at once to hit a target. **Draft rerolls:** a milestone draft can be rerolled for 1 SP, then 2, then 3.
- **Hearthstone Battlegrounds** ([icy-veins](https://www.icy-veins.com/hearthstone/hearthstone-battlegrounds-mechanics-guide)): the tavern upgrade gets cheaper each turn it isn't bought, and freezing the shop is free. **Freeze:** keep a draft offer for the next milestone. A purchase whose price falls each cycle it waits.
- **Slay the Spire card removal:** the price rises with each use. **Respec:** refund one node for 2 SP, +1 per use. This softens the overwrite rule (points lost) without making drafts meaningless.
- **Power Grid turn order** ([BGG](https://boardgamegeek.com/wiki/page/How_To_Teach:_Power_Grid)): the leader pays more. Node cost +1 at rank A and S: a catch-up rule.
- **Luck be a Landlord rent** ([Wikipedia](https://en.wikipedia.org/wiki/Luck_Be_a_Landlord)): a known, rising schedule. Rank fights are already the rent. **Show the next opponents' power ahead**, so players plan SP against a known schedule.

### 3.5 Push-your-luck and wagers

- **Can't Stop** ([Wikipedia](https://en.wikipedia.org/wiki/Can%27t_Stop_(board_game))): gains stay pending until you stop, and a bust wipes the pending gains. **Overtrain streak:** extra drills in a cycle add to a pending gain that locks in only on Rest; a fail loses the pending gain, never the banked gains. One tap at a time, which suits a phone.
- **Quacks of Quedlinburg** ([rules](https://rulespal.com/quacks-of-quedlinburg/rulebook)): keep pulling from a bag until you stop or go bust. Our energy and fail curve already works this way; **show the bust chance on the Drill button.**
- **Hades Chaos gates** ([Prima](https://primagames.com/gaming/hades-chaos-realm-chaos-blessings)): a curse for a few encounters, then a permanent boon. **Chaos Drill:** two cycles at higher fail or injury odds, then a free node.
- **Dead Cells cursed chests** ([wiki](https://deadcells.wiki.gg/wiki/Curse)): big loot, but one hit kills until you get 10 kills. **Cursed book:** a tree now, but an injury costs the banked SP until 2 wins.
- **Fight wager** (Hades Heat, Balatro boss blinds): before the auto-fight, stake 0-3 SP; a win pays double, a loss forfeits the stake. Scouting (3.1) makes this a read, not a coin flip.

### 3.6 Risk-for-reward fights and events

- **Slay the Spire elites, FTL** (unverified specifics): harder optional fights pay more. **Challenge fight:** before each rank fight, pick Standard or Challenge. Challenge is a stronger opponent for +3 SP or a skill book roll, and a loss also costs energy. **The main skill lever when the fights themselves are automatic.**
- **Balatro skipping a blind for a tag:** give up the fight (no promotion, no win SP) for a visible tag: a free book, +3 SP, a guaranteed super drill.
- **Slay the Spire Neow** ([wiki](https://slay-the-spire.fandom.com/wiki/Neow)): a start-of-run boon, some with a drawback. **Cycle-0 pick:** "+6 SP but start at 70% energy", "a random tree at 0 SP", "+1 SP per cycle but -1 AP".
- **Quacks "points or money"** ([tabletoptogether](https://tabletoptogether.com/2019/04/20/review-the-quacks-of-quedlinburg/)): after a bust, take points or coins, whichever the moment needs. **Loss payout:** a lost rank fight pays 2 SP now, or +1 SP income for the rest of the run.
- **Monster Rancher errantry** ([LegendCup](https://legendcup.com/faqmr2techs.php)), Umamusume event chains: a costly trip whose odds depend on stats. **Expedition:** a cycle spent on a roll for a skill book, with odds from the matching stat total. Milestones double as gamble odds.
- **RimWorld storyteller** ([wiki](https://rimworldwiki.com/wiki/Raid_points)): threats scale with wealth. Opponents scale with SP spent, or a per-run cadence (calm early and hard late, or random).
- **Against the Storm storms** ([guide](https://benedictjacka.co.uk/other-writing/against-the-storm-strategy-guide/)): an announced hard season. **Storm cycle:** announced, random per run. Drill costs double, wins pay +3 SP.

### 3.7 Currencies and conversion

- **Final Fantasy X Sphere Grid:** typed spheres gate typed nodes. **Typed SP:** physical drills earn Body SP and mental drills earn Mind SP; trees cost a mix. Drill choices then decide which trees are affordable.
- **Inscryption teeth** ([pcinvasion](https://www.pcinvasion.com/inscryption-beginners-guide-card-upgrades-vendors/3)): overkill pays. **Margin SP:** a dominant win (HP left above 75%) pays +1 SP.
- **Quacks endgame conversion:** leftovers convert at a poor rate. Unspent SP at the end becomes Training Shards at a poor rate, so banking is a hedge, not a trap.
- **Civilization buy vs build:** two actions convert to 1 SP, or 1 SP to an extra action.

### 3.8 Timing and scarcity

- **Puerto Rico bonus coins** ([Wikipedia](https://en.wikipedia.org/wiki/Puerto_Rico_(board_game))): a role nobody takes gains a coin each round. **Neglect bounty:** each stat grows a bounty of +1 SP per cycle it isn't drilled (random growth as a variant), and drilling it collects. This breaks "always drill the best stat" and makes builds diverge, at almost no UI cost.
- **Frostpunk laws** ([wiki](https://frostpunk.gamepedia.com/Administration)): choosing one branch locks out the other. **Fork nodes:** a tree node offers A or B, and the other is locked for the run.
- **Civilization golden ages:** a threshold triggers a temporary boost. **Hot streak:** three wins in a row make the next node 1 SP cheaper.
- **Dominion greening** (unverified): points clog the engine, so timing the switch is the skill. A capstone that weakens drill odds once bought makes buying it at cycle 8 versus cycle 11 a real choice.

## 4. Pitfalls

- **Solved lines.** TFT players bank to 50 gold as standard play. Interest compounds over 12 cycles unless the cap is low, interest starts late, or not spending costs fights. Every lever needs a value that depends on run state (Quacks: money is better early, points later). Test with a "bank until cycle 9" policy against "spend greedily"; if hoarding wins clearly, cut the cap.
- **Snowball and death spirals.** Win SP plus Challenge SP plus interest all stack on runs that are already winning. Loans plus injuries can end a run by cycle 4. Pair them with catch-up (loss pay, rank-scaled cost) and an income floor.
- **Phone load.** At most one new decision per cycle, shown as a two-option card, not a market. At most two currencies plus one gauge. Show cost and discount on the node itself.
- **Invisible math.** Hidden luck and hidden rubber-banding feel unfair. Show bust odds, bounties, stakes and discounts on the buttons; label catch-up ("Grit +2").
- **Noise, not divergence.** Randomness that ignores the player's state feels arbitrary. Tie odds to state (stats set expedition odds, hints land on slotted trees). Use **at most two RNG sources per run**.
- **Bots must use the levers,** or `balance` underestimates the spread. Each new lever needs a policy that uses it.

## 5. Recommendation (agent)

A first package of **three levers**. Each touches a different part of the cycle and adds both skill and RNG, and each is cheap to build on today's rules. Build them together, measure the spread, then tune toward the `specs/15` section 10 targets.

| Lever | Where | Adds | Why first |
|---|---|---|---|
| **Node hints** (3.3) | Drill results | RNG (where hints land) + skill (buy now or wait) | Reuses the drill roll; every run's hints differ; directly cheapens the chase path |
| **Challenge fight** (3.6) | Before each rank fight | Skill (read the matchup) + RNG (the fight) | The one decision the auto-fight lacks; a lever for the 50% book target (a book roll as a Challenge reward) |
| **Capped SP interest** (3.2) | Cycle end | Skill (bank or spend) | Makes the designed point dump a choice with a cost; cap +2 per cycle |

Next candidates, after the package is measured:
- **neglect bounties** (3.8): cheap, and it breaks "always drill the best stat";
- **cycle-0 boon with a drawback** (3.6): divergence from the first decision;
- **fork nodes** (3.8): divergence without RNG;
- **unspent SP to shards** (3.7): the hedge that keeps banking from being a trap.

Hold back loans, wagers, typed SP, temperament and storms until the package shows how much spread is needed. Each adds variance, and stacked RNG drowns skill.

**How to measure.** Add the spread itself to `balance`: SP sd, grade entropy, distinct builds, and capstone rate (#111). Then judge each lever by how much it widens the spread, not only by the mean.

## 6. Questions for kj (answered 2026-10-31)

1. **The first package** (node hints, Challenge fights, capped SP interest): **approved** (kj). It is not built yet.
2. **Challenge reward:** **skill points** (kj).
   - kj also asked for **Skill Books to be uber rare and uber strong**. Content 3.14 cut the Daily Tower drop chance from about 3-5 books a day to about one per 15 days of full clears (`specs/21` section 5).
   - The book chase capstones are to be strengthened to match (`specs/15` section 10).
3. **How much RNG:** **at most two new sources of randomness per run** (kj).
   - **(agent)** What counts: the core rolls are not counted (drill tier and gain, fight RNG, milestone draft draws). A source is a new system whose random draw makes runs differ.
   - The package uses **one**: node hints (where a hint lands). Challenge fights reuse the fight RNG; interest has no randomness.
   - **The second slot stays free** until the package is measured. The candidates are the cycle-0 boon (a random set of offers) and neglect bounties with random growth.
4. **Overwrite:** **keep** "points in an overwritten tree are lost"; no respec (kj).
5. **Visible odds:** **yes** (kj). Bust chance, hint discounts and interest are shown on the buttons.
