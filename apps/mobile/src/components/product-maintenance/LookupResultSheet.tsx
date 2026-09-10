import { useState } from "react";
import { Image, Pressable, StyleSheet, View } from "react-native";
import { Button, Icon, Text } from "react-native-paper";
import { BusinessSheet } from "@/components/ui/BusinessSheet";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { HB_COLORS, HB_RADIUS } from "@/shared/theme/tokens";
import type { ProductLookupItem } from "@/modules/product-maintenance/types";

interface LookupResultSheetProps {
  visible: boolean;
  queryText: string;
  items: ProductLookupItem[];
  selectedValue?: string;
  onSelect: (productCode: string) => void;
  onClose: () => void;
  onConfirm: () => void;
}

function resolveSourceLabel(source: string | null | undefined, t: (key: string) => string) {
  switch (source) {
    case "ProductBarcode":
      return t("lookup.source.productBarcode");
    case "MultiBarcode":
      return t("lookup.source.multiBarcode");
    case "SetBarcode":
      return t("lookup.source.setBarcode");
    case "ClearanceBarcode":
      return t("lookup.source.clearanceBarcode");
    case "ItemNumber":
      return t("lookup.source.itemNumber");
    default:
      return source || t("lookup.source.fallback");
  }
}

function ProductThumbnail({ uri, label, fallback }: { uri?: string; label: string; fallback: string }) {
  const [failed, setFailed] = useState(false);

  return (
    <View style={styles.thumbnail}>
      {uri && !failed ? (
        <Image
          source={{ uri }}
          style={styles.image}
          resizeMode="contain"
          accessibilityLabel={label}
          onError={() => setFailed(true)}
        />
      ) : (
        <>
          <Icon source="image-off-outline" size={24} color={HB_COLORS.textSecondary} />
          <Text style={styles.imageFallback}>{fallback}</Text>
        </>
      )}
    </View>
  );
}

export function LookupResultSheet({
  visible,
  queryText,
  items,
  selectedValue,
  onSelect,
  onClose,
  onConfirm,
}: LookupResultSheetProps) {
  const { t } = useAppTranslation(["productQuery", "common"]);

  return (
    <BusinessSheet
      visible={visible}
      title={t("lookup.title")}
      subtitle={t("lookup.count", { count: items.length })}
      onDismiss={onClose}
      footer={
        <View style={styles.actions}>
          <Button
            mode="contained-tonal"
            onPress={onClose}
            buttonColor={HB_COLORS.surfaceMuted}
            textColor={HB_COLORS.textSecondary}
            style={styles.cancelButton}
            contentStyle={styles.buttonContent}
          >
            {t("common:actions.cancel")}
          </Button>
          <Button
            mode="contained"
            onPress={onConfirm}
            disabled={!selectedValue}
            buttonColor={HB_COLORS.action}
            style={styles.confirmButton}
            contentStyle={styles.buttonContent}
          >
            {t("lookup.viewDetail")}
          </Button>
        </View>
      }
    >
      <Text style={styles.query}>{t("lookup.query", { value: queryText })}</Text>
      {items.map((item) => {
        const selected = selectedValue === item.productCode;
        const name = item.productName || item.productCode;
        const imageUri = item.productImage?.trim() || undefined;
        const itemNumber = t("hero.itemNumber", { value: item.itemNumber || item.productCode });
        const barcode = t("hero.barcode", { value: item.barcode || item.matchValue || t("common:na") });
        const source = resolveSourceLabel(item.matchSource, t);

        return (
          <Pressable
            key={`${item.productCode}-${item.matchSource}-${item.barcode || item.itemNumber || ""}`}
            accessibilityRole="radio"
            accessibilityState={{ checked: selected }}
            accessibilityLabel={`${name}, ${itemNumber}, ${barcode}, ${source}`}
            onPress={() => onSelect(item.productCode)}
            style={({ pressed }) => [styles.item, selected && styles.selectedItem, pressed && styles.pressedItem]}
          >
            {/* 图片地址变化时重置失败状态，避免复用商品行后一直显示占位。 */}
            <ProductThumbnail key={imageUri || "no-image"} uri={imageUri} label={name} fallback={t("lookup.noImage")} />
            <View style={styles.itemBody}>
              <View style={styles.itemHeading}>
                <Text style={styles.productName} numberOfLines={2}>{name}</Text>
                <Icon
                  source={selected ? "check-circle" : "checkbox-blank-circle-outline"}
                  size={22}
                  color={selected ? HB_COLORS.action : HB_COLORS.outline}
                />
              </View>
              <Text style={styles.meta}>{itemNumber}</Text>
              <Text style={styles.meta}>{barcode}</Text>
              <View style={styles.sourceTag}>
                <Text style={styles.sourceText}>{source}</Text>
              </View>
            </View>
          </Pressable>
        );
      })}
    </BusinessSheet>
  );
}

const styles = StyleSheet.create({
  query: {
    color: HB_COLORS.textSecondary,
    fontSize: 13,
    lineHeight: 20,
    marginBottom: 4,
  },
  item: {
    flexDirection: "row",
    alignItems: "flex-start",
    gap: 12,
    padding: 14,
    borderRadius: HB_RADIUS.surface,
    borderWidth: 1,
    borderColor: HB_COLORS.outlineMuted,
    backgroundColor: HB_COLORS.white,
  },
  selectedItem: { borderColor: HB_COLORS.action, backgroundColor: `${HB_COLORS.brand}0D` },
  pressedItem: { opacity: 0.75 },
  thumbnail: {
    width: 72,
    height: 72,
    borderRadius: 10,
    borderWidth: StyleSheet.hairlineWidth,
    borderColor: HB_COLORS.outlineMuted,
    backgroundColor: HB_COLORS.surfaceMuted,
    alignItems: "center",
    justifyContent: "center",
    gap: 4,
    overflow: "hidden",
  },
  image: { width: "100%", height: "100%", backgroundColor: HB_COLORS.white },
  imageFallback: { fontSize: 11, lineHeight: 16, color: HB_COLORS.textSecondary, textAlign: "center" },
  itemBody: {
    flex: 1,
    minWidth: 0,
    gap: 4,
  },
  itemHeading: { flexDirection: "row", alignItems: "flex-start", gap: 8 },
  productName: { flex: 1, fontSize: 17, lineHeight: 23, fontWeight: "700", color: HB_COLORS.textPrimary },
  meta: {
    color: HB_COLORS.textSecondary,
    fontSize: 13,
    lineHeight: 20,
  },
  sourceTag: { alignSelf: "flex-start", borderRadius: 6, paddingHorizontal: 6, paddingVertical: 2, backgroundColor: HB_COLORS.surfaceMuted, marginTop: 2 },
  sourceText: { fontSize: 11, lineHeight: 16, color: HB_COLORS.textSecondary },
  actions: {
    flexDirection: "row",
    gap: 12,
  },
  cancelButton: { borderRadius: HB_RADIUS.surface, minWidth: 86 },
  confirmButton: { flex: 1, borderRadius: HB_RADIUS.surface },
  buttonContent: { minHeight: 48 },
});
