import { useTranslation } from "react-i18next";
import { StyleSheet, Text, View } from "react-native";

import { usePosShellStore } from "./pos-shell-store";

import { posColors } from "@/ui/theme";

type IndicatorTone = "good" | "warning" | "neutral" | "danger";

type StatusIndicatorProps = {
  label: string;
  value: string;
  tone: IndicatorTone;
};

function StatusIndicator({ label, tone, value }: StatusIndicatorProps) {
  return (
    <View
      accessibilityLabel={`${label}: ${value}`}
      style={styles.indicator}
    >
      <View style={[styles.dot, toneStyles[tone]]} />
      <Text numberOfLines={1} style={styles.label}>
        {label}
      </Text>
      <Text numberOfLines={1} style={styles.value}>
        {value}
      </Text>
    </View>
  );
}

export function PosStatusStrip() {
  const { t } = useTranslation();
  const {
    connectivity,
    deviceGate,
    display,
    pendingSyncCount,
    printer,
    scanner,
  } = usePosShellStore();

  return (
    <View style={styles.container}>
      <StatusIndicator
        label={t("status.device")}
        tone={deviceGate === "authorized" ? "good" : deviceGate === "locked" ? "danger" : "warning"}
        value={t(`status.device.${deviceGate}`)}
      />
      <StatusIndicator
        label={t("status.network")}
        tone={connectivity === "online" ? "good" : connectivity === "offline" ? "danger" : "neutral"}
        value={t(`status.network.${connectivity}`)}
      />
      <StatusIndicator
        label={t("status.sync")}
        tone={pendingSyncCount === 0 ? "good" : "warning"}
        value={t("status.sync.pending", { count: pendingSyncCount })}
      />
      <StatusIndicator
        label={t("status.printer")}
        tone={printer === "ready" ? "good" : printer === "failed" ? "danger" : "neutral"}
        value={t(`status.peripheral.${printer}`)}
      />
      <StatusIndicator
        label={t("status.scanner")}
        tone={scanner === "capturing" || scanner === "camera" ? "good" : "neutral"}
        value={t(`status.scanner.${scanner}`)}
      />
      <StatusIndicator
        label={t("status.display")}
        tone={display === "ready" ? "good" : display === "failed" ? "danger" : "neutral"}
        value={t(`status.peripheral.${display}`)}
      />
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
    flexDirection: "row",
    gap: 6,
    minWidth: 0,
  },
  label: {
    color: "#B9C5D1",
    fontSize: 11,
    fontWeight: "700",
  },
  value: {
    color: "#FFFFFF",
    fontSize: 11,
    fontVariant: ["tabular-nums"],
    fontWeight: "700",
  },
});
