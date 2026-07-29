import { useEffect, useSyncExternalStore } from "react";
import { useTranslation } from "react-i18next";
import {
  ActivityIndicator,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
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
  type PaymentPresenter,
  type PaymentPresenterState,
  type PaymentPresenterTender,
  type PaymentUiMethod,
  type PaymentUiPhase,
} from "./payment-presenter";

import type {
  LinklySafeOperatorKey,
} from "@/features/payments/runtime/linkly-operator-runtime";
import { PosStatusStrip } from "@/ui/shell/status-strip";
import { posColors } from "@/ui/theme";

export const PAYMENT_MIN_TOUCH_TARGET = 44;

const PAYMENT_METHODS = Object.freeze([
  "cash",
  "square",
  "linkly-cloud",
  "voucher",
] as const satisfies readonly PaymentUiMethod[]);

type PaymentScreenProps = Readonly<{
  presenter: PaymentPresenter;
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
  const { width } = useWindowDimensions();
  const compact = width < 900;

  useEffect(() => {
    void presenter.initialize();
    return () => presenter.destroy();
  }, [presenter]);

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
          {onBack ? (
            <ActionButton
              disabled={!canLeave}
              label={t("action.back")}
              onPress={onBack}
              testID="payment-back"
              tone="quiet"
            />
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
              <Pressable
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
              </Pressable>
            ) : null}
          </View>
        ) : null}

        <View
          style={[
            styles.workspace,
            compact && styles.workspaceCompact,
          ]}
        >
          <View
            style={[
              styles.entryPane,
              compact && styles.entryPaneCompact,
            ]}
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

            {showEntry ? (
              <View style={styles.form} testID="payment-entry-form">
                <Text style={styles.inputLabel}>{t("amount.label")}</Text>
                <TextInput
                  accessibilityLabel={t("amount.label")}
                  editable={!state.busy}
                  keyboardType="decimal-pad"
                  onChangeText={(value) => presenter.setAmountText(value)}
                  placeholder="0.00"
                  placeholderTextColor="#7B8793"
                  selectionColor={posColors.blue}
                  style={styles.amountInput}
                  testID="payment-amount"
                  value={state.amountText}
                />
                <Text style={styles.inputHint}>{t("amount.hint")}</Text>

                {state.selectedMethod === "voucher" ? (
                  <View
                    key={state.sensitiveInputRevision}
                    style={styles.voucherInputGroup}
                  >
                    <Text style={styles.inputLabel}>
                      {t("voucher.label")}
                    </Text>
                    <TextInput
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
                  testID="payment-submit"
                />
              </View>
            ) : null}

            <RecoveryActions presenter={presenter} state={state} t={t} />
            <LinklyControls presenter={presenter} state={state} t={t} />
          </View>

          <PaymentSummary
            compact={compact}
            locale={locale}
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
    </SafeAreaView>
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
  presenter: PaymentPresenter;
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
  presenter: PaymentPresenter;
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
  presenter,
  state,
  t,
}: Readonly<{
  compact: boolean;
  locale: PaymentLocale;
  presenter: PaymentPresenter;
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
    <Pressable
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
    </Pressable>
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
    <Pressable
      accessibilityRole="button"
      accessibilityState={{ disabled }}
      disabled={disabled}
      onPress={onPress}
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
    </Pressable>
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
  if (state.busy) return false;
  if (!state.orderGuid) return state.phase === "ready";
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
  workspaceCompact: {
    minHeight: 0,
    flexDirection: "column",
  },
  entryPane: {
    flex: 1.6,
    padding: 20,
    borderRightWidth: 1,
    borderRightColor: posColors.border,
  },
  entryPaneCompact: {
    borderRightWidth: 0,
    borderBottomWidth: 1,
    borderBottomColor: posColors.border,
  },
  summaryPane: {
    flex: 1,
    minWidth: 320,
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
    minWidth: 120,
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
  disabled: {
    opacity: 0.42,
  },
  pressed: {
    opacity: 0.76,
  },
});
