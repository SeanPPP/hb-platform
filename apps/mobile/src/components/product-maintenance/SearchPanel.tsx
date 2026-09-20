import { StyleSheet, View } from "react-native";
import { IconButton, Searchbar, Text } from "react-native-paper";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { HB_COLORS, HB_RADIUS, HB_SPACING } from "@/shared/theme/tokens";

interface SearchPanelProps {
  value: string;
  loading?: boolean;
  lastHitLabel?: string;
  refreshing?: boolean;
  onChangeText: (value: string) => void;
  onFocus: () => void;
  onBlur: () => void;
  onSubmit: () => void;
  onClear: () => void;
  onScanPress?: () => void;
  onRefreshPress?: () => void;
  onOpenPrintSettings?: () => void;
  /** 已查到商品时，新建商品收进顶部一行为图标按钮，把首屏整行让给商品信息。 */
  onCreateProduct?: () => void;
  createProductDisabled?: boolean;
}

/** 顶部一行：搜索框（左扫码、右清空）+ 搜索 + 刷新 + 打印设置。 */
export function SearchPanel({
  value,
  loading = false,
  lastHitLabel,
  refreshing = false,
  onChangeText,
  onFocus,
  onBlur,
  onSubmit,
  onClear,
  onScanPress,
  onRefreshPress,
  onOpenPrintSettings,
  onCreateProduct,
  createProductDisabled = false,
}: SearchPanelProps) {
  const { t } = useAppTranslation(["productQuery", "common"]);

  return (
    <View style={styles.container}>
      <View style={styles.searchRow}>
        <Searchbar
          placeholder={t("search.placeholder")}
          value={value}
          onChangeText={onChangeText}
          onFocus={onFocus}
          onBlur={onBlur}
          onSubmitEditing={onSubmit}
          icon="barcode-scan"
          iconColor={HB_COLORS.action}
          onIconPress={onScanPress}
          searchAccessibilityLabel={t("search.scan")}
          // 清空还会清掉当前商品详情，因此关键字为空时也要保留 ×，不能用 Paper 自带的按值显示清除按钮。
          right={() => (
            <IconButton
              accessibilityLabel={t("common:actions.clear")}
              icon="close"
              size={18}
              disabled={loading && !value}
              onPress={onClear}
              style={styles.clearButton}
            />
          )}
          style={styles.searchbar}
          inputStyle={styles.input}
        />
        <IconButton
          accessibilityLabel={t("common:actions.search")}
          icon="magnify"
          mode="contained"
          containerColor="#EAF2FF"
          iconColor={HB_COLORS.action}
          size={20}
          loading={loading}
          disabled={loading}
          onPress={onSubmit}
          style={styles.searchButton}
        />
        <IconButton
          accessibilityLabel={t("common:actions.refresh")}
          icon={refreshing ? "loading" : "refresh"}
          size={20}
          disabled={refreshing || !onRefreshPress}
          onPress={onRefreshPress}
          style={styles.iconButton}
        />
        <IconButton
          accessibilityLabel={t("print.settingsTitle")}
          icon="cog-outline"
          size={20}
          onPress={onOpenPrintSettings}
          style={styles.iconButton}
        />
        {onCreateProduct ? (
          <IconButton
            accessibilityLabel={t("createProduct.action")}
            icon="plus"
            mode="contained-tonal"
            size={20}
            disabled={createProductDisabled}
            onPress={onCreateProduct}
            style={styles.iconButton}
          />
        ) : null}
      </View>
      {lastHitLabel ? (
        <Text variant="labelSmall" style={styles.lastHit} numberOfLines={1}>
          {t("search.lastHit", { value: lastHitLabel })}
        </Text>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    paddingHorizontal: HB_SPACING.sm,
    paddingTop: HB_SPACING.xxs,
    paddingBottom: HB_SPACING.xs,
    gap: HB_SPACING.xxs,
  },
  searchRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: 2,
  },
  searchbar: {
    flex: 1,
    height: 44,
    elevation: 0,
    borderRadius: HB_RADIUS.control,
    borderWidth: 1,
    borderColor: HB_COLORS.outline,
    backgroundColor: HB_COLORS.white,
  },
  input: {
    alignSelf: "center",
    minHeight: 0,
    paddingBottom: 0,
    paddingTop: 0,
    fontSize: 14,
  },
  clearButton: {
    margin: 0,
    marginRight: 2,
  },
  searchButton: {
    width: 44,
    height: 44,
    margin: 0,
    marginLeft: HB_SPACING.xxs,
    borderRadius: HB_RADIUS.control,
  },
  iconButton: {
    width: 36,
    height: 44,
    margin: 0,
  },
  lastHit: {
    color: "#98A2B3",
    paddingHorizontal: HB_SPACING.xxs,
  },
});
