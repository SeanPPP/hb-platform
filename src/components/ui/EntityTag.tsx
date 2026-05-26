import { StyleSheet, View } from "react-native";
import { Text } from "react-native-paper";
import { getEntityTone, type EntityKind } from "@/shared/utils/entity-color";

interface EntityTagProps {
  kind: EntityKind;
  code?: string | null;
  label?: string | null;
  compact?: boolean;
}

export function EntityTag({ kind, code, label, compact = false }: EntityTagProps) {
  const displayValue = label?.trim() || code?.trim() || "--";
  const tone = getEntityTone(code || label, kind);

  return (
    <View
      style={[
        styles.container,
        compact ? styles.compactContainer : null,
        {
          backgroundColor: tone.backgroundColor,
          borderColor: tone.borderColor,
        },
      ]}
    >
      <Text
        variant={compact ? "labelSmall" : "labelMedium"}
        numberOfLines={1}
        style={[styles.text, { color: tone.textColor }]}
      >
        {displayValue}
      </Text>
    </View>
  );
}

const styles = StyleSheet.create({
  compactContainer: {
    minHeight: 24,
    paddingHorizontal: 8,
    paddingVertical: 3,
  },
  container: {
    alignItems: "center",
    alignSelf: "flex-start",
    borderRadius: 999,
    borderWidth: 1,
    justifyContent: "center",
    maxWidth: "100%",
    minHeight: 28,
    paddingHorizontal: 10,
    paddingVertical: 4,
  },
  text: {
    fontWeight: "600",
  },
});
