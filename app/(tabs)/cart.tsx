import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Alert, Animated, FlatList, Image, PanResponder, Pressable, StyleSheet, View } from "react-native";
import {
  Button,
  Card,
  Chip,
  IconButton,
  Searchbar,
  Snackbar,
  Text,
} from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import { EmptyState } from "@/components/ui/EmptyState";
import { LoadingOverlay } from "@/components/ui/LoadingOverlay";
import { ScanResultPicker } from "@/components/ui/ScanResultPicker";
import { StoreChip } from "@/components/ui/StoreChip";
import { useClearCart } from "@/modules/shop/use-clear-cart";
import { useCartPage } from "@/modules/shop/use-cart-page";
import { useRemoveCartLine } from "@/modules/shop/use-remove-cart-line";
import { useStores } from "@/modules/shop/use-stores";
import { useUpdateCartQuantity } from "@/modules/shop/use-update-cart-quantity";
import { useHidBarcodeScanner } from "@/modules/scanner/use-hid-barcode-scanner";
import { useScanResult } from "@/modules/scanner/use-scan-result";
import type { StoreOrderCartItem } from "@/modules/shop/types";

const PAGE_SIZE_OPTIONS = [10, 20, 50, 100];
const SWIPE_ACTION_WIDTH = 92;

function clamp(value: number, min: number, max: number) {
  return Math.min(Math.max(value, min), max);
}

interface CartListItemCardProps {
  clearCartPending: boolean;
  index: number;
  isDeleting: boolean;
  isUpdating: boolean;
  item: StoreOrderCartItem;
  openSwipeDetailGUID: string | null;
  onDelete: (item: StoreOrderCartItem) => Promise<void>;
  onOpenSwipe: (detailGUID: string | null) => void;
  onUpdateQuantity: (item: StoreOrderCartItem, nextQuantity: number) => Promise<void>;
}

function CartListItemCard({
  clearCartPending,
  index,
  isDeleting,
  isUpdating,
  item,
  openSwipeDetailGUID,
  onDelete,
  onOpenSwipe,
  onUpdateQuantity,
}: CartListItemCardProps) {
  const translateX = useRef(new Animated.Value(0)).current;
  const offsetRef = useRef(0);
  const step = item.minOrderQuantity > 0 ? item.minOrderQuantity : 1;
  const isBusy = isUpdating || isDeleting || clearCartPending;

  const animateTo = useCallback(
    (toValue: number) => {
      Animated.spring(translateX, {
        toValue,
        useNativeDriver: true,
        bounciness: 0,
      }).start(() => {
        offsetRef.current = toValue;
      });
    },
    [translateX]
  );

  const openSwipe = useCallback(() => {
    onOpenSwipe(item.detailGUID);
    animateTo(-SWIPE_ACTION_WIDTH);
  }, [animateTo, item.detailGUID, onOpenSwipe]);

  const closeSwipe = useCallback(
    (shouldResetOpenKey: boolean) => {
      if (shouldResetOpenKey && openSwipeDetailGUID === item.detailGUID) {
        onOpenSwipe(null);
      }
      animateTo(0);
    },
    [animateTo, item.detailGUID, onOpenSwipe, openSwipeDetailGUID]
  );

  useEffect(() => {
    if (openSwipeDetailGUID !== item.detailGUID && offsetRef.current !== 0) {
      animateTo(0);
    }
  }, [animateTo, item.detailGUID, openSwipeDetailGUID]);

  const panResponder = useMemo(
    () =>
      PanResponder.create({
        onMoveShouldSetPanResponder: (_, gestureState) =>
          Math.abs(gestureState.dx) > Math.abs(gestureState.dy) && Math.abs(gestureState.dx) > 8,
        onPanResponderGrant: () => {
          if (openSwipeDetailGUID && openSwipeDetailGUID !== item.detailGUID) {
            onOpenSwipe(null);
          }
        },
        onPanResponderMove: (_, gestureState) => {
          const nextValue = clamp(offsetRef.current + gestureState.dx, -SWIPE_ACTION_WIDTH, 0);
          translateX.setValue(nextValue);
        },
        onPanResponderRelease: (_, gestureState) => {
          const nextValue = clamp(offsetRef.current + gestureState.dx, -SWIPE_ACTION_WIDTH, 0);
          if (nextValue <= -SWIPE_ACTION_WIDTH / 2) {
            openSwipe();
            return;
          }

          closeSwipe(true);
        },
        onPanResponderTerminate: () => {
          if (offsetRef.current <= -SWIPE_ACTION_WIDTH / 2) {
            openSwipe();
            return;
          }

          closeSwipe(true);
        },
      }),
    [closeSwipe, item.detailGUID, onOpenSwipe, openSwipe, openSwipeDetailGUID, translateX]
  );

  return (
    <View style={styles.swipeRow}>
      <View style={styles.deleteActionWrap}>
        <Pressable
          disabled={isBusy}
          onPress={() => {
            closeSwipe(true);
            void onDelete(item);
          }}
          style={({ pressed }) => [
            styles.deleteAction,
            pressed && !isBusy ? styles.deleteActionPressed : null,
            isBusy ? styles.deleteActionDisabled : null,
          ]}
        >
          <Text variant="labelLarge" style={styles.deleteActionText}>
            {isDeleting ? "删除中" : "删除"}
          </Text>
        </Pressable>
      </View>

      <Animated.View
        style={[
          styles.swipeCardWrap,
          {
            transform: [{ translateX }],
          },
        ]}
        {...panResponder.panHandlers}
      >
        <Card mode="outlined" style={styles.itemCard}>
          <Card.Content style={styles.itemContent}>
            <View style={styles.itemMainRow}>
              {item.productImage ? (
                <Image source={{ uri: item.productImage }} style={styles.itemImage} />
              ) : (
                <View style={styles.itemImagePlaceholder} />
              )}

              <View style={styles.itemBody}>
                <View style={styles.itemHeader}>
                  <View style={styles.itemTitleWrap}>
                    <View style={styles.itemTopRow}>
                      <View style={styles.rowNumberBadge}>
                        <Text variant="labelSmall" style={styles.rowNumberText}>
                          {index + 1}
                        </Text>
                      </View>
                      <Text variant="titleSmall" numberOfLines={2} style={styles.itemTitle}>
                        {item.productName || item.productCode}
                      </Text>
                    </View>
                    <Text variant="bodySmall" style={styles.itemNumberText}>
                      {item.itemNumber || "--"}
                    </Text>
                  </View>
                  <View style={styles.quantityStepper}>
                    <IconButton
                      icon="minus"
                      mode="contained-tonal"
                      size={18}
                      disabled={isBusy}
                      loading={isUpdating}
                      onPress={() => void onUpdateQuantity(item, Math.max(0, item.quantity - step))}
                      style={styles.quantityButton}
                    />
                    <View style={styles.quantityValueWrap}>
                      <Text variant="titleMedium" style={styles.quantityValue}>
                        {item.quantity}
                      </Text>
                    </View>
                    <IconButton
                      icon="plus"
                      mode="contained"
                      size={18}
                      disabled={isBusy}
                      loading={isUpdating}
                      onPress={() => void onUpdateQuantity(item, item.quantity + step)}
                      style={styles.quantityButton}
                    />
                  </View>
                </View>

                <View style={styles.itemMetaRow}>
                  <Text variant="bodySmall" style={styles.secondaryText}>
                    ${Number(item.price ?? 0).toFixed(2)}
                  </Text>
                  <Text variant="bodySmall" style={styles.amountText}>
                    小计 ${Number(item.amount ?? 0).toFixed(2)}
                  </Text>
                  <Text variant="bodySmall" style={styles.secondaryText}>
                    进口 ${Number(item.importAmount ?? 0).toFixed(2)}
                  </Text>
                </View>
              </View>
            </View>
          </Card.Content>
        </Card>
      </Animated.View>
    </View>
  );
}

export default function Cart() {
  const { selectedStore, selectedStoreCode } = useStores();
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [keyword, setKeyword] = useState("");
  const [searchInput, setSearchInput] = useState("");
  const [snackbarMessage, setSnackbarMessage] = useState("");
  const [activeCartItemCode, setActiveCartItemCode] = useState<string | null>(null);
  const [activeDeleteDetailGUID, setActiveDeleteDetailGUID] = useState<string | null>(null);
  const [openSwipeDetailGUID, setOpenSwipeDetailGUID] = useState<string | null>(null);
  const [priorityProductCode, setPriorityProductCode] = useState<string | null>(null);
  const updateCartQuantity = useUpdateCartQuantity(selectedStoreCode);
  const removeCartLine = useRemoveCartLine(selectedStoreCode);
  const clearCart = useClearCart(selectedStoreCode);

  const cartQuery = useCartPage({
    page,
    pageSize,
    priorityProductCode,
    storeCode: selectedStoreCode,
  });

  const filteredItems = useMemo(() => {
    const normalizedKeyword = keyword.trim().toLowerCase();
    if (!normalizedKeyword) {
      return cartQuery.items;
    }

    return cartQuery.items.filter((item) =>
      [item.productName, item.productCode, item.itemNumber, item.barcode]
        .filter(Boolean)
        .some((value) => value!.toLowerCase().includes(normalizedKeyword))
    );
  }, [cartQuery.items, keyword]);

  const canGoNextPage = page * pageSize < cartQuery.total;

  const scanResult = useScanResult({
    onAddedToCart: async (product) => {
      setPriorityProductCode(product.productCode);
    },
    storeCode: selectedStoreCode,
  });

  useHidBarcodeScanner({
    onScan: async (barcode) => {
      await scanResult.handleBarcode(barcode, "hid");
    },
  });

  useEffect(() => {
    setPage(1);
  }, [pageSize, selectedStoreCode]);

  useEffect(() => {
    setPriorityProductCode(null);
    setOpenSwipeDetailGUID(null);
  }, [selectedStoreCode]);

  useEffect(() => {
    const maxPage = Math.max(1, Math.ceil(cartQuery.total / pageSize));
    if (page > maxPage) {
      setPage(maxPage);
    }
  }, [cartQuery.total, page, pageSize]);

  useEffect(() => {
    if (scanResult.feedback.status === "ready" || scanResult.feedback.status === "scanning") {
      return;
    }

    setSnackbarMessage(scanResult.feedback.message);
  }, [scanResult.feedback.message, scanResult.feedback.status]);

  async function handleUpdateQuantity(item: StoreOrderCartItem, nextQuantity: number) {
    setActiveCartItemCode(item.productCode);

    try {
      await updateCartQuantity.mutateAsync({
        nextQuantity,
        product: item,
      });
    } catch (error) {
      setSnackbarMessage(error instanceof Error ? error.message : "更新购物车数量失败");
    } finally {
      setActiveCartItemCode(null);
    }
  }

  async function handleDeleteCartItem(item: StoreOrderCartItem) {
    setActiveDeleteDetailGUID(item.detailGUID);

    try {
      await removeCartLine.mutateAsync({
        detailGUID: item.detailGUID,
      });
      if (openSwipeDetailGUID === item.detailGUID) {
        setOpenSwipeDetailGUID(null);
      }
      if (priorityProductCode === item.productCode) {
        setPriorityProductCode(null);
      }
    } catch (error) {
      setSnackbarMessage(error instanceof Error ? error.message : "删除购物车商品失败");
    } finally {
      setActiveDeleteDetailGUID(null);
    }
  }

  async function handleClearCart() {
    try {
      await clearCart.mutateAsync();
      setOpenSwipeDetailGUID(null);
      setPriorityProductCode(null);
      setPage(1);
      setSnackbarMessage("购物车已清空");
    } catch (error) {
      setSnackbarMessage(error instanceof Error ? error.message : "清空购物车失败");
    }
  }

  function confirmClearCart() {
    Alert.alert("清空购物车", "确定要清空当前门店购物车吗？", [
      { text: "取消", style: "cancel" },
      {
        text: "清空",
        style: "destructive",
        onPress: () => {
          void handleClearCart();
        },
      },
    ]);
  }

  function renderCartItem({ item, index }: { item: StoreOrderCartItem; index: number }) {
    return (
      <CartListItemCard
        clearCartPending={clearCart.isPending}
        index={index}
        isDeleting={activeDeleteDetailGUID === item.detailGUID}
        isUpdating={activeCartItemCode === item.productCode}
        item={item}
        onDelete={handleDeleteCartItem}
        onOpenSwipe={setOpenSwipeDetailGUID}
        onUpdateQuantity={handleUpdateQuantity}
        openSwipeDetailGUID={openSwipeDetailGUID}
      />
    );
  }

  return (
    <SafeAreaView edges={["left", "right", "bottom"]} style={styles.container}>
      <View style={styles.header}>
        <View style={styles.headerRow}>
          <View style={styles.storeWrap}>
            <StoreChip compact store={selectedStore} onPress={() => setSnackbarMessage("请在商品页切换门店")} />
          </View>
          <Button
            compact
            mode="outlined"
            icon="delete-sweep-outline"
            disabled={!selectedStoreCode || !cartQuery.total || clearCart.isPending}
            loading={clearCart.isPending}
            onPress={confirmClearCart}
          >
            清空购物车
          </Button>
        </View>

        <Searchbar
          placeholder="搜索购物车商品"
          value={searchInput}
          onChangeText={setSearchInput}
          onSubmitEditing={() => setKeyword(searchInput)}
          onIconPress={() => setKeyword(searchInput)}
        />

        <View style={styles.summaryGrid}>
          <Text variant="bodySmall" style={styles.summaryText}>商品项: {cartQuery.total}</Text>
          <Text variant="bodySmall" style={styles.summaryText}>总数: {cartQuery.cart?.totalQuantity ?? 0}</Text>
          <Text variant="bodySmall" style={styles.summaryText}>销售合计: ${Number(cartQuery.cart?.totalAmount ?? 0).toFixed(2)}</Text>
          <Text variant="bodySmall" style={styles.summaryText}>进口合计: ${Number(cartQuery.cart?.totalImportAmount ?? 0).toFixed(2)}</Text>
          <Text variant="bodySmall" style={styles.summaryText}>体积合计: {Number(cartQuery.cart?.totalVolume ?? 0).toFixed(2)}</Text>
        </View>

        <View style={styles.pageSizeRow}>
          {PAGE_SIZE_OPTIONS.map((option) => (
            <Chip
              compact
              key={option}
              selected={pageSize === option}
              onPress={() => setPageSize(option)}
            >
              {option}
            </Chip>
          ))}
        </View>
      </View>

      <FlatList
        data={filteredItems}
        keyExtractor={(item) => item.detailGUID || item.productCode}
        renderItem={renderCartItem}
        contentContainerStyle={styles.listContent}
        keyboardShouldPersistTaps="handled"
        ListEmptyComponent={
          <EmptyState
            title={selectedStoreCode ? "购物车为空" : "先选择门店"}
            description={selectedStoreCode ? "可以在此页扫码加购商品" : "请先在商品页选择门店"}
          />
        }
        ListFooterComponent={
          cartQuery.total ? (
            <View style={styles.paginationRow}>
              <Button mode="outlined" disabled={page <= 1} onPress={() => setPage((value) => value - 1)}>
                上一页
              </Button>
              <Text variant="bodyMedium">第 {page} 页</Text>
              <Button mode="outlined" disabled={!canGoNextPage} onPress={() => setPage((value) => value + 1)}>
                下一页
              </Button>
            </View>
          ) : null
        }
      />

      {cartQuery.isLoading ? <LoadingOverlay /> : null}

      <ScanResultPicker
        visible={Boolean(scanResult.selectionState)}
        barcode={scanResult.selectionState?.barcode}
        items={scanResult.selectionState?.items ?? []}
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
    gap: 10,
    paddingHorizontal: 16,
    paddingTop: 8,
  },
  headerRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: 10,
  },
  storeWrap: {
    flex: 1,
  },
  summaryGrid: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
  },
  summaryText: {
    color: "#333",
  },
  pageSizeRow: {
    flexDirection: "row",
    gap: 8,
  },
  listContent: {
    flexGrow: 1,
    padding: 10,
    paddingBottom: 16,
  },
  swipeRow: {
    marginBottom: 10,
    position: "relative",
  },
  deleteActionWrap: {
    ...StyleSheet.absoluteFillObject,
    alignItems: "flex-end",
    justifyContent: "center",
  },
  swipeCardWrap: {
    zIndex: 1,
  },
  itemCard: {
    marginBottom: 0,
  },
  itemContent: {
    gap: 10,
  },
  itemMainRow: {
    flexDirection: "row",
    gap: 12,
  },
  itemImage: {
    width: 76,
    height: 76,
    borderRadius: 10,
    backgroundColor: "#f5f5f5",
  },
  itemImagePlaceholder: {
    width: 76,
    height: 76,
    borderRadius: 10,
    backgroundColor: "#f0f0f0",
  },
  itemBody: {
    flex: 1,
    gap: 8,
  },
  itemHeader: {
    flexDirection: "row",
    alignItems: "flex-start",
    justifyContent: "space-between",
    gap: 8,
  },
  itemTitleWrap: {
    flex: 1,
    gap: 4,
  },
  itemTopRow: {
    flexDirection: "row",
    alignItems: "flex-start",
    gap: 8,
  },
  rowNumberBadge: {
    minWidth: 28,
    height: 28,
    borderRadius: 14,
    backgroundColor: "#E8F3FF",
    alignItems: "center",
    justifyContent: "center",
  },
  rowNumberText: {
    color: "#1677FF",
    fontWeight: "700",
  },
  itemTitle: {
    flex: 1,
  },
  itemNumberText: {
    color: "#1F1F1F",
    fontWeight: "600",
  },
  quantityStepper: {
    flexDirection: "row",
    alignItems: "center",
    gap: 2,
  },
  quantityButton: {
    margin: 0,
  },
  quantityValueWrap: {
    minWidth: 48,
    alignItems: "center",
    justifyContent: "center",
    gap: 2,
  },
  quantityValue: {
    color: "#1677FF",
    fontWeight: "700",
  },
  itemMetaRow: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
  },
  amountText: {
    color: "#1677FF",
    fontWeight: "700",
  },
  deleteAction: {
    width: 92,
    marginBottom: 10,
    borderRadius: 12,
    backgroundColor: "#FF4D4F",
    alignItems: "center",
    justifyContent: "center",
  },
  deleteActionPressed: {
    opacity: 0.88,
  },
  deleteActionDisabled: {
    backgroundColor: "#FFB3B3",
  },
  deleteActionText: {
    color: "#fff",
    fontWeight: "700",
  },
  paginationRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    paddingTop: 12,
  },
  secondaryText: {
    color: "#666",
  },
});
