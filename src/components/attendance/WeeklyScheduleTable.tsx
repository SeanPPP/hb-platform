import { StyleSheet, View } from "react-native";
import { Card, Chip, Text } from "react-native-paper";
import type { AttendanceWeek } from "@/modules/attendance/types";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

function shortDate(value: string) {
  return value ? value.slice(5, 10) : "--";
}

function formatTime(value: string) {
  return value.includes("T") ? value.split("T").pop()?.slice(0, 5) || value : value.slice(0, 5);
}

export function WeeklyScheduleTable({ week }: { week?: AttendanceWeek }) {
  const { t } = useAppTranslation(["attendance", "common"]);

  return (
    <Card mode="elevated" style={styles.card}>
      <Card.Title title={t("sections.week")} subtitle={week?.weekStart && week?.weekEnd ? `${week.weekStart} - ${week.weekEnd}` : undefined} />
      <Card.Content style={styles.content}>
        {week?.days.length ? (
          week.days.map((day, index) => (
            <View key={day.workDate || index} style={styles.dayBlock}>
              <View style={styles.dayHeader}>
                <Text variant="titleSmall">
                  {t(`weekdays.${index}`)} {shortDate(day.workDate)}
                </Text>
                {day.holidayName ? <Chip compact>{day.holidayName}</Chip> : null}
              </View>
              {day.schedules.length ? (
                day.schedules.map((schedule) => (
                  <View
                    key={schedule.scheduleGuid || `${schedule.workDate}-${schedule.startTime}-${schedule.userGuid}`}
                    style={[styles.scheduleRow, schedule.isMine ? styles.mineRow : null]}
                  >
                    <Text variant="bodyMedium" style={styles.flexText}>
                      {schedule.employeeName || schedule.storeName || schedule.storeCode || t("common:na")}
                    </Text>
                    <Text variant="bodyMedium">
                      {formatTime(schedule.startTime)} - {formatTime(schedule.endTime)}
                    </Text>
                  </View>
                ))
              ) : (
                <Text variant="bodySmall" style={styles.muted}>
                  {t("week.noSchedule")}
                </Text>
              )}
            </View>
          ))
        ) : (
          <Text variant="bodyMedium" style={styles.muted}>
            {t("week.empty")}
          </Text>
        )}
      </Card.Content>
    </Card>
  );
}

const styles = StyleSheet.create({
  card: {
    borderRadius: 8,
  },
  content: {
    gap: 10,
  },
  dayBlock: {
    borderBottomColor: "#E5E7EB",
    borderBottomWidth: StyleSheet.hairlineWidth,
    gap: 6,
    paddingBottom: 10,
  },
  dayHeader: {
    alignItems: "center",
    flexDirection: "row",
    gap: 8,
    justifyContent: "space-between",
  },
  flexText: {
    flex: 1,
  },
  mineRow: {
    backgroundColor: "#E8F5E9",
    borderColor: "#A5D6A7",
  },
  muted: {
    color: "#6B7280",
  },
  scheduleRow: {
    alignItems: "center",
    borderColor: "#E5E7EB",
    borderRadius: 6,
    borderWidth: StyleSheet.hairlineWidth,
    flexDirection: "row",
    gap: 8,
    paddingHorizontal: 10,
    paddingVertical: 8,
  },
});
