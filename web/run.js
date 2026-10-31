// Monster Trainer run screen: the UI shell over the C# training-run engine (web/run-engine.js). No game rule lives here;
// it sends commands, reads the engine's state and labels, and draws them (specs/13 T6, specs/14, specs/15).
(function () {
  'use strict';
  const RE = window.RunEngine, SP = window.IdleSprites, TIP = window.Tips;
  const $ = (id) => document.getElementById(id);
  const pct = (x) => Math.round(x * 100) + '%';
  const up = (s) => String(s).toUpperCase();
  const num = (v) => (Number.isInteger(v) ? String(v) : v.toFixed(1));

  let run = null, C = null;
  const ui = {
    view: 'home',            // home | run | climb | grade
    tab: 'train',            // train | trees | log
    pick: null,              // species chosen on the home screen
    book: false,             // test option: start with a skill book
    feed: [],                // newest first
    last: null,              // last drill/rest result, shown under the stats
    flash: null,             // stat to flash after a drill
    fight: null,             // fight result shown in the modal
    play: null,              // fight playback in progress: {i: actions shown, timer, dom}; null when showing the result
    speed: '1',              // fight playback speed: '1', '2' or 'skip' (remembered, see loadSpeed)
    showAll: false,          // fight log expanded
    sealed: null,            // Vault entry of the finished run
    payout: 0,               // shards that run paid
    climb: null,             // the sealed monster's gauntlet floors for the wheel: { data, t0, done, raf }
  };

  // ------------------------------------------------------------ small DOM helpers
  function h(tag, attrs, kids) {
    const el = document.createElement(tag);
    if (attrs) for (const k in attrs) {
      const v = attrs[k];
      if (k === 'class') el.className = v;
      else if (k === 'text') el.textContent = v;
      else if (k.startsWith('on')) el.addEventListener(k.slice(2), v);
      else if (v !== undefined && v !== null && v !== false) el.setAttribute(k, v === true ? '' : v);
    }
    if (kids) for (const kid of [].concat(kids)) if (kid !== null && kid !== undefined) el.append(kid);
    return el;
  }
  function hash(str) { let x = 2166136261; for (let i = 0; i < str.length; i++) { x ^= str.charCodeAt(i); x = Math.imul(x, 16777619); } return x >>> 0; }
  function sprite(key) {
    const cv = h('canvas', { class: 'sprite', 'aria-hidden': 'true' });
    SP.draw(cv, SP.enemy(hash(key) % 100000, false, 0));
    return cv;
  }
  function toast(msg) {
    const t = h('div', { class: 'toast', text: msg });
    const box = $('toasts');
    while (box.children.length >= 3) box.firstChild.remove();
    box.append(t);
    setTimeout(() => t.remove(), 3200);
  }
  function bar(value, max, text, cls) {
    return h('div', { class: 'bar ' + (cls || '') }, [
      h('div', { class: 'fill', style: 'width:' + Math.max(0, Math.min(100, (value / max) * 100)) + '%' }),
      h('span', { text: text }),
    ]);
  }
  const speciesDef = (id) => C.species.find((s) => s.id === id);
  const rank = (i) => C.ranks[i];
  const elementTag = (e) => h('span', { class: 'tag', text: (C.elements[e] || { name: e }).name, style: C.elements[e] && C.elements[e].color ? 'border-color:' + C.elements[e].color : '' });
  const treeName = (id) => (C.trees[id] ? C.trees[id].name : id);

  // ------------------------------------------------------------ rendering
  function render() {
    const main = $('main'), tabs = $('tabs'), modal = $('modal');
    const prevScroll = main.scrollTop;
    main.replaceChildren();
    if (ui.view === 'home') { tabs.hidden = true; modal.hidden = true; renderHome(main); return; }
    const s = run.state;
    if (ui.view === 'grade') { tabs.hidden = true; modal.hidden = true; renderGrade(main); return; }
    if (ui.view === 'climb') { tabs.hidden = true; modal.hidden = true; renderClimb(main); return; }
    tabs.hidden = false;
    main.append(renderHud(s));
    if (ui.tab === 'train') renderTrain(main, s);
    else if (ui.tab === 'trees') renderTrees(main, s);
    else renderLog(main, s);
    renderTabs(tabs, s);
    renderModal(modal, s);
    main.scrollTop = prevScroll;
  }

  function renderTabs(tabs, s) {
    tabs.replaceChildren();
    const canBuy = s.slots.some((t) => t && C.trees[t.tree].nodes[t.bought] && C.trees[t.tree].nodes[t.bought].cost <= s.sp);
    [['train', 'Train'], ['trees', 'Trees'], ['log', 'Log']].forEach(([id, label]) => {
      const b = h('button', { type: 'button', role: 'tab', 'aria-selected': String(ui.tab === id), 'data-act': 'tab-' + id, text: label,
        onclick: () => { ui.tab = id; render(); } });
      if (id === 'trees' && canBuy) b.append(h('span', { class: 'dot' }));
      tabs.append(b);
    });
  }

  function renderHud(s) {
    const sp = speciesDef(s.species), r = rank(s.rank), k = C.consts;
    const low = s.energy / k.energyMax < 0.3;
    const pips = h('div', { class: 'pips', 'aria-label': s.ap + ' action points left' });
    for (let i = 0; i < k.runAp; i++) pips.append(h('span', { class: 'pip' + (i < s.ap ? ' full' : '') }));
    return h('div', { class: 'card hud' }, [
      sprite(s.species),
      h('div', null, [
        h('div', { class: 'hud-line' }, [
          h('span', { text: sp.name }), elementTag(sp.element), h('span', { class: 'tag', text: r.name, 'data-id': 'rank' }),
        ]),
        h('div', { class: 'hud-line small muted' }, [
          h('span', { text: 'Cycle ' + s.cycle + '/' + k.runCycles, 'data-id': 'cycle' }),
          h('span', { text: s.sp + ' SP', class: s.sp ? 'gems' : '', 'data-id': 'sp' }),
          h('span', { text: s.fightsWon + '/' + s.fightsFought + ' fights won' }),
        ]),
      ]),
      h('div', { style: 'grid-column: 1 / -1; display:flex; flex-direction:column; gap:6px' }, [
        h('div', { class: 'row between' }, [h('span', { class: 'small muted', text: 'Action points' }), pips]),
        bar(s.energy, k.energyMax, 'Energy ' + Math.round(s.energy) + '/' + k.energyMax, 'energy' + (low ? ' low' : '')),
      ]),
    ]);
  }

  function renderTrain(main, s) {
    const k = C.consts;
    if (s.phase === 'finished') {
      main.append(h('div', { class: 'card alert' }, [
        h('h2', { text: s.end === 'injured' ? 'Injured: the run is over' : 'Run complete' }),
        h('p', { class: 'muted', text: s.end === 'injured'
          ? 'A drill at low energy injured the monster. Sealing still vaults it and pays its shards.'
          : 'All ' + k.runCycles + ' cycles are done.' }),
        s.sp > 0 ? h('p', { class: 'gems', text: s.sp + ' unspent skill points are lost when you seal. Spend them on the Trees tab first.' }) : null,
        h('button', { class: 'btn primary block', type: 'button', 'data-act': 'seal', text: 'Seal in the Vault', onclick: seal }),
      ]));
    } else if (s.phase === 'awaitfight') {
      main.append(fightCard(s));
    }
    const busy = s.phase !== 'training' || s.offers.length > 0;
    const o = s.odds;
    main.append(h('div', { class: 'odds', 'data-id': 'odds',
      text: 'Drill at this energy: fail ' + pct(o.fail) + ' · success ' + pct(o.success) + ' · super ' + pct(o.super) + ' · injury ' + pct(o.injury) +
        '. Each drill costs ' + num(s.drillCost) + ' energy.' }));
    const card = h('div', { class: 'card' });
    C.stats.forEach((st) => {
      const row = h('div', { class: 'stat' + (ui.flash === st ? ' flash' : '') }, [
        h('span', { class: 'lbl', text: up(st), ...TIP.attrs('stat:' + st) }),
        h('div', { class: 'meta' }, [
          bar(s.stats[st], k.statMax, num(s.stats[st]) + ' / ' + k.statMax),
          h('small', { text: 'growth x' + s.growth[st].toFixed(2) + ' · ' + C.milestones.map((m) => (s.stats[st] >= m ? '✓' : '') + m).join(' ') }),
        ]),
        h('button', { class: 'btn primary small', type: 'button', 'data-act': 'drill-' + st, text: 'Drill', disabled: busy, onclick: () => drill(st) }),
      ]);
      card.append(row);
    });
    main.append(card);
    main.append(h('button', { class: 'btn gemsy block', type: 'button', 'data-act': 'rest', disabled: busy,
      text: 'Rest (+' + k.restEnergy + ' energy)', onclick: rest }));
    if (s.offers.length) main.append(h('p', { class: 'gems', text: 'A tree draft is waiting. Choose or discard it to keep training.' }));
    if (ui.last) main.append(resultCard(ui.last));
    ui.flash = null;
  }

  function resultCard(r) {
    if (r.kind === 'drill') {
      return h('div', { class: 'card result', 'data-id': 'result' }, [
        h('div', null, [h('b', { class: r.tier, text: r.tierName }), h('span', { text: ' ' + up(r.stat) + ' ' + (r.gain ? '+' + num(r.gain) : '+0') + ' → ' + num(r.value) })]),
        r.injured ? h('b', { class: 'bad', text: 'Injured! The run ends.' }) : null,
      ]);
    }
    return h('div', { class: 'card result', 'data-id': 'result' }, [h('div', null, [h('b', { text: 'Rest' }), h('span', { text: ' +' + num(r.restored) + ' energy' })])]);
  }

  function fightCard(s) {
    const opp = s.opponent;
    const cmp = h('div', { class: 'mini' });
    C.stats.forEach((st) => {
      const diff = s.stats[st] - opp.stats[st];
      cmp.append(h('span', TIP.attrs('stat:' + st + ':vs'), [up(st) + ' ', h('b', { class: diff >= 0 ? 'ok' : 'bad', text: (diff >= 0 ? '+' : '') + num(diff) })]));
    });
    return h('div', { class: 'card alert' }, [
      h('div', { class: 'row' }, [sprite('rank-' + s.rank), h('div', null, [h('h2', { text: 'Rank fight: ' + opp.name }), elementTag(opp.element)])]),
      h('div', { class: 'small muted', text: 'Your stats against theirs:' }), cmp,
      speedToggle(),
      h('button', { class: 'btn warn block', type: 'button', 'data-act': 'fight', disabled: s.offers.length > 0, text: 'Fight', onclick: fight }),
    ]);
  }

  // ---- trees
  function nodeRow(n, state, buy) {
    return h('div', { class: 'node ' + state + (n.last ? ' last' : '') }, [
      h('span', { class: 'chk' }),
      h('div', null, [h('div', { class: 'nm', text: n.name, ...TIP.attrs('node:' + n.id) }), h('div', { class: 'ds', text: n.desc })]),
      buy || h('span', { class: 'small muted num', text: n.cost + ' SP' }),
    ]);
  }

  // How to get each tree a chase condition names (the engine words it in tree.source).
  function conditionSources(t) {
    const refs = [];
    (t.conditionTrees || []).forEach((id) => { if (C.trees[id] && refs.indexOf(id) < 0) refs.push(id); });
    return refs.map((id) => h('div', { class: 'small muted src', text: C.trees[id].name + ': ' + C.trees[id].source + '.' }));
  }

  function renderTrees(main, s) {
    main.append(h('div', { class: 'row between' }, [h('h2', { text: 'Skill trees' }), h('span', { class: 'num ' + (s.sp ? 'gems' : 'muted'), text: s.sp + ' SP', 'data-id': 'sp2' })]));
    s.slots.forEach((slot, i) => {
      if (!slot) {
        main.append(h('div', { class: 'card slotcard empty' }, [h('b', { text: 'Slot ' + (i + 1) + ': empty' }), h('small', { text: 'Fills from tree drafts (stat milestones, books, chase unlocks).' })]));
        return;
      }
      const t = C.trees[slot.tree];
      const card = h('div', { class: 'card slotcard', 'data-id': 'slot-' + i }, [
        h('div', { class: 'row between' }, [
          h('b', { text: 'Slot ' + (i + 1) + ': ' + t.name }),
          h('span', null, [h('span', { class: 'tag', text: t.kind }), ' ', h('span', { class: 'small muted num', text: slot.bought + '/' + t.nodes.length })]),
        ]),
      ]);
      t.nodes.forEach((n, j) => {
        if (j < slot.bought) card.append(nodeRow(n, 'got'));
        else if (j === slot.bought) {
          card.append(nodeRow(n, 'next', h('button', { class: 'btn small primary', type: 'button', 'data-act': 'buy-' + i, disabled: s.sp < n.cost,
            text: 'Buy ' + n.cost + ' SP', onclick: () => spend(i) })));
        } else card.append(nodeRow(n, 'locked'));
      });
      main.append(card);
    });

    main.append(h('h3', { text: 'Chase trees' }));
    main.append(h('p', { class: 'small muted', text: 'Each species has two chase trees of its own: one unlocks from its own trees and stats, the other also needs a skill book tree. A chase tree unlocks when every condition holds, then it is offered as a draft. It locks again if a condition stops holding.' }));
    s.chase.forEach((c) => {
      const t = C.trees[c.tree];
      const slotted = s.slots.some((x) => x && x.tree === c.tree);
      main.append(h('div', { class: 'card', 'data-id': 'chasecard-' + c.tree }, [
        h('div', { class: 'row between' }, [h('b', { text: t.name }), h('span', null, [
          h('span', { class: 'tag', text: t.bookGated ? 'book-gated' : 'in-run' }), ' ',
          h('span', { class: 'tag', text: slotted ? 'slotted' : c.unlocked ? 'unlocked' : 'locked' })])]),
        ...t.conditions.map((txt, i) => h('div', { class: 'cond' + (c.met[i] ? ' met' : '') }, [h('span', { text: c.met[i] ? '✓' : '○' }), h('span', { text: txt })])),
        ...conditionSources(t),
      ]));
    });
    if (s.books.length) {
      main.append(h('h3', { text: 'Skill books' }));
      s.books.forEach((id) => main.append(h('div', { class: 'card' }, [
        h('div', { class: 'row between' }, [h('b', { text: C.books[id].name }),
          h('button', { class: 'btn small gemsy', type: 'button', 'data-act': 'book-' + id, text: 'Use', onclick: () => useBook(id) })]),
      ])));
    }
  }

  function renderLog(main, s) {
    main.append(h('h2', { text: 'Log' }));
    if (!ui.feed.length) main.append(h('p', { class: 'muted', text: 'Nothing yet this session.' }));
    else main.append(h('ul', { class: 'alog', 'data-id': 'feed' }, ui.feed.slice(0, 80).map((l) => h('li', { text: l }))));
    main.append(h('h3', { text: 'Run' }));
    main.append(h('div', { class: 'card small muted' }, [
      h('div', { text: 'Seed ' + s.seed + ' · state ' + s.hash.slice(0, 12) }),
      h('div', { text: s.drills + ' drills · ' + s.rests + ' rests · ' + s.tally.crits + ' crits · ' + s.tally.evades + ' evades in rank fights' }),
      h('div', { text: 'Milestones: ' + (s.milestonesHit.map((m) => up(m.stat) + ' ' + m.threshold).join(', ') || 'none yet') }),
    ]));
    // An in-page confirm: window.confirm is blocked (returns false) inside sandboxed frames such as the phone artifact.
    if (ui.confirmAbandon) {
      main.append(h('div', { class: 'card alert' }, [
        h('p', { text: 'Abandon this run? Nothing is sealed and no shards are paid. Unused books come back.' }),
        h('div', { class: 'row' }, [
          h('button', { class: 'btn small warn', type: 'button', 'data-act': 'abandon-yes', text: 'Yes, abandon', onclick: () => {
            ui.confirmAbandon = false; run.acct('abandon'); resetRunUi(); ui.view = 'home'; render();
          } }),
          h('button', { class: 'btn small', type: 'button', 'data-act': 'abandon-no', text: 'Keep playing', onclick: () => { ui.confirmAbandon = false; render(); } }),
        ]),
      ]));
    } else {
      main.append(h('button', { class: 'btn small', type: 'button', 'data-act': 'abandon', text: 'Abandon this run', onclick: () => { ui.confirmAbandon = true; render(); } }));
    }
  }

  // ---- modal: fight result and tree drafts
  function renderModal(modal, s) {
    modal.replaceChildren();
    if (ui.fight && ui.play) { modal.hidden = false; fightPlayView(modal, ui.fight, s); return; }
    if (ui.fight) { modal.hidden = false; fightModal(modal, ui.fight, s); return; }
    if (s.offers.length) { modal.hidden = false; offerModal(modal, s); return; }
    modal.hidden = true;
  }

  function actionLine(x, foeName, youName) {
    const who = (id) => (id === 'monster' ? youName : foeName);
    const a = who(x.actor), t = x.target ? who(x.target) : 'all';
    return { text: a + ' · ' + actionName(x) + ' → ' + t + ': ' + actionResult(x), you: x.actor === 'monster', skillId: x.skillId };
  }
  const actionName = (x) => x.skill || (x.counter ? 'Counter' : 'Attack');
  function actionResult(x) {
    let body;
    if (x.evaded) body = 'dodged';
    else if (x.kind === 'heal') body = 'heals ' + Math.round(x.amount);
    else if (x.kind === 'shield') body = 'shields ' + Math.round(x.amount);
    else if (x.amount > 0) body = Math.round(x.amount) + ' dmg';
    else body = 'applied';
    return body + (x.crit ? ' CRIT' : '');
  }

  function fightModal(modal, f, s) {
    const you = speciesDef(s.species).name, foe = rank(f.rankBefore).name;
    modal.append(
      h('h2', { class: f.won ? 'ok' : 'bad', text: f.won ? 'Victory! Promoted to ' + rank(f.rankAfter).name : f.outcome === 'TimedOut' ? 'Time ran out: no promotion' : 'Defeat: no promotion', 'data-id': 'fight-title' }),
      f.won ? h('p', { class: 'gems', text: '+' + f.sp + ' skill points' }) : h('p', { class: 'muted', text: 'Train more and try again next cycle.' }),
      fightUnits(f, you, foe, null).root,
      h('h3', { text: 'Battle log' }),
    );
    const lines = f.actions.map((x) => actionLine(x, foe, you));
    const shown = ui.showAll ? lines : lines.slice(0, 24);
    modal.append(h('ul', { class: 'alog' }, shown.map((l) => h('li', { class: l.you ? 'you' : 'foe', text: l.text, ...(l.skillId ? TIP.attrs('skill:' + l.skillId) : null) }))));
    if (lines.length > shown.length) modal.append(h('button', { class: 'btn small', type: 'button', text: 'Show all ' + f.actionCount + ' actions', onclick: () => { ui.showAll = true; render(); } }));
    if (f.actionCount > f.actions.length) modal.append(h('p', { class: 'small muted', text: 'Log capped at ' + f.actions.length + ' of ' + f.actionCount + ' actions.' }));
    modal.append(h('button', { class: 'btn primary block', type: 'button', 'data-act': 'fight-continue', text: 'Continue', onclick: () => { ui.fight = null; ui.showAll = false; render(); } }));
  }

  // ------------------------------------------------------------ fight playback (issue #79): presentation only
  // The engine has already resolved the fight; this replays its action log one action at a time so the player sees which ability
  // each unit uses and every skill's cooldown. HP and cooldowns come from the engine's per-action snapshots (BRIDGE.md, fight),
  // never computed here. Skip shows the result at once, as before.
  const FIGHT_STEP_MS = 350;   // PLACEHOLDER: time per logged action at 1x (2x halves it). Presentation only, not a game rule.
  const SPEED_KEY = 'idle-rng-fight-speed';
  const SPEEDS = [['1', '1x'], ['2', '2x'], ['skip', 'Skip']];

  function loadSpeed() {
    let v = null;
    try { v = localStorage.getItem(SPEED_KEY); } catch (e) { /* storage blocked: default speed */ }
    return SPEEDS.some((x) => x[0] === v) ? v : '1';
  }
  function setSpeed(v) {
    ui.speed = v;
    try { localStorage.setItem(SPEED_KEY, v); } catch (e) { /* storage blocked: kept for this session only */ }
    if (ui.play) {
      clearTimeout(ui.play.timer);
      if (v === 'skip') stopPlayback(); else schedulePlayback();
    }
    render();
  }
  function speedToggle() {
    return h('div', { class: 'speed', role: 'group', 'aria-label': 'Fight playback speed' }, [
      h('span', { class: 'small muted', text: 'Fight speed' }),
      ...SPEEDS.map(([v, label]) => h('button', { type: 'button', 'data-act': 'speed-' + v, 'aria-pressed': String(ui.speed === v), text: label,
        onclick: () => setSpeed(v) })),
    ]);
  }

  function startPlayback() {
    stopPlayback();
    if (ui.speed !== 'skip' && ui.fight && ui.fight.actions.length) { ui.play = { i: 0, timer: null, dom: null }; schedulePlayback(); }
  }
  function stopPlayback() {
    if (ui.play) clearTimeout(ui.play.timer);
    ui.play = null;
  }
  function schedulePlayback() {
    ui.play.timer = setTimeout(stepPlayback, FIGHT_STEP_MS / (Number(ui.speed) || 1));
  }
  function stepPlayback() {
    const p = ui.play;
    if (!p) return;
    if (p.i >= ui.fight.actions.length) { stopPlayback(); render(); return; }   // the last action stays up for one step
    p.i++;
    if (p.dom && p.dom.root.isConnected) updatePlay(); else render();
    schedulePlayback();
  }

  // Each unit's HP and cooldowns after the first `upto` logged actions (null: at the end of the fight).
  function unitsAt(f, upto) {
    return f.units.map((u, k) => {
      if (upto === null) return { hp: u.hp, cd: u.cd };
      let cd = [];                                 // no action yet: every skill starts ready
      for (let j = upto - 1; j >= 0; j--) if (f.actions[j].actor === u.id) { cd = f.actions[j].cd; break; }
      return { hp: upto ? f.actions[upto - 1].hp[k] : u.maxHp, cd: cd };
    });
  }

  // The unit panels: name, HP bar, and one row per active skill with its cooldown. Returns the element and refs for repainting.
  function fightUnits(f, you, foe, upto) {
    const refs = [];
    const root = h('div', { class: 'units' }, f.units.map((u) => {
      const chips = u.skills.map((sk, si) => (sk.kind === 'passive' ? null : h('div', { class: 'skill', 'data-id': 'skill-' + u.side + '-' + si }, [
        h('div', { class: 'cdfill' }), h('span', { class: 'nm', text: sk.name }), h('span', { class: 'st num' }),
      ])));
      const hpBar = bar(0, 1, '', '');
      refs.push({ u: u, hpBar: hpBar, chips: chips });
      return h('div', { class: 'unit' + (u.side ? ' foe' : ''), 'data-id': 'unit-' + u.side }, [
        h('b', { text: u.side ? foe : you }), hpBar,
        upto === null ? h('small', { class: 'muted', text: Math.round(u.damage) + ' dmg · ' + u.crits + ' crits · ' + u.evades + ' dodges' }) : null,
        h('div', { class: 'skills', 'data-id': 'skills-' + u.side }, chips.some(Boolean) ? chips : [h('small', { class: 'muted', text: 'Basic attacks only' })]),
      ]);
    }));
    paintUnits(refs, unitsAt(f, upto), null);
    return { root: root, refs: refs };
  }
  function paintUnits(refs, now, x) {
    refs.forEach((r, k) => {
      const hp = now[k].hp, max = r.u.maxHp;
      r.hpBar.querySelector('.fill').style.width = Math.max(0, Math.min(100, (hp / max) * 100)) + '%';
      r.hpBar.querySelector('span').textContent = Math.round(hp) + '/' + Math.round(max);
      r.chips.forEach((c, si) => {
        if (!c) return;
        const sk = r.u.skills[si], left = now[k].cd[si] || 0;
        c.classList.toggle('ready', left === 0);
        c.classList.toggle('used', !!x && x.actor === r.u.id && x.si === si);
        c.querySelector('.cdfill').style.width = (sk.cooldown ? Math.min(100, (left / sk.cooldown) * 100) : 0) + '%';
        c.querySelector('.st').textContent = left ? left + (left === 1 ? ' turn' : ' turns') : 'Ready';
        c.title = sk.name + ': ' + (left ? 'ready in ' + left + ' of its actions' : 'ready') + ', cooldown ' + sk.cooldown;
      });
    });
  }

  function fightPlayView(modal, f, s) {
    const you = speciesDef(s.species).name, foe = rank(f.rankBefore).name;
    const units = fightUnits(f, you, foe, ui.play.i);
    const now = h('div', { class: 'now', 'data-id': 'fight-now' });
    const progress = h('span', { class: 'small muted num', 'data-id': 'fight-progress' });
    const recent = h('ul', { class: 'alog' });
    modal.append(
      h('div', { class: 'row between' }, [h('h2', { text: 'Rank fight: ' + foe, 'data-id': 'fight-play' }), progress]),
      units.root, now, speedToggle(), h('h3', { text: 'Latest' }), recent,
    );
    ui.play.dom = { root: units.root, units: units, now: now, progress: progress, recent: recent, you: you, foe: foe };
    updatePlay();
  }
  function updatePlay() {
    const f = ui.fight, p = ui.play, d = p.dom;
    const x = p.i ? f.actions[p.i - 1] : null;
    paintUnits(d.units.refs, unitsAt(f, p.i), x);
    d.progress.textContent = 'Action ' + p.i + ' of ' + f.actions.length;
    const side = x && x.actor === 'monster' ? 'you' : 'foe';
    d.now.className = 'now' + (x ? ' ' + side : '');
    d.now.replaceChildren(...(x ? [
      h('small', { text: side === 'you' ? d.you : d.foe }),
      h('b', { text: actionName(x) }),
      h('span', { text: (x.target ? '→ ' + (x.target === 'monster' ? d.you : d.foe) + ': ' : '') + actionResult(x) }),
    ] : [h('b', { text: 'Fight!' })]));
    const lines = f.actions.slice(Math.max(0, p.i - 5), p.i).reverse().map((a) => actionLine(a, d.foe, d.you));
    d.recent.replaceChildren(...lines.map((l) => h('li', { class: l.you ? 'you' : 'foe', text: l.text })));
  }

  function offerModal(modal, s) {
    const o = s.offers[0];
    const why = o.source === 'milestone' ? up(o.stat) + ' reached ' + o.threshold + ': choose a tree'
      : o.source === 'book' ? 'Skill book: take its tree' : 'A chase tree unlocked';
    modal.append(h('h2', { text: why, 'data-id': 'offer-title' }),
      h('p', { class: 'small muted', text: 'Put it in a slot. An occupied slot is overwritten: its points and flat boosts are lost, and a chase tree that depended on it locks again.' +
        (s.offers.length > 1 ? ' (' + (s.offers.length - 1) + ' more waiting)' : '') }));
    o.trees.forEach((id, choice) => {
      const t = C.trees[id];
      const card = h('div', { class: 'card', 'data-id': 'offer-' + id }, [h('div', { class: 'row between' }, [h('b', { text: t.name }), h('span', { class: 'tag', text: t.kind })])]);
      t.nodes.forEach((n) => card.append(nodeRow(n, 'locked')));
      card.append(h('div', { class: 'small muted', text: 'Put it in:' }));
      const picks = h('div', { class: 'row', style: 'flex-wrap:wrap' });
      s.slots.forEach((slot, i) => picks.append(h('button', { class: 'btn small ' + (slot ? '' : 'primary'), type: 'button', 'data-act': 'take-' + choice + '-' + i,
        text: (slot ? 'Slot ' + (i + 1) + ' (replaces ' + treeName(slot.tree) + ')' : 'Slot ' + (i + 1) + ' (empty)'),
        onclick: () => take(choice, i) })));
      card.append(picks);
      modal.append(card);
    });
    modal.append(h('button', { class: 'btn block', type: 'button', 'data-act': 'discard', text: 'Discard this draft', onclick: discard }));
  }

  // ---- home and grade
  function renderHome(main) {
    const acct = run.account;
    main.append(h('div', { class: 'row between' }, [h('h2', { text: 'Raise a monster' }),
        h('span', { class: 'num gems', 'data-id': 'shards', text: acct.shards + ' shards' })]),
      h('p', { class: 'muted', text: 'Train over ' + C.consts.runCycles + ' cycles. Each cycle: ' + C.consts.runAp + ' actions (Drill a stat or Rest), then a rank fight. Win to rank up, spend skill points on trees, and avoid injury.' }));
    C.species.forEach((sp) => {
      const sel = ui.pick === sp.id;
      const mini = h('div', { class: 'mini' });
      C.stats.forEach((st) => mini.append(h('span', null, [up(st) + ' ', h('b', { text: num(sp.base[st]) })])));
      main.append(h('button', { class: 'card species' + (sel ? ' sel' : ''), type: 'button', 'data-act': 'species-' + sp.id, onclick: () => { ui.pick = sp.id; render(); } }, [
        sprite(sp.id),
        h('div', null, [h('div', { class: 'row' }, [h('b', { text: sp.name }), elementTag(sp.element)]), mini,
          h('small', { class: 'muted', text: 'Trees: ' + sp.trees.map(treeName).join(', ') }),
          h('small', { class: 'muted', 'data-id': 'chase-' + sp.id, text: 'Chase trees: ' + treeName(sp.chase[0]) + ', ' + treeName(sp.chase[1]) +
            (sp.book ? ' (needs ' + C.books[sp.book].name + ')' : '') })]),
      ]));
    });
    const bring = bookToBring();
    if (bring) {
      const cb = h('input', { type: 'checkbox', 'data-act': 'book-toggle', onchange: (e) => { ui.book = e.target.checked; } });
      cb.checked = ui.book;
      main.append(h('label', { class: 'small row' }, [cb, h('span', { text: 'Bring ' + C.books[bring].name + ' (you have ' + acct.books[bring] + ')' })]));
    }
    main.append(h('button', { class: 'btn primary block', type: 'button', 'data-act': 'start', disabled: !ui.pick, text: ui.pick ? 'Start run' : 'Pick a species', onclick: startRun }));
    const grant = speciesBook() || Object.keys(C.books)[0];
    if (grant) main.append(h('button', { class: 'btn small', type: 'button', 'data-act': 'grant-book', text: 'Test: add a ' + C.books[grant].name,
      onclick: () => { const r = run.acct('debugGrantBook', grant, 1); if (!r.ok) toast(r.reason); render(); } }));

    main.append(h('h3', { text: 'Talents' }));
    const m = acct.mods;
    main.append(h('p', { class: 'small muted', text: 'Bought with shards from finished runs. They apply from the next run on. Now: drills cost x' + m.drillCostMult.toFixed(2) + ', start stats x' + m.statFloorMult.toFixed(2) + ', +' + m.draftSizeBonus + ' draft size, +' + m.startSp + ' starting SP.' }));
    acct.talents.forEach((t) => {
      const def = C.talents.find((x) => x.id === t.id);
      main.append(h('div', { class: 'card', 'data-id': 'talent-' + t.id }, [
        h('div', { class: 'row between' }, [h('b', { text: def.name }), h('span', { class: 'tag num', text: t.rank + '/' + def.maxRank })]),
        h('div', { class: 'small muted', text: def.desc }),
        t.nextCost === null ? h('div', { class: 'small ok', text: 'Maxed' })
          : h('button', { class: 'btn small gemsy', type: 'button', 'data-act': 'talent-' + t.id, disabled: acct.shards < t.nextCost, text: 'Buy rank ' + (t.rank + 1) + ' · ' + t.nextCost + ' shards',
            onclick: () => { const r = run.acct('buyTalent', t.id); if (!r.ok) toast(r.reason); render(); } }),
      ]));
    });

    const vault = acct.vault.slice().reverse();
    main.append(h('h3', { text: 'Vault (' + vault.length + ')' }));
    if (!vault.length) main.append(h('p', { class: 'small muted', text: 'Sealed monsters appear here.' }));
    vault.forEach((e) => main.append(vaultCard(e)));
  }

  function vaultCard(e) {
    const sp = speciesDef(e.species) || { name: e.species };
    const total = Object.keys(e.stats).reduce((a, k) => a + e.stats[k], 0);
    return h('div', { class: 'card', 'data-id': 'vault-entry' }, [
      h('div', { class: 'row between' }, [
        h('div', { class: 'row' }, [h('span', { class: 'num', style: 'font-size:28px;font-weight:700', text: up(e.grade) }), h('div', null, [h('b', { text: sp.name }), h('div', { class: 'small muted', text: (e.end === 'injured' ? 'Injured' : 'Completed') + ' · cycle ' + e.cycles + ' · ' + e.fightsWon + ' wins' })])]),
        h('span', { class: 'small muted num', text: 'Σ ' + num(total) }),
      ]),
      e.gauntlet ? h('div', { class: 'small gold', 'data-id': 'vault-title', text: e.gauntlet.label }) : null,
      h('div', { class: 'small muted', text: e.trees.map((t) => treeName(t.id) + ' ' + t.bought + '/' + (C.trees[t.id] ? C.trees[t.id].nodes.length : '?')).join(' · ') }),
      e.sealedUtcMs ? h('div', { class: 'small muted', text: new Date(e.sealedUtcMs).toLocaleString() }) : null,
    ]);
  }

  function renderGrade(main) {
    const e = ui.sealed, sp = speciesDef(e.species) || { name: e.species };
    const r = C.ranks[e.rank];
    main.append(
      h('div', { class: 'center small muted', text: e.end === 'injured' ? 'Injured at cycle ' + e.cycles : 'Run complete' }),
      h('div', { class: 'grade ' + e.grade, 'data-id': 'grade', text: up(e.grade) }),
      h('div', { class: 'center' }, [h('b', { text: sp.name + ' reached ' + r.name })]),
      e.gauntlet ? h('div', { class: 'legacy', 'data-id': 'grade-title', text: e.gauntlet.label }) : null,
      h('div', { class: 'center small muted', text: e.fightsWon + ' of ' + e.fightsFought + ' rank fights won. Sealed in the Vault.' }),
      h('div', { class: 'center gems num', 'data-id': 'payout', text: '+' + ui.payout + ' Training Shards' }),
    );
    const card = h('div', { class: 'card' }, [h('h3', { text: 'Stat breakdown' })]);
    C.stats.forEach((st) => card.append(h('div', { class: 'stat', style: 'grid-template-columns:38px 1fr' }, [h('span', { class: 'lbl', text: up(st), ...TIP.attrs('stat:' + st) }), bar(e.stats[st], C.consts.statMax, num(e.stats[st]))])));
    main.append(card);
    main.append(h('div', { class: 'card' }, [h('h3', { text: 'Trees' }),
      ...e.trees.map((t) => h('div', { class: 'row between' }, [h('span', { text: treeName(t.id) }), h('span', { class: 'small muted num', text: t.bought + '/' + (C.trees[t.id] ? C.trees[t.id].nodes.length : '?') })]))]));
    main.append(h('div', { class: 'small muted center', text: 'Run hash ' + e.hash.slice(0, 16) }));
    if (ui.climb) main.append(h('button', { class: 'btn block', type: 'button', 'data-act': 'climb-replay', text: 'Watch the gauntlet again', onclick: () => openClimb(ui.climb.data) }));
    main.append(h('button', { class: 'btn primary block', type: 'button', 'data-act': 'new-run', text: 'New run', onclick: () => { resetRunUi(); ui.view = 'home'; render(); } }));
  }

  // ---- gauntlet wheel (specs/19, specs/12 section 6): a slowing reel of the floors climbed lands on the floor and the enemy
  // that beat the monster, then a comic defeat. The floors come from the engine (run.gauntlet); this only animates them.
  const POINTER_ROW = 2;   // the reel shows 5 rows; the pointer is on the middle one
  const reducedMotion = () => !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
  const foeDef = (id) => C.gauntlet.enemies[id] || { name: id, boss: false };
  function foeSprite(id, boss, cls) {
    const cv = h('canvas', { class: cls || '', 'aria-hidden': 'true' });
    SP.draw(cv, SP.enemy(hash('gauntlet-' + id) % 100000, boss, 0));
    return cv;
  }
  function openClimb(data) {
    stopClimb();
    ui.climb = { data: data, t0: null, done: reducedMotion(), raf: 0 };
    ui.view = 'climb';
    render();
  }
  function stopClimb() { if (ui.climb && ui.climb.raf) { cancelAnimationFrame(ui.climb.raf); ui.climb.raf = 0; } }
  function landClimb() { if (!ui.climb || ui.climb.done) return; stopClimb(); ui.climb.done = true; render(); }

  // Reel rows from the top: up to two unfought floors above the fall (the summit when cleared), then the floors fought down to 1.
  function climbRows(g) {
    const rows = [];
    if (g.cleared) rows.push({ summit: true });
    else for (let f = Math.min(C.gauntlet.floors, g.floorReached + 2); f > g.floorReached; f--) rows.push({ floor: f, unfought: true });
    for (let i = g.floors.length - 1; i >= 0; i--) rows.push(g.floors[i]);
    return rows;
  }
  function climbRow(r) {
    if (r.summit) return h('div', { class: 'floor summit' }, [h('span', { class: 'fl', text: 'TOP' }), h('span', { class: 'gold', text: '★' }), h('span', { class: 'nm', text: 'Tower cleared' }), h('span', { class: 'mk' })]);
    if (r.unfought) return h('div', { class: 'floor unfought' }, [h('span', { class: 'fl', text: 'F' + r.floor }), h('span'), h('span', { class: 'nm', text: '?' }), h('span', { class: 'mk' })]);
    const d = foeDef(r.enemy);
    return h('div', { class: 'floor ' + (r.won ? 'won' : 'fell') + (r.boss ? ' boss' : '') }, [
      h('span', { class: 'fl', text: 'F' + r.floor }), foeSprite(r.enemy, r.boss), h('span', { class: 'nm', text: d.name + (r.boss ? ' (boss)' : '') }),
      h('span', { class: 'mk', text: r.won ? '✓' : '' }),
    ]);
  }
  const rowLabel = (r) => (r.summit ? 'Tower cleared' : r.unfought ? 'Floor ' + r.floor : 'Floor ' + r.floor + ' · ' + foeDef(r.enemy).name);

  function renderClimb(main) {
    const cl = ui.climb, g = cl.data, e = ui.sealed, sp = speciesDef(e.species) || { name: e.species };
    const rows = climbRows(g);
    const start = rows.length - 1, end = rows.length - g.floors.length - (g.cleared ? 1 : 0);   // floor 1 -> where it stops
    const els = rows.map(climbRow);
    const strip = h('div', { class: 'strip' }, els);
    const now = h('div', { class: 'climb-now', 'data-id': 'climb-now' });
    let shown = -1;
    const place = (idx) => {
      strip.style.transform = 'translateY(calc(var(--row) * ' + (POINTER_ROW - idx).toFixed(3) + '))';
      const i = Math.round(idx);
      if (i !== shown) { shown = i; now.textContent = rowLabel(rows[i]); }
    };
    const reel = h('div', { class: 'reel', 'data-id': 'climb-reel', 'data-hash': g.hash, role: 'button', tabindex: '0',
      'aria-label': cl.done ? rowLabel(rows[end]) : 'The wheel is spinning through the floors. Tap to skip.', onclick: landClimb,
      onkeydown: (ev) => { if (ev.key === 'Enter' || ev.key === ' ') { ev.preventDefault(); landClimb(); } } },
    [strip, h('div', { class: 'pointer' })]);
    main.append(
      h('h2', { class: 'center', text: 'The Gauntlet' }),
      h('div', { class: 'center small muted', text: sp.name + ' climbs a ' + C.gauntlet.floors + '-floor tower, a boss every ' + C.gauntlet.bossEvery + ' floors, until a floor beats it.' }),
      now, reel,
    );
    if (!cl.done) {
      stopClimb();   // a re-render mid-spin restarts the loop on the new elements (t0 is kept)
      place(start);
      main.append(h('button', { class: 'btn block', type: 'button', 'data-act': 'climb-skip', text: 'Skip ▸▸', onclick: landClimb }));
      // A slowing wheel: ease-out-quart from floor 1 to the landing row; longer climbs spin longer, up to about 5 s.
      const dur = Math.min(5200, 1600 + 36 * (start - end));
      const tick = (t) => {
        if (ui.climb !== cl || cl.done) return;
        if (cl.t0 === null) cl.t0 = t;
        const k = Math.min(1, (t - cl.t0) / dur);
        place(start + (end - start) * (1 - Math.pow(1 - k, 4)));
        if (k >= 1) { landClimb(); return; }
        cl.raf = requestAnimationFrame(tick);
      };
      cl.raf = requestAnimationFrame(tick);
      return;
    }
    place(end);
    els[end].classList.add('landed');
    if (!g.cleared) els[end].querySelector('.mk').textContent = '✗';
    main.append(climbArena(g, e, sp));
    main.append(h('div', { class: 'card alert center' }, [
      h('div', { class: 'small muted', text: g.cleared ? sp.name + ' cleared every floor. Legacy Title:' : sp.name + ' fell on floor ' + g.floorReached + ' to ' + foeDef(g.killer).name + '. Legacy Title:' }),
      h('div', { class: 'legacy', 'data-id': 'climb-title', text: e.gauntlet.label }),
    ]));
    main.append(h('button', { class: 'btn primary block', type: 'button', 'data-act': 'climb-continue', text: 'See the grade', onclick: () => { stopClimb(); ui.view = 'grade'; render(); } }));
  }

  // The comic defeat: the killer strikes and the monster is launched off screen or turned to ash (picked from the result hash,
  // cosmetic only); a cleared tower gets a victory hop instead.
  function climbArena(g, e, sp) {
    const you = h('div', { class: 'fighter you' }, [sprite(e.species), h('span', { text: sp.name })]);
    if (g.cleared) {
      you.classList.add('hop');
      return h('div', { class: 'arena', 'data-id': 'climb-arena' }, [you, h('div', { class: 'pow', style: 'left:auto;right:10%', text: '★ CLEARED ★' })]);
    }
    const d = foeDef(g.killer), boss = !!d.boss, ash = parseInt(g.hash.slice(-1), 16) % 2 === 1;
    you.classList.add(ash ? 'ash' : 'launch');
    const foe = h('div', { class: 'fighter foe strike' + (boss ? ' boss' : '') }, [foeSprite(g.killer, boss, 'sprite'), h('span', { text: d.name })]);
    return h('div', { class: 'arena', 'data-id': 'climb-arena' }, [you, foe, h('div', { class: 'pow', text: ash ? 'FWOOSH!' : 'BONK!' })]);
  }

  // ------------------------------------------------------------ actions
  function resetRunUi() { stopClimb(); stopPlayback(); ui.climb = null; ui.confirmAbandon = false; ui.feed = []; ui.last = null; ui.fight = null; ui.sealed = null; ui.payout = 0; ui.tab = 'train'; ui.showAll = false; }
  const log = (line) => { ui.feed.unshift(line); };

  // The book the picked species' book-gated chase tree needs, if any.
  function speciesBook() { const sp = C.species.find((x) => x.id === ui.pick); return sp ? sp.book : null; }
  // The book the start screen offers to bring: the picked species' own book if owned, else the first owned book.
  function bookToBring() {
    const has = (id) => (run.account.books[id] || 0) > 0;
    const own = speciesBook();
    return own && has(own) ? own : Object.keys(C.books).find(has) || null;
  }

  function startRun() {
    if (!ui.pick) return;
    resetRunUi();
    const bring = bookToBring();
    const r = run.startRun(ui.pick, ui.book && bring ? [bring] : []);
    if (!r.ok) { toast(r.reason); render(); return; }
    ui.book = false;
    ui.view = 'run';
    render();
  }

  function after(r) {          // common tail of a command: report a refusal, refresh
    if (!r.ok) toast(r.reason);
    render();
  }
  function milestones(r) {
    (r.milestones || []).forEach((m) => { toast('Milestone: ' + up(m.stat) + ' ' + m.threshold + '. A tree draft is waiting'); log('Milestone ' + up(m.stat) + ' ' + m.threshold); });
  }

  function drill(stat) {
    const r = run.cmd('drill', stat);
    if (r.ok) {
      ui.last = r; ui.flash = stat;
      log('Drill ' + up(stat) + ': ' + r.tierName + ' +' + num(r.gain) + ' → ' + num(r.value) + (r.injured ? ' (injured)' : ''));
      milestones(r);
      if (r.injured) toast('Injured! The run ends.');
    }
    after(r);
  }
  function rest() {
    const r = run.cmd('rest');
    if (r.ok) { ui.last = r; log('Rest +' + num(r.restored) + ' energy'); }
    after(r);
  }
  function fight() {
    const r = run.cmd('fight');
    if (r.ok) {
      ui.fight = r; ui.last = null; ui.showAll = false;
      startPlayback();
      log('Rank fight vs ' + rank(r.rankBefore).name + ': ' + (r.won ? 'won → ' + rank(r.rankAfter).name + ' (+' + r.sp + ' SP)' : 'lost'));
    }
    after(r);
  }
  function take(choice, slot) {
    const before = run.state.slots[slot];
    const r = run.cmd('take', choice, slot);
    if (r.ok) {
      log('Took ' + treeName(r.placed) + ' into slot ' + (slot + 1) + (before ? ' (replaced ' + treeName(before.tree) + ')' : ''));
      r.removed.forEach((id) => { if (!before || id !== before.tree) toast(treeName(id) + ' left its slot'); });
    }
    after(r);
  }
  function discard() { const r = run.cmd('discard'); if (r.ok) log('Discarded a tree draft'); after(r); }
  function spend(slot) {
    const r = run.cmd('spend', slot);
    if (r.ok) { log('Bought ' + r.node + (r.statGain ? ' (+' + num(r.statGain) + ')' : '')); milestones(r); }
    after(r);
  }
  function useBook(id) { const r = run.cmd('book', id); if (r.ok) log('Used ' + C.books[id].name); after(r); }

  function seal() {
    const r = run.finishRun();
    if (!r.ok) { toast(r.reason); return; }
    ui.sealed = r.entry; ui.payout = r.shards; ui.view = 'grade';
    if (r.entry.gauntlet) {   // the wheel replays the new entry's climb (read only); without it the grade screen still works
      try {
        const g = run.gauntlet(run.account.vault.length - 1);
        if (g.ok) { openClimb(g); return; }
        console.warn('gauntlet replay refused:', g.reason);
      } catch (err) { console.warn('gauntlet replay failed:', err); }
    }
    render();
  }

  // ------------------------------------------------------------ boot
  async function boot() {
    try {
      run = await RE.create({});
      C = run.content;
      TIP.init(() => C, () => run.state);
      ui.pick = C.species[0].id;
      ui.speed = loadSpeed();
      if (run.state) ui.view = 'run';
      if (run.loadError) toast('Your saved account could not be loaded and was kept aside: ' + run.loadError);
      render();
    } catch (e) {
      $('main').replaceChildren(h('h2', { text: 'The game did not load' }), h('p', { class: 'err', text: e && e.message ? e.message : String(e) }));
    }
  }
  boot();
})();
