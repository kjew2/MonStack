#!/usr/bin/env node
// End-to-end browser check of the monster-trainer page (web/index.html) on the C# (wasm) engine at 390x844: picks a species,
// plays a whole run through the real buttons (drill, rest, rank fights, tree drafts, buying nodes), seals it, checks the grade
// screen (after the gauntlet wheel) and Vault list, reloads mid-run and after the end to prove the replay save restores the same run.
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
  const url = srv.url + 'index.html';
  // The account save is AccountSave text: a header line, then JSON.
  const readAccount = () => page.evaluate(() => { const t = localStorage.getItem('idle-rng-account-v1'); return t ? JSON.parse(t.slice(t.indexOf('\n') + 1)) : null; });

  // ---- home screen
  await page.goto(url);
  await act('start').waitFor({ timeout: 90000 });
  check((await page.locator('[data-act^="species-"]').count()) >= 3, 'home lists the species from the C# content');
  check(await act('start').isEnabled(), 'a species is pre-picked');
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

  // ---- trees tab
  await act('tab-trees').click();
  check((await page.locator('[data-id^="slot-"]').count()) === 2, 'two native trees slotted at the start');
  check(/SP/.test(await text('[data-id=sp2]')), 'skill points shown');
  await shot('03-trees');
  await act('tab-train').click();

  // ---- play the whole run through the buttons
  const played = await page.evaluate(() => {
    const q = (s) => document.querySelector(s);
    const click = (s) => { const e = q(s); if (!e || e.disabled) return false; e.click(); return true; };
    const stats = { fights: 0, drafts: 0, bought: 0, takes: 0, steps: 0 };
    for (let i = 0; i < 1500 && !q('[data-act="seal"]'); i++) {
      stats.steps++;
      if (click('[data-act="fight-continue"]')) continue;
      if (q('[data-id=offer-title]')) {
        stats.drafts++;
        if (click('[data-act^="take-0-2"]') || click('[data-act^="take-0-3"]') || click('[data-act^="take-0-1"]')) stats.takes++; else click('[data-act="discard"]');
        continue;
      }
      if (click('[data-act="fight"]')) { stats.fights++; continue; }
      click('[data-act="tab-trees"]');
      while (click('[data-act^="buy-"]')) stats.bought++;
      click('[data-act="tab-train"]');
      const m = /Energy (\d+)/.exec((q('.bar.energy span') || {}).textContent || '');
      const energy = m ? +m[1] : 100;
      if (energy < 45) click('[data-act="rest"]');
      else click('[data-act="drill-' + ['str', 'hp', 'def', 'spd', 'int', 'wis', 'dex', 'luk'][stats.steps % 8] + '"]');
    }
    return stats;
  });
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

  // ---- the book option and drafts
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

  check(errors.length === 0, 'no page/console errors' + (errors.length ? ': ' + errors.join(' | ') : ''));
  await browser.close();
  await srv.close();
  if (fails.length) { console.log('\n' + fails.length + ' FAILED'); process.exit(1); }
  console.log('\nall passed');
})().catch((e) => { console.error(e); process.exit(1); });
