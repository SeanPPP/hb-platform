import { useEffect, useSyncExternalStore } from "react";
import { useTranslation } from "react-i18next";
import {
  ActivityIndicator,
  Image,
  ScrollView,
  StyleSheet,
  Text,
  View,
} from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";

import {
  attendanceAuditQrStatusCopyKey,
  attendanceAuditStatusCopyKey,
  attendanceAuditText,
  attendanceAuditUploadStateCopyKey,
  resolveAttendanceAuditLocale,
  type AttendanceAuditCopyKey,
} from "./attendance-audit-copy";
import type {
  AttendanceAuditPresenter,
  AttendanceAuditPresenterState,
} from "./attendance-audit-presenter";
import type {
  OperationAuditRecord,
  OperationAuditSource,
  OperationAuditUploadState,
} from "./operation-audit-presenter";

import { PosPressable } from "@/ui/controls/pos-pressable";
import { PosTextInput } from "@/ui/controls/pos-text-input";
import { posColors } from "@/ui/theme";

export const ATTENDANCE_AUDIT_MIN_TOUCH_TARGET = 44;

export type AttendanceAuditScreenPresenter = Pick<
  AttendanceAuditPresenter,
  | "getState"
  | "loadAudit"
  | "refreshAttendanceQr"
  | "selectAudit"
  | "setAuditQuery"
  | "setAuditSource"
  | "setAuditUploadState"
  | "start"
  | "subscribe"
>;

type AttendanceAuditScreenProps = Readonly<{
  onBack?(): void;
  presenter: AttendanceAuditScreenPresenter;
}>;

type AttendanceAuditTranslate = (
  key: AttendanceAuditCopyKey,
  values?: Readonly<Record<string, string | number>>,
) => string;

/**
 * iPad 横屏高密度工作台：考勤 QR 始终独立显示；审计读取按 Audit.View
 * 单独保护。UI 永不接收二维码 token、签名密钥或审计 properties 原文。
 */
export function AttendanceAuditScreen({
  onBack,
  presenter,
}: AttendanceAuditScreenProps) {
  const { i18n } = useTranslation();
  const locale = resolveAttendanceAuditLocale(
    i18n.resolvedLanguage ?? i18n.language,
  );
  const t: AttendanceAuditTranslate = (key, values) =>
    attendanceAuditText(locale, key, values);
  const state = useSyncExternalStore(
    presenter.subscribe,
    presenter.getState,
    presenter.getState,
  );

  useEffect(() => {
    presenter.start();
  }, [presenter]);

  return (
    <SafeAreaView style={styles.safeArea} testID="attendance-audit-screen">
      <View style={styles.page}>
        <Header onBack={onBack} t={t} />
        <View
          style={styles.workspace}
          testID="attendance-audit-workspace"
        >
          <AttendanceQrPane presenter={presenter} state={state} t={t} />
          <AuditWorkspace presenter={presenter} state={state} t={t} />
        </View>
      </View>
    </SafeAreaView>
  );
}

function Header({
  onBack,
  t,
}: Readonly<{
  onBack: (() => void) | undefined;
  t: AttendanceAuditTranslate;
}>) {
  return (
    <View style={styles.header}>
      <View>
        <Text style={styles.eyebrow}>{t("header.eyebrow")}</Text>
        <Text style={styles.title}>{t("header.title")}</Text>
        <Text style={styles.subtitle}>{t("header.subtitle")}</Text>
      </View>
      {onBack ? (
        <ActionButton
          label={t("action.back")}
          onPress={onBack}
          sound="navigate"
          testID="attendance-audit-back"
          tone="quiet"
        />
      ) : null}
    </View>
  );
}

function AttendanceQrPane({
  presenter,
  state,
  t,
}: Readonly<{
  presenter: AttendanceAuditScreenPresenter;
  state: AttendanceAuditPresenterState;
  t: AttendanceAuditTranslate;
}>) {
  const qr = state.qr;
  const locked =
    qr.kind === "clock-invalid" || qr.requiresOnlineResync;
  return (
    <View style={[styles.panel, styles.qrPanel]}>
      <View style={styles.panelHeading}>
        <View>
          <Text style={styles.panelKicker}>{t("qr.kicker")}</Text>
          <Text style={styles.panelTitle}>{t("qr.title")}</Text>
        </View>
        <StatusPill
          label={
            qr.online ? t("qr.online") : t("qr.offline")
          }
          tone={qr.online ? "success" : "warning"}
        />
      </View>

      <View style={styles.qrStage}>
        {locked ? (
          <View
            accessibilityLiveRegion="assertive"
            style={styles.clockLock}
            testID="attendance-clock-lock"
          >
            <Text style={styles.lockIcon}>!</Text>
            <Text style={styles.lockTitle}>{t("qr.clockRollback.title")}</Text>
            <Text style={styles.lockBody}>{t("qr.clockRollback.body")}</Text>
          </View>
        ) : qr.qrImageUri ? (
          <Image
            accessibilityLabel={t("qr.imageLabel")}
            resizeMode="contain"
            source={{ uri: qr.qrImageUri }}
            style={styles.qrImage}
            testID="attendance-qr-image"
          />
        ) : (
          <View style={styles.qrPlaceholder}>
            {qr.kind === "initializing" ? (
              <ActivityIndicator color={posColors.orange} size="large" />
            ) : null}
            <Text style={styles.placeholderTitle}>
              {t(qr.kind === "initializing" ? "qr.preparing" : "qr.unavailable")}
            </Text>
            <Text style={styles.placeholderBody}>{t("qr.placeholder")}</Text>
          </View>
        )}
      </View>

      <View style={styles.countdownCard}>
        <Text style={styles.countdownValue}>
          {t("qr.remaining", { seconds: qr.secondsRemaining })}
        </Text>
        <Text style={styles.countdownLabel}>
          {t("qr.remainingLabel")}
        </Text>
      </View>
      <View style={styles.contextCard}>
        <ContextRow label={t("context.store")} value={qr.storeText || "—"} />
        <ContextRow
          label={t("context.device")}
          value={qr.deviceText || "—"}
        />
      </View>
      <QrStatusMessage statusCode={qr.statusCode} t={t} />
      <ActionButton
        label={t("action.refreshQr")}
        onPress={() => void presenter.refreshAttendanceQr()}
        testID="attendance-qr-refresh"
      />
    </View>
  );
}

function AuditWorkspace({
  presenter,
  state,
  t,
}: Readonly<{
  presenter: AttendanceAuditScreenPresenter;
  state: AttendanceAuditPresenterState;
  t: AttendanceAuditTranslate;
}>) {
  const audit = state.audit;
  if (!audit.access.canView) {
    return (
      <View
        style={[styles.panel, styles.auditPanel, styles.centeredPanel]}
        testID="audit-permission-required"
      >
        <Text style={styles.permissionCode}>AUDIT.VIEW</Text>
        <Text style={styles.permissionTitle}>{t("audit.permission.title")}</Text>
        <Text style={styles.permissionBody}>{t("audit.permission.body")}</Text>
      </View>
    );
  }

  return (
    <View style={[styles.panel, styles.auditPanel]}>
      <View style={styles.panelHeading}>
        <View>
          <Text style={styles.panelKicker}>{t("audit.kicker")}</Text>
          <Text style={styles.panelTitle}>{t("audit.title")}</Text>
        </View>
        <Text style={styles.resultCount}>
          {t("audit.resultCount", { count: audit.rows.length })}
        </Text>
      </View>

      <AuditFilters presenter={presenter} state={state} t={t} />
      <AuditStatus state={state} t={t} />

      <View style={styles.auditMasterDetail}>
        <AuditList presenter={presenter} state={state} t={t} />
        <AuditDetails state={state} t={t} />
      </View>
    </View>
  );
}

function AuditFilters({
  presenter,
  state,
  t,
}: Readonly<{
  presenter: AttendanceAuditScreenPresenter;
  state: AttendanceAuditPresenterState;
  t: AttendanceAuditTranslate;
}>) {
  const audit = state.audit;
  const sources: readonly {
    label: string;
    source: OperationAuditSource;
  }[] = [
    { label: t("audit.source.local"), source: "local" },
    { label: t("audit.source.remote"), source: "remote" },
  ];
  const uploadStates: readonly {
    label: string;
    state: OperationAuditUploadState | null;
  }[] = [
    { label: t("audit.upload.all"), state: null },
    { label: t("audit.upload.pending"), state: "pending" },
    { label: t("audit.upload.uploaded"), state: "uploaded" },
    { label: t("audit.upload.rejected"), state: "rejected" },
  ];
  return (
    <View style={styles.filters}>
      <View style={styles.filterLine}>
        {sources.map(({ label, source }) => (
          <ActionButton
            disabled={source === "remote" && !audit.online}
            key={source}
            label={label}
            onPress={() => presenter.setAuditSource(source)}
            selected={audit.source === source}
            testID={`audit-source-${source}`}
            tone="secondary"
          />
        ))}
        {uploadStates.map(({ label, state: uploadState }) => (
          <ActionButton
            compact
            key={uploadState ?? "all"}
            label={label}
            onPress={() =>
              presenter.setAuditUploadState(uploadState)
            }
            selected={audit.uploadState === uploadState}
            testID={`audit-upload-${uploadState ?? "all"}`}
            tone="quiet"
          />
        ))}
      </View>
      <View style={styles.searchRow}>
        <PosTextInput
          accessibilityLabel={t("audit.searchLabel")}
          autoCapitalize="none"
          autoCorrect={false}
          onChangeText={(value) => presenter.setAuditQuery(value)}
          onSubmitEditing={() => void presenter.loadAudit()}
          placeholder={t("audit.searchPlaceholder")}
          placeholderTextColor={posColors.mutedInk}
          style={styles.searchInput}
          testID="audit-search-input"
          value={audit.query}
        />
        <ActionButton
          disabled={
            audit.kind === "loading" ||
            (audit.source === "remote" && !audit.online)
          }
          label={t("audit.action.search")}
          onPress={() => void presenter.loadAudit()}
          testID="audit-search-submit"
        />
      </View>
    </View>
  );
}

function AuditStatus({
  state,
  t,
}: Readonly<{
  state: AttendanceAuditPresenterState;
  t: AttendanceAuditTranslate;
}>) {
  const audit = state.audit;
  if (audit.kind === "loading") {
    return (
      <View style={styles.loadingLine}>
        <ActivityIndicator color={posColors.orange} />
        <Text style={styles.loadingText}>{t("audit.loading")}</Text>
      </View>
    );
  }
  const message = auditStatusMessage(audit.statusCode, t);
  if (!message) return null;
  return (
    <View
      accessibilityLiveRegion="polite"
      style={styles.auditWarning}
    >
      <Text style={styles.auditWarningText}>{message}</Text>
    </View>
  );
}

function AuditList({
  presenter,
  state,
  t,
}: Readonly<{
  presenter: AttendanceAuditScreenPresenter;
  state: AttendanceAuditPresenterState;
  t: AttendanceAuditTranslate;
}>) {
  const audit = state.audit;
  return (
    <View style={styles.auditListPane}>
      <Text style={styles.sectionLabel}>{t("audit.records")}</Text>
      <ScrollView contentContainerStyle={styles.auditList}>
        {audit.rows.length === 0 ? (
          <View style={styles.emptyCard}>
            <Text style={styles.emptyTitle}>{t("audit.empty.title")}</Text>
            <Text style={styles.emptyBody}>{t("audit.empty.body")}</Text>
          </View>
        ) : (
          audit.rows.map((row) => (
            <AuditRow
              key={row.eventId}
              onPress={() => void presenter.selectAudit(row.eventId)}
              record={row}
              selected={audit.selectedEventId === row.eventId}
              t={t}
            />
          ))
        )}
      </ScrollView>
    </View>
  );
}

function AuditRow({
  onPress,
  record,
  selected,
  t,
}: Readonly<{
  onPress(): void;
  record: OperationAuditRecord;
  selected: boolean;
  t: AttendanceAuditTranslate;
}>) {
  return (
    <PosPressable
      accessibilityRole="button"
      accessibilityState={{ selected }}
      onPress={onPress}
      style={[styles.auditRow, selected && styles.auditRowSelected]}
      testID={`audit-row-${record.eventId}`}
    >
      <View style={styles.auditRowTop}>
        <Text numberOfLines={1} style={styles.auditOperation}>
          {record.operationType}
        </Text>
        <StatusPill
          label={uploadStateLabel(record.uploadState, t)}
          tone={
            record.uploadState === "rejected"
              ? "danger"
              : record.uploadState === "pending"
                ? "warning"
                : "success"
          }
        />
      </View>
      <Text numberOfLines={1} style={styles.auditPrimary}>
        {record.receiptNumber ??
          record.orderGuid ??
          record.eventId.slice(0, 8)}
      </Text>
      <Text style={styles.auditMeta}>
        {formatTimestamp(record.occurredAtIso)} · {record.cashierName ?? "—"}
      </Text>
    </PosPressable>
  );
}

function AuditDetails({
  state,
  t,
}: Readonly<{
  state: AttendanceAuditPresenterState;
  t: AttendanceAuditTranslate;
}>) {
  const { audit } = state;
  return (
    <View style={styles.auditDetailPane}>
      <Text style={styles.sectionLabel}>{t("audit.details")}</Text>
      <ScrollView contentContainerStyle={styles.detailScroll}>
        {audit.detailLoading ? (
          <ActivityIndicator color={posColors.orange} />
        ) : audit.detail ? (
          <AuditDetailCard record={audit.detail} t={t} />
        ) : (
          <View style={styles.emptyCard}>
            <Text style={styles.emptyTitle}>{t("audit.detail.empty.title")}</Text>
            <Text style={styles.emptyBody}>{t("audit.detail.empty.body")}</Text>
          </View>
        )}
      </ScrollView>
    </View>
  );
}

function AuditDetailCard({
  record,
  t,
}: Readonly<{
  record: OperationAuditRecord;
  t: AttendanceAuditTranslate;
}>) {
  return (
    <View style={styles.detailCard}>
      <DetailRow label={t("audit.detail.operation")} value={record.operationType} />
      <DetailRow label={t("audit.detail.outcome")} value={record.outcome} />
      <DetailRow
        label={t("audit.detail.time")}
        value={formatTimestamp(record.occurredAtIso)}
      />
      <DetailRow
        label={t("audit.detail.cashier")}
        value={record.cashierName ?? "—"}
      />
      <DetailRow
        label={t("audit.detail.receipt")}
        value={record.receiptNumber ?? "—"}
      />
      <DetailRow
        label={t("audit.detail.order")}
        value={record.orderGuid ?? "—"}
      />
      <DetailRow
        label={t("audit.detail.amount")}
        value={
          record.paymentAmountCents === null
            ? "—"
            : formatMoney(record.paymentAmountCents)
        }
      />
      <DetailRow
        label={t("audit.detail.correlation")}
        value={record.correlationId ?? "—"}
      />
      {record.safeMessage ? (
        <View style={styles.messageCard}>
          <Text style={styles.detailLabel}>{t("audit.detail.safeMessage")}</Text>
          <Text style={styles.safeMessage}>{record.safeMessage}</Text>
        </View>
      ) : null}
      {record.items.length > 0 ? (
        <View style={styles.itemsCard}>
          <Text style={styles.detailLabel}>{t("audit.detail.itemChanges")}</Text>
          {record.items.map((item) => (
            <View key={`${item.lineIndex}-${item.productCode ?? "none"}`}>
              <Text style={styles.itemTitle}>
                {item.displayName ?? item.productCode ?? "—"}
              </Text>
              <Text style={styles.itemMeta}>
                #{item.lineIndex + 1} · {item.quantityDelta ?? "—"} ·{" "}
                {item.actualAmountDeltaCents === null
                  ? "—"
                  : formatMoney(item.actualAmountDeltaCents)}
              </Text>
            </View>
          ))}
        </View>
      ) : null}
    </View>
  );
}

export function AttendanceAuditUnavailableScreen({
  onBack,
}: Readonly<{ onBack?(): void }>) {
  const { i18n } = useTranslation();
  const locale = resolveAttendanceAuditLocale(
    i18n.resolvedLanguage ?? i18n.language,
  );
  const t: AttendanceAuditTranslate = (key, values) =>
    attendanceAuditText(locale, key, values);
  return (
    <SafeAreaView
      style={styles.safeArea}
      testID="attendance-audit-runtime-unavailable"
    >
      <View style={styles.unavailablePage}>
        <Text style={styles.permissionCode}>SECURE RUNTIME</Text>
        <Text style={styles.permissionTitle}>{t("unavailable.title")}</Text>
        <Text style={styles.permissionBody}>{t("unavailable.body")}</Text>
        {onBack ? (
          <ActionButton
            label={t("action.back")}
            onPress={onBack}
            sound="navigate"
            testID="attendance-audit-unavailable-back"
          />
        ) : null}
      </View>
    </SafeAreaView>
  );
}

function ContextRow({
  label,
  value,
}: Readonly<{ label: string; value: string }>) {
  return (
    <View style={styles.contextRow}>
      <Text style={styles.contextLabel}>{label}</Text>
      <Text numberOfLines={1} style={styles.contextValue}>
        {value}
      </Text>
    </View>
  );
}

function DetailRow({
  label,
  value,
}: Readonly<{ label: string; value: string }>) {
  return (
    <View style={styles.detailRow}>
      <Text style={styles.detailLabel}>{label}</Text>
      <Text selectable style={styles.detailValue}>
        {value}
      </Text>
    </View>
  );
}

function QrStatusMessage({
  statusCode,
  t,
}: Readonly<{
  statusCode: AttendanceAuditPresenterState["qr"]["statusCode"];
  t: AttendanceAuditTranslate;
}>) {
  return (
    <Text style={styles.qrStatus}>
      {t(attendanceAuditQrStatusCopyKey(statusCode))}
    </Text>
  );
}

function ActionButton({
  compact = false,
  disabled = false,
  label,
  onPress,
  selected = false,
  sound = "tap",
  testID,
  tone = "primary",
}: Readonly<{
  compact?: boolean;
  disabled?: boolean;
  label: string;
  onPress(): void;
  selected?: boolean;
  sound?: "navigate" | "tap";
  testID: string;
  tone?: "primary" | "quiet" | "secondary";
}>) {
  return (
    <PosPressable
      accessibilityRole="button"
      accessibilityState={{ disabled, selected }}
      disabled={disabled}
      onPress={onPress}
      sound={sound}
      style={[
        styles.button,
        compact && styles.compactButton,
        tone === "quiet" && styles.quietButton,
        tone === "secondary" && styles.secondaryButton,
        selected && styles.selectedButton,
        disabled && styles.disabledButton,
      ]}
      testID={testID}
    >
      <Text
        style={[
          styles.buttonText,
          tone !== "primary" && styles.secondaryButtonText,
          selected && styles.selectedButtonText,
        ]}
      >
        {label}
      </Text>
    </PosPressable>
  );
}

function StatusPill({
  label,
  tone,
}: Readonly<{
  label: string;
  tone: "danger" | "success" | "warning";
}>) {
  return (
    <View
      style={[
        styles.pill,
        tone === "success" && styles.successPill,
        tone === "warning" && styles.warningPill,
        tone === "danger" && styles.dangerPill,
      ]}
    >
      <Text style={styles.pillText}>{label}</Text>
    </View>
  );
}

function auditStatusMessage(
  statusCode: AttendanceAuditPresenterState["audit"]["statusCode"],
  t: AttendanceAuditTranslate,
): string | null {
  if (!statusCode) return null;
  return t(attendanceAuditStatusCopyKey(statusCode));
}

function uploadStateLabel(
  value: OperationAuditUploadState,
  t: AttendanceAuditTranslate,
): string {
  return t(attendanceAuditUploadStateCopyKey(value));
}

function formatTimestamp(value: string): string {
  return value.replace("T", " ").replace(".000Z", " UTC");
}

function formatMoney(cents: number): string {
  const sign = cents < 0 ? "-" : "";
  return `${sign}$${(Math.abs(cents) / 100).toFixed(2)}`;
}

const styles = StyleSheet.create({
  safeArea: {
    flex: 1,
    backgroundColor: posColors.canvas,
  },
  page: {
    flex: 1,
    paddingHorizontal: 22,
    paddingVertical: 16,
    gap: 14,
  },
  header: {
    minHeight: 88,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 20,
  },
  eyebrow: {
    color: posColors.orange,
    fontSize: 11,
    fontWeight: "800",
    letterSpacing: 1.8,
  },
  title: {
    color: posColors.ink,
    fontSize: 30,
    fontWeight: "900",
    letterSpacing: -0.7,
  },
  subtitle: {
    color: posColors.mutedInk,
    fontSize: 13,
    lineHeight: 18,
    maxWidth: 760,
  },
  workspace: {
    flex: 1,
    flexDirection: "row",
    gap: 14,
    minHeight: 0,
  },
  panel: {
    backgroundColor: posColors.surface,
    borderColor: posColors.border,
    borderWidth: 1,
    borderRadius: 18,
    padding: 16,
  },
  qrPanel: {
    width: "33%",
    minWidth: 300,
    gap: 10,
  },
  auditPanel: {
    flex: 1,
    minWidth: 0,
  },
  centeredPanel: {
    alignItems: "center",
    justifyContent: "center",
    paddingHorizontal: 60,
  },
  panelHeading: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 12,
  },
  panelKicker: {
    color: posColors.mutedInk,
    fontSize: 10,
    fontWeight: "800",
    letterSpacing: 1.5,
  },
  panelTitle: {
    color: posColors.ink,
    fontSize: 21,
    fontWeight: "900",
  },
  qrStage: {
    minHeight: 222,
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: "#FAF9F6",
    borderColor: posColors.border,
    borderWidth: 1,
    borderRadius: 14,
    overflow: "hidden",
  },
  qrImage: {
    width: 218,
    height: 218,
  },
  qrPlaceholder: {
    alignItems: "center",
    paddingHorizontal: 24,
    gap: 12,
  },
  placeholderTitle: {
    color: posColors.ink,
    fontSize: 16,
    fontWeight: "800",
    textAlign: "center",
  },
  placeholderBody: {
    color: posColors.mutedInk,
    fontSize: 12,
    lineHeight: 17,
    textAlign: "center",
  },
  clockLock: {
    width: "100%",
    height: "100%",
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: posColors.redSoft,
    paddingHorizontal: 28,
    gap: 8,
  },
  lockIcon: {
    width: 42,
    height: 42,
    borderRadius: 21,
    backgroundColor: posColors.red,
    color: "#FFFFFF",
    fontSize: 25,
    lineHeight: 42,
    fontWeight: "900",
    textAlign: "center",
  },
  lockTitle: {
    color: posColors.red,
    fontSize: 19,
    fontWeight: "900",
  },
  lockBody: {
    color: posColors.ink,
    fontSize: 13,
    lineHeight: 19,
    textAlign: "center",
  },
  countdownCard: {
    backgroundColor: posColors.blueSoft,
    borderRadius: 12,
    paddingHorizontal: 14,
    paddingVertical: 10,
  },
  countdownValue: {
    color: posColors.blue,
    fontSize: 20,
    fontWeight: "900",
  },
  countdownLabel: {
    color: posColors.mutedInk,
    fontSize: 11,
  },
  contextCard: {
    borderColor: posColors.border,
    borderWidth: 1,
    borderRadius: 12,
    paddingHorizontal: 12,
  },
  contextRow: {
    minHeight: 34,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 10,
  },
  contextLabel: {
    color: posColors.mutedInk,
    fontSize: 11,
    fontWeight: "700",
  },
  contextValue: {
    flex: 1,
    color: posColors.ink,
    fontSize: 12,
    fontWeight: "700",
    textAlign: "right",
  },
  qrStatus: {
    minHeight: 32,
    color: posColors.mutedInk,
    fontSize: 11,
    lineHeight: 16,
  },
  filters: {
    marginTop: 12,
    gap: 10,
  },
  filterLine: {
    flexDirection: "row",
    alignItems: "center",
    flexWrap: "wrap",
    gap: 7,
  },
  searchRow: {
    flexDirection: "row",
    gap: 8,
  },
  searchInput: {
    flex: 1,
    minHeight: ATTENDANCE_AUDIT_MIN_TOUCH_TARGET,
    borderColor: posColors.border,
    borderWidth: 1,
    borderRadius: 10,
    backgroundColor: "#FAF9F6",
    color: posColors.ink,
    fontSize: 14,
    paddingHorizontal: 12,
  },
  auditMasterDetail: {
    flex: 1,
    minHeight: 0,
    flexDirection: "row",
    gap: 12,
    marginTop: 10,
  },
  auditListPane: {
    width: "46%",
    minWidth: 0,
  },
  auditDetailPane: {
    flex: 1,
    minWidth: 0,
    borderLeftColor: posColors.border,
    borderLeftWidth: 1,
    paddingLeft: 12,
  },
  sectionLabel: {
    color: posColors.mutedInk,
    fontSize: 10,
    fontWeight: "900",
    letterSpacing: 1.3,
    marginBottom: 7,
  },
  auditList: {
    gap: 8,
    paddingBottom: 14,
  },
  auditRow: {
    minHeight: 86,
    borderColor: posColors.border,
    borderWidth: 1,
    borderRadius: 11,
    backgroundColor: "#FAF9F6",
    padding: 11,
    gap: 4,
  },
  auditRowSelected: {
    borderColor: posColors.orange,
    borderWidth: 2,
    backgroundColor: posColors.orangeSoft,
  },
  auditRowTop: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 8,
  },
  auditOperation: {
    flex: 1,
    color: posColors.ink,
    fontSize: 13,
    fontWeight: "900",
  },
  auditPrimary: {
    color: posColors.blue,
    fontSize: 12,
    fontWeight: "700",
  },
  auditMeta: {
    color: posColors.mutedInk,
    fontSize: 10,
  },
  detailScroll: {
    paddingBottom: 14,
  },
  detailCard: {
    gap: 8,
  },
  detailRow: {
    borderBottomColor: posColors.border,
    borderBottomWidth: 1,
    paddingBottom: 7,
    gap: 2,
  },
  detailLabel: {
    color: posColors.mutedInk,
    fontSize: 10,
    fontWeight: "800",
    letterSpacing: 0.3,
  },
  detailValue: {
    color: posColors.ink,
    fontSize: 12,
    lineHeight: 17,
    fontWeight: "600",
  },
  messageCard: {
    backgroundColor: posColors.blueSoft,
    borderRadius: 10,
    padding: 10,
    gap: 4,
  },
  safeMessage: {
    color: posColors.ink,
    fontSize: 12,
    lineHeight: 18,
  },
  itemsCard: {
    backgroundColor: "#FAF9F6",
    borderColor: posColors.border,
    borderWidth: 1,
    borderRadius: 10,
    padding: 10,
    gap: 7,
  },
  itemTitle: {
    color: posColors.ink,
    fontSize: 12,
    fontWeight: "800",
  },
  itemMeta: {
    color: posColors.mutedInk,
    fontSize: 11,
  },
  resultCount: {
    color: posColors.mutedInk,
    fontSize: 12,
    fontWeight: "700",
  },
  loadingLine: {
    minHeight: 40,
    flexDirection: "row",
    alignItems: "center",
    gap: 8,
  },
  loadingText: {
    color: posColors.mutedInk,
    fontSize: 12,
  },
  auditWarning: {
    marginTop: 8,
    backgroundColor: posColors.redSoft,
    borderRadius: 9,
    padding: 9,
  },
  auditWarningText: {
    color: posColors.red,
    fontSize: 11,
    fontWeight: "700",
  },
  emptyCard: {
    borderColor: posColors.border,
    borderWidth: 1,
    borderStyle: "dashed",
    borderRadius: 12,
    padding: 18,
    gap: 6,
  },
  emptyTitle: {
    color: posColors.ink,
    fontSize: 14,
    fontWeight: "800",
  },
  emptyBody: {
    color: posColors.mutedInk,
    fontSize: 11,
    lineHeight: 17,
  },
  permissionCode: {
    color: posColors.orange,
    fontSize: 11,
    fontWeight: "900",
    letterSpacing: 1.8,
  },
  permissionTitle: {
    color: posColors.ink,
    fontSize: 24,
    fontWeight: "900",
    textAlign: "center",
  },
  permissionBody: {
    maxWidth: 580,
    color: posColors.mutedInk,
    fontSize: 14,
    lineHeight: 21,
    textAlign: "center",
  },
  unavailablePage: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
    paddingHorizontal: 80,
    gap: 16,
  },
  button: {
    minHeight: ATTENDANCE_AUDIT_MIN_TOUCH_TARGET,
    minWidth: 80,
    alignItems: "center",
    justifyContent: "center",
    borderRadius: 10,
    backgroundColor: posColors.orange,
    paddingHorizontal: 14,
    paddingVertical: 8,
  },
  compactButton: {
    minWidth: 64,
    paddingHorizontal: 9,
  },
  quietButton: {
    backgroundColor: "#F1EEE7",
    borderColor: posColors.border,
    borderWidth: 1,
  },
  secondaryButton: {
    backgroundColor: posColors.blueSoft,
    borderColor: "#B7CEE2",
    borderWidth: 1,
  },
  selectedButton: {
    backgroundColor: posColors.ink,
    borderColor: posColors.ink,
  },
  disabledButton: {
    opacity: 0.38,
  },
  buttonText: {
    color: "#FFFFFF",
    fontSize: 12,
    fontWeight: "800",
  },
  secondaryButtonText: {
    color: posColors.ink,
  },
  selectedButtonText: {
    color: "#FFFFFF",
  },
  pill: {
    maxWidth: 150,
    borderRadius: 999,
    paddingHorizontal: 9,
    paddingVertical: 5,
  },
  successPill: {
    backgroundColor: posColors.greenSoft,
  },
  warningPill: {
    backgroundColor: "#FFF1D7",
  },
  dangerPill: {
    backgroundColor: posColors.redSoft,
  },
  pillText: {
    color: posColors.ink,
    fontSize: 9,
    fontWeight: "800",
  },
});
