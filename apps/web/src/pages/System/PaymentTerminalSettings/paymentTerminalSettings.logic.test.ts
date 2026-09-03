import { readFileSync } from 'node:fs'
import enLocale from '../../../i18n/locales/en.json'
import zhLocale from '../../../i18n/locales/zh.json'
import { buildAccess } from '../../../utils/access'
import { buildWebRoleMenuPreview } from '../../../utils/webMenuPreview'
import type { CurrentUser } from '../../../types/auth'
import { P } from '../../../types/permissions'
import {
  buildLinklyCredentialPayload,
  buildCreateLinklyTerminalPayload,
  buildUpdateLinklyTerminalPayload,
  buildSquareTokenPayload,
  canActivateLinklyConfiguration,
  createLinklyCredentialFormValues,
  createLinklyTerminalFormValues,
  createSquareTokenFormValues,
  getEnvironmentStatus,
  getLinklyTerminalAssignmentOwner,
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

const terminalForm = createLinklyTerminalFormValues({
  terminalId: 'terminal-1',
  storeCode: '001',
  environment: 'Production',
  laneNo: 2,
  displayName: 'Back Counter',
  usernameMasked: '7457•••••002',
  hasPassword: true,
  pairingState: 'Ready',
  selectedDeviceCount: 1,
  updatedAtUtc: '2026-09-02T00:00:00Z',
})
assertEqual(terminalForm.laneNo, 2, 'terminal edit form should hydrate lane')
assertEqual(terminalForm.displayName, 'Back Counter', 'terminal edit form should hydrate display name')
assertEqual(terminalForm.username, '', 'terminal edit form must not hydrate masked username')
assertEqual(terminalForm.password, '', 'terminal edit form must not hydrate password')

const createTerminal = buildCreateLinklyTerminalPayload('001', 'Production', {
  laneNo: 3,
  displayName: ' Side Counter ',
  username: ' test-user-003 ',
  password: ' terminal-secret ',
})
assertEqual(createTerminal.displayName, 'Side Counter', 'terminal display name should be trimmed')
assertEqual(createTerminal.username, 'test-user-003', 'terminal username should be trimmed')
assertEqual(createTerminal.password, 'terminal-secret', 'terminal password should be trimmed')

const updateTerminal = buildUpdateLinklyTerminalPayload('001', 'Sandbox', {
  laneNo: 3,
  displayName: 'Side Counter',
  username: ' ',
  password: ' ',
})
assert('username' in updateTerminal === false, 'blank edit username should preserve server credential')
assert('password' in updateTerminal === false, 'blank edit password should preserve server credential')

const activationBase = {
  storeCode: '001',
  environment: 'Production' as const,
  mode: 'Draft' as const,
  terminals: [{
    terminalId: 'terminal-1',
    storeCode: '001',
    environment: 'Production' as const,
    laneNo: 1,
    displayName: 'Front Counter',
    usernameMasked: '7457•••••001',
    hasPassword: true,
    pairingState: 'Ready' as const,
    selectedDeviceCount: 1,
    updatedAtUtc: '2026-09-02T00:00:00Z',
  }],
  devices: [{
    deviceCode: 'POS-01',
    deviceSystem: 'Windows',
    enabled: true,
    deviceMissing: false,
    terminalId: 'terminal-1',
    revision: 1,
  }],
}
assertEqual(canActivateLinklyConfiguration(activationBase), true, 'ready terminal and complete selections can activate')
assertEqual(
  canActivateLinklyConfiguration({ ...activationBase, devices: [{ ...activationBase.devices[0], terminalId: null }] }),
  false,
  'enabled device without selection blocks activation',
)
assertEqual(
  canActivateLinklyConfiguration({
    ...activationBase,
    devices: [
      activationBase.devices[0],
      {
        ...activationBase.devices[0],
        deviceCode: 'POS-DISABLED',
        enabled: false,
        deviceMissing: false,
        terminalId: null,
      },
    ],
  }),
  true,
  'disabled device without selection should not block activation',
)
assertEqual(
  canActivateLinklyConfiguration({
    ...activationBase,
    devices: [
      activationBase.devices[0],
      {
        ...activationBase.devices[0],
        deviceCode: 'POS-02',
      },
    ],
  }),
  false,
  'two enabled POS devices cannot activate with the same terminal',
)
assertEqual(
  getLinklyTerminalAssignmentOwner(activationBase, 'terminal-1', 'POS-01'),
  null,
  'the current POS keeps its own terminal option',
)
assertEqual(
  getLinklyTerminalAssignmentOwner(activationBase, 'terminal-1', 'POS-02'),
  'POS-01',
  'another POS sees the terminal owner',
)
assertEqual(
  getLinklyTerminalAssignmentOwner(activationBase, 'terminal-unassigned', 'POS-02'),
  null,
  'an unassigned terminal stays available',
)

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
