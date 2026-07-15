import {
  deleteUserStorePosTerminalPermissions,
  getUserStorePosTerminalPermissions,
  updateUserStorePosTerminalPermissions,
} from './userService'

function assertEqual<T>(actual: T, expected: T, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}. Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function assertArrayEqual<T>(actual: T[], expected: T[], label: string) {
  const actualText = JSON.stringify(actual)
  const expectedText = JSON.stringify(expected)
  if (actualText !== expectedText) {
    throw new Error(`${label}. Expected: ${expectedText}, received: ${actualText}`)
  }
}

const originalFetch = globalThis.fetch
const calls: Array<{ method: string; pathname: string; body?: string }> = []
const responseData = {
  mode: 'Override',
  assignablePermissions: [
    {
      code: 'Permissions.PosTerminal.Sales.LineManualDiscount',
      name: '单行手动折扣',
      group: 'POS 销售',
      description: '收银端销售页 - 单行手动折扣',
    },
  ],
  inheritedPermissionCodes: [],
  overriddenPermissionCodes: ['Permissions.PosTerminal.Sales.LineManualDiscount'],
  grantedPermissionCodes: ['Permissions.PosTerminal.Sales.LineManualDiscount'],
  effectivePermissionCodes: ['Permissions.PosTerminal.Sales.LineManualDiscount'],
}

globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
  const requestUrl = new URL(String(input), 'http://localhost')
  calls.push({
    method: init?.method ?? 'GET',
    pathname: requestUrl.pathname,
    body: typeof init?.body === 'string' ? init.body : undefined,
  })
  return new Response(JSON.stringify({ success: true, data: responseData }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}) as typeof fetch

try {
  const userGuid = 'user-guid'
  const storeGuid = 'store-guid'
  const expectedPath = `/api/Users/guid/${userGuid}/stores/${storeGuid}/pos-terminal-permissions`

  const loaded = await getUserStorePosTerminalPermissions(userGuid, storeGuid)
  const saved = await updateUserStorePosTerminalPermissions(userGuid, storeGuid, {
    grantedPermissionCodes: ['Permissions.PosTerminal.Sales.LineManualDiscount'],
  })
  const restored = await deleteUserStorePosTerminalPermissions(userGuid, storeGuid)

  assertArrayEqual(
    calls.map((call) => `${call.method} ${call.pathname}`),
    [`GET ${expectedPath}`, `PUT ${expectedPath}`, `DELETE ${expectedPath}`],
    'POS terminal permission service should use the user and store scoped endpoint for all operations',
  )
  assertEqual(
    calls[1]?.body,
    JSON.stringify({ grantedPermissionCodes: ['Permissions.PosTerminal.Sales.LineManualDiscount'] }),
    'POS terminal permission PUT should send only grantedPermissionCodes',
  )
  assertEqual(loaded.mode, 'Override', 'GET should unwrap POS permission mode')
  assertEqual(
    loaded.assignablePermissions[0]?.code,
    'Permissions.PosTerminal.Sales.LineManualDiscount',
    'GET should preserve the backend permission code field',
  )
  assertEqual(
    loaded.assignablePermissions[0]?.name,
    '单行手动折扣',
    'GET should preserve the backend permission display name field',
  )
  assertArrayEqual(saved.effectivePermissionCodes, responseData.effectivePermissionCodes, 'PUT should unwrap effective permissions')
  assertEqual(restored.mode, 'Override', 'DELETE should unwrap restored permission state')
} finally {
  globalThis.fetch = originalFetch
}
