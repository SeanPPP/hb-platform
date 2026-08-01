import { useEffect, useState, useSyncExternalStore } from "react";
import { useTranslation } from "react-i18next";
import {
  ActivityIndicator,
  Modal,
  ScrollView,
  StyleSheet,
  Text,
  View,
  useWindowDimensions,
  type StyleProp,
  type ViewStyle,
} from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";

import {
  linklyKeyCopyKey,
  paymentFieldIssueCopyKey,
  paymentMethodCopyKey,
  paymentPhaseHintKey,
  paymentPhaseTitleKey,
  paymentRuntimeErrorCopyKey,
  paymentText,
  resolvePaymentLocale,
  type PaymentCopyKey,
  type PaymentLocale,
} from "./payment-copy";
import {
  LINKLY_SAFE_OPERATOR_KEYS,
  canSelectPaymentMethod,
  canSubmitPaymentMethod,
  type PaymentPresenterState,
  type PaymentPresenterTender,
  type PaymentScreenPresenter,
  type PaymentUiMethod,
  type PaymentUiPhase,
} from "./payment-presenter";

import type {
  LinklySafeOperatorKey,
} from "@/features/payments/runtime/linkly-operator-runtime";
import { PosPressable } from "@/ui/controls/pos-pressable";
import { PosTextInput } from "@/ui/controls/pos-text-input";
import { PosStatusStrip } from "@/ui/shell/status-strip";
import { posColors } from "@/ui/theme";

export const PAYMENT_MIN_TOUCH_TARGET = 44;

export type PaymentInstallmentModeIssue = "unavailable";

export type PaymentInstallmentModeControl = Readonly<{
  enabled: boolean;
  locked: boolean;
  issue: PaymentInstallmentModeIssue | null;
  onToggle(enabled: boolean): void;
}>;

const PAYMENT_METHODS = Object.freeze([
  "cash",
  "square",
  "linkly-cloud",
  "voucher",
] as const satisfies readonly PaymentUiMethod[]);

type PaymentScreenProps = Readonly<{
  installmentModeControl?: PaymentInstallmentModeControl;
  presenter: PaymentScreenPresenter;
  locale?: PaymentLocale;
  onBack?(): void;
  onComplete?(orderGuid: string): void;
  showStatusStrip?: boolean;
}>;

type Translate = (
  key: PaymentCopyKey,
  values?: Readonly<Record<string, string | number>>,
) => string;

export function PaymentScreen({
  installmentModeControl,
  presenter,
  locale: localeOverride,
  onBack,
  onComplete,
  showStatusStrip = true,
}: PaymentScreenProps) {
  const state = useSyncExternalStore(
    presenter.subscribe,
    presenter.getState,
    presenter.getState,
  );
  const { i18n } = useTranslation();
  const locale =
    localeOverride ??
    resolvePaymentLocale(i18n.resolvedLanguage ?? i18n.language);
  const t: Translate = (key, values) => paymentText(locale, key, values);
  const { height, width } = useWindowDimensions();
  const compact = width < 900;
  const shortLandscape = !compact && height < 900;
  const [
    fullInstallmentConfirmationOpen,
    setFullInstallmentConfirmationOpen,
  ] = useState(false);

  useEffect(() => {
    void presenter.initialize();
    return () => presenter.destroy();
  }, [presenter]);

  useEffect(() => {
    setFullInstallmentConfirmationOpen(false);
  }, [
    presenter,
    state.busy,
    state.checkout.fullInstallmentConfirmationRequired,
  ]);

  const canLeave = canSafelyLeave(state);
  const showEntry =
    state.phase !== "loading" &&
    state.phase !== "success" &&
    state.phase !== "submitting" &&
    (state.allowedActions.start || state.allowedActions.addCash);

  return (
    <SafeAreaView style={styles.safeArea} testID="payment-screen">
      <View style={styles.header}>
        <View style={styles.headerIdentity}>
          <Text style={styles.title}>{t("title")}</Text>
          <Text style={styles.subtitle}>{t("subtitle")}</Text>
        </View>
        <View style={styles.headerActions}>
          {state.orderGuid ? (
            <Text numberOfLines={1} style={styles.orderReference}>
              {t("summary.order", {
                order: shortIdentifier(state.orderGuid),
              })}
            </Text>
          ) : null}
        </View>
      </View>

      {showStatusStrip ? <PosStatusStrip /> : null}

      <ScrollView
        contentContainerStyle={styles.scrollContent}
        keyboardShouldPersistTaps="handled"
      >
        <PaymentStatusPanel state={state} t={t} />

        {state.runtimeErrorCode ? (
          <View
            accessibilityRole="alert"
            style={styles.errorBanner}
            testID="payment-runtime-error"
          >
            <Text style={styles.errorText}>
              {t(paymentRuntimeErrorCopyKey(state.runtimeErrorCode))}
            </Text>
            {!state.busy ? (
              <PosPressable
                accessibilityRole="button"
                onPress={() => presenter.dismissError()}
                style={({ pressed }) => [
                  styles.errorDismiss,
                  pressed && styles.pressed,
                ]}
                testID="payment-error-dismiss"
              >
                <Text style={styles.errorDismissText}>
                  {t("action.dismiss")}
                </Text>
              </PosPressable>
            ) : null}
          </View>
        ) : null}

        <View
          style={[
            styles.workspace,
            compact && styles.workspaceCompact,
          ]}
        >
          <PaymentContextPane
            canLeave={canLeave}
            compact={compact}
            installmentModeControl={installmentModeControl}
            locale={locale}
            onBack={onBack}
            presenter={presenter}
            state={state}
            t={t}
          />
          <View
            style={[
              styles.entryPane,
              shortLandscape && styles.entryPaneShort,
              compact && styles.entryPaneCompact,
            ]}
            testID="payment-entry-pane"
          >
            {showEntry ? (
              <View
                style={[
                  styles.form,
                  shortLandscape && styles.formShort,
                ]}
                testID="payment-entry-form"
              >
                <Text style={styles.inputLabel}>{t("amount.label")}</Text>
                <PosTextInput
                  accessibilityLabel={t("amount.label")}
                  editable={!state.busy}
                  keyboardType="decimal-pad"
                  onChangeText={(value) => presenter.setAmountText(value)}
                  placeholder="0.00"
                  placeholderTextColor="#7B8793"
                  selectionColor={posColors.blue}
                  showSoftInputOnFocus={false}
                  style={[
                    styles.amountInput,
                    shortLandscape && styles.amountInputShort,
                  ]}
                  testID="payment-amount"
                  value={state.amountText}
                />
                <Text
                  style={[
                    styles.inputHint,
                    shortLandscape && styles.inputHintShort,
                  ]}
                >
                  {t("amount.hint")}
                </Text>
                <PaymentKeypad
                  amountText={state.amountText}
                  dense={shortLandscape}
                  disabled={state.busy}
                  onChange={(value) => presenter.setAmountText(value)}
                />

                {state.selectedMethod === "cash" ? (
                  <View
                    style={[
                      styles.quickCashRow,
                      shortLandscape && styles.quickCashRowShort,
                    ]}
                    testID="payment-cash-quick"
                  >
                    {(
                      [
                        [5, styles.quickCashNote5],
                        [10, styles.quickCashNote10],
                        [20, styles.quickCashNote20],
                        [50, styles.quickCashNote50],
                        [100, styles.quickCashNote100],
                      ] as const
                    ).map(([amount, noteStyle]) => (
                      <ActionButton
                        key={amount}
                        label={`$${amount}`}
                        onPress={() =>
                          presenter.setAmountText(amount.toFixed(2))
                        }
                        style={[
                          styles.quickCashButton,
                          noteStyle,
                        ]}
                        testID={`payment-cash-quick-${amount}`}
                        tone="quiet"
                      />
                    ))}
                  </View>
                ) : null}

                {state.selectedMethod === "voucher" ? (
                  <View
                    key={state.sensitiveInputRevision}
                    style={styles.voucherInputGroup}
                  >
                    <Text style={styles.inputLabel}>
                      {t("voucher.label")}
                    </Text>
                    <PosTextInput
                      accessibilityLabel={t("voucher.label")}
                      autoCapitalize="characters"
                      autoCorrect={false}
                      editable={!state.busy}
                      onChangeText={(value) =>
                        presenter.setVoucherCode(value)
                      }
                      placeholder={t("voucher.placeholder")}
                      placeholderTextColor="#7B8793"
                      secureTextEntry
                      selectionColor={posColors.blue}
                      style={styles.voucherInput}
                      testID="payment-voucher-code"
                    />
                    {state.voucherCaptured ? (
                      <Text
                        style={styles.secureCapture}
                        testID="payment-voucher-captured"
                      >
                        {t("voucher.captured")}
                      </Text>
                    ) : null}
                  </View>
                ) : null}

                {state.fieldIssue ? (
                  <Text
                    accessibilityRole="alert"
                    style={styles.fieldError}
                    testID="payment-field-error"
                  >
                    {t(paymentFieldIssueCopyKey(state.fieldIssue))}
                  </Text>
                ) : null}

                <View
                  style={[
                    styles.formActions,
                    shortLandscape && styles.formActionsShort,
                  ]}
                >
                  <ActionButton
                    disabled={state.busy}
                    label={t("action.cancel")}
                    onPress={() => {
                      if (state.allowedActions.cancel) {
                        void presenter.cancel();
                      } else if (onBack && canLeave) {
                        onBack();
                      } else {
                        presenter.setAmountText("");
                      }
                    }}
                    style={styles.formAction}
                    testID="payment-entry-cancel"
                    tone="quiet"
                  />
                  <ActionButton
                    disabled={
                      !state.selectedMethod ||
                      !canSubmitPaymentMethod(
                        state,
                        state.selectedMethod,
                      )
                    }
                    label={
                      state.orderGuid
                        ? t("action.addTender")
                        : t("action.pay")
                    }
                    onPress={() => {
                      void presenter.submitSelected();
                    }}
                    style={styles.formAction}
                    testID="payment-submit"
                  />
                </View>
              </View>
            ) : null}
          </View>

          <PaymentSummary
            compact={compact}
            locale={locale}
            onConfirm={() => {
              const customer =
                state.checkout.installmentCustomer;
              const customerComplete =
                customer !== null &&
                customer.name.trim().length > 0 &&
                customer.phone.trim().length > 0;
              if (
                state.checkout.fullInstallmentConfirmationRequired &&
                customerComplete
              ) {
                setFullInstallmentConfirmationOpen(true);
                return;
              }
              void presenter.confirm?.();
            }}
            presenter={presenter}
            state={state}
            t={t}
          />
        </View>

        {state.phase === "success" && state.orderGuid && onComplete ? (
          <ActionButton
            label={t("action.newSale")}
            onPress={() => onComplete(state.orderGuid!)}
            style={styles.completeAction}
            testID="payment-complete"
          />
        ) : null}
      </ScrollView>

      <Modal
        animationType="fade"
        onRequestClose={() =>
          setFullInstallmentConfirmationOpen(false)
        }
        transparent
        visible={fullInstallmentConfirmationOpen}
      >
        <View
          accessibilityViewIsModal
          style={styles.confirmationBackdrop}
          testID="payment-full-installment-confirmation"
        >
          <View style={styles.confirmationCard}>
            <Text style={styles.confirmationTitle}>
              {t("installment.fullPayment.title")}
            </Text>
            <Text style={styles.confirmationBody}>
              {t("installment.fullPayment.body")}
            </Text>
            <View style={styles.confirmationActions}>
              <ActionButton
                label={t("installment.fullPayment.cancel")}
                onPress={() =>
                  setFullInstallmentConfirmationOpen(false)
                }
                style={styles.confirmationAction}
                testID="payment-full-installment-cancel"
                tone="quiet"
              />
              <ActionButton
                label={t("installment.fullPayment.confirm")}
                onPress={() => {
                  setFullInstallmentConfirmationOpen(false);
                  void presenter.confirm?.({
                    acknowledgeFullInstallmentPayment: true,
                  });
                }}
                style={styles.confirmationAction}
                testID="payment-full-installment-confirm"
              />
            </View>
          </View>
        </View>
      </Modal>
    </SafeAreaView>
  );
}

function PaymentContextPane({
  canLeave,
  compact,
  installmentModeControl,
  locale,
  onBack,
  presenter,
  state,
  t,
}: Readonly<{
  canLeave: boolean;
  compact: boolean;
  installmentModeControl: PaymentInstallmentModeControl | undefined;
  locale: PaymentLocale;
  onBack: (() => void) | undefined;
  presenter: PaymentScreenPresenter;
  state: PaymentPresenterState;
  t: Translate;
}>) {
  const customer = state.checkout.installmentCustomer;
  const flowLabel =
    state.checkout.flow === "installment-create"
      ? locale === "zh"
        ? "新建分期"
        : "New installment"
      : state.checkout.flow === "installment-repayment"
        ? locale === "zh"
          ? "分期还款"
          : "Installment repayment"
        : state.checkout.flow === "installment-recovery"
          ? locale === "zh"
            ? "恢复分期支付"
            : "Recover installment"
          : locale === "zh"
            ? "当前交易"
            : "Current transaction";
  return (
    <View
      style={[
        styles.contextPane,
        compact && styles.contextPaneCompact,
      ]}
      testID="payment-context-pane"
    >
      <View style={styles.contextControls}>
        {onBack ? (
          <ActionButton
            disabled={!canLeave}
            label={locale === "zh" ? "返回收银" : "Back to sale"}
            onPress={onBack}
            style={styles.contextBack}
            testID="payment-back"
            tone="quiet"
          />
        ) : null}
        {installmentModeControl ? (
          <PaymentInstallmentToggle
            control={installmentModeControl}
            t={t}
          />
        ) : null}
      </View>
      <Text style={styles.contextEyebrow}>{flowLabel}</Text>

      {customer ? (
        <View style={styles.customerCard} testID="payment-installment-customer">
          <View style={styles.customerHeading}>
            <Text style={styles.customerTitle}>
              {locale === "zh" ? "分期顾客" : "Installment customer"}
            </Text>
            {customer.editable &&
            presenter.openInstallmentCustomerEditor ? (
              <PosPressable
                accessibilityRole="button"
                disabled={state.busy}
                onPress={() =>
                  presenter.openInstallmentCustomerEditor?.()
                }
                style={({ pressed }) => [
                  styles.customerEdit,
                  pressed && styles.pressed,
                ]}
                testID="payment-customer-edit"
              >
                <Text style={styles.customerEditText}>
                  {locale === "zh" ? "编辑" : "Edit"}
                </Text>
              </PosPressable>
            ) : null}
          </View>
          {customer.installmentNumber ? (
            <Text style={styles.customerNumber}>
              {customer.installmentNumber}
            </Text>
          ) : null}
          <Text style={styles.customerValue}>
            {customer.name || (locale === "zh" ? "未填写姓名" : "Name required")}
          </Text>
          <Text style={styles.customerValue}>
            {customer.phone || (locale === "zh" ? "未填写电话" : "Phone required")}
          </Text>
          {customer.editorOpen ? (
            <View style={styles.customerEditor} testID="payment-customer-editor">
              <PosTextInput
                autoCorrect={false}
                editable={!state.busy}
                onChangeText={(value) =>
                  presenter.setInstallmentCustomerDraftName?.(value)
                }
                placeholder={locale === "zh" ? "顾客姓名" : "Customer name"}
                style={styles.customerInput}
                testID="payment-customer-name"
                value={customer.draftName}
              />
              <PosTextInput
                autoCorrect={false}
                editable={!state.busy}
                keyboardType="phone-pad"
                onChangeText={(value) =>
                  presenter.setInstallmentCustomerDraftPhone?.(value)
                }
                placeholder={locale === "zh" ? "联系电话" : "Phone"}
                style={styles.customerInput}
                testID="payment-customer-phone"
                value={customer.draftPhone}
              />
              <View style={styles.customerEditorActions}>
                <ActionButton
                  label={locale === "zh" ? "取消" : "Cancel"}
                  onPress={() =>
                    presenter.cancelInstallmentCustomerEditor?.()
                  }
                  style={styles.customerEditorButton}
                  testID="payment-customer-cancel"
                  tone="quiet"
                />
                <ActionButton
                  label={locale === "zh" ? "保存" : "Save"}
                  onPress={() => presenter.saveInstallmentCustomer?.()}
                  style={styles.customerEditorButton}
                  testID="payment-customer-save"
                />
              </View>
            </View>
          ) : null}
        </View>
      ) : null}

      <Text style={styles.contextListTitle}>
        {locale === "zh" ? "商品明细" : "Items"}
      </Text>
      <ScrollView
        nestedScrollEnabled
        style={styles.contextLines}
        testID="payment-context-lines"
      >
        {state.checkout.lines.length ? (
          state.checkout.lines.map((line) => (
            <View key={line.lineKey} style={styles.contextLine}>
              <View style={styles.contextLineCopy}>
                <Text numberOfLines={2} style={styles.contextLineName}>
                  {line.displayName}
                </Text>
                <Text style={styles.contextLineQuantity}>× {line.quantity}</Text>
              </View>
              <Text style={styles.contextLineAmount}>
                {formatAud(line.actualAmountCents, locale)}
              </Text>
            </View>
          ))
        ) : (
          <Text style={styles.contextEmpty}>
            {locale === "zh" ? "交易商品由收银页确认" : "Items confirmed at checkout"}
          </Text>
        )}
      </ScrollView>
    </View>
  );
}

function PaymentStatusPanel({
  state,
  t,
}: Readonly<{
  state: PaymentPresenterState;
  t: Translate;
}>) {
  const tone = statusTone(state.phase);
  return (
    <View
      accessibilityRole="summary"
      style={[
        styles.statusPanel,
        tone === "warning" && styles.statusWarning,
        tone === "danger" && styles.statusDanger,
        tone === "success" && styles.statusSuccess,
      ]}
      testID={`payment-status-${state.phase}`}
    >
      <View style={styles.statusCopy}>
        <Text style={styles.statusTitle}>
          {t(paymentPhaseTitleKey(state.phase))}
        </Text>
        <Text style={styles.statusHint}>
          {t(paymentPhaseHintKey(state.phase))}
        </Text>
      </View>
      {state.busy ? (
        <ActivityIndicator
          color={tone === "danger" ? posColors.red : posColors.blue}
          size="small"
          testID="payment-busy"
        />
      ) : null}
    </View>
  );
}

function ProviderBlockers({
  state,
  t,
}: Readonly<{
  state: PaymentPresenterState;
  t: Translate;
}>) {
  const blockers = state.providers.filter(
    (provider) => !provider.available && provider.blocker,
  );
  if (!blockers.length) return null;
  return (
    <View style={styles.providerBlockers} testID="payment-provider-blockers">
      {blockers.map((provider) => (
        <Text key={provider.provider} style={styles.providerBlockerText}>
          {t(paymentMethodCopyKey(provider.provider))}:{" "}
          {t(paymentRuntimeErrorCopyKey(provider.blocker!))}
        </Text>
      ))}
    </View>
  );
}

function RecoveryActions({
  presenter,
  state,
  t,
}: Readonly<{
  presenter: PaymentScreenPresenter;
  state: PaymentPresenterState;
  t: Translate;
}>) {
  if (!state.allowedActions.recover && !state.allowedActions.cancel) {
    return null;
  }
  return (
    <View style={styles.recoveryActions} testID="payment-recovery-actions">
      {state.allowedActions.recover ? (
        <ActionButton
          disabled={state.busy}
          label={t("action.recover")}
          onPress={() => {
            void presenter.recover();
          }}
          testID="payment-recover"
          tone="primary"
        />
      ) : null}
      {state.allowedActions.cancel ? (
        <ActionButton
          disabled={state.busy}
          label={t("action.cancel")}
          onPress={() => {
            void presenter.cancel();
          }}
          testID="payment-cancel"
          tone="danger"
        />
      ) : null}
    </View>
  );
}

function LinklyControls({
  presenter,
  state,
  t,
}: Readonly<{
  presenter: PaymentScreenPresenter;
  state: PaymentPresenterState;
  t: Translate;
}>) {
  if (state.provider !== "linkly-cloud" || !state.attemptId) {
    return null;
  }
  const allowed = new Set(state.linkly.allowedKeys);
  const showSafeKeys =
    state.linkly.status === "in-progress" &&
    state.phase !== "unknown" &&
    state.phase !== "recovery-required";
  return (
    <View style={styles.linklyPanel} testID="payment-linkly-controls">
      <Text style={styles.sectionTitle}>{t("terminal.title")}</Text>
      <Text style={styles.inputHint}>{t("terminal.safeOnly")}</Text>
      {showSafeKeys ? (
        <View style={styles.linklyKeyGrid}>
          {LINKLY_SAFE_OPERATOR_KEYS.map((key) => (
            <ActionButton
              disabled={state.busy || !allowed.has(key)}
              key={key}
              label={t(linklyKeyCopyKey(key))}
              onPress={() => {
                void presenter.sendLinklyKey(key);
              }}
              testID={`payment-linkly-${key}`}
              tone="secondary"
            />
          ))}
        </View>
      ) : null}
      {state.linkly.status === "completed" ? (
        <View style={styles.linklyConfirmation}>
          <ActionButton
            disabled={state.busy}
            label={t("action.linklyPrinted")}
            onPress={() => {
              void presenter.markLinklyReceiptPrinted();
            }}
            testID="payment-linkly-receipt-printed"
            tone="secondary"
          />
          <ActionButton
            disabled={state.busy}
            label={t("action.linklyAcknowledge")}
            onPress={() => {
              void presenter.acknowledgeLinkly();
            }}
            testID="payment-linkly-acknowledge"
            tone="secondary"
          />
        </View>
      ) : null}
    </View>
  );
}

function PaymentSummary({
  compact,
  locale,
  onConfirm,
  presenter,
  state,
  t,
}: Readonly<{
  compact: boolean;
  locale: PaymentLocale;
  onConfirm(): void;
  presenter: PaymentScreenPresenter;
  state: PaymentPresenterState;
  t: Translate;
}>) {
  const paidCents = Math.max(
    0,
    state.total.cents - state.remaining.cents,
  );
  return (
    <View
      style={[
        styles.summaryPane,
        compact && styles.summaryPaneCompact,
      ]}
      testID="payment-summary"
    >
      <Text style={styles.sectionTitle}>{t("method.title")}</Text>
      <View style={styles.methodGrid}>
        {PAYMENT_METHODS.map((method) => (
          <PaymentMethodButton
            active={state.selectedMethod === method}
            disabled={!canSelectPaymentMethod(state, method)}
            key={method}
            label={t(paymentMethodCopyKey(method))}
            onPress={() => presenter.selectMethod(method)}
            testID={`payment-method-${method}`}
          />
        ))}
      </View>
      <ProviderBlockers state={state} t={t} />
      <View style={styles.summarySectionRule} />
      <Text style={styles.sectionTitle}>{t("summary.title")}</Text>
      <SummaryAmount
        label={t("summary.total")}
        locale={locale}
        value={state.total.cents}
      />
      <SummaryAmount
        label={t("summary.paid")}
        locale={locale}
        value={paidCents}
      />
      <View style={styles.remainingRule} />
      <SummaryAmount
        emphasis
        label={t("summary.remaining")}
        locale={locale}
        testID="payment-remaining"
        value={state.remaining.cents}
      />
      {state.checkout.cash.tenderedCents > 0 ? (
        <View style={styles.cashSettlement} testID="payment-cash-settlement">
          <SummaryAmount
            label={locale === "zh" ? "实收现金" : "Cash tendered"}
            locale={locale}
            value={state.checkout.cash.tenderedCents}
          />
          <SummaryAmount
            label={locale === "zh" ? "入账金额" : "Applied"}
            locale={locale}
            testID="payment-cash-applied"
            value={state.checkout.cash.appliedCents}
          />
          <SummaryAmount
            emphasis
            label={locale === "zh" ? "找零" : "Change"}
            locale={locale}
            testID="payment-change"
            value={state.checkout.cash.changeCents}
          />
        </View>
      ) : null}

      <Text style={styles.tenderTitle}>{t("summary.tenders")}</Text>
      {state.tenders.length ? (
        state.tenders.map((tender) => (
          <TenderRow
            key={tender.tenderGuid}
            locale={locale}
            onRemove={() => {
              void presenter.removeTender(tender.tenderGuid);
            }}
            state={state}
            tender={tender}
            t={t}
          />
        ))
      ) : (
        <Text style={styles.emptyTenders}>{t("summary.noTenders")}</Text>
      )}
      {presenter.confirm && state.checkout.canConfirm ? (
        <ActionButton
          disabled={state.busy}
          label={locale === "zh" ? "确认分期付款" : "Confirm installment payment"}
          onPress={onConfirm}
          style={styles.confirmAction}
          testID="payment-confirm"
        />
      ) : null}
      <RecoveryActions presenter={presenter} state={state} t={t} />
      <LinklyControls presenter={presenter} state={state} t={t} />
    </View>
  );
}

function PaymentInstallmentToggle({
  control,
  t,
}: Readonly<{
  control: PaymentInstallmentModeControl;
  t: Translate;
}>) {
  return (
    <View style={styles.installmentToggleGroup}>
      <PosPressable
        accessibilityLabel={t("installment.toggle")}
        accessibilityRole="switch"
        accessibilityState={{
          checked: control.enabled,
          disabled: control.locked,
        }}
        disabled={control.locked}
        onPress={() => control.onToggle(!control.enabled)}
        style={({ pressed }) => [
          styles.installmentToggle,
          control.locked && styles.disabled,
          pressed && !control.locked && styles.pressed,
        ]}
        testID="payment-installment-toggle"
      >
        <Text style={styles.installmentToggleLabel}>
          {t("installment.toggle")}
        </Text>
        <View
          style={[
            styles.installmentToggleTrack,
            control.enabled && styles.installmentToggleTrackEnabled,
          ]}
        >
          <View
            style={[
              styles.installmentToggleThumb,
              control.enabled && styles.installmentToggleThumbEnabled,
            ]}
          />
        </View>
      </PosPressable>
      {control.issue ? (
        <Text
          accessibilityRole="alert"
          style={styles.installmentToggleIssue}
        >
          {t("installment.unavailable")}
        </Text>
      ) : null}
    </View>
  );
}

function PaymentKeypad({
  amountText,
  dense,
  disabled,
  onChange,
}: Readonly<{
  amountText: string;
  dense: boolean;
  disabled: boolean;
  onChange(value: string): void;
}>) {
  const keys = [
    "1",
    "2",
    "3",
    "4",
    "5",
    "6",
    "7",
    "8",
    "9",
    ".",
    "0",
    "backspace",
  ] as const;
  return (
    <View
      style={[styles.keypad, dense && styles.keypadShort]}
      testID="payment-keypad"
    >
      {keys.map((key) => (
        <PosPressable
          accessibilityRole="button"
          disabled={disabled}
          key={key}
          sound="key"
          onPress={() =>
            onChange(nextKeypadAmount(amountText, key))
          }
          style={({ pressed }) => [
            styles.keypadKey,
            dense && styles.keypadKeyShort,
            disabled && styles.disabled,
            pressed && !disabled && styles.pressed,
          ]}
          testID={`payment-key-${
            key === "." ? "decimal" : key
          }`}
        >
          <Text style={styles.keypadKeyText}>
            {key === "backspace" ? "⌫" : key}
          </Text>
        </PosPressable>
      ))}
    </View>
  );
}

function TenderRow({
  locale,
  onRemove,
  state,
  tender,
  t,
}: Readonly<{
  locale: PaymentLocale;
  onRemove(): void;
  state: PaymentPresenterState;
  tender: PaymentPresenterTender;
  t: Translate;
}>) {
  return (
    <View
      style={styles.tenderRow}
      testID={`payment-tender-${tender.tenderGuid}`}
    >
      <View style={styles.tenderIdentity}>
        <Text style={styles.tenderMethod}>
          {t(
            tender.method === "card"
              ? "method.card"
              : paymentMethodCopyKey(tender.method),
          )}
        </Text>
        <Text style={styles.tenderDisposition}>
          {t(
            tender.reversible
              ? "summary.reversible"
              : "summary.locked",
          )}
        </Text>
      </View>
      <Text style={styles.tenderAmount}>
        {formatAud(tender.amount.cents, locale)}
      </Text>
      {tender.reversible ? (
        <ActionButton
          disabled={!state.allowedActions.removeTender || state.busy}
          label={t("action.remove")}
          onPress={onRemove}
          testID={`payment-remove-${tender.tenderGuid}`}
          tone="quiet"
        />
      ) : null}
    </View>
  );
}

function SummaryAmount({
  emphasis = false,
  label,
  locale,
  testID,
  value,
}: Readonly<{
  emphasis?: boolean;
  label: string;
  locale: PaymentLocale;
  testID?: string;
  value: number;
}>) {
  return (
    <View style={styles.summaryAmountRow}>
      <Text style={styles.summaryLabel}>{label}</Text>
      <Text
        style={[
          styles.summaryAmount,
          emphasis && styles.summaryAmountEmphasis,
        ]}
        testID={testID}
      >
        {formatAud(value, locale)}
      </Text>
    </View>
  );
}

function PaymentMethodButton({
  active,
  disabled,
  label,
  onPress,
  testID,
}: Readonly<{
  active: boolean;
  disabled: boolean;
  label: string;
  onPress(): void;
  testID: string;
}>) {
  return (
    <PosPressable
      accessibilityRole="button"
      accessibilityState={{ disabled, selected: active }}
      disabled={disabled}
      onPress={onPress}
      style={({ pressed }) => [
        styles.methodButton,
        active && styles.methodButtonActive,
        disabled && styles.disabled,
        pressed && !disabled && styles.pressed,
      ]}
      testID={testID}
    >
      <Text
        style={[
          styles.methodButtonText,
          active && styles.methodButtonTextActive,
        ]}
      >
        {label}
      </Text>
    </PosPressable>
  );
}

function ActionButton({
  disabled = false,
  label,
  onPress,
  style,
  testID,
  tone = "primary",
}: Readonly<{
  disabled?: boolean;
  label: string;
  onPress(): void;
  style?: StyleProp<ViewStyle>;
  testID?: string;
  tone?: "primary" | "secondary" | "danger" | "quiet";
}>) {
  return (
    <PosPressable
      accessibilityRole="button"
      accessibilityState={{ disabled }}
      disabled={disabled}
      onPress={onPress}
      sound={tone === "danger" ? "danger" : "tap"}
      style={({ pressed }) => [
        styles.actionButton,
        tone === "secondary" && styles.actionSecondary,
        tone === "danger" && styles.actionDanger,
        tone === "quiet" && styles.actionQuiet,
        disabled && styles.disabled,
        pressed && !disabled && styles.pressed,
        style,
      ]}
      testID={testID}
    >
      <Text
        style={[
          styles.actionButtonText,
          tone === "secondary" && styles.actionSecondaryText,
          tone === "quiet" && styles.actionQuietText,
        ]}
      >
        {label}
      </Text>
    </PosPressable>
  );
}

function statusTone(
  phase: PaymentUiPhase,
): "neutral" | "warning" | "danger" | "success" {
  if (phase === "success" || phase === "partial") return "success";
  if (
    phase === "unknown" ||
    phase === "recovery-required" ||
    phase === "offline-cash" ||
    phase === "draft-prepared"
  ) {
    return "warning";
  }
  if (phase === "declined" || phase === "cancelled") return "danger";
  return "neutral";
}

function canSafelyLeave(state: PaymentPresenterState): boolean {
  if (
    !state.initialized ||
    state.busy ||
    state.attemptId !== null ||
    state.allowedActions.recover
  ) {
    return false;
  }
  if (!state.orderGuid) return true;
  return (
    state.phase === "cancelled" ||
    state.phase === "declined" ||
    state.phase === "success"
  );
}

function shortIdentifier(value: string): string {
  return value.length > 12 ? `${value.slice(0, 12)}…` : value;
}

export function formatAud(cents: number, locale: PaymentLocale): string {
  return new Intl.NumberFormat(locale === "zh" ? "zh-AU" : "en-AU", {
    style: "currency",
    currency: "AUD",
    minimumFractionDigits: 2,
  }).format(cents / 100);
}

export function isSafeLinklyUiKey(
  key: string,
): key is LinklySafeOperatorKey {
  return LINKLY_SAFE_OPERATOR_KEYS.includes(
    key as LinklySafeOperatorKey,
  );
}

export function nextKeypadAmount(
  current: string,
  key: "0" | "1" | "2" | "3" | "4" | "5" | "6" | "7" | "8" | "9" | "." | "backspace",
): string {
  if (key === "backspace") return current.slice(0, -1);
  if (key === ".") {
    if (current.includes(".")) return current;
    return current ? `${current}.` : "0.";
  }
  const base = /^\d+\.\d{2}$/u.test(current) ? "" : current;
  const candidate = base === "0" ? key : `${base}${key}`;
  if (!/^\d{0,9}(?:\.\d{0,2})?$/u.test(candidate)) return current;
  return candidate;
}

const styles = StyleSheet.create({
  safeArea: {
    flex: 1,
    backgroundColor: posColors.canvas,
  },
  header: {
    minHeight: 74,
    paddingHorizontal: 24,
    paddingVertical: 12,
    backgroundColor: posColors.ink,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 18,
  },
  headerIdentity: {
    flexShrink: 1,
  },
  title: {
    color: "#FFFFFF",
    fontSize: 25,
    fontWeight: "800",
    letterSpacing: -0.4,
  },
  subtitle: {
    marginTop: 2,
    color: "#C8D4DF",
    fontSize: 13,
    fontWeight: "600",
  },
  headerActions: {
    flexDirection: "row",
    alignItems: "center",
    gap: 12,
  },
  orderReference: {
    maxWidth: 230,
    color: "#D8E2EA",
    fontSize: 13,
    fontVariant: ["tabular-nums"],
  },
  scrollContent: {
    flexGrow: 1,
    padding: 18,
    gap: 14,
  },
  statusPanel: {
    minHeight: 70,
    paddingHorizontal: 18,
    paddingVertical: 13,
    borderLeftWidth: 5,
    borderLeftColor: posColors.blue,
    backgroundColor: posColors.blueSoft,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 16,
  },
  statusWarning: {
    borderLeftColor: "#B7791F",
    backgroundColor: "#FFF4D6",
  },
  statusDanger: {
    borderLeftColor: posColors.red,
    backgroundColor: posColors.redSoft,
  },
  statusSuccess: {
    borderLeftColor: posColors.green,
    backgroundColor: posColors.greenSoft,
  },
  statusCopy: {
    flex: 1,
  },
  statusTitle: {
    color: posColors.ink,
    fontSize: 18,
    fontWeight: "800",
  },
  statusHint: {
    marginTop: 4,
    color: posColors.mutedInk,
    fontSize: 14,
    lineHeight: 19,
  },
  errorBanner: {
    paddingHorizontal: 16,
    paddingVertical: 10,
    minHeight: PAYMENT_MIN_TOUCH_TARGET,
    borderWidth: 1,
    borderColor: "#DCA09C",
    backgroundColor: posColors.redSoft,
    flexDirection: "row",
    alignItems: "center",
    gap: 12,
  },
  errorText: {
    flex: 1,
    color: "#7A211D",
    fontSize: 14,
    fontWeight: "700",
  },
  errorDismiss: {
    minHeight: PAYMENT_MIN_TOUCH_TARGET,
    minWidth: PAYMENT_MIN_TOUCH_TARGET,
    paddingHorizontal: 12,
    alignItems: "center",
    justifyContent: "center",
  },
  errorDismissText: {
    color: posColors.red,
    fontSize: 14,
    fontWeight: "800",
  },
  workspace: {
    flex: 1,
    minHeight: 480,
    flexDirection: "row",
    backgroundColor: posColors.surface,
    borderWidth: 1,
    borderColor: posColors.border,
  },
  contextPane: {
    flex: 30,
    minWidth: 250,
    padding: 16,
    borderRightWidth: 1,
    borderRightColor: posColors.border,
    backgroundColor: "#FBFAF7",
  },
  contextPaneCompact: {
    minWidth: 0,
    maxHeight: 420,
    borderRightWidth: 0,
    borderBottomWidth: 1,
    borderBottomColor: posColors.border,
  },
  contextControls: {
    marginBottom: 14,
    gap: 8,
  },
  contextBack: {
    alignSelf: "flex-start",
    marginTop: 0,
    marginBottom: 0,
  },
  installmentToggleGroup: {
    gap: 5,
  },
  installmentToggle: {
    minHeight: PAYMENT_MIN_TOUCH_TARGET,
    paddingHorizontal: 11,
    borderWidth: 1,
    borderColor: posColors.border,
    backgroundColor: "#FFFFFF",
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 10,
  },
  installmentToggleLabel: {
    color: posColors.ink,
    fontSize: 14,
    fontWeight: "800",
  },
  installmentToggleTrack: {
    width: 42,
    height: 24,
    padding: 2,
    borderRadius: 12,
    backgroundColor: "#AAB4BE",
    justifyContent: "center",
  },
  installmentToggleTrackEnabled: {
    backgroundColor: posColors.blue,
  },
  installmentToggleThumb: {
    width: 20,
    height: 20,
    borderRadius: 10,
    backgroundColor: "#FFFFFF",
  },
  installmentToggleThumbEnabled: {
    alignSelf: "flex-end",
  },
  installmentToggleIssue: {
    color: posColors.red,
    fontSize: 12,
    fontWeight: "700",
    lineHeight: 17,
  },
  contextEyebrow: {
    color: posColors.orange,
    fontSize: 13,
    fontWeight: "900",
    letterSpacing: 0.7,
    textTransform: "uppercase",
  },
  customerCard: {
    marginTop: 14,
    padding: 12,
    borderWidth: 1,
    borderColor: posColors.border,
    backgroundColor: "#FFFFFF",
  },
  customerHeading: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 8,
  },
  customerTitle: {
    color: posColors.ink,
    fontSize: 15,
    fontWeight: "900",
  },
  customerEdit: {
    minHeight: PAYMENT_MIN_TOUCH_TARGET,
    minWidth: PAYMENT_MIN_TOUCH_TARGET,
    paddingHorizontal: 8,
    alignItems: "center",
    justifyContent: "center",
  },
  customerEditText: {
    color: posColors.blue,
    fontSize: 14,
    fontWeight: "800",
  },
  customerNumber: {
    marginTop: 7,
    color: posColors.orange,
    fontSize: 12,
    fontWeight: "800",
  },
  customerValue: {
    marginTop: 5,
    color: posColors.ink,
    fontSize: 14,
    fontWeight: "700",
  },
  customerEditor: {
    marginTop: 12,
    gap: 8,
  },
  customerInput: {
    minHeight: 48,
    paddingHorizontal: 10,
    borderWidth: 1,
    borderColor: posColors.border,
    backgroundColor: "#FFFFFF",
    color: posColors.ink,
    fontSize: 15,
  },
  customerEditorActions: {
    flexDirection: "row",
    gap: 8,
  },
  customerEditorButton: {
    flex: 1,
    marginTop: 0,
  },
  contextListTitle: {
    marginTop: 18,
    color: posColors.ink,
    fontSize: 15,
    fontWeight: "900",
  },
  contextLines: {
    marginTop: 8,
  },
  contextLine: {
    minHeight: 58,
    paddingVertical: 9,
    borderBottomWidth: 1,
    borderBottomColor: posColors.border,
    flexDirection: "row",
    alignItems: "center",
    gap: 8,
  },
  contextLineCopy: {
    flex: 1,
  },
  contextLineName: {
    color: posColors.ink,
    fontSize: 13,
    fontWeight: "700",
  },
  contextLineQuantity: {
    marginTop: 3,
    color: posColors.mutedInk,
    fontSize: 12,
  },
  contextLineAmount: {
    color: posColors.ink,
    fontSize: 13,
    fontWeight: "800",
    fontVariant: ["tabular-nums"],
  },
  contextEmpty: {
    paddingVertical: 18,
    color: posColors.mutedInk,
    fontSize: 13,
    lineHeight: 18,
  },
  workspaceCompact: {
    minHeight: 0,
    flexDirection: "column",
  },
  entryPane: {
    flex: 42,
    padding: 20,
    borderRightWidth: 1,
    borderRightColor: posColors.border,
  },
  entryPaneShort: {
    padding: 14,
  },
  entryPaneCompact: {
    borderRightWidth: 0,
    borderBottomWidth: 1,
    borderBottomColor: posColors.border,
  },
  summaryPane: {
    flex: 28,
    minWidth: 270,
    padding: 20,
    backgroundColor: "#FBFAF7",
  },
  summaryPaneCompact: {
    minWidth: 0,
  },
  sectionTitle: {
    color: posColors.ink,
    fontSize: 17,
    fontWeight: "800",
  },
  methodGrid: {
    marginTop: 12,
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
  },
  methodButton: {
    minHeight: 52,
    minWidth: 100,
    flexGrow: 1,
    flexBasis: "46%",
    paddingHorizontal: 18,
    borderWidth: 1,
    borderColor: posColors.border,
    backgroundColor: "#FFFFFF",
    alignItems: "center",
    justifyContent: "center",
  },
  methodButtonActive: {
    borderColor: posColors.blue,
    backgroundColor: posColors.blue,
  },
  methodButtonText: {
    color: posColors.ink,
    fontSize: 15,
    fontWeight: "800",
  },
  methodButtonTextActive: {
    color: "#FFFFFF",
  },
  providerBlockers: {
    marginTop: 10,
    gap: 3,
  },
  providerBlockerText: {
    color: posColors.mutedInk,
    fontSize: 12,
    lineHeight: 17,
  },
  form: {
    marginTop: 20,
    paddingTop: 18,
    borderTopWidth: 1,
    borderTopColor: posColors.border,
  },
  formShort: {
    marginTop: 12,
    paddingTop: 12,
  },
  inputLabel: {
    marginBottom: 6,
    color: posColors.ink,
    fontSize: 14,
    fontWeight: "800",
  },
  amountInput: {
    minHeight: 62,
    paddingHorizontal: 16,
    borderWidth: 2,
    borderColor: posColors.blue,
    backgroundColor: "#FFFFFF",
    color: posColors.ink,
    fontSize: 30,
    fontWeight: "800",
    fontVariant: ["tabular-nums"],
  },
  amountInputShort: {
    minHeight: 52,
    fontSize: 28,
  },
  keypad: {
    marginTop: 14,
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
  },
  keypadShort: {
    marginTop: 8,
    gap: 6,
  },
  keypadKey: {
    minHeight: 54,
    flexBasis: "30%",
    flexGrow: 1,
    borderWidth: 1,
    borderColor: posColors.border,
    backgroundColor: "#FFFFFF",
    alignItems: "center",
    justifyContent: "center",
  },
  keypadKeyShort: {
    minHeight: PAYMENT_MIN_TOUCH_TARGET,
  },
  keypadKeyText: {
    color: posColors.ink,
    fontSize: 22,
    fontWeight: "800",
    fontVariant: ["tabular-nums"],
  },
  quickCashRow: {
    marginTop: 10,
    flexDirection: "row",
    flexWrap: "nowrap",
    gap: 7,
  },
  quickCashRowShort: {
    marginTop: 8,
    flexWrap: "nowrap",
    gap: 6,
  },
  quickCashButton: {
    flexGrow: 1,
    flexBasis: 0,
    minWidth: PAYMENT_MIN_TOUCH_TARGET,
    marginTop: 0,
    paddingHorizontal: 4,
  },
  quickCashNote5: {
    backgroundColor: "#E7C5DD",
    borderColor: "#956485",
  },
  quickCashNote10: {
    backgroundColor: "#B9DCEB",
    borderColor: "#4F8198",
  },
  quickCashNote20: {
    backgroundColor: "#EDB5AA",
    borderColor: "#A85848",
  },
  quickCashNote50: {
    backgroundColor: "#F4DB7F",
    borderColor: "#9C7A18",
  },
  quickCashNote100: {
    backgroundColor: "#B9D8B4",
    borderColor: "#5C8358",
  },
  voucherInputGroup: {
    marginTop: 16,
  },
  voucherInput: {
    minHeight: 52,
    paddingHorizontal: 14,
    borderWidth: 1,
    borderColor: posColors.border,
    backgroundColor: "#FFFFFF",
    color: posColors.ink,
    fontSize: 18,
    fontWeight: "700",
  },
  inputHint: {
    marginTop: 6,
    color: posColors.mutedInk,
    fontSize: 13,
    lineHeight: 18,
  },
  inputHintShort: {
    marginTop: 4,
    lineHeight: 17,
  },
  secureCapture: {
    marginTop: 6,
    color: posColors.green,
    fontSize: 13,
    fontWeight: "700",
  },
  fieldError: {
    marginTop: 10,
    color: posColors.red,
    fontSize: 14,
    fontWeight: "700",
  },
  formActions: {
    marginTop: 12,
    flexDirection: "row",
    gap: 10,
  },
  formActionsShort: {
    marginTop: 8,
  },
  formAction: {
    flex: 1,
    marginTop: 0,
  },
  recoveryActions: {
    marginTop: 16,
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 10,
  },
  linklyPanel: {
    marginTop: 18,
    paddingTop: 18,
    borderTopWidth: 1,
    borderTopColor: posColors.border,
  },
  linklyKeyGrid: {
    marginTop: 12,
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
  },
  linklyConfirmation: {
    marginTop: 12,
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
  },
  summaryAmountRow: {
    minHeight: 38,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 16,
  },
  summaryLabel: {
    color: posColors.mutedInk,
    fontSize: 14,
    fontWeight: "600",
  },
  summaryAmount: {
    color: posColors.ink,
    fontSize: 18,
    fontWeight: "800",
    fontVariant: ["tabular-nums"],
  },
  summaryAmountEmphasis: {
    color: posColors.orange,
    fontSize: 30,
  },
  remainingRule: {
    height: 1,
    marginVertical: 8,
    backgroundColor: posColors.border,
  },
  cashSettlement: {
    marginTop: 10,
    paddingTop: 8,
    borderTopWidth: 1,
    borderTopColor: posColors.border,
  },
  summarySectionRule: {
    height: 1,
    marginVertical: 16,
    backgroundColor: posColors.border,
  },
  tenderTitle: {
    marginTop: 22,
    marginBottom: 8,
    color: posColors.ink,
    fontSize: 15,
    fontWeight: "800",
  },
  emptyTenders: {
    paddingVertical: 18,
    color: posColors.mutedInk,
    fontSize: 14,
  },
  tenderRow: {
    minHeight: 66,
    paddingVertical: 8,
    borderTopWidth: 1,
    borderTopColor: posColors.border,
    flexDirection: "row",
    alignItems: "center",
    gap: 10,
  },
  tenderIdentity: {
    flex: 1,
  },
  tenderMethod: {
    color: posColors.ink,
    fontSize: 15,
    fontWeight: "800",
  },
  tenderDisposition: {
    marginTop: 2,
    color: posColors.mutedInk,
    fontSize: 12,
  },
  tenderAmount: {
    color: posColors.ink,
    fontSize: 17,
    fontWeight: "800",
    fontVariant: ["tabular-nums"],
  },
  actionButton: {
    minHeight: 48,
    marginTop: 12,
    paddingHorizontal: 18,
    borderWidth: 1,
    borderColor: posColors.blue,
    backgroundColor: posColors.blue,
    alignItems: "center",
    justifyContent: "center",
  },
  actionButtonText: {
    color: "#FFFFFF",
    fontSize: 15,
    fontWeight: "800",
  },
  confirmationBackdrop: {
    flex: 1,
    padding: 24,
    backgroundColor: "rgba(13, 36, 53, 0.58)",
    alignItems: "center",
    justifyContent: "center",
  },
  confirmationCard: {
    width: "100%",
    maxWidth: 520,
    padding: 24,
    borderWidth: 1,
    borderColor: posColors.border,
    backgroundColor: "#FFFFFF",
  },
  confirmationTitle: {
    color: posColors.ink,
    fontSize: 22,
    fontWeight: "900",
  },
  confirmationBody: {
    marginTop: 10,
    color: posColors.mutedInk,
    fontSize: 15,
    lineHeight: 22,
  },
  confirmationActions: {
    marginTop: 18,
    flexDirection: "row",
    gap: 10,
  },
  confirmationAction: {
    flex: 1,
    marginTop: 0,
  },
  actionSecondary: {
    borderColor: posColors.blue,
    backgroundColor: posColors.blueSoft,
  },
  actionSecondaryText: {
    color: posColors.blue,
  },
  actionDanger: {
    borderColor: posColors.red,
    backgroundColor: posColors.red,
  },
  actionQuiet: {
    borderColor: posColors.border,
    backgroundColor: "#FFFFFF",
  },
  actionQuietText: {
    color: posColors.ink,
  },
  completeAction: {
    alignSelf: "flex-end",
    minWidth: 240,
  },
  confirmAction: {
    marginTop: 20,
  },
  disabled: {
    opacity: 0.42,
  },
  pressed: {
    opacity: 0.76,
  },
});
