import { useEffect } from "react";
import { router } from "expo-router";
import { View, ActivityIndicator, Text } from "react-native";
import { SecureStorage } from "@/shared/storage/secure";
import { apiClient } from "@/shared/api/client";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { useDeviceStore } from "@/store/device-store";

export default function Index() {
  const { t } = useAppTranslation("common");

  useEffect(() => {
    checkAuth();
  }, []);

  async function checkAuth() {
    try {
      const hydratedDeviceSession = await useDeviceStore.getState().hydrate();
      if (hydratedDeviceSession?.hardwareId && hydratedDeviceSession.authCode) {
        router.replace("/(tabs)/home");
        return;
      }

      const token = await SecureStorage.getToken();
      if (!token) {
        router.replace("/(auth)/login");
        return;
      }
      await apiClient.get("/auth/current");
      router.replace("/(tabs)/home");
    } catch {
      await SecureStorage.clearAll();
      router.replace("/(auth)/login");
    }
  }

  return (
    <View
      style={{
        flex: 1,
        justifyContent: "center",
        alignItems: "center",
        backgroundColor: "#fff",
      }}
    >
      <ActivityIndicator size="large" color="#1677FF" />
      <Text style={{ marginTop: 12, color: "#666" }}>{t("loading")}</Text>
    </View>
  );
}
