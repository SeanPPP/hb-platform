import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  Alert,
  Animated,
  FlatList,
  Image,
  PanResponder,
  Pressable,
  StyleSheet,
  TextInput,
  View,
} from "react-native";
import {
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
import { useFocusEffect } from "@react-navigation/native";
import { useRouter } from "expo-router";
import { useQueryClient } from "@tanstack/react-query";
import { EmptyState } from "@/components/ui/EmptyState";
import { LoadingOverlay } from "@/components/ui/LoadingOverlay";
import { ScanResultPicker } from "@/components/ui/ScanResultPicker";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { submitStoreOrder } from "@/modules/orders/store-order-api";
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
const PRODUCT_GRADE_CONFIG: Record<string, { color: string }> = {
  A: { color: "#722ED1" },
  B: { color: "#1890FF" },
  C: { color: "#FA8C16" },
  D: { color: "#F5222D" },
};

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
  t: (key: string, options?: Record<string, unknown>) => string;
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
  t,
  onDelete,
  onOpenSwipe,
  onUpdateQuantity,
}: CartListItemCardProps) {
  const translateX = useRef(new Animated.Value(0)).current;
  const offsetRef = useRef(0);
  const step = item.minOrderQuantity > 0 ? item.minOrderQuantity : 1;
  const isBusy = isUpdating || isDeleting || clearCartPending;
  const grade = item.grade?.trim().toUpperCase();
  const gradeColor = grade ? PRODUCT_GRADE_CONFIG[grade]?.color ?? "#999" : undefined;

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
            {isDeleting ? t("item.deleting") : t("item.delete")}
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
                    {grade ? (
                      <View style={[styles.gradeBadge, { backgroundColor: gradeColor }]}>
                        <Text style={styles.gradeBadgeText}>Grade {grade}</Text>
                      </View>
                    ) : null}
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
                    {t("item.subtotal", { amount: Number(item.amount ?? 0).toFixed(2) })}
                  </Text>
                  <Text variant="bodySmall" style={styles.secondaryText}>
                    {t("item.import", { amount: Number(item.importAmount ?? 0).toFixed(2) })}
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
  const router = useRouter();
  const { t } = useAppTranslation(["cart", "common"]);
  const queryClient = useQueryClient();
  const { selectedStore, selectedStoreCode } = useStores();
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [keyword, setKeyword] = useState("");
  const [searchInput, setSearchInput] = useState("");
  const [filtersVisible, setFiltersVisible] = useState(false);
  const [snackbarMessage, setSnackbarMessage] = useState("");
  const [activeCartItemCode, setActiveCartItemCode] = useState<string | null>(null);
  const [activeDeleteDetailGUID, setActiveDeleteDetailGUID] = useState<string | null>(null);
  const [openSwipeDetailGUID, setOpenSwipeDetailGUID] = useState<string | null>(null);
  const [priorityProductCode, setPriorityProductCode] = useState<string | null>(null);
  const [submitPending, setSubmitPending] = useState(false);
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
    }, [hidScanner.focusHiddenInput])
  );

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
      setSnackbarMessage(error instanceof Error ? error.message : t("messages.updateQtyFailed"));
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
      setSnackbarMessage(error instanceof Error ? error.message : t("messages.deleteFailed"));
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
      setSnackbarMessage(t("messages.clearSuccess"));
    } catch (error) {
      setSnackbarMessage(error instanceof Error ? error.message : t("messages.clearFailed"));
    }
  }

  function confirmClearCart() {
    Alert.alert(t("confirm.clearTitle"), t("confirm.clearMessage"), [
      { text: t("common:actions.cancel"), style: "cancel" },
      {
        text: t("confirm.clearAction"),
        style: "destructive",
        onPress: () => {
          void handleClearCart();
        },
      },
    ]);
  }

  async function handleSubmitCart() {
    if (!selectedStoreCode) {
      setSnackbarMessage(t("messages.needStore"));
      return;
    }

    if (!cartQuery.total) {
      setSnackbarMessage(t("messages.emptyCart"));
      return;
    }

    setSubmitPending(true);
    try {
      await submitStoreOrder(selectedStoreCode);
      await queryClient.invalidateQueries({ queryKey: ["cartSummary", selectedStoreCode] });
      setPriorityProductCode(null);
      setOpenSwipeDetailGUID(null);
      setPage(1);
      setSnackbarMessage(t("messages.submitSuccess"));
      router.push("/(tabs)/orders");
    } catch (error) {
      setSnackbarMessage(error instanceof Error ? error.message : t("messages.submitFailed"));
    } finally {
      setSubmitPending(false);
    }
  }

  function confirmSubmitCart() {
    Alert.alert(t("confirm.submitTitle"), t("confirm.submitMessage"), [
      { text: t("common:actions.cancel"), style: "cancel" },
      {
        text: t("common:actions.submit"),
        onPress: () => {
          void handleSubmitCart();
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
        t={t}
        onDelete={handleDeleteCartItem}
        onOpenSwipe={setOpenSwipeDetailGUID}
        onUpdateQuantity={handleUpdateQuantity}
        openSwipeDetailGUID={openSwipeDetailGUID}
      />
    );
  }

  return (
    <SafeAreaView edges={["left", "right"]} style={styles.container}>
      <View style={styles.header}>
        <View style={styles.headerRow}>
          <Text variant="titleLarge" style={styles.headerTitle}>
            {t("title")}
          </Text>
          <View style={styles.headerActions}>
            <IconButton
              icon="filter-variant"
              mode="contained-tonal"
              onPress={() => setFiltersVisible(true)}
              style={styles.headerIconButton}
            />
            <Button
              compact
              mode="contained"
              icon="check-circle-outline"
              disabled={!selectedStoreCode || !cartQuery.total || clearCart.isPending || submitPending}
              loading={submitPending}
              onPress={confirmSubmitCart}
            >
              {t("actions.submit")}
            </Button>
            <Button
              compact
              mode="outlined"
              icon="delete-sweep-outline"
              disabled={!selectedStoreCode || !cartQuery.total || clearCart.isPending || submitPending}
              loading={clearCart.isPending}
              onPress={confirmClearCart}
            >
              {t("actions.clear")}
            </Button>
          </View>
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
            title={selectedStoreCode ? t("empty.cartEmptyTitle") : t("empty.selectStoreTitle")}
            description={
              selectedStoreCode ? t("empty.cartEmptyDescription") : t("empty.selectStoreDescription")
            }
          />
        }
        ListFooterComponent={
          cartQuery.total ? (
            <View style={styles.paginationRow}>
              <Button mode="outlined" disabled={page <= 1} onPress={() => setPage((value) => value - 1)}>
                {t("pagination.previous")}
              </Button>
              <Text variant="bodyMedium">{t("pagination.page", { page })}</Text>
              <Button mode="outlined" disabled={!canGoNextPage} onPress={() => setPage((value) => value + 1)}>
                {t("pagination.next")}
              </Button>
            </View>
          ) : null
        }
      />

      {cartQuery.isLoading ? <LoadingOverlay /> : null}

      <Portal>
        <Modal
          visible={filtersVisible}
          onDismiss={() => setFiltersVisible(false)}
          contentContainerStyle={styles.filtersModal}
        >
          <View style={styles.filtersHeader}>
            <View style={styles.filtersTitleWrap}>
              <Text variant="titleMedium">{t("filters.title")}</Text>
              <Text variant="bodySmall" style={styles.secondaryText}>
                {t("filters.currentStore", { store: selectedStore?.storeName || t("common:na") })}
              </Text>
            </View>
            <Button mode="text" onPress={() => setFiltersVisible(false)}>
              {t("common:actions.close")}
            </Button>
          </View>

          <Searchbar
            placeholder={t("filters.searchPlaceholder")}
            value={searchInput}
            onChangeText={setSearchInput}
            onSubmitEditing={() => setKeyword(searchInput.trim())}
            onIconPress={() => setKeyword(searchInput.trim())}
            style={styles.searchbar}
          />

          <View style={styles.filterActions}>
            <Button
              mode="outlined"
              onPress={() => {
                setSearchInput("");
                setKeyword("");
              }}
            >
              {t("filters.clearSearch")}
            </Button>
            <Button
              mode="contained-tonal"
              onPress={() => {
                setKeyword(searchInput.trim());
                setFiltersVisible(false);
              }}
            >
              {t("filters.applySearch")}
            </Button>
          </View>

          <View style={styles.summaryGrid}>
            <Text variant="bodySmall" style={styles.summaryText}>
              {t("filters.items", { count: cartQuery.total })}
            </Text>
            <Text variant="bodySmall" style={styles.summaryText}>
              {t("filters.totalQuantity", { count: cartQuery.cart?.totalQuantity ?? 0 })}
            </Text>
            <Text variant="bodySmall" style={styles.summaryText}>
              {t("filters.salesAmount", {
                amount: Number(cartQuery.cart?.totalAmount ?? 0).toFixed(2),
              })}
            </Text>
            <Text variant="bodySmall" style={styles.summaryText}>
              {t("filters.importAmount", {
                amount: Number(cartQuery.cart?.totalImportAmount ?? 0).toFixed(2),
              })}
            </Text>
            <Text variant="bodySmall" style={styles.summaryText}>
              {t("filters.volume", { value: Number(cartQuery.cart?.totalVolume ?? 0).toFixed(2) })}
            </Text>
          </View>

          <View style={styles.pageSizeSection}>
            <Text variant="labelLarge">{t("filters.pageSize")}</Text>
            <View style={styles.pageSizeRow}>
              {PAGE_SIZE_OPTIONS.map((option) => (
                <Chip compact key={option} selected={pageSize === option} onPress={() => setPageSize(option)}>
                  {option}
                </Chip>
              ))}
            </View>
          </View>

          <View style={styles.storeHintCard}>
            <Text variant="bodyMedium">{t("filters.storeHint")}</Text>
            <Button mode="outlined" onPress={() => router.push("/(tabs)/home")}>
              {t("filters.goHome")}
            </Button>
          </View>
        </Modal>
      </Portal>

      <ScanResultPicker
        visible={Boolean(scanResult.selectionState)}
        barcode={scanResult.selectionState?.barcode}
        items={scanResult.selectionState?.items ?? []}
        selectLabel={t("common:actions.select")}
        cancelLabel={t("common:actions.cancel")}
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
    backgroundColor: "#fff",
  },
  header: {
    paddingHorizontal: 16,
    paddingTop: 4,
    paddingBottom: 2,
  },
  headerRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 10,
  },
  headerTitle: {
    color: "#0F172A",
    fontWeight: "700",
  },
  headerActions: {
    flexDirection: "row",
    alignItems: "center",
    gap: 8,
  },
  headerIconButton: {
    margin: 0,
  },
  listContent: {
    flexGrow: 1,
    paddingHorizontal: 10,
    paddingTop: 4,
    paddingBottom: 0,
  },
  filtersModal: {
    margin: 16,
    borderRadius: 16,
    backgroundColor: "#fff",
    padding: 16,
    gap: 14,
  },
  filtersHeader: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 12,
  },
  filtersTitleWrap: {
    flex: 1,
    gap: 4,
  },
  searchbar: {
    backgroundColor: "#F6F8FB",
  },
  filterActions: {
    flexDirection: "row",
    justifyContent: "flex-end",
    gap: 10,
  },
  summaryGrid: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
  },
  summaryText: {
    color: "#333",
  },
  pageSizeSection: {
    gap: 8,
  },
  pageSizeRow: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
  },
  storeHintCard: {
    gap: 10,
    padding: 12,
    borderRadius: 12,
    backgroundColor: "#F6F8FB",
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
  gradeBadge: {
    alignSelf: "flex-start",
    borderRadius: 999,
    paddingHorizontal: 8,
    paddingVertical: 2,
  },
  gradeBadgeText: {
    color: "#fff",
    fontSize: 12,
    fontWeight: "700",
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
    paddingTop: 6,
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
