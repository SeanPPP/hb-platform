import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Image, ScrollView, StyleSheet, TextInput as NativeTextInput, View } from "react-native";
import { useFocusEffect } from "@react-navigation/native";
import { CameraView, useCameraPermissions } from "expo-camera";
import { Button, Card, IconButton, Modal, Portal, Searchbar, SegmentedButtons, Snackbar, Switch, Text, TextInput } from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import { EmptyState } from "@/components/ui/EmptyState";
import { LoadingOverlay } from "@/components/ui/LoadingOverlay";
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

function formatNumber(value?: number | null, digits = 2) {
  if (value == null || Number.isNaN(value)) {
    return "";
  }
  return value.toFixed(digits);
}

function ProductField({
  label,
  value,
  onChangeText,
  keyboardType = "numeric",
}: {
  label: string;
  value: string;
  onChangeText: (value: string) => void;
  keyboardType?: "default" | "numeric";
}) {
  return (
    <TextInput
      mode="outlined"
      label={label}
      value={value}
      onChangeText={onChangeText}
      keyboardType={keyboardType}
      style={styles.input}
      dense
    />
  );
}

export default function WarehouseScreen() {
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
    oemPrice: "",
    importPrice: "",
    middlePackageQuantity: "",
    packingQuantity: "",
    volume: "",
    isActive: true,
  });
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
      purchasePrice: formatNumber(item?.purchasePrice),
      retailPrice: formatNumber(item?.retailPrice),
      domesticPrice: formatNumber(item?.domesticPrice),
      oemPrice: formatNumber(item?.oemPrice),
      importPrice: formatNumber(item?.importPrice),
      middlePackageQuantity: item?.middlePackageQuantity?.toString() ?? "",
      packingQuantity: item?.packingQuantity?.toString() ?? "",
      volume: formatNumber(item?.volume, 3),
      isActive: item?.isActive ?? true,
    });
  }, []);

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
        retailPrice: parseNullableNumber(productForm.retailPrice),
        domesticPrice: parseNullableNumber(productForm.domesticPrice),
        oemPrice: parseNullableNumber(productForm.oemPrice),
        importPrice: parseNullableNumber(productForm.importPrice),
        middlePackageQuantity: parseNullableNumber(productForm.middlePackageQuantity),
        packingQuantity: parseNullableNumber(productForm.packingQuantity),
        volume: parseNullableNumber(productForm.volume),
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
    setEditingLocationGuid(null);
    setLocationModalState({ locationCode: "", locationBarcode: "", locationType: 1, status: 1 });
    setLocationModalVisible(true);
  }, []);

  const openEditLocation = useCallback((detail: WarehouseLocationDetail) => {
    setEditingLocationGuid(detail.locationGuid);
    setLocationModalState({
      locationCode: detail.locationCode ?? "",
      locationBarcode: detail.locationBarcode ?? "",
      locationType: detail.locationType ?? 1,
      status: detail.status ?? 1,
    });
    setLocationModalVisible(true);
  }, []);

  const handleSaveLocation = useCallback(async () => {
    if (!locationModalState.locationCode.trim()) {
      return;
    }

    setBusy(true);
    try {
      const detail = editingLocationGuid
        ? await updateLocation(editingLocationGuid, locationModalState)
        : await createLocation(locationModalState);
      setSelectedLocation(detail);
      setLocationModalVisible(false);
      setSnackbar(t("messages.saved"));
      await handleLookupLocations();
    } catch (error) {
      setSnackbar(error instanceof Error ? error.message : t("messages.saveFailed"));
    } finally {
      setBusy(false);
    }
  }, [editingLocationGuid, handleLookupLocations, locationModalState, t]);

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

  if (!hasWarehouseAccess) {
    return (
      <SafeAreaView style={styles.safeArea}>
        <EmptyState
          title={t("messages.noAccessTitle")}
          description={t("messages.noAccessDescription")}
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
                      <Text variant="bodySmall">{productTypeText || notAvailableText}</Text>
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
                      <ProductField label={t("product.fields.purchasePrice")} value={productForm.purchasePrice} onChangeText={(value) => setProductForm((current) => ({ ...current, purchasePrice: value }))} />
                      <ProductField label={t("product.fields.retailPrice")} value={productForm.retailPrice} onChangeText={(value) => setProductForm((current) => ({ ...current, retailPrice: value }))} />
                      <ProductField label={t("product.fields.domesticPrice")} value={productForm.domesticPrice} onChangeText={(value) => setProductForm((current) => ({ ...current, domesticPrice: value }))} />
                      <ProductField label={t("product.fields.oemPrice")} value={productForm.oemPrice} onChangeText={(value) => setProductForm((current) => ({ ...current, oemPrice: value }))} />
                      <ProductField label={t("product.fields.importPrice")} value={productForm.importPrice} onChangeText={(value) => setProductForm((current) => ({ ...current, importPrice: value }))} />
                      <ProductField label={t("product.fields.middlePackageQuantity")} value={productForm.middlePackageQuantity} onChangeText={(value) => setProductForm((current) => ({ ...current, middlePackageQuantity: value }))} />
                      <ProductField label={t("product.fields.packingQuantity")} value={productForm.packingQuantity} onChangeText={(value) => setProductForm((current) => ({ ...current, packingQuantity: value }))} />
                      <ProductField label={t("product.fields.volume")} value={productForm.volume} onChangeText={(value) => setProductForm((current) => ({ ...current, volume: value }))} />
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
          <TextInput mode="outlined" label={t("location.fields.locationCode")} value={locationModalState.locationCode} onChangeText={(value) => setLocationModalState((current) => ({ ...current, locationCode: value }))} style={styles.input} />
          <TextInput mode="outlined" label={t("location.fields.locationBarcode")} value={locationModalState.locationBarcode ?? ""} onChangeText={(value) => setLocationModalState((current) => ({ ...current, locationBarcode: value }))} style={styles.input} />
          <ProductField label={t("location.fields.locationType")} value={String(locationModalState.locationType ?? 1)} onChangeText={(value) => setLocationModalState((current) => ({ ...current, locationType: Number(value || 1) }))} />
          <ProductField label={t("location.fields.status")} value={String(locationModalState.status ?? 1)} onChangeText={(value) => setLocationModalState((current) => ({ ...current, status: Number(value || 1) }))} />
          <Button mode="contained" onPress={() => void handleSaveLocation()} style={styles.primaryButton}>
            {t("common:actions.save")}
          </Button>
        </Modal>
      </Portal>

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
  switchRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    marginTop: 6,
  },
  fieldGrid: {
    gap: 10,
  },
  input: {
    backgroundColor: "#fff",
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
