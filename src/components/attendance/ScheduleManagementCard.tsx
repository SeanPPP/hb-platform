import { useEffect, useMemo, useRef, useState } from "react";
import * as ScreenOrientation from "expo-screen-orientation";
import {
  Alert,
  Modal,
  Pressable,
  ScrollView,
  StyleSheet,
  View,
} from "react-native";
import {
  Button,
  Card,
  Chip,
  Dialog,
  IconButton,
  Portal,
  Text,
  TextInput,
} from "react-native-paper";
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
const TIME_OPTION_HEIGHT = 36;
const TIME_HOURS = Array.from({ length: 24 }, (_, index) =>
  String(index).padStart(2, "0"),
);
const TIME_MINUTES = Array.from({ length: 60 }, (_, index) =>
  String(index).padStart(2, "0"),
);
const QUICK_SHIFTS = [
  { key: "nineToFiveThirty", startTime: "09:00", endTime: "17:30" },
  { key: "tenToFour", startTime: "10:00", endTime: "16:00" },
  { key: "nineToSeven", startTime: "09:00", endTime: "19:00" },
];
const EMPTY_FORM = {
  startTime: "09:00",
  endTime: "17:30",
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

function formatWeekRange(start: string, end: string) {
  if (!start) {
    return "--";
  }
  if (!end) {
    return start;
  }
  return `${start} - ${end}`;
}

function formatTime(value: string) {
  return value.includes("T")
    ? value.split("T").pop()?.slice(0, 5) || value
    : value.slice(0, 5);
}

function calculateDurationHours(startTime: string, endTime: string) {
  const [startHour, startMinute] = formatTime(startTime).split(":").map(Number);
  const [endHour, endMinute] = formatTime(endTime).split(":").map(Number);

  if (
    [startHour, startMinute, endHour, endMinute].some((value) =>
      Number.isNaN(value),
    )
  ) {
    return 0;
  }

  const startTotal = startHour * 60 + startMinute;
  const endTotal = endHour * 60 + endMinute;
  const duration = endTotal - startTotal;

  return duration > 0 ? duration / 60 : 0;
}

function splitTimeValue(value: string) {
  const [rawHour, rawMinute] = formatTime(value).split(":");
  const hour = TIME_HOURS.includes(rawHour) ? rawHour : "09";
  const minute = TIME_MINUTES.includes(rawMinute) ? rawMinute : "00";
  return { hour, minute };
}

function setTimePart(
  value: string,
  part: "hour" | "minute",
  nextValue: string,
) {
  const current = splitTimeValue(value);
  return part === "hour"
    ? `${nextValue}:${current.minute}`
    : `${current.hour}:${nextValue}`;
}

function getUserDisplayName(user: StoreUserListItem) {
  return user.fullName || user.username || user.userGUID;
}

function TimeScrollPicker({
  label,
  hourLabel,
  minuteLabel,
  value,
  onChange,
}: {
  label: string;
  hourLabel: string;
  minuteLabel: string;
  value: string;
  onChange: (value: string) => void;
}) {
  const { hour, minute } = splitTimeValue(value);
  const hourScrollRef = useRef<ScrollView>(null);
  const minuteScrollRef = useRef<ScrollView>(null);

  useEffect(() => {
    hourScrollRef.current?.scrollTo({
      y: TIME_HOURS.indexOf(hour) * TIME_OPTION_HEIGHT,
      animated: false,
    });
    minuteScrollRef.current?.scrollTo({
      y: TIME_MINUTES.indexOf(minute) * TIME_OPTION_HEIGHT,
      animated: false,
    });
  }, [hour, minute]);

  return (
    <View style={styles.timePicker}>
      <Text variant="labelMedium">{label}</Text>
      <Text variant="titleMedium" style={styles.timePickerValue}>
        {hour}:{minute}
      </Text>
      <View style={styles.timePickerHeader}>
        <Text variant="labelSmall" style={styles.timePickerHeaderText}>
          {hourLabel}
        </Text>
        <Text variant="labelSmall" style={styles.timePickerHeaderText}>
          {minuteLabel}
        </Text>
      </View>
      <View style={styles.timePickerColumns}>
        <ScrollView
          ref={hourScrollRef}
          style={styles.timeColumn}
          showsVerticalScrollIndicator
          nestedScrollEnabled
        >
          {TIME_HOURS.map((item) => (
            <Pressable
              key={item}
              accessibilityRole="button"
              style={[
                styles.timeOption,
                hour === item ? styles.timeOptionSelected : null,
              ]}
              onPress={() => onChange(setTimePart(value, "hour", item))}
            >
              <Text
                variant="labelLarge"
                style={hour === item ? styles.timeOptionTextSelected : null}
              >
                {item}
              </Text>
            </Pressable>
          ))}
        </ScrollView>
        <Text variant="titleMedium" style={styles.timeSeparator}>
          :
        </Text>
        <ScrollView
          ref={minuteScrollRef}
          style={styles.timeColumn}
          showsVerticalScrollIndicator
          nestedScrollEnabled
        >
          {TIME_MINUTES.map((item) => (
            <Pressable
              key={item}
              accessibilityRole="button"
              style={[
                styles.timeOption,
                minute === item ? styles.timeOptionSelected : null,
              ]}
              onPress={() => onChange(setTimePart(value, "minute", item))}
            >
              <Text
                variant="labelLarge"
                style={minute === item ? styles.timeOptionTextSelected : null}
              >
                {item}
              </Text>
            </Pressable>
          ))}
        </ScrollView>
      </View>
    </View>
  );
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
  onUpdate: (
    scheduleGuid: string,
    payload: AttendanceScheduleUpdatePayload,
  ) => void;
  onDelete: (scheduleGuid: string) => void;
  onPublishWeek: () => void;
}) {
  const { t } = useAppTranslation(["attendance", "common"]);
  const [editingTarget, setEditingTarget] = useState<EditingTarget | null>(
    null,
  );
  const [form, setForm] = useState<ScheduleDraft>(EMPTY_FORM);
  const [isFullscreen, setIsFullscreen] = useState(false);

  useEffect(() => {
    if (!isFullscreen) {
      return;
    }

    ScreenOrientation.lockAsync(
      ScreenOrientation.OrientationLock.LANDSCAPE,
    ).catch((error) =>
      console.warn("[schedule-management] landscape lock failed", error),
    );

    return () => {
      ScreenOrientation.lockAsync(
        ScreenOrientation.OrientationLock.PORTRAIT_UP,
      ).catch((error) =>
        console.warn("[schedule-management] portrait lock failed", error),
      );
    };
  }, [isFullscreen]);

  const days = useMemo(
    () =>
      Array.from({ length: 7 }, (_, index) => addDays(weekStartDate, index)),
    [weekStartDate],
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
      (left.employeeName || left.userGuid).localeCompare(
        right.employeeName || right.userGuid,
      ),
    );
  }, [schedules, users]);

  const totalPlannedHours = useMemo(
    () =>
      schedules.reduce(
        (sum, schedule) =>
          sum + calculateDurationHours(schedule.startTime, schedule.endTime),
        0,
      ),
    [schedules],
  );
  const weekEndDate = days[6] || "";

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

  const openDefaultCreate = () => {
    const firstRow = rows[0];
    const firstDay = days[0];
    if (!storeCode || !firstRow || !firstDay) {
      return;
    }
    openCreate(firstRow, firstDay);
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

    Alert.alert(
      t("scheduleManagement.deleteTitle"),
      t("scheduleManagement.deleteMessage"),
      [
        { text: t("common:actions.cancel"), style: "cancel" },
        {
          text: t("scheduleManagement.deleteConfirm"),
          style: "destructive",
          onPress: () => {
            onDelete(scheduleGuid);
            closeDialog();
          },
        },
      ],
    );
  };

  const canSubmit = Boolean(form.startTime.trim() && form.endTime.trim());
  const canCreateDefault = Boolean(storeCode && rows.length && days[0]);

  const weekRangeLabel = formatWeekRange(weekStartDate, weekEndDate);
  const storeLabel = storeName || storeCode || t("scheduleManagement.noStore");
  const plannedHoursLabel = Number.isInteger(totalPlannedHours)
    ? String(totalPlannedHours)
    : totalPlannedHours.toFixed(1);

  const renderScheduleTable = (fullscreen = false) => (
    <ScrollView
      horizontal
      showsHorizontalScrollIndicator
      contentContainerStyle={[
        styles.tableScroll,
        fullscreen ? styles.fullscreenTableScroll : null,
      ]}
    >
      <View
        style={[styles.tableWrap, fullscreen ? styles.fullscreenTable : null]}
      >
        <View style={styles.tableRow}>
          <View style={[styles.headerCell, styles.rowHeader]}>
            <Text variant="labelMedium">
              {t("scheduleManagement.employeeColumn")}
            </Text>
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
              <Text
                variant="bodyMedium"
                numberOfLines={2}
                style={styles.employeeName}
              >
                {row.employeeName || row.userGuid}
              </Text>
              <Text variant="bodySmall" style={styles.muted} numberOfLines={1}>
                {storeName || storeCode}
              </Text>
            </View>
            {days.map((day) => {
              const cellSchedules = row.schedules.filter(
                (schedule) => schedule.workDate === day,
              );
              return (
                <Pressable
                  key={`${row.userGuid}-${day}`}
                  style={styles.bodyCell}
                  onPress={() => openCreate(row, day)}
                >
                  {cellSchedules.length ? (
                    cellSchedules.map((schedule) => (
                      <Pressable
                        key={
                          schedule.scheduleGuid ||
                          `${schedule.workDate}-${schedule.startTime}`
                        }
                        style={[
                          styles.shiftBlock,
                          schedule.status.toLowerCase() === "draft"
                            ? styles.draftShift
                            : schedule.status.toLowerCase() === "cancelled"
                              ? styles.cancelledShift
                              : styles.activeShift,
                        ]}
                        onPress={() => openEdit(schedule, row)}
                      >
                        <Text variant="labelMedium" style={styles.shiftTime}>
                          {formatTime(schedule.startTime)}
                        </Text>
                        <Text
                          variant="labelMedium"
                          style={styles.shiftTimeSecondary}
                        >
                          {formatTime(schedule.endTime)}
                        </Text>
                        <Text variant="bodySmall" style={styles.shiftStatus}>
                          {t(`statuses.${schedule.status}`, schedule.status)}
                        </Text>
                      </Pressable>
                    ))
                  ) : (
                    <View style={styles.emptyCell}>
                      <Text variant="bodySmall" style={styles.emptyCellText}>
                        {t("scheduleManagement.restDay")}
                      </Text>
                    </View>
                  )}
                </Pressable>
              );
            })}
          </View>
        ))}
      </View>
    </ScrollView>
  );

  const renderScheduleArea = (fullscreen = false) => {
    if (!storeCode) {
      return (
        <Text variant="bodyMedium" style={styles.muted}>
          {t("scheduleManagement.noStore")}
        </Text>
      );
    }

    if (isLoading) {
      return (
        <Text variant="bodyMedium" style={styles.muted}>
          {t("common:loading")}
        </Text>
      );
    }

    if (!rows.length) {
      return (
        <Text variant="bodyMedium" style={styles.muted}>
          {t("scheduleManagement.noEmployees")}
        </Text>
      );
    }

    return (
      <>
        {fullscreen ? (
          <View style={styles.fullscreenActionRow}>
            <Button
              compact
              mode="contained-tonal"
              icon="calendar-plus"
              onPress={openDefaultCreate}
              disabled={isBusy || !canCreateDefault}
            >
              {t("actions.createSchedule")}
            </Button>
            <Text variant="bodySmall" style={styles.muted}>
              {rows.length} {t("scheduleManagement.stats.peopleUnit")} ·{" "}
              {plannedHoursLabel} {t("scheduleManagement.stats.hoursUnit")}
            </Text>
          </View>
        ) : (
          <View style={styles.quickCreateRow}>
            <View style={styles.quickCreateText}>
              <Text variant="titleSmall">
                {t("scheduleManagement.quickEntryTitle")}
              </Text>
              <Text variant="bodySmall" style={styles.muted}>
                {t("scheduleManagement.quickEntrySubtitle")}
              </Text>
            </View>
            <Button
              mode="contained"
              icon="calendar-plus"
              onPress={openDefaultCreate}
              disabled={isBusy || !canCreateDefault}
              style={styles.createButton}
            >
              {t("actions.createSchedule")}
            </Button>
          </View>
        )}
        {renderScheduleTable(fullscreen)}
      </>
    );
  };

  const renderEditorDialog = () => (
    <Dialog visible={Boolean(editingTarget)} onDismiss={closeDialog}>
      <Dialog.Title>
        {editingTarget?.schedule
          ? t("scheduleManagement.editTitle")
          : t("scheduleManagement.createTitle")}
      </Dialog.Title>
      <Dialog.Content style={styles.dialogContent}>
        <Text variant="bodySmall" style={styles.muted}>
          {editingTarget?.employeeName || editingTarget?.userGuid} ·{" "}
          {editingTarget?.workDate}
        </Text>
        <View style={styles.quickShiftRow}>
          <Text variant="labelMedium" style={styles.quickShiftTitle}>
            {t("scheduleManagement.quickShiftTitle")}
          </Text>
          {QUICK_SHIFTS.map((shift) => {
            const selected =
              form.startTime === shift.startTime &&
              form.endTime === shift.endTime;
            const label = t(`scheduleManagement.quickShifts.${shift.key}`);
            return (
              <Chip
                key={shift.key}
                compact
                selected={selected}
                onPress={() =>
                  setForm((current) => ({
                    ...current,
                    startTime: shift.startTime,
                    endTime: shift.endTime,
                  }))
                }
              >
                {label}
              </Chip>
            );
          })}
        </View>
        <View style={styles.timeRow}>
          <TimeScrollPicker
            label={t("fields.startTime")}
            hourLabel={t("scheduleManagement.timePicker.hour")}
            minuteLabel={t("scheduleManagement.timePicker.minute")}
            value={form.startTime}
            onChange={(value) =>
              setForm((current) => ({ ...current, startTime: value }))
            }
          />
          <TimeScrollPicker
            label={t("fields.endTime")}
            hourLabel={t("scheduleManagement.timePicker.hour")}
            minuteLabel={t("scheduleManagement.timePicker.minute")}
            value={form.endTime}
            onChange={(value) =>
              setForm((current) => ({ ...current, endTime: value }))
            }
          />
        </View>
        <TextInput
          mode="outlined"
          label={t("fields.note")}
          value={form.remark}
          onChangeText={(value) =>
            setForm((current) => ({ ...current, remark: value }))
          }
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
        <Button
          mode="contained"
          onPress={submit}
          disabled={!canSubmit || isBusy}
          loading={isBusy}
        >
          {t("common:actions.save")}
        </Button>
      </Dialog.Actions>
    </Dialog>
  );

  return (
    <>
      <Card mode="elevated" style={styles.card}>
        <Card.Title
          title={t("sections.scheduleManagement")}
          subtitle={storeLabel}
          right={(props) => (
            <IconButton
              {...props}
              icon="fullscreen"
              accessibilityLabel={t("scheduleManagement.fullscreen")}
              onPress={() => setIsFullscreen(true)}
            />
          )}
        />
        <Card.Content style={styles.content}>
          <View style={styles.toolbar}>
            <Button
              compact
              mode="outlined"
              icon="chevron-left"
              onPress={onPreviousWeek}
              disabled={isBusy}
            >
              {t("scheduleManagement.previousWeek")}
            </Button>
            <View style={styles.weekTitle}>
              <Text variant="titleMedium">{weekRangeLabel}</Text>
              <Text variant="bodySmall" style={styles.muted}>
                {storeLabel}
              </Text>
            </View>
            <Button
              compact
              mode="outlined"
              icon="chevron-right"
              onPress={onNextWeek}
              disabled={isBusy}
            >
              {t("scheduleManagement.nextWeek")}
            </Button>
          </View>

          <View style={styles.summaryRow}>
            <View style={styles.summaryCard}>
              <Text variant="labelMedium" style={styles.muted}>
                {t("scheduleManagement.stats.totalPeople")}
              </Text>
              <View style={styles.summaryValueRow}>
                <Text variant="headlineSmall">{rows.length}</Text>
                <Text variant="bodySmall" style={styles.muted}>
                  {t("scheduleManagement.stats.peopleUnit")}
                </Text>
              </View>
            </View>
            <View style={styles.summaryCard}>
              <Text variant="labelMedium" style={styles.muted}>
                {t("scheduleManagement.stats.plannedHours")}
              </Text>
              <View style={styles.summaryValueRow}>
                <Text variant="headlineSmall">{plannedHoursLabel}</Text>
                <Text variant="bodySmall" style={styles.muted}>
                  {t("scheduleManagement.stats.hoursUnit")}
                </Text>
              </View>
            </View>
          </View>

          <View style={styles.publishPanel}>
            <View style={styles.publishText}>
              <Text variant="titleSmall" style={styles.publishTitle}>
                {t("scheduleManagement.publishTitle")}
              </Text>
              <Text variant="bodySmall" style={styles.publishSubtitle}>
                {t("scheduleManagement.publishSubtitle")}
              </Text>
            </View>
            <Button
              mode="contained"
              icon="send-clock-outline"
              onPress={onPublishWeek}
              loading={isBusy}
              disabled={isBusy}
              buttonColor="#EA580C"
              textColor="#FFFFFF"
            >
              {t("actions.publishWeek")}
            </Button>
          </View>

          {renderScheduleArea()}
        </Card.Content>
      </Card>

      <Modal
        animationType="slide"
        onRequestClose={() => setIsFullscreen(false)}
        presentationStyle="fullScreen"
        supportedOrientations={["landscape", "portrait"]}
        visible={isFullscreen}
      >
        <Portal.Host>
          <View style={styles.fullscreenRoot}>
            <View style={styles.fullscreenHeader}>
              <IconButton
                icon="close"
                accessibilityLabel={t("scheduleManagement.exitFullscreen")}
                onPress={() => setIsFullscreen(false)}
              />
              <View style={styles.fullscreenTitleBlock}>
                <Text variant="titleLarge">
                  {t("scheduleManagement.fullscreenTitle")}
                </Text>
                <Text variant="bodyMedium" style={styles.muted}>
                  {storeLabel} · {weekRangeLabel}
                </Text>
              </View>
              <View style={styles.fullscreenControls}>
                <Button
                  compact
                  mode="outlined"
                  icon="chevron-left"
                  onPress={onPreviousWeek}
                  disabled={isBusy}
                >
                  {t("scheduleManagement.previousWeek")}
                </Button>
                <Button
                  compact
                  mode="outlined"
                  icon="chevron-right"
                  onPress={onNextWeek}
                  disabled={isBusy}
                >
                  {t("scheduleManagement.nextWeek")}
                </Button>
                <Button
                  compact
                  mode="contained"
                  icon="send-clock-outline"
                  onPress={onPublishWeek}
                  loading={isBusy}
                  disabled={isBusy}
                  buttonColor="#EA580C"
                  textColor="#FFFFFF"
                >
                  {t("actions.publishWeek")}
                </Button>
              </View>
            </View>
            <ScrollView
              style={styles.fullscreenBody}
              contentContainerStyle={styles.fullscreenBodyContent}
            >
              {renderScheduleArea(true)}
            </ScrollView>
          </View>
          <Portal>{renderEditorDialog()}</Portal>
        </Portal.Host>
      </Modal>

      <Portal>{isFullscreen ? null : renderEditorDialog()}</Portal>
    </>
  );
}

const styles = StyleSheet.create({
  activeShift: {
    backgroundColor: "#F3F4F6",
    borderLeftColor: "#34D399",
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
  cancelledShift: {
    backgroundColor: "#FFF1F2",
    borderLeftColor: "#FCA5A5",
  },
  createButton: {
    borderRadius: 12,
  },
  dialogContent: {
    gap: 12,
  },
  draftShift: {
    backgroundColor: "#FFF7ED",
    borderLeftColor: "#FB923C",
  },
  employeeName: {
    fontWeight: "600",
  },
  emptyCell: {
    alignItems: "center",
    backgroundColor: "#F8FAFC",
    borderColor: "#E5E7EB",
    borderRadius: 8,
    borderStyle: "dashed",
    borderWidth: StyleSheet.hairlineWidth,
    flex: 1,
    justifyContent: "center",
    minHeight: 56,
  },
  emptyCellText: {
    color: "#9CA3AF",
  },
  fullscreenActionRow: {
    alignItems: "center",
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 12,
    justifyContent: "space-between",
  },
  fullscreenBody: {
    flex: 1,
  },
  fullscreenBodyContent: {
    gap: 10,
    padding: 12,
    paddingBottom: 24,
  },
  fullscreenControls: {
    alignItems: "center",
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
    justifyContent: "flex-end",
  },
  fullscreenHeader: {
    alignItems: "center",
    backgroundColor: "#FFFFFF",
    borderBottomColor: "#E5E7EB",
    borderBottomWidth: StyleSheet.hairlineWidth,
    flexDirection: "row",
    gap: 8,
    paddingHorizontal: 12,
    paddingVertical: 8,
  },
  fullscreenRoot: {
    backgroundColor: "#F5F7FB",
    flex: 1,
  },
  fullscreenTable: {
    alignSelf: "flex-start",
  },
  fullscreenTableScroll: {
    paddingBottom: 12,
  },
  fullscreenTitleBlock: {
    flex: 1,
    minWidth: 220,
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
  publishPanel: {
    alignItems: "center",
    backgroundColor: "#FFF7ED",
    borderColor: "#FDBA74",
    borderRadius: 8,
    borderWidth: StyleSheet.hairlineWidth,
    flexDirection: "row",
    gap: 12,
    justifyContent: "space-between",
    padding: 12,
  },
  publishSubtitle: {
    color: "#9A3412",
  },
  publishText: {
    flex: 1,
    gap: 2,
  },
  publishTitle: {
    color: "#C2410C",
  },
  quickShiftRow: {
    alignItems: "center",
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
  },
  quickShiftTitle: {
    color: "#374151",
  },
  quickCreateRow: {
    alignItems: "center",
    flexDirection: "row",
    gap: 12,
    justifyContent: "space-between",
  },
  quickCreateText: {
    flex: 1,
    gap: 2,
  },
  rowHeader: {
    width: ROW_HEADER_WIDTH,
  },
  shiftBlock: {
    borderLeftWidth: 3,
    borderRadius: 8,
    gap: 2,
    minHeight: 64,
    paddingHorizontal: 8,
    paddingVertical: 8,
  },
  shiftStatus: {
    color: "#6B7280",
  },
  shiftTime: {
    color: "#111827",
    fontWeight: "700",
  },
  shiftTimeSecondary: {
    color: "#4B5563",
  },
  statusRow: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
  },
  summaryCard: {
    backgroundColor: "#FFFFFF",
    borderColor: "#E5E7EB",
    borderRadius: 12,
    borderWidth: StyleSheet.hairlineWidth,
    flex: 1,
    gap: 6,
    paddingHorizontal: 12,
    paddingVertical: 14,
  },
  summaryRow: {
    flexDirection: "row",
    gap: 10,
  },
  summaryValueRow: {
    alignItems: "flex-end",
    flexDirection: "row",
    gap: 4,
  },
  tableRow: {
    flexDirection: "row",
  },
  tableScroll: {
    paddingBottom: 4,
  },
  tableWrap: {
    backgroundColor: "#FFFFFF",
    borderColor: "#E5E7EB",
    borderRadius: 12,
    borderWidth: StyleSheet.hairlineWidth,
    overflow: "hidden",
  },
  timeColumn: {
    maxHeight: 160,
    width: 64,
  },
  timeOption: {
    alignItems: "center",
    borderRadius: 6,
    minHeight: TIME_OPTION_HEIGHT,
    justifyContent: "center",
    paddingVertical: 8,
  },
  timeOptionSelected: {
    backgroundColor: "#FFEDD5",
  },
  timeOptionTextSelected: {
    color: "#C2410C",
  },
  timePicker: {
    flex: 1,
    gap: 6,
  },
  timePickerColumns: {
    alignItems: "center",
    backgroundColor: "#F9FAFB",
    borderColor: "#E5E7EB",
    borderRadius: 8,
    borderWidth: StyleSheet.hairlineWidth,
    flexDirection: "row",
    justifyContent: "center",
    padding: 8,
  },
  timePickerHeader: {
    flexDirection: "row",
    justifyContent: "space-around",
  },
  timePickerHeaderText: {
    color: "#6B7280",
    width: 64,
    textAlign: "center",
  },
  timePickerValue: {
    color: "#111827",
  },
  timeSeparator: {
    color: "#6B7280",
    paddingHorizontal: 4,
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
