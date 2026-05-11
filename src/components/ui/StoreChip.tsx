import { StyleSheet, View } from "react-native";
import { Button, Text } from "react-native-paper";
import type { Store } from "@/modules/shop/types";

interface StoreChipProps {
  compact?: boolean;
  store: Store | null;
  onPress: () => void;
}

export function StoreChip({ compact = false, store, onPress }: StoreChipProps) {
  if (compact) {
    return (
      <Button
        mode="outlined"
        onPress={onPress}
        icon="storefront-outline"
        style={styles.compactButton}
        contentStyle={styles.compactButtonContent}
        labelStyle={styles.compactButtonLabel}
      >
        {store?.storeName ?? "选择门店"}
      </Button>
    );
  }

  return (
    <View style={styles.container}>
      <Text variant="labelMedium" style={styles.label}>
        当前门店
      </Text>
      <Button mode="outlined" onPress={onPress} icon="storefront-outline" contentStyle={styles.buttonContent}>
        {store?.storeName ?? "选择门店"}
      </Button>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    gap: 8,
  },
  label: {
    color: "#666",
  },
  buttonContent: {
    justifyContent: "space-between",
  },
  compactButton: {
    width: "100%",
  },
  compactButtonContent: {
    minHeight: 38,
    justifyContent: "flex-start",
  },
  compactButtonLabel: {
    marginVertical: 0,
  },
});
