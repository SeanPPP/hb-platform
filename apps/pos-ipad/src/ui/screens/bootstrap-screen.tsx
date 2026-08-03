import { MaterialCommunityIcons } from "@expo/vector-icons";
import { StatusBar } from "expo-status-bar";
import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  StyleSheet,
  Text,
  useWindowDimensions,
  View,
} from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";

import {
  loadExpoBootstrapServerDiagnostics,
  type BootstrapServerDiagnostics,
} from "@/core/runtime/expo-bootstrap-server-diagnostics";
import { usePosRuntime } from "@/core/runtime/pos-runtime-context";
import { serverConnectionPanelCopy } from "@/features/device-registration/server-connection-copy";
import { ServerConnectionPanel } from "@/features/device-registration/server-connection-panel";
import { PosPressable } from "@/ui/controls/pos-pressable";
import { usePosShellStore } from "@/ui/shell/pos-shell-store";
import { PosStatusStrip } from "@/ui/shell/status-strip";
import { posColors } from "@/ui/theme";

type ReadinessCardProps = {
  icon: keyof typeof MaterialCommunityIcons.glyphMap;
  label: string;
  ready: boolean;
  statusText?: string;
};

function ReadinessCard({
  icon,
  label,
  ready,
  statusText,
}: ReadinessCardProps) {
  const { t } = useTranslation();

  return (
    <View style={styles.card}>
      <View style={[styles.iconFrame, ready ? styles.iconReady : styles.iconPending]}>
        <MaterialCommunityIcons
          color={ready ? posColors.green : posColors.blue}
          name={icon}
          size={28}
        />
      </View>
      <View style={styles.cardCopy}>
        <Text style={styles.cardLabel}>{label}</Text>
        <Text style={[styles.cardState, ready ? styles.stateReady : styles.statePending]}>
          {statusText ?? (ready ? t("bootstrap.ready") : t("bootstrap.pending"))}
        </Text>
      </View>
    </View>
  );
}

export function BootstrapScreen() {
  const { t } = useTranslation();
  const { width } = useWindowDimensions();
  const { retry, state: runtime } = usePosRuntime();
  const [serverDiagnostics, setServerDiagnostics] =
    useState<BootstrapServerDiagnostics | null>(null);
  const serverProbe = useRef<AbortController | null>(null);
  const display = usePosShellStore((state) => state.display);
  const compact = width < 900;
  const backendReady = runtime.backend === "reachable";
  const databaseReady = runtime.database === "ready";
  const deviceReady =
    runtime.device === "authorized-local" ||
    runtime.device === "authorized-online";

  useEffect(() => {
    let active = true;
    void loadExpoBootstrapServerDiagnostics()
      .then((diagnostics) => {
        if (active) setServerDiagnostics(diagnostics);
      })
      .catch(() => {
        // 公开配置自身损坏时仍保留原始启动错误与重试入口。
      });
    return () => {
      active = false;
      serverProbe.current?.abort();
    };
  }, []);

  return (
    <SafeAreaView style={styles.safeArea}>
      <StatusBar style="dark" />
      <PosStatusStrip />
      <View style={[styles.page, compact && styles.pageCompact]}>
        <View style={styles.brandRail}>
          <View style={styles.brandMark}>
            <Text style={styles.brandLetters}>HB</Text>
          </View>
          <View>
            <Text style={styles.brandName}>{t("app.name")}</Text>
            <Text style={styles.eyebrow}>{t("bootstrap.eyebrow")}</Text>
          </View>
        </View>

        <View style={styles.hero}>
          <View style={styles.orangeRule} />
          <Text style={styles.title}>{t("bootstrap.title")}</Text>
          <Text style={styles.subtitle}>{t("bootstrap.subtitle")}</Text>
        </View>

        <View style={[styles.cardGrid, compact && styles.cardGridCompact]}>
          <ReadinessCard
            icon="table-account"
            label={t("bootstrap.device")}
            ready={deviceReady}
            statusText={t(`bootstrap.deviceState.${runtime.device}`)}
          />
          <ReadinessCard
            icon="server-security"
            label={t("bootstrap.backend")}
            ready={backendReady}
            statusText={t(`bootstrap.backendState.${runtime.backend}`)}
          />
          <ReadinessCard
            icon="database-lock"
            label={t("bootstrap.offline")}
            ready={databaseReady}
            statusText={t(`bootstrap.databaseState.${runtime.database}`)}
          />
          <ReadinessCard
            icon="monitor-multiple"
            label={t("bootstrap.display")}
            ready={display === "ready"}
            statusText={t(`status.peripheral.${display}`)}
          />
        </View>

        {runtime.phase === "failed" && serverDiagnostics ? (
          <View style={styles.serverDiagnostics}>
            <ServerConnectionPanel
              canSave={false}
              copy={serverConnectionPanelCopy(t)}
              currentAddress={serverDiagnostics.currentApiBaseUrl}
              saveAddress={() =>
                Promise.reject(
                  new Error("BOOTSTRAP_SERVER_SWITCH_SAFETY_UNAVAILABLE"),
                )
              }
              testAddress={(address) => {
                const controller = new AbortController();
                serverProbe.current?.abort();
                serverProbe.current = controller;
                return serverDiagnostics.test(address, controller.signal);
              }}
            />
          </View>
        ) : null}

        <View style={styles.footer}>
          <View style={styles.footerDot} />
          <View style={styles.footerCopy}>
            <Text style={styles.footerText}>
              {runtime.error ?? t("bootstrap.footer")}
            </Text>
            {runtime.phase === "failed" ? (
              <PosPressable
                accessibilityRole="button"
                onPress={() => {
                  void retry().catch(() => undefined);
                }}
                sound="tap"
                style={({ pressed }) => [
                  styles.retryButton,
                  pressed && styles.retryButtonPressed,
                ]}
              >
                <Text style={styles.retryLabel}>{t("bootstrap.retry")}</Text>
              </PosPressable>
            ) : null}
          </View>
        </View>
      </View>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safeArea: {
    flex: 1,
    backgroundColor: posColors.canvas,
  },
  page: {
    flex: 1,
    paddingHorizontal: 48,
    paddingVertical: 30,
  },
  pageCompact: {
    paddingHorizontal: 28,
    paddingVertical: 22,
  },
  brandRail: {
    flexDirection: "row",
    alignItems: "center",
    gap: 14,
  },
  brandMark: {
    width: 48,
    height: 48,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: posColors.ink,
  },
  brandLetters: {
    color: "#FFFFFF",
    fontSize: 18,
    fontWeight: "800",
    letterSpacing: 0.5,
  },
  brandName: {
    color: posColors.ink,
    fontSize: 20,
    fontWeight: "800",
  },
  eyebrow: {
    marginTop: 2,
    color: posColors.orange,
    fontSize: 10,
    fontWeight: "800",
    letterSpacing: 1.6,
  },
  hero: {
    maxWidth: 760,
    marginTop: 56,
  },
  orangeRule: {
    width: 72,
    height: 5,
    marginBottom: 22,
    backgroundColor: posColors.orange,
  },
  title: {
    color: posColors.ink,
    fontSize: 44,
    fontWeight: "800",
    letterSpacing: -1.2,
    lineHeight: 50,
  },
  subtitle: {
    maxWidth: 680,
    marginTop: 18,
    color: posColors.mutedInk,
    fontSize: 18,
    lineHeight: 28,
  },
  cardGrid: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 16,
    marginTop: 44,
  },
  cardGridCompact: {
    marginTop: 30,
  },
  serverDiagnostics: {
    marginTop: 20,
    maxWidth: 720,
  },
  card: {
    minWidth: 220,
    flex: 1,
    maxWidth: 310,
    flexDirection: "row",
    alignItems: "center",
    gap: 14,
    paddingHorizontal: 18,
    paddingVertical: 16,
    borderWidth: 1,
    borderColor: posColors.border,
    backgroundColor: posColors.surface,
  },
  iconFrame: {
    width: 48,
    height: 48,
    alignItems: "center",
    justifyContent: "center",
  },
  iconReady: {
    backgroundColor: posColors.greenSoft,
  },
  iconPending: {
    backgroundColor: posColors.blueSoft,
  },
  cardCopy: {
    flex: 1,
  },
  cardLabel: {
    color: posColors.ink,
    fontSize: 15,
    fontWeight: "700",
  },
  cardState: {
    marginTop: 4,
    fontSize: 12,
    fontWeight: "700",
  },
  stateReady: {
    color: posColors.green,
  },
  statePending: {
    color: posColors.blue,
  },
  footer: {
    flexDirection: "row",
    alignItems: "flex-start",
    gap: 9,
    marginTop: "auto",
  },
  footerCopy: {
    flex: 1,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 16,
  },
  footerDot: {
    width: 7,
    height: 7,
    borderRadius: 4,
    backgroundColor: posColors.orange,
  },
  footerText: {
    color: posColors.mutedInk,
    fontSize: 12,
    fontWeight: "600",
    letterSpacing: 0.4,
  },
  retryButton: {
    minHeight: 40,
    minWidth: 112,
    alignItems: "center",
    justifyContent: "center",
    paddingHorizontal: 18,
    backgroundColor: posColors.ink,
  },
  retryButtonPressed: {
    opacity: 0.78,
  },
  retryLabel: {
    color: "#FFFFFF",
    fontSize: 13,
    fontWeight: "800",
  },
});
