import { useSyncExternalStore } from "react";
import { useTranslation } from "react-i18next";
import {
  ActivityIndicator,
  ScrollView,
  StyleSheet,
  Text,
  useWindowDimensions,
  View,
} from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";

import type {
  CatalogRefreshStep,
  CatalogSummary,
} from "@hb/pos-domain/features/catalog/catalog-refresh-contract";

import {
  catalogMaintenanceText,
  resolveCatalogMaintenanceLocale,
  type CatalogMaintenanceCopyKey,
  type CatalogMaintenanceLocale,
} from "@hb/pos-domain/features/catalog/maintenance/catalog-maintenance-copy";
import type {
  CatalogMaintenancePresenter,
  CatalogMaintenanceState,
  CatalogRefreshProgress,
  CatalogRefreshWarningCode,
} from "./catalog-maintenance-presenter";

import { PosPressable } from "@/ui/controls/pos-pressable";
import { posColors } from "@/ui/theme";

export const CATALOG_MAINTENANCE_MIN_TOUCH_TARGET = 44;
const CATALOG_MAINTENANCE_COMPACT_WIDTH = 900;

type CatalogMaintenanceScreenProps = Readonly<{
  locale?: CatalogMaintenanceLocale;
  presenter: CatalogMaintenancePresenter;
  onBack?(): void;
}>;

/** 横屏保持并排工作台；窄屏以可滚动的顺序堆叠，避免进度步骤被截断。 */
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
  const { width } = useWindowDimensions();
  const { i18n } = useTranslation();
  const locale =
    localeOverride ??
    resolveCatalogMaintenanceLocale(i18n.resolvedLanguage ?? i18n.language);
  const t = (
    key: CatalogMaintenanceCopyKey,
    values?: Readonly<Record<string, string | number>>,
  ) => catalogMaintenanceText(locale, key, values);
  const compact = width < CATALOG_MAINTENANCE_COMPACT_WIDTH;
  const refreshInProgress = state.refresh.kind === "running";
  const catalogLoading = state.catalog.kind === "loading";

  return (
    <SafeAreaView style={styles.safeArea} testID="catalog-maintenance-screen">
      <ScrollView
        contentContainerStyle={styles.scrollContent}
        style={styles.scrollView}
      >
        <View style={[styles.page, compact && styles.pageCompact]}>
          <View style={[styles.header, compact && styles.headerCompact]}>
            <View style={styles.titleGroup}>
              <Text style={styles.eyebrow}>{t("header.eyebrow")}</Text>
              <Text style={styles.title}>{t("header.title")}</Text>
              <Text style={styles.subtitle}>{t("header.subtitle")}</Text>
            </View>
            {onBack ? (
              <CatalogMaintenanceButton
                label={t("action.back")}
                onPress={onBack}
                sound="navigate"
                testID="catalog-maintenance-back"
                tone="secondary"
              />
            ) : null}
          </View>

          <View style={[styles.workspace, compact && styles.workspaceCompact]}>
            <View style={[styles.statusPanel, compact && styles.panelCompact]}>
              <Text style={styles.panelLabel}>{t("status.panelLabel")}</Text>
              <StatusContent locale={locale} state={state} compact={compact} />
            </View>
            <View style={[styles.actionPanel, compact && styles.panelCompact]}>
              <Text style={styles.actionTitle}>{t("action.title")}</Text>
              <Text style={styles.actionCopy}>{t("action.copy")}</Text>
              <Text style={styles.backgroundHint}>{t("action.background")}</Text>
              <CatalogMaintenanceButton
                disabled={catalogLoading || refreshInProgress}
                label={t(
                  refreshInProgress ? "action.refreshing" : "action.refresh",
                )}
                onPress={() => void presenter.refresh()}
                testID="catalog-maintenance-refresh"
              />
              <Text style={styles.safetyFootnote}>{t("action.footnote")}</Text>
            </View>
          </View>
        </View>
      </ScrollView>
    </SafeAreaView>
  );
}

function StatusContent({
  compact,
  locale,
  state,
}: Readonly<{
  compact: boolean;
  locale: CatalogMaintenanceLocale;
  state: CatalogMaintenanceState;
}>) {
  const refreshTestId =
    state.refresh.kind === "running"
      ? "catalog-maintenance-downloading"
      : `catalog-maintenance-${state.refresh.kind}`;
  const progress =
    state.refresh.kind === "idle" ? null : state.refresh.progress;

  return (
    <View
      style={[styles.statusBody, compact && styles.statusBodyCompact]}
      testID={refreshTestId}
    >
      <LocalCatalogSummary catalog={state.catalog} locale={locale} />
      <RefreshStatus
        locale={locale}
        progress={progress}
        refresh={state.refresh}
      />
      {state.refresh.kind !== "warning" && state.catalog.summary !== null ? (
        <CatalogContinuityNote locale={locale} />
      ) : null}
    </View>
  );
}

function LocalCatalogSummary({
  catalog,
  locale,
}: Readonly<{
  catalog: CatalogMaintenanceState["catalog"];
  locale: CatalogMaintenanceLocale;
}>) {
  const t = (
    key: CatalogMaintenanceCopyKey,
    values?: Readonly<Record<string, string | number>>,
  ) => catalogMaintenanceText(locale, key, values);
  if (catalog.kind === "loading") {
    return (
      <View style={styles.catalogLoading} testID="catalog-maintenance-catalog-loading">
        <ActivityIndicator color={posColors.blue} size="small" />
        <Text style={styles.catalogLoadingText}>{t("status.catalogLoading")}</Text>
      </View>
    );
  }
  if (catalog.summary === null) {
    return (
      <View
        style={styles.catalogUnavailable}
        testID="catalog-maintenance-catalog-unavailable"
      >
        <Text
          style={[
            styles.catalogUnavailableTitle,
            catalog.kind === "failed" && styles.failedTitle,
          ]}
        >
          {t(
            catalog.kind === "failed"
              ? "status.catalogMetadataError"
              : "status.catalogUnavailable",
          )}
        </Text>
        {catalog.kind === "failed" ? (
          <Text style={styles.errorCode}>
            {t("status.safeError", { errorCode: catalog.errorCode })}
          </Text>
        ) : null}
      </View>
    );
  }

  return <CatalogSummaryGrid locale={locale} summary={catalog.summary} />;
}

function CatalogSummaryGrid({
  locale,
  summary,
}: Readonly<{
  locale: CatalogMaintenanceLocale;
  summary: CatalogSummary;
}>) {
  const t = (key: CatalogMaintenanceCopyKey) =>
    catalogMaintenanceText(locale, key);
  return (
    <View style={styles.summaryGrid} testID="catalog-maintenance-summary">
      <Metric label={t("metric.version")} value={summary.catalogVersion} />
      <Metric label={t("metric.items")} value={String(summary.itemCount)} />
      <Metric
        label={t("metric.activated")}
        value={formatCatalogTimestamp(summary.activatedAt)}
      />
      <Metric
        label={t("metric.snapshot")}
        secondary
        value={summary.snapshotId}
      />
    </View>
  );
}

function RefreshStatus({
  locale,
  progress,
  refresh,
}: Readonly<{
  locale: CatalogMaintenanceLocale;
  progress: CatalogRefreshProgress | null;
  refresh: CatalogMaintenanceState["refresh"];
}>) {
  const t = (
    key: CatalogMaintenanceCopyKey,
    values?: Readonly<Record<string, string | number>>,
  ) => catalogMaintenanceText(locale, key, values);

  if (refresh.kind === "idle") {
    return (
      <View style={styles.refreshStatus}>
        <Text style={styles.statusTitle}>{t("status.idle")}</Text>
        <Text style={styles.statusHint}>{t("status.idleHint")}</Text>
      </View>
    );
  }

  const title =
    refresh.kind === "success"
      ? t("status.success")
      : refresh.kind === "warning"
        ? t("status.warning")
      : refresh.kind === "failed"
        ? t("status.failed")
        : t("status.downloading");
  return (
    <View
      accessibilityRole={
        refresh.kind === "failed" || refresh.kind === "warning"
          ? "alert"
          : undefined
      }
      style={styles.refreshStatus}
    >
      <Text
        style={[
          styles.statusTitle,
          refresh.kind === "success" && styles.successTitle,
          refresh.kind === "warning" && styles.warningTitle,
          refresh.kind === "failed" && styles.failedTitle,
        ]}
      >
        {title}
      </Text>
      {progress ? <RefreshProgress locale={locale} progress={progress} /> : null}
      {refresh.kind === "failed" ? (
        <Text style={styles.errorCode}>
          {t("status.safeError", { errorCode: refresh.errorCode })}
        </Text>
      ) : null}
      {refresh.kind === "warning" ? (
        <Text style={styles.warningCopy}>
          {t(warningCopyKey(refresh.warningCode))}
        </Text>
      ) : null}
    </View>
  );
}

function RefreshProgress({
  locale,
  progress,
}: Readonly<{
  locale: CatalogMaintenanceLocale;
  progress: CatalogRefreshProgress;
}>) {
  const t = (
    key: CatalogMaintenanceCopyKey,
    values?: Readonly<Record<string, string | number>>,
  ) => catalogMaintenanceText(locale, key, values);
  const overallPercent = formatPercent(progress.overallPercent);
  const currentStep = t(stepCopyKey(progress.currentStep));
  const preparing = progress.currentStep === "prepare" && progress.steps[0]?.percent === 0;

  if (preparing) {
    return (
      <View style={styles.progressSection} testID="catalog-maintenance-progress">
        <View
          accessibilityLiveRegion="polite"
          style={styles.progressPreparing}
          testID="catalog-maintenance-preparing"
        >
          <ActivityIndicator
            accessibilityLabel={t("progress.preparing")}
            color={posColors.orange}
            size="small"
          />
          <Text style={styles.progressPreparingText}>{t("progress.preparing")}</Text>
        </View>
        <Text style={styles.progressElapsed} testID="catalog-maintenance-elapsed">
          {t("progress.elapsed", { elapsed: formatElapsedMilliseconds(progress.elapsedMilliseconds) })}
        </Text>
      </View>
    );
  }

  return (
    <View style={styles.progressSection} testID="catalog-maintenance-progress">
      <Text accessibilityLiveRegion="polite" style={styles.progressCurrentStep}>
        {t("progress.currentStep", { step: currentStep })}
      </Text>
      <View
        accessibilityLabel={t("progress.accessibility", {
          percent: overallPercent,
        })}
        accessibilityRole="progressbar"
        accessibilityValue={{ max: 100, min: 0, now: progress.overallPercent }}
        style={styles.progressTrack}
        testID="catalog-maintenance-overall-progress"
      >
        <View
          style={[
            styles.progressFill,
            { width: `${progress.overallPercent}%` },
          ]}
        />
      </View>
      <Text style={styles.progressTotal}>
        {t("progress.total", { percent: overallPercent })}
      </Text>
      <Text style={styles.progressElapsed} testID="catalog-maintenance-elapsed">
        {t("progress.elapsed", { elapsed: formatElapsedMilliseconds(progress.elapsedMilliseconds) })}
      </Text>
      <View style={styles.stepList}>
        {progress.steps.map((step) => {
          const percent = formatPercent(step.percent);
          const title = t(stepCopyKey(step.step));
          const status = stepStatus(progress, step.step, step.percent);
          const detail = formatStepDetail(step, (completed, total) =>
            t("progress.pages", { completed, total }),
          );
          return (
            <View
              accessibilityLabel={t("progress.stepAccessibility", {
                percent,
                step: title,
              })}
              key={step.step}
              style={styles.stepRow}
              testID={`catalog-maintenance-step-${step.step}`}
            >
              <View style={[styles.stepMarker, styles[`stepMarker${status}`]]} />
              <View style={styles.stepTextGroup}>
                <Text style={[styles.stepLabel, styles[`stepLabel${status}`]]}>
                  {title}
                </Text>
                {detail ? <Text style={styles.stepDetail}>{detail}</Text> : null}
              </View>
              <Text style={[styles.stepPercent, styles[`stepLabel${status}`]]}>
                {percent}%
              </Text>
            </View>
          );
        })}
      </View>
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

function Metric({
  label,
  secondary = false,
  value,
}: Readonly<{ label: string; secondary?: boolean; value: string }>) {
  return (
    <View style={[styles.metric, secondary && styles.secondaryMetric]}>
      <Text style={styles.metricLabel}>{label}</Text>
      <Text numberOfLines={1} style={[styles.metricValue, secondary && styles.secondaryMetricValue]}>
        {value}
      </Text>
    </View>
  );
}

function CatalogMaintenanceButton({
  disabled = false,
  label,
  onPress,
  sound = "tap",
  testID,
  tone = "primary",
}: Readonly<{
  disabled?: boolean;
  label: string;
  onPress(): void;
  sound?: "tap" | "navigate";
  testID: string;
  tone?: "primary" | "secondary";
}>) {
  return (
    <PosPressable
      accessibilityRole="button"
      accessibilityState={{ disabled }}
      disabled={disabled}
      onPress={onPress}
      sound={sound}
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
    </PosPressable>
  );
}

function stepCopyKey(step: CatalogRefreshStep): CatalogMaintenanceCopyKey {
  return `step.${step}` as CatalogMaintenanceCopyKey;
}

function warningCopyKey(
  warningCode: CatalogRefreshWarningCode,
): CatalogMaintenanceCopyKey {
  switch (warningCode) {
    case "catalog-runtime-reload-failed":
      return "warning.runtimeReload";
    case "catalog-activation-verification-failed":
      return "warning.activationVerification";
  }
}

function stepStatus(
  progress: CatalogRefreshProgress,
  step: CatalogRefreshStep,
  percent: number,
): "Pending" | "Current" | "Complete" {
  if (percent === 100) return "Complete";
  return progress.currentStep === step ? "Current" : "Pending";
}

function formatPercent(percent: number): string {
  const rounded = Math.round(percent * 100) / 100;
  return Number.isInteger(rounded) ? String(rounded) : String(rounded);
}

function formatElapsedMilliseconds(elapsedMilliseconds: number): string {
  const totalSeconds = Math.max(0, Math.floor(elapsedMilliseconds / 1_000));
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return `${String(minutes).padStart(2, "0")}:${String(seconds).padStart(2, "0")}`;
}

function formatStepDetail(
  step: CatalogRefreshProgress["steps"][number],
  formatPages: (completed: number, total: number) => string,
): string | null {
  const itemDetail =
    step.completedItemCount !== undefined && step.totalItemCount !== undefined
      ? `${step.completedItemCount} / ${step.totalItemCount}`
      : null;
  const pageDetail =
    step.completedPageCount !== undefined && step.totalPageCount !== undefined
      ? formatPages(step.completedPageCount, step.totalPageCount)
      : null;
  return [itemDetail, pageDetail].filter((value): value is string => value !== null).join(" · ") || null;
}

/** 目录仓储保存 canonical ISO；页面统一显示到分钟，避免因本地时区重解释启用事实。 */
function formatCatalogTimestamp(value: string): string {
  return value.replace(/:\d{2}(?:\.\d+)?Z$/, "Z");
}

const styles = StyleSheet.create({
  safeArea: { flex: 1, backgroundColor: posColors.canvas },
  scrollView: { flex: 1 },
  scrollContent: { flexGrow: 1 },
  page: { flex: 1, paddingHorizontal: 40, paddingVertical: 28 },
  pageCompact: { paddingHorizontal: 20, paddingVertical: 20 },
  header: {
    alignItems: "flex-start",
    flexDirection: "row",
    gap: 24,
    justifyContent: "space-between",
    marginBottom: 28,
  },
  headerCompact: { gap: 16, marginBottom: 20 },
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
  workspaceCompact: { flexDirection: "column", gap: 16, minHeight: 0 },
  statusPanel: {
    backgroundColor: posColors.surface,
    borderColor: posColors.border,
    borderRadius: 12,
    borderWidth: 1,
    flex: 1.25,
    padding: 28,
  },
  panelCompact: { flexGrow: 0, padding: 20 },
  panelLabel: {
    color: posColors.mutedInk,
    fontSize: 13,
    fontWeight: "800",
    letterSpacing: 0.9,
  },
  statusBody: { flex: 1, justifyContent: "center", paddingVertical: 24 },
  statusBodyCompact: { justifyContent: "flex-start", paddingVertical: 20 },
  catalogLoading: { alignItems: "center", flexDirection: "row", gap: 10 },
  catalogLoadingText: { color: posColors.mutedInk, fontSize: 16, fontWeight: "700" },
  catalogUnavailable: { paddingTop: 16 },
  catalogUnavailableTitle: { color: posColors.ink, fontSize: 20, fontWeight: "800" },
  summaryGrid: { flexDirection: "row", flexWrap: "wrap", gap: 12, marginTop: 16 },
  metric: {
    backgroundColor: posColors.greenSoft,
    borderRadius: 8,
    flexGrow: 1,
    minWidth: 168,
    padding: 14,
  },
  secondaryMetric: { backgroundColor: posColors.surface, borderColor: posColors.border, borderWidth: 1 },
  metricLabel: { color: posColors.mutedInk, fontSize: 13, fontWeight: "700" },
  metricValue: { color: posColors.ink, fontSize: 18, fontWeight: "800", marginTop: 4 },
  secondaryMetricValue: { fontSize: 15 },
  refreshStatus: { marginTop: 24 },
  statusTitle: { color: posColors.ink, fontSize: 23, fontWeight: "800", lineHeight: 31 },
  successTitle: { color: posColors.green },
  warningTitle: { color: posColors.orange },
  failedTitle: { color: posColors.red },
  statusHint: { color: posColors.mutedInk, fontSize: 17, lineHeight: 25, marginTop: 8 },
  errorCode: { color: posColors.red, fontFamily: "Courier", fontSize: 15, fontWeight: "700", marginTop: 10 },
  warningCopy: { color: posColors.ink, fontSize: 16, lineHeight: 23, marginTop: 10 },
  progressSection: { marginTop: 14 },
  progressCurrentStep: { color: posColors.mutedInk, fontSize: 15, fontWeight: "700" },
  progressPreparing: { alignItems: "center", flexDirection: "row", gap: 8, marginTop: 8 },
  progressPreparingText: { color: posColors.mutedInk, fontSize: 14, fontWeight: "700" },
  progressTrack: { backgroundColor: posColors.border, borderRadius: 99, height: 10, marginTop: 10, overflow: "hidden" },
  progressFill: { backgroundColor: posColors.orange, borderRadius: 99, height: "100%" },
  progressTotal: { color: posColors.ink, fontSize: 15, fontWeight: "800", marginTop: 8 },
  progressElapsed: { color: posColors.mutedInk, fontSize: 14, fontVariant: ["tabular-nums"], fontWeight: "700", marginTop: 4 },
  stepList: { gap: 8, marginTop: 14 },
  stepRow: { alignItems: "center", flexDirection: "row", gap: 9, minHeight: 24 },
  stepMarker: { borderRadius: 5, height: 10, width: 10 },
  stepMarkerPending: { backgroundColor: posColors.border },
  stepMarkerCurrent: { backgroundColor: posColors.orange },
  stepMarkerComplete: { backgroundColor: posColors.green },
  stepTextGroup: { flex: 1 },
  stepLabel: { fontSize: 16, lineHeight: 22 },
  stepDetail: { color: posColors.mutedInk, fontSize: 13, fontVariant: ["tabular-nums"], marginTop: 1 },
  stepPercent: { fontSize: 15, fontVariant: ["tabular-nums"], fontWeight: "800" },
  stepLabelPending: { color: posColors.mutedInk },
  stepLabelCurrent: { color: posColors.ink, fontWeight: "800" },
  stepLabelComplete: { color: posColors.green, fontWeight: "700" },
  continuityNote: { color: posColors.green, fontSize: 17, fontWeight: "700", lineHeight: 25, marginTop: 22 },
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
  actionCopy: { color: posColors.mutedInk, fontSize: 17, lineHeight: 25, marginTop: 12 },
  backgroundHint: {
    color: posColors.blue,
    fontSize: 14,
    fontWeight: "700",
    lineHeight: 21,
    marginTop: 8,
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
  buttonLabel: { color: "#FFFFFF", fontSize: 16, fontWeight: "800", textAlign: "center" },
  secondaryButton: { backgroundColor: posColors.surface, borderColor: posColors.border, borderWidth: 1, marginTop: 0 },
  secondaryButtonLabel: { color: posColors.ink },
  disabledButton: { backgroundColor: "#C9B6AF" },
  pressedButton: { opacity: 0.82 },
  safetyFootnote: { color: posColors.mutedInk, fontSize: 14, lineHeight: 20, marginTop: 16 },
});
