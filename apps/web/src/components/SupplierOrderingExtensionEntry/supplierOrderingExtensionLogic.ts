// 供应商下单助手浏览器扩展的纯逻辑：版本比较、路由判定、浏览器识别与消息握手校验。
// 与 React/DOM 解耦，便于在 Node 环境做单元测试。

export const PLATFORM_MESSAGE_SOURCE = 'hb-platform'
export const EXTENSION_MESSAGE_SOURCE = 'hb-supplier-ordering-extension'
export const PING_MESSAGE_TYPE = 'HB_SUPPLIER_ASSISTANT_PING'
export const STATUS_MESSAGE_TYPE = 'HB_SUPPLIER_ASSISTANT_STATUS'
export const OPEN_MESSAGE_TYPE = 'HB_SUPPLIER_ASSISTANT_OPEN'
export const OPEN_RESULT_MESSAGE_TYPE = 'HB_SUPPLIER_ASSISTANT_OPEN_RESULT'

export type ExtensionVersionStatus = 'forced' | 'optional' | 'current'
export type DetectedBrowser = 'edge' | 'chrome' | 'safari' | 'other'
export type ExtensionInstallExperience =
  | 'desktop-edge'
  | 'desktop-chrome'
  | 'desktop-safari-unsupported'
  | 'desktop-unsupported'
  | 'ios-safari'
  | 'ios-unsupported'
  | 'android-unsupported'

export interface BrowserExtensionRelease {
  latestVersion: string
  minimumVersion: string
  chromeStoreUrl: string
  edgeStoreUrl: string
  safariStoreUrl: string
  releaseNotes: {
    zh: string
    en: string
  }
}

interface SemverParts {
  major: number
  minor: number
  patch: number
  prerelease: string[]
}

// 仅显示在精确的 /shop，子路由（如 /shop/orders）不显示。
export function shouldShowSupplierOrderingEntry(pathname: string): boolean {
  return pathname === '/shop'
}

function parseSemver(input: string): SemverParts | null {
  const raw = String(input ?? '').trim()
  if (!raw) {
    return null
  }

  // 兼容可选的 v 前缀与 build metadata（+build），再拆分 prerelease（-beta）。
  const strippedPrefix = /^[vV]/.test(raw) ? raw.slice(1) : raw
  const withoutBuild = strippedPrefix.split('+')[0]
  const dashIndex = withoutBuild.indexOf('-')
  const core = dashIndex === -1 ? withoutBuild : withoutBuild.slice(0, dashIndex)
  const prereleaseRaw = dashIndex === -1 ? '' : withoutBuild.slice(dashIndex + 1)
  const coreParts = core.split('.')

  if (coreParts.length === 0 || coreParts.length > 3) {
    return null
  }

  const numbers: number[] = []
  for (const part of coreParts) {
    if (!/^\d+$/.test(part)) {
      return null
    }
    numbers.push(Number(part))
  }

  const prerelease = prereleaseRaw ? prereleaseRaw.split('.') : []
  if (prerelease.some((id) => !/^[0-9A-Za-z-]+$/.test(id))) {
    return null
  }

  return {
    major: numbers[0] ?? 0,
    minor: numbers[1] ?? 0,
    patch: numbers[2] ?? 0,
    prerelease,
  }
}

function compareIdentifier(left: string, right: string): number {
  const leftNumeric = /^\d+$/.test(left)
  const rightNumeric = /^\d+$/.test(right)
  if (leftNumeric && rightNumeric) {
    return Number(left) - Number(right)
  }
  // semver：纯数字标识符优先级低于字母数字标识符。
  if (leftNumeric) {
    return -1
  }
  if (rightNumeric) {
    return 1
  }
  return left < right ? -1 : left > right ? 1 : 0
}

// 纯 semver 数值比较；非法版本视为低于任何合法版本。
export function compareSemver(leftInput: string, rightInput: string): number {
  const left = parseSemver(leftInput)
  const right = parseSemver(rightInput)
  if (!left && !right) {
    return 0
  }
  if (!left) {
    return -1
  }
  if (!right) {
    return 1
  }

  if (left.major !== right.major) {
    return left.major < right.major ? -1 : 1
  }
  if (left.minor !== right.minor) {
    return left.minor < right.minor ? -1 : 1
  }
  if (left.patch !== right.patch) {
    return left.patch < right.patch ? -1 : 1
  }

  if (left.prerelease.length === 0 && right.prerelease.length === 0) {
    return 0
  }
  if (left.prerelease.length === 0) {
    return 1
  }
  if (right.prerelease.length === 0) {
    return -1
  }

  const length = Math.max(left.prerelease.length, right.prerelease.length)
  for (let i = 0; i < length; i += 1) {
    if (i >= left.prerelease.length) {
      return -1
    }
    if (i >= right.prerelease.length) {
      return 1
    }
    const result = compareIdentifier(left.prerelease[i], right.prerelease[i])
    if (result !== 0) {
      return result
    }
  }

  return 0
}

export function resolveExtensionVersionStatus(
  installedVersion: string,
  minimumVersion: string,
  latestVersion: string,
): ExtensionVersionStatus {
  if (compareSemver(installedVersion, minimumVersion) < 0) {
    return 'forced'
  }
  if (compareSemver(installedVersion, latestVersion) < 0) {
    return 'optional'
  }
  return 'current'
}

export function detectBrowser(userAgent: string): DetectedBrowser {
  const ua = String(userAgent ?? '').toLowerCase()
  // Edge 的 UA 同时包含 Chrome，必须优先识别 Edg/。
  if (ua.includes('edg/')) {
    return 'edge'
  }
  // 已知的其他 Chromium 浏览器不得误用 Chrome 的受支持安装入口。
  if (/opr\/|opera\/|vivaldi\/|yabrowser\/|samsungbrowser\//.test(ua)) {
    return 'other'
  }
  if (ua.includes('chrome/')) {
    return 'chrome'
  }
  if (ua.includes('version/') && ua.includes('safari/')) {
    return 'safari'
  }
  return 'other'
}

export function isMobileBrowser(
  userAgent: string,
  maxTouchPoints = 0,
  platform = '',
): boolean {
  const ua = String(userAgent ?? '')
  if (/Android|iPhone|iPad|iPod|Mobile/i.test(ua)) {
    return true
  }

  // iPadOS 桌面模式会报告 Macintosh/MacIntel，只能结合触点数量识别。
  return platform === 'MacIntel' && maxTouchPoints > 1
}

export function resolveExtensionInstallExperience(
  userAgent: string,
  maxTouchPoints = 0,
  platform = '',
): ExtensionInstallExperience {
  const ua = String(userAgent ?? '')
  const isAppleMobile = /iPhone|iPad|iPod/i.test(ua)
    || (platform === 'MacIntel' && maxTouchPoints > 1)

  // iPadOS 桌面模式会伪装成 Macintosh，因此必须先于桌面 Safari 判断。
  if (isAppleMobile) {
    // UA 可被伪装；这里仅用于安装体验分流，不作为权限或安全边界。
    const isAlternateIosBrowser = /CriOS\/|EdgiOS\/|FxiOS\/|OPiOS\/|DuckDuckGo\/|Ddg\/|GSA\/|FBAN\/|FBAV\/|Instagram\/|Line\/|MicroMessenger\/|YaBrowser\/|Coast\//i.test(ua)
    const isSafari = /Version\//i.test(ua) && /Safari\//i.test(ua) && !isAlternateIosBrowser
    return isSafari ? 'ios-safari' : 'ios-unsupported'
  }

  if (/Android|Mobile/i.test(ua)) {
    return 'android-unsupported'
  }

  const browser = detectBrowser(ua)
  if (browser === 'edge') {
    return 'desktop-edge'
  }
  if (browser === 'chrome') {
    return 'desktop-chrome'
  }
  if (browser === 'safari') {
    return 'desktop-safari-unsupported'
  }
  return 'desktop-unsupported'
}

export type ExtensionStatusMessageReason =
  | 'source'
  | 'origin'
  | 'type'
  | 'nonce'
  | 'installed'
  | 'fields'

export type ExtensionStatusMessageValidation =
  | { ok: true; version: string; browser: string }
  | { ok: false; reason: ExtensionStatusMessageReason }

export interface ExtensionStatusMessageContext {
  // 实际收到的 event.source 与页面 window，二者必须同一引用。
  eventSource: unknown
  windowObject: unknown
  // 实际收到的 event.origin 与 window.location.origin。
  messageOrigin: unknown
  windowOrigin: unknown
  expectedNonce: string
}

export type ExtensionOpenResultMessageValidation =
  | { ok: true; opened: boolean; error?: string }
  | { ok: false; reason: ExtensionStatusMessageReason }

// 严格校验扩展回包：来源窗口、origin、nonce、消息类型与载荷均需匹配。
export function validateExtensionStatusMessage(
  message: unknown,
  context: ExtensionStatusMessageContext,
): ExtensionStatusMessageValidation {
  if (typeof message !== 'object' || message === null) {
    return { ok: false, reason: 'fields' }
  }

  const candidate = message as Record<string, unknown>

  if (context.eventSource !== context.windowObject) {
    return { ok: false, reason: 'source' }
  }
  if (context.messageOrigin !== context.windowOrigin) {
    return { ok: false, reason: 'origin' }
  }
  if (candidate.source !== EXTENSION_MESSAGE_SOURCE || candidate.type !== STATUS_MESSAGE_TYPE) {
    return { ok: false, reason: 'type' }
  }
  if (candidate.nonce !== context.expectedNonce) {
    return { ok: false, reason: 'nonce' }
  }
  if (candidate.installed !== true) {
    return { ok: false, reason: 'installed' }
  }
  if (
    typeof candidate.version !== 'string'
    || candidate.version === ''
    || (
      candidate.browser !== 'chrome'
      && candidate.browser !== 'edge'
      && candidate.browser !== 'safari'
    )
  ) {
    return { ok: false, reason: 'fields' }
  }

  return { ok: true, version: candidate.version, browser: candidate.browser }
}

export function validateExtensionOpenResultMessage(
  message: unknown,
  context: ExtensionStatusMessageContext,
): ExtensionOpenResultMessageValidation {
  if (typeof message !== 'object' || message === null) {
    return { ok: false, reason: 'fields' }
  }

  const candidate = message as Record<string, unknown>
  if (context.eventSource !== context.windowObject) {
    return { ok: false, reason: 'source' }
  }
  if (context.messageOrigin !== context.windowOrigin) {
    return { ok: false, reason: 'origin' }
  }
  if (candidate.source !== EXTENSION_MESSAGE_SOURCE || candidate.type !== OPEN_RESULT_MESSAGE_TYPE) {
    return { ok: false, reason: 'type' }
  }
  if (candidate.nonce !== context.expectedNonce) {
    return { ok: false, reason: 'nonce' }
  }
  if (
    candidate.installed !== true
    || typeof candidate.ok !== 'boolean'
    || (candidate.error != null && typeof candidate.error !== 'string')
  ) {
    return { ok: false, reason: 'fields' }
  }

  return {
    ok: true,
    opened: candidate.ok,
    ...(typeof candidate.error === 'string' && candidate.error ? { error: candidate.error } : {}),
  }
}

export function createNonce(): string {
  const cryptoApi = globalThis.crypto
  if (cryptoApi && typeof cryptoApi.randomUUID === 'function') {
    return cryptoApi.randomUUID()
  }
  if (cryptoApi && typeof cryptoApi.getRandomValues === 'function') {
    const bytes = cryptoApi.getRandomValues(new Uint8Array(16))
    return Array.from(bytes, (value) => value.toString(16).padStart(2, '0')).join('')
  }

  throw new Error('当前浏览器不支持安全随机数，无法检测订货助手。')
}
