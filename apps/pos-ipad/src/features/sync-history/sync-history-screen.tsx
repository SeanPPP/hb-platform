import { useEffect, useRef, useState, useSyncExternalStore } from "react";
import { useTranslation } from "react-i18next";
import {
  ActivityIndicator,
  FlatList,
  ScrollView,
  StyleSheet,
  Text,
  View,
  type StyleProp,
  type ViewStyle,
} from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";

import { businessDayUtcRange } from "@hb/pos-sync/features/sync-history/business-day-range";
import {
  resolveSyncHistoryLocale,
  syncHistoryText,
  type SyncHistoryCopyKey,
  type SyncHistoryLocale,
} from "./sync-history-copy";
import {
  type LocalSyncHistoryFilters,
  type LocalSyncHistoryOrderState,
} from "@hb/pos-sync/features/sync-history/sync-history-domain";
import {
  type SyncHistoryPresenter,
  type SyncHistoryPresenterState,
  type SyncHistoryRow,
} from "@hb/pos-sync/features/sync-history/sync-history-presenter";

import { PosDatePickerField } from "@/ui/controls/pos-date-picker-field";
import { PosPressable } from "@/ui/controls/pos-pressable";
import { posColors } from "@/ui/theme";

export const SYNC_HISTORY_MIN_TOUCH_TARGET = 44;

const FILTER_STATES: readonly LocalSyncHistoryOrderState[] = [
  "CompletedLocal",
  "PendingSync",
  "Syncing",
  "Synced",
  "Blocked403",
  "Rejected",
];

type SyncHistoryScreenProps = Readonly<{
  /** presenter 生命周期归调用方；路由卸载时调用 destroy，屏幕只负责订阅与首刷。 */
  presenter: SyncHistoryPresenter;
  /** 门店业务时区；未注入时仅为兼容旧调用方而使用终端当前 IANA 时区。 */
  businessTimeZone?: string;
  onExport(serializedJson: string): void | Promise<void>;
  onBack?(): void;
}>;

type ActionButtonProps = Readonly<{
  label: string;
  onPress(): void;
  disabled?: boolean;
  sound?: "tap" | "navigate";
  testID: string;
  tone?: "primary" | "secondary" | "quiet";
  style?: StyleProp<ViewStyle>;
}>;

export function SyncHistoryScreen({
  presenter,
  businessTimeZone,
  onExport,
  onBack,
}: SyncHistoryScreenProps) {
  const state = useSyncExternalStore(
    presenter.subscribe,
    presenter.getState,
    presenter.getState,
  );
  const { i18n } = useTranslation();
  const locale = resolveSyncHistoryLocale(
    i18n.resolvedLanguage ?? i18n.language,
  );
  const t = (
    key: SyncHistoryCopyKey,
    values?: Readonly<Record<string, string | number>>,
  ) => syncHistoryText(locale, key, values);
  const [dateFrom, setDateFrom] = useState(
    dateInputValue(state.filters.dateFromIso),
  );
  const [dateTo, setDateTo] = useState(
    dateInputValue(state.filters.dateToIso),
  );
  const [exportStatus, setExportStatus] = useState<
    "idle" | "exporting" | "success" | "failed"
  >("idle");
  const exportInFlight = useRef(false);
  const mounted = useRef(true);

  useEffect(() => {
    mounted.current = true;
    void presenter.refresh();
    return () => {
      mounted.current = false;
    };
  }, [presenter]);

  const loading = state.kind === "loading";
  const selectedCount = state.selectedOrderGuids.length;
  const canManualRetransmit =
    state.access.canView && state.access.canManualRetransmit;

  const applyFilters = (
    states: readonly LocalSyncHistoryOrderState[] = state.filters.states,
  ) => {
    presenter.setFilters(
      buildFilters(dateFrom, dateTo, states, businessTimeZone),
    );
    void presenter.refresh();
  };

  const toggleState = (candidate: LocalSyncHistoryOrderState) => {
    const current = state.filters.states;
    const next = current.includes(candidate)
      ? current.filter((value) => value !== candidate)
      : [...current, candidate];
    applyFilters(next);
  };

  const retransmitDateRange = () => {
    presenter.setFilters(
      buildFilters(
        dateFrom,
        dateTo,
        state.filters.states,
        businessTimeZone,
      ),
    );
    void presenter.requestRetransmitDateRange();
  };

  const exportSupportBundle = async () => {
    if (!state.access.canExport || exportInFlight.current) return;
    exportInFlight.current = true;
    setExportStatus("exporting");
    try {
      const serializedJson = await presenter.serializeSupportExport();
      await onExport(serializedJson);
      if (mounted.current) setExportStatus("success");
    } catch {
      // 中文注释：失败提示绝不拼接异常或导出正文，避免把诊断内容回显到收银屏。
      if (mounted.current) setExportStatus("failed");
    } finally {
      exportInFlight.current = false;
    }
  };

  return (
    <SafeAreaView style={styles.safeArea} testID="sync-history-screen">
      <View style={styles.header}>
        {onBack ? (
          <ActionButton
            label={t("action.back")}
            onPress={onBack}
            sound="navigate"
            testID="sync-history-back"
            tone="quiet"
          />
        ) : null}
        <View style={styles.headerIdentity}>
          <Text style={styles.title}>{t("title")}</Text>
          <Text style={styles.subtitle}>{t("subtitle")}</Text>
        </View>
        <View style={styles.pendingCard} testID="sync-history-pending-count">
          <Text style={styles.pendingLabel}>{t("pending.label")}</Text>
          <Text style={styles.pendingCount}>
            {t("pending.count", { count: state.pendingCount })}
          </Text>
        </View>
        <ActionButton
          disabled={loading}
          label={loading ? t("action.refreshing") : t("action.refresh")}
          onPress={() => {
            void presenter.refresh();
          }}
          testID="sync-history-refresh"
        />
      </View>

      <View style={styles.workspace} testID="sync-history-workspace">
        <ScrollView
          contentContainerStyle={styles.filtersPaneContent}
          keyboardShouldPersistTaps="handled"
          showsVerticalScrollIndicator
          style={styles.filtersPane}
          testID="sync-history-filters-scroll"
        >
          <Text style={styles.paneTitle}>{t("filters.title")}</Text>
          <Text style={styles.sectionLabel}>{t("filters.status")}</Text>
          <View style={styles.filterGrid}>
            <FilterChip
              label={t("filters.all")}
              onPress={() => applyFilters([])}
              selected={state.filters.states.length === 0}
              testID="sync-history-filter-all"
            />
            {FILTER_STATES.map((status) => (
              <FilterChip
                key={status}
                label={t(`status.${status}`)}
                onPress={() => toggleState(status)}
                selected={state.filters.states.includes(status)}
                testID={`sync-history-filter-${status}`}
              />
            ))}
          </View>

          <DateField
            label={t("filters.dateFrom")}
            locale={locale}
            onChange={setDateFrom}
            testID="sync-history-date-from"
            value={dateFrom}
          />
          <DateField
            label={t("filters.dateTo")}
            locale={locale}
            onChange={setDateTo}
            testID="sync-history-date-to"
            value={dateTo}
          />
          <ActionButton
            disabled={loading}
            label={t("filters.apply")}
            onPress={() => applyFilters()}
            testID="sync-history-apply-filters"
            tone="secondary"
          />

          <View style={styles.divider} />
          <Text style={styles.selectionCount}>
            {t("selection.count", { count: selectedCount })}
          </Text>
          <ActionButton
            disabled={loading || !canManualRetransmit || selectedCount === 0}
            label={t("action.retransmitSelected")}
            onPress={() => {
              void presenter.requestRetransmitSelected();
            }}
            testID="sync-history-retransmit-selected"
          />
          <ActionButton
            disabled={
              loading ||
              !canManualRetransmit ||
              !dateFrom ||
              !dateTo
            }
            label={t("action.retransmitRange")}
            onPress={retransmitDateRange}
            testID="sync-history-retransmit-range"
            tone="secondary"
          />
          <ActionButton
            disabled={exportStatus === "exporting" || !state.access.canExport}
            label={
              exportStatus === "exporting"
                ? t("action.exporting")
                : t("action.export")
            }
            onPress={() => {
              void exportSupportBundle();
            }}
            testID="sync-history-export"
            tone="quiet"
          />
          {exportStatus === "success" ? (
            <Notice
              message={t("export.success")}
              testID="sync-history-export-success"
              tone="success"
            />
          ) : null}
          {exportStatus === "failed" ? (
            <Notice
              message={t("export.failed")}
              testID="sync-history-export-failed"
              tone="danger"
            />
          ) : null}
        </ScrollView>

        <View style={styles.listPane}>
          <View style={styles.listHeader}>
            <Text style={styles.paneTitle}>{t("list.title")}</Text>
            {loading && state.rows.length > 0 ? (
              <ActivityIndicator
                color={posColors.orange}
                testID="sync-history-inline-loading"
              />
            ) : null}
          </View>

          <RetransmitNotices locale={locale} state={state} />

          {state.kind === "loading" && state.rows.length === 0 ? (
            <CenteredState
              loading
              message={t("loading.title")}
              testID="sync-history-loading"
            />
          ) : null}
          {state.kind === "empty" ? (
            <CenteredState
              hint={t("empty.hint")}
              message={t("empty.title")}
              testID="sync-history-empty"
            />
          ) : null}
          {state.kind === "failed" ? (
            <CenteredState
              actionLabel={t("action.refresh")}
              hint={t("failed.hint")}
              message={`${t("failed.title")} · ${state.errorCode}`}
              onAction={() => {
                void presenter.refresh();
              }}
              testID="sync-history-failed"
            />
          ) : null}
          {(state.kind === "ready" ||
            (state.kind === "loading" && state.rows.length > 0)) ? (
            <FlatList
              contentContainerStyle={styles.orderList}
              data={state.rows}
              keyExtractor={(row) => row.orderGuid}
              renderItem={({ item }) => (
                <SyncHistoryOrderRow
                  canSelect={
                    canManualRetransmit &&
                    item.retransmit.kind === "allowed"
                  }
                  locale={locale}
                  onSelected={(selected) =>
                    presenter.setSelected(item.orderGuid, selected)
                  }
                  row={item}
                />
              )}
              testID="sync-history-list"
            />
          ) : null}

          <View style={styles.listFooter}>
            <ActionButton
              disabled={loading || state.nextBeforeLocalSequence === null}
              label={t("action.loadMore")}
              onPress={() => {
                void presenter.loadNextPage();
              }}
              style={styles.loadMore}
              testID="sync-history-load-more"
              tone="quiet"
            />
          </View>
        </View>
      </View>
    </SafeAreaView>
  );
}

function SyncHistoryOrderRow({
  canSelect,
  locale,
  onSelected,
  row,
}: Readonly<{
  canSelect: boolean;
  locale: SyncHistoryLocale;
  onSelected(selected: boolean): void;
  row: SyncHistoryRow;
}>) {
  const t = (
    key: SyncHistoryCopyKey,
    values?: Readonly<Record<string, string | number>>,
  ) => syncHistoryText(locale, key, values);
  const gateKey = row.retransmit.kind === "blocked"
    ? gateCopyKey(row.retransmit.reason)
    : null;

  return (
    <PosPressable
      accessibilityLabel={`${t("list.order", { orderGuid: row.orderGuid })}, ${t(`status.${row.state}`)}`}
      accessibilityRole="checkbox"
      accessibilityState={{
        checked: row.isSelected,
        disabled: !canSelect,
      }}
      {...(!canSelect ? { disabled: true } : {})}
      onPress={() => onSelected(!row.isSelected)}
      style={({ pressed }) => [
        styles.orderRow,
        row.isSelected && styles.orderRowSelected,
        pressed && styles.pressed,
      ]}
      testID={`sync-history-row-${row.orderGuid}`}
    >
      <View style={styles.checkbox} testID={`sync-history-select-${row.orderGuid}`}>
        <Text style={styles.checkboxText}>{row.isSelected ? "✓" : ""}</Text>
      </View>
      <View style={styles.orderIdentity}>
        <View style={styles.orderTitleRow}>
          <Text style={styles.sequence}>
            {t("list.sequence", { sequence: row.localSequence })}
          </Text>
          <StatusBadge locale={locale} state={row.state} />
        </View>
        <Text numberOfLines={1} style={styles.orderGuid}>
          {t("list.order", { orderGuid: row.orderGuid })}
        </Text>
        <Text style={styles.orderTime}>
          {formatDateTime(row.soldAtIso, locale)}
        </Text>
        <Text style={styles.deviceLine}>
          {row.storeCode} · {row.deviceCode}
        </Text>
        {gateKey ? (
          <Text
            style={[
              styles.gateText,
              row.state === "Rejected" && styles.gateTextDanger,
            ]}
            testID={`sync-history-gate-${row.orderGuid}`}
          >
            {t(gateKey)}
          </Text>
        ) : null}
      </View>
      <View style={styles.amountColumn}>
        <Text style={styles.metaLabel}>{t("list.amount")}</Text>
        <Text style={styles.amount}>
          {formatAud(row.actualAmountCents, locale)}
        </Text>
        {row.discountCents !== 0 ? (
          <Text style={styles.discount}>
            {t("list.discount")} −{formatAud(row.discountCents, locale)}
          </Text>
        ) : null}
        <Text numberOfLines={2} style={styles.tender}>
          {t("list.tender")} · {row.tenderSummary || "—"}
        </Text>
      </View>
      <View style={styles.outboxColumn}>
        <Text style={styles.metaLabel}>{t("list.outbox")}</Text>
        <Text style={styles.outboxState}>
          {row.outbox ? t(`outbox.${row.outbox.state}`) : "—"}
        </Text>
        {row.outbox ? (
          <Text style={styles.outboxMeta}>
            {t("list.attempts", { count: row.outbox.attemptCount })}
          </Text>
        ) : null}
        {row.outbox?.lastErrorCode ? (
          <Text
            numberOfLines={1}
            style={styles.safeError}
            testID={`sync-history-error-${row.orderGuid}`}
          >
            {t("list.error", { code: row.outbox.lastErrorCode })}
          </Text>
        ) : null}
        {row.outbox?.nextAttemptAtIso ? (
          <Text numberOfLines={1} style={styles.outboxMeta}>
            {t("list.nextAttempt", {
              time: formatDateTime(row.outbox.nextAttemptAtIso, locale),
            })}
          </Text>
        ) : null}
      </View>
    </PosPressable>
  );
}

function RetransmitNotices({
  locale,
  state,
}: Readonly<{
  locale: SyncHistoryLocale;
  state: SyncHistoryPresenterState;
}>) {
  const result = state.lastRetransmit;
  if (!result) return null;
  const t = (
    key: SyncHistoryCopyKey,
    values?: Readonly<Record<string, string | number>>,
  ) => syncHistoryText(locale, key, values);
  const summary =
    result.kind === "requested"
      ? t("retransmit.requested", { count: result.requestedCount })
      : result.kind === "nothing-eligible"
        ? t("retransmit.none")
        : t("retransmit.failed");

  return (
    <View style={styles.noticeStack}>
      <Notice
        message={summary}
        testID="sync-history-retransmit-result"
        tone={result.kind === "requested" ? "success" : "warning"}
      />
      {result.reauthenticationRequiredCount > 0 ? (
        <Notice
          message={t("gate.reauthentication", {
            count: result.reauthenticationRequiredCount,
          })}
          testID="sync-history-reauthentication-required"
          tone="warning"
        />
      ) : null}
      {result.supervisorRequiredCount > 0 ? (
        <Notice
          message={t("gate.supervisor", {
            count: result.supervisorRequiredCount,
          })}
          testID="sync-history-supervisor-required"
          tone="danger"
        />
      ) : null}
    </View>
  );
}

function FilterChip({
  label,
  onPress,
  selected,
  testID,
}: Readonly<{
  label: string;
  onPress(): void;
  selected: boolean;
  testID: string;
}>) {
  return (
    <PosPressable
      accessibilityRole="checkbox"
      accessibilityState={{ checked: selected }}
      onPress={onPress}
      style={({ pressed }) => [
        styles.filterChip,
        selected && styles.filterChipSelected,
        pressed && styles.pressed,
      ]}
      testID={testID}
    >
      <Text
        style={[
          styles.filterChipText,
          selected && styles.filterChipTextSelected,
        ]}
      >
        {label}
      </Text>
    </PosPressable>
  );
}

function DateField({
  label,
  locale,
  onChange,
  testID,
  value,
}: Readonly<{
  label: string;
  locale: SyncHistoryLocale;
  onChange(value: string | null): void;
  testID: string;
  value: string | null;
}>) {
  return (
    <View style={styles.dateField}>
      <Text style={styles.sectionLabel}>{label}</Text>
      <PosDatePickerField
        accessibilityLabel={label}
        allowClear
        locale={locale}
        onChange={onChange}
        testID={testID}
        value={value}
      />
    </View>
  );
}

function ActionButton({
  disabled = false,
  label,
  onPress,
  sound = "tap",
  style,
  testID,
  tone = "primary",
}: ActionButtonProps) {
  return (
    <PosPressable
      accessibilityRole="button"
      accessibilityState={{ disabled }}
      disabled={disabled}
      onPress={onPress}
      sound={sound}
      style={({ pressed }) => [
        styles.actionButton,
        actionToneStyles[tone],
        disabled && styles.actionButtonDisabled,
        pressed && !disabled && styles.pressed,
        style,
      ]}
      testID={testID}
    >
      <Text
        style={[
          styles.actionButtonText,
          tone !== "primary" && styles.actionButtonTextDark,
          disabled && styles.actionButtonTextDisabled,
        ]}
      >
        {label}
      </Text>
    </PosPressable>
  );
}

function CenteredState({
  actionLabel,
  hint,
  loading = false,
  message,
  onAction,
  testID,
}: Readonly<{
  actionLabel?: string;
  hint?: string;
  loading?: boolean;
  message: string;
  onAction?(): void;
  testID: string;
}>) {
  return (
    <View style={styles.centeredState} testID={testID}>
      {loading ? <ActivityIndicator color={posColors.orange} size="large" /> : null}
      <Text style={styles.centeredTitle}>{message}</Text>
      {hint ? <Text style={styles.centeredHint}>{hint}</Text> : null}
      {actionLabel && onAction ? (
        <ActionButton
          label={actionLabel}
          onPress={onAction}
          style={styles.centeredAction}
          testID={`${testID}-action`}
        />
      ) : null}
    </View>
  );
}

function Notice({
  message,
  testID,
  tone,
}: Readonly<{
  message: string;
  testID: string;
  tone: "success" | "warning" | "danger";
}>) {
  return (
    <View
      accessibilityRole="alert"
      style={[
        styles.notice,
        tone === "success" && styles.noticeSuccess,
        tone === "warning" && styles.noticeWarning,
        tone === "danger" && styles.noticeDanger,
      ]}
      testID={testID}
    >
      <Text
        style={[
          styles.noticeText,
          tone === "success" && styles.noticeTextSuccess,
          tone === "danger" && styles.noticeTextDanger,
        ]}
      >
        {message}
      </Text>
    </View>
  );
}

function StatusBadge({
  locale,
  state,
}: Readonly<{
  locale: SyncHistoryLocale;
  state: LocalSyncHistoryOrderState;
}>) {
  const danger = state === "Rejected";
  const warning = state === "Blocked403" || state === "PendingSync";
  const success = state === "Synced";
  return (
    <View
      style={[
        styles.statusBadge,
        warning && styles.statusBadgeWarning,
        danger && styles.statusBadgeDanger,
        success && styles.statusBadgeSuccess,
      ]}
    >
      <Text
        style={[
          styles.statusBadgeText,
          danger && styles.statusBadgeTextDanger,
          success && styles.statusBadgeTextSuccess,
        ]}
      >
        {syncHistoryText(locale, `status.${state}`)}
      </Text>
    </View>
  );
}

function gateCopyKey(
  reason:
    | "synced"
    | "syncing"
    | "reauthentication-required"
    | "supervisor-required"
    | "no-pending-outbox",
): SyncHistoryCopyKey {
  switch (reason) {
    case "synced":
      return "gate.synced";
    case "syncing":
      return "gate.syncing";
    case "reauthentication-required":
      return "gate.reauthenticationRow";
    case "supervisor-required":
      return "gate.supervisorRow";
    case "no-pending-outbox":
      return "gate.noPending";
  }
}

function buildFilters(
  dateFrom: string | null,
  dateTo: string | null,
  states: readonly LocalSyncHistoryOrderState[],
  businessTimeZone?: string,
): LocalSyncHistoryFilters {
  const range = businessDayUtcRange(
    dateFrom ?? "",
    dateTo ?? "",
    businessTimeZone,
  );
  if (!range) {
    // 非 ISO 哨兵会被 presenter 在查询 Port 之前拒绝，绝不退化成无日期筛选。
    return {
      dateFromIso: "invalid-business-date-range",
      dateToIso: "invalid-business-date-range",
      states,
    };
  }
  return {
    ...range,
    states,
  };
}

function dateInputValue(value: string | null): string | null {
  return value?.slice(0, 10) ?? null;
}

function formatAud(valueCents: number, locale: SyncHistoryLocale): string {
  return new Intl.NumberFormat(locale === "zh" ? "zh-CN" : "en-AU", {
    currency: "AUD",
    style: "currency",
  }).format(valueCents / 100);
}

function formatDateTime(value: string, locale: SyncHistoryLocale): string {
  const date = new Date(value);
  if (!Number.isFinite(date.getTime())) return "—";
  return new Intl.DateTimeFormat(locale === "zh" ? "zh-CN" : "en-AU", {
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    month: "2-digit",
    year: "numeric",
  }).format(date);
}

const actionToneStyles = StyleSheet.create({
  primary: {
    backgroundColor: posColors.orange,
    borderColor: posColors.orange,
  },
  quiet: {
    backgroundColor: posColors.surface,
    borderColor: posColors.border,
  },
  secondary: {
    backgroundColor: posColors.blueSoft,
    borderColor: posColors.blue,
  },
});

const styles = StyleSheet.create({
  actionButton: {
    alignItems: "center",
    borderRadius: 4,
    borderWidth: 1,
    justifyContent: "center",
    minHeight: SYNC_HISTORY_MIN_TOUCH_TARGET,
    minWidth: SYNC_HISTORY_MIN_TOUCH_TARGET,
    paddingHorizontal: 14,
  },
  actionButtonDisabled: {
    backgroundColor: "#E4E1DA",
    borderColor: "#D0CCC2",
    opacity: 0.72,
  },
  actionButtonText: {
    color: "#FFFFFF",
    fontSize: 14,
    fontWeight: "800",
  },
  actionButtonTextDark: {
    color: posColors.ink,
  },
  actionButtonTextDisabled: {
    color: "#7C8287",
  },
  amount: {
    color: posColors.ink,
    fontSize: 18,
    fontVariant: ["tabular-nums"],
    fontWeight: "900",
  },
  amountColumn: {
    alignItems: "flex-end",
    gap: 4,
    width: 160,
  },
  centeredAction: {
    marginTop: 12,
    minWidth: 150,
  },
  centeredHint: {
    color: posColors.mutedInk,
    fontSize: 14,
    lineHeight: 20,
    marginTop: 5,
    maxWidth: 440,
    textAlign: "center",
  },
  centeredState: {
    alignItems: "center",
    flex: 1,
    justifyContent: "center",
    minHeight: 260,
    padding: 32,
  },
  centeredTitle: {
    color: posColors.ink,
    fontSize: 18,
    fontWeight: "800",
    marginTop: 12,
    textAlign: "center",
  },
  checkbox: {
    alignItems: "center",
    backgroundColor: posColors.surface,
    borderColor: posColors.mutedInk,
    borderRadius: 3,
    borderWidth: 2,
    height: 26,
    justifyContent: "center",
    marginTop: 2,
    width: 26,
  },
  checkboxText: {
    color: posColors.blue,
    fontSize: 18,
    fontWeight: "900",
  },
  dateField: {
    gap: 6,
  },
  deviceLine: {
    color: posColors.mutedInk,
    fontSize: 11,
    marginTop: 3,
  },
  discount: {
    color: posColors.green,
    fontSize: 12,
    fontVariant: ["tabular-nums"],
    fontWeight: "700",
  },
  divider: {
    backgroundColor: posColors.border,
    height: 1,
    marginVertical: 2,
  },
  filterChip: {
    alignItems: "center",
    backgroundColor: posColors.surface,
    borderColor: posColors.border,
    borderRadius: 4,
    borderWidth: 1,
    flexBasis: "47%",
    flexGrow: 1,
    justifyContent: "center",
    minHeight: SYNC_HISTORY_MIN_TOUCH_TARGET,
    paddingHorizontal: 8,
  },
  filterChipSelected: {
    backgroundColor: posColors.blueSoft,
    borderColor: posColors.blue,
  },
  filterChipText: {
    color: posColors.mutedInk,
    fontSize: 12,
    fontWeight: "700",
    textAlign: "center",
  },
  filterChipTextSelected: {
    color: posColors.blue,
    fontWeight: "900",
  },
  filterGrid: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
  },
  filtersPane: {
    backgroundColor: "#F8F6F1",
    borderColor: posColors.border,
    borderRadius: 5,
    borderWidth: 1,
    minHeight: 0,
    width: 292,
  },
  filtersPaneContent: {
    gap: 10,
    padding: 14,
  },
  gateText: {
    color: "#8A531A",
    fontSize: 12,
    fontWeight: "800",
    marginTop: 5,
  },
  gateTextDanger: {
    color: posColors.red,
  },
  header: {
    alignItems: "center",
    backgroundColor: posColors.surface,
    borderBottomColor: posColors.border,
    borderBottomWidth: 1,
    flexDirection: "row",
    gap: 14,
    minHeight: 72,
    paddingHorizontal: 20,
    paddingVertical: 10,
  },
  headerIdentity: {
    flex: 1,
  },
  listFooter: {
    alignItems: "center",
    borderTopColor: posColors.border,
    borderTopWidth: 1,
    paddingTop: 10,
  },
  listHeader: {
    alignItems: "center",
    flexDirection: "row",
    justifyContent: "space-between",
    minHeight: SYNC_HISTORY_MIN_TOUCH_TARGET,
  },
  listPane: {
    backgroundColor: posColors.surface,
    borderColor: posColors.border,
    borderRadius: 5,
    borderWidth: 1,
    flex: 1,
    gap: 10,
    minWidth: 520,
    padding: 14,
  },
  loadMore: {
    minWidth: 180,
  },
  metaLabel: {
    color: posColors.mutedInk,
    fontSize: 10,
    fontWeight: "800",
    letterSpacing: 0.5,
    textTransform: "uppercase",
  },
  notice: {
    backgroundColor: posColors.orangeSoft,
    borderColor: posColors.orange,
    borderLeftWidth: 4,
    borderRadius: 3,
    minHeight: SYNC_HISTORY_MIN_TOUCH_TARGET,
    paddingHorizontal: 12,
    paddingVertical: 10,
  },
  noticeDanger: {
    backgroundColor: posColors.redSoft,
    borderColor: posColors.red,
  },
  noticeStack: {
    gap: 6,
  },
  noticeSuccess: {
    backgroundColor: posColors.greenSoft,
    borderColor: posColors.green,
  },
  noticeText: {
    color: "#744024",
    fontSize: 13,
    fontWeight: "700",
  },
  noticeTextDanger: {
    color: posColors.red,
  },
  noticeTextSuccess: {
    color: posColors.green,
  },
  noticeWarning: {
    backgroundColor: posColors.orangeSoft,
    borderColor: posColors.orange,
  },
  orderGuid: {
    color: posColors.ink,
    fontSize: 12,
    fontWeight: "700",
    marginTop: 5,
  },
  orderIdentity: {
    flex: 1,
    minWidth: 220,
  },
  orderList: {
    gap: 8,
    paddingBottom: 8,
  },
  orderRow: {
    alignItems: "flex-start",
    backgroundColor: "#FCFBF8",
    borderColor: posColors.border,
    borderRadius: 4,
    borderWidth: 1,
    flexDirection: "row",
    gap: 12,
    minHeight: 124,
    padding: 12,
  },
  orderRowSelected: {
    backgroundColor: posColors.blueSoft,
    borderColor: posColors.blue,
  },
  orderTime: {
    color: posColors.mutedInk,
    fontSize: 12,
    fontVariant: ["tabular-nums"],
    marginTop: 3,
  },
  orderTitleRow: {
    alignItems: "center",
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 7,
  },
  outboxColumn: {
    gap: 4,
    width: 196,
  },
  outboxMeta: {
    color: posColors.mutedInk,
    fontSize: 11,
    fontVariant: ["tabular-nums"],
  },
  outboxState: {
    color: posColors.ink,
    fontSize: 14,
    fontWeight: "800",
  },
  paneTitle: {
    color: posColors.ink,
    fontSize: 17,
    fontWeight: "900",
  },
  pendingCard: {
    alignItems: "flex-end",
    backgroundColor: posColors.orangeSoft,
    borderColor: posColors.orange,
    borderRadius: 4,
    borderWidth: 1,
    minWidth: 116,
    paddingHorizontal: 14,
    paddingVertical: 8,
  },
  pendingCount: {
    color: posColors.ink,
    fontSize: 18,
    fontVariant: ["tabular-nums"],
    fontWeight: "900",
  },
  pendingLabel: {
    color: "#744024",
    fontSize: 10,
    fontWeight: "800",
    textTransform: "uppercase",
  },
  pressed: {
    opacity: 0.72,
  },
  safeArea: {
    backgroundColor: posColors.canvas,
    flex: 1,
  },
  safeError: {
    color: posColors.red,
    fontFamily: "Courier",
    fontSize: 10,
  },
  sectionLabel: {
    color: posColors.ink,
    fontSize: 12,
    fontWeight: "800",
  },
  selectionCount: {
    color: posColors.mutedInk,
    fontSize: 13,
    fontVariant: ["tabular-nums"],
    fontWeight: "700",
  },
  sequence: {
    color: posColors.ink,
    fontSize: 14,
    fontVariant: ["tabular-nums"],
    fontWeight: "900",
  },
  statusBadge: {
    backgroundColor: posColors.blueSoft,
    borderRadius: 3,
    paddingHorizontal: 7,
    paddingVertical: 3,
  },
  statusBadgeDanger: {
    backgroundColor: posColors.redSoft,
  },
  statusBadgeSuccess: {
    backgroundColor: posColors.greenSoft,
  },
  statusBadgeText: {
    color: posColors.blue,
    fontSize: 10,
    fontWeight: "900",
  },
  statusBadgeTextDanger: {
    color: posColors.red,
  },
  statusBadgeTextSuccess: {
    color: posColors.green,
  },
  statusBadgeWarning: {
    backgroundColor: posColors.orangeSoft,
  },
  subtitle: {
    color: posColors.mutedInk,
    fontSize: 13,
    marginTop: 3,
  },
  tender: {
    color: posColors.mutedInk,
    fontSize: 11,
    marginTop: 4,
  },
  title: {
    color: posColors.ink,
    fontSize: 23,
    fontWeight: "900",
  },
  workspace: {
    flex: 1,
    flexDirection: "row",
    gap: 12,
    minHeight: 0,
    padding: 14,
  },
});
