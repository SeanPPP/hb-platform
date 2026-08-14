import { useEffect, useMemo, useRef, useState, useSyncExternalStore } from "react";
import { useTranslation } from "react-i18next";
import {
  ActivityIndicator,
  FlatList,
  Image,
  Modal,
  PanResponder,
  StyleSheet,
  Text,
  View,
} from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";

import {
  resolveSpecialProductsLocale,
  specialProductsStatusCopyKey,
  specialProductsText,
  type SpecialProductsCopyKey,
} from "./special-products-copy";
import {
  type SpecialProductsPresenter,
  type SpecialProductsStatusCode,
  type SpecialProductsState,
} from "./special-products-presenter";

import type { SpecialProductItem } from "@/core/contracts";
import {
  PosKeyboardAwareScrollView,
  PosKeyboardAwareTextInput,
} from "@/ui/controls/pos-keyboard-aware-scroll-view";
import { PosPanResponderView } from "@/ui/controls/pos-pan-responder-view";
import { PosPressable } from "@/ui/controls/pos-pressable";
import { HandheldStateSurface } from "@/ui/handheld/handheld-design-states";
import { posColors } from "@/ui/theme";
export const SPECIAL_PRODUCTS_MIN_TOUCH_TARGET = 48;
export const SPECIAL_PRODUCTS_GRID_COLUMNS = 2;
const SPECIAL_PRODUCTS_GRID_GAP = 8;

/**
 * 由拖拽位移计算目标网格索引（行列整数格取整，越界收敛到有效范围）。
 * 纯函数便于单元测试。
 */
export function specialProductsDragTargetIndex(
  fromIndex: number,
  dx: number,
  dy: number,
  columnCount: number,
  cellWidth: number,
  cellHeight: number,
  itemCount: number,
): number {
  if (
    columnCount <= 0 ||
    cellWidth <= 0 ||
    cellHeight <= 0 ||
    itemCount <= 0
  ) {
    return fromIndex;
  }
  const fromRow = Math.floor(fromIndex / columnCount);
  const fromCol = fromIndex % columnCount;
  const col = clampInt(
    fromCol + Math.round(dx / cellWidth),
    0,
    columnCount - 1,
  );
  const maxRow = Math.max(0, Math.ceil(itemCount / columnCount) - 1);
  const row = clampInt(fromRow + Math.round(dy / cellHeight), 0, maxRow);
  return Math.min(row * columnCount + col, itemCount - 1);
}

function clampInt(value: number, lower: number, upper: number): number {
  return Math.min(Math.max(value, lower), upper);
}

/** 拖拽中实时计算目标插槽索引；网格尺寸未知时返回 null（不高亮）。 */
function dragTargetIndex(
  state: SpecialProductsState,
  drag: Readonly<{
    code: string;
    dx: number;
    dy: number;
    fromIndex: number;
  }>,
  width: number,
  height: number,
): number | null {
  if (width <= 0 || height <= 0) return null;
  return specialProductsDragTargetIndex(
    drag.fromIndex,
    drag.dx,
    drag.dy,
    SPECIAL_PRODUCTS_GRID_COLUMNS,
    width,
    height,
    state.items.length,
  );
}

type SpecialProductsScreenProps = Readonly<{
  onBack?(): void;
  presenter: SpecialProductsScreenPresenter;
}>;

type SpecialProductsTranslate = (
  key: SpecialProductsCopyKey,
  values?: Readonly<Record<string, string | number>>,
) => string;

export type SpecialProductsScreenPresenter = Pick<
  SpecialProductsPresenter,
  | "addToCart"
  | "download"
  | "getState"
  | "load"
  | "mark"
  | "moveTo"
  | "reorder"
  | "searchCandidates"
  | "setSearchQuery"
  | "subscribe"
> & Readonly<{ getState(): SpecialProductsState }>;

/**
 * 手持端单列操作台：本地商品始终可离线读取、加购；Manage 写操作仅在
 * 在线且授权时通过受控弹窗开放，避免把在线能力伪装成本地可用。
 */
export function SpecialProductsScreen({
  onBack,
  presenter,
}: SpecialProductsScreenProps) {
  const { i18n } = useTranslation();
  const [addModalVisible, setAddModalVisible] = useState(false);
  const [drag, setDrag] = useState<Readonly<{
    code: string;
    dx: number;
    dy: number;
    fromIndex: number;
  }> | null>(null);
  const [cellWidth, setCellWidth] = useState(0);
  const [cellHeight, setCellHeight] = useState(0);
  const locale = resolveSpecialProductsLocale(
    i18n.resolvedLanguage ?? i18n.language,
  );
  const t: SpecialProductsTranslate = (key, values) =>
    specialProductsText(locale, key, values);
  const state = useSyncExternalStore(
    presenter.subscribe,
    presenter.getState,
    presenter.getState,
  );

  useEffect(() => {
    void presenter.load();
  }, [presenter]);

  return (
    <SafeAreaView style={styles.safeArea} testID="special-products-screen">
      <View style={styles.page}>
        <View style={styles.header}>
          <View style={styles.titleGroup}>
            <Text style={styles.eyebrow}>{t("header.eyebrow")}</Text>
            <Text style={styles.title}>{t("header.title")}</Text>
            <Text style={styles.subtitle}>{t("header.subtitle")}</Text>
          </View>
          <View style={styles.headerActions}>
            {onBack ? (
              <ActionButton
                label={t("action.back")}
                onPress={onBack}
                sound="navigate"
                testID="special-products-back"
                tone="quiet"
              />
            ) : null}
            <ActionButton
              disabled={state.busy || state.kind === "loading"}
              label={t("action.refreshLocal")}
              onPress={() => void presenter.load()}
              testID="special-products-refresh-local"
              tone="secondary"
            />
            {state.access.canManage ? (
              <>
                <ActionButton
                  disabled={!state.online || state.busy}
                  label={t("management.title")}
                  onPress={() => {
                    // 打开弹窗前清空上次搜索，避免旧候选误导
                    presenter.setSearchQuery("");
                    setAddModalVisible(true);
                  }}
                  testID="special-products-add-product"
                  tone="secondary"
                />
                <ActionButton
                  disabled={!state.online || state.busy}
                  label={
                    state.busy
                      ? t("action.working")
                      : t("action.download")
                  }
                  onPress={() => void presenter.download()}
                  testID="special-products-download"
                />
              </>
            ) : null}
          </View>
        </View>

        <HandheldStateSurface
          slug="special-products-grid"
          style={styles.stateSurface}
        >
        {!state.online ? (
          <View
            accessibilityLiveRegion="polite"
            style={styles.offlineNote}
            testID="special-products-offline-note"
          >
            <Text style={styles.offlineNoteText}>{t("offlineNote")}</Text>
          </View>
        ) : null}

        {state.statusCode ? (
          <StatusBanner statusCode={state.statusCode} t={t} />
        ) : null}

        <View style={styles.workspace}>
          <View style={styles.catalogPane}>
            <View style={styles.panelHeader}>
              <View>
                <Text style={styles.panelTitle}>{t("catalog.title")}</Text>
                <Text style={styles.panelMeta}>
                  {t("catalog.itemCount", { count: state.items.length })}
                </Text>
              </View>
              {state.kind === "loading" ? (
                <ActivityIndicator
                  color={posColors.orange}
                  testID="special-products-loading"
                />
              ) : null}
            </View>

            {state.kind === "unauthorized" ? (
              <EmptyState
                message={t("catalog.unauthorized")}
                testID="special-products-unauthorized"
              />
            ) : null}
            {state.kind === "failed" && state.items.length === 0 ? (
              <EmptyState
                message={t("catalog.failed")}
                testID="special-products-failed"
              />
            ) : null}
            {state.kind === "ready" && state.items.length === 0 ? (
              <EmptyState
                message={t("catalog.empty")}
                testID="special-products-empty"
              />
            ) : null}
            {state.items.length > 0 ? (
              <FlatList
                columnWrapperStyle={styles.productCardRow}
                contentContainerStyle={styles.productList}
                data={state.items}
                keyExtractor={(item) => item.productCode}
                numColumns={SPECIAL_PRODUCTS_GRID_COLUMNS}
                renderItem={({ item, index }) => (
                  <SpecialProductCard
                    canAddToCart={state.access.canAddToCart}
                    canDrag={
                      state.access.canManage && state.online && !state.busy
                    }
                    canManage={state.access.canManage}
                    cellWidth={cellWidth}
                    cellHeight={cellHeight}
                    disabled={state.busy}
                    drag={drag}
                    dragTargetIndex={
                      drag
                        ? dragTargetIndex(
                            state,
                            drag,
                            cellWidth,
                            cellHeight,
                          )
                        : null
                    }
                    index={index}
                    item={item}
                    itemCount={state.items.length}
                    online={state.online}
                    onAddToCart={() => void presenter.addToCart(item.productCode)}
                    onDragEnd={(code, fromIndex, dx, dy) => {
                      const targetIndex = specialProductsDragTargetIndex(
                        fromIndex,
                        dx,
                        dy,
                        SPECIAL_PRODUCTS_GRID_COLUMNS,
                        cellWidth,
                        cellHeight,
                        state.items.length,
                      );
                      setDrag(null);
                      if (targetIndex !== fromIndex) {
                        void presenter.moveTo(code, targetIndex);
                      }
                    }}
                    onDragMove={(code, fromIndex, dx, dy) =>
                      setDrag({ code, fromIndex, dx, dy })
                    }
                    onDragStart={(code, fromIndex) =>
                      setDrag({ code, fromIndex, dx: 0, dy: 0 })
                    }
                    onMeasureCell={(width, height) => {
                      // 从卡片实测尺寸推导网格单元格（宽=列宽，高=卡片高+行距）
                      if (width > 0) setCellWidth(width);
                      if (height > 0) setCellHeight(height + SPECIAL_PRODUCTS_GRID_GAP);
                    }}
                    onMoveDown={() => void presenter.reorder(item.productCode, 1)}
                    onMoveUp={() => void presenter.reorder(item.productCode, -1)}
                    onRemove={() => void presenter.mark(item.productCode, false)}
                    t={t}
                  />
                )}
                testID="special-products-list"
              />
            ) : null}
          </View>
        </View>
        </HandheldStateSurface>

        {state.access.canManage ? (
          <SpecialProductsAddModal
            busy={state.busy}
            candidates={state.candidates}
            onAdd={(productCode) => {
              // 关闭弹窗后由页面状态横幅呈现 mark 结果
              setAddModalVisible(false);
              void presenter.mark(productCode, true);
            }}
            onClose={() => setAddModalVisible(false)}
            onQueryChange={(query) => presenter.setSearchQuery(query)}
            onSearch={() => void presenter.searchCandidates()}
            query={state.searchQuery}
            searching={state.searching}
            t={t}
            visible={addModalVisible}
          />
        ) : null}
      </View>
    </SafeAreaView>
  );
}

export function SpecialProductsUnavailableScreen({
  onBack,
}: Readonly<{ onBack(): void }>) {
  const { i18n } = useTranslation();
  const locale = resolveSpecialProductsLocale(
    i18n.resolvedLanguage ?? i18n.language,
  );
  const t: SpecialProductsTranslate = (key, values) =>
    specialProductsText(locale, key, values);
  return (
    <SafeAreaView
      style={styles.safeArea}
      testID="special-products-runtime-unavailable"
    >
      <View style={styles.unavailable}>
        <Text style={styles.eyebrow}>{t("unavailable.eyebrow")}</Text>
        <Text style={styles.unavailableTitle}>{t("unavailable.title")}</Text>
        <Text style={styles.unavailableHint}>{t("unavailable.hint")}</Text>
        <ActionButton
          label={t("unavailable.back")}
          onPress={onBack}
          sound="navigate"
          testID="special-products-unavailable-back"
        />
      </View>
    </SafeAreaView>
  );
}

function SpecialProductsAddModal({
  busy,
  candidates,
  onAdd,
  onClose,
  onQueryChange,
  onSearch,
  query,
  searching,
  t,
  visible,
}: Readonly<{
  busy: boolean;
  candidates: readonly SpecialProductItem[];
  onAdd(productCode: string): void;
  onClose(): void;
  onQueryChange(query: string): void;
  onSearch(): void;
  query: string;
  searching: boolean;
  t: SpecialProductsTranslate;
  visible: boolean;
}>) {
  return (
    <Modal
      animationType="fade"
      onRequestClose={onClose}
      presentationStyle="overFullScreen"
      statusBarTranslucent
      supportedOrientations={["portrait", "portrait-upside-down"]}
      transparent
      visible={visible}
    >
      <View
        accessibilityViewIsModal
        style={styles.modalOverlay}
        testID="special-products-add-modal"
      >
        <HandheldStateSurface
          slug="special-product-editor"
          style={styles.modalStateSurface}
        >
          <View
            style={styles.modalPanel}
            testID="special-products-add-modal-panel"
          >
          <PosKeyboardAwareScrollView
            contentContainerStyle={styles.modalPanelContent}
            keyboardShouldPersistTaps="handled"
            style={styles.modalScroll}
            testID="special-products-add-modal-scroll"
          >
          <View style={styles.modalHeader}>
            <View style={styles.modalTitleGroup}>
              <Text style={styles.panelTitle}>{t("management.title")}</Text>
              <Text style={styles.managementHint}>{t("management.hint")}</Text>
            </View>
            <ActionButton
              label={t("action.close")}
              onPress={onClose}
              sound="navigate"
              testID="special-products-add-modal-close"
              tone="quiet"
            />
          </View>

          <PosKeyboardAwareTextInput
            accessibilityLabel={t("management.searchLabel")}
            autoCapitalize="none"
            autoCorrect={false}
            editable={!busy}
            onChangeText={onQueryChange}
            onSubmitEditing={onSearch}
            placeholder={t("management.searchPlaceholder")}
            placeholderTextColor="#7B8793"
            selectionColor={posColors.orange}
            style={styles.searchInput}
            testID="special-products-search-input"
            value={query}
          />
          <View style={styles.modalSearchButton}>
            <ActionButton
              disabled={busy || searching}
              label={
                searching ? t("management.searching") : t("management.search")
              }
              onPress={onSearch}
              testID="special-products-search"
              tone="secondary"
            />
          </View>

          {candidates.length === 0 ? (
            <Text style={styles.candidateEmpty}>{t("management.empty")}</Text>
          ) : (
            <View style={styles.candidateList}>
              {/* 候选上限 50 条，直接 map 渲染普通 View，避免 FlatList
                  嵌套在 ScrollView 内触发虚拟化警告与全量渲染 */}
              {candidates.map((item) => (
                // 整行可点击 = 添加为特殊商品，触屏友好
                <PosPressable
                  accessibilityRole="button"
                  accessibilityState={{ disabled: busy }}
                  disabled={busy}
                  key={item.productCode}
                  onPress={() => onAdd(item.productCode)}
                  style={({ pressed }) => [
                    styles.candidateRow,
                    pressed && !busy && styles.candidatePressed,
                  ]}
                  testID={`special-products-candidate-${item.productCode}`}
                >
                  {/* 候选商品同样展示缩略图，与目录卡片一致 */}
                  <ProductCardImage
                    imageUri={item.productImage}
                    placeholder={item.displayName.slice(0, 1).toUpperCase()}
                    size={56}
                    testID={`special-products-candidate-image-${item.productCode}`}
                  />
                  <View style={styles.candidateIdentity}>
                    <Text numberOfLines={2} style={styles.candidateName}>
                      {item.displayName}
                    </Text>
                    <Text numberOfLines={1} style={styles.candidateCode}>
                      {item.lookupCode || item.productCode}
                    </Text>
                  </View>
                  <Text style={styles.candidateAdd}>{t("management.mark")}</Text>
                </PosPressable>
              ))}
            </View>
          )}
          </PosKeyboardAwareScrollView>
          </View>
        </HandheldStateSurface>
      </View>
    </Modal>
  );
}

function SpecialProductCard({
  canAddToCart,
  canDrag,
  canManage,
  cellWidth,
  cellHeight,
  disabled,
  drag,
  dragTargetIndex,
  index,
  item,
  itemCount,
  online,
  onAddToCart,
  onDragEnd,
  onDragMove,
  onDragStart,
  onMeasureCell,
  onMoveDown,
  onMoveUp,
  onRemove,
  t,
}: Readonly<{
  canAddToCart: boolean;
  canDrag: boolean;
  canManage: boolean;
  cellWidth: number;
  cellHeight: number;
  disabled: boolean;
  drag: Readonly<{
    code: string;
    dx: number;
    dy: number;
    fromIndex: number;
  }> | null;
  dragTargetIndex: number | null;
  index: number;
  item: SpecialProductItem;
  itemCount: number;
  online: boolean;
  onAddToCart(): void;
  onDragEnd(
    productCode: string,
    fromIndex: number,
    dx: number,
    dy: number,
  ): void;
  onDragMove(
    productCode: string,
    fromIndex: number,
    dx: number,
    dy: number,
  ): void;
  onDragStart(productCode: string, fromIndex: number): void;
  onMeasureCell(width: number, height: number): void;
  onMoveDown(): void;
  onMoveUp(): void;
  onRemove(): void;
  t: SpecialProductsTranslate;
}>) {
  const managementDisabled = disabled || !online;
  const addable = canAddToCart && !disabled;
  const dragActive = drag?.code === item.productCode;
  const longPressActive = useRef(false);
  const panResponder = useMemo(
    () =>
      PanResponder.create({
        // 点击/长按由内层 Pressable 处理；仅在长按后开始移动时接管手势
        onStartShouldSetPanResponder: () => false,
        onMoveShouldSetPanResponder: () => longPressActive.current,
        onPanResponderMove: (_event, gesture) => {
          if (!longPressActive.current) return;
          onDragMove(item.productCode, index, gesture.dx, gesture.dy);
        },
        onPanResponderRelease: (_event, gesture) => {
          if (!longPressActive.current) return;
          longPressActive.current = false;
          onDragEnd(item.productCode, index, gesture.dx, gesture.dy);
        },
        onPanResponderTerminate: () => {
          if (!longPressActive.current) return;
          longPressActive.current = false;
          onDragEnd(item.productCode, index, 0, 0);
        },
      }),
    [index, item.productCode, onDragEnd, onDragMove],
  );
  const panHandlers = panResponder.panHandlers;
  return (
    <PosPanResponderView
      onLayout={(event) => {
        const { height, width } = event.nativeEvent.layout;
        onMeasureCell(width, height);
      }}
      panHandlers={panHandlers}
      style={[
        styles.productCard,
        dragTargetIndex !== null &&
          dragTargetIndex === index &&
          dragTargetIndex !== drag?.fromIndex &&
          styles.productCardDragTarget,
      ]}
      testID={`special-product-card-${item.productCode}-shell`}
    >
      {/* 整卡可点击 = 加购，触屏操作无需精确瞄准小按钮（与 WPF 卡片一致）；
          长按（canDrag 时）抬起卡片进入拖拽排序 */}
      <PosPressable
        accessibilityRole="button"
        accessibilityState={{ disabled: !addable || dragActive }}
        disabled={!addable || dragActive}
        onLongPress={
          canDrag
            ? () => {
                longPressActive.current = true;
                onDragStart(item.productCode, index);
              }
            : undefined
        }
        // 仅可拖拽时注册排序反馈，避免普通卡片的长按抑制 tap 音。
        longPressSound={canDrag ? "navigate" : false}
        onPress={onAddToCart}
        sound="tap"
        style={[
          styles.productCardMain,
          dragActive && styles.productCardDragging,
          dragActive &&
            cellHeight > 0 && {
              transform: [
                { translateX: drag.dx ?? 0 },
                { translateY: drag.dy ?? 0 },
              ],
            },
        ]}
        testID={`special-product-card-${item.productCode}`}
      >
        <ProductCardImage
          imageUri={item.productImage}
          placeholder={item.displayName.slice(0, 1).toUpperCase()}
          testID={`special-product-card-image-${item.productCode}`}
        />
        <Text numberOfLines={2} style={styles.productCardName}>
          {item.displayName}
        </Text>
        <Text numberOfLines={1} style={styles.productCardCode}>
          {item.lookupCode || item.productCode}
        </Text>
        <Text style={styles.productCardPrice}>
          {formatAud(item.retailPriceCents)}
        </Text>
      </PosPressable>
      {canManage ? (
        <View style={styles.productCardActions}>
          <ActionButton
            mini
            disabled={managementDisabled || index === 0}
            label="↑"
            onPress={onMoveUp}
            testID={`special-products-move-up-${item.productCode}`}
            tone="quiet"
          />
          <ActionButton
            mini
            disabled={managementDisabled || index === itemCount - 1}
            label="↓"
            onPress={onMoveDown}
            testID={`special-products-move-down-${item.productCode}`}
            tone="quiet"
          />
          <ActionButton
            mini
            danger
            disabled={managementDisabled}
            label={t("row.remove")}
            onPress={onRemove}
            testID={`special-products-remove-${item.productCode}`}
            tone="quiet"
          />
        </View>
      ) : null}
    </PosPanResponderView>
  );
}

function ProductCardImage({
  imageUri,
  placeholder,
  size,
  testID,
}: Readonly<{
  imageUri: string | null;
  placeholder: string;
  size?: number;
  testID: string;
}>) {
  // 渲染层协议白名单：仅允许 https/http 与 data:image，避免被篡改的
  // file:// 等非预期 uri 进入 RN Image 渲染管线（纵深防御）。
  const safeImageUri =
    typeof imageUri === "string" && /^(https?:|data:image\/)/iu.test(imageUri)
      ? imageUri
      : null;
  const [failedUri, setFailedUri] = useState<string | null>(null);
  const showImage = Boolean(safeImageUri) && failedUri !== safeImageUri;
  return (
    <View
      style={[
        styles.productCardImageFrame,
        // 提供 size 时渲染为方形缩略图（候选列表用），否则撑满卡片宽度
        size !== undefined && { height: size, width: size },
      ]}
      testID={testID}
    >
      {showImage ? (
        <Image
          accessible={false}
          onError={() => setFailedUri(safeImageUri)}
          resizeMode="cover"
          source={{ uri: safeImageUri as string }}
          style={[
            styles.productCardImage,
            size !== undefined && { height: size, width: size },
          ]}
          testID={`${testID}-content`}
        />
      ) : (
        <Text
          accessibilityElementsHidden
          importantForAccessibility="no-hide-descendants"
          style={styles.productCardImagePlaceholder}
          testID={`${testID}-placeholder`}
        >
          {placeholder}
        </Text>
      )}
    </View>
  );
}

function ActionButton({
  compact = false,
  danger = false,
  disabled = false,
  label,
  mini = false,
  onPress,
  sound,
  testID,
  tone = "primary",
}: Readonly<{
  compact?: boolean;
  danger?: boolean;
  disabled?: boolean;
  label: string;
  mini?: boolean;
  onPress(): void;
  sound?: "tap" | "navigate" | "danger";
  testID: string;
  tone?: "primary" | "quiet" | "secondary";
}>) {
  return (
    <PosPressable
      accessibilityRole="button"
      accessibilityState={{ disabled }}
      disabled={disabled}
      onPress={onPress}
      sound={sound ?? (danger ? "danger" : "tap")}
      style={({ pressed }) => [
        styles.button,
        compact && styles.buttonCompact,
        mini && styles.buttonMini,
        tone === "quiet" && styles.buttonQuiet,
        tone === "secondary" && styles.buttonSecondary,
        disabled && styles.buttonDisabled,
        pressed && !disabled && styles.buttonPressed,
      ]}
      testID={testID}
    >
      <Text
        style={[
          styles.buttonText,
          tone !== "primary" && styles.buttonTextDark,
          danger && styles.buttonTextDanger,
        ]}
      >
        {label}
      </Text>
    </PosPressable>
  );
}

function StatusBanner({
  statusCode,
  t,
}: Readonly<{
  statusCode: SpecialProductsStatusCode;
  t: SpecialProductsTranslate;
}>) {
  const failure =
    statusCode.endsWith("-failed") ||
    statusCode === "online-required" ||
    statusCode === "permission-required";
  return (
    <View
      accessibilityLiveRegion="polite"
      accessibilityRole={failure ? "alert" : undefined}
      style={[styles.statusBanner, failure && styles.statusBannerFailure]}
      testID="special-products-status"
    >
      <Text
        style={[
          styles.statusBannerText,
          failure && styles.statusBannerFailureText,
        ]}
      >
        {t(specialProductsStatusCopyKey(statusCode))}
      </Text>
    </View>
  );
}

function EmptyState({
  message,
  testID,
}: Readonly<{ message: string; testID: string }>) {
  return (
    <View style={styles.emptyState} testID={testID}>
      <Text style={styles.emptyStateText}>{message}</Text>
    </View>
  );
}

function formatAud(cents: number): string {
  return `$${(cents / 100).toFixed(2)}`;
}

const styles = StyleSheet.create({
  safeArea: {
    backgroundColor: posColors.canvas,
    flex: 1,
  },
  page: {
    flex: 1,
    paddingHorizontal: 16,
    paddingVertical: 8,
  },
  header: {
    alignItems: "flex-start",
    flexDirection: "column",
    gap: 8,
    justifyContent: "space-between",
  },
  titleGroup: {
    flex: 1,
    maxWidth: 760,
  },
  eyebrow: {
    color: posColors.blue,
    fontSize: 12,
    fontWeight: "800",
    letterSpacing: 1.1,
  },
  title: {
    color: posColors.ink,
    fontSize: 30,
    fontWeight: "800",
    marginTop: 5,
  },
  subtitle: {
    color: posColors.mutedInk,
    fontSize: 15,
    lineHeight: 22,
    marginTop: 6,
  },
  headerActions: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
    justifyContent: "flex-start",
    width: "100%",
  },
  offlineNote: {
    backgroundColor: posColors.blueSoft,
    borderColor: "#C7D9E8",
    borderRadius: 8,
    borderWidth: 1,
    marginTop: 14,
    minHeight: SPECIAL_PRODUCTS_MIN_TOUCH_TARGET,
    paddingHorizontal: 14,
    paddingVertical: 10,
  },
  offlineNoteText: {
    color: posColors.blue,
    fontSize: 14,
    fontWeight: "700",
    lineHeight: 20,
  },
  statusBanner: {
    backgroundColor: posColors.greenSoft,
    borderColor: posColors.green,
    borderRadius: 8,
    borderWidth: 1,
    marginTop: 10,
    minHeight: SPECIAL_PRODUCTS_MIN_TOUCH_TARGET,
    paddingHorizontal: 14,
    paddingVertical: 10,
  },
  statusBannerFailure: {
    backgroundColor: posColors.redSoft,
    borderColor: posColors.red,
  },
  statusBannerText: {
    color: posColors.green,
    fontSize: 14,
    fontWeight: "700",
  },
  statusBannerFailureText: {
    color: posColors.red,
  },
  workspace: {
    flex: 1,
    flexDirection: "column",
    marginTop: 8,
    minHeight: 0,
  },
  catalogPane: {
    backgroundColor: posColors.surface,
    borderColor: posColors.border,
    borderRadius: 10,
    borderWidth: 1,
    flex: 1,
    overflow: "hidden",
  },
  panelHeader: {
    alignItems: "center",
    borderBottomColor: posColors.border,
    borderBottomWidth: 1,
    flexDirection: "row",
    justifyContent: "space-between",
    minHeight: 66,
    paddingHorizontal: 18,
  },
  panelTitle: {
    color: posColors.ink,
    fontSize: 18,
    fontWeight: "800",
  },
  panelMeta: {
    color: posColors.mutedInk,
    fontSize: 12,
    fontWeight: "700",
    marginTop: 3,
  },
  productList: {
    padding: 8,
    paddingBottom: 16,
  },
  productCardRow: {
    gap: SPECIAL_PRODUCTS_GRID_GAP,
    marginBottom: SPECIAL_PRODUCTS_GRID_GAP,
  },
  productCard: {
    backgroundColor: posColors.surface,
    borderColor: posColors.border,
    borderRadius: 10,
    borderWidth: 1,
    // 两列固定占比避免末行卡片被拉伸，同时覆盖 320–430px 宽度。
    flexBasis: "48.5%",
    flexGrow: 0,
    flexShrink: 0,
    overflow: "hidden",
    width: "48.5%",
  },
  productCardMain: {
    minHeight: SPECIAL_PRODUCTS_MIN_TOUCH_TARGET,
    padding: 10,
  },
  productCardDragging: {
    backgroundColor: posColors.surface,
    elevation: 12,
    shadowColor: "#000000",
    shadowOffset: { height: 6, width: 0 },
    shadowOpacity: 0.25,
    shadowRadius: 12,
    zIndex: 10,
  },
  productCardDragTarget: {
    borderColor: posColors.orange,
    borderWidth: 2,
  },
  productCardImageFrame: {
    alignItems: "center",
    backgroundColor: posColors.blueSoft,
    borderRadius: 8,
    height: 88,
    justifyContent: "center",
    overflow: "hidden",
    width: "100%",
  },
  productCardImage: {
    height: 88,
    width: "100%",
  },
  productCardImagePlaceholder: {
    color: posColors.blue,
    fontSize: 32,
    fontWeight: "900",
  },
  productCardName: {
    color: posColors.ink,
    fontSize: 14,
    fontWeight: "800",
    lineHeight: 18,
    marginTop: 8,
    minHeight: 36,
  },
  productCardCode: {
    color: posColors.mutedInk,
    fontFamily: "Courier",
    fontSize: 11,
    marginTop: 4,
  },
  productCardPrice: {
    color: posColors.ink,
    fontSize: 17,
    fontVariant: ["tabular-nums"],
    fontWeight: "900",
    marginTop: 6,
  },
  productCardActions: {
    alignItems: "center",
    borderTopColor: posColors.border,
    borderTopWidth: 1,
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 4,
    justifyContent: "flex-start",
    paddingHorizontal: 4,
    paddingVertical: 6,
  },
  managementHint: {
    color: posColors.mutedInk,
    fontSize: 13,
    lineHeight: 19,
    marginTop: 7,
  },
  searchInput: {
    backgroundColor: posColors.canvas,
    borderColor: posColors.border,
    borderRadius: 7,
    borderWidth: 1,
    color: posColors.ink,
    fontSize: 15,
    marginTop: 16,
    minHeight: SPECIAL_PRODUCTS_MIN_TOUCH_TARGET,
    paddingHorizontal: 12,
    paddingVertical: 9,
  },
  candidateEmpty: {
    color: posColors.mutedInk,
    fontSize: 13,
    lineHeight: 20,
    marginTop: 18,
    textAlign: "center",
  },
  candidateList: {
    gap: 8,
    paddingTop: 14,
  },
  candidateRow: {
    alignItems: "center",
    backgroundColor: posColors.blueSoft,
    borderRadius: 7,
    flexDirection: "row",
    gap: 8,
    minHeight: 66,
    padding: 10,
  },
  candidateIdentity: {
    flex: 1,
  },
  candidateName: {
    color: posColors.ink,
    fontSize: 14,
    fontWeight: "800",
    lineHeight: 19,
  },
  candidateCode: {
    color: posColors.mutedInk,
    fontFamily: "Courier",
    fontSize: 11,
    marginTop: 3,
  },
  candidatePressed: {
    opacity: 0.7,
  },
  candidateAdd: {
    color: posColors.blue,
    fontSize: 14,
    fontWeight: "900",
  },
  modalOverlay: {
    alignItems: "center",
    backgroundColor: "rgba(16, 24, 40, 0.55)",
    flex: 1,
    justifyContent: "center",
    padding: 16,
  },
  modalPanel: {
    backgroundColor: posColors.surface,
    borderColor: posColors.border,
    borderRadius: 6,
    borderWidth: 1,
    maxHeight: "92%",
    maxWidth: 430,
    overflow: "hidden",
    width: "100%",
  },
  modalScroll: {
    flexGrow: 0,
  },
  modalPanelContent: {
    padding: 18,
  },
  modalHeader: {
    alignItems: "flex-start",
    flexDirection: "row",
    gap: 12,
    justifyContent: "space-between",
  },
  modalTitleGroup: {
    flex: 1,
  },
  modalSearchButton: {
    marginTop: 10,
  },
  button: {
    alignItems: "center",
    backgroundColor: posColors.orange,
    borderRadius: 6,
    justifyContent: "center",
    minHeight: SPECIAL_PRODUCTS_MIN_TOUCH_TARGET,
    paddingHorizontal: 14,
    paddingVertical: 9,
  },
  buttonCompact: {
    minWidth: SPECIAL_PRODUCTS_MIN_TOUCH_TARGET,
    paddingHorizontal: 10,
  },
  buttonMini: {
    minWidth: SPECIAL_PRODUCTS_MIN_TOUCH_TARGET,
    paddingHorizontal: 0,
  },
  buttonQuiet: {
    backgroundColor: posColors.surface,
    borderColor: posColors.border,
    borderWidth: 1,
  },
  buttonSecondary: {
    backgroundColor: posColors.blueSoft,
    borderColor: "#C7D9E8",
    borderWidth: 1,
  },
  buttonDisabled: {
    backgroundColor: "#D6D2CA",
    borderColor: "#D6D2CA",
  },
  buttonPressed: {
    opacity: 0.8,
  },
  buttonText: {
    color: "#FFFFFF",
    fontSize: 14,
    fontWeight: "800",
    textAlign: "center",
  },
  buttonTextDark: {
    color: posColors.ink,
  },
  buttonTextDanger: {
    color: posColors.red,
  },
  emptyState: {
    alignItems: "center",
    flex: 1,
    justifyContent: "center",
    padding: 28,
  },
  emptyStateText: {
    color: posColors.mutedInk,
    fontSize: 16,
    fontWeight: "700",
    textAlign: "center",
  },
  unavailable: {
    alignSelf: "center",
    backgroundColor: posColors.surface,
    borderColor: posColors.border,
    borderRadius: 10,
    borderWidth: 1,
    margin: 32,
    maxWidth: 560,
    padding: 28,
  },
  modalStateSurface: {
    alignItems: "center",
    justifyContent: "center",
    maxWidth: 430,
    width: "100%",
  },
  stateSurface: {
    flex: 1,
    minHeight: 0,
    width: "100%",
  },
  unavailableTitle: {
    color: posColors.ink,
    fontSize: 27,
    fontWeight: "800",
    marginTop: 8,
  },
  unavailableHint: {
    color: posColors.mutedInk,
    fontSize: 16,
    lineHeight: 24,
    marginBottom: 22,
    marginTop: 10,
  },
});
