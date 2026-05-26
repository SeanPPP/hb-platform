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
  buildStoreVoucherDetailTargets,
  fetchStoreVoucherDetailByTargets,
  fetchStoreVouchers,
} from "@/modules/store-vouchers/api";
import type {
  StoreVoucher,
  StoreVoucherDetail,
  StoreVoucherFilters,
  StoreVoucherLedgerItem,
  StoreVoucherRelatedOrder,
  StoreVoucherStatus,
} from "@/modules/store-vouchers/types";
import type { Store } from "@/modules/shop/types";
import { useStores } from "@/modules/shop/use-stores";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { resolveLocaleTag } from "@/shared/i18n/types";
import { resolveQrDisplayValue } from "@/shared/utils/qr-display";

type DateFilterKey = "startDate" | "endDate";
type StatusOption = {
  key: string;
  value: string;
  labelKey: string;
};

const PAGE_SIZE = 20;
const VOUCHER_STATUS_OPTIONS: StatusOption[] = [
  { key: "1", value: "1", labelKey: "statuses.available" },
  { key: "2", value: "2", labelKey: "statuses.used" },
  { key: "3", value: "3", labelKey: "statuses.expired" },
  { key: "0", value: "0", labelKey: "statuses.voided" },
];
const VOUCHER_STATUS_LABEL_KEYS: Record<string, string> = {
  "0": "statuses.voided",
  "1": "statuses.available",
  "2": "statuses.used",
  "3": "statuses.expired",
  active: "statuses.available",
  available: "statuses.available",
  issued: "statuses.available",
  use: "statuses.used",
  used: "statuses.used",
  redeemed: "statuses.used",
  expired: "statuses.expired",
  void: "statuses.voided",
  voided: "statuses.voided",
  cancelled: "statuses.voided",
  canceled: "statuses.voided",
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

function getPageCount(total: number, pageSize: number) {
  return Math.max(1, Math.ceil(total / pageSize));
}

function getFilterLabel(value?: string | null, placeholder?: string) {
  return value?.trim() || placeholder || "--";
}

function normalizeStatusKey(status?: StoreVoucherStatus) {
  return status == null ? "" : String(status).trim().toLowerCase();
}

function getStatusLabelKey(status?: StoreVoucherStatus) {
  return VOUCHER_STATUS_LABEL_KEYS[normalizeStatusKey(status)] ?? "statuses.unknown";
}

function StatusBadge({
  status,
  label,
}: {
  status?: StoreVoucherStatus;
  label: string;
}) {
  const normalizedStatus = normalizeStatusKey(status);
  const toneMap: Record<string, { backgroundColor: string; borderColor: string; textColor: string }> = {
    "0": { backgroundColor: "#FFF1F0", borderColor: "#FFA39E", textColor: "#A8071A" },
    "1": { backgroundColor: "#F6FFED", borderColor: "#B7EB8F", textColor: "#237804" },
    "2": { backgroundColor: "#E6F4FF", borderColor: "#91CAFF", textColor: "#0958D9" },
    "3": { backgroundColor: "#FFF7E6", borderColor: "#F7C58B", textColor: "#AD6800" },
    unknown: { backgroundColor: "#F5F5F5", borderColor: "#D9D9D9", textColor: "#595959" },
  };
  const tone =
    toneMap[normalizedStatus] ??
    (VOUCHER_STATUS_LABEL_KEYS[normalizedStatus] === "statuses.available"
      ? toneMap["1"]
      : VOUCHER_STATUS_LABEL_KEYS[normalizedStatus] === "statuses.used"
        ? toneMap["2"]
        : VOUCHER_STATUS_LABEL_KEYS[normalizedStatus] === "statuses.expired"
          ? toneMap["3"]
          : VOUCHER_STATUS_LABEL_KEYS[normalizedStatus] === "statuses.voided"
            ? toneMap["0"]
            : toneMap.unknown);

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

function LedgerActionBadge({ action, label }: { action: "issued" | "used"; label: string }) {
  const tone =
    action === "issued"
      ? { backgroundColor: "#F6FFED", borderColor: "#B7EB8F", textColor: "#237804" }
      : { backgroundColor: "#E6F4FF", borderColor: "#91CAFF", textColor: "#0958D9" };

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

export default function StoreVouchersScreen() {
  const { t, language } = useAppTranslation(["storeVouchers", "common"]);
  const localeTag = useMemo(() => resolveLocaleTag(language), [language]);
  const { stores, isLoading: storesLoading } = useStores();
  const [draftFilters, setDraftFilters] = useState<StoreVoucherFilters>({});
  const [filters, setFilters] = useState<StoreVoucherFilters>({});
  const [storePickerVisible, setStorePickerVisible] = useState(false);
  const [statusPickerVisible, setStatusPickerVisible] = useState(false);
  const [datePickerTarget, setDatePickerTarget] = useState<DateFilterKey | null>(null);
  const [page, setPage] = useState(1);
  const [items, setItems] = useState<StoreVoucher[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [selectedVoucher, setSelectedVoucher] = useState<StoreVoucher | null>(null);
  const [detail, setDetail] = useState<StoreVoucherDetail | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [snackbar, setSnackbar] = useState("");

  const selectedStore = useMemo(
    () => stores.find((store) => store.storeCode === (draftFilters.storeCode ?? "")) ?? null,
    [draftFilters.storeCode, stores]
  );
  const selectedStatus = useMemo(
    () =>
      VOUCHER_STATUS_OPTIONS.find(
        (option) => option.value === String(draftFilters.status ?? "")
      ) ?? null,
    [draftFilters.status]
  );
  const statusItems = useMemo<SelectionListItem[]>(
    () =>
      VOUCHER_STATUS_OPTIONS.map((option) => ({
        key: option.key,
        label: t(option.labelKey),
      })),
    [t]
  );
  const pageCount = getPageCount(total, PAGE_SIZE);
  const currentDateFilterValue = datePickerTarget ? draftFilters[datePickerTarget] : undefined;
  const detailVoucher = detail?.voucher ?? selectedVoucher;
  const detailVoucherQrValue = resolveQrDisplayValue(detailVoucher?.voucherCode, detailVoucher?.id);
  const ledgerItems = detail?.ledger ?? [];
  const relatedOrders = detail?.relatedOrders ?? [];
  const detailTargets = useMemo(
    () =>
      buildStoreVoucherDetailTargets({
        voucherCode: selectedVoucher?.voucherCode,
        id: selectedVoucher?.id,
      }),
    [selectedVoucher?.id, selectedVoucher?.voucherCode]
  );

  const loadVouchers = useCallback(
    async (refresh = false) => {
      if (refresh) {
        setRefreshing(true);
      } else {
        setLoading(true);
      }

      try {
        const result = await fetchStoreVouchers({ page, pageSize: PAGE_SIZE, filters });
        setItems(result.items);
        setTotal(result.total);
      } catch (error) {
        setSnackbar(error instanceof Error ? error.message : t("messages.loadFailed"));
      } finally {
        setLoading(false);
        setRefreshing(false);
      }
    },
    [filters, page, t]
  );

  useEffect(() => {
    void loadVouchers();
  }, [loadVouchers]);

  useEffect(() => {
    if (!detailTargets.length) {
      return;
    }

    let active = true;
    setDetail(null);
    setDetailLoading(true);

    fetchStoreVoucherDetailByTargets(detailTargets)
      .then((result) => {
        if (active) {
          setDetail(result);
        }
      })
      .catch((error) => {
        if (active) {
          setSnackbar(error instanceof Error ? error.message : t("messages.detailsLoadFailed"));
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
  }, [detailTargets, t]);

  const applyFilters = useCallback(() => {
    setPage(1);
    setFilters(draftFilters);
  }, [draftFilters]);

  const clearFilters = useCallback(() => {
    const emptyFilters: StoreVoucherFilters = {};
    setDraftFilters(emptyFilters);
    setFilters(emptyFilters);
    setPage(1);
  }, []);

  const handleSelectStore = useCallback((store: Store | null) => {
    setDraftFilters((current) => ({
      ...current,
      storeCode: store?.storeCode || undefined,
    }));
    setStorePickerVisible(false);
  }, []);

  const handleSelectStatus = useCallback((item: SelectionListItem | null) => {
    const option = item
      ? VOUCHER_STATUS_OPTIONS.find((statusOption) => statusOption.key === item.key)
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
    setSelectedVoucher(null);
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

  const renderLedgerItem = (item: StoreVoucherLedgerItem) => (
    <View key={item.id || `${item.action}-${item.actionTime}-${item.amount}`} style={styles.recordRow}>
      <View style={styles.recordHeader}>
        <LedgerActionBadge action={item.action} label={t(`ledgerActions.${item.action}`)} />
        <Text variant="titleSmall" style={styles.moneyText}>
          {formatMoney(item.amount)}
        </Text>
      </View>
      <DetailLine label={t("labels.actionTime")} value={formatDateTime(item.actionTime, localeTag)} />
      <DetailLine label={t("labels.remainingAfter")} value={formatMoney(item.remainingAmount)} />
      <DetailLine label={t("labels.operator")} value={item.operatorName || item.operatorId || "--"} />
      <DetailLine label={t("labels.reference")} value={item.reference || item.orderNo || item.orderGuid || "--"} />
      {item.remark ? <DetailLine label={t("labels.remark")} value={item.remark} /> : null}
    </View>
  );

  const renderRelatedOrder = (order: StoreVoucherRelatedOrder) => (
    <View key={order.orderGuid || order.orderNo} style={styles.recordRow}>
      <View style={styles.recordHeader}>
        <View style={styles.recordTitleGroup}>
          <Text variant="titleSmall" style={styles.recordTitle} numberOfLines={1}>
            {order.orderNo || order.orderGuid || "--"}
          </Text>
          <Text variant="bodySmall" style={styles.secondaryText}>
            {formatDateTime(order.orderTime, localeTag)}
          </Text>
        </View>
        <Text variant="titleSmall" style={styles.moneyText}>
          {formatMoney(order.amount)}
        </Text>
      </View>
      <View style={styles.tagRow}>
        <EntityTag kind="store" code={order.storeCode} compact />
        {order.supplierCode ? <EntityTag kind="supplier" code={order.supplierCode} compact /> : null}
      </View>
    </View>
  );

  return (
    <SafeAreaView style={styles.safeArea}>
      <ScrollView
        contentContainerStyle={styles.container}
        refreshControl={<RefreshControl refreshing={refreshing} onRefresh={() => void loadVouchers(true)} />}
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
                    {selectedStore?.storeName || draftFilters.storeCode || t("filters.allStores")}
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
            {items.map((voucher) => (
              <Card key={voucher.id || voucher.voucherCode} mode="outlined" style={styles.card}>
                <Card.Title
                  title={voucher.voucherCode || voucher.id}
                  subtitle={formatDateTime(voucher.createTime, localeTag)}
                  right={(props) => (
                    <IconButton
                      {...props}
                      accessibilityLabel={t("actions.viewDetail")}
                      icon="chevron-right"
                      onPress={() => setSelectedVoucher(voucher)}
                    />
                  )}
                />
                <Card.Content style={styles.cardContent}>
                  <View style={styles.tagRow}>
                    <EntityTag kind="store" code={voucher.storeCode} label={voucher.storeName || voucher.storeCode} />
                    {voucher.supplierCode || voucher.supplierName ? (
                      <EntityTag
                        kind="supplier"
                        code={voucher.supplierCode}
                        label={voucher.supplierName || voucher.supplierCode}
                      />
                    ) : null}
                    <StatusBadge status={voucher.status} label={t(getStatusLabelKey(voucher.status))} />
                  </View>
                  <View style={styles.summaryGrid}>
                    <SummaryMetric label={t("labels.amount")} value={formatMoney(voucher.amount)} />
                    <SummaryMetric label={t("labels.remainingAmount")} value={formatMoney(voucher.remainingAmount)} />
                  </View>
                  <Text variant="bodyMedium">
                    {t("labels.customer")}: {voucher.customerName || voucher.customerCode || "--"}
                  </Text>
                  <Text variant="bodyMedium">
                    {t("labels.expiredAt")}: {formatDateTime(voucher.expiredDate, localeTag)}
                  </Text>
                </Card.Content>
                <Card.Actions>
                  <Button compact mode="contained-tonal" onPress={() => setSelectedVoucher(voucher)}>
                    {t("actions.viewDetail")}
                  </Button>
                </Card.Actions>
              </Card>
            ))}
          </View>
        ) : (
          <EmptyState
            title={t("messages.empty")}
            primaryAction={{ label: t("common:actions.refresh"), icon: "refresh", onPress: () => void loadVouchers() }}
          />
        )}

        {items.length ? renderPagination() : null}
      </ScrollView>

      <Portal>
        <Modal visible={Boolean(selectedVoucher)} onDismiss={closeDetail} contentContainerStyle={styles.modal}>
          <View style={styles.modalHeader}>
            <View style={styles.modalTitleGroup}>
              <Text variant="titleMedium">{detailVoucher?.voucherCode || detailVoucher?.id || "--"}</Text>
              <Text variant="bodySmall" style={styles.subtitle}>
                {formatDateTime(detailVoucher?.createTime, localeTag)}
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
              <QrCodePanel label={t("labels.voucherQrCode")} value={detailVoucherQrValue} />
              <View style={styles.detailSummary}>
                <View style={styles.tagRow}>
                  <EntityTag
                    kind="store"
                    code={detailVoucher?.storeCode}
                    label={detailVoucher?.storeName || detailVoucher?.storeCode}
                  />
                  {detailVoucher?.supplierCode || detailVoucher?.supplierName ? (
                    <EntityTag
                      kind="supplier"
                      code={detailVoucher?.supplierCode}
                      label={detailVoucher?.supplierName || detailVoucher?.supplierCode}
                    />
                  ) : null}
                  <StatusBadge
                    status={detailVoucher?.status}
                    label={t(getStatusLabelKey(detailVoucher?.status))}
                  />
                </View>
                <View style={styles.summaryGrid}>
                  <SummaryMetric label={t("labels.amount")} value={formatMoney(detailVoucher?.amount)} />
                  <SummaryMetric label={t("labels.remainingAmount")} value={formatMoney(detailVoucher?.remainingAmount)} />
                </View>
                <DetailLine label={t("labels.customer")} value={detailVoucher?.customerName || detailVoucher?.customerCode || "--"} />
                <DetailLine label={t("labels.expiredAt")} value={formatDateTime(detailVoucher?.expiredDate, localeTag)} />
                {detailVoucher?.remark ? <DetailLine label={t("labels.remark")} value={detailVoucher.remark} /> : null}
              </View>

              <SectionTitle>{t("labels.ledger")}</SectionTitle>
              {ledgerItems.length ? (
                ledgerItems.map(renderLedgerItem)
              ) : (
                <EmptyState title={t("messages.ledgerEmpty")} />
              )}

              <SectionTitle>{t("labels.relatedOrders")}</SectionTitle>
              {relatedOrders.length ? (
                relatedOrders.map(renderRelatedOrder)
              ) : (
                <EmptyState title={t("messages.relatedOrdersEmpty")} />
              )}
            </ScrollView>
          )}
        </Modal>
      </Portal>

      <StorePickerModal
        visible={storePickerVisible}
        stores={stores}
        selectedStoreCode={draftFilters.storeCode}
        title={t("filters.storePickerTitle")}
        cancelLabel={t("common:actions.cancel")}
        includeAllOption
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
    minWidth: 128,
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
    fontWeight: "700",
  },
  moneyText: {
    color: "#047857",
    fontWeight: "700",
  },
  secondaryText: {
    color: "#667085",
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
