import { useCallback, useEffect, useMemo, useState } from "react";
import { FlatList, RefreshControl, View } from "react-native";
import { useFocusEffect, useRouter } from "expo-router";
import { useQuery } from "@tanstack/react-query";
import { ActivityIndicator, Avatar, Button, IconButton, Text } from "react-native-paper";
import { HB_COLORS as C } from "@/shared/theme/tokens";
import { fetchAccessRoleCatalog, fetchAccessStoreCatalog, fetchIdentityUsers } from "./api";
import { AdminEmpty, AdminError, AdminRow, AdminScreen, AdminTabs, SearchField, StatusTag, styles } from "./ui";
import { IdentitySelectionSheet } from "./selection-sheet";
import { managedIdentityStores, selectableIdentityRoles } from "./user-logic";
import { useIdentitySearch, useIdentitySession, useIdentityUserCopy } from "./user-hooks";

export default function IdentityUsersScreen() {
  const c = useIdentityUserCopy();
  const router = useRouter();
  const { user, access, allowed, actorKey } = useIdentitySession();
  const [input, setInput] = useState("");
  const search = useIdentitySearch(input);
  const [storeGuid, setStoreGuid] = useState("");
  const [roleGuid, setRoleGuid] = useState("");
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [sortBy, setSortBy] = useState("lastLoginAt");
  const [sortDirection, setSortDirection] = useState<"asc" | "desc">("desc");
  const [picker, setPicker] = useState<"store" | "role" | "sort" | "pageSize" | null>(null);
  const scoped = access.isStoreLevelManager;
  const storesQuery = useQuery({ queryKey: ["identity-admin", actorKey, "store-options"], queryFn: ({ signal }) => fetchAccessStoreCatalog(actorKey, signal), enabled: allowed && !scoped, retry: false });
  const rolesQuery = useQuery({ queryKey: ["identity-admin", actorKey, "role-options"], queryFn: ({ signal }) => fetchAccessRoleCatalog(actorKey, signal),
    enabled: allowed && (access.canReadRole || access.hasPermission("Users.ManageRoles")), retry: false });
  const stores = useMemo(() => scoped ? managedIdentityStores(user) : storesQuery.data ?? [], [scoped, user, storesQuery.data]);
  const roles = useMemo(() => selectableIdentityRoles(rolesQuery.data ?? [], scoped), [rolesQuery.data, scoped]);
  const query = { page, pageSize, search: search || undefined, storeGuid: storeGuid || undefined, roleGuid: roleGuid || undefined, sortBy, sortDirection };
  const usersQuery = useQuery({ queryKey: ["identity-admin", actorKey, "users", query], queryFn: ({ signal }) => fetchIdentityUsers(query, actorKey, signal), enabled: allowed, retry: false });
  const { refetch } = usersQuery;
  useFocusEffect(useCallback(() => { if (allowed) void refetch(); }, [allowed, refetch]));
  useEffect(() => { setPage(1); }, [search, storeGuid, roleGuid, pageSize, sortBy, sortDirection]);

  if (!allowed) return <AdminScreen title={c.users}><AdminEmpty text={c.noAccess} /></AdminScreen>;
  const sortOptions = [{ value: "lastLoginAt", label: c.sortLogin }, { value: "username", label: c.sortUsername }];
  const options = picker === "store" ? [{ value: "", label: c.allStores }, ...stores.map(store => ({ value: store.storeGUID, label: `${store.storeName} · ${store.storeCode}` }))]
    : picker === "role" ? [{ value: "", label: c.allRoles }, ...roles.map(role => ({ value: role.roleGUID, label: role.roleName }))]
    : picker === "pageSize" ? [10, 20, 50].map(size => ({ value: String(size), label: String(size) })) : sortOptions;
  const pickerTitle = picker === "store" ? c.chooseStore : picker === "role" ? c.chooseRole : picker === "pageSize" ? c.pageSize : c.sort;
  const selected = picker === "store" ? storeGuid : picker === "role" ? roleGuid : picker === "pageSize" ? String(pageSize) : sortBy;
  const items = usersQuery.data?.items ?? [];
  const total = usersQuery.data?.total ?? 0;

  return <AdminScreen title={c.users} action={access.isAdmin && access.hasPermission("Users.Create") ? <Button icon="plus" onPress={() => router.push("/(shell)/user-admin/new")}>{c.add}</Button> : undefined}>
    <AdminTabs value="users" onChange={key => key === "roles" && router.replace("/(shell)/roles")} items={[{ key: "users", label: c.usersTab }, ...(access.canReadRole ? [{ key: "roles", label: c.rolesTab }] : [])]} />
    <View style={{ paddingHorizontal: 16, gap: 10 }}>
      <SearchField value={input} onChange={setInput} placeholder={c.search} />
      <View style={{ flexDirection: "row", gap: 8 }}>
        <Button mode="outlined" icon="chevron-down" style={[styles.button, { flex: 1 }]} onPress={() => setPicker("store")} compact>
          {stores.find(store => store.storeGUID === storeGuid)?.storeName ?? c.allStores}
        </Button>
        {rolesQuery.isEnabled ? <Button mode="outlined" icon="chevron-down" style={[styles.button, { flex: 1 }]} onPress={() => setPicker("role")} compact>
          {roles.find(role => role.roleGUID === roleGuid)?.roleName ?? c.allRoles}
        </Button> : null}
      </View>
      <View style={{ flexDirection: "row", alignItems: "center" }}>
        <Text style={[styles.label, { flex: 1 }]}>{total} {c.count}</Text>
        <Button compact onPress={() => setPicker("sort")}>{sortOptions.find(option => option.value === sortBy)?.label}</Button>
        <IconButton icon={sortDirection === "asc" ? "sort-ascending" : "sort-descending"} accessibilityLabel={sortDirection === "asc" ? c.asc : c.desc} onPress={() => setSortDirection(value => value === "asc" ? "desc" : "asc")} />
        <IconButton icon="refresh" accessibilityLabel={c.refresh} onPress={() => void usersQuery.refetch()} />
      </View>
    </View>
    {usersQuery.isError ? <AdminError error={usersQuery.error} onRetry={() => void usersQuery.refetch()} /> : null}
    {usersQuery.isPending ? <ActivityIndicator style={{ margin: 32 }} /> : <FlatList data={items} keyExtractor={item => item.userGUID}
      refreshControl={<RefreshControl refreshing={usersQuery.isFetching && !usersQuery.isPending} onRefresh={() => void usersQuery.refetch()} />}
      contentContainerStyle={{ paddingBottom: 16 }} ListEmptyComponent={!usersQuery.isError ? <AdminEmpty text={c.noUsers} /> : null}
      renderItem={({ item }) => <View style={{ flexDirection: "row", alignItems: "center", paddingLeft: 16 }}>
        <Avatar.Text size={36} label={(item.fullName || item.username).slice(0, 1).toUpperCase()} style={{ backgroundColor: "#EFF6FF" }} color={C.action} />
        <View style={{ flex: 1 }}><AdminRow title={`${item.fullName || item.username}  ${item.fullName ? item.username : ""}`}
          subtitle={`${item.roleNames.join("、") || "—"} · ${item.storeNames.slice(0, 2).join("、") || "—"}${item.storeNames.length > 2 ? ` +${item.storeNames.length - 2}` : ""}`}
          trailing={<StatusTag active={item.isActive} />} onPress={() => router.push({ pathname: "/(shell)/user-admin/[userGuid]", params: { userGuid: item.userGUID } })} /></View>
      </View>} />}
    <View style={[styles.footer, { flexDirection: "row", alignItems: "center" }]}>
      <Text style={[styles.muted, { flex: 1 }]}>{c.shown} {items.length ? (page - 1) * pageSize + 1 : 0}–{items.length ? (page - 1) * pageSize + items.length : 0} / {total}</Text>
      <Button compact onPress={() => setPicker("pageSize")}>{pageSize}</Button>
      <IconButton icon="chevron-left" accessibilityLabel={c.previous} disabled={page <= 1 || usersQuery.isFetching} onPress={() => setPage(value => value - 1)} />
      <IconButton icon="chevron-right" accessibilityLabel={c.next} disabled={page * pageSize >= total || usersQuery.isFetching} onPress={() => setPage(value => value + 1)} />
    </View>
    {picker ? <IdentitySelectionSheet title={pickerTitle} value={selected} options={options} onDismiss={() => setPicker(null)} onSelect={value => {
      if (picker === "store") setStoreGuid(value); else if (picker === "role") setRoleGuid(value); else if (picker === "pageSize") setPageSize(Number(value)); else setSortBy(value); setPicker(null);
    }} /> : null}
    {picker === "store" && storesQuery.isError ? <AdminError error={storesQuery.error} onRetry={() => void storesQuery.refetch()} /> : null}
    {picker === "role" && rolesQuery.isError ? <AdminError error={rolesQuery.error} onRetry={() => void rolesQuery.refetch()} /> : null}
  </AdminScreen>;
}
