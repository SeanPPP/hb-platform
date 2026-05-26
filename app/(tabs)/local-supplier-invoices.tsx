import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Image, Pressable, RefreshControl, ScrollView, StyleSheet, View } from "react-native";
import * as Clipboard from "expo-clipboard";
import { useLocalSearchParams, useRouter } from "expo-router";
import {
  ActivityIndicator,
  Button,
  Card,
  IconButton,
  Menu,
  Modal,
  Portal,
  RadioButton,
  Snackbar,
  Surface,
  Text,
  TextInput,
} from "react-native-paper";
import { MonthDatePicker } from "@/components/attendance/MonthDatePicker";
import { StorePickerModal } from "@/components/ui/StorePickerModal";
import { SafeAreaView } from "react-native-safe-area-context";
import { EmptyState } from "@/components/ui/EmptyState";
import {
  fetchInvoiceDetailsGrid,
  fetchInvoices,
} from "@/modules/local-supplier-invoices/api";
import {
  buildLocalSupplierInvoicesReturnParams,
  decodeLocalSupplierInvoicesReturnParams,
} from "@/modules/local-supplier-invoices/navigation";
import type {
  InvoiceDetailPageSize,
  InvoiceGridFilters,
  InvoiceGridSort,
  InvoiceListPageSize,
  LocalSupplierInvoice,
  LocalSupplierInvoiceItem,
} from "@/modules/local-supplier-invoices/types";
import { printWarehouseProductLabel } from "@/modules/printer/api";
import type { Store } from "@/modules/shop/types";
import { useStores } from "@/modules/shop/use-stores";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

type SortOption = InvoiceGridSort & { labelKey: string };
type DateFilterKey = "orderDateFrom" | "orderDateTo";
type SupplierOption = { supplierCode: string; supplierName: string };

const LIST_PAGE_SIZES: InvoiceListPageSize[] = [20, 50, 100];
const DETAIL_PAGE_SIZES: InvoiceDetailPageSize[] = [50, 100, 200];
const SORT_OPTIONS: SortOption[] = [
  { colId: "OrderDate", direction: "desc", labelKey: "filters.orderDateDesc" },
  { colId: "OrderDate", direction: "asc", labelKey: "filters.orderDateAsc" },
  { colId: "InvoiceNo", direction: "asc", labelKey: "filters.invoiceNoAsc" },
  { colId: "StoreName", direction: "asc", labelKey: "filters.storeNameAsc" },
  { colId: "SupplierName", direction: "asc", labelKey: "filters.supplierNameAsc" },
];

function formatDate(value?: string | null) {
  if (!value) {
    return "--";
  }
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value.slice(0, 10);
  }
  return date.toLocaleDateString();
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

function asRecord(value: unknown): Record<string, unknown> | null {
  return value && typeof value === "object" && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null;
}

function asString(value: unknown) {
  if (typeof value === "string") {
    return value.trim();
  }
  if (typeof value === "number" && Number.isFinite(value)) {
    return String(value);
  }
  return "";
}

function normalizeSupplierOptions(payload: unknown): SupplierOption[] {
  const payloadRecord = asRecord(payload);
  const items = Array.isArray(payload)
    ? payload
    : Array.isArray(payloadRecord?.items)
      ? payloadRecord.items
      : Array.isArray(payloadRecord?.data)
        ? payloadRecord.data
        : [];
  const seen = new Set<string>();

  return items
    .map((item) => {
      const record = asRecord(item) ?? {};
      const supplierCode = asString(
        record.supplierCode ?? record.SupplierCode ?? record.localSupplierCode ?? record.LocalSupplierCode
      );
      const supplierName = asString(
        record.supplierName ?? record.SupplierName ?? record.localSupplierName ?? record.LocalSupplierName
      );

      if (!supplierCode) {
        return null;
      }

      return {
        supplierCode,
        supplierName: supplierName || supplierCode,
      };
    })
    .filter((item): item is SupplierOption => {
      if (!item || seen.has(item.supplierCode)) {
        return false;
      }
      seen.add(item.supplierCode);
      return true;
    })
    .sort((left, right) =>
      left.supplierName.localeCompare(right.supplierName, undefined, {
        sensitivity: "base",
      })
    );
}

function PageSizeMenu<T extends number>({
  label,
  options,
  value,
  onChange,
}: {
  label: string;
  options: T[];
  value: T;
  onChange: (value: T) => void;
}) {
  const [visible, setVisible] = useState(false);
  return (
    <Menu
      visible={visible}
      onDismiss={() => setVisible(false)}
      anchor={
        <Button compact mode="outlined" icon="format-list-numbered" onPress={() => setVisible(true)}>
          {label}: {value}
        </Button>
      }
    >
      {options.map((option) => (
        <Menu.Item
          key={option}
          onPress={() => {
            onChange(option);
            setVisible(false);
          }}
          title={String(option)}
        />
      ))}
    </Menu>
  );
}

export default function LocalSupplierInvoicesScreen() {
  const { t } = useAppTranslation(["localSupplierInvoices", "common"]);
  const router = useRouter();
  const { stores, isLoading: storesLoading } = useStores();
  const searchParams = useLocalSearchParams<{
    source?: string | string[];
    returnInvoiceGuid?: string | string[];
    returnDetailsPage?: string | string[];
    returnDetailsPageSize?: string | string[];
    returnListPage?: string | string[];
    returnListPageSize?: string | string[];
    returnFilterStoreCode?: string | string[];
    returnFilterSupplierCode?: string | string[];
    returnFilterInvoiceNo?: string | string[];
    returnFilterOrderDateFrom?: string | string[];
    returnFilterOrderDateTo?: string | string[];
    returnSortColId?: string | string[];
    returnSortDirection?: string | string[];
  }>();
  const [draftFilters, setDraftFilters] = useState<InvoiceGridFilters>({});
  const [filters, setFilters] = useState<InvoiceGridFilters>({});
  const [sort, setSort] = useState<InvoiceGridSort>({ colId: "OrderDate", direction: "desc" });
  const [sortMenuVisible, setSortMenuVisible] = useState(false);
  const [storePickerVisible, setStorePickerVisible] = useState(false);
  const [supplierPickerVisible, setSupplierPickerVisible] = useState(false);
  const [datePickerTarget, setDatePickerTarget] = useState<DateFilterKey | null>(null);
  const [suppliers, setSuppliers] = useState<SupplierOption[]>([]);
  const [suppliersLoading, setSuppliersLoading] = useState(false);
  const [suppliersLoaded, setSuppliersLoaded] = useState(false);
  const [pageSize, setPageSize] = useState<InvoiceListPageSize>(20);
  const [page, setPage] = useState(1);
  const [items, setItems] = useState<LocalSupplierInvoice[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [selectedInvoice, setSelectedInvoice] = useState<LocalSupplierInvoice | null>(null);
  const [details, setDetails] = useState<LocalSupplierInvoiceItem[]>([]);
  const [detailsTotal, setDetailsTotal] = useState(0);
  const [detailsPage, setDetailsPage] = useState(1);
  const [detailsPageSize, setDetailsPageSize] = useState<InvoiceDetailPageSize>(50);
  const [detailsLoading, setDetailsLoading] = useState(false);
  const [printingDetailGuid, setPrintingDetailGuid] = useState<string | null>(null);
  const [snackbar, setSnackbar] = useState("");
  const pendingRestoreRef = useRef<ReturnType<typeof decodeLocalSupplierInvoicesReturnParams>>(null);
  const handledRestoreKeyRef = useRef<string | null>(null);

  const restoreState = useMemo(
    () => decodeLocalSupplierInvoicesReturnParams(searchParams),
    [
      searchParams.returnDetailsPage,
      searchParams.returnDetailsPageSize,
      searchParams.returnFilterInvoiceNo,
      searchParams.returnFilterOrderDateFrom,
      searchParams.returnFilterOrderDateTo,
      searchParams.returnFilterStoreCode,
      searchParams.returnFilterSupplierCode,
      searchParams.returnInvoiceGuid,
      searchParams.returnListPage,
      searchParams.returnListPageSize,
      searchParams.returnSortColId,
      searchParams.returnSortDirection,
      searchParams.source,
    ]
  );

  const sortLabel = useMemo(() => {
    const match = SORT_OPTIONS.find(
      (option) => option.colId === sort.colId && option.direction === sort.direction
    );
    return t(match?.labelKey ?? "filters.orderDateDesc");
  }, [sort, t]);

  const listPageCount = getPageCount(total, pageSize);
  const detailsPageCount = getPageCount(detailsTotal, detailsPageSize);
  const selectedStore = useMemo(
    () => stores.find((store) => store.storeCode === (draftFilters.storeCode ?? "")) ?? null,
    [draftFilters.storeCode, stores]
  );
  const selectedSupplier = useMemo(
    () =>
      suppliers.find((supplier) => supplier.supplierCode === (draftFilters.supplierCode ?? "")) ??
      null,
    [draftFilters.supplierCode, suppliers]
  );

  const loadInvoices = useCallback(
    async (refresh = false) => {
      if (refresh) {
        setRefreshing(true);
      } else {
        setLoading(true);
      }
      try {
        const result = await fetchInvoices({ page, pageSize, filters, sort });
        setItems(result.items);
        setTotal(result.total);
      } catch (error) {
        setSnackbar(error instanceof Error ? error.message : t("messages.loadFailed"));
      } finally {
        setLoading(false);
        setRefreshing(false);
      }
    },
    [filters, page, pageSize, sort, t]
  );

  const loadDetails = useCallback(async () => {
    if (!selectedInvoice?.invoiceGuid) {
      return;
    }

    setDetailsLoading(true);
    try {
      const result = await fetchInvoiceDetailsGrid(selectedInvoice.invoiceGuid, {
        page: detailsPage,
        pageSize: detailsPageSize,
      });
      setDetails(result.items);
      setDetailsTotal(result.total);
    } catch (error) {
      setSnackbar(error instanceof Error ? error.message : t("messages.detailsLoadFailed"));
    } finally {
      setDetailsLoading(false);
    }
  }, [detailsPage, detailsPageSize, selectedInvoice?.invoiceGuid, t]);

  const loadSuppliers = useCallback(async () => {
    if (suppliersLoading) {
      return;
    }

    setSuppliersLoading(true);
    try {
      const module = (await import("@/modules/local-supplier-invoices/api")) as {
        fetchActiveLocalSuppliers?: () => Promise<unknown>;
      };

      if (typeof module.fetchActiveLocalSuppliers !== "function") {
        throw new Error(t("messages.suppliersSourceUnavailable"));
      }

      const payload = await module.fetchActiveLocalSuppliers();
      setSuppliers(normalizeSupplierOptions(payload));
      setSuppliersLoaded(true);
    } catch (error) {
      setSnackbar(error instanceof Error ? error.message : t("messages.suppliersLoadFailed"));
    } finally {
      setSuppliersLoading(false);
    }
  }, [suppliersLoading, t]);

  useEffect(() => {
    void loadInvoices();
  }, [loadInvoices]);

  useEffect(() => {
    void loadDetails();
  }, [loadDetails]);

  useEffect(() => {
    if (!restoreState) {
      return;
    }

    const restoreKey = JSON.stringify(
      buildLocalSupplierInvoicesReturnParams({
        returnInvoiceGuid: restoreState.returnInvoiceGuid,
        returnDetailsPage: restoreState.returnDetailsPage,
        returnDetailsPageSize: restoreState.returnDetailsPageSize,
        returnListPage: restoreState.returnListPage,
        returnListPageSize: restoreState.returnListPageSize,
        filters: restoreState.filters,
        sort: restoreState.sort,
      })
    );

    if (handledRestoreKeyRef.current === restoreKey) {
      return;
    }

    handledRestoreKeyRef.current = restoreKey;
    pendingRestoreRef.current = restoreState;
    setDraftFilters(restoreState.filters);
    setFilters(restoreState.filters);
    setSort(restoreState.sort);
    setPageSize(restoreState.returnListPageSize);
    setPage(restoreState.returnListPage);
    setSelectedInvoice(null);
    setDetails([]);
    setDetailsTotal(0);
    setDetailsPage(restoreState.returnDetailsPage);
    setDetailsPageSize(restoreState.returnDetailsPageSize);
  }, [restoreState]);

  useEffect(() => {
    const pendingRestore = pendingRestoreRef.current;
    if (
      !pendingRestore
      || loading
      || page !== pendingRestore.returnListPage
      || pageSize !== pendingRestore.returnListPageSize
      || JSON.stringify(filters) !== JSON.stringify(pendingRestore.filters)
      || JSON.stringify(sort) !== JSON.stringify(pendingRestore.sort)
    ) {
      return;
    }

    const matchedInvoice = items.find((item) => item.invoiceGuid === pendingRestore.returnInvoiceGuid);
    pendingRestoreRef.current = null;

    if (!matchedInvoice) {
      setSnackbar(t("messages.restoreInvoiceMissing"));
      return;
    }

    setSelectedInvoice(matchedInvoice);
  }, [filters, items, loading, page, pageSize, sort, t]);

  const applyFilters = useCallback(() => {
    setPage(1);
    setFilters(draftFilters);
  }, [draftFilters]);

  const clearFilters = useCallback(() => {
    const emptyFilters: InvoiceGridFilters = {};
    setDraftFilters(emptyFilters);
    setFilters(emptyFilters);
    setPage(1);
  }, []);

  const openDetails = useCallback((invoice: LocalSupplierInvoice) => {
    setSelectedInvoice(invoice);
    setDetails([]);
    setDetailsTotal(0);
    setDetailsPage(1);
    setDetailsPageSize(50);
  }, []);

  const copyValue = useCallback(
    async (label: string, value: string) => {
      if (!value.trim()) {
        return;
      }

      try {
        await Clipboard.setStringAsync(value);
        setSnackbar(t("messages.copied", { label }));
      } catch {
        setSnackbar(t("messages.copyFailed"));
      }
    },
    [t]
  );

  const printDetail = useCallback(
    async (detail: LocalSupplierInvoiceItem) => {
      setPrintingDetailGuid(detail.detailGuid);
      try {
        await printWarehouseProductLabel({
          productCode: detail.productCode || detail.storeProductCode || detail.itemNumber,
          productName: detail.productName,
          itemNumber: detail.itemNumber || null,
          barcode: detail.barcode || null,
          supplierName: selectedInvoice?.supplierName || selectedInvoice?.supplierCode || null,
          purchasePrice: detail.purchasePrice,
          retailPrice: detail.retailPrice,
        });
        setSnackbar(t("messages.printSuccess"));
      } catch (error) {
        const fallback = t("messages.printFailed");
        setSnackbar(error instanceof Error ? `${fallback}: ${error.message}` : fallback);
      } finally {
        setPrintingDetailGuid(null);
      }
    },
    [selectedInvoice?.supplierCode, selectedInvoice?.supplierName, t]
  );

  const openProduct = useCallback(
    (detail: LocalSupplierInvoiceItem) => {
      const keyword = detail.barcode || detail.itemNumber || detail.productCode;
      if (!keyword) {
        setSnackbar(t("messages.missingProductKeyword"));
        return;
      }

      try {
        router.push({
          pathname: "/(tabs)/product-query",
          params: {
            productCode: detail.productCode,
            keyword,
            storeCode: detail.storeCode || selectedInvoice?.storeCode || "",
            ...buildLocalSupplierInvoicesReturnParams({
              returnInvoiceGuid: detail.invoiceGuid || selectedInvoice?.invoiceGuid || "",
              returnDetailsPage: detailsPage,
              returnDetailsPageSize: detailsPageSize,
              returnListPage: page,
              returnListPageSize: pageSize,
              filters,
              sort,
            }),
          },
        } as Parameters<typeof router.push>[0]);
      } catch (error) {
        setSnackbar(error instanceof Error ? error.message : t("messages.productOpenFailed"));
      }
    },
    [detailsPage, detailsPageSize, filters, page, pageSize, router, selectedInvoice?.invoiceGuid, selectedInvoice?.storeCode, sort, t]
  );

  const openSupplierPicker = useCallback(() => {
    setSupplierPickerVisible(true);
    if (!suppliersLoaded) {
      void loadSuppliers();
    }
  }, [loadSuppliers, suppliersLoaded]);

  const handleSelectStore = useCallback((store: Store | null) => {
    setDraftFilters((current) => ({
      ...current,
      storeCode: store?.storeCode || undefined,
    }));
    setStorePickerVisible(false);
  }, []);

  const handleSelectSupplier = useCallback((supplierCode?: string) => {
    setDraftFilters((current) => ({
      ...current,
      supplierCode: supplierCode || undefined,
    }));
    setSupplierPickerVisible(false);
  }, []);

  const updateDateFilter = useCallback((key: DateFilterKey, value?: string) => {
    setDraftFilters((current) => ({
      ...current,
      [key]: value || undefined,
    }));
  }, []);

  const currentDateFilterValue = datePickerTarget ? draftFilters[datePickerTarget] : undefined;

  const renderPagination = (
    currentPage: number,
    pageCount: number,
    onChangePage: (page: number) => void
  ) => (
    <View style={styles.pagination}>
      <Button
        compact
        disabled={currentPage <= 1}
        icon="chevron-left"
        mode="outlined"
        onPress={() => onChangePage(Math.max(1, currentPage - 1))}
      >
        {t("common:actions.back")}
      </Button>
      <Text variant="bodyMedium">
        {currentPage} / {pageCount}
      </Text>
      <Button
        compact
        contentStyle={styles.nextButtonContent}
        disabled={currentPage >= pageCount}
        icon="chevron-right"
        mode="outlined"
        onPress={() => onChangePage(Math.min(pageCount, currentPage + 1))}
      >
        {t("actions.loadMore")}
      </Button>
    </View>
  );

  return (
    <SafeAreaView style={styles.safeArea}>
      <ScrollView
        contentContainerStyle={styles.container}
        refreshControl={
          <RefreshControl refreshing={refreshing} onRefresh={() => void loadInvoices(true)} />
        }
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
                {storesLoading ? (
                  <ActivityIndicator size="small" />
                ) : (
                  <IconButton icon="store-outline" size={20} />
                )}
              </Surface>
            </Pressable>
            <Pressable onPress={openSupplierPicker} style={styles.filterInput}>
              <Surface style={styles.pickerField} elevation={0}>
                <View style={styles.pickerFieldText}>
                  <Text variant="labelMedium" style={styles.pickerFieldLabel}>
                    {t("filters.supplierCode")}
                  </Text>
                  <Text variant="bodyLarge" numberOfLines={1}>
                    {selectedSupplier?.supplierName || draftFilters.supplierCode || t("filters.allSuppliers")}
                  </Text>
                </View>
                {suppliersLoading ? (
                  <ActivityIndicator size="small" />
                ) : (
                  <IconButton icon="truck-outline" size={20} />
                )}
              </Surface>
            </Pressable>
            <TextInput
              dense
              label={t("filters.invoiceNo")}
              mode="outlined"
              value={draftFilters.invoiceNo ?? ""}
              onChangeText={(value) => setDraftFilters((current) => ({ ...current, invoiceNo: value }))}
              style={styles.filterInput}
            />
            <Pressable
              onPress={() => setDatePickerTarget("orderDateFrom")}
              style={styles.filterInput}
            >
              <Surface style={styles.pickerField} elevation={0}>
                <View style={styles.pickerFieldText}>
                  <Text variant="labelMedium" style={styles.pickerFieldLabel}>
                    {t("filters.dateFrom")}
                  </Text>
                  <Text
                    variant="bodyLarge"
                    numberOfLines={1}
                    style={draftFilters.orderDateFrom ? undefined : styles.pickerPlaceholder}
                  >
                    {getFilterLabel(draftFilters.orderDateFrom, t("filters.emptyDate"))}
                  </Text>
                </View>
                <IconButton icon="calendar-month-outline" size={20} />
              </Surface>
            </Pressable>
            <Pressable
              onPress={() => setDatePickerTarget("orderDateTo")}
              style={styles.filterInput}
            >
              <Surface style={styles.pickerField} elevation={0}>
                <View style={styles.pickerFieldText}>
                  <Text variant="labelMedium" style={styles.pickerFieldLabel}>
                    {t("filters.dateTo")}
                  </Text>
                  <Text
                    variant="bodyLarge"
                    numberOfLines={1}
                    style={draftFilters.orderDateTo ? undefined : styles.pickerPlaceholder}
                  >
                    {getFilterLabel(draftFilters.orderDateTo, t("filters.emptyDate"))}
                  </Text>
                </View>
                <IconButton icon="calendar-month-outline" size={20} />
              </Surface>
            </Pressable>
          </View>

          <View style={styles.filterActions}>
            <PageSizeMenu
              label={t("filters.pageSize")}
              options={LIST_PAGE_SIZES}
              value={pageSize}
              onChange={(value) => {
                setPageSize(value);
                setPage(1);
              }}
            />
            <Menu
              visible={sortMenuVisible}
              onDismiss={() => setSortMenuVisible(false)}
              anchor={
                <Button compact icon="sort" mode="outlined" onPress={() => setSortMenuVisible(true)}>
                  {sortLabel}
                </Button>
              }
            >
              {SORT_OPTIONS.map((option) => (
                <Menu.Item
                  key={`${option.colId}-${option.direction}`}
                  onPress={() => {
                    setSort({ colId: option.colId, direction: option.direction });
                    setPage(1);
                    setSortMenuVisible(false);
                  }}
                  title={t(option.labelKey)}
                />
              ))}
            </Menu>
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
          <View style={styles.invoiceList}>
            {items.map((invoice) => (
              <Card key={invoice.invoiceGuid} mode="outlined" style={styles.invoiceCard}>
                <Card.Title
                  title={invoice.invoiceNo || invoice.invoiceGuid}
                  subtitle={`${invoice.storeName || invoice.storeCode || "--"} · ${invoice.supplierName || invoice.supplierCode || "--"}`}
                  right={(props) => (
                    <IconButton
                      {...props}
                      accessibilityLabel={t("common:actions.viewDetail")}
                      icon="chevron-right"
                      onPress={() => openDetails(invoice)}
                    />
                  )}
                />
                <Card.Content style={styles.invoiceMeta}>
                  <Text variant="bodyMedium">{t("labels.orderDate")}: {formatDate(invoice.orderDate)}</Text>
                  <Text variant="bodyMedium">{t("labels.amount")}: {formatMoney(invoice.totalAmount)}</Text>
                  <Text variant="bodyMedium">{t("labels.receivedAmount")}: {formatMoney(invoice.receivedTotalAmount)}</Text>
                </Card.Content>
                <Card.Actions>
                  <Button compact mode="contained-tonal" onPress={() => openDetails(invoice)}>
                    {t("common:actions.viewDetail")}
                  </Button>
                </Card.Actions>
              </Card>
            ))}
          </View>
        ) : (
          <EmptyState
            title={t("messages.empty")}
            primaryAction={{ label: t("common:actions.refresh"), icon: "refresh", onPress: () => void loadInvoices() }}
          />
        )}

        {items.length ? renderPagination(page, listPageCount, setPage) : null}
      </ScrollView>

      <Portal>
        <Modal
          visible={Boolean(selectedInvoice)}
          onDismiss={() => setSelectedInvoice(null)}
          contentContainerStyle={styles.modal}
        >
          <View style={styles.modalHeader}>
            <View style={styles.modalTitleGroup}>
              <Text variant="titleMedium">{selectedInvoice?.invoiceNo || "--"}</Text>
              <Text variant="bodySmall" style={styles.subtitle}>
                {selectedInvoice?.storeName || selectedInvoice?.storeCode || "--"} · {selectedInvoice?.supplierName || selectedInvoice?.supplierCode || "--"}
              </Text>
            </View>
            <IconButton
              accessibilityLabel={t("common:actions.close")}
              icon="close"
              onPress={() => setSelectedInvoice(null)}
            />
          </View>

          <View style={styles.detailToolbar}>
            <PageSizeMenu
              label={t("labels.detailPageSize")}
              options={DETAIL_PAGE_SIZES}
              value={detailsPageSize}
              onChange={(value) => {
                setDetailsPageSize(value);
                setDetailsPage(1);
              }}
            />
            <Text variant="bodyMedium">
              {t("labels.detailsCount", { count: detailsTotal })}
            </Text>
          </View>

          {detailsLoading ? (
            <View style={styles.loadingBox}>
              <ActivityIndicator />
              <Text variant="bodyMedium">{t("common:loading")}</Text>
            </View>
          ) : details.length ? (
            <ScrollView contentContainerStyle={styles.detailList}>
              {details.map((detail) => (
                <View key={detail.detailGuid} style={styles.detailRow}>
                  {detail.productImage ? (
                    <Image source={{ uri: detail.productImage }} style={styles.productImage} />
                  ) : (
                    <View style={styles.productImagePlaceholder}>
                      <Text variant="labelSmall">IMG</Text>
                    </View>
                  )}
                  <View style={styles.detailBody}>
                    <Text variant="titleSmall" numberOfLines={2}>
                      {detail.productName || "--"}
                    </Text>
                    <View style={styles.copyLine}>
                      <Button
                        compact
                        icon="content-copy"
                        mode="text"
                        onPress={() => void copyValue(t("labels.itemNumber"), detail.itemNumber)}
                      >
                        {t("labels.itemNumber")}: {detail.itemNumber || "--"}
                      </Button>
                    </View>
                    <View style={styles.copyLine}>
                      <Button
                        compact
                        icon="content-copy"
                        mode="text"
                        onPress={() => void copyValue(t("labels.barcode"), detail.barcode)}
                      >
                        {t("labels.barcode")}: {detail.barcode || "--"}
                      </Button>
                    </View>
                    <View style={styles.priceLine}>
                      <Text variant="bodySmall">{t("labels.purchasePrice")}: {formatMoney(detail.purchasePrice)}</Text>
                      <Text variant="bodySmall">{t("labels.quantity")}: {formatNumber(detail.quantity)}</Text>
                    </View>
                    <View style={styles.detailActions}>
                      <Button
                        compact
                        disabled={printingDetailGuid === detail.detailGuid}
                        icon="printer"
                        loading={printingDetailGuid === detail.detailGuid}
                        mode="contained-tonal"
                        onPress={() => void printDetail(detail)}
                      >
                        {t("actions.printLabel")}
                      </Button>
                      <Button compact icon="pencil-box-outline" mode="outlined" onPress={() => openProduct(detail)}>
                        {t("actions.openProduct")}
                      </Button>
                    </View>
                  </View>
                </View>
              ))}
            </ScrollView>
          ) : (
            <EmptyState title={t("messages.detailsEmpty")} />
          )}

          {details.length ? renderPagination(detailsPage, detailsPageCount, setDetailsPage) : null}
        </Modal>
      </Portal>

      <StorePickerModal
        visible={storePickerVisible}
        stores={stores}
        selectedStoreCode={draftFilters.storeCode ?? null}
        title={t("filters.storePickerTitle")}
        cancelLabel={t("common:actions.cancel")}
        includeAllOption
        allLabel={t("filters.allStores")}
        onDismiss={() => setStorePickerVisible(false)}
        onSelectStore={handleSelectStore}
      />

      <Portal>
        <Modal
          visible={supplierPickerVisible}
          onDismiss={() => setSupplierPickerVisible(false)}
          contentContainerStyle={styles.pickerModal}
        >
          <View style={styles.pickerModalHeader}>
            <Text variant="titleMedium">{t("filters.supplierPickerTitle")}</Text>
            <Button onPress={() => setSupplierPickerVisible(false)}>
              {t("common:actions.cancel")}
            </Button>
          </View>

          <ScrollView
            style={styles.pickerModalList}
            contentContainerStyle={styles.pickerModalListContent}
          >
            <View style={styles.pickerRow}>
              <RadioButton
                value="all-suppliers"
                status={!draftFilters.supplierCode ? "checked" : "unchecked"}
                onPress={() => handleSelectSupplier()}
              />
              <Button
                mode="text"
                onPress={() => handleSelectSupplier()}
                style={styles.pickerRowButton}
                contentStyle={styles.pickerRowButtonContent}
              >
                {t("filters.allSuppliers")}
              </Button>
            </View>

            {suppliersLoading ? (
              <View style={styles.loadingBox}>
                <ActivityIndicator />
                <Text variant="bodyMedium">{t("messages.suppliersLoading")}</Text>
              </View>
            ) : suppliers.length ? (
              suppliers.map((supplier) => (
                <View key={supplier.supplierCode} style={styles.pickerRow}>
                  <RadioButton
                    value={supplier.supplierCode}
                    status={
                      draftFilters.supplierCode === supplier.supplierCode
                        ? "checked"
                        : "unchecked"
                    }
                    onPress={() => handleSelectSupplier(supplier.supplierCode)}
                  />
                  <Button
                    mode="text"
                    onPress={() => handleSelectSupplier(supplier.supplierCode)}
                    style={styles.pickerRowButton}
                    contentStyle={styles.pickerRowButtonContent}
                  >
                    {supplier.supplierName}
                  </Button>
                </View>
              ))
            ) : (
              <View style={styles.emptyPickerState}>
                <Text variant="bodyMedium">{t("messages.suppliersEmpty")}</Text>
                <Button icon="refresh" mode="outlined" onPress={() => void loadSuppliers()}>
                  {t("common:actions.retry")}
                </Button>
              </View>
            )}
          </ScrollView>
        </Modal>
      </Portal>

      <Portal>
        <Modal
          visible={Boolean(datePickerTarget)}
          onDismiss={() => setDatePickerTarget(null)}
          contentContainerStyle={styles.dateModal}
        >
          <View style={styles.pickerModalHeader}>
            <Text variant="titleMedium">
              {datePickerTarget === "orderDateFrom"
                ? t("filters.dateFrom")
                : t("filters.dateTo")}
            </Text>
            <Button onPress={() => setDatePickerTarget(null)}>
              {t("common:actions.cancel")}
            </Button>
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
  invoiceList: {
    gap: 10,
  },
  invoiceCard: {
    backgroundColor: "#FFFFFF",
    borderRadius: 8,
  },
  invoiceMeta: {
    gap: 4,
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
    paddingRight: 8,
  },
  detailToolbar: {
    alignItems: "center",
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
    justifyContent: "space-between",
    paddingVertical: 8,
  },
  detailList: {
    gap: 10,
    paddingBottom: 8,
  },
  detailRow: {
    backgroundColor: "#F9FAFB",
    borderColor: "#EAECF0",
    borderRadius: 8,
    borderWidth: 1,
    flexDirection: "row",
    gap: 10,
    padding: 10,
  },
  productImage: {
    backgroundColor: "#EEF2F6",
    borderRadius: 6,
    height: 72,
    width: 72,
  },
  productImagePlaceholder: {
    alignItems: "center",
    backgroundColor: "#EEF2F6",
    borderRadius: 6,
    height: 72,
    justifyContent: "center",
    width: 72,
  },
  detailBody: {
    flex: 1,
    gap: 4,
    minWidth: 0,
  },
  copyLine: {
    alignItems: "flex-start",
    minHeight: 30,
  },
  priceLine: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 12,
  },
  detailActions: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
    paddingTop: 4,
  },
  pickerModal: {
    alignSelf: "center",
    backgroundColor: "#FFFFFF",
    borderRadius: 12,
    maxHeight: "82%",
    padding: 16,
    width: "88%",
  },
  pickerModalHeader: {
    alignItems: "center",
    flexDirection: "row",
    justifyContent: "space-between",
    marginBottom: 8,
  },
  pickerModalList: {
    flexGrow: 0,
  },
  pickerModalListContent: {
    paddingBottom: 4,
  },
  pickerRow: {
    alignItems: "center",
    flexDirection: "row",
    minHeight: 48,
  },
  pickerRowButton: {
    flex: 1,
  },
  pickerRowButtonContent: {
    justifyContent: "flex-start",
  },
  emptyPickerState: {
    alignItems: "center",
    gap: 12,
    paddingVertical: 24,
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
