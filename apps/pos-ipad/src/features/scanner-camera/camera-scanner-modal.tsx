import { useCallback, useEffect, useRef } from "react";
import { useTranslation } from "react-i18next";
import { Modal, StyleSheet, Text, View } from "react-native";

import {
  cameraScannerContextCopyKey,
  cameraScannerText,
  resolveCameraScannerLocale,
} from "./camera-scanner-copy";
import {
  CAMERA_SCANNER_MIN_TOUCH_TARGET,
  CAMERA_SCANNER_SUPPORTED_ORIENTATIONS,
  type CameraScannerBarcodeResult,
  CameraScannerCameraView,
  CameraScannerState,
  type CameraScannerPort,
  type CameraScannerTranslate,
  useCameraScannerSession,
} from "./camera-scanner-session";

import {
  normalizeScanValue,
  type ScannerCaptureContext,
} from "@/core/peripherals/scanner";
import { PosPressable } from "@/ui/controls/pos-pressable";
import { posColors } from "@/ui/theme";

export {
  CAMERA_SCANNER_MIN_TOUCH_TARGET,
  type CameraScannerPort,
} from "./camera-scanner-session";

export type CameraScannerModalProps = Readonly<{
  visible: boolean;
  context: ScannerCaptureContext;
  scanner: CameraScannerPort;
  onScan(value: string): void;
  onClose(): void;
}>;

/**
 * 相机是 HID 的兜底输入，不改变当前路由上下文；条码仍必须先由 scanner 接受。
 * 扫描成功后立即关闭，避免原生回调抖动造成重复加购或重复授权。
 */
export function CameraScannerModal({
  context,
  onClose,
  onScan,
  scanner,
  visible,
}: CameraScannerModalProps) {
  const { i18n } = useTranslation();
  const locale = resolveCameraScannerLocale(
    i18n.resolvedLanguage ?? i18n.language,
  );
  const t: CameraScannerTranslate = (key) => cameraScannerText(locale, key);
  const deliveredRef = useRef(false);
  const closingRef = useRef(false);
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

  const close = useCallback(() => {
    if (closingRef.current) return;
    closingRef.current = true;
    deactivateSession();
    onClose();
  }, [deactivateSession, onClose]);

  useEffect(() => {
    if (!visible) {
      deactivateSession();
      return;
    }
    deliveredRef.current = false;
    closingRef.current = false;
  }, [deactivateSession, visible]);

  const handleBarcodeScanned = useCallback(
    (result: CameraScannerBarcodeResult) => {
      if (
        !cameraStartedRef.current ||
        deliveredRef.current ||
        closingRef.current
      ) {
        return;
      }
      const rawValue = result.data ?? "";
      // 开通码 parser 必须看见未经 Unicode trim 的原文，才能先拒绝所有非 ASCII 字符。
      const value =
        context === "device-activation"
          ? rawValue
          : normalizeScanValue(rawValue);
      if (!value || !scanner.acceptCameraText(value)) {
        return;
      }
      deliveredRef.current = true;
      try {
        void Promise.resolve(onScan(value)).catch(() => undefined);
      } catch {
        // 单次模式无论业务回调结果如何都必须释放相机，避免卡在不可重试状态。
      }
      close();
    },
    [cameraStartedRef, close, context, onScan, scanner],
  );

  return (
    <Modal
      animationType="fade"
      onRequestClose={close}
      presentationStyle="overFullScreen"
      statusBarTranslucent
      supportedOrientations={CAMERA_SCANNER_SUPPORTED_ORIENTATIONS}
      transparent
      visible={visible}
    >
      <View accessibilityViewIsModal style={styles.backdrop} testID="camera-scanner-modal">
        <PosPressable
          accessible={false}
          onPress={close}
          sound="navigate"
          style={styles.backdropDismissArea}
          testID="camera-scanner-backdrop"
        />
        <View style={styles.panel}>
          <Text style={styles.eyebrow}>{t("header.eyebrow")}</Text>
          <Text style={styles.title}>{contextLabel(context, t)}</Text>
          <Text style={styles.description}>{t("description")}</Text>

          {showCamera ? (
            <View style={styles.previewFrame}>
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
            </View>
          ) : (
            <CameraScannerState
              availability={availability}
              permissionGranted={permissionGranted}
              permissionDenied={permissionDenied}
              onRequestPermission={requestCameraPermission}
              stateStyle={styles.stateSpacing}
              t={t}
            />
          )}

          <PosPressable
            accessibilityLabel={t("action.closeLabel")}
            accessibilityRole="button"
            onPress={close}
            sound="navigate"
            style={({ pressed }) => [styles.closeButton, pressed && styles.buttonPressed]}
            testID="camera-scanner-close"
          >
            <Text style={styles.closeButtonLabel}>{t("action.cancel")}</Text>
          </PosPressable>
        </View>
      </View>
    </Modal>
  );
}

function contextLabel(
  context: ScannerCaptureContext,
  t: CameraScannerTranslate,
): string {
  return t(cameraScannerContextCopyKey(context));
}

const styles = StyleSheet.create({
  backdrop: {
    alignItems: "center",
    backgroundColor: "rgba(16, 37, 58, 0.78)",
    flex: 1,
    justifyContent: "center",
    padding: 24,
  },
  backdropDismissArea: {
    ...StyleSheet.absoluteFillObject,
  },
  buttonPressed: { opacity: 0.8 },
  closeButton: {
    alignItems: "center",
    borderColor: posColors.border,
    borderRadius: 2,
    borderWidth: 1,
    justifyContent: "center",
    minHeight: CAMERA_SCANNER_MIN_TOUCH_TARGET,
    paddingHorizontal: 18,
  },
  closeButtonLabel: { color: posColors.ink, fontSize: 16, fontWeight: "700" },
  description: { color: posColors.mutedInk, fontSize: 15, lineHeight: 22, marginTop: 8 },
  eyebrow: { color: posColors.orange, fontSize: 12, fontWeight: "800", letterSpacing: 1.2 },
  panel: {
    backgroundColor: posColors.surface,
    borderRadius: 2,
    maxWidth: 880,
    padding: 28,
    shadowColor: "#000000",
    shadowOffset: { height: 8, width: 0 },
    shadowOpacity: 0.24,
    shadowRadius: 20,
    width: "100%",
  },
  preview: { flex: 1 },
  previewFrame: {
    backgroundColor: "#0A1723",
    height: 340,
    marginVertical: 22,
    overflow: "hidden",
    position: "relative",
  },
  stateSpacing: { marginVertical: 22 },
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
  title: { color: posColors.ink, fontSize: 26, fontWeight: "800", marginTop: 6 },
});
