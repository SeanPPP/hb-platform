import { useEffect, useSyncExternalStore } from "react";
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
  onBack?(): void;
  presenter: SettingsScreenPresenter;
  scanner?: HidScannerRouter;
}>;

const NAV_ITEMS: readonly Readonly<{
  label: string;
  pane: SettingsPane;
}>[] = [
  { pane: "general", label: "系统与目录 / System" },
  { pane: "payments", label: "支付终端 / Payments" },
  { pane: "peripherals", label: "外设 / Peripherals" },
  { pane: "device", label: "设备注册 / Device" },
  { pane: "hardware", label: "硬件测试 / Hardware test" },
];

/**
 * iPad 横屏设置台保持清晰的左侧分区与右侧工作区。危险操作不使用平台 Alert，
 * 而在同一屏显示完整影响范围，确保自动化、键盘与触控都走同一确认路径。
 */
export function SettingsScreen({
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
            <Text style={styles.eyebrow}>终端管理 / TERMINAL ADMIN</Text>
            <Text style={styles.title}>设置 / Settings</Text>
            <Text style={styles.subtitle}>
              管理公开运行参数与本机外设；支付凭据和设备密钥始终留在受保护服务中。
              / Manage public runtime choices and local peripherals; protected
              credentials stay on the service.
            </Text>
          </View>
          {onBack ? (
            <ActionButton
              disabled={interactionLocked}
              label="返回销售页 / Back to sales"
              onPress={onBack}
              testID="settings-back"
              tone="quiet"
            />
          ) : null}
        </View>

        {state.statusCode ? (
          <StatusBanner statusCode={state.statusCode} />
        ) : null}

        <View style={styles.workspace} testID="settings-workspace">
          <View
            pointerEvents={state.confirmation ? "none" : "auto"}
            style={styles.navigation}
          >
            <Text style={styles.navigationTitle}>设置分区 / Sections</Text>
            {NAV_ITEMS.map((item) => (
              <ActionButton
                disabled={interactionLocked}
                key={item.pane}
                label={item.label}
                onPress={() => presenter.selectPane(item.pane)}
                selected={state.activePane === item.pane}
                testID={`settings-nav-${item.pane}`}
                tone="nav"
              />
            ))}
            <View style={styles.deviceBadge}>
              <Text style={styles.badgeLabel}>当前终端 / Terminal</Text>
              <Text style={styles.badgeValue}>
                {state.device.deviceCode || "—"}
              </Text>
              <Text style={styles.badgeMeta}>
                {state.device.storeCode || "未绑定 / Unbound"}
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
                message="正在读取本机设置… / Loading terminal settings…"
                testID="settings-loading"
              />
            ) : null}
            {state.kind === "failed" ? (
              <EmptyPanel
                message="本机设置暂不可读 / Terminal settings unavailable"
                testID="settings-failed"
              />
            ) : null}
            {state.kind === "unauthorized" ? (
              <EmptyPanel
                message="没有查看设置权限 / Settings permission required"
                testID="settings-unauthorized"
              />
            ) : null}
            {state.kind === "ready" ? (
              <SettingsPaneContent presenter={presenter} state={state} />
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
  onBack,
}: Readonly<{ onBack(): void }>) {
  return (
    <SafeAreaView style={styles.safeArea} testID="settings-runtime-unavailable">
      <View style={styles.unavailable}>
        <Text style={styles.eyebrow}>TERMINAL SETTINGS</Text>
        <Text style={styles.unavailableTitle}>
          设置服务暂不可用 / Settings unavailable
        </Text>
        <Text style={styles.unavailableHint}>
          本机运行时尚未接入设置适配器，未执行任何配置或数据变更。 / The
          settings adapter is not connected; no configuration or local data was
          changed.
        </Text>
        <ActionButton
          label="返回销售页 / Back to sales"
          onPress={onBack}
          testID="settings-unavailable-back"
        />
      </View>
    </SafeAreaView>
  );
}

function SettingsPaneContent({
  presenter,
  state,
}: Readonly<{
  presenter: SettingsScreenPresenter;
  state: SettingsState;
}>) {
  switch (state.activePane) {
    case "payments":
      return <PaymentsPane presenter={presenter} state={state} />;
    case "peripherals":
      return <PeripheralsPane presenter={presenter} state={state} />;
    case "device":
      return <DevicePane presenter={presenter} state={state} />;
    case "hardware":
      return <HardwarePane presenter={presenter} state={state} />;
    default:
      return <GeneralPane presenter={presenter} state={state} />;
  }
}

function GeneralPane({
  presenter,
  state,
}: Readonly<{
  presenter: SettingsScreenPresenter;
  state: SettingsState;
}>) {
  const locked = state.busy || state.confirmation !== null;
  return (
    <View testID="settings-pane-content-general">
      <PaneHeading
        subtitle="切换服务地址会改变数据分区，必须确认且本机无待处理交易。/ Endpoint changes require confirmation and a clear local queue."
        title="系统与商品目录 / System & catalog"
      />
      <SectionCard eyebrow="NETWORK" title="API 地址 / API address">
        <TextInput
          accessibilityLabel="API 地址 / API address"
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
          label="检查并申请切换 / Review change"
          onPress={() => presenter.requestApiAddressChange()}
          testID="settings-api-request-change"
        />
      </SectionCard>

      <SectionCard eyebrow="CATALOG" title="商品目录 / Catalog">
        <View style={styles.metricRow}>
          <Metric
            label="快照 / Snapshot"
            value={state.catalog.snapshotId ?? "无 / None"}
          />
          <Metric
            label="商品数 / Items"
            value={String(state.catalog.itemCount)}
          />
          <Metric
            label="启用时间 / Activated"
            value={compactDate(state.catalog.activatedAt)}
          />
        </View>
        <View style={styles.actionRow}>
          <ActionButton
            disabled={locked || !state.access.canDownloadCatalog}
            label="下载并安全启用 / Download"
            onPress={() => void presenter.downloadCatalog()}
            testID="settings-catalog-download"
          />
          <ActionButton
            disabled={locked || !state.access.canResetCatalog}
            label="申请重置 / Review reset"
            onPress={() => presenter.requestCatalogReset()}
            testID="settings-catalog-reset"
            tone="danger"
          />
        </View>
        <Text style={styles.safetyNote}>
          下载失败时保留当前已验证目录；重置前不会清除待同步销售或退款。 / A
          failed download keeps the active catalog; reset never clears queued
          sales or returns.
        </Text>
      </SectionCard>

      <SectionCard eyebrow="UPDATES" title="应用更新 / App update">
        <View style={styles.metricRow}>
          <Metric label="渠道 / Channel" value={state.appUpdate.channel} />
          <Metric
            label="当前版本 / Current"
            value={state.appUpdate.currentVersion || "—"}
          />
          <Metric
            label="可用版本 / Available"
            value={state.appUpdate.availableVersion ?? "已是最新 / Current"}
          />
        </View>
        <View style={styles.actionRow}>
          <ActionButton
            disabled={locked || !state.access.canManageAppUpdate}
            label="检查更新 / Check"
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
            label="申请安全重启 / Review restart"
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
  presenter,
  state,
}: Readonly<{
  presenter: SettingsScreenPresenter;
  state: SettingsState;
}>) {
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
        subtitle="这里只保存公开的环境、门店和终端选择；受保护支付凭据由服务端管理。/ Only public environment and terminal choices are stored here."
        title="支付终端 / Payment terminals"
      />
      <SectionCard
        eyebrow="ACTIVE CARD TERMINAL"
        title="刷卡终端提供方 / Card terminal provider"
      >
        <Text style={styles.sectionCopy}>
          必须明确选择一个终端提供方；未选择或所选终端不可用时，银行卡支付保持关闭。
          / Select exactly one terminal provider; card payments remain disabled
          when no available provider is selected.
        </Text>
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
            ? "未选择：银行卡支付已关闭 / Not selected: card payments disabled"
            : state.paymentProviderDraft === "square"
              ? "已选择 Square / Square selected"
              : "已选择 Linkly / Linkly selected"}
        </Text>
      </SectionCard>
      <View style={styles.twoColumn}>
        <SectionCard
          eyebrow="CARD TERMINAL"
          style={styles.columnCard}
          title="Square"
        >
          <Availability
            available={squareAvailable}
            blockerCode={state.square.blockerCode}
          />
          <EnvironmentSelector
            disabled={squareDisabled}
            environment={state.squareDraft.environment}
            onSelect={(environment) =>
              presenter.setSquareEnvironment(environment)
            }
            prefix="settings-square"
          />
          <FieldLabel label="门店位置 ID / Location ID" />
          <TextInput
            accessibilityLabel="Square 门店位置 ID / Square location ID"
            autoCapitalize="none"
            autoCorrect={false}
            editable={!squareDisabled}
            onChangeText={(value) => presenter.setSquareLocationId(value)}
            style={styles.textInput}
            testID="settings-square-location"
            value={state.squareDraft.locationId}
          />
          <FieldLabel label="终端设备 ID / Device ID" />
          <TextInput
            accessibilityLabel="Square 终端设备 ID / Square device ID"
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
            label="测试可用性 / Test"
            onPress={() => void presenter.testPaymentProvider("square")}
            testID="settings-square-test"
            tone="secondary"
          />
        </SectionCard>

        <SectionCard eyebrow="EFTPOS" style={styles.columnCard} title="Linkly">
          <Availability
            available={linklyAvailable}
            blockerCode={state.linkly.blockerCode}
          />
          <EnvironmentSelector
            disabled={linklyDisabled}
            environment={state.linklyDraft.environment}
            onSelect={(environment) =>
              presenter.setLinklyEnvironment(environment)
            }
            prefix="settings-linkly"
          />
          <Text style={styles.sectionCopy}>
            iPad 使用后端异步 Linkly 通道。商户与 POS
            认证材料不会进入本机普通设置。 / iPad uses the backend asynchronous
            Linkly channel; merchant authentication material is not stored here.
          </Text>
          <ActionButton
            disabled={linklyDisabled}
            label="测试可用性 / Test"
            onPress={() => void presenter.testPaymentProvider("linkly")}
            testID="settings-linkly-test"
            tone="secondary"
          />
        </SectionCard>
      </View>
      <ActionButton
        disabled={disabled}
        label="保存支付终端选择 / Save payment choices"
        onPress={() => void presenter.savePaymentSettings()}
        testID="settings-payment-save"
      />
    </View>
  );
}

function PeripheralsPane({
  presenter,
  state,
}: Readonly<{
  presenter: SettingsScreenPresenter;
  state: SettingsState;
}>) {
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
        subtitle="发现、连接并验证本机打印机、扫描器与独立客显。/ Discover, connect and verify local peripherals."
        title="外设 / Peripherals"
      />
      <SectionCard eyebrow="RECEIPT" title="小票打印机 / Printer">
        <View style={styles.actionRow}>
          <ToggleButton
            disabled={printerDisabled}
            label="打印 / Printing"
            onPress={() =>
              presenter.setPrinterEnabled(!state.printer.printEnabled)
            }
            selected={state.printer.printEnabled}
            testID="settings-printer-enabled"
          />
          <ToggleButton
            disabled={printerDisabled}
            label="钱箱 / Drawer"
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
            label={state.printer.locale}
            onPress={() =>
              presenter.setPrinterLocale(
                state.printer.locale === "en" ? "zh-CN" : "en",
              )
            }
            selected
            testID="settings-printer-locale"
          />
        </View>
        <FieldLabel label="设备 ID / Peripheral ID" />
        <TextInput
          accessibilityLabel="打印机设备 ID / Printer peripheral ID"
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
            label="扫描打印机 / Scan"
            onPress={() => void presenter.scanPrinters()}
            testID="settings-printer-scan"
            tone="secondary"
          />
          <ActionButton
            disabled={printerDisabled}
            label="保存 / Save"
            onPress={() => void presenter.savePrinterSettings()}
            testID="settings-printer-save"
          />
          <ActionButton
            disabled={printerDisabled}
            label="测试打印 / Test"
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
              label="连接 / Connect"
              onPress={() => void presenter.connectPrinter(device.id)}
              testID={`settings-printer-connect-${device.id}`}
            />
          </View>
        ))}
      </SectionCard>

      <View style={styles.twoColumn}>
        <SectionCard
          eyebrow="SCANNER"
          style={styles.columnCard}
          title="扫描器 / Scanner"
        >
          <Text style={styles.sectionCopy}>
            状态 / Status: {state.hardware.scannerStatus}
          </Text>
          <ActionButton
            disabled={
              state.busy ||
              state.confirmation !== null ||
              !state.access.canTestScanner
            }
            label="等待一次扫码 / Capture one scan"
            onPress={() => void presenter.testScanner()}
            testID="settings-scanner-test"
            tone="secondary"
          />
        </SectionCard>
        <SectionCard
          eyebrow="CUSTOMER DISPLAY"
          style={styles.columnCard}
          title="独立客显 / External display"
        >
          <Text style={styles.sectionCopy}>
            状态 / Status: {state.externalDisplay.status}
          </Text>
          <View style={styles.actionRow}>
            <ToggleButton
              disabled={displayDisabled || !state.externalDisplay.available}
              label="启用 / Enabled"
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
              label="测试画面 / Test"
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
  presenter,
  state,
}: Readonly<{
  presenter: SettingsScreenPresenter;
  state: SettingsState;
}>) {
  const disabled =
    state.busy ||
    state.confirmation !== null ||
    !state.access.canReregisterDevice;
  return (
    <View testID="settings-pane-content-device">
      <PaneHeading
        subtitle="重新注册只改变可信设备绑定，不会删除本地待同步业务记录。/ Re-registration changes the trusted binding without deleting queued business records."
        title="设备注册 / Device registration"
      />
      <SectionCard eyebrow="CURRENT BINDING" title="当前绑定 / Current">
        <View style={styles.metricRow}>
          <Metric label="门店 / Store" value={state.device.storeCode || "—"} />
          <Metric
            label="门店名称 / Store name"
            value={state.device.storeName || "—"}
          />
          <Metric
            label="设备 / Device"
            value={state.device.deviceCode || "—"}
          />
        </View>
      </SectionCard>
      <SectionCard eyebrow="RE-REGISTER" title="重新注册 / Re-register">
        <FieldLabel label="目标门店代码 / Target store code" />
        <TextInput
          accessibilityLabel="目标门店代码 / Target store code"
          autoCapitalize="characters"
          autoCorrect={false}
          editable={!disabled}
          onChangeText={(value) => presenter.setReregisterStoreCode(value)}
          style={styles.textInput}
          testID="settings-reregister-store"
          value={state.reregisterStoreCode}
        />
        <FieldLabel label="终端名称 / Terminal name" />
        <TextInput
          accessibilityLabel="终端名称 / Terminal name"
          editable={!disabled}
          onChangeText={(value) => presenter.setTerminalName(value)}
          style={styles.textInput}
          testID="settings-reregister-terminal"
          value={state.terminalNameDraft}
        />
        <ActionButton
          disabled={disabled}
          label="检查并申请重新注册 / Review re-registration"
          onPress={() => presenter.requestDeviceReregistration()}
          testID="settings-reregister-request"
          tone="danger"
        />
      </SectionCard>
    </View>
  );
}

function HardwarePane({
  presenter,
  state,
}: Readonly<{
  presenter: SettingsScreenPresenter;
  state: SettingsState;
}>) {
  return (
    <View testID="settings-pane-content-hardware">
      <PaneHeading
        subtitle="逐项验证硬件链路；测试不会创建销售或支付。/ Verify each hardware path without creating a sale or payment."
        title="硬件测试 / Hardware test"
      />
      <View style={styles.hardwareGrid}>
        <HardwareCard
          actionLabel="打印测试小票 / Print test"
          disabled={
            state.busy ||
            state.confirmation !== null ||
            !state.access.canConfigurePrinter
          }
          onPress={() => void presenter.testPrinter()}
          status={state.hardware.printerStatus}
          testID="settings-hardware-printer"
          title="打印机 / Printer"
        />
        <HardwareCard
          actionLabel="等待一次扫码 / Capture scan"
          disabled={
            state.busy ||
            state.confirmation !== null ||
            !state.access.canTestScanner
          }
          onPress={() => void presenter.testScanner()}
          status={state.hardware.scannerStatus}
          testID="settings-hardware-scanner"
          title="扫描器 / Scanner"
        />
        <HardwareCard
          actionLabel="显示测试画面 / Show test"
          disabled={
            state.busy ||
            state.confirmation !== null ||
            !state.access.canManageCustomerDisplay ||
            !state.externalDisplay.available
          }
          onPress={() => void presenter.testExternalDisplay()}
          status={state.hardware.externalDisplayStatus}
          testID="settings-hardware-display"
          title="独立客显 / Display"
        />
      </View>
      <SectionCard eyebrow="LAST CAPTURE" title="最近扫码 / Last scan">
        <Text style={styles.scannerValue}>
          {state.hardware.lastScannerValue ?? "等待测试 / Waiting for test"}
        </Text>
      </SectionCard>
    </View>
  );
}

function ConfirmationCard({
  busy,
  confirmation,
  onCancel,
  onConfirm,
}: Readonly<{
  busy: boolean;
  confirmation: SettingsDangerousConfirmation;
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
          {confirmationTitle(confirmation)}
        </Text>
        <Text style={styles.confirmationBody}>
          确认前会再次检查活动购物车、待同步销售/退款、未决支付与耐久写入。
          待同步数据不会被清除；存在任何项目时操作会被阻断。 / Local pending
          data is never cleared; any pending work blocks this action.
        </Text>
      </View>
      <View style={styles.confirmationActions}>
        <ActionButton
          disabled={busy}
          label="取消 / Cancel"
          onPress={onCancel}
          testID="settings-confirm-cancel"
          tone="quiet"
        />
        <ActionButton
          disabled={busy}
          label={busy ? "检查中… / Checking…" : "确认执行 / Confirm"}
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
  onSelect,
  prefix,
}: Readonly<{
  disabled: boolean;
  environment: PaymentEnvironment;
  onSelect(environment: PaymentEnvironment): void;
  prefix: string;
}>) {
  return (
    <View style={styles.actionRow}>
      <ToggleButton
        disabled={disabled}
        label="生产 / Production"
        onPress={() => onSelect("Production")}
        selected={environment === "Production"}
        testID={`${prefix}-production`}
      />
      <ToggleButton
        disabled={disabled}
        label="沙盒 / Sandbox"
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
  testID,
  title,
}: Readonly<{
  actionLabel: string;
  disabled: boolean;
  onPress(): void;
  status: string;
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
        {status}
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
}: Readonly<{ available: boolean; blockerCode: string | null }>) {
  return (
    <View
      style={[
        styles.availability,
        available ? styles.availabilityReady : styles.availabilityUnavailable,
      ]}
    >
      <Text style={styles.availabilityText}>
        {available
          ? "运行时可用 / Runtime ready"
          : `运行时未配置 / Runtime unavailable${blockerCode ? ` · ${blockerCode}` : ""}`}
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
  statusCode,
}: Readonly<{ statusCode: SettingsStatusCode }>) {
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
      <Text style={styles.statusText}>{statusCopy(statusCode)}</Text>
      <Text style={styles.statusCode}>[{statusCode}]</Text>
    </View>
  );
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
  confirmation: SettingsDangerousConfirmation,
): string {
  switch (confirmation.kind) {
    case "change-api-address":
      return `切换 API 地址 / Change API address\n${confirmation.apiBaseUrl}`;
    case "change-payment-settings":
      return "切换支付终端配置 / Change payment terminal settings";
    case "reset-catalog":
      return "重置本地商品目录 / Reset local catalog";
    case "reregister-device":
      return `重新注册到 ${confirmation.targetStoreCode} / Re-register device`;
    default:
      return "安全重启应用 / Restart app safely";
  }
}

function statusCopy(statusCode: SettingsStatusCode): string {
  const copy: Record<SettingsStatusCode, string> = {
    "api-address-saved":
      "API 地址已保存；运行时必须按适配器指引重新建立。/ API address saved.",
    "api-health-check-failed":
      "候选 API 健康检查失败，旧地址保持不变 / Candidate API unavailable; previous address retained",
    "app-restart-requested": "已请求安全重启 / Safe restart requested",
    "app-update-check-failed": "更新检查失败 / Update check failed",
    "app-update-checked": "更新检查完成 / Update check complete",
    "catalog-download-failed":
      "目录下载失败，当前目录仍可用 / Download failed; current catalog remains active",
    "catalog-downloaded": "目录已下载并启用 / Catalog activated",
    "catalog-reset": "本地目录已重置 / Local catalog reset",
    "catalog-reset-failed": "目录重置失败 / Catalog reset failed",
    "device-reregister-failed":
      "重新注册未开始 / Re-registration did not start",
    "device-reregister-started":
      "设备重新注册已开始 / Device re-registration started",
    "display-setting-failed": "客显设置失败 / Display setting failed",
    "display-setting-saved": "客显设置已保存 / Display setting saved",
    "display-test-failed": "客显测试失败 / Display test failed",
    "display-test-passed": "客显测试已发送 / Display test sent",
    "invalid-api-address": "API 地址格式不安全 / Invalid API address",
    "invalid-device-registration":
      "请选择不同的有效门店 / Choose a different valid store",
    "load-failed": "读取设置失败 / Settings load failed",
    "payment-settings-invalid":
      "可用支付通道的公开配置不完整 / Public payment provider configuration is incomplete",
    "payment-settings-save-failed":
      "支付终端设置保存失败 / Payment settings save failed",
    "payment-settings-saved": "支付终端设置已保存 / Payment settings saved",
    "payment-test-failed": "支付通道测试失败 / Payment test failed",
    "payment-test-passed": "支付通道可用 / Payment provider available",
    "pending-local-data":
      "存在本地待处理业务，操作已阻断且数据保持不变 / Pending local work blocked the action",
    "permission-required": "需要相应设置权限 / Permission required",
    "printer-connect-failed": "打印机连接失败 / Printer connection failed",
    "printer-connected": "打印机已连接 / Printer connected",
    "printer-scan-failed": "打印机扫描失败 / Printer scan failed",
    "printer-scan-finished": "打印机扫描完成 / Printer scan complete",
    "printer-settings-save-failed":
      "打印机设置保存失败 / Printer settings save failed",
    "printer-settings-saved": "打印机设置已保存 / Printer settings saved",
    "printer-test-failed": "测试打印失败 / Test print failed",
    "printer-test-passed": "测试小票已发送 / Test receipt sent",
    "restart-failed": "重启或地址切换失败 / Restart or endpoint change failed",
    "safety-check-failed":
      "无法确认本地安全状态，操作已阻断 / Safety state unavailable; action blocked",
    "scanner-test-failed": "扫码测试失败 / Scanner test failed",
    "scanner-test-passed": "已收到扫码 / Scan received",
  };
  return copy[statusCode];
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
