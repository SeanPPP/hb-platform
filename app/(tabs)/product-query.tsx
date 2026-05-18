import { useCallback, useMemo, useState } from "react";
import { ScrollView, StyleSheet, TextInput, View } from "react-native";
import { CameraView } from "expo-camera";
import { useFocusEffect, useIsFocused } from "@react-navigation/native";
import { Button, Card, Modal, Portal, Snackbar, Text } from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import { LookupResultSheet } from "@/components/product-maintenance/LookupResultSheet";
import { MultiCodeCompactList } from "@/components/product-maintenance/MultiCodeCompactList";
import { ProductHeroCard } from "@/components/product-maintenance/ProductHeroCard";
import { QueryHeader } from "@/components/product-maintenance/QueryHeader";
import { SearchPanel } from "@/components/product-maintenance/SearchPanel";
import { SetCodeCompactSection } from "@/components/product-maintenance/SetCodeCompactSection";
import { StickyActionBar } from "@/components/product-maintenance/StickyActionBar";
import { StoreClearancePriceCard } from "@/components/product-maintenance/StoreClearancePriceCard";
import { StorePriceStrategyCard } from "@/components/product-maintenance/StorePriceStrategyCard";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import {
  getProductDetail,
  lookupProducts,
  updateMultiCode,
  updateStorePrice,
} from "@/modules/product-maintenance/api";
import type {
  MultiCodeEditableItem,
  ProductDetail,
  ProductLookupItem,
} from "@/modules/product-maintenance/types";
import { useCameraScan } from "@/modules/scanner/use-camera-scan";
import { useHidBarcodeScanner } from "@/modules/scanner/use-hid-barcode-scanner";
import { useStores } from "@/modules/shop/use-stores";

function cloneDetail(detail: ProductDetail | null): ProductDetail | null {
  return detail ? JSON.parse(JSON.stringify(detail)) : null;
}

function formatDecimal(value?: number | null) {
  return value == null ? "" : String(value);
}

function formatCurrency(value?: number | null) {
  return value == null ? "" : value.toFixed(2);
}

function isStorePriceDirty(current: ProductDetail | null, initial: ProductDetail | null) {
  const left = current?.storePrice;
  const right = initial?.storePrice;
  return JSON.stringify(left ?? null) !== JSON.stringify(right ?? null);
}

function getDirtyMultiCodeIds(current: ProductDetail | null, initial: ProductDetail | null) {
  const baseline = new Map((initial?.multiCodes ?? []).map((item) => [item.uuid, JSON.stringify(item)]));
  return (current?.multiCodes ?? [])
    .filter((item) => baseline.get(item.uuid) !== JSON.stringify(item))
    .map((item) => item.uuid);
}

type QueryFeedback =
  | { type: "idle" }
  | { type: "empty"; query: string }
  | { type: "error"; query?: string; message: string };

function ProductQueryContent() {
  const { t } = useAppTranslation(["productQuery", "common"]);
  const { selectedStore, selectedStoreCode, isLoading: storesLoading } = useStores();
  const [keyword, setKeyword] = useState("");
  const [lookupItems, setLookupItems] = useState<ProductLookupItem[]>([]);
  const [selectedLookupProductCode, setSelectedLookupProductCode] = useState<string>();
  const [detail, setDetail] = useState<ProductDetail | null>(null);
  const [initialDetail, setInitialDetail] = useState<ProductDetail | null>(null);
  const [lastHitLabel, setLastHitLabel] = useState<string>();
  const [lookupVisible, setLookupVisible] = useState(false);
  const [cameraVisible, setCameraVisible] = useState(false);
  const [queryFeedback, setQueryFeedback] = useState<QueryFeedback>({ type: "idle" });
  const [loading, setLoading] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [saving, setSaving] = useState(false);
  const [savingItemId, setSavingItemId] = useState<string | null>(null);
  const [snackbarMessage, setSnackbarMessage] = useState("");

  const loadDetail = useCallback(
    async (productCode: string) => {
      if (!selectedStoreCode) {
        setSnackbarMessage(t("messages.selectStoreFirst"));
        return;
      }

      console.log("[product-query] load detail", { productCode, selectedStoreCode });
      const payload = await getProductDetail(productCode, selectedStoreCode);
      setDetail(payload);
      setInitialDetail(cloneDetail(payload));
      setSelectedLookupProductCode(productCode);
      setLastHitLabel(`${payload.itemNumber || payload.productCode} / ${payload.barcode || "--"}`);
      setQueryFeedback({ type: "idle" });
    },
    [selectedStoreCode, t]
  );

  const handleLookup = useCallback(
    async (sourceKeyword?: string) => {
      const nextKeyword = (sourceKeyword ?? keyword).trim();
      if (!nextKeyword) {
        setSnackbarMessage(t("messages.keywordRequired"));
        return;
      }

      if (!selectedStoreCode) {
        setSnackbarMessage(t("messages.storeUnavailable"));
        return;
      }

      console.log("[product-query] lookup start", {
        keyword: nextKeyword,
        selectedStoreCode,
      });
      setLoading(true);
      setQueryFeedback({ type: "idle" });
      try {
        const items = await lookupProducts({
          keyword: nextKeyword,
          storeCode: selectedStoreCode,
        });
        console.log("[product-query] lookup success", {
          keyword: nextKeyword,
          count: items.length,
        });
        setLookupItems(items);
        if (!items.length) {
          setDetail(null);
          setInitialDetail(null);
          setSelectedLookupProductCode(undefined);
          setLookupVisible(false);
          setQueryFeedback({ type: "empty", query: nextKeyword });
          setSnackbarMessage(t("messages.notFound"));
          return;
        }

        if (items.length === 1) {
          setLookupVisible(false);
          await loadDetail(items[0].productCode);
          return;
        }

        setSelectedLookupProductCode(items[0].productCode);
        setLookupVisible(true);
      } catch (error) {
        const message = error instanceof Error ? error.message : t("messages.lookupFailed");
        console.error("[product-query] lookup failed", {
          keyword: nextKeyword,
          selectedStoreCode,
          message,
        });
        setDetail(null);
        setInitialDetail(null);
        setLookupVisible(false);
        setQueryFeedback({ type: "error", query: nextKeyword, message });
        setSnackbarMessage(message);
      } finally {
        setLoading(false);
      }
    },
    [keyword, loadDetail, selectedStoreCode, t]
  );

  const cameraScan = useCameraScan({
    onBarcode: async (barcode) => {
      console.log("[product-query] barcode scanned", { barcode });
      setKeyword(barcode);
      await handleLookup(barcode);
      setCameraVisible(false);
    },
  });
  const hidScanner = useHidBarcodeScanner({
    onScan: async (barcode) => {
      console.log("[product-query] hid barcode scanned", { barcode });
      setKeyword(barcode);
      await handleLookup(barcode);
    },
  });

  useFocusEffect(
    useCallback(() => {
      if (hidScanner.focusHiddenInput) {
        hidScanner.focusHiddenInput();
      }
    }, [hidScanner.focusHiddenInput])
  );

  const dirtyMultiCodeIds = useMemo(() => getDirtyMultiCodeIds(detail, initialDetail), [detail, initialDetail]);
  const dirtyCount = useMemo(
    () => (isStorePriceDirty(detail, initialDetail) ? 1 : 0) + dirtyMultiCodeIds.length,
    [detail, dirtyMultiCodeIds, initialDetail]
  );

  const handleRefresh = useCallback(async () => {
    if (!detail?.productCode) {
      if (keyword.trim()) {
        setRefreshing(true);
        try {
          await handleLookup(keyword);
        } finally {
          setRefreshing(false);
        }
      }
      return;
    }

    setRefreshing(true);
    try {
      await loadDetail(detail.productCode);
    } catch (error) {
      setSnackbarMessage(error instanceof Error ? error.message : t("messages.refreshFailed"));
    } finally {
      setRefreshing(false);
    }
  }, [detail?.productCode, handleLookup, keyword, loadDetail, t]);

  const handleClear = useCallback(() => {
    setKeyword("");
    setLookupItems([]);
    setSelectedLookupProductCode(undefined);
    setDetail(null);
    setInitialDetail(null);
    setLookupVisible(false);
    setQueryFeedback({ type: "idle" });
  }, []);

  const handleConfirmLookup = useCallback(async () => {
    if (!selectedLookupProductCode) {
      return;
    }

    setLookupVisible(false);
    try {
      await loadDetail(selectedLookupProductCode);
    } catch (error) {
      const message = error instanceof Error ? error.message : t("messages.lookupFailed");
      setDetail(null);
      setInitialDetail(null);
      setQueryFeedback({ type: "error", query: keyword.trim(), message });
      setSnackbarMessage(message);
    }
  }, [keyword, loadDetail, selectedLookupProductCode, t]);

  const handleChangeStorePrice = useCallback((patch: Partial<NonNullable<ProductDetail["storePrice"]>>) => {
    setDetail((current) =>
      current?.storePrice
        ? {
            ...current,
            storePrice: {
              ...current.storePrice,
              ...patch,
            },
          }
        : current
    );
  }, []);

  const handleChangeMultiCode = useCallback((uuid: string, patch: Partial<MultiCodeEditableItem>) => {
    setDetail((current) =>
      current
        ? {
            ...current,
            multiCodes: current.multiCodes.map((item) => (item.uuid === uuid ? { ...item, ...patch } : item)),
          }
        : current
    );
  }, []);

  const handleSaveMultiCode = useCallback(
    async (uuid: string) => {
      const target = detail?.multiCodes.find((item) => item.uuid === uuid);
      if (!target) {
        return;
      }

      setSavingItemId(uuid);
      try {
        const saved = await updateMultiCode(uuid, {
          purchasePrice: target.purchasePrice ?? null,
          retailPrice: target.retailPrice ?? null,
          isAutoPricing: target.isAutoPricing,
          isSpecialProduct: target.isSpecialProduct,
          isActive: target.isActive,
        });

        setDetail((current) =>
          current
            ? {
                ...current,
                multiCodes: current.multiCodes.map((item) => (item.uuid === uuid ? saved : item)),
              }
            : current
        );
        setInitialDetail((current) =>
          current
            ? {
                ...current,
                multiCodes: current.multiCodes.map((item) => (item.uuid === uuid ? saved : item)),
              }
            : current
        );
        setSnackbarMessage(t("messages.multiCodeSaved"));
      } catch (error) {
        setSnackbarMessage(error instanceof Error ? error.message : t("messages.multiCodeSaveFailed"));
      } finally {
        setSavingItemId(null);
      }
    },
    [detail?.multiCodes, t]
  );

  const handleSaveAll = useCallback(async () => {
    if (!detail) {
      return;
    }

    setSaving(true);
    try {
      const nextDetail = cloneDetail(detail)!;
      const nextInitial = cloneDetail(initialDetail)!;

      if (detail.storePrice && isStorePriceDirty(detail, initialDetail)) {
        const savedStorePrice = await updateStorePrice(detail.storePrice.uuid, {
          purchasePrice: detail.storePrice.purchasePrice ?? null,
          retailPrice: detail.storePrice.retailPrice ?? null,
          isAutoPricing: detail.storePrice.isAutoPricing,
          isSpecialProduct: detail.storePrice.isSpecialProduct,
          isActive: detail.storePrice.isActive,
        });
        nextDetail.storePrice = savedStorePrice;
        nextInitial.storePrice = savedStorePrice;
      }

      for (const uuid of dirtyMultiCodeIds) {
        const target = nextDetail.multiCodes.find((item) => item.uuid === uuid);
        if (!target) {
          continue;
        }

        const saved = await updateMultiCode(uuid, {
          purchasePrice: target.purchasePrice ?? null,
          retailPrice: target.retailPrice ?? null,
          isAutoPricing: target.isAutoPricing,
          isSpecialProduct: target.isSpecialProduct,
          isActive: target.isActive,
        });
        nextDetail.multiCodes = nextDetail.multiCodes.map((item) => (item.uuid === uuid ? saved : item));
        nextInitial.multiCodes = nextInitial.multiCodes.map((item) => (item.uuid === uuid ? saved : item));
      }

      setDetail(nextDetail);
      setInitialDetail(nextInitial);
      setSnackbarMessage(t("messages.saved"));
    } catch (error) {
      setSnackbarMessage(error instanceof Error ? error.message : t("messages.saveFailed"));
    } finally {
      setSaving(false);
    }
  }, [detail, dirtyMultiCodeIds, initialDetail, t]);

  const handleReset = useCallback(() => {
    setDetail(cloneDetail(initialDetail));
  }, [initialDetail]);

  const storePrice = detail?.storePrice;
  const clearancePrice = detail?.clearancePrice;

  return (
    <SafeAreaView style={styles.safeArea} edges={["top"]}>
      <QueryHeader
        storeName={selectedStore?.storeName}
        onScanPress={() => setCameraVisible(true)}
        onRefreshPress={() => void handleRefresh()}
        refreshing={refreshing}
      />

      <SearchPanel
        value={keyword}
        loading={loading || storesLoading}
        lastHitLabel={lastHitLabel}
        onChangeText={setKeyword}
        onSubmit={() => void handleLookup()}
        onClear={handleClear}
      />

      <ScrollView contentContainerStyle={styles.content}>
        {detail ? (
          <>
            <ProductHeroCard
              imageUrl={detail.productImage}
              productName={detail.productName}
              productCode={detail.productCode}
              itemNumber={detail.itemNumber}
              barcode={detail.barcode}
              productTypeLabel={detail.productTypeLabel}
              grade={detail.grade}
            />

            {storePrice ? (
              <StorePriceStrategyCard
                storeName={storePrice.storeName}
                purchasePrice={formatDecimal(storePrice.purchasePrice)}
                retailPrice={formatDecimal(storePrice.retailPrice)}
                autoPricing={storePrice.isAutoPricing}
                isSpecialProduct={storePrice.isSpecialProduct}
                isActive={storePrice.isActive}
                rate={storePrice.rate == null ? "" : String(storePrice.rate)}
                strategySourceLabel={storePrice.strategySourceLabel}
                strategyRuleLabel={storePrice.strategyRuleLabel}
                onChangePurchasePrice={(value) =>
                  handleChangeStorePrice({
                    purchasePrice: value.trim() === "" ? null : Number(value),
                  })
                }
                onChangeRetailPrice={(value) =>
                  handleChangeStorePrice({
                    retailPrice: value.trim() === "" ? null : Number(value),
                  })
                }
                onToggleAutoPricing={(value) => handleChangeStorePrice({ isAutoPricing: value })}
                onToggleSpecial={(value) => handleChangeStorePrice({ isSpecialProduct: value })}
                onToggleActive={(value) => handleChangeStorePrice({ isActive: value })}
              />
            ) : (
              <View style={styles.emptyBlock}>
                <Text variant="bodyMedium">{t("messages.emptyStorePrice")}</Text>
              </View>
            )}

            {clearancePrice ? (
              <StoreClearancePriceCard
                storeCode={clearancePrice.storeCode}
                storeName={clearancePrice.storeName}
                clearanceBarcode={clearancePrice.clearanceBarcode}
                clearancePrice={formatCurrency(clearancePrice.clearancePrice)}
              />
            ) : null}

            <SetCodeCompactSection items={detail.setCodes} />
            <MultiCodeCompactList
              items={detail.multiCodes}
              onChangeItem={handleChangeMultiCode}
              onSaveItem={(uuid) => void handleSaveMultiCode(uuid)}
              savingItemId={savingItemId}
            />
          </>
        ) : queryFeedback.type === "empty" ? (
          <View style={styles.emptyBlock}>
            <Text variant="titleSmall" style={styles.emptyTitle}>
              {t("messages.noResultTitle")}
            </Text>
            <Text variant="bodyMedium" style={styles.emptyText}>
              {t("messages.noResultDescription", { value: queryFeedback.query })}
            </Text>
          </View>
        ) : queryFeedback.type === "error" ? (
          <View style={styles.emptyBlock}>
            <Text variant="titleSmall" style={styles.emptyTitle}>
              {t("messages.lookupErrorTitle")}
            </Text>
            <Text variant="bodyMedium" style={styles.emptyText}>
              {queryFeedback.message || t("messages.lookupErrorDescription")}
            </Text>
          </View>
        ) : (
          <View style={styles.emptyBlock}>
            <Text variant="bodyMedium">{t("messages.emptyPrompt")}</Text>
          </View>
        )}
      </ScrollView>

      <StickyActionBar
        visible={dirtyCount > 0}
        dirtyCount={dirtyCount}
        saving={saving}
        onReset={handleReset}
        onSaveAll={() => void handleSaveAll()}
      />

      <LookupResultSheet
        visible={lookupVisible}
        queryText={keyword}
        items={lookupItems}
        selectedValue={selectedLookupProductCode}
        onSelect={setSelectedLookupProductCode}
        onClose={() => setLookupVisible(false)}
        onConfirm={() => void handleConfirmLookup()}
      />

      <Portal>
        <Modal
          visible={cameraVisible}
          onDismiss={() => setCameraVisible(false)}
          contentContainerStyle={styles.cameraModal}
        >
          <View style={styles.cameraHeader}>
            <Text variant="titleMedium">{t("camera.title")}</Text>
            <Button onPress={() => setCameraVisible(false)}>{t("common:actions.close")}</Button>
          </View>
          {cameraScan.permission?.granted ? (
            <View style={styles.cameraFrame}>
              <CameraView style={styles.cameraView} {...cameraScan.cameraProps} />
            </View>
          ) : (
            <Card style={styles.permissionCard}>
              <Card.Content style={styles.permissionCardContent}>
                <Text variant="titleMedium">{t("camera.needPermissionTitle")}</Text>
                <Text variant="bodySmall" style={styles.cameraTip}>
                  {t("camera.needPermissionDescription")}
                </Text>
                <Button mode="contained" onPress={() => void cameraScan.requestPermission()}>
                  {t("camera.grantPermission")}
                </Button>
              </Card.Content>
            </Card>
          )}
          <Text variant="bodySmall" style={styles.cameraTip}>
            {t("messages.cameraTip")}
          </Text>
        </Modal>
      </Portal>

      <Snackbar visible={Boolean(snackbarMessage)} onDismiss={() => setSnackbarMessage("")} duration={2500}>
        {snackbarMessage}
      </Snackbar>

      {hidScanner.mode === "textInput" && hidScanner.textInputProps ? (
        <TextInput style={styles.hiddenInput} {...hidScanner.textInputProps} />
      ) : null}
    </SafeAreaView>
  );
}

export default function ProductQueryScreen() {
  const isFocused = useIsFocused();

  if (!isFocused) {
    return <SafeAreaView style={styles.safeArea} edges={["top"]} />;
  }

  return <ProductQueryContent />;
}

const styles = StyleSheet.create({
  safeArea: {
    flex: 1,
    backgroundColor: "#F7F8FA",
  },
  content: {
    paddingHorizontal: 16,
    paddingBottom: 20,
    gap: 10,
  },
  hiddenInput: {
    position: "absolute",
    width: 1,
    height: 1,
    opacity: 0,
  },
  emptyBlock: {
    borderRadius: 8,
    backgroundColor: "#fff",
    padding: 16,
    gap: 6,
  },
  emptyTitle: {
    fontWeight: "700",
  },
  emptyText: {
    color: "#555",
  },
  cameraModal: {
    marginHorizontal: 16,
    padding: 16,
    borderRadius: 16,
    backgroundColor: "#fff",
    gap: 12,
  },
  cameraHeader: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
  },
  cameraFrame: {
    overflow: "hidden",
    borderRadius: 12,
    height: 320,
    backgroundColor: "#000",
  },
  permissionCard: {
    borderRadius: 12,
  },
  permissionCardContent: {
    gap: 12,
    paddingVertical: 8,
  },
  cameraView: {
    flex: 1,
  },
  cameraTip: {
    color: "#666",
  },
});
