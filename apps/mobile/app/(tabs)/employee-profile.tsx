import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Image, ScrollView, StyleSheet, View } from "react-native";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CameraView, useCameraPermissions } from "expo-camera";
import { useRouter } from "expo-router";
import {
  ActivityIndicator,
  Avatar,
  Button,
  Chip,
  HelperText,
  Modal,
  Portal,
  SegmentedButtons,
  Snackbar,
  Surface,
  Text,
  TextInput,
} from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import { EmptyState } from "@/components/ui/EmptyState";
import {
  getMyEmployeeProfileApi,
  updateMyEmployeeProfileApi,
} from "@/modules/employee-profile/api";
import {
  EmployeeProfileImageUploadError,
  uploadEmployeeProfileImage,
} from "@/modules/employee-profile/image-upload";
import {
  GENDERS,
  type EmployeeProfile,
  type EmployeeProfileImageKind,
  type UpdateEmployeeProfilePayload,
} from "@/modules/employee-profile/types";
import { resolveLocalizedErrorMessage } from "@/shared/i18n/error-message";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { resolveLocaleTag } from "@/shared/i18n/types";
import { useAuthStore } from "@/store/auth-store";

type ImageFieldKey = "avatarUrl" | "identityPhotoUrl";

const EMPTY_FORM: UpdateEmployeeProfilePayload = {
  bankBsb: "",
  bankAccountNumber: "",
  superannuationCompanyName: "",
  superannuationCompanyCode: "",
  superannuationAccountNumber: "",
  birthday: "",
  gender: "",
  employmentType: "",
  avatarUrl: "",
  identityId: "",
  identityPhotoUrl: "",
  address: "",
};

const IMAGE_FIELD_CONFIG: Record<
  EmployeeProfileImageKind,
  {
    fieldKey: ImageFieldKey;
    labelKey: "fields.avatarUrl" | "fields.identityPhotoUrl";
  }
> = {
  avatar: {
    fieldKey: "avatarUrl",
    labelKey: "fields.avatarUrl",
  },
  identityPhoto: {
    fieldKey: "identityPhotoUrl",
    labelKey: "fields.identityPhotoUrl",
  },
};

function toFormValues(profile: EmployeeProfile): UpdateEmployeeProfilePayload {
  return {
    bankBsb: profile.bankBsb,
    bankAccountNumber: profile.bankAccountNumber,
    superannuationCompanyName: profile.superannuationCompanyName,
    superannuationCompanyCode: profile.superannuationCompanyCode,
    superannuationAccountNumber: profile.superannuationAccountNumber,
    birthday: profile.birthday,
    gender: profile.gender,
    employmentType: profile.employmentType,
    avatarUrl: profile.avatarUrl,
    identityId: profile.identityId,
    identityPhotoUrl: profile.identityPhotoUrl,
    address: profile.address,
  };
}

function formatDateTime(value: string | undefined, locale: string) {
  if (!value) {
    return null;
  }

  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) {
    return value;
  }

  return new Intl.DateTimeFormat(locale, {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(parsed);
}

function getInitials(value: string) {
  const words = value.trim().split(/\s+/).filter(Boolean);
  if (!words.length) {
    return "?";
  }
  if (words.length === 1) {
    return words[0].slice(0, 2).toUpperCase();
  }
  return `${words[0][0]}${words[1][0]}`.toUpperCase();
}

function buildPhotoFileName(kind: EmployeeProfileImageKind, uri: string) {
  const rawName = uri.split("/").pop()?.split("?")[0] ?? `${kind}.jpg`;
  const hasExtension = /\.[a-z0-9]+$/i.test(rawName);
  return hasExtension ? rawName : `${rawName}.jpg`;
}

function ProfileImageField({
  label,
  uri,
  emptyText,
  takePhotoLabel,
  retakePhotoLabel,
  removeLabel,
  onTakePhoto,
  onRemove,
  disabled,
}: {
  label: string;
  uri: string;
  emptyText: string;
  takePhotoLabel: string;
  retakePhotoLabel: string;
  removeLabel: string;
  onTakePhoto: () => void;
  onRemove: () => void;
  disabled?: boolean;
}) {
  return (
    <View style={styles.imageField}>
      <Text variant="labelLarge">{label}</Text>
      {uri ? (
        <Image source={{ uri }} style={styles.previewImage} resizeMode="cover" />
      ) : (
        <View style={[styles.previewImage, styles.previewPlaceholder]}>
          <Text variant="bodySmall" style={styles.metaText}>
            {emptyText}
          </Text>
        </View>
      )}
      <View style={styles.imageActionRow}>
        <Button
          mode="outlined"
          icon="camera"
          onPress={onTakePhoto}
          disabled={disabled}
          style={styles.imageActionButton}
        >
          {uri ? retakePhotoLabel : takePhotoLabel}
        </Button>
        <Button
          mode="text"
          icon="trash-can-outline"
          onPress={onRemove}
          disabled={disabled || !uri}
          compact
        >
          {removeLabel}
        </Button>
      </View>
    </View>
  );
}

export default function EmployeeProfileScreen() {
  const router = useRouter();
  const queryClient = useQueryClient();
  const cameraRef = useRef<CameraView | null>(null);
  const { t, language } = useAppTranslation(["employeeProfile", "common"]);
  const user = useAuthStore((state) => state.user);
  const isAuthenticated = useAuthStore((state) => state.isAuthenticated);
  const [cameraPermission, requestCameraPermission] = useCameraPermissions();
  const [formValues, setFormValues] = useState<UpdateEmployeeProfilePayload>(EMPTY_FORM);
  const [snackbarMessage, setSnackbarMessage] = useState("");
  const [snackbarVisible, setSnackbarVisible] = useState(false);
  const [cameraTarget, setCameraTarget] = useState<EmployeeProfileImageKind | null>(null);
  const [uploadingField, setUploadingField] = useState<ImageFieldKey | null>(null);
  const getErrorMessage = useCallback((error: unknown, fallbackKey: string) => (
    resolveLocalizedErrorMessage(error, {
      language,
      t,
      fallbackKey,
    })
  ), [language, t]);

  const locale = useMemo(() => resolveLocaleTag(language), [language]);

  const handleBack = useCallback(() => {
    if (router.canGoBack()) {
      router.back();
      return;
    }

    router.navigate("/(tabs)/settings");
  }, [router]);

  const showMessage = useCallback((message: string) => {
    setSnackbarMessage(message);
    setSnackbarVisible(true);
  }, []);

  useEffect(() => {
    if (isAuthenticated && user) {
      return;
    }

    showMessage(t("messages.loginRequired"));
    router.navigate("/(tabs)/settings");
  }, [isAuthenticated, router, showMessage, t, user]);

  const profileQuery = useQuery({
    queryKey: ["employee-profile", "me"],
    queryFn: getMyEmployeeProfileApi,
    enabled: Boolean(isAuthenticated && user),
  });

  useEffect(() => {
    if (!profileQuery.data) {
      return;
    }

    setFormValues(toFormValues(profileQuery.data));
  }, [profileQuery.data]);

  const saveMutation = useMutation({
    mutationFn: updateMyEmployeeProfileApi,
    onSuccess: (profile) => {
      queryClient.setQueryData(["employee-profile", "me"], profile);
      setFormValues(toFormValues(profile));
      showMessage(t("messages.saveSuccess"));
    },
    onError: (error) => {
      showMessage(getErrorMessage(error, "messages.saveFailed"));
    },
  });

  const readonlyUsername = profileQuery.data?.username || user?.username || "";
  const readonlyDisplayName =
    profileQuery.data?.displayName || user?.fullName || t("common:na");
  const createdAtText = formatDateTime(profileQuery.data?.createdAt, locale) || t("common:na");
  const updatedAtText = formatDateTime(profileQuery.data?.updatedAt, locale) || t("common:na");

  const setFieldValue = useCallback(
    <K extends keyof UpdateEmployeeProfilePayload>(
      key: K,
      value: UpdateEmployeeProfilePayload[K]
    ) => {
      setFormValues((current) => ({ ...current, [key]: value }));
    },
    []
  );

  const handleSave = async () => {
    try {
      await saveMutation.mutateAsync({
        ...formValues,
        birthday: formValues.birthday.trim(),
        address: formValues.address.trim(),
        avatarUrl: formValues.avatarUrl.trim(),
        bankAccountNumber: formValues.bankAccountNumber.trim(),
        bankBsb: formValues.bankBsb.trim(),
        gender: formValues.gender.trim(),
        employmentType: formValues.employmentType.trim(),
        identityId: formValues.identityId.trim(),
        identityPhotoUrl: formValues.identityPhotoUrl.trim(),
        superannuationAccountNumber: formValues.superannuationAccountNumber.trim(),
        superannuationCompanyCode: formValues.superannuationCompanyCode.trim(),
        superannuationCompanyName: formValues.superannuationCompanyName.trim(),
      });
    } catch {
      // handled in mutation callbacks
    }
  };

  const handleRemoveImage = useCallback(
    (fieldKey: ImageFieldKey) => {
      setFieldValue(fieldKey, "");
      showMessage(t("messages.imageRemoved"));
    },
    [setFieldValue, showMessage, t]
  );

  const openCameraForImage = useCallback(
    (kind: EmployeeProfileImageKind) => {
      setCameraTarget(kind);
      if (cameraPermission && !cameraPermission.granted && !cameraPermission.canAskAgain) {
        showMessage(t("messages.permissionRequired"));
      }
    },
    [cameraPermission, showMessage, t]
  );

  const handleCapturePhoto = useCallback(async () => {
    if (!cameraTarget || !cameraRef.current) {
      return;
    }

    const { fieldKey } = IMAGE_FIELD_CONFIG[cameraTarget];
    setUploadingField(fieldKey);

    try {
      const picture = await cameraRef.current.takePictureAsync({ quality: 0.7 });
      if (!picture?.uri) {
        throw new Error(t("messages.uploadFailed"));
      }

      const uploadResult = await uploadEmployeeProfileImage({
        kind: cameraTarget,
        uri: picture.uri,
        fileName: buildPhotoFileName(cameraTarget, picture.uri),
        contentType: "image/jpeg",
      });

      setFieldValue(fieldKey, uploadResult.downloadUrl);
      setCameraTarget(null);
      showMessage(t("messages.uploadSuccess"));
    } catch (error) {
      if (
        error instanceof EmployeeProfileImageUploadError &&
        error.code === "signature_unavailable"
      ) {
        showMessage(t("messages.uploadNotAvailable"));
      } else {
        showMessage(getErrorMessage(error, "messages.uploadFailed"));
      }
    } finally {
      setUploadingField(null);
    }
  }, [cameraTarget, setFieldValue, showMessage, t]);

  if (!isAuthenticated || !user) {
    return (
      <SafeAreaView style={styles.container}>
        <View style={styles.centered}>
          <ActivityIndicator size="large" />
        </View>
        <Snackbar visible={snackbarVisible} onDismiss={() => setSnackbarVisible(false)}>
          {snackbarMessage}
        </Snackbar>
      </SafeAreaView>
    );
  }

  if (profileQuery.isLoading && !profileQuery.data) {
    return (
      <SafeAreaView style={styles.container}>
        <View style={styles.centered}>
          <ActivityIndicator size="large" />
        </View>
      </SafeAreaView>
    );
  }

  if (profileQuery.isError && !profileQuery.data) {
    return (
      <SafeAreaView style={styles.container} edges={["top", "left", "right"]}>
        <View style={styles.centered}>
          <EmptyState
            title={t("messages.loadFailed")}
            description={
              getErrorMessage(profileQuery.error, "messages.loadFailed")
            }
            primaryAction={{
              label: t("common:actions.retry"),
              icon: "refresh",
              onPress: () => void profileQuery.refetch(),
            }}
            secondaryAction={{
              label: t("common:actions.back"),
              icon: "arrow-left",
              onPress: handleBack,
            }}
          />
        </View>
      </SafeAreaView>
    );
  }

  return (
    <SafeAreaView style={styles.container} edges={["top", "left", "right"]}>
      <ScrollView contentContainerStyle={styles.content}>
        <Surface style={styles.heroCard} elevation={1}>
          {formValues.avatarUrl ? (
            <Avatar.Image size={68} source={{ uri: formValues.avatarUrl }} style={styles.heroAvatar} />
          ) : (
            <Avatar.Text
              size={68}
              label={getInitials(readonlyDisplayName || readonlyUsername)}
              style={styles.heroAvatar}
            />
          )}
          <View style={styles.heroCopy}>
            <Text variant="headlineSmall">{readonlyDisplayName}</Text>
            <Text variant="bodyMedium" style={styles.subtitle}>
              {readonlyUsername || t("common:na")}
            </Text>
            <Chip compact style={styles.profileChip}>
              {formValues.employmentType
                ? t(`employmentTypeOptions.${formValues.employmentType}`, formValues.employmentType)
                : t("subtitle")}
            </Chip>
          </View>
        </Surface>

        <Surface style={styles.card} elevation={1}>
          <Text variant="titleMedium">{t("sections.personal")}</Text>
          <View style={styles.readonlyGrid}>
            <View style={styles.readonlyItem}>
              <Text variant="labelMedium" style={styles.metaText}>
                {t("readonly.username")}
              </Text>
              <Text variant="bodyLarge">{readonlyUsername || t("common:na")}</Text>
            </View>
            <View style={styles.readonlyItem}>
              <Text variant="labelMedium" style={styles.metaText}>
                {t("readonly.displayName")}
              </Text>
              <Text variant="bodyLarge">{readonlyDisplayName}</Text>
            </View>
            <View style={styles.readonlyItem}>
              <Text variant="labelMedium" style={styles.metaText}>
                {t("readonly.createdAt")}
              </Text>
              <Text variant="bodyMedium">{createdAtText}</Text>
            </View>
            <View style={styles.readonlyItem}>
              <Text variant="labelMedium" style={styles.metaText}>
                {t("readonly.updatedAt")}
              </Text>
              <Text variant="bodyMedium">{updatedAtText}</Text>
            </View>
          </View>
        </Surface>

        <Surface style={styles.card} elevation={1}>
          <Text variant="titleMedium">{t("sections.images")}</Text>
          <ProfileImageField
            label={t(IMAGE_FIELD_CONFIG.avatar.labelKey)}
            uri={formValues.avatarUrl}
            emptyText={t("preview.empty")}
            takePhotoLabel={t("actions.takePhoto")}
            retakePhotoLabel={t("actions.retakePhoto")}
            removeLabel={t("actions.removeImage")}
            onTakePhoto={() => openCameraForImage("avatar")}
            onRemove={() => handleRemoveImage("avatarUrl")}
            disabled={saveMutation.isPending || uploadingField === "avatarUrl"}
          />
        </Surface>

        <Surface style={styles.card} elevation={1}>
          <Text variant="titleMedium">{t("sections.banking")}</Text>
          <TextInput
            mode="outlined"
            label={t("fields.bankBsb")}
            value={formValues.bankBsb}
            onChangeText={(value) => setFieldValue("bankBsb", value)}
          />
          <TextInput
            mode="outlined"
            label={t("fields.bankAccountNumber")}
            value={formValues.bankAccountNumber}
            onChangeText={(value) => setFieldValue("bankAccountNumber", value)}
          />
        </Surface>

        <Surface style={styles.card} elevation={1}>
          <Text variant="titleMedium">{t("sections.superannuation")}</Text>
          <TextInput
            mode="outlined"
            label={t("fields.superannuationCompanyName")}
            value={formValues.superannuationCompanyName}
            onChangeText={(value) => setFieldValue("superannuationCompanyName", value)}
          />
          <TextInput
            mode="outlined"
            label={t("fields.superannuationCompanyCode")}
            value={formValues.superannuationCompanyCode}
            onChangeText={(value) => setFieldValue("superannuationCompanyCode", value)}
          />
          <TextInput
            mode="outlined"
            label={t("fields.superannuationAccountNumber")}
            value={formValues.superannuationAccountNumber}
            onChangeText={(value) => setFieldValue("superannuationAccountNumber", value)}
          />
        </Surface>

        <Surface style={styles.card} elevation={1}>
          <Text variant="titleMedium">{t("sections.personal")}</Text>
          <TextInput
            mode="outlined"
            label={t("fields.birthday")}
            placeholder={t("placeholders.birthday")}
            value={formValues.birthday}
            onChangeText={(value) => setFieldValue("birthday", value)}
            autoCapitalize="none"
          />
          <View style={styles.segmentBlock}>
            <Text variant="labelLarge">{t("fields.gender")}</Text>
            <SegmentedButtons
              value={formValues.gender}
              onValueChange={(value) => setFieldValue("gender", value)}
              buttons={GENDERS.map((value) => ({
                value,
                label: t(`genderOptions.${value}`),
              }))}
            />
          </View>
          <TextInput
            mode="outlined"
            label={t("fields.address")}
            placeholder={t("placeholders.address")}
            value={formValues.address}
            onChangeText={(value) => setFieldValue("address", value)}
            multiline
            numberOfLines={4}
          />
        </Surface>

        <Surface style={styles.card} elevation={1}>
          <Text variant="titleMedium">{t("sections.identity")}</Text>
          <TextInput
            mode="outlined"
            label={t("fields.identityId")}
            value={formValues.identityId}
            onChangeText={(value) => setFieldValue("identityId", value)}
          />
          <ProfileImageField
            label={t(IMAGE_FIELD_CONFIG.identityPhoto.labelKey)}
            uri={formValues.identityPhotoUrl}
            emptyText={t("preview.empty")}
            takePhotoLabel={t("actions.takePhoto")}
            retakePhotoLabel={t("actions.retakePhoto")}
            removeLabel={t("actions.removeImage")}
            onTakePhoto={() => openCameraForImage("identityPhoto")}
            onRemove={() => handleRemoveImage("identityPhotoUrl")}
            disabled={saveMutation.isPending || uploadingField === "identityPhotoUrl"}
          />
        </Surface>

        <HelperText type="error" visible={profileQuery.isError && Boolean(profileQuery.data)}>
          {getErrorMessage(profileQuery.error, "messages.loadFailed")}
        </HelperText>

        <Button
          mode="contained"
          onPress={() => void handleSave()}
          loading={saveMutation.isPending}
          disabled={saveMutation.isPending || Boolean(uploadingField)}
          style={styles.saveButton}
        >
          {t("actions.save")}
        </Button>
      </ScrollView>

      <Portal>
        <Modal
          visible={cameraTarget !== null}
          onDismiss={() => {
            if (!uploadingField) {
              setCameraTarget(null);
            }
          }}
          contentContainerStyle={styles.cameraModal}
        >
          <Text variant="titleMedium" style={styles.modalTitle}>
            {t("camera.title")}
          </Text>
          <Text variant="bodySmall" style={styles.modalSubtitle}>
            {t("messages.cameraReady")}
          </Text>
          {cameraPermission?.granted ? (
            <>
              <CameraView ref={cameraRef} style={styles.cameraView} facing="back" />
              <Button
                mode="contained"
                icon="camera"
                onPress={() => void handleCapturePhoto()}
                loading={Boolean(uploadingField)}
                disabled={Boolean(uploadingField)}
              >
                {t("actions.capture")}
              </Button>
            </>
          ) : (
            <View style={styles.permissionBlock}>
              <Text variant="titleMedium">{t("camera.permissionTitle")}</Text>
              <Text variant="bodySmall" style={styles.permissionText}>
                {t("camera.permissionDescription")}
              </Text>
              <Button mode="contained" onPress={() => void requestCameraPermission()}>
                {t("actions.grantCameraPermission")}
              </Button>
            </View>
          )}
        </Modal>
      </Portal>

      <Snackbar visible={snackbarVisible} onDismiss={() => setSnackbarVisible(false)}>
        {snackbarMessage}
      </Snackbar>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: "#F5F7FA",
  },
  centered: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
    gap: 12,
    paddingHorizontal: 24,
  },
  content: {
    paddingHorizontal: 14,
    paddingTop: 4,
    paddingBottom: 20,
    gap: 14,
  },
  subtitle: {
    textAlign: "center",
    color: "#667085",
  },
  card: {
    padding: 18,
    borderRadius: 18,
    gap: 12,
  },
  heroAvatar: {
    backgroundColor: "#111827",
  },
  heroCard: {
    alignItems: "center",
    borderRadius: 18,
    flexDirection: "row",
    gap: 16,
    padding: 18,
  },
  heroCopy: {
    flex: 1,
    gap: 6,
  },
  profileChip: {
    alignSelf: "flex-start",
    backgroundColor: "#E8F5E9",
  },
  readonlyGrid: {
    gap: 12,
  },
  readonlyItem: {
    gap: 4,
  },
  metaText: {
    color: "#667085",
  },
  segmentBlock: {
    gap: 8,
  },
  imageField: {
    gap: 10,
  },
  imageActionRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    gap: 12,
  },
  imageActionButton: {
    flex: 1,
  },
  previewImage: {
    width: "100%",
    height: 180,
    borderRadius: 14,
    backgroundColor: "#E9EDF3",
  },
  previewPlaceholder: {
    alignItems: "center",
    justifyContent: "center",
    paddingHorizontal: 16,
  },
  saveButton: {
    marginTop: 4,
    marginBottom: 10,
  },
  cameraModal: {
    marginHorizontal: 16,
    borderRadius: 18,
    backgroundColor: "#FFFFFF",
    padding: 16,
    gap: 12,
  },
  modalTitle: {
    textAlign: "center",
  },
  modalSubtitle: {
    textAlign: "center",
    color: "#667085",
  },
  cameraView: {
    height: 360,
    borderRadius: 14,
    overflow: "hidden",
  },
  permissionBlock: {
    alignItems: "center",
    justifyContent: "center",
    gap: 12,
    paddingVertical: 24,
    paddingHorizontal: 8,
  },
  permissionText: {
    textAlign: "center",
    color: "#667085",
  },
});
