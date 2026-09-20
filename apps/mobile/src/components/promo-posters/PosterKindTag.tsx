import { StyleSheet, Text } from "react-native";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import type { PromoPosterKind } from "@/modules/promo-posters/types";

/** 类型小色块（设计稿 TYPE_TAG）：特价/多件价红、新品绿、清仓黄。 */
const TAG_COLORS: Record<PromoPosterKind, { fg: string; bg: string }> = {
  special: { fg: "#D40F16", bg: "#FEF3F2" },
  multibuy: { fg: "#D40F16", bg: "#FEF3F2" },
  new: { fg: "#0A7A45", bg: "#ECFDF3" },
  clearance: { fg: "#93370D", bg: "#FEF7C3" },
};

export function PosterKindTag({ kind }: { kind: PromoPosterKind }) {
  const { t } = useAppTranslation(["productQuery"]);
  const colors = TAG_COLORS[kind];
  return <Text style={[styles.tag, { color: colors.fg, backgroundColor: colors.bg }]}>{t(`poster.kinds.${kind}`)}</Text>;
}

const styles = StyleSheet.create({
  tag: {
    fontSize: 11,
    lineHeight: 16,
    fontWeight: "700",
    paddingHorizontal: 6,
    paddingVertical: 1,
    borderRadius: 4,
    overflow: "hidden",
  },
});
