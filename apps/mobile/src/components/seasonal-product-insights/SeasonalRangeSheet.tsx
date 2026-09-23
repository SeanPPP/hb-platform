import { useEffect, useState } from "react";
import { ScrollView, StyleSheet, View } from "react-native";
import { Button, Icon, Text, TextInput } from "react-native-paper";
import { InsightSheet } from "@/components/warehouse-product-insights/InsightSheet";
import {
  SEASONAL_MAX_RANGE_DAYS,
  addDays,
  rangesDiffer,
  seasonStart,
  validateSeasonalRange,
} from "@/modules/seasonal-product-insights/logic";
import type {
  SeasonalRange,
  SeasonalRangeError,
  SeasonalRanges,
} from "@/modules/seasonal-product-insights/types";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { HB_COLORS, HB_RADIUS, HB_SPACING } from "@/shared/theme/tokens";

export interface SeasonalRangeSheetProps {
  visible: boolean;
  ranges: SeasonalRanges;
  /** 门店当地今天，用于「8 月 1 日至今」等快捷区间。 */
  today: string;
  onClose: () => void;
  onApply: (ranges: SeasonalRanges) => void;
}

type Section = keyof SeasonalRanges;

/**
 * 进货区间与销售区间分开选择。两者不一致时不直接查询，而是在同一弹层内切到确认步骤：
 * 原生 Modal 之上再弹 Paper 对话框会被压住看不见，所以确认放在弹层里完成。
 */
export function SeasonalRangeSheet({ visible, ranges, today, onClose, onApply }: SeasonalRangeSheetProps) {
  const { t } = useAppTranslation("seasonalProductInsights");
  const [draft, setDraft] = useState<SeasonalRanges>(ranges);
  const [confirming, setConfirming] = useState(false);
  const [submitted, setSubmitted] = useState(false);

  useEffect(() => {
    if (!visible) return;
    setDraft(ranges);
    setConfirming(false);
    setSubmitted(false);
  }, [ranges, visible]);

  const errors: Record<Section, SeasonalRangeError | null> = {
    inbound: validateSeasonalRange(draft.inbound),
    sales: validateSeasonalRange(draft.sales),
  };
  const presets: { key: string; range: SeasonalRange }[] = [
    { key: "season", range: { startDate: seasonStart(today), endDate: today } },
    { key: "last30", range: { startDate: addDays(today, -29), endDate: today } },
    { key: "last90", range: { startDate: addDays(today, -89), endDate: today } },
  ];

  const update = (section: Section, patch: Partial<SeasonalRange>) =>
    setDraft((current) => ({ ...current, [section]: { ...current[section], ...patch } }));

  const submit = () => {
    setSubmitted(true);
    if (errors.inbound || errors.sales) return;
    if (rangesDiffer(draft)) setConfirming(true);
    else onApply(draft);
  };

  const renderSection = (section: Section) => {
    const value = draft[section];
    const error = submitted ? errors[section] : null;
    return (
      <View key={section} style={styles.section}>
        <Text style={styles.sectionTitle}>{t(`range.${section}`)}</Text>
        <View style={styles.presets}>
          {presets.map((preset) => {
            const selected =
              preset.range.startDate === value.startDate && preset.range.endDate === value.endDate;
            return (
              <Button
                key={preset.key}
                compact
                mode={selected ? "contained" : "outlined"}
                onPress={() => update(section, preset.range)}
                style={styles.preset}
                labelStyle={styles.presetLabel}
              >
                {t(`range.presets.${preset.key}`)}
              </Button>
            );
          })}
        </View>
        <View style={styles.inputs}>
          <TextInput
            mode="outlined"
            dense
            label={t("range.start")}
            value={value.startDate}
            onChangeText={(startDate) => update(section, { startDate: startDate.trim() })}
            placeholder="YYYY-MM-DD"
            keyboardType="numbers-and-punctuation"
            style={styles.input}
            error={Boolean(error)}
          />
          <TextInput
            mode="outlined"
            dense
            label={t("range.end")}
            value={value.endDate}
            onChangeText={(endDate) => update(section, { endDate: endDate.trim() })}
            placeholder="YYYY-MM-DD"
            keyboardType="numbers-and-punctuation"
            style={styles.input}
            error={Boolean(error)}
          />
        </View>
        {error ? (
          <Text style={styles.error} accessibilityLiveRegion="polite">
            {t(`range.errors.${error}`, { days: SEASONAL_MAX_RANGE_DAYS })}
          </Text>
        ) : null}
      </View>
    );
  };

  return (
    <InsightSheet
      visible={visible}
      title={confirming ? t("range.confirmTitle") : t("range.title")}
      subtitle={confirming ? null : t("range.subtitle", { days: SEASONAL_MAX_RANGE_DAYS })}
      closeLabel={t("actions.close")}
      onClose={onClose}
      heightRatio={confirming ? 0.5 : 0.78}
    >
      {confirming ? (
        <View style={styles.content}>
          <Text style={styles.confirmText}>{t("range.confirmDescription")}</Text>
          <View style={styles.confirmTable}>
            {(["inbound", "sales"] as Section[]).map((section, index) => (
              <View key={section} style={[styles.confirmRow, index ? styles.confirmRowBorder : null]}>
                <Text style={styles.confirmLabel}>{t(`range.${section}`)}</Text>
                <Text style={styles.confirmValue}>
                  {draft[section].startDate} – {draft[section].endDate}
                </Text>
              </View>
            ))}
          </View>
          <View style={styles.footer}>
            <Button mode="outlined" onPress={() => setConfirming(false)} style={styles.footerButton}>
              {t("range.backToEdit")}
            </Button>
            <Button mode="contained" onPress={() => onApply(draft)} style={styles.footerButton}>
              {t("range.confirmQuery")}
            </Button>
          </View>
        </View>
      ) : (
        <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
          {renderSection("inbound")}
          {renderSection("sales")}
          {rangesDiffer(draft) && !errors.inbound && !errors.sales ? (
            <View style={styles.hint}>
              <Icon source="alert-outline" size={16} color={HB_COLORS.warning} />
              <Text style={styles.hintText}>{t("range.mismatchHint")}</Text>
            </View>
          ) : null}
          <View style={styles.footer}>
            <Button mode="outlined" onPress={onClose} style={styles.footerButton}>
              {t("actions.cancel")}
            </Button>
            <Button mode="contained" onPress={submit} style={styles.footerButton}>
              {t("actions.query")}
            </Button>
          </View>
        </ScrollView>
      )}
    </InsightSheet>
  );
}

const styles = StyleSheet.create({
  content: { padding: HB_SPACING.md, gap: HB_SPACING.md },
  section: { gap: HB_SPACING.xs },
  sectionTitle: { fontSize: 14, fontWeight: "700", color: HB_COLORS.textPrimary },
  presets: { flexDirection: "row", flexWrap: "wrap", gap: 6 },
  preset: { borderRadius: HB_RADIUS.control },
  presetLabel: { fontSize: 12, marginVertical: 5, marginHorizontal: 10 },
  inputs: { flexDirection: "row", gap: HB_SPACING.xs },
  input: { flex: 1, backgroundColor: HB_COLORS.white },
  error: { fontSize: 12, color: HB_COLORS.danger },
  hint: {
    flexDirection: "row",
    gap: 6,
    alignItems: "flex-start",
    padding: HB_SPACING.sm,
    borderRadius: HB_RADIUS.control,
    backgroundColor: "#FFFAEB",
  },
  hintText: { flex: 1, fontSize: 12, lineHeight: 18, color: "#93370D" },
  confirmText: { fontSize: 13, lineHeight: 20, color: HB_COLORS.textSecondary },
  confirmTable: {
    borderRadius: HB_RADIUS.control,
    borderWidth: StyleSheet.hairlineWidth,
    borderColor: HB_COLORS.outlineMuted,
    backgroundColor: "#F9FAFB",
  },
  confirmRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    paddingHorizontal: HB_SPACING.sm,
    paddingVertical: 10,
  },
  confirmRowBorder: { borderTopWidth: StyleSheet.hairlineWidth, borderTopColor: HB_COLORS.outlineMuted },
  confirmLabel: { fontSize: 13, color: HB_COLORS.textSecondary },
  confirmValue: { fontSize: 14, fontWeight: "700", color: HB_COLORS.textPrimary, fontVariant: ["tabular-nums"] },
  footer: { flexDirection: "row", gap: 10 },
  footerButton: { flex: 1, borderRadius: HB_RADIUS.control },
});
