import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Alert, Modal, ScrollView, StyleSheet, View } from "react-native";
import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { usePreventRemove, type NavigationAction } from "@react-navigation/native";
import { useLocalSearchParams, useNavigation, useRouter } from "expo-router";
import { ActivityIndicator, Button, Checkbox, Icon, IconButton, Snackbar, Switch, Text, TextInput, TouchableRipple } from "react-native-paper";
import en from "@/locales/en/identityRoles.json";
import zh from "@/locales/zh/identityRoles.json";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { HB_COLORS as C } from "@/shared/theme/tokens";
import { useAuthStore } from "@/store/auth-store";
import { localizeAccessPermission, localizeAccessPermissionCategory } from "@/modules/users/access-permission-presentation";
import {
  addIdentityRoleUsers,
  createIdentityRole,
  fetchIdentityPermissionCatalog,
  fetchIdentityRoleDetail,
  fetchIdentityRolePermissionState,
  fetchIdentityRoleUsers,
  fetchIdentityUsers,
  getIdentityAdminErrorMeta,
  removeIdentityRoleUser,
  saveIdentityRolePermissions,
  updateIdentityRole,
} from "./api";
import type { IdentityRoleDetail, IdentityRoleUser } from "./types";
import {
  acceptSavedPermissionDraft,
  applyMenuPermissionChange,
  arePermissionCodesEqual,
  buildRoleMenuPreview,
  filterPermissionCategories,
  getRoleCapabilities,
  isImplicitAllRole,
  isPermissionDraftDirty,
  isRoleDraftVerified,
  isUncertainRoleWrite,
  reconcilePermissionDraft,
  resolveRolePreviewCodes,
  togglePermission,
  togglePermissionCategory,
  toRoleMutationInput,
  validateRoleDraft,
  type PermissionDraft,
  type RoleDraft,
} from "./role-logic";
import { getRoleMenuDefinitions } from "./role-menu-catalog";
import { AdminEmpty, AdminError, AdminRow, AdminScreen, AdminScroll, AdminTabs, SearchField, StatusTag, styles } from "./ui";
import { useIdentitySession } from "./user-hooks";

type DetailTab = "profile" | "permissions" | "members" | "menus";
type MenuFilter = "all" | "visible" | "hidden";

function interpolate(value: string, params: Record<string, string | number> = {}) {
  return Object.entries(params).reduce((text, [key, replacement]) => text.replace(`{{${key}}}`, String(replacement)), value);
}

function firstParam(value: string | string[] | undefined) {
  return Array.isArray(value) ? value[0] : value;
}

function initialDraft(role?: IdentityRoleDetail | null): RoleDraft {
  return { roleName: role?.roleName ?? "", description: role?.description ?? "", isActive: role?.isActive ?? true };
}

function isProfileDirty(draft: RoleDraft, baseline: RoleDraft) {
  return draft.roleName.trim() !== baseline.roleName.trim()
    || draft.description.trim() !== baseline.description.trim()
    || draft.isActive !== baseline.isActive;
}

async function fetchAllRoleUsers(roleGuid: string, actorGuid: string, signal?: AbortSignal) {
  const pageSize = 200;
  const users: IdentityRoleUser[] = [];
  for (let page = 1; ; page += 1) {
    const result = await fetchIdentityRoleUsers(roleGuid, { page, pageSize }, actorGuid, signal);
    users.push(...result.items);
    if (page >= result.totalPages) return users;
  }
}

export default function RoleDetailScreen() {
  const params = useLocalSearchParams<{ roleGuid?: string | string[] }>();
  const roleGuid = firstParam(params.roleGuid)?.trim() ?? "";
  const { actorKey } = useIdentitySession("Roles.View");
  // 账号或角色变化时强制重建详情状态，避免脏草稿跨身份或跨角色残留。
  return <RoleDetailScreenContent key={`${actorKey}:${roleGuid}`} roleGuid={roleGuid} />;
}

function RoleDetailScreenContent({ roleGuid }: { roleGuid: string }) {
  const router = useRouter();
  const navigation = useNavigation();
  const queryClient = useQueryClient();
  const isCreate = roleGuid === "new";
  const { language } = useAppTranslation();
  const copy = language.startsWith("zh") ? zh : en;
  const appLanguage = language.startsWith("zh") ? "zh" : "en";
  const { access, allowed, actorKey, isAuthenticated } = useIdentitySession("Roles.View");
  const capabilities = useMemo(() => getRoleCapabilities({
    isDeviceMode: !allowed,
    isAdmin: access.isAdmin,
    hasPermission: access.hasPermission,
  }), [access.hasPermission, access.isAdmin, allowed]);
  const [tab, setTab] = useState<DetailTab>("profile");
  const [draft, setDraft] = useState<RoleDraft>(() => initialDraft());
  const [baseline, setBaseline] = useState<RoleDraft>(() => initialDraft());
  const [permissionDraft, setPermissionDraft] = useState<PermissionDraft | null>(null);
  const [permissionSearch, setPermissionSearch] = useState("");
  const [expandedCategories, setExpandedCategories] = useState<Set<string>>(new Set());
  const [memberSearch, setMemberSearch] = useState("");
  const [addSearch, setAddSearch] = useState("");
  const [addOpen, setAddOpen] = useState(false);
  const [selectedUsers, setSelectedUsers] = useState<string[]>([]);
  const [menuPlatform, setMenuPlatform] = useState<"web" | "mobile">("web");
  const [menuFilter, setMenuFilter] = useState<MenuFilter>("all");
  const [writeForbidden, setWriteForbidden] = useState(false);
  const [allowRemove, setAllowRemove] = useState(false);
  const [notice, setNotice] = useState("");
  const pendingActionRef = useRef<NavigationAction | null>(null);
  const createdRoleGuidRef = useRef<string | null>(null);
  const createPostAttemptedRef = useRef(false);
  const initializedRoleRef = useRef<string | null>(null);

  const canOpen = allowed && (!isCreate || capabilities.canCreate);
  const roleQuery = useQuery({
    queryKey: ["identity-admin", actorKey, "role", roleGuid],
    enabled: canOpen && !isCreate && Boolean(roleGuid),
    retry: false,
    queryFn: ({ signal }) => fetchIdentityRoleDetail(roleGuid, actorKey, signal),
  });
  const catalogQuery = useQuery({
    queryKey: ["identity-admin", actorKey, "permissionCatalog"],
    enabled: canOpen && !isCreate && (tab === "permissions" || tab === "menus"),
    retry: false,
    queryFn: ({ signal }) => fetchIdentityPermissionCatalog(actorKey, signal),
  });
  const permissionQuery = useQuery({
    queryKey: ["identity-admin", actorKey, "rolePermissionState", roleGuid],
    enabled: canOpen && !isCreate && Boolean(roleGuid) && (tab === "permissions" || tab === "menus"),
    retry: false,
    queryFn: ({ signal }) => fetchIdentityRolePermissionState(roleGuid, actorKey, signal),
  });
  const membersQuery = useInfiniteQuery({
    queryKey: ["identity-admin", actorKey, "roleMembers", roleGuid, memberSearch.trim()],
    enabled: canOpen && !isCreate && tab === "members",
    retry: false,
    initialPageParam: 1,
    queryFn: ({ pageParam, signal }) => fetchIdentityRoleUsers(roleGuid, { page: pageParam, pageSize: 100, searchKeyword: memberSearch.trim() || undefined }, actorKey, signal),
    getNextPageParam: (lastPage) => lastPage.page < lastPage.totalPages ? lastPage.page + 1 : undefined,
  });
  const allMemberIdsQuery = useQuery({
    queryKey: ["identity-admin", actorKey, "roleAllMemberIds", roleGuid],
    enabled: canOpen && !isCreate && addOpen && capabilities.canManageUsers && !writeForbidden,
    retry: false,
    queryFn: ({ signal }) => fetchAllRoleUsers(roleGuid, actorKey, signal),
  });
  const availableUsersQuery = useInfiniteQuery({
    queryKey: ["identity-admin", actorKey, "roleAvailableUsers", roleGuid, addSearch.trim()],
    enabled: canOpen && !isCreate && addOpen && capabilities.canManageUsers && !writeForbidden && allMemberIdsQuery.isSuccess,
    retry: false,
    initialPageParam: 1,
    queryFn: async ({ pageParam, signal }) => {
      const page = await fetchIdentityUsers({ page: pageParam, pageSize: 100, search: addSearch.trim() || undefined }, actorKey, signal);
      const assigned = new Set((allMemberIdsQuery.data ?? []).map((member) => member.userGUID));
      return { ...page, items: page.items.filter((user) => !assigned.has(user.userGUID)) };
    },
    getNextPageParam: (lastPage) => lastPage.page < lastPage.totalPages ? lastPage.page + 1 : undefined,
  });

  const profileDirty = isProfileDirty(draft, baseline);
  const permissionsDirty = Boolean(permissionDraft && isPermissionDraftDirty(permissionDraft));
  const dirty = profileDirty || permissionsDirty;

  const assertCurrentSession = useCallback((permission: string) => {
    const state = useAuthStore.getState();
    const currentActorKey = state.user?.userGUID ?? "";
    if (
      !state.isAuthenticated
      || state.sessionKind !== "account"
      || state.iosReviewOfflineGuardActive
      || currentActorKey !== actorKey
      || !state.access.hasPermission(permission)
    ) {
      throw Object.assign(new Error("IDENTITY_ROLE_SESSION_CHANGED"), { status: 403 });
    }
  }, [actorKey]);
  const canApplyMutationResult = useCallback((permission: string) => {
    try {
      assertCurrentSession(permission);
      return true;
    } catch {
      return false;
    }
  }, [assertCurrentSession]);

  useEffect(() => {
    if (isCreate || !roleQuery.data || initializedRoleRef.current === roleGuid) return;
    const next = initialDraft(roleQuery.data);
    setDraft(next);
    setBaseline(next);
    initializedRoleRef.current = roleGuid;
  }, [isCreate, roleGuid, roleQuery.data]);

  useEffect(() => {
    if (!permissionQuery.data) return;
    const codes = isImplicitAllRole(permissionQuery.data, catalogQuery.data?.superAdminRoleNames)
      ? permissionQuery.data.effectivePermissionCodes
      : permissionQuery.data.explicitPermissionCodes;
    setPermissionDraft((current) => reconcilePermissionDraft(current, codes));
  }, [catalogQuery.data?.superAdminRoleNames, permissionQuery.data]);

  const handleMutationError = useCallback((error: unknown) => {
    const status = getIdentityAdminErrorMeta(error).status;
    if ([401, 403].includes(status ?? 0) || isUncertainRoleWrite(error)) {
      setWriteForbidden(true);
      setNotice([401, 403].includes(status ?? 0) ? copy.forbidden : copy.uncertainWrite);
    } else setNotice(copy.saveFailed);
  }, [copy.forbidden, copy.saveFailed, copy.uncertainWrite]);

  const profileMutation = useMutation({
    retry: false,
    mutationFn: async () => {
      const input = toRoleMutationInput(draft);
      if (isCreate) {
        assertCurrentSession("Roles.Create");
        const createdRoleGuid = createdRoleGuidRef.current;
        if (!createdRoleGuid && createPostAttemptedRef.current) throw new Error("ROLE_CREATE_RETRY_BLOCKED");
        let guid = createdRoleGuid;
        if (!guid) {
          // 在 POST 前锁定本次创建；即使用户快速重复点击或响应不明确，也不能发送第二次创建。
          createPostAttemptedRef.current = true;
          try {
            guid = (await createIdentityRole(input, actorKey)).roleGUID;
          } catch (error) {
            const status = getIdentityAdminErrorMeta(error).status;
            if (!isUncertainRoleWrite(error) && ![401, 403].includes(status ?? 0)) {
              createPostAttemptedRef.current = false;
            }
            throw error;
          }
        }
        // POST 已返回 GUID 后只允许继续核对该角色，绝不再次创建同名角色。
        createdRoleGuidRef.current = guid;
        assertCurrentSession("Roles.Create");
        const verified = await fetchIdentityRoleDetail(guid, actorKey);
        assertCurrentSession("Roles.Create");
        if (!isRoleDraftVerified(draft, verified)) throw new Error("ROLE_READBACK_MISMATCH");
        return verified;
      }
      assertCurrentSession("Roles.Edit");
      await updateIdentityRole(roleGuid, input, actorKey);
      assertCurrentSession("Roles.Edit");
      const verified = await fetchIdentityRoleDetail(roleGuid, actorKey);
      assertCurrentSession("Roles.Edit");
      if (!isRoleDraftVerified(draft, verified)) throw new Error("ROLE_READBACK_MISMATCH");
      return verified;
    },
    onSuccess: async (verified) => {
      if (!canApplyMutationResult(isCreate ? "Roles.Create" : "Roles.Edit")) return;
      queryClient.setQueryData(["identity-admin", actorKey, "role", verified.roleGUID], verified);
      await queryClient.invalidateQueries({ queryKey: ["identity-admin", actorKey, "roles"] });
      if (!canApplyMutationResult(isCreate ? "Roles.Create" : "Roles.Edit")) return;
      const next = initialDraft(verified);
      setDraft(next);
      setBaseline(next);
      setNotice(isCreate ? copy.created : copy.profileSaved);
      if (isCreate) {
        createdRoleGuidRef.current = verified.roleGUID;
        setAllowRemove(true);
      }
    },
    onError: (error) => {
      handleMutationError(error);
      if (isCreate && createdRoleGuidRef.current && canApplyMutationResult("Roles.View")) setAllowRemove(true);
    },
  });
  const permissionsMutation = useMutation({
    retry: false,
    mutationFn: async () => {
      if (!permissionDraft) throw new Error("Permission draft unavailable");
      assertCurrentSession("Roles.ManagePermissions");
      await saveIdentityRolePermissions(roleGuid, permissionDraft.selected, actorKey);
      assertCurrentSession("Roles.ManagePermissions");
      const verified = await fetchIdentityRolePermissionState(roleGuid, actorKey);
      assertCurrentSession("Roles.ManagePermissions");
      if (!arePermissionCodesEqual(permissionDraft.selected, verified.explicitPermissionCodes)) {
        throw new Error("ROLE_PERMISSION_READBACK_MISMATCH");
      }
      return verified;
    },
    onSuccess: async (verified) => {
      if (!canApplyMutationResult("Roles.ManagePermissions")) return;
      queryClient.setQueryData(["identity-admin", actorKey, "rolePermissionState", roleGuid], verified);
      const verifiedCodes = isImplicitAllRole(verified, catalogQuery.data?.superAdminRoleNames)
        ? verified.effectivePermissionCodes
        : verified.explicitPermissionCodes;
      setPermissionDraft(acceptSavedPermissionDraft({ selected: verifiedCodes, baseline: verifiedCodes }));
      await queryClient.invalidateQueries({ queryKey: ["identity-admin", actorKey, "role", roleGuid] });
      if (!canApplyMutationResult("Roles.ManagePermissions")) return;
      setNotice(copy.permissionsSaved);
    },
    onError: handleMutationError,
  });
  const addMembersMutation = useMutation({
    retry: false,
    mutationFn: async () => {
      assertCurrentSession("Roles.ManageUsers");
      await addIdentityRoleUsers(roleGuid, selectedUsers, actorKey);
      assertCurrentSession("Roles.ManageUsers");
      const [detail, allMembers] = await Promise.all([fetchIdentityRoleDetail(roleGuid, actorKey), fetchAllRoleUsers(roleGuid, actorKey)]);
      assertCurrentSession("Roles.ManageUsers");
      const memberGuids = new Set(allMembers.map((member) => member.userGUID));
      if (!selectedUsers.every((userGuid) => memberGuids.has(userGuid))) throw new Error("ROLE_MEMBER_READBACK_MISMATCH");
      return detail;
    },
    onSuccess: async (verified) => {
      if (!canApplyMutationResult("Roles.ManageUsers")) return;
      queryClient.setQueryData(["identity-admin", actorKey, "role", roleGuid], verified);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["identity-admin", actorKey, "roleMembers", roleGuid] }),
        queryClient.invalidateQueries({ queryKey: ["identity-admin", actorKey, "roleAllMemberIds", roleGuid] }),
        queryClient.invalidateQueries({ queryKey: ["identity-admin", actorKey, "roleAvailableUsers", roleGuid] }),
        queryClient.invalidateQueries({ queryKey: ["identity-admin", actorKey, "roles"] }),
      ]);
      if (!canApplyMutationResult("Roles.ManageUsers")) return;
      setSelectedUsers([]);
      setAddOpen(false);
      setNotice(copy.membersAdded);
    },
    onError: handleMutationError,
  });
  const removeMemberMutation = useMutation({
    retry: false,
    mutationFn: async (userGuid: string) => {
      assertCurrentSession("Roles.ManageUsers");
      await removeIdentityRoleUser(roleGuid, userGuid, actorKey);
      assertCurrentSession("Roles.ManageUsers");
      const [detail, allMembers] = await Promise.all([fetchIdentityRoleDetail(roleGuid, actorKey), fetchAllRoleUsers(roleGuid, actorKey)]);
      assertCurrentSession("Roles.ManageUsers");
      if (allMembers.some((member) => member.userGUID === userGuid)) throw new Error("ROLE_MEMBER_READBACK_MISMATCH");
      return detail;
    },
    onSuccess: async (verified) => {
      if (!canApplyMutationResult("Roles.ManageUsers")) return;
      queryClient.setQueryData(["identity-admin", actorKey, "role", roleGuid], verified);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["identity-admin", actorKey, "roleMembers", roleGuid] }),
        queryClient.invalidateQueries({ queryKey: ["identity-admin", actorKey, "roleAllMemberIds", roleGuid] }),
        queryClient.invalidateQueries({ queryKey: ["identity-admin", actorKey, "roleAvailableUsers", roleGuid] }),
        queryClient.invalidateQueries({ queryKey: ["identity-admin", actorKey, "roles"] }),
      ]);
      if (!canApplyMutationResult("Roles.ManageUsers")) return;
      setNotice(copy.memberRemoved);
    },
    onError: handleMutationError,
  });
  const saving = profileMutation.isPending || permissionsMutation.isPending || addMembersMutation.isPending || removeMemberMutation.isPending;

  const handleVerifyRefresh = useCallback(async () => {
    if (!canApplyMutationResult("Roles.View")) return;
    if (isCreate) {
      await queryClient.invalidateQueries({ queryKey: ["identity-admin", actorKey, "roles"] });
      if (!canApplyMutationResult("Roles.View")) return;
      // 创建请求未返回 GUID 时无法安全判断是否已落库，因此刷新后仍保持冻结，禁止重复 POST。
      setNotice(copy.verificationFailed);
      return;
    }
    try {
      assertCurrentSession("Roles.View");
      const verified = await fetchIdentityRoleDetail(roleGuid, actorKey);
      assertCurrentSession("Roles.View");
      queryClient.setQueryData(["identity-admin", actorKey, "role", roleGuid], verified);
      const remoteDraft = initialDraft(verified);
      setBaseline(remoteDraft);
      if (isRoleDraftVerified(draft, verified)) setDraft(remoteDraft);

      if (tab === "permissions" || tab === "menus") {
        const state = await fetchIdentityRolePermissionState(roleGuid, actorKey);
        assertCurrentSession("Roles.View");
        queryClient.setQueryData(["identity-admin", actorKey, "rolePermissionState", roleGuid], state);
        const remoteCodes = isImplicitAllRole(state, catalogQuery.data?.superAdminRoleNames)
          ? state.effectivePermissionCodes
          : state.explicitPermissionCodes;
        setPermissionDraft((current) => current
          ? { selected: current.selected, baseline: remoteCodes }
          : reconcilePermissionDraft(null, remoteCodes));
      }
      if (tab === "members") {
        await fetchAllRoleUsers(roleGuid, actorKey);
        assertCurrentSession("Roles.View");
        await Promise.all([
          queryClient.invalidateQueries({ queryKey: ["identity-admin", actorKey, "roleMembers", roleGuid] }),
          queryClient.invalidateQueries({ queryKey: ["identity-admin", actorKey, "roleAllMemberIds", roleGuid] }),
        ]);
        assertCurrentSession("Roles.View");
      }
      setWriteForbidden(false);
      setNotice(copy.verificationComplete);
    } catch {
      if (canApplyMutationResult("Roles.View")) setNotice(copy.verificationFailed);
    }
  }, [actorKey, assertCurrentSession, canApplyMutationResult, catalogQuery.data?.superAdminRoleNames, copy.verificationComplete, copy.verificationFailed, draft, isCreate, queryClient, roleGuid, tab]);

  usePreventRemove(isAuthenticated && (dirty || saving) && !allowRemove, ({ data }) => {
    const session = useAuthStore.getState();
    if (
      !session.isAuthenticated
      || session.sessionKind !== "account"
      || session.iosReviewOfflineGuardActive
      || (session.user?.userGUID ?? "") !== actorKey
    ) {
      // 登出或身份切换必须立即离开，不能被旧账号的脏草稿确认框拦住。
      pendingActionRef.current = data.action;
      setAllowRemove(true);
      return;
    }
    if (saving) {
      Alert.alert(copy.busyTitle, copy.busyMessage, [{ text: copy.confirm }]);
      return;
    }
    Alert.alert(copy.unsavedTitle, copy.unsavedMessage, [
      { text: copy.cancel, style: "cancel" },
      { text: copy.discard, style: "destructive", onPress: () => { pendingActionRef.current = data.action; setAllowRemove(true); } },
    ]);
  });
  useEffect(() => {
    if (!allowRemove) return;
    if (pendingActionRef.current) {
      const action = pendingActionRef.current;
      pendingActionRef.current = null;
      navigation.dispatch(action);
      return;
    }
    if (isCreate && createdRoleGuidRef.current) {
      const createdRoleGuid = createdRoleGuidRef.current;
      router.replace({ pathname: "/roles/[roleGuid]", params: { roleGuid: createdRoleGuid } });
      // 同一动态路由可能复用组件，跳转完成后重新启用脏草稿保护。
      setAllowRemove(false);
    }
  }, [allowRemove, isCreate, navigation, router]);

  const presentedCategories = useMemo(() => (catalogQuery.data?.categories ?? []).map((category) => ({
    ...category,
    displayName: localizeAccessPermissionCategory(category.category || category.displayName, appLanguage),
    permissions: category.permissions.map((permission) => {
      const localized = localizeAccessPermission(permission, appLanguage);
      return { ...permission, displayName: localized.name, description: localized.description };
    }),
  })), [appLanguage, catalogQuery.data?.categories]);
  const filteredCategories = useMemo(
    () => filterPermissionCategories(presentedCategories, permissionSearch),
    [permissionSearch, presentedCategories],
  );
  const selectedPermissionCodes = useMemo(() => permissionDraft?.selected ?? [], [permissionDraft?.selected]);
  const members = useMemo(() => membersQuery.data?.pages.flatMap((page) => page.items) ?? [], [membersQuery.data?.pages]);
  const availableUsers = useMemo(() => availableUsersQuery.data?.pages.flatMap((page) => page.items) ?? [], [availableUsersQuery.data?.pages]);
  const implicitAllPermissions = Boolean(permissionQuery.data && isImplicitAllRole(
    permissionQuery.data,
    catalogQuery.data?.superAdminRoleNames,
  ));
  const actualSuperAdmin = permissionQuery.data?.isSuperAdmin ?? false;
  const assignablePermissionCodes = useMemo(
    () => catalogQuery.data?.categories.flatMap((category) => category.permissions.map((permission) => permission.name)) ?? [],
    [catalogQuery.data?.categories],
  );
  const previewPermissionCodes = useMemo(() => permissionQuery.data
    ? resolveRolePreviewCodes({
      selectedCodes: selectedPermissionCodes,
      aliases: catalogQuery.data?.permissionAliases ?? [],
    })
    : [], [catalogQuery.data?.permissionAliases, permissionQuery.data, selectedPermissionCodes]);
  const menuItems = useMemo(() => {
    const items = buildRoleMenuPreview(
      getRoleMenuDefinitions(language),
      previewPermissionCodes,
      { isSuperAdmin: actualSuperAdmin, implicitAllPermissions },
    );
    return items.filter((item) => item.platform === menuPlatform && (menuFilter === "all" || (menuFilter === "visible") === item.visible));
  }, [actualSuperAdmin, implicitAllPermissions, language, menuFilter, menuPlatform, previewPermissionCodes]);
  const totalPermissions = catalogQuery.data?.categories.reduce((total, category) => total + category.permissions.length, 0) ?? 0;
  const profileErrors = validateRoleDraft(draft);
  const canSaveProfile = (isCreate ? capabilities.canCreate : capabilities.canEdit) && !writeForbidden && !saving && Object.keys(profileErrors).length === 0 && (isCreate || profileDirty);
  const canSavePermissions = capabilities.canManagePermissions
    && catalogQuery.isSuccess
    && permissionQuery.isSuccess
    && !writeForbidden
    && !implicitAllPermissions
    && !saving
    && permissionsDirty;

  const footer = tab === "profile" ? (
    (isCreate || capabilities.canEdit) ? <Button mode="contained" style={styles.button} disabled={!canSaveProfile || profileMutation.isPending} loading={profileMutation.isPending} onPress={() => profileMutation.mutate()}>{isCreate ? copy.createRole : copy.saveProfile}</Button> : undefined
  ) : (tab === "permissions" || tab === "menus") ? (
    <Button mode="contained" style={styles.button} disabled={!canSavePermissions || permissionsMutation.isPending} loading={permissionsMutation.isPending} onPress={() => permissionsMutation.mutate()}>{copy.savePermissions}</Button>
  ) : undefined;

  if (!canOpen) {
    return <AdminScreen title={isCreate ? copy.createTitle : copy.detailTitle}><View style={localStyles.message}><Text>{copy.accessDenied}</Text></View></AdminScreen>;
  }
  if (!isCreate && roleQuery.isPending) return <AdminScreen title={copy.detailTitle}><ActivityIndicator style={{ flex: 1 }} /></AdminScreen>;
  if (!isCreate && roleQuery.error) return <AdminScreen title={copy.detailTitle}><AdminError error={roleQuery.error} onRetry={() => void roleQuery.refetch()} /></AdminScreen>;

  return (
    <AdminScreen
      title={isCreate ? copy.createTitle : roleQuery.data?.roleName || copy.detailTitle}
      action={writeForbidden ? <IconButton icon="refresh" accessibilityLabel={copy.retry} onPress={() => void handleVerifyRefresh()} /> : undefined}
      footer={footer}
    >
      {!isCreate ? <AdminTabs value={tab} items={[
        { key: "profile", label: copy.profileTab }, { key: "permissions", label: copy.permissionsTab },
        { key: "members", label: copy.membersTab }, { key: "menus", label: copy.menusTab },
      ]} onChange={(key) => setTab(key as DetailTab)} /> : null}
      {tab === "profile" ? <ProfilePanel copy={copy} draft={draft} setDraft={setDraft} errors={profileErrors} readOnly={saving || writeForbidden || (!isCreate && !capabilities.canEdit)} role={roleQuery.data} /> : null}
      {tab === "permissions" ? (
        <AdminScroll>
          {implicitAllPermissions ? <View style={localStyles.info}><Icon source="shield-lock-outline" size={22} color={C.action} /><Text style={styles.muted}>{copy.superAdminHint}</Text></View> : null}
          <SearchField value={permissionSearch} onChange={setPermissionSearch} placeholder={copy.permissionSearch} />
          <Text style={styles.muted}>{interpolate(copy.selectedCount, { selected: selectedPermissionCodes.length, total: totalPermissions })}</Text>
          {catalogQuery.error || permissionQuery.error ? <AdminError error={catalogQuery.error ?? permissionQuery.error} onRetry={() => { void catalogQuery.refetch(); void permissionQuery.refetch(); }} /> : null}
          {!catalogQuery.isPending && filteredCategories.length === 0 ? <AdminEmpty text={copy.noPermissions} /> : filteredCategories.map((category) => {
            const codes = category.permissions.map((permission) => permission.name);
            const allSelected = codes.length > 0 && codes.every((code) => selectedPermissionCodes.includes(code));
            const expanded = expandedCategories.has(category.category) || Boolean(permissionSearch.trim());
            const readOnly = !capabilities.canManagePermissions || writeForbidden || saving || implicitAllPermissions;
            return <View key={category.category} style={styles.section}>
              <View style={localStyles.categoryHeader}>
                <IconButton icon={expanded ? "chevron-down" : "chevron-right"} accessibilityLabel={category.displayName} onPress={() => setExpandedCategories((current) => {
                  const next = new Set(current);
                  if (next.has(category.category)) next.delete(category.category);
                  else next.add(category.category);
                  return next;
                })} />
                <View style={{ flex: 1 }}><Text style={styles.value}>{category.displayName}</Text><Text style={styles.muted}>{category.permissions.length}</Text></View>
                <Button compact disabled={readOnly} onPress={() => setPermissionDraft((current) => current ? togglePermissionCategory(current, codes) : current)}>{allSelected ? copy.clearAll : copy.selectAll}</Button>
              </View>
              {expanded ? category.permissions.map((permission) => <TouchableRipple key={permission.name} disabled={readOnly} onPress={() => setPermissionDraft((current) => current ? togglePermission(current, permission.name) : current)}>
                <View style={localStyles.permissionRow}><Checkbox status={selectedPermissionCodes.includes(permission.name) ? "checked" : "unchecked"} disabled={readOnly} /><View style={{ flex: 1 }}><Text>{permission.displayName}</Text><Text style={styles.muted}>{permission.name}</Text></View></View>
              </TouchableRipple>) : null}
            </View>;
          })}
        </AdminScroll>
      ) : null}
      {tab === "members" ? <MembersPanel copy={copy} search={memberSearch} setSearch={setMemberSearch} members={members} total={membersQuery.data?.pages[0]?.total ?? 0} loading={membersQuery.isPending} loadingMore={membersQuery.isFetchingNextPage} hasMore={membersQuery.hasNextPage} loadMore={() => void membersQuery.fetchNextPage()} error={membersQuery.error} retry={() => void membersQuery.refetch()} canManage={capabilities.canManageUsers && !writeForbidden && !addMembersMutation.isPending && !removeMemberMutation.isPending} onAdd={() => setAddOpen(true)} onRemove={(member) => Alert.alert(copy.removeTitle, interpolate(copy.removeMessage, { name: member.fullName || member.username }), [{ text: copy.cancel, style: "cancel" }, { text: copy.remove, style: "destructive", onPress: () => removeMemberMutation.mutate(member.userGUID) }])} /> : null}
      {tab === "menus" ? (
        <AdminScroll>
          {catalogQuery.isPending || permissionQuery.isPending ? <ActivityIndicator /> : catalogQuery.error || permissionQuery.error ? (
            <AdminError error={catalogQuery.error ?? permissionQuery.error} onRetry={() => { void catalogQuery.refetch(); void permissionQuery.refetch(); }} />
          ) : <>
            <AdminTabs value={menuPlatform} items={[{ key: "web", label: copy.menuPlatformWeb }, { key: "mobile", label: copy.menuPlatformMobile }]} onChange={(key) => setMenuPlatform(key as "web" | "mobile")} />
            <AdminTabs value={menuFilter} items={[{ key: "all", label: copy.menuFilterAll }, { key: "visible", label: copy.menuFilterVisible }, { key: "hidden", label: copy.menuFilterHidden }]} onChange={(key) => setMenuFilter(key as MenuFilter)} />
            <Text style={styles.muted}>{copy.menuSaveHint}</Text>
            <View style={styles.section}>{menuItems.map((item) => <AdminRow key={`${item.platform}:${item.key}`} title={item.title} subtitle={item.fixed ? copy.menuFixed : item.requireAdmin ? copy.menuAdminOnly : item.visible ? copy.menuFilterVisible : copy.menuFilterHidden} trailing={<Switch value={item.visible} disabled={item.readOnly || !capabilities.canManagePermissions || writeForbidden || saving} onValueChange={(visible) => setPermissionDraft((current) => current ? { ...current, selected: applyMenuPermissionChange(current.selected, item, visible, catalogQuery.data?.permissionAliases ?? [], assignablePermissionCodes) } : current)} />} />)}</View>
          </>}
        </AdminScroll>
      ) : null}
      <Modal visible={addOpen} animationType="slide" presentationStyle="pageSheet" onRequestClose={() => setAddOpen(false)}>
        <AdminScreen title={copy.addMembers} onBack={() => setAddOpen(false)} footer={<Button mode="contained" disabled={!selectedUsers.length || addMembersMutation.isPending} loading={addMembersMutation.isPending} onPress={() => addMembersMutation.mutate()}>{interpolate(copy.addSelected, { count: selectedUsers.length })}</Button>}>
          <View style={localStyles.search}><SearchField value={addSearch} onChange={setAddSearch} placeholder={copy.availableSearch} /></View>
          {allMemberIdsQuery.isPending || availableUsersQuery.isPending ? <ActivityIndicator style={{ flex: 1 }} /> : allMemberIdsQuery.error || availableUsersQuery.error ? <AdminError error={allMemberIdsQuery.error ?? availableUsersQuery.error} onRetry={() => { void allMemberIdsQuery.refetch(); void availableUsersQuery.refetch(); }} /> : <ScrollView>{availableUsers.length === 0 && !availableUsersQuery.hasNextPage ? <AdminEmpty text={copy.noAvailableUsers} /> : availableUsers.map((user) => <TouchableRipple key={user.userGUID} onPress={() => setSelectedUsers((current) => current.includes(user.userGUID) ? current.filter((guid) => guid !== user.userGUID) : [...current, user.userGUID])}><View style={localStyles.permissionRow}><Checkbox status={selectedUsers.includes(user.userGUID) ? "checked" : "unchecked"} /><View style={{ flex: 1 }}><Text>{user.fullName || user.username}</Text><Text style={styles.muted}>{user.email}</Text></View></View></TouchableRipple>)}{availableUsersQuery.hasNextPage ? <Button loading={availableUsersQuery.isFetchingNextPage} onPress={() => void availableUsersQuery.fetchNextPage()}>{copy.loadMore}</Button> : null}</ScrollView>}
        </AdminScreen>
      </Modal>
      <Snackbar visible={Boolean(notice)} onDismiss={() => setNotice("")} duration={5000}>{notice}</Snackbar>
    </AdminScreen>
  );
}

function ProfilePanel({ copy, draft, setDraft, errors, readOnly, role }: { copy: typeof en; draft: RoleDraft; setDraft: (draft: RoleDraft) => void; errors: ReturnType<typeof validateRoleDraft>; readOnly: boolean; role?: IdentityRoleDetail }) {
  const nameError = errors.roleName === "required" ? copy.validationRequired : errors.roleName === "tooShort" ? copy.validationTooShort : errors.roleName === "tooLong" ? copy.validationNameTooLong : "";
  return <AdminScroll>
    <TextInput mode="outlined" label={copy.roleName} placeholder={copy.roleNamePlaceholder} value={draft.roleName} disabled={readOnly} maxLength={50} error={Boolean(nameError)} onChangeText={(roleName) => setDraft({ ...draft, roleName })} />
    {nameError ? <Text style={localStyles.errorText}>{nameError}</Text> : null}
    <TextInput mode="outlined" label={copy.description} placeholder={copy.descriptionPlaceholder} value={draft.description} disabled={readOnly} multiline maxLength={200} error={Boolean(errors.description)} onChangeText={(description) => setDraft({ ...draft, description })} />
    {errors.description ? <Text style={localStyles.errorText}>{copy.validationDescriptionTooLong}</Text> : null}
    <View style={localStyles.switchRow}><View style={{ flex: 1 }}><Text style={styles.value}>{copy.status}</Text><Text style={styles.muted}>{draft.isActive ? copy.active : copy.disabled}</Text></View><Switch value={draft.isActive} disabled={readOnly} onValueChange={(isActive) => setDraft({ ...draft, isActive })} /></View>
    {role ? <View style={styles.section}><AdminRow title={copy.createdAt} trailing={<Text style={styles.muted}>{role.createdAt || "—"}</Text>} /><AdminRow title={copy.updatedAt} trailing={<Text style={styles.muted}>{role.updatedAt || "—"}</Text>} /><AdminRow title={copy.status} trailing={<StatusTag active={role.isActive} />} /></View> : null}
  </AdminScroll>;
}

function MembersPanel({ copy, search, setSearch, members, total, loading, loadingMore, hasMore, loadMore, error, retry, canManage, onAdd, onRemove }: { copy: typeof en; search: string; setSearch: (value: string) => void; members: IdentityRoleUser[]; total: number; loading: boolean; loadingMore: boolean; hasMore: boolean; loadMore: () => void; error: unknown; retry: () => void; canManage: boolean; onAdd: () => void; onRemove: (member: IdentityRoleUser) => void }) {
  return <View style={{ flex: 1 }}><View style={localStyles.memberTools}><View style={{ flex: 1 }}><SearchField value={search} onChange={setSearch} placeholder={copy.membersSearch} /></View>{canManage ? <IconButton icon="account-plus-outline" accessibilityLabel={copy.addMembers} onPress={onAdd} /> : null}</View>{loading ? <ActivityIndicator style={{ flex: 1 }} /> : error ? <AdminError error={error} onRetry={retry} /> : <ScrollView><Text style={localStyles.memberCount}>{interpolate(copy.membersCount, { count: total })}</Text>{members.length === 0 ? <AdminEmpty text={copy.noMembers} /> : members.map((member) => <AdminRow key={member.userGUID} icon="account-outline" title={member.fullName || member.username} subtitle={member.email} trailing={canManage ? <IconButton icon="account-minus-outline" accessibilityLabel={copy.remove} onPress={() => onRemove(member)} /> : <StatusTag active={member.isActive} />} />)}{hasMore ? <Button loading={loadingMore} onPress={loadMore}>{copy.loadMore}</Button> : null}</ScrollView>}</View>;
}

const localStyles = StyleSheet.create({
  message: { margin: 16, padding: 16, borderRadius: 8, backgroundColor: C.surfaceMuted },
  info: { flexDirection: "row", gap: 10, padding: 12, borderRadius: 8, backgroundColor: "#EFF6FF" },
  categoryHeader: { minHeight: 52, flexDirection: "row", alignItems: "center", paddingHorizontal: 12, gap: 8 },
  permissionRow: { minHeight: 52, flexDirection: "row", alignItems: "center", paddingHorizontal: 12, gap: 8, borderTopWidth: StyleSheet.hairlineWidth, borderTopColor: C.outlineMuted },
  switchRow: { minHeight: 56, flexDirection: "row", alignItems: "center", padding: 12, borderWidth: 1, borderColor: C.outlineMuted, borderRadius: 8 },
  errorText: { color: C.danger, fontSize: 12, marginTop: -12 },
  memberTools: { flexDirection: "row", alignItems: "center", paddingHorizontal: 16, paddingBottom: 12 },
  memberCount: { paddingHorizontal: 16, paddingBottom: 8, color: C.textSecondary },
  search: { paddingHorizontal: 16, paddingBottom: 12 },
});
