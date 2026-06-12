import { useState } from "react";
import { StyleSheet, View } from "react-native";
import { Button, Card, Chip, SegmentedButtons, Text, TextInput } from "react-native-paper";
import type { AttendanceLeaveRequest, AttendanceLeaveRequestPayload, AttendanceLeaveType } from "@/modules/attendance/types";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

const EMPTY_FORM: AttendanceLeaveRequestPayload = {
  leaveType: "AnnualLeave",
  startDate: "",
  endDate: "",
  reason: "",
};

export function LeaveRequestCard({
  requests,
  isBusy,
  onCreate,
  onCancel,
}: {
  requests: AttendanceLeaveRequest[];
  isBusy: boolean;
  onCreate: (payload: AttendanceLeaveRequestPayload) => void;
  onCancel: (leaveGuid: string) => void;
}) {
  const { t } = useAppTranslation(["attendance", "common"]);
  const [form, setForm] = useState<AttendanceLeaveRequestPayload>(EMPTY_FORM);

  const setField = (key: keyof AttendanceLeaveRequestPayload, value: string) => {
    setForm((current) => ({ ...current, [key]: value }));
  };

  const submit = () => {
    onCreate({
      ...form,
      leaveType: form.leaveType,
      startDate: form.startDate.trim(),
      endDate: form.endDate.trim(),
      reason: form.reason?.trim() || undefined,
    });
    setForm(EMPTY_FORM);
  };

  const canSubmit = Boolean(form.startDate.trim() && form.endDate.trim());

  return (
    <Card mode="elevated" style={styles.card}>
      <Card.Title title={t("sections.leave")} />
      <Card.Content style={styles.content}>
        <SegmentedButtons
          value={form.leaveType}
          onValueChange={(value) => setField("leaveType", value as AttendanceLeaveType)}
          buttons={[
            { value: "AnnualLeave", label: t("leaveTypes.AnnualLeave") },
            { value: "SickLeave", label: t("leaveTypes.SickLeave") },
            { value: "PublicHoliday", label: t("leaveTypes.PublicHoliday") },
          ]}
        />
        <View style={styles.dateRow}>
          <TextInput
            mode="outlined"
            label={t("fields.startDate")}
            value={form.startDate}
            placeholder={t("common:placeholders.date")}
            style={styles.dateInput}
            onChangeText={(value) => setField("startDate", value)}
          />
          <TextInput
            mode="outlined"
            label={t("fields.endDate")}
            value={form.endDate}
            placeholder={t("common:placeholders.date")}
            style={styles.dateInput}
            onChangeText={(value) => setField("endDate", value)}
          />
        </View>
        <TextInput
          mode="outlined"
          label={t("fields.reason")}
          value={form.reason}
          onChangeText={(value) => setField("reason", value)}
        />
        <View style={styles.actions}>
          <Button mode="contained" icon="send-outline" onPress={submit} disabled={!canSubmit || isBusy} loading={isBusy}>
            {t("actions.submitLeave")}
          </Button>
        </View>

        <View style={styles.list}>
          {requests.length ? (
            requests.map((item) => (
              <View key={item.leaveGuid} style={styles.itemRow}>
                <View style={styles.itemText}>
                  <Text variant="bodyMedium">
                    {t(`leaveTypes.${item.leaveType}`, item.leaveType)} · {item.startDate} - {item.endDate}
                  </Text>
                  <Text variant="bodySmall" style={styles.muted}>
                    {item.reason || t("common:na")}
                  </Text>
                </View>
                <Chip compact>{t(`statuses.${item.status}`, item.status)}</Chip>
                {item.status.toLowerCase() === "pending" ? (
                  <Button compact onPress={() => onCancel(item.leaveGuid)} disabled={isBusy}>
                    {t("common:actions.cancel")}
                  </Button>
                ) : null}
              </View>
            ))
          ) : (
            <Text variant="bodyMedium" style={styles.muted}>
              {t("leave.empty")}
            </Text>
          )}
        </View>
      </Card.Content>
    </Card>
  );
}

const styles = StyleSheet.create({
  actions: {
    alignItems: "center",
    flexDirection: "row",
    justifyContent: "flex-end",
  },
  card: {
    borderRadius: 8,
  },
  content: {
    gap: 12,
  },
  dateInput: {
    flex: 1,
  },
  dateRow: {
    flexDirection: "row",
    gap: 8,
  },
  itemRow: {
    alignItems: "center",
    borderColor: "#E5E7EB",
    borderRadius: 6,
    borderWidth: StyleSheet.hairlineWidth,
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
    padding: 10,
  },
  itemText: {
    flex: 1,
    minWidth: 180,
  },
  list: {
    gap: 8,
  },
  muted: {
    color: "#6B7280",
  },
});
