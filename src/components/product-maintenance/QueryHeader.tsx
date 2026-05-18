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
      <View style={styles.titleWrap}>
        <Text variant="titleMedium" style={styles.title}>
          {t("title")}
        </Text>
        <Text variant="bodySmall" style={styles.subtitle}>
          {t("currentStore", { store: storeName || t("common:na") })}
        </Text>
      </View>
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
    gap: 12,
    paddingHorizontal: 16,
    paddingTop: 4,
    paddingBottom: 2,
  },
  titleWrap: {
    flex: 1,
    gap: 0,
  },
  title: {
    fontWeight: "700",
  },
  subtitle: {
    color: "#666",
  },
  actions: {
    flexDirection: "row",
    alignItems: "center",
    gap: 2,
    marginRight: -4,
  },
  iconButton: {
    width: 36,
    height: 36,
    margin: 0,
  },
});
