import assert from 'node:assert/strict'

import {
  applyMultiBuyOffer,
  applyPosterKind,
  buildPosterPdfFallbackFileName,
  buildPromoPosterPdfRequest,
  canGeneratePosters,
  countPosterPages,
  createLoadingPosterRow,
  createPosterDraft,
  findUnprintablePosterChars,
  isPrintablePosterChar,
  normalizePromoPosterDefaults,
  parsePosterPageCount,
  parsePosterPdfFileName,
  pickDefaultPosterKind,
  preparePosterProducts,
  PROMO_POSTER_MAX_COUNT,
  resolvePosterKindAvailability,
  resolvePosterPriceMismatch,
  runWithConcurrency,
  summarizePosterBlockers,
  toDateOnly,
  validatePosterDraft,
  type PromoPosterDefaults,
  type PromoPosterRowState,
} from './promoPosterLogic'

const failures: string[] = []

async function test(name: string, execute: () => void | Promise<void>) {
  try {
    await execute()
    console.log(`ok - ${name}`)
  } catch (error) {
    const reason = error instanceof Error ? error.message : String(error)
    console.error(`not ok - ${name}\n${reason}`)
    failures.push(name)
  }
}

function makeDefaults(overrides: Partial<PromoPosterDefaults> = {}): PromoPosterDefaults {
  return {
    productCode: 'P001',
    itemNumber: 'HB-1001',
    productName: '不锈钢杯',
    englishName: 'Steel Mug',
    posterTitle: 'Steel Mug',
    retailPrice: 12.99,
    discountRate: 0.3,
    discountedPrice: 9.09,
    clearancePrice: 5,
    multiBuyOffers: [
      {
        promotionId: '11',
        name: '3 for 30',
        applyQuantity: 3,
        fixedPrice: 30,
        effectiveStart: '2026-09-19T00:00:00',
        effectiveEnd: '2026-10-02T23:59:59',
        productsCount: 4,
      },
    ],
    canSpecial: true,
    canMultiBuy: true,
    canClearance: true,
    ...overrides,
  }
}

type ReadyRow = Extract<PromoPosterRowState, { status: 'ready' }>

function readyRow(defaults: PromoPosterDefaults, itemNumber?: string): ReadyRow {
  return {
    key: defaults.productCode,
    product: { productCode: defaults.productCode, productName: defaults.productName, itemNumber },
    status: 'ready',
    defaults,
    draft: createPosterDraft(defaults),
  }
}

async function main() {
  await test('normalizePromoPosterDefaults 兼容 PascalCase，并过滤无效多件价促销', () => {
    const defaults = normalizePromoPosterDefaults({
      ProductCode: ' P009 ',
      ItemNumber: 'X-9',
      ProductName: '测试',
      PosterTitle: 'Test Item',
      RetailPrice: '10.5',
      DiscountRate: 0.2,
      DiscountedPrice: 8.4,
      ClearancePrice: 0,
      MultiBuyOffers: [
        { PromotionId: 5, ApplyQuantity: 2, FixedPrice: 18, EffectiveStart: '2026-09-01T00:00:00', EffectiveEnd: '2026-09-30T00:00:00', ProductsCount: 1 },
        { PromotionId: 6, ApplyQuantity: 1, FixedPrice: 5 },
        { PromotionId: 7, ApplyQuantity: 3, FixedPrice: 0 },
      ],
      CanSpecial: true,
      CanMultiBuy: 'true',
      CanClearance: false,
    })
    assert.ok(defaults)
    assert.equal(defaults.productCode, 'P009')
    assert.equal(defaults.retailPrice, 10.5)
    assert.equal(defaults.clearancePrice, null, '清仓价 0 视为未设置')
    assert.equal(defaults.multiBuyOffers.length, 1, '件数 < 2 或组合价 ≤ 0 的促销要丢弃')
    assert.equal(defaults.multiBuyOffers[0].promotionId, '5')
    assert.equal(defaults.canMultiBuy, true)
    assert.equal(normalizePromoPosterDefaults({ productName: 'x' }), null, '缺商品编码视为无效')
    assert.equal(normalizePromoPosterDefaults(null), null)
  })

  await test('默认类型按 特价 > 清仓 > 多件价 > 新品 取第一个可用', () => {
    assert.equal(pickDefaultPosterKind(makeDefaults()), 'special')
    assert.equal(pickDefaultPosterKind(makeDefaults({ discountedPrice: null })), 'clearance')
    assert.equal(pickDefaultPosterKind(makeDefaults({ discountedPrice: null, canClearance: false })), 'multibuy')
    assert.equal(
      pickDefaultPosterKind(makeDefaults({ discountedPrice: null, canClearance: false, canMultiBuy: true, multiBuyOffers: [] })),
      'new',
      'canMultiBuy 但没有促销时多件价不可用',
    )
    const availability = resolvePosterKindAvailability(makeDefaults({ canSpecial: false, canClearance: false, canMultiBuy: false }))
    assert.deepEqual(availability, { special: true, multibuy: false, new: true, clearance: false })
  })

  await test('草稿按类型预填价格，切换类型时保留品名', () => {
    const defaults = makeDefaults()
    const special = createPosterDraft(defaults)
    assert.deepEqual(special, { kind: 'special', title: 'Steel Mug', price: 9.09, wasPrice: 12.99, offerId: null })

    const noDiscount = makeDefaults({ discountedPrice: null })
    assert.deepEqual(applyPosterKind(createPosterDraft(noDiscount), noDiscount, 'special'), {
      kind: 'special', title: 'Steel Mug', price: 12.99, wasPrice: null, offerId: null,
    })

    const edited = { ...special, title: 'Big Steel Mug' }
    const clearance = applyPosterKind(edited, defaults, 'clearance')
    assert.deepEqual(clearance, { kind: 'clearance', title: 'Big Steel Mug', price: 5, wasPrice: 12.99, offerId: null })

    const multibuy = applyPosterKind(clearance, defaults, 'multibuy')
    assert.deepEqual(multibuy, { kind: 'multibuy', title: 'Big Steel Mug', price: 30, wasPrice: null, offerId: '11' })

    const fresh = applyPosterKind(multibuy, defaults, 'new')
    assert.deepEqual(fresh, { kind: 'new', title: 'Big Steel Mug', price: 12.99, wasPrice: null, offerId: null })

    assert.deepEqual(applyMultiBuyOffer(multibuy, defaults, 'missing'), { ...multibuy, offerId: null, price: null })
  })

  await test('品名字符校验：只允许 U+0020–U+024F 与常见排版标点', () => {
    for (const char of ['A', 'z', '9', ' ', '&', 'é', 'Ž', 'ɏ', '–', '—', '‘', '’', '“', '”', '×', '•', '…']) {
      assert.equal(isPrintablePosterChar(char), true, `${char} 应可打印`)
    }
    for (const char of ['杯', '€', '™', '😀', '\u0085', '\u0250']) {
      assert.equal(isPrintablePosterChar(char), false, `${char} 应不可打印`)
    }
    assert.deepEqual(findUnprintablePosterChars('Mug 杯子 杯 €'), ['杯', '子', '€'], '去重且保持出现顺序')
  })

  await test('行级校验覆盖品名、价格、原价与多件价促销', () => {
    const defaults = makeDefaults()
    const base = createPosterDraft(defaults)
    assert.deepEqual(validatePosterDraft(base, defaults), [])

    assert.deepEqual(validatePosterDraft({ ...base, title: '   ' }, defaults).map((i) => i.code), ['titleRequired'])
    assert.deepEqual(validatePosterDraft({ ...base, title: '不锈钢 Mug' }, defaults), [{ code: 'titleUnprintable', chars: '不锈钢' }])
    assert.deepEqual(validatePosterDraft({ ...base, title: 'A'.repeat(81) }, defaults), [{ code: 'titleTooLong', max: 80 }])
    assert.deepEqual(validatePosterDraft({ ...base, title: `  ${'A'.repeat(40)}   ${'B'.repeat(39)}  ` }, defaults), [], '先合并空白再算长度')

    assert.deepEqual(validatePosterDraft({ ...base, price: null }, defaults).map((i) => i.code), ['priceRequired'])
    assert.deepEqual(validatePosterDraft({ ...base, price: 0 }, defaults).map((i) => i.code), ['priceInvalid'])
    assert.deepEqual(validatePosterDraft({ ...base, price: 10000 }, defaults).map((i) => i.code), ['priceInvalid'])
    assert.deepEqual(validatePosterDraft({ ...base, wasPrice: null }, defaults), [], '特价原价可留空')
    assert.deepEqual(validatePosterDraft({ ...base, wasPrice: 9.09 }, defaults).map((i) => i.code), ['wasPriceNotHigher'])

    const fresh = applyPosterKind(base, defaults, 'new')
    assert.deepEqual(validatePosterDraft({ ...fresh, wasPrice: null }, defaults), [], '新品不需要原价')

    const multibuy = applyPosterKind(base, defaults, 'multibuy')
    assert.deepEqual(validatePosterDraft({ ...multibuy, offerId: null }, defaults).map((i) => i.code), ['offerRequired'])

    const noClearance = makeDefaults({ canClearance: false, clearancePrice: null })
    assert.deepEqual(
      validatePosterDraft(applyPosterKind(base, noClearance, 'clearance'), noClearance).map((i) => i.code),
      ['kindUnavailable', 'priceRequired'],
      '不可用类型要拦下（清仓价为空）',
    )
  })

  await test('海报价与系统价不一致提示（按分比较）', () => {
    const defaults = makeDefaults()
    assert.equal(resolvePosterPriceMismatch({ kind: 'special', price: 9.09 }, defaults), null)
    assert.equal(resolvePosterPriceMismatch({ kind: 'special', price: 9.090000001 }, defaults), null, '浮点误差不提示')
    assert.deepEqual(resolvePosterPriceMismatch({ kind: 'special', price: 8.99 }, defaults), { kind: 'special', expected: 9.09 })
    assert.deepEqual(resolvePosterPriceMismatch({ kind: 'clearance', price: 4.5 }, defaults), { kind: 'clearance', expected: 5 })
    assert.equal(resolvePosterPriceMismatch({ kind: 'clearance', price: 4.5 }, makeDefaults({ clearancePrice: null })), null)
    assert.equal(resolvePosterPriceMismatch({ kind: 'new', price: 1 }, defaults), null)
    assert.equal(resolvePosterPriceMismatch({ kind: 'special', price: null }, defaults), null)
  })

  await test('页数估算：拼版按 A4 1/A5 2/A6 4/A7 8 向上取整，不拼版每张一页', () => {
    assert.equal(countPosterPages(5, 'A4', true), 5)
    assert.equal(countPosterPages(5, 'A5', true), 3)
    assert.equal(countPosterPages(5, 'A6', true), 2)
    assert.equal(countPosterPages(8, 'A7', true), 1)
    assert.equal(countPosterPages(9, 'A7', true), 2)
    assert.equal(countPosterPages(9, 'A7', false), 9)
    assert.equal(countPosterPages(0, 'A6', true), 0)
  })

  await test('选中商品去重并截断到 200 个', () => {
    const many = Array.from({ length: 205 }, (_, index) => ({ productCode: `P${index}` }))
    const result = preparePosterProducts([{ productCode: ' P0 ' }, ...many, { productCode: '' }])
    assert.equal(result.products.length, PROMO_POSTER_MAX_COUNT)
    assert.equal(result.truncatedCount, 5)
    assert.equal(result.products[0].productCode, 'P0')
    assert.equal(result.products[1].productCode, 'P1', '重复编码只保留第一次出现')
  })

  await test('生成阻塞统计：加载中 / 失败 / 校验不通过都禁止生成', () => {
    const ok = readyRow(makeDefaults())
    const invalid = readyRow(makeDefaults({ productCode: 'P002', posterTitle: '' }))
    const loading = createLoadingPosterRow({ productCode: 'P003' })
    const failed: PromoPosterRowState = { key: 'P004', product: { productCode: 'P004' }, status: 'error', errorMessage: '商品不存在', errorStatus: 404 }

    assert.equal(canGeneratePosters(summarizePosterBlockers([ok])), true)
    assert.equal(canGeneratePosters(summarizePosterBlockers([])), false, '没有行时不能生成')
    assert.deepEqual(summarizePosterBlockers([ok, invalid, loading, failed]), { total: 4, loading: 1, failed: 1, invalid: 1 })
    assert.equal(canGeneratePosters(summarizePosterBlockers([ok, loading])), false)
  })

  await test('请求体按类型只带后端需要的字段', () => {
    const defaults = makeDefaults()
    const special = readyRow(defaults)
    const clearanceDefaults = makeDefaults({ productCode: 'P002', itemNumber: '' })
    const clearance: PromoPosterRowState = {
      ...readyRow(clearanceDefaults, 'ROW-2'),
      draft: { ...applyPosterKind(createPosterDraft(clearanceDefaults), clearanceDefaults, 'clearance'), title: '  Big   Mug ' },
    }
    const newDefaults = makeDefaults({ productCode: 'P003', itemNumber: '' })
    const fresh: PromoPosterRowState = {
      ...readyRow(newDefaults),
      draft: applyPosterKind(createPosterDraft(newDefaults), newDefaults, 'new'),
    }
    const multiDefaults = makeDefaults({ productCode: 'P004', retailPrice: null })
    const multibuy: PromoPosterRowState = {
      ...readyRow(multiDefaults),
      draft: applyPosterKind(createPosterDraft(multiDefaults), multiDefaults, 'multibuy'),
    }
    const loading = createLoadingPosterRow({ productCode: 'P005' })

    const body = buildPromoPosterPdfRequest('S01', [special, clearance, fresh, multibuy, loading], {
      style: 'modern',
      size: 'A6',
      impose: true,
      today: '2026-09-19',
    })

    assert.equal(body.storeCode, 'S01')
    assert.equal(body.impose, true)
    assert.equal(body.showLogo, true, '旧调用未传 showLogo 时默认开启')
    assert.equal(body.posters.length, 4, '非 ready 行不进请求')
    assert.deepEqual(body.posters[0], {
      kind: 'special', style: 'modern', size: 'A6', productCode: 'P001', itemNumber: 'HB-1001', title: 'Steel Mug', price: 9.09, wasPrice: 12.99,
    })
    assert.deepEqual(body.posters[1], {
      kind: 'clearance', style: 'modern', size: 'A6', productCode: 'P002', itemNumber: 'ROW-2', title: 'Big Mug', price: 5, wasPrice: 12.99,
    }, '品名合并空白；defaults 无货号时回退到列表行货号')
    assert.deepEqual(body.posters[2], {
      kind: 'new', style: 'modern', size: 'A6', productCode: 'P003', title: 'Steel Mug', price: 12.99, inStoreSince: '2026-09-19',
    }, '没有货号时不带 itemNumber')
    assert.deepEqual(body.posters[3], {
      kind: 'multibuy', style: 'modern', size: 'A6', productCode: 'P004', itemNumber: 'HB-1001', title: 'Steel Mug', price: 30,
      quantity: 3, mixAndMatch: true, validFrom: '2026-09-19', validTo: '2026-10-02',
    }, '多件价：零售价为空时不带 unitPrice，日期只取日期部分')

    const singleProductOffer = makeDefaults({
      multiBuyOffers: [{ ...makeDefaults().multiBuyOffers[0], productsCount: 1 }],
    })
    const single = buildPromoPosterPdfRequest('S01', [{
      ...readyRow(singleProductOffer),
      draft: applyPosterKind(createPosterDraft(singleProductOffer), singleProductOffer, 'multibuy'),
    }], { style: 'classic', size: 'A4', impose: false, today: '2026-09-19' })
    assert.equal(single.posters[0].mixAndMatch, false, '促销只含一个商品时不是 Mix & match')
    assert.equal(single.posters[0].unitPrice, 12.99)

    const withoutLogo = buildPromoPosterPdfRequest('S01', [special], {
      style: 'classic', size: 'A4', impose: false, showLogo: false, today: '2026-09-19',
    })
    assert.equal(withoutLogo.showLogo, false)
  })

  await test('日期、文件名与页数响应头解析', () => {
    assert.equal(toDateOnly('2026-09-19T00:00:00'), '2026-09-19')
    assert.equal(toDateOnly('2026-02-30T00:00:00'), undefined)
    assert.equal(toDateOnly(''), undefined)

    assert.equal(
      parsePosterPdfFileName("attachment; filename=HB-Posters-20260919-1530.pdf; filename*=UTF-8''HB-Posters-20260919-1530.pdf"),
      'HB-Posters-20260919-1530.pdf',
    )
    assert.equal(parsePosterPdfFileName('attachment; filename="a/b.pdf"'), 'a_b.pdf')
    assert.equal(parsePosterPdfFileName(null), null)

    assert.equal(parsePosterPageCount('3'), 3)
    assert.equal(parsePosterPageCount('0'), null)
    assert.equal(parsePosterPageCount('abc'), null)
    assert.equal(parsePosterPageCount(null), null)

    assert.equal(buildPosterPdfFallbackFileName(new Date(2026, 8, 9, 7, 5)), 'HB-Posters-20260909-0705.pdf')
  })

  await test('runWithConcurrency 不超过并发上限且处理全部任务', async () => {
    let active = 0
    let peak = 0
    const done: number[] = []
    await runWithConcurrency(Array.from({ length: 20 }, (_, index) => index), 6, async (item) => {
      active += 1
      peak = Math.max(peak, active)
      await new Promise((resolve) => setTimeout(resolve, item % 3))
      done.push(item)
      active -= 1
    })
    assert.equal(peak, 6)
    assert.equal(done.length, 20)
    assert.deepEqual([...done].sort((a, b) => a - b), Array.from({ length: 20 }, (_, index) => index))

    const controller = new AbortController()
    let started = 0
    await runWithConcurrency([1, 2, 3, 4, 5], 2, async () => {
      started += 1
      await Promise.resolve()
      controller.abort()
    }, controller.signal)
    assert.equal(started, 2, '已发出的 2 个任务照常结束，取消后不再启动新任务')
  })

  if (failures.length > 0) {
    console.error(`promoPosterLogic.test: ${failures.length} 项失败`)
    process.exit(1)
  }
  console.log('promoPosterLogic.test: ok')
}

void main()
