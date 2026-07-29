import { useSyncExternalStore } from "react";
import { useTranslation } from "react-i18next";
import {
  ActivityIndicator,
  Pressable,
  StyleSheet,
  Text,
  View,
} from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";

import {
  catalogMaintenanceText,
  resolveCatalogMaintenanceLocale,
  type CatalogMaintenanceCopyKey,
  type CatalogMaintenanceLocale,
} from "./catalog-maintenance-copy";
import type {
  CatalogMaintenancePresenter,
  CatalogMaintenanceState,
} from "./catalog-maintenance-presenter";

import { posColors } from "@/ui/theme";

export const CATALOG_MAINTENANCE_MIN_TOUCH_TARGET = 44;

type CatalogMaintenanceScreenProps = Readonly<{
  locale?: CatalogMaintenanceLocale;
  presenter: CatalogMaintenancePresenter;
  onBack?(): void;
}>;

/** 为横屏 iPad 维护工作流设计；既有目录在每一个状态下都被明确标示为可继续使用。 */
export function CatalogMaintenanceScreen({
  locale: localeOverride,
  presenter,
  onBack,
}: CatalogMaintenanceScreenProps) {
  const state = useSyncExternalStore(
    presenter.subscribe,
    presenter.getState,
    presenter.getState,
  );
  const { i18n } = useTranslation();
  const locale =
    localeOverride ??
    resolveCatalogMaintenanceLocale(i18n.resolvedLanguage ?? i18n.language);
  const t = (
    key: CatalogMaintenanceCopyKey,
    values?: Readonly<Record<string, string | number>>,
  ) => catalogMaintenanceText(locale, key, values);
  const isDownloading = state.kind === "downloading";

  return (
    <SafeAreaView style={styles.safeArea} testID="catalog-maintenance-screen">
      <View style={styles.page}>
        <View style={styles.header}>
          <View style={styles.titleGroup}>
            <Text style={styles.eyebrow}>{t("header.eyebrow")}</Text>
            <Text style={styles.title}>{t("header.title")}</Text>
            <Text style={styles.subtitle}>{t("header.subtitle")}</Text>
          </View>
          {onBack ? (
            <CatalogMaintenanceButton
              label={t("action.back")}
              onPress={onBack}
              testID="catalog-maintenance-back"
              tone="secondary"
            />
          ) : null}
        </View>

        <View style={styles.workspace}>
          <View style={styles.statusPanel}>
            <Text style={styles.panelLabel}>{t("status.panelLabel")}</Text>
            <StatusContent locale={locale} state={state} />
          </View>
          <View style={styles.actionPanel}>
            <Text style={styles.actionTitle}>{t("action.title")}</Text>
            <Text style={styles.actionCopy}>{t("action.copy")}</Text>
            <CatalogMaintenanceButton
              disabled={isDownloading}
              label={t(isDownloading ? "action.refreshing" : "action.refresh")}
              onPress={() => void presenter.refresh()}
              testID="catalog-maintenance-refresh"
            />
            <Text style={styles.safetyFootnote}>{t("action.footnote")}</Text>
          </View>
        </View>
      </View>
    </SafeAreaView>
  );
}

function StatusContent({
  locale,
  state,
}: Readonly<{
  locale: CatalogMaintenanceLocale;
  state: CatalogMaintenanceState;
}>) {
  if (state.kind === "downloading") {
    return (
      <View
        accessibilityLiveRegion="polite"
        style={styles.statusBody}
        testID="catalog-maintenance-downloading"
      >
        <ActivityIndicator color={posColors.orange} size="large" />
        <Text style={styles.statusTitle}>
          {catalogMaintenanceText(locale, "status.downloading")}
        </Text>
        <CatalogContinuityNote locale={locale} />
      </View>
    );
  }

  if (state.kind === "success") {
    return (
      <View
        accessibilityLiveRegion="polite"
        style={styles.statusBody}
        testID="catalog-maintenance-success"
      >
        <Text style={[styles.statusTitle, styles.successTitle]}>
          {catalogMaintenanceText(locale, "status.success")}
        </Text>
        <View style={styles.resultGrid}>
          <Metric
            label={catalogMaintenanceText(locale, "metric.snapshot")}
            value={state.snapshotId}
          />
          <Metric
            label={catalogMaintenanceText(locale, "metric.items")}
            value={String(state.itemCount)}
          />
        </View>
        <CatalogContinuityNote locale={locale} />
      </View>
    );
  }

  if (state.kind === "failed") {
    return (
      <View
        accessibilityRole="alert"
        style={styles.statusBody}
        testID="catalog-maintenance-failed"
      >
        <Text style={[styles.statusTitle, styles.failedTitle]}>
          {catalogMaintenanceText(locale, "status.failed")}
        </Text>
        <Text style={styles.errorCode}>
          {catalogMaintenanceText(locale, "status.safeError", {
            errorCode: state.errorCode,
          })}
        </Text>
        <CatalogContinuityNote locale={locale} />
      </View>
    );
  }

  return (
    <View style={styles.statusBody} testID="catalog-maintenance-idle">
      <Text style={styles.statusTitle}>
        {catalogMaintenanceText(locale, "status.idle")}
      </Text>
      <Text style={styles.statusHint}>
        {catalogMaintenanceText(locale, "status.idleHint")}
      </Text>
      <CatalogContinuityNote locale={locale} />
    </View>
  );
}

function CatalogContinuityNote({
  locale,
}: Readonly<{ locale: CatalogMaintenanceLocale }>) {
  return (
    <Text style={styles.continuityNote}>
      {catalogMaintenanceText(locale, "continuity")}
    </Text>
  );
}

function Metric({ label, value }: Readonly<{ label: string; value: string }>) {
  return (
    <View style={styles.metric}>
      <Text style={styles.metricLabel}>{label}</Text>
      <Text numberOfLines={1} style={styles.metricValue}>
        {value}
      </Text>
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
      <Text
        style={[
          styles.buttonLabel,
          tone === "secondary" && styles.secondaryButtonLabel,
        ]}
      >
        {label}
      </Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  safeArea: { flex: 1, backgroundColor: posColors.canvas },
  page: { flex: 1, paddingHorizontal: 40, paddingVertical: 28 },
  header: {
    alignItems: "flex-start",
    flexDirection: "row",
    gap: 24,
    justifyContent: "space-between",
    marginBottom: 28,
  },
  titleGroup: { flex: 1, maxWidth: 880 },
  eyebrow: {
    color: posColors.blue,
    fontSize: 13,
    fontWeight: "800",
    letterSpacing: 1.1,
  },
  title: {
    color: posColors.ink,
    fontSize: 32,
    fontWeight: "800",
    marginTop: 8,
  },
  subtitle: {
    color: posColors.mutedInk,
    fontSize: 17,
    lineHeight: 25,
    marginTop: 8,
  },
  workspace: { flex: 1, flexDirection: "row", gap: 24, minHeight: 330 },
  statusPanel: {
    backgroundColor: posColors.surface,
    borderColor: posColors.border,
    borderRadius: 12,
    borderWidth: 1,
    flex: 1.25,
    padding: 28,
  },
  panelLabel: {
    color: posColors.mutedInk,
    fontSize: 13,
    fontWeight: "800",
    letterSpacing: 0.9,
  },
  statusBody: { flex: 1, justifyContent: "center", paddingVertical: 24 },
  statusTitle: {
    color: posColors.ink,
    fontSize: 25,
    fontWeight: "800",
    lineHeight: 34,
    marginTop: 14,
  },
  successTitle: { color: posColors.green },
  failedTitle: { color: posColors.red },
  statusHint: {
    color: posColors.mutedInk,
    fontSize: 17,
    lineHeight: 25,
    marginTop: 12,
  },
  continuityNote: {
    color: posColors.green,
    fontSize: 17,
    fontWeight: "700",
    lineHeight: 25,
    marginTop: 18,
  },
  errorCode: {
    color: posColors.red,
    fontFamily: "Courier",
    fontSize: 16,
    fontWeight: "700",
    marginTop: 12,
  },
  resultGrid: { flexDirection: "row", gap: 14, marginTop: 18 },
  metric: {
    backgroundColor: posColors.greenSoft,
    borderRadius: 8,
    flex: 1,
    padding: 14,
  },
  metricLabel: { color: posColors.mutedInk, fontSize: 13, fontWeight: "700" },
  metricValue: {
    color: posColors.ink,
    fontSize: 19,
    fontWeight: "800",
    marginTop: 4,
  },
  actionPanel: {
    backgroundColor: posColors.blueSoft,
    borderColor: "#C7D9E8",
    borderRadius: 12,
    borderWidth: 1,
    flex: 0.9,
    justifyContent: "center",
    padding: 28,
  },
  actionTitle: { color: posColors.ink, fontSize: 23, fontWeight: "800" },
  actionCopy: {
    color: posColors.mutedInk,
    fontSize: 17,
    lineHeight: 25,
    marginTop: 12,
  },
  button: {
    alignItems: "center",
    backgroundColor: posColors.orange,
    borderRadius: 8,
    justifyContent: "center",
    marginTop: 24,
    minHeight: CATALOG_MAINTENANCE_MIN_TOUCH_TARGET,
    paddingHorizontal: 20,
    paddingVertical: 10,
  },
  buttonLabel: {
    color: "#FFFFFF",
    fontSize: 16,
    fontWeight: "800",
    textAlign: "center",
  },
  secondaryButton: {
    backgroundColor: posColors.surface,
    borderColor: posColors.border,
    borderWidth: 1,
    marginTop: 0,
  },
  secondaryButtonLabel: { color: posColors.ink },
  disabledButton: { backgroundColor: "#C9B6AF" },
  pressedButton: { opacity: 0.82 },
  safetyFootnote: {
    color: posColors.mutedInk,
    fontSize: 14,
    lineHeight: 20,
    marginTop: 16,
  },
});
