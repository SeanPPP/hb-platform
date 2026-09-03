// 供应商列表页注入：MutationObserver + IntersectionObserver + WeakMap + generation guard，
// 微批请求商品摘要并注入 shadow DOM 按钮，点击定位到侧栏采购周期。
(async () => {
  const [
    profilesMod,
    batchMod,
    transformsMod,
    stateMod,
    i18nMod,
    recoveryMod,
    storageCompatMod,
    rankingMod,
  ] = await Promise.all([
    import(chrome.runtime.getURL('lib/profiles.js')),
    import(chrome.runtime.getURL('lib/batch.js')),
    import(chrome.runtime.getURL('lib/transforms.js')),
    import(chrome.runtime.getURL('lib/dats-state.js')),
    import(chrome.runtime.getURL('lib/i18n.js')),
    import(chrome.runtime.getURL('lib/list-recovery.js')),
    import(chrome.runtime.getURL('lib/storage-compat.js')),
    import(chrome.runtime.getURL('lib/ranking.js')),
  ]);
  const { matchProfile } = profilesMod;
  const { createBatchQueue } = batchMod;
  const { applyTransforms } = transformsMod;
  const {
    createGenerationGuard,
    createNodeStateRegistry,
    shouldInjectList,
    computeButtonState,
    buildSummaryCacheKey,
    normalizeSummaryMap,
  } = stateMod;
  const { normalizeLocale, t } = i18nMod;
  const {
    markSummaryRequestFailed,
    needsHostRemount,
    resetSummaryRetry,
    shouldRequestVisibleSummary,
  } = recoveryMod;
  const { matchesStorageArea } = storageCompatMod;
  const { formatSalesRankBand, normalizeRankingDays } = rankingMod;

  const origin = location.origin;

  const stored = await chrome.storage.local.get([
    'supplierProfiles',
    'selectedStoreCode',
    'locale',
    'salesRankingDays',
  ]);
  const { supplierProfiles } = stored;
  let selectedStoreCode = stored.selectedStoreCode || null;
  let locale = normalizeLocale(stored.locale);
  let salesRankingDays = normalizeRankingDays(stored.salesRankingDays);
  const profiles = (supplierProfiles && supplierProfiles.profiles) || [];
  const profile = matchProfile(profiles, { origin, pathname: location.pathname });
  if (!profile) return;

  function formatMessage(key, values = {}) {
    return Object.entries(values).reduce(
      (message, [name, value]) => message.replaceAll(`{${name}}`, String(value)),
      t(locale, key),
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
    // 后台 selector 配置有语法错误时 fail closed，避免影响供应商页面。
    return;
  }

  const generation = createGenerationGuard(0);
  const registry = createNodeStateRegistry();
  const trackedCards = new Set();
  let active = true;
  let cardObserver = null;
  let visibilityObserver = null;
  let scanTimer = null;
  let scanInterval = null;
  let gfaLayoutStyle = null;

  // 读取卡片商品号：attribute/text + 声明式 transforms
  function readItemNumber(card) {
    let el = card;
    if (itemCfg.selector) {
      const sub = card.querySelector(itemCfg.selector);
      if (!sub) return '';
      el = sub;
    }
    const raw = itemCfg.source === 'attribute' ? el.getAttribute(itemCfg.attribute) : el.textContent;
    return applyTransforms(raw, itemCfg.transforms);
  }

  function ensureGfaLayoutStyle() {
    if (gfaLayoutStyle?.isConnected) return;
    const existing = document.querySelector('style[data-hb-sro-gfa-layout]');
    if (existing) {
      gfaLayoutStyle = existing;
      return;
    }
    gfaLayoutStyle = document.createElement('style');
    gfaLayoutStyle.setAttribute('data-hb-sro-gfa-layout', '');
    // GFA 的 100px 小列表行容不下商品明细和两行摘要；让内容按摘要高度自然扩展。
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
    let pos = 'beforeend';
    if (mountSelector) {
      const found = card.querySelector(mountSelector);
      if (found) {
        mountEl = found;
        pos = mountPosition || 'afterend';
      }
    }
    const host = document.createElement('div');
    host.setAttribute('data-hb-sro-host', '');
    const isGfaFixedHeightRow =
      profile.supplierCode === '236' && card.matches('.list-row[data-product]');
    if (isGfaFixedHeightRow) ensureGfaLayoutStyle();
    host.style.cssText = isGfaFixedHeightRow
      ? 'display:block;margin:4px 235px 0 0;position:relative;z-index:2;pointer-events:none;'
      : 'display:block;margin:4px 0;';
    mountEl.insertAdjacentElement(pos, host);
    return host;
  }

  function createShadowButton(host) {
    // 供应商页面只获得一个不可读的宿主节点，不能遍历本店销售摘要文本。
    const root = host.attachShadow({ mode: 'closed' });
    const style = document.createElement('style');
    style.textContent = [
      '.hb-btn{all:unset;box-sizing:border-box;display:inline-block;max-width:100%;padding:4px 8px;border-radius:4px;border:1px solid #d5d5d5;background:#fafafa;color:#333;cursor:pointer;font:12px/1.5 system-ui,sans-serif;white-space:normal;overflow-wrap:anywhere;pointer-events:auto;}',
      '.hb-btn:focus-visible{outline:2px solid #2563eb;outline-offset:2px;}',
      '.hb-order{color:#c62828;font-weight:600;}',
      '.hb-sales{color:#1565c0;font-weight:600;}',
      '.hb-muted{color:#757575;}',
      '.hb-rank-line{display:block;width:max-content;max-width:100%;box-sizing:border-box;margin-top:2px;padding:1px 6px;border:1px solid #b8d8ff;border-radius:999px;background:#eaf3ff;color:#1565c0;font-size:10px;font-weight:700;line-height:1.5;overflow-wrap:anywhere;white-space:normal;}',
      '.hb-rank-line-top-20{border-color:#c7e3ca;background:#eef7ef;color:#2e7d32;}',
      '.hb-rank-line-top-30{border-color:#ddd0ef;background:#f5f1fb;color:#6f3cc3;}',
    ].join('');
    const btn = document.createElement('button');
    btn.type = 'button';
    btn.className = 'hb-btn';
    root.appendChild(style);
    root.appendChild(btn);
    return btn;
  }

  function renderButton(entry, state) {
    const btn = entry.btn;
    btn.replaceChildren();
    if (state.kind === 'loading') {
      btn.textContent = t(locale, 'loading');
    } else if (state.kind === 'none' || state.kind === 'error') {
      const span = document.createElement('span');
      span.className = 'hb-muted';
      span.textContent = shortStatus(state);
      btn.appendChild(span);
    } else if (state.kind === 'noStore') {
      const span = document.createElement('span');
      span.className = 'hb-muted';
      span.textContent = t(locale, 'noStore');
      btn.appendChild(span);
    } else {
      const order = document.createElement('span');
      order.className = 'hb-order';
      order.textContent = `${t(locale, 'lastOrder')} ${state.lastOrderDate || '—'} × ${state.lastOrderQuantity ?? 0}`;
      const sales = document.createElement('span');
      sales.className = 'hb-sales';
      sales.textContent = `${t(locale, 'salesToDate')} ${state.salesToDate ?? 0}`;
      btn.appendChild(order);
      btn.appendChild(document.createTextNode(' · '));
      btn.appendChild(sales);
    }
    const rankLabel = formatSalesRankBand(state.salesRankBand);
    if ((state.kind === 'ok' || state.reason === 'noPurchase') && rankLabel) {
      const rankLine = document.createElement('span');
      rankLine.className = 'hb-rank-line';
      rankLine.classList.add(`hb-rank-line-${state.salesRankBand}`);
      rankLine.textContent = formatMessage('salesRankBand', {
        days: state.salesRankingDays,
        band: rankLabel,
      });
      btn.appendChild(rankLine);
    }
  }

  function shortStatus(state) {
    if (state.kind === 'error') return t(locale, 'error');
    if (state.reason === 'noPurchase') return t(locale, 'noPurchase');
    return t(locale, 'noMatch');
  }

  function requestSummary(entry) {
    if (!active || entry.requested) return;
    if (entry.state?.kind === 'loading') resetSummaryRetry(entry);
    entry.requested = true;
    const requestedGeneration = entry.generation;
    const requestedItemNumber = entry.itemNumber;
    const requestedCard = entry.card;
    const requestedRankingDays = salesRankingDays;
    batch
      .enqueue(
        buildSummaryCacheKey(selectedStoreCode, requestedItemNumber, salesRankingDays),
        requestedItemNumber,
      )
      .then((summary) => {
        if (
          !active
          || !generation.isCurrent(requestedGeneration)
          || registry.get(requestedCard) !== entry
          || entry.itemNumber !== requestedItemNumber
          || !requestedCard.isConnected
        ) {
          return;
        }
        const state = summary && summary.storeMissing
          ? { kind: 'noStore' }
          : computeButtonState({ ...summary, salesRankingDays: requestedRankingDays });
        resetSummaryRetry(entry);
        entry.state = state;
        renderButton(entry, state);
      })
      .catch(() => {
        if (
          !active
          || !generation.isCurrent(requestedGeneration)
          || registry.get(requestedCard) !== entry
          || entry.itemNumber !== requestedItemNumber
        ) {
          return;
        }
        const state = markSummaryRequestFailed(entry);
        renderButton(entry, state);
      });
  }

  // 每个队列固定绑定门店与排名周期；上下文变化时换代，旧请求即使晚返回也会被 generation 丢弃。
  function createSummaryBatch(storeCode, rankingDays) {
    return createBatchQueue({
      maxSize: 100,
      delayMs: 150,
      cacheTtlMs: 60000,
      flush: async (entries) => {
        if (!storeCode) {
          const out = {};
          for (const e of entries) out[e.key] = { storeMissing: true };
          return out;
        }
        const itemNumbers = entries.map((e) => e.item);
        const resp = await chrome.runtime.sendMessage({
          type: 'SUMMARY_BATCH',
          storeCode,
          supplierCode: profile.supplierCode,
          itemNumbers,
          salesRankingDays: rankingDays,
        });
        if (!resp || !resp.ok) {
          throw new Error((resp && resp.error) || 'summary request failed');
        }
        const map = normalizeSummaryMap(resp && resp.data);
        const out = {};
        for (const e of entries) out[e.key] = map[e.item] || { hasMatch: false };
        return out;
      },
    });
  }

  let batch = createSummaryBatch(selectedStoreCode, salesRankingDays);

  function attachEntryButton(entry) {
    entry.host?.remove();
    entry.card.querySelector('[data-hb-sro-host]')?.remove();
    entry.host = mountHost(entry.card);
    entry.btn = createShadowButton(entry.host);
    entry.btn.addEventListener('click', () => {
      chrome.runtime.sendMessage({
        type: 'LOCATE_ITEM',
        storeCode: selectedStoreCode || null,
        supplierCode: profile.supplierCode,
        itemNumber: entry.itemNumber,
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
        state: { kind: 'loading' },
        requested: false,
        isVisible: false,
      };
      // 扩展更新或脚本重载后，清理失去事件处理器的旧 host，再重新挂载。
      attachEntryButton(entry);
      registry.set(card, entry);
      trackedCards.add(card);
      if (visibilityObserver) visibilityObserver.observe(card);
    } else {
      entry.generation = generation.current();
      if (needsHostRemount(entry)) attachEntryButton(entry);
      if (entry.itemNumber !== itemNumber) {
        entry.itemNumber = itemNumber;
        entry.requested = false;
        entry.state = { kind: 'loading' };
        if (entry.isVisible) requestSummary(entry);
      }
    }
    renderButton(entry, entry.state);
    if (shouldRequestVisibleSummary(entry)) requestSummary(entry);
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
    const pageEligible = shouldInjectList({
      href: location.href,
      listPagePatterns: profile.listPagePatterns,
      cardCount: cards.length,
      isDetailPage:
        document.body.classList.contains('catalog-product-view')
        || document.body.classList.contains('page-ProductDetail')
        || !!document.querySelector('.product-info-main, [data-role="product-info-main"]'),
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

  // 仅可见（含 600px 缓冲）卡片才请求摘要
  visibilityObserver = new IntersectionObserver(
    (entries) => {
      for (const e of entries) {
        const entry = registry.get(e.target);
        if (!entry) continue;
        entry.isVisible = e.isIntersecting;
        if (e.isIntersecting && shouldRequestVisibleSummary(entry)) requestSummary(entry);
      }
    },
    { rootMargin: '600px' },
  );

  // 监听新增节点与 data-* 属性变化
  const attributeFilter = itemCfg.source === 'attribute' && itemCfg.attribute ? [itemCfg.attribute] : [];
  cardObserver = new MutationObserver((mutations) => {
    let shouldScan = false;
    for (const m of mutations) {
      if (m.type === 'childList') {
        const target = m.target?.nodeType === 1 ? m.target : m.target?.parentElement;
        if (target && (target.matches?.(cardSelector) || target.closest?.(cardSelector))) {
          shouldScan = true;
        }
        for (const node of m.addedNodes) {
          if (node && node.nodeType === 1) {
            const el = node;
            if (typeof el.matches === 'function' && (el.matches(cardSelector) || el.querySelector(cardSelector))) {
              shouldScan = true;
              break;
            }
          }
        }
      } else if (m.type === 'attributes' || m.type === 'characterData') {
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
    subtree: true,
  };
  if (attributeFilter.length > 0) {
    observerOptions.attributes = true;
    observerOptions.attributeFilter = attributeFilter;
  }
  if (itemCfg.source === 'text') {
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

  // SPA 导航/过滤：代次守卫 + 重扫；周期扫描作为兜底（幂等，不重复按钮）
  const handleNavigation = () => {
    generation.advance();
    for (const card of trackedCards) {
      const entry = registry.get(card);
      if (entry) {
        entry.generation = generation.current();
        entry.requested = false;
        entry.state = { kind: 'loading' };
      }
    }
    scan();
  };
  window.addEventListener('popstate', handleNavigation);
  window.addEventListener('hashchange', handleNavigation);

  function refreshForStore(storeCode) {
    refreshSummaryContext({ storeCode });
  }

  function refreshSummaryContext({
    storeCode = selectedStoreCode,
    rankingDays = salesRankingDays,
  } = {}) {
    selectedStoreCode = storeCode || null;
    salesRankingDays = normalizeRankingDays(rankingDays);
    generation.advance();
    batch.clearCache();
    batch = createSummaryBatch(selectedStoreCode, salesRankingDays);
    for (const card of trackedCards) {
      const entry = registry.get(card);
      if (!entry || !card.isConnected) continue;
      entry.generation = generation.current();
      entry.requested = false;
      entry.state = { kind: 'loading' };
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
    window.removeEventListener('popstate', handleNavigation);
    window.removeEventListener('hashchange', handleNavigation);
    for (const card of trackedCards) {
      registry.get(card)?.host?.remove();
    }
    trackedCards.clear();
  }

  chrome.storage.onChanged.addListener((changes, areaName) => {
    if (!matchesStorageArea(areaName, 'local') || !active) return;
    if (changes.selectedStoreCode && !changes.salesRankingDays) {
      refreshForStore(changes.selectedStoreCode.newValue);
    } else if (changes.selectedStoreCode || changes.salesRankingDays) {
      refreshSummaryContext({
        storeCode: changes.selectedStoreCode
          ? changes.selectedStoreCode.newValue
          : selectedStoreCode,
        rankingDays: changes.salesRankingDays
          ? changes.salesRankingDays.newValue
          : salesRankingDays,
      });
    }
    if (changes.locale) {
      locale = normalizeLocale(changes.locale.newValue);
      for (const card of trackedCards) {
        const entry = registry.get(card);
        if (entry) renderButton(entry, entry.state);
      }
    }
    if (changes.supplierProfiles) {
      const updatedProfiles = changes.supplierProfiles.newValue?.profiles || [];
      const updatedProfile = matchProfile(updatedProfiles, { origin, pathname: location.pathname });
      if (!updatedProfile || updatedProfile.supplierCode !== profile.supplierCode) teardown();
    }
  });

  scanInterval = setInterval(scan, 2000);

  scan();
})();
