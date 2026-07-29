import assert from "node:assert/strict";
import test from "node:test";

import type {
  SettingsControlPort,
  SettingsSnapshot,
} from "../../features/settings/settings-presenter";

import { createProductionSettingsRuntime } from "./production-settings-runtime";

const SNAPSHOT: SettingsSnapshot = {
  apiBaseUrl: "https://pos.example.test/api",
  appUpdate: {
    channel: "preview",
    currentVersion: "1.0.0",
    availableVersion: null,
    updateRequired: false,
    restartAvailable: false,
  },
  catalog: {
    snapshotId: "catalog-1",
    itemCount: 1,
    activatedAt: "2026-07-28T00:00:00.000Z",
  },
  device: {
    deviceCode: "IPAD-1",
    storeCode: "S1",
    storeName: "Store One",
    terminalName: "Front",
  },
  externalDisplay: {
    available: true,
    enabled: true,
    status: "connected",
  },
  hardware: {
    printerStatus: "connected",
    scannerStatus: "ready",
    externalDisplayStatus: "connected",
    lastScannerValue: null,
  },
  linkly: {
    available: true,
    blockerCode: null,
    environment: "Production",
  },
  paymentProvider: "square",
  printer: {
    printEnabled: true,
    drawerEnabled: true,
    peripheralId: "XP-1",
    paper: "80mm",
    locale: "en",
    brandName: "",
    storeName: "",
    address: "",
    phone: "",
    abn: "",
  },
  square: {
    available: true,
    blockerCode: null,
    environment: "Sandbox",
    deviceId: "SQ-1",
    locationId: "LOC-1",
  },
};

test("零参数工厂冻结可信权限，并在每次端口调用前后复核 cashier lease", async () => {
  let epoch = 1;
  const events: string[] = [];
  const runtime = createProductionSettingsRuntime({
    createSessionLease: () => ({
      get: () => {
        events.push(`lease:${epoch}`);
        if (epoch !== 1) throw new Error("SESSION_REPLACED");
        return {
          storeCode: "S1",
          deviceCode: "IPAD-1",
          permissionCodes: ["Permissions.PosTerminal.Settings.View"],
        };
      },
    }),
    control: fakeControl({
      loadSnapshot: async () => {
        events.push("load");
        return SNAPSHOT;
      },
    }),
    runDangerousExclusive: (operation) => operation(),
  });
  const presenter = runtime.createPresenter();

  await presenter.load();
  assert.equal(presenter.getState().kind, "ready");
  assert.deepEqual(events, ["lease:1", "lease:1", "load", "lease:1"]);

  epoch = 2;
  await presenter.load();
  assert.equal(presenter.getState().kind, "failed");
});

test("危险动作只能在组合根独占区执行，并在动作完成后再次复核 lease", async () => {
  const events: string[] = [];
  const runtime = createProductionSettingsRuntime({
    createSessionLease: () => ({
      get: () => {
        events.push("lease");
        return {
          storeCode: "S1",
          deviceCode: "IPAD-1",
          permissionCodes: [
            "Permissions.PosTerminal.Settings.View",
            "Permissions.PosTerminal.Settings.CatalogReset",
          ],
        };
      },
    }),
    control: fakeControl({
      loadSnapshot: async () => SNAPSHOT,
      executeDangerousAction: async () => {
        events.push("danger");
        return {
          status: "completed",
          kind: "reset-catalog",
          catalog: SNAPSHOT.catalog,
        };
      },
    }),
    runDangerousExclusive: async (operation) => {
      events.push("exclusive:start");
      const result = await operation();
      events.push("exclusive:end");
      return result;
    },
  });
  const presenter = runtime.createPresenter();
  await presenter.load();
  events.length = 0;

  assert.equal(presenter.requestCatalogReset(), true);
  await presenter.confirmDangerousAction();
  assert.deepEqual(events, [
    "lease",
    "exclusive:start",
    "danger",
    "lease",
    "exclusive:end",
  ]);
});

function fakeControl(
  overrides: Partial<SettingsControlPort>,
): SettingsControlPort {
  const unavailable = async (): Promise<never> => {
    throw new Error("not implemented");
  };
  return {
    loadSnapshot: unavailable,
    downloadCatalog: unavailable,
    testApiAddress: unavailable,
    testPaymentProvider: unavailable,
    savePrinterSettings: unavailable,
    scanPrinters: unavailable,
    connectPrinter: unavailable,
    testPrinter: unavailable,
    testScanner: unavailable,
    setExternalDisplayEnabled: unavailable,
    testExternalDisplay: unavailable,
    checkForAppUpdate: unavailable,
    executeDangerousAction: unavailable,
    ...overrides,
  };
}
