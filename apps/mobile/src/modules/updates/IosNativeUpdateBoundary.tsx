import type { ReactNode } from "react";
import { ScrollView, StyleSheet, View } from "react-native";
import { ActivityIndicator, Button, Surface, Text } from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import {
  getIosNativeUpdateBoundaryMode,
  type IosNativeUpdateDecision,
} from "./ios-native-app-update";

type IosNativeUpdateBoundaryProps = {
  enabled: boolean;
  initialized: boolean;
  checking: boolean;
  decision: IosNativeUpdateDecision | null;
  onOpenRequiredUpdate: () => void;
  onRetryRequiredUpdate: () => void;
  children: ReactNode;
};

export function IosNativeUpdateBoundary({
  enabled,
  initialized,
  checking,
  decision,
  onOpenRequiredUpdate,
  onRetryRequiredUpdate,
  children,
}: IosNativeUpdateBoundaryProps) {
  const { t } = useAppTranslation("settings");
  const mode = getIosNativeUpdateBoundaryMode({
    enabled,
    initialized,
    state: decision?.state ?? null,
  });

  if (mode === "content") {
    return children;
  }

  if (mode === "checking") {
    return (
      <SafeAreaView style={styles.screen}>
        <View
          style={styles.checking}
          accessibilityLiveRegion="polite"
          accessibilityLabel={t("dialogs.iosNativeUpdateChecking")}
        >
          <ActivityIndicator size="large" />
          <Text variant="bodyLarge" style={styles.checkingText}>
            {t("dialogs.iosNativeUpdateChecking")}
          </Text>
        </View>
      </SafeAreaView>
    );
  }

  return (
    <SafeAreaView style={styles.screen} accessibilityViewIsModal>
      <ScrollView
        contentContainerStyle={styles.scrollContent}
        bounces={false}
        keyboardShouldPersistTaps="handled"
      >
        <Surface style={styles.panel} elevation={1}>
          <Text variant="labelLarge" style={styles.eyebrow}>
            {t("dialogs.iosNativeUpdateRequiredEyebrow")}
          </Text>
          <Text variant="headlineMedium" style={styles.title} accessibilityRole="header">
            {t("dialogs.iosNativeUpdateRequiredTitle")}
          </Text>
          {decision?.latestVersion ? (
            <View style={styles.versionBadge}>
              <Text variant="labelLarge" style={styles.versionText}>
                {t("dialogs.iosNativeUpdateVersion", {
                  version: decision.latestVersion,
                })}
              </Text>
            </View>
          ) : null}
          <Text variant="bodyLarge" style={styles.message}>
            {decision?.releaseMessage
              || t("dialogs.iosNativeUpdateRequiredMessage")}
          </Text>
          <Button
            mode="contained"
            icon="open-in-new"
            onPress={onOpenRequiredUpdate}
            contentStyle={styles.primaryActionContent}
            accessibilityLabel={t("dialogs.iosNativeUpdateOpenStoreAction")}
          >
            {t("dialogs.iosNativeUpdateOpenStoreAction")}
          </Button>
          <Button
            mode="outlined"
            icon="refresh"
            loading={checking}
            disabled={checking}
            onPress={onRetryRequiredUpdate}
            contentStyle={styles.secondaryActionContent}
            accessibilityLabel={t("dialogs.iosNativeUpdateRetryAction")}
          >
            {t("dialogs.iosNativeUpdateRetryAction")}
          </Button>
          <Text variant="bodySmall" style={styles.helper}>
            {t("dialogs.iosNativeUpdateRequiredHelper")}
          </Text>
        </Surface>
      </ScrollView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  screen: {
    flex: 1,
    backgroundColor: "#F8FBFF",
  },
  checking: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
    gap: 14,
    padding: 24,
  },
  checkingText: {
    color: "#475569",
    textAlign: "center",
  },
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
  eyebrow: {
    color: "#1677FF",
    fontWeight: "700",
    letterSpacing: 0.4,
  },
  title: {
    color: "#0F172A",
    fontWeight: "700",
  },
  versionBadge: {
    alignSelf: "flex-start",
    borderRadius: 8,
    backgroundColor: "#E6F4FF",
    paddingHorizontal: 12,
    paddingVertical: 7,
  },
  versionText: {
    color: "#0958D9",
  },
  message: {
    color: "#334155",
    lineHeight: 26,
  },
  primaryActionContent: {
    minHeight: 48,
  },
  secondaryActionContent: {
    minHeight: 44,
  },
  helper: {
    color: "#64748B",
    lineHeight: 19,
  },
});
