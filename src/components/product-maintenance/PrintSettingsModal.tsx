import { StyleSheet, View } from "react-native";
import { Button, IconButton, Modal, Switch, Text } from "react-native-paper";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

interface PrintSettingsModalProps {
  visible: boolean;
  continuousPrint: boolean;
  smallLabel: boolean;
  printQuantity: number;
  quantitySingleUse: boolean;
  onToggleContinuousPrint: (value: boolean) => void;
  onToggleSmallLabel: (value: boolean) => void;
  onChangePrintQuantity: (value: number) => void;
  onToggleQuantitySingleUse: (value: boolean) => void;
  onDismiss: () => void;
}

export function PrintSettingsModal({
  visible,
  continuousPrint,
  smallLabel,
  printQuantity,
  quantitySingleUse,
  onToggleContinuousPrint,
  onToggleSmallLabel,
  onChangePrintQuantity,
  onToggleQuantitySingleUse,
  onDismiss,
}: PrintSettingsModalProps) {
  const { t } = useAppTranslation("productQuery");

  return (
    <Modal visible={visible} onDismiss={onDismiss} contentContainerStyle={styles.modal}>
      <View style={styles.content}>
        <Text variant="titleMedium" style={styles.title}>
          {t("print.settingsTitle")}
        </Text>

        <View style={styles.row}>
          <Text variant="bodyMedium">{t("print.continuousPrint")}</Text>
          <Switch value={continuousPrint} onValueChange={onToggleContinuousPrint} />
        </View>

        <View style={styles.row}>
          <Text variant="bodyMedium">{t("print.smallLabel")}</Text>
          <Switch value={smallLabel} onValueChange={onToggleSmallLabel} />
        </View>

        <View style={styles.row}>
          <Text variant="bodyMedium">{t("print.quantity")}</Text>
          <View style={styles.quantityRow}>
            <IconButton
              icon="minus"
              size={16}
              onPress={() => onChangePrintQuantity(Math.max(1, printQuantity - 1))}
              disabled={printQuantity <= 1}
              style={styles.qtyButton}
            />
            <Text variant="titleMedium" style={styles.qtyValue}>
              {printQuantity}
            </Text>
            <IconButton
              icon="plus"
              size={16}
              onPress={() => onChangePrintQuantity(Math.min(99, printQuantity + 1))}
              disabled={printQuantity >= 99}
              style={styles.qtyButton}
            />
          </View>
        </View>

        <View style={styles.row}>
          <Text variant="bodyMedium">{t("print.quantitySingleUse")}</Text>
          <Switch value={quantitySingleUse} onValueChange={onToggleQuantitySingleUse} />
        </View>

        <View style={styles.footer}>
          <Button mode="contained" onPress={onDismiss}>
            {t("common:actions.confirm")}
          </Button>
        </View>
      </View>
    </Modal>
  );
}

const styles = StyleSheet.create({
  modal: {
    marginHorizontal: 24,
    borderRadius: 16,
    backgroundColor: "#fff",
  },
  content: {
    padding: 20,
    gap: 16,
  },
  title: {
    fontWeight: "700",
    color: "#111827",
  },
  row: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
  },
  quantityRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: 4,
  },
  qtyButton: {
    width: 28,
    height: 28,
    margin: 0,
  },
  qtyValue: {
    minWidth: 28,
    textAlign: "center",
    fontWeight: "700",
  },
  footer: {
    alignItems: "flex-end",
    paddingTop: 4,
  },
});
