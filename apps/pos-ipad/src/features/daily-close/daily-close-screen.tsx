import {
  useCallback,
  useEffect,
  useRef,
  useState,
  useSyncExternalStore,
  type Ref,
} from "react";
import {
  Keyboard,
  KeyboardAvoidingView,
  Platform,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  View,
  type FocusEvent,
  type StyleProp,
  type ViewStyle,
} from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";

import {
  type DailyClosePresenter,
  type DailyCloseState,
  type DailyCloseStatusCode,
} from "./daily-close-presenter";

import type {
  AudCashDenominationCents,
  DailyCloseArchive,
  DailyCloseTenderBreakdown,
} from "@/core/contracts";
import { posColors } from "@/ui/theme";

export const DAILY_CLOSE_MIN_TOUCH_TARGET = 44;
export const DAILY_CLOSE_KEYBOARD_AVOIDER_ENABLED = Platform.OS !== "ios";
export const DAILY_CLOSE_KEYBOARD_AVOIDING_BEHAVIOR = "height";

const DAILY_CLOSE_KEYBOARD_SCROLL_OFFSET = 16;

export type DailyCloseScreenPresenter = Pick<
  DailyClosePresenter,
  | "getState"
  | "load"
  | "reprintSelected"
  | "saveAndPrint"
  | "selectArchive"
  | "setBusinessDate"
  | "setCount"
  | "showCount"
  | "showHistory"
  | "subscribe"
> &
  Readonly<{ getState(): DailyCloseState }>;

type DailyCloseScreenProps = Readonly<{
  onBack?(): void;
  presenter: DailyCloseScreenPresenter;
}>;

export function DailyCloseScreen({ onBack, presenter }: DailyCloseScreenProps) {
  const state = useSyncExternalStore(
    presenter.subscribe,
    presenter.getState,
    presenter.getState,
  );
  const [businessDate, setBusinessDate] = useState(state.businessDate);
  const firstCountInputRef = useRef<TextInput>(null);
  const summaryScrollRef = useRef<ScrollView>(null);

  const revealSummaryInput = useCallback((event: FocusEvent) => {
    // 自动 inset 负责腾出键盘空间；这里再把实际焦点精确滚到键盘上沿。
    summaryScrollRef.current?.scrollResponderScrollNativeHandleToKeyboard(
      event.target,
      DAILY_CLOSE_KEYBOARD_SCROLL_OFFSET,
      true,
    );
  }, []);

  useEffect(() => {
    setBusinessDate(state.businessDate);
  }, [state.businessDate]);

  useEffect(() => {
    void presenter.load();
  }, [presenter]);

  useEffect(() => {
    if (state.activePane === "history") Keyboard.dismiss();
  }, [state.activePane]);

  const refresh = () => {
    if (!presenter.setBusinessDate(businessDate)) return;
    void presenter.load();
  };

  const showCount = () => {
    presenter.showCount();
    // 重新取得第一响应者，确保从 History 返回或重复点 Count 时再次请求系统键盘。
    firstCountInputRef.current?.blur();
    firstCountInputRef.current?.focus();
  };

  return (
    <SafeAreaView style={styles.safeArea} testID="daily-close-screen">
      <KeyboardAvoidingView
        behavior={DAILY_CLOSE_KEYBOARD_AVOIDING_BEHAVIOR}
        // iOS 由 ScrollView 的 keyboard inset 负责避让，避免重复计算同一键盘高度。
        enabled={DAILY_CLOSE_KEYBOARD_AVOIDER_ENABLED}
        style={styles.keyboardAvoider}
        testID="daily-close-keyboard-avoider"
      >
        <View style={styles.page}>
          <View style={styles.header}>
            <View style={styles.titleGroup}>
              <Text style={styles.eyebrow}>门店运营 / STORE OPERATIONS</Text>
              <Text style={styles.title}>日结 / Daily close</Text>
              <Text style={styles.subtitle}>
                本机、本门店、本营业日汇总；每次保存形成独立冻结归档。 / Local
                store, terminal and business-day totals; every save creates a
                separate archive.
              </Text>
            </View>
            <View style={styles.headerActions}>
              {onBack ? (
                <ActionButton
                  label="返回 / Back"
                  onPress={onBack}
                  testID="daily-close-back"
                  tone="quiet"
                />
              ) : null}
              <ActionButton
                disabled={state.busy}
                label="点钞 / Count"
                onPress={showCount}
                selected={state.activePane === "count"}
                testID="daily-close-show-count"
                tone="secondary"
              />
              <ActionButton
                disabled={state.busy}
                label="历史 / History"
                onPress={() => presenter.showHistory()}
                selected={state.activePane === "history"}
                testID="daily-close-show-history"
                tone="secondary"
              />
            </View>
          </View>

          {state.statusCode ? (
            <StatusBanner statusCode={state.statusCode} />
          ) : null}

          <View style={styles.workspace} testID="daily-close-workspace">
            <ScrollView
              automaticallyAdjustKeyboardInsets
              contentContainerStyle={styles.paneContent}
              keyboardDismissMode="interactive"
              keyboardShouldPersistTaps="handled"
              ref={summaryScrollRef}
              style={[styles.pane, styles.summaryPane]}
              testID="daily-close-summary-scroll"
            >
              <View style={styles.sectionHeader}>
                <View>
                  <Text style={styles.sectionTitle}>
                    当日汇总 / Business-day summary
                  </Text>
                  <Text style={styles.sectionHint}>
                    时间窗为门店本地午夜起止的半开区间 [from, to)。
                  </Text>
                </View>
                <View style={styles.dateControls}>
                  <TextInput
                    accessibilityLabel="营业日 / Business date"
                    editable={!state.busy}
                    onChangeText={setBusinessDate}
                    onFocus={revealSummaryInput}
                    placeholder="YYYY-MM-DD"
                    style={styles.dateInput}
                    testID="daily-close-business-date"
                    value={businessDate}
                  />
                  <ActionButton
                    disabled={state.busy}
                    label={
                      state.kind === "loading"
                        ? "刷新中… / Loading…"
                        : "刷新 / Refresh"
                    }
                    onPress={refresh}
                    testID="daily-close-refresh"
                  />
                </View>
              </View>

              {state.summary ? (
                <>
                  <View style={styles.tenderCard}>
                    <View style={styles.tenderHeader}>
                      <Text style={styles.tenderMethod}>方式 / Method</Text>
                      <Text style={styles.tenderValues}>
                        销售 / 退款 / 净额
                      </Text>
                    </View>
                    {state.summary.tenders.map((tender) => (
                      <TenderRow key={tender.method} tender={tender} />
                    ))}
                  </View>

                  <View style={styles.metrics}>
                    <Metric
                      label="订单 / Orders"
                      value={String(state.summary.orderCount)}
                    />
                    <Metric
                      label="退货数量 / Return qty"
                      value={state.summary.returnQuantity}
                    />
                    <Metric
                      label="应有现金 / Expected"
                      value={money(state.summary.expectedCashCents)}
                    />
                  </View>
                </>
              ) : (
                <View style={styles.emptyCard}>
                  <Text style={styles.emptyTitle}>
                    {state.kind === "loading"
                      ? "正在读取本地汇总… / Loading local summary…"
                      : "请选择营业日并刷新 / Select a business date and refresh"}
                  </Text>
                </View>
              )}

              {state.access.canSave ? (
                <View style={styles.countSection}>
                  <View style={styles.sectionHeader}>
                    <View>
                      <Text style={styles.sectionTitle}>
                        现金点算 / Cash count
                      </Text>
                      <Text style={styles.sectionHint}>
                        只接受非负整数张数；金额始终以整数分币计算。进入点钞或点按任一面额会自动打开系统数字键盘；当前输入会滚至键盘上方。
                        / Enter non-negative whole-number quantities. The
                        numeric keyboard opens automatically for counting; the
                        focused field stays visible above it.
                      </Text>
                    </View>
                    <View style={styles.countTotals}>
                      <Text style={styles.countTotalText}>
                        纸币 {money(state.notesSubtotalCents)}
                      </Text>
                      <Text style={styles.countTotalText}>
                        硬币 {money(state.coinsSubtotalCents)}
                      </Text>
                    </View>
                  </View>

                  <View style={styles.denominationGrid}>
                    {state.counts.map((count, index) => (
                      <DenominationField
                        autoFocus={
                          index === 0 &&
                          state.activePane === "count" &&
                          !state.busy
                        }
                        count={count.quantity}
                        denominationCents={count.denominationCents}
                        disabled={state.busy}
                        inputRef={index === 0 ? firstCountInputRef : undefined}
                        key={count.denominationCents}
                        onChange={(quantity) =>
                          presenter.setCount(count.denominationCents, quantity)
                        }
                        onFocus={revealSummaryInput}
                        subtotalCents={count.subtotalCents}
                      />
                    ))}
                  </View>

                  <View style={styles.saveBar}>
                    <View>
                      <Text style={styles.countedLabel}>
                        实点现金 / Counted cash
                      </Text>
                      <Text style={styles.countedAmount}>
                        {money(state.countedCashCents)}
                      </Text>
                      <Text
                        style={[
                          styles.variance,
                          state.varianceCents === 0
                            ? styles.varianceBalanced
                            : styles.varianceDifferent,
                        ]}
                      >
                        差额 / Variance {signedMoney(state.varianceCents)}
                      </Text>
                    </View>
                    <ActionButton
                      disabled={
                        state.busy || !state.summary || state.kind !== "ready"
                      }
                      label={
                        state.busy
                          ? "处理中… / Working…"
                          : "保存并打印 / Save & print"
                      }
                      onPress={() => void presenter.saveAndPrint()}
                      testID="daily-close-save"
                      wide
                    />
                  </View>
                </View>
              ) : (
                <View style={styles.permissionNote}>
                  <Text style={styles.permissionText}>
                    当前权限仅允许查看；点钞和保存已隐藏。/ View-only access:
                    counting and saving are hidden.
                  </Text>
                </View>
              )}
            </ScrollView>

            <ScrollView
              contentContainerStyle={styles.paneContent}
              style={[styles.pane, styles.historyPane]}
            >
              <Text style={styles.sectionTitle}>冻结归档 / Saved archives</Text>
              <Text style={styles.sectionHint}>
                同一营业日可保留多次归档；补打始终使用所选冻结事实。
              </Text>

              <View style={styles.historyList}>
                {state.archives.length === 0 ? (
                  <Text style={styles.historyEmpty}>
                    暂无归档 / No saved archives
                  </Text>
                ) : (
                  state.archives.map((archive) => (
                    <ArchiveRow
                      archive={archive}
                      key={archive.closeId}
                      onPress={() => presenter.selectArchive(archive.closeId)}
                      selected={
                        state.selectedArchive?.closeId === archive.closeId
                      }
                    />
                  ))
                )}
              </View>

              {state.selectedArchive ? (
                <ArchiveDetails archive={state.selectedArchive} />
              ) : null}

              {state.access.canReprint ? (
                <ActionButton
                  disabled={state.busy || !state.selectedArchive}
                  label={
                    state.busy
                      ? "处理中… / Working…"
                      : "补打所选归档 / Reprint selected"
                  }
                  onPress={() => void presenter.reprintSelected()}
                  testID="daily-close-reprint"
                  tone="secondary"
                  wide
                />
              ) : null}
            </ScrollView>
          </View>
        </View>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}

export function DailyCloseUnavailableScreen({
  onBack,
}: Readonly<{ onBack(): void }>) {
  return (
    <SafeAreaView style={styles.safeArea}>
      <View style={styles.unavailable}>
        <Text style={styles.title}>日结暂不可用 / Daily close unavailable</Text>
        <Text style={styles.subtitle}>
          本地归档或打印服务尚未接线，请返回销售页。/ Local archive or printing
          services are not configured.
        </Text>
        <ActionButton
          label="返回销售 / Back to sales"
          onPress={onBack}
          testID="daily-close-unavailable-back"
          wide
        />
      </View>
    </SafeAreaView>
  );
}

function TenderRow({
  tender,
}: Readonly<{ tender: DailyCloseTenderBreakdown }>) {
  return (
    <View style={styles.tenderRow}>
      <Text style={styles.tenderMethod}>{tenderName(tender.method)}</Text>
      <Text style={styles.tenderValues}>
        {money(tender.salesCents)} / {money(tender.refundCents)} /{" "}
        {money(tender.netCents)}
      </Text>
    </View>
  );
}

function Metric({ label, value }: Readonly<{ label: string; value: string }>) {
  return (
    <View style={styles.metric}>
      <Text style={styles.metricLabel}>{label}</Text>
      <Text style={styles.metricValue}>{value}</Text>
    </View>
  );
}

function DenominationField({
  autoFocus,
  count,
  denominationCents,
  disabled,
  inputRef,
  onChange,
  onFocus,
  subtotalCents,
}: Readonly<{
  autoFocus: boolean;
  count: number;
  denominationCents: AudCashDenominationCents;
  disabled: boolean;
  inputRef?: Ref<TextInput> | undefined;
  onChange(quantity: number): void;
  onFocus(event: FocusEvent): void;
  subtotalCents: number;
}>) {
  return (
    <View style={styles.denomination}>
      <View>
        <Text style={styles.denominationLabel}>
          {denominationLabel(denominationCents)}
        </Text>
        <Text style={styles.denominationSubtotal}>{money(subtotalCents)}</Text>
      </View>
      <TextInput
        accessibilityLabel={`${denominationLabel(denominationCents)} count`}
        autoFocus={autoFocus}
        editable={!disabled}
        keyboardType="number-pad"
        onChangeText={(value) => {
          if (value === "") {
            onChange(0);
            return;
          }
          if (!/^\d+$/.test(value)) return;
          const quantity = Number(value);
          if (Number.isSafeInteger(quantity)) onChange(quantity);
        }}
        onFocus={onFocus}
        ref={inputRef}
        selectTextOnFocus
        showSoftInputOnFocus
        style={styles.countInput}
        testID={`daily-close-count-${denominationCents}`}
        value={String(count)}
      />
    </View>
  );
}

function ArchiveRow({
  archive,
  onPress,
  selected,
}: Readonly<{
  archive: DailyCloseArchive;
  onPress(): void;
  selected: boolean;
}>) {
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityState={{ selected }}
      onPress={onPress}
      style={[styles.archiveRow, selected && styles.archiveRowSelected]}
      testID={`daily-close-history-${archive.closeId}`}
    >
      <View>
        <Text style={styles.archiveTitle}>
          {archive.businessDate} · {shortId(archive.closeId)}
        </Text>
        <Text style={styles.archiveMeta}>
          {archive.savedAtIso.replace("T", " ").slice(0, 19)} ·{" "}
          {archive.savedCashierName}
        </Text>
      </View>
      <Text style={styles.archiveAmount}>
        {money(archive.countedCashCents)}
      </Text>
    </Pressable>
  );
}

function ArchiveDetails({ archive }: Readonly<{ archive: DailyCloseArchive }>) {
  return (
    <View style={styles.archiveDetails}>
      <Text style={styles.detailTitle}>所选归档 / Selected archive</Text>
      <Text style={styles.detailLine}>
        终端 / Terminal: {archive.deviceCode}
      </Text>
      <Text style={styles.detailLine}>
        收银员 / Cashier: {archive.savedCashierName}
      </Text>
      <Text style={styles.detailLine}>
        订单 / Orders: {archive.orderCount} · 退货 / Returns:{" "}
        {archive.returnQuantity}
      </Text>
      <Text style={styles.detailLine}>
        应有 / Expected {money(archive.expectedCashCents)}
      </Text>
      <Text style={styles.detailLine}>
        实点 / Counted {money(archive.countedCashCents)}
      </Text>
      <Text
        style={[
          styles.detailVariance,
          archive.varianceCents === 0
            ? styles.varianceBalanced
            : styles.varianceDifferent,
        ]}
      >
        差额 / Variance {signedMoney(archive.varianceCents)}
      </Text>
    </View>
  );
}

function ActionButton({
  disabled = false,
  label,
  onPress,
  selected = false,
  style,
  testID,
  tone = "primary",
  wide = false,
}: Readonly<{
  disabled?: boolean;
  label: string;
  onPress(): void;
  selected?: boolean;
  style?: StyleProp<ViewStyle>;
  testID: string;
  tone?: "primary" | "secondary" | "quiet";
  wide?: boolean;
}>) {
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityState={{ disabled, selected }}
      disabled={disabled}
      onPress={onPress}
      style={[
        styles.action,
        tone === "secondary" && styles.actionSecondary,
        tone === "quiet" && styles.actionQuiet,
        selected && styles.actionSelected,
        wide && styles.actionWide,
        disabled && styles.actionDisabled,
        style,
      ]}
      testID={testID}
    >
      <Text
        style={[
          styles.actionText,
          tone !== "primary" && styles.actionTextSecondary,
          selected && styles.actionTextSelected,
        ]}
      >
        {label}
      </Text>
    </Pressable>
  );
}

function StatusBanner({
  statusCode,
}: Readonly<{ statusCode: DailyCloseStatusCode }>) {
  const success =
    statusCode === "saved-printed" || statusCode === "reprint-printed";
  return (
    <View
      accessibilityLiveRegion="polite"
      style={[
        styles.status,
        success ? styles.statusSuccess : styles.statusWarning,
      ]}
      testID={`daily-close-status-${statusCode}`}
    >
      <Text style={styles.statusText}>{statusMessage(statusCode)}</Text>
    </View>
  );
}

function statusMessage(statusCode: DailyCloseStatusCode): string {
  return {
    "invalid-business-date":
      "营业日或门店时区无效。/ Invalid business date or store time zone.",
    "load-failed":
      "本地日结汇总读取失败，请重试。/ Local daily-close summary could not be loaded.",
    "permission-required":
      "当前收银员没有执行此操作的权限。/ Permission required.",
    "reprint-failed":
      "补打失败；冻结归档未改变。/ Reprint failed; the archive is unchanged.",
    "reprint-printed": "所选归档已发送打印。/ Selected archive sent to print.",
    "save-failed":
      "归档未保存，点钞数量已保留。/ Save failed; counts were preserved.",
    "saved-print-failed":
      "归档与审计已保存，但打印失败，可从历史补打。/ Saved safely; printing failed and can be retried from history.",
    "saved-printed": "归档与审计已保存并发送打印。/ Saved and sent to print.",
    "select-archive-required": "请先选择一个归档。/ Select an archive first.",
  }[statusCode];
}

function tenderName(method: DailyCloseTenderBreakdown["method"]): string {
  return {
    cash: "Cash / 现金",
    card: "Card / 银行卡",
    voucher: "Voucher / 代金券",
  }[method];
}

function denominationLabel(value: AudCashDenominationCents): string {
  return value >= 100 ? `$${value / 100}` : `${value}c`;
}

function money(cents: number): string {
  const sign = cents < 0 ? "-" : "";
  const value = Math.abs(cents);
  return `${sign}$${Math.floor(value / 100)}.${String(value % 100).padStart(
    2,
    "0",
  )}`;
}

function signedMoney(cents: number): string {
  return cents > 0 ? `+${money(cents)}` : money(cents);
}

function shortId(closeId: string): string {
  return closeId.length <= 12 ? closeId : closeId.slice(0, 12);
}

const styles = StyleSheet.create({
  safeArea: {
    flex: 1,
    backgroundColor: posColors.canvas,
  },
  keyboardAvoider: {
    flex: 1,
  },
  page: {
    flex: 1,
    paddingHorizontal: 20,
    paddingVertical: 14,
    gap: 12,
  },
  header: {
    flexDirection: "row",
    alignItems: "flex-start",
    justifyContent: "space-between",
    gap: 18,
  },
  titleGroup: {
    flex: 1,
    maxWidth: 760,
  },
  eyebrow: {
    color: posColors.orange,
    fontSize: 12,
    fontWeight: "800",
    letterSpacing: 1.2,
  },
  title: {
    color: posColors.ink,
    fontSize: 30,
    fontWeight: "900",
    letterSpacing: -0.7,
    marginTop: 3,
  },
  subtitle: {
    color: posColors.mutedInk,
    fontSize: 13,
    lineHeight: 19,
    marginTop: 4,
  },
  headerActions: {
    flexDirection: "row",
    flexWrap: "wrap",
    justifyContent: "flex-end",
    gap: 8,
  },
  workspace: {
    flex: 1,
    flexDirection: "row",
    gap: 12,
    minHeight: 0,
  },
  pane: {
    backgroundColor: posColors.surface,
    borderColor: posColors.border,
    borderRadius: 16,
    borderWidth: 1,
  },
  summaryPane: {
    flex: 1.65,
  },
  historyPane: {
    flex: 1,
  },
  paneContent: {
    padding: 16,
    gap: 12,
  },
  sectionHeader: {
    flexDirection: "row",
    alignItems: "flex-start",
    justifyContent: "space-between",
    gap: 12,
  },
  sectionTitle: {
    color: posColors.ink,
    fontSize: 18,
    fontWeight: "800",
  },
  sectionHint: {
    color: posColors.mutedInk,
    fontSize: 12,
    lineHeight: 17,
    marginTop: 2,
  },
  dateControls: {
    flexDirection: "row",
    alignItems: "center",
    gap: 8,
  },
  dateInput: {
    minHeight: DAILY_CLOSE_MIN_TOUCH_TARGET,
    minWidth: 142,
    borderColor: posColors.border,
    borderRadius: 10,
    borderWidth: 1,
    color: posColors.ink,
    fontSize: 15,
    fontVariant: ["tabular-nums"],
    paddingHorizontal: 12,
    paddingVertical: 9,
  },
  tenderCard: {
    borderColor: posColors.border,
    borderRadius: 12,
    borderWidth: 1,
    overflow: "hidden",
  },
  tenderHeader: {
    flexDirection: "row",
    justifyContent: "space-between",
    backgroundColor: posColors.blueSoft,
    paddingHorizontal: 12,
    paddingVertical: 9,
  },
  tenderRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    borderTopColor: posColors.border,
    borderTopWidth: 1,
    paddingHorizontal: 12,
    paddingVertical: 10,
  },
  tenderMethod: {
    color: posColors.ink,
    fontSize: 13,
    fontWeight: "700",
  },
  tenderValues: {
    color: posColors.ink,
    fontSize: 13,
    fontVariant: ["tabular-nums"],
  },
  metrics: {
    flexDirection: "row",
    gap: 8,
  },
  metric: {
    flex: 1,
    backgroundColor: posColors.canvas,
    borderRadius: 10,
    padding: 11,
  },
  metricLabel: {
    color: posColors.mutedInk,
    fontSize: 11,
    fontWeight: "700",
  },
  metricValue: {
    color: posColors.ink,
    fontSize: 20,
    fontWeight: "900",
    fontVariant: ["tabular-nums"],
    marginTop: 4,
  },
  emptyCard: {
    alignItems: "center",
    borderColor: posColors.border,
    borderRadius: 12,
    borderStyle: "dashed",
    borderWidth: 1,
    padding: 24,
  },
  emptyTitle: {
    color: posColors.mutedInk,
    fontSize: 14,
    textAlign: "center",
  },
  countSection: {
    borderTopColor: posColors.border,
    borderTopWidth: 1,
    gap: 12,
    paddingTop: 14,
  },
  countTotals: {
    alignItems: "flex-end",
  },
  countTotalText: {
    color: posColors.mutedInk,
    fontSize: 12,
    fontVariant: ["tabular-nums"],
  },
  denominationGrid: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
  },
  denomination: {
    width: "18.8%",
    minWidth: 112,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    backgroundColor: posColors.canvas,
    borderRadius: 10,
    paddingLeft: 10,
  },
  denominationLabel: {
    color: posColors.ink,
    fontSize: 14,
    fontWeight: "800",
  },
  denominationSubtotal: {
    color: posColors.mutedInk,
    fontSize: 10,
    fontVariant: ["tabular-nums"],
  },
  countInput: {
    minHeight: DAILY_CLOSE_MIN_TOUCH_TARGET,
    width: 54,
    borderColor: posColors.border,
    borderLeftWidth: 1,
    color: posColors.ink,
    fontSize: 17,
    fontWeight: "800",
    fontVariant: ["tabular-nums"],
    paddingHorizontal: 8,
    textAlign: "center",
  },
  saveBar: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    backgroundColor: posColors.orangeSoft,
    borderRadius: 12,
    padding: 13,
  },
  countedLabel: {
    color: posColors.mutedInk,
    fontSize: 11,
    fontWeight: "700",
  },
  countedAmount: {
    color: posColors.ink,
    fontSize: 26,
    fontWeight: "900",
    fontVariant: ["tabular-nums"],
  },
  variance: {
    fontSize: 12,
    fontWeight: "800",
    fontVariant: ["tabular-nums"],
  },
  varianceBalanced: {
    color: posColors.green,
  },
  varianceDifferent: {
    color: posColors.red,
  },
  permissionNote: {
    backgroundColor: posColors.blueSoft,
    borderRadius: 10,
    padding: 12,
  },
  permissionText: {
    color: posColors.blue,
    fontSize: 12,
    fontWeight: "700",
  },
  historyList: {
    gap: 7,
  },
  historyEmpty: {
    color: posColors.mutedInk,
    fontSize: 13,
    paddingVertical: 20,
    textAlign: "center",
  },
  archiveRow: {
    minHeight: DAILY_CLOSE_MIN_TOUCH_TARGET,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    borderColor: posColors.border,
    borderRadius: 10,
    borderWidth: 1,
    paddingHorizontal: 11,
    paddingVertical: 9,
  },
  archiveRowSelected: {
    backgroundColor: posColors.blueSoft,
    borderColor: posColors.blue,
  },
  archiveTitle: {
    color: posColors.ink,
    fontSize: 13,
    fontWeight: "800",
  },
  archiveMeta: {
    color: posColors.mutedInk,
    fontSize: 10,
    marginTop: 2,
  },
  archiveAmount: {
    color: posColors.ink,
    fontSize: 15,
    fontWeight: "900",
    fontVariant: ["tabular-nums"],
  },
  archiveDetails: {
    backgroundColor: posColors.canvas,
    borderRadius: 12,
    gap: 5,
    padding: 12,
  },
  detailTitle: {
    color: posColors.ink,
    fontSize: 14,
    fontWeight: "800",
    marginBottom: 3,
  },
  detailLine: {
    color: posColors.mutedInk,
    fontSize: 12,
    fontVariant: ["tabular-nums"],
  },
  detailVariance: {
    fontSize: 13,
    fontWeight: "900",
    fontVariant: ["tabular-nums"],
  },
  action: {
    minHeight: DAILY_CLOSE_MIN_TOUCH_TARGET,
    minWidth: 100,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: posColors.orange,
    borderColor: posColors.orange,
    borderRadius: 10,
    borderWidth: 1,
    paddingHorizontal: 14,
    paddingVertical: 9,
  },
  actionSecondary: {
    backgroundColor: posColors.surface,
    borderColor: posColors.border,
  },
  actionQuiet: {
    backgroundColor: "transparent",
    borderColor: posColors.border,
  },
  actionSelected: {
    backgroundColor: posColors.blue,
    borderColor: posColors.blue,
  },
  actionWide: {
    minWidth: 190,
  },
  actionDisabled: {
    opacity: 0.42,
  },
  actionText: {
    color: "#FFFFFF",
    fontSize: 13,
    fontWeight: "800",
    textAlign: "center",
  },
  actionTextSecondary: {
    color: posColors.ink,
  },
  actionTextSelected: {
    color: "#FFFFFF",
  },
  status: {
    borderRadius: 9,
    paddingHorizontal: 12,
    paddingVertical: 9,
  },
  statusSuccess: {
    backgroundColor: posColors.greenSoft,
  },
  statusWarning: {
    backgroundColor: posColors.redSoft,
  },
  statusText: {
    color: posColors.ink,
    fontSize: 12,
    fontWeight: "700",
  },
  unavailable: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
    gap: 16,
    padding: 32,
  },
});
