import { Pressable, StyleSheet, View } from "react-native";
import { IconButton, Text } from "react-native-paper";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

interface QueryHeaderProps {
  storeName?: string;
  canSelectStore?: boolean;
  onScanPress: () => void;
  onRefreshPress: () => void;
  onStorePress?: () => void;
  refreshing?: boolean;
}

export function QueryHeader({
  storeName,
  canSelectStore = false,
  onScanPress,
  onRefreshPress,
  onStorePress,
  refreshing = false,
}: QueryHeaderProps) {
  const { t } = useAppTranslation(["productQuery", "common"]);
  const storeLabel = t("currentStore", { store: storeName || t("common:na") });

  return (
    <View style={styles.container}>
      <Pressable
        accessibilityRole={canSelectStore ? "button" : undefined}
        accessibilityLabel={storeLabel}
        disabled={!canSelectStore}
        onPress={onStorePress}
        style={styles.storeButton}
      >
        <Text
          variant="bodySmall"
          style={[styles.storeLabel, canSelectStore ? styles.storeLabelSelectable : null]}
          numberOfLines={1}
        >
          {storeLabel}
        </Text>
      </Pressable>
      <View style={styles.actions}>
        <IconButton icon="barcode-scan" size={20} onPress={onScanPress} style={styles.iconButton} />
        <IconButton
          icon={refreshing ? "loading" : "refresh"}
          size={20}
          onPress={onRefreshPress}
          disabled={refreshing}
          style={styles.iconButton}
        />
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 8,
    paddingHorizontal: 12,
    paddingTop: 0,
    paddingBottom: 0,
  },
  storeLabel: {
    color: "#667085",
  },
  storeButton: {
    flex: 1,
  },
  storeLabelSelectable: {
    color: "#2563EB",
  },
  actions: {
    flexDirection: "row",
    alignItems: "center",
    gap: 0,
    marginRight: -6,
  },
  iconButton: {
    width: 32,
    height: 32,
    margin: 0,
  },
});
