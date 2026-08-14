import { useEffect, useRef, useState } from "react";
import {
  ActivityIndicator,
  StyleSheet,
  Text,
  View,
} from "react-native";

import {
  PosKeyboardAwareScrollView,
  PosKeyboardAwareTextInput,
} from "@/ui/controls/pos-keyboard-aware-scroll-view";
import { PosPressable } from "@/ui/controls/pos-pressable";
import { posColors } from "@/ui/theme";

const MIN_TOUCH_TARGET = 48;

type ConnectionState =
  | "idle"
  | "testing"
  | "passed"
  | "failed"
  | "saving"
  | "saved"
  | "save-failed";

export type ServerConnectionPanelCopy = Readonly<{
  title: string;
  eyebrow: string;
  currentAddress: string;
  edit: string;
  addressLabel: string;
  addressPlaceholder: string;
  test: string;
  testing: string;
  save: string;
  cancel: string;
  confirm: string;
  confirmationTitle: string;
  confirmationHint: string;
  emptyAddress: string;
  testPassed: string;
  testFailed: string;
  saveBlocked: string;
  saving: string;
  saved: string;
  saveFailed: string;
}>;

const DEFAULT_COPY: ServerConnectionPanelCopy = Object.freeze({
  title: "服务器连接",
  eyebrow: "网络检查",
  currentAddress: "当前服务器",
  edit: "修改",
  addressLabel: "服务器地址",
  addressPlaceholder: "https://example.com/pos-api",
  test: "测试连接",
  testing: "正在检查",
  save: "保存地址",
  cancel: "取消",
  confirm: "确认保存",
  confirmationTitle: "确认切换服务器？",
  confirmationHint: "应用将保存已测试的地址，并按安全流程重新载入。",
  emptyAddress: "请输入服务器地址。",
  testPassed: "连接成功，可以保存此地址。",
  testFailed: "连接失败，请检查地址和网络后重试。",
  saveBlocked: "本机账本无法检查，暂不能切换服务器。",
  saving: "正在保存服务器地址…",
  saved: "服务器地址已保存。",
  saveFailed: "保存失败，当前服务器地址未改变。",
});

export type ServerConnectionPanelProps = Readonly<{
  canSave: boolean;
  copy?: Partial<ServerConnectionPanelCopy>;
  currentAddress: string;
  saveAddress(address: string): Promise<void>;
  testAddress(address: string): Promise<boolean>;
}>;

/**
 * 预登录页只负责编辑与呈现；地址校验、健康检查和账本安全门禁均由注入端口负责。
 */
export function ServerConnectionPanel({
  canSave,
  copy: copyOverride,
  currentAddress,
  saveAddress,
  testAddress,
}: ServerConnectionPanelProps) {
  const copy = { ...DEFAULT_COPY, ...copyOverride };
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState(currentAddress);
  const [state, setState] = useState<ConnectionState>("idle");
  const [testedAddress, setTestedAddress] = useState<string | null>(null);
  const [confirmationVisible, setConfirmationVisible] = useState(false);
  const requestGeneration = useRef(0);

  useEffect(() => {
    if (!editing) setDraft(currentAddress);
  }, [currentAddress, editing]);

  useEffect(
    () => () => {
      requestGeneration.current += 1;
    },
    [],
  );

  const candidate = draft.trim();
  const busy = state === "testing" || state === "saving";
  const saveEnabled =
    canSave &&
    !busy &&
    state === "passed" &&
    testedAddress === candidate;

  const updateDraft = (value: string) => {
    requestGeneration.current += 1;
    setDraft(value);
    setState("idle");
    setTestedAddress(null);
    setConfirmationVisible(false);
  };

  const runTest = async () => {
    if (!candidate || busy) {
      if (!candidate) setState("failed");
      return;
    }
    const generation = ++requestGeneration.current;
    setConfirmationVisible(false);
    setTestedAddress(null);
    setState("testing");
    try {
      const reachable = await testAddress(candidate);
      if (generation !== requestGeneration.current) return;
      setTestedAddress(reachable ? candidate : null);
      setState(reachable ? "passed" : "failed");
    } catch {
      if (generation !== requestGeneration.current) return;
      setTestedAddress(null);
      setState("failed");
    }
  };

  const confirmSave = async () => {
    if (!saveEnabled) return;
    const generation = ++requestGeneration.current;
    setConfirmationVisible(false);
    setState("saving");
    try {
      await saveAddress(candidate);
      if (generation !== requestGeneration.current) return;
      setState("saved");
    } catch {
      if (generation !== requestGeneration.current) return;
      setState("save-failed");
    }
  };

  return (
    <PosKeyboardAwareScrollView
      contentContainerStyle={styles.panelContent}
      nestedScrollEnabled
      style={styles.panel}
      testID="server-connection-panel"
    >
      <View style={styles.headerRow}>
        <View style={styles.headingCopy}>
          <Text style={styles.eyebrow}>{copy.eyebrow}</Text>
          <Text style={styles.title}>{copy.title}</Text>
        </View>
        {!editing ? (
          <PanelButton
            label={copy.edit}
            onPress={() => {
              setDraft(currentAddress);
              setState("idle");
              setTestedAddress(null);
              setEditing(true);
            }}
            testID="server-connection-edit"
            tone="secondary"
          />
        ) : null}
      </View>

      <View style={styles.currentAddressRow}>
        <Text style={styles.currentAddressLabel}>{copy.currentAddress}</Text>
        <Text
          numberOfLines={2}
          selectable
          style={styles.currentAddress}
          testID="server-connection-current"
        >
          {currentAddress}
        </Text>
      </View>

      {editing ? (
        <View style={styles.editor}>
          <Text style={styles.fieldLabel}>{copy.addressLabel}</Text>
          <PosKeyboardAwareTextInput
            accessibilityLabel={copy.addressLabel}
            autoCapitalize="none"
            autoCorrect={false}
            editable={!busy && !confirmationVisible}
            onChangeText={updateDraft}
            placeholder={copy.addressPlaceholder}
            placeholderTextColor="#7B8793"
            style={styles.input}
            testID="server-connection-input"
            value={draft}
          />

          <View style={styles.actionRow}>
            <PanelButton
              disabled={!candidate || busy || confirmationVisible}
              label={state === "testing" ? copy.testing : copy.test}
              loading={state === "testing"}
              onPress={() => void runTest()}
              testID="server-connection-test"
              tone="secondary"
            />
            <PanelButton
              disabled={!saveEnabled}
              label={copy.save}
              onPress={() => setConfirmationVisible(true)}
              testID="server-connection-save"
            />
          </View>

          {!canSave ? (
            <Text
              accessibilityRole="alert"
              style={styles.blockedText}
              testID="server-connection-save-disabled-reason"
            >
              {copy.saveBlocked}
            </Text>
          ) : null}

          {statusText(state, candidate, copy) ? (
            <Text
              accessibilityRole="alert"
              style={[
                styles.statusText,
                (state === "failed" || state === "save-failed") &&
                  styles.statusError,
                (state === "passed" || state === "saved") &&
                  styles.statusSuccess,
              ]}
              testID="server-connection-status"
            >
              {statusText(state, candidate, copy)}
            </Text>
          ) : null}

          {confirmationVisible ? (
            <View
              accessibilityRole="alert"
              style={styles.confirmation}
              testID="server-connection-confirmation"
            >
              <Text style={styles.confirmationTitle}>
                {copy.confirmationTitle}
              </Text>
              <Text style={styles.confirmationHint}>
                {copy.confirmationHint}
              </Text>
              <Text numberOfLines={2} style={styles.confirmationAddress}>
                {candidate}
              </Text>
              <View style={styles.confirmationActions}>
                <PanelButton
                  label={copy.cancel}
                  onPress={() => setConfirmationVisible(false)}
                  testID="server-connection-cancel"
                  tone="secondary"
                />
                <PanelButton
                  label={copy.confirm}
                  onPress={() => void confirmSave()}
                  testID="server-connection-confirm"
                />
              </View>
            </View>
          ) : null}
        </View>
      ) : null}
    </PosKeyboardAwareScrollView>
  );
}

function statusText(
  state: ConnectionState,
  candidate: string,
  copy: ServerConnectionPanelCopy,
): string | null {
  switch (state) {
    case "testing":
      return copy.testing;
    case "passed":
      return copy.testPassed;
    case "failed":
      return candidate ? copy.testFailed : copy.emptyAddress;
    case "saving":
      return copy.saving;
    case "saved":
      return copy.saved;
    case "save-failed":
      return copy.saveFailed;
    case "idle":
      return null;
  }
}

function PanelButton({
  disabled = false,
  label,
  loading = false,
  onPress,
  testID,
  tone = "primary",
}: Readonly<{
  disabled?: boolean;
  label: string;
  loading?: boolean;
  onPress(): void;
  testID: string;
  tone?: "primary" | "secondary";
}>) {
  return (
    <PosPressable
      accessibilityRole="button"
      accessibilityState={{ disabled }}
      disabled={disabled}
      onPress={onPress}
      style={({ pressed }) => [
        styles.button,
        tone === "secondary" && styles.secondaryButton,
        disabled && styles.buttonDisabled,
        pressed && !disabled && styles.buttonPressed,
      ]}
      testID={testID}
    >
      {loading ? (
        <ActivityIndicator color={posColors.blue} size="small" />
      ) : null}
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

const styles = StyleSheet.create({
  panel: {
    backgroundColor: posColors.surface,
    borderColor: posColors.border,
    borderRadius: 2,
    borderWidth: 1,
  },
  panelContent: {
    gap: 14,
    padding: 18,
  },
  headerRow: {
    alignItems: "center",
    flexDirection: "row",
    gap: 16,
    justifyContent: "space-between",
  },
  headingCopy: {
    flex: 1,
    gap: 2,
  },
  eyebrow: {
    color: posColors.orange,
    fontSize: 11,
    fontWeight: "800",
    letterSpacing: 0.8,
    textTransform: "uppercase",
  },
  title: {
    color: posColors.ink,
    fontSize: 20,
    fontWeight: "800",
  },
  currentAddressRow: {
    alignItems: "flex-start",
    backgroundColor: posColors.canvas,
    borderColor: posColors.border,
    borderLeftColor: posColors.blue,
    borderLeftWidth: 3,
    borderWidth: 1,
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
    minHeight: MIN_TOUCH_TARGET,
    paddingHorizontal: 12,
    paddingVertical: 10,
  },
  currentAddressLabel: {
    color: posColors.mutedInk,
    fontSize: 13,
    fontWeight: "700",
    minWidth: 82,
  },
  currentAddress: {
    color: posColors.ink,
    flex: 1,
    fontSize: 14,
    fontWeight: "600",
    minWidth: 220,
  },
  editor: {
    gap: 10,
  },
  fieldLabel: {
    color: posColors.ink,
    fontSize: 13,
    fontWeight: "700",
  },
  input: {
    backgroundColor: posColors.surface,
    borderColor: posColors.border,
    borderRadius: 2,
    borderWidth: 1,
    color: posColors.ink,
    fontSize: 15,
    minHeight: MIN_TOUCH_TARGET,
    paddingHorizontal: 12,
    paddingVertical: 9,
  },
  actionRow: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 10,
  },
  button: {
    alignItems: "center",
    backgroundColor: posColors.blue,
    borderColor: posColors.blue,
    borderRadius: 2,
    borderWidth: 1,
    flexDirection: "row",
    gap: 8,
    justifyContent: "center",
    minHeight: MIN_TOUCH_TARGET,
    minWidth: 116,
    paddingHorizontal: 16,
    paddingVertical: 10,
  },
  secondaryButton: {
    backgroundColor: posColors.surface,
  },
  buttonDisabled: {
    opacity: 0.45,
  },
  buttonPressed: {
    opacity: 0.76,
  },
  buttonLabel: {
    color: "#FFFFFF",
    fontSize: 14,
    fontWeight: "800",
  },
  secondaryButtonLabel: {
    color: posColors.blue,
  },
  blockedText: {
    color: posColors.yellow,
    fontSize: 13,
    fontWeight: "700",
  },
  statusText: {
    color: posColors.mutedInk,
    fontSize: 13,
    fontWeight: "700",
  },
  statusError: {
    color: posColors.red,
  },
  statusSuccess: {
    color: posColors.green,
  },
  confirmation: {
    backgroundColor: posColors.blueSoft,
    borderColor: posColors.blue,
    borderLeftWidth: 3,
    borderWidth: 1,
    gap: 7,
    padding: 14,
  },
  confirmationTitle: {
    color: posColors.ink,
    fontSize: 15,
    fontWeight: "800",
  },
  confirmationHint: {
    color: posColors.mutedInk,
    fontSize: 13,
    lineHeight: 19,
  },
  confirmationAddress: {
    color: posColors.ink,
    fontSize: 13,
    fontWeight: "700",
  },
  confirmationActions: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 10,
    justifyContent: "flex-end",
    marginTop: 4,
  },
});
