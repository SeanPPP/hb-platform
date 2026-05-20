import { useCallback, useEffect, useMemo, useState } from "react";
import { RefreshControl, ScrollView, StyleSheet, View } from "react-native";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useRouter } from "expo-router";
import { ActivityIndicator, Button, Chip, Snackbar, Text } from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import { AvailabilityForm } from "@/components/attendance/AvailabilityForm";
import { LeaveRequestCard } from "@/components/attendance/LeaveRequestCard";
import { ManagerApprovalList } from "@/components/attendance/ManagerApprovalList";
import { ScheduleManagementCard } from "@/components/attendance/ScheduleManagementCard";
import { TodayPunchCard } from "@/components/attendance/TodayPunchCard";
import { WeeklyScheduleTable } from "@/components/attendance/WeeklyScheduleTable";
import {
  approveAttendanceApproval,
  cancelAvailability,
  cancelLeaveRequest,
  createAttendanceSchedule,
  createAvailability,
  createLeaveRequest,
  deleteAttendanceSchedule,
  getAttendanceSchedulesWeek,
  getMyAttendanceToday,
  getMyAttendanceWeek,
  getMyAvailability,
  getMyLeaveRequests,
  getPendingApprovals,
  punchAttendance,
  publishAttendanceSchedulesWeek,
  rejectAttendanceApproval,
  updateAttendanceSchedule,
  updateAvailability,
} from "@/modules/attendance/api";
import type {
  AttendanceAvailabilityPayload,
  AttendanceLeaveRequestPayload,
  AttendancePunchType,
  AttendanceSchedulePayload,
  AttendanceScheduleUpdatePayload,
} from "@/modules/attendance/types";
import { useStoreUsers } from "@/modules/users";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { useAuthStore } from "@/store/auth-store";

function toDateString(date: Date) {
  return date.toISOString().slice(0, 10);
}

function getWeekStartDate(value = new Date()) {
  const date = new Date(value);
  const diff = (date.getDay() + 6) % 7;
  date.setDate(date.getDate() - diff);
  return toDateString(date);
}

function addWeeks(weekStartDate: string, weeks: number) {
  const date = new Date(`${weekStartDate}T00:00:00`);
  if (Number.isNaN(date.getTime())) {
    return getWeekStartDate();
  }
  date.setDate(date.getDate() + weeks * 7);
  return toDateString(date);
}

const attendanceKeys = {
  today: (storeCode?: string) => ["attendance", "my", "today", storeCode ?? "all"] as const,
  week: (storeCode?: string) => ["attendance", "my", "week", storeCode ?? "all"] as const,
  availability: (storeCode?: string) => ["attendance", "my", "availability", storeCode ?? "all"] as const,
  leaves: ["attendance", "my", "leave-requests"] as const,
  approvals: (storeCode?: string) => ["attendance", "approvals", "pending", storeCode ?? "all"] as const,
  schedulesWeek: (storeCode?: string, weekStartDate?: string) =>
    ["attendance", "schedules", "week", storeCode ?? "all", weekStartDate ?? "current"] as const,
};

export default function AttendanceScreen() {
  const router = useRouter();
  const queryClient = useQueryClient();
  const { t } = useAppTranslation(["attendance", "common"]);
  const user = useAuthStore((state) => state.user);
  const access = useAuthStore((state) => state.access);
  const isAuthenticated = useAuthStore((state) => state.isAuthenticated);
  const [selectedStoreCode, setSelectedStoreCode] = useState<string | undefined>(undefined);
  const [managerWeekStartDate, setManagerWeekStartDate] = useState(getWeekStartDate);
  const [snackbarMessage, setSnackbarMessage] = useState("");
  const [snackbarVisible, setSnackbarVisible] = useState(false);

  const stores = user?.stores ?? [];
  const canReview = access.isAdmin || access.isStoreManager;

  useEffect(() => {
    if (!selectedStoreCode && stores.length > 0) {
      setSelectedStoreCode(stores[0].storeCode);
    }
  }, [selectedStoreCode, stores]);

  const showMessage = useCallback((message: string) => {
    setSnackbarMessage(message);
    setSnackbarVisible(true);
  }, []);

  useEffect(() => {
    if (isAuthenticated && user) {
      return;
    }
    showMessage(t("messages.loginRequired"));
    router.replace("/(tabs)/settings");
  }, [isAuthenticated, router, showMessage, t, user]);

  const todayQuery = useQuery({
    queryKey: attendanceKeys.today(selectedStoreCode),
    queryFn: () => getMyAttendanceToday(selectedStoreCode),
    enabled: Boolean(isAuthenticated && user),
  });
  const weekQuery = useQuery({
    queryKey: attendanceKeys.week(selectedStoreCode),
    queryFn: () => getMyAttendanceWeek(selectedStoreCode),
    enabled: Boolean(isAuthenticated && user),
  });
  const availabilityQuery = useQuery({
    queryKey: attendanceKeys.availability(selectedStoreCode),
    queryFn: () => getMyAvailability(selectedStoreCode),
    enabled: Boolean(isAuthenticated && user),
  });
  const leaveQuery = useQuery({
    queryKey: attendanceKeys.leaves,
    queryFn: getMyLeaveRequests,
    enabled: Boolean(isAuthenticated && user),
  });
  const approvalsQuery = useQuery({
    queryKey: attendanceKeys.approvals(selectedStoreCode),
    queryFn: () => getPendingApprovals(selectedStoreCode),
    enabled: Boolean(isAuthenticated && user && canReview),
  });
  const storeUsersQuery = useStoreUsers(canReview ? selectedStoreCode : null);
  const managerSchedulesQuery = useQuery({
    queryKey: attendanceKeys.schedulesWeek(selectedStoreCode, managerWeekStartDate),
    queryFn: () => getAttendanceSchedulesWeek({ storeCode: selectedStoreCode, weekStartDate: managerWeekStartDate }),
    enabled: Boolean(isAuthenticated && user && canReview && selectedStoreCode),
  });

  const invalidateEmployeeData = useCallback(async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ["attendance", "my", "today"] }),
      queryClient.invalidateQueries({ queryKey: ["attendance", "my", "week"] }),
      queryClient.invalidateQueries({ queryKey: ["attendance", "my", "availability"] }),
      queryClient.invalidateQueries({ queryKey: attendanceKeys.leaves }),
    ]);
  }, [queryClient]);

  const invalidateScheduleManagementData = useCallback(async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ["attendance", "schedules", "week"] }),
      invalidateEmployeeData(),
    ]);
  }, [invalidateEmployeeData, queryClient]);

  const punchMutation = useMutation({
    mutationFn: punchAttendance,
    onSuccess: async () => {
      await invalidateEmployeeData();
      showMessage(t("messages.punchSuccess"));
    },
    onError: (error) => showMessage(error instanceof Error ? error.message : t("messages.punchFailed")),
  });

  const createAvailabilityMutation = useMutation({
    mutationFn: createAvailability,
    onSuccess: async () => {
      await invalidateEmployeeData();
      showMessage(t("messages.availabilitySaved"));
    },
    onError: (error) => showMessage(error instanceof Error ? error.message : t("messages.saveFailed")),
  });

  const updateAvailabilityMutation = useMutation({
    mutationFn: ({ availabilityGuid, payload }: { availabilityGuid: string; payload: AttendanceAvailabilityPayload }) =>
      updateAvailability(availabilityGuid, payload),
    onSuccess: async () => {
      await invalidateEmployeeData();
      showMessage(t("messages.availabilitySaved"));
    },
    onError: (error) => showMessage(error instanceof Error ? error.message : t("messages.saveFailed")),
  });

  const cancelAvailabilityMutation = useMutation({
    mutationFn: cancelAvailability,
    onSuccess: async () => {
      await invalidateEmployeeData();
      showMessage(t("messages.availabilityCancelled"));
    },
    onError: (error) => showMessage(error instanceof Error ? error.message : t("messages.cancelFailed")),
  });

  const createLeaveMutation = useMutation({
    mutationFn: createLeaveRequest,
    onSuccess: async () => {
      await invalidateEmployeeData();
      if (canReview) {
        await queryClient.invalidateQueries({ queryKey: ["attendance", "approvals", "pending"] });
      }
      showMessage(t("messages.leaveSubmitted"));
    },
    onError: (error) => showMessage(error instanceof Error ? error.message : t("messages.saveFailed")),
  });

  const cancelLeaveMutation = useMutation({
    mutationFn: cancelLeaveRequest,
    onSuccess: async () => {
      await invalidateEmployeeData();
      showMessage(t("messages.leaveCancelled"));
    },
    onError: (error) => showMessage(error instanceof Error ? error.message : t("messages.cancelFailed")),
  });

  const approveMutation = useMutation({
    mutationFn: approveAttendanceApproval,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["attendance", "approvals", "pending"] });
      await invalidateEmployeeData();
      showMessage(t("messages.approvalApproved"));
    },
    onError: (error) => showMessage(error instanceof Error ? error.message : t("messages.approvalFailed")),
  });

  const rejectMutation = useMutation({
    mutationFn: rejectAttendanceApproval,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["attendance", "approvals", "pending"] });
      await invalidateEmployeeData();
      showMessage(t("messages.approvalRejected"));
    },
    onError: (error) => showMessage(error instanceof Error ? error.message : t("messages.approvalFailed")),
  });

  const createScheduleMutation = useMutation({
    mutationFn: createAttendanceSchedule,
    onSuccess: async () => {
      await invalidateScheduleManagementData();
      showMessage(t("messages.scheduleSaved"));
    },
    onError: (error) => showMessage(error instanceof Error ? error.message : t("messages.saveFailed")),
  });

  const updateScheduleMutation = useMutation({
    mutationFn: ({ scheduleGuid, payload }: { scheduleGuid: string; payload: AttendanceScheduleUpdatePayload }) =>
      updateAttendanceSchedule(scheduleGuid, payload),
    onSuccess: async () => {
      await invalidateScheduleManagementData();
      showMessage(t("messages.scheduleSaved"));
    },
    onError: (error) => showMessage(error instanceof Error ? error.message : t("messages.saveFailed")),
  });

  const deleteScheduleMutation = useMutation({
    mutationFn: deleteAttendanceSchedule,
    onSuccess: async () => {
      await invalidateScheduleManagementData();
      showMessage(t("messages.scheduleDeleted"));
    },
    onError: (error) => showMessage(error instanceof Error ? error.message : t("messages.cancelFailed")),
  });

  const publishWeekMutation = useMutation({
    mutationFn: publishAttendanceSchedulesWeek,
    onSuccess: async () => {
      await invalidateScheduleManagementData();
      showMessage(t("messages.weekPublished"));
    },
    onError: (error) => showMessage(error instanceof Error ? error.message : t("messages.publishFailed")),
  });

  const isRefreshing =
    todayQuery.isRefetching ||
    weekQuery.isRefetching ||
    availabilityQuery.isRefetching ||
    leaveQuery.isRefetching ||
    approvalsQuery.isRefetching ||
    managerSchedulesQuery.isRefetching ||
    storeUsersQuery.isRefetching;

  const isAvailabilityBusy =
    createAvailabilityMutation.isPending ||
    updateAvailabilityMutation.isPending ||
    cancelAvailabilityMutation.isPending;
  const isLeaveBusy = createLeaveMutation.isPending || cancelLeaveMutation.isPending;
  const isApprovalBusy = approveMutation.isPending || rejectMutation.isPending;
  const isScheduleBusy =
    createScheduleMutation.isPending ||
    updateScheduleMutation.isPending ||
    deleteScheduleMutation.isPending ||
    publishWeekMutation.isPending;

  const initialLoading = todayQuery.isLoading || weekQuery.isLoading || availabilityQuery.isLoading || leaveQuery.isLoading;
  const loadError = todayQuery.error || weekQuery.error || availabilityQuery.error || leaveQuery.error;

  const selectedStoreName = useMemo(
    () => stores.find((store) => store.storeCode === selectedStoreCode)?.storeName,
    [selectedStoreCode, stores]
  );

  const refresh = useCallback(async () => {
    try {
      await Promise.all([
        todayQuery.refetch(),
        weekQuery.refetch(),
        availabilityQuery.refetch(),
        leaveQuery.refetch(),
        canReview ? approvalsQuery.refetch() : Promise.resolve(),
        canReview && selectedStoreCode ? storeUsersQuery.refetch() : Promise.resolve(),
        canReview && selectedStoreCode ? managerSchedulesQuery.refetch() : Promise.resolve(),
      ]);
    } catch (error) {
      showMessage(error instanceof Error ? error.message : t("messages.refreshFailed"));
    }
  }, [
    approvalsQuery,
    availabilityQuery,
    canReview,
    leaveQuery,
    managerSchedulesQuery,
    selectedStoreCode,
    showMessage,
    storeUsersQuery,
    t,
    todayQuery,
    weekQuery,
  ]);

  const withSelectedStore = <T extends { storeCode?: string }>(payload: T): T => ({
    ...payload,
    storeCode: payload.storeCode || selectedStoreCode,
  });

  const handlePunch = (punchType: AttendancePunchType) => {
    punchMutation.mutate({ punchType, storeCode: selectedStoreCode });
  };

  const handleCreateSchedule = (payload: AttendanceSchedulePayload) => {
    createScheduleMutation.mutate(payload);
  };

  const handlePublishWeek = () => {
    if (!selectedStoreCode) {
      showMessage(t("messages.selectStoreFirst"));
      return;
    }

    publishWeekMutation.mutate({ storeCode: selectedStoreCode, weekStartDate: managerWeekStartDate });
  };

  if (!isAuthenticated || !user || initialLoading) {
    return (
      <SafeAreaView style={styles.container} edges={["top", "left", "right"]}>
        <View style={styles.centered}>
          <ActivityIndicator size="large" />
        </View>
        <Snackbar visible={snackbarVisible} onDismiss={() => setSnackbarVisible(false)}>
          {snackbarMessage}
        </Snackbar>
      </SafeAreaView>
    );
  }

  if (loadError) {
    return (
      <SafeAreaView style={styles.container} edges={["top", "left", "right"]}>
        <View style={styles.centered}>
          <Text variant="bodyLarge" style={styles.errorText}>
            {loadError instanceof Error ? loadError.message : t("messages.loadFailed")}
          </Text>
          <Button mode="contained" icon="refresh" onPress={() => void refresh()}>
            {t("common:actions.retry")}
          </Button>
        </View>
      </SafeAreaView>
    );
  }

  return (
    <SafeAreaView style={styles.container} edges={["top", "left", "right"]}>
      <ScrollView
        contentContainerStyle={styles.content}
        refreshControl={<RefreshControl refreshing={isRefreshing} onRefresh={() => void refresh()} />}
      >
        <View style={styles.header}>
          <View style={styles.headerText}>
            <Text variant="headlineSmall">{t("title")}</Text>
            <Text variant="bodyMedium" style={styles.muted}>
              {selectedStoreName || selectedStoreCode || t("subtitle")}
            </Text>
          </View>
          <Button compact mode="outlined" icon="refresh" onPress={() => void refresh()} disabled={isRefreshing}>
            {t("common:actions.refresh")}
          </Button>
        </View>

        {stores.length > 1 ? (
          <View style={styles.storeChips}>
            {stores.map((store) => (
              <Chip
                key={store.storeCode}
                selected={store.storeCode === selectedStoreCode}
                onPress={() => setSelectedStoreCode(store.storeCode)}
              >
                {store.storeName || store.storeCode}
              </Chip>
            ))}
          </View>
        ) : null}

        <TodayPunchCard
          today={todayQuery.data}
          isLoading={todayQuery.isFetching}
          isPunching={punchMutation.isPending}
          onPunch={handlePunch}
        />
        <WeeklyScheduleTable week={weekQuery.data} />
        {canReview ? (
          <ScheduleManagementCard
            weekStartDate={managerWeekStartDate}
            storeCode={selectedStoreCode}
            storeName={selectedStoreName}
            users={storeUsersQuery.data ?? []}
            schedules={managerSchedulesQuery.data ?? []}
            isLoading={storeUsersQuery.isLoading || managerSchedulesQuery.isLoading}
            isBusy={isScheduleBusy}
            onPreviousWeek={() => setManagerWeekStartDate((current) => addWeeks(current, -1))}
            onNextWeek={() => setManagerWeekStartDate((current) => addWeeks(current, 1))}
            onCreate={handleCreateSchedule}
            onUpdate={(scheduleGuid, payload) => updateScheduleMutation.mutate({ scheduleGuid, payload })}
            onDelete={(scheduleGuid) => deleteScheduleMutation.mutate(scheduleGuid)}
            onPublishWeek={handlePublishWeek}
          />
        ) : null}
        <AvailabilityForm
          availability={availabilityQuery.data ?? []}
          isBusy={isAvailabilityBusy}
          onCreate={(payload) => createAvailabilityMutation.mutate(withSelectedStore(payload))}
          onUpdate={(availabilityGuid, payload) =>
            updateAvailabilityMutation.mutate({ availabilityGuid, payload: withSelectedStore(payload) })
          }
          onCancel={(availabilityGuid) => cancelAvailabilityMutation.mutate(availabilityGuid)}
        />
        <LeaveRequestCard
          requests={leaveQuery.data ?? []}
          isBusy={isLeaveBusy}
          onCreate={(payload: AttendanceLeaveRequestPayload) => createLeaveMutation.mutate(withSelectedStore(payload))}
          onCancel={(leaveGuid) => cancelLeaveMutation.mutate(leaveGuid)}
        />
        {canReview ? (
          <ManagerApprovalList
            approvals={approvalsQuery.data ?? []}
            isBusy={isApprovalBusy}
            onApprove={(approvalGuid, remark) => approveMutation.mutate({ approvalGuid, remark })}
            onReject={(approvalGuid, remark) => rejectMutation.mutate({ approvalGuid, remark })}
          />
        ) : null}
      </ScrollView>
      <Snackbar visible={snackbarVisible} onDismiss={() => setSnackbarVisible(false)}>
        {snackbarMessage}
      </Snackbar>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  centered: {
    alignItems: "center",
    flex: 1,
    gap: 12,
    justifyContent: "center",
    padding: 20,
  },
  container: {
    backgroundColor: "#F7F8FA",
    flex: 1,
  },
  content: {
    gap: 12,
    padding: 16,
    paddingBottom: 32,
  },
  errorText: {
    color: "#B42318",
    textAlign: "center",
  },
  header: {
    alignItems: "center",
    flexDirection: "row",
    gap: 12,
    justifyContent: "space-between",
  },
  headerText: {
    flex: 1,
  },
  muted: {
    color: "#6B7280",
  },
  storeChips: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
  },
});
