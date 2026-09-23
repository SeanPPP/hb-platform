import { memo } from "react";
import { StyleSheet, View } from "react-native";
import Svg, { Rect } from "react-native-svg";
import { Text } from "react-native-paper";
import { encodeCashRegisterBarcodeModules } from "@/modules/cash-register-users/barcode";
import { HB_COLORS } from "@/shared/theme/tokens";

interface CashRegisterBarcodeSvgProps {
  value: string;
  moduleWidth?: number;
  height?: number;
}

/** 屏幕预览用一维码：合法 EAN13 画 EAN13，否则画 Code128（react-native-svg 已在依赖中，纯 JS 渲染，无需重建原生包）。 */
export const CashRegisterBarcodeSvg = memo(function CashRegisterBarcodeSvg({ value, moduleWidth = 2, height = 44 }: CashRegisterBarcodeSvgProps) {
  const modules = encodeCashRegisterBarcodeModules(value);
  return (
    <View style={styles.wrap} accessibilityLabel={value}>
      {modules ? (
        <Svg width={modules.length * moduleWidth} height={height}>
          {Array.from(modules).map((bit, index) =>
            bit === "1" ? (
              <Rect key={index} x={index * moduleWidth} y={0} width={moduleWidth} height={height} fill="#000" />
            ) : null
          )}
        </Svg>
      ) : null}
      <Text variant="titleSmall" selectable style={styles.digits}>
        {value || "--"}
      </Text>
    </View>
  );
});

const styles = StyleSheet.create({
  wrap: {
    alignItems: "center",
    gap: 2,
    paddingVertical: 4,
    paddingHorizontal: 8,
    backgroundColor: "#fff",
    borderRadius: 6,
    alignSelf: "flex-start",
  },
  digits: {
    color: HB_COLORS.textPrimary,
    letterSpacing: 1,
    fontVariant: ["tabular-nums"],
  },
});
