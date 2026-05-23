import { useCallback, useEffect, useMemo, useState } from "react";
import { FlatList, RefreshControl, ScrollView, StyleSheet, View } from "react-native";
import { useFocusEffect } from "@react-navigation/native";
import { useRouter } from "expo-router";
import {
  ActivityIndicator,
  Button,
  Card,
  Divider,
  Menu,
  Modal,
  Portal,
  Snackbar,
  Text,
  TextInput,
} from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import { EmptyState } from "@/components/ui/EmptyState";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { resolveLocaleTag } from "@/shared/i18n/types";
import { useAuthStore } from "@/store/auth-store";
import {
  createDomesticProductBatch,
  exportDomesticProductBatch,
  fetchDomesticProductBatchDetail,
  fetchDomesticProductBatches,
  fetchDomesticSuppliers,
  fetchProductPrefixes,
  updateDomesticProductBatchItems,
} from "@/modules/domestic-purchase/api";
import type {
  DomesticProductBatch,
  DomesticProductBatchDetail,
  DomesticProductBatchItem,
  DomesticSupplierOption,
  ProductPrefixOption,
} from "@/modules/domestic-purchase/types";
import { ProductCreationType } from "@/modules/domestic-purchase/types";

const PAGE_SIZE = 20;

interface DetailEditState {
  productName: string;
  privateLabelPrice: string;
}

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

function formatPriceInput(value?: number | null) {
  if (value == null || !Number.isFinite(value)) {
    return "";
  }
  return String(value);
}

function buildDetailEdits(items: DomesticProductBatchItem[]) {
  return items.reduce<Record<string, DetailEditState>>((current, item) => {
    const key = item.productCode || item.itemNumber;
    current[key] = {
      productName: item.productName || "",
      privateLabelPrice: formatPriceInput(item.privateLabelPrice),
    };
    return current;
  }, {});
}

function typeLabel(type: ProductCreationType, t: (key: string) => string) {
  if (type === ProductCreationType.Set) {
    return t("types.set");
  }
  if (type === ProductCreationType.SetSubItem) {
    return t("types.setSubItem");
  }
  return t("types.normal");
}

export default function DomesticPurchaseScreen() {
  const router = useRouter();
  const { t, language } = useAppTranslation(["domesticPurchase", "common"]);
  const localeTag = resolveLocaleTag(language);
  const access = useAuthStore((state) => state.access);
  const hasAccess =
    access.isAdmin || access.isWarehouseManager || access.hasPermission("DomesticPurchase.ManageProducts");

  const [batches, setBatches] = useState<DomesticProductBatch[]>([]);
  const [page, setPage] = useState(1);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [busy, setBusy] = useState(false);
  const [loadErrorMessage, setLoadErrorMessage] = useState("");
  const [snackbar, setSnackbar] = useState("");

  const [detailVisible, setDetailVisible] = useState(false);
  const [detailLoading, setDetailLoading] = useState(false);
  const [detailErrorMessage, setDetailErrorMessage] = useState("");
  const [selectedBatch, setSelectedBatch] = useState<DomesticProductBatch | null>(null);
  const [detail, setDetail] = useState<DomesticProductBatchDetail | null>(null);
  const [detailEdits, setDetailEdits] = useState<Record<string, DetailEditState>>({});
  const [detailSaving, setDetailSaving] = useState(false);

  const [createVisible, setCreateVisible] = useState(false);
  const [suppliers, setSuppliers] = useState<DomesticSupplierOption[]>([]);
  const [prefixes, setPrefixes] = useState<ProductPrefixOption[]>([]);
  const [supplierMenuVisible, setSupplierMenuVisible] = useState(false);
  const [prefixMenuVisible, setPrefixMenuVisible] = useState(false);
  const [selectedSupplier, setSelectedSupplier] = useState<DomesticSupplierOption | null>(null);
  const [selectedPrefix, setSelectedPrefix] = useState<ProductPrefixOption | null>(null);
  const [createCount, setCreateCount] = useState("5");
  const [privateLabelPrice, setPrivateLabelPrice] = useState("");

  const hasMore = batches.length < total;

  const loadBatches = useCallback(
    async (nextPage = 1, mode: "replace" | "append" = "replace") => {
      if (!hasAccess) {
        return;
      }

      setLoading(true);
      try {
        const result = await fetchDomesticProductBatches(nextPage, PAGE_SIZE);
        setBatches((current) => (mode === "append" ? [...current, ...result.items] : result.items));
        setTotal(result.total);
        setPage(result.page);
        setLoadErrorMessage("");
      } catch (error) {
        const message = error instanceof Error ? error.message : t("messages.loadFailed");
        setLoadErrorMessage(message);
        setSnackbar(message);
      } finally {
        setLoading(false);
        setRefreshing(false);
      }
    },
    [hasAccess, t]
  );

  const loadSuppliers = useCallback(async () => {
    try {
      setSuppliers(await fetchDomesticSuppliers());
    } catch (error) {
      setSnackbar(error instanceof Error ? error.message : t("messages.loadSuppliersFailed"));
    }
  }, [t]);

  useFocusEffect(
    useCallback(() => {
      void loadBatches(1, "replace");
    }, [loadBatches])
  );

  useEffect(() => {
    if (createVisible) {
      void loadSuppliers();
    }
  }, [createVisible, loadSuppliers]);

  const openDetail = useCallback(
    async (batch: DomesticProductBatch) => {
      setSelectedBatch(batch);
      setDetailVisible(true);
      setDetailLoading(true);
      setDetail(null);
      setDetailEdits({});
      setDetailErrorMessage("");
      try {
        const nextDetail = await fetchDomesticProductBatchDetail(batch.batchNumber);
        setDetail(nextDetail);
        setDetailEdits(buildDetailEdits(nextDetail.items));
      } catch (error) {
        const message = error instanceof Error ? error.message : t("messages.loadDetailFailed");
        setDetailErrorMessage(message);
        setSnackbar(message);
      } finally {
        setDetailLoading(false);
      }
    },
    [t]
  );

  const resetCreateForm = useCallback(() => {
    setSelectedSupplier(null);
    setSelectedPrefix(null);
    setPrefixes([]);
    setCreateCount("5");
    setPrivateLabelPrice("");
    setSupplierMenuVisible(false);
    setPrefixMenuVisible(false);
  }, []);

  const handleSupplierSelect = useCallback(
    async (supplier: DomesticSupplierOption) => {
      setSelectedSupplier(supplier);
      setSelectedPrefix(null);
      setSupplierMenuVisible(false);
      try {
        setPrefixes(await fetchProductPrefixes(supplier.supplierCode));
      } catch (error) {
        setSnackbar(error instanceof Error ? error.message : t("messages.loadPrefixesFailed"));
      }
    },
    [t]
  );

  const handleCreate = useCallback(async () => {
    if (!selectedSupplier) {
      setSnackbar(t("messages.selectSupplier"));
      return;
    }

    const count = Number(createCount);
    if (!Number.isInteger(count) || count < 1 || count > 100) {
      setSnackbar(t("messages.invalidCount"));
      return;
    }

    const price = privateLabelPrice.trim() ? Number(privateLabelPrice) : null;
    if (price != null && (!Number.isFinite(price) || price < 0)) {
      setSnackbar(t("messages.invalidPrice"));
      return;
    }

    setBusy(true);
    try {
      await createDomesticProductBatch({
        supplierCode: selectedSupplier.supplierCode,
        prefixCode: selectedPrefix?.prefixCode || selectedPrefix?.prefixName,
        prefixName: selectedPrefix?.prefixName || selectedPrefix?.prefixCode,
        items: Array.from({ length: count }, () => ({
          productName: "",
          productType: ProductCreationType.Normal,
          privateLabelPrice: price,
        })),
      });
      setSnackbar(t("messages.createSuccess"));
      setCreateVisible(false);
      resetCreateForm();
      await loadBatches(1, "replace");
    } catch (error) {
      setSnackbar(error instanceof Error ? error.message : t("messages.createFailed"));
    } finally {
      setBusy(false);
    }
  }, [createCount, loadBatches, privateLabelPrice, resetCreateForm, selectedPrefix, selectedSupplier, t]);

  const handleExport = useCallback(
    async (batchNumber: string) => {
      setBusy(true);
      try {
        await exportDomesticProductBatch(batchNumber);
        setSnackbar(t("messages.exportSuccess"));
      } catch (error) {
        setSnackbar(error instanceof Error ? error.message : t("messages.exportFailed"));
      } finally {
        setBusy(false);
      }
    },
    [t]
  );

  const updateDetailEdit = useCallback((key: string, patch: Partial<DetailEditState>) => {
    setDetailEdits((current) => ({
      ...current,
      [key]: {
        productName: current[key]?.productName ?? "",
        privateLabelPrice: current[key]?.privateLabelPrice ?? "",
        ...patch,
      },
    }));
  }, []);

  const handleSaveDetailChanges = useCallback(async () => {
    if (!selectedBatch || !detail) {
      return;
    }

    const items = detail.items.map((item) => {
      const key = item.productCode || item.itemNumber;
      const edit = detailEdits[key] ?? {
        productName: item.productName || "",
        privateLabelPrice: formatPriceInput(item.privateLabelPrice),
      };
      const rawPrice = edit.privateLabelPrice.trim();
      const privateLabelPrice = rawPrice ? Number(rawPrice) : null;

      return {
        productCode: key,
        productName: edit.productName,
        privateLabelPrice,
      };
    });

    if (items.some((item) => item.privateLabelPrice != null && (!Number.isFinite(item.privateLabelPrice) || item.privateLabelPrice < 0))) {
      setSnackbar(t("messages.invalidPrice"));
      return;
    }

    setDetailSaving(true);
    try {
      await updateDomesticProductBatchItems(selectedBatch.batchNumber, { items });
      const nextDetail = await fetchDomesticProductBatchDetail(selectedBatch.batchNumber);
      setDetail(nextDetail);
      setDetailEdits(buildDetailEdits(nextDetail.items));
      setSnackbar(t("messages.saveSuccess"));
    } catch (error) {
      setSnackbar(error instanceof Error ? error.message : t("messages.saveFailed"));
    } finally {
      setDetailSaving(false);
    }
  }, [detail, detailEdits, selectedBatch, t]);

  const summaryText = useMemo(
    () => t("summary", { total, shown: batches.length }),
    [batches.length, t, total]
  );

  if (!hasAccess) {
    return (
      <SafeAreaView style={styles.safeArea}>
        <EmptyState
          title={t("empty.noAccessTitle")}
          description={t("empty.noAccessDescription")}
          primaryAction={{
            label: t("common:actions.goToSettings"),
            icon: "cog-outline",
            onPress: () => router.navigate("/(tabs)/settings"),
          }}
        />
      </SafeAreaView>
    );
  }

  return (
    <SafeAreaView style={styles.safeArea}>
      <View style={styles.header}>
        <View>
          <Text variant="titleLarge" style={styles.title}>
            {t("title")}
          </Text>
          <Text variant="bodySmall" style={styles.subtitle}>
            {summaryText}
          </Text>
        </View>
        <Button mode="contained" icon="plus" compact onPress={() => setCreateVisible(true)}>
          {t("actions.create")}
        </Button>
      </View>

      <FlatList
        data={batches}
        keyExtractor={(item) => item.batchNumber}
        contentContainerStyle={batches.length ? styles.listContent : styles.emptyContent}
        refreshControl={
          <RefreshControl
            refreshing={refreshing}
            onRefresh={() => {
              setRefreshing(true);
              void loadBatches(1, "replace");
            }}
          />
        }
        ListEmptyComponent={
          loading ? (
            <ActivityIndicator style={styles.emptyLoader} />
          ) : loadErrorMessage ? (
            <EmptyState
              title={t("messages.loadFailed")}
              description={loadErrorMessage}
              primaryAction={{
                label: t("common:actions.retry"),
                icon: "refresh",
                onPress: () => void loadBatches(1, "replace"),
              }}
            />
          ) : (
            <EmptyState title={t("empty.noBatchTitle")} description={t("empty.noBatchDescription")} />
          )
        }
        renderItem={({ item }) => (
          <Card mode="outlined" style={styles.batchCard}>
            <Card.Content style={styles.batchCardContent}>
              <View style={styles.batchHeader}>
                <View style={styles.batchTitleBlock}>
                  <Text variant="titleMedium" style={styles.batchNumber}>
                    {item.batchNumber}
                  </Text>
                  <Text variant="bodySmall" style={styles.mutedText}>
                    {item.supplierCode} {item.supplierName ? `- ${item.supplierName}` : ""}
                  </Text>
                </View>
                <Text variant="labelMedium" style={styles.countBadge}>
                  {item.totalCount}
                </Text>
              </View>
              <View style={styles.metricRow}>
                <Text variant="bodySmall" style={styles.mutedText}>
                  {t("fields.normalCount", { value: item.normalCount })}
                </Text>
                <Text variant="bodySmall" style={styles.mutedText}>
                  {t("fields.setCount", { value: item.setCount })}
                </Text>
                <Text variant="bodySmall" style={styles.mutedText}>
                  {formatDateTime(item.createdAt, localeTag)}
                </Text>
              </View>
              <View style={styles.cardActions}>
                <Button compact mode="outlined" onPress={() => openDetail(item)}>
                  {t("actions.detail")}
                </Button>
                <Button compact icon="download" onPress={() => handleExport(item.batchNumber)} disabled={busy}>
                  {t("actions.export")}
                </Button>
              </View>
            </Card.Content>
          </Card>
        )}
        ListFooterComponent={
          hasMore ? (
            <Button
              mode="outlined"
              loading={loading}
              disabled={loading}
              style={styles.loadMoreButton}
              onPress={() => loadBatches(page + 1, "append")}
            >
              {t("actions.loadMore")}
            </Button>
          ) : null
        }
      />

      <Portal>
        <Modal
          visible={createVisible}
          onDismiss={() => {
            setCreateVisible(false);
            resetCreateForm();
          }}
          contentContainerStyle={styles.modal}
        >
          <Text variant="titleMedium" style={styles.modalTitle}>
            {t("create.title")}
          </Text>
          <Menu
            visible={supplierMenuVisible}
            onDismiss={() => setSupplierMenuVisible(false)}
            anchor={
              <Button mode="outlined" onPress={() => setSupplierMenuVisible(true)} style={styles.fullButton}>
                {selectedSupplier
                  ? `${selectedSupplier.supplierCode} - ${selectedSupplier.supplierName}`
                  : t("create.selectSupplier")}
              </Button>
            }
          >
            <ScrollView style={styles.menuScroll}>
              {suppliers.map((supplier) => (
                <Menu.Item
                  key={supplier.supplierCode}
                  title={`${supplier.supplierCode} - ${supplier.supplierName}`}
                  onPress={() => handleSupplierSelect(supplier)}
                />
              ))}
            </ScrollView>
          </Menu>
          <Menu
            visible={prefixMenuVisible}
            onDismiss={() => setPrefixMenuVisible(false)}
            anchor={
              <Button
                mode="outlined"
                onPress={() => setPrefixMenuVisible(true)}
                disabled={!selectedSupplier}
                style={styles.fullButton}
              >
                {selectedPrefix?.prefixName || selectedPrefix?.prefixCode || t("create.selectPrefix")}
              </Button>
            }
          >
            <ScrollView style={styles.menuScroll}>
              <Menu.Item
                title={t("create.noPrefix")}
                onPress={() => {
                  setSelectedPrefix(null);
                  setPrefixMenuVisible(false);
                }}
              />
              {prefixes.map((prefix) => (
                <Menu.Item
                  key={`${prefix.prefixCode}-${prefix.prefixName}`}
                  title={prefix.prefixDescription ? `${prefix.prefixName} - ${prefix.prefixDescription}` : prefix.prefixName}
                  onPress={() => {
                    setSelectedPrefix(prefix);
                    setPrefixMenuVisible(false);
                  }}
                />
              ))}
            </ScrollView>
          </Menu>
          <TextInput
            mode="outlined"
            label={t("create.count")}
            value={createCount}
            keyboardType="number-pad"
            onChangeText={setCreateCount}
            style={styles.input}
          />
          <TextInput
            mode="outlined"
            label={t("create.privateLabelPrice")}
            value={privateLabelPrice}
            keyboardType="decimal-pad"
            onChangeText={setPrivateLabelPrice}
            style={styles.input}
          />
          <Text variant="bodySmall" style={styles.inputHelpText}>
            {t("create.privateLabelPriceHelp")}
          </Text>
          <View style={styles.modalActions}>
            <Button onPress={() => setCreateVisible(false)}>{t("actions.cancel")}</Button>
            <Button mode="contained" loading={busy} disabled={busy} onPress={handleCreate}>
              {t("actions.confirmCreate")}
            </Button>
          </View>
        </Modal>

        <Modal
          visible={detailVisible}
          onDismiss={() => {
            setDetailVisible(false);
            setDetailEdits({});
          }}
          contentContainerStyle={styles.detailModal}
        >
          <View style={styles.detailHeader}>
            <View>
              <Text variant="titleMedium">{selectedBatch?.batchNumber}</Text>
              <Text variant="bodySmall" style={styles.mutedText}>
                {detail?.supplierName || selectedBatch?.supplierName || selectedBatch?.supplierCode}
              </Text>
            </View>
            <View style={styles.detailHeaderActions}>
              <Button
                compact
                mode="contained"
                icon="content-save"
                loading={detailSaving}
                disabled={!detail || detailLoading || detailSaving}
                onPress={handleSaveDetailChanges}
              >
                {t("actions.saveChanges")}
              </Button>
              <Button
                compact
                icon="download"
                loading={busy}
                disabled={!selectedBatch || busy || detailSaving}
                onPress={() => selectedBatch && handleExport(selectedBatch.batchNumber)}
              >
                {t("actions.export")}
              </Button>
            </View>
          </View>
          <Divider style={styles.divider} />
          {detailLoading ? (
            <ActivityIndicator style={styles.detailLoader} />
          ) : detailErrorMessage ? (
            <EmptyState
              title={t("messages.loadDetailFailed")}
              description={detailErrorMessage}
              primaryAction={{
                label: t("common:actions.retry"),
                icon: "refresh",
                onPress: () => selectedBatch && void openDetail(selectedBatch),
              }}
              secondaryAction={{
                label: t("common:actions.close"),
                icon: "close",
                onPress: () => setDetailVisible(false),
              }}
            />
          ) : (
            <ScrollView style={styles.detailList}>
              {(detail?.items ?? []).map((item, index) => (
                <View key={`${item.hbProductNo}-${item.barcode}-${index}`} style={styles.detailItem}>
                  <View style={styles.detailItemHeader}>
                    <Text variant="titleSmall" style={styles.itemNumber}>
                      {item.hbProductNo || item.itemNumber || "--"}
                    </Text>
                    <Text variant="labelSmall" style={styles.typeBadge}>
                      {typeLabel(item.productType, t)}
                    </Text>
                  </View>
                  <TextInput
                    mode="outlined"
                    dense
                    label={t("fields.productName")}
                    value={detailEdits[item.productCode || item.itemNumber]?.productName ?? item.productName ?? ""}
                    onChangeText={(value) => updateDetailEdit(item.productCode || item.itemNumber, { productName: value })}
                    style={styles.detailInput}
                  />
                  <Text variant="bodySmall" style={styles.mutedText}>
                    {t("fields.barcode", { value: item.barcode || "--" })}
                  </Text>
                  <View style={styles.detailMetaRow}>
                    <TextInput
                      mode="outlined"
                      dense
                      label={t("fields.privateLabelPriceLabel")}
                      value={detailEdits[item.productCode || item.itemNumber]?.privateLabelPrice ?? formatPriceInput(item.privateLabelPrice)}
                      keyboardType="decimal-pad"
                      onChangeText={(value) => updateDetailEdit(item.productCode || item.itemNumber, { privateLabelPrice: value })}
                      style={styles.detailPriceInput}
                    />
                    {item.parentItemNumber ? (
                      <Text variant="bodySmall" style={styles.mutedText}>
                        {t("fields.parent", { value: item.parentItemNumber })}
                      </Text>
                    ) : null}
                  </View>
                </View>
              ))}
            </ScrollView>
          )}
        </Modal>
      </Portal>

      <Snackbar visible={Boolean(snackbar)} onDismiss={() => setSnackbar("")} duration={3000}>
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
  header: {
    paddingHorizontal: 16,
    paddingTop: 10,
    paddingBottom: 10,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 12,
  },
  title: {
    fontWeight: "700",
  },
  subtitle: {
    color: "#667085",
    marginTop: 2,
  },
  listContent: {
    padding: 12,
    paddingBottom: 24,
  },
  emptyContent: {
    flexGrow: 1,
    justifyContent: "center",
    padding: 24,
  },
  emptyLoader: {
    paddingVertical: 32,
  },
  batchCard: {
    marginBottom: 10,
    borderRadius: 8,
    backgroundColor: "#FFFFFF",
  },
  batchCardContent: {
    gap: 10,
  },
  batchHeader: {
    flexDirection: "row",
    alignItems: "flex-start",
    justifyContent: "space-between",
    gap: 10,
  },
  batchTitleBlock: {
    flex: 1,
  },
  batchNumber: {
    fontWeight: "700",
  },
  mutedText: {
    color: "#667085",
  },
  countBadge: {
    minWidth: 36,
    textAlign: "center",
    color: "#0958D9",
    backgroundColor: "#E6F4FF",
    borderRadius: 6,
    paddingHorizontal: 8,
    paddingVertical: 4,
  },
  metricRow: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 12,
  },
  cardActions: {
    flexDirection: "row",
    justifyContent: "flex-end",
    gap: 8,
  },
  loadMoreButton: {
    marginTop: 6,
  },
  modal: {
    margin: 18,
    padding: 16,
    borderRadius: 8,
    backgroundColor: "#FFFFFF",
  },
  detailModal: {
    margin: 14,
    padding: 14,
    borderRadius: 8,
    backgroundColor: "#FFFFFF",
    maxHeight: "86%",
  },
  modalTitle: {
    fontWeight: "700",
    marginBottom: 12,
  },
  fullButton: {
    marginTop: 8,
    alignItems: "stretch",
  },
  menuScroll: {
    maxHeight: 300,
  },
  input: {
    marginTop: 10,
  },
  inputHelpText: {
    marginTop: 4,
    color: "#667085",
  },
  modalActions: {
    marginTop: 14,
    flexDirection: "row",
    justifyContent: "flex-end",
    gap: 8,
  },
  detailHeader: {
    flexDirection: "row",
    alignItems: "flex-start",
    justifyContent: "space-between",
    gap: 10,
  },
  detailHeaderActions: {
    flexDirection: "row",
    flexWrap: "wrap",
    justifyContent: "flex-end",
    gap: 8,
  },
  divider: {
    marginVertical: 10,
  },
  detailLoader: {
    paddingVertical: 32,
  },
  detailList: {
    maxHeight: 560,
  },
  detailItem: {
    paddingVertical: 10,
    borderBottomWidth: StyleSheet.hairlineWidth,
    borderBottomColor: "#EAECF0",
    gap: 8,
  },
  detailItemHeader: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 8,
  },
  itemNumber: {
    flex: 1,
    fontWeight: "700",
  },
  typeBadge: {
    color: "#237804",
    backgroundColor: "#F6FFED",
    borderRadius: 6,
    paddingHorizontal: 8,
    paddingVertical: 3,
  },
  detailMetaRow: {
    flexDirection: "row",
    flexWrap: "wrap",
    alignItems: "center",
    gap: 12,
  },
  detailInput: {
    backgroundColor: "#FFFFFF",
  },
  detailPriceInput: {
    width: 160,
    backgroundColor: "#FFFFFF",
  },
});
