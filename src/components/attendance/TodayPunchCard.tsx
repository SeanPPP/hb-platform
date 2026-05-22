import { StyleSheet, View } from "react-native";
import { Button, Card, Chip, Text } from "react-native-paper";
import type { AttendancePunchType, AttendanceToday } from "@/modules/attendance/types";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

function formatTime(value?: string) {
  if (!value) {
    return "--";
  }
  const timePart = value.includes("T") ? value.split("T").pop() : value;
  return timePart?.slice(0, 5) || value;
}

export function TodayPunchCard({
  today,
  title,
  subtitle,
  selectedDate,
  allowPunch = true,
  isLoading,
  isPunching,
  onPunch,
}: {
  today?: AttendanceToday;
  title?: string;
  subtitle?: string;
  selectedDate?: string;
  allowPunch?: boolean;
  isLoading: boolean;
  isPunching: boolean;
  onPunch: (punchType: AttendancePunchType) => void;
}) {
  const { t } = useAppTranslation(["attendance", "common"]);
  const nextPunchType = today?.nextPunchType ?? "ClockIn";
  const canPunch = nextPunchType === "ClockOut" ? today?.canClockOut !== false : today?.canClockIn !== false;
  const cardSubtitle = subtitle ?? selectedDate ?? today?.workDate ?? t("common:loading");

  return (
    <Card mode="elevated" style={styles.card}>
      <Card.Title title={title ?? t("sections.today")} subtitle={cardSubtitle} />
      <Card.Content style={styles.content}>
        {today?.holidayName ? (
          <Chip icon="calendar-alert" style={styles.holidayChip}>
            {today.holidayName}
          </Chip>
        ) : null}

        <View style={styles.scheduleList}>
          <Text variant="labelLarge">{t("today.schedules")}</Text>
          {today?.schedules.length ? (
            today.schedules.map((schedule) => (
              <View key={schedule.scheduleGuid || `${schedule.workDate}-${schedule.startTime}`} style={styles.row}>
                <Text variant="bodyMedium" style={styles.flexText}>
                  {schedule.storeName || schedule.storeCode || t("common:na")}
                </Text>
                <Text variant="bodyMedium">
                  {formatTime(schedule.startTime)} - {formatTime(schedule.endTime)}
                </Text>
              </View>
            ))
          ) : (
            <Text variant="bodyMedium" style={styles.muted}>
              {isLoading ? t("common:loading") : t("today.noSchedule")}
            </Text>
          )}
        </View>

        <View style={styles.punchList}>
          <Text variant="labelLarge">{t("today.punches")}</Text>
          {today?.punches.length ? (
            today.punches.map((punch) => (
              <View key={punch.punchGuid || `${punch.punchType}-${punch.punchTimeLocal}`} style={styles.row}>
                <Chip compact>{t(`punchTypes.${punch.punchType}`, punch.punchType)}</Chip>
                <Text variant="bodyMedium" style={styles.flexText}>
                  {formatTime(punch.punchTimeLocal || punch.punchTimeUtc)}
                </Text>
                <Text variant="labelMedium">{t(`statuses.${punch.status}`, punch.status)}</Text>
              </View>
            ))
          ) : (
            <Text variant="bodyMedium" style={styles.muted}>
              {t("today.noPunch")}
            </Text>
          )}
        </View>
      </Card.Content>
      <Card.Actions>
        <Button
          mode="contained"
          icon={nextPunchType === "ClockOut" ? "clock-out" : "clock-in"}
          loading={isPunching}
          disabled={isLoading || isPunching || !allowPunch || !canPunch}
          onPress={() => onPunch(nextPunchType)}
        >
          {t(`actions.${nextPunchType === "ClockOut" ? "clockOut" : "clockIn"}`)}
        </Button>
      </Card.Actions>
    </Card>
  );
}

const styles = StyleSheet.create({
  card: {
    borderRadius: 8,
  },
  content: {
    gap: 12,
  },
  flexText: {
    flex: 1,
  },
  holidayChip: {
    alignSelf: "flex-start",
  },
  muted: {
    color: "#6B7280",
  },
  punchList: {
    gap: 8,
  },
  row: {
    alignItems: "center",
    flexDirection: "row",
    gap: 8,
  },
  scheduleList: {
    gap: 8,
  },
});
