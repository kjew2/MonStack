// Gambit rulebook editor (specs/28 section 4.5): one Vault monster's rulebook as a list of rule cards, IF [condition] [value]
// THEN [action] AT [target], each chip opening a picker sheet; up and down buttons reorder (no drag, one thumb). A Test button
// (when the caller passes an Endless floor) fights that floor with the draft through the engine's read-only preview and plays it
// back, lighting the card of the rule that chose each of this monster's actions.
// UI only: the vocabulary comes from the content (C.gambits), the engine checks and stores the text (run.setRulebook) and
// fights the preview (run.endlessPreview). This file only splits and joins the rulebook text (specs/28 section 7) for display.
//
//   RulebookView.open(ctx, vaultIndex, opts)   ctx = { run, C, h, toast, sprite, TIP, up, num, onChange }
//                                              opts = { season, team: [Vault indices], floor } or null (no Test button)
//   RulebookView.close(force)                  asks first when the draft has unsaved changes, unless force
//   RulebookView.isOpen(), RulebookView.lastCtx
(function () {
  'use strict';
  const STEP_MS = 250;          // playback pace, one action per step (a placeholder like FIGHT_STEP_MS)
  const AUTO = 'auto', ALWAYS = 'always', BASIC = 'basic';

  let st = null;                // the open editor, or null
  let lastCtx = null;

  // ------------------------------------------------------------ rulebook text (specs/28 section 7; GambitText.cs)
  // Splits saved text into rule objects { when, value, action, target }. The engine only stores text it accepted, so a part that
  // does not split cleanly is kept as a raw rule the player can only delete.
  function parse(text) {
    const rules = [];
    String(text || '').split('|').forEach((part) => {
      if (!part) return;
      const gt = part.indexOf('>');
      if (gt < 0) { rules.push({ raw: part, when: ALWAYS, value: null, action: BASIC, target: AUTO }); return; }
      const cond = part.slice(0, gt), act = part.slice(gt + 1);
      const colon = cond.indexOf(':'), at = act.indexOf('@');
      const val = colon < 0 ? '' : cond.slice(colon + 1);
      rules.push({
        when: colon < 0 ? cond : cond.slice(0, colon),
        value: val === '' || isNaN(Number(val)) ? null : Number(val),
        action: at < 0 ? act : act.slice(0, at),
        target: at < 0 || at === act.length - 1 ? AUTO : act.slice(at + 1),
      });
    });
    return rules;
  }
  // The canonical text: no @auto, no value on always, values as plain decimals (0.4).
  function format(rules) {
    return rules.map((r) => {
      if (r.raw !== undefined) return r.raw;
      const c = cond(r.when);
      return r.when + (c && c.value && r.value !== null ? ':' + String(r.value) : '') + '>' + r.action + (r.target && r.target !== AUTO ? '@' + r.target : '');
    }).join('|');
  }

  // ------------------------------------------------------------ vocabulary
  const G = () => st.ctx.C.gambits;
  const cond = (id) => G().conditions.find((c) => c.id === id) || null;
  const targetDef = (id) => G().targets.find((t) => t.id === id) || null;
  const entry = () => st.ctx.run.account.vault[st.vi] || null;
  // A skill's name: its skill tooltip's title, else the title of the tree node that grants it, else its id.
  function skillName(id) {
    const C = st.ctx.C;
    if (id === BASIC) return G().basic.name;
    if (C.skillTips && C.skillTips[id]) return C.skillTips[id].title;
    const node = C.skillNode && C.skillNode[id], tip = node && C.nodeTips && C.nodeTips[node];
    return tip ? tip.title : id;
  }
  // The tooltip ref for a skill: its own tooltip, else the node that grants it (web/tooltips.js resolves both).
  const skillTip = (id) => (st.ctx.C.skillTips && st.ctx.C.skillTips[id] ? 'mskill:' + id : 'skill:' + id);
  const valueText = (c, v) => (v === null || v === undefined ? '?' : c && c.value === 'fraction' ? Math.round(v * 100) + '%' : String(v));
  const valueChoices = (c) => (c.value === 'fraction' ? [1, 2, 3, 4, 5, 6, 7, 8, 9, 10].map((k) => k / 10) : [1, 2, 3, 4, 5, 6]);
  const defaultValue = (c) => (c.value === 'fraction' ? 0.5 : c.value === 'count' ? 1 : null);
  // What is wrong with a saved rule the engine would not take now (a skill the monster lost, an unknown word), or null.
  function problem(r, e) {
    if (r.raw !== undefined) return 'This rule could not be read. Delete it.';
    if (!cond(r.when)) return 'Unknown condition "' + r.when + '". Pick another.';
    if (!targetDef(r.target)) return 'Unknown target "' + r.target + '". Pick another.';
    if (r.action !== BASIC && (e.skills || []).indexOf(r.action) < 0) return 'This monster no longer has ' + skillName(r.action) + ': fights skip this rule. Pick another action or delete it.';
    if (cond(r.when).value && r.value === null) return 'Pick a value.';
    return null;
  }

  // ------------------------------------------------------------ open and close
  function open(ctx, vaultIndex, opts) {
    if (st) close(true);
    lastCtx = ctx;
    const e = ctx.run.account.vault[vaultIndex];
    if (!e) { ctx.toast('No such monster in the Vault'); return; }
    const saved = e.rulebook || '';
    st = {
      ctx: ctx, vi: vaultIndex, opts: opts || null, key: e.id + ':' + e.sealedUtcMs + ':' + e.species,
      saved: '', rules: parse(saved),
      error: null, pick: null, confirm: false, play: null, focusBack: document.activeElement,
      el: null, body: null,
    };
    st.saved = format(st.rules);   // the canonical form of the saved text (format reads the vocabulary through st)
    const h = ctx.h;
    st.el = h('div', { class: 'rbsheet', role: 'dialog', 'aria-modal': 'true', 'aria-labelledby': 'rb-title', 'data-act': 'rb-sheet' });
    st.body = h('div', { class: 'rbbody' });
    st.el.append(st.body);
    document.getElementById('app').append(st.el);
    render();
    const first = st.el.querySelector('[data-act=rb-close]');
    if (first) first.focus();
  }

  const dirty = () => !!st && format(st.rules) !== st.saved;

  function close(force) {
    if (!st) return;
    if (!force && dirty() && entry() && entry().id >= 0) { st.confirm = true; st.pick = null; render(); const b = st.el.querySelector('[data-act=rb-discard]'); if (b) b.focus(); return; }
    stopPlay();
    const back = st.focusBack;
    st.el.remove();
    st = null;
    if (back && back.isConnected && back.focus) back.focus();
  }

  // Escape closes the open tooltip first (web/tooltips.js), then the picker, then the confirm row, then the sheet. Capture phase,
  // so the tooltip is still open when this runs and the account sheet's handler does not see the key.
  window.addEventListener('keydown', (ev) => {
    if (!st || ev.key !== 'Escape') return;
    const tip = document.getElementById('tip');
    if (tip && !tip.hidden) return;
    ev.stopPropagation();
    ev.preventDefault();
    if (st.pick) { const k = st.pick; st.pick = null; render(); focusChip(k); }
    else if (st.confirm) { st.confirm = false; render(); }
    else close();
  }, true);

  function focusChip(p) {
    const el = st && st.el.querySelector('[data-act="rb-' + p.kind + '-' + p.k + '"]');
    if (el) el.focus();
  }

  // ------------------------------------------------------------ editing
  function edit(fn) {
    fn();
    st.error = null;
    if (st.play) endPlay(true);   // the shown test no longer matches the cards
    render();
  }
  function addRule() {
    if (st.rules.length >= G().slots) return;
    edit(() => st.rules.push({ when: ALWAYS, value: null, action: BASIC, target: AUTO }));
  }
  function move(k, d) {
    const j = k + d;
    if (j < 0 || j >= st.rules.length) return;
    edit(() => { const t = st.rules[k]; st.rules[k] = st.rules[j]; st.rules[j] = t; });
    if (!st) return;
    const b = st.el.querySelector('[data-act="rb-' + (d < 0 ? 'up' : 'down') + '-' + j + '"]');
    if (b && !b.disabled) b.focus();
  }
  function pickValue(kind, k, v) {
    const r = st.rules[k];
    if (!r) return;
    edit(() => {
      if (kind === 'cond') {
        const was = cond(r.when), c = cond(v);
        r.when = v;
        if (!c || !c.value) r.value = null;
        else if (!was || was.value !== c.value || r.value === null) r.value = defaultValue(c);
      } else if (kind === 'value') r.value = v;
      else if (kind === 'action') r.action = v;
      else if (kind === 'target') r.target = v;
      st.pick = null;
    });
    focusChip({ kind: kind, k: k });
  }

  function save() {
    const e = entry();
    if (!e) return;
    const text = format(st.rules);
    const r = st.ctx.run.setRulebook(st.vi, text);
    if (!r.ok) { st.error = r.reason || 'The rulebook was not saved.'; render(); return; }
    const now = (st.ctx.run.account.vault[st.vi] || {}).rulebook || '';
    st.saved = format(parse(now));
    st.rules = parse(now);
    st.error = null;
    st.confirm = false;
    const ctx = st.ctx;
    render();
    ctx.toast(now ? 'Rulebook saved' : 'Rulebook cleared');
    if (ctx.onChange) ctx.onChange();
  }

  // ------------------------------------------------------------ test preview and playback
  const reducedMotion = () => !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
  function test() {
    const o = st.opts;
    if (!o) return;
    stopPlay();
    st.error = null;
    const slot = o.team.indexOf(st.vi);
    if (slot < 0) { st.play = { error: 'This monster is not in the team being tested.' }; render(); return; }
    const books = o.team.map((v, k) => (k === slot ? format(st.rules) : null));
    let f;
    try { f = st.ctx.run.endlessPreview(o.season, o.team, o.floor, books); }
    catch (ex) { f = { ok: false, reason: ex.message }; }
    if (!f || f.ok === false) { st.play = { error: (f && f.reason) || 'The test could not run.' }; render(); return; }
    const me = f.units.find((u) => u.vault === st.vi && !u.add);
    st.play = { f: f, me: me ? me.id : null, i: 0, lit: null, timer: 0, done: false, refs: null };
    render();
    if (reducedMotion()) { endPlay(false); return; }
    st.play.timer = setInterval(stepPlay, STEP_MS);
  }
  function stopPlay() { if (st && st.play && st.play.timer) { clearInterval(st.play.timer); st.play.timer = 0; } }
  function endPlay(rerender) {
    const p = st.play;
    if (!p || !p.f) return;
    stopPlay();
    p.i = p.f.actions.length;
    p.lit = null;
    p.done = true;
    if (rerender) return;   // the caller renders
    render();
  }
  // The acting rule after action i: a gambit event by this monster lights its rule; it stays lit through the action it chose
  // (one or more entries by this monster in a row, counters and events aside) and goes out at this monster's next action.
  function litAfter(f, me, n) {
    let lit = null, armed = false, lastActor = null;
    for (let j = 0; j < n; j++) {
      const x = f.actions[j];
      if (x.event === 'gambit') { if (x.actor === me) { lit = x.amount; armed = true; } continue; }
      if (x.event || x.counter) continue;
      if (x.actor === me) {
        if (armed) armed = false;
        else if (lastActor !== me) lit = null;   // a new action of this monster that no rule chose
      }
      lastActor = x.actor;
    }
    return lit;
  }
  function stepPlay() {
    const p = st && st.play;
    if (!p || !p.f) return;
    if (p.i >= p.f.actions.length) { endPlay(false); return; }
    p.i++;
    p.lit = litAfter(p.f, p.me, p.i);
    paintPlay();
  }
  // Updates the playback panel and the lit card in place (no re-render while it plays).
  function paintPlay() {
    const p = st.play, d = p && p.refs;
    if (!d) return;
    const f = p.f, x = p.i ? f.actions[p.i - 1] : null;
    f.units.forEach((u, k) => {
      const r = d.units[k];
      const here = x ? k < x.hp.length : !u.add;
      const hp = p.done ? u.hp : x ? (here ? x.hp[k] : u.maxHp) : u.maxHp;
      r.row.hidden = !here && !p.done;
      r.fill.style.width = Math.max(0, Math.min(100, (hp / Math.max(1, u.maxHp)) * 100)) + '%';
      r.text.textContent = st.ctx.num(Math.max(0, hp)) + '/' + st.ctx.num(u.maxHp);
      r.row.classList.toggle('down', hp <= 0);
      r.row.classList.toggle('acting', !p.done && !!x && x.actor === u.id);
    });
    d.now.textContent = p.done ? '' : nowText(f, x);
    d.progress.textContent = p.done ? f.actions.length + ' actions' : 'Action ' + p.i + ' of ' + f.actions.length;
    d.skip.hidden = p.done;
    d.result.hidden = !p.done;
    st.el.querySelectorAll('.rbcard').forEach((c, k) => c.classList.toggle('lit', p.lit === k));
  }
  function nowText(f, x) {
    if (!x) return 'Fight!';
    const name = (id) => { const u = f.units.find((v) => v.id === id); return u ? u.name : id || ''; };
    if (x.event === 'gambit') return name(x.actor) + ': rule ' + (x.amount + 1);
    if (x.event) return name(x.actor) + ': ' + (x.skill || x.event.replace(/^[a-z]+:/, '').replace(/_/g, ' '));
    return name(x.actor) + ' · ' + (x.skill || G().basic.name) + (x.target ? ' → ' + name(x.target) : '') +
      (x.evaded ? ': evaded' : x.amount ? ': ' + st.ctx.num(x.amount) : '');
  }

  // ------------------------------------------------------------ drawing
  function render() {
    if (!st) return;
    const e = entry();
    const ctx = st.ctx, h = ctx.h;
    // The Vault changed under the sheet (the entry is gone or another monster sits at that index): close it.
    if (!e || e.id + ':' + e.sealedUtcMs + ':' + e.species !== st.key) { close(true); return; }
    const slots = G().slots, n = st.rules.length;
    const kids = [];
    kids.push(h('div', { class: 'row between rbhead' }, [
      h('div', { class: 'row', style: 'min-width:0' }, [ctx.sprite(e.species, e.apex, 'small'),
        h('div', { style: 'min-width:0' }, [h('h2', { id: 'rb-title', text: 'Rulebook' }),
          h('div', { class: 'small muted', 'data-id': 'rb-count' }, [h('b', { text: e.name }), document.createTextNode(' · ' + n + ' / ' + slots + ' rules')])])]),
      h('button', { class: 'btn small', type: 'button', 'data-act': 'rb-close', text: 'Close', onclick: () => close() }),
    ]));
    if (!(e.id >= 0)) {
      kids.push(h('p', { class: 'card small', 'data-id': 'rb-noid', text: 'This monster was sealed before monsters had ids and cannot hold a rulebook.' }));
      st.body.replaceChildren(...kids);
      return;
    }
    kids.push(h('p', { class: 'small muted rbhelp', text: 'Rules are checked from the top; the first one that can act now is used. A skill named in any rule is used only by its rules. When no rule fires, the monster acts as usual.' }));
    if (st.error) kids.push(h('p', { class: 'card small rberr', role: 'alert', 'data-act': 'rb-error', text: st.error }));
    if (st.play) kids.push(playPanel());
    const list = h('ol', { class: 'rblist', 'data-id': 'rb-list' });
    st.rules.forEach((r, k) => list.append(ruleCard(r, k, e)));
    kids.push(n ? list : h('p', { class: 'small muted', 'data-id': 'rb-empty', text: 'No rules: this monster fights on its usual AI. Add a rule to steer it.' }));
    kids.push(h('div', { class: 'row rbtools' }, [
      h('button', { class: 'btn small', type: 'button', 'data-act': 'rb-add', disabled: n >= slots, text: n >= slots ? 'All ' + slots + ' rules used' : '+ Add rule', onclick: addRule }),
      h('button', { class: 'btn small', type: 'button', 'data-act': 'rb-clear', disabled: !n, text: 'Clear all', onclick: () => edit(() => { st.rules = []; }) }),
      st.opts ? h('button', { class: 'btn small gemsy', type: 'button', 'data-act': 'rb-test', text: 'Test floor ' + st.opts.floor, onclick: test }) : null,
    ]));
    if (st.confirm) {
      kids.push(h('div', { class: 'card alert', 'data-id': 'rb-confirm' }, [
        h('p', { class: 'small', text: 'Close without saving? The changes are lost.' }),
        h('div', { class: 'row' }, [
          h('button', { class: 'btn small warn', type: 'button', 'data-act': 'rb-discard', text: 'Discard changes', onclick: () => close(true) }),
          h('button', { class: 'btn small', type: 'button', 'data-act': 'rb-keep', text: 'Keep editing', onclick: () => { st.confirm = false; render(); } }),
        ]),
      ]));
    }
    kids.push(h('button', { class: 'btn primary block', type: 'button', 'data-act': 'rb-save', disabled: !dirty(), text: dirty() ? 'Save rulebook' : 'Saved', onclick: save }));
    st.body.replaceChildren(...kids);
    st.el.querySelectorAll('.rbpick, .rbscrim').forEach((x) => x.remove());
    if (st.pick) st.el.append(...picker(e));
    if (st.play && st.play.refs) paintPlay();
  }

  function chip(kind, k, label, aria, warn) {
    return st.ctx.h('button', { class: 'rbchip ' + kind + (warn ? ' warn' : ''), type: 'button', 'data-act': 'rb-' + kind + '-' + k, 'aria-label': aria + ': ' + label,
      'aria-haspopup': 'dialog', text: label, onclick: () => { st.pick = { kind: kind, k: k }; st.confirm = false; render(); } });
  }

  function ruleCard(r, k, e) {
    const h = st.ctx.h, c = cond(r.when), t = targetDef(r.target), bad = problem(r, e), n = st.rules.length;
    const skillOk = r.action === BASIC || (e.skills || []).indexOf(r.action) >= 0;
    const line = h('div', { class: 'rbline' }, [
      h('span', { class: 'rbk', text: 'IF' }), chip('cond', k, c ? c.name : r.when, 'Condition', !c),
      c && c.value ? chip('value', k, valueText(c, r.value), 'Value', r.value === null) : null,
      h('span', { class: 'rbk', text: 'THEN' }), chip('action', k, skillName(r.action), 'Action', !skillOk),
      h('span', { class: 'rbk', text: 'AT' }), chip('target', k, t ? t.name : r.target, 'Target', !t),
    ]);
    const btn = (act, text, label, dis, fn) => h('button', { class: 'rbbtn', type: 'button', 'data-act': act + '-' + k, 'aria-label': label, disabled: dis, text: text, onclick: fn });
    return h('li', { class: 'rbcard' + (bad ? ' warn' : '') + (st.play && st.play.lit === k ? ' lit' : ''), 'data-act': 'rb-rule-' + k }, [
      h('span', { class: 'rbnum num', 'aria-hidden': 'true', text: String(k + 1) }), r.raw !== undefined ? h('code', { class: 'small', text: r.raw }) : line,
      h('div', { class: 'rbmoves' }, [
        btn('rb-up', '↑', 'Move rule ' + (k + 1) + ' up', k === 0, () => move(k, -1)),
        btn('rb-down', '↓', 'Move rule ' + (k + 1) + ' down', k === n - 1, () => move(k, 1)),
        btn('rb-del', '✕', 'Delete rule ' + (k + 1), false, () => edit(() => st.rules.splice(k, 1))),
      ]),
      bad ? h('div', { class: 'small rbwarn', text: '⚠ ' + bad }) : null,
    ]);
  }

  // The bottom picker sheet for the chip st.pick names, and the scrim behind it (a tap on it closes the picker).
  function picker(e) {
    const h = st.ctx.h, p = st.pick, r = st.rules[p.k];
    if (!r) { st.pick = null; return []; }
    const rows = [];
    const row = (id, name, desc, sel, tip) => h('div', { class: 'rbrow' + (sel ? ' sel' : '') }, [
      h('button', { class: 'rbopt', type: 'button', 'data-act': 'rb-pick-' + id, 'aria-pressed': sel ? 'true' : 'false', onclick: () => pickValue(p.kind, p.k, id) },
        [h('b', { text: name }), desc ? h('span', { class: 'small muted', text: desc }) : null]),
      tip ? h('span', { class: 'rbinfo', 'aria-label': 'About ' + name, ...st.ctx.TIP.attrs(tip), text: 'i' }) : null,
    ]);
    let title;
    if (p.kind === 'cond') { title = 'Condition'; G().conditions.forEach((c) => rows.push(row(c.id, c.name, c.desc, c.id === r.when))); }
    else if (p.kind === 'target') { title = 'Target'; G().targets.forEach((t) => rows.push(row(t.id, t.name, t.desc, t.id === r.target))); }
    else if (p.kind === 'action') {
      title = 'Action';
      rows.push(row(BASIC, G().basic.name, G().basic.desc, r.action === BASIC));
      (e.skills || []).forEach((id) => rows.push(row(id, skillName(id), null, r.action === id, skillTip(id))));
      if (!(e.skills || []).length) rows.push(h('p', { class: 'small muted', text: e.name + ' has no active skill: only the basic attack.' }));
    } else {
      const c = cond(r.when);
      title = c ? c.name : 'Value';
      const grid = h('div', { class: 'rbvals' }, (c ? valueChoices(c) : []).map((v) => h('button', { class: 'btn small' + (v === r.value ? ' gemsy' : ''), type: 'button',
        'data-act': 'rb-pick-' + v, 'aria-pressed': v === r.value ? 'true' : 'false', text: valueText(c, v), onclick: () => pickValue('value', p.k, v) })));
      rows.push(grid);
    }
    const closePick = () => { st.pick = null; render(); focusChip(p); };
    const sheet = h('div', { class: 'rbpick', role: 'dialog', 'aria-modal': 'true', 'aria-label': title + ' for rule ' + (p.k + 1), 'data-id': 'rb-picker' }, [
      h('div', { class: 'row between' }, [h('h3', { text: title + ' · rule ' + (p.k + 1) }), h('button', { class: 'btn small', type: 'button', 'data-act': 'rb-pick-close', text: 'Done', onclick: closePick })]),
      ...rows,
    ]);
    const scrim = h('div', { class: 'rbscrim', 'aria-hidden': 'true', onclick: closePick });
    queueMicrotask(() => { const f = sheet.querySelector('[aria-pressed=true]') || sheet.querySelector('.rbopt, .rbvals button'); if (f) f.focus(); });
    return [scrim, sheet];
  }

  function playPanel() {
    const h = st.ctx.h, p = st.play;
    if (p.error) return h('div', { class: 'card rbplay', 'data-act': 'rb-play' }, [h('p', { class: 'small rberr', 'data-act': 'rb-error', text: 'Test refused: ' + p.error })]);
    const f = p.f, units = [];
    const col = (side) => h('div', { class: 'rbside' }, f.units.map((u, k) => {
      if (u.side !== side) return null;
      const fill = h('div', { class: 'fill' }), text = h('span');
      const row = h('div', { class: 'rbunit' + (u.id === p.me ? ' me' : '') }, [h('span', { class: 'small rbuname', text: u.name }), h('div', { class: 'bar hp' }, [fill, text])]);
      units[k] = { row: row, fill: fill, text: text };
      return row;
    }));
    const now = h('div', { class: 'small rbnow', 'aria-live': 'off' }), progress = h('span', { class: 'small muted num' });
    const skip = h('button', { class: 'btn small', type: 'button', 'data-act': 'rb-skip', text: 'Skip', onclick: () => endPlay(false) });
    const result = h('div', { class: 'rbresult', 'data-act': 'rb-result', role: 'status' }, [
      h('b', { class: f.won ? 'ok' : 'bad', text: f.won ? 'Won floor ' + f.floor : 'Lost on floor ' + f.floor }),
      h('span', { class: 'small muted', text: ' · a test pays nothing and changes nothing' }),
    ]);
    const panel = h('div', { class: 'card rbplay', 'data-act': 'rb-play' }, [
      h('div', { class: 'row between' }, [h('b', { class: 'small', text: 'Test · floor ' + f.floor + ': ' + f.name }), progress]),
      h('div', { class: 'rbsides' }, [col(0), col(1)]),
      now,
      h('div', { class: 'row between' }, [result, skip]),
    ]);
    p.refs = { units: units, now: now, progress: progress, skip: skip, result: result };
    return panel;
  }

  window.RulebookView = {
    open: open,
    close: close,
    isOpen: () => !!st,
    get lastCtx() { return lastCtx; },
    parse: parse,
    format: format,
  };
})();
