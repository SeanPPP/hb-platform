import { useMemo, useState } from "react";
import { FlatList, RefreshControl, ScrollView, StyleSheet, View } from "react-native";
import { useRouter } from "expo-router";
import { ActivityIndicator, Chip, Icon, IconButton, Text, TouchableRipple } from "react-native-paper";
import { HB_COLORS as C } from "@/shared/theme/tokens";
import { filterPermissionListItems, groupPermissionsByCategory, type PermissionCategoryGroup, type PermissionListItem } from "./permission-logic";
import { interpolatePermissionCopy as interpolate, usePermissionCopy, usePermissionItems, usePermissionRoleCounts, usePermissionSession } from "./permission-hooks";
import { AdminEmpty, AdminError, AdminScreen, AdminTabs, SearchField, styles } from "./ui";
import { useIdentitySearch } from "./user-hooks";

export default function PermissionsScreen() {
  const router = useRouter();
  const { copy, appLanguage } = usePermissionCopy();
  const { access, allowed, actorKey, capabilities } = usePermissionSession();
  const [input, setInput] = useState("");
  const search = useIdentitySearch(input);
  const [categoryKey, setCategoryKey] = useState("");
  const [expanded, setExpanded] = useState<Set<string>>(new Set());

  const permissions = usePermissionItems({ actorKey, enabled: allowed, appLanguage });
  const roleCountsQuery = usePermissionRoleCounts({ actorKey, enabled: allowed });

  const allGroups = useMemo(() => groupPermissionsByCategory(permissions.items), [permissions.items]);
  const visibleGroups = useMemo(
    () => groupPermissionsByCategory(filterPermissionListItems(permissions.items, { keyword: search, categoryKey })),
    [categoryKey, permissions.items, search],
  );
  // 搜索或按分类筛选时自动展开全部，用户无需再逐个点开分组。
  const forceExpanded = Boolean(search.trim()) || Boolean(categoryKey);
  const toggleGroup = (key: string) => setExpanded((current) => {
    const next = new Set(current);
    if (next.has(key)) next.delete(key);
    else next.add(key);
    return next;
  });

  const canViewUsers = allowed && access.hasPermission("Users.View");
  const canViewRoles = allowed && access.canReadRole;
  const tabs = [
    ...(canViewUsers ? [{ key: "users", label: copy.usersTab }] : []),
    ...(canViewRoles ? [{ key: "roles", label: copy.rolesTab }] : []),
    { key: "permissions", label: copy.permissionsTab },
  ];

  return (
    <AdminScreen
      title={copy.title}
      action={capabilities.canManage ? (
        <IconButton icon="plus" accessibilityLabel={copy.add} onPress={() => router.push("/(shell)/permissions/new")} />
      ) : undefined}
    >
      <AdminTabs
        value="permissions"
        items={tabs}
        onChange={(key) => {
          if (key === "users") router.replace("/(shell)/user-admin");
          else if (key === "roles") router.replace("/(shell)/roles");
        }}
      />
      {!allowed ? (
        <View style={localStyles.denied}>
          <Text variant="titleMedium">{copy.accessDeniedTitle}</Text>
          <Text style={styles.muted}>{copy.accessDenied}</Text>
        </View>
      ) : (
        <>
          <View style={localStyles.search}><SearchField value={input} onChange={setInput} placeholder={copy.search} /></View>
          {allGroups.length > 0 ? (
            <ScrollView horizontal showsHorizontalScrollIndicator={false} style={localStyles.chipScroll} contentContainerStyle={localStyles.chips} keyboardShouldPersistTaps="handled">
              <Chip compact selected={!categoryKey} showSelectedCheck={false} mode="outlined" style={[localStyles.chip, !categoryKey && localStyles.chipSelected]} textStyle={!categoryKey ? localStyles.chipTextSelected : undefined} onPress={() => setCategoryKey("")}>
                {`${copy.allCategories} ${permissions.items.length}`}
              </Chip>
              {allGroups.map((group) => {
                const selected = categoryKey === group.key;
                return (
                  <Chip key={group.key} compact selected={selected} showSelectedCheck={false} mode="outlined" style={[localStyles.chip, selected && localStyles.chipSelected]} textStyle={selected ? localStyles.chipTextSelected : undefined} onPress={() => setCategoryKey(selected ? "" : group.key)}>
                    {`${group.displayName} ${group.items.length}`}
                  </Chip>
                );
              })}
            </ScrollView>
          ) : null}
          {permissions.isPending ? <ActivityIndicator style={localStyles.loader} /> : permissions.error ? (
            <AdminError error={permissions.error} onRetry={() => void permissions.refetch()} />
          ) : (
            <FlatList
              data={visibleGroups}
              keyExtractor={(group) => group.key}
              refreshControl={<RefreshControl refreshing={permissions.isRefetching} onRefresh={() => { void permissions.refetch(); void roleCountsQuery.refetch(); }} />}
              contentContainerStyle={visibleGroups.length === 0 ? localStyles.emptyList : undefined}
              ListEmptyComponent={<AdminEmpty text={copy.empty} />}
              ListHeaderComponent={
                <View style={localStyles.toolbar}>
                  <Text style={styles.muted}>
                    {interpolate(copy.summary, { count: permissions.items.length, categories: allGroups.length })}
                    {roleCountsQuery.error ? ` · ${copy.roleCountUnavailable}` : ""}
                  </Text>
                  <IconButton icon="refresh" size={19} accessibilityLabel={copy.refresh} onPress={() => { void permissions.refetch(); void roleCountsQuery.refetch(); }} />
                </View>
              }
              renderItem={({ item: group }) => (
                <CategorySection
                  group={group}
                  copy={copy}
                  expanded={forceExpanded || expanded.has(group.key)}
                  onToggle={() => toggleGroup(group.key)}
                  roleCounts={roleCountsQuery.data}
                  onPressItem={(item) => router.push({ pathname: "/(shell)/permissions/[code]", params: { code: item.code } })}
                />
              )}
            />
          )}
        </>
      )}
    </AdminScreen>
  );
}

function CategorySection({ group, copy, expanded, onToggle, roleCounts, onPressItem }: {
  group: PermissionCategoryGroup;
  copy: ReturnType<typeof usePermissionCopy>["copy"];
  expanded: boolean;
  onToggle: () => void;
  roleCounts?: Record<string, number>;
  onPressItem: (item: PermissionListItem) => void;
}) {
  return (
    <View style={styles.section}>
      <TouchableRipple onPress={onToggle} accessibilityRole="button" accessibilityState={{ expanded }}>
        <View style={localStyles.categoryHeader}>
          <Icon source={expanded ? "chevron-down" : "chevron-right"} size={22} color={C.textSecondary} />
          <View style={{ flex: 1 }}>
            <Text style={styles.value}>{group.displayName} <Text style={styles.muted}>{group.key !== group.displayName ? group.key : ""}</Text></Text>
            <Text style={styles.muted}>{interpolate(copy.categoryCount, { count: group.items.length })} · {group.isCustom ? copy.customCategory : copy.systemCategory}</Text>
          </View>
        </View>
      </TouchableRipple>
      {expanded ? group.items.map((item) => (
        <TouchableRipple key={item.code} onPress={() => onPressItem(item)} accessibilityRole="button">
          <View style={localStyles.permissionRow}>
            <View style={{ flex: 1, gap: 2 }}>
              <Text style={localStyles.permissionName}>{item.name}</Text>
              <Text style={localStyles.permissionCode}>{item.code}</Text>
            </View>
            {item.isSystem ? (
              <View style={localStyles.systemTag}>
                <Icon source="lock-outline" size={12} color={C.textSecondary} />
                <Text style={localStyles.systemTagText}>{copy.systemTag}</Text>
              </View>
            ) : null}
            {roleCounts ? <Text style={styles.muted}>{interpolate(copy.roleCount, { count: roleCounts[item.code] ?? 0 })}</Text> : null}
            <Icon source="chevron-right" size={20} color={C.textSecondary} />
          </View>
        </TouchableRipple>
      )) : null}
    </View>
  );
}

const localStyles = StyleSheet.create({
  search: { paddingHorizontal: 16, paddingBottom: 8 },
  // 横向 ScrollView 默认会随 flex 布局被下方列表压扁，必须固定不伸缩才能完整显示 chip。
  chipScroll: { flexGrow: 0, flexShrink: 0 },
  chips: { paddingHorizontal: 16, paddingBottom: 10, gap: 6, alignItems: "center" },
  chip: { backgroundColor: C.white, borderColor: C.outline },
  chipSelected: { backgroundColor: "#EFF6FF", borderColor: C.brand },
  chipTextSelected: { color: C.action, fontWeight: "600" },
  loader: { flex: 1 },
  denied: { margin: 16, padding: 16, gap: 8, borderRadius: 8, backgroundColor: C.surfaceMuted },
  emptyList: { flexGrow: 1 },
  toolbar: { minHeight: 40, paddingHorizontal: 16, flexDirection: "row", alignItems: "center", justifyContent: "space-between", borderBottomWidth: StyleSheet.hairlineWidth, borderBottomColor: C.outlineMuted },
  categoryHeader: { minHeight: 52, flexDirection: "row", alignItems: "center", paddingHorizontal: 12, gap: 8 },
  permissionRow: { minHeight: 56, flexDirection: "row", alignItems: "center", paddingLeft: 42, paddingRight: 12, paddingVertical: 8, gap: 10, borderTopWidth: StyleSheet.hairlineWidth, borderTopColor: C.outlineMuted },
  permissionName: { fontSize: 15, lineHeight: 21, color: C.textPrimary },
  permissionCode: { fontSize: 12, lineHeight: 16, color: C.textSecondary, fontFamily: "monospace" },
  systemTag: { flexDirection: "row", alignItems: "center", gap: 3, paddingHorizontal: 6, paddingVertical: 3, borderRadius: 5, backgroundColor: C.surfaceMuted },
  systemTagText: { fontSize: 11, fontWeight: "600", color: C.textSecondary },
});
