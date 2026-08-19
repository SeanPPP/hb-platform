// /shop bridge：仅在 HB 正式源且 pathname === '/shop' 时处理页面 PING/OPEN。
// 只返回 installed/version/browser/nonce，绝不传递账户、令牌或销售数据。
(async () => {
  if (location.pathname !== '/shop') return;

  const [
    { EXTENSION_VERSION, BUILD_TARGET, HB_WEB_ORIGIN },
    {
      OPEN_MESSAGE_TYPE,
      PING_MESSAGE_TYPE,
      validateBridgeMessage,
      buildBridgeResponse,
      detectBrowser,
    },
  ] =
    await Promise.all([
      import(chrome.runtime.getURL('config.js')),
      import(chrome.runtime.getURL('lib/handshake.js')),
    ]);

  if (location.origin !== HB_WEB_ORIGIN) return;

  const version = EXTENSION_VERSION;
  const browser = BUILD_TARGET && BUILD_TARGET !== 'unknown' ? BUILD_TARGET : detectBrowser(navigator.userAgent);
  const expectedOrigin = location.origin;

  window.addEventListener('message', async (event) => {
    const check = validateBridgeMessage({
      source: event.source,
      sourceWindow: window,
      origin: event.origin,
      expectedOrigin,
      message: event.data,
    });
    if (!check.ok) return; // 非法消息静默忽略

    const msg = event.data;
    if (msg.type === PING_MESSAGE_TYPE) {
      event.source.postMessage(
        buildBridgeResponse({ kind: 'STATUS', nonce: msg.nonce, version, browser, installed: true }),
        expectedOrigin,
      );
      return;
    }

    if (msg.type === OPEN_MESSAGE_TYPE) {
      try {
        const result = await chrome.runtime.sendMessage({ type: 'OPEN_SIDE_PANEL' });
        const ok = !!(result && result.ok);
        event.source.postMessage(
          buildBridgeResponse({
            kind: 'OPEN_RESULT',
            nonce: msg.nonce,
            version,
            browser,
            installed: true,
            ok,
            error: ok ? undefined : (result && result.error) || 'open-failed',
          }),
          expectedOrigin,
        );
      } catch (e) {
        event.source.postMessage(
          buildBridgeResponse({
            kind: 'OPEN_RESULT',
            nonce: msg.nonce,
            version,
            browser,
            installed: true,
            ok: false,
            error: String((e && e.message) || e),
          }),
          expectedOrigin,
        );
      }
    }
  });
})();
