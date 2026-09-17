import { StyleSheet, View } from "react-native";
import { Button, Surface, Text } from "react-native-paper";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

interface StickyActionBarProps {
  visible: boolean;
  dirtyCount: number;
  saving?: boolean;
  onReset: () => void;
  onSaveAll: () => void;
  onSaveAndReturn?: () => void;
}

export function StickyActionBar({
  visible,
  dirtyCount,
  saving = false,
  onReset,
  onSaveAll,
  onSaveAndReturn,
}: StickyActionBarProps) {
  const { t } = useAppTranslation(["productQuery", "common"]);

  if (!visible) {
    return null;
  }

  return (
    <Surface style={styles.container} elevation={2}>
      <View style={styles.primaryRow}>
        <Text variant="bodyMedium">{t("multiCode.dirtyCount", { count: dirtyCount })}</Text>
        <View style={styles.actions}>
          <Button compact onPress={onReset} disabled={saving}>
            {t("common:actions.discard")}
          </Button>
          <Button mode="contained" compact onPress={onSaveAll} loading={saving} disabled={saving}>
            {saving ? t("common:actions.saving") : t("common:actions.saveAll")}
          </Button>
        </View>
      </View>
      {onSaveAndReturn ? (
        <Button
          compact
          icon="arrow-left"
          mode="outlined"
          onPress={onSaveAndReturn}
          disabled={saving}
        >
          {t("actions.saveAndReturnToInvoice")}
        </Button>
      ) : null}
    </Surface>
  );
}

const styles = StyleSheet.create({
  container: {
    gap: 12,
    paddingHorizontal: 16,
    paddingVertical: 10,
    backgroundColor: "#fff",
    borderTopWidth: StyleSheet.hairlineWidth,
    borderTopColor: "#E4E7EC",
  },
  primaryRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 12,
  },
  actions: {
    flexDirection: "row",
    alignItems: "center",
    gap: 8,
  },
});
