import { useEffect, useSyncExternalStore } from "react";
import {
  ActivityIndicator,
  Image,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  View,
} from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";

import type {
  AttendanceAuditPresenter,
  AttendanceAuditPresenterState,
} from "./attendance-audit-presenter";
import type {
  OperationAuditRecord,
  OperationAuditSource,
  OperationAuditUploadState,
} from "./operation-audit-presenter";

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

/**
 * iPad 横屏高密度工作台：考勤 QR 始终独立显示；审计读取按 Audit.View
 * 单独保护。UI 永不接收二维码 token、签名密钥或审计 properties 原文。
 */
export function AttendanceAuditScreen({
  onBack,
  presenter,
}: AttendanceAuditScreenProps) {
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
        <Header onBack={onBack} />
        <View
          style={styles.workspace}
          testID="attendance-audit-workspace"
        >
          <AttendanceQrPane presenter={presenter} state={state} />
          <AuditWorkspace presenter={presenter} state={state} />
        </View>
      </View>
    </SafeAreaView>
  );
}

function Header({
  onBack,
}: Readonly<{ onBack: (() => void) | undefined }>) {
  return (
    <View style={styles.header}>
      <View>
        <Text style={styles.eyebrow}>门店安全 / STORE SECURITY</Text>
        <Text style={styles.title}>
          考勤与审计 / Attendance & audit
        </Text>
        <Text style={styles.subtitle}>
          动态短时二维码与本设备操作轨迹；密钥、token 和支付引用不会显示。
          / Short-lived QR and device-scoped audit trail; secrets stay hidden.
        </Text>
      </View>
      {onBack ? (
        <ActionButton
          label="返回销售 / Back to sales"
          onPress={onBack}
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
}: Readonly<{
  presenter: AttendanceAuditScreenPresenter;
  state: AttendanceAuditPresenterState;
}>) {
  const qr = state.qr;
  const locked =
    qr.kind === "clock-invalid" || qr.requiresOnlineResync;
  return (
    <View style={[styles.panel, styles.qrPanel]}>
      <View style={styles.panelHeading}>
        <View>
          <Text style={styles.panelKicker}>ATTENDANCE</Text>
          <Text style={styles.panelTitle}>考勤二维码 / QR</Text>
        </View>
        <StatusPill
          label={
            qr.online
              ? "在线验证 / Verified"
              : "离线签发 / Offline signed"
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
            <Text style={styles.lockTitle}>
              时钟回拨 / Clock rollback
            </Text>
            <Text style={styles.lockBody}>
              二维码已锁定。请在线重新同步可信时间后再使用。
              / QR locked; an online re-sync of trusted time is required.
            </Text>
          </View>
        ) : qr.qrImageUri ? (
          <Image
            accessibilityLabel="考勤二维码 / Attendance QR"
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
              {qr.kind === "initializing"
                ? "正在建立安全二维码… / Preparing secure QR…"
                : "二维码暂不可用 / QR unavailable"}
            </Text>
            <Text style={styles.placeholderBody}>
              首次启用或安全身份失效时必须联网登记。
              / Online registration is required for first use or re-keying.
            </Text>
          </View>
        )}
      </View>

      <View style={styles.countdownCard}>
        <Text style={styles.countdownValue}>
          {qr.secondsRemaining} 秒 / {qr.secondsRemaining} sec
        </Text>
        <Text style={styles.countdownLabel}>
          当前二维码剩余有效期 / Current QR validity
        </Text>
      </View>
      <View style={styles.contextCard}>
        <ContextRow label="门店 / Store" value={qr.storeText || "—"} />
        <ContextRow
          label="设备 / Device"
          value={qr.deviceText || "—"}
        />
      </View>
      <QrStatusMessage statusCode={qr.statusCode} />
      <ActionButton
        label="安全刷新 / Secure refresh"
        onPress={() => void presenter.refreshAttendanceQr()}
        testID="attendance-qr-refresh"
      />
    </View>
  );
}

function AuditWorkspace({
  presenter,
  state,
}: Readonly<{
  presenter: AttendanceAuditScreenPresenter;
  state: AttendanceAuditPresenterState;
}>) {
  const audit = state.audit;
  if (!audit.access.canView) {
    return (
      <View
        style={[styles.panel, styles.auditPanel, styles.centeredPanel]}
        testID="audit-permission-required"
      >
        <Text style={styles.permissionCode}>AUDIT.VIEW</Text>
        <Text style={styles.permissionTitle}>
          审计记录受保护 / Audit trail protected
        </Text>
        <Text style={styles.permissionBody}>
          当前收银员没有 Permissions.PosTerminal.Audit.View。
          考勤二维码仍可正常使用；请由主管授权后重新登录。
          / The current cashier cannot view audit records. Attendance QR
          remains available; sign in again after supervisor approval.
        </Text>
      </View>
    );
  }

  return (
    <View style={[styles.panel, styles.auditPanel]}>
      <View style={styles.panelHeading}>
        <View>
          <Text style={styles.panelKicker}>OPERATION AUDIT</Text>
          <Text style={styles.panelTitle}>操作审计 / Audit trail</Text>
        </View>
        <Text style={styles.resultCount}>
          {audit.rows.length} 项 / records
        </Text>
      </View>

      <AuditFilters presenter={presenter} state={state} />
      <AuditStatus state={state} />

      <View style={styles.auditMasterDetail}>
        <AuditList presenter={presenter} state={state} />
        <AuditDetails state={state} />
      </View>
    </View>
  );
}

function AuditFilters({
  presenter,
  state,
}: Readonly<{
  presenter: AttendanceAuditScreenPresenter;
  state: AttendanceAuditPresenterState;
}>) {
  const audit = state.audit;
  const sources: readonly {
    label: string;
    source: OperationAuditSource;
  }[] = [
    { label: "本机 / Local", source: "local" },
    { label: "远程 / Remote", source: "remote" },
  ];
  const uploadStates: readonly {
    label: string;
    state: OperationAuditUploadState | null;
  }[] = [
    { label: "全部 / All", state: null },
    { label: "待传 / Pending", state: "pending" },
    { label: "已传 / Uploaded", state: "uploaded" },
    { label: "拒绝 / Rejected", state: "rejected" },
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
        <TextInput
          accessibilityLabel="搜索操作审计 / Search operation audit"
          autoCapitalize="none"
          autoCorrect={false}
          onChangeText={(value) => presenter.setAuditQuery(value)}
          onSubmitEditing={() => void presenter.loadAudit()}
          placeholder="小票、订单、操作 / Receipt, order, operation"
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
          label="查询 / Search"
          onPress={() => void presenter.loadAudit()}
          testID="audit-search-submit"
        />
      </View>
    </View>
  );
}

function AuditStatus({
  state,
}: Readonly<{ state: AttendanceAuditPresenterState }>) {
  const audit = state.audit;
  if (audit.kind === "loading") {
    return (
      <View style={styles.loadingLine}>
        <ActivityIndicator color={posColors.orange} />
        <Text style={styles.loadingText}>
          正在读取审计记录… / Loading audit records…
        </Text>
      </View>
    );
  }
  const message = auditStatusMessage(audit.statusCode);
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
}: Readonly<{
  presenter: AttendanceAuditScreenPresenter;
  state: AttendanceAuditPresenterState;
}>) {
  const audit = state.audit;
  return (
    <View style={styles.auditListPane}>
      <Text style={styles.sectionLabel}>记录 / RECORDS</Text>
      <ScrollView contentContainerStyle={styles.auditList}>
        {audit.rows.length === 0 ? (
          <View style={styles.emptyCard}>
            <Text style={styles.emptyTitle}>
              暂无记录 / No audit records
            </Text>
            <Text style={styles.emptyBody}>
              更改来源或筛选后重新查询。
              / Change source or filters, then search again.
            </Text>
          </View>
        ) : (
          audit.rows.map((row) => (
            <AuditRow
              key={row.eventId}
              onPress={() => void presenter.selectAudit(row.eventId)}
              record={row}
              selected={audit.selectedEventId === row.eventId}
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
}: Readonly<{
  onPress(): void;
  record: OperationAuditRecord;
  selected: boolean;
}>) {
  return (
    <Pressable
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
          label={uploadStateLabel(record.uploadState)}
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
    </Pressable>
  );
}

function AuditDetails({
  state,
}: Readonly<{ state: AttendanceAuditPresenterState }>) {
  const { audit } = state;
  return (
    <View style={styles.auditDetailPane}>
      <Text style={styles.sectionLabel}>详情 / DETAILS</Text>
      <ScrollView contentContainerStyle={styles.detailScroll}>
        {audit.detailLoading ? (
          <ActivityIndicator color={posColors.orange} />
        ) : audit.detail ? (
          <AuditDetailCard record={audit.detail} />
        ) : (
          <View style={styles.emptyCard}>
            <Text style={styles.emptyTitle}>
              选择一条记录 / Select a record
            </Text>
            <Text style={styles.emptyBody}>
              这里只显示已校验并脱敏的字段。
              / Only validated and redacted fields are shown.
            </Text>
          </View>
        )}
      </ScrollView>
    </View>
  );
}

function AuditDetailCard({
  record,
}: Readonly<{ record: OperationAuditRecord }>) {
  return (
    <View style={styles.detailCard}>
      <DetailRow label="操作 / Operation" value={record.operationType} />
      <DetailRow label="结果 / Outcome" value={record.outcome} />
      <DetailRow
        label="时间 / Time"
        value={formatTimestamp(record.occurredAtIso)}
      />
      <DetailRow
        label="收银员 / Cashier"
        value={record.cashierName ?? "—"}
      />
      <DetailRow
        label="小票 / Receipt"
        value={record.receiptNumber ?? "—"}
      />
      <DetailRow
        label="订单 / Order"
        value={record.orderGuid ?? "—"}
      />
      <DetailRow
        label="金额 / Amount"
        value={
          record.paymentAmountCents === null
            ? "—"
            : formatMoney(record.paymentAmountCents)
        }
      />
      <DetailRow
        label="关联 / Correlation"
        value={record.correlationId ?? "—"}
      />
      {record.safeMessage ? (
        <View style={styles.messageCard}>
          <Text style={styles.detailLabel}>安全消息 / Safe message</Text>
          <Text style={styles.safeMessage}>{record.safeMessage}</Text>
        </View>
      ) : null}
      {record.items.length > 0 ? (
        <View style={styles.itemsCard}>
          <Text style={styles.detailLabel}>商品变化 / Item changes</Text>
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
  return (
    <SafeAreaView
      style={styles.safeArea}
      testID="attendance-audit-runtime-unavailable"
    >
      <View style={styles.unavailablePage}>
        <Text style={styles.permissionCode}>SECURE RUNTIME</Text>
        <Text style={styles.permissionTitle}>
          安全服务尚未接线 / Secure services unavailable
        </Text>
        <Text style={styles.permissionBody}>
          考勤密钥、可信时间或审计仓储未由原生组合根提供。页面保持关闭，
          不会用临时存储降级。 / The native composition root has not supplied
          secure key, trusted-time, or audit storage services. No insecure
          fallback is used.
        </Text>
        {onBack ? (
          <ActionButton
            label="返回销售 / Back to sales"
            onPress={onBack}
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
}: Readonly<{ statusCode: AttendanceAuditPresenterState["qr"]["statusCode"] }>) {
  const messages = {
    "clock-rollback":
      "可信时间已锁定 / Trusted time locked",
    "enable-online":
      "首次启用需联网 / Online setup required",
    "offline-signed":
      "使用已登记本机密钥离线签发 / Signed locally with the registered device key",
    "online-verified":
      "设备身份与可信时间已在线验证 / Device identity and trusted time verified",
    "setup-failed":
      "安全二维码建立失败，请检查网络后重试 / Secure QR setup failed; check connectivity and retry",
  } as const;
  return <Text style={styles.qrStatus}>{messages[statusCode]}</Text>;
}

function ActionButton({
  compact = false,
  disabled = false,
  label,
  onPress,
  selected = false,
  testID,
  tone = "primary",
}: Readonly<{
  compact?: boolean;
  disabled?: boolean;
  label: string;
  onPress(): void;
  selected?: boolean;
  testID: string;
  tone?: "primary" | "quiet" | "secondary";
}>) {
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityState={{ disabled, selected }}
      disabled={disabled}
      onPress={onPress}
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
    </Pressable>
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
): string | null {
  if (!statusCode) return null;
  const messages = {
    "details-failed":
      "详情读取失败；未显示部分结果。 / Details failed; no partial result shown.",
    "details-unavailable":
      "记录已不存在或不可访问。 / Record is unavailable.",
    "list-failed":
      "审计读取失败；未显示可能误导的部分列表。 / Audit load failed; no partial list shown.",
    "online-required":
      "远程审计必须联网；本机审计仍可读取。 / Remote audit requires connectivity; local audit remains available.",
    "permission-required":
      "当前收银员无审计查看权限。 / Audit.View permission is required.",
  } as const;
  return messages[statusCode];
}

function uploadStateLabel(value: OperationAuditUploadState): string {
  if (value === "pending") return "待传 / Pending";
  if (value === "rejected") return "拒绝 / Rejected";
  return "已传 / Uploaded";
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
