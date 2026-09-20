import { StyleSheet, View } from "react-native";
import { Button, Surface, Text } from "react-native-paper";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { HB_COLORS, HB_RADIUS, HB_SPACING } from "@/shared/theme/tokens";

interface StickyActionBarProps {
  visible: boolean;
  dirtyCount: number;
  saving?: boolean;
  /** 保存并打印进行中（保存完成后的打印阶段也保持忙碌）。 */
  savingAndPrinting?: boolean;
  onReset: () => void;
  onSaveAll: () => void;
  /** 从发票编辑进入时的主按钮；存在时不显示「保存并打印」。 */
  onSaveAndReturn?: () => void;
  onSaveAndPrint?: () => void;
}

export function StickyActionBar({
  visible,
  dirtyCount,
  saving = false,
  savingAndPrinting = false,
  onReset,
  onSaveAll,
  onSaveAndReturn,
  onSaveAndPrint,
}: StickyActionBarProps) {
  const { t } = useAppTranslation(["productQuery", "common"]);

  if (!visible) {
    return null;
  }

  const busy = saving || savingAndPrinting;
  // 发票上下文优先「保存并返回」；否则有打印入口时主按钮为「保存并打印」。
  const primary = onSaveAndReturn
    ? { label: t("actions.saveAndReturnToInvoice"), icon: "arrow-left", onPress: onSaveAndReturn }
    : onSaveAndPrint
      ? { label: t("actions.saveAndPrint"), icon: "printer-outline", onPress: onSaveAndPrint }
      : null;

  return (
    <Surface style={styles.container} elevation={2}>
      <View style={styles.countRow}>
        <View style={styles.dot} />
        <Text style={styles.countText} numberOfLines={1}>
          {t("multiCode.dirtyCount", { count: dirtyCount })}
        </Text>
        <Button compact onPress={onReset} disabled={busy}>
          {t("actions.reset")}
        </Button>
      </View>
      <View style={styles.actions}>
        <Button
          mode={primary ? "outlined" : "contained"}
          onPress={onSaveAll}
          loading={saving && !savingAndPrinting}
          disabled={busy}
          style={styles.button}
          contentStyle={styles.buttonContent}
        >
          {saving && !savingAndPrinting
            ? t("common:actions.saving")
            : primary ? t("common:actions.save") : t("common:actions.saveAll")}
        </Button>
        {primary ? (
          <Button
            mode="contained"
            icon={primary.icon}
            onPress={primary.onPress}
            loading={savingAndPrinting}
            disabled={busy}
            style={[styles.button, styles.primaryButton]}
            contentStyle={styles.buttonContent}
          >
            {primary.label}
          </Button>
        ) : null}
      </View>
    </Surface>
  );
}

const styles = StyleSheet.create({
  container: {
    gap: HB_SPACING.xxs,
    paddingHorizontal: HB_SPACING.md,
    paddingTop: HB_SPACING.xxs,
    paddingBottom: HB_SPACING.xs,
    backgroundColor: HB_COLORS.white,
    borderTopWidth: StyleSheet.hairlineWidth,
    borderTopColor: HB_COLORS.outlineMuted,
  },
  countRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: 6,
  },
  dot: {
    width: 8,
    height: 8,
    borderRadius: 4,
    backgroundColor: "#F79009",
  },
  countText: {
    flex: 1,
    minWidth: 0,
    fontSize: 13,
    fontWeight: "600",
    color: HB_COLORS.textPrimary,
  },
  actions: {
    flexDirection: "row",
    alignItems: "center",
    gap: HB_SPACING.xs,
  },
  button: {
    flex: 1,
    borderRadius: HB_RADIUS.control,
  },
  primaryButton: {
    flex: 1.6,
  },
  buttonContent: {
    minHeight: 44,
  },
});
