# 24 - Spike: creature inspiration for the skill trees

Status: **spike report (agent, 2026-10-31); kj answered section 6 the same day.** Only the answers in section 6 are agreed design; the rest is research. kj asked for creature inspiration that fits the current skill trees thematically, from any medium (myth, folklore, games, anime, manga, film, books, nature), as long as it is cool and fits the tree. Section 6 has questions for kj.

## 1. Scope and method

- Every tree in `content_src/trees.csv` (content 3.14) gets one creature concept: 6 native, 10 generic, 3 book and 6 chase trees.
- The fit is read from what the tree does in `content_src/tree_nodes.csv`: its node-4 skill, its node-7 trigger or rule, and the stat it is drafted from. A creature that "looks like" its tree tells the player what the tree does before they read a tooltip.
- Each concept lists its inspirations. They are references for mood and silhouette, not designs to copy: every creature here is meant to be an original design (no recreation of an existing character or game).
- The three species are placeholders (`specs/14`), so section 2 also suggests what slime, drake and sprite could look like to carry their trees, and section 5 suggests species for the two unused elements (nature, dark).

## 2. The three species and their native trees

| Species | Native trees | Creature direction |
|---|---|---|
| slime (water) | *Slime Fundamentals*, *Gel Armor* | A jelly with a visible core that floats inside it. Whatever it last absorbed shows in the gel (a sword, a coin, a fish). Hardens into chrome when it guards. |
| drake (fire) | *Pyromancy*, *Heavy Armor* | A squat, heavy young dragon whose scales are forge plates riveted on. The fire lives in a bellows-like throat that glows through the plates. |
| sprite (light) | *Aether Striker*, *Mind Warp* | A small light being with moth wings covered in eye spots. It flickers between positions; the afterglow lingers where it was. |

### 2.1 Native trees

| Tree | What it does | Creature concept | Inspirations |
|---|---|---|---|
| *Slime Fundamentals* | HP; *Gel Mend* heal; *Ooze Back* when hit | **Coremote**: a jelly whose nucleus drifts away from wherever it is hit, then splashes back at the attacker. | Rimuru (*That Time I Got Reincarnated as a Slime*), Dragon Quest slimes, the gelatinous cube (D&D), *The Blob* (1958) |
| *Gel Armor* | *Harden* shield; *Lucky Rebound* on evade | **Quicksilver Puddle**: a chrome slime that sets solid when struck and bounces blows off its mirror skin. | Metal Slime (Dragon Quest, the dodgy one), the T-1000 (*Terminator 2*), mercury, ochre jelly (D&D) |
| *Pyromancy* | *Flame Breath* (all enemies); *Wildfire* on crit | **Cinder Wyrmling**: inhales with a visible bellows chest, exhales a sweeping fan of fire; a crit leaves the ground burning. | The salamander as fire elemental (Paracelsus), Smaug (*The Hobbit*), Monster Hunter's Rathalos, Dark Souls dragons |
| *Heavy Armor* | *Iron Hide* DEF buff; *Plate Mastery* passive | **Bastion Drake**: armoured like a siege tower; each plate is hammered on during training, so it visibly gets heavier as the tree fills. | The heavy knight of *The Exiled Heavy Knight Knows How to Game the System*, ankylosaurus, Monster Hunter's Gravios, Havel (Dark Souls) |
| *Aether Striker* | *Aether Bolt* INT nuke; *Overcharge* SPD on action | **Wisp Lancer**: a ball of light that stretches into a spear of lightning each time it acts, faster every time. | Raijū (Japanese lightning beast), will-o'-the-wisp, Ori (*Ori and the Blind Forest*), Ramuh (Final Fantasy) |
| *Mind Warp* | *Confuse* debuff; *Phase Shift* heal on evade | **Mirage Moth**: its wing eyespots hypnotise; when hit it was never there (a dust-image shatters, the real one is behind you). | The kitsune's illusions, the Cheshire Cat, Puck (*A Midsummer Night's Dream*), real eyespot moths |

## 3. Generic trees (milestone drafts)

These are drafted from a stat, so each creature is the purest image of that stat. They suit "this is what your monster is turning into" draft-card art.

| Tree (stat) | What it does | Creature concept | Inspirations |
|---|---|---|---|
| *Vital Core* (HP) | *Second Wind* heal; *Vital Surge* HP passive | **Heartwood Hulk**: a mossy giant with a glowing heart visible inside a hollow trunk; cut branches regrow. | The Lernaean Hydra, trolls that regenerate, Totoro, Ents (*The Lord of the Rings*) |
| *Brute Force* (STR) | *Smash*; *Follow Through* on basic attack | **Oni Bull**: a horned, club-dragging brute whose every swing is a two-hit combo. | Japanese oni, the Minotaur, Monster Hunter's Rajang, Goro (*Mortal Kombat*) |
| *Spellcraft* (INT) | *Arcane Blast*; *Mana Surge* on crit | **Grimoire Mimic**: a spellbook with teeth whose pages flip faster as it casts; a crit makes it bite a new page out. | The mimic (D&D), the Book of Sand (Borges), grimoires in *Frieren*, the Monster Book of Monsters (*Harry Potter*) |
| *Sage Mind* (WIS) | *Ward* team shield; *Enlightened* passive | **Lantern Tortoise**: an ancient turtle with a stone lantern on its shell that shields the whole team in its light. | Oogway (*Kung Fu Panda*), Genbu, the Shishigami (*Princess Mononoke*), the Great Deku Tree (Zelda) |
| *Nimble Feet* (DEX) | *Quick Jab*; *Riposte* on evade | **Sickle Weasel**: a whirlwind with scythe paws that cuts you as it dodges. | Kamaitachi (Japanese folklore), mantis shrimp, Sun Wukong, real stoats' "war dance" |
| *Bulwark* (DEF) | *Provoke* taunt; *Spiked Shell* when hit | **Thornback Pangolin**: curls into a spiked ball and dares you to hit it. | Pangolins and armadillos, the Black Tortoise, Cactuar (Final Fantasy), hedgehogs |
| *Overclock* (SPD) | *Blitz*; *Afterimage* on action | **Clockwork Hare**: a wind-up hare whose key spins faster every turn until it leaves afterimages. | The March Hare, Sleipnir, the Roadrunner, wind-up toys, the Flash's speed trails |
| *Fortune* (LUK) | *Gamble* luck nuke; *Jackpot* crit damage | **Dice Tanuki**: a pot-bellied tanuki that rolls dice from its sleeve; when they come up all sixes, the reels spin. | Tanuki and maneki-neko, Hakari's jackpot domain (*Jujutsu Kaisen*), the leprechaun, pachinko |
| *Battle Instinct* (any 500) | *Rally Cry* STR buff; *Killer Instinct* crit | **Scarred Dire Wolf**: a battle-scarred alpha whose howl makes it hunt harder; its scars glow on a crit. | Fenrir, Guts' berserker armour (*Berserk*), Inosuke (*Demon Slayer*), the dire wolf |
| *Survivor* (any 500) | *Regenerate*; *Adrenaline* when hit | **Iron Tardigrade**: the "water bear" scaled up; the closer to death, the faster it moves. | Tardigrades (survive vacuum and radiation), the axolotl, Wolverine, cockroaches |

## 4. Book and chase trees (the rare and jackpot looks)

`specs/12` section 5.2 says a chase capstone must feel like the run's jackpot. The strongest way to sell that is a **visual change**: when the chase tree's capstone is bought, the monster takes on that tree's look. These concepts are written as apex forms of the species that owns them.

### 4.1 Book trees

| Tree (species) | What it does | Creature concept | Inspirations |
|---|---|---|---|
| *Echo Song* (sprite) | *Echo Bolt*; *Resonance* INT on action | **Bellfae**: a sprite inside a bell-shaped flower; every spell rings, and the ring repeats. | Echo the nymph (Greek myth), sirens and banshees, singing bowls, whale song |
| *Riptide* (slime) | *Tide Pool* heal; *Undertow* when hit | **Umibōzu Jelly**: a dark, smooth sea-giant head that rises from the slime; striking it drags you under. | Umibōzu (Japanese sea spirit), jellyfish, La the Ocean Spirit (*Avatar: The Last Airbender*) |
| *Ember Heart* (drake) | *Ember Lance*; *Kindle* on crit | **Hearth Salamander**: a fire lizard that lives in a forge; its flames are a living spark with a face. | Calcifer (*Howl's Moving Castle*), Hephaestus' forge, the phoenix's embers, Charmander's tail flame |

### 4.2 Chase trees

| Tree (species) | Capstone rule | Apex form | Inspirations |
|---|---|---|---|
| *Living Ocean* (slime, in-run) | `tidal_overflow`: heals each action, overheal becomes a shield | **Solaris Tide**: the slime becomes a small, living sea with its own weather; the overflow shows as a wave-wall around it. | *Solaris* (Lem: a whole living ocean), Tiamat (primordial salt sea), Kyogre, Te Fiti (*Moana*) |
| *Abyssal Bloom* (slime, book) | `abyssal_drain`: every hit heals it | **Abyssal Siphonophore**: a glowing deep-sea colony; tendrils bloom into its target and pulse light back to it. | Siphonophores (real colonial organisms tens of metres long), *Annihilation*'s Shimmer, anglerfish, Cthulhu |
| *Unbreakable Will* (drake, in-run) | `counter_mitigated`: counterattacks for the damage DEF stopped | **Aegis Wyrm**: the plates crack to show a molten core; every blocked hit flares out of the cracks as a counterblow. | Naofumi's counter shields (*The Rising of the Shield Hero*), Reinhardt (Overwatch), the heavy knight, Iron Golem (Dark Souls) |
| *Inferno Sovereign* (drake, book) | `fire_pierce`: fire ignores part of DEF/WIS and is never resisted | **Surtr Drake**: wears a crown of black flame that water cannot put out. | Surtr (Norse fire giant who burns the world), Amaterasu's black flames (*Naruto*), the Balrog, Kagutsuchi |
| *Starlit Ascension* (sprite, in-run) | `double_turn_start`: acts twice on its first turns | **Comet Moth**: arrives as a falling star, a streak of light that hits twice before anything else moves. | Star Platinum's time stop (*JoJo's Bizarre Adventure*), Jirachi, shooting stars, Sailor Moon |
| *Arcane Overflow* (sprite, book) | `echo_cast`: every active skill casts again at reduced power | **Gemini Moth**: a sprite with a mirror twin a half-beat behind it that repeats each spell. | Dualcast (*Final Fantasy* games, *Slay the Spire*'s Defect), Castor and Pollux, Doctor Strange's mirror dimension |

## 5. Ideas beyond today's trees

### 5.1 Species for the unused elements

`content_src/elements.csv` has nature and dark, but no species uses them. Two candidates that pair naturally with existing generic trees (their native trees would be new content):

| Element | Species concept | Native tree ideas | Inspirations |
|---|---|---|---|
| nature | **Mandrake**: a root creature that grows on the training ground; its scream stuns. Strong with *Vital Core* and *Survivor*. | *Rootbound* (regrowth, thorns when hit), *Mandrake Wail* (stun, poison) | The mandrake root, kodama (*Princess Mononoke*), Groot, Bulbasaur |
| dark | **Shade Hound**: a dog made of a shadow, with no body casting it. Its shadow eats what it defeats. Pairs with the rank-fight loot of `specs/22` 8.6. | *Umbral Step* (evasion, first strike from the shadows), *Shadow Harvest* (grows from defeated foes) | Black Shuck and other black dogs of British folklore, shadow extraction in *Solo Leveling*, Dementors, the nuckelavee |
| water | **Fountain Axolotl** (#160, species of the idea-pool tree *Elemental Avatar*; built in #150): a pink-aqua axolotl whose six feathery gills are glowing little fountains, with a ring of water orbiting it; claws and tail are water jets, so everything it does lands as water. A lucky INT/LUK/WIS caster that converts (*Elemental Avatar*), cancels the conversion's drawback (*Clear Spring*), seeps past defences and shields (*Aquifer*, apex **Aquifer Olm**, a pale translucent cave olm glowing aqua in cracked bedrock) and splits every hit into jets (*Six Fountains*, apex **Xolotl Wellspring**, a mythic river-dragon axolotl with geyser gills and a floating fountain-basin halo). Distinct from the slime (an HP/LUK/WIS regeneration and drain tank) | Elemental Avatar (lane A), Clear Spring, Aquifer, Six Fountains | The axolotl; fountains and springs; the olm ("baby dragon" of folk tales); Xolotl |

### 5.1.1 Idea-pool species (epic #160)

The species lanes of #160 add creatures built around the idea-pool trees (#132); each lane's issue holds the full concept, and `specs/15` section 7 its trees.

| Species | Element, tone | Concept | Trees and capstones |
|---|---|---|---|
| **Candlemane** (`candlemane`, #141) | light, mythic | A sacred lion of living light and candle wax: its mane is a ring of burning votive candles and it burns its own body to shine. The only species that wants to lose HP: HP and WIS, the stats others train to survive, are its damage, and DEF is close to useless to it (the reverse of Bastion Drake). The opposite of Mirage Moth, the other light species: slow and heavy, it takes hits and burns. | *Votive Flame* (`blood_magic`: pay HP, no cooldowns), *Sanctum Grace* (a self-heal and *Kindled Grace*); in-run *Martyr's Pyre* (`martyr_flame`: heals become light damage; apex Martyr Pyrelion), book *Hollow Sun* (`hollow_sun`: HP lost is stored as Radiance and released on hits; apex Hollow Sun Lion, book *Psalter of the Unburnt*). All three rules feed one loop, pay HP, heal, convert to damage: the Exiled Heavy Knight's pattern (`specs/22` section 3.1). |
| **Pyre Stag** (`pyre_stag`, #142) | fire; grimdark with a mythic edge | A gaunt, ash-grey stag whose antlers are its own funeral pyre: hide cracked like cooling charcoal with ember light in the cracks, rusted penitent chains with black bells. It burns itself every turn, and the closer it gets to ash, the harder it hits. The game's glass cannon on a fuse: the only even STR/INT hybrid, the lowest DEF growth (0.5), HP drilled as fuel. | Natives *Funeral Pyre* (`pyre_tithe`) and *Ashen Vigil* (`ash_shroud`, a Life Shield threshold enabler, `specs/22` section 3.1); in-run chase *Inverted Mercy* (`pyre_mercy`: heals become fire) → **Cinder Saint**; book *Litany of Embers*, book chase *Undying Pyre* (`ember_eternal`: death burns max HP instead) → **Everpyre Hart**. Unlocked by *Kindled* (reach rank B). |
| **Keepshell Crab** (`keepshell_crab`, #152) | water, cute | A stubby, glaring rust-orange hermit crab that wears a whole stone castle keep as its shell (crenellated towers, a portcullis over its face, coral and seaweed in the mortar), its claws wrapped in riveted gate-plates and raised to punch. It fights by slamming its fortress into you: its defence is its attack. The only species whose one damage stat is neither STR nor INT: training is all DEF with HP and LUK behind it, every STR or INT point it rolls is wasted, and DEF-scaled generic skills (*Iron Press*, *Shield Bash*, *Spiked Shell*) are its jackpots. The reverse of Pyre Stag and Candlemane, which skip DEF; distinct from Bastion Drake (DEF that hits with STR) and Mandrake (outlasts with HP and WIS). | *Fortress Fist* (`fortress_fist`: DEF is damage, STR and INT count 0), *Tide Moat* (`tide_moat`: DEF also stops spells); in-run *Unbreached Keep* (`unbreached`: small hits bounce off a wall that wears down; apex Citadel Kingcrab), book *Siege Pincers* (`shellcrack`: the enemy's DEF becomes damage; apex Siegebreaker Crab, book *The Siege Manual*). Each tree answers the previous one's hole. |
| **Iaido Mantis** (`iaido_mantis`, #159) | nature, hot | A cracked jade-and-orchid mantis with one torn wing, crouched in a quick-draw stance, one scythe sheathed in a folded leaf (iaijutsu; the orchid mantis that waits for prey). The only species that fights best on the other side's turn: it needs to be hurt, then turns the enemy's attacks into the trigger for its own cuts. The highest base SPD of any species, low HP and DEF on purpose, WIS to survive spells while low. | *Vantage* (lane B's tree: below half HP it strikes first), *Leaf Scabbard* (*Draw Cut*; `iai_draw`: out-of-turn hits deal more and crit more); in-run *Still Hunt* (`still_hunt`: it waits so Vantage answers at any HP and area attacks; apex Ghost Orchid Reaper); book *Severed Arts* with the *Scroll of the Unseen Blade* (`sever_art`: a deep counter cuts the enemy's move off; apex Thousand-Petal Kensei) |

### 5.2 Fusion looks

kj allowed fusion for **some specific pairs**, with the pair clear in the UI (`specs/22` section 6.5). A fused creature that shows both parents makes the pair readable. Candidates from today's trees:

| Pair | Fused look | Why it fits |
|---|---|---|
| *Pyromancy* + *Riptide* | **Geyser Wyrm**: steam vents along its back | fire and water is the classic forbidden pairing; reads as a jackpot |
| *Heavy Armor* + *Bulwark* | **Fortress Turtle-Drake**: a walking castle | two tank trees: taunt + armour |
| *Mind Warp* + *Nimble Feet* | **Mirror Fox**: nine afterimages, only one is real | dodge triggers on both trees |
| *Fortune* + *Spellcraft* | **Tarot Mimic**: a deck of cards with teeth, deals a random spell | gambling magic; Spellcraft's on-crit trigger feeds on Fortune's crit build |

## 6. Questions for kj (answered 2026-10-31)

1. **Tone:** **a mix, the best of all worlds** (kj): cute, mythic, grimdark or hot. No one style is required; each creature takes whichever suits it.
2. **Apex forms:** **yes** (kj). Buying a chase tree's capstone changes the monster's look and gives it the apex name in section 4.2. Rules: `specs/15` section 5.3.
3. **Species names:** **rename them and adjust them to the concepts** (kj). The species ids stay (`slime`, `drake`, `sprite`, so saves and goldens keep working); their names become **Coremote**, **Bastion Drake** and **Mirage Moth**, and the web client draws each from hand-made pixel art of its concept instead of a seeded sprite (since replaced by generated art, `specs/26` section 8; the pixel art stays as the fallback). The *Starlit Ascension* and *Arcane Overflow* apex forms follow the moth: **Comet Moth** and **Gemini Moth**.
4. **Nature and dark species:** **yes** (kj). The **Mandrake** (nature) and the **Shade Hound** (dark) are added as achievement unlocks (Veteran and Climber) with their own native, book and chase trees and four new capstone rules (`specs/15` section 7, `specs/16` section 6.2).
5. **Fusion pairs:** **yes** (kj): the four pairs in section 5.2 are the fusing pairs (`specs/12` section 5.2.1). kj then agreed how it works (2026-10-31): `specs/15` section 5.6, built at content 4.5 (the fused looks were renamed after the species they fit: Fortress Drake, Mirrorwing).
