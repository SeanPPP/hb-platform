import { MaterialCommunityIcons } from "@expo/vector-icons";
import { Redirect } from "expo-router";
import { StatusBar } from "expo-status-bar";
import {
  type Dispatch,
  type SetStateAction,
  useCallback,
  useEffect,
  useRef,
  useState,
} from "react";
import { useTranslation } from "react-i18next";
import {
  ScrollView,
  StyleSheet,
  Text,
  View,
} from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";

import { reconcileDeviceSessionRuntime } from "./device-registration-state";
import { serverConnectionPanelCopy } from "./server-connection-copy";
import { ServerConnectionPanel } from "./server-connection-panel";

import {
  HbposApiError,
  type DeviceRegistrationStore,
} from "@/core/api/hbpos-api";
import { usePosRuntime } from "@/core/runtime/pos-runtime-context";
import type { DeviceSessionState } from "@/core/security/device-session";
import { toggleAppLanguage } from "@/i18n";
import { PosPressable } from "@/ui/controls/pos-pressable";
import { HandheldStateSurface } from "@/ui/handheld";
import { PosStatusStrip } from "@/ui/shell/status-strip";
import { posColors } from "@/ui/theme";

type StoreLoadState = "idle" | "loading" | "ready" | "failed";

export function DeviceRegistrationScreen() {
  const { t } = useTranslation();
  const runtime = usePosRuntime();
  const [session, setSession] = useState<DeviceSessionState | null>(null);
  const [requestError, setRequestError] = useState<string | null>(null);
  const [stores, setStores] = useState<readonly DeviceRegistrationStore[]>([]);
  const [storeLoadState, setStoreLoadState] =
    useState<StoreLoadState>("idle");
  const [storeLoadError, setStoreLoadError] = useState<string | null>(null);
  const [selectedStoreCode, setSelectedStoreCode] = useState("");
  const [pickerVisible, setPickerVisible] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const serverOperation = useRef<AbortController | null>(null);
  const deviceSession = runtime.services?.deviceSession;
  const serverConnection = runtime.services?.serverConnection;
  const selectedStore =
    stores.find((store) => store.storeCode === selectedStoreCode) ?? null;
  const visibleRegistrationState = registrationState(
    session,
    runtime.state.phase,
  );

  const reconcile = useCallback(
    async (next: DeviceSessionState) => {
      setSession(next);
      await reconcileDeviceSessionRuntime(next, runtime);
    },
    [runtime],
  );

  const loadStores = useCallback(async () => {
    if (!deviceSession) {
      setStoreLoadState("failed");
      setStoreLoadError(null);
      return;
    }

    setStoreLoadState("loading");
    setStoreLoadError(null);
    try {
      const nextStores = await deviceSession.listRegistrationStores();
      setStores(nextStores);
      setSelectedStoreCode((current) =>
        nextStores.some((store) => store.storeCode === current)
          ? current
          : "",
      );
      setStoreLoadState("ready");
    } catch (error: unknown) {
      setStores([]);
      setStoreLoadState("failed");
      setStoreLoadError(error instanceof Error ? error.message : null);
    }
  }, [deviceSession]);

  useEffect(() => {
    if (runtime.state.phase === "pending-approval") {
      return;
    }
    void loadStores();
  }, [loadStores, runtime.state.phase]);

  const poll = useCallback(async () => {
    if (!deviceSession) return;
    try {
      const next = await deviceSession.poll();
      setRequestError(null);
      await reconcile(next);
    } catch (error: unknown) {
      applyRequestFailure(error, runtime, setRequestError);
    }
  }, [deviceSession, reconcile, runtime]);

  useEffect(() => {
    if (
      runtime.state.phase !== "pending-approval" ||
      !deviceSession
    ) {
      return;
    }

    void poll();
    const timer = setInterval(() => void poll(), 5_000);
    return () => clearInterval(timer);
  }, [deviceSession, poll, runtime.state.phase]);

  useEffect(
    () => () => {
      serverOperation.current?.abort();
    },
    [],
  );

  if (
    runtime.state.phase === "ready" ||
    runtime.state.phase === "ready-offline"
  ) {
    return <Redirect href="/" />;
  }

  const submit = async () => {
    if (!deviceSession) {
      setRequestError(t("registration.runtimeUnavailable"));
      return;
    }
    if (!selectedStore) {
      setRequestError(t("registration.storeRequired"));
      return;
    }

    setRequestError(null);
    setIsSubmitting(true);
    try {
      const next = await deviceSession.register({
        storeCode: selectedStore.storeCode,
      });
      await reconcile(next);
    } catch (error: unknown) {
      applyRequestFailure(error, runtime, setRequestError);
    } finally {
      setIsSubmitting(false);
    }
  };

  const submitDisabled =
    isSubmitting ||
    runtime.state.database !== "ready" ||
    storeLoadState !== "ready" ||
    !selectedStore;

  return (
    <SafeAreaView style={styles.safeArea}>
      <StatusBar style="dark" />
      <PosStatusStrip />
      <View style={styles.languageBar}>
        <PosPressable
          accessibilityLabel={t("registration.languageSwitchLabel")}
          accessibilityRole="button"
          hitSlop={8}
          onPress={() => void toggleAppLanguage()}
          style={({ pressed }) => [
            styles.languageButton,
            pressed && styles.languageButtonPressed,
          ]}
          testID="registration-language-switch"
        >
          <MaterialCommunityIcons
            color={posColors.ink}
            name="translate"
            size={18}
          />
          <Text style={styles.languageButtonLabel}>
            {t("registration.languageSwitch")}
          </Text>
        </PosPressable>
      </View>
      <HandheldStateSurface
        slug="device-registration"
        style={styles.stateSurface}
      >
        <ScrollView
          contentContainerStyle={styles.page}
          keyboardShouldPersistTaps="handled"
          showsVerticalScrollIndicator={false}
        >
          <View style={styles.contextPanel}>
            <Text style={styles.eyebrow}>{t("registration.eyebrow")}</Text>
            <Text style={styles.title}>{t("registration.title")}</Text>
            <Text style={styles.subtitle}>{t("registration.subtitle")}</Text>

            <View style={styles.securityNote}>
              <MaterialCommunityIcons
                color={posColors.green}
                name="shield-lock-outline"
                size={22}
              />
              <Text style={styles.securityCopy}>
                {t("registration.securityNote")}
              </Text>
            </View>
          </View>

          <View style={styles.formPanel}>
            <Text style={styles.formTitle}>
              {runtime.state.phase === "pending-approval"
                ? t("registration.pendingTitle")
                : t("registration.formTitle")}
            </Text>
            <Text style={styles.formHint}>
              {runtime.state.phase === "pending-approval"
                ? t("registration.pendingHint")
                : t("registration.formHint")}
            </Text>

            {visibleRegistrationState ? (
              <HandheldStateSurface
                slug="registration-states"
                style={styles.registrationStateSurface}
              >
                <View
                  accessibilityLiveRegion="polite"
                  style={[
                    styles.registrationState,
                    visibleRegistrationState === "approved" &&
                      styles.registrationStateApproved,
                    (visibleRegistrationState === "rejected" ||
                      visibleRegistrationState === "disabled") &&
                      styles.registrationStateRejected,
                  ]}
                  testID={`registration-state-${visibleRegistrationState}`}
                >
                  <MaterialCommunityIcons
                    color={
                      visibleRegistrationState === "approved"
                        ? posColors.green
                        : visibleRegistrationState === "pending"
                          ? posColors.orange
                          : posColors.red
                    }
                    name={
                      visibleRegistrationState === "approved"
                        ? "check-circle-outline"
                        : visibleRegistrationState === "pending"
                          ? "clock-outline"
                          : "alert-circle-outline"
                    }
                    size={22}
                  />
                  <View style={styles.registrationStateCopy}>
                    <Text style={styles.registrationStateTitle}>
                      {t(`registration.state.${visibleRegistrationState}`)}
                    </Text>
                    <Text style={styles.registrationStateHint}>
                      {t(`registration.state.${visibleRegistrationState}Hint`)}
                    </Text>
                  </View>
                </View>
              </HandheldStateSurface>
            ) : null}

            {runtime.state.phase !== "pending-approval" ? (
            <>
              <FieldLabel>{t("registration.storeCode")}</FieldLabel>
              <PosPressable
                accessibilityRole="button"
                accessibilityState={{
                  disabled:
                    storeLoadState !== "ready" || stores.length === 0,
                  expanded: pickerVisible,
                }}
                disabled={
                  storeLoadState !== "ready" || stores.length === 0
                }
                onPress={() => setPickerVisible(true)}
                sound="navigate"
                style={({ pressed }) => [
                  styles.storePicker,
                  pressed && styles.storePickerPressed,
                ]}
                testID="registration-store-picker"
              >
                <View style={styles.storePickerCopy}>
                  <Text
                    style={[
                      styles.storePickerValue,
                      !selectedStore && styles.storePickerPlaceholder,
                    ]}
                  >
                    {selectedStore
                      ? storeDisplayName(selectedStore)
                      : storePickerMessage(storeLoadState, stores.length, t)}
                  </Text>
                </View>
                <MaterialCommunityIcons
                  color={posColors.ink}
                  name="chevron-down"
                  size={24}
                />
              </PosPressable>

              {storeLoadState === "failed" ||
              (storeLoadState === "ready" && stores.length === 0) ? (
                <View style={styles.storeStatus}>
                  <Text style={styles.storeStatusText}>
                    {storeLoadError ?? (
                      storeLoadState === "failed"
                        ? t("registration.storeLoadFailed")
                        : t("registration.storeEmpty")
                    )}
                  </Text>
                  <PosPressable
                    accessibilityRole="button"
                    onPress={() => void loadStores()}
                    style={({ pressed }) => [
                      styles.retryButton,
                      pressed && styles.retryButtonPressed,
                    ]}
                    testID="registration-store-retry"
                  >
                    <Text style={styles.retryButtonLabel}>
                      {t("registration.storeRetry")}
                    </Text>
                  </PosPressable>
                </View>
              ) : null}
            </>
            ) : (
            <View style={styles.pendingCard}>
              <MaterialCommunityIcons
                color={posColors.orange}
                name="clock-outline"
                size={34}
              />
              <View style={styles.pendingCopy}>
                <Text style={styles.pendingCode}>
                  {session?.deviceCode ?? t("registration.pendingDeviceCode")}
                </Text>
                <Text style={styles.pendingStore}>
                  {session?.storeCode ?? t("registration.pendingStore")}
                </Text>
              </View>
            </View>
            )}

            {requestError ? (
            <View accessibilityRole="alert" style={styles.errorBanner}>
              <Text style={styles.errorBannerText}>{requestError}</Text>
            </View>
            ) : null}

            {runtime.state.phase !== "pending-approval" ? (
            <PosPressable
              accessibilityRole="button"
              accessibilityState={{ disabled: submitDisabled }}
              disabled={submitDisabled}
              onPress={() => void submit()}
              style={({ pressed }) => [
                styles.primaryButton,
                (pressed || isSubmitting) && styles.primaryButtonPressed,
                submitDisabled && styles.primaryButtonDisabled,
              ]}
              testID="registration-submit"
            >
              <Text style={styles.primaryButtonLabel}>
                {isSubmitting
                  ? t("registration.submitting")
                  : t("registration.submit")}
              </Text>
            </PosPressable>
            ) : (
            <PosPressable
              accessibilityRole="button"
              onPress={() => void poll()}
              style={({ pressed }) => [
                styles.secondaryButton,
                pressed && styles.secondaryButtonPressed,
              ]}
            >
              <Text style={styles.secondaryButtonLabel}>
                {t("registration.checkNow")}
              </Text>
            </PosPressable>
            )}

            {serverConnection ? (
            <View style={styles.serverConnectionPanel}>
              <ServerConnectionPanel
                canSave={runtime.state.phase === "registration-required"}
                copy={serverConnectionPanelCopy(t)}
                currentAddress={serverConnection.getCurrentApiBaseUrl()}
                saveAddress={async (address) => {
                  const controller = new AbortController();
                  serverOperation.current?.abort();
                  serverOperation.current = controller;
                  const result = await serverConnection.change(
                    address,
                    controller.signal,
                  );
                  if (result.status !== "completed") {
                    throw new Error(result.reason);
                  }
                  await runtime.retry();
                }}
                testAddress={(address) => {
                  const controller = new AbortController();
                  serverOperation.current?.abort();
                  serverOperation.current = controller;
                  return serverConnection.test(address, controller.signal);
                }}
              />
            </View>
            ) : null}
          </View>
        </ScrollView>
      </HandheldStateSurface>
      <StorePickerOverlay
        onClose={() => setPickerVisible(false)}
        onSelect={(storeCode) => {
          setSelectedStoreCode(storeCode);
          setRequestError(null);
          setPickerVisible(false);
        }}
        selectedStoreCode={selectedStoreCode}
        stores={stores}
        visible={pickerVisible}
      />
    </SafeAreaView>
  );
}

function StorePickerOverlay({
  onClose,
  onSelect,
  selectedStoreCode,
  stores,
  visible,
}: Readonly<{
  onClose(): void;
  onSelect(storeCode: string): void;
  selectedStoreCode: string;
  stores: readonly DeviceRegistrationStore[];
  visible: boolean;
}>) {
  const { t } = useTranslation();
  if (!visible) {
    return null;
  }

  return (
    <View style={styles.modalBackdrop}>
      <PosPressable
        accessible={false}
        onPress={onClose}
        sound="navigate"
        style={StyleSheet.absoluteFillObject}
        testID="registration-store-backdrop"
      />
      <View
        accessibilityViewIsModal
        style={styles.modalCard}
        testID="registration-store-modal"
      >
        <View style={styles.modalHeader}>
          <Text style={styles.modalTitle}>
            {t("registration.storePickerTitle")}
          </Text>
          <PosPressable
            accessibilityRole="button"
            onPress={onClose}
            sound="navigate"
            style={({ pressed }) => [
              styles.modalClose,
              pressed && styles.modalClosePressed,
            ]}
          >
            <Text style={styles.modalCloseLabel}>
              {t("registration.storePickerClose")}
            </Text>
          </PosPressable>
        </View>
        <ScrollView
          contentContainerStyle={styles.storeList}
          keyboardShouldPersistTaps="handled"
        >
          {stores.map((store) => {
            const selected = store.storeCode === selectedStoreCode;
            return (
              <PosPressable
                accessibilityRole="button"
                accessibilityState={{ selected }}
                key={store.storeCode}
                onPress={() => onSelect(store.storeCode)}
                style={({ pressed }) => [
                  styles.storeOption,
                  selected && styles.storeOptionSelected,
                  pressed && styles.storeOptionPressed,
                ]}
                testID={`registration-store-${store.storeCode}`}
              >
                <View style={styles.storeOptionCopy}>
                  <Text style={styles.storeOptionName}>
                    {store.storeName}
                  </Text>
                  <Text style={styles.storeOptionCode}>
                    {store.storeCode}
                  </Text>
                </View>
                {selected ? (
                  <MaterialCommunityIcons
                    color={posColors.green}
                    name="check-circle"
                    size={22}
                  />
                ) : null}
              </PosPressable>
            );
          })}
        </ScrollView>
      </View>
    </View>
  );
}

function applyRequestFailure(
  error: unknown,
  runtime: ReturnType<typeof usePosRuntime>,
  setRequestError: Dispatch<SetStateAction<string | null>>,
) {
  if (error instanceof HbposApiError) {
    if (error.status === 403) {
      runtime.updateOperationalState({
        backend: "rejected",
        device: "locked",
      });
    } else if (error.kind === "transport") {
      runtime.updateOperationalState({
        backend: "offline",
        device:
          runtime.state.device === "pending-approval"
            ? "pending-approval"
            : "registration-required",
      });
    }
    setRequestError(error.message);
    return;
  }
  setRequestError(
    error instanceof Error
      ? error.message
      : "Device registration request failed.",
  );
}

function FieldLabel({ children }: { children: string }) {
  return <Text style={styles.fieldLabel}>{children}</Text>;
}

type VisibleRegistrationState =
  | "pending"
  | "approved"
  | "rejected"
  | "disabled";

function registrationState(
  session: DeviceSessionState | null,
  runtimePhase: string,
): VisibleRegistrationState | null {
  switch (session?.status) {
    case "pending-approval":
      return "pending";
    case "authorized":
      return "approved";
    case "denied":
      return "rejected";
    case "disabled":
      return "disabled";
    default:
      return runtimePhase === "pending-approval" ? "pending" : null;
  }
}

function storeDisplayName(store: DeviceRegistrationStore): string {
  return `${store.storeName} · ${store.storeCode}`;
}

function storePickerMessage(
  state: StoreLoadState,
  storeCount: number,
  t: ReturnType<typeof useTranslation>["t"],
): string {
  if (state === "loading" || state === "idle") {
    return t("registration.storeLoading");
  }
  if (state === "failed") {
    return t("registration.storeLoadFailed");
  }
  if (storeCount === 0) {
    return t("registration.storeEmpty");
  }
  return t("registration.storePlaceholder");
}

const styles = StyleSheet.create({
  safeArea: { flex: 1, backgroundColor: posColors.canvas },
  stateSurface: { flex: 1 },
  languageBar: {
    minHeight: 48,
    alignItems: "flex-end",
    justifyContent: "center",
    paddingHorizontal: 16,
  },
  languageButton: {
    minWidth: 108,
    minHeight: 48,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "center",
    gap: 8,
    paddingHorizontal: 16,
    borderWidth: 1,
    borderColor: posColors.border,
    backgroundColor: posColors.surface,
  },
  languageButtonPressed: { backgroundColor: posColors.blueSoft },
  languageButtonLabel: {
    color: posColors.ink,
    fontSize: 13,
    fontWeight: "800",
  },
  page: {
    flexGrow: 1,
    padding: 16,
    gap: 16,
  },
  contextPanel: { width: "100%" },
  eyebrow: {
    color: posColors.orange,
    fontSize: 11,
    fontWeight: "900",
    letterSpacing: 1.8,
  },
  title: {
    marginTop: 8,
    color: posColors.ink,
    fontSize: 28,
    fontWeight: "900",
    lineHeight: 34,
  },
  subtitle: {
    marginTop: 8,
    color: posColors.mutedInk,
    fontSize: 14,
    lineHeight: 20,
  },
  securityNote: {
    marginTop: 16,
    flexDirection: "row",
    alignItems: "center",
    gap: 12,
    padding: 12,
    borderLeftWidth: 3,
    borderLeftColor: posColors.green,
    backgroundColor: posColors.greenSoft,
  },
  securityCopy: {
    flex: 1,
    color: posColors.ink,
    fontSize: 13,
    fontWeight: "600",
    lineHeight: 20,
  },
  formPanel: {
    width: "100%",
    padding: 16,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: posColors.border,
    backgroundColor: posColors.surface,
  },
  formTitle: { color: posColors.ink, fontSize: 20, fontWeight: "900" },
  formHint: {
    marginTop: 8,
    marginBottom: 16,
    color: posColors.mutedInk,
    fontSize: 14,
    lineHeight: 21,
  },
  fieldLabel: {
    marginTop: 14,
    marginBottom: 8,
    color: posColors.ink,
    fontSize: 13,
    fontWeight: "800",
  },
  storePicker: {
    minHeight: 50,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    paddingHorizontal: 15,
    borderWidth: 1,
    borderColor: posColors.border,
    backgroundColor: "#FFFFFF",
  },
  storePickerPressed: { backgroundColor: posColors.blueSoft },
  storePickerCopy: { flex: 1, paddingRight: 12 },
  storePickerValue: {
    color: posColors.ink,
    fontSize: 16,
    fontWeight: "700",
  },
  storePickerPlaceholder: {
    color: posColors.mutedInk,
    fontWeight: "500",
  },
  storeStatus: {
    marginTop: 10,
    padding: 12,
    borderLeftWidth: 3,
    borderLeftColor: posColors.orange,
    backgroundColor: "#FFF7ED",
  },
  storeStatusText: {
    color: posColors.ink,
    fontSize: 13,
    lineHeight: 19,
  },
  retryButton: {
    minHeight: 48,
    marginTop: 8,
    alignSelf: "flex-start",
    justifyContent: "center",
    paddingHorizontal: 12,
    borderWidth: 1,
    borderColor: posColors.ink,
  },
  retryButtonPressed: { backgroundColor: posColors.blueSoft },
  retryButtonLabel: {
    color: posColors.ink,
    fontSize: 13,
    fontWeight: "800",
  },
  errorBanner: {
    marginTop: 18,
    padding: 12,
    backgroundColor: "#FEE4E2",
  },
  errorBannerText: { color: "#912018", fontSize: 13, fontWeight: "700" },
  primaryButton: {
    minHeight: 52,
    marginTop: 24,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: posColors.ink,
  },
  primaryButtonPressed: { opacity: 0.7 },
  primaryButtonDisabled: { opacity: 0.45 },
  primaryButtonLabel: { color: "#FFFFFF", fontSize: 15, fontWeight: "900" },
  secondaryButton: {
    minHeight: 50,
    marginTop: 20,
    alignItems: "center",
    justifyContent: "center",
    borderWidth: 1,
    borderColor: posColors.ink,
  },
  secondaryButtonPressed: { backgroundColor: posColors.blueSoft },
  secondaryButtonLabel: {
    color: posColors.ink,
    fontSize: 14,
    fontWeight: "800",
  },
  pendingCard: {
    flexDirection: "row",
    alignItems: "center",
    gap: 16,
    padding: 20,
    borderWidth: 1,
    borderColor: posColors.border,
    backgroundColor: "#FFF7ED",
  },
  registrationStateSurface: { marginBottom: 8 },
  registrationState: {
    alignItems: "flex-start",
    backgroundColor: "#FFF7ED",
    borderColor: posColors.orange,
    borderRadius: 6,
    borderWidth: 1,
    flexDirection: "row",
    gap: 10,
    padding: 12,
  },
  registrationStateApproved: {
    backgroundColor: posColors.greenSoft,
    borderColor: posColors.green,
  },
  registrationStateRejected: {
    backgroundColor: posColors.redSoft,
    borderColor: posColors.red,
  },
  registrationStateCopy: { flex: 1 },
  registrationStateTitle: {
    color: posColors.ink,
    fontSize: 14,
    fontWeight: "900",
  },
  registrationStateHint: {
    color: posColors.mutedInk,
    fontSize: 12,
    lineHeight: 18,
    marginTop: 2,
  },
  pendingCopy: { flex: 1 },
  pendingCode: { color: posColors.ink, fontSize: 18, fontWeight: "900" },
  pendingStore: {
    marginTop: 4,
    color: posColors.mutedInk,
    fontSize: 13,
    fontWeight: "700",
  },
  serverConnectionPanel: {
    marginTop: 22,
  },
  modalBackdrop: {
    ...StyleSheet.absoluteFillObject,
    zIndex: 20,
    alignItems: "center",
    justifyContent: "center",
    padding: 16,
    backgroundColor: "rgba(10, 31, 50, 0.42)",
  },
  modalCard: {
    width: "100%",
    maxWidth: 560,
    maxHeight: "76%",
    padding: 16,
    borderWidth: 1,
    borderColor: posColors.border,
    backgroundColor: posColors.surface,
  },
  modalHeader: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 16,
    paddingBottom: 16,
    borderBottomWidth: 1,
    borderBottomColor: posColors.border,
  },
  modalTitle: {
    flex: 1,
    color: posColors.ink,
    fontSize: 22,
    fontWeight: "900",
  },
  modalClose: {
    minWidth: 72,
    minHeight: 48,
    alignItems: "center",
    justifyContent: "center",
    paddingHorizontal: 12,
    borderWidth: 1,
    borderColor: posColors.border,
  },
  modalClosePressed: { backgroundColor: posColors.blueSoft },
  modalCloseLabel: {
    color: posColors.ink,
    fontSize: 13,
    fontWeight: "800",
  },
  storeList: { paddingTop: 12, gap: 8 },
  storeOption: {
    minHeight: 58,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 12,
    paddingHorizontal: 16,
    paddingVertical: 10,
    borderWidth: 1,
    borderColor: posColors.border,
    backgroundColor: "#FFFFFF",
  },
  storeOptionSelected: {
    borderColor: posColors.green,
    backgroundColor: posColors.greenSoft,
  },
  storeOptionPressed: { opacity: 0.72 },
  storeOptionCopy: { flex: 1 },
  storeOptionName: {
    color: posColors.ink,
    fontSize: 15,
    fontWeight: "800",
  },
  storeOptionCode: {
    marginTop: 3,
    color: posColors.mutedInk,
    fontSize: 12,
    fontWeight: "700",
  },
});
