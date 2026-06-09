import { readFileSync } from 'node:fs'
import enLocale from '../../../i18n/locales/en.json'
import zhLocale from '../../../i18n/locales/zh.json'
import { buildAccess } from '../../../utils/access'
import { buildWebRoleMenuPreview } from '../../../utils/webMenuPreview'
import type { CurrentUser } from '../../../types/auth'
import { P } from '../../../types/permissions'
import {
  buildInvoiceEmailSettingsSavePayload,
  buildInvoiceEmailSettingsTestPayload,
  createInvoiceEmailSettingsFormValues,
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
    userGUID: 'invoice-settings-user',
    username: 'tester',
    email: 'tester@example.com',
    permissions: [],
    roleNames: [],
    storeNames: [],
    ...overrides,
  }
}

const permissionAccess = buildAccess(
  createCurrentUser({
    permissions: [P.System.ManageSettings],
  }),
)
assertEqual(permissionAccess.canManageSystemSettings, true, 'System.ManageSettings 应解锁系统设置管理访问')

const deniedAccess = buildAccess(createCurrentUser())
assertEqual(deniedAccess.canManageSystemSettings, false, '缺少 System.ManageSettings 时不应解锁系统设置管理访问')

const translate = (key: string, fallback?: string) => fallback ?? key
const visibleSystemMenu = buildWebRoleMenuPreview(permissionAccess, translate)
const invoiceEmailMenu = visibleSystemMenu
  .find((node) => node.path === '/system')
  ?.children?.find((node) => node.path === '/system/invoice-email-settings')

assert(invoiceEmailMenu, '拥有 System.ManageSettings 时系统菜单应显示发票邮箱配置')
assertEqual(
  invoiceEmailMenu?.permissionCodes.join(','),
  P.System.ManageSettings,
  '发票邮箱配置菜单应展示 System.ManageSettings 权限',
)

const hiddenSystemMenu = buildWebRoleMenuPreview(deniedAccess, translate, { includeHidden: true })
const hiddenInvoiceEmailMenu = hiddenSystemMenu
  .find((node) => node.path === '/system')
  ?.children?.find((node) => node.path === '/system/invoice-email-settings')

assertEqual(hiddenInvoiceEmailMenu?.visible, false, '缺少权限时发票邮箱配置菜单应保持隐藏')

assertEqual(zhLocale.menu.invoiceEmailSettings, '发票邮箱配置', '中文菜单文案应存在')
assertEqual(enLocale.menu.invoiceEmailSettings, 'Invoice Email Settings', '英文菜单文案应存在')
assertEqual(zhLocale.invoiceEmailSettings.testToEmail, '测试收件邮箱', '中文页面文案应存在')
assertEqual(enLocale.invoiceEmailSettings.testToEmail, 'Test recipient email', '英文页面文案应存在')

const formValues = createInvoiceEmailSettingsFormValues({
  host: 'smtp.initial.test',
  port: 465,
  useSsl: true,
  checkCertificateRevocation: true,
  username: 'initial-user',
  hasPassword: true,
  fromEmail: 'from@test.com',
  fromName: 'Initial Sender',
  maxAttachmentBytes: 2097152,
})

assertEqual(formValues.password, '', '初始化表单时不应回填密码明文')
assertEqual(formValues.clearPassword, false, '初始化表单时 clearPassword 应默认为 false')

const keepPasswordPayload = buildInvoiceEmailSettingsSavePayload({
  ...formValues,
  host: 'smtp.changed.test',
  clearPassword: false,
  password: '   ',
})

assert('password' in keepPasswordPayload === false, '空白密码保存时应省略 password 字段以保留原密码')
assertEqual(keepPasswordPayload.clearPassword, false, '保留原密码时 clearPassword 应为 false')

const clearPasswordPayload = buildInvoiceEmailSettingsSavePayload({
  ...formValues,
  clearPassword: true,
  password: 'new-secret',
})

assert('password' in clearPasswordPayload === false, '清空密码时不应同时发送 password')
assertEqual(clearPasswordPayload.clearPassword, true, '清空密码时应发送 clearPassword=REDACTED

const updatePasswordPayload = buildInvoiceEmailSettingsSavePayload({
  ...formValues,
  clearPassword: false,
  password: 'new-secret',
})

assertEqual(updatePasswordPayload.password, 'new-secret', '填写新密码时应提交 password')

const testPayload = buildInvoiceEmailSettingsTestPayload({
  ...formValues,
  host: 'smtp.changed.test',
  testToEmail: 'qa@test.com',
  password: 'temporary-secret',
})

assertEqual(testPayload.testToEmail, 'qa@test.com', '测试邮件 payload 应携带测试收件邮箱')
assertEqual(testPayload.password, 'temporary-secret', '测试邮件 payload 应允许携带当前输入的密码')

const routeSource = readFileSync('src/router/routes.tsx', 'utf8')
assert(
  routeSource.includes("path: '/system/invoice-email-settings'"),
  '路由源码中应包含发票邮箱配置路径，避免后续误删',
)
assert(
  routeSource.includes("title: 'menu.invoiceEmailSettings'"),
  '路由源码中应包含发票邮箱配置菜单标题 key',
)
assert(
  routeSource.includes("accessKey: 'canManageSystemSettings'"),
  '路由源码中应使用 canManageSystemSettings 控制发票邮箱配置访问',
)

console.log('invoiceEmailSettings.logic.test: ok')
