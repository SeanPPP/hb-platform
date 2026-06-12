import { memo, useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  FlatList,
  Image,
  Pressable,
  RefreshControl,
  ScrollView,
  StyleSheet,
  type StyleProp,
  type ViewToken,
  type ViewStyle,
  View,
} from "react-native";
import { useQuery } from "@tanstack/react-query";
import {
  ActivityIndicator,
  Button,
  Card,
  Chip,
  Divider,
  IconButton,
  Portal,
  Modal,
  RadioButton,
  Snackbar,
  Text,
  TextInput,
} from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import { ProductBarcodeImage } from "@/components/product-maintenance/ProductBarcodeImage";
import { EmptyState } from "@/components/ui/EmptyState";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { resolveLocalizedErrorMessage } from "@/shared/i18n/error-message";
import { resolveLocaleTag } from "@/shared/i18n/types";
import {
  DEFAULT_ORDER_LIST_PAGE_SIZE,
  filterOrderDetailLinesByItemNumber,
  formatOrderDate,
  getOrderRowNumber,
} from "@/modules/orders/order-list-display";
import { buildOrderLineLabelPayload } from "@/modules/orders/order-label-payload";
import { fetchOrderDetail, fetchOrderList } from "@/modules/orders/store-order-api";
import {
  StoreOrderFlowStatus,
  type StoreOrderDetail,
  type StoreOrderDetailLine,
  type StoreOrderListItem,
} from "@/modules/orders/types";
import { printProductLabelPayload } from "@/modules/printer/api";
import { useStores } from "@/modules/shop/use-stores";

const HISTORY_STATUS_VALUES: StoreOrderFlowStatus[] = [
  StoreOrderFlowStatus.Submitted,
  StoreOrderFlowStatus.Completed,
  StoreOrderFlowStatus.Picking,
];
const PAGE_SIZE = DEFAULT_ORDER_LIST_PAGE_SIZE;

function formatDateTime(value: string | undefined, localeTag: string) {
  if (!value) {
    return "--";
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }

  return date.toLocaleString(localeTag, { hour12: false });
}

function formatNumber(value?: number, digits = 0) {
  if (value === undefined || value === null) {
    return "--";
  }

  return Number(value).toFixed(digits);
}

function formatMoney(value?: number) {
  return `$${formatNumber(value, 2)}`;
}

function StatusBadge({
  status,
  label,
}: {
  status?: StoreOrderFlowStatus;
  label: string;
}) {
  const toneMap: Record<string, { textColor: string; toneColor: string }> = {
    ShoppingCart: { textColor: "#5B5B5B", toneColor: "#ECECEC" },
    Submitted: { textColor: "#0958D9", toneColor: "#E6F4FF" },
    Completed: { textColor: "#237804", toneColor: "#F6FFED" },
    Picking: { textColor: "#AD6800", toneColor: "#FFF7E6" },
    Unknown: { textColor: "#5B5B5B", toneColor: "#ECECEC" },
  };

  const key =
    status === StoreOrderFlowStatus.ShoppingCart
      ? "ShoppingCart"
      : status === StoreOrderFlowStatus.Submitted
        ? "Submitted"
        : status === StoreOrderFlowStatus.Completed
          ? "Completed"
          : status === StoreOrderFlowStatus.Picking
            ? "Picking"
            : "Unknown";
  const meta = toneMap[key];

  return (
    <View style={[styles.statusBadge, { backgroundColor: meta.toneColor }]}>
      <Text variant="labelMedium" style={[styles.statusBadgeText, { color: meta.textColor }]}>
        {label}
      </Text>
    </View>
  );
}

function SummaryMetric({
  label,
  style,
  value,
}: {
  label: string;
  style?: StyleProp<ViewStyle>;
  value: string;
}) {
  return (
    <View style={[styles.summaryMetric, style]}>
      <Text variant="labelMedium" style={styles.summaryLabel} numberOfLines={1}>
        {label}
      </Text>
      <Text
        variant="titleMedium"
        style={styles.summaryValue}
        numberOfLines={1}
        adjustsFontSizeToFit
        minimumFontScale={0.72}
      >
        {value}
      </Text>
    </View>
  );
}

function OrderCardMetric({ label, value }: { label: string; value: string }) {
  return (
    <View style={styles.orderCardMetric}>
      <Text variant="labelMedium" style={styles.orderCardMetricLabel} numberOfLines={1}>
        {label}
      </Text>
      <Text variant="titleMedium" style={styles.orderCardMetricValue} numberOfLines={1}>
        {value}
      </Text>
    </View>
  );
}

const OrderLineCard = memo(function OrderLineCard({
  isPrinting,
  item,
  index,
  onPrint,
  renderMedia,
  t,
}: {
  isPrinting: boolean;
  item: StoreOrderDetailLine;
  index: number;
  onPrint: (item: StoreOrderDetailLine) => void;
  renderMedia: boolean;
  t: (key: string, options?: Record<string, unknown>) => string;
}) {
  return (
    <Card mode="outlined" style={styles.detailItemCard}>
      <Card.Content style={styles.detailItemContent}>
        <View style={styles.detailItemHeader}>
          {renderMedia && item.productImage ? (
            <Image source={{ uri: item.productImage }} style={styles.detailProductImage} resizeMode="cover" />
          ) : (
            <View style={[styles.detailProductImage, styles.detailProductImagePlaceholder]}>
              <Text variant="labelSmall" style={styles.detailProductImageText} numberOfLines={2}>
                {item.itemNumber || item.productCode}
              </Text>
            </View>
          )}
          <View style={styles.detailItemMain}>
            <View style={styles.detailItemTopRow}>
              <View style={styles.detailItemTitleWrap}>
                <Text variant="labelSmall" style={styles.detailItemIndex}>
                  #{index + 1}
                </Text>
                <Text variant="titleSmall" style={styles.detailItemTitle} numberOfLines={3}>
                  {item.productName || item.productCode}
                </Text>
                <Text variant="bodySmall" style={styles.detailItemSubTitle}>
                  {t("fields.itemNumber", { value: item.itemNumber || "--" })}
                </Text>
              </View>
              <View style={styles.detailItemStatusWrap}>
                <Text variant="bodyMedium" style={styles.detailQtyText}>
                  {t("fields.orderedQty", { value: formatNumber(item.quantity) })}
                </Text>
                <Text variant="bodySmall" style={styles.detailAllocText}>
                  {t("fields.allocQty", { value: formatNumber(item.allocQuantity) })}
                </Text>
              </View>
            </View>
          </View>
        </View>

        <View style={styles.detailMetaGrid}>
          <View style={styles.detailMetaCell}>
            <Text variant="labelSmall" style={styles.detailMetaLabel}>
              {t("fields.salesPrice")}
            </Text>
            <Text variant="bodyMedium">{formatMoney(item.price)}</Text>
          </View>
          <View style={styles.detailMetaCell}>
            <Text variant="labelSmall" style={styles.detailMetaLabel}>
              {t("fields.importPrice")}
            </Text>
            <Text variant="bodyMedium">{formatMoney(item.importPrice)}</Text>
          </View>
          <View style={styles.detailMetaCell}>
            <Text variant="labelSmall" style={styles.detailMetaLabel}>
              {t("fields.allocAmount")}
            </Text>
            <Text variant="bodyMedium">{formatMoney(item.importAmount)}</Text>
          </View>
        </View>
        {item.barcode ? (
          <View style={styles.detailBarcodeWrap}>
            {renderMedia ? <ProductBarcodeImage value={item.barcode} /> : <View style={styles.detailBarcodePlaceholder} />}
          </View>
        ) : null}
        <View style={styles.detailItemActions}>
          <Button
            compact
            disabled={isPrinting}
            icon="printer-outline"
            loading={isPrinting}
            mode="contained-tonal"
            onPress={() => onPrint(item)}
          >
            {t("actions.printLabel")}
          </Button>
        </View>
      </Card.Content>
    </Card>
  );
}, (prevProps, nextProps) => (
  prevProps.isPrinting === nextProps.isPrinting
  && prevProps.index === nextProps.index
  && prevProps.item === nextProps.item
  && prevProps.onPrint === nextProps.onPrint
  && prevProps.renderMedia === nextProps.renderMedia
  && prevProps.t === nextProps.t
));

function OrderDetailContent({
  detail,
  itemNumberFilter,
  loading,
  errorMessage,
  localeTag,
  onClose,
  onItemNumberFilterChange,
  onPrintLine,
  onRetry,
  printingDetailGuid,
  statusLabel,
  t,
}: {
  detail?: StoreOrderDetail;
  itemNumberFilter: string;
  loading: boolean;
  errorMessage?: string;
  localeTag: string;
  onClose: () => void;
  onItemNumberFilterChange: (value: string) => void;
  onPrintLine: (item: StoreOrderDetailLine) => void;
  onRetry: () => void;
  printingDetailGuid: string | null;
  statusLabel: (status?: StoreOrderFlowStatus) => string;
  t: (key: string, options?: Record<string, unknown>) => string;
}) {
  const filteredItems = useMemo(
    () => filterOrderDetailLinesByItemNumber(detail?.items ?? [], itemNumberFilter),
    [detail?.items, itemNumberFilter]
  );
  const [visibleDetailGuids, setVisibleDetailGuids] = useState<Set<string>>(() => new Set());
  const viewabilityConfig = useRef({ itemVisiblePercentThreshold: 10 });

  useEffect(() => {
    setVisibleDetailGuids(new Set());
  }, [detail?.orderGUID, itemNumberFilter]);

  const onViewableItemsChanged = useRef(
    ({ viewableItems }: { viewableItems: ViewToken[] }) => {
      const nextVisibleGuids = new Set<string>();

      for (const viewableItem of viewableItems) {
        const detailLine = viewableItem.item as StoreOrderDetailLine | undefined;

        if (detailLine?.detailGUID) {
          nextVisibleGuids.add(detailLine.detailGUID);
        }
      }

      setVisibleDetailGuids((previous) => {
        if (previous.size === nextVisibleGuids.size) {
          let changed = false;

          for (const detailGuid of nextVisibleGuids) {
            if (!previous.has(detailGuid)) {
              changed = true;
              break;
            }
          }

          if (!changed) {
            return previous;
          }
        }

        return nextVisibleGuids;
      });
    }
  );

  // 只给可见行挂载重媒体内容，减少图片和条码组件同时存在的数量。
  const renderDetailItem = useCallback(
    ({ item, index }: { item: StoreOrderDetailLine; index: number }) => (
      <OrderLineCard
        isPrinting={printingDetailGuid === item.detailGUID}
        item={item}
        index={index}
        onPrint={onPrintLine}
        renderMedia={visibleDetailGuids.has(item.detailGUID)}
        t={t}
      />
    ),
    [onPrintLine, printingDetailGuid, t, visibleDetailGuids]
  );

  const renderDetailHeader = useCallback(
    () => (
      <View style={styles.detailHeaderContent}>
        <Card mode="outlined" style={styles.detailSummaryCard}>
          <Card.Content style={styles.detailSummaryContent}>
            <View style={styles.detailTitleRow}>
              <View style={styles.detailTitleWrap}>
                <Text variant="titleLarge" style={styles.detailOrderNo}>
                  {detail?.orderNo || "--"}
                </Text>
                <Text variant="bodyMedium" style={styles.detailStoreText}>
                  {t("fields.store", { store: detail?.storeCode || "--" })}
                </Text>
              </View>
              <StatusBadge status={detail?.flowStatus} label={statusLabel(detail?.flowStatus)} />
            </View>

            <View style={styles.detailInfoBlock}>
              <Text variant="bodyMedium">
                {t("fields.orderedAt", { value: formatDateTime(detail?.orderDate, localeTag) })}
              </Text>
              <Text variant="bodyMedium">
                {t("fields.storeAddress", { value: detail?.storeAddress || "--" })}
              </Text>
              <Text variant="bodyMedium">{t("fields.remarks", { value: detail?.remarks || "--" })}</Text>
            </View>

            <ScrollView
              horizontal
              showsHorizontalScrollIndicator={false}
              contentContainerStyle={styles.summaryGrid}
            >
              <SummaryMetric style={styles.detailSummaryMetric} label={t("summary.sku")} value={formatNumber(detail?.totalSKU)} />
              <SummaryMetric style={styles.detailSummaryMetric} label={t("summary.orderedQty")} value={formatNumber(detail?.totalQuantity)} />
              <SummaryMetric style={styles.detailSummaryMetric} label={t("summary.allocQty")} value={formatNumber(detail?.totalAllocQuantity)} />
              <SummaryMetric style={styles.detailSummaryMetric} label={t("summary.orderAmount")} value={formatMoney(detail?.totalAmount)} />
              <SummaryMetric style={styles.detailSummaryMetric} label={t("summary.allocAmount")} value={formatMoney(detail?.totalImportAmount)} />
              <SummaryMetric style={styles.detailSummaryMetric} label={t("summary.orderVolume")} value={formatNumber(detail?.totalOrderVolume, 4)} />
            </ScrollView>
          </Card.Content>
        </Card>

        <View style={styles.detailListHeader}>
          <View style={styles.detailListTitleWrap}>
            <Text variant="titleMedium">{t("detailTitle")}</Text>
            <Text variant="bodySmall" style={styles.detailListHint}>
              {t("detailCount", { count: filteredItems.length })}
            </Text>
          </View>
          {itemNumberFilter.trim() ? (
            <Button compact onPress={() => onItemNumberFilterChange("")}>
              {t("filters.clearItemNumber")}
            </Button>
          ) : null}
        </View>

        <TextInput
          dense
          mode="outlined"
          label={t("filters.itemNumber")}
          placeholder={t("filters.itemNumberPlaceholder")}
          value={itemNumberFilter}
          onChangeText={onItemNumberFilterChange}
          autoCapitalize="none"
          autoCorrect={false}
          left={<TextInput.Icon icon="magnify" />}
          right={
            itemNumberFilter ? (
              <TextInput.Icon icon="close" onPress={() => onItemNumberFilterChange("")} />
            ) : undefined
          }
          style={styles.detailFilterInput}
        />
      </View>
    ),
    [detail, filteredItems.length, itemNumberFilter, localeTag, onItemNumberFilterChange, statusLabel, t]
  );

  const renderDetailEmpty = useCallback(
    () => (itemNumberFilter.trim() ? (
      <EmptyState
        title={t("empty.noMatchingLinesTitle")}
        description={t("empty.noMatchingLinesDescription")}
      />
    ) : (
      <EmptyState title={t("empty.noLinesTitle")} description={t("empty.noLinesDescription")} />
    )),
    [itemNumberFilter, t]
  );
  const detailListExtraData = useMemo(
    () => ({
      printingDetailGuid,
      visibleDetailGuids,
    }),
    [printingDetailGuid, visibleDetailGuids]
  );

  if (loading) {
    return (
      <View style={styles.detailLoadingWrap}>
        <ActivityIndicator animating size="large" color="#1677FF" />
      </View>
    );
  }

  if (errorMessage) {
    return (
      <EmptyState
        title={t("empty.detailFailedTitle")}
        description={errorMessage}
        primaryAction={{ label: t("common:actions.retry"), icon: "refresh", onPress: onRetry }}
        secondaryAction={{ label: t("common:actions.close"), icon: "close", onPress: onClose }}
      />
    );
  }

  if (!detail) {
    return (
      <EmptyState
        title={t("empty.detailNotFoundTitle")}
        description={t("empty.detailNotFoundDescription")}
        primaryAction={{ label: t("common:actions.retry"), icon: "refresh", onPress: onRetry }}
        secondaryAction={{ label: t("common:actions.close"), icon: "close", onPress: onClose }}
      />
    );
  }

  return (
    <FlatList
      data={filteredItems}
      keyExtractor={(item) => item.detailGUID}
      renderItem={renderDetailItem}
      extraData={detailListExtraData}
      contentContainerStyle={[
        styles.detailListContent,
        filteredItems.length ? null : styles.detailListContentEmpty,
      ]}
      ListHeaderComponent={renderDetailHeader}
      ListEmptyComponent={renderDetailEmpty}
      ItemSeparatorComponent={DetailItemSeparator}
      initialNumToRender={4}
      maxToRenderPerBatch={4}
      windowSize={5}
      removeClippedSubviews
      keyboardShouldPersistTaps="handled"
      onViewableItemsChanged={onViewableItemsChanged.current}
      viewabilityConfig={viewabilityConfig.current}
    />
  );
}

function DetailItemSeparator() {
  return <View style={styles.detailItemSeparator} />;
}

export default function Orders() {
  const { t, language } = useAppTranslation(["orders", "common"]);
  const getErrorMessage = useCallback((error: unknown, fallbackKey: string) => (
    resolveLocalizedErrorMessage(error, {
      language,
      t,
      fallbackKey,
    })
  ), [language, t]);
  const localeTag = resolveLocaleTag(language);
  const { stores, selectedStore, selectedStoreCode, selectStore, isLoading: storesLoading } = useStores();
  const [selectedStatus, setSelectedStatus] = useState<"all" | StoreOrderFlowStatus>("all");
  const [pageNumber, setPageNumber] = useState(1);
  const [selectedOrderGuid, setSelectedOrderGuid] = useState<string | null>(null);
  const [filtersVisible, setFiltersVisible] = useState(false);
  const [storePickerVisible, setStorePickerVisible] = useState(false);
  const [itemNumberFilter, setItemNumberFilter] = useState("");
  const [ordersRefreshing, setOrdersRefreshing] = useState(false);
  const [printingDetailGuid, setPrintingDetailGuid] = useState<string | null>(null);
  const [snackbar, setSnackbar] = useState("");

  const statusLabel = useCallback(
    (status?: StoreOrderFlowStatus) => {
      switch (status) {
        case StoreOrderFlowStatus.ShoppingCart:
          return t("statuses.shoppingCart");
        case StoreOrderFlowStatus.Submitted:
          return t("statuses.submitted");
        case StoreOrderFlowStatus.Completed:
          return t("statuses.completed");
        case StoreOrderFlowStatus.Picking:
          return t("statuses.picking");
        default:
          return t("statuses.unknown");
      }
    },
    [t]
  );

  const statusList = useMemo(
    () => (selectedStatus === "all" ? HISTORY_STATUS_VALUES : [selectedStatus]),
    [selectedStatus]
  );

  useEffect(() => {
    setPageNumber(1);
  }, [selectedStatus, selectedStoreCode]);

  const ordersQuery = useQuery({
    queryKey: ["storeOrders", selectedStoreCode, statusList.join(","), pageNumber],
    enabled: Boolean(selectedStoreCode),
    queryFn: () =>
      fetchOrderList({
        storeCode: selectedStoreCode ?? undefined,
        pageNumber,
        pageSize: PAGE_SIZE,
        statusList,
      }),
  });

  const detailQuery = useQuery({
    queryKey: ["storeOrderDetail", selectedOrderGuid],
    enabled: Boolean(selectedOrderGuid),
    queryFn: () => fetchOrderDetail(selectedOrderGuid!),
  });

  const refetchOrders = ordersQuery.refetch;
  const handleRefreshOrders = useCallback(async () => {
    if (!selectedStoreCode) {
      return;
    }

    setOrdersRefreshing(true);
    try {
      await refetchOrders();
    } finally {
      setOrdersRefreshing(false);
    }
  }, [refetchOrders, selectedStoreCode]);

  const orderItems = ordersQuery.data?.items ?? [];
  const total = ordersQuery.data?.total ?? 0;
  const canGoPrevPage = pageNumber > 1;
  const canGoNextPage = pageNumber * PAGE_SIZE < total;

  const handleCloseDetail = useCallback(() => {
    setSelectedOrderGuid(null);
    setItemNumberFilter("");
  }, []);

  const handleSelectOrder = useCallback((orderGuid: string) => {
    setItemNumberFilter("");
    setSelectedOrderGuid(orderGuid);
  }, []);

  const handlePrintLine = useCallback(
    async (item: StoreOrderDetailLine) => {
      setPrintingDetailGuid(item.detailGUID);
      try {
        // 订单明细中的 Print label 统一走普通商品标签模板。
        await printProductLabelPayload(buildOrderLineLabelPayload(item));
        setSnackbar(t("messages.printSuccess"));
      } catch (error) {
        setSnackbar(getErrorMessage(error, "messages.printFailed"));
      } finally {
        setPrintingDetailGuid(null);
      }
    },
    [getErrorMessage, t]
  );
  const handlePrintDetailLine = useCallback(
    (item: StoreOrderDetailLine) => void handlePrintLine(item),
    [handlePrintLine]
  );

  const renderOrderCard = ({ item, index }: { item: StoreOrderListItem; index: number }) => (
    <Pressable onPress={() => handleSelectOrder(item.orderGUID)}>
      <Card mode="outlined" style={styles.orderCard}>
        <Card.Content style={styles.orderCardContent}>
          <View style={styles.orderHeader}>
            <View style={styles.orderHeaderLeft}>
              <View style={styles.orderNoRow}>
                <Text variant="labelMedium" style={styles.orderRowNumber}>
                  #{getOrderRowNumber(pageNumber, PAGE_SIZE, index)}
                </Text>
                <Text variant="titleMedium" style={styles.orderNo}>
                  {item.orderNo || "--"}
                </Text>
              </View>
              <View style={styles.orderMetaRow}>
                <Text variant="bodySmall" style={styles.orderStoreText}>
                  {item.storeName || item.storeCode || "--"}
                </Text>
              </View>
              <View style={styles.orderDateGrid}>
                <View style={styles.orderDateItem}>
                  <Text variant="labelSmall" style={styles.orderDateLabel}>
                    {t("fields.orderDate")}
                  </Text>
                  <Text variant="bodySmall" style={styles.orderDateText}>
                    {formatOrderDate(item.orderDate, localeTag)}
                  </Text>
                </View>
                <View style={styles.orderDateItem}>
                  <Text variant="labelSmall" style={styles.orderDateLabel}>
                    {t("fields.outboundDate")}
                  </Text>
                  <Text variant="bodySmall" style={styles.orderDateText}>
                    {formatOrderDate(item.outboundDate, localeTag)}
                  </Text>
                </View>
              </View>
            </View>
            <StatusBadge status={item.flowStatus} label={statusLabel(item.flowStatus)} />
          </View>

          <View style={styles.orderSummaryRow}>
            <OrderCardMetric label={t("summary.orderedQty")} value={formatNumber(item.totalQuantity)} />
            <OrderCardMetric label={t("summary.allocQty")} value={formatNumber(item.totalAllocQuantity)} />
            <OrderCardMetric label={t("summary.orderAmount")} value={formatMoney(item.totalOrderAmount)} />
          </View>

          <Divider style={styles.orderDivider} />

          <View style={styles.orderFooter}>
            <Text variant="bodySmall" style={styles.orderFooterText}>
              {t("summary.allocAmount")} {formatMoney(item.importTotalAmount)}
            </Text>
            <Text variant="bodySmall" style={styles.orderFooterLink}>
              {t("viewDetail")}
            </Text>
          </View>
        </Card.Content>
      </Card>
    </Pressable>
  );

  return (
    <SafeAreaView edges={["top", "left", "right"]} style={styles.container}>
      <View style={styles.headerRow}>
        <Text variant="titleLarge" style={styles.pageTitle}>
          {t("title")}
        </Text>
        <IconButton
          icon="filter-variant"
          size={20}
          mode="contained-tonal"
          onPress={() => setFiltersVisible(true)}
          style={styles.filterButton}
        />
      </View>

      {!selectedStoreCode && !storesLoading ? (
        <EmptyState title={t("empty.selectStoreTitle")} description={t("empty.selectStoreDescription")} />
      ) : (
        <FlatList
          data={orderItems}
          keyExtractor={(item) => item.orderGUID}
          renderItem={renderOrderCard}
          contentContainerStyle={styles.listContent}
          refreshControl={
            <RefreshControl refreshing={ordersRefreshing} onRefresh={() => void handleRefreshOrders()} />
          }
          ListHeaderComponent={
            <View style={styles.listHeader}>
              <Text variant="titleSmall" style={styles.listHeaderTitle}>
                {selectedStore?.storeName || t("listTitle")}
              </Text>
              <Text variant="bodySmall" style={styles.listHeaderMeta}>
                {t("summary.total", { count: total })}
              </Text>
            </View>
          }
          ListEmptyComponent={
            ordersQuery.isLoading ? (
              <View style={styles.loadingWrap}>
                <ActivityIndicator animating size="large" color="#1677FF" />
              </View>
            ) : ordersQuery.isError ? (
              <EmptyState
                title={t("empty.listFailedTitle")}
                description={resolveLocalizedErrorMessage(ordersQuery.error, {
                  t,
                  language,
                  fallbackKey: "empty.noHistoryDescription",
                })}
                primaryAction={{
                  label: t("common:actions.retry"),
                  icon: "refresh",
                  onPress: () => void handleRefreshOrders(),
                }}
              />
            ) : (
              <EmptyState
                title={selectedStoreCode ? t("empty.noHistoryTitle") : t("empty.noAccessTitle")}
                description={t("empty.noHistoryDescription")}
              />
            )
          }
          ListFooterComponent={
            total ? (
              <View style={styles.paginationWrap}>
                <Button mode="outlined" disabled={!canGoPrevPage} onPress={() => setPageNumber((value) => value - 1)}>
                  {t("pagination.previous")}
                </Button>
                <Text variant="bodyMedium" style={styles.pageNumberText}>
                  {t("pagination.page", { page: pageNumber })}
                </Text>
                <Button mode="outlined" disabled={!canGoNextPage} onPress={() => setPageNumber((value) => value + 1)}>
                  {t("pagination.next")}
                </Button>
              </View>
            ) : null
          }
        />
      )}

      <Portal>
        <Modal
          visible={filtersVisible}
          onDismiss={() => setFiltersVisible(false)}
          contentContainerStyle={styles.filtersModalContent}
        >
          <ScrollView contentContainerStyle={styles.filtersModalScroll}>
            <View style={styles.filtersModalHeader}>
              <Text variant="titleMedium" style={styles.filtersModalTitle}>
                {t("filterTitle")}
              </Text>
              <Button compact onPress={() => setFiltersVisible(false)}>
                {t("common:actions.close")}
              </Button>
            </View>

            <View style={styles.filtersSection}>
              <Text variant="labelLarge" style={styles.filtersSectionTitle}>
                {t("filters.store")}
              </Text>
              <Button
                mode="outlined"
                icon="storefront-outline"
                contentStyle={styles.storeSelectorButtonContent}
                style={styles.storeSelectorButton}
                onPress={() => setStorePickerVisible(true)}
              >
                {selectedStore?.storeName || t("selectStore")}
              </Button>
              <Text variant="bodySmall" style={styles.filtersCurrentText}>
                {t("filters.currentStore", { store: selectedStore?.storeName || t("common:na") })}
              </Text>
            </View>

            <View style={styles.filtersSection}>
              <Text variant="labelLarge" style={styles.filtersSectionTitle}>
                {t("filters.status")}
              </Text>
              <View style={styles.filterChipsGrid}>
                <Chip
                  selected={selectedStatus === "all"}
                  mode={selectedStatus === "all" ? "flat" : "outlined"}
                  onPress={() => setSelectedStatus("all")}
                  style={styles.filterChip}
                >
                  {t("filters.allHistory")}
                </Chip>
                {HISTORY_STATUS_VALUES.map((status) => (
                  <Chip
                    key={status}
                    selected={selectedStatus === status}
                    mode={selectedStatus === status ? "flat" : "outlined"}
                    onPress={() => setSelectedStatus(status)}
                    style={styles.filterChip}
                  >
                    {statusLabel(status)}
                  </Chip>
                ))}
              </View>
            </View>
          </ScrollView>
        </Modal>
        <Modal
          visible={storePickerVisible}
          onDismiss={() => setStorePickerVisible(false)}
          contentContainerStyle={styles.storePickerModalContent}
        >
          <View style={styles.storePickerHeader}>
            <View style={styles.storePickerTitleWrap}>
              <Text variant="titleMedium" style={styles.filtersModalTitle}>
                {t("filters.chooseStore")}
              </Text>
              <Text variant="bodySmall" style={styles.filtersCurrentText}>
                {t("filters.currentStore", { store: selectedStore?.storeName || t("common:na") })}
              </Text>
            </View>
            <Button compact onPress={() => setStorePickerVisible(false)}>
              {t("common:actions.close")}
            </Button>
          </View>

          {storesLoading ? (
            <View style={styles.storePickerLoading}>
              <ActivityIndicator animating color="#1677FF" />
              <Text variant="bodyMedium" style={styles.filtersCurrentText}>
                {t("common:loading")}
              </Text>
            </View>
          ) : stores.length ? (
            <FlatList
              data={stores}
              keyExtractor={(store) => store.storeCode}
              contentContainerStyle={styles.storePickerListContent}
              renderItem={({ item: store }) => {
                const selected = store.storeCode === selectedStoreCode;

                return (
                  <Pressable
                    style={[styles.storePickerRow, selected ? styles.storePickerRowSelected : null]}
                    onPress={() => {
                      void selectStore(store);
                      setStorePickerVisible(false);
                    }}
                  >
                    <RadioButton
                      value={store.storeCode}
                      status={selected ? "checked" : "unchecked"}
                      onPress={() => {
                        void selectStore(store);
                        setStorePickerVisible(false);
                      }}
                    />
                    <View style={styles.storePickerRowTextWrap}>
                      <Text variant="bodyMedium" style={styles.storePickerStoreName}>
                        {store.storeName || store.storeCode}
                      </Text>
                      <Text variant="bodySmall" style={styles.filtersCurrentText}>
                        {store.storeCode}
                      </Text>
                    </View>
                  </Pressable>
                );
              }}
            />
          ) : (
            <Text variant="bodyMedium" style={styles.filtersCurrentText}>
              {t("filters.noStores")}
            </Text>
          )}
        </Modal>
        <Modal
          visible={Boolean(selectedOrderGuid)}
          onDismiss={handleCloseDetail}
          contentContainerStyle={styles.modalContent}
        >
          <OrderDetailContent
            detail={detailQuery.data}
            itemNumberFilter={itemNumberFilter}
            loading={!detailQuery.data && (detailQuery.isLoading || detailQuery.isFetching)}
            errorMessage={
              detailQuery.error
                ? getErrorMessage(detailQuery.error, "empty.detailFailedDescription")
                : undefined
            }
            localeTag={localeTag}
            onClose={handleCloseDetail}
            onItemNumberFilterChange={setItemNumberFilter}
            onPrintLine={handlePrintDetailLine}
            onRetry={() => void detailQuery.refetch()}
            printingDetailGuid={printingDetailGuid}
            statusLabel={statusLabel}
            t={t}
          />
        </Modal>
      </Portal>
      <Snackbar visible={Boolean(snackbar)} onDismiss={() => setSnackbar("")} duration={3000}>
        {snackbar}
      </Snackbar>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: "#F6F8FB",
  },
  headerRow: {
    paddingHorizontal: 16,
    paddingTop: 0,
    paddingBottom: 0,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
  },
  pageTitle: {
    color: "#0F172A",
    fontWeight: "700",
  },
  filterButton: {
    margin: 0,
  },
  filterChip: {
    backgroundColor: "#FFFFFF",
  },
  storeSelectorButton: {
    alignSelf: "stretch",
  },
  storeSelectorButtonContent: {
    flexDirection: "row-reverse",
    justifyContent: "space-between",
  },
  filtersCurrentText: {
    color: "#64748B",
  },
  listContent: {
    paddingHorizontal: 16,
    paddingTop: 0,
    paddingBottom: 0,
    gap: 12,
    flexGrow: 1,
  },
  listHeader: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    marginBottom: 2,
  },
  listHeaderTitle: {
    color: "#0F172A",
    fontWeight: "600",
  },
  listHeaderMeta: {
    color: "#64748B",
  },
  loadingWrap: {
    paddingVertical: 48,
    alignItems: "center",
    justifyContent: "center",
  },
  orderCard: {
    backgroundColor: "#FFFFFF",
    borderRadius: 18,
  },
  orderCardContent: {
    gap: 14,
  },
  orderHeader: {
    flexDirection: "row",
    justifyContent: "space-between",
    gap: 12,
  },
  orderHeaderLeft: {
    flex: 1,
    gap: 4,
  },
  orderNoRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: 8,
  },
  orderRowNumber: {
    color: "#1677FF",
    fontWeight: "700",
  },
  orderNo: {
    flex: 1,
    color: "#0F172A",
    fontWeight: "700",
  },
  orderMetaRow: {
    flexDirection: "row",
    alignItems: "center",
    flexWrap: "wrap",
    columnGap: 6,
    rowGap: 2,
  },
  orderStoreText: {
    color: "#64748B",
  },
  orderDateGrid: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 10,
  },
  orderDateItem: {
    flexDirection: "row",
    alignItems: "center",
    gap: 4,
  },
  orderDateLabel: {
    color: "#94A3B8",
    fontWeight: "700",
  },
  orderDateText: {
    color: "#B45309",
    fontWeight: "700",
  },
  statusBadge: {
    alignSelf: "flex-start",
    paddingHorizontal: 10,
    paddingVertical: 6,
    borderRadius: 999,
  },
  statusBadgeText: {
    fontWeight: "700",
  },
  orderSummaryRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    gap: 10,
  },
  orderCardMetric: {
    flex: 1,
    minWidth: 0,
    gap: 6,
  },
  orderCardMetricLabel: {
    color: "#94A3B8",
    fontWeight: "700",
    lineHeight: 18,
  },
  orderCardMetricValue: {
    color: "#0F172A",
    fontSize: 20,
    fontWeight: "700",
    lineHeight: 26,
  },
  summaryMetric: {
    flex: 1,
    gap: 4,
  },
  summaryLabel: {
    color: "#94A3B8",
  },
  summaryValue: {
    color: "#0F172A",
    fontWeight: "700",
  },
  orderDivider: {
    backgroundColor: "#E2E8F0",
  },
  orderFooter: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    gap: 12,
  },
  orderFooterText: {
    color: "#475569",
  },
  orderFooterLink: {
    color: "#1677FF",
    fontWeight: "700",
  },
  paginationWrap: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 12,
    marginTop: 2,
  },
  pageNumberText: {
    color: "#475569",
  },
  filtersModalContent: {
    marginHorizontal: 16,
    marginVertical: 84,
    backgroundColor: "#FFFFFF",
    borderRadius: 20,
    overflow: "hidden",
  },
  filtersModalScroll: {
    padding: 16,
    gap: 16,
  },
  filtersModalHeader: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
  },
  filtersModalTitle: {
    color: "#0F172A",
    fontWeight: "700",
  },
  filtersSection: {
    gap: 10,
  },
  filtersSectionTitle: {
    color: "#475569",
  },
  storePickerModalContent: {
    marginHorizontal: 16,
    marginVertical: 84,
    backgroundColor: "#FFFFFF",
    borderRadius: 20,
    maxHeight: "78%",
    overflow: "hidden",
    padding: 16,
    gap: 14,
  },
  storePickerHeader: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 12,
  },
  storePickerTitleWrap: {
    flex: 1,
    gap: 4,
  },
  storePickerLoading: {
    minHeight: 160,
    alignItems: "center",
    justifyContent: "center",
    gap: 10,
  },
  storePickerListContent: {
    gap: 8,
    paddingBottom: 4,
  },
  storePickerRow: {
    minHeight: 56,
    borderRadius: 14,
    borderWidth: 1,
    borderColor: "#E2E8F0",
    backgroundColor: "#FFFFFF",
    flexDirection: "row",
    alignItems: "center",
    gap: 4,
    paddingRight: 12,
  },
  storePickerRowSelected: {
    borderColor: "#1677FF",
    backgroundColor: "#EFF6FF",
  },
  storePickerRowTextWrap: {
    flex: 1,
    minWidth: 0,
    gap: 2,
  },
  storePickerStoreName: {
    color: "#0F172A",
    fontWeight: "700",
  },
  filterChipsGrid: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
  },
  modalContent: {
    flex: 1,
    marginVertical: 36,
    marginHorizontal: 12,
    backgroundColor: "#FFFFFF",
    borderRadius: 24,
    overflow: "hidden",
  },
  detailLoadingWrap: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
    minHeight: 240,
  },
  detailListContent: {
    padding: 16,
    paddingBottom: 24,
  },
  detailListContentEmpty: {
    flexGrow: 1,
  },
  detailHeaderContent: {
    gap: 14,
    marginBottom: 14,
  },
  detailSummaryCard: {
    backgroundColor: "#F8FAFC",
    borderRadius: 20,
  },
  detailSummaryContent: {
    gap: 10,
  },
  detailTitleRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    gap: 12,
  },
  detailTitleWrap: {
    flex: 1,
    gap: 4,
  },
  detailOrderNo: {
    color: "#0F172A",
    fontWeight: "700",
  },
  detailStoreText: {
    color: "#475569",
  },
  detailInfoBlock: {
    gap: 4,
  },
  summaryGrid: {
    flexDirection: "row",
    gap: 8,
    paddingRight: 4,
  },
  detailSummaryMetric: {
    flex: 0,
    width: 112,
    borderRadius: 10,
    backgroundColor: "#FFFFFF",
    paddingHorizontal: 9,
    paddingVertical: 7,
  },
  detailListHeader: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 12,
  },
  detailListTitleWrap: {
    flex: 1,
    minWidth: 0,
  },
  detailListHint: {
    color: "#64748B",
  },
  detailFilterInput: {
    backgroundColor: "#FFFFFF",
  },
  detailItemCard: {
    backgroundColor: "#FFFFFF",
    borderRadius: 18,
  },
  detailItemSeparator: {
    height: 14,
  },
  detailItemContent: {
    gap: 12,
  },
  detailItemHeader: {
    flexDirection: "row",
    gap: 12,
  },
  detailProductImage: {
    width: 72,
    height: 72,
    borderRadius: 10,
    backgroundColor: "#F1F5F9",
  },
  detailProductImagePlaceholder: {
    alignItems: "center",
    justifyContent: "center",
    padding: 6,
    borderWidth: 1,
    borderColor: "#E2E8F0",
  },
  detailProductImageText: {
    color: "#64748B",
    textAlign: "center",
  },
  detailItemMain: {
    flex: 1,
    minWidth: 0,
  },
  detailItemTopRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    gap: 10,
  },
  detailItemTitleWrap: {
    flex: 1,
    minWidth: 0,
    gap: 4,
  },
  detailItemIndex: {
    color: "#1677FF",
    fontWeight: "700",
  },
  detailItemTitle: {
    color: "#0F172A",
    fontWeight: "700",
  },
  detailItemSubTitle: {
    color: "#64748B",
  },
  detailItemStatusWrap: {
    alignItems: "flex-end",
    justifyContent: "center",
    gap: 2,
  },
  detailQtyText: {
    color: "#0F172A",
    fontWeight: "700",
  },
  detailAllocText: {
    color: "#64748B",
  },
  detailMetaGrid: {
    flexDirection: "row",
    flexWrap: "wrap",
    rowGap: 10,
    columnGap: 12,
  },
  detailBarcodeWrap: {
    alignSelf: "stretch",
    height: 48,
    maxWidth: 220,
  },
  detailBarcodePlaceholder: {
    flex: 1,
    borderRadius: 8,
    backgroundColor: "#F8FAFC",
    borderWidth: 1,
    borderColor: "#E2E8F0",
  },
  detailItemActions: {
    alignItems: "flex-end",
  },
  detailMetaCell: {
    width: "47%",
    gap: 4,
  },
  detailMetaLabel: {
    color: "#94A3B8",
  },
});
