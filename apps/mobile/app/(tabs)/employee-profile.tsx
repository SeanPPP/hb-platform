import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { AppState, ScrollView, StyleSheet, View } from "react-native";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useRouter } from "expo-router";
import {
  ActivityIndicator,
  Avatar,
  Button,
  Chip,
  HelperText,
  SegmentedButtons,
  Snackbar,
  Surface,
  Text,
  TextInput,
} from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import { EmptyState } from "@/components/ui/EmptyState";
import {
  deleteEmployeeProfileImageApi,
  getMyEmployeeProfileApi,
  updateMyEmployeeProfileApi,
} from "@/modules/employee-profile/api";
import { AvatarEditorField } from "@/modules/employee-profile/AvatarEditorField";
import { CashierBarcodeCard } from "@/modules/employee-profile/CashierBarcodeCard";
import {
  getEmployeeProfileQueryKey,
  resolveEmployeeProfileIdentity,
  shouldResetEmployeeProfileDraft,
} from "@/modules/employee-profile/cache-keys";
import {
  syncEmployeeProfileDraft,
  toEmployeeProfileDraft,
} from "@/modules/employee-profile/profile-draft";
import {
  EmployeeProfileImageUploadError,
  uploadEmployeeProfileImage,
} from "@/modules/employee-profile/image-upload";
import {
  GENDERS,
  type EmployeeProfile,
  type UpdateEmployeeProfilePayload,
} from "@/modules/employee-profile/types";
import { getIdentityPhotoRefetchDelay } from "@/modules/employee-profile/identity-photo-expiry";
import { resolveLocalizedErrorMessage } from "@/shared/i18n/error-message";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { resolveLocaleTag } from "@/shared/i18n/types";
import { useAuthStore } from "@/store/auth-store";

const EMPTY_FORM: UpdateEmployeeProfilePayload = {
  bankBsb: "",
  bankAccountNumber: "",
  superannuationCompanyName: "",
  superannuationCompanyCode: "",
  superannuationAccountNumber: "",
  birthday: "",
  gender: "",
  employmentType: "",
  identityId: "",
  address: "",
};

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

export default function EmployeeProfileScreen() {
  const router = useRouter();
  const queryClient = useQueryClient();
  const { t, language } = useAppTranslation(["employeeProfile", "common"]);
  const user = useAuthStore((state) => state.user);
  const isAuthenticated = useAuthStore((state) => state.isAuthenticated);
  const [storedFormValues, setFormValues] = useState<UpdateEmployeeProfilePayload>(EMPTY_FORM);
  const [snackbarMessage, setSnackbarMessage] = useState("");
  const [snackbarVisible, setSnackbarVisible] = useState(false);
  const [savingImageKind, setSavingImageKind] = useState<"avatar" | "identityPhoto" | null>(null);
  const formInitializedRef = useRef(false);
  const formIdentityRef = useRef("");
  const getErrorMessage = useCallback((error: unknown, fallbackKey: string) => (
    resolveLocalizedErrorMessage(error, {
      language,
      t,
      fallbackKey,
    })
  ), [language, t]);

  const locale = useMemo(() => resolveLocaleTag(language), [language]);
  const userIdentity = resolveEmployeeProfileIdentity(user);
  const formValues = formIdentityRef.current === userIdentity ? storedFormValues : EMPTY_FORM;
  const profileQueryKey = useMemo(
    () => getEmployeeProfileQueryKey(userIdentity),
    [userIdentity]
  );

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

  useEffect(() => {
    if (!shouldResetEmployeeProfileDraft(formIdentityRef.current, userIdentity)) {
      return;
    }
    // 账号变化时先清空旧草稿，随后由新账号自己的查询结果重新初始化。
    formIdentityRef.current = userIdentity;
    formInitializedRef.current = false;
    setFormValues(EMPTY_FORM);
  }, [userIdentity]);

  const profileQuery = useQuery({
    queryKey: profileQueryKey,
    queryFn: getMyEmployeeProfileApi,
    enabled: Boolean(isAuthenticated && user),
    refetchInterval: (query) => getIdentityPhotoRefetchDelay(
      (query.state.data as EmployeeProfile | undefined)?.identityPhotoUrlExpiresAt
    ),
  });

  useEffect(() => {
    const subscription = AppState.addEventListener("change", (state) => {
      if (state === "active" && isAuthenticated) {
        void queryClient.invalidateQueries({ queryKey: profileQueryKey });
      }
    });
    return () => subscription.remove();
  }, [isAuthenticated, profileQueryKey, queryClient]);

  useEffect(() => {
    if (!profileQuery.data) {
      return;
    }

    const wasInitialized = formInitializedRef.current;
    formInitializedRef.current = true;
    setFormValues((current) => syncEmployeeProfileDraft(current, profileQuery.data!, wasInitialized));
  }, [profileQuery.data]);

  const saveMutation = useMutation({
    mutationFn: updateMyEmployeeProfileApi,
    onSuccess: (profile) => {
      queryClient.setQueryData(profileQueryKey, profile);
      setFormValues(toEmployeeProfileDraft(profile));
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
        bankAccountNumber: formValues.bankAccountNumber.trim(),
        bankBsb: formValues.bankBsb.trim(),
        gender: formValues.gender.trim(),
        employmentType: formValues.employmentType.trim(),
        identityId: formValues.identityId.trim(),
        superannuationAccountNumber: formValues.superannuationAccountNumber.trim(),
        superannuationCompanyCode: formValues.superannuationCompanyCode.trim(),
        superannuationCompanyName: formValues.superannuationCompanyName.trim(),
      });
    } catch {
      // handled in mutation callbacks
    }
  };

  const handleSaveImage = async (
    kind: "avatar" | "identityPhoto",
    image: { uri: string; fileName: string; contentType: "image/jpeg"; fileSize: number }
  ) => {
    setSavingImageKind(kind);
    try {
      const profile = await uploadEmployeeProfileImage({
        kind,
        ...image,
      });
      queryClient.setQueryData(profileQueryKey, profile);
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
      throw error;
    } finally {
      setSavingImageKind(null);
    }
  };

  const handleDeleteImage = async (kind: "avatar" | "identityPhoto") => {
    setSavingImageKind(kind);
    try {
      const profile = await deleteEmployeeProfileImageApi(kind);
      queryClient.setQueryData(profileQueryKey, profile);
      showMessage(t("messages.imageRemoved"));
    } catch (error) {
      showMessage(getErrorMessage(error, "messages.removeImageFailed"));
      throw error;
    } finally {
      setSavingImageKind(null);
    }
  };

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
          {profileQuery.data?.avatarUrl ? (
            <Avatar.Image size={68} source={{ uri: profileQuery.data.avatarUrl }} style={styles.heroAvatar} />
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

        <CashierBarcodeCard employeeName={readonlyDisplayName} userIdentity={userIdentity} />

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
          <AvatarEditorField
            kind="avatar"
            label={t("fields.avatarUrl")}
            uri={profileQuery.data?.avatarUrl ?? ""}
            onSave={(image) => handleSaveImage("avatar", image)}
            onDelete={() => handleDeleteImage("avatar")}
            disabled={saveMutation.isPending || savingImageKind !== null}
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
          <AvatarEditorField
            kind="identityPhoto"
            label={t("fields.identityPhotoUrl")}
            uri={profileQuery.data?.identityPhotoUrl ?? ""}
            onSave={(image) => handleSaveImage("identityPhoto", image)}
            onDelete={() => handleDeleteImage("identityPhoto")}
            onImageLoadError={() => {
              void queryClient.invalidateQueries({ queryKey: profileQueryKey });
            }}
            disabled={saveMutation.isPending || savingImageKind !== null}
          />
        </Surface>

        <HelperText type="error" visible={profileQuery.isError && Boolean(profileQuery.data)}>
          {getErrorMessage(profileQuery.error, "messages.loadFailed")}
        </HelperText>

        <Button
          mode="contained"
          onPress={() => void handleSave()}
          loading={saveMutation.isPending}
          disabled={saveMutation.isPending || savingImageKind !== null}
          style={styles.saveButton}
        >
          {t("actions.save")}
        </Button>
      </ScrollView>

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
  saveButton: {
    marginTop: 4,
    marginBottom: 10,
  },
});
