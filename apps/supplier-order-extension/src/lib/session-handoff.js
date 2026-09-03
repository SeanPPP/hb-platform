// 网站会话授权交接的纯逻辑；具体浏览器 I/O 由 content script 与 service worker 注入。
export const WEBSITE_SESSION_CLIENT_ID = 'hb-supplier-order';

function base64Url(bytes) {
  let binary = '';
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary)
    .replaceAll('+', '-')
    .replaceAll('/', '_')
    .replace(/=+$/u, '');
}

export async function createPkceMaterial({ cryptoImpl = globalThis.crypto } = {}) {
  if (!cryptoImpl?.getRandomValues || !cryptoImpl?.subtle?.digest) {
    throw new Error('当前浏览器不支持安全的网站会话授权');
  }

  const verifierBytes = cryptoImpl.getRandomValues(new Uint8Array(32));
  const stateBytes = cryptoImpl.getRandomValues(new Uint8Array(32));
  const codeVerifier = base64Url(verifierBytes);
  const digest = await cryptoImpl.subtle.digest(
    'SHA-256',
    new TextEncoder().encode(codeVerifier),
  );

  return {
    codeVerifier,
    codeChallenge: base64Url(new Uint8Array(digest)),
    state: base64Url(stateBytes),
  };
}

function parseOrigin(value) {
  try {
    const url = new URL(value);
    if (url.username || url.password) return null;
    return url.origin;
  } catch {
    return null;
  }
}

export function validateWebsiteSessionContext({
  pageUrl,
  webOrigin,
  apiOrigin,
  isTopLevel,
}) {
  if (!isTopLevel) return { ok: false, reason: 'NOT_TOP_LEVEL' };

  let page;
  try {
    page = new URL(pageUrl);
  } catch {
    return { ok: false, reason: 'INVALID_PAGE_URL' };
  }

  const trustedWebOrigin = parseOrigin(webOrigin);
  const trustedApiOrigin = parseOrigin(apiOrigin);
  if (!trustedWebOrigin || page.origin !== trustedWebOrigin) {
    return { ok: false, reason: 'UNTRUSTED_PAGE' };
  }
  if (page.pathname !== '/shop') return { ok: false, reason: 'NOT_SHOP_PAGE' };
  if (!trustedApiOrigin || trustedApiOrigin !== page.origin) {
    return { ok: false, reason: 'API_ORIGIN_MISMATCH' };
  }

  return { ok: true, origin: page.origin };
}

function failure(body, fallbackReason) {
  return {
    ok: false,
    reason: body?.errorCode || fallbackReason,
    error: body?.message || fallbackReason,
  };
}

export function parseAuthorizeResponse({ httpOk, body, expectedState }) {
  if (!httpOk || body?.success !== true) return failure(body, 'WEBSITE_LOGIN_REQUIRED');
  const data = body.data;
  if (
    !data
    || typeof data.code !== 'string'
    || !data.code
    || typeof data.state !== 'string'
    || data.state !== expectedState
    || typeof data.expiresAtUtc !== 'string'
  ) {
    return failure(body, data?.state !== expectedState ? 'STATE_MISMATCH' : 'INVALID_AUTHORIZE_RESPONSE');
  }

  return {
    ok: true,
    code: data.code,
    state: data.state,
    expiresAtUtc: data.expiresAtUtc,
  };
}

export function parseTokenResponse({ httpOk, body, nowMs = Date.now() }) {
  if (!httpOk || body?.success !== true) return failure(body, 'TOKEN_EXCHANGE_FAILED');
  const data = body.data;
  const expiryMs = Date.parse(data?.accessTokenExpiry || '');
  if (
    !data
    || typeof data.accessToken !== 'string'
    || !data.accessToken
    || typeof data.accessTokenExpiry !== 'string'
    || !Number.isFinite(expiryMs)
    || expiryMs <= nowMs + 5_000
    || data.refreshToken != null
    || typeof data.userGuid !== 'string'
    || !data.userGuid.trim()
    || !(
      (typeof data.username === 'string' && data.username.trim())
      || (typeof data.fullName === 'string' && data.fullName.trim())
    )
  ) {
    return failure(body, 'INVALID_TOKEN_RESPONSE');
  }

  const user = {
    userGuid: data.userGuid.trim(),
    ...(typeof data.username === 'string' && data.username.trim()
      ? { username: data.username.trim() }
      : {}),
    ...(typeof data.fullName === 'string' && data.fullName.trim()
      ? { fullName: data.fullName.trim() }
      : {}),
  };
  return {
    ok: true,
    accessToken: data.accessToken,
    accessTokenExpiry: data.accessTokenExpiry,
    user,
  };
}

export function createSingleFlight(task) {
  let pending = null;
  return (...args) => {
    if (pending) return pending;

    let result;
    try {
      result = task(...args);
    } catch (error) {
      result = Promise.reject(error);
    }
    const current = Promise.resolve(result);
    const wrapped = current.finally(() => {
      if (pending === wrapped) pending = null;
    });
    pending = wrapped;
    return wrapped;
  };
}

export async function authorizeWithCookieSessionRefresh({
  authorizeRequest,
  refreshSession,
}) {
  const first = await authorizeRequest();
  if (first?.httpStatus !== 401) return first;

  const refreshed = await refreshSession();
  return refreshed ? authorizeRequest() : first;
}

function invokeAndSettle(task) {
  try {
    return Promise.resolve(task()).then(
      (value) => ({ ok: true, value }),
      (error) => ({ ok: false, error }),
    );
  } catch (error) {
    return Promise.resolve({ ok: false, error });
  }
}

// Chrome/Edge 的侧栏 API 必须直接响应用户手势；Safari 则需先完成授权，避免切页后暂停来源标签。
export async function coordinateWebsiteOpen({ buildTarget, authorize, openPanel }) {
  const earlyOpen = buildTarget === 'safari' ? null : invokeAndSettle(openPanel);
  const handoff = await authorize();

  if (!handoff?.ok) {
    if (earlyOpen) await earlyOpen;
    return { handoff, openResult: null };
  }

  if (!earlyOpen) {
    return { handoff, openResult: await openPanel() };
  }

  const opened = await earlyOpen;
  if (!opened.ok) throw opened.error;
  return { handoff, openResult: opened.value };
}

export function createAccessRequestExecutor({ isAuthFailure, clearAccessSession }) {
  return async (request) => {
    const response = await request();
    if (isAuthFailure(response)) await clearAccessSession();
    return response;
  };
}
