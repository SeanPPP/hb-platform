import { Pressable, StyleSheet, View } from "react-native";
import { Text } from "react-native-paper";
import { HB_COLORS, HB_SPACING } from "@/shared/theme/tokens";

export interface PosterSegmentOption<T extends string> {
  value: T;
  label: string;
  disabled?: boolean;
}

interface PosterSegmentedProps<T extends string> {
  label: string;
  value: T;
  options: readonly PosterSegmentOption<T>[];
  onChange: (value: T) => void;
  /** 仅用于风格选择：选项较多时换行，不改变其它分段控件。 */
  wrap?: boolean;
}

/** 带左侧标题的分段选择（设计稿 seg）：选中蓝底描边，不可用项置灰加删除线。 */
export function PosterSegmented<T extends string>({ label, value, options, onChange, wrap = false }: PosterSegmentedProps<T>) {
  return (
    <View style={styles.row}>
      <Text style={styles.label} numberOfLines={1}>
        {label}
      </Text>
      <View style={[styles.track, wrap ? styles.trackWrap : null]} accessibilityRole="radiogroup" accessibilityLabel={label}>
        {options.map((option) => {
          const selected = option.value === value;
          return (
            <Pressable
              key={option.value}
              accessibilityRole="radio"
              accessibilityState={{ selected, disabled: Boolean(option.disabled) }}
              disabled={option.disabled}
              onPress={() => onChange(option.value)}
              style={[styles.item, wrap ? styles.itemWrap : null, selected ? styles.itemSelected : null]}
            >
              <Text
                numberOfLines={1}
                style={[
                  styles.itemText,
                  selected ? styles.itemTextSelected : null,
                  option.disabled ? styles.itemTextDisabled : null,
                ]}
              >
                {option.label}
              </Text>
            </Pressable>
          );
        })}
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  row: {
    flexDirection: "row",
    alignItems: "center",
    gap: 10,
  },
  label: {
    minWidth: 32,
    fontSize: 13,
    fontWeight: "700",
    color: HB_COLORS.textPrimary,
  },
  track: {
    flex: 1,
    flexDirection: "row",
    gap: 2,
    padding: 2,
    borderRadius: 9,
    backgroundColor: HB_COLORS.surfaceMuted,
  },
  trackWrap: {
    flexWrap: "wrap",
  },
  item: {
    flex: 1,
    minWidth: 0,
    height: 36,
    borderRadius: 7,
    alignItems: "center",
    justifyContent: "center",
    paddingHorizontal: HB_SPACING.xxs / 2,
  },
  itemWrap: {
    flexBasis: "49%",
    flexGrow: 0,
    flexShrink: 0,
  },
  itemSelected: {
    backgroundColor: "#EAF2FF",
    borderWidth: 1,
    borderColor: "#91CAFF",
  },
  itemText: {
    fontSize: 13,
    fontWeight: "500",
    color: HB_COLORS.textPrimary,
  },
  itemTextSelected: {
    fontWeight: "700",
    color: "#073B83",
  },
  itemTextDisabled: {
    color: "#98A2B3",
    textDecorationLine: "line-through",
    textDecorationColor: HB_COLORS.outline,
  },
});
