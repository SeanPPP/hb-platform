import { StyleSheet, View } from "react-native";
import { Button, Searchbar, Text } from "react-native-paper";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

interface SearchPanelProps {
  value: string;
  loading?: boolean;
  lastHitLabel?: string;
  onChangeText: (value: string) => void;
  onSubmit: () => void;
  onClear: () => void;
}

export function SearchPanel({
  value,
  loading = false,
  lastHitLabel,
  onChangeText,
  onSubmit,
  onClear,
}: SearchPanelProps) {
  const { t } = useAppTranslation(["productQuery", "common"]);

  return (
    <View style={styles.container}>
      <Searchbar
        placeholder={t("search.placeholder")}
        value={value}
        onChangeText={onChangeText}
        onSubmitEditing={onSubmit}
        style={styles.searchbar}
        inputStyle={styles.input}
      />
      <View style={styles.actions}>
        <Button mode="contained" compact loading={loading} disabled={loading} onPress={onSubmit}>
          {t("common:actions.search")}
        </Button>
        <Button mode="text" compact onPress={onClear}>
          {t("common:actions.clear")}
        </Button>
      </View>
      {lastHitLabel ? (
        <Text variant="bodySmall" style={styles.lastHit}>
          {t("search.lastHit", { value: lastHitLabel })}
        </Text>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    paddingHorizontal: 16,
    paddingBottom: 8,
    gap: 8,
  },
  searchbar: {
    elevation: 0,
    borderRadius: 10,
    backgroundColor: "#F5F5F5",
  },
  input: {
    minHeight: 0,
    fontSize: 14,
  },
  actions: {
    flexDirection: "row",
    alignItems: "center",
    gap: 8,
  },
  lastHit: {
    color: "#666",
  },
});
