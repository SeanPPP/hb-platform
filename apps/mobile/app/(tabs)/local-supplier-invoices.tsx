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
import {
  clearInvoiceDateRange,
  formatInvoiceDateRangeDisplay,
  selectInvoiceDateRange,
  toInvoiceOrderDateFilters,
  type InvoiceDateRangeValue,
} from "@/modules/local-supplier-invoices/date-range";
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
import { bindDeviceStoreFilter, getDeviceBoundStoreCode } from "@/modules/shop/device-bound-store-filter";
import { useStores } from "@/modules/shop/use-stores";
import { resolveLocalizedErrorMessage } from "@/shared/i18n/error-message";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

type SortOption = InvoiceGridSort & { labelKey: string };
type SupplierOption = { supplierCode: string; supplierName: string };
type CalendarCell = { date: Date; dateString: string; isCurrentMonth: boolean };
type EntityTagTone = "store" | "supplier" | "neutral";

const LIST_PAGE_SIZES: InvoiceListPageSize[] = [20, 50, 100];
const DETAIL_PAGE_SIZES: InvoiceDetailPageSize[] = [50, 100, 200];
const DATE_PATTERN = /^(\d{4})-(\d{2})-(\d{2})$/;
const GRID_DAYS = 42;
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

function EntityTag({
  label,
  tone,
}: {
  label: string;
  tone: EntityTagTone;
}) {
  const toneStyles = ENTITY_TAG_STYLES[tone];

  return (
    <View style={[styles.entityTag, toneStyles.tag]}>
      <Text
        variant="labelMedium"
        numberOfLines={1}
        style={[styles.entityTagText, toneStyles.text]}
      >
        {label}
      </Text>
    </View>
  );
}

function getPageCount(total: number, pageSize: number) {
  return Math.max(1, Math.ceil(total / pageSize));
}

function pad2(value: number) {
  return value.toString().padStart(2, "0");
}

function formatMonthDate(date: Date) {
  return `${date.getFullYear()}-${pad2(date.getMonth() + 1)}-${pad2(date.getDate())}`;
}

function parseMonthDate(value?: string) {
  const match = value?.match(DATE_PATTERN);
  if (!match) {
    return undefined;
  }

  const year = Number(match[1]);
  const monthIndex = Number(match[2]) - 1;
  const day = Number(match[3]);
  const parsed = new Date(year, monthIndex, day);

  if (
    parsed.getFullYear() !== year
    || parsed.getMonth() !== monthIndex
    || parsed.getDate() !== day
  ) {
    return undefined;
  }

  return parsed;
}

function normalizeMonthDate(value?: string | null) {
  const parsed = parseMonthDate(value ?? undefined);
  return parsed ? formatMonthDate(parsed) : undefined;
}

function getMonthStart(date: Date) {
  return new Date(date.getFullYear(), date.getMonth(), 1);
}

function addMonths(date: Date, count: number) {
  return new Date(date.getFullYear(), date.getMonth() + count, 1);
}

function buildMonthGrid(displayMonth: Date): CalendarCell[] {
  const monthStart = getMonthStart(displayMonth);
  const mondayOffset = (monthStart.getDay() + 6) % 7;
  const gridStart = new Date(monthStart.getFullYear(), monthStart.getMonth(), 1 - mondayOffset);

  return Array.from({ length: GRID_DAYS }, (_, index) => {
    const date = new Date(gridStart.getFullYear(), gridStart.getMonth(), gridStart.getDate() + index);
    return {
      date,
      dateString: formatMonthDate(date),
      isCurrentMonth: date.getMonth() === monthStart.getMonth(),
    };
  });
}

function chunkWeeks<T>(items: T[]) {
  return Array.from({ length: Math.ceil(items.length / 7) }, (_, index) =>
    items.slice(index * 7, index * 7 + 7)
  );
}

function compareMonthDate(left?: string, right?: string) {
  if (!left || !right) {
    return 0;
  }
  return left.localeCompare(right);
}

function isDateInRange(date: string, from?: string, to?: string) {
  if (!from || !to) {
    return false;
  }
  return compareMonthDate(date, from) >= 0 && compareMonthDate(date, to) <= 0;
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
  const { t, language } = useAppTranslation(["localSupplierInvoices", "common", "attendance"]);
  const getErrorMessage = useCallback((error: unknown, fallbackKey: string) => (
    resolveLocalizedErrorMessage(error, {
      language,
      t,
      fallbackKey,
    })
  ), [language, t]);
  const router = useRouter();
  const {
    stores,
    selectedStoreCode,
    isDeviceMode,
    isLoading: storesLoading,
  } = useStores();
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
  const [dateRangeModalVisible, setDateRangeModalVisible] = useState(false);
  const [dateRangeSnapshot, setDateRangeSnapshot] = useState<InvoiceDateRangeValue | null>(null);
  const [dateRangeDisplayMonth, setDateRangeDisplayMonth] = useState(() => getMonthStart(new Date()));
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
  const suppliersLoadingRef = useRef(false);
  const deviceBoundStoreCode = getDeviceBoundStoreCode({ isDeviceMode, selectedStoreCode });

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
  const monthCells = useMemo(() => buildMonthGrid(dateRangeDisplayMonth), [dateRangeDisplayMonth]);
  const calendarWeeks = useMemo(() => chunkWeeks(monthCells), [monthCells]);
  const today = useMemo(() => formatMonthDate(new Date()), []);
  const weekdayLabels = useMemo(
    () =>
      Array.from({ length: 7 }, (_, index) =>
        t(`attendance:weekdays.${index}`, ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"][index])
      ),
    [t]
  );
  const monthTitle = useMemo(() => {
    const locale = language === "zh" ? "zh-CN" : "en-AU";
    return new Intl.DateTimeFormat(locale, {
      year: "numeric",
      month: "long",
    }).format(dateRangeDisplayMonth);
  }, [dateRangeDisplayMonth, language]);
  const orderDateRangeLabel = useMemo(() => {
    const from = normalizeMonthDate(draftFilters.orderDateFrom);
    const to = normalizeMonthDate(draftFilters.orderDateTo);

    return formatInvoiceDateRangeDisplay(
      { from, to },
      {
        allDatesLabel: t("filters.allDates"),
        formatFrom: (date) => t("filters.dateRangeFrom", { date }),
        formatTo: (date) => t("filters.dateRangeTo", { date }),
        rangeSeparator: " ~ ",
      }
    ).text;
  }, [draftFilters.orderDateFrom, draftFilters.orderDateTo, t]);

  const bindDeviceStore = useCallback(
    (nextFilters: InvoiceGridFilters) =>
      bindDeviceStoreFilter(nextFilters, {
        isDeviceMode,
        selectedStoreCode,
        storeField: "storeCode",
      }),
    [isDeviceMode, selectedStoreCode]
  );

  const loadInvoices = useCallback(
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
        const result = await fetchInvoices({ page, pageSize, filters: bindDeviceStore(filters), sort });
        setItems(result.items);
        setTotal(result.total);
      } catch (error) {
        setSnackbar(getErrorMessage(error, "messages.loadFailed"));
      } finally {
        setLoading(false);
        setRefreshing(false);
      }
    },
    [bindDeviceStore, filters, getErrorMessage, isDeviceMode, page, pageSize, selectedStoreCode, sort]
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
      setSnackbar(getErrorMessage(error, "messages.detailsLoadFailed"));
    } finally {
      setDetailsLoading(false);
    }
  }, [detailsPage, detailsPageSize, getErrorMessage, selectedInvoice?.invoiceGuid]);

  const loadSuppliers = useCallback(async () => {
    if (suppliersLoadingRef.current) {
      return;
    }

    setSuppliersLoading(true);
    suppliersLoadingRef.current = true;
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
      setSnackbar(getErrorMessage(error, "messages.suppliersLoadFailed"));
    } finally {
      suppliersLoadingRef.current = false;
      setSuppliersLoading(false);
    }
  }, [getErrorMessage, t]);

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
    const restoredFilters = bindDeviceStore(restoreState.filters);
    pendingRestoreRef.current = {
      ...restoreState,
      filters: restoredFilters,
    };
    setDraftFilters(restoredFilters);
    setFilters(restoredFilters);
    setSort(restoreState.sort);
    setPageSize(restoreState.returnListPageSize);
    setPage(restoreState.returnListPage);
    setSelectedInvoice(null);
    setDetails([]);
    setDetailsTotal(0);
    setDetailsPage(restoreState.returnDetailsPage);
    setDetailsPageSize(restoreState.returnDetailsPageSize);
  }, [bindDeviceStore, restoreState]);

  useEffect(() => {
    if (!deviceBoundStoreCode) {
      return;
    }

    setDraftFilters((current) =>
      current.storeCode === deviceBoundStoreCode
        ? current
        : { ...current, storeCode: deviceBoundStoreCode }
    );
    setFilters((current) =>
      current.storeCode === deviceBoundStoreCode
        ? current
        : { ...current, storeCode: deviceBoundStoreCode }
    );
  }, [deviceBoundStoreCode]);

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
    setFilters(bindDeviceStore(draftFilters));
  }, [bindDeviceStore, draftFilters]);

  const clearFilters = useCallback(() => {
    const emptyFilters = bindDeviceStore({});
    setDraftFilters(emptyFilters);
    setFilters(emptyFilters);
    setPage(1);
  }, [bindDeviceStore]);

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
        setSnackbar(getErrorMessage(error, "messages.printFailed"));
      } finally {
        setPrintingDetailGuid(null);
      }
    },
    [getErrorMessage, selectedInvoice?.supplierCode, selectedInvoice?.supplierName, t]
  );

  const openProduct = useCallback(
    (detail: LocalSupplierInvoiceItem) => {
      const keyword = detail.barcode || detail.itemNumber || detail.productCode;
      if (!keyword) {
        setSnackbar(t("messages.missingProductKeyword"));
        return;
      }

      try {
        const pushParams = {
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
        } as Parameters<typeof router.push>[0];

        setSelectedInvoice(null);

        const navigate = () => {
          router.push(pushParams);
        };

        if (typeof requestAnimationFrame === "function") {
          requestAnimationFrame(navigate);
        } else {
          setTimeout(navigate, 0);
        }
      } catch (error) {
        setSnackbar(getErrorMessage(error, "messages.productOpenFailed"));
      }
    },
    [detailsPage, detailsPageSize, filters, getErrorMessage, page, pageSize, router, selectedInvoice?.invoiceGuid, selectedInvoice?.storeCode, sort, t]
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
      storeCode: deviceBoundStoreCode ?? store?.storeCode ?? undefined,
    }));
    setStorePickerVisible(false);
  }, [deviceBoundStoreCode]);

  const handleSelectSupplier = useCallback((supplierCode?: string) => {
    setDraftFilters((current) => ({
      ...current,
      supplierCode: supplierCode || undefined,
    }));
    setSupplierPickerVisible(false);
  }, []);

  const updateOrderDateRange = useCallback((nextRange: InvoiceDateRangeValue) => {
    const nextFilters = toInvoiceOrderDateFilters(nextRange);
    setDraftFilters((current) => ({
      ...current,
      orderDateFrom: nextFilters.orderDateFrom,
      orderDateTo: nextFilters.orderDateTo,
    }));
  }, []);

  const openDateRangeModal = useCallback(() => {
    const from = normalizeMonthDate(draftFilters.orderDateFrom);
    const to = normalizeMonthDate(draftFilters.orderDateTo);
    setDateRangeSnapshot({ from, to });
    const displayMonth = parseMonthDate(from || to) ?? new Date();
    setDateRangeDisplayMonth(getMonthStart(displayMonth));
    setDateRangeModalVisible(true);
  }, [draftFilters.orderDateFrom, draftFilters.orderDateTo]);

  const closeDateRangeModal = useCallback(() => {
    setDateRangeModalVisible(false);
    setDateRangeSnapshot(null);
  }, []);

  const cancelDateRangeModal = useCallback(() => {
    if (dateRangeSnapshot) {
      updateOrderDateRange(dateRangeSnapshot);
    }
    closeDateRangeModal();
  }, [closeDateRangeModal, dateRangeSnapshot, updateOrderDateRange]);

  const applyDateRangeModal = useCallback(() => {
    closeDateRangeModal();
  }, [closeDateRangeModal]);

  const clearDateRange = useCallback(() => {
    updateOrderDateRange(clearInvoiceDateRange());
  }, [updateOrderDateRange]);

  const selectRangeDate = useCallback(
    (dateString: string) => {
      const from = normalizeMonthDate(draftFilters.orderDateFrom);
      const to = normalizeMonthDate(draftFilters.orderDateTo);
      updateOrderDateRange(selectInvoiceDateRange({ from, to }, dateString));

      setDateRangeDisplayMonth(getMonthStart(parseMonthDate(dateString) ?? new Date()));
    },
    [draftFilters.orderDateFrom, draftFilters.orderDateTo, updateOrderDateRange]
  );

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
                  <View style={styles.pickerTagRow}>
                    <EntityTag
                      label={selectedStore?.storeName || draftFilters.storeCode || t("filters.allStores")}
                      tone={draftFilters.storeCode ? "store" : "neutral"}
                    />
                  </View>
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
                  <View style={styles.pickerTagRow}>
                    <EntityTag
                      label={selectedSupplier?.supplierName || draftFilters.supplierCode || t("filters.allSuppliers")}
                      tone={draftFilters.supplierCode ? "supplier" : "neutral"}
                    />
                  </View>
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
            <Pressable onPress={openDateRangeModal} style={styles.filterInput}>
              <Surface style={styles.pickerField} elevation={0}>
                <View style={styles.pickerFieldText}>
                  <Text variant="labelMedium" style={styles.pickerFieldLabel}>
                    {t("filters.orderDateRange")}
                  </Text>
                  <Text
                    variant="bodyLarge"
                    numberOfLines={1}
                    style={
                      draftFilters.orderDateFrom || draftFilters.orderDateTo
                        ? undefined
                        : styles.pickerPlaceholder
                    }
                  >
                    {orderDateRangeLabel}
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
                  right={(props) => (
                    <IconButton
                      {...props}
                      accessibilityLabel={t("common:actions.viewDetail")}
                      icon="chevron-right"
                      onPress={() => openDetails(invoice)}
                    />
                  )}
                />
                <Card.Content style={styles.invoiceContent}>
                  <View style={styles.entityTagRow}>
                    <EntityTag label={invoice.storeName || invoice.storeCode || "--"} tone="store" />
                    <EntityTag label={invoice.supplierName || invoice.supplierCode || "--"} tone="supplier" />
                  </View>
                  <View style={styles.invoiceMeta}>
                    <Text variant="bodyMedium">{t("labels.orderDate")}: {formatDate(invoice.orderDate)}</Text>
                    <Text variant="bodyMedium">{t("labels.amount")}: {formatMoney(invoice.totalAmount)}</Text>
                    <Text variant="bodyMedium">{t("labels.receivedAmount")}: {formatMoney(invoice.receivedTotalAmount)}</Text>
                  </View>
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
              <View style={styles.entityTagRow}>
                <EntityTag label={selectedInvoice?.storeName || selectedInvoice?.storeCode || "--"} tone="store" />
                <EntityTag label={selectedInvoice?.supplierName || selectedInvoice?.supplierCode || "--"} tone="supplier" />
              </View>
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
                      <Text variant="labelSmall">{t("labels.noImage")}</Text>
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
        includeAllOption={!deviceBoundStoreCode}
        allLabel={t("filters.allStores")}
        renderAllLabel={(label) => <EntityTag label={label} tone="neutral" />}
        renderStoreLabel={(store) => (
          <EntityTag label={store.storeName || store.storeCode} tone="store" />
        )}
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
              <Pressable
                accessibilityRole="button"
                onPress={() => handleSelectSupplier()}
                style={styles.pickerRowButton}
              >
                <EntityTag label={t("filters.allSuppliers")} tone="neutral" />
              </Pressable>
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
                  <Pressable
                    accessibilityRole="button"
                    onPress={() => handleSelectSupplier(supplier.supplierCode)}
                    style={styles.pickerRowButton}
                  >
                    <EntityTag label={supplier.supplierName} tone="supplier" />
                  </Pressable>
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
          visible={dateRangeModalVisible}
          onDismiss={cancelDateRangeModal}
          contentContainerStyle={styles.dateModal}
        >
          <View style={styles.pickerModalHeader}>
            <Text variant="titleMedium">{t("filters.dateRangeTitle")}</Text>
            <Button onPress={cancelDateRangeModal}>
              {t("common:actions.cancel")}
            </Button>
          </View>
          <View style={styles.calendarHeader}>
            <IconButton
              icon="chevron-left"
              size={22}
              onPress={() => setDateRangeDisplayMonth((current) => addMonths(current, -1))}
            />
            <Text variant="titleMedium" style={styles.calendarMonthTitle}>
              {monthTitle}
            </Text>
            <IconButton
              icon="chevron-right"
              size={22}
              onPress={() => setDateRangeDisplayMonth((current) => addMonths(current, 1))}
            />
          </View>
          <View style={styles.weekdayRow}>
            {weekdayLabels.map((label, index) => (
              <Text key={`${label}-${index}`} variant="labelSmall" style={styles.weekdayText}>
                {label}
              </Text>
            ))}
          </View>
          <View style={styles.calendarGrid}>
            {calendarWeeks.map((week, weekIndex) => (
              <View key={weekIndex} style={styles.calendarWeekRow}>
                {week.map((cell) => {
                  const from = normalizeMonthDate(draftFilters.orderDateFrom);
                  const to = normalizeMonthDate(draftFilters.orderDateTo);
                  const isSelectedStart = Boolean(from && cell.dateString === from);
                  const isSelectedEnd = Boolean(to && cell.dateString === to);
                  const isSingleSelected = isSelectedStart && !to;
                  const isInRange = isDateInRange(cell.dateString, from, to);
                  const isToday = cell.dateString === today;

                  return (
                    <Pressable
                      key={cell.dateString}
                      accessibilityRole="button"
                      accessibilityState={{ selected: isSelectedStart || isSelectedEnd || isInRange }}
                      onPress={() => selectRangeDate(cell.dateString)}
                      style={({ pressed }) => [
                        styles.calendarDateCell,
                        !cell.isCurrentMonth ? styles.calendarDateCellOutside : null,
                        isInRange ? styles.calendarDateCellInRange : null,
                        isToday ? styles.calendarDateCellToday : null,
                        isSelectedStart || isSelectedEnd ? styles.calendarDateCellSelected : null,
                        isSingleSelected ? styles.calendarDateCellSingleSelected : null,
                        pressed ? styles.calendarDateCellPressed : null,
                      ]}
                    >
                      <Text
                        variant="labelLarge"
                        style={[
                          styles.calendarDateText,
                          !cell.isCurrentMonth ? styles.calendarDateTextOutside : null,
                          isSelectedStart || isSelectedEnd ? styles.calendarDateTextSelected : null,
                        ]}
                      >
                        {cell.date.getDate()}
                      </Text>
                    </Pressable>
                  );
                })}
              </View>
            ))}
          </View>
          <View style={styles.dateModalActions}>
            <Button
              icon="close-circle-outline"
              mode="text"
              onPress={clearDateRange}
            >
              {t("common:actions.clear")}
            </Button>
            <Button icon="check" mode="contained" onPress={applyDateRangeModal}>
              {t("filters.applyDateRange")}
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
  pickerTagRow: {
    alignItems: "flex-start",
    minWidth: 0,
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
  invoiceContent: {
    gap: 8,
  },
  invoiceMeta: {
    gap: 4,
  },
  entityTagRow: {
    alignItems: "center",
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 6,
  },
  entityTag: {
    alignSelf: "flex-start",
    borderRadius: 999,
    borderWidth: 1,
    maxWidth: "100%",
    paddingHorizontal: 10,
    paddingVertical: 4,
  },
  entityTagText: {
    fontWeight: "700",
  },
  storeTag: {
    backgroundColor: "#EAF2FF",
    borderColor: "#B8D4FF",
  },
  storeTagText: {
    color: "#175CD3",
  },
  supplierTag: {
    backgroundColor: "#E9F8EF",
    borderColor: "#ABEFC6",
  },
  supplierTagText: {
    color: "#067647",
  },
  neutralTag: {
    backgroundColor: "#F2F4F7",
    borderColor: "#D0D5DD",
  },
  neutralTagText: {
    color: "#475467",
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
    flexDirection: "row",
    justifyContent: "space-between",
    marginTop: 8,
  },
  calendarHeader: {
    alignItems: "center",
    flexDirection: "row",
    justifyContent: "space-between",
    marginBottom: 4,
  },
  calendarMonthTitle: {
    flex: 1,
    textAlign: "center",
  },
  weekdayRow: {
    flexDirection: "row",
    marginBottom: 8,
  },
  weekdayText: {
    color: "#667085",
    flex: 1,
    textAlign: "center",
  },
  calendarGrid: {
    gap: 6,
  },
  calendarWeekRow: {
    flexDirection: "row",
    gap: 6,
  },
  calendarDateCell: {
    alignItems: "center",
    aspectRatio: 1,
    backgroundColor: "#FFFFFF",
    borderColor: "#D0D5DD",
    borderRadius: 8,
    borderWidth: 1,
    flex: 1,
    justifyContent: "center",
  },
  calendarDateCellOutside: {
    backgroundColor: "#F8FAFC",
    borderColor: "#EAECF0",
  },
  calendarDateCellInRange: {
    backgroundColor: "#E8F1FF",
    borderColor: "#B2CCFF",
  },
  calendarDateCellToday: {
    borderColor: "#2563EB",
  },
  calendarDateCellSelected: {
    backgroundColor: "#2563EB",
    borderColor: "#2563EB",
  },
  calendarDateCellSingleSelected: {
    backgroundColor: "#2563EB",
  },
  calendarDateCellPressed: {
    opacity: 0.8,
  },
  calendarDateText: {
    color: "#101828",
  },
  calendarDateTextOutside: {
    color: "#98A2B3",
  },
  calendarDateTextSelected: {
    color: "#FFFFFF",
    fontWeight: "700",
  },
});

const ENTITY_TAG_STYLES = {
  neutral: {
    tag: styles.neutralTag,
    text: styles.neutralTagText,
  },
  store: {
    tag: styles.storeTag,
    text: styles.storeTagText,
  },
  supplier: {
    tag: styles.supplierTag,
    text: styles.supplierTagText,
  },
};
