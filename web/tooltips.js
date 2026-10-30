// Tap-to-open tooltips ("toggletips") for stats and skill-tree abilities. No game rule lives here: every word and number comes
// from the C# engine (RunContent: statInfo, nodeTips, skillNode; RunState: statNow, nowTitle), see web/wasm/BRIDGE.md, "Tooltips".
//
//   Tips.init(() => content, () => runState)       once, after the engine loaded
//   h('span', { text: 'STR', ...Tips.attrs('stat:str') })
//
// Refs: 'stat:<id>' (the stat, with the monster's "now" line), 'stat:<id>:vs' (also the rank opponent's), 'node:<nodeId>',
// 'skill:<skillId>' (the tree node that grants the skill). Tap, Enter or Space opens one; a long press opens it too; tapping
// elsewhere, Escape, scrolling or resizing closes it. One shared role="tooltip" element; the open trigger gets aria-describedby.
(function (root) {
  'use strict';
  const ID = 'tip';
  const LONG_PRESS_MS = 450;
  let getContent = () => null, getState = () => null;
  let box = null, anchor = null, press = null, swallowClick = false, openedAt = 0;

  function el(tag, cls, text) {
    const e = document.createElement(tag);
    if (cls) e.className = cls;
    if (text !== undefined && text !== null) e.textContent = text;
    return e;
  }

  /** Attributes that make an element a tooltip trigger. */
  function attrs(ref) {
    return { 'data-tip': ref, role: 'button', tabindex: '0', 'aria-expanded': 'false' };
  }

  function nowBlock(who, title, line) {
    const d = el('div', 'tip-now');
    const head = el('div', 'tip-sub');
    head.append(el('b', null, who), document.createTextNode(' · ' + title));
    d.append(head, el('div', null, line));
    return d;
  }

  function statContent(C, S, id, vs) {
    const info = C.statInfo && C.statInfo[id];
    if (!info) return null;
    const f = document.createDocumentFragment();
    f.append(el('div', 'tip-title', id.toUpperCase() + ' · ' + info.name), el('p', null, info.desc));
    if (S && S.statNow) {
      const sp = (C.species || []).find((x) => x.id === S.species);
      f.append(nowBlock(sp ? sp.name : S.species, S.nowTitle, S.statNow[id]));
      if (vs && S.opponent && S.opponent.statNow) f.append(nowBlock(S.opponent.name, S.opponent.nowTitle, S.opponent.statNow[id]));
    }
    return f;
  }

  function nodeContent(C, nodeId) {
    const t = C.nodeTips && C.nodeTips[nodeId];
    if (!t) return null;
    const f = document.createDocumentFragment();
    f.append(el('div', 'tip-title', t.title));
    if (t.tags.length) {
      const tags = el('div', 'tip-tags');
      t.tags.forEach((x) => tags.append(el('span', 'tag', x)));
      f.append(tags);
    }
    if (t.text) f.append(el('p', null, t.text));
    if (t.facts.length) {
      const dl = el('dl', 'tip-facts');
      t.facts.forEach(([k, v]) => dl.append(el('dt', null, k), el('dd', null, v)));
      f.append(dl);
    }
    return f;
  }

  function content(ref) {
    const C = getContent();
    if (!C) return null;
    const [kind, id, mode] = String(ref).split(':');
    if (kind === 'stat') return statContent(C, getState(), id, mode === 'vs');
    if (kind === 'node') return nodeContent(C, id);
    if (kind === 'skill') return C.skillNode && C.skillNode[id] ? nodeContent(C, C.skillNode[id]) : null;
    return null;
  }

  function open(a) {
    close();
    const c = content(a.getAttribute('data-tip'));
    if (!c) return;
    box.replaceChildren(c);
    box.hidden = false;
    box.scrollTop = 0;
    anchor = a;
    openedAt = Date.now();
    a.setAttribute('aria-expanded', 'true');
    a.setAttribute('aria-describedby', ID);
    place();
  }

  function close() {
    if (!anchor) return;
    box.hidden = true;
    anchor.setAttribute('aria-expanded', 'false');
    anchor.removeAttribute('aria-describedby');
    anchor = null;
  }

  /** Below the trigger, or above when it does not fit; always inside the viewport with a 12px margin. */
  function place() {
    const M = 12, GAP = 6;
    const vw = document.documentElement.clientWidth, vh = window.innerHeight;
    box.style.maxWidth = Math.min(360, vw - 2 * M) + 'px';
    box.style.maxHeight = (vh - 2 * M) + 'px';
    box.style.left = '0px'; box.style.top = '0px';
    const r = anchor.getBoundingClientRect(), b = box.getBoundingClientRect();
    const left = Math.max(M, Math.min(r.left + r.width / 2 - b.width / 2, vw - M - b.width));
    let top = r.bottom + GAP;
    if (top + b.height > vh - M) top = r.top - GAP - b.height >= M ? r.top - GAP - b.height : Math.max(M, vh - M - b.height);
    box.style.left = Math.round(left) + 'px';
    box.style.top = Math.round(top) + 'px';
  }

  const trigger = (t) => (t && t.closest ? t.closest('[data-tip]') : null);

  function onClick(e) {
    if (swallowClick) { swallowClick = false; if (trigger(e.target)) { e.preventDefault(); e.stopPropagation(); return; } }
    if (box.contains(e.target)) return;
    const a = trigger(e.target);
    if (a) { if (a === anchor) close(); else open(a); return; }
    close();                                   // tap elsewhere: close, and let the tap do what it does
  }

  function onKey(e) {
    if (e.key === 'Escape' && anchor) { const a = anchor; close(); a.focus(); return; }
    if ((e.key === 'Enter' || e.key === ' ') && e.target.matches && e.target.matches('[data-tip]')) {
      e.preventDefault();
      if (e.target === anchor) close(); else open(e.target);
    }
  }

  function onPointerDown(e) {
    swallowClick = false;
    const a = trigger(e.target);
    if (!a) return;
    const x = e.clientX, y = e.clientY;
    clearTimeout(press && press.timer);
    press = { x, y, timer: setTimeout(() => { press = null; open(a); swallowClick = true; }, LONG_PRESS_MS) };
  }
  function cancelPress(e) {
    if (!press) return;
    if (e.type === 'pointermove' && Math.abs(e.clientX - press.x) < 10 && Math.abs(e.clientY - press.y) < 10) return;
    clearTimeout(press.timer);
    press = null;
  }

  // Scrolling the page closes the tooltip; a scroll right after opening (the browser bringing the tapped trigger into view) moves it.
  function onScroll(e) {
    if (!anchor || box.contains(e.target)) return;
    if (Date.now() - openedAt < 300) place(); else close();
  }

  function init(contentGetter, stateGetter) {
    getContent = contentGetter;
    getState = stateGetter || (() => null);
    if (box) return;
    box = el('div', 'tip');
    box.id = ID;
    box.setAttribute('role', 'tooltip');
    box.hidden = true;
    document.body.append(box);
    const style = el('style');
    style.textContent = CSS;
    document.head.append(style);
    document.addEventListener('click', onClick, true);
    document.addEventListener('keydown', onKey);
    document.addEventListener('pointerdown', onPointerDown, true);
    ['pointermove', 'pointerup', 'pointercancel'].forEach((t) => document.addEventListener(t, cancelPress, true));
    document.addEventListener('contextmenu', (e) => { if (trigger(e.target)) e.preventDefault(); }, true);
    document.addEventListener('scroll', onScroll, true);
    document.addEventListener('focusout', (e) => { if (e.target === anchor && e.relatedTarget && !box.contains(e.relatedTarget)) close(); });
    window.addEventListener('resize', close);
  }

  // Kept here, not in index.html, so the tooltip module is one file. Uses the page's colour tokens.
  const CSS = [
    '[data-tip] { cursor: help; text-decoration: underline dotted; text-underline-offset: 3px; text-decoration-color: var(--muted, #9e9e9e);',
    '  -webkit-touch-callout: none; -webkit-user-select: none; user-select: none; }',
    '[data-tip]:focus-visible { outline: 2px solid var(--gems, #4dd0e1); outline-offset: 2px; border-radius: 3px; }',
    '[data-tip][aria-expanded="true"] { text-decoration-style: solid; text-decoration-color: var(--gems, #4dd0e1); }',
    '.stat .lbl[data-tip] { padding: 10px 0; margin: -10px 0; }',
    '.alog li[data-tip] { text-decoration: none; }',
    '.alog li[data-tip]::after { content: " \\24D8"; color: var(--muted, #9e9e9e); }',
    '.tip { position: fixed; z-index: 30; box-sizing: border-box; overflow-y: auto; padding: 10px 12px; border-radius: 8px;',
    '  background: var(--panel-2, #262626); color: var(--text, #eee); border: 1px solid var(--gems, #4dd0e1);',
    '  box-shadow: 0 6px 20px rgba(0,0,0,.6); font-size: 13px; line-height: 1.4; display: flex; flex-direction: column; gap: 6px; }',
    '.tip[hidden] { display: none; }',
    '.tip p { margin: 0; }',
    '.tip-title { font: 600 15px var(--font-display, system-ui); }',
    '.tip-tags { display: flex; flex-wrap: wrap; gap: 4px; }',
    '.tip-sub { font-size: 12px; color: var(--muted, #9e9e9e); }',
    '.tip-now { border-top: 1px solid var(--line, #333); padding-top: 6px; }',
    '.tip-facts { margin: 0; display: grid; grid-template-columns: auto 1fr; gap: 3px 10px; font-size: 12px; }',
    '.tip-facts dt { color: var(--muted, #9e9e9e); }',
    '.tip-facts dd { margin: 0; }',
  ].join('\n');

  root.Tips = { init: init, attrs: attrs, open: open, close: close };
})(typeof self !== 'undefined' ? self : this);
