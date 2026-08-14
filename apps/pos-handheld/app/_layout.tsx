import { useKeepAwake } from "expo-keep-awake";
import { Stack } from "expo-router";
import * as SplashScreen from "expo-splash-screen";

import { AppProviders } from "@/app-providers";
import { ScannerRouteProvider } from "@/ui/scanner/scanner-route-bridge";

// 手持 POS 只有一个主 React surface；路由加载后显式移除启动遮罩。
void SplashScreen.hideAsync().catch(() => undefined);

export default function RootLayout() {
  // POS 在前台运行时保持屏幕常亮，避免收银过程中自动锁屏。
  useKeepAwake();

  return (
    <AppProviders>
      <ScannerRouteProvider>
        <Stack screenOptions={{ headerShown: false }} />
      </ScannerRouteProvider>
    </AppProviders>
  );
}
