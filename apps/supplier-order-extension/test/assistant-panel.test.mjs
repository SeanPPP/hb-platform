import test from 'node:test';
import assert from 'node:assert/strict';
import {
  ASSISTANT_PANEL_SOURCE_TAB_KEY,
  createAssistantPanelController,
} from '../src/lib/assistant-panel.js';

function createBrowserApi({ tabs = [], fallbackTabs = [], openOptionsPageError = null } = {}) {
  const session = {};
  const tabsById = new Map(tabs.map((tab) => [tab.id, tab]));
  const calls = {
    actionListeners: [],
    tabActivatedListeners: [],
    sidePanelOpen: [],
    sidePanelBehavior: [],
    optionsPageOpen: [],
    tabsCreate: [],
    tabsUpdate: [],
    tabsQuery: [],
  };

  const api = {
    action: {
      onClicked: {
        addListener(listener) {
          calls.actionListeners.push(listener);
        },
      },
    },
    runtime: {
      getURL(path = '') {
        return `safari-web-extension://hb-supplier-order/${path}`;
      },
      async openOptionsPage() {
        calls.optionsPageOpen.push(true);
        if (openOptionsPageError) throw openOptionsPageError;
      },
    },
    sidePanel: {
      async open(options) {
        calls.sidePanelOpen.push(options);
      },
      async setPanelBehavior(options) {
        calls.sidePanelBehavior.push(options);
      },
    },
    storage: {
      session: {
        async get(key) {
          return { [key]: session[key] };
        },
        async set(value) {
          Object.assign(session, value);
        },
        async remove(key) {
          delete session[key];
        },
      },
    },
    tabs: {
      async get(tabId) {
        const tab = tabsById.get(tabId);
        if (!tab) throw new Error('tab not found');
        return tab;
      },
      async query(query) {
        calls.tabsQuery.push(query);
        return fallbackTabs;
      },
      async create(options) {
        calls.tabsCreate.push(options);
        return { id: 91, url: options.url };
      },
      async update(tabId, options) {
        calls.tabsUpdate.push({ tabId, options });
        return tabsById.get(tabId) || { id: tabId };
      },
      onActivated: {
        addListener(listener) {
          calls.tabActivatedListeners.push(listener);
        },
      },
    },
  };

  return { api, calls, session };
}

test('Chrome/Edge 继续使用原生 sidePanel 行为', async () => {
  const { api, calls } = createBrowserApi({ fallbackTabs: [{ id: 3, url: 'https://example.com' }] });
  const controller = createAssistantPanelController({ browserApi: api, buildTarget: 'chrome' });

  controller.registerListeners();
  await controller.configureAction();
  await controller.open({ tabId: 3 });
  const activeTabs = await controller.queryActiveTabs();

  assert.deepEqual(calls.sidePanelBehavior, [{ openPanelOnActionClick: true }]);
  assert.deepEqual(calls.sidePanelOpen, [{ tabId: 3 }]);
  assert.deepEqual(activeTabs, [{ id: 3, url: 'https://example.com' }]);
  assert.equal(calls.actionListeners.length, 0);
  assert.equal(calls.tabActivatedListeners.length, 0);
});

test('iOS Safari 工具栏点击打开完整助手页并记住来源标签页', async () => {
  const sourceTab = { id: 7, url: 'https://www.dats.com.au/catalog' };
  const { api, calls, session } = createBrowserApi({ tabs: [sourceTab] });
  const controller = createAssistantPanelController({ browserApi: api, buildTarget: 'safari' });

  controller.registerListeners();
  await calls.actionListeners[0](sourceTab);

  assert.equal(calls.actionListeners.length, 1);
  assert.equal(calls.tabActivatedListeners.length, 1);
  assert.equal(session[ASSISTANT_PANEL_SOURCE_TAB_KEY], 7);
  assert.equal(calls.optionsPageOpen.length, 1);
  assert.deepEqual(calls.tabsCreate, []);
});

test('iOS Safari options page 失败时复用已有助手标签页', async () => {
  const panelUrl = 'safari-web-extension://hb-supplier-order/sidepanel/sidepanel.html';
  const { api, calls } = createBrowserApi({
    tabs: [
      { id: 8, url: 'https://boomup.com.au/shop' },
      { id: 13, url: panelUrl },
    ],
    fallbackTabs: [{ id: 13, url: panelUrl }],
    openOptionsPageError: new Error('unsupported'),
  });
  const controller = createAssistantPanelController({ browserApi: api, buildTarget: 'safari' });

  await controller.open({ tabId: 8 });

  assert.equal(calls.optionsPageOpen.length, 1);
  assert.deepEqual(calls.tabsUpdate, [{ tabId: 13, options: { active: true } }]);
  assert.deepEqual(calls.tabsCreate, []);
});

test('iOS Safari options page 失败且没有现有页面时创建助手标签页', async () => {
  const sourceTab = { id: 17, url: 'https://www.dats.com.au/catalog' };
  const { api, calls } = createBrowserApi({
    tabs: [sourceTab],
    fallbackTabs: [],
    openOptionsPageError: new Error('unsupported'),
  });
  const controller = createAssistantPanelController({ browserApi: api, buildTarget: 'safari' });

  await controller.open({ tabId: sourceTab.id });

  assert.deepEqual(calls.tabsCreate, [{
    url: 'safari-web-extension://hb-supplier-order/sidepanel/sidepanel.html',
    active: true,
  }]);
});

test('iOS Safari 并发打开请求只调用一次 options page', async () => {
  const sourceTab = { id: 18, url: 'https://www.dats.com.au/catalog' };
  const { api, calls } = createBrowserApi({ tabs: [sourceTab] });
  let releaseOpen;
  const openGate = new Promise((resolve) => { releaseOpen = resolve; });
  api.runtime.openOptionsPage = async () => {
    calls.optionsPageOpen.push(true);
    await openGate;
  };
  const controller = createAssistantPanelController({ browserApi: api, buildTarget: 'safari' });

  const firstOpen = controller.open({ tabId: sourceTab.id });
  await new Promise((resolve) => setTimeout(resolve, 0));
  const secondOpen = controller.open({ tabId: sourceTab.id });
  await new Promise((resolve) => setTimeout(resolve, 0));

  assert.equal(calls.optionsPageOpen.length, 1);
  releaseOpen();
  await Promise.all([firstOpen, secondOpen]);
  assert.equal(calls.optionsPageOpen.length, 1);
});

test('iOS Safari 打开失败后允许下一次重试', async () => {
  const sourceTab = { id: 19, url: 'https://www.dats.com.au/catalog' };
  const { api, calls } = createBrowserApi({ tabs: [sourceTab] });
  let attempt = 0;
  api.runtime.openOptionsPage = async () => {
    calls.optionsPageOpen.push(true);
    attempt += 1;
    if (attempt === 1) throw new Error('open failed');
  };
  api.tabs.query = async () => { throw new Error('fallback failed'); };
  const controller = createAssistantPanelController({ browserApi: api, buildTarget: 'safari' });

  await assert.rejects(controller.open({ tabId: sourceTab.id }), /fallback failed/);
  await controller.open({ tabId: sourceTab.id });

  assert.equal(calls.optionsPageOpen.length, 2);
});

test('Safari 查询活动供应商时优先使用记住的来源标签页', async () => {
  const sourceTab = { id: 21, url: 'https://www.dats.com.au/list' };
  const fallback = [{ id: 30, url: 'safari-web-extension://hb-supplier-order/sidepanel/sidepanel.html' }];
  const { api, calls } = createBrowserApi({ tabs: [sourceTab], fallbackTabs: fallback });
  const controller = createAssistantPanelController({ browserApi: api, buildTarget: 'safari' });

  await controller.rememberSourceTab(sourceTab);
  const activeTabs = await controller.queryActiveTabs();

  assert.deepEqual(activeTabs, [sourceTab]);
  assert.equal(calls.tabsQuery.length, 0);
});

test('Safari 忽略扩展自身标签，来源标签失效时安全回退', async () => {
  const fallback = [{ id: 44, url: 'https://example.com' }];
  const { api, calls, session } = createBrowserApi({ fallbackTabs: fallback });
  const controller = createAssistantPanelController({ browserApi: api, buildTarget: 'safari' });

  await controller.rememberSourceTab({
    id: 40,
    url: 'safari-web-extension://hb-supplier-order/sidepanel/sidepanel.html',
  });
  session[ASSISTANT_PANEL_SOURCE_TAB_KEY] = 999;
  const activeTabs = await controller.queryActiveTabs();

  assert.deepEqual(activeTabs, fallback);
  assert.equal(session[ASSISTANT_PANEL_SOURCE_TAB_KEY], undefined);
  assert.deepEqual(calls.tabsQuery, [{ active: true, lastFocusedWindow: true }]);
});
