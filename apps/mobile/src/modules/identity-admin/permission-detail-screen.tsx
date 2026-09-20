import { useCallback, useEffect, useMemo, useState } from "react";
import { Alert, StyleSheet, View } from "react-native";
import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useLocalSearchParams, useRouter } from "expo-router";
import { ActivityIndicator, Button, Checkbox, Icon, IconButton, Snackbar, Text, TouchableRipple } from "react-native-paper";
import { BusinessSheet } from "@/components/ui/BusinessSheet";
import { HB_COLORS as C } from "@/shared/theme/tokens";
import { useAuthStore } from "@/store/auth-store";
import { localizeAccessRoleName } from "@/modules/users/access-permission-presentation";
import {
  assignIdentityPermissionRoles,
  assignIdentityPermissionUsers,
  deleteIdentitySysPermission,
  fetchIdentityPermissionRoles,
  fetchIdentityPermissionUsers,
  fetchIdentityRoles,
  fetchIdentityUsers,
  getIdentityAdminErrorMeta,
} from "./api";
import type { IdentityRole, IdentityRoleUser } from "./types";
import { isUncertainRoleWrite } from "./role-logic";
import {
  areRoleGuidsEqual,
  createRoleAssignmentDraft,
  findPermissionListItem,
  formatPermissionUserName,
  getRoleAssignmentDelta,
  isImplicitAllRoleName,
  isPermissionUserDeltaApplied,
  isRoleAssignmentDirty,
  toggleRoleAssignment,
  type RoleAssignmentDraft,
} from "./permission-logic";
import { interpolatePermissionCopy as interpolate, permissionQueryKeys, usePermissionCopy, usePermissionItems, usePermissionSession } from "./permission-hooks";
import { AdminEmpty, AdminError, AdminRow, AdminScreen, AdminScroll, SearchField, StatusTag, styles } from "./ui";
import { identityDate, useIdentitySearch } from "./user-hooks";

type PermissionUserLite = Pick<IdentityRoleUser, "userGUID" | "username" | "email" | "fullName" | "isActive">;

function toPermissionUserLite(user: PermissionUserLite): PermissionUserLite {
  return { userGUID: user.userGUID, username: user.username, email: user.email, isActive: user.isActive, ...(user.fullName ? { fullName: user.fullName } : {}) };
}

function matchesPermissionUser(user: PermissionUserLite, keyword: string) {
  return !keyword || [user.username, user.fullName ?? "", user.email].some((value) => value.toLocaleLowerCase().includes(keyword));
}

function firstParam(value: string | string[] | undefined) {
  return Array.isArray(value) ? value[0] : value;
}

async function fetchAllRoles(actorGuid: string, signal?: AbortSignal) {
  const pageSize = 200;
  const roles: IdentityRole[] = [];
  for (let page = 1; ; page += 1) {
    const result = await fetchIdentityRoles({ page, pageSize }, actorGuid, signal);
    roles.push(...result.items);
    if (page >= result.totalPages) return roles;
  }
}

export default function PermissionDetailScreen() {
  const params = useLocalSearchParams<{ code?: string | string[] }>();
  const code = firstParam(params.code)?.trim() ?? "";
  const { actorKey } = usePermissionSession();
  // 账号或权限代码变化时强制重建状态，避免分配草稿跨身份残留。
  return <PermissionDetailContent key={`${actorKey}:${code.toLocaleLowerCase()}`} code={code} />;
}

function PermissionDetailContent({ code }: { code: string }) {
  const router = useRouter();
  const queryClient = useQueryClient();
  const { copy, appLanguage } = usePermissionCopy();
  const { allowed, actorKey, capabilities, access } = usePermissionSession();
  // 权限反查用户是管理员专属接口（服务端 ADMIN_REQUIRED），非管理员不发请求也不展示该区块。
  const canViewUsers = allowed && access.isAdmin;
  const [assignOpen, setAssignOpen] = useState(false);
  const [assignSearch, setAssignSearch] = useState("");
  const [onlySelected, setOnlySelected] = useState(false);
  const [assignment, setAssignment] = useState<RoleAssignmentDraft | null>(null);
  const [writeForbidden, setWriteForbidden] = useState(false);
  const [notice, setNotice] = useState("");
  const [userAssignOpen, setUserAssignOpen] = useState(false);
  const [userSearch, setUserSearch] = useState("");
  const debouncedUserSearch = useIdentitySearch(userSearch);
  const [onlySelectedUsers, setOnlySelectedUsers] = useState(false);
  // 复用 GUID 集合草稿：baseline 为服务端直接授权用户，保存时只提交增量。
  const [userAssignment, setUserAssignment] = useState<RoleAssignmentDraft | null>(null);
  // 搜索结果按关键字分页，切换关键字后旧页不在当前数据里；这里记住见过的用户，「仅看已选」时仍能显示名称。
  const [seenUsers, setSeenUsers] = useState<Map<string, PermissionUserLite>>(() => new Map());

  const permissions = usePermissionItems({ actorKey, enabled: allowed, appLanguage });
  const item = useMemo(() => findPermissionListItem(permissions.items, code), [code, permissions.items]);
  const superAdminRoleNames = permissions.catalog?.superAdminRoleNames;

  const assignedQuery = useQuery({
    queryKey: permissionQueryKeys.permissionRoles(actorKey, code),
    enabled: allowed && Boolean(code),
    retry: false,
    queryFn: ({ signal }) => fetchIdentityPermissionRoles(code, actorKey, signal),
  });
  const allRolesQuery = useQuery({
    queryKey: permissionQueryKeys.allRoles(actorKey),
    enabled: allowed && Boolean(code),
    retry: false,
    queryFn: ({ signal }) => fetchAllRoles(actorKey, signal),
  });

  const assignedUsersQuery = useQuery({
    queryKey: permissionQueryKeys.permissionUsers(actorKey, code),
    enabled: canViewUsers && Boolean(code),
    retry: false,
    queryFn: ({ signal }) => fetchIdentityPermissionUsers(code, actorKey, signal),
  });
  const userCandidatesQuery = useInfiniteQuery({
    queryKey: permissionQueryKeys.permissionUserCandidates(actorKey, debouncedUserSearch),
    enabled: userAssignOpen && capabilities.canManage && !writeForbidden,
    retry: false,
    initialPageParam: 1,
    queryFn: ({ pageParam, signal }) => fetchIdentityUsers({ page: pageParam, pageSize: 50, search: debouncedUserSearch || undefined }, actorKey, signal),
    getNextPageParam: (lastPage) => lastPage.page < lastPage.totalPages ? lastPage.page + 1 : undefined,
  });

  const isLockedRole = useCallback(
    (role: Pick<IdentityRole, "roleName">) => isImplicitAllRoleName(role.roleName, superAdminRoleNames),
    [superAdminRoleNames],
  );
  // 服务端分配接口会跳过超级管理员角色，因此显式分配列表里不会出现它们；详情页单独补上「隐式」行。
  const implicitRoles = useMemo(() => (allRolesQuery.data ?? []).filter(isLockedRole), [allRolesQuery.data, isLockedRole]);
  const explicitRoles = useMemo(() => (assignedQuery.data ?? []).filter((role) => !isLockedRole(role)), [assignedQuery.data, isLockedRole]);
  const explicitGuids = useMemo(() => explicitRoles.map((role) => role.roleGUID), [explicitRoles]);

  useEffect(() => {
    if (!assignedQuery.data) return;
    // 后台刷新不能覆盖用户尚未保存的勾选；仅干净草稿接受服务器最新值。
    setAssignment((current) => current && isRoleAssignmentDirty(current) ? current : createRoleAssignmentDraft(explicitGuids));
  }, [assignedQuery.data, explicitGuids]);

  const assignedUsers = useMemo(() => assignedUsersQuery.data ?? [], [assignedUsersQuery.data]);
  const assignedUserGuids = useMemo(() => assignedUsers.map((user) => user.userGUID), [assignedUsers]);

  useEffect(() => {
    if (!assignedUsersQuery.data) return;
    // 与角色草稿相同：后台刷新只覆盖干净草稿，不吞掉尚未保存的勾选。
    setUserAssignment((current) => current && isRoleAssignmentDirty(current) ? current : createRoleAssignmentDraft(assignedUserGuids));
  }, [assignedUsersQuery.data, assignedUserGuids]);

  useEffect(() => {
    const incoming = [...assignedUsers, ...(userCandidatesQuery.data?.pages.flatMap((page) => page.items) ?? [])];
    if (incoming.length === 0) return;
    setSeenUsers((current) => {
      let changed = false;
      const next = new Map(current);
      for (const user of incoming) {
        const previous = next.get(user.userGUID);
        if (!previous || previous.username !== user.username || previous.fullName !== user.fullName || previous.email !== user.email || previous.isActive !== user.isActive) {
          next.set(user.userGUID, toPermissionUserLite(user));
          changed = true;
        }
      }
      return changed ? next : current;
    });
  }, [assignedUsers, userCandidatesQuery.data]);

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
      throw Object.assign(new Error("IDENTITY_PERMISSION_SESSION_CHANGED"), { status: 403 });
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
  const handleMutationError = useCallback((error: unknown) => {
    const status = getIdentityAdminErrorMeta(error).status;
    if ([401, 403].includes(status ?? 0) || isUncertainRoleWrite(error)) {
      setWriteForbidden(true);
      setNotice([401, 403].includes(status ?? 0) ? copy.forbidden : copy.uncertainWrite);
    } else setNotice(copy.saveFailed);
  }, [copy.forbidden, copy.saveFailed, copy.uncertainWrite]);

  const assignMutation = useMutation({
    retry: false,
    mutationFn: async () => {
      if (!assignment) throw new Error("Assignment draft unavailable");
      assertCurrentSession("Roles.ManagePermissions");
      await assignIdentityPermissionRoles(code, assignment.selected, actorKey);
      assertCurrentSession("Roles.ManagePermissions");
      const verified = await fetchIdentityPermissionRoles(code, actorKey);
      assertCurrentSession("Roles.ManagePermissions");
      const verifiedGuids = verified.filter((role) => !isLockedRole(role)).map((role) => role.roleGUID);
      if (!areRoleGuidsEqual(assignment.selected, verifiedGuids)) throw new Error("PERMISSION_ROLE_READBACK_MISMATCH");
      return verified;
    },
    onSuccess: async (verified) => {
      if (!canApplyMutationResult("Roles.ManagePermissions")) return;
      queryClient.setQueryData(permissionQueryKeys.permissionRoles(actorKey, code), verified);
      setAssignment(createRoleAssignmentDraft(verified.filter((role) => !isLockedRole(role)).map((role) => role.roleGUID)));
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: permissionQueryKeys.roleCounts(actorKey) }),
        // 角色详情页的权限勾选与本次分配是同一份数据，需一并失效。
        queryClient.invalidateQueries({ queryKey: ["identity-admin", actorKey, "rolePermissionState"] }),
      ]);
      if (!canApplyMutationResult("Roles.ManagePermissions")) return;
      setAssignOpen(false);
      setNotice(copy.assignmentSaved);
    },
    onError: handleMutationError,
  });
  const deleteMutation = useMutation({
    retry: false,
    mutationFn: async () => {
      assertCurrentSession("Roles.ManagePermissions");
      await deleteIdentitySysPermission(code, actorKey);
      assertCurrentSession("Roles.ManagePermissions");
    },
    onSuccess: async () => {
      if (!canApplyMutationResult("Roles.ManagePermissions")) return;
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: permissionQueryKeys.catalog(actorKey) }),
        queryClient.invalidateQueries({ queryKey: permissionQueryKeys.sysPermissions(actorKey) }),
        queryClient.invalidateQueries({ queryKey: permissionQueryKeys.roleCounts(actorKey) }),
        queryClient.invalidateQueries({ queryKey: ["identity-admin", actorKey, "rolePermissionState"] }),
        queryClient.invalidateQueries({ queryKey: permissionQueryKeys.permissionUsers(actorKey, code) }),
      ]);
      if (router.canGoBack()) router.back();
      else router.replace("/(shell)/permissions");
    },
    onError: handleMutationError,
  });

  const userAssignMutation = useMutation({
    retry: false,
    mutationFn: async () => {
      if (!userAssignment) throw new Error("User assignment draft unavailable");
      const delta = getRoleAssignmentDelta(userAssignment);
      assertCurrentSession("Roles.ManagePermissions");
      await assignIdentityPermissionUsers(code, { addUserGuids: delta.added, removeUserGuids: delta.removed }, actorKey);
      assertCurrentSession("Roles.ManagePermissions");
      const verified = await fetchIdentityPermissionUsers(code, actorKey);
      assertCurrentSession("Roles.ManagePermissions");
      if (!isPermissionUserDeltaApplied(delta, verified.map((user) => user.userGUID))) throw new Error("PERMISSION_USER_READBACK_MISMATCH");
      return verified;
    },
    onSuccess: async (verified) => {
      if (!canApplyMutationResult("Roles.ManagePermissions")) return;
      queryClient.setQueryData(permissionQueryKeys.permissionUsers(actorKey, code), verified);
      setUserAssignment(createRoleAssignmentDraft(verified.map((user) => user.userGUID)));
      await Promise.all([
        // 用户授权页与员工访问管理读取同一张直接权限表，需一并失效。
        queryClient.invalidateQueries({ queryKey: ["identity-admin", actorKey, "access-permissions"] }),
        queryClient.invalidateQueries({ queryKey: ["userAccessManagement"] }),
      ]);
      if (!canApplyMutationResult("Roles.ManagePermissions")) return;
      setUserAssignOpen(false);
      setNotice(copy.userAssignmentSaved);
    },
    onError: (error) => {
      // 权限未入库是确定的业务拒绝，给出可操作的原因而不是冻结写入。
      if (getIdentityAdminErrorMeta(error).code === "PERMISSION_NOT_FOUND") {
        setNotice(copy.permissionNotPersisted);
        return;
      }
      handleMutationError(error);
    },
  });

  const handleVerifyRefresh = useCallback(async () => {
    if (!canApplyMutationResult("Roles.View")) return;
    try {
      assertCurrentSession("Roles.View");
      const [verified, verifiedUsers] = await Promise.all([
        fetchIdentityPermissionRoles(code, actorKey),
        canViewUsers ? fetchIdentityPermissionUsers(code, actorKey) : Promise.resolve(null),
        permissions.refetch(),
      ]);
      assertCurrentSession("Roles.View");
      queryClient.setQueryData(permissionQueryKeys.permissionRoles(actorKey, code), verified);
      setAssignment(createRoleAssignmentDraft(verified.filter((role) => !isLockedRole(role)).map((role) => role.roleGUID)));
      if (verifiedUsers) {
        queryClient.setQueryData(permissionQueryKeys.permissionUsers(actorKey, code), verifiedUsers);
        setUserAssignment(createRoleAssignmentDraft(verifiedUsers.map((user) => user.userGUID)));
      }
      setWriteForbidden(false);
      setNotice(copy.verificationComplete);
    } catch {
      if (canApplyMutationResult("Roles.View")) setNotice(copy.verificationFailed);
    }
  }, [actorKey, assertCurrentSession, canApplyMutationResult, canViewUsers, code, copy.verificationComplete, copy.verificationFailed, isLockedRole, permissions, queryClient]);

  const saving = assignMutation.isPending || deleteMutation.isPending || userAssignMutation.isPending;
  const canWrite = capabilities.canManage && !writeForbidden && !saving;
  const delta = assignment ? getRoleAssignmentDelta(assignment) : { added: [], removed: [] };
  const assignmentDirty = Boolean(assignment && isRoleAssignmentDirty(assignment));
  const userDelta = userAssignment ? getRoleAssignmentDelta(userAssignment) : { added: [], removed: [] };
  const userAssignmentDirty = Boolean(userAssignment && isRoleAssignmentDirty(userAssignment));
  const resetUserAssignment = () => setUserAssignment(createRoleAssignmentDraft(assignedUserGuids));

  const sheetUsers = useMemo<PermissionUserLite[]>(() => {
    const keyword = userSearch.trim().toLocaleLowerCase();
    if (onlySelectedUsers) {
      // 已选名单可能跨多次搜索，改为本地过滤；未见过详情的 GUID 以 GUID 兜底展示，保证仍可取消勾选。
      return (userAssignment?.selected ?? [])
        .map((guid) => seenUsers.get(guid) ?? { userGUID: guid, username: guid, email: "", isActive: true })
        .filter((user) => matchesPermissionUser(user, keyword))
        .sort((a, b) => formatPermissionUserName(a).localeCompare(formatPermissionUserName(b)));
    }
    const unique = new Map<string, PermissionUserLite>();
    for (const user of userCandidatesQuery.data?.pages.flatMap((page) => page.items) ?? []) {
      if (!unique.has(user.userGUID)) unique.set(user.userGUID, toPermissionUserLite(user));
    }
    return Array.from(unique.values());
  }, [onlySelectedUsers, seenUsers, userAssignment?.selected, userCandidatesQuery.data, userSearch]);

  const sheetRoles = useMemo(() => {
    const keyword = assignSearch.trim().toLocaleLowerCase();
    const selected = new Set(assignment?.selected ?? []);
    return (allRolesQuery.data ?? []).filter((role) => {
      const locked = isLockedRole(role);
      if (onlySelected && !locked && !selected.has(role.roleGUID)) return false;
      if (!keyword) return true;
      return [role.roleName, localizeAccessRoleName(role.roleName, appLanguage), role.description ?? ""]
        .some((value) => value.toLocaleLowerCase().includes(keyword));
    });
  }, [allRolesQuery.data, appLanguage, assignSearch, assignment?.selected, isLockedRole, onlySelected]);

  const confirmDelete = () => {
    if (!item) return;
    Alert.alert(copy.deleteTitle, interpolate(copy.deleteMessage, { name: item.name }), [
      { text: copy.cancel, style: "cancel" },
      { text: copy.delete, style: "destructive", onPress: () => deleteMutation.mutate() },
    ]);
  };

  if (!allowed) {
    return <AdminScreen title={copy.detailTitle}><View style={localStyles.message}><Text>{copy.accessDenied}</Text></View></AdminScreen>;
  }
  if (permissions.isPending) return <AdminScreen title={copy.detailTitle}><ActivityIndicator style={{ flex: 1 }} /></AdminScreen>;
  if (permissions.error) return <AdminScreen title={copy.detailTitle}><AdminError error={permissions.error} onRetry={() => void permissions.refetch()} /></AdminScreen>;
  if (!item) return <AdminScreen title={copy.detailTitle}><AdminEmpty text={copy.notFound} /></AdminScreen>;

  const deleteHint = item.isSystem ? copy.deleteSystemHint : !item.deletable ? copy.deleteUnsavedHint : "";
  const footer = capabilities.canManage ? (
    <>
      <View style={localStyles.footerRow}>
        <Button mode="contained" style={[styles.button, { flex: 1 }]} disabled={!canWrite || assignedQuery.isPending || allRolesQuery.isPending} onPress={() => { setAssignSearch(""); setOnlySelected(false); setAssignOpen(true); }}>{copy.assignRoles}</Button>
        <Button mode="contained" style={[styles.button, { flex: 1 }]} disabled={!canWrite || !canViewUsers || assignedUsersQuery.isPending || Boolean(assignedUsersQuery.error)}
          onPress={() => { setUserSearch(""); setOnlySelectedUsers(false); setUserAssignOpen(true); }}>{copy.assignUsers}</Button>
      </View>
      <Button mode="outlined" textColor={C.danger} style={styles.button} disabled={!canWrite || !item.deletable} loading={deleteMutation.isPending} onPress={confirmDelete}>
        {deleteHint ? `${copy.deletePermission}（${deleteHint}）` : copy.deletePermission}
      </Button>
    </>
  ) : undefined;

  return (
    <AdminScreen
      title={copy.detailTitle}
      action={writeForbidden ? <IconButton icon="refresh" accessibilityLabel={copy.retry} onPress={() => void handleVerifyRefresh()} /> : undefined}
      footer={footer}
    >
      <AdminScroll>
        <View style={localStyles.hero}>
          <View style={localStyles.heroRow}>
            <View style={localStyles.heroIcon}><Icon source="key-outline" size={22} color={C.action} /></View>
            <View style={{ flex: 1, gap: 2 }}>
              <Text style={localStyles.heroTitle}>{item.name}</Text>
              <Text style={localStyles.code} selectable>{item.code}</Text>
            </View>
          </View>
          <View style={localStyles.tags}>
            <View style={localStyles.tag}><Text style={localStyles.tagText}>{item.categoryName}</Text></View>
            <View style={localStyles.tag}>
              {item.isSystem ? <Icon source="lock-outline" size={12} color={C.textSecondary} /> : null}
              <Text style={[localStyles.tagText, { color: C.textSecondary }]}>{item.isSystem ? copy.systemPermission : copy.customPermission}</Text>
            </View>
          </View>
        </View>

        <View>
          <Text style={styles.label}>{copy.description}</Text>
          <Text style={item.description ? localStyles.body : styles.muted}>{item.description || copy.noDescription}</Text>
        </View>
        {item.createdAt || item.updatedAt ? (
          <View style={styles.section}>
            {item.createdAt ? <AdminRow title={copy.createdAt} subtitle={item.createdBy} trailing={<Text style={styles.muted}>{identityDate(item.createdAt)}</Text>} /> : null}
            {item.updatedAt ? <AdminRow title={copy.updatedAt} subtitle={item.updatedBy} trailing={<Text style={styles.muted}>{identityDate(item.updatedAt)}</Text>} /> : null}
          </View>
        ) : null}

        <View>
          <Text style={styles.value}>{interpolate(copy.assignedRolesCount, { count: explicitRoles.length + implicitRoles.length })}</Text>
          {assignedQuery.isPending || allRolesQuery.isPending ? <ActivityIndicator style={{ marginVertical: 16 }} /> : assignedQuery.error || allRolesQuery.error ? (
            <AdminError error={assignedQuery.error ?? allRolesQuery.error} onRetry={() => { void assignedQuery.refetch(); void allRolesQuery.refetch(); }} />
          ) : explicitRoles.length + implicitRoles.length === 0 ? <AdminEmpty text={copy.noAssignedRoles} /> : (
            <View style={[styles.section, { marginTop: 8 }]}>
              {implicitRoles.map((role) => (
                <AdminRow key={role.roleGUID} icon="shield-account-outline" title={localizeAccessRoleName(role.roleName, appLanguage)}
                  subtitle={`${copy.implicitRoleHint} · ${interpolate(copy.membersCount, { count: role.userCount })}`}
                  trailing={<View style={localStyles.tag}><Text style={[localStyles.tagText, { color: C.textSecondary }]}>{copy.implicitRole}</Text></View>}
                  onPress={() => router.push({ pathname: "/(shell)/roles/[roleGuid]", params: { roleGuid: role.roleGUID } })} />
              ))}
              {explicitRoles.map((role) => (
                <AdminRow key={role.roleGUID} icon="shield-account-outline" title={localizeAccessRoleName(role.roleName, appLanguage)}
                  subtitle={`${copy.explicitRoleHint} · ${interpolate(copy.membersCount, { count: role.userCount })}`}
                  trailing={<StatusTag active={role.isActive} />}
                  onPress={() => router.push({ pathname: "/(shell)/roles/[roleGuid]", params: { roleGuid: role.roleGUID } })} />
              ))}
            </View>
          )}
        </View>

        {canViewUsers ? (
          <View>
            <Text style={styles.value}>{interpolate(copy.assignedUsersCount, { count: assignedUsers.length })}</Text>
            <Text style={[styles.muted, { marginTop: 2 }]}>{copy.assignedUsersHint}</Text>
            {assignedUsersQuery.isPending ? <ActivityIndicator style={{ marginVertical: 16 }} /> : assignedUsersQuery.error ? (
              <AdminError error={assignedUsersQuery.error} onRetry={() => void assignedUsersQuery.refetch()} />
            ) : assignedUsers.length === 0 ? <AdminEmpty text={copy.noAssignedUsers} /> : (
              <View style={[styles.section, { marginTop: 8 }]}>
                {assignedUsers.map((user) => (
                  <AdminRow key={user.userGUID} icon="account-outline" title={formatPermissionUserName(user)}
                    subtitle={[user.email, interpolate(copy.grantedAt, { time: identityDate(user.assignedAt) })].filter(Boolean).join(" · ")}
                    trailing={<StatusTag active={user.isActive} />}
                    onPress={() => router.push({ pathname: "/(shell)/user-admin/[userGuid]", params: { userGuid: user.userGUID } })} />
                ))}
              </View>
            )}
          </View>
        ) : null}
      </AdminScroll>

      <BusinessSheet
        visible={assignOpen}
        title={copy.assignRoles}
        subtitle={`${item.name} · ${item.code}`}
        dismissable={!assignMutation.isPending}
        onDismiss={() => {
          if (assignmentDirty) {
            Alert.alert(copy.unsavedTitle, copy.unsavedMessage, [
              { text: copy.cancel, style: "cancel" },
              { text: copy.discard, style: "destructive", onPress: () => { setAssignment(createRoleAssignmentDraft(explicitGuids)); setAssignOpen(false); } },
            ]);
            return;
          }
          setAssignOpen(false);
        }}
        footer={
          <View style={localStyles.sheetFooter}>
            <Button mode="outlined" style={[styles.button, { flex: 1 }]} disabled={assignMutation.isPending} onPress={() => { setAssignment(createRoleAssignmentDraft(explicitGuids)); setAssignOpen(false); }}>{copy.cancel}</Button>
            <Button mode="contained" style={[styles.button, { flex: 2 }]} disabled={!canWrite || !assignmentDirty} loading={assignMutation.isPending} onPress={() => assignMutation.mutate()}>
              {assignmentDirty ? interpolate(copy.saveAssignment, { added: delta.added.length, removed: delta.removed.length }) : copy.saveAssignmentIdle}
            </Button>
          </View>
        }
      >
        <SearchField value={assignSearch} onChange={setAssignSearch} placeholder={copy.assignRolesSearch} />
        <View style={localStyles.sheetToolbar}>
          <Text style={styles.muted}>{interpolate(copy.selectedRoles, { selected: (assignment?.selected.length ?? 0) + implicitRoles.length, total: allRolesQuery.data?.length ?? 0 })}</Text>
          <Button compact onPress={() => setOnlySelected((value) => !value)}>{onlySelected ? copy.showAll : copy.onlySelected}</Button>
        </View>
        {sheetRoles.length === 0 ? <AdminEmpty text={copy.noRoles} /> : sheetRoles.map((role) => {
          const locked = isLockedRole(role);
          const checked = locked || Boolean(assignment?.selected.includes(role.roleGUID));
          return (
            <TouchableRipple key={role.roleGUID} disabled={locked || !canWrite} accessibilityRole="checkbox" accessibilityState={{ checked, disabled: locked || !canWrite }}
              onPress={() => setAssignment((current) => current ? toggleRoleAssignment(current, role.roleGUID) : current)}>
              <View style={localStyles.sheetRow}>
                <Checkbox status={checked ? "checked" : "unchecked"} disabled={locked || !canWrite} />
                <View style={{ flex: 1 }}>
                  <Text>{localizeAccessRoleName(role.roleName, appLanguage)}</Text>
                  <Text style={styles.muted}>{locked ? copy.lockedRoleHint : interpolate(copy.membersCount, { count: role.userCount })}</Text>
                </View>
                {locked ? <View style={localStyles.tag}><Text style={[localStyles.tagText, { color: C.textSecondary }]}>{copy.lockedRole}</Text></View> : <StatusTag active={role.isActive} />}
              </View>
            </TouchableRipple>
          );
        })}
      </BusinessSheet>
      <BusinessSheet
        visible={userAssignOpen}
        title={copy.assignUsers}
        subtitle={`${item.name} · ${item.code}`}
        dismissable={!userAssignMutation.isPending}
        onDismiss={() => {
          if (userAssignmentDirty) {
            Alert.alert(copy.unsavedTitle, copy.unsavedMessage, [
              { text: copy.cancel, style: "cancel" },
              { text: copy.discard, style: "destructive", onPress: () => { resetUserAssignment(); setUserAssignOpen(false); } },
            ]);
            return;
          }
          setUserAssignOpen(false);
        }}
        footer={
          <View style={localStyles.sheetFooter}>
            <Button mode="outlined" style={[styles.button, { flex: 1 }]} disabled={userAssignMutation.isPending} onPress={() => { resetUserAssignment(); setUserAssignOpen(false); }}>{copy.cancel}</Button>
            <Button mode="contained" style={[styles.button, { flex: 2 }]} disabled={!canWrite || !userAssignmentDirty} loading={userAssignMutation.isPending} onPress={() => userAssignMutation.mutate()}>
              {userAssignmentDirty ? interpolate(copy.saveAssignment, { added: userDelta.added.length, removed: userDelta.removed.length }) : copy.saveAssignmentIdle}
            </Button>
          </View>
        }
      >
        <SearchField value={userSearch} onChange={setUserSearch} placeholder={copy.assignUsersSearch} />
        <View style={localStyles.sheetToolbar}>
          <Text style={styles.muted}>{interpolate(copy.selectedUsers, { count: userAssignment?.selected.length ?? 0 })}</Text>
          <Button compact onPress={() => setOnlySelectedUsers((value) => !value)}>{onlySelectedUsers ? copy.showAll : copy.onlySelected}</Button>
        </View>
        {!onlySelectedUsers && userCandidatesQuery.isPending ? <ActivityIndicator style={{ marginVertical: 16 }} /> : !onlySelectedUsers && userCandidatesQuery.error ? (
          <AdminError error={userCandidatesQuery.error} onRetry={() => void userCandidatesQuery.refetch()} />
        ) : sheetUsers.length === 0 ? <AdminEmpty text={copy.noUsers} /> : sheetUsers.map((user) => {
          const checked = Boolean(userAssignment?.selected.includes(user.userGUID));
          return (
            <TouchableRipple key={user.userGUID} disabled={!canWrite} accessibilityRole="checkbox" accessibilityState={{ checked, disabled: !canWrite }}
              onPress={() => setUserAssignment((current) => current ? toggleRoleAssignment(current, user.userGUID) : current)}>
              <View style={localStyles.sheetRow}>
                <Checkbox status={checked ? "checked" : "unchecked"} disabled={!canWrite} />
                <View style={{ flex: 1 }}>
                  <Text>{formatPermissionUserName(user)}</Text>
                  {user.email ? <Text style={styles.muted}>{user.email}</Text> : null}
                </View>
                <StatusTag active={user.isActive} />
              </View>
            </TouchableRipple>
          );
        })}
        {!onlySelectedUsers && userCandidatesQuery.hasNextPage ? (
          <Button compact loading={userCandidatesQuery.isFetchingNextPage} disabled={userCandidatesQuery.isFetchingNextPage} onPress={() => void userCandidatesQuery.fetchNextPage()}>{copy.loadMoreUsers}</Button>
        ) : null}
      </BusinessSheet>
      <Snackbar visible={Boolean(notice)} onDismiss={() => setNotice("")} duration={5000}>{notice}</Snackbar>
    </AdminScreen>
  );
}

const localStyles = StyleSheet.create({
  message: { margin: 16, padding: 16, borderRadius: 8, backgroundColor: C.surfaceMuted },
  hero: { padding: 14, borderRadius: 12, backgroundColor: "#EFF6FF", gap: 10 },
  heroRow: { flexDirection: "row", alignItems: "center", gap: 10 },
  heroIcon: { width: 40, height: 40, borderRadius: 10, backgroundColor: C.white, alignItems: "center", justifyContent: "center" },
  heroTitle: { fontSize: 17, lineHeight: 24, fontWeight: "700", color: C.textPrimary },
  code: { fontSize: 12, lineHeight: 16, color: C.textSecondary, fontFamily: "monospace" },
  tags: { flexDirection: "row", flexWrap: "wrap", gap: 6 },
  tag: { flexDirection: "row", alignItems: "center", gap: 3, paddingHorizontal: 7, paddingVertical: 3, borderRadius: 5, backgroundColor: C.white },
  tagText: { fontSize: 11, fontWeight: "600", color: C.action },
  body: { fontSize: 14, lineHeight: 21, color: C.textPrimary },
  sheetFooter: { flexDirection: "row", gap: 8, paddingBottom: 4 },
  footerRow: { flexDirection: "row", gap: 8 },
  sheetToolbar: { flexDirection: "row", alignItems: "center", justifyContent: "space-between" },
  sheetRow: { minHeight: 52, flexDirection: "row", alignItems: "center", gap: 8, borderTopWidth: StyleSheet.hairlineWidth, borderTopColor: C.outlineMuted },
});
