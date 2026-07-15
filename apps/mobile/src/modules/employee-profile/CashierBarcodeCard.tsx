import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Alert, AppState, StyleSheet, View } from "react-native";
import * as Crypto from "expo-crypto";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useRouter } from "expo-router";
import { ActivityIndicator, Button, HelperText, Surface, Text } from "react-native-paper";

import { ProductBarcodeImage } from "@/components/product-maintenance/ProductBarcodeImage";
import {
  confirmMyCashierBarcodePrintApi,
  getMyCashierBarcodeApi,
  refreshMyCashierBarcodeApi,
} from "./api";
import {
  classifyCashierBarcodePrintError,
  canRefreshCashierBarcode,
  CashierBarcodePendingChangedError,
  CashierBarcodePrintConfirmationError,
  CashierBarcodeRevalidationError,
  executeCashierBarcodePrint,
  isCashierBarcodeChangedError,
  loadCashierBarcodePrintPending,
  prepareNewCashierBarcodePrint,
  prepareUncertainCashierBarcodeReprint,
  resolveUncertainCashierBarcodePrint,
  saveCashierBarcodePrintPending,
  type PendingCashierBarcodePrintConfirmation,
} from "./cashier-barcode";
import { getCashierBarcodeQueryKey } from "./cache-keys";
import { printEmployeeCashierBarcodeLabel } from "@/modules/printer/api";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { SecureStorage } from "@/shared/storage/secure";
import {
  activateCashierPrintSecureSession,
  type CashierPrintSecureSession,
} from "@/shared/storage/cashier-print-secure-session";

type CashierPrintAction = "default" | "confirmUncertain" | "reprintUncertain";

export function CashierBarcodeCard({
  employeeName,
  userIdentity,
}: {
  employeeName: string;
  userIdentity: string;
}) {
  const { t } = useAppTranslation(["employeeProfile", "common"]);
  const router = useRouter();
  const queryClient = useQueryClient();
  const [pendingConfirmation, setPendingConfirmation] =
    useState<PendingCashierBarcodePrintConfirmation | null>(null);
  const [pendingLoaded, setPendingLoaded] = useState(false);
  const pendingIdentityRef = useRef(userIdentity);
  const secureSessionRef = useRef<CashierPrintSecureSession | null>(null);
  const mismatchHandledRef = useRef("");
  const queryKey = useMemo(() => getCashierBarcodeQueryKey(userIdentity), [userIdentity]);
  const barcodeQuery = useQuery({ queryKey, queryFn: getMyCashierBarcodeApi });

  const persistPendingConfirmation = useCallback(async (
    value: PendingCashierBarcodePrintConfirmation | null
  ) => {
    const targetIdentity = userIdentity;
    const session = secureSessionRef.current;
    if (!session || session.userIdentity !== targetIdentity) {
      throw new Error("Cashier print secure session expired.");
    }
    await saveCashierBarcodePrintPending(SecureStorage, targetIdentity, value, session);
    if (pendingIdentityRef.current === targetIdentity) {
      setPendingConfirmation(value);
    }
  }, [userIdentity]);

  const refreshMutation = useMutation({
    mutationFn: refreshMyCashierBarcodeApi,
    onSuccess: (data) => {
      setPendingConfirmation(null);
      queryClient.setQueryData(queryKey, data);
    },
  });
  const printMutation = useMutation({
    mutationFn: async (action: CashierPrintAction) => {
      let barcode = barcodeQuery.data?.barcode;
      if (!barcode) {
        throw new Error(t("cashierBarcode.empty"));
      }
      let effectivePending = pendingConfirmation;
      const refetchBarcode = async () => {
        const result = await barcodeQuery.refetch();
        return result.isError || !result.data?.exists ? null : result.data.barcode ?? null;
      };
      if (action === "confirmUncertain" && effectivePending?.phase === "printing") {
        effectivePending = await resolveUncertainCashierBarcodePrint(effectivePending, "printed");
        await persistPendingConfirmation(effectivePending);
      } else if (action === "reprintUncertain") {
        if (!effectivePending) {
          throw new CashierBarcodeRevalidationError();
        }
        barcode = await prepareUncertainCashierBarcodeReprint({
          pending: effectivePending,
          refetchBarcode,
          onPendingChange: persistPendingConfirmation,
        });
        effectivePending = null;
      } else if (!effectivePending) {
        // 每个会实际出纸的新 attempt 都必须先取得服务端权威条码。
        barcode = await prepareNewCashierBarcodePrint({
          cachedBarcode: barcode,
          refetchBarcode,
        });
      }
      return executeCashierBarcodePrint({
        pending: effectivePending,
        barcode,
        createAttemptId: () => Crypto.randomUUID(),
        printLabel: async (value) => {
          const printed = await printEmployeeCashierBarcodeLabel({ employeeName, barcode: value });
          if (!printed) {
            throw new Error(t("cashierBarcode.printFailed"));
          }
        },
        confirmPrint: (confirmation) => confirmMyCashierBarcodePrintApi(
          confirmation.barcode,
          confirmation.attemptId
        ),
        onPendingChange: persistPendingConfirmation,
      });
    },
    onSuccess: (data) => queryClient.setQueryData(queryKey, data),
    onError: (error) => {
      if (error instanceof CashierBarcodePendingChangedError || isCashierBarcodeChangedError(error)) {
        void persistPendingConfirmation(null).catch(() => undefined);
        // 实体标签已打印但后台条码已变化，立即重新加载，避免继续显示过期条码。
        void queryClient.invalidateQueries({ queryKey });
        Alert.alert(t("cashierBarcode.changedTitle"), t("cashierBarcode.changed"));
        return;
      }
      if (error instanceof CashierBarcodePrintConfirmationError) {
        Alert.alert(
          t("cashierBarcode.confirmationPendingTitle"),
          t("cashierBarcode.confirmationPending")
        );
        return;
      }
      if (error instanceof CashierBarcodeRevalidationError) {
        void queryClient.invalidateQueries({ queryKey });
        Alert.alert(t("cashierBarcode.revalidationFailedTitle"), t("cashierBarcode.revalidationFailed"));
        return;
      }
      Alert.alert(
        t("cashierBarcode.printFailed"),
        t(`cashierBarcode.printErrors.${classifyCashierBarcodePrintError(error)}`),
        [
          { text: t("common:actions.cancel"), style: "cancel" },
          { text: t("cashierBarcode.openPrinterSettings"), onPress: () => router.navigate("/(tabs)/settings") },
        ]
      );
    },
  });

  useEffect(() => {
    pendingIdentityRef.current = userIdentity;
    const secureSession = activateCashierPrintSecureSession(userIdentity);
    secureSessionRef.current = secureSession;
    mismatchHandledRef.current = "";
    setPendingConfirmation(null);
    setPendingLoaded(false);
    let cancelled = false;
    void loadCashierBarcodePrintPending(SecureStorage, userIdentity, secureSession)
      .then((value) => {
        if (!cancelled && pendingIdentityRef.current === userIdentity) {
          setPendingConfirmation(value);
          setPendingLoaded(true);
        }
      })
      .catch(() => {
        if (!cancelled && pendingIdentityRef.current === userIdentity) {
          // 无法确认磁盘上是否存在未完成打印时保持操作禁用，避免重复出纸。
          setPendingLoaded(false);
          Alert.alert(t("cashierBarcode.recoveryFailedTitle"), t("cashierBarcode.recoveryFailed"));
        }
      });
    return () => { cancelled = true; };
  }, [t, userIdentity]);

  useEffect(() => {
    const subscription = AppState.addEventListener("change", (state) => {
      if (state === "active") {
        void queryClient.invalidateQueries({ queryKey });
      }
    });
    return () => subscription.remove();
  }, [queryClient, queryKey]);

  useEffect(() => {
    const currentBarcode = barcodeQuery.data?.barcode;
    if (!pendingLoaded || !pendingConfirmation || !currentBarcode
      || pendingConfirmation.barcode === currentBarcode
      || mismatchHandledRef.current === pendingConfirmation.attemptId) {
      return;
    }
    mismatchHandledRef.current = pendingConfirmation.attemptId;
    // 恢复到历史条码 attempt 时立即清理，禁止确认旧码或自动打印新码。
    void persistPendingConfirmation(null).then(() => {
      void queryClient.invalidateQueries({ queryKey });
      Alert.alert(t("cashierBarcode.changedTitle"), t("cashierBarcode.changed"));
    }).catch(() => undefined);
  }, [barcodeQuery.data?.barcode, pendingConfirmation, pendingLoaded, persistPendingConfirmation, queryClient, queryKey, t]);

  const handlePrintPress = () => {
    if (pendingConfirmation?.phase !== "printing") {
      printMutation.mutate("default");
      return;
    }
    Alert.alert(t("cashierBarcode.uncertainTitle"), t("cashierBarcode.uncertainDescription"), [
      { text: t("common:actions.cancel"), style: "cancel" },
      {
        text: t("cashierBarcode.notPrintedReprint"),
        style: "destructive",
        onPress: () => printMutation.mutate("reprintUncertain"),
      },
      {
        text: t("cashierBarcode.printedConfirmOnly"),
        onPress: () => printMutation.mutate("confirmUncertain"),
      },
    ]);
  };

  const confirmRefresh = () => {
    Alert.alert(t("cashierBarcode.refreshTitle"), t("cashierBarcode.refreshDescription"), [
      { text: t("common:actions.cancel"), style: "cancel" },
      { text: t("cashierBarcode.refresh"), style: "destructive", onPress: () => refreshMutation.mutate() },
    ]);
  };

  const busy = refreshMutation.isPending || printMutation.isPending || !pendingLoaded;
  const refreshAllowed = canRefreshCashierBarcode(pendingConfirmation, pendingLoaded);
  return (
    <Surface style={styles.card} elevation={1}>
      <View style={styles.header}>
        <View style={styles.titleBlock}>
          <Text variant="titleMedium">{t("cashierBarcode.title")}</Text>
          <Text variant="bodySmall" style={styles.muted}>{employeeName}</Text>
        </View>
        {barcodeQuery.data?.exists ? (
          <Text variant="labelSmall" style={styles.muted}>
            {t("cashierBarcode.printCount", { count: barcodeQuery.data.printCount })}
          </Text>
        ) : null}
      </View>

      {barcodeQuery.isLoading ? (
        <View style={styles.centered}><ActivityIndicator /><Text>{t("cashierBarcode.loading")}</Text></View>
      ) : barcodeQuery.isError ? (
        <View style={styles.centered}>
          <HelperText type="error" visible>{t("cashierBarcode.loadFailed")}</HelperText>
          <Button mode="outlined" icon="refresh" onPress={() => void barcodeQuery.refetch()}>{t("common:actions.retry")}</Button>
        </View>
      ) : barcodeQuery.data?.exists && barcodeQuery.data.barcode ? (
        <ProductBarcodeImage value={barcodeQuery.data.barcode} />
      ) : (
        <Text variant="bodyMedium" style={styles.muted}>{t("cashierBarcode.empty")}</Text>
      )}

      <View style={styles.actions}>
        <Button mode="outlined" icon="refresh" onPress={confirmRefresh} loading={refreshMutation.isPending} disabled={busy || !refreshAllowed} compact>
          {t("cashierBarcode.refresh")}
        </Button>
        <Button
          mode="contained"
          icon="printer"
          onPress={handlePrintPress}
          loading={printMutation.isPending}
          disabled={busy || !barcodeQuery.data?.exists}
          compact
        >
          {pendingConfirmation?.phase === "printing"
            ? t("cashierBarcode.resolvePrintStatus")
            : pendingConfirmation
              ? t("cashierBarcode.retryConfirmation")
              : t("cashierBarcode.print")}
        </Button>
      </View>
      <HelperText type="error" visible={refreshMutation.isError}>{t("cashierBarcode.refreshFailed")}</HelperText>
    </Surface>
  );
}

const styles = StyleSheet.create({
  card: { padding: 18, borderRadius: 18, gap: 12 },
  header: { flexDirection: "row", alignItems: "flex-start", justifyContent: "space-between", gap: 12 },
  titleBlock: { flex: 1, gap: 3 },
  muted: { color: "#667085" },
  centered: { alignItems: "center", justifyContent: "center", gap: 8, minHeight: 72 },
  actions: { flexDirection: "row", justifyContent: "flex-end", gap: 8 },
});
