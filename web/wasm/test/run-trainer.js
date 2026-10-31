#!/usr/bin/env node
// End-to-end browser check of the monster-trainer page (web/index.html) on the C# (wasm) engine at 390x844: picks a species,
// opens a stat and an ability tooltip, plays a whole run through the real buttons (drill, rest, rank fights, tree drafts, buying
// nodes), watches two rank fights play back (current ability, live HP, skill cooldown tiles, the remembered 1x/2x/Skip speed),
// checks the skill point gain indicators (HUD float-up, fight badge, Trees tab count, log lines), seals it, checks the grade
// screen (after the gauntlet wheel) and Vault list, reloads mid-run and after the end to prove the replay save restores the same run,
// locks and dismantles a Vault entry, loads a v1 save over the Vault cap to check the migration and the blocked run start, and
// browses a varied Vault (sort, filter, search, detail, compare; lock and dismantle under a filter hit the real Vault index).
// Also checks the header build stamp from dist/build-info.json, and that a missing stamp shows "dev".
// Fails (exit 1) on any page error, console error (Google Fonts aborts excluded) or failed assertion.
// Usage: node web/wasm/test/run-trainer.js [--shots dir]
'use strict';
const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');
const { start } = require('./serve.js');

const WEB = path.resolve(__dirname, '..', '..');
const shotsDir = process.argv.includes('--shots') ? process.argv[process.argv.indexOf('--shots') + 1] : null;
const fails = [], errors = [];
const check = (ok, what) => { console.log((ok ? 'ok   ' : 'FAIL ') + what); if (!ok) fails.push(what); };

(async () => {
  if (!fs.existsSync(path.join(WEB, 'wasm', 'dist', '_framework', 'dotnet.js'))) { console.error('web/wasm/dist is missing: run web/wasm/build.sh first'); process.exit(2); }
  const srv = await start(WEB, 0);
  const browser = await chromium.launch();
  const ctx = await browser.newContext({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true });
  await ctx.route(/fonts\.(googleapis|gstatic)\.com/, (r) => r.abort());
  const page = await ctx.newPage();
  page.on('pageerror', (e) => errors.push('pageerror: ' + e.message));
  page.on('console', (m) => {
    if (m.type() === 'error' && !/fonts\.g|ERR_FAILED|Failed to load resource/.test(m.text() + (m.location().url || ''))) errors.push('console.error: ' + m.text());
  });
  const shot = async (n) => { if (shotsDir) { fs.mkdirSync(shotsDir, { recursive: true }); await page.screenshot({ path: path.join(shotsDir, n + '.png') }); } };
  const act = (a) => page.locator('[data-act="' + a + '"]');
  const text = (sel) => page.locator(sel).first().innerText();
  const tc = (sel) => page.locator(sel).first().textContent();   // as written, before CSS text-transform
  const url = srv.url + 'index.html';
  // The account save is AccountSave text: a header line, then JSON.
  const readAccount = () => page.evaluate(() => { const t = localStorage.getItem('idle-rng-account-v1'); return t ? JSON.parse(t.slice(t.indexOf('\n') + 1)) : null; });

  // ---- home screen
  await page.goto(url);
  await act('start').waitFor({ timeout: 90000 });
  check((await page.locator('[data-act^="species-"]').count()) >= 3, 'home lists the species from the C# content');
  check(await act('start').isEnabled(), 'a species is pre-picked');
  await page.waitForFunction(() => document.getElementById('build').textContent !== '');
  const stamp = await page.locator('#build').textContent();
  check(/^[0-9a-f]{4,}(\+dirty)? · \d{4}-\d\d-\d\d$/.test(stamp), 'the header shows the build stamp: ' + stamp);
  await shot('01-home');

  // ---- start a run, first drill and rest
  await act('species-drake').click();
  await act('start').click();
  await act('drill-str').waitFor();
  check(/Cycle 1\/12/.test(await text('[data-id=cycle]')), 'HUD shows cycle 1/12');
  check(/Rank E/.test(await text('[data-id=rank]')), 'starts at rank E');
  check((await page.locator('[data-act^="drill-"]').count()) === 8, 'eight stats can be drilled');
  const odds0 = await text('[data-id=odds]');
  check(/fail \d+%/.test(odds0) && /injury \d+%/.test(odds0), 'drill odds shown: ' + odds0.slice(0, 60));
  await act('drill-str').click();
  check(/Drill|Fail|Success|Super/.test(await text('[data-id=result]')), 'a drill result appears');
  await act('rest').click();
  check(/Rest/.test(await text('[data-id=result]')), 'a rest result appears');
  await shot('02-train');

  // ---- mid-run reload restores the same run (replay save)
  const before = await page.evaluate(() => ({ cycle: document.querySelector('[data-id=cycle]').textContent, bars: [...document.querySelectorAll('.stat .bar span')].map((e) => e.textContent).join('|') }));
  const saved = await readAccount();
  check(saved && saved.Kind === 'monster_account', 'the save is the C# account save');
  check(saved && saved.ActiveRun && saved.ActiveRun.SpeciesId === 'drake' && saved.ActiveRun.Log.length === 2, 'the active run is stored as species + seed + command log (' + (saved && saved.ActiveRun && saved.ActiveRun.Log.length) + ' commands)');
  await page.reload();
  await act('drill-str').waitFor({ timeout: 90000 });
  const after = await page.evaluate(() => ({ cycle: document.querySelector('[data-id=cycle]').textContent, bars: [...document.querySelectorAll('.stat .bar span')].map((e) => e.textContent).join('|') }));
  check(JSON.stringify(before) === JSON.stringify(after), 'reload restores cycle and stats exactly');

  // ---- tooltips (specs/16 section 9): a stat and an ability, from the engine's text, inside a 390px screen
  const tip = page.locator('#tip');
  const fits = async () => { const b = await tip.boundingBox(); return !!b && b.x >= 0 && b.y >= 0 && b.x + b.width <= 390 && b.y + b.height <= 844; };
  const strLabel = page.locator('[data-tip="stat:str"]').first();
  await strLabel.click();
  check(await tip.isVisible(), 'tapping a stat opens its tooltip');
  const strTip = await tip.innerText();
  check(/Strength/.test(strTip) && /Basic attack: \d+ damage/.test(strTip) && !/[{}]/.test(strTip), 'the STR tooltip explains the stat with the engine\'s numbers: ' + strTip.replace(/\s+/g, ' ').slice(0, 70));
  check((await tip.getAttribute('role')) === 'tooltip' && (await strLabel.getAttribute('aria-describedby')) === 'tip' && (await strLabel.getAttribute('aria-expanded')) === 'true', 'the open trigger is aria-described by the role=tooltip box');
  check(await fits(), 'the stat tooltip fits the screen');
  await shot('02b-stat-tip');
  await page.locator('[data-id=odds]').click();
  check(!(await tip.isVisible()) && (await strLabel.getAttribute('aria-describedby')) === null, 'tapping elsewhere closes it');

  // ---- trees tab
  await act('tab-trees').click();
  check((await page.locator('[data-id^="slot-"]').count()) === 2, 'two native trees slotted at the start');
  check(/SP/.test(await text('[data-id=sp2]')), 'skill points shown');
  await shot('03-trees');
  await page.locator('[data-tip="node:pyromancy_n4"]').click();
  const abTip = await tip.innerText();
  check(/Flame Breath/.test(abTip) && /90% of STR/.test(abTip) && /once every 4/.test(abTip) && /Physical/.test(abTip) && /2 SP/.test(abTip) && !/[{}]/.test(abTip),
    'an ability tooltip shows effect, scaling, cooldown, tags and cost: ' + abTip.replace(/\s+/g, ' ').slice(0, 70));
  check(await fits(), 'the ability tooltip fits the screen');
  await shot('03b-ability-tip');
  await page.keyboard.press('Escape');
  check(!(await tip.isVisible()), 'Escape closes it');
  await act('tab-train').click();

  // ---- play through the buttons. stop: 'fight' returns when a rank fight is due, 'skill' when one is due and slot 1 has bought
  // its skill node (node 5), 'end' at the seal button. A fight still playing back is skipped.
  let played = { fights: 0, drafts: 0, bought: 0, takes: 0, steps: 0, slot0: 0, skillRows: 0 };
  const playUntil = async (stop) => {
    played = await page.evaluate(([stop, stats]) => {
      const q = (s) => document.querySelector(s);
      const click = (s) => { const e = q(s); if (!e || e.disabled) return false; e.click(); return true; };
      for (let i = 0; i < 1500 && !q('[data-act="seal"]'); i++) {
        stats.steps++;
        if (q('[data-id=fight-play]')) { click('[data-act="speed-skip"]'); continue; }
        if (q('[data-act="fight-continue"]')) stats.skillRows = Math.max(stats.skillRows, document.querySelectorAll('[data-id^="skill-0-"]').length);
        if (click('[data-act="fight-continue"]')) continue;
        if (q('[data-id=offer-title]')) {
          stats.drafts++;
          if (click('[data-act^="take-0-2"]') || click('[data-act^="take-0-3"]') || click('[data-act^="take-0-1"]')) stats.takes++; else click('[data-act="discard"]');
          continue;
        }
        const f = q('[data-act="fight"]');
        if (f && !f.disabled && (stop === 'fight' || (stop === 'skill' && stats.slot0 >= 5))) return stats;
        if (click('[data-act="fight"]')) { stats.fights++; continue; }
        click('[data-act="tab-trees"]');
        while (click('[data-act^="buy-"]')) stats.bought++;
        stats.slot0 = +((/(\d+)\//.exec((q('[data-id=slot-0] .num') || {}).textContent || '') || [])[1] || 0);
        click('[data-act="tab-train"]');
        const m = /Energy (\d+)/.exec((q('.bar.energy span') || {}).textContent || '');
        const energy = m ? +m[1] : 100;
        if (energy < 45) click('[data-act="rest"]');
        else click('[data-act="drill-' + ['str', 'hp', 'def', 'spd', 'int', 'wis', 'dex', 'luk'][stats.steps % 8] + '"]');
      }
      return stats;
    }, [stop, played]);
  };
  const speedSaved = () => page.evaluate(() => localStorage.getItem('idle-rng-fight-speed'));

  // ---- the first rank fight plays back action by action (issue #79), then the speed toggle
  await playUntil('fight');
  check(await act('fight').isEnabled(), 'a rank fight is due after ' + played.steps + ' steps');
  check((await act('speed-1').getAttribute('aria-pressed')) === 'true', 'fight speed defaults to 1x');
  await act('fight').click();
  check(await page.locator('[data-id=fight-play]').isVisible(), 'the fight plays back instead of showing the result at once');
  check((await act('fight-continue').count()) === 0, 'no result while it plays');
  await page.waitForFunction(() => /\S/.test((document.querySelector('[data-id=fight-now] small') || {}).textContent || ''), null, { timeout: 5000 });
  const now1 = await text('[data-id=fight-now]');
  check(/\S/.test(await text('[data-id=fight-now] b')) && !/Fight!/.test(now1), 'the current action is shown: ' + now1.replace(/\s+/g, ' '));
  check((await page.locator('[data-id^="unit-"] .bar').count()) === 2, 'both units have live HP bars');
  check(/^Action \d+ of \d+$/.test(await text('[data-id=fight-progress]')), 'playback progress: ' + (await text('[data-id=fight-progress]')));
  await shot('04a-fight-play');
  await act('speed-2').click();
  check((await speedSaved()) === '2' && (await act('speed-2').getAttribute('aria-pressed')) === 'true', '2x is selected and remembered');
  await act('speed-skip').click();
  check(await page.locator('[data-id=fight-title]').isVisible(), 'Skip jumps to the result');
  check((await speedSaved()) === 'skip', 'Skip is remembered');
  check((await page.locator('[data-id^="unit-"] .bar').count()) === 2, 'the result shows both units\' final HP');
  // ---- skill point gains (issue #86): the fight starts the next cycle, so it pays SP_PER_CYCLE (and SP_PER_WIN if won)
  const badge = await text('[data-id=fight-sp]');
  check(/^\+\d+ SP/.test(badge) && /skill points: new cycle/.test(badge), 'the fight result shows its SP reward as a badge: ' + badge.replace(/\s+/g, ' '));
  await act('fight-continue').click();
  const gain = await page.evaluate(() => [...document.querySelectorAll('[data-id=sp-gain]')].map((e) => e.textContent));
  check(gain.some((g) => /^\+\d+ SP \(new cycle\)$/.test(g)), 'closing it floats the gain up by the HUD SP count: ' + gain.join(', '));
  check(await page.locator('[data-id=sp].pulse').count() === 1, 'and the SP count pulses');
  await page.waitForTimeout(400);
  await shot('04-sp-gain');
  const spNow = +(/^(\d+) SP/.exec(await text('[data-id=sp]')) || [])[1];
  check(spNow > 0 && (await text('[data-act=tab-trees]')).replace(/\s+/g, ' ') === 'Trees · ' + spNow, 'the Trees tab shows the unspent SP: ' + (await text('[data-act=tab-trees]')));
  await act('tab-log').click();
  const feed = await text('[data-id=feed]');
  check(/Cycle \d+: \+\d+ SP \(new cycle\)/.test(feed), 'the log has a line per SP gain');   // the session log restarts on the reload above
  await act('tab-train').click();

  // ---- a fight with a bought skill: its row shows which ability is used and its cooldown
  await playUntil('skill');
  check(played.slot0 >= 5, 'slot 1 has its skill node before a later fight (' + played.slot0 + ' nodes, ' + played.fights + ' fights)');
  await act('speed-2').click();
  await act('fight').click();
  check((await page.locator('[data-id^="skill-0-"]').count()) >= 1, 'the monster\'s skills are listed during the fight');
  const used = await (await page.waitForFunction(() => {
    const u = document.querySelector('.skill.used');
    return u && { id: u.dataset.id, name: u.querySelector('.nm').textContent, st: u.querySelector('.st').textContent, title: u.title, fill: u.querySelector('.cdfill').style.height,
      now: document.querySelector('[data-id=fight-now] b').textContent };
  }, null, { timeout: 15000 })).jsonValue();
  check(used.name === used.now, 'the skill in use is highlighted and named in the banner: ' + used.now);
  const cdLen = +((/cooldown (\d+)/.exec(used.title) || [])[1]);
  check(used.st === (cdLen ? String(cdLen) : ''), 'a used skill\'s tile shows its full cooldown (' + cdLen + '): ' + used.st);
  check(used.fill === (cdLen ? '100%' : '0%'), 'its grey overlay covers the whole tile: ' + used.fill);
  await shot('04b-fight-skill');
  if (cdLen > 1) {
    const fill2 = await (await page.waitForFunction((id) => {
      const u = document.querySelector('[data-id="' + id + '"]'), hgt = parseFloat(u.querySelector('.cdfill').style.height);
      return hgt < 100 && hgt > 0 && { hgt: hgt, st: u.querySelector('.st').textContent };
    }, used.id, { timeout: 15000 })).jsonValue();
    check(+fill2.st > 0 && +fill2.st < cdLen && Math.abs(fill2.hgt - (fill2.st / cdLen) * 100) < 0.01, 'as the cooldown runs down the overlay recedes (' + fill2.st + ' left, ' + fill2.hgt.toFixed(0) + '% high)');
  }
  await act('fight-continue').waitFor({ timeout: 60000 });                 // plays to the end at 2x
  check((await page.locator('[data-id^="skill-0-"]').count()) >= 1, 'the result keeps the skill tiles with their final cooldowns');
  await shot('04c-fight-result');
  await act('fight-continue').click();

  await playUntil('end');                                                  // later fights: the loop taps Skip once, which sticks
  console.log('played: ' + JSON.stringify(played));
  check(played.fights >= 6, 'rank fights happened (' + played.fights + ')');
  check(played.bought >= 3, 'tree nodes were bought (' + played.bought + ')');
  if (await act('fight-continue').count()) await act('fight-continue').click();   // the last fight's result popup
  check(await act('seal').isVisible(), 'the run reaches its end and offers to seal');
  await shot('04-end');

  // ---- seal -> gauntlet wheel -> grade screen -> vault
  await act('seal').click();
  await page.locator('[data-id=climb-reel]').waitFor();
  check(await act('climb-skip').isVisible(), 'sealing spins the gauntlet wheel, with a skip button');
  await page.waitForTimeout(700);
  await shot('05-wheel');
  check(/^Floor \d+/.test(await text('[data-id=climb-now]')), 'the wheel shows the floor under the pointer: ' + (await text('[data-id=climb-now]')));
  await page.locator('[data-id=climb-reel]').tap();                // tap the wheel to skip it
  const legacy = await text('[data-id=climb-title]');
  check(/^(Floor \d+|All \d+ floors): \S/.test(legacy), 'tapping lands the wheel on the Legacy Title: ' + legacy);
  check((await act('climb-skip').count()) === 0 && (await page.locator('[data-id=climb-arena]').count()) === 1, 'the skip button goes and the defeat plays');
  await page.waitForTimeout(1600);
  await shot('05b-wheel-landed');
  const sealedAcct = await readAccount();
  const sealed = sealedAcct && sealedAcct.Account.Vault[0];
  check(sealed && sealed.LegacyTitle && legacy.startsWith(sealed.GauntletCleared ? 'All ' : 'Floor ' + sealed.GauntletFloor + ':'), 'the title matches the floor stored on the Vault entry (' + (sealed && sealed.GauntletFloor) + ')');
  check(sealed && (await page.locator('[data-id=climb-reel]').getAttribute('data-hash')) === sealed.GauntletHash, 'the wheel\'s replayed climb hashes the same as the sealed gauntlet');
  const fell = page.locator('.floor.landed');
  check((await fell.count()) === 1 && ((sealed && sealed.GauntletCleared) || /✗/.test(await fell.innerText())), 'the landed row marks the floor that beat it');
  await act('climb-continue').click();
  const grade = await text('[data-id=grade]');
  check(/^[A-Z]$/.test(grade), 'grade screen shows the rank reached: ' + grade);
  const acct = await readAccount();
  const entry = acct && acct.Account.Vault[0];
  check(acct && acct.Account.Vault.length === 1 && entry.SpeciesId === 'drake' && entry.Log.length > 20 && acct.ActiveRun === null, 'sealing stored a Vault entry with the run log and cleared the active run');
  check(entry && entry.Grade === grade.toLowerCase() && /^[0-9a-f]{64}$/.test(entry.RunHash), 'entry grade and run hash match');
  const payout = +(/\+(\d+) Training Shards/.exec(await text('[data-id=payout]')) || [])[1];
  check(payout > 0 && acct.Account.Shards === payout, 'the run paid ' + payout + ' shards into the account');
  check((await text('[data-id=grade-title]')) === legacy, 'the grade screen shows the Legacy Title');
  await shot('05c-grade');

  // ---- reduced motion: the wheel replays already landed, no spin
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await act('climb-replay').click();
  check((await act('climb-skip').count()) === 0 && (await text('[data-id=climb-title]')) === legacy, 'with reduced motion the wheel lands at once');
  await act('climb-continue').click();
  await page.emulateMedia({ reducedMotion: 'no-preference' });
  check(/^[A-Z]$/.test(await text('[data-id=grade]')), 'and continues to the grade screen');

  await page.reload();
  await act('start').waitFor({ timeout: 90000 });
  check((await page.locator('[data-id=vault-entry]').count()) === 1, 'a reload after sealing keeps one Vault entry (no double seal)');
  check((await text('[data-id=shards]')).startsWith(payout + ' '), 'and the shards: ' + (await text('[data-id=shards]')));
  check((await text('[data-id=vault-title]')) === legacy, 'the Vault card shows the Legacy Title: ' + legacy);
  await shot('06-vault');

  // ---- talents: buy one, and it shapes the next run
  const talent = page.locator('[data-act="talent-frugal_drills"]');
  check(await talent.isEnabled(), 'the first talent is affordable with the payout');
  const cost = +(/(\d+) shards/.exec(await talent.innerText()) || [])[1];
  await talent.click();
  check(/^(\d+) shards/.exec(await text('[data-id=shards]'))[1] == payout - cost, 'buying it spent ' + cost + ' shards');
  check(/1\/5/.test(await text('[data-id=talent-frugal_drills]')), 'the talent shows rank 1');
  await act('start').click();
  await act('drill-str').waitFor();
  check(/costs 19 energy/.test(await text('[data-id=odds]')), 'the talent cut the next run\'s drill cost: ' + (await text('[data-id=odds]')).slice(-26));
  await act('tab-log').click();
  await act('abandon').click();
  await act('abandon-no').click();
  check((await readAccount()).ActiveRun !== null, 'declining the abandon prompt keeps the run');
  await act('abandon').click();
  await act('abandon-yes').click();
  await act('start').waitFor();
  check(((await readAccount()).ActiveRun) === null, 'abandoning ends the run without sealing');
  check((await page.locator('[data-id=vault-entry]').count()) === 1, 'and the Vault is unchanged');

  // ---- Vault cap, lock and dismantle (issue #82, specs/18 section 7)
  const cap = +(/^Vault 1\/(\d+)$/.exec(await tc('[data-id=vault-count]')) || [])[1];
  check(cap > 1,'the Vault shows its count against the cap: ' + (await tc('[data-id=vault-count]')));
  await act('lock-0').click();
  check((await act('lock-0').getAttribute('aria-pressed')) === 'true' && await act('dismantle-0').isDisabled(), 'locking an entry disables its dismantle button');
  check((await readAccount()).Account.Vault[0].Locked === true, 'the lock is saved');
  await act('lock-0').click();
  check((await act('lock-0').getAttribute('aria-pressed')) === 'false' && await act('dismantle-0').isEnabled(), 'unlocking enables it again');
  await act('dismantle-0').click();
  check(await page.locator('[data-id=dismantle-confirm]').isVisible(), 'dismantling asks for confirmation first');
  await shot('08-dismantle-confirm');
  await act('dismantle-no').click();
  check((await page.locator('[data-id=vault-entry]').count()) === 1 && (await page.locator('[data-id=dismantle-confirm]').count()) === 0, 'declining keeps the monster');

  // Over the cap: a v1 save (written before the lock existed) whose Vault holds cap + 1 copies of the sealed entry. Loading it
  // migrates it to v2; the run start is blocked until one is dismantled.
  const v2 = await readAccount();
  const v1 = JSON.parse(JSON.stringify(v2));
  v1.SchemaVersion = 1; v1.Account.SchemaVersion = 1;
  const one = v1.Account.Vault[0];
  delete one.Locked;
  v1.Account.Vault = Array.from({ length: cap + 1 }, () => one);
  const v1Body = Buffer.from(JSON.stringify(v1), 'utf8');
  const v1Text = 'IDLERNG-SAVE fmt=json crc32=' + (require('zlib').crc32(v1Body) >>> 0).toString(16).padStart(8, '0') + ' len=' + v1Body.length + '\n' + v1Body.toString('utf8');
  await page.evaluate((t) => localStorage.setItem('idle-rng-account-v1', t), v1Text);
  await page.reload();
  await act('start').waitFor({ timeout: 90000 });
  check((await page.evaluate(() => RunEngine.badKeys().length)) === 0, 'a v1 save loads (migrated), not kept aside');
  check((await page.locator('[data-id=vault-entry]').count()) === cap + 1, 'all ' + (cap + 1) + ' monsters are in the Vault');
  check((await tc('[data-id=vault-count]')) === 'Vault ' + (cap + 1) + '/' + cap, 'the count shows it over the cap');
  check(await act('start').isDisabled() && /dismantle 1/i.test(await act('start').innerText()), 'starting a run is blocked: ' + (await act('start').innerText()));
  check(/Dismantle 1 monster/.test(await text('[data-id=vault-over]')), 'with a clear message: ' + (await text('[data-id=vault-over]')));
  await shot('09-vault-over-cap');
  const shardsBefore = (await readAccount()).Account.Shards;
  const refund = +(/\+(\d+) shards/.exec(await act('dismantle-' + cap).innerText()) || [])[1];
  await act('dismantle-' + cap).click();
  await act('dismantle-yes').click();
  const afterDismantle = await readAccount();
  check(afterDismantle.SchemaVersion === 2 && afterDismantle.Account.Vault.length === cap && afterDismantle.Account.Vault.every((e) => e.Locked === false), 'dismantling saved the account as v2 with ' + cap + ' unlocked entries');
  check(refund > 0 && afterDismantle.Account.Shards === shardsBefore + refund, 'dismantling refunded ' + refund + ' shards by grade');
  check((await tc('[data-id=vault-count]')) === 'Vault ' + cap + '/' + cap && (await page.locator('[data-id=vault-over]').count()) === 0, 'back at the cap, the warning goes');
  check(await act('start').isEnabled(), 'and a run can start again at the cap');
  const vw = await page.evaluate(() => document.documentElement.scrollWidth);
  check(vw <= 390, 'the Vault fits the phone width (' + vw + 'px)');

  // ---- Vault browsing (issue #81): sort, filter, search, detail and compare over a varied Vault, and lock/dismantle under a
  // filter still address the real Vault index. The six entries are copies of the sealed one with species, grade, title, STR,
  // date and lock rewritten (the account view reads these stored fields; nothing replays them here).
  {
    const base = await readAccount();
    const tpl = base.Account.Vault[0];
    const strKey = Object.keys(tpl.Stats).find((k) => k.toLowerCase() === 'str');
    const mk = (species, grade, rank, floor, title, str, locked, i) => Object.assign(JSON.parse(JSON.stringify(tpl)), {
      SpeciesId: species, Grade: grade, RankIndex: rank, GauntletFloor: floor, GauntletCleared: floor > 100, LegacyTitle: title,
      GauntletKiller: floor > 100 || !title ? null : tpl.GauntletKiller, Locked: locked, SealedUtcMs: 1790000000000 + i * 86400000,
      Stats: Object.assign({}, tpl.Stats, { [strKey]: str }) });
    base.Account.Vault = [
      mk('slime', 'c', 2, 30, 'title.respectable_brawler', 100, false, 0),
      mk('drake', 'a', 4, 70, 'title.food', 300, false, 1),
      mk('sprite', 'e', 0, 0, null, 50, false, 2),
      mk('slime', 'b', 3, 50, 'title.professional_punching_bag', 200, true, 3),
      mk('drake', 'd', 1, 12, 'title.warm_up_act', 150, false, 4),
      mk('sprite', 's', 5, 101, 'title.tower_breaker', 400, false, 5),
    ];
    base.Account.Vault[1].Trees[0].Bought = 3;                         // a part-bought tree for the detail sheet
    const body = Buffer.from(JSON.stringify(base), 'utf8');
    const saveTxt = 'IDLERNG-SAVE fmt=json crc32=' + (require('zlib').crc32(body) >>> 0).toString(16).padStart(8, '0') + ' len=' + body.length + '\n' + body.toString('utf8');
    await page.evaluate((t) => { localStorage.setItem('idle-rng-account-v1', t); localStorage.removeItem('idle-rng-vault-view'); }, saveTxt);
    await page.reload();
    await act('start').waitFor({ timeout: 90000 });
    const order = () => page.$$eval('[data-id=vault-entry]', (els) => els.map((e) => +e.dataset.index));
    const same = (a, b) => JSON.stringify(a) === JSON.stringify(b);
    const sortBy = async (v) => { await page.selectOption('[data-act=vault-sort]', v); return order(); };
    check(same(await order(), [5, 4, 3, 2, 1, 0]), 'the Vault lists newest first by default');
    check(same(await sortBy('grade'), [5, 1, 3, 0, 4, 2]), 'sort by grade, best first');
    check(same(await sortBy('floor'), [5, 1, 3, 0, 4, 2]), 'sort by gauntlet floor, a cleared tower on top and no title last');
    check(same(await sortBy('species'), [1, 4, 3, 0, 5, 2]), 'sort by species A to Z, best grade first within one');
    check(same(await sortBy('old'), [0, 1, 2, 3, 4, 5]), 'sort oldest first');
    check(same(await sortBy('stat:str'), [5, 1, 3, 4, 0, 2]), 'sort by a chosen stat (STR)');
    check(JSON.parse(await page.evaluate(() => localStorage.getItem('idle-rng-vault-view'))).sort === 'stat:str', 'the sort is remembered for this viewer');
    await page.reload();
    await act('start').waitFor({ timeout: 90000 });
    check((await page.locator('[data-act=vault-sort]').inputValue()) === 'stat:str' && same(await order(), [5, 1, 3, 4, 0, 2]), 'and survives a reload');

    await act('vault-filters').click();
    check(await page.locator('[data-id=vault-filter-panel]').isVisible(), 'the Filters button opens the filter panel');
    await page.selectOption('[data-act=vault-species]', 'slime');
    check(same(await order(), [3, 0]), 'filter by species');
    check((await text('[data-id=vault-showing]')).startsWith('Showing 2 of 6') && /Filters · 1/.test(await act('vault-filters').innerText()), 'it says how many it shows and counts the active filters');
    await page.selectOption('[data-act=vault-species]', '');
    await page.selectOption('[data-act=vault-element]', 'light');
    check(same(await order(), [5, 2]), 'filter by element (Sprite is light)');
    await page.selectOption('[data-act=vault-element]', '');
    await page.selectOption('[data-act=vault-title]', { label: 'No title' });
    check(same(await order(), [2]), 'filter: no Legacy Title');
    await page.selectOption('[data-act=vault-title]', { label: 'Has a title' });
    check(same(await order(), [5, 1, 3, 4, 0]), 'filter: has a Legacy Title');
    await page.selectOption('[data-act=vault-title]', { label: 'Respectable Brawler' });
    check(same(await order(), [0]), 'filter: one specific Legacy Title');
    await page.selectOption('[data-act=vault-title]', '');
    await page.selectOption('[data-act=vault-lock]', 'locked');
    check(same(await order(), [3]), 'filter: locked only');
    await page.selectOption('[data-act=vault-lock]', 'unlocked');
    check(same(await order(), [5, 1, 4, 0, 2]), 'filter: unlocked only');
    await page.selectOption('[data-act=vault-lock]', '');
    await shot('10-vault-filters');

    const search = page.locator('[data-act=vault-search]');
    await search.fill('brawler');
    check(same(await order(), [0]), 'search finds a monster by its Legacy Title');
    await search.fill('tower breaker');
    check(same(await order(), [5]), 'every word of a search must match');
    await search.fill('drake');
    check(same(await order(), [1, 4]), 'search by species name');
    check(await search.evaluate((el) => el === document.activeElement), 'typing a search keeps the focus in the box');
    await search.fill('zzz');
    check((await page.locator('[data-id=vault-empty]').count()) === 1, 'a search with no match says so');
    await act('vault-clear').click();
    check(same(await order(), [5, 1, 3, 4, 0, 2]) && (await search.inputValue()) === '', 'Clear filters shows the whole Vault again, in the chosen sort');

    // Lock and dismantle under a filter address the real Vault index, not the position in the filtered list.
    await page.selectOption('[data-act=vault-species]', 'sprite');
    await page.selectOption('[data-act=vault-sort]', 'new');
    check(same(await order(), [5, 2]), 'filtered to the two Sprites');
    const second = page.locator('[data-id=vault-entry]').nth(1);
    check((await second.locator('[data-act^="lock-"]').getAttribute('data-act')) === 'lock-2', 'the second filtered card\'s lock button names Vault index 2');
    await second.locator('[data-act^="lock-"]').click();
    const locks = (await readAccount()).Account.Vault.map((e) => e.Locked);
    check(same(locks, [false, false, true, true, false, false]), 'locking it under the filter locked Vault entry 2 only: ' + locks.join(','));
    await page.selectOption('[data-act=vault-species]', 'drake');
    check(same(await order(), [4, 1]), 'filtered to the two Drakes');
    const shards0 = (await readAccount()).Account.Shards;
    const first = page.locator('[data-id=vault-entry]').first();
    const refundD = +(/\+(\d+)/.exec(await first.locator('[data-act^="dismantle-"]').innerText()) || [])[1];
    await first.locator('[data-act="dismantle-4"]').click();
    check((await page.locator('[data-id=vault-entry]').first().locator('[data-id=dismantle-confirm]').count()) === 1, 'the confirm opens on that card');
    await act('dismantle-yes').click();
    const left = (await readAccount()).Account;
    check(same(left.Vault.map((e) => e.SpeciesId + ':' + e.Grade), ['slime:c', 'drake:a', 'sprite:e', 'slime:b', 'sprite:s']), 'dismantling the first filtered card removed Vault entry 4 (the D Drake) and no other');
    check(left.Shards === shards0 + refundD, 'and paid its refund (' + refundD + ')');
    check(same(await order(), [1]) && (await page.locator('[data-act=vault-species]').inputValue()) === 'drake', 'the filter stays on after the dismantle');
    await act('vault-clear').click();

    // Detail of one entry: stats, slotted trees with the bought nodes, rank, title and date.
    await act('detail-1').click();
    check(await page.locator('[data-id=detail-title]').isVisible(), 'Details opens the detail sheet');
    const legacyLine = await text('[data-id=detail-legacy]');
    check(/^Floor 70: .*Food$/.test(legacyLine), 'it shows the floor and Legacy Title: ' + legacyLine);
    const statRows = page.locator('#modal [data-id=detail-stats] .stat');
    check((await statRows.count()) === 8 && (await page.locator('#modal [data-id=detail-stats]').innerText()).includes('300'), 'it shows all eight stats');
    check(/sealed /.test(await text('[data-id=detail-facts]')) && /Rank A/.test(await page.locator('#modal').innerText()), 'it shows the rank and seal date');
    const trees = left.Vault[1].Trees;
    let gotOk = (await page.locator('#modal [data-id^="detail-tree-"]').count()) === trees.length;
    for (let k = 0; k < trees.length; k++) gotOk = gotOk && (await page.locator('#modal [data-id="detail-tree-' + k + '"] .node.got').count()) === trees[k].Bought;
    check(gotOk && trees.length >= 2 && trees[0].Bought === 3, 'it lists each slotted tree with its bought nodes checked (' + trees.map((t) => t.Bought).join(', ') + ')');
    check((await page.locator('#modal [data-id="detail-tree-0"] .node.locked').count()) > 0, 'and the nodes not bought unchecked');
    const mw = await page.evaluate(() => { const m = document.getElementById('modal'); return m.scrollWidth - m.clientWidth; });
    check(mw <= 0, 'the detail sheet fits the phone width');
    await shot('11-vault-detail');
    await act('detail-close').click();
    check(await page.locator('#modal').isHidden(), 'Close returns to the Vault');

    // Compare two entries side by side.
    await act('compare-0').click();
    check(await page.locator('[data-id=compare-pick]').isVisible(), 'Compare on one monster asks for a second');
    await act('compare-4').click();                                    // after the dismantle, index 4 is the S Sprite
    check(await page.locator('[data-id=compare-grid]').isVisible(), 'Compare on a second opens the compare sheet');
    check((await text('[data-id=diff-str]')) === '+300' && (await text('[data-id=diff-rank]')) === '+3' && (await text('[data-id=diff-floor]')) === '+71', 'it shows B minus A per stat, rank and floor');
    check((await text('[data-id=diff-hp]')) === '=', 'an equal stat shows =');
    const cw = await page.evaluate(() => { const m = document.getElementById('modal'); return m.scrollWidth - m.clientWidth; });
    check(cw <= 0, 'the compare sheet fits the phone width');
    await shot('12-vault-compare');
    await act('compare-swap').click();
    check((await text('[data-id=diff-str]')) === '−300', 'Swap turns the diff around');
    await act('compare-close').click();
    await act('vault-filters').click();
    const fw = await page.evaluate(() => document.documentElement.scrollWidth);
    check(fw <= 390, 'the Vault with its filter panel open fits the phone width (' + fw + 'px)');
  }


  // ---- the book option and drafts (the test book is the picked species' own: drake's Ember Codex)
  await act('species-drake').click();
  check(/Ember Codex/.test(await text('[data-id=chase-drake]')), 'the species card names its chase trees and the book one needs');
  await act('grant-book').click();
  await act('book-toggle').check();
  await act('start').click();
  await act('tab-trees').click();
  check((await page.locator('[data-act^="book-"]:not([data-act="book-toggle"])').count()) === 1, 'a test skill book can be used');
  await page.locator('[data-act^="book-"]:not([data-act="book-toggle"])').first().click();
  check(/Skill book/.test(await text('[data-id=offer-title]')), 'using the book opens a draft');
  await shot('07-draft');
  await act('take-0-2').click();                                   // into the first empty slot
  check((await page.locator('[data-id=offer-title]').count()) === 0, 'taking the tree closes the draft');
  check((await page.locator('[data-id^="slot-"]').count()) === 3, 'the book tree now fills slot 3');
  check((await page.locator('[data-act^="book-"]:not([data-act="book-toggle"])').count()) === 0, 'the used book is gone');
  check((await page.locator('[data-id^="chasecard-"]').count()) === 2, 'the Trees tab lists the drake\'s own two chase trees');
  check(/Ember Codex/.test(await text('[data-id=chasecard-inferno_sovereign]')), 'the book-gated chase tree says which skill book it needs');

  // ---- without a build stamp (an older dist) the header says "dev" and nothing errors
  const bare = await ctx.newPage();
  bare.on('pageerror', (e) => errors.push('pageerror (no stamp): ' + e.message));
  await bare.route(/build-info\.json/, (r) => r.fulfill({ status: 404, body: '' }));
  await bare.goto(url);
  await bare.waitForFunction(() => document.getElementById('build').textContent !== '');
  check((await bare.locator('#build').textContent()) === 'dev', 'a missing build stamp shows "dev"');
  await bare.close();

  check(errors.length === 0, 'no page/console errors' + (errors.length ? ': ' + errors.join(' | ') : ''));
  await browser.close();
  await srv.close();
  if (fails.length) { console.log('\n' + fails.length + ' FAILED'); process.exit(1); }
  console.log('\nall passed');
})().catch((e) => { console.error(e); process.exit(1); });
