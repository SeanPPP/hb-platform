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
  Platform,
  ScrollView,
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
import { PosPressable } from "@/ui/controls/pos-pressable";
import { HandheldActionButton } from "@/ui/handheld/handheld-actions";
import { HandheldStateSurface } from "@/ui/handheld/handheld-design-states";
import { HandheldSection } from "@/ui/handheld/handheld-layout";
import { posColors } from "@/ui/theme";

export const CAMERA_SCANNER_MIN_TOUCH_TARGET = 48;

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

const cameraSessionOwnerByPort = new WeakMap<CameraScannerPort, symbol>();

/** 仅释放当前会话拥有的相机，避免旧异步启动关闭已重开的新会话。 */
function releaseOwnedCameraSession(
  scanner: CameraScannerPort,
  token: symbol,
): boolean {
  if (cameraSessionOwnerByPort.get(scanner) !== token) return false;
  cameraSessionOwnerByPort.delete(scanner);
  void scanner.stopCamera();
  return true;
}

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
  const [availability, setAvailability] =
    useState<NativeAvailability>("checking");
  const [cameraStarted, setCameraStarted] = useState(false);
  const deliveredRef = useRef(false);
  const closingRef = useRef(false);
  const cameraStartedRef = useRef(false);
  const cameraSessionTokenRef = useRef<symbol | null>(null);
  const permissionRequestTokenRef = useRef<symbol | null>(null);
  const sessionGenerationRef = useRef(0);

  const stopCamera = useCallback(() => {
    const token = cameraSessionTokenRef.current;
    cameraSessionTokenRef.current = null;
    if (cameraStartedRef.current) {
      cameraStartedRef.current = false;
      setCameraStarted(false);
    }
    if (token) releaseOwnedCameraSession(scanner, token);
  }, [scanner]);

  const deactivateSession = useCallback(() => {
    sessionGenerationRef.current += 1;
    permissionRequestTokenRef.current = null;
    stopCamera();
  }, [stopCamera]);

  const markCameraUnavailable = useCallback(() => {
    deactivateSession();
    setAvailability("unavailable");
  }, [deactivateSession]);

  const close = useCallback(() => {
    if (closingRef.current) return;
    closingRef.current = true;
    deactivateSession();
    onClose();
  }, [deactivateSession, onClose]);

  useEffect(() => {
    if (!visible) {
      setAvailability("checking");
      deactivateSession();
      return;
    }
    deliveredRef.current = false;
    closingRef.current = false;
  }, [deactivateSession, visible]);

  useEffect(() => {
    if (!visible || !permission?.granted) {
      setAvailability("checking");
      return;
    }

    // expo-camera 的 isAvailableAsync 只有 Web 实现；原生以 CameraView 挂载结果判断能力。
    if (Platform.OS !== "web") {
      setAvailability("ready");
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
    const generation = sessionGenerationRef.current;
    const token = Symbol("camera-scanner-session");
    cameraSessionOwnerByPort.set(scanner, token);
    cameraSessionTokenRef.current = token;

    const startCamera = async (): Promise<void> => {
      try {
        await scanner.startCamera();
        const ownsCamera = cameraSessionOwnerByPort.get(scanner) === token;
        if (
          cancelled ||
          closingRef.current ||
          generation !== sessionGenerationRef.current ||
          !ownsCamera
        ) {
          if (ownsCamera) {
            releaseOwnedCameraSession(scanner, token);
          } else if (!cameraSessionOwnerByPort.has(scanner)) {
            // 已关闭且没有新会话接管时，清理由旧启动迟到恢复的底层相机。
            void scanner.stopCamera();
          }
          return;
        }
        cameraStartedRef.current = true;
        setCameraStarted(true);
      } catch {
        const ownsCamera = cameraSessionOwnerByPort.get(scanner) === token;
        if (ownsCamera) releaseOwnedCameraSession(scanner, token);
        if (cameraSessionTokenRef.current === token) {
          cameraSessionTokenRef.current = null;
        }
        if (!cancelled && generation === sessionGenerationRef.current) {
          markCameraUnavailable();
        }
      }
    };
    void startCamera();
    return () => {
      cancelled = true;
      if (cameraSessionTokenRef.current === token) {
        cameraSessionTokenRef.current = null;
        if (cameraStartedRef.current) {
          cameraStartedRef.current = false;
          setCameraStarted(false);
        }
      }
      releaseOwnedCameraSession(scanner, token);
    };
  }, [
    availability,
    markCameraUnavailable,
    permission?.granted,
    scanner,
    stopCamera,
    visible,
  ]);

  const handleBarcodeScanned = useCallback(
    (result: BarcodeScanningResult) => {
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
      onScan(value);
      close();
    },
    [close, context, onScan, scanner],
  );

  const requestCameraPermission = useCallback(() => {
    // 同一弹窗只允许一个授权请求，旧请求完成后也不能覆盖新会话状态。
    if (permissionRequestTokenRef.current) return;
    const generation = sessionGenerationRef.current;
    const token = Symbol("camera-permission-request");
    permissionRequestTokenRef.current = token;
    const finishRequest = () => {
      if (permissionRequestTokenRef.current !== token) return false;
      permissionRequestTokenRef.current = null;
      return true;
    };
    const handleFailure = () => {
      if (finishRequest() && generation === sessionGenerationRef.current) {
        markCameraUnavailable();
      }
    };
    try {
      void Promise.resolve(requestPermission()).then(finishRequest, handleFailure);
    } catch {
      handleFailure();
    }
  }, [markCameraUnavailable, requestPermission]);

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
      supportedOrientations={["portrait"]}
      transparent
      visible={visible}
    >
      <View
        accessibilityViewIsModal
        style={styles.backdrop}
        testID="camera-scanner-modal"
      >
        <PosPressable
          accessible={false}
          onPress={close}
          sound="navigate"
          style={styles.modalDismissArea}
          testID="camera-scanner-backdrop"
        />
        <HandheldStateSurface slug="camera-scanner" style={styles.panel}>
          {/* 短屏只收缩并滚动内容区，关闭操作始终留在面板底部可达。 */}
          <ScrollView
            contentContainerStyle={styles.scrollContent}
            showsVerticalScrollIndicator={false}
            style={styles.scroll}
            testID="camera-scanner-scroll"
          >
            <Text style={styles.eyebrow}>{t("header.eyebrow")}</Text>
            <Text style={styles.title}>{contextLabel(context, t)}</Text>
            <Text style={styles.description}>{t("description")}</Text>

            <HandheldSection testID="camera-scanner-content">
              {showCamera ? (
                <View style={styles.previewFrame}>
                  <CameraView
                    active
                    barcodeScannerSettings={{
                      barcodeTypes: [
                        "aztec",
                        "code128",
                        "code39",
                        "code93",
                        "ean13",
                        "ean8",
                        "itf14",
                        "pdf417",
                        "qr",
                        "upc_a",
                        "upc_e",
                      ],
                    }}
                    facing="back"
                    onBarcodeScanned={handleBarcodeScanned}
                    onMountError={() => {
                      // 原生 view 启动失败时不允许回调继续被当作有效扫码。
                      markCameraUnavailable();
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
            </HandheldSection>
          </ScrollView>

          <HandheldActionButton
            accessibilityLabel={t("action.closeLabel")}
            label={t("action.cancel")}
            onPress={close}
            sound="navigate"
            testID="camera-scanner-close"
            variant="secondary"
          />
        </HandheldStateSurface>
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
      <View
        accessibilityRole="alert"
        style={styles.state}
        testID="camera-scanner-permission-denied"
      >
        <Text style={styles.stateTitle}>{t("permission.denied.title")}</Text>
        <Text style={styles.stateCopy}>{t("permission.denied.body")}</Text>
      </View>
    );
  }

  if (availability === "unavailable") {
    return (
      <View
        accessibilityRole="alert"
        style={styles.state}
        testID="camera-scanner-unavailable"
      >
        <Text style={styles.stateTitle}>{t("unavailable.title")}</Text>
        <Text style={styles.stateCopy}>{t("unavailable.body")}</Text>
      </View>
    );
  }

  if (!permissionGranted) {
    return (
      <View
        style={styles.state}
        testID="camera-scanner-permission-request-state"
      >
        <Text style={styles.stateTitle}>{t("permission.required.title")}</Text>
        <Text style={styles.stateCopy}>{t("permission.required.body")}</Text>
        <HandheldActionButton
          label={t("action.allowCamera")}
          onPress={onRequestPermission}
          testID="camera-scanner-request-permission"
        />
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
    padding: 16,
  },
  modalDismissArea: {
    bottom: 0,
    left: 0,
    position: "absolute",
    right: 0,
    top: 0,
  },
  description: {
    color: posColors.mutedInk,
    fontSize: 15,
    lineHeight: 22,
    marginTop: 8,
  },
  eyebrow: {
    color: posColors.orange,
    fontSize: 12,
    fontWeight: "800",
    letterSpacing: 1.2,
  },
  panel: {
    backgroundColor: posColors.surface,
    borderRadius: 6,
    gap: 16,
    maxHeight: "96%",
    maxWidth: 520,
    padding: 16,
    width: "100%",
  },
  preview: { flex: 1 },
  previewFrame: {
    backgroundColor: "#0A1723",
    height: 320,
    overflow: "hidden",
    position: "relative",
  },
  scroll: {
    flexShrink: 1,
    minHeight: 0,
  },
  scrollContent: {
    gap: 16,
  },
  state: {
    alignItems: "flex-start",
    backgroundColor: posColors.canvas,
    gap: 16,
    justifyContent: "center",
    minHeight: 180,
    padding: 16,
  },
  stateCopy: {
    color: posColors.mutedInk,
    fontSize: 15,
    lineHeight: 22,
    marginTop: 8,
  },
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
  title: {
    color: posColors.ink,
    fontSize: 26,
    fontWeight: "800",
    marginTop: 6,
  },
});
