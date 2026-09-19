import assert from 'node:assert/strict'

import { buildAdminPurchaseSalesTabPath, resolveAdminPurchaseSalesTab } from './tabs'

const both = { canViewBatch: true, canViewStore: true }
assert.equal(resolveAdminPurchaseSalesTab(null, both), 'batch', '两个权限都有且未指定标签时默认批量货号销量')
assert.equal(resolveAdminPurchaseSalesTab('batch', both), 'batch')
assert.equal(resolveAdminPurchaseSalesTab('store', both), 'store', '?tab=store 进入分店进货销量分析')
assert.equal(resolveAdminPurchaseSalesTab('unknown', both), 'batch', '非法参数回落到默认标签')

const batchOnly = { canViewBatch: true, canViewStore: false }
assert.equal(resolveAdminPurchaseSalesTab('store', batchOnly), 'batch', '无分店页权限时 ?tab=store 不得打开分店进货销量分析')
assert.equal(resolveAdminPurchaseSalesTab(null, batchOnly), 'batch')

const storeOnly = { canViewBatch: false, canViewStore: true }
assert.equal(resolveAdminPurchaseSalesTab(null, storeOnly), 'store', '只有分店页权限时直接落到分店进货销量分析')
assert.equal(resolveAdminPurchaseSalesTab('batch', storeOnly), 'store', '无批量页权限时 ?tab=batch 不得打开批量货号销量')

assert.equal(resolveAdminPurchaseSalesTab('store', { canViewBatch: false, canViewStore: false }), null, '两个权限都没有时不返回任何标签')

assert.equal(buildAdminPurchaseSalesTabPath('store'), '/executive-sales-intelligence/purchase-sales-analysis?tab=store')
assert.equal(buildAdminPurchaseSalesTabPath('batch'), '/executive-sales-intelligence/purchase-sales-analysis?tab=batch')

console.log('adminPurchaseSalesTabs.test: ok')
