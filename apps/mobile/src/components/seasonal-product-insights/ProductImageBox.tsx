import { useState } from "react";
import { Image, StyleSheet, View } from "react-native";
import { Icon } from "react-native-paper";
import { HB_COLORS, HB_RADIUS } from "@/shared/theme/tokens";

/** 商品图：地址为空或加载失败时显示占位图标，不显示破图。 */
export function ProductImageBox({ uri, size, label }: { uri: string | null; size: number; label: string }) {
  const [failed, setFailed] = useState(false);
  return (
    <View
      style={[styles.box, { width: size, height: size }]}
      accessible
      accessibilityRole="image"
      accessibilityLabel={label}
    >
      {uri && !failed ? (
        <Image source={{ uri }} style={styles.image} resizeMode="cover" onError={() => setFailed(true)} />
      ) : (
        <Icon source="image-outline" size={Math.round(size * 0.38)} color="#98A2B3" />
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  box: {
    borderRadius: HB_RADIUS.control,
    backgroundColor: HB_COLORS.surfaceMuted,
    alignItems: "center",
    justifyContent: "center",
    overflow: "hidden",
  },
  image: { width: "100%", height: "100%" },
});
