import { useCallback, useEffect, useMemo, useState } from "react";
import { Pressable, RefreshControl, ScrollView, StyleSheet, View } from "react-native";
import {
  ActivityIndicator,
  Button,
  Card,
  IconButton,
  Modal,
  Portal,
  Snackbar,
  Surface,
  Text,
  TextInput,
} from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import { MonthDatePicker } from "@/components/attendance/MonthDatePicker";
import { EmptyState } from "@/components/ui/EmptyState";
import { EntityTag } from "@/components/ui/EntityTag";
import { QrCodePanel } from "@/components/ui/QrCodePanel";
import {
  SelectionListModal,
  type SelectionListItem,
} from "@/components/ui/SelectionListModal";
import { StorePickerModal } from "@/components/ui/StorePickerModal";
import {
  fetchInstallmentOrderDetail,
  fetchInstallmentOrders,
} from "@/modules/installment-orders/api";
import type {
  InstallmentOrderDetail,
  InstallmentOrderDetailLine,
  InstallmentOrderFilters,
  InstallmentOrderListItem,
  InstallmentPaymentRecord,
  InstallmentOrderStatus,
} from "@/modules/installment-orders/types";
import type { Store } from "@/modules/shop/types";
import { bindDeviceStoreFilter, getDeviceBoundStoreCode } from "@/modules/shop/device-bound-store-filter";
import { useStores } from "@/modules/shop/use-stores";
import { resolveLocalizedErrorMessage } from "@/shared/i18n/error-message";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { resolveLocaleTag } from "@/shared/i18n/types";
import { resolveQrDisplayValue } from "@/shared/utils/qr-display";

type DateFilterKey = "startDate" | "endDate";
type StatusOption = {
  key: string;
  value: number;
  labelKey: string;
};

const PAGE_SIZE = 20;
const INSTALLMENT_STATUS_OPTIONS: StatusOption[] = [
  { key: "0", value: 0, labelKey: "statuses.pending" },
  { key: "4", value: 4, labelKey: "statuses.installment" },
  { key: "1", value: 1, labelKey: "statuses.paid" },
  { key: "2", value: 2, labelKey: "statuses.cancelled" },
  { key: "3", value: 3, labelKey: "statuses.refunded" },
];
const INSTALLMENT_STATUS_LABEL_KEYS: Record<number, string> = {
  0: "statuses.pending",
  1: "statuses.paid",
  2: "statuses.cancelled",
  3: "statuses.refunded",
  4: "statuses.installment",
};

function formatDateTime(value?: string | null, localeTag = "en-AU") {
  if (!value) {
    return "--";
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }

  return date.toLocaleString(localeTag, { hour12: false });
}

function formatMoney(value?: number | null) {
  return value == null || Number.isNaN(value) ? "--" : `$${value.toFixed(2)}`;
}

function formatNumber(value?: number | null) {
  return value == null || Number.isNaN(value) ? "--" : String(value);
}

function getPageCount(total: number, pageSize: number) {
  return Math.max(1, Math.ceil(total / pageSize));
}

function getFilterLabel(value?: string | null, placeholder?: string) {
  return value?.trim() || placeholder || "--";
}

function getStatusLabelKey(status?: InstallmentOrderStatus) {
  if (status == null) {
    return "statuses.unknown";
  }
  return INSTALLMENT_STATUS_LABEL_KEYS[status] ?? "statuses.unknown";
}

function StatusBadge({
  status,
  label,
}: {
  status?: InstallmentOrderStatus;
  label: string;
}) {
  const toneMap: Record<string, { backgroundColor: string; borderColor: string; textColor: string }> = {
    "0": { backgroundColor: "#FFF7E6", borderColor: "#F7C58B", textColor: "#AD6800" },
    "1": { backgroundColor: "#F6FFED", borderColor: "#B7EB8F", textColor: "#237804" },
    "2": { backgroundColor: "#FFF1F0", borderColor: "#FFA39E", textColor: "#A8071A" },
    "3": { backgroundColor: "#E6F4FF", borderColor: "#91CAFF", textColor: "#0958D9" },
    "4": { backgroundColor: "#F4EEFF", borderColor: "#C7AEFF", textColor: "#6B3FD6" },
    unknown: { backgroundColor: "#F5F5F5", borderColor: "#D9D9D9", textColor: "#595959" },
  };
  const tone = toneMap[String(status ?? "unknown")] ?? toneMap.unknown;

  return (
    <View
      style={[
        styles.statusBadge,
        { backgroundColor: tone.backgroundColor, borderColor: tone.borderColor },
      ]}
    >
      <Text variant="labelMedium" style={[styles.statusBadgeText, { color: tone.textColor }]}>
        {label}
      </Text>
    </View>
  );
}

function SummaryMetric({ label, value }: { label: string; value: string }) {
  return (
    <View style={styles.summaryMetric}>
      <Text variant="labelMedium" style={styles.summaryLabel}>
        {label}
      </Text>
      <Text variant="titleMedium" style={styles.summaryValue}>
        {value}
      </Text>
    </View>
  );
}

function DetailLine({ label, value }: { label: string; value: string }) {
  return (
    <View style={styles.detailLine}>
      <Text variant="labelMedium" style={styles.detailLineLabel}>
        {label}
      </Text>
      <Text variant="bodyMedium" style={styles.detailLineValue}>
        {value}
      </Text>
    </View>
  );
}

function SectionTitle({ children }: { children: string }) {
  return (
    <Text variant="titleMedium" style={styles.sectionTitle}>
      {children}
    </Text>
  );
}

export default function InstallmentOrdersScreen() {
  const { t, language } = useAppTranslation(["installmentOrders", "common"]);
  const getErrorMessage = useCallback((error: unknown, fallbackKey: string) => (
    resolveLocalizedErrorMessage(error, {
      language,
      t,
      fallbackKey,
    })
  ), [language, t]);
  const localeTag = useMemo(() => resolveLocaleTag(language), [language]);
  const {
    stores,
    selectedStoreCode,
    isDeviceMode,
    isLoading: storesLoading,
  } = useStores();
  const [draftFilters, setDraftFilters] = useState<InstallmentOrderFilters>({});
  const [filters, setFilters] = useState<InstallmentOrderFilters>({});
  const [storePickerVisible, setStorePickerVisible] = useState(false);
  const [statusPickerVisible, setStatusPickerVisible] = useState(false);
  const [datePickerTarget, setDatePickerTarget] = useState<DateFilterKey | null>(null);
  const [page, setPage] = useState(1);
  const [items, setItems] = useState<InstallmentOrderListItem[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [selectedOrder, setSelectedOrder] = useState<InstallmentOrderListItem | null>(null);
  const [detail, setDetail] = useState<InstallmentOrderDetail | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [snackbar, setSnackbar] = useState("");
  const deviceBoundStoreCode = getDeviceBoundStoreCode({ isDeviceMode, selectedStoreCode });

  const selectedStore = useMemo(
    () => stores.find((store) => store.storeCode === (draftFilters.branchCode ?? "")) ?? null,
    [draftFilters.branchCode, stores]
  );
  const selectedStatus = useMemo(
    () => INSTALLMENT_STATUS_OPTIONS.find((option) => option.value === draftFilters.status) ?? null,
    [draftFilters.status]
  );
  const statusItems = useMemo<SelectionListItem[]>(
    () =>
      INSTALLMENT_STATUS_OPTIONS.map((option) => ({
        key: option.key,
        label: t(option.labelKey),
      })),
    [t]
  );
  const pageCount = getPageCount(total, PAGE_SIZE);
  const currentDateFilterValue = datePickerTarget ? draftFilters[datePickerTarget] : undefined;
  const detailOrder = detail?.order ?? selectedOrder;
  const detailOrderQrValue = resolveQrDisplayValue(detailOrder?.orderNo, detailOrder?.orderGuid);
  const paymentRecords = detail?.paymentDetails ?? [];
  const orderLines = detail?.orderDetails ?? [];

  const loadOrders = useCallback(
    async (refresh = false) => {
      if (isDeviceMode && !selectedStoreCode) {
        return;
      }

      if (refresh) {
        setRefreshing(true);
      } else {
        setLoading(true);
      }

      try {
        const result = await fetchInstallmentOrders({
          page,
          pageSize: PAGE_SIZE,
          filters: bindDeviceStoreFilter(filters, {
            isDeviceMode,
            selectedStoreCode,
            storeField: "branchCode",
          }),
        });
        setItems(result.items);
        setTotal(result.total);
      } catch (error) {
        setSnackbar(getErrorMessage(error, "messages.loadFailed"));
      } finally {
        setLoading(false);
        setRefreshing(false);
      }
    },
    [filters, getErrorMessage, isDeviceMode, page, selectedStoreCode, t]
  );

  useEffect(() => {
    void loadOrders();
  }, [loadOrders]);

  useEffect(() => {
    if (!selectedOrder?.orderGuid) {
      return;
    }

    let active = true;
    setDetail(null);
    setDetailLoading(true);

    fetchInstallmentOrderDetail(selectedOrder.orderGuid)
      .then((result) => {
        if (active) {
          setDetail(result);
        }
      })
      .catch((error) => {
        if (active) {
          setSnackbar(getErrorMessage(error, "messages.detailsLoadFailed"));
        }
      })
      .finally(() => {
        if (active) {
          setDetailLoading(false);
        }
      });

    return () => {
      active = false;
    };
  }, [getErrorMessage, selectedOrder?.orderGuid, t]);

  useEffect(() => {
    if (!deviceBoundStoreCode) {
      return;
    }

    setDraftFilters((current) =>
      current.branchCode === deviceBoundStoreCode
        ? current
        : { ...current, branchCode: deviceBoundStoreCode }
    );
    setFilters((current) =>
      current.branchCode === deviceBoundStoreCode
        ? current
        : { ...current, branchCode: deviceBoundStoreCode }
    );
  }, [deviceBoundStoreCode]);

  const bindDeviceStore = useCallback(
    (nextFilters: InstallmentOrderFilters) =>
      bindDeviceStoreFilter(nextFilters, {
        isDeviceMode,
        selectedStoreCode,
        storeField: "branchCode",
      }),
    [isDeviceMode, selectedStoreCode]
  );

  const applyFilters = useCallback(() => {
    setPage(1);
    setFilters(bindDeviceStore(draftFilters));
  }, [bindDeviceStore, draftFilters]);

  const clearFilters = useCallback(() => {
    const emptyFilters = bindDeviceStore({});
    setDraftFilters(emptyFilters);
    setFilters(emptyFilters);
    setPage(1);
  }, [bindDeviceStore]);

  const handleSelectStore = useCallback((store: Store | null) => {
    setDraftFilters((current) => ({
      ...current,
      branchCode: deviceBoundStoreCode ?? store?.storeCode ?? undefined,
    }));
    setStorePickerVisible(false);
  }, [deviceBoundStoreCode]);

  const handleSelectStatus = useCallback((item: SelectionListItem | null) => {
    const option = item
      ? INSTALLMENT_STATUS_OPTIONS.find((statusOption) => statusOption.key === item.key)
      : null;
    setDraftFilters((current) => ({
      ...current,
      status: option?.value,
    }));
    setStatusPickerVisible(false);
  }, []);

  const updateDateFilter = useCallback((key: DateFilterKey, value?: string) => {
    setDraftFilters((current) => ({
      ...current,
      [key]: value || undefined,
    }));
  }, []);

  const closeDetail = useCallback(() => {
    setSelectedOrder(null);
    setDetail(null);
  }, []);

  const renderPagination = () => (
    <View style={styles.pagination}>
      <Button
        compact
        disabled={page <= 1}
        icon="chevron-left"
        mode="outlined"
        onPress={() => setPage((current) => Math.max(1, current - 1))}
      >
        {t("common:actions.back")}
      </Button>
      <Text variant="bodyMedium">
        {page} / {pageCount}
      </Text>
      <Button
        compact
        contentStyle={styles.nextButtonContent}
        disabled={page >= pageCount}
        icon="chevron-right"
        mode="outlined"
        onPress={() => setPage((current) => Math.min(pageCount, current + 1))}
      >
        {t("actions.loadMore")}
      </Button>
    </View>
  );

  const renderPaymentRecord = (record: InstallmentPaymentRecord) => (
    <View key={record.paymentGuid || `${record.paymentTime}-${record.amount}`} style={styles.recordRow}>
      <View style={styles.recordHeader}>
        <Text variant="titleSmall" style={styles.recordTitle}>
          {record.paymentMethodName || t("labels.paymentMethod")}
        </Text>
        <Text variant="titleSmall" style={styles.moneyText}>
          {formatMoney(record.amount)}
        </Text>
      </View>
      <DetailLine label={t("labels.paymentTime")} value={formatDateTime(record.paymentTime, localeTag)} />
      <DetailLine label={t("labels.cashier")} value={record.cashierName || record.cashierId || "--"} />
      <DetailLine label={t("labels.reference")} value={record.reference || "--"} />
    </View>
  );

  const renderOrderLine = (line: InstallmentOrderDetailLine, index: number) => (
    <View key={`${line.productCode}-${index}`} style={styles.recordRow}>
      <View style={styles.recordHeader}>
        <View style={styles.recordTitleGroup}>
          <Text variant="titleSmall" style={styles.recordTitle} numberOfLines={2}>
            {line.productName || line.productCode || `#${index + 1}`}
          </Text>
          <Text variant="bodySmall" style={styles.secondaryText}>
            {line.productCode || "--"}
          </Text>
        </View>
        <Text variant="titleSmall" style={styles.moneyText}>
          {formatMoney(line.actualAmount)}
        </Text>
      </View>
      <View style={styles.compactMetaRow}>
        <Text variant="bodySmall">{t("labels.quantity")}: {formatNumber(line.quantity)}</Text>
        <Text variant="bodySmall">{t("labels.unitPrice")}: {formatMoney(line.unitPrice)}</Text>
        <Text variant="bodySmall">{t("labels.discountAmount")}: {formatMoney(line.discountAmount)}</Text>
      </View>
    </View>
  );

  return (
    <SafeAreaView style={styles.safeArea}>
      <ScrollView
        contentContainerStyle={styles.container}
        refreshControl={<RefreshControl refreshing={refreshing} onRefresh={() => void loadOrders(true)} />}
      >
        <View style={styles.header}>
          <Text variant="headlineSmall" style={styles.title}>
            {t("title")}
          </Text>
          <Text variant="bodyMedium" style={styles.subtitle}>
            {t("subtitle")}
          </Text>
        </View>

        <View style={styles.filterPanel}>
          <View style={styles.filterGrid}>
            <Pressable onPress={() => setStorePickerVisible(true)} style={styles.filterInput}>
              <Surface style={styles.pickerField} elevation={0}>
                <View style={styles.pickerFieldText}>
                  <Text variant="labelMedium" style={styles.pickerFieldLabel}>
                    {t("filters.storeCode")}
                  </Text>
                  <Text variant="bodyLarge" numberOfLines={1}>
                    {selectedStore?.storeName || draftFilters.branchCode || t("filters.allStores")}
                  </Text>
                </View>
                {storesLoading ? <ActivityIndicator size="small" /> : <IconButton icon="store-outline" size={20} />}
              </Surface>
            </Pressable>

            <Pressable onPress={() => setStatusPickerVisible(true)} style={styles.filterInput}>
              <Surface style={styles.pickerField} elevation={0}>
                <View style={styles.pickerFieldText}>
                  <Text variant="labelMedium" style={styles.pickerFieldLabel}>
                    {t("filters.status")}
                  </Text>
                  <Text variant="bodyLarge" numberOfLines={1}>
                    {selectedStatus ? t(selectedStatus.labelKey) : t("filters.allStatuses")}
                  </Text>
                </View>
                <IconButton icon="list-status" size={20} />
              </Surface>
            </Pressable>

            <TextInput
              dense
              keyboardType="phone-pad"
              label={t("filters.userPhone")}
              mode="outlined"
              style={styles.filterInput}
              value={draftFilters.userPhone ?? ""}
              onChangeText={(value) =>
                setDraftFilters((current) => ({ ...current, userPhone: value || undefined }))
              }
            />
            <TextInput
              dense
              label={t("filters.userName")}
              mode="outlined"
              style={styles.filterInput}
              value={draftFilters.userName ?? ""}
              onChangeText={(value) =>
                setDraftFilters((current) => ({ ...current, userName: value || undefined }))
              }
            />

            <Pressable onPress={() => setDatePickerTarget("startDate")} style={styles.filterInput}>
              <Surface style={styles.pickerField} elevation={0}>
                <View style={styles.pickerFieldText}>
                  <Text variant="labelMedium" style={styles.pickerFieldLabel}>
                    {t("filters.dateFrom")}
                  </Text>
                  <Text
                    variant="bodyLarge"
                    numberOfLines={1}
                    style={draftFilters.startDate ? undefined : styles.pickerPlaceholder}
                  >
                    {getFilterLabel(draftFilters.startDate, t("filters.emptyDate"))}
                  </Text>
                </View>
                <IconButton icon="calendar-month-outline" size={20} />
              </Surface>
            </Pressable>

            <Pressable onPress={() => setDatePickerTarget("endDate")} style={styles.filterInput}>
              <Surface style={styles.pickerField} elevation={0}>
                <View style={styles.pickerFieldText}>
                  <Text variant="labelMedium" style={styles.pickerFieldLabel}>
                    {t("filters.dateTo")}
                  </Text>
                  <Text
                    variant="bodyLarge"
                    numberOfLines={1}
                    style={draftFilters.endDate ? undefined : styles.pickerPlaceholder}
                  >
                    {getFilterLabel(draftFilters.endDate, t("filters.emptyDate"))}
                  </Text>
                </View>
                <IconButton icon="calendar-month-outline" size={20} />
              </Surface>
            </Pressable>
          </View>

          <View style={styles.filterActions}>
            <Button compact icon="filter-check" mode="contained" onPress={applyFilters}>
              {t("common:actions.search")}
            </Button>
            <Button compact icon="filter-remove" mode="outlined" onPress={clearFilters}>
              {t("common:actions.clear")}
            </Button>
          </View>
        </View>

        {loading ? (
          <View style={styles.loadingBox}>
            <ActivityIndicator />
            <Text variant="bodyMedium">{t("common:loading")}</Text>
          </View>
        ) : items.length ? (
          <View style={styles.list}>
            {items.map((order) => (
              <Card key={order.orderGuid || order.orderNo} mode="outlined" style={styles.card}>
                <Card.Title
                  title={order.orderNo || order.orderGuid}
                  subtitle={formatDateTime(order.orderTime, localeTag)}
                  right={(props) => (
                    <IconButton
                      {...props}
                      accessibilityLabel={t("actions.viewDetail")}
                      icon="chevron-right"
                      onPress={() => setSelectedOrder(order)}
                    />
                  )}
                />
                <Card.Content style={styles.cardContent}>
                  <View style={styles.tagRow}>
                    <EntityTag kind="store" code={order.branchCode} label={order.branchName || order.branchCode} />
                    <StatusBadge status={order.status} label={t(getStatusLabelKey(order.status))} />
                  </View>
                  <View style={styles.summaryGrid}>
                    <SummaryMetric label={t("labels.actualAmount")} value={formatMoney(order.actualAmount)} />
                    <SummaryMetric label={t("labels.totalAmount")} value={formatMoney(order.totalAmount)} />
                    <SummaryMetric label={t("labels.itemCount")} value={formatNumber(order.itemCount)} />
                    <SummaryMetric label={t("labels.skuCount")} value={formatNumber(order.skuCount)} />
                  </View>
                  <Text variant="bodyMedium">
                    {t("labels.customer")}: {order.customerName || "--"} / {order.customerPhone || "--"}
                  </Text>
                </Card.Content>
                <Card.Actions>
                  <Button compact mode="contained-tonal" onPress={() => setSelectedOrder(order)}>
                    {t("actions.viewDetail")}
                  </Button>
                </Card.Actions>
              </Card>
            ))}
          </View>
        ) : (
          <EmptyState
            title={t("messages.empty")}
            primaryAction={{ label: t("common:actions.refresh"), icon: "refresh", onPress: () => void loadOrders() }}
          />
        )}

        {items.length ? renderPagination() : null}
      </ScrollView>

      <Portal>
        <Modal visible={Boolean(selectedOrder)} onDismiss={closeDetail} contentContainerStyle={styles.modal}>
          <View style={styles.modalHeader}>
            <View style={styles.modalTitleGroup}>
              <Text variant="titleMedium">{detailOrder?.orderNo || detailOrder?.orderGuid || "--"}</Text>
              <Text variant="bodySmall" style={styles.subtitle}>
                {formatDateTime(detailOrder?.orderTime, localeTag)}
              </Text>
            </View>
            <IconButton accessibilityLabel={t("common:actions.close")} icon="close" onPress={closeDetail} />
          </View>

          {detailLoading ? (
            <View style={styles.loadingBox}>
              <ActivityIndicator />
              <Text variant="bodyMedium">{t("common:loading")}</Text>
            </View>
          ) : (
            <ScrollView contentContainerStyle={styles.detailContent}>
              <QrCodePanel label={t("labels.orderQrCode")} value={detailOrderQrValue} />
              <View style={styles.detailSummary}>
                <View style={styles.tagRow}>
                  <EntityTag
                    kind="store"
                    code={detailOrder?.branchCode}
                    label={detailOrder?.branchName || detailOrder?.branchCode}
                  />
                  <StatusBadge
                    status={detailOrder?.status}
                    label={t(getStatusLabelKey(detailOrder?.status))}
                  />
                </View>
                <View style={styles.summaryGrid}>
                  <SummaryMetric label={t("labels.actualAmount")} value={formatMoney(detailOrder?.actualAmount)} />
                  <SummaryMetric label={t("labels.totalAmount")} value={formatMoney(detailOrder?.totalAmount)} />
                  <SummaryMetric label={t("labels.discountAmount")} value={formatMoney(detailOrder?.discountAmount)} />
                </View>
                <DetailLine label={t("labels.customerName")} value={detailOrder?.customerName || "--"} />
                <DetailLine label={t("labels.customerPhone")} value={detailOrder?.customerPhone || "--"} />
              </View>

              <SectionTitle>{t("labels.paymentRecords")}</SectionTitle>
              {paymentRecords.length ? (
                paymentRecords.map(renderPaymentRecord)
              ) : (
                <EmptyState title={t("messages.paymentRecordsEmpty")} />
              )}

              <SectionTitle>{t("labels.orderLines")}</SectionTitle>
              {orderLines.length ? (
                orderLines.map(renderOrderLine)
              ) : (
                <EmptyState title={t("messages.orderLinesEmpty")} />
              )}
            </ScrollView>
          )}
        </Modal>
      </Portal>

      <StorePickerModal
        visible={storePickerVisible}
        stores={stores}
        selectedStoreCode={draftFilters.branchCode}
        title={t("filters.storePickerTitle")}
        cancelLabel={t("common:actions.cancel")}
        includeAllOption={!deviceBoundStoreCode}
        allLabel={t("filters.allStores")}
        onDismiss={() => setStorePickerVisible(false)}
        onSelectStore={handleSelectStore}
      />

      <SelectionListModal
        visible={statusPickerVisible}
        title={t("filters.statusPickerTitle")}
        cancelLabel={t("common:actions.cancel")}
        items={statusItems}
        selectedKey={selectedStatus?.key}
        includeAllOption
        allLabel={t("filters.allStatuses")}
        emptyLabel={t("filters.allStatuses")}
        onDismiss={() => setStatusPickerVisible(false)}
        onSelect={handleSelectStatus}
      />

      <Portal>
        <Modal
          visible={Boolean(datePickerTarget)}
          onDismiss={() => setDatePickerTarget(null)}
          contentContainerStyle={styles.dateModal}
        >
          <View style={styles.pickerModalHeader}>
            <Text variant="titleMedium">
              {datePickerTarget === "startDate" ? t("filters.dateFrom") : t("filters.dateTo")}
            </Text>
            <Button onPress={() => setDatePickerTarget(null)}>{t("common:actions.cancel")}</Button>
          </View>
          <MonthDatePicker
            key={`${datePickerTarget ?? "none"}-${currentDateFilterValue ?? "empty"}`}
            allowEmpty
            value={currentDateFilterValue || ""}
            onChange={(value) => {
              if (!datePickerTarget) {
                return;
              }
              updateDateFilter(datePickerTarget, value);
              setDatePickerTarget(null);
            }}
          />
          <View style={styles.dateModalActions}>
            <Button
              icon="close-circle-outline"
              mode="text"
              onPress={() => {
                if (datePickerTarget) {
                  updateDateFilter(datePickerTarget, undefined);
                }
                setDatePickerTarget(null);
              }}
            >
              {t("common:actions.clear")}
            </Button>
          </View>
        </Modal>
      </Portal>

      <Snackbar visible={Boolean(snackbar)} onDismiss={() => setSnackbar("")} duration={3500}>
        {snackbar}
      </Snackbar>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safeArea: {
    flex: 1,
    backgroundColor: "#F6F7F9",
  },
  container: {
    gap: 12,
    padding: 16,
    paddingBottom: 32,
  },
  header: {
    gap: 4,
  },
  title: {
    fontWeight: "700",
  },
  subtitle: {
    color: "#667085",
  },
  filterPanel: {
    backgroundColor: "#FFFFFF",
    borderColor: "#E4E7EC",
    borderRadius: 8,
    borderWidth: 1,
    gap: 12,
    padding: 12,
  },
  filterGrid: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
  },
  filterInput: {
    flexGrow: 1,
    minWidth: 150,
  },
  filterActions: {
    alignItems: "center",
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
  },
  pickerField: {
    alignItems: "center",
    backgroundColor: "#FFFFFF",
    borderColor: "#79747E",
    borderRadius: 4,
    borderWidth: 1,
    flexDirection: "row",
    minHeight: 56,
    paddingLeft: 16,
  },
  pickerFieldLabel: {
    color: "#49454F",
  },
  pickerFieldText: {
    flex: 1,
    gap: 2,
    minWidth: 0,
    paddingVertical: 8,
  },
  pickerPlaceholder: {
    color: "#9CA3AF",
  },
  loadingBox: {
    alignItems: "center",
    gap: 8,
    justifyContent: "center",
    padding: 28,
  },
  list: {
    gap: 10,
  },
  card: {
    backgroundColor: "#FFFFFF",
    borderRadius: 8,
  },
  cardContent: {
    gap: 10,
  },
  tagRow: {
    alignItems: "center",
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
  },
  statusBadge: {
    alignItems: "center",
    alignSelf: "flex-start",
    borderRadius: 999,
    borderWidth: 1,
    justifyContent: "center",
    minHeight: 28,
    paddingHorizontal: 10,
    paddingVertical: 4,
  },
  statusBadgeText: {
    fontWeight: "600",
  },
  summaryGrid: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
  },
  summaryMetric: {
    backgroundColor: "#F9FAFB",
    borderColor: "#EAECF0",
    borderRadius: 8,
    borderWidth: 1,
    flexGrow: 1,
    minWidth: 118,
    padding: 10,
  },
  summaryLabel: {
    color: "#667085",
  },
  summaryValue: {
    fontWeight: "700",
  },
  pagination: {
    alignItems: "center",
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 12,
    justifyContent: "center",
    paddingVertical: 8,
  },
  nextButtonContent: {
    flexDirection: "row-reverse",
  },
  modal: {
    alignSelf: "center",
    backgroundColor: "#FFFFFF",
    borderRadius: 8,
    maxHeight: "92%",
    padding: 12,
    width: "94%",
  },
  modalHeader: {
    alignItems: "center",
    flexDirection: "row",
    justifyContent: "space-between",
  },
  modalTitleGroup: {
    flex: 1,
    minWidth: 0,
    paddingRight: 8,
  },
  detailContent: {
    gap: 12,
    paddingBottom: 8,
  },
  detailSummary: {
    backgroundColor: "#F9FAFB",
    borderColor: "#EAECF0",
    borderRadius: 8,
    borderWidth: 1,
    gap: 10,
    padding: 10,
  },
  detailLine: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
  },
  detailLineLabel: {
    color: "#667085",
    minWidth: 96,
  },
  detailLineValue: {
    flex: 1,
    minWidth: 120,
  },
  sectionTitle: {
    fontWeight: "700",
    marginTop: 4,
  },
  recordRow: {
    backgroundColor: "#FFFFFF",
    borderColor: "#EAECF0",
    borderRadius: 8,
    borderWidth: 1,
    gap: 6,
    padding: 10,
  },
  recordHeader: {
    alignItems: "flex-start",
    flexDirection: "row",
    gap: 8,
    justifyContent: "space-between",
  },
  recordTitleGroup: {
    flex: 1,
    minWidth: 0,
  },
  recordTitle: {
    flex: 1,
    fontWeight: "700",
  },
  moneyText: {
    color: "#047857",
    fontWeight: "700",
  },
  secondaryText: {
    color: "#667085",
  },
  compactMetaRow: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 12,
  },
  pickerModalHeader: {
    alignItems: "center",
    flexDirection: "row",
    justifyContent: "space-between",
    marginBottom: 8,
  },
  dateModal: {
    alignSelf: "center",
    backgroundColor: "#FFFFFF",
    borderRadius: 12,
    padding: 16,
    width: "92%",
  },
  dateModalActions: {
    alignItems: "flex-end",
    marginTop: 8,
  },
});
