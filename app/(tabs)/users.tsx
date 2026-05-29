import { useCallback, useEffect, useMemo, useState } from "react";
import { Alert, FlatList, RefreshControl, ScrollView, StyleSheet, View } from "react-native";
import { useRouter } from "expo-router";
import { SafeAreaView } from "react-native-safe-area-context";
import {
  ActivityIndicator,
  Avatar,
  Button,
  Card,
  Chip,
  Dialog,
  FAB,
  IconButton,
  Portal,
  Searchbar,
  SegmentedButtons,
  Snackbar,
  Switch,
  Text,
  TextInput,
} from "react-native-paper";
import { EmptyState } from "@/components/ui/EmptyState";
import { StorePickerModal } from "@/components/ui/StorePickerModal";
import type { Store } from "@/modules/shop/types";
import { getDeviceBoundStoreCode } from "@/modules/shop/device-bound-store-filter";
import { useStores } from "@/modules/shop/use-stores";
import {
  STORE_STAFF_ROLE,
  useStoreUserDetail,
  useStoreUserMutations,
  useStoreUsers,
  type StoreUserFormValues,
  type StoreUserListItem,
} from "@/modules/users";
import { calculateAge } from "@/modules/users/profile-display";
import { validatePasswordValue, validateStoreUserForm, type UserDialogMode } from "@/modules/users/validation";
import { resolveLocalizedErrorMessage } from "@/shared/i18n/error-message";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { useAuthStore } from "@/store/auth-store";

type StatusFilter = "all" | "active" | "disabled";

const EMPTY_FORM: StoreUserFormValues = {
  username: "",
  fullName: "",
  email: "",
  phone: "",
  password: "",
  status: true,
};

function getInitials(user: StoreUserListItem) {
  const source = user.fullName || user.username || "?";
  const words = source.trim().split(/\s+/).filter(Boolean);
  if (!words.length) {
    return "?";
  }
  if (words.length === 1) {
    return words[0].slice(0, 2).toUpperCase();
  }
  return `${words[0][0]}${words[1][0]}`.toUpperCase();
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

export default function UsersScreen() {
  const router = useRouter();
  const { t, language } = useAppTranslation(["userManagement", "common"]);
  const access = useAuthStore((state) => state.access);
  const {
    stores,
    selectedStoreCode: rememberedStoreCode,
    isDeviceMode,
    isHydratingSelection,
    isLoading: storesLoading,
    selectStore,
  } = useStores();
  const [managedStoreCode, setManagedStoreCode] = useState<string | null>(null);
  const [storePickerVisible, setStorePickerVisible] = useState(false);
  const [keywordInput, setKeywordInput] = useState("");
  const [keyword, setKeyword] = useState("");
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("all");
  const [snackbarMessage, setSnackbarMessage] = useState("");
  const [dialogVisible, setDialogVisible] = useState(false);
  const [dialogMode, setDialogMode] = useState<UserDialogMode>("create");
  const [editingUserGuid, setEditingUserGuid] = useState<string | null>(null);
  const [editingStoreCode, setEditingStoreCode] = useState<string | null>(null);
  const [formValues, setFormValues] = useState<StoreUserFormValues>(EMPTY_FORM);
  const [resetPasswordVisible, setResetPasswordVisible] = useState(false);
  const [resetPasswordValue, setResetPasswordValue] = useState("");
  const [passwordUser, setPasswordUser] = useState<StoreUserListItem | null>(null);
  const deviceBoundStoreCode = getDeviceBoundStoreCode({
    isDeviceMode,
    selectedStoreCode: rememberedStoreCode,
  });

  const canManageUsers = access.isAdmin || (access.canReadUser && access.canWriteUser);
  const manageableStores = useMemo(
    () =>
      deviceBoundStoreCode
        ? stores
        : access.isAdmin
          ? stores
          : stores.filter((store) => store.isPrimary === true),
    [access.isAdmin, deviceBoundStoreCode, stores]
  );

  useEffect(() => {
    if (isHydratingSelection || storesLoading) {
      return;
    }

    setManagedStoreCode((current) => {
      if (deviceBoundStoreCode) {
        return deviceBoundStoreCode;
      }

      if (current && manageableStores.some((store) => store.storeCode === current)) {
        return current;
      }

      const selectedManageableStore = rememberedStoreCode
        ? manageableStores.find((store) => store.storeCode === rememberedStoreCode)
        : null;
      if (selectedManageableStore) {
        return selectedManageableStore.storeCode;
      }

      return null;
    });
  }, [deviceBoundStoreCode, isHydratingSelection, manageableStores, rememberedStoreCode, storesLoading]);

  const managedStore = useMemo(
    () => manageableStores.find((store) => store.storeCode === managedStoreCode) ?? null,
    [manageableStores, managedStoreCode]
  );

  const usersQuery = useStoreUsers(
    canManageUsers ? (isDeviceMode ? deviceBoundStoreCode : managedStoreCode) : undefined,
    keyword
  );
  const detailQuery = useStoreUserDetail(
    dialogMode === "edit" ? editingUserGuid : null,
    dialogMode === "edit" ? editingStoreCode : null
  );
  const { createMutation, updateMutation, statusMutation, passwordMutation } =
    useStoreUserMutations(managedStoreCode, keyword);

  const isBusy =
    createMutation.isPending ||
    updateMutation.isPending ||
    statusMutation.isPending ||
    passwordMutation.isPending;

  const resolveUserStoreCode = useCallback(
    (user: StoreUserListItem) => managedStoreCode || user.storeCode || null,
    [managedStoreCode]
  );

  useEffect(() => {
    if (!detailQuery.isSuccess || dialogMode !== "edit" || !dialogVisible) {
      return;
    }

    setFormValues({
      username: detailQuery.data.username,
      fullName: detailQuery.data.fullName ?? "",
      email: detailQuery.data.email ?? "",
      phone: detailQuery.data.phone ?? "",
      password: "",
      status: detailQuery.data.status === 1,
    });
  }, [detailQuery.data, detailQuery.isSuccess, dialogMode, dialogVisible]);

  const resetDialogState = useCallback(() => {
    setDialogVisible(false);
    setDialogMode("create");
    setEditingUserGuid(null);
    setEditingStoreCode(null);
    setFormValues(EMPTY_FORM);
  }, []);

  const openCreateDialog = useCallback(() => {
    setDialogMode("create");
    setEditingUserGuid(null);
    setEditingStoreCode(null);
    setFormValues(EMPTY_FORM);
    setDialogVisible(true);
  }, []);

  const openEditDialog = useCallback(
    (user: StoreUserListItem) => {
      const targetStoreCode = resolveUserStoreCode(user);
      if (!targetStoreCode) {
        setSnackbarMessage(t("messages.selectStoreFirst"));
        return;
      }

      setDialogMode("edit");
      setEditingUserGuid(user.userGUID);
      setEditingStoreCode(targetStoreCode);
      setFormValues({
        username: user.username,
        fullName: user.fullName ?? "",
        email: user.email ?? "",
        phone: user.phone ?? "",
        password: "",
        status: user.status === 1,
      });
      setDialogVisible(true);
    },
    [resolveUserStoreCode, t]
  );

  const openStaffDetail = useCallback(
    (user: StoreUserListItem) => {
      const targetStoreCode = resolveUserStoreCode(user);
      if (!targetStoreCode) {
        setSnackbarMessage(t("messages.selectStoreFirst"));
        return;
      }

      router.push({
        pathname: "/staff/[userGuid]",
        params: { userGuid: user.userGUID, storeCode: targetStoreCode },
      } as Parameters<typeof router.push>[0]);
    },
    [resolveUserStoreCode, router, t]
  );

  const closeResetPasswordDialog = useCallback(() => {
    setPasswordUser(null);
    setResetPasswordValue("");
    setResetPasswordVisible(false);
  }, []);

  const submitKeyword = useCallback(() => {
    setKeyword(keywordInput.trim());
  }, [keywordInput]);

  const handleSelectManagedStore = useCallback(
    async (store: Store | null) => {
      setManagedStoreCode(deviceBoundStoreCode ?? store?.storeCode ?? null);
      setStorePickerVisible(false);

      try {
        await selectStore(store);
      } catch (error) {
        console.warn("[store-users] failed to persist store selection", error);
      }
    },
    [deviceBoundStoreCode, selectStore]
  );

  const handleRefresh = useCallback(async () => {
    try {
      await usersQuery.refetch();
    } catch (error) {
      console.warn("[store-users] refresh failed", error);
      setSnackbarMessage(resolveLocalizedErrorMessage(error, { t, language, fallbackKey: "messages.refreshFailed" }));
    }
  }, [language, t, usersQuery]);

  const validateForm = useCallback(() => {
    const validationMessage = validateStoreUserForm(formValues, dialogMode, t);
    if (validationMessage) {
      setSnackbarMessage(validationMessage);
      return false;
    }

    return true;
  }, [dialogMode, formValues, t]);

  const handleSubmit = useCallback(async () => {
    const targetStoreCode = dialogMode === "edit" ? editingStoreCode : managedStoreCode;
    if (!targetStoreCode) {
      setSnackbarMessage(t("messages.selectStoreFirst"));
      return;
    }

    if (!validateForm()) {
      return;
    }

    const payload = {
      username: formValues.username.trim(),
      fullName: formValues.fullName.trim() || undefined,
      email: formValues.email.trim() || undefined,
      phone: formValues.phone.trim() || undefined,
      password: formValues.password.trim() || undefined,
      status: formValues.status ? 1 : 0,
      storeCode: targetStoreCode,
      roleNames: [STORE_STAFF_ROLE],
    };

    try {
      if (dialogMode === "edit" && editingUserGuid) {
        await updateMutation.mutateAsync({ ...payload, userGuid: editingUserGuid });
        setSnackbarMessage(t("messages.userUpdated"));
      } else {
        await createMutation.mutateAsync({ ...payload, employmentType: "casual" });
        setSnackbarMessage(t("messages.userCreated"));
      }

      resetDialogState();
    } catch (error) {
      console.warn("[store-users] save failed", error);
      setSnackbarMessage(resolveLocalizedErrorMessage(error, { t, language, fallbackKey: "messages.saveFailed" }));
    }
  }, [
    createMutation,
    dialogMode,
    editingStoreCode,
    editingUserGuid,
    formValues,
    managedStoreCode,
    resetDialogState,
    language,
    t,
    updateMutation,
    validateForm,
  ]);

  const handleToggleStatus = useCallback(
    (user: StoreUserListItem) => {
      const targetStoreCode = resolveUserStoreCode(user);
      if (!targetStoreCode) {
        setSnackbarMessage(t("messages.selectStoreFirst"));
        return;
      }

      const nextEnabled = user.status !== 1;
      const actionLabel = nextEnabled ? t("actions.enable") : t("actions.disable");
      Alert.alert(
        actionLabel,
        t("dialogs.statusConfirmMessage", { action: actionLabel, username: user.username }),
        [
          { text: t("actions.cancel"), style: "cancel" },
          {
            text: actionLabel,
            style: nextEnabled ? "default" : "destructive",
            onPress: async () => {
              try {
                await statusMutation.mutateAsync({
                  userGuid: user.userGUID,
                  storeCode: targetStoreCode,
                  status: nextEnabled ? 1 : 0,
                });
                setSnackbarMessage(nextEnabled ? t("messages.userEnabled") : t("messages.userDisabled"));
              } catch (error) {
                console.warn("[store-users] status failed", error);
                setSnackbarMessage(resolveLocalizedErrorMessage(error, { t, language, fallbackKey: "messages.statusFailed" }));
              }
            },
          },
        ]
      );
    },
    [language, resolveUserStoreCode, statusMutation, t]
  );

  const openResetPasswordDialog = useCallback((user: StoreUserListItem) => {
    setPasswordUser(user);
    setResetPasswordValue("");
    setResetPasswordVisible(true);
  }, []);

  const handleResetPassword = useCallback(async () => {
    if (!passwordUser) {
      return;
    }

    const targetStoreCode = resolveUserStoreCode(passwordUser);
    if (!targetStoreCode) {
      setSnackbarMessage(t("messages.selectStoreFirst"));
      return;
    }

    const validationMessage = validatePasswordValue(resetPasswordValue, t);
    if (validationMessage) {
      setSnackbarMessage(validationMessage);
      return;
    }

    try {
      await passwordMutation.mutateAsync({
        userGuid: passwordUser.userGUID,
        storeCode: targetStoreCode,
        newPassword: resetPasswordValue.trim(),
      });
      setSnackbarMessage(t("messages.passwordReset"));
      closeResetPasswordDialog();
    } catch (error) {
      console.warn("[store-users] password reset failed", error);
      setSnackbarMessage(resolveLocalizedErrorMessage(error, { t, language, fallbackKey: "messages.passwordResetFailed" }));
    }
  }, [
    closeResetPasswordDialog,
    language,
    passwordMutation,
    passwordUser,
    resolveUserStoreCode,
    resetPasswordValue,
    t,
  ]);

  const filteredUsers = useMemo(() => {
    const items = usersQuery.data ?? [];
    const filtered = items.filter((item) => {
      if (statusFilter === "active") {
        return item.status === 1;
      }
      if (statusFilter === "disabled") {
        return item.status !== 1;
      }
      return true;
    });

    return [...filtered].sort((left, right) =>
      (left.fullName || left.username).localeCompare(right.fullName || right.username)
    );
  }, [statusFilter, usersQuery.data]);

  const storeCaption = useMemo(() => {
    if (!managedStoreCode) {
      return t("currentStore.allRelated");
    }

    if (!managedStore) {
      return t("currentStore.empty");
    }

    return t("currentStore.value", {
      code: managedStore.storeCode,
      name: managedStore.storeName || managedStore.storeCode,
    });
  }, [managedStore, managedStoreCode, t]);

  const renderUserCard = useCallback(
    ({ item }: { item: StoreUserListItem }) => {
      const lastLogin = formatDateTime(item.lastLoginTime, language);
      const updatedAt = formatDateTime(item.updatedAt, language);
      const storeName = item.storeName || managedStore?.storeName || item.storeCode || managedStoreCode;
      const emptyValue = t("common:na");
      const age = calculateAge(item.birthday);
      const gender = item.gender
        ? t(`detail.genders.${item.gender}`, item.gender)
        : emptyValue;
      const employmentType = item.employmentType
        ? t(`detail.employmentTypes.${item.employmentType}`, item.employmentType)
        : emptyValue;

      return (
        <Card style={styles.userCard} mode="elevated" onPress={() => openStaffDetail(item)}>
          <Card.Content style={styles.userCardContent}>
            <View style={styles.userCardHeader}>
              <View style={styles.identityRow}>
                <Avatar.Text size={44} label={getInitials(item)} style={styles.avatar} />
                <View style={styles.userTitleWrap}>
                  <Text variant="titleMedium">{item.fullName || item.username}</Text>
                  <Text variant="bodySmall" style={styles.secondaryText}>
                    {item.username}
                  </Text>
                </View>
              </View>
              <View style={styles.cardMenuActions}>
                <Chip compact style={item.status === 1 ? styles.activeChip : styles.inactiveChip}>
                  {item.status === 1 ? t("statuses.active") : t("statuses.disabled")}
                </Chip>
                <IconButton
                  icon="dots-vertical"
                  size={20}
                  accessibilityLabel={t("actions.more")}
                  onPress={() => openEditDialog(item)}
                />
              </View>
            </View>

            <View style={styles.metaWrap}>
              <Text variant="bodyMedium">{t("fields.positionValue")}</Text>
              {storeName ? <Text variant="bodyMedium">{t("fields.storeValue", { value: storeName })}</Text> : null}
              <Text variant="bodyMedium">{t("fields.ageValue", { value: age ?? emptyValue })}</Text>
              <Text variant="bodyMedium">{t("fields.genderValue", { value: gender })}</Text>
              <Text variant="bodyMedium">{t("fields.employmentTypeValue", { value: employmentType })}</Text>
              <Text variant="bodyMedium">{t("fields.phoneValue", { value: item.phone || emptyValue })}</Text>
              {item.email ? <Text variant="bodyMedium">{t("fields.emailValue", { value: item.email })}</Text> : null}
              {lastLogin ? (
                <Text variant="bodySmall" style={styles.secondaryText}>
                  {t("fields.lastLoginValue", { value: lastLogin })}
                </Text>
              ) : null}
              {updatedAt ? (
                <Text variant="bodySmall" style={styles.secondaryText}>
                  {t("fields.updatedAtValue", { value: updatedAt })}
                </Text>
              ) : null}
            </View>

            <View style={styles.actionRow}>
              <Button compact mode="outlined" icon="account-details-outline" onPress={() => openStaffDetail(item)}>
                {t("actions.viewDetails")}
              </Button>
              <Button compact mode="outlined" icon="pencil-outline" onPress={() => openEditDialog(item)}>
                {t("actions.edit")}
              </Button>
              <Button compact mode="outlined" icon="lock-reset" onPress={() => openResetPasswordDialog(item)}>
                {t("actions.resetPassword")}
              </Button>
              <Button
                compact
                mode={item.status === 1 ? "outlined" : "contained-tonal"}
                icon={item.status === 1 ? "pause-circle-outline" : "play-circle-outline"}
                onPress={() => handleToggleStatus(item)}
              >
                {item.status === 1 ? t("actions.disable") : t("actions.enable")}
              </Button>
            </View>
          </Card.Content>
        </Card>
      );
    },
    [
      handleToggleStatus,
      language,
      managedStore?.storeName,
      managedStoreCode,
      openEditDialog,
      openResetPasswordDialog,
      openStaffDetail,
      t,
    ]
  );

  if (!canManageUsers) {
    return (
      <SafeAreaView style={styles.screen} edges={["top", "left", "right"]}>
        <EmptyState
          title={t("messages.noAccessTitle")}
          description={t("messages.noAccessDescription")}
          primaryAction={{
            label: t("common:actions.goToSettings"),
            icon: "cog-outline",
            onPress: () => router.navigate("/(tabs)/settings"),
          }}
        />
      </SafeAreaView>
    );
  }

  return (
    <SafeAreaView style={styles.screen} edges={["top", "left", "right"]}>
      <FlatList
        data={filteredUsers}
        keyExtractor={(item) => item.userGUID}
        renderItem={renderUserCard}
        refreshControl={
          <RefreshControl refreshing={usersQuery.isFetching && !usersQuery.isLoading} onRefresh={handleRefresh} />
        }
        contentContainerStyle={styles.listContent}
        ListHeaderComponent={
          <View style={styles.headerWrap}>
            <View style={styles.titleRow}>
              <View>
                <Text variant="headlineSmall">{t("title")}</Text>
                <Text variant="bodyMedium" style={styles.secondaryText}>
                  {storeCaption}
                </Text>
              </View>
              <Button mode="contained" icon="account-plus-outline" onPress={openCreateDialog} disabled={!managedStoreCode}>
                {t("actions.create")}
              </Button>
            </View>

            <View style={styles.filterPanel}>
              <Button
                mode="outlined"
                icon="store-outline"
                onPress={() => setStorePickerVisible(true)}
                contentStyle={styles.storePickerButtonContent}
              >
                {managedStore?.storeName || t("currentStore.allRelated")}
              </Button>
              <Searchbar
                placeholder={t("searchPlaceholder")}
                value={keywordInput}
                onChangeText={setKeywordInput}
                onIconPress={submitKeyword}
                onSubmitEditing={submitKeyword}
                style={styles.searchbar}
              />
              <View style={styles.filterActions}>
                <Chip icon="sort-alphabetical-ascending" compact>
                  {t("filters.sortByName")}
                </Chip>
                <Button mode="outlined" icon="refresh" onPress={handleRefresh} disabled={usersQuery.isFetching}>
                  {t("actions.refresh")}
                </Button>
              </View>
              <SegmentedButtons
                value={statusFilter}
                onValueChange={(value) => setStatusFilter(value as StatusFilter)}
                buttons={[
                  { value: "all", label: t("filters.statusAll") },
                  { value: "active", label: t("filters.statusActive") },
                  { value: "disabled", label: t("filters.statusDisabled") },
                ]}
              />
            </View>

            {usersQuery.isError ? (
              <EmptyState
                title={t("messages.loadFailedTitle")}
                description={resolveLocalizedErrorMessage(usersQuery.error, {
                  t,
                  language,
                  fallbackKey: "messages.loadFailedDescription",
                })}
                primaryAction={{
                  label: t("common:actions.retry"),
                  icon: "refresh",
                  onPress: () => void handleRefresh(),
                }}
              />
            ) : null}

            {!usersQuery.isLoading && !usersQuery.isError && filteredUsers.length === 0 ? (
              <EmptyState
                title={keyword ? t("messages.emptySearchTitle") : t("messages.emptyTitle")}
                description={
                  keyword ? t("messages.emptySearchDescription", { keyword }) : t("messages.emptyDescription")
                }
              />
            ) : null}
          </View>
        }
        ListFooterComponent={
          usersQuery.isLoading ? (
            <View style={styles.loadingWrap}>
              <ActivityIndicator />
            </View>
          ) : null
        }
      />

      <FAB
        icon="account-plus-outline"
        label={t("actions.create")}
        style={styles.fab}
        onPress={openCreateDialog}
        disabled={!managedStoreCode || isBusy}
      />

      <Portal>
        <Dialog visible={dialogVisible} onDismiss={resetDialogState}>
          <Dialog.Title>
            {dialogMode === "edit" ? t("dialogs.editTitle") : t("dialogs.createTitle")}
          </Dialog.Title>
          <Dialog.ScrollArea>
            <ScrollView contentContainerStyle={styles.dialogContent}>
              <TextInput
                mode="outlined"
                label={t("fields.username")}
                value={formValues.username}
                onChangeText={(value) => setFormValues((current) => ({ ...current, username: value }))}
                autoCapitalize="none"
                disabled={dialogMode === "edit" || isBusy}
              />
              <TextInput
                mode="outlined"
                label={t("fields.fullName")}
                value={formValues.fullName}
                onChangeText={(value) => setFormValues((current) => ({ ...current, fullName: value }))}
                disabled={isBusy}
              />
              <TextInput
                mode="outlined"
                label={t("fields.email")}
                value={formValues.email}
                onChangeText={(value) => setFormValues((current) => ({ ...current, email: value }))}
                keyboardType="email-address"
                autoCapitalize="none"
                disabled={isBusy}
              />
              <TextInput
                mode="outlined"
                label={t("fields.phone")}
                value={formValues.phone}
                onChangeText={(value) => setFormValues((current) => ({ ...current, phone: value }))}
                keyboardType="phone-pad"
                disabled={isBusy}
              />
              {dialogMode === "create" ? (
                <TextInput
                  mode="outlined"
                  label={t("fields.initialPassword")}
                  value={formValues.password}
                  onChangeText={(value) => setFormValues((current) => ({ ...current, password: value }))}
                  secureTextEntry
                  autoCapitalize="none"
                  disabled={isBusy}
                />
              ) : null}
              <View style={styles.switchRow}>
                <Text variant="bodyLarge">{t("fields.enabled")}</Text>
                <Switch
                  value={formValues.status}
                  onValueChange={(value) => setFormValues((current) => ({ ...current, status: value }))}
                  disabled={isBusy}
                />
              </View>
              <Text variant="bodySmall" style={styles.secondaryText}>
                {t("fields.fixedRoleHint")}
              </Text>
              {dialogMode === "edit" && detailQuery.isFetching ? (
                <View style={styles.inlineLoading}>
                  <ActivityIndicator />
                </View>
              ) : null}
            </ScrollView>
          </Dialog.ScrollArea>
          <Dialog.Actions>
            <Button onPress={resetDialogState} disabled={isBusy}>
              {t("actions.cancel")}
            </Button>
            <Button onPress={handleSubmit} loading={createMutation.isPending || updateMutation.isPending}>
              {dialogMode === "edit" ? t("actions.save") : t("actions.create")}
            </Button>
          </Dialog.Actions>
        </Dialog>

        <Dialog visible={resetPasswordVisible} onDismiss={closeResetPasswordDialog}>
          <Dialog.Title>{t("dialogs.resetPasswordTitle")}</Dialog.Title>
          <Dialog.Content style={styles.dialogContent}>
            <Text variant="bodyMedium">
              {t("dialogs.resetPasswordDescription", { username: passwordUser?.username ?? "" })}
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
            <Button onPress={closeResetPasswordDialog} disabled={passwordMutation.isPending}>
              {t("actions.cancel")}
            </Button>
            <Button onPress={handleResetPassword} loading={passwordMutation.isPending}>
              {t("actions.confirmReset")}
            </Button>
          </Dialog.Actions>
        </Dialog>
      </Portal>

      <StorePickerModal
        visible={storePickerVisible}
        stores={manageableStores}
        selectedStoreCode={managedStoreCode}
        title={t("common:labels.selectStore")}
        cancelLabel={t("common:actions.cancel")}
        includeAllOption={!deviceBoundStoreCode}
        allLabel={t("currentStore.allRelated")}
        onDismiss={() => setStorePickerVisible(false)}
        onSelectStore={handleSelectManagedStore}
      />

      <Snackbar visible={Boolean(snackbarMessage)} onDismiss={() => setSnackbarMessage("")} duration={2500}>
        {snackbarMessage}
      </Snackbar>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  actionRow: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
  },
  activeChip: {
    backgroundColor: "#D1FAE5",
  },
  avatar: {
    backgroundColor: "#111827",
  },
  cardMenuActions: {
    alignItems: "flex-end",
    gap: 4,
  },
  dialogContent: {
    gap: 12,
    paddingBottom: 8,
  },
  fab: {
    bottom: 24,
    position: "absolute",
    right: 20,
  },
  filterActions: {
    alignItems: "center",
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 10,
    justifyContent: "space-between",
  },
  filterPanel: {
    backgroundColor: "#FFFFFF",
    borderColor: "#E5E7EB",
    borderRadius: 12,
    borderWidth: StyleSheet.hairlineWidth,
    gap: 10,
    padding: 12,
  },
  headerWrap: {
    gap: 12,
    marginBottom: 12,
  },
  identityRow: {
    alignItems: "center",
    flex: 1,
    flexDirection: "row",
    gap: 12,
  },
  inactiveChip: {
    backgroundColor: "#FEE2E2",
  },
  inlineLoading: {
    alignItems: "center",
    paddingVertical: 8,
  },
  listContent: {
    gap: 12,
    padding: 16,
    paddingBottom: 112,
  },
  loadingWrap: {
    alignItems: "center",
    paddingVertical: 24,
  },
  metaWrap: {
    gap: 4,
  },
  screen: {
    backgroundColor: "#F5F7FB",
    flex: 1,
  },
  searchbar: {
    backgroundColor: "#F8FAFC",
  },
  secondaryText: {
    color: "#6B7280",
  },
  storePickerButtonContent: {
    justifyContent: "flex-start",
  },
  switchRow: {
    alignItems: "center",
    flexDirection: "row",
    justifyContent: "space-between",
  },
  titleRow: {
    alignItems: "center",
    flexDirection: "row",
    gap: 12,
    justifyContent: "space-between",
  },
  userCard: {
    backgroundColor: "#FFFFFF",
    borderRadius: 12,
  },
  userCardContent: {
    gap: 12,
  },
  userCardHeader: {
    alignItems: "flex-start",
    flexDirection: "row",
    gap: 12,
    justifyContent: "space-between",
  },
  userTitleWrap: {
    flex: 1,
    gap: 2,
  },
});
