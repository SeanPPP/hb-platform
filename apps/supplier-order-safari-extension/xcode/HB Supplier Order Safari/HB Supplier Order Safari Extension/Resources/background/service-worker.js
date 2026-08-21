(() => {
  // src/lib/api-response.js
  var AUTH_ERROR_CODES = [
    "UNAUTHORIZED",
    "TOKEN_EXPIRED",
    "AUTH_FAILED",
    "INVALID_TOKEN",
    "LOGIN_REQUIRED",
    "EXPIRED_TOKEN"
  ];
  function isAuthFailure(resp, httpStatus) {
    if (httpStatus === 401) return true;
    if (resp && resp.success === false && typeof resp.errorCode === "string" && AUTH_ERROR_CODES.includes(resp.errorCode.toUpperCase())) {
      return true;
    }
    return false;
  }

  // src/lib/api-origin.js
  var LOCAL_API_ORIGIN = "http://localhost:5002";
  function parseAllowedOrigin(value) {
    try {
      const url = new URL(value);
      const isLocalHttp = url.protocol === "http:" && (url.hostname === "localhost" || url.hostname === "127.0.0.1");
      if (url.protocol !== "https:" && !isLocalHttp) return null;
      if (url.username || url.password || url.pathname !== "/" || url.search || url.hash) return null;
      return url.origin;
    } catch {
      return null;
    }
  }
  function normalizeApiOrigin(value, defaultOrigin) {
    const normalizedDefault = parseAllowedOrigin(String(defaultOrigin || "").trim());
    if (!normalizedDefault) return null;
    const input = String(value ?? "").trim();
    if (!input || input === "/") return normalizedDefault;
    return parseAllowedOrigin(input);
  }
  function resolveApiOrigin(storedOrigin, defaultOrigin) {
    return normalizeApiOrigin(storedOrigin, defaultOrigin) || normalizeApiOrigin("/", defaultOrigin);
  }
  function toApiHostPattern(origin) {
    const parsed = parseAllowedOrigin(String(origin || "").trim());
    return parsed ? `${parsed}/*` : null;
  }

  // src/lib/refresh-flow.js
  function createAuthExecutor({ isAuthFailure: isAuthFailure2, refresh }) {
    let refreshPromise = null;
    async function withRefresh(request) {
      const first = await request();
      if (!isAuthFailure2(first)) return first;
      if (!refreshPromise) {
        refreshPromise = Promise.resolve().then(refresh).then(
          () => {
            refreshPromise = null;
          },
          (err) => {
            refreshPromise = null;
            throw err;
          }
        );
      }
      await refreshPromise;
      return request();
    }
    return {
      withRefresh,
      _isRefreshing: () => !!refreshPromise,
      _reset: () => {
        refreshPromise = null;
      }
    };
  }

  // src/lib/origin-registration.js
  async function resolveGrantedProfileOrigins(profiles, hasPermission) {
    const allowedOrigins = [
      ...new Set(
        (Array.isArray(profiles) ? profiles : []).filter((profile) => profile && profile.enabled !== false).flatMap((profile) => Array.isArray(profile.origins) ? profile.origins : [])
      )
    ];
    const grantedOrigins = [];
    for (const origin of allowedOrigins) {
      try {
        if (await hasPermission(origin)) grantedOrigins.push(origin);
      } catch {
      }
    }
    return grantedOrigins;
  }

  // src/lib/profiles-default.js
  var DEFAULT_PROFILES = {
    configVersion: "2",
    profiles: [
      {
        // DATS 是显示名称；HB 的供应商业务代码是 240。
        supplierCode: "240",
        displayName: "DATS",
        enabled: true,
        origins: ["https://www.dats.com.au/*"],
        listPagePatterns: ["https://www.dats.com.au/*"],
        cardSelector: ".product[data-product-code]",
        itemNumber: {
          source: "attribute",
          selector: null,
          attribute: "data-product-code",
          transforms: ["trim", "uppercase"]
        },
        mountSelector: ".widget-productlist-code",
        mountPosition: "afterend"
      }
    ]
  };

  // src/lib/profile-cache.js
  var LEGACY_DATS_CODE = "DATS";
  var DATS_BUSINESS_CODE = "240";
  var DATS_ORIGIN = "https://www.dats.com.au/*";
  function migrateProfileConfig(raw) {
    if (!raw || typeof raw !== "object" || !Array.isArray(raw.profiles)) return raw;
    let changed = false;
    const profiles = raw.profiles.map((profile) => {
      const isLegacyDats = profile && profile.supplierCode === LEGACY_DATS_CODE && Array.isArray(profile.origins) && profile.origins.includes(DATS_ORIGIN);
      if (!isLegacyDats) return profile;
      changed = true;
      return { ...profile, supplierCode: DATS_BUSINESS_CODE };
    });
    return changed ? { ...raw, profiles } : raw;
  }

  // src/lib/transforms.js
  var ALLOWED_TRANSFORMS = /* @__PURE__ */ new Set([
    "trim",
    "uppercase",
    "lowercase",
    "after-colon",
    "underscore-to-slash",
    "after-sku"
  ]);
  function isTransformAllowed(type) {
    return ALLOWED_TRANSFORMS.has(type);
  }
  function normalizeTransform(transform) {
    return typeof transform === "string" ? { type: transform } : transform;
  }
  function safeTransformList(transforms) {
    if (transforms == null) return true;
    if (!Array.isArray(transforms)) return false;
    return transforms.every((transform) => {
      const normalized = normalizeTransform(transform);
      return !!normalized && isTransformAllowed(normalized.type);
    });
  }

  // src/lib/profiles.js
  var ALLOWED_SOURCES = /* @__PURE__ */ new Set(["attribute", "text"]);
  var ALLOWED_MOUNT_POSITIONS = /* @__PURE__ */ new Set(["beforebegin", "afterbegin", "beforeend", "afterend"]);
  var TXK_HTTP_PATTERN = /^http:\/\/txkorders\.inzantsales\.com(?<path>\/[^\s]*)$/i;
  function isSafeMatchPattern(value, originOnly = false) {
    if (typeof value !== "string" || value.length === 0 || value.length > 300) return false;
    const match = /^https:\/\/(?:\*\.)?[A-Za-z0-9.-]+(?::\d+)?(?<path>\/[^\s]*)$/.exec(value) || TXK_HTTP_PATTERN.exec(value);
    return !!match && (!originOnly || match.groups.path === "/*");
  }
  function isSafePagePattern(value) {
    return typeof value === "string" && value.startsWith("/") && value.length <= 300 || isSafeMatchPattern(value);
  }
  function originMatchesAny(origins, origin) {
    return (origins || []).some((pattern) => matchUrlPattern(pattern, `${origin}/`));
  }
  function escapeRegex(value) {
    return value.replace(/[|\\{}()[\]^$+?.]/g, "\\$&");
  }
  function matchUrlPattern(pattern, href) {
    if (typeof pattern !== "string" || !pattern || typeof href !== "string") return false;
    let target = href;
    let candidate = pattern;
    if (candidate.startsWith("/")) {
      try {
        target = new URL(href).pathname;
      } catch {
        return false;
      }
    }
    const regex = `^${escapeRegex(candidate).replaceAll("*", ".*")}$`;
    return new RegExp(regex, "i").test(target);
  }
  function validateProfiles(raw) {
    if (!raw || typeof raw !== "object" || !Array.isArray(raw.profiles)) {
      return { valid: false, profiles: [], errors: ["profiles \u5FC5\u987B\u4E3A {profiles:[...]} \u5BF9\u8C61"] };
    }
    const errors = [];
    const out = [];
    raw.profiles.forEach((p, i) => {
      const path = `profiles[${i}]`;
      if (!p || typeof p !== "object") {
        errors.push(`${path} \u4E0D\u662F\u5BF9\u8C61`);
        return;
      }
      const errs = [];
      if (typeof p.supplierCode !== "string" || !p.supplierCode) errs.push("supplierCode \u5FC5\u586B");
      if (typeof p.displayName !== "string" || !p.displayName) errs.push("displayName \u5FC5\u586B");
      if (typeof p.enabled !== "boolean") errs.push("enabled \u5FC5\u987B\u4E3A boolean");
      if (!Array.isArray(p.origins) || p.origins.length === 0) {
        errs.push("origins \u5FC5\u987B\u4E3A\u975E\u7A7A\u6570\u7EC4");
      } else {
        p.origins.forEach((o, j) => {
          if (!isSafeMatchPattern(o, true)) errs.push(`origins[${j}] \u975E\u6CD5`);
        });
      }
      if (!Array.isArray(p.listPagePatterns)) {
        errs.push("listPagePatterns \u5FC5\u987B\u4E3A\u6570\u7EC4");
      } else {
        p.listPagePatterns.forEach((pattern, j) => {
          if (!isSafePagePattern(pattern)) errs.push(`listPagePatterns[${j}] \u975E\u6CD5`);
        });
      }
      if (typeof p.cardSelector !== "string" || !p.cardSelector) errs.push("cardSelector \u5FC5\u586B");
      if (!p.itemNumber || typeof p.itemNumber !== "object") {
        errs.push("itemNumber \u5FC5\u586B");
      } else {
        const it = p.itemNumber;
        if (!ALLOWED_SOURCES.has(it.source)) errs.push("itemNumber.source \u975E\u6CD5");
        if (it.source === "attribute" && (typeof it.attribute !== "string" || !it.attribute)) {
          errs.push("attribute source \u9700\u8981 attribute");
        }
        if (it.selector != null && typeof it.selector !== "string") {
          errs.push("itemNumber.selector \u5FC5\u987B\u4E3A\u5B57\u7B26\u4E32\u6216 null");
        }
        if (!safeTransformList(it.transforms)) errs.push("itemNumber.transforms \u5305\u542B\u4E0D\u652F\u6301\u7684 transform");
      }
      if (typeof p.mountSelector !== "string" || !p.mountSelector) errs.push("mountSelector \u5FC5\u586B");
      if (!ALLOWED_MOUNT_POSITIONS.has(p.mountPosition)) errs.push("mountPosition \u975E\u6CD5");
      if (errs.length) {
        errors.push(...errs.map((e) => `${path}.${e}`));
        return;
      }
      out.push(p);
    });
    return { valid: errors.length === 0, profiles: out, errors };
  }
  function matchProfile(profiles, { origin, pathname }) {
    for (const p of profiles || []) {
      if (p.enabled === false) continue;
      if (!originMatchesAny(p.origins, origin)) continue;
      return p;
    }
    return null;
  }

  // src/lib/assistant-panel.js
  var ASSISTANT_PANEL_SOURCE_TAB_KEY = "assistantPanelSourceTabId";
  var PANEL_PATH = "sidepanel/sidepanel.html";
  function createAssistantPanelController({ browserApi, buildTarget }) {
    const isSafari = buildTarget === "safari";
    const panelUrl = browserApi.runtime.getURL(PANEL_PATH);
    const extensionRoot = browserApi.runtime.getURL("");
    let safariPagePromise = null;
    async function rememberSourceTab(tab) {
      if (!isSafari || !Number.isInteger(tab?.id)) return;
      if (typeof tab.url === "string" && tab.url.startsWith(extensionRoot)) return;
      await browserApi.storage.session.set({ [ASSISTANT_PANEL_SOURCE_TAB_KEY]: tab.id });
    }
    async function focusOrCreateSafariTab() {
      const tabs = await browserApi.tabs.query({});
      const existing = tabs.find((tab) => tab.url === panelUrl);
      if (Number.isInteger(existing?.id)) {
        await browserApi.tabs.update(existing.id, { active: true });
        return;
      }
      await browserApi.tabs.create({
        url: panelUrl,
        active: true
      });
    }
    async function openSafariPage({ tabId } = {}) {
      if (Number.isInteger(tabId)) {
        try {
          await rememberSourceTab(await browserApi.tabs.get(tabId));
        } catch {
        }
      }
      if (!safariPagePromise) {
        safariPagePromise = (async () => {
          try {
            await browserApi.runtime.openOptionsPage();
          } catch {
            await focusOrCreateSafariTab();
          }
        })().finally(() => {
          safariPagePromise = null;
        });
      }
      return safariPagePromise;
    }
    async function queryActiveTabs() {
      if (isSafari) {
        const stored = await browserApi.storage.session.get(ASSISTANT_PANEL_SOURCE_TAB_KEY);
        const tabId = stored[ASSISTANT_PANEL_SOURCE_TAB_KEY];
        if (Number.isInteger(tabId)) {
          try {
            const tab = await browserApi.tabs.get(tabId);
            if (!(typeof tab.url === "string" && tab.url.startsWith(extensionRoot))) {
              return [tab];
            }
          } catch {
          }
          await browserApi.storage.session.remove(ASSISTANT_PANEL_SOURCE_TAB_KEY);
        }
      }
      return browserApi.tabs.query({ active: true, lastFocusedWindow: true });
    }
    function registerListeners() {
      if (!isSafari) return;
      browserApi.action.onClicked.addListener((tab) => openSafariPage({ tabId: tab?.id }).catch(() => void 0));
      browserApi.tabs.onActivated.addListener(({ tabId }) => browserApi.tabs.get(tabId).then(rememberSourceTab).catch(() => void 0));
    }
    return {
      rememberSourceTab,
      queryActiveTabs,
      registerListeners,
      configureAction() {
        return isSafari ? Promise.resolve() : browserApi.sidePanel.setPanelBehavior({ openPanelOnActionClick: true });
      },
      open(options) {
        return isSafari ? openSafariPage(options) : browserApi.sidePanel.open(options);
      }
    };
  }

  // hb-safari-config:config.js
  var EXTENSION_VERSION = "1.2.0";
  var HB_API_ORIGIN = "https://hotbargain.vip";
  var BUILD_TARGET = "safari";
  var API_BASE = HB_API_ORIGIN;

  // src/background/service-worker.js
  var ACCESS_KEY = "accessToken";
  var REFRESH_KEY = "refreshToken";
  var PROFILES_KEY = "supplierProfiles";
  var GRANTED_KEY = "grantedOrigins";
  var API_ORIGIN_KEY = "apiOrigin";
  var assistantPanel = createAssistantPanelController({ browserApi: chrome, buildTarget: BUILD_TARGET });
  assistantPanel.registerListeners();
  var getSession = (keys) => chrome.storage.session.get(keys);
  var setSession = (obj) => chrome.storage.session.set(obj);
  var removeSession = (keys) => chrome.storage.session.remove(keys);
  var getLocal = (keys) => chrome.storage.local.get(keys);
  var setLocal = (obj) => chrome.storage.local.set(obj);
  var removeLocal = (keys) => chrome.storage.local.remove(keys);
  async function getAccessToken() {
    const r = await getSession(ACCESS_KEY);
    return r[ACCESS_KEY];
  }
  async function getRefreshToken() {
    const r = await getLocal(REFRESH_KEY);
    return r[REFRESH_KEY];
  }
  async function getApiOrigin() {
    const stored = await getLocal(API_ORIGIN_KEY);
    return resolveApiOrigin(stored[API_ORIGIN_KEY], API_BASE);
  }
  async function rawFetch(path, options = {}) {
    const [accessToken, apiOrigin] = await Promise.all([getAccessToken(), getApiOrigin()]);
    const headers = {
      "X-HB-Extension-Version": EXTENSION_VERSION,
      ...options.body ? { "Content-Type": "application/json" } : {},
      ...options.headers || {}
    };
    if (accessToken) headers.Authorization = `Bearer ${accessToken}`;
    const res = await fetch(`${apiOrigin}${path}`, { ...options, headers });
    let body = null;
    try {
      body = await res.json();
    } catch {
    }
    return {
      httpStatus: res.status,
      ok: res.ok,
      success: body && body.success,
      data: body && body.data,
      message: body && body.message,
      errorCode: body && body.errorCode
    };
  }
  async function handleGetApiOrigin() {
    return {
      ok: true,
      apiOrigin: await getApiOrigin(),
      defaultApiOrigin: API_BASE,
      localApiOrigin: LOCAL_API_ORIGIN
    };
  }
  async function handleSetApiOrigin({ apiOrigin }) {
    const normalized = normalizeApiOrigin(apiOrigin, API_BASE);
    if (!normalized) return { ok: false, error: "\u63A5\u53E3\u5730\u5740\u65E0\u6548" };
    const pattern = toApiHostPattern(normalized);
    if (!pattern || !await chrome.permissions.contains({ origins: [pattern] })) {
      return { ok: false, error: "\u63A5\u53E3\u5730\u5740\u5C1A\u672A\u83B7\u5F97\u6D4F\u89C8\u5668\u6388\u6743" };
    }
    const current = await getApiOrigin();
    if (normalized === current) {
      return { ok: true, apiOrigin: normalized, changed: false, requiresLogin: false };
    }
    await setLocal({ [API_ORIGIN_KEY]: normalized, [PROFILES_KEY]: DEFAULT_PROFILES });
    await Promise.all([removeSession([ACCESS_KEY]), removeLocal([REFRESH_KEY])]);
    await syncContentScripts();
    return { ok: true, apiOrigin: normalized, changed: true, requiresLogin: true };
  }
  async function doRefresh() {
    try {
      const [accessToken, refreshToken] = await Promise.all([getAccessToken(), getRefreshToken()]);
      if (!refreshToken) throw new Error("no refresh token");
      const res = await rawFetch("/api/Auth/refresh", {
        method: "POST",
        body: JSON.stringify({ accessToken, refreshToken })
      });
      if (!res.success || !res.data || !res.data.accessToken || !res.data.refreshToken) {
        throw new Error(res.message || res.errorCode || "refresh failed");
      }
      await Promise.all([
        setSession({ [ACCESS_KEY]: res.data.accessToken }),
        setLocal({ [REFRESH_KEY]: res.data.refreshToken })
      ]);
    } catch (error) {
      await Promise.all([removeSession([ACCESS_KEY]), removeLocal([REFRESH_KEY])]);
      throw error;
    }
  }
  var authExecutor = createAuthExecutor({
    isAuthFailure: (r) => isAuthFailure(r, r.httpStatus),
    refresh: doRefresh
  });
  function apiRequest(path, options = {}) {
    return authExecutor.withRefresh(() => rawFetch(path, options));
  }
  async function handleLogin({ username, password }) {
    if (typeof username !== "string" || typeof password !== "string" || !username || !password) {
      return { ok: false, error: "\u7528\u6237\u540D\u6216\u5BC6\u7801\u4E3A\u7A7A" };
    }
    const res = await rawFetch("/api/Auth/login", {
      method: "POST",
      body: JSON.stringify({ username, password, passwordFormat: "raw" })
    });
    if (!res.success || !res.data || !res.data.accessToken) {
      return { ok: false, error: res.message || res.errorCode || "\u767B\u5F55\u5931\u8D25" };
    }
    await setSession({ [ACCESS_KEY]: res.data.accessToken });
    if (res.data.refreshToken) await setLocal({ [REFRESH_KEY]: res.data.refreshToken });
    return { ok: true, user: res.data.user || res.data };
  }
  async function handleCurrent() {
    const res = await apiRequest("/api/Auth/current", { method: "GET" });
    if (!res.success) return { ok: false, error: res.message || res.errorCode || "\u83B7\u53D6\u7528\u6237\u5931\u8D25" };
    return { ok: true, user: res.data };
  }
  async function handleLogout() {
    try {
      await authExecutor.withRefresh(async () => {
        const refreshToken = await getRefreshToken();
        if (!refreshToken) return { httpStatus: 200, success: true };
        return rawFetch("/api/Auth/logout", {
          method: "POST",
          body: JSON.stringify({ refreshToken })
        });
      });
    } catch {
    }
    await removeSession([ACCESS_KEY]);
    await removeLocal([REFRESH_KEY]);
    return { ok: true };
  }
  async function handleGetProfiles() {
    const storedConfig = await migrateStoredProfiles();
    const storedValidation = validateProfiles(storedConfig);
    let config = storedValidation.valid ? {
      configVersion: storedConfig.configVersion ?? "1",
      profiles: storedValidation.profiles
    } : DEFAULT_PROFILES;
    let source = storedValidation.valid ? "cache" : "default";
    try {
      const res = await apiRequest("/api/react/v1/browser-extension/supplier-profiles", { method: "GET" });
      if (res.success && res.data && Array.isArray(res.data.profiles)) {
        const v = validateProfiles(res.data);
        if (v.valid) {
          config = {
            configVersion: res.data.configVersion ?? "1",
            profiles: v.profiles
          };
          source = "server";
        } else {
          config = { configVersion: res.data.configVersion ?? "invalid", profiles: [] };
          source = "invalid-server";
        }
      }
    } catch {
    }
    await setLocal({ [PROFILES_KEY]: config });
    await syncContentScripts();
    return { ok: true, profiles: config.profiles, configVersion: config.configVersion, source };
  }
  async function migrateStoredProfiles() {
    const { [PROFILES_KEY]: storedConfig } = await getLocal(PROFILES_KEY);
    const migrated = migrateProfileConfig(storedConfig);
    if (migrated !== storedConfig) await setLocal({ [PROFILES_KEY]: migrated });
    return migrated;
  }
  async function handleRelease() {
    const res = await apiRequest("/api/react/v1/browser-extension/release", { method: "GET" });
    if (!res.success) return { ok: false, error: res.message || res.errorCode || "\u83B7\u53D6\u7248\u672C\u5931\u8D25" };
    return { ok: true, release: res.data };
  }
  async function handleSummaryBatch({ storeCode, supplierCode, itemNumbers }) {
    if (!storeCode || !supplierCode || !Array.isArray(itemNumbers)) {
      return { ok: false, error: "\u53C2\u6570\u7F3A\u5931" };
    }
    const res = await apiRequest("/api/react/v1/browser-extension/product-purchase-cycle-summary/batch", {
      method: "POST",
      body: JSON.stringify({ storeCode, supplierCode, itemNumbers })
    });
    if (!res.success) return { ok: false, error: res.message || res.errorCode || "\u6458\u8981\u83B7\u53D6\u5931\u8D25" };
    return { ok: true, data: res.data };
  }
  async function handlePurchaseCycles({ storeCode, supplierCode, itemNumber }) {
    if (!storeCode || !supplierCode || !itemNumber) {
      return { ok: false, error: "\u53C2\u6570\u7F3A\u5931" };
    }
    const res = await apiRequest("/api/react/v1/browser-extension/product-purchase-cycles", {
      method: "POST",
      body: JSON.stringify({ storeCode, supplierCode, itemNumber })
    });
    if (!res.success) return { ok: false, error: res.message || res.errorCode || "\u91C7\u8D2D\u5468\u671F\u83B7\u53D6\u5931\u8D25" };
    return { ok: true, data: res.data };
  }
  async function handleStores() {
    const res = await apiRequest("/api/react/v1/browser-extension/stores", { method: "GET" });
    if (!res.success) return { ok: false, error: res.message || res.errorCode || "\u95E8\u5E97\u83B7\u53D6\u5931\u8D25" };
    return { ok: true, data: res.data };
  }
  async function handleSupplierTopSales({ supplierCode, days }) {
    if (!supplierCode) return { ok: false, error: "\u4F9B\u5E94\u5546\u4EE3\u7801\u7F3A\u5931" };
    const normalizedDays = Number(days) === 90 ? 90 : 60;
    const res = await apiRequest("/api/react/v1/browser-extension/supplier-top-sales", {
      method: "POST",
      body: JSON.stringify({ supplierCode, days: normalizedDays })
    });
    if (!res.success) return { ok: false, error: res.message || res.errorCode || "\u70ED\u9500\u6392\u884C\u83B7\u53D6\u5931\u8D25" };
    return { ok: true, data: res.data, apiOrigin: await getApiOrigin() };
  }
  async function handleActiveSupplier() {
    let tabs = [];
    try {
      tabs = await assistantPanel.queryActiveTabs();
    } catch {
      return { ok: true, supplier: null };
    }
    const href = tabs[0] && tabs[0].url;
    if (!href) return { ok: true, supplier: null };
    let url;
    try {
      url = new URL(href);
    } catch {
      return { ok: true, supplier: null };
    }
    const storedConfig = await migrateStoredProfiles();
    const validation = validateProfiles(storedConfig);
    const profile = validation.valid ? matchProfile(validation.profiles, { origin: url.origin, pathname: url.pathname }) : null;
    return {
      ok: true,
      supplier: profile ? { supplierCode: profile.supplierCode, displayName: profile.displayName } : null
    };
  }
  async function syncContentScripts() {
    const stored = await getLocal([GRANTED_KEY, PROFILES_KEY]);
    const granted = Array.isArray(stored[GRANTED_KEY]) ? stored[GRANTED_KEY] : [];
    const validation = validateProfiles(stored[PROFILES_KEY]);
    const origins = await resolveGrantedProfileOrigins(
      validation.valid ? validation.profiles : [],
      (origin) => chrome.permissions.contains({ origins: [origin] })
    );
    if (origins.length !== granted.length || origins.some((origin, index) => origin !== granted[index])) {
      await setLocal({ [GRANTED_KEY]: origins });
    }
    try {
      await chrome.scripting.unregisterContentScripts({ ids: ["hb-supplier-list"] });
    } catch {
    }
    if (origins.length) {
      await chrome.scripting.registerContentScripts([
        {
          id: "hb-supplier-list",
          matches: origins,
          js: ["content/list.js"],
          runAt: "document_idle",
          allFrames: false
        }
      ]);
    }
  }
  async function handleRegisterOrigin({ originPattern }) {
    if (typeof originPattern !== "string" || !originPattern) {
      return { ok: false, error: "origin \u7F3A\u5931" };
    }
    const stored = await getLocal([GRANTED_KEY, PROFILES_KEY]);
    const validation = validateProfiles(stored[PROFILES_KEY]);
    const allowedOrigins = new Set(
      (validation.valid ? validation.profiles : []).filter((profile) => profile.enabled !== false).flatMap((profile) => profile.origins || [])
    );
    if (!allowedOrigins.has(originPattern)) {
      return { ok: false, error: "origin \u4E0D\u5728\u5DF2\u542F\u7528\u4F9B\u5E94\u5546\u914D\u7F6E\u4E2D" };
    }
    if (!await chrome.permissions.contains({ origins: [originPattern] })) {
      return { ok: false, error: "origin \u5C1A\u672A\u83B7\u5F97\u6D4F\u89C8\u5668\u6388\u6743" };
    }
    const granted = stored[GRANTED_KEY];
    const set = new Set(Array.isArray(granted) ? granted : []);
    set.add(originPattern);
    const grantedList = [...set];
    await setLocal({ [GRANTED_KEY]: grantedList });
    await syncContentScripts();
    return { ok: true, granted: grantedList };
  }
  function friendlySidePanelError(e) {
    const msg = String(e && e.message || e);
    if (/user gesture/i.test(msg)) return "\u9700\u8981\u7528\u6237\u64CD\u4F5C";
    if (/tab/i.test(msg)) return "\u672A\u627E\u5230\u6807\u7B7E\u9875";
    return msg || "\u6253\u5F00\u4FA7\u680F\u5931\u8D25";
  }
  function openSidePanel(sender, pendingLocate) {
    const tabId = sender && sender.tab && sender.tab.id;
    if (tabId == null) return Promise.resolve({ ok: false, error: "\u7F3A\u5C11\u6807\u7B7E\u9875" });
    const openPromise = assistantPanel.open({ tabId });
    const locatePromise = pendingLocate ? chrome.storage.session.set({ pendingLocate }) : Promise.resolve();
    return Promise.all([openPromise, locatePromise]).then(() => ({ ok: true })).catch((e) => ({ ok: false, error: friendlySidePanelError(e) }));
  }
  chrome.runtime.onInstalled.addListener(async () => {
    try {
      await assistantPanel.configureAction();
    } catch {
    }
    const existing = await migrateStoredProfiles();
    if (!existing) await setLocal({ [PROFILES_KEY]: DEFAULT_PROFILES });
    await syncContentScripts();
  });
  chrome.runtime.onStartup.addListener(async () => {
    await migrateStoredProfiles();
    await syncContentScripts();
  });
  chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
    const type = message && message.type;
    const run = async () => {
      switch (type) {
        case "LOGIN":
          return handleLogin(message);
        case "GET_API_ORIGIN":
          return handleGetApiOrigin();
        case "SET_API_ORIGIN":
          return handleSetApiOrigin(message);
        case "CURRENT":
          return handleCurrent();
        case "LOGOUT":
          return handleLogout();
        case "RELEASE":
          return handleRelease();
        case "GET_PROFILES":
          return handleGetProfiles();
        case "SUMMARY_BATCH":
          return handleSummaryBatch(message);
        case "PURCHASE_CYCLES":
          return handlePurchaseCycles(message);
        case "GET_STORES":
          return handleStores();
        case "SUPPLIER_TOP_SALES":
          return handleSupplierTopSales(message);
        case "ACTIVE_SUPPLIER":
          return handleActiveSupplier();
        case "REGISTER_ORIGIN":
          return handleRegisterOrigin(message);
        case "OPEN_SIDE_PANEL":
          return openSidePanel(sender);
        case "LOCATE_ITEM":
          return openSidePanel(sender, {
            storeCode: message.storeCode,
            supplierCode: message.supplierCode,
            itemNumber: message.itemNumber
          });
        default:
          return { ok: false, error: "\u672A\u77E5\u6D88\u606F\u7C7B\u578B" };
      }
    };
    run().then(sendResponse).catch((err) => sendResponse({ ok: false, error: String(err && err.message || err) }));
    return true;
  });
})();
