import { readFileSync } from 'node:fs'
import enLocale from '../../../i18n/locales/en.json'
import zhLocale from '../../../i18n/locales/zh.json'
import { buildAccess } from '../../../utils/access'
import { buildWebRoleMenuPreview } from '../../../utils/webMenuPreview'
import type { CurrentUser } from '../../../types/auth'
import { P } from '../../../types/permissions'
import {
  buildLinklyCredentialPayload,
  buildSquareTokenPayload,
  createLinklyCredentialFormValues,
  createSquareTokenFormValues,
  getEnvironmentStatus,
  resolvePaymentTerminalSettingsErrorMessage,
} from './pageLogic'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
}

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}: expected ${String(expected)}, got ${String(actual)}`)
  }
}

function createCurrentUser(overrides: Partial<CurrentUser> = {}): CurrentUser {
  return {
    userGUID: 'payment-terminal-settings-user',
    username: 'tester',
    email: 'tester@example.com',
    permissions: [],
    roleNames: [],
    storeNames: [],
    ...overrides,
  }
}

const access = buildAccess(createCurrentUser({ permissions: [P.System.ManageSettings] }))
assertEqual(access.canManageSystemSettings, true, 'System.ManageSettings should unlock payment terminal settings')

const menu = buildWebRoleMenuPreview(access, (key) => key)
const paymentMenu = menu
  .find((node) => node.path === '/system')
  ?.children?.find((node) => node.path === '/system/payment-terminal-settings')

assert(paymentMenu, 'system menu should include payment terminal settings')
assertEqual(
  paymentMenu?.permissionCodes.join(','),
  P.System.ManageSettings,
  'payment terminal settings menu should use System.ManageSettings',
)

const squareForm = createSquareTokenFormValues()
assertEqual(squareForm.accessToken, '', 'Square token input should start empty')
assertEqual(squareForm.clearToken, false, 'Square clear switch should default false')

const keepSquare = buildSquareTokenPayload('Production', { accessToken: '   ', clearToken: false })
assertEqual(keepSquare.environment, 'Production', 'Square payload should include environment')
assert('accessToken' in keepSquare === false, 'blank Square token should be omitted to keep existing token')

const clearSquare = buildSquareTokenPayload('Sandbox', { accessToken: 'new-secret', clearToken: true })
assertEqual(clearSquare.clearToken, true, 'clear Square payload should set clearToken=true')
assert('accessToken' in clearSquare === false, 'clear Square payload should not send accessToken')

const saveSquare = buildSquareTokenPayload('Sandbox', { accessToken: ' sandbox-secret ', clearToken: false })
assertEqual(saveSquare.accessToken, 'sandbox-secret', 'Square token should be trimmed before submit')

const linklyForm = createLinklyCredentialFormValues({
  storeCode: '001',
  environment: 'Production',
  username: 'existing-user',
  hasPassword: true,
})
assertEqual(linklyForm.password, '', 'Linkly password input should not be hydrated')
assertEqual(linklyForm.username, 'existing-user', 'Linkly username should be hydrated')

const keepLinkly = buildLinklyCredentialPayload('001', 'Production', {
  username: ' new-user ',
  password: ' ',
  clearCredential: false,
})
assertEqual(keepLinkly.username, 'new-user', 'Linkly username should be trimmed')
assert('password' in keepLinkly === false, 'blank Linkly password should be omitted to keep existing password')

const clearLinkly = buildLinklyCredentialPayload('001', 'Sandbox', {
  username: 'ignored',
  password: 'ignored',
  clearCredential: true,
})
assertEqual(clearLinkly.clearCredential, true, 'clear Linkly payload should set clearCredential=true')
assert('password' in clearLinkly === false, 'clear Linkly payload should not send password')

const sandboxStatus = getEnvironmentStatus(
  [
    { environment: 'Production', configured: false, enabled: false },
    { environment: 'Sandbox', configured: true, enabled: true },
  ],
  'Sandbox',
)
assertEqual(sandboxStatus?.configured, true, 'environment status helper should select Sandbox')

assertEqual(
  resolvePaymentTerminalSettingsErrorMessage(new Error('backend detail'), 'fallback'),
  'backend detail',
  'error resolver should prefer backend detail',
)
assertEqual(
  resolvePaymentTerminalSettingsErrorMessage(new Error(''), 'fallback'),
  'fallback',
  'error resolver should fallback when message is blank',
)

assertEqual(zhLocale.menu.paymentTerminalSettings, '支付终端配置', 'Chinese menu text should exist')
assertEqual(enLocale.menu.paymentTerminalSettings, 'Payment Terminal Settings', 'English menu text should exist')
assertEqual(zhLocale.paymentTerminalSettings.squareTitle, 'Square Token', 'Chinese page text should exist')
assertEqual(enLocale.paymentTerminalSettings.linklyTitle, 'Linkly Cloud Credential', 'English page text should exist')

const routeSource = readFileSync('src/router/routes.tsx', 'utf8')
assert(routeSource.includes("path: '/system/payment-terminal-settings'"), 'route should include payment terminal path')
assert(routeSource.includes("title: 'menu.paymentTerminalSettings'"), 'route should include menu key')
assert(routeSource.includes("accessKey: 'canManageSystemSettings'"), 'route should use system settings access')

console.log('paymentTerminalSettings.logic.test: ok')
