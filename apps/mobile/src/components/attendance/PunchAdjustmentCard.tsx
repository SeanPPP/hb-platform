import { useEffect, useMemo, useRef, useState } from "react";
import { StyleSheet, View } from "react-native";
import {
  Button,
  Card,
  Chip,
  SegmentedButtons,
  Text,
  TextInput,
} from "react-native-paper";
import {
  canRequestAttendanceAdjustmentForDate,
  buildAttendancePunchAdjustmentResetKey,
  buildAttendancePunchAdjustmentFingerprint,
  buildAttendancePunchAdjustmentPayload,
  createAttendanceAdjustmentRequestGate,
  runLatestAttendanceAdjustmentRequest,
  validateAttendancePunchAdjustment,
} from "@/modules/attendance/attendance-punch-adjustment";
import type {
  AttendanceAdjustmentPreview,
  AttendancePunchAdjustmentPayload,
  AttendancePunchType,
  AttendanceToday,
} from "@/modules/attendance/types";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import {
  toAttendanceDeviceLocalTime,
  toAttendancePunchTimeUtc,
} from "@/modules/attendance/attendance-device-time";

function toDateString(date: Date) {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}

function defaultLocalTime(workDate: string) {
  const now = new Date();
  return `${workDate}T${String(now.getHours()).padStart(2, "0")}:${String(now.getMinutes()).padStart(2, "0")}`;
}

function punchTime(punch: AttendanceToday["punches"][number]) {
  return punch.punchTimeUtc
    ? toAttendanceDeviceLocalTime(punch.punchTimeUtc)
    : punch.punchTimeLocal ?? punch.effectivePunchTime ?? "";
}

export function PunchAdjustmentCard({
  today,
  selectedDate,
  storeCode,
  isManagerStore,
  isBusy,
  onPreview,
  onSubmit,
}: {
  today?: AttendanceToday;
  selectedDate: string;
  storeCode?: string;
  isManagerStore: boolean;
  isBusy: boolean;
  onPreview: (payload: AttendancePunchAdjustmentPayload) => Promise<AttendanceAdjustmentPreview>;
  onSubmit: (payload: AttendancePunchAdjustmentPayload) => Promise<void>;
}) {
  const { t } = useAppTranslation(["attendance", "common"]);
  const [punchType, setPunchType] = useState<AttendancePunchType>("ClockIn");
  const [requestedPunchTimeLocal, setRequestedPunchTimeLocal] = useState(() => defaultLocalTime(selectedDate));
  const [reason, setReason] = useState("");
  const [scheduleGuid, setScheduleGuid] = useState<string | undefined>();
  const [originalPunchGuid, setOriginalPunchGuid] = useState<string | undefined>();
  const [preview, setPreview] = useState<AttendanceAdjustmentPreview>();
  const [previewFingerprint, setPreviewFingerprint] = useState<string>();
  const [localError, setLocalError] = useState("");
  const latestPayloadFingerprintRef = useRef("");
  const previewRequestGateRef = useRef(createAttendanceAdjustmentRequestGate());
  const submitRequestGateRef = useRef(createAttendanceAdjustmentRequestGate());
  const selectableSchedules = useMemo(
    () => (today?.scheduleSessions ?? []).filter((session) => session.scheduleState !== "NoSchedule"),
    [today?.scheduleSessions],
  );
  const resetKey = buildAttendancePunchAdjustmentResetKey(
    selectedDate,
    storeCode,
    selectableSchedules,
  );

  useEffect(() => {
    setRequestedPunchTimeLocal(defaultLocalTime(selectedDate));
    setScheduleGuid(selectableSchedules[0]?.scheduleGuid);
    setOriginalPunchGuid(undefined);
    setPreview(undefined);
    setPreviewFingerprint(undefined);
    setLocalError("");
    previewRequestGateRef.current.invalidate();
    submitRequestGateRef.current.invalidate();
  }, [resetKey]);

  const isWithinSelfWindow = canRequestAttendanceAdjustmentForDate(
    selectedDate,
    toDateString(new Date()),
  );
  const canOpenForm = today?.canRequestAdjustment ?? isWithinSelfWindow;
  const payload = useMemo<AttendancePunchAdjustmentPayload>(
    () => ({
      ...buildAttendancePunchAdjustmentPayload({
        storeCode,
        today,
        scheduleGuid,
        originalPunchGuid,
        punchType,
        requestedPunchTimeLocal,
        reason,
      }),
      requestedPunchTimeUtc: toAttendancePunchTimeUtc(requestedPunchTimeLocal),
    }),
    [originalPunchGuid, punchType, reason, requestedPunchTimeLocal, scheduleGuid, storeCode, today],
  );
  const payloadFingerprint = buildAttendancePunchAdjustmentFingerprint(payload);
  latestPayloadFingerprintRef.current = payloadFingerprint;
  const missingFields = validateAttendancePunchAdjustment(payload);
  const isManagerDirect = preview?.wouldAutoApprove === true;
  const isPreviewRevisionMissing = preview?.isValid && !preview.previewRevision;
  const canSubmit = Boolean(
    preview?.isValid
    && preview.previewRevision
    && previewFingerprint === buildAttendancePunchAdjustmentFingerprint(payload),
  );

  const updateDraft = (update: () => void) => {
    update();
    setPreview(undefined);
    setPreviewFingerprint(undefined);
    setLocalError("");
    previewRequestGateRef.current.invalidate();
    submitRequestGateRef.current.invalidate();
  };

  const selectExistingPunch = (punch: AttendanceToday["punches"][number]) => {
    updateDraft(() => {
      setOriginalPunchGuid(punch.punchGuid || undefined);
      setScheduleGuid(punch.scheduleGuid ?? selectableSchedules[0]?.scheduleGuid);
      setPunchType(punch.punchType === "ClockOut" ? "ClockOut" : "ClockIn");
      setRequestedPunchTimeLocal(punchTime(punch) || defaultLocalTime(selectedDate));
    });
  };

  const handlePreview = async () => {
    if (missingFields.length) {
      setLocalError(t("adjustment.validation.required"));
      return;
    }
    const requestedFingerprint = buildAttendancePunchAdjustmentFingerprint(payload);
    const request = previewRequestGateRef.current.begin(requestedFingerprint);
    await runLatestAttendanceAdjustmentRequest({
      gate: previewRequestGateRef.current,
      request,
      getCurrentFingerprint: () => latestPayloadFingerprintRef.current,
      operation: () => onPreview({ ...payload, reason: payload.reason.trim() }),
      onSuccess: (result) => {
        setPreview(result);
        setPreviewFingerprint(requestedFingerprint);
      },
      onError: (error) => {
        setLocalError(error instanceof Error ? error.message : t("adjustment.messages.previewFailed"));
      },
    });
  };

  const handleSubmit = async () => {
    if (
      !preview?.isValid
      || previewFingerprint !== buildAttendancePunchAdjustmentFingerprint(payload)
    ) return;
    if (!preview.previewRevision) {
      setLocalError(t("adjustment.messages.previewRevisionMissing"));
      return;
    }
    const requestedFingerprint = buildAttendancePunchAdjustmentFingerprint(payload);
    const request = submitRequestGateRef.current.begin(requestedFingerprint);
    await runLatestAttendanceAdjustmentRequest({
      gate: submitRequestGateRef.current,
      request,
      getCurrentFingerprint: () => latestPayloadFingerprintRef.current,
      operation: () => onSubmit({
        ...payload,
        reason: payload.reason.trim(),
        previewRevision: preview.previewRevision,
      }),
      onSuccess: () => {
        setReason("");
        setOriginalPunchGuid(undefined);
        setPreview(undefined);
        setPreviewFingerprint(undefined);
        setLocalError("");
      },
      onError: (error) => {
        setLocalError(error instanceof Error ? error.message : t("adjustment.messages.submitFailed"));
      },
    });
  };

  if (!canOpenForm) {
    return (
      <Card mode="outlined" style={styles.card}>
        <Card.Content>
          <Text variant="bodySmall" style={styles.muted}>
            {t("adjustment.outsideWindow")}
          </Text>
        </Card.Content>
      </Card>
    );
  }

  return (
    <Card mode="outlined" style={styles.card}>
      <Card.Content style={styles.content}>
        <View style={styles.header}>
          <View style={styles.flexText}>
            <Text variant="titleMedium">{t("adjustment.title")}</Text>
            <Text variant="bodySmall" style={styles.muted}>
              {isManagerStore ? t("adjustment.managerDescription") : t("adjustment.employeeDescription")}
            </Text>
          </View>
          {originalPunchGuid ? <Chip compact>{t("adjustment.editingExisting")}</Chip> : null}
        </View>

        {selectableSchedules.length > 1 ? (
          <View style={styles.existingPunches}>
            <Text variant="labelMedium">{t("adjustment.selectSchedule")}</Text>
            <View style={styles.chipRow}>
              {selectableSchedules.map((session, index) => (
                <Chip
                  key={session.scheduleGuid}
                  compact
                  selected={scheduleGuid === session.scheduleGuid}
                  onPress={() => updateDraft(() => {
                    setScheduleGuid(session.scheduleGuid);
                    setOriginalPunchGuid(undefined);
                  })}
                >
                  {t("adjustment.scheduleOption", {
                    number: index + 1,
                    start: session.startTime || "--:--",
                    end: session.endTime || "--:--",
                  })}
                </Chip>
              ))}
            </View>
          </View>
        ) : null}

        {today?.punches.length ? (
          <View style={styles.existingPunches}>
            <Text variant="labelMedium">{t("adjustment.selectExisting")}</Text>
            <View style={styles.chipRow}>
              {today.punches.map((punch) => (
                <Chip
                  key={punch.punchGuid || `${punch.punchType}-${punchTime(punch)}`}
                  compact
                  selected={originalPunchGuid === punch.punchGuid}
                  onPress={() => selectExistingPunch(punch)}
                >
                  {t(`punchTypes.${punch.punchType}`, punch.punchType)} {punchTime(punch).slice(11, 16) || punchTime(punch).slice(0, 5)}
                </Chip>
              ))}
              <Chip
                compact
                selected={!originalPunchGuid}
                onPress={() => updateDraft(() => setOriginalPunchGuid(undefined))}
              >
                {t("adjustment.addMissing")}
              </Chip>
            </View>
          </View>
        ) : null}

        <SegmentedButtons
          value={punchType}
          onValueChange={(value) => updateDraft(() => setPunchType(value as AttendancePunchType))}
          buttons={[
            { value: "ClockIn", label: t("actions.clockIn") },
            { value: "ClockOut", label: t("actions.clockOut") },
          ]}
        />
        <TextInput
          mode="outlined"
          dense
          label={t("adjustment.requestedTime")}
          value={requestedPunchTimeLocal}
          onChangeText={(value) => updateDraft(() => setRequestedPunchTimeLocal(value))}
          placeholder={`${selectedDate}T09:00`}
          autoCapitalize="none"
        />
        <TextInput
          mode="outlined"
          dense
          multiline
          label={t("fields.reason")}
          value={reason}
          onChangeText={(value) => updateDraft(() => setReason(value))}
          maxLength={500}
        />

        <Button
          mode="outlined"
          icon="calculator-variant-outline"
          disabled={isBusy || Boolean(missingFields.length)}
          loading={isBusy && !preview}
          onPress={() => void handlePreview()}
        >
          {t("adjustment.preview")}
        </Button>

        {preview ? (
          <View style={[styles.previewBox, !preview.isValid ? styles.previewInvalid : null]}>
            <Text variant="labelLarge">{t("adjustment.previewTitle")}</Text>
            {preview.proposedSession ? (
              <>
                <Text variant="bodySmall">
                  {t("adjustment.previewWorked", {
                    before: preview.existingSession?.workedMinutes ?? 0,
                    after: preview.proposedSession.workedMinutes ?? 0,
                    delta: preview.workedMinutesDelta,
                  })}
                </Text>
                <Text variant="bodySmall">
                  {t("adjustment.previewOvertime", {
                    before: preview.existingSession?.candidateOvertimeMinutes ?? 0,
                    after: preview.proposedSession.candidateOvertimeMinutes ?? 0,
                    delta: preview.candidateOvertimeMinutesDelta,
                  })}
                </Text>
                {preview.proposedSession.hasMissingClockOut ? (
                  <Text variant="bodySmall" style={styles.exceptionText}>
                    {t("today.timeline.missingClockOut")}
                  </Text>
                ) : null}
              </>
            ) : null}
            {!preview.isValid ? (
              <Text variant="bodySmall" style={styles.exceptionText}>
                {preview.validationMessage || preview.validationErrorCode || t("adjustment.messages.invalid")}
              </Text>
            ) : null}
            {isPreviewRevisionMissing ? (
              <Text variant="bodySmall" style={styles.exceptionText}>
                {t("adjustment.messages.previewRevisionMissing")}
              </Text>
            ) : null}
            {preview.wouldAutoApprove ? (
              <Text variant="bodySmall">{t("adjustment.directApplyNotice")}</Text>
            ) : (
              <Text variant="bodySmall" style={styles.muted}>{t("adjustment.pendingNotice")}</Text>
            )}
          </View>
        ) : null}

        {localError ? <Text variant="bodySmall" style={styles.exceptionText}>{localError}</Text> : null}
        <Button
          mode="contained"
          disabled={isBusy || !canSubmit}
          loading={isBusy && Boolean(preview)}
          onPress={() => void handleSubmit()}
        >
          {isManagerDirect ? t("adjustment.saveDirect") : t("adjustment.submitRequest")}
        </Button>
      </Card.Content>
    </Card>
  );
}

const styles = StyleSheet.create({
  card: { borderRadius: 8 },
  chipRow: { flexDirection: "row", flexWrap: "wrap", gap: 6 },
  content: { gap: 10 },
  exceptionText: { color: "#B42318" },
  existingPunches: { gap: 6 },
  flexText: { flex: 1 },
  header: { alignItems: "flex-start", flexDirection: "row", gap: 8 },
  muted: { color: "#6B7280" },
  previewBox: { backgroundColor: "#EEF6F0", borderRadius: 8, gap: 5, padding: 10 },
  previewInvalid: { backgroundColor: "#FFF5F5" },
});
