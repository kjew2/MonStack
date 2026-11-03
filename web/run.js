// Monster Trainer run screen: the UI shell over the C# training-run engine (web/run-engine.js). No game rule lives here;
// it sends commands, reads the engine's state and labels, and draws them (specs/13 T6, specs/14, specs/15).
(function () {
  'use strict';
  const RE = window.RunEngine, SP = window.IdleSprites, TIP = window.Tips, VAULT = window.VaultView;
  const $ = (id) => document.getElementById(id);
  const pct = (x) => Math.round(x * 100) + '%';
  const up = (s) => String(s).toUpperCase();
  const num = (v) => (Number.isInteger(v) ? String(v) : v.toFixed(1));

  let run = null, C = null, shownView = null, shownHomeTab = null;   // shownView: the view the last render drew (shownHomeTab: and the home tab)
  const ui = {
    view: 'home',            // home | run | climb | grade | vendor | tower | endless
    tab: 'train',            // train | trees | ladder | log
    homeTab: 'raise',        // the home screen's tab: raise | vault | talents | dungeon | goals
    pick: null,              // species chosen on the home screen
    showLocked: false,       // the Raise tab's locked species list is open
    howto: false,            // the Raise tab's "How a run works" is open
    carousel: { x: 0, glide: false, timer: 0, el: null },   // the species carousel: its scroll, whether to glide to the picked card, the swipe-settle timer, the kept row
    book: false,             // test option: start with a skill book
    feed: [],                // newest first
    last: null,              // last drill/rest result, shown under the stats
    flash: null,             // the drill to flash its stat row for: {stat, tier}
    drillPop: null,          // the drill outcome not yet popped over its stat row (issue #95): {stat, tier, gain, sad}, shown once no modal covers it
    fight: null,             // fight result shown in the modal
    play: null,              // fight playback in progress: {f: the fight, i: actions shown, timer, dom}; null when showing the result
    speed: '1',              // fight playback speed: '1', '2' or 'skip' (remembered, see loadSpeed)
    showAll: false,          // fight log expanded
    sealed: null,            // Vault entry of the finished run
    payout: 0,               // shards that run paid
    ticketGain: null,        // Dungeon ticket pieces that run earned and the whole tickets they made ({pieces, tickets})
    climb: null,             // the sealed monster's gauntlet floors for the wheel: { data, t0, done, raf }
    spGains: [],             // skill point gains not yet shown by the HUD (issue #86): [{n, why, cycle}], shown once no modal covers it
    fightGains: [],          // the gains the shown fight brought, for its result badge
    earned: [],              // achievements the sealed run earned (specs/18 section 8), shown on the grade screen
    towerTeam: [],           // Dungeon team: Vault indices in slot order (specs/21 section 8)
    towerResult: null,       // the last attempt's result from the engine (run.runTower) plus its day and team, shown on the tower screen
    offer: { key: null, pick: 0, open: false },   // the tree offer on screen: its key, the picked card, the node list open
    treeOpen: {},            // Trees tab: the slots whose full node list is open, by 'slot:tree' (a new tree in the slot starts closed)
    heldToasts: [],          // toasts waiting for the tree offer to close (issue #115: they covered it)
    towerFight: null,        // the tower floor fight shown in the playback modal (issue #98), or null
    reveal: null,            // the merchant's card reveal on the vendor screen (issue #108): {i, phase: 'hint'|'flip', timer}, or null
    endlessTeam: [],         // Endless Tower team: exactly teamSize Vault indices in slot order (specs/28 section 2)
    endlessTab: 'team',      // the Endless screen's section: 'team', 'floors' or 'board' (issue #130: one phone screen each)
    endlessPickOpen: false,  // the Vault list under a full Endless team, unfolded to swap a monster
    endlessResult: null,     // the last climb's result from the engine (run.runEndless) plus its season and team, shown on the Endless screen
    endlessFight: null,      // the Endless floor fight shown in the playback modal, or null
    nameEdit: null,          // the season board's name field while it is open: {draft, error}, else null
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
  // A species' battle sprite: the generated art (apex form) when it has some (specs/26 section 8), else the 12x12 pixel art,
  // else a seeded sprite. An image that fails to load falls back to the pixel art. size: an extra class ('small' is 32px).
  function sprite(key, apex, size) {
    const art = SP.art(key, apex);
    if (!art) { const cv = pixelSprite(key, apex); if (size) cv.classList.add(size); return cv; }
    const img = h('img', { class: 'sprite art' + (size ? ' ' + size : ''), src: art.sprite, alt: '', 'aria-hidden': 'true', draggable: 'false' });
    img.addEventListener('error', () => { const cv = pixelSprite(key, apex); cv.className = img.className.replace(' art', ''); img.replaceWith(cv); }, { once: true });
    return img;
  }
  function pixelSprite(key, apex) {
    const cv = h('canvas', { class: 'sprite', 'aria-hidden': 'true' });
    SP.draw(cv, SP.species(key, apex) || SP.enemy(hash(key) % 100000, false, 0));   // hand-drawn species art (apex form), else a seeded sprite
    return cv;
  }
  // A rank opponent's sprite (a ladder step or a fight's opponent, BRIDGE.md): its own shape, the boss horns at the top rank, tinted
  // by its element. A legacy run's flat rank (id null) keys on the rank.
  function oppSprite(o, cls) {
    const cv = h('canvas', { class: cls || 'sprite', 'aria-hidden': 'true' });
    const el = C.elements[o.element];
    SP.draw(cv, SP.monster(hash('rank-' + (o.id || o.rank)) % 100000, o.boss, el && el.color));
    return cv;
  }
  // An opponent's skills as tags; tapping one opens the skill's tooltip (skillTips, from the engine).
  function oppSkills(ids) {
    if (!ids.length) return h('small', { class: 'muted', text: 'Basic attacks only' });
    return h('div', { class: 'oppskills' }, ids.map((id) => h('span', { class: 'tag tipword', text: C.skillTips[id] ? C.skillTips[id].title : id,
      'data-id': 'oppskill-' + id, ...TIP.attrs('mskill:' + id) })));
  }
  // Toasts (issue #115): none covers a tree offer. A toast raised while an offer is on screen, or just before the render that opens
  // one (a drill that reaches a milestone), waits until the offer closes; any already showing when an offer opens goes back in the
  // queue. The check runs after the current render (a microtask) and after every render.
  let toastFlushQueued = false;
  function toast(msg) {
    ui.heldToasts.push(msg);
    if (toastFlushQueued) return;
    toastFlushQueued = true;
    queueMicrotask(() => { toastFlushQueued = false; flushToasts(); });
  }
  const offerOnScreen = () => !$('modal').hidden && !!$('modal').querySelector('[data-id=offer-title]');
  function flushToasts() {
    const box = $('toasts');
    if (offerOnScreen()) {
      if (box.children.length) ui.heldToasts = [...box.children].map((t) => t.textContent).concat(ui.heldToasts);
      box.replaceChildren();
      return;
    }
    const msgs = ui.heldToasts;
    ui.heldToasts = [];
    msgs.forEach(showToast);
  }
  function showToast(msg) {
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
  // A screen's main buttons, kept at the bottom of its scrolling pane (issue #130). Append it last.
  const actionBar = (kids) => h('div', { class: 'actionbar', 'data-id': 'actionbar' }, kids);
  // A screen header with Back on the left (issue #130): the way out is at the top, not after the whole page.
  const screenHead = (title, act, onBack, right) => h('div', { class: 'screenhead' }, [
    h('button', { class: 'btn small', type: 'button', 'data-act': act, 'aria-label': 'Back', text: '‹ Back', onclick: onBack }), h('h2', { text: title }), right]);
  // The eight stats as two columns of compact bars (grade screen, Vault detail).
  const statGrid = (stats) => h('div', { class: 'statgrid' }, C.stats.map((st) => h('div', { class: 'stat' }, [
    h('span', { class: 'lbl', text: up(st), ...TIP.attrs('stat:' + st) }), bar(stats[st], C.consts.statMax, num(stats[st]))])));
  const speciesDef = (id) => C.species.find((s) => s.id === id);
  const rank = (i) => C.ranks[i];
  const elementTag = (e) => h('span', { class: 'tag', text: (C.elements[e] || { name: e }).name, style: C.elements[e] && C.elements[e].color ? 'border-color:' + C.elements[e].color : '' });
  const treeName = (id) => (C.trees[id] ? C.trees[id].name : id);
  // A tree as drawn: its content definition, or for a rolled Awakening tree (specs/15 section 4.1) its own nodes and boosts. A
  // rolled node's id names its rolled content and its tooltip comes with it, so it is registered under that id.
  function treeView(id, rolled) {
    const t = C.trees[id];
    if (!rolled || !t) return t;
    rolled.nodes.forEach((n) => { if (n.tip && C.nodeTips) C.nodeTips[n.id] = n.tip; });
    return Object.assign({}, t, { nodes: rolled.nodes, boosts: rolled.boosts, rolledStat: rolled.stat });
  }

  // ------------------------------------------------------------ rendering
  function render() {
    renderView();
    flushToasts();   // held toasts show once no tree offer is on screen
  }
  function renderView() {
    const main = $('main'), tabs = $('tabs'), modal = $('modal');
    const prevScroll = main.scrollTop;
    main.replaceChildren();
    if (ui.view !== 'run') $('floats').replaceChildren();
    const wasView = shownView; shownView = ui.view;
    if (ui.view === 'home') {   // the Vault's detail and compare sheets open in the modal over the home screen
      modal.hidden = true; modal.replaceChildren(); renderHome(main);
      tabs.hidden = false; renderHomeTabs(tabs);
      main.scrollTop = wasView === 'home' && shownHomeTab === ui.homeTab ? prevScroll : 0;   // a lock or dismantle deep in a long Vault keeps the place; a new tab opens at its top
      shownHomeTab = ui.homeTab;
      return;
    }
    if (ui.view === 'tower') {   // a floor playback (issue #98) opens in the modal over the tower screen
      tabs.hidden = true; modal.hidden = true; modal.replaceChildren(); renderTower(main, modal);
      if (wasView === 'tower') main.scrollTop = prevScroll;
      return;
    }
    if (ui.view === 'endless') {   // a floor playback opens in the modal over the Endless screen (the rulebook editor draws its own overlay)
      tabs.hidden = true; modal.hidden = true; modal.replaceChildren(); renderEndless(main, modal);
      if (wasView === 'endless') main.scrollTop = prevScroll;
      return;
    }
    const s = run.state;
    if (ui.view === 'grade') { tabs.hidden = true; modal.hidden = true; renderGrade(main); return; }
    if (ui.view === 'climb') { tabs.hidden = true; modal.hidden = true; renderClimb(main); return; }
    if (ui.view === 'vendor') { tabs.hidden = true; modal.hidden = true; renderVendor(main); return; }
    tabs.hidden = false;
    main.append(renderHud(s));
    if (ui.tab === 'train') renderTrain(main, s);
    else if (ui.tab === 'trees') renderTrees(main, s);
    else if (ui.tab === 'ladder') renderLadder(main, s);
    else renderLog(main, s);
    renderTabs(tabs, s);
    renderModal(modal, s);
    main.scrollTop = prevScroll;
    const due = modal.hidden && main.querySelector('[data-focus="1"]');
    if (due) due.scrollIntoView({ block: 'nearest' });   // before the drill pop, which is placed by where the stage ends up
    if (ui.tab !== 'train') $('floats').querySelectorAll('.dpop').forEach((e) => e.remove());   // a drill's pop belongs to the stage, not the other tabs
    if (modal.hidden) { showSpGains(); showDrillPop(); } else $('floats').replaceChildren();
  }

  // ---- skill point gains (issue #86): a float-up next to the HUD's SP count and a pulse on it. Gains made behind a modal (a fight,
  // an Awakening) wait until it closes. The floats live in a layer outside #main so a re-render does not cut them short.
  function showSpGains() {
    const gains = ui.spGains, at = document.querySelector('#main [data-id=sp]');
    if (!gains.length || !at) return;
    ui.spGains = [];
    at.classList.add('pulse');
    const layer = $('floats'), app = $('app').getBoundingClientRect(), mainTop = $('main').getBoundingClientRect().top;
    const r = at.getBoundingClientRect();
    gains.forEach((g, i) => {
      const el = h('div', { class: 'spgain', 'data-id': 'sp-gain', text: '+' + g.n + ' SP (' + g.why + ')',
        style: 'left:' + Math.max(8, r.left - app.left) + 'px;top:' + (Math.max(r.top, mainTop) - app.top + 18 + i * 22) + 'px;animation-delay:' + i * 0.15 + 's' });
      const sps = layer.querySelectorAll('.spgain');
      for (let j = 0; j < sps.length - 3; j++) sps[j].remove();
      layer.append(el);
      setTimeout(() => el.remove(), 2200);
    });
  }

  // ---- drill outcome pop (issue #95): "+x" over the monster on the stage, styled by the tier (fail, success, super), and the monster
  // hops on a gain, or droops sadly on a failed or injuring drill. Like the SP gains it lives in #floats and waits out a modal (a milestone's Awakening).
  function showDrillPop() {
    const p = ui.drillPop, mon = p && document.querySelector('#main [data-id=stage-mon]');
    if (!mon) { if (ui.tab !== 'train') ui.drillPop = null; return; }
    ui.drillPop = null;
    if (p.sad) mon.classList.add('sad');
    else if (p.gain) mon.classList.add('hop');
    const layer = $('floats'), app = $('app').getBoundingClientRect(), r = mon.getBoundingClientRect();
    const pane = $('main').getBoundingClientRect();   // a fight card drawn above can push the stage down: keep the pop on screen
    const y = Math.min(Math.max(r.top + r.height * 0.4, pane.top + 40), pane.bottom - 30);
    layer.querySelectorAll('.dpop').forEach((e) => e.remove());   // one at a time: a fast second drill replaces the first
    const el = h('div', { class: 'dpop ' + p.tier, 'data-id': 'drill-pop',
      text: '+' + num(p.gain), style: 'left:' + (r.left + r.width / 2 - app.left) + 'px;top:' + (y - app.top) + 'px' });
    layer.append(el);
    setTimeout(() => el.remove(), 2000);
  }

  function renderTabs(tabs, s) {
    tabs.replaceChildren();
    const canBuy = s.slots.some((t) => t && treeView(t.tree, t.rolled).nodes[t.bought] && treeView(t.tree, t.rolled).nodes[t.bought].cost <= s.sp);
    [['train', 'Train'], ['trees', 'Trees'], ['ladder', 'Ladder'], ['log', 'Log']].forEach(([id, label]) => {
      const b = h('button', { type: 'button', role: 'tab', 'aria-selected': String(ui.tab === id), 'data-act': 'tab-' + id, text: label,
        onclick: () => { ui.tab = id; render(); } });
      // Unspent skill points on the Trees tab ("Trees · 4"), bright when they can buy a node (issue #86).
      if (id === 'trees' && s.sp > 0) {
        b.append(h('span', { class: 'tabsp num' + (canBuy ? ' gems' : ''), 'data-id': 'tab-sp', text: ' · ' + s.sp }));
        b.setAttribute('aria-label', 'Trees, ' + s.sp + ' unspent skill points');
      }
      tabs.append(b);
    });
  }

  // Apex forms (specs/15 section 5.3): the evolution plays once when the run's monster takes one (a chase capstone or a fusion),
  // not on a reload of a run already in it.
  let apexSeen = null;
  function noticeApex(s) {
    const name = s.name || speciesDef(s.species).name;
    if (apexSeen && apexSeen.seed === s.seed && s.apex && s.apex !== apexSeen.apex)
      playEvolve({ species: s.species, from: apexSeen.apex, fromName: apexSeen.name, to: s.apex, toName: name });
    apexSeen = { seed: s.seed, apex: s.apex, name: name };
  }

  // ---- the evolution (presentation only): the old form glows and shakes, white silhouettes of the old and new forms swap faster
  // and faster, a white flash, then the new form in colour with a burst of sparkles. A tap skips to the end, the next tap closes.
  // Reduced motion shows the end at once. EVOLVE_MS are placeholders.
  const EVOLVE_MS = { charge: 900, morph: 2400, flash: 160 };
  let evo = null;
  function stopEvolve() {
    if (!evo) return;
    evo.timers.forEach(clearTimeout);
    evo.box.remove();
    evo = null;
  }
  function playEvolve(e) {
    stopEvolve();
    const done = 'became the ' + e.toName + '!';
    const oldS = sprite(e.species, e.from), newS = sprite(e.species, e.to);
    oldS.classList.add('evo-old'); newS.classList.add('evo-new');
    const line = h('div', { class: 'evo-line', 'data-id': 'evolve-line', 'aria-live': 'polite', text: 'What? ' + e.fromName + ' is evolving!' });
    const hint = h('small', { class: 'evo-hint', 'data-id': 'evolve-hint', text: 'Tap to skip' });
    const sparks = h('div', { class: 'evo-sparks', 'aria-hidden': 'true' });
    for (let i = 0; i < 14; i++) sparks.append(h('i', { style: '--a:' + Math.round(i * 360 / 14 + (i % 2) * 12) + 'deg;--d:' + (i % 3 ? 96 : 130) + 'px' }));
    const box = h('div', { class: 'evolve charge', 'data-id': 'evolve', role: 'dialog', 'aria-modal': 'true', 'aria-label': 'Evolution' }, [
      h('div', { class: 'evo-mon' }, [h('div', { class: 'evo-rays', 'aria-hidden': 'true' }), oldS, newS, sparks]),
      line, hint, h('div', { class: 'evo-flash', 'aria-hidden': 'true' }),
    ]);
    const me = evo = { box: box, timers: [], done: false };
    const at = (ms, f) => me.timers.push(setTimeout(() => { if (evo === me) f(); }, ms));
    const finish = () => {
      me.timers.forEach(clearTimeout); me.timers = [];
      me.done = true;
      box.className = 'evolve done';
      line.replaceChildren(h('span', { text: e.fromName + ' ' }), h('b', { text: done }));
      hint.textContent = 'Tap to continue';
    };
    const tap = () => { if (evo !== me) return; if (me.done) stopEvolve(); else finish(); };
    box.addEventListener('click', tap);
    box.addEventListener('keydown', (ev) => { if (ev.key === 'Enter' || ev.key === ' ' || ev.key === 'Escape') { ev.preventDefault(); tap(); } });
    box.tabIndex = -1;
    $('app').append(box);
    box.focus({ preventScroll: true });
    if (reducedMotion()) { finish(); return; }
    // The swaps: each gap a little shorter than the last, down to a flicker.
    const morphEnd = EVOLVE_MS.charge + EVOLVE_MS.morph;
    at(EVOLVE_MS.charge, () => { box.className = 'evolve morph'; });
    for (let t = EVOLVE_MS.charge, gap = 300, alt = false; t < morphEnd; t += gap, gap = Math.max(45, gap * 0.8)) {
      const v = alt = !alt;
      at(t + gap, () => box.classList.toggle('alt', v));
    }
    at(morphEnd, () => { box.className = 'evolve morph alt flash'; });
    at(morphEnd + EVOLVE_MS.flash, () => { finish(); box.classList.add('flash'); });
  }

  // Actions left (pips) and energy: in the HUD on the other tabs, in the corners of the Train tab's stage.
  function apPips(s) {
    const pips = h('div', { class: 'pips', 'aria-label': s.ap + ' action points left' });
    for (let i = 0; i < C.consts.runAp; i++) pips.append(h('span', { class: 'pip' + (i < s.ap ? ' full' : '') }));
    return pips;
  }
  function energyBar(s) {
    const k = C.consts;
    return bar(s.energy, k.energyMax, 'Energy ' + Math.round(s.energy) + '/' + k.energyMax, 'energy' + (s.energy / k.energyMax < 0.3 ? ' low' : ''));
  }

  // The HUD over every tab. The Train tab shows the monster and the meters on its stage instead, so its HUD is the text lines only.
  function renderHud(s) {
    const sp = speciesDef(s.species), r = rank(s.rank), k = C.consts;
    noticeApex(s);
    const staged = ui.tab === 'train';
    return h('div', { class: 'card hud' + (staged ? ' staged' : '') }, [
      staged ? null : sprite(s.species, s.apex),
      h('div', null, [
        h('div', { class: 'hud-line' }, [
          h('span', { text: s.name || sp.name, 'data-id': 'monster-name' }), elementTag(sp.element),
          h('button', { type: 'button', class: 'tag rankbtn', text: r.name, 'data-id': 'rank', 'aria-label': r.name + ': open the rank ladder',
            onclick: () => { ui.tab = 'ladder'; render(); } }),
        ]),
        h('div', { class: 'hud-line small muted' }, [
          h('span', { text: 'Cycle ' + s.cycle + '/' + k.runCycles, 'data-id': 'cycle' }),
          h('span', { text: s.sp + ' SP', class: 'spcount' + (s.sp ? ' gems' : ''), 'data-id': 'sp' }),
          h('span', { text: s.fightsWon + '/' + s.fightsFought + ' fights won' }),
        ]),
      ]),
      staged ? null : h('div', { class: 'hud-meters' }, [h('span', { class: 'small muted', text: 'Actions' }), apPips(s), energyBar(s)]),
    ]);
  }

  function renderTrain(main, s) {
    const k = C.consts;
    if (s.phase === 'finished') {
      const lastHurt = s.injuries.length ? s.injuries[s.injuries.length - 1] : null;
      main.append(h('div', { class: 'card alert' }, [
        h('h2', { text: 'Run complete' }),
        h('div', { class: 'small muted', text: 'All ' + k.runCycles + ' cycles are done.' +
          (lastHurt && !lastHurt.back ? ' An injury in cycle ' + lastHurt.cycle + ' cost the rest of the run.' : '') }),
        s.sp > 0 ? h('div', { class: 'small gems', text: s.sp + ' unspent skill points are lost when you seal. Spend them on the Trees tab first.' }) : null,
        h('button', { class: 'btn primary block', type: 'button', 'data-act': 'seal', text: 'Seal in the Vault', onclick: seal }),
      ]));
    } else if (s.phase === 'awaitfight') {
      main.append(fightCard(s));
    }
    const hurt = s.injuries.length ? s.injuries[s.injuries.length - 1] : null;
    if (hurt && hurt.back === s.cycle && s.phase === 'training') main.append(h('div', { class: 'card injury', 'data-id': 'injury' }, [
      h('b', { class: 'bad', text: ui.last && ui.last.injury ? 'Injured!' : 'Back from injury' }), h('div', { class: 'small', text: injuryText(hurt) }),
    ]));
    const busy = s.phase !== 'training' || s.offers.length > 0;
    // The monster on a stage in the middle: actions and energy in its top corners, the last action's result under it (a fixed line,
    // so feedback stays in view and nothing shifts), and the drill's "+x" pops over it (showDrillPop).
    main.append(h('div', { class: 'card stage', 'data-id': 'stage' }, [
      h('div', { class: 'stage-meters' }, [
        h('div', { class: 'stage-ap' }, [h('span', { class: 'small muted', text: 'Actions' }), apPips(s)]),
        energyBar(s),
      ]),
      h('div', { class: 'stage-mon', 'data-id': 'stage-mon' }, [sprite(s.species, s.apex)]),
      resultLine(ui.last, s),
    ]));
    if (s.phase !== 'finished') main.append(oddsChips(s));   // no drills are left once the run is over (issue #130: keep the stats in view)
    // The drill pad in the thumb zone: a 4x2 grid of stat tiles. The whole tile (label, value and growth, gauge with milestone ticks)
    // is the Drill button; the small "i" in its corner, a sibling laid over the button, opens the stat's tooltip (issue #124).
    const pad = h('div', { class: 'pad', 'data-id': 'pad' });
    C.stats.forEach((st) => {
      const v = s.stats[st];
      pad.append(h('div', { class: 'dtile st-' + st + (busy ? ' off' : '') + (ui.flash && ui.flash.stat === st ? ' flash t-' + ui.flash.tier : ''), 'data-id': 'stat-' + st }, [
        h('button', { class: 'drill', type: 'button', 'data-act': 'drill-' + st, disabled: busy, 'aria-label': 'Drill ' + up(st) + ', now ' + num(v),
          onclick: () => drill(st) }, [
          h('span', { class: 'meta' }, [h('span', { class: 'lbl', text: up(st) })]),
          h('span', { class: 'vrow' }, [
            h('span', { class: 'val num', text: num(v) }),
            h('small', { class: 'gmul num', text: 'x' + s.growth[st].toFixed(2), title: 'Growth: drill gains are multiplied by ' + s.growth[st].toFixed(2) }),
          ]),
          statGauge(s, st)]),
        h('button', { class: 'info', type: 'button', ...TIP.attrs('stat:' + st), 'data-id': 'info-' + st, 'aria-label': up(st) + ': what it does' }, [h('span', { class: 'ic', 'aria-hidden': 'true', text: 'i' })]),
      ]));
    });
    main.append(pad);
    if (s.offers.length) main.append(h('p', { class: 'gems', text: 'A tree offer is waiting. Choose or discard it to keep training.' }));
    if (s.phase !== 'finished') main.append(h('button', { class: 'btn gemsy block', type: 'button', 'data-act': 'rest', disabled: busy,
      text: 'Rest (+' + num(s.restEnergy) + ' energy)', onclick: rest }));
    ui.flash = null;
  }

  // The drill odds at this energy as one row of small cells, value over label (issue #115), all numbers from the engine
  // (RunState odds, drillCost): fail, success, super, injury (lit when above 0; what an injury loses is its title), then the
  // drill's energy cost, or Momentum's free drill, and Second Wind while it would stop the next injury.
  function oddsChips(s) {
    const o = s.odds;
    const chip = (cls, label, value, title) => h('span', { class: 'ochip ' + cls, title: title }, [label + ' ', h('b', { text: value })]);
    return h('div', { class: 'odds', 'data-id': 'odds', role: 'group', 'aria-label': 'Drill odds at this energy' }, [
      chip('o-fail', 'Fail', pct(o.fail)),
      chip('o-success', 'Success', pct(o.success)),
      chip('o-super', 'Super', pct(o.super)),
      chip('o-injury' + (o.injury > 0 ? ' on' : ''), 'Injury', pct(o.injury), 'An injury loses ' + injuryRisk() + '.'),
      s.freeDrills > 0 ? chip('o-free', 'Momentum', 'Free', 'Momentum: the next drill costs no energy.')
        : chip('o-cost', 'Energy', '-' + num(s.drillCost), 'Each drill costs ' + num(s.drillCost) + ' energy.'),
      o.injury > 0 && s.shieldsLeft > 0 ? chip('o-shield', '2nd Wind', '🛡', 'Second Wind will stop the next injury (energy drops to 0).') : null,
    ]);
  }

  // A stat as a gauge in its colour (issue #115): the value against the stat cap (the tile shows the number above it), and a tick at
  // each milestone this run crosses (RunState milestoneAt, lowered by Early Bloom), lit once the stat has reached it.
  function statGauge(s, st) {
    const max = C.consts.statMax, v = s.stats[st];
    const g = h('div', { class: 'bar gauge', title: num(v) + ' / ' + max }, [h('div', { class: 'fill', style: 'width:' + Math.max(0, Math.min(100, (v / max) * 100)) + '%' })]);
    s.milestoneAt.forEach((m) => g.append(h('i', { class: 'tick' + (v >= m ? ' hit' : ''), 'data-at': num(m), title: 'Milestone ' + num(m) + (v >= m ? ' (reached)' : ''),
      style: 'left:' + Math.max(0, Math.min(100, (m / max) * 100)) + '%' })));
    return g;
  }

  // The last action's outcome, a fixed line under the monster on the stage; before the first action of a visit, a hint.
  function resultLine(r, s) {
    if (!r) return h('div', { class: 'result hint small muted', 'data-id': 'result-hint',
      text: s.phase === 'training' && s.ap > 0 ? 'Drill a stat or Rest. ' + s.ap + ' action' + (s.ap === 1 ? '' : 's') + ' left this cycle.' : 'No actions left this cycle.' });
    if (r.kind === 'drill') {
      return h('div', { class: 'result', 'data-id': 'result' }, [
        h('div', null, [h('b', { class: r.tier, text: r.tierName }), h('span', { text: ' ' + up(r.stat) + ' ' + (r.gain ? '+' + num(r.gain) : '+0') + ' → ' + num(r.value) })]),
        r.injury ? h('div', { class: 'bad', 'data-id': 'injury-result' }, [h('b', { text: 'Injured! ' }),
          r.injury.back ? 'Back in cycle ' + r.injury.back + '.' : 'The run is over.']) : null,
        r.shielded ? h('div', { class: 'gold', 'data-id': 'shield-result' }, [h('b', { text: 'Second Wind! ' }), 'The injury was prevented; energy dropped to 0.']) : null,
        r.free ? h('div', { class: 'small gems', 'data-id': 'free-result', text: 'Momentum: this drill cost no energy.' }) : null,
        r.echoStat ? h('div', { class: 'small gold', 'data-id': 'echo-result', text: 'Echo Training: ' + up(r.echoStat) + ' +' + num(r.echoGain) + '.' }) : null,
      ]);
    }
    return h('div', { class: 'result', 'data-id': 'result' }, [h('div', null, [h('b', { text: 'Rest' }), h('span', { text: ' +' + num(r.restored) + ' energy' })])]);
  }

  // An injury (specs/14 section 4.1): what it cost and when the monster is back, from the engine's numbers.
  function cycleList(a, b) { return a === b ? 'cycle ' + a : 'cycles ' + a + '-' + b; }
  function injuryText(i) {
    const k = C.consts, parts = ['the rest of cycle ' + i.cycle];
    if (i.cycle % k.fightEvery === 0) parts.push('its rank fight');
    if (i.lost) parts.push(cycleList(i.cycle + 1, i.cycle + i.lost) + ' (no drills, fights or cycle SP)');
    return 'Lost: ' + parts.join(', ') + '. ' +
      (i.back ? 'Back in cycle ' + i.back + ' with ' + num(k.injuryRecoverEnergy) + ' energy.' : 'The run had no cycles left, so it is over.');
  }
  function injuryRisk() {
    const n = C.consts.injurySkipCycles;
    return 'the rest of the cycle and its fight' + (n ? ', then ' + (n === 1 ? 'the next cycle' : 'the next ' + n + ' cycles') : '');
  }

  // The fight card. The first time it shows for a fight (the cycle's actions are spent and no tree offer covers it) it pulses and
  // scrolls into view, so the player sees the cycle is over (issue #126).
  let fightCardSeen = null;
  function fightCard(s) {
    const opp = s.opponent;
    const cmp = h('div', { class: 'mini' });
    C.stats.forEach((st) => {
      const diff = s.stats[st] - opp.stats[st];
      cmp.append(h('span', TIP.attrs('stat:' + st + ':vs'), [up(st) + ' ', h('b', { class: diff >= 0 ? 'ok' : 'bad', text: (diff >= 0 ? '+' : '') + num(diff) })]));
    });
    const key = s.seed + ':' + s.fightsFought + ':' + s.cycle, fresh = fightCardSeen !== key && !s.offers.length;
    if (fresh) fightCardSeen = key;
    return h('div', { class: 'card alert fightcard' + (fresh && !reducedMotion() ? ' due' : ''), 'data-id': 'fight-card', 'data-focus': fresh ? '1' : null }, [
      h('div', { class: 'row' }, [oppSprite(opp), h('div', { class: 'grow' }, [
        h('h2', { text: 'Rank ' + opp.letter + ' fight: ' + opp.name, 'data-id': 'fight-opp' }), elementTag(opp.element), oppSkills(opp.skills)])]),
      h('div', { class: 'small muted', text: 'Your stats against theirs:' }), cmp,
      ...((m) => [m.element ? h('div', { class: 'vsel ' + m.element.cls, 'data-id': 'fight-element', text: m.element.text }) : null, stakesLine(m)])(matchup(s)),
      speedToggle(),
      h('button', { class: 'btn warn block', type: 'button', 'data-act': 'fight', disabled: s.offers.length > 0, text: 'Fight', onclick: fight }),
    ]);
  }

  // ---- the matchup of the coming rank fight (issue #126), read from the run state before the fight: both sides (the intro), the
  // element edge (the engine's multipliers, BRIDGE.md opponent.elementDealt/elementTaken) and the stakes (the fight card).
  function matchup(s) {
    const k = C.consts, opp = s.opponent, sp = speciesDef(s.species);
    const elName = (e) => (C.elements[e] || { name: e }).name;
    const x = (m) => '×' + String(Math.round(m * 100) / 100);
    const dealt = typeof opp.elementDealt === 'number' ? opp.elementDealt : 1, taken = typeof opp.elementTaken === 'number' ? opp.elementTaken : 1;
    let element = null;
    if (dealt > 1 && taken > 1) element = { cls: 'mixed', text: elName(sp.element) + ' and ' + elName(opp.element) + ' beat each other: both hit ' + x(dealt) + '.' };
    else if (dealt > 1) element = { cls: 'ok', text: 'Your ' + elName(sp.element) + ' beats their ' + elName(opp.element) + ': you hit ' + x(dealt) + (taken < 1 ? ', they hit ' + x(taken) : '') + '.' };
    else if (taken > 1) element = { cls: 'bad', text: 'Their ' + elName(opp.element) + ' beats your ' + elName(sp.element) + ': they hit ' + x(taken) + (dealt < 1 ? ', you hit ' + x(dealt) : '') + '.' };
    const topRank = s.rank >= C.ranks.length - 1, next = s.cycle + k.fightEvery, lossSp = (s.mods && s.mods.lossSp) || 0;
    const win = (topRank ? 'stay at ' + rank(s.rank).name + ', the top' : 'promote to ' + rank(s.rank + 1).name) + (k.spPerWin ? ', +' + k.spPerWin + ' SP' : '') + '.';
    const lose = (next > k.runCycles ? 'no rematch: it is the run\'s last fight' : 'rematch ' + (k.fightEvery === 1 ? 'next cycle' : 'in cycle ' + next))
      + (lossSp ? ', +' + lossSp + ' SP (Grudge Match)' : '') + '.';
    return {
      you: { name: s.name || sp.name, species: s.species, apex: s.apex, rank: rank(s.rank).name, element: sp.element },
      opp: opp, element: element, win: win, lose: lose,
    };
  }
  function stakesLine(m) {
    return h('div', { class: 'stakes small', 'data-id': 'fight-stakes' }, [
      h('span', null, [h('b', { class: 'ok', text: 'Win: ' }), m.win]), ' ',
      h('span', null, [h('b', { class: 'bad', text: 'Lose: ' }), m.lose]),
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
    const firstEmpty = s.slots.indexOf(null);
    s.slots.forEach((slot, i) => {
      if (!slot) {   // one slim line per empty slot, the explanation once (issue #130: they pushed the chase trees off screen)
        main.append(h('div', { class: 'card slotcard empty' }, [h('b', { text: 'Slot ' + (i + 1) + ': empty' }),
          i === firstEmpty ? h('small', { text: 'Fills from tree offers (Awakenings at stat milestones, books, chase unlocks).' }) : null]));
        return;
      }
      const t = treeView(slot.tree, slot.rolled);
      const card = h('div', { class: 'card slotcard', 'data-id': 'slot-' + i }, [
        h('div', { class: 'row between' }, [
          h('b', { text: 'Slot ' + (i + 1) + ': ' + t.name }),
          h('span', null, [rolledChip(t), ' ', h('span', { class: 'tag', text: t.kind }), ' ', h('span', { class: 'small muted num', text: slot.bought + '/' + t.nodes.length })]),
        ]),
        fusionLine(s, slot, t),
        nodePath(t, slot.bought),
      ]);
      // The path tiles show the whole tree (issue #115); below them only the next node to buy, the full list one tap away.
      const next = t.nodes[slot.bought];
      if (next) card.append(nodeRow(next, 'next', h('button', { class: 'btn small primary', type: 'button', 'data-act': 'buy-' + i, disabled: s.sp < next.cost,
        text: 'Buy ' + next.cost + ' SP', onclick: () => spend(i) })));
      else card.append(h('small', { class: 'ok', 'data-id': 'maxed-' + i, text: '✓ Every node bought' }));
      const key = i + ':' + slot.tree, open = !!ui.treeOpen[key];
      card.append(h('button', { class: 'linkish', type: 'button', 'data-act': 'slot-nodes-' + i, 'aria-expanded': String(open),
        text: open ? 'Hide nodes ▴' : 'All ' + t.nodes.length + ' nodes ▾', onclick: () => { ui.treeOpen[key] = !open; render(); } }));
      if (open) card.append(h('div', { 'data-id': 'nodes-' + i }, t.nodes.map((n, j) => nodeRow(n, j < slot.bought ? 'got' : j === slot.bought ? 'next' : 'locked'))));
      main.append(card);
    });

    main.append(h('h3', { text: 'Chase trees' }));
    main.append(h('p', { class: 'small muted', text: 'Each species has two chase trees of its own: one unlocks from its own trees and stats, the other also needs a skill book tree. A chase tree unlocks when every condition holds, then it is offered. It locks again if a condition stops holding.' }));
    s.chase.forEach((c) => {
      const t = C.trees[c.tree];
      const slotted = s.slots.some((x) => x && x.tree === c.tree);
      main.append(h('div', { class: 'card', 'data-id': 'chasecard-' + c.tree }, [
        h('div', { class: 'row between' }, [h('b', { text: t.name }), h('span', null, [
          h('span', { class: 'tag', text: t.bookGated ? 'book-gated' : 'in-run' }), ' ',
          h('span', { class: 'tag', text: !c.open ? 'dormant' : slotted ? 'slotted' : c.unlocked ? 'unlocked' : 'locked' })])]),
        ...(c.open ? [] : [h('small', { class: 'muted', text: 'This run was started under the old attunement rule and was not attuned, so this tree cannot unlock in it. New runs always can.' })]),
        ...t.conditions.map((txt, i) => h('div', { class: 'cond' + (c.met[i] ? ' met' : '') }, [h('span', { text: c.met[i] ? '✓' : '○' }), h('span', { text: txt })])),
        ...conditionSources(t),
        // Debug only: play this tree's evolution without buying its capstone (nothing in the run changes).
        debugOn() && t.apexName ? h('button', { class: 'btn small', type: 'button', 'data-act': 'evolve-preview-' + c.tree, text: 'Test: preview evolution',
          onclick: () => playEvolve({ species: s.species, from: s.apex, fromName: s.name || speciesDef(s.species).name, to: c.tree, toName: t.apexName }) }) : null,
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

  // Fusion (specs/15 section 5.6; kj: each fusing pair must be clear): a fused tree names its parents, a tree that can fuse names
  // its partner and the fused tree, with a check once the partner is slotted and maxed too.
  function fusionLine(s, slot, t) {
    if (slot.absorbed && slot.absorbed.length)
      return h('small', { class: 'gold', 'data-id': 'fused-from' }, 'Fused from ' + slot.absorbed.map((a) => treeName(a.tree)).join(' + ') + ': their nodes still count');
    if (!t.fusesInto) return null;
    const partner = s.slots.find((x) => x && x.tree === t.fusesWith);
    const ready = partner && partner.bought >= C.trees[t.fusesWith].nodes.length;
    return h('small', { class: ready ? 'ok' : 'muted', 'data-id': 'fuses-with' },
      '⚭ Max this and ' + treeName(t.fusesWith) + ' to fuse them into ' + treeName(t.fusesInto) + (partner ? (ready ? ' (partner ready ✓)' : ' (partner slotted)') : ''));
  }

  // ---- the rank ladder (specs/14 section 5): one opponent per rank, rolled at the start of the run, all visible from the start.
  // Top rank first, so the list reads as the climb; beaten and current come from the engine (RunState.ladder).
  function renderLadder(main, s) {
    main.append(h('div', { class: 'row between' }, [h('h2', { text: 'Rank ladder' }), h('span', { class: 'small muted', text: rank(s.rank).name })]),
      h('p', { class: 'small muted', text: 'Rolled when this run started. Win a rank fight to climb to the next opponent; a loss means a rematch with the same one.' }));
    const list = h('div', { class: 'ladder', 'data-id': 'ladder' });
    s.ladder.slice().reverse().forEach((o) => {
      const state = o.current ? 'current' : o.beaten ? 'beaten' : 'ahead';
      list.append(h('div', { class: 'card rung ' + state + (o.boss ? ' boss' : ''), 'data-id': 'ladder-' + o.rank }, [
        h('span', { class: 'letter', text: o.letter }),
        oppSprite(o),
        h('div', { class: 'grow' }, [
          h('div', { class: 'row between' }, [h('b', { text: o.name }), h('span', { class: 'tag', text: o.current ? 'next fight' : o.beaten ? 'beaten ✓' : 'ahead' })]),
          elementTag(o.element),
          oppSkills(o.skills),
        ]),
      ]));
    });
    main.append(list);
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

  // ---- modal: fight result and tree offers
  function renderModal(modal, s) {
    modal.replaceChildren();
    modal.classList.toggle('bleed', !!(ui.fight && ui.play && ui.play.intro));   // the intro fills the screen edge to edge
    if (ui.fight && ui.play && ui.play.intro) { modal.hidden = false; fightIntro(modal, ui.play.intro.m); return; }
    if (ui.fight && ui.play) { modal.hidden = false; fightPlayView(modal, ui.fight, rankView(ui.fight, s)); return; }
    if (ui.fight) { modal.hidden = false; fightModal(modal, ui.fight, s); return; }
    if (s.offers.length) { modal.hidden = false; offerModal(modal, s); return; }
    modal.hidden = true;
  }

  function actionLine(x, foeName, youName) {
    const who = (id) => (id === 'monster' ? youName : foeName);
    const a = who(x.actor), t = x.target ? who(x.target) : 'all';
    return { text: a + ' · ' + actionName(x) + ' → ' + t + ': ' + actionResult(x), you: x.actor === 'monster', skillId: x.skillId };
  }
  const actionName = (x) => x.skill || x.event || (x.counter ? 'Counter' : 'Attack');
  function actionResult(x) {
    let body;
    if (x.event) body = x.amount > 0 ? 'restores ' + Math.round(x.amount) : 'activates';   // a rule modifier's event (BRIDGE.md, fight)
    else if (x.evaded) body = 'dodged';
    else if (x.kind === 'heal') body = 'heals ' + Math.round(x.amount);
    else if (x.kind === 'shield') body = 'shields ' + Math.round(x.amount);
    else if (x.kind === 'buff' || x.kind === 'debuff' || x.kind === 'passive' || x.kind === 'taunt') body = 'applied';   // amount is the effect size, not damage
    else if (x.amount > 0) body = Math.round(x.amount) + ' dmg';
    else body = 'applied';
    return body + (x.graze ? ' (grazed)' : '') + (x.crit ? ' CRIT' : '') + (x.lucky ? ' · lucky!' : '');
  }

  const FIGHT_LOG_LINES = 6;   // lines shown before "Show all", so Continue fits on a phone screen without scrolling
  function fightModal(modal, f, s) {
    const you = speciesDef(s.species).name, foe = f.opponent.name;
    modal.append(
      h('h2', { class: f.won ? 'ok' : 'bad', text: f.won ? 'Victory! Promoted to ' + rank(f.rankAfter).name : f.outcome === 'TimedOut' ? 'Time ran out: no promotion' : 'Defeat: no promotion', 'data-id': 'fight-title' }),
      ...[f.won ? null : h('p', { class: 'muted', text: 'Train more and try again next cycle: ' + foe + ' waits for the rematch.' }),
        spBadge(ui.fightGains)].filter(Boolean),   // append() would print a null as the text "null"
    );
    const units = fightUnits(f, you, foe, null, rankArt(f, s), true);
    const loser = f.won ? 1 : f.outcome === 'TimedOut' ? -1 : 0;   // the side knocked down (a timeout downs no one)
    units.refs.forEach((r) => r.el.classList.toggle('down', r.u.side === loser));
    modal.append(
      h('div', { class: 'fstage ' + (f.won ? 'won' : 'lost') }, [units.root,
        h('div', { class: 'stamp', 'aria-hidden': 'true', text: f.won ? 'Victory!' : f.outcome === 'TimedOut' ? 'Time up' : 'Defeat' })]),
      units.skillRows,
      h('h3', { text: 'Battle log' }),
    );
    const lines = f.actions.map((x) => actionLine(x, foe, you));
    const shown = ui.showAll ? lines : lines.slice(0, FIGHT_LOG_LINES);
    modal.append(h('ul', { class: 'alog' }, shown.map((l) => h('li', { class: l.you ? 'you' : 'foe', text: l.text,
      ...(l.skillId ? TIP.attrs((l.you ? 'skill:' : 'mskill:') + l.skillId) : null) }))));
    if (lines.length > shown.length) modal.append(h('button', { class: 'btn small', type: 'button', text: 'Show all ' + f.actionCount + ' actions', onclick: () => { ui.showAll = true; render(); } }));
    if (f.actionCount > f.actions.length) modal.append(h('p', { class: 'small muted', text: 'Log capped at ' + f.actions.length + ' of ' + f.actionCount + ' actions.' }));
    modal.append(actionBar(h('button', { class: 'btn primary block', type: 'button', 'data-act': 'fight-continue', text: 'Continue', onclick: () => { ui.fight = null; ui.showAll = false; render(); } })));
  }

  // The fight's skill point reward as a badge (issue #86): the total, and a line per source (rank win, the next cycle).
  function spBadge(gains) {
    if (!gains.length) return null;
    const total = gains.reduce((a, g) => a + g.n, 0);
    return h('div', { class: 'spbadge', 'data-id': 'fight-sp' }, [
      h('b', { class: 'num', text: '+' + total + ' SP' }),
      h('div', null, [
        ...gains.map((g) => h('div', { text: '+' + g.n + ' skill points: ' + g.why })),
        h('div', { class: 'small muted', text: 'Spend them on the Trees tab.' }),
      ]),
    ]);
  }

  // ------------------------------------------------------------ fight playback (issue #79): presentation only
  // The engine has already resolved the fight; this replays its action log one action at a time so the player sees which ability
  // each unit uses and every skill's cooldown. HP, cooldowns and (in Dungeon fights, issue #98) entropy, the next vent and the
  // corruption level come from the engine's per-action snapshots (BRIDGE.md, fight), never computed here. Skip shows the result at
  // once, as before. The same view plays a rank fight (ui.fight) and a tower floor (ui.towerFight); ui.play.f is the one playing.
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

  // Plays fight f (a rank fight by default) unless the speed is Skip. `force` plays it even at Skip, at 1x (the tower's Watch
  // button); tapping Skip then stops it. `intro` (a rank fight's matchup, issue #126) opens with the versus intro.
  function startPlayback(f, force, intro) {
    stopPlayback();
    f = f || ui.fight;
    if ((force || ui.speed !== 'skip') && f && f.actions.length) {
      ui.play = { f: f, i: 0, timer: null, dom: null, intro: intro ? { m: intro, phase: 'vs' } : null };
      schedulePlayback();
    }
  }
  function stopPlayback() {
    if (ui.play) clearTimeout(ui.play.timer);
    ui.play = null;
  }
  function schedulePlayback() {
    const p = ui.play, speed = Number(ui.speed) || 1;
    if (p.intro) p.timer = setTimeout(stepIntro, INTRO_MS[p.intro.phase] / speed);
    else p.timer = setTimeout(stepPlayback, FIGHT_STEP_MS / speed);
  }
  function stepPlayback() {
    const p = ui.play;
    if (!p) return;
    if (p.i >= p.f.actions.length) { stopPlayback(); render(); return; }   // the last action stays up for one step
    p.i++;
    if (p.dom && p.dom.root.isConnected) updatePlay(); else render();
    schedulePlayback();
  }

  // ---- the rank fight intro (issue #126; kj, 2026-11-01: about 2 seconds, snappy, skippable; kj, 2026-11-02: pure hype, no
  // matchup, which the fight card shows before the fight): the screen splits on a diagonal, both monsters slam in with their names,
  // ranks and elements, "VS" lands, then "Fight!" and the playback. 2x halves it, the Skip speed skips it with the playback, and a
  // tap (or Enter, Space, Escape) anywhere but the speed buttons ends it at once. Reduced motion keeps the intro but drops its
  // movement. INTRO_MS are placeholders (presentation only, not a game rule).
  const INTRO_MS = { vs: 1500, go: 500 };
  function stepIntro() {
    const p = ui.play;
    if (!p || !p.intro) return;
    if (p.intro.phase === 'vs') {
      p.intro.phase = 'go';
      if (p.dom && p.dom.intro && p.dom.intro.isConnected) paintIntroPhase(p.dom.intro, 'go'); else render();
      schedulePlayback();
    } else endIntro();
  }
  function endIntro() {
    const p = ui.play;
    if (!p || !p.intro) return;
    clearTimeout(p.timer);
    p.intro = null;
    p.dom = null;
    render();
    schedulePlayback();
  }
  function paintIntroPhase(root, phase) {
    root.classList.toggle('go', phase === 'go');
    root.querySelector('[data-id=intro-vs]').textContent = phase === 'go' ? 'Fight!' : 'VS';
  }
  function fightIntro(modal, m) {
    const p = ui.play, speed = Number(ui.speed) || 1;
    const elName = (e) => (C.elements[e] || { name: e }).name;
    const side = (cls, art, name, tag) => h('div', { class: 'hyside ' + cls }, [
      h('div', { class: 'hyart' }, [art]),
      h('div', { class: 'hyplate' }, [h('b', { class: 'hyname', text: name }), h('span', { class: 'hytag', text: tag })]),
    ]);
    const anim = !p.intro.drawn && !reducedMotion();   // the entrance plays once, not again on a re-render (a speed change)
    p.intro.drawn = true;
    const root = h('div', { class: 'hype' + (anim ? ' anim' : ''), 'data-id': 'fight-intro', 'data-ms': String((INTRO_MS.vs + INTRO_MS.go) / speed),
      role: 'group', 'aria-label': 'Rank ' + m.opp.letter + ' fight: ' + m.you.name + ' versus ' + m.opp.name, tabindex: '-1' }, [
      h('div', { class: 'hyhalf you', 'aria-hidden': 'true' }), h('div', { class: 'hyhalf foe', 'aria-hidden': 'true' }), h('div', { class: 'hyslash', 'aria-hidden': 'true' }),
      h('div', { class: 'hybanner', text: 'Rank ' + m.opp.letter + ' fight' }),
      side('you', sprite(m.you.species, m.you.apex), m.you.name, m.you.rank + ' · ' + elName(m.you.element)),
      h('div', { class: 'hymark' }, [h('span', { 'data-id': 'intro-vs', 'aria-live': 'polite', text: 'VS' })]),
      side('foe', oppSprite(m.opp), m.opp.name, 'Rank ' + m.opp.letter + ' · ' + elName(m.opp.element)),
      h('div', { class: 'hyfoot' }, [h('button', { class: 'linkish', type: 'button', 'data-act': 'intro-skip', text: 'Tap to skip ▸' }), speedToggle()]),
    ]);
    // a tap anywhere ends it, except on the speed buttons (they set the speed: 2x reschedules the intro, Skip ends the fight)
    root.addEventListener('click', (e) => { if (!e.target.closest('.speed')) endIntro(); });
    root.addEventListener('keydown', (e) => {   // Escape anywhere; Enter or Space on the intro itself (a focused button keeps its own)
      if (e.key === 'Escape' || (e.target === root && (e.key === 'Enter' || e.key === ' '))) { e.preventDefault(); endIntro(); }
    });
    modal.append(root);
    paintIntroPhase(root, p.intro.phase);
    p.dom = { intro: root };
    root.focus({ preventScroll: true });
  }

  // Each unit's state after the first `upto` logged actions (null: at the end of the fight): HP and cooldowns; in a tower fight
  // also its entropy and next vent, and whether it is in the fight yet (a split phase's adds join later, BRIDGE.md).
  function unitsAt(f, upto) {
    return f.units.map((u, k) => {
      if (upto === null) return { hp: u.hp, sh: u.shield || 0, cd: u.cd, en: u.entropy, vent: u.vent, here: true };
      let cd = [];                                 // no action yet: every skill starts ready
      for (let j = upto - 1; j >= 0; j--) if (f.actions[j].actor === u.id) { cd = f.actions[j].cd; break; }
      const x = upto ? f.actions[upto - 1] : null;
      const here = x ? k < x.hp.length : !u.add;
      return { hp: x ? (here ? x.hp[k] : u.maxHp) : u.maxHp, sh: x && here && x.sh ? x.sh[k] : 0, cd: cd, en: x && x.en ? x.en[k] : 0, vent: x && x.vent ? x.vent[k] : null, here: here };
    });
  }
  const corruptionAt = (f, upto) => (upto === null ? f.corruption : upto ? f.actions[upto - 1].corruption : 0);

  // A skill tile's label: the initials of a multi-word name ("Flame Breath" -> FB), else its first two letters.
  function skillAbbr(name) {
    const words = String(name).split(/[\s-]+/).filter(Boolean);
    return (words.length > 1 ? words.slice(0, 3).map((w) => w[0]).join('') : (words[0] || '?').slice(0, 2)).toUpperCase();
  }

  // A Dungeon tooltip trigger (specs/21 section 8): text from BattleText through RunContent's towerTips (web/tooltips.js).
  const towerTip = (key) => (C.towerTips && C.towerTips[key] ? TIP.attrs('tower:' + key) : null);
  const tipName = (key) => (C.towerTips && C.towerTips[key] ? C.towerTips[key].name : key);

  // The unit panels: name, HP bar, and a square tile per active skill (issue #85) whose grey overlay shows the cooldown left and
  // recedes downward as it runs out; a tower fight adds each unit's entropy gauge and next vent. A unit's own `name` (tower
  // fights) wins over you/foe. `stage` (a rank fight, 2026-11-02) draws the two monsters big and face to face under their names and
  // HP, with a damage number over the one hit, and moves the skill tiles to rows of their own (skillRows) under the stage.
  // Returns the element, the skill rows (stage only) and refs for repainting.
  function fightUnits(f, you, foe, upto, art, stage) {
    const refs = [], tower = f.entropyCap > 0;
    const root = h('div', { class: 'units' + (tower ? ' tower' : '') }, f.units.map((u, k) => {
      const chips = u.skills.map((sk, si) => (sk.kind === 'passive' ? null : h('div', { class: 'skill', 'data-id': 'skill-' + (tower ? k : u.side) + '-' + si, 'data-kind': sk.kind }, [
        h('div', { class: 'tile', style: '--hue:' + (hash(sk.name) % 360) }, [
          h('div', { class: 'cdfill' }), h('span', { class: 'ab', 'aria-hidden': 'true', text: skillAbbr(sk.name) }), h('span', { class: 'st num', 'aria-hidden': 'true' }),
        ]),
        h('span', { class: 'nm', text: sk.name }),
      ])));
      const hpBar = bar(0, 1, '', 'hp');
      hpBar.insertBefore(h('div', { class: 'shield', 'data-id': 'shield-' + (tower ? k : u.side) }), hpBar.querySelector('span'));
      let entropy = null;
      if (tower) {
        entropy = { bar: bar(0, f.entropyCap, '', 'ent'), vent: h('span', { class: 'vent small tipword', 'data-id': 'vent-' + k }) };
        entropy.row = h('div', { class: 'entrow', 'data-id': 'entropy-' + k }, [h('span', { class: 'small muted tipword', text: 'EN', ...towerTip('entropy') }), entropy.bar, entropy.vent]);
      }
      const name = u.name || (u.side ? foe : you);
      const skills = h('div', { class: 'skills', 'data-id': 'skills-' + (tower ? k : u.side) }, chips.some(Boolean) ? chips : [h('small', { class: 'muted', text: 'Basic attacks only' })]);
      const totals = upto === null ? h('small', { class: 'muted', text: Math.round(u.damage) + ' dmg · ' + u.crits + ' crits · ' + u.evades + ' dodges' }) : null;
      const float = stage ? h('div', { class: 'dmgfloat', 'aria-hidden': 'true' }) : null;
      const el = stage
        ? h('div', { class: 'unit' + (u.side ? ' foe' : ''), 'data-id': 'unit-' + u.side }, [h('b', { class: 'uname', text: name }), hpBar, totals, h('div', { class: 'uart' }, [art(u), float])])
        : h('div', { class: 'unit' + (u.side ? ' foe' : ''), 'data-id': 'unit-' + (tower ? k : u.side) }, [
          art ? h('div', { class: 'row unithead' }, [art(u), h('b', { text: name })]) : h('b', { text: name }),
          hpBar, entropy ? entropy.row : null, totals, skills,
        ]);
      refs.push({ u: u, el: el, hpBar: hpBar, chips: chips, entropy: entropy, float: float, skills: skills });
      return el;
    }));
    const skillRows = stage ? h('div', { class: 'skillrows' }, refs.map((r) => h('div', { class: 'skillrow' + (r.u.side ? ' foe' : '') }, [
      h('span', { class: 'who', text: r.u.side ? 'Foe' : 'You' }), r.skills]))) : null;
    paintUnits(refs, unitsAt(f, upto), null, f.entropyCap);
    return { root: root, refs: refs, skillRows: skillRows };
  }
  // What floats over a unit the current action lands on (stage only): the damage, a heal, or a dodge.
  function floatOf(x) {
    if (!x || x.event) return null;
    if (x.evaded) return { text: 'Dodged', cls: 'miss' };
    if (x.kind === 'heal') return x.amount > 0 ? { text: '+' + Math.round(x.amount), cls: 'heal' } : null;
    if (x.kind === 'shield' || x.kind === 'buff' || x.kind === 'debuff' || x.kind === 'passive' || x.kind === 'taunt') return null;
    return x.amount > 0 ? { text: '-' + Math.round(x.amount), cls: x.crit ? 'crit' : 'dmg' } : null;
  }
  function paintUnits(refs, now, x, cap) {
    refs.forEach((r, k) => {
      const hp = now[k].hp, sh = now[k].sh || 0, max = r.u.maxHp;
      r.el.hidden = !now[k].here;
      if (r.float) {   // the stage: the actor lunges, the one hit recoils, and the number floats up (a new node restarts its animation)
        const fl = x && x.target === r.u.id ? floatOf(x) : null;
        r.el.classList.toggle('acting', !!x && x.actor === r.u.id && x.target !== r.u.id);
        r.el.classList.toggle('hit', !!fl && x.actor !== r.u.id && (fl.cls === 'dmg' || fl.cls === 'crit'));
        r.float.replaceChildren(...(fl ? [h('span', { class: fl.cls, text: fl.text })] : []));
      }
      r.hpBar.querySelector('.fill').style.width = Math.max(0, Math.min(100, (hp / max) * 100)) + '%';
      // the shield is a band along the bar's bottom, sized against max HP (a shield can exceed what HP is missing)
      const shEl = r.hpBar.querySelector('.shield'), shown = Math.round(sh) > 0;
      shEl.style.width = (shown ? Math.min(100, (sh / max) * 100) : 0) + '%';
      r.hpBar.classList.toggle('shielded', shown);
      r.hpBar.querySelector('span').textContent = Math.round(hp) + '/' + Math.round(max) + (shown ? '  🛡' + Math.round(sh) : '');
      r.hpBar.title = shown ? 'Shield ' + Math.round(sh) + ': absorbs damage before HP' : '';
      if (r.entropy) {
        const en = now[k].en || 0, vent = now[k].vent;
        r.entropy.bar.querySelector('.fill').style.width = Math.max(0, Math.min(100, (en / cap) * 100)) + '%';
        r.entropy.bar.querySelector('span').textContent = Math.round(en) + '/' + Math.round(cap);
        if ((r.entropy.vent.dataset.kind || '') !== (vent || '')) {   // the next vent's name opens its tooltip
          r.entropy.vent.dataset.kind = vent || '';
          r.entropy.vent.textContent = vent ? 'next: ' + tipName('vent.' + vent) : '';
          const a = vent ? towerTip('vent.' + vent) : null;
          if (a) for (const n in a) r.entropy.vent.setAttribute(n, a[n]);
          else ['data-tip', 'role', 'tabindex', 'aria-expanded'].forEach((n) => r.entropy.vent.removeAttribute(n));
        }
      }
      r.chips.forEach((c, si) => {
        if (!c) return;
        const sk = r.u.skills[si], left = now[k].cd[si] || 0;
        c.classList.toggle('ready', left === 0);
        c.classList.toggle('used', !!x && x.actor === r.u.id && x.si === si);
        setCdFill(c.querySelector('.cdfill'), sk.cooldown ? Math.min(100, (left / sk.cooldown) * 100) : 0);
        c.querySelector('.st').textContent = left ? String(left) : '';
        c.title = sk.name + ': ' + (left ? 'ready in ' + left + ' of its actions' : 'ready') + ', cooldown ' + sk.cooldown;
      });
    });
  }

  // The grey overlay snaps up at once when a skill is used and only animates as it recedes (issue #97): a rise skips the CSS
  // height transition (turned off, reflow forced, turned back on), a fall keeps it.
  function setCdFill(el, pct) {
    const prev = parseFloat(el.style.height) || 0;
    if (pct > prev) {
      el.style.transition = 'none';
      el.style.height = pct + '%';
      void el.offsetHeight;   // flush the jump before the transition comes back
      el.style.transition = '';
    } else el.style.height = pct + '%';
  }

  // How a fight is labelled: the rank fight's monster and rank opponent, or a tower floor's units by their own names.
  function rankView(f, s) {
    const you = speciesDef(s.species).name, foe = f.opponent.name;
    return { stage: true, you: you, foe: foe, title: 'Rank ' + f.opponent.letter + ' fight: ' + foe, art: rankArt(f, s),
      name: (id) => (id === 'monster' ? you : foe), ours: (id) => id === 'monster',
      line: (x) => { const l = actionLine(x, foe, you); return { cls: l.you ? 'you' : 'foe', text: l.text }; } };
  }
  // The unit panels' sprites in a rank fight: the monster's and the opponent's (BRIDGE.md, fight.opponent).
  const rankArt = (f, s) => (u) => (u.side ? oppSprite(f.opponent) : sprite(s.species, s.apex));
  function towerView(f) {
    const nameOf = {}, sideOf = {};
    f.units.forEach((u) => { nameOf[u.id] = u.name; sideOf[u.id] = u.side; });
    const name = (id) => nameOf[id] || id;
    return { you: '', foe: '', title: 'Floor ' + f.floor + ': ' + f.name, name: name, ours: (id) => sideOf[id] === 0, line: (x) => towerLine(x, name, sideOf) };
  }

  // The playback view. While ui.play runs it shows the action reached; otherwise (Skip, or the tower floor once played) the end.
  function fightPlayView(modal, f, v, extra) {
    const playing = ui.play && ui.play.f === f;
    const units = fightUnits(f, v.you, v.foe, playing ? ui.play.i : null, v.art, !!v.stage);
    const now = h('div', { class: 'now', 'data-id': 'fight-now' });
    const progress = h('span', { class: 'small muted num', 'data-id': 'fight-progress' });
    const recent = h('ul', { class: 'alog' });
    let corruption = null;
    if (f.entropyCap > 0) {
      corruption = bar(0, Math.max(1, f.corruptMax), '', 'corrupt');
      modal.append(h('h2', { text: v.title, 'data-id': 'fight-play', 'data-hash': f.hash }),
        h('div', { class: 'row between' }, [progress, h('div', { class: 'corrow', 'data-id': 'corruption' }, [h('span', { class: 'small muted tipword', text: tipName('corruption'), ...towerTip('corruption') }), corruption])]));
    } else modal.append(h('div', { class: 'row between' }, [h('h2', { text: v.title, 'data-id': 'fight-play' }), progress]));
    if (v.cls) units.root.classList.add(v.cls);   // a layout of the view's own (the Endless Tower's 4v4 grid)
    if (v.head) modal.append(v.head);             // what the view shows under the title (the Endless Tower's mutators)
    if (v.stage) modal.append(h('div', { class: 'fstage' }, [units.root, now]), units.skillRows, speedToggle(), h('h3', { text: 'Latest' }), recent);
    else modal.append(units.root, now, speedToggle(), h('h3', { text: 'Latest' }), recent);
    if (extra) modal.append(extra);
    const dom = { root: units.root, units: units, now: now, progress: progress, recent: recent, corruption: corruption, v: v };
    if (playing) { ui.play.dom = dom; updatePlay(); } else paintPlay(f, null, dom);
  }
  function updatePlay() {
    const p = ui.play;
    paintPlay(p.f, p.i, p.dom);
  }
  function paintPlay(f, i, d) {
    const n = i === null ? f.actions.length : i;
    const x = n ? f.actions[n - 1] : null;
    paintUnits(d.units.refs, unitsAt(f, i), i === null ? null : x, f.entropyCap);
    d.progress.textContent = i === null ? (f.won ? 'Won' : 'Lost') + ' · ' + f.actionCount + ' actions' : 'Action ' + i + ' of ' + f.actions.length;
    if (d.corruption) {
      const lv = corruptionAt(f, i);
      d.corruption.querySelector('.fill').style.width = Math.min(100, (lv / Math.max(1, f.corruptMax)) * 100) + '%';
      d.corruption.querySelector('span').textContent = 'lv ' + lv;
    }
    const side = x && d.v.ours(x.actor) ? 'you' : 'foe';
    const gambit = !!x && x.event === 'gambit';   // a rulebook rule firing: its own banner, not a tower warning
    const ev = x && !gambit && x.event && x.event.indexOf('rule:') !== 0 ? x.event : null;   // a tower event (rule events read like actions)
    d.now.className = 'now' + (x ? ' ' + (gambit ? 'gambit ' + side : ev ? 'event' : side) : '');
    d.now.replaceChildren(...(x ? (gambit ? [
      h('small', { text: d.v.name(x.actor) }),
      h('b', { 'data-id': 'fight-gambit', text: 'Rule ' + (Math.round(x.amount) + 1) }),
      h('span', { text: gambitText(x, d.v.name(x.actor)) }),
    ] : ev ? eventBanner(x, d.v.name) : [
      h('small', { text: d.v.name(x.actor) }),
      h('b', { text: actionName(x) }),
      h('span', { text: (x.target ? '→ ' + d.v.name(x.target) + ': ' : '') + actionResult(x) }),
    ]) : [h('b', { text: 'Fight!' })]));
    const lines = f.actions.slice(Math.max(0, n - 5), n).reverse().map(d.v.line);
    d.recent.replaceChildren(...lines.map((l) => h('li', { class: l.cls, text: l.text })));
  }

  // A tower event's banner: what happened, with the phase or vent kind opening its tooltip.
  function eventBanner(x, name) {
    const key = x.event.indexOf('phase:') === 0 ? 'phase.' + x.event.slice(6) : x.event.indexOf('vent:') === 0 ? 'vent.' + x.event.slice(5)
      : x.event === 'channel_broken' || x.event === 'channel_strike' ? 'phase.channel' : x.event === 'check_failed' ? 'phase.shield_check' : null;
    const title = x.event.indexOf('vent:') === 0 ? 'Entropy vent: ' + tipName(key) : key ? tipName(key) : x.event;
    return [h('small', { text: name(x.actor) }), h('b', { text: title, 'data-id': 'fight-event', ...(key ? towerTip(key) : null) }), h('span', { text: towerEventText(x, name(x.actor)) })];
  }

  const refundShare = () => { const p = C.consts.treeRefund; return p === 0.5 ? 'half' : Math.round(p * 100) + '%'; };
  // A tree offer (Awakening, skill book or chase unlock) as a card pick: a banner, one compact card per tree (its node path,
  // its stat boosts summed and its skills and capstone in one line each), then big slot tiles on the picked card. Every node's full
  // text stays one tap away (the path tiles' tooltips, or "All nodes").
  const offerKey = (o) => [o.source, o.stat, o.threshold].concat(o.trees, (o.rolled || []).map((r) => (r ? r.nodes.map((n) => n.id).join(',') : ''))).join('|');
  // A rolled tree's own stat (specs/15 section 4.1): most of its nodes and its capstone growth go to it.
  const rolledChip = (t) => (t.rolledStat ? h('span', { class: 'stchip st-' + t.rolledStat, 'data-id': 'rolled-stat', title: 'Rolled ' + up(t.rolledStat) + ' tree', text: up(t.rolledStat) }) : null);
  const signed = (v) => (v > 0 ? '+' : '') + String(Math.round(v * 100) / 100);
  function offerModal(modal, s) {
    const o = s.offers[0], key = offerKey(o), fresh = ui.offer.key !== key;
    if (fresh) ui.offer = { key, pick: 0, open: false };   // the banner pops once per offer, not on every re-render
    const banner = o.source === 'milestone' ? { cls: 'milestone', big: 'Awakening!' }
      : o.source === 'book' ? { cls: 'book', big: 'Skill book!' } : o.source === 'fusion' ? { cls: 'chase', big: 'Fusion!' } : { cls: 'chase', big: 'Chase unlocked!' };
    const ft = o.source === 'fusion' ? C.trees[o.trees[0]] : null;
    const sub = o.source === 'milestone'
      ? [h('span', { class: 'stchip st-' + o.stat, text: up(o.stat) }), ' reached ', h('b', { class: 'num', text: String(o.threshold) })]
      : ft ? [treeName(ft.fuseA) + ' + ' + treeName(ft.fuseB) + ' can fuse into one tree']
      : [o.source === 'book' ? 'Its tree is yours to slot' : 'A chase tree is ready'];
    if (o.trees.length > 1) sub.push(' · pick 1 of ' + o.trees.length);
    modal.append(h('div', { class: 'obanner ' + banner.cls + (fresh && !reducedMotion() ? ' anim' : '') }, [
      h('div', { class: 'big', text: banner.big, 'data-id': 'offer-title' }),
      h('div', { class: 'sub' }, sub),
      s.offers.length > 1 ? h('span', { class: 'more', text: '+' + (s.offers.length - 1) + ' more' }) : null,
    ]));
    o.trees.forEach((id, choice) => modal.append(offerCard(s, id, choice, choice === ui.offer.pick, o.rolled && o.rolled[choice])));
    modal.append(actionBar([
      s.canReroll ? h('button', { class: 'btn block gemsy', type: 'button', 'data-act': 'reroll',
        text: '🎲 Second Look: reroll, any stat (' + s.rerollsLeft + ' left)', onclick: reroll }) : null,
      h('button', { class: 'btn ghost block', type: 'button', 'data-act': 'discard',
        text: o.source === 'milestone' ? 'Skip — keep my trees' : 'Discard this offer', onclick: discard })]));
  }

  function offerCard(s, id, choice, sel, rolled) {
    const t = treeView(id, rolled);
    const card = h('div', { class: 'tcard k-' + t.kind + (sel ? ' sel' : ''), 'data-id': 'offer-' + id },
      [h('div', { class: 'row between' }, [h('b', { class: 'tname', text: t.name }), h('span', null, [rolledChip(t), ' ', h('span', { class: 'tag kind', text: t.kind })])])]);
    if (!sel) {
      card.setAttribute('role', 'button'); card.setAttribute('tabindex', '0'); card.setAttribute('data-act', 'pick-' + choice);
      const pick = () => { ui.offer.pick = choice; ui.offer.open = false; render(); };
      card.addEventListener('click', (e) => { if (!e.target.closest('[data-tip]')) pick(); });
      card.addEventListener('keydown', (e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); pick(); } });
    }
    card.append(nodePath(t));
    if (t.boosts && t.boosts.length) card.append(h('div', { class: 'chips' }, t.boosts.map((b) => h('span', { class: 'bchip st-' + b.stat }, [
      h('b', { text: up(b.stat) }),
      b.growth ? h('span', { class: 'num', text: '▲' + signed(b.growth) }) : null,
      b.flat ? h('span', { class: 'num', text: signed(b.flat) }) : null,
    ]))));
    const perks = t.nodes.filter((n) => n.last || (n.kind !== 'growth' && n.kind !== 'stat'));
    card.append(h('div', { class: 'perks' }, perks.map((n) => h('div', { class: 'perk' + (n.last ? ' cap' : '') }, [
      h('span', { class: 'pg', 'aria-hidden': 'true', text: n.last ? '★' : n.kind === 'trigger' ? '⚡' : n.kind === 'rule' ? '◆' : '✦' }),
      h('span', null, [h('b', { text: n.name, ...TIP.attrs('node:' + n.id) }), ' ', h('span', { class: 'muted', text: n.desc.replace(/^Chase trait\.\s*/, '') })]),
    ]))));
    if (!sel) return card;

    const grid = h('div', { class: 'slotgrid', style: 'grid-template-columns:repeat(' + (s.slots.length === 5 ? 5 : 4) + ',minmax(0,1fr))' });   // one row of 4 or 5
    // Warn on a slot whose tree a chase tree needs (specs/15 section 5.2): the offered chase tree itself, which would lock at once,
    // or a slotted chase tree, which would lock again.
    const needs = (chaseId, treeId) => (C.trees[chaseId].conditionTrees || []).indexOf(treeId) >= 0;
    // A fusion goes into one of its parents' slots; the other parent's slot comes free (specs/15 section 5.6).
    const parentSlots = t.kind === 'fusion' ? s.slots.map((x, i) => (x && (x.tree === t.fuseA || x.tree === t.fuseB) ? i : -1)).filter((i) => i >= 0) : null;
    if (parentSlots) {
      parentSlots.forEach((i) => {
        const other = parentSlots.find((j) => j !== i);
        grid.append(h('button', { class: 'stile full', type: 'button', 'data-act': 'take-' + choice + '-' + i,
          'aria-label': 'Fuse into slot ' + (i + 1) + ', freeing slot ' + (other + 1), onclick: () => take(choice, i) }, [
          h('span', { class: 'sn num', text: String(i + 1) }),
          h('span', { class: 'sl', text: treeName(s.slots[i].tree) }),
          h('span', { class: 'sw', text: '⚭ Fuse here, frees ' + (other + 1) }),
        ]));
      });
      card.append(h('div', { class: 'small muted', text: 'Fuse into either parent\'s slot. Both parents\' nodes and stats stay; the other slot comes free.' }), grid);
      return card;
    }
    s.slots.forEach((slot, i) => {
      const warn = !slot ? null : needs(id, slot.tree) ? 'Locks it!'
        : s.slots.some((x) => x && x !== slot && C.trees[x.tree].kind === 'chase' && needs(x.tree, slot.tree)) ? 'Chase needs it' : null;
      const label = 'Put ' + t.name + ' in slot ' + (i + 1) + (slot ? ' (replaces ' + treeName(slot.tree) + (warn ? '; ' + warn.toLowerCase().replace('!', '') : '') + ')' : ' (empty)');
      grid.append(h('button', { class: 'stile ' + (slot ? 'full' : 'empty') + (warn ? ' risky' : ''), type: 'button', 'data-act': 'take-' + choice + '-' + i, 'aria-label': label,
        onclick: () => take(choice, i) }, [
        h('span', { class: 'sn num', text: String(i + 1) }),
        h('span', { class: 'sl', text: slot ? treeName(slot.tree) : 'Empty' }),
        h('span', { class: 'sw', text: warn ? '⚠ ' + warn : slot ? (slot.bought ? '↺ ' + slot.bought + '/' + C.trees[slot.tree].nodes.length : 'Replace') : 'Slot it' }),
      ]));
    });
    card.append(h('div', { class: 'small muted', text: 'Tap a slot:' }), grid);
    if (s.slots.some((x) => x && x.bought))
      card.append(h('div', { class: 'small muted', text: 'Replacing gives back ' + refundShare() + ' of the SP spent in it. Its flat boosts are lost, and a chase tree that needs it locks again.' }));
    card.append(h('button', { class: 'linkish', type: 'button', 'data-act': 'offer-nodes', 'aria-expanded': String(ui.offer.open),
      text: (ui.offer.open ? 'Hide nodes ▴' : 'All ' + t.nodes.length + ' nodes ▾'), onclick: () => { ui.offer.open = !ui.offer.open; render(); } }));
    if (ui.offer.open) card.append(h('div', null, t.nodes.map((n) => nodeRow(n, 'locked'))));
    return card;
  }

  // A tree's nodes as a strip of tiles in buy order: stat nodes show their stat (▲ growth, + flat), skills their initials, the
  // capstone a star. Each tile opens its node's tooltip. With `bought` (a slotted tree on the Trees tab, issue #115) the bought
  // tiles are lit, the next one is outlined and the rest dimmed; without it (an offer card) every tile shows plain.
  function nodePath(t, bought) {
    const prog = typeof bought === 'number';
    return h('div', { class: 'npath' + (prog ? ' prog' : ''), 'data-id': 'npath', 'aria-label': t.nodes.length + ' nodes' + (prog ? ', ' + bought + ' bought' : '') }, t.nodes.map((n, j) => {
      const filler = n.kind === 'growth' || n.kind === 'stat';
      const cls = 'nt ' + (n.last ? 'cap' : filler ? 'fill st-' + n.stat : 'sk k-' + n.kind) + (prog ? (j < bought ? ' got' : j === bought ? ' next' : '') : '');
      const face = n.last ? '★' : filler ? up(n.stat) : skillAbbr(n.name);
      return h('span', { class: cls, ...TIP.attrs('node:' + n.id), style: !filler && !n.last ? '--hue:' + (hash(n.name) % 360) : null }, [
        h('span', { class: 'f', text: face }),
        filler ? h('span', { class: 'm', text: n.kind === 'growth' ? '▲' : '+' }) : null,
      ]);
    }));
  }

  // The run bonuses the account's talents give now (specs/18 section 3), only those that change something.
  function talentSummary(m) {
    const pts = (v) => num(Math.abs(v) * 100);
    return [
      m.drillCostMult !== 1 && 'drills cost x' + m.drillCostMult.toFixed(2),
      m.statFloorMult !== 1 && 'start stats x' + m.statFloorMult.toFixed(2),
      m.startSp && '+' + m.startSp + ' starting SP',
      m.restEnergyBonus && 'rests +' + num(m.restEnergyBonus) + ' energy',
      m.failChanceDelta && 'fail -' + pts(m.failChanceDelta) + ' pts',
      m.superChanceBonus && 'super +' + pts(m.superChanceBonus) + ' pts',
      m.shardMult !== 1 && 'shards x' + m.shardMult.toFixed(2),
      m.momentumDrills && 'a free drill after each Super',
      m.lossSp && '+' + m.lossSp + ' SP per lost fight',
      m.specialistBonus && 'top stat grows +' + pts(m.specialistBonus) + '%, others -' + pts(m.specialistBonus / 2) + '%',
      m.treeSlotBonus && '+' + m.treeSlotBonus + ' tree slot' + (m.treeSlotBonus > 1 ? 's' : ''),
      m.injuryShields && m.injuryShields + ' injur' + (m.injuryShields > 1 ? 'ies' : 'y') + ' prevented per run',
      m.lastCycleAp && '+' + m.lastCycleAp + ' AP in the last cycle',
      m.milestoneMult !== 1 && 'milestones at x' + m.milestoneMult.toFixed(2),
      m.draftRerolls && m.draftRerolls + ' Awakening reroll' + (m.draftRerolls > 1 ? 's' : '') + ' per run',
      // legendary rule changes (issue #103)
      m.injuryImmune && 'no injuries',
      m.restEnergyMult !== 1 && 'rests x' + m.restEnergyMult.toFixed(2),
      m.neverFail && 'drills never Fail',
      m.glassCannonBonus && 'drills +' + pts(m.glassCannonBonus) + '% on every stat but HP, none on HP',
      m.echoFrac && pts(m.echoFrac) + '% of each drill echoes to another stat',
    ].filter(Boolean);
  }

  // ---- home and grade
  // The home screen is split into tabs on the bottom bar (the run screen's bar): Raise (species, start),
  // Vault, Talents, Dungeon and Goals (achievements). Each tab opens at its top; the header keeps the shards in view.
  const HOME_TABS = [['raise', 'Raise'], ['vault', 'Vault'], ['talents', 'Talents'], ['dungeon', 'Dungeon'], ['goals', 'Goals']];
  function goHomeTab(id) { ui.homeTab = id; render(); }
  function renderHomeTabs(tabs) {
    tabs.replaceChildren();
    const acct = run.account, ti = run.towerInfo(run.towerDay());
    const done = run.account.achievements.filter((x) => x.earned).length;
    // A count on a tab when it says something: the Vault's fill (red when over its cap), Dungeon tickets ready, goals done.
    const badge = {
      vault: acct.vault.length ? { n: acct.vault.length, cls: acct.vaultOver > 0 ? ' bad' : '', label: acct.vault.length + ' monsters' + (acct.vaultOver > 0 ? ', over the cap' : '') } : null,
      dungeon: acct.vault.length && ti.tickets > 0 ? { n: ti.tickets, cls: ' gems', label: ti.tickets + (ti.tickets === 1 ? ' ticket' : ' tickets') + ' ready' } : null,
      goals: { n: done + '/' + C.achievements.length, cls: '', label: done + ' of ' + C.achievements.length + ' done' },
    };
    HOME_TABS.forEach(([id, label]) => {
      const b = h('button', { type: 'button', role: 'tab', 'aria-selected': String(ui.homeTab === id), 'data-act': 'hometab-' + id, text: label,
        onclick: () => goHomeTab(id) });
      const bd = badge[id];
      if (bd) { b.append(h('span', { class: 'tabsp pill num' + bd.cls, 'data-id': 'hometab-' + id + '-n', text: String(bd.n) })); b.setAttribute('aria-label', label + ', ' + bd.label); }
      tabs.append(b);
    });
  }
  function homeHeader(main, title, attrs) {
    main.append(h('div', { class: 'row between' }, [h('h2', Object.assign({ text: title }, attrs)),
      h('span', { class: 'num gems', 'data-id': 'shards', text: run.account.shards + ' shards' })]));
  }
  function renderHome(main) {
    if (!HOME_TABS.some(([id]) => id === ui.homeTab)) ui.homeTab = 'raise';
    if (ui.homeTab === 'vault') {   // the count against the cap, red when over it (specs/18 section 7), then the list (web/vault.js)
      const acct = run.account;
      homeHeader(main, 'Vault ' + acct.vault.length + '/' + acct.vaultCap, { class: acct.vaultOver > 0 ? 'bad' : '', 'data-id': 'vault-count' });
      VAULT.render(main, $('modal'), vaultHelpers());
      return;
    }
    if (ui.homeTab === 'talents') return homeTalents(main);
    if (ui.homeTab === 'dungeon') return homeDungeon(main);
    if (ui.homeTab === 'goals') return homeGoals(main);
    homeRaise(main);
  }
  function homeRaise(main) {
    const acct = run.account;
    homeHeader(main, 'Raise a monster');
    main.append(h('details', { class: 'howto small muted', 'data-id': 'howto', open: ui.howto, ontoggle: (e) => { ui.howto = e.target.open; } }, [h('summary', { text: 'How a run works' }),
      h('p', { text: 'Train over ' + C.consts.runCycles + ' cycles. Each cycle: ' + C.consts.runAp + ' actions (Drill a stat or Rest), then a rank fight. Win to rank up and spend skill points on trees. Drilling at low energy can injure the monster, which loses ' + injuryRisk() + '.' })]));
    if (!owned(ui.pick)) ui.pick = acct.owned[0] || null;
    const over = acct.vaultOver;   // the engine refuses a new run while the Vault is over its cap (specs/18 section 7)
    if (over > 0) main.append(h('div', { class: 'row between' }, [
      h('p', { class: 'small bad', 'data-id': 'vault-over', role: 'status',
        text: 'The Vault holds ' + acct.vault.length + ' of ' + acct.vaultCap + ' monsters. Dismantle ' + over + (over === 1 ? ' monster' : ' monsters') + ' in the Vault to start a run.' }),
      h('button', { class: 'btn small', type: 'button', 'data-act': 'vault-over-open', text: 'Open Vault', onclick: () => goHomeTab('vault') })]));
    speciesCarousel(main);
    speciesDetail(main);
    const bring = bookToBring();
    if (bring) {
      const cb = h('input', { type: 'checkbox', 'data-act': 'book-toggle', onchange: (e) => { ui.book = e.target.checked; } });
      cb.checked = ui.book;
      main.append(h('label', { class: 'small row' }, [cb, h('span', { text: 'Bring ' + C.books[bring].name + ' (you have ' + acct.books[bring] + ')' })]));
    }
    lockedSpecies(main);

    const grant = debugOn() ? speciesBook() || Object.keys(C.books)[0] : null;   // debug only: the Dungeon drops books (specs/21 section 7)
    if (grant) main.append(h('button', { class: 'btn small', type: 'button', 'data-act': 'grant-book', text: 'Test: add a ' + C.books[grant].name,
      onclick: () => { const r = run.acct('debugGrantBook', grant, 1); if (!r.ok) toast(r.reason); render(); } }));

    // The start button sticks to the bottom of the screen, so it is in reach from anywhere in the species list (not while it is
    // disabled: a faded button over the cards only gets in the way).
    main.append(h('button', { class: 'btn primary block' + (ui.pick && over <= 0 ? ' sticky-start' : ''), type: 'button', 'data-act': 'start', disabled: !ui.pick || over > 0,
      text: over > 0 ? 'Vault full: dismantle ' + over : ui.pick ? 'Start run' : 'Pick a species', onclick: startRun }));
  }
  // The owned species as a row of big square cards (the whole portrait, no crop, the name over its foot) that swipes sideways; the card that settles in the middle is the pick (tapping a
  // card or a dot picks it too and glides it to the middle). Locked species stay out of the row, folded under one line.
  // The row is built once and kept across renders (rebuilding it reloaded every portrait, so the cards popped in after each swipe), and
  // the card nearest the middle lights up while the row moves; the pick itself (and the re-render) waits for the row to settle.
  function pickSpecies(id) { if (id === ui.pick) return; ui.pick = id; ui.carousel.glide = true; render(); }
  function markCarousel(car, id) {   // light up one card and its dot without a re-render
    car.row.querySelectorAll('.species-card').forEach((c) => {
      const on = c.dataset.sp === id;
      c.classList.toggle('sel', on); c.setAttribute('aria-selected', String(on));
      const img = c.querySelector('.pimg');
      if (on) img.setAttribute('data-id', 'portrait'); else img.removeAttribute('data-id');
    });
    if (car.dots) car.dots.querySelectorAll('.dot').forEach((d) => d.classList.toggle('on', d.dataset.sp === id));
  }
  function nearestCard(row) {
    const mid = row.scrollLeft + row.clientWidth / 2;
    let best = null, dist = Infinity;
    row.querySelectorAll('.species-card').forEach((c) => { const d = Math.abs(c.offsetLeft + c.offsetWidth / 2 - mid); if (d < dist) { dist = d; best = c; } });
    return best;
  }
  function buildCarousel(mine) {
    const row = h('div', { class: 'carousel', 'data-id': 'species-carousel', role: 'listbox', 'aria-label': 'Species' });
    mine.forEach((sp) => {
      const art = SP.art(sp.id);   // the species' portrait (specs/26 section 8), else its sprite
      row.append(h('button', { class: 'card species-card', type: 'button', role: 'option', 'aria-selected': 'false',
        'data-act': 'species-' + sp.id, 'data-sp': sp.id, onclick: () => pickSpecies(sp.id) }, [
        h('div', { class: 'pimg' }, [art ? h('img', { src: art.portrait, alt: sp.name, draggable: 'false', decoding: 'async' }) : sprite(sp.id)]),
        h('div', { class: 'row' }, [h('b', { text: sp.name }), elementTag(sp.element)]),
      ]));
    });
    const dots = mine.length > 1 ? h('div', { class: 'dots', 'aria-hidden': 'true' }, mine.map((sp) =>
      h('button', { class: 'dot', type: 'button', tabindex: '-1', 'data-sp': sp.id, onclick: () => pickSpecies(sp.id) }))) : null;
    const car = { key: mine.map((sp) => sp.id).join(','), row, dots, raf: 0, gliding: null, info: null };   // gliding: the scrollLeft a tap's glide is heading for (the tapped card stays lit), else null
    // A swipe: each frame the card nearest the middle lights up; once the row stops moving it becomes the pick.
    const settle = () => {
      clearTimeout(ui.carousel.timer);
      if (!row.isConnected) return;
      if (car.gliding !== null) { if (Math.abs(row.scrollLeft - car.gliding) > 2) return; car.gliding = null; }   // an earlier scroll settling mid-glide
      const best = nearestCard(row);
      if (best && best.dataset.sp !== ui.pick) { ui.pick = best.dataset.sp; render(); }
    };
    row.addEventListener('scroll', () => {
      ui.carousel.x = row.scrollLeft;
      if (!car.raf) car.raf = requestAnimationFrame(() => { car.raf = 0; if (car.gliding === null) { const best = nearestCard(row); if (best) markCarousel(car, best.dataset.sp); } trackInfo(car); });
      clearTimeout(ui.carousel.timer);
      ui.carousel.timer = setTimeout(settle, 120);   // a fallback for browsers without scrollend
    }, { passive: true });
    row.addEventListener('scrollend', settle, { passive: true });
    row.addEventListener('pointerdown', () => { car.gliding = null; }, { passive: true });   // a finger takes over from a glide
    return car;
  }
  function speciesCarousel(main) {
    const mine = C.species.filter((sp) => owned(sp.id));
    if (!mine.length) return;
    clearTimeout(ui.carousel.timer);
    const key = mine.map((sp) => sp.id).join(',');
    const fresh = !ui.carousel.el || ui.carousel.el.key !== key;
    if (fresh) ui.carousel.el = buildCarousel(mine);
    const car = ui.carousel.el, row = car.row;
    markCarousel(car, ui.pick);
    main.append(row);
    if (car.dots) main.append(car.dots);
    // Back to where the row was, then the picked card to the middle: a glide after a tap, a jump on a new row. A re-render after a
    // swipe leaves the row alone (the snap is already centring it, and a jump would fight a finger still on the screen).
    if (Math.abs(row.scrollLeft - ui.carousel.x) > 1) row.scrollLeft = ui.carousel.x;
    const card = row.querySelector('.sel');
    if (card && (fresh || ui.carousel.glide)) {
      const target = card.offsetLeft - (row.clientWidth - card.offsetWidth) / 2;
      const smooth = ui.carousel.glide && !matchMedia('(prefers-reduced-motion: reduce)').matches;
      if (Math.abs(row.scrollLeft - target) > 1) { if (smooth) { car.gliding = Math.max(0, Math.min(target, row.scrollWidth - row.clientWidth)); row.scrollTo({ left: target, behavior: 'smooth' }); } else row.scrollLeft = target; }
      if (!smooth) ui.carousel.x = row.scrollLeft;
    }
    ui.carousel.glide = false;
  }
  // The picked species' numbers: base stats, its trees and its chase trees. The card rides along with the carousel: while the row
  // moves it shifts with the middle card, fading as that card leaves, and shows the next species once its card is nearer the middle.
  function speciesInfoKids(sp) {
    const mini = h('div', { class: 'mini' });
    C.stats.forEach((st) => mini.append(h('span', null, [up(st) + ' ', h('b', { text: num(sp.base[st]) })])));
    return [mini,
      h('small', { class: 'muted', text: 'Trees: ' + sp.trees.map(treeName).join(', ') }),
      h('small', { class: 'muted', 'data-id': 'chase-' + sp.id, text: 'Chase trees: ' + treeName(sp.chase[0]) + ', ' + treeName(sp.chase[1]) +
        (sp.book ? ' (needs ' + C.books[sp.book].name + ')' : '') })];
  }
  function speciesDetail(main) {
    const sp = ui.pick && speciesDef(ui.pick);
    if (!sp) return;
    const info = h('div', { class: 'card species-info', 'data-id': 'species-info', 'data-sp': sp.id }, speciesInfoKids(sp));
    main.append(info);
    if (ui.carousel.el) { ui.carousel.el.info = info; trackInfo(ui.carousel.el); }
  }
  // Moves the info card with the carousel's middle card: the same sideways offset, faded by how far that card is from the middle.
  function trackInfo(car) {
    const info = car.info, row = car.row;
    if (!info || !info.isConnected || !row.isConnected) return;
    const best = car.gliding !== null ? row.querySelector('.sel') : nearestCard(row);
    if (!best) return;
    if (info.dataset.sp !== best.dataset.sp) { info.dataset.sp = best.dataset.sp; info.replaceChildren(...speciesInfoKids(speciesDef(best.dataset.sp))); }
    if (matchMedia('(prefers-reduced-motion: reduce)').matches) return;
    const stride = best.offsetWidth + 12;   // card width plus the row's gap
    const d = best.offsetLeft + best.offsetWidth / 2 - (row.scrollLeft + row.clientWidth / 2);
    const f = Math.min(1, Math.abs(d) / (stride / 2));   // 0 centred, 1 halfway to the next card
    if (Math.abs(d) < 0.5) { info.style.transform = ''; info.style.opacity = ''; return; }
    info.style.transform = 'translateX(' + d.toFixed(1) + 'px)';
    info.style.opacity = String(1 - 0.85 * f);
  }
  // Locked species (specs/18 section 8) behind one line; open, each shows the achievement that unlocks it and the progress.
  function lockedSpecies(main) {
    const locked = C.species.filter((sp) => !owned(sp.id));
    if (!locked.length) return;
    main.append(h('button', { class: 'btn small block locked-toggle', type: 'button', 'data-act': 'locked-toggle', 'aria-expanded': String(ui.showLocked),
      text: (ui.showLocked ? '▾ ' : '▸ ') + '🔒 ' + locked.length + ' locked species', onclick: () => { ui.showLocked = !ui.showLocked; render(); } }));
    if (!ui.showLocked) return;
    locked.forEach((sp) => {
      const lock = sp.unlock ? achievementProgress(sp.unlock.achievement) : null;
      main.append(h('button', { class: 'card species locked', type: 'button', 'data-act': 'species-' + sp.id, 'data-locked': 'true', disabled: true, 'aria-disabled': 'true' }, [
        sprite(sp.id),
        h('div', null, [h('div', { class: 'row' }, [h('b', { text: sp.name }), elementTag(sp.element)]),
          h('small', { class: 'gold', 'data-id': 'unlock-' + sp.id,
            text: sp.unlock ? 'Unlock: ' + sp.unlock.name + ' — ' + sp.unlock.text + (lock ? ' (' + num(lock.current) + '/' + num(lock.target) + ')' : '') : 'Not available yet' })]),
      ]));
    });
  }
  function homeTalents(main) {
    const acct = run.account;
    homeHeader(main, 'Talents');
    const now = talentSummary(acct.mods);
    main.append(h('p', { class: 'small muted', 'data-id': 'talent-now', text: 'Talent ranks are sold by the vendor after each run. They apply from the next run on. Untick a talent to leave it out of the next run (free); ' +
      C.consts.legendarySlots + ' legendary talent' + (C.consts.legendarySlots === 1 ? '' : 's') + ' can be on per run. Next run: ' + (now.length ? now.join(', ') : 'no talents yet') + '.' }));
    const tl = h('div', { class: 'card' });
    acct.talents.forEach((t) => {
      const def = C.talents.find((x) => x.id === t.id);
      const on = t.rank > 0 && acct.talentsOff.indexOf(t.id) < 0;
      // The loadout toggle (issue #106): only owned talents can be switched; the engine checks the legendary slots.
      const cb = t.rank > 0 ? h('input', { type: 'checkbox', 'data-act': 'talent-toggle-' + t.id, 'aria-label': 'Use ' + def.name + ' in the next run', onchange: (e) => toggleTalent(t.id, e.target.checked) }) : null;
      if (cb) cb.checked = on;
      tl.append(h('div', { class: 'row between', 'data-id': 'talent-' + t.id, title: def.desc }, [
        h('label', { class: 'row' + (t.rank > 0 && !on ? ' muted' : '') }, [cb, h('span', { text: def.name }), def.legendary ? h('span', { class: 'tag gold', text: 'Legendary' }) : null]),
        h('span', { class: 'tag num', text: t.rank + '/' + def.maxRank })]));
    });
    main.append(tl);
  }
  function homeGoals(main) {
    homeHeader(main, 'Goals');
    main.append(h('p', { class: 'small muted', text: 'Achievements. Some unlock a species.' }));
    const al = h('div', { class: 'card' });
    C.achievements.forEach((d) => {
      const p = achievementProgress(d.id);
      const sp = d.species ? speciesDef(d.species) : null;
      al.append(h('div', { class: 'ach' + (p.earned ? ' got' : ''), 'data-id': 'ach-' + d.id }, [
        h('span', { class: 'chk', 'aria-hidden': 'true', text: p.earned ? '✓' : '' }),
        h('div', null, [h('b', { text: d.name }), h('div', { class: 'small muted', text: d.text + (sp ? ' · unlocks ' + sp.name : '') })]),
        h('span', { class: 'small num ' + (p.earned ? 'ok' : 'muted'), text: p.earned ? 'Done' : num(p.current) + '/' + num(p.target) }),
      ]));
    });
    main.append(al);
  }
  function homeDungeon(main) {
    const ti = run.towerInfo(run.towerDay());
    homeHeader(main, 'Dungeon');
    main.append(h('div', { class: 'card' }, [
      h('p', { class: 'small muted', text: 'Send a team of up to ' + ti.teamMax + ' Vault monsters through today\'s ' + ti.floors.length + ' boss floors. Each entry costs a ticket; every floor cleared pays shards and may drop a Skill Book. ' + ticketHelp(ti) }),
      h('div', { class: 'row between' }, [
        h('span', { class: 'small num', 'data-id': 'tower-tickets', text: ticketStatus(ti) + ' · best today ' + ti.clearedToday + ' floors' }),
        h('button', { class: 'btn gemsy small', type: 'button', 'data-act': 'tower', text: 'Enter',
          onclick: () => { ui.view = 'tower'; ui.towerResult = null; ui.towerFight = null; ui.towerTeam = ui.towerTeam.filter((i) => i < run.account.vault.length); render(); $('main').scrollTop = 0; } }),
      ]),
    ]));
    // The Endless Tower (specs/28): free, unlimited, a new lineup every week.
    const ei = run.endlessInfo(run.endlessSeason());
    main.append(h('h3', { text: 'Endless Tower' }), h('div', { class: 'card', 'data-id': 'endless-card' }, [
      h('p', { class: 'small muted', text: 'A free weekly climb with no top floor: exactly ' + ei.teamSize + ' Vault monsters fight ' + ei.teamSize + 'v' + ei.teamSize +
        ' squads that keep getting stronger, and a new mutator joins every ' + ei.band + ' floors. Climb as often as you like; floors new this season pay shards. Each monster follows its own rulebook.' }),
      h('div', { class: 'row between' }, [
        h('span', { class: 'small num', 'data-id': 'endless-bests', text: 'Best this season ' + ei.clearedSeason + ' · best ever ' + ei.clearedEver }),
        h('button', { class: 'btn gemsy small', type: 'button', 'data-act': 'endless', text: 'Enter', onclick: openEndless }),
      ]),
    ]));
  }

  // ---- Dungeon (specs/21 section 8): today's lineup, a team of Vault monsters, one attempt at a time. The engine runs the
  // climb and pays the rewards (run.runTower); this screen only lays out what it returns. A floor's fight plays back in the rank
  // fight's view (issue #98): the last floor right after the climb (unless the speed is Skip), any floor fought from its Watch
  // button (run.towerFloor replays it, read only).
  // "3 tickets · 2/4 pieces": tickets ready (held plus today's free one, from towerInfo) and pieces toward the next.
  function ticketStatus(ti) {
    return ti.tickets + (ti.tickets === 1 ? ' ticket' : ' tickets') + ' · ' + ti.ticketPieces + '/' + ti.piecesPerTicket + ' pieces';
  }
  function ticketHelp(ti) {
    return ti.ticketsPerDay + (ti.ticketsPerDay === 1 ? ' free ticket' : ' free tickets') + ' a day (saved up to ' + ti.ticketCap + '); finished runs earn ticket pieces by grade, ' + ti.piecesPerTicket + ' pieces make a ticket.';
  }

  function renderTower(main, modal) {
    const acct = run.account, day = run.towerDay(), ti = run.towerInfo(day);
    main.append(screenHead('Dungeon', 'tower-back', () => { stopPlayback(); ui.view = 'home'; ui.towerResult = null; ui.towerFight = null; render(); },
      h('span', { class: 'num gems', 'data-id': 'shards', text: acct.shards + ' shards' })),
      h('p', { class: 'small muted', 'data-id': 'tower-status', text: ticketStatus(ti) + ' · best today ' + ti.clearedToday +
        ' of ' + ti.floors.length + ' floors · best ever ' + ti.clearedEver }));
    if (ui.towerResult) towerResult(main, ui.towerResult);

    main.append(h('h3', { text: 'Team' }));
    if (!acct.vault.length) main.append(h('p', { class: 'muted', text: 'Finish a run first: the team comes from your Vault.' }));
    else towerTeamPicker(main, ti.teamMax);
    const climb = h('button', { class: 'btn primary block', type: 'button', 'data-act': 'tower-climb', disabled: !ui.towerTeam.length || ti.tickets <= 0,
      text: ti.tickets <= 0 ? 'No tickets: finish a run for pieces, or come back tomorrow' : ui.towerTeam.length ? 'Enter with ' + ui.towerTeam.length + (ui.towerTeam.length === 1 ? ' monster' : ' monsters') : 'Pick up to ' + ti.teamMax + ' monsters',
      onclick: () => {
        const r = run.runTower(day, ui.towerTeam.slice());
        if (!r.ok) { toast(r.reason); render(); return; }
        r.day = day; r.team = ui.towerTeam.slice();   // what towerFloor needs to replay this attempt's other floors
        ui.towerResult = r;
        stopPlayback();
        ui.towerFight = ui.speed === 'skip' ? null : r.final;   // the deciding floor plays at once, unless the speed is Skip
        if (ui.towerFight) startPlayback(r.final);
        render();
        $('main').scrollTop = 0;
      } });

    main.append(h('h3', { text: "Today's floors" }));
    const fl = h('div', { class: 'card' });
    ti.floors.forEach((f) => fl.append(h('div', { class: 'row between small', 'data-id': 'tower-floor-' + f.floor }, [
      h('span', { class: 'row' }, [h('span', { class: 'num muted', text: 'F' + f.floor }), h('b', { class: f.marquee ? 'gold' : '', text: f.name }), elementTag(f.element)]),
      h('span', { class: 'muted' }, [
        ...f.phases.flatMap((p, k) => [k ? ' → ' : null, h('span', { class: 'tipword', text: p.name, ...towerTip('phase.' + p.kind) })]).filter(Boolean),
        (f.phases.length ? ' · ' : '') + 'lv ' + f.level,
      ]),
    ])));
    main.append(fl);
    main.append(actionBar(climb));

    if (ui.towerFight) { modal.hidden = false; towerPlayModal(modal, ui.towerFight); }
  }

  // The team (issue #98): the slots in order, each movable up or down (slot order breaks ATB ties and sets default targets,
  // specs/21 section 2) or removable, then the Vault with its search, sort and filters (web/vault.js) to pick from.
  function towerTeamPicker(main, teamMax) { teamPicker(main, ui.towerTeam, teamMax, 'tower', null); }
  // The picker for any team: `team` (Vault indices, changed in place), `slotsN` slots, `pre` the prefix of every data-act and
  // data-id ('tower' for the Dungeon, 'endless' for the Endless Tower). Optional:
  // extra(k, i, e) -> { note, button }: a line under the monster's name and a button after the slot's arrows (the Endless rule
  // count and Rules). fold: when the team is full, the Vault list folds behind one line (open while ui.endlessPickOpen).
  function teamPicker(main, team, slotsN, pre, extra, fold) {
    const acct = run.account;
    const name = (i) => { const e = acct.vault[i]; return e.name || (speciesDef(e.species) || { name: e.species }).name; };   // the apex form's name when it has one, as in the Vault
    const move = (k, d) => { const t = team[k]; team[k] = team[k + d]; team[k + d] = t; render(); };
    const slots = h('div', { class: 'card tslots', 'data-id': pre + '-slots' });
    for (let k = 0; k < slotsN; k++) {
      const i = team[k], e = i === undefined ? null : acct.vault[i];
      const more = e && extra ? extra(k, i, e) : null;
      const who = e ? [h('span', { class: 'tag num', text: String(k + 1) }), h('b', { text: name(i) }), h('span', { class: 'tag', text: up(e.grade) })] : null;
      const kids = e ? [
        more ? h('span', { class: 'tslot-who' }, [h('span', { class: 'row', style: 'min-width:0' }, who), more.note]) : h('span', { class: 'row', style: 'min-width:0' }, who),
        h('span', { class: 'row', style: 'gap:4px;flex-shrink:0' }, [
          h('button', { class: 'btn small', type: 'button', 'data-act': pre + '-slot-up-' + k, 'aria-label': 'Move ' + name(i) + ' up', text: '↑', disabled: k === 0, onclick: () => move(k, -1) }),
          h('button', { class: 'btn small', type: 'button', 'data-act': pre + '-slot-down-' + k, 'aria-label': 'Move ' + name(i) + ' down', text: '↓', disabled: k === team.length - 1, onclick: () => move(k, 1) }),
          h('button', { class: 'btn small', type: 'button', 'data-act': pre + '-slot-remove-' + k, 'aria-label': 'Take ' + name(i) + ' off the team', text: '✕', onclick: () => { team.splice(k, 1); render(); } }),
          more ? more.button : null,
        ]),
      ] : [h('span', { class: 'small muted', text: 'Slot ' + (k + 1) + ': empty' })];
      slots.append(h('div', { class: 'row between tslot' + (e ? '' : ' empty'), 'data-id': pre + '-slot-' + k }, kids));
    }
    const full = team.length >= slotsN;
    main.append(slots, h('p', { class: 'small muted', text: 'Slot 1 acts first on a tie and is the default target.' + (fold && full ? '' : ' Pick from the Vault:') }));
    const list = h('div', { class: 'card', 'data-id': pre + '-vault' });
    // A full team folds the Vault list away (issue #130): it is only needed to swap a monster.
    const into = fold && full ? h('details', { 'data-id': pre + '-swap', open: ui.endlessPickOpen, ontoggle: (ev) => { ui.endlessPickOpen = ev.target.open; } },
      [h('summary', { class: 'small linkish', text: 'Swap a monster from the Vault (' + acct.vault.length + ')' })]) : null;
    if (into) main.append(into);
    (into || main).append(VAULT.browse(vaultHelpers(), (rows, size) => {
      const kids = [];
      if (rows.length !== size) kids.push(h('div', { class: 'row between small muted', 'data-id': pre + '-showing' }, [
        h('span', { text: 'Showing ' + rows.length + ' of ' + size }),
        h('button', { class: 'btn small', type: 'button', 'data-act': pre + '-clear', text: 'Clear filters', onclick: VAULT.clear }),
      ]));
      if (!rows.length) kids.push(h('p', { class: 'small muted', text: 'No monster in the Vault matches.' }));
      rows.forEach(([e, i]) => {
        const slot = team.indexOf(i);
        const cb = h('input', { type: 'checkbox', 'data-act': pre + '-pick-' + i, disabled: slot < 0 && team.length >= slotsN,
          onchange: (ev) => { if (ev.target.checked) team.push(i); else team.splice(team.indexOf(i), 1); render(); } });
        cb.checked = slot >= 0;
        kids.push(h('label', { class: 'row between small', 'data-id': pre + '-entry', 'data-index': String(i) }, [
          h('span', { class: 'row', style: 'min-width:0;flex-wrap:wrap' }, [cb, sprite(e.species, e.apex, 'small'), h('b', { text: name(i) }), h('span', { class: 'tag', text: up(e.grade) }),
            e.gauntlet ? h('span', { class: 'muted', text: e.gauntlet.label }) : null]),
          h('span', { class: 'row', style: 'gap:6px;flex-shrink:0' }, [h('span', { class: 'muted num', style: 'white-space:nowrap', text: 'Σ ' + num(VAULT.total(e)) }),
            slot >= 0 ? h('span', { class: 'tag num', text: 'Slot ' + (slot + 1) }) : null]),
        ]));
      });
      list.replaceChildren(...kids);
    }), list);
  }

  function towerResult(main, r) {
    const last = r.floors[r.floors.length - 1];
    const card = h('div', { class: 'card alert', 'data-id': 'tower-result' }, [towerLoot(r, last)]);
    const rows = h('div', { class: 'small floors-fought' });
    r.floors.forEach((f) => rows.append(h('div', { class: 'row between', 'data-id': 'tower-fought-' + f.floor, 'data-hash': f.hash }, [
      h('span', null, [
        h('span', { class: f.won ? 'ok' : 'bad', text: (f.won ? '✓ ' : '✗ ') + 'F' + f.floor + ' ' + f.name }),
        h('span', { class: 'muted', text: [f.phases.join(' → '), f.vents ? f.vents + ' vents' : '', f.corruption ? 'corruption ' + f.corruption : ''].filter(Boolean).map((t) => ' · ' + t).join('') }),
      ]),
      h('button', { class: 'btn small', type: 'button', 'data-act': 'tower-watch-' + f.floor, text: 'Watch', onclick: () => watchFloor(f.floor) }),
    ])));
    card.append(rows);
    // The last floor's fight, as a log: ordinary actions plus the tower's events (phases, entropy vents, channels).
    const f = r.final, v = towerView(f);
    const list = h('ul', { class: 'alog', 'data-id': 'tower-log' });
    f.actions.forEach((x) => { const l = v.line(x); list.append(h('li', { class: l.cls, text: l.text })); });
    card.append(h('details', null, [h('summary', { class: 'small', text: 'Floor ' + f.floor + ' fight log (' + f.actionCount + ' actions' + (f.actionCount > f.actions.length ? ', first ' + f.actions.length + ' shown' : '') + ')' }), list]));
    main.append(card);
  }

  // The attempt's loot (specs/21 section 8): a banner (gold rays on a full clear) slams in once per attempt, when the player first
  // sees it (after the deciding floor's playback closes, or at once on Skip). Under it each drop is a face-down card glowing its
  // rarity colour (blue shards, purple Skill Books, gold first clears) that the player flips with a tap, or all at once with Reveal
  // all; a book wobbles first and bursts. Which cards are face up is kept on the result, so a re-render keeps them.
  function towerLoot(r, last) {
    const reveal = !r.lootSeen && !ui.towerFight;
    if (reveal) r.lootSeen = true;
    const n = r.floorsCleared;
    const big = r.cleared ? 'Full clear!' : n ? n + (n === 1 ? ' floor cleared' : ' floors cleared') : 'Defeated';
    const sub = r.cleared ? 'All ' + n + ' floors beaten' : 'Fell on floor ' + r.floorReached + ' to ' + last.name;
    const items = towerLootItems(r);
    if (!r.flipped) r.flipped = items.map(() => false);
    let body;
    if (!items.length) {
      body = h('div', { class: 'lempty' }, [h('b', { 'data-id': 'tower-shards', text: 'No floors cleared: nothing to pay' })]);
    } else {
      const cards = items.map((it, k) => {
        const up = r.flipped[k];
        return h('button', { class: 'lcard t-' + it.kind + (up ? ' up' : ''), type: 'button', 'data-act': 'tower-card-' + k,
          'aria-label': up ? null : 'Flip a ' + (it.kind === 'book' ? 'purple' : it.kind === 'first' ? 'gold' : 'blue') + ' loot card',
          onclick: () => flipLoot(r, k) }, [
          h('span', { class: 'lin' }, [
            h('span', { class: 'lface lback', 'aria-hidden': 'true', text: '?' }),
            h('span', { class: 'lface lfront' }, it.front),
          ]),
          h('span', { class: 'lring', 'aria-hidden': 'true' }),
        ]);
      });
      const left = r.flipped.filter((x) => !x).length;
      let i = 0;
      const at = (k) => setTimeout(() => flipLoot(r, k), reducedMotion() ? 0 : 420 * i++);   // Reveal all: one card after another
      body = h('div', { class: 'lcards-wrap' }, [
        h('div', { class: 'lcards', 'data-id': 'tower-cards' }, cards),
        h('button', { class: 'btn small gemsy', type: 'button', 'data-act': 'tower-reveal', hidden: !left, text: 'Reveal all',
          onclick: (e) => { e.currentTarget.disabled = true; r.flipped.forEach((up, k) => { if (!up) at(k); }); } }),
      ]);
    }
    return h('div', { class: 'loot ' + (r.cleared ? 'clear' : n ? 'some' : 'none') + (reveal ? ' anim' : ''), 'data-id': 'tower-loot' }, [
      h('div', { class: 'lhead' }, [h('div', { class: 'big', text: big }), h('div', { class: 'sub', text: sub })]),
      body,
    ]);
  }

  // The attempt's drops in card order: the shards, each Skill Book, then the first clears as one card.
  function towerLootItems(r) {
    const art = (cls) => h('span', { class: cls, 'aria-hidden': 'true' });
    const items = [];
    if (r.shards) items.push({ kind: 'shards', front: [art('lgem'), h('b', { class: 'num', 'data-id': 'tower-shards' }, ['+' + r.shards + ' ', h('small', { text: 'Training Shards' })])] });
    r.books.forEach((b) => items.push({ kind: 'book', front: [art('lbook'), h('b', { 'data-id': 'tower-book', text: b.name }), h('small', { text: 'Skill Book · floor ' + b.floor })] }));
    const fc = r.firstClears, inRow = fc.every((f, k) => !k || f === fc[k - 1] + 1);   // a range only when they run in a row
    if (fc.length) items.push({ kind: 'first', front: [art('lstar'), h('b', { 'data-id': 'tower-first',
      text: fc.length === 1 ? 'Floor ' + fc[0] : 'Floors ' + (inRow ? fc[0] + '\u2060–\u2060' + fc[fc.length - 1] : fc.join(', ')) }),
      h('small', { text: fc.length === 1 ? 'First clear' : 'First clears' })] });
    return items;
  }

  // Flips loot card k of attempt r face up, on whatever copy of the card is on screen now (a render may have replaced it).
  function flipLoot(r, k) {
    if (r.flipped[k] || ui.towerResult !== r) return;
    const el = document.querySelector('[data-act="tower-card-' + k + '"]');
    if (!el || el.dataset.busy) return;
    const done = () => {
      r.flipped[k] = true;
      const cur = document.querySelector('[data-act="tower-card-' + k + '"]');
      if (cur) { cur.classList.remove('wobble'); cur.classList.add('up'); cur.removeAttribute('aria-label'); if (cur.classList.contains('t-book')) cur.classList.add('burst'); }
      if (r.flipped.every(Boolean)) { const all = document.querySelector('[data-act="tower-reveal"]'); if (all) all.hidden = true; }
    };
    if (el.classList.contains('t-book') && !reducedMotion()) { el.dataset.busy = '1'; el.classList.add('wobble'); setTimeout(done, 500); }
    else done();
  }

  // Plays one floor of the shown attempt: the last floor is in the attempt's result; the engine replays any other (read only).
  function watchFloor(floor) {
    const r = ui.towerResult;
    if (!r) return;
    const f = floor === r.final.floor ? r.final : run.towerFloor(r.day, r.team, floor);
    if (f.ok === false) { toast(f.reason); return; }
    ui.towerFight = f;
    startPlayback(f, true);
    render();
  }

  // The tower floor playback modal: the rank fight's view, plus watch again, the floors either side and close.
  function towerPlayModal(modal, f) {
    const r = ui.towerResult, at = r ? r.floors.findIndex((x) => x.floor === f.floor) : -1;
    const prev = at > 0 ? r.floors[at - 1] : null, next = r && at >= 0 && at < r.floors.length - 1 ? r.floors[at + 1] : null;
    const close = () => { stopPlayback(); ui.towerFight = null; render(); };
    fightPlayView(modal, f, towerView(f), h('div', { class: 'row', style: 'flex-wrap:wrap' }, [
      ui.play ? null : h('button', { class: 'btn small', type: 'button', 'data-act': 'tower-replay', text: 'Watch again', onclick: () => watchFloor(f.floor) }),
      prev ? h('button', { class: 'btn small', type: 'button', 'data-act': 'tower-prev', text: '← Floor ' + prev.floor, onclick: () => watchFloor(prev.floor) }) : null,
      next ? h('button', { class: 'btn small', type: 'button', 'data-act': 'tower-next', text: 'Floor ' + next.floor + ' →', onclick: () => watchFloor(next.floor) }) : null,
    ]));
    modal.append(actionBar(h('button', { class: 'btn primary block', type: 'button', 'data-act': 'tower-play-close', text: 'Close', onclick: close })));
  }

  // One line of a tower fight's log: an ordinary action, or a tower event (rule events read like actions).
  function towerLine(x, name, sideOf) {
    if (x.event === 'gambit') return { cls: 'gambit', text: gambitText(x, name(x.actor)) };
    if (x.event && x.event.indexOf('rule:') !== 0) return { cls: 'gold', text: towerEventText(x, name(x.actor)) };
    return { cls: sideOf[x.actor] === 0 ? 'you' : 'foe', text: name(x.actor) + ' · ' + actionName(x) + ' → ' + (x.target ? name(x.target) : 'all') + ': ' + actionResult(x) };
  }

  // A rulebook rule firing (specs/28 section 4.3): the gambit event comes just before the action the rule chose; amount is the
  // rule's index from 0.
  const gambitText = (x, who) => who + ' follows rule ' + (Math.round(x.amount) + 1);

  function towerEventText(x, who) {
    const n = Math.round(x.amount);
    if (x.event.indexOf('phase:') === 0) return '⚠ ' + who + ' enters ' + tipName('phase.' + x.event.slice(6));
    if (x.event.indexOf('vent:') === 0) {
      const k = x.event.slice(5);
      return '⚡ ' + who + "'s entropy vents: " + (k === 'recoil' ? 'recoil, ' + n + ' dmg to itself' : k === 'stun' ? 'stunned' : k === 'enrage' ? 'its foes are enraged' : 'converted into a shield of ' + n);
    }
    if (x.event === 'check_failed') return '⚠ ' + who + "'s shield held: it heals " + n + ' and hits harder';
    if (x.event === 'channel_broken') return '✓ ' + who + "'s channel is interrupted";
    if (x.event === 'channel_strike') return '⚠ ' + who + "'s channel strikes the team for " + n;
    return who + ': ' + x.event;
  }

  // ---- Endless Tower (specs/28 sections 2, 3, 5 and 6): this week's season, its mutators and the next floors, a team of exactly
  // teamSize Vault monsters (each fights by its own rulebook) and free, unlimited climbs. The engine runs the climb and pays the
  // floors new this season (run.runEndless); this screen only lays out what it returns. The deciding floor plays back at once
  // (unless the speed is Skip), any floor fought from its Watch button (run.endlessFloor replays it, read only).
  function openEndless() {
    stopPlayback();
    ui.view = 'endless'; ui.endlessResult = null; ui.endlessFight = null; ui.nameEdit = null; ui.endlessTab = 'team'; ui.endlessPickOpen = false;
    ui.endlessTeam = ui.endlessTeam.filter((i) => i < run.account.vault.length);
    render();
    $('main').scrollTop = 0;
  }
  const plural = (n, one, many) => n + ' ' + (n === 1 ? one : many);
  const CHECK_HINT = { aoe: 'many weak foes', focus: 'a tough core', open: 'open' };   // what a squad shape asks of the team (specs/28 section 3.1)
  // A list of floors in words: "floor 4", "floors 3–7" when they run in a row, else "floors 2, 5, 9".
  function floorsText(fs) {
    const inRow = fs.every((f, k) => !k || f === fs[k - 1] + 1);
    return fs.length === 1 ? 'floor ' + fs[0] : 'floors ' + (inRow ? fs[0] + '⁠–⁠' + fs[fs.length - 1] : fs.join(', '));
  }
  // Time left in season s by this device's clock: it ends at the next Monday 00:00 UTC, day 7(s + 1) - 3 (run.endlessSeason).
  function seasonLeft(season) {
    const ms = ((season + 1) * 7 - 3) * 86400000 - Date.now();
    if (ms <= 0) return 'ending now';
    const m = Math.floor(ms / 60000), d = Math.floor(m / 1440), hh = Math.floor((m % 1440) / 60);
    return (d ? d + 'd ' + hh + 'h' : hh + 'h ' + (m % 60) + 'm') + ' left';
  }
  // The rules in a Vault entry's rulebook text (BRIDGE.md: entry.rulebook, rules split by "|"; empty = the default AI).
  const ruleCount = (e) => (e && e.rulebook ? e.rulebook.split('|').filter(Boolean).length : 0);
  // Each team slot's rulebook now, to tell whether the shown climb was fought with the same ones.
  const teamBooks = (team) => team.map((i) => (run.account.vault[i] || {}).rulebook || '').join('\n');

  function renderEndless(main, modal) {
    const acct = run.account, season = run.endlessSeason(), ei = run.endlessInfo(season), r = ui.endlessResult;
    const size = ei.teamSize, team = ui.endlessTeam, next = Math.min(ei.maxFloors, ei.clearedSeason + 1);
    main.append(screenHead('Endless Tower', 'endless-back', () => { stopPlayback(); ui.view = 'home'; ui.endlessResult = null; ui.endlessFight = null; render(); },
      h('span', { class: 'num gems', 'data-id': 'shards', text: acct.shards + ' shards' })),
      h('p', { class: 'small muted', 'data-id': 'endless-status', 'data-season': String(season), text: 'Season ' + season + ' · ' + seasonLeft(season) +
        ' · best this season ' + ei.clearedSeason + ' · best ever ' + ei.clearedEver + ' · ' + plural(ei.climbs, 'climb', 'climbs') + ' this season' }));
    if (r) endlessResult(main, r, season);

    // Three sections, one phone screen each (issue #130): the team, the floors and mutators ahead, the season board. Climb stays
    // in the action bar on all three.
    const TABS = [['team', 'Team'], ['floors', 'Floors'], ['board', 'Board']];
    if (!TABS.some(([id]) => id === ui.endlessTab)) ui.endlessTab = 'team';
    const goTab = (id) => { ui.endlessTab = id; render(); $('main').scrollTop = 0; };
    main.append(h('div', { class: 'seg', role: 'tablist', 'data-id': 'endless-tabs' }, TABS.map(([id, label]) => h('button', { type: 'button', role: 'tab',
      'aria-selected': String(ui.endlessTab === id), 'data-act': 'endless-tab-' + id, text: label, onclick: () => goTab(id) }))));

    const ready = team.length === size;
    const climb = h('button', { class: 'btn primary block', type: 'button', 'data-act': 'endless-climb', disabled: !ready,
      text: ready ? 'Climb with this team' : 'Pick ' + (team.length ? plural(size - team.length, 'more monster', 'more monsters') : size + ' monsters'),
      onclick: climbEndless });

    if (ui.endlessTab === 'team') {
      // The next floor in one line, so the team is picked knowing what it faces; the rest is on the Floors tab.
      const nf = ei.floors[0];
      if (nf) main.append(h('div', { class: 'card enext row between small', 'data-id': 'endless-next' }, [
        h('span', { class: 'row', style: 'min-width:0;flex-wrap:wrap' }, [h('span', { class: 'muted', text: 'Next' }), h('b', { class: nf.elite ? 'gold' : '', text: 'F' + nf.floor + ' ' + nf.shapeName }),
          h('span', { class: 'tag hint', text: CHECK_HINT[nf.check] || nf.check }), h('span', { class: 'gems num', text: '+' + nf.shards })]),
        h('button', { class: 'btn small', type: 'button', 'data-act': 'endless-see-floors', text: 'Floors', onclick: () => goTab('floors') }),
      ]));
      if (acct.vault.length < size) main.append(h('p', { class: 'small muted', 'data-id': 'endless-need', text: acct.vault.length
        ? 'The climb takes exactly ' + size + ' monsters and your Vault holds ' + acct.vault.length + ': finish ' + plural(size - acct.vault.length, 'more run', 'more runs') + ' first.'
        : 'Finish a run first: the team of ' + size + ' comes from your Vault.' }));
      if (acct.vault.length) teamPicker(main, team, size, 'endless', (k, i, e) => {
        const n = ruleCount(e);
        return {
          note: h('span', { class: 'small ' + (n ? '' : 'muted'), 'data-id': 'endless-rules-n-' + k, text: n ? plural(n, 'rule', 'rules') : 'No rules' }),
          button: h('button', { class: 'btn small', type: 'button', 'data-act': 'endless-rules-' + k, text: 'Rules', onclick: () => openRulebook(i) }),
        };
      }, true);
    } else if (ui.endlessTab === 'floors') {
      // The next floors after the season's best (specs/28 section 3.1): shape and what it asks, the squad, the shards a first clear pays.
      main.append(h('h3', { text: 'Next floors' }));
      const fl = h('div', { class: 'card efloors', 'data-id': 'endless-floors' });
      if (!ei.floors.length) fl.append(h('p', { class: 'small muted', text: 'Every floor is cleared this season: a new lineup comes on Monday.' }));
      const floorRow = (f) => h('div', { class: 'efloor' + (f.elite ? ' elite' : ''), 'data-id': 'endless-floor-' + f.floor }, [
        h('div', { class: 'row between' }, [
          h('span', { class: 'row', style: 'min-width:0;flex-wrap:wrap' }, [
            h('span', { class: 'num muted', text: 'F' + f.floor }), h('b', { class: f.elite ? 'gold' : '', text: f.shapeName }),
            f.elite ? h('span', { class: 'tag elite', text: 'Elite' }) : null,
            h('span', { class: 'tag hint', 'data-id': 'endless-check', text: CHECK_HINT[f.check] || f.check }),
          ]),
          h('span', { class: 'small num gems', style: 'white-space:nowrap', text: '+' + f.shards + ' shards' }),
        ]),
        h('div', { class: 'efoes' }, f.enemies.map((x) => h('span', { class: 'efoe', 'data-id': 'endless-foe' }, [h('b', { text: x.name }), h('span', { class: 'muted', text: x.role }), elementTag(x.element)]))),
        h('div', { class: 'small muted num', text: 'lv ' + f.level + (f.mutators ? ' · ' + plural(f.mutators, 'mutator', 'mutators') : ' · no mutators') }),
      ]);
      ei.floors.slice(0, 3).forEach((f) => fl.append(floorRow(f)));
      if (ei.floors.length > 3) fl.append(h('details', { 'data-id': 'endless-more-floors' }, [
        h('summary', { class: 'small', text: plural(ei.floors.length - 3, 'more floor', 'more floors') + ' (to floor ' + ei.floors[ei.floors.length - 1].floor + ')' }),
        ...ei.floors.slice(3).map(floorRow),
      ]));
      main.append(fl);

      // The season's mutators so far (specs/28 section 3.2): fixed and shown in advance; those in force on the next floor are marked.
      main.append(h('h3', { text: 'Mutators' }));
      const mc = h('div', { class: 'card emuts', 'data-id': 'endless-mutators' }, [h('p', { class: 'small muted', text: 'Rule changes for both sides. One joins every ' + ei.band + ' floors and they stack.' })]);
      ei.mutators.forEach((m, k) => {
        const on = m.fromFloor <= next;
        mc.append(h('div', { class: 'emut' + (on ? ' on' : ''), 'data-id': 'endless-mutator-' + k, 'data-on': on ? '1' : null }, [
          h('div', { class: 'row between' }, [h('b', { text: m.name }), h('span', { class: 'tag num', text: (on ? 'In force · ' : '') + 'from floor ' + m.fromFloor })]),
          h('div', { class: 'small muted', text: m.desc }),
        ]));
      });
      main.append(mc);
    } else endlessBoard(main, season);
    main.append(actionBar(climb));

    if (ui.endlessFight) { modal.hidden = false; endlessPlayModal(modal, ui.endlessFight); }
  }

  // The season board (specs/28 section 6): the best floors cleared this season, ties to whoever got there first, the player's
  // own row marked (and shown under the top when it is below it), and the display name the board shows, edited in place. The
  // engine ranks, names and checks names; this only lays it out. While the page has no server the board is this device's only.
  function endlessBoard(main, season) {
    const b = run.endlessBoard(season);
    main.append(h('h3', { text: 'Season board' }));
    const card = h('div', { class: 'card eboard', 'data-id': 'endless-board' });
    if (b.local) card.append(h('p', { class: 'small muted', 'data-id': 'endless-board-local', text: 'Only your own results show here until online accounts are live.' }));
    const row = (r) => h('li', { class: 'brow' + (r.you ? ' you' : ''), 'data-id': 'endless-board-row-' + r.rank, 'data-you': r.you ? '1' : null }, [
      h('span', { class: 'num muted', text: '#' + r.rank }),
      h('b', { class: 'bname', text: r.name + (r.you ? ' (you)' : '') }),
      h('span', { class: 'num', text: plural(r.floor, 'floor', 'floors') }),
    ]);
    if (!b.top.length) card.append(h('p', { class: 'small', 'data-id': 'endless-board-empty', text: 'Nobody has cleared a floor this season yet. Climb to get on the board.' }));
    else {
      const list = h('ol', { class: 'blist' }, b.top.map(row));
      if (b.you && !b.top.some((r) => r.you)) list.append(h('li', { class: 'brow gap muted', text: '…' }), row(b.you));
      card.append(list);
      card.append(h('p', { class: 'small muted num', 'data-id': 'endless-board-total', text: plural(b.total, 'trainer', 'trainers') + ' on the board · ties go to whoever got there first' }));
    }

    const ed = ui.nameEdit;
    if (!ed) card.append(h('div', { class: 'row between small' }, [
      h('span', { 'data-id': 'endless-name' }, b.name ? ['Your name: ', h('b', { text: b.name })]
        : ['Your name: ', h('span', { class: 'muted', text: b.fallbackName + ' (until you pick one)' })]),
      h('button', { class: 'btn small', type: 'button', 'data-act': 'endless-name', text: b.name ? 'Change' : 'Pick a name',
        onclick: () => { ui.nameEdit = { draft: b.name || '', error: null }; render(); const i = document.querySelector('[data-id=endless-name-input]'); if (i) i.focus(); } }),
    ]));
    else {
      const input = h('input', { type: 'text', 'data-id': 'endless-name-input', maxlength: String(b.nameMax + 8), autocomplete: 'off', spellcheck: 'false',
        placeholder: b.fallbackName, 'aria-label': 'Display name', value: ed.draft });
      input.oninput = () => { ed.draft = input.value; };
      const save = () => {
        const r = run.setName(input.value);
        if (!r.ok) { ed.draft = input.value; ed.error = r.reason; render(); const i = document.querySelector('[data-id=endless-name-input]'); if (i) i.focus(); return; }
        ui.nameEdit = null; toast(r.name ? 'Name saved: ' + r.name : 'Name cleared'); render();
      };
      input.onkeydown = (e) => { if (e.key === 'Enter') { e.preventDefault(); save(); } else if (e.key === 'Escape') { ui.nameEdit = null; render(); } };
      card.append(h('div', { class: 'bname-edit' }, [
        h('label', { class: 'small', text: 'Display name: ' + b.nameMin + ' to ' + b.nameMax + ' letters, digits, spaces, - or _. Leave it empty to clear it.' }),
        input,
        ed.error ? h('p', { class: 'small bad', 'data-id': 'endless-name-error', role: 'alert', text: ed.error }) : null,
        h('div', { class: 'row' }, [
          h('button', { class: 'btn small primary', type: 'button', 'data-act': 'endless-name-save', text: 'Save', onclick: save }),
          h('button', { class: 'btn small', type: 'button', 'data-act': 'endless-name-cancel', text: 'Cancel', onclick: () => { ui.nameEdit = null; render(); } }),
        ]),
      ]));
    }
    main.append(card);
  }

  // The rulebook editor (web/rulebook.js, specs/28 section 4.5) draws its own overlay and calls onChange after a save. Its Test
  // button replays `floor` with the edited rulebooks: the floor that stopped the shown climb, else the next floor not yet cleared;
  // it needs a full team, so with fewer than teamSize monsters picked the editor gets no options and hides it.
  // The season is read at the tap, not at the render: a week can end while the screen is open.
  function openRulebook(vaultIndex) {
    if (!window.RulebookView) { toast('Rulebook editor not loaded'); return; }
    const season = run.endlessSeason(), ei = run.endlessInfo(season), r = ui.endlessResult;
    const floor = r && r.season === season ? r.final.floor : Math.min(ei.maxFloors, ei.clearedSeason + 1);
    const opts = ui.endlessTeam.length === ei.teamSize ? { season: season, team: ui.endlessTeam.slice(), floor: floor } : null;
    window.RulebookView.open({ run: run, C: C, h: h, toast: toast, sprite: sprite, TIP: TIP, up: up, num: num, onChange: () => render() }, vaultIndex, opts);
  }

  function climbEndless() {
    const season = run.endlessSeason(), team = ui.endlessTeam.slice();
    const r = run.runEndless(season, team);
    if (!r.ok) { toast(r.reason); render(); return; }
    r.team = team; r.books = teamBooks(team);   // what endlessFloor needs to replay this climb's other floors, and the rulebooks it used
    ui.endlessResult = r;
    (r.achievements || []).forEach((a) => toast('Achievement: ' + a.name + (a.species ? '. ' + a.speciesName + ' unlocked!' : '')));
    stopPlayback();
    ui.endlessFight = ui.speed === 'skip' ? null : r.final;   // the deciding floor plays at once, unless the speed is Skip
    if (ui.endlessFight) startPlayback(r.final);
    render();
    $('main').scrollTop = 0;
  }

  // The climb's result: a banner (it slams in once, when the player first sees it: after the deciding floor's playback closes, or
  // at once on Skip), what it paid, then every floor fought with a Watch button and the deciding floor's log.
  const FOUGHT_SHOWN = 3;   // a long climb lists its last floors, the rest one tap away (issue #130: the result card fits a phone)
  function endlessResult(main, r, season) {
    const reveal = !r.seen && !ui.endlessFight;
    if (reveal) r.seen = true;
    const n = r.floorsCleared;
    const big = r.capped ? 'Reached the top!' : n ? plural(n, 'floor cleared', 'floors cleared') : 'Defeated';
    const sub = r.capped ? 'All ' + n + ' floors cleared: nothing stopped this team' : 'Fell on floor ' + r.floorReached + ' to ' + r.final.name;
    const pay = [
      h('div', { class: r.shards ? 'gems num' : 'muted', 'data-id': 'endless-shards', text: r.shards ? '+' + r.shards + ' Training Shards' : 'No new floors this season: nothing to pay' }),
      r.newFloors.length ? h('div', { 'data-id': 'endless-new', text: 'New this season: ' + floorsText(r.newFloors) }) : null,
      r.firstClears.length ? h('div', { class: 'gold', 'data-id': 'endless-first', text: 'First clears ever: ' + floorsText(r.firstClears) }) : null,
      ...(r.achievements || []).map((a) => h('div', { class: 'gold', 'data-id': 'endless-earned-' + a.id }, [h('b', { text: '🏆 ' + a.name }), h('span', { class: 'muted', text: ' · ' + a.text })])),
    ];
    const card = h('div', { class: 'card alert eresult', 'data-id': 'endless-result', 'data-season': String(r.season) }, [
      h('div', { class: 'loot ' + (r.capped ? 'clear' : r.shards ? 'some' : 'none') + (reveal ? ' anim' : ''), 'data-id': 'endless-banner' }, [
        h('div', { class: 'lhead' }, [h('div', { class: 'big', text: big }), h('div', { class: 'sub', 'data-id': 'endless-fell', text: sub })]),
      ]),
      h('div', { class: 'epay small' }, pay),
      r.season !== season ? h('p', { class: 'small muted', 'data-id': 'endless-old', text: "This climb was last season's: this week's floors are new." }) : null,
      teamBooks(r.team) !== r.books ? h('p', { class: 'small muted', 'data-id': 'endless-books-changed', text: 'Rulebooks changed since this climb: Watch replays a floor with the new ones.' }) : null,
    ]);
    const rows = h('div', { class: 'small floors-fought', 'data-id': 'endless-fought' });
    const all = r.showAll || r.floors.length <= FOUGHT_SHOWN + 2;
    if (!all) rows.append(h('button', { class: 'btn small', type: 'button', 'data-act': 'endless-fought-all', text: 'Show all ' + r.floors.length + ' floors',
      onclick: () => { r.showAll = true; render(); } }));
    (all ? r.floors : r.floors.slice(-FOUGHT_SHOWN)).forEach((f) => rows.append(h('div', { class: 'row between', 'data-id': 'endless-fought-' + f.floor, 'data-hash': f.hash }, [
      h('span', null, [
        h('span', { class: f.won ? 'ok' : 'bad', text: (f.won ? '✓ ' : '✗ ') + 'F' + f.floor + ' ' + (f.shapeName || f.shape) }),
        f.elite ? h('span', { class: 'gold', text: ' · elite' }) : null,
        h('span', { class: 'muted', text: [f.phases.join(' → '), f.vents ? plural(f.vents, 'vent', 'vents') : ''].filter(Boolean).map((t) => ' · ' + t).join('') }),
      ]),
      h('button', { class: 'btn small', type: 'button', 'data-act': 'endless-watch-' + f.floor, text: 'Watch', onclick: () => endlessWatch(f.floor) }),
    ])));
    card.append(rows);
    const f = r.final, v = endlessView(f);
    const list = h('ul', { class: 'alog', 'data-id': 'endless-log' });
    f.actions.forEach((x) => { const l = v.line(x); list.append(h('li', { class: l.cls, text: l.text })); });
    card.append(h('details', null, [h('summary', { class: 'small', text: 'Floor ' + f.floor + ' fight log (' + f.actionCount + ' actions' + (f.actionCount > f.actions.length ? ', first ' + f.actions.length + ' shown' : '') + ')' }), list]));
    main.append(card);
  }

  // Plays one floor of the shown climb: the deciding floor is in the climb's result (while the rulebooks are the ones it used);
  // the engine replays any other (read only).
  function endlessWatch(floor) {
    const r = ui.endlessResult;
    if (!r) return;
    const f = floor === r.final.floor && teamBooks(r.team) === r.books ? r.final : run.endlessFloor(r.season, r.team, floor);
    if (f.ok === false) { toast(f.reason); return; }
    ui.endlessFight = f;
    startPlayback(f, true);
    render();
  }

  // The playback modal: the tower floor's view over a 4v4 grid with the floor's mutators, plus watch again, the floors either
  // side and close.
  function endlessPlayModal(modal, f) {
    const r = ui.endlessResult, at = r ? r.floors.findIndex((x) => x.floor === f.floor) : -1;
    const prev = at > 0 ? r.floors[at - 1] : null, next = r && at >= 0 && at < r.floors.length - 1 ? r.floors[at + 1] : null;
    const close = () => { stopPlayback(); ui.endlessFight = null; render(); };
    fightPlayView(modal, f, endlessView(f), h('div', { class: 'row', style: 'flex-wrap:wrap' }, [
      ui.play ? null : h('button', { class: 'btn small', type: 'button', 'data-act': 'endless-replay', text: 'Watch again', onclick: () => endlessWatch(f.floor) }),
      prev ? h('button', { class: 'btn small', type: 'button', 'data-act': 'endless-prev', text: '← Floor ' + prev.floor, onclick: () => endlessWatch(prev.floor) }) : null,
      next ? h('button', { class: 'btn small', type: 'button', 'data-act': 'endless-next', text: 'Floor ' + next.floor + ' →', onclick: () => endlessWatch(next.floor) }) : null,
    ]));
    modal.append(actionBar(h('button', { class: 'btn primary block', type: 'button', 'data-act': 'endless-play-close', text: 'Close', onclick: close })));
  }

  // An Endless floor's fight is labelled as a tower floor's ("Floor N: <name>", units named by the bridge), laid out 4v4, with
  // the mutators in force under the title.
  function endlessView(f) {
    const v = towerView(f);
    v.cls = 'endless';
    v.head = h('div', { class: 'emuts small', 'data-id': 'endless-play-mutators' }, f.mutators && f.mutators.length
      ? f.mutators.map((m) => h('div', { class: 'emut on' }, [h('b', { text: m.name }), h('span', { class: 'muted', text: ' · ' + m.desc })]))
      : [h('span', { class: 'muted', text: 'No mutators on this floor' })]);
    return v;
  }

  function vaultHelpers() {
    return { h: h, C: C, run: run, toast: toast, rerender: render, nodeRow: nodeRow, nodePath: nodePath, treeView: treeView, elementTag: elementTag, up: up, num: num, bar: bar, statGrid: statGrid, TIP: TIP, sprite: sprite };
  }

  // The grade screen's Dungeon ticket line: the pieces this grade earned, any whole ticket they made, and the progress after.
  function ticketLine() {
    const g = ui.ticketGain;
    if (!g) return null;
    const ti = run.towerInfo(run.towerDay());
    const toward = ti.ticketPieces + '/' + ti.piecesPerTicket + ' toward the next';
    return h('div', { class: 'center small num', 'data-id': 'grade-tickets', text: g.pieces
      ? '+' + g.pieces + ' Dungeon ticket ' + (g.pieces === 1 ? 'piece' : 'pieces') + (g.tickets ? ' · +' + g.tickets + (g.tickets === 1 ? ' ticket!' : ' tickets!') : '') + ' · ' + toward
      : 'No Dungeon ticket pieces at this grade · ' + toward });
  }

  function renderGrade(main) {
    const e = ui.sealed, sp = speciesDef(e.species) || { name: e.species };
    const r = C.ranks[e.rank];
    main.append(
      h('div', { class: 'center small muted', text: 'Run complete' }),
      h('div', { class: 'grade ' + e.grade, 'data-id': 'grade', text: up(e.grade) }),
      h('div', { class: 'center' }, [h('b', { text: sp.name + ' reached ' + r.name })]),
      e.gauntlet ? h('div', { class: 'legacy', 'data-id': 'grade-title', text: e.gauntlet.label }) : null,
      h('div', { class: 'center small muted', text: e.fightsWon + ' of ' + e.fightsFought + ' rank fights won. Sealed in the Vault.' }),
      h('div', { class: 'center gems num', 'data-id': 'payout', text: '+' + ui.payout + ' Training Shards' }),
      ticketLine(),
    );
    main.append(h('div', { class: 'card', 'data-id': 'grade-stats' }, [h('h3', { text: 'Stat breakdown' }), statGrid(e.stats)]));
    main.append(h('div', { class: 'card' }, [h('h3', { text: 'Trees' }),
      ...e.trees.map((t) => h('div', { class: 'row between' }, [h('span', { text: treeName(t.id) }), h('span', { class: 'small muted num', text: t.bought + '/' + (C.trees[t.id] ? C.trees[t.id].nodes.length : '?') })]))]));
    ui.earned.forEach((a) => main.append(h('div', { class: 'card alert', 'data-id': 'earned-' + a.id }, [
      h('b', { text: '🏆 Achievement: ' + a.name }),
      h('div', { class: 'small muted', text: a.text }),
      a.species ? h('div', { class: 'small gold', 'data-id': 'unlocked-' + a.species, text: a.speciesName + ' unlocked! Pick it on the next run.' }) : null,
    ])));
    const acct = run.account;
    if (acct.vaultOver > 0) main.append(h('p', { class: 'small bad center', 'data-id': 'grade-vault-over', role: 'status',
      text: 'Sealed over the Vault cap (' + acct.vault.length + '/' + acct.vaultCap + '). Dismantle ' + acct.vaultOver + ' before the next run.' }));
    main.append(actionBar(acct.vendor && acct.vendor.offers.length
      ? h('button', { class: 'btn primary block', type: 'button', 'data-act': 'to-vendor', text: 'Visit the vendor', onclick: () => { stopClimb(); ui.view = 'vendor'; startReveal(); render(); } })
      : h('button', { class: 'btn primary block', type: 'button', 'data-act': 'new-run', text: 'New run', onclick: closeVendor })));   // a vendor with nothing to sell closes too
  }

  // ---- post-run vendor (specs/18 section 4): offers rolled when the run was sealed, rarer with a better grade, bought with shards.
  const rarityDef = (id) => C.rarities.find((r) => r.id === id) || { id: id, name: id };
  function achievementProgress(id) { return run.account.achievements.find((x) => x.id === id) || { earned: false, current: 0, target: 1 }; }
  function owned(id) { return !!id && run.account.owned.indexOf(id) >= 0; }
  const offerTier = (o) => Math.max(0, C.rarities.indexOf(rarityDef(o.rarity)));
  // How offer i shows during the merchant's reveal: 'back' (face down), 'hint' (face down, glowing its tier's colour), 'flip'
  // (turning face up now) or 'face'. Without a reveal (home screen, reveal done or skipped) every offer is 'face'.
  function revealState(i) {
    const r = ui.reveal;
    if (!r || ui.view !== 'vendor' || i < r.i) return 'face';
    if (i > r.i) return 'back';
    return r.phase;
  }
  function vendorOffers(main) {
    const acct = run.account;
    acct.vendor.offers.forEach((o, i) => {
      const tier = offerTier(o), state = revealState(i);
      if (state === 'back' || state === 'hint') {
        main.append(h('div', { class: 'card offer back' + (state === 'hint' ? ' hint r' + tier : ''), 'data-id': 'vendor-offer', 'aria-hidden': 'true',
          onclick: skipReveal }, [h('span', { class: 'mark', text: '?' })]));
        return;
      }
      const def = C.talents.find((x) => x.id === o.talent) || { name: o.talent, desc: '', maxRank: 0 };
      const rar = rarityDef(o.rarity);
      const status = o.bought ? h('div', { class: 'small ok', text: 'Sold' })
        : o.maxed ? h('div', { class: 'small muted', text: 'Maxed' })
        : h('button', { class: 'btn small gemsy', type: 'button', 'data-act': 'buy-offer-' + i, disabled: acct.shards < o.price,
          text: 'Buy · ' + o.price + ' shards', onclick: () => buyOffer(i) });
      main.append(h('div', { class: 'card offer r' + tier + (o.bought ? ' sold' : '') + (state === 'flip' ? ' flip' + (tier >= 2 ? ' burst' : '') + (tier >= 3 ? ' shake' : '') : ''),
        'data-id': 'vendor-offer' }, [
        h('div', { class: 'row between' }, [h('b', { text: def.name }), h('span', { class: 'tag rarity', text: rar.name })]),
        h('div', { class: 'small muted', text: def.desc }),
        def.legendary ? h('div', { class: 'small gold', 'data-id': 'legendary-note', text: 'Legendary: changes a rule. ' + C.consts.legendarySlots + ' can be on per run; pick it on the species screen.' }) : null,
        h('div', { class: 'row between' }, [
          h('span', { class: 'small num', text: o.bought || o.maxed ? 'Rank ' + o.rank + '/' + def.maxRank : '+1 rank · ' + o.rank + ' → ' + (o.rank + 1) + '/' + def.maxRank }),
          status]),
      ]));
    });
  }

  // ---- the merchant's reveal (issue #108): presentation only. The offers were rolled and saved when the run was sealed; this
  // turns them face up one at a time, each glowing its tier's colour first (rarer tiers build up longer, epic and up burst as they
  // flip). It plays when the player visits the vendor from the grade screen; tapping a face-down card or Skip shows them all.
  // Reduced motion shows them at once. Not tied to the fight speed, so a player who skips fights still sees it.
  const REVEAL_HINT_MS = [260, 520, 820, 1150];   // PLACEHOLDER: glow time before the flip, by tier (common, rare, epic, legendary+)
  const REVEAL_FLIP_MS = 380;                      // PLACEHOLDER: the flip; matches the .offer.flip animation
  function startReveal() {
    stopReveal();
    const v = run.account.vendor;
    if (!v || !v.offers.length || reducedMotion()) return;
    ui.reveal = { i: 0, phase: 'hint', timer: null };
    scheduleReveal();
  }
  function scheduleReveal() {
    const r = ui.reveal, o = run.account.vendor.offers[r.i];
    r.timer = setTimeout(stepReveal, r.phase === 'hint' ? REVEAL_HINT_MS[Math.min(offerTier(o), REVEAL_HINT_MS.length - 1)] : REVEAL_FLIP_MS);
  }
  function stepReveal() {
    const r = ui.reveal, v = run.account.vendor;
    if (!r) return;
    if (ui.view !== 'vendor' || !v) { stopReveal(); return; }
    if (r.phase === 'hint') r.phase = 'flip';
    else {
      r.i++;
      r.phase = 'hint';
      if (r.i >= v.offers.length) { stopReveal(); render(); return; }
    }
    render();
    scheduleReveal();
  }
  function stopReveal() { if (ui.reveal) clearTimeout(ui.reveal.timer); ui.reveal = null; }
  function skipReveal() { if (!ui.reveal) return; stopReveal(); render(); }
  function buyOffer(i) {
    const r = run.acct('buyOffer', i);
    if (!r.ok) toast(r.reason);
    else { const def = C.talents.find((x) => x.id === r.talent); toast('Bought ' + (def ? def.name : r.talent) + ' +1 for ' + r.cost + ' shards'); }
    render();
  }
  // Vendor reroll (specs/18 section 4, issue #104): replaces the unbought offers; the price rises with each reroll this visit.
  function rerollButton() {
    const acct = run.account, v = acct.vendor;
    if (!v || !v.canReroll) return null;
    return h('button', { class: 'btn gemsy', type: 'button', 'data-act': 'vendor-reroll', disabled: acct.shards < v.rerollPrice,
      text: 'Reroll · ' + v.rerollPrice + ' shards', 'aria-label': 'Reroll unbought offers for ' + v.rerollPrice + ' shards', onclick: rerollVendor });
  }
  function rerollVendor() {
    stopReveal();
    const r = run.acct('rerollVendor');
    if (!r.ok) toast(r.reason);
    else toast('Rerolled the offers for ' + r.cost + ' shards');
    render();
  }
  // Leaving the vendor closes it (kj, 2026-11-01): the unbought offers are gone, and the account no longer holds a vendor.
  function closeVendor() {
    stopReveal();
    const r = run.acct('closeVendor');
    if (!r.ok) toast('The vendor did not close: ' + r.reason);
    resetRunUi();
    ui.view = 'home';
    render();
  }
  function renderVendor(main) {
    const acct = run.account, v = acct.vendor;
    main.append(h('div', { class: 'row between' }, [h('h2', { text: 'Vendor' }), h('span', { class: 'num gems', 'data-id': 'shards', text: acct.shards + ' shards' })]));
    if (!v || !v.offers.length) main.append(h('p', { class: 'muted', text: 'Every talent is maxed: nothing to sell.' }));
    else {
      main.append(h('p', { class: 'small muted', text: v.offers.length + ' offers for your grade ' + up(v.grade) + ' run: a better grade rolls rarer talents. Each offer adds one rank. Talents apply from the next run on. Unbought offers vanish when you leave.' }));
      if (ui.reveal) main.append(h('div', { class: 'row between' }, [
        h('span', { class: 'small merchant', 'data-id': 'merchant-line', text: 'The merchant lays out the wares…' }),
        h('button', { class: 'btn small', type: 'button', 'data-act': 'reveal-skip', text: 'Skip', onclick: skipReveal })]));
      vendorOffers(main);
    }
    main.append(actionBar([v && v.offers.length ? rerollButton() : null,
      h('button', { class: 'btn primary', type: 'button', 'data-act': 'vendor-done', text: 'Done', onclick: closeVendor })]));
  }

  // ---- gauntlet wheel (specs/19, specs/12 section 6): a slowing reel of the floors climbed lands on the floor and the enemy
  // that beat the monster, then a comic defeat. The floors come with the seal (finishRun's climb); this only animates them, once.
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
    const you = h('div', { class: 'fighter you' }, [sprite(e.species, e.apex), h('span', { text: e.name || sp.name })]);
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
  function resetRunUi() { stopClimb(); stopPlayback(); stopReveal(); stopEvolve(); ui.climb = null; ui.confirmAbandon = false; VAULT.reset(); ui.feed = []; ui.last = null; ui.fight = null; ui.sealed = null; ui.payout = 0; ui.ticketGain = null; ui.tab = 'train'; ui.homeTab = 'raise'; ui.showAll = false; ui.spGains = []; ui.drillPop = null; ui.fightGains = []; ui.earned = []; ui.treeOpen = {}; }
  const log = (line) => { ui.feed.unshift(line); };

  // The book the picked species' book-gated chase tree needs, if any.
  function speciesBook() { const sp = C.species.find((x) => x.id === ui.pick); return sp ? sp.book : null; }
  // The book the start screen offers to bring: the picked species' own book if owned, else the first owned book.
  function bookToBring() {
    const has = (id) => (run.account.books[id] || 0) > 0;
    const own = speciesBook();
    return own && has(own) ? own : Object.keys(C.books).find(has) || null;
  }

  // Skill points a command brought (issue #86), read from the state before and after it: a won fight's r.sp is the rank win, the
  // rest arrived with a new cycle (SP_PER_CYCLE). Each gain is logged and queued for the HUD; returns them.
  function spGained(sp0, cycle0, r) {
    const s = run.state, total = s.sp - sp0;
    if (!r.ok || total <= 0) return [];
    const win = Math.min(total, r.sp || 0);   // a lost fight's r.sp is the Grudge Match talent's
    const gains = [];
    if (win > 0) gains.push({ n: win, why: r.won ? 'rank win' : 'Grudge Match', cycle: cycle0 });
    if (total > win) gains.push({ n: total - win, why: s.cycle > cycle0 ? 'new cycle' : 'gained', cycle: s.cycle });
    gains.forEach((g) => { log('Cycle ' + g.cycle + ': +' + g.n + ' SP (' + g.why + ')'); ui.spGains.push(g); });
    return gains;
  }

  // Switch an owned talent on or off for the next run (issue #106). Switching a legendary on when every legendary slot is taken
  // switches the legendaries that were on off, so the player swaps one for another in one tap.
  function toggleTalent(id, on) {
    const acct = run.account, def = C.talents.find((x) => x.id === id);
    let off = acct.talentsOff.filter((x) => x !== id);
    if (!on) off.push(id);
    else if (def.legendary) {
      const active = acct.talents.filter((t) => t.rank > 0 && t.id !== id && off.indexOf(t.id) < 0 && C.talents.find((x) => x.id === t.id).legendary);
      if (active.length >= C.consts.legendarySlots) active.forEach((t) => off.push(t.id));
    }
    const r = run.acct('setLoadout', off);
    if (!r.ok) toast(r.reason);
    render();
  }

  function startRun() {
    if (!ui.pick) return;
    resetRunUi();
    const bring = bookToBring();
    const r = run.startRun(ui.pick, ui.book && bring ? [bring] : [], run.account.talentsOff);
    if (!r.ok) { toast(r.reason); render(); return; }
    ui.book = false;
    ui.view = 'run';
    if (run.state.sp > 0) log('Cycle 1: +' + run.state.sp + ' SP (run start)');
    render();
  }

  function after(r) {          // common tail of a command: report a refusal, refresh
    if (!r.ok) toast(r.reason);
    render();
  }
  // A milestone reached: logged, and toasted only when no offer is waiting. When one is, its banner already names the stat and
  // threshold (issue #115: the toast was redundant and covered it); with none, no Awakening is waiting, so the toast does not say so.
  function milestones(r) {
    const offered = run.state.offers.length > 0;
    (r.milestones || []).forEach((m) => { if (!offered) toast('Milestone: ' + up(m.stat) + ' ' + m.threshold); log('Milestone ' + up(m.stat) + ' ' + m.threshold); });
  }

  function drill(stat) {
    const sp0 = run.state.sp, cycle0 = run.state.cycle;
    const r = run.cmd('drill', stat);
    if (r.ok) {
      ui.last = r; ui.flash = { stat, tier: r.tier }; ui.drillPop = { stat, tier: r.tier, gain: r.gain, sad: r.tier === 'fail' || !!r.injury };
      log('Drill ' + up(stat) + ': ' + r.tierName + ' +' + num(r.gain) + ' → ' + num(r.value) + (r.injured ? ' (injured)' : '') +
        (r.shielded ? ' (Second Wind stopped an injury)' : '') + (r.free ? ' (free: Momentum)' : '') +
        (r.echoStat ? ' (echo: ' + up(r.echoStat) + ' +' + num(r.echoGain) + ')' : ''));
      if (r.shielded) toast('Second Wind! The injury was prevented.');
      milestones(r);
      if (r.injury) {
        toast(r.injury.back ? 'Injured! Back in cycle ' + r.injury.back + '.' : 'Injured! The run is over.');
        log('Injured in cycle ' + r.injury.cycle + (r.injury.back ? ': back in cycle ' + r.injury.back : ': out for the rest of the run'));
      }
      spGained(sp0, cycle0, r);
    }
    after(r);
  }
  function rest() {
    const sp0 = run.state.sp, cycle0 = run.state.cycle;
    const r = run.cmd('rest');
    if (r.ok) { ui.last = r; log('Rest +' + num(r.restored) + ' energy'); spGained(sp0, cycle0, r); }
    after(r);
  }
  function fight() {
    const sp0 = run.state.sp, cycle0 = run.state.cycle, before = matchup(run.state);   // the intro shows the matchup as it was fought
    const r = run.cmd('fight');
    if (r.ok) {
      ui.fight = r; ui.last = null; ui.showAll = false;
      startPlayback(null, false, before);
      log('Rank ' + r.opponent.letter + ' fight vs ' + r.opponent.name + ': ' + (r.won ? 'won → ' + rank(r.rankAfter).name + ' (+' + r.sp + ' SP)' : 'lost (rematch next time)'));
      ui.fightGains = spGained(sp0, cycle0, r);
    }
    after(r);
  }
  function take(choice, slot) {
    const before = run.state.slots[slot];
    const r = run.cmd('take', choice, slot);
    if (r.ok) {
      log('Took ' + treeName(r.placed) + ' into slot ' + (slot + 1) + (before ? ' (replaced ' + treeName(before.tree) + ')' : '') + (r.refunded ? ' · +' + r.refunded + ' SP refunded' : ''));
      r.removed.forEach((id) => { if (!before || id !== before.tree) toast(treeName(id) + ' left its slot'); });
    }
    after(r);
  }
  function discard() { const r = run.cmd('discard'); if (r.ok) log('Discarded a tree offer'); after(r); }
  function reroll() { const r = run.cmd('reroll'); if (r.ok) log('Second Look: rerolled an Awakening'); after(r); }
  function spend(slot) {
    const r = run.cmd('spend', slot);
    if (r.ok) { log('Bought ' + r.node + (r.statGain ? ' (+' + num(r.statGain) + ')' : '')); milestones(r); }
    after(r);
  }
  function useBook(id) { const r = run.cmd('book', id); if (r.ok) log('Used ' + C.books[id].name); after(r); }

  function seal() {
    const r = run.finishRun();
    if (!r.ok) { toast(r.reason); return; }
    ui.sealed = r.entry; ui.payout = r.shards; ui.ticketGain = { pieces: r.ticketPieces, tickets: r.ticketsEarned }; ui.view = 'grade'; ui.earned = r.achievements || [];
    ui.earned.forEach((a) => toast('Achievement: ' + a.name + (a.species ? '. ' + a.speciesName + ' unlocked!' : '')));
    // The wheel shows the climb once, from the floors finishRun returned; only the result is kept (specs/19 section 7).
    if (r.entry.gauntlet && r.climb) { openClimb(r.climb); return; }
    render();
  }

  // ------------------------------------------------------------ debug controls
  // Debug-only controls (specs/21 section 7): the home screen's "Test: add a book" button. On with ?debug in the page URL, or
  // after five quick taps on the header's build stamp, which is remembered on this device (five more turn it off).
  const DEBUG_KEY = 'idle-rng-debug';
  function debugOn() {
    if (/[?&]debug(=|&|$)/.test(location.search)) return true;
    try { return localStorage.getItem(DEBUG_KEY) === '1'; } catch (e) { return false; }
  }
  function watchDebugTaps() {
    let taps = [];
    $('build').addEventListener('click', () => {
      const now = Date.now();
      taps = taps.filter((t) => now - t < 2000).concat(now);
      if (taps.length < 5) return;
      taps = [];
      const on = !debugOn();
      try { localStorage.setItem(DEBUG_KEY, on ? '1' : '0'); } catch (e) { /* storage blocked: ?debug still works */ }
      toast(debugOn() ? 'Debug controls on' : 'Debug controls off');
      render();
    });
  }

  // ------------------------------------------------------------ boot
  async function boot() {
    try {
      run = await RE.create({});
      C = run.content;
      TIP.init(() => C, () => run.state);
      ui.pick = C.species[0].id;
      ui.speed = loadSpeed();
      watchDebugTaps();
      if (run.state) ui.view = 'run';
      if (run.loadNotice) toast(run.loadNotice);
      if (run.loadError) toast('Your saved account could not be loaded and was kept aside: ' + run.loadError);
      render();
    } catch (e) {
      $('main').replaceChildren(h('h2', { text: 'The game did not load' }), h('p', { class: 'err', text: e && e.message ? e.message : String(e) }));
    }
  }
  boot();
})();
