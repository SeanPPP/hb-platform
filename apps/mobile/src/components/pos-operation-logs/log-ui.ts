import { StyleSheet } from "react-native";
import type { PosOperationOutcome } from "@/modules/pos-operation-logs/types";
import { HB_COLORS, HB_RADIUS, HB_SPACING } from "@/shared/theme/tokens";

/** 结果三态的配色：成功不描边，拒绝与失败分别用警告橙与危险红。 */
export const OUTCOME_TONES: Record<
  PosOperationOutcome,
  { background: string; text: string; accent: string | null }
> = {
  Succeeded: { background: "#E7F6EC", text: HB_COLORS.success, accent: null },
  Denied: { background: "#FFF4E5", text: HB_COLORS.warning, accent: HB_COLORS.warning },
  Failed: { background: "#FEE4E2", text: HB_COLORS.danger, accent: HB_COLORS.danger },
};

export function platformIcon(deviceSystem: string | null): string {
  if (deviceSystem === "Windows") return "microsoft-windows";
  if (deviceSystem === "iPadOS") return "tablet";
  return "help-circle-outline";
}

export const LOG_UI = StyleSheet.create({
  pill: {
    paddingHorizontal: 7,
    paddingVertical: 1,
    borderRadius: 999,
    alignSelf: "center",
  },
  pillText: { fontSize: 11, lineHeight: 15, fontWeight: "600" },
  tag: {
    paddingHorizontal: 5,
    paddingVertical: 1,
    borderRadius: 4,
    borderWidth: StyleSheet.hairlineWidth,
    borderColor: HB_COLORS.outline,
    alignSelf: "center",
  },
  tagText: { fontSize: 11, lineHeight: 14, color: HB_COLORS.textSecondary },
  tagDanger: { borderColor: HB_COLORS.danger },
  tagDangerText: { color: HB_COLORS.danger },
  mono: { fontVariant: ["tabular-nums"] },
  chip: {
    flexDirection: "row",
    alignItems: "center",
    gap: 4,
    paddingHorizontal: 10,
    paddingVertical: 6,
    borderRadius: 999,
    borderWidth: 1,
    borderColor: HB_COLORS.outline,
    backgroundColor: HB_COLORS.white,
  },
  chipSelected: { borderColor: HB_COLORS.action, backgroundColor: "#EAF2FF" },
  chipText: { fontSize: 12, lineHeight: 16, color: HB_COLORS.textPrimary },
  chipTextSelected: { color: HB_COLORS.action, fontWeight: "600" },
  sectionLabel: {
    fontSize: 11,
    lineHeight: 14,
    color: HB_COLORS.textSecondary,
    marginTop: HB_SPACING.sm,
    marginBottom: 6,
  },
  card: {
    backgroundColor: HB_COLORS.white,
    borderRadius: HB_RADIUS.surface,
    borderWidth: StyleSheet.hairlineWidth,
    borderColor: HB_COLORS.outlineMuted,
  },
});
