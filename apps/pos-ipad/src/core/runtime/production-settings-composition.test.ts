import assert from "node:assert/strict";
import test from "node:test";

import { CurrentCashierSession } from "./current-cashier-session";
import {
  createProductionSettingsComposition,
  type ProductionSettingsCompositionInput,
} from "./production-settings-composition";

import { PricingCart } from "@/features/sales/domain";
import { ActivePricingCartSession } from "@/features/sales/runtime";

const NOW = "2026-07-28T00:00:00.000Z";

test("生产设置组合只从可信组合根生成快照，并复用同一购物车独占门闩", async () => {
  const events: string[] = [];
  const cashier = activeCashier();
  const activeCart = new ActivePricingCartSession(
    new PricingCart(),
    () => new PricingCart(),
  );
  const input = dependencies({
    currentCashier: cashier,
    activeCart,
    readDevicePresentation: async () => ({
      deviceCode: "IPAD-1",
      storeCode: "S1",
      storeName: "Store One",
      terminalName: "Front",
    }),
    pendingData: {
      read: async () => ({
        pendingDurableWriteCount: 0,
        pendingReturnCount: 0,
        pendingSaleCount: 0,
        unresolvedPaymentCount: 0,
      }),
    },
    apiConfiguration: {
      probe: async () => true,
      save: async () => {
        events.push("api:save");
      },
    },
    runtimeReload: {
      reload: async () => {
        events.push("reload");
      },
    },
  });
  const runtime = createProductionSettingsComposition(input);
  const presenter = runtime.createPresenter();

  await presenter.load();
  assert.equal(presenter.getState().kind, "ready");
  assert.equal(presenter.getState().device.deviceCode, "IPAD-1");
  assert.equal(presenter.getState().catalog.itemCount, 2);

  presenter.setApiAddressDraft("https://next.example.test");
  assert.equal(presenter.requestApiAddressChange(), true);
  await presenter.confirmDangerousAction();
  assert.deepEqual(events, ["api:save", "reload"]);
});

test("数据库、退货或支付恢复任一未清零时，危险设置动作保持阻断", async () => {
  let saved = false;
  const runtime = createProductionSettingsComposition(
    dependencies({
      currentCashier: activeCashier(),
      pendingData: {
        read: async () => ({
          pendingDurableWriteCount: 1,
          pendingReturnCount: 0,
          pendingSaleCount: 0,
          unresolvedPaymentCount: 0,
        }),
      },
      apiConfiguration: {
        probe: async () => true,
        save: async () => {
          saved = true;
        },
      },
    }),
  );
  const presenter = runtime.createPresenter();
  await presenter.load();
  presenter.setApiAddressDraft("https://next.example.test");
  presenter.requestApiAddressChange();
  await presenter.confirmDangerousAction();

  assert.equal(saved, false);
  assert.equal(presenter.getState().statusCode, "pending-local-data");
});

test("打印与外屏测试只使用已配置外设和无敏感字段的只读测试快照", async () => {
  const events: unknown[] = [];
  const runtime = createProductionSettingsComposition(
    dependencies({
      currentCashier: activeCashier(),
      printer: {
        getStatus: async () => "ready",
        scan: async () => [],
        connect: async (id) => {
          events.push(["connect", id]);
        },
        disconnect: async () => undefined,
        print: async (id, bytes) => {
          events.push(["print", id, Array.from(bytes)]);
          return { status: "printed", errorCode: null };
        },
        subscribe: () => () => undefined,
        open: async () => ({
          status: "completed",
          errorCode: null,
        }),
      },
      externalDisplay: {
        getStatus: async () => "ready",
        setEnabled: async () => undefined,
        publish: async (snapshot) => {
          events.push(["display", snapshot]);
        },
        subscribe: () => () => undefined,
      },
    }),
  );
  const presenter = runtime.createPresenter();
  await presenter.load();

  await presenter.testPrinter();
  await presenter.testExternalDisplay();

  assert.equal(presenter.getState().statusCode, "display-test-passed");
  assert.deepEqual(events[0], ["connect", "XP-1"]);
  const display = events.at(-1) as [string, Record<string, unknown>];
  assert.equal(display[0], "display");
  assert.deepEqual(Object.keys(display[1]).sort(), [
    "advert",
    "change",
    "discount",
    "gst",
    "items",
    "mode",
    "revision",
    "total",
  ]);
});

test("销毁 settings presenter 只退订页面，不中止应用级目录下载", async () => {
  let receivedSignal: AbortSignal | null = null;
  let releaseDownload!: () => void;
  let downloadEntered!: () => void;
  const entered = new Promise<void>((resolve) => { downloadEntered = resolve; });
  const release = new Promise<void>((resolve) => { releaseDownload = resolve; });
  const runtime = createProductionSettingsComposition(
    dependencies({
      catalog: {
        getActiveMetadata: async () => ({
          snapshotId: "catalog-1",
          catalogVersion: "catalog-v1",
          itemCount: 2,
          activatedAt: NOW,
        }),
        getRefreshState: () => ({ kind: "idle" }),
        subscribeRefresh: () => () => undefined,
        runExclusive: (operation) => operation(),
        download: async (signal) => {
          receivedSignal = signal;
          downloadEntered();
          await release;
          return { snapshotId: "catalog-2", itemCount: 3, activatedAt: NOW };
        },
        reset: async () => ({
          snapshotId: "catalog-3",
          itemCount: 4,
          activatedAt: NOW,
        }),
      },
    }),
  );
  const presenter = runtime.createPresenter();
  await presenter.load();

  const download = presenter.downloadCatalog();
  await entered;
  presenter.destroy();
  assert.equal((receivedSignal as unknown as AbortSignal).aborted, false);
  releaseDownload();
  await download;
});

function activeCashier(): CurrentCashierSession {
  const cashier = new CurrentCashierSession();
  const epoch = cashier.beginAuthentication();
  cashier.activate(
    epoch,
    {
      source: "online",
      session: {
        cashierId: "C1",
        cashierName: "Alice",
        userGuid: "U1",
        storeCode: "S1",
        deviceCode: "IPAD-1",
        permissionCodes: [
          "Permissions.PosTerminal.Settings.View",
          "Permissions.PosTerminal.Settings.AppUpdate",
          "Permissions.PosTerminal.Settings.CatalogDownload",
          "Permissions.PosTerminal.Settings.CatalogReset",
          "Permissions.PosTerminal.CustomerDisplay.Manage",
          "Permissions.PosTerminal.Settings.PaymentTerminal",
          "Permissions.PosTerminal.Settings.ReceiptPrinter",
          "Permissions.PosTerminal.Settings.DeviceRegistration",
        ],
      },
    },
    { storeCode: "S1", deviceCode: "IPAD-1" },
  );
  return cashier;
}

function dependencies(
  overrides: Partial<ProductionSettingsCompositionInput> = {},
): ProductionSettingsCompositionInput {
  const activeCart =
    overrides.activeCart ??
    new ActivePricingCartSession(
      new PricingCart(),
      () => new PricingCart(),
    );
  return {
    currentCashier: overrides.currentCashier ?? activeCashier(),
    terminal: { storeCode: "S1", deviceCode: "IPAD-1" },
    activeCart,
    apiBaseUrl: "https://pos.example.test",
    appVersion: "1.0.0",
    updateChannel: "preview",
    createId: () => "settings-operation-1",
    readDevicePresentation: async () => ({
      deviceCode: "IPAD-1",
      storeCode: "S1",
      storeName: "Store One",
      terminalName: "Front",
    }),
    catalog: {
      getActiveMetadata: async () => ({
        snapshotId: "catalog-1",
        catalogVersion: "catalog-v1",
        itemCount: 2,
        activatedAt: NOW,
      }),
      getRefreshState: () => ({ kind: "idle" }),
      subscribeRefresh: () => () => undefined,
      runExclusive: (operation) => operation(),
      download: async () => ({
        snapshotId: "catalog-2",
        itemCount: 3,
        activatedAt: NOW,
      }),
      reset: async () => ({
        snapshotId: "catalog-3",
        itemCount: 4,
        activatedAt: NOW,
      }),
    },
    receiptSettings: {
      get: async () => ({
        printEnabled: true,
        drawerEnabled: true,
        peripheralId: "XP-1",
        paper: "80mm",
        locale: "en",
        brandName: "",
        storeName: "Store One",
        address: "",
        phone: "",
        abn: "",
      }),
      save: async () => undefined,
    },
    paymentConfiguration: {
      current: {
        provider: "square",
        square: {
          environment: "Sandbox",
          deviceId: "SQ-1",
          locationId: "LOC-1",
        },
        linkly: null,
      },
      test: async () => undefined,
      save: async () => undefined,
      availability: {
        square: { available: true, blockerCode: null },
        linkly: { available: true, blockerCode: null },
      },
    },
    pendingData: {
      read: async () => ({
        pendingDurableWriteCount: 0,
        pendingReturnCount: 0,
        pendingSaleCount: 0,
        unresolvedPaymentCount: 0,
      }),
    },
    apiConfiguration: {
      probe: async () => true,
      save: async () => undefined,
    },
    runtimeReload: {
      reload: async () => undefined,
    },
    device: {
      reregister: async () => undefined,
    },
    printer: {
      getStatus: async () => "ready",
      scan: async () => [],
      connect: async () => undefined,
      disconnect: async () => undefined,
      print: async () => ({ status: "printed", errorCode: null }),
      subscribe: () => () => undefined,
      open: async () => ({ status: "completed", errorCode: null }),
    },
    scanner: {
      test: async () => ({ source: "hid", value: "SKU-1" }),
      status: "ready",
    },
    externalDisplay: {
      getStatus: async () => "ready",
      setEnabled: async () => undefined,
      publish: async () => undefined,
      subscribe: () => () => undefined,
    },
    appUpdate: {
      check: async () => ({
        channel: "preview",
        currentVersion: "1.0.0",
        availableVersion: null,
        updateRequired: false,
        restartAvailable: true,
      }),
      snapshot: () => ({
        channel: "preview",
        currentVersion: "1.0.0",
        availableVersion: null,
        updateRequired: false,
        restartAvailable: true,
      }),
      restart: async () => true,
    },
    ...overrides,
  };
}
