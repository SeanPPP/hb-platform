import { Pressable, StyleSheet, Text, View } from "react-native";

import { MIN_TOUCH_TARGET } from "./sales-presenter";

import { posColors } from "@/ui/theme";

export type SalesNumberKey =
  | "0"
  | "1"
  | "2"
  | "3"
  | "4"
  | "5"
  | "6"
  | "7"
  | "8"
  | "9"
  | "decimal"
  | "quick-50"
  | "quick-99"
  | "clear"
  | "backspace";

export type SalesNumberKeypadMode = "integer" | "decimal";

export type SalesNumberKeypadLabels = Readonly<{
  clear: string;
  backspace: string;
  decimal: string;
  quick50: string;
  quick99: string;
}>;

export type SalesNumberKeypadProps = Readonly<{
  mode: SalesNumberKeypadMode;
  disabled?: boolean;
  onKeyPress(key: SalesNumberKey): void;
  testIDPrefix: string;
  labels: SalesNumberKeypadLabels;
}>;

type ApplySalesNumberKeyOptions = Readonly<{
  mode: SalesNumberKeypadMode;
  replaceOnNextDigit?: boolean;
}>;

type DigitKey = Extract<
  SalesNumberKey,
  "0" | "1" | "2" | "3" | "4" | "5" | "6" | "7" | "8" | "9"
>;

const INTEGER_KEY_ROWS: readonly (readonly SalesNumberKey[])[] = [
  ["1", "2", "3"],
  ["4", "5", "6"],
  ["7", "8", "9"],
  ["clear", "0", "backspace"],
];

const DECIMAL_KEY_ROWS: readonly (readonly SalesNumberKey[])[] = [
  ...INTEGER_KEY_ROWS.slice(0, 3),
  ["clear", "0", "decimal"],
  ["quick-50", "quick-99", "backspace"],
];

export function applySalesNumberKey(
  value: string,
  key: SalesNumberKey,
  options: ApplySalesNumberKeyOptions,
): string {
  if (key === "clear") return "";
  if (key === "backspace") return value.slice(0, -1);

  if (key === "decimal") {
    if (options.mode === "integer" || value.includes(".")) return value;
    return value.length === 0 ? "0." : `${value}.`;
  }

  if (key === "quick-50" || key === "quick-99") {
    if (options.mode === "integer") return value;
    const integerPart = value.split(".")[0] || "0";
    return `${integerPart}.${key === "quick-50" ? "50" : "99"}`;
  }

  // 替换标志只影响下一次普通数字，功能键仍基于当前值执行。
  if (options.replaceOnNextDigit) return key;

  if (options.mode === "decimal" && hasTwoDecimalDigits(value)) return value;
  return `${value}${key}`;
}

export function SalesNumberKeypad({
  mode,
  disabled = false,
  onKeyPress,
  testIDPrefix,
  labels,
}: SalesNumberKeypadProps) {
  const rows = mode === "decimal" ? DECIMAL_KEY_ROWS : INTEGER_KEY_ROWS;

  return (
    <View style={styles.keypad} testID={`${testIDPrefix}-keypad`}>
      {rows.map((row, rowIndex) => (
        <View key={rowIndex} style={styles.row}>
          {row.map((key) => (
            <Pressable
              accessibilityLabel={getAccessibilityLabel(key, labels)}
              accessibilityRole="button"
              accessibilityState={{ disabled }}
              disabled={disabled}
              key={key}
              onPress={() => onKeyPress(key)}
              style={({ pressed }) => [
                styles.key,
                isQuickKey(key) && styles.quickKey,
                key === "clear" && styles.clearKey,
                disabled && styles.keyDisabled,
                pressed && !disabled && styles.keyPressed,
              ]}
              testID={`${testIDPrefix}-key-${key}`}
            >
              <Text
                style={[
                  styles.keyText,
                  isQuickKey(key) && styles.quickKeyText,
                  key === "clear" && styles.clearKeyText,
                  disabled && styles.keyTextDisabled,
                ]}
              >
                {getVisibleLabel(key, labels)}
              </Text>
            </Pressable>
          ))}
        </View>
      ))}
    </View>
  );
}

function hasTwoDecimalDigits(value: string): boolean {
  const decimalIndex = value.indexOf(".");
  return decimalIndex >= 0 && value.length - decimalIndex - 1 >= 2;
}

function isDigitKey(key: SalesNumberKey): key is DigitKey {
  return key.length === 1 && key >= "0" && key <= "9";
}

function isQuickKey(
  key: SalesNumberKey,
): key is Extract<SalesNumberKey, "quick-50" | "quick-99"> {
  return key === "quick-50" || key === "quick-99";
}

function getVisibleLabel(
  key: SalesNumberKey,
  labels: SalesNumberKeypadLabels,
): string {
  if (isDigitKey(key)) return key;
  switch (key) {
    case "decimal":
      return ".";
    case "quick-50":
      return "0.5";
    case "quick-99":
      return "0.99";
    case "clear":
      return labels.clear;
    case "backspace":
      return "⌫";
  }
}

function getAccessibilityLabel(
  key: SalesNumberKey,
  labels: SalesNumberKeypadLabels,
): string {
  if (isDigitKey(key)) return key;
  switch (key) {
    case "decimal":
      return labels.decimal;
    case "quick-50":
      return labels.quick50;
    case "quick-99":
      return labels.quick99;
    case "clear":
      return labels.clear;
    case "backspace":
      return labels.backspace;
  }
}

const styles = StyleSheet.create({
  keypad: {
    gap: 8,
    width: "100%",
  },
  row: {
    flexDirection: "row",
    gap: 8,
  },
  key: {
    alignItems: "center",
    backgroundColor: posColors.surface,
    borderColor: posColors.border,
    borderRadius: 4,
    borderWidth: 1,
    flex: 1,
    justifyContent: "center",
    minHeight: MIN_TOUCH_TARGET,
    minWidth: MIN_TOUCH_TARGET,
    paddingHorizontal: 8,
  },
  quickKey: {
    backgroundColor: posColors.blueSoft,
    borderColor: posColors.blue,
  },
  clearKey: {
    backgroundColor: posColors.redSoft,
    borderColor: posColors.red,
  },
  keyDisabled: {
    backgroundColor: posColors.canvas,
    borderColor: posColors.border,
    opacity: 0.62,
  },
  keyPressed: {
    opacity: 0.72,
    transform: [{ scale: 0.98 }],
  },
  keyText: {
    color: posColors.ink,
    fontSize: 20,
    fontVariant: ["tabular-nums"],
    fontWeight: "800",
  },
  quickKeyText: {
    color: posColors.blue,
  },
  clearKeyText: {
    color: posColors.red,
  },
  keyTextDisabled: {
    color: posColors.mutedInk,
  },
});
