// Vault screen (issue #81, specs/18 section 9): the list of sealed monsters on the home screen, with sort, filter and search, a
// detail sheet for one entry and a side-by-side compare of two. Lock and dismantle (issue #82, specs/18 section 7) always address
// an entry by its real index in the engine's oldest-first Vault, never by its position in the sorted or filtered list.
// UI only: every value comes from the engine's account view and content; nothing here is a game rule.
(function () {
  'use strict';
  const VIEW_KEY = 'idle-rng-vault-view';   // the viewer's sort and filters (a convenience, see loadView)
  const DEFAULT_VIEW = { sort: 'new', species: '', element: '', title: '', lock: '' };
  // Filter values for the title select that are not a title text.
  const HAS_TITLE = '\u0001has', NO_TITLE = '\u0001none';

  let X = null;   // the run screen's helpers, set by render: { h, C, run, toast, rerender, nodeRow, elementTag, up, num, bar, TIP }
  const st = {
    view: loadView(),        // { sort, species, element, title, lock }
    search: '',              // text search; not remembered, so a reload never hides monsters behind a forgotten search
    showFilters: false,      // the filter panel is open
    confirm: null,           // entry awaiting the dismantle confirm: { i (vault index), hash }
    detail: null,            // entry shown in the detail sheet: { i, hash }
    pick: null,              // first entry picked for a compare: { i, hash }
    compare: null,           // the two entries compared: [{ i, hash }, { i, hash }]
    sheetTop: false,         // a sheet was just opened: scroll the modal to its top
  };

  function loadView() {
    const v = Object.assign({}, DEFAULT_VIEW);
    try {
      const saved = JSON.parse(localStorage.getItem(VIEW_KEY) || 'null');
      if (saved && typeof saved === 'object') for (const k in DEFAULT_VIEW) if (typeof saved[k] === 'string') v[k] = saved[k];
    } catch (e) { /* storage blocked or junk: defaults */ }
    return v;
  }
  function saveView() {
    try { localStorage.setItem(VIEW_KEY, JSON.stringify(st.view)); } catch (e) { /* storage blocked: the choice lasts this page only */ }
  }

  // ------------------------------------------------------------ entry facts
  // The entry's species, named by its apex form when it has one (specs/15 section 5.3); speciesName keeps the species' own name.
  const spDef = (e) => {
    const sp = X.C.species.find((s) => s.id === e.species) || { name: e.species, element: null };
    return { name: e.name || sp.name, speciesName: sp.name, element: sp.element };
  };
  const total = (e) => Object.keys(e.stats).reduce((a, k) => a + e.stats[k], 0);
  const floorOf = (e) => (e.gauntlet ? e.gauntlet.floor : -1);   // a cleared tower stores GAUNTLET_FLOORS + 1, so it sorts on top
  const rankName = (e) => (X.C.ranks[e.rank] ? X.C.ranks[e.rank].name : e.grade);
  const elementName = (id) => (X.C.elements[id] ? X.C.elements[id].name : id || '');
  const treeName = (id) => (X.C.trees[id] ? X.C.trees[id].name : id);
  const treeSize = (id) => (X.C.trees[id] ? X.C.trees[id].nodes.length : '?');
  // An entry reference stays valid only while the entry at that index is still the same monster (a dismantle shifts later ones).
  const refOk = (r) => { const v = X.run.account.vault; return !!r && r.i >= 0 && r.i < v.length && v[r.i].hash === r.hash; };
  const ref = (e, i) => ({ i: i, hash: e.hash });
  const same = (r, e, i) => !!r && r.i === i && r.hash === e.hash;

  function sorts() {
    const list = [['new', 'Newest first'], ['old', 'Oldest first'], ['grade', 'Grade (best first)'], ['floor', 'Gauntlet floor (highest)'],
      ['species', 'Species (A to Z)'], ['total', 'Stat total (highest)']];
    X.C.stats.forEach((s) => list.push(['stat:' + s, X.up(s) + ' (highest)']));
    return list;
  }
  // Comparator over [entry, index] pairs; ties fall back to newest first so the order is stable and total.
  function comparator(sort) {
    const newest = (a, b) => b[1] - a[1];
    const desc = (f) => (a, b) => f(b[0]) - f(a[0]) || newest(a, b);
    if (sort === 'old') return (a, b) => a[1] - b[1];
    if (sort === 'grade') return desc((e) => e.rank);
    if (sort === 'floor') return desc(floorOf);
    if (sort === 'total') return desc(total);
    if (sort === 'species') return (a, b) => spDef(a[0]).speciesName.localeCompare(spDef(b[0]).speciesName) || b[0].rank - a[0].rank || newest(a, b);
    if (sort.startsWith('stat:')) { const k = sort.slice(5); return desc((e) => e.stats[k] || 0); }
    return newest;
  }
  function searchText(e) {
    const sp = spDef(e), g = e.gauntlet;
    return [sp.name, sp.speciesName, elementName(sp.element), e.grade, rankName(e), g ? g.title : '', g ? g.label : '', g && g.killerName ? g.killerName : '',
      e.end, ...e.trees.map((t) => treeName(t.id))].join(' \n ').toLowerCase();
  }
  function matches(e) {
    const v = st.view, sp = spDef(e);
    if (v.species && e.species !== v.species) return false;
    if (v.element && sp.element !== v.element) return false;
    if (v.title === HAS_TITLE && !e.gauntlet) return false;
    if (v.title === NO_TITLE && e.gauntlet) return false;
    if (v.title && v.title !== HAS_TITLE && v.title !== NO_TITLE && (!e.gauntlet || e.gauntlet.title !== v.title)) return false;
    if (v.lock === 'locked' && !e.locked) return false;
    if (v.lock === 'unlocked' && e.locked) return false;
    const q = st.search.trim().toLowerCase();
    if (q && !q.split(/\s+/).every((w) => searchText(e).includes(w))) return false;
    return true;
  }
  // The entries to show, as [entry, vault index] pairs in display order.
  function shown() {
    const vault = X.run.account.vault;
    return vault.map((e, i) => [e, i]).filter((p) => matches(p[0])).sort(comparator(st.view.sort));
  }
  const activeFilters = () => ['species', 'element', 'title', 'lock'].filter((k) => st.view[k]).length + (st.search.trim() ? 1 : 0);

  // ------------------------------------------------------------ list
  // Draws the Vault list into main, under the home screen's Vault tab header (run.js draws the count). When the detail or compare sheet is open, draws it into modal (shown over the home screen).
  function render(main, modal, helpers) {
    X = helpers;
    const h = X.h, acct = X.run.account;
    if (st.confirm && !refOk(st.confirm)) st.confirm = null;
    if (st.detail && !refOk(st.detail)) st.detail = null;
    if (st.pick && !refOk(st.pick)) st.pick = null;
    if (st.compare && !st.compare.every(refOk)) st.compare = null;

    if (!acct.vault.length) { main.append(h('p', { class: 'small muted', text: 'Sealed monsters appear here.' })); return renderSheet(modal); }
    main.append(h('p', { class: 'small muted', text: 'Dismantle a monster for Training Shards; lock your favourites so they cannot be dismantled. Tap Details for its stats and trees, or Compare on two monsters.' }));
    const list = h('div', { class: 'vlist', 'data-id': 'vault-list' });
    main.append(controls(() => fillList(list)), list);
    fillList(list);
    renderSheet(modal);
  }

  // The search, sort and filter controls; refresh() redraws the list they drive.
  function controls(refresh) {
    const h = X.h, v = st.view;
    const refill = () => { saveView(); refresh(); };
    const select = (id, label, options, value, onchange) => {
      const s = h('select', { 'data-act': id, 'aria-label': label, onchange: (ev) => onchange(ev.target.value) },
        options.map((o) => h('option', { value: o[0], text: o[1] })));
      s.value = value;
      return h('label', { class: 'vfield' }, [h('span', { class: 'small muted', text: label }), s]);
    };
    const search = h('input', { type: 'search', 'data-act': 'vault-search', placeholder: 'Search species, title, tree…', 'aria-label': 'Search the Vault', autocomplete: 'off',
      oninput: (ev) => { st.search = ev.target.value; refresh(); syncFilterButton(); } });
    search.value = st.search;
    const filterBtn = h('button', { class: 'btn small', type: 'button', 'data-act': 'vault-filters', 'aria-expanded': st.showFilters ? 'true' : 'false',
      onclick: () => { st.showFilters = !st.showFilters; X.rerender(); } });
    const syncFilterButton = () => { const n = activeFilters(); filterBtn.textContent = 'Filters' + (n ? ' · ' + n : ''); };
    syncFilterButton();

    const box = h('div', { class: 'vcontrols', 'data-id': 'vault-controls' }, [
      search,
      h('div', { class: 'vrow' }, [
        select('vault-sort', 'Sort', sorts(), v.sort, (x) => { v.sort = x; refill(); }),
        filterBtn,
      ]),
    ]);
    if (st.showFilters) {
      const vault = X.run.account.vault;
      const titles = [];
      vault.forEach((e) => { if (e.gauntlet && titles.indexOf(e.gauntlet.title) < 0) titles.push(e.gauntlet.title); });
      if (v.title && v.title !== HAS_TITLE && v.title !== NO_TITLE && titles.indexOf(v.title) < 0) titles.push(v.title);   // keep a remembered pick visible
      titles.sort((a, b) => a.localeCompare(b));
      const set = (k) => (x) => { v[k] = x; refill(); syncFilterButton(); };
      box.append(h('div', { class: 'vgrid', 'data-id': 'vault-filter-panel' }, [
        select('vault-species', 'Species', [['', 'All species']].concat(X.C.species.map((s) => [s.id, s.name])), v.species, set('species')),
        select('vault-element', 'Element', [['', 'All elements']].concat(Object.keys(X.C.elements).map((k) => [k, elementName(k)])), v.element, set('element')),
        select('vault-title', 'Legacy Title', [['', 'Any'], [HAS_TITLE, 'Has a title'], [NO_TITLE, 'No title']].concat(titles.map((t) => [t, t])), v.title, set('title')),
        select('vault-lock', 'Lock', [['', 'Locked or not'], ['locked', 'Locked only'], ['unlocked', 'Unlocked only']], v.lock, set('lock')),
      ]));
    }
    return box;
  }

  function clearFilters() {
    st.search = '';
    Object.assign(st.view, { species: '', element: '', title: '', lock: '' });
    saveView();
    X.rerender();
  }

  function fillList(list) {
    const h = X.h, vault = X.run.account.vault, rows = shown();
    const kids = [];
    const filtered = rows.length !== vault.length;
    kids.push(h('div', { class: 'row between small muted', 'data-id': 'vault-showing' }, [
      h('span', { text: filtered ? 'Showing ' + rows.length + ' of ' + vault.length : vault.length + (vault.length === 1 ? ' monster' : ' monsters') }),
      filtered ? h('button', { class: 'btn small', type: 'button', 'data-act': 'vault-clear', text: 'Clear filters', onclick: clearFilters }) : null,
    ]));
    if (st.pick) {
      const e = vault[st.pick.i];
      kids.push(h('div', { class: 'card alert row between', style: 'flex-direction:row', 'data-id': 'compare-pick' }, [
        h('span', { class: 'small', text: 'Comparing ' + X.up(e.grade) + ' ' + spDef(e).name + ': tap Compare on another monster.' }),
        h('button', { class: 'btn small', type: 'button', 'data-act': 'compare-cancel', text: 'Cancel', onclick: () => { st.pick = null; X.rerender(); } }),
      ]));
    }
    if (!rows.length) kids.push(h('p', { class: 'small muted', 'data-id': 'vault-empty', text: 'No monster in the Vault matches.' }));
    rows.forEach((p) => kids.push(card(p[0], p[1])));
    list.replaceChildren(...kids);
  }

  function card(e, i) {
    const h = X.h, sp = spDef(e);
    return h('div', { class: 'card' + (same(st.pick, e, i) ? ' alert' : ''), 'data-id': 'vault-entry', 'data-index': String(i) }, [
      h('div', { class: 'row between' }, [
        h('div', { class: 'row', style: 'min-width:0' }, [h('span', { class: 'num', style: 'font-size:28px;font-weight:700', text: X.up(e.grade) }),
          h('div', { style: 'min-width:0' }, [h('div', { class: 'row', style: 'gap:6px;flex-wrap:wrap' }, [h('b', { 'data-id': 'vault-species', text: sp.name }), sp.element ? X.elementTag(sp.element) : null, e.locked ? h('span', { class: 'small', text: '🔒' }) : null]),
            h('div', { class: 'small muted', text: (e.end === 'injured' ? 'Injured' : 'Completed') + ' · cycle ' + e.cycles + ' · ' + e.fightsWon + ' wins' })])]),
        h('span', { class: 'small muted num', 'data-id': 'vault-total', text: 'Σ ' + X.num(total(e)) }),
      ]),
      e.gauntlet ? h('div', { class: 'small gold', 'data-id': 'vault-title', text: e.gauntlet.label }) : null,
      h('div', { class: 'small muted', text: e.trees.map((t) => treeName(t.id) + ' ' + t.bought + '/' + treeSize(t.id)).join(' · ') }),
      e.sealedUtcMs ? h('div', { class: 'small muted', text: new Date(e.sealedUtcMs).toLocaleString() }) : null,
      actions(e, i),
    ]);
  }

  // Details, compare, lock toggle and dismantle (with an in-page confirm) for Vault entry i (its index in the engine's oldest-first list).
  function actions(e, i) {
    const h = X.h, sp = spDef(e), c = st.confirm;
    if (same(c, e, i)) {
      return h('div', { class: 'card alert', 'data-id': 'dismantle-confirm' }, [
        h('p', { class: 'small', text: 'Dismantle this ' + X.up(e.grade) + ' ' + sp.name + ' for ' + e.refund + ' Training Shards? It is gone for good.' }),
        h('div', { class: 'row' }, [
          h('button', { class: 'btn small warn', type: 'button', 'data-act': 'dismantle-yes', text: 'Yes, dismantle', onclick: () => {
            st.confirm = null;
            const r = X.run.acct('dismantle', i);
            X.toast(r.ok ? 'Dismantled ' + sp.name + ': +' + r.refund + ' shards' : r.reason);
            X.rerender();
          } }),
          h('button', { class: 'btn small', type: 'button', 'data-act': 'dismantle-no', text: 'Keep it', onclick: () => { st.confirm = null; X.rerender(); } }),
        ]),
      ]);
    }
    const picked = same(st.pick, e, i);
    return h('div', { class: 'row', style: 'flex-wrap:wrap;gap:6px;margin-top:6px' }, [
      h('button', { class: 'btn small', type: 'button', 'data-act': 'detail-' + i, text: 'Details', onclick: () => { st.detail = ref(e, i); st.sheetTop = true; X.rerender(); } }),
      h('button', { class: 'btn small' + (picked ? ' gemsy' : ''), type: 'button', 'data-act': 'compare-' + i, 'aria-pressed': picked ? 'true' : 'false', text: 'Compare',
        onclick: () => {
          if (picked) st.pick = null;
          else if (st.pick) { st.compare = [st.pick, ref(e, i)]; st.pick = null; st.sheetTop = true; }
          else st.pick = ref(e, i);
          X.rerender();
        } }),
      h('button', { class: 'btn small', type: 'button', 'data-act': 'lock-' + i, 'aria-pressed': e.locked ? 'true' : 'false',
        text: e.locked ? '🔒 Locked' : '🔓 Lock', title: e.locked ? 'Unlock so it can be dismantled' : 'Lock so it cannot be dismantled',
        onclick: () => { st.confirm = null; const r = X.run.acct('lock', i, !e.locked); if (!r.ok) X.toast(r.reason); X.rerender(); } }),
      h('button', { class: 'btn small warn', type: 'button', 'data-act': 'dismantle-' + i, disabled: e.locked,
        text: 'Dismantle · +' + e.refund + ' shards', onclick: () => { st.confirm = ref(e, i); X.rerender(); } }),
    ]);
  }

  // ------------------------------------------------------------ detail and compare sheets (drawn into #modal)
  function renderSheet(modal) {
    if (st.sheetTop) { modal.scrollTop = 0; st.sheetTop = false; }   // a newly opened sheet starts at its top; a re-render keeps the place
    if (st.compare) { modal.hidden = false; compareSheet(modal); }
    else if (st.detail) { modal.hidden = false; detailSheet(modal); }
  }
  const close = () => { st.detail = null; st.compare = null; X.rerender(); };

  function head(e) {
    const h = X.h, sp = spDef(e);
    return h('div', { class: 'row' }, [
      h('span', { class: 'grade ' + e.grade, style: 'font-size:44px', text: X.up(e.grade) }),
      h('div', { style: 'min-width:0' }, [
        h('div', { class: 'row', style: 'gap:6px;flex-wrap:wrap' }, [h('b', { text: sp.name }), sp.element ? X.elementTag(sp.element) : null]),
        h('div', { class: 'small muted', text: rankName(e) + ' · ' + (e.end === 'injured' ? 'injured at cycle ' + e.cycles : 'completed ' + e.cycles + ' cycles') }),
      ]),
    ]);
  }

  function detailSheet(modal) {
    const h = X.h, i = st.detail.i, e = X.run.account.vault[i];
    modal.append(
      h('div', { class: 'row between' }, [h('h2', { 'data-id': 'detail-title', text: 'Vault monster' }), h('button', { class: 'btn small', type: 'button', 'data-act': 'detail-close', text: 'Close', onclick: close })]),
      head(e),
      e.gauntlet ? h('div', { class: 'legacy', 'data-id': 'detail-legacy', text: e.gauntlet.label }) : h('div', { class: 'small muted', text: 'Sealed before the gauntlet: no Legacy Title.' }),
      h('div', { class: 'small muted', 'data-id': 'detail-facts', text: e.fightsWon + ' of ' + e.fightsFought + ' rank fights won · dismantles for ' + e.refund + ' shards' +
        (e.sealedUtcMs ? ' · sealed ' + new Date(e.sealedUtcMs).toLocaleString() : '') }),
    );
    const stats = h('div', { class: 'card', 'data-id': 'detail-stats' }, [h('div', { class: 'row between' }, [h('h3', { text: 'Stats' }), h('span', { class: 'small muted num', text: 'Σ ' + X.num(total(e)) })])]);
    X.C.stats.forEach((s) => stats.append(h('div', { class: 'stat', style: 'grid-template-columns:38px 1fr' }, [
      h('span', { class: 'lbl', text: X.up(s), ...X.TIP.attrs('stat:' + s) }), X.bar(e.stats[s], X.C.consts.statMax, X.num(e.stats[s]))])));
    modal.append(stats);
    e.trees.forEach((t, k) => {
      const def = X.C.trees[t.id];
      const tc = h('div', { class: 'card', 'data-id': 'detail-tree-' + k }, [h('div', { class: 'row between' }, [h('b', { text: treeName(t.id) }),
        h('span', { class: 'small muted num', text: t.bought + '/' + treeSize(t.id) + ' bought' })])]);
      // Nodes are bought in order (specs/15 section 1), so the first `bought` nodes are the bought ones.
      if (def) def.nodes.forEach((n, ni) => tc.append(X.nodeRow(n, ni < t.bought ? 'got' : 'locked')));
      modal.append(tc);
    });
    if (!e.trees.length) modal.append(h('p', { class: 'small muted', text: 'No trees slotted.' }));
    modal.append(h('div', { class: 'row', style: 'flex-wrap:wrap' }, [
      h('button', { class: 'btn small', type: 'button', 'data-act': 'detail-lock', 'aria-pressed': e.locked ? 'true' : 'false', text: e.locked ? '🔒 Locked' : '🔓 Lock',
        onclick: () => { const r = X.run.acct('lock', i, !e.locked); if (!r.ok) X.toast(r.reason); X.rerender(); } }),
      h('button', { class: 'btn small', type: 'button', 'data-act': 'detail-compare', text: 'Compare with…',
        onclick: () => { st.pick = ref(e, i); st.detail = null; X.rerender(); } }),
    ]));
    modal.append(h('div', { class: 'small muted center', text: 'Run hash ' + e.hash.slice(0, 16) }));
  }

  function compareSheet(modal) {
    const h = X.h, vault = X.run.account.vault, a = vault[st.compare[0].i], b = vault[st.compare[1].i];
    modal.append(h('div', { class: 'row between' }, [h('h2', { 'data-id': 'compare-title', text: 'Compare' }),
      h('button', { class: 'btn small', type: 'button', 'data-act': 'compare-close', text: 'Close', onclick: close })]));
    modal.append(h('div', { class: 'vcmp-heads' }, [a, b].map((e, k) => h('div', { class: 'card', 'data-id': 'compare-head-' + k }, [head(e),
      e.gauntlet ? h('div', { class: 'small gold', text: e.gauntlet.label }) : h('div', { class: 'small muted', text: 'No Legacy Title' })]))));
    // One row per number: A, B and B minus A (green when B is higher, red when lower).
    const grid = h('div', { class: 'card vcmp', 'data-id': 'compare-grid' }, [
      h('span', { class: 'small muted' }), h('b', { class: 'small', text: 'A' }), h('b', { class: 'small', text: 'B' }), h('span', { class: 'small muted', text: 'B − A' })]);
    // va, vb: the numbers (null when one has none, then no diff); ta, tb: how they read (default: the number).
    const row = (id, label, va, vb, ta, tb) => {
      const d = va === null || vb === null ? null : vb - va;
      grid.append(h('span', { class: 'lbl', text: label }), h('span', { class: 'num', text: ta !== undefined ? ta : X.num(va) }), h('span', { class: 'num', text: tb !== undefined ? tb : X.num(vb) }),
        h('span', { class: 'num ' + (d > 0 ? 'ok' : d < 0 ? 'bad' : 'muted'), 'data-id': 'diff-' + id, text: d === null ? '—' : d === 0 ? '=' : (d > 0 ? '+' : '−') + X.num(Math.abs(d)) }));
    };
    row('rank', 'Rank', a.rank, b.rank, rankName(a), rankName(b));
    // Floor: the floor it fell on; a cleared tower stores GAUNTLET_FLOORS + 1, reads Cleared and counts one above the top floor.
    const floorNum = (e) => (e.gauntlet ? e.gauntlet.floor : null);
    const floorText = (e) => (!e.gauntlet ? '—' : e.gauntlet.cleared ? 'Cleared' : String(e.gauntlet.floor));
    row('floor', 'Floor', floorNum(a), floorNum(b), floorText(a), floorText(b));
    X.C.stats.forEach((s) => row(s, X.up(s), a.stats[s], b.stats[s]));
    row('total', 'Σ', total(a), total(b));
    modal.append(grid);
    modal.append(h('div', { class: 'vcmp-heads' }, [a, b].map((e, k) => h('div', { class: 'card', 'data-id': 'compare-trees-' + k }, [h('h3', { text: k ? 'B trees' : 'A trees' }),
      ...e.trees.map((t) => h('div', { class: 'row between small' }, [h('span', { text: treeName(t.id) }), h('span', { class: 'muted num', text: t.bought + '/' + treeSize(t.id) })]))]))));
    modal.append(h('button', { class: 'btn block', type: 'button', 'data-act': 'compare-swap', text: 'Swap A and B',
      onclick: () => { st.compare = [st.compare[1], st.compare[0]]; X.rerender(); } }));
  }

  // The Vault's search, sort and filters for another screen's list of Vault monsters (the Dungeon team picker, issue #98).
  // They share the Vault's remembered sort and filters. fill(rows, vaultSize) draws [entry, vault index] pairs in display order;
  // it runs now and whenever the controls change. Returns the controls element.
  function browse(helpers, fill) {
    X = helpers;
    const refresh = () => fill(shown(), X.run.account.vault.length);
    const box = controls(refresh);
    refresh();
    return box;
  }

  // Called when a run starts or ends: drop the transient picks (the sort and filters stay).
  function reset() { st.confirm = null; st.detail = null; st.pick = null; st.compare = null; }

  window.VaultView = { render: render, browse: browse, clear: clearFilters, reset: reset, VIEW_KEY: VIEW_KEY, total: total };
})();
