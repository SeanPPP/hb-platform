import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { StyleSheet, Text, View } from "react-native";

import {
  cameraScannerContextCopyKey,
  cameraScannerText,
  resolveCameraScannerLocale,
} from "./camera-scanner-copy";
import {
  CAMERA_SCANNER_MIN_TOUCH_TARGET,
  type CameraScannerBarcodeResult,
  CameraScannerCameraView,
  type CameraScannerPort,
  CameraScannerState,
  type CameraScannerTranslate,
  useCameraScannerSession,
} from "./camera-scanner-session";

import {
  normalizeScanValue,
  type ScannerCaptureContext,
} from "@/core/peripherals/scanner";
import { PosPressable } from "@/ui/controls/pos-pressable";
import { posColors } from "@/ui/theme";

export const CAMERA_SCANNER_DUPLICATE_UNLOCK_MS = 1200;

export type CameraScannerInlineProps = Readonly<{
  context: ScannerCaptureContext;
  onClose(): void;
  onScan(value: string): boolean | Promise<boolean>;
  scanner: CameraScannerPort;
  visible: boolean;
}>;

type InlineSubmissionState = "idle" | "verifying" | "submitted" | "failed";

export function CameraScannerInline({
  context,
  onClose,
  onScan,
  scanner,
  visible,
}: CameraScannerInlineProps) {
  const { i18n } = useTranslation();
  const locale = resolveCameraScannerLocale(
    i18n.resolvedLanguage ?? i18n.language,
  );
  const t: CameraScannerTranslate = (key) => cameraScannerText(locale, key);
  const {
    availability,
    cameraStartedRef,
    deactivateSession,
    markCameraUnavailable,
    permissionDenied,
    permissionGranted,
    requestCameraPermission,
    showCamera,
  } = useCameraScannerSession({
    scanner,
    visible,
  });
  const [submissionState, setSubmissionState] =
    useState<InlineSubmissionState>("idle");
  const lockTimersRef = useRef(
    new Map<string, ReturnType<typeof setTimeout>>(),
  );
  const pendingValuesRef = useRef<string[]>([]);
  const pendingValueSetRef = useRef(new Set<string>());
  const submittingRef = useRef(false);
  const submittingValueRef = useRef<string | null>(null);
  const submissionGenerationRef = useRef(0);
  const mountedRef = useRef(true);
  const visibleRef = useRef(visible);
  const closingRef = useRef(false);
  const deactivateSessionRef = useRef(deactivateSession);
  const onScanRef = useRef(onScan);
  const drainQueueRef = useRef<() => void>(() => undefined);
  deactivateSessionRef.current = deactivateSession;
  onScanRef.current = onScan;

  const clearBarcodeLocks = useCallback(() => {
    lockTimersRef.current.forEach((timer) => clearTimeout(timer));
    lockTimersRef.current.clear();
  }, []);

  const disposeContinuousSession = useCallback(() => {
    submissionGenerationRef.current += 1;
    submittingRef.current = false;
    submittingValueRef.current = null;
    pendingValuesRef.current = [];
    pendingValueSetRef.current.clear();
    clearBarcodeLocks();
  }, [clearBarcodeLocks]);

  const lockBarcode = useCallback((value: string) => {
    const existingTimer = lockTimersRef.current.get(value);
    if (existingTimer) clearTimeout(existingTimer);
    const timer = setTimeout(() => {
      lockTimersRef.current.delete(value);
    }, CAMERA_SCANNER_DUPLICATE_UNLOCK_MS);
    lockTimersRef.current.set(value, timer);
  }, []);

  const isCurrentSubmission = useCallback((generation: number) => {
    return (
      mountedRef.current &&
      visibleRef.current &&
      generation === submissionGenerationRef.current
    );
  }, []);

  const drainQueue = useCallback(() => {
    if (submittingRef.current) return;
    const submissionGeneration = submissionGenerationRef.current;
    submittingRef.current = true;

    const run = async (): Promise<void> => {
      try {
        while (isCurrentSubmission(submissionGeneration)) {
          const value = pendingValuesRef.current.shift();
          if (!value) break;
          pendingValueSetRef.current.delete(value);
          submittingValueRef.current = value;
          setSubmissionState("verifying");

          let submitted = false;
          try {
            submitted = await onScanRef.current(value);
          } catch {
            submitted = false;
          }
          if (!isCurrentSubmission(submissionGeneration)) return;
          setSubmissionState(submitted ? "submitted" : "failed");
        }
      } finally {
        if (!isCurrentSubmission(submissionGeneration)) return;
        submittingRef.current = false;
        submittingValueRef.current = null;
        // 扫码可能恰好落在队列排空与 finally 之间；再次检查以免漏单。
        if (pendingValuesRef.current.length > 0) {
          drainQueueRef.current();
        }
      }
    };

    void run();
  }, [isCurrentSubmission]);
  drainQueueRef.current = drainQueue;

  useEffect(() => {
    visibleRef.current = visible;
    if (visible) {
      closingRef.current = false;
      return;
    }
    disposeContinuousSession();
    setSubmissionState("idle");
  }, [disposeContinuousSession, visible]);

  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
      visibleRef.current = false;
      disposeContinuousSession();
      deactivateSessionRef.current();
    };
  }, [disposeContinuousSession]);

  const close = useCallback(() => {
    if (closingRef.current) return;
    closingRef.current = true;
    visibleRef.current = false;
    disposeContinuousSession();
    setSubmissionState("idle");
    deactivateSession();
    onClose();
  }, [deactivateSession, disposeContinuousSession, onClose]);

  const handleBarcodeScanned = useCallback(
    (result: CameraScannerBarcodeResult) => {
      if (!cameraStartedRef.current || closingRef.current) {
        return;
      }
      const value = normalizeScanValue(result.data ?? "");
      if (!value) {
        return;
      }
      if (lockTimersRef.current.has(value)) {
        lockBarcode(value);
        return;
      }
      if (
        submittingValueRef.current === value ||
        pendingValueSetRef.current.has(value)
      ) {
        // 同码在提交或排队期间仍视为持续出现，必须继续延后解锁。
        lockBarcode(value);
        return;
      }
      if (!scanner.acceptCameraText(value)) {
        return;
      }

      lockBarcode(value);
      pendingValuesRef.current.push(value);
      pendingValueSetRef.current.add(value);
      drainQueue();
    },
    [cameraStartedRef, drainQueue, lockBarcode, scanner],
  );

  if (!visible) {
    return null;
  }

  return (
    <View
      accessibilityLabel={`${t("inline.header.eyebrow")} ${contextLabel(context, t)}`}
      style={styles.container}
      testID="camera-scanner-inline"
    >
      <View style={styles.header}>
        <View style={styles.headerText}>
          <Text style={styles.eyebrow}>{t("inline.header.eyebrow")}</Text>
          <Text numberOfLines={2} style={styles.title}>
            {contextLabel(context, t)}
          </Text>
        </View>
        <PosPressable
          accessibilityLabel={t("inline.action.closeLabel")}
          accessibilityRole="button"
          onPress={close}
          sound="navigate"
          style={({ pressed }) => [
            styles.closeButton,
            pressed && styles.buttonPressed,
          ]}
          testID="camera-scanner-inline-close"
        >
          <Text style={styles.closeButtonLabel}>{t("action.cancel")}</Text>
        </PosPressable>
      </View>

      <View style={styles.previewFrame}>
        {showCamera ? (
          <>
            <CameraScannerCameraView
              accessibilityLabel={t("preview.label")}
              onBarcodeScanned={handleBarcodeScanned}
              onMountUnavailable={() => {
                // 原生 view 启动失败时不允许回调继续被当作有效扫码。
                markCameraUnavailable();
              }}
              style={styles.preview}
            />
            <View pointerEvents="none" style={styles.target} />
          </>
        ) : (
          <CameraScannerState
            availability={availability}
            permissionGranted={permissionGranted}
            permissionDenied={permissionDenied}
            onRequestPermission={requestCameraPermission}
            stateStyle={styles.state}
            t={t}
          />
        )}
      </View>

      <View
        accessibilityLiveRegion="polite"
        style={statusStyle(submissionState)}
        testID="camera-scanner-inline-status"
      >
        <Text style={statusTextStyle(submissionState)}>
          {submissionLabel(submissionState, t)}
        </Text>
      </View>
    </View>
  );
}

function contextLabel(
  context: ScannerCaptureContext,
  t: CameraScannerTranslate,
): string {
  return t(cameraScannerContextCopyKey(context));
}

function submissionLabel(
  state: InlineSubmissionState,
  t: CameraScannerTranslate,
): string {
  switch (state) {
    case "failed":
      return t("inline.status.failed");
    case "submitted":
      return t("inline.status.submitted");
    case "verifying":
      return t("inline.status.verifying");
    case "idle":
      return t("inline.status.ready");
  }
}

function statusStyle(state: InlineSubmissionState) {
  switch (state) {
    case "failed":
      return [styles.status, styles.statusFailed];
    case "submitted":
      return [styles.status, styles.statusSubmitted];
    case "verifying":
      return [styles.status, styles.statusVerifying];
    case "idle":
      return [styles.status, styles.statusIdle];
  }
}

function statusTextStyle(state: InlineSubmissionState) {
  switch (state) {
    case "failed":
      return [styles.statusText, styles.statusTextFailed];
    case "submitted":
      return [styles.statusText, styles.statusTextSubmitted];
    case "verifying":
      return [styles.statusText, styles.statusTextVerifying];
    case "idle":
      return [styles.statusText, styles.statusTextIdle];
  }
}

const styles = StyleSheet.create({
  buttonPressed: { opacity: 0.8 },
  closeButton: {
    alignItems: "center",
    borderColor: posColors.border,
    borderRadius: 2,
    borderWidth: 1,
    justifyContent: "center",
    minHeight: CAMERA_SCANNER_MIN_TOUCH_TARGET,
    minWidth: CAMERA_SCANNER_MIN_TOUCH_TARGET,
    paddingHorizontal: 12,
  },
  closeButtonLabel: { color: posColors.ink, fontSize: 15, fontWeight: "700" },
  container: {
    alignSelf: "stretch",
    backgroundColor: posColors.surface,
    borderLeftColor: posColors.border,
    borderLeftWidth: 1,
    gap: 12,
    maxWidth: "100%",
    padding: 14,
    width: "100%",
  },
  eyebrow: {
    color: posColors.orange,
    fontSize: 12,
    fontWeight: "800",
    letterSpacing: 0,
  },
  header: {
    alignItems: "flex-start",
    flexDirection: "row",
    gap: 10,
    justifyContent: "space-between",
  },
  headerText: { flex: 1, minWidth: 0 },
  preview: { flex: 1 },
  previewFrame: {
    backgroundColor: "#0A1723",
    height: 210,
    overflow: "hidden",
    position: "relative",
  },
  state: {
    flex: 1,
    minHeight: 210,
  },
  status: {
    alignItems: "center",
    borderRadius: 2,
    borderWidth: 1,
    justifyContent: "center",
    minHeight: CAMERA_SCANNER_MIN_TOUCH_TARGET,
    paddingHorizontal: 12,
  },
  statusFailed: {
    backgroundColor: posColors.redSoft,
    borderColor: posColors.red,
  },
  statusIdle: {
    backgroundColor: posColors.canvas,
    borderColor: posColors.border,
  },
  statusSubmitted: {
    backgroundColor: posColors.greenSoft,
    borderColor: posColors.green,
  },
  statusText: {
    fontSize: 15,
    fontWeight: "800",
    textAlign: "center",
  },
  statusTextFailed: { color: posColors.red },
  statusTextIdle: { color: posColors.mutedInk },
  statusTextSubmitted: { color: posColors.green },
  statusTextVerifying: { color: posColors.blue },
  statusVerifying: {
    backgroundColor: posColors.blueSoft,
    borderColor: posColors.blue,
  },
  target: {
    borderColor: "#FFFFFF",
    borderRadius: 2,
    borderWidth: 2,
    bottom: "32%",
    left: "10%",
    position: "absolute",
    right: "10%",
    top: "32%",
  },
  title: {
    color: posColors.ink,
    fontSize: 20,
    fontWeight: "800",
    lineHeight: 25,
    marginTop: 2,
  },
});
