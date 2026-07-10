import { useEffect, useState } from "react";
import { Stack } from "expo-router";
import Constants from "expo-constants";
import * as SplashScreen from "expo-splash-screen";
import { Image, StyleSheet, Text, View } from "react-native";
import { PaperProvider, MD3LightTheme } from "react-native-paper";
import { SafeAreaProvider } from "react-native-safe-area-context";
import { I18nextProvider } from "react-i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { StatusBar } from "expo-status-bar";
import { usePrinterAutoConnect } from "@/modules/printer/use-printer-auto-connect";
import { waitForStartupReadiness } from "@/modules/startup/startup-readiness";
import { shouldRunAutomaticAppUpdatesForProfile } from "@/modules/updates/app-build-profile";
import { useAutomaticAppUpdate } from "@/modules/updates/use-automatic-app-update";
import { useAutomaticNativeAppUpdate } from "@/modules/updates/use-automatic-native-app-update";
import { i18n, initI18n } from "@/shared/i18n/i18n";
import { installGlobalErrorLogging, reportApplicationLog } from "@/shared/logging/log-center-runtime";
import { useDeviceStore } from "@/store/device-store";
import "@/modules/attendance/location-tracking";

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 5 * 60 * 1000,
      retry: 1,
    },
  },
});

const MIN_SPLASH_VISIBLE_MS = 900;

SplashScreen.preventAutoHideAsync().catch(() => {
  // 启动画面可能已经被系统隐藏，忽略即可，避免启动流程被异常打断。
});

SplashScreen.setOptions({
  duration: 700,
  fade: true,
});

const theme = {
  ...MD3LightTheme,
  colors: {
    ...MD3LightTheme.colors,
    primary: "#1677FF",
    secondary: "#52C41A",
    error: "#FF4D4F",
  },
};

export default function RootLayout() {
  const [appReady, setAppReady] = useState(false);
  const [startupError, setStartupError] = useState<unknown>(null);
  const automaticUpdatesEnabled =
    appReady
    && !startupError
    && shouldRunAutomaticAppUpdatesForProfile(Constants.expoConfig?.extra?.nativeAppBuildProfile);

  usePrinterAutoConnect();
  useAutomaticAppUpdate({ enabled: automaticUpdatesEnabled });
  useAutomaticNativeAppUpdate({ enabled: automaticUpdatesEnabled });

  useEffect(() => {
    return installGlobalErrorLogging();
  }, []);

  useEffect(() => {
    let mounted = true;

    async function prepareApp() {
      // 等待语言与设备缓存完成，避免启动页过早消失后露出空白过渡。
      const result = await waitForStartupReadiness(
        [
          initI18n(),
          useDeviceStore.getState().hydrate(),
        ],
        MIN_SPLASH_VISIBLE_MS,
      );

      if (!mounted) {
        return;
      }

      if (result.ok) {
        setAppReady(true);
      } else {
        console.warn("[startup] prepare app before splash hide failed", result.error);
        const startupError = result.error instanceof Error ? result.error : new Error(String(result.error));
        reportApplicationLog({
          level: "Critical",
          message: "移动端启动准备失败",
          sourceType: "app.startup",
          exceptionType: startupError.name,
          exceptionMessage: startupError.message,
          stackTrace: startupError.stack,
        });
        setStartupError(result.error);
      }
    }

    void prepareApp();

    return () => {
      mounted = false;
    };
  }, []);

  useEffect(() => {
    if (!appReady && !startupError) {
      return;
    }

    const frame = requestAnimationFrame(() => {
      void SplashScreen.hideAsync();
    });

    return () => {
      cancelAnimationFrame(frame);
    };
  }, [appReady, startupError]);

  if (startupError) {
    return (
      <View style={styles.splashFallback}>
        <StatusBar style="dark" />
        <Image
          source={require("../assets/splash-logo.png")}
          style={styles.splashLogo}
          resizeMode="contain"
        />
        <Text style={styles.startupErrorText}>启动失败，请重新打开应用</Text>
      </View>
    );
  }

  if (!appReady) {
    return (
      <View style={styles.splashFallback}>
        <StatusBar style="dark" />
        <Image
          source={require("../assets/splash-logo.png")}
          style={styles.splashLogo}
          resizeMode="contain"
        />
      </View>
    );
  }

  return (
    <SafeAreaProvider>
      <QueryClientProvider client={queryClient}>
        <I18nextProvider i18n={i18n}>
          <PaperProvider theme={theme}>
            {/* 浅色业务页面统一使用深色系统图标，避免白底白字看不清。 */}
            <StatusBar style="dark" />
            <Stack screenOptions={{ headerShown: false }}>
              <Stack.Screen name="index" />
              <Stack.Screen name="(auth)" />
              <Stack.Screen name="(tabs)" />
            </Stack>
          </PaperProvider>
        </I18nextProvider>
      </QueryClientProvider>
    </SafeAreaProvider>
  );
}

const styles = StyleSheet.create({
  splashFallback: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: "#F8FBFF",
  },
  splashLogo: {
    width: 210,
    height: 210,
  },
  startupErrorText: {
    marginTop: 16,
    color: "#334155",
    fontSize: 15,
    lineHeight: 22,
  },
});
