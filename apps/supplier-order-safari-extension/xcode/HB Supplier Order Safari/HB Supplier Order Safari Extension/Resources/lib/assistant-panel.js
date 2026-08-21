// 浏览器面板适配：Chrome/Edge 使用原生 Side Panel，iOS Safari 使用完整扩展页。
export const ASSISTANT_PANEL_SOURCE_TAB_KEY = 'assistantPanelSourceTabId';

const PANEL_PATH = 'sidepanel/sidepanel.html';

export function createAssistantPanelController({ browserApi, buildTarget }) {
  const isSafari = buildTarget === 'safari';
  const panelUrl = browserApi.runtime.getURL(PANEL_PATH);
  const extensionRoot = browserApi.runtime.getURL('');
  let safariPagePromise = null;

  async function rememberSourceTab(tab) {
    if (!isSafari || !Number.isInteger(tab?.id)) return;
    // Safari 助手页本身也会触发 tab 激活事件，绝不能把它覆盖成业务来源页。
    if (typeof tab.url === 'string' && tab.url.startsWith(extensionRoot)) return;
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
      active: true,
    });
  }

  async function openSafariPage({ tabId } = {}) {
    if (Number.isInteger(tabId)) {
      try {
        await rememberSourceTab(await browserApi.tabs.get(tabId));
      } catch {
        // 来源标签可能刚关闭；助手页仍可打开，活动供应商稍后安全回退。
      }
    }

    // 多入口可能同时触发 OPEN；single-flight 防止 Safari 重复创建 options page。
    if (!safariPagePromise) {
      safariPagePromise = (async () => {
        try {
          await browserApi.runtime.openOptionsPage();
        } catch {
          // 某些 Safari 版本无法从后台直接打开 options page，降级为扩展标签页。
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
          if (!(typeof tab.url === 'string' && tab.url.startsWith(extensionRoot))) {
            return [tab];
          }
        } catch {
          // 已关闭或失效的来源标签会在下面移除并回退浏览器查询。
        }
        await browserApi.storage.session.remove(ASSISTANT_PANEL_SOURCE_TAB_KEY);
      }
    }
    return browserApi.tabs.query({ active: true, lastFocusedWindow: true });
  }

  function registerListeners() {
    if (!isSafari) return;

    browserApi.action.onClicked.addListener((tab) =>
      openSafariPage({ tabId: tab?.id }).catch(() => undefined));
    browserApi.tabs.onActivated.addListener(({ tabId }) =>
      browserApi.tabs.get(tabId).then(rememberSourceTab).catch(() => undefined));
  }

  return {
    rememberSourceTab,
    queryActiveTabs,
    registerListeners,
    configureAction() {
      return isSafari
        ? Promise.resolve()
        : browserApi.sidePanel.setPanelBehavior({ openPanelOnActionClick: true });
    },
    open(options) {
      return isSafari ? openSafariPage(options) : browserApi.sidePanel.open(options);
    },
  };
}
