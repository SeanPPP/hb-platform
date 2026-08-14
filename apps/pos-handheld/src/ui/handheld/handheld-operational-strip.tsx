import { StyleSheet, Text, View } from "react-native";

import {
  handheldControl,
  handheldLayout,
  handheldTone,
  handheldType,
} from "./handheld-design-tokens";

import { posColors } from "@/ui/theme";

type HandheldOperationalTone = keyof typeof handheldTone | "neutral";

export type HandheldOperationalItem = Readonly<{
  key: string;
  label: string;
  tone?: HandheldOperationalTone;
  value: string;
}>;

type HandheldOperationalStripProps = Readonly<{
  items: readonly HandheldOperationalItem[];
}>;

/** 小屏状态区允许自动换行，避免横向压缩造成门店或外设状态不可读。 */
export function HandheldOperationalStrip({
  items,
}: HandheldOperationalStripProps) {
  return (
    <View style={styles.container} testID="handheld-operational-strip">
      {items.map((item) => {
        const tone = item.tone ?? "neutral";
        const foreground =
          tone === "neutral"
            ? posColors.mutedInk
            : handheldTone[tone].foreground;
        return (
          <View
            accessibilityLabel={`${item.label}: ${item.value}`}
            key={item.key}
            style={styles.item}
            testID="handheld-operational-item"
          >
            <View style={[styles.dot, { backgroundColor: foreground }]} />
            <Text numberOfLines={1} style={styles.label}>
              {item.label}
            </Text>
            <Text numberOfLines={1} style={[styles.value, { color: foreground }]}>
              {item.value}
            </Text>
          </View>
        );
      })}
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    backgroundColor: posColors.surface,
    borderBottomColor: posColors.border,
    borderBottomWidth: handheldControl.borderWidth,
    flexDirection: "row",
    flexWrap: "wrap",
    gap: handheldLayout.compactGap,
    paddingHorizontal: handheldLayout.screenPadding,
    paddingVertical: handheldLayout.compactGap,
  },
  item: {
    alignItems: "center",
    flexDirection: "row",
    flexGrow: 1,
    gap: 4,
    minHeight: 24,
    minWidth: "30%",
  },
  dot: {
    borderRadius: 3,
    height: 6,
    width: 6,
  },
  label: {
    color: posColors.mutedInk,
    fontSize: handheldType.metadata,
    fontWeight: "700",
  },
  value: {
    flexShrink: 1,
    fontSize: handheldType.metadata,
    fontWeight: "800",
  },
});
