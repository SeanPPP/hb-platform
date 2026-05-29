import { useCallback, useEffect, useMemo, useState } from "react";
import { FlatList, ScrollView, StyleSheet, TextInput, useWindowDimensions, View } from "react-native";
import { useQuery } from "@tanstack/react-query";
import { CameraView } from "expo-camera";
import { useRouter } from "expo-router";
import { useFocusEffect } from "@react-navigation/native";
import {
  Badge,
  Button,
  Card,
  Chip,
  IconButton,
  Modal,
  Portal,
  Searchbar,
  Snackbar,
  Text,
} from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import { EmptyState } from "@/components/ui/EmptyState";
import { LoadingOverlay } from "@/components/ui/LoadingOverlay";
import { ProductCard } from "@/components/ui/ProductCard";
import { getCategoryTree } from "@/modules/shop/api";
import { resolveMinimumOrderQuantity, useAddToCart } from "@/modules/shop/use-add-to-cart";
import { useCartSummary } from "@/modules/shop/use-cart-summary";
import { useProductGrades } from "@/modules/shop/use-product-grades";
import { useProducts } from "@/modules/shop/use-products";
import { useStores } from "@/modules/shop/use-stores";
import { useUpdateCartQuantity } from "@/modules/shop/use-update-cart-quantity";
import { useCameraScan } from "@/modules/scanner/use-camera-scan";
import { useHidBarcodeScanner } from "@/modules/scanner/use-hid-barcode-scanner";
import { useScanResult } from "@/modules/scanner/use-scan-result";
import { ScanResultPicker } from "@/components/ui/ScanResultPicker";
import type { StoreOrderCategoryNode, StoreOrderProductItem } from "@/modules/shop/types";
import { useCartStore } from "@/store/cart-store";
import { resolveLocalizedErrorMessage } from "@/shared/i18n/error-message";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

function resolveDisplayCategories(tree: StoreOrderCategoryNode[]) {
  const allNode = tree.find((item) => item.categoryName.toLowerCase().includes("all"));
  return allNode?.children?.length ? allNode.children : tree;
}

function findCategoryName(
  nodes: StoreOrderCategoryNode[],
  categoryGUID: string | undefined
): string | undefined {
  if (!categoryGUID) {
    return undefined;
  }

  for (const node of nodes) {
    if (node.categoryGUID === categoryGUID) {
      return node.categoryName;
    }

    const childName = findCategoryName(node.children ?? [], categoryGUID);
    if (childName) {
      return childName;
    }
  }

  return undefined;
}

function normalizeGradeValue(value: string | null | undefined) {
  return value?.trim().toUpperCase() || "";
}

export default function Home() {
  const { t, language } = useAppTranslation(["home", "common"]);
  const { height: windowHeight } = useWindowDimensions();
  const router = useRouter();
  const {
    stores,
    selectedStore,
    selectedStoreCode,
    selectStore,
    isLoading: storesLoading,
    isError: storesLoadFailed,
    error: storesError,
    refetch: refetchStores,
  } = useStores();
  const cartSummary = useCartStore((state) => state.cartSummary);
  const [cameraVisible, setCameraVisible] = useState(false);
  const [storePickerVisible, setStorePickerVisible] = useState(false);
  const [filtersVisible, setFiltersVisible] = useState(false);
  const [gradeFilterVisible, setGradeFilterVisible] = useState(false);
  const [searchInput, setSearchInput] = useState("");
  const [autoAddWhenSingle, setAutoAddWhenSingle] = useState(true);
  const [keyword, setKeyword] = useState("");
  const [scannedProducts, setScannedProducts] = useState<StoreOrderProductItem[] | null>(null);
  const [selectedCategoryGUID, setSelectedCategoryGUID] = useState<string | undefined>();
  const [selectedGrade, setSelectedGrade] = useState<string | undefined>();
  const [expandedCategoryGUIDs, setExpandedCategoryGUIDs] = useState<string[]>([]);
  const [pageNumber, setPageNumber] = useState(1);
  const [snackbarMessage, setSnackbarMessage] = useState("");
  const [activeCartMutationProductCode, setActiveCartMutationProductCode] = useState<string | null>(null);
  const getErrorMessage = useCallback((error: unknown, fallbackKey: string) => (
    resolveLocalizedErrorMessage(error, {
      language,
      t,
      fallbackKey,
    })
  ), [language, t]);
  const addToCart = useAddToCart(selectedStoreCode);
  const updateCartQuantity = useUpdateCartQuantity(selectedStoreCode);

  useCartSummary(selectedStoreCode);
  const handleScanLookupProduct = useCallback(
    async (product: StoreOrderProductItem) => {
      setSearchInput("");
      setKeyword("");
      setScannedProducts([product]);
      setSelectedCategoryGUID(undefined);
      setSelectedGrade(undefined);

      if (!autoAddWhenSingle) {
        return;
      }

      setActiveCartMutationProductCode(product.productCode);
      try {
        await addToCart.mutateAsync({
          product,
          quantity: resolveMinimumOrderQuantity(product),
        });
        setSnackbarMessage(
          t("messages.addedToCart", { name: product.productName || product.productCode })
        );
      } catch (error) {
        setSnackbarMessage(getErrorMessage(error, "messages.scanAddFailed"));
      } finally {
        setActiveCartMutationProductCode(null);
      }
    },
    [addToCart, autoAddWhenSingle, getErrorMessage, t]
  );
  const scanResult = useScanResult({
    autoAddWhenSingle,
    mode: "lookup",
    onProductFound: handleScanLookupProduct,
    storeCode: selectedStoreCode,
  });
  const cameraScan = useCameraScan({
    onBarcode: async (barcode) => {
      await scanResult.handleBarcode(barcode, "camera");
    },
  });
  const hidScanner = useHidBarcodeScanner({
    onScan: async (barcode) => {
      await scanResult.handleBarcode(barcode, "hid");
    },
  });

  useFocusEffect(
    useCallback(() => {
      if (hidScanner.focusHiddenInput) {
        hidScanner.focusHiddenInput();
      }
    }, [hidScanner.focusHiddenInput]),
  );

  const categoriesQuery = useQuery({
    queryKey: ["shopCategories"],
    queryFn: getCategoryTree,
  });

  const categoryOptions = useMemo(
    () => resolveDisplayCategories(categoriesQuery.data ?? []),
    [categoriesQuery.data]
  );
  const selectedCategoryName = useMemo(
    () => findCategoryName(categoryOptions, selectedCategoryGUID) ?? t("filters.all"),
    [categoryOptions, selectedCategoryGUID, t]
  );
  const productGradesQuery = useProductGrades();
  const gradeOptions = useMemo(() => productGradesQuery.data ?? [], [productGradesQuery.data]);
  const selectedGradeLabel = useMemo(() => {
    if (!selectedGrade) {
      return t("filters.all");
    }

    return (
      gradeOptions.find((item) => normalizeGradeValue(item.value) === selectedGrade)?.label ??
      selectedGrade
    );
  }, [gradeOptions, selectedGrade, t]);
  const productsQuery = useProducts({
    storeCode: selectedStoreCode ?? undefined,
    itemNumber: keyword || undefined,
    categoryGUID: selectedCategoryGUID,
    grade: selectedGrade,
    pageNumber,
    pageSize: 18,
    sortBy: "Default",
  });

  useEffect(() => {
    setPageNumber(1);
  }, [keyword, selectedCategoryGUID, selectedGrade, selectedStoreCode]);

  useEffect(() => {
    if (
      scanResult.feedback.status === "ready" ||
      scanResult.feedback.status === "scanning" ||
      scanResult.feedback.status === "found" ||
      scanResult.feedback.status === "multiple"
    ) {
      return;
    }

    setSnackbarMessage(scanResult.feedback.message);
  }, [scanResult.feedback.message, scanResult.feedback.status]);

  useEffect(() => {
    if (!storesLoadFailed) {
      return;
    }

    setSnackbarMessage(getErrorMessage(storesError, "messages.storesLoadFailed"));
  }, [getErrorMessage, storesError, storesLoadFailed]);

  useEffect(() => {
    if (!productsQuery.isError) {
      return;
    }

    setSnackbarMessage(getErrorMessage(productsQuery.error, "messages.productsLoadFailed"));
  }, [getErrorMessage, productsQuery.error, productsQuery.isError]);

  const canGoNextPage = useMemo(() => {
    const total = productsQuery.data?.total ?? 0;
    return pageNumber * 18 < total;
  }, [pageNumber, productsQuery.data?.total]);
  const isCompactLayout = windowHeight < 780;
  const productListContentStyle = useMemo(
    () => [styles.listContent, isCompactLayout ? styles.listContentCompact : null],
    [isCompactLayout]
  );
  const displayProducts = useMemo(() => {
    if (scannedProducts?.length) {
      return selectedGrade
        ? scannedProducts.filter((product) => normalizeGradeValue(product.grade) === selectedGrade)
        : scannedProducts;
    }

    return productsQuery.data?.items ?? [];
  }, [productsQuery.data?.items, scannedProducts, selectedGrade]);
  const displayDynamicDataMap = useMemo(() => {
    const mergedMap = { ...productsQuery.dynamicDataMap };

    if (!scannedProducts?.length || !cartSummary?.items?.length) {
      return mergedMap;
    }

    scannedProducts.forEach((product) => {
      const cartItem = cartSummary.items.find((item) => item.productCode === product.productCode);
      if (!cartItem) {
        return;
      }

      mergedMap[product.productCode] = {
        ...mergedMap[product.productCode],
        productCode: product.productCode,
        cartQuantity: cartItem.quantity ?? 0,
      };
    });

    return mergedMap;
  }, [cartSummary?.items, productsQuery.dynamicDataMap, scannedProducts]);
  const handleApplySearch = useCallback(() => {
    setScannedProducts(null);
    setKeyword(searchInput.trim());
  }, [searchInput]);
  const handleClearSearchAndScan = useCallback(() => {
      setScannedProducts(null);
      setSearchInput("");
      setKeyword("");
      setSelectedGrade(undefined);
  }, []);

  const toggleCategoryExpanded = useCallback((categoryGUID: string) => {
    setExpandedCategoryGUIDs((currentValue) =>
      currentValue.includes(categoryGUID)
        ? currentValue.filter((item) => item !== categoryGUID)
        : [...currentValue, categoryGUID]
    );
  }, []);

  const renderCategoryTree = useCallback(
    (nodes: StoreOrderCategoryNode[], depth = 0): React.ReactNode =>
      nodes.map((node) => {
        const hasChildren = Boolean(node.children?.length);
        const isExpanded = expandedCategoryGUIDs.includes(node.categoryGUID);
        const isSelected = selectedCategoryGUID === node.categoryGUID;

        return (
          <View key={node.categoryGUID} style={[styles.categoryTreeNode, depth ? { marginLeft: depth * 14 } : null]}>
            <View style={[styles.categoryTreeRow, isSelected ? styles.categoryTreeRowSelected : null]}>
              <Button
                compact
                mode={isSelected ? "contained-tonal" : "text"}
                onPress={() =>
                  setSelectedCategoryGUID((currentValue) =>
                    currentValue === node.categoryGUID ? undefined : node.categoryGUID
                  )
                }
                style={styles.categoryTreeButton}
                contentStyle={styles.categoryTreeButtonContent}
                labelStyle={styles.categoryTreeButtonLabel}
              >
                {node.categoryName}
              </Button>
              {hasChildren ? (
                <IconButton
                  icon={isExpanded ? "chevron-up" : "chevron-down"}
                  size={18}
                  onPress={() => toggleCategoryExpanded(node.categoryGUID)}
                  style={styles.categoryTreeToggle}
                />
              ) : null}
            </View>

            {hasChildren && isExpanded ? (
              <View style={styles.categoryTreeChildren}>{renderCategoryTree(node.children ?? [], depth + 1)}</View>
            ) : null}
          </View>
        );
      }),
    [expandedCategoryGUIDs, selectedCategoryGUID, toggleCategoryExpanded]
  );

  async function handleAddToCart(product: StoreOrderProductItem) {
    setActiveCartMutationProductCode(product.productCode);
    try {
      await addToCart.mutateAsync({ product });
      setSnackbarMessage(
        t("messages.addedToCart", { name: product.productName || product.productCode })
      );
    } catch (error) {
      setSnackbarMessage(getErrorMessage(error, "messages.addFailed"));
    } finally {
      setActiveCartMutationProductCode(null);
    }
  }

  function getCurrentCartQuantity(productCode: string) {
    return displayDynamicDataMap[productCode]?.cartQuantity ?? 0;
  }

  async function handleIncreaseCartQuantity(product: StoreOrderProductItem) {
    const step = resolveMinimumOrderQuantity(product);
    const nextQuantity = getCurrentCartQuantity(product.productCode) + step;
    setActiveCartMutationProductCode(product.productCode);

    try {
      await updateCartQuantity.mutateAsync({
        nextQuantity,
        product,
      });
    } catch (error) {
      setSnackbarMessage(getErrorMessage(error, "messages.updateQtyFailed"));
    } finally {
      setActiveCartMutationProductCode(null);
    }
  }

  async function handleDecreaseCartQuantity(product: StoreOrderProductItem, currentQuantity: number) {
    const step = resolveMinimumOrderQuantity(product);
    const nextQuantity = Math.max(0, currentQuantity - step);
    setActiveCartMutationProductCode(product.productCode);

    try {
      await updateCartQuantity.mutateAsync({
        nextQuantity,
        product,
      });
    } catch (error) {
      setSnackbarMessage(getErrorMessage(error, "messages.updateQtyFailed"));
    } finally {
      setActiveCartMutationProductCode(null);
    }
  }

  const fixedHeaderContent = (
    <View style={[styles.header, isCompactLayout ? styles.headerCompact : null]}>
      <View style={[styles.headerTopRow, isCompactLayout ? styles.headerTopRowCompact : null]}>
        <View style={styles.headerTitleWrap}>
          <Text variant="titleLarge" style={styles.headerTitle}>{t("title")}</Text>
          <Text variant="bodySmall" style={styles.headerSubtitle}>
            {selectedStore?.storeName || t("common:labels.selectStore")}
          </Text>
        </View>
        <View style={styles.cartBox}>
          <IconButton
            icon="cart-outline"
            size={18}
            onPress={() => router.push("/(tabs)/cart")}
            style={styles.cartButton}
          />
          {cartSummary?.totalQuantity ? <Badge style={styles.badge}>{cartSummary.totalQuantity}</Badge> : null}
          <Text variant="labelMedium" style={styles.cartText}>
            {t("cartSummary.total")} {cartSummary?.totalQuantity ?? 0}
          </Text>
        </View>
      </View>
      <View style={[styles.searchRow, isCompactLayout ? styles.searchRowCompact : null]}>
        <View style={styles.searchInputWrap}>
          <Searchbar
            placeholder={t("searchPlaceholder")}
            value={searchInput}
            onChangeText={(value) => {
              setSearchInput(value);
              setScannedProducts(null);
            }}
            onSubmitEditing={handleApplySearch}
            onIconPress={handleApplySearch}
            style={styles.searchInput}
            inputStyle={styles.searchInputText}
          />
        </View>
        <IconButton
          icon="camera-outline"
          mode="contained-tonal"
          accessibilityLabel={t("cameraQuery")}
          onPress={() => setCameraVisible(true)}
          style={styles.cameraQueryButton}
        />
        <IconButton
          icon="filter-variant"
          mode="outlined"
          accessibilityLabel={t("common:actions.openFilters")}
          onPress={() => setFiltersVisible(true)}
          style={styles.filterToggleButton}
        />
      </View>
      <ScrollView
        horizontal
        showsHorizontalScrollIndicator={false}
        contentContainerStyle={styles.utilityRow}
      >
        <Chip
          compact
          mode="outlined"
          icon="storefront-outline"
          onPress={() => setStorePickerVisible(true)}
          style={styles.utilityChip}
          textStyle={styles.utilityChipText}
        >
          {selectedStore?.storeName || t("common:labels.selectStore")}
        </Chip>
        <Chip
          compact
          mode={selectedCategoryGUID ? "flat" : "outlined"}
          icon="shape-outline"
          onPress={() => setFiltersVisible(true)}
          style={[styles.utilityChip, selectedCategoryGUID ? styles.utilityChipActive : null]}
          textStyle={[styles.utilityChipText, selectedCategoryGUID ? styles.utilityChipTextActive : null]}
        >
          {selectedCategoryName}
        </Chip>
        <Chip
          compact
          mode={selectedGrade ? "flat" : "outlined"}
          icon="label-outline"
          onPress={() => setGradeFilterVisible(true)}
          style={[styles.utilityChip, selectedGrade ? styles.utilityChipActive : null]}
          textStyle={[styles.utilityChipText, selectedGrade ? styles.utilityChipTextActive : null]}
        >
          {selectedGrade ? t("common:grade", { grade: selectedGrade }) : t("filters.grade")}
        </Chip>
        <Chip
          compact
          mode={autoAddWhenSingle ? "flat" : "outlined"}
          selected={autoAddWhenSingle}
          icon={autoAddWhenSingle ? "cart-check" : "cart-off"}
          onPress={() => setAutoAddWhenSingle((currentValue) => !currentValue)}
          accessibilityLabel={autoAddWhenSingle ? t("autoAddOn") : t("autoAddOff")}
          style={[styles.utilityChip, autoAddWhenSingle ? styles.utilityChipActive : null]}
          textStyle={[styles.utilityChipText, autoAddWhenSingle ? styles.utilityChipTextActive : null]}
        >
          {t("common:labels.autoAddShort")}
        </Chip>
        {scannedProducts?.length ? (
          <Chip
            compact
            mode="outlined"
            icon="barcode-scan"
            onClose={handleClearSearchAndScan}
            style={styles.utilityChip}
            textStyle={styles.utilityChipText}
          >
            {t("common:labels.scanResults")}
          </Chip>
        ) : null}
        {hidScanner.mode === "textInput" ? (
          <IconButton
            icon="barcode-scan"
            mode="outlined"
            accessibilityLabel={t("resetScanFocus")}
            onPress={() => hidScanner.focusHiddenInput?.()}
            style={styles.scanFocusButton}
          />
        ) : null}
      </ScrollView>
      {scannedProducts?.length ? (
        <View style={styles.scanHintBanner}>
          <Text variant="bodySmall" style={styles.scanHintText}>
            {t("scanResultHint")}
          </Text>
        </View>
      ) : null}
    </View>
  );
  return (
    <SafeAreaView edges={["top", "left", "right"]} style={styles.container}>
      {fixedHeaderContent}
      <FlatList
        style={styles.content}
        data={displayProducts}
        keyExtractor={(item) => item.productCode}
        numColumns={3}
        columnWrapperStyle={styles.columnWrapper}
        contentContainerStyle={productListContentStyle}
        keyboardShouldPersistTaps="handled"
        ListEmptyComponent={
          <EmptyState
            title={
              productsQuery.isError
                ? t("empty.productsLoadFailedTitle")
                : selectedStoreCode
                  ? t("empty.noProductsTitle")
                  : t("empty.selectStoreTitle")
            }
            description={
              productsQuery.isError
                ? t("empty.productsLoadFailedDescription")
                : selectedStoreCode
                  ? t("empty.noProductsDescription")
                  : t("empty.selectStoreDescription")
            }
            actionLabel={productsQuery.isError ? t("common:actions.retry") : undefined}
            onAction={productsQuery.isError ? () => void productsQuery.refetch() : undefined}
          />
        }
        ListFooterComponent={
          displayProducts.length ? (
            <View style={styles.paginationRow}>
              <Button mode="outlined" disabled={pageNumber <= 1} onPress={() => setPageNumber((value) => value - 1)}>
                {t("pagination.previous")}</Button>
              <Text variant="bodyMedium">{t("pagination.page", { page: pageNumber })}</Text>
              <Button mode="outlined" disabled={!canGoNextPage} onPress={() => setPageNumber((value) => value + 1)}>
                {t("pagination.next")}</Button>
            </View>
          ) : null
        }
        renderItem={({ item }) => (
          <ProductCard
            product={item}
            dynamicDataMap={displayDynamicDataMap}
            disabled={!selectedStoreCode}
            isUpdatingCart={activeCartMutationProductCode === item.productCode}
            onAddToCart={handleAddToCart}
            onDecreaseCartQuantity={handleDecreaseCartQuantity}
            onIncreaseCartQuantity={handleIncreaseCartQuantity}
          />
        )}
      />

      {productsQuery.isLoading || storesLoading ? <LoadingOverlay /> : null}

      <Portal>
        <Modal
          visible={storePickerVisible}
          onDismiss={() => setStorePickerVisible(false)}
          contentContainerStyle={styles.filtersModal}
        >
          <ScrollView contentContainerStyle={styles.filtersModalContent}>
            <View style={styles.filtersModalHeader}>
              <View style={styles.filtersModalTitleWrap}>
                <Text variant="titleMedium">{t("filters.store")}</Text>
                <Text variant="bodySmall" style={styles.secondaryText}>
                  {t("filters.currentStore", { store: selectedStore?.storeName || t("common:na") })}
                </Text>
              </View>
              <Button mode="text" onPress={() => setStorePickerVisible(false)}>
                {t("common:actions.close")}
              </Button>
            </View>
            <View style={styles.filtersSection}>
              <View style={styles.filtersSectionHeader}>
                <Text variant="labelLarge">{t("filters.store")}</Text>
                <Button compact onPress={() => void refetchStores()}>
                  {t("common:actions.refresh")}
                </Button>
              </View>
              {storesLoading ? (
                <Text variant="bodyMedium">{t("common:loading")}</Text>
              ) : storesLoadFailed ? (
                <View style={styles.storeErrorWrap}>
                  <Text variant="bodyMedium">
                    {getErrorMessage(storesError, "messages.storesLoadFailed")}
                  </Text>
                  <Button mode="outlined" onPress={() => void refetchStores()}>
                    {t("common:actions.retry")}
                  </Button>
                </View>
              ) : stores.length ? (
                <ScrollView style={styles.storeListScroll} contentContainerStyle={styles.storeListContent}>
                  {stores.map((item) => (
                    <Button
                      key={item.storeCode}
                      mode={selectedStoreCode === item.storeCode ? "contained" : "outlined"}
                      style={styles.storeButton}
                      onPress={() => {
                        void selectStore(item);
                        setStorePickerVisible(false);
                      }}
                    >
                      {item.storeName}
                    </Button>
                  ))}
                </ScrollView>
              ) : (
                <Text variant="bodyMedium">{t("filters.noStores")}</Text>
              )}
            </View>
          </ScrollView>
        </Modal>
      </Portal>

      <Portal>
        <Modal
          visible={filtersVisible}
          onDismiss={() => setFiltersVisible(false)}
          contentContainerStyle={styles.filtersModal}
        >
          <ScrollView contentContainerStyle={styles.filtersModalContent}>
            <View style={styles.filtersModalHeader}>
              <View style={styles.filtersModalTitleWrap}>
                <Text variant="titleMedium">{t("filterTitle")}</Text>
                <Text variant="bodySmall" style={styles.secondaryText}>
                  {t("filters.currentCategory", { category: selectedCategoryName })}
                </Text>
              </View>
              <Button mode="text" onPress={() => setFiltersVisible(false)}>
                {t("common:actions.close")}
              </Button>
            </View>
            <View style={styles.filtersSection}>
              <View style={styles.filtersSectionHeader}>
                <Text variant="labelLarge">{t("filters.category")}</Text>
                <Button compact onPress={() => setSelectedCategoryGUID(undefined)}>
                  {t("filters.allCategories")}
                </Button>
              </View>
              <View style={styles.categoryTreeWrap}>
                <Button
                  compact
                  mode={!selectedCategoryGUID ? "contained-tonal" : "text"}
                  onPress={() => setSelectedCategoryGUID(undefined)}
                  contentStyle={styles.resetFilterButtonContent}
                >
                  {t("filters.all")}
                </Button>
                {renderCategoryTree(categoryOptions)}
              </View>
            </View>
          </ScrollView>
        </Modal>
      </Portal>

      <Portal>
        <Modal
          visible={gradeFilterVisible}
          onDismiss={() => setGradeFilterVisible(false)}
          contentContainerStyle={styles.filtersModal}
        >
          <ScrollView contentContainerStyle={styles.filtersModalContent}>
            <View style={styles.filtersModalHeader}>
              <View style={styles.filtersModalTitleWrap}>
                <Text variant="titleMedium">{t("gradeFilterTitle")}</Text>
                <Text variant="bodySmall" style={styles.secondaryText}>
                  {t("filters.currentGrade", { grade: selectedGradeLabel })}
                </Text>
              </View>
              <Button mode="text" onPress={() => setGradeFilterVisible(false)}>
                {t("common:actions.close")}
              </Button>
            </View>
            <View style={styles.filtersSection}>
              <View style={styles.filtersSectionHeader}>
                <Text variant="labelLarge">{t("filters.grade")}</Text>
                <Button compact onPress={() => setSelectedGrade(undefined)}>
                  {t("filters.allGrades")}
                </Button>
              </View>
              <View style={styles.gradeOptionsWrap}>
                <Chip
                  compact
                  mode={!selectedGrade ? "flat" : "outlined"}
                  selected={!selectedGrade}
                  onPress={() => {
                    setSelectedGrade(undefined);
                    setGradeFilterVisible(false);
                  }}
                  style={[styles.filterChip, !selectedGrade ? styles.filterChipActive : null]}
                  textStyle={[styles.filterChipText, !selectedGrade ? styles.filterChipTextActive : null]}
                >
                  {t("filters.all")}
                </Chip>
                {gradeOptions.map((option) => {
                  const grade = normalizeGradeValue(option.value);
                  const isSelected = selectedGrade === grade;

                  return (
                    <Chip
                      key={option.value}
                      compact
                      mode={isSelected ? "flat" : "outlined"}
                      selected={isSelected}
                      onPress={() => {
                        setSelectedGrade(isSelected ? undefined : grade);
                        setGradeFilterVisible(false);
                      }}
                      style={[styles.filterChip, isSelected ? styles.filterChipActive : null]}
                      textStyle={[styles.filterChipText, isSelected ? styles.filterChipTextActive : null]}
                    >
                      {t("common:grade", { grade: option.label })}
                    </Chip>
                  );
                })}
              </View>
              {productGradesQuery.isError ? (
                <Text variant="bodySmall" style={styles.secondaryText}>
                  {t("filters.gradesLoadFailed")}
                </Text>
              ) : null}
              {!gradeOptions.length ? (
                <Text variant="bodySmall" style={styles.secondaryText}>
                  {productGradesQuery.isLoading ? t("common:loading") : t("filters.noGrades")}
                </Text>
              ) : null}
            </View>
          </ScrollView>
        </Modal>
      </Portal>

      <Portal>
        <Modal
          visible={cameraVisible}
          onDismiss={() => {
            setCameraVisible(false);
          }}
          contentContainerStyle={styles.cameraModal}
        >
          <View style={styles.cameraModalHeader}>
            <View style={styles.cameraModalTitleWrap}>
              <Text variant="titleMedium">{t("camera.title")}</Text>
              <Text variant="bodySmall" style={styles.secondaryText}>
                {t("camera.currentStore", { store: selectedStore?.storeName || t("common:na") })}
              </Text>
            </View>
            <Button
              mode="text"
              onPress={() => {
                setCameraVisible(false);
              }}
            >
              {t("common:actions.close")}
            </Button>
          </View>

          {cameraScan.permission?.granted ? (
            <CameraView style={styles.cameraView} {...cameraScan.cameraProps} />
          ) : (
            <Card style={styles.permissionCard}>
              <Card.Content style={styles.permissionCardContent}>
                <Text variant="titleMedium">{t("camera.needPermissionTitle")}</Text>
                <Text variant="bodySmall" style={styles.secondaryText}>
                  {t("camera.needPermissionDescription")}
                </Text>
                <Button mode="contained" onPress={() => void cameraScan.requestPermission()}>
                  {t("camera.grantPermission")}
                </Button>
              </Card.Content>
            </Card>
          )}
        </Modal>
      </Portal>

      <ScanResultPicker
        visible={Boolean(scanResult.selectionState)}
        barcode={scanResult.selectionState?.barcode}
        items={scanResult.selectionState?.items ?? []}
        selectLabel={t("common:actions.select")}
        cancelLabel={t("common:actions.cancel")}
        title={t("productQuery:lookup.title")}
        tip={t("productQuery:lookup.query", { value: scanResult.selectionState?.barcode || t("common:na") })}
        onDismiss={() => {
          scanResult.clearSelection();
        }}
        onSelect={async (product) => {
          await scanResult.confirmSelection(product);
        }}
      />

      {hidScanner.mode === "textInput" && hidScanner.textInputProps ? (
        <TextInput style={styles.hiddenInput} {...hidScanner.textInputProps} />
      ) : null}

      <Snackbar visible={Boolean(snackbarMessage)} onDismiss={() => setSnackbarMessage("")} duration={2500}>
        {snackbarMessage}
      </Snackbar>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: "#F6FAFE",
  },
  header: {
    paddingHorizontal: 16,
    paddingTop: 2,
    paddingBottom: 8,
    gap: 10,
  },
  headerCompact: {
    gap: 8,
  },
  headerTopRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    gap: 12,
  },
  headerTopRowCompact: {
    gap: 8,
  },
  headerTitleWrap: {
    flex: 1,
    gap: 2,
  },
  headerTitle: {
    color: "#0F172A",
    fontWeight: "700",
    fontSize: 18,
  },
  headerSubtitle: {
    color: "#5B6474",
  },
  filterToggleButton: {
    margin: 0,
  },
  cartBox: {
    minWidth: 82,
    paddingHorizontal: 8,
    paddingVertical: 4,
    borderRadius: 8,
    backgroundColor: "#FFFFFF",
    borderWidth: 1,
    borderColor: "#DFE3E7",
    alignItems: "center",
    justifyContent: "center",
  },
  cartButton: {
    margin: 0,
  },
  badge: {
    position: "absolute",
    top: 3,
    right: 8,
  },
  cartText: {
    color: "#2C3134",
  },
  categoryCardContent: {
    gap: 10,
  },
  categoryCardContentCompact: {
    gap: 6,
    paddingVertical: 10,
  },
  categoryCardHeader: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    gap: 12,
  },
  categoryCardTitleWrap: {
    flex: 1,
    gap: 2,
  },
  categoryCardActions: {
    flexDirection: "row",
    alignItems: "center",
    gap: 2,
  },
  categoryTriggerButton: {
    flex: 1,
  },
  categoryTriggerButtonContent: {
    minHeight: 38,
    justifyContent: "flex-start",
  },
  categoryTriggerButtonLabel: {
    marginVertical: 0,
  },
  categoryMenuButton: {
    margin: 0,
  },
  categoryTreeWrap: {
    gap: 6,
  },
  categoryTreeNode: {
    gap: 6,
  },
  categoryTreeRow: {
    minHeight: 40,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    borderRadius: 10,
    backgroundColor: "#f7f7f7",
    paddingLeft: 4,
    paddingRight: 2,
  },
  categoryTreeRowSelected: {
    backgroundColor: "#eef4ff",
  },
  categoryTreeButton: {
    flex: 1,
    alignItems: "flex-start",
    justifyContent: "center",
  },
  categoryTreeButtonContent: {
    justifyContent: "flex-start",
    minHeight: 40,
  },
  categoryTreeButtonLabel: {
    marginVertical: 0,
  },
  categoryTreeToggle: {
    margin: 0,
  },
  categoryTreeChildren: {
    gap: 6,
  },
  searchRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: 8,
  },
  searchRowCompact: {
    gap: 6,
  },
  cameraQueryButton: {
    margin: 0,
  },
  autoAddToggleButton: {
    margin: 0,
  },
  scanFocusButton: {
    margin: 0,
  },
  searchToggleButton: {
    margin: 0,
  },
  searchClearFilterButton: {
    margin: 0,
  },
  searchInputWrap: {
    flex: 1,
    minWidth: 0,
  },
  searchInput: {
    width: "100%",
    backgroundColor: "#FFFFFF",
    borderWidth: 1,
    borderColor: "#DFE3E7",
    elevation: 0,
  },
  searchInputText: {
    minHeight: 0,
    fontSize: 14,
  },
  utilityRow: {
    gap: 8,
    paddingRight: 4,
  },
  utilityChip: {
    backgroundColor: "#FFFFFF",
    borderColor: "#DFE3E7",
  },
  utilityChipActive: {
    backgroundColor: "#E8F3EC",
    borderColor: "#9AF1C7",
  },
  utilityChipText: {
    color: "#45474C",
    fontSize: 12,
  },
  utilityChipTextActive: {
    color: "#0B704F",
    fontWeight: "700",
  },
  filterChip: {
    backgroundColor: "#FFFFFF",
    borderColor: "#C5C6CD",
  },
  filterChipActive: {
    backgroundColor: "#111C2E",
    borderColor: "#111C2E",
  },
  filterChipText: {
    fontSize: 12,
    color: "#45474C",
  },
  filterChipTextActive: {
    color: "#FFFFFF",
    fontWeight: "700",
  },
  gradeOptionsWrap: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
  },
  resetFilterButtonContent: {
    justifyContent: "flex-start",
  },
  scanHintBanner: {
    borderRadius: 8,
    backgroundColor: "#EEF4FF",
    paddingHorizontal: 12,
    paddingVertical: 6,
  },
  scanHintText: {
    color: "#1677FF",
    fontWeight: "600",
  },
  content: {
    flex: 1,
  },
  listContent: {
    paddingHorizontal: 6,
    paddingBottom: 10,
    paddingTop: 4,
    flexGrow: 1,
  },
  listContentCompact: {
    paddingBottom: 0,
  },
  columnWrapper: {
    justifyContent: "space-between",
    gap: 0,
  },
  paginationRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    marginHorizontal: 8,
    marginTop: 12,
    paddingHorizontal: 12,
    paddingVertical: 10,
    borderRadius: 10,
    backgroundColor: "#FFFFFF",
  },
  filtersModal: {
    margin: 16,
    borderRadius: 16,
    backgroundColor: "#fff",
    padding: 16,
  },
  filtersModalContent: {
    gap: 16,
  },
  filtersModalHeader: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    gap: 12,
  },
  filtersModalTitleWrap: {
    flex: 1,
    gap: 4,
  },
  filtersSection: {
    gap: 10,
  },
  filtersSectionHeader: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    gap: 12,
  },
  cameraModal: {
    margin: 16,
    borderRadius: 16,
    backgroundColor: "#fff",
    padding: 16,
    gap: 12,
  },
  cameraModalHeader: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    gap: 12,
  },
  cameraModalTitleWrap: {
    flex: 1,
    gap: 4,
  },
  cameraView: {
    width: "100%",
    height: 420,
    borderRadius: 12,
    overflow: "hidden",
  },
  permissionCard: {
    marginTop: 4,
  },
  permissionCardContent: {
    gap: 12,
  },
  storeButton: {
    marginBottom: 8,
  },
  storeListScroll: {
    maxHeight: 280,
  },
  storeListContent: {
    paddingBottom: 4,
  },
  storeErrorWrap: {
    gap: 12,
  },
  hiddenInput: {
    position: "absolute",
    width: 1,
    height: 1,
    opacity: 0,
  },
  secondaryText: {
    color: "#666",
  },
});
