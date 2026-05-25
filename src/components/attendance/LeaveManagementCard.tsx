import { useEffect, useMemo, useRef, useState } from "react";
import {
  Image,
  ScrollView,
  StyleSheet,
  View,
  useWindowDimensions,
} from "react-native";
import { CameraView, useCameraPermissions } from "expo-camera";
import {
  Button,
  Card,
  HelperText,
  Modal,
  Portal,
  RadioButton,
  SegmentedButtons,
  Text,
  TextInput,
} from "react-native-paper";
import {
  MonthDatePickerField,
  normalizeMonthDate,
} from "@/components/attendance/MonthDatePicker";
import {
  getAttendanceLeaveAttachmentUploadSignature,
} from "@/modules/attendance/api";
import { uploadAttendanceLeaveAttachmentToSignedUrl } from "@/modules/attendance/leave-attachment-upload";
import type {
  AttendanceLeaveRequestPayload,
  AttendanceLeaveType,
} from "@/modules/attendance/types";
import type { StoreUserListItem } from "@/modules/users/types";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

type LeaveFormState = AttendanceLeaveRequestPayload & {
  userGuid: string;
  reason: string;
};

const SUPPORTED_LEAVE_TYPES: AttendanceLeaveType[] = [
  "AnnualLeave",
  "SickLeave",
];

function createEmptyForm(storeCode?: string): LeaveFormState {
  const today = normalizeMonthDate();
  return {
    userGuid: "",
    storeCode,
    leaveType: "AnnualLeave",
    startDate: today,
    endDate: today,
    startTime: "",
    endTime: "",
    reason: "",
    attachmentUrl: "",
  };
}

function normalizeEmploymentType(value?: string) {
  return value?.trim().toLowerCase() ?? "";
}

function isEligibleEmployee(user: StoreUserListItem) {
  const employmentType = normalizeEmploymentType(user.employmentType);
  return employmentType === "fulltime" || employmentType === "parttime";
}

function getEmployeeLabel(user: StoreUserListItem) {
  return (
    user.fullName?.trim() ||
    user.username?.trim() ||
    user.email?.trim() ||
    user.userGUID
  );
}

function trimToUndefined(value?: string) {
  const nextValue = value?.trim();
  return nextValue ? nextValue : undefined;
}

interface LeaveManagementCardProps {
  storeCode?: string;
  storeName?: string;
  users: StoreUserListItem[];
  isBusy: boolean;
  onSubmit: (payload: AttendanceLeaveRequestPayload) => void | Promise<void>;
  onShowMessage?: (message: string) => void;
}

export function LeaveManagementCard({
  storeCode,
  storeName,
  users,
  isBusy,
  onSubmit,
  onShowMessage,
}: LeaveManagementCardProps) {
  const { t } = useAppTranslation(["attendance", "common"]);
  const { height } = useWindowDimensions();
  const [form, setForm] = useState<LeaveFormState>(() => createEmptyForm(storeCode));
  const [employeePickerVisible, setEmployeePickerVisible] = useState(false);
  const [cameraVisible, setCameraVisible] = useState(false);
  const [cameraError, setCameraError] = useState("");
  const [uploadPreviewUri, setUploadPreviewUri] = useState("");
  const [isUploadingAttachment, setIsUploadingAttachment] = useState(false);
  const [permission, requestPermission] = useCameraPermissions();
  const cameraRef = useRef<CameraView | null>(null);

  const eligibleUsers = useMemo(
    () => users.filter(isEligibleEmployee).sort((left, right) =>
      getEmployeeLabel(left).localeCompare(getEmployeeLabel(right)),
    ),
    [users],
  );
  const selectedEmployee = useMemo(
    () => eligibleUsers.find((user) => user.userGUID === form.userGuid),
    [eligibleUsers, form.userGuid],
  );
  const canSelectEmployee = eligibleUsers.length > 0;
  const isSickLeave = form.leaveType === "SickLeave";
  const canSubmit = Boolean(
    storeCode &&
      selectedEmployee &&
      form.startDate.trim() &&
      form.endDate.trim() &&
      (!isSickLeave || form.attachmentUrl?.trim()),
  );
  const employeeListHeight = Math.max(160, Math.min(420, height * 0.5));

  useEffect(() => {
    setForm((current) => ({ ...current, storeCode }));
  }, [storeCode]);

  useEffect(() => {
    if (!form.userGuid) {
      return;
    }
    if (eligibleUsers.some((user) => user.userGUID === form.userGuid)) {
      return;
    }
    setForm((current) => ({ ...current, userGuid: "" }));
  }, [eligibleUsers, form.userGuid]);

  const setField = <K extends keyof LeaveFormState>(
    key: K,
    value: LeaveFormState[K],
  ) => {
    setForm((current) => ({ ...current, [key]: value }));
  };

  const handleSelectEmployee = (user: StoreUserListItem) => {
    setField("userGuid", user.userGUID);
    setEmployeePickerVisible(false);
  };

  const handleLeaveTypeChange = (value: string) => {
    const leaveType = value as AttendanceLeaveType;
    setForm((current) => ({
      ...current,
      leaveType,
      attachmentUrl: leaveType === "SickLeave" ? current.attachmentUrl : "",
    }));
    if (leaveType !== "SickLeave") {
      setUploadPreviewUri("");
      setCameraError("");
      setCameraVisible(false);
    }
  };

  const resetForm = () => {
    setForm(createEmptyForm(storeCode));
    setUploadPreviewUri("");
    setCameraError("");
  };

  const submit = async () => {
    if (!storeCode || !selectedEmployee) {
      return;
    }

    try {
      await onSubmit({
        userGuid: selectedEmployee.userGUID,
        storeCode,
        leaveType: form.leaveType,
        startDate: form.startDate.trim(),
        endDate: form.endDate.trim(),
        startTime: trimToUndefined(form.startTime),
        endTime: trimToUndefined(form.endTime),
        reason: trimToUndefined(form.reason),
        attachmentUrl: isSickLeave ? trimToUndefined(form.attachmentUrl) : undefined,
      });
      resetForm();
    } catch {
      // The parent mutation already shows the API error; keep the form intact for retry.
    }
  };

  const handleOpenCamera = async () => {
    if (!permission?.granted) {
      const nextPermission = await requestPermission();
      if (!nextPermission.granted) {
        const message = t("leaveManagement.messages.permissionRequired");
        setCameraError(message);
        onShowMessage?.(message);
        return;
      }
    }

    setCameraError("");
    setCameraVisible(true);
  };

  const handleCapturePhoto = async () => {
    if (!cameraRef.current || isUploadingAttachment) {
      return;
    }

    try {
      setIsUploadingAttachment(true);
      setCameraError("");
      const photo = await cameraRef.current.takePictureAsync({
        quality: 0.7,
      });

      if (!photo?.uri) {
        throw new Error(t("leaveManagement.messages.captureFailed"));
      }

      const fileResponse = await fetch(photo.uri);
      const fileBlob = await fileResponse.blob();
      if (!fileBlob.size) {
        throw new Error(t("leaveManagement.messages.uploadFailed"));
      }

      const fileName = `leave-attachment-${Date.now()}.jpg`;
      const signature = await getAttendanceLeaveAttachmentUploadSignature({
        fileName,
        contentType: "image/jpeg",
        fileSize: fileBlob.size,
      });
      const result = await uploadAttendanceLeaveAttachmentToSignedUrl(
        photo.uri,
        signature,
      );

      setField("attachmentUrl", result.downloadUrl);
      setUploadPreviewUri(photo.uri);
      setCameraVisible(false);
      onShowMessage?.(t("leaveManagement.messages.attachmentUploaded"));
    } catch (error) {
      const message =
        error instanceof Error
          ? error.message
          : t("leaveManagement.messages.uploadFailed");
      setCameraError(message);
      onShowMessage?.(message);
    } finally {
      setIsUploadingAttachment(false);
    }
  };

  return (
    <>
      <Card mode="elevated" style={styles.card}>
        <Card.Title
          title={t("tabs.leaveManagement")}
          subtitle={
            storeName ||
            storeCode ||
            t("leaveManagement.noStore", {
              defaultValue: "Select a store first",
            })
          }
        />
        <Card.Content style={styles.content}>
          <Button
            mode="outlined"
            icon="account-outline"
            onPress={() => setEmployeePickerVisible(true)}
            disabled={!canSelectEmployee || isBusy}
            contentStyle={styles.selectionButtonContent}
          >
            {selectedEmployee
              ? getEmployeeLabel(selectedEmployee)
              : t("leaveManagement.fields.employee")}
          </Button>
          <HelperText type="error" visible={!storeCode}>
            {t("leaveManagement.noStore", {
              defaultValue: "Select a store first",
            })}
          </HelperText>
          <HelperText
            type="info"
            visible={Boolean(storeCode && !canSelectEmployee && !isBusy)}
          >
            {t("leaveManagement.noEligibleEmployees")}
          </HelperText>

          <SegmentedButtons
            value={form.leaveType}
            onValueChange={handleLeaveTypeChange}
            buttons={SUPPORTED_LEAVE_TYPES.map((leaveType) => ({
              value: leaveType,
              label: t(`leaveTypes.${leaveType}`),
              disabled: isBusy,
            }))}
          />

          <View style={styles.dateRow}>
            <MonthDatePickerField
              label={t("fields.startDate")}
              value={form.startDate}
              onChange={(value) => setField("startDate", value)}
              style={styles.dateField}
            />
            <MonthDatePickerField
              label={t("fields.endDate")}
              value={form.endDate}
              onChange={(value) => setField("endDate", value)}
              style={styles.dateField}
            />
          </View>

          <View style={styles.timeRow}>
            <TextInput
              mode="outlined"
              label={t("fields.startTimeOptional")}
              value={form.startTime ?? ""}
              placeholder="09:00"
              style={styles.timeInput}
              onChangeText={(value) => setField("startTime", value)}
              disabled={isBusy}
            />
            <TextInput
              mode="outlined"
              label={t("fields.endTimeOptional")}
              value={form.endTime ?? ""}
              placeholder="17:00"
              style={styles.timeInput}
              onChangeText={(value) => setField("endTime", value)}
              disabled={isBusy}
            />
          </View>

          <TextInput
            mode="outlined"
            label={t("fields.reason")}
            value={form.reason}
            multiline
            onChangeText={(value) => setField("reason", value)}
            disabled={isBusy}
          />

          {isSickLeave ? (
            <View style={styles.attachmentSection}>
              <Text variant="titleSmall">
                {t("leaveManagement.fields.attachment")}
              </Text>
              <Button
                mode={!form.attachmentUrl ? "contained" : "outlined"}
                icon="camera-outline"
                onPress={() => void handleOpenCamera()}
                disabled={isBusy || isUploadingAttachment}
                loading={isUploadingAttachment}
              >
                {uploadPreviewUri || form.attachmentUrl
                  ? t("leaveManagement.actions.retakePhoto")
                  : t("leaveManagement.actions.takePhoto")}
              </Button>
              {uploadPreviewUri ? (
                <Image source={{ uri: uploadPreviewUri }} style={styles.previewImage} />
              ) : null}
              <Text variant="bodySmall" style={styles.muted}>
                {t("leaveManagement.attachmentRequired")}
              </Text>
              <HelperText type="error" visible={!form.attachmentUrl}>
                {t("leaveManagement.messages.attachmentRequired")}
              </HelperText>
            </View>
          ) : null}

          <View style={styles.actions}>
            <Button mode="outlined" onPress={resetForm} disabled={isBusy}>
              {t("common:actions.cancel")}
            </Button>
            <Button
              mode="contained"
              icon="send-outline"
              onPress={() => void submit()}
              disabled={!canSubmit || isBusy}
              loading={isBusy}
            >
              {t("actions.submitLeave")}
            </Button>
          </View>
        </Card.Content>
      </Card>

      <Portal>
        <Modal
          visible={employeePickerVisible}
          onDismiss={() => setEmployeePickerVisible(false)}
          contentContainerStyle={styles.modalContainer}
        >
          <Text variant="titleLarge" style={styles.modalTitle}>
            {t("leaveManagement.fields.employee")}
          </Text>
          <ScrollView
            style={[styles.modalList, { maxHeight: employeeListHeight }]}
            contentContainerStyle={styles.modalListContent}
            nestedScrollEnabled
          >
            {eligibleUsers.map((user) => (
              <View key={user.userGUID} style={styles.employeeRow}>
                <RadioButton
                  value={user.userGUID}
                  status={form.userGuid === user.userGUID ? "checked" : "unchecked"}
                  onPress={() => handleSelectEmployee(user)}
                />
                <Button
                  mode="text"
                  onPress={() => handleSelectEmployee(user)}
                  style={styles.employeeButton}
                  contentStyle={styles.employeeButtonContent}
                >
                  {getEmployeeLabel(user)}
                </Button>
              </View>
            ))}
          </ScrollView>
          <View style={styles.actions}>
            <Button onPress={() => setEmployeePickerVisible(false)}>
              {t("common:actions.cancel")}
            </Button>
          </View>
        </Modal>
      </Portal>

      <Portal>
        <Modal
          visible={cameraVisible}
          onDismiss={() => {
            if (!isUploadingAttachment) {
              setCameraVisible(false);
            }
          }}
          contentContainerStyle={styles.cameraModal}
        >
          <Text variant="titleLarge" style={styles.modalTitle}>
            {t("leaveManagement.camera.title")}
          </Text>
          {!permission?.granted ? (
            <View style={styles.cameraPermissionState}>
              <Text variant="titleMedium">
                {t("leaveManagement.camera.permissionTitle")}
              </Text>
              <Text variant="bodyMedium" style={styles.muted}>
                {t("leaveManagement.camera.permissionDescription")}
              </Text>
              <Button mode="contained" onPress={() => void handleOpenCamera()}>
                {t("leaveManagement.actions.grantCameraPermission")}
              </Button>
            </View>
          ) : (
            <>
              <CameraView
                ref={cameraRef}
                facing="back"
                style={styles.cameraPreview}
              />
              <HelperText type="info" visible>
                {t("leaveManagement.messages.cameraReady")}
              </HelperText>
              <HelperText type="error" visible={Boolean(cameraError)}>
                {cameraError}
              </HelperText>
              <View style={styles.actions}>
                <Button
                  mode="outlined"
                  onPress={() => setCameraVisible(false)}
                  disabled={isUploadingAttachment}
                >
                  {t("common:actions.cancel")}
                </Button>
                <Button
                  mode="contained"
                  icon="camera"
                  onPress={() => void handleCapturePhoto()}
                  loading={isUploadingAttachment}
                  disabled={isUploadingAttachment}
                >
                  {t("leaveManagement.actions.capture")}
                </Button>
              </View>
            </>
          )}
        </Modal>
      </Portal>
    </>
  );
}

const styles = StyleSheet.create({
  actions: {
    alignItems: "center",
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
    justifyContent: "flex-end",
  },
  attachmentSection: {
    gap: 8,
  },
  cameraModal: {
    alignSelf: "center",
    backgroundColor: "#FFFFFF",
    borderRadius: 12,
    gap: 12,
    padding: 16,
    width: "92%",
  },
  cameraPermissionState: {
    gap: 12,
  },
  cameraPreview: {
    borderRadius: 8,
    height: 360,
    overflow: "hidden",
  },
  card: {
    borderRadius: 8,
  },
  content: {
    gap: 10,
  },
  dateField: {
    flex: 1,
    minWidth: 150,
  },
  dateRow: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
  },
  employeeButton: {
    flex: 1,
  },
  employeeButtonContent: {
    justifyContent: "flex-start",
  },
  employeeRow: {
    alignItems: "center",
    flexDirection: "row",
    gap: 4,
    minHeight: 48,
  },
  modalContainer: {
    alignSelf: "center",
    backgroundColor: "#FFFFFF",
    borderRadius: 12,
    padding: 20,
    width: "88%",
  },
  modalList: {
    flexGrow: 0,
  },
  modalListContent: {
    paddingBottom: 4,
  },
  modalTitle: {
    marginBottom: 8,
  },
  muted: {
    color: "#6B7280",
  },
  previewImage: {
    alignSelf: "flex-start",
    borderRadius: 8,
    height: 120,
    width: 120,
  },
  selectionButtonContent: {
    justifyContent: "flex-start",
  },
  timeInput: {
    flex: 1,
    minWidth: 140,
  },
  timeRow: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
  },
});
