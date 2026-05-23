import { useCallback, useEffect, useMemo, useState } from "react";
import {
  FlatList,
  Pressable,
  RefreshControl,
  ScrollView,
  StyleSheet,
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
  Text,
} from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import { EmptyState } from "@/components/ui/EmptyState";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { resolveLocaleTag } from "@/shared/i18n/types";
import { fetchOrderDetail, fetchOrderList } from "@/modules/orders/store-order-api";
import {
  StoreOrderFlowStatus,
  type StoreOrderDetail,
  type StoreOrderDetailLine,
  type StoreOrderListItem,
} from "@/modules/orders/types";
import { useStores } from "@/modules/shop/use-stores";

const HISTORY_STATUS_VALUES: StoreOrderFlowStatus[] = [
  StoreOrderFlowStatus.Submitted,
  StoreOrderFlowStatus.Completed,
  StoreOrderFlowStatus.Picking,
];
const PAGE_SIZE = 20;

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

function OrderLineCard({
  item,
  index,
  t,
}: {
  item: StoreOrderDetailLine;
  index: number;
  t: (key: string, options?: Record<string, unknown>) => string;
}) {
  return (
    <Card mode="outlined" style={styles.detailItemCard}>
      <Card.Content style={styles.detailItemContent}>
        <View style={styles.detailItemHeader}>
          <View style={styles.detailItemTitleWrap}>
            <Text variant="labelSmall" style={styles.detailItemIndex}>
              #{index + 1}
            </Text>
            <Text variant="titleSmall" style={styles.detailItemTitle}>
              {item.productName || item.productCode}
            </Text>
            <Text variant="bodySmall" style={styles.detailItemSubTitle}>
              {t("fields.itemNumber", { value: item.itemNumber || "--" })} |{" "}
              {t("fields.location", { value: item.locationCode || "--" })}
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
              {t("fields.orderAmount")}
            </Text>
            <Text variant="bodyMedium">{formatMoney(item.amount)}</Text>
          </View>
          <View style={styles.detailMetaCell}>
            <Text variant="labelSmall" style={styles.detailMetaLabel}>
              {t("fields.allocAmount")}
            </Text>
            <Text variant="bodyMedium">{formatMoney(item.importAmount)}</Text>
          </View>
        </View>
      </Card.Content>
    </Card>
  );
}

function OrderDetailContent({
  detail,
  loading,
  errorMessage,
  localeTag,
  onClose,
  onRetry,
  statusLabel,
  t,
}: {
  detail?: StoreOrderDetail;
  loading: boolean;
  errorMessage?: string;
  localeTag: string;
  onClose: () => void;
  onRetry: () => void;
  statusLabel: (status?: StoreOrderFlowStatus) => string;
  t: (key: string, options?: Record<string, unknown>) => string;
}) {
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
    <ScrollView contentContainerStyle={styles.detailScrollContent}>
      <Card mode="outlined" style={styles.detailSummaryCard}>
        <Card.Content style={styles.detailSummaryContent}>
          <View style={styles.detailTitleRow}>
            <View style={styles.detailTitleWrap}>
              <Text variant="titleLarge" style={styles.detailOrderNo}>
                {detail.orderNo || "--"}
              </Text>
              <Text variant="bodyMedium" style={styles.detailStoreText}>
                {t("fields.store", { store: detail.storeCode || "--" })}
              </Text>
            </View>
            <StatusBadge status={detail.flowStatus} label={statusLabel(detail.flowStatus)} />
          </View>

          <View style={styles.detailInfoBlock}>
            <Text variant="bodyMedium">
              {t("fields.orderedAt", { value: formatDateTime(detail.orderDate, localeTag) })}
            </Text>
            <Text variant="bodyMedium">
              {t("fields.storeAddress", { value: detail.storeAddress || "--" })}
            </Text>
            <Text variant="bodyMedium">{t("fields.remarks", { value: detail.remarks || "--" })}</Text>
          </View>

          <View style={styles.summaryGrid}>
            <SummaryMetric label={t("summary.sku")} value={formatNumber(detail.totalSKU)} />
            <SummaryMetric label={t("summary.orderedQty")} value={formatNumber(detail.totalQuantity)} />
            <SummaryMetric label={t("summary.allocQty")} value={formatNumber(detail.totalAllocQuantity)} />
            <SummaryMetric label={t("summary.orderAmount")} value={formatMoney(detail.totalAmount)} />
            <SummaryMetric label={t("summary.allocAmount")} value={formatMoney(detail.totalImportAmount)} />
            <SummaryMetric label={t("summary.orderVolume")} value={formatNumber(detail.totalOrderVolume, 4)} />
          </View>
        </Card.Content>
      </Card>

      <View style={styles.detailListHeader}>
        <Text variant="titleMedium">{t("detailTitle")}</Text>
        <Text variant="bodySmall" style={styles.detailListHint}>
          {t("detailCount", { count: detail.items.length })}
        </Text>
      </View>

      {detail.items.length ? (
        detail.items.map((item, index) => (
          <OrderLineCard key={item.detailGUID} item={item} index={index} t={t} />
        ))
      ) : (
        <EmptyState title={t("empty.noLinesTitle")} description={t("empty.noLinesDescription")} />
      )}
    </ScrollView>
  );
}

export default function Orders() {
  const { t, language } = useAppTranslation(["orders", "common"]);
  const localeTag = resolveLocaleTag(language);
  const { stores, selectedStore, selectedStoreCode, selectStore, isLoading: storesLoading } = useStores();
  const [selectedStatus, setSelectedStatus] = useState<"all" | StoreOrderFlowStatus>("all");
  const [pageNumber, setPageNumber] = useState(1);
  const [selectedOrderGuid, setSelectedOrderGuid] = useState<string | null>(null);
  const [filtersVisible, setFiltersVisible] = useState(false);
  const [storePickerVisible, setStorePickerVisible] = useState(false);
  const [ordersRefreshing, setOrdersRefreshing] = useState(false);

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

  const renderOrderCard = ({ item }: { item: StoreOrderListItem }) => (
    <Pressable onPress={() => setSelectedOrderGuid(item.orderGUID)}>
      <Card mode="outlined" style={styles.orderCard}>
        <Card.Content style={styles.orderCardContent}>
          <View style={styles.orderHeader}>
            <View style={styles.orderHeaderLeft}>
              <Text variant="titleMedium" style={styles.orderNo}>
                {item.orderNo || "--"}
              </Text>
              <Text variant="bodySmall" style={styles.orderMetaText}>
                {item.storeName || item.storeCode || "--"} | {formatDateTime(item.orderDate, localeTag)}
              </Text>
            </View>
            <StatusBadge status={item.flowStatus} label={statusLabel(item.flowStatus)} />
          </View>

          <View style={styles.orderSummaryRow}>
            <SummaryMetric label={t("summary.orderedQty")} value={formatNumber(item.totalQuantity)} />
            <SummaryMetric label={t("summary.allocQty")} value={formatNumber(item.totalAllocQuantity)} />
            <SummaryMetric label={t("summary.orderAmount")} value={formatMoney(item.totalOrderAmount)} />
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
                description={ordersQuery.error instanceof Error ? ordersQuery.error.message : t("empty.noHistoryDescription")}
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
          onDismiss={() => setSelectedOrderGuid(null)}
          contentContainerStyle={styles.modalContent}
        >
          <OrderDetailContent
            detail={detailQuery.data}
            loading={!detailQuery.data && (detailQuery.isLoading || detailQuery.isFetching)}
            errorMessage={detailQuery.error instanceof Error ? detailQuery.error.message : undefined}
            localeTag={localeTag}
            onClose={() => setSelectedOrderGuid(null)}
            onRetry={() => void detailQuery.refetch()}
            statusLabel={statusLabel}
            t={t}
          />
        </Modal>
      </Portal>
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
  orderNo: {
    color: "#0F172A",
    fontWeight: "700",
  },
  orderMetaText: {
    color: "#64748B",
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
  detailScrollContent: {
    padding: 16,
    gap: 14,
  },
  detailSummaryCard: {
    backgroundColor: "#F8FAFC",
    borderRadius: 20,
  },
  detailSummaryContent: {
    gap: 14,
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
    gap: 6,
  },
  summaryGrid: {
    flexDirection: "row",
    flexWrap: "wrap",
    rowGap: 12,
    columnGap: 12,
  },
  detailListHeader: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
  },
  detailListHint: {
    color: "#64748B",
  },
  detailItemCard: {
    backgroundColor: "#FFFFFF",
    borderRadius: 18,
  },
  detailItemContent: {
    gap: 12,
  },
  detailItemHeader: {
    flexDirection: "row",
    justifyContent: "space-between",
    gap: 12,
  },
  detailItemTitleWrap: {
    flex: 1,
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
  detailMetaCell: {
    width: "47%",
    gap: 4,
  },
  detailMetaLabel: {
    color: "#94A3B8",
  },
});
