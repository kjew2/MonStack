#!/usr/bin/env node
// Browser checks of player accounts (issue #19, specs/08 section 4; web/auth.js and web/account.js), against a fake Unity
// Authentication server and a fake Google Identity Services script (no network):
// - first launch signs in anonymously; a reload reuses the stored session (same player); a session the server rejects
//   starts a new guest;
// - with no Google client id the sheet says sign-in is not set up; with one, Google links to the guest player;
// - a second device (fresh auth storage) signing in with the same Google account gets the same player back;
// - sign out makes a new guest, and Google brings the linked player back;
// - with the server unreachable the game still plays and the account shows Offline; the sheet fits the phone width.
// Usage: node web/wasm/test/auth.js [--shots dir]   (screenshots are debug output, never compared)
'use strict';
const H = require('./harness.js');

const AUTH_KEY = 'monstack-auth-v1';   // web/auth.js KEY

// A fake Unity Authentication: players, their session tokens and Google links, plus a log of the calls.
function fakeUgs() {
  let n = 0;
  const players = {}, sessions = {}, googleOwner = {}, calls = [];
  const sub = (jwt) => { try { return JSON.parse(Buffer.from(jwt.split('.')[1], 'base64').toString()).sub; } catch (e) { return null; } };
  const issue = (id) => {
    const st = 'st-' + (++n), it = 'it-' + id + '-' + n;
    sessions[st] = id; players[id].idTokens.add(it);
    return { idToken: it, sessionToken: st, expiresIn: 3599, userId: id, user: { id, disabled: false, externalIds: players[id].ext } };
  };
  const create = () => { const id = 'P' + (++n); players[id] = { ext: [], idTokens: new Set() }; return id; };
  const bearerOf = (req) => {
    const a = req.headers().authorization || '';
    const t = a.replace(/^Bearer /, '');
    return Object.keys(players).find((id) => players[id].idTokens.has(t)) || null;
  };
  const handle = (path, body, req) => {
    calls.push(path);
    if (path === '/v1/authentication/anonymous') return [200, issue(create())];
    if (path === '/v1/authentication/session-token') {
      const id = sessions[body.sessionToken];
      return id ? [200, issue(id)] : [401, { title: 'INVALID_SESSION_TOKEN', detail: 'The session token could not be validated.', status: 401 }];
    }
    if (path === '/v1/authentication/link/google.com') {
      const id = bearerOf(req), g = sub(body.token);
      if (!id) return [401, { title: 'UNAUTHORIZED', detail: 'bad bearer', status: 401 }];
      if (!g) return [401, { title: 'INVALID_TOKEN', detail: 'bad google token', status: 401 }];
      if (googleOwner[g] && googleOwner[g] !== id) return [409, { title: 'ENTITY_EXISTS', detail: 'already linked to another player', status: 409 }];
      googleOwner[g] = id; players[id].ext = [{ providerId: 'google.com', externalId: g }];
      return [200, { user: { id, externalIds: players[id].ext } }];
    }
    if (path === '/v1/authentication/external-token/google.com') {
      const g = sub(body.token);
      if (!g) return [401, { title: 'INVALID_TOKEN', detail: 'bad google token', status: 401 }];
      if (!googleOwner[g]) {
        if (body.signInOnly) return [404, { title: 'ENTITY_NOT_FOUND', detail: 'player not found', status: 404 }];
        const id = create(); googleOwner[g] = id; players[id].ext = [{ providerId: 'google.com', externalId: g }];
      }
      return [200, issue(googleOwner[g])];
    }
    return [404, { title: 'Not Found', status: 404 }];
  };
  return { calls, sessions, handle, up: true };
}

// A fake Google Identity Services script: renderButton draws a button that "signs in" as window.__googleEmail.
const FAKE_GIS = `window.google = { accounts: { id: {
  initialize(c) { window.__gis = c; },
  renderButton(el) { const b = document.createElement('button'); b.type = 'button'; b.dataset.act = 'fake-google'; b.textContent = 'Sign in with Google';
    b.onclick = () => window.__gis.callback({ credential: window.__googleEmail ? window.__googleJwt(window.__googleEmail) : 'not-a-token' }); el.append(b); },
} } };`;

(async () => {
  const t = await H.open({ shotsDir: H.shotsArg() });
  const { page, ctx, check, act } = t;
  const ugs = fakeUgs();
  await ctx.route(/player-auth\.services\.api\.unity\.com\//, async (r) => {
    if (!ugs.up) return r.abort();
    const req = r.request();
    const url = new URL(req.url());
    const [status, body] = ugs.handle(url.pathname, JSON.parse(req.postData() || '{}'), req);
    await r.fulfill({ status, contentType: 'application/json', headers: { 'access-control-allow-origin': '*' }, body: JSON.stringify(body) });
  });
  await ctx.route(/accounts\.google\.com\/gsi\/client/, (r) => r.fulfill({ status: 200, contentType: 'text/javascript', body: FAKE_GIS }));
  // A fake Google ID token (base64url header and payload with the email and a subject, no real signature).
  await ctx.addInitScript(() => {
    const b = (o) => btoa(JSON.stringify(o)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
    window.__googleJwt = (email) => b({ alg: 'RS256' }) + '.' + b({ email, sub: 'g-' + email }) + '.sig';
  });

  const ready = () => page.locator('[data-act="start"], [data-act^="drill-"]').first().waitFor({ timeout: 90000 });
  const auth = () => page.evaluate((k) => JSON.parse(localStorage.getItem(k) || 'null'), AUTH_KEY);
  const btn = () => page.locator('#account-btn').innerText();
  const settled = () => page.waitForFunction(() => window.MonStackAuth && !MonStackAuth.state().busy && MonStackAuth.state().status !== 'signing-in');
  const openSheet = async () => { await act('account').click(); await page.locator('#account-sheet').waitFor(); };
  const closeSheet = () => act('account-close').click();
  const sheetText = () => page.locator('#account-sheet').innerText();
  const googleIn = async (email) => {
    await page.evaluate((e) => { window.__googleEmail = e; }, email);
    await act('fake-google').click();
    await page.locator('[data-id=account-note]').waitFor();
    await settled();
  };

  // First launch: a guest player, stored on the device.
  await page.goto(t.url);
  await ready();
  await settled();
  const first = await auth();
  check(first && /^P\d+$/.test(first.userId) && !!first.sessionToken, 'first launch signs in anonymously and stores the session');
  check(await btn() === 'Guest', 'the header shows Guest');
  check(ugs.calls.filter((c) => c.endsWith('/anonymous')).length === 1, 'one anonymous sign-in');

  // Reload: the stored session is refreshed, same player.
  await t.reload(); await settled();
  check((await auth()).userId === first.userId, 'a reload keeps the same player (session token)');
  check(ugs.calls.filter((c) => c.endsWith('/anonymous')).length === 1 && ugs.calls.some((c) => c.endsWith('/session-token')), 'the reload used the session token, not a new sign-in');

  // No Google client id: the sheet says so.
  await openSheet();
  check(/guest/i.test(await sheetText()), 'the sheet says the player is a guest');
  check(await page.locator('[data-id=google-off]').count() === 1, 'with no Google client id the sheet says sign-in is not set up');
  await t.layout('account sheet');
  const sheetWide = await page.evaluate(() => { const e = document.getElementById('account-sheet'); return e.scrollWidth > e.clientWidth; });
  check(!sheetWide, 'account sheet: no horizontal scroll');
  await closeSheet();
  check(await page.locator('#account-sheet').count() === 0, 'Close shuts the sheet');

  // Link Google to the guest player.
  await page.evaluate(() => { MonStackAuth.config.googleClientId = 'test-client.apps.googleusercontent.com'; });
  await openSheet();
  await act('fake-google').waitFor();
  // A Google token the server rejects (as UGS does before the Google provider is set up): an error, still the same guest.
  await googleIn('');
  check(/Google sign-in failed/.test(await page.locator('[data-id=account-note]').innerText()) && await btn() === 'Guest'
    && (await auth()).userId === first.userId && !(await auth()).google, 'a rejected Google sign-in shows an error and keeps the guest');
  await googleIn('alice@example.com');
  const linked = await auth();
  check(linked.userId === first.userId && linked.google && linked.google.email === 'alice@example.com', 'Google links to the guest player (same player id)');
  check(/Signed in with Google as alice@example\.com/.test(await sheetText()), 'the sheet shows the Google account');
  check(/linked/i.test(await page.locator('[data-id=account-note]').innerText()), 'the note says the account was linked');
  check(await btn() === 'Google', 'the header shows Google');
  check(await act('fake-google').count() === 0 && await act('account-signout').count() === 1, 'once linked: no Google button, a Sign out button');
  await closeSheet();

  // Still linked after a reload (the session refresh reports the Google link).
  await t.reload(); await settled();
  check(await btn() === 'Google' && (await auth()).userId === first.userId, 'a reload keeps the Google player');

  // Second device: no auth storage, so a new guest; Google brings back the first player.
  await page.evaluate((k) => localStorage.removeItem(k), AUTH_KEY);
  await t.reload(); await settled();
  const second = await auth();
  check(second.userId !== first.userId && !second.google, 'a fresh device starts as a new guest');
  await page.evaluate(() => { MonStackAuth.config.googleClientId = 'test-client.apps.googleusercontent.com'; });
  await openSheet();
  await googleIn('alice@example.com');
  check((await auth()).userId === first.userId, 'Google on the second device signs in to the same player');
  check(/Signed in to your Google account/.test(await page.locator('[data-id=account-note]').innerText()), 'the note says it switched to the Google account\'s player');

  // Sign out: a new guest; Google again brings the player back.
  await act('account-signout').click();
  await settled();
  const out = await auth();
  check(out && out.userId !== first.userId && !out.google && await btn() === 'Guest', 'sign out leaves a new guest player');
  await googleIn('alice@example.com');
  check((await auth()).userId === first.userId, 'signing in with Google again gets the same player back');

  // A different Google account on a guest links to that guest (a new Google player).
  await act('account-signout').click(); await settled();
  const guest = (await auth()).userId;
  await googleIn('bob@example.com');
  check((await auth()).userId === guest && /bob@example\.com/.test(await sheetText()), 'a new Google account links to the current guest');
  await closeSheet();

  // A session the server no longer accepts: dropped, a new guest.
  await page.evaluate((k) => { const a = JSON.parse(localStorage.getItem(k)); a.sessionToken = 'revoked'; localStorage.setItem(k, JSON.stringify(a)); }, AUTH_KEY);
  await t.reload(); await settled();
  const fresh = await auth();
  check(fresh.userId !== guest && !fresh.google && await btn() === 'Guest', 'a rejected session starts a new guest');

  // Server unreachable: the game plays, the account is Offline, the stored session is kept for next time.
  ugs.up = false;
  await t.reload(); await settled();
  check(await btn() === 'Offline', 'with the server down the header shows Offline');
  check((await auth()).userId === fresh.userId, 'a network failure keeps the stored session');
  await openSheet();
  check(await page.locator('[data-id=account-error]').count() === 1 && await act('account-retry').count() === 1, 'the sheet shows the error and a Try again button');
  ugs.up = true;
  await act('account-retry').click(); await settled();
  check(await btn() === 'Guest' && (await auth()).userId === fresh.userId, 'Try again signs back in to the same player');
  await closeSheet();

  // The game still plays with an account: start a run.
  await act('start').click();
  await page.locator('[data-act^="drill-"]').first().waitFor();
  check(true, 'a run starts with the account button in the header');
  await t.layout('run screen with account button');

  await t.finish();
})().catch((e) => { console.error(e); process.exit(1); });
