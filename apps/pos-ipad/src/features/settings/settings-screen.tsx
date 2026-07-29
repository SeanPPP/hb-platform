import { useEffect, useSyncExternalStore } from "react";
import { useTranslation } from "react-i18next";
import {
  Modal,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  View,
  type StyleProp,
  type ViewStyle,
} from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";

import {
  resolveSettingsLocale,
  settingsText,
  type SettingsCopyKey,
  type SettingsLocale,
} from "./settings-copy";
import {
  type PaymentEnvironment,
  type SettingsDangerousConfirmation,
  type SettingsPane,
  type SettingsPresenter,
  type SettingsState,
  type SettingsStatusCode,
} from "./settings-presenter";

import {
  HidScannerCapture,
  type HidScannerRouter,
} from "@/core/peripherals/scanner";
import { usePosShellStore } from "@/ui/shell/pos-shell-store";
import { posColors } from "@/ui/theme";

export const SETTINGS_MIN_TOUCH_TARGET = 44;

export type SettingsScreenPresenter = Pick<
  SettingsPresenter,
  | "cancelConfirmation"
  | "checkForAppUpdate"
  | "confirmDangerousAction"
  | "connectPrinter"
  | "downloadCatalog"
  | "getState"
  | "load"
  | "requestApiAddressChange"
  | "requestAppRestart"
  | "requestCatalogReset"
  | "requestDeviceReregistration"
  | "savePaymentSettings"
  | "savePrinterSettings"
  | "scanPrinters"
  | "selectPane"
  | "setApiAddressDraft"
  | "setDrawerEnabled"
  | "setExternalDisplayEnabled"
  | "setLinklyEnvironment"
  | "setPaymentProvider"
  | "setPrinterEnabled"
  | "setPrinterLocale"
  | "setPrinterPaper"
  | "setPrinterPeripheralId"
  | "setReregisterStoreCode"
  | "setSquareDeviceId"
  | "setSquareEnvironment"
  | "setSquareLocationId"
  | "setTerminalName"
  | "subscribe"
  | "testExternalDisplay"
  | "testPaymentProvider"
  | "testPrinter"
  | "testScanner"
> &
  Readonly<{ getState(): SettingsState }>;

type SettingsScreenProps = Readonly<{
  locale?: SettingsLocale;
  onBack?(): void;
  presenter: SettingsScreenPresenter;
  scanner?: HidScannerRouter;
}>;

const NAV_ITEMS: readonly Readonly<{
  label: SettingsCopyKey;
  pane: SettingsPane;
}>[] = [
  { pane: "general", label: "navigation.general" },
  { pane: "payments", label: "navigation.payments" },
  { pane: "peripherals", label: "navigation.peripherals" },
  { pane: "device", label: "navigation.device" },
  { pane: "hardware", label: "navigation.hardware" },
];

/**
 * iPad 横屏设置台保持清晰的左侧分区与右侧工作区。危险操作不使用平台 Alert，
 * 而在同一屏显示完整影响范围，确保自动化、键盘与触控都走同一确认路径。
 */
export function SettingsScreen({
  locale: localeOverride,
  onBack,
  presenter,
  scanner,
}: SettingsScreenProps) {
  const state = useSyncExternalStore(
    presenter.subscribe,
    presenter.getState,
    presenter.getState,
  );

  useEffect(() => {
    if (presenter.getState().kind === "idle") {
      void presenter.load();
    }
  }, [presenter]);
  useEffect(() => {
    if (!scanner) return undefined;
    scanner.pushContext("dialog");
    return () => {
      scanner.popContext();
    };
  }, [scanner]);
  const setScannerStatus = usePosShellStore((current) => current.setScanner);
  const { i18n } = useTranslation();
  const locale =
    localeOverride ??
    resolveSettingsLocale(i18n.resolvedLanguage ?? i18n.language);
  const t = (
    key: SettingsCopyKey,
    values?: Readonly<Record<string, string | number>>,
  ) => settingsText(locale, key, values);
  const interactionLocked = state.busy || state.confirmation !== null;

  return (
    <SafeAreaView style={styles.safeArea} testID="settings-screen">
      <View style={styles.page}>
        {scanner ? (
          <HidScannerCapture
            active={
              state.activePane === "hardware" && state.confirmation === null
            }
            onCaptureStatusChange={setScannerStatus}
            scanner={scanner}
          />
        ) : null}
        <View style={styles.header}>
          <View style={styles.titleGroup}>
            <Text style={styles.eyebrow}>{t("header.eyebrow")}</Text>
            <Text style={styles.title}>{t("header.title")}</Text>
            <Text style={styles.subtitle}>{t("header.subtitle")}</Text>
          </View>
          {onBack ? (
            <ActionButton
              disabled={interactionLocked}
              label={t("action.backToSales")}
              onPress={onBack}
              testID="settings-back"
              tone="quiet"
            />
          ) : null}
        </View>

        {state.statusCode ? (
          <StatusBanner locale={locale} statusCode={state.statusCode} />
        ) : null}

        <View style={styles.workspace} testID="settings-workspace">
          <View
            pointerEvents={state.confirmation ? "none" : "auto"}
            style={styles.navigation}
          >
            <Text style={styles.navigationTitle}>{t("navigation.title")}</Text>
            {NAV_ITEMS.map((item) => (
              <ActionButton
                disabled={interactionLocked}
                key={item.pane}
                label={t(item.label)}
                onPress={() => presenter.selectPane(item.pane)}
                selected={state.activePane === item.pane}
                testID={`settings-nav-${item.pane}`}
                tone="nav"
              />
            ))}
            <View style={styles.deviceBadge}>
              <Text style={styles.badgeLabel}>
                {t("device.currentTerminal")}
              </Text>
              <Text style={styles.badgeValue}>
                {state.device.deviceCode || "—"}
              </Text>
              <Text style={styles.badgeMeta}>
                {state.device.storeCode || t("device.unbound")}
              </Text>
            </View>
          </View>

          <ScrollView
            contentContainerStyle={styles.content}
            keyboardShouldPersistTaps="handled"
            pointerEvents={state.confirmation ? "none" : "auto"}
            style={styles.contentScroll}
          >
            {state.kind === "loading" || state.kind === "idle" ? (
              <EmptyPanel
                message={t("state.loading")}
                testID="settings-loading"
              />
            ) : null}
            {state.kind === "failed" ? (
              <EmptyPanel
                message={t("state.failed")}
                testID="settings-failed"
              />
            ) : null}
            {state.kind === "unauthorized" ? (
              <EmptyPanel
                message={t("state.unauthorized")}
                testID="settings-unauthorized"
              />
            ) : null}
            {state.kind === "ready" ? (
              <SettingsPaneContent
                locale={locale}
                presenter={presenter}
                state={state}
              />
            ) : null}
          </ScrollView>
        </View>

        <Modal
          animationType="fade"
          onRequestClose={() => presenter.cancelConfirmation()}
          testID="settings-confirmation-modal"
          transparent
          visible={state.confirmation !== null}
        >
          <View accessibilityViewIsModal style={styles.confirmationOverlay}>
            {state.confirmation ? (
              <ConfirmationCard
                busy={state.busy}
                confirmation={state.confirmation}
                locale={locale}
                onCancel={() => presenter.cancelConfirmation()}
                onConfirm={() => void presenter.confirmDangerousAction()}
              />
            ) : null}
          </View>
        </Modal>
      </View>
    </SafeAreaView>
  );
}

export function SettingsUnavailableScreen({
  locale: localeOverride,
  onBack,
}: Readonly<{ locale?: SettingsLocale; onBack(): void }>) {
  const { i18n } = useTranslation();
  const locale =
    localeOverride ??
    resolveSettingsLocale(i18n.resolvedLanguage ?? i18n.language);
  const t = (key: SettingsCopyKey) => settingsText(locale, key);
  return (
    <SafeAreaView style={styles.safeArea} testID="settings-runtime-unavailable">
      <View style={styles.unavailable}>
        <Text style={styles.eyebrow}>{t("unavailable.eyebrow")}</Text>
        <Text style={styles.unavailableTitle}>{t("unavailable.title")}</Text>
        <Text style={styles.unavailableHint}>{t("unavailable.hint")}</Text>
        <ActionButton
          label={t("action.backToSales")}
          onPress={onBack}
          testID="settings-unavailable-back"
        />
      </View>
    </SafeAreaView>
  );
}

function SettingsPaneContent({
  locale,
  presenter,
  state,
}: Readonly<{
  locale: SettingsLocale;
  presenter: SettingsScreenPresenter;
  state: SettingsState;
}>) {
  switch (state.activePane) {
    case "payments":
      return (
        <PaymentsPane locale={locale} presenter={presenter} state={state} />
      );
    case "peripherals":
      return (
        <PeripheralsPane locale={locale} presenter={presenter} state={state} />
      );
    case "device":
      return <DevicePane locale={locale} presenter={presenter} state={state} />;
    case "hardware":
      return (
        <HardwarePane locale={locale} presenter={presenter} state={state} />
      );
    default:
      return (
        <GeneralPane locale={locale} presenter={presenter} state={state} />
      );
  }
}

function GeneralPane({
  locale,
  presenter,
  state,
}: Readonly<{
  locale: SettingsLocale;
  presenter: SettingsScreenPresenter;
  state: SettingsState;
}>) {
  const locked = state.busy || state.confirmation !== null;
  const t = (key: SettingsCopyKey) => settingsText(locale, key);
  return (
    <View testID="settings-pane-content-general">
      <PaneHeading
        subtitle={t("general.subtitle")}
        title={t("general.title")}
      />
      <SectionCard
        eyebrow={t("eyebrow.network")}
        title={t("general.apiAddress")}
      >
        <TextInput
          accessibilityLabel={t("general.apiAddress")}
          autoCapitalize="none"
          autoCorrect={false}
          editable={!locked && state.access.canReregisterDevice}
          onChangeText={(value) => presenter.setApiAddressDraft(value)}
          placeholder="https://example.com/pos-api"
          placeholderTextColor="#7B8793"
          style={styles.textInput}
          testID="settings-api-address"
          value={state.apiAddressDraft}
        />
        <ActionButton
          disabled={locked || !state.access.canReregisterDevice}
          label={t("general.reviewApiAddress")}
          onPress={() => presenter.requestApiAddressChange()}
          testID="settings-api-request-change"
        />
      </SectionCard>

      <SectionCard eyebrow={t("eyebrow.catalog")} title={t("general.catalog")}>
        <View style={styles.metricRow}>
          <Metric
            label={t("metric.snapshot")}
            value={state.catalog.snapshotId ?? t("value.none")}
          />
          <Metric
            label={t("metric.items")}
            value={String(state.catalog.itemCount)}
          />
          <Metric
            label={t("metric.activated")}
            value={compactDate(state.catalog.activatedAt)}
          />
        </View>
        <View style={styles.actionRow}>
          <ActionButton
            disabled={locked || !state.access.canDownloadCatalog}
            label={t("general.downloadCatalog")}
            onPress={() => void presenter.downloadCatalog()}
            testID="settings-catalog-download"
          />
          <ActionButton
            disabled={locked || !state.access.canResetCatalog}
            label={t("general.resetCatalog")}
            onPress={() => presenter.requestCatalogReset()}
            testID="settings-catalog-reset"
            tone="danger"
          />
        </View>
        <Text style={styles.safetyNote}>{t("general.catalogSafety")}</Text>
      </SectionCard>

      <SectionCard
        eyebrow={t("eyebrow.updates")}
        title={t("general.appUpdate")}
      >
        <View style={styles.metricRow}>
          <Metric label={t("metric.channel")} value={state.appUpdate.channel} />
          <Metric
            label={t("metric.currentVersion")}
            value={state.appUpdate.currentVersion || "—"}
          />
          <Metric
            label={t("metric.availableVersion")}
            value={state.appUpdate.availableVersion ?? t("value.current")}
          />
        </View>
        <View style={styles.actionRow}>
          <ActionButton
            disabled={locked || !state.access.canManageAppUpdate}
            label={t("general.checkUpdate")}
            onPress={() => void presenter.checkForAppUpdate()}
            testID="settings-update-check"
            tone="secondary"
          />
          <ActionButton
            disabled={
              locked ||
              !state.access.canManageAppUpdate ||
              !state.appUpdate.restartAvailable
            }
            label={t("general.restart")}
            onPress={() => presenter.requestAppRestart()}
            testID="settings-update-restart"
            tone="danger"
          />
        </View>
      </SectionCard>
    </View>
  );
}

function PaymentsPane({
  locale,
  presenter,
  state,
}: Readonly<{
  locale: SettingsLocale;
  presenter: SettingsScreenPresenter;
  state: SettingsState;
}>) {
  const t = (key: SettingsCopyKey) => settingsText(locale, key);
  const disabled =
    state.busy ||
    state.confirmation !== null ||
    !state.access.canConfigurePayments;
  const squareAvailable =
    state.square.available && state.square.blockerCode === null;
  const linklyAvailable =
    state.linkly.available && state.linkly.blockerCode === null;
  const squareDisabled =
    disabled || !squareAvailable || state.paymentProviderDraft !== "square";
  const linklyDisabled =
    disabled || !linklyAvailable || state.paymentProviderDraft !== "linkly";
  return (
    <View testID="settings-pane-content-payments">
      <PaneHeading
        subtitle={t("payments.subtitle")}
        title={t("payments.title")}
      />
      <SectionCard
        eyebrow={t("eyebrow.activeCardTerminal")}
        title={t("payments.provider")}
      >
        <Text style={styles.sectionCopy}>{t("payments.providerHint")}</Text>
        <View style={styles.actionRow}>
          <ToggleButton
            disabled={disabled || !squareAvailable}
            label="Square"
            onPress={() => presenter.setPaymentProvider("square")}
            selected={state.paymentProviderDraft === "square"}
            testID="settings-payment-provider-square"
          />
          <ToggleButton
            disabled={disabled || !linklyAvailable}
            label="Linkly"
            onPress={() => presenter.setPaymentProvider("linkly")}
            selected={state.paymentProviderDraft === "linkly"}
            testID="settings-payment-provider-linkly"
          />
        </View>
        <Text
          style={styles.safetyNote}
          testID="settings-payment-provider-state"
        >
          {state.paymentProviderDraft === null
            ? t("payments.noneSelected")
            : state.paymentProviderDraft === "square"
              ? t("payments.squareSelected")
              : t("payments.linklySelected")}
        </Text>
      </SectionCard>
      <View style={styles.twoColumn}>
        <SectionCard
          eyebrow={t("eyebrow.cardTerminal")}
          style={styles.columnCard}
          title="Square"
        >
          <Availability
            available={squareAvailable}
            blockerCode={state.square.blockerCode}
            locale={locale}
          />
          <EnvironmentSelector
            disabled={squareDisabled}
            environment={state.squareDraft.environment}
            locale={locale}
            onSelect={(environment) =>
              presenter.setSquareEnvironment(environment)
            }
            prefix="settings-square"
          />
          <FieldLabel label={t("field.locationId")} />
          <TextInput
            accessibilityLabel={t("field.squareLocationId")}
            autoCapitalize="none"
            autoCorrect={false}
            editable={!squareDisabled}
            onChangeText={(value) => presenter.setSquareLocationId(value)}
            style={styles.textInput}
            testID="settings-square-location"
            value={state.squareDraft.locationId}
          />
          <FieldLabel label={t("field.deviceId")} />
          <TextInput
            accessibilityLabel={t("field.squareDeviceId")}
            autoCapitalize="none"
            autoCorrect={false}
            editable={!squareDisabled}
            onChangeText={(value) => presenter.setSquareDeviceId(value)}
            style={styles.textInput}
            testID="settings-square-device"
            value={state.squareDraft.deviceId}
          />
          <ActionButton
            disabled={squareDisabled}
            label={t("action.test")}
            onPress={() => void presenter.testPaymentProvider("square")}
            testID="settings-square-test"
            tone="secondary"
          />
        </SectionCard>

        <SectionCard
          eyebrow={t("eyebrow.eftpos")}
          style={styles.columnCard}
          title="Linkly"
        >
          <Availability
            available={linklyAvailable}
            blockerCode={state.linkly.blockerCode}
            locale={locale}
          />
          <EnvironmentSelector
            disabled={linklyDisabled}
            environment={state.linklyDraft.environment}
            locale={locale}
            onSelect={(environment) =>
              presenter.setLinklyEnvironment(environment)
            }
            prefix="settings-linkly"
          />
          <Text style={styles.sectionCopy}>{t("payments.linklyHint")}</Text>
          <ActionButton
            disabled={linklyDisabled}
            label={t("action.test")}
            onPress={() => void presenter.testPaymentProvider("linkly")}
            testID="settings-linkly-test"
            tone="secondary"
          />
        </SectionCard>
      </View>
      <ActionButton
        disabled={disabled}
        label={t("payments.save")}
        onPress={() => void presenter.savePaymentSettings()}
        testID="settings-payment-save"
      />
    </View>
  );
}

function PeripheralsPane({
  locale,
  presenter,
  state,
}: Readonly<{
  locale: SettingsLocale;
  presenter: SettingsScreenPresenter;
  state: SettingsState;
}>) {
  const t = (
    key: SettingsCopyKey,
    values?: Readonly<Record<string, string | number>>,
  ) => settingsText(locale, key, values);
  const printerDisabled =
    state.busy ||
    state.confirmation !== null ||
    !state.access.canConfigurePrinter;
  const displayDisabled =
    state.busy ||
    state.confirmation !== null ||
    !state.access.canManageCustomerDisplay;
  return (
    <View testID="settings-pane-content-peripherals">
      <PaneHeading
        subtitle={t("peripherals.subtitle")}
        title={t("peripherals.title")}
      />
      <SectionCard
        eyebrow={t("eyebrow.receipt")}
        title={t("peripherals.printer")}
      >
        <View style={styles.actionRow}>
          <ToggleButton
            disabled={printerDisabled}
            label={t("peripherals.printing")}
            onPress={() =>
              presenter.setPrinterEnabled(!state.printer.printEnabled)
            }
            selected={state.printer.printEnabled}
            testID="settings-printer-enabled"
          />
          <ToggleButton
            disabled={printerDisabled}
            label={t("peripherals.drawer")}
            onPress={() =>
              presenter.setDrawerEnabled(!state.printer.drawerEnabled)
            }
            selected={state.printer.drawerEnabled}
            testID="settings-drawer-enabled"
          />
          <ToggleButton
            disabled={printerDisabled}
            label={state.printer.paper}
            onPress={() =>
              presenter.setPrinterPaper(
                state.printer.paper === "80mm" ? "58mm" : "80mm",
              )
            }
            selected
            testID="settings-printer-paper"
          />
          <ToggleButton
            disabled={printerDisabled}
            label={t(
              state.printer.locale === "en"
                ? "printer.locale.en"
                : "printer.locale.zhCN",
            )}
            onPress={() =>
              presenter.setPrinterLocale(
                state.printer.locale === "en" ? "zh-CN" : "en",
              )
            }
            selected
            testID="settings-printer-locale"
          />
        </View>
        <FieldLabel label={t("field.peripheralId")} />
        <TextInput
          accessibilityLabel={t("field.printerPeripheralId")}
          autoCapitalize="none"
          autoCorrect={false}
          editable={!printerDisabled}
          onChangeText={(value) => presenter.setPrinterPeripheralId(value)}
          style={styles.textInput}
          testID="settings-printer-id"
          value={state.printer.peripheralId ?? ""}
        />
        <View style={styles.actionRow}>
          <ActionButton
            disabled={printerDisabled}
            label={t("peripherals.scanPrinters")}
            onPress={() => void presenter.scanPrinters()}
            testID="settings-printer-scan"
            tone="secondary"
          />
          <ActionButton
            disabled={printerDisabled}
            label={t("action.save")}
            onPress={() => void presenter.savePrinterSettings()}
            testID="settings-printer-save"
          />
          <ActionButton
            disabled={printerDisabled}
            label={t("peripherals.testPrint")}
            onPress={() => void presenter.testPrinter()}
            testID="settings-printer-test"
            tone="secondary"
          />
        </View>
        {state.printerDevices.map((device) => (
          <View
            key={device.id}
            style={styles.deviceRow}
            testID={`settings-printer-device-${device.id}`}
          >
            <View style={styles.deviceIdentity}>
              <Text style={styles.deviceName}>{device.name}</Text>
              <Text style={styles.deviceMeta}>
                {device.transport} · {device.id}
              </Text>
            </View>
            <ActionButton
              compact
              disabled={printerDisabled}
              label={t("action.connect")}
              onPress={() => void presenter.connectPrinter(device.id)}
              testID={`settings-printer-connect-${device.id}`}
            />
          </View>
        ))}
      </SectionCard>

      <View style={styles.twoColumn}>
        <SectionCard
          eyebrow={t("eyebrow.scanner")}
          style={styles.columnCard}
          title={t("peripherals.scanner")}
        >
          <Text style={styles.sectionCopy}>
            {t("label.status", {
              status: hardwareStatusText(locale, state.hardware.scannerStatus),
            })}
          </Text>
          <ActionButton
            disabled={
              state.busy ||
              state.confirmation !== null ||
              !state.access.canTestScanner
            }
            label={t("peripherals.captureOneScan")}
            onPress={() => void presenter.testScanner()}
            testID="settings-scanner-test"
            tone="secondary"
          />
        </SectionCard>
        <SectionCard
          eyebrow={t("eyebrow.customerDisplay")}
          style={styles.columnCard}
          title={t("peripherals.externalDisplay")}
        >
          <Text style={styles.sectionCopy}>
            {t("label.status", {
              status: hardwareStatusText(locale, state.externalDisplay.status),
            })}
          </Text>
          <View style={styles.actionRow}>
            <ToggleButton
              disabled={displayDisabled || !state.externalDisplay.available}
              label={t("action.enabled")}
              onPress={() =>
                void presenter.setExternalDisplayEnabled(
                  !state.externalDisplay.enabled,
                )
              }
              selected={state.externalDisplay.enabled}
              testID="settings-display-enabled"
            />
            <ActionButton
              disabled={displayDisabled || !state.externalDisplay.available}
              label={t("peripherals.testDisplay")}
              onPress={() => void presenter.testExternalDisplay()}
              testID="settings-display-test"
              tone="secondary"
            />
          </View>
        </SectionCard>
      </View>
    </View>
  );
}

function DevicePane({
  locale,
  presenter,
  state,
}: Readonly<{
  locale: SettingsLocale;
  presenter: SettingsScreenPresenter;
  state: SettingsState;
}>) {
  const disabled =
    state.busy ||
    state.confirmation !== null ||
    !state.access.canReregisterDevice;
  const t = (key: SettingsCopyKey) => settingsText(locale, key);
  return (
    <View testID="settings-pane-content-device">
      <PaneHeading subtitle={t("device.subtitle")} title={t("device.title")} />
      <SectionCard
        eyebrow={t("eyebrow.currentBinding")}
        title={t("device.currentBinding")}
      >
        <View style={styles.metricRow}>
          <Metric
            label={t("metric.store")}
            value={state.device.storeCode || "—"}
          />
          <Metric
            label={t("metric.storeName")}
            value={state.device.storeName || "—"}
          />
          <Metric
            label={t("metric.device")}
            value={state.device.deviceCode || "—"}
          />
        </View>
      </SectionCard>
      <SectionCard
        eyebrow={t("eyebrow.reregister")}
        title={t("device.reregister")}
      >
        <FieldLabel label={t("field.targetStoreCode")} />
        <TextInput
          accessibilityLabel={t("field.targetStoreCode")}
          autoCapitalize="characters"
          autoCorrect={false}
          editable={!disabled}
          onChangeText={(value) => presenter.setReregisterStoreCode(value)}
          style={styles.textInput}
          testID="settings-reregister-store"
          value={state.reregisterStoreCode}
        />
        <FieldLabel label={t("field.terminalName")} />
        <TextInput
          accessibilityLabel={t("field.terminalName")}
          editable={!disabled}
          onChangeText={(value) => presenter.setTerminalName(value)}
          style={styles.textInput}
          testID="settings-reregister-terminal"
          value={state.terminalNameDraft}
        />
        <ActionButton
          disabled={disabled}
          label={t("device.reviewReregistration")}
          onPress={() => presenter.requestDeviceReregistration()}
          testID="settings-reregister-request"
          tone="danger"
        />
      </SectionCard>
    </View>
  );
}

function HardwarePane({
  locale,
  presenter,
  state,
}: Readonly<{
  locale: SettingsLocale;
  presenter: SettingsScreenPresenter;
  state: SettingsState;
}>) {
  const t = (key: SettingsCopyKey) => settingsText(locale, key);
  return (
    <View testID="settings-pane-content-hardware">
      <PaneHeading
        subtitle={t("hardware.subtitle")}
        title={t("hardware.title")}
      />
      <View style={styles.hardwareGrid}>
        <HardwareCard
          actionLabel={t("hardware.printTest")}
          disabled={
            state.busy ||
            state.confirmation !== null ||
            !state.access.canConfigurePrinter
          }
          onPress={() => void presenter.testPrinter()}
          status={state.hardware.printerStatus}
          statusText={hardwareStatusText(locale, state.hardware.printerStatus)}
          testID="settings-hardware-printer"
          title={t("peripherals.printer")}
        />
        <HardwareCard
          actionLabel={t("hardware.captureScan")}
          disabled={
            state.busy ||
            state.confirmation !== null ||
            !state.access.canTestScanner
          }
          onPress={() => void presenter.testScanner()}
          status={state.hardware.scannerStatus}
          statusText={hardwareStatusText(locale, state.hardware.scannerStatus)}
          testID="settings-hardware-scanner"
          title={t("peripherals.scanner")}
        />
        <HardwareCard
          actionLabel={t("hardware.showTest")}
          disabled={
            state.busy ||
            state.confirmation !== null ||
            !state.access.canManageCustomerDisplay ||
            !state.externalDisplay.available
          }
          onPress={() => void presenter.testExternalDisplay()}
          status={state.hardware.externalDisplayStatus}
          statusText={hardwareStatusText(
            locale,
            state.hardware.externalDisplayStatus,
          )}
          testID="settings-hardware-display"
          title={t("hardware.display")}
        />
      </View>
      <SectionCard
        eyebrow={t("eyebrow.lastCapture")}
        title={t("hardware.lastScan")}
      >
        <Text style={styles.scannerValue}>
          {state.hardware.lastScannerValue ?? t("hardware.waitingForTest")}
        </Text>
      </SectionCard>
    </View>
  );
}

function ConfirmationCard({
  busy,
  confirmation,
  locale,
  onCancel,
  onConfirm,
}: Readonly<{
  busy: boolean;
  confirmation: SettingsDangerousConfirmation;
  locale: SettingsLocale;
  onCancel(): void;
  onConfirm(): void;
}>) {
  return (
    <View
      accessibilityRole="alert"
      style={styles.confirmation}
      testID="settings-confirmation"
    >
      <View style={styles.confirmationCopy}>
        <Text style={styles.confirmationTitle}>
          {confirmationTitle(locale, confirmation)}
        </Text>
        <Text style={styles.confirmationBody}>
          {settingsText(locale, "confirmation.body")}
        </Text>
      </View>
      <View style={styles.confirmationActions}>
        <ActionButton
          disabled={busy}
          label={settingsText(locale, "action.cancel")}
          onPress={onCancel}
          testID="settings-confirm-cancel"
          tone="quiet"
        />
        <ActionButton
          disabled={busy}
          label={settingsText(
            locale,
            busy ? "action.checking" : "action.confirm",
          )}
          onPress={onConfirm}
          testID="settings-confirm"
          tone="danger"
        />
      </View>
    </View>
  );
}

function EnvironmentSelector({
  disabled,
  environment,
  locale,
  onSelect,
  prefix,
}: Readonly<{
  disabled: boolean;
  environment: PaymentEnvironment;
  locale: SettingsLocale;
  onSelect(environment: PaymentEnvironment): void;
  prefix: string;
}>) {
  return (
    <View style={styles.actionRow}>
      <ToggleButton
        disabled={disabled}
        label={settingsText(locale, "environment.production")}
        onPress={() => onSelect("Production")}
        selected={environment === "Production"}
        testID={`${prefix}-production`}
      />
      <ToggleButton
        disabled={disabled}
        label={settingsText(locale, "environment.sandbox")}
        onPress={() => onSelect("Sandbox")}
        selected={environment === "Sandbox"}
        testID={`${prefix}-sandbox`}
      />
    </View>
  );
}

function HardwareCard({
  actionLabel,
  disabled,
  onPress,
  status,
  statusText,
  testID,
  title,
}: Readonly<{
  actionLabel: string;
  disabled: boolean;
  onPress(): void;
  status: string;
  statusText: string;
  testID: string;
  title: string;
}>) {
  return (
    <View style={styles.hardwareCard}>
      <Text style={styles.hardwareTitle}>{title}</Text>
      <Text
        style={[
          styles.hardwareStatus,
          status === "connected" || status === "ready"
            ? styles.hardwareStatusReady
            : status === "disconnected"
              ? styles.hardwareStatusDisconnected
              : styles.hardwareStatusUnavailable,
        ]}
        testID={`${testID}-status`}
      >
        {statusText}
      </Text>
      <ActionButton
        disabled={disabled}
        label={actionLabel}
        onPress={onPress}
        testID={testID}
        tone="secondary"
      />
    </View>
  );
}

function PaneHeading({
  subtitle,
  title,
}: Readonly<{ subtitle: string; title: string }>) {
  return (
    <View style={styles.paneHeading}>
      <Text style={styles.paneTitle}>{title}</Text>
      <Text style={styles.paneSubtitle}>{subtitle}</Text>
    </View>
  );
}

function SectionCard({
  children,
  eyebrow,
  style,
  title,
}: Readonly<{
  children: React.ReactNode;
  eyebrow: string;
  style?: StyleProp<ViewStyle>;
  title: string;
}>) {
  return (
    <View style={[styles.sectionCard, style]}>
      <Text style={styles.cardEyebrow}>{eyebrow}</Text>
      <Text style={styles.cardTitle}>{title}</Text>
      {children}
    </View>
  );
}

function Availability({
  available,
  blockerCode,
  locale,
}: Readonly<{
  available: boolean;
  blockerCode: string | null;
  locale: SettingsLocale;
}>) {
  return (
    <View
      style={[
        styles.availability,
        available ? styles.availabilityReady : styles.availabilityUnavailable,
      ]}
    >
      <Text style={styles.availabilityText}>
        {available
          ? settingsText(locale, "availability.ready")
          : settingsText(locale, "availability.unavailable", {
              blocker: blockerCode ? ` · ${blockerCode}` : "",
            })}
      </Text>
    </View>
  );
}

function Metric({ label, value }: Readonly<{ label: string; value: string }>) {
  return (
    <View style={styles.metric}>
      <Text style={styles.metricLabel}>{label}</Text>
      <Text numberOfLines={2} style={styles.metricValue}>
        {value}
      </Text>
    </View>
  );
}

function FieldLabel({ label }: Readonly<{ label: string }>) {
  return <Text style={styles.fieldLabel}>{label}</Text>;
}

function EmptyPanel({
  message,
  testID,
}: Readonly<{ message: string; testID: string }>) {
  return (
    <View style={styles.emptyPanel} testID={testID}>
      <Text style={styles.emptyText}>{message}</Text>
    </View>
  );
}

function StatusBanner({
  locale,
  statusCode,
}: Readonly<{ locale: SettingsLocale; statusCode: SettingsStatusCode }>) {
  const success = isSuccessStatus(statusCode);
  return (
    <View
      accessibilityLiveRegion="polite"
      style={[
        styles.statusBanner,
        success ? styles.statusSuccess : styles.statusWarning,
      ]}
      testID="settings-status"
    >
      <Text style={styles.statusText}>{statusCopy(locale, statusCode)}</Text>
      <Text style={styles.statusCode}>[{statusCode}]</Text>
    </View>
  );
}

function hardwareStatusText(locale: SettingsLocale, status: string): string {
  const statusKey: Readonly<Record<string, SettingsCopyKey>> = {
    connected: "hardware.connected",
    ready: "hardware.ready",
    disconnected: "hardware.disconnected",
    unavailable: "hardware.unavailable",
  };
  return statusKey[status] ? settingsText(locale, statusKey[status]) : status;
}

function ToggleButton({
  disabled,
  label,
  onPress,
  selected,
  testID,
}: Readonly<{
  disabled: boolean;
  label: string;
  onPress(): void;
  selected: boolean;
  testID: string;
}>) {
  return (
    <ActionButton
      disabled={disabled}
      label={`${selected ? "✓ " : ""}${label}`}
      onPress={onPress}
      selected={selected}
      testID={testID}
      tone="secondary"
    />
  );
}

function ActionButton({
  compact = false,
  disabled = false,
  label,
  onPress,
  selected = false,
  style,
  testID,
  tone = "primary",
}: Readonly<{
  compact?: boolean;
  disabled?: boolean;
  label: string;
  onPress(): void;
  selected?: boolean;
  style?: StyleProp<ViewStyle>;
  testID: string;
  tone?: "danger" | "nav" | "primary" | "quiet" | "secondary";
}>) {
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityState={{ disabled, selected }}
      disabled={disabled}
      onPress={onPress}
      style={({ pressed }) => [
        styles.button,
        compact && styles.compactButton,
        tone === "secondary" && styles.secondaryButton,
        tone === "quiet" && styles.quietButton,
        tone === "danger" && styles.dangerButton,
        tone === "nav" && styles.navButton,
        selected && styles.selectedButton,
        disabled && styles.disabledButton,
        pressed && !disabled && styles.pressedButton,
        style,
      ]}
      testID={testID}
    >
      <Text
        style={[
          styles.buttonLabel,
          (tone === "secondary" || tone === "quiet" || tone === "nav") &&
            styles.secondaryButtonLabel,
          selected && styles.selectedButtonLabel,
        ]}
      >
        {label}
      </Text>
    </Pressable>
  );
}

function confirmationTitle(
  locale: SettingsLocale,
  confirmation: SettingsDangerousConfirmation,
): string {
  switch (confirmation.kind) {
    case "change-api-address":
      return settingsText(locale, "confirmation.changeApiAddress", {
        apiBaseUrl: confirmation.apiBaseUrl,
      });
    case "change-payment-settings":
      return settingsText(locale, "confirmation.changePaymentSettings");
    case "reset-catalog":
      return settingsText(locale, "confirmation.resetCatalog");
    case "reregister-device":
      return settingsText(locale, "confirmation.reregisterDevice", {
        targetStoreCode: confirmation.targetStoreCode,
      });
    default:
      return settingsText(locale, "confirmation.restartApp");
  }
}

function statusCopy(
  locale: SettingsLocale,
  statusCode: SettingsStatusCode,
): string {
  return settingsText(locale, `status.${statusCode}` as SettingsCopyKey);
}

function isSuccessStatus(statusCode: SettingsStatusCode): boolean {
  return [
    "api-address-saved",
    "app-restart-requested",
    "app-update-checked",
    "catalog-downloaded",
    "catalog-reset",
    "device-reregister-started",
    "display-setting-saved",
    "display-test-passed",
    "payment-settings-saved",
    "payment-test-passed",
    "printer-connected",
    "printer-scan-finished",
    "printer-settings-saved",
    "printer-test-passed",
    "scanner-test-passed",
  ].includes(statusCode);
}

function compactDate(value: string | null): string {
  if (!value) return "—";
  const parsed = new Date(value);
  return Number.isNaN(parsed.valueOf())
    ? value
    : parsed.toISOString().replace("T", " ").slice(0, 16);
}

const styles = StyleSheet.create({
  safeArea: { flex: 1, backgroundColor: posColors.canvas },
  page: { flex: 1, paddingHorizontal: 30, paddingVertical: 22 },
  header: {
    alignItems: "flex-start",
    flexDirection: "row",
    gap: 24,
    justifyContent: "space-between",
    marginBottom: 18,
  },
  titleGroup: { flex: 1, maxWidth: 920 },
  eyebrow: {
    color: posColors.blue,
    fontSize: 12,
    fontWeight: "800",
    letterSpacing: 1.1,
  },
  title: {
    color: posColors.ink,
    fontSize: 30,
    fontWeight: "800",
    marginTop: 5,
  },
  subtitle: {
    color: posColors.mutedInk,
    fontSize: 15,
    lineHeight: 22,
    marginTop: 5,
  },
  workspace: {
    flex: 1,
    flexDirection: "row",
    gap: 20,
    minHeight: 360,
  },
  navigation: {
    backgroundColor: posColors.surface,
    borderColor: posColors.border,
    borderRadius: 12,
    borderWidth: 1,
    padding: 12,
    width: 230,
  },
  navigationTitle: {
    color: posColors.mutedInk,
    fontSize: 12,
    fontWeight: "800",
    letterSpacing: 0.8,
    marginBottom: 8,
    paddingHorizontal: 6,
  },
  contentScroll: { flex: 1 },
  content: { gap: 14, paddingBottom: 28 },
  navButton: {
    alignItems: "flex-start",
    backgroundColor: "transparent",
    borderColor: "transparent",
    marginTop: 4,
  },
  deviceBadge: {
    backgroundColor: posColors.blueSoft,
    borderRadius: 8,
    marginTop: "auto",
    padding: 12,
  },
  badgeLabel: {
    color: posColors.mutedInk,
    fontSize: 11,
    fontWeight: "700",
  },
  badgeValue: {
    color: posColors.ink,
    fontSize: 17,
    fontWeight: "800",
    marginTop: 4,
  },
  badgeMeta: {
    color: posColors.blue,
    fontSize: 13,
    fontWeight: "700",
    marginTop: 2,
  },
  paneHeading: { marginBottom: 14 },
  paneTitle: { color: posColors.ink, fontSize: 25, fontWeight: "800" },
  paneSubtitle: {
    color: posColors.mutedInk,
    fontSize: 15,
    lineHeight: 22,
    marginTop: 6,
  },
  sectionCard: {
    backgroundColor: posColors.surface,
    borderColor: posColors.border,
    borderRadius: 12,
    borderWidth: 1,
    marginBottom: 14,
    padding: 20,
  },
  columnCard: { flex: 1, marginBottom: 0 },
  cardEyebrow: {
    color: posColors.blue,
    fontSize: 11,
    fontWeight: "800",
    letterSpacing: 0.9,
  },
  cardTitle: {
    color: posColors.ink,
    fontSize: 20,
    fontWeight: "800",
    marginBottom: 14,
    marginTop: 4,
  },
  sectionCopy: {
    color: posColors.mutedInk,
    fontSize: 15,
    lineHeight: 22,
    marginBottom: 14,
  },
  twoColumn: { flexDirection: "row", gap: 14 },
  actionRow: {
    alignItems: "center",
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 10,
    marginTop: 12,
  },
  metricRow: { flexDirection: "row", gap: 10 },
  metric: {
    backgroundColor: posColors.blueSoft,
    borderRadius: 8,
    flex: 1,
    minWidth: 130,
    padding: 12,
  },
  metricLabel: {
    color: posColors.mutedInk,
    fontSize: 11,
    fontWeight: "700",
  },
  metricValue: {
    color: posColors.ink,
    fontSize: 16,
    fontWeight: "800",
    marginTop: 4,
  },
  fieldLabel: {
    color: posColors.mutedInk,
    fontSize: 13,
    fontWeight: "700",
    marginBottom: 5,
    marginTop: 9,
  },
  textInput: {
    backgroundColor: "#FAFAF8",
    borderColor: posColors.border,
    borderRadius: 8,
    borderWidth: 1,
    color: posColors.ink,
    fontSize: 16,
    minHeight: SETTINGS_MIN_TOUCH_TARGET,
    paddingHorizontal: 13,
    paddingVertical: 9,
  },
  button: {
    alignItems: "center",
    alignSelf: "flex-start",
    backgroundColor: posColors.orange,
    borderColor: posColors.orange,
    borderRadius: 8,
    borderWidth: 1,
    justifyContent: "center",
    marginTop: 12,
    minHeight: SETTINGS_MIN_TOUCH_TARGET,
    paddingHorizontal: 17,
    paddingVertical: 9,
  },
  compactButton: {
    marginTop: 0,
    minWidth: 110,
    paddingHorizontal: 12,
  },
  buttonLabel: {
    color: "#FFFFFF",
    fontSize: 14,
    fontWeight: "800",
    textAlign: "center",
  },
  secondaryButton: {
    backgroundColor: posColors.surface,
    borderColor: posColors.border,
  },
  quietButton: {
    backgroundColor: "transparent",
    borderColor: posColors.border,
  },
  dangerButton: {
    backgroundColor: posColors.red,
    borderColor: posColors.red,
  },
  secondaryButtonLabel: { color: posColors.ink },
  selectedButton: {
    backgroundColor: posColors.blueSoft,
    borderColor: posColors.blue,
  },
  selectedButtonLabel: { color: posColors.blue },
  disabledButton: { opacity: 0.42 },
  pressedButton: { opacity: 0.78 },
  safetyNote: {
    color: posColors.green,
    fontSize: 13,
    fontWeight: "700",
    lineHeight: 20,
    marginTop: 12,
  },
  availability: {
    alignSelf: "flex-start",
    borderRadius: 999,
    marginBottom: 10,
    paddingHorizontal: 11,
    paddingVertical: 6,
  },
  availabilityReady: { backgroundColor: posColors.greenSoft },
  availabilityUnavailable: { backgroundColor: posColors.redSoft },
  availabilityText: {
    color: posColors.ink,
    fontSize: 12,
    fontWeight: "800",
  },
  deviceRow: {
    alignItems: "center",
    borderColor: posColors.border,
    borderTopWidth: 1,
    flexDirection: "row",
    gap: 12,
    justifyContent: "space-between",
    marginTop: 12,
    paddingTop: 12,
  },
  deviceIdentity: { flex: 1 },
  deviceName: { color: posColors.ink, fontSize: 15, fontWeight: "800" },
  deviceMeta: {
    color: posColors.mutedInk,
    fontSize: 12,
    marginTop: 3,
  },
  hardwareGrid: { flexDirection: "row", gap: 14, marginBottom: 14 },
  hardwareCard: {
    backgroundColor: posColors.surface,
    borderColor: posColors.border,
    borderRadius: 12,
    borderWidth: 1,
    flex: 1,
    padding: 18,
  },
  hardwareTitle: {
    color: posColors.ink,
    fontSize: 18,
    fontWeight: "800",
  },
  hardwareStatus: {
    fontSize: 14,
    fontWeight: "700",
    marginBottom: 8,
    marginTop: 8,
  },
  hardwareStatusReady: { color: posColors.green },
  hardwareStatusDisconnected: { color: posColors.red },
  hardwareStatusUnavailable: { color: posColors.mutedInk },
  scannerValue: {
    color: posColors.ink,
    fontFamily: "Courier",
    fontSize: 24,
    fontWeight: "800",
  },
  confirmation: {
    alignItems: "center",
    backgroundColor: "#FFF3F0",
    borderColor: posColors.red,
    borderRadius: 12,
    borderWidth: 1,
    flexDirection: "row",
    gap: 20,
    maxWidth: 980,
    padding: 18,
    shadowColor: "#000000",
    shadowOffset: { height: 5, width: 0 },
    shadowOpacity: 0.18,
    shadowRadius: 10,
    width: "90%",
  },
  confirmationOverlay: {
    alignItems: "center",
    backgroundColor: "rgba(16, 37, 58, 0.45)",
    flex: 1,
    justifyContent: "center",
    padding: 24,
  },
  confirmationCopy: { flex: 1 },
  confirmationTitle: {
    color: posColors.red,
    fontSize: 18,
    fontWeight: "800",
  },
  confirmationBody: {
    color: posColors.ink,
    fontSize: 13,
    lineHeight: 19,
    marginTop: 6,
  },
  confirmationActions: {
    alignItems: "center",
    flexDirection: "row",
    gap: 10,
  },
  statusBanner: {
    alignItems: "center",
    borderRadius: 8,
    flexDirection: "row",
    gap: 8,
    marginBottom: 14,
    paddingHorizontal: 14,
    paddingVertical: 10,
  },
  statusSuccess: { backgroundColor: posColors.greenSoft },
  statusWarning: { backgroundColor: posColors.redSoft },
  statusText: {
    color: posColors.ink,
    flex: 1,
    fontSize: 13,
    fontWeight: "700",
  },
  statusCode: {
    color: posColors.mutedInk,
    fontFamily: "Courier",
    fontSize: 11,
  },
  emptyPanel: {
    alignItems: "center",
    backgroundColor: posColors.surface,
    borderColor: posColors.border,
    borderRadius: 12,
    borderWidth: 1,
    flex: 1,
    justifyContent: "center",
    minHeight: 320,
    padding: 28,
  },
  emptyText: {
    color: posColors.mutedInk,
    fontSize: 18,
    fontWeight: "700",
    textAlign: "center",
  },
  unavailable: {
    alignItems: "center",
    flex: 1,
    justifyContent: "center",
    padding: 40,
  },
  unavailableTitle: {
    color: posColors.ink,
    fontSize: 28,
    fontWeight: "800",
    marginTop: 10,
    textAlign: "center",
  },
  unavailableHint: {
    color: posColors.mutedInk,
    fontSize: 16,
    lineHeight: 24,
    marginTop: 10,
    maxWidth: 680,
    textAlign: "center",
  },
});
