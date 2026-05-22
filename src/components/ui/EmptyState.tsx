import type { ComponentProps } from "react";
import { StyleSheet, View } from "react-native";
import { Button, Text } from "react-native-paper";

type EmptyStateAction = {
  label: string;
  onPress: () => void;
  icon?: ComponentProps<typeof Button>["icon"];
  mode?: ComponentProps<typeof Button>["mode"];
  disabled?: boolean;
};

interface EmptyStateProps {
  title: string;
  description?: string;
  actionLabel?: string;
  onAction?: () => void;
  primaryAction?: EmptyStateAction;
  secondaryAction?: EmptyStateAction;
}

function renderAction(action: EmptyStateAction, defaultMode: ComponentProps<typeof Button>["mode"]) {
  return (
    <Button
      compact
      disabled={action.disabled}
      icon={action.icon}
      mode={action.mode ?? defaultMode}
      onPress={action.onPress}
      style={styles.actionButton}
    >
      {action.label}
    </Button>
  );
}

export function EmptyState({
  title,
  description,
  actionLabel,
  onAction,
  primaryAction,
  secondaryAction,
}: EmptyStateProps) {
  const resolvedPrimaryAction =
    primaryAction ?? (actionLabel && onAction ? { label: actionLabel, mode: "outlined" as const, onPress: onAction } : undefined);

  return (
    <View style={styles.container}>
      <Text variant="titleMedium">{title}</Text>
      {description ? (
        <Text variant="bodyMedium" style={styles.description}>
          {description}
        </Text>
      ) : null}
      {resolvedPrimaryAction || secondaryAction ? (
        <View style={styles.actions}>
          {resolvedPrimaryAction ? renderAction(resolvedPrimaryAction, "contained") : null}
          {secondaryAction ? renderAction(secondaryAction, "outlined") : null}
        </View>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    padding: 24,
    alignItems: "center",
    justifyContent: "center",
    gap: 8,
  },
  description: {
    color: "#666",
    textAlign: "center",
  },
  actions: {
    alignItems: "center",
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
    justifyContent: "center",
  },
  actionButton: {
    maxWidth: "100%",
  },
});
