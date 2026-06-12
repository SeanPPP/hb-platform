import { useRouter } from "expo-router";
import {
  View,
  StyleSheet,
  Animated,
  StatusBar,
  Dimensions,
  KeyboardAvoidingView,
  ScrollView,
  Platform,
  TouchableOpacity,
} from "react-native";
import { isAxiosError } from "axios";
import {
  TextInput,
  Button,
  Text,
  Checkbox,
  Snackbar,
  Portal,
  Modal,
} from "react-native-paper";
import { useState, useEffect, useRef } from "react";
import { useAuthStore } from "@/store/auth-store";
import { useDeviceStore } from "@/store/device-store";
import { getDeviceProfileApi } from "@/modules/device/api";
import { DeviceStorage } from "@/modules/device/storage";
import type { DeviceProfile } from "@/modules/device/types";
import {
  getFriendlyDeviceLoginErrorDescriptor,
  getFriendlyLoginErrorDescriptor,
  type LoginErrorDescriptor,
} from "@/modules/auth/login-errors";
import { prepareDeviceLoginSession } from "@/modules/auth/device-login-session";
import { resolveDefaultTabRoute } from "@/modules/navigation/default-route";
import { useAppNavigationStore } from "@/modules/navigation/store";
import { i18n, setAppLanguage } from "@/shared/i18n/i18n";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { AppAsyncStorage } from "@/shared/storage/async-storage";
import {
  API_HOST_PRESETS,
  getCurrentApiHost,
  getStoredApiHost,
  normalizeApiHost,
  setStoredApiHost,
} from "@/shared/api/config";

const REMEMBERED_USERNAME_KEY = "remembered_username";
const BRAND_RED = "#E53935";
const BRAND_BG = "#F5F5F5";
type LoginMode = "device" | "user";

const { height: SCREEN_HEIGHT } = Dimensions.get("window");
const IS_SMALL_SCREEN = SCREEN_HEIGHT < 640;

function getApiOriginLabel() {
  return getCurrentApiHost();
}

function translateLoginError(descriptor: LoginErrorDescriptor): string {
  return i18n.t(`login:${descriptor.key}`, descriptor.values);
}

function getFriendlyDeviceLookupErrorMessage(error: unknown): string {
  if (isAxiosError(error)) {
    if (error.response?.status === 404) {
      return "";
    }
    if (error.code === "ECONNABORTED") {
      return i18n.t("login:device.lookupTimeout", { origin: getApiOriginLabel() });
    }
    if (error.message === "Network Error") {
      return i18n.t("login:device.lookupNetwork", { origin: getApiOriginLabel() });
    }
    if (error.response?.status) {
      return i18n.t("login:device.lookupHttp", { status: error.response.status });
    }
  }

  return i18n.t("login:device.lookupFailed");
}

function resolveDeviceStatusText(status: number | undefined, fallback?: string | null) {
  switch (status) {
    case -1:
      return i18n.t("login:device.status.pending");
    case 0:
      return i18n.t("login:device.status.disabled");
    case 1:
      return i18n.t("login:device.status.enabled");
    case 2:
      return i18n.t("login:device.status.locked");
    case 3:
      return i18n.t("login:device.status.unregistered");
    default:
      return fallback || i18n.t("login:device.status.unknown");
  }
}

function getVisibleRouteNames() {
  return useAppNavigationStore.getState().items.map((item) => item.routeName);
}

export default function Login() {
  const router = useRouter();
  const { t } = useAppTranslation(["login", "common"]);
  const loginFn = useAuthStore((s) => s.login);
  const clearLocalAuthSession = useAuthStore((s) => s.clearLocalSession);
  const syncDeviceFromProfile = useDeviceStore((s) => s.syncFromProfile);
  const validateDevice = useDeviceStore((s) => s.validate);
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [loading, setLoading] = useState(false);
  const [deviceLoginLoading, setDeviceLoginLoading] = useState(false);
  const [deviceLookupLoading, setDeviceLookupLoading] = useState(false);
  const [loginMode, setLoginMode] = useState<LoginMode>("user");
  const [registeredDevice, setRegisteredDevice] = useState<DeviceProfile | null>(null);
  const [detectedHardwareId, setDetectedHardwareId] = useState("");
  const [error, setError] = useState("");
  const [rememberUsername, setRememberUsername] = useState(false);
  const [snackbarVisible, setSnackbarVisible] = useState(false);
  const [rememberReady, setRememberReady] = useState(false);
  const [apiHost, setApiHost] = useState(getCurrentApiHost());
  const [apiHostDraft, setApiHostDraft] = useState(getCurrentApiHost());
  const [apiHostModalVisible, setApiHostModalVisible] = useState(false);

  // 动画 refs
  const logoScale = useRef(new Animated.Value(0)).current;
  const logoOpacity = useRef(new Animated.Value(0)).current;
  const titleOpacity = useRef(new Animated.Value(0)).current;
  const titleTranslateY = useRef(new Animated.Value(20)).current;
  const usernameSlide = useRef(new Animated.Value(0)).current;
  const passwordSlide = useRef(new Animated.Value(0)).current;
  const buttonOpacity = useRef(new Animated.Value(0)).current;
  const buttonPulse = useRef(new Animated.Value(1)).current;

  useEffect(() => {
    Animated.sequence([
      Animated.parallel([
        Animated.spring(logoScale, { toValue: 1, tension: 70, friction: 7, useNativeDriver: true }),
        Animated.timing(logoOpacity, { toValue: 1, duration: 600, useNativeDriver: true }),
      ]),
      Animated.parallel([
        Animated.timing(titleOpacity, { toValue: 1, duration: 450, useNativeDriver: true }),
        Animated.timing(titleTranslateY, { toValue: 0, duration: 450, useNativeDriver: true }),
      ]),
      Animated.timing(usernameSlide, { toValue: 1, duration: 400, useNativeDriver: true }),
      Animated.timing(passwordSlide, { toValue: 1, duration: 400, useNativeDriver: true }),
      Animated.timing(buttonOpacity, { toValue: 1, duration: 400, useNativeDriver: true }),
    ]).start();

    const timer = setTimeout(() => {
      Animated.loop(
        Animated.sequence([
          Animated.timing(buttonPulse, { toValue: 1.03, duration: 1200, useNativeDriver: true }),
          Animated.timing(buttonPulse, { toValue: 1, duration: 1200, useNativeDriver: true }),
        ]),
      ).start();
    }, 2200);
    return () => clearTimeout(timer);
  }, []);

  useEffect(() => {
    void loadRememberedUsername();
    void loadApiHost();
  }, []);

  useEffect(() => {
    if (!rememberReady) return;
    if (!rememberUsername) {
      void AppAsyncStorage.removeItem(REMEMBERED_USERNAME_KEY);
      return;
    }
    if (username) {
      void AppAsyncStorage.setString(REMEMBERED_USERNAME_KEY, username);
    }
  }, [rememberReady, rememberUsername, username]);

  async function loadRememberedUsername() {
    const saved = await AppAsyncStorage.getString(REMEMBERED_USERNAME_KEY);
    if (saved) {
      setUsername(saved);
      setRememberUsername(true);
    }
    setRememberReady(true);
  }

  async function loadApiHost() {
    const host = await getStoredApiHost();
    setApiHost(host);
    setApiHostDraft(host);
    await identifyRegisteredDevice();
  }

  async function identifyRegisteredDevice() {
    const session = await DeviceStorage.getSession();
    if (!session?.systemDeviceNumber || !session.hardwareId) {
      setDetectedHardwareId("");
      setRegisteredDevice(null);
      setLoginMode("user");
      setDeviceLookupLoading(false);
      return;
    }

    setDeviceLookupLoading(true);
    try {
      setDetectedHardwareId(session.hardwareId);
      const profile = await getDeviceProfileApi(session.hardwareId);
      setRegisteredDevice(profile);
      setLoginMode("device");
    } catch (err) {
      setRegisteredDevice(null);
      setLoginMode("user");

      if (isAxiosError(err) && err.response?.status === 404) {
        return;
      }

      const message = getFriendlyDeviceLookupErrorMessage(err);
      if (message) {
        setError(message);
        setSnackbarVisible(true);
      }
    } finally {
      setDeviceLookupLoading(false);
    }
  }

  async function handleSaveApiHost() {
    const normalizedHost = normalizeApiHost(apiHostDraft);
    if (!normalizedHost) {
      setError(t("apiHost.empty"));
      setSnackbarVisible(true);
      return;
    }

    const host = await setStoredApiHost(normalizedHost);
    setApiHost(host);
    setApiHostDraft(host);
    setApiHostModalVisible(false);
    setError(t("apiHost.saved", { host }));
    setSnackbarVisible(true);
    await identifyRegisteredDevice();
  }

  async function handleLogin() {
    setError("");
    setLoading(true);
    try {
      await loginFn({ username, password });
      router.replace(
        resolveDefaultTabRoute({
          isDeviceMode: false,
          routeNames: getVisibleRouteNames(),
        }) as Parameters<typeof router.replace>[0]
      );
    } catch (err) {
      setError(translateLoginError(getFriendlyLoginErrorDescriptor(err)));
      setSnackbarVisible(true);
    } finally {
      setLoading(false);
    }
  }

  async function handleDeviceLogin() {
    if (!registeredDevice) {
      return;
    }

    setError("");
    setDeviceLoginLoading(true);
    try {
      const isReady = await prepareDeviceLoginSession(registeredDevice, {
        clearAccountSession: clearLocalAuthSession,
        syncDeviceFromProfile,
        validateDevice,
      });
      if (isReady) {
        router.replace(
          resolveDefaultTabRoute({
            isDeviceMode: true,
            routeNames: getVisibleRouteNames(),
          }) as Parameters<typeof router.replace>[0]
        );
        return;
      }

      setError(t("device.notReadyMessage"));
      setSnackbarVisible(true);
    } catch (err) {
      setError(translateLoginError(getFriendlyDeviceLoginErrorDescriptor(err)));
      setSnackbarVisible(true);
    } finally {
      setDeviceLoginLoading(false);
    }
  }

  const deviceStatusText = resolveDeviceStatusText(
    registeredDevice?.status,
    registeredDevice?.statusDescription
  );
  const canUseDeviceLogin = Boolean(
    registeredDevice?.status === 1 &&
      registeredDevice.storeCode &&
      registeredDevice.authCode
  );
  const openApiHostSettings = () => {
    setApiHostDraft(apiHost);
    setApiHostModalVisible(true);
  };

  return (
    <View style={styles.root}>
      <StatusBar barStyle="light-content" backgroundColor={BRAND_RED} />

      {/* ── 品牌区 ── */}
      <View style={styles.brandSection}>
        <View style={styles.topActions}>
          <TouchableOpacity
            style={styles.langSwitch}
            onPress={() => setAppLanguage(i18n.language === "zh" ? "en" : "zh")}
            activeOpacity={0.7}
          >
            <Text style={styles.langSwitchText}>
              {t(i18n.language === "zh" ? "languageSwitch.toEnglish" : "languageSwitch.toChinese")}
            </Text>
          </TouchableOpacity>
        </View>
        <Animated.View
          style={{
            opacity: logoOpacity,
            transform: [{ scale: logoScale }],
          }}
        >
          <View style={styles.logoCircle}>
            <Text style={styles.logoText}>HB</Text>
          </View>
        </Animated.View>

        <Animated.View
          style={{
            alignItems: "center",
            opacity: titleOpacity,
            transform: [{ translateY: titleTranslateY }],
          }}
        >
          <Text style={styles.brandTitle}>{t("common:appName")}</Text>
          <Text style={styles.brandSubtitle}>{t("subtitle")}</Text>
        </Animated.View>
      </View>

      {/* ── 表单区 ── */}
      <KeyboardAvoidingView
        style={styles.formSection}
        behavior={Platform.OS === "ios" ? "padding" : undefined}
      >
        <ScrollView
          contentContainerStyle={styles.formScrollContent}
          keyboardShouldPersistTaps="handled"
          showsVerticalScrollIndicator={false}
        >
          <View style={styles.formCard}>
            {registeredDevice ? (
              <View style={styles.loginModeRow}>
                <Button
                  compact
                  mode={loginMode === "device" ? "contained" : "outlined"}
                  buttonColor={loginMode === "device" ? BRAND_RED : undefined}
                  textColor={loginMode === "device" ? "#FFFFFF" : BRAND_RED}
                  onPress={() => setLoginMode("device")}
                  style={styles.loginModeButton}
                >
                  {t("device.mode")}
                </Button>
                <Button
                  compact
                  mode={loginMode === "user" ? "contained" : "outlined"}
                  buttonColor={loginMode === "user" ? BRAND_RED : undefined}
                  textColor={loginMode === "user" ? "#FFFFFF" : BRAND_RED}
                  onPress={() => setLoginMode("user")}
                  style={styles.loginModeButton}
                >
                  {t("userMode")}
                </Button>
              </View>
            ) : null}

            {deviceLookupLoading ? (
              <Text style={styles.deviceLookupText}>{t("device.lookupInProgress")}</Text>
            ) : null}

            {registeredDevice && loginMode === "device" ? (
              <View style={styles.deviceCard}>
                <Text style={styles.deviceTitle}>{t("device.title")}</Text>
                <Text style={styles.deviceDescription}>{t("device.description")}</Text>
                <View style={styles.deviceInfoBox}>
                  <Text style={styles.deviceInfoText}>
                    {t("device.deviceNumber", {
                      value: registeredDevice.systemDeviceNumber || t("common:na"),
                    })}
                  </Text>
                  <Text style={styles.deviceInfoText}>
                    {t("device.store", {
                      value: registeredDevice.storeCode || t("common:na"),
                    })}
                  </Text>
                  <Text style={styles.deviceInfoText}>
                    {t("device.statusLabel", { status: deviceStatusText })}
                  </Text>
                  <Text style={styles.deviceHardwareText} numberOfLines={1}>
                    {t("device.hardwareId", {
                      value: registeredDevice.hardwareId || detectedHardwareId || t("common:na"),
                    })}
                  </Text>
                </View>
                {!canUseDeviceLogin ? (
                  <Text style={styles.deviceUnavailableText}>{t("device.unavailable")}</Text>
                ) : null}
                <Button
                  mode="contained"
                  onPress={handleDeviceLogin}
                  loading={deviceLoginLoading}
                  disabled={deviceLoginLoading || !canUseDeviceLogin}
                  buttonColor={BRAND_RED}
                  textColor="#FFFFFF"
                  style={styles.button}
                  contentStyle={styles.buttonContent}
                  labelStyle={styles.buttonLabel}
                >
                  {t("device.login")}
                </Button>
                <Button mode="text" textColor={BRAND_RED} onPress={() => setLoginMode("user")}>
                  {t("device.switchToUser")}
                </Button>
                <Button mode="text" icon="cog-outline" textColor={BRAND_RED} onPress={openApiHostSettings}>
                  {t("apiHost.settingsButton")}
                </Button>
              </View>
            ) : (
              <>
                <Animated.View
                  style={{
                    opacity: usernameSlide,
                    transform: [{ translateX: usernameSlide.interpolate({ inputRange: [0, 1], outputRange: [-60, 0] }) }],
                  }}
                >
                  <TextInput
                    label={t("username")}
                    value={username}
                    onChangeText={setUsername}
                    mode="outlined"
                    autoCapitalize="none"
                    style={styles.input}
                    outlineColor="#E0E0E0"
                    activeOutlineColor={BRAND_RED}
                  />
                </Animated.View>

                <Animated.View
                  style={{
                    opacity: passwordSlide,
                    transform: [{ translateX: passwordSlide.interpolate({ inputRange: [0, 1], outputRange: [60, 0] }) }],
                  }}
                >
                  <TextInput
                    label={t("password")}
                    value={password}
                    onChangeText={setPassword}
                    mode="outlined"
                    secureTextEntry={!showPassword}
                    style={styles.input}
                    outlineColor="#E0E0E0"
                    activeOutlineColor={BRAND_RED}
                    right={
                      <TextInput.Icon
                        icon={showPassword ? "eye-off" : "eye"}
                        onPress={() => setShowPassword((v) => !v)}
                        forceTextInputFocus={false}
                      />
                    }
                  />
                </Animated.View>

                <Animated.View style={{ opacity: buttonOpacity, transform: [{ scale: buttonPulse }] }}>
                  <Checkbox.Item
                    label={t("rememberUsername")}
                    status={rememberUsername ? "checked" : "unchecked"}
                    onPress={() => setRememberUsername(!rememberUsername)}
                    color={BRAND_RED}
                    labelStyle={styles.checkboxLabel}
                  />
                  <Button
                    mode="contained"
                    onPress={handleLogin}
                    loading={loading}
                    disabled={loading || !username || !password}
                    buttonColor={BRAND_RED}
                    textColor="#FFFFFF"
                    style={styles.button}
                    contentStyle={styles.buttonContent}
                    labelStyle={styles.buttonLabel}
                  >
                    {t("common:actions.login")}
                  </Button>
                  {registeredDevice ? (
                    <Button mode="text" textColor={BRAND_RED} onPress={() => setLoginMode("device")}>
                      {t("device.switchToDevice")}
                    </Button>
                  ) : null}
                  <Button mode="text" icon="cog-outline" textColor={BRAND_RED} onPress={openApiHostSettings}>
                    {t("apiHost.settingsButton")}
                  </Button>
                </Animated.View>
              </>
            )}
          </View>
        </ScrollView>
      </KeyboardAvoidingView>

      <Portal>
        <Modal
          visible={apiHostModalVisible}
          onDismiss={() => setApiHostModalVisible(false)}
          contentContainerStyle={styles.apiHostModal}
        >
          <Text style={styles.apiHostModalTitle}>{t("apiHost.title")}</Text>
          <Text style={styles.apiHostModalDescription}>{t("apiHost.description")}</Text>
          <View style={styles.apiHostCurrentBox}>
            <Text style={styles.apiHostLabel}>{t("apiHost.current")}</Text>
            <Text style={styles.apiHostValue} numberOfLines={1}>{apiHost}</Text>
          </View>
          <Text style={styles.apiHostLabel}>{t("apiHost.presets.label")}</Text>
          <View style={styles.apiHostPresetList}>
            {API_HOST_PRESETS.map((preset) => {
              const selected = normalizeApiHost(apiHostDraft) === preset.host;
              return (
                <Button
                  key={preset.key}
                  compact
                  mode={selected ? "contained" : "outlined"}
                  buttonColor={selected ? BRAND_RED : undefined}
                  textColor={selected ? "#FFFFFF" : BRAND_RED}
                  style={styles.apiHostPresetButton}
                  onPress={() => setApiHostDraft(preset.host)}
                >
                  {t(preset.labelKey)}
                </Button>
              );
            })}
          </View>
          <TextInput
            label={t("apiHost.inputLabel")}
            value={apiHostDraft}
            onChangeText={setApiHostDraft}
            mode="outlined"
            autoCapitalize="none"
            autoCorrect={false}
            style={styles.input}
            outlineColor="#E0E0E0"
            activeOutlineColor={BRAND_RED}
          />
          <View style={styles.apiHostModalActions}>
            <Button mode="text" textColor="#555" onPress={() => setApiHostModalVisible(false)}>
              {t("common:actions.cancel")}
            </Button>
            <Button mode="contained" buttonColor={BRAND_RED} onPress={handleSaveApiHost}>
              {t("common:actions.save")}
            </Button>
          </View>
        </Modal>
      </Portal>

      <Snackbar
        visible={snackbarVisible}
        onDismiss={() => setSnackbarVisible(false)}
        duration={3000}
        wrapperStyle={styles.snackbar}
      >
        {error}
      </Snackbar>
    </View>
  );
}

const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: BRAND_BG },
  topActions: {
    alignItems: "center",
    alignSelf: "stretch",
    flexDirection: "row",
    justifyContent: "flex-end",
    marginBottom: IS_SMALL_SCREEN ? 14 : 20,
    paddingHorizontal: 14,
  },
  // 语言切换
  langSwitch: {
    backgroundColor: "rgba(255,255,255,0.25)",
    borderRadius: 18,
    paddingHorizontal: 14,
    paddingVertical: 6,
    borderWidth: 1,
    borderColor: "rgba(255,255,255,0.4)",
  },
  langSwitchText: {
    color: "#FFFFFF",
    fontSize: 13,
    fontWeight: "700",
    letterSpacing: 1,
  },
  // 品牌区
  brandSection: {
    backgroundColor: BRAND_RED,
    paddingTop: IS_SMALL_SCREEN ? 50 : 80,
    paddingBottom: IS_SMALL_SCREEN ? 30 : 50,
    borderBottomLeftRadius: 36,
    borderBottomRightRadius: 36,
    alignItems: "center",
  },
  logoCircle: {
    width: IS_SMALL_SCREEN ? 72 : 100,
    height: IS_SMALL_SCREEN ? 72 : 100,
    borderRadius: IS_SMALL_SCREEN ? 36 : 50,
    backgroundColor: "#FFFFFF",
    justifyContent: "center",
    alignItems: "center",
    marginBottom: IS_SMALL_SCREEN ? 10 : 16,
    shadowColor: "#000",
    shadowOffset: { width: 0, height: 6 },
    shadowOpacity: 0.25,
    shadowRadius: 12,
    elevation: 10,
  },
  logoText: {
    fontSize: IS_SMALL_SCREEN ? 32 : 42,
    fontWeight: "900",
    color: BRAND_RED,
    letterSpacing: 3,
  },
  brandTitle: {
    fontSize: IS_SMALL_SCREEN ? 22 : 26,
    fontWeight: "800",
    color: "#FFFFFF",
    letterSpacing: 2,
    marginBottom: 6,
  },
  brandSubtitle: {
    fontSize: IS_SMALL_SCREEN ? 12 : 14,
    color: "rgba(255,255,255,0.85)",
    letterSpacing: 1,
  },
  // 表单区
  formSection: { flex: 1, paddingHorizontal: 24, paddingTop: 24 },
  formScrollContent: { flexGrow: 1, justifyContent: "center", paddingBottom: 20 },
  formCard: {
    backgroundColor: "#FFFFFF",
    borderRadius: 16,
    padding: 24,
    borderLeftWidth: 4,
    borderLeftColor: BRAND_RED,
    shadowColor: "#000",
    shadowOffset: { width: 0, height: 2 },
    shadowOpacity: 0.06,
    shadowRadius: 8,
    elevation: 2,
  },
  loginModeRow: {
    flexDirection: "row",
    gap: 10,
    marginBottom: 18,
  },
  loginModeButton: {
    flex: 1,
    borderColor: BRAND_RED,
  },
  input: { marginBottom: 14, backgroundColor: "#FAFAFA" },
  deviceCard: { gap: 12 },
  deviceTitle: {
    color: "#222",
    fontSize: 18,
    fontWeight: "800",
  },
  deviceDescription: {
    color: "#666",
    fontSize: 13,
    lineHeight: 19,
  },
  deviceInfoBox: {
    backgroundColor: "#FFF7F6",
    borderColor: "#F2D7D5",
    borderRadius: 12,
    borderWidth: 1,
    gap: 6,
    paddingHorizontal: 12,
    paddingVertical: 12,
  },
  deviceInfoText: {
    color: "#333",
    fontSize: 14,
    fontWeight: "600",
  },
  deviceHardwareText: {
    color: "#777",
    fontSize: 12,
  },
  deviceLookupText: {
    color: "#777",
    fontSize: 13,
    marginBottom: 12,
    textAlign: "center",
  },
  deviceUnavailableText: {
    color: "#A8071A",
    fontSize: 13,
    lineHeight: 19,
  },
  apiHostCurrentBox: {
    borderColor: "#F2D7D5",
    borderRadius: 12,
    borderWidth: 1,
    marginBottom: 14,
    paddingHorizontal: 12,
    paddingVertical: 10,
  },
  apiHostLabel: { color: "#777", fontSize: 12, marginBottom: 2 },
  apiHostValue: { color: "#333", fontSize: 14, fontWeight: "700" },
  apiHostPresetList: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
    marginBottom: 14,
    marginTop: 8,
  },
  apiHostPresetButton: {
    borderColor: BRAND_RED,
    borderRadius: 12,
  },
  apiHostModal: {
    backgroundColor: "#FFFFFF",
    borderRadius: 16,
    marginHorizontal: 24,
    padding: 20,
  },
  apiHostModalTitle: {
    color: "#222",
    fontSize: 18,
    fontWeight: "800",
    marginBottom: 8,
  },
  apiHostModalDescription: {
    color: "#666",
    fontSize: 13,
    lineHeight: 19,
    marginBottom: 16,
  },
  apiHostModalActions: {
    flexDirection: "row",
    gap: 8,
    justifyContent: "flex-end",
  },
  checkboxLabel: { fontSize: 14, color: "#555" },
  button: { marginTop: 6, borderRadius: 14 },
  buttonContent: { height: 50 },
  buttonLabel: { fontSize: 17, fontWeight: "700", letterSpacing: 2 },
  snackbar: { bottom: 40 },
});
