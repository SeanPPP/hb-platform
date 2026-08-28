import { MaterialCommunityIcons } from "@expo/vector-icons";
import { useTranslation } from "react-i18next";
import { ScrollView, StyleSheet, Text, View } from "react-native";

import { usePosShellStore } from "./pos-shell-store";

import { PosPressable } from "@/ui/controls/pos-pressable";
import { posColors } from "@/ui/theme";

type IndicatorTone = "good" | "warning" | "neutral" | "danger";
type StatusIconName = keyof typeof MaterialCommunityIcons.glyphMap;

type StatusIndicatorProps = {
  icon: StatusIconName;
  label: string;
  testID: string;
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

function StatusIndicator({
  icon,
  label,
  testID,
  tone,
  value,
}: StatusIndicatorProps) {
  return (
    <View
      accessibilityLabel={`${label}: ${value}`}
      style={styles.indicator}
      testID={`status-strip-indicator-${testID}`}
    >
      <View style={[styles.dot, toneStyles[tone]]} />
      <MaterialCommunityIcons
        accessible={false}
        color="#B9C5D1"
        name={icon}
        size={14}
      />
      <Text
        numberOfLines={1}
        style={styles.value}
        testID={`status-strip-indicator-${testID}-value`}
      >
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
    pendingSync,
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
    <View style={styles.container} testID="status-strip">
      <ScrollView
        contentContainerStyle={styles.scrollContent}
        horizontal
        showsHorizontalScrollIndicator={false}
        style={styles.scroll}
        testID="status-strip-scroll"
      >
        <StatusIndicator
          icon="cellphone"
          label={t("status.device")}
          testID="device"
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
          testID="network"
          tone={connectivity === "online" ? "good" : connectivity === "offline" ? "danger" : "neutral"}
          value={t(`status.network.${connectivity}`)}
        />
        <StatusIndicator
          icon="sync"
          label={t("status.sync")}
          testID="sync"
          tone={
            pendingSync.kind === "ready"
              ? pendingSync.count === 0
                ? "good"
                : "warning"
              : "neutral"
          }
          value={
            pendingSync.kind === "ready"
              ? t("status.sync.pending", { count: pendingSync.count })
              : t(`status.sync.${pendingSync.kind}`)
          }
        />
        <StatusIndicator
          icon="printer-outline"
          label={t("status.printer")}
          testID="printer"
          tone={printer === "ready" ? "good" : printer === "failed" ? "danger" : "neutral"}
          value={t(`status.peripheral.${printer}`)}
        />
        <StatusIndicator
          icon="barcode-scan"
          label={t("status.scanner")}
          testID="scanner"
          tone={scanner === "capturing" || scanner === "camera" ? "good" : "neutral"}
          value={t(`status.scanner.${scanner}`)}
        />
      </ScrollView>
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
    minHeight: 42,
  },
  scroll: { flex: 1, minWidth: 0 },
  scrollContent: {
    alignItems: "center",
    gap: 16,
    minHeight: 42,
    paddingHorizontal: 12,
  },
  dot: {
    borderRadius: 4,
    height: 7,
    width: 7,
  },
  indicator: {
    alignItems: "center",
    flexShrink: 0,
    flexDirection: "row",
    gap: 6,
    minWidth: 0,
  },
  languageButton: {
    alignItems: "center",
    flexShrink: 0,
    justifyContent: "center",
    minHeight: 48,
    minWidth: 48,
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
    flexShrink: 0,
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
    flexShrink: 0,
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
