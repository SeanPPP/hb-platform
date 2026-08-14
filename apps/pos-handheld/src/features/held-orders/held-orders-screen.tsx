import { useEffect, useState, useSyncExternalStore } from "react";
import { useTranslation } from "react-i18next";
import {
  ActivityIndicator,
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
import { HeldOrdersPresenter } from "./held-orders-presenter";

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
  const [selectedHoldId, setSelectedHoldId] = useState<string | null>(null);
  const t = (
    key: HeldOrdersCopyKey,
    values?: Readonly<Record<string, string | number>>,
  ) => heldOrdersText(locale, key, values);

  useEffect(() => {
    void presenter.refresh();
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
          onRecall={() => void presenter.recall(selectedRow.holdId)}
          onRecover={() => void presenter.recover(selectedRow.holdId)}
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
            {state.busy || state.kind === "loading" ? (
              <ActivityIndicator
                color={posColors.orange}
                testID="held-orders-loading-indicator"
              />
            ) : null}
          </View>

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
          {state.kind === "ready" && !state.rows.length ? (
            <CenteredState
              hint={t("empty.hint")}
              message={t("empty.title")}
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
                  locale={locale}
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
  row: import("@/core/contracts").HeldOrderSummary;
  t(
    key: HeldOrdersCopyKey,
    values?: Readonly<Record<string, string | number>>,
  ): string;
}>) {
  const recalling = row.status === "Recalling";
  return (
    <HandheldStateSurface slug="held-order-detail" style={styles.singleColumn}>
      <HandheldSection
        action={
          <HandheldStatusBadge
            label={t(recalling ? "status.Recalling" : "status.Pending")}
            tone={recalling ? "warning" : "success"}
          />
        }
        title={t("list.sequence", { sequence: row.localSequence })}
      >
        <Text style={styles.detailHint}>{t("detail.hint")}</Text>
        <DetailRow
          label={t("list.items", { count: row.itemCount })}
          value={t("list.heldAt", {
            time: formatTime(row.heldAtIso, locale),
          })}
        />
        <DetailRow
          label={t("list.amount")}
          value={formatAud(row.actualAmountCents, locale)}
        />
      </HandheldSection>
      <View style={styles.detailActions}>
        <HandheldActionButton
          disabled={busy}
          label={t(recalling ? "action.recover" : "action.recall")}
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
  locale,
  onViewDetails,
  row,
}: Readonly<{
  locale: ReturnType<typeof resolveHeldOrdersLocale>;
  onViewDetails(): void;
  row: import("@/core/contracts").HeldOrderSummary;
}>) {
  const t = (
    key: HeldOrdersCopyKey,
    values?: Readonly<Record<string, string | number>>,
  ) => heldOrdersText(locale, key, values);
  const recalling = row.status === "Recalling";
  return (
    <View style={styles.row} testID={`held-order-row-${row.holdId}`}>
      <View style={styles.rowIdentity}>
        <Text style={styles.sequence}>
          {t("list.sequence", { sequence: row.localSequence })}
        </Text>
        <Text style={[styles.status, recalling && styles.statusWarning]}>
          {t(recalling ? "status.Recalling" : "status.Pending")}
        </Text>
        <Text style={styles.itemCount}>
          {t("list.items", { count: row.itemCount })}
        </Text>
        <Text style={styles.time}>
          {t("list.heldAt", { time: formatTime(row.heldAtIso, locale) })}
        </Text>
      </View>
      <View style={styles.amountRow}>
        <Text style={styles.amountLabel}>{t("list.amount")}</Text>
        <Text style={styles.amount}>
          {formatAud(row.actualAmountCents, locale)}
        </Text>
      </View>
      <HandheldActionButton
        label={t("action.details")}
        onPress={onViewDetails}
        sound="navigate"
        testID={`held-order-view-${row.holdId}`}
        variant="secondary"
      />
    </View>
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
  itemCount: { color: posColors.mutedInk, fontSize: 14 },
  list: { gap: 8, paddingBottom: 16 },
  listHeader: {
    alignItems: "center",
    flexDirection: "row",
    justifyContent: "space-between",
  },
  listTitle: { color: posColors.ink, fontSize: 18, fontWeight: "800" },
  notice: { borderRadius: 6, fontSize: 14, fontWeight: "700", padding: 12 },
  noticeDanger: { backgroundColor: posColors.redSoft, color: posColors.red },
  noticeSuccess: {
    backgroundColor: posColors.greenSoft,
    color: posColors.green,
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
  rowIdentity: { gap: 4 },
  sequence: { color: posColors.ink, fontSize: 16, fontWeight: "800" },
  singleColumn: {
    flex: 1,
    flexDirection: "column",
    gap: 8,
    minHeight: 0,
  },
  status: { color: posColors.green, fontSize: 14, fontWeight: "700" },
  statusWarning: { color: posColors.orange },
  time: { color: posColors.mutedInk, fontSize: 13 },
});
