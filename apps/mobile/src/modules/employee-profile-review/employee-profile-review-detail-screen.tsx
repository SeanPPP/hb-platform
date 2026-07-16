import { useEffect, useMemo, useState } from "react";
import { Image, ScrollView, StyleSheet, View } from "react-native";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useLocalSearchParams, useRouter } from "expo-router";
import {
  ActivityIndicator,
  Button,
  Card,
  Chip,
  Dialog,
  Divider,
  Portal,
  Snackbar,
  Text,
  TextInput,
  useTheme,
} from "react-native-paper";
import { getEmployeeProfileReviewAccess } from "./access";
import {
  approveEmployeeProfileReviewApi,
  getEmployeeProfileReviewDetailApi,
  rejectEmployeeProfileReviewApi,
} from "./api";
import {
  getReviewFailureKind,
  isRejectReasonValid,
  maskSensitiveValue,
  shouldDisableReviewActions,
} from "./review-logic";
import type {
  EmployeeProfileReviewDetail,
  EmployeeProfileSensitiveField,
} from "./types";
import { useAppNavigationStore } from "@/modules/navigation/store";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { useAuthStore } from "@/store/auth-store";

const MASKED_FIELDS = new Set<EmployeeProfileSensitiveField>([
  "bankBsb",
  "bankAccountNumber",
  "superannuationAccountNumber",
  "identityId",
]);

function detailQueryKey(requestId: number) {
  return ["employeeProfileReview", "detail", requestId] as const;
}

function formatValue(value: string) {
  return value.trim() || "--";
}

export function EmployeeProfileReviewDetailScreen() {
  const router = useRouter();
  const queryClient = useQueryClient();
  const theme = useTheme();
  const { t } = useAppTranslation("employeeProfileReview");
  const params = useLocalSearchParams<{ requestId?: string | string[] }>();
  const requestIdValue = Array.isArray(params.requestId) ? params.requestId[0] : params.requestId;
  const requestId = Number(requestIdValue);
  const validRequestId = Number.isInteger(requestId) && requestId > 0;
  const currentUser = useAuthStore((state) => state.user);
  const sessionKind = useAuthStore((state) => state.sessionKind);
  const navigationItems = useAppNavigationStore((state) => state.items);
  const navigationReady = useAppNavigationStore((state) => state.isReady);
  const reviewAccess = useMemo(
    () => getEmployeeProfileReviewAccess({
      roleNames: currentUser?.roleNames,
      permissions: currentUser?.permissions,
      menuRouteNames: navigationItems.map((item) => item.routeName),
      sessionKind,
    }),
    [currentUser?.permissions, currentUser?.roleNames, navigationItems, sessionKind]
  );
  const detailQuery = useQuery({
    queryKey: detailQueryKey(requestId),
    enabled: navigationReady && reviewAccess.allowed && validRequestId,
    queryFn: () => getEmployeeProfileReviewDetailApi(requestId),
  });
  const [revealedFields, setRevealedFields] = useState<Set<EmployeeProfileSensitiveField>>(
    () => new Set()
  );
  const [dialogAction, setDialogAction] = useState<"approve" | "reject" | null>(null);
  const [reason, setReason] = useState("");
  const [staleAfterConflict, setStaleAfterConflict] = useState(false);
  const [snackbarMessage, setSnackbarMessage] = useState("");

  useEffect(() => {
    if (navigationReady && (!reviewAccess.allowed || !validRequestId)) {
      // 权限失效、设备模式、iOS 审核会话或非法参数都不得触发详情 API。
      router.replace("/(tabs)/settings");
    }
  }, [navigationReady, reviewAccess.allowed, router, validRequestId]);

  const reviewMutation = useMutation({
    mutationFn: async () => {
      if (dialogAction === "approve") {
        return approveEmployeeProfileReviewApi(requestId, reason);
      }
      if (dialogAction === "reject") {
        return rejectEmployeeProfileReviewApi(requestId, reason);
      }
      throw new Error("Review action is not selected");
    },
    onSuccess: async (result) => {
      queryClient.setQueryData(detailQueryKey(requestId), result);
      setDialogAction(null);
      setReason("");
      setSnackbarMessage(t(`messages.${result.status === "Approved" ? "approveSuccess" : "rejectSuccess"}`));
      await queryClient.invalidateQueries({ queryKey: ["employeeProfileReview", "requests"] });
    },
    onError: async (error) => {
      const kind = getReviewFailureKind(error);
      setDialogAction(null);
      if (kind === "conflict") {
        // 关键逻辑：冲突后立即刷新，但保留 stale 标记，避免对旧详情重复提交审核。
        setStaleAfterConflict(true);
        setSnackbarMessage(t("messages.conflict"));
        await Promise.all([
          detailQuery.refetch(),
          queryClient.invalidateQueries({ queryKey: ["employeeProfileReview", "requests"] }),
        ]);
        return;
      }
      if (kind === "forbidden") {
        setStaleAfterConflict(true);
        setSnackbarMessage(t("messages.permissionChanged"));
        return;
      }
      setSnackbarMessage(t("messages.reviewFailed"));
    },
  });

  const toggleReveal = (field: EmployeeProfileSensitiveField) => {
    setRevealedFields((current) => {
      const next = new Set(current);
      if (next.has(field)) {
        next.delete(field);
      } else {
        next.add(field);
      }
      return next;
    });
  };

  if (!navigationReady || (detailQuery.isLoading && !detailQuery.data)) {
    return (
      <View style={styles.centered}>
        <ActivityIndicator size="large" accessibilityLabel={t("detail.loading")} />
      </View>
    );
  }

  if (!reviewAccess.allowed || !validRequestId) {
    return (
      <View style={styles.centered}>
        <Text selectable>{t("messages.permissionChanged")}</Text>
      </View>
    );
  }

  if (detailQuery.isError || !detailQuery.data) {
    return (
      <View style={styles.centered}>
        <Text variant="titleMedium" selectable>{t("messages.detailLoadFailed")}</Text>
        <Button
          icon="refresh"
          mode="contained"
          contentStyle={styles.touchTarget}
          onPress={() => void detailQuery.refetch()}
        >
          {t("actions.retry")}
        </Button>
      </View>
    );
  }

  const detail = detailQuery.data;
  const definitions: Array<{
    key: EmployeeProfileSensitiveField;
    current: string;
    proposed: string;
  }> = [
    { key: "bankBsb", current: detail.currentSnapshot.bankBsb, proposed: detail.bankBsb },
    { key: "bankAccountNumber", current: detail.currentSnapshot.bankAccountNumber, proposed: detail.bankAccountNumber },
    { key: "superannuationCompanyName", current: detail.currentSnapshot.superannuationCompanyName, proposed: detail.superannuationCompanyName },
    { key: "superannuationCompanyCode", current: detail.currentSnapshot.superannuationCompanyCode, proposed: detail.superannuationCompanyCode },
    { key: "superannuationAccountNumber", current: detail.currentSnapshot.superannuationAccountNumber, proposed: detail.superannuationAccountNumber },
    { key: "identityType", current: detail.currentSnapshot.identityType, proposed: detail.identityType },
    { key: "identityId", current: detail.currentSnapshot.identityId, proposed: detail.identityId },
  ];
  const actionsDisabled = shouldDisableReviewActions(detail.status, staleAfterConflict);
  const rejectReasonInvalid = dialogAction === "reject" && !isRejectReasonValid(reason);

  return (
    <>
      <ScrollView
        contentInsetAdjustmentBehavior="automatic"
        style={{ backgroundColor: theme.colors.background }}
        contentContainerStyle={styles.content}
      >
        <Card mode="outlined">
          <Card.Content style={styles.summaryCard}>
            <View style={styles.summaryTitleRow}>
              <View style={styles.flex}>
                <Text variant="titleLarge" selectable>{detail.username || detail.userGuid}</Text>
                <Text variant="bodySmall" style={{ color: theme.colors.onSurfaceVariant }} selectable>
                  {detail.storeNames.join(" · ") || t("list.storeUnavailable")}
                </Text>
              </View>
              <Chip icon="shield-check-outline">{t(`statuses.${detail.status}`)}</Chip>
            </View>
            <Text variant="bodyMedium" selectable>{t("detail.securityNotice")}</Text>
          </Card.Content>
        </Card>

        {definitions.map(({ key, current, proposed }) => {
          const changed = detail.changedFields.includes(key);
          const masked = MASKED_FIELDS.has(key) && !revealedFields.has(key);
          return (
            <Card
              mode="outlined"
              key={key}
              style={changed ? { backgroundColor: theme.colors.secondaryContainer } : undefined}
            >
              <Card.Content style={styles.comparisonCard}>
                <View style={styles.fieldTitleRow}>
                  <Text variant="titleMedium" selectable>{t(`fields.${key}`)}</Text>
                  <View style={styles.fieldActions}>
                    {changed ? <Chip compact icon="swap-horizontal">{t("detail.changed")}</Chip> : null}
                    {MASKED_FIELDS.has(key) ? (
                      <Button
                        compact
                        mode="text"
                        icon={masked ? "eye-outline" : "eye-off-outline"}
                        contentStyle={styles.touchTarget}
                        accessibilityLabel={t(masked ? "actions.reveal" : "actions.hide")}
                        onPress={() => toggleReveal(key)}
                      >
                        {t(masked ? "actions.reveal" : "actions.hide")}
                      </Button>
                    ) : null}
                  </View>
                </View>
                <Divider />
                <View style={styles.valueRow}>
                  <View style={styles.valueColumn}>
                    <Text variant="labelMedium" style={{ color: theme.colors.onSurfaceVariant }} selectable>
                      {t("detail.currentValue")}
                    </Text>
                    <Text variant="bodyLarge" selectable>
                      {masked ? maskSensitiveValue(current) : formatValue(current)}
                    </Text>
                  </View>
                  <View style={styles.valueColumn}>
                    <Text variant="labelMedium" style={{ color: theme.colors.onSurfaceVariant }} selectable>
                      {t("detail.proposedValue")}
                    </Text>
                    <Text variant="bodyLarge" selectable style={changed ? { fontWeight: "700" } : undefined}>
                      {masked ? maskSensitiveValue(proposed) : formatValue(proposed)}
                    </Text>
                  </View>
                </View>
              </Card.Content>
            </Card>
          );
        })}

        <Card
          mode="outlined"
          style={detail.changedFields.includes("identityPhotoUrl")
            ? { backgroundColor: theme.colors.secondaryContainer }
            : undefined}
        >
          <Card.Content style={styles.comparisonCard}>
            <View style={styles.fieldTitleRow}>
              <Text variant="titleMedium" selectable>{t("fields.identityPhotoUrl")}</Text>
              {detail.changedFields.includes("identityPhotoUrl")
                ? <Chip compact icon="swap-horizontal">{t("detail.changed")}</Chip>
                : null}
            </View>
            <View style={styles.photoRow}>
              <PhotoPreview
                label={t("detail.currentValue")}
                hasPhoto={detail.currentSnapshot.hasIdentityPhoto}
                uri={detail.currentSnapshot.identityPhotoUrl}
                emptyLabel={t("detail.noPhoto")}
                unavailableLabel={t("detail.previewUnavailable")}
              />
              <PhotoPreview
                label={t("detail.proposedValue")}
                hasPhoto={detail.hasIdentityPhoto}
                uri={detail.identityPhotoUrl}
                emptyLabel={t("detail.noPhoto")}
                unavailableLabel={t("detail.previewUnavailable")}
              />
            </View>
          </Card.Content>
        </Card>

        {detail.reviewReason ? (
          <Card mode="outlined">
            <Card.Content style={styles.comparisonCard}>
              <Text variant="labelLarge" selectable>{t("detail.reviewReason")}</Text>
              <Text selectable>{detail.reviewReason}</Text>
            </Card.Content>
          </Card>
        ) : null}

        {staleAfterConflict ? (
          <Card mode="outlined" style={{ backgroundColor: theme.colors.errorContainer }}>
            <Card.Content>
              <Text selectable style={{ color: theme.colors.onErrorContainer }}>
                {t("messages.conflict")}
              </Text>
            </Card.Content>
          </Card>
        ) : null}

        <View style={styles.reviewActions}>
          <Button
            mode="outlined"
            icon="close-circle-outline"
            disabled={actionsDisabled || reviewMutation.isPending}
            contentStyle={styles.touchTarget}
            onPress={() => {
              setReason("");
              setDialogAction("reject");
            }}
          >
            {t("actions.reject")}
          </Button>
          <Button
            mode="contained"
            icon="check-circle-outline"
            disabled={actionsDisabled || reviewMutation.isPending}
            loading={reviewMutation.isPending}
            contentStyle={styles.touchTarget}
            onPress={() => {
              setReason("");
              setDialogAction("approve");
            }}
          >
            {t("actions.approve")}
          </Button>
        </View>
      </ScrollView>

      <Portal>
        <Dialog visible={dialogAction !== null} onDismiss={() => setDialogAction(null)}>
          <Dialog.Title>
            {t(dialogAction === "reject" ? "dialog.rejectTitle" : "dialog.approveTitle")}
          </Dialog.Title>
          <Dialog.Content style={styles.dialogContent}>
            <Text selectable>
              {t(dialogAction === "reject" ? "dialog.rejectDescription" : "dialog.approveDescription")}
            </Text>
            <TextInput
              mode="outlined"
              multiline
              numberOfLines={3}
              maxLength={1000}
              label={t(dialogAction === "reject" ? "dialog.reasonRequired" : "dialog.reasonOptional")}
              value={reason}
              error={rejectReasonInvalid}
              onChangeText={setReason}
            />
            {rejectReasonInvalid ? (
              <Text variant="bodySmall" style={{ color: theme.colors.error }} selectable>
                {t("dialog.reasonRequiredMessage")}
              </Text>
            ) : null}
          </Dialog.Content>
          <Dialog.Actions>
            <Button contentStyle={styles.touchTarget} onPress={() => setDialogAction(null)}>
              {t("actions.cancel")}
            </Button>
            <Button
              mode="contained"
              loading={reviewMutation.isPending}
              disabled={reviewMutation.isPending || rejectReasonInvalid}
              contentStyle={styles.touchTarget}
              onPress={() => reviewMutation.mutate()}
            >
              {t("actions.confirm")}
            </Button>
          </Dialog.Actions>
        </Dialog>
      </Portal>

      <Snackbar visible={Boolean(snackbarMessage)} onDismiss={() => setSnackbarMessage("")} duration={5000}>
        {snackbarMessage}
      </Snackbar>
    </>
  );
}

function PhotoPreview({
  label,
  hasPhoto,
  uri,
  emptyLabel,
  unavailableLabel,
}: {
  label: string;
  hasPhoto: boolean;
  uri: string;
  emptyLabel: string;
  unavailableLabel: string;
}) {
  return (
    <View style={styles.photoColumn}>
      <Text variant="labelMedium" selectable>{label}</Text>
      {hasPhoto && uri ? (
        <Image
          source={{ uri }}
          style={styles.photo}
          resizeMode="contain"
          accessibilityLabel={label}
        />
      ) : (
        <View style={styles.photoEmpty}>
          <Text variant="bodySmall" selectable>
            {hasPhoto ? unavailableLabel : emptyLabel}
          </Text>
        </View>
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  centered: { flex: 1, padding: 24, alignItems: "center", justifyContent: "center", gap: 16 },
  content: { padding: 16, paddingBottom: 40, gap: 12 },
  summaryCard: { gap: 10 },
  summaryTitleRow: { flexDirection: "row", alignItems: "flex-start", gap: 12 },
  comparisonCard: { gap: 12 },
  fieldTitleRow: { minHeight: 48, flexDirection: "row", alignItems: "center", justifyContent: "space-between", gap: 8 },
  fieldActions: { flexDirection: "row", alignItems: "center", flexWrap: "wrap", justifyContent: "flex-end", gap: 4 },
  valueRow: { flexDirection: "row", alignItems: "flex-start", gap: 16 },
  valueColumn: { flex: 1, gap: 4 },
  photoRow: { flexDirection: "row", alignItems: "stretch", gap: 12 },
  photoColumn: { flex: 1, gap: 8 },
  photo: { width: "100%", height: 180, borderRadius: 8, backgroundColor: "#F1F5F9" },
  photoEmpty: { minHeight: 180, alignItems: "center", justifyContent: "center", borderWidth: 1, borderColor: "#CBD5E1", borderRadius: 8 },
  reviewActions: { flexDirection: "row", gap: 12 },
  touchTarget: { minHeight: 48 },
  dialogContent: { gap: 12 },
});
