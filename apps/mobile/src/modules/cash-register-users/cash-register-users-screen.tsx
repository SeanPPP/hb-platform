import { useCallback, useEffect, useMemo, useState } from "react";
import { Alert, FlatList, RefreshControl, ScrollView, StyleSheet, View } from "react-native";
import { useRouter } from "expo-router";
import { SafeAreaView } from "react-native-safe-area-context";
import {
  ActivityIndicator,
  Button,
  Card,
  Chip,
  Dialog,
  Portal,
  Searchbar,
  SegmentedButtons,
  Snackbar,
  Switch,
  Text,
  TextInput,
  TouchableRipple,
} from "react-native-paper";
import { LabelPrinterSetupSheet } from "@/components/printer/LabelPrinterSetupSheet";
import { BUSINESS_UI } from "@/components/ui/business-ui";
import { EmptyState } from "@/components/ui/EmptyState";
import { StorePickerModal } from "@/components/ui/StorePickerModal";
import {
  resolveCashRegisterUserAccess,
  resolveEffectiveCashRegisterUserAccess,
} from "@/modules/cash-register-users/access";
import {
  generateCashRegisterBarcode,
  validateCashRegisterUserForm,
} from "@/modules/cash-register-users/barcode";
import { CashRegisterBarcodeSvg } from "@/modules/cash-register-users/CashRegisterBarcodeSvg";
import {
  useCashRegisterUserMutations,
  useCashRegisterUserOptions,
  useCashRegisterUserScope,
  useCashRegisterUsers,
} from "@/modules/cash-register-users/hooks";
import type {
  CashRegisterUserFormValues,
  CashRegisterUserListItem,
  CashRegisterUserStatusFilter,
  CashRegisterUserUserOption,
} from "@/modules/cash-register-users/types";
import { hydrateSavedPrinter, printCashRegisterUserBarcodeLabel } from "@/modules/printer/api";
import { PersonalCodePrintBusyError, runPersonalCodePrintExclusive } from "@/modules/printer/personal-code-print-lock";
import { usePrinterStore, type PrinterConnectionState } from "@/modules/printer/state";
import { isStoreManageable } from "@/modules/shop/store-scope";
import type { Store } from "@/modules/shop/types";
import { useStores } from "@/modules/shop/use-stores";
import { resolveLocalizedErrorMessage } from "@/shared/i18n/error-message";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { HB_COLORS, HB_SPACING } from "@/shared/theme/tokens";
import { useAuthStore } from "@/store/auth-store";

const EMPTY_FORM: CashRegisterUserFormValues = {
  storeCode: "",
  userGuid: "",
  operatorUser: "",
  userBarcode: "",
  loginRole: "2",
  remark: "",
  status: true,
};

function resolvePrinterChip(status: PrinterConnectionState, hasPrinter: boolean) {
  if (!hasPrinter) return { key: "printer.notConfigured", icon: "printer-off-outline", color: HB_COLORS.textSecondary };
  if (status === "connected") return { key: "printer.connected", icon: "printer-check", color: HB_COLORS.success };
  if (status === "connecting" || status === "reconnecting") {
    return { key: "printer.connecting", icon: "printer-outline", color: HB_COLORS.action };
  }
  return { key: "printer.disconnected", icon: "printer-alert", color: HB_COLORS.warning };
}

function displayUserName(option: Pick<CashRegisterUserUserOption, "username" | "userFullName">) {
  return option.userFullName ? `${option.userFullName} (@${option.username})` : `@${option.username}`;
}

export function CashRegisterUsersScreen() {
  const router = useRouter();
  const { t, language } = useAppTranslation(["cashRegisterUsers", "common"]);
  const accessControl = useAuthStore((state) => state.access);
  const { stores, isDeviceMode, isLoading: storesLoading } = useStores();
  const cachedAccess = useMemo(
    () => resolveCashRegisterUserAccess(accessControl, isDeviceMode),
    [accessControl, isDeviceMode]
  );
  // 管理范围与权限以服务端实时判定为准：仓库经理的分店接口不带 isPrimary，客户端无法自行推断；
  // 后台改了角色权限后，本页下拉刷新即可生效，无需重新登录。
  const scopeQuery = useCashRegisterUserScope(!isDeviceMode);
  const scope = scopeQuery.data;
  const access = useMemo(
    () => resolveEffectiveCashRegisterUserAccess(cachedAccess, scope, isDeviceMode),
    [cachedAccess, isDeviceMode, scope]
  );
  const isScopeAdmin = scope?.isAdmin === true;
  const manageableStores: Store[] = useMemo(() => scope?.manageableStores ?? [], [scope]);
  const [storeFilter, setStoreFilter] = useState<string | null>(null);
  const [storePickerTarget, setStorePickerTarget] = useState<"filter" | "form" | null>(null);
  const [keywordInput, setKeywordInput] = useState("");
  const [keyword, setKeyword] = useState("");
  const [statusFilter, setStatusFilter] = useState<CashRegisterUserStatusFilter>("all");
  const [snackbarMessage, setSnackbarMessage] = useState("");
  const [dialogVisible, setDialogVisible] = useState(false);
  const [editingItem, setEditingItem] = useState<CashRegisterUserListItem | null>(null);
  const [formValues, setFormValues] = useState<CashRegisterUserFormValues>(EMPTY_FORM);
  const [userPickerVisible, setUserPickerVisible] = useState(false);
  const [userKeyword, setUserKeyword] = useState("");
  const [printerSheetVisible, setPrinterSheetVisible] = useState(false);
  const [printingHGuid, setPrintingHGuid] = useState<string | null>(null);

  const printerStatus = usePrinterStore((state) => state.status);
  const savedPrinter = usePrinterStore((state) => state.savedPrinter);
  const printerHydrated = usePrinterStore((state) => state.hydrated);

  const listQuery = useCashRegisterUsers(access.canView, storeFilter, keyword, statusFilter);
  const userOptionsQuery = useCashRegisterUserOptions(access.canManage && dialogVisible);
  const { createMutation, updateMutation, printConfirmMutation } = useCashRegisterUserMutations();
  const isSaving = createMutation.isPending || updateMutation.isPending;

  const items = useMemo(() => listQuery.data?.pages.flatMap((page) => page.items) ?? [], [listQuery.data]);
  const total = listQuery.data?.pages[0]?.total ?? 0;
  const filterStore = stores.find((store) => store.storeCode === storeFilter) ?? null;
  const formStore = stores.find((store) => store.storeCode === formValues.storeCode) ?? null;
  const printerChip = resolvePrinterChip(printerStatus, Boolean(savedPrinter?.address));

  const filteredUserOptions = useMemo(() => {
    const options = userOptionsQuery.data ?? [];
    const needle = userKeyword.trim().toLowerCase();
    if (!needle) return options;
    return options.filter((option) =>
      `${option.username} ${option.userFullName}`.toLowerCase().includes(needle)
    );
  }, [userKeyword, userOptionsQuery.data]);
  const selectedUserOption = (userOptionsQuery.data ?? []).find((option) => option.userGuid === formValues.userGuid);

  useEffect(() => {
    if (!access.canPrint || printerHydrated) return;
    void hydrateSavedPrinter().catch(() => undefined);
  }, [access.canPrint, printerHydrated]);

  const errorText = useCallback(
    (error: unknown, fallbackKey: string) => resolveLocalizedErrorMessage(error, { t, language, fallbackKey }),
    [language, t]
  );

  const submitKeyword = useCallback(() => setKeyword(keywordInput.trim()), [keywordInput]);

  const openCreateDialog = useCallback(() => {
    const defaultStore = storeFilter && isStoreManageable(storeFilter, manageableStores)
      ? storeFilter
      : manageableStores[0]?.storeCode ?? "";
    setEditingItem(null);
    // 与 Web 一致：新建时预生成 13 位 EAN13 条码、默认收银员角色、默认启用。
    setFormValues({ ...EMPTY_FORM, storeCode: defaultStore, userBarcode: generateCashRegisterBarcode() });
    setDialogVisible(true);
  }, [manageableStores, storeFilter]);

  const openEditDialog = useCallback((item: CashRegisterUserListItem) => {
    setEditingItem(item);
    setFormValues({
      // 与 Web 一致：优先回填旧表 StoreCode，展示用分店可能是多个分店拼接。
      storeCode: item.legacyStoreCode || item.storeCode,
      userGuid: item.userGuid,
      operatorUser: item.operatorUser,
      userBarcode: item.userBarcode,
      loginRole: item.loginRole === "1" ? "1" : "2",
      remark: item.remark,
      status: item.status,
    });
    setDialogVisible(true);
  }, []);

  const closeDialog = useCallback(() => {
    if (isSaving) return;
    setDialogVisible(false);
    setEditingItem(null);
    setUserPickerVisible(false);
  }, [isSaving]);

  const handleSubmit = useCallback(async () => {
    const validationError = validateCashRegisterUserForm(formValues);
    if (validationError) {
      setSnackbarMessage(t(`validation.${validationError}`));
      return;
    }
    if (!isStoreManageable(formValues.storeCode, manageableStores) && !isScopeAdmin) {
      setSnackbarMessage(t("validation.storeNotManageable"));
      return;
    }
    try {
      if (editingItem) {
        await updateMutation.mutateAsync({ hGuid: editingItem.hGuid, values: formValues });
        setSnackbarMessage(t("messages.updated"));
      } else {
        await createMutation.mutateAsync(formValues);
        setSnackbarMessage(t("messages.created"));
      }
      setDialogVisible(false);
      setEditingItem(null);
    } catch (error) {
      setSnackbarMessage(errorText(error, "messages.saveFailed"));
    }
  }, [createMutation, editingItem, errorText, formValues, isScopeAdmin, manageableStores, t, updateMutation]);

  const handleToggleStatus = useCallback((item: CashRegisterUserListItem) => {
    const nextStatus = !item.status;
    Alert.alert(
      nextStatus ? t("dialogs.enableTitle") : t("dialogs.disableTitle"),
      t(nextStatus ? "dialogs.enableDescription" : "dialogs.disableDescription", {
        name: item.operatorUser || item.username,
      }),
      [
        { text: t("common:actions.cancel"), style: "cancel" },
        {
          text: nextStatus ? t("actions.enable") : t("actions.disable"),
          style: nextStatus ? "default" : "destructive",
          onPress: () => {
            void updateMutation
              .mutateAsync({
                hGuid: item.hGuid,
                values: {
                  storeCode: item.legacyStoreCode || item.storeCode,
                  userGuid: item.userGuid,
                  operatorUser: item.operatorUser,
                  userBarcode: item.userBarcode,
                  loginRole: item.loginRole === "1" ? "1" : "2",
                  remark: item.remark,
                  status: nextStatus,
                },
              })
              .then(() => setSnackbarMessage(nextStatus ? t("messages.enabled") : t("messages.disabled")))
              .catch((error) => setSnackbarMessage(errorText(error, "messages.saveFailed")));
          },
        },
      ]
    );
  }, [errorText, t, updateMutation]);

  const handlePrint = useCallback(async (item: CashRegisterUserListItem) => {
    if (!savedPrinter?.address) {
      setPrinterSheetVisible(true);
      return;
    }
    setPrintingHGuid(item.hGuid);
    let printed = false;
    try {
      // 与员工个人码共用打印互斥，拒绝连点造成的重复出纸。
      await runPersonalCodePrintExclusive(async () => {
        printed = await printCashRegisterUserBarcodeLabel({
          operatorName: item.operatorUser || item.userFullName || item.username,
          storeName: item.storeName || item.storeCode,
          barcode: item.userBarcode,
        });
        if (!printed) throw new Error("CASH_REGISTER_USER_PRINT_NOT_ACCEPTED");
      });
    } catch (error) {
      setPrintingHGuid(null);
      setSnackbarMessage(
        error instanceof PersonalCodePrintBusyError ? t("messages.printBusy") : errorText(error, "messages.printFailed")
      );
      return;
    }
    try {
      // 标签已出纸才计数；带上打印时的条码，换码或停用后的确认会被后端拒绝。
      await printConfirmMutation.mutateAsync({ hGuid: item.hGuid, userBarcode: item.userBarcode });
      setSnackbarMessage(t("messages.printed"));
    } catch (error) {
      setSnackbarMessage(t("messages.printCountFailed", { reason: errorText(error, "messages.printFailed") }));
    } finally {
      setPrintingHGuid(null);
    }
  }, [errorText, printConfirmMutation, savedPrinter?.address, t]);

  const renderItem = useCallback(({ item }: { item: CashRegisterUserListItem }) => {
    const linkedUser = item.username ? displayUserName(item) : t("card.noLinkedUser");
    const isPrinting = printingHGuid === item.hGuid;
    return (
      <Card mode="outlined" style={styles.card}>
        <Card.Content style={styles.cardContent}>
          <View style={styles.cardHeader}>
            <View style={styles.flex}>
              <Text variant="titleMedium" numberOfLines={1}>{item.operatorUser || item.username || "--"}</Text>
              <Text variant="bodySmall" style={styles.secondaryText} numberOfLines={1}>{linkedUser}</Text>
              <Text variant="bodySmall" style={styles.secondaryText} numberOfLines={1}>
                {item.storeName || item.storeCode || t("card.noStore")}
              </Text>
            </View>
            <View style={styles.chips}>
              <Chip compact style={item.status ? styles.activeChip : styles.inactiveChip} textStyle={styles.chipText}>
                {item.status ? t("status.active") : t("status.disabled")}
              </Chip>
              <Chip compact style={styles.roleChip} textStyle={styles.chipText}>
                {item.loginRole === "1" ? t("roles.admin") : item.loginRole === "2" ? t("roles.cashier") : item.loginRole || "--"}
              </Chip>
            </View>
          </View>
          <CashRegisterBarcodeSvg value={item.userBarcode} />
          <Text variant="bodySmall" style={styles.secondaryText}>
            {t("card.printCount", { count: item.printCount })}
            {item.remark ? ` · ${item.remark}` : ""}
          </Text>
        </Card.Content>
        {access.canManage || access.canPrint ? (
          <Card.Actions style={styles.cardActions}>
            {access.canManage ? (
              <Button compact mode="text" onPress={() => handleToggleStatus(item)} disabled={updateMutation.isPending}>
                {item.status ? t("actions.disable") : t("actions.enable")}
              </Button>
            ) : null}
            {access.canManage ? (
              <Button compact mode="outlined" icon="pencil-outline" onPress={() => openEditDialog(item)}>
                {t("actions.edit")}
              </Button>
            ) : null}
            {access.canPrint ? (
              <Button
                compact
                mode="contained-tonal"
                icon="printer-outline"
                onPress={() => void handlePrint(item)}
                loading={isPrinting}
                disabled={!item.status || !item.userBarcode || Boolean(printingHGuid)}
              >
                {t("actions.print")}
              </Button>
            ) : null}
          </Card.Actions>
        ) : null}
      </Card>
    );
  }, [access.canManage, access.canPrint, handlePrint, handleToggleStatus, openEditDialog, printingHGuid, t, updateMutation.isPending]);

  if (!access.canView) {
    if (scopeQuery.isLoading) {
      return (
        <SafeAreaView style={[styles.screen, styles.centered]} edges={["top", "left", "right"]}>
          <ActivityIndicator />
        </SafeAreaView>
      );
    }
    return (
      <SafeAreaView style={styles.screen} edges={["top", "left", "right"]}>
        <EmptyState
          title={t("messages.noAccessTitle")}
          description={t("messages.noAccessDescription")}
          // 后台刚授权时就地重新检查服务端权限，不必重新登录。
          primaryAction={
            isDeviceMode
              ? undefined
              : { label: t("actions.recheckAccess"), icon: "refresh", onPress: () => void scopeQuery.refetch() }
          }
          secondaryAction={{
            label: t("common:actions.goToSettings"),
            icon: "cog-outline",
            onPress: () => router.navigate("/(shell)/settings"),
          }}
        />
      </SafeAreaView>
    );
  }

  return (
    <SafeAreaView style={styles.screen} edges={["top", "left", "right"]}>
      <FlatList
        data={items}
        keyExtractor={(item) => item.hGuid}
        renderItem={renderItem}
        contentContainerStyle={styles.listContent}
        refreshControl={
          <RefreshControl
            refreshing={listQuery.isRefetching && !listQuery.isFetchingNextPage}
            onRefresh={() => {
              // 同时刷新服务端权限与范围，后台刚授权/撤权时无需重新登录。
              void scopeQuery.refetch();
              void listQuery.refetch();
            }}
          />
        }
        onEndReachedThreshold={0.4}
        onEndReached={() => {
          if (listQuery.hasNextPage && !listQuery.isFetchingNextPage) void listQuery.fetchNextPage();
        }}
        ListHeaderComponent={
          <View style={styles.headerWrap}>
            <View style={styles.titleRow}>
              <View style={styles.flex}>
                <Text variant="headlineSmall">{t("title")}</Text>
                <Text variant="bodySmall" style={styles.secondaryText}>
                  {listQuery.data ? t("subtitle", { count: total }) : t("subtitleLoading")}
                </Text>
              </View>
              {access.canManage ? (
                <Button
                  compact
                  mode="contained"
                  icon="plus"
                  onPress={openCreateDialog}
                  disabled={manageableStores.length === 0}
                >
                  {t("actions.create")}
                </Button>
              ) : null}
            </View>
            <View style={styles.filterPanel}>
              <View style={styles.filterRow}>
                <Button
                  mode="outlined"
                  icon="store-outline"
                  onPress={() => setStorePickerTarget("filter")}
                  style={styles.flex}
                  disabled={storesLoading}
                >
                  {filterStore?.storeName || t("filters.allStores")}
                </Button>
                {access.canPrint ? (
                  <Chip
                    compact
                    icon={printerChip.icon}
                    textStyle={{ color: printerChip.color }}
                    onPress={() => setPrinterSheetVisible(true)}
                    accessibilityHint={t("printer.openSettingsHint")}
                  >
                    {t(printerChip.key)}
                  </Chip>
                ) : null}
              </View>
              <Searchbar
                placeholder={t("filters.searchPlaceholder")}
                value={keywordInput}
                onChangeText={setKeywordInput}
                onIconPress={submitKeyword}
                onSubmitEditing={submitKeyword}
                onClearIconPress={() => setKeyword("")}
                style={styles.searchbar}
                inputStyle={styles.searchInput}
              />
              <SegmentedButtons
                value={statusFilter}
                onValueChange={(value) => setStatusFilter(value as CashRegisterUserStatusFilter)}
                buttons={[
                  { value: "all", label: t("filters.statusAll") },
                  { value: "active", label: t("status.active") },
                  { value: "disabled", label: t("status.disabled") },
                ]}
                theme={{ colors: { secondaryContainer: "#E8F1FF", onSecondaryContainer: HB_COLORS.action } }}
              />
              {access.canManage && scope && !isScopeAdmin && manageableStores.length === 0 ? (
                // 后端只按主分店（isPrimary）授权管理范围；没有主分店时说明为何不能新建、列表为空。
                <Text variant="bodySmall" style={styles.readOnlyHint}>{t("messages.noManageableStore")}</Text>
              ) : null}
            </View>
            {listQuery.isError ? (
              <EmptyState
                title={t("messages.loadFailedTitle")}
                description={errorText(listQuery.error, "messages.loadFailedDescription")}
                primaryAction={{ label: t("common:actions.retry"), icon: "refresh", onPress: () => void listQuery.refetch() }}
              />
            ) : null}
            {!listQuery.isLoading && !listQuery.isError && items.length === 0 ? (
              <EmptyState title={t("messages.emptyTitle")} description={t("messages.emptyDescription")} />
            ) : null}
          </View>
        }
        ListFooterComponent={
          listQuery.isLoading || listQuery.isFetchingNextPage ? (
            <View style={styles.loadingWrap}>
              <ActivityIndicator />
            </View>
          ) : null
        }
      />

      <Portal>
        <Dialog visible={dialogVisible} onDismiss={closeDialog}>
          <Dialog.Title>{editingItem ? t("dialogs.editTitle") : t("dialogs.createTitle")}</Dialog.Title>
          <Dialog.ScrollArea>
            <ScrollView contentContainerStyle={styles.dialogContent} keyboardShouldPersistTaps="handled">
              <Button
                mode="outlined"
                icon="store-outline"
                onPress={() => setStorePickerTarget("form")}
                disabled={isSaving}
                contentStyle={styles.fieldButtonContent}
              >
                {formStore?.storeName || formValues.storeCode || t("fields.storePlaceholder")}
              </Button>
              <Button
                mode="outlined"
                icon="account-outline"
                onPress={() => {
                  setUserKeyword("");
                  setUserPickerVisible(true);
                }}
                disabled={isSaving}
                contentStyle={styles.fieldButtonContent}
              >
                {selectedUserOption
                  ? displayUserName(selectedUserOption)
                  : editingItem?.userGuid === formValues.userGuid && editingItem.username
                    ? displayUserName(editingItem)
                    : t("fields.userPlaceholder")}
              </Button>
              <TextInput
                mode="outlined"
                label={t("fields.operatorUser")}
                value={formValues.operatorUser}
                onChangeText={(value) => setFormValues((current) => ({ ...current, operatorUser: value }))}
                maxLength={100}
                disabled={isSaving}
              />
              <View style={styles.barcodeRow}>
                <TextInput
                  mode="outlined"
                  label={t("fields.userBarcode")}
                  value={formValues.userBarcode}
                  onChangeText={(value) => setFormValues((current) => ({ ...current, userBarcode: value.trim() }))}
                  keyboardType="number-pad"
                  maxLength={13}
                  disabled={isSaving}
                  style={styles.flex}
                />
                <Button
                  compact
                  icon="refresh"
                  disabled={isSaving}
                  onPress={() => setFormValues((current) => ({ ...current, userBarcode: generateCashRegisterBarcode() }))}
                >
                  {editingItem ? t("actions.regenerate") : t("actions.generate")}
                </Button>
              </View>
              {formValues.userBarcode ? <CashRegisterBarcodeSvg value={formValues.userBarcode} /> : null}
              <Text variant="labelLarge">{t("fields.loginRole")}</Text>
              <SegmentedButtons
                value={formValues.loginRole}
                onValueChange={(value) =>
                  setFormValues((current) => ({ ...current, loginRole: value === "1" ? "1" : "2" }))
                }
                buttons={[
                  { value: "2", label: t("roles.cashier"), disabled: isSaving },
                  { value: "1", label: t("roles.admin"), disabled: isSaving },
                ]}
              />
              <TextInput
                mode="outlined"
                label={t("fields.remark")}
                value={formValues.remark}
                onChangeText={(value) => setFormValues((current) => ({ ...current, remark: value }))}
                maxLength={500}
                multiline
                disabled={isSaving}
              />
              <View style={styles.switchRow}>
                <Text variant="bodyLarge">{t("fields.enabled")}</Text>
                <Switch
                  value={formValues.status}
                  onValueChange={(value) => setFormValues((current) => ({ ...current, status: value }))}
                  disabled={isSaving}
                />
              </View>
              <Text variant="bodySmall" style={styles.secondaryText}>{t("fields.singleActiveHint")}</Text>
            </ScrollView>
          </Dialog.ScrollArea>
          <Dialog.Actions>
            <Button onPress={closeDialog} disabled={isSaving}>{t("common:actions.cancel")}</Button>
            <Button mode="contained" onPress={() => void handleSubmit()} loading={isSaving} disabled={isSaving}>
              {editingItem ? t("actions.save") : t("actions.create")}
            </Button>
          </Dialog.Actions>
        </Dialog>

        <Dialog visible={userPickerVisible} onDismiss={() => setUserPickerVisible(false)}>
          <Dialog.Title>{t("dialogs.pickUserTitle")}</Dialog.Title>
          <Dialog.Content style={styles.pickerHeader}>
            <Searchbar
              placeholder={t("dialogs.pickUserSearch")}
              value={userKeyword}
              onChangeText={setUserKeyword}
              style={styles.searchbar}
              inputStyle={styles.searchInput}
            />
          </Dialog.Content>
          <Dialog.ScrollArea style={styles.pickerList}>
            {userOptionsQuery.isLoading ? (
              <View style={styles.loadingWrap}><ActivityIndicator /></View>
            ) : userOptionsQuery.isError ? (
              <Text style={styles.errorText}>{errorText(userOptionsQuery.error, "messages.userOptionsFailed")}</Text>
            ) : (
              <FlatList
                data={filteredUserOptions}
                keyExtractor={(option) => option.userGuid}
                keyboardShouldPersistTaps="handled"
                ListEmptyComponent={<Text style={styles.secondaryText}>{t("dialogs.pickUserEmpty")}</Text>}
                renderItem={({ item: option }) => (
                  <TouchableRipple
                    onPress={() => {
                      setFormValues((current) => ({
                        ...current,
                        userGuid: option.userGuid,
                        // 操作员为空时带出后台用户姓名，老收银界面显示的就是这个名字。
                        operatorUser: current.operatorUser.trim() ? current.operatorUser : option.userFullName || option.username,
                      }));
                      setUserPickerVisible(false);
                    }}
                    style={styles.pickerRow}
                  >
                    <View>
                      <Text variant="bodyLarge">{option.userFullName || option.username}</Text>
                      <Text variant="bodySmall" style={styles.secondaryText}>@{option.username}</Text>
                    </View>
                  </TouchableRipple>
                )}
              />
            )}
          </Dialog.ScrollArea>
          <Dialog.Actions>
            <Button onPress={() => setUserPickerVisible(false)}>{t("common:actions.cancel")}</Button>
          </Dialog.Actions>
        </Dialog>
      </Portal>

      <StorePickerModal
        visible={storePickerTarget !== null}
        stores={storePickerTarget === "form" ? manageableStores : stores}
        selectedStoreCode={storePickerTarget === "form" ? formValues.storeCode : storeFilter}
        title={t("filters.pickStore")}
        cancelLabel={t("common:actions.cancel")}
        includeAllOption={storePickerTarget === "filter"}
        allLabel={t("filters.allStores")}
        onDismiss={() => setStorePickerTarget(null)}
        onSelectStore={(store) => {
          if (storePickerTarget === "form") {
            setFormValues((current) => ({ ...current, storeCode: store?.storeCode ?? current.storeCode }));
          } else {
            setStoreFilter(store?.storeCode ?? null);
          }
          setStorePickerTarget(null);
        }}
      />

      <LabelPrinterSetupSheet visible={printerSheetVisible} onDismiss={() => setPrinterSheetVisible(false)} />

      <Snackbar visible={Boolean(snackbarMessage)} onDismiss={() => setSnackbarMessage("")} duration={3500}>
        {snackbarMessage}
      </Snackbar>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  activeChip: { backgroundColor: "#D1FAE5" },
  barcodeRow: { alignItems: "center", flexDirection: "row", gap: 4 },
  card: { backgroundColor: HB_COLORS.surface },
  centered: { alignItems: "center", justifyContent: "center" },
  cardActions: { flexWrap: "wrap", justifyContent: "flex-end" },
  cardContent: { gap: 8 },
  cardHeader: { alignItems: "flex-start", flexDirection: "row", gap: 8 },
  chipText: { fontSize: 12, marginVertical: 2 },
  chips: { alignItems: "flex-end", gap: 4 },
  dialogContent: { gap: 12, paddingBottom: 8, paddingTop: 8 },
  errorText: { color: HB_COLORS.danger, padding: HB_SPACING.md },
  fieldButtonContent: { justifyContent: "flex-start" },
  filterPanel: { ...BUSINESS_UI.filterGroup, borderWidth: StyleSheet.hairlineWidth, gap: 8, padding: 8 },
  filterRow: { alignItems: "center", flexDirection: "row", gap: 8 },
  flex: { flex: 1 },
  headerWrap: { gap: 8, marginBottom: 8 },
  inactiveChip: { backgroundColor: "#FEE2E2" },
  listContent: { gap: 8, padding: HB_SPACING.sm, paddingBottom: 112 },
  loadingWrap: { alignItems: "center", paddingVertical: 24 },
  pickerHeader: { paddingBottom: 8 },
  pickerList: { maxHeight: 360, paddingHorizontal: 0 },
  pickerRow: { paddingHorizontal: 24, paddingVertical: 10 },
  readOnlyHint: { color: HB_COLORS.warning },
  roleChip: { backgroundColor: "#E8F1FF" },
  screen: { backgroundColor: HB_COLORS.background, flex: 1 },
  searchbar: { backgroundColor: "#F8FAFC", height: 44 },
  searchInput: { minHeight: 44 },
  secondaryText: { color: HB_COLORS.textSecondary },
  switchRow: { alignItems: "center", flexDirection: "row", justifyContent: "space-between" },
  titleRow: { alignItems: "center", flexDirection: "row", gap: 8 },
});
