import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { AppState, Image, RefreshControl, ScrollView, StyleSheet, View } from "react-native";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useFocusEffect, useRouter } from "expo-router";
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
  getMySensitiveChangeRequestApi,
  updateMyEmployeeProfileApi,
  upsertMySensitiveChangeRequestApi,
} from "@/modules/employee-profile/api";
import { AvatarEditorField } from "@/modules/employee-profile/AvatarEditorField";
import { CashierBarcodeCard } from "@/modules/employee-profile/CashierBarcodeCard";
import {
  getEmployeeProfileQueryKey,
  getEmployeeSensitiveChangeQueryKey,
  resolveEmployeeProfileIdentity,
  shouldResetEmployeeProfileDraft,
} from "@/modules/employee-profile/cache-keys";
import {
  syncEmployeeProfileDraft,
  toEmployeeProfileDraft,
} from "@/modules/employee-profile/profile-draft";
import {
  buildNonSensitiveProfilePayload,
  getChangedSensitiveFields,
  getSensitiveAccountSummary,
  getSensitiveStatusView,
  isSensitiveVersionConflict,
  normalizeSensitiveDraft,
  refreshEmployeeProfileAfterIdentityMutation,
  selectSensitiveDraft,
  shouldRefreshSensitiveProfile,
  shouldShowPendingIdentityPhotoRemoval,
  submitSensitiveProfileWithCache,
} from "@/modules/employee-profile/sensitive-profile";
import {
  EmployeeProfileImageUploadError,
  uploadEmployeeProfileImage,
} from "@/modules/employee-profile/image-upload";
import {
  EMPLOYMENT_TYPES,
  GENDERS,
  type EmployeeProfile,
  type SensitiveEmployeeProfilePayload,
  type UpdateEmployeeProfilePayload,
} from "@/modules/employee-profile/types";
import {
  getIdentityPhotoRefetchDelay,
  shouldRefreshIdentityPhotoAfterLoadError,
} from "@/modules/employee-profile/identity-photo-expiry";
import { resolveLocalizedErrorMessage } from "@/shared/i18n/error-message";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { resolveLocaleTag } from "@/shared/i18n/types";
import { useAuthStore } from "@/store/auth-store";

const EMPTY_FORM: UpdateEmployeeProfilePayload = {
  phone: "",
  birthday: "",
  gender: "",
  employmentType: "",
  address: "",
};

const EMPTY_SENSITIVE_FORM: SensitiveEmployeeProfilePayload = {
  bankBsb: "",
  bankAccountNumber: "",
  superannuationCompanyName: "",
  superannuationCompanyCode: "",
  superannuationAccountNumber: "",
  identityType: "",
  identityId: "",
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
  const [sensitiveFormValues, setSensitiveFormValues] = useState<SensitiveEmployeeProfilePayload>(
    EMPTY_SENSITIVE_FORM
  );
  const [sensitiveEditing, setSensitiveEditing] = useState(false);
  const [snackbarMessage, setSnackbarMessage] = useState("");
  const [snackbarVisible, setSnackbarVisible] = useState(false);
  const [savingImageKind, setSavingImageKind] = useState<"avatar" | "identityPhoto" | null>(null);
  const formInitializedRef = useRef(false);
  const formIdentityRef = useRef("");
  const formalIdentityPhotoErrorUrlRef = useRef("");
  const pendingIdentityPhotoErrorUrlRef = useRef("");
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
  const sensitiveQueryKey = useMemo(
    () => getEmployeeSensitiveChangeQueryKey(userIdentity),
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
    setSensitiveFormValues(EMPTY_SENSITIVE_FORM);
    setSensitiveEditing(false);
  }, [userIdentity]);

  const profileQuery = useQuery({
    queryKey: profileQueryKey,
    queryFn: getMyEmployeeProfileApi,
    enabled: Boolean(isAuthenticated && user),
    refetchInterval: (query) => getIdentityPhotoRefetchDelay(
      (query.state.data as EmployeeProfile | undefined)?.identityPhotoUrlExpiresAt
    ),
  });

  const sensitiveQuery = useQuery({
    queryKey: sensitiveQueryKey,
    queryFn: getMySensitiveChangeRequestApi,
    enabled: Boolean(isAuthenticated && user),
    refetchInterval: (query) => getIdentityPhotoRefetchDelay(
      query.state.data?.identityPhotoUrlExpiresAt
    ),
  });

  const refreshProfileQueries = useCallback(async () => {
    if (!shouldRefreshSensitiveProfile("manual", isAuthenticated, AppState.currentState)) {
      return;
    }
    await Promise.all([profileQuery.refetch(), sensitiveQuery.refetch()]);
  }, [isAuthenticated, profileQuery.refetch, sensitiveQuery.refetch]);

  useFocusEffect(useCallback(() => {
    if (!shouldRefreshSensitiveProfile("focus", isAuthenticated, AppState.currentState)) {
      return;
    }
    void queryClient.invalidateQueries({ queryKey: profileQueryKey });
    void queryClient.invalidateQueries({ queryKey: sensitiveQueryKey });
  }, [isAuthenticated, profileQueryKey, queryClient, sensitiveQueryKey]));

  useEffect(() => {
    const subscription = AppState.addEventListener("change", (state) => {
      if (shouldRefreshSensitiveProfile("app-active", isAuthenticated, state)) {
        void queryClient.invalidateQueries({ queryKey: profileQueryKey });
        void queryClient.invalidateQueries({ queryKey: sensitiveQueryKey });
      }
    });
    return () => subscription.remove();
  }, [isAuthenticated, profileQueryKey, queryClient, sensitiveQueryKey]);

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

  const sensitiveMutation = useMutation({
    mutationFn: (payload: SensitiveEmployeeProfilePayload) => submitSensitiveProfileWithCache(
      payload,
      {
        cancelRequestQuery: () => queryClient.cancelQueries({ queryKey: sensitiveQueryKey }),
        submitRequest: upsertMySensitiveChangeRequestApi,
        setRequestData: (request) => queryClient.setQueryData(sensitiveQueryKey, request),
        refreshRequestQuery: () => queryClient.invalidateQueries({
          queryKey: sensitiveQueryKey,
          refetchType: "active",
        }),
      }
    ),
    onSuccess: () => {
      setSensitiveEditing(false);
      showMessage(t("messages.sensitiveSubmitSuccess"));
    },
    onError: (error) => {
      if (isSensitiveVersionConflict(error)) {
        setSensitiveEditing(false);
        void refreshProfileQueries();
        showMessage(t("messages.versionConflict"));
        return;
      }
      showMessage(getErrorMessage(error, "messages.sensitiveSubmitFailed"));
    },
  });

  const readonlyUsername = profileQuery.data?.username || user?.username || "";
  const readonlyDisplayName =
    profileQuery.data?.displayName || user?.fullName || t("common:na");
  const createdAtText = formatDateTime(profileQuery.data?.createdAt, locale) || t("common:na");
  const updatedAtText = formatDateTime(profileQuery.data?.updatedAt, locale) || t("common:na");
  const sensitiveStatus = getSensitiveStatusView(sensitiveQuery.data);
  const sensitiveChangedFields = useMemo(() => {
    if (!profileQuery.data || !sensitiveQuery.data) {
      return [];
    }
    return sensitiveQuery.data.changedFields.length
      ? sensitiveQuery.data.changedFields
      : getChangedSensitiveFields(profileQuery.data, selectSensitiveDraft(profileQuery.data, sensitiveQuery.data));
  }, [profileQuery.data, sensitiveQuery.data]);
  const pendingIdentityPhotoRemoval = sensitiveQuery.data?.status === "Pending"
    && shouldShowPendingIdentityPhotoRemoval({
      changedFields: sensitiveChangedFields,
      pendingHasIdentityPhoto: sensitiveQuery.data.hasIdentityPhoto,
      formalHasIdentityPhoto: Boolean(profileQuery.data?.identityPhotoUrl),
    });

  const setFieldValue = useCallback(
    <K extends keyof UpdateEmployeeProfilePayload>(
      key: K,
      value: UpdateEmployeeProfilePayload[K]
    ) => {
      setFormValues((current) => ({ ...current, [key]: value }));
    },
    []
  );

  const setSensitiveFieldValue = useCallback(
    <K extends keyof SensitiveEmployeeProfilePayload>(
      key: K,
      value: SensitiveEmployeeProfilePayload[K]
    ) => {
      setSensitiveFormValues((current) => ({ ...current, [key]: value }));
    },
    []
  );

  const handleStartSensitiveEdit = () => {
    if (!profileQuery.data) {
      return;
    }
    setSensitiveFormValues(selectSensitiveDraft(profileQuery.data, sensitiveQuery.data));
    setSensitiveEditing(true);
  };

  const handleSave = async () => {
    try {
      await saveMutation.mutateAsync(buildNonSensitiveProfilePayload(formValues));
    } catch {
      // handled in mutation callbacks
    }
  };

  const handleSensitiveSubmit = async () => {
    try {
      await sensitiveMutation.mutateAsync(normalizeSensitiveDraft(sensitiveFormValues));
    } catch {
      // mutation 回调统一展示错误，避免敏感值进入日志。
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
      if (kind === "identityPhoto") {
        const refreshResult = await refreshEmployeeProfileAfterIdentityMutation({
          refetchSensitive: sensitiveQuery.refetch,
          refetchFormal: profileQuery.refetch,
        });
        showMessage(t(refreshResult.isError
          ? "messages.identityStatusRefreshFailed"
          : "messages.identitySubmitSuccess"));
      } else {
        queryClient.setQueryData(profileQueryKey, profile);
        showMessage(t("messages.uploadSuccess"));
      }
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
      if (kind === "identityPhoto") {
        const refreshResult = await refreshEmployeeProfileAfterIdentityMutation({
          refetchSensitive: sensitiveQuery.refetch,
          refetchFormal: profileQuery.refetch,
        });
        showMessage(t(refreshResult.isError
          ? "messages.identityStatusRefreshFailed"
          : "messages.identityRemoveSubmitSuccess"));
      } else {
        queryClient.setQueryData(profileQueryKey, profile);
        showMessage(t("messages.imageRemoved"));
      }
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
      <ScrollView
        contentContainerStyle={styles.content}
        refreshControl={(
          <RefreshControl
            refreshing={profileQuery.isRefetching || sensitiveQuery.isRefetching}
            onRefresh={() => void refreshProfileQueries()}
          />
        )}
      >
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
          <Text variant="titleMedium">{t("sections.sensitiveStatus")}</Text>
          {sensitiveQuery.isLoading ? <ActivityIndicator /> : (
            <Chip compact style={styles.statusChip}>{t(sensitiveStatus.statusKey)}</Chip>
          )}
          {sensitiveStatus.submittedAt ? (
            <Text variant="bodySmall" style={styles.metaText}>
              {t("sensitive.submittedAt", {
                time: formatDateTime(sensitiveStatus.submittedAt, locale) ?? sensitiveStatus.submittedAt,
              })}
            </Text>
          ) : null}
          {sensitiveChangedFields.length ? (
            <View style={styles.changedFields}>
              <Text variant="labelMedium">{t("sensitive.changedFields")}</Text>
              <Text variant="bodyMedium">
                {sensitiveChangedFields.map((field) => t(`fields.${field}`)).join(t("sensitive.fieldSeparator"))}
              </Text>
            </View>
          ) : null}
          {sensitiveStatus.reviewReason ? (
            <HelperText type="error" visible>
              {t("sensitive.rejectionReason", { reason: sensitiveStatus.reviewReason })}
            </HelperText>
          ) : null}
          <HelperText type="error" visible={sensitiveQuery.isError}>
            {getErrorMessage(sensitiveQuery.error, "messages.sensitiveLoadFailed")}
          </HelperText>
          {sensitiveQuery.isError ? (
            <Button mode="text" icon="refresh" onPress={() => void sensitiveQuery.refetch()}>
              {t("common:actions.retry")}
            </Button>
          ) : null}
          {!sensitiveEditing ? (
            <Button
              mode="outlined"
              icon="pencil"
              onPress={handleStartSensitiveEdit}
              disabled={sensitiveQuery.isLoading || sensitiveQuery.isError}
            >
              {t("actions.editSensitive")}
            </Button>
          ) : null}
        </Surface>

        <Surface style={styles.card} elevation={1}>
          <Text variant="titleMedium">{t("sections.banking")}</Text>
          <Text variant="labelMedium" style={styles.metaText}>{t("sensitive.confirmed")}</Text>
          <Text variant="bodyMedium">{t("fields.bankBsb")}: {profileQuery.data?.bankBsb || t("common:na")}</Text>
          <Text variant="bodyMedium">
            {t("fields.bankAccountNumber")}: {getSensitiveAccountSummary(profileQuery.data?.bankAccountNumber) || t("common:na")}
          </Text>
          {sensitiveQuery.data?.status === "Pending" ? (
            <View style={styles.pendingSnapshot}>
              <Text variant="labelMedium">{t("sensitive.pendingSnapshot")}</Text>
              <Text variant="bodyMedium">{t("fields.bankBsb")}: {sensitiveQuery.data.bankBsb || t("common:na")}</Text>
              <Text variant="bodyMedium">
                {t("fields.bankAccountNumber")}: {getSensitiveAccountSummary(sensitiveQuery.data.bankAccountNumber) || t("common:na")}
              </Text>
            </View>
          ) : null}
          {sensitiveEditing ? (
            <>
              <TextInput mode="outlined" label={t("fields.bankBsb")} value={sensitiveFormValues.bankBsb} onChangeText={(value) => setSensitiveFieldValue("bankBsb", value)} />
              <TextInput mode="outlined" label={t("fields.bankAccountNumber")} value={sensitiveFormValues.bankAccountNumber} onChangeText={(value) => setSensitiveFieldValue("bankAccountNumber", value)} secureTextEntry />
            </>
          ) : null}
        </Surface>

        <Surface style={styles.card} elevation={1}>
          <Text variant="titleMedium">{t("sections.superannuation")}</Text>
          <Text variant="labelMedium" style={styles.metaText}>{t("sensitive.confirmed")}</Text>
          <Text variant="bodyMedium">{profileQuery.data?.superannuationCompanyName || t("common:na")}</Text>
          <Text variant="bodyMedium">{profileQuery.data?.superannuationCompanyCode || t("common:na")}</Text>
          <Text variant="bodyMedium">
            {getSensitiveAccountSummary(profileQuery.data?.superannuationAccountNumber) || t("common:na")}
          </Text>
          {sensitiveQuery.data?.status === "Pending" ? (
            <View style={styles.pendingSnapshot}>
              <Text variant="labelMedium">{t("sensitive.pendingSnapshot")}</Text>
              <Text variant="bodyMedium">{sensitiveQuery.data.superannuationCompanyName || t("common:na")}</Text>
              <Text variant="bodyMedium">{sensitiveQuery.data.superannuationCompanyCode || t("common:na")}</Text>
              <Text variant="bodyMedium">{getSensitiveAccountSummary(sensitiveQuery.data.superannuationAccountNumber) || t("common:na")}</Text>
            </View>
          ) : null}
          {sensitiveEditing ? (
            <>
              <TextInput mode="outlined" label={t("fields.superannuationCompanyName")} value={sensitiveFormValues.superannuationCompanyName} onChangeText={(value) => setSensitiveFieldValue("superannuationCompanyName", value)} />
              <TextInput mode="outlined" label={t("fields.superannuationCompanyCode")} value={sensitiveFormValues.superannuationCompanyCode} onChangeText={(value) => setSensitiveFieldValue("superannuationCompanyCode", value)} />
              <TextInput mode="outlined" label={t("fields.superannuationAccountNumber")} value={sensitiveFormValues.superannuationAccountNumber} onChangeText={(value) => setSensitiveFieldValue("superannuationAccountNumber", value)} secureTextEntry />
            </>
          ) : null}
        </Surface>

        <Surface style={styles.card} elevation={1}>
          <Text variant="titleMedium">{t("sections.personal")}</Text>
          <TextInput
            mode="outlined"
            label={t("fields.phone")}
            value={formValues.phone}
            onChangeText={(value) => setFieldValue("phone", value)}
            keyboardType="phone-pad"
          />
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
          <View style={styles.segmentBlock}>
            <Text variant="labelLarge">{t("fields.employmentType")}</Text>
            <SegmentedButtons
              value={formValues.employmentType}
              onValueChange={(value) => setFieldValue("employmentType", value)}
              buttons={EMPLOYMENT_TYPES.map((value) => ({
                value,
                label: t(`employmentTypeOptions.${value}`),
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
          <Text variant="labelMedium" style={styles.metaText}>{t("sensitive.confirmed")}</Text>
          <Text variant="bodyMedium">{profileQuery.data?.identityType || t("common:na")}</Text>
          <Text variant="bodyMedium">{getSensitiveAccountSummary(profileQuery.data?.identityId) || t("common:na")}</Text>
          {profileQuery.data?.identityPhotoUrl ? (
            <Image
              source={{ uri: profileQuery.data.identityPhotoUrl }}
              style={styles.identityPreview}
              resizeMode="contain"
              onError={() => {
                const imageUrl = profileQuery.data?.identityPhotoUrl;
                if (!shouldRefreshIdentityPhotoAfterLoadError(
                  imageUrl,
                  formalIdentityPhotoErrorUrlRef.current
                )) {
                  return;
                }
                // 每个签名 URL 最多自动刷新一次，避免 Image onError 紧密循环。
                formalIdentityPhotoErrorUrlRef.current = imageUrl!;
                void profileQuery.refetch();
              }}
            />
          ) : <Text variant="bodySmall" style={styles.metaText}>{t("preview.empty")}</Text>}
          {sensitiveQuery.data?.status === "Pending" ? (
            <View style={styles.pendingSnapshot}>
              <Text variant="labelMedium">{t("sensitive.pendingSnapshot")}</Text>
              <Text variant="bodyMedium">{sensitiveQuery.data.identityType || t("common:na")}</Text>
              <Text variant="bodyMedium">{getSensitiveAccountSummary(sensitiveQuery.data.identityId) || t("common:na")}</Text>
              {sensitiveQuery.data.identityPhotoUrl ? (
                <Image
                  source={{ uri: sensitiveQuery.data.identityPhotoUrl }}
                  style={styles.identityPreview}
                  resizeMode="contain"
                  onError={() => {
                    const imageUrl = sensitiveQuery.data?.identityPhotoUrl;
                    if (!shouldRefreshIdentityPhotoAfterLoadError(
                      imageUrl,
                      pendingIdentityPhotoErrorUrlRef.current
                    )) {
                      return;
                    }
                    // 待审私有 URL 失败时只刷新当前签名一次；新签名返回后才允许再次尝试。
                    pendingIdentityPhotoErrorUrlRef.current = imageUrl!;
                    void sensitiveQuery.refetch();
                  }}
                />
              ) : pendingIdentityPhotoRemoval ? (
                <Text variant="bodySmall">{t("sensitive.pendingPhotoRemoval")}</Text>
              ) : (
                <Text variant="bodySmall" style={styles.metaText}>{t("preview.empty")}</Text>
              )}
            </View>
          ) : null}
          {sensitiveEditing ? (
            <>
              <TextInput mode="outlined" label={t("fields.identityType")} value={sensitiveFormValues.identityType} onChangeText={(value) => setSensitiveFieldValue("identityType", value)} />
              <TextInput mode="outlined" label={t("fields.identityId")} value={sensitiveFormValues.identityId} onChangeText={(value) => setSensitiveFieldValue("identityId", value)} secureTextEntry />
              <AvatarEditorField
                kind="identityPhoto"
                label={t("fields.identityPhotoUrl")}
                uri={sensitiveQuery.data?.status === "Pending"
                  ? sensitiveQuery.data.identityPhotoUrl
                  : profileQuery.data?.identityPhotoUrl ?? ""}
                onSave={(image) => handleSaveImage("identityPhoto", image)}
                onDelete={() => handleDeleteImage("identityPhoto")}
                disabled={sensitiveMutation.isPending || savingImageKind !== null}
              />
              <Button
                mode="contained"
                onPress={() => void handleSensitiveSubmit()}
                loading={sensitiveMutation.isPending}
                disabled={sensitiveMutation.isPending || savingImageKind !== null}
              >
                {t("actions.submitSensitive")}
              </Button>
              <Button mode="text" onPress={() => setSensitiveEditing(false)} disabled={sensitiveMutation.isPending}>
                {t("common:actions.cancel")}
              </Button>
            </>
          ) : null}
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
          {t("actions.savePersonal")}
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
  statusChip: {
    alignSelf: "flex-start",
  },
  changedFields: {
    gap: 4,
  },
  pendingSnapshot: {
    gap: 6,
    padding: 12,
    borderRadius: 12,
    backgroundColor: "#F0F4F8",
  },
  identityPreview: {
    width: "100%",
    height: 180,
    borderRadius: 14,
    backgroundColor: "#E9EDF3",
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
