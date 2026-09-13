import { useEffect, useMemo, useState } from "react";
import { FlatList, StyleSheet, View } from "react-native";
import { useQuery } from "@tanstack/react-query";
import { useRouter } from "expo-router";
import { ActivityIndicator, Button, IconButton, Text } from "react-native-paper";
import en from "@/locales/en/identityRoles.json";
import zh from "@/locales/zh/identityRoles.json";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { HB_COLORS as C } from "@/shared/theme/tokens";
import { fetchIdentityRoles } from "./api";
import { getRoleCapabilities } from "./role-logic";
import { useIdentitySession } from "./user-hooks";
import { AdminEmpty, AdminError, AdminRow, AdminScreen, AdminTabs, SearchField, StatusTag, styles } from "./ui";

function interpolate(value: string, params: Record<string, string | number> = {}) {
  return Object.entries(params).reduce((text, [key, replacement]) => text.replace(`{{${key}}}`, String(replacement)), value);
}

export default function RolesScreen() {
  const router = useRouter();
  const { language } = useAppTranslation();
  const copy = language.startsWith("zh") ? zh : en;
  const { access, allowed, actorKey } = useIdentitySession("Roles.View");
  const [search, setSearch] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const capabilities = useMemo(() => getRoleCapabilities({
    isDeviceMode: !allowed,
    isAdmin: access.isAdmin,
    hasPermission: access.hasPermission,
  }), [access.hasPermission, access.isAdmin, allowed]);

  useEffect(() => {
    const timer = setTimeout(() => {
      setDebouncedSearch(search.trim());
      setPage(1);
    }, 250);
    return () => clearTimeout(timer);
  }, [search]);

  const rolesQuery = useQuery({
    queryKey: ["identity-admin", actorKey, "roles", page, pageSize, debouncedSearch],
    enabled: allowed,
    retry: false,
    queryFn: ({ signal }) => fetchIdentityRoles({ page, pageSize, searchKeyword: debouncedSearch || undefined }, actorKey, signal),
  });

  const canViewUsers = allowed && access.hasPermission("Users.View");
  return (
    <AdminScreen
      title={copy.title}
      action={capabilities.canCreate ? (
        <IconButton icon="plus" accessibilityLabel={copy.add} onPress={() => router.push({ pathname: "/roles/[roleGuid]", params: { roleGuid: "new" } })} />
      ) : undefined}
    >
      <AdminTabs
        value="roles"
        items={canViewUsers ? [{ key: "users", label: copy.usersTab }, { key: "roles", label: copy.rolesTab }] : [{ key: "roles", label: copy.rolesTab }]}
        onChange={(key) => { if (key === "users") router.replace("/user-admin"); }}
      />
      {!allowed ? (
        <View style={localStyles.denied}>
          <Text variant="titleMedium">{copy.accessDeniedTitle}</Text>
          <Text style={styles.muted}>{copy.accessDenied}</Text>
        </View>
      ) : (
        <>
          <View style={localStyles.search}><SearchField value={search} onChange={setSearch} placeholder={copy.search} /></View>
          {rolesQuery.isPending ? <ActivityIndicator style={localStyles.loader} /> : rolesQuery.error ? (
            <AdminError error={rolesQuery.error} onRetry={() => void rolesQuery.refetch()} />
          ) : (
            <FlatList
              data={rolesQuery.data?.items ?? []}
              refreshing={rolesQuery.isRefetching}
              onRefresh={() => void rolesQuery.refetch()}
              keyExtractor={(item) => item.roleGUID}
              contentContainerStyle={(rolesQuery.data?.items.length ?? 0) === 0 ? localStyles.emptyList : undefined}
              ListEmptyComponent={<AdminEmpty text={copy.empty} />}
              renderItem={({ item }) => (
                <AdminRow
                  icon="shield-account-outline"
                  title={item.roleName}
                  subtitle={`${item.description || "—"} · ${interpolate(copy.membersCount, { count: item.userCount })}`}
                  trailing={<StatusTag active={item.isActive} />}
                  onPress={() => router.push({ pathname: "/roles/[roleGuid]", params: { roleGuid: item.roleGUID } })}
                />
              )}
              ListHeaderComponent={<View style={localStyles.toolbar}>
                <Text style={styles.muted}>{interpolate(copy.totalCount, { count: rolesQuery.data?.total ?? 0 })}</Text>
                <IconButton icon="refresh" size={19} accessibilityLabel={copy.refresh} onPress={() => void rolesQuery.refetch()} />
                <Text style={styles.muted}>{copy.pageSize}</Text>
                {[10, 20, 50].map((size) => <Button key={size} compact mode={pageSize === size ? "contained-tonal" : "text"} onPress={() => { setPageSize(size); setPage(1); }}>{size}</Button>)}
              </View>}
              ListFooterComponent={rolesQuery.data && rolesQuery.data.totalPages > 1 ? (
                <View style={localStyles.pagination}>
                  <Button mode="outlined" disabled={page <= 1} onPress={() => setPage((value) => Math.max(1, value - 1))}>‹</Button>
                  <Text style={styles.muted}>{page} / {rolesQuery.data.totalPages}</Text>
                  <Button mode="outlined" disabled={page >= rolesQuery.data.totalPages} onPress={() => setPage((value) => value + 1)}>›</Button>
                </View>
              ) : null}
            />
          )}
        </>
      )}
    </AdminScreen>
  );
}

const localStyles = StyleSheet.create({
  search: { paddingHorizontal: 16, paddingBottom: 12 },
  loader: { flex: 1 },
  denied: { margin: 16, padding: 16, gap: 8, borderRadius: 8, backgroundColor: C.surfaceMuted },
  emptyList: { flexGrow: 1 },
  pagination: { flexDirection: "row", alignItems: "center", justifyContent: "center", gap: 16, padding: 16 },
  toolbar: { minHeight: 44, paddingHorizontal: 16, paddingVertical: 4, flexDirection: "row", flexWrap: "wrap", alignItems: "center", justifyContent: "flex-end", gap: 2, borderBottomWidth: StyleSheet.hairlineWidth, borderBottomColor: C.outlineMuted },
});
