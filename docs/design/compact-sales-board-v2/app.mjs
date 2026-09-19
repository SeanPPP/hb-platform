// 独立销售看板 v2 设计原型：数据为确定性随机生成的示意数据，仅用于演示布局与联动规则。
const $ = (s) => document.querySelector(s)
const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]))
const money0 = new Intl.NumberFormat('en-AU', { style: 'currency', currency: 'AUD', maximumFractionDigits: 0 })
const money2 = new Intl.NumberFormat('en-AU', { style: 'currency', currency: 'AUD', minimumFractionDigits: 2, maximumFractionDigits: 2 })
const int = new Intl.NumberFormat('en-AU')
const pct = (v) => `${(v * 100).toFixed(1)}%`

function rng(seed) {
  let a = seed >>> 0
  return () => {
    a = (a + 0x6d2b79f5) | 0
    let t = Math.imul(a ^ (a >>> 15), 1 | a)
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296
  }
}

const BRANCHES = [
  ['1012', 'Glendale', 1.35], ['1005', 'Charlestown Square', 1.2], ['1017', 'Waratah', 1.05], ['1016', 'Greenhills', 1.0],
  ['1013', 'Orion', 0.86], ['1009', 'Lake Haven', 0.82], ['1024', 'Bankstown', 0.76], ['1015', 'Cronulla', 0.7],
  ['1007', 'Jesmond', 0.66], ['1033', 'Top Ryde', 0.62], ['1003', 'Peninsula Fair', 0.6], ['1004', 'Campbelltown', 0.58],
  ['1020', 'Wallsend', 0.52], ['1008', 'Warners Bay', 0.5], ['1010', 'Erina', 0.48], ['1011', 'Tuggerah', 0.46],
  ['1006', 'Kotara', 0.44], ['1030', 'Rhodes', 0.42], ['1026', 'Hurstville', 0.4], ['1027', 'Parramatta', 0.38],
  ['1028', 'Blacktown', 0.35], ['1029', 'Penrith', 0.33], ['1031', 'Castle Hill', 0.3], ['1032', 'Macquarie', 0.28], ['1025', 'Liverpool', 0.25],
].map(([code, name, w]) => ({ code, name, w }))

const BANKS = {
  art: { p: ['MP', 'MU', 'ME'], n: ['Acrylic Paint Set', 'Oil Pastel', 'Canvas Board', 'Easel Tabletop Pine', 'Palette Knife Set', 'Sketch Pencil Set', 'Watercolour Brush Round', 'Gouache Paint', 'Charcoal Stick', 'Drawing Paper A3'], s: ['12Pcs', '24 Colours', '20*25cm', '5Pcs', '6Pcs', '30ml', '50 Sheets', '12B'] },
  stationery: { p: ['ST'], n: ['Gel Pen Black', 'Highlighter Pastel', 'Sticky Notes', 'Spiral Notebook A5', 'Correction Tape', 'Pencil Case Zip'], s: ['10Pcs', '6 Colours', '76*76mm', '80 Sheets', '5m', 'Large'] },
  office: { p: ['OF'], n: ['Lever Arch File', 'Stapler Heavy Duty', 'Desk Organiser Mesh', 'Label Tape', 'Document Wallet'], s: ['A4', '24/6', '5 Compartment', '12mm', '10Pk'] },
  toy: { p: ['TY'], n: ['Bubble Wand', 'Slime Kit', 'Puzzle Floor', 'Water Pistol', 'Plush Bear', 'Building Blocks'], s: ['3Pk', 'Glitter', '48Pcs', '30cm', '25cm', '120Pcs'] },
  home: { p: ['HM'], n: ['Storage Basket Woven', 'Photo Frame', 'Cushion Cover', 'LED Fairy Light', 'Door Mat Coir'], s: ['Medium', '4*6in', '45*45cm', '5m', '40*60cm'] },
  kitchen: { p: ['KT'], n: ['Silicone Spatula', 'Food Container', 'Cutting Board Bamboo', 'Measuring Cup Set', 'Oven Mitt'], s: ['2Pk', '1.2L', '30*20cm', '4Pcs', 'Pair'] },
  hardware: { p: ['HW'], n: ['Cable Tie', 'Screwdriver Set', 'Hook Adhesive', 'Tape Measure'], s: ['100Pk', '6Pcs', '8Pk', '5m'] },
  beauty: { p: ['BT'], n: ['Makeup Brush Set', 'Hair Clip Claw', 'Nail File', 'Cotton Pad'], s: ['10Pcs', '4Pk', '6Pk', '120Pcs'] },
  digital: { p: ['DG'], n: ['USB-C Cable', 'Phone Holder Car', 'Earphone Wired', 'Power Board'], s: ['1m', 'Vent', '3.5mm', '4 Outlet'] },
  storage: { p: ['SG'], n: ['Vacuum Bag', 'Drawer Divider', 'Shoe Box Clear', 'Hanger Velvet'], s: ['3Pk', '4Pcs', 'Stackable', '10Pk'] },
  pet: { p: ['PT'], n: ['Pet Bowl Steel', 'Cat Toy Wand', 'Poop Bag Roll', 'Pet Brush'], s: ['Medium', 'Feather', '8 Rolls', 'Slicker'] },
  party: { p: ['PY'], n: ['Balloon Latex', 'Party Plate Paper', 'Banner Happy Birthday', 'Candle Number'], s: ['30Pk', '20Pk', 'Gold', 'Pink'] },
  craft: { p: ['CR'], n: ['Pom Pom Mixed', 'Felt Sheet', 'Glue Gun Mini', 'Pipe Cleaner'], s: ['100Pcs', '20Pk', '10W', '50Pk'] },
  festive: { p: ['FS'], n: ['Christmas Bauble', 'Tinsel Garland', 'Easter Egg Craft'], s: ['6Pk', '2m', '12Pcs'] },
  garden: { p: ['GD'], n: ['Plant Pot Ceramic', 'Garden Glove', 'Seed Tray'], s: ['15cm', 'Medium', '24 Cell'] },
  clean: { p: ['CL'], n: ['Microfibre Cloth', 'Scrub Sponge', 'Spray Bottle'], s: ['6Pk', '10Pk', '500ml'] },
  textile: { p: ['TX'], n: ['Tea Towel Cotton', 'Bath Towel', 'Table Runner'], s: ['2Pk', '70*140cm', '180cm'] },
  sport: { p: ['SP'], n: ['Skipping Rope', 'Yoga Mat', 'Drink Bottle'], s: ['2.8m', '6mm', '750ml'] },
}

const SUPPLIERS = [
  ['HB215', '玛索文具', 1.0, 'art', 180], ['HB102', '华瑞文具', 0.55, 'stationery', 70], ['HB118', '鑫达办公', 0.42, 'office', 60],
  ['HB233', '星辉玩具', 0.5, 'toy', 65], ['HB141', '恒昌家居', 0.46, 'home', 60], ['HB156', '金盛厨具', 0.4, 'kitchen', 55],
  ['HB167', '永固五金', 0.3, 'hardware', 45], ['HB179', '雅妍美妆', 0.34, 'beauty', 50], ['HB188', '联拓数码', 0.28, 'digital', 40],
  ['HB195', '佳纳收纳', 0.32, 'storage', 45], ['HB204', '萌宠用品', 0.22, 'pet', 35], ['HB221', '欢聚派对', 0.26, 'party', 40],
  ['HB228', '巧艺手工', 0.24, 'craft', 35], ['HB240', '喜庆节日', 0.18, 'festive', 30], ['HB246', '绿源园艺', 0.16, 'garden', 28],
  ['HB252', '洁亮清洁', 0.2, 'clean', 30], ['HB260', '锦织纺织', 0.14, 'textile', 25], ['HB271', '跃动运动', 0.12, 'sport', 22],
].map(([code, name, w, cat, n]) => ({ code, name, w, cat, n }))

const FIXED_HB215 = [
  ['MP165-24', '380g Canvas Frame 60*90cm', 25.99, 2.2], ['MP165-38', '380g Canvas Frame 90*120cm', 35.99, 1.3],
  ['MU098-7', 'A4 Cardstock 230G 50 Sheets', 9.99, 1.8], ['MP128-2', 'A4 Sketch Pad 160G 24 Sheets', 3.5, 2.5],
  ['MP165-10', '380g Canvas Frame 30*40cm', 6.99, 2.1], ['MP174-2', 'Paint Brush Flat Head 6Pcs', 4.5, 2.3],
  ['MP126-2', 'A4 Watercolor Pad 300G 12 Sheets', 12.99, 1.5], ['MP130-24', '280g Canvas Frame 60*90cm', 11.99, 1.4],
  ['MP130-13', '280g canvas frame 30*40cm', 5.99, 1.9], ['ME553-1', 'Modelling Clay White 250g', 2.99, 2.4], ['MP231-2', 'Oil Pastel 24 Colours', 3.99, 1.9],
]
const PRICES = [1.5, 1.99, 2.5, 2.99, 3.5, 3.99, 4.5, 4.99, 5.99, 6.99, 7.99, 8.99, 9.99, 12.99, 14.99, 19.99, 24.99, 29.99]
const THUMB_COLORS = ['#e6f0ff', '#fdf0e6', '#eef8ea', '#f6f0ff', '#fff4e0', '#e8f7f7']

const products = []
{
  const r = rng(20260919)
  for (const s of SUPPLIERS) {
    const bank = BANKS[s.cat]
    const seen = new Set()
    const push = (item, name, price, pop) => {
      if (seen.has(item)) return
      seen.add(item)
      products.push({ code: `${s.code}-${item}`, item, name, price, pop, supplier: s.code, tone: THUMB_COLORS[products.length % THUMB_COLORS.length] })
    }
    if (s.code === 'HB215') FIXED_HB215.forEach(([item, name, price, pop]) => push(item, name, price, pop))
    while (seen.size < s.n) {
      const prefix = bank.p[Math.floor(r() * bank.p.length)]
      const item = `${prefix}${100 + Math.floor(r() * 180)}-${1 + Math.floor(r() * 40)}`
      const name = `${bank.n[Math.floor(r() * bank.n.length)]} ${bank.s[Math.floor(r() * bank.s.length)]}`
      push(item, name, PRICES[Math.floor(r() * PRICES.length)], 0.08 + 2.2 * r() ** 3)
    }
  }
}
const productMap = new Map(products.map((p) => [p.code, p]))
const supplierMap = new Map(SUPPLIERS.map((s) => [s.code, s]))
const branchMap = new Map(BRANCHES.map((b) => [b.code, b]))

const RANGES = {
  today: { seed: 1, scale: 1, from: '2026-09-19', to: '2026-09-19' },
  yesterday: { seed: 2, scale: 1.12, from: '2026-09-18', to: '2026-09-18' },
  week: { seed: 3, scale: 5.4, from: '2026-09-14', to: '2026-09-20' },
  month: { seed: 4, scale: 17, from: '2026-09-01', to: '2026-09-30' },
}

// 服务端「门店×商品」聚合立方体的示意：真实实现中按日期范围只查一次，之后所有联动都在内存计算。
function buildCube(rangeKey) {
  const { seed, scale } = RANGES[rangeKey]
  const r = rng(seed * 7919)
  const rows = []
  for (const b of BRANCHES) {
    for (const p of products) {
      const lambda = 0.55 * scale * b.w * supplierMap.get(p.supplier).w * p.pop
      if (r() < 1 - Math.exp(-lambda)) {
        const q = 1 + Math.floor(r() * lambda * 1.6)
        rows.push({ b: b.code, p: p.code, s: p.supplier, q, a: q * p.price })
      }
    }
  }
  return rows
}

const params = new URLSearchParams(location.search)
const state = {
  range: 'today',
  cube: [],
  branch: null, supplier: null, product: null,
  productKeyword: '', supplierKeyword: '',
  page: 1, pageSize: 80,
  sort: { branch: { f: 'amount', o: 'desc' }, supplier: { f: 'amount', o: 'desc' }, product: { f: 'amount', o: 'desc' } },
  loading: new Set(),
  skeleton: false,
  elapsed: 38,
  cacheHit: false,
}

function aggregate(filter, keyOf) {
  const map = new Map()
  for (const row of state.cube) {
    if (filter.branch && row.b !== filter.branch) continue
    if (filter.supplier && row.s !== filter.supplier) continue
    if (filter.product && row.p !== filter.product) continue
    const key = keyOf(row)
    let agg = map.get(key)
    if (!agg) { agg = { key, q: 0, a: 0, skus: new Set() }; map.set(key, agg) }
    agg.q += row.q
    agg.a += row.a
    agg.skus.add(row.p)
  }
  return [...map.values()]
}

function totals(filter) {
  const skus = new Set(), branches = new Set(), suppliers = new Set()
  let q = 0, a = 0
  for (const row of state.cube) {
    if (filter.branch && row.b !== filter.branch) continue
    if (filter.supplier && row.s !== filter.supplier) continue
    if (filter.product && row.p !== filter.product) continue
    q += row.q; a += row.a
    skus.add(row.p); branches.add(row.b); suppliers.add(row.s)
  }
  return { q, a, skus: skus.size, branches: branches.size, suppliers: suppliers.size }
}

function sortRows(rows, { f, o }, textOf) {
  const dir = o === 'asc' ? 1 : -1
  const val = (row) => f === 'amount' ? row.a : f === 'qty' ? row.q : f === 'price' ? (row.q ? row.a / row.q : 0) : textOf(row)
  return rows.sort((x, y) => {
    const vx = val(x), vy = val(y)
    const c = typeof vx === 'string' ? vx.localeCompare(vy, 'en', { numeric: true }) : vx - vy
    return c !== 0 ? c * dir : String(x.key).localeCompare(String(y.key))
  })
}

// 交叉筛选：每栏只受「其他栏」的选中项约束，不被自身选中项收窄。
function computeView() {
  const { branch, supplier, product } = state
  const branchRows = sortRows(aggregate({ supplier, product }, (r) => r.b), state.sort.branch, (r) => branchMap.get(r.key).name)
  let supplierRows = sortRows(aggregate({ branch, product }, (r) => r.s), state.sort.supplier, (r) => supplierMap.get(r.key).name)
  let productRows = sortRows(aggregate({ branch, supplier }, (r) => r.p), state.sort.product, (r) => productMap.get(r.key).item)
  const branchTotal = branchRows.reduce((t, r) => t + r.a, 0)
  const supplierTotal = supplierRows.reduce((t, r) => t + r.a, 0)
  const productTotal = productRows.reduce((t, r) => t + r.a, 0)
  productRows.forEach((r, i) => { r.rank = i + 1 })
  const sk = state.supplierKeyword.trim().toLowerCase()
  if (sk) supplierRows = supplierRows.filter((r) => `${supplierMap.get(r.key).name} ${r.key}`.toLowerCase().includes(sk))
  const terms = state.productKeyword.trim().toLowerCase().split(/\s+/).filter(Boolean)
  if (terms.length) productRows = productRows.filter((r) => { const p = productMap.get(r.key); const hay = `${p.item} ${p.name}`.toLowerCase(); return terms.every((t) => hay.includes(t)) })
  return {
    branchRows, supplierRows, productRows, branchTotal, supplierTotal, productTotal,
    kpi: totals({ branch, supplier, product }), all: totals({}),
  }
}

const caret = '<span class="car"><svg class="up" viewBox="0 0 8 4"><path d="M4 0 8 4H0z"/></svg><svg class="dn" viewBox="0 0 8 4"><path d="M0 0h8L4 4z"/></svg></span>'
function th(dim, title, field, right, extra = '') {
  if (!field) return `<th class="${right ? 'r' : ''}">${title}${extra}</th>`
  const s = state.sort[dim]
  const aria = s.f === field ? ` aria-sort="${s.o === 'asc' ? 'ascending' : 'descending'}"` : ''
  return `<th class="${right ? 'r' : ''}"${aria}><button class="sort" data-dim="${dim}" data-field="${field}">${title}${caret}</button>${extra}</th>`
}
const shareCell = (value, total, label) => `<td><div class="share" title="${esc(label)}：${money0.format(total)}"><div class="bar"><i style="width:${total ? Math.max(2, (value / total) * 100) : 0}%"></i></div><span>${total ? pct(value / total) : '-'}</span></div></td>`
const skeletonRows = (cols, n) => Array.from({ length: n }, (_, i) => `<tr class="skel-row">${Array.from({ length: cols }, (_, c) => `<td><span class="skel" style="width:${c === 0 ? 60 + (i * 13) % 30 : 70}%"></span></td>`).join('')}</tr>`).join('')
const emptyRow = (cols) => `<tr class="empty"><td colspan="${cols}"><p>当前筛选组合下没有销售记录</p><button data-action="clear">清除筛选</button></td></tr>`
const rowAttrs = (dim, key, selected) => `data-dim="${dim}" data-key="${esc(key)}" tabindex="0" role="button" aria-pressed="${selected}"${selected ? ' class="sel"' : ''}`

function thumbSvg(p) {
  const hue = p.tone
  return `<div class="thumb" style="background:${hue}"><svg viewBox="0 0 30 30"><rect x="7" y="6" width="16" height="18" rx="2" fill="#fff" stroke="#d0d7e2"/><rect x="10" y="10" width="10" height="2" rx="1" fill="#c4cedc"/><rect x="10" y="14" width="7" height="2" rx="1" fill="#dbe2ec"/></svg></div>`
}

function render() {
  const v = computeView()
  const range = RANGES[state.range]
  document.querySelectorAll('#quickRange button').forEach((b) => b.setAttribute('aria-checked', String(b.dataset.range === state.range)))
  $('#rangeText').children[0].textContent = range.from
  $('#rangeText').children[2].textContent = range.to

  // KPI
  const k = v.kpi, filtered = state.branch || state.supplier || state.product
  const kpiEl = $('#kpis')
  const cell = (label, value, sub, main) => `<div class="kpi${main ? ' main' : ''}${state.skeleton ? ' skeleton' : ''}"><span>${label}</span><strong>${value}</strong>${sub ?? ''}</div>`
  kpiEl.innerHTML = [
    cell('国内营业额', money0.format(k.a), filtered ? `<em class="hl">占全部 ${pct(v.all.a ? k.a / v.all.a : 0)} · 全部 ${money0.format(v.all.a)}</em>` : '<em>全部分店 · 全部供应商</em>', true),
    cell('销量', int.format(k.q), '<em>件</em>'),
    cell('动销商品', int.format(k.skus), '<em>款</em>'),
    cell('平均单价', money2.format(k.q ? k.a / k.q : 0), '<em>营业额 ÷ 销量</em>'),
    cell('分店', int.format(k.branches), '<em>有销售</em>'),
    cell('国内供应商', int.format(k.suppliers), '<em>有销售</em>'),
    `<div class="status">${state.skeleton ? '<span class="ok">加载中…</span>' : `<span class="ok"><i></i>统计已更新 · 截至 19:31</span><small>本次查询 <b>${state.elapsed} ms</b><span class="badge">${state.cacheHit ? '命中缓存' : '实时聚合'}</span></small>`}</div>`,
  ].join('')

  // 联动筛选条
  const x = '<svg viewBox="0 0 10 10"><path d="M2 2l6 6M8 2 2 8"/></svg>'
  const chips = []
  if (state.branch) { const b = branchMap.get(state.branch); chips.push(`<span class="chip branch"><i></i><span class="k">分店</span><b>${esc(b.name)}</b><small>${b.code}</small><button data-clear="branch" aria-label="移除分店筛选">${x}</button></span>`) }
  if (state.supplier) { const s = supplierMap.get(state.supplier); chips.push(`<span class="chip supplier"><i></i><span class="k">国内供应商</span><b>${esc(s.name)}</b><small>${s.code}</small><button data-clear="supplier" aria-label="移除供应商筛选">${x}</button></span>`) }
  if (state.product) { const p = productMap.get(state.product); chips.push(`<span class="chip product"><i></i><span class="k">商品</span><b>${esc(p.item)}</b><small>${esc(p.name)}</small><button data-clear="product" aria-label="移除商品筛选">${x}</button></span>`) }
  $('#filters').innerHTML = `<span class="lbl">联动筛选</span>${chips.length ? `${chips.join('')}<button class="link-btn" data-action="clear">清除全部</button><span class="hint"><kbd>Esc</kbd></span>` : '<span class="hint">点击任意分店、供应商或商品行即可联动筛选，再点一次取消；各栏不会被自身的选中项收窄。</span>'}`

  // 分店
  const loadingCls = (dim) => $(`.panel[data-dim="${dim}"]`).classList.toggle('is-loading', state.loading.has(dim))
  ;['branch', 'supplier', 'product'].forEach(loadingCls)
  $('#branchCount').innerHTML = `<b>${v.branchRows.length}</b> 家`
  $('#branchHint').textContent = state.product ? '该商品在各分店的销售' : state.supplier ? '该供应商在各分店的销售' : ''
  $('#branchHead').innerHTML = `<tr>${th('branch', '分店', 'name')}${th('branch', '金额', 'amount', true)}${th('branch', '数量', 'qty', true)}${th('branch', '占比', null, false, '<span class="tip" title="分母为本栏合计">ⓘ</span>')}</tr>`
  $('#branchBody').innerHTML = state.skeleton ? skeletonRows(4, 14) : v.branchRows.length ? v.branchRows.map((r) => {
    const b = branchMap.get(r.key)
    return `<tr ${rowAttrs('branch', r.key, r.key === state.branch)}><td><div class="name"><b>${esc(b.name)}</b><small>${b.code}<span class="sep">·</span>${r.skus.size} 款</small></div></td><td class="r num">${money0.format(r.a)}</td><td class="r num muted">${int.format(r.q)}</td>${shareCell(r.a, v.branchTotal, '分店合计')}</tr>`
  }).join('') : emptyRow(4)

  // 供应商
  $('#supplierCount').innerHTML = `<b>${v.supplierRows.length}</b> 个`
  $('#supplierHead').innerHTML = `<tr>${th('supplier', '国内供应商', 'name')}${th('supplier', '金额', 'amount', true)}${th('supplier', '数量', 'qty', true)}${th('supplier', '占比', null)}</tr>`
  $('#supplierBody').innerHTML = state.skeleton ? skeletonRows(4, 14) : v.supplierRows.length ? v.supplierRows.map((r) => {
    const s = supplierMap.get(r.key)
    return `<tr ${rowAttrs('supplier', r.key, r.key === state.supplier)}><td><div class="name"><b>${esc(s.name)}</b><small>${s.code}<span class="sep">·</span>${r.skus.size} 款</small></div></td><td class="r num">${money0.format(r.a)}</td><td class="r num muted">${int.format(r.q)}</td>${shareCell(r.a, v.supplierTotal, '供应商合计')}</tr>`
  }).join('') : emptyRow(4)

  // 商品（服务端分页 + 全量排序）
  const total = v.productRows.length
  const pages = Math.max(1, Math.ceil(total / state.pageSize))
  if (state.page > pages) state.page = pages
  const start = (state.page - 1) * state.pageSize
  const pageRows = v.productRows.slice(start, start + state.pageSize)
  $('#productCount').innerHTML = `<b>${int.format(total)}</b> 款`
  $('#productHead').innerHTML = `<tr><th class="r">#</th><th>图片</th>${th('product', '货号 / 名称', 'item')}<th>供应商</th>${th('product', '数量', 'qty', true)}${th('product', '单价', 'price', true)}${th('product', '金额', 'amount', true)}<th>占比</th></tr>`
  $('#productBody').innerHTML = state.skeleton ? skeletonRows(8, 12) : pageRows.length ? pageRows.map((r, i) => {
    const p = productMap.get(r.key)
    const cur = p.supplier === state.supplier
    return `<tr ${rowAttrs('product', r.key, r.key === state.product)}><td class="rank${r.rank <= 3 && state.sort.product.f === 'amount' && state.sort.product.o === 'desc' ? ' top' : ''}">${r.rank}</td><td>${thumbSvg(p)}</td><td><div class="name"><b>${esc(p.item)}</b><small title="${esc(p.name)}">${esc(p.name)}</small></div></td><td><button class="sup-code${cur ? ' is-current' : ''}" data-supplier="${p.supplier}" title="${esc(supplierMap.get(p.supplier).name)}：点击按该供应商筛选">${p.supplier}</button></td><td class="r num muted">${int.format(r.q)}</td><td class="r num muted">${money2.format(r.q ? r.a / r.q : 0)}</td><td class="r num">${money0.format(r.a)}</td>${shareCell(r.a, v.productTotal, '商品合计')}</tr>`
  }).join('') : emptyRow(8)

  const sortLabel = { amount: '金额', qty: '数量', price: '单价', item: '货号' }[state.sort.product.f]
  const pageBtns = []
  const addPage = (n) => pageBtns.push(`<button data-page="${n}"${n === state.page ? ' aria-current="page"' : ''}>${n}</button>`)
  for (let n = 1; n <= pages; n++) {
    if (n === 1 || n === pages || Math.abs(n - state.page) <= 1) addPage(n)
    else if (pageBtns[pageBtns.length - 1] !== '<span class="gap">…</span>') pageBtns.push('<span class="gap">…</span>')
  }
  placeNotes()
  $('#productPager').innerHTML = state.skeleton ? '' : `<span class="meta">第 <b>${total ? start + 1 : 0}–${Math.min(start + state.pageSize, total)}</b> 条 / 共 ${int.format(total)} 条 · 按${sortLabel}${state.sort.product.o === 'asc' ? '升序' : '降序'}（全部结果排序）</span><span class="pages"><button data-page="${state.page - 1}" ${state.page <= 1 ? 'disabled' : ''} aria-label="上一页">‹</button>${pageBtns.join('')}<button data-page="${state.page + 1}" ${state.page >= pages ? 'disabled' : ''} aria-label="下一页">›</button><select id="pageSize" aria-label="每页条数">${[50, 80, 120, 200].map((n) => `<option value="${n}"${n === state.pageSize ? ' selected' : ''}>${n} 条/页</option>`).join('')}</select></span>`
}

// 设计标注：按元素实际位置放置编号圆点（?notes=1）
const NOTE_TARGETS = [
  [1, '.kpis', 'left'], [2, '.filters', 'left'], [3, '.panel[data-dim="branch"] .panel-head', 'right'],
  [4, '#productHead th[aria-sort] .sort', 'before'], [5, '.panel[data-dim="product"] .search', 'before'], [6, '#productBody tr:first-child .sup-code', 'top-left'],
  [7, '#productBody tr:first-child .share', 'top-left'], [8, '.panel[data-dim="product"]', 'top-center'], [9, '.status', 'before'],
]
function placeNotes() {
  const layer = $('#noteLayer')
  if (!document.body.classList.contains('show-notes')) { layer.innerHTML = ''; return }
  requestAnimationFrame(() => {
    layer.innerHTML = NOTE_TARGETS.map(([n, sel, at]) => {
      const el = document.querySelector(sel)
      if (!el) return ''
      const r = el.getBoundingClientRect()
      const pos = { left: [r.left, r.top + r.height / 2], right: [r.right - 14, r.top + 14], 'top-left': [r.left, r.top], 'top-center': [r.left + r.width / 2, r.top + 1], before: [r.left - 13, r.top + r.height / 2] }[at]
      return `<span class="note-pin" style="left:${pos[0]}px;top:${pos[1]}px">${n}</span>`
    }).join('')
  })
}

// 模拟服务端请求：只让受影响的栏进入加载态，旧数据保留。
let pending = 0
function commit(affected, mutate) {
  mutate()
  affected.forEach((d) => state.loading.add(d))
  const ticket = ++pending
  render()
  const ms = 26 + Math.floor(Math.random() * 60)
  setTimeout(() => {
    if (ticket !== pending) return
    state.loading.clear()
    state.elapsed = ms
    state.cacheHit = Math.random() > 0.4
    render()
  }, ms + 140)
}

const OTHERS = { branch: ['supplier', 'product'], supplier: ['branch', 'product'], product: ['branch', 'supplier'] }
function toggle(dim, key) {
  commit(OTHERS[dim], () => {
    state[dim] = state[dim] === key ? null : key
    // 商品只属于一个供应商：切到别的供应商时解除商品选择，避免隐藏条件继续生效。
    if (dim === 'supplier' && state.product && state[dim] && productMap.get(state.product).supplier !== state[dim]) state.product = null
    state.page = 1
  })
}
function clearDim(dim) { commit(['branch', 'supplier', 'product'], () => { state[dim] = null; state.page = 1 }) }
function clearAll() { commit(['branch', 'supplier', 'product'], () => { state.branch = state.supplier = state.product = null; state.page = 1 }) }

function cycleSort(dim, field) {
  const cur = state.sort[dim]
  const firstOrder = field === 'name' || field === 'item' ? 'asc' : 'desc'
  if (cur.f !== field) state.sort[dim] = { f: field, o: firstOrder }
  else if (cur.o === firstOrder) state.sort[dim] = { f: field, o: firstOrder === 'asc' ? 'desc' : 'asc' }
  else state.sort[dim] = { f: 'amount', o: 'desc' }
  state.page = 1
  // 分店、供应商是完整列表，本地即时排序；商品分页，需要服务端对全部结果排序。
  if (dim === 'product') commit(['product'], () => {})
  else render()
}

document.addEventListener('click', (e) => {
  const t = e.target.closest('button, tr[data-dim]')
  if (!t) return
  if (t.matches('.sort')) return cycleSort(t.dataset.dim, t.dataset.field)
  if (t.matches('.sup-code')) { e.stopPropagation(); return toggle('supplier', t.dataset.supplier) }
  if (t.dataset.clear) return clearDim(t.dataset.clear)
  if (t.dataset.action === 'clear') return clearAll()
  if (t.dataset.page) { const n = Number(t.dataset.page); return commit(['product'], () => { state.page = n; $('#productWrap').scrollTop = 0 }) }
  if (t.dataset.range) return commit(['branch', 'supplier', 'product'], () => { state.range = t.dataset.range; state.cube = buildCube(state.range); state.page = 1 })
  if (t.id === 'refreshBtn') return commit(['branch', 'supplier', 'product'], () => {})
  if (t.matches('tr[data-dim]')) return toggle(t.dataset.dim, t.dataset.key)
})
document.addEventListener('keydown', (e) => {
  if ((e.key === 'Enter' || e.key === ' ') && e.target.matches?.('tr[data-dim]')) { e.preventDefault(); toggle(e.target.dataset.dim, e.target.dataset.key) }
  if (e.key === 'Escape' && !e.target.matches?.('input')) clearAll()
})
document.addEventListener('change', (e) => { if (e.target.id === 'pageSize') commit(['product'], () => { state.pageSize = Number(e.target.value); state.page = 1 }) })
let searchTimer
$('#productSearch').addEventListener('input', (e) => { clearTimeout(searchTimer); searchTimer = setTimeout(() => commit(['product'], () => { state.productKeyword = e.target.value; state.page = 1 }), 200) })
$('#supplierSearch').addEventListener('input', (e) => { state.supplierKeyword = e.target.value; render() })

// 演示参数：?demo=linked | skeleton | loading，?notes=1 显示设计标注
state.cube = buildCube(state.range)
const demo = params.get('demo')
if (params.get('notes') === '1') document.body.classList.add('show-notes')
if (demo === 'linked' || demo === 'loading') { state.branch = '1012'; state.supplier = 'HB215' }
if (demo === 'product') { state.supplier = 'HB215'; state.product = 'HB215-MP165-24' }
if (demo === 'sorted') state.sort.product = { f: 'qty', o: 'desc' }
if (demo === 'skeleton') { state.skeleton = true; render() }
else {
  render()
  if (demo === 'loading') { state.loading = new Set(['supplier', 'product']); render() }
}
