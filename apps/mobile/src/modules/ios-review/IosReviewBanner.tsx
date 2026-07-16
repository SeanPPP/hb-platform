import { StyleSheet } from "react-native";
import { Surface, Text, useTheme } from "react-native-paper";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { IOS_REVIEW_BANNER } from "./helpers";

export function IosReviewBanner() {
  const theme = useTheme();
  const insets = useSafeAreaInsets();

  return (
    <Surface
      accessibilityRole="alert"
      accessibilityLabel={IOS_REVIEW_BANNER.accessibilityLabel}
      accessibilityLiveRegion="polite"
      pointerEvents="none"
      elevation={2}
      style={[
        styles.banner,
        {
          top: insets.top,
          backgroundColor: theme.colors.secondaryContainer,
        },
      ]}
    >
      <Text
        variant="labelSmall"
        style={[styles.text, { color: theme.colors.onSecondaryContainer }]}
      >
        {IOS_REVIEW_BANNER.title}
        {"\n"}
        {IOS_REVIEW_BANNER.description}
      </Text>
    </Surface>
  );
}

const styles = StyleSheet.create({
  banner: {
    position: "absolute",
    right: 12,
    left: 12,
    zIndex: 20,
    borderRadius: 8,
    paddingHorizontal: 12,
    paddingVertical: 7,
  },
  text: {
    textAlign: "center",
    lineHeight: 15,
  },
});
