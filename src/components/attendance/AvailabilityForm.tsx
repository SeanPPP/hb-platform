import { useEffect, useMemo, useState } from "react";
import { StyleSheet, View } from "react-native";
import { Button, Card, IconButton, Text, TextInput } from "react-native-paper";
import {
  MonthDatePickerField,
  normalizeMonthDate,
} from "@/components/attendance/MonthDatePicker";
import type {
  AttendanceAvailability,
  AttendanceAvailabilityPayload,
} from "@/modules/attendance/types";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

function createEmptyForm(defaultDate?: string): AttendanceAvailabilityPayload {
  return {
    workDate: normalizeMonthDate(defaultDate),
    startTime: "",
    endTime: "",
    note: "",
  };
}

export function AvailabilityForm({
  availability,
  defaultDate,
  isBusy,
  onCreate,
  onUpdate,
  onCancel,
}: {
  availability: AttendanceAvailability[];
  defaultDate?: string;
  isBusy: boolean;
  onCreate: (payload: AttendanceAvailabilityPayload) => void;
  onUpdate: (availabilityGuid: string, payload: AttendanceAvailabilityPayload) => void;
  onCancel: (availabilityGuid: string) => void;
}) {
  const { t } = useAppTranslation(["attendance", "common"]);
  const [editingGuid, setEditingGuid] = useState<string | null>(null);
  const [form, setForm] = useState<AttendanceAvailabilityPayload>(() =>
    createEmptyForm(defaultDate),
  );

  const activeItems = useMemo(
    () =>
      availability.filter((item) => item.status.toLowerCase() !== "cancelled"),
    [availability],
  );

  useEffect(() => {
    if (editingGuid) {
      return;
    }

    const nextDate = normalizeMonthDate(defaultDate);
    setForm((current) =>
      current.workDate === nextDate
        ? current
        : { ...current, workDate: nextDate },
    );
  }, [defaultDate, editingGuid]);

  const setField = (
    key: keyof AttendanceAvailabilityPayload,
    value: string,
  ) => {
    setForm((current) => ({ ...current, [key]: value }));
  };

  const resetForm = () => {
    setEditingGuid(null);
    setForm(createEmptyForm(defaultDate));
  };

  const submit = () => {
    const payload = {
      ...form,
      workDate: form.workDate.trim(),
      startTime: form.startTime.trim(),
      endTime: form.endTime.trim(),
      note: form.note?.trim() || undefined,
    };
    if (editingGuid) {
      onUpdate(editingGuid, payload);
    } else {
      onCreate(payload);
    }
    resetForm();
  };

  const beginEdit = (item: AttendanceAvailability) => {
    setEditingGuid(item.availabilityGuid);
    setForm({
      storeCode: item.storeCode,
      workDate: item.workDate,
      startTime: item.startTime,
      endTime: item.endTime,
      note: item.note ?? "",
    });
  };

  const canSubmit = Boolean(
    form.workDate.trim() && form.startTime.trim() && form.endTime.trim(),
  );

  return (
    <Card mode="elevated" style={styles.card}>
      <Card.Title title={t("sections.availability")} />
      <Card.Content style={styles.content}>
        <View style={styles.formGrid}>
          <MonthDatePickerField
            value={form.workDate}
            onChange={(value) => setField("workDate", value)}
            disabled={isBusy}
            label={t("fields.workDate")}
          />
          <View style={styles.timeRow}>
            <TextInput
              mode="outlined"
              label={t("fields.startTime")}
              value={form.startTime}
              placeholder="09:00"
              style={styles.timeInput}
              onChangeText={(value) => setField("startTime", value)}
            />
            <TextInput
              mode="outlined"
              label={t("fields.endTime")}
              value={form.endTime}
              placeholder="17:00"
              style={styles.timeInput}
              onChangeText={(value) => setField("endTime", value)}
            />
          </View>
          <TextInput
            mode="outlined"
            label={t("fields.note")}
            value={form.note}
            onChangeText={(value) => setField("note", value)}
          />
        </View>
        <View style={styles.actions}>
          {editingGuid ? (
            <Button mode="outlined" onPress={resetForm} disabled={isBusy}>
              {t("common:actions.cancel")}
            </Button>
          ) : null}
          <Button
            mode="contained"
            icon={editingGuid ? "content-save-outline" : "plus"}
            onPress={submit}
            disabled={!canSubmit || isBusy}
            loading={isBusy}
          >
            {editingGuid
              ? t("common:actions.save")
              : t("actions.addAvailability")}
          </Button>
        </View>

        <View style={styles.list}>
          {activeItems.length ? (
            activeItems.map((item) => (
              <View key={item.availabilityGuid} style={styles.itemRow}>
                <View style={styles.itemText}>
                  <Text variant="bodyMedium">{item.workDate}</Text>
                  <Text variant="bodySmall" style={styles.muted}>
                    {item.startTime.slice(0, 5)} - {item.endTime.slice(0, 5)}
                    {item.note ? ` · ${item.note}` : ""}
                  </Text>
                </View>
                <IconButton icon="pencil-outline" size={20} onPress={() => beginEdit(item)} disabled={isBusy} />
                <IconButton icon="close-circle-outline" size={20} onPress={() => onCancel(item.availabilityGuid)} disabled={isBusy} />
              </View>
            ))
          ) : (
            <Text variant="bodyMedium" style={styles.muted}>
              {t("availability.empty")}
            </Text>
          )}
        </View>
      </Card.Content>
    </Card>
  );
}

const styles = StyleSheet.create({
  actions: {
    alignItems: "center",
    flexDirection: "row",
    gap: 8,
    justifyContent: "flex-end",
  },
  card: {
    borderRadius: 8,
  },
  content: {
    gap: 12,
  },
  formGrid: {
    gap: 8,
  },
  itemRow: {
    alignItems: "center",
    borderColor: "#E5E7EB",
    borderRadius: 6,
    borderWidth: StyleSheet.hairlineWidth,
    flexDirection: "row",
    paddingLeft: 10,
  },
  itemText: {
    flex: 1,
  },
  list: {
    gap: 8,
  },
  muted: {
    color: "#6B7280",
  },
  timeInput: {
    flex: 1,
  },
  timeRow: {
    flexDirection: "row",
    gap: 8,
  },
});
