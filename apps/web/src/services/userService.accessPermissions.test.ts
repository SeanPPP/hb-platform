import assert from 'node:assert/strict'
import { getUserAccessPermissions } from './userService'

function assertEqual<T>(actual: T, expected: T, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}. Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

const originalFetch = globalThis.fetch
let requestedUrl = ''
let responseMode: 'success' | 'delegator-denied' = 'success'

globalThis.fetch = (async (input) => {
  requestedUrl = String(input)
  if (responseMode === 'delegator-denied') {
    return new Response(JSON.stringify({
      success: false,
      errorCode: 'ACCESS_DELEGATOR_DENIED',
      message: '当前账号不能分配员工访问权限',
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })
  }

  return new Response(JSON.stringify({
    success: true,
    data: {
      state: {
        userGuid: 'user-1',
        isSuperAdmin: false,
        implicitAllPermissions: false,
        inheritedPermissionCodes: ['Orders.View'],
        directPermissionCodes: ['Reports.Export'],
        effectivePermissionCodes: ['Orders.View', 'Reports.Export'],
        inheritedSources: [{ roleName: 'Order', permissionCodes: ['Orders.View'] }],
      },
      categories: [
        {
          category: 'orders',
          displayName: '订单',
          permissions: [],
        },
      ],
    },
  }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}) as typeof fetch

try {
  const result = await getUserAccessPermissions('user-1')
  const requestUrl = new URL(requestedUrl, 'http://localhost')

  assertEqual(
    requestUrl.pathname,
    '/api/Users/guid/user-1/access-permissions',
    '用户权限编辑应调用按操作者范围裁剪的目录接口',
  )
  assertEqual(result.state.directPermissionCodes[0], 'Reports.Export', '应解析用户直接权限状态')
  assertEqual(result.categories[0]?.category, 'orders', '应解析可分配权限分类')
  assertEqual(result.state.isSuperAdmin, false, '应保留超级管理员只读标识')

  responseMode = 'delegator-denied'
  await assert.rejects(
    () => getUserAccessPermissions('user-2'),
    /ACCESS_DELEGATOR_DENIED/,
    '业务失败响应必须转成异常，供抽屉显示不可用或错误状态',
  )
} finally {
  globalThis.fetch = originalFetch
}
