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
  value: NonNullable<InstallmentOrderStatus>;
  labelKey: string;
};

const PAGE_SIZE = 20;
const INSTALLMENT_STATUS_OPTIONS: StatusOption[] = [
  { key: "1", value: 1, labelKey: "statuses.active" },
  { key: "2", value: 2, labelKey: "statuses.paidOff" },
  { key: "3", value: 3, labelKey: "statuses.pickedUp" },
  { key: "4", value: 4, labelKey: "statuses.cancelled" },
];
const INSTALLMENT_STATUS_LABEL_KEYS: Record<number, string> = {
  1: "statuses.active",
  2: "statuses.paidOff",
  3: "statuses.pickedUp",
  4: "statuses.cancelled",
};
const PAYMENT_METHOD_LABEL_KEYS: Record<number, string> = {
  1: "paymentMethods.cash",
  2: "paymentMethods.card",
  3: "paymentMethods.voucher",
};
const PAYMENT_STATUS_LABEL_KEYS: Record<number, string> = {
  1: "paymentStatuses.recorded",
  2: "paymentStatuses.voided",
};
const CANCELLATION_KIND_LABEL_KEYS: Record<number, string> = {
  1: "cancellationKinds.refundCancel",
  2: "cancellationKinds.voidCancel",
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

function getPaymentMethodLabelKey(method?: number | null) {
  return method == null ? "paymentMethods.unknown" : PAYMENT_METHOD_LABEL_KEYS[method] ?? "paymentMethods.unknown";
}

function getPaymentStatusLabelKey(status?: number | null) {
  return status == null ? "paymentStatuses.unknown" : PAYMENT_STATUS_LABEL_KEYS[status] ?? "paymentStatuses.unknown";
}

function getCancellationKindLabelKey(kind?: number | null) {
  return kind == null
    ? "cancellationKinds.unknown"
    : CANCELLATION_KIND_LABEL_KEYS[kind] ?? "cancellationKinds.unknown";
}

function StatusBadge({
  status,
  label,
}: {
  status?: InstallmentOrderStatus;
  label: string;
}) {
  const toneMap: Record<string, { backgroundColor: string; borderColor: string; textColor: string }> = {
    "1": { backgroundColor: "#FFF7E6", borderColor: "#F7C58B", textColor: "#AD6800" },
    "2": { backgroundColor: "#F6FFED", borderColor: "#B7EB8F", textColor: "#237804" },
    "3": { backgroundColor: "#E6F4FF", borderColor: "#91CAFF", textColor: "#0958D9" },
    "4": { backgroundColor: "#FFF1F0", borderColor: "#FFA39E", textColor: "#A8071A" },
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
      <Text selectable variant="titleMedium" style={styles.summaryValue}>
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
      <Text selectable variant="bodyMedium" style={styles.detailLineValue}>
        {value}
      </Text>
    </View>
  );
}


function PaymentStatusBadge({ status, label }: { status?: number | null; label: string }) {
  const isVoided = status === 2;
  return (
    <View style={[styles.paymentStatusBadge, isVoided && styles.paymentStatusBadgeVoided]}>
      <Text
        variant="labelSmall"
        style={[styles.paymentStatusBadgeText, isVoided && styles.paymentStatusBadgeTextVoided]}
      >
        {label}
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
  const detailOrderQrValue = resolveQrDisplayValue(
    detailOrder?.installmentNumber,
    detailOrder?.installmentGuid
  );
  const paymentRecords = detail?.payments ?? [];
  const orderLines = detail?.lines ?? [];

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
    if (!selectedOrder?.installmentGuid) {
      return;
    }

    let active = true;
    setDetail(null);
    setDetailLoading(true);

    fetchInstallmentOrderDetail(selectedOrder.installmentGuid)
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
  }, [getErrorMessage, selectedOrder?.installmentGuid, t]);

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

  const renderPaymentRecord = (record: InstallmentPaymentRecord) => {
    const isVoided = record.status === 2;
    return (
      <View
        key={record.paymentGuid || `${record.recordedAt}-${record.amount}`}
        style={[styles.recordRow, isVoided && styles.voidedRecordRow]}
      >
        <View style={styles.recordHeader}>
          <View style={styles.recordTitleGroup}>
            <Text variant="titleSmall" style={[styles.recordTitle, isVoided && styles.voidedText]}>
              {t(getPaymentMethodLabelKey(record.method))}
            </Text>
            <PaymentStatusBadge
              status={record.status}
              label={t(getPaymentStatusLabelKey(record.status))}
            />
          </View>
          <Text
            selectable
            variant="titleSmall"
            style={[styles.moneyText, isVoided && styles.voidedMoneyText]}
          >
            {formatMoney(record.amount)}
          </Text>
        </View>
        <DetailLine label={t("labels.paymentTime")} value={formatDateTime(record.recordedAt, localeTag)} />
        <DetailLine label={t("labels.cashier")} value={record.cashierId || "--"} />
        <DetailLine label={t("labels.deviceCode")} value={record.deviceCode || "--"} />
        <DetailLine label={t("labels.reference")} value={record.reference || "--"} />
      </View>
    );
  };

  const renderOrderLine = (line: InstallmentOrderDetailLine, index: number) => (
    <View key={line.installmentLineGuid || `${line.productCode}-${index}`} style={styles.recordRow}>
      <View style={styles.recordHeader}>
        <View style={styles.recordTitleGroup}>
          <Text variant="titleSmall" style={styles.recordTitle} numberOfLines={2}>
            {line.displayName || line.productCode || `#${index + 1}`}
          </Text>
          <Text variant="bodySmall" style={styles.secondaryText}>
            {line.referenceCode || line.productCode || "--"}
          </Text>
        </View>
        <Text selectable variant="titleSmall" style={styles.moneyText}>
          {formatMoney(line.actualAmount)}
        </Text>
      </View>
      <View style={styles.compactMetaRow}>
        <Text variant="bodySmall">{t("labels.quantity")}: {formatNumber(line.quantity)}</Text>
        <Text variant="bodySmall">{t("labels.unitPrice")}: {formatMoney(line.unitPrice)}</Text>
        <Text variant="bodySmall">{t("labels.discountAmount")}: {formatMoney(line.discountAmount)}</Text>
        <Text variant="bodySmall">{t("labels.lookupCode")}: {line.lookupCode || "--"}</Text>
        <Text variant="bodySmall">{t("labels.itemNumber")}: {line.itemNumber || "--"}</Text>
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
              label={t("filters.customerPhone")}
              mode="outlined"
              style={styles.filterInput}
              value={draftFilters.customerPhone ?? ""}
              onChangeText={(value) =>
                setDraftFilters((current) => ({ ...current, customerPhone: value || undefined }))
              }
            />
            <TextInput
              dense
              label={t("filters.customerName")}
              mode="outlined"
              style={styles.filterInput}
              value={draftFilters.customerName ?? ""}
              onChangeText={(value) =>
                setDraftFilters((current) => ({ ...current, customerName: value || undefined }))
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
              <Card
                key={order.installmentGuid || order.installmentNumber}
                mode="outlined"
                style={styles.card}
              >
                <Card.Title
                  title={order.installmentNumber || order.installmentGuid}
                  subtitle={formatDateTime(order.createdAt, localeTag)}
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
                    <EntityTag kind="store" code={order.storeCode} label={order.storeName || order.storeCode} />
                    <StatusBadge status={order.status} label={t(getStatusLabelKey(order.status))} />
                  </View>
                  <View style={styles.summaryGrid}>
                    <SummaryMetric label={t("labels.totalAmount")} value={formatMoney(order.totalAmount)} />
                    <SummaryMetric
                      label={t("labels.minimumDownPayment")}
                      value={formatMoney(order.minimumDownPayment)}
                    />
                    <SummaryMetric
                      label={t("labels.downPaymentAmount")}
                      value={formatMoney(order.downPaymentAmount)}
                    />
                    <SummaryMetric label={t("labels.paidAmount")} value={formatMoney(order.paidAmount)} />
                    <SummaryMetric
                      label={t("labels.balanceAmount")}
                      value={formatMoney(order.balanceAmount)}
                    />
                  </View>
                  <Text variant="bodyMedium">
                    {t("labels.customer")}: {order.customerName || "--"} / {order.customerPhone || "--"}
                  </Text>
                  <Text variant="bodyMedium">
                    {t("labels.cashier")}: {order.cashierName || "--"}
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
              <Text variant="titleMedium">
                {detailOrder?.installmentNumber || detailOrder?.installmentGuid || "--"}
              </Text>
              <Text selectable variant="bodySmall" style={styles.subtitle}>
                {formatDateTime(detailOrder?.createdAt, localeTag)}
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
                    code={detailOrder?.storeCode}
                    label={detailOrder?.storeName || detailOrder?.storeCode}
                  />
                  <StatusBadge
                    status={detailOrder?.status}
                    label={t(getStatusLabelKey(detailOrder?.status))}
                  />
                </View>
                <View style={styles.summaryGrid}>
                  <SummaryMetric label={t("labels.totalAmount")} value={formatMoney(detailOrder?.totalAmount)} />
                  <SummaryMetric
                    label={t("labels.minimumDownPayment")}
                    value={formatMoney(detailOrder?.minimumDownPayment)}
                  />
                  <SummaryMetric
                    label={t("labels.downPaymentAmount")}
                    value={formatMoney(detailOrder?.downPaymentAmount)}
                  />
                  <SummaryMetric
                    label={t("labels.paidAmount")}
                    value={formatMoney(detailOrder?.paidAmount)}
                  />
                  <SummaryMetric
                    label={t("labels.balanceAmount")}
                    value={formatMoney(detailOrder?.balanceAmount)}
                  />
                </View>
                <DetailLine label={t("labels.customerName")} value={detailOrder?.customerName || "--"} />
                <DetailLine label={t("labels.customerPhone")} value={detailOrder?.customerPhone || "--"} />
                <DetailLine label={t("labels.cashier")} value={detailOrder?.cashierName || "--"} />
                <DetailLine label={t("labels.deviceCode")} value={detail?.order?.deviceCode || "--"} />
                {detail?.order?.note ? (
                  <DetailLine label={t("labels.note")} value={detail.order.note} />
                ) : null}
              </View>

              {detail?.pickupInfo ? (
                <>
                  <SectionTitle>{t("labels.pickupInfo")}</SectionTitle>
                  <View style={styles.recordRow}>
                    <DetailLine
                      label={t("labels.pickedUpAt")}
                      value={formatDateTime(detail.pickupInfo.pickedUpAt, localeTag)}
                    />
                    <DetailLine
                      label={t("labels.pickedUpBy")}
                      value={detail.pickupInfo.pickedUpBy || "--"}
                    />
                    <DetailLine
                      label={t("labels.pickupNote")}
                      value={detail.pickupInfo.pickupNote || "--"}
                    />
                  </View>
                </>
              ) : null}

              {detail?.cancellationInfo ? (
                <>
                  <SectionTitle>{t("labels.cancellationInfo")}</SectionTitle>
                  <View style={[styles.recordRow, styles.cancellationRecordRow]}>
                    <DetailLine
                      label={t("labels.cancellationKind")}
                      value={t(
                        getCancellationKindLabelKey(
                          detail.cancellationInfo.cancellationKind
                        )
                      )}
                    />
                    <DetailLine
                      label={t("labels.cancelledAt")}
                      value={formatDateTime(detail.cancellationInfo.cancelledAt, localeTag)}
                    />
                    <DetailLine
                      label={t("labels.cancelledBy")}
                      value={detail.cancellationInfo.cancelledBy || "--"}
                    />
                    <DetailLine
                      label={t("labels.cancellationReason")}
                      value={detail.cancellationInfo.cancellationReason || "--"}
                    />
                  </View>
                </>
              ) : null}

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
    fontVariant: ["tabular-nums"],
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
    fontVariant: ["tabular-nums"],
    fontWeight: "700",
  },
  paymentStatusBadge: {
    alignSelf: "flex-start",
    backgroundColor: "#F6FFED",
    borderColor: "#B7EB8F",
    borderRadius: 999,
    borderWidth: 1,
    marginTop: 4,
    paddingHorizontal: 8,
    paddingVertical: 2,
  },
  paymentStatusBadgeVoided: {
    backgroundColor: "#F5F5F5",
    borderColor: "#D9D9D9",
  },
  paymentStatusBadgeText: {
    color: "#237804",
    fontWeight: "600",
  },
  paymentStatusBadgeTextVoided: {
    color: "#667085",
  },
  voidedRecordRow: {
    backgroundColor: "#F9FAFB",
    borderColor: "#D0D5DD",
  },
  voidedText: {
    color: "#667085",
    textDecorationLine: "line-through",
  },
  voidedMoneyText: {
    color: "#667085",
    textDecorationLine: "line-through",
  },
  cancellationRecordRow: {
    backgroundColor: "#FFF8F7",
    borderColor: "#FDA29B",
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
