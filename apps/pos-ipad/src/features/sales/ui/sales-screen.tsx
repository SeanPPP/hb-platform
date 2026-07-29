import { useEffect, useRef, useState, useSyncExternalStore } from "react";
import {
  FlatList,
  Modal,
  Pressable,
  StyleSheet,
  Text,
  TextInput,
  View,
  type StyleProp,
  type ViewStyle,
} from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";

import {
  drawerWarningCopyKey,
  resolveSalesLocale,
  salesText,
  type SalesCopyKey,
  type SalesLocale,
} from "./sales-copy";
import {
  deriveCashDraft,
  formatAud,
  getCashDueCents,
  MIN_TOUCH_TARGET,
  parseCashInput,
  type SalesPresenter,
  type SalesProductSearchItem,
} from "./sales-presenter";
import { SalesToolbar, type SalesToolbarAction } from "./sales-toolbar";
import {
  DEFAULT_SALES_TOOLBAR_ORDER,
  type SalesToolbarActionId,
} from "./sales-toolbar-order";

import type { NewTransactionGate } from "@/core/contracts/app-updates";
import { usePosShellStore } from "@/ui/shell/pos-shell-store";
import { PosStatusStrip } from "@/ui/shell/status-strip";
import { posColors } from "@/ui/theme";

const QUICK_DISCOUNTS = [0, 1_000, 2_000, 3_000, 4_000, 5_000] as const;
const QUICK_ORDER_DISCOUNTS = [1_000, 2_000, 3_000, 4_000, 5_000] as const;

type LineEditMode =
  "quantity" | "price" | "discount-amount" | "discount-percent";

type LineEditState = Readonly<{
  lineId: string;
  mode: LineEditMode;
  value: string;
}>;

type OrderEditState = Readonly<{
  mode: "amount" | "percent";
  value: string;
}>;

type SalesScreenProps = Readonly<{
  presenter: SalesPresenter;
  locale?: SalesLocale;
  newTransactionGate?: NewTransactionGate;
  onOpenAttendanceAudit?: () => void;
  onOpenCameraScanner?: () => void;
  onOpenCatalogMaintenance?: () => void;
  onOpenDailyClose?: () => void;
  onOpenHeldOrders?: () => void;
  onOpenInstallments?: () => void;
  onOpenPayment?: () => void;
  onOpenRequiredUpdate?: () => void;
  onOpenRemoteHistory?: () => void;
  onOpenReturns?: () => void;
  onOpenSettings?: () => void;
  onOpenSpecialProducts?: () => void;
  onOpenSyncHistory?: () => void;
  onSwitchLanguage?: () => void;
  onToolbarOrderChange?: (order: readonly SalesToolbarActionId[]) => void;
  onManualInputFocusChange?: (focused: boolean) => void;
  showStatusStrip?: boolean;
  toolbarOrder?: readonly SalesToolbarActionId[];
}>;

type ActionButtonProps = Readonly<{
  label: string;
  onPress(): void;
  disabled?: boolean;
  tone?: "primary" | "secondary" | "danger" | "quiet";
  testID?: string;
  style?: StyleProp<ViewStyle>;
  accessibilityLabel?: string;
}>;

export function SalesScreen({
  presenter,
  locale: localeOverride,
  newTransactionGate,
  onOpenAttendanceAudit,
  onOpenCameraScanner,
  onOpenCatalogMaintenance,
  onOpenDailyClose,
  onOpenHeldOrders,
  onOpenInstallments,
  onOpenPayment,
  onOpenRequiredUpdate,
  onOpenRemoteHistory,
  onOpenReturns,
  onOpenSettings,
  onOpenSpecialProducts,
  onOpenSyncHistory,
  onSwitchLanguage,
  onToolbarOrderChange = () => undefined,
  onManualInputFocusChange,
  showStatusStrip = true,
  toolbarOrder = DEFAULT_SALES_TOOLBAR_ORDER,
}: SalesScreenProps) {
  const state = useSyncExternalStore(
    presenter.subscribe,
    presenter.getState,
    presenter.getState,
  );
  const connectivity = usePosShellStore((current) => current.connectivity);
  const locale = localeOverride ?? resolveSalesLocale();
  const t = (
    key: SalesCopyKey,
    values?: Readonly<Record<string, string | number>>,
  ) => salesText(locale, key, values);
  const [discountLineId, setDiscountLineId] = useState<string | null>(null);
  const [openItemVisible, setOpenItemVisible] = useState(false);
  const [openItemPrice, setOpenItemPrice] = useState("");
  const [lineEdit, setLineEdit] = useState<LineEditState | null>(null);
  const [orderDiscountVisible, setOrderDiscountVisible] = useState(false);
  const [orderEdit, setOrderEdit] = useState<OrderEditState | null>(null);
  const [clearCartVisible, setClearCartVisible] = useState(false);
  const [searchSoftInputOnFocus, setSearchSoftInputOnFocus] = useState(false);
  const searchInputRef = useRef<TextInput>(null);
  const searchKeyboardRequestRef = useRef(false);
  const searchKeyboardTimerRef = useRef<ReturnType<typeof setTimeout> | null>(
    null,
  );
  const manualInputFocusChangeRef = useRef(onManualInputFocusChange);
  const manualInputActiveRef = useRef(false);
  const manualInputBlurTimerRef = useRef<ReturnType<typeof setTimeout> | null>(
    null,
  );
  manualInputFocusChangeRef.current = onManualInputFocusChange;
  const successDrawerWarning = state.success
    ? drawerWarningCopyKey(state.success.drawerDisposition)
    : null;
  const cartEmpty = state.cart.lines.length === 0;
  const newTransactionBlocked =
    cartEmpty && newTransactionGate?.canStartNewTransaction === false;
  const manualInputTreeUnavailable =
    state.phase === "success" ||
    state.phase === "locked" ||
    newTransactionBlocked;

  const clearManualInputBlurTimer = (): void => {
    if (manualInputBlurTimerRef.current === null) return;
    clearTimeout(manualInputBlurTimerRef.current);
    manualInputBlurTimerRef.current = null;
  };

  const clearSearchKeyboardTimer = (): void => {
    if (searchKeyboardTimerRef.current === null) return;
    clearTimeout(searchKeyboardTimerRef.current);
    searchKeyboardTimerRef.current = null;
  };

  const requestSearchKeyboard = (): void => {
    searchKeyboardRequestRef.current = true;
    if (!searchSoftInputOnFocus) {
      setSearchSoftInputOnFocus(true);
      return;
    }
    // 软键盘被手动收起但输入仍聚焦时，先切回 HID 模式再重新启用。
    setSearchSoftInputOnFocus(false);
  };

  const notifyManualInputFocused = (): void => {
    clearManualInputBlurTimer();
    if (manualInputActiveRef.current) return;
    manualInputActiveRef.current = true;
    manualInputFocusChangeRef.current?.(true);
  };

  const notifyManualInputBlurred = (): void => {
    if (!manualInputActiveRef.current || manualInputBlurTimerRef.current)
      return;
    // TextInput 切换时先触发 blur 再触发 focus；延后一轮才释放 HID 捕获。
    manualInputBlurTimerRef.current = setTimeout(() => {
      manualInputBlurTimerRef.current = null;
      if (!manualInputActiveRef.current) return;
      manualInputActiveRef.current = false;
      manualInputFocusChangeRef.current?.(false);
    }, 0);
  };

  const resetSearchInputToHidMode = (): void => {
    clearSearchKeyboardTimer();
    searchKeyboardRequestRef.current = false;
    setSearchSoftInputOnFocus(false);
  };

  const handleSearchInputBlur = (): void => {
    resetSearchInputToHidMode();
    notifyManualInputBlurred();
  };

  const closeCashInput = (): void => {
    notifyManualInputBlurred();
    presenter.closeCash();
  };

  const closeOpenItemInput = (): void => {
    notifyManualInputBlurred();
    setOpenItemVisible(false);
  };

  const closeLineEditInput = (): void => {
    notifyManualInputBlurred();
    setLineEdit(null);
  };

  const closeOrderEditInput = (): void => {
    notifyManualInputBlurred();
    setOrderEdit(null);
  };

  useEffect(
    () => () => {
      clearManualInputBlurTimer();
      clearSearchKeyboardTimer();
      if (!manualInputActiveRef.current) return;
      manualInputActiveRef.current = false;
      manualInputFocusChangeRef.current?.(false);
    },
    [],
  );

  useEffect(() => {
    if (searchSoftInputOnFocus || !searchKeyboardRequestRef.current) return;
    searchKeyboardTimerRef.current = setTimeout(() => {
      searchKeyboardTimerRef.current = null;
      setSearchSoftInputOnFocus(true);
    }, 0);
    return clearSearchKeyboardTimer;
  }, [searchSoftInputOnFocus]);

  useEffect(() => {
    if (!searchSoftInputOnFocus || !searchKeyboardRequestRef.current) return;
    searchKeyboardRequestRef.current = false;
    searchInputRef.current?.focus();
  }, [searchSoftInputOnFocus]);

  useEffect(() => {
    if (!manualInputTreeUnavailable) return;
    if (searchKeyboardTimerRef.current !== null) {
      clearTimeout(searchKeyboardTimerRef.current);
      searchKeyboardTimerRef.current = null;
    }
    searchKeyboardRequestRef.current = false;
    setSearchSoftInputOnFocus(false);
    notifyManualInputBlurred();
  }, [manualInputTreeUnavailable]);

  const openLineEditor = (lineId: string): void => {
    const line = state.cart.lines.find(
      (candidate) => candidate.lineId === lineId,
    );
    if (!line) return;
    setLineEdit({
      lineId,
      mode: "quantity",
      value: line.quantity,
    });
  };

  const selectLineEditMode = (mode: LineEditMode): void => {
    setLineEdit((current) => {
      if (!current) return null;
      const line = state.cart.lines.find(
        (candidate) => candidate.lineId === current.lineId,
      );
      if (!line) return null;
      return {
        lineId: current.lineId,
        mode,
        value:
          mode === "quantity"
            ? line.quantity
            : mode === "price"
              ? formatEditorMoney(line.unitPrice.cents)
              : mode === "discount-amount"
                ? formatEditorMoney(line.discount.cents)
                : "",
      };
    });
  };

  const submitLineEdit = (): void => {
    if (!lineEdit) return;
    let submission: Promise<boolean>;
    if (lineEdit.mode === "quantity") {
      submission = presenter.setLineQuantity(
        lineEdit.lineId,
        parsePositiveInteger(lineEdit.value) ?? 0,
      );
    } else if (lineEdit.mode === "price") {
      submission = presenter.setLineUnitPriceCents(
        lineEdit.lineId,
        parseCashInput(lineEdit.value) ?? -1,
      );
    } else if (lineEdit.mode === "discount-amount") {
      submission = presenter.applyLineDiscountAmountCents(
        lineEdit.lineId,
        parseCashInput(lineEdit.value) ?? -1,
      );
    } else {
      submission = presenter.applyLineManualDiscountBasisPoints(
        lineEdit.lineId,
        parsePercentageBasisPoints(lineEdit.value) ?? -1,
      );
    }
    void submission.then((applied) => {
      if (applied) closeLineEditInput();
    });
  };

  const submitOrderEdit = (): void => {
    if (!orderEdit) return;
    const submission =
      orderEdit.mode === "amount"
        ? presenter.applyOrderDiscountAmountCents(
            parseCashInput(orderEdit.value) ?? -1,
          )
        : presenter.applyOrderManualDiscountBasisPoints(
            parsePercentageBasisPoints(orderEdit.value) ?? -1,
          );
    void submission.then((applied) => {
      if (applied) closeOrderEditInput();
    });
  };

  if (state.phase === "success" && state.success) {
    return (
      <SafeAreaView style={styles.safeArea} testID="sales-screen">
        <View style={styles.successScreen} testID="sales-success">
          <View style={styles.successMark}>
            <Text style={styles.successMarkText}>✓</Text>
          </View>
          <Text style={styles.successEyebrow}>{t("success.eyebrow")}</Text>
          <Text style={styles.successTitle}>{t("success.title")}</Text>
          <Text style={styles.successOrder}>
            {t("success.order", { orderGuid: state.success.orderGuid })}
          </Text>
          <View style={styles.changeCard}>
            <Text style={styles.changeLabel}>{t("success.change")}</Text>
            <Text style={styles.changeAmount}>
              {formatAud(state.success.changeCents, locale)}
            </Text>
          </View>
          <Text style={styles.successSync}>
            {connectivity === "offline"
              ? t("success.syncOffline")
              : t("success.syncOnline")}
          </Text>
          {successDrawerWarning ? (
            <Text style={styles.successWarning} testID="sales-drawer-warning">
              {t(successDrawerWarning)}
            </Text>
          ) : null}
          {state.errorCode === "cart-clear-failed" ? (
            <ErrorBanner
              dismissLabel={t("common.dismiss")}
              message={t("error.cartClearFailed")}
              onDismiss={() => presenter.dismissError()}
            />
          ) : null}
          <ActionButton
            disabled={!state.success.clearCartSignalled}
            label={t("success.newSale")}
            onPress={() => presenter.startNewSale()}
            style={styles.successAction}
            testID="sales-new-sale"
          />
        </View>
        {showStatusStrip ? <PosStatusStrip /> : null}
      </SafeAreaView>
    );
  }

  if (state.phase === "locked") {
    return (
      <SafeAreaView style={styles.safeArea} testID="sales-screen">
        <View style={styles.lockedScreen} testID="sales-locked">
          <View style={styles.lockIcon}>
            <Text style={styles.lockIconText}>●</Text>
          </View>
          <Text style={styles.lockedTitle}>{t("locked.title")}</Text>
          <Text style={styles.lockedHint}>{t("locked.hint")}</Text>
          <View style={styles.lockedNotice}>
            <Text style={styles.lockedNoticeText}>
              {t("locked.unlockPending")}
            </Text>
          </View>
        </View>
        {showStatusStrip ? <PosStatusStrip /> : null}
      </SafeAreaView>
    );
  }

  const runtimeUnavailable =
    !state.capabilities.catalog ||
    !state.capabilities.cartEditing ||
    !state.capabilities.cashCheckout;
  const cashDraft = deriveCashDraft(state.cart, state.cashTenderedText);
  const toolbarActions: readonly SalesToolbarAction[] = [
    ...(onOpenHeldOrders
      ? [
          {
            id: "held-orders" as const,
            label: t("header.heldOrders"),
            onPress: onOpenHeldOrders,
            testID: "sales-open-held-orders",
            tone: "quiet" as const,
          },
        ]
      : []),
    ...(onOpenDailyClose
      ? [
          {
            id: "daily-close" as const,
            label: locale === "zh" ? "日结" : "Daily close",
            onPress: onOpenDailyClose,
            testID: "sales-open-daily-close",
            tone: "quiet" as const,
          },
        ]
      : []),
    ...(onOpenReturns
      ? [
          {
            id: "returns" as const,
            label: locale === "zh" ? "退货" : "Returns",
            onPress: onOpenReturns,
            testID: "sales-open-returns",
            tone: "quiet" as const,
          },
        ]
      : []),
    ...(onOpenRemoteHistory
      ? [
          {
            id: "remote-history" as const,
            label: locale === "zh" ? "远程历史" : "History",
            onPress: onOpenRemoteHistory,
            testID: "sales-open-remote-history",
            tone: "quiet" as const,
          },
        ]
      : []),
    ...(onOpenSpecialProducts
      ? [
          {
            id: "special-products" as const,
            label: locale === "zh" ? "特殊商品" : "Specials",
            onPress: onOpenSpecialProducts,
            testID: "sales-open-special-products",
            tone: "quiet" as const,
          },
        ]
      : []),
    ...(onOpenInstallments
      ? [
          {
            id: "installments" as const,
            label: locale === "zh" ? "分期" : "Installments",
            onPress: onOpenInstallments,
            testID: "sales-open-installments",
            tone: "quiet" as const,
          },
        ]
      : []),
    ...(onOpenSettings
      ? [
          {
            id: "settings" as const,
            label: locale === "zh" ? "设置" : "Settings",
            onPress: onOpenSettings,
            testID: "sales-open-settings",
            tone: "quiet" as const,
          },
        ]
      : []),
    ...(onOpenAttendanceAudit
      ? [
          {
            id: "attendance-audit" as const,
            label: locale === "zh" ? "考勤与审计" : "Attendance",
            onPress: onOpenAttendanceAudit,
            testID: "sales-open-attendance-audit",
            tone: "quiet" as const,
          },
        ]
      : []),
    ...(onOpenSyncHistory
      ? [
          {
            id: "sync-history" as const,
            label: t("header.syncHistory"),
            onPress: onOpenSyncHistory,
            testID: "sales-open-sync-history",
            tone: "quiet" as const,
          },
        ]
      : []),
    ...(onOpenCatalogMaintenance
      ? [
          {
            id: "catalog-maintenance" as const,
            label: t("header.catalogMaintenance"),
            onPress: onOpenCatalogMaintenance,
            testID: "sales-open-catalog-maintenance",
            tone: "quiet" as const,
          },
        ]
      : []),
    {
      disabled: !state.capabilities.hold || cartEmpty,
      id: "hold",
      label: t("header.hold"),
      onPress: () => {
        void presenter.holdCart();
      },
      testID: "sales-hold",
      tone: "secondary",
    },
    {
      id: "language",
      label: locale === "zh" ? "English" : "中文",
      onPress: () => onSwitchLanguage?.(),
      testID: "sales-switch-language",
      tone: "quiet",
    },
    {
      disabled: !state.capabilities.lock,
      id: "lock",
      label: t("header.lock"),
      onPress: () => {
        void presenter.lockTerminal();
      },
      testID: "sales-lock",
      tone: "quiet",
    },
  ];

  return (
    <SafeAreaView style={styles.safeArea} testID="sales-screen">
      <View style={styles.header}>
        <View>
          <Text style={styles.brand}>{t("app.title")}</Text>
          <Text style={styles.workspace}>{t("app.workspace")}</Text>
        </View>
        <SalesToolbar
          accessibilityCopy={
            locale === "zh"
              ? {
                  moveEarlier: "前移",
                  moveLater: "后移",
                  reorderHint: "长按后拖动可排序；也可使用前移或后移操作。",
                  positionChanged: (label, position, total) =>
                    `${label} 已移到第 ${position} 位，共 ${total} 位。`,
                }
              : {
                  moveEarlier: "Move earlier",
                  moveLater: "Move later",
                  reorderHint:
                    "Long press and drag to reorder, or use the move earlier and move later actions.",
                  positionChanged: (label, position, total) =>
                    `${label} moved to position ${position} of ${total}.`,
                }
          }
          actions={toolbarActions}
          canonicalOrder={toolbarOrder}
          onOrderChange={onToolbarOrderChange}
          style={styles.headerActions}
        />
      </View>

      {showStatusStrip ? <PosStatusStrip /> : null}

      {runtimeUnavailable ? (
        <View
          accessibilityRole="alert"
          style={styles.runtimeBanner}
          testID="sales-runtime-unavailable"
        >
          <Text style={styles.runtimeBannerText}>
            {t("app.runtimeUnavailable")}
          </Text>
        </View>
      ) : null}
      {newTransactionBlocked ? (
        <View
          accessibilityRole="alert"
          style={styles.updateGateBanner}
          testID="sales-new-transaction-gate"
        >
          <View style={styles.updateGateCopy}>
            <Text style={styles.updateGateTitle}>
              {t(
                newTransactionGate?.state === "force-update"
                  ? "updateGate.forceTitle"
                  : newTransactionGate?.state === "disabled"
                    ? "updateGate.disabledTitle"
                    : "updateGate.uncheckedTitle",
              )}
            </Text>
            <Text style={styles.updateGateHint}>{t("updateGate.hint")}</Text>
          </View>
          {newTransactionGate?.state === "force-update" &&
          onOpenRequiredUpdate ? (
            <ActionButton
              label={t("updateGate.openStore")}
              onPress={onOpenRequiredUpdate}
              testID="sales-open-required-update"
              tone="secondary"
            />
          ) : null}
        </View>
      ) : null}

      <View style={styles.workspaceRow}>
        <View style={styles.catalogPane}>
          <Text style={styles.paneTitle}>{t("catalog.title")}</Text>
          <View style={styles.searchInputRow}>
            <TextInput
              ref={searchInputRef}
              accessibilityLabel={t("catalog.searchPlaceholder")}
              autoCapitalize="none"
              autoCorrect={false}
              editable={state.capabilities.catalog && !newTransactionBlocked}
              onBlur={handleSearchInputBlur}
              onChangeText={(value) => presenter.setQuery(value)}
              onFocus={notifyManualInputFocused}
              onSubmitEditing={() => {
                void presenter.addLookupCode();
              }}
              placeholder={t("catalog.searchPlaceholder")}
              placeholderTextColor="#7B8793"
              returnKeyType="done"
              selectionColor={posColors.orange}
              showSoftInputOnFocus={searchSoftInputOnFocus}
              style={styles.searchInput}
              submitBehavior="blurAndSubmit"
              testID="sales-search-input"
              value={state.query}
            />
            <ActionButton
              disabled={!state.capabilities.catalog || newTransactionBlocked}
              label={t("catalog.keyboard")}
              onPress={requestSearchKeyboard}
              style={styles.searchKeyboardAction}
              testID="sales-show-keyboard"
              tone="secondary"
            />
          </View>
          <View style={styles.searchActions}>
            <ActionButton
              disabled={!state.capabilities.catalog || newTransactionBlocked}
              label={t("catalog.search")}
              onPress={() => {
                void presenter.searchProducts();
              }}
              style={styles.searchAction}
              testID="sales-search-button"
              tone="secondary"
            />
            <ActionButton
              disabled={!state.capabilities.catalog || newTransactionBlocked}
              label={t("catalog.addCode")}
              onPress={() => {
                void presenter.addLookupCode();
              }}
              style={styles.searchAction}
              testID="sales-add-code-button"
            />
            <ActionButton
              disabled={!state.capabilities.catalog || newTransactionBlocked}
              label={t("catalog.openItem")}
              onPress={() => {
                setOpenItemPrice("");
                setOpenItemVisible(true);
              }}
              style={styles.searchAction}
              testID="sales-open-item-button"
              tone="secondary"
            />
            {onOpenCameraScanner ? (
              <ActionButton
                disabled={!state.capabilities.catalog || newTransactionBlocked}
                label={t("catalog.cameraScan")}
                onPress={onOpenCameraScanner}
                style={styles.searchAction}
                testID="sales-open-camera-scanner"
                tone="secondary"
              />
            ) : null}
          </View>
          <Text style={styles.scanHint}>{t("catalog.scanHint")}</Text>

          {state.searchStatus === "searching" ? (
            <Text style={styles.searchMessage}>{t("catalog.searching")}</Text>
          ) : null}
          {state.searchStatus === "ready" &&
          state.searchResults.length === 0 ? (
            <Text style={styles.searchMessage}>{t("catalog.noResults")}</Text>
          ) : null}
          <FlatList
            contentContainerStyle={styles.searchResults}
            data={state.searchResults}
            keyExtractor={(item) => `${item.productCode}:${item.lookupCode}`}
            keyboardShouldPersistTaps="handled"
            renderItem={({ item }) => (
              <ProductSearchRow
                disabled={newTransactionBlocked}
                item={item}
                locale={locale}
                onAdd={() => {
                  void presenter.addProduct(item);
                }}
                t={t}
              />
            )}
          />
        </View>

        <View style={styles.cartPane}>
          <View style={styles.cartHeader}>
            <Text style={styles.paneTitle}>{t("cart.title")}</Text>
            <Text style={styles.cartCount}>
              {t("cart.items", { count: state.cart.lines.length })}
            </Text>
          </View>
          {cartEmpty ? (
            <View style={styles.emptyCart} testID="sales-cart-empty">
              <Text style={styles.emptyCartIcon}>＋</Text>
              <Text style={styles.emptyCartTitle}>{t("cart.emptyTitle")}</Text>
              <Text style={styles.emptyCartHint}>{t("cart.emptyHint")}</Text>
            </View>
          ) : (
            <FlatList
              contentContainerStyle={styles.cartList}
              data={state.cart.lines}
              keyExtractor={(line) => line.lineId}
              renderItem={({ item }) => (
                <View
                  style={styles.cartLine}
                  testID={`sales-line-${item.lineId}`}
                >
                  <View style={styles.cartLineTop}>
                    <View style={styles.cartLineIdentity}>
                      <Text numberOfLines={2} style={styles.cartLineName}>
                        {item.displayName}
                      </Text>
                      <Text numberOfLines={1} style={styles.cartLineCode}>
                        {item.lookupCode || item.productCode}
                      </Text>
                      <Text style={styles.cartLineUnitPrice}>
                        {formatAud(item.unitPrice.cents, locale)}
                        {item.discount.cents > 0
                          ? `  −${formatAud(item.discount.cents, locale)}`
                          : ""}
                      </Text>
                    </View>
                    <Text style={styles.cartLineTotal}>
                      {formatAud(item.actualAmount.cents, locale)}
                    </Text>
                  </View>
                  <View style={styles.lineControls}>
                    <ActionButton
                      accessibilityLabel={t("cart.decrease")}
                      disabled={!state.capabilities.cartEditing}
                      label="−"
                      onPress={() => {
                        void presenter.decreaseLine(item.lineId);
                      }}
                      testID={`sales-line-${item.lineId}-decrease`}
                      tone="quiet"
                    />
                    <View
                      accessibilityLabel={`${t("cart.quantity")}: ${item.quantity}`}
                      style={styles.quantityValue}
                    >
                      <Text style={styles.quantityText}>{item.quantity}</Text>
                    </View>
                    <ActionButton
                      accessibilityLabel={t("cart.increase")}
                      disabled={!state.capabilities.cartEditing}
                      label="+"
                      onPress={() => {
                        void presenter.increaseLine(item.lineId);
                      }}
                      testID={`sales-line-${item.lineId}-increase`}
                      tone="quiet"
                    />
                    <ActionButton
                      disabled={!state.capabilities.cartEditing}
                      label={t("cart.edit")}
                      onPress={() => openLineEditor(item.lineId)}
                      testID={`sales-line-${item.lineId}-edit`}
                      tone="quiet"
                    />
                    <ActionButton
                      disabled={!state.capabilities.cartEditing}
                      label={t("cart.discount")}
                      onPress={() => setDiscountLineId(item.lineId)}
                      testID={`sales-line-${item.lineId}-discount`}
                      tone="secondary"
                    />
                    <ActionButton
                      disabled={!state.capabilities.cartEditing}
                      label={t("cart.remove")}
                      onPress={() => {
                        void presenter.removeLine(item.lineId);
                      }}
                      testID={`sales-line-${item.lineId}-remove`}
                      tone="danger"
                    />
                  </View>
                </View>
              )}
            />
          )}
        </View>

        <View style={styles.summaryPane}>
          <Text style={styles.paneTitle}>{t("summary.title")}</Text>
          <View style={styles.summaryRows}>
            <SummaryRow
              amount={formatAud(state.cart.subtotal.cents, locale)}
              label={t("summary.subtotal")}
            />
            <SummaryRow
              amount={`−${formatAud(state.cart.discount.cents, locale)}`}
              label={t("summary.discount")}
              muted
            />
          </View>
          <View style={styles.totalRule} />
          <View style={styles.totalRow}>
            <Text style={styles.totalLabel}>{t("summary.total")}</Text>
            <Text style={styles.totalAmount}>
              {formatAud(state.cart.actualAmount.cents, locale)}
            </Text>
          </View>

          <View style={styles.summaryEditActions}>
            <ActionButton
              disabled={cartEmpty || !state.capabilities.cartEditing}
              label={t("summary.orderDiscount")}
              onPress={() => setOrderDiscountVisible(true)}
              style={styles.summaryEditAction}
              testID="sales-order-discount"
              tone="secondary"
            />
            <ActionButton
              disabled={cartEmpty || !state.capabilities.cartEditing}
              label={t("summary.clearCart")}
              onPress={() => setClearCartVisible(true)}
              style={styles.summaryEditAction}
              testID="sales-clear-cart"
              tone="danger"
            />
          </View>

          {connectivity === "offline" ? (
            <View
              accessibilityRole="alert"
              style={styles.offlineCard}
              testID="sales-offline-cash-only"
            >
              <Text style={styles.offlineTitle}>{t("summary.cashOnly")}</Text>
              <Text style={styles.offlineHint}>
                {t("summary.cashOnlyHint")}
              </Text>
            </View>
          ) : null}
          <View style={styles.summarySpacer} />

          <ActionButton
            disabled={cartEmpty || !state.capabilities.cashCheckout}
            label={
              cartEmpty
                ? t("summary.checkoutDisabled")
                : t("summary.checkoutCash")
            }
            onPress={() => presenter.openCash()}
            style={styles.checkoutButton}
            testID="sales-cash-checkout"
          />
          {onOpenPayment ? (
            <ActionButton
              disabled={
                cartEmpty ||
                connectivity !== "online" ||
                !state.capabilities.cartEditing
              }
              label={
                cartEmpty
                  ? t("summary.checkoutDisabled")
                  : t("summary.checkoutOnline")
              }
              onPress={onOpenPayment}
              style={styles.onlineCheckoutButton}
              testID="sales-online-checkout"
              tone="secondary"
            />
          ) : null}
        </View>
      </View>

      {state.errorCode ? (
        <ErrorBanner
          dismissLabel={t("common.dismiss")}
          message={t(errorCopyKey(state.errorCode))}
          onDismiss={() => presenter.dismissError()}
        />
      ) : null}

      <Modal
        animationType="fade"
        onRequestClose={closeCashInput}
        presentationStyle="overFullScreen"
        transparent
        visible={state.phase === "cash" || state.phase === "submitting-cash"}
      >
        <View style={styles.modalBackdrop}>
          <View
            accessibilityViewIsModal
            style={styles.cashModal}
            testID="sales-cash-modal"
          >
            <Text style={styles.modalTitle}>{t("cash.title")}</Text>
            <Text style={styles.cashDueLabel}>{t("cash.amountDue")}</Text>
            <Text style={styles.cashDueAmount}>
              {formatAud(getCashDueCents(state.cart), locale)}
            </Text>
            <Text style={styles.fieldLabel}>{t("cash.tendered")}</Text>
            <View style={styles.cashInputRow}>
              <Text style={styles.currencyPrefix}>$</Text>
              <TextInput
                accessibilityLabel={t("cash.tendered")}
                editable={state.phase === "cash"}
                keyboardType="decimal-pad"
                onBlur={notifyManualInputBlurred}
                onChangeText={(value) => presenter.setCashTenderedText(value)}
                onFocus={notifyManualInputFocused}
                placeholder={t("cash.tenderedPlaceholder")}
                placeholderTextColor="#7B8793"
                selectionColor={posColors.orange}
                showSoftInputOnFocus
                style={styles.cashInput}
                testID="sales-cash-tendered"
                value={state.cashTenderedText}
              />
              <ActionButton
                disabled={state.phase !== "cash"}
                label={t("cash.exact")}
                onPress={() => presenter.setExactCash()}
                testID="sales-cash-exact"
                tone="secondary"
              />
            </View>
            <View style={styles.changePreview}>
              <Text style={styles.changePreviewLabel}>{t("cash.change")}</Text>
              <Text style={styles.changePreviewAmount}>
                {formatAud(cashDraft.changeCents, locale)}
              </Text>
            </View>
            {state.errorCode === "cash-invalid" ||
            state.errorCode === "cash-insufficient" ||
            state.errorCode === "cash-failed" ? (
              <Text accessibilityRole="alert" style={styles.cashError}>
                {t(errorCopyKey(state.errorCode))}
              </Text>
            ) : null}
            <View style={styles.modalActions}>
              <ActionButton
                disabled={state.phase === "submitting-cash"}
                label={t("cash.cancel")}
                onPress={closeCashInput}
                style={styles.modalAction}
                testID="sales-cash-cancel"
                tone="secondary"
              />
              <ActionButton
                disabled={state.phase === "submitting-cash" || !cashDraft.valid}
                label={
                  state.phase === "submitting-cash"
                    ? t("cash.confirming")
                    : t("cash.confirm")
                }
                onPress={() => {
                  void presenter.submitCash();
                }}
                style={styles.modalAction}
                testID="sales-cash-confirm"
              />
            </View>
          </View>
        </View>
      </Modal>

      <Modal
        animationType="fade"
        onRequestClose={closeOpenItemInput}
        presentationStyle="overFullScreen"
        transparent
        visible={openItemVisible}
      >
        <View style={styles.modalBackdrop}>
          <View
            accessibilityViewIsModal
            style={styles.editorModal}
            testID="sales-open-item-modal"
          >
            <Text style={styles.modalTitle}>{t("openItem.title")}</Text>
            <Text style={styles.discountHint}>{t("openItem.hint")}</Text>
            <Text style={styles.fieldLabel}>{t("openItem.price")}</Text>
            <EditorInput
              accessibilityLabel={t("openItem.price")}
              onChangeText={setOpenItemPrice}
              onFocusChange={(focused) =>
                focused
                  ? notifyManualInputFocused()
                  : notifyManualInputBlurred()
              }
              testID="sales-open-item-price"
              value={openItemPrice}
            />
            <View style={styles.modalActions}>
              <ActionButton
                label={t("discount.cancel")}
                onPress={closeOpenItemInput}
                style={styles.modalAction}
                tone="secondary"
              />
              <ActionButton
                label={t("openItem.confirm")}
                onPress={() => {
                  void presenter
                    .addOpenItem(parseCashInput(openItemPrice) ?? 0)
                    .then((added) => {
                      if (added) closeOpenItemInput();
                    });
                }}
                style={styles.modalAction}
                testID="sales-open-item-confirm"
              />
            </View>
          </View>
        </View>
      </Modal>

      <Modal
        animationType="fade"
        onRequestClose={() => setDiscountLineId(null)}
        presentationStyle="overFullScreen"
        transparent
        visible={discountLineId !== null}
      >
        <View style={styles.modalBackdrop}>
          <View
            accessibilityViewIsModal
            style={styles.discountModal}
            testID="sales-discount-modal"
          >
            <Text style={styles.modalTitle}>{t("discount.title")}</Text>
            <Text style={styles.discountHint}>{t("discount.hint")}</Text>
            <View style={styles.discountGrid}>
              {QUICK_DISCOUNTS.map((basisPoints) => (
                <ActionButton
                  key={basisPoints}
                  label={
                    basisPoints === 0
                      ? t("discount.none")
                      : `${basisPoints / 100}%`
                  }
                  onPress={() => {
                    const lineId = discountLineId;
                    setDiscountLineId(null);
                    if (lineId) {
                      void presenter.applyLineDiscount(lineId, basisPoints);
                    }
                  }}
                  style={styles.discountButton}
                  testID={`sales-discount-${basisPoints}`}
                  tone={basisPoints === 0 ? "quiet" : "secondary"}
                />
              ))}
            </View>
            <View style={styles.modalActions}>
              <ActionButton
                label={t("discount.amount")}
                onPress={() => {
                  const lineId = discountLineId;
                  setDiscountLineId(null);
                  if (lineId) {
                    setLineEdit({
                      lineId,
                      mode: "discount-amount",
                      value: "",
                    });
                  }
                }}
                style={styles.modalAction}
                testID="sales-line-discount-amount"
                tone="secondary"
              />
              <ActionButton
                label={t("discount.percent")}
                onPress={() => {
                  const lineId = discountLineId;
                  setDiscountLineId(null);
                  if (lineId) {
                    setLineEdit({
                      lineId,
                      mode: "discount-percent",
                      value: "",
                    });
                  }
                }}
                style={styles.modalAction}
                testID="sales-line-discount-percent"
                tone="secondary"
              />
            </View>
            <ActionButton
              label={t("discount.cancel")}
              onPress={() => setDiscountLineId(null)}
              style={styles.modalCancelAction}
              testID="sales-discount-cancel"
              tone="quiet"
            />
          </View>
        </View>
      </Modal>

      <Modal
        animationType="fade"
        onRequestClose={closeLineEditInput}
        presentationStyle="overFullScreen"
        transparent
        visible={lineEdit !== null}
      >
        <View style={styles.modalBackdrop}>
          <View
            accessibilityViewIsModal
            style={styles.editorModal}
            testID="sales-line-edit-modal"
          >
            <Text style={styles.modalTitle}>{t("editLine.title")}</Text>
            <View style={styles.editModeGrid}>
              <ActionButton
                label={t("editLine.quantity")}
                onPress={() => selectLineEditMode("quantity")}
                style={styles.editModeButton}
                testID="sales-line-edit-quantity"
                tone={lineEdit?.mode === "quantity" ? "primary" : "secondary"}
              />
              <ActionButton
                label={t("editLine.price")}
                onPress={() => selectLineEditMode("price")}
                style={styles.editModeButton}
                testID="sales-line-edit-price"
                tone={lineEdit?.mode === "price" ? "primary" : "secondary"}
              />
              <ActionButton
                label={t("editLine.discountAmount")}
                onPress={() => selectLineEditMode("discount-amount")}
                style={styles.editModeButton}
                testID="sales-line-edit-discount-amount"
                tone={
                  lineEdit?.mode === "discount-amount" ? "primary" : "secondary"
                }
              />
              <ActionButton
                label={t("editLine.discountPercent")}
                onPress={() => selectLineEditMode("discount-percent")}
                style={styles.editModeButton}
                testID="sales-line-edit-discount-percent"
                tone={
                  lineEdit?.mode === "discount-percent"
                    ? "primary"
                    : "secondary"
                }
              />
            </View>
            <Text style={styles.fieldLabel}>{t("editLine.value")}</Text>
            <EditorInput
              accessibilityLabel={t("editLine.value")}
              keyboardType={
                lineEdit?.mode === "quantity" ? "number-pad" : "decimal-pad"
              }
              onChangeText={(value) =>
                setLineEdit((current) =>
                  current ? { ...current, value } : null,
                )
              }
              onFocusChange={(focused) =>
                focused
                  ? notifyManualInputFocused()
                  : notifyManualInputBlurred()
              }
              testID="sales-line-edit-value"
              value={lineEdit?.value ?? ""}
            />
            <View style={styles.modalActions}>
              <ActionButton
                label={t("discount.cancel")}
                onPress={closeLineEditInput}
                style={styles.modalAction}
                tone="secondary"
              />
              <ActionButton
                label={t("editLine.confirm")}
                onPress={submitLineEdit}
                style={styles.modalAction}
                testID="sales-line-edit-confirm"
              />
            </View>
          </View>
        </View>
      </Modal>

      <Modal
        animationType="fade"
        onRequestClose={() => setOrderDiscountVisible(false)}
        presentationStyle="overFullScreen"
        transparent
        visible={orderDiscountVisible}
      >
        <View style={styles.modalBackdrop}>
          <View
            accessibilityViewIsModal
            style={styles.discountModal}
            testID="sales-order-discount-modal"
          >
            <Text style={styles.modalTitle}>{t("orderDiscount.title")}</Text>
            <Text style={styles.discountHint}>{t("orderDiscount.hint")}</Text>
            <View style={styles.discountGrid}>
              {QUICK_ORDER_DISCOUNTS.map((basisPoints) => (
                <ActionButton
                  key={basisPoints}
                  label={`${basisPoints / 100}%`}
                  onPress={() => {
                    void presenter
                      .applyOrderQuickDiscount(basisPoints)
                      .then((applied) => {
                        if (applied) setOrderDiscountVisible(false);
                      });
                  }}
                  style={styles.discountButton}
                  testID={`sales-order-discount-${basisPoints}`}
                  tone="secondary"
                />
              ))}
            </View>
            <View style={styles.modalActions}>
              <ActionButton
                label={t("discount.amount")}
                onPress={() => {
                  setOrderDiscountVisible(false);
                  setOrderEdit({ mode: "amount", value: "" });
                }}
                style={styles.modalAction}
                testID="sales-order-discount-amount"
                tone="secondary"
              />
              <ActionButton
                label={t("discount.percent")}
                onPress={() => {
                  setOrderDiscountVisible(false);
                  setOrderEdit({ mode: "percent", value: "" });
                }}
                style={styles.modalAction}
                testID="sales-order-discount-percent"
                tone="secondary"
              />
            </View>
            <ActionButton
              label={t("discount.cancel")}
              onPress={() => setOrderDiscountVisible(false)}
              style={styles.modalCancelAction}
              tone="quiet"
            />
          </View>
        </View>
      </Modal>

      <Modal
        animationType="fade"
        onRequestClose={closeOrderEditInput}
        presentationStyle="overFullScreen"
        transparent
        visible={orderEdit !== null}
      >
        <View style={styles.modalBackdrop}>
          <View
            accessibilityViewIsModal
            style={styles.editorModal}
            testID="sales-order-edit-modal"
          >
            <Text style={styles.modalTitle}>
              {t("orderDiscount.editTitle")}
            </Text>
            <Text style={styles.fieldLabel}>
              {orderEdit?.mode === "percent"
                ? t("editLine.discountPercent")
                : t("editLine.discountAmount")}
            </Text>
            <EditorInput
              accessibilityLabel={t("editLine.value")}
              onChangeText={(value) =>
                setOrderEdit((current) =>
                  current ? { ...current, value } : null,
                )
              }
              onFocusChange={(focused) =>
                focused
                  ? notifyManualInputFocused()
                  : notifyManualInputBlurred()
              }
              testID="sales-order-edit-value"
              value={orderEdit?.value ?? ""}
            />
            <View style={styles.modalActions}>
              <ActionButton
                label={t("discount.cancel")}
                onPress={closeOrderEditInput}
                style={styles.modalAction}
                tone="secondary"
              />
              <ActionButton
                label={t("editLine.confirm")}
                onPress={submitOrderEdit}
                style={styles.modalAction}
                testID="sales-order-edit-confirm"
              />
            </View>
          </View>
        </View>
      </Modal>

      <Modal
        animationType="fade"
        onRequestClose={() => setClearCartVisible(false)}
        presentationStyle="overFullScreen"
        transparent
        visible={clearCartVisible}
      >
        <View style={styles.modalBackdrop}>
          <View
            accessibilityViewIsModal
            style={styles.editorModal}
            testID="sales-clear-cart-modal"
          >
            <Text style={styles.modalTitle}>{t("clearCart.title")}</Text>
            <Text style={styles.discountHint}>{t("clearCart.hint")}</Text>
            <View style={styles.modalActions}>
              <ActionButton
                label={t("clearCart.cancel")}
                onPress={() => setClearCartVisible(false)}
                style={styles.modalAction}
                testID="sales-clear-cart-cancel"
                tone="secondary"
              />
              <ActionButton
                label={t("clearCart.confirm")}
                onPress={() => {
                  void presenter.clearCart().then((cleared) => {
                    if (cleared) setClearCartVisible(false);
                  });
                }}
                style={styles.modalAction}
                testID="sales-clear-cart-confirm"
                tone="danger"
              />
            </View>
          </View>
        </View>
      </Modal>
    </SafeAreaView>
  );
}

function ProductSearchRow({
  disabled = false,
  item,
  locale,
  onAdd,
  t,
}: Readonly<{
  disabled?: boolean;
  item: SalesProductSearchItem;
  locale: SalesLocale;
  onAdd(): void;
  t(
    key: SalesCopyKey,
    values?: Readonly<Record<string, string | number>>,
  ): string;
}>) {
  return (
    <View style={styles.productRow}>
      <View style={styles.productIdentity}>
        <Text numberOfLines={2} style={styles.productName}>
          {item.displayName}
        </Text>
        <Text numberOfLines={1} style={styles.productCode}>
          {item.lookupCode}
        </Text>
      </View>
      <Text style={styles.productPrice}>
        {formatAud(item.unitPriceCents, locale)}
      </Text>
      <ActionButton
        disabled={disabled}
        label={t("catalog.add")}
        onPress={onAdd}
        testID={`sales-product-${item.productCode}-add`}
        tone="secondary"
      />
    </View>
  );
}

function EditorInput({
  accessibilityLabel,
  keyboardType = "decimal-pad",
  onChangeText,
  onFocusChange,
  testID,
  value,
}: Readonly<{
  accessibilityLabel: string;
  keyboardType?: "decimal-pad" | "number-pad";
  onChangeText(value: string): void;
  onFocusChange(focused: boolean): void;
  testID: string;
  value: string;
}>) {
  return (
    <TextInput
      accessibilityLabel={accessibilityLabel}
      autoFocus
      keyboardType={keyboardType}
      onBlur={() => onFocusChange(false)}
      onChangeText={onChangeText}
      onFocus={() => onFocusChange(true)}
      placeholder="0.00"
      placeholderTextColor="#7B8793"
      selectionColor={posColors.orange}
      selectTextOnFocus
      showSoftInputOnFocus
      style={styles.editorInput}
      testID={testID}
      value={value}
    />
  );
}

function SummaryRow({
  amount,
  label,
  muted = false,
}: Readonly<{ amount: string; label: string; muted?: boolean }>) {
  return (
    <View style={styles.summaryRow}>
      <Text style={[styles.summaryLabel, muted && styles.mutedText]}>
        {label}
      </Text>
      <Text style={[styles.summaryAmount, muted && styles.mutedText]}>
        {amount}
      </Text>
    </View>
  );
}

function ErrorBanner({
  dismissLabel,
  message,
  onDismiss,
}: Readonly<{
  dismissLabel: string;
  message: string;
  onDismiss(): void;
}>) {
  return (
    <View accessibilityRole="alert" style={styles.errorBanner}>
      <Text style={styles.errorBannerText}>{message}</Text>
      <Pressable
        accessibilityLabel={dismissLabel}
        accessibilityRole="button"
        hitSlop={8}
        onPress={onDismiss}
        style={styles.errorDismiss}
      >
        <Text style={styles.errorDismissText}>×</Text>
      </Pressable>
    </View>
  );
}

function ActionButton({
  accessibilityLabel,
  disabled = false,
  label,
  onPress,
  style,
  testID,
  tone = "primary",
}: ActionButtonProps) {
  return (
    <Pressable
      accessibilityLabel={accessibilityLabel ?? label}
      accessibilityRole="button"
      accessibilityState={{ disabled }}
      disabled={disabled}
      onPress={onPress}
      style={({ pressed }) => [
        styles.actionButton,
        actionToneStyles[tone],
        disabled && styles.actionButtonDisabled,
        pressed && !disabled && styles.actionButtonPressed,
        style,
      ]}
      testID={testID}
    >
      <Text
        style={[
          styles.actionButtonText,
          (tone === "quiet" || tone === "secondary") && styles.quietButtonText,
          tone === "danger" && styles.dangerButtonText,
          disabled && styles.actionButtonTextDisabled,
        ]}
      >
        {label}
      </Text>
    </Pressable>
  );
}

function errorCopyKey(errorCode: string): SalesCopyKey {
  switch (errorCode) {
    case "search-required":
      return "error.searchRequired";
    case "search-failed":
      return "error.searchFailed";
    case "product-add-failed":
      return "error.productAddFailed";
    case "authorization-denied":
      return "error.authorizationDenied";
    case "cart-update-failed":
      return "error.cartUpdateFailed";
    case "invalid-quantity":
      return "error.invalidQuantity";
    case "invalid-price":
      return "error.invalidPrice";
    case "invalid-discount":
      return "error.invalidDiscount";
    case "empty-cart":
      return "error.emptyCart";
    case "cash-invalid":
      return "error.cashInvalid";
    case "cash-insufficient":
      return "error.cashInsufficient";
    case "cash-failed":
      return "error.cashFailed";
    case "cart-clear-failed":
      return "error.cartClearFailed";
    case "hold-failed":
      return "error.holdFailed";
    case "lock-failed":
      return "error.lockFailed";
    case "new-transactions-disabled":
      return "error.newTransactionsDisabled";
    case "runtime-unavailable":
    default:
      return "error.runtimeUnavailable";
  }
}

function parsePositiveInteger(value: string): number | null {
  const normalized = value.trim();
  if (!/^\d+$/.test(normalized)) return null;
  const quantity = Number(normalized);
  return Number.isSafeInteger(quantity) && quantity > 0 ? quantity : null;
}

function parsePercentageBasisPoints(value: string): number | null {
  const normalized = value.trim().replace(/%$/, "").trim();
  const match = /^(\d{1,3})(?:\.(\d{1,2}))?$/.exec(normalized);
  if (!match) return null;
  const whole = Number(match[1]);
  const fraction = Number((match[2] ?? "").padEnd(2, "0"));
  if (whole > 100 || (whole === 100 && fraction > 0)) return null;
  return whole * 100 + fraction;
}

function formatEditorMoney(cents: number): string {
  return `${Math.floor(cents / 100)}.${String(cents % 100).padStart(2, "0")}`;
}

const actionToneStyles = StyleSheet.create({
  danger: {
    backgroundColor: posColors.redSoft,
    borderColor: posColors.red,
  },
  primary: {
    backgroundColor: posColors.orange,
    borderColor: posColors.orange,
  },
  quiet: {
    backgroundColor: "transparent",
    borderColor: posColors.border,
  },
  secondary: {
    backgroundColor: posColors.surface,
    borderColor: posColors.ink,
  },
});

const styles = StyleSheet.create({
  actionButton: {
    alignItems: "center",
    borderRadius: 4,
    borderWidth: 1,
    justifyContent: "center",
    minHeight: MIN_TOUCH_TARGET,
    minWidth: MIN_TOUCH_TARGET,
    paddingHorizontal: 14,
  },
  actionButtonDisabled: {
    backgroundColor: "#E4E1DA",
    borderColor: "#D0CCC2",
    opacity: 0.72,
  },
  actionButtonPressed: {
    opacity: 0.72,
    transform: [{ scale: 0.99 }],
  },
  actionButtonText: {
    color: "#FFFFFF",
    fontSize: 14,
    fontWeight: "800",
  },
  actionButtonTextDisabled: {
    color: "#7C8287",
  },
  brand: {
    color: posColors.ink,
    fontSize: 19,
    fontWeight: "900",
    letterSpacing: 0.4,
  },
  cartCount: {
    color: posColors.mutedInk,
    fontSize: 12,
    fontVariant: ["tabular-nums"],
    fontWeight: "700",
  },
  cartHeader: {
    alignItems: "center",
    flexDirection: "row",
    justifyContent: "space-between",
  },
  cartLine: {
    alignItems: "stretch",
    backgroundColor: posColors.surface,
    borderColor: posColors.border,
    borderRadius: 4,
    borderWidth: 1,
    gap: 8,
    minHeight: 118,
    paddingHorizontal: 14,
    paddingVertical: 10,
  },
  cartLineCode: {
    color: posColors.mutedInk,
    fontSize: 11,
    fontVariant: ["tabular-nums"],
  },
  cartLineIdentity: {
    flex: 1,
    minWidth: 120,
  },
  cartLineName: {
    color: posColors.ink,
    fontSize: 15,
    fontWeight: "800",
    lineHeight: 19,
  },
  cartLineTotal: {
    color: posColors.ink,
    fontSize: 17,
    fontVariant: ["tabular-nums"],
    fontWeight: "900",
    minWidth: 80,
    textAlign: "right",
  },
  cartLineTop: {
    alignItems: "flex-start",
    flexDirection: "row",
    gap: 12,
  },
  cartLineUnitPrice: {
    color: posColors.mutedInk,
    fontSize: 12,
    fontVariant: ["tabular-nums"],
    marginTop: 4,
  },
  cartList: {
    gap: 8,
    paddingBottom: 12,
  },
  cartPane: {
    backgroundColor: "#F8F6F1",
    borderColor: posColors.border,
    borderRadius: 5,
    borderWidth: 1,
    flex: 1,
    gap: 12,
    minWidth: 360,
    padding: 14,
  },
  catalogPane: {
    backgroundColor: posColors.surface,
    borderColor: posColors.border,
    borderRadius: 5,
    borderWidth: 1,
    gap: 10,
    padding: 14,
    width: 252,
  },
  cashDueAmount: {
    color: posColors.ink,
    fontSize: 42,
    fontVariant: ["tabular-nums"],
    fontWeight: "900",
    letterSpacing: -1,
    marginBottom: 18,
  },
  cashDueLabel: {
    color: posColors.mutedInk,
    fontSize: 12,
    fontWeight: "800",
    marginTop: 18,
    textTransform: "uppercase",
  },
  cashError: {
    color: posColors.red,
    fontSize: 13,
    fontWeight: "700",
    marginTop: 12,
  },
  cashInput: {
    backgroundColor: "#FAFAF8",
    borderColor: posColors.ink,
    borderRadius: 4,
    borderWidth: 1,
    color: posColors.ink,
    flex: 1,
    fontSize: 24,
    fontVariant: ["tabular-nums"],
    fontWeight: "800",
    height: 52,
    paddingHorizontal: 38,
  },
  cashInputRow: {
    alignItems: "center",
    flexDirection: "row",
    gap: 10,
    position: "relative",
  },
  cashModal: {
    backgroundColor: posColors.surface,
    borderColor: posColors.border,
    borderRadius: 6,
    borderWidth: 1,
    maxWidth: 560,
    padding: 28,
    width: "72%",
  },
  changeAmount: {
    color: posColors.green,
    fontSize: 52,
    fontVariant: ["tabular-nums"],
    fontWeight: "900",
    letterSpacing: -1.5,
  },
  changeCard: {
    alignItems: "center",
    backgroundColor: posColors.greenSoft,
    borderColor: posColors.green,
    borderRadius: 6,
    borderWidth: 1,
    marginTop: 28,
    minWidth: 360,
    paddingHorizontal: 36,
    paddingVertical: 20,
  },
  changeLabel: {
    color: posColors.green,
    fontSize: 13,
    fontWeight: "800",
    textTransform: "uppercase",
  },
  changePreview: {
    alignItems: "center",
    backgroundColor: posColors.greenSoft,
    borderRadius: 4,
    flexDirection: "row",
    justifyContent: "space-between",
    marginTop: 18,
    minHeight: 64,
    paddingHorizontal: 18,
  },
  changePreviewAmount: {
    color: posColors.green,
    fontSize: 26,
    fontVariant: ["tabular-nums"],
    fontWeight: "900",
  },
  changePreviewLabel: {
    color: posColors.green,
    fontSize: 14,
    fontWeight: "800",
  },
  checkoutButton: {
    minHeight: 64,
    width: "100%",
  },
  onlineCheckoutButton: {
    marginTop: 10,
    minHeight: 64,
    width: "100%",
  },
  currencyPrefix: {
    color: posColors.mutedInk,
    fontSize: 22,
    fontWeight: "800",
    left: 14,
    position: "absolute",
    top: 12,
    zIndex: 1,
  },
  dangerButtonText: {
    color: posColors.red,
  },
  discountButton: {
    flexBasis: "29%",
    flexGrow: 1,
  },
  discountGrid: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 10,
    marginBottom: 18,
    marginTop: 18,
  },
  discountHint: {
    color: posColors.mutedInk,
    fontSize: 14,
    lineHeight: 20,
    marginTop: 8,
  },
  discountModal: {
    backgroundColor: posColors.surface,
    borderColor: posColors.border,
    borderRadius: 6,
    borderWidth: 1,
    maxWidth: 520,
    padding: 26,
    width: "65%",
  },
  editModeButton: {
    flexBasis: "47%",
    flexGrow: 1,
  },
  editModeGrid: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 10,
    marginBottom: 18,
    marginTop: 18,
  },
  editorInput: {
    backgroundColor: "#FAFAF8",
    borderColor: posColors.ink,
    borderRadius: 4,
    borderWidth: 1,
    color: posColors.ink,
    fontSize: 24,
    fontVariant: ["tabular-nums"],
    fontWeight: "800",
    height: 54,
    paddingHorizontal: 14,
  },
  editorModal: {
    backgroundColor: posColors.surface,
    borderColor: posColors.border,
    borderRadius: 6,
    borderWidth: 1,
    maxWidth: 560,
    padding: 26,
    width: "65%",
  },
  emptyCart: {
    alignItems: "center",
    flex: 1,
    justifyContent: "center",
    padding: 24,
  },
  emptyCartHint: {
    color: posColors.mutedInk,
    fontSize: 14,
    lineHeight: 20,
    marginTop: 6,
    maxWidth: 280,
    textAlign: "center",
  },
  emptyCartIcon: {
    color: "#A7AFB5",
    fontSize: 38,
    fontWeight: "300",
  },
  emptyCartTitle: {
    color: posColors.ink,
    fontSize: 18,
    fontWeight: "800",
    marginTop: 8,
  },
  errorBanner: {
    alignItems: "center",
    backgroundColor: posColors.redSoft,
    borderColor: posColors.red,
    borderTopWidth: 1,
    flexDirection: "row",
    minHeight: 48,
    paddingLeft: 18,
  },
  errorBannerText: {
    color: posColors.red,
    flex: 1,
    fontSize: 13,
    fontWeight: "700",
  },
  errorDismiss: {
    alignItems: "center",
    height: MIN_TOUCH_TARGET,
    justifyContent: "center",
    width: MIN_TOUCH_TARGET,
  },
  errorDismissText: {
    color: posColors.red,
    fontSize: 24,
    fontWeight: "700",
  },
  fieldLabel: {
    color: posColors.ink,
    fontSize: 13,
    fontWeight: "800",
    marginBottom: 7,
  },
  header: {
    alignItems: "center",
    backgroundColor: posColors.surface,
    borderBottomColor: posColors.border,
    borderBottomWidth: 1,
    flexDirection: "row",
    justifyContent: "space-between",
    minHeight: 60,
    paddingHorizontal: 20,
    paddingVertical: 8,
  },
  headerActions: {
    flex: 1,
    marginLeft: 20,
    minWidth: 0,
  },
  lineControls: {
    alignItems: "center",
    flexDirection: "row",
    gap: 5,
    justifyContent: "flex-end",
  },
  lockIcon: {
    alignItems: "center",
    backgroundColor: posColors.redSoft,
    borderRadius: 32,
    height: 64,
    justifyContent: "center",
    width: 64,
  },
  lockIconText: {
    color: posColors.red,
    fontSize: 24,
  },
  lockedHint: {
    color: posColors.mutedInk,
    fontSize: 16,
    lineHeight: 24,
    maxWidth: 520,
    textAlign: "center",
  },
  lockedNotice: {
    backgroundColor: posColors.surface,
    borderColor: posColors.border,
    borderRadius: 4,
    borderWidth: 1,
    marginTop: 24,
    paddingHorizontal: 20,
    paddingVertical: 14,
  },
  lockedNoticeText: {
    color: posColors.mutedInk,
    fontSize: 14,
  },
  lockedScreen: {
    alignItems: "center",
    backgroundColor: posColors.canvas,
    flex: 1,
    justifyContent: "center",
    padding: 32,
  },
  lockedTitle: {
    color: posColors.ink,
    fontSize: 30,
    fontWeight: "900",
    marginBottom: 10,
    marginTop: 20,
  },
  modalAction: {
    flex: 1,
  },
  modalCancelAction: {
    marginTop: 12,
  },
  modalActions: {
    flexDirection: "row",
    gap: 12,
    marginTop: 22,
  },
  modalBackdrop: {
    alignItems: "center",
    backgroundColor: "rgba(16, 37, 58, 0.55)",
    flex: 1,
    justifyContent: "center",
    padding: 28,
  },
  modalTitle: {
    color: posColors.ink,
    fontSize: 24,
    fontWeight: "900",
  },
  mutedText: {
    color: posColors.mutedInk,
  },
  offlineCard: {
    backgroundColor: posColors.orangeSoft,
    borderColor: posColors.orange,
    borderRadius: 4,
    borderWidth: 1,
    marginTop: 22,
    padding: 14,
  },
  offlineHint: {
    color: "#744024",
    fontSize: 12,
    lineHeight: 17,
    marginTop: 4,
  },
  offlineTitle: {
    color: "#743A20",
    fontSize: 13,
    fontWeight: "900",
  },
  paneTitle: {
    color: posColors.ink,
    fontSize: 16,
    fontWeight: "900",
  },
  productCode: {
    color: posColors.mutedInk,
    fontSize: 11,
    fontVariant: ["tabular-nums"],
    marginTop: 3,
  },
  productIdentity: {
    flex: 1,
  },
  productName: {
    color: posColors.ink,
    fontSize: 13,
    fontWeight: "800",
    lineHeight: 17,
  },
  productPrice: {
    color: posColors.ink,
    fontSize: 14,
    fontVariant: ["tabular-nums"],
    fontWeight: "800",
  },
  productRow: {
    alignItems: "center",
    borderBottomColor: posColors.border,
    borderBottomWidth: 1,
    flexDirection: "row",
    gap: 8,
    minHeight: 66,
    paddingVertical: 8,
  },
  quantityText: {
    color: posColors.ink,
    fontSize: 15,
    fontVariant: ["tabular-nums"],
    fontWeight: "900",
  },
  quantityValue: {
    alignItems: "center",
    height: MIN_TOUCH_TARGET,
    justifyContent: "center",
    minWidth: MIN_TOUCH_TARGET,
  },
  quietButtonText: {
    color: posColors.ink,
  },
  runtimeBanner: {
    backgroundColor: posColors.blueSoft,
    borderBottomColor: posColors.blue,
    borderBottomWidth: 1,
    minHeight: 38,
    paddingHorizontal: 20,
    paddingVertical: 9,
  },
  runtimeBannerText: {
    color: posColors.blue,
    fontSize: 12,
    fontWeight: "800",
  },
  updateGateBanner: {
    alignItems: "center",
    backgroundColor: "#FFF4D8",
    borderBottomColor: posColors.orange,
    borderBottomWidth: 1,
    flexDirection: "row",
    gap: 16,
    justifyContent: "space-between",
    minHeight: 58,
    paddingHorizontal: 20,
    paddingVertical: 8,
  },
  updateGateCopy: {
    flex: 1,
    gap: 2,
  },
  updateGateHint: {
    color: posColors.mutedInk,
    fontSize: 12,
    lineHeight: 17,
  },
  updateGateTitle: {
    color: posColors.ink,
    fontSize: 14,
    fontWeight: "900",
  },
  safeArea: {
    backgroundColor: posColors.canvas,
    flex: 1,
  },
  scanHint: {
    color: posColors.mutedInk,
    fontSize: 11,
    lineHeight: 16,
  },
  searchAction: {
    flex: 1,
    paddingHorizontal: 7,
  },
  searchActions: {
    flexDirection: "row",
    gap: 8,
  },
  searchInput: {
    backgroundColor: "#FAFAF8",
    borderColor: posColors.ink,
    borderRadius: 4,
    borderWidth: 1,
    color: posColors.ink,
    flex: 1,
    fontSize: 15,
    height: 48,
    paddingHorizontal: 12,
  },
  searchInputRow: {
    alignItems: "stretch",
    flexDirection: "row",
    gap: 8,
  },
  searchKeyboardAction: {
    paddingHorizontal: 10,
  },
  searchMessage: {
    color: posColors.mutedInk,
    fontSize: 13,
    paddingVertical: 16,
    textAlign: "center",
  },
  searchResults: {
    paddingBottom: 12,
  },
  successAction: {
    marginTop: 28,
    minWidth: 240,
  },
  successEyebrow: {
    color: posColors.green,
    fontSize: 12,
    fontWeight: "900",
    letterSpacing: 1.5,
    marginTop: 18,
  },
  successMark: {
    alignItems: "center",
    backgroundColor: posColors.green,
    borderRadius: 38,
    height: 76,
    justifyContent: "center",
    width: 76,
  },
  successMarkText: {
    color: "#FFFFFF",
    fontSize: 38,
    fontWeight: "900",
  },
  successOrder: {
    color: posColors.mutedInk,
    fontSize: 13,
    fontVariant: ["tabular-nums"],
    marginTop: 8,
  },
  successScreen: {
    alignItems: "center",
    backgroundColor: posColors.canvas,
    flex: 1,
    justifyContent: "center",
    padding: 30,
  },
  successSync: {
    color: posColors.mutedInk,
    fontSize: 14,
    lineHeight: 20,
    marginTop: 16,
    maxWidth: 480,
    textAlign: "center",
  },
  successTitle: {
    color: posColors.ink,
    fontSize: 31,
    fontWeight: "900",
    marginTop: 8,
  },
  successWarning: {
    backgroundColor: "#FFF4D8",
    borderColor: "#DFA321",
    borderRadius: 6,
    borderWidth: 1,
    color: posColors.ink,
    fontSize: 14,
    lineHeight: 20,
    marginTop: 14,
    maxWidth: 560,
    paddingHorizontal: 14,
    paddingVertical: 10,
    textAlign: "center",
  },
  summaryAmount: {
    color: posColors.ink,
    fontSize: 14,
    fontVariant: ["tabular-nums"],
    fontWeight: "800",
  },
  summaryLabel: {
    color: posColors.ink,
    fontSize: 14,
  },
  summaryPane: {
    backgroundColor: posColors.surface,
    borderColor: posColors.border,
    borderRadius: 5,
    borderWidth: 1,
    padding: 16,
    width: 274,
  },
  summaryEditAction: {
    flex: 1,
    paddingHorizontal: 8,
  },
  summaryEditActions: {
    flexDirection: "row",
    gap: 8,
    marginTop: 16,
  },
  summaryRow: {
    alignItems: "center",
    flexDirection: "row",
    justifyContent: "space-between",
    minHeight: 36,
  },
  summaryRows: {
    marginTop: 15,
  },
  summarySpacer: {
    flex: 1,
  },
  totalAmount: {
    color: posColors.ink,
    fontSize: 31,
    fontVariant: ["tabular-nums"],
    fontWeight: "900",
    letterSpacing: -0.8,
  },
  totalLabel: {
    color: posColors.ink,
    fontSize: 14,
    fontWeight: "900",
    textTransform: "uppercase",
  },
  totalRow: {
    alignItems: "flex-end",
    flexDirection: "row",
    justifyContent: "space-between",
  },
  totalRule: {
    backgroundColor: posColors.ink,
    height: 2,
    marginBottom: 14,
    marginTop: 10,
  },
  workspace: {
    color: posColors.mutedInk,
    fontSize: 11,
    fontWeight: "700",
    marginTop: 1,
  },
  workspaceRow: {
    flex: 1,
    flexDirection: "row",
    gap: 10,
    padding: 10,
  },
});
