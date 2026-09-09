import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import type { AccessControl, CurrentUser } from '../types/auth'
import { buildAccess } from './access'
import { applyRolePermissionMutation } from './roleMenuPreview'
import { buildWebRoleMenuPreview } from './webMenuPreview'
import { getDefaultWebPath, resolveAuthorizedWebTarget } from './webPortalAccess'

const pages: Array<[string, keyof AccessControl, string]> = [
  ['overview', 'canViewSalesData', 'SalesDashboard.SalesData.View'],
  ['sales-detail-v2', 'canViewSalesDetail', 'SalesDashboard.SalesDetail.View'],
  ['compact-sales-board', 'canViewCompactSalesBoard', 'SalesDashboard.CompactBoard.View'],
  ['product-movement-report', 'canViewProductMovementReport', 'SalesDashboard.ProductMovement.View'],
  ['warehouse-product-flow-analysis', 'canViewWarehouseProductFlowAnalysis', 'SalesDashboard.WarehouseFlow.View'],
  ['local-product-sales-analysis', 'canViewLocalProductSalesAnalysis', 'SalesDashboard.LocalProductAnalysis.View'],
  ['purchase-amount-dashboard', 'canViewPurchaseAmountDashboard', 'SalesDashboard.PurchaseAmount.View'],
]
const prefix = '/executive-sales-intelligence'
const user = (permissions: string[], roleNames: string[] = []): CurrentUser => ({
  userGUID: 'sales-page-permissions-user', username: 'test', email: '', storeNames: [],
  permissions, exactPermissions: permissions, roleNames,
})
const preview = (access: AccessControl, permissions: string[] = []) => buildWebRoleMenuPreview(
  access, key => key, { includeHidden: true, explicitPermissionCodes: permissions },
).find(node => node.path === prefix)

for (const role of [[], ['WarehouseManager'], ['WarehouseStaff']]) {
  for (const [path, key, permission] of pages) {
    const access = buildAccess(user([permission], role))
    assert.equal(access[key], true, `${role}/${permission} 应放开对应页面`)
    assert.equal(access.canViewSalesIntelligence, true, '任一页面权限应显示父菜单')
    assert.equal(access.canAccessAdminShell, true, '单页授权应允许进入后台')
    assert.equal(getDefaultWebPath(access), `${prefix}/${path}`, '单页授权应直接进入该页')
    const nodes = preview(access, [permission])?.children ?? []
    assert.equal(nodes.filter(node => node.visible).length, 1, '只应显示一个已授权页面')
    for (const [otherPath, otherKey] of pages) {
      assert.equal(access[otherKey], otherKey === key, '不同页面权限不能互相放行')
      assert.equal(resolveAuthorizedWebTarget(`${prefix}/${otherPath}`, access),
        otherKey === key ? `${prefix}/${otherPath}` : undefined, '历史地址也应执行单页权限')
    }
    assert.equal(resolveAuthorizedWebTarget('/pos-admin/local-supplier-invoices', access), undefined,
      '查看看板不应同时授予进货单业务权限')
    const node = nodes.find(item => item.path === `${prefix}/${path}`)!
    assert.deepEqual(node.permissionCodes, [permission])
    assert.deepEqual(node.edit.removePermissionCodes, [permission], '移除页面只撤销本页权限')
  }
}

for (const permissions of [[], ['Reports.View'], ['Reports.ProductMovement.View'], ['LocalPurchase.View'], ['LocalInvocie.View']]) {
  const access = buildAccess(user(permissions))
  assert.equal(access.canViewSalesIntelligence, false, '旧业务权限不能隐式授予销售看板页面')
  for (const [, key] of pages) assert.equal(access[key], false)
}

for (const role of ['Admin', '管理员', 'SuperAdmin', '超级管理员']) {
  const access = buildAccess(user([], [role]))
  assert.equal(preview(access)?.children?.filter(node => node.visible).length, 7, '管理员默认显示全部七页')
  for (const [path, key] of pages) {
    assert.equal(access[key], true)
    assert.equal(resolveAuthorizedWebTarget(`${prefix}/${path}`, access), `${prefix}/${path}`)
  }
}

const allCodes = pages.map(([, , code]) => code)
const allNodes = preview(buildAccess(user(allCodes)), allCodes)!.children!
for (const [path, key, code] of pages) {
  const node = allNodes.find(item => item.path === `${prefix}/${path}`)!
  const remaining = applyRolePermissionMutation({ currentPermissionCodes: allCodes,
    removePermissionCodes: node.edit.removePermissionCodes })
  const access = buildAccess(user(remaining))
  assert.equal(access[key], false, '撤销一页后该页立即不满足前端权限')
  assert.equal(preview(access)?.children?.filter(item => item.visible).length, 6, '其余六页应保持可见')
  const hidden = preview(buildAccess(user([])))!.children!.find(item => item.path === `${prefix}/${path}`)!
  assert.deepEqual(hidden.edit.addPermissionCodes, [code], '新增看板页只授予该页，不额外授予工作台')
}

const routes = readFileSync('src/router/routes.tsx', 'utf8')
for (const [path, key] of pages) {
  const start = routes.indexOf(`path: '${prefix}/${path}'`)
  const next = routes.indexOf('element:', start)
  assert.ok(start >= 0 && routes.slice(start, next).includes(`accessKey: '${key}'`),
    `${path} 的实际路由必须使用单页权限`)
}
const aliasAccess = buildAccess(user(['SalesDashboard.WarehouseFlow.View']))
assert.equal(resolveAuthorizedWebTarget(`${prefix}/product-sales-analysis`, aliasAccess), `${prefix}/product-sales-analysis`)
assert.equal(buildAccess(null).canViewSalesIntelligence, false)
console.log('销售看板七页独立授权、菜单、跳转、撤销及管理员默认权限测试通过')
