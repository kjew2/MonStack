// Monster Trainer run screen: the UI shell over the C# training-run engine (web/run-engine.js). No game rule lives here;
// it sends commands, reads the engine's state and labels, and draws them (specs/13 T6, specs/14, specs/15).
(function () {
  'use strict';
  const RE = window.RunEngine, SP = window.IdleSprites;
  const $ = (id) => document.getElementById(id);
  const pct = (x) => Math.round(x * 100) + '%';
  const up = (s) => String(s).toUpperCase();
  const num = (v) => (Number.isInteger(v) ? String(v) : v.toFixed(1));

  let run = null, C = null;
  const ui = {
    view: 'home',            // home | run | grade
    tab: 'train',            // train | trees | log
    pick: null,              // species chosen on the home screen
    book: false,             // test option: start with a skill book
    feed: [],                // newest first
    last: null,              // last drill/rest result, shown under the stats
    flash: null,             // stat to flash after a drill
    fight: null,             // fight result shown in the modal
    showAll: false,          // fight log expanded
    sealed: null,            // Vault entry of the finished run
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
          ? 'A drill at low energy injured the monster. It is still sealed in the Vault and its shards still count.'
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
        '. Each drill costs ' + k.drillEnergyCost + ' energy.' }));
    const card = h('div', { class: 'card' });
    C.stats.forEach((st) => {
      const row = h('div', { class: 'stat' + (ui.flash === st ? ' flash' : '') }, [
        h('span', { class: 'lbl', text: up(st) }),
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
      cmp.append(h('span', null, [up(st) + ' ', h('b', { class: diff >= 0 ? 'ok' : 'bad', text: (diff >= 0 ? '+' : '') + num(diff) })]));
    });
    return h('div', { class: 'card alert' }, [
      h('div', { class: 'row' }, [sprite('rank-' + s.rank), h('div', null, [h('h2', { text: 'Rank fight: ' + opp.name }), elementTag(opp.element)])]),
      h('div', { class: 'small muted', text: 'Your stats against theirs:' }), cmp,
      h('button', { class: 'btn warn block', type: 'button', 'data-act': 'fight', disabled: s.offers.length > 0, text: 'Fight', onclick: fight }),
    ]);
  }

  // ---- trees
  function nodeRow(n, state, buy) {
    return h('div', { class: 'node ' + state + (n.last ? ' last' : '') }, [
      h('span', { class: 'chk' }),
      h('div', null, [h('div', { class: 'nm', text: n.name }), h('div', { class: 'ds', text: n.desc })]),
      buy || h('span', { class: 'small muted num', text: n.cost + ' SP' }),
    ]);
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
    main.append(h('p', { class: 'small muted', text: 'A chase tree unlocks when every condition holds, then it is offered as a draft. It locks again if a condition stops holding.' }));
    s.chase.forEach((c) => {
      const t = C.trees[c.tree];
      const slotted = s.slots.some((x) => x && x.tree === c.tree);
      main.append(h('div', { class: 'card' }, [
        h('div', { class: 'row between' }, [h('b', { text: t.name }), h('span', { class: 'tag', text: slotted ? 'slotted' : c.unlocked ? 'unlocked' : 'locked' })]),
        ...t.conditions.map((txt, i) => h('div', { class: 'cond' + (c.met[i] ? ' met' : '') }, [h('span', { text: c.met[i] ? '✓' : '○' }), h('span', { text: txt })])),
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
    main.append(h('button', { class: 'btn small', type: 'button', 'data-act': 'abandon', text: 'Abandon this run', onclick: () => {
      if (window.confirm('Abandon this run? Nothing is sealed in the Vault.')) { run.abandon(); resetRunUi(); ui.view = 'home'; render(); }
    } }));
  }

  // ---- modal: fight result and tree drafts
  function renderModal(modal, s) {
    modal.replaceChildren();
    if (ui.fight) { modal.hidden = false; fightModal(modal, ui.fight, s); return; }
    if (s.offers.length) { modal.hidden = false; offerModal(modal, s); return; }
    modal.hidden = true;
  }

  function actionLine(x, foeName, youName) {
    const who = (id) => (id === 'monster' ? youName : foeName);
    const a = who(x.actor), t = x.target ? who(x.target) : 'all';
    let body;
    if (x.evaded) body = 'dodged';
    else if (x.kind === 'heal') body = 'heals ' + Math.round(x.amount);
    else if (x.kind === 'shield') body = 'shields ' + Math.round(x.amount);
    else if (x.amount > 0) body = Math.round(x.amount) + ' dmg';
    else body = 'applied';
    return { text: a + ' · ' + (x.skill || (x.counter ? 'Counter' : 'Attack')) + ' → ' + t + ': ' + body + (x.crit ? ' CRIT' : ''), you: x.actor === 'monster' };
  }

  function fightModal(modal, f, s) {
    const you = speciesDef(s.species).name, foe = rank(f.rankBefore).name;
    const mine = f.units.find((u) => u.side === 0), theirs = f.units.find((u) => u.side === 1);
    modal.append(
      h('h2', { class: f.won ? 'ok' : 'bad', text: f.won ? 'Victory! Promoted to ' + rank(f.rankAfter).name : f.outcome === 'TimedOut' ? 'Time ran out: no promotion' : 'Defeat: no promotion', 'data-id': 'fight-title' }),
      f.won ? h('p', { class: 'gems', text: '+' + f.sp + ' skill points' }) : h('p', { class: 'muted', text: 'Train more and try again next cycle.' }),
      h('div', { class: 'units' }, [
        h('div', { class: 'unit' }, [h('b', { text: you }), bar(mine.hp, mine.maxHp, Math.round(mine.hp) + '/' + Math.round(mine.maxHp)), h('small', { class: 'muted', text: Math.round(mine.damage) + ' dmg · ' + mine.crits + ' crits · ' + mine.evades + ' dodges' })]),
        h('div', { class: 'unit foe' }, [h('b', { text: foe }), bar(theirs.hp, theirs.maxHp, Math.round(theirs.hp) + '/' + Math.round(theirs.maxHp)), h('small', { class: 'muted', text: Math.round(theirs.damage) + ' dmg · ' + theirs.crits + ' crits · ' + theirs.evades + ' dodges' })]),
      ]),
      h('h3', { text: 'Battle log' }),
    );
    const lines = f.actions.map((x) => actionLine(x, foe, you));
    const shown = ui.showAll ? lines : lines.slice(0, 24);
    modal.append(h('ul', { class: 'alog' }, shown.map((l) => h('li', { class: l.you ? 'you' : 'foe', text: l.text }))));
    if (lines.length > shown.length) modal.append(h('button', { class: 'btn small', type: 'button', text: 'Show all ' + f.actionCount + ' actions', onclick: () => { ui.showAll = true; render(); } }));
    if (f.actionCount > f.actions.length) modal.append(h('p', { class: 'small muted', text: 'Log capped at ' + f.actions.length + ' of ' + f.actionCount + ' actions.' }));
    modal.append(h('button', { class: 'btn primary block', type: 'button', 'data-act': 'fight-continue', text: 'Continue', onclick: () => { ui.fight = null; ui.showAll = false; render(); } }));
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
    main.append(h('h2', { text: 'Raise a monster' }),
      h('p', { class: 'muted', text: 'Train over ' + C.consts.runCycles + ' cycles. Each cycle: ' + C.consts.runAp + ' actions (Drill a stat or Rest), then a rank fight. Win to rank up, spend skill points on trees, and avoid injury.' }));
    C.species.forEach((sp) => {
      const sel = ui.pick === sp.id;
      const mini = h('div', { class: 'mini' });
      C.stats.forEach((st) => mini.append(h('span', null, [up(st) + ' ', h('b', { text: num(sp.base[st]) })])));
      main.append(h('button', { class: 'card species' + (sel ? ' sel' : ''), type: 'button', 'data-act': 'species-' + sp.id, onclick: () => { ui.pick = sp.id; render(); } }, [
        sprite(sp.id),
        h('div', null, [h('div', { class: 'row' }, [h('b', { text: sp.name }), elementTag(sp.element)]), mini,
          h('small', { class: 'muted', text: 'Trees: ' + sp.trees.map(treeName).join(', ') })]),
      ]));
    });
    const bookIds = Object.keys(C.books);
    if (bookIds.length) {
      const cb = h('input', { type: 'checkbox', 'data-act': 'book-toggle', onchange: (e) => { ui.book = e.target.checked; } });
      cb.checked = ui.book;
      main.append(h('label', { class: 'small muted row' }, [cb, h('span', { text: 'Test: start with ' + C.books[bookIds[0]].name })]));
    }
    main.append(h('button', { class: 'btn primary block', type: 'button', 'data-act': 'start', disabled: !ui.pick, text: ui.pick ? 'Start run' : 'Pick a species', onclick: startRun }));

    const vault = run.vault();
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
      h('div', { class: 'center small muted', text: e.fightsWon + ' of ' + e.fightsFought + ' rank fights won. Sealed in the Vault.' }),
    );
    const card = h('div', { class: 'card' }, [h('h3', { text: 'Stat breakdown' })]);
    C.stats.forEach((st) => card.append(h('div', { class: 'stat', style: 'grid-template-columns:38px 1fr' }, [h('span', { class: 'lbl', text: up(st) }), bar(e.stats[st], C.consts.statMax, num(e.stats[st]))])));
    main.append(card);
    main.append(h('div', { class: 'card' }, [h('h3', { text: 'Trees' }),
      ...e.trees.map((t) => h('div', { class: 'row between' }, [h('span', { text: treeName(t.id) }), h('span', { class: 'small muted num', text: t.bought + '/' + (C.trees[t.id] ? C.trees[t.id].nodes.length : '?') })]))]));
    main.append(h('div', { class: 'small muted center', text: 'Run hash ' + e.hash.slice(0, 16) }));
    main.append(h('button', { class: 'btn primary block', type: 'button', 'data-act': 'new-run', text: 'New run', onclick: () => { run.abandon(); resetRunUi(); ui.view = 'home'; render(); } }));
  }

  // ------------------------------------------------------------ actions
  function resetRunUi() { ui.feed = []; ui.last = null; ui.fight = null; ui.sealed = null; ui.tab = 'train'; ui.showAll = false; }
  const log = (line) => { ui.feed.unshift(line); };

  function startRun() {
    if (!ui.pick) return;
    resetRunUi();
    run.start(ui.pick, { books: ui.book ? [Object.keys(C.books)[0]] : [] });
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
    const r = run.seal();
    if (!r.ok) { toast(r.reason); return; }
    ui.sealed = r.entry; ui.view = 'grade'; render();
  }

  // ------------------------------------------------------------ boot
  async function boot() {
    try {
      run = await RE.create({});
      C = run.content;
      ui.pick = C.species[0].id;
      if (run.load()) {
        const s = run.state;
        const done = s.phase === 'finished' ? run.vault().find((e) => e.hash === s.hash) : null;
        if (done) { ui.sealed = done; ui.view = 'grade'; } else ui.view = 'run';
      }
      render();
    } catch (e) {
      $('main').replaceChildren(h('h2', { text: 'The game did not load' }), h('p', { class: 'err', text: e && e.message ? e.message : String(e) }));
    }
  }
  boot();
})();
