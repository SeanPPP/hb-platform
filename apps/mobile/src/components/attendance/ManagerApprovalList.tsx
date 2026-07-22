import { useState } from "react";
import { StyleSheet, View } from "react-native";
import { Button, Card, Chip, Text, TextInput } from "react-native-paper";
import type { AttendanceApproval } from "@/modules/attendance/types";
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
  onApprove: (approvalGuid: string, remark?: string) => void;
  onReject: (approvalGuid: string, remark?: string) => void;
}) {
  const { t } = useAppTranslation(["attendance", "common"]);
  const [remarks, setRemarks] = useState<Record<string, string>>({});

  const setRemark = (approvalGuid: string, value: string) => {
    setRemarks((current) => ({ ...current, [approvalGuid]: value }));
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
                  {item.employeeName || item.title}
                </Text>
                <Chip compact>
                  {t(`sourceTypes.${item.sourceType}`, item.sourceType)}
                </Chip>
              </View>
              <Text variant="bodyMedium">
                {item.storeName || item.storeCode || t("common:na")} ·{" "}
                {item.workDate || t("common:na")}
              </Text>
              {item.adjustment ? (
                <Text variant="bodySmall" style={styles.muted}>
                  {t(`punchTypes.${item.adjustment.punchType}`, item.adjustment.punchType)} ·{" "}
                  {item.adjustment.requestedPunchTimeUtc
                    ? toAttendanceDeviceLocalTime(item.adjustment.requestedPunchTimeUtc)
                    : item.adjustment.requestedPunchTimeLocal}
                  {item.adjustment.reason ? ` · ${item.adjustment.reason}` : ""}
                </Text>
              ) : item.detail ? (
                <Text variant="bodySmall" style={styles.muted}>
                  {item.detail}
                </Text>
              ) : null}
              <TextInput
                mode="outlined"
                dense
                label={t("fields.remark")}
                value={remarks[item.approvalGuid] ?? ""}
                onChangeText={(value) => setRemark(item.approvalGuid, value)}
              />
              <View style={styles.actions}>
                <Button
                  mode="outlined"
                  icon="close-circle-outline"
                  onPress={() =>
                    onReject(item.approvalGuid, remarks[item.approvalGuid])
                  }
                  disabled={isBusy || !canReview}
                >
                  {t("actions.reject")}
                </Button>
                <Button
                  mode="contained"
                  icon="check-circle-outline"
                  onPress={() =>
                    onApprove(item.approvalGuid, remarks[item.approvalGuid])
                  }
                  disabled={isBusy || !canReview}
                >
                  {t("actions.approve")}
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
