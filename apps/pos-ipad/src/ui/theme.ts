import { MD3LightTheme, type MD3Theme } from "react-native-paper";

export const posColors = {
  ink: "#10253A",
  mutedInk: "#5D6D7E",
  canvas: "#F4F1EA",
  surface: "#FFFFFF",
  border: "#D9D4C9",
  orange: "#E65A2F",
  orangeSoft: "#FCEBE4",
  green: "#277C63",
  greenSoft: "#E5F3ED",
  yellow: "#B98516",
  yellowSoft: "#FFF5CF",
  red: "#B73932",
  redSoft: "#F9E7E5",
  blue: "#235C8C",
  blueSoft: "#E7F0F8",
} as const;

export const posTheme: MD3Theme = {
  ...MD3LightTheme,
  roundness: 2,
  colors: {
    ...MD3LightTheme.colors,
    primary: posColors.orange,
    onPrimary: "#FFFFFF",
    primaryContainer: posColors.orangeSoft,
    onPrimaryContainer: "#6C1C07",
    secondary: posColors.blue,
    onSecondary: "#FFFFFF",
    background: posColors.canvas,
    surface: posColors.surface,
    onSurface: posColors.ink,
    outline: posColors.border,
  },
};
