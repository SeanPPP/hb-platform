import { memo } from "react";
import { Pressable, StyleSheet, View } from "react-native";
import { Icon, Text } from "react-native-paper";
import {
  describeRowSummary,
  formatLogTime,
  formatSignedMoney,
  resolveRowAmount,
} from "@/modules/pos-operation-logs/logic";
import type { PosOperationLogItem } from "@/modules/pos-operation-logs/types";
import { HB_COLORS, HB_SPACING } from "@/shared/theme/tokens";
import { LOG_UI, OUTCOME_TONES, platformIcon } from "./log-ui";

export interface PosOperationLogRowProps {
  item: PosOperationLogItem;
  operationLabel: (operationType: string) => string;
  outcomeLabel: (outcome: PosOperationLogItem["outcome"]) => string;
  t: (key: string, options?: Record<string, unknown>) => string;
  onPress: (item: PosOperationLogItem) => void;
}

function summaryText(
  item: PosOperationLogItem,
  t: PosOperationLogRowProps["t"],
): string {
  const summary = describeRowSummary(item);
  switch (summary.kind) {
    case "reason":
      return [summary.reasonCode ? t("row.reason", { code: summary.reasonCode }) : null, summary.message]
        .filter(Boolean)
        .join(" · ");
    case "product":
      return summary.extraCount > 0
        ? t("row.productMore", { name: summary.name, count: summary.extraCount })
        : summary.name;
    case "receipt":
      return [t("row.receipt", { value: summary.receiptNumber }), summary.paymentMethod]
        .filter(Boolean)
        .join(" · ");
    case "payment":
      return summary.paymentMethod;
    default:
      return item.orderGuid ? "" : t("row.noOrder");
  }
}

/** 三行固定结构：操作+结果 / 员工+终端 / 摘要；右列金额与商品数。异常行左侧描边。 */
function PosOperationLogRowComponent({
  item,
  operationLabel,
  outcomeLabel,
  t,
  onPress,
}: PosOperationLogRowProps) {
  const tone = OUTCOME_TONES[item.outcome];
  const amount = resolveRowAmount(item);
  const summary = summaryText(item, t);
  const employee = item.cashierName || item.cashierId || "—";
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={`${formatLogTime(item.occurredAtUtc)} ${operationLabel(item.operationType)} ${outcomeLabel(item.outcome)} ${employee}`}
      onPress={() => onPress(item)}
      style={({ pressed }) => [
        styles.row,
        { borderLeftColor: tone.accent ?? "transparent" },
        pressed ? styles.rowPressed : null,
      ]}
    >
      <Text style={[styles.time, LOG_UI.mono]}>{formatLogTime(item.occurredAtUtc)}</Text>
      <View style={styles.main}>
        <View style={styles.titleLine}>
          <Text numberOfLines={1} style={styles.title}>
            {operationLabel(item.operationType)}
          </Text>
          <View style={[LOG_UI.pill, { backgroundColor: tone.background }]}>
            <Text style={[LOG_UI.pillText, { color: tone.text }]}>{outcomeLabel(item.outcome)}</Text>
          </View>
          {item.isEmergencyOverride ? (
            <View style={[LOG_UI.tag, LOG_UI.tagDanger]}>
              <Text style={[LOG_UI.tagText, LOG_UI.tagDangerText]}>{t("flags.emergencyOverride")}</Text>
            </View>
          ) : null}
        </View>
        <View style={styles.metaLine}>
          <Text numberOfLines={1} style={styles.meta}>
            {employee} · {item.deviceCode}
          </Text>
          <Icon source={platformIcon(item.deviceSystem)} size={13} color={HB_COLORS.textSecondary} />
          {item.isOfflineCached ? (
            <View style={LOG_UI.tag}>
              <Text style={LOG_UI.tagText}>{t("flags.offlineCached")}</Text>
            </View>
          ) : null}
        </View>
        {summary ? (
          <Text numberOfLines={1} style={styles.meta}>
            {summary}
          </Text>
        ) : null}
      </View>
      <View style={styles.side}>
        <Text
          style={[
            styles.amount,
            LOG_UI.mono,
            amount == null ? styles.amountEmpty : amount < 0 ? styles.amountNegative : null,
          ]}
        >
          {formatSignedMoney(amount, item.currencyCode)}
        </Text>
        {item.productCount > 0 ? (
          <Text style={styles.meta}>{t("row.products", { count: item.productCount })}</Text>
        ) : item.paymentMethod && amount != null ? (
          <Text numberOfLines={1} style={styles.meta}>
            {item.paymentMethod}
          </Text>
        ) : null}
      </View>
    </Pressable>
  );
}

export const PosOperationLogRow = memo(PosOperationLogRowComponent);

const styles = StyleSheet.create({
  row: {
    flexDirection: "row",
    gap: HB_SPACING.xs,
    paddingVertical: 9,
    paddingRight: HB_SPACING.sm,
    paddingLeft: 9,
    borderLeftWidth: 3,
    borderBottomWidth: StyleSheet.hairlineWidth,
    borderBottomColor: HB_COLORS.outlineMuted,
    backgroundColor: HB_COLORS.white,
  },
  rowPressed: { backgroundColor: HB_COLORS.surfaceMuted },
  time: { width: 56, fontSize: 12, lineHeight: 18, color: HB_COLORS.textSecondary },
  main: { flex: 1, minWidth: 0, gap: 2 },
  titleLine: { flexDirection: "row", alignItems: "center", gap: 6, flexWrap: "wrap" },
  title: { fontSize: 14, lineHeight: 18, fontWeight: "600", color: HB_COLORS.textPrimary, flexShrink: 1 },
  metaLine: { flexDirection: "row", alignItems: "center", gap: 4 },
  meta: { fontSize: 12, lineHeight: 16, color: HB_COLORS.textSecondary, flexShrink: 1 },
  side: { alignItems: "flex-end", minWidth: 72, gap: 2 },
  amount: { fontSize: 13, lineHeight: 18, fontWeight: "600", color: HB_COLORS.textPrimary },
  amountNegative: { color: HB_COLORS.danger },
  amountEmpty: { color: HB_COLORS.outline, fontWeight: "400" },
});
