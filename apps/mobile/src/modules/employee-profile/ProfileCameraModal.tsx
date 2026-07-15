import { useEffect, useRef, useState } from "react";
import { Linking, StyleSheet, View } from "react-native";
import { CameraView, useCameraPermissions, type CameraType } from "expo-camera";
import { Button, Modal, Portal, Text } from "react-native-paper";

import type { EmployeeProfileImageKind } from "./types";
import {
  captureProfilePhoto,
  createProfileCameraCaptureLock,
} from "./profile-camera-capture";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

type CapturedImage = { uri: string; width: number; height: number };

export function ProfileCameraModal({
  visible,
  kind,
  busy,
  onDismiss,
  onCaptured,
  onError,
}: {
  visible: boolean;
  kind: EmployeeProfileImageKind;
  busy: boolean;
  onDismiss: () => void;
  onCaptured: (image: CapturedImage) => Promise<void>;
  onError: (error: unknown) => void;
}) {
  const { t } = useAppTranslation(["employeeProfile", "common"]);
  const cameraRef = useRef<CameraView | null>(null);
  const captureLockRef = useRef(createProfileCameraCaptureLock());
  const [capturePending, setCapturePending] = useState(false);
  const [permission, requestPermission] = useCameraPermissions();
  const [facing, setFacing] = useState<CameraType>(kind === "avatar" ? "front" : "back");

  useEffect(() => {
    if (visible) {
      setFacing(kind === "avatar" ? "front" : "back");
    }
  }, [kind, visible]);

  const handleCapture = async () => {
    await captureProfilePhoto({
      captureLock: captureLockRef.current,
      onPendingChange: setCapturePending,
      takePicture: async () => cameraRef.current?.takePictureAsync({ quality: 1 }),
      onCaptured,
      onError,
    });
  };

  const interactionBusy = busy || capturePending;

  return (
    <Portal>
      <Modal visible={visible} onDismiss={interactionBusy ? undefined : onDismiss} dismissable={!interactionBusy} contentContainerStyle={styles.modal}>
        <View style={styles.header}>
          <Text variant="titleMedium">{t("camera.title")}</Text>
          <Button
            mode="text"
            icon="close"
            compact
            accessibilityLabel={t("common:actions.close")}
            onPress={onDismiss}
            disabled={interactionBusy}
          >
            {t("common:actions.close")}
          </Button>
        </View>
        {permission?.granted ? (
          <>
            <CameraView ref={cameraRef} style={styles.camera} facing={facing} />
            <View style={styles.actions}>
              <Button
                mode="outlined"
                icon="camera-flip-outline"
                onPress={() => setFacing((current) => (current === "front" ? "back" : "front"))}
                disabled={interactionBusy}
              >
                {t("actions.switchCamera")}
              </Button>
              <Button mode="contained" icon="camera" onPress={() => void handleCapture()} loading={interactionBusy} disabled={interactionBusy}>
                {t("actions.capture")}
              </Button>
            </View>
          </>
        ) : (
          <View style={styles.permissionBlock}>
            <Text variant="titleSmall">{t("camera.permissionTitle")}</Text>
            <Text variant="bodySmall" style={styles.muted}>{t("camera.permissionDescription")}</Text>
            {permission && !permission.canAskAgain ? (
              <Button mode="contained" icon="cog" onPress={() => void Linking.openSettings()}>
                {t("actions.openSettings")}
              </Button>
            ) : (
              <Button mode="contained" onPress={() => void requestPermission()}>
                {t("actions.grantCameraPermission")}
              </Button>
            )}
          </View>
        )}
      </Modal>
    </Portal>
  );
}

const styles = StyleSheet.create({
  modal: { marginHorizontal: 16, borderRadius: 18, backgroundColor: "#FFFFFF", padding: 16, gap: 12 },
  header: { flexDirection: "row", alignItems: "center", justifyContent: "space-between", gap: 12 },
  camera: { height: 360, borderRadius: 14, overflow: "hidden" },
  actions: { flexDirection: "row", justifyContent: "space-between", gap: 10 },
  permissionBlock: { alignItems: "center", gap: 12, paddingHorizontal: 8, paddingVertical: 24 },
  muted: { color: "#667085", textAlign: "center" },
});
