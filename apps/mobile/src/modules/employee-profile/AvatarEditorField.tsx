import { useState } from "react";
import { Image, Linking, StyleSheet, View } from "react-native";
import * as ImagePicker from "expo-image-picker";
import { Button, HelperText, Text } from "react-native-paper";

import { ProfileCameraModal } from "./ProfileCameraModal";
import {
  EmployeeProfileImageProcessingError,
  processEmployeeProfileImage,
} from "./image-processing";
import type { EmployeeProfileImageKind } from "./types";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

type SourceImage = { uri: string; width: number; height: number };
type ProcessedImage = NonNullable<Awaited<ReturnType<typeof processEmployeeProfileImage>>>;

export function AvatarEditorField({
  kind,
  label,
  uri,
  disabled,
  onSave,
  onDelete,
  onImageLoadError,
}: {
  kind: EmployeeProfileImageKind;
  label: string;
  uri: string;
  disabled?: boolean;
  onSave: (image: ProcessedImage) => Promise<void>;
  onDelete: () => Promise<void>;
  onImageLoadError?: () => void;
}) {
  const { t } = useAppTranslation(["employeeProfile", "common"]);
  const [cameraVisible, setCameraVisible] = useState(false);
  const [busy, setBusy] = useState(false);
  const [previewUri, setPreviewUri] = useState<string | null>(null);
  const [error, setError] = useState("");

  const processAndSave = async (source: SourceImage) => {
    setBusy(true);
    setError("");
    try {
      const processed = await processEmployeeProfileImage({ kind, ...source });
      if (!processed) {
        return;
      }
      // 预览与上传使用同一处理结果，避免用户看到的内容和实际保存内容不一致。
      setPreviewUri(processed.uri);
      await onSave(processed);
      setPreviewUri(null);
      setCameraVisible(false);
    } catch (nextError) {
      setPreviewUri(null);
      if (nextError instanceof EmployeeProfileImageProcessingError) {
        setError(t(`imageErrors.${nextError.code}`));
      } else {
        setError(nextError instanceof Error ? nextError.message : t("messages.uploadFailed"));
      }
    } finally {
      setBusy(false);
    }
  };

  const chooseFromLibrary = async () => {
    const permission = await ImagePicker.requestMediaLibraryPermissionsAsync();
    if (!permission.granted) {
      if (!permission.canAskAgain) {
        setError(t("messages.libraryPermissionBlocked"));
      }
      return;
    }

    const result = await ImagePicker.launchImageLibraryAsync({
      mediaTypes: ["images"],
      allowsEditing: false,
      quality: 1,
    });
    if (result.canceled || !result.assets[0]) {
      return;
    }
    const asset = result.assets[0];
    await processAndSave({ uri: asset.uri, width: asset.width, height: asset.height });
  };

  const removeImage = async () => {
    setBusy(true);
    setError("");
    try {
      await onDelete();
    } catch (nextError) {
      setError(nextError instanceof Error ? nextError.message : t("messages.removeImageFailed"));
    } finally {
      setBusy(false);
    }
  };

  const shownUri = previewUri ?? uri;
  return (
    <View style={styles.field}>
      <Text variant="labelLarge">{label}</Text>
      {shownUri ? (
        <Image
          source={{ uri: shownUri }}
          style={styles.preview}
          resizeMode={kind === "avatar" ? "cover" : "contain"}
          onError={onImageLoadError}
        />
      ) : (
        <View style={[styles.preview, styles.placeholder]}>
          <Text variant="bodySmall" style={styles.muted}>{t("preview.empty")}</Text>
        </View>
      )}
      <View style={styles.actions}>
        <Button mode="outlined" icon="image" onPress={() => void chooseFromLibrary()} disabled={disabled || busy} compact>
          {t("actions.choosePhoto")}
        </Button>
        <Button mode="outlined" icon="camera" onPress={() => setCameraVisible(true)} disabled={disabled || busy} compact>
          {t("actions.takePhoto")}
        </Button>
        <Button mode="text" icon="trash-can-outline" onPress={() => void removeImage()} disabled={disabled || busy || !uri} compact>
          {t("actions.removeImage")}
        </Button>
      </View>
      {busy ? <HelperText type="info" visible>{t("messages.processingImage")}</HelperText> : null}
      <HelperText type="error" visible={Boolean(error)}>{error}</HelperText>
      {error === t("messages.libraryPermissionBlocked") ? (
        <Button mode="text" icon="cog" onPress={() => void Linking.openSettings()}>{t("actions.openSettings")}</Button>
      ) : null}
      <ProfileCameraModal
        visible={cameraVisible}
        kind={kind}
        busy={busy}
        onDismiss={() => setCameraVisible(false)}
        onCaptured={processAndSave}
        onError={() => setError(t("messages.cameraCaptureFailed"))}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  field: { gap: 10 },
  preview: { width: "100%", height: 180, borderRadius: 14, backgroundColor: "#E9EDF3" },
  placeholder: { alignItems: "center", justifyContent: "center", paddingHorizontal: 16 },
  muted: { color: "#667085" },
  actions: { flexDirection: "row", alignItems: "center", justifyContent: "space-between", gap: 6 },
});
