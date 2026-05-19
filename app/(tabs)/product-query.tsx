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
import {
  getSavedPrinter,
  printClearanceLabel,
  printDiscountLabel,
  printProductLabel,
} from "@/modules/printer/api";
import { usePrinterStore } from "@/modules/printer/state";
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

type LookupTrigger = "manual" | "scan";

function cloneDetail(detail: ProductDetail | null): ProductDetail | null {
  return detail ? JSON.parse(JSON.stringify(detail)) : null;
}

function formatDecimal(value?: number | null) {
  return value == null ? "" : String(value);
}

function formatCurrency(value?: number | null) {
  return value == null ? "" : value.toFixed(2);
}

function clamp(value: number, min: number, max: number) {
  return Math.min(Math.max(value, min), max);
}

function parseDecimalInput(value: string): number | null {
  const trimmed = value.trim();
  if (!trimmed) {
    return null;
  }

  const parsed = Number(trimmed);
  return Number.isFinite(parsed) ? parsed : null;
}

function normalizeDiscountRateValue(value?: number | null) {
  if (value == null || !Number.isFinite(value) || value < 0) {
    return null;
  }

  if (value <= 1) {
    return value;
  }

  if (value <= 100) {
    return value / 100;
  }

  return null;
}

function getDiscountedRetailPrice(retailPrice?: number | null, discountRate?: number | null) {
  if (
    retailPrice == null
    || !Number.isFinite(retailPrice)
    || retailPrice <= 0
    || discountRate == null
    || !Number.isFinite(discountRate)
  ) {
    return null;
  }

  return retailPrice * (1 - clamp(discountRate, 0, 1));
}

function getDiscountRateFromDiscountedRetail(retailPrice?: number | null, discountedRetail?: number | null) {
  if (
    retailPrice == null
    || !Number.isFinite(retailPrice)
    || retailPrice <= 0
    || discountedRetail == null
    || !Number.isFinite(discountedRetail)
  ) {
    return null;
  }

  return clamp(1 - discountedRetail / retailPrice, 0, 1);
}

function formatPercentValue(value?: number | null) {
  if (value == null || !Number.isFinite(value)) {
    return "";
  }

  const percent = value * 100;
  return Number.isInteger(percent) ? String(percent) : percent.toFixed(2).replace(/\.?0+$/, "");
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
  const printerAutoReconnectPaused = usePrinterStore((state) => state.autoReconnectPaused);
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
  const [printingKind, setPrintingKind] = useState<"product" | "discount" | "clearance" | null>(null);
  const [continuousPrintEnabled, setContinuousPrintEnabled] = useState(false);
  const [autoPrintOnLookupConfirm, setAutoPrintOnLookupConfirm] = useState(false);

  const loadDetail = useCallback(
    async (productCode: string) => {
      if (!selectedStoreCode) {
        setSnackbarMessage(t("messages.selectStoreFirst"));
        return null;
      }

      console.log("[product-query] load detail", { productCode, selectedStoreCode });
      const payload = await getProductDetail(productCode, selectedStoreCode);
      setDetail(payload);
      setInitialDetail(cloneDetail(payload));
      setSelectedLookupProductCode(productCode);
      setLastHitLabel(`${payload.itemNumber || payload.productCode} / ${payload.barcode || "--"}`);
      setQueryFeedback({ type: "idle" });
      return payload;
    },
    [selectedStoreCode, t]
  );

  const sendProductLabel = useCallback(
    async (targetDetail: ProductDetail) => {
      const savedPrinter = await getSavedPrinter();
      if (!savedPrinter?.address) {
        setSnackbarMessage(t("messages.printerRequired"));
        return false;
      }

      if (printerAutoReconnectPaused) {
        setSnackbarMessage(t("messages.printerPaused"));
        return false;
      }

      setPrintingKind("product");
      try {
        await printProductLabel(targetDetail);
        setSnackbarMessage(t("messages.printSuccess"));
        return true;
      } catch (error) {
        const fallback = t("messages.printFailed");
        setSnackbarMessage(error instanceof Error ? `${fallback}: ${error.message}` : fallback);
        return false;
      } finally {
        setPrintingKind(null);
      }
    },
    [printerAutoReconnectPaused, t]
  );

  const handleLookup = useCallback(
    async (sourceKeyword?: string, trigger: LookupTrigger = "manual") => {
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
        trigger,
      });
      setLoading(true);
      setAutoPrintOnLookupConfirm(false);
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
          const nextDetail = await loadDetail(items[0].productCode);
          if (nextDetail && trigger === "scan" && continuousPrintEnabled) {
            await sendProductLabel(nextDetail);
          }
          return;
        }

        setSelectedLookupProductCode(items[0].productCode);
        setAutoPrintOnLookupConfirm(trigger === "scan" && continuousPrintEnabled);
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
    [continuousPrintEnabled, keyword, loadDetail, selectedStoreCode, sendProductLabel, t]
  );

  const cameraScan = useCameraScan({
    onBarcode: async (barcode) => {
      console.log("[product-query] barcode scanned", { barcode });
      setKeyword(barcode);
      await handleLookup(barcode, "scan");
      setCameraVisible(false);
    },
  });
  const hidScanner = useHidBarcodeScanner({
    onScan: async (barcode) => {
      console.log("[product-query] hid barcode scanned", { barcode });
      setKeyword(barcode);
      await handleLookup(barcode, "scan");
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
    setAutoPrintOnLookupConfirm(false);
    setQueryFeedback({ type: "idle" });
  }, []);

  const handleConfirmLookup = useCallback(async () => {
    if (!selectedLookupProductCode) {
      return;
    }

    setLookupVisible(false);
    try {
      const nextDetail = await loadDetail(selectedLookupProductCode);
      if (nextDetail && autoPrintOnLookupConfirm) {
        await sendProductLabel(nextDetail);
      }
    } catch (error) {
      const message = error instanceof Error ? error.message : t("messages.lookupFailed");
      setDetail(null);
      setInitialDetail(null);
      setQueryFeedback({ type: "error", query: keyword.trim(), message });
      setSnackbarMessage(message);
    } finally {
      setAutoPrintOnLookupConfirm(false);
    }
  }, [autoPrintOnLookupConfirm, keyword, loadDetail, selectedLookupProductCode, sendProductLabel, t]);

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

  const handleChangeStoreRetailPrice = useCallback(
    (value: string) => {
      const retailPrice = parseDecimalInput(value);
      setDetail((current) => {
        if (!current?.storePrice) {
          return current;
        }

        const discountRate = normalizeDiscountRateValue(current.storePrice.discountRate);
        return {
          ...current,
          storePrice: {
            ...current.storePrice,
            retailPrice,
            discountRate,
          },
        };
      });
    },
    []
  );

  const handleChangeStoreDiscountPercent = useCallback(
    (value: string) => {
      const percentValue = parseDecimalInput(value);
      const discountRate =
        percentValue == null ? null : normalizeDiscountRateValue(clamp(percentValue, 0, 100));
      handleChangeStorePrice({ discountRate });
    },
    [handleChangeStorePrice]
  );

  const handleChangeStoreDiscountedRetailPrice = useCallback((value: string) => {
    const discountedRetail = parseDecimalInput(value);
    setDetail((current) => {
      if (!current?.storePrice) {
        return current;
      }

      const retailPrice = current.storePrice.retailPrice ?? null;
      const boundedDiscountedRetail =
        discountedRetail == null || retailPrice == null || retailPrice <= 0
          ? discountedRetail
          : clamp(discountedRetail, 0, retailPrice);
      const discountRate = getDiscountRateFromDiscountedRetail(retailPrice, boundedDiscountedRetail);

      return {
        ...current,
        storePrice: {
          ...current.storePrice,
          discountRate,
        },
      };
    });
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
          discountRate: normalizeDiscountRateValue(detail.storePrice.discountRate),
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

  const handlePrint = useCallback(
    async (kind: "product" | "discount" | "clearance") => {
      if (!detail) {
        return;
      }

      if (kind === "discount" && !(detail.storePrice?.discountRate && detail.storePrice.discountRate > 0)) {
        setSnackbarMessage(t("messages.discountPrintUnavailable"));
        return;
      }

      if (kind === "clearance" && !detail.clearancePrice) {
        setSnackbarMessage(t("messages.clearancePrintUnavailable"));
        return;
      }

      setPrintingKind(kind);
      try {
        if (kind === "product") {
          await sendProductLabel(detail);
          return;
        } else if (kind === "discount") {
          await printDiscountLabel(detail);
        } else {
          await printClearanceLabel(detail);
        }
        setSnackbarMessage(t("messages.printSuccess"));
      } catch (error) {
        const fallback = t("messages.printFailed");
        setSnackbarMessage(error instanceof Error ? `${fallback}: ${error.message}` : fallback);
      } finally {
        if (kind !== "product") {
          setPrintingKind(null);
        }
      }
    },
    [detail, sendProductLabel, t]
  );

  const storePrice = detail?.storePrice;
  const clearancePrice = detail?.clearancePrice;
  const normalizedStoreDiscountRate = normalizeDiscountRateValue(storePrice?.discountRate);
  const discountedRetailPrice = getDiscountedRetailPrice(
    storePrice?.retailPrice,
    normalizedStoreDiscountRate
  );

  return (
    <SafeAreaView style={styles.safeArea} edges={["top", "left", "right"]}>
      <QueryHeader
        storeName={selectedStore?.storeName}
        onScanPress={() => setCameraVisible(true)}
        onRefreshPress={() => void handleRefresh()}
        refreshing={refreshing}
      />

      <SearchPanel
        continuousPrint={continuousPrintEnabled}
        value={keyword}
        loading={loading || storesLoading}
        lastHitLabel={detail ? undefined : lastHitLabel}
        onChangeText={setKeyword}
        onToggleContinuousPrint={setContinuousPrintEnabled}
        onSubmit={() => void handleLookup()}
        onClear={handleClear}
      />

      <ScrollView contentContainerStyle={styles.content}>
        {detail ? (
          <>
            <View style={styles.firstScreenSection}>
              <ProductHeroCard
                imageUrl={detail.productImage}
                isPrintingProductLabel={printingKind === "product"}
                onPrintProductLabel={() => void handlePrint("product")}
                productName={detail.productName}
                supplierName={detail.localSupplierName}
                supplierCode={detail.localSupplierCode}
                itemNumber={detail.itemNumber}
                barcode={detail.barcode}
                productType={detail.productType}
                grade={detail.grade}
              />

              {storePrice ? (
                <StorePriceStrategyCard
                  storeName={storePrice.storeName}
                  purchasePrice={formatDecimal(storePrice.purchasePrice)}
                  retailPrice={formatDecimal(storePrice.retailPrice)}
                  discountPercent={formatPercentValue(normalizedStoreDiscountRate)}
                  discountedRetailPrice={formatCurrency(discountedRetailPrice)}
                  autoPricing={storePrice.isAutoPricing}
                  isSpecialProduct={storePrice.isSpecialProduct}
                  rate={storePrice.rate == null ? "" : String(storePrice.rate)}
                  strategySourceLabel={storePrice.strategySourceLabel}
                  strategyRuleLabel={storePrice.strategyRuleLabel}
                  onChangePurchasePrice={(value) =>
                    handleChangeStorePrice({
                      purchasePrice: parseDecimalInput(value),
                    })
                  }
                  onChangeRetailPrice={handleChangeStoreRetailPrice}
                  onChangeDiscountPercent={handleChangeStoreDiscountPercent}
                  onChangeDiscountedRetailPrice={handleChangeStoreDiscountedRetailPrice}
                  onToggleAutoPricing={(value) => handleChangeStorePrice({ isAutoPricing: value })}
                  onToggleSpecial={(value) => handleChangeStorePrice({ isSpecialProduct: value })}
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

              <Card style={styles.printCard} mode="contained">
                <Card.Content style={styles.printCardContent}>
                  <Text variant="titleSmall" style={styles.printTitle}>
                    {t("print.title")}
                  </Text>
                  <View style={styles.printActions}>
                    <Button
                      mode="outlined"
                      onPress={() => void handlePrint("discount")}
                      loading={printingKind === "discount"}
                      disabled={Boolean(printingKind) || !(storePrice?.discountRate && storePrice.discountRate > 0)}
                    >
                      {printingKind === "discount" ? t("print.sending") : t("print.discount")}
                    </Button>
                    <Button
                      mode="outlined"
                      onPress={() => void handlePrint("clearance")}
                      loading={printingKind === "clearance"}
                      disabled={Boolean(printingKind) || !clearancePrice}
                    >
                      {printingKind === "clearance" ? t("print.sending") : t("print.clearance")}
                    </Button>
                  </View>
                </Card.Content>
              </Card>
            </View>

            {detail.setCodes.length || detail.multiCodes.length ? (
              <View style={styles.secondarySection}>
                <Text variant="titleSmall" style={styles.secondaryTitle}>
                  {t("sections.moreInfo")}
                </Text>
                <SetCodeCompactSection items={detail.setCodes} />
                <MultiCodeCompactList
                  items={detail.multiCodes}
                  onChangeItem={handleChangeMultiCode}
                  onSaveItem={(uuid) => void handleSaveMultiCode(uuid)}
                  savingItemId={savingItemId}
                />
              </View>
            ) : null}
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
    return <SafeAreaView style={styles.safeArea} edges={["top", "left", "right"]} />;
  }

  return <ProductQueryContent />;
}

const styles = StyleSheet.create({
  safeArea: {
    flex: 1,
    backgroundColor: "#F7F8FA",
  },
  content: {
    paddingHorizontal: 12,
    paddingTop: 0,
    paddingBottom: 16,
    gap: 6,
  },
  hiddenInput: {
    position: "absolute",
    width: 1,
    height: 1,
    opacity: 0,
  },
  firstScreenSection: {
    gap: 6,
  },
  secondarySection: {
    gap: 8,
    paddingTop: 4,
  },
  secondaryTitle: {
    fontWeight: "700",
    color: "#111827",
    paddingHorizontal: 4,
  },
  emptyBlock: {
    borderRadius: 8,
    backgroundColor: "#fff",
    padding: 14,
    gap: 6,
  },
  emptyTitle: {
    fontWeight: "700",
  },
  emptyText: {
    color: "#555",
  },
  printCard: {
    borderRadius: 8,
    backgroundColor: "#FFFDF7",
  },
  printCardContent: {
    gap: 8,
    paddingVertical: 6,
  },
  printTitle: {
    fontWeight: "700",
    color: "#111827",
  },
  printActions: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 6,
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
