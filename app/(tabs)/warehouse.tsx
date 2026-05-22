import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Image, ScrollView, StyleSheet, TextInput as NativeTextInput, View } from "react-native";
import { useFocusEffect } from "@react-navigation/native";
import { CameraView, useCameraPermissions } from "expo-camera";
import { useRouter } from "expo-router";
import { Button, Card, IconButton, Menu, Modal, Portal, Searchbar, SegmentedButtons, Snackbar, Switch, Text, TextInput } from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import { EmptyState } from "@/components/ui/EmptyState";
import { LoadingOverlay } from "@/components/ui/LoadingOverlay";
import { NumericInputModal } from "@/components/product-maintenance/NumericInputModal";
import { useCameraScan } from "@/modules/scanner/use-camera-scan";
import { useHidBarcodeScanner } from "@/modules/scanner/use-hid-barcode-scanner";
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
  const { t } = useAppTranslation(["warehouse", "common"]);
  const access = useAuthStore((state) => state.access);
  const deviceSession = useDeviceStore((state) => state.session);
  const hasStoredDeviceSession = Boolean(deviceSession?.hardwareId && deviceSession?.authCode);
  const photoCameraRef = useRef<CameraView | null>(null);
  const [photoPermission, requestPhotoPermission] = useCameraPermissions();
  const [segment, setSegment] = useState<SegmentValue>("product");
  const [snackbar, setSnackbar] = useState("");
  const [busy, setBusy] = useState(false);

  const [productKeyword, setProductKeyword] = useState("");
  const [productMatches, setProductMatches] = useState<WarehouseProduct[]>([]);
  const [product, setProduct] = useState<WarehouseProduct | null>(null);
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
  const [bindProductCode, setBindProductCode] = useState("");
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

  const hasWarehouseAccess = access.isAdmin || access.isWarehouseManager || access.isWarehouseStaff || hasStoredDeviceSession;
  const notAvailableText = t("messages.notAvailable");

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

  const cameraScan = useCameraScan({
    onBarcode: async (barcode) => {
      setScannerVisible(false);
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

  const handleLookupProduct = useCallback(async (value?: string) => {
    const keyword = (value ?? productKeyword).trim();
    if (!keyword) {
      setSnackbar(t("messages.keywordRequired"));
      return;
    }

    setBusy(true);
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
      setSnackbar(error instanceof Error ? error.message : t("messages.lookupFailed"));
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
      setSnackbar(error instanceof Error ? error.message : t("messages.lookupFailed"));
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
      setSnackbar(error instanceof Error ? error.message : t("messages.saveFailed"));
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
      setSnackbar(error instanceof Error ? error.message : t("messages.locationLookupFailed"));
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
      setSnackbar(error instanceof Error ? error.message : t("messages.saveFailed"));
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
      setSnackbar(error instanceof Error ? error.message : t("messages.printFailed"));
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
      setSnackbar(error instanceof Error ? error.message : t("messages.printFailed"));
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
      setSnackbar(error instanceof Error ? error.message : t("messages.printFailed"));
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
      setSnackbar(error instanceof Error ? error.message : t("messages.uploadFailed"));
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
    try {
      const items = await lookupLocations(keyword);
      setLocationResults(items);
      if (items.length === 1) {
        const detail = await getLocationDetail(items[0].locationGuid);
        setSelectedLocation(detail);
      }
    } catch (error) {
      setSnackbar(error instanceof Error ? error.message : t("messages.locationLookupFailed"));
    } finally {
      setBusy(false);
    }
  }, [locationKeyword, t]);

  const handleLookupLocations = useCallback(async () => {
    await handleLookupLocationsByKeyword();
  }, [handleLookupLocationsByKeyword]);

  const hidScanner = useHidBarcodeScanner({
    onScan: async (barcode) => {
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
      setSelectedLocation(detail);
    } catch (error) {
      setSnackbar(error instanceof Error ? error.message : t("messages.locationLookupFailed"));
    } finally {
      setBusy(false);
    }
  }, [t]);

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
      setSelectedLocation(detail);
      setLocationModalVisible(false);
      setSnackbar(t("messages.saved"));
      await handleLookupLocations();
    } catch (error) {
      setSnackbar(error instanceof Error ? error.message : t("messages.saveFailed"));
    } finally {
      setBusy(false);
    }
  }, [editingLocationGuid, handleLookupLocations, locationCodeParts, locationModalState, t]);

  const handleDeleteLocation = useCallback(async () => {
    if (!selectedLocation) {
      return;
    }
    setBusy(true);
    try {
      await deleteLocation(selectedLocation.locationGuid);
      setSelectedLocation(null);
      setLocationResults([]);
      setSnackbar(t("messages.saved"));
    } catch (error) {
      setSnackbar(error instanceof Error ? error.message : t("messages.locationDeleteFailed"));
    } finally {
      setBusy(false);
    }
  }, [selectedLocation, t]);

  const handleBindProductToLocation = useCallback(async () => {
    if (!selectedLocation || !bindProductCode.trim()) {
      return;
    }

    setBusy(true);
    try {
      const detail = await bindProductToLocation(selectedLocation.locationGuid, bindProductCode.trim());
      setSelectedLocation(detail);
      setBindProductCode("");
      setSnackbar(t("messages.locationBound"));
    } catch (error) {
      setSnackbar(error instanceof Error ? error.message : t("messages.saveFailed"));
    } finally {
      setBusy(false);
    }
  }, [bindProductCode, selectedLocation, t]);

  const handleUnbindProduct = useCallback(async (productCode: string) => {
    if (!selectedLocation) {
      return;
    }

    setBusy(true);
    try {
      const detail = await unbindProductFromLocation(selectedLocation.locationGuid, productCode);
      setSelectedLocation(detail);
      setSnackbar(t("messages.locationUnbound"));
    } catch (error) {
      setSnackbar(error instanceof Error ? error.message : t("messages.saveFailed"));
    } finally {
      setBusy(false);
    }
  }, [selectedLocation, t]);

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

  if (!hasWarehouseAccess) {
    return (
      <SafeAreaView style={styles.safeArea}>
        <EmptyState
          title={t("messages.noAccessTitle")}
          description={t("messages.noAccessDescription")}
          primaryAction={{
            label: t("common:actions.goToSettings"),
            icon: "cog-outline",
            onPress: () => router.replace("/(tabs)/settings"),
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
              <IconButton icon="barcode-scan" onPress={() => setScannerVisible(true)} />
            </View>

            {!product && productMatches.length === 0 ? (
              <EmptyState
                title={t("product.emptyTitle")}
                description={t("product.emptyDescription")}
              />
            ) : null}

            {!product && productMatches.length > 1 ? (
              <Card mode="contained" style={styles.card}>
                <Card.Title title={t("product.multipleTitle")} />
                <Card.Content style={styles.cardContent}>
                  {productMatches.map((item) => (
                    <Button key={item.productCode} mode="outlined" onPress={() => void handleSelectProduct(item.productCode)}>
                      {item.itemNumber || item.productCode} {item.productName}
                    </Button>
                  ))}
                </Card.Content>
              </Card>
            ) : null}

            {product ? (
              <>
                <Card mode="contained" style={styles.card}>
                  <Card.Content style={styles.heroCard}>
                    {product.productImage ? (
                      <Image source={{ uri: product.productImage }} style={styles.productImage} resizeMode="cover" />
                    ) : (
                      <View style={[styles.productImage, styles.productImagePlaceholder]}>
                        <Text variant="bodySmall">{product.productCode}</Text>
                      </View>
                    )}
                    <View style={styles.heroMeta}>
                      <Text variant="titleMedium">{product.productName}</Text>
                      <Text variant="bodyMedium">{product.itemNumber || product.productCode}</Text>
                      <Text variant="bodySmall">{product.barcode || notAvailableText}</Text>
                      <View style={styles.heroBadgeRow}>
                        <Text variant="bodySmall" style={styles.heroTypeText}>{productTypeText || notAvailableText}</Text>
                        {normalizedProductGrade ? (
                          <View style={[styles.gradeBadge, { backgroundColor: productGradeColor }]}>
                            <Text variant="labelSmall" style={styles.gradeBadgeText}>
                              {t("product.gradeText", { grade: normalizedProductGrade })}
                            </Text>
                          </View>
                        ) : null}
                      </View>
                      <Text variant="bodySmall">{product.supplierName || product.localSupplierCode || notAvailableText}</Text>
                      <View style={styles.switchRow}>
                        <Text variant="bodyMedium">{t("product.fields.isActive")}</Text>
                        <Switch value={productForm.isActive} onValueChange={(value) => setProductForm((current) => ({ ...current, isActive: value }))} />
                      </View>
                    </View>
                  </Card.Content>
                </Card>

                <Card mode="contained" style={styles.card}>
                  <Card.Title title={t("segments.product")} />
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

                <Card mode="contained" style={styles.card}>
                  <Card.Title title={t("product.currentLocation")} subtitle={product.locationCode || t("product.noLocation")} />
                  <Card.Content style={styles.cardContent}>
                    <View style={styles.searchRow}>
                      <Searchbar
                        placeholder={t("location.searchPlaceholder")}
                        value={locationLookupKeyword}
                        onChangeText={setLocationLookupKeyword}
                        onSubmitEditing={() => void handleLookupLocationsForProduct()}
                        style={styles.search}
                      />
                      <Button onPress={() => void handleBindLocation(null)}>{t("product.clearLocation")}</Button>
                    </View>
                    {locationMatches.map((item) => (
                      <Button key={item.locationGuid} mode="outlined" onPress={() => void handleBindLocation(item.locationGuid)}>
                        {item.locationCode || item.locationBarcode || item.locationGuid}
                      </Button>
                    ))}
                  </Card.Content>
                </Card>

                <View style={styles.actionRow}>
                  <Button mode="outlined" onPress={() => setPhotoVisible(true)}>{t("product.takePhoto")}</Button>
                  <Button mode="outlined" onPress={() => void handlePrintProduct()}>{t("product.printProduct")}</Button>
                  <Button mode="outlined" onPress={() => void handlePrintLocation()}>{t("product.printLocation")}</Button>
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
              <Button onPress={openCreateLocation}>{t("location.newLocation")}</Button>
            </View>

            {!selectedLocation && locationResults.length === 0 ? (
              <EmptyState title={t("location.emptyTitle")} description={t("location.emptyDescription")} />
            ) : null}

            {locationResults.map((item) => (
              <Card key={item.locationGuid} mode="contained" style={styles.card}>
                <Card.Title
                  title={item.locationCode || item.locationGuid}
                  subtitle={item.locationBarcode || notAvailableText}
                  right={() => <Button onPress={() => void handleSelectLocation(item.locationGuid)}>{t("common:actions.viewDetail")}</Button>}
                />
              </Card>
            ))}

            {selectedLocation ? (
              <Card mode="contained" style={styles.card}>
                <Card.Title
                  title={selectedLocation.locationCode || selectedLocation.locationGuid}
                  subtitle={selectedLocation.locationBarcode || notAvailableText}
                  right={() => (
                    <View style={styles.inlineActions}>
                      <IconButton icon="printer-outline" onPress={() => void handlePrintSelectedLocation()} />
                      <IconButton icon="pencil-outline" onPress={() => openEditLocation(selectedLocation)} />
                      <IconButton icon="delete-outline" onPress={() => void handleDeleteLocation()} />
                    </View>
                  )}
                />
                <Card.Content style={styles.cardContent}>
                  <TextInput
                    mode="outlined"
                    label={t("location.bindProduct")}
                    placeholder={t("location.lookupProductPlaceholder")}
                    value={bindProductCode}
                    onChangeText={setBindProductCode}
                  />
                  <Button mode="contained" onPress={() => void handleBindProductToLocation()}>
                    {t("location.bindProduct")}
                  </Button>
                  {selectedLocation.products.map((item) => (
                    <View key={`${selectedLocation.locationGuid}-${item.productCode}`} style={styles.locationProductRow}>
                      <View style={styles.locationProductMeta}>
                        <Text variant="bodyMedium">{item.productName || item.productCode || notAvailableText}</Text>
                        <Text variant="bodySmall">{item.itemNumber || item.productCode || notAvailableText}</Text>
                      </View>
                      <Button compact mode="text" onPress={() => item.productCode && void handleUnbindProduct(item.productCode)}>
                        {t("location.unbindProduct")}
                      </Button>
                    </View>
                  ))}
                </Card.Content>
              </Card>
            ) : null}
          </>
        )}
      </ScrollView>

      <Portal>
        <Modal visible={scannerVisible} onDismiss={() => setScannerVisible(false)} contentContainerStyle={styles.modal}>
          <Text variant="titleMedium" style={styles.modalTitle}>{t("camera.scanTitle")}</Text>
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
    backgroundColor: "#fff",
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
    paddingBottom: 40,
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
  },
  cardContent: {
    gap: 10,
  },
  heroCard: {
    flexDirection: "row",
    gap: 12,
  },
  productImage: {
    width: 110,
    height: 110,
    borderRadius: 12,
    backgroundColor: "#F1F5F9",
  },
  productImagePlaceholder: {
    alignItems: "center",
    justifyContent: "center",
    padding: 12,
  },
  heroMeta: {
    flex: 1,
    gap: 4,
  },
  heroBadgeRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: 6,
  },
  heroTypeText: {
    flexShrink: 1,
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
    marginTop: 6,
  },
  fieldGrid: {
    gap: 10,
  },
  fieldRow: {
    flexDirection: "row",
    gap: 8,
  },
  fieldCell: {
    flex: 1,
    minWidth: 0,
  },
  input: {
    backgroundColor: "#fff",
  },
  numericField: {
    borderRadius: 8,
  },
  numericFieldContent: {
    minHeight: 42,
    justifyContent: "flex-start",
  },
  numericFieldInner: {
    width: "100%",
    alignItems: "flex-start",
  },
  numericFieldLabel: {
    color: "#64748B",
  },
  numericFieldValue: {
    color: "#111827",
    fontWeight: "700",
  },
  gradeCell: {
    flex: 2,
    gap: 4,
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
  },
  actionRow: {
    flexDirection: "row",
    gap: 8,
    flexWrap: "wrap",
  },
  inlineActions: {
    flexDirection: "row",
    alignItems: "center",
  },
  locationProductRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 12,
    paddingVertical: 6,
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
    borderRadius: 16,
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
  hiddenInput: {
    position: "absolute",
    width: 1,
    height: 1,
    opacity: 0,
  },
});
