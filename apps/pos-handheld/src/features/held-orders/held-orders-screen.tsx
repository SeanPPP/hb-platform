import { useEffect, useState, useSyncExternalStore } from "react";
import { useTranslation } from "react-i18next";
import {
  ActivityIndicator,
  AppState,
  FlatList,
  StyleSheet,
  Text,
  View,
} from "react-native";

import {
  heldOrdersText,
  resolveHeldOrdersLocale,
  type HeldOrdersCopyKey,
} from "./held-orders-copy";
import {
  HeldOrdersPresenter,
  type HeldOrderViewRow,
  type HeldOrderViewStatus,
} from "./held-orders-presenter";

import {
  PosKeyboardAwareScrollView,
  PosKeyboardAwareTextInput,
} from "@/ui/controls/pos-keyboard-aware-scroll-view";
import { HandheldActionButton } from "@/ui/handheld/handheld-actions";
import { HandheldStateSurface } from "@/ui/handheld/handheld-design-states";
import {
  HandheldPageHeader,
  HandheldScreenFrame,
  HandheldSection,
  HandheldStatusBadge,
} from "@/ui/handheld/handheld-layout";
import { posColors } from "@/ui/theme";

export const HELD_ORDERS_MIN_TOUCH_TARGET = 48;
export const HELD_ORDERS_AUTO_REFRESH_INTERVAL_MS = 10_000;

type HeldOrdersScreenProps = Readonly<{
  presenter: HeldOrdersPresenter;
  onBack?(): void;
}>;

export function HeldOrdersScreen({ presenter, onBack }: HeldOrdersScreenProps) {
  const state = useSyncExternalStore(
    presenter.subscribe,
    presenter.getState,
    presenter.getState,
  );
  const { i18n } = useTranslation();
  const locale = resolveHeldOrdersLocale(
    i18n.resolvedLanguage ?? i18n.language,
  );
  const dateFilter = state.dateFilter ?? "today";
  const sourceTab = state.sourceTab ?? "local";
  const shareBusyHoldIds = state.shareBusyHoldIds ?? [];
  const [selectedHoldId, setSelectedHoldId] = useState<string | null>(null);
  const [forceReleaseFor, setForceReleaseFor] = useState<string | null>(null);
  const [forceReleaseReason, setForceReleaseReason] = useState("");
  const [deleteFor, setDeleteFor] = useState<string | null>(null);
  const t = (
    key: HeldOrdersCopyKey,
    values?: Readonly<Record<string, string | number>>,
  ) => heldOrdersText(locale, key, values);

  useEffect(() => {
    void presenter.refresh();
    presenter.startAutoRefresh?.(HELD_ORDERS_AUTO_REFRESH_INTERVAL_MS);
    const subscription = AppState.addEventListener("change", (next) => {
      if (next === "active") {
        presenter.startAutoRefresh?.(HELD_ORDERS_AUTO_REFRESH_INTERVAL_MS);
      } else {
        presenter.stopAutoRefresh?.();
      }
    });
    return () => {
      subscription.remove();
      presenter.stopAutoRefresh?.();
    };
  }, [presenter]);

  const selectedRow = selectedHoldId
    ? (state.rows.find((row) => row.holdId === selectedHoldId) ?? null)
    : null;

  useEffect(() => {
    if (selectedHoldId && !selectedRow && state.kind === "ready") {
      setSelectedHoldId(null);
    }
  }, [selectedHoldId, selectedRow, state.kind]);

  return (
    <HandheldScreenFrame
      footer={
        selectedRow ? (
          <HandheldActionButton
            label={t("action.backToList")}
            onPress={() => setSelectedHoldId(null)}
            sound="navigate"
            testID="held-order-detail-back"
            variant="secondary"
          />
        ) : (
          <HandheldActionButton
            disabled={state.busy}
            label={t("action.hold")}
            onPress={() => void presenter.hold()}
            testID="held-orders-hold"
          />
        )
      }
      header={
        <HandheldPageHeader
          eyebrow={t("list.title")}
          leading={
            onBack ? (
              <HandheldActionButton
                label={t("action.back")}
                onPress={onBack}
                sound="navigate"
                testID="held-orders-back"
                variant="secondary"
              />
            ) : null
          }
          subtitle={t("subtitle")}
          title={selectedRow ? t("detail.title") : t("title")}
          trailing={
            <HandheldActionButton
              disabled={state.busy || state.kind === "loading"}
              label={t("action.refresh")}
              onPress={() => void presenter.refresh()}
              testID="held-orders-refresh"
              variant="secondary"
            />
          }
        />
      }
      testID="held-orders-screen"
    >
      {selectedRow ? (
        <HeldOrderDetail
          busy={state.busy}
          locale={locale}
          onRecall={() => void recallRow(presenter, selectedRow, onBack)}
          onRecover={() => void recoverRow(presenter, selectedRow, onBack)}
          onRelease={() => void presenter.release(selectedRow.holdId)}
          row={selectedRow}
          t={t}
        />
      ) : (
        <HandheldStateSurface
          slug="held-orders-list"
          style={styles.singleColumn}
        >
          <View style={styles.listHeader}>
            <Text style={styles.listTitle}>{t("list.title")}</Text>
            <View style={styles.listHeaderActions}>
              {state.busy || state.kind === "loading" ? (
                <ActivityIndicator
                  color={posColors.orange}
                  testID="held-orders-loading-indicator"
                />
              ) : null}
              <View style={styles.filterGroup}>
                <FilterButton
                  label={t("filter.today")}
                  onPress={() => presenter.setDateFilter("today")}
                  selected={dateFilter === "today"}
                  testID="held-orders-filter-today"
                />
                <FilterButton
                  label={t("filter.all")}
                  onPress={() => presenter.setDateFilter("all")}
                  selected={dateFilter === "all"}
                  testID="held-orders-filter-all"
                />
              </View>
              {state.sharedEnabled ? (
                <View style={styles.filterGroup}>
                  <FilterButton
                    label={t("filter.local")}
                    onPress={() => presenter.setSourceTab("local")}
                    selected={sourceTab === "local"}
                    testID="held-orders-source-local"
                  />
                  <FilterButton
                    label={t("filter.other")}
                    onPress={() => presenter.setSourceTab("other")}
                    selected={sourceTab === "other"}
                    testID="held-orders-source-other"
                  />
                </View>
              ) : null}
            </View>
          </View>

          {sourceTab === "other" && state.refreshError ? (
            <Text style={styles.syncNotice} testID="held-orders-refresh-error">
              {t("error.shared-sync")}
            </Text>
          ) : null}

          {state.lastAction ? (
            <Text
              style={[
                styles.notice,
                state.lastAction.ok
                  ? styles.noticeSuccess
                  : styles.noticeDanger,
              ]}
              testID="held-orders-result"
            >
              {t(`result.${state.lastAction.code}`)}
            </Text>
          ) : null}

          {state.kind === "loading" && !state.rows.length ? (
            <CenteredState
              message={t("loading")}
              testID="held-orders-loading"
            />
          ) : null}
          {state.kind === "unauthorized" ? (
            <CenteredState
              hint={t("unauthorized.hint")}
              message={t("unauthorized.title")}
              testID="held-orders-unauthorized"
            />
          ) : null}
          {state.kind === "failed" ? (
            <CenteredState
              actionLabel={t("action.refresh")}
              hint={t("failed.hint")}
              message={t("failed.title")}
              onAction={() => void presenter.refresh()}
              testID="held-orders-failed"
            />
          ) : null}
          {forceReleaseFor ? (
            <PosKeyboardAwareScrollView
              contentContainerStyle={styles.forceReleasePanelContent}
              keyboardShouldPersistTaps="handled"
              showsVerticalScrollIndicator={false}
              style={styles.forceReleasePanel}
              testID="held-orders-force-release-panel"
            >
              <Text style={styles.panelTitle}>{t("forceRelease.title")}</Text>
              <PosKeyboardAwareTextInput
                accessibilityLabel={t("forceRelease.reasonAccessibility")}
                onChangeText={setForceReleaseReason}
                placeholder={t("forceRelease.reasonPlaceholder")}
                style={styles.forceReleaseInput}
                testID="held-orders-force-release-reason"
                value={forceReleaseReason}
              />
              <View style={styles.panelActions}>
                <HandheldActionButton
                  label={t("action.back")}
                  onPress={() => {
                    setForceReleaseFor(null);
                    setForceReleaseReason("");
                  }}
                  testID="held-orders-force-release-cancel"
                  variant="secondary"
                />
                <HandheldActionButton
                  disabled={!forceReleaseReason.trim() || state.busy}
                  label={t("forceRelease.confirm")}
                  onPress={() => {
                    const holdId = forceReleaseFor;
                    const reason = forceReleaseReason.trim();
                    setForceReleaseFor(null);
                    setForceReleaseReason("");
                    void presenter.forceRelease(holdId, reason);
                  }}
                  testID="held-orders-force-release-confirm"
                  variant="danger"
                />
              </View>
            </PosKeyboardAwareScrollView>
          ) : null}
          {deleteFor ? (
            <View style={styles.deletePanel} testID="held-orders-delete-panel">
              <Text style={styles.panelTitle}>{t("delete.title")}</Text>
              <Text style={styles.deleteHint}>{t("delete.hint")}</Text>
              <View style={styles.panelActions}>
                <HandheldActionButton
                  label={t("delete.cancel")}
                  onPress={() => setDeleteFor(null)}
                  testID="held-orders-delete-cancel"
                  variant="secondary"
                />
                <HandheldActionButton
                  disabled={state.busy}
                  label={t("delete.confirm")}
                  onPress={() => {
                    const holdId = deleteFor;
                    setDeleteFor(null);
                    void presenter.delete(holdId);
                  }}
                  testID="held-orders-delete-confirm"
                  variant="danger"
                />
              </View>
            </View>
          ) : null}
          {state.kind === "ready" && !state.rows.length ? (
            <CenteredState
              hint={t(
                state.sharedEnabled && sourceTab === "other"
                  ? "empty.other.hint"
                  : "empty.hint",
              )}
              message={t(
                state.sharedEnabled && sourceTab === "other"
                  ? "empty.other.title"
                  : "empty.title",
              )}
              testID="held-orders-empty"
            />
          ) : null}
          {state.kind === "ready" ||
          (state.kind === "loading" && state.rows.length > 0) ? (
            <FlatList
              contentContainerStyle={styles.list}
              data={state.rows}
              keyExtractor={(item) => item.holdId}
              renderItem={({ item }) => (
                <HeldOrderRow
                  busy={state.busy}
                  locale={locale}
                  forceReleaseSupported={presenter.supportsForceRelease?.() ?? false}
                  localSourceSelected={sourceTab === "local"}
                  onShare={() => void presenter.requestShare(item.holdId)}
                  shareBusy={shareBusyHoldIds.includes(item.holdId)}
                  onDelete={(holdId) => {
                    setDeleteFor(holdId);
                    setForceReleaseFor(null);
                    setForceReleaseReason("");
                  }}
                  onForceRelease={(holdId) => {
                    setForceReleaseFor(holdId);
                    setForceReleaseReason("");
                    setDeleteFor(null);
                  }}
                  onRecall={() =>
                    void recallRow(presenter, item, onBack)
                  }
                  onRecover={() =>
                    void recoverRow(presenter, item, onBack)
                  }
                  onRelease={() => void presenter.release(item.holdId)}
                  onViewDetails={() => setSelectedHoldId(item.holdId)}
                  row={item}
                />
              )}
              testID="held-orders-list"
            />
          ) : null}
        </HandheldStateSurface>
      )}
    </HandheldScreenFrame>
  );
}

function HeldOrderDetail({
  busy,
  locale,
  onRecall,
  onRecover,
  onRelease,
  row,
  t,
}: Readonly<{
  busy: boolean;
  locale: ReturnType<typeof resolveHeldOrdersLocale>;
  onRecall(): void;
  onRecover(): void;
  onRelease(): void;
  row: HeldOrderViewRow;
  t(
    key: HeldOrdersCopyKey,
    values?: Readonly<Record<string, string | number>>,
  ): string;
}>) {
  const local = row.local;
  const remote = row.remote;
  const recalling =
    row.status === "claiming-here" ||
    String(row.status) === "Recalling" ||
    row.local?.status === "Recalling";
  const itemCount = local?.itemCount ?? remote?.lineCount ?? 0;
  const amountCents = local?.actualAmountCents ?? remote?.actualCents ?? 0;
  const heldAtIso = local?.heldAtIso ?? remote?.heldAtIso ?? "";
  const title = local
    ? t("list.sequence", { sequence: local.localSequence })
    : remote
      ? t("remote.source", {
          device: remote.deviceCode,
          cashier: remote.cashierName,
        })
      : "—";
  return (
    <HandheldStateSurface slug="held-order-detail" style={styles.singleColumn}>
      <HandheldSection
        action={
          <HandheldStatusBadge
            label={t(statusCopyKey(row.status))}
            tone={recalling ? "warning" : remote ? "info" : "success"}
          />
        }
        title={title}
      >
        <Text style={styles.detailHint}>{t("detail.hint")}</Text>
        <DetailRow
          label={t("list.items", { count: itemCount })}
          value={t("list.heldAt", {
            time: formatTime(heldAtIso, locale),
          })}
        />
        <DetailRow
          label={t("list.amount")}
          value={formatAud(amountCents, locale)}
        />
        {row.blockReason ? (
          <Text style={styles.blockReason}>
            {t(blockReasonCopyKey(row.blockReason))}
          </Text>
        ) : null}
      </HandheldSection>
      <View style={styles.detailActions}>
        <HandheldActionButton
          disabled={busy}
          label={t(recalling ? "action.recover" : remote && !local ? "action.take-remote" : "action.recall")}
          onPress={recalling ? onRecover : onRecall}
          testID={`held-order-action-${row.holdId}`}
          variant={recalling ? "secondary" : "primary"}
        />
        {recalling ? (
          <HandheldActionButton
            disabled={busy}
            label={t("action.release")}
            onPress={onRelease}
            testID={`held-order-release-${row.holdId}`}
            variant="secondary"
          />
        ) : null}
      </View>
    </HandheldStateSurface>
  );
}

function HeldOrderRow({
  busy,
  forceReleaseSupported,
  localSourceSelected,
  locale,
  onDelete,
  onForceRelease,
  onRecall,
  onRecover,
  onRelease,
  onShare,
  onViewDetails,
  row,
  shareBusy,
}: Readonly<{
  busy: boolean;
  forceReleaseSupported: boolean;
  locale: ReturnType<typeof resolveHeldOrdersLocale>;
  localSourceSelected: boolean;
  onDelete(holdId: string): void;
  onForceRelease(holdId: string): void;
  onRecall(): void;
  onRecover(): void;
  onRelease(): void;
  onShare(): void;
  onViewDetails(): void;
  row: HeldOrderViewRow;
  shareBusy: boolean;
}>) {
  const t = (
    key: HeldOrdersCopyKey,
    values?: Readonly<Record<string, string | number>>,
  ) => heldOrdersText(locale, key, values);
  const local = row.local;
  const remote = row.remote;
  const recalling =
    row.status === "claiming-here" ||
    String(row.status) === "Recalling" ||
    row.local?.status === "Recalling";
  const remoteOnly = local === null && remote !== null;
  const canDelete = local !== null && !recalling;
  const canRecall =
    row.status !== "blocked" || row.blockReason !== "LOCAL_DELETE_PENDING";
  const canShare =
    localSourceSelected &&
    local?.status === "Pending" &&
    row.isSyntheticSharedClaim !== true &&
    row.shareState === "NeedsEvaluation" &&
    row.shareRequestedAtIso === null;
  const itemCount = local?.itemCount ?? remote?.lineCount ?? 0;
  const amountCents = local?.actualAmountCents ?? remote?.actualCents ?? 0;
  const heldAtIso = local?.heldAtIso ?? remote?.heldAtIso ?? "";
  return (
    <View style={styles.row} testID={`held-order-row-${row.holdId}`}>
      <View style={styles.rowIdentity}>
        <Text style={styles.sequence}>
          {local
            ? t("list.sequence", { sequence: local.localSequence })
            : remote
              ? t("remote.source", {
                  device: remote.deviceCode,
                  cashier: remote.cashierName,
                })
              : "—"}
        </Text>
        <Text
          style={[
            styles.status,
            recalling || row.status === "blocked" ? styles.statusWarning : null,
            remoteOnly ? styles.statusRemote : null,
          ]}
        >
          {t(statusCopyKey(row.status))}
        </Text>
        {local && remote ? (
          <Text style={styles.itemCount}>
            {t("remote.source", {
              device: remote.deviceCode,
              cashier: remote.cashierName,
            })}
          </Text>
        ) : null}
        <Text style={styles.itemCount}>
          {t("list.items", { count: itemCount })}
        </Text>
        <Text style={styles.time}>
          {t("list.heldAt", { time: formatTime(heldAtIso, locale) })}
        </Text>
      </View>
      <View style={styles.amountRow}>
        <Text style={styles.amountLabel}>{t("list.amount")}</Text>
        <Text style={styles.amount}>
          {formatAud(amountCents, locale)}
        </Text>
      </View>
      {row.status === "blocked" ? (
        <Text
          style={styles.blockReason}
          testID={`held-order-blocked-reason-${row.holdId}`}
        >
          {t(blockReasonCopyKey(row.blockReason))}
        </Text>
      ) : null}
      <View style={styles.rowActions}>
        {canShare ? (
          <HandheldActionButton
            disabled={shareBusy || busy}
            label={t("action.share")}
            onPress={onShare}
            testID={`held-order-share-${row.holdId}`}
            variant="secondary"
          />
        ) : null}
        {canRecall ? (
          <HandheldActionButton
            disabled={busy}
            label={t(
              recalling
                ? "action.recover"
                : remoteOnly
                  ? "action.take-remote"
                  : "action.recall",
            )}
            onPress={recalling ? onRecover : onRecall}
            testID={`held-order-action-${row.holdId}`}
            variant={recalling ? "secondary" : "primary"}
          />
        ) : null}
        {recalling ? (
          <>
            <HandheldActionButton
              disabled={busy}
              label={t("action.release")}
              onPress={onRelease}
              testID={`held-order-release-${row.holdId}`}
              variant="secondary"
            />
            {forceReleaseSupported ? (
              <HandheldActionButton
                disabled={busy}
                label={t("action.force-release")}
                onPress={() => onForceRelease(row.holdId)}
                testID={`held-order-force-release-${row.holdId}`}
                variant="secondary"
              />
            ) : null}
          </>
        ) : null}
        {canDelete ? (
          <HandheldActionButton
            disabled={busy}
            label={t("action.delete")}
            onPress={() => onDelete(row.holdId)}
            testID={`held-order-delete-${row.holdId}`}
            variant="danger"
          />
        ) : null}
        <HandheldActionButton
          label={t("action.details")}
          onPress={onViewDetails}
          sound="navigate"
          testID={`held-order-view-${row.holdId}`}
          variant="secondary"
        />
      </View>
    </View>
  );
}

async function recallRow(
  presenter: HeldOrdersPresenter,
  row: HeldOrderViewRow,
  onBack?: () => void,
): Promise<void> {
  const result =
    row.status === "remote-pending"
      ? await presenter.takeRemote(row.holdId)
      : row.status === "published-shareable" ||
          row.status === "local-pending-publish"
        ? await presenter.recallLocalShared(row.holdId)
        : await presenter.recall(row.holdId);
  if (
    result.ok &&
    (result.code === "recalled" || result.code === "recovered")
  ) {
    onBack?.();
  }
}

async function recoverRow(
  presenter: HeldOrdersPresenter,
  row: HeldOrderViewRow,
  onBack?: () => void,
): Promise<void> {
  const result = await presenter.recover(row.holdId);
  if (
    result.ok &&
    (result.code === "recalled" || result.code === "recovered")
  ) {
    onBack?.();
  }
}

function statusCopyKey(status: HeldOrderViewStatus | string): HeldOrdersCopyKey {
  switch (status) {
    case "claiming-here":
      return "status.claiming-here";
    case "local-pending-publish":
      return "status.local-pending-publish";
    case "published-shareable":
      return "status.published-shareable";
    case "remote-pending":
      return "status.remote-pending";
    case "blocked":
      return "status.blocked";
    case "Recalling":
      return "status.Recalling";
    case "local-pending":
    case "Pending":
    default:
      return "status.local-pending";
  }
}

function blockReasonCopyKey(reason: string | null): HeldOrdersCopyKey {
  switch (reason) {
    case "LEGACY_PAYLOAD_CORRUPTED":
      return "blocked.LEGACY_PAYLOAD_CORRUPTED";
    case "LEGACY_PAYLOAD_VERSION_UNSUPPORTED":
      return "blocked.LEGACY_PAYLOAD_VERSION_UNSUPPORTED";
    case "SHARED_CART_VERSION_UNSUPPORTED":
      return "blocked.SHARED_CART_VERSION_UNSUPPORTED";
    case "SHARED_CART_MODE_NOT_SALE":
      return "blocked.SHARED_CART_MODE_NOT_SALE";
    case "SHARED_CART_LINE_KIND_NOT_SALE":
      return "blocked.SHARED_CART_LINE_KIND_NOT_SALE";
    case "SHARED_CART_RETURN_ORIGINAL_NOT_EMPTY":
      return "blocked.SHARED_CART_RETURN_ORIGINAL_NOT_EMPTY";
    case "SHARED_CART_INVALID":
      return "blocked.SHARED_CART_INVALID";
    case "LOCAL_DELETE_PENDING":
      return "blocked.LOCAL_DELETE_PENDING";
    case "SHARED_HELD_ORDER_CANCELLED":
      return "blocked.SHARED_HELD_ORDER_CANCELLED";
    default:
      return "blocked.unknown";
  }
}

function FilterButton({
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
    <HandheldActionButton
      label={label}
      onPress={onPress}
      testID={testID}
      variant={selected ? "primary" : "secondary"}
    />
  );
}

function DetailRow({
  label,
  value,
}: Readonly<{ label: string; value: string }>) {
  return (
    <View style={styles.detailRow}>
      <Text style={styles.detailLabel}>{label}</Text>
      <Text style={styles.detailValue}>{value}</Text>
    </View>
  );
}

function CenteredState({
  actionLabel,
  hint,
  message,
  onAction,
  testID,
}: Readonly<{
  actionLabel?: string;
  hint?: string;
  message: string;
  onAction?(): void;
  testID: string;
}>) {
  return (
    <View style={styles.centered} testID={testID}>
      <Text style={styles.centeredTitle}>{message}</Text>
      {hint ? <Text style={styles.centeredHint}>{hint}</Text> : null}
      {actionLabel && onAction ? (
        <HandheldActionButton
          label={actionLabel}
          onPress={onAction}
          testID={`${testID}-action`}
        />
      ) : null}
    </View>
  );
}

function formatAud(
  cents: number,
  locale: ReturnType<typeof resolveHeldOrdersLocale>,
): string {
  return new Intl.NumberFormat(locale === "zh" ? "zh-CN" : "en-AU", {
    style: "currency",
    currency: "AUD",
  }).format(cents / 100);
}

function formatTime(
  iso: string,
  locale: ReturnType<typeof resolveHeldOrdersLocale>,
): string {
  const timestamp = Date.parse(iso);
  if (!Number.isFinite(timestamp)) return "—";
  return new Intl.DateTimeFormat(locale === "zh" ? "zh-CN" : "en-AU", {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(new Date(timestamp));
}

const styles = StyleSheet.create({
  amount: { color: posColors.ink, fontSize: 22, fontWeight: "800" },
  amountLabel: { color: posColors.mutedInk, fontSize: 13 },
  amountRow: {
    alignItems: "center",
    flexDirection: "row",
    justifyContent: "space-between",
  },
  centered: {
    alignItems: "center",
    flex: 1,
    justifyContent: "center",
    padding: 32,
  },
  centeredHint: {
    color: posColors.mutedInk,
    fontSize: 15,
    marginBottom: 20,
    marginTop: 8,
    textAlign: "center",
  },
  centeredTitle: {
    color: posColors.ink,
    fontSize: 20,
    fontWeight: "800",
    textAlign: "center",
  },
  detailActions: { flexDirection: "column", gap: 8 },
  detailHint: { color: posColors.mutedInk, fontSize: 14, lineHeight: 20 },
  detailLabel: { color: posColors.mutedInk, fontSize: 13 },
  detailRow: {
    alignItems: "center",
    borderTopColor: posColors.border,
    borderTopWidth: 1,
    flexDirection: "row",
    justifyContent: "space-between",
    minHeight: HELD_ORDERS_MIN_TOUCH_TARGET,
  },
  detailValue: {
    color: posColors.ink,
    flexShrink: 1,
    fontSize: 14,
    fontWeight: "800",
    textAlign: "right",
  },
  blockReason: { color: posColors.red, fontSize: 13, fontWeight: "700" },
  deleteHint: { color: posColors.mutedInk, flex: 1, fontSize: 14, lineHeight: 20 },
  deletePanel: {
    backgroundColor: posColors.redSoft,
    borderColor: posColors.red,
    borderRadius: 6,
    borderWidth: 1,
    gap: 8,
    padding: 12,
  },
  filterGroup: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 6,
  },
  itemCount: { color: posColors.mutedInk, fontSize: 14 },
  list: { gap: 8, paddingBottom: 16 },
  listHeader: {
    alignItems: "center",
    flexDirection: "row",
    justifyContent: "space-between",
  },
  listHeaderActions: {
    alignItems: "center",
    flexDirection: "row",
    flexShrink: 1,
    flexWrap: "wrap",
    gap: 6,
    justifyContent: "flex-end",
  },
  listTitle: { color: posColors.ink, fontSize: 18, fontWeight: "800" },
  notice: { borderRadius: 6, fontSize: 14, fontWeight: "700", padding: 12 },
  noticeDanger: { backgroundColor: posColors.redSoft, color: posColors.red },
  noticeSuccess: {
    backgroundColor: posColors.greenSoft,
    color: posColors.green,
  },
  panelActions: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
    justifyContent: "flex-end",
  },
  panelTitle: { color: posColors.ink, fontSize: 16, fontWeight: "800" },
  forceReleaseInput: {
    backgroundColor: posColors.surface,
    borderColor: posColors.border,
    borderRadius: 6,
    borderWidth: 1,
    color: posColors.ink,
    minHeight: HELD_ORDERS_MIN_TOUCH_TARGET,
    paddingHorizontal: 10,
    paddingVertical: 8,
  },
  forceReleasePanel: {
    backgroundColor: posColors.surface,
    borderColor: posColors.red,
    borderRadius: 6,
    borderWidth: 1,
    flexGrow: 0,
    maxHeight: 240,
  },
  forceReleasePanelContent: {
    gap: 8,
    padding: 12,
  },
  row: {
    backgroundColor: posColors.surface,
    borderColor: posColors.border,
    borderRadius: 6,
    borderWidth: 1,
    flexDirection: "column",
    gap: 8,
    minHeight: 106,
    padding: 16,
  },
  rowActions: { flexDirection: "column", gap: 6 },
  rowIdentity: { gap: 4 },
  sequence: { color: posColors.ink, fontSize: 16, fontWeight: "800" },
  singleColumn: {
    flex: 1,
    flexDirection: "column",
    gap: 8,
    minHeight: 0,
  },
  status: { color: posColors.green, fontSize: 14, fontWeight: "700" },
  statusRemote: { color: posColors.blue },
  statusWarning: { color: posColors.orange },
  syncNotice: {
    backgroundColor: posColors.blueSoft,
    borderRadius: 6,
    color: posColors.blue,
    fontSize: 13,
    fontWeight: "700",
    padding: 10,
  },
  time: { color: posColors.mutedInk, fontSize: 13 },
});
