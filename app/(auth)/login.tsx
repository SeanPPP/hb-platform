import { useRouter } from "expo-router";
import { View, StyleSheet } from "react-native";
import type { AxiosError } from "axios";
import {
  TextInput,
  Button,
  Text,
  Surface,
  Checkbox,
  Snackbar,
} from "react-native-paper";
import { useState, useEffect } from "react";
import { useAuthStore } from "@/store/auth-store";
import { i18n } from "@/shared/i18n/i18n";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { AppAsyncStorage } from "@/shared/storage/async-storage";
import { API_BASE_URL } from "@/shared/constants/api";

const REMEMBERED_USERNAME_KEY = "remembered_username";

function getApiOriginLabel() {
  try {
    const url = new URL(API_BASE_URL);
    return `${url.origin}${url.pathname}`;
  } catch {
    return API_BASE_URL;
  }
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
    status === 401 ||
    status === 403 ||
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

export default function Login() {
  const router = useRouter();
  const { t } = useAppTranslation(["login", "common"]);
  const login = useAuthStore((s) => s.login);
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const [rememberUsername, setRememberUsername] = useState(false);
  const [snackbarVisible, setSnackbarVisible] = useState(false);
  const [rememberReady, setRememberReady] = useState(false);

  useEffect(() => {
    void loadRememberedUsername();
  }, []);

  useEffect(() => {
    if (!rememberReady) {
      return;
    }

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

  async function handleLogin() {
    setError("");
    setLoading(true);
    try {
      await login({ username, password });
      router.replace("/(tabs)/home");
    } catch (error) {
      setError(getFriendlyLoginErrorMessage(error));
      setSnackbarVisible(true);
    } finally {
      setLoading(false);
    }
  }

  return (
    <View style={styles.container}>
      <Surface style={styles.card} elevation={2}>
        <Text variant="headlineMedium" style={styles.title}>
          {t("common:appName")}
        </Text>
        <Text variant="bodyMedium" style={styles.subtitle}>
          {t("subtitle")}
        </Text>
        <TextInput
          label={t("username")}
          value={username}
          onChangeText={setUsername}
          style={styles.input}
          mode="outlined"
          autoCapitalize="none"
        />
        <TextInput
          label={t("password")}
          value={password}
          onChangeText={setPassword}
          style={styles.input}
          mode="outlined"
          secureTextEntry={!showPassword}
          right={
            <TextInput.Icon
              icon={showPassword ? "eye-off" : "eye"}
              onPress={() => setShowPassword((value) => !value)}
              forceTextInputFocus={false}
            />
          }
        />
        <Checkbox.Item
          label={t("rememberUsername")}
          status={rememberUsername ? "checked" : "unchecked"}
          onPress={() => setRememberUsername(!rememberUsername)}
        />
        <Button
          mode="contained"
          onPress={handleLogin}
          loading={loading}
          disabled={loading || !username || !password}
          style={styles.button}
        >
          {t("common:actions.login")}
        </Button>
      </Surface>
      <Snackbar
        visible={snackbarVisible}
        onDismiss={() => setSnackbarVisible(false)}
        duration={3000}
      >
        {error}
      </Snackbar>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    justifyContent: "center",
    padding: 24,
    backgroundColor: "#f5f5f5",
  },
  card: { padding: 24, borderRadius: 12 },
  title: { textAlign: "center", fontWeight: "bold" },
  subtitle: { textAlign: "center", marginBottom: 24, color: "#666" },
  input: { marginBottom: 16 },
  button: { marginTop: 8 },
});
