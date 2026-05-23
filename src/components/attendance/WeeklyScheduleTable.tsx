import { StyleSheet, View } from "react-native";
import { Card, Chip, Text } from "react-native-paper";
import type { AttendanceWeek } from "@/modules/attendance/types";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

function shortDate(value: string) {
  return value ? value.slice(5, 10) : "--";
}

function formatTime(value: string) {
  return value.includes("T")
    ? value.split("T").pop()?.slice(0, 5) || value
    : value.slice(0, 5);
}

export function WeeklyScheduleTable({ week }: { week?: AttendanceWeek }) {
  const { t } = useAppTranslation(["attendance", "common"]);

  return (
    <Card mode="elevated" style={styles.card}>
      <Card.Title
        title={t("sections.week")}
        subtitle={
          week?.weekStart && week?.weekEnd
            ? `${week.weekStart} - ${week.weekEnd}`
            : undefined
        }
      />
      <Card.Content style={styles.content}>
        {week?.days.length ? (
          week.days.map((day, index) => (
            <View key={day.workDate || index} style={styles.dayBlock}>
              <View style={styles.dayHeader}>
                <View style={styles.dayTitleBlock}>
                  <Text variant="titleSmall">{t(`weekdays.${index}`)}</Text>
                  <Text variant="bodySmall" style={styles.muted}>
                    {shortDate(day.workDate)}
                  </Text>
                </View>
                {day.holidayName ? (
                  <Chip compact>{day.holidayName}</Chip>
                ) : null}
              </View>
              {day.schedules.length ? (
                day.schedules.map((schedule) => (
                  <View
                    key={
                      schedule.scheduleGuid ||
                      `${schedule.workDate}-${schedule.startTime}-${schedule.userGuid}`
                    }
                    style={[
                      styles.scheduleRow,
                      schedule.isMine ? styles.mineRow : null,
                    ]}
                  >
                    <View style={styles.scheduleMeta}>
                      <Text variant="bodyMedium" style={styles.flexText}>
                        {schedule.employeeName ||
                          schedule.storeName ||
                          schedule.storeCode ||
                          t("common:na")}
                      </Text>
                      <Text variant="bodySmall" style={styles.muted}>
                        {t(`statuses.${schedule.status}`, schedule.status)}
                      </Text>
                    </View>
                    <Text variant="titleSmall">
                      {formatTime(schedule.startTime)} -{" "}
                      {formatTime(schedule.endTime)}
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
    backgroundColor: "#FFFFFF",
    borderColor: "#E5E7EB",
    borderRadius: 10,
    borderWidth: StyleSheet.hairlineWidth,
    gap: 8,
    padding: 12,
  },
  dayHeader: {
    alignItems: "center",
    flexDirection: "row",
    gap: 8,
    justifyContent: "space-between",
  },
  dayTitleBlock: {
    gap: 2,
  },
  flexText: {
    flex: 1,
  },
  mineRow: {
    backgroundColor: "#ECFDF5",
    borderColor: "#6EE7B7",
  },
  muted: {
    color: "#6B7280",
  },
  scheduleMeta: {
    flex: 1,
    gap: 2,
  },
  scheduleRow: {
    backgroundColor: "#F8FAFC",
    borderColor: "#E5E7EB",
    borderLeftWidth: 3,
    borderRadius: 8,
    borderWidth: StyleSheet.hairlineWidth,
    flexDirection: "column",
    gap: 6,
    paddingHorizontal: 12,
    paddingVertical: 10,
  },
});
