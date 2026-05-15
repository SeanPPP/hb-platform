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
        <Text variant="titleLarge" style={styles.title}>
          {t("title")}
        </Text>
        <Text variant="bodySmall" style={styles.subtitle}>
          {t("currentStore", { store: storeName || t("common:na") })}
        </Text>
      </View>
      <View style={styles.actions}>
        <IconButton icon="barcode-scan" size={20} onPress={onScanPress} />
        <IconButton
          icon={refreshing ? "loading" : "refresh"}
          size={20}
          onPress={onRefreshPress}
          disabled={refreshing}
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
    paddingTop: 8,
    paddingBottom: 6,
  },
  titleWrap: {
    flex: 1,
    gap: 2,
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
    marginRight: -8,
  },
});
