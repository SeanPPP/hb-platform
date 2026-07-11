import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { ScrollView, StyleSheet, TextInput, View } from "react-native";
import { CameraView } from "expo-camera";
import { useLocalSearchParams, useRouter } from "expo-router";
import { useFocusEffect, useIsFocused } from "@react-navigation/native";
import { Button, Card, Modal, Portal, RadioButton, Snackbar, Switch, Text } from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import { LookupResultSheet } from "@/components/product-maintenance/LookupResultSheet";
import { LabelPrintCard } from "@/components/product-maintenance/LabelPrintCard";
import { PrintSettingsModal } from "@/components/product-maintenance/PrintSettingsModal";
import { MultiCodeCompactList } from "@/components/product-maintenance/MultiCodeCompactList";
import { NumericInputModal } from "@/components/product-maintenance/NumericInputModal";
import { ProductHeroCard } from "@/components/product-maintenance/ProductHeroCard";
import { QueryHeader } from "@/components/product-maintenance/QueryHeader";
import { SearchPanel } from "@/components/product-maintenance/SearchPanel";
import { SetCodeCompactSection } from "@/components/product-maintenance/SetCodeCompactSection";
import { StickyActionBar } from "@/components/product-maintenance/StickyActionBar";
import { StoreClearancePriceCard } from "@/components/product-maintenance/StoreClearancePriceCard";
import { StorePriceStrategyCard } from "@/components/product-maintenance/StorePriceStrategyCard";
import { WarehousePriceSyncModal } from "@/components/product-maintenance/WarehousePriceSyncModal";
import { CameraScanModeSelector } from "@/components/ui/CameraScanModeSelector";
import { StorePickerModal } from "@/components/ui/StorePickerModal";
import {
  getSavedPrinter,
  printBigDiscountLabel,
  printClearanceLabel,
  printDiscountLabel,
  printProductLabel,
} from "@/modules/printer/api";
import { usePrinterStore } from "@/modules/printer/state";
import { resolveLocalizedErrorMessage } from "@/shared/i18n/error-message";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import {
  createProductWithPrices,
  createSetCode,
  evaluateAutoPricing,
  fetchActiveLocalSuppliers,
  getProductCodes,
  getProductFastDetail,
  lookupProducts,
  syncWarehousePrice,
  updateSetCode,
  updateProductType,
  updateStorePrice,
  upsertClearancePrice,
} from "@/modules/product-maintenance/api";
import { resolveExternalQueryStore } from "@/modules/product-maintenance/external-query-store";
import { validateCreateProductForm } from "@/modules/product-maintenance/create-product-validation";
import type {
  EvaluateAutoPricingResult,
  LocalSupplierOption,
  MultiCodeEditableItem,
  ProductDetail,
  ProductLookupItem,
  ProductSetCodeItem,
} from "@/modules/product-maintenance/types";
import {
  buildWarehousePriceSyncRequest,
  createWarehousePriceSyncState,
  extractWarehousePriceSyncConflict,
  getWarehousePriceSyncApplicability,
  isProductQueryInteractionBlocked,
  isWarehousePriceConflictSnapshotComplete,
  isWarehousePriceInteractionLocked,
  reduceWarehousePriceSyncState,
  resolveWarehousePriceConfirmationFeedback,
  shouldAutoPrintWarehousePrice,
  type WarehousePriceLookupOrigin,
  type WarehousePriceSyncState,
} from "@/modules/product-maintenance/warehouse-price-sync";
import { useCameraScan, type CameraScanMode } from "@/modules/scanner/use-camera-scan";
import { isAxiosError } from "axios";
import { useHidBarcodeScanner } from "@/modules/scanner/use-hid-barcode-scanner";
import {
  playBarcodeCapturedSound,
  playScanFeedbackSound,
  preloadScanFeedbackSounds,
} from "@/modules/scanner/scan-sound";
import type { ScanSource } from "@/modules/scanner/types";
import { useStores } from "@/modules/shop/use-stores";
import type { Store } from "@/modules/shop/types";
import { useAuthStore } from "@/store/auth-store";
import {
  buildLocalSupplierInvoicesRestoreHref,
  decodeLocalSupplierInvoicesReturnParams,
} from "@/modules/local-supplier-invoices/navigation";

type LookupTrigger = "manual" | "scan" | "refresh" | "deep-link";

function cloneDetail(detail: ProductDetail | null): ProductDetail | null {
  return detail ? JSON.parse(JSON.stringify(detail)) : null;
}

function formatCurrency(value?: number | null) {
  return value == null ? "" : value.toFixed(2);
}

function formatFixedDecimal(value?: number | null) {
  return value == null || !Number.isFinite(value) ? "" : value.toFixed(2);
}

function clamp(value: number, min: number, max: number) {
  return Math.min(Math.max(value, min), max);
}

function calcGpPercent(sellPrice?: number | null, purchasePrice?: number | null): string {
  if (sellPrice == null || !Number.isFinite(sellPrice) || sellPrice <= 0) return "";
  if (purchasePrice == null || !Number.isFinite(purchasePrice) || purchasePrice < 0) return "";
  const gp = ((sellPrice - purchasePrice) / sellPrice) * 100;
  if (!Number.isFinite(gp)) return "";
  return gp.toFixed(0) + "%";
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

function firstParam(value: string | string[] | undefined) {
  const raw = Array.isArray(value) ? value[0] : value;
  const trimmed = raw?.trim();
  return trimmed || undefined;
}

function toFixedDecimalInput(value: string) {
  const numeric = parseDecimalInput(value);
  return {
    numeric,
    display: numeric == null ? "" : numeric.toFixed(2),
  };
}

function isStorePriceDirty(current: ProductDetail | null, initial: ProductDetail | null) {
  const left = current?.storePrice;
  const right = initial?.storePrice;
  return JSON.stringify(left ?? null) !== JSON.stringify(right ?? null);
}

function replaceStorePriceDetail(
  detail: ProductDetail,
  storePrice: NonNullable<ProductDetail["storePrice"]>
) {
  return {
    ...detail,
    storePrice,
  };
}

type PrintAction =
  | "product"
  | "discount"
  | "clearance"
  | "bigDiscount"
  | `set:${string}`
  | `multi:${string}`;

type QueryFeedback =
  | { type: "idle" }
  | { type: "empty"; query: string }
  | { type: "error"; query?: string; message: string };

type AutoPricingFlowStatus =
  | "no_action"
  | "saved_without_prompt"
  | "prompt_confirmed"
  | "prompt_cancelled"
  | "failed";

interface LookupFlowResult {
  keepCameraOpen: boolean;
  labelPrinted: boolean;
  autoPricingStatus: AutoPricingFlowStatus;
}

interface AutoPricingDialogState {
  detail: ProductDetail;
  evaluation: EvaluateAutoPricingResult;
  scanSource: ScanSource | null;
}

interface AutoPricingDialogResolution {
  status: "confirmed" | "cancelled" | "failed";
  keepCameraOpen: boolean;
  labelPrinted: boolean;
  updatedDetail?: ProductDetail | null;
}

interface DetailPostLoadOptions {
  lookupOrigin: WarehousePriceLookupOrigin;
  storeCodeOverride?: string;
  scanSource?: ScanSource | null;
  scanKeyword?: string;
  autoPrintEnabled?: boolean;
}

interface WarehousePriceSyncContext {
  detail: ProductDetail;
  lookupOrigin: WarehousePriceLookupOrigin;
  storeCodeOverride?: string;
  scanSource: ScanSource | null;
  scanKeyword: string;
  autoPrintEnabled: boolean;
  alreadyPrinted: boolean;
}

interface NumericInputModalState {
  key: string;
  title: string;
  value: string;
  allowDecimal: boolean;
  confirmLabel?: string;
}

interface BarcodeEditModalState {
  key: string;
  title: string;
  value: string;
  targetId: string;
  codeType: "set" | "multi";
}

interface CodeAddModalState {
  codeType: "set" | "multi";
  value: string;
}

interface CreateProductDraft {
  localSupplierCode: string;
  itemNumber: string;
  barcode: string;
  productName: string;
  purchasePrice: string;
  retailPrice: string;
  isSpecialProduct: boolean;
  isAutoPricing: boolean;
}

const PRODUCT_TYPE_OPTIONS = [0, 1, 2] as const;
const CODE_PAGE_SIZE = 50;
const EMPTY_CREATE_PRODUCT_DRAFT: CreateProductDraft = {
  localSupplierCode: "",
  itemNumber: "",
  barcode: "",
  productName: "",
  purchasePrice: "",
  retailPrice: "",
  isSpecialProduct: false,
  isAutoPricing: false,
};

const DEFAULT_LOOKUP_FLOW_RESULT: LookupFlowResult = {
  keepCameraOpen: false,
  labelPrinted: false,
  autoPricingStatus: "no_action",
};

function ProductQueryContent() {
  const { t, language } = useAppTranslation(["productQuery", "common"]);
  const router = useRouter();
  const queryParams = useLocalSearchParams<{
    productCode?: string | string[];
    keyword?: string | string[];
    storeCode?: string | string[];
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
  const {
    stores,
    selectedStore,
    selectedStoreCode,
    selectStore,
    isDeviceMode,
    isLoading: storesLoading,
    isHydratingSelection,
  } = useStores();
  const access = useAuthStore((state) => state.access);
  const printerAutoReconnectPaused = usePrinterStore((state) => state.autoReconnectPaused);
  const [keyword, setKeyword] = useState("");
  const [lookupItems, setLookupItems] = useState<ProductLookupItem[]>([]);
  const [selectedLookupProductCode, setSelectedLookupProductCode] = useState<string>();
  const [detail, setDetail] = useState<ProductDetail | null>(null);
  const [initialDetail, setInitialDetail] = useState<ProductDetail | null>(null);
  const [lastHitLabel, setLastHitLabel] = useState<string>();
  const [lookupVisible, setLookupVisible] = useState(false);
  const [lookupSelectionSource, setLookupSelectionSource] = useState<ScanSource | null>(null);
  const [cameraVisible, setCameraVisible] = useState(false);
  const [cameraScanMode, setCameraScanMode] = useState<CameraScanMode>("single");
  const [queryFeedback, setQueryFeedback] = useState<QueryFeedback>({ type: "idle" });
  const [loading, setLoading] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [saving, setSaving] = useState(false);
  const [savingItemId, setSavingItemId] = useState<string | null>(null);
  const [snackbarMessage, setSnackbarMessage] = useState("");
  const [printingAction, setPrintingAction] = useState<PrintAction | null>(null);
  const [continuousPrintEnabled, setContinuousPrintEnabled] = useState(false);
  const [autoPrintOnLookupConfirm, setAutoPrintOnLookupConfirm] = useState(false);
  const [autoPricingDialog, setAutoPricingDialog] = useState<AutoPricingDialogState | null>(null);
  const [autoPricingDialogSaving, setAutoPricingDialogSaving] = useState(false);
  const [warehousePriceSyncState, setWarehousePriceSyncState] = useState<WarehousePriceSyncState>(
    createWarehousePriceSyncState
  );
  const [warehousePriceSyncContext, setWarehousePriceSyncContext] =
    useState<WarehousePriceSyncContext | null>(null);
  const [createProductVisible, setCreateProductVisible] = useState(false);
  const [createSupplierPickerVisible, setCreateSupplierPickerVisible] = useState(false);
  const [createProductSaving, setCreateProductSaving] = useState(false);
  const [createSuppliers, setCreateSuppliers] = useState<LocalSupplierOption[]>([]);
  const [createSuppliersLoading, setCreateSuppliersLoading] = useState(false);
  const [createProductDraft, setCreateProductDraft] = useState<CreateProductDraft>(
    EMPTY_CREATE_PRODUCT_DRAFT
  );
  const [productTypeDialogVisible, setProductTypeDialogVisible] = useState(false);
  const [productTypeSaving, setProductTypeSaving] = useState(false);
  const [storePurchaseInput, setStorePurchaseInput] = useState("");
  const [storeRetailInput, setStoreRetailInput] = useState("");
  const [clearancePriceInput, setClearancePriceInput] = useState("");
  const [savingClearance, setSavingClearance] = useState(false);
  const [codesLoading, setCodesLoading] = useState(false);
  const [codesLoadingMore, setCodesLoadingMore] = useState(false);
  const [codePage, setCodePage] = useState(1);
  const [codesHasMore, setCodesHasMore] = useState(false);
  const [barcodeEditModal, setBarcodeEditModal] = useState<BarcodeEditModalState | null>(null);
  const [codeAddModal, setCodeAddModal] = useState<CodeAddModalState | null>(null);
  const [smallLabel, setSmallLabel] = useState(false);
  const [printQuantity, setPrintQuantity] = useState(1);
  const [quantitySingleUse, setQuantitySingleUse] = useState(true);
  const [printSettingsVisible, setPrintSettingsVisible] = useState(false);
  const [storePickerVisible, setStorePickerVisible] = useState(false);
  const getErrorMessage = useCallback((error: unknown, fallbackKey: string) => (
    resolveLocalizedErrorMessage(error, {
      language,
      t,
      fallbackKey,
    })
  ), [language, t]);
  const autoPricingDialogResolverRef = useRef<((result: AutoPricingDialogResolution) => void) | null>(null);
  const numericInputConfirmRef = useRef<((value: string) => void) | null>(null);
  const lookupSelectionOpenRef = useRef(false);
  const lookupRequestInFlightRef = useRef(false);
  const storeSelectionInFlightRef = useRef(false);
  const warehousePriceRequestInFlightRef = useRef(false);
  const resumeHiddenScannerFocusTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const searchInputFocusedRef = useRef(false);
  const processLoadedDetailRef = useRef<
    ((targetDetail: ProductDetail, options: DetailPostLoadOptions) => Promise<LookupFlowResult>) | null
  >(null);
  const saveClearanceRef = useRef<() => Promise<void>>(async () => {});
  const handledExternalQueryRef = useRef<string | null>(null);
  const [numericInputModal, setNumericInputModal] = useState<NumericInputModalState | null>(null);
  const warehousePriceInteractionLocked = isWarehousePriceInteractionLocked(
    warehousePriceSyncState
  );
  const isProductQueryBusy = useCallback(
    () =>
      isProductQueryInteractionBlocked({
        loading,
        lookupVisible,
        lookupSelectionOpen: lookupSelectionOpenRef.current,
        autoPricingVisible: Boolean(autoPricingDialog),
        autoPricingSaving: autoPricingDialogSaving,
        warehouseLocked: warehousePriceInteractionLocked,
        requestInFlight:
          lookupRequestInFlightRef.current || warehousePriceRequestInFlightRef.current,
        storeSelectionInFlight: storeSelectionInFlightRef.current,
      }),
    [
      autoPricingDialog,
      autoPricingDialogSaving,
      loading,
      lookupVisible,
      warehousePriceInteractionLocked,
    ]
  );
  const invoiceReturnState = useMemo(
    () => decodeLocalSupplierInvoicesReturnParams(queryParams),
    [
      queryParams.returnDetailsPage,
      queryParams.returnDetailsPageSize,
      queryParams.returnFilterInvoiceNo,
      queryParams.returnFilterOrderDateFrom,
      queryParams.returnFilterOrderDateTo,
      queryParams.returnFilterStoreCode,
      queryParams.returnFilterSupplierCode,
      queryParams.returnInvoiceGuid,
      queryParams.returnListPage,
      queryParams.returnListPageSize,
      queryParams.returnSortColId,
      queryParams.returnSortDirection,
      queryParams.source,
    ]
  );

  useEffect(() => {
    setStorePurchaseInput(formatFixedDecimal(detail?.storePrice?.purchasePrice));
    setStoreRetailInput(formatFixedDecimal(detail?.storePrice?.retailPrice));
  }, [detail?.storePrice?.purchasePrice, detail?.storePrice?.retailPrice, detail?.storePrice?.uuid]);

  useEffect(() => {
    setClearancePriceInput(formatFixedDecimal(detail?.clearancePrice?.clearancePrice));
  }, [detail?.clearancePrice?.clearancePrice, detail?.clearancePrice?.uuid]);

  useEffect(() => {
    preloadScanFeedbackSounds();
  }, []);

  const playQueryFeedback = useCallback((status: "found" | "multiple" | "not_found" | "error" | "price_update_required") => {
    playScanFeedbackSound(status);
  }, []);

  const loadProductCodes = useCallback(
    async (
      sourceDetail: ProductDetail,
      nextPage = 1,
      append = false,
      storeCodeOverride?: string
    ): Promise<ProductDetail | undefined> => {
      const storeCode = storeCodeOverride ?? selectedStoreCode;
      if (!storeCode) {
        return;
      }

      const hasSetCodes = sourceDetail.setCodeCount > 0;
      const hasMultiCodes = sourceDetail.multiCodeCount > 0;
      if (!hasSetCodes && !hasMultiCodes && sourceDetail.productType !== 1 && sourceDetail.productType !== 2) {
        return;
      }

      if (append) {
        setCodesLoadingMore(true);
      } else {
        setCodesLoading(true);
        setCodePage(1);
        setCodesHasMore(false);
      }

      try {
        if (sourceDetail.productType === 1 || (sourceDetail.productType !== 2 && hasSetCodes && !hasMultiCodes)) {
          const page = await getProductCodes(
            sourceDetail.productCode,
            storeCode,
            1,
            nextPage,
            CODE_PAGE_SIZE
          );
          const applyPage = (current: ProductDetail | null) =>
            current?.productCode === sourceDetail.productCode
              ? {
                  ...current,
                  setCodes: append ? [...current.setCodes, ...page.items] : page.items,
                  setCodeCount: page.totalCount,
                  codesIncluded: true,
                }
              : current;
          setDetail(applyPage);
          setInitialDetail((current) => cloneDetail(applyPage(current)));
          setCodePage(page.page);
          setCodesHasMore(page.hasMore);
          if (!append) {
            return {
              ...sourceDetail,
              setCodes: page.items,
              setCodeCount: page.totalCount,
              codesIncluded: true,
            };
          }
          return;
        }

        const page = await getProductCodes(
          sourceDetail.productCode,
          storeCode,
          2,
          nextPage,
          CODE_PAGE_SIZE
        );
        const applyPage = (current: ProductDetail | null) =>
          current?.productCode === sourceDetail.productCode
            ? {
                ...current,
                multiCodes: append ? [...current.multiCodes, ...page.items] : page.items,
                multiCodeCount: page.totalCount,
                codesIncluded: true,
              }
            : current;
        setDetail(applyPage);
        setInitialDetail((current) => cloneDetail(applyPage(current)));
        setCodePage(page.page);
        setCodesHasMore(page.hasMore);
        if (!append) {
          return {
            ...sourceDetail,
            multiCodes: page.items,
            multiCodeCount: page.totalCount,
            codesIncluded: true,
          };
        }
      } catch (error) {
        const fallback = t("messages.codesLoadFailed");
        let detail = "";
        if (isAxiosError(error) && !error.response) {
          detail = error.code === "ECONNABORTED"
            ? t("messages.lookupTimeout")
            : t("messages.lookupNetworkError");
        } else {
          detail = getErrorMessage(error, "messages.codesLoadFailed");
        }
        setSnackbarMessage(detail ? `${fallback}: ${detail}` : fallback);
        playQueryFeedback("error");
      } finally {
        setCodesLoading(false);
        setCodesLoadingMore(false);
      }
    },
    [playQueryFeedback, selectedStoreCode, t]
  );

  const loadDetail = useCallback(
    async (productCode: string, storeCodeOverride?: string) => {
      const targetStoreCode = storeCodeOverride ?? selectedStoreCode;
      if (!targetStoreCode) {
        setSnackbarMessage(t("messages.selectStoreFirst"));
        return null;
      }

      console.log("[product-query] load detail", { productCode, selectedStoreCode: targetStoreCode });
      const payload = await getProductFastDetail(productCode, targetStoreCode);
      setDetail(payload);
      setInitialDetail(cloneDetail(payload));
      setSelectedLookupProductCode(productCode);
      setLastHitLabel(`${payload.itemNumber || payload.productCode} / ${payload.barcode || "--"}`);
      setQueryFeedback({ type: "idle" });
      setCodePage(1);
      setCodesHasMore(false);
      const detailWithCodes = await loadProductCodes(payload, 1, false, targetStoreCode);
      return detailWithCodes ?? payload;
    },
    [loadProductCodes, selectedStoreCode, t]
  );

  const canSelectStore = !isDeviceMode && stores.length > 0 && !isProductQueryBusy();

  const handleSelectStore = useCallback(
    async (store: Store | null) => {
      if (!store || isProductQueryBusy()) {
        return;
      }

      storeSelectionInFlightRef.current = true;
      setLoading(true);
      setStorePickerVisible(false);
      try {
        await selectStore(store);

        if (detail?.productCode) {
          const nextDetail = await loadDetail(detail.productCode, store.storeCode);
          if (nextDetail) {
            await processLoadedDetailRef.current?.(nextDetail, {
              lookupOrigin: "refresh",
              storeCodeOverride: store.storeCode,
            });
          }
        }
      } catch (error) {
        if (detail?.productCode) {
          setDetail(null);
          setInitialDetail(null);
          setSelectedLookupProductCode(undefined);
          setCodePage(1);
          setCodesHasMore(false);
        }
        setSnackbarMessage(getErrorMessage(error, "messages.refreshFailed"));
        playQueryFeedback("error");
      } finally {
        storeSelectionInFlightRef.current = false;
        setLoading(false);
      }
    },
    [detail?.productCode, getErrorMessage, isProductQueryBusy, loadDetail, playQueryFeedback, selectStore]
  );

  const selectedCreateSupplier = useMemo(
    () =>
      createSuppliers.find(
        (supplier) => supplier.supplierCode === createProductDraft.localSupplierCode
      ) ?? null,
    [createProductDraft.localSupplierCode, createSuppliers]
  );

  const loadCreateSuppliers = useCallback(async () => {
    setCreateSuppliersLoading(true);
    try {
      const suppliers = await fetchActiveLocalSuppliers();
      setCreateSuppliers(suppliers);
      setCreateProductDraft((current) =>
        current.localSupplierCode || !suppliers[0]
          ? current
          : { ...current, localSupplierCode: suppliers[0].supplierCode }
      );
    } catch (error) {
      setSnackbarMessage(getErrorMessage(error, "createProduct.messages.suppliersLoadFailed"));
    } finally {
      setCreateSuppliersLoading(false);
    }
  }, [getErrorMessage]);

  const openCreateProductModal = useCallback(() => {
    setCreateProductDraft({
      ...EMPTY_CREATE_PRODUCT_DRAFT,
      localSupplierCode: createSuppliers[0]?.supplierCode ?? "",
    });
    setCreateProductVisible(true);
    if (!createSuppliers.length) {
      void loadCreateSuppliers();
    }
  }, [createSuppliers, loadCreateSuppliers]);

  const updateCreateProductDraft = useCallback(
    (patch: Partial<CreateProductDraft>) => {
      setCreateProductDraft((current) => ({ ...current, ...patch }));
    },
    []
  );

  const closeCreateProductModal = useCallback(() => {
    setCreateSupplierPickerVisible(false);
    setCreateProductVisible(false);
  }, []);

  const handleSelectCreateSupplier = useCallback(
    (supplier: LocalSupplierOption) => {
      updateCreateProductDraft({ localSupplierCode: supplier.supplierCode });
      setCreateSupplierPickerVisible(false);
    },
    [updateCreateProductDraft]
  );

  const handleCreateProductSubmit = useCallback(async () => {
    const validation = validateCreateProductForm(createProductDraft);
    if (!validation.ok) {
      setSnackbarMessage(t(`createProduct.messages.${validation.reason}`));
      return;
    }

    setCreateProductSaving(true);
    let createdProductCode = "";
    try {
      const result = await createProductWithPrices(validation.payload);
      const nextKeyword =
        result.productCode || validation.payload.itemNumber || validation.payload.barcode;
      createdProductCode = result.productCode;
      setCreateSupplierPickerVisible(false);
      setCreateProductVisible(false);
      setCreateProductDraft(EMPTY_CREATE_PRODUCT_DRAFT);
      setKeyword(nextKeyword);
      setSnackbarMessage(t("createProduct.messages.created"));
    } catch (error) {
      setSnackbarMessage(getErrorMessage(error, "createProduct.messages.createFailed"));
      setCreateProductSaving(false);
      return;
    }

    if (createdProductCode) {
      try {
        await loadDetail(createdProductCode);
      } catch (error) {
        setSnackbarMessage(getErrorMessage(error, "createProduct.messages.refreshFailedAfterCreate"));
      }
    }

    setCreateProductSaving(false);
  }, [createProductDraft, getErrorMessage, loadDetail, t]);

  const persistStorePrice = useCallback(
    async (
      sourceDetail: ProductDetail,
      patch: Partial<NonNullable<ProductDetail["storePrice"]>>
    ) => {
      if (!sourceDetail.storePrice) {
        return null;
      }

      const nextStorePrice = {
        ...sourceDetail.storePrice,
        ...patch,
      };

      try {
        const savedStorePrice = await updateStorePrice(sourceDetail.storePrice.uuid, {
          purchasePrice: nextStorePrice.purchasePrice ?? null,
          retailPrice: nextStorePrice.retailPrice ?? null,
          discountRate: normalizeDiscountRateValue(nextStorePrice.discountRate),
          isAutoPricing: nextStorePrice.isAutoPricing,
          isSpecialProduct: nextStorePrice.isSpecialProduct,
          isActive: nextStorePrice.isActive,
        });

        if (selectedStoreCode) {
          const refreshed = await getProductFastDetail(sourceDetail.productCode, selectedStoreCode);
          setDetail(refreshed);
          setInitialDetail(cloneDetail(refreshed));
          void loadProductCodes(refreshed, 1, false);
          return refreshed;
        }

        const nextDetail = replaceStorePriceDetail(sourceDetail, savedStorePrice);
        setDetail(nextDetail);
        setInitialDetail(cloneDetail(nextDetail));
        return nextDetail;
      } catch (error) {
        setSnackbarMessage(getErrorMessage(error, "messages.autoPricingUpdateFailed"));
        playQueryFeedback("error");
        return null;
      }
    },
    [getErrorMessage, loadProductCodes, playQueryFeedback, selectedStoreCode]
  );

  const finishAutoPricingDialog = useCallback((result: AutoPricingDialogResolution) => {
    setAutoPricingDialog(null);
    setAutoPricingDialogSaving(false);
    const resolve = autoPricingDialogResolverRef.current;
    autoPricingDialogResolverRef.current = null;
    resolve?.(result);
  }, []);

  const openNumericInputModal = useCallback(
    (config: NumericInputModalState & { onConfirmValue: (value: string) => void }) => {
      numericInputConfirmRef.current = config.onConfirmValue;
      setNumericInputModal({
        key: config.key,
        title: config.title,
        value: config.value,
        allowDecimal: config.allowDecimal,
        confirmLabel: config.confirmLabel,
      });
    },
    []
  );

  const dismissNumericInputModal = useCallback(() => {
    numericInputConfirmRef.current = null;
    setNumericInputModal(null);
  }, []);

  const handleConfirmNumericInputModal = useCallback(() => {
    if (!numericInputModal) {
      return;
    }

    numericInputConfirmRef.current?.(numericInputModal.value);
    dismissNumericInputModal();
  }, [dismissNumericInputModal, numericInputModal]);

  const openAutoPricingDialog = useCallback(
    (state: AutoPricingDialogState) =>
      new Promise<AutoPricingDialogResolution>((resolve) => {
        autoPricingDialogResolverRef.current = resolve;
        setAutoPricingDialog(state);
      }),
    []
  );

  const sendProductLabel = useCallback(
    async (
      targetDetail: ProductDetail,
      options?: {
        barcode?: string | null;
        retailPrice?: number | null;
        action?: PrintAction;
        printType?: string | null;
      }
    ) => {
      const savedPrinter = await getSavedPrinter();
      if (!savedPrinter?.address) {
        setSnackbarMessage(t("messages.printerRequired"));
        playQueryFeedback("error");
        return false;
      }

      if (printerAutoReconnectPaused) {
        setSnackbarMessage(t("messages.printerPaused"));
        playQueryFeedback("error");
        return false;
      }

      const action = options?.action ?? "product";
      setPrintingAction(action);
      try {
        console.log("[product-query] sendProductLabel", {
          productCode: targetDetail.productCode,
          action,
          printType: options?.printType,
          barcode: options?.barcode,
          retailPrice: options?.retailPrice,
        });
        await printProductLabel(targetDetail, {
          barcode: options?.barcode,
          retailPrice: options?.retailPrice,
        }, options?.printType);
        const qty = printQuantity;
        for (let i = 1; i < qty; i++) {
          await printProductLabel(targetDetail, {
            barcode: options?.barcode,
            retailPrice: options?.retailPrice,
          }, options?.printType);
        }
        console.log("[product-query] sendProductLabel success", { action, quantity: qty });
        setSnackbarMessage(t("messages.printSuccess"));
        if (quantitySingleUse && qty > 1) {
          setPrintQuantity(1);
        }
        return true;
      } catch (error) {
        console.error("[product-query] sendProductLabel failed", {
          action,
          printType: options?.printType,
          error: error instanceof Error ? error.message : String(error),
          stack: error instanceof Error ? error.stack : undefined,
        });
        setSnackbarMessage(getErrorMessage(error, "messages.printFailed"));
        playQueryFeedback("error");
        return false;
      } finally {
        setPrintingAction(null);
      }
    },
    [getErrorMessage, playQueryFeedback, printQuantity, printerAutoReconnectPaused, quantitySingleUse, t]
  );

  const smartAutoPrint = useCallback(
    async (scanKeyword: string, targetDetail: ProductDetail) => {
      const kw = scanKeyword.trim();

      const setMatch = targetDetail.setCodes.find(
        (item) => item.setBarcode?.trim() === kw,
      );
      if (setMatch?.setBarcode?.trim()) {
        return sendProductLabel(targetDetail, {
          barcode: setMatch.setBarcode.trim(),
          retailPrice: setMatch.setRetailPrice,
          action: `set:${setMatch.setCodeId}`,
          printType: smallLabel ? "small" : null,
        });
      }

      const multiMatch = targetDetail.multiCodes.find(
        (item) => item.barcode?.trim() === kw,
      );
      if (multiMatch?.barcode?.trim()) {
        return sendProductLabel(targetDetail, {
          barcode: multiMatch.barcode.trim(),
          retailPrice:
            multiMatch.retailPrice ??
            targetDetail.storePrice?.retailPrice ??
            null,
          action: `multi:${multiMatch.setCodeId}`,
          printType: smallLabel ? "small" : null,
        });
      }

      if (
        targetDetail.clearancePrice?.clearanceBarcode?.trim() === kw
      ) {
        try {
          setPrintingAction("clearance");
          for (let i = 0; i < printQuantity; i++) {
            await printClearanceLabel(targetDetail);
          }
          setSnackbarMessage(t("messages.printSuccess"));
          if (quantitySingleUse && printQuantity > 1) {
            setPrintQuantity(1);
          }
          return true;
        } catch (error) {
          setSnackbarMessage(getErrorMessage(error, "messages.printFailed"));
          return false;
        } finally {
          setPrintingAction(null);
        }
      }

      return sendProductLabel(targetDetail);
    },
    [getErrorMessage, printQuantity, quantitySingleUse, sendProductLabel, smallLabel, t],
  );

  const maybeHandleAutoPricing = useCallback(
    async (
      targetDetail: ProductDetail,
      options?: {
        forceAutoPricing?: boolean;
        scanSource?: ScanSource | null;
      }
    ): Promise<LookupFlowResult> => {
      const storePrice = targetDetail.storePrice;
      const scanSource = options?.scanSource ?? null;
      if (!storePrice || !selectedStoreCode) {
        return DEFAULT_LOOKUP_FLOW_RESULT;
      }

      const shouldEvaluate = options?.forceAutoPricing === true || storePrice.isAutoPricing;
      if (!shouldEvaluate) {
        return DEFAULT_LOOKUP_FLOW_RESULT;
      }

      try {
        const evaluation = await evaluateAutoPricing({
          productCode: targetDetail.productCode,
          storeCode: storePrice.storeCode ?? selectedStoreCode,
          forceAutoPricing: options?.forceAutoPricing === true,
        });

        if (evaluation.shouldUpdate && evaluation.recalculatedRetailPrice != null) {
          playQueryFeedback("price_update_required");
          const dialogResult = await openAutoPricingDialog({
            detail: targetDetail,
            evaluation,
            scanSource,
          });

          return {
            keepCameraOpen: dialogResult.keepCameraOpen,
            labelPrinted: dialogResult.labelPrinted,
            autoPricingStatus:
              dialogResult.status === "confirmed"
                ? "prompt_confirmed"
                : dialogResult.status === "failed"
                  ? "failed"
                  : "prompt_cancelled",
          };
        }

        if (options?.forceAutoPricing === true) {
          const savedDetail = await persistStorePrice(targetDetail, { isAutoPricing: true });
          return savedDetail
            ? {
                keepCameraOpen: false,
                labelPrinted: false,
                autoPricingStatus: "saved_without_prompt",
              }
            : {
                keepCameraOpen: false,
                labelPrinted: false,
                autoPricingStatus: "failed",
              };
        }

        return DEFAULT_LOOKUP_FLOW_RESULT;
      } catch (error) {
        setSnackbarMessage(getErrorMessage(error, "messages.autoPricingEvaluateFailed"));
        playQueryFeedback("error");
        return {
          keepCameraOpen: false,
          labelPrinted: false,
          autoPricingStatus: "failed",
        };
      }
    },
    [getErrorMessage, openAutoPricingDialog, persistStorePrice, playQueryFeedback, selectedStoreCode]
  );

  const processLoadedDetail = useCallback(
    async (
      targetDetail: ProductDetail,
      options: DetailPostLoadOptions
    ): Promise<LookupFlowResult> => {
      const applicability = getWarehousePriceSyncApplicability(
        targetDetail.localSupplierCode,
        targetDetail.storePrice?.uuid
      );
      if (applicability === "not_supplier") {
        const autoPricingResult = await maybeHandleAutoPricing(targetDetail, {
          scanSource: options.scanSource,
        });
        if (autoPricingResult.autoPricingStatus === "no_action") {
          playQueryFeedback("found");
          if (options.autoPrintEnabled && !autoPricingResult.labelPrinted) {
            const labelPrinted = await smartAutoPrint(options.scanKeyword ?? "", targetDetail);
            return { ...autoPricingResult, labelPrinted };
          }
        }
        return autoPricingResult;
      }

      // 供应商 200 本轮只走仓库权威价对账，明确跳过现有自动定价评估。
      const currentStorePrice = targetDetail.storePrice;
      if (applicability === "missing_store_price" || !currentStorePrice?.uuid) {
        playQueryFeedback("found");
        // 缺少目标分店价时无法证明标签价格来自仓库，禁止自动打印旧标签。
        return DEFAULT_LOOKUP_FLOW_RESULT;
      }

      if (warehousePriceRequestInFlightRef.current) {
        return DEFAULT_LOOKUP_FLOW_RESULT;
      }
      warehousePriceRequestInFlightRef.current = true;

      setWarehousePriceSyncState((current) =>
        reduceWarehousePriceSyncState(current, { type: "preview_started" })
      );
      try {
        const snapshot = await syncWarehousePrice(currentStorePrice.uuid, {
          confirmRetailPrice: false,
          expectedWarehousePurchasePrice: null,
          expectedWarehouseRetailPrice: null,
          expectedStorePurchasePrice: currentStorePrice.purchasePrice ?? null,
          expectedStoreRetailPrice: currentStorePrice.retailPrice ?? null,
          expectedDiscountRate: normalizeDiscountRateValue(currentStorePrice.discountRate),
        });

        let latestDetail = snapshot.storePrice
          ? replaceStorePriceDetail(targetDetail, snapshot.storePrice)
          : targetDetail;

        if (snapshot.purchaseUpdated) {
          // 后端同时更新派生条码价格；重新读取一次，确保主价和条码列表都使用同一新快照。
          latestDetail =
            (await loadDetail(targetDetail.productCode, options.storeCodeOverride)) ?? latestDetail;
          setSnackbarMessage(t("warehousePriceSync.purchaseUpdated"));
        } else if (snapshot.storePrice) {
          setDetail(latestDetail);
          setInitialDetail(cloneDetail(latestDetail));
        }

        setWarehousePriceSyncState((current) =>
          reduceWarehousePriceSyncState(current, { type: "preview_succeeded", snapshot })
        );

        if (snapshot.retailConfirmationRequired) {
          setWarehousePriceSyncContext({
            detail: latestDetail,
            lookupOrigin: options.lookupOrigin,
            storeCodeOverride: options.storeCodeOverride,
            scanSource: options.scanSource ?? null,
            scanKeyword: options.scanKeyword ?? "",
            autoPrintEnabled: options.autoPrintEnabled === true,
            alreadyPrinted: false,
          });
          playQueryFeedback("price_update_required");
          return DEFAULT_LOOKUP_FLOW_RESULT;
        }

        setWarehousePriceSyncContext(null);
        playQueryFeedback("found");
        const shouldPrint =
          options.autoPrintEnabled === true &&
          shouldAutoPrintWarehousePrice({
            lookupOrigin: options.lookupOrigin,
            stage: "preview_succeeded",
            snapshot,
            alreadyPrinted: false,
          });
        const labelPrinted = shouldPrint
          ? await smartAutoPrint(options.scanKeyword ?? "", latestDetail)
          : false;

        return {
          keepCameraOpen: false,
          labelPrinted,
          autoPricingStatus: "no_action",
        };
      } catch (error) {
        const message = getErrorMessage(error, "warehousePriceSync.previewFailed");
        setWarehousePriceSyncState((current) =>
          reduceWarehousePriceSyncState(current, { type: "preview_failed", message })
        );
        setWarehousePriceSyncContext(null);
        setSnackbarMessage(message);
        playQueryFeedback("error");
        return {
          keepCameraOpen: false,
          labelPrinted: false,
          autoPricingStatus: "failed",
        };
      } finally {
        warehousePriceRequestInFlightRef.current = false;
      }
    },
    [
      getErrorMessage,
      loadDetail,
      maybeHandleAutoPricing,
      playQueryFeedback,
      smartAutoPrint,
      t,
    ]
  );
  processLoadedDetailRef.current = processLoadedDetail;

  const handleLookup = useCallback(
    async (
      sourceKeyword?: string,
      trigger: LookupTrigger = "manual",
      scanSource?: ScanSource
    ): Promise<LookupFlowResult> => {
      if (isProductQueryBusy()) {
        return DEFAULT_LOOKUP_FLOW_RESULT;
      }

      const nextKeyword = (sourceKeyword ?? keyword).trim();
      if (!nextKeyword) {
        setSnackbarMessage(t("messages.keywordRequired"));
        return DEFAULT_LOOKUP_FLOW_RESULT;
      }

      if (!selectedStoreCode) {
        setSnackbarMessage(t("messages.storeUnavailable"));
        return DEFAULT_LOOKUP_FLOW_RESULT;
      }

      lookupRequestInFlightRef.current = true;

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
          setLookupSelectionSource(null);
          lookupSelectionOpenRef.current = false;
          setLookupVisible(false);
          setQueryFeedback({ type: "empty", query: nextKeyword });
          setSnackbarMessage(t("messages.notFound"));
          playQueryFeedback("not_found");
          return DEFAULT_LOOKUP_FLOW_RESULT;
        }

        if (items.length === 1) {
          lookupSelectionOpenRef.current = false;
          setLookupVisible(false);
          setLookupSelectionSource(null);
          const nextDetail = await loadDetail(items[0].productCode);
          if (nextDetail) {
            return processLoadedDetail(nextDetail, {
              lookupOrigin: trigger,
              scanSource,
              scanKeyword: nextKeyword,
              autoPrintEnabled: trigger === "scan" && continuousPrintEnabled,
            });
          }
          return DEFAULT_LOOKUP_FLOW_RESULT;
        }

        setSelectedLookupProductCode(items[0].productCode);
        setAutoPrintOnLookupConfirm(trigger === "scan" && continuousPrintEnabled);
        setLookupSelectionSource(scanSource ?? null);
        lookupSelectionOpenRef.current = true;
        setLookupVisible(true);
        playQueryFeedback("multiple");
        return DEFAULT_LOOKUP_FLOW_RESULT;
      } catch (error) {
        let message: string;
        if (isAxiosError(error)) {
          if (!error.response) {
            message = error.code === "ECONNABORTED"
              ? t("messages.lookupTimeout")
              : t("messages.lookupNetworkError");
          } else {
            const status = error.response.status;
            message = status >= 500
              ? t("messages.lookupServerError", { status })
              : t("messages.lookupFailed");
          }
        } else {
          message = getErrorMessage(error, "messages.lookupFailed");
        }
        console.error("[product-query] lookup failed", {
          keyword: nextKeyword,
          selectedStoreCode,
          message,
        });
        setDetail(null);
        setInitialDetail(null);
        lookupSelectionOpenRef.current = false;
        setLookupVisible(false);
        setQueryFeedback({ type: "error", query: nextKeyword, message });
        setSnackbarMessage(message);
        playQueryFeedback("error");
        return DEFAULT_LOOKUP_FLOW_RESULT;
      } finally {
        lookupRequestInFlightRef.current = false;
        setLoading(false);
      }
    },
    [
      continuousPrintEnabled,
      isProductQueryBusy,
      keyword,
      loadDetail,
      playQueryFeedback,
      processLoadedDetail,
      selectedStoreCode,
      t,
    ]
  );

  useEffect(() => {
    const productCodeParam = firstParam(queryParams.productCode);
    const keywordParam = firstParam(queryParams.keyword);
    const storeCodeParam = firstParam(queryParams.storeCode);
    const sourceParam = firstParam(queryParams.source);
    const nextKeyword = keywordParam || productCodeParam;

    if (!nextKeyword && !storeCodeParam) {
      return;
    }

    const requestKey = `${productCodeParam ?? ""}|${keywordParam ?? ""}|${storeCodeParam ?? ""}|${sourceParam ?? ""}`;
    if (handledExternalQueryRef.current === requestKey || storesLoading) {
      return;
    }

    let cancelled = false;

    async function applyExternalQuery() {
      if (isProductQueryBusy()) {
        return;
      }

      const storeResolution = resolveExternalQueryStore({
        targetStoreCode: storeCodeParam,
        selectedStoreCode,
        stores,
        storesLoading: storesLoading || isHydratingSelection,
      });

      if (storeResolution.type === "wait") {
        return;
      }

      if (storeResolution.type === "select-store") {
        await handleSelectStore(storeResolution.store);
        return;
      }

      if (storeResolution.type === "store-not-found") {
        handledExternalQueryRef.current = requestKey;
        setSnackbarMessage(
          t("messages.targetStoreUnavailable", { storeCode: storeResolution.storeCode })
        );
        playQueryFeedback("error");
        return;
      }

      handledExternalQueryRef.current = requestKey;
      if (!nextKeyword || cancelled) {
        return;
      }

      setKeyword(nextKeyword);
      try {
        if (productCodeParam) {
          lookupRequestInFlightRef.current = true;
          setLoading(true);
          const nextDetail = await loadDetail(productCodeParam);
          if (nextDetail) {
            await processLoadedDetail(nextDetail, { lookupOrigin: "deep-link" });
          }
          return;
        }
        await handleLookup(nextKeyword, "deep-link");
      } catch (error) {
        setSnackbarMessage(getErrorMessage(error, "messages.lookupFailed"));
        playQueryFeedback("error");
      } finally {
        if (productCodeParam) {
          lookupRequestInFlightRef.current = false;
          setLoading(false);
        }
      }
    }

    void applyExternalQuery();

    return () => {
      cancelled = true;
    };
  }, [
    handleLookup,
    handleSelectStore,
    isHydratingSelection,
    isProductQueryBusy,
    loadDetail,
    playQueryFeedback,
    processLoadedDetail,
    queryParams.keyword,
    queryParams.productCode,
    queryParams.source,
    queryParams.storeCode,
    selectedStoreCode,
    stores,
    storesLoading,
    t,
  ]);

  const scannerInputBlocked = isProductQueryBusy();
  const cameraScanDisabled = scannerInputBlocked;
  const cameraScan = useCameraScan({
    disabled: cameraScanDisabled,
    ignoreWhileProcessing: cameraScanMode === "continuous",
    resetKey: [
      cameraScanMode,
      selectedStoreCode ?? "",
      lookupVisible ? "lookup" : "idle",
      autoPricingDialog ? "pricing" : "idle",
    ].join(":"),
    suppressRepeatsUntilChange: cameraScanMode === "continuous",
    onBarcode: async (barcode) => {
      if (isProductQueryBusy()) {
        return;
      }
      console.log("[product-query] barcode scanned", { barcode });
      setKeyword(barcode);
      if (cameraScanMode === "single") {
        // 单次扫码命中后立即隐藏预览，查询结果继续由原有反馈流处理。
        setCameraVisible(false);
      }
      // 相机识别到条码先给短提示；查询完成后再播放命中/无结果等结果音。
      playBarcodeCapturedSound();
      await handleLookup(barcode, "scan", "camera");
    },
  });
  const hidScanner = useHidBarcodeScanner({
    enabled: !scannerInputBlocked,
    onScan: async (barcode) => {
      if (isProductQueryBusy()) {
        return;
      }
      console.log("[product-query] hid barcode scanned", { barcode });
      setKeyword(barcode);
      await handleLookup(barcode, "scan", "hid");
    },
  });
  const pauseHiddenScannerFocus = useCallback(() => {
    searchInputFocusedRef.current = true;
    if (resumeHiddenScannerFocusTimerRef.current) {
      clearTimeout(resumeHiddenScannerFocusTimerRef.current);
      resumeHiddenScannerFocusTimerRef.current = null;
    }
    hidScanner.pauseHiddenInputFocus();
  }, [hidScanner.pauseHiddenInputFocus]);
  const resumeHiddenScannerFocusLater = useCallback(() => {
    searchInputFocusedRef.current = false;
    if (resumeHiddenScannerFocusTimerRef.current) {
      clearTimeout(resumeHiddenScannerFocusTimerRef.current);
    }
    // 延迟恢复隐藏扫码输入框，避免搜索框失焦时两个输入框立刻互相抢焦点。
    resumeHiddenScannerFocusTimerRef.current = setTimeout(() => {
      resumeHiddenScannerFocusTimerRef.current = null;
      // 若搜索框已再次聚焦，保留用户当前输入焦点，不恢复隐藏扫码输入框。
      if (!searchInputFocusedRef.current) {
        hidScanner.resumeHiddenInputFocus();
      }
    }, 250);
  }, [hidScanner.resumeHiddenInputFocus]);

  useEffect(() => () => {
    if (resumeHiddenScannerFocusTimerRef.current) {
      clearTimeout(resumeHiddenScannerFocusTimerRef.current);
      resumeHiddenScannerFocusTimerRef.current = null;
    }
  }, []);

  const shouldRestoreCameraScan = useCallback(
    (source?: ScanSource | null) => source === "camera" && cameraScanMode === "continuous",
    [cameraScanMode]
  );
  const restoreScanAbility = useCallback(
    (source?: ScanSource | null) => {
      const restoreCamera = shouldRestoreCameraScan(source);
      setTimeout(() => {
        hidScanner.focusHiddenInput?.();
      }, restoreCamera ? 160 : 60);
    },
    [hidScanner.focusHiddenInput, shouldRestoreCameraScan]
  );
  const handleCameraScanModeChange = useCallback((mode: CameraScanMode) => {
    setCameraScanMode(mode);
    if (mode === "continuous") {
      setCameraVisible(false);
    }
  }, []);

  useFocusEffect(
    useCallback(() => {
      if (!isProductQueryBusy() && hidScanner.focusHiddenInput) {
        hidScanner.focusHiddenInput();
      }
    }, [hidScanner.focusHiddenInput, isProductQueryBusy])
  );

  const dirtyCount = useMemo(() => (isStorePriceDirty(detail, initialDetail) ? 1 : 0), [detail, initialDetail]);

  const handleRefresh = useCallback(async () => {
    if (isProductQueryBusy()) {
      return;
    }

    if (!detail?.productCode) {
      if (keyword.trim()) {
        setRefreshing(true);
        try {
          await handleLookup(keyword, "refresh");
        } finally {
          setRefreshing(false);
        }
      }
      return;
    }

    setRefreshing(true);
    lookupRequestInFlightRef.current = true;
    try {
      const nextDetail = await loadDetail(detail.productCode);
      if (nextDetail) {
        await processLoadedDetail(nextDetail, { lookupOrigin: "refresh" });
      }
    } catch (error) {
      setSnackbarMessage(getErrorMessage(error, "messages.refreshFailed"));
    } finally {
      lookupRequestInFlightRef.current = false;
      setRefreshing(false);
    }
  }, [
    detail?.productCode,
    handleLookup,
    isProductQueryBusy,
    keyword,
    loadDetail,
    processLoadedDetail,
  ]);

  const handleClear = useCallback(() => {
    const wasLookupSelectionOpen = lookupSelectionOpenRef.current;
    lookupSelectionOpenRef.current = false;
    if (!wasLookupSelectionOpen && isProductQueryBusy()) {
      return;
    }
    setKeyword("");
    setLookupItems([]);
    setSelectedLookupProductCode(undefined);
    setDetail(null);
    setInitialDetail(null);
    setLookupVisible(false);
    setLookupSelectionSource(null);
    setAutoPrintOnLookupConfirm(false);
    setQueryFeedback({ type: "idle" });
    setWarehousePriceSyncContext(null);
    setWarehousePriceSyncState(createWarehousePriceSyncState());
  }, [isProductQueryBusy]);

  const handleConfirmLookup = useCallback(async () => {
    if (
      !selectedLookupProductCode ||
      !lookupSelectionOpenRef.current ||
      lookupRequestInFlightRef.current ||
      storeSelectionInFlightRef.current ||
      warehousePriceRequestInFlightRef.current ||
      warehousePriceInteractionLocked ||
      autoPricingDialogSaving
    ) {
      return;
    }

    lookupSelectionOpenRef.current = false;
    lookupRequestInFlightRef.current = true;
    setLoading(true);
    setLookupVisible(false);
    try {
      const nextDetail = await loadDetail(selectedLookupProductCode);
      if (nextDetail) {
        await processLoadedDetail(nextDetail, {
          lookupOrigin: lookupSelectionSource ? "scan" : "manual",
          scanSource: lookupSelectionSource,
          scanKeyword: keyword,
          autoPrintEnabled: autoPrintOnLookupConfirm,
        });
      }
    } catch (error) {
      const message = getErrorMessage(error, "messages.lookupFailed");
      setDetail(null);
      setInitialDetail(null);
      setQueryFeedback({ type: "error", query: keyword.trim(), message });
      setSnackbarMessage(message);
      playQueryFeedback("error");
    } finally {
      lookupSelectionOpenRef.current = false;
      lookupRequestInFlightRef.current = false;
      setLoading(false);
      setAutoPrintOnLookupConfirm(false);
      setLookupSelectionSource(null);
    }
  }, [
    autoPrintOnLookupConfirm,
    autoPricingDialogSaving,
    keyword,
    loadDetail,
    lookupSelectionSource,
    playQueryFeedback,
    processLoadedDetail,
    selectedLookupProductCode,
    t,
    warehousePriceInteractionLocked,
  ]);

  const handleCancelWarehousePriceSync = useCallback(() => {
    if (warehousePriceSyncState.phase === "confirming") {
      return;
    }

    const scanSource = warehousePriceSyncContext?.scanSource;
    setWarehousePriceSyncState((current) =>
      reduceWarehousePriceSyncState(current, { type: "cancelled" })
    );
    setWarehousePriceSyncContext(null);
    // 取消只放弃零售价更新；首轮已同步的进货价继续保留。
    restoreScanAbility(scanSource);
  }, [restoreScanAbility, warehousePriceSyncContext?.scanSource, warehousePriceSyncState.phase]);

  const handleConfirmWarehousePriceSync = useCallback(async () => {
    const snapshot = warehousePriceSyncState.snapshot;
    const context = warehousePriceSyncContext;
    const storePriceUuid = snapshot?.storePrice?.uuid || context?.detail.storePrice?.uuid;
    if (
      !snapshot ||
      !context ||
      !storePriceUuid ||
      warehousePriceRequestInFlightRef.current
    ) {
      return;
    }
    warehousePriceRequestInFlightRef.current = true;

    setWarehousePriceSyncState((current) =>
      reduceWarehousePriceSyncState(current, { type: "confirm_started" })
    );
    try {
      const confirmed = await syncWarehousePrice(
        storePriceUuid,
        buildWarehousePriceSyncRequest(snapshot, true)
      );
      let latestDetail = confirmed.storePrice
        ? replaceStorePriceDetail(context.detail, confirmed.storePrice)
        : context.detail;

      // 零售价确认会同步派生条码，成功后统一读取最终详情再决定打印。
      latestDetail =
        (await loadDetail(context.detail.productCode, context.storeCodeOverride)) ?? latestDetail;
      const shouldPrint =
        context.autoPrintEnabled &&
        shouldAutoPrintWarehousePrice({
          lookupOrigin: context.lookupOrigin,
          stage: "confirmation_succeeded",
          snapshot: confirmed,
          alreadyPrinted: context.alreadyPrinted,
        });
      const labelPrinted = shouldPrint
        ? await smartAutoPrint(context.scanKeyword, latestDetail)
        : false;

      setWarehousePriceSyncState((current) =>
        reduceWarehousePriceSyncState(current, { type: "confirm_succeeded", snapshot: confirmed })
      );
      setWarehousePriceSyncContext(null);
      const feedback = resolveWarehousePriceConfirmationFeedback({
        retailUpdated: confirmed.retailUpdated,
        printAttempted: shouldPrint,
        labelPrinted,
      });
      if (feedback === "retail_updated_print_failed") {
        setSnackbarMessage(t("warehousePriceSync.retailUpdatedPrintFailed"));
      } else if (feedback === "retail_updated") {
        setSnackbarMessage(t("warehousePriceSync.retailUpdated"));
      }
      restoreScanAbility(context.scanSource);
    } catch (error) {
      let latestSnapshot = extractWarehousePriceSyncConflict(error);
      if (latestSnapshot) {
        let latestDetail = latestSnapshot.storePrice
          ? replaceStorePriceDetail(context.detail, latestSnapshot.storePrice)
          : context.detail;

        const snapshotComplete = isWarehousePriceConflictSnapshotComplete(latestSnapshot);
        if (!snapshotComplete) {
          const latestStorePrice = latestSnapshot.storePrice ?? latestDetail.storePrice;
          if (latestStorePrice?.uuid) {
            // 冲突响应缺字段时仅重新取一次预览，避免用户基于旧值确认。
            try {
              latestSnapshot = await syncWarehousePrice(latestStorePrice.uuid, {
                confirmRetailPrice: false,
                expectedWarehousePurchasePrice: null,
                expectedWarehouseRetailPrice: null,
                expectedStorePurchasePrice: latestStorePrice.purchasePrice ?? null,
                expectedStoreRetailPrice: latestStorePrice.retailPrice ?? null,
                expectedDiscountRate: normalizeDiscountRateValue(latestStorePrice.discountRate),
              });
              latestDetail = latestSnapshot.storePrice
                ? replaceStorePriceDetail(latestDetail, latestSnapshot.storePrice)
                : latestDetail;
            } catch (refreshError) {
              const message = getErrorMessage(refreshError, "warehousePriceSync.confirmFailed");
              setWarehousePriceSyncState((current) =>
                reduceWarehousePriceSyncState(current, { type: "confirm_failed", message })
              );
              return;
            }
          } else {
            const message = t("warehousePriceSync.confirmFailed");
            setWarehousePriceSyncState((current) =>
              reduceWarehousePriceSyncState(current, { type: "confirm_failed", message })
            );
            return;
          }
        }

        setDetail(latestDetail);
        setInitialDetail(cloneDetail(latestDetail));
        if (!latestSnapshot.retailConfirmationRequired) {
          const shouldPrint =
            context.autoPrintEnabled &&
            shouldAutoPrintWarehousePrice({
              lookupOrigin: context.lookupOrigin,
              stage: "preview_succeeded",
              snapshot: latestSnapshot,
              alreadyPrinted: context.alreadyPrinted,
            });
          if (shouldPrint) {
            await smartAutoPrint(context.scanKeyword, latestDetail);
          }
          setWarehousePriceSyncState((current) =>
            reduceWarehousePriceSyncState(current, {
              type: "confirm_succeeded",
              snapshot: latestSnapshot!,
            })
          );
          setWarehousePriceSyncContext(null);
          restoreScanAbility(context.scanSource);
          return;
        }

        const message = t("warehousePriceSync.conflict");
        setWarehousePriceSyncContext({ ...context, detail: latestDetail });
        setWarehousePriceSyncState((current) =>
          reduceWarehousePriceSyncState(current, {
            type: "conflict_received",
            snapshot: latestSnapshot!,
            message,
          })
        );
        return;
      }

      const message = getErrorMessage(error, "warehousePriceSync.confirmFailed");
      setWarehousePriceSyncState((current) =>
        reduceWarehousePriceSyncState(current, { type: "confirm_failed", message })
      );
    } finally {
      warehousePriceRequestInFlightRef.current = false;
    }
  }, [
    getErrorMessage,
    loadDetail,
    restoreScanAbility,
    smartAutoPrint,
    t,
    warehousePriceSyncContext,
    warehousePriceSyncState.snapshot,
  ]);

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

  const handleUpdateProductType = useCallback(
    async (productType: number) => {
      if (!detail?.productCode || !selectedStoreCode) {
        return;
      }

      if (detail.productType === productType) {
        setProductTypeDialogVisible(false);
        return;
      }

      setProductTypeSaving(true);
      try {
        const result = await updateProductType(detail.productCode, {
          productType,
          storeCode: selectedStoreCode,
        });

        setDetail((current) =>
          current
            ? {
                ...current,
                productType: result.productType,
                productTypeLabel: result.productTypeLabel,
              }
            : current
        );
        setInitialDetail((current) =>
          current
            ? {
                ...current,
                productType: result.productType,
                productTypeLabel: result.productTypeLabel,
              }
            : current
        );
        setSnackbarMessage(t("messages.productTypeUpdated"));
        setProductTypeDialogVisible(false);
      } catch (error) {
        setSnackbarMessage(getErrorMessage(error, "messages.productTypeUpdateFailed"));
      } finally {
        setProductTypeSaving(false);
      }
    },
    [detail, getErrorMessage, selectedStoreCode, t]
  );

  const handleToggleAutoPricing = useCallback(
    async (value: boolean) => {
      if (!detail?.storePrice) {
        return;
      }

      if (!value) {
        handleChangeStorePrice({ isAutoPricing: false });
        return;
      }

      await maybeHandleAutoPricing(detail, { forceAutoPricing: true });
    },
    [detail, handleChangeStorePrice, maybeHandleAutoPricing]
  );

  const handleChangeStorePurchasePrice = useCallback(
    (value: string) => {
      const next = toFixedDecimalInput(value);
      setStorePurchaseInput(next.display);
      handleChangeStorePrice({ purchasePrice: next.numeric });
    },
    [handleChangeStorePrice]
  );

  const handleChangeStoreRetailPrice = useCallback(
    (value: string) => {
      const next = toFixedDecimalInput(value);
      setStoreRetailInput(next.display);
      setDetail((current) => {
        if (!current?.storePrice) {
          return current;
        }

        const discountRate = normalizeDiscountRateValue(current.storePrice.discountRate);
        return {
          ...current,
          storePrice: {
            ...current.storePrice,
            retailPrice: next.numeric,
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

  const openStorePurchasePriceEditor = useCallback(() => {
    openNumericInputModal({
      key: "store-purchase",
      title: t("storePrice.purchase"),
      value: storePurchaseInput,
      allowDecimal: true,
      onConfirmValue: handleChangeStorePurchasePrice,
    });
  }, [handleChangeStorePurchasePrice, openNumericInputModal, storePurchaseInput, t]);

  const openStoreRetailPriceEditor = useCallback(() => {
    openNumericInputModal({
      key: "store-retail",
      title: t("storePrice.retail"),
      value: storeRetailInput,
      allowDecimal: true,
      onConfirmValue: handleChangeStoreRetailPrice,
    });
  }, [handleChangeStoreRetailPrice, openNumericInputModal, storeRetailInput, t]);

  const openStoreDiscountPercentEditor = useCallback(() => {
    const currentDiscountRate = normalizeDiscountRateValue(detail?.storePrice?.discountRate);
    openNumericInputModal({
      key: "store-discount-percent",
      title: t("storePrice.discountPercent"),
      value: formatPercentValue(currentDiscountRate),
      allowDecimal: true,
      onConfirmValue: handleChangeStoreDiscountPercent,
    });
  }, [detail?.storePrice?.discountRate, handleChangeStoreDiscountPercent, openNumericInputModal, t]);

  const openStoreDiscountedRetailEditor = useCallback(() => {
    const currentDiscountRate = normalizeDiscountRateValue(detail?.storePrice?.discountRate);
    const currentDiscountedRetail = getDiscountedRetailPrice(
      detail?.storePrice?.retailPrice,
      currentDiscountRate
    );
    openNumericInputModal({
      key: "store-discounted-retail",
      title: t("storePrice.discountedRetail"),
      value: formatCurrency(currentDiscountedRetail),
      allowDecimal: true,
      onConfirmValue: handleChangeStoreDiscountedRetailPrice,
    });
  }, [
    detail?.storePrice?.discountRate,
    detail?.storePrice?.retailPrice,
    handleChangeStoreDiscountedRetailPrice,
    openNumericInputModal,
    t,
  ]);

  const handleChangeSetCode = useCallback((setCodeId: string, patch: Partial<ProductSetCodeItem>) => {
    setDetail((current) =>
      current
        ? {
            ...current,
            setCodes: current.setCodes.map((item) => (item.setCodeId === setCodeId ? { ...item, ...patch } : item)),
          }
        : current
    );
  }, []);

  const handleChangeSetCodeRetail = useCallback((setCodeId: string, value: string) => {
    setDetail((current) =>
      current
        ? {
            ...current,
            setCodes: current.setCodes.map((item) =>
              item.setCodeId === setCodeId
                ? { ...item, setRetailPrice: value.trim() === "" ? null : Number(value) }
                : item
            ),
          }
        : current
    );
  }, []);

  const handleChangeMultiCode = useCallback((setCodeId: string, patch: Partial<MultiCodeEditableItem>) => {
    setDetail((current) =>
      current
        ? {
            ...current,
            multiCodes: current.multiCodes.map((item) => (item.setCodeId === setCodeId ? { ...item, ...patch } : item)),
          }
        : current
    );
  }, []);

  const handleSaveSetCode = useCallback(
    async (setCodeId: string) => {
      if (!detail?.productCode || !selectedStoreCode) {
        return;
      }

      const target = detail.setCodes.find((item) => item.setCodeId === setCodeId);
      if (!target || !target.setBarcode?.trim()) {
        setSnackbarMessage(t("messages.setCodeBarcodeRequired"));
        return;
      }

      if (target.setRetailPrice == null || !Number.isFinite(target.setRetailPrice)) {
        setSnackbarMessage(t("messages.setCodeRetailRequired"));
        return;
      }

      setSavingItemId(setCodeId);
      try {
        await updateSetCode(setCodeId, {
          storeCode: selectedStoreCode,
          barcode: target.setBarcode.trim(),
          retailPrice: target.setRetailPrice,
          isActive: target.isActive,
        });
        await loadDetail(detail.productCode);
        setSnackbarMessage(t("messages.setCodeSaved"));
      } catch (error) {
        setSnackbarMessage(getErrorMessage(error, "messages.setCodeSaveFailed"));
      } finally {
        setSavingItemId(null);
      }
    },
    [detail, getErrorMessage, loadDetail, selectedStoreCode, t]
  );

  const openEditSetCodeBarcode = useCallback((setCodeId: string) => {
    const target = detail?.setCodes.find((item) => item.setCodeId === setCodeId);
    if (!target) {
      return;
    }
    setBarcodeEditModal({
      key: `set-barcode-${setCodeId}`,
      title: t("setCode.editBarcodeTitle"),
      value: target.setBarcode ?? "",
      targetId: setCodeId,
      codeType: "set",
    });
  }, [detail?.setCodes, t]);

  const openEditSetCodeRetailPrice = useCallback((setCodeId: string) => {
    const target = detail?.setCodes.find((item) => item.setCodeId === setCodeId);
    if (!target) {
      return;
    }
    openNumericInputModal({
      key: `set-retail-${setCodeId}`,
      title: t("setCode.editRetailTitle"),
      value: formatFixedDecimal(target.setRetailPrice),
      allowDecimal: true,
      onConfirmValue: (value) => {
        handleChangeSetCodeRetail(setCodeId, value);
        if (detail?.productCode) {
          void loadDetail(detail.productCode);
        }
      },
    });
  }, [detail?.setCodes, detail?.productCode, handleChangeSetCodeRetail, loadDetail, openNumericInputModal, t]);

  const openAddSetCode = useCallback(() => {
    setCodeAddModal({ codeType: "set", value: "" });
  }, []);

  const openEditMultiCodeBarcode = useCallback((setCodeId: string) => {
    const target = detail?.multiCodes.find((item) => item.setCodeId === setCodeId);
    if (!target) {
      return;
    }
    setBarcodeEditModal({
      key: `multi-barcode-${setCodeId}`,
      title: t("multiCode.editBarcodeTitle"),
      value: target.barcode ?? "",
      targetId: setCodeId,
      codeType: "multi",
    });
  }, [detail?.multiCodes, t]);

  const openEditMultiCodeRetailPrice = useCallback((setCodeId: string) => {
    const target = detail?.multiCodes.find((item) => item.setCodeId === setCodeId);
    if (!target) {
      return;
    }
    openNumericInputModal({
      key: `multi-retail-${setCodeId}`,
      title: t("multiCode.editRetailTitle"),
      value: formatFixedDecimal(target.retailPrice),
      allowDecimal: true,
      onConfirmValue: (value) => {
        handleChangeMultiCode(setCodeId, { retailPrice: value.trim() === "" ? null : Number(value) });
      },
    });
  }, [detail?.multiCodes, handleChangeMultiCode, openNumericInputModal, t]);

  const openAddMultiCode = useCallback(() => {
    setCodeAddModal({ codeType: "multi", value: "" });
  }, []);

  const handleConfirmBarcodeEdit = useCallback(async () => {
    if (!barcodeEditModal || !detail?.productCode || !selectedStoreCode) {
      setBarcodeEditModal(null);
      return;
    }

    const { targetId, value, codeType } = barcodeEditModal;
    const trimmed = value.trim();
    if (!trimmed) {
      setSnackbarMessage(t("messages.setCodeBarcodeRequired"));
      setBarcodeEditModal(null);
      return;
    }

    setSavingItemId(targetId);
    setBarcodeEditModal(null);
    try {
      await updateSetCode(targetId, {
        storeCode: selectedStoreCode,
        barcode: trimmed,
        isActive: true,
      });
      await loadDetail(detail.productCode);
      setSnackbarMessage(codeType === "set" ? t("messages.setCodeSaved") : t("messages.multiCodeSaved"));
    } catch (error) {
      setSnackbarMessage(getErrorMessage(error, "messages.setCodeSaveFailed"));
    } finally {
      setSavingItemId(null);
    }
  }, [barcodeEditModal, detail?.productCode, getErrorMessage, loadDetail, selectedStoreCode, t]);

  const handleConfirmCodeAdd = useCallback(async () => {
    if (!codeAddModal || !detail?.productCode || !selectedStoreCode) {
      setCodeAddModal(null);
      return;
    }

    const { codeType, value } = codeAddModal;
    const trimmed = value.trim();
    if (!trimmed) {
      setSnackbarMessage(codeType === "set" ? t("messages.setCodeBarcodeRequired") : t("messages.multiCodeBarcodeRequired"));
      setCodeAddModal(null);
      return;
    }

    setSavingItemId(codeType === "set" ? "new-set" : "new-multi");
    setCodeAddModal(null);
    try {
      await createSetCode({
        productCode: detail.productCode,
        storeCode: selectedStoreCode,
        productType: codeType === "set" ? 1 : 2,
        barcode: trimmed,
        isActive: true,
      });
      await loadDetail(detail.productCode);
      setSnackbarMessage(codeType === "set" ? t("messages.setCodeCreated") : t("messages.multiCodeCreated"));
    } catch (error) {
      setSnackbarMessage(getErrorMessage(error, "messages.setCodeSaveFailed"));
    } finally {
      setSavingItemId(null);
    }
  }, [codeAddModal, detail?.productCode, getErrorMessage, loadDetail, selectedStoreCode, t]);

  const openClearancePriceEditor = useCallback(() => {
    openNumericInputModal({
      key: "clearance-price",
      title: t("clearancePrice.price"),
      value: clearancePriceInput,
      allowDecimal: true,
      onConfirmValue: (value: string) => {
        setClearancePriceInput(value);
        setTimeout(() => void saveClearanceRef.current(), 0);
      },
    });
  }, [clearancePriceInput, openNumericInputModal, t]);

  const handleSaveMultiCode = useCallback(
    async (setCodeId: string) => {
      if (!detail?.productCode || !selectedStoreCode) {
        return;
      }

      const target = detail.multiCodes.find((item) => item.setCodeId === setCodeId);
      if (!target || !target.barcode?.trim()) {
        setSnackbarMessage(t("messages.multiCodeBarcodeRequired"));
        return;
      }

      setSavingItemId(setCodeId);
      try {
        await updateSetCode(setCodeId, {
          storeCode: selectedStoreCode,
          barcode: target.barcode.trim(),
          isActive: target.isActive,
        });
        await loadDetail(detail.productCode);
        setSnackbarMessage(t("messages.multiCodeSaved"));
      } catch (error) {
        setSnackbarMessage(getErrorMessage(error, "messages.multiCodeSaveFailed"));
      } finally {
        setSavingItemId(null);
      }
    },
    [detail, getErrorMessage, loadDetail, selectedStoreCode, t]
  );

  const handleLoadMoreCodes = useCallback(() => {
    if (!detail || codesLoading || codesLoadingMore || !codesHasMore) {
      return;
    }

    void loadProductCodes(detail, codePage + 1, true);
  }, [codePage, codesHasMore, codesLoading, codesLoadingMore, detail, loadProductCodes]);

  const handleSaveClearancePrice = useCallback(async () => {
    if (!detail?.productCode || !selectedStoreCode) {
      return;
    }

    const clearancePrice = parseDecimalInput(clearancePriceInput);
    if (clearancePriceInput.trim() && clearancePrice == null) {
      setSnackbarMessage(t("messages.clearancePriceRequired"));
      return;
    }

    setSavingClearance(true);
    try {
      await upsertClearancePrice(detail.productCode, {
        storeCode: selectedStoreCode,
        clearancePrice,
      });
      await loadDetail(detail.productCode);
      setSnackbarMessage(t("messages.clearanceSaved"));
    } catch (error) {
      setSnackbarMessage(getErrorMessage(error, "messages.clearanceSaveFailed"));
    } finally {
      setSavingClearance(false);
    }
  }, [clearancePriceInput, detail?.productCode, getErrorMessage, loadDetail, selectedStoreCode, t]);

  saveClearanceRef.current = handleSaveClearancePrice;

  const handleSaveAll = useCallback(async () => {
    if (!detail?.storePrice || !isStorePriceDirty(detail, initialDetail)) {
      return;
    }

    setSaving(true);
    try {
      const saved = await persistStorePrice(detail, {
        purchasePrice: detail.storePrice.purchasePrice ?? null,
        retailPrice: detail.storePrice.retailPrice ?? null,
        discountRate: normalizeDiscountRateValue(detail.storePrice.discountRate),
        isAutoPricing: detail.storePrice.isAutoPricing,
        isSpecialProduct: detail.storePrice.isSpecialProduct,
        isActive: detail.storePrice.isActive,
      });
      if (saved) {
        setSnackbarMessage(t("messages.saved"));
      }
    } catch (error) {
      setSnackbarMessage(getErrorMessage(error, "messages.saveFailed"));
    } finally {
      setSaving(false);
    }
  }, [detail, getErrorMessage, initialDetail, persistStorePrice, t]);

  const handleReset = useCallback(() => {
    setDetail(cloneDetail(initialDetail));
  }, [initialDetail]);

  const handlePrintSetCodeProduct = useCallback(
    async (setCodeId: string) => {
      if (!detail) {
        return;
      }

      const target = detail.setCodes.find((item) => item.setCodeId === setCodeId);
      if (!target?.setBarcode?.trim()) {
        setSnackbarMessage(t("messages.setCodeBarcodeRequired"));
        return;
      }

      if (target.setRetailPrice == null || !Number.isFinite(target.setRetailPrice)) {
        setSnackbarMessage(t("messages.setCodeRetailRequired"));
        return;
      }

      await sendProductLabel(detail, {
        barcode: target.setBarcode.trim(),
        retailPrice: target.setRetailPrice,
        action: `set:${setCodeId}`,
        printType: smallLabel ? "small" : null,
      });
    },
    [detail, sendProductLabel, smallLabel, t]
  );

  const handlePrintMultiCodeProduct = useCallback(
    async (setCodeId: string) => {
      if (!detail) {
        return;
      }

      const target = detail.multiCodes.find((item) => item.setCodeId === setCodeId);
      if (!target?.barcode?.trim()) {
        setSnackbarMessage(t("messages.multiCodeBarcodeRequired"));
        return;
      }

      await sendProductLabel(detail, {
        barcode: target.barcode.trim(),
        retailPrice: target.retailPrice ?? detail.storePrice?.retailPrice ?? null,
        action: `multi:${setCodeId}`,
        printType: smallLabel ? "small" : null,
      });
    },
    [detail, sendProductLabel, smallLabel, t]
  );

  const handlePrint = useCallback(
    async (kind: "product" | "discount" | "clearance" | "bigDiscount") => {
      if (!detail) {
        return;
      }

      if (
        (kind === "discount" || kind === "bigDiscount")
        && !(detail.storePrice?.discountRate && detail.storePrice.discountRate > 0)
      ) {
        setSnackbarMessage(t("messages.discountPrintUnavailable"));
        return;
      }

      if (kind === "clearance" && !detail.clearancePrice) {
        setSnackbarMessage(t("messages.clearancePrintUnavailable"));
        return;
      }

      setPrintingAction(kind);
      try {
        const printType = smallLabel ? "small" : null;
        if (kind === "product") {
          await sendProductLabel(detail, { action: "product", printType });
          return;
        } else if (kind === "discount") {
          await printDiscountLabel(detail, printType);
          for (let i = 1; i < printQuantity; i++) {
            await printDiscountLabel(detail, printType);
          }
        } else if (kind === "bigDiscount") {
          await printBigDiscountLabel(detail);
          for (let i = 1; i < printQuantity; i++) {
            await printBigDiscountLabel(detail);
          }
        } else {
          await printClearanceLabel(detail);
          for (let i = 1; i < printQuantity; i++) {
            await printClearanceLabel(detail);
          }
        }
        setSnackbarMessage(t("messages.printSuccess"));
        if (quantitySingleUse && printQuantity > 1) {
          setPrintQuantity(1);
        }
      } catch (error) {
        setSnackbarMessage(getErrorMessage(error, "messages.printFailed"));
      } finally {
        setPrintingAction(null);
      }
    },
    [detail, getErrorMessage, printQuantity, quantitySingleUse, sendProductLabel, smallLabel, t]
  );

  const handleReturnToInvoices = useCallback(() => {
    if (!invoiceReturnState) {
      return;
    }

    router.replace(
      buildLocalSupplierInvoicesRestoreHref(invoiceReturnState) as unknown as Parameters<typeof router.replace>[0]
    );
  }, [invoiceReturnState, router]);

  const storePrice = detail?.storePrice;
  const clearancePrice = detail?.clearancePrice;
  const normalizedStoreDiscountRate = normalizeDiscountRateValue(storePrice?.discountRate);
  const discountedRetailPrice = getDiscountedRetailPrice(
    storePrice?.retailPrice,
    normalizedStoreDiscountRate
  );
  const retailGp = calcGpPercent(storePrice?.retailPrice, storePrice?.purchasePrice);
  const discountedRetailGp = calcGpPercent(discountedRetailPrice, storePrice?.purchasePrice);
  const renderCameraScanner = () => (
    <>
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
    </>
  );

  return (
    <SafeAreaView style={styles.safeArea} edges={["top", "left", "right"]}>
      <QueryHeader
        storeName={selectedStore?.storeName}
        canSelectStore={canSelectStore}
        onStorePress={() => {
          if (!isProductQueryBusy()) {
            setStorePickerVisible(true);
          }
        }}
        onScanPress={() => {
          if (isProductQueryBusy()) {
            return;
          }
          if (cameraScanMode === "single") {
            setCameraVisible(true);
          }
        }}
        onRefreshPress={() => void handleRefresh()}
        refreshing={refreshing}
      />

      <SearchPanel
        value={keyword}
        loading={loading || storesLoading || scannerInputBlocked}
        lastHitLabel={detail ? undefined : lastHitLabel}
        onChangeText={setKeyword}
        onFocus={pauseHiddenScannerFocus}
        onBlur={resumeHiddenScannerFocusLater}
        onOpenPrintSettings={() => setPrintSettingsVisible(true)}
        onSubmit={() => void handleLookup()}
        onClear={handleClear}
      />
      <CameraScanModeSelector
        value={cameraScanMode}
        onChange={handleCameraScanModeChange}
        style={styles.cameraModeSelector}
      />
      {cameraScanMode === "continuous" ? (
        <View style={styles.inlineCameraPanel}>
          {renderCameraScanner()}
        </View>
      ) : null}

      <ScrollView
        contentContainerStyle={styles.content}
        pointerEvents={scannerInputBlocked ? "none" : "auto"}
      >
        {access.canCreateStoreProducts ? (
          <View style={styles.createProductBar}>
            <Button icon="plus" mode="contained-tonal" onPress={openCreateProductModal}>
              {t("createProduct.action")}
            </Button>
          </View>
        ) : null}
        {invoiceReturnState ? (
          <View style={styles.returnBar}>
            <Button icon="arrow-left" mode="contained-tonal" onPress={handleReturnToInvoices}>
              {t("actions.returnToInvoiceDetails")}
            </Button>
          </View>
        ) : null}
        {detail ? (
          <>
            <View style={styles.firstScreenSection}>
              <ProductHeroCard
                imageUrl={detail.productImage}
                productName={detail.productName}
                itemNumber={detail.itemNumber}
                supplierName={detail.localSupplierName}
                supplierCode={detail.localSupplierCode}
                barcode={detail.barcode}
                productType={detail.productType}
                grade={detail.grade}
                onPressProductType={() => setProductTypeDialogVisible(true)}
              />

              {storePrice ? (
                <StorePriceStrategyCard
                  storeName={storePrice.storeName}
                  purchasePrice={storePurchaseInput}
                  retailPrice={storeRetailInput}
                  retailGp={retailGp}
                  discountPercent={formatPercentValue(normalizedStoreDiscountRate)}
                  discountedRetailPrice={formatCurrency(discountedRetailPrice)}
                  discountedRetailGp={discountedRetailGp}
                  autoPricing={storePrice.isAutoPricing}
                  isSpecialProduct={storePrice.isSpecialProduct}
                  rate={formatFixedDecimal(storePrice.rate)}
                  strategySourceLabel={storePrice.strategySourceLabel}
                  strategyRuleLabel={storePrice.strategyRuleLabel}
                  onEditPurchasePrice={openStorePurchasePriceEditor}
                  onEditRetailPrice={openStoreRetailPriceEditor}
                  onEditDiscountPercent={openStoreDiscountPercentEditor}
                  onEditDiscountedRetailPrice={openStoreDiscountedRetailEditor}
                  onToggleAutoPricing={(value) => void handleToggleAutoPricing(value)}
                  onToggleSpecial={(value) => handleChangeStorePrice({ isSpecialProduct: value })}
                />
              ) : (
                <View style={styles.emptyBlock}>
                  <Text variant="bodyMedium">{t("messages.emptyStorePrice")}</Text>
                </View>
              )}

              <LabelPrintCard
                isPrintingProduct={printingAction === "product"}
                isPrintingDiscount={printingAction === "discount"}
                isPrintingBigDiscount={printingAction === "bigDiscount"}
                canPrintDiscount={Boolean(normalizedStoreDiscountRate && normalizedStoreDiscountRate > 0)}
                canPrintBigDiscount={Boolean(normalizedStoreDiscountRate && normalizedStoreDiscountRate > 0)}
                onPrintProduct={
                  printingAction && printingAction !== "product" ? undefined : () => void handlePrint("product")
                }
                onPrintDiscount={
                  printingAction && printingAction !== "discount" ? undefined : () => void handlePrint("discount")
                }
                onPrintBigDiscount={
                  printingAction && printingAction !== "bigDiscount"
                    ? undefined
                    : () => void handlePrint("bigDiscount")
                }
              />

              <StoreClearancePriceCard
                clearanceBarcode={clearancePrice?.clearanceBarcode}
                clearancePrice={clearancePriceInput}
                isPrintingClearance={printingAction === "clearance"}
                onEditClearancePrice={openClearancePriceEditor}
                onPrintClearance={
                  printingAction && printingAction !== "clearance"
                    ? undefined
                    : () => void handlePrint("clearance")
                }
              />
            </View>

            {detail.productType === 1 || detail.productType === 2 || detail.setCodeCount > 0 || detail.multiCodeCount > 0 ? (
              <View style={styles.secondarySection}>
                <Text variant="titleSmall" style={styles.secondaryTitle}>
                  {t("sections.moreInfo")}
                </Text>
                {detail.productType === 1 || (detail.productType !== 2 && detail.setCodeCount > 0 && detail.multiCodeCount === 0) ? (
                  <SetCodeCompactSection
                    items={detail.setCodes}
                    savingItemId={savingItemId}
                    printingItemId={printingAction?.startsWith("set:") ? printingAction.slice(4) : null}
                    totalCount={detail.setCodeCount}
                    loading={codesLoading}
                    loadingMore={codesLoadingMore}
                    hasMore={codesHasMore}
                    onEditItemBarcode={openEditSetCodeBarcode}
                    onEditItemRetailPrice={openEditSetCodeRetailPrice}
                    onSaveItem={(setCodeId) => void handleSaveSetCode(setCodeId)}
                    onPrintItem={(setCodeId) => void handlePrintSetCodeProduct(setCodeId)}
                    onAddItem={openAddSetCode}
                    onLoadMore={handleLoadMoreCodes}
                  />
                ) : null}
                {detail.productType === 2 || detail.multiCodeCount > 0 ? (
                  <MultiCodeCompactList
                    items={detail.multiCodes}
                    savingItemId={savingItemId}
                    printingItemId={printingAction?.startsWith("multi:") ? printingAction.slice(6) : null}
                    totalCount={detail.multiCodeCount}
                    loading={codesLoading}
                    loadingMore={codesLoadingMore}
                    hasMore={codesHasMore}
                    onEditItemBarcode={openEditMultiCodeBarcode}
                    onEditItemRetailPrice={openEditMultiCodeRetailPrice}
                    onSaveItem={(setCodeId) => void handleSaveMultiCode(setCodeId)}
                    onPrintItem={(setCodeId) => void handlePrintMultiCodeProduct(setCodeId)}
                    onAddItem={openAddMultiCode}
                    onLoadMore={handleLoadMoreCodes}
                  />
                ) : null}
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
        visible={dirtyCount > 0 && !scannerInputBlocked}
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
        onClose={() => {
          lookupSelectionOpenRef.current = false;
          setLookupVisible(false);
        }}
        onConfirm={() => void handleConfirmLookup()}
      />

      <WarehousePriceSyncModal
        visible={warehousePriceSyncState.phase === "confirmation" || warehousePriceSyncState.phase === "confirming"}
        productName={warehousePriceSyncContext?.detail.productName}
        productCode={
          warehousePriceSyncContext?.detail.itemNumber ||
          warehousePriceSyncContext?.detail.productCode
        }
        snapshot={warehousePriceSyncState.snapshot}
        loading={warehousePriceSyncState.phase === "confirming"}
        errorMessage={warehousePriceSyncState.errorMessage}
        onCancel={handleCancelWarehousePriceSync}
        onConfirm={() => void handleConfirmWarehousePriceSync()}
      />

      <Portal>
        <Modal
          visible={createProductVisible}
          onDismiss={createProductSaving ? undefined : closeCreateProductModal}
          contentContainerStyle={styles.createProductModal}
        >
          <ScrollView
            contentContainerStyle={styles.createProductModalContent}
            keyboardShouldPersistTaps="handled"
          >
            <Text variant="titleMedium" style={styles.createProductTitle}>
              {t("createProduct.title")}
            </Text>

            <View style={styles.createFieldGroup}>
              <Text variant="labelMedium" style={styles.createFieldLabel}>
                {t("createProduct.fields.supplier")}
              </Text>
              {createSuppliersLoading ? (
                <Text variant="bodySmall" style={styles.createHint}>
                  {t("createProduct.messages.suppliersLoading")}
                </Text>
              ) : createSuppliers.length ? (
                <Button
                  mode="outlined"
                  onPress={() => setCreateSupplierPickerVisible(true)}
                  disabled={createProductSaving}
                  style={styles.createSupplierSelectButton}
                  contentStyle={styles.createSupplierSelectContent}
                >
                  {selectedCreateSupplier
                    ? `${selectedCreateSupplier.supplierCode} - ${selectedCreateSupplier.supplierName || selectedCreateSupplier.supplierCode}`
                    : t("createProduct.selectSupplier")}
                </Button>
              ) : (
                <Button
                  mode="outlined"
                  onPress={() => void loadCreateSuppliers()}
                  disabled={createProductSaving}
                >
                  {t("createProduct.reloadSuppliers")}
                </Button>
              )}
              {selectedCreateSupplier ? (
                <Text variant="bodySmall" style={styles.createHint}>
                  {selectedCreateSupplier.supplierCode}
                </Text>
              ) : null}
            </View>

            <TextInput
              style={styles.createTextInput}
              value={createProductDraft.itemNumber}
              onChangeText={(itemNumber) => updateCreateProductDraft({ itemNumber })}
              placeholder={t("createProduct.fields.itemNumber")}
              editable={!createProductSaving}
            />
            <TextInput
              style={styles.createTextInput}
              value={createProductDraft.barcode}
              onChangeText={(barcode) => updateCreateProductDraft({ barcode })}
              placeholder={t("createProduct.fields.barcode")}
              editable={!createProductSaving}
            />
            <TextInput
              style={styles.createTextInput}
              value={createProductDraft.productName}
              onChangeText={(productName) => updateCreateProductDraft({ productName })}
              placeholder={t("createProduct.fields.productName")}
              editable={!createProductSaving}
            />
            <TextInput
              style={styles.createTextInput}
              value={createProductDraft.purchasePrice}
              onChangeText={(purchasePrice) => updateCreateProductDraft({ purchasePrice })}
              placeholder={t("createProduct.fields.purchasePrice")}
              keyboardType="decimal-pad"
              editable={!createProductSaving}
            />
            <TextInput
              style={styles.createTextInput}
              value={createProductDraft.retailPrice}
              onChangeText={(retailPrice) => updateCreateProductDraft({ retailPrice })}
              placeholder={t("createProduct.fields.retailPrice")}
              keyboardType="decimal-pad"
              editable={!createProductSaving}
            />

            <View style={styles.createSwitchRow}>
              <Text variant="bodyMedium">{t("createProduct.fields.isSpecialProduct")}</Text>
              <Switch
                value={createProductDraft.isSpecialProduct}
                onValueChange={(isSpecialProduct) =>
                  updateCreateProductDraft({ isSpecialProduct })
                }
                disabled={createProductSaving}
              />
            </View>
            <View style={styles.createSwitchRow}>
              <Text variant="bodyMedium">{t("createProduct.fields.isAutoPricing")}</Text>
              <Switch
                value={createProductDraft.isAutoPricing}
                onValueChange={(isAutoPricing) =>
                  updateCreateProductDraft({ isAutoPricing })
                }
                disabled={createProductSaving}
              />
            </View>

            <View style={styles.createProductFooter}>
              <Button
                mode="text"
                onPress={closeCreateProductModal}
                disabled={createProductSaving}
              >
                {t("common:actions.cancel")}
              </Button>
              <Button
                mode="contained"
                loading={createProductSaving}
                disabled={createProductSaving}
                onPress={() => void handleCreateProductSubmit()}
              >
                {t("createProduct.submit")}
              </Button>
            </View>
          </ScrollView>
        </Modal>

        <Modal
          visible={createSupplierPickerVisible}
          onDismiss={createProductSaving ? undefined : () => setCreateSupplierPickerVisible(false)}
          contentContainerStyle={styles.createSupplierPickerModal}
        >
          <View style={styles.createSupplierPickerHeader}>
            <View style={styles.createSupplierPickerTitleWrap}>
              <Text variant="titleMedium" style={styles.createProductTitle}>
                {t("createProduct.supplierPickerTitle")}
              </Text>
              <Text variant="bodySmall" style={styles.createHint}>
                {selectedCreateSupplier
                  ? `${selectedCreateSupplier.supplierCode} - ${selectedCreateSupplier.supplierName || selectedCreateSupplier.supplierCode}`
                  : t("createProduct.selectSupplier")}
              </Text>
            </View>
            <Button compact onPress={() => setCreateSupplierPickerVisible(false)} disabled={createProductSaving}>
              {t("common:actions.close")}
            </Button>
          </View>
          {createSuppliersLoading ? (
            <View style={styles.createSupplierPickerLoading}>
              <Text variant="bodyMedium">{t("createProduct.messages.suppliersLoading")}</Text>
            </View>
          ) : createSuppliers.length ? (
            <ScrollView style={styles.createSupplierPickerList} keyboardShouldPersistTaps="handled">
              {createSuppliers.map((supplier) => {
                const selected = supplier.supplierCode === createProductDraft.localSupplierCode;

                return (
                  <View key={supplier.supplierCode} style={styles.createSupplierPickerRow}>
                    <RadioButton
                      value={supplier.supplierCode}
                      status={selected ? "checked" : "unchecked"}
                      onPress={() => handleSelectCreateSupplier(supplier)}
                      disabled={createProductSaving}
                    />
                    <Button
                      mode={selected ? "contained-tonal" : "text"}
                      compact
                      icon={selected ? "check" : undefined}
                      onPress={() => handleSelectCreateSupplier(supplier)}
                      disabled={createProductSaving}
                      style={styles.createSupplierPickerButton}
                      contentStyle={styles.createSupplierPickerButtonContent}
                    >
                      {supplier.supplierCode} - {supplier.supplierName || supplier.supplierCode}
                    </Button>
                  </View>
                );
              })}
            </ScrollView>
          ) : (
            <Button
              mode="outlined"
              onPress={() => void loadCreateSuppliers()}
              disabled={createProductSaving}
            >
              {t("createProduct.reloadSuppliers")}
            </Button>
          )}
        </Modal>

        <Modal
          visible={Boolean(autoPricingDialog)}
          onDismiss={autoPricingDialogSaving ? undefined : () => {
            if (!autoPricingDialog) {
              return;
            }

            const keepCameraOpen = shouldRestoreCameraScan(autoPricingDialog.scanSource);
            restoreScanAbility(autoPricingDialog.scanSource);
            finishAutoPricingDialog({
              status: "cancelled",
              keepCameraOpen,
              labelPrinted: false,
              updatedDetail: autoPricingDialog.detail,
            });
          }}
          contentContainerStyle={styles.autoPricingModal}
        >
          {autoPricingDialog ? (
            <View style={styles.autoPricingContent}>
              <Text variant="titleMedium" style={styles.autoPricingTitle}>
                {t("autoPricingConfirm.title")}
              </Text>
              <Text variant="bodyMedium" style={styles.autoPricingDescription}>
                {t("autoPricingConfirm.description", {
                  name: autoPricingDialog.detail.productName || t("hero.unnamedProduct"),
                  code: autoPricingDialog.detail.itemNumber || autoPricingDialog.detail.productCode,
                })}
              </Text>
              <View style={styles.autoPricingPriceBlock}>
                <Text variant="bodyMedium" style={styles.autoPricingCurrentPrice}>
                  {t("autoPricingConfirm.currentRetail", {
                    value: autoPricingDialog.evaluation.currentRetailPriceFormatted || "--",
                  })}
                </Text>
                <Text variant="bodyMedium" style={styles.autoPricingNextPrice}>
                  {t("autoPricingConfirm.nextRetail", {
                    value: autoPricingDialog.evaluation.recalculatedRetailPriceFormatted || "--",
                  })}
                </Text>
              </View>
              <Text variant="bodySmall" style={styles.autoPricingHint}>
                {t("autoPricingConfirm.discountHint")}
              </Text>
              <View style={styles.autoPricingActions}>
                <Button
                  mode="outlined"
                  onPress={() => {
                    if (!autoPricingDialog) {
                      return;
                    }

                    const keepCameraOpen = shouldRestoreCameraScan(autoPricingDialog.scanSource);
                    restoreScanAbility(autoPricingDialog.scanSource);
                    finishAutoPricingDialog({
                      status: "cancelled",
                      keepCameraOpen,
                      labelPrinted: false,
                      updatedDetail: autoPricingDialog.detail,
                    });
                  }}
                  disabled={autoPricingDialogSaving}
                >
                  {t("common:actions.cancel")}
                </Button>
                <Button
                  mode="contained"
                  loading={autoPricingDialogSaving}
                  disabled={autoPricingDialogSaving}
                  onPress={async () => {
                    if (!autoPricingDialog) {
                      return;
                    }

                    setAutoPricingDialogSaving(true);
                    const keepCameraOpen = shouldRestoreCameraScan(autoPricingDialog.scanSource);
                    const savedDetail = await persistStorePrice(autoPricingDialog.detail, {
                      retailPrice:
                        autoPricingDialog.evaluation.recalculatedRetailPrice
                        ?? autoPricingDialog.detail.storePrice?.retailPrice
                        ?? null,
                      discountRate: normalizeDiscountRateValue(
                        autoPricingDialog.evaluation.discountRate
                        ?? autoPricingDialog.detail.storePrice?.discountRate
                      ),
                      isAutoPricing: true,
                    });

                    if (!savedDetail) {
                      restoreScanAbility(autoPricingDialog.scanSource);
                      finishAutoPricingDialog({
                        status: "failed",
                        keepCameraOpen,
                        labelPrinted: false,
                        updatedDetail: autoPricingDialog.detail,
                      });
                      return;
                    }

                    const labelPrinted = await sendProductLabel(savedDetail);
                    restoreScanAbility(autoPricingDialog.scanSource);
                    finishAutoPricingDialog({
                      status: "confirmed",
                      keepCameraOpen,
                      labelPrinted,
                      updatedDetail: savedDetail,
                    });
                  }}
                >
                  {t("autoPricingConfirm.confirm")}
                </Button>
              </View>
            </View>
          ) : null}
        </Modal>

        <NumericInputModal
          visible={Boolean(numericInputModal)}
          title={numericInputModal?.title ?? ""}
          value={numericInputModal?.value ?? ""}
          allowDecimal={numericInputModal?.allowDecimal ?? true}
          confirmLabel={numericInputModal?.confirmLabel}
          onChangeValue={(value) =>
            setNumericInputModal((current) => (current ? { ...current, value } : current))
          }
          onConfirm={handleConfirmNumericInputModal}
          onDismiss={dismissNumericInputModal}
        />

        <Modal
          visible={Boolean(barcodeEditModal)}
          onDismiss={() => setBarcodeEditModal(null)}
          contentContainerStyle={styles.textEditModal}
        >
          <View style={styles.textEditModalContent}>
            <Text variant="titleMedium" style={styles.textEditModalTitle}>
              {barcodeEditModal?.title ?? ""}
            </Text>
            <TextInput
              style={styles.textEditInput}
              value={barcodeEditModal?.value ?? ""}
              onChangeText={(value) =>
                setBarcodeEditModal((current) => (current ? { ...current, value } : current))
              }
              autoFocus
              selectTextOnFocus
            />
            <View style={styles.textEditModalFooter}>
              <Button mode="text" onPress={() => setBarcodeEditModal(null)}>
                {t("common:actions.cancel")}
              </Button>
              <Button mode="contained" onPress={() => void handleConfirmBarcodeEdit()}>
                {t("common:actions.apply")}
              </Button>
            </View>
          </View>
        </Modal>

        <Modal
          visible={Boolean(codeAddModal)}
          onDismiss={() => setCodeAddModal(null)}
          contentContainerStyle={styles.textEditModal}
        >
          <View style={styles.textEditModalContent}>
            <Text variant="titleMedium" style={styles.textEditModalTitle}>
              {codeAddModal?.codeType === "set" ? t("setCode.addTitle") : t("multiCode.addTitle")}
            </Text>
            <TextInput
              style={styles.textEditInput}
              value={codeAddModal?.value ?? ""}
              onChangeText={(value) =>
                setCodeAddModal((current) => (current ? { ...current, value } : current))
              }
              placeholder={t("setCode.barcode")}
              autoFocus
              selectTextOnFocus
            />
            <View style={styles.textEditModalFooter}>
              <Button mode="text" onPress={() => setCodeAddModal(null)}>
                {t("common:actions.cancel")}
              </Button>
              <Button mode="contained" onPress={() => void handleConfirmCodeAdd()}>
                {t("common:actions.apply")}
              </Button>
            </View>
          </View>
        </Modal>

        <Modal
          visible={productTypeDialogVisible}
          onDismiss={productTypeSaving ? undefined : () => setProductTypeDialogVisible(false)}
          contentContainerStyle={styles.productTypeModal}
        >
          <View style={styles.productTypeModalContent}>
            <Text variant="titleMedium" style={styles.productTypeModalTitle}>
              {t("hero.productTypeChooseTitle")}
            </Text>
            <View style={styles.productTypeOptions}>
              {PRODUCT_TYPE_OPTIONS.map((type) => {
                const selected = detail?.productType === type;
                const label =
                  type === 0 ? t("hero.productType.normal")
                  : type === 1 ? t("hero.productType.set")
                  : t("hero.productType.multi");
                const description =
                  type === 0 ? t("hero.productTypeDescription.normal")
                  : type === 1 ? t("hero.productTypeDescription.set")
                  : t("hero.productTypeDescription.multi");

                return (
                  <View key={type} style={styles.productTypeOptionCard}>
                    <Button
                      mode={selected ? "contained" : "outlined"}
                      onPress={() => void handleUpdateProductType(type)}
                      disabled={productTypeSaving}
                      loading={productTypeSaving && selected}
                      style={styles.productTypeOptionButton}
                    >
                      {label}
                    </Button>
                    <Text variant="bodySmall" style={styles.productTypeOptionDescription}>
                      {description}
                    </Text>
                  </View>
                );
              })}
            </View>
            <View style={styles.productTypeFooter}>
              <Button
                mode="text"
                onPress={() => setProductTypeDialogVisible(false)}
                disabled={productTypeSaving}
              >
                {t("common:actions.cancel")}
              </Button>
            </View>
          </View>
        </Modal>

        <PrintSettingsModal
          visible={printSettingsVisible}
          continuousPrint={continuousPrintEnabled}
          smallLabel={smallLabel}
          printQuantity={printQuantity}
          quantitySingleUse={quantitySingleUse}
          onToggleContinuousPrint={setContinuousPrintEnabled}
          onToggleSmallLabel={setSmallLabel}
          onChangePrintQuantity={setPrintQuantity}
          onToggleQuantitySingleUse={setQuantitySingleUse}
          onDismiss={() => setPrintSettingsVisible(false)}
        />
        <StorePickerModal
          visible={storePickerVisible}
          stores={stores}
          selectedStoreCode={selectedStoreCode}
          title={t("common:labels.selectStore")}
          cancelLabel={t("common:actions.cancel")}
          onDismiss={() => setStorePickerVisible(false)}
          onSelectStore={handleSelectStore}
        />

        <Modal
          visible={cameraVisible && cameraScanMode === "single"}
          onDismiss={() => setCameraVisible(false)}
          contentContainerStyle={styles.cameraModal}
        >
          <View style={styles.cameraHeader}>
            <Text variant="titleMedium">{t("camera.title")}</Text>
            <Button onPress={() => setCameraVisible(false)}>{t("common:actions.close")}</Button>
          </View>
          {renderCameraScanner()}
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
  cameraModeSelector: {
    marginHorizontal: 12,
    marginBottom: 6,
  },
  inlineCameraPanel: {
    marginHorizontal: 12,
    marginBottom: 8,
    padding: 10,
    borderRadius: 12,
    backgroundColor: "#fff",
    gap: 8,
  },
  returnBar: {
    alignItems: "flex-start",
    paddingTop: 8,
  },
  createProductBar: {
    alignItems: "flex-start",
    paddingTop: 8,
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
  autoPricingModal: {
    marginHorizontal: 18,
    borderRadius: 16,
    backgroundColor: "#fff",
    padding: 16,
  },
  createProductModal: {
    marginHorizontal: 16,
    maxHeight: "88%",
    borderRadius: 16,
    backgroundColor: "#fff",
    padding: 16,
  },
  createProductModalContent: {
    gap: 12,
    paddingBottom: 4,
  },
  createProductTitle: {
    fontWeight: "700",
    color: "#111827",
  },
  createFieldGroup: {
    gap: 6,
  },
  createFieldLabel: {
    color: "#344054",
    fontWeight: "700",
  },
  createHint: {
    color: "#667085",
  },
  createSupplierSelectButton: {
    alignSelf: "stretch",
  },
  createSupplierSelectContent: {
    justifyContent: "flex-start",
  },
  createSupplierPickerModal: {
    marginHorizontal: 16,
    maxHeight: "78%",
    borderRadius: 16,
    backgroundColor: "#fff",
    padding: 16,
    gap: 12,
  },
  createSupplierPickerHeader: {
    flexDirection: "row",
    alignItems: "flex-start",
    justifyContent: "space-between",
    gap: 12,
  },
  createSupplierPickerTitleWrap: {
    flex: 1,
    minWidth: 0,
    gap: 4,
  },
  createSupplierPickerLoading: {
    minHeight: 96,
    alignItems: "center",
    justifyContent: "center",
  },
  createSupplierPickerList: {
    maxHeight: 360,
  },
  createSupplierPickerRow: {
    minHeight: 48,
    flexDirection: "row",
    alignItems: "center",
  },
  createSupplierPickerButton: {
    flex: 1,
    minWidth: 0,
  },
  createSupplierPickerButtonContent: {
    justifyContent: "flex-start",
  },
  createTextInput: {
    borderWidth: 1,
    borderColor: "#CBD5E1",
    borderRadius: 8,
    paddingHorizontal: 12,
    paddingVertical: 9,
    fontSize: 16,
    backgroundColor: "#FFFFFF",
  },
  createSwitchRow: {
    minHeight: 44,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 12,
  },
  createProductFooter: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "flex-end",
    gap: 8,
    paddingTop: 4,
  },
  autoPricingContent: {
    gap: 12,
  },
  autoPricingTitle: {
    fontWeight: "700",
    color: "#111827",
  },
  autoPricingDescription: {
    color: "#344054",
  },
  autoPricingPriceBlock: {
    gap: 6,
    borderRadius: 12,
    backgroundColor: "#F8FAFC",
    padding: 12,
  },
  autoPricingCurrentPrice: {
    color: "#475467",
  },
  autoPricingNextPrice: {
    color: "#1677FF",
    fontWeight: "700",
  },
  autoPricingHint: {
    color: "#667085",
  },
  autoPricingActions: {
    flexDirection: "row",
    justifyContent: "flex-end",
    gap: 8,
  },
  productTypeModal: {
    marginHorizontal: 18,
    borderRadius: 16,
    backgroundColor: "#fff",
    padding: 16,
  },
  textEditModal: {
    marginHorizontal: 18,
    borderRadius: 16,
    backgroundColor: "#fff",
    padding: 16,
  },
  textEditModalContent: {
    gap: 14,
  },
  textEditModalTitle: {
    fontWeight: "700",
    color: "#111827",
  },
  textEditInput: {
    borderWidth: 1,
    borderColor: "#CBD5E1",
    borderRadius: 8,
    paddingHorizontal: 12,
    paddingVertical: 8,
    fontSize: 16,
  },
  textEditModalFooter: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "flex-end",
    gap: 8,
  },
  productTypeModalContent: {
    gap: 12,
  },
  productTypeModalTitle: {
    fontWeight: "700",
    color: "#111827",
  },
  productTypeOptions: {
    gap: 8,
  },
  productTypeOptionCard: {
    gap: 6,
  },
  productTypeOptionButton: {
    justifyContent: "center",
  },
  productTypeOptionDescription: {
    color: "#475467",
    lineHeight: 18,
    paddingHorizontal: 4,
  },
  productTypeFooter: {
    flexDirection: "row",
    justifyContent: "flex-end",
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
