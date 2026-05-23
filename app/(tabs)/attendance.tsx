import { useCallback, useEffect, useMemo, useState } from "react";
import { RefreshControl, ScrollView, StyleSheet, View } from "react-native";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useRouter } from "expo-router";
import {
  ActivityIndicator,
  Button,
  SegmentedButtons,
  Snackbar,
  Text,
} from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import { AvailabilityForm } from "@/components/attendance/AvailabilityForm";
import { HolidayManagementCard } from "@/components/attendance/HolidayManagementCard";
import { ManagerApprovalList } from "@/components/attendance/ManagerApprovalList";
import { MonthDatePickerCard } from "@/components/attendance/MonthDatePicker";
import { ScheduleManagementCard } from "@/components/attendance/ScheduleManagementCard";
import { TodayPunchCard } from "@/components/attendance/TodayPunchCard";
import { WeeklyScheduleTable } from "@/components/attendance/WeeklyScheduleTable";
import { EmptyState } from "@/components/ui/EmptyState";
import { StorePickerModal } from "@/components/ui/StorePickerModal";
import {
  approveAttendanceApproval,
  cancelAvailability,
  createAttendanceHoliday,
  createAttendanceSchedule,
  createAvailability,
  deleteAttendanceHoliday,
  deleteAttendanceSchedule,
  getAttendanceHolidays,
  getAttendanceSchedulesWeek,
  getMyAttendanceToday,
  getMyAttendanceWeek,
  getMyAvailability,
  getPendingApprovals,
  punchAttendance,
  publishAttendanceSchedulesWeek,
  rejectAttendanceApproval,
  updateAttendanceHoliday,
  updateAttendanceSchedule,
  updateAvailability,
} from "@/modules/attendance/api";
import type {
  AttendanceAvailabilityPayload,
  AttendancePunchType,
  AttendanceSchedulePayload,
  AttendanceScheduleUpdatePayload,
  AttendanceStoreHolidayPayload,
} from "@/modules/attendance/types";
import type { Store } from "@/modules/shop/types";
import { useStores } from "@/modules/shop/use-stores";
import { useStoreUsers } from "@/modules/users";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { useAuthStore } from "@/store/auth-store";

type AttendanceSection = "employee" | "manager";

function toDateString(date: Date) {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}

function parseDate(value: Date | string) {
  const date =
    value instanceof Date ? new Date(value) : new Date(`${value}T00:00:00`);
  return Number.isNaN(date.getTime()) ? new Date() : date;
}

function getWeekStartDate(value: Date | string = new Date()) {
  const date = parseDate(value);
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

function isPrimaryStore(store: object) {
  return "isPrimary" in store && store.isPrimary === true;
}

function findStoreByCode(stores: Store[], storeCode?: string | null) {
  return storeCode
    ? stores.find((store) => store.storeCode === storeCode)
    : undefined;
}

const attendanceKeys = {
  today: (storeCode?: string, workDate?: string) =>
    [
      "attendance",
      "my",
      "today",
      storeCode ?? "all",
      workDate ?? "today",
    ] as const,
  week: (storeCode?: string, weekStartDate?: string) =>
    [
      "attendance",
      "my",
      "week",
      storeCode ?? "all",
      weekStartDate ?? "current",
    ] as const,
  availability: (storeCode?: string, weekStartDate?: string) =>
    [
      "attendance",
      "my",
      "availability",
      storeCode ?? "all",
      weekStartDate ?? "current",
    ] as const,
  approvals: (storeCode?: string) =>
    ["attendance", "approvals", "pending", storeCode ?? "all"] as const,
  holidays: (storeCode?: string) =>
    ["attendance", "holidays", storeCode ?? "all"] as const,
  schedulesWeek: (storeCode?: string, weekStartDate?: string) =>
    [
      "attendance",
      "schedules",
      "week",
      storeCode ?? "all",
      weekStartDate ?? "current",
    ] as const,
};

export default function AttendanceScreen() {
  const router = useRouter();
  const queryClient = useQueryClient();
  const { t } = useAppTranslation(["attendance", "common"]);
  const user = useAuthStore((state) => state.user);
  const access = useAuthStore((state) => state.access);
  const isAuthenticated = useAuthStore((state) => state.isAuthenticated);
  const {
    stores,
    selectedStoreCode: rememberedStoreCode,
    isHydratingSelection,
    selectStore,
  } = useStores();
  const [activeSection, setActiveSection] =
    useState<AttendanceSection>("employee");
  const [selectedDate, setSelectedDate] = useState(() =>
    toDateString(new Date()),
  );
  const [selectedStoreCode, setSelectedStoreCode] = useState<
    string | undefined
  >(undefined);
  const [storePickerVisible, setStorePickerVisible] = useState(false);
  const [managerWeekStartDate, setManagerWeekStartDate] =
    useState(getWeekStartDate);
  const [snackbarMessage, setSnackbarMessage] = useState("");
  const [snackbarVisible, setSnackbarVisible] = useState(false);

  const managerStores = useMemo(
    () => (access.isAdmin ? stores : stores.filter(isPrimaryStore)),
    [access.isAdmin, stores],
  );
  const canReview = access.isAdmin || access.isStoreManager;
  const isEmployeeSection = activeSection === "employee";
  const isManagerSection = canReview && activeSection === "manager";
  const sectionStores = isManagerSection ? managerStores : stores;
  const employeeWeekStartDate = useMemo(
    () => getWeekStartDate(selectedDate),
    [selectedDate],
  );
  const todayDate = useMemo(() => toDateString(new Date()), []);

  useEffect(() => {
    setSelectedStoreCode((current) => {
      if (
        current &&
        sectionStores.some((store) => store.storeCode === current)
      ) {
        return current;
      }

      if (
        rememberedStoreCode &&
        sectionStores.some((store) => store.storeCode === rememberedStoreCode)
      ) {
        return rememberedStoreCode;
      }

      return sectionStores[0]?.storeCode;
    });
  }, [rememberedStoreCode, sectionStores]);

  useEffect(() => {
    if (!canReview && activeSection !== "employee") {
      setActiveSection("employee");
    }
  }, [activeSection, canReview]);

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
    queryKey: attendanceKeys.today(selectedStoreCode, selectedDate),
    queryFn: () => getMyAttendanceToday(selectedStoreCode, selectedDate),
    enabled: Boolean(isAuthenticated && user && isEmployeeSection),
  });
  const weekQuery = useQuery({
    queryKey: attendanceKeys.week(selectedStoreCode, employeeWeekStartDate),
    queryFn: () =>
      getMyAttendanceWeek(selectedStoreCode, employeeWeekStartDate),
    enabled: Boolean(isAuthenticated && user && isEmployeeSection),
  });
  const availabilityQuery = useQuery({
    queryKey: attendanceKeys.availability(
      selectedStoreCode,
      employeeWeekStartDate,
    ),
    queryFn: () => getMyAvailability(selectedStoreCode, employeeWeekStartDate),
    enabled: Boolean(isAuthenticated && user && isEmployeeSection),
  });
  const approvalsQuery = useQuery({
    queryKey: attendanceKeys.approvals(selectedStoreCode),
    queryFn: () => getPendingApprovals(selectedStoreCode),
    enabled: Boolean(isAuthenticated && user && isManagerSection),
  });
  const storeUsersQuery = useStoreUsers(
    isManagerSection && selectedStoreCode ? selectedStoreCode : undefined,
  );
  const managerSchedulesQuery = useQuery({
    queryKey: attendanceKeys.schedulesWeek(
      selectedStoreCode,
      managerWeekStartDate,
    ),
    queryFn: () =>
      getAttendanceSchedulesWeek({
        storeCode: selectedStoreCode,
        weekStartDate: managerWeekStartDate,
      }),
    enabled: Boolean(
      isAuthenticated && user && isManagerSection && selectedStoreCode,
    ),
  });
  const holidaysQuery = useQuery({
    queryKey: attendanceKeys.holidays(selectedStoreCode),
    queryFn: () => getAttendanceHolidays({ storeCode: selectedStoreCode }),
    enabled: Boolean(
      isAuthenticated && user && isManagerSection && selectedStoreCode,
    ),
  });

  const invalidateEmployeeData = useCallback(async () => {
    await Promise.all([
      queryClient.invalidateQueries({
        queryKey: ["attendance", "my", "today"],
      }),
      queryClient.invalidateQueries({ queryKey: ["attendance", "my", "week"] }),
      queryClient.invalidateQueries({
        queryKey: ["attendance", "my", "availability"],
      }),
    ]);
  }, [queryClient]);

  const invalidateScheduleManagementData = useCallback(async () => {
    await Promise.all([
      queryClient.invalidateQueries({
        queryKey: ["attendance", "schedules", "week"],
      }),
      invalidateEmployeeData(),
    ]);
  }, [invalidateEmployeeData, queryClient]);

  const invalidateHolidayManagementData = useCallback(async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ["attendance", "holidays"] }),
      invalidateScheduleManagementData(),
    ]);
  }, [invalidateScheduleManagementData, queryClient]);

  const punchMutation = useMutation({
    mutationFn: punchAttendance,
    onSuccess: async () => {
      await invalidateEmployeeData();
      showMessage(t("messages.punchSuccess"));
    },
    onError: (error) =>
      showMessage(
        error instanceof Error ? error.message : t("messages.punchFailed"),
      ),
  });

  const createAvailabilityMutation = useMutation({
    mutationFn: createAvailability,
    onSuccess: async () => {
      await invalidateEmployeeData();
      showMessage(t("messages.availabilitySaved"));
    },
    onError: (error) =>
      showMessage(
        error instanceof Error ? error.message : t("messages.saveFailed"),
      ),
  });

  const updateAvailabilityMutation = useMutation({
    mutationFn: ({
      availabilityGuid,
      payload,
    }: {
      availabilityGuid: string;
      payload: AttendanceAvailabilityPayload;
    }) => updateAvailability(availabilityGuid, payload),
    onSuccess: async () => {
      await invalidateEmployeeData();
      showMessage(t("messages.availabilitySaved"));
    },
    onError: (error) =>
      showMessage(
        error instanceof Error ? error.message : t("messages.saveFailed"),
      ),
  });

  const cancelAvailabilityMutation = useMutation({
    mutationFn: cancelAvailability,
    onSuccess: async () => {
      await invalidateEmployeeData();
      showMessage(t("messages.availabilityCancelled"));
    },
    onError: (error) =>
      showMessage(
        error instanceof Error ? error.message : t("messages.cancelFailed"),
      ),
  });

  const approveMutation = useMutation({
    mutationFn: approveAttendanceApproval,
    onSuccess: async () => {
      await queryClient.invalidateQueries({
        queryKey: ["attendance", "approvals", "pending"],
      });
      await invalidateEmployeeData();
      showMessage(t("messages.approvalApproved"));
    },
    onError: (error) =>
      showMessage(
        error instanceof Error ? error.message : t("messages.approvalFailed"),
      ),
  });

  const rejectMutation = useMutation({
    mutationFn: rejectAttendanceApproval,
    onSuccess: async () => {
      await queryClient.invalidateQueries({
        queryKey: ["attendance", "approvals", "pending"],
      });
      await invalidateEmployeeData();
      showMessage(t("messages.approvalRejected"));
    },
    onError: (error) =>
      showMessage(
        error instanceof Error ? error.message : t("messages.approvalFailed"),
      ),
  });

  const createScheduleMutation = useMutation({
    mutationFn: createAttendanceSchedule,
    onSuccess: async () => {
      await invalidateScheduleManagementData();
      showMessage(t("messages.scheduleSaved"));
    },
    onError: (error) =>
      showMessage(
        error instanceof Error ? error.message : t("messages.saveFailed"),
      ),
  });

  const updateScheduleMutation = useMutation({
    mutationFn: ({
      scheduleGuid,
      payload,
    }: {
      scheduleGuid: string;
      payload: AttendanceScheduleUpdatePayload;
    }) => updateAttendanceSchedule(scheduleGuid, payload),
    onSuccess: async () => {
      await invalidateScheduleManagementData();
      showMessage(t("messages.scheduleSaved"));
    },
    onError: (error) =>
      showMessage(
        error instanceof Error ? error.message : t("messages.saveFailed"),
      ),
  });

  const deleteScheduleMutation = useMutation({
    mutationFn: deleteAttendanceSchedule,
    onSuccess: async () => {
      await invalidateScheduleManagementData();
      showMessage(t("messages.scheduleDeleted"));
    },
    onError: (error) =>
      showMessage(
        error instanceof Error ? error.message : t("messages.cancelFailed"),
      ),
  });

  const publishWeekMutation = useMutation({
    mutationFn: publishAttendanceSchedulesWeek,
    onSuccess: async () => {
      await invalidateScheduleManagementData();
      showMessage(t("messages.weekPublished"));
    },
    onError: (error) =>
      showMessage(
        error instanceof Error ? error.message : t("messages.publishFailed"),
      ),
  });

  const createHolidayMutation = useMutation({
    mutationFn: createAttendanceHoliday,
    onSuccess: async () => {
      await invalidateHolidayManagementData();
      showMessage(t("messages.holidaySaved"));
    },
    onError: (error) =>
      showMessage(
        error instanceof Error ? error.message : t("messages.saveFailed"),
      ),
  });

  const updateHolidayMutation = useMutation({
    mutationFn: ({
      holidayGuid,
      payload,
    }: {
      holidayGuid: string;
      payload: AttendanceStoreHolidayPayload;
    }) => updateAttendanceHoliday(holidayGuid, payload),
    onSuccess: async () => {
      await invalidateHolidayManagementData();
      showMessage(t("messages.holidaySaved"));
    },
    onError: (error) =>
      showMessage(
        error instanceof Error ? error.message : t("messages.saveFailed"),
      ),
  });

  const deleteHolidayMutation = useMutation({
    mutationFn: deleteAttendanceHoliday,
    onSuccess: async () => {
      await invalidateHolidayManagementData();
      showMessage(t("messages.holidayDeleted"));
    },
    onError: (error) =>
      showMessage(
        error instanceof Error ? error.message : t("messages.cancelFailed"),
      ),
  });

  const isEmployeeRefreshing =
    todayQuery.isRefetching ||
    weekQuery.isRefetching ||
    availabilityQuery.isRefetching;
  const isManagerRefreshing =
    approvalsQuery.isRefetching ||
    managerSchedulesQuery.isRefetching ||
    storeUsersQuery.isRefetching ||
    holidaysQuery.isRefetching;
  const isRefreshing = isEmployeeSection
    ? isEmployeeRefreshing
    : isManagerRefreshing;

  const isAvailabilityBusy =
    createAvailabilityMutation.isPending ||
    updateAvailabilityMutation.isPending ||
    cancelAvailabilityMutation.isPending;
  const isApprovalBusy = approveMutation.isPending || rejectMutation.isPending;
  const isScheduleBusy =
    createScheduleMutation.isPending ||
    updateScheduleMutation.isPending ||
    deleteScheduleMutation.isPending ||
    publishWeekMutation.isPending;
  const isHolidayBusy =
    createHolidayMutation.isPending ||
    updateHolidayMutation.isPending ||
    deleteHolidayMutation.isPending;

  const employeeInitialLoading =
    isEmployeeSection &&
    (todayQuery.isLoading ||
      weekQuery.isLoading ||
      availabilityQuery.isLoading);
  const managerInitialLoading =
    isManagerSection &&
    (approvalsQuery.isLoading ||
      storeUsersQuery.isLoading ||
      managerSchedulesQuery.isLoading ||
      holidaysQuery.isLoading);
  const employeeLoadError =
    todayQuery.error || weekQuery.error || availabilityQuery.error;
  const managerLoadError =
    isManagerSection &&
    (approvalsQuery.error ||
      storeUsersQuery.error ||
      managerSchedulesQuery.error ||
      holidaysQuery.error);

  const selectedStoreName = useMemo(
    () => findStoreByCode(stores, selectedStoreCode)?.storeName,
    [selectedStoreCode, stores],
  );

  const handleSelectStore = useCallback(
    async (store: Store | null) => {
      if (!store) {
        return;
      }

      setSelectedStoreCode(store.storeCode);
      setStorePickerVisible(false);

      try {
        await selectStore(store);
      } catch (error) {
        console.warn("[attendance] failed to persist store selection", error);
      }
    },
    [selectStore],
  );

  const handleBack = useCallback(() => {
    if (router.canGoBack()) {
      router.back();
      return;
    }

    router.replace("/(tabs)/settings");
  }, [router]);

  const refresh = useCallback(async () => {
    try {
      await Promise.all([
        isEmployeeSection ? todayQuery.refetch() : Promise.resolve(),
        isEmployeeSection ? weekQuery.refetch() : Promise.resolve(),
        isEmployeeSection ? availabilityQuery.refetch() : Promise.resolve(),
        isManagerSection ? approvalsQuery.refetch() : Promise.resolve(),
        isManagerSection && selectedStoreCode
          ? storeUsersQuery.refetch()
          : Promise.resolve(),
        isManagerSection && selectedStoreCode
          ? managerSchedulesQuery.refetch()
          : Promise.resolve(),
        isManagerSection && selectedStoreCode
          ? holidaysQuery.refetch()
          : Promise.resolve(),
      ]);
    } catch (error) {
      showMessage(
        error instanceof Error ? error.message : t("messages.refreshFailed"),
      );
    }
  }, [
    approvalsQuery,
    availabilityQuery,
    holidaysQuery,
    isEmployeeSection,
    isManagerSection,
    managerSchedulesQuery,
    selectedStoreCode,
    showMessage,
    storeUsersQuery,
    t,
    todayQuery,
    weekQuery,
  ]);

  const withSelectedStore = <T extends { storeCode?: string }>(
    payload: T,
  ): T => ({
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

    publishWeekMutation.mutate({
      storeCode: selectedStoreCode,
      weekStartDate: managerWeekStartDate,
    });
  };

  const handleCreateHoliday = (payload: AttendanceStoreHolidayPayload) => {
    const payloadWithStore = withSelectedStore(payload);
    if (!payloadWithStore.storeCode) {
      showMessage(t("messages.selectStoreFirst"));
      return;
    }

    createHolidayMutation.mutate(payloadWithStore);
  };

  const handleUpdateHoliday = (
    holidayGuid: string,
    payload: AttendanceStoreHolidayPayload,
  ) => {
    const payloadWithStore = withSelectedStore(payload);
    if (!payloadWithStore.storeCode) {
      showMessage(t("messages.selectStoreFirst"));
      return;
    }

    updateHolidayMutation.mutate({ holidayGuid, payload: payloadWithStore });
  };

  if (
    !isAuthenticated ||
    !user ||
    isHydratingSelection ||
    employeeInitialLoading
  ) {
    return (
      <SafeAreaView style={styles.container} edges={["top", "left", "right"]}>
        <View style={styles.centered}>
          <ActivityIndicator size="large" />
        </View>
        <Snackbar
          visible={snackbarVisible}
          onDismiss={() => setSnackbarVisible(false)}
        >
          {snackbarMessage}
        </Snackbar>
      </SafeAreaView>
    );
  }

  if (isEmployeeSection && employeeLoadError) {
    return (
      <SafeAreaView style={styles.container} edges={["top", "left", "right"]}>
        <View style={styles.centered}>
          <EmptyState
            title={t("messages.loadFailed")}
            description={
              employeeLoadError instanceof Error
                ? employeeLoadError.message
                : t("messages.loadFailed")
            }
            primaryAction={{
              label: t("common:actions.retry"),
              icon: "refresh",
              onPress: () => void refresh(),
            }}
            secondaryAction={{
              label: t("common:actions.back"),
              icon: "arrow-left",
              onPress: handleBack,
            }}
          />
        </View>
      </SafeAreaView>
    );
  }

  return (
    <SafeAreaView style={styles.container} edges={["top", "left", "right"]}>
      <ScrollView
        contentContainerStyle={styles.content}
        refreshControl={
          <RefreshControl
            refreshing={isRefreshing}
            onRefresh={() => void refresh()}
          />
        }
      >
        <View style={styles.header}>
          <View style={styles.headerText}>
            <Text variant="headlineSmall">{t("title")}</Text>
            <Text variant="bodyMedium" style={styles.muted}>
              {selectedStoreName || selectedStoreCode || t("subtitle")}
            </Text>
          </View>
          <Button
            compact
            mode="outlined"
            icon="refresh"
            onPress={() => void refresh()}
            disabled={isRefreshing}
          >
            {t("common:actions.refresh")}
          </Button>
        </View>

        {sectionStores.length > 1 ? (
          <Button
            mode="outlined"
            icon="storefront-outline"
            onPress={() => setStorePickerVisible(true)}
            contentStyle={styles.storePickerButtonContent}
          >
            {selectedStoreName ||
              selectedStoreCode ||
              t("common:labels.selectStore")}
          </Button>
        ) : null}

        {canReview ? (
          <SegmentedButtons
            value={activeSection}
            onValueChange={(value) =>
              setActiveSection(value as AttendanceSection)
            }
            buttons={[
              { value: "employee", label: t("sections.employeeAttendance") },
              { value: "manager", label: t("sections.managerSchedule") },
            ]}
            style={styles.sectionTabs}
          />
        ) : null}

        {isEmployeeSection ? (
          <>
            <TodayPunchCard
              title={t("sections.selectedDay")}
              selectedDate={selectedDate}
              storeName={selectedStoreName}
              allowPunch={selectedDate === todayDate}
              today={todayQuery.data}
              isLoading={todayQuery.isFetching}
              isPunching={punchMutation.isPending}
              onPunch={handlePunch}
            />
            <MonthDatePickerCard
              title={t("sections.calendar")}
              subtitle={selectedDate}
              value={selectedDate}
              onChange={setSelectedDate}
            />
            <WeeklyScheduleTable week={weekQuery.data} />
            <AvailabilityForm
              availability={availabilityQuery.data ?? []}
              defaultDate={selectedDate}
              isBusy={isAvailabilityBusy}
              onCreate={(payload) =>
                createAvailabilityMutation.mutate(withSelectedStore(payload))
              }
              onUpdate={(availabilityGuid, payload) =>
                updateAvailabilityMutation.mutate({
                  availabilityGuid,
                  payload: withSelectedStore(payload),
                })
              }
              onCancel={(availabilityGuid) =>
                cancelAvailabilityMutation.mutate(availabilityGuid)
              }
            />
          </>
        ) : null}

        {isManagerSection && managerStores.length === 0 ? (
          <EmptyState
            title={t("messages.noManagedStoreTitle")}
            description={t("messages.noManagedStoreDescription")}
            primaryAction={{
              label: t("common:actions.goToSettings"),
              icon: "cog-outline",
              onPress: () => router.replace("/(tabs)/settings"),
            }}
          />
        ) : null}

        {isManagerSection && managerStores.length > 0 && managerLoadError ? (
          <EmptyState
            title={t("messages.managerLoadFailedTitle")}
            description={
              managerLoadError instanceof Error
                ? managerLoadError.message
                : t("messages.loadFailed")
            }
            primaryAction={{
              label: t("common:actions.retry"),
              icon: "refresh",
              onPress: () => void refresh(),
            }}
            secondaryAction={{
              label: t("common:actions.back"),
              icon: "arrow-left",
              onPress: handleBack,
            }}
          />
        ) : null}

        {isManagerSection && managerStores.length > 0 && !managerLoadError ? (
          <>
            <ScheduleManagementCard
              weekStartDate={managerWeekStartDate}
              storeCode={selectedStoreCode}
              storeName={selectedStoreName}
              users={storeUsersQuery.data ?? []}
              schedules={managerSchedulesQuery.data ?? []}
              isLoading={
                storeUsersQuery.isLoading || managerSchedulesQuery.isLoading
              }
              isBusy={isScheduleBusy}
              onPreviousWeek={() =>
                setManagerWeekStartDate((current) => addWeeks(current, -1))
              }
              onNextWeek={() =>
                setManagerWeekStartDate((current) => addWeeks(current, 1))
              }
              onCreate={handleCreateSchedule}
              onUpdate={(scheduleGuid, payload) =>
                updateScheduleMutation.mutate({ scheduleGuid, payload })
              }
              onDelete={(scheduleGuid) =>
                deleteScheduleMutation.mutate(scheduleGuid)
              }
              onPublishWeek={handlePublishWeek}
            />
            <ManagerApprovalList
              approvals={approvalsQuery.data ?? []}
              isBusy={isApprovalBusy}
              onApprove={(approvalGuid, remark) =>
                approveMutation.mutate({ approvalGuid, remark })
              }
              onReject={(approvalGuid, remark) =>
                rejectMutation.mutate({ approvalGuid, remark })
              }
            />
            <HolidayManagementCard
              holidays={holidaysQuery.data ?? []}
              storeCode={selectedStoreCode}
              storeName={selectedStoreName}
              isBusy={isHolidayBusy}
              selectedDate={selectedDate}
              onCreate={handleCreateHoliday}
              onUpdate={handleUpdateHoliday}
              onDelete={(holidayGuid) =>
                deleteHolidayMutation.mutate(holidayGuid)
              }
            />
          </>
        ) : null}
      </ScrollView>
      <Snackbar
        visible={snackbarVisible}
        onDismiss={() => setSnackbarVisible(false)}
      >
        {snackbarMessage}
      </Snackbar>
      <StorePickerModal
        visible={storePickerVisible}
        stores={sectionStores}
        selectedStoreCode={selectedStoreCode}
        title={t("common:labels.selectStore")}
        cancelLabel={t("common:actions.cancel")}
        onDismiss={() => setStorePickerVisible(false)}
        onSelectStore={handleSelectStore}
      />
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
  sectionTabs: {
    marginTop: 2,
  },
  storePickerButtonContent: {
    justifyContent: "flex-start",
  },
});
