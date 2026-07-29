import {
  useEffect,
  useState,
  useSyncExternalStore,
} from "react";
import {
  ActivityIndicator,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  View,
} from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";

import type {
  InstallmentPaymentMethod,
} from "./installment-models";
import type {
  InstallmentCreateDraft,
  InstallmentPresenter,
  InstallmentPresenterState,
  InstallmentStatusCode,
} from "./installment-presenter";

import type { InstallmentStatus, InstallmentSummary } from "@/core/contracts";
import { posColors } from "@/ui/theme";

export const INSTALLMENTS_MIN_TOUCH_TARGET = 44;
const VOUCHER_QUERY_LOCK_HELP =
  "只输入券码；提交后由在线支付服务查询并锁定。 / Enter the voucher code only; the online payment provider will query and lock it after submission.";

export type InstallmentScreenPresenter = Pick<
  InstallmentPresenter,
  | "addRepayment"
  | "cancelWithRefund"
  | "confirmPickup"
  | "create"
  | "getState"
  | "load"
  | "recoverBlocking"
  | "select"
  | "setCancelReason"
  | "setCreateDownPayment"
  | "setCreateNote"
  | "setCreatePaymentMethod"
  | "setCreateVoucherReference"
  | "setCustomerName"
  | "setCustomerPhone"
  | "setPickupNote"
  | "setRepaymentAmount"
  | "setRepaymentMethod"
  | "setRepaymentVoucherReference"
  | "setSearchQuery"
  | "setStatusFilter"
  | "setVoidReason"
  | "showCreate"
  | "showHistory"
  | "subscribe"
  | "voidSelected"
> &
  Readonly<{ getState(): InstallmentPresenterState }>;

type InstallmentScreenProps = Readonly<{
  onBack?(): void;
  presenter: InstallmentScreenPresenter;
}>;

type ConfirmationKind = "cancel" | "void" | "pickup";

/**
 * 高密度横屏主从工作台。历史与详情保持并排，付款与取消动作不打开新路由，
 * 让收银员始终看见当前分期号、余额和状态。
 */
export function InstallmentScreen({
  onBack,
  presenter,
}: InstallmentScreenProps) {
  const state = useSyncExternalStore(
    presenter.subscribe,
    presenter.getState,
    presenter.getState,
  );
  const [confirmation, setConfirmation] =
    useState<ConfirmationKind | null>(null);

  useEffect(() => {
    void presenter.load();
  }, [presenter]);

  useEffect(() => {
    setConfirmation(null);
  }, [state.selectedGuid]);

  return (
    <SafeAreaView style={styles.safeArea} testID="installments-screen">
      <View style={styles.page}>
        <Header
          canCreate={state.access.canCreate}
          busy={state.busy}
          onBack={onBack}
          pane={state.pane}
          showCreate={presenter.showCreate}
          showHistory={presenter.showHistory}
        />

        {!state.online ? (
          <View
            accessibilityLiveRegion="polite"
            style={styles.offlineNote}
            testID="installments-offline-note"
          >
            <Text style={styles.offlineText}>
              离线模式：仅可浏览本机加密缓存；创建、首付、补款、取消、作废和取货均已锁定。
              / Offline: cached viewing only; every installment write is locked.
            </Text>
          </View>
        ) : null}

        {state.statusCode ? (
          <StatusBanner statusCode={state.statusCode} />
        ) : null}
        {state.recoveryRequired && state.online ? (
          <View style={styles.recoveryActions}>
            <ActionButton
              disabled={state.busy}
              label="恢复上一笔 / Recover previous action"
              onPress={() => void presenter.recoverBlocking()}
              testID="installments-recover-blocking-action"
              tone="danger"
            />
          </View>
        ) : null}

        {state.pane === "create" ? (
          <CreateWorkspace presenter={presenter} state={state} />
        ) : (
          <View style={styles.workspace}>
            <HistoryPane presenter={presenter} state={state} />
            <DetailsPane
              confirmation={confirmation}
              presenter={presenter}
              setConfirmation={setConfirmation}
              state={state}
            />
          </View>
        )}
      </View>
    </SafeAreaView>
  );
}

function Header({
  canCreate,
  busy,
  onBack,
  pane,
  showCreate,
  showHistory,
}: Readonly<{
  canCreate: boolean;
  busy: boolean;
  onBack: (() => void) | undefined;
  pane: "history" | "create";
  showCreate(): void;
  showHistory(): void;
}>) {
  return (
    <View style={styles.header}>
      <View style={styles.titleGroup}>
        <Text style={styles.eyebrow}>门店运营 / STORE OPERATIONS</Text>
        <Text style={styles.title}>分期 / Installments</Text>
        <Text style={styles.subtitle}>
          历史可从本机缓存读取；所有创建、付款、退款和状态变更必须在线。
          / History may use the local cache; all mutations require a live backend.
        </Text>
      </View>
      <View style={styles.headerActions}>
        {onBack ? (
          <ActionButton
            label="返回 / Back"
            onPress={onBack}
            testID="installments-back"
            tone="quiet"
          />
        ) : null}
        <ActionButton
          disabled={busy}
          label="历史 / History"
          onPress={showHistory}
          selected={pane === "history"}
          testID="installments-history-tab"
          tone="secondary"
        />
        {canCreate ? (
          <ActionButton
            disabled={busy}
            label="新建 / New"
            onPress={showCreate}
            selected={pane === "create"}
            testID="installments-create-tab"
          />
        ) : null}
      </View>
    </View>
  );
}

function HistoryPane({
  presenter,
  state,
}: Readonly<{
  presenter: InstallmentScreenPresenter;
  state: InstallmentPresenterState;
}>) {
  const statuses: readonly {
    label: string;
    status: InstallmentStatus | null;
  }[] = [
    { label: "全部 / All", status: null },
    { label: "进行中 / Active", status: "Active" },
    { label: "已付清 / Paid", status: "PaidOff" },
    { label: "已取货 / Picked", status: "PickedUp" },
    { label: "已取消 / Cancelled", status: "Cancelled" },
  ];
  return (
    <View style={[styles.pane, styles.historyPane]}>
      <View style={styles.panelHeader}>
        <View>
          <Text style={styles.panelTitle}>分期历史 / History</Text>
          <Text style={styles.panelMeta}>
            {state.orders.length} 项 / items
          </Text>
        </View>
        {state.kind === "loading" ? (
          <ActivityIndicator color={posColors.orange} />
        ) : null}
      </View>
      <View style={styles.searchRow}>
        <TextInput
          accessibilityLabel="搜索分期 / Search installments"
          autoCapitalize="none"
          autoCorrect={false}
          onChangeText={presenter.setSearchQuery}
          onSubmitEditing={() => void presenter.load()}
          placeholder="分期号、客户、电话 / Number, customer, phone"
          placeholderTextColor={posColors.mutedInk}
          style={styles.searchInput}
          testID="installments-search"
          value={state.query}
        />
        <ActionButton
          disabled={state.kind === "loading"}
          label="查询 / Search"
          onPress={() => void presenter.load()}
          testID="installments-search-submit"
        />
      </View>
      <ScrollView
        horizontal
        contentContainerStyle={styles.filterRow}
        showsHorizontalScrollIndicator={false}
      >
        {statuses.map((item) => (
          <ActionButton
            compact
            key={item.label}
            label={item.label}
            onPress={() => presenter.setStatusFilter(item.status)}
            selected={state.statusFilter === item.status}
            testID={`installments-filter-${item.status ?? "all"}`}
            tone="secondary"
          />
        ))}
      </ScrollView>
      <ScrollView contentContainerStyle={styles.orderList}>
        {state.orders.length === 0 ? (
          <View style={styles.emptyCard}>
            <Text style={styles.emptyTitle}>
              {state.kind === "loading"
                ? "正在读取… / Loading…"
                : "暂无分期记录 / No installment records"}
            </Text>
          </View>
        ) : (
          state.orders.map((order) => (
            <OrderRow
              key={order.installmentGuid}
              onPress={() => void presenter.select(order.installmentGuid)}
              order={order}
              selected={state.selectedGuid === order.installmentGuid}
            />
          ))
        )}
      </ScrollView>
    </View>
  );
}

function OrderRow({
  onPress,
  order,
  selected,
}: Readonly<{
  onPress(): void;
  order: InstallmentSummary;
  selected: boolean;
}>) {
  return (
    <Pressable
      accessibilityRole="button"
      onPress={onPress}
      style={({ pressed }) => [
        styles.orderRow,
        selected && styles.orderRowSelected,
        pressed && styles.pressed,
      ]}
      testID={`installment-row-${order.installmentGuid}`}
    >
      <View style={styles.orderIdentity}>
        <Text style={styles.orderNumber}>{order.installmentNumber}</Text>
        <Text numberOfLines={1} style={styles.orderCustomer}>
          {order.customerName} · {order.customerPhone ?? "—"}
        </Text>
        <Text style={styles.orderMeta}>
          {displayDate(order.updatedAtIso)} · {order.deviceCode}
        </Text>
      </View>
      <View style={styles.orderAmounts}>
        <StatusPill status={order.status} />
        <Text style={styles.balanceAmount}>
          {money(order.balanceCents)}
        </Text>
        <Text style={styles.balanceLabel}>余额 / balance</Text>
      </View>
    </Pressable>
  );
}

function DetailsPane({
  confirmation,
  presenter,
  setConfirmation,
  state,
}: Readonly<{
  confirmation: ConfirmationKind | null;
  presenter: InstallmentScreenPresenter;
  setConfirmation(value: ConfirmationKind | null): void;
  state: InstallmentPresenterState;
}>) {
  if (state.detailsLoading) {
    return (
      <View style={[styles.pane, styles.detailsPane, styles.centered]}>
        <ActivityIndicator color={posColors.orange} size="large" />
        <Text style={styles.emptyTitle}>
          正在读取详情… / Loading details…
        </Text>
      </View>
    );
  }
  if (!state.details) {
    return (
      <View style={[styles.pane, styles.detailsPane, styles.centered]}>
        <Text style={styles.emptyTitle}>
          选择一张分期单 / Select an installment
        </Text>
        <Text style={styles.emptyHint}>
          离线时若本机没有加密详情缓存，页面不会向网络发起写操作。
        </Text>
      </View>
    );
  }

  const details = state.details;
  const writeDisabled =
    state.busy || !state.online || state.recoveryRequired;
  return (
    <ScrollView
      contentContainerStyle={styles.detailsContent}
      style={[styles.pane, styles.detailsPane]}
      testID="installment-details"
    >
      <View style={styles.detailHeading}>
        <View>
          <Text style={styles.detailNumber}>
            {details.installmentNumber}
          </Text>
          <Text style={styles.detailCustomer}>
            {details.customerName} · {details.customerPhone ?? "—"}
          </Text>
        </View>
        <StatusPill status={details.status} />
      </View>

      <View style={styles.metrics}>
        <Metric label="总额 / Total" value={money(details.totalCents)} />
        <Metric label="首付 / Down" value={money(details.downPaymentCents)} />
        <Metric label="已付 / Paid" value={money(details.paidCents)} />
        <Metric
          emphasized
          label="余额 / Balance"
          value={money(details.balanceCents)}
        />
      </View>

      <Section title="商品 / Items">
        {details.lines.map((line) => (
          <View key={line.installmentLineGuid} style={styles.factRow}>
            <View style={styles.factGrow}>
              <Text style={styles.factPrimary}>{line.displayName}</Text>
              <Text style={styles.factSecondary}>
                {line.lookupCode} · × {line.quantity}
              </Text>
            </View>
            <Text style={styles.factAmount}>
              {money(line.actualAmountCents)}
            </Text>
          </View>
        ))}
      </Section>

      <Section title="付款历史 / Payments">
        {details.payments.length === 0 ? (
          <Text style={styles.emptyHint}>
            暂无付款 / No recorded payments
          </Text>
        ) : (
          details.payments.map((payment) => (
            <View key={payment.paymentGuid} style={styles.factRow}>
              <View style={styles.factGrow}>
                <Text style={styles.factPrimary}>
                  {methodLabel(payment.method)} · {payment.status}
                </Text>
                <Text style={styles.factSecondary}>
                  {displayDate(payment.recordedAtIso)}
                  {payment.cardType ? ` · ${payment.cardType}` : ""}
                  {payment.maskedCardNumber
                    ? ` · ${payment.maskedCardNumber}`
                    : ""}
                </Text>
              </View>
              <Text style={styles.factAmount}>
                {money(payment.amountCents)}
              </Text>
            </View>
          ))
        )}
      </Section>

      {details.note ? (
        <Section title="备注 / Note">
          <Text style={styles.noteText}>{details.note}</Text>
        </Section>
      ) : null}

      {details.status === "Active" &&
      state.access.canAddRepayment ? (
        <RepaymentPanel
          disabled={writeDisabled}
          presenter={presenter}
          state={state}
        />
      ) : null}

      {details.status === "Active" && state.access.canCancel ? (
        <CancellationPanel
          confirmation={confirmation}
          disabled={writeDisabled}
          presenter={presenter}
          setConfirmation={setConfirmation}
          state={state}
        />
      ) : null}

      {details.status === "PaidOff" &&
      state.access.canConfirmPickup ? (
        <PickupPanel
          confirmation={confirmation}
          disabled={writeDisabled}
          presenter={presenter}
          setConfirmation={setConfirmation}
          state={state}
        />
      ) : null}

      {details.pickupInfo ? (
        <View style={styles.completedNote}>
          <Text style={styles.completedTitle}>
            已取货 / Picked up
          </Text>
          <Text style={styles.completedText}>
            {displayDate(details.pickupInfo.pickedUpAtIso)} ·{" "}
            {details.pickupInfo.pickedUpBy}
          </Text>
        </View>
      ) : null}
      {details.cancellationInfo ? (
        <View style={styles.cancelledNote}>
          <Text style={styles.completedTitle}>
            已取消 / Cancelled
          </Text>
          <Text style={styles.completedText}>
            {details.cancellationInfo.kind} ·{" "}
            {displayDate(details.cancellationInfo.cancelledAtIso)}
          </Text>
        </View>
      ) : null}
    </ScrollView>
  );
}

function RepaymentPanel({
  disabled,
  presenter,
  state,
}: Readonly<{
  disabled: boolean;
  presenter: InstallmentScreenPresenter;
  state: InstallmentPresenterState;
}>) {
  return (
    <View style={styles.actionCard}>
      <Text style={styles.sectionTitle}>续付 / Add repayment</Text>
      <TextInput
        accessibilityLabel="续付金额 / Repayment amount"
        editable={!disabled}
        keyboardType="decimal-pad"
        onChangeText={presenter.setRepaymentAmount}
        placeholder="0.00"
        style={styles.textInput}
        testID="installment-repayment-amount"
        value={state.repaymentAmount}
      />
      <PaymentMethodSelector
        disabled={disabled}
        onSelect={presenter.setRepaymentMethod}
        prefix="installment-repayment-method"
        selected={state.repaymentMethod}
      />
      {state.repaymentMethod === "voucher" ? (
        <>
          <TextInput
            accessibilityLabel="续付券码 / Repayment voucher code"
            editable={!disabled}
            onChangeText={presenter.setRepaymentVoucherReference}
            placeholder="券码 / Voucher code"
            style={styles.textInput}
            testID="installment-repayment-voucher-reference"
            value={state.repaymentVoucherReference}
          />
          <Text
            style={styles.fieldHint}
            testID="installment-repayment-voucher-help"
          >
            {VOUCHER_QUERY_LOCK_HELP}
          </Text>
        </>
      ) : null}
      <ActionButton
        disabled={disabled}
        label={
          state.busy
            ? "处理中… / Working…"
            : "记录续付 / Add repayment"
        }
        onPress={() => void presenter.addRepayment()}
        testID="installment-add-repayment"
        wide
      />
    </View>
  );
}

function CancellationPanel({
  confirmation,
  disabled,
  presenter,
  setConfirmation,
  state,
}: Readonly<{
  confirmation: ConfirmationKind | null;
  disabled: boolean;
  presenter: InstallmentScreenPresenter;
  setConfirmation(value: ConfirmationKind | null): void;
  state: InstallmentPresenterState;
}>) {
  return (
    <View style={styles.dangerCard}>
      <Text style={styles.sectionTitle}>
        取消分期 / Cancel installment
      </Text>
      <TextInput
        accessibilityLabel="取消退款原因 / Cancellation reason"
        editable={!disabled}
        onChangeText={presenter.setCancelReason}
        placeholder="退款取消原因 / Refund-cancel reason"
        style={styles.textInput}
        testID="installment-cancel-reason"
        value={state.cancelReason}
      />
      <View style={styles.inlineActions}>
        <ActionButton
          disabled={disabled}
          label="退款并取消 / Refund & cancel"
          onPress={() => setConfirmation("cancel")}
          testID="installment-cancel-refund"
          tone="danger"
        />
        <TextInput
          accessibilityLabel="作废原因 / Void reason"
          editable={!disabled}
          onChangeText={presenter.setVoidReason}
          placeholder="作废原因 / Void reason"
          style={[styles.textInput, styles.growInput]}
          testID="installment-void-reason"
          value={state.voidReason}
        />
        <ActionButton
          disabled={disabled}
          label="作废 / Void"
          onPress={() => setConfirmation("void")}
          testID="installment-void"
          tone="danger"
        />
      </View>
      {confirmation === "cancel" || confirmation === "void" ? (
        <ConfirmationStrip
          kind={confirmation}
          onCancel={() => setConfirmation(null)}
          onConfirm={() => {
            const action =
              confirmation === "cancel"
                ? presenter.cancelWithRefund()
                : presenter.voidSelected();
            setConfirmation(null);
            void action;
          }}
        />
      ) : null}
    </View>
  );
}

function PickupPanel({
  confirmation,
  disabled,
  presenter,
  setConfirmation,
  state,
}: Readonly<{
  confirmation: ConfirmationKind | null;
  disabled: boolean;
  presenter: InstallmentScreenPresenter;
  setConfirmation(value: ConfirmationKind | null): void;
  state: InstallmentPresenterState;
}>) {
  return (
    <View style={styles.actionCard}>
      <Text style={styles.sectionTitle}>
        取货确认 / Confirm pickup
      </Text>
      <TextInput
        accessibilityLabel="取货备注 / Pickup note"
        editable={!disabled}
        onChangeText={presenter.setPickupNote}
        placeholder="证件核对或备注 / ID check or note"
        style={styles.textInput}
        testID="installment-pickup-note"
        value={state.pickupNote}
      />
      <ActionButton
        disabled={disabled}
        label="确认已取货 / Confirm pickup"
        onPress={() => setConfirmation("pickup")}
        testID="installment-confirm-pickup"
        wide
      />
      {confirmation === "pickup" ? (
        <ConfirmationStrip
          kind="pickup"
          onCancel={() => setConfirmation(null)}
          onConfirm={() => {
            setConfirmation(null);
            void presenter.confirmPickup();
          }}
        />
      ) : null}
    </View>
  );
}

function ConfirmationStrip({
  kind,
  onCancel,
  onConfirm,
}: Readonly<{
  kind: ConfirmationKind;
  onCancel(): void;
  onConfirm(): void;
}>) {
  const message =
    kind === "cancel"
      ? "将按原付款记录执行退款，并在结果明确后取消分期。"
      : kind === "void"
        ? "作废不会退款；仅用于无需退款的错误分期单。"
        : "确认后分期单会标记为已取货。";
  return (
    <View
      accessibilityLiveRegion="assertive"
      style={styles.confirmation}
      testID={`installment-confirm-${kind}`}
    >
      <Text style={styles.confirmationText}>
        {message} / Confirm this operation?
      </Text>
      <View style={styles.inlineActions}>
        <ActionButton
          label="返回 / Back"
          onPress={onCancel}
          testID="installment-confirm-operation-cancel"
          tone="quiet"
        />
        <ActionButton
          label="确认 / Confirm"
          onPress={onConfirm}
          testID="installment-confirm-operation-submit"
          tone="danger"
        />
      </View>
    </View>
  );
}

function CreateWorkspace({
  presenter,
  state,
}: Readonly<{
  presenter: InstallmentScreenPresenter;
  state: InstallmentPresenterState;
}>) {
  const draft = state.createDraft;
  const disabled =
    state.busy ||
    !state.online ||
    state.recoveryRequired ||
    !draft;
  return (
    <View style={styles.workspace} testID="installment-create-workspace">
      <ScrollView
        contentContainerStyle={styles.detailsContent}
        style={[styles.pane, styles.historyPane]}
      >
        <Text style={styles.panelTitle}>当前订单 / Current cart</Text>
        <Text style={styles.panelMeta}>
          创建时组合根会再次核对购物车 revision，页面快照不能越权替代真实购物车。
        </Text>
        {draft ? (
          <DraftSummary draft={draft} />
        ) : (
          <View style={styles.emptyCard}>
            <Text style={styles.emptyTitle}>
              当前购物车为空 / Current cart is empty
            </Text>
          </View>
        )}
      </ScrollView>
      <ScrollView
        contentContainerStyle={styles.detailsContent}
        style={[styles.pane, styles.detailsPane]}
      >
        <Text style={styles.panelTitle}>
          客户与首付 / Customer & down payment
        </Text>
        <TextInput
          accessibilityLabel="客户姓名 / Customer name"
          editable={!disabled}
          onChangeText={presenter.setCustomerName}
          placeholder="客户姓名 / Customer name"
          style={styles.textInput}
          testID="installment-create-customer-name"
          value={state.customerName}
        />
        <TextInput
          accessibilityLabel="客户电话 / Customer phone"
          editable={!disabled}
          keyboardType="phone-pad"
          onChangeText={presenter.setCustomerPhone}
          placeholder="客户电话 / Customer phone"
          style={styles.textInput}
          testID="installment-create-customer-phone"
          value={state.customerPhone}
        />
        <TextInput
          accessibilityLabel="分期备注 / Installment note"
          editable={!disabled}
          multiline
          onChangeText={presenter.setCreateNote}
          placeholder="备注 / Note"
          style={[styles.textInput, styles.multilineInput]}
          testID="installment-create-note"
          value={state.createNote}
        />
        <View style={styles.fieldGroup}>
          <Text style={styles.fieldLabel}>
            首付金额 / Down payment
          </Text>
          <TextInput
            accessibilityLabel="首付金额 / Down payment amount"
            editable={!disabled}
            keyboardType="decimal-pad"
            onChangeText={presenter.setCreateDownPayment}
            placeholder="20.00"
            style={styles.textInput}
            testID="installment-create-down-payment"
            value={state.createDownPayment}
          />
          <Text style={styles.fieldHint}>
            分期总额最低 {money(5_000)}；首付最低 {money(2_000)}。
          </Text>
        </View>
        <PaymentMethodSelector
          disabled={disabled}
          onSelect={presenter.setCreatePaymentMethod}
          prefix="installment-create-method"
          selected={state.createPaymentMethod}
        />
        {state.createPaymentMethod === "voucher" ? (
          <>
            <TextInput
              accessibilityLabel="首付券码 / Down payment voucher code"
              editable={!disabled}
              onChangeText={presenter.setCreateVoucherReference}
              placeholder="券码 / Voucher code"
              style={styles.textInput}
              testID="installment-create-voucher-reference"
              value={state.createVoucherReference}
            />
            <Text
              style={styles.fieldHint}
              testID="installment-create-voucher-help"
            >
              {VOUCHER_QUERY_LOCK_HELP}
            </Text>
          </>
        ) : null}
        <ActionButton
          disabled={disabled}
          label={
            state.busy
              ? "处理中… / Working…"
              : "创建分期并收取首付 / Create & take down payment"
          }
          onPress={() => void presenter.create()}
          testID="installment-create-submit"
          wide
        />
      </ScrollView>
    </View>
  );
}

function DraftSummary({
  draft,
}: Readonly<{ draft: InstallmentCreateDraft }>) {
  return (
    <View style={styles.draftCard}>
      <View style={styles.detailHeading}>
        <Text style={styles.sectionTitle}>
          {draft.lines.length} 项 / items
        </Text>
        <Text style={styles.draftTotal}>{money(draft.totalCents)}</Text>
      </View>
      {draft.lines.map((line) => (
        <View key={line.lineKey} style={styles.factRow}>
          <View style={styles.factGrow}>
            <Text style={styles.factPrimary}>{line.displayName}</Text>
            <Text style={styles.factSecondary}>× {line.quantity}</Text>
          </View>
          <Text style={styles.factAmount}>
            {money(line.actualAmountCents)}
          </Text>
        </View>
      ))}
    </View>
  );
}

function PaymentMethodSelector({
  disabled,
  onSelect,
  prefix,
  selected,
}: Readonly<{
  disabled: boolean;
  onSelect(method: InstallmentPaymentMethod): void;
  prefix: string;
  selected: InstallmentPaymentMethod;
}>) {
  const methods: readonly {
    method: InstallmentPaymentMethod;
    label: string;
  }[] = [
    { method: "cash", label: "现金 / Cash" },
    { method: "card", label: "银行卡 / Card" },
    { method: "voucher", label: "券 / Voucher" },
  ];
  return (
    <View style={styles.methodRow}>
      {methods.map((item) => (
        <ActionButton
          compact
          disabled={disabled}
          key={item.method}
          label={item.label}
          onPress={() => onSelect(item.method)}
          selected={selected === item.method}
          testID={`${prefix}-${item.method}`}
          tone="secondary"
        />
      ))}
    </View>
  );
}

function Section({
  children,
  title,
}: Readonly<{ children: React.ReactNode; title: string }>) {
  return (
    <View style={styles.section}>
      <Text style={styles.sectionTitle}>{title}</Text>
      {children}
    </View>
  );
}

function Metric({
  emphasized = false,
  label,
  value,
}: Readonly<{
  emphasized?: boolean;
  label: string;
  value: string;
}>) {
  return (
    <View style={[styles.metric, emphasized && styles.metricEmphasized]}>
      <Text style={styles.metricLabel}>{label}</Text>
      <Text style={styles.metricValue}>{value}</Text>
    </View>
  );
}

function StatusPill({
  status,
}: Readonly<{ status: InstallmentStatus }>) {
  return (
    <View
      style={[
        styles.statusPill,
        status === "Active"
          ? styles.statusActive
          : status === "PaidOff"
            ? styles.statusPaid
            : status === "PickedUp"
              ? styles.statusPicked
              : styles.statusCancelled,
      ]}
    >
      <Text style={styles.statusText}>{statusLabel(status)}</Text>
    </View>
  );
}

function StatusBanner({
  statusCode,
}: Readonly<{ statusCode: InstallmentStatusCode }>) {
  const danger = [
    "action-failed",
    "authorization-declined",
    "conflict",
    "details-failed",
    "history-failed",
    "invalid-create",
    "invalid-repayment",
    "online-required",
    "payment-recovery-required",
    "permission-required",
  ].includes(statusCode);
  return (
    <View
      accessibilityLiveRegion={
        statusCode === "payment-recovery-required"
          ? "assertive"
          : "polite"
      }
      style={[
        styles.statusBanner,
        danger ? styles.statusBannerDanger : styles.statusBannerSuccess,
      ]}
      testID={
        statusCode === "payment-recovery-required"
          ? "installments-payment-recovery-required"
          : `installments-status-${statusCode}`
      }
    >
      <Text style={styles.statusBannerText}>
        {statusMessage(statusCode)}
      </Text>
    </View>
  );
}

export function InstallmentsUnavailableScreen({
  onBack,
}: Readonly<{ onBack(): void }>) {
  return (
    <SafeAreaView
      style={styles.safeArea}
      testID="installments-runtime-unavailable"
    >
      <View style={styles.unavailable}>
        <Text style={styles.eyebrow}>INSTALLMENTS</Text>
        <Text style={styles.title}>
          分期功能暂不可用 / Installments unavailable
        </Text>
        <Text style={styles.subtitle}>
          在线支付恢复、加密缓存或可信收银员运行时尚未接线，请返回销售页。
          / The trusted payment and cache runtime is not configured.
        </Text>
        <ActionButton
          label="返回销售 / Back to sales"
          onPress={onBack}
          testID="installments-unavailable-back"
          wide
        />
      </View>
    </SafeAreaView>
  );
}

function ActionButton({
  compact = false,
  disabled = false,
  label,
  onPress,
  selected = false,
  testID,
  tone = "primary",
  wide = false,
}: Readonly<{
  compact?: boolean;
  disabled?: boolean;
  label: string;
  onPress(): void;
  selected?: boolean;
  testID: string;
  tone?: "primary" | "secondary" | "quiet" | "danger";
  wide?: boolean;
}>) {
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityState={{ disabled, selected }}
      disabled={disabled}
      onPress={onPress}
      style={({ pressed }) => [
        styles.button,
        compact && styles.buttonCompact,
        wide && styles.buttonWide,
        tone === "secondary" && styles.buttonSecondary,
        tone === "quiet" && styles.buttonQuiet,
        tone === "danger" && styles.buttonDanger,
        selected && styles.buttonSelected,
        disabled && styles.disabled,
        pressed && !disabled && styles.pressed,
      ]}
      testID={testID}
    >
      <Text
        style={[
          styles.buttonText,
          (tone === "secondary" || tone === "quiet") &&
            !selected &&
            styles.buttonTextDark,
        ]}
      >
        {label}
      </Text>
    </Pressable>
  );
}

function money(cents: number): string {
  return `$${(cents / 100).toFixed(2)}`;
}

function displayDate(iso: string): string {
  const parsed = new Date(iso);
  return Number.isFinite(parsed.getTime())
    ? parsed.toLocaleString("en-AU", {
        day: "2-digit",
        month: "2-digit",
        year: "numeric",
        hour: "2-digit",
        minute: "2-digit",
      })
    : iso;
}

function statusLabel(status: InstallmentStatus): string {
  if (status === "Active") return "进行中 / Active";
  if (status === "PaidOff") return "已付清 / Paid off";
  if (status === "PickedUp") return "已取货 / Picked up";
  return "已取消 / Cancelled";
}

function methodLabel(method: InstallmentPaymentMethod): string {
  if (method === "cash") return "现金 / Cash";
  if (method === "card") return "银行卡 / Card";
  return "券 / Voucher";
}

function statusMessage(code: InstallmentStatusCode): string {
  const messages: Record<InstallmentStatusCode, string> = {
    "action-failed": "操作失败，请核对状态后重试。/ Operation failed.",
    "authorization-declined":
      "支付未获批准，分期状态未改变。/ Payment was declined; no installment change was made.",
    "cancel-complete": "分期已退款取消。/ Installment refunded and cancelled.",
    conflict: "服务端状态已变化，请刷新后处理。/ Server state changed; refresh first.",
    "create-complete": "分期已创建并记录首付。/ Installment and down payment recorded.",
    "details-failed": "详情读取失败。/ Unable to load details.",
    "details-unavailable":
      "本机没有可用详情缓存。/ No cached details are available.",
    "history-failed": "分期历史读取失败。/ Unable to load history.",
    "invalid-create":
      "请核对购物车、客户、最低 AUD 50 总额及最低 AUD 20 首付。/ Check cart, customer and minimum amounts.",
    "invalid-repayment":
      "续付金额或付款信息无效。/ Repayment amount or tender is invalid.",
    "online-required":
      "此操作必须在线完成。/ This operation requires a live backend.",
    "payment-recovery-required":
      "支付结果未知：恢复完成前禁止再次扣款、退款、作废或切换付款方式。/ Payment outcome unknown; recovery is required before any new action.",
    "permission-required":
      "当前收银员没有所需权限。/ Cashier permission is required.",
    "pickup-complete": "取货已确认。/ Pickup confirmed.",
    "repayment-complete": "续付已记录。/ Repayment recorded.",
    "recovery-complete":
      "上一笔支付与分期状态已恢复。/ Previous payment and installment action recovered.",
    "void-complete": "分期已作废。/ Installment voided.",
  };
  return messages[code];
}

const styles = StyleSheet.create({
  safeArea: {
    flex: 1,
    backgroundColor: posColors.canvas,
  },
  page: {
    flex: 1,
    paddingHorizontal: 20,
    paddingBottom: 16,
    gap: 12,
  },
  recoveryActions: {
    alignItems: "flex-end",
    marginBottom: 8,
  },
  header: {
    minHeight: 112,
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    gap: 20,
  },
  titleGroup: {
    flex: 1,
    maxWidth: 720,
  },
  eyebrow: {
    color: posColors.orange,
    fontSize: 12,
    fontWeight: "800",
    letterSpacing: 1.4,
  },
  title: {
    color: posColors.ink,
    fontSize: 31,
    fontWeight: "800",
    marginTop: 3,
  },
  subtitle: {
    color: posColors.mutedInk,
    fontSize: 13,
    lineHeight: 19,
    marginTop: 5,
  },
  headerActions: {
    flexDirection: "row",
    alignItems: "center",
    gap: 8,
  },
  offlineNote: {
    backgroundColor: posColors.blueSoft,
    borderColor: posColors.blue,
    borderLeftWidth: 4,
    paddingHorizontal: 14,
    paddingVertical: 9,
  },
  offlineText: {
    color: posColors.ink,
    fontSize: 13,
    fontWeight: "600",
  },
  statusBanner: {
    borderLeftWidth: 4,
    paddingHorizontal: 14,
    paddingVertical: 9,
  },
  statusBannerDanger: {
    backgroundColor: posColors.redSoft,
    borderLeftColor: posColors.red,
  },
  statusBannerSuccess: {
    backgroundColor: posColors.greenSoft,
    borderLeftColor: posColors.green,
  },
  statusBannerText: {
    color: posColors.ink,
    fontSize: 13,
    fontWeight: "700",
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
    borderWidth: 1,
  },
  historyPane: {
    flex: 0.42,
    padding: 14,
  },
  detailsPane: {
    flex: 0.58,
  },
  panelHeader: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    marginBottom: 10,
  },
  panelTitle: {
    color: posColors.ink,
    fontSize: 20,
    fontWeight: "800",
  },
  panelMeta: {
    color: posColors.mutedInk,
    fontSize: 12,
    marginTop: 3,
  },
  searchRow: {
    flexDirection: "row",
    gap: 8,
    alignItems: "center",
  },
  searchInput: {
    flex: 1,
    minHeight: INSTALLMENTS_MIN_TOUCH_TARGET,
    borderColor: posColors.border,
    borderWidth: 1,
    color: posColors.ink,
    backgroundColor: posColors.canvas,
    paddingHorizontal: 12,
    fontSize: 14,
  },
  filterRow: {
    gap: 6,
    paddingVertical: 10,
  },
  orderList: {
    gap: 8,
    paddingBottom: 12,
  },
  orderRow: {
    minHeight: 86,
    borderColor: posColors.border,
    borderWidth: 1,
    padding: 11,
    flexDirection: "row",
    justifyContent: "space-between",
    gap: 10,
    backgroundColor: posColors.surface,
  },
  orderRowSelected: {
    backgroundColor: posColors.orangeSoft,
    borderColor: posColors.orange,
  },
  orderIdentity: {
    flex: 1,
  },
  orderNumber: {
    color: posColors.ink,
    fontSize: 16,
    fontWeight: "800",
  },
  orderCustomer: {
    color: posColors.ink,
    fontSize: 13,
    marginTop: 4,
  },
  orderMeta: {
    color: posColors.mutedInk,
    fontSize: 11,
    marginTop: 5,
  },
  orderAmounts: {
    alignItems: "flex-end",
    minWidth: 115,
  },
  balanceAmount: {
    color: posColors.ink,
    fontSize: 17,
    fontWeight: "800",
    marginTop: 6,
  },
  balanceLabel: {
    color: posColors.mutedInk,
    fontSize: 10,
  },
  detailsContent: {
    padding: 16,
    gap: 14,
  },
  centered: {
    alignItems: "center",
    justifyContent: "center",
    padding: 24,
    gap: 12,
  },
  emptyCard: {
    borderColor: posColors.border,
    borderWidth: 1,
    backgroundColor: posColors.canvas,
    padding: 18,
  },
  emptyTitle: {
    color: posColors.ink,
    fontSize: 16,
    fontWeight: "700",
    textAlign: "center",
  },
  emptyHint: {
    color: posColors.mutedInk,
    fontSize: 12,
    lineHeight: 18,
    textAlign: "center",
  },
  detailHeading: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "flex-start",
    gap: 12,
  },
  detailNumber: {
    color: posColors.ink,
    fontSize: 24,
    fontWeight: "800",
  },
  detailCustomer: {
    color: posColors.mutedInk,
    fontSize: 14,
    marginTop: 4,
  },
  metrics: {
    flexDirection: "row",
    gap: 8,
  },
  metric: {
    flex: 1,
    borderColor: posColors.border,
    borderWidth: 1,
    padding: 10,
    backgroundColor: posColors.canvas,
  },
  metricEmphasized: {
    backgroundColor: posColors.orangeSoft,
    borderColor: posColors.orange,
  },
  metricLabel: {
    color: posColors.mutedInk,
    fontSize: 11,
  },
  metricValue: {
    color: posColors.ink,
    fontSize: 17,
    fontWeight: "800",
    marginTop: 4,
  },
  section: {
    borderTopColor: posColors.border,
    borderTopWidth: 1,
    paddingTop: 12,
    gap: 7,
  },
  sectionTitle: {
    color: posColors.ink,
    fontSize: 16,
    fontWeight: "800",
  },
  factRow: {
    minHeight: INSTALLMENTS_MIN_TOUCH_TARGET,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 12,
    borderBottomColor: posColors.border,
    borderBottomWidth: StyleSheet.hairlineWidth,
    paddingVertical: 6,
  },
  factGrow: {
    flex: 1,
  },
  factPrimary: {
    color: posColors.ink,
    fontSize: 13,
    fontWeight: "700",
  },
  factSecondary: {
    color: posColors.mutedInk,
    fontSize: 11,
    marginTop: 2,
  },
  factAmount: {
    color: posColors.ink,
    fontSize: 14,
    fontWeight: "800",
  },
  noteText: {
    color: posColors.ink,
    fontSize: 13,
    lineHeight: 19,
  },
  actionCard: {
    backgroundColor: posColors.blueSoft,
    borderColor: posColors.blue,
    borderWidth: 1,
    padding: 12,
    gap: 9,
  },
  dangerCard: {
    backgroundColor: posColors.redSoft,
    borderColor: posColors.red,
    borderWidth: 1,
    padding: 12,
    gap: 9,
  },
  textInput: {
    minHeight: INSTALLMENTS_MIN_TOUCH_TARGET,
    borderColor: posColors.border,
    borderWidth: 1,
    color: posColors.ink,
    backgroundColor: posColors.surface,
    paddingHorizontal: 12,
    fontSize: 14,
  },
  multilineInput: {
    minHeight: 76,
    paddingTop: 10,
    textAlignVertical: "top",
  },
  growInput: {
    flex: 1,
  },
  methodRow: {
    flexDirection: "row",
    gap: 7,
  },
  inlineActions: {
    flexDirection: "row",
    alignItems: "center",
    gap: 8,
  },
  confirmation: {
    borderTopColor: posColors.red,
    borderTopWidth: 1,
    paddingTop: 10,
    gap: 8,
  },
  confirmationText: {
    color: posColors.ink,
    fontSize: 12,
    fontWeight: "700",
    lineHeight: 18,
  },
  completedNote: {
    backgroundColor: posColors.greenSoft,
    borderLeftColor: posColors.green,
    borderLeftWidth: 4,
    padding: 12,
  },
  cancelledNote: {
    backgroundColor: posColors.redSoft,
    borderLeftColor: posColors.red,
    borderLeftWidth: 4,
    padding: 12,
  },
  completedTitle: {
    color: posColors.ink,
    fontSize: 14,
    fontWeight: "800",
  },
  completedText: {
    color: posColors.mutedInk,
    fontSize: 12,
    marginTop: 3,
  },
  draftCard: {
    marginTop: 12,
    borderColor: posColors.border,
    borderWidth: 1,
    padding: 12,
    gap: 8,
  },
  draftTotal: {
    color: posColors.orange,
    fontSize: 24,
    fontWeight: "800",
  },
  fieldGroup: {
    gap: 5,
  },
  fieldLabel: {
    color: posColors.ink,
    fontSize: 13,
    fontWeight: "700",
  },
  fieldHint: {
    color: posColors.mutedInk,
    fontSize: 11,
  },
  statusPill: {
    paddingHorizontal: 8,
    paddingVertical: 4,
    borderWidth: 1,
  },
  statusActive: {
    backgroundColor: posColors.blueSoft,
    borderColor: posColors.blue,
  },
  statusPaid: {
    backgroundColor: posColors.orangeSoft,
    borderColor: posColors.orange,
  },
  statusPicked: {
    backgroundColor: posColors.greenSoft,
    borderColor: posColors.green,
  },
  statusCancelled: {
    backgroundColor: posColors.redSoft,
    borderColor: posColors.red,
  },
  statusText: {
    color: posColors.ink,
    fontSize: 10,
    fontWeight: "800",
  },
  button: {
    minHeight: INSTALLMENTS_MIN_TOUCH_TARGET,
    minWidth: INSTALLMENTS_MIN_TOUCH_TARGET,
    paddingHorizontal: 14,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: posColors.orange,
    borderColor: posColors.orange,
    borderWidth: 1,
  },
  buttonCompact: {
    paddingHorizontal: 10,
  },
  buttonWide: {
    width: "100%",
  },
  buttonSecondary: {
    backgroundColor: posColors.surface,
    borderColor: posColors.border,
  },
  buttonQuiet: {
    backgroundColor: "transparent",
    borderColor: posColors.border,
  },
  buttonDanger: {
    backgroundColor: posColors.red,
    borderColor: posColors.red,
  },
  buttonSelected: {
    backgroundColor: posColors.blue,
    borderColor: posColors.blue,
  },
  buttonText: {
    color: "#FFFFFF",
    fontSize: 12,
    fontWeight: "800",
    textAlign: "center",
  },
  buttonTextDark: {
    color: posColors.ink,
  },
  disabled: {
    opacity: 0.42,
  },
  pressed: {
    opacity: 0.72,
  },
  unavailable: {
    flex: 1,
    maxWidth: 620,
    alignSelf: "center",
    justifyContent: "center",
    padding: 28,
    gap: 14,
  },
});
