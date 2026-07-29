import { Stack } from "expo-router";

import { AppProviders } from "@/app-providers";
import "@/core/peripherals/customer-display/native";
import { ScannerRouteProvider } from "@/ui/scanner/scanner-route-bridge";

export default function RootLayout() {
  return (
    <AppProviders>
      <ScannerRouteProvider>
        <Stack screenOptions={{ headerShown: false }} />
      </ScannerRouteProvider>
    </AppProviders>
  );
}
