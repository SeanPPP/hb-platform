import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { PropsWithChildren } from "react";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { PaperProvider } from "react-native-paper";
import { SafeAreaProvider } from "react-native-safe-area-context";

import "@/i18n";
import {
  PosRuntimeProvider,
  usePosRuntime,
} from "@/core/runtime/pos-runtime-context";
import { AppUpdateGateBridge } from "@/features/app-updates";
import { CashierSessionInvalidationBridge } from "@/features/cashier-login";
import { OperationAuthorizationModal } from "@/features/operation-authorization/operation-authorization-modal";
import { NetworkStatusBridge } from "@/ui/shell/network-status-bridge";
import { PeripheralStatusBridge } from "@/ui/shell/peripheral-status-bridge";
import { RuntimeStatusBridge } from "@/ui/shell/runtime-status-bridge";
import { RuntimeWorkBridge } from "@/ui/shell/runtime-work-bridge";
import { posTheme } from "@/ui/theme";

export function AppProviders({ children }: PropsWithChildren) {
  const [queryClient] = useState(
    () =>
      new QueryClient({
        defaultOptions: {
          queries: {
            retry: 1,
            staleTime: 30_000,
          },
          mutations: {
            retry: false,
          },
        },
      }),
  );

  return (
    <SafeAreaProvider>
      <QueryClientProvider client={queryClient}>
        <PaperProvider theme={posTheme}>
          <PosRuntimeProvider>
            <CashierSessionInvalidationBridge />
            <NetworkStatusBridge />
            <PeripheralStatusBridge />
            <RuntimeStatusBridge />
            <RuntimeWorkBridge />
            <OperationAuthorizationModalBridge />
            {children}
            <AppUpdateGateBridge />
          </PosRuntimeProvider>
        </PaperProvider>
      </QueryClientProvider>
    </SafeAreaProvider>
  );
}

function OperationAuthorizationModalBridge() {
  const runtime = usePosRuntime();
  const { i18n } = useTranslation();
  const authorization = runtime.services?.operationAuthorization;
  if (!authorization || authorization.status !== "available") return null;

  return (
    <OperationAuthorizationModal
      locale={i18n.resolvedLanguage ?? i18n.language}
      service={authorization}
    />
  );
}
