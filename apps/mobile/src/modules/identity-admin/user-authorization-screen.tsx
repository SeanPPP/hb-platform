import { useEffect, useMemo, useRef, useState } from "react";
import { Alert, ScrollView, View } from "react-native";
import { type NavigationAction, useNavigation, usePreventRemove } from "@react-navigation/native";
import { useLocalSearchParams } from "expo-router";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Button, Checkbox, Divider, Snackbar, Switch, Text, TouchableRipple } from "react-native-paper";
import {
  buildDirectPermissionDraft,
  buildUserAccessStoreAssignments,
  getAccessPermissionSelectionState,
  getAccessRoleSelectionState,
  getUserAccessStoreState,
  isStoreManagerRoleName,
  isStoreStaffRoleName,
  setUserAccessStoreState,
  toggleDirectPermission,
} from "@/modules/users/access-management";
import type { DirectPermissionDraft, UserAccessStoreAssignment } from "@/modules/users/access-management-types";
import { useAuthStore } from "@/store/auth-store";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import {
  localizeAccessPermission,
  localizeAccessPermissionCategory,
} from "@/modules/users/access-permission-presentation";
import en from "@/locales/en/identityAccess.json";
import zh from "@/locales/zh/identityAccess.json";
import {
  assignUserAccessRoles,
  assignUserAccessStores,
  assignUserDirectPermissions,
  fetchAccessRoleCatalog,
  fetchAccessStoreCatalog,
  fetchIdentityUserDetail,
  fetchUserAccessPermissionAccess,
  fetchUserAccessRoles,
  fetchUserAccessStores,
  getIdentityAdminErrorMeta,
} from "./api";
import { getRoleMenuDefinitions } from "./role-menu-catalog";
import {
  type AuthorizationSection,
  type MobileMenuItem,
  type MobileMenuSection,
  areStringSetsEqual,
  splitMobileMenuItems,
  togglePermissionCodes,
  userMenuPermissionState,
} from "./user-authorization-logic";
import { canModifyIdentityUser, isUncertainIdentityWrite, managedIdentityStores } from "./user-logic";
import { useIdentitySession } from "./user-hooks";
import { AdminEmpty, AdminError, AdminRow, AdminScreen, AdminTabs, SearchField, styles } from "./ui";

type Copy = typeof en;
type Resource = "roles" | "stores" | "permissions";

const POS_PERMISSION_PATTERN = /^(Permissions\.)?PosTerminal\./i;
const first = (value: string | string[] | undefined) => (Array.isArray(value) ? value[0] : value) ?? "";
const resourceForSection = (section: AuthorizationSection): Resource =>
  section === "roles" ? "roles" : section === "stores" ? "stores" : "permissions";
const storeKey = (item: UserAccessStoreAssignment) => `${item.storeGUID}:${item.isPrimary ? "manage" : "view"}`;

export default function UserAuthorizationScreen() {
  const params = useLocalSearchParams<{ userGuid?: string | string[]; section?: string | string[] }>();
  const userGuid = first(params.userGuid).trim();
  const initialSection = first(params.section);
  const actorKey = useAuthStore((state) => state.user?.userGUID ?? "");
  // 切换操作者或目标用户时销毁整份草稿，禁止跨身份复用授权状态。
  return <AuthorizationEditor key={`${actorKey}:${userGuid}`} actorKey={actorKey} userGuid={userGuid} initialSection={initialSection} />;
}

function AuthorizationEditor({ actorKey, userGuid, initialSection }: { actorKey: string; userGuid: string; initialSection: string }) {
  const { language } = useAppTranslation();
  const copy: Copy = language.startsWith("zh") ? zh : en;
  const queryClient = useQueryClient();
  const navigation = useNavigation();
  const { user: currentUser, access, allowed, isAuthenticated } = useIdentitySession("Users.View");
  const scoped = access.isStoreLevelManager;
  const [section, setSection] = useState<AuthorizationSection>(
    (["roles", "stores", "permissions", "mobile"] as string[]).includes(initialSection)
      ? initialSection as AuthorizationSection
      : "roles",
  );
  const [roleSearch, setRoleSearch] = useState("");
  const [storeSearch, setStoreSearch] = useState("");
  const [permissionSearch, setPermissionSearch] = useState("");
  const [permissionPlatform, setPermissionPlatform] = useState<"web" | "pos">("web");
  const [roleDraft, setRoleDraft] = useState<string[] | null>(null);
  const [roleBaseline, setRoleBaseline] = useState<string[] | null>(null);
  const [storeDraft, setStoreDraft] = useState<UserAccessStoreAssignment[] | null>(null);
  const [storeBaseline, setStoreBaseline] = useState<UserAccessStoreAssignment[] | null>(null);
  const [permissionDraft, setPermissionDraft] = useState<DirectPermissionDraft | null>(null);
  const [busyResource, setBusyResource] = useState<Resource | null>(null);
  const [frozenResources, setFrozenResources] = useState<Resource[]>([]);
  const [snack, setSnack] = useState("");
  const [departing, setDeparting] = useState(false);
  const pendingAction = useRef<NavigationAction | null>(null);
  const inFlight = useRef(false);

  const targetQuery = useQuery({
    queryKey: ["identity-admin", actorKey, "target", userGuid],
    queryFn: ({ signal }) => fetchIdentityUserDetail(userGuid, actorKey, signal),
    enabled: allowed && Boolean(userGuid),
    retry: false,
  });
  const target = targetQuery.data;
  const canRoles = Boolean(target && canModifyIdentityUser(currentUser, access, target, "Users.ManageRoles"));
  const canStores = Boolean(target && canModifyIdentityUser(currentUser, access, target, "Users.ManageStores"));
  const canPermissions = canRoles || Boolean(
    scoped && target && canModifyIdentityUser(currentUser, access, target, "Users.ManagePosTerminalPermissions"),
  );

  const assignedRolesQuery = useQuery({
    queryKey: ["identity-admin", actorKey, "access-roles", userGuid],
    queryFn: ({ signal }) => fetchUserAccessRoles(userGuid, actorKey, signal),
    enabled: allowed && canRoles,
    retry: false,
  });
  const roleCatalogQuery = useQuery({
    queryKey: ["identity-admin", actorKey, "access-role-catalog"],
    queryFn: ({ signal }) => fetchAccessRoleCatalog(actorKey, signal),
    enabled: allowed && canRoles,
    retry: false,
  });
  const assignedStoresQuery = useQuery({
    queryKey: ["identity-admin", actorKey, "access-stores", userGuid],
    queryFn: ({ signal }) => fetchUserAccessStores(userGuid, actorKey, signal),
    enabled: allowed && canStores,
    retry: false,
  });
  const storeCatalogQuery = useQuery({
    queryKey: ["identity-admin", actorKey, "access-store-catalog"],
    queryFn: ({ signal }) => fetchAccessStoreCatalog(actorKey, signal),
    enabled: allowed && canStores && !scoped,
    retry: false,
  });
  const permissionQuery = useQuery({
    queryKey: ["identity-admin", actorKey, "access-permissions", userGuid],
    queryFn: ({ signal }) => fetchUserAccessPermissionAccess(userGuid, actorKey, signal),
    enabled: allowed && canPermissions,
    retry: false,
  });

  useEffect(() => {
    if (roleDraft !== null || !assignedRolesQuery.data) return;
    const next = assignedRolesQuery.data.filter((role) => !isStoreManagerRoleName(role.roleName)).map((role) => role.roleGUID);
    setRoleDraft(next);
    setRoleBaseline([...next]);
  }, [assignedRolesQuery.data, roleDraft]);
  useEffect(() => {
    if (storeDraft !== null || !assignedStoresQuery.data) return;
    const next = buildUserAccessStoreAssignments(assignedStoresQuery.data);
    setStoreDraft(next);
    setStoreBaseline(next.map((item) => ({ ...item })));
  }, [assignedStoresQuery.data, storeDraft]);
  useEffect(() => {
    if (permissionDraft !== null || !permissionQuery.data) return;
    setPermissionDraft(buildDirectPermissionDraft(permissionQuery.data.state));
  }, [permissionDraft, permissionQuery.data]);

  const tabs = useMemo(() => [
    ...(canRoles ? [{ key: "roles", label: copy.sections.roles }] : []),
    ...(canStores ? [{ key: "stores", label: copy.sections.stores }] : []),
    ...(canPermissions ? [{ key: "permissions", label: copy.sections.permissions }, { key: "mobile", label: copy.sections.mobile }] : []),
  ], [canPermissions, canRoles, canStores, copy.sections]);
  useEffect(() => {
    if (tabs.length && !tabs.some((item) => item.key === section)) setSection(tabs[0].key as AuthorizationSection);
  }, [section, tabs]);

  const roles = useMemo(() => {
    const assigned = assignedRolesQuery.data ?? [];
    const catalog = scoped ? (roleCatalogQuery.data ?? []).filter((role) => isStoreStaffRoleName(role.roleName)) : (roleCatalogQuery.data ?? []);
    return [...catalog, ...assigned].filter((role, index, list) => list.findIndex((item) => item.roleGUID === role.roleGUID) === index);
  }, [assignedRolesQuery.data, roleCatalogQuery.data, scoped]);
  const stores = useMemo(() => {
    const assigned = assignedStoresQuery.data ?? [];
    const catalog = scoped ? managedIdentityStores(currentUser) : (storeCatalogQuery.data ?? []);
    return [...catalog, ...assigned].filter((store, index, list) => Boolean(store.storeGUID) && list.findIndex((item) => item.storeGUID === store.storeGUID) === index);
  }, [assignedStoresQuery.data, currentUser, scoped, storeCatalogQuery.data]);
  const managedStoreGuids = useMemo(() => new Set(managedIdentityStores(currentUser).map((store) => store.storeGUID).filter(Boolean)), [currentUser]);
  const assignablePermissionCodes = useMemo(
    () => permissionQuery.data?.categories.flatMap((category) => category.permissions.map((permission) => permission.name)) ?? [],
    [permissionQuery.data?.categories],
  );
  const mobileDefinitions = useMemo(() => {
    const seen = new Set<string>();
    return getRoleMenuDefinitions(language).filter((definition) => {
      if (definition.platform !== "mobile" || seen.has(definition.key)) return false;
      seen.add(definition.key);
      return true;
    });
  }, [language]);
  const mobileItems = useMemo(() => {
    if (!permissionDraft || !permissionQuery.data) return [];
    return mobileDefinitions.map((definition): MobileMenuItem => {
      const state = userMenuPermissionState(definition, permissionDraft, assignablePermissionCodes, permissionQuery.data.state);
      return { key: definition.key, title: definition.title, permissionCodes: definition.permissionCodes, visible: state.visible, locked: state.locked };
    });
  }, [assignablePermissionCodes, mobileDefinitions, permissionDraft, permissionQuery.data]);
  const mobileSections = useMemo(() => splitMobileMenuItems(mobileItems), [mobileItems]);

  const roleDirty = Boolean(roleDraft && roleBaseline && !areStringSetsEqual(roleDraft, roleBaseline));
  const storeDirty = Boolean(storeDraft && storeBaseline && !areStringSetsEqual(storeDraft.map(storeKey), storeBaseline.map(storeKey)));
  const permissionDirty = Boolean(permissionDraft && !areStringSetsEqual(permissionDraft.selectedCodes, permissionDraft.baselineCodes));
  const anyDirty = roleDirty || storeDirty || permissionDirty;
  const activeResource = resourceForSection(section);
  const activeDirty = activeResource === "roles" ? roleDirty : activeResource === "stores" ? storeDirty : permissionDirty;
  const activeFrozen = frozenResources.includes(activeResource);
  const activeReady = activeResource === "roles"
    ? roleDraft !== null && roleBaseline !== null && Boolean(assignedRolesQuery.data) && Boolean(roleCatalogQuery.data) && !assignedRolesQuery.error && !roleCatalogQuery.error
    : activeResource === "stores"
      ? storeDraft !== null && storeBaseline !== null && Boolean(assignedStoresQuery.data) && (scoped || Boolean(storeCatalogQuery.data)) && !assignedStoresQuery.error && !storeCatalogQuery.error
      : permissionDraft !== null && Boolean(permissionQuery.data) && !permissionQuery.error;
  const activeAllowed = activeResource === "roles" ? canRoles : activeResource === "stores" ? canStores : canPermissions;
  const canSave = activeAllowed && activeReady && activeDirty && !activeFrozen && busyResource === null;
  const canEditRole = (locked: boolean) => activeReady && !activeFrozen && !busyResource && !locked;

  const isCurrentSession = () => {
    const state = useAuthStore.getState();
    return state.user?.userGUID === actorKey && state.sessionKind === "account" && state.isAuthenticated && !state.iosReviewOfflineGuardActive;
  };
  const freeze = (resource: Resource, error: unknown) => {
    setFrozenResources((current) => current.includes(resource) ? current : [...current, resource]);
    setSnack(getIdentityAdminErrorMeta(error).status === 403 ? copy.forbidden : copy.verifyRequired);
  };
  const refreshPermissionInheritance = async () => {
    const next = await fetchUserAccessPermissionAccess(userGuid, actorKey);
    if (!isCurrentSession()) return;
    queryClient.setQueryData(["identity-admin", actorKey, "access-permissions", userGuid], next);
    // 角色或分店保存只刷新继承项，保留尚未保存的直接权限草稿。
    setPermissionDraft((current) => current ? { ...current, inheritedPermissionCodes: [...next.state.inheritedPermissionCodes] } : buildDirectPermissionDraft(next.state));
  };
  const refreshPermissionInheritanceSafely = async () => {
    if (!canPermissions) return true;
    try {
      await refreshPermissionInheritance();
      return isCurrentSession();
    } catch (error) {
      if (isCurrentSession()) freeze("permissions", error);
      return false;
    }
  };
  const invalidateRelatedQueries = async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ["identity-admin", actorKey] }),
      queryClient.invalidateQueries({ queryKey: ["userAccessManagement"] }),
      queryClient.invalidateQueries({ queryKey: ["storeUsers"] }),
    ]);
  };

  const save = async () => {
    const resource = activeResource;
    if (inFlight.current || !canSave || !isCurrentSession()) return;
    inFlight.current = true;
    setBusyResource(resource);
    let postCompleted = false;
    try {
      if (resource === "roles" && roleDraft) {
        await assignUserAccessRoles({ userGuid, roleGuids: roleDraft, roleCatalog: roles }, actorKey);
        postCompleted = true;
        if (!isCurrentSession()) return;
        const verified = await fetchUserAccessRoles(userGuid, actorKey);
        if (!isCurrentSession()) return;
        const next = verified.filter((role) => !isStoreManagerRoleName(role.roleName)).map((role) => role.roleGUID);
        setRoleDraft(next); setRoleBaseline([...next]);
        queryClient.setQueryData(["identity-admin", actorKey, "access-roles", userGuid], verified);
        await refreshPermissionInheritanceSafely();
        if (!isCurrentSession()) return;
      } else if (resource === "stores" && storeDraft) {
        await assignUserAccessStores({ userGuid, assignments: storeDraft }, actorKey);
        postCompleted = true;
        if (!isCurrentSession()) return;
        const verified = await fetchUserAccessStores(userGuid, actorKey);
        if (!isCurrentSession()) return;
        const next = buildUserAccessStoreAssignments(verified);
        setStoreDraft(next); setStoreBaseline(next.map((item) => ({ ...item })));
        queryClient.setQueryData(["identity-admin", actorKey, "access-stores", userGuid], verified);
        await refreshPermissionInheritanceSafely();
        if (!isCurrentSession()) return;
      } else if (resource === "permissions" && permissionDraft) {
        await assignUserDirectPermissions({ userGuid, permissions: permissionDraft.selectedCodes }, actorKey);
        postCompleted = true;
        if (!isCurrentSession()) return;
        const verified = await fetchUserAccessPermissionAccess(userGuid, actorKey);
        if (!isCurrentSession()) return;
        setPermissionDraft(buildDirectPermissionDraft(verified.state));
        queryClient.setQueryData(["identity-admin", actorKey, "access-permissions", userGuid], verified);
      }
      await invalidateRelatedQueries();
      if (!isCurrentSession()) return;
      setFrozenResources((current) => current.filter((item) => item !== resource));
      setSnack(copy.saved);
    } catch (error) {
      if (!isCurrentSession()) return;
      const status = getIdentityAdminErrorMeta(error).status;
      if (postCompleted || status === 403 || isUncertainIdentityWrite(error)) freeze(resource, error);
      else setSnack(copy.saveFailed);
    } finally {
      inFlight.current = false;
      if (isCurrentSession()) setBusyResource(null);
    }
  };

  const reread = async () => {
    const resource = activeResource;
    if (inFlight.current || !isCurrentSession()) return;
    inFlight.current = true;
    setBusyResource(resource);
    try {
      if (resource === "roles") {
        const verified = await fetchUserAccessRoles(userGuid, actorKey);
        if (!isCurrentSession()) return;
        const next = verified.filter((role) => !isStoreManagerRoleName(role.roleName)).map((role) => role.roleGUID);
        setRoleDraft(next); setRoleBaseline([...next]);
        queryClient.setQueryData(["identity-admin", actorKey, "access-roles", userGuid], verified);
        await refreshPermissionInheritanceSafely();
        if (!isCurrentSession()) return;
      } else if (resource === "stores") {
        const verified = await fetchUserAccessStores(userGuid, actorKey);
        if (!isCurrentSession()) return;
        const next = buildUserAccessStoreAssignments(verified);
        setStoreDraft(next); setStoreBaseline(next.map((item) => ({ ...item })));
        queryClient.setQueryData(["identity-admin", actorKey, "access-stores", userGuid], verified);
        await refreshPermissionInheritanceSafely();
        if (!isCurrentSession()) return;
      } else {
        const verified = await fetchUserAccessPermissionAccess(userGuid, actorKey);
        if (!isCurrentSession()) return;
        setPermissionDraft(buildDirectPermissionDraft(verified.state));
        queryClient.setQueryData(["identity-admin", actorKey, "access-permissions", userGuid], verified);
      }
      await invalidateRelatedQueries();
      if (!isCurrentSession()) return;
      setFrozenResources((current) => current.filter((item) => item !== resource));
      setSnack(copy.rereadDone);
    } catch (error) {
      if (!isCurrentSession()) return;
      freeze(resource, error);
    } finally {
      inFlight.current = false;
      if (isCurrentSession()) setBusyResource(null);
    }
  };

  usePreventRemove(isAuthenticated && !departing && (anyDirty || busyResource !== null), ({ data }) => {
    if (!isCurrentSession()) {
      // 登出、账号切换和审核模式变化必须直接离开，不能被旧身份的草稿确认拦住。
      pendingAction.current = data.action;
      setDeparting(true);
      return;
    }
    if (busyResource || inFlight.current) {
      Alert.alert(copy.busyTitle, copy.busyMessage, [{ text: copy.cancel }]);
      return;
    }
    Alert.alert(copy.discardTitle, copy.discardDescription, [
      { text: copy.cancel, style: "cancel" },
      { text: copy.discard, style: "destructive", onPress: () => { pendingAction.current = data.action; setDeparting(true); } },
    ]);
  });
  useEffect(() => {
    if (!departing || !pendingAction.current) return;
    const action = pendingAction.current;
    pendingAction.current = null;
    navigation.dispatch(action);
  }, [departing, navigation]);

  if (!userGuid) return <AdminScreen title={copy.title}><AdminEmpty text={copy.noItems} /></AdminScreen>;
  if (!allowed || (targetQuery.data && tabs.length === 0)) return <AdminScreen title={copy.title}><AdminEmpty text={copy.noAccess} /></AdminScreen>;
  if (targetQuery.isPending) return <AdminScreen title={copy.title}><AdminEmpty text={copy.loading} /></AdminScreen>;
  if (targetQuery.error) return <AdminScreen title={copy.title}><AdminError error={targetQuery.error} onRetry={() => void targetQuery.refetch()} /></AdminScreen>;

  const renderRoles = () => {
    const queryError = assignedRolesQuery.error ?? roleCatalogQuery.error;
    const filtered = roles.filter((role) => `${role.roleName} ${role.description ?? ""}`.toLowerCase().includes(roleSearch.trim().toLowerCase()));
    return <View style={{ gap: 12 }}><SearchField value={roleSearch} onChange={setRoleSearch} placeholder={copy.roleSearch} />
      {queryError ? <AdminError error={queryError} onRetry={() => { void assignedRolesQuery.refetch(); void roleCatalogQuery.refetch(); }} />
        : !activeReady ? <AdminEmpty text={copy.loading} /> : filtered.length === 0 ? <AdminEmpty text={copy.noItems} />
          : <View style={styles.section}>{filtered.map((role) => {
            const selected = getAccessRoleSelectionState({
              role,
              selectedRoleGuids: roleDraft ?? [],
              hasManagedStoreAssignment: storeDraft?.some((item) => item.isPrimary)
                ?? assignedRolesQuery.data?.some((assignedRole) => isStoreManagerRoleName(assignedRole.roleName))
                ?? false,
            });
            const locked = selected.locked || (scoped && !isStoreStaffRoleName(role.roleName));
            return <View key={role.roleGUID}><TouchableRipple disabled={!canEditRole(locked)} onPress={() => setRoleDraft((current) => current ? selected.selected ? current.filter((guid) => guid !== role.roleGUID) : [...current, role.roleGUID] : current)}>
              <AdminRow title={role.roleName} subtitle={selected.derived ? copy.derived : role.description} trailing={<Checkbox status={selected.selected ? "checked" : "unchecked"} disabled={!canEditRole(locked)} />} />
            </TouchableRipple><Divider /></View>;
          })}</View>}
    </View>;
  };

  const renderStores = () => {
    const queryError = assignedStoresQuery.error ?? storeCatalogQuery.error;
    const filtered = stores.filter((store) => `${store.storeName} ${store.storeCode}`.toLowerCase().includes(storeSearch.trim().toLowerCase()));
    return <View style={{ gap: 12 }}><SearchField value={storeSearch} onChange={setStoreSearch} placeholder={copy.storeSearch} />
      {queryError ? <AdminError error={queryError} onRetry={() => { void assignedStoresQuery.refetch(); if (!scoped) void storeCatalogQuery.refetch(); }} />
        : !activeReady ? <AdminEmpty text={copy.loading} /> : filtered.length === 0 ? <AdminEmpty text={copy.noItems} />
          : <View style={styles.section}>{filtered.map((store) => {
            const guid = store.storeGUID ?? "";
            const state = getUserAccessStoreState(storeDraft ?? [], guid);
            const inScope = !scoped || managedStoreGuids.has(guid);
            const editable = activeReady && !activeFrozen && !busyResource && inScope;
            return <View key={guid}><AdminRow title={store.storeName || store.storeCode} subtitle={`${store.storeCode}${!inScope ? ` · ${copy.readOnly}` : ""}`} />
              <View style={{ flexDirection: "row", padding: 12, gap: 8 }}>{(["unassigned", "view", "manage"] as const).map((next) => {
                const cannotGrantManage = scoped && next === "manage" && state !== "manage";
                return <Button key={next} compact mode={state === next ? "contained" : "outlined"} style={{ flex: 1 }} disabled={!editable || cannotGrantManage} onPress={() => setStoreDraft((current) => current ? setUserAccessStoreState(current, guid, next) : current)}>{copy[next]}</Button>;
              })}</View><Divider /></View>;
          })}</View>}
    </View>;
  };

  const renderPermissions = () => {
    const keyword = permissionSearch.trim().toLowerCase();
    const categories = (permissionQuery.data?.categories ?? []).map((category) => ({
      ...category,
      displayName: localizeAccessPermissionCategory(category.displayName || category.category, language),
      permissions: category.permissions.map((permission) => ({
        ...permission,
        presentation: localizeAccessPermission(permission, language),
      })).filter((permission) => {
        const pos = POS_PERMISSION_PATTERN.test(permission.name);
        return (permissionPlatform === "pos" ? pos : !pos)
          && `${permission.presentation.name} ${permission.name} ${permission.presentation.description}`.toLowerCase().includes(keyword);
      }),
    })).filter((category) => category.permissions.length > 0);
    const implicitAll = Boolean(permissionQuery.data?.state.implicitAllPermissions);
    const isSuperAdmin = Boolean(permissionQuery.data?.state.isSuperAdmin);
    const readOnly = !activeReady || activeFrozen || Boolean(busyResource) || implicitAll || isSuperAdmin;
    return <View style={{ gap: 12 }}><AdminTabs value={permissionPlatform} items={[{ key: "web", label: copy.web }, { key: "pos", label: copy.pos }]} onChange={(key) => setPermissionPlatform(key as "web" | "pos")} />
      <SearchField value={permissionSearch} onChange={setPermissionSearch} placeholder={copy.permissionSearch} />
      {permissionQuery.error ? <AdminError error={permissionQuery.error} onRetry={() => void permissionQuery.refetch()} />
        : !activeReady ? <AdminEmpty text={copy.loading} /> : categories.length === 0 ? <AdminEmpty text={copy.noItems} />
          : categories.map((category) => <View key={category.category} style={styles.section}><Text style={[styles.label, { padding: 16 }]}>{category.displayName}</Text>
            {category.permissions.map((permission) => {
              const state = getAccessPermissionSelectionState(permissionDraft!, permission.name);
              return <View key={permission.name}><AdminRow title={permission.presentation.name} subtitle={state.inherited ? copy.inherited : state.direct ? copy.direct : permission.presentation.description} trailing={<Switch value={implicitAll || isSuperAdmin || state.checked} disabled={readOnly || state.locked} onValueChange={(checked) => setPermissionDraft((current) => current ? toggleDirectPermission(current, permission.name, checked) : current)} />} /><Divider /></View>;
            })}</View>)}
    </View>;
  };

  const renderMobile = () => {
    const readOnly = !activeReady || activeFrozen || Boolean(busyResource) || Boolean(permissionQuery.data?.state.implicitAllPermissions);
    return <View style={{ gap: 12 }}><Text style={styles.muted}>{copy.mobileHint}</Text>
      {permissionQuery.error ? <AdminError error={permissionQuery.error} onRetry={() => void permissionQuery.refetch()} />
        : !activeReady ? <AdminEmpty text={copy.loading} />
          : (["bottom", "store", "operations", "reports"] as MobileMenuSection[]).map((group) => <View key={group} style={styles.section}><Text style={[styles.label, { padding: 16 }]}>{copy.mobileSections[group]}</Text>
            {mobileSections[group].map((item) => {
              const definition = mobileDefinitions.find((candidate) => candidate.key === item.key)!;
              const state = userMenuPermissionState(definition, permissionDraft!, assignablePermissionCodes, permissionQuery.data!.state);
              return <View key={item.key}><AdminRow title={item.title} subtitle={definition.requireAdmin ? copy.adminOnly : definition.fixed ? copy.fixed : state.inherited ? copy.inherited : state.direct ? copy.direct : state.visible ? copy.visible : copy.hidden} trailing={<Switch value={state.visible} disabled={readOnly || state.locked} onValueChange={(checked) => setPermissionDraft((current) => current ? togglePermissionCodes(current, item.permissionCodes, checked, assignablePermissionCodes) : current)} />} /><Divider /></View>;
            })}</View>)}
    </View>;
  };

  return <AdminScreen title={target?.fullName || target?.username || copy.identityUnknown} footer={activeDirty || activeFrozen ? <View style={{ gap: 8 }}>
    {activeFrozen ? <><Text style={styles.muted}>{copy.rereadHint}</Text><Button mode="outlined" loading={busyResource === activeResource} disabled={Boolean(busyResource)} onPress={() => void reread()}>{copy.reread}</Button></> : null}
    {activeDirty ? <Button mode="contained" style={styles.button} loading={busyResource === activeResource} disabled={!canSave} onPress={() => void save()}>{copy.save}</Button> : null}
  </View> : undefined}>
    {tabs.length ? <AdminTabs value={section} onChange={(key) => setSection(key as AuthorizationSection)} items={tabs} /> : null}
    <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
      {section === "roles" ? renderRoles() : section === "stores" ? renderStores() : section === "permissions" ? renderPermissions() : renderMobile()}
    </ScrollView>
    <Snackbar visible={Boolean(snack)} onDismiss={() => setSnack("")} duration={4000}>{snack}</Snackbar>
  </AdminScreen>;
}
