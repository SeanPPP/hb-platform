import { StyleSheet } from "react-native";
import { HB_COLORS, HB_RADIUS, HB_SPACING } from "@/shared/theme/tokens";

/** 显式用于本次业务页的视觉样式，不替换全局主题和未改版页面。 */
export const BUSINESS_UI = StyleSheet.create({
  screen: { flex: 1, backgroundColor: HB_COLORS.background },
  content: { paddingHorizontal: HB_SPACING.md, paddingTop: HB_SPACING.sm, paddingBottom: HB_SPACING.lg, gap: HB_SPACING.sm },
  header: { paddingHorizontal: HB_SPACING.md, paddingTop: HB_SPACING.sm, paddingBottom: HB_SPACING.sm, gap: HB_SPACING.xs },
  headerRow: { flexDirection: "row", alignItems: "center", justifyContent: "space-between", gap: HB_SPACING.sm },
  title: { fontSize: 24, lineHeight: 32, fontWeight: "700", color: HB_COLORS.textPrimary, flexShrink: 1 },
  subtitle: { fontSize: 12, lineHeight: 18, color: HB_COLORS.textSecondary },
  section: { backgroundColor: HB_COLORS.white, borderWidth: 1, borderColor: HB_COLORS.outlineMuted, borderRadius: HB_RADIUS.surface, overflow: "hidden" },
  sectionContent: { padding: HB_SPACING.md, gap: HB_SPACING.sm },
  sectionTitle: { fontSize: 16, lineHeight: 24, fontWeight: "700", color: HB_COLORS.textPrimary },
  filterGroup: { padding: HB_SPACING.sm, gap: HB_SPACING.xs, backgroundColor: HB_COLORS.white, borderWidth: 1, borderColor: HB_COLORS.outlineMuted, borderRadius: HB_RADIUS.surface },
  row: { flexDirection: "row", alignItems: "center", gap: HB_SPACING.sm, minHeight: 48 },
  fieldLabel: { fontSize: 12, lineHeight: 18, color: HB_COLORS.textSecondary },
  fieldValue: { fontSize: 14, lineHeight: 20, fontWeight: "600", color: HB_COLORS.textPrimary },
  number: { fontVariant: ["tabular-nums"], fontWeight: "700", color: HB_COLORS.textPrimary },
  footer: { paddingHorizontal: HB_SPACING.md, paddingVertical: HB_SPACING.sm, backgroundColor: HB_COLORS.white, borderTopWidth: StyleSheet.hairlineWidth, borderTopColor: HB_COLORS.outlineMuted, gap: HB_SPACING.xs },
  button: { borderRadius: HB_RADIUS.control },
  buttonContent: { minHeight: 44 },
});
