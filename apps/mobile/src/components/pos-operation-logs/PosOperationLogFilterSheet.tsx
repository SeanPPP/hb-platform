import { useEffect, useMemo, useState } from "react";
import { Pressable, ScrollView, StyleSheet, View } from "react-native";
import { Button, Icon, Text, TextInput } from "react-native-paper";
import { BusinessSheet } from "@/components/ui/BusinessSheet";
import { StorePickerModal } from "@/components/ui/StorePickerModal";
import {
  POS_OPERATION_DEVICE_SYSTEMS,
  POS_OPERATION_LOG_MAX_RANGE_DAYS,
  POS_OPERATION_OUTCOMES,
  POS_OPERATION_RANGE_PRESETS,
  POS_OPERATION_TYPE_GROUPS,
  operationTypeI18nKey,
  validateCustomRange,
} from "@/modules/pos-operation-logs/logic";
import type { PosOperationLogFilters } from "@/modules/pos-operation-logs/types";
import type { Store } from "@/modules/shop/types";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { HB_COLORS, HB_RADIUS, HB_SPACING } from "@/shared/theme/tokens";
import { LOG_UI } from "./log-ui";

export interface PosOperationLogFilterSheetProps {
  visible: boolean;
  filters: PosOperationLogFilters;
  stores: Store[];
  onClose: () => void;
  onApply: (filters: PosOperationLogFilters) => void;
  onReset: () => void;
}

/** 筛选抽屉：本地草稿，点"应用"才回写；自定义区间校验失败时禁用应用。 */
export function PosOperationLogFilterSheet({
  visible,
  filters,
  stores,
  onClose,
  onApply,
  onReset,
}: PosOperationLogFilterSheetProps) {
  const { t } = useAppTranslation("posOperationLogs");
  const [draft, setDraft] = useState(filters);
  const [typesExpanded, setTypesExpanded] = useState(false);
  const [storePickerVisible, setStorePickerVisible] = useState(false);

  useEffect(() => {
    if (visible) {
      setDraft(filters);
      setTypesExpanded(Boolean(filters.operationType && !POS_OPERATION_TYPE_GROUPS[0].types.includes(filters.operationType)));
    }
  }, [filters, visible]);

  const patch = (partial: Partial<PosOperationLogFilters>) =>
    setDraft((current) => ({ ...current, ...partial }));

  const rangeValidation = useMemo(
    () => (draft.preset === "custom" ? validateCustomRange(draft.startDate, draft.endDate) : { ok: true, dayCount: 0 }),
    [draft.endDate, draft.preset, draft.startDate],
  );
  const rangeError =
    rangeValidation.ok
      ? null
      : rangeValidation.reason === "format"
        ? t("filters.rangeInvalidFormat")
        : rangeValidation.reason === "order"
          ? t("filters.rangeInvalidOrder")
          : t("filters.rangeTooLong", { max: POS_OPERATION_LOG_MAX_RANGE_DAYS, days: rangeValidation.dayCount });

  const operationLabel = (operationType: string) => t(`operations.${operationTypeI18nKey(operationType)}`);
  const selectedStore = draft.storeCode
    ? stores.find((store) => store.storeCode === draft.storeCode) ?? null
    : null;
  const hiddenGroups = POS_OPERATION_TYPE_GROUPS.slice(1);
  const hiddenCount = hiddenGroups.reduce((sum, group) => sum + group.types.length, 0);

  return (
    <BusinessSheet
      visible={visible}
      title={t("filters.title")}
      onDismiss={onClose}
      footer={
        <View style={styles.footer}>
          <Button mode="outlined" onPress={onReset} style={styles.footerButton}>
            {t("actions.reset")}
          </Button>
          <Button
            mode="contained"
            disabled={!rangeValidation.ok}
            onPress={() => onApply(draft)}
            style={[styles.footerButton, styles.footerPrimary]}
          >
            {t("actions.apply")}
          </Button>
        </View>
      }
    >
      <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
        <Text style={LOG_UI.sectionLabel}>{t("filters.timeRange")}</Text>
        <View style={styles.wrap}>
          {POS_OPERATION_RANGE_PRESETS.map((preset) => (
            <OptionChip
              key={preset}
              label={t(`presets.${preset}`)}
              icon={preset === "custom" ? "calendar" : undefined}
              selected={draft.preset === preset}
              onPress={() => patch({ preset })}
            />
          ))}
        </View>
        {draft.preset === "custom" ? (
          <View style={styles.dates}>
            <TextInput
              mode="outlined"
              dense
              label={t("filters.startDate")}
              value={draft.startDate}
              onChangeText={(startDate) => patch({ startDate })}
              placeholder="YYYY-MM-DD"
              keyboardType="numbers-and-punctuation"
              error={!rangeValidation.ok}
              style={styles.dateInput}
            />
            <TextInput
              mode="outlined"
              dense
              label={t("filters.endDate")}
              value={draft.endDate}
              onChangeText={(endDate) => patch({ endDate })}
              placeholder="YYYY-MM-DD"
              keyboardType="numbers-and-punctuation"
              error={!rangeValidation.ok}
              style={styles.dateInput}
            />
          </View>
        ) : null}
        {draft.preset === "custom" ? (
          <Text style={[styles.hint, rangeError ? styles.hintError : null]}>
            {rangeError ?? t("filters.rangeDays", { days: rangeValidation.dayCount })}
          </Text>
        ) : null}

        <Text style={LOG_UI.sectionLabel}>{t("filters.outcome")}</Text>
        <Segmented
          options={[
            { value: "", label: t("quick.all") },
            ...POS_OPERATION_OUTCOMES.map((outcome) => ({ value: outcome, label: t(`outcomes.${outcome}`) })),
          ]}
          value={draft.outcome ?? ""}
          onChange={(value) => patch({ outcome: (value || null) as PosOperationLogFilters["outcome"] })}
        />

        <Text style={LOG_UI.sectionLabel}>{t("filters.platform")}</Text>
        <Segmented
          options={[
            { value: "", label: t("platforms.all") },
            ...POS_OPERATION_DEVICE_SYSTEMS.map((system) => ({ value: system, label: t(`platforms.${system}`) })),
          ]}
          value={draft.deviceSystem ?? ""}
          onChange={(value) => patch({ deviceSystem: (value || null) as PosOperationLogFilters["deviceSystem"] })}
        />

        <Text style={LOG_UI.sectionLabel}>{t("filters.operationType")}</Text>
        <View style={styles.wrap}>
          <OptionChip
            label={t("filters.operationTypeAll")}
            selected={draft.operationType == null}
            onPress={() => patch({ operationType: null })}
          />
          {POS_OPERATION_TYPE_GROUPS[0].types.map((type) => (
            <OptionChip
              key={type}
              label={operationLabel(type)}
              selected={draft.operationType === type}
              onPress={() => patch({ operationType: type })}
            />
          ))}
          {!typesExpanded ? (
            <OptionChip
              label={t("actions.showMoreTypes", { count: hiddenCount })}
              icon="chevron-down"
              onPress={() => setTypesExpanded(true)}
            />
          ) : null}
        </View>
        {typesExpanded
          ? hiddenGroups.map((group) => (
              <View key={group.key}>
                <Text style={styles.groupLabel}>{t(`typeGroups.${group.key}`)}</Text>
                <View style={styles.wrap}>
                  {group.types.map((type) => (
                    <OptionChip
                      key={type}
                      label={operationLabel(type)}
                      selected={draft.operationType === type}
                      onPress={() => patch({ operationType: type })}
                    />
                  ))}
                </View>
              </View>
            ))
          : null}
        {typesExpanded ? (
          <Pressable onPress={() => setTypesExpanded(false)} style={styles.collapse}>
            <Text style={styles.collapseText}>{t("actions.showLessTypes")}</Text>
            <Icon source="chevron-up" size={14} color={HB_COLORS.action} />
          </Pressable>
        ) : null}

        <Text style={LOG_UI.sectionLabel}>{t("filters.identity")}</Text>
        <Pressable
          accessibilityRole="button"
          onPress={() => setStorePickerVisible(true)}
          style={styles.kvRow}
        >
          <Text style={styles.kvLabel}>{t("filters.store")}</Text>
          <Text style={styles.kvValue}>
            {selectedStore ? selectedStore.storeName || selectedStore.storeCode : t("filters.storeAll")}
          </Text>
          <Icon source="chevron-right" size={16} color={HB_COLORS.textSecondary} />
        </Pressable>
        <TextInput
          mode="outlined"
          dense
          label={t("filters.cashier")}
          placeholder={t("filters.cashierPlaceholder")}
          value={draft.cashierKeyword}
          onChangeText={(cashierKeyword) => patch({ cashierKeyword })}
          style={styles.input}
        />
        <TextInput
          mode="outlined"
          dense
          label={t("filters.device")}
          placeholder={t("filters.devicePlaceholder")}
          value={draft.deviceCode}
          onChangeText={(deviceCode) => patch({ deviceCode })}
          autoCapitalize="characters"
          style={styles.input}
        />

        <Text style={LOG_UI.sectionLabel}>{t("filters.context")}</Text>
        <TextInput
          mode="outlined"
          dense
          label={t("filters.product")}
          placeholder={t("filters.productPlaceholder")}
          value={draft.productKeyword}
          onChangeText={(productKeyword) => patch({ productKeyword })}
          style={styles.input}
        />
        <TextInput
          mode="outlined"
          dense
          label={t("filters.order")}
          placeholder={t("filters.orderPlaceholder")}
          value={draft.orderGuid}
          onChangeText={(orderGuid) => patch({ orderGuid })}
          autoCapitalize="none"
          style={styles.input}
        />
        <TextInput
          mode="outlined"
          dense
          label={t("filters.keyword")}
          placeholder={t("filters.keywordPlaceholder")}
          value={draft.keyword}
          onChangeText={(keyword) => patch({ keyword })}
          style={styles.input}
        />

        <Text style={LOG_UI.sectionLabel}>{t("filters.onlyShow")}</Text>
        <View style={styles.wrap}>
          <OptionChip
            label={t("flags.emergencyOverride")}
            icon="alert-outline"
            selected={draft.emergencyOverrideOnly}
            onPress={() => patch({ emergencyOverrideOnly: !draft.emergencyOverrideOnly })}
          />
          <OptionChip
            label={t("flags.offlineCached")}
            icon="cloud-off-outline"
            selected={draft.offlineCachedOnly}
            onPress={() => patch({ offlineCachedOnly: !draft.offlineCachedOnly })}
          />
        </View>
      </ScrollView>
      <StorePickerModal
        // 筛选面板本身是原生 Modal，Portal 会被它盖住，门店选择器必须用原生 Modal 承载。
        host="native-modal"
        presentation="sheet"
        visible={storePickerVisible}
        stores={stores}
        selectedStoreCode={draft.storeCode}
        title={t("filters.storePickerTitle")}
        cancelLabel={t("actions.cancel")}
        includeAllOption
        allLabel={t("filters.storeAll")}
        onDismiss={() => setStorePickerVisible(false)}
        onSelectStore={(store) => {
          patch({ storeCode: store?.storeCode ?? null });
          setStorePickerVisible(false);
        }}
      />
    </BusinessSheet>
  );
}

function OptionChip({
  label,
  icon,
  selected = false,
  onPress,
}: {
  label: string;
  icon?: string;
  selected?: boolean;
  onPress: () => void;
}) {
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityState={{ selected }}
      onPress={onPress}
      style={[LOG_UI.chip, selected ? LOG_UI.chipSelected : null]}
    >
      {icon ? (
        <Icon source={icon} size={14} color={selected ? HB_COLORS.action : HB_COLORS.textSecondary} />
      ) : null}
      <Text style={[LOG_UI.chipText, selected ? LOG_UI.chipTextSelected : null]}>{label}</Text>
    </Pressable>
  );
}

function Segmented({
  options,
  value,
  onChange,
}: {
  options: { value: string; label: string }[];
  value: string;
  onChange: (value: string) => void;
}) {
  return (
    <View style={styles.segmented}>
      {options.map((option, index) => {
        const selected = option.value === value;
        return (
          <Pressable
            key={option.value || "all"}
            accessibilityRole="button"
            accessibilityState={{ selected }}
            onPress={() => onChange(option.value)}
            style={[
              styles.segment,
              index > 0 ? styles.segmentDivider : null,
              selected ? styles.segmentSelected : null,
            ]}
          >
            <Text style={[styles.segmentText, selected ? styles.segmentTextSelected : null]}>
              {option.label}
            </Text>
          </Pressable>
        );
      })}
    </View>
  );
}

const styles = StyleSheet.create({
  content: { paddingHorizontal: HB_SPACING.md, paddingBottom: HB_SPACING.md },
  wrap: { flexDirection: "row", flexWrap: "wrap", gap: 6 },
  dates: { flexDirection: "row", gap: HB_SPACING.xs, marginTop: HB_SPACING.xs },
  dateInput: { flex: 1, backgroundColor: HB_COLORS.white },
  hint: { fontSize: 12, color: HB_COLORS.textSecondary, marginTop: 4 },
  hintError: { color: HB_COLORS.danger },
  groupLabel: { fontSize: 11, color: HB_COLORS.textSecondary, marginTop: HB_SPACING.xs, marginBottom: 4 },
  collapse: { flexDirection: "row", alignItems: "center", gap: 2, alignSelf: "flex-start", marginTop: 6 },
  collapseText: { fontSize: 12, color: HB_COLORS.action },
  kvRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: HB_SPACING.xs,
    minHeight: 44,
    paddingHorizontal: HB_SPACING.sm,
    borderWidth: 1,
    borderColor: HB_COLORS.outline,
    borderRadius: HB_RADIUS.control,
    backgroundColor: HB_COLORS.white,
    marginBottom: HB_SPACING.xs,
  },
  kvLabel: { fontSize: 13, color: HB_COLORS.textSecondary, width: 40 },
  kvValue: { flex: 1, fontSize: 13, color: HB_COLORS.textPrimary, textAlign: "right" },
  input: { backgroundColor: HB_COLORS.white, marginBottom: HB_SPACING.xs },
  segmented: {
    flexDirection: "row",
    borderWidth: 1,
    borderColor: HB_COLORS.outline,
    borderRadius: HB_RADIUS.control,
    overflow: "hidden",
    backgroundColor: HB_COLORS.white,
  },
  segment: { flex: 1, paddingVertical: 7, alignItems: "center" },
  segmentDivider: { borderLeftWidth: StyleSheet.hairlineWidth, borderLeftColor: HB_COLORS.outline },
  segmentSelected: { backgroundColor: HB_COLORS.textPrimary },
  segmentText: { fontSize: 12, color: HB_COLORS.textPrimary },
  segmentTextSelected: { color: HB_COLORS.white, fontWeight: "600" },
  footer: { flexDirection: "row", gap: HB_SPACING.xs },
  footerButton: { borderRadius: HB_RADIUS.control },
  footerPrimary: { flex: 1 },
});
