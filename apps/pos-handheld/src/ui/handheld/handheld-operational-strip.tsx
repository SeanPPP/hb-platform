import {
  ScrollView,
  StyleSheet,
  Text,
  View,
  type StyleProp,
  type ViewStyle,
} from "react-native";

import {
  handheldControl,
  handheldLayout,
  handheldTone,
  handheldType,
} from "./handheld-design-tokens";

import { PosPressable } from "@/ui/controls/pos-pressable";
import { posColors } from "@/ui/theme";

type HandheldOperationalTone = keyof typeof handheldTone | "neutral";

export type HandheldOperationalItem = Readonly<{
  accessibilityHint?: string;
  accessibilityLiveRegion?: "none" | "polite" | "assertive";
  key: string;
  label: string;
  onPress?: () => void;
  tone?: HandheldOperationalTone;
  value: string;
}>;

type HandheldOperationalStripProps = Readonly<{
  compact?: boolean;
  items: readonly HandheldOperationalItem[];
  style?: StyleProp<ViewStyle>;
}>;

/** 状态区固定单行，超宽时横向滑动；compact 用于页头等紧凑嵌入。 */
export function HandheldOperationalStrip({
  compact = false,
  items,
  style,
}: HandheldOperationalStripProps) {
  return (
    <ScrollView
      contentContainerStyle={[styles.content, compact && styles.contentCompact]}
      horizontal
      showsHorizontalScrollIndicator={false}
      style={[styles.container, compact && styles.containerCompact, style]}
      testID="handheld-operational-strip"
    >
      {items.map((item) => {
        const tone = item.tone ?? "neutral";
        const foreground =
          tone === "neutral"
            ? posColors.mutedInk
            : handheldTone[tone].foreground;
        const itemContent = (
          <>
            <View style={[styles.dot, { backgroundColor: foreground }]} />
            <Text numberOfLines={1} style={styles.label}>
              {item.label}
            </Text>
            <Text
              numberOfLines={1}
              style={[styles.value, { color: foreground }]}
            >
              {item.value}
            </Text>
          </>
        );
        const accessibilityLabel = `${item.label}: ${item.value}`;
        const itemStyle = [
          styles.item,
          compact && styles.itemCompact,
          // 可点击项无论是否 compact 都保持 48dp 触控下限。
          item.onPress && styles.itemPressable,
        ];
        if (item.onPress) {
          return (
            <PosPressable
              accessibilityHint={item.accessibilityHint}
              accessibilityLabel={accessibilityLabel}
              accessibilityLiveRegion={item.accessibilityLiveRegion}
              accessibilityRole="button"
              key={item.key}
              onPress={item.onPress}
              style={itemStyle}
              testID="handheld-operational-item"
            >
              {itemContent}
            </PosPressable>
          );
        }
        return (
          <View
            accessibilityLabel={accessibilityLabel}
            accessibilityLiveRegion={item.accessibilityLiveRegion}
            key={item.key}
            style={itemStyle}
            testID="handheld-operational-item"
          >
            {itemContent}
          </View>
        );
      })}
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  container: {
    backgroundColor: posColors.surface,
    borderBottomColor: posColors.border,
    borderBottomWidth: handheldControl.borderWidth,
    // 横向 ScrollView 默认会 flexGrow: 1；作为页头时必须保持内容高度。
    flexGrow: 0,
    flexShrink: 0,
  },
  containerCompact: {
    minHeight: handheldControl.minimumHeight,
  },
  content: {
    alignItems: "center",
    flexDirection: "row",
    gap: handheldLayout.compactGap,
    paddingHorizontal: handheldLayout.screenPadding,
    paddingVertical: handheldLayout.compactGap,
  },
  contentCompact: {
    paddingVertical: 0,
  },
  item: {
    alignItems: "center",
    flexDirection: "row",
    // 横向滚动时保持条目固有宽度，不被容器压缩。
    flexShrink: 0,
    gap: 4,
    minHeight: 24,
  },
  itemCompact: {
    minHeight: handheldControl.minimumHeight,
  },
  itemPressable: {
    minHeight: handheldControl.minimumHeight,
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
