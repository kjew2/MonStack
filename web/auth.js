// Player accounts (issue #19, specs/08 section 12): Unity Authentication through its REST API, no Unity SDK.
// - Anonymous sign-in on first launch, so the game plays with no account; the session token is kept on the device and
//   refreshed on later launches (one UGS player per device until it is linked).
// - Google: a Google ID token (Google Identity Services, web/account.js) is linked to the current player. If that Google
//   account already belongs to another player (the second-device case), it signs in to that player instead.
// - Local-first: nothing here ever blocks play. Every failure leaves the game running and shows in the account sheet.
// The save stays on the device; uploading it is cloud save (#25).
'use strict';
(function () {
  const CONFIG = {
    // Public identifiers (not secrets): the UGS project and environment, and the Google OAuth web client id. With no Google
    // client id the Google button says sign-in is not set up (specs/08 section 12.3).
    ugsProjectId: 'e83a4b44-27a7-4f76-8769-08380b340736',
    ugsEnvironment: 'production',
    googleClientId: '',
    authBase: 'https://player-auth.services.api.unity.com/v1',
  };
  const KEY = 'monstack-auth-v1';             // {userId, sessionToken, google: {email} | null}
  const REFRESH_MARGIN_MS = 60 * 1000;        // refresh the id token this long before it expires
  const TIMEOUT_MS = 10 * 1000;

  let saved = load();
  let idToken = null, idTokenExp = 0;         // memory only
  let status = 'off';                          // off | signing-in | ready | error
  let error = null, busy = null;
  const listeners = [];

  function load() {
    try {
      const v = JSON.parse(localStorage.getItem(KEY) || 'null');
      return v && typeof v.userId === 'string' && typeof v.sessionToken === 'string' ? v : null;
    } catch (e) { return null; }
  }
  function store() {
    try { if (saved) localStorage.setItem(KEY, JSON.stringify(saved)); else localStorage.removeItem(KEY); } catch (e) { /* storage blocked: session lasts this page */ }
  }
  function emit() { listeners.forEach((f) => { try { f(state()); } catch (e) { console.warn('auth listener', e); } }); }
  function set(st, err) { status = st; error = err || null; emit(); }

  function state() {
    return {
      status, error,
      busy: !!busy,
      userId: saved ? saved.userId : null,
      google: saved && saved.google ? { email: saved.google.email || '' } : null,
      googleReady: !!CONFIG.googleClientId,
    };
  }

  class AuthError extends Error { constructor(msg, status, title) { super(msg); this.status = status; this.title = title; } }

  async function call(path, body, bearer) {
    const headers = { 'Content-Type': 'application/json', ProjectId: CONFIG.ugsProjectId, UnityEnvironment: CONFIG.ugsEnvironment };
    if (bearer) headers.Authorization = 'Bearer ' + bearer;
    const ctl = typeof AbortController === 'function' ? new AbortController() : null;
    const timer = ctl ? setTimeout(() => ctl.abort(), TIMEOUT_MS) : null;
    let r;
    try {
      r = await fetch(CONFIG.authBase + path, { method: 'POST', headers, body: JSON.stringify(body || {}), signal: ctl ? ctl.signal : undefined });
    } catch (e) {
      throw new AuthError('Could not reach the account server. You can keep playing; your progress is saved on this device.', 0, 'NETWORK');
    } finally { if (timer) clearTimeout(timer); }
    let j = null;
    try { j = await r.json(); } catch (e) { /* not JSON */ }
    if (!r.ok) throw new AuthError((j && (j.detail || j.title)) || ('HTTP ' + r.status), r.status, j && j.title);
    return j;
  }

  // A sign-in response: {idToken, sessionToken, expiresIn, userId, user: {externalIds: [{providerId, externalId}]}}.
  function accept(j, google) {
    if (!j || !j.userId || !j.sessionToken || !j.idToken) throw new AuthError('The account server sent an incomplete answer.', 0, 'BAD_RESPONSE');
    const linked = !!(j.user && Array.isArray(j.user.externalIds) && j.user.externalIds.some((x) => x && x.providerId === 'google.com'));
    const keep = saved && saved.userId === j.userId && saved.google ? saved.google : null;
    saved = { userId: j.userId, sessionToken: j.sessionToken, google: google || (linked ? keep || { email: '' } : null) };
    idToken = j.idToken;
    idTokenExp = Date.now() + Math.max(0, (j.expiresIn || 0) * 1000 - REFRESH_MARGIN_MS);
    store();
  }

  // One sign-in at a time; a second caller waits for the first.
  function exclusive(fn) {
    if (busy) return busy;
    busy = (async () => {
      try { return await fn(); } finally { busy = null; emit(); }
    })();
    emit();
    return busy;
  }

  // First launch: a new anonymous player. Later launches: the stored session, refreshed. A session the server no longer
  // accepts (401) is dropped and a fresh anonymous player started; a network error keeps the stored session for next time.
  function start() {
    if (!CONFIG.ugsProjectId) return Promise.resolve(state());
    return exclusive(async () => {
      set('signing-in');
      try {
        if (saved) {
          try { accept(await call('/authentication/session-token', { sessionToken: saved.sessionToken })); set('ready'); return state(); }
          catch (e) { if (e.status !== 401 && e.status !== 400) throw e; saved = null; store(); }
        }
        accept(await call('/authentication/anonymous', {}));
        set('ready');
      } catch (e) { set('error', e.message); }
      return state();
    });
  }

  async function token() {
    if (idToken && Date.now() < idTokenExp) return idToken;
    while (busy) await busy.catch(() => {});
    if (!(idToken && Date.now() < idTokenExp)) await start();
    if (status !== 'ready') throw new AuthError(error || 'Not signed in.', 0, 'NOT_SIGNED_IN');
    return idToken;
  }

  // The email in a Google ID token, for display only (the server verifies the token; nothing here trusts it).
  function googleEmail(jwt) {
    try {
      const p = jwt.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
      const json = decodeURIComponent(atob(p + '='.repeat((4 - p.length % 4) % 4)).split('').map((c) => '%' + c.charCodeAt(0).toString(16).padStart(2, '0')).join(''));
      return String(JSON.parse(json).email || '');
    } catch (e) { return ''; }
  }

  // A Google credential from the sign-in button. Link it to this device's player; if that fails because the Google account
  // already has a player (signed in on another device), sign in to that player instead (signInOnly: no new player).
  // With no player yet (the first sign-in failed, e.g. offline), sign in with Google directly.
  async function google(credential) {
    while (busy) await busy.catch(() => {});
    return exclusive(async () => {
      const g = { email: googleEmail(credential) };
      set('signing-in');
      try {
        let bearer = null;
        if (saved) {
          try { bearer = idToken && Date.now() < idTokenExp ? idToken : (accept(await call('/authentication/session-token', { sessionToken: saved.sessionToken })), idToken); }
          catch (e) { if (e.status !== 401 && e.status !== 400) throw e; saved = null; store(); }
        }
        if (bearer) {
          let linkErr = null;
          try {
            await call('/authentication/link/google.com', { token: credential }, bearer);   // answers with the user, no new tokens
            saved.google = g; store();
            set('ready');
            return { ok: true, switched: false, state: state() };
          } catch (e) { if (e.title === 'NETWORK') throw e; linkErr = e; }
          // Linking failed, most likely because the Google account already has a player: sign in to it (signInOnly makes no
          // new player). If that fails too, the link error is the one to show.
          const before = saved.userId;
          try { accept(await call('/authentication/external-token/google.com', { token: credential, signInOnly: true }), g); }
          catch (e) { throw e.title === 'NETWORK' ? e : linkErr; }
          set('ready');
          return { ok: true, switched: saved.userId !== before, state: state() };
        }
        accept(await call('/authentication/external-token/google.com', { token: credential, signInOnly: false }), g);
        set('ready');
        return { ok: true, switched: true, state: state() };
      } catch (e) {
        set(idToken && Date.now() < idTokenExp ? 'ready' : 'error', e.message);   // still signed in only if this player's token is live
        return { ok: false, error: e.message, state: state() };
      }
    });
  }

  // Forget this device's session. The next start() makes a new anonymous player; signing in with Google again gets the
  // linked player back. The local save is not touched.
  async function signOut() {
    while (busy) await busy.catch(() => {});
    saved = null; idToken = null; idTokenExp = 0; store();
    set('off');
    return start();
  }

  window.MonStackAuth = {
    config: CONFIG,
    state, start, token, google, signOut,
    onChange: (f) => { listeners.push(f); return () => { const i = listeners.indexOf(f); if (i >= 0) listeners.splice(i, 1); }; },
    _googleEmail: googleEmail,
  };
})();
