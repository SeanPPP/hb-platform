import { useTranslation } from "react-i18next";
import {
  ActivityIndicator,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  View,
} from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";

import {
  UPDATE_RECOVERY_SNAPSHOT_UNAVAILABLE,
  type AppUpdateRecoverySnapshot,
} from "./app-update-recovery-contract";

import { posColors } from "@/ui/theme";

export type AppUpdateRecoverySection = "settings" | "support";

export type AppUpdateRecoveryScreenState =
  | Readonly<{ kind: "loading" }>
  | Readonly<{
      kind: "error";
      errorCode: typeof UPDATE_RECOVERY_SNAPSHOT_UNAVAILABLE;
    }>
  | Readonly<{
      kind: "ready";
      snapshot: AppUpdateRecoverySnapshot;
    }>;

type AppUpdateRecoveryScreenProps = Readonly<{
  section: AppUpdateRecoverySection;
  state: AppUpdateRecoveryScreenState;
  exporting: boolean;
  exportError: boolean;
  onSelectSection(section: AppUpdateRecoverySection): void;
  onOpenRegistration(): void;
  onRetry(): void;
  onExport(): void;
}>;

const copy = {
  en: {
    eyebrow: "UPDATE RECOVERY",
    title: "Terminal settings and support",
    subtitle:
      "Read-only diagnostics stay available while sales pages are locked for a required update.",
    settings: "Settings",
    support: "Support",
    registration: "Device registration",
    retry: "Retry diagnostics",
    export: "Export diagnostics",
    exporting: "Exporting…",
    exportFailed: "Diagnostics export failed. Please try again.",
    unavailable: "Diagnostics are temporarily unavailable.",
    unavailableCode: "Error code",
    supportTitle: "Update support",
    supportBody:
      "This export contains only version, connection and masked device details. It does not include credentials, orders, payments or audit records.",
    labels: {
      appVersion: "App version",
      buildNumber: "Build",
      runtimeVersion: "Runtime",
      channel: "OTA channel",
      apiOrigin: "API origin",
      backendState: "Backend",
      deviceState: "Device state",
    },
  },
  zh: {
    eyebrow: "更新恢复",
    title: "终端设置与支持",
    subtitle: "强制更新锁定业务页面期间，仍可查看只读诊断并处理设备注册。",
    settings: "设置",
    support: "支持",
    registration: "设备注册",
    retry: "重试诊断",
    export: "导出诊断",
    exporting: "正在导出…",
    exportFailed: "诊断导出失败，请重试。",
    unavailable: "诊断信息暂不可用。",
    unavailableCode: "错误代码",
    supportTitle: "更新支持",
    supportBody:
      "导出内容仅包含版本、连接状态和脱敏设备信息，不包含凭据、订单、支付或审计记录。",
    labels: {
      appVersion: "应用版本",
      buildNumber: "构建号",
      runtimeVersion: "Runtime",
      channel: "OTA Channel",
      apiOrigin: "API Origin",
      backendState: "后台状态",
      deviceState: "设备状态",
    },
  },
} as const;

const SNAPSHOT_FIELDS = [
  "appVersion",
  "buildNumber",
  "runtimeVersion",
  "channel",
  "apiOrigin",
  "backendState",
  "deviceState",
] as const satisfies readonly (keyof AppUpdateRecoverySnapshot)[];

export function AppUpdateRecoveryScreen({
  section,
  state,
  exporting,
  exportError,
  onSelectSection,
  onOpenRegistration,
  onRetry,
  onExport,
}: AppUpdateRecoveryScreenProps) {
  const { i18n } = useTranslation();
  const text = (i18n.resolvedLanguage ?? i18n.language)
    .toLowerCase()
    .startsWith("zh")
    ? copy.zh
    : copy.en;

  return (
    <SafeAreaView
      style={styles.safeArea}
      testID="app-update-recovery-screen"
    >
      <View style={styles.page}>
        <View style={styles.header}>
          <View style={styles.titleGroup}>
            <Text style={styles.eyebrow}>{text.eyebrow}</Text>
            <Text style={styles.title}>{text.title}</Text>
            <Text style={styles.subtitle}>{text.subtitle}</Text>
          </View>
          <Pressable
            accessibilityRole="button"
            onPress={onOpenRegistration}
            style={styles.registrationButton}
            testID="app-update-recovery-registration"
          >
            <Text style={styles.registrationButtonText}>
              {text.registration}
            </Text>
          </Pressable>
        </View>

        <View style={styles.workspace}>
          <View style={styles.navigation}>
            {(["settings", "support"] as const).map((item) => (
              <Pressable
                accessibilityRole="tab"
                accessibilityState={{ selected: section === item }}
                key={item}
                onPress={() => onSelectSection(item)}
                style={[
                  styles.navigationButton,
                  section === item
                    ? styles.navigationButtonSelected
                    : null,
                ]}
                testID={`app-update-recovery-nav-${item}`}
              >
                <Text
                  style={[
                    styles.navigationButtonText,
                    section === item
                      ? styles.navigationButtonTextSelected
                      : null,
                  ]}
                >
                  {text[item]}
                </Text>
              </Pressable>
            ))}
          </View>

          <ScrollView
            contentContainerStyle={styles.content}
            showsVerticalScrollIndicator={false}
          >
            {state.kind === "loading" ? (
              <View style={styles.centerState}>
                <ActivityIndicator color={posColors.orange} size="large" />
              </View>
            ) : state.kind === "error" ? (
              <View style={styles.errorCard}>
                <Text style={styles.errorTitle}>{text.unavailable}</Text>
                <Text style={styles.errorCode}>
                  {text.unavailableCode}: {state.errorCode}
                </Text>
                <Pressable
                  accessibilityRole="button"
                  onPress={onRetry}
                  style={styles.secondaryButton}
                  testID="app-update-recovery-retry"
                >
                  <Text style={styles.secondaryButtonText}>
                    {text.retry}
                  </Text>
                </Pressable>
              </View>
            ) : (
              <>
                {section === "support" ? (
                  <View style={styles.supportCard}>
                    <Text style={styles.supportTitle}>
                      {text.supportTitle}
                    </Text>
                    <Text style={styles.supportBody}>
                      {text.supportBody}
                    </Text>
                    {exportError ? (
                      <Text
                        accessibilityRole="alert"
                        style={styles.exportError}
                        testID="app-update-recovery-export-error"
                      >
                        {text.exportFailed}
                      </Text>
                    ) : null}
                    <Pressable
                      accessibilityRole="button"
                      disabled={exporting}
                      onPress={onExport}
                      style={[
                        styles.primaryButton,
                        exporting ? styles.disabledButton : null,
                      ]}
                      testID="app-update-recovery-export"
                    >
                      <Text style={styles.primaryButtonText}>
                        {exporting ? text.exporting : text.export}
                      </Text>
                    </Pressable>
                  </View>
                ) : null}
                <View style={styles.detailsCard}>
                  {SNAPSHOT_FIELDS.map((field) => (
                    <View key={field} style={styles.detailRow}>
                      <Text style={styles.detailLabel}>
                        {text.labels[field]}
                      </Text>
                      <Text
                        selectable
                        style={styles.detailValue}
                        testID={`app-update-recovery-${field}`}
                      >
                        {state.snapshot[field]}
                      </Text>
                    </View>
                  ))}
                </View>
              </>
            )}
          </ScrollView>
        </View>
      </View>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safeArea: { flex: 1, backgroundColor: posColors.canvas },
  page: { flex: 1, paddingHorizontal: 28, paddingVertical: 24 },
  header: {
    alignItems: "flex-start",
    flexDirection: "row",
    gap: 24,
    justifyContent: "space-between",
    marginBottom: 22,
  },
  titleGroup: { flex: 1, maxWidth: 760 },
  eyebrow: {
    color: posColors.orange,
    fontSize: 13,
    fontWeight: "800",
    letterSpacing: 1.2,
  },
  title: {
    color: posColors.ink,
    fontSize: 30,
    fontWeight: "800",
    marginTop: 7,
  },
  subtitle: {
    color: posColors.mutedInk,
    fontSize: 16,
    lineHeight: 23,
    marginTop: 8,
  },
  registrationButton: {
    alignItems: "center",
    borderColor: posColors.border,
    borderWidth: 1,
    justifyContent: "center",
    minHeight: 48,
    paddingHorizontal: 20,
  },
  registrationButtonText: {
    color: posColors.ink,
    fontSize: 16,
    fontWeight: "700",
  },
  workspace: {
    backgroundColor: posColors.surface,
    borderColor: posColors.border,
    borderWidth: 1,
    flex: 1,
    flexDirection: "row",
    minHeight: 0,
  },
  navigation: {
    borderRightColor: posColors.border,
    borderRightWidth: 1,
    gap: 8,
    padding: 16,
    width: 190,
  },
  navigationButton: {
    justifyContent: "center",
    minHeight: 52,
    paddingHorizontal: 16,
  },
  navigationButtonSelected: {
    backgroundColor: posColors.orangeSoft,
    borderLeftColor: posColors.orange,
    borderLeftWidth: 4,
  },
  navigationButtonText: {
    color: posColors.mutedInk,
    fontSize: 16,
    fontWeight: "700",
  },
  navigationButtonTextSelected: { color: posColors.ink },
  content: { gap: 16, padding: 22 },
  centerState: {
    alignItems: "center",
    justifyContent: "center",
    minHeight: 280,
  },
  detailsCard: {
    borderColor: posColors.border,
    borderWidth: 1,
  },
  detailRow: {
    alignItems: "center",
    borderBottomColor: posColors.border,
    borderBottomWidth: StyleSheet.hairlineWidth,
    flexDirection: "row",
    minHeight: 52,
    paddingHorizontal: 16,
    paddingVertical: 10,
  },
  detailLabel: {
    color: posColors.mutedInk,
    fontSize: 14,
    fontWeight: "700",
    width: 160,
  },
  detailValue: {
    color: posColors.ink,
    flex: 1,
    fontSize: 15,
    lineHeight: 21,
  },
  supportCard: {
    backgroundColor: posColors.blueSoft,
    padding: 20,
  },
  supportTitle: {
    color: posColors.ink,
    fontSize: 20,
    fontWeight: "800",
  },
  supportBody: {
    color: posColors.mutedInk,
    fontSize: 15,
    lineHeight: 22,
    marginTop: 8,
    maxWidth: 680,
  },
  exportError: {
    color: posColors.red,
    fontSize: 15,
    fontWeight: "700",
    lineHeight: 22,
    marginTop: 12,
  },
  primaryButton: {
    alignItems: "center",
    alignSelf: "flex-start",
    backgroundColor: posColors.orange,
    justifyContent: "center",
    marginTop: 16,
    minHeight: 48,
    minWidth: 160,
    paddingHorizontal: 20,
  },
  primaryButtonText: {
    color: "#FFFFFF",
    fontSize: 16,
    fontWeight: "800",
  },
  secondaryButton: {
    alignItems: "center",
    alignSelf: "flex-start",
    borderColor: posColors.red,
    borderWidth: 1,
    justifyContent: "center",
    marginTop: 16,
    minHeight: 48,
    paddingHorizontal: 18,
  },
  secondaryButtonText: {
    color: posColors.red,
    fontSize: 15,
    fontWeight: "800",
  },
  errorCard: {
    backgroundColor: posColors.redSoft,
    padding: 20,
  },
  errorTitle: {
    color: posColors.red,
    fontSize: 19,
    fontWeight: "800",
  },
  errorCode: {
    color: posColors.ink,
    fontSize: 14,
    marginTop: 8,
  },
  disabledButton: { opacity: 0.55 },
});
