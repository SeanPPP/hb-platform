import { useCallback, useEffect, useMemo, useState } from "react";
import { FlatList, ScrollView, StyleSheet, useWindowDimensions, View } from "react-native";
import { useQuery } from "@tanstack/react-query";
import { CameraView } from "expo-camera";
import { useRouter } from "expo-router";
import {
  Badge,
  Button,
  Card,
  Chip,
  Dialog,
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
import { StoreChip } from "@/components/ui/StoreChip";
import { getCategoryTree } from "@/modules/shop/api";
import { resolveMinimumOrderQuantity, useAddToCart } from "@/modules/shop/use-add-to-cart";
import { useCartSummary } from "@/modules/shop/use-cart-summary";
import { useProducts } from "@/modules/shop/use-products";
import { useStores } from "@/modules/shop/use-stores";
import { useUpdateCartQuantity } from "@/modules/shop/use-update-cart-quantity";
import { useCameraScan } from "@/modules/scanner/use-camera-scan";
import { useHidBarcodeScanner } from "@/modules/scanner/use-hid-barcode-scanner";
import { useScanResult } from "@/modules/scanner/use-scan-result";
import { ScanResultPicker } from "@/components/ui/ScanResultPicker";
import type { StoreOrderCategoryNode, StoreOrderProductItem } from "@/modules/shop/types";
import { useCartStore } from "@/store/cart-store";

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

export default function Home() {
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
  const [storeDialogVisible, setStoreDialogVisible] = useState(false);
  const [cameraVisible, setCameraVisible] = useState(false);
  const [searchInput, setSearchInput] = useState("");
  const [autoAddWhenSingle, setAutoAddWhenSingle] = useState(true);
  const [isSearchExpanded, setIsSearchExpanded] = useState(false);
  const [keyword, setKeyword] = useState("");
  const [scannedProductCodes, setScannedProductCodes] = useState<string[] | null>(null);
  const [isCategoryPanelExpanded, setIsCategoryPanelExpanded] = useState(false);
  const [selectedCategoryGUID, setSelectedCategoryGUID] = useState<string | undefined>();
  const [expandedCategoryGUIDs, setExpandedCategoryGUIDs] = useState<string[]>([]);
  const [pageNumber, setPageNumber] = useState(1);
  const [snackbarMessage, setSnackbarMessage] = useState("");
  const [activeCartMutationProductCode, setActiveCartMutationProductCode] = useState<string | null>(null);
  const addToCart = useAddToCart(selectedStoreCode);
  const updateCartQuantity = useUpdateCartQuantity(selectedStoreCode);

  useCartSummary(selectedStoreCode);
  const handleScanLookupProduct = useCallback((product: StoreOrderProductItem, barcode: string) => {
    const nextKeyword = barcode;
    setIsSearchExpanded(true);
    setSearchInput(nextKeyword);
    setKeyword(nextKeyword);
    setScannedProductCodes([product.productCode]);
    setSelectedCategoryGUID(undefined);
  }, []);
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
  useHidBarcodeScanner({
    onScan: async (barcode) => {
      await scanResult.handleBarcode(barcode, "hid");
    },
  });

  const categoriesQuery = useQuery({
    queryKey: ["shopCategories"],
    queryFn: getCategoryTree,
  });

  const categoryOptions = useMemo(
    () => resolveDisplayCategories(categoriesQuery.data ?? []),
    [categoriesQuery.data]
  );
  const selectedCategoryName = useMemo(
    () => findCategoryName(categoryOptions, selectedCategoryGUID) ?? "全部",
    [categoryOptions, selectedCategoryGUID]
  );

  const productsQuery = useProducts({
    storeCode: selectedStoreCode ?? undefined,
    itemNumber: keyword || undefined,
    categoryGUID: selectedCategoryGUID,
    pageNumber,
    pageSize: 20,
    sortBy: "Default",
  });

  useEffect(() => {
    setPageNumber(1);
  }, [keyword, selectedCategoryGUID, selectedStoreCode]);

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

    setSnackbarMessage(
      storesError instanceof Error ? storesError.message : "门店加载失败，请稍后重试"
    );
  }, [storesError, storesLoadFailed]);

  const canGoNextPage = useMemo(() => {
    const total = productsQuery.data?.total ?? 0;
    return pageNumber * 20 < total;
  }, [pageNumber, productsQuery.data?.total]);
  const isCompactLayout = windowHeight < 780;
  const productListContentStyle = useMemo(
    () => [styles.listContent, isCompactLayout ? styles.listContentCompact : null],
    [isCompactLayout]
  );
  const displayProducts = useMemo(() => {
    const items = productsQuery.data?.items ?? [];
    if (!scannedProductCodes?.length) {
      return items;
    }

    return items.filter((item) => scannedProductCodes.includes(item.productCode));
  }, [productsQuery.data?.items, scannedProductCodes]);

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
      setSnackbarMessage(`${product.productName || product.productCode} 已加入购物车`);
    } catch (error) {
      setSnackbarMessage(error instanceof Error ? error.message : "加入购物车失败");
    } finally {
      setActiveCartMutationProductCode(null);
    }
  }

  function getCurrentCartQuantity(productCode: string) {
    return productsQuery.dynamicDataMap[productCode]?.cartQuantity ?? 0;
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
      setSnackbarMessage(error instanceof Error ? error.message : "更新购物车数量失败");
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
      setSnackbarMessage(error instanceof Error ? error.message : "更新购物车数量失败");
    } finally {
      setActiveCartMutationProductCode(null);
    }
  }

  const fixedHeaderContent = (
    <View style={[styles.header, isCompactLayout ? styles.headerCompact : null]}>
      <View style={[styles.headerTopRow, isCompactLayout ? styles.headerTopRowCompact : null]}>
        <View style={styles.headerSelectorsRow}>
          <View style={styles.headerSelectorItem}>
            <StoreChip compact store={selectedStore} onPress={() => setStoreDialogVisible(true)} />
          </View>
          <Button
            compact
            mode="outlined"
            icon={isCategoryPanelExpanded ? "menu-open" : "menu"}
            onPress={() => setIsCategoryPanelExpanded((currentValue) => !currentValue)}
            style={styles.categoryTriggerButton}
            contentStyle={styles.categoryTriggerButtonContent}
            labelStyle={styles.categoryTriggerButtonLabel}
          >
            分类: {selectedCategoryName}
          </Button>
        </View>

        <View style={styles.cartBox}>
          <IconButton icon="cart-outline" onPress={() => router.push("/(tabs)/cart")} />
          {cartSummary?.totalQuantity ? <Badge style={styles.badge}>{cartSummary.totalQuantity}</Badge> : null}
          <Text variant="bodySmall">总数 {cartSummary?.totalQuantity ?? 0}</Text>
        </View>
      </View>

      {isCategoryPanelExpanded ? (
        <Card mode="outlined">
          <Card.Content style={[styles.categoryCardContent, isCompactLayout ? styles.categoryCardContentCompact : null]}>
            <View style={styles.categoryCardHeader}>
              <View style={styles.categoryCardTitleWrap}>
                <Text variant="titleSmall">商品分类</Text>
                <Text variant="bodySmall" style={styles.secondaryText}>
                  当前选择: {selectedCategoryName}
                </Text>
              </View>
              <Button compact mode={!selectedCategoryGUID ? "contained-tonal" : "text"} onPress={() => setSelectedCategoryGUID(undefined)}>
                全部
              </Button>
            </View>

            <View style={styles.categoryTreeWrap}>{renderCategoryTree(categoryOptions)}</View>
          </Card.Content>
        </Card>
      ) : null}

      <View style={[styles.searchRow, isCompactLayout ? styles.searchRowCompact : null]}>
        <IconButton
          icon="camera-outline"
          mode="contained-tonal"
          accessibilityLabel="相机查询"
          onPress={() => setCameraVisible(true)}
          style={styles.cameraQueryButton}
        />
        <IconButton
          icon={autoAddWhenSingle ? "cart-check" : "cart-off"}
          mode={autoAddWhenSingle ? "contained-tonal" : "outlined"}
          accessibilityLabel={autoAddWhenSingle ? "自动加入购物车已开启" : "自动加入购物车已关闭"}
          onPress={() => setAutoAddWhenSingle((currentValue) => !currentValue)}
          style={styles.autoAddToggleButton}
        />
        <IconButton
          icon={isSearchExpanded ? "close" : "magnify"}
          mode="outlined"
          accessibilityLabel={isSearchExpanded ? "收起搜索框" : "展开搜索框"}
          onPress={() => {
            if (isSearchExpanded) {
              setIsSearchExpanded(false);
            } else {
              setIsSearchExpanded(true);
            }
          }}
          style={styles.searchToggleButton}
        />
        <IconButton
          icon="filter-remove-outline"
          mode="outlined"
          accessibilityLabel="清除扫码过滤"
          onPress={() => {
            setScannedProductCodes(null);
            setSearchInput("");
            setKeyword("");
          }}
          style={styles.searchClearFilterButton}
        />
        {isSearchExpanded ? (
          <View style={styles.searchInputWrap}>
            <Searchbar
              placeholder="按货号搜索"
              value={searchInput}
              onChangeText={(value) => {
                setSearchInput(value);
                setScannedProductCodes(null);
              }}
              onSubmitEditing={() => {
                setScannedProductCodes(null);
                setKeyword(searchInput.trim());
              }}
              onIconPress={() => {
                setScannedProductCodes(null);
                setKeyword(searchInput.trim());
              }}
              style={styles.searchInput}
            />
          </View>
        ) : null}
      </View>
    </View>
  );

  return (
    <SafeAreaView edges={["left", "right", "bottom"]} style={styles.container}>
      {fixedHeaderContent}
      <FlatList
        style={styles.content}
        data={displayProducts}
        keyExtractor={(item) => item.productCode}
        numColumns={2}
        columnWrapperStyle={styles.columnWrapper}
        contentContainerStyle={productListContentStyle}
        keyboardShouldPersistTaps="handled"
        ListEmptyComponent={
          <EmptyState
            title={selectedStoreCode ? "暂无商品" : "先选择门店"}
            description={selectedStoreCode ? "可以换个关键词、重新扫码或切换分类试试" : "选中门店后可查看商品动态"}
          />
        }
        ListFooterComponent={
          displayProducts.length ? (
            <View style={styles.paginationRow}>
              <Button mode="outlined" disabled={pageNumber <= 1} onPress={() => setPageNumber((value) => value - 1)}>
                上一页
              </Button>
              <Text variant="bodyMedium">第 {pageNumber} 页</Text>
              <Button mode="outlined" disabled={!canGoNextPage} onPress={() => setPageNumber((value) => value + 1)}>
                下一页
              </Button>
            </View>
          ) : null
        }
        renderItem={({ item }) => (
          <ProductCard
            product={item}
            dynamicDataMap={productsQuery.dynamicDataMap}
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
        <Dialog visible={storeDialogVisible} onDismiss={() => setStoreDialogVisible(false)}>
          <Dialog.Title>选择门店</Dialog.Title>
          <Dialog.Content>
            {storesLoading ? (
              <Text variant="bodyMedium">门店加载中...</Text>
            ) : storesLoadFailed ? (
              <View style={styles.storeErrorWrap}>
                <Text variant="bodyMedium">
                  {storesError instanceof Error ? storesError.message : "门店加载失败"}
                </Text>
                <Button mode="outlined" onPress={() => void refetchStores()}>
                  重试加载
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
                      setStoreDialogVisible(false);
                    }}
                  >
                    {item.storeName}
                  </Button>
                ))}
              </ScrollView>
            ) : (
              <Text variant="bodyMedium">当前账号没有可用门店。</Text>
            )}
          </Dialog.Content>
          <Dialog.Actions>
            <Button
              onPress={() => {
                void selectStore(null);
                setStoreDialogVisible(false);
              }}
            >
              清空
            </Button>
            <Button onPress={() => setStoreDialogVisible(false)}>关闭</Button>
          </Dialog.Actions>
        </Dialog>
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
              <Text variant="titleMedium">相机扫码查询</Text>
              <Text variant="bodySmall" style={styles.secondaryText}>
                当前门店: {selectedStore?.storeName || "未选择门店"}
              </Text>
            </View>
            <Button
              mode="text"
              onPress={() => {
                setCameraVisible(false);
              }}
            >
              关闭
            </Button>
          </View>

          {cameraScan.permission?.granted ? (
            <CameraView style={styles.cameraView} {...cameraScan.cameraProps} />
          ) : (
            <Card style={styles.permissionCard}>
              <Card.Content style={styles.permissionCardContent}>
                <Text variant="titleMedium">需要相机权限</Text>
                <Text variant="bodySmall" style={styles.secondaryText}>
                  开启权限后，就能在商品页里直接用摄像头扫码查询商品。
                </Text>
                <Button mode="contained" onPress={() => void cameraScan.requestPermission()}>
                  开启相机权限
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
        selectLabel="选择"
        title="选择商品"
        tip={`条码 ${scanResult.selectionState?.barcode || "--"} 匹配到了多个商品，请选择一个继续。`}
        onDismiss={() => {
          scanResult.clearSelection();
        }}
        onSelect={async (product) => {
          await scanResult.confirmSelection(product);
        }}
      />

      <Snackbar visible={Boolean(snackbarMessage)} onDismiss={() => setSnackbarMessage("")} duration={2500}>
        {snackbarMessage}
      </Snackbar>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: "#fff",
  },
  header: {
    paddingHorizontal: 16,
    paddingTop: 4,
    gap: 12,
  },
  headerCompact: {
    gap: 8,
    paddingTop: 2,
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
  headerSelectorsRow: {
    flex: 1,
    flexDirection: "row",
    alignItems: "center",
    gap: 8,
  },
  headerSelectorItem: {
    flex: 1,
  },
  cartBox: {
    alignItems: "center",
    justifyContent: "center",
  },
  badge: {
    position: "absolute",
    top: 6,
    right: 6,
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
  },
  content: {
    flex: 1,
  },
  listContent: {
    paddingHorizontal: 10,
    paddingBottom: 16,
    paddingTop: 8,
    flexGrow: 1,
  },
  listContentCompact: {
    paddingBottom: 0,
  },
  columnWrapper: {
    justifyContent: "space-between",
  },
  paginationRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    paddingHorizontal: 8,
    paddingTop: 12,
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
  secondaryText: {
    color: "#666",
  },
});
