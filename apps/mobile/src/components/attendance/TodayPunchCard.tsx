import { useEffect, useMemo, useState } from "react";
import { Pressable, StyleSheet, View } from "react-native";
import { Card, Chip, Text } from "react-native-paper";
import type {
  AttendancePunchVerificationState,
  AttendancePunchType,
  AttendanceToday,
} from "@/modules/attendance/types";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

function formatTime(value?: string) {
  if (!value) {
    return "--:--";
  }
  const timePart = value.includes("T") ? value.split("T").pop() : value;
  return timePart?.slice(0, 5) || value;
}

function formatClockTime(value: Date) {
  return value.toLocaleTimeString([], {
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
    hour12: false,
  });
}

function getLatestPunch(
  today: AttendanceToday | undefined,
  punchType: AttendancePunchType,
) {
  return today?.punches
    .filter((item) => item.punchType === punchType)
    .sort((left, right) =>
      (left.punchTimeLocal || left.punchTimeUtc || "").localeCompare(
        right.punchTimeLocal || right.punchTimeUtc || "",
      ),
    )
    .at(-1);
}

export function TodayPunchCard({
  today,
  title,
  subtitle,
  selectedDate,
  storeName,
  allowPunch = true,
  isLoading,
  isVerificationRefreshing,
  isPunching,
  verification,
  onPunch,
}: {
  today?: AttendanceToday;
  title?: string;
  subtitle?: string;
  selectedDate?: string;
  storeName?: string;
  allowPunch?: boolean;
  isLoading: boolean;
  isVerificationRefreshing?: boolean;
  isPunching: boolean;
  verification: AttendancePunchVerificationState;
  onPunch: (punchType: AttendancePunchType) => void;
}) {
  const { t } = useAppTranslation(["attendance", "common"]);
  const [currentTime, setCurrentTime] = useState(() => new Date());

  useEffect(() => {
    const timer = setInterval(() => setCurrentTime(new Date()), 1000);
    return () => clearInterval(timer);
  }, []);

  const nextPunchType = today?.nextPunchType ?? "ClockIn";
  const canPunch =
    nextPunchType === "ClockOut"
      ? today?.canClockOut !== false
      : today?.canClockIn !== false;
  const cardSubtitle =
    subtitle ?? selectedDate ?? today?.workDate ?? t("common:loading");
  const clockInPunch = getLatestPunch(today, "ClockIn");
  const clockOutPunch = getLatestPunch(today, "ClockOut");
  const primarySchedule = today?.schedules[0];
  const statusLabel = useMemo(() => {
    if (!allowPunch) {
      return t("today.status.viewOnly");
    }
    if (today?.holidayName) {
      return t("today.status.holiday");
    }
    if (nextPunchType === "ClockOut") {
      return t("today.status.readyToClockOut");
    }
    if (clockOutPunch) {
      return t("today.status.completed");
    }
    return t("today.status.readyToClockIn");
  }, [allowPunch, clockOutPunch, nextPunchType, t, today?.holidayName]);

  const locationValue = useMemo(() => {
    if (verification.location.latitude !== undefined) {
      return `${verification.location.latitude.toFixed(6)}, ${verification.location.longitude?.toFixed(6) ?? "--"}`;
    }

    if (verification.location.reason === "dependencyMissing") {
      return t("today.info.locationDependencyMissing");
    }

    if (verification.location.status === "permissionDenied") {
      return t("today.info.locationPermissionDenied");
    }

    return t("today.info.locationUnavailable");
  }, [
    t,
    verification.location.latitude,
    verification.location.longitude,
    verification.location.reason,
    verification.location.status,
  ]);

  const networkValue = useMemo(() => {
    if (verification.network.status === "available") {
      return today?.storeTimeZone
        ? t("today.info.networkAvailableWithTimeZone", {
            timeZone: today.storeTimeZone,
          })
        : t("today.info.networkAvailable");
    }

    if (verification.network.reason === "networkUnreachable") {
      return t("today.info.networkUnavailable");
    }

    return t("today.info.networkUnknown");
  }, [t, today?.storeTimeZone, verification.network.reason, verification.network.status]);

  const locationChipLabel = t(
    `today.verification.statuses.${verification.location.status}`,
  );
  const networkChipLabel = t(
    `today.verification.statuses.${verification.network.status}`,
  );

  const records = [
    {
      key: "clockIn",
      accent: "#34D399",
      iconBg: "#ECFDF5",
      title: t("today.dailyRecords.clockInTitle"),
      time: formatTime(
        clockInPunch?.punchTimeLocal || clockInPunch?.punchTimeUtc,
      ),
      body: clockInPunch
        ? t("today.dailyRecords.clockInSuccess", {
            status: t(`statuses.${clockInPunch.status}`, clockInPunch.status),
          })
        : primarySchedule
          ? t("today.dailyRecords.clockInPending", {
              startTime: formatTime(primarySchedule.startTime),
            })
          : t("today.dailyRecords.noSchedule"),
      muted: !clockInPunch,
    },
    {
      key: "clockOut",
      accent: "#D1D5DB",
      iconBg: "#F3F4F6",
      title: t("today.dailyRecords.clockOutTitle"),
      time: formatTime(
        clockOutPunch?.punchTimeLocal ||
          clockOutPunch?.punchTimeUtc ||
          primarySchedule?.endTime,
      ),
      body: clockOutPunch
        ? t("today.dailyRecords.clockOutSuccess", {
            status: t(`statuses.${clockOutPunch.status}`, clockOutPunch.status),
          })
        : primarySchedule
          ? t("today.dailyRecords.clockOutPending", {
              endTime: formatTime(primarySchedule.endTime),
            })
          : t("today.dailyRecords.awaitingShift"),
      muted: !clockOutPunch,
    },
  ];

  const alertMessage = !allowPunch
    ? t("today.dailyRecords.alertSelectedDate")
    : today?.holidayName
      ? t("today.dailyRecords.alertHoliday", { holidayName: today.holidayName })
      : verification.location.status === "available" &&
          verification.network.status === "available"
        ? t("today.dailyRecords.alertVerificationCaptured")
        : today?.schedules.length
          ? t("today.dailyRecords.alertVerificationSentForReview")
        : t("today.dailyRecords.alertNoSchedule");

  return (
    <Card mode="elevated" style={styles.card}>
      <Card.Content style={styles.content}>
        <View style={styles.heroCard}>
          <View style={styles.heroHeader}>
            <Text variant="titleMedium">{title ?? t("sections.today")}</Text>
            <Text variant="bodyMedium" style={styles.muted}>
              {cardSubtitle}
            </Text>
          </View>
          <Text variant="displaySmall" style={styles.clockText}>
            {formatClockTime(currentTime)}
          </Text>
          <Chip
            compact
            style={styles.statusChip}
            textStyle={styles.statusChipText}
          >
            {statusLabel}
          </Chip>
          {today?.holidayName ? (
            <Chip icon="calendar-alert" style={styles.holidayChip}>
              {today.holidayName}
            </Chip>
          ) : null}
          {today?.schedules.length ? (
            <View style={styles.shiftSummary}>
              <Text variant="labelLarge">{t("today.schedules")}</Text>
              {today.schedules.map((schedule) => (
                <View
                  key={
                    schedule.scheduleGuid ||
                    `${schedule.workDate}-${schedule.startTime}`
                  }
                  style={styles.shiftRow}
                >
                  <Text variant="bodyMedium" style={styles.flexText}>
                    {schedule.storeName || schedule.storeCode || t("common:na")}
                  </Text>
                  <Text variant="bodyMedium">
                    {formatTime(schedule.startTime)} -{" "}
                    {formatTime(schedule.endTime)}
                  </Text>
                </View>
              ))}
            </View>
          ) : (
            <Text variant="bodyMedium" style={styles.muted}>
              {isLoading ? t("common:loading") : t("today.noSchedule")}
            </Text>
          )}
          <Pressable
            accessibilityRole="button"
            accessibilityState={{
              disabled: isLoading || isPunching || !allowPunch || !canPunch,
            }}
            disabled={isLoading || isPunching || !allowPunch || !canPunch}
            style={({ pressed }) => [
              styles.punchButton,
              isLoading || isPunching || !allowPunch || !canPunch
                ? styles.punchButtonDisabled
                : null,
              pressed ? styles.punchButtonPressed : null,
            ]}
            onPress={() => onPunch(nextPunchType)}
          >
            <Text variant="headlineSmall" style={styles.punchButtonText}>
              {isPunching
                ? t("common:loading")
                : t(
                    `actions.${nextPunchType === "ClockOut" ? "clockOut" : "clockIn"}`,
                  )}
            </Text>
          </Pressable>
        </View>

        <View style={styles.metaRow}>
          <View style={styles.metaCard}>
            <View style={styles.metaHeader}>
              <Text variant="labelMedium" style={styles.muted}>
                {t("today.info.location")}
              </Text>
              <Chip compact style={styles.metaChip} textStyle={styles.metaChipText}>
                {locationChipLabel}
              </Chip>
            </View>
            <Text variant="bodyMedium">{locationValue}</Text>
          </View>
          <View style={styles.metaCard}>
            <View style={styles.metaHeader}>
              <Text variant="labelMedium" style={styles.muted}>
                {t("today.info.network")}
              </Text>
              <Chip compact style={styles.metaChip} textStyle={styles.metaChipText}>
                {isVerificationRefreshing
                  ? t("today.verification.refreshing")
                  : networkChipLabel}
              </Chip>
            </View>
            <Text variant="bodyMedium">{networkValue}</Text>
          </View>
        </View>

        <View style={styles.recordsCard}>
          <Text variant="titleMedium">{t("today.dailyRecords.title")}</Text>
          <View style={styles.recordList}>
            {records.map((record) => (
              <View
                key={record.key}
                style={[styles.recordItem, { borderLeftColor: record.accent }]}
              >
                <View
                  style={[
                    styles.recordIcon,
                    { backgroundColor: record.iconBg },
                  ]}
                >
                  <Text variant="labelSmall">
                    {t(
                      record.key === "clockIn"
                        ? "today.dailyRecords.clockInBadge"
                        : "today.dailyRecords.clockOutBadge",
                    )}
                  </Text>
                </View>
                <View style={styles.recordBody}>
                  <View style={styles.recordHeader}>
                    <Text variant="titleSmall">{record.title}</Text>
                    <Text variant="labelMedium" style={styles.muted}>
                      {record.time}
                    </Text>
                  </View>
                  <Text
                    variant="bodySmall"
                    style={record.muted ? styles.muted : undefined}
                  >
                    {record.body}
                  </Text>
                </View>
              </View>
            ))}
            <View style={styles.alertItem}>
              <View style={styles.alertIcon}>
                <Text variant="labelSmall" style={styles.alertIconText}>
                  !
                </Text>
              </View>
              <View style={styles.recordBody}>
                <View style={styles.recordHeader}>
                  <Text variant="titleSmall" style={styles.alertTitle}>
                    {t("today.dailyRecords.systemAlertTitle")}
                  </Text>
                  <Text variant="labelMedium" style={styles.alertTime}>
                    {formatClockTime(currentTime).slice(0, 5)}
                  </Text>
                </View>
                <Text variant="bodySmall" style={styles.muted}>
                  {alertMessage}
                </Text>
              </View>
            </View>
          </View>
        </View>
      </Card.Content>
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
  alertIcon: {
    alignItems: "center",
    backgroundColor: "#FEE2E2",
    borderRadius: 16,
    height: 32,
    justifyContent: "center",
    width: 32,
  },
  alertIconText: {
    color: "#B42318",
  },
  alertItem: {
    alignItems: "flex-start",
    backgroundColor: "#FFF5F5",
    borderLeftColor: "#EF4444",
    borderLeftWidth: 3,
    borderRadius: 8,
    flexDirection: "row",
    gap: 10,
    padding: 12,
  },
  alertTime: {
    color: "#B42318",
  },
  alertTitle: {
    color: "#B42318",
  },
  flexText: {
    flex: 1,
  },
  heroCard: {
    alignItems: "center",
    backgroundColor: "#FFFFFF",
    borderColor: "#D8DEE6",
    borderRadius: 12,
    borderWidth: StyleSheet.hairlineWidth,
    gap: 12,
    paddingHorizontal: 18,
    paddingVertical: 20,
  },
  heroHeader: {
    alignItems: "center",
    gap: 4,
  },
  holidayChip: {
    alignSelf: "flex-start",
  },
  clockText: {
    color: "#111827",
    fontWeight: "700",
  },
  metaCard: {
    backgroundColor: "#EEF2F6",
    borderRadius: 10,
    flex: 1,
    gap: 4,
    paddingHorizontal: 12,
    paddingVertical: 12,
  },
  metaChip: {
    backgroundColor: "#FFFFFF",
  },
  metaChipText: {
    color: "#4B5563",
  },
  metaHeader: {
    alignItems: "center",
    flexDirection: "row",
    justifyContent: "space-between",
  },
  metaRow: {
    gap: 10,
  },
  muted: {
    color: "#6B7280",
  },
  punchButton: {
    alignItems: "center",
    backgroundColor: "#050505",
    borderRadius: 14,
    justifyContent: "center",
    minHeight: 124,
    paddingHorizontal: 24,
    width: 164,
  },
  punchButtonDisabled: {
    backgroundColor: "#9CA3AF",
  },
  punchButtonPressed: {
    opacity: 0.85,
  },
  punchButtonText: {
    color: "#FFFFFF",
    fontWeight: "700",
  },
  recordBody: {
    flex: 1,
    gap: 4,
  },
  recordHeader: {
    alignItems: "center",
    flexDirection: "row",
    justifyContent: "space-between",
  },
  recordIcon: {
    alignItems: "center",
    borderRadius: 16,
    height: 32,
    justifyContent: "center",
    width: 32,
  },
  recordItem: {
    alignItems: "flex-start",
    backgroundColor: "#F5F7FA",
    borderLeftWidth: 3,
    borderRadius: 8,
    flexDirection: "row",
    gap: 10,
    padding: 12,
  },
  recordList: {
    gap: 8,
  },
  recordsCard: {
    backgroundColor: "#FFFFFF",
    borderColor: "#D8DEE6",
    borderRadius: 12,
    borderWidth: StyleSheet.hairlineWidth,
    gap: 12,
    padding: 14,
  },
  shiftRow: {
    alignItems: "center",
    flexDirection: "row",
    gap: 8,
  },
  shiftSummary: {
    alignSelf: "stretch",
    gap: 8,
  },
  statusChip: {
    backgroundColor: "#F3F4F6",
  },
  statusChipText: {
    color: "#4B5563",
  },
});
