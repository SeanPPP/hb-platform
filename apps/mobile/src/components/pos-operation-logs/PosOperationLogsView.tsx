import { useCallback, useMemo } from "react";
import {
  Pressable,
  RefreshControl,
  ScrollView,
  SectionList,
  StyleSheet,
  View,
} from "react-native";
import {
  ActivityIndicator,
  Button,
  Icon,
  IconButton,
  Searchbar,
  Text,
} from "react-native-paper";
import { SafeAreaView, useSafeAreaInsets } from "react-native-safe-area-context";
import { EmptyState } from "@/components/ui/EmptyState";
import {
  countActivePosOperationFilters,
  formatDisplayDate,
  groupPosOperationLogsByDay,
  resolvePosOperationRange,
  resolveQuickFilter,
  shortenIdentifier,
} from "@/modules/pos-operation-logs/logic";
import type {
  PosOperationLogDaySection,
  PosOperationLogFilters,
  PosOperationLogItem,
  PosOperationLogSummary,
  PosOperationQuickFilter,
} from "@/modules/pos-operation-logs/types";
import type { Store } from "@/modules/shop/types";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { HB_COLORS, HB_RADIUS, HB_SPACING } from "@/shared/theme/tokens";
import { LOG_UI } from "./log-ui";
import { PosOperationLogRow } from "./PosOperationLogRow";

export interface PosOperationLogsViewProps {
  filters: PosOperationLogFilters;
  stores: Store[];
  scopeLabel: string;
  items: PosOperationLogItem[];
  total: number;
  summary: PosOperationLogSummary | null;
  summaryError: boolean;
  loading: boolean;
  refreshing: boolean;
  loadingMore: boolean;
  hasMore: boolean;
  error: string | null;
  searchDraft: string;
  onSearchDraftChange: (value: string) => void;
  onSearchSubmit: () => void;
  onQuickFilter: (quick: PosOperationQuickFilter) => void;
  onOpenFilters: () => void;
  /** 四个筛选 chip 各自直接下拉选择，完整筛选面板只由右上角按钮打开。 */
  onPickRange: () => void;
  onPickStore: () => void;
  onPickType: () => void;
  onPickPlatform: () => void;
  onClearOrderTrace: () => void;
  onRefresh: () => void;
  onLoadMore: () => void;
  onRetry: () => void;
  onOpenItem: (item: PosOperationLogItem) => void;
  onBack: () => void;
}

const QUICK_FILTERS: PosOperationQuickFilter[] = [
  "all",
  "Denied",
  "Failed",
  "emergencyOverride",
];

export function PosOperationLogsView({
  filters,
  stores,
  scopeLabel,
  items,
  total,
  summary,
  summaryError,
  loading,
  refreshing,
  loadingMore,
  hasMore,
  error,
  searchDraft,
  onSearchDraftChange,
  onSearchSubmit,
  onQuickFilter,
  onOpenFilters,
  onPickRange,
  onPickStore,
  onPickType,
  onPickPlatform,
  onClearOrderTrace,
  onRefresh,
  onLoadMore,
  onRetry,
  onOpenItem,
  onBack,
}: PosOperationLogsViewProps) {
  const { t } = useAppTranslation("posOperationLogs");
  const insets = useSafeAreaInsets();
  // SectionList 要求 data 字段，逻辑层的分组结果按原样附上 data 引用。
  const sections = useMemo(
    () => groupPosOperationLogsByDay(items).map((section) => ({ ...section, data: section.items })),
    [items],
  );
  const quick = resolveQuickFilter(filters);
  const activeCount = countActivePosOperationFilters(filters);
  const range = resolvePosOperationRange(filters);
  const operationLabel = useCallback(
    (operationType: string) => {
      const key = `operations.${operationType.toLowerCase().replace(/_([a-z0-9])/g, (_, c: string) => c.toUpperCase())}`;
      const label = t(key);
      return label === key ? operationType : label;
    },
    [t],
  );
  const outcomeLabel = useCallback(
    (outcome: PosOperationLogItem["outcome"]) => t(`outcomes.${outcome}`),
    [t],
  );
  // 行内显示门店名称而不是编码；主档没有的门店回退显示编码。
  const storeNames = useMemo(
    () => new Map(stores.map((store) => [store.storeCode, store.storeName || store.storeCode])),
    [stores],
  );
  const storeName = filters.storeCode
    ? stores.find((store) => store.storeCode === filters.storeCode)?.storeName ?? filters.storeCode
    : null;
  const rangeChipLabel =
    filters.preset === "custom" && range
      ? `${formatDisplayDate(range.startDate)} – ${formatDisplayDate(range.endDate)}`
      : t(`presets.${filters.preset}`);

  const quickCount = (key: PosOperationQuickFilter): number | null => {
    if (!summary) return null;
    switch (key) {
      case "all":
        return summary.total;
      case "Succeeded":
        return summary.succeeded;
      case "Denied":
        return summary.denied;
      case "Failed":
        return summary.failed;
      case "emergencyOverride":
        return summary.emergencyOverride;
    }
  };
  const quickTone = (key: PosOperationQuickFilter, selected: boolean) => {
    if (key === "Denied") return selected ? styles.quickWarningOn : styles.quickWarning;
    if (key === "Failed") return selected ? styles.quickDangerOn : styles.quickDanger;
    if (key === "emergencyOverride") return selected ? styles.quickNeutralOn : styles.quickNeutral;
    return selected ? styles.quickAllOn : styles.quickNeutral;
  };
  const quickTextTone = (key: PosOperationQuickFilter, selected: boolean) => {
    if (selected) return styles.quickTextOn;
    if (key === "Denied") return { color: HB_COLORS.warning };
    if (key === "Failed") return { color: HB_COLORS.danger };
    return { color: HB_COLORS.textPrimary };
  };

  const renderSectionHeader = ({ section }: { section: PosOperationLogDaySection & { data: PosOperationLogItem[] } }) => (
    <View style={styles.dayHeader}>
      <Text style={styles.dayText}>
        {section.relative ? `${t(`groups.${section.relative}`)} · ` : ""}
        {section.dateLabel}
      </Text>
      <Text style={styles.dayText}>{t("groups.count", { count: section.items.length })}</Text>
    </View>
  );

  const listFooter = (
    <View style={[styles.footer, { paddingBottom: HB_SPACING.lg + insets.bottom }]}>
      {loadingMore ? (
        <View style={styles.footerRow}>
          <ActivityIndicator size="small" color={HB_COLORS.brand} />
          <Text style={styles.footerText}>{t("list.loadingMore")}</Text>
        </View>
      ) : items.length > 0 ? (
        <Text style={styles.footerText}>
          {hasMore
            ? t("list.loadedOf", { shown: items.length, total })
            : t("list.end", { total })}
        </Text>
      ) : null}
    </View>
  );

  return (
    <SafeAreaView style={styles.safe} edges={["top", "left", "right"]}>
      <View style={styles.header}>
        <IconButton icon="chevron-left" accessibilityLabel={t("actions.back")} onPress={onBack} />
        <View style={styles.heading}>
          <Text variant="titleMedium" style={styles.title}>
            {t("title")}
          </Text>
          <Text numberOfLines={1} style={styles.subtitle}>
            {scopeLabel}
          </Text>
        </View>
        <View>
          <IconButton
            icon="tune-variant"
            accessibilityLabel={t("actions.openFilters")}
            onPress={onOpenFilters}
          />
          {activeCount > 0 ? (
            <View style={styles.badge} pointerEvents="none">
              <Text style={styles.badgeText}>{activeCount}</Text>
            </View>
          ) : null}
        </View>
      </View>

      <ScrollView
        horizontal
        showsHorizontalScrollIndicator={false}
        // ScrollView 默认 flexGrow: 1，会和下方 flex: 1 的列表平分高度，把 chip 拉成整块；这里只占内容高度。
        style={styles.chipScroll}
        contentContainerStyle={styles.chipRow}
        keyboardShouldPersistTaps="handled"
      >
        <Chip label={rangeChipLabel} icon="calendar" selected onPress={onPickRange} />
        <Chip label={storeName ?? t("chips.allStores")} selected={Boolean(storeName)} onPress={onPickStore} />
        <Chip
          label={filters.operationType ? operationLabel(filters.operationType) : t("chips.allTypes")}
          selected={Boolean(filters.operationType)}
          onPress={onPickType}
        />
        <Chip
          label={filters.deviceSystem ? t(`platforms.${filters.deviceSystem}`) : t("chips.platform")}
          selected={Boolean(filters.deviceSystem)}
          onPress={onPickPlatform}
        />
      </ScrollView>

      <View style={styles.searchRow}>
        <Searchbar
          placeholder={t("search.placeholder")}
          value={searchDraft}
          onChangeText={onSearchDraftChange}
          onSubmitEditing={onSearchSubmit}
          onClearIconPress={() => {
            onSearchDraftChange("");
          }}
          multiline={false}
          numberOfLines={1}
          style={styles.search}
          inputStyle={styles.searchInput}
          returnKeyType="search"
        />
      </View>

      {filters.orderGuid ? (
        <View style={styles.traceBanner}>
          <Icon source="timeline-clock-outline" size={15} color={HB_COLORS.action} />
          <Text numberOfLines={1} style={styles.traceText}>
            {t("list.orderTrace", { order: shortenIdentifier(filters.orderGuid, 6) })}
          </Text>
          <Button compact onPress={onClearOrderTrace}>
            {t("actions.clearOrder")}
          </Button>
        </View>
      ) : null}

      <View style={styles.quickRow}>
        {QUICK_FILTERS.map((key) => {
          const selected = quick === key;
          const count = quickCount(key);
          return (
            <Pressable
              key={key}
              accessibilityRole="button"
              accessibilityState={{ selected }}
              onPress={() => onQuickFilter(key)}
              style={[styles.quick, quickTone(key, selected)]}
            >
              <Text style={[styles.quickText, quickTextTone(key, selected)]}>
                {t(`quick.${key === "emergencyOverride" ? key : key.toLowerCase()}`)}
                {count != null ? ` ${count}` : summaryError ? "" : " …"}
              </Text>
            </Pressable>
          );
        })}
      </View>

      {loading ? (
        <View style={styles.state}>
          <ActivityIndicator color={HB_COLORS.brand} />
          <Text style={styles.stateText}>{t("states.loading")}</Text>
        </View>
      ) : error ? (
        <View style={styles.state}>
          <EmptyState
            title={t("states.loadFailed")}
            description={error}
            actionLabel={t("actions.retry")}
            onAction={onRetry}
          />
        </View>
      ) : (
        <SectionList
          sections={sections}
          keyExtractor={(item) => item.eventId}
          renderItem={({ item }) => (
            <PosOperationLogRow
              item={item}
              storeName={storeNames.get(item.storeCode) ?? null}
              operationLabel={operationLabel}
              outcomeLabel={outcomeLabel}
              t={t}
              onPress={onOpenItem}
            />
          )}
          renderSectionHeader={renderSectionHeader}
          stickySectionHeadersEnabled
          onEndReachedThreshold={0.4}
          onEndReached={() => {
            if (hasMore && !loadingMore) onLoadMore();
          }}
          refreshControl={
            <RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor={HB_COLORS.brand} />
          }
          ListEmptyComponent={
            <View style={styles.state}>
              <EmptyState title={t("states.emptyTitle")} description={t("states.emptyDescription")} />
            </View>
          }
          ListFooterComponent={listFooter}
          keyboardShouldPersistTaps="handled"
          style={styles.list}
        />
      )}
    </SafeAreaView>
  );
}

function Chip({
  label,
  icon,
  selected,
  onPress,
}: {
  label: string;
  icon?: string;
  selected?: boolean;
  onPress: () => void;
}) {
  return (
    <Pressable
      accessibilityRole="button"
      onPress={onPress}
      style={[LOG_UI.chip, selected ? LOG_UI.chipSelected : null]}
    >
      {icon ? (
        <Icon source={icon} size={14} color={selected ? HB_COLORS.action : HB_COLORS.textSecondary} />
      ) : null}
      <Text numberOfLines={1} style={[LOG_UI.chipText, selected ? LOG_UI.chipTextSelected : null]}>
        {label}
      </Text>
      <Icon source="chevron-down" size={14} color={selected ? HB_COLORS.action : HB_COLORS.textSecondary} />
    </Pressable>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1, backgroundColor: HB_COLORS.background },
  header: { flexDirection: "row", alignItems: "center", paddingRight: HB_SPACING.xxs },
  heading: { flex: 1, minWidth: 0 },
  title: { fontWeight: "700", color: HB_COLORS.textPrimary },
  subtitle: { fontSize: 11, lineHeight: 14, color: HB_COLORS.textSecondary },
  badge: {
    position: "absolute",
    top: 6,
    right: 6,
    minWidth: 16,
    height: 16,
    paddingHorizontal: 4,
    borderRadius: 8,
    backgroundColor: HB_COLORS.action,
    alignItems: "center",
    justifyContent: "center",
  },
  badgeText: { fontSize: 10, lineHeight: 12, color: HB_COLORS.white, fontWeight: "700" },
  chipScroll: { flexGrow: 0, flexShrink: 0 },
  chipRow: {
    paddingHorizontal: HB_SPACING.md,
    gap: 6,
    paddingBottom: HB_SPACING.xs,
    alignItems: "center",
  },
  searchRow: { paddingHorizontal: HB_SPACING.md, paddingBottom: HB_SPACING.xs },
  search: {
    height: 38,
    borderRadius: HB_RADIUS.control,
    backgroundColor: HB_COLORS.white,
    borderWidth: 1,
    borderColor: HB_COLORS.outline,
  },
  searchInput: { fontSize: 13, minHeight: 0, paddingVertical: 0, alignSelf: "center" },
  traceBanner: {
    flexDirection: "row",
    alignItems: "center",
    gap: 6,
    marginHorizontal: HB_SPACING.md,
    marginBottom: HB_SPACING.xs,
    paddingLeft: HB_SPACING.sm,
    backgroundColor: "#EAF2FF",
    borderRadius: HB_RADIUS.control,
  },
  traceText: { flex: 1, fontSize: 12, color: HB_COLORS.action },
  quickRow: {
    flexDirection: "row",
    gap: 6,
    paddingHorizontal: HB_SPACING.md,
    paddingBottom: HB_SPACING.xs,
  },
  quick: {
    paddingHorizontal: 10,
    paddingVertical: 5,
    borderRadius: 999,
    borderWidth: 1,
  },
  quickText: { fontSize: 12, lineHeight: 16, fontWeight: "600" },
  quickTextOn: { color: HB_COLORS.white },
  quickNeutral: { borderColor: HB_COLORS.outline, backgroundColor: HB_COLORS.white },
  quickNeutralOn: { borderColor: HB_COLORS.textPrimary, backgroundColor: HB_COLORS.textPrimary },
  quickAllOn: { borderColor: HB_COLORS.textPrimary, backgroundColor: HB_COLORS.textPrimary },
  quickWarning: { borderColor: "#F9DBAF", backgroundColor: "#FFF4E5" },
  quickWarningOn: { borderColor: HB_COLORS.warning, backgroundColor: HB_COLORS.warning },
  quickDanger: { borderColor: "#FECDCA", backgroundColor: "#FEE4E2" },
  quickDangerOn: { borderColor: HB_COLORS.danger, backgroundColor: HB_COLORS.danger },
  list: { flex: 1 },
  dayHeader: {
    flexDirection: "row",
    justifyContent: "space-between",
    paddingHorizontal: HB_SPACING.sm,
    paddingVertical: 5,
    backgroundColor: HB_COLORS.surfaceMuted,
    borderBottomWidth: StyleSheet.hairlineWidth,
    borderBottomColor: HB_COLORS.outlineMuted,
  },
  dayText: { fontSize: 11, lineHeight: 14, color: HB_COLORS.textSecondary, fontWeight: "600" },
  footer: { paddingTop: HB_SPACING.sm, alignItems: "center" },
  footerRow: { flexDirection: "row", alignItems: "center", gap: 6 },
  footerText: { fontSize: 12, color: HB_COLORS.textSecondary },
  state: { paddingTop: HB_SPACING.xl, paddingHorizontal: HB_SPACING.lg, alignItems: "center", gap: HB_SPACING.sm },
  stateText: { fontSize: 13, color: HB_COLORS.textSecondary },
});
