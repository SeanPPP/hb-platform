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
import type { AxiosError } from "axios";
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
import { resolveDefaultTabRoute } from "@/modules/navigation/default-route";
import { useAppNavigationStore } from "@/modules/navigation/store";
import { i18n, setAppLanguage } from "@/shared/i18n/i18n";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { AppAsyncStorage } from "@/shared/storage/async-storage";
import { getCurrentApiHost, getStoredApiHost, normalizeApiHost, setStoredApiHost } from "@/shared/api/config";

const REMEMBERED_USERNAME_KEY = "remembered_username";
const BRAND_RED = "#E53935";
const BRAND_BG = "#F5F5F5";

const { height: SCREEN_HEIGHT } = Dimensions.get("window");
const IS_SMALL_SCREEN = SCREEN_HEIGHT < 640;

function getApiOriginLabel() {
  return getCurrentApiHost();
}

function getFriendlyLoginErrorMessage(error: unknown): string {
  const axiosError = error as AxiosError<{
    message?: string;
    error?: string;
  }>;
  const status = axiosError.response?.status;
  const serverMessage =
    axiosError.response?.data?.message || axiosError.response?.data?.error;
  const normalizedMessage = serverMessage?.toLowerCase() || "";

  if (
    status === 401 || status === 403 ||
    normalizedMessage.includes("invalid") ||
    normalizedMessage.includes("unauthorized") ||
    normalizedMessage.includes("password") ||
    normalizedMessage.includes("credential")
  ) {
    return i18n.t("login:errors.invalidCredentials");
  }
  if (axiosError.code === "ECONNABORTED") {
    return i18n.t("login:errors.timeout", { origin: getApiOriginLabel() });
  }
  if (axiosError.message === "Network Error") {
    return i18n.t("login:errors.network", { origin: getApiOriginLabel() });
  }
  if (status && status >= 500) {
    return i18n.t("login:errors.server");
  }
  if (status) {
    return i18n.t("login:errors.http", { status });
  }
  return i18n.t("login:errors.default");
}

function getVisibleRouteNames() {
  return useAppNavigationStore.getState().items.map((item) => item.routeName);
}

export default function Login() {
  const router = useRouter();
  const { t } = useAppTranslation(["login", "common"]);
  const loginFn = useAuthStore((s) => s.login);
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [loading, setLoading] = useState(false);
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
      setError(getFriendlyLoginErrorMessage(err));
      setSnackbarVisible(true);
    } finally {
      setLoading(false);
    }
  }

  return (
    <View style={styles.root}>
      <StatusBar barStyle="light-content" backgroundColor={BRAND_RED} />

      {/* ── 品牌区 ── */}
      <View style={styles.brandSection}>
        <View style={styles.topActions}>
          <Button
            icon="cog-outline"
            mode="outlined"
            compact
            textColor="#FFFFFF"
            accessibilityLabel={t("apiHost.title")}
            style={styles.serverSettingsButton}
            contentStyle={styles.serverSettingsButtonContent}
            labelStyle={styles.serverSettingsButtonLabel}
            onPress={() => {
              setApiHostDraft(apiHost);
              setApiHostModalVisible(true);
            }}
          >
            {t("apiHost.settingsButton")}
          </Button>

          <TouchableOpacity
            style={styles.langSwitch}
            onPress={() => setAppLanguage(i18n.language === "zh" ? "en" : "zh")}
            activeOpacity={0.7}
          >
            <Text style={styles.langSwitchText}>
              {i18n.language === "zh" ? "EN" : "中"}
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
            <Button
              mode="text"
              icon="cog-outline"
              textColor={BRAND_RED}
              onPress={() => {
                setApiHostDraft(apiHost);
                setApiHostModalVisible(true);
              }}
            >
              {t("apiHost.settingsButton")}
            </Button>
          </Animated.View>
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
    justifyContent: "space-between",
    marginBottom: IS_SMALL_SCREEN ? 14 : 20,
    paddingHorizontal: 14,
  },
  serverSettingsButton: {
    backgroundColor: "rgba(255,255,255,0.25)",
    borderColor: "rgba(255,255,255,0.4)",
    borderWidth: 1,
    borderRadius: 18,
  },
  serverSettingsButtonContent: {
    minHeight: 32,
    paddingHorizontal: 2,
  },
  serverSettingsButtonLabel: {
    fontSize: 12,
    fontWeight: "700",
    marginHorizontal: 4,
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
  input: { marginBottom: 14, backgroundColor: "#FAFAFA" },
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
