import { useCallback, useMemo, useState } from "react";
import { Alert, ScrollView, StyleSheet, View } from "react-native";
import { useLocalSearchParams, useRouter } from "expo-router";
import {
  ActivityIndicator,
  Avatar,
  Button,
  Card,
  Chip,
  Dialog,
  IconButton,
  Portal,
  Snackbar,
  Text,
  TextInput,
} from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import { EmptyState } from "@/components/ui/EmptyState";
import { useStoreUserMutations, useStoreUserProfile } from "@/modules/users";
import { calculateAge, maskTrailingFour } from "@/modules/users/profile-display";
import { validatePasswordValue } from "@/modules/users/validation";
import { extractApiErrorMessage } from "@/shared/api/error-message";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { resolveLocaleTag } from "@/shared/i18n/types";

function firstParam(value: string | string[] | undefined) {
  return Array.isArray(value) ? value[0] : value;
}

function getInitials(name: string) {
  const words = name.trim().split(/\s+/).filter(Boolean);
  if (!words.length) {
    return "?";
  }
  if (words.length === 1) {
    return words[0].slice(0, 2).toUpperCase();
  }
  return `${words[0][0]}${words[1][0]}`.toUpperCase();
}

function formatDate(value: string | undefined, locale: string) {
  if (!value) {
    return null;
  }
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) {
    return value.slice(0, 10);
  }
  return new Intl.DateTimeFormat(locale, { dateStyle: "medium" }).format(
    parsed,
  );
}

function DetailRow({
  emptyValue,
  icon,
  label,
  value,
}: {
  emptyValue: string;
  icon: string;
  label: string;
  value?: string | null;
}) {
  return (
    <View style={styles.detailRow}>
      <Avatar.Icon size={34} icon={icon} style={styles.detailIcon} />
      <View style={styles.detailCopy}>
        <Text variant="labelMedium" style={styles.muted}>
          {label}
        </Text>
        <Text variant="bodyLarge">{value || emptyValue}</Text>
      </View>
    </View>
  );
}

export default function StaffDetailScreen() {
  const router = useRouter();
  const params = useLocalSearchParams();
  const { t, language } = useAppTranslation(["userManagement", "common"]);
  const locale = useMemo(() => resolveLocaleTag(language), [language]);
  const userGuid = firstParam(params.userGuid);
  const storeCode = firstParam(params.storeCode);
  const profileQuery = useStoreUserProfile(userGuid, storeCode);
  const { statusMutation, passwordMutation } = useStoreUserMutations(
    storeCode,
    undefined,
  );
  const [snackbarMessage, setSnackbarMessage] = useState("");
  const [resetPasswordVisible, setResetPasswordVisible] = useState(false);
  const [resetPasswordValue, setResetPasswordValue] = useState("");

  const profile = profileQuery.data;
  const title = profile?.fullName || profile?.username || t("detail.title");
  const isActive = profile?.status === 1;
  const emptyValue = t("common:na");
  const age = calculateAge(profile?.birthday);
  const gender = profile?.gender
    ? t(`detail.genders.${profile.gender}`, profile.gender)
    : emptyValue;
  const employmentType = profile?.employmentType
    ? t(`detail.employmentTypes.${profile.employmentType}`, profile.employmentType)
    : emptyValue;

  const handleBack = useCallback(() => {
    if (router.canGoBack()) {
      router.back();
      return;
    }
    router.replace("/(tabs)/users");
  }, [router]);

  const handleResetPassword = useCallback(async () => {
    if (!userGuid || !storeCode) {
      return;
    }

    const validationMessage = validatePasswordValue(resetPasswordValue, t);
    if (validationMessage) {
      setSnackbarMessage(validationMessage);
      return;
    }

    try {
      await passwordMutation.mutateAsync({
        userGuid,
        storeCode,
        newPassword: resetPasswordValue.trim(),
      });
      setResetPasswordVisible(false);
      setResetPasswordValue("");
      setSnackbarMessage(t("messages.passwordReset"));
    } catch (error) {
      console.warn("[staff-profile] password reset failed", error);
      setSnackbarMessage(
        extractApiErrorMessage(error, t("messages.passwordResetFailed")),
      );
    }
  }, [passwordMutation, resetPasswordValue, storeCode, t, userGuid]);

  const handleToggleStatus = useCallback(() => {
    if (!profile || !storeCode) {
      return;
    }

    const nextEnabled = profile.status !== 1;
    const actionLabel = nextEnabled
      ? t("actions.enable")
      : t("actions.disable");
    Alert.alert(
      actionLabel,
      t("dialogs.statusConfirmMessage", {
        action: actionLabel,
        username: profile.username,
      }),
      [
        { text: t("actions.cancel"), style: "cancel" },
        {
          text: actionLabel,
          style: nextEnabled ? "default" : "destructive",
          onPress: async () => {
            try {
              await statusMutation.mutateAsync({
                userGuid: profile.userGUID,
                storeCode,
                status: nextEnabled ? 1 : 0,
              });
              await profileQuery.refetch();
              setSnackbarMessage(
                nextEnabled
                  ? t("messages.userEnabled")
                  : t("messages.userDisabled"),
              );
            } catch (error) {
              console.warn("[staff-profile] status failed", error);
              setSnackbarMessage(
                extractApiErrorMessage(error, t("messages.statusFailed")),
              );
            }
          },
        },
      ],
    );
  }, [profile, profileQuery, statusMutation, storeCode, t]);

  const openSchedule = useCallback(() => {
    router.push("/(tabs)/attendance" as Parameters<typeof router.push>[0]);
  }, [router]);

  if (!userGuid || !storeCode) {
    return (
      <SafeAreaView style={styles.container}>
        <EmptyState
          title={t("detail.missingTitle")}
          description={t("detail.missingDescription")}
          primaryAction={{
            label: t("common:actions.back"),
            icon: "arrow-left",
            onPress: handleBack,
          }}
        />
      </SafeAreaView>
    );
  }

  if (profileQuery.isLoading && !profile) {
    return (
      <SafeAreaView style={styles.container}>
        <View style={styles.centered}>
          <ActivityIndicator size="large" />
        </View>
      </SafeAreaView>
    );
  }

  if (profileQuery.isError && !profile) {
    return (
      <SafeAreaView style={styles.container}>
        <EmptyState
          title={t("detail.loadFailedTitle")}
          description={
            profileQuery.error instanceof Error
              ? profileQuery.error.message
              : t("messages.loadFailedDescription")
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
      </SafeAreaView>
    );
  }

  return (
    <SafeAreaView style={styles.container} edges={["top", "left", "right"]}>
      <ScrollView contentContainerStyle={styles.content}>
        <View style={styles.topBar}>
          <IconButton icon="arrow-left" onPress={handleBack} />
          <Text variant="titleLarge">{t("detail.title")}</Text>
          <IconButton
            icon="pencil-outline"
            onPress={() => router.replace("/(tabs)/users")}
          />
        </View>

        <Card mode="elevated" style={styles.heroCard}>
          <Card.Content style={styles.heroContent}>
            <Avatar.Text
              size={72}
              label={getInitials(title)}
              style={styles.heroAvatar}
            />
            <View style={styles.heroCopy}>
              <Text variant="headlineSmall">{title}</Text>
              <Text variant="bodyMedium" style={styles.muted}>
                {profile?.employmentType
                  ? t(
                      `detail.employmentTypes.${profile.employmentType}`,
                      profile.employmentType,
                    )
                  : t("fields.positionValue")}
              </Text>
              <Chip
                compact
                style={isActive ? styles.activeChip : styles.inactiveChip}
              >
                {isActive ? t("statuses.active") : t("statuses.disabled")}
              </Chip>
            </View>
          </Card.Content>
        </Card>

        <View style={styles.segmentTabs}>
          <Chip selected>{t("detail.tabs.personalInfo")}</Chip>
          <Chip onPress={openSchedule}>{t("detail.tabs.schedule")}</Chip>
          <Chip onPress={openSchedule}>{t("detail.tabs.records")}</Chip>
        </View>

        <Card mode="elevated" style={styles.sectionCard}>
          <Card.Content style={styles.sectionContent}>
            <Text variant="titleMedium">
              {t("detail.sections.employeeDetails")}
            </Text>
            <DetailRow
              emptyValue={emptyValue}
              icon="badge-account-outline"
              label={t("detail.fields.employeeId")}
              value={profile?.userGUID}
            />
            <DetailRow
              emptyValue={emptyValue}
              icon="calendar-account-outline"
              label={t("detail.fields.age")}
              value={age === null ? null : String(age)}
            />
            <DetailRow
              emptyValue={emptyValue}
              icon="account-heart-outline"
              label={t("detail.fields.gender")}
              value={gender}
            />
            <DetailRow
              emptyValue={emptyValue}
              icon="briefcase-account-outline"
              label={t("detail.fields.employmentType")}
              value={employmentType}
            />
            <DetailRow
              emptyValue={emptyValue}
              icon="phone-outline"
              label={t("detail.fields.phone")}
              value={profile?.phone}
            />
            <DetailRow
              emptyValue={emptyValue}
              icon="email-outline"
              label={t("detail.fields.email")}
              value={profile?.email}
            />
            <DetailRow
              emptyValue={emptyValue}
              icon="storefront-outline"
              label={t("detail.fields.store")}
              value={profile?.storeName || profile?.storeCode}
            />
            <DetailRow
              emptyValue={emptyValue}
              icon="calendar-outline"
              label={t("detail.fields.joinDate")}
              value={formatDate(profile?.createdAt, locale)}
            />
            <DetailRow
              emptyValue={emptyValue}
              icon="cake-variant-outline"
              label={t("detail.fields.birthday")}
              value={formatDate(profile?.birthday, locale)}
            />
            <DetailRow
              emptyValue={emptyValue}
              icon="map-marker-outline"
              label={t("detail.fields.address")}
              value={profile?.address}
            />
          </Card.Content>
        </Card>

        <Card mode="elevated" style={styles.sectionCard}>
          <Card.Content style={styles.sectionContent}>
            <Text variant="titleMedium">
              {t("detail.sections.accountInfo")}
            </Text>
            <DetailRow
              emptyValue={emptyValue}
              icon="badge-account-outline"
              label={t("detail.fields.identityId")}
              value={maskTrailingFour(profile?.identityId, emptyValue)}
            />
            <DetailRow
              emptyValue={emptyValue}
              icon="credit-card-outline"
              label={t("detail.fields.bankAccount")}
              value={maskTrailingFour(profile?.bankAccountNumber, emptyValue)}
            />
            <DetailRow
              emptyValue={emptyValue}
              icon="identifier"
              label={t("detail.fields.superAccount")}
              value={maskTrailingFour(profile?.superannuationAccountNumber, emptyValue)}
            />
          </Card.Content>
        </Card>

        <Card mode="elevated" style={styles.sectionCard}>
          <Card.Content style={styles.sectionContent}>
            <Text variant="titleMedium">{t("detail.sections.payroll")}</Text>
            <DetailRow
              emptyValue={emptyValue}
              icon="bank-outline"
              label={t("detail.fields.bankBsb")}
              value={profile?.bankBsb}
            />
            <DetailRow
              emptyValue={emptyValue}
              icon="shield-account-outline"
              label={t("detail.fields.superCompany")}
              value={profile?.superannuationCompanyName}
            />
          </Card.Content>
        </Card>

        <Card mode="elevated" style={styles.sectionCard}>
          <Card.Content style={styles.sectionContent}>
            <Text variant="titleMedium">
              {t("detail.sections.quickActions")}
            </Text>
            <Button
              mode="outlined"
              icon="calendar-month-outline"
              onPress={openSchedule}
            >
              {t("detail.actions.viewFullSchedule")}
            </Button>
            <Button
              mode="outlined"
              icon="key-outline"
              onPress={() => setResetPasswordVisible(true)}
            >
              {t("actions.resetPassword")}
            </Button>
            <Button
              mode={isActive ? "outlined" : "contained-tonal"}
              icon={isActive ? "block-helper" : "check-circle-outline"}
              onPress={handleToggleStatus}
              loading={statusMutation.isPending}
            >
              {isActive ? t("actions.disable") : t("actions.enable")}
            </Button>
          </Card.Content>
        </Card>
      </ScrollView>

      <Portal>
        <Dialog
          visible={resetPasswordVisible}
          onDismiss={() => setResetPasswordVisible(false)}
        >
          <Dialog.Title>{t("dialogs.resetPasswordTitle")}</Dialog.Title>
          <Dialog.Content style={styles.dialogContent}>
            <Text variant="bodyMedium">
              {t("dialogs.resetPasswordDescription", {
                username: profile?.username ?? "",
              })}
            </Text>
            <TextInput
              mode="outlined"
              label={t("fields.newPassword")}
              value={resetPasswordValue}
              onChangeText={setResetPasswordValue}
              secureTextEntry
              autoCapitalize="none"
              disabled={passwordMutation.isPending}
            />
          </Dialog.Content>
          <Dialog.Actions>
            <Button
              onPress={() => setResetPasswordVisible(false)}
              disabled={passwordMutation.isPending}
            >
              {t("actions.cancel")}
            </Button>
            <Button
              onPress={handleResetPassword}
              loading={passwordMutation.isPending}
            >
              {t("actions.confirmReset")}
            </Button>
          </Dialog.Actions>
        </Dialog>
      </Portal>

      <Snackbar
        visible={Boolean(snackbarMessage)}
        onDismiss={() => setSnackbarMessage("")}
        duration={2500}
      >
        {snackbarMessage}
      </Snackbar>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  activeChip: {
    alignSelf: "flex-start",
    backgroundColor: "#D1FAE5",
  },
  centered: {
    alignItems: "center",
    flex: 1,
    justifyContent: "center",
  },
  container: {
    backgroundColor: "#F5F7FB",
    flex: 1,
  },
  content: {
    gap: 14,
    padding: 16,
    paddingBottom: 28,
  },
  detailCopy: {
    flex: 1,
    gap: 2,
  },
  detailIcon: {
    backgroundColor: "#EEF2F6",
  },
  detailRow: {
    alignItems: "center",
    flexDirection: "row",
    gap: 12,
  },
  dialogContent: {
    gap: 12,
  },
  heroAvatar: {
    backgroundColor: "#111827",
  },
  heroCard: {
    backgroundColor: "#FFFFFF",
    borderRadius: 12,
  },
  heroContent: {
    alignItems: "center",
    flexDirection: "row",
    gap: 16,
  },
  heroCopy: {
    flex: 1,
    gap: 6,
  },
  inactiveChip: {
    alignSelf: "flex-start",
    backgroundColor: "#FEE2E2",
  },
  muted: {
    color: "#6B7280",
  },
  sectionCard: {
    backgroundColor: "#FFFFFF",
    borderRadius: 12,
  },
  sectionContent: {
    gap: 14,
  },
  segmentTabs: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
  },
  topBar: {
    alignItems: "center",
    flexDirection: "row",
    justifyContent: "space-between",
  },
});
