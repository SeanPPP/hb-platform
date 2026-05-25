import { useEffect, useMemo, useState, type ComponentType } from "react";
import { Alert, StyleSheet, View } from "react-native";
import { Button, Card, Chip, HelperText, IconButton, SegmentedButtons, Switch, Text, TextInput } from "react-native-paper";
import { MonthDatePicker as RawMonthDatePicker } from "@/components/attendance/MonthDatePicker";
import type {
  AttendanceHolidayBusinessStatus,
  AttendanceStoreHoliday,
  AttendanceStoreHolidayPayload,
} from "@/modules/attendance/types";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

const BUSINESS_STATUSES: AttendanceHolidayBusinessStatus[] = ["Open", "Closed", "Partial"];
const EMPTY_TIME = "";

interface MonthDatePickerProps {
  value?: string;
  date?: string;
  selectedDate?: string;
  disabled?: boolean;
  onChange?: (date: string) => void;
  onDateChange?: (date: string) => void;
  onSelectDate?: (date: string) => void;
}

const MonthDatePicker = RawMonthDatePicker as ComponentType<MonthDatePickerProps>;

interface HolidayDraft {
  storeCode?: string;
  holidayDate: string;
  holidayName: string;
  businessStatus: AttendanceHolidayBusinessStatus;
  openTime: string;
  closeTime: string;
  isPaidHoliday: boolean;
  remark: string;
}

interface HolidayManagementCardProps {
  holidays: AttendanceStoreHoliday[];
  storeCode?: string;
  storeName?: string;
  isBusy: boolean;
  isSyncBusy?: boolean;
  canSync?: boolean;
  syncDisabledReason?: string;
  selectedDate?: string;
  onCreate: (payload: AttendanceStoreHolidayPayload) => void;
  onUpdate: (holidayGuid: string, payload: AttendanceStoreHolidayPayload) => void;
  onDelete: (holidayGuid: string) => void;
  onSync?: () => void;
}

function createEmptyDraft(storeCode?: string, selectedDate?: string): HolidayDraft {
  return {
    storeCode,
    holidayDate: selectedDate ?? "",
    holidayName: "",
    businessStatus: "Open",
    openTime: EMPTY_TIME,
    closeTime: EMPTY_TIME,
    isPaidHoliday: false,
    remark: "",
  };
}

function normalizeTime(value?: string) {
  return value ? value.slice(0, 5) : EMPTY_TIME;
}

function cleanPayload(form: HolidayDraft): AttendanceStoreHolidayPayload | null {
  const storeCode = form.storeCode?.trim();
  if (!storeCode) {
    return null;
  }

  return {
    storeCode,
    holidayDate: form.holidayDate.trim(),
    holidayName: form.holidayName.trim(),
    businessStatus: form.businessStatus,
    openTime: form.businessStatus === "Closed" ? undefined : form.openTime.trim() || undefined,
    closeTime: form.businessStatus === "Closed" ? undefined : form.closeTime.trim() || undefined,
    isPaidHoliday: form.isPaidHoliday,
    remark: form.remark.trim() || undefined,
  };
}

function sortHolidays(holidays: AttendanceStoreHoliday[]) {
  return [...holidays].sort((left, right) => {
    const byDate = left.holidayDate.localeCompare(right.holidayDate);
    return byDate || left.holidayName.localeCompare(right.holidayName);
  });
}

export function HolidayManagementCard({
  holidays,
  storeCode,
  storeName,
  isBusy,
  isSyncBusy = false,
  canSync = false,
  syncDisabledReason,
  selectedDate,
  onCreate,
  onUpdate,
  onDelete,
  onSync,
}: HolidayManagementCardProps) {
  const { t } = useAppTranslation(["attendance", "common"]);
  const [editingGuid, setEditingGuid] = useState<string | null>(null);
  const [form, setForm] = useState<HolidayDraft>(() => createEmptyDraft(storeCode, selectedDate));

  const sortedHolidays = useMemo(() => sortHolidays(holidays), [holidays]);
  const canEdit = Boolean(storeCode || form.storeCode);
  const canSubmit = Boolean(canEdit && form.holidayDate.trim() && form.holidayName.trim());
  const isClosed = form.businessStatus === "Closed";

  useEffect(() => {
    if (editingGuid) {
      return;
    }
    setForm((current) => ({
      ...current,
      storeCode,
      holidayDate: selectedDate ?? current.holidayDate,
    }));
  }, [editingGuid, selectedDate, storeCode]);

  const setField = <K extends keyof HolidayDraft>(key: K, value: HolidayDraft[K]) => {
    setForm((current) => ({ ...current, [key]: value }));
  };

  const setHolidayDate = (date: string) => {
    setField("holidayDate", date);
  };

  const resetForm = () => {
    setEditingGuid(null);
    setForm(createEmptyDraft(storeCode, selectedDate));
  };

  const beginEdit = (holiday: AttendanceStoreHoliday) => {
    setEditingGuid(holiday.holidayGuid);
    setForm({
      storeCode: holiday.storeCode || storeCode,
      holidayDate: holiday.holidayDate,
      holidayName: holiday.holidayName,
      businessStatus: holiday.businessStatus || "Open",
      openTime: normalizeTime(holiday.openTime),
      closeTime: normalizeTime(holiday.closeTime),
      isPaidHoliday: holiday.isPaidHoliday,
      remark: holiday.remark ?? "",
    });
  };

  const submit = () => {
    const payload = cleanPayload({
      ...form,
      storeCode: form.storeCode || storeCode,
    });
    if (!payload) {
      return;
    }

    if (editingGuid) {
      onUpdate(editingGuid, payload);
    } else {
      onCreate(payload);
    }
    resetForm();
  };

  const confirmDelete = (holiday: AttendanceStoreHoliday) => {
    Alert.alert(
      t("holidayManagement.deleteTitle", { defaultValue: "Delete holiday" }),
      t("holidayManagement.deleteMessage", { defaultValue: "Delete this public holiday?" }),
      [
        { text: t("common:actions.cancel"), style: "cancel" },
        {
          text: t("holidayManagement.deleteConfirm", { defaultValue: "Delete" }),
          style: "destructive",
          onPress: () => {
            onDelete(holiday.holidayGuid);
            if (editingGuid === holiday.holidayGuid) {
              resetForm();
            }
          },
        },
      ]
    );
  };

  return (
    <Card mode="elevated" style={styles.card}>
      <Card.Title
        title={t("sections.holidayManagement", { defaultValue: "Public holidays" })}
        subtitle={storeName || storeCode || t("holidayManagement.noStore", { defaultValue: "Select a store first" })}
      />
      <Card.Content style={styles.content}>
        <View style={styles.formGrid}>
          <MonthDatePicker
            value={form.holidayDate}
            date={form.holidayDate}
            selectedDate={form.holidayDate}
            disabled={isBusy}
            onChange={setHolidayDate}
            onDateChange={setHolidayDate}
            onSelectDate={setHolidayDate}
          />

          <TextInput
            mode="outlined"
            label={t("holidayManagement.fields.name", { defaultValue: "Holiday name" })}
            value={form.holidayName}
            onChangeText={(value) => setField("holidayName", value)}
            disabled={isBusy}
          />

          <SegmentedButtons
            value={form.businessStatus}
            onValueChange={(value) =>
              setForm((current) => ({
                ...current,
                businessStatus: value as AttendanceHolidayBusinessStatus,
                openTime: value === "Closed" ? EMPTY_TIME : current.openTime,
                closeTime: value === "Closed" ? EMPTY_TIME : current.closeTime,
              }))
            }
            buttons={BUSINESS_STATUSES.map((status) => ({
              value: status,
              label: t(`holidayManagement.businessStatuses.${status}`, { defaultValue: status }),
              disabled: isBusy,
            }))}
          />

          <View style={styles.timeRow}>
            <TextInput
              mode="outlined"
              label={t("fields.startTime")}
              value={form.openTime}
              placeholder="09:00"
              style={styles.timeInput}
              onChangeText={(value) => setField("openTime", value)}
              disabled={isBusy || isClosed}
            />
            <TextInput
              mode="outlined"
              label={t("fields.endTime")}
              value={form.closeTime}
              placeholder="17:00"
              style={styles.timeInput}
              onChangeText={(value) => setField("closeTime", value)}
              disabled={isBusy || isClosed}
            />
          </View>

          <View style={styles.switchRow}>
            <View style={styles.switchText}>
              <Text variant="bodyMedium">
                {t("holidayManagement.fields.paidHoliday", { defaultValue: "Paid holiday" })}
              </Text>
            </View>
            <Switch
              value={form.isPaidHoliday}
              onValueChange={(value) => setField("isPaidHoliday", value)}
              disabled={isBusy}
            />
          </View>

          <TextInput
            mode="outlined"
            label={t("fields.note")}
            value={form.remark}
            multiline
            onChangeText={(value) => setField("remark", value)}
            disabled={isBusy}
          />

          <HelperText type="error" visible={!canEdit}>
            {t("holidayManagement.noStore", { defaultValue: "Select a store first" })}
          </HelperText>
        </View>

        <View style={styles.actions}>
          {onSync ? (
            <Button
              mode="outlined"
              icon="refresh"
              onPress={onSync}
              disabled={!canSync || isBusy || isSyncBusy}
              loading={isSyncBusy}
            >
              {t("holidayManagement.syncAction", {
                defaultValue: "Sync next 30 days",
              })}
            </Button>
          ) : null}
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
              : t("holidayManagement.addAction", { defaultValue: "Add holiday" })}
          </Button>
        </View>
        <HelperText type="info" visible={Boolean(syncDisabledReason)}>
          {syncDisabledReason || " "}
        </HelperText>

        <View style={styles.list}>
          {sortedHolidays.length ? (
            sortedHolidays.map((holiday) => (
              <View key={holiday.holidayGuid || `${holiday.storeCode}-${holiday.holidayDate}`} style={styles.itemRow}>
                <View style={styles.itemText}>
                  <View style={styles.itemHeader}>
                    <Text variant="bodyMedium" numberOfLines={1} style={styles.itemName}>
                      {holiday.holidayDate} - {holiday.holidayName}
                    </Text>
                    <Chip compact>{t(`holidayManagement.businessStatuses.${holiday.businessStatus}`, { defaultValue: holiday.businessStatus })}</Chip>
                  </View>
                  <Text variant="bodySmall" style={styles.muted} numberOfLines={2}>
                    {holiday.businessStatus === "Closed"
                      ? t("holidayManagement.closedAllDay", { defaultValue: "Closed all day" })
                      : `${normalizeTime(holiday.openTime) || "--:--"} - ${normalizeTime(holiday.closeTime) || "--:--"}`}
                    {holiday.isPaidHoliday
                      ? ` - ${t("holidayManagement.paidLabel", { defaultValue: "Paid" })}`
                      : ""}
                    {holiday.remark ? ` - ${holiday.remark}` : ""}
                  </Text>
                </View>
                <IconButton icon="pencil-outline" size={20} onPress={() => beginEdit(holiday)} disabled={isBusy} />
                <IconButton icon="delete-outline" size={20} iconColor="#B42318" onPress={() => confirmDelete(holiday)} disabled={isBusy} />
              </View>
            ))
          ) : (
            <Text variant="bodyMedium" style={styles.muted}>
              {t("holidayManagement.empty", { defaultValue: "No public holidays for this store." })}
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
  itemHeader: {
    alignItems: "center",
    flexDirection: "row",
    gap: 8,
  },
  itemName: {
    flex: 1,
  },
  itemRow: {
    alignItems: "center",
    borderColor: "#E5E7EB",
    borderRadius: 6,
    borderWidth: StyleSheet.hairlineWidth,
    flexDirection: "row",
    gap: 4,
    paddingLeft: 10,
  },
  itemText: {
    flex: 1,
    gap: 4,
    paddingVertical: 10,
  },
  list: {
    gap: 8,
  },
  muted: {
    color: "#6B7280",
  },
  switchRow: {
    alignItems: "center",
    borderColor: "#E5E7EB",
    borderRadius: 6,
    borderWidth: StyleSheet.hairlineWidth,
    flexDirection: "row",
    gap: 12,
    paddingHorizontal: 12,
    paddingVertical: 10,
  },
  switchText: {
    flex: 1,
  },
  timeInput: {
    flex: 1,
  },
  timeRow: {
    flexDirection: "row",
    gap: 8,
  },
});
