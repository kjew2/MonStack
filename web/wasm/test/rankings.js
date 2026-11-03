#!/usr/bin/env node
// Browser check of the home Rankings tab at phone width 390x844: the six home tabs fit, the Endless season board (empty before a
// climb, last season, the player's row after a climb), the gauntlet boards (the sealed monster's row with its Legacy Title on the
// overall board, the species chips filtering it), the display name (shown on the rows once saved), a board read that fails
// (the error and Retry), a read that lands after another board was picked (drawn nowhere), and the layout.
// The page's run.readBoard is wrapped (an init script around RunEngine.create) to count the reads, fail them or hold them back;
// it still reads the real board. Fails (exit 1) on any page error, console error or failed assertion.
// Usage: node web/wasm/test/rankings.js [--shots dir]   (screenshots are debug output, never compared)
'use strict';
const H = require('./harness.js');

(async () => {
  const t = await H.open({ shotsDir: H.shotsArg() });
  const { page, check, act, text } = t;
  const count = (sel) => page.locator(sel).count();
  const acct = async () => (await t.readAccount()).Account;
  // window.__rank: reads (each [kind, arg]), fail (a message: the next reads reject with it), hold (true: the next reads wait
  // for release(i), oldest first by default).
  await t.ctx.addInitScript(() => {
    const R = window.__rank = { reads: [], fail: null, hold: false, held: [], release: (i) => { const x = R.held.splice(i || 0, 1)[0]; if (x) x(); } };
    let RE;
    Object.defineProperty(window, 'RunEngine', { configurable: true, get: () => RE, set: (v) => {
      RE = Object.assign({}, v, { create: async (o) => {
        const run = await v.create(o), read = run.readBoard;
        run.readBoard = (kind, arg) => {
          R.reads.push([kind, arg]);
          if (R.fail) return Promise.reject(new Error(R.fail));
          if (!R.hold) return read(kind, arg);
          return new Promise((go) => R.held.push(go)).then(() => read(kind, arg));
        };
        return run;
      } });
    } });
  });
  const reads = () => page.evaluate(() => window.__rank.reads.length);
  const boardOn = (key) => page.locator('[data-id=rank-board][data-key="' + key + '"]:not([data-loading])').waitFor();
  const season = Math.floor((Math.floor(Date.now() / 86400000) + 3) / 7);   // run.endlessSeason(): the UTC week from Monday
  const rowText = (sel) => text(sel).then((s) => s.replace(/\s+/g, ' ').trim());

  // ---- one run to the end, sealed: its monster climbs the gauntlet and gets a Legacy Title
  await page.goto(t.url);
  await act('start').waitFor({ timeout: 90000 });
  await act('start').click();
  await act('drill-str').waitFor();
  await t.playUntil('end');
  if (await act('fight-continue').count()) await act('fight-continue').click();
  await page.emulateMedia({ reducedMotion: 'reduce' });   // the wheel lands at once
  await act('seal').click();
  await act('climb-continue').click();
  await page.locator('[data-id=grade]').waitFor();
  if (await act('to-vendor').count()) { await act('to-vendor').click(); await act('vendor-done').click(); }
  else await act('new-run').click();
  await act('start').waitFor();
  await act('hometab-vault').click();
  const label = await text('[data-id=vault-title]');
  check(/^(Floor \d+: \S|All \d+ floors: \S)/.test(label), 'the sealed monster has a Legacy Title in the Vault: ' + label);

  // ---- six home tabs fit the phone: no label runs past its tab
  const tabs = await page.$$eval('#tabs button', (bs) => bs.map((b) => {
    const r = document.createRange(); r.selectNodeContents(b.firstChild);
    return { act: b.dataset.act, fits: r.getBoundingClientRect().width <= b.getBoundingClientRect().width + 0.5 };
  }));
  check(tabs.length === 6 && tabs[5].act === 'hometab-rankings' && tabs.every((x) => x.fits), 'six home tabs, Ranks last, every label inside its tab: ' + JSON.stringify(tabs));

  // ---- the Rankings tab opens on the Endless season board: empty before a climb, this account's only
  const r0 = await reads();
  await act('hometab-rankings').click();
  await boardOn('endless:' + season);
  check((await act('rank-kind-endless').getAttribute('aria-selected')) === 'true' && (await reads()) === r0 + 1
    && JSON.stringify(await page.evaluate(() => window.__rank.reads[window.__rank.reads.length - 1])) === JSON.stringify(['endless', season]),
    'the tab opens on this season\'s Endless board, read once');
  check((await count('[data-id=rank-board-empty]')) === 1 && (await count('[data-id=rank-board-local]')) === 1
    && /until online accounts are live/.test(await text('[data-id=rank-board-local]')) && /^Season \d+ · /.test(await text('[data-id=rank-season]')),
    'no climb yet: the board is empty and says it holds this account only');
  check(/^Your name: Trainer [0-9A-F]{4} \(until you pick one\)$/.test(await text('[data-id=rank-name]')), 'a generated name until one is picked: ' + (await text('[data-id=rank-name]')));
  await t.layout('rankings endless (empty)');
  if (season >= 1) {
    await act('rank-season-last').click();
    await boardOn('endless:' + (season - 1));
    check(/last season/.test(await text('[data-id=rank-board-empty]')) && (await act('rank-season-last').getAttribute('aria-selected')) === 'true',
      'Last season reads the board of season ' + (season - 1) + ': empty');
    await act('rank-season-this').click();
    await boardOn('endless:' + season);
  }

  // ---- the gauntlet board, all species: the sealed monster's row with its title, the player's (generated) name
  await act('rank-kind-gauntlet').click();
  await boardOn('gauntlet:all');
  check((await act('rank-species-all').getAttribute('aria-pressed')) === 'true'
    && JSON.stringify(await page.evaluate(() => window.__rank.reads[window.__rank.reads.length - 1])) === JSON.stringify(['gauntlet', null]),
    'Gauntlet reads the overall board (species null), the All chip on');
  const g1 = await rowText('[data-id=rank-row-1]');
  check((await page.locator('[data-id=rank-row-1]').getAttribute('data-you')) === '1' && /^#1 Trainer [0-9A-F]{4} \(you\) /.test(g1)
    && (await text('[data-id=rank-row-1] [data-id=rank-row-label]')) === label && (await count('[data-id=rank-row-1] [data-id=rank-row-species]')) === 1
    && /^1 monster on the board · ties go to whoever sealed first$/.test(await text('[data-id=rank-board-total]')),
    'the sealed monster is first, marked as the player\'s, with its species and Legacy Title: ' + g1);
  await t.layout('rankings gauntlet');

  // a re-render (the name field opening and closing) draws the kept board without reading it again
  const r1 = await reads();
  await act('rank-name').click();
  await act('rank-name-cancel').click();
  check((await reads()) === r1 && (await count('[data-id=rank-row-1]')) === 1, 'a re-render draws the kept board, no new read');

  // ---- species chips: the sealed species shows the row, another species is empty
  const spName = (await text('[data-id=rank-row-1] [data-id=rank-row-species]')).replace(/ · $/, '').trim();
  const chips = await page.$$eval('[data-act^=rank-species-]', (cs) => cs.map((c) => ({ act: c.dataset.act, name: c.textContent })));
  const mine = chips.find((c) => c.name === spName), other = chips.find((c) => c.act !== 'rank-species-all' && c.name !== spName);
  check(!!mine && !!other, 'a chip per species: ' + chips.map((c) => c.name).join(', '));
  await act(mine.act).click();
  const spId = mine.act.slice('rank-species-'.length);
  await boardOn('gauntlet:' + spId);
  check((await act(mine.act).getAttribute('aria-pressed')) === 'true' && (await text('[data-id=rank-row-1] [data-id=rank-row-label]')) === label
    && (await count('[data-id=rank-row-species]')) === 0
    && JSON.stringify(await page.evaluate(() => window.__rank.reads[window.__rank.reads.length - 1])) === JSON.stringify(['gauntlet', spId]),
    'the ' + spName + ' chip reads that species\' board: the row is there, the species left out');
  await act(other.act).click();
  await boardOn('gauntlet:' + other.act.slice('rank-species-'.length));
  check((await count('[data-id=rank-row-1]')) === 0 && new RegExp('^No ' + other.name + ' has climbed').test(await text('[data-id=rank-board-empty]')),
    'the ' + other.name + ' chip shows its empty board');

  // ---- the display name: a refused one says why, a saved one shows on the rows (the boards are read again)
  await act('rank-species-all').click();
  await boardOn('gauntlet:all');
  await act('rank-name').click();
  const nameIn = page.locator('[data-id=rank-name-input]');
  await nameIn.fill('Mi!');
  await act('rank-name-save').click();
  check(/only letters/.test(await text('[data-id=rank-name-error]')) && !(await acct()).Name, 'a name the rules refuse shows why and saves nothing');
  const r2 = await reads();
  await nameIn.fill('Misty');
  await act('rank-name-save').click();
  await page.locator('[data-id=rank-name]').waitFor();
  await boardOn('gauntlet:all');
  check((await text('[data-id=rank-name]')) === 'Your name: Misty' && (await acct()).Name === 'Misty' && (await reads()) > r2
    && /^#1 Misty \(you\) /.test(await rowText('[data-id=rank-row-1]')), 'the saved name shows on the player\'s row, the board read again: ' + (await rowText('[data-id=rank-row-1]')));

  // ---- a read that fails shows why with Retry; Retry reads it again
  await page.evaluate(() => { window.__rank.fail = 'Network down'; });
  await act('rank-refresh').click();
  await page.locator('[data-id=rank-board-error]').waitFor();
  check(/Network down/.test(await text('[data-id=rank-board-error]')) && (await count('[data-act=rank-retry]')) === 1 && (await count('[data-id=rank-row-1]')) === 0,
    'a failed read shows the error and a Retry button: ' + (await text('[data-id=rank-board-error]')));
  await t.layout('rankings error');
  await page.evaluate(() => { window.__rank.fail = null; });
  await act('rank-retry').click();
  await boardOn('gauntlet:all');
  check((await count('[data-id=rank-board-error]')) === 0 && /^#1 Misty \(you\) /.test(await rowText('[data-id=rank-row-1]')), 'Retry reads the board again');

  // ---- a read that lands after another board was picked is kept, not drawn
  await page.evaluate(() => { window.__rank.hold = true; });
  await act('rank-kind-endless').click();
  await act('rank-kind-gauntlet').click();
  check((await page.locator('[data-id=rank-board]').getAttribute('data-key')) === 'gauntlet:all' && (await page.locator('[data-id=rank-board]').getAttribute('data-loading')) === '1'
    && (await act('rank-refresh').isDisabled()), 'while a read is out the kept board shows and Refresh waits');
  await page.evaluate(() => { window.__rank.hold = false; window.__rank.release(0); });   // the Endless read lands first
  await page.waitForTimeout(100);
  check((await page.locator('[data-id=rank-board]').getAttribute('data-key')) === 'gauntlet:all' && (await page.locator('[data-id=rank-board]').getAttribute('data-loading')) === '1',
    'the Endless read landing late draws nothing: the gauntlet board stays, still loading');
  await page.evaluate(() => window.__rank.release(0));
  await boardOn('gauntlet:all');
  check(/^#1 Misty \(you\) /.test(await rowText('[data-id=rank-row-1]')), 'then the gauntlet read lands and draws');

  // ---- the Endless board after a climb: a Vault of four copies of the sealed monster climbs once
  const save = await t.readAccount();
  const base = save.Account.Vault[0];
  save.Account.Vault = [0, 1, 2, 3].map((id) => Object.assign(JSON.parse(JSON.stringify(base)), { Id: id }));
  await t.writeSaveText(H.encodeSave(save));
  await page.evaluate(() => localStorage.setItem('idle-rng-fight-speed', '1'));
  await t.reload();
  await act('hometab-dungeon').click();
  await act('endless').click();
  await page.locator('[data-id=endless-status]').waitFor();
  for (const i of [0, 1, 2, 3]) await act('endless-pick-' + i).check();
  await act('endless-climb').click();
  await act('endless-reel-done').click();
  const best = (await acct()).Endless.ClearedSeason || 0;
  await act('endless-rankings').click();
  await boardOn('endless:' + season);
  if (best > 0) {
    const me = await rowText('[data-id=rank-row-1]');
    check((await page.locator('[data-id=rank-row-1]').getAttribute('data-you')) === '1' && me === '#1 Misty (you) ' + best + (best === 1 ? ' floor' : ' floors')
      && /^1 trainer on the board · ties go to whoever got there first$/.test(await text('[data-id=rank-board-total]')),
      'after a climb the Endless board shows the player first with the season best: ' + me);
  } else check((await count('[data-id=rank-board-empty]')) === 1, 'no floor cleared: the Endless board stays empty');
  await t.layout('rankings endless');
  await t.finish();
})().catch((e) => { console.error(e); process.exit(1); });
