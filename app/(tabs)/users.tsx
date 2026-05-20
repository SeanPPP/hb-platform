import { useCallback, useEffect, useMemo, useState } from "react";
import { Alert, FlatList, RefreshControl, ScrollView, StyleSheet, View } from "react-native";
import {
  ActivityIndicator,
  Button,
  Card,
  Chip,
  Dialog,
  FAB,
  Portal,
  Searchbar,
  Snackbar,
  Switch,
  Text,
  TextInput,
} from "react-native-paper";
import { EmptyState } from "@/components/ui/EmptyState";
import { useStores } from "@/modules/shop/use-stores";
import {
  STORE_STAFF_ROLE,
  useStoreUserDetail,
  useStoreUserMutations,
  useStoreUsers,
  type StoreUserFormValues,
  type StoreUserListItem,
} from "@/modules/users";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { useAuthStore } from "@/store/auth-store";

type UserDialogMode = "create" | "edit";

const EMPTY_FORM: StoreUserFormValues = {
  username: "",
  fullName: "",
  email: "",
  phone: "",
  password: "",
  status: true,
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

export default function UsersScreen() {
  const { t, language } = useAppTranslation("userManagement");
  const access = useAuthStore((state) => state.access);
  const { selectedStore, selectedStoreCode, isHydratingSelection } = useStores();
  const [keywordInput, setKeywordInput] = useState("");
  const [keyword, setKeyword] = useState("");
  const [snackbarMessage, setSnackbarMessage] = useState("");
  const [dialogVisible, setDialogVisible] = useState(false);
  const [dialogMode, setDialogMode] = useState<UserDialogMode>("create");
  const [editingUserGuid, setEditingUserGuid] = useState<string | null>(null);
  const [formValues, setFormValues] = useState<StoreUserFormValues>(EMPTY_FORM);
  const [resetPasswordVisible, setResetPasswordVisible] = useState(false);
  const [resetPasswordValue, setResetPasswordValue] = useState("");
  const [passwordUser, setPasswordUser] = useState<StoreUserListItem | null>(null);

  const canManageUsers =
    access.isAdmin ||
    access.isStoreManager ||
    access.isStoreLevelManager ||
    (access.canReadUser && access.canWriteUser);

  const usersQuery = useStoreUsers(selectedStoreCode, keyword);
  const detailQuery = useStoreUserDetail(
    dialogMode === "edit" ? editingUserGuid : null,
    dialogMode === "edit" ? selectedStoreCode : null
  );
  const { createMutation, updateMutation, statusMutation, passwordMutation } =
    useStoreUserMutations(selectedStoreCode, keyword);

  const activeMutationCount =
    (createMutation.isPending ? 1 : 0) +
    (updateMutation.isPending ? 1 : 0) +
    (statusMutation.isPending ? 1 : 0) +
    (passwordMutation.isPending ? 1 : 0);
  const isBusy = activeMutationCount > 0;

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
    setFormValues(EMPTY_FORM);
  }, []);

  const openCreateDialog = useCallback(() => {
    setDialogMode("create");
    setEditingUserGuid(null);
    setFormValues(EMPTY_FORM);
    setDialogVisible(true);
  }, []);

  const openEditDialog = useCallback((user: StoreUserListItem) => {
    setDialogMode("edit");
    setEditingUserGuid(user.userGUID);
    setFormValues({
      username: user.username,
      fullName: user.fullName ?? "",
      email: user.email ?? "",
      phone: user.phone ?? "",
      password: "",
      status: user.status === 1,
    });
    setDialogVisible(true);
  }, []);

  const closeResetPasswordDialog = useCallback(() => {
    setPasswordUser(null);
    setResetPasswordValue("");
    setResetPasswordVisible(false);
  }, []);

  const submitKeyword = useCallback(() => {
    setKeyword(keywordInput.trim());
  }, [keywordInput]);

  const handleRefresh = useCallback(async () => {
    try {
      await usersQuery.refetch();
    } catch (error) {
      console.error("[store-users] refresh failed", error);
      setSnackbarMessage(error instanceof Error ? error.message : t("messages.refreshFailed"));
    }
  }, [t, usersQuery]);

  const validateForm = useCallback(() => {
    if (!formValues.username.trim()) {
      setSnackbarMessage(t("messages.usernameRequired"));
      return false;
    }

    if (dialogMode === "create" && !formValues.password.trim()) {
      setSnackbarMessage(t("messages.passwordRequired"));
      return false;
    }

    return true;
  }, [dialogMode, formValues.password, formValues.username, t]);

  const handleSubmit = useCallback(async () => {
    if (!selectedStoreCode) {
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
      storeCode: selectedStoreCode,
      roleNames: [STORE_STAFF_ROLE],
    };

    try {
      if (dialogMode === "edit" && editingUserGuid) {
        await updateMutation.mutateAsync({
          ...payload,
          userGuid: editingUserGuid,
        });
        setSnackbarMessage(t("messages.userUpdated"));
      } else {
        await createMutation.mutateAsync(payload);
        setSnackbarMessage(t("messages.userCreated"));
      }

      resetDialogState();
    } catch (error) {
      console.error("[store-users] save failed", error);
      setSnackbarMessage(error instanceof Error ? error.message : t("messages.saveFailed"));
    }
  }, [
    createMutation,
    dialogMode,
    editingUserGuid,
    formValues.email,
    formValues.fullName,
    formValues.password,
    formValues.phone,
    formValues.status,
    formValues.username,
    resetDialogState,
    selectedStoreCode,
    t,
    updateMutation,
    validateForm,
  ]);

  const handleToggleStatus = useCallback(
    (user: StoreUserListItem) => {
      if (!selectedStoreCode) {
        setSnackbarMessage(t("messages.selectStoreFirst"));
        return;
      }

      const nextEnabled = user.status !== 1;
      const actionLabel = nextEnabled ? t("actions.enable") : t("actions.disable");
      Alert.alert(
        actionLabel,
        t("dialogs.statusConfirmMessage", {
          action: actionLabel,
          username: user.username,
        }),
        [
          { text: t("actions.cancel"), style: "cancel" },
          {
            text: actionLabel,
            style: nextEnabled ? "default" : "destructive",
            onPress: async () => {
              try {
                await statusMutation.mutateAsync({
                  userGuid: user.userGUID,
                  storeCode: selectedStoreCode,
                  status: nextEnabled ? 1 : 0,
                });
                setSnackbarMessage(
                  nextEnabled ? t("messages.userEnabled") : t("messages.userDisabled")
                );
              } catch (error) {
                console.error("[store-users] status failed", error);
                setSnackbarMessage(
                  error instanceof Error ? error.message : t("messages.statusFailed")
                );
              }
            },
          },
        ]
      );
    },
    [selectedStoreCode, statusMutation, t]
  );

  const openResetPasswordDialog = useCallback((user: StoreUserListItem) => {
    setPasswordUser(user);
    setResetPasswordValue("");
    setResetPasswordVisible(true);
  }, []);

  const handleResetPassword = useCallback(async () => {
    if (!selectedStoreCode || !passwordUser) {
      setSnackbarMessage(t("messages.selectStoreFirst"));
      return;
    }

    if (!resetPasswordValue.trim()) {
      setSnackbarMessage(t("messages.passwordRequired"));
      return;
    }

    try {
      await passwordMutation.mutateAsync({
        userGuid: passwordUser.userGUID,
        storeCode: selectedStoreCode,
        newPassword: resetPasswordValue.trim(),
      });
      setSnackbarMessage(t("messages.passwordReset"));
      closeResetPasswordDialog();
    } catch (error) {
      console.error("[store-users] password reset failed", error);
      setSnackbarMessage(error instanceof Error ? error.message : t("messages.passwordResetFailed"));
    }
  }, [
    closeResetPasswordDialog,
    passwordMutation,
    passwordUser,
    resetPasswordValue,
    selectedStoreCode,
    t,
  ]);

  const renderedUsers = usersQuery.data ?? [];
  const storeCaption = useMemo(() => {
    if (!selectedStore) {
      return t("currentStore.empty");
    }

    return t("currentStore.value", {
      code: selectedStore.storeCode,
      name: selectedStore.storeName || selectedStore.storeCode,
    });
  }, [selectedStore, t]);

  const renderUserCard = useCallback(
    ({ item }: { item: StoreUserListItem }) => {
      const lastLogin = formatDateTime(item.lastLoginTime, language);
      const updatedAt = formatDateTime(item.updatedAt, language);
      return (
        <Card style={styles.userCard} mode="outlined">
          <Card.Content style={styles.userCardContent}>
            <View style={styles.userCardHeader}>
              <View style={styles.userTitleWrap}>
                <Text variant="titleMedium">{item.fullName || item.username}</Text>
                <Text variant="bodySmall" style={styles.secondaryText}>
                  {item.username}
                </Text>
              </View>
              <Chip compact style={item.status === 1 ? styles.activeChip : styles.inactiveChip}>
                {item.status === 1 ? t("statuses.active") : t("statuses.disabled")}
              </Chip>
            </View>

            <View style={styles.metaWrap}>
              <Text variant="bodyMedium">{t("fields.roleValue", { role: STORE_STAFF_ROLE })}</Text>
              {item.email ? <Text variant="bodyMedium">{t("fields.emailValue", { value: item.email })}</Text> : null}
              {item.phone ? <Text variant="bodyMedium">{t("fields.phoneValue", { value: item.phone })}</Text> : null}
              {lastLogin ? <Text variant="bodySmall" style={styles.secondaryText}>{t("fields.lastLoginValue", { value: lastLogin })}</Text> : null}
              {updatedAt ? <Text variant="bodySmall" style={styles.secondaryText}>{t("fields.updatedAtValue", { value: updatedAt })}</Text> : null}
            </View>

            <View style={styles.actionRow}>
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
    [handleToggleStatus, language, openEditDialog, openResetPasswordDialog, t]
  );

  if (!canManageUsers) {
    return (
      <View style={styles.screen}>
        <EmptyState
          title={t("messages.noAccessTitle")}
          description={t("messages.noAccessDescription")}
        />
      </View>
    );
  }

  return (
    <View style={styles.screen}>
      <FlatList
        data={renderedUsers}
        keyExtractor={(item) => item.userGUID}
        renderItem={renderUserCard}
        refreshControl={
          <RefreshControl
            refreshing={usersQuery.isFetching && !usersQuery.isLoading}
            onRefresh={handleRefresh}
          />
        }
        contentContainerStyle={styles.listContent}
        ListHeaderComponent={
          <View style={styles.headerWrap}>
            <Text variant="headlineSmall">{t("title")}</Text>
            <Card mode="outlined">
              <Card.Content style={styles.currentStoreCard}>
                <Text variant="titleMedium">{t("currentStore.label")}</Text>
                <Text variant="bodyMedium" style={styles.currentStoreValue}>
                  {storeCaption}
                </Text>
                <Text variant="bodySmall" style={styles.secondaryText}>
                  {t("currentStore.helper")}
                </Text>
              </Card.Content>
            </Card>

            <Card mode="outlined">
              <Card.Content style={styles.toolbarCard}>
                <Searchbar
                  placeholder={t("searchPlaceholder")}
                  value={keywordInput}
                  onChangeText={setKeywordInput}
                  onIconPress={submitKeyword}
                  onSubmitEditing={submitKeyword}
                  style={styles.searchbar}
                />
                <View style={styles.toolbarActions}>
                  <Button mode="outlined" icon="refresh" onPress={handleRefresh} disabled={!selectedStoreCode}>
                    {t("actions.refresh")}
                  </Button>
                  <Button mode="contained" icon="account-plus-outline" onPress={openCreateDialog} disabled={!selectedStoreCode}>
                    {t("actions.create")}
                  </Button>
                </View>
              </Card.Content>
            </Card>

            {!selectedStoreCode && !isHydratingSelection ? (
              <EmptyState
                title={t("messages.selectStoreTitle")}
                description={t("messages.selectStoreDescription")}
              />
            ) : null}

            {selectedStoreCode && usersQuery.isError ? (
              <EmptyState
                title={t("messages.loadFailedTitle")}
                description={
                  usersQuery.error instanceof Error
                    ? usersQuery.error.message
                    : t("messages.loadFailedDescription")
                }
              />
            ) : null}

            {selectedStoreCode &&
            !usersQuery.isLoading &&
            !usersQuery.isError &&
            renderedUsers.length === 0 ? (
              <EmptyState
                title={keyword ? t("messages.emptySearchTitle") : t("messages.emptyTitle")}
                description={
                  keyword
                    ? t("messages.emptySearchDescription", { keyword })
                    : t("messages.emptyDescription")
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
        disabled={!selectedStoreCode || isBusy}
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
                {t("fields.fixedRoleHint", { role: STORE_STAFF_ROLE })}
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
              {t("dialogs.resetPasswordDescription", {
                username: passwordUser?.username ?? "",
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
            <Button onPress={closeResetPasswordDialog} disabled={passwordMutation.isPending}>
              {t("actions.cancel")}
            </Button>
            <Button onPress={handleResetPassword} loading={passwordMutation.isPending}>
              {t("actions.confirmReset")}
            </Button>
          </Dialog.Actions>
        </Dialog>
      </Portal>

      <Snackbar visible={Boolean(snackbarMessage)} onDismiss={() => setSnackbarMessage("")} duration={2500}>
        {snackbarMessage}
      </Snackbar>
    </View>
  );
}

const styles = StyleSheet.create({
  screen: {
    flex: 1,
    backgroundColor: "#f5f7fb",
  },
  listContent: {
    padding: 16,
    paddingBottom: 112,
    gap: 12,
  },
  headerWrap: {
    gap: 12,
    marginBottom: 12,
  },
  currentStoreCard: {
    gap: 6,
  },
  currentStoreValue: {
    color: "#1f2937",
  },
  toolbarCard: {
    gap: 12,
  },
  searchbar: {
    backgroundColor: "#fff",
  },
  toolbarActions: {
    flexDirection: "row",
    gap: 10,
    flexWrap: "wrap",
  },
  userCard: {
    backgroundColor: "#fff",
  },
  userCardContent: {
    gap: 12,
  },
  userCardHeader: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "flex-start",
    gap: 12,
  },
  userTitleWrap: {
    flex: 1,
    gap: 2,
  },
  activeChip: {
    backgroundColor: "#d1fae5",
  },
  inactiveChip: {
    backgroundColor: "#fee2e2",
  },
  metaWrap: {
    gap: 4,
  },
  actionRow: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
  },
  secondaryText: {
    color: "#6b7280",
  },
  loadingWrap: {
    paddingVertical: 24,
    alignItems: "center",
  },
  fab: {
    position: "absolute",
    right: 20,
    bottom: 24,
  },
  dialogContent: {
    gap: 12,
    paddingBottom: 8,
  },
  switchRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
  },
  inlineLoading: {
    paddingVertical: 8,
    alignItems: "center",
  },
});

