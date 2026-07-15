import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Alert, ScrollView, StyleSheet, View } from "react-native";
import {
  Stack,
  useLocalSearchParams,
  useNavigation,
  useRouter,
} from "expo-router";
import {
  usePreventRemove,
  type NavigationAction,
} from "@react-navigation/native";
import { SafeAreaView } from "react-native-safe-area-context";
import {
  ActivityIndicator,
  Button,
  Card,
  Chip,
  IconButton,
  Snackbar,
  Surface,
  Switch,
  Text,
} from "react-native-paper";
import { EmptyState } from "@/components/ui/EmptyState";
import {
  useRestoreStoreUserPosTerminalPermissions,
  useStoreUserPosTerminalPermissions,
  useUpdateStoreUserPosTerminalPermissions,
} from "@/modules/users/pos-terminal-permissions-hooks";
import {
  arePermissionCodeSetsEqual,
  buildGrantedPosPermissionCodes,
  buildPosPermissionDraft,
  classifyPosPermissionError,
  groupPosPermissions,
  setPosPermissionGroupSelection,
  shouldApplyPosPermissionResponse,
  shouldPreventPosPermissionRemoval,
  togglePosPermissionCode,
} from "@/modules/users/pos-terminal-permissions";
import type { PosPermissionErrorKind } from "@/modules/users/pos-terminal-permissions";
import type { StoreUserPosTerminalPermissions } from "@/modules/users/types";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { PERMISSIONS } from "@/shared/utils/access";
import { useAuthStore } from "@/store/auth-store";

function firstParam(value: string | string[] | undefined) {
  return Array.isArray(value) ? value[0] : value;
}

export default function PosTerminalPermissionsScreen() {
  const router = useRouter();
  const navigation = useNavigation();
  const { t } = useAppTranslation(["userManagement", "common"]);
  const params = useLocalSearchParams<{
    userGuid?: string | string[];
    storeGuid?: string | string[];
    userName?: string | string[];
    storeName?: string | string[];
  }>();
  const access = useAuthStore((state) => state.access);
  const currentUser = useAuthStore((state) => state.user);
  const userGuid = firstParam(params.userGuid)?.trim() ?? "";
  const storeGuid = firstParam(params.storeGuid)?.trim() ?? "";
  const userName = firstParam(params.userName)?.trim() || t("posPermissions.unknownUser");
  const storeName = firstParam(params.storeName)?.trim() || t("posPermissions.unknownStore");
  const hasRequiredParams = Boolean(userGuid && storeGuid);
  const isCurrentUser = Boolean(
    userGuid &&
      currentUser?.userGUID?.trim().toLowerCase() === userGuid.toLowerCase()
  );
  const canManage = access.hasPermission(
    PERMISSIONS.Users.ManagePosTerminalPermissions
  );
  const queryEnabled = hasRequiredParams && canManage && !isCurrentUser;
  const scopeKey = `${storeGuid}:${userGuid}`;

  const permissionsQuery = useStoreUserPosTerminalPermissions(
    userGuid,
    storeGuid,
    queryEnabled
  );
  const updateMutation = useUpdateStoreUserPosTerminalPermissions();
  const restoreMutation = useRestoreStoreUserPosTerminalPermissions();
  const [selectedCodes, setSelectedCodes] = useState<string[]>([]);
  const [baselineCodes, setBaselineCodes] = useState<string[]>([]);
  const [snackbarMessage, setSnackbarMessage] = useState("");
  const [allowRemove, setAllowRemove] = useState(false);
  const [terminalErrorKind, setTerminalErrorKind] = useState<
    Exclude<PosPermissionErrorKind, "network"> | null
  >(null);
  const initializedScopeKeyRef = useRef<string | null>(null);
  const appliedDataUpdatedAtRef = useRef(0);
  const operationInFlightRef = useRef(false);
  const pendingActionRef = useRef<NavigationAction | null>(null);
  const busy = updateMutation.isPending || restoreMutation.isPending;
  const dirty = !arePermissionCodeSetsEqual(selectedCodes, baselineCodes);

  const applyServerPermissions = useCallback(
    (
      permissions: StoreUserPosTerminalPermissions,
      dataUpdatedAt = Date.now()
    ) => {
      // 服务端响应是保存后的权威状态，必须同时重建选择与基线。
      const draft = buildPosPermissionDraft(permissions);
      setSelectedCodes(draft.selectedCodes);
      setBaselineCodes(draft.baselineCodes);
      initializedScopeKeyRef.current = scopeKey;
      appliedDataUpdatedAtRef.current = dataUpdatedAt;
    },
    [scopeKey]
  );

  useEffect(() => {
    if (initializedScopeKeyRef.current !== scopeKey) {
      initializedScopeKeyRef.current = null;
      appliedDataUpdatedAtRef.current = 0;
      setSelectedCodes([]);
      setBaselineCodes([]);
      setTerminalErrorKind(null);
    }
  }, [scopeKey]);

  useEffect(() => {
    if (
      permissionsQuery.data &&
      shouldApplyPosPermissionResponse({
        initializedScopeKey: initializedScopeKeyRef.current,
        nextScopeKey: scopeKey,
        dirty,
        busy,
        appliedDataUpdatedAt: appliedDataUpdatedAtRef.current,
        nextDataUpdatedAt: permissionsQuery.dataUpdatedAt,
      })
    ) {
      applyServerPermissions(
        permissionsQuery.data,
        permissionsQuery.dataUpdatedAt
      );
    }
  }, [
    applyServerPermissions,
    busy,
    dirty,
    permissionsQuery.data,
    permissionsQuery.dataUpdatedAt,
    scopeKey,
  ]);

  usePreventRemove(
    shouldPreventPosPermissionRemoval({ dirty, busy, allowRemove }),
    ({ data }) => {
      if (busy) {
        Alert.alert(
          t("posPermissions.busy.title"),
          t("posPermissions.busy.description"),
          [{ text: t("common:actions.confirm") }]
        );
        return;
      }

      Alert.alert(
        t("posPermissions.unsaved.title"),
        t("posPermissions.unsaved.description"),
        [
          { text: t("common:actions.cancel"), style: "cancel" },
          {
            text: t("posPermissions.unsaved.discard"),
            style: "destructive",
            onPress: () => {
              // 保存原始 action，下一次渲染先解除阻止再统一派发，兼容返回键与手势。
              pendingActionRef.current = data.action;
              setAllowRemove(true);
            },
          },
        ]
      );
    }
  );

  useEffect(() => {
    if (!allowRemove || !pendingActionRef.current) return;

    const pendingAction = pendingActionRef.current;
    pendingActionRef.current = null;
    // usePreventRemove 已在本次渲染关闭阻止，派发时不会递归进入确认框。
    navigation.dispatch(pendingAction);
  }, [allowRemove, navigation]);

  const handleBack = useCallback(() => {
    if (router.canGoBack()) {
      router.back();
      return;
    }
    router.replace("/(tabs)/users");
  }, [router]);

  const groupedPermissions = useMemo(
    () => groupPosPermissions(permissionsQuery.data?.assignablePermissions ?? []),
    [permissionsQuery.data?.assignablePermissions]
  );
  const selectedCodeSet = useMemo(() => new Set(selectedCodes), [selectedCodes]);
  const mode = permissionsQuery.data?.mode?.toLowerCase() === "override"
    ? "override"
    : "inherited";

  const handleSave = useCallback(async () => {
    if (
      !permissionsQuery.data ||
      !dirty ||
      busy ||
      operationInFlightRef.current
    ) return;

    // ref 在等待 React Query 更新 isPending 前同步占位，封住快速双击窗口。
    operationInFlightRef.current = true;
    try {
      const response = await updateMutation.mutateAsync({
        userGuid,
        storeGuid,
        grantedPermissionCodes: buildGrantedPosPermissionCodes(
          selectedCodes,
          permissionsQuery.data.assignablePermissions
        ),
      });
      setTerminalErrorKind(null);
      applyServerPermissions(response);
      setSnackbarMessage(t("posPermissions.messages.saved"));
    } catch (error) {
      console.warn("[pos-terminal-permissions] 保存失败", error);
      const errorKind = classifyPosPermissionError(error);
      if (errorKind === "network") {
        setSnackbarMessage(t("posPermissions.messages.saveFailed"));
      } else {
        setTerminalErrorKind(errorKind);
      }
    } finally {
      operationInFlightRef.current = false;
    }
  }, [
    applyServerPermissions,
    busy,
    dirty,
    permissionsQuery.data,
    selectedCodes,
    storeGuid,
    t,
    updateMutation,
    userGuid,
  ]);

  const handleRestore = useCallback(() => {
    if (busy || mode !== "override" || operationInFlightRef.current) return;

    Alert.alert(
      t("posPermissions.restore.title"),
      t("posPermissions.restore.description"),
      [
        { text: t("common:actions.cancel"), style: "cancel" },
        {
          text: t("posPermissions.actions.restore"),
          style: "destructive",
          onPress: async () => {
            if (
              operationInFlightRef.current ||
              updateMutation.isPending ||
              restoreMutation.isPending
            ) return;

            // Alert 回调同样同步占位，防止确认按钮被连续触发。
            operationInFlightRef.current = true;
            try {
              const response = await restoreMutation.mutateAsync({
                userGuid,
                storeGuid,
              });
              setTerminalErrorKind(null);
              applyServerPermissions(response);
              setSnackbarMessage(t("posPermissions.messages.restored"));
            } catch (error) {
              console.warn("[pos-terminal-permissions] 恢复继承失败", error);
              const errorKind = classifyPosPermissionError(error);
              if (errorKind === "network") {
                setSnackbarMessage(t("posPermissions.messages.restoreFailed"));
              } else {
                setTerminalErrorKind(errorKind);
              }
            } finally {
              operationInFlightRef.current = false;
            }
          },
        },
      ]
    );
  }, [
    applyServerPermissions,
    busy,
    mode,
    restoreMutation,
    storeGuid,
    t,
    updateMutation.isPending,
    userGuid,
  ]);

  const renderHeader = () => (
    <Surface style={styles.header} elevation={1}>
      <IconButton
        icon="arrow-left"
        accessibilityLabel={t("posPermissions.actions.back")}
        disabled={busy}
        onPress={handleBack}
      />
      <Text variant="titleLarge" numberOfLines={1} style={styles.headerTitle}>
        {t("posPermissions.title")}
      </Text>
    </Surface>
  );

  const renderErrorState = (explicitKind?: PosPermissionErrorKind) => {
    const errorKind = explicitKind ?? classifyPosPermissionError(permissionsQuery.error);
    const canReturn = errorKind === "forbidden" || errorKind === "notFound";
    const canRetry = errorKind === "network";

    return (
      <EmptyState
        title={t(`posPermissions.errors.${errorKind}Title`)}
        description={t(`posPermissions.errors.${errorKind}Description`)}
        primaryAction={
          canReturn
            ? { label: t("posPermissions.actions.back"), icon: "arrow-left", onPress: handleBack }
            : canRetry
              ? { label: t("common:actions.retry"), icon: "refresh", onPress: () => void permissionsQuery.refetch() }
              : undefined
        }
      />
    );
  };

  let content;
  if (!hasRequiredParams) {
    content = (
      <EmptyState
        title={t("posPermissions.errors.invalidTitle")}
        description={t("posPermissions.errors.invalidDescription")}
        primaryAction={{ label: t("posPermissions.actions.back"), icon: "arrow-left", onPress: handleBack }}
      />
    );
  } else if (!canManage || isCurrentUser) {
    content = (
      <EmptyState
        title={t("posPermissions.errors.forbiddenTitle")}
        description={t("posPermissions.errors.forbiddenDescription")}
        primaryAction={{ label: t("posPermissions.actions.back"), icon: "arrow-left", onPress: handleBack }}
      />
    );
  } else if (terminalErrorKind) {
    // 401/403/404 表示当前编辑上下文已失效，停止继续展示或操作旧草稿。
    content = renderErrorState(terminalErrorKind);
  } else if (permissionsQuery.isLoading) {
    content = (
      <View style={styles.centerState}>
        <ActivityIndicator />
        <Text variant="bodyMedium" style={styles.secondaryText}>
          {t("posPermissions.loading")}
        </Text>
      </View>
    );
  } else if (permissionsQuery.isError) {
    content = renderErrorState();
  } else if (permissionsQuery.data) {
    content = (
      <>
        <ScrollView contentContainerStyle={styles.scrollContent}>
          <Card mode="outlined" style={styles.identityCard}>
            <Card.Content style={styles.identityContent}>
              <View style={styles.identityText}>
                <Text variant="titleMedium">{userName}</Text>
                <Text variant="bodyMedium" style={styles.secondaryText}>{storeName}</Text>
                <Text variant="bodySmall" style={styles.secondaryText}>
                  {t("posPermissions.selectedCount", {
                    selected: selectedCodes.length,
                    total: permissionsQuery.data.assignablePermissions.length,
                  })}
                </Text>
              </View>
              <Chip compact icon={mode === "override" ? "shield-account" : "source-branch"}>
                {t(`posPermissions.mode.${mode}`)}
              </Chip>
            </Card.Content>
          </Card>

          {groupedPermissions.length === 0 ? (
            <EmptyState
              title={t("posPermissions.emptyTitle")}
              description={t("posPermissions.emptyDescription")}
            />
          ) : (
            groupedPermissions.map((permissionGroup) => {
              const groupCodes = permissionGroup.permissions.map((item) => item.code);
              const selectedInGroup = groupCodes.filter((code) => selectedCodeSet.has(code)).length;
              const allSelected = selectedInGroup === groupCodes.length;

              return (
                <Card key={permissionGroup.group} mode="outlined" style={styles.groupCard}>
                  <Card.Content style={styles.groupContent}>
                    <View style={styles.groupHeader}>
                      <View style={styles.groupTitleWrap}>
                        <Text variant="titleMedium">{permissionGroup.group}</Text>
                        <Text variant="bodySmall" style={styles.secondaryText}>
                          {t("posPermissions.selectedCount", {
                            selected: selectedInGroup,
                            total: groupCodes.length,
                          })}
                        </Text>
                      </View>
                      <View style={styles.groupActions}>
                        <Button
                          compact
                          mode="text"
                          disabled={busy || allSelected}
                          onPress={() => setSelectedCodes((current) =>
                            setPosPermissionGroupSelection(current, groupCodes, true)
                          )}
                        >
                          {t("posPermissions.actions.selectGroup")}
                        </Button>
                        <Button
                          compact
                          mode="text"
                          disabled={busy || selectedInGroup === 0}
                          onPress={() => setSelectedCodes((current) =>
                            setPosPermissionGroupSelection(current, groupCodes, false)
                          )}
                        >
                          {t("posPermissions.actions.clearGroup")}
                        </Button>
                      </View>
                    </View>

                    {permissionGroup.permissions.map((permission) => (
                      <View key={permission.code} style={styles.permissionRow}>
                        <View style={styles.permissionText}>
                          <Text variant="bodyLarge">{permission.name}</Text>
                          {permission.description ? (
                            <Text variant="bodySmall" style={styles.secondaryText}>
                              {permission.description}
                            </Text>
                          ) : null}
                        </View>
                        <Switch
                          value={selectedCodeSet.has(permission.code)}
                          disabled={busy}
                          accessibilityLabel={permission.name}
                          accessibilityHint={permission.description || undefined}
                          onValueChange={() => setSelectedCodes((current) =>
                            togglePosPermissionCode(current, permission.code)
                          )}
                        />
                      </View>
                    ))}
                  </Card.Content>
                </Card>
              );
            })
          )}
        </ScrollView>

        <Surface style={styles.footer} elevation={3}>
          {mode === "override" ? (
            <Button
              mode="outlined"
              icon="backup-restore"
              disabled={busy}
              onPress={handleRestore}
              style={styles.footerButton}
            >
              {t("posPermissions.actions.restore")}
            </Button>
          ) : null}
          <Button
            mode="contained"
            icon="content-save-outline"
            loading={updateMutation.isPending}
            disabled={!dirty || busy}
            onPress={handleSave}
            style={styles.footerButton}
          >
            {t("posPermissions.actions.save")}
          </Button>
        </Surface>
      </>
    );
  }

  return (
    <SafeAreaView style={styles.safeArea} edges={["top", "left", "right", "bottom"]}>
      <Stack.Screen options={{ headerBackButtonMenuEnabled: false }} />
      {renderHeader()}
      <View style={styles.body}>{content}</View>
      <Snackbar
        visible={Boolean(snackbarMessage)}
        duration={2800}
        onDismiss={() => setSnackbarMessage("")}
      >
        {snackbarMessage}
      </Snackbar>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safeArea: { flex: 1, backgroundColor: "#F4F6F8" },
  body: { flex: 1 },
  header: {
    minHeight: 56,
    flexDirection: "row",
    alignItems: "center",
    backgroundColor: "#FFFFFF",
    paddingRight: 12,
  },
  headerTitle: { flex: 1 },
  centerState: { flex: 1, alignItems: "center", justifyContent: "center", gap: 12, padding: 24 },
  scrollContent: { gap: 12, padding: 12, paddingBottom: 24 },
  identityCard: { borderRadius: 10, backgroundColor: "#FFFFFF" },
  identityContent: { flexDirection: "row", alignItems: "center", justifyContent: "space-between", gap: 12 },
  identityText: { flex: 1, gap: 2 },
  groupCard: { borderRadius: 10, backgroundColor: "#FFFFFF" },
  groupContent: { gap: 4 },
  groupHeader: { flexDirection: "row", alignItems: "center", justifyContent: "space-between", gap: 8, paddingBottom: 4 },
  groupTitleWrap: { flex: 1 },
  groupActions: { flexDirection: "row", flexWrap: "wrap", justifyContent: "flex-end" },
  permissionRow: { minHeight: 58, flexDirection: "row", alignItems: "center", gap: 12, paddingVertical: 8, borderTopWidth: StyleSheet.hairlineWidth, borderTopColor: "#D8DEE6" },
  permissionText: { flex: 1, gap: 2 },
  secondaryText: { color: "#5E6B78" },
  footer: { flexDirection: "row", flexWrap: "wrap", alignItems: "center", gap: 10, paddingHorizontal: 12, paddingVertical: 10, backgroundColor: "#FFFFFF" },
  footerButton: { flexGrow: 1, minWidth: 160 },
});
