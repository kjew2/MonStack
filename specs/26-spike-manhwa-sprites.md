# 26 - Spike: manhwa-style creature art

Status: **spike report (agent, 2026-10-31); kj answered section 6 the same day.** Only the answers in section 6 are agreed design; the rest is research. kj asked for a spike on creature sprites in the style of widely known manhwa (Korean webtoons): (1) is it feasible, (2) which styles are the most popular, (3) a panel comparing some of our creatures in those styles.

The panel is the artifact https://claude.ai/artifact/6kuv4A2UWBV6sM6UVmizpo. Its 20 concept drawings are kept in `specs/assets/26/` (hand-written SVG, one file per style and species), so they survive without the artifact.

## 1. Where the art is today

- The web client draws every creature from 12x12 pixel art in `web/sprites.js`: five hand-made species (Coremote, Bastion Drake, Mirage Moth, Mandrake, Shade Hound; `specs/24` section 6) plus ten apex forms made by palette swap and overlay. Enemies are seeded sprites. There are no image files.
- That is 15 creature looks today. Every new species adds one base look and two apex looks.
- The phone artifact is already published as several files (`web/wasm/publish-artifact.sh`, through `artifact-files.py`), so shipping image files is a matter of adding them to that list. 15 portraits at roughly 50-150 KB each fit easily inside the 16 MB page limit.

## 2. Feasibility

**Short answer: feasible, but not from inside an agent session alone.** The manhwa look comes from painted, high-resolution art. The routes:

| Route | Quality and consistency | Cost and speed | Risks |
|---|---|---|---|
| **A. AI image generation** (FLUX, SDXL, or a hosted model such as GPT-image or Imagen; a style LoRA or reference images to keep one look across creatures) | Can reach the manhwa look closely. Keeping the same creature recognisable across its base and two apex forms needs a reference image or a LoRA per species, plus a manual pick from several tries. | Cents per image; a day or two for 15 looks including cleanup (transparent background, crop, size). | Base-model licences differ (FLUX.1 [dev] is non-commercial; FLUX.1 [schnell], paid APIs and SDXL allow commercial use). Prompting with a living artist's name, or a LoRA trained on one artist's pages, is legally and ethically risky: describe the style instead. Steam has asked for a disclosure of AI content since January 2024, narrowed in January 2026 to content players see; a mobile store listing may raise the same question. |
| **B. Commissioned artist** | Best consistency and full ownership; an artist can match a style family without copying anyone. | Weeks; a few hundred dollars per finished illustration is a common range, so a few thousand for 15 looks. | Schedule and budget. |
| **C. Hand-written vector (SVG) by an agent** (what this spike did) | Works today with no keys or assets. Reaches the flat, ink and dark-action styles convincingly; the painterly style only as clean vector shading, without real brushwork. Below real manhwa painting. | Hours; tiny files that scale to any size. | Quality ceiling; each new look is another hand drawing. |
| **D. Hybrid** | AI or agent concepts, then an artist (or an agent with the chosen concept as reference) produces the final set. | Between A and B. | Same licence questions as A for the concept step. |

What blocks route A **in this environment**: the cloud session has no image model and no image-API key, so an agent cannot generate raster art here. It would need an API key added to the environment's secrets (and the host on the network allow list), or kj generating images and committing them.

**Fit with the game.** Manhwa art is drawn for tall scrolling panels and close-up portraits. It reads best large: the monster card, the draft-card art, the apex reveal and the fight intro. In a battle row at 48-64 px, a painted portrait turns to mush, while the 12x12 pixel art stays readable. The natural split is **manhwa portraits for cards and reveals, small sprites for the battle view**, with the small sprite later redrawn from the portrait so the two match.

## 3. The most popular manhwa styles

There is no reliable public ranking of webtoons by readers (platforms publish per-series subscriber or view counts inconsistently, and most "top 10" pages are unsourced). The most solid popularity signal is which series got big adaptations, so the families below are ranked by that, plus fit with this game.

| Style family | Flagship series | Popularity signal | Look | Fit with a monster raiser |
|---|---|---|---|---|
| **Hunter / system dark action** | *Solo Leveling* (art: DUBU, Redice Studio), *Omniscient Reader's Viewpoint*, *SSS-Class Revival Hunter* | *Solo Leveling* anime (2024-25) was a global hit; *Omniscient Reader* got a live-action film (2025). The dominant look of action-fantasy webtoons today. | Near-black scenes, hard two-tone cel shading, rim light and glowing eyes in electric blue or violet, sharp silhouettes, aura smoke. | High. Ranks, gates, leveling and "system" windows are already this game's vocabulary (`specs/22` section 8). Strongest for dark and fire species and for apex reveals. |
| **Classic webtoon flat** | *Tower of God* (SIU), *The God of High School*, *Noblesse* | All three got anime (2020; *Tower of God* season 2 in 2024); *Tower of God* is often cited at several billion views. | Bold uneven outlines, flat colour with one hard shadow, simple expressive faces. | High for readability: survives small sizes best of the four, cheapest to produce consistently. |
| **Painterly high fantasy** | *The Beginning After the End* (Fuyuki23), *The Remarried Empress*; *Lore Olympus* (a Webtoon original, not Korean) for colour-driven work | Long-running top fantasy titles; *TBATE* got an anime adaptation; *Lore Olympus* won an Eisner. | Soft gradients, warm key and cool bounce light, bloom, bokeh backgrounds; reads like a cover illustration. | Medium. Gorgeous for apex reveals and store art; hardest to keep consistent and weakest at small sizes. |
| **Murim ink** | *Return of the Mount Hua Sect*, *Nano Machine*, *The Legend of the Northern Blade* | Among the top-selling martial-arts titles in Korea; a whole genre on its own. | Ink-wash greys on rice paper, tapered brush strokes, one restrained accent colour, red seal stamp. | Medium. Very distinctive and cheap to keep consistent; suits the folklore creatures (Mandrake, Shade Hound) and a possible eastern-myth region more than the whole roster. |

Families researched but not drawn: **romance soft-pastel** (*True Beauty*, one of the most-subscribed series on Webtoon; little fit with monsters), **urban street** (*Lookism*, Netflix anime 2022; human-centred), and **chibi gag** (the super-deformed comedy panels most manhwa use, *Eleceed*'s cats). Chibi is the obvious candidate for a cute variant of any of the four (e.g. hunter-dark for fights, chibi for the training screen) and is worth a second panel if kj likes the idea.

## 4. The panel

Five species (`specs/24` section 6) in each of the four drawn families, next to today's 12x12 pixel art. The drawings are original creatures, drawn in a style family's rendering language, not copies of any character. They are agent-made SVG (route C), so they show the direction and readability of each style, not final quality: route A or B would look markedly better, the painterly column most of all.

### 4.1 Stretching a style to the UI chrome

Asked by the UI overhaul (issue #115): could a family dress the whole UI, not just the creatures? Mostly with CSS and fonts; few image assets.

| Family | Chrome treatment | Assets needed | Fonts (Google Fonts) |
|---|---|---|---|
| Hunter / system dark | The "system window": near-black panels with a thin glowing border (box-shadow in the element colour), cut corners (`clip-path`), uppercase labels, a blue glow on focus and rare events. Reads as the in-world status screen. | None; CSS only. A few SVG icons for stats. | Display: Black Han Sans or Chakra Petch (already loaded); body: Gothic A1 or Barlow. |
| Classic webtoon flat | Comic panels: thick dark borders, flat fills, hard offset shadows, speech-bubble tooltips, halftone dot backgrounds (CSS radial-gradient). Closest to today's arcade skin. | None; CSS only. | Display: Bangers (already loaded) or Black Han Sans; body: Gothic A1. |
| Painterly high fantasy | Ornate frames, parchment and gradient panels, soft glows. | Needs painted frame and banner images (9-slice) to look right; CSS alone looks cheap. | Display: Cinzel; body: Gothic A1. |
| Murim ink | Rice-paper panels (CSS gradient plus an SVG noise filter), brush-stroke dividers and buttons, a red seal stamp for rare events, ink-splash transitions. | A handful of small SVG brush strokes and the seal; the rest is CSS. | Display: Nanum Brush Script or Gowun Batang; body: Gowun Dodum or Noto Serif KR. |

Palette tokens that line up with any of them: a background, a raised surface, ink (text), muted text, one accent, plus the existing per-stat and per-element colours, which every family keeps. Hunter-dark and flat both work with today's dark phone palette; ink and painterly want a light, paper-coloured base.

**Live: the UI chrome is daylight (kj, 2026-10-31), replacing hunter / system dark.** kj compared seven chrome themes and sixteen title fonts on a mock Train screen and picked daylight with its own font. `web/index.html` carries it: the `:root` tokens hold the palette (a pale sky-blue base, white surfaces, soft blue lines, navy ink, sky blue `--gems` as the accent, text-strength accent shades that read on white, a bright `--gold-pop` for loud outlined text), the fonts (Fredoka for titles, banners and grades; Nunito for labels, numbers and body; Caveat stays for the fail pop), generous radii and soft blue shadows; a skin block at the end of the sheet draws the sky with two soft clouds, makes every panel a white rounded card with a soft shadow, every button a chunky key that presses down (Fight in coral, primary in green, the accent in blue), and the tab bar a row of pills. The creature art stays hunter (section 6); only the chrome changed. **Before (hunter / system dark, live until 2026-10-31):** near-black navy windows with a thin inward-glowing border, cut corners, uppercase labels, a faint blue grid and a "System message" alert for tree offers; Black Han Sans, Chakra Petch and Barlow.

**Phone layout rule (issue #130, 2026-11-03).** On a 390x844 phone no screen makes the player scroll to find what to do next. A screen's main buttons sit in an action bar (`.actionbar`) that rides the bottom of its scrolling pane (grade screen, vendor, tree draft, fight result, floor playbacks, Dungeon and Endless Tower, the rulebook editor's Save); Back sits in the screen's header, left of the title (`.screenhead`), never at the page's foot; stat breakdowns are two columns of compact bars (`.statgrid`); the Vault detail sheet puts its actions above the stats and folds each tree's node list behind "All N nodes"; empty tree slots are one slim line; toasts show at the top of the screen so they never cover the action bar or the tabs. A screen too full for one phone screen splits into tabs (the Endless Tower: Team, Floors, Board; `specs/28` section 9 step 5). Long lists (Vault, achievements, ladder, log, floor lists) may scroll. The browser checks hold it: `layout()` in `web/wasm/test/harness.js` fails a screen whose action-bar, full-size primary or Back button is outside the screen (or under the tab bar) as it opens.

## 5. Recommendation

1. **Pick hunter/system dark action as the main style.** It is the most popular action look, matches the game's own vocabulary, and makes apex reveals feel like a jackpot. Use the **flat** family's rules (bold outline, at most two tones) for the small battle sprites so they stay readable.
2. **Use the art for portraits first** (monster card, draft cards, apex reveal); keep the pixel sprites in battle until the portraits are settled.
3. **Produce the final set by route D**: generate concepts with a commercially licensed model and style descriptions (no artist names), pick one per species, then have an artist (or an agent working from the picked concept) make the 15 looks consistent. Disclose AI use if it ships on Steam.

## 6. Questions for kj (answered 2026-10-31)

1. **Style:** **hunter / system dark action** (kj, final). kj tried the abyss expedition watercolour style (section 7) in between, compared AI drafts of both (section 8) and settled on hunter. It is the art direction for the creatures. Whether the UI chrome follows it is for the UI overhaul, issue #115 (sections 4.1 and 7.1): it did for a while, then kj moved the chrome to daylight (section 4.1).
2. **Route:** **AI generation** (kj). It needs an image-API key and network access in the cloud environment (done by kj, 2026-10-31); issue #116 has the steps and the generation plan. Prompts describe the style in words and never name an artist or series; a model licensed for commercial use only (section 2).
3. **Where the art shows:** **both** (kj): portraits on the monster card, draft cards and apex reveals, and the battle sprites redrawn in the same style. Sprites use a bold outline and simple two-tone shading so they read at 32-88 px, keeping the glowing accents (eyes, nucleus, eyespots). The 12x12 pixel art in `web/sprites.js` stays until the generated set replaces it.
4. **Chibi:** **not for now** (kj); maybe a later use case.

## 7. Addendum: batch 2, anime and manga known for their art

kj asked for a second batch of demos after section 6 was answered: creature styles inspired by anime and manga known for good art. Same method as section 4 (agent-drawn SVG, original creatures, rendering language only), drawn without the image API to save cost. The panel artifact shows batch 2 next to the chosen hunter style; the drawings are in `specs/assets/26/` (`ghibli_`, `abyss_`, `meshi_`, `berserk_`, `ukiyo_`). kj picked the abyss style from this batch for a while, then settled on hunter (section 6.1); the rest is research.

| Style | Inspired by | Known for | Fit |
|---|---|---|---|
| Forest-spirit watercolour | *Princess Mononoke*, *Spirited Away*, *My Neighbor Totoro* | Hand-painted nature spirits; the benchmark for warm, wondrous creature design. | Medium: charming but soft; suits the training screen more than fights. |
| Abyss expedition watercolour | *Made in Abyss* | Delicate watercolour worlds; cute creatures with one unsettling detail. | High: cute-but-eerie fits a monster raiser (kj's pick for a while; section 6.1). |
| Monster field guide | *Delicious in Dungeon* (*Dungeon Meshi*) | Monsters drawn as believable animals, like naturalist studies. | Medium: ideal for a bestiary or codex page. |
| Grimdark pen and ink | *Berserk*, *Vagabond*, *Claymore* | Some of the most admired linework in manga: dense hatching, weight, scars. | Low as a main look: hatching dies at sprite size; strong for apex reveals. The agent drawings undersell it most (hatching too light). |
| Woodblock print action | *Demon Slayer*, *Okami*, Edo-period yokai prints | Effects drawn as ukiyo-e prints: bold outline, pattern, waves and fire. | High for effects: skill and capstone flashes, rank-fight banners. |

Possible uses next to the abyss style, none decided: the field-guide style for a bestiary page (it shares the pencil-and-watercolour look) and woodblock motifs for skill effects.

### 7.1 The abyss style on the UI chrome

For issue #115, in the terms of section 4.1: mostly CSS, a few small assets. Panels read as deep layers: dark teal-to-ink backgrounds with a soft vertical depth gradient, thin sepia borders with a hand-drawn wobble (an SVG border image or `feTurbulence` displacement), paper grain at low opacity, and bioluminescent glows (teal, amber) on focus, rare events and capstones. Assets: a few hanging-root and glowing-flora corner ornaments as SVG. Fonts (Google Fonts): a hand-lettered display face such as Amatic SC or Caveat (already loaded), with a soft humanist body face such as Nunito or Gothic A1. It keeps a dark base, so today's dark palette and per-stat and per-element colours carry over.

**Was live briefly, then replaced (kj, 2026-10-31):** the UI wore this style for a while; when the creature art settled on hunter / system dark, kj moved the UI to hunter too, and later to daylight (section 4.1).

## 8. Generation (issue #116)

### 8.1 Tool

- `tools/art/generate.py` calls the OpenAI Images API with `OPENAI_API_KEY`. Prompts are in `tools/art/prompts.json`: one per style (`abyss`, `hunter`), one per species and one per apex form, plus `apex_base` and `sprite`. They describe the style in words and name no artist or series. Each species prompt names its one unsettling detail (section 7); only the abyss style uses it.
- `draft` makes new candidates (`gpt-image-2`). `apex` draws an apex form from a picked image as a reference (`gpt-image-2`, image edit). `sprite` redraws an image as a battle sprite on a transparent background (`gpt-image-1.5`, because `gpt-image-2` cannot make a transparent background).
- `tools/art/export.py` turns the masters in `tools/art/final/` into the web assets: `web/art/portrait/<key>.webp` (512 px) and `web/art/sprite/<key>.webp` (trimmed to the creature, 192 px), key = the apex form's chase tree id or the species id.

### 8.2 What was made (2026-10-31)

- Drafts, three per species per style, in `tools/art/drafts/abyss/` and `tools/art/drafts/hunter/` (deleted 2026-11-01 once the finals were picked; git history holds them), shown on the panel's "AI drafts" tab. kj first picked abyss drafts, asked that the art not look washed out (the abyss prompt now asks for rich colour and deep contrast), then settled on hunter.
- **Hunter picks (kj):** Coremote 1, Bastion Drake 1, Mirage Moth 1, Mandrake 3, Shade Hound 3. They are the base portraits as generated, in `tools/art/final/<species>.webp`.
- The ten apex forms, each drawn from its species' pick, in `tools/art/final/<chase tree>.webp`, and the fifteen battle sprites in `tools/art/final/sprite/`. The sprites came out as hi-bit pixel art with a bold outline, which reads well at 32-88 px. **(agent)** Accepted as is: it keeps the game's pixel look in battle while the portraits carry the painted style.
- **Apex distinctness (kj, 2026-11-03):** every apex form must differ clearly from its base form and from its sibling apex, by silhouette, colour palette or both, and no art should read as disturbing (no clustered bumps or holes on a body). An audit of the ten apex forms found Worldroot Colossus and Grave Shuck too close to their base; kj picked redraws (a moss-green tree giant; a pale silver-blue ghost hound among gravestones), now in `final/` with new sprites and their prompts in `prompts.json`. Redraws that change the palette need the edit prompt to drop `apex_base`'s "same colours" line.
- **Apex art for the planned species (epic #160):** all 36 apex forms of the 18 planned species are drawn and audited by the rule above, on branch `art/species-drafts` in `tools/art/drafts/apex/` (`<species>_apex1.webp` is the in-run chase apex, `_apex2` the book or harder in-run chase apex). `apex.json` there holds each prompt and reference. A species lane copies its two files to `final/<chase tree>.webp` instead of generating new apex art, then makes the sprites.

### 8.3 In the web client

- Every species sprite (home cards, run HUD, fights, tower and gauntlet playback, Vault) is the generated sprite of the monster's apex form, else of its species (`IdleSprites.art`, `run.js` `sprite()`). An image that fails to load falls back to the 12x12 pixel art, which also stays for species without art. Rank opponents keep their seeded sprites.
- The home screen's Raise tab shows each owned species' portrait as a square card in a sideways-swiping carousel: the whole image, uncropped and edge to edge, at most 240 px wide and centred (issue #120; 320 px was too tall on a phone, kj 2026-10-31), with the name and element over its foot (the card that settles in the middle is the pick; kj 2026-10-31, chosen over a full-screen backdrop, an edge-to-edge hero and a blurred backdrop). **(agent)** Other portrait spots (an apex reveal, a monster card) are not built yet; the UI overhaul (#115) is the natural place for them.
