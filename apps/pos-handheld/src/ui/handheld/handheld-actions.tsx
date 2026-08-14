import { StyleSheet, Text } from "react-native";

import {
  handheldControl,
  handheldLayout,
  handheldType,
} from "./handheld-design-tokens";

import { PosPressable } from "@/ui/controls/pos-pressable";
import type { TouchSoundKind } from "@/ui/feedback/pos-sound-context";
import { posColors } from "@/ui/theme";

type HandheldActionVariant = "primary" | "secondary" | "danger";

type HandheldActionButtonProps = Readonly<{
  accessibilityLabel?: string;
  disabled?: boolean;
  label: string;
  onPress: () => void;
  sound?: TouchSoundKind | false;
  testID?: string;
  variant?: HandheldActionVariant;
}>;

/** 手持端统一主操作；所有关键按钮保持至少 48px 触控高度。 */
export function HandheldActionButton({
  accessibilityLabel,
  disabled = false,
  label,
  onPress,
  sound = "tap",
  testID,
  variant = "primary",
}: HandheldActionButtonProps) {
  return (
    <PosPressable
      accessibilityLabel={accessibilityLabel ?? label}
      accessibilityRole="button"
      accessibilityState={{ disabled }}
      disabled={disabled}
      onPress={onPress}
      sound={sound}
      style={({ pressed }) => [
        styles.button,
        variantStyles[variant],
        pressed && !disabled && styles.pressed,
        disabled && styles.disabled,
      ]}
      testID={testID}
    >
      <Text
        style={[
          styles.label,
          variant === "secondary" ? styles.secondaryLabel : styles.lightLabel,
        ]}
      >
        {label}
      </Text>
    </PosPressable>
  );
}

const variantStyles = StyleSheet.create({
  danger: {
    backgroundColor: posColors.red,
    borderColor: posColors.red,
  },
  primary: {
    backgroundColor: posColors.orange,
    borderColor: posColors.orange,
  },
  secondary: {
    backgroundColor: posColors.surface,
    borderColor: posColors.border,
  },
});

const styles = StyleSheet.create({
  button: {
    alignItems: "center",
    borderRadius: handheldControl.radius,
    borderWidth: handheldControl.borderWidth,
    justifyContent: "center",
    minHeight: handheldControl.minimumHeight,
    paddingHorizontal: handheldLayout.screenPadding,
  },
  disabled: {
    opacity: 0.45,
  },
  pressed: {
    opacity: 0.82,
  },
  label: {
    fontSize: handheldType.body,
    fontWeight: "800",
  },
  lightLabel: {
    color: "#FFFFFF",
  },
  secondaryLabel: {
    color: posColors.ink,
  },
});
