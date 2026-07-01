import { StyleSheet } from "react-native";
import type { StyleProp, ViewStyle } from "react-native";
import { SegmentedButtons } from "react-native-paper";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import type { CameraScanMode } from "@/modules/scanner/use-camera-scan";

interface CameraScanModeSelectorProps {
  value: CameraScanMode;
  onChange: (value: CameraScanMode) => void;
  style?: StyleProp<ViewStyle>;
}

export function CameraScanModeSelector({
  value,
  onChange,
  style,
}: CameraScanModeSelectorProps) {
  const { t } = useAppTranslation("common");

  return (
    <SegmentedButtons
      value={value}
      onValueChange={(nextValue) => onChange(nextValue as CameraScanMode)}
      buttons={[
        // 扫码模式文案统一放在 common 命名空间，页面接入时不用重复组装标签。
        { value: "single", label: t("scanner.cameraModeSingle") },
        { value: "continuous", label: t("scanner.cameraModeContinuous") },
      ]}
      density="small"
      style={[styles.segmentedButtons, style]}
    />
  );
}

const styles = StyleSheet.create({
  segmentedButtons: {
    marginTop: 6,
  },
});
