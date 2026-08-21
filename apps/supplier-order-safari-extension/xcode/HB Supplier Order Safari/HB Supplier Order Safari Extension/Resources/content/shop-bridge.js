(() => {
  var __defProp = Object.defineProperty;
  var __getOwnPropNames = Object.getOwnPropertyNames;
  var __esm = (fn, res, err) => function __init() {
    if (err) throw err[0];
    try {
      return fn && (res = (0, fn[__getOwnPropNames(fn)[0]])(fn = 0)), res;
    } catch (e) {
      throw err = [e], e;
    }
  };
  var __export = (target, all) => {
    for (var name in all)
      __defProp(target, name, { get: all[name], enumerable: true });
  };

  // hb-safari-config:config.js
  var config_exports = {};
  __export(config_exports, {
    API_BASE: () => API_BASE,
    BUILD_TARGET: () => BUILD_TARGET,
    EXTENSION_VERSION: () => EXTENSION_VERSION,
    HB_API_ORIGIN: () => HB_API_ORIGIN,
    HB_WEB_ORIGIN: () => HB_WEB_ORIGIN
  });
  var EXTENSION_VERSION, HB_API_ORIGIN, HB_WEB_ORIGIN, BUILD_TARGET, API_BASE;
  var init_config = __esm({
    "hb-safari-config:config.js"() {
      EXTENSION_VERSION = "1.2.0";
      HB_API_ORIGIN = "https://hotbargain.vip";
      HB_WEB_ORIGIN = "https://hotbargain.vip";
      BUILD_TARGET = "safari";
      API_BASE = HB_API_ORIGIN;
    }
  });

  // src/lib/handshake.js
  var handshake_exports = {};
  __export(handshake_exports, {
    EXTENSION_MESSAGE_SOURCE: () => EXTENSION_MESSAGE_SOURCE,
    NONCE_PATTERN: () => NONCE_PATTERN,
    OPEN_MESSAGE_TYPE: () => OPEN_MESSAGE_TYPE,
    OPEN_RESULT_MESSAGE_TYPE: () => OPEN_RESULT_MESSAGE_TYPE,
    PING_MESSAGE_TYPE: () => PING_MESSAGE_TYPE,
    PLATFORM_MESSAGE_SOURCE: () => PLATFORM_MESSAGE_SOURCE,
    STATUS_MESSAGE_TYPE: () => STATUS_MESSAGE_TYPE,
    buildBridgeResponse: () => buildBridgeResponse,
    detectBrowser: () => detectBrowser,
    generateNonce: () => generateNonce,
    isValidNonce: () => isValidNonce,
    validateBridgeMessage: () => validateBridgeMessage
  });
  function isValidNonce(nonce) {
    return typeof nonce === "string" && NONCE_PATTERN.test(nonce);
  }
  function bytesToBase64Url(bytes) {
    let binary = "";
    for (let i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]);
    if (typeof btoa === "function") {
      return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
    }
    return Buffer.from(bytes).toString("base64url");
  }
  function generateNonce(bytes = 32) {
    const arr = new Uint8Array(bytes);
    const c = globalThis.crypto;
    if (c && typeof c.getRandomValues === "function") {
      c.getRandomValues(arr);
    } else {
      for (let i = 0; i < bytes; i++) arr[i] = Math.floor(Math.random() * 256);
    }
    return bytesToBase64Url(arr);
  }
  function detectBrowser(userAgent) {
    const ua = String(userAgent || "");
    if (/Edg\//.test(ua)) return "edge";
    if (/Chrome\//.test(ua)) return "chrome";
    if (/Version\//.test(ua) && /Safari\//.test(ua)) return "safari";
    return "unknown";
  }
  function validateBridgeMessage({ source, sourceWindow, origin, expectedOrigin, message }) {
    if (source !== sourceWindow) return { ok: false, reason: "invalid-source" };
    if (origin !== expectedOrigin) return { ok: false, reason: "invalid-origin" };
    if (!message || typeof message !== "object") return { ok: false, reason: "invalid-message" };
    if (message.source !== PLATFORM_MESSAGE_SOURCE) return { ok: false, reason: "invalid-message-source" };
    if (message.type !== PING_MESSAGE_TYPE && message.type !== OPEN_MESSAGE_TYPE) {
      return { ok: false, reason: "invalid-type" };
    }
    if (!isValidNonce(message.nonce)) return { ok: false, reason: "invalid-nonce" };
    return { ok: true };
  }
  function buildBridgeResponse({ kind, nonce, version, browser, installed = true, ok, error }) {
    const resp = {
      source: EXTENSION_MESSAGE_SOURCE,
      type: kind === "OPEN_RESULT" ? OPEN_RESULT_MESSAGE_TYPE : STATUS_MESSAGE_TYPE,
      nonce,
      installed,
      version,
      browser
    };
    if (kind === "OPEN_RESULT") {
      resp.ok = ok === true;
      if (error) resp.error = error;
    }
    return resp;
  }
  var PLATFORM_MESSAGE_SOURCE, EXTENSION_MESSAGE_SOURCE, PING_MESSAGE_TYPE, OPEN_MESSAGE_TYPE, STATUS_MESSAGE_TYPE, OPEN_RESULT_MESSAGE_TYPE, NONCE_PATTERN;
  var init_handshake = __esm({
    "src/lib/handshake.js"() {
      PLATFORM_MESSAGE_SOURCE = "hb-platform";
      EXTENSION_MESSAGE_SOURCE = "hb-supplier-ordering-extension";
      PING_MESSAGE_TYPE = "HB_SUPPLIER_ASSISTANT_PING";
      OPEN_MESSAGE_TYPE = "HB_SUPPLIER_ASSISTANT_OPEN";
      STATUS_MESSAGE_TYPE = "HB_SUPPLIER_ASSISTANT_STATUS";
      OPEN_RESULT_MESSAGE_TYPE = "HB_SUPPLIER_ASSISTANT_OPEN_RESULT";
      NONCE_PATTERN = /^[A-Za-z0-9_-]{16,64}$/;
    }
  });

  // src/content/shop-bridge.js
  (async () => {
    if (location.pathname !== "/shop") return;
    const [
      { EXTENSION_VERSION: EXTENSION_VERSION2, BUILD_TARGET: BUILD_TARGET2, HB_WEB_ORIGIN: HB_WEB_ORIGIN2 },
      {
        OPEN_MESSAGE_TYPE: OPEN_MESSAGE_TYPE2,
        PING_MESSAGE_TYPE: PING_MESSAGE_TYPE2,
        validateBridgeMessage: validateBridgeMessage2,
        buildBridgeResponse: buildBridgeResponse2,
        detectBrowser: detectBrowser2
      }
    ] = await Promise.all([
      Promise.resolve().then(() => (init_config(), config_exports)),
      Promise.resolve().then(() => (init_handshake(), handshake_exports))
    ]);
    if (location.origin !== HB_WEB_ORIGIN2) return;
    const version = EXTENSION_VERSION2;
    const browser = BUILD_TARGET2 && BUILD_TARGET2 !== "unknown" ? BUILD_TARGET2 : detectBrowser2(navigator.userAgent);
    const expectedOrigin = location.origin;
    window.addEventListener("message", async (event) => {
      const check = validateBridgeMessage2({
        source: event.source,
        sourceWindow: window,
        origin: event.origin,
        expectedOrigin,
        message: event.data
      });
      if (!check.ok) return;
      const msg = event.data;
      if (msg.type === PING_MESSAGE_TYPE2) {
        event.source.postMessage(
          buildBridgeResponse2({ kind: "STATUS", nonce: msg.nonce, version, browser, installed: true }),
          expectedOrigin
        );
        return;
      }
      if (msg.type === OPEN_MESSAGE_TYPE2) {
        try {
          const result = await chrome.runtime.sendMessage({ type: "OPEN_SIDE_PANEL" });
          const ok = !!(result && result.ok);
          event.source.postMessage(
            buildBridgeResponse2({
              kind: "OPEN_RESULT",
              nonce: msg.nonce,
              version,
              browser,
              installed: true,
              ok,
              error: ok ? void 0 : result && result.error || "open-failed"
            }),
            expectedOrigin
          );
        } catch (e) {
          event.source.postMessage(
            buildBridgeResponse2({
              kind: "OPEN_RESULT",
              nonce: msg.nonce,
              version,
              browser,
              installed: true,
              ok: false,
              error: String(e && e.message || e)
            }),
            expectedOrigin
          );
        }
      }
    });
  })();
})();
