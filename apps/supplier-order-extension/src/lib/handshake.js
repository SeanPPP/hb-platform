// /shop bridge 握手：nonce 格式、浏览器识别、消息校验与响应构造
export const PLATFORM_MESSAGE_SOURCE = 'hb-platform';
export const EXTENSION_MESSAGE_SOURCE = 'hb-supplier-ordering-extension';
export const PING_MESSAGE_TYPE = 'HB_SUPPLIER_ASSISTANT_PING';
export const OPEN_MESSAGE_TYPE = 'HB_SUPPLIER_ASSISTANT_OPEN';
export const STATUS_MESSAGE_TYPE = 'HB_SUPPLIER_ASSISTANT_STATUS';
export const OPEN_RESULT_MESSAGE_TYPE = 'HB_SUPPLIER_ASSISTANT_OPEN_RESULT';
export const NONCE_PATTERN = /^[A-Za-z0-9_-]{16,64}$/;

export function isValidNonce(nonce) {
  return typeof nonce === 'string' && NONCE_PATTERN.test(nonce);
}

function bytesToBase64Url(bytes) {
  let binary = '';
  for (let i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]);
  if (typeof btoa === 'function') {
    return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  }
  return Buffer.from(bytes).toString('base64url');
}

export function generateNonce(bytes = 32) {
  const arr = new Uint8Array(bytes);
  const c = globalThis.crypto;
  if (c && typeof c.getRandomValues === 'function') {
    c.getRandomValues(arr);
  } else {
    for (let i = 0; i < bytes; i++) arr[i] = Math.floor(Math.random() * 256);
  }
  return bytesToBase64Url(arr);
}

export function detectBrowser(userAgent) {
  const ua = String(userAgent || '');
  if (/Edg\//.test(ua)) return 'edge';
  if (/Chrome\//.test(ua)) return 'chrome';
  return 'unknown';
}

// 校验页面 postMessage：source/window、origin、类型与 nonce 格式
export function validateBridgeMessage({ source, sourceWindow, origin, expectedOrigin, message }) {
  if (source !== sourceWindow) return { ok: false, reason: 'invalid-source' };
  if (origin !== expectedOrigin) return { ok: false, reason: 'invalid-origin' };
  if (!message || typeof message !== 'object') return { ok: false, reason: 'invalid-message' };
  if (message.source !== PLATFORM_MESSAGE_SOURCE) return { ok: false, reason: 'invalid-message-source' };
  if (message.type !== PING_MESSAGE_TYPE && message.type !== OPEN_MESSAGE_TYPE) {
    return { ok: false, reason: 'invalid-type' };
  }
  if (!isValidNonce(message.nonce)) return { ok: false, reason: 'invalid-nonce' };
  return { ok: true };
}

// 仅返回 installed/version/browser/nonce，绝不带账户、令牌或销售数据
export function buildBridgeResponse({ kind, nonce, version, browser, installed = true, ok, error }) {
  const resp = {
    source: EXTENSION_MESSAGE_SOURCE,
    type: kind === 'OPEN_RESULT' ? OPEN_RESULT_MESSAGE_TYPE : STATUS_MESSAGE_TYPE,
    nonce,
    installed,
    version,
    browser,
  };
  if (kind === 'OPEN_RESULT') {
    resp.ok = ok === true;
    if (error) resp.error = error;
  }
  return resp;
}
