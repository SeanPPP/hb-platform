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
import { AppAsyncStorage } from "@/shared/storage/async-storage";

const REMEMBERED_USERNAME_KEY = "remembered_username";

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
    return "用户名或密码不正确，请重新输入。";
  }

  if (axiosError.code === "ECONNABORTED") {
    return "登录超时，请稍后重试。";
  }

  if (axiosError.message === "Network Error") {
    return "网络连接失败，请检查网络后重试。";
  }

  if (status && status >= 500) {
    return "服务器暂时不可用，请稍后再试。";
  }

  return "登录失败，请稍后重试。";
}

export default function Login() {
  const router = useRouter();
  const login = useAuthStore((s) => s.login);
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const [rememberUsername, setRememberUsername] = useState(false);
  const [snackbarVisible, setSnackbarVisible] = useState(false);

  useEffect(() => {
    loadRememberedUsername();
  }, []);

  async function loadRememberedUsername() {
    const saved = await AppAsyncStorage.getString(REMEMBERED_USERNAME_KEY);
    if (saved) {
      setUsername(saved);
      setRememberUsername(true);
    }
  }

  async function handleLogin() {
    setError("");
    setLoading(true);
    try {
      await login({ username, password });

      if (rememberUsername && username) {
        await AppAsyncStorage.setString(REMEMBERED_USERNAME_KEY, username);
      } else {
        await AppAsyncStorage.removeItem(REMEMBERED_USERNAME_KEY);
      }
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
          HbwebExpo
        </Text>
        <Text variant="bodyMedium" style={styles.subtitle}>
          Store Order System
        </Text>
        <TextInput
          label="Username"
          value={username}
          onChangeText={setUsername}
          style={styles.input}
          mode="outlined"
          autoCapitalize="none"
        />
        <TextInput
          label="Password"
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
          label="Remember username"
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
          Login
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
