import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Image, Pressable, ScrollView, StyleSheet, TextInput as NativeTextInput, View } from "react-native";
import { useFocusEffect } from "@react-navigation/native";
import { CameraView, useCameraPermissions } from "expo-camera";
import { useRouter } from "expo-router";
import { Button, Card, IconButton, Menu, Modal, Portal, Searchbar, SegmentedButtons, Snackbar, Switch, Text, TextInput } from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import { EmptyState } from "@/components/ui/EmptyState";
import { LoadingOverlay } from "@/components/ui/LoadingOverlay";
import { NumericInputModal } from "@/components/product-maintenance/NumericInputModal";
import { hasVisibleTabRoute } from "@/modules/navigation/default-route";
import { useAppNavigationStore } from "@/modules/navigation/store";
import { useCameraScan } from "@/modules/scanner/use-camera-scan";
import { useHidBarcodeScanner } from "@/modules/scanner/use-hid-barcode-scanner";
import { resolveLocalizedErrorMessage } from "@/shared/i18n/error-message";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { useAuthStore } from "@/store/auth-store";
import { useDeviceStore } from "@/store/device-store";
import {
  bindProductToLocation,
  createLocation,
  deleteLocation,
  getLocationDetail,
  getWarehouseImageUploadSignature,
  getWarehouseLocationPrintPayload,
  getWarehouseProduct,
  getWarehouseProductPrintPayload,
  lookupLocations,
  lookupWarehouseProducts,
  patchWarehouseProduct,
  setWarehouseProductLocation,
  unbindProductFromLocation,
  updateLocation,
  uploadFileToSignedUrl,
} from "@/modules/warehouse/api";
import type { WarehouseLocation, WarehouseLocationDetail, WarehouseLocationMutation, WarehouseProduct } from "@/modules/warehouse/types";
import { printWarehouseLocationLabel, printWarehouseProductLabel } from "@/modules/printer/api";

type SegmentValue = "product" | "location";
type LocationCodePart = "letter" | "section" | "shelf" | "slot";
type ScannerTarget = "product" | "location" | "bindProduct";
type LocationVisualState = "bound" | "empty" | "lowStock";
type ProductStockState = "inStock" | "lowStock" | "outOfStock";
type NumericProductFieldKey =
  | "purchasePrice"
  | "retailPrice"
  | "domesticPrice"
  | "stockQuantity"
  | "middlePackageQuantity"
  | "packingQuantity"
  | "volume";

interface NumericInputModalState {
  field: NumericProductFieldKey;
  title: string;
  value: string;
  allowDecimal: boolean;
}

const LOCATION_LETTER_OPTIONS = Array.from({ length: 26 }, (_, index) => String.fromCharCode(65 + index));
const LOCATION_NUMBER_OPTIONS = Array.from({ length: 100 }, (_, index) => String(index).padStart(2, "0"));
const PRODUCT_GRADE_OPTIONS = ["A", "B", "C", "D"];
const PRODUCT_GRADE_CONFIG: Record<string, { color: string }> = {
  A: { color: "#722ED1" },
  B: { color: "#1890FF" },
  C: { color: "#FA8C16" },
  D: { color: "#F5222D" },
};
const LOCATION_VISUALS: Record<LocationVisualState, { stripe: string; badgeBackground: string; badgeText: string; badgeBorder: string }> = {
  bound: {
    stripe: "#10B981",
    badgeBackground: "#DCFCE7",
    badgeText: "#166534",
    badgeBorder: "#BBF7D0",
  },
  empty: {
    stripe: "#CBD5E1",
    badgeBackground: "#F1F5F9",
    badgeText: "#475569",
    badgeBorder: "#E2E8F0",
  },
  lowStock: {
    stripe: "#EF4444",
    badgeBackground: "#FEE2E2",
    badgeText: "#B91C1C",
    badgeBorder: "#FECACA",
  },
};
const PRODUCT_STOCK_COLORS: Record<ProductStockState, { background: string; text: string; border: string }> = {
  inStock: { background: "#DCFCE7", text: "#166534", border: "#BBF7D0" },
  lowStock: { background: "#FEF3C7", text: "#B45309", border: "#FDE68A" },
  outOfStock: { background: "#FEE2E2", text: "#B91C1C", border: "#FECACA" },
};

function buildLocationCode(parts: Record<LocationCodePart, string>) {
  return `${parts.letter}-${parts.section}-${parts.shelf}-${parts.slot}`;
}

function splitLocationCode(code?: string | null): Record<LocationCodePart, string> {
  const normalized = (code ?? "").trim().toUpperCase();
  const match = /^([A-Z])-(\d{2})-(\d{2})-(\d{2})$/.exec(normalized);
  if (!match) {
    return { letter: "A", section: "00", shelf: "00", slot: "01" };
  }

  return {
    letter: match[1],
    section: match[2],
    shelf: match[3],
    slot: match[4],
  };
}

function formatNumber(value?: number | null, digits = 2) {
  if (value == null || Number.isNaN(value)) {
    return "";
  }
  return value.toFixed(digits);
}

function formatDisplayValue(value?: string | number | null) {
  if (value == null || value === "") {
    return "--";
  }
  return String(value);
}

function formatPrice(value?: number | null) {
  if (value == null || Number.isNaN(value)) {
    return "--";
  }
  return value.toFixed(2);
}

function getBindInitialQuantityValue(item?: WarehouseProduct | null) {
  if (item?.stockQuantity != null && Number.isFinite(item.stockQuantity) && item.stockQuantity >= 0) {
    return item.stockQuantity.toString();
  }
  return "0";
}

function getLocationVisualState(productCount: number) {
  if (productCount <= 0) {
    return "empty";
  }
  if (productCount === 1) {
    return "lowStock";
  }
  return "bound";
}

function getProductStockState(stockQuantity?: number | null) {
  if (stockQuantity == null) {
    return "inStock";
  }
  if (stockQuantity <= 0) {
    return "outOfStock";
  }
  if (stockQuantity <= 5) {
    return "lowStock";
  }
  return "inStock";
}

function ProductNumericField({
  label,
  value,
  onPress,
}: {
  label: string;
  value: string;
  onPress: () => void;
}) {
  return (
    <Button mode="outlined" compact onPress={onPress} contentStyle={styles.numericFieldContent} style={styles.numericField}>
      <View style={styles.numericFieldInner}>
        <Text variant="labelSmall" style={styles.numericFieldLabel} numberOfLines={1}>{label}</Text>
        <Text variant="bodyMedium" style={styles.numericFieldValue} numberOfLines={1}>{value || "--"}</Text>
      </View>
    </Button>
  );
}

function InfoTile({
  label,
  value,
  emphasize = false,
}: {
  label: string;
  value: string;
  emphasize?: boolean;
}) {
  return (
    <View style={styles.infoTile}>
      <Text variant="labelSmall" style={styles.infoTileLabel}>
        {label}
      </Text>
      <Text variant={emphasize ? "titleMedium" : "bodyMedium"} style={styles.infoTileValue} numberOfLines={2}>
        {value}
      </Text>
    </View>
  );
}

function LocationPartMenu({
  label,
  value,
  options,
  visible,
  onDismiss,
  onOpen,
  onSelect,
}: {
  label: string;
  value: string;
  options: string[];
  visible: boolean;
  onDismiss: () => void;
  onOpen: () => void;
  onSelect: (value: string) => void;
}) {
  return (
    <View style={styles.locationPart}>
      <Text variant="labelSmall" style={styles.locationPartLabel}>
        {label}
      </Text>
      <Menu
        visible={visible}
        onDismiss={onDismiss}
        anchor={
          <Button mode="outlined" compact onPress={onOpen} contentStyle={styles.locationPartButton}>
            {value}
          </Button>
        }
      >
        <ScrollView style={styles.locationPartMenu}>
          {options.map((option) => (
            <Menu.Item
              key={`${label}-${option}`}
              title={option}
              onPress={() => {
                onSelect(option);
                onDismiss();
              }}
            />
          ))}
        </ScrollView>
      </Menu>
    </View>
  );
}

export default function WarehouseScreen() {
  const router = useRouter();
  const { t, language } = useAppTranslation(["warehouse", "common"]);
  const access = useAuthStore((state) => state.access);
  const deviceSession = useDeviceStore((state) => state.session);
  const hasStoredDeviceSession = Boolean(deviceSession?.hardwareId && deviceSession?.authCode);
  const photoCameraRef = useRef<CameraView | null>(null);
  const [photoPermission, requestPhotoPermission] = useCameraPermissions();
  const [segment, setSegment] = useState<SegmentValue>("product");
  const [scannerTarget, setScannerTarget] = useState<ScannerTarget>("product");
  const [snackbar, setSnackbar] = useState("");
  const [busy, setBusy] = useState(false);

  const [productKeyword, setProductKeyword] = useState("");
  const [productMatches, setProductMatches] = useState<WarehouseProduct[]>([]);
  const [product, setProduct] = useState<WarehouseProduct | null>(null);
  const [hasProductLookup, setHasProductLookup] = useState(false);
  const [productForm, setProductForm] = useState({
    purchasePrice: "",
    retailPrice: "",
    domesticPrice: "",
    stockQuantity: "",
    middlePackageQuantity: "",
    packingQuantity: "",
    volume: "",
    grade: "",
    isActive: true,
  });
  const [numericInputModal, setNumericInputModal] = useState<NumericInputModalState | null>(null);
  const [locationLookupKeyword, setLocationLookupKeyword] = useState("");
  const [locationMatches, setLocationMatches] = useState<WarehouseLocation[]>([]);

  const [locationKeyword, setLocationKeyword] = useState("");
  const [locationResults, setLocationResults] = useState<WarehouseLocation[]>([]);
  const [selectedLocation, setSelectedLocation] = useState<WarehouseLocationDetail | null>(null);
  const [hasLocationLookup, setHasLocationLookup] = useState(false);
  const [bindModalVisible, setBindModalVisible] = useState(false);
  const [bindProductKeyword, setBindProductKeyword] = useState("");
  const [bindProductMatches, setBindProductMatches] = useState<WarehouseProduct[]>([]);
  const [selectedBindProduct, setSelectedBindProduct] = useState<WarehouseProduct | null>(null);
  const [bindInitialQuantity, setBindInitialQuantity] = useState("0");
  const [hasBindProductLookup, setHasBindProductLookup] = useState(false);
  const [locationModalVisible, setLocationModalVisible] = useState(false);
  const [locationModalState, setLocationModalState] = useState<WarehouseLocationMutation>({
    locationCode: "",
    locationBarcode: "",
    locationType: 1,
    status: 1,
  });
  const [locationCodeParts, setLocationCodeParts] = useState<Record<LocationCodePart, string>>({
    letter: "A",
    section: "01",
    shelf: "01",
    slot: "01",
  });
  const [locationPartMenus, setLocationPartMenus] = useState<Record<LocationCodePart, boolean>>({
    letter: false,
    section: false,
    shelf: false,
    slot: false,
  });
  const [editingLocationGuid, setEditingLocationGuid] = useState<string | null>(null);
  const [scannerVisible, setScannerVisible] = useState(false);
  const [photoVisible, setPhotoVisible] = useState(false);
  const navigationItems = useAppNavigationStore((state) => state.items);

  const hasWarehouseAccess =
    hasStoredDeviceSession ||
    hasVisibleTabRoute(
      navigationItems.map((item) => item.routeName),
      "warehouse"
    );
  const notAvailableText = t("messages.notAvailable");
  const getErrorMessage = useCallback((error: unknown, fallbackKey: string) => (
    resolveLocalizedErrorMessage(error, {
      language,
      t,
      fallbackKey,
    })
  ), [language, t]);

  const parseNullableNumber = useCallback((value: string) => {
    if (!value.trim()) {
      return null;
    }
    const parsed = Number(value);
    if (!Number.isFinite(parsed)) {
      throw new Error(t("messages.invalidNumber"));
    }
    return parsed;
  }, [t]);

  const parseInitialQuantity = useCallback((value: string) => {
    const trimmed = value.trim();
    if (!trimmed) {
      throw new Error(t("messages.invalidQuantity"));
    }

    const parsed = Number(trimmed);
    if (!Number.isFinite(parsed) || parsed < 0) {
      throw new Error(t("messages.invalidQuantity"));
    }

    return parsed;
  }, [t]);

  const cameraScan = useCameraScan({
    onBarcode: async (barcode) => {
      setScannerVisible(false);
      if (scannerTarget === "location") {
        setLocationKeyword(barcode);
        await handleLookupLocationsByKeyword(barcode);
        return;
      }

      if (scannerTarget === "bindProduct") {
        setBindProductKeyword(barcode);
        await handleLookupBindProducts(barcode);
        return;
      }

      setProductKeyword(barcode);
      await handleLookupProduct(barcode);
    },
  });

  const syncFormFromProduct = useCallback((item: WarehouseProduct | null) => {
    setProductForm({
      purchasePrice: formatNumber(item?.purchasePrice ?? item?.importPrice),
      retailPrice: formatNumber(item?.retailPrice ?? item?.oemPrice),
      domesticPrice: formatNumber(item?.domesticPrice),
      stockQuantity: item?.stockQuantity?.toString() ?? "",
      middlePackageQuantity: item?.middlePackageQuantity?.toString() ?? "",
      packingQuantity: item?.packingQuantity?.toString() ?? "",
      volume: formatNumber(item?.volume, 3),
      grade: item?.grade?.trim().toUpperCase() ?? "",
      isActive: item?.isActive ?? true,
    });
  }, []);

  const openNumericInputModal = useCallback((field: NumericProductFieldKey, title: string, allowDecimal: boolean) => {
    setNumericInputModal({
      field,
      title,
      value: productForm[field],
      allowDecimal,
    });
  }, [productForm]);

  const dismissNumericInputModal = useCallback(() => {
    setNumericInputModal(null);
  }, []);

  const handleConfirmNumericInputModal = useCallback(() => {
    if (!numericInputModal) {
      return;
    }

    setProductForm((current) => ({
      ...current,
      [numericInputModal.field]: numericInputModal.value,
    }));
    dismissNumericInputModal();
  }, [dismissNumericInputModal, numericInputModal]);

  const applyProduct = useCallback((item: WarehouseProduct | null) => {
    setProduct(item);
    syncFormFromProduct(item);
  }, [syncFormFromProduct]);

  const applyLocationDetail = useCallback((detail: WarehouseLocationDetail | null) => {
    setSelectedLocation(detail);
    if (!detail) {
      return;
    }

    setLocationResults((current) => current.map((item) => (
      item.locationGuid === detail.locationGuid
        ? {
            ...item,
            locationCode: detail.locationCode,
            locationBarcode: detail.locationBarcode,
            locationType: detail.locationType,
            status: detail.status,
            productCount: detail.products.length,
          }
        : item
    )));
  }, []);

  const handleLookupProduct = useCallback(async (value?: string) => {
    const keyword = (value ?? productKeyword).trim();
    if (!keyword) {
      setSnackbar(t("messages.keywordRequired"));
      return;
    }

    setBusy(true);
    setHasProductLookup(true);
    try {
      const items = await lookupWarehouseProducts(keyword);
      setProductMatches(items);
      if (items.length === 1) {
        const detail = await getWarehouseProduct(items[0].productCode);
        applyProduct(detail);
      } else {
        applyProduct(null);
      }
    } catch (error) {
      setSnackbar(getErrorMessage(error, "messages.lookupFailed"));
    } finally {
      setBusy(false);
    }
  }, [applyProduct, productKeyword, t]);

  const handleSelectProduct = useCallback(async (productCode: string) => {
    setBusy(true);
    try {
      const detail = await getWarehouseProduct(productCode);
      applyProduct(detail);
    } catch (error) {
      setSnackbar(getErrorMessage(error, "messages.lookupFailed"));
    } finally {
      setBusy(false);
    }
  }, [applyProduct, t]);

  const handleSaveProduct = useCallback(async () => {
    if (!product) {
      return;
    }

    setBusy(true);
    try {
      const saved = await patchWarehouseProduct(product.productCode, {
        purchasePrice: parseNullableNumber(productForm.purchasePrice),
        importPrice: parseNullableNumber(productForm.purchasePrice),
        retailPrice: parseNullableNumber(productForm.retailPrice),
        oemPrice: parseNullableNumber(productForm.retailPrice),
        domesticPrice: parseNullableNumber(productForm.domesticPrice),
        stockQuantity: parseNullableNumber(productForm.stockQuantity),
        middlePackageQuantity: parseNullableNumber(productForm.middlePackageQuantity),
        packingQuantity: parseNullableNumber(productForm.packingQuantity),
        volume: parseNullableNumber(productForm.volume),
        grade: productForm.grade || null,
        isActive: productForm.isActive,
      });
      applyProduct(saved);
      setSnackbar(t("messages.saved"));
    } catch (error) {
      setSnackbar(getErrorMessage(error, "messages.saveFailed"));
    } finally {
      setBusy(false);
    }
  }, [applyProduct, parseNullableNumber, product, productForm, t]);

  const handleLookupLocationsForProduct = useCallback(async () => {
    const keyword = locationLookupKeyword.trim();
    if (!keyword) {
      return;
    }

    setBusy(true);
    try {
      const items = await lookupLocations(keyword);
      setLocationMatches(items);
    } catch (error) {
      setSnackbar(getErrorMessage(error, "messages.locationLookupFailed"));
    } finally {
      setBusy(false);
    }
  }, [locationLookupKeyword, t]);

  const handleBindLocation = useCallback(async (locationGuid?: string | null) => {
    if (!product) {
      return;
    }

    setBusy(true);
    try {
      const saved = await setWarehouseProductLocation(product.productCode, locationGuid ?? null);
      applyProduct(saved);
      setLocationMatches([]);
      setLocationLookupKeyword("");
      setSnackbar(t("messages.locationSaved"));
    } catch (error) {
      setSnackbar(getErrorMessage(error, "messages.saveFailed"));
    } finally {
      setBusy(false);
    }
  }, [applyProduct, product, t]);

  const handlePrintProduct = useCallback(async () => {
    if (!product) {
      return;
    }
    try {
      const payload = await getWarehouseProductPrintPayload(product.productCode);
      await printWarehouseProductLabel(payload);
      setSnackbar(t("messages.printSent"));
    } catch (error) {
      setSnackbar(getErrorMessage(error, "messages.printFailed"));
    }
  }, [product, t]);

  const handlePrintLocation = useCallback(async () => {
    if (!product) {
      return;
    }
    try {
      const payload = await getWarehouseLocationPrintPayload(product.productCode);
      await printWarehouseLocationLabel(payload);
      setSnackbar(t("messages.printSent"));
    } catch (error) {
      setSnackbar(getErrorMessage(error, "messages.printFailed"));
    }
  }, [product, t]);

  const handlePrintSelectedLocation = useCallback(async () => {
    if (!selectedLocation) {
      return;
    }
    try {
      await printWarehouseLocationLabel({
        locationGuid: selectedLocation.locationGuid,
        locationCode: selectedLocation.locationCode,
        locationBarcode: selectedLocation.locationBarcode,
        productCount: selectedLocation.products.length,
      });
      setSnackbar(t("messages.printSent"));
    } catch (error) {
      setSnackbar(getErrorMessage(error, "messages.printFailed"));
    }
  }, [selectedLocation, t]);

  const handleCapturePhoto = useCallback(async () => {
    if (!product || !photoCameraRef.current) {
      return;
    }

    setBusy(true);
    try {
      const picture = await photoCameraRef.current.takePictureAsync({ quality: 0.7 });
      if (!picture?.uri) {
        throw new Error(t("messages.uploadFailed"));
      }
      setSnackbar(t("messages.photoCaptured"));
      const fileName = picture.uri.split("/").pop() || `${product.productCode}.jpg`;
      const fileBlob = await fetch(picture.uri).then((response) => response.blob());
      const signature = await getWarehouseImageUploadSignature(product.productCode, {
        fileName,
        contentType: "image/jpeg",
        fileSize: fileBlob.size,
      });
      await uploadFileToSignedUrl(picture.uri, signature);
      const downloadUrl = signature.url.split("?")[0];
      const saved = await patchWarehouseProduct(product.productCode, { productImage: downloadUrl });
      applyProduct(saved);
      setPhotoVisible(false);
      setSnackbar(t("messages.saved"));
    } catch (error) {
      setSnackbar(getErrorMessage(error, "messages.uploadFailed"));
    } finally {
      setBusy(false);
    }
  }, [applyProduct, product, t]);

  const handleLookupLocationsByKeyword = useCallback(async (value?: string) => {
    const keyword = (value ?? locationKeyword).trim();
    if (!keyword) {
      setSnackbar(t("messages.keywordRequired"));
      return;
    }

    setBusy(true);
    setHasLocationLookup(true);
    try {
      const items = await lookupLocations(keyword);
      setLocationResults(items);
      if (items.length === 1) {
        const detail = await getLocationDetail(items[0].locationGuid);
        applyLocationDetail(detail);
      } else {
        applyLocationDetail(null);
      }
    } catch (error) {
      setSnackbar(getErrorMessage(error, "messages.locationLookupFailed"));
    } finally {
      setBusy(false);
    }
  }, [applyLocationDetail, locationKeyword, t]);

  const handleLookupLocations = useCallback(async () => {
    await handleLookupLocationsByKeyword();
  }, [handleLookupLocationsByKeyword]);

  const hidScanner = useHidBarcodeScanner({
    onScan: async (barcode) => {
      if (bindModalVisible) {
        setBindProductKeyword(barcode);
        await handleLookupBindProducts(barcode);
        return;
      }

      if (segment === "location") {
        setLocationKeyword(barcode);
        await handleLookupLocationsByKeyword(barcode);
        return;
      }

      setProductKeyword(barcode);
      await handleLookupProduct(barcode);
    },
  });

  useFocusEffect(
    useCallback(() => {
      hidScanner.focusHiddenInput?.();
    }, [hidScanner.focusHiddenInput])
  );

  useEffect(() => {
    hidScanner.focusHiddenInput?.();
  }, [hidScanner.focusHiddenInput, segment]);

  const handleSelectLocation = useCallback(async (locationGuid: string) => {
    setBusy(true);
    try {
      const detail = await getLocationDetail(locationGuid);
      applyLocationDetail(detail);
    } catch (error) {
      setSnackbar(getErrorMessage(error, "messages.locationLookupFailed"));
    } finally {
      setBusy(false);
    }
  }, [applyLocationDetail, t]);

  const openCreateLocation = useCallback(() => {
    const initialParts = splitLocationCode(null);
    setEditingLocationGuid(null);
    setLocationCodeParts(initialParts);
    setLocationModalState({ locationCode: buildLocationCode(initialParts), locationBarcode: "", locationType: 1, status: 1 });
    setLocationModalVisible(true);
  }, []);

  const openEditLocation = useCallback((detail: WarehouseLocationDetail) => {
    const nextParts = splitLocationCode(detail.locationCode);
    setEditingLocationGuid(detail.locationGuid);
    setLocationCodeParts(nextParts);
    setLocationModalState({
      locationCode: buildLocationCode(nextParts),
      locationBarcode: detail.locationBarcode ?? "",
      locationType: detail.locationType ?? 1,
      status: detail.status ?? 1,
    });
    setLocationModalVisible(true);
  }, []);

  const handleSaveLocation = useCallback(async () => {
    const locationCode = buildLocationCode(locationCodeParts);
    if (!locationCode.trim()) {
      return;
    }

    setBusy(true);
    try {
      const payload = {
        ...locationModalState,
        locationCode,
        locationBarcode: editingLocationGuid ? locationModalState.locationBarcode : null,
      };
      const detail = editingLocationGuid
        ? await updateLocation(editingLocationGuid, payload)
        : await createLocation(payload);
      applyLocationDetail(detail);
      setLocationModalVisible(false);
      setSnackbar(t("messages.saved"));
      await handleLookupLocations();
    } catch (error) {
      setSnackbar(getErrorMessage(error, "messages.saveFailed"));
    } finally {
      setBusy(false);
    }
  }, [applyLocationDetail, editingLocationGuid, handleLookupLocations, locationCodeParts, locationModalState, t]);

  const handleDeleteLocation = useCallback(async () => {
    if (!selectedLocation) {
      return;
    }
    setBusy(true);
    try {
      await deleteLocation(selectedLocation.locationGuid);
      applyLocationDetail(null);
      setLocationResults((current) => current.filter((item) => item.locationGuid !== selectedLocation.locationGuid));
      setBindModalVisible(false);
      setSnackbar(t("messages.saved"));
    } catch (error) {
      setSnackbar(getErrorMessage(error, "messages.locationDeleteFailed"));
    } finally {
      setBusy(false);
    }
  }, [applyLocationDetail, selectedLocation, t]);

  const openBindProductModal = useCallback(() => {
    setBindProductKeyword("");
    setBindProductMatches([]);
    setSelectedBindProduct(null);
    setBindInitialQuantity("0");
    setHasBindProductLookup(false);
    setBindModalVisible(true);
  }, []);

  const handleLookupBindProducts = useCallback(async (value?: string) => {
    const keyword = (value ?? bindProductKeyword).trim();
    if (!keyword) {
      setSnackbar(t("messages.keywordRequired"));
      return;
    }

    setBusy(true);
    setHasBindProductLookup(true);
    try {
      const items = await lookupWarehouseProducts(keyword);
      setBindProductMatches(items);
      if (items.length === 1) {
        setSelectedBindProduct(items[0]);
        setBindInitialQuantity(getBindInitialQuantityValue(items[0]));
      } else {
        setSelectedBindProduct(null);
        setBindInitialQuantity("0");
      }
    } catch (error) {
      setSnackbar(getErrorMessage(error, "messages.lookupFailed"));
    } finally {
      setBusy(false);
    }
  }, [bindProductKeyword, t]);

  const handleBindProductToLocation = useCallback(async () => {
    const productIdentifier = selectedBindProduct?.productCode ?? bindProductKeyword.trim();
    if (!selectedLocation || !productIdentifier) {
      setSnackbar(t("location.bindModalSelectRequired"));
      return;
    }

    let initialQuantity: number;
    try {
      initialQuantity = parseInitialQuantity(bindInitialQuantity);
    } catch (error) {
      setSnackbar(getErrorMessage(error, "messages.invalidQuantity"));
      return;
    }

    setBusy(true);
    try {
      const detail = await bindProductToLocation(selectedLocation.locationGuid, {
        productIdentifier,
        initialQuantity,
      });
      applyLocationDetail(detail);
      setBindModalVisible(false);
      setBindProductKeyword("");
      setBindProductMatches([]);
      setSelectedBindProduct(null);
      setBindInitialQuantity("0");
      setHasBindProductLookup(false);
      setSnackbar(t("messages.locationBound"));
    } catch (error) {
      const responseData = (error as { response?: { data?: unknown } } | undefined)?.response?.data;
      const rawMessage = typeof responseData === "string"
        ? responseData
        : responseData && typeof responseData === "object" && "message" in responseData
          ? String((responseData as { message?: unknown }).message ?? "")
          : error instanceof Error
            ? error.message
            : "";
      const normalizedMessage = rawMessage.trim().toLowerCase();
      const looksLikeQuantityContractIssue = normalizedMessage.includes("initialquantity")
        || normalizedMessage.includes("initial quantity")
        || normalizedMessage.includes("quantity")
        || normalizedMessage.includes("json")
        || normalizedMessage.includes("convert")
        || normalizedMessage.includes("deserialize")
        || normalizedMessage.includes("validation");
      setSnackbar(looksLikeQuantityContractIssue ? t("messages.quantityBackendRequired") : rawMessage || t("messages.saveFailed"));
    } finally {
      setBusy(false);
    }
  }, [applyLocationDetail, bindInitialQuantity, bindProductKeyword, parseInitialQuantity, selectedBindProduct, selectedLocation, t]);

  const handleUnbindProduct = useCallback(async (productCode: string) => {
    if (!selectedLocation) {
      return;
    }

    setBusy(true);
    try {
      const detail = await unbindProductFromLocation(selectedLocation.locationGuid, productCode);
      applyLocationDetail(detail);
      setSnackbar(t("messages.locationUnbound"));
    } catch (error) {
      setSnackbar(getErrorMessage(error, "messages.saveFailed"));
    } finally {
      setBusy(false);
    }
  }, [applyLocationDetail, selectedLocation, t]);

  const productTypeText = useMemo(() => {
    if (!product) {
      return "";
    }
    return product.productTypeLabel || product.productType?.toString() || "";
  }, [product]);

  const normalizedProductGrade = productForm.grade.trim().toUpperCase();
  const productGradeColor = normalizedProductGrade
    ? PRODUCT_GRADE_CONFIG[normalizedProductGrade]?.color ?? "#98A2B3"
    : "#98A2B3";
  const productStockState = getProductStockState(product?.stockQuantity);
  const selectedLocationProductCount = selectedLocation?.products.length ?? 0;
  const selectedLocationVisualState = getLocationVisualState(selectedLocationProductCount);
  const selectedLocationVisualColors = LOCATION_VISUALS[selectedLocationVisualState];
  const productStockColors = PRODUCT_STOCK_COLORS[productStockState];

  const setLocationPart = useCallback((part: LocationCodePart, value: string) => {
    setLocationCodeParts((current) => {
      const next = { ...current, [part]: value };
      setLocationModalState((modal) => ({ ...modal, locationCode: buildLocationCode(next) }));
      return next;
    });
  }, []);

  const setLocationPartMenuVisible = useCallback((part: LocationCodePart, visible: boolean) => {
    setLocationPartMenus((current) => ({ ...current, [part]: visible }));
  }, []);

  const getLocationTypeLabel = useCallback((locationType?: number | null) => (
    locationType === 2 ? t("location.typeStorage") : t("location.typePick")
  ), [t]);

  const getLocationStatusLabel = useCallback((state: LocationVisualState) => {
    if (state === "empty") {
      return t("location.statusEmpty");
    }
    if (state === "lowStock") {
      return t("location.statusLowStock");
    }
    return t("location.statusBound");
  }, [t]);

  const getProductStockLabel = useCallback((state: ProductStockState) => {
    if (state === "outOfStock") {
      return t("product.stockStates.outOfStock");
    }
    if (state === "lowStock") {
      return t("product.stockStates.lowStock");
    }
    return t("product.stockStates.inStock");
  }, [t]);

  if (!hasWarehouseAccess) {
    return (
      <SafeAreaView style={styles.safeArea}>
        <EmptyState
          title={t("messages.noAccessTitle")}
          description={t("messages.noAccessDescription")}
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
    <SafeAreaView style={styles.safeArea} edges={["top", "left", "right"]}>
      <View style={styles.header}>
        <Text variant="headlineSmall">{t("title")}</Text>
      </View>

      <SegmentedButtons
        value={segment}
        onValueChange={(value) => setSegment(value as SegmentValue)}
        buttons={[
          { value: "product", label: t("segments.product") },
          { value: "location", label: t("segments.location") },
        ]}
        style={styles.segmented}
      />

      <ScrollView contentContainerStyle={styles.content}>
        {segment === "product" ? (
          <>
            <View style={styles.searchRow}>
              <Searchbar
                placeholder={t("product.searchPlaceholder")}
                value={productKeyword}
                onChangeText={setProductKeyword}
                onSubmitEditing={() => void handleLookupProduct()}
                style={styles.search}
              />
              <IconButton
                icon="barcode-scan"
                mode="contained-tonal"
                onPress={() => {
                  setScannerTarget("product");
                  setScannerVisible(true);
                }}
              />
            </View>

            {!product && productMatches.length === 0 ? (
              <EmptyState
                title={hasProductLookup ? t("product.noResultsTitle") : t("product.emptyTitle")}
                description={hasProductLookup ? t("product.noResultsDescription") : t("product.emptyDescription")}
              />
            ) : null}

            {!product && productMatches.length > 1 ? (
              <View style={styles.sectionBlock}>
                <Text variant="titleSmall" style={styles.sectionTitle}>
                  {t("product.multipleTitle")}
                </Text>
                <View style={styles.compactCardList}>
                  {productMatches.map((item) => {
                    const stockState = getProductStockState(item.stockQuantity);
                    const stockColors = PRODUCT_STOCK_COLORS[stockState];
                    return (
                      <Pressable
                        key={item.productCode}
                        onPress={() => void handleSelectProduct(item.productCode)}
                        style={styles.compactProductCard}
                      >
                        {item.productImage ? (
                          <Image source={{ uri: item.productImage }} style={styles.compactProductImage} resizeMode="cover" />
                        ) : (
                          <View style={[styles.compactProductImage, styles.productImagePlaceholder]}>
                            <Text variant="labelSmall" numberOfLines={2}>
                              {item.productCode}
                            </Text>
                          </View>
                        )}
                        <View style={styles.compactProductMeta}>
                          <Text variant="titleSmall" numberOfLines={2}>
                            {item.productName || item.productCode}
                          </Text>
                          <Text variant="bodyMedium" style={styles.mutedText}>
                            {t("product.fields.itemNumber")}: {formatDisplayValue(item.itemNumber || item.productCode)}
                          </Text>
                          <Text variant="bodySmall" style={styles.mutedText} numberOfLines={1}>
                            {t("product.fields.barcode")}: {formatDisplayValue(item.barcode)}
                          </Text>
                        </View>
                        <View style={styles.compactProductAside}>
                          <View style={[styles.statusPill, { backgroundColor: stockColors.background, borderColor: stockColors.border }]}>
                            <Text variant="labelSmall" style={[styles.statusPillText, { color: stockColors.text }]}>
                              {getProductStockLabel(stockState)}
                            </Text>
                          </View>
                          <Text variant="titleSmall" style={styles.priceText}>
                            {formatPrice(item.retailPrice ?? item.oemPrice ?? item.domesticPrice)}
                          </Text>
                        </View>
                      </Pressable>
                    );
                  })}
                </View>
              </View>
            ) : null}

            {product ? (
              <>
                <Card mode="contained" style={styles.card}>
                  <Card.Content style={styles.productHeroCard}>
                    {product.productImage ? (
                      <Image source={{ uri: product.productImage }} style={styles.productImage} resizeMode="cover" />
                    ) : (
                      <View style={[styles.productImage, styles.productImagePlaceholder]}>
                        <Text variant="bodyMedium" numberOfLines={3}>
                          {product.productCode}
                        </Text>
                      </View>
                    )}
                    <View style={styles.heroMeta}>
                      <Text variant="headlineSmall" style={styles.heroTitle}>
                        {product.productName || product.productCode}
                      </Text>
                      <Text variant="titleMedium" style={styles.heroIdentifier}>
                        {formatDisplayValue(product.itemNumber || product.productCode)}
                      </Text>
                      <Text variant="bodyMedium" style={styles.mutedText}>
                        {t("product.fields.barcode")}: {formatDisplayValue(product.barcode)}
                      </Text>
                      <Text variant="bodyMedium" style={styles.mutedText}>
                        {t("product.fields.supplier")}: {formatDisplayValue(product.supplierName || product.localSupplierCode)}
                      </Text>
                      <View style={styles.heroBadgeRow}>
                        <View style={[styles.statusPill, { backgroundColor: productForm.isActive ? "#DCFCE7" : "#F1F5F9", borderColor: productForm.isActive ? "#BBF7D0" : "#E2E8F0" }]}>
                          <Text variant="labelSmall" style={[styles.statusPillText, { color: productForm.isActive ? "#166534" : "#475569" }]}>
                            {productForm.isActive ? t("product.active") : t("product.inactive")}
                          </Text>
                        </View>
                        <View style={[styles.statusPill, { backgroundColor: productStockColors.background, borderColor: productStockColors.border }]}>
                          <Text variant="labelSmall" style={[styles.statusPillText, { color: productStockColors.text }]}>
                            {getProductStockLabel(productStockState)}
                          </Text>
                        </View>
                        {normalizedProductGrade ? (
                          <View style={[styles.gradeBadge, { backgroundColor: productGradeColor }]}>
                            <Text variant="labelSmall" style={styles.gradeBadgeText}>
                              {t("product.gradeText", { grade: normalizedProductGrade })}
                            </Text>
                          </View>
                        ) : null}
                      </View>
                      {productTypeText ? (
                        <Text variant="bodySmall" style={styles.mutedText}>
                          {productTypeText}
                        </Text>
                      ) : null}
                    </View>
                  </Card.Content>
                  <Card.Content style={styles.infoGrid}>
                    <InfoTile label={t("product.fields.itemNumber")} value={formatDisplayValue(product.itemNumber || product.productCode)} />
                    <InfoTile label={t("product.fields.barcode")} value={formatDisplayValue(product.barcode)} />
                    <InfoTile label={t("product.fields.stockQuantity")} value={formatDisplayValue(product.stockQuantity)} emphasize />
                    <InfoTile label={t("product.fields.location")} value={formatDisplayValue(product.locationCode || t("product.noLocation"))} emphasize />
                    <InfoTile label={t("product.fields.purchaseImportPrice")} value={formatPrice(product.purchasePrice ?? product.importPrice)} />
                    <InfoTile label={t("product.fields.retailOemPrice")} value={formatPrice(product.retailPrice ?? product.oemPrice)} />
                    <InfoTile label={t("product.fields.domesticPrice")} value={formatPrice(product.domesticPrice)} />
                    <View style={styles.infoTile}>
                      <View style={styles.switchRow}>
                        <Text variant="labelSmall" style={styles.infoTileLabel}>
                          {t("product.fields.isActive")}
                        </Text>
                        <Switch
                          value={productForm.isActive}
                          onValueChange={(value) => setProductForm((current) => ({ ...current, isActive: value }))}
                        />
                      </View>
                    </View>
                  </Card.Content>
                </Card>

                <Card mode="contained" style={styles.card}>
                  <Card.Title
                    title={t("product.currentLocation")}
                    subtitle={product.locationCode || t("product.noLocation")}
                    right={() => (
                      <Button compact onPress={() => void handleBindLocation(null)}>
                        {t("product.clearLocation")}
                      </Button>
                    )}
                  />
                  <Card.Content style={styles.cardContent}>
                    <View style={styles.searchRow}>
                      <Searchbar
                        placeholder={t("location.searchPlaceholder")}
                        value={locationLookupKeyword}
                        onChangeText={setLocationLookupKeyword}
                        onSubmitEditing={() => void handleLookupLocationsForProduct()}
                        style={styles.search}
                      />
                    </View>
                    {locationMatches.length ? (
                      <View style={styles.compactCardList}>
                        {locationMatches.map((item) => (
                          <Pressable
                            key={item.locationGuid}
                            onPress={() => void handleBindLocation(item.locationGuid)}
                            style={styles.locationCandidateCard}
                          >
                            <View style={styles.locationCandidateMeta}>
                              <Text variant="titleSmall">{item.locationCode || item.locationGuid}</Text>
                              <Text variant="bodySmall" style={styles.mutedText}>
                                {item.locationBarcode || notAvailableText}
                              </Text>
                            </View>
                            <Text variant="bodySmall" style={styles.mutedText}>
                              {t("location.productCountValue", { count: item.productCount })}
                            </Text>
                          </Pressable>
                        ))}
                      </View>
                    ) : (
                      <Text variant="bodySmall" style={styles.secondaryText}>
                        {t("product.locationLookupHint")}
                      </Text>
                    )}
                  </Card.Content>
                </Card>

                <Card mode="contained" style={styles.card}>
                  <Card.Title title={t("product.editorTitle")} />
                  <Card.Content>
                    <View style={styles.fieldGrid}>
                      <View style={styles.fieldRow}>
                        <View style={styles.fieldCell}>
                          <ProductNumericField label={t("product.fields.domesticPrice")} value={productForm.domesticPrice} onPress={() => openNumericInputModal("domesticPrice", t("product.fields.domesticPrice"), true)} />
                        </View>
                        <View style={styles.fieldCell}>
                          <ProductNumericField label={t("product.fields.purchaseImportPrice")} value={productForm.purchasePrice} onPress={() => openNumericInputModal("purchasePrice", t("product.fields.purchaseImportPrice"), true)} />
                        </View>
                        <View style={styles.fieldCell}>
                          <ProductNumericField label={t("product.fields.retailOemPrice")} value={productForm.retailPrice} onPress={() => openNumericInputModal("retailPrice", t("product.fields.retailOemPrice"), true)} />
                        </View>
                      </View>
                      <View style={styles.fieldRow}>
                        <View style={styles.fieldCell}>
                          <ProductNumericField label={t("product.fields.stockQuantity")} value={productForm.stockQuantity} onPress={() => openNumericInputModal("stockQuantity", t("product.fields.stockQuantity"), false)} />
                        </View>
                        <View style={styles.fieldCell}>
                          <ProductNumericField label={t("product.fields.middlePackageQuantity")} value={productForm.middlePackageQuantity} onPress={() => openNumericInputModal("middlePackageQuantity", t("product.fields.middlePackageQuantity"), false)} />
                        </View>
                        <View style={styles.fieldCell}>
                          <ProductNumericField label={t("product.fields.packingQuantity")} value={productForm.packingQuantity} onPress={() => openNumericInputModal("packingQuantity", t("product.fields.packingQuantity"), false)} />
                        </View>
                      </View>
                      <View style={styles.fieldRow}>
                        <View style={styles.fieldCell}>
                          <ProductNumericField label={t("product.fields.volume")} value={productForm.volume} onPress={() => openNumericInputModal("volume", t("product.fields.volume"), true)} />
                        </View>
                        <View style={[styles.fieldCell, styles.gradeCell]}>
                          <Text variant="labelSmall" style={styles.gradeFieldLabel}>
                            {t("product.fields.grade")}
                          </Text>
                          <SegmentedButtons
                            value={normalizedProductGrade}
                            onValueChange={(value) => setProductForm((current) => ({ ...current, grade: value }))}
                            buttons={PRODUCT_GRADE_OPTIONS.map((grade) => ({ value: grade, label: grade }))}
                            style={styles.gradeSegmented}
                          />
                        </View>
                      </View>
                    </View>
                    <Button mode="contained" onPress={() => void handleSaveProduct()} style={styles.primaryButton}>
                      {t("common:actions.save")}
                    </Button>
                  </Card.Content>
                </Card>

                <View style={styles.actionRow}>
                  <Button mode="outlined" icon="camera-outline" onPress={() => setPhotoVisible(true)}>
                    {t("product.takePhoto")}
                  </Button>
                  <Button mode="outlined" icon="printer-outline" onPress={() => void handlePrintProduct()}>
                    {t("product.printProduct")}
                  </Button>
                  <Button mode="outlined" icon="map-marker-outline" onPress={() => void handlePrintLocation()}>
                    {t("product.printLocation")}
                  </Button>
                </View>
              </>
            ) : null}
          </>
        ) : (
          <>
            <View style={styles.searchRow}>
              <Searchbar
                placeholder={t("location.searchPlaceholder")}
                value={locationKeyword}
                onChangeText={setLocationKeyword}
                onSubmitEditing={() => void handleLookupLocations()}
                style={styles.search}
              />
              <Button mode="contained" icon="plus" onPress={openCreateLocation}>
                {t("location.newLocation")}
              </Button>
            </View>

            {!selectedLocation && locationResults.length === 0 ? (
              <EmptyState
                title={hasLocationLookup ? t("location.noResultsTitle") : t("location.emptyTitle")}
                description={hasLocationLookup ? t("location.noResultsDescription") : t("location.emptyDescription")}
              />
            ) : null}

            {locationResults.length ? (
              <View style={styles.sectionBlock}>
                <Text variant="titleSmall" style={styles.sectionTitle}>
                  {t("location.searchResultsTitle")}
                </Text>
                <View style={styles.binCardList}>
                  {locationResults.map((item) => {
                    const visualState = getLocationVisualState(item.productCount);
                    const visualColors = LOCATION_VISUALS[visualState];
                    return (
                      <Pressable
                        key={item.locationGuid}
                        onPress={() => void handleSelectLocation(item.locationGuid)}
                        style={styles.binCard}
                      >
                        <View style={[styles.binStripe, { backgroundColor: visualColors.stripe }]} />
                        <View style={styles.binCardBody}>
                          <View style={styles.binCardHeader}>
                            <View style={styles.binTitleBlock}>
                              <Text variant="titleLarge">{item.locationCode || item.locationGuid}</Text>
                              <Text variant="bodySmall" style={styles.mutedText}>
                                {getLocationTypeLabel(item.locationType)}
                              </Text>
                            </View>
                            <View style={[styles.statusPill, { backgroundColor: visualColors.badgeBackground, borderColor: visualColors.badgeBorder }]}>
                              <Text variant="labelSmall" style={[styles.statusPillText, { color: visualColors.badgeText }]}>
                                {getLocationStatusLabel(visualState)}
                              </Text>
                            </View>
                          </View>
                          <View style={styles.binInfoRow}>
                            <InfoTile label={t("location.fields.locationBarcode")} value={formatDisplayValue(item.locationBarcode)} />
                            <InfoTile label={t("location.productsLabel")} value={t("location.productCountValue", { count: item.productCount })} emphasize />
                          </View>
                          <View style={styles.binFooter}>
                            <Button compact icon="eye-outline" onPress={() => void handleSelectLocation(item.locationGuid)}>
                              {t("common:actions.viewDetail")}
                            </Button>
                          </View>
                        </View>
                      </Pressable>
                    );
                  })}
                </View>
              </View>
            ) : null}

            {selectedLocation ? (
              <View style={styles.sectionBlock}>
                <Text variant="titleSmall" style={styles.sectionTitle}>
                  {t("location.selectedTitle")}
                </Text>
                <View style={styles.binCard}>
                  <View style={[styles.binStripe, { backgroundColor: selectedLocationVisualColors.stripe }]} />
                  <View style={styles.binCardBody}>
                    <View style={styles.binCardHeader}>
                      <View style={styles.binTitleBlock}>
                        <Text variant="headlineSmall">{selectedLocation.locationCode || selectedLocation.locationGuid}</Text>
                        <Text variant="bodySmall" style={styles.mutedText}>
                          {getLocationTypeLabel(selectedLocation.locationType)}
                        </Text>
                      </View>
                      <View style={styles.inlineActions}>
                        <View style={[styles.statusPill, { backgroundColor: selectedLocationVisualColors.badgeBackground, borderColor: selectedLocationVisualColors.badgeBorder }]}>
                          <Text variant="labelSmall" style={[styles.statusPillText, { color: selectedLocationVisualColors.badgeText }]}>
                            {getLocationStatusLabel(selectedLocationVisualState)}
                          </Text>
                        </View>
                        <IconButton icon="printer-outline" size={20} onPress={() => void handlePrintSelectedLocation()} />
                        <IconButton icon="pencil-outline" size={20} onPress={() => openEditLocation(selectedLocation)} />
                        <IconButton icon="delete-outline" size={20} onPress={() => void handleDeleteLocation()} />
                      </View>
                    </View>

                    <View style={styles.binInfoRow}>
                      <InfoTile label={t("location.fields.locationBarcode")} value={formatDisplayValue(selectedLocation.locationBarcode)} />
                      <InfoTile label={t("location.productsLabel")} value={t("location.productCountValue", { count: selectedLocationProductCount })} emphasize />
                    </View>

                    <View style={styles.locationProductsSection}>
                      <View style={styles.sectionHeaderRow}>
                        <Text variant="titleSmall">{t("location.productListTitle")}</Text>
                        <Button compact icon="link-variant" mode="contained" onPress={openBindProductModal}>
                          {t("location.bindProduct")}
                        </Button>
                      </View>

                      {selectedLocation.products.length ? (
                        <View style={styles.locationProductList}>
                          {selectedLocation.products.map((item) => (
                            <View key={`${selectedLocation.locationGuid}-${item.productCode}`} style={styles.locationProductRow}>
                              {item.productImage ? (
                                <Image source={{ uri: item.productImage }} style={styles.locationProductImage} resizeMode="cover" />
                              ) : (
                                <View style={[styles.locationProductImage, styles.productImagePlaceholder]}>
                                  <Text variant="labelSmall">{item.productCode || "--"}</Text>
                                </View>
                              )}
                              <View style={styles.locationProductMeta}>
                                <Text variant="bodyMedium" numberOfLines={1}>
                                  {item.productName || item.productCode || notAvailableText}
                                </Text>
                                <Text variant="bodySmall" style={styles.mutedText} numberOfLines={1}>
                                  {item.itemNumber || item.productCode || notAvailableText}
                                </Text>
                              </View>
                              <Button compact mode="text" onPress={() => item.productCode && void handleUnbindProduct(item.productCode)}>
                                {t("location.unbindProduct")}
                              </Button>
                            </View>
                          ))}
                        </View>
                      ) : (
                        <View style={styles.emptyBinState}>
                          <Text variant="bodyMedium" style={styles.secondaryText}>
                            {t("location.productListEmpty")}
                          </Text>
                          <Button mode="contained" onPress={openBindProductModal}>
                            {t("location.bindProduct")}
                          </Button>
                        </View>
                      )}
                    </View>
                  </View>
                </View>
              </View>
            ) : null}
          </>
        )}
      </ScrollView>

      <Portal>
        <Modal visible={scannerVisible} onDismiss={() => setScannerVisible(false)} contentContainerStyle={styles.modal}>
          <Text variant="titleMedium" style={styles.modalTitle}>
            {scannerTarget === "bindProduct" ? t("location.bindModalScanTitle") : t("camera.scanTitle")}
          </Text>
          {cameraScan.permission?.granted ? (
            <CameraView style={styles.cameraView} {...cameraScan.cameraProps} />
          ) : (
            <View style={styles.permissionBlock}>
              <Text variant="titleMedium">{t("camera.permissionTitle")}</Text>
              <Text variant="bodySmall">{t("camera.permissionDescription")}</Text>
              <Button mode="contained" onPress={() => void cameraScan.requestPermission()}>{t("camera.grantPermission")}</Button>
            </View>
          )}
        </Modal>

        <Modal visible={photoVisible} onDismiss={() => setPhotoVisible(false)} contentContainerStyle={styles.modal}>
          <Text variant="titleMedium" style={styles.modalTitle}>{t("camera.photoTitle")}</Text>
          {photoPermission?.granted ? (
            <>
              <CameraView ref={photoCameraRef} style={styles.cameraView} facing="back" />
              <Button mode="contained" onPress={() => void handleCapturePhoto()} style={styles.primaryButton}>
                {t("camera.capture")}
              </Button>
            </>
          ) : (
            <View style={styles.permissionBlock}>
              <Text variant="titleMedium">{t("camera.permissionTitle")}</Text>
              <Text variant="bodySmall">{t("camera.permissionDescription")}</Text>
              <Button mode="contained" onPress={() => void requestPhotoPermission()}>{t("camera.grantPermission")}</Button>
            </View>
          )}
        </Modal>

        <Modal
          visible={bindModalVisible}
          onDismiss={() => setBindModalVisible(false)}
          style={styles.bottomSheetModal}
          contentContainerStyle={styles.bottomSheetContainer}
        >
          <View style={styles.bottomSheetHandle} />
          <View style={styles.sheetHeader}>
            <View style={styles.sheetHeaderMeta}>
              <Text variant="titleLarge">{t("location.bindModalTitle")}</Text>
              <Text variant="bodySmall" style={styles.secondaryText}>
                {t("location.bindModalCurrentLocation")}: {selectedLocation?.locationCode || selectedLocation?.locationGuid || notAvailableText}
              </Text>
            </View>
            <IconButton icon="close" size={20} onPress={() => setBindModalVisible(false)} />
          </View>

          <ScrollView contentContainerStyle={styles.sheetContent}>
            <View style={styles.searchRow}>
              <Searchbar
                placeholder={t("location.bindModalSearchPlaceholder")}
                value={bindProductKeyword}
                onChangeText={setBindProductKeyword}
                onSubmitEditing={() => void handleLookupBindProducts()}
                style={styles.search}
              />
              <IconButton
                icon="barcode-scan"
                mode="contained-tonal"
                onPress={() => {
                  setScannerTarget("bindProduct");
                  setScannerVisible(true);
                }}
              />
            </View>

            <View style={styles.bindHintCard}>
              <Text variant="labelSmall" style={styles.infoTileLabel}>
                {t("location.bindModalQuantityTitle")}
              </Text>
              <TextInput
                mode="outlined"
                value={bindInitialQuantity}
                onChangeText={setBindInitialQuantity}
                keyboardType="decimal-pad"
                autoCapitalize="none"
                placeholder={t("location.bindModalQuantityPlaceholder")}
                style={styles.bindQuantityInput}
              />
              <Text variant="bodySmall" style={styles.secondaryText}>
                {t("location.bindModalQuantityHint")}
              </Text>
            </View>

            <View style={styles.sectionHeaderRow}>
              <Text variant="titleSmall">{t("location.bindModalSearchResults")}</Text>
            </View>

            {bindProductMatches.length ? (
              <View style={styles.compactCardList}>
                {bindProductMatches.map((item) => {
                  const isSelected = selectedBindProduct?.productCode === item.productCode;
                  return (
                    <Pressable
                      key={item.productCode}
                      onPress={() => {
                        setSelectedBindProduct(item);
                        setBindInitialQuantity(getBindInitialQuantityValue(item));
                      }}
                      style={[
                        styles.bindResultCard,
                        isSelected ? styles.bindResultCardSelected : null,
                      ]}
                    >
                      {item.productImage ? (
                        <Image source={{ uri: item.productImage }} style={styles.bindResultImage} resizeMode="cover" />
                      ) : (
                        <View style={[styles.bindResultImage, styles.productImagePlaceholder]}>
                          <Text variant="labelSmall">{item.productCode}</Text>
                        </View>
                      )}
                      <View style={styles.bindResultMeta}>
                        <Text variant="titleSmall" numberOfLines={1}>
                          {item.productName || item.productCode}
                        </Text>
                        <Text variant="bodySmall" style={styles.mutedText} numberOfLines={1}>
                          {t("product.fields.itemNumber")}: {formatDisplayValue(item.itemNumber || item.productCode)}
                        </Text>
                        <Text variant="bodySmall" style={styles.mutedText} numberOfLines={1}>
                          {t("product.fields.barcode")}: {formatDisplayValue(item.barcode)}
                        </Text>
                      </View>
                      {isSelected ? (
                        <View style={styles.bindResultCheck}>
                          <Text variant="labelSmall" style={styles.bindResultCheckText}>✓</Text>
                        </View>
                      ) : null}
                    </Pressable>
                  );
                })}
              </View>
            ) : hasBindProductLookup ? (
              <Text variant="bodySmall" style={styles.secondaryText}>
                {t("location.bindModalNoResults")}
              </Text>
            ) : (
              <Text variant="bodySmall" style={styles.secondaryText}>
                {t("location.bindModalSearchHint")}
              </Text>
            )}
          </ScrollView>

          <View style={styles.sheetFooter}>
            <Button onPress={() => setBindModalVisible(false)}>{t("common:actions.cancel")}</Button>
            <Button mode="contained" icon="link-variant" onPress={() => void handleBindProductToLocation()}>
              {t("location.bindModalConfirm")}
            </Button>
          </View>
        </Modal>

        <Modal visible={locationModalVisible} onDismiss={() => setLocationModalVisible(false)} contentContainerStyle={styles.modal}>
          <Text variant="titleMedium" style={styles.modalTitle}>
            {editingLocationGuid ? t("location.editLocation") : t("location.newLocation")}
          </Text>
          <View style={styles.generatedLocationCode}>
            <Text variant="labelMedium">{t("location.fields.locationCode")}</Text>
            <Text variant="titleMedium">{buildLocationCode(locationCodeParts)}</Text>
          </View>
          <View style={styles.locationPartRow}>
            <LocationPartMenu
              label={t("location.codeParts.letter")}
              value={locationCodeParts.letter}
              options={LOCATION_LETTER_OPTIONS}
              visible={locationPartMenus.letter}
              onOpen={() => setLocationPartMenuVisible("letter", true)}
              onDismiss={() => setLocationPartMenuVisible("letter", false)}
              onSelect={(value) => setLocationPart("letter", value)}
            />
            <LocationPartMenu
              label={t("location.codeParts.section")}
              value={locationCodeParts.section}
              options={LOCATION_NUMBER_OPTIONS}
              visible={locationPartMenus.section}
              onOpen={() => setLocationPartMenuVisible("section", true)}
              onDismiss={() => setLocationPartMenuVisible("section", false)}
              onSelect={(value) => setLocationPart("section", value)}
            />
            <LocationPartMenu
              label={t("location.codeParts.shelf")}
              value={locationCodeParts.shelf}
              options={LOCATION_NUMBER_OPTIONS}
              visible={locationPartMenus.shelf}
              onOpen={() => setLocationPartMenuVisible("shelf", true)}
              onDismiss={() => setLocationPartMenuVisible("shelf", false)}
              onSelect={(value) => setLocationPart("shelf", value)}
            />
            <LocationPartMenu
              label={t("location.codeParts.slot")}
              value={locationCodeParts.slot}
              options={LOCATION_NUMBER_OPTIONS}
              visible={locationPartMenus.slot}
              onOpen={() => setLocationPartMenuVisible("slot", true)}
              onDismiss={() => setLocationPartMenuVisible("slot", false)}
              onSelect={(value) => setLocationPart("slot", value)}
            />
          </View>
          {editingLocationGuid && locationModalState.locationBarcode ? (
            <View style={styles.generatedLocationCode}>
              <Text variant="labelMedium">{t("location.fields.locationBarcode")}</Text>
              <Text variant="bodyMedium">{locationModalState.locationBarcode}</Text>
            </View>
          ) : (
            <Text variant="bodySmall" style={styles.secondaryText}>{t("location.barcodeAutoHint")}</Text>
          )}
          <SegmentedButtons
            value={String(locationModalState.locationType ?? 1)}
            onValueChange={(value) => setLocationModalState((current) => ({ ...current, locationType: Number(value) }))}
            buttons={[
              { value: "1", label: t("location.typePick") },
              { value: "2", label: t("location.typeStorage") },
            ]}
          />
          <View style={styles.switchRow}>
            <Text variant="bodyMedium">{t("location.fields.status")}</Text>
            <Switch
              value={(locationModalState.status ?? 1) === 1}
              onValueChange={(value) => setLocationModalState((current) => ({ ...current, status: value ? 1 : 0 }))}
            />
          </View>
          <Button mode="contained" onPress={() => void handleSaveLocation()} style={styles.primaryButton}>
            {t("common:actions.save")}
          </Button>
        </Modal>

      </Portal>

      {numericInputModal ? (
        <NumericInputModal
          visible
          title={numericInputModal.title}
          value={numericInputModal.value}
          allowDecimal={numericInputModal.allowDecimal}
          onChangeValue={(value) =>
            setNumericInputModal((current) => (current ? { ...current, value } : current))
          }
          onConfirm={handleConfirmNumericInputModal}
          onDismiss={dismissNumericInputModal}
        />
      ) : null}

      {busy ? <LoadingOverlay /> : null}

      <Snackbar visible={Boolean(snackbar)} onDismiss={() => setSnackbar("")} duration={2500}>
        {snackbar}
      </Snackbar>

      {hidScanner.mode === "textInput" && hidScanner.textInputProps ? (
        <NativeTextInput style={styles.hiddenInput} {...hidScanner.textInputProps} />
      ) : null}
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safeArea: {
    flex: 1,
    backgroundColor: "#F8FAFC",
  },
  header: {
    paddingHorizontal: 16,
    paddingTop: 8,
    paddingBottom: 12,
  },
  segmented: {
    marginHorizontal: 16,
    marginBottom: 12,
  },
  content: {
    padding: 16,
    gap: 12,
    paddingBottom: 56,
  },
  searchRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: 8,
  },
  search: {
    flex: 1,
  },
  card: {
    backgroundColor: "#fff",
    borderRadius: 18,
  },
  cardContent: {
    gap: 10,
  },
  productHeroCard: {
    flexDirection: "row",
    gap: 14,
  },
  productImage: {
    width: 112,
    height: 112,
    borderRadius: 16,
    backgroundColor: "#F1F5F9",
  },
  productImagePlaceholder: {
    alignItems: "center",
    justifyContent: "center",
    padding: 12,
    borderWidth: 1,
    borderColor: "#E2E8F0",
  },
  heroMeta: {
    flex: 1,
    gap: 6,
    minWidth: 0,
  },
  heroTitle: {
    flexShrink: 1,
  },
  heroIdentifier: {
    color: "#0F172A",
  },
  heroBadgeRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: 6,
    flexWrap: "wrap",
  },
  gradeBadge: {
    borderRadius: 999,
    paddingHorizontal: 8,
    paddingVertical: 2,
  },
  gradeBadgeText: {
    color: "#fff",
    fontWeight: "700",
  },
  switchRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
  },
  infoGrid: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 10,
    paddingTop: 14,
  },
  infoTile: {
    width: "48%",
    minWidth: 150,
    backgroundColor: "#F8FAFC",
    borderRadius: 14,
    paddingHorizontal: 12,
    paddingVertical: 10,
    gap: 4,
    borderWidth: 1,
    borderColor: "#E2E8F0",
  },
  infoTileLabel: {
    color: "#64748B",
  },
  infoTileValue: {
    color: "#0F172A",
    fontWeight: "600",
  },
  fieldGrid: {
    gap: 10,
  },
  fieldRow: {
    flexDirection: "row",
    gap: 10,
  },
  fieldCell: {
    flex: 1,
    minWidth: 0,
  },
  numericField: {
    borderRadius: 12,
  },
  numericFieldContent: {
    minHeight: 54,
    justifyContent: "flex-start",
  },
  numericFieldInner: {
    width: "100%",
    alignItems: "flex-start",
    gap: 4,
  },
  numericFieldLabel: {
    color: "#64748B",
  },
  numericFieldValue: {
    color: "#0F172A",
    fontWeight: "700",
  },
  gradeCell: {
    flex: 2,
    gap: 6,
  },
  gradeFieldLabel: {
    color: "#64748B",
  },
  gradeSegmented: {
    minHeight: 36,
  },
  secondaryText: {
    color: "#64748B",
  },
  primaryButton: {
    marginTop: 8,
    borderRadius: 12,
  },
  actionRow: {
    flexDirection: "row",
    gap: 8,
    flexWrap: "wrap",
  },
  inlineActions: {
    flexDirection: "row",
    alignItems: "center",
    gap: 2,
  },
  locationProductRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 12,
    paddingVertical: 10,
    paddingHorizontal: 10,
    borderRadius: 14,
    backgroundColor: "#FFFFFF",
    borderWidth: 1,
    borderColor: "#E2E8F0",
  },
  locationProductImage: {
    width: 44,
    height: 44,
    borderRadius: 10,
    backgroundColor: "#F8FAFC",
  },
  locationProductMeta: {
    flex: 1,
    gap: 2,
  },
  generatedLocationCode: {
    gap: 4,
    padding: 12,
    borderRadius: 12,
    backgroundColor: "#F8FAFC",
  },
  locationPartRow: {
    flexDirection: "row",
    gap: 8,
  },
  locationPart: {
    flex: 1,
    gap: 4,
  },
  locationPartLabel: {
    color: "#64748B",
  },
  locationPartButton: {
    minHeight: 36,
  },
  locationPartMenu: {
    maxHeight: 260,
  },
  modal: {
    backgroundColor: "#fff",
    margin: 16,
    borderRadius: 20,
    padding: 16,
    gap: 12,
  },
  modalTitle: {
    marginBottom: 8,
  },
  cameraView: {
    width: "100%",
    height: 320,
    borderRadius: 12,
    overflow: "hidden",
  },
  permissionBlock: {
    gap: 10,
    alignItems: "center",
  },
  sectionBlock: {
    gap: 10,
  },
  sectionTitle: {
    color: "#0F172A",
  },
  sectionHeaderRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 8,
  },
  compactCardList: {
    gap: 10,
  },
  compactProductCard: {
    backgroundColor: "#FFFFFF",
    borderRadius: 16,
    borderWidth: 1,
    borderColor: "#E2E8F0",
    padding: 12,
    flexDirection: "row",
    alignItems: "center",
    gap: 12,
  },
  compactProductImage: {
    width: 72,
    height: 72,
    borderRadius: 12,
    backgroundColor: "#F8FAFC",
  },
  compactProductMeta: {
    flex: 1,
    gap: 4,
    minWidth: 0,
  },
  compactProductAside: {
    alignItems: "flex-end",
    gap: 8,
  },
  mutedText: {
    color: "#64748B",
  },
  statusPill: {
    paddingHorizontal: 8,
    paddingVertical: 4,
    borderRadius: 999,
    borderWidth: 1,
  },
  statusPillText: {
    fontWeight: "700",
  },
  priceText: {
    color: "#0F172A",
    fontWeight: "700",
  },
  locationCandidateCard: {
    backgroundColor: "#F8FAFC",
    borderRadius: 14,
    borderWidth: 1,
    borderColor: "#E2E8F0",
    paddingHorizontal: 12,
    paddingVertical: 10,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 12,
  },
  locationCandidateMeta: {
    flex: 1,
    gap: 2,
  },
  binCardList: {
    gap: 12,
  },
  binCard: {
    flexDirection: "row",
    backgroundColor: "#FFFFFF",
    borderRadius: 18,
    borderWidth: 1,
    borderColor: "#E2E8F0",
    overflow: "hidden",
  },
  binStripe: {
    width: 4,
  },
  binCardBody: {
    flex: 1,
    paddingHorizontal: 14,
    paddingVertical: 14,
    gap: 12,
  },
  binCardHeader: {
    flexDirection: "row",
    alignItems: "flex-start",
    justifyContent: "space-between",
    gap: 10,
  },
  binTitleBlock: {
    flex: 1,
    gap: 2,
    minWidth: 0,
  },
  binInfoRow: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 10,
  },
  binFooter: {
    flexDirection: "row",
    justifyContent: "flex-start",
  },
  locationProductsSection: {
    gap: 12,
  },
  locationProductList: {
    gap: 10,
  },
  emptyBinState: {
    gap: 12,
    alignItems: "flex-start",
    paddingVertical: 4,
  },
  bottomSheetModal: {
    justifyContent: "flex-end",
    margin: 0,
  },
  bottomSheetContainer: {
    backgroundColor: "#FFFFFF",
    borderTopLeftRadius: 24,
    borderTopRightRadius: 24,
    paddingHorizontal: 16,
    paddingTop: 8,
    paddingBottom: 20,
    maxHeight: "82%",
  },
  bottomSheetHandle: {
    alignSelf: "center",
    width: 44,
    height: 5,
    borderRadius: 999,
    backgroundColor: "#CBD5E1",
    marginBottom: 10,
  },
  sheetHeader: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 8,
  },
  sheetHeaderMeta: {
    flex: 1,
    gap: 2,
  },
  sheetContent: {
    gap: 12,
    paddingBottom: 12,
  },
  bindHintCard: {
    backgroundColor: "#F8FAFC",
    borderRadius: 14,
    paddingHorizontal: 12,
    paddingVertical: 10,
    borderWidth: 1,
    borderColor: "#E2E8F0",
    gap: 4,
  },
  bindQuantityInput: {
    backgroundColor: "#FFFFFF",
  },
  bindResultCard: {
    backgroundColor: "#FFFFFF",
    borderRadius: 14,
    borderWidth: 1,
    borderColor: "#CBD5E1",
    padding: 10,
    flexDirection: "row",
    alignItems: "center",
    gap: 10,
  },
  bindResultCardSelected: {
    borderColor: "#34D399",
    backgroundColor: "#ECFDF5",
  },
  bindResultImage: {
    width: 52,
    height: 52,
    borderRadius: 10,
    backgroundColor: "#F8FAFC",
  },
  bindResultMeta: {
    flex: 1,
    gap: 2,
    minWidth: 0,
  },
  bindResultCheck: {
    minWidth: 28,
    height: 28,
    borderRadius: 14,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: "#34D399",
  },
  bindResultCheckText: {
    color: "#FFFFFF",
    fontWeight: "700",
  },
  sheetFooter: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "flex-end",
    gap: 8,
    paddingTop: 12,
    borderTopWidth: 1,
    borderTopColor: "#E2E8F0",
  },
  hiddenInput: {
    position: "absolute",
    width: 1,
    height: 1,
    opacity: 0,
  },
});
