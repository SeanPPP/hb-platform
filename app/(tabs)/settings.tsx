import { useState } from "react";
import { Alert, View, StyleSheet } from "react-native";
import { useRouter } from "expo-router";
import { Button, Surface, Text } from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import { useAuthStore } from "@/store/auth-store";

export default function Settings() {
  const router = useRouter();
  const user = useAuthStore((state) => state.user);
  const logout = useAuthStore((state) => state.logout);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const handleLogout = () => {
    Alert.alert("退出登录", "确定要退出当前账号吗？", [
      { text: "取消", style: "cancel" },
      {
        text: "退出",
        style: "destructive",
        onPress: async () => {
          setIsSubmitting(true);
          try {
            await logout();
            router.replace("/(auth)/login");
          } finally {
            setIsSubmitting(false);
          }
        },
      },
    ]);
  };

  return (
    <SafeAreaView style={styles.container}>
      <View style={styles.content}>
        <Text variant="headlineSmall" style={styles.title}>
          设置
        </Text>

        <Surface style={styles.card} elevation={1}>
          <Text variant="titleMedium">当前账号</Text>
          <Text variant="bodyLarge" style={styles.value}>
            {user?.fullName || user?.username || "未登录"}
          </Text>
          <Text variant="bodyMedium" style={styles.meta}>
            {user?.email || "暂无邮箱信息"}
          </Text>
        </Surface>

        <Button
          mode="contained"
          buttonColor="#FF4D4F"
          onPress={handleLogout}
          loading={isSubmitting}
          disabled={isSubmitting}
          style={styles.logoutButton}
        >
          退出登录
        </Button>
      </View>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: "#fff" },
  content: { flex: 1, padding: 24, justifyContent: "center" },
  title: { marginBottom: 20, textAlign: "center" },
  card: { padding: 20, borderRadius: 12, gap: 8 },
  value: { marginTop: 8, fontWeight: "600" },
  meta: { color: "#666" },
  logoutButton: { marginTop: 20 },
});
