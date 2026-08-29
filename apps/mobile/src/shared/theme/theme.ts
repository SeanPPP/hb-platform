import { MD3LightTheme, type MD3Theme } from "react-native-paper";
import { HB_COLORS, HB_RADIUS } from "@/shared/theme/tokens";

export const hbLightTheme: MD3Theme = {
  ...MD3LightTheme,
  roundness: HB_RADIUS.control,
  colors: {
    ...MD3LightTheme.colors,
    // 品牌蓝保留给图标与选中态；操作蓝加深以确保白字达到 WCAG AA。
    primary: HB_COLORS.action,
    onPrimary: HB_COLORS.white,
    primaryContainer: "#EAF2FF",
    onPrimaryContainer: "#073B83",
    secondary: HB_COLORS.success,
    onSecondary: HB_COLORS.white,
    secondaryContainer: "#E7F6EC",
    onSecondaryContainer: "#054F31",
    error: HB_COLORS.danger,
    onError: HB_COLORS.white,
    errorContainer: "#FEE4E2",
    onErrorContainer: "#7A271A",
    background: HB_COLORS.background,
    onBackground: HB_COLORS.textPrimary,
    surface: HB_COLORS.surface,
    onSurface: HB_COLORS.textPrimary,
    surfaceVariant: HB_COLORS.surfaceMuted,
    onSurfaceVariant: HB_COLORS.textSecondary,
    outline: HB_COLORS.outline,
    outlineVariant: HB_COLORS.outlineMuted,
    elevation: {
      ...MD3LightTheme.colors.elevation,
      level0: "transparent",
      level1: HB_COLORS.surface,
      level2: "#F8FAFC",
      level3: "#F2F4F7",
      level4: "#EEF2F6",
      level5: "#EAECF0",
    },
  },
};
