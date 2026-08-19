import assert from 'node:assert/strict'
import {
  compareSemver,
  detectBrowser,
  EXTENSION_MESSAGE_SOURCE,
  OPEN_MESSAGE_TYPE,
  OPEN_RESULT_MESSAGE_TYPE,
  PING_MESSAGE_TYPE,
  PLATFORM_MESSAGE_SOURCE,
  isMobileBrowser,
  resolveExtensionVersionStatus,
  shouldShowSupplierOrderingEntry,
  STATUS_MESSAGE_TYPE,
  type ExtensionStatusMessageReason,
  type ExtensionStatusMessageValidation,
  validateExtensionStatusMessage,
  validateExtensionOpenResultMessage,
} from './supplierOrderingExtensionLogic'

function assertInvalid(result: ExtensionStatusMessageValidation, reason: ExtensionStatusMessageReason) {
  assert.equal(result.ok, false)
  if (result.ok === false) {
    assert.equal(result.reason, reason)
  }
}

// 消息契约常量：避免页面与扩展之间出现大小写或前缀漂移。
assert.equal(PLATFORM_MESSAGE_SOURCE, 'hb-platform')
assert.equal(EXTENSION_MESSAGE_SOURCE, 'hb-supplier-ordering-extension')
assert.equal(PING_MESSAGE_TYPE, 'HB_SUPPLIER_ASSISTANT_PING')
assert.equal(STATUS_MESSAGE_TYPE, 'HB_SUPPLIER_ASSISTANT_STATUS')
assert.equal(OPEN_MESSAGE_TYPE, 'HB_SUPPLIER_ASSISTANT_OPEN')
assert.equal(OPEN_RESULT_MESSAGE_TYPE, 'HB_SUPPLIER_ASSISTANT_OPEN_RESULT')

// 精确 /shop 才显示，任意子路由不显示。
assert.equal(shouldShowSupplierOrderingEntry('/shop'), true, 'exact /shop 必须显示')
assert.equal(shouldShowSupplierOrderingEntry('/shop/best-sellers'), false, '/shop/best-sellers 不得显示')
assert.equal(shouldShowSupplierOrderingEntry('/shop/orders'), false, '/shop/orders 不得显示')
assert.equal(shouldShowSupplierOrderingEntry('/shop/orders/1'), false, '/shop/orders/:id 不得显示')
assert.equal(shouldShowSupplierOrderingEntry('/shop/'), false, '尾斜杠不得显示')
assert.equal(shouldShowSupplierOrderingEntry('/shop-preorders'), false, '前缀相似路径不得显示')

// 纯 semver 数值比较，不能退化成字符串比较。
assert.equal(compareSemver('1.2.3', '1.2.3'), 0)
assert.equal(compareSemver('1.2.4', '1.2.3'), 1)
assert.equal(compareSemver('1.2.3', '1.2.4'), -1)
assert.equal(compareSemver('2.0.0', '10.0.0'), -1, '2 < 10 必须按数值比较')
assert.equal(compareSemver('1.0.0', '1.0'), 0, '缺省 patch 视为 0')
assert.equal(compareSemver('1.0.0-beta', '1.0.0'), -1, '预发布版本低于正式版本')
assert.equal(compareSemver('1.0.0', '1.0.0-beta'), 1)

// 版本状态：低于 minimum 强制，低于 latest 可选，否则当前。
assert.equal(resolveExtensionVersionStatus('1.0.0', '1.1.0', '1.2.0'), 'forced')
assert.equal(resolveExtensionVersionStatus('1.1.0', '1.1.0', '1.2.0'), 'optional')
assert.equal(resolveExtensionVersionStatus('1.1.9', '1.1.0', '1.2.0'), 'optional')
assert.equal(resolveExtensionVersionStatus('1.2.0', '1.1.0', '1.2.0'), 'current')
assert.equal(resolveExtensionVersionStatus('1.3.0', '1.1.0', '1.2.0'), 'current')

// 浏览器识别：Edge 的 UA 同时含 Chrome，必须先识别 Edg/。
assert.equal(detectBrowser('Mozilla/5.0 AppleWebKit/537.36 Chrome/120 Safari/537.36 Edg/120'), 'edge')
assert.equal(detectBrowser('Mozilla/5.0 AppleWebKit/537.36 Chrome/120 Safari/537.36'), 'chrome')
assert.equal(detectBrowser('Mozilla/5.0 (Macintosh) AppleWebKit/605.1.15 Version/17 Safari/605.1.15'), 'other')
assert.equal(isMobileBrowser('Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X)'), true)
assert.equal(isMobileBrowser('Mozilla/5.0 (Linux; Android 15; Pixel 9) Mobile'), true)
assert.equal(isMobileBrowser('Mozilla/5.0 (Macintosh) Chrome/120', 0, 'MacIntel'), false)
assert.equal(isMobileBrowser('Mozilla/5.0 (Macintosh) Version/18 Safari/605.1.15', 5, 'MacIntel'), true)

const validMessage = {
  source: EXTENSION_MESSAGE_SOURCE,
  type: STATUS_MESSAGE_TYPE,
  nonce: 'nonce-1',
  installed: true,
  version: '1.2.3',
  browser: 'chrome',
}
const validContext = {
  eventSource: 'window',
  windowObject: 'window',
  messageOrigin: 'https://hotbargain.example',
  windowOrigin: 'https://hotbargain.example',
  expectedNonce: 'nonce-1',
}

// 合法消息：严格校验通过并返回版本与浏览器。
assert.deepEqual(
  validateExtensionStatusMessage(validMessage, validContext),
  { ok: true, version: '1.2.3', browser: 'chrome' },
  '合法 STATUS 必须通过校验',
)

// 来源非当前 window。
const badSource = validateExtensionStatusMessage(validMessage, {
  ...validContext,
  eventSource: 'other-window',
})
assertInvalid(badSource, 'source')

// 来源 origin 与当前页面不一致。
const badOrigin = validateExtensionStatusMessage(validMessage, {
  ...validContext,
  messageOrigin: 'https://evil.example',
})
assertInvalid(badOrigin, 'origin')

// nonce 不匹配。
const badNonce = validateExtensionStatusMessage(validMessage, {
  ...validContext,
  expectedNonce: 'nonce-2',
})
assertInvalid(badNonce, 'nonce')

// 消息 type/source 不匹配。
assertInvalid(validateExtensionStatusMessage({ ...validMessage, type: 'OTHER' }, validContext), 'type')
assertInvalid(validateExtensionStatusMessage({ ...validMessage, source: 'evil' }, validContext), 'type')

// installed 非 true 视为无效。
assertInvalid(validateExtensionStatusMessage({ ...validMessage, installed: false }, validContext), 'installed')

// 缺少 version/browser 字段视为无效。
const { version: _version, ...noVersion } = validMessage
assertInvalid(validateExtensionStatusMessage(noVersion, validContext), 'fields')
assertInvalid(validateExtensionStatusMessage({ ...validMessage, browser: 'firefox' }, validContext), 'fields')

// 非对象载荷直接拒绝。
assertInvalid(validateExtensionStatusMessage(null, validContext), 'fields')
assertInvalid(validateExtensionStatusMessage('garbage', validContext), 'fields')

const openResult = {
  source: EXTENSION_MESSAGE_SOURCE,
  type: OPEN_RESULT_MESSAGE_TYPE,
  nonce: 'open-nonce',
  installed: true,
  version: '1.2.3',
  browser: 'chrome',
  ok: false,
  error: 'blocked',
}
assert.deepEqual(
  validateExtensionOpenResultMessage(openResult, {
    ...validContext,
    expectedNonce: 'open-nonce',
  }),
  { ok: true, opened: false, error: 'blocked' },
)
assert.equal(
  validateExtensionOpenResultMessage(
    { ...openResult, nonce: 'wrong' },
    { ...validContext, expectedNonce: 'open-nonce' },
  ).ok,
  false,
)

console.log('supplierOrderingExtensionLogic.test: ok')
