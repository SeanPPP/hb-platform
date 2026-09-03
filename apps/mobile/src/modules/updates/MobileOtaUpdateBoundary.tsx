import { createContext, useContext, type ReactNode } from "react";
import { ScrollView, StyleSheet, View } from "react-native";
import { ActivityIndicator, Button, Surface, Text } from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import {
  getMobileOtaBoundaryMode,
  type MobileOtaManualCheckResult,
  type MobileOtaUpdateDecision,
} from "./mobile-ota-update";

type MobileOtaManualCheck = () => Promise<MobileOtaManualCheckResult>;

const MobileOtaManualCheckContext = createContext<MobileOtaManualCheck>(
  async () => Object.freeze({ status: "disabled" }),
);

export function useMobileOtaManualCheck() {
  return useContext(MobileOtaManualCheckContext);
}

type MobileOtaUpdateBoundaryProps = Readonly<{
  enabled: boolean;
  initialized: boolean;
  checking: boolean;
  downloading: boolean;
  applying: boolean;
  downloaded: boolean;
  decision: MobileOtaUpdateDecision | null;
  lastError: string | null;
  onManualCheck: MobileOtaManualCheck;
  onDownload: () => void;
  onRestart: () => void;
  onRetry: () => void;
  children: ReactNode;
}>;

export function MobileOtaUpdateBoundary(props: MobileOtaUpdateBoundaryProps) {
  const { t } = useAppTranslation("settings");
  const mode = getMobileOtaBoundaryMode({
    enabled: props.enabled,
    initialized: props.initialized,
    state: props.decision?.state ?? null,
  });

  if (mode === "content") {
    return (
      <MobileOtaManualCheckContext.Provider value={props.onManualCheck}>
        {props.children}
      </MobileOtaManualCheckContext.Provider>
    );
  }

  if (mode === "checking") {
    return (
      <SafeAreaView style={styles.screen}>
        <View style={styles.checking} accessibilityLiveRegion="polite">
          <ActivityIndicator size="large" />
          <Text variant="bodyLarge" style={styles.checkingText}>
            {t("dialogs.mobileOtaChecking")}
          </Text>
        </View>
      </SafeAreaView>
    );
  }

  const busy = props.checking || props.downloading || props.applying;
  return (
    <SafeAreaView style={styles.screen} accessibilityViewIsModal>
      <ScrollView contentContainerStyle={styles.scrollContent} bounces={false}>
        <Surface style={styles.panel} elevation={1}>
          <Text variant="labelLarge" style={styles.eyebrow}>
            {t("dialogs.mobileOtaRequiredEyebrow")}
          </Text>
          <Text variant="headlineMedium" style={styles.title} accessibilityRole="header">
            {t("dialogs.mobileOtaRequiredTitle")}
          </Text>
          <View style={styles.versionBadge}>
            <Text variant="labelLarge" style={styles.versionText}>
              {t("dialogs.mobileOtaRuntime", {
                runtime: props.decision?.runtimeVersion ?? "--",
              })}
            </Text>
          </View>
          <Text variant="bodyLarge" style={styles.message}>
            {props.decision?.releaseMessage || t("dialogs.mobileOtaRequiredMessage")}
          </Text>
          <Button
            mode="contained"
            icon={props.downloaded ? "restart" : "download"}
            loading={busy}
            disabled={busy}
            onPress={props.downloaded ? props.onRestart : props.onDownload}
            contentStyle={styles.primaryActionContent}
          >
            {props.downloaded
              ? t("dialogs.mobileOtaRestartAction")
              : t("dialogs.mobileOtaDownloadAction")}
          </Button>
          <Button
            mode="outlined"
            icon="refresh"
            disabled={busy}
            onPress={props.onRetry}
            contentStyle={styles.secondaryActionContent}
          >
            {t("dialogs.mobileOtaRetryAction")}
          </Button>
          {props.lastError ? (
            <Text variant="bodySmall" style={styles.error} accessibilityLiveRegion="polite">
              {t("dialogs.mobileOtaRetryHelper")}
            </Text>
          ) : null}
          <Text variant="bodySmall" style={styles.helper}>
            {t("dialogs.mobileOtaRequiredHelper")}
          </Text>
        </Surface>
      </ScrollView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: "#F8FBFF" },
  checking: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
    gap: 14,
    padding: 24,
  },
  checkingText: { color: "#475569", textAlign: "center" },
  scrollContent: {
    flexGrow: 1,
    justifyContent: "center",
    paddingHorizontal: 20,
    paddingVertical: 28,
  },
  panel: {
    width: "100%",
    maxWidth: 520,
    alignSelf: "center",
    gap: 16,
    borderRadius: 16,
    backgroundColor: "#FFFFFF",
    paddingHorizontal: 24,
    paddingVertical: 28,
  },
  eyebrow: { color: "#D4380D", fontWeight: "700", letterSpacing: 0.4 },
  title: { color: "#0F172A", fontWeight: "700" },
  versionBadge: {
    alignSelf: "flex-start",
    borderRadius: 8,
    backgroundColor: "#FFF2E8",
    paddingHorizontal: 12,
    paddingVertical: 7,
  },
  versionText: { color: "#AD2102" },
  message: { color: "#334155", lineHeight: 26 },
  primaryActionContent: { minHeight: 48 },
  secondaryActionContent: { minHeight: 44 },
  error: { color: "#CF1322", lineHeight: 19 },
  helper: { color: "#64748B", lineHeight: 19 },
});
