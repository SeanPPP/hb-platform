import { StyleSheet, View } from "react-native";
import { RadioButton, SegmentedButtons, Text, TextInput } from "react-native-paper";
import { MonthDatePickerField } from "@/components/attendance/MonthDatePicker";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { HB_COLORS, HB_SPACING } from "@/shared/theme/tokens";
import { MOBILE_SUPPLY_PRECISIONS, SUPPLY_PLANS, type MobileSupplyPrecision, type SupplyNoticeDraft } from "./supply-notice-draft";
import type { SupplyPlan } from "./types";

interface SupplyNoticeFormProps {
  draft: SupplyNoticeDraft;
  onChange: (next: SupplyNoticeDraft) => void;
  disabled?: boolean;
}

/**
 * 仓库维护屏下架时的供货说明表单。后续计划不预选，避免顺手全选“会补货”让门店误以为迟早会回来。
 */
export function SupplyNoticeForm({ draft, onChange, disabled }: SupplyNoticeFormProps) {
  const { t } = useAppTranslation("supplyNotice");
  const showExpected = draft.supplyPlan !== "Discontinued";

  return (
    <View style={styles.root}>
      <Text variant="labelLarge">{t("form.planLabel")}</Text>
      <RadioButton.Group
        value={draft.supplyPlan ?? ""}
        onValueChange={(value) => onChange({ ...draft, supplyPlan: value as SupplyPlan })}
      >
        {SUPPLY_PLANS.map((plan) => (
          <RadioButton.Item
            key={plan}
            value={plan}
            label={t(`plan.${plan}`)}
            disabled={disabled}
            position="leading"
            style={styles.radioItem}
            labelStyle={styles.radioLabel}
          />
        ))}
      </RadioButton.Group>
      {showExpected ? (
        <View style={styles.block}>
          <Text variant="labelLarge">{t("form.expectedLabel")}</Text>
          <SegmentedButtons
            value={draft.expectedPrecision}
            onValueChange={(value) => onChange({ ...draft, expectedPrecision: value as MobileSupplyPrecision })}
            buttons={MOBILE_SUPPLY_PRECISIONS.map((value) => ({ value, label: t(`form.precision.${value}`), disabled }))}
            density="small"
          />
          {draft.expectedPrecision !== "Unknown" ? (
            <MonthDatePickerField
              value={draft.expectedDate}
              placeholder={t(draft.expectedPrecision === "Month" ? "form.monthPlaceholder" : "form.dayPlaceholder")}
              disabled={disabled}
              onChange={(date) => onChange({ ...draft, expectedDate: date })}
            />
          ) : null}
        </View>
      ) : null}
      <TextInput
        mode="outlined"
        dense
        label={t("form.storeNoteLabel")}
        placeholder={t("form.storeNotePlaceholder")}
        value={draft.storeFacingNote}
        onChangeText={(value) => onChange({ ...draft, storeFacingNote: value })}
        disabled={disabled}
        maxLength={500}
        multiline
      />
      <TextInput
        mode="outlined"
        dense
        label={t("form.internalNoteLabel")}
        value={draft.internalNote}
        onChangeText={(value) => onChange({ ...draft, internalNote: value })}
        disabled={disabled}
        maxLength={500}
        multiline
      />
    </View>
  );
}

const styles = StyleSheet.create({
  root: { gap: HB_SPACING.sm },
  block: { gap: HB_SPACING.xs },
  radioItem: { paddingVertical: 0, paddingHorizontal: 0 },
  radioLabel: { color: HB_COLORS.textPrimary },
});
