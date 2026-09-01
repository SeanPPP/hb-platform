import { useEffect, useMemo, useState, type ReactNode } from "react";
import { Alert, ScrollView, StyleSheet, View } from "react-native";
import { useRouter } from "expo-router";
import { Button, HelperText, Modal, Portal, SegmentedButtons, Surface, Switch, Text, TextInput } from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import {
  clearSavedReceiptPrinter,
  clearSavedPrinter,
  connectSavedPrinter,
  disconnectCurrentPrinter,
  hydrateSavedReceiptPrinter,
  scanPrinterDevices,
  selectReceiptPrinter,
  selectPrinter,
  syncPrinterStatus,
  testReceiptPrinterConnection,
  testPrinterConnection,
} from "@/modules/printer/api";
import { usePrinterStore, useReceiptPrinterStore, type PrinterConnectionState } from "@/modules/printer/state";
import type { PrinterDevice } from "@/modules/printer/types";
import { i18n, setAppLanguage } from "@/shared/i18n/i18n";
import { resolveLocalizedErrorMessage } from "@/shared/i18n/error-message";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import type { AppLanguage } from "@/shared/i18n/types";
import { useAuthStore } from "@/store/auth-store";
import { useDeviceStore } from "@/store/device-store";
import { resolveSettingsAuthMode, shouldShowProfileAction } from "@/modules/device/settings-mode";
import { isIosReviewSessionActive } from "@/modules/ios-review/session";
import { isRequiredLocationError } from "@/modules/attendance/required-location";
import { buildAppUpdateInfoRows, formatAppPackageVersion } from "@/modules/updates/app-update-info";
import {
  checkAndDownloadAppUpdate,
  getCurrentAppUpdateInfo,
} from "@/modules/updates/app-update-runtime";
import {
  API_HOST_PRESETS,
  getCurrentApiHost,
  getStoredApiHost,
  normalizeApiHost,
  setStoredApiHost,
} from "@/shared/api/config";
import { DeviceActivationDialog } from "@/modules/device-activation/DeviceActivationDialog";
import type { MobileDeviceActivationMode } from "@/modules/device-activation/types";

function resolveDeviceStatusText(
  status: number | undefined,
  description: string | null | undefined,
  t: (key: string, options?: Record<string, unknown>) => string,
  language: string
) {
  if (description && language === "zh") {
    return description;
  }

  switch (status) {
    case -1:
      return t("deviceStatus.pending");
    case 0:
      return t("deviceStatus.disabled");
    case 1:
      return t("deviceStatus.enabled");
    case 2:
      return t("deviceStatus.locked");
    case 3:
      return t("deviceStatus.unregistered");
    default:
      return t("deviceStatus.unregistered");
  }
}

interface CompactSectionProps {
  title: string;
  description?: string;
  children: ReactNode;
}

function CompactSection({ title, description, children }: CompactSectionProps) {
  return (
    <Surface style={styles.card} elevation={1}>
      <View style={styles.sectionHeader}>
        <Text variant="titleMedium" style={styles.sectionTitle}>
          {title}
        </Text>
        {description ? (
          <Text variant="bodySmall" style={styles.meta}>
            {description}
          </Text>
        ) : null}
      </View>
      {children}
    </Surface>
  );
}

interface CompactRowProps {
  label: string;
  value?: string;
  meta?: string;
  action?: ReactNode;
}

function CompactRow({ label, value, meta, action }: CompactRowProps) {
  return (
    <View style={styles.compactRow}>
      <View style={styles.compactRowText}>
        <Text variant="bodyMedium" style={styles.compactRowLabel}>
          {label}
        </Text>
        {value ? (
          <Text variant="bodySmall" style={styles.compactRowValue} numberOfLines={1}>
            {value}
          </Text>
        ) : null}
        {meta ? (
          <Text variant="bodySmall" style={styles.meta} numberOfLines={2}>
            {meta}
          </Text>
        ) : null}
      </View>
      {action ? <View style={styles.compactRowAction}>{action}</View> : null}
    </View>
  );
}

interface PrinterDeviceListProps {
  devices: PrinterDevice[];
  selectedAddress?: string | null;
  bondedLabel: string;
  actionLabel: string;
  disabled: boolean;
  onSelect: (printer: PrinterDevice) => void;
}

function PrinterDeviceList({
  devices,
  selectedAddress,
  bondedLabel,
  actionLabel,
  disabled,
  onSelect,
}: PrinterDeviceListProps) {
  return (
    <View style={styles.printerList}>
      {devices.map((printer) => {
        const selected = selectedAddress === printer.address;
        return (
          <View key={printer.address} style={styles.printerRow}>
            <View style={styles.printerMeta}>
              <Text variant="bodyMedium" style={styles.printerName} numberOfLines={1}>
                {printer.name || printer.address}
              </Text>
              <Text variant="bodySmall" style={styles.meta} numberOfLines={1}>
                {printer.address}
              </Text>
              {printer.bonded ? (
                <Text variant="bodySmall" style={styles.meta}>
                  {bondedLabel}
                </Text>
              ) : null}
            </View>
            <Button
              compact
              mode={selected ? "contained-tonal" : "outlined"}
              onPress={() => onSelect(printer)}
              disabled={disabled}
            >
              {actionLabel}
            </Button>
          </View>
        );
      })}
    </View>
  );
}

export default function Settings() {
  const router = useRouter();
  const { t, language } = useAppTranslation(["settings", "common"]);
  const user = useAuthStore((state) => state.user);
  const logout = useAuthStore((state) => state.logout);
  const loginDeviceAccount = useAuthStore((state) => state.loginDeviceAccount);
  const sessionKind = useAuthStore((state) => state.sessionKind);
  const deviceSession = useDeviceStore((state) => state.session);
  const accountBinding = useDeviceStore((state) => state.accountBinding);
  const refreshAccountBinding = useDeviceStore((state) => state.refreshAccountBinding);
  const validateDevice = useDeviceStore((state) => state.validate);
  const unbindDevice = useDeviceStore((state) => state.unbind);
  const unbindAccountBinding = useDeviceStore((state) => state.unbindAccountBinding);
  const deviceLoading = useDeviceStore((state) => state.isLoading);
  const savedPrinter = usePrinterStore((state) => state.savedPrinter);
  const printerStatus = usePrinterStore((state) => state.status);
  const autoReconnectPaused = usePrinterStore((state) => state.autoReconnectPaused);
  const savedReceiptPrinter = useReceiptPrinterStore((state) => state.savedPrinter);
  const receiptPrinterStatus = useReceiptPrinterStore((state) => state.status);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [rawPrinters, setRawPrinters] = useState<PrinterDevice[]>([]);
  const [printerBusy, setPrinterBusy] = useState(false);
  const [printerScanCompleted, setPrinterScanCompleted] = useState(false);
  const [filterXPOnly, setFilterXPOnly] = useState(true);
  const [receiptRawPrinters, setReceiptRawPrinters] = useState<PrinterDevice[]>([]);
  const [receiptPrinterBusy, setReceiptPrinterBusy] = useState(false);
  const [receiptPrinterScanCompleted, setReceiptPrinterScanCompleted] = useState(false);
  const [updateBusy, setUpdateBusy] = useState(false);
  const [updateInfo, setUpdateInfo] = useState(() => getCurrentAppUpdateInfo());
  const [apiHost, setApiHost] = useState(getCurrentApiHost());
  const [apiHostDraft, setApiHostDraft] = useState(getCurrentApiHost());
  const [apiHostModalVisible, setApiHostModalVisible] = useState(false);
  const [activationVisible, setActivationVisible] = useState(false);
  const [activationMode, setActivationMode] = useState<MobileDeviceActivationMode>("redeem");

  const settingsAuthMode = resolveSettingsAuthMode({
    hasUser: Boolean(user),
    hasDeviceSession: Boolean(deviceSession),
  });
  const showProfileAction = shouldShowProfileAction(settingsAuthMode);
  const canViewDeviceCard = Boolean(user || deviceSession || accountBinding);

  const deviceStatusText = resolveDeviceStatusText(
    deviceSession?.status,
    deviceSession?.statusDescription,
    t,
    language
  );
  const deviceReady = Boolean(accountBinding) ||
    (deviceSession?.status === 1 && Boolean(deviceSession.storeCode));
  const deviceStoreDisplayName =
    accountBinding?.binding.storeName ||
    deviceSession?.storeName ||
    deviceSession?.storeCode ||
    t("common:na");
  const updateInfoRows = useMemo(() => buildAppUpdateInfoRows(updateInfo), [updateInfo]);
  const appPackageVersion = useMemo(
    () => formatAppPackageVersion(updateInfo, t("updates.unknown")),
    [t, updateInfo]
  );

  const visiblePrinters = useMemo(() => {
    if (!filterXPOnly) {
      return rawPrinters;
    }

    return rawPrinters.filter((printer) => {
      const name = printer.name?.trim();
      return typeof name === "string" && name.toUpperCase().startsWith("XP");
    });
  }, [filterXPOnly, rawPrinters]);

  const visibleReceiptPrinters = useMemo(() => receiptRawPrinters, [receiptRawPrinters]);

  useEffect(() => {
    let cancelled = false;

    void getStoredApiHost().then((host) => {
      if (cancelled) {
        return;
      }

      setApiHost(host);
      setApiHostDraft(host);
    });

    void syncPrinterStatus().catch((error) => {
      if (cancelled) {
        return;
      }

      const message = resolveLocalizedErrorMessage(error, {
        language,
        t,
        fallbackKey: "dialogs.refreshFailedMessage",
      });
      const store = usePrinterStore.getState();
      store.setLastError(message);
      store.setStatus("error");
    });

    void hydrateSavedReceiptPrinter().catch((error) => {
      if (cancelled) {
        return;
      }

      const message = resolveLocalizedErrorMessage(error, {
        language,
        t,
        fallbackKey: "dialogs.refreshFailedMessage",
      });
      const store = useReceiptPrinterStore.getState();
      store.setLastError(message);
      store.setStatus("error");
    });

    return () => {
      cancelled = true;
    };
  }, []);

  const isPrinterConnected = printerStatus === "connected";
  const isPrinterConnecting = printerStatus === "connecting";
  const isPrinterReconnecting = printerStatus === "reconnecting";
  const isReceiptPrinterTesting =
    receiptPrinterStatus === "connecting" || receiptPrinterStatus === "connected";
  const printerNativeBusy = printerBusy || receiptPrinterBusy;

  const getErrorMessage = (error: unknown, fallbackKey: string) =>
    resolveLocalizedErrorMessage(error, {
      language,
      t,
      fallbackKey,
    });

  function resolvePrinterStatusText(
    status: PrinterConnectionState,
    paused: boolean,
    namespace: "printer" | "receiptPrinter",
    tLabel: (key: string, options?: Record<string, unknown>) => string
  ) {
    if (paused || status === "paused") {
      return tLabel(`${namespace}.statusPaused`);
    }
    switch (status) {
      case "connected":
        return tLabel(`${namespace}.statusConnected`);
      case "connecting":
        return tLabel(`${namespace}.statusConnecting`);
      case "reconnecting":
        return tLabel(`${namespace}.statusReconnecting`);
      case "error":
        return tLabel(`${namespace}.statusDisconnected`);
      default:
        return tLabel(`${namespace}.statusDisconnected`);
    }
  }

  const handleLogout = () => {
    Alert.alert(t("dialogs.logoutTitle"), t("dialogs.logoutMessage"), [
      { text: t("common:actions.cancel"), style: "cancel" },
      {
        text: t("dialogs.logoutAction"),
        style: "destructive",
        onPress: async () => {
          setIsSubmitting(true);
          try {
            await logout();
            router.replace("/(auth)/login");
          } finally {
            setIsSubmitting(false);
          }
        },
      },
    ]);
  };

  const handleDeviceUnbind = () => {
    if (isIosReviewSessionActive()) {
      Alert.alert(
        "App Review Demo",
        "Device binding is simulated and does not change this device. / 设备绑定为本地演示，不会修改当前设备。",
      );
      return;
    }
    Alert.alert(
      t("dialogs.unbindDeviceTitle"),
      t("dialogs.unbindDeviceMessage"),
      [
        { text: t("common:actions.cancel"), style: "cancel" },
        {
          text: t("device.unbind"),
          style: "destructive",
          onPress: async () => {
            setIsSubmitting(true);
            try {
              if (accountBinding) {
                await unbindAccountBinding("user-requested");
                if (sessionKind === "deviceAccount") {
                  await logout();
                  router.replace("/(auth)/login");
                }
              } else {
                await unbindDevice();
                router.replace("/(auth)/login");
              }
            } catch (error) {
              Alert.alert(
                t("dialogs.unbindDeviceFailedTitle"),
                getErrorMessage(error, "dialogs.unbindDeviceFailedMessage")
              );
            } finally {
              setIsSubmitting(false);
            }
          },
        },
      ]
    );
  };

  const openDeviceActivation = () => {
    setActivationMode(accountBinding ? "rebind" : "redeem");
    setActivationVisible(true);
  };

  const handleActivationCompleted = async () => {
    try {
      await refreshAccountBinding();
      await loginDeviceAccount();
    } catch {
      Alert.alert(
        t("dialogs.bindingLoginFailedTitle"),
        t("dialogs.bindingLoginFailedMessage"),
      );
      router.replace("/(auth)/login");
    }
  };

  const handleLanguageChange = async (nextLanguage: AppLanguage) => {
    if (isIosReviewSessionActive()) {
      // 审核模式仅切换当前内存语言，不覆盖普通用户的持久化偏好。
      await i18n.changeLanguage(nextLanguage);
      return;
    }
    await setAppLanguage(nextLanguage);
  };

  const openApiHostSettings = () => {
    setApiHostDraft(apiHost);
    setApiHostModalVisible(true);
  };

  const handleSaveApiHost = async () => {
    const normalizedHost = normalizeApiHost(apiHostDraft);
    if (!normalizedHost) {
      Alert.alert(t("apiHost.emptyTitle"), t("apiHost.emptyMessage"));
      return;
    }

    try {
      if (isIosReviewSessionActive()) {
        // 审核模式不持久化 API Host，所有业务请求仍由本地 adapter 处理。
        setApiHost(normalizedHost);
        setApiHostDraft(normalizedHost);
        setApiHostModalVisible(false);
        Alert.alert(
          "App Review Demo",
          "Server settings are temporary in offline demo mode. / 离线演示中的服务器设置仅当前会话有效。",
        );
        return;
      }
      // 保存后无需手动刷新客户端，API 拦截器会在后续请求前同步新的 baseURL。
      const host = await setStoredApiHost(normalizedHost);
      setApiHost(host);
      setApiHostDraft(host);
      setApiHostModalVisible(false);
      Alert.alert(t("apiHost.savedTitle"), t("apiHost.savedMessage", { host }));
    } catch (error) {
      Alert.alert(
        t("apiHost.saveFailedTitle"),
        getErrorMessage(error, "apiHost.saveFailedMessage")
      );
    }
  };

  const handleCheckUpdates = async () => {
    if (isIosReviewSessionActive()) {
      Alert.alert(
        "App Review Demo",
        "Updates are disabled in offline demo mode. / 离线演示模式不检查更新。",
      );
      return;
    }
    setUpdateBusy(true);
    try {
      // 手动检查只负责下载更新，避免在扫码、保存等操作中主动重载 App。
      const result = await checkAndDownloadAppUpdate();
      if (result.status === "downloaded") {
        Alert.alert(t("dialogs.updateDownloadedTitle"), t("dialogs.updateDownloadedMessage"));
      } else if (result.status === "not-available") {
        Alert.alert(t("dialogs.updateNotAvailableTitle"), t("dialogs.updateNotAvailableMessage"));
      } else if (result.status === "configuration-disabled") {
        Alert.alert(t("dialogs.updateConfigurationDisabledTitle"), t("dialogs.updateConfigurationDisabledMessage"));
      } else {
        Alert.alert(t("dialogs.updateDisabledTitle"), t("dialogs.updateDisabledMessage"));
      }
    } catch (error) {
      Alert.alert(
        t("dialogs.updateCheckFailedTitle"),
        getErrorMessage(error, "dialogs.updateCheckFailedMessage")
      );
    } finally {
      setUpdateInfo(getCurrentAppUpdateInfo());
      setUpdateBusy(false);
    }
  };

  const handleRefreshDevice = async () => {
    setIsSubmitting(true);
    try {
      const isReady = await validateDevice();
      Alert.alert(
        isReady ? t("dialogs.refreshReadyTitle") : t("dialogs.refreshPendingTitle"),
        isReady ? t("dialogs.refreshReadyMessage") : t("dialogs.refreshPendingMessage")
      );
    } catch (error) {
      Alert.alert(
        t("dialogs.refreshFailedTitle"),
        isRequiredLocationError(error)
          ? t("dialogs.locationRequiredMessage")
          : getErrorMessage(error, "dialogs.refreshFailedMessage")
      );
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleScanPrinters = async () => {
    setPrinterBusy(true);
    try {
      const nextPrinters = await scanPrinterDevices();
      setRawPrinters(nextPrinters);
      setPrinterScanCompleted(true);
    } catch (error) {
      Alert.alert(
        t("dialogs.printerScanFailedTitle"),
        getErrorMessage(error, "dialogs.refreshFailedMessage")
      );
    } finally {
      setPrinterBusy(false);
    }
  };

  const handleScanReceiptPrinters = async () => {
    setReceiptPrinterBusy(true);
    try {
      const nextPrinters = await scanPrinterDevices();
      setReceiptRawPrinters(nextPrinters);
      setReceiptPrinterScanCompleted(true);
    } catch (error) {
      Alert.alert(
        t("dialogs.receiptPrinterScanFailedTitle"),
        getErrorMessage(error, "dialogs.refreshFailedMessage")
      );
    } finally {
      setReceiptPrinterBusy(false);
    }
  };

  const handleConnectPrinter = async (device: PrinterDevice) => {
    setPrinterBusy(true);
    try {
      await selectPrinter(device);
      Alert.alert(
        t("dialogs.printerSavedTitle"),
        t("dialogs.printerSavedMessage", { printer: device.name || device.address })
      );
    } catch (error) {
      Alert.alert(
        t("dialogs.printerConnectFailedTitle"),
        getErrorMessage(error, "dialogs.refreshFailedMessage")
      );
    } finally {
      setPrinterBusy(false);
    }
  };

  const handleTestPrinter = async () => {
    setPrinterBusy(true);
    try {
      await testPrinterConnection();
      Alert.alert(t("dialogs.printerTestSuccessTitle"), t("dialogs.printerTestSuccessMessage"));
    } catch (error) {
      Alert.alert(
        t("dialogs.printerTestFailedTitle"),
        getErrorMessage(error, "dialogs.refreshFailedMessage")
      );
    } finally {
      setPrinterBusy(false);
    }
  };

  const handleClearPrinter = async () => {
    setPrinterBusy(true);
    try {
      await clearSavedPrinter();
      Alert.alert(t("dialogs.printerClearedTitle"), t("dialogs.printerClearedMessage"));
    } catch (error) {
      Alert.alert(
        t("dialogs.printerDisconnectFailedTitle"),
        getErrorMessage(error, "dialogs.refreshFailedMessage")
      );
    } finally {
      setPrinterBusy(false);
    }
  };

  const handleConnectSavedPrinter = async () => {
    setPrinterBusy(true);
    try {
      await connectSavedPrinter();
    } catch (error) {
      Alert.alert(
        t("dialogs.printerConnectFailedTitle"),
        getErrorMessage(error, "dialogs.refreshFailedMessage")
      );
    } finally {
      setPrinterBusy(false);
    }
  };

  const handleDisconnectPrinter = async () => {
    setPrinterBusy(true);
    try {
      await disconnectCurrentPrinter({ pauseAutoReconnect: true });
    } catch (error) {
      Alert.alert(
        t("dialogs.printerDisconnectFailedTitle"),
        getErrorMessage(error, "dialogs.refreshFailedMessage")
      );
    } finally {
      setPrinterBusy(false);
    }
  };

  const handleSaveReceiptPrinter = async (device: PrinterDevice) => {
    setReceiptPrinterBusy(true);
    try {
      await selectReceiptPrinter(device);
      Alert.alert(
        t("dialogs.receiptPrinterSavedTitle"),
        t("dialogs.receiptPrinterSavedMessage", { printer: device.name || device.address })
      );
    } catch (error) {
      Alert.alert(
        t("dialogs.receiptPrinterConnectFailedTitle"),
        getErrorMessage(error, "dialogs.refreshFailedMessage")
      );
    } finally {
      setReceiptPrinterBusy(false);
    }
  };

  const handleTestReceiptPrinter = async () => {
    setReceiptPrinterBusy(true);
    try {
      await testReceiptPrinterConnection();
      Alert.alert(t("dialogs.receiptPrinterTestSuccessTitle"), t("dialogs.receiptPrinterTestSuccessMessage"));
    } catch (error) {
      Alert.alert(
        t("dialogs.receiptPrinterTestFailedTitle"),
        getErrorMessage(error, "dialogs.refreshFailedMessage")
      );
    } finally {
      setReceiptPrinterBusy(false);
    }
  };

  const handleClearReceiptPrinter = async () => {
    setReceiptPrinterBusy(true);
    try {
      await clearSavedReceiptPrinter();
      Alert.alert(t("dialogs.receiptPrinterClearedTitle"), t("dialogs.receiptPrinterClearedMessage"));
    } catch (error) {
      Alert.alert(
        t("dialogs.receiptPrinterDisconnectFailedTitle"),
        getErrorMessage(error, "dialogs.refreshFailedMessage")
      );
    } finally {
      setReceiptPrinterBusy(false);
    }
  };

  return (
    <SafeAreaView edges={["top", "left", "right"]} style={styles.container}>
      <ScrollView contentContainerStyle={styles.content}>
        <Text variant="headlineSmall" style={styles.title}>
          {t("title")}
        </Text>

        <CompactSection title={t("account.title")}>
          <CompactRow
            label={user?.fullName || user?.username || t("common:notLoggedIn")}
            value={user?.email || t("account.guestEmail")}
            meta={
              user?.roleNames?.length
                ? t("account.roles", { roles: user.roleNames.join(" / ") })
                : t("account.deviceMode")
            }
            action={
              showProfileAction ? (
                <Button
                  compact
                  mode="outlined"
                  icon="account-circle-outline"
                  onPress={() => {
                    router.push("/(shell)/employee-profile" as unknown as Parameters<typeof router.push>[0]);
                  }}
                >
                  {t("account.profileButton")}
                </Button>
              ) : null
            }
          />
          {!showProfileAction ? (
            <Text variant="bodySmall" style={styles.meta}>
              {t("account.deviceModeHelper")}
            </Text>
          ) : null}
        </CompactSection>

        <CompactSection
          title={t("privacy.title")}
          description={t(
            showProfileAction
              ? "privacy.employeeDescription"
              : "privacy.deviceDescription"
          )}
        >
          <Button
            compact
            mode="outlined"
            icon="shield-check-outline"
            onPress={() => router.push("/privacy")}
          >
            {t("privacy.openPolicy")}
          </Button>
        </CompactSection>

        <CompactSection title={t("groups.app")}>
          <CompactRow
            label={t("common:language.title")}
            action={
              <SegmentedButtons
                value={language}
                onValueChange={(value) => void handleLanguageChange(value as AppLanguage)}
                buttons={[
                  {
                    value: "zh",
                    label: t("common:language.zh"),
                    accessibilityLabel: t("common:language.zh"),
                  },
                  {
                    value: "en",
                    label: t("common:language.en"),
                    accessibilityLabel: t("common:language.en"),
                  },
                ]}
                style={styles.languageSelector}
              />
            }
          />
          <View style={styles.sectionDivider} />
          <CompactRow
            label={t("apiHost.title")}
            value={apiHost}
            action={
              <Button compact mode="outlined" icon="server-network" onPress={openApiHostSettings}>
                {t("apiHost.change")}
              </Button>
            }
          />
          <View style={styles.sectionDivider} />
          <CompactRow
            label={t("updates.title")}
            value={appPackageVersion}
            meta={`${t("updates.channel")}: ${updateInfo.channel ?? t("updates.noChannel")}`}
            action={
              <Button
                compact
                mode="outlined"
                icon="cloud-download-outline"
                onPress={handleCheckUpdates}
                loading={updateBusy}
                disabled={updateBusy}
              >
                {t("updates.check")}
              </Button>
            }
          />
          <View style={styles.updateInfoCompactList}>
            {updateInfoRows.map((row) => (
              <View key={row.key} style={styles.updateInfoRow}>
                <Text variant="bodySmall" style={styles.updateInfoLabel}>
                  {t(row.labelKey)}
                </Text>
                <Text variant="bodySmall" style={styles.updateInfoValue} numberOfLines={1}>
                  {row.value ?? t(row.valueKey ?? "updates.unknown")}
                </Text>
              </View>
            ))}
          </View>
        </CompactSection>

        {canViewDeviceCard ? (
          <CompactSection title={t("device.title")} description={t("device.description")}>
            <View style={styles.statusBlock}>
              <CompactRow
                label={t("device.statusLabel")}
                value={accountBinding ? t("deviceStatus.bound") : deviceStatusText}
              />
              <View style={styles.sectionDivider} />
              <CompactRow label={t("device.storeLabelCompact")} value={deviceStoreDisplayName} />
              <View style={styles.sectionDivider} />
              <CompactRow
                label={t("device.deviceNumberLabel")}
                value={accountBinding?.binding.deviceCode || deviceSession?.systemDeviceNumber || t("common:na")}
              />
              {accountBinding ? (
                <>
                  <View style={styles.sectionDivider} />
                  <CompactRow
                    label={t("device.accountLabel")}
                    value={accountBinding.binding.targetFullName || accountBinding.binding.targetUsername}
                  />
                  <View style={styles.sectionDivider} />
                  <CompactRow
                    label={t("device.systemLabel")}
                    value={accountBinding.binding.deviceSystem}
                  />
                </>
              ) : null}
            </View>

            <Button
              mode="contained"
              icon="qrcode-scan"
              onPress={openDeviceActivation}
              loading={isSubmitting || deviceLoading}
              disabled={isSubmitting || deviceLoading}
              style={styles.primaryButton}
            >
              {accountBinding
                ? t("device.rebindByScan")
                : deviceSession
                  ? t("device.upgradeByScan")
                  : t("device.bindByScan")}
            </Button>

            {deviceSession && !accountBinding ? (
              <Button
                mode="outlined"
                onPress={handleRefreshDevice}
                loading={isSubmitting || deviceLoading}
                disabled={isSubmitting || deviceLoading}
                style={styles.secondaryButton}
              >
                {t("device.refreshStatus")}
              </Button>
            ) : null}

            {deviceSession || accountBinding ? (
              <View style={styles.deviceDangerActions}>
                <Button
                  mode="outlined"
                  icon="link-off"
                  textColor="#A8071A"
                  onPress={handleDeviceUnbind}
                  loading={isSubmitting || deviceLoading}
                  disabled={isSubmitting || deviceLoading}
                  style={styles.deviceDangerButton}
                >
                  {t("device.unbind")}
                </Button>
              </View>
            ) : null}

            {deviceReady ? (
              <Text variant="bodySmall" style={styles.successText}>
                {t("device.ready")}
              </Text>
            ) : null}
          </CompactSection>
        ) : null}

        <CompactSection title={t("groups.printers")}>
          <View style={styles.printerSection}>
            <View style={styles.printerSectionHeader}>
              <View style={styles.compactRowText}>
                <Text variant="titleSmall" style={styles.sectionTitle}>
                  {t("printer.title")}
                </Text>
                <Text variant="bodySmall" style={styles.compactRowValue} numberOfLines={1}>
                  {savedPrinter
                    ? t("printer.selected", { printer: savedPrinter.name || savedPrinter.address })
                    : t("printer.notSelected")}
                </Text>
                <Text variant="bodySmall" style={styles.meta}>
                  {resolvePrinterStatusText(printerStatus, autoReconnectPaused, "printer", t)}
                </Text>
              </View>
            </View>

            <View style={styles.primaryPrinterActions}>
              <Button
                compact
                mode="contained"
                icon="magnify"
                onPress={handleScanPrinters}
                loading={printerBusy && !isPrinterConnecting}
                disabled={printerNativeBusy}
                style={styles.primaryActionButton}
              >
                {printerBusy && !isPrinterConnecting ? t("printer.scanning") : t("printer.scan")}
              </Button>
              {savedPrinter ? (
                isPrinterConnected ? (
                  <Button
                    compact
                    mode="outlined"
                    icon="link-off"
                    onPress={handleDisconnectPrinter}
                    disabled={printerNativeBusy}
                    style={styles.primaryActionButton}
                  >
                    {t("printer.disconnect")}
                  </Button>
                ) : (
                  <Button
                    compact
                    mode="outlined"
                    icon="bluetooth-connect"
                    onPress={handleConnectSavedPrinter}
                    loading={printerBusy && (isPrinterConnecting || isPrinterReconnecting)}
                    disabled={printerNativeBusy}
                    style={styles.primaryActionButton}
                  >
                    {printerBusy && (isPrinterConnecting || isPrinterReconnecting)
                      ? t("printer.connecting")
                      : t("printer.connect")}
                  </Button>
                )
              ) : null}
            </View>

            <View style={styles.filterRow}>
              <Text variant="bodySmall">{t("printer.filterXPOnly")}</Text>
              <Switch value={filterXPOnly} onValueChange={setFilterXPOnly} disabled={printerNativeBusy} />
            </View>

            {printerScanCompleted ? (
              visiblePrinters.length ? (
                <>
                  <Text variant="labelMedium" style={styles.listLabel}>
                    {t("printer.available")}
                  </Text>
                  <PrinterDeviceList
                    devices={visiblePrinters}
                    selectedAddress={savedPrinter?.address}
                    bondedLabel={t("printer.bonded")}
                    actionLabel={t("printer.connect")}
                    disabled={printerNativeBusy}
                    onSelect={(printer) => void handleConnectPrinter(printer)}
                  />
                </>
              ) : (
                <HelperText type="info" visible>
                  {rawPrinters.length && filterXPOnly ? t("printer.emptyFiltered") : t("printer.empty")}
                </HelperText>
              )
            ) : null}

            <View style={styles.printerActions}>
              <Button
                compact
                mode="outlined"
                icon="printer-check"
                onPress={handleTestPrinter}
                disabled={printerNativeBusy || !savedPrinter || !isPrinterConnected}
              >
                {t("printer.test")}
              </Button>
              <Button
                compact
                mode="text"
                icon="delete-outline"
                onPress={handleClearPrinter}
                disabled={printerNativeBusy || !savedPrinter}
              >
                {t("printer.clear")}
              </Button>
            </View>
          </View>

          <View style={styles.sectionDivider} />

          <View style={styles.printerSection}>
            <View style={styles.printerSectionHeader}>
              <View style={styles.compactRowText}>
                <Text variant="titleSmall" style={styles.sectionTitle}>
                  {t("receiptPrinter.title")}
                </Text>
                <Text variant="bodySmall" style={styles.compactRowValue} numberOfLines={1}>
                  {savedReceiptPrinter
                    ? t("receiptPrinter.selected", {
                        printer: savedReceiptPrinter.name || savedReceiptPrinter.address,
                      })
                    : t("receiptPrinter.notSelected")}
                </Text>
                <Text variant="bodySmall" style={styles.meta}>
                  {resolvePrinterStatusText(receiptPrinterStatus, false, "receiptPrinter", t)}
                </Text>
              </View>
            </View>

            <View style={styles.primaryPrinterActions}>
              <Button
                compact
                mode="contained"
                icon="magnify"
                onPress={handleScanReceiptPrinters}
                loading={receiptPrinterBusy && !isReceiptPrinterTesting}
                disabled={printerNativeBusy}
                style={styles.primaryActionButton}
              >
                {receiptPrinterBusy && !isReceiptPrinterTesting
                  ? t("receiptPrinter.scanning")
                  : t("receiptPrinter.scan")}
              </Button>
              <Button
                compact
                mode="outlined"
                icon="receipt-text-outline"
                onPress={handleTestReceiptPrinter}
                loading={receiptPrinterBusy && isReceiptPrinterTesting}
                disabled={printerNativeBusy || !savedReceiptPrinter}
                style={styles.primaryActionButton}
              >
                {receiptPrinterBusy && isReceiptPrinterTesting
                  ? t("receiptPrinter.testing")
                  : t("receiptPrinter.test")}
              </Button>
            </View>

            {receiptPrinterScanCompleted ? (
              visibleReceiptPrinters.length ? (
                <>
                  <Text variant="labelMedium" style={styles.listLabel}>
                    {t("receiptPrinter.available")}
                  </Text>
                  <PrinterDeviceList
                    devices={visibleReceiptPrinters}
                    selectedAddress={savedReceiptPrinter?.address}
                    bondedLabel={t("printer.bonded")}
                    actionLabel={t("receiptPrinter.save")}
                    disabled={printerNativeBusy}
                    onSelect={(printer) => void handleSaveReceiptPrinter(printer)}
                  />
                </>
              ) : (
                <HelperText type="info" visible>
                  {t("receiptPrinter.empty")}
                </HelperText>
              )
            ) : null}

            <View style={styles.printerActions}>
              <Button
                compact
                mode="text"
                icon="delete-outline"
                onPress={handleClearReceiptPrinter}
                disabled={printerNativeBusy || !savedReceiptPrinter}
              >
                {t("receiptPrinter.clear")}
              </Button>
            </View>
          </View>
        </CompactSection>

        {user ? (
          <Button
            mode="contained"
            buttonColor="#FF4D4F"
            onPress={handleLogout}
            loading={isSubmitting}
            disabled={isSubmitting}
            style={styles.logoutButton}
          >
            {t("account.logoutToLogin")}
          </Button>
        ) : null}
      </ScrollView>
      <DeviceActivationDialog
        visible={activationVisible}
        mode={activationMode}
        onDismiss={() => setActivationVisible(false)}
        onCompleted={handleActivationCompleted}
      />
      <Portal>
        <Modal
          visible={apiHostModalVisible}
          onDismiss={() => setApiHostModalVisible(false)}
          contentContainerStyle={styles.modal}
        >
          <Text variant="titleMedium">{t("apiHost.modalTitle")}</Text>
          <Text variant="bodyMedium" style={styles.meta}>
            {t("apiHost.modalDescription")}
          </Text>
          <View style={styles.apiHostCurrentBox}>
            <Text variant="labelMedium" style={styles.meta}>
              {t("apiHost.current")}
            </Text>
            <Text variant="bodyLarge" style={styles.value} numberOfLines={1}>
              {apiHost}
            </Text>
          </View>
          <Text variant="labelLarge">{t("apiHost.presetsLabel")}</Text>
          <View style={styles.apiHostPresetList}>
            {API_HOST_PRESETS.map((preset) => {
              const selected = normalizeApiHost(apiHostDraft) === preset.host;
              return (
                <Button
                  key={preset.key}
                  compact
                  mode={selected ? "contained" : "outlined"}
                  style={styles.apiHostPresetButton}
                  onPress={() => setApiHostDraft(preset.host)}
                >
                  {t(`apiHost.presets.${preset.key}`)}
                </Button>
              );
            })}
          </View>
          <TextInput
            label={t("apiHost.inputLabel")}
            value={apiHostDraft}
            onChangeText={setApiHostDraft}
            mode="outlined"
            autoCapitalize="none"
            autoCorrect={false}
          />
          <View style={styles.modalActions}>
            <Button mode="text" onPress={() => setApiHostModalVisible(false)}>
              {t("common:actions.cancel")}
            </Button>
            <Button mode="contained" onPress={handleSaveApiHost}>
              {t("common:actions.save")}
            </Button>
          </View>
        </Modal>
      </Portal>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: "#F5F7FA" },
  content: { paddingHorizontal: 10, paddingTop: 4, paddingBottom: 10, gap: 8 },
  title: { textAlign: "center", marginBottom: 0 },
  card: { padding: 12, borderRadius: 8, gap: 8 },
  sectionHeader: { gap: 2 },
  sectionTitle: { fontWeight: "700" },
  compactRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 10,
  },
  compactRowText: {
    flex: 1,
    gap: 2,
    minWidth: 0,
  },
  compactRowLabel: {
    fontWeight: "600",
  },
  compactRowValue: {
    color: "#394150",
  },
  compactRowAction: {
    flexShrink: 0,
  },
  languageSelector: {
    width: 190,
  },
  sectionDivider: {
    height: StyleSheet.hairlineWidth,
    backgroundColor: "#E5E7EB",
  },
  value: { marginTop: 8, fontWeight: "600" },
  meta: { color: "#666" },
  updateInfoCompactList: { gap: 4, marginTop: 2 },
  updateInfoRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    gap: 12,
  },
  updateInfoLabel: { color: "#666", flexShrink: 0 },
  updateInfoValue: { flex: 1, textAlign: "right", fontWeight: "600" },
  apiHostCurrentBox: {
    gap: 4,
    borderRadius: 12,
    backgroundColor: "#F7F8FA",
    paddingHorizontal: 12,
    paddingVertical: 10,
  },
  apiHostPresetList: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
  },
  apiHostPresetButton: {
    borderRadius: 999,
  },
  statusBlock: { gap: 6, marginTop: 4 },
  primaryButton: { marginTop: 4 },
  filterRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    marginTop: 2,
  },
  primaryPrinterActions: {
    flexDirection: "row",
    gap: 8,
    marginTop: 2,
  },
  primaryActionButton: {
    flex: 1,
  },
  printerSection: {
    gap: 8,
  },
  printerSectionHeader: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 8,
  },
  listLabel: {
    color: "#4B5563",
  },
  printerList: {
    gap: 6,
  },
  printerRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 10,
    borderRadius: 8,
    backgroundColor: "#F7F8FA",
    paddingHorizontal: 10,
    paddingVertical: 8,
  },
  printerMeta: {
    flex: 1,
    gap: 2,
    minWidth: 0,
  },
  printerName: {
    fontWeight: "600",
  },
  printerActions: {
    flexDirection: "row",
    gap: 8,
    marginTop: 2,
  },
  deviceDangerActions: {
    flexDirection: "row",
    gap: 8,
    marginTop: 2,
  },
  deviceDangerButton: {
    flex: 1,
  },
  modal: {
    backgroundColor: "#FFFFFF",
    margin: 18,
    borderRadius: 8,
    padding: 18,
    gap: 12,
  },
  modalActions: {
    flexDirection: "row",
    justifyContent: "flex-end",
    gap: 8,
  },
  secondaryButton: { marginTop: 8 },
  logoutButton: { marginTop: 2, marginBottom: 6 },
  successText: { color: "#1677FF", marginTop: 8 },
});
