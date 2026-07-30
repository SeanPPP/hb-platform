import Constants from "expo-constants";
import * as Crypto from "expo-crypto";
import * as ExpoNetwork from "expo-network";
import * as Updates from "expo-updates";
import { Linking } from "react-native";

import {
  AppUpdateCoordinator,
  AppUpdateOrchestrator,
  ExpoOtaUpdatePort,
  HbposPosIpadOtaUpdateApi,
  HbposPosIpadUpdateApi,
  OtaUpdateCoordinator,
  shouldCheckOtaPolicy,
  UpdateTransitionLeaseCoordinator,
} from "../../features/app-updates";
import {
  HbposAttendanceSecurityApi,
  HbposOperationAuditReadApi,
} from "../../features/attendance-audit";
import {
  CustomerDisplayAdvertisementCache,
} from "../../features/customer-display";
import type { SettingsPaymentSettingsInput } from "../../features/settings";
import {
  createAxiosHbposTransport,
  type HbposAuthenticationFailureHandler,
  type HbposRequestCredentialProvider,
  type HbposRequestCredentials,
} from "../api/axios-transport";
import {
  HbposCashierApi,
  HbposDeviceApi,
} from "../api/hbpos-api";
import type { ExternalCustomerDisplayPort } from "../contracts/external-display";
import { createExpoKeychainDatabaseKeyProvider } from "../db/expo-keychain-database-key-provider";
import { ExpoSqliteDriver } from "../db/expo-sqlite-driver";
import { PosDatabase } from "../db/pos-database";
import {
  createExpoAttendanceSecurityAdapter,
} from "../peripherals/attendance-security/native";
import {
  customerDisplayAdvertisementCacheRootUri,
  ExpoAdvertisementFileSystem,
  externalDisplay,
} from "../peripherals/customer-display/native";
import { HidScannerRouter } from "../peripherals/scanner";
import { SecurityApiCredentialProvider } from "../security/api-credential-provider";
import { CashierAuthenticationService } from "../security/cashier-authentication";
import { CashierSessionInvalidationBus } from "../security/cashier-session-invalidation";
import { DeviceSessionCoordinator } from "../security/device-session";
import { ExpoSecureStoreAdapter } from "../security/expo-secure-store";
import {
  mergePosPaymentPublicConfiguration,
  normalizeTrustedApiOrigins,
  PosPublicRuntimeConfigurationStore,
} from "../security/pos-public-runtime-configuration";
import {
  CashierAuthorizationStore,
  CashierSessionCache,
  DeviceCredentialStore,
  DeviceLockStore,
  InstallationIdentityStore,
  PendingDeviceRegistrationStore,
} from "../security/secure-storage";
import { SensitivePayloadEncryptor } from "../security/sensitive-payload-encryptor";

import { createEmergencyCashierRuntime } from "./emergency-cashier-runtime";
import { createExpoAttendanceRuntimeConfiguration } from "./expo-attendance-runtime-configuration";
import { createLazyExpoPrinterAdapter } from "./expo-printer-adapter";
import {
  createSettingsApiHealthProbe,
  settingsAppUpdateSnapshot,
  settingsPaymentConfiguration,
} from "./expo-settings-configuration";
import { resolveLocalDeviceState } from "./local-device-state";
import { createPaymentProviderRuntimeBootstrap } from "./payment-provider-runtime-bootstrap";
import {
  configuredLinklyEnvironment,
  type PosPaymentPublicExtra,
} from "./payment-runtime-config";
import {
  PosRuntimeController,
  type PosRuntimeServices,
} from "./pos-runtime";
import {
  createProductionPosRuntimeServices,
  type ProductionPosRuntimeServices,
} from "./production-pos-service-composition";
import {
  createPublicCashierInvalidation,
  type PosCashierInvalidationRuntimeService,
} from "./public-cashier-invalidation";
import {
  createPublicDeviceSession,
  type PosDeviceSessionRuntimeService,
} from "./public-device-session";
import { resolveHbposApiUrl } from "./runtime-config";
import { HbposSettingsPaymentTestApi } from "./settings-payment-test-api";
import { SettingsScannerTestCoordinator } from "./settings-scanner-test";
import { resolveStartupDeviceGate } from "./startup-device-gate";

const POS_DATABASE_NAME = "hb-pos-ipad.db";
const POS_APP_ID = "com.hbweb.posipad";

type HbposExtraConfig = Readonly<{
  hbpos?: Readonly<{
    apiBaseUrl?: string;
    automaticOtaChecks?: boolean;
    buildProfile?: string;
    businessTimeZone?: string;
    trustedApiOrigins?: readonly string[];
  }>;
  payments?: PosPaymentPublicExtra;
}>;

export type ExpoPosRuntimeServices = PosRuntimeServices &
  ProductionPosRuntimeServices &
  Readonly<{
    apiBaseUrl: string;
    deviceSession: PosDeviceSessionRuntimeService;
    cashierSessionInvalidation: PosCashierInvalidationRuntimeService;
    externalDisplay: ExternalCustomerDisplayPort;
    appUpdates: AppUpdateOrchestrator;
    scanner: Readonly<{ router: HidScannerRouter }>;
  }>;

type ExpoSettingsDevicePresentation = Readonly<{
  deviceCode: string;
  storeCode: string;
  storeName: string;
  terminalName: string;
}>;

export async function readSettingsDevicePresentation(
  deviceSession: Pick<
    PosDeviceSessionRuntimeService,
    "getDevicePresentation"
  >,
): Promise<ExpoSettingsDevicePresentation> {
  const presentation = await deviceSession.getDevicePresentation();
  if (!presentation) {
    throw new Error("SETTINGS_DEVICE_IDENTITY_REQUIRED");
  }
  return Object.freeze({
    deviceCode: presentation.deviceCode,
    storeCode: presentation.storeCode,
    storeName: presentation.storeName ?? "",
    terminalName: "",
  });
}

class ExpoNetworkStatus {
  private online = false;

  public async isOnline(): Promise<boolean> {
    const state = await ExpoNetwork.getNetworkStateAsync();
    this.online =
      state.isConnected === true &&
      state.isInternetReachable !== false;
    return this.online;
  }

  public currentOnline(): boolean {
    return this.online;
  }
}

/**
 * 解决 DeviceSessionCoordinator 与 Axios credential provider 的组合环：
 * 注册 API 先构造，但所有请求发生前真实安全提供者必定已绑定。
 */
class DeferredSecurityBridge
  implements HbposRequestCredentialProvider, HbposAuthenticationFailureHandler
{
  private delegate: SecurityApiCredentialProvider | undefined;

  public bind(delegate: SecurityApiCredentialProvider): void {
    if (this.delegate) {
      throw new Error("Security credential bridge is already bound.");
    }
    this.delegate = delegate;
  }

  public getCredentials(): Promise<HbposRequestCredentials> {
    return this.requireDelegate().getCredentials();
  }

  public onUnauthorized(): Promise<void> {
    return this.requireDelegate().onUnauthorized();
  }

  public onForbidden(): Promise<void> {
    return this.requireDelegate().onForbidden();
  }

  private requireDelegate(): SecurityApiCredentialProvider {
    if (!this.delegate) {
      throw new Error("Security credential bridge is not initialized.");
    }
    return this.delegate;
  }
}

export async function createExpoPosRuntimeServices(): Promise<ExpoPosRuntimeServices> {
  const publicExtra =
    Constants.expoConfig?.extra as HbposExtraConfig | undefined;
  const secureStore = new ExpoSecureStoreAdapter();
  const attendanceSecurity =
    createExpoAttendanceSecurityAdapter();
  const trustedApiOrigins = normalizeTrustedApiOrigins([
    ...(publicExtra?.hbpos?.trustedApiOrigins ?? []),
    ...(publicExtra?.hbpos?.apiBaseUrl
      ? [publicExtra.hbpos.apiBaseUrl]
      : [resolveHbposApiUrl(undefined)]),
  ]);
  const publicConfigurationStore =
    new PosPublicRuntimeConfigurationStore(
      secureStore,
      trustedApiOrigins,
    );
  const persistedPublicConfiguration =
    await publicConfigurationStore.load();
  const configuredApiUrl =
    persistedPublicConfiguration.apiBaseUrl ??
    publicExtra?.hbpos?.apiBaseUrl;
  const apiBaseUrl = resolveHbposApiUrl(configuredApiUrl);
  const paymentPublicConfiguration =
    mergePosPaymentPublicConfiguration(
      publicExtra?.payments,
      persistedPublicConfiguration.payments,
    );
  const installation = new InstallationIdentityStore(
    secureStore,
    () => Crypto.randomUUID(),
  );
  const deviceCredentials = new DeviceCredentialStore(secureStore);
  const pendingRegistration = new PendingDeviceRegistrationStore(secureStore);
  const deviceLock = new DeviceLockStore(secureStore);
  const cashierAuthorization = new CashierAuthorizationStore(
    secureStore,
    {
      getSystemUptimeMilliseconds: () =>
        attendanceSecurity.getSystemUptimeMilliseconds(),
      nowEpochMs: Date.now,
    },
  );
  const cashierSessionInvalidation = new CashierSessionInvalidationBus();
  const publicCashierSessionInvalidation = createPublicCashierInvalidation(
    cashierSessionInvalidation,
  );
  const securityBridge = new DeferredSecurityBridge();
  const transport = createAxiosHbposTransport(
    apiBaseUrl,
    securityBridge,
    undefined,
    securityBridge,
  );
  const deviceApi = new HbposDeviceApi(transport);
  const deviceSession = new DeviceSessionCoordinator(
    deviceApi,
    installation,
    deviceCredentials,
    deviceLock,
    pendingRegistration,
  );
  const publicDeviceSession = createPublicDeviceSession(
    deviceSession,
    () => deviceApi.listRegistrationStores(),
  );
  const lockRuntimeDevice = async (reason: string): Promise<void> => {
    await deviceSession.lockFromAuthorizationFailure(reason);
    // 锁定已经耐久化后才通知 React 重建 runtime；锁机失败必须保留原始异常。
    cashierSessionInvalidation.notify("forbidden");
  };
  securityBridge.bind(
    new SecurityApiCredentialProvider(
      deviceSession,
      cashierAuthorization,
      cashierSessionInvalidation,
    ),
  );

  const network = new ExpoNetworkStatus();
  const cashierApi = new HbposCashierApi(transport);
  const cashierCache = new CashierSessionCache(secureStore, {
    sha256Hex: async (material) =>
      Crypto.digestStringAsync(
        Crypto.CryptoDigestAlgorithm.SHA256,
        `${await installation.getOrCreate()}\n${material}`,
        { encoding: Crypto.CryptoEncoding.HEX },
      ),
  });
  const [locked, credentials, pending, installationId, online] = await Promise.all([
    deviceLock.isLocked(),
    deviceCredentials.load(),
    pendingRegistration.load(),
    installation.getOrCreate(),
    network.isOnline(),
  ]);
  const localDevice = resolveLocalDeviceState({
    locked,
    credentials,
    pending,
    installationId,
  });
  const startupGate = await resolveStartupDeviceGate({
    internetReachable: online,
    verifyCurrentDevice: () => deviceSession.poll(),
    readLocalDevice: async () => {
      const [
        currentLocked,
        currentCredentials,
        currentPending,
        currentInstallationId,
      ] =
        await Promise.all([
          deviceLock.isLocked(),
          deviceCredentials.load(),
          pendingRegistration.load(),
          installation.getOrCreate(),
        ]);
      return resolveLocalDeviceState({
        locked: currentLocked,
        credentials: currentCredentials,
        pending: currentPending,
        installationId: currentInstallationId,
      });
    },
    lockDevice: lockRuntimeDevice,
  });
  // startup gate 的 verify 可能刚把 pending 设备批准并写入 Keychain；
  // 组合根必须重新读取，不能继续捕获启动前的 unregistered 身份。
  const runtimeCredentials = await deviceCredentials.load();
  const database = await PosDatabase.open({
    databaseName: POS_DATABASE_NAME,
    driver: new ExpoSqliteDriver(),
    keyProvider: createExpoKeychainDatabaseKeyProvider(secureStore),
    nowIso: () => new Date().toISOString(),
  });

  try {
    const now = () => new Date();
    const createId = () => Crypto.randomUUID();
    const sha256Hex = (material: string) =>
      Crypto.digestStringAsync(
        Crypto.CryptoDigestAlgorithm.SHA256,
        material,
        { encoding: Crypto.CryptoEncoding.HEX },
      );
    const encryptor = new SensitivePayloadEncryptor(secureStore, {
      getRandomBytes: (length) => Crypto.getRandomBytesAsync(length),
    });
    const attendanceCredentials =
      runtimeCredentials?.hardwareId === installationId
        ? runtimeCredentials
        : null;
    const attendanceAuthorizationMarker = attendanceCredentials
      ? (
          await sha256Hex(attendanceCredentials.authorizationCode)
        ).toUpperCase()
      : null;
    const attendanceRemote = attendanceCredentials
      ? new HbposAttendanceSecurityApi(transport)
      : null;
    const attendanceCrypto = attendanceCredentials
      ? attendanceSecurity
      : null;
    const attendancePersistence =
      attendanceCredentials && attendanceAuthorizationMarker
        ? database.attendanceSecurity(encryptor, {
            apiPartition: apiBaseUrl,
            storeCode: attendanceCredentials.storeCode,
            deviceCode: attendanceCredentials.deviceCode,
            hardwareId: attendanceCredentials.hardwareId,
            authorizationMarker: attendanceAuthorizationMarker,
          })
        : null;
    const emergencyCashier =
      attendanceRemote &&
      attendanceCrypto &&
      attendancePersistence
        ? createEmergencyCashierRuntime({
            authorization: cashierAuthorization,
            cache:
              attendancePersistence.emergencyPublicKeyCache,
            crypto: attendanceCrypto,
            remote: attendanceRemote,
            systemUptime: attendanceCrypto,
            trustedTime:
              attendancePersistence.emergencyTrustedTime,
          })
        : null;
    const cashierAuthentication = new CashierAuthenticationService(
      cashierApi,
      cashierCache,
      network,
      cashierAuthorization,
      deviceLock,
      emergencyCashier?.authentication,
    );
    // 主管代授权复用同一加密离线缓存，但紧急二维码不得替换当前收银员或主管票据。
    const supervisorAuthentication =
      new CashierAuthenticationService(
        cashierApi,
        cashierCache,
        network,
        undefined,
        deviceLock,
      );
    const paymentBootstrap =
      await createPaymentProviderRuntimeBootstrap({
        transport,
        extra: paymentPublicConfiguration,
        voucherProtectedTokens: database.voucherProtectedTokens(
          encryptor,
          createId,
        ),
      });
    const installmentPaymentPersistence =
      database.installmentPaymentPersistence(encryptor, createId);
    const installmentPaymentBootstrap =
      await createPaymentProviderRuntimeBootstrap({
        transport,
        extra: paymentPublicConfiguration,
        voucherProtectedTokens:
          installmentPaymentPersistence.voucherProtectedTokens,
      });
    const appVersion =
      Constants.nativeAppVersion ??
      Constants.expoConfig?.version ??
      "0.0.0";
    const runtimeVersion = Updates.runtimeVersion ?? appVersion;
    let appUpdateSafety:
      | ProductionPosRuntimeServices["appUpdateSafety"]
      | null = null;
    const updateCacheScope = Object.freeze({
      apiOrigin: new URL(apiBaseUrl).origin,
      storeCode: runtimeCredentials?.storeCode ?? "unregistered",
      runtimeVersion,
      installedVersion: appVersion,
    });
    const nativeAppUpdates = new AppUpdateCoordinator({
      metadata: {
        version: appVersion,
        build: Constants.nativeBuildVersion ?? "0",
        runtimeVersion,
      },
      policyStore: database.appUpdatePolicy(updateCacheScope),
      remote: new HbposPosIpadUpdateApi(transport),
    });
    const otaInstaller = new ExpoOtaUpdatePort({
      enabled: Updates.isEnabled,
      runtimeVersion: Updates.runtimeVersion,
      updates: {
        setUpdateRequestHeadersOverride: (headers) =>
          Updates.setUpdateRequestHeadersOverride(headers),
        checkForUpdateAsync: () => Updates.checkForUpdateAsync(),
        fetchUpdateAsync: () => Updates.fetchUpdateAsync(),
        reloadAsync: () => Updates.reloadAsync(),
      },
    });
    const otaAppUpdates = new OtaUpdateCoordinator({
      automaticChecksEnabled: shouldCheckOtaPolicy({
        automaticChecksConfigured:
          publicExtra?.hbpos?.automaticOtaChecks === true,
        updatesEnabled: Updates.isEnabled,
      }),
      metadata: {
        runtimeVersion,
        currentUpdateId: Updates.updateId,
        currentUpdateGroupId: readCurrentUpdateGroupId(
          Updates.manifest,
        ),
      },
      policyStore: database.otaUpdatePolicy(updateCacheScope),
      remote: new HbposPosIpadOtaUpdateApi(transport),
      installer: otaInstaller,
    });
    const appUpdateTransition =
      new UpdateTransitionLeaseCoordinator();
    const appUpdates = new AppUpdateOrchestrator({
      installedVersion: appVersion,
      native: nativeAppUpdates,
      ota: otaAppUpdates,
      transition: appUpdateTransition,
      appStore: {
        open: (url) => Linking.openURL(url),
      },
      safety: {
        getSafetySnapshot: () => {
          if (!appUpdateSafety) {
            throw new Error(
              "App update safety is not initialized.",
            );
          }
          return appUpdateSafety.getSnapshot();
        },
      },
    });
    const printer = createLazyExpoPrinterAdapter();
    const scannerRouter = new HidScannerRouter();
    const scannerTest =
      new SettingsScannerTestCoordinator(scannerRouter);
    const paymentTestApi =
      new HbposSettingsPaymentTestApi(transport);
    const currentPaymentSettings =
      settingsPaymentConfiguration(paymentPublicConfiguration);
    const updateChannel = Updates.channel?.trim() || "embedded";
    const readUpdateSnapshot = () =>
      settingsAppUpdateSnapshot({
        channel: updateChannel,
        currentVersion: appVersion,
        policy: appUpdates.getPolicy(),
        restartAvailable: Updates.isEnabled,
      });
    const probeApiHealth = createSettingsApiHealthProbe(
      (url, init) => fetch(url, init),
    );
    const attendanceAudit =
      attendanceCredentials &&
      attendanceAuthorizationMarker &&
      attendanceRemote &&
      attendanceCrypto &&
      attendancePersistence
        ? createExpoAttendanceRuntimeConfiguration({
            attendanceSecurity: attendanceRemote,
            authorizationMarker: attendanceAuthorizationMarker,
            connectivity: network,
            credentials: attendanceCredentials,
            localAudit: database.operationAudits({
              storeCode: attendanceCredentials.storeCode,
              deviceCode: attendanceCredentials.deviceCode,
            }),
            qrCache: attendancePersistence.attendanceQrCache,
            qrCrypto: attendanceCrypto,
            readCurrentCredentials: () =>
              deviceSession.getTransportCredentials(),
            readStoreName: async () => {
              const receipt =
                await database.settings().getReceiptPrinterSettings();
              return receipt.storeName;
            },
            remoteAudit: new HbposOperationAuditReadApi(
              transport,
              attendanceCredentials.storeCode,
              attendanceCredentials.deviceCode,
            ),
            scheduler: {
              every(intervalMs, task) {
                const timer = setInterval(task, intervalMs);
                return () => clearInterval(timer);
              },
            },
            sha256Hex,
          })
        : null;
    const composition = createProductionPosRuntimeServices({
      database,
      transport,
      encryptor,
      syncSecurity: {
        lockDevice: lockRuntimeDevice,
      },
      auditMetadata: {
        storeCode: runtimeCredentials?.storeCode ?? "unregistered",
        deviceCode: runtimeCredentials?.deviceCode ?? "unregistered",
        appVersion,
        instanceId: installationId,
      },
      supportAppId: POS_APP_ID,
      clock: {
        now,
        nowIso: () => now().toISOString(),
      },
      systemUptimeMilliseconds: () =>
        attendanceSecurity.getSystemUptimeMilliseconds(),
      createId,
      random: Math.random,
      sha256Hex,
      // 仅返回惰性 adapter；requireNativeModule("HbPrinter") 要到实际硬件动作才会调用。
      createPrinter: () => printer,
      externalDisplay,
      customerDisplayAdvertisementCacheRootUri,
      advertisementCache: new CustomerDisplayAdvertisementCache({
        rootUri: customerDisplayAdvertisementCacheRootUri,
        files: new ExpoAdvertisementFileSystem(),
        sha256Hex,
      }),
      ...(publicExtra?.hbpos?.businessTimeZone
        ? {
            businessTimeZone:
              publicExtra.hbpos.businessTimeZone,
          }
        : {}),
      connectivity: network,
      cashierAuthentication,
      cashierSessionSecurity: {
        getDeviceIdentity: () => deviceSession.getDeviceIdentity(),
        clearAuthorization: () => cashierAuthorization.clear(),
        subscribeSessionInvalidation: (listener) =>
          cashierSessionInvalidation.subscribe(() => listener()),
      },
      newTransactionGate: appUpdates,
      appUpdateTransition,
      operationAuthorization: {
        cashierAuthentication: supervisorAuthentication,
      },
      ...(attendanceAudit ? { attendanceAudit } : {}),
      cashierLock: {
        onLocked: () => {
          cashierSessionInvalidation.notify("manual-lock");
        },
      },
      payments: {
        bootstrap: paymentBootstrap,
        linklyEnvironment: configuredLinklyEnvironment(
          paymentPublicConfiguration,
        ),
      },
      installments: {
        bootstrap: installmentPaymentBootstrap,
      },
      settings: {
        apiBaseUrl,
        appVersion,
        updateChannel,
        printer,
        readDevicePresentation: () =>
          readSettingsDevicePresentation(publicDeviceSession),
        paymentConfiguration: {
          current: currentPaymentSettings,
          availability: {
            square: paymentAvailability(
              paymentBootstrap.configurationAvailability.getAvailability(
                "square",
              ),
            ),
            linkly: paymentAvailability(
              paymentBootstrap.configurationAvailability.getAvailability(
                "linkly-cloud",
              ),
            ),
          },
          test: async (
            provider: "square" | "linkly",
            configuration: SettingsPaymentSettingsInput,
            signal: AbortSignal,
          ) => {
            throwIfRuntimeAborted(signal);
            await paymentTestApi.test(provider, configuration);
            throwIfRuntimeAborted(signal);
          },
          save: (configuration) =>
            publicConfigurationStore.savePayments(configuration),
        },
        apiConfiguration: {
          allowSwitchWithPendingLocalData: __DEV__,
          probe: probeApiHealth,
          save: (nextApiBaseUrl) =>
            publicConfigurationStore.saveApiBaseUrl(nextApiBaseUrl),
        },
        runtimeReload: {
          reload: async (signal) => {
            throwIfRuntimeAborted(signal);
            await Updates.reloadAsync();
          },
        },
        device: {
          reregister: async (request, signal) => {
            throwIfRuntimeAborted(signal);
            const result = await deviceSession.reregister(request);
            throwIfRuntimeAborted(signal);
            if (result.status !== "authorized") {
              throw new Error(
                `SETTINGS_DEVICE_REREGISTRATION_${result.status.toUpperCase()}`,
              );
            }
          },
        },
        scanner: {
          status: "ready",
          test: (signal) => scannerTest.test(signal),
        },
        appUpdate: {
          snapshot: readUpdateSnapshot,
          check: async (signal) => {
            throwIfRuntimeAborted(signal);
            await appUpdates.refreshOnForeground();
            throwIfRuntimeAborted(signal);
            return readUpdateSnapshot();
          },
          restart: async (signal) => {
            throwIfRuntimeAborted(signal);
            const decision = await appUpdates.restartIfSafe();
            throwIfRuntimeAborted(signal);
            return decision.canRestart;
          },
        },
      },
    });
    appUpdateSafety = composition.appUpdateSafety;
    // 销售路由拿到 runtime 前必须先处理崩溃遗留的 HoldClear/RecallActive fence。
    // 初始化失败保持数据库可恢复并让启动 fail-closed，绝不开放普通收银。
    await composition.initialize();
    if (online) {
      // 公钥同步失败只关闭紧急登录；普通在线/离线收银登录保持原有路径。
      void emergencyCashier?.syncPublicKeys();
    }
    const {
      initialize: _initialize,
      shutdownBackgroundWork,
      ...services
    } = composition;
    void _initialize;

    return {
      ...services,
      apiBaseUrl,
      deviceSession: publicDeviceSession,
      cashierSessionInvalidation: publicCashierSessionInvalidation,
      externalDisplay,
      appUpdates,
      scanner: Object.freeze({ router: scannerRouter }),
      shutdown: async () => {
        // 先覆盖公共外屏，再与 401/403/手动锁屏使用同一可信桥撤销可信会话。
        if (services.customerDisplay.status === "available") {
          services.customerDisplay.stopAdvertisements();
          await services.customerDisplay
            .clearSensitiveContent()
            .catch(() => undefined);
        }
        cashierSessionInvalidation.notify("manual-lock");
        appUpdates.dispose();
        // 页面离开不会取消目录刷新；只有 runtime 关闭会先中止并等待 staging 清理。
        await shutdownBackgroundWork();
        await database.close();
      },
      backend: startupGate.backend,
      device:
        startupGate.backend === "unverified"
          ? localDevice
          : startupGate.device,
    };
  } catch (error) {
    await database.close();
    throw error;
  }
}

function readCurrentUpdateGroupId(manifest: unknown): string | null {
  if (!manifest || typeof manifest !== "object" || Array.isArray(manifest)) {
    return null;
  }
  const record = manifest as Record<string, unknown>;
  const metadata =
    record.metadata &&
    typeof record.metadata === "object" &&
    !Array.isArray(record.metadata)
      ? (record.metadata as Record<string, unknown>)
      : null;
  const candidate =
    metadata?.updateGroupId ??
    metadata?.updateGroup ??
    record.updateGroupId ??
    null;
  if (typeof candidate !== "string") return null;
  const normalized = candidate.trim().toLowerCase();
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/u.test(
    normalized,
  )
    ? normalized
    : null;
}

export function createExpoPosRuntimeController(): PosRuntimeController<ExpoPosRuntimeServices> {
  return new PosRuntimeController(createExpoPosRuntimeServices);
}

function paymentAvailability(input: Readonly<{
  available: boolean;
  blocker: string | null;
}>): Readonly<{ available: boolean; blockerCode: string | null }> {
  return Object.freeze({
    available: input.available,
    blockerCode: input.blocker,
  });
}

function throwIfRuntimeAborted(signal: AbortSignal): void {
  if (signal.aborted) {
    throw Object.assign(
      new Error("Runtime settings operation aborted."),
      { name: "AbortError" },
    );
  }
}
