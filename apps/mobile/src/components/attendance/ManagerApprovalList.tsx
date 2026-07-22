import { useState } from "react";
import { StyleSheet, View } from "react-native";
import { Button, Card, Chip, Text, TextInput } from "react-native-paper";
import {
  getSupplementalAttendanceApprovalDetail,
  isKnownAttendanceApprovalSourceType,
  validateAttendanceOvertimeApproval,
  type OvertimeApprovalAction,
} from "@/modules/attendance/attendance-approval";
import type {
  AttendanceApproval,
  AttendanceApprovalPayload,
} from "@/modules/attendance/types";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { toAttendanceDeviceLocalTime } from "@/modules/attendance/attendance-device-time";

export function ManagerApprovalList({
  title,
  emptyMessage,
  approvals,
  isBusy,
  canReview = true,
  onApprove,
  onReject,
}: {
  title?: string;
  emptyMessage?: string;
  approvals: AttendanceApproval[];
  isBusy: boolean;
  canReview?: boolean;
  onApprove: (payload: AttendanceApprovalPayload) => void;
  onReject: (payload: AttendanceApprovalPayload) => void;
}) {
  const { t } = useAppTranslation(["attendance", "common"]);
  const [remarks, setRemarks] = useState<Record<string, string>>({});
  const [approvedMinutes, setApprovedMinutes] = useState<Record<string, string>>({});
  const [approvalErrors, setApprovalErrors] = useState<Record<string, string>>({});

  const setRemark = (approvalGuid: string, value: string) => {
    setRemarks((current) => ({ ...current, [approvalGuid]: value }));
    setApprovalErrors((current) => ({ ...current, [approvalGuid]: "" }));
  };

  const approvalTitle = (item: AttendanceApproval) => (
    isKnownAttendanceApprovalSourceType(item.sourceType)
      ? t(`approvals.presentation.${item.sourceType}.title`)
      : item.title || item.sourceType
  );

  const approvalDetails = (item: AttendanceApproval) => {
    if (!isKnownAttendanceApprovalSourceType(item.sourceType)) {
      return item.detail ? (
        <Text variant="bodySmall" style={styles.muted}>{item.detail}</Text>
      ) : null;
    }

    if (item.sourceType === "Punch" || item.sourceType === "Leave") {
      const supplementalDetail = getSupplementalAttendanceApprovalDetail({
        sourceType: item.sourceType,
        detail: item.detail,
        displayedTitle: approvalTitle(item),
      });
      return (
        <View style={styles.detailList}>
          <Text variant="bodySmall" style={styles.muted}>
            {t(`approvals.presentation.${item.sourceType}.detail`, {
              workDate: item.workDate || t("common:na"),
            })}
          </Text>
          {supplementalDetail ? (
            <Text variant="bodySmall" style={styles.muted}>{supplementalDetail}</Text>
          ) : null}
        </View>
      );
    }

    if (item.sourceType === "MissingClockOut") {
      return (
        <Text variant="bodySmall" style={styles.muted}>
          {t("approvals.presentation.MissingClockOut.detail", {
            workDate: item.workDate || t("common:na"),
          })}
        </Text>
      );
    }

    if (item.sourceType === "Overtime") {
      return (
        <View style={styles.detailList}>
          <Text variant="bodySmall" style={styles.muted}>
            {t("approvals.overtimeCandidate", { minutes: item.candidateOvertimeMinutes ?? 0 })}
          </Text>
          {item.approvedOvertimeMinutes !== undefined ? (
            <Text variant="bodySmall" style={styles.muted}>
              {t("approvals.overtimeApprovedValue", { minutes: item.approvedOvertimeMinutes })}
            </Text>
          ) : null}
        </View>
      );
    }

    const adjustment = item.adjustment;
    if (!adjustment) return null;
    const requestedTime = adjustment.requestedPunchTimeLocal
      || (adjustment.requestedPunchTimeUtc
        ? toAttendanceDeviceLocalTime(adjustment.requestedPunchTimeUtc)
        : undefined);
    return (
      <View style={styles.detailList}>
        {adjustment.originalPunchTimeLocal ? (
          <Text variant="bodySmall" style={styles.muted}>
            {t("approvals.adjustmentOriginal", { time: adjustment.originalPunchTimeLocal })}
          </Text>
        ) : null}
        {requestedTime ? (
          <Text variant="bodySmall" style={styles.muted}>
            {t("approvals.adjustmentRequested", {
              punchType: t(`punchTypes.${adjustment.punchType}`, adjustment.punchType),
              time: requestedTime,
            })}
          </Text>
        ) : null}
        {adjustment.effectivePunchTimeLocal ? (
          <Text variant="bodySmall" style={styles.muted}>
            {t("approvals.adjustmentEffective", { time: adjustment.effectivePunchTimeLocal })}
          </Text>
        ) : null}
        {adjustment.reason ? (
          <Text variant="bodySmall" style={styles.muted}>
            {t("fields.reason")}: {adjustment.reason}
          </Text>
        ) : null}
      </View>
    );
  };

  const submit = (
    item: AttendanceApproval,
    action: OvertimeApprovalAction,
  ) => {
    const remark = remarks[item.approvalGuid];
    const isOvertime = item.sourceType === "Overtime";
    const candidateMinutes = item.candidateOvertimeMinutes ?? 0;
    const approvedOvertimeMinutes = isOvertime
      ? action === "reject"
        ? 0
        : Number(approvedMinutes[item.approvalGuid] ?? candidateMinutes)
      : undefined;
    const validationError = isOvertime
      ? validateAttendanceOvertimeApproval({
        candidateMinutes,
        approvedMinutes: approvedOvertimeMinutes ?? 0,
        action,
        remark,
      })
      : null;

    if (validationError) {
      setApprovalErrors((current) => ({
        ...current,
        [item.approvalGuid]: t(`approvals.overtimeValidation.${validationError}`),
      }));
      return;
    }

    const payload: AttendanceApprovalPayload = {
      approvalGuid: item.approvalGuid,
      remark,
      approvedOvertimeMinutes,
    };
    if (action === "approve") onApprove(payload);
    else onReject(payload);
  };

  return (
    <Card mode="elevated" style={styles.card}>
      <Card.Title title={title ?? t("sections.approvals")} />
      <Card.Content style={styles.content}>
        {approvals.length ? (
          approvals.map((item) => (
            <View key={item.approvalGuid} style={styles.item}>
              <View style={styles.itemHeader}>
                <Text variant="titleSmall" style={styles.flexText}>
                  {item.employeeName || approvalTitle(item)}
                </Text>
                <Chip compact>
                  {t(`sourceTypes.${item.sourceType}`, item.sourceType)}
                </Chip>
              </View>
              <Text variant="bodyMedium">
                {item.storeName || item.storeCode || t("common:na")} ·{" "}
                {item.workDate || t("common:na")}
              </Text>
              <Text variant="bodyMedium">{approvalTitle(item)}</Text>
              {approvalDetails(item)}
              {item.sourceType === "Overtime" ? (
                <>
                  <TextInput
                    mode="outlined"
                    dense
                    label={t("approvals.overtimeApproved")}
                    value={approvedMinutes[item.approvalGuid] ?? String(item.candidateOvertimeMinutes ?? 0)}
                    onChangeText={(value) => {
                      setApprovedMinutes((current) => ({ ...current, [item.approvalGuid]: value }));
                      setApprovalErrors((current) => ({ ...current, [item.approvalGuid]: "" }));
                    }}
                    keyboardType="number-pad"
                    placeholder={t("approvals.overtimeIncrement")}
                  />
                </>
              ) : null}
              <TextInput
                mode="outlined"
                dense
                label={t("fields.remark")}
                value={remarks[item.approvalGuid] ?? ""}
                onChangeText={(value) => setRemark(item.approvalGuid, value)}
              />
              {approvalErrors[item.approvalGuid] ? (
                <Text variant="bodySmall" style={styles.error}>
                  {approvalErrors[item.approvalGuid]}
                </Text>
              ) : null}
              <View style={styles.actions}>
                <Button
                  mode="outlined"
                  icon="close-circle-outline"
                  onPress={() => submit(item, "reject")}
                  disabled={isBusy || !canReview}
                >
                  {item.sourceType === "Overtime"
                    ? t("actions.rejectOvertime")
                    : t("actions.reject")}
                </Button>
                <Button
                  mode="contained"
                  icon="check-circle-outline"
                  onPress={() => submit(item, "approve")}
                  disabled={isBusy || !canReview}
                >
                  {item.sourceType === "Overtime"
                    ? t("actions.approveOvertime")
                    : t("actions.approve")}
                </Button>
              </View>
            </View>
          ))
        ) : (
          <Text variant="bodyMedium" style={styles.muted}>
            {emptyMessage ?? t("approvals.empty")}
          </Text>
        )}
      </Card.Content>
    </Card>
  );
}

const styles = StyleSheet.create({
  actions: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
    justifyContent: "flex-end",
  },
  card: {
    borderRadius: 8,
  },
  content: {
    gap: 10,
  },
  detailList: {
    gap: 3,
  },
  error: {
    color: "#B42318",
  },
  flexText: {
    flex: 1,
  },
  item: {
    borderColor: "#E5E7EB",
    borderRadius: 6,
    borderWidth: StyleSheet.hairlineWidth,
    gap: 8,
    padding: 10,
  },
  itemHeader: {
    alignItems: "center",
    flexDirection: "row",
    gap: 8,
  },
  muted: {
    color: "#6B7280",
  },
});
