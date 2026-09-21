import { Pressable, StyleSheet, View } from "react-native";
import { Icon, Text } from "react-native-paper";
import type { PrinterConnectionState } from "@/modules/printer/state";
import type { SavedPrinter } from "@/modules/printer/types";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

interface PrinterConnectionStatusProps {
  savedPrinter: SavedPrinter | null;
  status: PrinterConnectionState;
  lastError: string | null;
  onPress?: () => void;
}

const STATUS_PRESENTATION: Record<
  PrinterConnectionState,
  { icon: string; color: string }
> = {
  idle: { icon: "printer-outline", color: "#667085" },
  connecting: { icon: "bluetooth-connect", color: "#0958D9" },
  connected: { icon: "printer-check", color: "#027A48" },
  reconnecting: { icon: "sync", color: "#B54708" },
  disconnected: { icon: "printer-off-outline", color: "#B54708" },
  paused: { icon: "pause-circle-outline", color: "#667085" },
  error: { icon: "alert-circle-outline", color: "#B42318" },
};

export function PrinterConnectionStatus({
  savedPrinter,
  status,
  lastError,
  onPress,
}: PrinterConnectionStatusProps) {
  const { t } = useAppTranslation(["productQuery"]);
  const effectiveStatus = savedPrinter ? status : "idle";
  const presentation = STATUS_PRESENTATION[effectiveStatus];
  const statusLabel = savedPrinter
    ? t(`print.printerStatus.${effectiveStatus}`)
    : t("print.printerStatus.unselected");
  const printerName = savedPrinter?.name?.trim() || savedPrinter?.address;
  // 原生错误可能包含堆栈或驱动细节；这里只提示处理方向，避免把底层信息直接展示给操作员。
  const errorHint = effectiveStatus === "error" && lastError
    ? t("print.printerStatus.errorHint")
    : null;
  const summary = [statusLabel, printerName, errorHint].filter(Boolean).join(" · ");

  return (
    <Pressable
      accessibilityRole={onPress ? "button" : undefined}
      accessibilityLabel={summary}
      accessibilityHint={onPress ? t("print.printerStatus.openSettingsHint") : undefined}
      accessibilityLiveRegion="polite"
      disabled={!onPress}
      onPress={onPress}
      style={({ pressed }) => [styles.container, pressed && onPress ? styles.pressed : null]}
    >
      <Icon source={presentation.icon} size={18} color={presentation.color} />
      <View style={styles.copy}>
        <Text
          variant="labelMedium"
          style={[styles.status, { color: presentation.color }]}
        >
          {statusLabel}
        </Text>
        {printerName ? (
          <Text variant="bodySmall" style={styles.name} numberOfLines={1}>
            {printerName}
          </Text>
        ) : null}
        {errorHint ? (
          <Text variant="bodySmall" style={styles.errorHint} numberOfLines={1}>
            {errorHint}
          </Text>
        ) : null}
      </View>
      {onPress ? <Icon source="chevron-right" size={18} color="#98A2B3" /> : null}
    </Pressable>
  );
}

const styles = StyleSheet.create({
  container: {
    minHeight: 38,
    flexDirection: "row",
    alignItems: "center",
    gap: 8,
    paddingHorizontal: 12,
    paddingVertical: 7,
    borderWidth: 1,
    borderColor: "#E4E7EC",
    borderRadius: 12,
    backgroundColor: "#FFFFFF",
  },
  pressed: {
    opacity: 0.72,
  },
  copy: {
    flex: 1,
    minWidth: 0,
    flexDirection: "row",
    alignItems: "center",
    gap: 6,
  },
  status: {
    fontWeight: "700",
  },
  name: {
    flexShrink: 1,
    color: "#475467",
  },
  errorHint: {
    flexShrink: 1,
    color: "#B42318",
  },
});
