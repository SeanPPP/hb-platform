import { useEffect, useRef, useState, useSyncExternalStore } from "react";
import {
  FlatList,
  Image,
  Modal,
  Pressable,
  ScrollView,
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
  applySalesNumberKey,
  SalesNumberKeypad,
  type SalesNumberKey,
  type SalesNumberKeypadMode,
} from "./sales-number-keypad";
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

import type { CartSnapshot } from "@/core/contracts";
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
  replaceOnNextDigit: boolean;
  value: string;
}>;

type OrderEditState = Readonly<{
  mode: "amount" | "percent";
  value: string;
}>;

export type SalesUtilityActionResult = Readonly<{
  kind:
    "completed" | "not-found" | "denied" | "unavailable" | "failed" | "unknown";
}>;

type SalesUtilityAction = "reprint" | "drawer";

export type SalesScreenProps = Readonly<{
  presenter: SalesPresenter;
  locale?: SalesLocale;
  newTransactionGate?: NewTransactionGate;
  resolveCartProductImage?: (input: {
    productCode: string;
    lookupCode: string;
  }) => Promise<string | null>;
  onOpenAttendanceAudit?: () => void;
  onOpenCashDrawer?: () => Promise<SalesUtilityActionResult>;
  onOpenCatalogMaintenance?: () => void;
  onOpenDailyClose?: () => void;
  onOpenHeldOrders?: () => void;
  onOpenInstallments?: () => void;
  onOpenPayment?: (cart: CartSnapshot) => void;
  onOpenRequiredUpdate?: () => void;
  onReprintReceipt?: () => Promise<SalesUtilityActionResult>;
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

type NumericValueDisplayProps = Readonly<{
  accessibilityLabel: string;
  currencyPrefix?: string;
  placeholder: string;
  testID: string;
  value: string;
}>;

export function SalesScreen({
  presenter,
  locale: localeOverride,
  newTransactionGate,
  resolveCartProductImage,
  onOpenAttendanceAudit,
  onOpenCashDrawer,
  onOpenCatalogMaintenance,
  onOpenDailyClose,
  onOpenHeldOrders,
  onOpenInstallments,
  onOpenPayment,
  onOpenRequiredUpdate,
  onReprintReceipt,
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
  const keypadLabels = {
    backspace: t("keypad.backspace"),
    clear: t("keypad.clear"),
    decimal: t("keypad.decimal"),
    quick50: t("keypad.quick50"),
    quick99: t("keypad.quick99"),
  };
  const [discountLineId, setDiscountLineId] = useState<string | null>(null);
  const [openItemVisible, setOpenItemVisible] = useState(false);
  const [openItemPrice, setOpenItemPrice] = useState("");
  const [lineEdit, setLineEdit] = useState<LineEditState | null>(null);
  const [orderDiscountVisible, setOrderDiscountVisible] = useState(false);
  const [orderEdit, setOrderEdit] = useState<OrderEditState | null>(null);
  const [clearCartVisible, setClearCartVisible] = useState(false);
  const [searchDrawerVisible, setSearchDrawerVisible] = useState(false);
  const [searchDrawerQuery, setSearchDrawerQuery] = useState("");
  const [searchResultsQuery, setSearchResultsQuery] = useState<string | null>(
    null,
  );
  const [cartProductImages, setCartProductImages] = useState<
    Readonly<Record<string, string | null>>
  >({});
  const [utilityActionPending, setUtilityActionPending] =
    useState<SalesUtilityAction | null>(null);
  const [utilityActionResult, setUtilityActionResult] = useState<Readonly<{
    action: SalesUtilityAction;
    result: SalesUtilityActionResult;
  }> | null>(null);
  const [searchSoftInputOnFocus, setSearchSoftInputOnFocus] = useState(false);
  const searchInputRef = useRef<TextInput>(null);
  const searchKeyboardRequestPhaseRef = useRef<
    "idle" | "awaiting-blur" | "enabling" | "awaiting-focus"
  >("idle");
  const searchKeyboardRequestGenerationRef = useRef(0);
  const searchKeyboardTimerRef = useRef<ReturnType<typeof setTimeout> | null>(
    null,
  );
  const manualInputFocusChangeRef = useRef(onManualInputFocusChange);
  const manualInputActiveRef = useRef(false);
  const numericInputModalActiveRef = useRef(false);
  const manualInputBlurTimerRef = useRef<ReturnType<typeof setTimeout> | null>(
    null,
  );
  const searchRequestGenerationRef = useRef(0);
  const productImageRequestsRef = useRef(new Map<string, symbol>());
  const currentCartProductImageKeysRef = useRef<ReadonlySet<string>>(new Set());
  const productImageResolverGenerationRef = useRef(0);
  const beginNumericInputRef = useRef<() => void>(() => undefined);
  manualInputFocusChangeRef.current = onManualInputFocusChange;
  currentCartProductImageKeysRef.current = new Set(
    state.cart.lines.map(cartProductImageKey),
  );
  const successDrawerWarning = state.success
    ? drawerWarningCopyKey(state.success.drawerDisposition)
    : null;
  const cartEmpty = state.cart.lines.length === 0;
  const searchResultsAreCurrent =
    state.searchStatus === "ready" &&
    searchResultsQuery !== null &&
    searchResultsQuery === searchDrawerQuery &&
    state.query.trim() === searchDrawerQuery;
  const visibleSearchResults = searchResultsAreCurrent
    ? state.searchResults
    : [];
  const newTransactionBlocked =
    cartEmpty && newTransactionGate?.canStartNewTransaction === false;
  const checkoutVerifying = state.phase === "verifying-checkout";
  const transactionActionsDisabled = state.phase !== "selling";
  const catalogActionsDisabled =
    !state.capabilities.catalog ||
    newTransactionBlocked ||
    transactionActionsDisabled;
  const manualInputTreeUnavailable =
    state.phase === "success" ||
    state.phase === "locked" ||
    checkoutVerifying ||
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

  const scheduleSearchKeyboardEnable = (
    requestGeneration: number,
  ): void => {
    clearSearchKeyboardTimer();
    searchKeyboardRequestPhaseRef.current = "enabling";
    searchKeyboardTimerRef.current = setTimeout(() => {
      searchKeyboardTimerRef.current = null;
      if (
        requestGeneration !== searchKeyboardRequestGenerationRef.current ||
        searchKeyboardRequestPhaseRef.current !== "enabling"
      ) {
        return;
      }
      setSearchSoftInputOnFocus(true);
    }, 0);
  };

  const requestSearchKeyboard = (): void => {
    const previousPhase = searchKeyboardRequestPhaseRef.current;
    clearSearchKeyboardTimer();
    const requestGeneration =
      searchKeyboardRequestGenerationRef.current + 1;
    searchKeyboardRequestGenerationRef.current = requestGeneration;
    const searchInput = searchInputRef.current;
    searchInput?.setNativeProps({ showSoftInputOnFocus: false });
    setSearchSoftInputOnFocus(false);
    if (searchInput?.isFocused()) {
      searchKeyboardRequestPhaseRef.current = "awaiting-blur";
      // 重复点击复用唯一在途 blur，避免多个迟到事件影响下一次焦点周期。
      if (previousPhase !== "awaiting-blur") {
        searchInput.blur();
      }
      return;
    }
    scheduleSearchKeyboardEnable(requestGeneration);
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
    searchKeyboardRequestGenerationRef.current += 1;
    searchKeyboardRequestPhaseRef.current = "idle";
    searchInputRef.current?.setNativeProps({ showSoftInputOnFocus: false });
    setSearchSoftInputOnFocus(false);
  };

  const beginNumericInput = (): void => {
    numericInputModalActiveRef.current = true;
    resetSearchInputToHidMode();
    searchInputRef.current?.blur();
    notifyManualInputFocused();
  };
  beginNumericInputRef.current = beginNumericInput;

  const handleSearchInputBlur = (): void => {
    if (searchKeyboardRequestPhaseRef.current === "awaiting-blur") {
      const requestGeneration = searchKeyboardRequestGenerationRef.current;
      searchInputRef.current?.setNativeProps({
        showSoftInputOnFocus: false,
      });
      setSearchSoftInputOnFocus(false);
      scheduleSearchKeyboardEnable(requestGeneration);
      return;
    }
    if (
      searchKeyboardRequestPhaseRef.current === "enabling" ||
      searchKeyboardRequestPhaseRef.current === "awaiting-focus"
    ) {
      return;
    }
    resetSearchInputToHidMode();
    if (!numericInputModalActiveRef.current) {
      notifyManualInputBlurred();
    }
  };

  const handleSearchInputFocus = (): void => {
    if (searchKeyboardRequestPhaseRef.current === "awaiting-focus") {
      searchKeyboardRequestPhaseRef.current = "idle";
    }
    notifyManualInputFocused();
  };

  const closeCashInput = (): void => {
    if (!presenter.closeCash()) return;
    numericInputModalActiveRef.current = false;
    notifyManualInputBlurred();
  };

  const closeOpenItemInput = (): void => {
    numericInputModalActiveRef.current = false;
    notifyManualInputBlurred();
    setOpenItemVisible(false);
  };

  const closeLineEditInput = (): void => {
    numericInputModalActiveRef.current = false;
    notifyManualInputBlurred();
    setLineEdit(null);
  };

  const closeOrderEditInput = (): void => {
    numericInputModalActiveRef.current = false;
    notifyManualInputBlurred();
    setOrderEdit(null);
  };

  const handleCashKey = (key: SalesNumberKey): void => {
    presenter.setCashTenderedText(
      applySalesNumberKey(state.cashTenderedText, key, { mode: "decimal" }),
    );
  };

  const handleOpenItemKey = (key: SalesNumberKey): void => {
    setOpenItemPrice((current) =>
      applySalesNumberKey(current, key, { mode: "decimal" }),
    );
  };

  const handleLineEditKey = (key: SalesNumberKey): void => {
    setLineEdit((current) => {
      if (!current) return null;
      const mode: SalesNumberKeypadMode =
        current.mode === "quantity" ? "integer" : "decimal";
      const nextValue = applySalesNumberKey(current.value, key, {
        mode,
        replaceOnNextDigit: current.replaceOnNextDigit,
      });
      // 首次数字用于替换预填值；一旦用户执行实际编辑，后续数字就按位追加。
      const keepReplaceSelection =
        key === "decimal" && nextValue === current.value;
      return {
        ...current,
        replaceOnNextDigit: current.replaceOnNextDigit && keepReplaceSelection,
        value: nextValue,
      };
    });
  };

  const handleOrderEditKey = (key: SalesNumberKey): void => {
    setOrderEdit((current) =>
      current
        ? {
            ...current,
            value: applySalesNumberKey(current.value, key, {
              mode: "decimal",
            }),
          }
        : null,
    );
  };

  useEffect(
    () => () => {
      clearManualInputBlurTimer();
      clearSearchKeyboardTimer();
      searchKeyboardRequestGenerationRef.current += 1;
      searchKeyboardRequestPhaseRef.current = "idle";
      searchInputRef.current?.setNativeProps({
        showSoftInputOnFocus: false,
      });
      searchRequestGenerationRef.current += 1;
      numericInputModalActiveRef.current = false;
      if (!manualInputActiveRef.current) return;
      manualInputActiveRef.current = false;
      manualInputFocusChangeRef.current?.(false);
    },
    [],
  );

  useEffect(() => {
    if (
      !searchSoftInputOnFocus ||
      searchKeyboardRequestPhaseRef.current !== "enabling"
    ) {
      return;
    }
    const requestGeneration = searchKeyboardRequestGenerationRef.current;
    clearSearchKeyboardTimer();
    searchKeyboardTimerRef.current = setTimeout(() => {
      searchKeyboardTimerRef.current = null;
      if (
        requestGeneration !== searchKeyboardRequestGenerationRef.current ||
        searchKeyboardRequestPhaseRef.current !== "enabling"
      ) {
        return;
      }
      searchKeyboardRequestPhaseRef.current = "awaiting-focus";
      searchInputRef.current?.setNativeProps({ showSoftInputOnFocus: true });
      searchInputRef.current?.focus();
    }, 0);
  }, [searchSoftInputOnFocus]);

  useEffect(() => {
    if (!catalogActionsDisabled && !manualInputTreeUnavailable) return;
    if (searchKeyboardTimerRef.current !== null) {
      clearTimeout(searchKeyboardTimerRef.current);
      searchKeyboardTimerRef.current = null;
    }
    searchKeyboardRequestGenerationRef.current += 1;
    searchKeyboardRequestPhaseRef.current = "idle";
    searchInputRef.current?.setNativeProps({
      showSoftInputOnFocus: false,
    });
    setSearchSoftInputOnFocus(false);
    searchInputRef.current?.blur();
    numericInputModalActiveRef.current = false;
    notifyManualInputBlurred();
  }, [catalogActionsDisabled, manualInputTreeUnavailable]);

  useEffect(() => {
    if (
      (state.phase !== "cash" && state.phase !== "submitting-cash") ||
      numericInputModalActiveRef.current
    ) {
      return;
    }
    // 现金入口已迁入统一支付页；若恢复流程仍进入旧现金态，继续安全接管手动输入。
    beginNumericInputRef.current();
  }, [state.phase]);

  useEffect(() => {
    productImageResolverGenerationRef.current += 1;
    productImageRequestsRef.current.clear();
    setCartProductImages({});
    return () => {
      productImageResolverGenerationRef.current += 1;
    };
  }, [resolveCartProductImage]);

  useEffect(() => {
    const currentKeys = new Set(state.cart.lines.map(cartProductImageKey));
    for (const key of productImageRequestsRef.current.keys()) {
      if (!currentKeys.has(key)) {
        productImageRequestsRef.current.delete(key);
      }
    }
    setCartProductImages((current) => {
      const retainedEntries = Object.entries(current).filter(([key]) =>
        currentKeys.has(key),
      );
      return retainedEntries.length === Object.keys(current).length
        ? current
        : Object.fromEntries(retainedEntries);
    });
  }, [state.cart.lines]);

  useEffect(() => {
    if (!resolveCartProductImage) return;
    const generation = productImageResolverGenerationRef.current;
    for (const line of state.cart.lines) {
      const imageKey = cartProductImageKey(line);
      if (
        Object.prototype.hasOwnProperty.call(cartProductImages, imageKey) ||
        productImageRequestsRef.current.has(imageKey)
      ) {
        continue;
      }
      const requestToken = Symbol(imageKey);
      productImageRequestsRef.current.set(imageKey, requestToken);
      void resolveCartProductImage({
        productCode: line.productCode,
        lookupCode: line.lookupCode,
      })
        .then((imageUri) => {
          if (
            productImageResolverGenerationRef.current !== generation ||
            productImageRequestsRef.current.get(imageKey) !== requestToken ||
            !currentCartProductImageKeysRef.current.has(imageKey)
          ) {
            return;
          }
          const normalizedUri = imageUri?.trim() || null;
          setCartProductImages((current) => ({
            ...current,
            [imageKey]: normalizedUri,
          }));
        })
        .catch(() => {
          if (
            productImageResolverGenerationRef.current !== generation ||
            productImageRequestsRef.current.get(imageKey) !== requestToken ||
            !currentCartProductImageKeysRef.current.has(imageKey)
          ) {
            return;
          }
          setCartProductImages((current) => ({
            ...current,
            [imageKey]: null,
          }));
        })
        .finally(() => {
          if (
            productImageResolverGenerationRef.current === generation &&
            productImageRequestsRef.current.get(imageKey) === requestToken
          ) {
            productImageRequestsRef.current.delete(imageKey);
          }
        });
    }
  }, [cartProductImages, resolveCartProductImage, state.cart.lines]);

  const openProductSearchDrawer = (): void => {
    const query = state.query.trim();
    const generation = ++searchRequestGenerationRef.current;
    setSearchDrawerQuery(query);
    setSearchResultsQuery(null);
    setSearchDrawerVisible(true);
    void presenter.searchProducts().then((searched) => {
      if (generation === searchRequestGenerationRef.current && searched) {
        setSearchResultsQuery(query);
      }
    });
  };

  const runUtilityAction = (
    action: SalesUtilityAction,
    operation: (() => Promise<SalesUtilityActionResult>) | undefined,
  ): void => {
    if (!operation || utilityActionPending) return;
    setUtilityActionPending(action);
    setUtilityActionResult(null);
    void operation()
      .then((result) => {
        setUtilityActionResult({ action, result });
      })
      .catch(() => {
        setUtilityActionResult({ action, result: { kind: "failed" } });
      })
      .finally(() => {
        setUtilityActionPending(null);
      });
  };

  const openLineEditor = (lineId: string): void => {
    const line = state.cart.lines.find(
      (candidate) => candidate.lineId === lineId,
    );
    if (!line) return;
    setLineEdit({
      lineId,
      mode: "quantity",
      replaceOnNextDigit: true,
      value: line.quantity,
    });
    beginNumericInput();
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
        replaceOnNextDigit: true,
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
        {showStatusStrip ? (
          <PosStatusStrip
            language={locale}
            {...(onSwitchLanguage ? { onSwitchLanguage } : {})}
            showTerminalIdentity
          />
        ) : null}
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
        {showStatusStrip ? (
          <PosStatusStrip
            language={locale}
            {...(onSwitchLanguage ? { onSwitchLanguage } : {})}
            showTerminalIdentity
          />
        ) : null}
      </SafeAreaView>
    );
  }

  const runtimeUnavailable =
    !state.capabilities.catalog ||
    !state.capabilities.cartEditing ||
    !state.capabilities.cashCheckout;
  const cashDraft = deriveCashDraft(state.cart, state.cashTenderedText);
  const toolbarActions: readonly SalesToolbarAction[] = [
    ...(onOpenDailyClose
      ? [
          {
            id: "daily-close" as const,
            label: t("header.dailyClose"),
            onPress: onOpenDailyClose,
            testID: "sales-open-daily-close",
            tone: "quiet" as const,
          },
        ]
      : []),
    ...(onOpenRemoteHistory
      ? [
          {
            id: "remote-history" as const,
            label: t("header.remoteHistory"),
            onPress: onOpenRemoteHistory,
            testID: "sales-open-remote-history",
            tone: "quiet" as const,
          },
        ]
      : []),
    ...(onOpenSettings
      ? [
          {
            id: "settings" as const,
            label: t("header.settings"),
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
            label: t("header.attendanceAudit"),
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
          accessibilityCopy={{
            moveEarlier: t("toolbar.moveEarlier"),
            moveLater: t("toolbar.moveLater"),
            reorderHint: t("toolbar.reorderHint"),
            positionChanged: (label, position, total) =>
              t("toolbar.positionChanged", { label, position, total }),
          }}
          actions={toolbarActions}
          canonicalOrder={toolbarOrder}
          onOrderChange={onToolbarOrderChange}
          style={styles.headerActions}
        />
      </View>

      {showStatusStrip ? (
        <PosStatusStrip
          language={locale}
          {...(onSwitchLanguage ? { onSwitchLanguage } : {})}
          showTerminalIdentity
        />
      ) : null}

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
                  : newTransactionGate?.state === "ota-update"
                    ? "updateGate.otaTitle"
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
        <View style={styles.transactionPane} testID="sales-transaction-pane">
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
                <Text style={styles.emptyCartTitle}>
                  {t("cart.emptyTitle")}
                </Text>
                <Text style={styles.emptyCartHint}>{t("cart.emptyHint")}</Text>
              </View>
            ) : (
              <FlatList
                contentContainerStyle={styles.cartList}
                data={state.cart.lines}
                keyExtractor={(line) => line.lineId}
                style={styles.cartListViewport}
                renderItem={({ item, index }) => {
                  const imageKey = cartProductImageKey(item);
                  return (
                    <View
                      style={styles.cartLine}
                      testID={`sales-line-${item.lineId}`}
                    >
                      <View style={styles.cartLineTop}>
                        <Text
                          accessibilityLabel={t("cart.lineNumber", {
                            number: index + 1,
                          })}
                          style={styles.cartLineNumber}
                          testID={`sales-line-${item.lineId}-line-number`}
                        >
                          {index + 1}
                        </Text>
                        <CartProductThumbnail
                          accessibilityLabel={t("cart.productImage", {
                            product: item.displayName,
                          })}
                          imageUri={cartProductImages[imageKey]}
                          placeholderLabel={t("cart.imagePlaceholder")}
                          testID={`sales-line-${item.lineId}-image`}
                        />
                        <View style={styles.cartLineIdentity}>
                          <Text numberOfLines={2} style={styles.cartLineName}>
                            {item.displayName}
                          </Text>
                          <Text numberOfLines={1} style={styles.cartLineCode}>
                            {item.lookupCode || item.productCode}
                          </Text>
                          <Text style={styles.cartLineUnitPrice}>
                            {formatAud(item.unitPrice.cents, locale)}
                            {item.discount.cents > 0 ? (
                              <Text
                                style={styles.discountAmountText}
                                testID={`sales-line-${item.lineId}-discount-amount`}
                              >
                                {`  −${formatAud(item.discount.cents, locale)}`}
                              </Text>
                            ) : null}
                          </Text>
                        </View>
                        <Text style={styles.cartLineTotal}>
                          {formatAud(item.actualAmount.cents, locale)}
                        </Text>
                      </View>
                      <View style={styles.lineControls}>
                        <ActionButton
                          accessibilityLabel={t("cart.decrease")}
                          disabled={
                            !state.capabilities.cartEditing ||
                            transactionActionsDisabled
                          }
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
                          <Text style={styles.quantityText}>
                            {item.quantity}
                          </Text>
                        </View>
                        <ActionButton
                          accessibilityLabel={t("cart.increase")}
                          disabled={
                            !state.capabilities.cartEditing ||
                            transactionActionsDisabled
                          }
                          label="+"
                          onPress={() => {
                            void presenter.increaseLine(item.lineId);
                          }}
                          testID={`sales-line-${item.lineId}-increase`}
                          tone="quiet"
                        />
                        <ActionButton
                          disabled={
                            !state.capabilities.cartEditing ||
                            transactionActionsDisabled
                          }
                          label={t("cart.edit")}
                          onPress={() => openLineEditor(item.lineId)}
                          testID={`sales-line-${item.lineId}-edit`}
                          tone="quiet"
                        />
                        <ActionButton
                          disabled={
                            !state.capabilities.cartEditing ||
                            transactionActionsDisabled
                          }
                          label={t("cart.discount")}
                          onPress={() => setDiscountLineId(item.lineId)}
                          testID={`sales-line-${item.lineId}-discount`}
                          tone="secondary"
                        />
                        <ActionButton
                          disabled={
                            !state.capabilities.cartEditing ||
                            transactionActionsDisabled
                          }
                          label={t("cart.remove")}
                          onPress={() => {
                            void presenter.removeLine(item.lineId);
                          }}
                          testID={`sales-line-${item.lineId}-remove`}
                          tone="danger"
                        />
                      </View>
                    </View>
                  );
                }}
              />
            )}
          </View>

          <View style={styles.summaryPane} testID="sales-summary-pane">
            <View style={styles.summaryMetrics}>
              <Text style={styles.summaryTitle}>{t("summary.title")}</Text>
              <SummaryRow
                amount={formatAud(state.cart.subtotal.cents, locale)}
                label={t("summary.subtotal")}
              />
              <SummaryRow
                amount={`−${formatAud(state.cart.discount.cents, locale)}`}
                amountTestID="sales-summary-discount-amount"
                amountTone="danger"
                label={t("summary.discount")}
                muted
              />
            </View>
            <View style={styles.summaryTotal}>
              <Text style={styles.totalLabel}>{t("summary.total")}</Text>
              <Text
                adjustsFontSizeToFit
                minimumFontScale={0.72}
                numberOfLines={1}
                style={styles.totalAmount}
              >
                {formatAud(state.cart.actualAmount.cents, locale)}
              </Text>
            </View>
            <View style={styles.summaryEditActions}>
              <ActionButton
                disabled={
                  cartEmpty ||
                  !state.capabilities.cartEditing ||
                  transactionActionsDisabled
                }
                label={t("summary.orderDiscount")}
                onPress={() => setOrderDiscountVisible(true)}
                style={styles.summaryEditAction}
                testID="sales-order-discount"
                tone="secondary"
              />
              <ActionButton
                disabled={
                  cartEmpty ||
                  !state.capabilities.cartEditing ||
                  transactionActionsDisabled
                }
                label={t("summary.clearCart")}
                onPress={() => setClearCartVisible(true)}
                style={styles.summaryEditAction}
                testID="sales-clear-cart"
                tone="danger"
              />
            </View>
          </View>
        </View>

        <View style={styles.functionPane} testID="sales-function-pane">
          <ScrollView
            contentContainerStyle={styles.functionScrollContent}
            keyboardShouldPersistTaps="handled"
            showsVerticalScrollIndicator={false}
            style={styles.functionScroll}
          >
            <Text style={styles.paneTitle}>{t("functions.title")}</Text>
            <Text style={styles.functionSectionTitle}>
              {t("functions.productEntry")}
            </Text>
            <View style={styles.searchInputRow}>
              <TextInput
                ref={searchInputRef}
                accessibilityLabel={t("catalog.searchPlaceholder")}
                autoCapitalize="none"
                autoCorrect={false}
                editable={!catalogActionsDisabled}
                onBlur={handleSearchInputBlur}
                onChangeText={(value) => presenter.setQuery(value)}
                onFocus={handleSearchInputFocus}
                onSubmitEditing={() => {
                  resetSearchInputToHidMode();
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
                disabled={catalogActionsDisabled}
                label={t("catalog.keyboard")}
                onPress={requestSearchKeyboard}
                style={styles.searchKeyboardAction}
                testID="sales-show-keyboard"
                tone="secondary"
              />
            </View>
            <View style={styles.searchActions}>
              <ActionButton
                disabled={catalogActionsDisabled}
                label={t("catalog.search")}
                onPress={openProductSearchDrawer}
                style={styles.searchAction}
                testID="sales-search-button"
                tone="secondary"
              />
              <ActionButton
                disabled={catalogActionsDisabled}
                label={t("catalog.addCode")}
                onPress={() => {
                  void presenter.addLookupCode();
                }}
                style={styles.searchAction}
                testID="sales-add-code-button"
              />
              <ActionButton
                disabled={catalogActionsDisabled}
                label={t("catalog.openItem")}
                onPress={() => {
                  setOpenItemPrice("");
                  setOpenItemVisible(true);
                  beginNumericInput();
                }}
                style={styles.searchAction}
                testID="sales-open-item-button"
                tone="secondary"
              />
              {onOpenSpecialProducts ? (
                <ActionButton
                  disabled={catalogActionsDisabled}
                  label={t("catalog.specialProducts")}
                  onPress={onOpenSpecialProducts}
                  style={styles.searchAction}
                  testID="sales-open-special-products"
                  tone="secondary"
                />
              ) : null}
            </View>
            <Text style={styles.scanHint}>{t("catalog.scanHint")}</Text>
            {state.pendingLookupCount > 0 || checkoutVerifying ? (
              <Text
                accessibilityLiveRegion="polite"
                style={styles.verifyingMessage}
                testID="sales-catalog-verifying"
              >
                {t("catalog.verifying")}
              </Text>
            ) : null}

            <View style={styles.functionDivider} />
            <Text style={styles.functionSectionTitle}>
              {t("functions.saleActions")}
            </Text>
            <View style={styles.functionGrid}>
              <ActionButton
                disabled={
                  !state.capabilities.hold ||
                  cartEmpty ||
                  transactionActionsDisabled
                }
                label={t("functions.hold")}
                onPress={() => {
                  void presenter.holdCart();
                }}
                style={styles.functionAction}
                testID="sales-hold"
                tone="secondary"
              />
              {onOpenReturns ? (
                <ActionButton
                  label={t("functions.returns")}
                  onPress={onOpenReturns}
                  style={styles.functionAction}
                  testID="sales-open-returns"
                  tone="secondary"
                />
              ) : null}
              {onOpenInstallments ? (
                <ActionButton
                  label={t("functions.installmentManagement")}
                  onPress={onOpenInstallments}
                  style={styles.functionAction}
                  testID="sales-open-installments"
                  tone="secondary"
                />
              ) : null}
              {onOpenHeldOrders ? (
                <ActionButton
                  label={t("functions.heldSales")}
                  onPress={onOpenHeldOrders}
                  style={styles.functionAction}
                  testID="sales-open-held-orders"
                  tone="secondary"
                />
              ) : null}
              <ActionButton
                disabled={!onReprintReceipt || utilityActionPending !== null}
                label={
                  utilityActionPending === "reprint"
                    ? t("functions.reprinting")
                    : t("functions.reprintReceipt")
                }
                onPress={() => runUtilityAction("reprint", onReprintReceipt)}
                style={styles.functionAction}
                testID="sales-reprint-receipt"
                tone="quiet"
              />
              <ActionButton
                disabled={!onOpenCashDrawer || utilityActionPending !== null}
                label={
                  utilityActionPending === "drawer"
                    ? t("functions.openingDrawer")
                    : t("functions.openDrawer")
                }
                onPress={() => runUtilityAction("drawer", onOpenCashDrawer)}
                style={styles.functionAction}
                testID="sales-open-cash-drawer"
                tone="quiet"
              />
            </View>
            {utilityActionResult ? (
              <Text
                accessibilityLiveRegion="polite"
                style={[
                  styles.utilityResult,
                  utilityActionResult.result.kind !== "completed" &&
                    styles.utilityResultWarning,
                ]}
                testID="sales-utility-action-result"
              >
                {t(
                  utilityResultCopyKey(
                    utilityActionResult.action,
                    utilityActionResult.result.kind,
                  ),
                )}
              </Text>
            ) : null}
          </ScrollView>

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
          <ActionButton
            disabled={
              cartEmpty ||
              !onOpenPayment ||
              !state.capabilities.cartEditing ||
              state.phase !== "selling"
            }
            label={
              cartEmpty
                ? t("summary.checkoutDisabled")
                : t("summary.goToPayment")
            }
            onPress={() => {
              if (!onOpenPayment) return;
              void presenter.prepareOnlineCheckout().then((preparedCart) => {
                if (preparedCart) onOpenPayment(preparedCart);
              });
            }}
            style={styles.checkoutButton}
            testID="sales-open-payment"
          />
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
        onRequestClose={() => setSearchDrawerVisible(false)}
        presentationStyle="overFullScreen"
        supportedOrientations={["landscape-left", "landscape-right"]}
        transparent
        visible={searchDrawerVisible}
      >
        <View style={styles.searchDrawerBackdrop}>
          <Pressable
            accessibilityLabel={t("catalog.closeResults")}
            accessibilityRole="button"
            onPress={() => setSearchDrawerVisible(false)}
            style={styles.searchDrawerDismissArea}
            testID="sales-search-results-backdrop"
          />
          <View
            accessibilityViewIsModal
            style={styles.searchDrawer}
            testID="sales-search-results-drawer"
          >
            <View style={styles.searchDrawerHeader}>
              <View style={styles.searchDrawerHeading}>
                <Text style={styles.modalTitle}>
                  {t("catalog.resultsTitle")}
                </Text>
                <Text numberOfLines={1} style={styles.searchDrawerQuery}>
                  {searchDrawerQuery}
                </Text>
              </View>
              <ActionButton
                label={t("catalog.closeResults")}
                onPress={() => setSearchDrawerVisible(false)}
                testID="sales-search-results-close"
                tone="quiet"
              />
            </View>
            {state.searchStatus === "searching" ? (
              <Text
                accessibilityLiveRegion="polite"
                style={styles.searchMessage}
                testID="sales-search-results-loading"
              >
                {t("catalog.searching")}
              </Text>
            ) : null}
            {searchResultsAreCurrent && visibleSearchResults.length === 0 ? (
              <Text
                accessibilityLiveRegion="polite"
                style={styles.searchMessage}
                testID="sales-search-results-empty"
              >
                {t("catalog.noResults")}
              </Text>
            ) : null}
            {state.errorCode === "search-required" ||
            state.errorCode === "search-failed" ||
            state.errorCode === "product-add-failed" ||
            state.errorCode === "authorization-denied" ? (
              <Text accessibilityRole="alert" style={styles.searchDrawerError}>
                {t(errorCopyKey(state.errorCode))}
              </Text>
            ) : null}
            <FlatList
              contentContainerStyle={styles.searchResults}
              data={visibleSearchResults}
              keyExtractor={(item) => `${item.productCode}:${item.lookupCode}`}
              keyboardShouldPersistTaps="handled"
              renderItem={({ item }) => (
                <ProductSearchRow
                  disabled={catalogActionsDisabled}
                  item={item}
                  locale={locale}
                  onAdd={() => {
                    void presenter.addProduct(item).then((added) => {
                      if (added) setSearchDrawerVisible(false);
                    });
                  }}
                  t={t}
                />
              )}
              style={styles.searchDrawerList}
              testID="sales-search-results-list"
            />
          </View>
        </View>
      </Modal>

      <Modal
        animationType="fade"
        onRequestClose={closeCashInput}
        presentationStyle="overFullScreen"
        supportedOrientations={["landscape-left", "landscape-right"]}
        transparent
        visible={state.phase === "cash" || state.phase === "submitting-cash"}
      >
        <View style={styles.modalBackdrop}>
          <View
            accessibilityViewIsModal
            style={styles.numericModal}
            testID="sales-cash-modal"
          >
            <Text style={styles.modalTitle}>{t("cash.title")}</Text>
            <View style={styles.numericEditorBody}>
              <View style={styles.numericEditorSummary}>
                <Text style={styles.cashDueLabel}>{t("cash.amountDue")}</Text>
                <Text style={styles.cashDueAmount}>
                  {formatAud(getCashDueCents(state.cart), locale)}
                </Text>
                <Text style={styles.fieldLabel}>{t("cash.tendered")}</Text>
                <NumericValueDisplay
                  accessibilityLabel={t("cash.tendered")}
                  currencyPrefix="$"
                  placeholder={t("cash.tenderedPlaceholder")}
                  testID="sales-cash-tendered"
                  value={state.cashTenderedText}
                />
                <ActionButton
                  disabled={state.phase !== "cash"}
                  label={t("cash.exact")}
                  onPress={() => presenter.setExactCash()}
                  style={styles.cashExactAction}
                  testID="sales-cash-exact"
                  tone="secondary"
                />
                <View style={styles.changePreview}>
                  <Text style={styles.changePreviewLabel}>
                    {t("cash.change")}
                  </Text>
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
              </View>
              <View style={styles.numericKeypadColumn}>
                <SalesNumberKeypad
                  disabled={state.phase !== "cash"}
                  labels={keypadLabels}
                  mode="decimal"
                  onKeyPress={handleCashKey}
                  testIDPrefix="sales-cash"
                />
              </View>
            </View>
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
        supportedOrientations={["landscape-left", "landscape-right"]}
        transparent
        visible={openItemVisible}
      >
        <View style={styles.modalBackdrop}>
          <View
            accessibilityViewIsModal
            style={styles.numericModal}
            testID="sales-open-item-modal"
          >
            <Text style={styles.modalTitle}>{t("openItem.title")}</Text>
            <Text style={styles.discountHint}>{t("openItem.hint")}</Text>
            <View style={styles.numericEditorBody}>
              <View style={styles.numericEditorSummary}>
                <Text style={styles.fieldLabel}>{t("openItem.price")}</Text>
                <NumericValueDisplay
                  accessibilityLabel={t("openItem.price")}
                  placeholder="0.00"
                  testID="sales-open-item-price"
                  value={openItemPrice}
                />
              </View>
              <View style={styles.numericKeypadColumn}>
                <SalesNumberKeypad
                  labels={keypadLabels}
                  mode="decimal"
                  onKeyPress={handleOpenItemKey}
                  testIDPrefix="sales-open-item"
                />
              </View>
            </View>
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
        supportedOrientations={["landscape-left", "landscape-right"]}
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
                      replaceOnNextDigit: false,
                      value: "",
                    });
                    beginNumericInput();
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
                      replaceOnNextDigit: false,
                      value: "",
                    });
                    beginNumericInput();
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
        supportedOrientations={["landscape-left", "landscape-right"]}
        transparent
        visible={lineEdit !== null}
      >
        <View style={styles.modalBackdrop}>
          <View
            accessibilityViewIsModal
            style={styles.numericModal}
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
            <View style={styles.numericEditorBody}>
              <View style={styles.numericEditorSummary}>
                <Text style={styles.fieldLabel}>{t("editLine.value")}</Text>
                <NumericValueDisplay
                  accessibilityLabel={t("editLine.value")}
                  placeholder={lineEdit?.mode === "quantity" ? "0" : "0.00"}
                  testID="sales-line-edit-value"
                  value={lineEdit?.value ?? ""}
                />
              </View>
              <View style={styles.numericKeypadColumn}>
                <SalesNumberKeypad
                  labels={keypadLabels}
                  mode={lineEdit?.mode === "quantity" ? "integer" : "decimal"}
                  onKeyPress={handleLineEditKey}
                  testIDPrefix="sales-line-edit"
                />
              </View>
            </View>
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
        supportedOrientations={["landscape-left", "landscape-right"]}
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
                  beginNumericInput();
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
                  beginNumericInput();
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
        supportedOrientations={["landscape-left", "landscape-right"]}
        transparent
        visible={orderEdit !== null}
      >
        <View style={styles.modalBackdrop}>
          <View
            accessibilityViewIsModal
            style={styles.numericModal}
            testID="sales-order-edit-modal"
          >
            <Text style={styles.modalTitle}>
              {t("orderDiscount.editTitle")}
            </Text>
            <View style={styles.numericEditorBody}>
              <View style={styles.numericEditorSummary}>
                <Text style={styles.fieldLabel}>
                  {orderEdit?.mode === "percent"
                    ? t("editLine.discountPercent")
                    : t("editLine.discountAmount")}
                </Text>
                <NumericValueDisplay
                  accessibilityLabel={t("editLine.value")}
                  placeholder="0.00"
                  testID="sales-order-edit-value"
                  value={orderEdit?.value ?? ""}
                />
              </View>
              <View style={styles.numericKeypadColumn}>
                <SalesNumberKeypad
                  labels={keypadLabels}
                  mode="decimal"
                  onKeyPress={handleOrderEditKey}
                  testIDPrefix="sales-order-edit"
                />
              </View>
            </View>
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
        supportedOrientations={["landscape-left", "landscape-right"]}
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

function CartProductThumbnail({
  accessibilityLabel,
  imageUri,
  placeholderLabel,
  testID,
}: Readonly<{
  accessibilityLabel: string;
  imageUri: string | null | undefined;
  placeholderLabel: string;
  testID: string;
}>) {
  const [imageFailed, setImageFailed] = useState(false);
  useEffect(() => {
    setImageFailed(false);
  }, [imageUri]);
  const showImage = Boolean(imageUri) && !imageFailed;
  return (
    <View
      accessible
      accessibilityLabel={accessibilityLabel}
      style={styles.cartProductImageFrame}
      testID={testID}
    >
      {showImage ? (
        <Image
          accessible={false}
          onError={() => setImageFailed(true)}
          resizeMode="contain"
          source={{ uri: imageUri as string }}
          style={styles.cartProductImage}
          testID={`${testID}-content`}
        />
      ) : (
        <Text
          accessibilityElementsHidden
          importantForAccessibility="no-hide-descendants"
          style={styles.cartProductImagePlaceholder}
        >
          {placeholderLabel}
        </Text>
      )}
    </View>
  );
}

function NumericValueDisplay({
  accessibilityLabel,
  currencyPrefix,
  placeholder,
  testID,
  value,
}: NumericValueDisplayProps) {
  const displayValue = value || placeholder;
  return (
    <View
      accessible
      accessibilityLabel={accessibilityLabel}
      accessibilityLiveRegion="polite"
      accessibilityValue={{ text: displayValue }}
      style={styles.numericValueDisplay}
      testID={testID}
    >
      {currencyPrefix ? (
        <Text style={styles.numericValuePrefix}>{currencyPrefix}</Text>
      ) : null}
      <Text
        adjustsFontSizeToFit
        minimumFontScale={0.65}
        numberOfLines={1}
        style={[
          styles.numericValueText,
          value.length === 0 && styles.numericValuePlaceholder,
        ]}
      >
        {displayValue}
      </Text>
    </View>
  );
}

function SummaryRow({
  amount,
  amountTestID,
  amountTone = "default",
  label,
  muted = false,
}: Readonly<{
  amount: string;
  amountTestID?: string;
  amountTone?: "default" | "danger";
  label: string;
  muted?: boolean;
}>) {
  return (
    <View style={styles.summaryRow}>
      <Text style={[styles.summaryLabel, muted && styles.mutedText]}>
        {label}
      </Text>
      <Text
        style={[
          styles.summaryAmount,
          muted && styles.mutedText,
          amountTone === "danger" && styles.discountAmountText,
        ]}
        testID={amountTestID}
      >
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

function cartProductImageKey(
  input: Readonly<{ productCode: string; lookupCode: string }>,
): string {
  return `${input.productCode}\u0000${input.lookupCode}`;
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

function utilityResultCopyKey(
  action: SalesUtilityAction,
  result: SalesUtilityActionResult["kind"],
): SalesCopyKey {
  if (result === "completed") {
    return action === "reprint"
      ? "functions.reprintCompleted"
      : "functions.drawerCompleted";
  }
  if (result === "not-found") {
    return action === "reprint"
      ? "functions.reprintNotFound"
      : "functions.drawerUnavailable";
  }
  if (result === "denied") {
    return "functions.actionDenied";
  }
  if (result === "unavailable") {
    return action === "reprint"
      ? "functions.reprintUnavailable"
      : "functions.drawerUnavailable";
  }
  if (result === "unknown") {
    return "functions.actionUnknown";
  }
  return action === "reprint"
    ? "functions.reprintFailed"
    : "functions.drawerFailed";
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
    minHeight: 120,
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
  cartLineNumber: {
    color: posColors.mutedInk,
    fontSize: 13,
    fontVariant: ["tabular-nums"],
    fontWeight: "800",
    textAlign: "center",
    width: 24,
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
    alignItems: "center",
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
  cartListViewport: {
    flex: 1,
    minHeight: 0,
  },
  cartPane: {
    backgroundColor: "#F8F6F1",
    borderColor: posColors.border,
    borderRadius: 5,
    borderWidth: 1,
    flex: 1,
    gap: 12,
    minHeight: 0,
    minWidth: 0,
    padding: 14,
  },
  cartProductImage: {
    height: "100%",
    width: "100%",
  },
  cartProductImageFrame: {
    alignItems: "center",
    backgroundColor: "#EEECE6",
    borderColor: posColors.border,
    borderRadius: 4,
    borderWidth: 1,
    height: 54,
    justifyContent: "center",
    overflow: "hidden",
    width: 54,
  },
  cartProductImagePlaceholder: {
    color: "#8B9399",
    fontSize: 10,
    fontWeight: "900",
    letterSpacing: 0.4,
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
  cashExactAction: {
    marginTop: 12,
    width: "100%",
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
  dangerButtonText: {
    color: posColors.red,
  },
  discountAmountText: {
    color: posColors.red,
    fontVariant: ["tabular-nums"],
    fontWeight: "800",
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
  functionAction: {
    flexBasis: "47%",
    flexGrow: 1,
    paddingHorizontal: 8,
  },
  functionDivider: {
    backgroundColor: posColors.border,
    height: 1,
    marginVertical: 4,
  },
  functionGrid: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
  },
  functionPane: {
    backgroundColor: posColors.surface,
    borderColor: posColors.border,
    borderRadius: 5,
    borderWidth: 1,
    flexBasis: 336,
    flexGrow: 0,
    flexShrink: 0,
    maxWidth: 380,
    minWidth: 312,
    padding: 14,
  },
  functionScroll: {
    flex: 1,
    minHeight: 0,
  },
  functionScrollContent: {
    gap: 10,
    paddingBottom: 12,
  },
  functionSectionTitle: {
    color: posColors.mutedInk,
    fontSize: 11,
    fontWeight: "900",
    letterSpacing: 0.5,
    marginTop: 2,
    textTransform: "uppercase",
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
  numericEditorBody: {
    alignItems: "stretch",
    flexDirection: "row",
    gap: 24,
    marginTop: 18,
  },
  numericEditorSummary: {
    flex: 1,
    justifyContent: "center",
    minWidth: 0,
  },
  numericKeypadColumn: {
    flexBasis: 352,
    flexGrow: 0,
    flexShrink: 1,
    maxWidth: 380,
    minWidth: 292,
  },
  numericModal: {
    backgroundColor: posColors.surface,
    borderColor: posColors.border,
    borderRadius: 6,
    borderWidth: 1,
    maxWidth: 820,
    padding: 26,
    width: "78%",
  },
  numericValueDisplay: {
    alignItems: "center",
    backgroundColor: "#FAFAF8",
    borderColor: posColors.ink,
    borderRadius: 4,
    borderWidth: 1,
    flexDirection: "row",
    minHeight: 64,
    paddingHorizontal: 16,
  },
  numericValuePlaceholder: {
    color: "#7B8793",
  },
  numericValuePrefix: {
    color: posColors.mutedInk,
    fontSize: 24,
    fontWeight: "800",
    marginRight: 8,
  },
  numericValueText: {
    color: posColors.ink,
    flex: 1,
    fontSize: 30,
    fontVariant: ["tabular-nums"],
    fontWeight: "900",
    textAlign: "right",
  },
  mutedText: {
    color: posColors.mutedInk,
  },
  offlineCard: {
    backgroundColor: posColors.orangeSoft,
    borderColor: posColors.orange,
    borderRadius: 4,
    borderWidth: 1,
    marginBottom: 10,
    marginTop: 8,
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
    flexBasis: "47%",
    flexGrow: 1,
    paddingHorizontal: 7,
  },
  searchActions: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
  },
  searchDrawer: {
    backgroundColor: posColors.surface,
    borderLeftColor: posColors.border,
    borderLeftWidth: 1,
    flexBasis: "38%",
    height: "100%",
    maxWidth: 460,
    minWidth: 360,
    paddingBottom: 16,
    paddingHorizontal: 18,
    paddingTop: 24,
  },
  searchDrawerBackdrop: {
    backgroundColor: "rgba(16, 37, 58, 0.42)",
    flex: 1,
    flexDirection: "row",
    justifyContent: "flex-end",
  },
  searchDrawerDismissArea: {
    flex: 1,
  },
  searchDrawerError: {
    backgroundColor: posColors.redSoft,
    color: posColors.red,
    fontSize: 13,
    fontWeight: "700",
    marginVertical: 8,
    padding: 10,
  },
  searchDrawerHeader: {
    alignItems: "center",
    borderBottomColor: posColors.border,
    borderBottomWidth: 1,
    flexDirection: "row",
    gap: 12,
    justifyContent: "space-between",
    paddingBottom: 12,
  },
  searchDrawerHeading: {
    flex: 1,
    minWidth: 0,
  },
  searchDrawerList: {
    flex: 1,
    minHeight: 0,
  },
  searchDrawerQuery: {
    color: posColors.mutedInk,
    fontSize: 12,
    marginTop: 4,
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
    alignItems: "stretch",
    backgroundColor: posColors.surface,
    borderColor: posColors.border,
    borderRadius: 5,
    borderWidth: 1,
    flexDirection: "row",
    gap: 14,
    minHeight: 132,
    padding: 14,
  },
  summaryEditAction: {
    flex: 1,
    paddingHorizontal: 8,
  },
  summaryEditActions: {
    flexBasis: 184,
    flexDirection: "column",
    gap: 8,
    minWidth: 164,
  },
  summaryMetrics: {
    flex: 1,
    justifyContent: "center",
    minWidth: 148,
  },
  summaryRow: {
    alignItems: "center",
    flexDirection: "row",
    justifyContent: "space-between",
    minHeight: 30,
  },
  summaryRows: {
    marginTop: 15,
  },
  summarySpacer: {
    flex: 1,
  },
  summaryTitle: {
    color: posColors.ink,
    fontSize: 14,
    fontWeight: "900",
    marginBottom: 4,
  },
  summaryTotal: {
    borderLeftColor: posColors.border,
    borderLeftWidth: 1,
    flexBasis: 186,
    justifyContent: "center",
    minWidth: 166,
    paddingLeft: 14,
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
  transactionPane: {
    flex: 1,
    gap: 10,
    minWidth: 0,
  },
  utilityResult: {
    backgroundColor: posColors.greenSoft,
    borderColor: posColors.green,
    borderRadius: 4,
    borderWidth: 1,
    color: posColors.green,
    fontSize: 12,
    fontWeight: "700",
    lineHeight: 17,
    padding: 10,
  },
  utilityResultWarning: {
    backgroundColor: "#FFF4D8",
    borderColor: posColors.orange,
    color: "#744024",
  },
  verifyingMessage: {
    color: posColors.mutedInk,
    fontSize: 12,
    paddingVertical: 4,
    textAlign: "center",
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
    minHeight: 0,
    padding: 10,
  },
});
