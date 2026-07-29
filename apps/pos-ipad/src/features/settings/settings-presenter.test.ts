import assert from "node:assert/strict";
import test from "node:test";

import {
  SETTINGS_APP_UPDATE_PERMISSION,
  SETTINGS_CATALOG_DOWNLOAD_PERMISSION,
  SETTINGS_CATALOG_RESET_PERMISSION,
  SETTINGS_CUSTOMER_DISPLAY_PERMISSION,
  SETTINGS_DEVICE_REGISTRATION_PERMISSION,
  SETTINGS_PAYMENT_TERMINAL_PERMISSION,
  SETTINGS_RECEIPT_PRINTER_PERMISSION,
  SETTINGS_VIEW_PERMISSION,
} from "./settings-authorization";
import {
  SettingsPresenter,
  type SettingsControlPort,
  type SettingsDangerousActionResult,
  type SettingsDangerousConfirmation,
  type SettingsPaymentSettingsInput,
  type SettingsPendingDataSnapshot,
  type SettingsSnapshot,
} from "./settings-presenter";

import {
  DEFAULT_RECEIPT_PRINTER_SETTINGS,
  type ReceiptPrinterSettings,
} from "@/core/db/pos-settings-repository";
import type { CatalogRefreshState } from "@/features/catalog/catalog-refresh-coordinator";

const allPermissions = [
  SETTINGS_VIEW_PERMISSION,
  SETTINGS_PAYMENT_TERMINAL_PERMISSION,
  SETTINGS_RECEIPT_PRINTER_PERMISSION,
  SETTINGS_CATALOG_DOWNLOAD_PERMISSION,
  SETTINGS_CATALOG_RESET_PERMISSION,
  SETTINGS_DEVICE_REGISTRATION_PERMISSION,
  SETTINGS_APP_UPDATE_PERMISSION,
  SETTINGS_CUSTOMER_DISPLAY_PERMISSION,
] as const;

test("无 View 权限时 fail closed 且不读取任何运行时设置", async () => {
  const port = new FakeSettingsPort();
  const presenter = new SettingsPresenter({ permissions: [], port });

  await presenter.load();

  assert.equal(port.loadCalls, 0);
  assert.equal(presenter.getState().kind, "unauthorized");
  assert.equal(presenter.getState().statusCode, "permission-required");
});

test("加载公开配置但不包含 Square/Linkly 密钥字段", async () => {
  const port = new FakeSettingsPort();
  const presenter = createPresenter(port);

  await presenter.load();

  assert.equal(port.loadCalls, 1);
  assert.equal(presenter.getState().kind, "ready");
  assert.equal(
    presenter.getState().apiAddressDraft,
    "https://hotbargain.vip/pos-api",
  );
  assert.deepEqual(presenter.getState().squareDraft, {
    environment: "Production",
    deviceId: "sq-device-1",
    locationId: "sq-location-1",
  });
  assert.deepEqual(presenter.getState().linklyDraft, {
    environment: "Production",
  });
  assert.equal(presenter.getState().paymentProvider, "square");
  assert.equal(presenter.getState().paymentProviderDraft, "square");
  assert.equal(
    "accessToken" in (presenter.getState().squareDraft as object),
    false,
  );
  assert.equal("secret" in (presenter.getState().linklyDraft as object), false);
});

test("运行时快照即使夹带额外敏感字段也只按白名单进入 state", async () => {
  const port = new FakeSettingsPort();
  const clean = snapshot();
  port.snapshotValue = {
    ...clean,
    externalDisplay: {
      ...clean.externalDisplay,
      authorization: "display-auth-should-not-enter-state",
    },
    hardware: {
      ...clean.hardware,
      scannerCredential: "scanner-credential-should-not-enter-state",
    },
    linkly: {
      ...clean.linkly,
      secret: "linkly-secret-should-not-enter-state",
    },
    printer: {
      ...clean.printer,
      accessToken: "printer-token-should-not-enter-state",
    },
    square: {
      ...clean.square,
      accessToken: "square-token-should-not-enter-state",
    },
  } as unknown as SettingsSnapshot;
  const presenter = createPresenter(port);

  await presenter.load();

  const serialized = JSON.stringify(presenter.getState());
  for (const sensitive of [
    "authorization",
    "scannerCredential",
    "secret",
    "accessToken",
    "should-not-enter-state",
  ]) {
    assert.equal(serialized.includes(sensitive), false);
  }
});

test("Square/Linkly 与打印机保存按精确权限执行并使用稳定状态码", async () => {
  const port = new FakeSettingsPort();
  const presenter = createPresenter(port);
  await presenter.load();

  presenter.setSquareEnvironment("Sandbox");
  presenter.setSquareLocationId(" sq-location-2 ");
  presenter.setSquareDeviceId(" sq-device-2 ");
  presenter.setLinklyEnvironment("Sandbox");
  presenter.setPaymentProvider("square");
  await presenter.testPaymentProvider("square");
  assert.deepEqual(port.paymentTests, [
    {
      provider: "square",
      input: {
        provider: "square",
        square: {
          environment: "Sandbox",
          locationId: "sq-location-2",
          deviceId: "sq-device-2",
        },
        linkly: null,
      },
    },
  ]);
  await presenter.savePaymentSettings();

  assert.equal(
    presenter.getState().confirmation?.kind,
    "change-payment-settings",
  );
  assert.deepEqual(port.savedPayments, []);
  await presenter.confirmDangerousAction();

  assert.deepEqual(port.savedPayments, [
    {
      provider: "square",
      square: {
        environment: "Sandbox",
        locationId: "sq-location-2",
        deviceId: "sq-device-2",
      },
      linkly: null,
    },
  ]);
  assert.equal(presenter.getState().statusCode, "payment-settings-saved");

  presenter.setPrinterEnabled(false);
  presenter.setPrinterPeripheralId(" printer-2 ");
  presenter.setPrinterPaper("58mm");
  presenter.setPrinterLocale("zh-CN");
  await presenter.savePrinterSettings();

  assert.deepEqual(port.savedPrinters, [
    {
      ...DEFAULT_RECEIPT_PRINTER_SETTINGS,
      printEnabled: false,
      peripheralId: "printer-2",
      paper: "58mm",
      locale: "zh-CN",
    },
  ]);
  assert.equal(presenter.getState().statusCode, "printer-settings-saved");
});

test("Square 不可用时仍可单独保存 Linkly 环境", async () => {
  const port = new FakeSettingsPort();
  const current = snapshot();
  port.snapshotValue = {
    ...current,
    square: {
      available: false,
      blockerCode: "square-not-configured",
      environment: "Production",
      deviceId: "",
      locationId: "",
    },
    paymentProvider: null,
  };
  const presenter = createPresenter(port);
  await presenter.load();

  presenter.setPaymentProvider("linkly");
  presenter.setLinklyEnvironment("Sandbox");
  await presenter.savePaymentSettings();

  assert.equal(
    presenter.getState().confirmation?.kind,
    "change-payment-settings",
  );
  await presenter.confirmDangerousAction();

  assert.deepEqual(port.savedPayments, [
    {
      provider: "linkly",
      square: null,
      linkly: { environment: "Sandbox" },
    },
  ]);
  assert.equal(presenter.getState().statusCode, "payment-settings-saved");
});

test("Square 与 Linkly 同时可用但未显式选择时支付保持 fail closed", async () => {
  const port = new FakeSettingsPort();
  port.snapshotValue = { ...snapshot(), paymentProvider: null };
  const presenter = createPresenter(port);
  await presenter.load();

  assert.equal(presenter.getState().paymentProvider, null);
  assert.equal(presenter.getState().paymentProviderDraft, null);

  await presenter.savePaymentSettings();
  await presenter.testPaymentProvider("square");

  assert.equal(presenter.getState().confirmation, null);
  assert.equal(presenter.getState().statusCode, "payment-settings-invalid");
  assert.deepEqual(port.paymentTests, []);
  assert.deepEqual(port.savedPayments, []);
});

test("显式切换到 Linkly 时只保存 Linkly，不能依赖提供方顺序", async () => {
  const port = new FakeSettingsPort();
  const presenter = createPresenter(port);
  await presenter.load();

  presenter.setPaymentProvider("linkly");
  presenter.setLinklyEnvironment("Sandbox");
  await presenter.testPaymentProvider("linkly");
  await presenter.savePaymentSettings();
  await presenter.confirmDangerousAction();

  const expected = {
    provider: "linkly" as const,
    square: null,
    linkly: { environment: "Sandbox" as const },
  };
  assert.deepEqual(port.paymentTests, [
    { provider: "linkly", input: expected },
  ]);
  assert.deepEqual(port.savedPayments, [expected]);
  assert.equal(presenter.getState().paymentProvider, "linkly");
  assert.equal(presenter.getState().paymentProviderDraft, "linkly");
});

test("快照选择不可用提供方时清空活动选择，且不可再次选中", async () => {
  const port = new FakeSettingsPort();
  const current = snapshot();
  port.snapshotValue = {
    ...current,
    paymentProvider: "square",
    square: {
      ...current.square,
      blockerCode: "square-not-configured",
    },
  };
  const presenter = createPresenter(port);
  await presenter.load();

  assert.equal(presenter.getState().paymentProvider, null);
  assert.equal(presenter.getState().paymentProviderDraft, null);

  presenter.setPaymentProvider("square");
  await presenter.savePaymentSettings();

  assert.equal(presenter.getState().paymentProviderDraft, null);
  assert.equal(presenter.getState().confirmation, null);
  assert.equal(presenter.getState().statusCode, "payment-settings-invalid");
});

test("API 切换、目录重置、设备重注册与应用重启必须先确认", async () => {
  const port = new FakeSettingsPort();
  const presenter = createPresenter(port);
  await presenter.load();

  presenter.setApiAddressDraft("https://staging.example.com/pos-api/");
  assert.equal(presenter.requestApiAddressChange(), true);
  assert.equal(port.apiAddressChanges.length, 0);
  assert.equal(presenter.getState().confirmation?.kind, "change-api-address");
  await presenter.confirmDangerousAction();
  assert.deepEqual(port.apiAddressChanges, [
    "https://staging.example.com/pos-api",
  ]);

  assert.equal(presenter.requestCatalogReset(), true);
  assert.equal(port.catalogResetCalls, 0);
  await presenter.confirmDangerousAction();
  assert.equal(port.catalogResetCalls, 1);

  presenter.setReregisterStoreCode(" BNE-02 ");
  presenter.setTerminalName(" iPad Front ");
  assert.equal(presenter.requestDeviceReregistration(), true);
  assert.equal(port.reregistrations.length, 0);
  await presenter.confirmDangerousAction();
  assert.deepEqual(port.reregistrations, [
    { targetStoreCode: "BNE-02", terminalName: "iPad Front" },
  ]);

  assert.equal(presenter.requestAppRestart(), true);
  assert.equal(port.restartCalls, 0);
  await presenter.confirmDangerousAction();
  assert.equal(port.restartCalls, 1);
  assert.equal(port.dangerousActionCalls, 4);
});

test("任何待同步、未决支付、活动购物车或耐久写入都会阻断危险操作并保留本地数据", async () => {
  const pendingCases: SettingsPendingDataSnapshot[] = [
    safePending({ pendingSaleCount: 1 }),
    safePending({ pendingReturnCount: 1 }),
    safePending({ unresolvedPaymentCount: 1 }),
    safePending({ pendingDurableWriteCount: 1 }),
    safePending({ hasActiveCart: true }),
  ];

  for (const pending of pendingCases) {
    const port = new FakeSettingsPort();
    port.pending = pending;
    const presenter = createPresenter(port);
    await presenter.load();
    assert.equal(presenter.requestCatalogReset(), true);

    await presenter.confirmDangerousAction();

    assert.equal(port.catalogResetCalls, 0);
    assert.equal(port.dangerousActionCalls, 1);
    assert.equal(presenter.getState().statusCode, "pending-local-data");
    assert.equal(presenter.getState().confirmation, null);
    assert.deepEqual(port.pending, pending);
  }
});

test("未决支付会阻断支付配置切换且不会持久化新环境", async () => {
  const port = new FakeSettingsPort();
  port.pending = safePending({ unresolvedPaymentCount: 1 });
  const presenter = createPresenter(port);
  await presenter.load();

  presenter.setSquareEnvironment("Sandbox");
  await presenter.savePaymentSettings();
  assert.equal(
    presenter.getState().confirmation?.kind,
    "change-payment-settings",
  );

  await presenter.confirmDangerousAction();

  assert.deepEqual(port.savedPayments, []);
  assert.equal(presenter.getState().statusCode, "pending-local-data");
  assert.equal(presenter.getState().square.environment, "Production");
});

test("安全检查失败时 fail closed，异常详情不会进入 UI 状态", async () => {
  const port = new FakeSettingsPort();
  port.failSafety = true;
  const presenter = createPresenter(port);
  await presenter.load();
  presenter.setReregisterStoreCode("BNE-02");
  presenter.requestDeviceReregistration();

  await presenter.confirmDangerousAction();

  assert.deepEqual(port.reregistrations, []);
  assert.equal(port.dangerousActionCalls, 1);
  assert.equal(presenter.getState().statusCode, "safety-check-failed");
  assert.equal(
    JSON.stringify(presenter.getState()).includes("Bearer secret"),
    false,
  );
});

test("确认期间锁定底层设置与其他确认，取消后才恢复编辑", async () => {
  const port = new FakeSettingsPort();
  const presenter = createPresenter(port);
  await presenter.load();
  const originalApiAddress = presenter.getState().apiAddressDraft;

  assert.equal(presenter.requestCatalogReset(), true);
  assert.equal(presenter.selectPane("payments"), false);
  presenter.setApiAddressDraft("https://should-not-apply.example.com");
  assert.equal(presenter.getState().apiAddressDraft, originalApiAddress);
  assert.equal(presenter.requestAppRestart(), false);
  assert.equal(presenter.getState().confirmation?.kind, "reset-catalog");

  presenter.cancelConfirmation();
  assert.equal(presenter.selectPane("payments"), true);
  presenter.setApiAddressDraft("https://allowed.example.com");
  assert.equal(
    presenter.getState().apiAddressDraft,
    "https://allowed.example.com",
  );
});

test("destroy 会 abort 等待中的扫码并让端口释放硬件监听", async () => {
  const port = new FakeSettingsPort();
  port.holdScannerUntilAbort = true;
  const presenter = createPresenter(port);
  await presenter.load();

  const testInFlight = presenter.testScanner();
  await Promise.resolve();
  presenter.destroy();
  await testInFlight;

  assert.equal(port.scannerAbortObserved, true);
});

test("API 地址拒绝凭据、query 与 fragment，且不会打开确认框", async () => {
  const port = new FakeSettingsPort();
  const presenter = createPresenter(port);
  await presenter.load();

  for (const invalid of [
    "not-a-url",
    "http://example.com/pos-api",
    "https://user:pass@example.com/pos-api",
    "https://example.com/pos-api?token=secret",
    "https://example.com/pos-api#secret",
  ]) {
    presenter.setApiAddressDraft(invalid);
    assert.equal(presenter.requestApiAddressChange(), false);
    assert.equal(presenter.getState().statusCode, "invalid-api-address");
    assert.equal(presenter.getState().confirmation, null);
  }

  presenter.setApiAddressDraft("http://192.168.31.246:5159/pos-api/");
  assert.equal(presenter.requestApiAddressChange(), true);
  assert.deepEqual(presenter.getState().confirmation, {
    kind: "change-api-address",
    apiBaseUrl: "http://192.168.31.246:5159/pos-api",
  });
});

test("测试候选 API 只检查规范地址并显示结果，不切换当前地址", async () => {
  const port = new FakeSettingsPort();
  const presenter = createPresenter(port);
  await presenter.load();

  presenter.setApiAddressDraft("http://192.168.31.246:5159/");
  await presenter.testApiAddress();

  assert.deepEqual(port.apiAddressTests, ["http://192.168.31.246:5159"]);
  assert.deepEqual(port.apiAddressChanges, []);
  assert.equal(
    presenter.getState().apiBaseUrl,
    "https://hotbargain.vip/pos-api",
  );
  assert.equal(
    presenter.getState().apiAddressDraft,
    "http://192.168.31.246:5159",
  );
  assert.equal(presenter.getState().confirmation, null);
  assert.equal(presenter.getState().statusCode, "api-health-check-passed");

  port.failApiHealth = true;
  await presenter.testApiAddress();
  assert.equal(
    presenter.getState().apiAddressDraft,
    "http://192.168.31.246:5159",
  );
  assert.equal(presenter.getState().statusCode, "api-health-check-failed");
});

test("候选 API 健康检查失败时保留旧地址", async () => {
  const port = new FakeSettingsPort();
  port.failApiHealth = true;
  const presenter = createPresenter(port);
  await presenter.load();

  presenter.setApiAddressDraft("https://offline.example.com/pos-api");
  assert.equal(presenter.requestApiAddressChange(), true);
  await presenter.confirmDangerousAction();

  assert.deepEqual(port.apiAddressChanges, []);
  assert.equal(
    presenter.getState().apiAddressDraft,
    "https://hotbargain.vip/pos-api",
  );
  assert.equal(
    presenter.getState().apiBaseUrl,
    "https://hotbargain.vip/pos-api",
  );
  assert.equal(presenter.getState().statusCode, "api-health-check-failed");
});

test("目录下载、硬件测试与客显开关使用单航班并返回安全结果", async () => {
  let releaseCatalog!: () => void;
  const port = new FakeSettingsPort();
  port.catalogHold = new Promise<void>((resolve) => {
    releaseCatalog = resolve;
  });
  const presenter = createPresenter(port);
  await presenter.load();

  const firstDownload = presenter.downloadCatalog();
  const secondDownload = presenter.downloadCatalog();
  assert.equal(firstDownload, secondDownload);
  assert.equal(port.catalogDownloadCalls, 1);
  releaseCatalog();
  await firstDownload;
  assert.equal(presenter.getState().catalog.snapshotId, "catalog-new");

  await presenter.testPrinter();
  await presenter.testScanner();
  await presenter.setExternalDisplayEnabled(true);
  await presenter.testExternalDisplay();

  assert.equal(port.printerTestCalls, 1);
  assert.equal(port.scannerTestCalls, 1);
  assert.equal(port.displayTestCalls, 1);
  assert.deepEqual(port.displayEnabledValues, [true]);
  assert.equal(
    presenter.getState().hardware.lastScannerValue,
    "••••0001 · 12 chars",
  );
  assert.equal(
    JSON.stringify(presenter.getState()).includes("930000000001"),
    false,
  );
  assert.equal(presenter.getState().externalDisplay.enabled, true);
});

test("设置呈现器立即恢复共享目录进度，销毁只退订且不取消刷新", async () => {
  let releaseCatalog!: () => void;
  const port = new FakeSettingsPort();
  port.catalogHold = new Promise<void>((resolve) => {
    releaseCatalog = resolve;
  });
  port.publishCatalogRefresh({
    kind: "running",
    storeCode: "BNE-01",
    progress: catalogProgress({
      currentStep: "products",
      elapsedMilliseconds: 76_000,
      overallPercent: 35,
      steps: [
        { step: "prepare", percent: 100 },
        {
          step: "products",
          percent: 25,
          completedItemCount: 500,
          totalItemCount: 2_000,
          completedPageCount: 1,
          totalPageCount: 4,
        },
        { step: "promotions", percent: 0 },
        { step: "activate", percent: 0 },
      ],
    }),
  });
  const presenter = createPresenter(port);

  const recoveredRefresh = presenter.getState().catalogRefresh;
  assert.equal(recoveredRefresh.kind, "running");
  assert.equal(
    recoveredRefresh.kind === "running"
      ? recoveredRefresh.progress.elapsedMilliseconds
      : null,
    76_000,
  );
  await presenter.load();

  const download = presenter.downloadCatalog();
  await Promise.resolve();
  presenter.destroy();

  assert.equal(port.catalogRefreshListenerCount, 0);
  assert.equal(port.catalogDownloadSignal?.aborted, false);
  releaseCatalog();
  await download;
});

test("共享目录刷新状态持续同步；成功更新摘要，失败只暴露稳定安全码", async () => {
  const port = new FakeSettingsPort();
  const presenter = createPresenter(port);
  await presenter.load();

  port.publishCatalogRefresh({
    kind: "success",
    storeCode: "BNE-01",
    summary: {
      snapshotId: "catalog-background",
      catalogVersion: "v-background",
      itemCount: 81,
      activatedAt: "2026-07-29T01:00:00.000Z",
    },
    progress: catalogProgress({
      currentStep: "activate",
      elapsedMilliseconds: 91_000,
      overallPercent: 100,
      steps: [
        { step: "prepare", percent: 100 },
        { step: "products", percent: 100 },
        { step: "promotions", percent: 100 },
        { step: "activate", percent: 100 },
      ],
    }),
  });

  assert.equal(presenter.getState().catalog.snapshotId, "catalog-background");
  assert.equal(presenter.getState().catalog.itemCount, 81);
  assert.equal(presenter.getState().catalogRefresh.kind, "success");

  port.publishCatalogRefresh({
    kind: "failed",
    storeCode: "BNE-01",
    errorCode: "catalog-refresh-network-failed",
    progress: catalogProgress({
      elapsedMilliseconds: 94_000,
    }),
  });
  assert.equal(presenter.getState().catalogRefresh.kind, "failed");
  assert.equal(
    JSON.stringify(presenter.getState()).includes("Bearer secret"),
    false,
  );
});

test("目录刷新中阻断所有会重绑运行时的危险操作，但不锁定页签", async () => {
  const port = new FakeSettingsPort();
  const presenter = createPresenter(port);
  await presenter.load();
  port.publishCatalogRefresh({
    kind: "running",
    storeCode: "BNE-01",
    progress: catalogProgress(),
  });

  presenter.setApiAddressDraft("https://next.example.test/pos-api");
  assert.equal(presenter.requestApiAddressChange(), false);
  assert.equal(presenter.requestCatalogReset(), false);
  await presenter.savePaymentSettings();
  presenter.setReregisterStoreCode("BNE-02");
  assert.equal(presenter.requestDeviceReregistration(), false);
  assert.equal(presenter.requestAppRestart(), false);
  assert.equal(presenter.getState().statusCode, "safety-check-failed");
  assert.equal(presenter.getState().confirmation, null);
  assert.equal(presenter.selectPane("payments"), true);
  assert.equal(port.dangerousActionCalls, 0);
});

test("确认后目录刷新才开始时，执行前再次 fail closed", async () => {
  const port = new FakeSettingsPort();
  const presenter = createPresenter(port);
  await presenter.load();
  presenter.setApiAddressDraft("https://next.example.test/pos-api");
  assert.equal(presenter.requestApiAddressChange(), true);

  port.publishCatalogRefresh({
    kind: "running",
    storeCode: "BNE-01",
    progress: catalogProgress(),
  });
  await presenter.confirmDangerousAction();

  assert.equal(port.dangerousActionCalls, 0);
  assert.equal(presenter.getState().confirmation, null);
  assert.equal(presenter.getState().statusCode, "safety-check-failed");
});

test("缺少细分权限时写操作 fail closed", async () => {
  const port = new FakeSettingsPort();
  const presenter = new SettingsPresenter({
    permissions: [SETTINGS_VIEW_PERMISSION],
    port,
  });
  await presenter.load();

  presenter.setSquareEnvironment("Sandbox");
  await presenter.savePaymentSettings();
  presenter.setPrinterEnabled(false);
  await presenter.savePrinterSettings();
  await presenter.setExternalDisplayEnabled(true);
  presenter.requestCatalogReset();
  presenter.setReregisterStoreCode("BNE-02");
  presenter.requestDeviceReregistration();

  assert.deepEqual(port.savedPayments, []);
  assert.deepEqual(port.savedPrinters, []);
  assert.deepEqual(port.displayEnabledValues, []);
  assert.equal(presenter.getState().confirmation, null);
  assert.equal(presenter.getState().statusCode, "permission-required");
});

function createPresenter(port: FakeSettingsPort): SettingsPresenter {
  return new SettingsPresenter({ permissions: allPermissions, port });
}

function safePending(
  patch: Partial<SettingsPendingDataSnapshot> = {},
): SettingsPendingDataSnapshot {
  return {
    hasActiveCart: false,
    pendingDurableWriteCount: 0,
    pendingReturnCount: 0,
    pendingSaleCount: 0,
    unresolvedPaymentCount: 0,
    ...patch,
  };
}

class FakeSettingsPort implements SettingsControlPort {
  public loadCalls = 0;
  public safetyCalls = 0;
  public dangerousActionCalls = 0;
  public catalogDownloadCalls = 0;
  public catalogResetCalls = 0;
  public printerTestCalls = 0;
  public scannerTestCalls = 0;
  public displayTestCalls = 0;
  public restartCalls = 0;
  public catalogHold: Promise<void> | null = null;
  public catalogDownloadSignal: AbortSignal | null = null;
  public catalogRefreshListenerCount = 0;
  private catalogRefreshState: CatalogRefreshState = { kind: "idle" };
  private readonly catalogRefreshListeners = new Set<() => void>();
  public failSafety = false;
  public failApiHealth = false;
  public pending = safePending();
  public snapshotValue: SettingsSnapshot | null = null;
  public holdScannerUntilAbort = false;
  public scannerAbortObserved = false;
  public readonly apiAddressChanges: string[] = [];
  public readonly apiAddressTests: string[] = [];
  public readonly savedPayments: SettingsPaymentSettingsInput[] = [];
  public readonly paymentTests: Readonly<{
    provider: "square" | "linkly";
    input: SettingsPaymentSettingsInput;
  }>[] = [];
  public readonly savedPrinters: ReceiptPrinterSettings[] = [];
  public readonly displayEnabledValues: boolean[] = [];
  public readonly reregistrations: {
    targetStoreCode: string;
    terminalName?: string;
  }[] = [];

  public async loadSnapshot(): Promise<SettingsSnapshot> {
    this.loadCalls += 1;
    return this.snapshotValue ?? snapshot();
  }

  public getCatalogRefreshState() {
    return this.catalogRefreshState;
  }

  public subscribeCatalogRefresh(listener: () => void): () => void {
    this.catalogRefreshListeners.add(listener);
    this.catalogRefreshListenerCount = this.catalogRefreshListeners.size;
    return () => {
      this.catalogRefreshListeners.delete(listener);
      this.catalogRefreshListenerCount = this.catalogRefreshListeners.size;
    };
  }

  public publishCatalogRefresh(
    state: CatalogRefreshState,
  ): void {
    this.catalogRefreshState = state;
    for (const listener of this.catalogRefreshListeners) listener();
  }

  public async testApiAddress(apiBaseUrl: string): Promise<boolean> {
    this.apiAddressTests.push(apiBaseUrl);
    return !this.failApiHealth;
  }

  public async executeDangerousAction(
    action: SettingsDangerousConfirmation,
  ): Promise<SettingsDangerousActionResult> {
    this.dangerousActionCalls += 1;
    if (this.failSafety) {
      return {
        status: "blocked" as const,
        reason: "safety-check-failed" as const,
      };
    }
    if (
      this.pending.hasActiveCart ||
      this.pending.pendingDurableWriteCount > 0 ||
      this.pending.pendingReturnCount > 0 ||
      this.pending.pendingSaleCount > 0 ||
      this.pending.unresolvedPaymentCount > 0
    ) {
      return {
        status: "blocked" as const,
        reason: "pending-local-data" as const,
      };
    }
    if (action.kind === "change-api-address" && this.failApiHealth) {
      return {
        status: "blocked" as const,
        reason: "candidate-unreachable" as const,
      };
    }
    if (action.kind === "change-api-address") {
      this.apiAddressChanges.push(action.apiBaseUrl);
      return { status: "completed" as const, kind: action.kind };
    }
    if (action.kind === "reset-catalog") {
      this.catalogResetCalls += 1;
      return {
        status: "completed" as const,
        kind: action.kind,
        catalog: {
          snapshotId: null,
          itemCount: 0,
          activatedAt: null,
        },
      };
    }
    if (action.kind === "reregister-device") {
      this.reregistrations.push({
        targetStoreCode: action.targetStoreCode,
        ...(action.terminalName ? { terminalName: action.terminalName } : {}),
      });
      return { status: "completed" as const, kind: action.kind };
    }
    if (action.kind === "change-payment-settings") {
      this.savedPayments.push(action.input);
      return { status: "completed" as const, kind: action.kind };
    }
    this.restartCalls += 1;
    return { status: "completed" as const, kind: action.kind };
  }

  public async downloadCatalog(signal: AbortSignal) {
    this.catalogDownloadCalls += 1;
    this.catalogDownloadSignal = signal;
    await this.catalogHold;
    return {
      snapshotId: "catalog-new",
      itemCount: 77,
      activatedAt: "2026-07-28T02:00:00.000Z",
    };
  }

  public async testPaymentProvider(
    provider: "square" | "linkly",
    input: SettingsPaymentSettingsInput,
  ): Promise<void> {
    this.paymentTests.push({ provider, input });
  }

  public async savePrinterSettings(
    input: ReceiptPrinterSettings,
  ): Promise<void> {
    this.savedPrinters.push(input);
  }

  public async scanPrinters() {
    return [];
  }

  public async connectPrinter(): Promise<void> {}

  public async testPrinter(): Promise<void> {
    this.printerTestCalls += 1;
  }

  public async testScanner(signal?: AbortSignal) {
    this.scannerTestCalls += 1;
    if (this.holdScannerUntilAbort) {
      await new Promise<void>((_resolve, reject) => {
        signal?.addEventListener(
          "abort",
          () => {
            this.scannerAbortObserved = true;
            reject(new Error("scanner test aborted"));
          },
          { once: true },
        );
      });
    }
    return { source: "hid" as const, value: "930000000001" };
  }

  public async setExternalDisplayEnabled(enabled: boolean): Promise<void> {
    this.displayEnabledValues.push(enabled);
  }

  public async testExternalDisplay(): Promise<void> {
    this.displayTestCalls += 1;
  }

  public async checkForAppUpdate() {
    return {
      channel: "production",
      currentVersion: "1.0.0",
      availableVersion: "1.1.0",
      updateRequired: false,
      restartAvailable: true,
    };
  }
}

function catalogProgress(
  patch: Partial<
    Extract<
      ReturnType<SettingsControlPort["getCatalogRefreshState"]>,
      { kind: "running" }
    >["progress"]
  > = {},
) {
  return {
    currentStep: "prepare" as const,
    overallPercent: 0,
    elapsedMilliseconds: 0,
    steps: [
      { step: "prepare" as const, percent: 0 },
      { step: "products" as const, percent: 0 },
      { step: "promotions" as const, percent: 0 },
      { step: "activate" as const, percent: 0 },
    ],
    ...patch,
  };
}

function snapshot(): SettingsSnapshot {
  return {
    apiBaseUrl: "https://hotbargain.vip/pos-api",
    appUpdate: {
      channel: "production",
      currentVersion: "1.0.0",
      availableVersion: null,
      updateRequired: false,
      restartAvailable: false,
    },
    catalog: {
      snapshotId: "catalog-old",
      itemCount: 42,
      activatedAt: "2026-07-27T01:00:00.000Z",
    },
    device: {
      deviceCode: "POS-01",
      storeCode: "BNE-01",
      storeName: "Brisbane",
      terminalName: "Front",
    },
    externalDisplay: {
      available: true,
      enabled: false,
      status: "connected",
    },
    hardware: {
      printerStatus: "connected",
      scannerStatus: "ready",
      externalDisplayStatus: "connected",
      lastScannerValue: null,
    },
    paymentProvider: "square",
    linkly: {
      available: true,
      blockerCode: null,
      environment: "Production",
    },
    printer: DEFAULT_RECEIPT_PRINTER_SETTINGS,
    square: {
      available: true,
      blockerCode: null,
      environment: "Production",
      deviceId: "sq-device-1",
      locationId: "sq-location-1",
    },
  };
}
