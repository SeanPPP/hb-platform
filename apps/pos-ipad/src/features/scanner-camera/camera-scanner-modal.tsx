import {
  CameraView,
  useCameraPermissions,
  type BarcodeScanningResult,
} from "expo-camera";
import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  ActivityIndicator,
  Modal,
  Pressable,
  StyleSheet,
  Text,
  View,
} from "react-native";

import {
  cameraScannerContextCopyKey,
  cameraScannerText,
  resolveCameraScannerLocale,
  type CameraScannerCopyKey,
} from "./camera-scanner-copy";

import {
  normalizeScanValue,
  type ScannerCaptureContext,
} from "@/core/peripherals/scanner";
import { posColors } from "@/ui/theme";

export const CAMERA_SCANNER_MIN_TOUCH_TARGET = 44;

/** 相机功能只依赖扫描公共边界，禁止把会话或授权资料传入 UI。 */
export type CameraScannerPort = Pick<
  import("@/core/peripherals/scanner").HidScannerRouter,
  "acceptCameraText" | "startCamera" | "stopCamera"
>;

export type CameraScannerModalProps = Readonly<{
  visible: boolean;
  context: ScannerCaptureContext;
  scanner: CameraScannerPort;
  onScan(value: string): void;
  onClose(): void;
}>;

type NativeAvailability = "checking" | "ready" | "unavailable";

type CameraScannerTranslate = (key: CameraScannerCopyKey) => string;

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
  const [permission, requestPermission] = useCameraPermissions();
  const [availability, setAvailability] = useState<NativeAvailability>("checking");
  const [cameraStarted, setCameraStarted] = useState(false);
  const deliveredRef = useRef(false);
  const closingRef = useRef(false);
  const cameraStartedRef = useRef(false);

  const stopCamera = useCallback(() => {
    if (!cameraStartedRef.current) return;
    cameraStartedRef.current = false;
    setCameraStarted(false);
    void scanner.stopCamera();
  }, [scanner]);

  const close = useCallback(() => {
    if (closingRef.current) return;
    closingRef.current = true;
    stopCamera();
    onClose();
  }, [onClose, stopCamera]);

  useEffect(() => {
    if (!visible) {
      stopCamera();
      return;
    }
    deliveredRef.current = false;
    closingRef.current = false;
  }, [stopCamera, visible]);

  useEffect(() => {
    if (!visible || !permission?.granted) {
      setAvailability("checking");
      return;
    }

    let cancelled = false;
    const checkNativeCamera = CameraView.isAvailableAsync;
    if (typeof checkNativeCamera !== "function") {
      setAvailability("unavailable");
      return;
    }
    void checkNativeCamera()
      .then((isAvailable) => {
        if (!cancelled) {
          setAvailability(isAvailable ? "ready" : "unavailable");
        }
      })
      .catch(() => {
        // Development Build 缺少原生模块或模拟器无相机时必须保持关闭状态。
        if (!cancelled) setAvailability("unavailable");
      });
    return () => {
      cancelled = true;
    };
  }, [permission?.granted, visible]);

  useEffect(() => {
    if (!visible || !permission?.granted || availability !== "ready") {
      stopCamera();
      return;
    }

    let cancelled = false;
    void scanner
      .startCamera()
      .then(() => {
        if (cancelled || closingRef.current) {
          void scanner.stopCamera();
          return;
        }
        cameraStartedRef.current = true;
        setCameraStarted(true);
      })
      .catch(() => {
        if (!cancelled) setAvailability("unavailable");
      });
    return () => {
      cancelled = true;
      stopCamera();
    };
  }, [availability, permission?.granted, scanner, stopCamera, visible]);

  const handleBarcodeScanned = useCallback(
    (result: BarcodeScanningResult) => {
      if (!cameraStartedRef.current || deliveredRef.current || closingRef.current) {
        return;
      }
      const value = normalizeScanValue(result.data ?? "");
      if (!value || !scanner.acceptCameraText(value)) {
        return;
      }
      deliveredRef.current = true;
      onScan(value);
      close();
    },
    [close, onScan, scanner],
  );

  const requestCameraPermission = useCallback(() => {
    void requestPermission();
  }, [requestPermission]);

  const permissionDenied = permission?.status === "denied";
  const showCamera = Boolean(
    visible && permission?.granted && availability === "ready" && cameraStarted,
  );

  return (
    <Modal
      animationType="fade"
      onRequestClose={close}
      presentationStyle="overFullScreen"
      statusBarTranslucent
      transparent
      visible={visible}
    >
      <View accessibilityViewIsModal style={styles.backdrop} testID="camera-scanner-modal">
        <View style={styles.panel}>
          <Text style={styles.eyebrow}>{t("header.eyebrow")}</Text>
          <Text style={styles.title}>{contextLabel(context, t)}</Text>
          <Text style={styles.description}>{t("description")}</Text>

          {showCamera ? (
            <View style={styles.previewFrame}>
              <CameraView
                active
                barcodeScannerSettings={{
                  barcodeTypes: ["aztec", "code128", "code39", "code93", "ean13", "ean8", "itf14", "pdf417", "qr", "upc_a", "upc_e"],
                }}
                facing="back"
                onBarcodeScanned={handleBarcodeScanned}
                onMountError={() => {
                  // 原生 view 启动失败时不允许回调继续被当作有效扫码。
                  stopCamera();
                  setAvailability("unavailable");
                }}
                style={styles.preview}
              />
              <View pointerEvents="none" style={styles.target} />
            </View>
          ) : (
            <CameraState
              availability={availability}
              permissionGranted={Boolean(permission?.granted)}
              permissionDenied={permissionDenied}
              onRequestPermission={requestCameraPermission}
              t={t}
            />
          )}

          <Pressable
            accessibilityLabel={t("action.closeLabel")}
            accessibilityRole="button"
            onPress={close}
            style={({ pressed }) => [styles.closeButton, pressed && styles.buttonPressed]}
            testID="camera-scanner-close"
          >
            <Text style={styles.closeButtonLabel}>{t("action.cancel")}</Text>
          </Pressable>
        </View>
      </View>
    </Modal>
  );
}

function CameraState({
  availability,
  onRequestPermission,
  permissionGranted,
  permissionDenied,
  t,
}: Readonly<{
  availability: NativeAvailability;
  permissionDenied: boolean;
  permissionGranted: boolean;
  onRequestPermission(): void;
  t: CameraScannerTranslate;
}>) {
  if (permissionDenied) {
    return (
      <View accessibilityRole="alert" style={styles.state} testID="camera-scanner-permission-denied">
        <Text style={styles.stateTitle}>{t("permission.denied.title")}</Text>
        <Text style={styles.stateCopy}>{t("permission.denied.body")}</Text>
      </View>
    );
  }

  if (availability === "unavailable") {
    return (
      <View accessibilityRole="alert" style={styles.state} testID="camera-scanner-unavailable">
        <Text style={styles.stateTitle}>{t("unavailable.title")}</Text>
        <Text style={styles.stateCopy}>{t("unavailable.body")}</Text>
      </View>
    );
  }

  if (!permissionGranted) {
    return (
      <View style={styles.state} testID="camera-scanner-permission-request-state">
        <Text style={styles.stateTitle}>{t("permission.required.title")}</Text>
        <Text style={styles.stateCopy}>{t("permission.required.body")}</Text>
        <Pressable
          accessibilityRole="button"
          onPress={onRequestPermission}
          style={({ pressed }) => [styles.permissionButton, pressed && styles.buttonPressed]}
          testID="camera-scanner-request-permission"
        >
          <Text style={styles.permissionButtonLabel}>{t("action.allowCamera")}</Text>
        </Pressable>
      </View>
    );
  }

  if (availability === "checking") {
    return (
      <View style={styles.state} testID="camera-scanner-starting">
        <ActivityIndicator color={posColors.orange} />
        <Text style={styles.stateTitle}>{t("checking")}</Text>
      </View>
    );
  }

  return (
    <View style={styles.state} testID="camera-scanner-starting">
      <ActivityIndicator color={posColors.orange} />
      <Text style={styles.stateTitle}>{t("starting")}</Text>
    </View>
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
  permissionButton: {
    alignItems: "center",
    alignSelf: "flex-start",
    backgroundColor: posColors.orange,
    borderRadius: 2,
    justifyContent: "center",
    marginTop: 18,
    minHeight: CAMERA_SCANNER_MIN_TOUCH_TARGET,
    paddingHorizontal: 18,
  },
  permissionButtonLabel: { color: "#FFFFFF", fontSize: 16, fontWeight: "800" },
  preview: { flex: 1 },
  previewFrame: {
    backgroundColor: "#0A1723",
    height: 340,
    marginVertical: 22,
    overflow: "hidden",
    position: "relative",
  },
  state: {
    alignItems: "flex-start",
    backgroundColor: posColors.canvas,
    justifyContent: "center",
    marginVertical: 22,
    minHeight: 200,
    padding: 22,
  },
  stateCopy: { color: posColors.mutedInk, fontSize: 15, lineHeight: 22, marginTop: 8 },
  stateTitle: { color: posColors.ink, fontSize: 18, fontWeight: "800" },
  target: {
    borderColor: "#FFFFFF",
    borderRadius: 2,
    borderWidth: 2,
    bottom: "18%",
    left: "14%",
    position: "absolute",
    right: "14%",
    top: "18%",
  },
  title: { color: posColors.ink, fontSize: 26, fontWeight: "800", marginTop: 6 },
});
