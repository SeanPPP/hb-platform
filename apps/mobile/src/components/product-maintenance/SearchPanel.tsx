import { StyleSheet, View } from "react-native";
import { Button, IconButton, Searchbar, Text } from "react-native-paper";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

interface SearchPanelProps {
  value: string;
  loading?: boolean;
  lastHitLabel?: string;
  onChangeText: (value: string) => void;
  onSubmit: () => void;
  onClear: () => void;
  onOpenPrintSettings?: () => void;
}

export function SearchPanel({
  value,
  loading = false,
  lastHitLabel,
  onChangeText,
  onSubmit,
  onClear,
  onOpenPrintSettings,
}: SearchPanelProps) {
  const { t } = useAppTranslation(["productQuery", "common"]);

  return (
    <View style={styles.container}>
      <View style={styles.searchRow}>
        <Searchbar
          placeholder={t("search.placeholder")}
          value={value}
          onChangeText={onChangeText}
          onSubmitEditing={onSubmit}
          style={styles.searchbar}
          inputStyle={styles.input}
        />
        <IconButton
          accessibilityLabel={t("common:actions.search")}
          icon="magnify"
          mode="contained"
          size={20}
          loading={loading}
          disabled={loading}
          onPress={onSubmit}
          style={styles.iconButton}
        />
        <IconButton
          accessibilityLabel={t("common:actions.clear")}
          icon="close"
          mode="contained-tonal"
          size={18}
          disabled={loading && !value}
          onPress={onClear}
          style={styles.iconButton}
        />
        <IconButton
          icon="cog-outline"
          mode="contained-tonal"
          size={18}
          onPress={onOpenPrintSettings}
          style={styles.iconButton}
        />
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
    paddingHorizontal: 12,
    paddingBottom: 2,
    gap: 2,
  },
  searchRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: 2,
  },
  searchbar: {
    flex: 1,
    height: 36,
    elevation: 0,
    borderRadius: 8,
    backgroundColor: "#F5F5F5",
  },
  input: {
    alignSelf: "center",
    minHeight: 0,
    paddingBottom: 0,
    paddingTop: 0,
    fontSize: 12,
  },
  iconButton: {
    width: 32,
    height: 32,
    margin: 0,
    borderRadius: 8,
  },
  lastHit: {
    color: "#98A2B3",
  },
});
