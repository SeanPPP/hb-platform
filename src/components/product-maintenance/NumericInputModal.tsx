import { StyleSheet, View } from "react-native";
import { Button, Modal, Portal, Text } from "react-native-paper";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

interface NumericInputModalProps {
  visible: boolean;
  title: string;
  value: string;
  allowDecimal?: boolean;
  confirmLabel?: string;
  onChangeValue: (value: string) => void;
  onConfirm: () => void;
  onDismiss: () => void;
}

function sanitizeNumericValue(value: string, allowDecimal: boolean) {
  let result = "";
  let hasDecimal = false;
  let decimalCount = 0;

  for (const char of value) {
    if (char >= "0" && char <= "9") {
      if (!hasDecimal) {
        result += char;
      } else if (decimalCount < 2) {
        result += char;
        decimalCount += 1;
      }
      continue;
    }

    if (char === "." && allowDecimal && !hasDecimal) {
      result += result ? "." : "0.";
      hasDecimal = true;
      decimalCount = 0;
    }
  }

  return result;
}

function appendChar(current: string, nextChar: string, allowDecimal: boolean) {
  if (nextChar === "." && (!allowDecimal || current.includes("."))) {
    return current;
  }

  return sanitizeNumericValue(`${current}${nextChar}`, allowDecimal);
}

export function NumericInputModal({
  visible,
  title,
  value,
  allowDecimal = true,
  confirmLabel,
  onChangeValue,
  onConfirm,
  onDismiss,
}: NumericInputModalProps) {
  const { t } = useAppTranslation("common");

  return (
    <Portal>
      <Modal visible={visible} onDismiss={onDismiss} contentContainerStyle={styles.modal}>
        <View style={styles.content}>
          <Text variant="titleMedium" style={styles.title}>
            {title}
          </Text>
          <View style={styles.valueCard}>
            <Text variant="headlineMedium" style={styles.valueText}>
              {value || "0"}
            </Text>
          </View>

          <View style={styles.keypad}>
            {["1", "2", "3", "4", "5", "6", "7", "8", "9"].map((digit) => (
              <Button
                key={digit}
                mode="outlined"
                compact
                onPress={() => onChangeValue(appendChar(value, digit, allowDecimal))}
                style={styles.keyButton}
                labelStyle={styles.keyLabel}
              >
                {digit}
              </Button>
            ))}
            <Button
              mode="outlined"
              compact
              onPress={() => onChangeValue(appendChar(value, ".", allowDecimal))}
              disabled={!allowDecimal}
              style={styles.keyButton}
              labelStyle={styles.keyLabel}
            >
              .
            </Button>
            <Button
              mode="outlined"
              compact
              onPress={() => onChangeValue(appendChar(value, "0", allowDecimal))}
              style={styles.keyButton}
              labelStyle={styles.keyLabel}
            >
              0
            </Button>
            <Button
              mode="outlined"
              compact
              onPress={() => onChangeValue(value.slice(0, -1))}
              style={styles.keyButton}
              labelStyle={styles.keyLabel}
            >
              Del
            </Button>
          </View>

          <View style={styles.footer}>
            <Button mode="text" onPress={() => onChangeValue("")}>
              {t("actions.clear")}
            </Button>
            <View style={styles.footerActions}>
              <Button mode="text" onPress={onDismiss}>
                {t("actions.cancel")}
              </Button>
              <Button mode="contained" onPress={onConfirm}>
                {confirmLabel || t("actions.apply")}
              </Button>
            </View>
          </View>
        </View>
      </Modal>
    </Portal>
  );
}

const styles = StyleSheet.create({
  modal: {
    marginHorizontal: 18,
    borderRadius: 16,
    backgroundColor: "#fff",
    padding: 16,
  },
  content: {
    gap: 14,
  },
  title: {
    fontWeight: "700",
    color: "#111827",
  },
  valueCard: {
    borderRadius: 12,
    backgroundColor: "#F8FAFC",
    paddingHorizontal: 14,
    paddingVertical: 18,
    alignItems: "flex-end",
  },
  valueText: {
    color: "#111827",
    fontWeight: "700",
  },
  keypad: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
  },
  keyButton: {
    width: "31%",
    minWidth: 88,
    justifyContent: "center",
  },
  keyLabel: {
    fontSize: 18,
    fontWeight: "700",
  },
  footer: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 8,
  },
  footerActions: {
    flexDirection: "row",
    alignItems: "center",
    gap: 8,
  },
});
