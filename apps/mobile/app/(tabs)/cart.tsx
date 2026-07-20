import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { MaterialCommunityIcons } from "@expo/vector-icons";
import {
  Alert,
  Animated,
  FlatList,
  Image,
  PanResponder,
  Pressable,
  StyleSheet,
  TextInput as NativeTextInput,
  useWindowDimensions,
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
  TextInput as PaperTextInput,
} from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import { useFocusEffect } from "@react-navigation/native";
import { type Href, useRouter } from "expo-router";
import { useIsMutating, useQueryClient } from "@tanstack/react-query";
import { EmptyState } from "@/components/ui/EmptyState";
import { LoadingOverlay } from "@/components/ui/LoadingOverlay";
import { ScanResultPicker } from "@/components/ui/ScanResultPicker";
import { resolveLocalizedErrorMessage } from "@/shared/i18n/error-message";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { submitStoreOrder } from "@/modules/orders/store-order-api";
import { useClearCart } from "@/modules/shop/use-clear-cart";
import { useCartPage } from "@/modules/shop/use-cart-page";
import { useRemoveCartLine } from "@/modules/shop/use-remove-cart-line";
import { useStores } from "@/modules/shop/use-stores";
import { useUpdateCartQuantity } from "@/modules/shop/use-update-cart-quantity";
import {
  resolveCartSummaryScale,
  resolveCheckoutBarMaxHeight,
} from "@/modules/shop/cart-summary-density";
import { useHidBarcodeScanner } from "@/modules/scanner/use-hid-barcode-scanner";
import { useScanResult } from "@/modules/scanner/use-scan-result";
import type { StoreOrderCartItem } from "@/modules/shop/types";
import { isPreorderRequiredError } from "@/modules/preorder/api";
import { canBypassPreorderGate } from "@/modules/preorder/gate";
import { PreorderGateBanner } from "@/modules/preorder/preorder-gate-banner";
import { usePreorderGate } from "@/modules/preorder/use-preorder-gate";
import { useAuthStore } from "@/store/auth-store";

const PAGE_SIZE_OPTIONS = [10, 20, 50, 100];
const SWIPE_ACTION_WIDTH = 92;
// 与 Web 端购物车备注长度保持一致。
const ORDER_REMARKS_MAX_LENGTH = 500;
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
  const importPrice = Number(item.importPrice ?? 0);
  const importAmount = Number(item.importAmount ?? importPrice * item.quantity);
  const hasZeroImportPrice = importPrice <= 0;
  const skuValue = item.itemNumber || item.productCode || "--";

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
            <View style={styles.itemAccentBar} />
            <View style={styles.itemMainRow}>
              <View style={styles.itemImageWrap}>
                {item.productImage ? (
                  <Image source={{ uri: item.productImage }} style={styles.itemImage} />
                ) : (
                  <View style={styles.itemImagePlaceholder}>
                    <MaterialCommunityIcons name="package-variant-closed" size={24} color="#8A919F" />
                  </View>
                )}
              </View>

              <View style={styles.itemBody}>
                <View style={styles.itemHeader}>
                  <View style={styles.itemTitleWrap}>
                    <Text variant="titleSmall" numberOfLines={2} style={styles.itemTitle}>
                      {item.productName || item.productCode}
                    </Text>
                    <Text variant="bodySmall" style={styles.itemNumberText}>
                      {t("item.sku", { value: skuValue })}
                    </Text>
                    <View style={styles.itemTagRow}>
                      {grade ? (
                        <View style={[styles.gradeBadge, { backgroundColor: gradeColor }]}>
                          <Text style={styles.gradeBadgeText}>{t("item.grade", { grade })}</Text>
                        </View>
                      ) : null}
                      {item.productCode ? (
                        <Text variant="labelSmall" numberOfLines={1} style={styles.itemCodeText}>
                          {item.productCode}
                        </Text>
                      ) : null}
                    </View>
                    <Text
                      variant="labelSmall"
                      numberOfLines={1}
                      style={[styles.itemUnitPriceText, hasZeroImportPrice ? styles.zeroImportText : null]}
                    >
                      {t("item.unitPrice", { amount: importPrice.toFixed(2) })}
                    </Text>
                  </View>
                  <View style={styles.itemRightColumn}>
                    <View style={styles.quantityStepper}>
                      <IconButton
                        icon="minus"
                        mode="contained-tonal"
                        size={16}
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
                        size={16}
                        disabled={isBusy}
                        loading={isUpdating}
                        onPress={() => void onUpdateQuantity(item, item.quantity + step)}
                        style={styles.quantityButton}
                      />
                    </View>
                    <Text
                      variant="labelSmall"
                      numberOfLines={1}
                      style={[styles.importPriceText, hasZeroImportPrice ? styles.zeroImportText : null]}
                    >
                      {t("item.subtotal", {
                        amount: importAmount.toFixed(2),
                      })}
                    </Text>
                  </View>
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
  const viewport = useWindowDimensions();
  const { t, language } = useAppTranslation(["cart", "common"]);
  const queryClient = useQueryClient();
  const { selectedStore, selectedStoreCode } = useStores();
  const access = useAuthStore((state) => state.access);
  const preorderGate = usePreorderGate(
    selectedStoreCode,
    canBypassPreorderGate(access)
  );
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [keyword, setKeyword] = useState("");
  const [searchInput, setSearchInput] = useState("");
  const [filtersVisible, setFiltersVisible] = useState(false);
  const [submitDialogVisible, setSubmitDialogVisible] = useState(false);
  const [orderRemarks, setOrderRemarks] = useState("");
  const [snackbarMessage, setSnackbarMessage] = useState("");
  const getErrorMessage = useCallback((error: unknown, fallbackKey: string) => (
    resolveLocalizedErrorMessage(error, {
      language,
      t,
      fallbackKey,
    })
  ), [language, t]);
  const [activeCartItemCode, setActiveCartItemCode] = useState<string | null>(null);
  const [activeDeleteDetailGUID, setActiveDeleteDetailGUID] = useState<string | null>(null);
  const [openSwipeDetailGUID, setOpenSwipeDetailGUID] = useState<string | null>(null);
  const [priorityProductCode, setPriorityProductCode] = useState<string | null>(null);
  const [submitPending, setSubmitPending] = useState(false);
  const updateCartQuantity = useUpdateCartQuantity(selectedStoreCode);
  const removeCartLine = useRemoveCartLine(selectedStoreCode);
  const clearCart = useClearCart(selectedStoreCode);

  const openPreorder = useCallback(() => {
    const firstActivation = preorderGate.activations[0];
    if (firstActivation && selectedStoreCode) {
      router.push({
        pathname: "/preorders/[activationGuid]",
        params: { activationGuid: firstActivation.activationGuid, storeCode: selectedStoreCode },
      } as unknown as Href);
      return;
    }
    router.push("/preorders" as Href);
  }, [preorderGate.activations, router, selectedStoreCode]);

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
  const totalAmount = t("summary.money", { amount: cartQuery.stats.totalImportAmount.toFixed(2) });
  const cartSummaryScale = resolveCartSummaryScale(viewport);
  const useCompactSummary = cartSummaryScale < 0.95;
  const checkoutBarMaxHeight = resolveCheckoutBarMaxHeight(viewport);
  const checkoutButtonHeight = Math.max(36, Math.min(40, Math.round(checkoutBarMaxHeight * 0.42)));

  const scanResult = useScanResult({
    onAddedToCart: async (product) => {
      setPriorityProductCode(product.productCode);
    },
    mode: "add-to-cart",
    storeCode: selectedStoreCode,
  });
  const globalCartMutationPending =
    useIsMutating({ mutationKey: ["cartMutation", selectedStoreCode ?? null] }) > 0;
  // 购物车写操作 pending 时禁止提交，避免提交到乐观缓存尚未确认的数量。
  const cartMutationPending =
    submitPending ||
    globalCartMutationPending ||
    scanResult.isBusy ||
    updateCartQuantity.isPending ||
    removeCartLine.isPending ||
    clearCart.isPending;
  const cartMutationPendingRef = useRef(cartMutationPending);
  cartMutationPendingRef.current = cartMutationPending;

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
    if (!submitDialogVisible) {
      return;
    }

    // 填写必填备注时暂停隐藏扫码输入，避免它定时抢走备注输入焦点。
    hidScanner.pauseHiddenInputFocus();
    return () => {
      hidScanner.resumeHiddenInputFocus();
    };
  }, [
    hidScanner.pauseHiddenInputFocus,
    hidScanner.resumeHiddenInputFocus,
    submitDialogVisible,
  ]);

  useEffect(() => {
    setPage(1);
  }, [pageSize, selectedStoreCode]);

  useEffect(() => {
    setPriorityProductCode(null);
    setOpenSwipeDetailGUID(null);
    setSubmitDialogVisible(false);
    setOrderRemarks("");
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
      if (isPreorderRequiredError(error)) {
        void preorderGate.refresh();
        openPreorder();
      } else {
        setSnackbarMessage(getErrorMessage(error, "messages.updateQtyFailed"));
      }
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
      setSnackbarMessage(getErrorMessage(error, "messages.deleteFailed"));
    } finally {
      setActiveDeleteDetailGUID(null);
    }
  }

  async function handleClearCart() {
    if (cartMutationPendingRef.current) {
      setSnackbarMessage(t("common:loading"));
      return;
    }

    try {
      await clearCart.mutateAsync();
      setOpenSwipeDetailGUID(null);
      setPriorityProductCode(null);
      setPage(1);
      setSnackbarMessage(t("messages.clearSuccess"));
    } catch (error) {
      setSnackbarMessage(getErrorMessage(error, "messages.clearFailed"));
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
    if (cartMutationPendingRef.current) {
      setSnackbarMessage(t("common:loading"));
      return;
    }

    if (preorderGate.normalOrderBlocked) {
      setSubmitDialogVisible(false);
      openPreorder();
      return;
    }

    if (!selectedStoreCode) {
      setSnackbarMessage(t("messages.needStore"));
      return;
    }

    if (!cartQuery.total) {
      setSnackbarMessage(t("messages.emptyCart"));
      return;
    }

    // 备注是选填项；空白备注不传给后端，避免保存无意义空字符串。
    const trimmedRemarks = orderRemarks.trim();

    setSubmitPending(true);
    try {
      await submitStoreOrder(selectedStoreCode, trimmedRemarks || undefined);
      await queryClient.invalidateQueries({ queryKey: ["cartSummary", selectedStoreCode] });
      setPriorityProductCode(null);
      setOpenSwipeDetailGUID(null);
      setSubmitDialogVisible(false);
      setOrderRemarks("");
      setPage(1);
      setSnackbarMessage(t("messages.submitSuccess"));
      router.push("/(tabs)/orders");
    } catch (error) {
      if (isPreorderRequiredError(error)) {
        setSubmitDialogVisible(false);
        void preorderGate.refresh();
        openPreorder();
      } else {
        setSnackbarMessage(getErrorMessage(error, "messages.submitFailed"));
      }
    } finally {
      setSubmitPending(false);
    }
  }

  function confirmSubmitCart() {
    if (cartMutationPendingRef.current) {
      setSnackbarMessage(t("common:loading"));
      return;
    }

    if (preorderGate.normalOrderBlocked) {
      openPreorder();
      return;
    }

    if (!selectedStoreCode) {
      setSnackbarMessage(t("messages.needStore"));
      return;
    }

    if (!cartQuery.total) {
      setSnackbarMessage(t("messages.emptyCart"));
      return;
    }

    setOrderRemarks("");
    setSubmitDialogVisible(true);
  }

  function renderCartItem({ item }: { item: StoreOrderCartItem }) {
    return (
      <CartListItemCard
        clearCartPending={clearCart.isPending}
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
    <SafeAreaView edges={["top", "left", "right"]} style={styles.container}>
      <View style={styles.header}>
        <View style={styles.headerRow}>
          <View style={styles.headerTitleRow}>
            <IconButton icon="cart-outline" size={22} style={styles.headerCartIcon} />
            <Text variant="titleLarge" style={styles.headerTitle}>
              {t("title")}
            </Text>
          </View>
          <View style={styles.headerActions}>
            <IconButton
              icon="filter-variant"
              mode="contained-tonal"
              onPress={() => setFiltersVisible(true)}
              style={styles.headerIconButton}
            />
          </View>
        </View>
      </View>

      <PreorderGateBanner gate={preorderGate} onOpen={openPreorder} />

      <FlatList
        data={filteredItems}
        keyExtractor={(item) => item.detailGUID || item.productCode}
        renderItem={renderCartItem}
        contentContainerStyle={styles.listContent}
        keyboardShouldPersistTaps="handled"
        ListHeaderComponent={
          cartQuery.total ? (
            <View style={[styles.cartStatsBar, useCompactSummary ? styles.cartStatsBarCompact : null]}>
              <View style={[styles.cartStatItem, useCompactSummary ? styles.cartStatItemCompact : null]}>
                <Text variant="labelSmall" style={styles.cartStatLabel}>
                  {t("summary.quantity")}
                </Text>
                <Text variant="titleSmall" style={styles.cartStatValue}>
                  {cartQuery.stats.totalQuantity}
                </Text>
              </View>
              <View style={[styles.cartStatDivider, useCompactSummary ? styles.cartStatDividerCompact : null]} />
              <View style={[styles.cartStatItem, useCompactSummary ? styles.cartStatItemCompact : null]}>
                <Text variant="labelSmall" style={styles.cartStatLabel}>
                  {t("summary.sku")}
                </Text>
                <Text variant="titleSmall" style={styles.cartStatValue}>
                  {cartQuery.stats.skuCount}
                </Text>
              </View>
              <View style={[styles.cartStatDivider, useCompactSummary ? styles.cartStatDividerCompact : null]} />
              <View
                style={[
                  styles.cartStatItem,
                  styles.cartStatAmountItem,
                  useCompactSummary ? styles.cartStatItemCompact : null,
                ]}
              >
                <Text variant="labelSmall" style={styles.cartStatLabel}>
                  {t("summary.orderTotal")}
                </Text>
                <Text variant="titleSmall" style={styles.cartStatAmount}>
                  {totalAmount}
                </Text>
              </View>
            </View>
          ) : null
        }
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

      {cartQuery.total ? (
        <View
          style={[
            styles.checkoutBar,
            { maxHeight: checkoutBarMaxHeight },
            useCompactSummary ? styles.checkoutBarCompact : null,
          ]}
        >
          <View style={styles.checkoutSummaryRow}>
            <View style={styles.checkoutSummaryGroup}>
              <Text variant="bodySmall" style={styles.checkoutLabel}>
                {t("checkout.totalItems")}
              </Text>
              <Text variant="bodySmall" style={styles.checkoutValue}>
                {cartQuery.stats.totalQuantity}
              </Text>
            </View>
            <View style={[styles.checkoutSummaryGroup, styles.checkoutSummaryAmount]}>
              <Text variant="bodySmall" style={styles.checkoutTotalLabel}>
                {t("checkout.totalAmount")}
              </Text>
              <Text variant="bodySmall" numberOfLines={1} style={styles.checkoutTotalValue}>
                {totalAmount}
              </Text>
            </View>
          </View>
          <View style={styles.checkoutActionsRow}>
            <Button
              mode="contained"
              icon="arrow-right"
              contentStyle={[styles.checkoutButtonContent, { minHeight: checkoutButtonHeight }]}
              disabled={!selectedStoreCode || !cartQuery.total || cartMutationPending}
              loading={submitPending}
              onPress={confirmSubmitCart}
              style={[styles.checkoutButton, styles.checkoutSubmitButton]}
              labelStyle={useCompactSummary ? styles.checkoutButtonLabelCompact : undefined}
            >
              {t("checkout.submit")}
            </Button>
            <Button
              compact
              mode="outlined"
              icon="delete-sweep-outline"
              contentStyle={styles.clearCartButtonContent}
              disabled={!selectedStoreCode || !cartQuery.total || cartMutationPending}
              loading={clearCart.isPending}
              onPress={confirmClearCart}
              style={styles.checkoutClearButton}
              labelStyle={[
                styles.clearCartLabel,
                useCompactSummary ? styles.clearCartLabelCompact : null,
              ]}
            >
              {t("checkout.clear")}
            </Button>
          </View>
        </View>
      ) : null}

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

        <Modal
          visible={submitDialogVisible}
          onDismiss={() => {
            if (!submitPending) {
              setSubmitDialogVisible(false);
            }
          }}
          contentContainerStyle={styles.submitModal}
        >
          <Text variant="titleMedium" style={styles.submitModalTitle}>
            {t("confirm.submitTitle")}
          </Text>
          <Text variant="bodyMedium" style={styles.submitModalMessage}>
            {t("confirm.submitMessage")}
          </Text>
          <PaperTextInput
            mode="outlined"
            label={t("remarks.label")}
            placeholder={t("remarks.placeholder")}
            value={orderRemarks}
            onChangeText={(value) => setOrderRemarks(value.slice(0, ORDER_REMARKS_MAX_LENGTH))}
            multiline
            numberOfLines={3}
            maxLength={ORDER_REMARKS_MAX_LENGTH}
            disabled={submitPending}
            style={styles.submitRemarksInput}
          />
          <Text variant="labelSmall" style={styles.submitRemarksCounter}>
            {t("remarks.counter", {
              count: orderRemarks.length,
              max: ORDER_REMARKS_MAX_LENGTH,
            })}
          </Text>
          <View style={styles.submitModalActions}>
            <Button
              mode="contained-tonal"
              disabled={submitPending}
              onPress={() => setSubmitDialogVisible(false)}
              style={styles.submitModalButton}
            >
              {t("common:actions.cancel")}
            </Button>
            <Button
              mode="contained"
              loading={submitPending}
              disabled={
                !selectedStoreCode ||
                !cartQuery.total ||
                cartMutationPending
              }
              onPress={() => {
                void handleSubmitCart();
              }}
              style={[styles.submitModalButton, styles.submitModalSubmitButton]}
            >
              {t("common:actions.submit")}
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
        <NativeTextInput style={styles.hiddenInput} {...hidScanner.textInputProps} />
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
    paddingTop: 0,
    paddingBottom: 6,
    borderBottomWidth: StyleSheet.hairlineWidth,
    borderBottomColor: "#C5C6CD",
    backgroundColor: "#FFFFFF",
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
  headerTitleRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: 0,
  },
  headerCartIcon: {
    margin: 0,
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
    paddingHorizontal: 12,
    paddingTop: 10,
    paddingBottom: 20,
  },
  cartStatsBar: {
    flexDirection: "row",
    alignItems: "center",
    marginBottom: 10,
    paddingHorizontal: 12,
    paddingVertical: 10,
    borderRadius: 10,
    backgroundColor: "#F8FAFC",
    borderWidth: StyleSheet.hairlineWidth,
    borderColor: "#D9E0E8",
  },
  cartStatsBarCompact: {
    marginBottom: 6,
    paddingHorizontal: 9,
    paddingVertical: 7,
    borderRadius: 8,
  },
  cartStatItem: {
    flex: 1,
    gap: 2,
  },
  cartStatItemCompact: {
    gap: 0,
  },
  cartStatAmountItem: {
    flex: 1.35,
  },
  cartStatLabel: {
    color: "#6B7280",
    fontWeight: "600",
  },
  cartStatValue: {
    color: "#111827",
    fontWeight: "800",
  },
  cartStatAmount: {
    color: "#111827",
    fontWeight: "800",
  },
  cartStatDivider: {
    width: StyleSheet.hairlineWidth,
    height: 30,
    marginHorizontal: 8,
    backgroundColor: "#D9E0E8",
  },
  cartStatDividerCompact: {
    height: 22,
    marginHorizontal: 6,
  },
  filtersModal: {
    margin: 16,
    borderRadius: 16,
    backgroundColor: "#fff",
    padding: 16,
    gap: 14,
  },
  submitModal: {
    margin: 18,
    borderRadius: 16,
    backgroundColor: "#fff",
    padding: 18,
    gap: 12,
  },
  submitModalTitle: {
    color: "#0F172A",
    fontWeight: "800",
  },
  submitModalMessage: {
    color: "#6B7280",
  },
  submitRemarksInput: {
    backgroundColor: "#FFFFFF",
  },
  submitRemarksCounter: {
    alignSelf: "flex-end",
    color: "#6B7280",
  },
  submitModalActions: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 10,
  },
  submitModalButton: {
    flex: 1,
    borderRadius: 8,
  },
  submitModalSubmitButton: {
    backgroundColor: "#111111",
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
    marginBottom: 8,
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
    overflow: "hidden",
    borderColor: "#D7DCE2",
    backgroundColor: "#FFFFFF",
  },
  itemContent: {
    paddingVertical: 10,
    paddingLeft: 12,
    paddingRight: 10,
  },
  itemAccentBar: {
    position: "absolute",
    left: 0,
    top: 0,
    bottom: 0,
    width: 4,
    borderTopLeftRadius: 12,
    borderBottomLeftRadius: 12,
    backgroundColor: "#9ADBC3",
  },
  itemMainRow: {
    flexDirection: "row",
    alignItems: "flex-start",
    gap: 12,
  },
  itemImageWrap: {
    width: 60,
    height: 60,
  },
  itemImage: {
    width: "100%",
    height: "100%",
    borderRadius: 8,
    backgroundColor: "#E9EDF2",
  },
  itemImagePlaceholder: {
    width: "100%",
    height: "100%",
    borderRadius: 8,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: "#EEF2F6",
  },
  itemBody: {
    flex: 1,
    minWidth: 0,
  },
  itemHeader: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 10,
  },
  itemTitleWrap: {
    flex: 1,
    minWidth: 0,
    gap: 3,
  },
  gradeBadge: {
    alignSelf: "flex-start",
    borderRadius: 999,
    paddingHorizontal: 7,
    paddingVertical: 2,
  },
  gradeBadgeText: {
    color: "#fff",
    fontSize: 11,
    fontWeight: "700",
  },
  itemTitle: {
    flex: 1,
    color: "#111827",
    fontSize: 14,
    lineHeight: 18,
    fontWeight: "800",
  },
  itemNumberText: {
    color: "#6B7280",
    fontWeight: "600",
  },
  itemTagRow: {
    flexDirection: "row",
    alignItems: "center",
    flexWrap: "wrap",
    gap: 6,
  },
  itemCodeText: {
    flexShrink: 1,
    color: "#8A919F",
    fontWeight: "600",
  },
  itemUnitPriceText: {
    color: "#111827",
    fontWeight: "700",
  },
  itemRightColumn: {
    width: 104,
    alignItems: "flex-end",
    gap: 6,
  },
  quantityStepper: {
    flexDirection: "row",
    alignItems: "center",
    gap: 0,
    borderWidth: StyleSheet.hairlineWidth,
    borderColor: "#CDD4DC",
    borderRadius: 8,
    backgroundColor: "#F7F8FA",
  },
  quantityButton: {
    margin: 0,
    width: 28,
    height: 28,
  },
  quantityValueWrap: {
    minWidth: 36,
    alignItems: "center",
    justifyContent: "center",
    paddingHorizontal: 2,
  },
  quantityValue: {
    color: "#171C1F",
    fontWeight: "700",
  },
  importPriceText: {
    color: "#111827",
    fontWeight: "700",
    textAlign: "right",
  },
  zeroImportText: {
    color: "#F5222D",
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
    paddingTop: 8,
    paddingBottom: 4,
  },
  checkoutBar: {
    borderTopWidth: StyleSheet.hairlineWidth,
    borderTopColor: "#D7DCE2",
    backgroundColor: "#FFFFFF",
    paddingHorizontal: 12,
    paddingTop: 8,
    paddingBottom: 8,
    gap: 6,
    overflow: "hidden",
    shadowColor: "#0F172A",
    shadowOpacity: 0.08,
    shadowRadius: 10,
    shadowOffset: { width: 0, height: -4 },
    elevation: 8,
  },
  checkoutBarCompact: {
    paddingHorizontal: 10,
    paddingTop: 6,
    paddingBottom: 6,
    gap: 5,
  },
  checkoutSummaryRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 12,
  },
  checkoutSummaryGroup: {
    flexDirection: "row",
    alignItems: "center",
    gap: 6,
    minWidth: 0,
  },
  checkoutSummaryAmount: {
    flex: 1,
    justifyContent: "flex-end",
  },
  checkoutActionsRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: 8,
  },
  checkoutLabel: {
    color: "#6B7280",
  },
  checkoutValue: {
    color: "#374151",
    fontWeight: "600",
  },
  checkoutTotalLabel: {
    color: "#111827",
    fontWeight: "700",
  },
  checkoutTotalValue: {
    color: "#111827",
    fontWeight: "800",
  },
  checkoutButton: {
    borderRadius: 8,
    backgroundColor: "#111111",
  },
  checkoutSubmitButton: {
    flex: 1,
  },
  checkoutButtonContent: {
    flexDirection: "row-reverse",
  },
  checkoutButtonLabelCompact: {
    fontSize: 13,
  },
  checkoutClearButton: {
    borderRadius: 8,
    borderColor: "#D1D5DB",
  },
  clearCartButtonContent: {
    minHeight: 36,
  },
  clearCartLabel: {
    color: "#6B7280",
  },
  clearCartLabelCompact: {
    fontSize: 12,
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
