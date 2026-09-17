import { useEffect, useMemo, useState } from "react";
import { ScrollView, StyleSheet, View } from "react-native";
import { Button, Icon, Text, TextInput } from "react-native-paper";
import {
  WAREHOUSE_INSIGHT_MAX_RANGE_DAYS,
  WAREHOUSE_INSIGHT_RANGE_PRESETS,
  buildInsightPresetRange,
  matchInsightRangePreset,
  validateWarehouseInsightRange,
} from "@/modules/warehouse-product-insights/logic";
import type { WarehouseInsightRange } from "@/modules/warehouse-product-insights/types";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { HB_COLORS, HB_RADIUS, HB_SPACING } from "@/shared/theme/tokens";
import { InsightSheet } from "./InsightSheet";

export interface InsightRangeSheetProps {
  visible: boolean;
  range: WarehouseInsightRange;
  onClose: () => void;
  onApply: (range: WarehouseInsightRange) => void;
}

/** 区间选择：预设 chips + 手输日期，400 天上限以可见余量和一键收敛呈现。 */
export function InsightRangeSheet({
  visible,
  range,
  onClose,
  onApply,
}: InsightRangeSheetProps) {
  const { t } = useAppTranslation("warehouseProductInsights");
  const [startDate, setStartDate] = useState(range.startDate);
  const [endDate, setEndDate] = useState(range.endDate);

  useEffect(() => {
    setStartDate(range.startDate);
    setEndDate(range.endDate);
  }, [range.endDate, range.startDate, visible]);

  const draft = useMemo(
    () => ({ startDate, endDate }),
    [startDate, endDate],
  );
  const validation = useMemo(
    () => validateWarehouseInsightRange(draft),
    [draft],
  );
  const activePreset = validation.ok ? matchInsightRangePreset(draft) : null;
  const usage = Math.min(
    validation.dayCount / WAREHOUSE_INSIGHT_MAX_RANGE_DAYS,
    1,
  );
  const overflow = validation.reason === "tooLong";

  return (
    <InsightSheet
      visible={visible}
      title={t("rangeSheet.title")}
      subtitle={t("rangeSheet.limit", { days: WAREHOUSE_INSIGHT_MAX_RANGE_DAYS })}
      closeLabel={t("actions.close")}
      onClose={onClose}
      heightRatio={0.68}
    >
      <ScrollView
        contentContainerStyle={styles.content}
        keyboardShouldPersistTaps="handled"
      >
        <View style={styles.presets}>
          {WAREHOUSE_INSIGHT_RANGE_PRESETS.map((preset) => {
            const selected = activePreset === preset;
            return (
              <Text
                key={preset}
                accessibilityRole="button"
                onPress={() => {
                  const next = buildInsightPresetRange(endDate, preset);
                  setStartDate(next.startDate);
                }}
                style={[styles.preset, selected ? styles.presetSelected : null]}
              >
                {t("rangeSheet.preset", { days: preset })}
              </Text>
            );
          })}
        </View>
        <View style={styles.inputs}>
          <TextInput
            mode="outlined"
            dense
            label={t("rangeSheet.startDate")}
            value={startDate}
            onChangeText={setStartDate}
            placeholder="YYYY-MM-DD"
            keyboardType="numbers-and-punctuation"
            error={overflow || validation.reason === "order"}
            style={styles.input}
          />
          <TextInput
            mode="outlined"
            dense
            label={t("rangeSheet.endDate")}
            value={endDate}
            onChangeText={setEndDate}
            placeholder="YYYY-MM-DD"
            keyboardType="numbers-and-punctuation"
            style={styles.input}
          />
        </View>
        <View>
          <View style={styles.usageRow}>
            <Text style={[styles.usageText, overflow ? styles.usageDanger : null]}>
              {overflow
                ? t("rangeSheet.overflow", {
                    days: validation.dayCount,
                    excess: validation.dayCount - WAREHOUSE_INSIGHT_MAX_RANGE_DAYS,
                  })
                : t("rangeSheet.selectedDays", { days: validation.dayCount })}
            </Text>
            <Text style={styles.usageLimit}>
              {t("rangeSheet.maxDays", { days: WAREHOUSE_INSIGHT_MAX_RANGE_DAYS })}
            </Text>
          </View>
          <View style={styles.track}>
            <View
              style={[
                styles.trackFill,
                {
                  width: `${Math.round((overflow ? 1 : usage) * 100)}%`,
                  backgroundColor: overflow ? HB_COLORS.danger : HB_COLORS.brand,
                },
              ]}
            />
          </View>
        </View>
        {validation.reason === "format" ? (
          <Notice tone="danger" text={t("rangeSheet.invalidFormat")} />
        ) : null}
        {validation.reason === "order" ? (
          <Notice tone="danger" text={t("rangeSheet.invalidOrder")} />
        ) : null}
        {overflow ? (
          <Notice
            tone="danger"
            text={t("rangeSheet.overflowHint", {
              days: WAREHOUSE_INSIGHT_MAX_RANGE_DAYS,
            })}
          />
        ) : (
          <Notice tone="muted" text={t("rangeSheet.limitHint")} />
        )}
        <View style={styles.actions}>
          {overflow && validation.clampedStartDate ? (
            <Button
              mode="outlined"
              compact
              icon="arrow-collapse-right"
              onPress={() => setStartDate(validation.clampedStartDate!)}
              style={styles.clampButton}
            >
              {t("rangeSheet.clamp", { days: WAREHOUSE_INSIGHT_MAX_RANGE_DAYS })}
            </Button>
          ) : (
            <Button mode="outlined" compact onPress={onClose} style={styles.clampButton}>
              {t("actions.cancel")}
            </Button>
          )}
          <Button
            mode="contained"
            disabled={!validation.ok}
            onPress={() => onApply(draft)}
            style={styles.applyButton}
          >
            {t("rangeSheet.apply")}
          </Button>
        </View>
      </ScrollView>
    </InsightSheet>
  );
}

function Notice({ tone, text }: { tone: "danger" | "muted"; text: string }) {
  const danger = tone === "danger";
  return (
    <View style={[styles.notice, danger ? styles.noticeDanger : null]}>
      <Icon
        source={danger ? "alert-circle-outline" : "information-outline"}
        size={16}
        color={danger ? HB_COLORS.danger : HB_COLORS.textSecondary}
      />
      <Text style={[styles.noticeText, danger ? styles.noticeTextDanger : null]}>
        {text}
      </Text>
    </View>
  );
}

const styles = StyleSheet.create({
  content: { padding: HB_SPACING.md, gap: HB_SPACING.sm },
  presets: { flexDirection: "row", flexWrap: "wrap", gap: HB_SPACING.xs },
  preset: {
    fontSize: 12,
    paddingVertical: 6,
    paddingHorizontal: 12,
    borderRadius: 16,
    backgroundColor: HB_COLORS.surfaceMuted,
    color: HB_COLORS.textSecondary,
    overflow: "hidden",
  },
  presetSelected: { backgroundColor: HB_COLORS.brand, color: HB_COLORS.white },
  inputs: { flexDirection: "row", gap: HB_SPACING.xs },
  input: { flex: 1, minWidth: 0, backgroundColor: HB_COLORS.white },
  usageRow: { flexDirection: "row", justifyContent: "space-between" },
  usageText: { fontSize: 12, color: HB_COLORS.textSecondary },
  usageDanger: { color: HB_COLORS.danger, fontWeight: "700" },
  usageLimit: { fontSize: 12, color: HB_COLORS.textSecondary },
  track: {
    height: 6,
    marginTop: 6,
    borderRadius: 3,
    backgroundColor: HB_COLORS.outlineMuted,
    overflow: "hidden",
  },
  trackFill: { height: "100%" },
  notice: {
    flexDirection: "row",
    gap: HB_SPACING.xs,
    padding: HB_SPACING.sm,
    borderRadius: HB_RADIUS.control,
    backgroundColor: HB_COLORS.surfaceMuted,
  },
  noticeDanger: { backgroundColor: "#FCEBEB" },
  noticeText: { flex: 1, fontSize: 12, color: HB_COLORS.textSecondary, lineHeight: 18 },
  noticeTextDanger: { color: HB_COLORS.danger },
  actions: { flexDirection: "row", gap: HB_SPACING.xs, alignItems: "center" },
  clampButton: { flex: 1.4 },
  applyButton: { flex: 1 },
});
