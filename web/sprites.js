// Seeded 12x12 pixel sprites drawn on canvas, no image files. Ported from the earlier web-proto build.
// Cosmetic only: uses its own hash-based RNG keyed by hero id / stage, never the game's RNG streams.
(function (root) {
  'use strict';
function hash(str) { let h = 2166136261; for (let i = 0; i < str.length; i++) { h ^= str.charCodeAt(i); h = Math.imul(h, 16777619); } return h >>> 0; }
function rng(seed) { let a = seed >>> 0; return () => { a = (a + 0x6D2B79F5) >>> 0; let t = a; t = Math.imul(t ^ (t >>> 15), t | 1); t ^= t + Math.imul(t ^ (t >>> 7), t | 61); return ((t ^ (t >>> 14)) >>> 0) / 4294967296; }; }
const pick = (r, arr) => arr[Math.floor(r() * arr.length)];
const SPR = {};
const BODY = [
  "............",
  "............",
  "....SSSS....",
  "...SESSES...",
  "...SSSSSS...",
  "....SSSS....",
  "..AAAAAAAA..",
  "..SABBBBAS..",
  "..S.AAAA.S..",
  "....LLLL....",
  "....LL.LL...",
  "...FF...FF..",
];
const HATS = [
  ["....HHHH....","...HHHHHH...","...H....H..."],          // helm
  ["...HHHHHH...","..HHHHHHHH..","..HH....HH.."],          // hood
  [".....HH.....","...HHHHHH...","..HHHHHHHHH."],          // wizard hat
  ["....HHHH....","...HHHHHH...","............"],          // hair
  ["...H.HH.H...","...HHHHHH...","............"],          // crown
];
function heroSprite(id) {
  if (SPR[id]) return SPR[id];
  const h = { tier: id[0] }; const r = rng(hash(id + "hero"));
  const tierCol = { C: ["#8d8a9c","#b7b3c7"], R: ["#3d7fe0","#79b4ff"], E: ["#8b52e0","#c49bff"], L: ["#e98a1c","#ffc069"], M: ["#e0306f","#ff8ab3"] }[h.tier];
  const pal = {
    S: pick(r, ["#f5d0a9","#e0ac7e","#b97c51","#8a5a3b","#f2c6b0"]),
    E: "#1b1428",
    A: tierCol[0], B: tierCol[1],
    L: pick(r, ["#3b3355","#4a3b2a","#2e3d4f","#5a2e3e"]),
    F: "#2a2133",
    H: pick(r, ["#d9b84a","#6e4a2e","#c8c8d8","#7a2f3f","#2f6f5e","#3f3a8a","#e8e0c8","#9a3fd0"]),
    W: pick(r, ["#d6dbe6","#c9a45c","#8fd1ff"]),
  };
  if (h.tier === "L" || h.tier === "M") pal.H = h.tier === "M" ? "#ff8ab3" : "#ffd36b";
  const g = BODY.map(row => row.split(""));
  const hat = (h.tier === "L" || h.tier === "M") && r() < 0.6 ? HATS[4] : pick(r, HATS.slice(0, 4));
  hat.forEach((row, y) => row.split("").forEach((c, x) => { if (c === "H") g[y][x] = "H"; }));
  // hair/helm sides over the head
  if (hat !== HATS[2]) { g[2][3] = "H"; g[2][8] = "H"; }
  const weapon = Math.floor(r() * 3);
  if (weapon === 0) { for (let y = 3; y <= 8; y++) g[y][11] = "W"; g[7][10] = "W"; }           // sword
  else if (weapon === 1) { for (let y = 2; y <= 10; y++) g[y][10] = "L"; g[1][10] = "W"; g[2][11] = "W"; g[2][9] = "W"; } // staff
  else { g[4][10] = "W"; g[5][11] = "W"; g[6][11] = "W"; g[7][11] = "W"; g[8][10] = "W"; }      // bow
  return (SPR[id] = paint(g, pal));
}
function playerSprite(cls) {
  const key = "player" + cls; if (SPR[key]) return SPR[key];
  const cols = ["#9a8f7a","#a8b0c0","#c9ced8","#f2d27a","#ffe08a"];
  const pal = { S: "#e8c39e", E: "#1b1428", A: cols[cls], B: "#f2c14e", L: "#3b3355", F: "#2a2133", H: cols[cls], W: "#e6ebf5" };
  const g = BODY.map(row => row.split(""));
  HATS[0].forEach((row, y) => row.split("").forEach((c, x) => { if (c === "H") g[y][x] = "H"; }));
  g[2][3] = "H"; g[2][8] = "H";
  for (let y = 2; y <= 8; y++) g[y][11] = "W"; g[7][10] = "W"; g[6][10] = "B";
  if (cls >= 3) { g[0][5] = "B"; g[0][6] = "B"; }
  return (SPR[key] = paint(g, pal));
}
// hue (0-359) tints the body, e.g. a rank opponent in its element's colour; omitted, the seed picks it.
function enemySprite(seed, boss, hue) {
  const key = "en" + seed + (boss ? "b" : "") + (hue === undefined ? "" : "h" + hue); if (SPR[key]) return SPR[key];
  const r = rng(seed * 7919 + 13);
  const W = 12, Hh = 12; const g = Array.from({ length: Hh }, () => Array(W).fill("."));
  for (let y = 2; y < Hh; y++) for (let x = 0; x < W / 2; x++) {
    const cx = W / 2 - 0.5, dx = Math.abs(x - cx) / (W / 2), dy = Math.abs(y - 7) / 5.5;
    if (r() < 1.05 - (dx * dx + dy * dy) * 0.9) { g[y][x] = r() < 0.2 ? "B" : "A"; g[y][W - 1 - x] = g[y][x]; }
  }
  for (let y = 2; y < Hh; y++) { if (g[y][5] === ".") g[y][5] = "A"; if (g[y][6] === ".") g[y][6] = "A"; }
  const ey = 5 + Math.floor(r() * 2); g[ey][3] = "E"; g[ey][8] = "E"; g[ey][4] = "W"; g[ey][7] = "W";
  if (r() < .5) { g[ey + 2][4] = "E"; g[ey + 2][5] = "E"; g[ey + 2][6] = "E"; g[ey + 2][7] = "E"; }
  if (boss) { g[0][2] = "H"; g[1][2] = "H"; g[1][3] = "H"; g[0][9] = "H"; g[1][9] = "H"; g[1][8] = "H"; g[2][4] = "H"; g[2][7] = "H"; }
  const rolled = Math.floor(r() * 360);
  if (hue === undefined) hue = rolled;
  const pal = { A: `hsl(${hue} 45% 45%)`, B: `hsl(${hue} 50% 32%)`, E: "#120d1c", W: "#fff4d6", H: boss ? "#f2c14e" : "#ddd" };
  return (SPR[key] = paint(g, pal));
}
// Hue of a #RRGGBB colour (0-359), for tinting a sprite in an element's display colour.
function hueOf(hex) {
  const n = parseInt(String(hex).slice(1), 16);
  const r = (n >> 16 & 255) / 255, g = (n >> 8 & 255) / 255, b = (n & 255) / 255;
  const max = Math.max(r, g, b), min = Math.min(r, g, b), d = max - min;
  if (!d) return 0;
  const h = max === r ? ((g - b) / d) % 6 : max === g ? (b - r) / d + 2 : (r - g) / d + 4;
  return Math.round(h * 60 + 360) % 360;
}
// Hand-drawn species art (specs/24 section 6): 12x12 rows, one palette letter per pixel, "." is empty.
const SPECIES_ART = {
  slime: { pal: { A: "#1d5f9e", B: "#4fb3e8", W: "#d8f3ff", E: "#10203a", C: "#f2c14e", K: "#fff4b0" }, rows: [   // Coremote
    "............", "............", "....AAAA....", "...AWWBBA...", "..AWBBBBBA..", "..ABEBBEBA..",
    ".ABBBBBBBBA.", ".ABBBCCBBBA.", ".ABBCKKCBBA.", "AABBBCCBBBAA", "ABBBBBBBBBBA", ".AAAAAAAAAA."] },
  drake: { pal: { P: "#4a4f5c", M: "#a9b2c4", R: "#c2452e", O: "#ffb347", E: "#ffe066", D: "#2a2133", H: "#eef1f6" }, rows: [   // Bastion Drake
    "............", ".......H.H..", "......MMMM..", "..M..MRRERM.", ".MPM.MRRRRRO", "MPPPMMRRMM..",
    "MPOPPPPPM...", "MPPOPPOPPM..", ".MPPOOOPPM..", ".MMPPPPPMM..", "..MR..MR....", "..DD..DD...."] },
  sprite: { pal: { A: "#6b5b3e", W: "#fff6d8", Y: "#f2c94c", K: "#3a2a5a", O: "#ff9f43", B: "#c9b27a", E: "#1b1428" }, rows: [   // Mirage Moth
    "...A....A...", "....A..A....", "YY...BB...YY", "YWY.BEEB.YWY", "YKOYBBBBYOKY", "YOKYYBBYYKOY",
    ".YYYWBBWYYY.", "..YWYBBYWY..", ".YOKYBBYKOY.", ".YKOY.BYOKY.", "..YY..B.YY..", "............"] },
  mandrake: { pal: { L: "#3f8f3a", G: "#7ccf5a", R: "#a8743f", S: "#d9a86c", E: "#1b1428", M: "#5a1e1e" }, rows: [   // Mandrake
    "..G..GG..G..", "..GL.LL.LG..", "...LLGGLL...", ".....LL.....", "....RSSR....", "...RSSSSR...",
    "...SESSES...", "...SSMMSS...", "...RSMMSR...", "..R.RSSR.R..", ".R..R..R..R.", "R...R..R...R"] },
  shade_hound: { pal: { K: "#1c1426", P: "#4b3570", V: "#a98ae0", E: "#c6ff4d", T: "#f0ecf8" }, rows: [   // Shade Hound
    "............", "........V.V.", "........PVP.", ".......VPEPV", "V......PPPPT", ".V....VPPKK.",
    "..VVVVPPPV..", "..VPPPPPPV..", "..VPKPPKPV..", "..VP.VV.PV..", "..V..V..V...", ".KK.KK.KK..."] },
};
// Apex forms (specs/15 section 5.3): the species' art with a palette swap and an overlay (non-"." pixels replace the base).
const BLANK = "............";
const APEX_ART = {
  living_ocean: { pal: { A: "#0b6e6e", B: "#2ec4b6", W: "#e0fffb", C: "#7fd6ff", K: "#ffffff", S: "#e0fffb" },   // Solaris Tide
    over: ["..S.S..S.S..", ".SSS.SS.SSS."] },
  abyssal_bloom: { pal: { A: "#2a0f4a", B: "#4b1d7a", W: "#ff7ad9", E: "#ffe0f5", C: "#ff3fa4", K: "#ffd1f0", T: "#ff7ad9" },   // Abyssal Siphonophore
    over: ["...T..T..T..", "..T.T.T..T.."] },
  unbreakable_will: { pal: { P: "#5a4a2a", M: "#ffd36b", O: "#ff4d1a", H: "#fff2b0", G: "#ffd36b" },   // Aegis Wyrm
    over: ["......G.G.G."] },
  inferno_sovereign: { pal: { P: "#2a2038", M: "#7a6a96", R: "#2a1a2e", O: "#b366ff", E: "#d9b3ff", H: "#b366ff", F: "#7a3cff" },   // Surtr Drake
    over: ["......F.F.F.", ".....F......"] },
  starlit_ascension: { pal: { Y: "#8fd1ff", W: "#ffffff", O: "#4f7dff", K: "#1b2a6b", B: "#dfe8ff", A: "#bcd4ff", S: "#fff6b0" },   // Comet Moth
    over: [BLANK, BLANK, BLANK, BLANK, BLANK, BLANK, BLANK, BLANK, BLANK, BLANK, BLANK, ".S.S....S.S."] },
  arcane_overflow: { pal: { Y: "#b388ff", W: "#f3e8ff", O: "#ff7ad9", K: "#2a1050", B: "#d9c2ff", S: "#e8d6ff" },   // Gemini Moth
    over: [BLANK, BLANK, BLANK, BLANK, BLANK, BLANK, BLANK, BLANK, BLANK, BLANK, BLANK, "..S..SS..S.."] },
  world_root: { pal: { L: "#2f6b2a", G: "#a6e070", R: "#6b4423", S: "#8a5a32", M: "#2a1208", F: "#f2c94c" },   // Worldroot Colossus
    over: ["F.G..GG..G.F", ".FGL.LL.LGF."] },
  mandragora_scream: { pal: { L: "#7a2f6b", G: "#e070c0", R: "#7a3f5a", S: "#e8b0c8", M: "#ff3f6b", E: "#ffffff", F: "#ff9ad5" },   // Mandragora Queen
    over: ["F...F..F...F", "..F......F.."] },
  umbral_veil: { pal: { K: "#0b0812", P: "#1c1426", V: "#5a4a7a", E: "#9fe8ff", T: "#9fe8ff", G: "#3a3050" },   // Grave Shuck
    over: ["G.G.........", ".G.G........"] },
  night_feast: { pal: { K: "#2a0a10", P: "#3a1428", V: "#ff5a6b", E: "#ffcf3f", T: "#ffcf3f", O: "#ffcf3f" },   // Eclipse Hound
    over: ["........OOO.", ".......O...O"] },
};
// The species' hand-drawn art, in its apex form when apex (a chase tree id) has one; null for a species without art.
function speciesSprite(id, apex) {
  const a = SPECIES_ART[id]; if (!a) return null;
  const x = apex ? APEX_ART[apex] : null;
  const key = "sp:" + id + (x ? ":" + apex : ""); if (SPR[key]) return SPR[key];
  const g = a.rows.map(r => r.split(""));
  if (x) x.over.forEach((row, y) => row.split("").forEach((c, xx) => { if (c !== ".") g[y][xx] = c; }));
  return (SPR[key] = paint(g, x ? Object.assign({}, a.pal, x.pal) : a.pal));
}
function paint(g, pal) {
  const c = document.createElement("canvas"); c.width = g[0].length; c.height = g.length;
  const x = c.getContext("2d");
  g.forEach((row, yy) => row.forEach((ch, xx) => { if (ch !== "." && pal[ch]) { x.fillStyle = pal[ch]; x.fillRect(xx, yy, 1, 1); } }));
  return c;
}
  const ENEMY_NAMES = ["Slime","Goblin","Wolf","Skeleton","Bandit","Orc","Wraith","Golem","Harpy","Ogre","Lich","Drake","Wyrm","Titan"];
  function enemyName(stage, boss) { const n = ENEMY_NAMES[Math.floor(stage / 10) % ENEMY_NAMES.length]; return boss ? n + " Warlord" : n; }
  // Draws a sprite into a canvas element sized in CSS; the canvas keeps a 12x12 backing store.
  function draw(canvas, sprite) {
    if (canvas.width !== 12) { canvas.width = 12; canvas.height = 12; }
    const x = canvas.getContext("2d"); x.imageSmoothingEnabled = false; x.clearRect(0, 0, 12, 12); x.drawImage(sprite, 0, 0);
  }
  root.IdleSprites = {
    hero: heroSprite,
    species: speciesSprite,
    player: playerSprite,
    enemy: function (stage, boss, variant) { return enemySprite(stage * 31 + (boss ? 999 : (variant || 0) % 4), boss); },
    // A rank opponent (specs/14 section 5): its own shape from seed, the boss horns at the top rank, tinted by colour (#RRGGBB).
    monster: function (seed, boss, color) { return enemySprite(seed, boss, color ? hueOf(color) : undefined); },
    enemyName: enemyName,
    draw: draw,
  };
})(typeof self !== 'undefined' ? self : this);
