#!/usr/bin/env node
// Browser checks of the client's sync with the account server (web/sync.js, specs/27 sections 4 to 6), at phone width:
// - the server core runs in a page of its own (the local test server, specs/27 section 5.4) and two "devices" (browser contexts
//   with their own storage) reach it through a test transport (MonStackSyncTransport, web/wasm/BRIDGE.md "Sync transport"), as
//   a remote server would be reached; both sign in as the same player;
// - the first open of this build wipes the device's old saves (decision 9); the server's clock, three days ahead of the
//   device's, gives the Dungeon day and the seal time (issue #21);
// - a lost connection: play goes on inside the run; the next account-level command waits ("Connecting...") until the server is
//   back, then play resumes and nothing is lost;
// - two devices: the second takes the account; the first is signed out at its next commit and goes on as a new guest;
// - a rejection: a commit the server refuses (an edited command) reverts the device to the server's account and says so;
// - stale content: the server runs other content; the device says a new version is out, keeps its moves and plays them again
//   after the reload;
// - one tab plays: a second tab says the game is open elsewhere and can take over;
// - without a transport the page runs the local test server and says "Local test build"; on the GitHub Pages address it uses
//   the Cloud Code module and, with that unreachable, waits on "Connecting..." instead of falling back.
// Usage: node web/wasm/test/sync.js [--shots dir]   (screenshots are debug output, never compared)
'use strict';
const H = require('./harness.js');

const DAY_MS = 86400000;
const OFFSET_MS = 3 * DAY_MS;   // the server's clock runs three days ahead of the devices'

// The test transport each device page gets: open and commit go through window.__relay to the server page. The player is the
// same on both devices (as after a Google sign-in on each) until a sign-out makes this device a new guest.
function deviceTransport() {
  const P = 'test-player';
  const player = () => localStorage.getItem(P) || 'kj';
  const call = (op, req) => window.__relay(op, player(), JSON.stringify(req)).then((t) => JSON.parse(t));
  window.MonStackSyncTransport = {
    kind: 'test-remote',
    open: (req) => call('open', req),
    commit: (req) => call('commit', req),
    signOut: () => localStorage.setItem(P, 'guest-' + Math.random().toString(16).slice(2)),
  };
}

(async () => {
  const t = await H.open({ shotsDir: H.shotsArg() });
  const { page, ctx, check, act } = t;
  const browser = ctx.browser();
  const base = new URL(t.url);

  // ---- the server: the server core in a bare page of its own (engine.js and the local transport of sync.js, no game UI)
  const sctx = await browser.newContext();
  const sp = await sctx.newPage();
  sp.on('pageerror', (e) => t.errors.push('pageerror (server): ' + e.message));
  const serverUrl = new URL('sync-server.html', base).href;
  await sp.route(serverUrl, (r) => r.fulfill({ contentType: 'text/html', body: '<!doctype html><script src="engine.js"></script><script src="sync.js"></script>' }));
  await sp.goto(serverUrl);
  await sp.evaluate(async (off) => {
    const l = await IdleEngine.loadWasm({});
    l.host.RunInit(l.contentPack);
    window.srv = MonStackSync.localTransport(l.host, { storeKey: 'test-server-store', playerKey: 'test-server-player' });
    window.srv.clockOffsetMs = off;
  }, OFFSET_MS);
  const server = { down: false, tamper: null, oldContent: false, log: [] };
  const relay = async (src, op, player, reqText) => {
    if (server.down) throw new Error('the network is down');
    const req = JSON.parse(reqText);
    if (op === 'commit' && server.tamper) { server.tamper(req); server.tamper = null; }
    if (op === 'commit' && server.oldContent) req.contentVersion = '0.0.0-old';   // as if the server ran other content
    server.log.push({ op, player, req });
    return JSON.stringify(await sp.evaluate(([o, p, r]) => srv.call(o, p, r), [op, player, req]));
  };
  const commits = () => server.log.filter((x) => x.op === 'commit');

  async function device(c, pg) {
    await c.exposeBinding('__relay', relay);
    await c.addInitScript(deviceTransport);
    await c.addInitScript(() => document.addEventListener('DOMContentLoaded', () => {
      const st = document.createElement('style');
      st.textContent = '*, *::before, *::after { text-transform: none !important; }';
      document.head.append(st);
    }));
    await pg.emulateMedia({ reducedMotion: 'reduce' });   // the gauntlet wheel lands at once
  }
  const sync = (pg) => pg.evaluate(() => MonStackSync.state());
  const waitShown = (pg, mode) => pg.locator('#sync-wait[data-mode="' + mode + '"]').waitFor({ timeout: 30000 });
  const home = (pg) => pg.locator('[data-act="start"]').waitFor({ timeout: 90000 });
  // Plays run commands through the real buttons until n commands are pending or the sync overlay shows.
  const playMoves = (pg, n) => pg.evaluate(async (n) => {
    const q = (s) => document.querySelector(s);
    const click = (s) => { const e = q(s); if (!e || e.disabled) return false; e.click(); return true; };
    for (let i = 0; i < 600 && !q('#sync-wait') && MonStackSync.state().pending < n; i++) {
      if (q('[data-id=fight-play]') || q('[data-id=fight-intro]')) { click('[data-act="speed-skip"]'); continue; }
      if (click('[data-act="fight-continue"]')) continue;
      if (q('[data-id=offer-title]')) { click('[data-act="discard"]'); continue; }
      if (click('[data-act="fight"]')) continue;
      const m = /Energy (\d+)/.exec((q('.bar.energy span') || {}).textContent || '');
      click((m ? +m[1] : 100) < 45 ? '[data-act="rest"]' : '[data-act="drill-int"]');
      await new Promise((r) => setTimeout(r, 0));   // the event loop turns between taps, as for a player
    }
    return MonStackSync.state().pending;
  }, n);
  const vaultCount = async (pg) => {
    const a = await pg.evaluate((k) => { const x = localStorage.getItem(k); return x ? JSON.parse(x.slice(x.indexOf('\n') + 1)) : null; }, H.SAVE_KEY);
    return a && a.Account.Vault ? a.Account.Vault.length : 0;
  };

  // ---- device A: the first open of this build wipes the old saves; the account opens on the server
  await device(ctx, page);
  await page.goto(new URL('none.html', base).href);   // the origin, before the game loads
  await page.evaluate(() => {
    localStorage.setItem('idle-rng-account-v1', 'an account from before the server');
    localStorage.setItem('idle-rng-account-v1-unloadable-123', 'kept aside');
    localStorage.setItem('idle-rng-run-v1', 'older still');
  });
  await page.goto(t.url);
  await home(page);
  const wiped = await page.evaluate(() => ({ save: localStorage.getItem('idle-rng-account-v1'), bad: localStorage.getItem('idle-rng-account-v1-unloadable-123'), old: localStorage.getItem('idle-rng-run-v1') }));
  check(wiped.save && wiped.save.startsWith('IDLERNG-SAVE') && wiped.bad === null && wiped.old === null, 'the first open of this build deleted the old saves; the cache holds the server\'s account');
  check(server.log[0] && server.log[0].op === 'open' && server.log[0].req.claim === true, 'a guest\'s first open claims the account');
  const a0 = await sync(page);
  check(a0.status === 'saved' && a0.seq === 0 && !a0.local && (await page.locator('#server-mode').count()) === 0, 'device A opened the new account at commit 0 (no "Local test build" on a remote server)');
  const deviceDay = Math.floor(Date.now() / DAY_MS);
  check(a0.day === deviceDay + 3, 'the day comes from the server\'s clock, three days ahead of the device (' + a0.day + ', device ' + deviceDay + ')');

  // ---- a run: commands are sent at checkpoints, not one by one
  await act('start').click();
  await act('drill-str').waitFor();
  const started = commits().length;
  check(started === 1 && commits()[0].req.commands.map((c) => c.k).join() === 'startRun', 'startRun is its own checkpoint (the server counts the run first)');
  await act('drill-str').click();
  await act('rest').click();
  const a1 = await sync(page);
  check(a1.status === 'pending' && a1.pending === 2 && commits().length === started, 'run commands wait for the next checkpoint (' + a1.pending + ' pending)');
  // The server is unreachable at the next checkpoint: the run plays on (only run commands, fewer than twice the checkpoint).
  server.down = true;
  const offRun = await playMoves(page, 30);
  const o1 = await sync(page);
  check(o1.status === 'offline' && offRun >= 30 && (await page.locator('#sync-wait').count()) === 0, 'with the server unreachable the run plays on (' + offRun + ' moves not saved yet)');
  server.down = false;   // back: the retry lands them while play goes on
  await t.playUntil('end');
  if (await act('fight-continue').count()) await act('fight-continue').click();
  check(commits().length > started && commits().every((c) => c.req.commands.length <= 50), 'a run sends its commands at checkpoints of at most twice ' + 25 + ' (' + commits().length + ' commits)');

  // ---- a lost connection: the run plays on; the next account-level command waits until the server is back
  server.down = true;
  await act('seal').click();                          // finishRun applies at once; its commit cannot land
  await act('climb-continue').click();
  await page.locator('[data-id=grade]').waitFor();
  await page.waitForFunction(() => MonStackSync.state().status === 'offline', null, { timeout: 30000 });
  const off = await sync(page);
  check(off.pending >= 1 && /network is down/.test(off.error || ''), 'the commit with finishRun could not land: ' + off.pending + ' pending, ' + off.error);
  // The next account-level command: closing the vendor, or the next run's start.
  let next;
  if (await act('to-vendor').count()) { await act('to-vendor').click(); next = 'vendor-done'; }
  else { await act('new-run').click(); next = 'start'; }
  await act(next).click();
  await waitShown(page, 'play');
  check(/Connecting/.test(await page.locator('#sync-wait').innerText()) && (await act('sync-retry').count()) === 1, 'play waits: "Connecting..." with Try again');
  check((await act(next).count()) === 1 && (await sync(page)).pending === off.pending, 'the waiting command was not applied (' + next + ')');
  await t.layout('waiting for the server');
  server.down = false;
  await act('sync-retry').click();
  await page.locator('#sync-wait').waitFor({ state: 'detached', timeout: 30000 });
  const back = await sync(page);
  check(back.status === 'saved' && back.pending === 0, 'Try again lands the commit; the wait goes');
  const sealCommit = commits().find((c) => c.req.commands.some((x) => x.k === 'finishRun'));
  const sealMs = sealCommit && sealCommit.req.commands.find((x) => x.k === 'finishRun').a[0];
  check(sealMs > Date.now() + OFFSET_MS - 600000, 'the seal time is the server\'s time, not the device\'s (issue #21)');
  await act(next).click();
  if (next === 'start') { await act('drill-str').waitFor(); await act('tab-log').click(); await act('abandon').click(); await act('abandon-yes').click(); }
  await home(page);
  await page.waitForFunction(() => MonStackSync.state().status === 'saved', null, { timeout: 30000 });
  const resumed = await sync(page), vc = await vaultCount(page);
  check(resumed.status === 'saved' && vc === 1, 'play resumes, the sealed monster is in the Vault (' + resumed.status + ', ' + vc + ')');
  const seqA = (await sync(page)).seq;

  // ---- a second device signs in to the same player: it takes the account
  const bctx = await browser.newContext({ viewport: H.PHONE, deviceScaleFactor: 2, isMobile: true, hasTouch: true });
  await bctx.route(/fonts\.(googleapis|gstatic)\.com/, (r) => r.abort());
  await bctx.route(/(\.services\.api\.unity\.com|accounts\.google\.com)\//, (r) => r.abort());
  const pb = await bctx.newPage();
  pb.on('pageerror', (e) => t.errors.push('pageerror (B): ' + e.message));
  await device(bctx, pb);
  await pb.goto(t.url);
  await home(pb);
  const b0 = await sync(pb);
  check(b0.seq === seqA && await vaultCount(pb) === 1, 'device B opens the same account (commit ' + b0.seq + ', the sealed monster)');
  // Device A plays on: its next commit finds the account taken and signs it out.
  await act('start').click();                         // the run screen, then the reload of the sign-out
  await home(page);
  check(/Signed out: your account was opened on another device/.test(await page.locator('#toasts').innerText()), 'device A is signed out with a message');
  const aGuest = await sync(page);
  check(aGuest.status === 'saved' && aGuest.seq === 0 && await vaultCount(page) === 0, 'and goes on as a new guest (a new account, empty Vault)');
  check(server.log.filter((x) => x.op === 'open').slice(-1)[0].player !== 'kj', 'the guest is another player on the server');
  await pb.locator('[data-act="start"]').click();
  await pb.locator('[data-act="drill-str"]').waitFor();
  check((await sync(pb)).seq === seqA + 1, 'device B keeps playing the account');

  // ---- a rejection: the server refuses an edited command; device B reverts to the server's account
  await pb.locator('[data-act="drill-str"]').click();
  await pb.locator('[data-act="tab-log"]').click();
  const before = (await sync(pb)).seq;
  server.tamper = (req) => { req.commands[0].a = ['no_such_stat']; };   // an edited client: the drill the server re-applies is not the one played
  await pb.locator('[data-act="abandon"]').click();
  const reverted = pb.waitForEvent('load', { timeout: 30000 });
  await pb.locator('[data-act="abandon-yes"]').click();   // abandon is a checkpoint: its commit carries the drill and the abandon
  await reverted;
  await pb.locator('[data-act^="drill-"]').first().waitFor({ timeout: 90000 });
  const rb = await sync(pb);
  check(/did not accept your last moves/.test(await pb.locator('#toasts').innerText()) && rb.lastReject && rb.lastReject.code === 'Refused' && rb.lastReject.index === 0,
    'device B is told the server refused the commit (' + (rb.lastReject && rb.lastReject.reason) + ')');
  check(rb.seq === before && rb.pending === 0, 'it reverted to the server\'s account: the run the abandon would have ended is still there');
  await pb.locator('[data-act="account"]').click();
  check(/turned down a save \(Refused, at drill/.test(await pb.locator('[data-id=sync-reject]').innerText()), 'the account sheet reports the refused command');
  await pb.locator('[data-act="account-close"]').click();

  // ---- stale content: the server runs other content; the moves are kept and played again after the reload
  await pb.locator('[data-act="tab-train"]').click();
  server.oldContent = true;
  const played = await playMoves(pb, 25);
  await waitShown(pb, 'version');
  check(/A new version is out/.test(await pb.locator('#sync-wait').innerText()) && played >= 25, 'the checkpoint finds other content on the server: "A new version is out" (' + played + ' moves kept)');
  const runBefore = await pb.evaluate((k) => { const x = localStorage.getItem(k); return JSON.parse(x.slice(x.indexOf('\n') + 1)).Run; }, H.SAVE_KEY);
  server.oldContent = false;   // the new version is out: the reload loads it
  const reloaded = pb.waitForEvent('load', { timeout: 30000 });
  await pb.locator('[data-act="sync-reload"]').click();
  await reloaded;
  await pb.locator('[data-act^="drill-"], [data-act="fight"]').first().waitFor({ timeout: 90000 });
  await pb.waitForFunction(() => MonStackSync.state().status === 'saved', null, { timeout: 30000 });
  const runAfter = await pb.evaluate((k) => { const x = localStorage.getItem(k); return JSON.parse(x.slice(x.indexOf('\n') + 1)).Run; }, H.SAVE_KEY);
  check(JSON.stringify(runAfter) === JSON.stringify(runBefore), 'after the reload the kept moves are played again and committed: the same run (' + (runAfter.Drills || 0) + ' drills)');

  // ---- one tab plays: a second tab of device B says the game is open elsewhere, and can take over
  const pb2 = await bctx.newPage();
  pb2.on('pageerror', (e) => t.errors.push('pageerror (B2): ' + e.message));
  await pb2.goto(t.url);
  await waitShown(pb2, 'tab');
  check(/open in another tab/.test(await pb2.locator('#sync-wait').innerText()), 'a second tab says the game is open in another tab');
  await pb2.locator('[data-act="sync-here"]').click();
  await pb2.locator('[data-act^="drill-"], [data-act="fight"]').first().waitFor({ timeout: 90000 });
  await waitShown(pb, 'taken');
  check(/opened in another tab/.test(await pb.locator('#sync-wait').innerText()), '"Play here" moves play to the new tab; the first one stops');
  await pb2.close();
  await bctx.close();

  // ---- without a transport the page runs the local test server and says so
  const lctx = await browser.newContext({ viewport: H.PHONE });
  await lctx.route(/fonts\.(googleapis|gstatic)\.com/, (r) => r.abort());
  await lctx.route(/(\.services\.api\.unity\.com|accounts\.google\.com)\//, (r) => r.abort());
  const pl = await lctx.newPage();
  pl.on('pageerror', (e) => t.errors.push('pageerror (local): ' + e.message));
  await pl.goto(t.url);
  await home(pl);
  check((await pl.locator('#server-mode').innerText()) === 'Local test build' && (await sync(pl)).local, 'the local test server: the header says "Local test build"');
  await pl.locator('[data-act="account"]').click();
  check(/Saved on the server/.test(await pl.locator('[data-id=sync-status]').innerText()) && (await pl.locator('[data-id=sync-local]').count()) === 1, 'the account sheet shows the sync state and the local test build');
  await lctx.close();

  // ---- on the GitHub Pages address the page syncs with the Cloud Code module (web/ugs-transport.js), never the local test
  // server: with the module unreachable it waits on "Connecting..." with Try again
  const gctx = await browser.newContext({ viewport: H.PHONE });
  await gctx.route(/fonts\.(googleapis|gstatic)\.com/, (r) => r.abort());
  await gctx.route(/(\.services\.api\.unity\.com|accounts\.google\.com)\//, (r) => r.abort());   // UGS (auth and Cloud Code) unreachable
  await gctx.route(/^https:\/\/kjew2\.github\.io\/MonStack\//, async (r) => {   // the Pages site, served from web/
    const rest = new URL(r.request().url()).pathname.slice('/MonStack/'.length) || 'index.html';
    const res = await r.fetch({ url: new URL(rest, base).href });
    await r.fulfill({ response: res });
  });
  const pg = await gctx.newPage();
  pg.on('pageerror', (e) => t.errors.push('pageerror (pages): ' + e.message));
  await pg.goto('https://kjew2.github.io/MonStack/');
  await pg.locator('[data-act="sync-retry"]').waitFor({ timeout: 90000 });
  const gs = await sync(pg);
  check(/Connecting/.test(await pg.locator('#sync-wait').innerText()) && (gs.status === 'connecting' || gs.status === 'offline') && gs.seq === -1 && !gs.local && (await pg.locator('#server-mode').count()) === 0
    && (await pg.locator('[data-act="start"]').count()) === 0, 'on GitHub Pages with the module unreachable the page waits on "Connecting..." with Try again, no local fallback (' + JSON.stringify(gs) + ')');
  check(await pg.evaluate(() => localStorage.getItem('monstack-local-server-v1') === null), 'and no local test server store was made');
  await gctx.close();
  await sctx.close();

  await t.finish();
})().catch((e) => { console.error(e); process.exit(1); });
