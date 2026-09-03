import { useCallback, useEffect, useMemo, useState } from "react";
import { Alert, FlatList, RefreshControl, ScrollView, StyleSheet, View } from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";
import {
  ActivityIndicator,
  Button,
  Card,
  Chip,
  Modal,
  Portal,
  Searchbar,
  SegmentedButtons,
  Snackbar,
  Text,
} from "react-native-paper";
import { EmptyState } from "@/components/ui/EmptyState";
import { StorePickerModal } from "@/components/ui/StorePickerModal";
import type { Store } from "@/modules/shop/types";
import { useStores } from "@/modules/shop/use-stores";
import {
  useAppDeviceStatuses,
  useAppDeviceStatusSummary,
  useDeviceManagementDevices,
  useDeviceManagementMutations,
} from "@/modules/device-management/hooks";
import { DEVICE_STATUS, getDeviceStatusKey, type DeviceStatusKey } from "@/modules/device-management/status";
import type {
  AppDeviceOnlineState,
  AppDeviceStatus,
  AppDeviceStatusQuery,
  DeviceManagementDevice,
  DeviceManagementQuery,
} from "@/modules/device-management/types";
import { resolveLocalizedErrorMessage } from "@/shared/i18n/error-message";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { useAuthStore } from "@/store/auth-store";

type StatusFilter = "all" | "pendingConfirmation" | "active" | "disabled" | "locked" | "unregistered";
type DeviceSystemFilter = "all" | "Android" | "iOS" | "Windows" | "Mac";
type DeviceTypeFilter = "all" | "Mobile" | "PDA" | "POS" | "Admin";
type DeviceAction = "activate" | "disable" | "lock";
type DeviceManagementViewMode = "registered" | "appUsage";
type DeviceRow = DeviceManagementDevice & {
  systemDeviceNumber?: string | null;
  deviceNumber?: string | null;
  deviceType?: string | null;
  deviceSystem?: string | null;
};
type DeviceManagementListRow = DeviceRow | AppDeviceStatus;

const STATUS_FILTERS: StatusFilter[] = ["all", "pendingConfirmation", "active", "disabled", "locked", "unregistered"];
const DEVICE_SYSTEM_FILTERS: DeviceSystemFilter[] = ["all", "Android", "iOS", "Windows", "Mac"];
const DEVICE_TYPE_FILTERS: DeviceTypeFilter[] = ["all", "Mobile", "PDA", "POS", "Admin"];
const APP_ONLINE_FILTERS: AppDeviceOnlineState[] = ["all", "online", "offline"];
const PAGE_SIZE = 20;

function getDeviceKey(item: DeviceRow) {
  return String(item.id);
}

function getDeviceTitle(item: DeviceRow, fallback: string) {
  return item.systemDeviceNumber || item.deviceNumber || item.deviceName || item.hardwareId || fallback;
}

function getHardwareTail(hardwareId?: string | null) {
  const value = hardwareId?.trim();
  if (!value) {
    return null;
  }

  if (value.length <= 12) {
    return value;
  }

  return `...${value.slice(-8)}`;
}

function getUpdateTail(updateId?: string | null) {
  const value = updateId?.trim();
  if (!value) {
    return null;
  }

  return value.length <= 10 ? value : `...${value.slice(-10)}`;
}

function getAppDeviceKey(item: AppDeviceStatus) {
  return item.id || item.hardwareId;
}

function getAppDeviceTitle(item: AppDeviceStatus, fallback: string) {
  return item.systemDeviceNumber || item.hardwareId || fallback;
}

function getAppDeviceUser(item: AppDeviceStatus, fallback: string) {
  return item.lastSeenUserFullName || item.lastSeenUsername || item.lastSeenUserGuid || fallback;
}

function getAppVersionText(item: AppDeviceStatus, fallback: string) {
  if (item.appVersion && item.appBuildVersion) {
    return `${item.appVersion} (${item.appBuildVersion})`;
  }

  return item.appVersion || item.appBuildVersion || fallback;
}

function formatDateTime(value: string | undefined | null, locale: string) {
  if (!value) {
    return null;
  }

  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) {
    return value;
  }

  return new Intl.DateTimeFormat(locale, {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(parsed);
}

function statusFilterToQueryValue(status: StatusFilter) {
  switch (status) {
    case "pendingConfirmation":
      return DEVICE_STATUS.PENDING_CONFIRMATION;
    case "disabled":
      return DEVICE_STATUS.DISABLED;
    case "active":
      return DEVICE_STATUS.ACTIVE;
    case "locked":
      return DEVICE_STATUS.LOCKED;
    case "unregistered":
      return DEVICE_STATUS.UNREGISTERED;
    case "all":
    default:
      return undefined;
  }
}

function deviceSystemFilterToQueryValue(deviceSystem: DeviceSystemFilter) {
  // 全部选项只存在于前端 UI，避免把 all 传给后端枚举筛选。
  return deviceSystem === "all" ? undefined : deviceSystem;
}

function deviceTypeFilterToQueryValue(deviceType: DeviceTypeFilter) {
  // 全部选项只存在于前端 UI，避免把 all 传给后端枚举筛选。
  return deviceType === "all" ? undefined : deviceType;
}

function getStatusStyle(statusKey: DeviceStatusKey) {
  switch (statusKey) {
    case "active":
      return styles.enabledChip;
    case "disabled":
      return styles.disabledChip;
    case "locked":
      return styles.lockedChip;
    case "pendingConfirmation":
      return styles.pendingChip;
    default:
      return styles.unknownChip;
  }
}

export default function DeviceManagementScreen() {
  const { t, language } = useAppTranslation(["deviceManagement", "common"]);
  const access = useAuthStore((state) => state.access);

  if (!access.canViewDeviceRegistration) {
    return (
      <SafeAreaView style={styles.screen} edges={["top", "left", "right"]}>
        <EmptyState title={t("messages.noAccessTitle")} description={t("messages.noAccessDescription")} />
      </SafeAreaView>
    );
  }

  return (
    <DeviceManagementAdminContent
      canManageDeviceRegistration={access.canManageDeviceRegistration}
      language={language}
      t={t}
    />
  );
}

function DeviceManagementAdminContent({
  canManageDeviceRegistration,
  language,
  t,
}: {
  canManageDeviceRegistration: boolean;
  language: string;
  t: ReturnType<typeof useAppTranslation>["t"];
}) {
  const { stores, isLoading: storesLoading } = useStores();
  const [viewMode, setViewMode] = useState<DeviceManagementViewMode>("registered");
  const [filtersVisible, setFiltersVisible] = useState(false);
  const [storePickerVisible, setStorePickerVisible] = useState(false);
  const [resumeFiltersAfterStorePicker, setResumeFiltersAfterStorePicker] = useState(false);
  const [managedStoreCode, setManagedStoreCode] = useState<string | null>(null);
  const [keywordInput, setKeywordInput] = useState("");
  const [keyword, setKeyword] = useState("");
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("all");
  const [deviceSystemFilter, setDeviceSystemFilter] = useState<DeviceSystemFilter>("all");
  const [deviceTypeFilter, setDeviceTypeFilter] = useState<DeviceTypeFilter>("all");
  const [pageNumber, setPageNumber] = useState(1);
  const [pagedDevices, setPagedDevices] = useState<DeviceRow[]>([]);
  const [appOnlineFilter, setAppOnlineFilter] = useState<AppDeviceOnlineState>("all");
  const [appPageNumber, setAppPageNumber] = useState(1);
  const [pagedAppDevices, setPagedAppDevices] = useState<AppDeviceStatus[]>([]);
  const [busyDeviceKey, setBusyDeviceKey] = useState<string | null>(null);
  const [snackbarMessage, setSnackbarMessage] = useState("");

  const managedStore = useMemo(
    () => stores.find((store) => store.storeCode === managedStoreCode) ?? null,
    [managedStoreCode, stores]
  );
  const trimmedKeyword = keyword.trim();
  const query = useMemo<DeviceManagementQuery>(
    () => ({
      keyword: trimmedKeyword || undefined,
      status: statusFilterToQueryValue(statusFilter),
      deviceSystem: deviceSystemFilterToQueryValue(deviceSystemFilter),
      deviceType: deviceTypeFilterToQueryValue(deviceTypeFilter),
      storeCode: managedStoreCode || null,
      pageNumber,
      pageSize: PAGE_SIZE,
    }),
    [deviceSystemFilter, deviceTypeFilter, managedStoreCode, pageNumber, statusFilter, trimmedKeyword]
  );
  const appStatusQueryParams = useMemo<AppDeviceStatusQuery>(
    () => ({
      keyword: trimmedKeyword || undefined,
      deviceSystem: deviceSystemFilterToQueryValue(deviceSystemFilter),
      storeCode: managedStoreCode || null,
      onlineState: appOnlineFilter,
      pageNumber: appPageNumber,
      pageSize: PAGE_SIZE,
    }),
    [appOnlineFilter, appPageNumber, deviceSystemFilter, managedStoreCode, trimmedKeyword]
  );
  const appSummaryQueryParams = useMemo(
    () => ({
      keyword: trimmedKeyword || undefined,
      deviceSystem: deviceSystemFilterToQueryValue(deviceSystemFilter),
      storeCode: managedStoreCode || null,
    }),
    [deviceSystemFilter, managedStoreCode, trimmedKeyword]
  );

  const devicesQuery = useDeviceManagementDevices(query, viewMode === "registered");
  const appStatusesQuery = useAppDeviceStatuses(appStatusQueryParams, viewMode === "appUsage");
  const appSummaryQuery = useAppDeviceStatusSummary(appSummaryQueryParams, viewMode === "appUsage");
  const { activateMutation, disableMutation, lockMutation } = useDeviceManagementMutations();
  const devices = pagedDevices;
  const appDevices = pagedAppDevices;
  const registrationTotal = devicesQuery.data?.pagination.totalCount ?? devices.length;
  const appTotal = appStatusesQuery.data?.pagination.totalCount ?? appDevices.length;
  const total = viewMode === "appUsage" ? appSummaryQuery.data?.total ?? appTotal : registrationTotal;
  const hasNextPage =
    viewMode === "appUsage"
      ? appPageNumber < (appStatusesQuery.data?.pagination.totalPages ?? 1)
      : pageNumber < (devicesQuery.data?.pagination.totalPages ?? 1);
  const hasActiveFilters = Boolean(
    viewMode === "appUsage"
      ? trimmedKeyword ||
          managedStoreCode ||
          deviceSystemFilter !== "all" ||
          appOnlineFilter !== "all"
      : trimmedKeyword ||
          managedStoreCode ||
          statusFilter !== "all" ||
          deviceSystemFilter !== "all" ||
          deviceTypeFilter !== "all"
  );
  const activeFilterCount =
    viewMode === "appUsage"
      ? [trimmedKeyword, managedStoreCode, deviceSystemFilter !== "all", appOnlineFilter !== "all"].filter(Boolean).length
      : [
          trimmedKeyword,
          managedStoreCode,
          statusFilter !== "all",
          deviceSystemFilter !== "all",
          deviceTypeFilter !== "all",
        ].filter(Boolean).length;

  useEffect(() => {
    setPageNumber(1);
    setPagedDevices([]);
  }, [deviceSystemFilter, deviceTypeFilter, managedStoreCode, statusFilter, trimmedKeyword]);

  useEffect(() => {
    setAppPageNumber(1);
    setPagedAppDevices([]);
  }, [appOnlineFilter, deviceSystemFilter, managedStoreCode, trimmedKeyword]);

  useEffect(() => {
    if (!devicesQuery.data?.devices) {
      return;
    }

    const nextDevices = devicesQuery.data.devices as DeviceRow[];
    setPagedDevices((current) => {
      if (pageNumber === 1) {
        return nextDevices;
      }

      const seen = new Set(current.map((item) => getDeviceKey(item)));
      const appended = nextDevices.filter((item) => !seen.has(getDeviceKey(item)));
      return [...current, ...appended];
    });
  }, [devicesQuery.data?.devices, pageNumber]);

  useEffect(() => {
    if (!appStatusesQuery.data?.devices) {
      return;
    }

    const nextDevices = appStatusesQuery.data.devices;
    setPagedAppDevices((current) => {
      if (appPageNumber === 1) {
        return nextDevices;
      }

      const seen = new Set(current.map((item) => getAppDeviceKey(item)));
      const appended = nextDevices.filter((item) => !seen.has(getAppDeviceKey(item)));
      return [...current, ...appended];
    });
  }, [appPageNumber, appStatusesQuery.data?.devices]);

  const submitKeyword = useCallback(() => {
    setKeyword(keywordInput.trim());
  }, [keywordInput]);

  const clearFilters = useCallback(() => {
    setManagedStoreCode(null);
    setStatusFilter("all");
    setDeviceSystemFilter("all");
    setDeviceTypeFilter("all");
    setAppOnlineFilter("all");
    setKeywordInput("");
    setKeyword("");
  }, []);

  const applyFilters = useCallback(() => {
    submitKeyword();
    setFiltersVisible(false);
  }, [submitKeyword]);

  const openStorePickerFromFilters = useCallback(() => {
    setResumeFiltersAfterStorePicker(true);
    setFiltersVisible(false);
    setStorePickerVisible(true);
  }, []);

  const closeStorePicker = useCallback(() => {
    setStorePickerVisible(false);
    if (resumeFiltersAfterStorePicker) {
      setResumeFiltersAfterStorePicker(false);
      setFiltersVisible(true);
    }
  }, [resumeFiltersAfterStorePicker]);

  const handleSelectStore = useCallback((store: Store | null) => {
    setManagedStoreCode(store?.storeCode ?? null);
    closeStorePicker();
  }, [closeStorePicker]);

  const handleRefresh = useCallback(async () => {
    try {
      if (viewMode === "appUsage") {
        if (appPageNumber !== 1) {
          setAppPageNumber(1);
          return;
        }
        await Promise.all([appStatusesQuery.refetch(), appSummaryQuery.refetch()]);
        return;
      }

      if (pageNumber !== 1) {
        setPageNumber(1);
        return;
      }
      await devicesQuery.refetch();
    } catch (error) {
      console.warn("[device-management] refresh failed", error);
      setSnackbarMessage(resolveLocalizedErrorMessage(error, { t, language, fallbackKey: "messages.refreshFailed" }));
    }
  }, [appPageNumber, appStatusesQuery, appSummaryQuery, devicesQuery, language, pageNumber, t, viewMode]);

  const handleLoadMore = useCallback(() => {
    if (viewMode === "appUsage") {
      if (!hasNextPage || appStatusesQuery.isFetching) {
        return;
      }
      setAppPageNumber((current) => current + 1);
      return;
    }

    if (!hasNextPage || devicesQuery.isFetching) {
      return;
    }
    setPageNumber((current) => current + 1);
  }, [appStatusesQuery.isFetching, devicesQuery.isFetching, hasNextPage, viewMode]);

  const runDeviceAction = useCallback(
    (action: DeviceAction, item: DeviceRow) => {
      const deviceKey = getDeviceKey(item);
      const deviceTitle = getDeviceTitle(item, t("fields.unnamedDevice"));
      const actionLabel = t(`actions.${action}`);
      const mutation =
        action === "activate" ? activateMutation : action === "disable" ? disableMutation : lockMutation;

      Alert.alert(
        t("dialogs.confirmTitle", { action: actionLabel }),
        t("dialogs.confirmMessage", { action: actionLabel, device: deviceTitle }),
        [
          { text: t("actions.cancel"), style: "cancel" },
          {
            text: actionLabel,
            style: action === "activate" ? "default" : "destructive",
            onPress: () => {
              void (async () => {
	                try {
	                  setBusyDeviceKey(deviceKey);
	                  setPageNumber(1);
	                  await mutation.mutateAsync({
	                    id: item.id,
	                  });
                  setSnackbarMessage(t(`messages.${action}Success`));
                } catch (error) {
                  console.warn(`[device-management] ${action} failed`, error);
                  setSnackbarMessage(resolveLocalizedErrorMessage(error, {
                    t,
                    language,
                    fallbackKey: `messages.${action}Failed`,
                  }));
                } finally {
                  setBusyDeviceKey(null);
                }
              })();
            },
          },
        ]
      );
    },
    [activateMutation, disableMutation, language, lockMutation, t]
  );

  const renderDeviceCard = useCallback(
    ({ item }: { item: DeviceRow }) => {
      const statusKey = getDeviceStatusKey(item.status);
      const deviceKey = getDeviceKey(item);
      const isBusy = busyDeviceKey === deviceKey;
      const updatedAt = formatDateTime(item.updatedAt, language);
      const createdAt = formatDateTime(item.createdAt, language);
      const hardwareTail = getHardwareTail(item.hardwareId);
      const storeName = item.storeName || item.storeCode || t("fields.noStore");

      return (
        <Card style={styles.deviceCard} mode="elevated">
          <Card.Content style={styles.deviceCardContent}>
            <View style={styles.cardHeader}>
              <View style={styles.titleWrap}>
                <Text variant="titleMedium">{getDeviceTitle(item, t("fields.unnamedDevice"))}</Text>
                <Text variant="bodySmall" style={styles.secondaryText}>
                  {t("fields.hardwareValue", { value: hardwareTail ?? t("common:na") })}
                </Text>
              </View>
              <Chip compact style={getStatusStyle(statusKey)}>
                {t(`statuses.${statusKey}`)}
              </Chip>
            </View>

            <View style={styles.metaWrap}>
              <Text variant="bodyMedium">{t("fields.storeValue", { value: storeName })}</Text>
              <Text variant="bodyMedium">{t("fields.deviceTypeValue", { value: item.deviceType || item.platform || t("common:na") })}</Text>
              <Text variant="bodyMedium">{t("fields.deviceSystemValue", { value: item.deviceSystem || item.platform || item.appVersion || t("common:na") })}</Text>
              {updatedAt ? (
                <Text variant="bodySmall" style={styles.secondaryText}>
                  {t("fields.updatedAtValue", { value: updatedAt })}
                </Text>
              ) : null}
              {createdAt ? (
                <Text variant="bodySmall" style={styles.secondaryText}>
                  {t("fields.createdAtValue", { value: createdAt })}
                </Text>
              ) : null}
            </View>

            {canManageDeviceRegistration ? (
              <View style={styles.actionRow}>
                <Button
                  compact
                  mode={item.status === DEVICE_STATUS.ACTIVE ? "outlined" : "contained-tonal"}
                  icon="play-circle-outline"
                  loading={isBusy && activateMutation.isPending}
                  disabled={isBusy}
                  onPress={() => runDeviceAction("activate", item)}
                >
                  {t("actions.activate")}
                </Button>
                <Button
                  compact
                  mode="outlined"
                  icon="pause-circle-outline"
                  loading={isBusy && disableMutation.isPending}
                  disabled={isBusy}
                  onPress={() => runDeviceAction("disable", item)}
                >
                  {t("actions.disable")}
                </Button>
                <Button
                  compact
                  mode="outlined"
                  icon="lock-outline"
                  loading={isBusy && lockMutation.isPending}
                  disabled={isBusy}
                  onPress={() => runDeviceAction("lock", item)}
                >
                  {t("actions.lock")}
                </Button>
              </View>
            ) : null}
          </Card.Content>
        </Card>
      );
    },
    [
      activateMutation.isPending,
      busyDeviceKey,
      disableMutation.isPending,
      language,
      lockMutation.isPending,
      runDeviceAction,
      t,
    ]
  );

  const renderAppDeviceCard = useCallback(
    ({ item }: { item: AppDeviceStatus }) => {
      const hardwareTail = getHardwareTail(item.hardwareId);
      const updateTail = getUpdateTail(item.updateId);
      const lastSeenAt = formatDateTime(item.lastSeenAtUtc, language);
      const storeName = item.storeCode || t("fields.noStore");
      const system = item.deviceSystem || item.platform || t("common:na");
      const packageVersion = getAppVersionText(item, t("common:na"));

      return (
        <Card style={styles.deviceCard} mode="elevated">
          <Card.Content style={styles.deviceCardContent}>
            <View style={styles.cardHeader}>
              <View style={styles.titleWrap}>
                <Text variant="titleMedium">{getAppDeviceTitle(item, t("fields.unnamedDevice"))}</Text>
                <Text variant="bodySmall" style={styles.secondaryText}>
                  {t("fields.hardwareValue", { value: hardwareTail ?? t("common:na") })}
                </Text>
              </View>
              <Chip compact style={item.isOnline ? styles.enabledChip : styles.disabledChip}>
                {item.isOnline ? t("appUsage.online") : t("appUsage.offline")}
              </Chip>
            </View>

            <View style={styles.metaWrap}>
              <Text variant="bodyMedium">{t("fields.storeValue", { value: storeName })}</Text>
              <Text variant="bodyMedium">{t("fields.deviceSystemValue", { value: system })}</Text>
              <Text variant="bodyMedium">{t("appUsage.packageVersion", { value: packageVersion })}</Text>
              <Text variant="bodyMedium">
                {t("appUsage.runtimeValue", { value: item.runtimeVersion || t("common:na") })}
              </Text>
              <Text variant="bodyMedium">
                {t("appUsage.channelValue", { value: item.channel || t("common:na") })}
              </Text>
              <Text variant="bodyMedium">
                {t("appUsage.updateIdValue", { value: updateTail || t("common:na") })}
              </Text>
              <Text variant="bodyMedium">
                {t("appUsage.lastUserValue", {
                  value: getAppDeviceUser(item, t("appUsage.noRecentUser")),
                })}
              </Text>
              {lastSeenAt ? (
                <Text variant="bodySmall" style={styles.secondaryText}>
                  {t("appUsage.lastSeenValue", { value: lastSeenAt })}
                </Text>
              ) : null}
            </View>
          </Card.Content>
        </Card>
      );
    },
    [language, t]
  );

  const isAppUsageView = viewMode === "appUsage";
  const listData: DeviceManagementListRow[] = isAppUsageView ? appDevices : devices;
  const isCurrentLoading = isAppUsageView ? appStatusesQuery.isLoading : devicesQuery.isLoading;
  const isCurrentFetching = isAppUsageView ? appStatusesQuery.isFetching : devicesQuery.isFetching;
  const isCurrentError = isAppUsageView ? appStatusesQuery.isError : devicesQuery.isError;
  const currentError = isAppUsageView ? appStatusesQuery.error : devicesQuery.error;
  const currentPageNumber = isAppUsageView ? appPageNumber : pageNumber;

  return (
    <SafeAreaView style={styles.screen} edges={["top", "left", "right"]}>
      <FlatList<DeviceManagementListRow>
        data={listData}
        keyExtractor={(item) =>
          isAppUsageView
            ? getAppDeviceKey(item as AppDeviceStatus)
            : getDeviceKey(item as unknown as DeviceRow)
        }
        renderItem={({ item }) =>
          isAppUsageView
            ? renderAppDeviceCard({ item: item as AppDeviceStatus })
            : renderDeviceCard({ item: item as unknown as DeviceRow })
        }
        refreshControl={
          <RefreshControl refreshing={isCurrentFetching && !isCurrentLoading} onRefresh={handleRefresh} />
        }
	        contentContainerStyle={styles.listContent}
	        onEndReached={handleLoadMore}
	        onEndReachedThreshold={0.35}
        ListHeaderComponent={
          <View style={styles.headerWrap}>
            <SegmentedButtons
              value={viewMode}
              onValueChange={(value) => setViewMode(value as DeviceManagementViewMode)}
              buttons={[
                { value: "registered", label: t("views.registered") },
                { value: "appUsage", label: t("views.appUsage") },
              ]}
            />

            <View style={styles.titleRow}>
              <View style={styles.titleWrap}>
                <Text variant="headlineSmall">{t("title")}</Text>
                <Text variant="bodyMedium" style={styles.secondaryText}>
                  {t(isAppUsageView ? "appUsage.subtitle" : "subtitle", { count: total })}
                </Text>
              </View>
              <Button mode="outlined" icon="refresh" onPress={handleRefresh} disabled={isCurrentFetching}>
                {t("actions.refresh")}
              </Button>
            </View>

            {isAppUsageView ? (
              <View style={styles.summaryGrid}>
                <Card mode="contained" style={styles.summaryCard}>
                  <Card.Content style={styles.summaryCardContent}>
                    <Text variant="labelMedium" style={styles.secondaryText}>{t("appUsage.summary.total")}</Text>
                    <Text variant="headlineSmall">{appSummaryQuery.data?.total ?? 0}</Text>
                  </Card.Content>
                </Card>
                <Card mode="contained" style={styles.summaryCard}>
                  <Card.Content style={styles.summaryCardContent}>
                    <Text variant="labelMedium" style={styles.secondaryText}>{t("appUsage.summary.online")}</Text>
                    <Text variant="headlineSmall">{appSummaryQuery.data?.online ?? 0}</Text>
                  </Card.Content>
                </Card>
                <Card mode="contained" style={styles.summaryCard}>
                  <Card.Content style={styles.summaryCardContent}>
                    <Text variant="labelMedium" style={styles.secondaryText}>{t("appUsage.summary.android")}</Text>
                    <Text variant="headlineSmall">{appSummaryQuery.data?.android ?? 0}</Text>
                  </Card.Content>
                </Card>
                <Card mode="contained" style={styles.summaryCard}>
                  <Card.Content style={styles.summaryCardContent}>
                    <Text variant="labelMedium" style={styles.secondaryText}>{t("appUsage.summary.ios")}</Text>
                    <Text variant="headlineSmall">{appSummaryQuery.data?.ios ?? 0}</Text>
                  </Card.Content>
                </Card>
              </View>
            ) : null}

            <View style={styles.filterSummaryPanel}>
              <Button
                mode={hasActiveFilters ? "contained-tonal" : "outlined"}
                icon="filter-variant"
                onPress={() => setFiltersVisible(true)}
                contentStyle={styles.filterButtonContent}
              >
                {activeFilterCount > 0
                  ? t("filters.buttonWithCount", { count: activeFilterCount })
                  : t("filters.button")}
              </Button>
              {hasActiveFilters ? (
                <View style={styles.activeFilterChips}>
                  {managedStoreCode ? (
                    <Chip compact icon="store-outline">
                      {managedStore?.storeName || managedStoreCode}
                    </Chip>
                  ) : null}
                  {!isAppUsageView && statusFilter !== "all" ? (
                    <Chip compact icon="list-status">
                      {t(`filters.${statusFilter}`)}
                    </Chip>
                  ) : null}
                  {deviceSystemFilter !== "all" ? (
                    <Chip compact icon="cellphone">
                      {t(`filters.systemOptions.${deviceSystemFilter}`)}
                    </Chip>
                  ) : null}
                  {!isAppUsageView && deviceTypeFilter !== "all" ? (
                    <Chip compact icon="view-grid-outline">
                      {t(`filters.typeOptions.${deviceTypeFilter}`)}
                    </Chip>
                  ) : null}
                  {isAppUsageView && appOnlineFilter !== "all" ? (
                    <Chip compact icon="access-point">
                      {t(`appUsage.onlineFilters.${appOnlineFilter}`)}
                    </Chip>
                  ) : null}
                  {trimmedKeyword ? (
                    <Chip compact icon="magnify">
                      {trimmedKeyword}
                    </Chip>
                  ) : null}
                </View>
              ) : null}
              {isAppUsageView ? (
                <View style={styles.statusChipGrid}>
                  {APP_ONLINE_FILTERS.map((onlineState) => (
                    <Chip
                      key={onlineState}
                      mode={appOnlineFilter === onlineState ? "flat" : "outlined"}
                      selected={appOnlineFilter === onlineState}
                      onPress={() => setAppOnlineFilter(onlineState)}
                    >
                      {t(`appUsage.onlineFilters.${onlineState}`)}
                    </Chip>
                  ))}
                </View>
              ) : null}
            </View>

            {isCurrentError ? (
              <EmptyState
                title={t("messages.loadFailedTitle")}
                description={resolveLocalizedErrorMessage(currentError, {
                  t,
                  language,
                  fallbackKey: "messages.loadFailedDescription",
                })}
                primaryAction={{
                  label: t("common:actions.retry"),
                  icon: "refresh",
                  onPress: () => void handleRefresh(),
                }}
              />
            ) : null}

            {!isCurrentLoading && !isCurrentError && listData.length === 0 ? (
              <EmptyState
                title={hasActiveFilters ? t("messages.emptySearchTitle") : t("messages.emptyTitle")}
                description={
                  hasActiveFilters ? t("messages.emptySearchDescription") : t("messages.emptyDescription")
                }
              />
            ) : null}
          </View>
        }
	        ListFooterComponent={
	          isCurrentLoading || (isCurrentFetching && currentPageNumber > 1) ? (
	            <View style={styles.loadingWrap}>
	              <ActivityIndicator />
	            </View>
          ) : null
        }
      />

      <StorePickerModal
        visible={storePickerVisible}
        stores={stores}
        selectedStoreCode={managedStoreCode}
        title={t("common:labels.selectStore")}
        cancelLabel={t("common:actions.cancel")}
        includeAllOption
        allLabel={t("currentStore.allStores")}
        onDismiss={closeStorePicker}
        onSelectStore={handleSelectStore}
      />

      <Portal>
        <Modal
          visible={filtersVisible}
          onDismiss={() => setFiltersVisible(false)}
          contentContainerStyle={styles.filtersModal}
        >
          <ScrollView contentContainerStyle={styles.filtersModalContent}>
            <View style={styles.filtersModalHeader}>
              <View style={styles.titleWrap}>
                <Text variant="titleMedium">{t("filters.title")}</Text>
                <Text variant="bodySmall" style={styles.secondaryText}>
                  {isAppUsageView
                    ? t("filters.currentApp", {
                        store: managedStore?.storeName || t("currentStore.allStores"),
                        system: t(`filters.systemOptions.${deviceSystemFilter}`),
                        online: t(`appUsage.onlineFilters.${appOnlineFilter}`),
                      })
                    : t("filters.current", {
                        store: managedStore?.storeName || t("currentStore.allStores"),
                        status: t(`filters.${statusFilter}`),
                        system: t(`filters.systemOptions.${deviceSystemFilter}`),
                        type: t(`filters.typeOptions.${deviceTypeFilter}`),
                      })}
                </Text>
              </View>
              <Button compact mode="text" onPress={clearFilters}>
                {t("filters.clear")}
              </Button>
            </View>

            <View style={styles.filtersSection}>
              <Text variant="labelLarge">{t("filters.keyword")}</Text>
              <Searchbar
                placeholder={t("searchPlaceholder")}
                value={keywordInput}
                onChangeText={setKeywordInput}
                onIconPress={submitKeyword}
                onSubmitEditing={applyFilters}
                style={styles.searchbar}
              />
            </View>

            <View style={styles.filtersSection}>
              <Text variant="labelLarge">{t("filters.store")}</Text>
              <Button
                mode="outlined"
                icon="store-outline"
                onPress={openStorePickerFromFilters}
                disabled={storesLoading}
                contentStyle={styles.storePickerButtonContent}
              >
                {managedStore?.storeName || t("currentStore.allStores")}
              </Button>
            </View>

            {!isAppUsageView ? (
              <View style={styles.filtersSection}>
                <Text variant="labelLarge">{t("filters.status")}</Text>
                <View style={styles.statusChipGrid}>
                  {STATUS_FILTERS.map((status) => (
                    <Chip
                      key={status}
                      mode={statusFilter === status ? "flat" : "outlined"}
                      selected={statusFilter === status}
                      onPress={() => setStatusFilter(status)}
                    >
                      {t(`filters.${status}`)}
                    </Chip>
                  ))}
                </View>
              </View>
            ) : null}

            <View style={styles.filtersSection}>
              <Text variant="labelLarge">{t("filters.systemTitle")}</Text>
              <View style={styles.statusChipGrid}>
                {DEVICE_SYSTEM_FILTERS.map((deviceSystem) => (
                  <Chip
                    key={deviceSystem}
                    mode={deviceSystemFilter === deviceSystem ? "flat" : "outlined"}
                    selected={deviceSystemFilter === deviceSystem}
                    onPress={() => setDeviceSystemFilter(deviceSystem)}
                  >
                    {t(`filters.systemOptions.${deviceSystem}`)}
                  </Chip>
                ))}
              </View>
            </View>

            {!isAppUsageView ? (
              <View style={styles.filtersSection}>
                <Text variant="labelLarge">{t("filters.typeTitle")}</Text>
                <View style={styles.statusChipGrid}>
                  {DEVICE_TYPE_FILTERS.map((deviceType) => (
                    <Chip
                      key={deviceType}
                      mode={deviceTypeFilter === deviceType ? "flat" : "outlined"}
                      selected={deviceTypeFilter === deviceType}
                      onPress={() => setDeviceTypeFilter(deviceType)}
                    >
                      {t(`filters.typeOptions.${deviceType}`)}
                    </Chip>
                  ))}
                </View>
              </View>
            ) : null}

            <Button mode="contained" icon="check" onPress={applyFilters}>
              {t("filters.apply")}
            </Button>
          </ScrollView>
        </Modal>
      </Portal>

      <Snackbar visible={Boolean(snackbarMessage)} onDismiss={() => setSnackbarMessage("")} duration={2500}>
        {snackbarMessage}
      </Snackbar>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  actionRow: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
  },
  cardHeader: {
    alignItems: "flex-start",
    flexDirection: "row",
    gap: 12,
    justifyContent: "space-between",
  },
  deviceCard: {
    backgroundColor: "#FFFFFF",
  },
  deviceCardContent: {
    gap: 12,
  },
  disabledChip: {
    backgroundColor: "#FEE2E2",
  },
  enabledChip: {
    backgroundColor: "#D1FAE5",
  },
  activeFilterChips: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
  },
  filterButtonContent: {
    minHeight: 44,
  },
  filterSummaryPanel: {
    backgroundColor: "#FFFFFF",
    borderColor: "#E5E7EB",
    borderRadius: 12,
    borderWidth: StyleSheet.hairlineWidth,
    gap: 10,
    padding: 12,
  },
  filtersModal: {
    alignSelf: "center",
    backgroundColor: "#FFFFFF",
    borderRadius: 16,
    margin: 16,
    maxHeight: "86%",
    width: "92%",
  },
  filtersModalContent: {
    gap: 18,
    padding: 18,
  },
  filtersModalHeader: {
    alignItems: "flex-start",
    flexDirection: "row",
    gap: 12,
    justifyContent: "space-between",
  },
  filtersSection: {
    gap: 10,
  },
  headerWrap: {
    gap: 12,
    marginBottom: 12,
  },
  listContent: {
    gap: 12,
    padding: 16,
    paddingBottom: 32,
  },
  loadingWrap: {
    alignItems: "center",
    paddingVertical: 24,
  },
  lockedChip: {
    backgroundColor: "#E0E7FF",
  },
  metaWrap: {
    gap: 4,
  },
  pendingChip: {
    backgroundColor: "#FEF3C7",
  },
  screen: {
    backgroundColor: "#F3F4F6",
    flex: 1,
  },
  searchbar: {
    backgroundColor: "#F9FAFB",
  },
  secondaryText: {
    color: "#6B7280",
  },
  storePickerButtonContent: {
    justifyContent: "flex-start",
  },
  statusChipGrid: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
  },
  summaryCard: {
    backgroundColor: "#FFFFFF",
    flexBasis: "48%",
    flexGrow: 1,
  },
  summaryCardContent: {
    gap: 2,
    paddingVertical: 12,
  },
  summaryGrid: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 10,
  },
  titleRow: {
    alignItems: "center",
    flexDirection: "row",
    gap: 12,
    justifyContent: "space-between",
  },
  titleWrap: {
    flex: 1,
    gap: 2,
  },
  unknownChip: {
    backgroundColor: "#E5E7EB",
  },
});
