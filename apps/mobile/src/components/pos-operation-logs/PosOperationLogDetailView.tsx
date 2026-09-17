import { useState } from "react";
import { Pressable, ScrollView, StyleSheet, View } from "react-native";
import { ActivityIndicator, Button, Icon, IconButton, Text } from "react-native-paper";
import { SafeAreaView, useSafeAreaInsets } from "react-native-safe-area-context";
import { EmptyState } from "@/components/ui/EmptyState";
import {
  buildMoneyRows,
  formatLogDateTime,
  formatMoney,
  formatQuantity,
  formatSignedMoney,
  operationTypeI18nKey,
  reportingDelaySeconds,
  shortenIdentifier,
} from "@/modules/pos-operation-logs/logic";
import type { PosOperationLogDetail, PosOperationLogDetailItem } from "@/modules/pos-operation-logs/types";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { HB_COLORS, HB_RADIUS, HB_SPACING } from "@/shared/theme/tokens";
import { LOG_UI, OUTCOME_TONES, platformIcon } from "./log-ui";

export interface PosOperationLogDetailViewProps {
  detail: PosOperationLogDetail | null;
  loading: boolean;
  error: string | null;
  storeName: string | null;
  copiedKey: string | null;
  onCopy: (key: string, value: string) => void;
  onOpenOrderTimeline: (orderGuid: string) => void;
  onOpenCashierToday: (detail: PosOperationLogDetail) => void;
  onRetry: () => void;
  onBack: () => void;
}

export function PosOperationLogDetailView({
  detail,
  loading,
  error,
  storeName,
  copiedKey,
  onCopy,
  onOpenOrderTimeline,
  onOpenCashierToday,
  onRetry,
  onBack,
}: PosOperationLogDetailViewProps) {
  const { t } = useAppTranslation("posOperationLogs");
  const insets = useSafeAreaInsets();
  const [jsonExpanded, setJsonExpanded] = useState(false);

  return (
    <SafeAreaView style={styles.safe} edges={["top", "left", "right"]}>
      <View style={styles.header}>
        <IconButton icon="chevron-left" accessibilityLabel={t("actions.back")} onPress={onBack} />
        <Text variant="titleMedium" style={styles.headerTitle}>
          {t("detailTitle")}
        </Text>
      </View>
      {loading ? (
        <View style={styles.state}>
          <ActivityIndicator color={HB_COLORS.brand} />
          <Text style={styles.stateText}>{t("states.detailLoading")}</Text>
        </View>
      ) : error || !detail ? (
        <View style={styles.state}>
          <EmptyState
            title={t("states.detailFailed")}
            description={error ?? undefined}
            actionLabel={t("actions.retry")}
            onAction={onRetry}
          />
        </View>
      ) : (
        <DetailBody
          detail={detail}
          storeName={storeName}
          copiedKey={copiedKey}
          jsonExpanded={jsonExpanded}
          onToggleJson={() => setJsonExpanded((value) => !value)}
          onCopy={onCopy}
          onOpenOrderTimeline={onOpenOrderTimeline}
          onOpenCashierToday={onOpenCashierToday}
          bottomInset={insets.bottom}
          t={t}
        />
      )}
    </SafeAreaView>
  );
}

function DetailBody({
  detail,
  storeName,
  copiedKey,
  jsonExpanded,
  onToggleJson,
  onCopy,
  onOpenOrderTimeline,
  onOpenCashierToday,
  bottomInset,
  t,
}: {
  detail: PosOperationLogDetail;
  storeName: string | null;
  copiedKey: string | null;
  jsonExpanded: boolean;
  onToggleJson: () => void;
  onCopy: (key: string, value: string) => void;
  onOpenOrderTimeline: (orderGuid: string) => void;
  onOpenCashierToday: (detail: PosOperationLogDetail) => void;
  bottomInset: number;
  t: (key: string, options?: Record<string, unknown>) => string;
}) {
  const tone = OUTCOME_TONES[detail.outcome];
  const operationKey = `operations.${operationTypeI18nKey(detail.operationType)}`;
  const operationLabel = t(operationKey) === operationKey ? detail.operationType : t(operationKey);
  const delay = reportingDelaySeconds(detail);
  const moneyRows = buildMoneyRows(detail);
  const employee = [detail.cashierName, detail.cashierId].filter(Boolean).join(" · ") || "—";
  const reason = [detail.reasonCode, detail.safeMessage].filter(Boolean).join(" · ");
  const canTraceCashier = Boolean(detail.cashierId || detail.cashierName);

  return (
    <ScrollView contentContainerStyle={[styles.content, { paddingBottom: HB_SPACING.lg + bottomInset }]}>
      <View style={[styles.hero, { borderLeftColor: tone.accent ?? HB_COLORS.outlineMuted }]}>
        <View style={styles.heroTitleRow}>
          <Text style={styles.heroTitle}>{operationLabel}</Text>
          <View style={[LOG_UI.pill, styles.heroPill, { backgroundColor: tone.background }]}>
            <Text style={[LOG_UI.pillText, styles.heroPillText, { color: tone.text }]}>
              {t(`outcomes.${detail.outcome}`)}
            </Text>
          </View>
        </View>
        <Text style={styles.heroMeta}>
          {formatLogDateTime(detail.occurredAtUtc)}
          {delay != null ? ` · ${t("detail.reportingDelay", { seconds: delay })}` : ""}
        </Text>
        <View style={styles.flagRow}>
          {detail.isEmergencyOverride ? (
            <View style={[LOG_UI.tag, LOG_UI.tagDanger]}>
              <Text style={[LOG_UI.tagText, LOG_UI.tagDangerText]}>{t("flags.emergencyOverride")}</Text>
            </View>
          ) : null}
          {detail.isOfflineCached ? (
            <View style={LOG_UI.tag}>
              <Text style={LOG_UI.tagText}>{t("flags.offlineCached")}</Text>
            </View>
          ) : null}
          <View style={LOG_UI.tag}>
            <Text style={LOG_UI.tagText}>{t("detail.trustLevel")}</Text>
          </View>
        </View>
        {reason ? (
          <View style={[styles.reason, { backgroundColor: tone.background }]}>
            <Icon
              source={detail.outcome === "Succeeded" ? "information-outline" : "shield-alert-outline"}
              size={15}
              color={tone.text}
            />
            <Text style={[styles.reasonText, { color: tone.text }]}>{reason}</Text>
          </View>
        ) : null}
      </View>

      <Text style={LOG_UI.sectionLabel}>{t("detail.operator")}</Text>
      <View style={LOG_UI.card}>
        <KeyValue label={t("detail.employee")} value={employee} />
        <KeyValue
          label={t("detail.storeDevice")}
          value={`${storeName ?? detail.storeCode} · ${detail.deviceCode}`}
        />
        <KeyValue
          label={t("detail.platformVersion")}
          value={`${detail.deviceSystem ?? t("platforms.Unknown")}${detail.appVersion ? ` · v${detail.appVersion}` : ""}`}
          icon={platformIcon(detail.deviceSystem)}
          last
        />
      </View>

      {moneyRows.length > 0 || detail.paymentAmount != null ? (
        <>
          <Text style={LOG_UI.sectionLabel}>{t("detail.money", { currency: detail.currencyCode })}</Text>
          <View style={[LOG_UI.card, styles.moneyCard]}>
            {moneyRows.length > 0 ? (
              <View style={styles.moneyGrid}>
                <Text style={[styles.moneyHead, styles.moneyLabelCol]}>{t("detail.moneyItem")}</Text>
                <Text style={styles.moneyHead}>{t("detail.before")}</Text>
                <Text style={styles.moneyHead}>{t("detail.after")}</Text>
                <Text style={styles.moneyHead}>{t("detail.delta")}</Text>
                {moneyRows.map((row) => (
                  <MoneyRow
                    key={row.key}
                    label={t(`detail.${row.key}`)}
                    before={row.before}
                    after={row.after}
                    delta={row.delta}
                    currency={detail.currencyCode}
                    emphasis={row.key === "actual"}
                  />
                ))}
              </View>
            ) : null}
            {detail.paymentAmount != null ? (
              <KeyValue
                label={t("detail.paymentAmount")}
                value={`${formatMoney(detail.paymentAmount, detail.currencyCode)}${detail.paymentMethod ? ` · ${detail.paymentMethod}` : ""}`}
                mono
                last
              />
            ) : null}
          </View>
        </>
      ) : null}

      {detail.items.length > 0 ? (
        <>
          <Text style={LOG_UI.sectionLabel}>{t("detail.items", { count: detail.items.length })}</Text>
          <View style={LOG_UI.card}>
            {detail.items.map((line, index) => (
              <LineItem
                key={`${line.lineIndex}-${index}`}
                line={line}
                currency={detail.currencyCode}
                last={index === detail.items.length - 1}
                t={t}
              />
            ))}
          </View>
        </>
      ) : null}

      <Text style={LOG_UI.sectionLabel}>{t("detail.trace")}</Text>
      <View style={LOG_UI.card}>
        <CopyRow label={t("detail.order")} value={detail.orderGuid} copyKey="order" copiedKey={copiedKey} onCopy={onCopy} />
        <CopyRow label={t("detail.receipt")} value={detail.receiptNumber} copyKey="receipt" copiedKey={copiedKey} onCopy={onCopy} />
        <CopyRow label={t("detail.correlation")} value={detail.correlationId} copyKey="correlation" copiedKey={copiedKey} onCopy={onCopy} />
        <CopyRow label={t("detail.traceId")} value={detail.traceId} copyKey="trace" copiedKey={copiedKey} onCopy={onCopy} last={!detail.propertiesJson} />
        {detail.propertiesJson ? (
          <>
            <Pressable accessibilityRole="button" onPress={onToggleJson} style={styles.jsonToggle}>
              <Icon source="code-json" size={15} color={HB_COLORS.textSecondary} />
              <Text style={styles.jsonToggleText}>{t("detail.propertiesJson")}</Text>
              <Icon source={jsonExpanded ? "chevron-up" : "chevron-down"} size={16} color={HB_COLORS.textSecondary} />
            </Pressable>
            {jsonExpanded ? (
              <Text selectable style={styles.json}>
                {prettyJson(detail.propertiesJson)}
              </Text>
            ) : null}
          </>
        ) : null}
      </View>

      <View style={styles.actions}>
        <Button
          mode="outlined"
          icon="timeline-clock-outline"
          disabled={!detail.orderGuid}
          onPress={() => detail.orderGuid && onOpenOrderTimeline(detail.orderGuid)}
          style={styles.actionButton}
          contentStyle={styles.actionContent}
          labelStyle={styles.actionLabel}
        >
          {t("actions.orderTimeline")}
        </Button>
        <Button
          mode="outlined"
          icon="account-clock-outline"
          disabled={!canTraceCashier}
          onPress={() => onOpenCashierToday(detail)}
          style={styles.actionButton}
          contentStyle={styles.actionContent}
          labelStyle={styles.actionLabel}
        >
          {t("actions.cashierToday")}
        </Button>
      </View>
    </ScrollView>
  );
}

function prettyJson(value: string): string {
  try {
    return JSON.stringify(JSON.parse(value), null, 2);
  } catch {
    return value;
  }
}

function KeyValue({
  label,
  value,
  icon,
  mono = false,
  last = false,
}: {
  label: string;
  value: string;
  icon?: string;
  mono?: boolean;
  last?: boolean;
}) {
  return (
    <View style={[styles.kv, last ? styles.kvLast : null]}>
      <Text style={styles.kvLabel}>{label}</Text>
      <View style={styles.kvValueRow}>
        {icon ? <Icon source={icon} size={14} color={HB_COLORS.textSecondary} /> : null}
        <Text numberOfLines={2} style={[styles.kvValue, mono ? LOG_UI.mono : null]}>
          {value}
        </Text>
      </View>
    </View>
  );
}

function CopyRow({
  label,
  value,
  copyKey,
  copiedKey,
  onCopy,
  last = false,
}: {
  label: string;
  value: string | null;
  copyKey: string;
  copiedKey: string | null;
  onCopy: (key: string, value: string) => void;
  last?: boolean;
}) {
  const copied = copiedKey === copyKey;
  return (
    <View style={[styles.kv, last ? styles.kvLast : null]}>
      <Text style={styles.kvLabel}>{label}</Text>
      {value ? (
        <Pressable
          accessibilityRole="button"
          accessibilityLabel={`${label} ${value}`}
          onPress={() => onCopy(copyKey, value)}
          style={styles.copyRow}
        >
          <Text style={[styles.copyValue, LOG_UI.mono]}>{shortenIdentifier(value, 6)}</Text>
          <Icon source={copied ? "check" : "content-copy"} size={14} color={copied ? HB_COLORS.success : HB_COLORS.action} />
        </Pressable>
      ) : (
        <Text style={[styles.kvValue, styles.kvEmpty]}>—</Text>
      )}
    </View>
  );
}

function MoneyRow({
  label,
  before,
  after,
  delta,
  currency,
  emphasis,
}: {
  label: string;
  before: number | null;
  after: number | null;
  delta: number | null;
  currency: string;
  emphasis: boolean;
}) {
  const deltaStyle = delta == null ? styles.moneyEmpty : delta < 0 ? styles.moneyNegative : delta > 0 ? styles.moneyPositive : null;
  return (
    <>
      <Text style={[styles.moneyCell, styles.moneyLabelCol, emphasis ? styles.moneyEmphasis : null]}>{label}</Text>
      <Text style={[styles.moneyCell, LOG_UI.mono]}>{formatMoney(before, currency).replace(/^[^\d−]+/, "")}</Text>
      <Text style={[styles.moneyCell, LOG_UI.mono]}>{formatMoney(after, currency).replace(/^[^\d−]+/, "")}</Text>
      <Text style={[styles.moneyCell, LOG_UI.mono, deltaStyle, emphasis ? styles.moneyEmphasis : null]}>
        {formatSignedMoney(delta, currency).replace(/A\$|[A-Z]{3} /, "")}
      </Text>
    </>
  );
}

function LineItem({
  line,
  currency,
  last,
  t,
}: {
  line: PosOperationLogDetailItem;
  currency: string;
  last: boolean;
  t: (key: string, options?: Record<string, unknown>) => string;
}) {
  const codes = [line.itemNumber, line.productCode, line.referenceCode].filter(Boolean).join(" · ");
  return (
    <View style={[styles.line, last ? styles.kvLast : null]}>
      <Text numberOfLines={2} style={styles.lineName}>
        {line.displayName || line.productCode || line.lookupCode || "—"}
      </Text>
      {codes ? <Text style={styles.lineCodes}>{codes}</Text> : null}
      <Change label={t("detail.quantity")} before={formatQuantity(line.beforeQuantity)} after={formatQuantity(line.afterQuantity)} delta={line.quantityDelta} />
      <Change label={t("detail.unitPrice")} before={formatMoney(line.beforeUnitPrice, currency)} after={formatMoney(line.afterUnitPrice, currency)} delta={line.unitPriceDelta} />
      {line.beforeDiscountAmount != null || line.afterDiscountAmount != null ? (
        <Change label={t("detail.lineDiscount")} before={formatMoney(line.beforeDiscountAmount, currency)} after={formatMoney(line.afterDiscountAmount, currency)} delta={line.discountAmountDelta} />
      ) : null}
      {line.beforeActualAmount != null || line.afterActualAmount != null ? (
        <Change label={t("detail.lineActual")} before={formatMoney(line.beforeActualAmount, currency)} after={formatMoney(line.afterActualAmount, currency)} delta={line.actualAmountDelta} />
      ) : null}
    </View>
  );
}

/** 单行"前 → 后"，无变化时不着色，避免整块明细一片红。 */
function Change({
  label,
  before,
  after,
  delta,
}: {
  label: string;
  before: string;
  after: string;
  delta: number | null;
}) {
  if (before === "—" && after === "—") return null;
  const changed = delta != null && delta !== 0;
  return (
    <View style={styles.change}>
      <Text style={styles.changeLabel}>{label}</Text>
      <Text style={[styles.changeValue, LOG_UI.mono]}>
        {before}
        <Text style={styles.changeArrow}> → </Text>
        <Text style={changed ? (delta < 0 ? styles.moneyNegative : styles.moneyPositive) : null}>{after}</Text>
      </Text>
    </View>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1, backgroundColor: HB_COLORS.background },
  header: { flexDirection: "row", alignItems: "center" },
  headerTitle: { fontWeight: "700", color: HB_COLORS.textPrimary },
  content: { paddingHorizontal: HB_SPACING.md },
  hero: {
    backgroundColor: HB_COLORS.white,
    borderRadius: HB_RADIUS.surface,
    borderWidth: StyleSheet.hairlineWidth,
    borderColor: HB_COLORS.outlineMuted,
    borderLeftWidth: 3,
    padding: HB_SPACING.sm,
    gap: 6,
  },
  heroTitleRow: { flexDirection: "row", alignItems: "center", justifyContent: "space-between", gap: HB_SPACING.xs },
  heroTitle: { fontSize: 18, lineHeight: 24, fontWeight: "700", color: HB_COLORS.textPrimary, flexShrink: 1 },
  heroPill: { paddingHorizontal: 9, paddingVertical: 3 },
  heroPillText: { fontSize: 12, lineHeight: 16 },
  heroMeta: { fontSize: 12, color: HB_COLORS.textSecondary },
  flagRow: { flexDirection: "row", flexWrap: "wrap", gap: 6 },
  reason: { flexDirection: "row", alignItems: "flex-start", gap: 6, padding: HB_SPACING.xs, borderRadius: HB_RADIUS.control },
  reasonText: { flex: 1, fontSize: 12, lineHeight: 17 },
  kv: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: HB_SPACING.sm,
    paddingHorizontal: HB_SPACING.sm,
    paddingVertical: 9,
    borderBottomWidth: StyleSheet.hairlineWidth,
    borderBottomColor: HB_COLORS.outlineMuted,
  },
  kvLast: { borderBottomWidth: 0 },
  kvLabel: { fontSize: 12, color: HB_COLORS.textSecondary },
  kvValueRow: { flexDirection: "row", alignItems: "center", gap: 4, flexShrink: 1 },
  kvValue: { fontSize: 13, color: HB_COLORS.textPrimary, textAlign: "right", flexShrink: 1 },
  kvEmpty: { color: HB_COLORS.outline },
  copyRow: { flexDirection: "row", alignItems: "center", gap: 6 },
  copyValue: { fontSize: 13, color: HB_COLORS.action },
  moneyCard: { paddingTop: 4 },
  moneyGrid: {
    flexDirection: "row",
    flexWrap: "wrap",
    paddingHorizontal: HB_SPACING.sm,
    paddingBottom: 6,
  },
  moneyHead: { width: "22%", fontSize: 11, color: HB_COLORS.textSecondary, textAlign: "right", paddingVertical: 4 },
  moneyCell: { width: "22%", fontSize: 13, color: HB_COLORS.textPrimary, textAlign: "right", paddingVertical: 4 },
  moneyLabelCol: { width: "34%", textAlign: "left" },
  moneyEmphasis: { fontWeight: "700" },
  moneyNegative: { color: HB_COLORS.danger },
  moneyPositive: { color: HB_COLORS.success },
  moneyEmpty: { color: HB_COLORS.outline },
  line: {
    paddingHorizontal: HB_SPACING.sm,
    paddingVertical: HB_SPACING.xs,
    gap: 2,
    borderBottomWidth: StyleSheet.hairlineWidth,
    borderBottomColor: HB_COLORS.outlineMuted,
  },
  lineName: { fontSize: 13, fontWeight: "600", color: HB_COLORS.textPrimary },
  lineCodes: { fontSize: 11, color: HB_COLORS.textSecondary },
  change: { flexDirection: "row", justifyContent: "space-between", alignItems: "center", marginTop: 2 },
  changeLabel: { fontSize: 12, color: HB_COLORS.textSecondary },
  changeValue: { fontSize: 12, color: HB_COLORS.textPrimary },
  changeArrow: { color: HB_COLORS.outline },
  jsonToggle: {
    flexDirection: "row",
    alignItems: "center",
    gap: 6,
    paddingHorizontal: HB_SPACING.sm,
    paddingVertical: 9,
    borderTopWidth: StyleSheet.hairlineWidth,
    borderTopColor: HB_COLORS.outlineMuted,
  },
  jsonToggleText: { flex: 1, fontSize: 12, color: HB_COLORS.textSecondary },
  json: {
    fontFamily: "Menlo",
    fontSize: 11,
    lineHeight: 15,
    color: HB_COLORS.textPrimary,
    backgroundColor: HB_COLORS.surfaceMuted,
    marginHorizontal: HB_SPACING.sm,
    marginBottom: HB_SPACING.sm,
    padding: HB_SPACING.xs,
    borderRadius: HB_RADIUS.control,
  },
  actions: { flexDirection: "row", gap: HB_SPACING.xs, marginTop: HB_SPACING.md },
  actionButton: { flex: 1, borderRadius: HB_RADIUS.control, borderColor: HB_COLORS.outline },
  actionContent: { minHeight: 42 },
  actionLabel: { fontSize: 12 },
  state: { paddingTop: HB_SPACING.xl, paddingHorizontal: HB_SPACING.lg, alignItems: "center", gap: HB_SPACING.sm },
  stateText: { fontSize: 13, color: HB_COLORS.textSecondary },
});
