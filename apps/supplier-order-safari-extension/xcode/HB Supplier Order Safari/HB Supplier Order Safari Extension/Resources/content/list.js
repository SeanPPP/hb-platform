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

  // src/lib/transforms.js
  var transforms_exports = {};
  __export(transforms_exports, {
    ALLOWED_TRANSFORMS: () => ALLOWED_TRANSFORMS,
    applyTransform: () => applyTransform,
    applyTransforms: () => applyTransforms,
    isTransformAllowed: () => isTransformAllowed,
    normalizeTransform: () => normalizeTransform,
    safeTransformList: () => safeTransformList
  });
  function isTransformAllowed(type) {
    return ALLOWED_TRANSFORMS.has(type);
  }
  function normalizeTransform(transform) {
    return typeof transform === "string" ? { type: transform } : transform;
  }
  function applyTransform(value, transform) {
    const normalized = normalizeTransform(transform);
    const t2 = normalized && normalized.type;
    if (!isTransformAllowed(t2)) {
      throw new Error(`unsupported transform: ${String(t2)}`);
    }
    const s = value == null ? "" : String(value);
    switch (t2) {
      case "trim":
        return s.trim();
      case "uppercase":
        return s.toUpperCase();
      case "lowercase":
        return s.toLowerCase();
      case "after-colon": {
        const colonIndex = s.indexOf(":");
        return colonIndex === -1 ? "" : s.slice(colonIndex + 1).trim();
      }
      case "underscore-to-slash":
        return s.replaceAll("_", "/");
      case "after-sku": {
        const match = /^\s*-?\s*SKU\s+(.+)$/i.exec(s);
        return match ? match[1].trim() : "";
      }
      default:
        throw new Error(`unsupported transform: ${String(t2)}`);
    }
  }
  function applyTransforms(value, transforms) {
    let out = value;
    for (const t2 of transforms || []) {
      out = applyTransform(out, t2);
    }
    return out;
  }
  function safeTransformList(transforms) {
    if (transforms == null) return true;
    if (!Array.isArray(transforms)) return false;
    return transforms.every((transform) => {
      const normalized = normalizeTransform(transform);
      return !!normalized && isTransformAllowed(normalized.type);
    });
  }
  var ALLOWED_TRANSFORMS;
  var init_transforms = __esm({
    "src/lib/transforms.js"() {
      ALLOWED_TRANSFORMS = /* @__PURE__ */ new Set([
        "trim",
        "uppercase",
        "lowercase",
        "after-colon",
        "underscore-to-slash",
        "after-sku"
      ]);
    }
  });

  // src/lib/profiles.js
  var profiles_exports = {};
  __export(profiles_exports, {
    ALLOWED_MOUNT_POSITIONS: () => ALLOWED_MOUNT_POSITIONS,
    ALLOWED_SOURCES: () => ALLOWED_SOURCES,
    matchProfile: () => matchProfile,
    matchUrlPattern: () => matchUrlPattern,
    matchesListPage: () => matchesListPage,
    originMatchesAny: () => originMatchesAny,
    validateProfiles: () => validateProfiles
  });
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
  function matchesListPage(listPagePatterns, href) {
    return (listPagePatterns || []).some((pattern) => matchUrlPattern(pattern, href));
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
  var ALLOWED_SOURCES, ALLOWED_MOUNT_POSITIONS, TXK_HTTP_PATTERN;
  var init_profiles = __esm({
    "src/lib/profiles.js"() {
      init_transforms();
      ALLOWED_SOURCES = /* @__PURE__ */ new Set(["attribute", "text"]);
      ALLOWED_MOUNT_POSITIONS = /* @__PURE__ */ new Set(["beforebegin", "afterbegin", "beforeend", "afterend"]);
      TXK_HTTP_PATTERN = /^http:\/\/txkorders\.inzantsales\.com(?<path>\/[^\s]*)$/i;
    }
  });

  // src/lib/batch.js
  var batch_exports = {};
  __export(batch_exports, {
    createBatchQueue: () => createBatchQueue
  });
  function createBatchQueue({
    flush,
    maxSize = 100,
    delayMs = 150,
    cacheTtlMs = 6e4,
    schedule = (fn) => setTimeout(fn, delayMs),
    cancel = clearTimeout
  } = {}) {
    const pending = /* @__PURE__ */ new Map();
    const cache = /* @__PURE__ */ new Map();
    let timer = null;
    let flushing = false;
    const now = () => Date.now();
    function readCache(key) {
      const c = cache.get(key);
      if (!c) return void 0;
      if (c.expiresAt <= now()) {
        cache.delete(key);
        return void 0;
      }
      return c.value;
    }
    function scheduleFlush() {
      if (timer !== null) return;
      timer = schedule(drain);
    }
    async function drain() {
      if (flushing) return;
      flushing = true;
      try {
        while (pending.size > 0) {
          const batch = [];
          for (const [key, entry] of pending) {
            if (batch.length >= maxSize) break;
            batch.push(entry);
            pending.delete(key);
          }
          let results;
          try {
            results = await flush(batch.map((e) => ({ key: e.key, item: e.item })));
          } catch (err) {
            for (const entry of batch) entry.reject(err);
            for (const entry of pending.values()) entry.reject(err);
            pending.clear();
            return;
          }
          for (const entry of batch) {
            const val = results instanceof Map ? results.get(entry.key) : results && results[entry.key];
            cache.set(entry.key, { value: val, expiresAt: now() + cacheTtlMs });
            entry.resolve(val);
          }
        }
      } finally {
        flushing = false;
        timer = null;
      }
    }
    function enqueue(key, item) {
      const cached = readCache(key);
      if (cached !== void 0) return Promise.resolve(cached);
      const existing = pending.get(key);
      if (existing) return existing.promise;
      let resolve;
      let reject;
      const promise = new Promise((res, rej) => {
        resolve = res;
        reject = rej;
      });
      pending.set(key, { key, item, resolve, reject, promise });
      scheduleFlush();
      return promise;
    }
    return {
      enqueue,
      flushNow: () => {
        if (timer !== null) {
          cancel(timer);
          timer = null;
        }
        return drain();
      },
      pendingSize: () => pending.size,
      cacheSize: () => cache.size,
      clearCache: () => cache.clear()
    };
  }
  var init_batch = __esm({
    "src/lib/batch.js"() {
    }
  });

  // src/lib/ranking.js
  var ranking_exports = {};
  __export(ranking_exports, {
    beginRankingLoad: () => beginRankingLoad,
    buildDefaultProductImageUrl: () => buildDefaultProductImageUrl,
    buildProductImageCandidates: () => buildProductImageCandidates,
    formatAverageSellingPrice: () => formatAverageSellingPrice,
    formatSalesRankBand: () => formatSalesRankBand,
    normalizeRankingDays: () => normalizeRankingDays,
    normalizeRankingPageSize: () => normalizeRankingPageSize,
    normalizeSalesRankBand: () => normalizeSalesRankBand,
    normalizeStoreOptions: () => normalizeStoreOptions,
    normalizeSupplierOptions: () => normalizeSupplierOptions,
    normalizeTopSalesPage: () => normalizeTopSalesPage,
    normalizeTopSalesRequest: () => normalizeTopSalesRequest,
    paginateRanking: () => paginateRanking,
    resolveRankingRetryTarget: () => resolveRankingRetryTarget,
    resolveRankingViewState: () => resolveRankingViewState,
    restoreRankingLoad: () => restoreRankingLoad,
    shouldPreserveManualSupplier: () => shouldPreserveManualSupplier,
    transitionRankingPagination: () => transitionRankingPagination
  });
  function normalizeStoreOptions(data) {
    const stores = data && Array.isArray(data.stores) ? data.stores : [];
    const seen = /* @__PURE__ */ new Set();
    const result = [];
    for (const store of stores) {
      if (!store || typeof store !== "object") continue;
      const code = String(store.storeCode ?? store.code ?? "").trim();
      if (!code || seen.has(code.toUpperCase())) continue;
      seen.add(code.toUpperCase());
      const name = String(store.storeName ?? store.name ?? code).trim() || code;
      result.push({ code, name });
    }
    return result;
  }
  function normalizeRankingDays(value) {
    return Number(value) === 90 ? 90 : 60;
  }
  function normalizeRankingPageSize(value) {
    const numericValue = Number(value);
    return RANKING_PAGE_SIZES.has(numericValue) ? numericValue : DEFAULT_RANKING_PAGE_SIZE;
  }
  function normalizeSalesRankBand(value) {
    const normalized = typeof value === "string" ? value.trim().toLowerCase() : "";
    return SALES_RANK_BANDS.has(normalized) ? normalized : null;
  }
  function formatSalesRankBand(value) {
    const normalized = normalizeSalesRankBand(value);
    if (!normalized) return "";
    return `TOP ${normalized.slice(4)}%`;
  }
  function transitionRankingPagination(state, action) {
    const pageSize = normalizeRankingPageSize(state?.pageSize);
    if (action?.type === "page-size") {
      return { page: 1, pageSize: normalizeRankingPageSize(action.pageSize) };
    }
    if (action?.type === "context") return { page: 1, pageSize };
    const requestedPage = Number(action?.page);
    const page = Number.isFinite(requestedPage) ? Math.max(1, Math.trunc(requestedPage)) : 1;
    return { page, pageSize };
  }
  function beginRankingLoad({
    page = 1,
    pageSize = DEFAULT_RANKING_PAGE_SIZE,
    data = null,
    legacyItems = null
  } = {}, { clear = false } = {}) {
    const requestedPage = Math.max(1, Math.trunc(Number(page)) || 1);
    const requestedPageSize = normalizeRankingPageSize(pageSize);
    const checkpoint = {
      page: Number.isInteger(data?.page) && data.page >= 1 ? data.page : requestedPage,
      // pageSize 是用户偏好；请求失败时保留新选择，旧页数据仅作为可见回退内容。
      pageSize: requestedPageSize,
      data,
      legacyItems
    };
    return {
      checkpoint,
      state: {
        page: requestedPage,
        pageSize: requestedPageSize,
        data: clear ? null : data,
        legacyItems: clear ? null : legacyItems,
        loading: true,
        error: null
      }
    };
  }
  function restoreRankingLoad(checkpoint, error) {
    return {
      page: Math.max(1, Math.trunc(Number(checkpoint?.page)) || 1),
      pageSize: normalizeRankingPageSize(checkpoint?.pageSize),
      data: checkpoint?.data ?? null,
      legacyItems: checkpoint?.legacyItems ?? null,
      loading: false,
      error: String(error && error.message || error || "")
    };
  }
  function resolveRankingRetryTarget(target, { supplierCode, days } = {}) {
    const targetSupplierCode = String(target?.supplierCode || "").trim().toUpperCase();
    const currentSupplierCode = String(supplierCode || "").trim().toUpperCase();
    const targetDays = Number(target?.days);
    const page = Number(target?.page);
    const pageSize = Number(target?.pageSize);
    if (!targetSupplierCode || targetSupplierCode !== currentSupplierCode || ![60, 90].includes(targetDays) || targetDays !== normalizeRankingDays(days) || !Number.isInteger(page) || page < 1 || normalizeRankingPageSize(pageSize) !== pageSize) {
      return null;
    }
    return { page, pageSize };
  }
  function resolveRankingViewState({
    hasSupplier,
    loading,
    error,
    totalRankedCount
  } = {}) {
    if (!hasSupplier) return "no-supplier";
    if (loading) return "loading";
    if (error) return "error";
    if (totalRankedCount === 0) return "empty";
    if (Number(totalRankedCount) > 0) return "content";
    return "idle";
  }
  function normalizeTopSalesRequest({ topPercent, page, pageSize } = {}) {
    const providedCount = [topPercent, page, pageSize].filter((value) => value != null).length;
    if (providedCount === 0) return null;
    const numericTopPercent = Number(topPercent);
    const numericPage = Number(page);
    const numericPageSize = Number(pageSize);
    if (providedCount !== 3 || numericTopPercent !== 30 || !Number.isInteger(numericPage) || numericPage < 1 || normalizeRankingPageSize(numericPageSize) !== numericPageSize) {
      throw new Error("\u65E0\u6548\u7684\u70ED\u9500\u699C\u5206\u9875\u53C2\u6570");
    }
    return {
      topPercent: numericTopPercent,
      page: numericPage,
      pageSize: numericPageSize
    };
  }
  function normalizeSupplierOptions(profiles) {
    const seen = /* @__PURE__ */ new Set();
    const result = [];
    for (const profile of Array.isArray(profiles) ? profiles : []) {
      if (!profile || typeof profile !== "object") continue;
      const code = String(profile.supplierCode ?? "").trim();
      const key = code.toUpperCase();
      if (!code || seen.has(key)) continue;
      seen.add(key);
      const name = String(profile.displayName ?? code).trim() || code;
      result.push({ code, name });
    }
    return result;
  }
  function shouldPreserveManualSupplier({
    manualSupplierCode,
    detectedSupplierCode,
    previousDetectedSupplierCode
  }) {
    if (!String(manualSupplierCode || "").trim()) return false;
    const detectedCode = String(detectedSupplierCode || "").trim();
    const previousCode = String(previousDetectedSupplierCode || "").trim();
    return !detectedCode || detectedCode.toUpperCase() === previousCode.toUpperCase();
  }
  function paginateRanking(items, requestedPage, pageSize = DEFAULT_RANKING_PAGE_SIZE) {
    const source = Array.isArray(items) ? items : [];
    const normalizedPageSize = normalizeRankingPageSize(pageSize);
    const totalPages = Math.max(1, Math.ceil(source.length / normalizedPageSize));
    const numericPage = Number.isFinite(Number(requestedPage)) ? Math.trunc(Number(requestedPage)) : 1;
    const page = Math.min(totalPages, Math.max(1, numericPage));
    const start = (page - 1) * normalizedPageSize;
    return {
      items: source.slice(start, start + normalizedPageSize),
      page,
      totalPages,
      totalItems: source.length,
      pageSize: normalizedPageSize
    };
  }
  function normalizeTopSalesPage(raw, {
    requestedPage = 1,
    requestedPageSize = DEFAULT_RANKING_PAGE_SIZE,
    requestedSupplierCode = null,
    requestedDays = null
  } = {}) {
    if (!raw || typeof raw !== "object" || Array.isArray(raw) || !Array.isArray(raw.items)) {
      throw new Error("\u65E0\u6548\u7684\u70ED\u9500\u699C\u5206\u9875\u54CD\u5E94");
    }
    const data = raw;
    const source = data.items;
    const topPercent = data.topPercent;
    const responsePage = data.page;
    const responsePageSize = data.pageSize;
    const responseTotalPages = data.totalPages;
    const totalProductCount = data.totalProductCount;
    const expectedSupplierCode = String(requestedSupplierCode || "").trim().toUpperCase();
    const responseSupplierCode = String(data.supplierCode || "").trim().toUpperCase();
    const hasMatchingContext = (!expectedSupplierCode || responseSupplierCode === expectedSupplierCode) && (requestedDays == null || data.days === normalizeRankingDays(requestedDays));
    const isLegacy = (topPercent == null || topPercent === 10) && responsePage == null && responsePageSize == null && responseTotalPages == null;
    if (isLegacy) {
      const expectedLegacyTotal = Number.isInteger(totalProductCount) && totalProductCount >= 0 ? Math.ceil(totalProductCount * 0.1) : -1;
      const legacyTotal = data.totalRankedCount == null ? source.length : data.totalRankedCount;
      const hasValidRanks = source.every(
        (item, index) => item && typeof item === "object" && item.rank === index + 1 && (item.salesRankBand == null || item.salesRankBand === "top-10")
      );
      if (!hasMatchingContext || expectedLegacyTotal < 0 || !Number.isInteger(legacyTotal) || legacyTotal !== expectedLegacyTotal || source.length !== expectedLegacyTotal || !hasValidRanks) {
        throw new Error("\u65E0\u6548\u7684\u70ED\u9500\u699C\u5206\u9875\u54CD\u5E94");
      }
      const paged = paginateRanking(source, requestedPage, requestedPageSize);
      return {
        mode: "legacy",
        topPercent: 10,
        items: paged.items,
        totalRankedCount: paged.totalItems,
        page: paged.page,
        pageSize: paged.pageSize,
        totalPages: paged.totalPages
      };
    }
    if (topPercent !== 30 || !hasMatchingContext || !Number.isInteger(totalProductCount) || totalProductCount < 0 || !Number.isInteger(data.totalRankedCount) || data.totalRankedCount < 0 || !Number.isInteger(responsePage) || responsePage < 1 || !Number.isInteger(responsePageSize) || normalizeRankingPageSize(responsePageSize) !== responsePageSize || !Number.isInteger(responseTotalPages) || responseTotalPages < 0 || normalizeRankingPageSize(requestedPageSize) !== Number(requestedPageSize) || responsePageSize !== Number(requestedPageSize) || !Number.isInteger(Number(requestedPage)) || Number(requestedPage) < 1) {
      throw new Error("\u65E0\u6548\u7684\u70ED\u9500\u699C\u5206\u9875\u54CD\u5E94");
    }
    const totalRankedCount = data.totalRankedCount;
    const expectedTotalRankedCount = Math.ceil(totalProductCount * 0.3);
    const expectedTotalPages = totalRankedCount === 0 ? 0 : Math.ceil(totalRankedCount / responsePageSize);
    const expectedPage = expectedTotalPages === 0 ? 1 : Math.min(Number(requestedPage), expectedTotalPages);
    const expectedItemCount = totalRankedCount === 0 ? 0 : Math.min(responsePageSize, totalRankedCount - (responsePage - 1) * responsePageSize);
    const firstExpectedRank = (responsePage - 1) * responsePageSize + 1;
    const hasValidItems = source.length === expectedItemCount && source.every(
      (item, index) => item && typeof item === "object" && item.rank === firstExpectedRank + index && item.salesRankBand === (item.rank <= Math.ceil(totalProductCount * 0.1) ? "top-10" : item.rank <= Math.ceil(totalProductCount * 0.2) ? "top-20" : "top-30")
    );
    if (totalRankedCount !== expectedTotalRankedCount || responseTotalPages !== expectedTotalPages || responsePage !== expectedPage || !hasValidItems) {
      throw new Error("\u65E0\u6548\u7684\u70ED\u9500\u699C\u5206\u9875\u54CD\u5E94");
    }
    return {
      mode: "server",
      topPercent: 30,
      items: source,
      totalRankedCount,
      page: responsePage,
      pageSize: responsePageSize,
      totalPages: responseTotalPages
    };
  }
  function formatAverageSellingPrice(value) {
    if (value == null || value === "") return "\u2014";
    const numericValue = Number(value);
    if (!Number.isFinite(numericValue)) return "\u2014";
    return new Intl.NumberFormat("en-AU", {
      style: "currency",
      currency: "AUD",
      minimumFractionDigits: 2,
      maximumFractionDigits: 2
    }).format(numericValue);
  }
  function buildDefaultProductImageUrl(itemNumber, productCode) {
    const imageKey = String(itemNumber || productCode || "").trim();
    return imageKey ? `${DEFAULT_PRODUCT_IMAGE_BASE_URL}/${encodeURIComponent(imageKey)}.jpg` : "";
  }
  function toAbsoluteUrl(value, apiOrigin) {
    const raw = String(value || "").trim();
    if (!raw) return "";
    try {
      return new URL(raw, `${String(apiOrigin || "").replace(/\/$/, "")}/`).href;
    } catch {
      return "";
    }
  }
  function buildProductImageCandidates(item, apiOrigin) {
    const candidates = [
      toAbsoluteUrl(item && item.imageUrl, apiOrigin),
      buildDefaultProductImageUrl(item && item.itemNumber),
      buildDefaultProductImageUrl(item && item.productCode)
    ].filter(Boolean);
    return [...new Set(candidates)];
  }
  var DEFAULT_PRODUCT_IMAGE_BASE_URL, DEFAULT_RANKING_PAGE_SIZE, RANKING_PAGE_SIZES, SALES_RANK_BANDS;
  var init_ranking = __esm({
    "src/lib/ranking.js"() {
      DEFAULT_PRODUCT_IMAGE_BASE_URL = "https://hotbargain-yw-2023-1300114625.cos.ap-shanghai.myqcloud.com/YW200";
      DEFAULT_RANKING_PAGE_SIZE = 50;
      RANKING_PAGE_SIZES = /* @__PURE__ */ new Set([50, 100, 200]);
      SALES_RANK_BANDS = /* @__PURE__ */ new Set(["top-10", "top-20", "top-30"]);
    }
  });

  // src/lib/dats-state.js
  var dats_state_exports = {};
  __export(dats_state_exports, {
    buildSummaryCacheKey: () => buildSummaryCacheKey,
    computeButtonState: () => computeButtonState,
    createGenerationGuard: () => createGenerationGuard,
    createNodeStateRegistry: () => createNodeStateRegistry,
    needsProcessing: () => needsProcessing,
    normalizeSummaryItem: () => normalizeSummaryItem,
    normalizeSummaryMap: () => normalizeSummaryMap,
    shouldInjectList: () => shouldInjectList
  });
  function createGenerationGuard(initial = 0) {
    let gen = initial;
    return {
      current: () => gen,
      advance: () => ++gen,
      isCurrent: (g) => g === gen
    };
  }
  function createNodeStateRegistry() {
    const map = /* @__PURE__ */ new WeakMap();
    return {
      set: (node, state) => map.set(node, state),
      get: (node) => map.get(node),
      has: (node) => map.has(node),
      delete: (node) => map.delete(node)
    };
  }
  function needsProcessing(node, registry, generation) {
    const state = registry.get(node);
    return !state || state.generation !== generation;
  }
  function shouldInjectList({ href, listPagePatterns, cardCount, isDetailPage = false }) {
    if (isDetailPage || cardCount < 1) return false;
    const patterns = Array.isArray(listPagePatterns) ? listPagePatterns : [];
    if (patterns.length > 0) {
      return matchesListPage(patterns, href);
    }
    return cardCount > 1;
  }
  function computeButtonState(item) {
    if (!item) return { kind: "none", reason: "noMatch" };
    if (item.error) return { kind: "error", reason: "error" };
    if (item.hasMatch === false) return { kind: "none", reason: "noMatch" };
    const salesRankBand = normalizeSalesRankBand(item.salesRankBand);
    const salesRankingDays = salesRankBand ? normalizeRankingDays(item.salesRankingDays) : null;
    if (item.hasPurchase === false) {
      return {
        kind: "none",
        reason: "noPurchase",
        ...salesRankBand ? { salesRankBand, salesRankingDays } : {}
      };
    }
    return {
      kind: "ok",
      lastOrderDate: item.lastOrderDate,
      lastOrderQuantity: item.lastOrderQuantity,
      salesToDate: item.salesToDate,
      ...salesRankBand ? { salesRankBand, salesRankingDays } : {}
    };
  }
  function buildSummaryCacheKey(storeCode, itemNumber, salesRankingDays) {
    const normalizedStoreCode = String(storeCode || "none").trim() || "none";
    const normalizedItemNumber = String(itemNumber || "").trim();
    return `${normalizedStoreCode}:${normalizeRankingDays(salesRankingDays)}:${normalizedItemNumber}`;
  }
  function normalizeSummaryItem(raw, { salesRankingAvailable = false } = {}) {
    if (!raw || typeof raw !== "object") return { hasMatch: false };
    if (raw.error) return { error: raw.error };
    const matchStatus = typeof raw.matchStatus === "string" ? raw.matchStatus.toLowerCase() : null;
    const hasMatch = matchStatus ? matchStatus !== "unmatched" : raw.hasMatch !== false;
    const hasPurchase = matchStatus ? matchStatus === "matched" : raw.hasPurchase != null ? raw.hasPurchase : raw.lastOrderDate != null || raw.latestPurchaseDate != null || raw.lastOrderQuantity != null || raw.latestPurchaseQuantity != null || raw.orderCount > 0;
    const normalized = {
      hasMatch,
      hasPurchase: !!hasPurchase,
      lastOrderDate: raw.latestPurchaseDate ?? raw.lastOrderDate ?? raw.lastOrderDateStr ?? null,
      lastOrderQuantity: raw.latestPurchaseQuantity ?? raw.lastOrderQuantity ?? raw.lastOrderQty ?? null,
      salesToDate: raw.salesSinceLatestPurchase ?? raw.salesToDate ?? raw.salesQty ?? null
    };
    const salesRankBand = normalizeSalesRankBand(raw.salesRankBand);
    if (salesRankingAvailable === true && hasMatch && salesRankBand) {
      normalized.salesRankBand = salesRankBand;
    }
    return normalized;
  }
  function normalizeSummaryMap(rawData) {
    const out = {};
    if (!rawData) return out;
    if (Array.isArray(rawData)) {
      for (const item of rawData) {
        if (item && item.itemNumber) out[item.itemNumber] = normalizeSummaryItem(item);
      }
      return out;
    }
    if (Array.isArray(rawData.items)) {
      const options = { salesRankingAvailable: rawData.salesRankingAvailable === true };
      for (const item of rawData.items) {
        if (item && item.itemNumber) out[item.itemNumber] = normalizeSummaryItem(item, options);
      }
      return out;
    }
    if (typeof rawData === "object") {
      for (const [k, v] of Object.entries(rawData)) {
        out[k] = normalizeSummaryItem(v);
      }
      return out;
    }
    return out;
  }
  var init_dats_state = __esm({
    "src/lib/dats-state.js"() {
      init_profiles();
      init_ranking();
    }
  });

  // src/lib/i18n.js
  var i18n_exports = {};
  __export(i18n_exports, {
    DEFAULT_LOCALE: () => DEFAULT_LOCALE,
    MESSAGES: () => MESSAGES,
    SUPPORTED_LOCALES: () => SUPPORTED_LOCALES,
    normalizeLocale: () => normalizeLocale,
    resolveInitialLocale: () => resolveInitialLocale,
    t: () => t
  });
  function normalizeLocale(locale) {
    return SUPPORTED_LOCALES.includes(locale) ? locale : DEFAULT_LOCALE;
  }
  function resolveInitialLocale(storedLocale, preferredLanguages = []) {
    if (SUPPORTED_LOCALES.includes(storedLocale)) {
      return storedLocale;
    }
    const languages = Array.isArray(preferredLanguages) ? preferredLanguages : [preferredLanguages];
    const systemLocale = languages.find(
      (language) => typeof language === "string" && language.trim()
    );
    return /^zh(?:-|$)/i.test(systemLocale?.trim() || "") ? "zh" : "en";
  }
  function t(locale, key) {
    const dict = MESSAGES[locale] || MESSAGES[DEFAULT_LOCALE];
    return dict[key] != null ? dict[key] : key;
  }
  var SUPPORTED_LOCALES, DEFAULT_LOCALE, MESSAGES;
  var init_i18n = __esm({
    "src/lib/i18n.js"() {
      SUPPORTED_LOCALES = ["zh", "en"];
      DEFAULT_LOCALE = "zh";
      MESSAGES = {
        zh: {
          title: "HB \u4F9B\u5E94\u5546\u8BA2\u8D27\u52A9\u624B",
          apiTitle: "\u540E\u7AEF\u63A5\u53E3",
          apiRemote: "\u8FDC\u7AEF /",
          apiLocal: "\u672C\u5730 5002",
          apiApply: "\u5E94\u7528",
          apiPlaceholder: "https://api.example.com",
          apiHint: "\u4EC5\u4F7F\u7528\u53EF\u4FE1 HB \u63A5\u53E3\uFF1B\u8F93\u5165 / \u6062\u590D\u8FDC\u7AEF",
          apiSaved: "\u540E\u7AEF\u63A5\u53E3\u5DF2\u4FDD\u5B58",
          apiSwitched: "\u540E\u7AEF\u63A5\u53E3\u5DF2\u5207\u6362\uFF0C\u6B63\u5728\u91CD\u65B0\u68C0\u67E5\u7F51\u7AD9\u4F1A\u8BDD",
          apiInvalid: "\u8BF7\u8F93\u5165\u6709\u6548\u7684 HTTPS \u5730\u5740\u6216\u672C\u673A HTTP \u5730\u5740",
          apiPermissionDenied: "\u672A\u6388\u6743\u8BBF\u95EE\u8BE5\u63A5\u53E3\u5730\u5740",
          sessionCheckingTitle: "\u6B63\u5728\u68C0\u67E5\u7F51\u7AD9\u767B\u5F55\u72B6\u6001",
          sessionCheckingDescription: "\u5C06\u4F7F\u7528\u5F53\u524D HB SHOP \u7F51\u7AD9\u8D26\u53F7\u8FDE\u63A5\u6269\u5C55\u3002",
          sessionNeedsWebsiteTitle: "\u8BF7\u6253\u5F00\u6216\u767B\u5F55 HB SHOP",
          sessionNeedsWebsiteDescription: "\u7F51\u7AD9\u767B\u5F55\u540E\u8FD4\u56DE\u6269\u5C55\u5E76\u91CD\u65B0\u68C0\u67E5\uFF0C\u65E0\u9700\u5728\u6269\u5C55\u8F93\u5165\u5BC6\u7801\u3002",
          sessionConnectedTitle: "\u5DF2\u8FDE\u63A5\u7F51\u7AD9\u8D26\u53F7",
          openShop: "\u6253\u5F00 HB SHOP",
          recheckSession: "\u91CD\u65B0\u68C0\u67E5",
          disconnectExtension: "\u65AD\u5F00\u6269\u5C55",
          apiOriginMismatch: "\u5F53\u524D\u63A5\u53E3\u4E0E HB SHOP \u7F51\u9875\u4E0D\u540C\u6E90\uFF0C\u8BF7\u5207\u56DE\u8FDC\u7AEF\u63A5\u53E3\u6216\u5728\u5BF9\u5E94\u7F51\u9875\u73AF\u5883\u767B\u5F55\u3002",
          save: "\u4FDD\u5B58",
          store: "\u95E8\u5E97",
          storeCode: "\u95E8\u5E97\u7F16\u7801",
          supplier: "\u4F9B\u5E94\u5546",
          supplierExpand: "\u5C55\u5F00\u5DF2\u6388\u6743\uFF08{count}\uFF09",
          supplierCollapse: "\u6536\u8D77\u5DF2\u6388\u6743",
          supplierCollapsedHint: "\u5DF2\u9690\u85CF {count} \u4E2A\u5DF2\u6388\u6743\u7F51\u7AD9\uFF0C\u672A\u6388\u6743\u7F51\u7AD9\u59CB\u7EC8\u663E\u793A\u3002",
          grant: "\u6388\u6743",
          granted: "\u5DF2\u6388\u6743",
          grantSuccess: "\u6388\u6743\u6210\u529F",
          grantDenied: "\u672A\u6388\u6743",
          grantFailed: "\u6388\u6743\u5931\u8D25",
          storeSaved: "\u5DF2\u4FDD\u5B58\u95E8\u5E97",
          noPosStore: "\u6682\u65E0\u5DF2\u542F\u7528 POS \u7684\u5173\u8054\u95E8\u5E97",
          historyTab: "\u5546\u54C1\u8BB0\u5F55",
          rankingTab: "\u70ED\u9500 TOP {percent}%",
          rankingTitle: "\u4F9B\u5E94\u5546\u70ED\u9500 TOP {percent}%",
          rankingSupplier: "\u6392\u540D\u4F9B\u5E94\u5546",
          rankingChooseSupplier: "\u9009\u62E9\u4F9B\u5E94\u5546",
          rankingPeriod: "\u7EDF\u8BA1\u5468\u671F",
          days: "\u5929",
          rankingScope: "\u5168\u516C\u53F8 {stores} \u5BB6\u542F\u7528 POS \u95E8\u5E97 \xB7 {products} \u4E2A\u6709\u9500\u91CF\u5546\u54C1",
          rankingNoSupplier: "\u8BF7\u6253\u5F00\u53D7\u652F\u6301\u7684\u4F9B\u5E94\u5546\u5546\u54C1\u5217\u8868\u9875",
          rankingNoData: "\u8BE5\u4F9B\u5E94\u5546\u5728\u6240\u9009\u5468\u671F\u5185\u6682\u65E0\u9500\u552E\u6570\u636E",
          rankingPageSize: "\u6BCF\u9875",
          rankingPageSummary: "\u7B2C {page} / {totalPages} \u9875 \xB7 \u5171 {total} \u4E2A\u5546\u54C1",
          rankingLoading: "\u6B63\u5728\u52A0\u8F7D\u70ED\u9500\u699C\u2026",
          rankingLoadFailed: "\u70ED\u9500\u699C\u52A0\u8F7D\u5931\u8D25",
          rankingRetry: "\u91CD\u8BD5",
          rankingLegacyHint: "\u5F53\u524D\u670D\u52A1\u6682\u4EC5\u63D0\u4F9B TOP 10%\uFF0C\u5347\u7EA7\u540E\u53EF\u67E5\u770B TOP 30%\u3002",
          rankingPageChanged: "\u5DF2\u663E\u793A\u7B2C {page} / {totalPages} \u9875",
          copy: "\u590D\u5236",
          copied: "\u5DF2\u590D\u5236",
          averageSellingPrice: "\u5747\u4EF7",
          historyNoItem: "\u8BF7\u70B9\u51FB\u4F9B\u5E94\u5546\u5546\u54C1\u65C1\u7684\u8BB0\u5F55\u6309\u94AE\u67E5\u770B\u5546\u54C1\u5386\u53F2",
          all: "\u5168\u90E8",
          order: "\u8BA2\u8D27",
          sales: "\u9500\u552E",
          type: "\u7C7B\u578B",
          date: "\u65E5\u671F",
          orderNo: "\u5355\u53F7",
          quantity: "\u6570\u91CF",
          price: "\u5E73\u5747\u4EF7\u683C",
          page: "\u9875",
          prev: "\u4E0A\u4E00\u9875",
          next: "\u4E0B\u4E00\u9875",
          noData: "\u6682\u65E0\u6570\u636E",
          loading: "\u52A0\u8F7D\u4E2D\u2026",
          error: "\u52A0\u8F7D\u5931\u8D25",
          noMatch: "\u65E0\u5339\u914D",
          noPurchase: "\u65E0\u91C7\u8D2D",
          noStore: "\u8BF7\u5148\u9009\u62E9\u95E8\u5E97",
          lastOrder: "\u4E0A\u6B21\u8BA2\u8D27",
          salesToDate: "\u81F3\u4ECA\u9500\u91CF",
          salesRankBand: "\u8FD1 {days} \u5929\u9500\u91CF\uFF1A{band}"
        },
        en: {
          title: "HB Supplier Ordering Assistant",
          apiTitle: "Backend API",
          apiRemote: "Remote /",
          apiLocal: "Local 5002",
          apiApply: "Apply",
          apiPlaceholder: "https://api.example.com",
          apiHint: "Use trusted HB APIs only; enter / for remote",
          apiSaved: "Backend API saved",
          apiSwitched: "Backend API changed. Checking the website session again.",
          apiInvalid: "Enter a valid HTTPS or local HTTP origin",
          apiPermissionDenied: "Access to this API origin was not granted",
          sessionCheckingTitle: "Checking website sign-in",
          sessionCheckingDescription: "The extension will connect with the current HB SHOP website account.",
          sessionNeedsWebsiteTitle: "Open or sign in to HB SHOP",
          sessionNeedsWebsiteDescription: "After signing in on the website, return here and check again. No extension password is needed.",
          sessionConnectedTitle: "Connected to website account",
          openShop: "Open HB SHOP",
          recheckSession: "Check again",
          disconnectExtension: "Disconnect extension",
          apiOriginMismatch: "This API is not the same origin as HB SHOP. Switch back to Remote or sign in in the matching web environment.",
          save: "Save",
          store: "Store",
          storeCode: "Store code",
          supplier: "Supplier",
          supplierExpand: "Show granted ({count})",
          supplierCollapse: "Hide granted",
          supplierCollapsedHint: "{count} granted sites hidden. Sites awaiting permission always remain visible.",
          grant: "Grant",
          granted: "Granted",
          grantSuccess: "Permission granted",
          grantDenied: "Permission not granted",
          grantFailed: "Permission failed",
          storeSaved: "Store saved",
          noPosStore: "No related store has an enabled POS",
          historyTab: "Item history",
          rankingTab: "Top {percent}% sellers",
          rankingTitle: "Supplier top {percent}% sellers",
          rankingSupplier: "Ranking supplier",
          rankingChooseSupplier: "Choose supplier",
          rankingPeriod: "Period",
          days: "days",
          rankingScope: "{stores} enabled POS stores company-wide \xB7 {products} selling products",
          rankingNoSupplier: "Open a supported supplier product list",
          rankingNoData: "No sales data for this supplier in the selected period",
          rankingPageSize: "Per page",
          rankingPageSummary: "Page {page} of {totalPages} \xB7 {total} products",
          rankingLoading: "Loading ranking\u2026",
          rankingLoadFailed: "Could not load the ranking",
          rankingRetry: "Retry",
          rankingLegacyHint: "This service currently provides TOP 10% only. Upgrade it to view TOP 30%.",
          rankingPageChanged: "Showing page {page} of {totalPages}",
          copy: "Copy",
          copied: "Copied",
          averageSellingPrice: "Avg price",
          historyNoItem: "Select the history button beside a supplier product to view item history",
          all: "All",
          order: "Order",
          sales: "Sales",
          type: "Type",
          date: "Date",
          orderNo: "Reference no.",
          quantity: "Qty",
          price: "Average price",
          page: "Page",
          prev: "Prev",
          next: "Next",
          noData: "No data",
          loading: "Loading\u2026",
          error: "Load failed",
          noMatch: "No match",
          noPurchase: "No purchase",
          noStore: "Select a store first",
          lastOrder: "Last order",
          salesToDate: "Sales to date",
          salesRankBand: "Sales in the last {days} days: {band}"
        }
      };
    }
  });

  // src/lib/list-recovery.js
  var list_recovery_exports = {};
  __export(list_recovery_exports, {
    markSummaryRequestFailed: () => markSummaryRequestFailed,
    needsHostRemount: () => needsHostRemount,
    resetSummaryRetry: () => resetSummaryRetry,
    shouldRequestVisibleSummary: () => shouldRequestVisibleSummary
  });
  function needsHostRemount(entry) {
    return !entry?.host?.isConnected;
  }
  function resetSummaryRetry(entry) {
    entry.retryCount = 0;
    entry.nextRetryAt = 0;
  }
  function markSummaryRequestFailed(entry, now = Date.now()) {
    const retryCount = (entry.retryCount || 0) + 1;
    const retryable = retryCount <= MAX_SUMMARY_RETRIES;
    const state = { kind: "error", reason: "error", retryable };
    entry.requested = false;
    entry.retryCount = retryCount;
    entry.nextRetryAt = retryable ? now + SUMMARY_RETRY_BASE_MS * 2 ** (retryCount - 1) : 0;
    entry.state = state;
    return state;
  }
  function shouldRequestVisibleSummary(entry, now = Date.now()) {
    if (!entry || !entry.isVisible || entry.requested) return false;
    if (entry.state?.kind === "loading") return true;
    return entry.state?.kind === "error" && entry.state.retryable === true && (entry.nextRetryAt || 0) <= now;
  }
  var MAX_SUMMARY_RETRIES, SUMMARY_RETRY_BASE_MS;
  var init_list_recovery = __esm({
    "src/lib/list-recovery.js"() {
      MAX_SUMMARY_RETRIES = 3;
      SUMMARY_RETRY_BASE_MS = 2e3;
    }
  });

  // src/lib/storage-compat.js
  var storage_compat_exports = {};
  __export(storage_compat_exports, {
    getPendingLocateChange: () => getPendingLocateChange,
    matchesStorageArea: () => matchesStorageArea
  });
  function matchesStorageArea(areaName, expectedArea) {
    return areaName === void 0 || areaName === expectedArea;
  }
  function getPendingLocateChange(changes, areaName) {
    if (!matchesStorageArea(areaName, "session")) return null;
    return changes?.pendingLocate?.newValue ?? null;
  }
  var init_storage_compat = __esm({
    "src/lib/storage-compat.js"() {
    }
  });

  // src/content/list.js
  (async () => {
    const [
      profilesMod,
      batchMod,
      transformsMod,
      stateMod,
      i18nMod,
      recoveryMod,
      storageCompatMod,
      rankingMod
    ] = await Promise.all([
      Promise.resolve().then(() => (init_profiles(), profiles_exports)),
      Promise.resolve().then(() => (init_batch(), batch_exports)),
      Promise.resolve().then(() => (init_transforms(), transforms_exports)),
      Promise.resolve().then(() => (init_dats_state(), dats_state_exports)),
      Promise.resolve().then(() => (init_i18n(), i18n_exports)),
      Promise.resolve().then(() => (init_list_recovery(), list_recovery_exports)),
      Promise.resolve().then(() => (init_storage_compat(), storage_compat_exports)),
      Promise.resolve().then(() => (init_ranking(), ranking_exports))
    ]);
    const { matchProfile: matchProfile2 } = profilesMod;
    const { createBatchQueue: createBatchQueue2 } = batchMod;
    const { applyTransforms: applyTransforms2 } = transformsMod;
    const {
      createGenerationGuard: createGenerationGuard2,
      createNodeStateRegistry: createNodeStateRegistry2,
      shouldInjectList: shouldInjectList2,
      computeButtonState: computeButtonState2,
      buildSummaryCacheKey: buildSummaryCacheKey2,
      normalizeSummaryMap: normalizeSummaryMap2
    } = stateMod;
    const { normalizeLocale: normalizeLocale2, t: t2 } = i18nMod;
    const {
      markSummaryRequestFailed: markSummaryRequestFailed2,
      needsHostRemount: needsHostRemount2,
      resetSummaryRetry: resetSummaryRetry2,
      shouldRequestVisibleSummary: shouldRequestVisibleSummary2
    } = recoveryMod;
    const { matchesStorageArea: matchesStorageArea2 } = storageCompatMod;
    const { formatSalesRankBand: formatSalesRankBand2, normalizeRankingDays: normalizeRankingDays2 } = rankingMod;
    const origin = location.origin;
    const stored = await chrome.storage.local.get([
      "supplierProfiles",
      "selectedStoreCode",
      "locale",
      "salesRankingDays"
    ]);
    const { supplierProfiles } = stored;
    let selectedStoreCode = stored.selectedStoreCode || null;
    let locale = normalizeLocale2(stored.locale);
    let salesRankingDays = normalizeRankingDays2(stored.salesRankingDays);
    const profiles = supplierProfiles && supplierProfiles.profiles || [];
    const profile = matchProfile2(profiles, { origin, pathname: location.pathname });
    if (!profile) return;
    function formatMessage(key, values = {}) {
      return Object.entries(values).reduce(
        (message, [name, value]) => message.replaceAll(`{${name}}`, String(value)),
        t2(locale, key)
      );
    }
    const cardSelector = profile.cardSelector;
    const itemCfg = profile.itemNumber;
    const mountSelector = profile.mountSelector;
    const mountPosition = profile.mountPosition;
    try {
      document.querySelector(cardSelector);
      if (itemCfg.selector) document.querySelector(itemCfg.selector);
      if (mountSelector) document.querySelector(mountSelector);
    } catch {
      return;
    }
    const generation = createGenerationGuard2(0);
    const registry = createNodeStateRegistry2();
    const trackedCards = /* @__PURE__ */ new Set();
    let active = true;
    let cardObserver = null;
    let visibilityObserver = null;
    let scanTimer = null;
    let scanInterval = null;
    let gfaLayoutStyle = null;
    function readItemNumber(card) {
      let el = card;
      if (itemCfg.selector) {
        const sub = card.querySelector(itemCfg.selector);
        if (!sub) return "";
        el = sub;
      }
      const raw = itemCfg.source === "attribute" ? el.getAttribute(itemCfg.attribute) : el.textContent;
      return applyTransforms2(raw, itemCfg.transforms);
    }
    function ensureGfaLayoutStyle() {
      if (gfaLayoutStyle?.isConnected) return;
      const existing = document.querySelector("style[data-hb-sro-gfa-layout]");
      if (existing) {
        gfaLayoutStyle = existing;
        return;
      }
      gfaLayoutStyle = document.createElement("style");
      gfaLayoutStyle.setAttribute("data-hb-sro-gfa-layout", "");
      gfaLayoutStyle.textContent = `
.list-row[data-product]:has(> .content > [data-hb-sro-host]) > .content {
  height: auto !important;
  min-height: 100px;
}
.list-row[data-product]:has(> .content > [data-hb-sro-host]) > .content > a[href*="/product/view?id="] > .list-content {
  height: auto !important;
}
.list-row[data-product]:has(> .content > [data-hb-sro-host]) > .content > a[href*="/product/view?id="] > .list-content .list-detail {
  height: auto !important;
}
@media (max-width: 500px) {
  .list-row[data-product]:has(> .content > [data-hb-sro-host]) > .content {
    padding-bottom: 46px !important;
  }
  .list-row[data-product] > .content > [data-hb-sro-host] {
    margin-right: 0 !important;
  }
}`;
      (document.head || document.documentElement).appendChild(gfaLayoutStyle);
    }
    function mountHost(card) {
      let mountEl = card;
      let pos = "beforeend";
      if (mountSelector) {
        const found = card.querySelector(mountSelector);
        if (found) {
          mountEl = found;
          pos = mountPosition || "afterend";
        }
      }
      const host = document.createElement("div");
      host.setAttribute("data-hb-sro-host", "");
      const isGfaFixedHeightRow = profile.supplierCode === "236" && card.matches(".list-row[data-product]");
      if (isGfaFixedHeightRow) ensureGfaLayoutStyle();
      host.style.cssText = isGfaFixedHeightRow ? "display:block;margin:4px 235px 0 0;position:relative;z-index:2;pointer-events:none;" : "display:block;margin:4px 0;";
      mountEl.insertAdjacentElement(pos, host);
      return host;
    }
    function createShadowButton(host) {
      const root = host.attachShadow({ mode: "closed" });
      const style = document.createElement("style");
      style.textContent = [
        ".hb-btn{all:unset;box-sizing:border-box;display:inline-block;max-width:100%;padding:4px 8px;border-radius:4px;border:1px solid #d5d5d5;background:#fafafa;color:#333;cursor:pointer;font:12px/1.5 system-ui,sans-serif;white-space:normal;overflow-wrap:anywhere;pointer-events:auto;}",
        ".hb-btn:focus-visible{outline:2px solid #2563eb;outline-offset:2px;}",
        ".hb-order{color:#c62828;font-weight:600;}",
        ".hb-sales{color:#1565c0;font-weight:600;}",
        ".hb-muted{color:#757575;}",
        ".hb-rank-line{display:block;width:max-content;max-width:100%;box-sizing:border-box;margin-top:2px;padding:1px 6px;border:1px solid #b8d8ff;border-radius:999px;background:#eaf3ff;color:#1565c0;font-size:10px;font-weight:700;line-height:1.5;overflow-wrap:anywhere;white-space:normal;}",
        ".hb-rank-line-top-20{border-color:#c7e3ca;background:#eef7ef;color:#2e7d32;}",
        ".hb-rank-line-top-30{border-color:#ddd0ef;background:#f5f1fb;color:#6f3cc3;}"
      ].join("");
      const btn = document.createElement("button");
      btn.type = "button";
      btn.className = "hb-btn";
      root.appendChild(style);
      root.appendChild(btn);
      return btn;
    }
    function renderButton(entry, state) {
      const btn = entry.btn;
      btn.replaceChildren();
      if (state.kind === "loading") {
        btn.textContent = t2(locale, "loading");
      } else if (state.kind === "none" || state.kind === "error") {
        const span = document.createElement("span");
        span.className = "hb-muted";
        span.textContent = shortStatus(state);
        btn.appendChild(span);
      } else if (state.kind === "noStore") {
        const span = document.createElement("span");
        span.className = "hb-muted";
        span.textContent = t2(locale, "noStore");
        btn.appendChild(span);
      } else {
        const order = document.createElement("span");
        order.className = "hb-order";
        order.textContent = `${t2(locale, "lastOrder")} ${state.lastOrderDate || "\u2014"} \xD7 ${state.lastOrderQuantity ?? 0}`;
        const sales = document.createElement("span");
        sales.className = "hb-sales";
        sales.textContent = `${t2(locale, "salesToDate")} ${state.salesToDate ?? 0}`;
        btn.appendChild(order);
        btn.appendChild(document.createTextNode(" \xB7 "));
        btn.appendChild(sales);
      }
      const rankLabel = formatSalesRankBand2(state.salesRankBand);
      if ((state.kind === "ok" || state.reason === "noPurchase") && rankLabel) {
        const rankLine = document.createElement("span");
        rankLine.className = "hb-rank-line";
        rankLine.classList.add(`hb-rank-line-${state.salesRankBand}`);
        rankLine.textContent = formatMessage("salesRankBand", {
          days: state.salesRankingDays,
          band: rankLabel
        });
        btn.appendChild(rankLine);
      }
    }
    function shortStatus(state) {
      if (state.kind === "error") return t2(locale, "error");
      if (state.reason === "noPurchase") return t2(locale, "noPurchase");
      return t2(locale, "noMatch");
    }
    function requestSummary(entry) {
      if (!active || entry.requested) return;
      if (entry.state?.kind === "loading") resetSummaryRetry2(entry);
      entry.requested = true;
      const requestedGeneration = entry.generation;
      const requestedItemNumber = entry.itemNumber;
      const requestedCard = entry.card;
      const requestedRankingDays = salesRankingDays;
      batch.enqueue(
        buildSummaryCacheKey2(selectedStoreCode, requestedItemNumber, salesRankingDays),
        requestedItemNumber
      ).then((summary) => {
        if (!active || !generation.isCurrent(requestedGeneration) || registry.get(requestedCard) !== entry || entry.itemNumber !== requestedItemNumber || !requestedCard.isConnected) {
          return;
        }
        const state = summary && summary.storeMissing ? { kind: "noStore" } : computeButtonState2({ ...summary, salesRankingDays: requestedRankingDays });
        resetSummaryRetry2(entry);
        entry.state = state;
        renderButton(entry, state);
      }).catch(() => {
        if (!active || !generation.isCurrent(requestedGeneration) || registry.get(requestedCard) !== entry || entry.itemNumber !== requestedItemNumber) {
          return;
        }
        const state = markSummaryRequestFailed2(entry);
        renderButton(entry, state);
      });
    }
    function createSummaryBatch(storeCode, rankingDays) {
      return createBatchQueue2({
        maxSize: 100,
        delayMs: 150,
        cacheTtlMs: 6e4,
        flush: async (entries) => {
          if (!storeCode) {
            const out2 = {};
            for (const e of entries) out2[e.key] = { storeMissing: true };
            return out2;
          }
          const itemNumbers = entries.map((e) => e.item);
          const resp = await chrome.runtime.sendMessage({
            type: "SUMMARY_BATCH",
            storeCode,
            supplierCode: profile.supplierCode,
            itemNumbers,
            salesRankingDays: rankingDays
          });
          if (!resp || !resp.ok) {
            throw new Error(resp && resp.error || "summary request failed");
          }
          const map = normalizeSummaryMap2(resp && resp.data);
          const out = {};
          for (const e of entries) out[e.key] = map[e.item] || { hasMatch: false };
          return out;
        }
      });
    }
    let batch = createSummaryBatch(selectedStoreCode, salesRankingDays);
    function attachEntryButton(entry) {
      entry.host?.remove();
      entry.card.querySelector("[data-hb-sro-host]")?.remove();
      entry.host = mountHost(entry.card);
      entry.btn = createShadowButton(entry.host);
      entry.btn.addEventListener("click", () => {
        chrome.runtime.sendMessage({
          type: "LOCATE_ITEM",
          storeCode: selectedStoreCode || null,
          supplierCode: profile.supplierCode,
          itemNumber: entry.itemNumber
        });
      });
    }
    function ensureCard(card) {
      const itemNumber = readItemNumber(card);
      const existing = registry.get(card);
      if (!itemNumber) {
        if (existing) {
          visibilityObserver?.unobserve(card);
          existing.host?.remove();
          registry.delete(card);
          trackedCards.delete(card);
        }
        return null;
      }
      let entry = existing;
      if (!entry) {
        entry = {
          generation: generation.current(),
          card,
          itemNumber,
          host: null,
          btn: null,
          state: { kind: "loading" },
          requested: false,
          isVisible: false
        };
        attachEntryButton(entry);
        registry.set(card, entry);
        trackedCards.add(card);
        if (visibilityObserver) visibilityObserver.observe(card);
      } else {
        entry.generation = generation.current();
        if (needsHostRemount2(entry)) attachEntryButton(entry);
        if (entry.itemNumber !== itemNumber) {
          entry.itemNumber = itemNumber;
          entry.requested = false;
          entry.state = { kind: "loading" };
          if (entry.isVisible) requestSummary(entry);
        }
      }
      renderButton(entry, entry.state);
      if (shouldRequestVisibleSummary2(entry)) requestSummary(entry);
      return entry;
    }
    function scan() {
      if (!active) return;
      for (const card of trackedCards) {
        if (card.isConnected) continue;
        visibilityObserver?.unobserve(card);
        registry.delete(card);
        trackedCards.delete(card);
      }
      const cards = Array.from(document.querySelectorAll(cardSelector));
      const pageEligible = shouldInjectList2({
        href: location.href,
        listPagePatterns: profile.listPagePatterns,
        cardCount: cards.length,
        isDetailPage: document.body.classList.contains("catalog-product-view") || document.body.classList.contains("page-ProductDetail") || !!document.querySelector('.product-info-main, [data-role="product-info-main"]')
      });
      if (!pageEligible) {
        for (const card of trackedCards) {
          const entry = registry.get(card);
          visibilityObserver?.unobserve(card);
          entry?.host?.remove();
          registry.delete(card);
        }
        trackedCards.clear();
        return;
      }
      for (const card of cards) {
        ensureCard(card);
      }
    }
    visibilityObserver = new IntersectionObserver(
      (entries) => {
        for (const e of entries) {
          const entry = registry.get(e.target);
          if (!entry) continue;
          entry.isVisible = e.isIntersecting;
          if (e.isIntersecting && shouldRequestVisibleSummary2(entry)) requestSummary(entry);
        }
      },
      { rootMargin: "600px" }
    );
    const attributeFilter = itemCfg.source === "attribute" && itemCfg.attribute ? [itemCfg.attribute] : [];
    cardObserver = new MutationObserver((mutations) => {
      let shouldScan = false;
      for (const m of mutations) {
        if (m.type === "childList") {
          const target = m.target?.nodeType === 1 ? m.target : m.target?.parentElement;
          if (target && (target.matches?.(cardSelector) || target.closest?.(cardSelector))) {
            shouldScan = true;
          }
          for (const node of m.addedNodes) {
            if (node && node.nodeType === 1) {
              const el = node;
              if (typeof el.matches === "function" && (el.matches(cardSelector) || el.querySelector(cardSelector))) {
                shouldScan = true;
                break;
              }
            }
          }
        } else if (m.type === "attributes" || m.type === "characterData") {
          const target = m.target?.nodeType === 1 ? m.target : m.target?.parentElement;
          if (target && (target.matches?.(cardSelector) || target.closest?.(cardSelector))) {
            shouldScan = true;
          }
        }
        if (shouldScan) break;
      }
      if (shouldScan) scheduleScan();
    });
    const observerOptions = {
      childList: true,
      subtree: true
    };
    if (attributeFilter.length > 0) {
      observerOptions.attributes = true;
      observerOptions.attributeFilter = attributeFilter;
    }
    if (itemCfg.source === "text") {
      observerOptions.characterData = true;
    }
    cardObserver.observe(document.body, observerOptions);
    function scheduleScan() {
      if (scanTimer !== null) return;
      scanTimer = setTimeout(() => {
        scanTimer = null;
        scan();
      }, 50);
    }
    const handleNavigation = () => {
      generation.advance();
      for (const card of trackedCards) {
        const entry = registry.get(card);
        if (entry) {
          entry.generation = generation.current();
          entry.requested = false;
          entry.state = { kind: "loading" };
        }
      }
      scan();
    };
    window.addEventListener("popstate", handleNavigation);
    window.addEventListener("hashchange", handleNavigation);
    function refreshForStore(storeCode) {
      refreshSummaryContext({ storeCode });
    }
    function refreshSummaryContext({
      storeCode = selectedStoreCode,
      rankingDays = salesRankingDays
    } = {}) {
      selectedStoreCode = storeCode || null;
      salesRankingDays = normalizeRankingDays2(rankingDays);
      generation.advance();
      batch.clearCache();
      batch = createSummaryBatch(selectedStoreCode, salesRankingDays);
      for (const card of trackedCards) {
        const entry = registry.get(card);
        if (!entry || !card.isConnected) continue;
        entry.generation = generation.current();
        entry.requested = false;
        entry.state = { kind: "loading" };
        renderButton(entry, entry.state);
        if (entry.isVisible) requestSummary(entry);
      }
    }
    function teardown() {
      if (!active) return;
      active = false;
      cardObserver?.disconnect();
      visibilityObserver?.disconnect();
      if (scanTimer !== null) clearTimeout(scanTimer);
      if (scanInterval !== null) clearInterval(scanInterval);
      window.removeEventListener("popstate", handleNavigation);
      window.removeEventListener("hashchange", handleNavigation);
      for (const card of trackedCards) {
        registry.get(card)?.host?.remove();
      }
      trackedCards.clear();
    }
    chrome.storage.onChanged.addListener((changes, areaName) => {
      if (!matchesStorageArea2(areaName, "local") || !active) return;
      if (changes.selectedStoreCode && !changes.salesRankingDays) {
        refreshForStore(changes.selectedStoreCode.newValue);
      } else if (changes.selectedStoreCode || changes.salesRankingDays) {
        refreshSummaryContext({
          storeCode: changes.selectedStoreCode ? changes.selectedStoreCode.newValue : selectedStoreCode,
          rankingDays: changes.salesRankingDays ? changes.salesRankingDays.newValue : salesRankingDays
        });
      }
      if (changes.locale) {
        locale = normalizeLocale2(changes.locale.newValue);
        for (const card of trackedCards) {
          const entry = registry.get(card);
          if (entry) renderButton(entry, entry.state);
        }
      }
      if (changes.supplierProfiles) {
        const updatedProfiles = changes.supplierProfiles.newValue?.profiles || [];
        const updatedProfile = matchProfile2(updatedProfiles, { origin, pathname: location.pathname });
        if (!updatedProfile || updatedProfile.supplierCode !== profile.supplierCode) teardown();
      }
    });
    scanInterval = setInterval(scan, 2e3);
    scan();
  })();
})();
