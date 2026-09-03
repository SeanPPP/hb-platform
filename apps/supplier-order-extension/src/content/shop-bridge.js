// /shop bridge：仅在 HB 正式源的顶层 /shop 页面处理 PING/OPEN 与内部网站会话交接。
// window 消息只返回安装/版本/浏览器/nonce/结果，授权 code 与 token 只走扩展 runtime。
(async () => {
  if (window !== window.top || location.pathname !== '/shop') return;

  const [
    {
      API_BASE,
      EXTENSION_VERSION,
      BUILD_TARGET,
      HB_WEB_ORIGIN,
    },
    {
      OPEN_MESSAGE_TYPE,
      PING_MESSAGE_TYPE,
      validateBridgeMessage,
      buildBridgeResponse,
      detectBrowser,
    },
    {
      authorizeWithCookieSessionRefresh,
      coordinateWebsiteOpen,
      createPkceMaterial,
      createSingleFlight,
      parseAuthorizeResponse,
      validateWebsiteSessionContext,
      WEBSITE_SESSION_CLIENT_ID,
    },
  ] = await Promise.all([
    import(chrome.runtime.getURL('config.js')),
    import(chrome.runtime.getURL('lib/handshake.js')),
    import(chrome.runtime.getURL('lib/session-handoff.js')),
  ]);

  if (location.origin !== HB_WEB_ORIGIN) return;

  const version = EXTENSION_VERSION;
  const browser = BUILD_TARGET && BUILD_TARGET !== 'unknown'
    ? BUILD_TARGET
    : detectBrowser(navigator.userAgent);
  const expectedOrigin = location.origin;

  async function getConfiguredApiOrigin() {
    try {
      const result = await chrome.runtime.sendMessage({ type: 'GET_API_ORIGIN' });
      return result?.ok && result.apiOrigin ? result.apiOrigin : API_BASE;
    } catch {
      return API_BASE;
    }
  }

  const authorizeAndExchange = createSingleFlight(async ({ apiOrigin = API_BASE } = {}) => {
    const context = validateWebsiteSessionContext({
      pageUrl: location.href,
      webOrigin: HB_WEB_ORIGIN,
      apiOrigin,
      isTopLevel: window === window.top,
    });
    if (!context.ok) {
      return {
        ok: false,
        reason: context.reason,
        error: context.reason === 'API_ORIGIN_MISMATCH'
          ? '当前接口与 HB SHOP 网页不同源'
          : '网站会话授权页面无效',
      };
    }

    const { codeVerifier, codeChallenge, state } = await createPkceMaterial();
    let authorization;
    try {
      const authorizeRequest = async () => {
        const response = await fetch(`${apiOrigin}/api/Auth/extension/authorize`, {
          method: 'POST',
          credentials: 'include',
          headers: {
            'Content-Type': 'application/json',
            'X-HB-Extension-Version': version,
          },
          body: JSON.stringify({
            codeChallenge,
            state,
            clientId: WEBSITE_SESSION_CLIENT_ID,
          }),
        });
        let body = null;
        try {
          body = await response.json();
        } catch {
          // 非 JSON 响应由统一解析转为安全失败，不把响应正文带入消息或日志。
        }
        return { httpStatus: response.status, httpOk: response.ok, body };
      };
      const refreshSession = async () => {
        const response = await fetch(`${apiOrigin}/api/Auth/session/refresh`, {
          method: 'POST',
          credentials: 'include',
          headers: {
            'Content-Type': 'application/json',
            'X-HB-Extension-Version': version,
          },
          body: JSON.stringify({}),
        });
        let body = null;
        try {
          body = await response.json();
        } catch {
          // 刷新失败按未登录处理，不读取或传递 Cookie/响应正文。
        }
        return response.ok && body?.success === true;
      };

      // 网站 access cookie 过期但 refresh cookie 仍有效时，仅由同源网页桥刷新一次。
      // Worker 永不调用刷新接口，也不会读取或保存网站 refresh token。
      authorization = await authorizeWithCookieSessionRefresh({
        authorizeRequest,
        refreshSession,
      });
    } catch (error) {
      return {
        ok: false,
        reason: 'AUTHORIZE_UNAVAILABLE',
        error: String(error?.message || error),
      };
    }

    const authorized = parseAuthorizeResponse({
      httpOk: authorization.httpOk,
      body: authorization.body,
      expectedState: state,
    });
    if (!authorized.ok) return authorized;

    return chrome.runtime.sendMessage({
      type: 'WEBSITE_SESSION_GRANT',
      code: authorized.code,
      codeVerifier,
      state: authorized.state,
      clientId: WEBSITE_SESSION_CLIENT_ID,
    });
  });

  chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
    if (message?.type !== 'REQUEST_WEBSITE_SESSION') return false;
    authorizeAndExchange({ apiOrigin: message.apiOrigin })
      .then(sendResponse)
      .catch((error) => sendResponse({
        ok: false,
        reason: 'WEBSITE_SESSION_HANDOFF_FAILED',
        error: String(error?.message || error),
      }));
    return true;
  });

  window.addEventListener('message', async (event) => {
    const check = validateBridgeMessage({
      source: event.source,
      sourceWindow: window,
      origin: event.origin,
      expectedOrigin,
      message: event.data,
    });
    if (!check.ok) return;

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
        const { handoff, openResult: result } = await coordinateWebsiteOpen({
          buildTarget: BUILD_TARGET,
          // Chrome/Edge 会在本次调用先触发侧栏；Safari 仍先授权再打开完整助手页。
          openPanel: () => chrome.runtime.sendMessage({ type: 'OPEN_SIDE_PANEL' }),
          authorize: async () => authorizeAndExchange({
            apiOrigin: await getConfiguredApiOrigin(),
          }),
        });
        if (!handoff?.ok) {
          event.source.postMessage(
            buildBridgeResponse({
              kind: 'OPEN_RESULT',
              nonce: msg.nonce,
              version,
              browser,
              installed: true,
              ok: false,
              error: handoff?.reason || 'website-session-required',
            }),
            expectedOrigin,
          );
          return;
        }

        const ok = !!result?.ok;
        event.source.postMessage(
          buildBridgeResponse({
            kind: 'OPEN_RESULT',
            nonce: msg.nonce,
            version,
            browser,
            installed: true,
            ok,
            error: ok ? undefined : result?.error || 'open-failed',
          }),
          expectedOrigin,
        );
      } catch (error) {
        event.source.postMessage(
          buildBridgeResponse({
            kind: 'OPEN_RESULT',
            nonce: msg.nonce,
            version,
            browser,
            installed: true,
            ok: false,
            error: String(error?.message || error),
          }),
          expectedOrigin,
        );
      }
    }
  });

  try {
    const ready = await chrome.runtime.sendMessage({ type: 'SHOP_BRIDGE_READY' });
    if (ready?.ok && ready.shouldAuthorize) {
      void authorizeAndExchange({ apiOrigin: ready.apiOrigin });
    }
  } catch {
    // Worker 尚未就绪时，CURRENT/重新检查仍会主动向当前 /shop 标签页请求授权。
  }
})();
