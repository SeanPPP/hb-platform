import { useEffect, useState, useSyncExternalStore } from "react";
import { useTranslation } from "react-i18next";
import {
  ActivityIndicator,
  ScrollView,
  StyleSheet,
  Text,
  View,
} from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";

import {
  installmentText,
  resolveInstallmentLocale,
  type InstallmentLocale,
} from "./installment-copy";
import type { InstallmentPaymentMethod } from "./installment-models";
import type {
  InstallmentCreateDraft,
  InstallmentPresenter,
  InstallmentPresenterState,
  InstallmentStatusCode,
} from "./installment-presenter";

import type { InstallmentStatus, InstallmentSummary } from "@/core/contracts";
import { PosPressable } from "@/ui/controls/pos-pressable";
import { PosTextInput } from "@/ui/controls/pos-text-input";
import { posColors } from "@/ui/theme";

export const INSTALLMENTS_MIN_TOUCH_TARGET = 44;

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

export type InstallmentScreenProps = Readonly<{
  onBack?(): void;
  onStartCreate?(): void;
  onStartRepayment?(installmentGuid: string): void;
  presenter: InstallmentScreenPresenter;
}>;

type ConfirmationKind = "cancel" | "void" | "pickup";

/**
 * 高密度横屏主从工作台。历史与详情保持并排，付款与取消动作不打开新路由，
 * 让收银员始终看见当前分期号、余额和状态。
 */
export function InstallmentScreen({
  onBack,
  onStartCreate,
  onStartRepayment,
  presenter,
}: InstallmentScreenProps) {
  const state = useSyncExternalStore(
    presenter.subscribe,
    presenter.getState,
    presenter.getState,
  );
  const [confirmation, setConfirmation] = useState<ConfirmationKind | null>(
    null,
  );
  const { i18n } = useTranslation();
  const locale = resolveInstallmentLocale(
    i18n.resolvedLanguage ?? i18n.language,
  );

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
          locale={locale}
          onBack={onBack}
          pane={state.pane}
          showCreate={onStartCreate ?? presenter.showCreate}
          showHistory={presenter.showHistory}
        />

        {!state.online ? (
          <View
            accessibilityLiveRegion="polite"
            style={styles.offlineNote}
            testID="installments-offline-note"
          >
            <Text style={styles.offlineText}>
              {installmentText(locale, "offline")}
            </Text>
          </View>
        ) : null}

        {state.statusCode ? (
          <StatusBanner locale={locale} statusCode={state.statusCode} />
        ) : null}
        {state.recoveryRequired && state.online ? (
          <View style={styles.recoveryActions}>
            <ActionButton
              disabled={state.busy}
              label={installmentText(locale, "action.recover")}
              onPress={() => void presenter.recoverBlocking()}
              testID="installments-recover-blocking-action"
              tone="danger"
            />
          </View>
        ) : null}

        {state.pane === "create" ? (
          <CreateWorkspace
            locale={locale}
            presenter={presenter}
            state={state}
          />
        ) : (
          <View style={styles.workspace}>
            <HistoryPane locale={locale} presenter={presenter} state={state} />
            <DetailsPane
              confirmation={confirmation}
              locale={locale}
              onStartRepayment={onStartRepayment}
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
  locale,
  onBack,
  pane,
  showCreate,
  showHistory,
}: Readonly<{
  canCreate: boolean;
  busy: boolean;
  locale: InstallmentLocale;
  onBack: (() => void) | undefined;
  pane: "history" | "create";
  showCreate(): void;
  showHistory(): void;
}>) {
  return (
    <View style={styles.header}>
      <View style={styles.titleGroup}>
        <Text style={styles.eyebrow}>{installmentText(locale, "eyebrow")}</Text>
        <Text style={styles.title}>{installmentText(locale, "title")}</Text>
        <Text style={styles.subtitle}>
          {installmentText(locale, "subtitle")}
        </Text>
      </View>
      <View style={styles.headerActions}>
        {onBack ? (
          <ActionButton
            label={installmentText(locale, "action.back")}
            onPress={onBack}
            sound="navigate"
            testID="installments-back"
            tone="quiet"
          />
        ) : null}
        <ActionButton
          disabled={busy}
          label={installmentText(locale, "action.history")}
          onPress={showHistory}
          sound="navigate"
          selected={pane === "history"}
          testID="installments-history-tab"
          tone="secondary"
        />
        {canCreate ? (
          <ActionButton
            disabled={busy}
            label={installmentText(locale, "action.new")}
            onPress={showCreate}
            sound="navigate"
            selected={pane === "create"}
            testID="installments-create-tab"
          />
        ) : null}
      </View>
    </View>
  );
}

function HistoryPane({
  locale,
  presenter,
  state,
}: Readonly<{
  locale: InstallmentLocale;
  presenter: InstallmentScreenPresenter;
  state: InstallmentPresenterState;
}>) {
  const statuses: readonly {
    label: string;
    status: InstallmentStatus | null;
  }[] = [
    { label: installmentText(locale, "filter.all"), status: null },
    { label: installmentText(locale, "filter.active"), status: "Active" },
    { label: installmentText(locale, "filter.paid"), status: "PaidOff" },
    { label: installmentText(locale, "filter.picked"), status: "PickedUp" },
    { label: installmentText(locale, "filter.cancelled"), status: "Cancelled" },
  ];
  return (
    <View style={[styles.pane, styles.historyPane]}>
      <View style={styles.panelHeader}>
        <View>
          <Text style={styles.panelTitle}>
            {installmentText(locale, "history.title")}
          </Text>
          <Text style={styles.panelMeta}>
            {installmentText(locale, "history.count", {
              count: state.orders.length,
            })}
          </Text>
        </View>
        {state.kind === "loading" ? (
          <ActivityIndicator color={posColors.orange} />
        ) : null}
      </View>
      <View style={styles.searchRow}>
        <PosTextInput
          accessibilityLabel={installmentText(locale, "search.accessibility")}
          autoCapitalize="none"
          autoCorrect={false}
          onChangeText={presenter.setSearchQuery}
          onSubmitEditing={() => void presenter.load()}
          placeholder={installmentText(locale, "search.placeholder")}
          placeholderTextColor={posColors.mutedInk}
          style={styles.searchInput}
          testID="installments-search"
          value={state.query}
        />
        <ActionButton
          disabled={state.kind === "loading"}
          label={installmentText(locale, "action.search")}
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
                ? installmentText(locale, "history.loading")
                : installmentText(locale, "history.empty")}
            </Text>
          </View>
        ) : (
          state.orders.map((order) => (
            <OrderRow
              key={order.installmentGuid}
              locale={locale}
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
  locale,
  onPress,
  order,
  selected,
}: Readonly<{
  locale: InstallmentLocale;
  onPress(): void;
  order: InstallmentSummary;
  selected: boolean;
}>) {
  return (
    <PosPressable
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
          {displayDate(order.updatedAtIso, locale)} · {order.deviceCode}
        </Text>
      </View>
      <View style={styles.orderAmounts}>
        <StatusPill locale={locale} status={order.status} />
        <Text style={styles.balanceAmount}>{money(order.balanceCents)}</Text>
        <Text style={styles.balanceLabel}>
          {installmentText(locale, "balance.label")}
        </Text>
      </View>
    </PosPressable>
  );
}

function DetailsPane({
  confirmation,
  locale,
  onStartRepayment,
  presenter,
  setConfirmation,
  state,
}: Readonly<{
  confirmation: ConfirmationKind | null;
  locale: InstallmentLocale;
  onStartRepayment: ((installmentGuid: string) => void) | undefined;
  presenter: InstallmentScreenPresenter;
  setConfirmation(value: ConfirmationKind | null): void;
  state: InstallmentPresenterState;
}>) {
  if (state.detailsLoading) {
    return (
      <View style={[styles.pane, styles.detailsPane, styles.centered]}>
        <ActivityIndicator color={posColors.orange} size="large" />
        <Text style={styles.emptyTitle}>
          {installmentText(locale, "details.loading")}
        </Text>
      </View>
    );
  }
  if (!state.details) {
    return (
      <View style={[styles.pane, styles.detailsPane, styles.centered]}>
        <Text style={styles.emptyTitle}>
          {installmentText(locale, "details.empty")}
        </Text>
        <Text style={styles.emptyHint}>
          {installmentText(locale, "details.offlineHint")}
        </Text>
      </View>
    );
  }

  const details = state.details;
  const writeDisabled = state.busy || !state.online || state.recoveryRequired;
  return (
    <ScrollView
      contentContainerStyle={styles.detailsContent}
      style={[styles.pane, styles.detailsPane]}
      testID="installment-details"
    >
      <View style={styles.detailHeading}>
        <View>
          <Text style={styles.detailNumber}>{details.installmentNumber}</Text>
          <Text style={styles.detailCustomer}>
            {details.customerName} · {details.customerPhone ?? "—"}
          </Text>
        </View>
        <StatusPill locale={locale} status={details.status} />
      </View>

      <View style={styles.metrics}>
        <Metric
          label={installmentText(locale, "metric.total")}
          value={money(details.totalCents)}
        />
        <Metric
          label={installmentText(locale, "metric.down")}
          value={money(details.downPaymentCents)}
        />
        <Metric
          label={installmentText(locale, "metric.paid")}
          value={money(details.paidCents)}
        />
        <Metric
          emphasized
          label={installmentText(locale, "metric.balance")}
          value={money(details.balanceCents)}
        />
      </View>

      <Section title={installmentText(locale, "section.items")}>
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

      <Section title={installmentText(locale, "section.payments")}>
        {details.payments.length === 0 ? (
          <Text style={styles.emptyHint}>
            {installmentText(locale, "payments.empty")}
          </Text>
        ) : (
          details.payments.map((payment) => (
            <View key={payment.paymentGuid} style={styles.factRow}>
              <View style={styles.factGrow}>
                <Text style={styles.factPrimary}>
                  {methodLabel(payment.method, locale)} ·{" "}
                  {paymentStatusLabel(payment.status, locale)}
                </Text>
                <Text style={styles.factSecondary}>
                  {displayDate(payment.recordedAtIso, locale)}
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
        <Section title={installmentText(locale, "section.note")}>
          <Text style={styles.noteText}>{details.note}</Text>
        </Section>
      ) : null}

      {details.status === "Active" && state.access.canAddRepayment ? (
        onStartRepayment ? (
          <View style={styles.actionCard}>
            <Text style={styles.sectionTitle}>
              {installmentText(locale, "repayment.title")}
            </Text>
            <ActionButton
              disabled={writeDisabled}
              label={installmentText(locale, "action.addRepayment")}
              onPress={() =>
                onStartRepayment(details.installmentGuid)
              }
              testID="installment-continue-to-payment"
              wide
            />
          </View>
        ) : (
          <RepaymentPanel
            disabled={writeDisabled}
            locale={locale}
            presenter={presenter}
            state={state}
          />
        )
      ) : null}

      {details.status === "Active" && state.access.canCancel ? (
        <CancellationPanel
          confirmation={confirmation}
          disabled={writeDisabled}
          locale={locale}
          presenter={presenter}
          setConfirmation={setConfirmation}
          state={state}
        />
      ) : null}

      {details.status === "PaidOff" && state.access.canConfirmPickup ? (
        <PickupPanel
          confirmation={confirmation}
          disabled={writeDisabled}
          locale={locale}
          presenter={presenter}
          setConfirmation={setConfirmation}
          state={state}
        />
      ) : null}

      {details.pickupInfo ? (
        <View style={styles.completedNote}>
          <Text style={styles.completedTitle}>
            {installmentText(locale, "completed.picked")}
          </Text>
          <Text style={styles.completedText}>
            {displayDate(details.pickupInfo.pickedUpAtIso, locale)} ·{" "}
            {details.pickupInfo.pickedUpBy}
          </Text>
        </View>
      ) : null}
      {details.cancellationInfo ? (
        <View style={styles.cancelledNote}>
          <Text style={styles.completedTitle}>
            {installmentText(locale, "completed.cancelled")}
          </Text>
          <Text style={styles.completedText}>
            {cancellationLabel(details.cancellationInfo.kind, locale)} ·{" "}
            {displayDate(details.cancellationInfo.cancelledAtIso, locale)}
          </Text>
        </View>
      ) : null}
    </ScrollView>
  );
}

function RepaymentPanel({
  disabled,
  locale,
  presenter,
  state,
}: Readonly<{
  disabled: boolean;
  locale: InstallmentLocale;
  presenter: InstallmentScreenPresenter;
  state: InstallmentPresenterState;
}>) {
  return (
    <View style={styles.actionCard}>
      <Text style={styles.sectionTitle}>
        {installmentText(locale, "repayment.title")}
      </Text>
      <PosTextInput
        accessibilityLabel={installmentText(
          locale,
          "repayment.amountAccessibility",
        )}
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
        locale={locale}
        onSelect={presenter.setRepaymentMethod}
        prefix="installment-repayment-method"
        selected={state.repaymentMethod}
      />
      {state.repaymentMethod === "voucher" ? (
        <>
          <PosTextInput
            accessibilityLabel={installmentText(
              locale,
              "repayment.voucherAccessibility",
            )}
            editable={!disabled}
            onChangeText={presenter.setRepaymentVoucherReference}
            placeholder={installmentText(locale, "voucher.placeholder")}
            style={styles.textInput}
            testID="installment-repayment-voucher-reference"
            value={state.repaymentVoucherReference}
          />
          <Text
            style={styles.fieldHint}
            testID="installment-repayment-voucher-help"
          >
            {installmentText(locale, "voucher.help")}
          </Text>
        </>
      ) : null}
      <ActionButton
        disabled={disabled}
        label={
          state.busy
            ? installmentText(locale, "action.working")
            : installmentText(locale, "action.addRepayment")
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
  locale,
  presenter,
  setConfirmation,
  state,
}: Readonly<{
  confirmation: ConfirmationKind | null;
  disabled: boolean;
  locale: InstallmentLocale;
  presenter: InstallmentScreenPresenter;
  setConfirmation(value: ConfirmationKind | null): void;
  state: InstallmentPresenterState;
}>) {
  return (
    <View style={styles.dangerCard}>
      <Text style={styles.sectionTitle}>
        {installmentText(locale, "cancel.title")}
      </Text>
      <PosTextInput
        accessibilityLabel={installmentText(
          locale,
          "cancel.reasonAccessibility",
        )}
        editable={!disabled}
        onChangeText={presenter.setCancelReason}
        placeholder={installmentText(locale, "cancel.reasonPlaceholder")}
        style={styles.textInput}
        testID="installment-cancel-reason"
        value={state.cancelReason}
      />
      <View style={styles.inlineActions}>
        <ActionButton
          disabled={disabled}
          label={installmentText(locale, "action.refundCancel")}
          onPress={() => setConfirmation("cancel")}
          testID="installment-cancel-refund"
          tone="danger"
        />
        <PosTextInput
          accessibilityLabel={installmentText(
            locale,
            "void.reasonAccessibility",
          )}
          editable={!disabled}
          onChangeText={presenter.setVoidReason}
          placeholder={installmentText(locale, "void.reasonPlaceholder")}
          style={[styles.textInput, styles.growInput]}
          testID="installment-void-reason"
          value={state.voidReason}
        />
        <ActionButton
          disabled={disabled}
          label={installmentText(locale, "action.void")}
          onPress={() => setConfirmation("void")}
          testID="installment-void"
          tone="danger"
        />
      </View>
      {confirmation === "cancel" || confirmation === "void" ? (
        <ConfirmationStrip
          kind={confirmation}
          locale={locale}
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
  locale,
  presenter,
  setConfirmation,
  state,
}: Readonly<{
  confirmation: ConfirmationKind | null;
  disabled: boolean;
  locale: InstallmentLocale;
  presenter: InstallmentScreenPresenter;
  setConfirmation(value: ConfirmationKind | null): void;
  state: InstallmentPresenterState;
}>) {
  return (
    <View style={styles.actionCard}>
      <Text style={styles.sectionTitle}>
        {installmentText(locale, "pickup.title")}
      </Text>
      <PosTextInput
        accessibilityLabel={installmentText(locale, "pickup.noteAccessibility")}
        editable={!disabled}
        onChangeText={presenter.setPickupNote}
        placeholder={installmentText(locale, "pickup.notePlaceholder")}
        style={styles.textInput}
        testID="installment-pickup-note"
        value={state.pickupNote}
      />
      <ActionButton
        disabled={disabled}
        label={installmentText(locale, "action.confirmPickup")}
        onPress={() => setConfirmation("pickup")}
        testID="installment-confirm-pickup"
        wide
      />
      {confirmation === "pickup" ? (
        <ConfirmationStrip
          kind="pickup"
          locale={locale}
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
  locale,
  onCancel,
  onConfirm,
}: Readonly<{
  kind: ConfirmationKind;
  locale: InstallmentLocale;
  onCancel(): void;
  onConfirm(): void;
}>) {
  const message = installmentText(locale, `confirmation.${kind}`);
  return (
    <View
      accessibilityLiveRegion="assertive"
      style={styles.confirmation}
      testID={`installment-confirm-${kind}`}
    >
      <Text style={styles.confirmationText}>{message}</Text>
      <View style={styles.inlineActions}>
        <ActionButton
          label={installmentText(locale, "action.back")}
          onPress={onCancel}
          sound="navigate"
          testID="installment-confirm-operation-cancel"
          tone="quiet"
        />
        <ActionButton
          label={installmentText(locale, "action.confirm")}
          onPress={onConfirm}
          testID="installment-confirm-operation-submit"
          tone="danger"
        />
      </View>
    </View>
  );
}

function CreateWorkspace({
  locale,
  presenter,
  state,
}: Readonly<{
  locale: InstallmentLocale;
  presenter: InstallmentScreenPresenter;
  state: InstallmentPresenterState;
}>) {
  const draft = state.createDraft;
  const disabled =
    state.busy || !state.online || state.recoveryRequired || !draft;
  return (
    <View style={styles.workspace} testID="installment-create-workspace">
      <ScrollView
        contentContainerStyle={styles.detailsContent}
        style={[styles.pane, styles.historyPane]}
      >
        <Text style={styles.panelTitle}>
          {installmentText(locale, "cart.title")}
        </Text>
        <Text style={styles.panelMeta}>
          {installmentText(locale, "cart.hint")}
        </Text>
        {draft ? (
          <DraftSummary draft={draft} locale={locale} />
        ) : (
          <View style={styles.emptyCard}>
            <Text style={styles.emptyTitle}>
              {installmentText(locale, "cart.empty")}
            </Text>
          </View>
        )}
      </ScrollView>
      <ScrollView
        contentContainerStyle={styles.detailsContent}
        style={[styles.pane, styles.detailsPane]}
      >
        <Text style={styles.panelTitle}>
          {installmentText(locale, "create.title")}
        </Text>
        <PosTextInput
          accessibilityLabel={installmentText(
            locale,
            "create.customerNameAccessibility",
          )}
          editable={!disabled}
          onChangeText={presenter.setCustomerName}
          placeholder={installmentText(
            locale,
            "create.customerNamePlaceholder",
          )}
          style={styles.textInput}
          testID="installment-create-customer-name"
          value={state.customerName}
        />
        <PosTextInput
          accessibilityLabel={installmentText(
            locale,
            "create.customerPhoneAccessibility",
          )}
          editable={!disabled}
          keyboardType="phone-pad"
          onChangeText={presenter.setCustomerPhone}
          placeholder={installmentText(
            locale,
            "create.customerPhonePlaceholder",
          )}
          style={styles.textInput}
          testID="installment-create-customer-phone"
          value={state.customerPhone}
        />
        <PosTextInput
          accessibilityLabel={installmentText(
            locale,
            "create.noteAccessibility",
          )}
          editable={!disabled}
          multiline
          onChangeText={presenter.setCreateNote}
          placeholder={installmentText(locale, "create.notePlaceholder")}
          style={[styles.textInput, styles.multilineInput]}
          testID="installment-create-note"
          value={state.createNote}
        />
        <View style={styles.fieldGroup}>
          <Text style={styles.fieldLabel}>
            {installmentText(locale, "create.downPayment")}
          </Text>
          <PosTextInput
            accessibilityLabel={installmentText(
              locale,
              "create.downPaymentAccessibility",
            )}
            editable={!disabled}
            keyboardType="decimal-pad"
            onChangeText={presenter.setCreateDownPayment}
            placeholder="20.00"
            style={styles.textInput}
            testID="installment-create-down-payment"
            value={state.createDownPayment}
          />
          <Text style={styles.fieldHint}>
            {installmentText(locale, "create.minimums", {
              total: money(5_000),
              downPayment: money(2_000),
            })}
          </Text>
        </View>
        <PaymentMethodSelector
          disabled={disabled}
          locale={locale}
          onSelect={presenter.setCreatePaymentMethod}
          prefix="installment-create-method"
          selected={state.createPaymentMethod}
        />
        {state.createPaymentMethod === "voucher" ? (
          <>
            <PosTextInput
              accessibilityLabel={installmentText(
                locale,
                "create.voucherAccessibility",
              )}
              editable={!disabled}
              onChangeText={presenter.setCreateVoucherReference}
              placeholder={installmentText(locale, "voucher.placeholder")}
              style={styles.textInput}
              testID="installment-create-voucher-reference"
              value={state.createVoucherReference}
            />
            <Text
              style={styles.fieldHint}
              testID="installment-create-voucher-help"
            >
              {installmentText(locale, "voucher.help")}
            </Text>
          </>
        ) : null}
        <ActionButton
          disabled={disabled}
          label={
            state.busy
              ? installmentText(locale, "action.working")
              : installmentText(locale, "action.create")
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
  locale,
}: Readonly<{ draft: InstallmentCreateDraft; locale: InstallmentLocale }>) {
  return (
    <View style={styles.draftCard}>
      <View style={styles.detailHeading}>
        <Text style={styles.sectionTitle}>
          {installmentText(locale, "draft.count", {
            count: draft.lines.length,
          })}
        </Text>
        <Text style={styles.draftTotal}>{money(draft.totalCents)}</Text>
      </View>
      {draft.lines.map((line) => (
        <View key={line.lineKey} style={styles.factRow}>
          <View style={styles.factGrow}>
            <Text style={styles.factPrimary}>{line.displayName}</Text>
            <Text style={styles.factSecondary}>× {line.quantity}</Text>
          </View>
          <Text style={styles.factAmount}>{money(line.actualAmountCents)}</Text>
        </View>
      ))}
    </View>
  );
}

function PaymentMethodSelector({
  disabled,
  locale,
  onSelect,
  prefix,
  selected,
}: Readonly<{
  disabled: boolean;
  locale: InstallmentLocale;
  onSelect(method: InstallmentPaymentMethod): void;
  prefix: string;
  selected: InstallmentPaymentMethod;
}>) {
  const methods: readonly {
    method: InstallmentPaymentMethod;
    label: string;
  }[] = [
    { method: "cash", label: installmentText(locale, "method.cash") },
    { method: "card", label: installmentText(locale, "method.card") },
    { method: "voucher", label: installmentText(locale, "method.voucher") },
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
  locale,
  status,
}: Readonly<{ locale: InstallmentLocale; status: InstallmentStatus }>) {
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
      <Text style={styles.statusText}>{statusLabel(status, locale)}</Text>
    </View>
  );
}

function StatusBanner({
  locale,
  statusCode,
}: Readonly<{
  locale: InstallmentLocale;
  statusCode: InstallmentStatusCode;
}>) {
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
        statusCode === "payment-recovery-required" ? "assertive" : "polite"
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
        {statusMessage(statusCode, locale)}
      </Text>
    </View>
  );
}

export function InstallmentsUnavailableScreen({
  onBack,
}: Readonly<{ onBack(): void }>) {
  const { i18n } = useTranslation();
  const locale = resolveInstallmentLocale(
    i18n.resolvedLanguage ?? i18n.language,
  );
  return (
    <SafeAreaView
      style={styles.safeArea}
      testID="installments-runtime-unavailable"
    >
      <View style={styles.unavailable}>
        <Text style={styles.eyebrow}>{installmentText(locale, "title")}</Text>
        <Text style={styles.title}>
          {installmentText(locale, "unavailable.title")}
        </Text>
        <Text style={styles.subtitle}>
          {installmentText(locale, "unavailable.subtitle")}
        </Text>
        <ActionButton
          label={installmentText(locale, "action.backToSales")}
          onPress={onBack}
          sound="navigate"
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
  sound,
  testID,
  tone = "primary",
  wide = false,
}: Readonly<{
  compact?: boolean;
  disabled?: boolean;
  label: string;
  onPress(): void;
  selected?: boolean;
  sound?: "danger" | "navigate" | "tap";
  testID: string;
  tone?: "primary" | "secondary" | "quiet" | "danger";
  wide?: boolean;
}>) {
  return (
    <PosPressable
      accessibilityRole="button"
      accessibilityState={{ disabled, selected }}
      disabled={disabled}
      onPress={onPress}
      sound={sound ?? (tone === "danger" ? "danger" : "tap")}
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
    </PosPressable>
  );
}

function money(cents: number): string {
  return `$${(cents / 100).toFixed(2)}`;
}

function displayDate(iso: string, locale: InstallmentLocale): string {
  const parsed = new Date(iso);
  return Number.isFinite(parsed.getTime())
    ? parsed.toLocaleString(locale === "zh" ? "zh-AU" : "en-AU", {
        day: "2-digit",
        month: "2-digit",
        year: "numeric",
        hour: "2-digit",
        minute: "2-digit",
      })
    : iso;
}

function statusLabel(
  status: InstallmentStatus,
  locale: InstallmentLocale,
): string {
  return installmentText(locale, `status.${status}`);
}

function methodLabel(
  method: InstallmentPaymentMethod,
  locale: InstallmentLocale,
): string {
  return installmentText(locale, `method.${method}`);
}

function paymentStatusLabel(
  status: "Recorded" | "Voided",
  locale: InstallmentLocale,
): string {
  return installmentText(locale, `paymentStatus.${status}`);
}

function cancellationLabel(
  kind: "RefundCancel" | "VoidCancel",
  locale: InstallmentLocale,
): string {
  return installmentText(locale, `cancellation.${kind}`);
}

function statusMessage(
  code: InstallmentStatusCode,
  locale: InstallmentLocale,
): string {
  return installmentText(locale, `status.${code}`);
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
