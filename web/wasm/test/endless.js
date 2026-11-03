#!/usr/bin/env node
// Browser check of the Endless Tower screen (specs/28 section 9 step 5), at phone width 390x844: plays and seals one run, then
// seeds a Vault of five copies of the sealed monster (ids 0..4, entry id 1 with a rulebook) through the save and checks the
// Dungeon tab's Endless card, the screen (Team / Floors / Board sections, issue #130; season, mutators, next floors with their
// squads), the team picker (exactly four, reorder, the rule count and Rules button per slot), a climb on the reel (opened landed) with its result, save
// record and deciding-floor playback (a rule firing reads "follows rule 1"), a second climb paying nothing new, the season
// board and display name (a refused name, a saved one, the player's row after the climb), watching another floor (same fight
// hash as the climb's list), the layout and Back.
// Fails (exit 1) on any page error, console error (Google Fonts aborts excluded) or failed assertion.
// Usage: node web/wasm/test/endless.js [--shots dir]   (screenshots are debug output, never compared)
'use strict';
const H = require('./harness.js');

(async () => {
  const t = await H.open({ shotsDir: H.shotsArg() });
  const { page, check, act, text } = t;
  const count = (sel) => page.locator(sel).count();
  const acct = async () => (await t.readAccount()).Account;

  // ---- one run to the end, sealed, through the wheel, the grade screen and the vendor back home
  await page.goto(t.url);
  await act('start').waitFor({ timeout: 90000 });
  await act('start').click();
  await act('drill-str').waitFor();
  await t.playUntil('end');
  if (await act('fight-continue').count()) await act('fight-continue').click();
  check(await act('seal').isVisible(), 'the run plays to its end');
  await page.emulateMedia({ reducedMotion: 'reduce' });   // the wheel lands at once
  await act('seal').click();
  await act('climb-continue').click();
  await page.locator('[data-id=grade]').waitFor();
  if (await act('to-vendor').count()) { await act('to-vendor').click(); await act('vendor-done').click(); }
  else await act('new-run').click();
  await act('start').waitFor();

  // ---- one monster in the Vault: the screen explains the team of four and the climb stays off
  await act('hometab-dungeon').click();
  check((await count('[data-id=endless-card]')) === 1 && /Best this season 0 · best ever 0/.test(await text('[data-id=endless-bests]')),
    'the Dungeon tab shows the Endless Tower card with this season\'s and the best ever floors');
  await act('endless').click();
  await page.locator('[data-id=endless-status]').waitFor();
  check(/4 monsters and your Vault holds 1/.test(await text('[data-id=endless-need]')) && (await act('endless-climb').isDisabled()),
    'with one monster the screen says the climb takes four, and Climb is off');
  await act('endless-back').click();

  // ---- a Vault of five copies of the sealed monster (ids 0..4); entry id 1 has a rulebook that always fires
  const save = await t.readAccount();
  const base = save.Account.Vault[0];
  save.Account.Vault = [0, 1, 2, 3, 4].map((id) => Object.assign(JSON.parse(JSON.stringify(base)), { Id: id }));
  save.Account.Rulebooks = { 1: 'always>basic@lowest_hp' };
  await t.writeSaveText(H.encodeSave(save));
  await page.evaluate(() => localStorage.setItem('idle-rng-fight-speed', '1'));   // playUntil left the remembered speed at Skip
  await t.reload();
  await act('hometab-dungeon').click();
  await act('endless').click();
  const status = page.locator('[data-id=endless-status]');
  await status.waitFor();
  const season = Math.floor((Math.floor(Date.now() / 86400000) + 3) / 7);   // run.endlessSeason(): the UTC week from Monday
  check((await status.getAttribute('data-season')) === String(season) && new RegExp('^Season ' + season + ' · \\d+[dh] \\d+[hm] left · best this season 0').test(await status.innerText()),
    'the season line shows this UTC week, the time left and the bests: ' + (await status.innerText()));
  // Issue #130: the screen opens on its Team section, with the next floor in one line; Floors and Board are one tap each
  check((await act('endless-tab-team').getAttribute('aria-selected')) === 'true' && /^Next\s+F1 /.test(await text('[data-id=endless-next]'))
    && (await count('[data-id=endless-mutators]')) === 0 && (await count('[data-id=endless-board]')) === 0,
    'the screen opens on Team with the next floor in one line, floors and board on their own tabs: ' + (await text('[data-id=endless-next]')).replace(/\s+/g, ' '));
  await act('endless-see-floors').click();
  check((await act('endless-tab-floors').getAttribute('aria-selected')) === 'true', 'the next floor line leads to the Floors tab');
  const muts = await page.$$eval('[data-id^=endless-mutator-]', (es) => es.map((e) => ({ on: e.dataset.on === '1', text: e.innerText })));
  check(muts.length >= 1 && muts.every((m) => /from floor \d+/.test(m.text)) && !muts[0].on, 'the season\'s mutators are listed with the floor each joins (none in force on floor 1): ' + muts.length);
  const floors = await page.$$eval('[data-id^=endless-floor-]', (es) => es.map((e) => ({ id: e.dataset.id, foes: e.querySelectorAll('[data-id=endless-foe]').length,
    check: (e.querySelector('[data-id=endless-check]') || {}).textContent, elite: e.classList.contains('elite') })));
  check(floors.length === 10 && floors[0].id === 'endless-floor-1' && floors.every((f) => f.foes === 4 && f.check) && floors[9].elite,
    'the next ten floors show four enemies each and their check, the tenth an elite: ' + JSON.stringify(floors.map((f) => f.check)));
  check(await act('endless-climb').isDisabled(), 'Climb is off with no team');
  await t.layout('endless floors');

  // ---- the season board (specs/28 section 6): empty before a climb, this device's only, a generated name until one is picked
  await act('endless-tab-board').click();
  check((await count('[data-id=endless-board-empty]')) === 1 && (await count('[data-id=endless-board-local]')) === 1
    && /^Your name: Trainer [0-9A-F]{4} \(until you pick one\)$/.test(await text('[data-id=endless-name]')),
    'the board is empty, says it is local, and shows a generated name: ' + (await text('[data-id=endless-name]')));
  await act('endless-name').click();
  const nameIn = page.locator('[data-id=endless-name-input]');
  await nameIn.fill('Ash!');
  await act('endless-name-save').click();
  check(/only letters/.test(await text('[data-id=endless-name-error]')) && (await nameIn.inputValue()) === 'Ash!' && !(await acct()).Name,
    'a name the rules refuse shows why, keeps the field as typed and saves nothing');
  await nameIn.fill('  Ash   Ketchum ');
  await nameIn.press('Enter');
  await page.locator('[data-id=endless-name]').waitFor();
  check((await text('[data-id=endless-name]')) === 'Your name: Ash Ketchum' && (await acct()).Name === 'Ash Ketchum',
    'Enter saves the name, trimmed and with its spaces folded, to the account');
  await t.layout('endless board (empty)');
  await act('endless-tab-team').click();
  await t.layout('endless screen');

  // ---- the team: exactly four
  for (const i of [0, 1, 2]) await act('endless-pick-' + i).check();
  check(await act('endless-climb').isDisabled(), 'three picked: Climb is still off');
  await act('endless-pick-3').check();
  check(await act('endless-climb').isEnabled() && (await act('endless-pick-4').isDisabled()), 'four picked: Climb is on and a fifth monster cannot be picked');
  check((await page.locator('[data-id=endless-swap]').count()) === 1 && !(await page.locator('[data-id=endless-swap]').evaluate((d) => d.open)),
    'a full team folds the Vault list behind "Swap a monster"');
  check((await count('[data-act^=endless-rules-]')) === 4 && (await text('[data-id=endless-rules-n-1]')) === '1 rule' && (await text('[data-id=endless-rules-n-0]')) === 'No rules',
    'each filled slot shows its rule count and a Rules button');
  await act('endless-slot-up-1').click();             // the monster with the rulebook moves to slot 1
  check((await text('[data-id=endless-rules-n-0]')) === '1 rule' && (await text('[data-id=endless-rules-n-1]')) === 'No rules', 'a slot moves up');
  // Rules opens the rulebook editor (web/rulebook.js) on this monster, with Test on the next floor for this team of four
  await act('endless-rules-0').click();
  await act('rb-sheet').waitFor();
  check((await count('[data-act=rb-rule-0]')) === 1 && (await act('rb-test').isVisible()) && /Test floor 1/.test(await act('rb-test').innerText()),
    'Rules opens the editor on that monster, its saved rule as a card, with Test on the next floor');
  await act('rb-test').click();
  await act('rb-result').waitFor({ timeout: 30000 });
  check(/^(Won|Lost on) floor 1/.test(await act('rb-result').innerText()), 'the editor tests floor 1 with this team: ' + (await act('rb-result').innerText()));
  await act('rb-close').click();
  check((await count('[data-act=rb-sheet]')) === 0, 'and closes back to the Endless screen');
  await t.layout('endless team');

  // ---- climb: it plays out on the reel (landed at once under reduced motion), then Watch plays the deciding floor back and a
  // rule firing reads as a rule line
  const shards0 = (await acct()).Shards || 0;
  await act('endless-climb').click();
  await page.locator('#modal [data-id=endless-reel]').waitFor();
  const landed = await text('[data-id=endless-landed]');
  check((await count('[data-act=endless-skip]')) === 0 && (await count('#modal [data-id=endless-arena] .squad .sprite')) === 4
    && /^Floor \d+/.test(await text('[data-id=endless-now]')) && /(Fell on floor \d+ to \S|nothing stopped this team)/.test(landed),
    'the climb plays out on the reel: with reduced motion it opens landed, the team of four in the arena: ' + landed.replace(/\s+/g, ' '));
  check((await count('[data-id=endless-banner].anim')) === 0, 'the result banner waits under the reel');
  await t.layout('endless reel');
  await act('endless-reel-watch').click();
  const play = page.locator('#modal [data-id=fight-play]');
  await play.waitFor();
  check(/^Floor \d+: \S/.test(await play.innerText()) && (await count('#modal [data-id=endless-play-mutators]')) === 1 && (await count('#modal .units.endless [data-id^=unit-]')) === 8,
    'the deciding floor plays back 4v4 with its mutators: ' + (await play.innerText()));
  await page.waitForTimeout(700);
  await t.layout('endless playback');
  await act('speed-skip').click();
  check((await count('[data-act=endless-replay]')) === 1, 'Skip ends the playback, Watch again shows');
  const logs = await page.$$eval('[data-id=endless-log] li.gambit', (ls) => ls.map((l) => l.textContent));
  check(logs.some((l) => / follows rule 1$/.test(l)), 'the deciding floor\'s log has the rulebook\'s rule firing: ' + (logs[0] || 'none'));
  const decidingHash = await play.getAttribute('data-hash');
  await act('endless-play-close').click();
  check((await count('#modal [data-id=fight-play]')) === 0 && (await count('#modal [data-id=endless-reel]')) === 0 && (await count('[data-id=endless-result]')) === 1,
    'Close leaves the result card, not the reel');
  const a1 = await acct();
  const fell = await text('[data-id=endless-fell]');
  check(/^(Fell on floor \d+ to \S|All \d+ floors cleared)/.test(fell), 'the result says where the climb ended: ' + fell);
  const paid = await text('[data-id=endless-shards]'), paidN = +((/^\+(\d+)/.exec(paid) || [])[1] || 0);
  check(a1.Endless && a1.Endless.Season === season && (a1.Endless.Climbs || 0) === 1 && (a1.Shards || 0) - shards0 === paidN,
    'the climb is saved for this season (1 climb) and paid what the card says: ' + paid);
  const rows = await page.$$eval('[data-id^=endless-fought-]', (rs) => rs.map((r) => ({ id: r.dataset.id, hash: r.dataset.hash })));
  check(rows.length >= 1 && rows[rows.length - 1].hash === decidingHash, 'the floors fought are listed, the deciding one last with the played fight\'s hash');
  check(/best this season \d+/.test(await status.innerText()) && /1 climb this season/.test(await status.innerText()), 'the season line counts the climb');
  // the damage chart: four monsters, the team totals are the bars' sum, one floor at a time on request
  await act('endless-chart').click();
  await page.locator('#modal [data-id=chart-endless]').waitFor();
  const parse = (v) => +v.split(' ')[0].replace(/,/g, '');
  const dealt = (await page.$$eval('#modal .crow.dealt .cval', (vs) => vs.map((v) => v.textContent))).map(parse);
  const teamDealt = +((/dealt ([\d,]+)/.exec(await text('[data-id=chart-total]')) || [])[1] || '-1').replace(/,/g, '');
  check(dealt.length === 4 && Math.abs(dealt.reduce((a, b) => a + b, 0) - teamDealt) <= 4,
    'the damage chart has a bar per monster and the team total is their sum: ' + dealt.join(' + ') + ' = ' + teamDealt);
  await page.selectOption('[data-act=chart-floor]', rows[rows.length - 1].id.slice('endless-fought-'.length));
  check((await page.locator('#modal [data-id=chart-endless]').getAttribute('data-floor')) === rows[rows.length - 1].id.slice('endless-fought-'.length), 'the floor picker shows the deciding floor alone');
  await t.layout('endless damage chart');
  await act('chart-close').click();
  check((await count('#modal [data-id=chart-endless]')) === 0 && (await count('[data-id=endless-result]')) === 1, 'Close leaves the result card');
  const best1 = a1.Endless.ClearedSeason || 0;
  await act('endless-tab-board').click();
  if (best1 > 0) {
    const me = page.locator('[data-id=endless-board-row-1]'), meText = (await me.innerText()).replace(/\s+/g, ' ').trim();
    check((await me.getAttribute('data-you')) === '1' && meText === '#1 Ash Ketchum (you) ' + best1 + (best1 === 1 ? ' floor' : ' floors')
      && /^1 trainer on the board/.test(await text('[data-id=endless-board-total]')),
      'the board shows the player first with the season best and their name: ' + meText);
  } else check((await count('[data-id=endless-board-empty]')) === 1, 'no floor cleared: the board stays empty');
  await t.layout('endless board');
  await t.layout('endless result');

  // ---- a second climb pays nothing new (the reel again; its Damage button opens the chart, every floor added up; Close goes on
  // to the card, its banner slamming in then)
  await act('endless-climb').click();
  await act('endless-reel-chart').click();
  await page.locator('#modal [data-id=chart-endless]').waitFor();
  check((await count('#modal [data-id=endless-reel]')) === 0 && (await page.locator('#modal [data-id=chart-endless]').getAttribute('data-floor')) === '0',
    'the reel\'s Damage button opens the damage chart on every floor added up');
  await act('chart-close').click();
  check((await count('#modal [data-id=chart-endless]')) === 0 && (await count('[data-id=endless-banner].anim')) === 1, 'closing the chart shows the card and slams in the banner');
  const a2 = await acct();
  check((await count('#modal [data-id=fight-play]')) === 0 && /^No new floors this season/.test(await text('[data-id=endless-shards]'))
    && (a2.Endless.Climbs || 0) === 2 && (a2.Shards || 0) === (a1.Shards || 0), 'a second climb pays nothing new and counts a second climb');

  // ---- watch another floor: the same fight the climb's list names
  const first = rows[0], floorN = +first.id.slice('endless-fought-'.length);
  await act('endless-watch-' + floorN).click();
  await play.waitFor();
  check((await play.getAttribute('data-hash')) === first.hash && (await play.innerText()).startsWith('Floor ' + floorN + ':'), 'Watch plays floor ' + floorN + ' with the listed fight hash');
  if (rows.length >= 2) {
    await act('endless-next').click();
    check((await play.getAttribute('data-hash')) === rows[1].hash, 'the next-floor button plays the next floor fought');
    await act('endless-prev').click();
    check((await play.getAttribute('data-hash')) === first.hash, 'and the previous-floor button goes back');
  }
  await act('endless-play-close').click();

  // ---- Back: home, with the new best on the card
  await act('endless-back').click();
  await act('endless').waitFor();
  check(new RegExp('Best this season ' + (a2.Endless.ClearedSeason || 0) + ' ').test(await text('[data-id=endless-bests]')), 'Back returns home; the card shows the season best');
  await t.finish();
})().catch((e) => { console.error(e); process.exit(1); });
