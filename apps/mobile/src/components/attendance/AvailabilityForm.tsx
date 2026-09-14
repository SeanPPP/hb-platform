import { useMemo, useRef, useState } from "react";
import { Pressable, StyleSheet, View } from "react-native";
import { Button, Card, Icon, IconButton, SegmentedButtons, Text, TextInput } from "react-native-paper";
import { AvailabilityDatePicker } from "./AvailabilityDatePicker";
import { AvailabilityTimePicker } from "./AvailabilityTimePicker";
import { normalizeMonthDate } from "./MonthDatePicker";
import { AvailabilityBatchSaveError } from "@/modules/attendance/availability-batch";
import {
  buildAvailabilityBatchPayload,
  getAvailabilityDraftError,
  isAllDayAvailability,
  type AvailabilityDraft,
} from "@/modules/attendance/availability-entry";
import type {
  AttendanceAvailability,
  AttendanceAvailabilityBatchPayload,
  AttendanceAvailabilityPayload,
} from "@/modules/attendance/types";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { HB_COLORS, HB_RADIUS } from "@/shared/theme/tokens";

function createEmptyForm(defaultDate?: string): AvailabilityDraft {
  return {
    workDates: [normalizeMonthDate(defaultDate)],
    allDay: true,
    startTime: "09:00",
    endTime: "17:30",
    note: "",
  };
}

export function AvailabilityForm({
  availability,
  defaultDate,
  isBusy,
  onWeekChange,
  onCreate,
  onVerify,
  onUpdate,
  onCancel,
}: {
  availability: AttendanceAvailability[];
  defaultDate?: string;
  isBusy: boolean;
  onWeekChange: (date: string) => void;
  onCreate: (payload: AttendanceAvailabilityBatchPayload) => Promise<unknown>;
  onVerify: (payload: AttendanceAvailabilityBatchPayload) => Promise<string[]>;
  onUpdate: (availabilityGuid: string, payload: AttendanceAvailabilityPayload) => Promise<unknown>;
  onCancel: (availabilityGuid: string) => void;
}) {
  const { t } = useAppTranslation(["attendance", "common"]);
  const [editingGuid, setEditingGuid] = useState<string | null>(null);
  const [form, setForm] = useState(() => createEmptyForm(defaultDate));
  const [timePicker, setTimePicker] = useState<"startTime" | "endTime" | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const submittingRef = useRef(false);
  const [saveFailed, setSaveFailed] = useState(false);
  const [verification, setVerification] = useState<{
    payload: AttendanceAvailabilityBatchPayload;
    uncertainDates: string[];
  } | null>(null);
  const busy = isBusy || submitting;
  const locked = busy || verification !== null;
  const activeItems = useMemo(
    () => availability.filter((item) => item.status.toLowerCase() !== "cancelled")
      .sort((a, b) => a.workDate.localeCompare(b.workDate) || a.startTime.localeCompare(b.startTime)),
    [availability],
  );
  const validationError = getAvailabilityDraftError(form);

  const resetForm = () => {
    setEditingGuid(null);
    setForm(createEmptyForm(defaultDate));
    setTimePicker(null);
    setSaveFailed(false);
    setVerification(null);
  };

  const submit = async () => {
    if (locked || submittingRef.current || validationError) return;
    submittingRef.current = true;
    setSubmitting(true);
    setSaveFailed(false);
    try {
      const { workDates, ...timeFields } = buildAvailabilityBatchPayload(form);
      if (editingGuid) {
        await onUpdate(editingGuid, { ...timeFields, workDate: workDates[0] });
      } else {
        await onCreate({ ...timeFields, workDates });
      }
      // 收到保存成功后才清空草稿；网络或业务失败保留全部选择，便于核对。
      resetForm();
    } catch (error) {
      if (error instanceof AvailabilityBatchSaveError) {
        setForm((current) => ({ ...current, workDates: error.remainingDates }));
        if (error.uncertainDates.length) {
          setVerification({
            payload: { ...buildAvailabilityBatchPayload(form), workDates: error.remainingDates },
            uncertainDates: error.uncertainDates,
          });
        }
      }
      setSaveFailed(true);
    } finally {
      submittingRef.current = false;
      setSubmitting(false);
    }
  };

  const verifySave = async () => {
    if (!verification || busy || submittingRef.current) return;
    submittingRef.current = true;
    setSubmitting(true);
    try {
      // 只读核对未知请求；未确认结果前绝不再次发送新增请求。
      const confirmed = new Set(await onVerify(verification.payload));
      const remainingDates = form.workDates.filter((date) => !confirmed.has(date));
      const uncertainDates = verification.uncertainDates.filter((date) => !confirmed.has(date));
      setForm((current) => ({ ...current, workDates: remainingDates }));
      setVerification(uncertainDates.length ? {
        payload: { ...verification.payload, workDates: remainingDates },
        uncertainDates,
      } : null);
      setSaveFailed(uncertainDates.length > 0);
      if (!remainingDates.length) resetForm();
    } catch {
      setSaveFailed(true);
    } finally {
      submittingRef.current = false;
      setSubmitting(false);
    }
  };

  const beginEdit = (item: AttendanceAvailability) => {
    if (locked) return;
    setEditingGuid(item.availabilityGuid);
    setSaveFailed(false);
    const allDay = isAllDayAvailability(item.startTime, item.endTime);
    setForm({
      workDates: [item.workDate],
      allDay,
      startTime: allDay ? "09:00" : item.startTime.slice(0, 5),
      endTime: allDay ? "17:30" : item.endTime.slice(0, 5),
      note: item.note ?? "",
    });
    onWeekChange(item.workDate);
  };

  return (
    <>
      <Card mode="outlined" style={styles.card}>
        <Card.Content style={styles.content}>
          <Text variant="titleMedium" style={styles.title}>
            {editingGuid ? t("availability.editTitle") : t("sections.availability")}
          </Text>
          <AvailabilityDatePicker
            value={form.workDates}
            onChange={(workDates) => setForm((current) => ({ ...current, workDates }))}
            weekDate={normalizeMonthDate(defaultDate)}
            onWeekChange={onWeekChange}
            disabled={locked}
            singleDate={Boolean(editingGuid)}
          />
          <View style={styles.fieldGroup}>
            <Text variant="labelLarge">{t("availability.timeRange")}</Text>
            <SegmentedButtons
              value={form.allDay ? "allDay" : "specific"}
              onValueChange={(value) => setForm((current) => ({ ...current, allDay: value === "allDay" }))}
              buttons={[
                { value: "allDay", label: t("availability.allDay"), disabled: locked, style: form.allDay ? styles.activeSegment : undefined, checkedColor: HB_COLORS.white },
                { value: "specific", label: t("availability.specificTime"), disabled: locked, style: !form.allDay ? styles.activeSegment : undefined, checkedColor: HB_COLORS.white },
              ]}
              density="small"
            />
            {form.allDay ? (
              <Text variant="bodySmall" style={styles.muted}>{t("availability.allDayHint")}</Text>
            ) : (
              <View style={styles.timeRow}>
                {(["startTime", "endTime"] as const).map((field) => (
                  <Pressable
                    key={field}
                    accessibilityRole="button"
                    accessibilityLabel={`${t(`fields.${field}`)} ${form[field]}`}
                    accessibilityState={{ disabled: locked }}
                    disabled={locked}
                    onPress={() => setTimePicker(field)}
                    style={({ pressed }) => [styles.timeInput, pressed && styles.pressed]}
                  >
                    <Icon source="clock-outline" size={20} color={HB_COLORS.textSecondary} />
                    <View style={styles.timeText}>
                      <Text variant="labelSmall" style={styles.muted}>{t(`fields.${field}`)}</Text>
                      <Text variant="bodyLarge">{form[field]}</Text>
                    </View>
                    <Icon source="chevron-right" size={18} color={HB_COLORS.textSecondary} />
                  </Pressable>
                ))}
              </View>
            )}
            {validationError && validationError !== "datesRequired" ? (
              <Text accessibilityRole="alert" style={styles.error}>{t(`availability.${validationError}`)}</Text>
            ) : null}
          </View>
          <TextInput
            mode="outlined"
            label={t("availability.optionalNote")}
            placeholder={t("availability.notePlaceholder")}
            value={form.note}
            onChangeText={(note) => setForm((current) => ({ ...current, note }))}
            disabled={locked}
            multiline
            maxLength={500}
            style={styles.note}
          />
          {saveFailed ? (
            <Text accessibilityRole="alert" style={styles.error}>
              {verification
                ? t("availability.unconfirmedHint", { dates: verification.uncertainDates.join("、") })
                : t("availability.saveFailedHint")}
            </Text>
          ) : null}
          {verification ? <Button mode="outlined" disabled={busy} onPress={() => void verifySave()}>{t("availability.verifySave")}</Button> : null}
          <Button
            mode="contained"
            onPress={() => void submit()}
            disabled={Boolean(validationError) || locked}
            loading={busy}
            style={styles.saveButton}
            contentStyle={styles.saveButtonContent}
          >
            {editingGuid ? t("common:actions.save") : t("availability.add")}
          </Button>
          <Text variant="bodySmall" style={styles.summary}>
            {t("availability.summary", {
              count: form.workDates.length,
              time: form.allDay ? t("availability.allDay") : `${form.startTime} – ${form.endTime}`,
            })}
          </Text>
          {editingGuid || verification ? <Button onPress={resetForm} disabled={busy}>{t("common:actions.cancel")}</Button> : null}
        </Card.Content>
      </Card>

      <Card mode="outlined" style={styles.card}>
        <Card.Content style={styles.content}>
          <Text variant="titleSmall">{t("availability.submittedTitle")}</Text>
          {activeItems.length ? activeItems.map((item) => (
            <View key={item.availabilityGuid} style={styles.itemRow}>
              <View style={styles.itemText}>
                <Text variant="bodyMedium">{item.workDate}</Text>
                <Text variant="bodySmall" style={styles.muted}>
                  {isAllDayAvailability(item.startTime, item.endTime)
                    ? t("availability.allDay")
                    : `${item.startTime.slice(0, 5)} – ${item.endTime.slice(0, 5)}`}
                  {item.note ? ` · ${item.note}` : ""}
                </Text>
              </View>
              <IconButton icon="pencil-outline" size={20} accessibilityLabel={t("availability.editItem", { date: item.workDate })} onPress={() => beginEdit(item)} disabled={locked} />
              <IconButton icon="close-circle-outline" size={20} accessibilityLabel={t("availability.cancelItem", { date: item.workDate })} onPress={() => onCancel(item.availabilityGuid)} disabled={locked} />
            </View>
          )) : <Text variant="bodyMedium" style={styles.muted}>{t("availability.empty")}</Text>}
        </Card.Content>
      </Card>

      <AvailabilityTimePicker
        visible={timePicker !== null}
        title={t(timePicker === "endTime" ? "availability.chooseEndTime" : "availability.chooseStartTime")}
        value={timePicker ? form[timePicker] : form.startTime}
        caption={t("availability.applyToDates", { count: form.workDates.length })}
        onDismiss={() => setTimePicker(null)}
        onConfirm={(value) => {
          if (timePicker) setForm((current) => ({ ...current, [timePicker]: value }));
          setTimePicker(null);
        }}
      />
    </>
  );
}

const styles = StyleSheet.create({
  card: { backgroundColor: HB_COLORS.white, borderColor: HB_COLORS.outlineMuted, borderRadius: HB_RADIUS.surface, borderWidth: StyleSheet.hairlineWidth, elevation: 0 },
  content: { gap: 14, paddingTop: 16 },
  title: { fontWeight: "700" },
  fieldGroup: { gap: 8 },
  activeSegment: { backgroundColor: HB_COLORS.brand },
  timeRow: { flexDirection: "row", gap: 8 },
  timeInput: { flex: 1, minWidth: 0, minHeight: 62, paddingHorizontal: 10, paddingVertical: 8, borderWidth: 1, borderColor: HB_COLORS.outline, borderRadius: HB_RADIUS.control, flexDirection: "row", alignItems: "center", gap: 8 },
  timeText: { flex: 1, minWidth: 0 },
  pressed: { backgroundColor: HB_COLORS.surfaceMuted },
  note: { backgroundColor: HB_COLORS.white, minHeight: 72 },
  saveButton: { borderRadius: HB_RADIUS.control, backgroundColor: HB_COLORS.brand },
  saveButtonContent: { minHeight: 46 },
  summary: { color: HB_COLORS.textSecondary, textAlign: "center", marginTop: -8 },
  muted: { color: HB_COLORS.textSecondary },
  error: { color: HB_COLORS.danger, fontSize: 13 },
  itemRow: { flexDirection: "row", alignItems: "center", borderTopWidth: StyleSheet.hairlineWidth, borderColor: HB_COLORS.outlineMuted, paddingVertical: 4 },
  itemText: { flex: 1, gap: 4 },
});
