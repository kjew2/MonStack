// The account button in the header and its sheet (issue #19, specs/08 section 4): guest or Google, the Google sign-in
// button (Google Identity Services, loaded only when the sheet opens), sign out. Account logic is in web/auth.js.
'use strict';
(function () {
  const A = window.MonStackAuth;
  if (!A) return;
  const GIS_SRC = 'https://accounts.google.com/gsi/client';
  const $ = (id) => document.getElementById(id);
  let gis = null;   // promise of window.google.accounts.id
  let gisReady = false;   // initialize() once per page

  function el(tag, attrs, kids) {
    const e = document.createElement(tag);
    Object.entries(attrs || {}).forEach(([k, v]) => {
      if (v == null || v === false) return;
      if (k === 'text') e.textContent = v;
      else if (k.startsWith('on')) e.addEventListener(k.slice(2), v);
      else e.setAttribute(k, v === true ? '' : v);
    });
    (kids || []).forEach((c) => c && e.append(c));
    return e;
  }

  function label(s) {
    if (s.google) return 'Google';
    if (s.status === 'ready') return 'Guest';
    if (s.status === 'signing-in') return '...';
    return 'Offline';
  }

  function loadGis() {
    if (window.google && google.accounts && google.accounts.id) return Promise.resolve(google.accounts.id);
    if (gis) return gis;
    gis = new Promise((resolve, reject) => {
      const s = el('script', { src: GIS_SRC, async: true });
      s.onload = () => (window.google && google.accounts && google.accounts.id ? resolve(google.accounts.id) : reject(new Error('no google.accounts.id')));
      s.onerror = () => reject(new Error('blocked'));
      document.head.append(s);
    }).catch((e) => { gis = null; throw e; });
    return gis;
  }

  let note = null;   // a one-off message under the status: {text, cls}
  function sheet() { return $('account-sheet'); }
  function close() { const s = sheet(); if (s) s.remove(); note = null; }

  async function onCredential(resp) {
    note = null;
    const r = await A.google(resp && resp.credential);
    note = r.ok ? { text: r.switched ? 'Signed in to your Google account\'s player.' : 'Google account linked to this player.', cls: 'ok' }
      : { text: 'Google sign-in failed: ' + r.error, cls: 'bad' };
    draw();
  }

  function googleButton(box) {
    const cfg = A.config;
    if (!cfg.googleClientId) {
      box.append(el('p', { class: 'small muted', 'data-id': 'google-off', text: 'Google sign-in is not set up for this copy of the game yet.' }));
      return;
    }
    const slot = el('div', { 'data-id': 'google-button', class: 'google-slot' });
    box.append(slot);
    loadGis().then((id) => {
      if (!gisReady) { id.initialize({ client_id: cfg.googleClientId, callback: onCredential, ux_mode: 'popup', auto_select: false, cancel_on_tap_outside: true }); gisReady = true; }
      id.renderButton(slot, { type: 'standard', theme: 'outline', size: 'large', text: 'signin_with', shape: 'pill', logo_alignment: 'left' });
    }).catch(() => {
      slot.replaceWith(el('p', { class: 'small muted', 'data-id': 'google-off',
        text: 'Google sign-in cannot load here. Open the game at kjew2.github.io/MonStack to sign in.' }));
    });
  }

  function draw() {
    const s = A.state();
    const b = $('account-btn');
    if (b) { b.textContent = label(s); b.setAttribute('aria-label', 'Account: ' + label(s)); }
    const box = sheet();
    if (!box) return;
    box.replaceChildren();
    box.append(el('div', { class: 'row between' }, [el('h2', { text: 'Account' }),
      el('button', { class: 'btn small', type: 'button', 'data-act': 'account-close', text: 'Close', onclick: close })]));
    let who;
    if (s.google) who = 'Signed in with Google' + (s.google.email ? ' as ' + s.google.email : '') + '.';
    else if (s.status === 'ready') who = 'Playing as a guest. Sign in with Google to keep this player when you change devices.';
    else if (s.status === 'signing-in') who = 'Signing in...';
    else who = 'Not signed in.';
    box.append(el('p', { 'data-id': 'account-status', text: who }));
    if (s.error) box.append(el('p', { class: 'small bad', 'data-id': 'account-error', role: 'status', text: s.error }));
    if (note) box.append(el('p', { class: 'small ' + note.cls, 'data-id': 'account-note', role: 'status', text: note.text }));
    if (!s.google) googleButton(box);
    if (s.status !== 'ready' && s.status !== 'signing-in') {
      box.append(el('button', { class: 'btn small', type: 'button', 'data-act': 'account-retry', text: 'Try again', disabled: s.busy, onclick: () => A.start() }));
    }
    if (s.google) {
      box.append(el('button', { class: 'btn small', type: 'button', 'data-act': 'account-signout', text: 'Sign out', disabled: s.busy,
        onclick: async () => { note = { text: 'Signed out. This device plays as a new guest; sign in with Google to get your player back.', cls: 'muted' }; await A.signOut(); draw(); } }));
    }
    box.append(el('p', { class: 'small muted', 'data-id': 'account-local', text: 'Your monsters and progress are saved on this device only for now. Cloud save comes next.' }));
    if (s.userId) box.append(el('p', { class: 'small muted num', 'data-id': 'account-id', text: 'Player id ' + s.userId }));
  }

  function open() {
    if (sheet()) return;
    $('app').append(el('div', { id: 'account-sheet', role: 'dialog', 'aria-modal': 'true', 'aria-label': 'Account' }));
    draw();
  }

  function init() {
    const top = $('top');
    if (!top) return;
    top.append(el('button', { id: 'account-btn', class: 'link-btn', type: 'button', 'data-act': 'account', onclick: open }));
    document.addEventListener('keydown', (e) => { if (e.key === 'Escape' && sheet()) close(); });
    A.onChange(draw);
    draw();
    A.start();
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();
})();
