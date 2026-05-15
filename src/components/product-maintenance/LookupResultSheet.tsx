import { ScrollView, StyleSheet, View } from "react-native";
import { Button, Modal, Portal, RadioButton, Text } from "react-native-paper";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
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
    case "ItemNumber":
    case "SetItemNumber":
      return t("lookup.source.itemNumber");
    case "ProductCode":
    case "SetProductCode":
    case "MultiCodeProductCode":
    case "StoreMultiCodeProductCode":
      return t("lookup.source.productCode");
    default:
      return source || t("lookup.source.fallback");
  }
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
    <Portal>
      <Modal visible={visible} onDismiss={onClose} contentContainerStyle={styles.modal}>
        <Text variant="titleMedium" style={styles.title}>
          {t("lookup.title")}
        </Text>
        <Text variant="bodySmall" style={styles.subtitle}>
          {t("lookup.query", { value: queryText })}
        </Text>
        <ScrollView style={styles.list}>
          {items.map((item) => (
            <View
              key={`${item.productCode}-${item.matchSource}-${item.barcode || item.itemNumber || ""}`}
              style={styles.item}
            >
              <RadioButton
                value={item.productCode}
                status={selectedValue === item.productCode ? "checked" : "unchecked"}
                onPress={() => onSelect(item.productCode)}
              />
              <View style={styles.itemBody}>
                <Text variant="titleSmall">{item.productName || item.productCode}</Text>
                <Text variant="bodySmall" style={styles.meta}>
                  {item.itemNumber || item.productCode} · {resolveSourceLabel(item.matchSource, t)}
                </Text>
                <Text variant="bodySmall" style={styles.meta}>
                  {item.barcode || item.matchValue || t("common:na")}
                </Text>
              </View>
            </View>
          ))}
        </ScrollView>
        <View style={styles.actions}>
          <Button onPress={onClose}>{t("common:actions.cancel")}</Button>
          <Button mode="contained" onPress={onConfirm} disabled={!selectedValue}>
            {t("lookup.viewDetail")}
          </Button>
        </View>
      </Modal>
    </Portal>
  );
}

const styles = StyleSheet.create({
  modal: {
    marginHorizontal: 16,
    padding: 16,
    borderRadius: 14,
    backgroundColor: "#fff",
    maxHeight: "75%",
  },
  title: {
    fontWeight: "700",
  },
  subtitle: {
    color: "#666",
    marginTop: 4,
    marginBottom: 12,
  },
  list: {
    maxHeight: 360,
  },
  item: {
    flexDirection: "row",
    alignItems: "flex-start",
    gap: 6,
    paddingVertical: 8,
    borderBottomWidth: StyleSheet.hairlineWidth,
    borderBottomColor: "#E3E3E3",
  },
  itemBody: {
    flex: 1,
    gap: 2,
    paddingTop: 5,
  },
  meta: {
    color: "#666",
  },
  actions: {
    flexDirection: "row",
    justifyContent: "flex-end",
    gap: 8,
    marginTop: 12,
  },
});
