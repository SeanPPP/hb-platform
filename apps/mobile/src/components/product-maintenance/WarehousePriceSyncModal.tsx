import { StyleSheet, View } from "react-native";
import { Button, Modal, Portal, Text } from "react-native-paper";
import type { WarehousePriceSyncResult } from "@/modules/product-maintenance/types";
import {
  calculateDiscountedRetailPrice,
  formatWarehouseDiscountRate,
} from "@/modules/product-maintenance/warehouse-price-sync";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

interface WarehousePriceSyncModalProps {
  visible: boolean;
  productName?: string | null;
  productCode?: string | null;
  snapshot: WarehousePriceSyncResult | null;
  loading?: boolean;
  errorMessage?: string | null;
  onCancel: () => void;
  onConfirm: () => void;
}

function formatMoney(value?: number | null) {
  return value == null || !Number.isFinite(value) ? "--" : value.toFixed(2);
}

export function WarehousePriceSyncModal({
  visible,
  productName,
  productCode,
  snapshot,
  loading = false,
  errorMessage,
  onCancel,
  onConfirm,
}: WarehousePriceSyncModalProps) {
  const { t } = useAppTranslation("productQuery");
  const discountRate = snapshot?.storePrice
    ? snapshot.storePrice.discountRate
    : snapshot?.discountRate;
  const previousDiscountedRetailPrice =
    snapshot?.previousDiscountedRetailPrice ??
    calculateDiscountedRetailPrice(snapshot?.previousStoreRetailPrice, discountRate);
  const newDiscountedRetailPrice =
    snapshot?.newDiscountedRetailPrice ??
    calculateDiscountedRetailPrice(snapshot?.warehouseRetailPrice, discountRate);

  return (
    <Portal>
      <Modal
        visible={visible}
        onDismiss={loading ? undefined : onCancel}
        contentContainerStyle={styles.modal}
      >
        <View style={styles.content} accessibilityViewIsModal>
          <View style={styles.header}>
            <Text variant="titleMedium" style={styles.title}>
              {t("warehousePriceSync.title")}
            </Text>
            <Text variant="bodySmall" style={styles.product} selectable>
              {t("warehousePriceSync.product", {
                name: productName || t("hero.unnamedProduct"),
                code: productCode || "--",
              })}
            </Text>
          </View>

          <View style={styles.priceComparison}>
            <View style={styles.priceColumn}>
              <Text variant="labelSmall" style={styles.label}>
                {t("warehousePriceSync.currentRetail")}
              </Text>
              <Text variant="headlineSmall" style={styles.currentPrice} selectable>
                {formatMoney(snapshot?.previousStoreRetailPrice ?? snapshot?.storePrice?.retailPrice)}
              </Text>
            </View>
            <Text variant="titleMedium" style={styles.arrow} accessibilityLabel={t("warehousePriceSync.toWarehousePrice")}>
              →
            </Text>
            <View style={styles.priceColumn}>
              <Text variant="labelSmall" style={styles.label}>
                {t("warehousePriceSync.warehouseRetail")}
              </Text>
              <Text variant="headlineSmall" style={styles.nextPrice} selectable>
                {formatMoney(snapshot?.warehouseRetailPrice)}
              </Text>
            </View>
          </View>

          <View style={styles.summary}>
            <View style={styles.summaryRow}>
              <Text variant="bodySmall" style={styles.summaryLabel}>
                {t("warehousePriceSync.preservedDiscount")}
              </Text>
              <Text variant="bodyMedium" style={styles.summaryValue} selectable>
                {formatWarehouseDiscountRate(discountRate)}
              </Text>
            </View>
            <View style={styles.summaryRow}>
              <Text variant="bodySmall" style={styles.summaryLabel}>
                {t("warehousePriceSync.discountedRetail")}
              </Text>
              <Text variant="bodyMedium" style={styles.summaryValue} selectable>
                {formatMoney(previousDiscountedRetailPrice)} → {formatMoney(newDiscountedRetailPrice)}
              </Text>
            </View>
          </View>

          <Text variant="bodySmall" style={styles.hint} selectable>
            {t("warehousePriceSync.hint")}
          </Text>

          {errorMessage ? (
            <View style={styles.errorBlock}>
              <Text variant="bodySmall" style={styles.errorText} selectable>
                {errorMessage}
              </Text>
            </View>
          ) : null}

          <View style={styles.actions}>
            <Button mode="outlined" onPress={onCancel} disabled={loading} style={styles.actionButton}>
              {t("warehousePriceSync.cancel")}
            </Button>
            <Button
              mode="contained"
              onPress={onConfirm}
              loading={loading}
              disabled={loading || !snapshot}
              style={styles.actionButton}
            >
              {t("warehousePriceSync.confirm")}
            </Button>
          </View>
        </View>
      </Modal>
    </Portal>
  );
}

const styles = StyleSheet.create({
  modal: {
    marginHorizontal: 18,
    borderRadius: 16,
    backgroundColor: "#FFFFFF",
    padding: 16,
  },
  content: {
    gap: 14,
  },
  header: {
    gap: 3,
  },
  title: {
    color: "#111827",
    fontWeight: "700",
  },
  product: {
    color: "#667085",
  },
  priceComparison: {
    flexDirection: "row",
    alignItems: "center",
    gap: 8,
    borderRadius: 12,
    backgroundColor: "#F8FAFC",
    paddingHorizontal: 12,
    paddingVertical: 14,
  },
  priceColumn: {
    flex: 1,
    gap: 2,
  },
  label: {
    color: "#667085",
  },
  currentPrice: {
    color: "#475467",
    fontWeight: "700",
    fontVariant: ["tabular-nums"],
  },
  nextPrice: {
    color: "#166534",
    fontWeight: "700",
    fontVariant: ["tabular-nums"],
  },
  arrow: {
    color: "#98A2B3",
  },
  summary: {
    gap: 8,
  },
  summaryRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 12,
  },
  summaryLabel: {
    color: "#667085",
  },
  summaryValue: {
    color: "#111827",
    fontWeight: "700",
    fontVariant: ["tabular-nums"],
  },
  hint: {
    color: "#475467",
  },
  errorBlock: {
    borderRadius: 10,
    backgroundColor: "#FEF3F2",
    paddingHorizontal: 10,
    paddingVertical: 8,
  },
  errorText: {
    color: "#B42318",
  },
  actions: {
    flexDirection: "row",
    justifyContent: "flex-end",
    gap: 8,
  },
  actionButton: {
    minWidth: 108,
  },
});
