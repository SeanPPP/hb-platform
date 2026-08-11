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
import { SafeAreaView } from "react-native-safe-area-context";

import {
  heldOrdersText,
  resolveHeldOrdersLocale,
  type HeldOrdersCopyKey,
} from "./held-orders-copy";
import {
  HeldOrdersPresenter,
  type HeldOrderViewStatus,
} from "./held-orders-presenter";

import {
  PosKeyboardAwareScrollView,
  PosKeyboardAwareTextInput,
} from "@/ui/controls/pos-keyboard-aware-scroll-view";
import { PosPressable } from "@/ui/controls/pos-pressable";
import { posColors } from "@/ui/theme";

export const HELD_ORDERS_MIN_TOUCH_TARGET = 44;
export const HELD_ORDERS_AUTO_REFRESH_INTERVAL_MS = 10_000;

type HeldOrdersScreenProps = Readonly<{
  presenter: HeldOrdersPresenter;
  onBack?(): void;
}>;

export function HeldOrdersScreen({
  presenter,
  onBack,
}: HeldOrdersScreenProps) {
  const state = useSyncExternalStore(
    presenter.subscribe,
    presenter.getState,
    presenter.getState,
  );
  const { i18n } = useTranslation();
  const locale = resolveHeldOrdersLocale(i18n.resolvedLanguage ?? i18n.language);
  const t = (
    key: HeldOrdersCopyKey,
    values?: Readonly<Record<string, string | number>>,
  ) => heldOrdersText(locale, key, values);

  useEffect(() => {
    void presenter.refresh();
    presenter.startAutoRefresh(HELD_ORDERS_AUTO_REFRESH_INTERVAL_MS);
    const subscription = AppState.addEventListener("change", (next) => {
      if (next === "active") {
        presenter.startAutoRefresh(HELD_ORDERS_AUTO_REFRESH_INTERVAL_MS);
      } else {
        presenter.stopAutoRefresh();
      }
    });
    return () => {
      subscription.remove();
      presenter.stopAutoRefresh();
    };
  }, [presenter]);

  const [forceReleaseFor, setForceReleaseFor] = useState<string | null>(null);
  const [forceReleaseReason, setForceReleaseReason] = useState("");

  const returnToSalesAfterRestore = async (
    action: () => ReturnType<HeldOrdersPresenter["recall"]>,
  ) => {
    const result = await action();
    if (
      result.ok &&
      (result.code === "recalled" || result.code === "recovered")
    ) {
      onBack?.();
    }
  };

  return (
    <SafeAreaView style={styles.safeArea} testID="held-orders-screen">
      <View style={styles.header}>
        <View style={styles.identity}>
          <Text style={styles.title}>{t("title")}</Text>
          <Text style={styles.subtitle}>
            {t(state.sharedEnabled ? "subtitle.shared" : "subtitle")}
          </Text>
        </View>
        <View style={styles.headerActions}>
          {onBack ? (
            <ActionButton
              label={t("action.back")}
              onPress={onBack}
              sound="navigate"
              testID="held-orders-back"
              tone="quiet"
            />
          ) : null}
          <ActionButton
            disabled={state.busy}
            label={t("action.hold")}
            onPress={() => void presenter.hold()}
            testID="held-orders-hold"
          />
          <ActionButton
            disabled={state.busy || state.kind === "loading"}
            label={t("action.refresh")}
            onPress={() => void presenter.refresh()}
            testID="held-orders-refresh"
            tone="secondary"
          />
        </View>
      </View>

      <View style={styles.workspace}>
        <View style={styles.listHeader}>
          <Text style={styles.listTitle}>{t("list.title")}</Text>
          {state.busy || state.kind === "loading" ? (
            <ActivityIndicator color={posColors.orange} testID="held-orders-loading-indicator" />
          ) : null}
        </View>

        {state.refreshError ? (
          <View style={styles.syncNotice} testID="held-orders-refresh-error">
            <Text style={styles.syncNoticeText}>{t("error.shared-sync")}</Text>
          </View>
        ) : null}

        {state.lastAction ? (
          <Text
            style={[
              styles.notice,
              state.lastAction.ok ? styles.noticeSuccess : styles.noticeDanger,
            ]}
            testID="held-orders-result"
          >
            {t(`result.${state.lastAction.code}`)}
          </Text>
        ) : null}

        {state.kind === "loading" && !state.rows.length ? (
          <CenteredState message={t("loading")} testID="held-orders-loading" />
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
            showsVerticalScrollIndicator={false}
            style={styles.forceReleasePanel}
            testID="held-orders-force-release-panel"
          >
            <Text style={styles.forceReleaseTitle}>{t("forceRelease.title")}</Text>
            <PosKeyboardAwareTextInput
              accessibilityLabel={t("forceRelease.reasonAccessibility")}
              onChangeText={setForceReleaseReason}
              placeholder={t("forceRelease.reasonPlaceholder")}
              style={styles.forceReleaseInput}
              testID="held-orders-force-release-reason"
              value={forceReleaseReason}
            />
            <View style={styles.forceReleaseActions}>
              <ActionButton
                label={t("action.back")}
                onPress={() => {
                  setForceReleaseFor(null);
                  setForceReleaseReason("");
                }}
                testID="held-orders-force-release-cancel"
                tone="quiet"
              />
              <ActionButton
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
                tone="danger"
              />
            </View>
          </PosKeyboardAwareScrollView>
        ) : null}
        {state.kind === "ready" && !state.rows.length ? (
          <CenteredState
            hint={t(state.sharedEnabled ? "empty.hint.shared" : "empty.hint")}
            message={t("empty.title")}
            testID="held-orders-empty"
          />
        ) : null}
        {(state.kind === "ready" ||
          (state.kind === "loading" && state.rows.length > 0)) ? (
          <FlatList
            contentContainerStyle={styles.list}
            data={state.rows}
            keyExtractor={(item) => item.holdId}
            renderItem={({ item }) => (
              <HeldOrderRow
                busy={state.busy}
                forceReleaseSupported={presenter.supportsForceRelease()}
                locale={locale}
                onForceRelease={(holdId) => {
                  setForceReleaseFor(holdId);
                  setForceReleaseReason("");
                }}
                onRecall={() =>
                  void returnToSalesAfterRestore(() =>
                    item.status === "published-shareable" ||
                    item.status === "local-pending-publish"
                      ? presenter.recallLocalShared(item.holdId)
                      : presenter.recall(item.holdId),
                  )
                }
                onRecover={() =>
                  void returnToSalesAfterRestore(() =>
                    presenter.recover(item.holdId),
                  )
                }
                onRelease={() => void presenter.release(item.holdId)}
                onTakeRemote={() =>
                  void returnToSalesAfterRestore(() =>
                    presenter.takeRemote(item.holdId),
                  )
                }
                row={item}
              />
            )}
            testID="held-orders-list"
          />
        ) : null}
      </View>
    </SafeAreaView>
  );
}

function HeldOrderRow({
  busy,
  forceReleaseSupported,
  locale,
  onForceRelease,
  onRecall,
  onRecover,
  onRelease,
  onTakeRemote,
  row,
}: Readonly<{
  busy: boolean;
  forceReleaseSupported: boolean;
  locale: ReturnType<typeof resolveHeldOrdersLocale>;
  onForceRelease(holdId: string): void;
  onRecall(): void;
  onRecover(): void;
  onRelease(): void;
  onTakeRemote(): void;
  row: import("./held-orders-presenter").HeldOrderViewRow;
}>) {
  const t = (
    key: HeldOrdersCopyKey,
    values?: Readonly<Record<string, string | number>>,
  ) => heldOrdersText(locale, key, values);
  const claiming = row.status === "claiming-here";
  const remoteOnly = row.status === "remote-pending";
  const itemCount = row.local?.itemCount ?? row.remote?.lineCount ?? 0;
  const amountCents = row.local?.actualAmountCents ?? row.remote?.actualCents ?? 0;
  const heldAtIso = row.local?.heldAtIso ?? row.remote?.heldAtIso ?? "";
  const statusKeyMap: Record<HeldOrderViewStatus, HeldOrdersCopyKey> = {
    "local-pending": "status.local-pending",
    "claiming-here": "status.claiming-here",
    "local-pending-publish": "status.local-pending-publish",
    "published-shareable": "status.published-shareable",
    "remote-pending": "status.remote-pending",
    blocked: "status.blocked",
  };
  const statusKey = statusKeyMap[row.status];
  return (
    <View style={styles.row} testID={`held-order-row-${row.holdId}`}>
      <View style={styles.rowIdentity}>
        <Text style={styles.sequence}>
          {row.local
            ? t("list.sequence", { sequence: row.local.localSequence })
            : row.remote
              ? t("remote.source", {
                  device: row.remote.deviceCode,
                  cashier: row.remote.cashierName,
                })
              : "—"}
        </Text>
        <Text
          style={[
            styles.status,
            (claiming || row.status === "blocked") && styles.statusWarning,
            row.status === "remote-pending" && styles.statusRemote,
          ]}
        >
          {t(statusKey)}
        </Text>
        {row.remote && row.local ? (
          <Text style={styles.itemCount}>
            {t("remote.source", {
              device: row.remote.deviceCode,
              cashier: row.remote.cashierName,
            })}
          </Text>
        ) : null}
        <Text style={styles.itemCount}>{t("list.items", { count: itemCount })}</Text>
        <Text style={styles.time}>
          {t("list.heldAt", { time: formatTime(heldAtIso, locale) })}
        </Text>
        {row.status === "blocked" ? (
          <Text style={styles.blockReason} testID={`held-order-blocked-reason-${row.holdId}`}>
            {t(blockReasonCopyKey(row.blockReason))}
          </Text>
        ) : null}
      </View>
      <View style={styles.amountColumn}>
        <Text style={styles.amountLabel}>{t("list.amount")}</Text>
        <Text style={styles.amount}>{formatAud(amountCents, locale)}</Text>
      </View>
      <View style={styles.rowActions}>
        <ActionButton
          disabled={busy}
          label={t(
            claiming
              ? "action.recover"
              : remoteOnly
                ? "action.take-remote"
                : "action.recall",
          )}
          onPress={claiming ? onRecover : remoteOnly ? onTakeRemote : onRecall}
          testID={`held-order-action-${row.holdId}`}
          tone={claiming ? "secondary" : "primary"}
        />
        {claiming ? (
          <>
            <ActionButton
              disabled={busy}
              label={t("action.release")}
              onPress={onRelease}
              testID={`held-order-release-${row.holdId}`}
              tone="quiet"
            />
            {forceReleaseSupported ? (
              <ActionButton
                disabled={busy}
                label={t("action.force-release")}
                onPress={() => onForceRelease(row.holdId)}
                testID={`held-order-force-release-${row.holdId}`}
                tone="quiet"
              />
            ) : null}
          </>
        ) : null}
      </View>
    </View>
  );
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
    default:
      return "blocked.unknown";
  }
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
        <ActionButton label={actionLabel} onPress={onAction} testID={`${testID}-action`} />
      ) : null}
    </View>
  );
}

function ActionButton({
  disabled = false,
  label,
  onPress,
  sound = "tap",
  testID,
  tone = "primary",
}: Readonly<{
  disabled?: boolean;
  label: string;
  onPress(): void;
  sound?: "tap" | "navigate";
  testID: string;
  tone?: "primary" | "secondary" | "quiet" | "danger";
}>) {
  return (
    <PosPressable
      accessibilityRole="button"
      accessibilityState={{ disabled }}
      disabled={disabled}
      onPress={onPress}
      sound={sound}
      style={({ pressed }) => [
        styles.button,
        tone === "primary"
          ? styles.buttonPrimary
          : tone === "secondary"
            ? styles.buttonSecondary
            : tone === "danger"
              ? styles.buttonDanger
              : styles.buttonQuiet,
        disabled && styles.buttonDisabled,
        pressed && !disabled && styles.pressed,
      ]}
      testID={testID}
    >
      <Text style={[styles.buttonText, tone !== "primary" && styles.buttonTextDark]}>{label}</Text>
    </PosPressable>
  );
}

function formatAud(cents: number, locale: ReturnType<typeof resolveHeldOrdersLocale>): string {
  return new Intl.NumberFormat(locale === "zh" ? "zh-CN" : "en-AU", {
    style: "currency",
    currency: "AUD",
  }).format(cents / 100);
}

function formatTime(iso: string, locale: ReturnType<typeof resolveHeldOrdersLocale>): string {
  const timestamp = Date.parse(iso);
  if (!Number.isFinite(timestamp)) return "—";
  return new Intl.DateTimeFormat(locale === "zh" ? "zh-CN" : "en-AU", {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(new Date(timestamp));
}

const styles = StyleSheet.create({
  safeArea: { flex: 1, backgroundColor: posColors.canvas },
  header: { alignItems: "center", borderBottomColor: posColors.border, borderBottomWidth: 1, flexDirection: "row", gap: 20, minHeight: 96, paddingHorizontal: 28, paddingVertical: 16 },
  identity: { flex: 1, gap: 4 },
  title: { color: posColors.ink, fontSize: 27, fontWeight: "800" },
  subtitle: { color: posColors.mutedInk, fontSize: 15 },
  headerActions: { flexDirection: "row", flexWrap: "wrap", gap: 10, justifyContent: "flex-end" },
  workspace: { flex: 1, padding: 28 },
  listHeader: { alignItems: "center", flexDirection: "row", justifyContent: "space-between", marginBottom: 16 },
  listTitle: { color: posColors.ink, fontSize: 20, fontWeight: "800" },
  notice: { borderRadius: 4, fontSize: 15, fontWeight: "700", marginBottom: 14, padding: 12 },
  noticeSuccess: { backgroundColor: posColors.greenSoft, color: posColors.green },
  noticeDanger: { backgroundColor: posColors.redSoft, color: posColors.red },
  syncNotice: { backgroundColor: posColors.blueSoft, borderRadius: 4, marginBottom: 14, padding: 12 },
  syncNoticeText: { color: posColors.blue, fontSize: 14, fontWeight: "700" },
  forceReleasePanel: { backgroundColor: posColors.surface, borderColor: posColors.red, borderRadius: 4, borderWidth: 1, marginBottom: 16 },
  forceReleasePanelContent: { gap: 12, padding: 16 },
  forceReleaseTitle: { color: posColors.ink, fontSize: 17, fontWeight: "800" },
  forceReleaseInput: { borderColor: posColors.border, borderRadius: 4, borderWidth: 1, color: posColors.ink, fontSize: 15, minHeight: HELD_ORDERS_MIN_TOUCH_TARGET, paddingHorizontal: 12, paddingVertical: 10 },
  forceReleaseActions: { alignItems: "center", flexDirection: "row", gap: 10, justifyContent: "flex-end" },
  list: { gap: 10, paddingBottom: 28 },
  row: { alignItems: "center", backgroundColor: posColors.surface, borderColor: posColors.border, borderRadius: 4, borderWidth: 1, flexDirection: "row", gap: 20, minHeight: 106, padding: 18 },
  rowIdentity: { flex: 1, gap: 4 },
  rowActions: { alignItems: "stretch", gap: 8 },
  sequence: { color: posColors.ink, fontSize: 16, fontWeight: "800" },
  status: { color: posColors.green, fontSize: 14, fontWeight: "700" },
  statusWarning: { color: posColors.orange },
  statusRemote: { color: posColors.blue },
  blockReason: { color: posColors.red, fontSize: 13, fontWeight: "600" },
  itemCount: { color: posColors.mutedInk, fontSize: 14 },
  time: { color: posColors.mutedInk, fontSize: 13 },
  amountColumn: { alignItems: "flex-end", minWidth: 126 },
  amountLabel: { color: posColors.mutedInk, fontSize: 13 },
  amount: { color: posColors.ink, fontSize: 22, fontWeight: "800" },
  button: { alignItems: "center", borderRadius: 4, justifyContent: "center", minHeight: HELD_ORDERS_MIN_TOUCH_TARGET, paddingHorizontal: 16 },
  buttonPrimary: { backgroundColor: posColors.orange },
  buttonSecondary: { backgroundColor: posColors.blueSoft, borderColor: posColors.blue, borderWidth: 1 },
  buttonQuiet: { backgroundColor: posColors.surface, borderColor: posColors.border, borderWidth: 1 },
  buttonDanger: { backgroundColor: posColors.red },
  buttonDisabled: { opacity: 0.45 },
  buttonText: { color: "#FFFFFF", fontSize: 15, fontWeight: "800" },
  buttonTextDark: { color: posColors.ink },
  pressed: { opacity: 0.78 },
  centered: { alignItems: "center", flex: 1, justifyContent: "center", padding: 32 },
  centeredTitle: { color: posColors.ink, fontSize: 20, fontWeight: "800", textAlign: "center" },
  centeredHint: { color: posColors.mutedInk, fontSize: 15, marginBottom: 20, marginTop: 8, textAlign: "center" },
});
