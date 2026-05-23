import { useEffect, useMemo, useState } from "react";
import { Alert, ScrollView, StyleSheet, View } from "react-native";
import { useRouter } from "expo-router";
import { Button, HelperText, Menu, Surface, Switch, Text } from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import {
  clearSavedPrinter,
  connectSavedPrinter,
  disconnectCurrentPrinter,
  scanPrinterDevices,
  selectPrinter,
  syncPrinterStatus,
  testPrinterConnection,
} from "@/modules/printer/api";
import { usePrinterStore, type PrinterConnectionState } from "@/modules/printer/state";
import type { PrinterDevice } from "@/modules/printer/types";
import { setAppLanguage } from "@/shared/i18n/i18n";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import type { AppLanguage } from "@/shared/i18n/types";
import { useAuthStore } from "@/store/auth-store";
import { useDeviceStore } from "@/store/device-store";
import { useStores } from "@/modules/shop/use-stores";

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

export default function Settings() {
  const router = useRouter();
  const { t, language } = useAppTranslation(["settings", "common"]);
  const user = useAuthStore((state) => state.user);
  const access = useAuthStore((state) => state.access);
  const logout = useAuthStore((state) => state.logout);
  const deviceSession = useDeviceStore((state) => state.session);
  const registerDevice = useDeviceStore((state) => state.register);
  const validateDevice = useDeviceStore((state) => state.validate);
  const deviceLoading = useDeviceStore((state) => state.isLoading);
  const { stores, selectedStore, selectStore } = useStores();
  const savedPrinter = usePrinterStore((state) => state.savedPrinter);
  const printerStatus = usePrinterStore((state) => state.status);
  const autoReconnectPaused = usePrinterStore((state) => state.autoReconnectPaused);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [storeMenuVisible, setStoreMenuVisible] = useState(false);
  const [languageMenuVisible, setLanguageMenuVisible] = useState(false);
  const [rawPrinters, setRawPrinters] = useState<PrinterDevice[]>([]);
  const [printerBusy, setPrinterBusy] = useState(false);
  const [filterXPOnly, setFilterXPOnly] = useState(true);

  const canRegisterDevice = access.hasRole("Order") || access.hasRole("订货员");
  const canViewDeviceCard = canRegisterDevice || Boolean(deviceSession);

  const effectiveStore = selectedStore
    ? selectedStore
    : deviceSession?.storeCode
      ? {
          storeCode: deviceSession.storeCode,
          storeName: deviceSession.storeName || deviceSession.storeCode,
        }
      : null;

  const deviceStatusText = resolveDeviceStatusText(
    deviceSession?.status,
    deviceSession?.statusDescription,
    t,
    language
  );
  const deviceReady = deviceSession?.status === 1 && Boolean(deviceSession.storeCode);

  const sortedStores = useMemo(
    () =>
      stores.slice().sort((left, right) =>
        (left.storeName || left.storeCode).localeCompare(
          right.storeName || right.storeCode,
          undefined,
          { sensitivity: "base" }
        )
      ),
    [stores]
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

  useEffect(() => {
    let cancelled = false;

    void syncPrinterStatus().catch((error) => {
      if (cancelled) {
        return;
      }

      const message = error instanceof Error ? error.message : "Printer status check failed.";
      const store = usePrinterStore.getState();
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

  function resolvePrinterStatusText(
    status: PrinterConnectionState,
    paused: boolean,
    tLabel: (key: string, options?: Record<string, unknown>) => string
  ) {
    if (paused || status === "paused") {
      return tLabel("printer.statusPaused");
    }
    switch (status) {
      case "connected":
        return tLabel("printer.statusConnected");
      case "connecting":
        return tLabel("printer.statusConnecting");
      case "reconnecting":
        return tLabel("printer.statusReconnecting");
      case "error":
        return tLabel("printer.statusDisconnected");
      default:
        return tLabel("printer.statusDisconnected");
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

  const handleLanguageChange = async (nextLanguage: AppLanguage) => {
    setLanguageMenuVisible(false);
    await setAppLanguage(nextLanguage);
  };

  const handleRegisterDevice = async () => {
    if (!effectiveStore?.storeCode) {
      Alert.alert(t("dialogs.selectStoreTitle"), t("dialogs.selectStoreMessage"));
      return;
    }

    setIsSubmitting(true);
    try {
      const session = await registerDevice({
        storeCode: effectiveStore.storeCode,
        storeName: effectiveStore.storeName,
      });

      Alert.alert(
        session.resolvedFromExisting
          ? t("dialogs.registerExistingTitle")
          : t("dialogs.registerSuccessTitle"),
        t(session.resolvedFromExisting ? "dialogs.registerExistingMessage" : "dialogs.registerSuccessMessage", {
          store: session.storeName || session.storeCode,
          status: resolveDeviceStatusText(session.status, session.statusDescription, t, language),
        })
      );
    } catch (error) {
      Alert.alert(
        t("dialogs.registerFailedTitle"),
        error instanceof Error ? error.message : t("dialogs.registerFailedMessage")
      );
    } finally {
      setIsSubmitting(false);
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
        error instanceof Error ? error.message : t("dialogs.refreshFailedMessage")
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
    } catch (error) {
      Alert.alert(
        t("dialogs.printerScanFailedTitle"),
        error instanceof Error ? error.message : t("dialogs.refreshFailedMessage")
      );
    } finally {
      setPrinterBusy(false);
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
        error instanceof Error ? error.message : t("dialogs.refreshFailedMessage")
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
        error instanceof Error ? error.message : t("dialogs.refreshFailedMessage")
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
        error instanceof Error ? error.message : t("dialogs.refreshFailedMessage")
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
        error instanceof Error ? error.message : t("dialogs.refreshFailedMessage")
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
        error instanceof Error ? error.message : t("dialogs.refreshFailedMessage")
      );
    } finally {
      setPrinterBusy(false);
    }
  };

  return (
    <SafeAreaView edges={["top", "left", "right"]} style={styles.container}>
      <ScrollView contentContainerStyle={styles.content}>
        <Text variant="headlineSmall" style={styles.title}>
          {t("title")}
        </Text>

        <Surface style={styles.card} elevation={1}>
          <Text variant="titleMedium">{t("account.title")}</Text>
          <Text variant="bodyLarge" style={styles.value}>
            {user?.fullName || user?.username || t("common:notLoggedIn")}
          </Text>
          <Text variant="bodyMedium" style={styles.meta}>
            {user?.email || t("account.guestEmail")}
          </Text>
          <Text variant="bodySmall" style={styles.meta}>
            {user?.roleNames?.length
              ? t("account.roles", { roles: user.roleNames.join(" / ") })
              : t("account.deviceMode")}
          </Text>
          <Text variant="bodyMedium" style={styles.meta}>
            {t("account.profileHelper")}
          </Text>
          <Button
            mode="outlined"
            icon="account-circle-outline"
            onPress={() => {
              if (!user) {
                Alert.alert(t("title"), t("messages.profileLoginRequired"));
                return;
              }

              router.push("/(tabs)/employee-profile");
            }}
            style={styles.secondaryButton}
          >
            {t("account.profileButton")}
          </Button>
        </Surface>

        <Surface style={styles.card} elevation={1}>
          <Text variant="titleMedium">{t("common:language.title")}</Text>
          <Text variant="bodyMedium" style={styles.meta}>
            {t("language.description")}
          </Text>
          <Menu
            visible={languageMenuVisible}
            onDismiss={() => setLanguageMenuVisible(false)}
            anchor={
              <Button
                mode="outlined"
                icon="chevron-down"
                contentStyle={styles.dropdownButtonContent}
                style={styles.dropdownButton}
                onPress={() => setLanguageMenuVisible(true)}
              >
                {language === "en" ? t("common:language.en") : t("common:language.zh")}
              </Button>
            }
          >
            <Menu.Item title={t("common:language.zh")} onPress={() => void handleLanguageChange("zh")} />
            <Menu.Item title={t("common:language.en")} onPress={() => void handleLanguageChange("en")} />
          </Menu>
        </Surface>

        {canViewDeviceCard ? (
          <Surface style={styles.card} elevation={1}>
            <Text variant="titleMedium">{t("device.title")}</Text>
            <Text variant="bodyMedium" style={styles.meta}>
              {t("device.description")}
            </Text>

            <View style={styles.statusBlock}>
              <Text variant="bodyMedium">{t("device.status", { status: deviceStatusText })}</Text>
              <Text variant="bodyMedium">
                {t("device.store", {
                  store:
                    deviceSession?.storeName ||
                    deviceSession?.storeCode ||
                    effectiveStore?.storeName ||
                    effectiveStore?.storeCode ||
                    t("device.selectStore"),
                })}
              </Text>
              <Text variant="bodySmall" style={styles.meta}>
                {t("device.deviceNumber", {
                  value: deviceSession?.systemDeviceNumber || t("common:na"),
                })}
              </Text>
            </View>

            {canRegisterDevice ? (
              <View style={styles.storePickerWrap}>
                <Text variant="labelLarge" style={styles.storePickerLabel}>
                  {t("device.storeLabel")}
                </Text>
                <Menu
                  visible={storeMenuVisible}
                  onDismiss={() => setStoreMenuVisible(false)}
                  anchor={
                    <Button
                      mode="outlined"
                      icon="chevron-down"
                      contentStyle={styles.dropdownButtonContent}
                      style={styles.dropdownButton}
                      onPress={() => setStoreMenuVisible(true)}
                    >
                      {effectiveStore?.storeName || t("device.selectStore")}
                    </Button>
                  }
                >
                  {sortedStores.map((store) => (
                    <Menu.Item
                      key={store.storeCode}
                      title={store.storeName || store.storeCode}
                      onPress={() => {
                        void selectStore(store);
                        setStoreMenuVisible(false);
                      }}
                    />
                  ))}
                </Menu>
              </View>
            ) : null}

            {canRegisterDevice ? (
              <>
                <HelperText type="info" visible={!effectiveStore}>
                  {t("device.helper")}
                </HelperText>
                <Button
                  mode="contained"
                  onPress={handleRegisterDevice}
                  loading={isSubmitting || deviceLoading}
                  disabled={isSubmitting || deviceLoading || !effectiveStore}
                  style={styles.primaryButton}
                >
                  {deviceSession ? t("device.registerAgain") : t("device.register")}
                </Button>
              </>
            ) : null}

            {deviceSession ? (
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

            {deviceReady ? (
              <Text variant="bodySmall" style={styles.successText}>
                {t("device.ready")}
              </Text>
            ) : null}
          </Surface>
        ) : null}

        <Surface style={styles.card} elevation={1}>
          <Text variant="titleMedium">{t("printer.title")}</Text>
          <Text variant="bodyMedium" style={styles.meta}>
            {t("printer.description")}
          </Text>
          <Text variant="bodyLarge" style={styles.value}>
            {savedPrinter
              ? t("printer.selected", { printer: savedPrinter.name || savedPrinter.address })
              : t("printer.notSelected")}
          </Text>
          <Text variant="bodySmall" style={styles.meta}>
            {resolvePrinterStatusText(printerStatus, autoReconnectPaused, t)}
          </Text>

          <View style={styles.primaryPrinterActions}>
            <Button
              mode="contained"
              onPress={handleScanPrinters}
              loading={printerBusy && !isPrinterConnecting}
              disabled={printerBusy}
              style={styles.primaryActionButton}
            >
              {printerBusy && !isPrinterConnecting ? t("printer.scanning") : t("printer.scan")}
            </Button>
            {savedPrinter ? (
              isPrinterConnected ? (
                <Button
                  mode="outlined"
                  onPress={handleDisconnectPrinter}
                  disabled={printerBusy}
                  style={styles.primaryActionButton}
                >
                  {t("printer.disconnect")}
                </Button>
              ) : (
                <Button
                  mode="outlined"
                  onPress={handleConnectSavedPrinter}
                  loading={printerBusy && (isPrinterConnecting || isPrinterReconnecting)}
                  disabled={printerBusy}
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
            <Text variant="bodyMedium">{t("printer.filterXPOnly")}</Text>
            <Switch value={filterXPOnly} onValueChange={setFilterXPOnly} disabled={printerBusy} />
          </View>

          {visiblePrinters.length ? (
            <View style={styles.printerList}>
              <Text variant="labelLarge">{t("printer.available")}</Text>
              {visiblePrinters.map((printer) => {
                const selected = savedPrinter?.address === printer.address;
                return (
                  <Surface key={printer.address} style={styles.printerRow} elevation={0}>
                    <View style={styles.printerMeta}>
                      <Text variant="bodyMedium" style={styles.printerName}>
                        {printer.name || printer.address}
                      </Text>
                      <Text variant="bodySmall" style={styles.meta}>
                        {printer.address}
                      </Text>
                      {printer.bonded ? (
                        <Text variant="bodySmall" style={styles.meta}>
                          {t("printer.bonded")}
                        </Text>
                      ) : null}
                    </View>
                    <Button
                      mode={selected ? "contained-tonal" : "outlined"}
                      onPress={() => void handleConnectPrinter(printer)}
                      disabled={printerBusy}
                    >
                      {t("printer.connect")}
                    </Button>
                  </Surface>
                );
              })}
            </View>
          ) : (
            <HelperText type="info" visible>
              {rawPrinters.length && filterXPOnly ? t("printer.emptyFiltered") : t("printer.empty")}
            </HelperText>
          )}

          <View style={styles.printerActions}>
            <Button
              mode="outlined"
              onPress={handleTestPrinter}
              disabled={printerBusy || !savedPrinter || !isPrinterConnected}
            >
              {t("printer.test")}
            </Button>
            <Button
              mode="text"
              onPress={handleClearPrinter}
              disabled={printerBusy || !savedPrinter}
            >
              {t("printer.clear")}
            </Button>
          </View>
        </Surface>

        {user ? (
          <Button
            mode="contained"
            buttonColor="#FF4D4F"
            onPress={handleLogout}
            loading={isSubmitting}
            disabled={isSubmitting}
            style={styles.logoutButton}
          >
            {t("common:actions.logout")}
          </Button>
        ) : null}
      </ScrollView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: "#F5F7FA" },
  content: { paddingHorizontal: 14, paddingTop: 4, paddingBottom: 10, gap: 14 },
  title: { textAlign: "center", marginBottom: 2 },
  card: { padding: 22, borderRadius: 18, gap: 12 },
  value: { marginTop: 8, fontWeight: "600" },
  meta: { color: "#666" },
  statusBlock: { gap: 6, marginTop: 4 },
  storePickerWrap: {
    marginTop: 4,
  },
  storePickerLabel: {
    marginBottom: 8,
  },
  dropdownButton: {
    alignSelf: "stretch",
  },
  dropdownButtonContent: {
    flexDirection: "row-reverse",
    justifyContent: "space-between",
  },
  primaryButton: { marginTop: 4 },
  filterRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    marginTop: 4,
  },
  primaryPrinterActions: {
    flexDirection: "row",
    gap: 10,
    marginTop: 4,
  },
  primaryActionButton: {
    flex: 1,
  },
  printerList: {
    gap: 8,
    marginTop: 4,
  },
  printerRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 12,
    borderRadius: 12,
    backgroundColor: "#F7F8FA",
    paddingHorizontal: 12,
    paddingVertical: 10,
  },
  printerMeta: {
    flex: 1,
    gap: 2,
  },
  printerName: {
    fontWeight: "600",
  },
  printerActions: {
    flexDirection: "row",
    gap: 10,
    marginTop: 4,
  },
  secondaryButton: { marginTop: 8 },
  logoutButton: { marginTop: 2, marginBottom: 6 },
  successText: { color: "#1677FF", marginTop: 8 },
});
