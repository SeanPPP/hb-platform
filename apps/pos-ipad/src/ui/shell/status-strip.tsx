import { MaterialCommunityIcons } from "@expo/vector-icons";
import { useTranslation } from "react-i18next";
import { StyleSheet, Text, View } from "react-native";

import { usePosShellStore } from "./pos-shell-store";

import { PosPressable } from "@/ui/controls/pos-pressable";
import { posColors } from "@/ui/theme";

type IndicatorTone = "good" | "warning" | "neutral" | "danger";
type StatusIconName = keyof typeof MaterialCommunityIcons.glyphMap;

type StatusIndicatorProps = {
  icon: StatusIconName;
  label: string;
  value: string;
  tone: IndicatorTone;
};

type PosStatusStripProps = Readonly<{
  language?: string;
  onSwitchLanguage?: () => void;
  showTerminalIdentity?: boolean;
}>;

type StaticIdentityProps = Readonly<{
  icon: StatusIconName;
  label: string;
  testID: string;
  truncate?: boolean;
  value: string | null | undefined;
}>;

function StatusIndicator({ icon, label, tone, value }: StatusIndicatorProps) {
  return (
    <View
      accessibilityLabel={`${label}: ${value}`}
      style={styles.indicator}
    >
      <View style={[styles.dot, toneStyles[tone]]} />
      <MaterialCommunityIcons
        accessible={false}
        color="#B9C5D1"
        name={icon}
        size={14}
      />
      <Text numberOfLines={1} style={styles.value}>
        {value}
      </Text>
    </View>
  );
}

function StaticIdentity({
  icon,
  label,
  testID,
  truncate = false,
  value,
}: StaticIdentityProps) {
  const displayValue = value?.trim() || "—";

  return (
    <View
      accessibilityLabel={`${label}: ${displayValue}`}
      style={[
        styles.staticIdentity,
        truncate
          ? styles.storeIdentity
          : styles.deviceCodeIdentity,
      ]}
      testID={testID}
    >
      <MaterialCommunityIcons
        accessible={false}
        color="#B9C5D1"
        name={icon}
        size={14}
      />
      <Text
        numberOfLines={1}
        style={[
          styles.value,
          truncate
            ? styles.storeIdentityValue
            : styles.deviceCodeIdentityValue,
        ]}
      >
        {displayValue}
      </Text>
    </View>
  );
}

export function PosStatusStrip({
  language,
  onSwitchLanguage,
  showTerminalIdentity = false,
}: PosStatusStripProps = {}) {
  const { i18n, t } = useTranslation();
  const {
    connectivity,
    deviceGate,
    display,
    pendingSyncCount,
    printer,
    scanner,
    terminalPresentation,
  } = usePosShellStore();
  const currentLanguage =
    language ?? i18n.resolvedLanguage ?? i18n.language;
  const targetLanguageGlyph = currentLanguage
    .toLowerCase()
    .startsWith("zh")
    ? "EN"
    : "中";

  return (
    <View style={styles.container}>
      <StatusIndicator
        icon="tablet"
        label={t("status.device")}
        tone={deviceGate === "authorized" ? "good" : deviceGate === "locked" ? "danger" : "warning"}
        value={t(`status.device.${deviceGate}`)}
      />
      {showTerminalIdentity ? (
        <View
          style={styles.terminalIdentity}
          testID="status-strip-terminal-identity"
        >
          <StaticIdentity
            icon="store-outline"
            label={t("status.storeName")}
            testID="status-strip-store-identity"
            truncate
            value={terminalPresentation?.storeName}
          />
          <StaticIdentity
            icon="identifier"
            label={t("status.deviceCode")}
            testID="status-strip-device-code-identity"
            value={terminalPresentation?.deviceCode}
          />
        </View>
      ) : null}
      <StatusIndicator
        icon="wifi"
        label={t("status.network")}
        tone={connectivity === "online" ? "good" : connectivity === "offline" ? "danger" : "neutral"}
        value={t(`status.network.${connectivity}`)}
      />
      <StatusIndicator
        icon="sync"
        label={t("status.sync")}
        tone={pendingSyncCount === 0 ? "good" : "warning"}
        value={t("status.sync.pending", { count: pendingSyncCount })}
      />
      <StatusIndicator
        icon="printer-outline"
        label={t("status.printer")}
        tone={printer === "ready" ? "good" : printer === "failed" ? "danger" : "neutral"}
        value={t(`status.peripheral.${printer}`)}
      />
      <StatusIndicator
        icon="barcode-scan"
        label={t("status.scanner")}
        tone={scanner === "capturing" || scanner === "camera" ? "good" : "neutral"}
        value={t(`status.scanner.${scanner}`)}
      />
      <StatusIndicator
        icon="monitor"
        label={t("status.display")}
        tone={display === "ready" ? "good" : display === "failed" ? "danger" : "neutral"}
        value={t(`status.peripheral.${display}`)}
      />
      {onSwitchLanguage ? (
        <PosPressable
          accessibilityHint={t("status.languageSwitchHint")}
          accessibilityLabel={t("status.languageSwitchLabel")}
          accessibilityRole="button"
          hitSlop={4}
          onPress={onSwitchLanguage}
          sound="navigate"
          style={({ pressed }) => [
            styles.languageButton,
            pressed && styles.languageButtonPressed,
          ]}
          testID="status-strip-language-switch"
        >
          <Text
            accessible={false}
            style={styles.languageTargetGlyph}
            testID="status-strip-language-icon"
          >
            {targetLanguageGlyph}
          </Text>
        </PosPressable>
      ) : null}
    </View>
  );
}

const toneStyles = StyleSheet.create({
  danger: {
    backgroundColor: posColors.red,
  },
  good: {
    backgroundColor: posColors.green,
  },
  neutral: {
    backgroundColor: posColors.mutedInk,
  },
  warning: {
    backgroundColor: posColors.orange,
  },
});

const styles = StyleSheet.create({
  container: {
    alignItems: "center",
    backgroundColor: posColors.ink,
    flexDirection: "row",
    gap: 20,
    minHeight: 42,
    paddingHorizontal: 24,
  },
  dot: {
    borderRadius: 4,
    height: 7,
    width: 7,
  },
  indicator: {
    alignItems: "center",
    flexShrink: 1,
    flexDirection: "row",
    gap: 6,
    minWidth: 0,
  },
  languageButton: {
    alignItems: "center",
    flexShrink: 0,
    justifyContent: "center",
    marginLeft: "auto",
    minHeight: 44,
    minWidth: 44,
  },
  languageButtonPressed: {
    backgroundColor: posColors.mutedInk,
  },
  languageTargetGlyph: {
    color: "#FFFFFF",
    fontSize: 13,
    fontWeight: "900",
    lineHeight: 16,
  },
  staticIdentity: {
    alignItems: "center",
    flexDirection: "row",
    gap: 4,
    minWidth: 0,
  },
  storeIdentity: {
    flexShrink: 1,
  },
  storeIdentityValue: {
    flexShrink: 1,
    minWidth: 0,
  },
  deviceCodeIdentity: {
    flexShrink: 0,
  },
  deviceCodeIdentityValue: {
    flexShrink: 0,
  },
  terminalIdentity: {
    alignItems: "center",
    flexDirection: "row",
    flexShrink: 1,
    gap: 12,
    minWidth: 0,
  },
  value: {
    color: "#FFFFFF",
    fontSize: 11,
    fontVariant: ["tabular-nums"],
    fontWeight: "700",
  },
});
