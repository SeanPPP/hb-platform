import { useSyncExternalStore } from "react";
import {
  ActivityIndicator,
  Pressable,
  StyleSheet,
  Text,
  View,
} from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";

import type {
  CatalogMaintenancePresenter,
  CatalogMaintenanceState,
} from "./catalog-maintenance-presenter";

import { posColors } from "@/ui/theme";

export const CATALOG_MAINTENANCE_MIN_TOUCH_TARGET = 44;

type CatalogMaintenanceScreenProps = Readonly<{
  presenter: CatalogMaintenancePresenter;
  onBack?(): void;
}>;

/** 为横屏 iPad 维护工作流设计；既有目录在每一个状态下都被明确标示为可继续使用。 */
export function CatalogMaintenanceScreen({
  presenter,
  onBack,
}: CatalogMaintenanceScreenProps) {
  const state = useSyncExternalStore(
    presenter.subscribe,
    presenter.getState,
    presenter.getState,
  );
  const isDownloading = state.kind === "downloading";

  return (
    <SafeAreaView style={styles.safeArea} testID="catalog-maintenance-screen">
      <View style={styles.page}>
        <View style={styles.header}>
          <View style={styles.titleGroup}>
            <Text style={styles.eyebrow}>目录维护 / CATALOG MAINTENANCE</Text>
            <Text style={styles.title}>手动刷新目录 / Refresh catalog</Text>
            <Text style={styles.subtitle}>
              下载并验证替换快照；仅在完整成功后才激活。/ Download and verify a replacement snapshot; it activates only after completion.
            </Text>
          </View>
          {onBack ? (
            <CatalogMaintenanceButton
              label="返回 / Back"
              onPress={onBack}
              testID="catalog-maintenance-back"
              tone="secondary"
            />
          ) : null}
        </View>

        <View style={styles.workspace}>
          <View style={styles.statusPanel}>
            <Text style={styles.panelLabel}>当前状态 / CURRENT STATUS</Text>
            <StatusContent state={state} />
          </View>
          <View style={styles.actionPanel}>
            <Text style={styles.actionTitle}>安全切换 / Safe activation</Text>
            <Text style={styles.actionCopy}>
              当前旧目录在下载、校验或失败时都可继续使用。/ Your current catalog remains available during download, validation, or failure.
            </Text>
            <CatalogMaintenanceButton
              disabled={isDownloading}
              label={isDownloading ? "正在下载与校验… / Downloading and validating…" : "立即刷新目录 / Refresh now"}
              onPress={() => void presenter.refresh()}
              testID="catalog-maintenance-refresh"
            />
            <Text style={styles.safetyFootnote}>
              此页面不提供目录重置或 API 地址设置。/ No catalog reset or API address settings are available here.
            </Text>
          </View>
        </View>
      </View>
    </SafeAreaView>
  );
}

function StatusContent({ state }: Readonly<{ state: CatalogMaintenanceState }>) {
  if (state.kind === "downloading") {
    return (
      <View accessibilityLiveRegion="polite" style={styles.statusBody} testID="catalog-maintenance-downloading">
        <ActivityIndicator color={posColors.orange} size="large" />
        <Text style={styles.statusTitle}>正在下载与校验 / Downloading and validating</Text>
        <CatalogContinuityNote />
      </View>
    );
  }

  if (state.kind === "success") {
    return (
      <View accessibilityLiveRegion="polite" style={styles.statusBody} testID="catalog-maintenance-success">
        <Text style={[styles.statusTitle, styles.successTitle]}>目录已更新 / Catalog updated</Text>
        <View style={styles.resultGrid}>
          <Metric label="快照 / Snapshot" value={state.snapshotId} />
          <Metric label="商品数 / Items" value={String(state.itemCount)} />
        </View>
        <CatalogContinuityNote />
      </View>
    );
  }

  if (state.kind === "failed") {
    return (
      <View accessibilityRole="alert" style={styles.statusBody} testID="catalog-maintenance-failed">
        <Text style={[styles.statusTitle, styles.failedTitle]}>刷新未完成 / Refresh did not complete</Text>
        <Text style={styles.errorCode}>安全错误码 / Safe error: {state.errorCode}</Text>
        <CatalogContinuityNote />
      </View>
    );
  }

  return (
    <View style={styles.statusBody} testID="catalog-maintenance-idle">
      <Text style={styles.statusTitle}>准备就绪 / Ready to refresh</Text>
      <Text style={styles.statusHint}>可开始下载新的已验证目录快照。/ You can download a new verified catalog snapshot.</Text>
      <CatalogContinuityNote />
    </View>
  );
}

function CatalogContinuityNote() {
  return (
    <Text style={styles.continuityNote}>
      旧目录仍可继续使用。/ The existing catalog remains available.
    </Text>
  );
}

function Metric({ label, value }: Readonly<{ label: string; value: string }>) {
  return (
    <View style={styles.metric}>
      <Text style={styles.metricLabel}>{label}</Text>
      <Text numberOfLines={1} style={styles.metricValue}>{value}</Text>
    </View>
  );
}

function CatalogMaintenanceButton({
  disabled = false,
  label,
  onPress,
  testID,
  tone = "primary",
}: Readonly<{
  disabled?: boolean;
  label: string;
  onPress(): void;
  testID: string;
  tone?: "primary" | "secondary";
}>) {
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityState={{ disabled }}
      disabled={disabled}
      onPress={onPress}
      style={({ pressed }) => [
        styles.button,
        tone === "secondary" && styles.secondaryButton,
        disabled && styles.disabledButton,
        pressed && !disabled && styles.pressedButton,
      ]}
      testID={testID}
    >
      <Text style={[styles.buttonLabel, tone === "secondary" && styles.secondaryButtonLabel]}>{label}</Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  safeArea: { flex: 1, backgroundColor: posColors.canvas },
  page: { flex: 1, paddingHorizontal: 40, paddingVertical: 28 },
  header: { alignItems: "flex-start", flexDirection: "row", gap: 24, justifyContent: "space-between", marginBottom: 28 },
  titleGroup: { flex: 1, maxWidth: 880 },
  eyebrow: { color: posColors.blue, fontSize: 13, fontWeight: "800", letterSpacing: 1.1 },
  title: { color: posColors.ink, fontSize: 32, fontWeight: "800", marginTop: 8 },
  subtitle: { color: posColors.mutedInk, fontSize: 17, lineHeight: 25, marginTop: 8 },
  workspace: { flex: 1, flexDirection: "row", gap: 24, minHeight: 330 },
  statusPanel: { backgroundColor: posColors.surface, borderColor: posColors.border, borderRadius: 12, borderWidth: 1, flex: 1.25, padding: 28 },
  panelLabel: { color: posColors.mutedInk, fontSize: 13, fontWeight: "800", letterSpacing: 0.9 },
  statusBody: { flex: 1, justifyContent: "center", paddingVertical: 24 },
  statusTitle: { color: posColors.ink, fontSize: 25, fontWeight: "800", lineHeight: 34, marginTop: 14 },
  successTitle: { color: posColors.green },
  failedTitle: { color: posColors.red },
  statusHint: { color: posColors.mutedInk, fontSize: 17, lineHeight: 25, marginTop: 12 },
  continuityNote: { color: posColors.green, fontSize: 17, fontWeight: "700", lineHeight: 25, marginTop: 18 },
  errorCode: { color: posColors.red, fontFamily: "Courier", fontSize: 16, fontWeight: "700", marginTop: 12 },
  resultGrid: { flexDirection: "row", gap: 14, marginTop: 18 },
  metric: { backgroundColor: posColors.greenSoft, borderRadius: 8, flex: 1, padding: 14 },
  metricLabel: { color: posColors.mutedInk, fontSize: 13, fontWeight: "700" },
  metricValue: { color: posColors.ink, fontSize: 19, fontWeight: "800", marginTop: 4 },
  actionPanel: { backgroundColor: posColors.blueSoft, borderColor: "#C7D9E8", borderRadius: 12, borderWidth: 1, flex: 0.9, justifyContent: "center", padding: 28 },
  actionTitle: { color: posColors.ink, fontSize: 23, fontWeight: "800" },
  actionCopy: { color: posColors.mutedInk, fontSize: 17, lineHeight: 25, marginTop: 12 },
  button: { alignItems: "center", backgroundColor: posColors.orange, borderRadius: 8, justifyContent: "center", marginTop: 24, minHeight: CATALOG_MAINTENANCE_MIN_TOUCH_TARGET, paddingHorizontal: 20, paddingVertical: 10 },
  buttonLabel: { color: "#FFFFFF", fontSize: 16, fontWeight: "800", textAlign: "center" },
  secondaryButton: { backgroundColor: posColors.surface, borderColor: posColors.border, borderWidth: 1, marginTop: 0 },
  secondaryButtonLabel: { color: posColors.ink },
  disabledButton: { backgroundColor: "#C9B6AF" },
  pressedButton: { opacity: 0.82 },
  safetyFootnote: { color: posColors.mutedInk, fontSize: 14, lineHeight: 20, marginTop: 16 },
});
