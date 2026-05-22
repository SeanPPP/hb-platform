import { useMemo, useState } from "react";
import { Alert, Pressable, ScrollView, StyleSheet, View } from "react-native";
import { Button, Card, Chip, Dialog, Portal, Text, TextInput } from "react-native-paper";
import type {
  AttendanceSchedule,
  AttendanceSchedulePayload,
  AttendanceScheduleStatus,
  AttendanceScheduleUpdatePayload,
} from "@/modules/attendance/types";
import type { StoreUserListItem } from "@/modules/users/types";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

const CELL_WIDTH = 132;
const ROW_HEADER_WIDTH = 132;
const EMPTY_FORM = {
  startTime: "09:00",
  endTime: "17:00",
  status: "Draft" as AttendanceScheduleStatus,
  remark: "",
};
const STATUSES: AttendanceScheduleStatus[] = ["Draft", "Active", "Cancelled"];

type ScheduleDraft = typeof EMPTY_FORM;

interface EditingTarget {
  schedule?: AttendanceSchedule;
  userGuid: string;
  employeeName?: string;
  workDate: string;
}

interface ScheduleRow {
  userGuid: string;
  employeeName?: string;
  schedules: AttendanceSchedule[];
}

function addDays(date: string, days: number) {
  const parsed = new Date(`${date}T00:00:00`);
  if (Number.isNaN(parsed.getTime())) {
    return "";
  }
  parsed.setDate(parsed.getDate() + days);
  const year = parsed.getFullYear();
  const month = String(parsed.getMonth() + 1).padStart(2, "0");
  const day = String(parsed.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}

function shortDate(value: string) {
  return value ? value.slice(5, 10) : "--";
}

function formatTime(value: string) {
  return value.includes("T") ? value.split("T").pop()?.slice(0, 5) || value : value.slice(0, 5);
}

function getUserDisplayName(user: StoreUserListItem) {
  return user.fullName || user.username || user.userGUID;
}

export function ScheduleManagementCard({
  weekStartDate,
  storeCode,
  storeName,
  users,
  schedules,
  isLoading,
  isBusy,
  onPreviousWeek,
  onNextWeek,
  onCreate,
  onUpdate,
  onDelete,
  onPublishWeek,
}: {
  weekStartDate: string;
  storeCode?: string;
  storeName?: string;
  users: StoreUserListItem[];
  schedules: AttendanceSchedule[];
  isLoading: boolean;
  isBusy: boolean;
  onPreviousWeek: () => void;
  onNextWeek: () => void;
  onCreate: (payload: AttendanceSchedulePayload) => void;
  onUpdate: (scheduleGuid: string, payload: AttendanceScheduleUpdatePayload) => void;
  onDelete: (scheduleGuid: string) => void;
  onPublishWeek: () => void;
}) {
  const { t } = useAppTranslation(["attendance", "common"]);
  const [editingTarget, setEditingTarget] = useState<EditingTarget | null>(null);
  const [form, setForm] = useState<ScheduleDraft>(EMPTY_FORM);

  const days = useMemo(
    () => Array.from({ length: 7 }, (_, index) => addDays(weekStartDate, index)),
    [weekStartDate]
  );

  const rows = useMemo<ScheduleRow[]>(() => {
    const rowMap = new Map<string, ScheduleRow>();
    users.forEach((user) => {
      rowMap.set(user.userGUID, {
        userGuid: user.userGUID,
        employeeName: getUserDisplayName(user),
        schedules: [],
      });
    });

    schedules.forEach((schedule) => {
      const existing = rowMap.get(schedule.userGuid);
      const row = existing ?? {
        userGuid: schedule.userGuid,
        employeeName: schedule.employeeName || schedule.userGuid,
        schedules: [],
      };
      row.schedules = [...row.schedules, schedule];
      rowMap.set(schedule.userGuid, row);
    });

    return Array.from(rowMap.values()).sort((left, right) =>
      (left.employeeName || left.userGuid).localeCompare(right.employeeName || right.userGuid)
    );
  }, [schedules, users]);

  const openCreate = (row: ScheduleRow, workDate: string) => {
    if (!storeCode) {
      return;
    }
    setEditingTarget({
      userGuid: row.userGuid,
      employeeName: row.employeeName,
      workDate,
    });
    setForm(EMPTY_FORM);
  };

  const openEdit = (schedule: AttendanceSchedule, row: ScheduleRow) => {
    setEditingTarget({
      schedule,
      userGuid: row.userGuid,
      employeeName: row.employeeName,
      workDate: schedule.workDate,
    });
    setForm({
      startTime: formatTime(schedule.startTime),
      endTime: formatTime(schedule.endTime),
      status: schedule.status || "Draft",
      remark: schedule.remark ?? "",
    });
  };

  const closeDialog = () => {
    setEditingTarget(null);
    setForm(EMPTY_FORM);
  };

  const submit = () => {
    if (!editingTarget || !storeCode) {
      return;
    }

    const normalized = {
      workDate: editingTarget.workDate,
      startTime: form.startTime.trim(),
      endTime: form.endTime.trim(),
      status: form.status,
      remark: form.remark.trim() || undefined,
    };

    if (editingTarget.schedule?.scheduleGuid) {
      onUpdate(editingTarget.schedule.scheduleGuid, normalized);
    } else {
      onCreate({
        workDate: normalized.workDate,
        startTime: normalized.startTime,
        endTime: normalized.endTime,
        status: "Draft",
        remark: normalized.remark,
        storeCode,
        userGuid: editingTarget.userGuid,
      });
    }
    closeDialog();
  };

  const confirmDelete = () => {
    const scheduleGuid = editingTarget?.schedule?.scheduleGuid;
    if (!scheduleGuid) {
      return;
    }

    Alert.alert(t("scheduleManagement.deleteTitle"), t("scheduleManagement.deleteMessage"), [
      { text: t("common:actions.cancel"), style: "cancel" },
      {
        text: t("scheduleManagement.deleteConfirm"),
        style: "destructive",
        onPress: () => {
          onDelete(scheduleGuid);
          closeDialog();
        },
      },
    ]);
  };

  const canSubmit = Boolean(form.startTime.trim() && form.endTime.trim());

  return (
    <Card mode="elevated" style={styles.card}>
      <Card.Title
        title={t("sections.scheduleManagement")}
        subtitle={storeName || storeCode || t("scheduleManagement.noStore")}
      />
      <Card.Content style={styles.content}>
        <View style={styles.toolbar}>
          <Button compact mode="outlined" icon="chevron-left" onPress={onPreviousWeek} disabled={isBusy}>
            {t("scheduleManagement.previousWeek")}
          </Button>
          <View style={styles.weekTitle}>
            <Text variant="labelLarge">{weekStartDate}</Text>
            <Text variant="bodySmall" style={styles.muted}>
              {days[6] || "--"}
            </Text>
          </View>
          <Button compact mode="outlined" icon="chevron-right" onPress={onNextWeek} disabled={isBusy}>
            {t("scheduleManagement.nextWeek")}
          </Button>
        </View>

        <Button mode="contained" icon="send-clock-outline" onPress={onPublishWeek} loading={isBusy} disabled={isBusy}>
          {t("actions.publishWeek")}
        </Button>

        {!storeCode ? (
          <Text variant="bodyMedium" style={styles.muted}>
            {t("scheduleManagement.noStore")}
          </Text>
        ) : isLoading ? (
          <Text variant="bodyMedium" style={styles.muted}>
            {t("common:loading")}
          </Text>
        ) : rows.length ? (
          <ScrollView horizontal showsHorizontalScrollIndicator contentContainerStyle={styles.tableScroll}>
            <View>
              <View style={styles.tableRow}>
                <View style={[styles.headerCell, styles.rowHeader]}>
                  <Text variant="labelMedium">{t("scheduleManagement.employeeColumn")}</Text>
                </View>
                {days.map((day, index) => (
                  <View key={day || index} style={styles.headerCell}>
                    <Text variant="labelMedium">{t(`weekdays.${index}`)}</Text>
                    <Text variant="bodySmall" style={styles.muted}>
                      {shortDate(day)}
                    </Text>
                  </View>
                ))}
              </View>

              {rows.map((row) => (
                <View key={row.userGuid} style={styles.tableRow}>
                  <View style={[styles.bodyCell, styles.rowHeader]}>
                    <Text variant="bodyMedium" numberOfLines={2}>
                      {row.employeeName || row.userGuid}
                    </Text>
                    <Text variant="bodySmall" style={styles.muted} numberOfLines={1}>
                      {storeName || storeCode}
                    </Text>
                  </View>
                  {days.map((day) => {
                    const cellSchedules = row.schedules.filter((schedule) => schedule.workDate === day);
                    return (
                      <Pressable key={`${row.userGuid}-${day}`} style={styles.bodyCell} onPress={() => openCreate(row, day)}>
                        {cellSchedules.length ? (
                          cellSchedules.map((schedule) => (
                            <Pressable
                              key={schedule.scheduleGuid || `${schedule.workDate}-${schedule.startTime}`}
                              style={[
                                styles.shiftBlock,
                                schedule.status.toLowerCase() === "draft" ? styles.draftShift : styles.activeShift,
                              ]}
                              onPress={() => openEdit(schedule, row)}
                            >
                              <Text variant="labelMedium">
                                {formatTime(schedule.startTime)}-{formatTime(schedule.endTime)}
                              </Text>
                              <Text variant="bodySmall">{t(`statuses.${schedule.status}`, schedule.status)}</Text>
                            </Pressable>
                          ))
                        ) : (
                          <Text variant="bodySmall" style={styles.emptyCellText}>
                            {t("scheduleManagement.tapToAdd")}
                          </Text>
                        )}
                      </Pressable>
                    );
                  })}
                </View>
              ))}
            </View>
          </ScrollView>
        ) : (
          <Text variant="bodyMedium" style={styles.muted}>
            {t("scheduleManagement.noEmployees")}
          </Text>
        )}
      </Card.Content>

      <Portal>
        <Dialog visible={Boolean(editingTarget)} onDismiss={closeDialog}>
          <Dialog.Title>
            {editingTarget?.schedule ? t("scheduleManagement.editTitle") : t("scheduleManagement.createTitle")}
          </Dialog.Title>
          <Dialog.Content style={styles.dialogContent}>
            <Text variant="bodySmall" style={styles.muted}>
              {editingTarget?.employeeName || editingTarget?.userGuid} · {editingTarget?.workDate}
            </Text>
            <View style={styles.timeRow}>
              <TextInput
                mode="outlined"
                label={t("fields.startTime")}
                value={form.startTime}
                placeholder="09:00"
                style={styles.timeInput}
                onChangeText={(value) => setForm((current) => ({ ...current, startTime: value }))}
              />
              <TextInput
                mode="outlined"
                label={t("fields.endTime")}
                value={form.endTime}
                placeholder="17:00"
                style={styles.timeInput}
                onChangeText={(value) => setForm((current) => ({ ...current, endTime: value }))}
              />
            </View>
            <TextInput
              mode="outlined"
              label={t("fields.note")}
              value={form.remark}
              onChangeText={(value) => setForm((current) => ({ ...current, remark: value }))}
            />
            {editingTarget?.schedule ? (
              <View style={styles.statusRow}>
                {STATUSES.map((status) => (
                  <Chip
                    key={status}
                    selected={form.status === status}
                    onPress={() => setForm((current) => ({ ...current, status }))}
                  >
                    {t(`statuses.${status}`, status)}
                  </Chip>
                ))}
              </View>
            ) : null}
          </Dialog.Content>
          <Dialog.Actions>
            {editingTarget?.schedule ? (
              <Button textColor="#B42318" onPress={confirmDelete} disabled={isBusy}>
                {t("scheduleManagement.deleteAction")}
              </Button>
            ) : null}
            <Button onPress={closeDialog} disabled={isBusy}>
              {t("common:actions.cancel")}
            </Button>
            <Button mode="contained" onPress={submit} disabled={!canSubmit || isBusy} loading={isBusy}>
              {t("common:actions.save")}
            </Button>
          </Dialog.Actions>
        </Dialog>
      </Portal>
    </Card>
  );
}

const styles = StyleSheet.create({
  activeShift: {
    backgroundColor: "#E8F5E9",
    borderColor: "#81C784",
  },
  bodyCell: {
    borderColor: "#E5E7EB",
    borderRightWidth: StyleSheet.hairlineWidth,
    borderTopWidth: StyleSheet.hairlineWidth,
    gap: 6,
    minHeight: 82,
    padding: 8,
    width: CELL_WIDTH,
  },
  card: {
    borderRadius: 8,
  },
  content: {
    gap: 12,
  },
  dialogContent: {
    gap: 12,
  },
  draftShift: {
    backgroundColor: "#FFF7E6",
    borderColor: "#FDBA74",
  },
  emptyCellText: {
    color: "#9CA3AF",
  },
  headerCell: {
    backgroundColor: "#F3F4F6",
    borderColor: "#E5E7EB",
    borderRightWidth: StyleSheet.hairlineWidth,
    gap: 2,
    padding: 8,
    width: CELL_WIDTH,
  },
  muted: {
    color: "#6B7280",
  },
  rowHeader: {
    width: ROW_HEADER_WIDTH,
  },
  shiftBlock: {
    borderRadius: 6,
    borderWidth: StyleSheet.hairlineWidth,
    gap: 2,
    paddingHorizontal: 8,
    paddingVertical: 6,
  },
  statusRow: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
  },
  tableRow: {
    flexDirection: "row",
  },
  tableScroll: {
    paddingBottom: 4,
  },
  timeInput: {
    flex: 1,
  },
  timeRow: {
    flexDirection: "row",
    gap: 8,
  },
  toolbar: {
    alignItems: "center",
    flexDirection: "row",
    gap: 8,
    justifyContent: "space-between",
  },
  weekTitle: {
    alignItems: "center",
    flex: 1,
  },
});
