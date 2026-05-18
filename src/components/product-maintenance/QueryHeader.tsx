import { StyleSheet, View } from "react-native";
import { IconButton, Text } from "react-native-paper";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

interface QueryHeaderProps {
  storeName?: string;
  onScanPress: () => void;
  onRefreshPress: () => void;
  refreshing?: boolean;
}

export function QueryHeader({
  storeName,
  onScanPress,
  onRefreshPress,
  refreshing = false,
}: QueryHeaderProps) {
  const { t } = useAppTranslation(["productQuery", "common"]);

  return (
    <View style={styles.container}>
      <Text variant="bodySmall" style={styles.storeLabel} numberOfLines={1}>
        {t("currentStore", { store: storeName || t("common:na") })}
      </Text>
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
    paddingTop: 2,
    paddingBottom: 0,
  },
  storeLabel: {
    flex: 1,
    color: "#667085",
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
