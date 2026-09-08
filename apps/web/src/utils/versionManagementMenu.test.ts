import assert from 'node:assert/strict'
import { buildAccess } from './access'
import { buildExpoRoleMenuPreview, buildExpoUserMenuPreview } from './expoRoleMenuPreview'

const names = ['app-downloads', 'wpf-versions']
for (const role of ['Admin', '管理员', 'SuperAdmin', '超级管理员', 'User', 'StoreManager', 'WarehouseManager']) {
  const access = buildAccess({ userGUID: 'test', username: 'test', email: '', storeNames: [], roleNames: [role], permissions: ['System.ViewAppDownloads', 'System.ManageAppDownloads'] })
  const preview = buildExpoRoleMenuPreview(access)
  for (const name of names) {
    const route = preview.allRoutes.find((item) => item.routeName === name)!
    assert.equal(route.visible, access.isAdmin)
    assert.equal(route.canAdd, false)
    assert.equal(route.canRemove, false)
    assert.equal(route.locked, true)
  }
}
for (const isSuperAdmin of [false, true]) {
  for (const implicitAllPermissions of [false, true]) {
    const preview = buildExpoUserMenuPreview({ inheritedPermissionCodes: ['System.ManageAppDownloads'], directPermissionCodes: ['System.ViewAppDownloads'], assignablePermissionCodes: ['System.ViewAppDownloads', 'System.ManageAppDownloads'], isSuperAdmin, implicitAllPermissions })
    for (const name of names) {
      const route = preview.allRoutes.find((item) => item.routeName === name)!
      assert.equal(Boolean(route?.visible), isSuperAdmin)
      assert.equal(Boolean(route?.canAdd), false)
      assert.equal(Boolean(route?.canRemove), false)
    }
  }
}
console.log('版本管理移动菜单预览管理员限制测试通过')
