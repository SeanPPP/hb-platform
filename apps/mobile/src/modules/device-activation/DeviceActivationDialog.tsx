import { useCallback, useEffect, useRef, useState } from "react";
import { KeyboardAvoidingView, Platform, ScrollView, StyleSheet, View } from "react-native";
import { CameraView } from "expo-camera";
import {
  Button,
  HelperText,
  Modal,
  Portal,
  Text,
  TextInput,
} from "react-native-paper";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { useCameraScan } from "@/modules/scanner/use-camera-scan";
import { parseDeviceActivationCode } from "./device-activation-code";
import {
  activateMobileDeviceAccount,
  previewMobileDeviceActivation,
  recoverStoredMobileDeviceActivation,
} from "./device-activation-runtime";
import { DeviceActivationRecoveryRequiredError } from "./device-activation-operation";
import type {
  MobileDeviceActivationBinding,
  MobileDeviceActivationMode,
  MobileDeviceActivationPreview,
} from "./types";

interface DeviceActivationDialogProps {
  visible: boolean;
  mode: MobileDeviceActivationMode;
  onDismiss(reason: "cancelled" | "completed"): void;
  onCompleted(binding: MobileDeviceActivationBinding): void | Promise<void>;
}

const BRAND_RED = "#E53935";

export function DeviceActivationDialog({
  visible,
  mode,
  onDismiss,
  onCompleted,
}: DeviceActivationDialogProps) {
  const { t } = useAppTranslation(["login", "common"]);
  const [activationCode, setActivationCode] = useState("");
  const [preview, setPreview] = useState<MobileDeviceActivationPreview | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [recoveryRequired, setRecoveryRequired] = useState(false);
  const [scanResetKey, setScanResetKey] = useState(0);
  const previewGeneration = useRef(0);
  const onCompletedRef = useRef(onCompleted);
  const onDismissRef = useRef(onDismiss);
  const translationRef = useRef(t);
  onCompletedRef.current = onCompleted;
  onDismissRef.current = onDismiss;
  translationRef.current = t;

  const resetDraft = useCallback(() => {
    previewGeneration.current += 1;
    setActivationCode("");
    setPreview(null);
    setError("");
    setRecoveryRequired(false);
    setBusy(false);
    setScanResetKey((value) => value + 1);
  }, []);

  const finish = useCallback(async (binding: MobileDeviceActivationBinding) => {
    await onCompletedRef.current(binding);
    resetDraft();
    onDismissRef.current("completed");
  }, [resetDraft]);

  useEffect(() => {
    if (!visible) {
      resetDraft();
      return;
    }

    let cancelled = false;
    setRecoveryRequired(false);
    setBusy(true);
    void recoverStoredMobileDeviceActivation()
      .then(async (binding) => {
        if (!cancelled && binding) {
          await finish(binding);
        }
      })
      .catch((recoveryError) => {
        if (!cancelled) {
          const isStillUncertain =
            recoveryError instanceof DeviceActivationRecoveryRequiredError;
          setRecoveryRequired(isStillUncertain);
          setError(
            isStillUncertain
              ? translationRef.current("activation.recoveryPending")
              : translationRef.current("activation.recoveryFailed"),
          );
        }
      })
      .finally(() => {
        if (!cancelled) {
          setBusy(false);
        }
      });

    return () => {
      cancelled = true;
      previewGeneration.current += 1;
    };
    // visible 每次由 false 切到 true 都代表一个新的用户显式开通会话。
  }, [finish, resetDraft, visible]);

  const handlePreview = async (rawCode = activationCode) => {
    if (recoveryRequired) {
      return;
    }
    const parsed = parseDeviceActivationCode(rawCode);
    if (!parsed) {
      setPreview(null);
      setError(t("activation.invalidCode"));
      return;
    }

    const generation = ++previewGeneration.current;
    setActivationCode(parsed);
    setPreview(null);
    setError("");
    setBusy(true);
    try {
      const result = await previewMobileDeviceActivation(parsed, mode);
      if (generation !== previewGeneration.current) {
        return;
      }
      if (!result.isAllowed) {
        setError(t("activation.previewRejected"));
        return;
      }
      setPreview(result);
    } catch {
      if (generation === previewGeneration.current) {
        setError(t("activation.previewFailed"));
      }
    } finally {
      if (generation === previewGeneration.current) {
        setBusy(false);
      }
    }
  };

  const cameraScan = useCameraScan({
    disabled: !visible || busy || recoveryRequired || Boolean(preview),
    ignoreWhileProcessing: true,
    resetKey: scanResetKey,
    singleScanUntilReset: true,
    suppressRepeatsUntilChange: true,
    onBarcode: (barcode) => handlePreview(barcode),
  });

  const handleCodeChange = (value: string) => {
    if (recoveryRequired) {
      return;
    }
    previewGeneration.current += 1;
    setActivationCode(value);
    setPreview(null);
    setError("");
    setScanResetKey((current) => current + 1);
  };

  const handleConfirm = async () => {
    const parsed = parseDeviceActivationCode(activationCode);
    if (!parsed || !preview?.isAllowed) {
      setError(t("activation.previewFirst"));
      return;
    }

    setBusy(true);
    setError("");
    try {
      const binding = await activateMobileDeviceAccount(mode, parsed);
      await finish(binding);
    } catch (commitError) {
      const isStillUncertain =
        commitError instanceof DeviceActivationRecoveryRequiredError;
      setRecoveryRequired(isStillUncertain);
      setError(
        isStillUncertain
          ? t("activation.recoveryPending")
          : t("activation.commitFailed"),
      );
    } finally {
      setBusy(false);
    }
  };

  const handleRecovery = async () => {
    setBusy(true);
    setError("");
    try {
      const binding = await recoverStoredMobileDeviceActivation();
      if (binding) {
        await finish(binding);
        return;
      }
      setRecoveryRequired(false);
    } catch (recoveryError) {
      const isStillUncertain =
        recoveryError instanceof DeviceActivationRecoveryRequiredError;
      setRecoveryRequired(isStillUncertain);
      setError(
        isStillUncertain
          ? t("activation.recoveryPending")
          : t("activation.recoveryFailed"),
      );
    } finally {
      setBusy(false);
    }
  };

  const dismiss = () => {
    if (busy) {
      return;
    }
    resetDraft();
    onDismiss("cancelled");
  };

  return (
    <Portal>
      <Modal
        visible={visible}
        onDismiss={dismiss}
        contentContainerStyle={styles.modal}
      >
        <KeyboardAvoidingView behavior={Platform.OS === "ios" ? "padding" : undefined}>
          <ScrollView keyboardShouldPersistTaps="handled" showsVerticalScrollIndicator={false}>
            <Text variant="titleLarge" style={styles.title}>
              {t(mode === "rebind" ? "activation.rebindTitle" : "activation.title")}
            </Text>
            <Text variant="bodyMedium" style={styles.description}>
              {t("activation.description")}
            </Text>

            {cameraScan.permission?.granted ? (
              <View style={styles.cameraFrame}>
                <CameraView
                  style={styles.camera}
                  barcodeScannerSettings={{ barcodeTypes: ["qr"] }}
                  {...cameraScan.cameraProps}
                />
                <View pointerEvents="none" style={styles.scanGuide} />
              </View>
            ) : (
              <View style={styles.permissionBox}>
                <Text variant="bodySmall">{t("activation.cameraPermission")}</Text>
                <Button
                  compact
                  mode="outlined"
                  icon="camera"
                  onPress={() => void cameraScan.requestPermission()}
                  disabled={busy || recoveryRequired}
                >
                  {t("activation.grantCamera")}
                </Button>
              </View>
            )}

            <TextInput
              mode="outlined"
              label={t("activation.codeLabel")}
              placeholder="HBDEV1-…-…"
              value={activationCode}
              onChangeText={handleCodeChange}
              autoCapitalize="characters"
              autoCorrect={false}
              disabled={busy || recoveryRequired}
              style={styles.input}
            />
            <Button
              mode="outlined"
              icon="shield-search"
              onPress={() => void handlePreview()}
              loading={busy && !preview}
              disabled={busy || recoveryRequired || !activationCode}
            >
              {t("activation.preview")}
            </Button>

            {preview ? (
              <View style={styles.previewCard}>
                <Text variant="titleMedium" style={styles.previewTitle}>
                  {t("activation.confirmTitle")}
                </Text>
                <Text>{t("activation.account", {
                  value: preview.targetFullName || preview.targetUsername || t("common:na"),
                })}</Text>
                <Text>{t("activation.store", {
                  value: preview.storeName || preview.storeCode || t("common:na"),
                })}</Text>
                <Text>{t("activation.storeCount", {
                  count: preview.assignedStoreCount ?? 0,
                })}</Text>
                <Text>{t("activation.expires", {
                  value: preview.expiresAtUtc || t("common:na"),
                })}</Text>
                <Text variant="bodySmall" style={styles.warning}>
                  {t("activation.fullAccessWarning")}
                </Text>
              </View>
            ) : null}

            <HelperText type="error" visible={Boolean(error)}>
              {error}
            </HelperText>
            <View style={styles.actions}>
              <Button mode="text" onPress={dismiss} disabled={busy}>
                {t("common:actions.cancel")}
              </Button>
              {recoveryRequired ? (
                <Button
                  mode="contained"
                  buttonColor={BRAND_RED}
                  icon="restore"
                  onPress={() => void handleRecovery()}
                  loading={busy}
                  disabled={busy}
                >
                  {t("activation.retryRecovery")}
                </Button>
              ) : (
                <Button
                  mode="contained"
                  buttonColor={BRAND_RED}
                  icon="link-variant"
                  onPress={() => void handleConfirm()}
                  loading={busy && Boolean(preview)}
                  disabled={busy || !preview}
                >
                  {t(mode === "rebind" ? "activation.confirmRebind" : "activation.confirm")}
                </Button>
              )}
            </View>
          </ScrollView>
        </KeyboardAvoidingView>
      </Modal>
    </Portal>
  );
}

const styles = StyleSheet.create({
  modal: {
    alignSelf: "center",
    backgroundColor: "#FFFFFF",
    borderRadius: 18,
    maxHeight: "92%",
    maxWidth: 560,
    padding: 20,
    width: "92%",
  },
  title: { color: "#222", fontWeight: "800" },
  description: { color: "#616873", lineHeight: 20, marginBottom: 14, marginTop: 6 },
  cameraFrame: {
    backgroundColor: "#111827",
    borderRadius: 14,
    height: 210,
    marginBottom: 14,
    overflow: "hidden",
  },
  camera: { flex: 1 },
  scanGuide: {
    borderColor: "#FFFFFF",
    borderRadius: 12,
    borderWidth: 2,
    bottom: 30,
    left: 54,
    position: "absolute",
    right: 54,
    top: 30,
  },
  permissionBox: {
    alignItems: "center",
    backgroundColor: "#F8F9FA",
    borderRadius: 14,
    gap: 10,
    marginBottom: 14,
    padding: 18,
  },
  input: { backgroundColor: "#FFFFFF", marginBottom: 10 },
  previewCard: {
    backgroundColor: "#FFF7F6",
    borderColor: "#F2D7D5",
    borderRadius: 14,
    borderWidth: 1,
    gap: 6,
    marginTop: 14,
    padding: 14,
  },
  previewTitle: { color: "#222", fontWeight: "700", marginBottom: 2 },
  warning: { color: "#A8071A", lineHeight: 18, marginTop: 4 },
  actions: { flexDirection: "row", gap: 8, justifyContent: "flex-end", marginTop: 4 },
});
