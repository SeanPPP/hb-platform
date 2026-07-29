import { useEffect, useSyncExternalStore } from "react";
import {
  ActivityIndicator,
  FlatList,
  Pressable,
  StyleSheet,
  Text,
  TextInput,
  View,
} from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";

import {
  type SpecialProductsPresenter,
  type SpecialProductsStatusCode,
  type SpecialProductsState,
} from "./special-products-presenter";

import type { SpecialProductItem } from "@/core/contracts";
import { posColors } from "@/ui/theme";


export const SPECIAL_PRODUCTS_MIN_TOUCH_TARGET = 44;

type SpecialProductsScreenProps = Readonly<{
  onBack?(): void;
  presenter: SpecialProductsScreenPresenter;
}>;

export type SpecialProductsScreenPresenter = Pick<
  SpecialProductsPresenter,
  | "addToCart"
  | "download"
  | "getState"
  | "load"
  | "mark"
  | "reorder"
  | "searchCandidates"
  | "setSearchQuery"
  | "subscribe"
> & Readonly<{ getState(): SpecialProductsState }>;

/**
 * 横屏操作台：左侧始终保留可离线读取、加购的本地商品，右侧仅在 Manage
 * 权限存在时显示维护工具，避免把在线写操作伪装成本地可用。
 */
export function SpecialProductsScreen({
  onBack,
  presenter,
}: SpecialProductsScreenProps) {
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
            <Text style={styles.eyebrow}>门店运营 / STORE OPERATIONS</Text>
            <Text style={styles.title}>特殊商品 / Special products</Text>
            <Text style={styles.subtitle}>
              本地列表可离线浏览与加购；标记、取消、下载和排序需要在线。
              / Browse and add cached items offline; management changes require online access.
            </Text>
          </View>
          <View style={styles.headerActions}>
            {onBack ? (
              <ActionButton
                label="返回 / Back"
                onPress={onBack}
                testID="special-products-back"
                tone="quiet"
              />
            ) : null}
            <ActionButton
              disabled={state.busy || state.kind === "loading"}
              label="刷新本地 / Refresh local"
              onPress={() => void presenter.load()}
              testID="special-products-refresh-local"
              tone="secondary"
            />
            {state.access.canManage ? (
              <ActionButton
                disabled={!state.online || state.busy}
                label={
                  state.busy
                    ? "处理中… / Working…"
                    : "下载更新 / Download"
                }
                onPress={() => void presenter.download()}
                testID="special-products-download"
              />
            ) : null}
          </View>
        </View>

        {!state.online ? (
          <View
            accessibilityLiveRegion="polite"
            style={styles.offlineNote}
            testID="special-products-offline-note"
          >
            <Text style={styles.offlineNoteText}>
              离线模式：本地浏览与加购可用，管理写操作已锁定。/ Offline: local browsing and cart access remain available.
            </Text>
          </View>
        ) : null}

        {state.statusCode ? (
          <StatusBanner statusCode={state.statusCode} />
        ) : null}

        <View style={styles.workspace}>
          <View style={styles.catalogPane}>
            <View style={styles.panelHeader}>
              <View>
                <Text style={styles.panelTitle}>本地特殊商品 / Local list</Text>
                <Text style={styles.panelMeta}>
                  {state.items.length} 项 / items
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
                message="没有查看权限 / View permission required"
                testID="special-products-unauthorized"
              />
            ) : null}
            {state.kind === "failed" && state.items.length === 0 ? (
              <EmptyState
                message="本地列表读取失败 / Local list unavailable"
                testID="special-products-failed"
              />
            ) : null}
            {state.kind === "ready" && state.items.length === 0 ? (
              <EmptyState
                message="暂无特殊商品 / No special products"
                testID="special-products-empty"
              />
            ) : null}
            {state.items.length > 0 ? (
              <FlatList
                contentContainerStyle={styles.productList}
                data={state.items}
                keyExtractor={(item) => item.productCode}
                renderItem={({ item, index }) => (
                  <SpecialProductRow
                    canAddToCart={state.access.canAddToCart}
                    canManage={state.access.canManage}
                    disabled={state.busy}
                    index={index}
                    item={item}
                    itemCount={state.items.length}
                    online={state.online}
                    onAddToCart={() => void presenter.addToCart(item.productCode)}
                    onMoveDown={() => void presenter.reorder(item.productCode, 1)}
                    onMoveUp={() => void presenter.reorder(item.productCode, -1)}
                    onRemove={() => void presenter.mark(item.productCode, false)}
                  />
                )}
                testID="special-products-list"
              />
            ) : null}
          </View>

          {state.access.canManage ? (
            <View
              style={styles.managementPane}
              testID="special-products-management"
            >
              <Text style={styles.panelTitle}>添加商品 / Add product</Text>
              <Text style={styles.managementHint}>
                候选来自本地目录；真正标记时仍需在线。/ Candidates are local; marking still requires online access.
              </Text>
              <TextInput
                accessibilityLabel="搜索本地商品 / Search local products"
                autoCapitalize="none"
                autoCorrect={false}
                editable={!state.busy}
                onChangeText={(query) => presenter.setSearchQuery(query)}
                onSubmitEditing={() => void presenter.searchCandidates()}
                placeholder="名称、条码或商品码 / Name, barcode or code"
                placeholderTextColor="#7B8793"
                selectionColor={posColors.orange}
                style={styles.searchInput}
                testID="special-products-search-input"
                value={state.searchQuery}
              />
              <ActionButton
                disabled={state.busy || state.searching}
                label={
                  state.searching
                    ? "搜索中… / Searching…"
                    : "搜索本地目录 / Search local"
                }
                onPress={() => void presenter.searchCandidates()}
                testID="special-products-search"
                tone="secondary"
              />

              {state.candidates.length === 0 ? (
                <Text style={styles.candidateEmpty}>
                  输入关键词查找可标记商品。/ Enter a query to find candidates.
                </Text>
              ) : (
                <FlatList
                  contentContainerStyle={styles.candidateList}
                  data={state.candidates}
                  keyExtractor={(item) => item.productCode}
                  renderItem={({ item }) => (
                    <View
                      style={styles.candidateRow}
                      testID={`special-products-candidate-${item.productCode}`}
                    >
                      <View style={styles.candidateIdentity}>
                        <Text numberOfLines={2} style={styles.candidateName}>
                          {item.displayName}
                        </Text>
                        <Text numberOfLines={1} style={styles.productCode}>
                          {item.lookupCode || item.productCode}
                        </Text>
                      </View>
                      <ActionButton
                        compact
                        disabled={!state.online || state.busy}
                        label="标记 / Add"
                        onPress={() => void presenter.mark(item.productCode, true)}
                        testID={`special-products-mark-${item.productCode}`}
                      />
                    </View>
                  )}
                  keyboardShouldPersistTaps="handled"
                  testID="special-products-candidates"
                />
              )}
            </View>
          ) : null}
        </View>
      </View>
    </SafeAreaView>
  );
}

export function SpecialProductsUnavailableScreen({
  onBack,
}: Readonly<{ onBack(): void }>) {
  return (
    <SafeAreaView
      style={styles.safeArea}
      testID="special-products-runtime-unavailable"
    >
      <View style={styles.unavailable}>
        <Text style={styles.eyebrow}>SPECIAL PRODUCTS</Text>
        <Text style={styles.unavailableTitle}>
          功能暂不可用 / Feature unavailable
        </Text>
        <Text style={styles.unavailableHint}>
          本机运行时尚未提供特殊商品服务，请返回销售页。/ The local runtime has not provided this service.
        </Text>
        <ActionButton
          label="返回销售页 / Back to sales"
          onPress={onBack}
          testID="special-products-unavailable-back"
        />
      </View>
    </SafeAreaView>
  );
}

function SpecialProductRow({
  canAddToCart,
  canManage,
  disabled,
  index,
  item,
  itemCount,
  online,
  onAddToCart,
  onMoveDown,
  onMoveUp,
  onRemove,
}: Readonly<{
  canAddToCart: boolean;
  canManage: boolean;
  disabled: boolean;
  index: number;
  item: SpecialProductItem;
  itemCount: number;
  online: boolean;
  onAddToCart(): void;
  onMoveDown(): void;
  onMoveUp(): void;
  onRemove(): void;
}>) {
  const managementDisabled = disabled || !online;
  return (
    <View
      style={styles.productRow}
      testID={`special-product-row-${item.productCode}`}
    >
      <View style={styles.orderBadge}>
        <Text style={styles.orderBadgeText}>{index + 1}</Text>
      </View>
      <View style={styles.productIdentity}>
        <Text numberOfLines={2} style={styles.productName}>
          {item.displayName}
        </Text>
        <Text numberOfLines={1} style={styles.productCode}>
          {item.lookupCode || item.productCode}
        </Text>
      </View>
      <Text style={styles.productPrice}>
        {formatAud(item.retailPriceCents)}
      </Text>
      <View style={styles.productActions}>
        {canAddToCart ? (
          <ActionButton
            compact
            disabled={disabled}
            label="加购 / Add"
            onPress={onAddToCart}
            testID={`special-products-add-${item.productCode}`}
          />
        ) : null}
        {canManage ? (
          <>
            <ActionButton
              compact
              disabled={managementDisabled || index === 0}
              label="↑"
              onPress={onMoveUp}
              testID={`special-products-move-up-${item.productCode}`}
              tone="quiet"
            />
            <ActionButton
              compact
              disabled={managementDisabled || index === itemCount - 1}
              label="↓"
              onPress={onMoveDown}
              testID={`special-products-move-down-${item.productCode}`}
              tone="quiet"
            />
            <ActionButton
              compact
              danger
              disabled={managementDisabled}
              label="取消 / Remove"
              onPress={onRemove}
              testID={`special-products-remove-${item.productCode}`}
              tone="quiet"
            />
          </>
        ) : null}
      </View>
    </View>
  );
}

function ActionButton({
  compact = false,
  danger = false,
  disabled = false,
  label,
  onPress,
  testID,
  tone = "primary",
}: Readonly<{
  compact?: boolean;
  danger?: boolean;
  disabled?: boolean;
  label: string;
  onPress(): void;
  testID: string;
  tone?: "primary" | "quiet" | "secondary";
}>) {
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityState={{ disabled }}
      disabled={disabled}
      onPress={onPress}
      style={({ pressed }) => [
        styles.button,
        compact && styles.buttonCompact,
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
    </Pressable>
  );
}

function StatusBanner({
  statusCode,
}: Readonly<{ statusCode: SpecialProductsStatusCode }>) {
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
        {STATUS_COPY[statusCode]}
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

const STATUS_COPY: Readonly<Record<SpecialProductsStatusCode, string>> = {
  "added-to-cart": "已加入购物车 / Added to cart",
  "add-to-cart-failed": "加购未完成 / Could not add to cart",
  "download-complete": "下载完成 / Download complete",
  "download-failed": "下载未完成 / Download did not complete",
  "load-failed": "本地列表读取失败 / Local list could not be read",
  "mark-complete": "特殊商品标记已更新 / Special product updated",
  "mark-failed": "标记未完成 / Update did not complete",
  "online-required": "此管理操作需要在线 / This management action requires online access",
  "permission-required": "当前收银员没有所需权限 / Required permission is missing",
  "reorder-complete": "本地顺序已保存 / Local order saved",
  "reorder-failed": "排序未保存 / Order was not saved",
  "search-failed": "本地候选搜索失败 / Local candidate search failed",
};

const styles = StyleSheet.create({
  safeArea: {
    backgroundColor: posColors.canvas,
    flex: 1,
  },
  page: {
    flex: 1,
    paddingHorizontal: 28,
    paddingVertical: 22,
  },
  header: {
    alignItems: "flex-start",
    flexDirection: "row",
    gap: 20,
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
    justifyContent: "flex-end",
    maxWidth: 520,
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
    flexDirection: "row",
    gap: 18,
    marginTop: 16,
    minHeight: 360,
  },
  catalogPane: {
    backgroundColor: posColors.surface,
    borderColor: posColors.border,
    borderRadius: 10,
    borderWidth: 1,
    flex: 1.5,
    overflow: "hidden",
  },
  managementPane: {
    backgroundColor: posColors.surface,
    borderColor: posColors.border,
    borderRadius: 10,
    borderWidth: 1,
    flex: 0.85,
    padding: 18,
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
    padding: 12,
  },
  productRow: {
    alignItems: "center",
    borderBottomColor: posColors.border,
    borderBottomWidth: 1,
    flexDirection: "row",
    gap: 12,
    minHeight: 76,
    paddingHorizontal: 8,
    paddingVertical: 9,
  },
  orderBadge: {
    alignItems: "center",
    backgroundColor: posColors.blueSoft,
    borderRadius: 6,
    height: 32,
    justifyContent: "center",
    width: 32,
  },
  orderBadgeText: {
    color: posColors.blue,
    fontSize: 13,
    fontWeight: "900",
  },
  productIdentity: {
    flex: 1,
    minWidth: 120,
  },
  productName: {
    color: posColors.ink,
    fontSize: 16,
    fontWeight: "800",
    lineHeight: 21,
  },
  productCode: {
    color: posColors.mutedInk,
    fontFamily: "Courier",
    fontSize: 12,
    marginTop: 3,
  },
  productPrice: {
    color: posColors.ink,
    fontSize: 17,
    fontVariant: ["tabular-nums"],
    fontWeight: "900",
    minWidth: 74,
    textAlign: "right",
  },
  productActions: {
    alignItems: "center",
    flexDirection: "row",
    gap: 6,
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
  button: {
    alignItems: "center",
    backgroundColor: posColors.orange,
    borderRadius: 7,
    justifyContent: "center",
    minHeight: SPECIAL_PRODUCTS_MIN_TOUCH_TARGET,
    paddingHorizontal: 14,
    paddingVertical: 9,
  },
  buttonCompact: {
    minWidth: SPECIAL_PRODUCTS_MIN_TOUCH_TARGET,
    paddingHorizontal: 10,
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
    marginTop: 10,
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
