import { useEffect, useMemo, useState } from "react";
import { Pressable, StyleSheet, View } from "react-native";
import { Card, Chip, Text } from "react-native-paper";
import type {
  AttendancePunchVerificationState,
  AttendancePunch,
  AttendanceToday,
} from "@/modules/attendance/types";
import { canOpenAttendanceQrScanner } from "@/modules/attendance/attendance-qr";
import {
  buildAttendanceTodayDisplay,
  resolveAttendancePunchExceptionMinutes,
} from "@/modules/attendance/attendance-today-normalization";
import { resolveAttendanceTodayStatus } from "@/modules/attendance/attendance-today-status";
import { resolveAttendancePunchDisplayTime } from "@/modules/attendance/attendance-device-time";
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
  hasAuthorizedStores,
  verification,
  lastQrPunch,
  trackingWarning,
  onScan,
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
  hasAuthorizedStores: boolean;
  verification: AttendancePunchVerificationState;
  lastQrPunch?: AttendancePunch;
  trackingWarning?: string;
  onScan: () => void;
}) {
  const { t } = useAppTranslation(["attendance", "common"]);
  const [currentTime, setCurrentTime] = useState(() => new Date());

  useEffect(() => {
    const timer = setInterval(() => setCurrentTime(new Date()), 1000);
    return () => clearInterval(timer);
  }, []);

  const canScan = canOpenAttendanceQrScanner({
    isLoading,
    isPunching,
    isToday: allowPunch,
    hasAuthorizedStores,
  });
  const cardSubtitle =
    subtitle ?? selectedDate ?? today?.workDate ?? t("common:loading");
  const display = useMemo(() => buildAttendanceTodayDisplay(today), [today]);
  const primarySchedule = today?.schedules[0];
  const statusLabel = useMemo(
    () => t(`today.status.${resolveAttendanceTodayStatus(today, allowPunch)}`),
    [allowPunch, t, today],
  );

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

  const networkChipLabel = t(
    `today.verification.statuses.${verification.network.status}`,
  );

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
          {display.stores.length ? (
            <View style={styles.shiftSummary}>
              <Text variant="labelLarge">{t("today.schedules")}</Text>
              {display.stores.map((store) => store.sessions.map((session) => (
                  <View
                    key={session.scheduleGuid || `${store.storeCode}-${session.startTime}`}
                    style={styles.shiftRow}
                  >
                    <Text variant="bodyMedium" style={styles.flexText}>
                      {store.storeName || store.storeCode || storeName || t("common:na")}
                    </Text>
                    <Text variant="bodyMedium">
                      {session.scheduleState === "NoSchedule"
                        ? t("today.timeline.unscheduledPunches")
                        : `${formatTime(session.startTime)} - ${formatTime(session.endTime)}`}
                    </Text>
                  </View>
                ))) }
            </View>
          ) : (
            <Text variant="bodyMedium" style={styles.muted}>
              {isLoading ? t("common:loading") : t("today.noSchedule")}
            </Text>
          )}
          <Pressable
            accessibilityRole="button"
            accessibilityState={{
              disabled: !canScan,
            }}
            disabled={!canScan}
            style={({ pressed }) => [
              styles.punchButton,
              !canScan ? styles.punchButtonDisabled : null,
              pressed ? styles.punchButtonPressed : null,
            ]}
            onPress={onScan}
          >
            <Text variant="headlineSmall" style={styles.punchButtonText}>
              {isPunching
                ? t("actions.processingPunch")
                : t("actions.scanPunch")}
            </Text>
          </Pressable>
          {lastQrPunch ? (
            <View style={styles.qrResult}>
              <Text variant="titleSmall" selectable>
                {t("today.qrResult.title")}
              </Text>
              <Text selectable>{t("today.qrResult.employee", {
                employee: lastQrPunch.employeeName || lastQrPunch.userGuid || t("common:na"),
              })}</Text>
              <Text selectable>{t("today.qrResult.store", {
                store: lastQrPunch.storeName || lastQrPunch.storeCode || t("common:na"),
              })}</Text>
              <Text selectable>{t("today.qrResult.posDevice", {
                device: lastQrPunch.posDeviceCode || t("common:na"),
              })}</Text>
              <Text selectable>{t("today.qrResult.action", {
                action: t(`punchTypes.${lastQrPunch.punchType}`, lastQrPunch.punchType),
              })}</Text>
              <Text selectable style={styles.tabularText}>{t("today.qrResult.deviceTime", {
                time: resolveAttendancePunchDisplayTime({
                  punchTimeUtc: lastQrPunch.punchTimeUtc || lastQrPunch.serverTimeUtc,
                }) || t("common:na"),
              })}</Text>
              <Text selectable>{t("today.qrResult.status", {
                status: t(`statuses.${lastQrPunch.status}`, lastQrPunch.status),
              })}</Text>
              {trackingWarning ? (
                <Text selectable style={styles.trackingWarning}>
                  {trackingWarning}
                </Text>
              ) : null}
            </View>
          ) : null}
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

        <View style={styles.recordsCard}>
          <Text variant="titleMedium">{t("today.dailyRecords.title")}</Text>
          <View style={styles.recordList}>
            {display.relatedStoreAlerts.map((alert) => (
              <View
                key={`${alert.missingStoreCode}-${alert.activeStoreCode}`}
                style={styles.relatedAlert}
              >
                <Text variant="labelMedium" style={styles.relatedAlertTitle}>
                  {t("today.timeline.relatedStoreAlert")}
                </Text>
                <Text variant="bodySmall">
                  {t("today.timeline.relatedStoreConflict", {
                    missingStore: alert.missingStoreName || alert.missingStoreCode,
                    activeStore: alert.activeStoreName || alert.activeStoreCode,
                  })}
                </Text>
              </View>
            ))}
            {display.relatedStoreReminders.map((reminder) => (
              <View key={reminder} style={styles.relatedAlert}>
                <Text variant="labelMedium" style={styles.relatedAlertTitle}>
                  {t("today.timeline.relatedStoreAlert")}
                </Text>
                <Text variant="bodySmall">{reminder}</Text>
              </View>
            ))}
            {display.stores.map((store) => (
              <View key={store.storeCode || store.storeName} style={styles.storeGroup}>
                <View style={styles.storeHeader}>
                  <Text variant="titleSmall">
                    {store.storeName || store.storeCode || storeName || t("common:na")}
                  </Text>
                  <Chip compact>{t("today.timeline.shiftCount", { count: store.sessions.length })}</Chip>
                </View>
                {store.relatedReminder && !display.relatedStoreReminders.includes(store.relatedReminder) ? (
                  <Text variant="bodySmall" style={styles.relatedAlertTitle}>
                    {store.relatedReminder}
                  </Text>
                ) : null}
                {store.sessions.map((session) => (
                  <View
                    key={session.scheduleGuid || `${store.storeCode}-${session.startTime}`}
                    style={styles.sessionCard}
                  >
                    <View style={styles.sessionHeader}>
                      <View style={styles.flexText}>
                        <Text variant="labelLarge">
                          {session.scheduleState === "NoSchedule"
                            ? t("today.timeline.unscheduledPunches")
                            : `${formatTime(session.startTime)} - ${formatTime(session.endTime)}`}
                        </Text>
                        <Text variant="bodySmall" style={styles.muted}>
                          {t("today.timeline.segmentProgress", {
                            completed: session.completedSegmentCount ?? session.segments.filter((item) => item.clockOut).length,
                            limit: session.segmentLimit ?? session.segments.length,
                          })}
                        </Text>
                      </View>
                      {session.scheduleState ? <Chip compact>{session.scheduleState}</Chip> : null}
                    </View>
                    <View style={styles.metricsRow}>
                      <Text variant="bodySmall">
                        {t("today.timeline.workedMinutes", { minutes: session.workedMinutes })}
                      </Text>
                      {session.breakMinutes !== undefined ? (
                        <Text variant="bodySmall" style={styles.muted}>
                          {t("today.timeline.breakMinutes", { minutes: session.breakMinutes })}
                        </Text>
                      ) : null}
                    </View>
                    {session.hasMissingClockOut ? (
                      <Text variant="bodySmall" style={styles.exceptionText}>
                        {t("today.timeline.missingClockOut")}
                      </Text>
                    ) : null}
                    <View style={styles.segmentList}>
                      {session.segments.map((segment) => {
                        const clockInException = segment.clockIn && segment.clockIn.status !== "Normal";
                        const clockOutException = segment.clockOut && segment.clockOut.status !== "Normal";
                        const clockInMinutes = segment.clockIn
                          ? resolveAttendancePunchExceptionMinutes(segment.clockIn)
                          : undefined;
                        const clockOutMinutes = segment.clockOut
                          ? resolveAttendancePunchExceptionMinutes(segment.clockOut)
                          : undefined;
                        return (
                          <View key={segment.segmentIndex} style={styles.segmentRow}>
                            <View style={styles.segmentHeader}>
                              <Text variant="labelMedium">
                                {t("today.timeline.segment", { number: segment.segmentNumber })}
                              </Text>
                              <Text variant="labelMedium" style={styles.muted}>
                                {segment.workedMinutes ?? segment.durationMinutes ?? 0} min
                              </Text>
                            </View>
                            <View style={styles.punchPair}>
                              <Text variant="bodySmall" style={styles.flexText}>
                                {t("today.timeline.clockIn", {
                                  time: formatTime(resolveAttendancePunchDisplayTime(segment.clockIn)),
                                })}
                              </Text>
                              <Text variant="bodySmall" style={styles.flexText}>
                                {t("today.timeline.clockOut", {
                                  time: formatTime(resolveAttendancePunchDisplayTime(segment.clockOut)),
                                })}
                              </Text>
                            </View>
                            {segment.showClockInException && segment.clockIn ? (
                              <Text variant="bodySmall" style={clockInException ? styles.exceptionText : styles.muted}>
                                {clockInMinutes !== undefined
                                  ? t("today.timeline.firstClockInStatus", {
                                      status: t(`statuses.${segment.clockIn.status}`, segment.clockIn.status),
                                      minutes: clockInMinutes,
                                    })
                                  : t("today.timeline.firstClockInStatusOnly", {
                                      status: t(`statuses.${segment.clockIn.status}`, segment.clockIn.status),
                                    })}
                              </Text>
                            ) : null}
                            {segment.showClockOutException && segment.clockOut ? (
                              <Text variant="bodySmall" style={clockOutException ? styles.exceptionText : styles.muted}>
                                {clockOutMinutes !== undefined
                                  ? t("today.timeline.finalClockOutStatus", {
                                      status: t(`statuses.${segment.clockOut.status}`, segment.clockOut.status),
                                      minutes: clockOutMinutes,
                                    })
                                  : t("today.timeline.finalClockOutStatusOnly", {
                                      status: t(`statuses.${segment.clockOut.status}`, segment.clockOut.status),
                                    })}
                              </Text>
                            ) : null}
                            {segment.isBreakAfter ? (
                              <Chip compact icon="coffee-outline" style={styles.breakChip}>
                                {t("today.timeline.onBreak")}
                              </Chip>
                            ) : null}
                          </View>
                        );
                      })}
                    </View>
                    {(session.overtime.rawMinutes > 0 ||
                      session.overtime.candidateMinutes > 0 ||
                      session.overtime.approvedMinutes > 0) ? (
                      <View style={styles.overtimeBox}>
                        <Text variant="labelMedium">{t("today.timeline.overtimeTitle")}</Text>
                        <Text variant="bodySmall">
                          {t("today.timeline.overtimeSummary", {
                            raw: session.overtime.rawMinutes,
                            candidate: session.overtime.candidateMinutes,
                            approved: session.overtime.approvedMinutes,
                          })}
                        </Text>
                        {session.overtime.status ? (
                          <Text variant="bodySmall" style={styles.muted}>{session.overtime.status}</Text>
                        ) : null}
                      </View>
                    ) : null}
                  </View>
                ))}
              </View>
            ))}
            {!display.stores.length && !isLoading ? (
              <Text variant="bodySmall" style={styles.muted}>{t("today.dailyRecords.noSchedule")}</Text>
            ) : null}
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
  breakChip: {
    alignSelf: "flex-start",
    backgroundColor: "#E8F1F8",
  },
  exceptionText: {
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
  muted: {
    color: "#6B7280",
  },
  metricsRow: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 12,
  },
  overtimeBox: {
    backgroundColor: "#FFF7E6",
    borderRadius: 8,
    gap: 3,
    padding: 10,
  },
  punchPair: {
    flexDirection: "row",
    gap: 8,
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
  qrResult: {
    alignSelf: "stretch",
    backgroundColor: "#F5F7FA",
    borderRadius: 10,
    gap: 4,
    padding: 12,
  },
  tabularText: {
    fontVariant: ["tabular-nums"],
  },
  trackingWarning: {
    color: "#B42318",
    fontWeight: "600",
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
  relatedAlert: {
    backgroundColor: "#FFF7E6",
    borderLeftColor: "#F59E0B",
    borderLeftWidth: 3,
    borderRadius: 8,
    gap: 3,
    padding: 10,
  },
  relatedAlertTitle: {
    color: "#92400E",
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
  segmentHeader: {
    alignItems: "center",
    flexDirection: "row",
    justifyContent: "space-between",
  },
  segmentList: {
    gap: 8,
  },
  segmentRow: {
    backgroundColor: "#F5F7FA",
    borderRadius: 8,
    gap: 5,
    padding: 10,
  },
  sessionCard: {
    borderColor: "#D8DEE6",
    borderRadius: 10,
    borderWidth: StyleSheet.hairlineWidth,
    gap: 9,
    padding: 10,
  },
  sessionHeader: {
    alignItems: "center",
    flexDirection: "row",
    gap: 8,
  },
  storeGroup: {
    gap: 8,
  },
  storeHeader: {
    alignItems: "center",
    flexDirection: "row",
    justifyContent: "space-between",
  },
  statusChip: {
    backgroundColor: "#F3F4F6",
  },
  statusChipText: {
    color: "#4B5563",
  },
});
