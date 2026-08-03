import { useEffect, useSyncExternalStore } from "react";
import { useTranslation } from "react-i18next";
import {
  ActivityIndicator,
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
import { HeldOrdersPresenter } from "./held-orders-presenter";

import { PosPressable } from "@/ui/controls/pos-pressable";
import { posColors } from "@/ui/theme";

export const HELD_ORDERS_MIN_TOUCH_TARGET = 44;

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
  }, [presenter]);

  return (
    <SafeAreaView style={styles.safeArea} testID="held-orders-screen">
      <View style={styles.header}>
        <View style={styles.identity}>
          <Text style={styles.title}>{t("title")}</Text>
          <Text style={styles.subtitle}>{t("subtitle")}</Text>
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
        {state.kind === "ready" && !state.rows.length ? (
          <CenteredState
            hint={t("empty.hint")}
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
                locale={locale}
                onRecall={() => void presenter.recall(item.holdId)}
                onRecover={() => void presenter.recover(item.holdId)}
                onRelease={() => void presenter.release(item.holdId)}
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
  locale,
  onRecall,
  onRecover,
  onRelease,
  row,
}: Readonly<{
  busy: boolean;
  locale: ReturnType<typeof resolveHeldOrdersLocale>;
  onRecall(): void;
  onRecover(): void;
  onRelease(): void;
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
        <Text style={styles.sequence}>{t("list.sequence", { sequence: row.localSequence })}</Text>
        <Text style={[styles.status, recalling && styles.statusWarning]}>
          {t(recalling ? "status.Recalling" : "status.Pending")}
        </Text>
        <Text style={styles.itemCount}>{t("list.items", { count: row.itemCount })}</Text>
        <Text style={styles.time}>{t("list.heldAt", { time: formatTime(row.heldAtIso, locale) })}</Text>
      </View>
      <View style={styles.amountColumn}>
        <Text style={styles.amountLabel}>{t("list.amount")}</Text>
        <Text style={styles.amount}>{formatAud(row.actualAmountCents, locale)}</Text>
      </View>
      <View style={styles.rowActions}>
        <ActionButton
          disabled={busy}
          label={t(recalling ? "action.recover" : "action.recall")}
          onPress={recalling ? onRecover : onRecall}
          testID={`held-order-action-${row.holdId}`}
          tone={recalling ? "secondary" : "primary"}
        />
        {recalling ? (
          <ActionButton
            disabled={busy}
            label={t("action.release")}
            onPress={onRelease}
            testID={`held-order-release-${row.holdId}`}
            tone="quiet"
          />
        ) : null}
      </View>
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
  tone?: "primary" | "secondary" | "quiet";
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
        tone === "primary" ? styles.buttonPrimary : tone === "secondary" ? styles.buttonSecondary : styles.buttonQuiet,
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
  list: { gap: 10, paddingBottom: 28 },
  row: { alignItems: "center", backgroundColor: posColors.surface, borderColor: posColors.border, borderRadius: 4, borderWidth: 1, flexDirection: "row", gap: 20, minHeight: 106, padding: 18 },
  rowIdentity: { flex: 1, gap: 4 },
  rowActions: { alignItems: "stretch", gap: 8 },
  sequence: { color: posColors.ink, fontSize: 16, fontWeight: "800" },
  status: { color: posColors.green, fontSize: 14, fontWeight: "700" },
  statusWarning: { color: posColors.orange },
  itemCount: { color: posColors.mutedInk, fontSize: 14 },
  time: { color: posColors.mutedInk, fontSize: 13 },
  amountColumn: { alignItems: "flex-end", minWidth: 126 },
  amountLabel: { color: posColors.mutedInk, fontSize: 13 },
  amount: { color: posColors.ink, fontSize: 22, fontWeight: "800" },
  button: { alignItems: "center", borderRadius: 4, justifyContent: "center", minHeight: HELD_ORDERS_MIN_TOUCH_TARGET, paddingHorizontal: 16 },
  buttonPrimary: { backgroundColor: posColors.orange },
  buttonSecondary: { backgroundColor: posColors.blueSoft, borderColor: posColors.blue, borderWidth: 1 },
  buttonQuiet: { backgroundColor: posColors.surface, borderColor: posColors.border, borderWidth: 1 },
  buttonDisabled: { opacity: 0.45 },
  buttonText: { color: "#FFFFFF", fontSize: 15, fontWeight: "800" },
  buttonTextDark: { color: posColors.ink },
  pressed: { opacity: 0.78 },
  centered: { alignItems: "center", flex: 1, justifyContent: "center", padding: 32 },
  centeredTitle: { color: posColors.ink, fontSize: 20, fontWeight: "800", textAlign: "center" },
  centeredHint: { color: posColors.mutedInk, fontSize: 15, marginBottom: 20, marginTop: 8, textAlign: "center" },
});
