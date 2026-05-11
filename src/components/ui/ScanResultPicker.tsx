import { ScrollView, StyleSheet, View } from "react-native";
import { Button, Dialog, Portal, Text } from "react-native-paper";
import type { StoreOrderProductItem } from "@/modules/shop/types";

interface ScanResultPickerProps {
  visible: boolean;
  barcode?: string;
  items: StoreOrderProductItem[];
  onDismiss: () => void;
  onSelect: (product: StoreOrderProductItem) => void | Promise<void>;
  selectLabel?: string;
  title?: string;
  tip?: string;
}

export function ScanResultPicker({
  visible,
  barcode,
  items,
  onDismiss,
  onSelect,
  selectLabel = "选择",
  title = "选择要加入的商品",
  tip,
}: ScanResultPickerProps) {
  return (
    <Portal>
      <Dialog visible={visible} onDismiss={onDismiss}>
        <Dialog.Title>{title}</Dialog.Title>
        <Dialog.Content>
          <Text variant="bodyMedium" style={styles.tip}>
            {tip ?? `条码 ${barcode || "--"} 匹配到了多个商品，请选择一个继续。`}
          </Text>
          <ScrollView style={styles.list}>
            {items.map((item) => (
              <View key={item.productCode} style={styles.item}>
                <View style={styles.itemContent}>
                  <Text variant="titleSmall">{item.productName || item.productCode}</Text>
                  <Text variant="bodySmall" style={styles.secondaryText}>
                    货号: {item.itemNumber || "--"}
                  </Text>
                  <Text variant="bodySmall" style={styles.secondaryText}>
                    起订量: {item.minOrderQuantity || 1}
                  </Text>
                </View>
                <Button mode="contained-tonal" onPress={() => void onSelect(item)}>
                  {selectLabel}
                </Button>
              </View>
            ))}
          </ScrollView>
        </Dialog.Content>
        <Dialog.Actions>
          <Button onPress={onDismiss}>取消</Button>
        </Dialog.Actions>
      </Dialog>
    </Portal>
  );
}

const styles = StyleSheet.create({
  tip: {
    color: "#666",
    marginBottom: 12,
  },
  list: {
    maxHeight: 320,
  },
  item: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 12,
    paddingVertical: 10,
    borderBottomWidth: StyleSheet.hairlineWidth,
    borderBottomColor: "#ddd",
  },
  itemContent: {
    flex: 1,
    gap: 4,
  },
  secondaryText: {
    color: "#666",
  },
});
