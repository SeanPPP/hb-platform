import { useCallback, useEffect, useRef, useState, type MutableRefObject } from "react";
import { ScrollView, StyleSheet, useWindowDimensions, View } from "react-native";
import * as Crypto from "expo-crypto";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useRouter } from "expo-router";
import QRCode from "react-native-qrcode-svg";
import { ActivityIndicator, Button, Dialog, Divider, Portal, Text } from "react-native-paper";
import {
  confirmStaffCashierBarcodePrintApi,
  ensureStaffCashierBarcodeApi,
  getStaffCashierBarcodeApi,
} from "@/modules/users/api";
import {
  clearStaffBarcodeBatch,
  createStaffBarcodeBatch,
  hasIncompleteStaffBarcodeConfirmation,
  invalidateStaffBarcodeOperationSession,
  isStaffBarcodeOperationSessionCurrent,
  loadStaffBarcodeBatch,
  processNextStaffBarcodeTarget,
  resolveStaffBarcodeUncertain,
  runStaffBarcodeActionExclusive,
  saveStaffBarcodeBatch,
  skipStaffBarcodeTarget,
  updateStaffBarcodeOperationSession,
  type StaffBarcodeBatch,
  type StaffBarcodeBatchTarget,
  type StaffBarcodeOperationSession,
} from "@/modules/users/staff-barcode/controller";
import { staffBarcodeQueueSecureStorage } from "@/modules/users/staff-barcode/storage";
import type { StoreUserListItem } from "@/modules/users/types";
import { hydrateSavedPrinter, printEmployeeCashierBarcodeLabel } from "@/modules/printer/api";
import { runPersonalCodePrintExclusive } from "@/modules/printer/personal-code-print-lock";
import { usePrinterStore } from "@/modules/printer/state";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { HB_COLORS, HB_RADIUS, HB_SPACING } from "@/shared/theme/tokens";

function queryKey(actorGuid: string, storeCode: string, userGuid: string, generation: number) {
  return ["staffCashierBarcode", actorGuid, storeCode, userGuid, generation] as const;
}

function useOperationSession(key: string, active: boolean) {
  const sessionRef = useRef<StaffBarcodeOperationSession>({ key, active, generation: 1 });
  const next = updateStaffBarcodeOperationSession(sessionRef.current, key, active);
  if (next !== sessionRef.current) {
    // visible 的每次开关都会递增，避免同一 actor/store 快速关闭重开后旧请求通过 ABA 比较。
    sessionRef.current = next;
  }
  useEffect(() => () => {
    // 卸载不一定再触发 render，必须主动让所有旧 await token 失效。
    sessionRef.current = invalidateStaffBarcodeOperationSession(sessionRef.current);
  }, []);
  return sessionRef;
}

function isCurrentSession(sessionRef: MutableRefObject<StaffBarcodeOperationSession>, generation: number) {
  return isStaffBarcodeOperationSessionCurrent(sessionRef.current, generation);
}

function dependencies(
  queryClient: ReturnType<typeof useQueryClient>,
  actorGuid: string,
  generation: number,
  isScopeCurrent: () => boolean
) {
  return {
    ensureBarcode: async (target: StaffBarcodeBatchTarget, storeCode: string) => {
      const result = await ensureStaffCashierBarcodeApi(target.userGuid, storeCode);
      if (!isScopeCurrent()) throw new Error("STAFF_BARCODE_SCOPE_CHANGED");
      queryClient.setQueryData(queryKey(actorGuid, storeCode, target.userGuid, generation), result);
      return result;
    },
    printLabel: async (target: StaffBarcodeBatchTarget, barcode: string) => {
      const printed = await printEmployeeCashierBarcodeLabel({
        employeeName: target.employeeName,
        username: target.username,
        barcode,
      });
      if (!printed) throw new Error("STAFF_BARCODE_PRINT_NOT_ACCEPTED");
    },
    confirmPrint: async (target: StaffBarcodeBatchTarget, storeCode: string, pending: { barcode: string; attemptId: string }) => {
      const result = await confirmStaffCashierBarcodePrintApi(target.userGuid, storeCode, pending.barcode, pending.attemptId);
      if (!isScopeCurrent()) throw new Error("STAFF_BARCODE_SCOPE_CHANGED");
      queryClient.setQueryData(queryKey(actorGuid, storeCode, target.userGuid, generation), result);
      return result;
    },
    createAttemptId: () => Crypto.randomUUID(),
    runExclusive: runPersonalCodePrintExclusive,
    isScopeCurrent,
  };
}

export function StaffBarcodeDialog({
  actorGuid,
  storeCode,
  user,
  visible,
  onDismiss,
}: {
  actorGuid: string;
  storeCode: string;
  user: StoreUserListItem | null;
  visible: boolean;
  onDismiss: () => void;
}) {
  const { t } = useAppTranslation(["userManagement", "common"]);
  const { fontScale, width } = useWindowDimensions();
  const router = useRouter();
  const queryClient = useQueryClient();
  const printer = usePrinterStore((state) => state.savedPrinter);
  const printerStatus = usePrinterStore((state) => state.status);
  const printerHydrated = usePrinterStore((state) => state.hydrated);
  const [operation, setOperation] = useState<StaffBarcodeBatch | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [pendingLoaded, setPendingLoaded] = useState(false);
  const [restoreFailed, setRestoreFailed] = useState(false);
  const actionGateRef = useRef({ inFlight: false });
  const userGuid = user?.userGUID ?? "";
  const operationScope = userGuid ? `single:${userGuid}` : "single:none";
  const sessionRef = useOperationSession(
    `${actorGuid}:${storeCode}:${userGuid}`,
    visible && Boolean(actorGuid && storeCode && userGuid)
  );
  const sessionGeneration = sessionRef.current.generation;
  const barcodeQuery = useQuery({
    queryKey: queryKey(actorGuid, storeCode, userGuid, sessionGeneration),
    enabled: visible && Boolean(storeCode && userGuid && user?.status === 1),
    queryFn: () => getStaffCashierBarcodeApi(userGuid, storeCode),
  });

  useEffect(() => {
    setOperation(null);
    setPendingLoaded(false);
    setRestoreFailed(false);
    setError("");
    setBusy(false);
    if (!visible || !actorGuid || !storeCode || !userGuid) return;
    const generation = sessionRef.current.generation;
    void loadStaffBarcodeBatch(staffBarcodeQueueSecureStorage, actorGuid, storeCode, operationScope)
      .then((saved) => {
        if (!isCurrentSession(sessionRef, generation)) return;
        setOperation(saved);
        setPendingLoaded(true);
      })
      .catch(() => {
        if (!isCurrentSession(sessionRef, generation)) return;
        setRestoreFailed(true);
        setError(t("staffBarcode.errors.restore"));
      });
  }, [actorGuid, operationScope, sessionRef, storeCode, t, userGuid, visible]);

  useEffect(() => {
    if (!visible || printerHydrated) return;
    void hydrateSavedPrinter().catch(() => undefined);
  }, [printerHydrated, visible]);

  const createCode = useCallback(async () => {
    if (!user) return;
    await runStaffBarcodeActionExclusive(actionGateRef.current, async () => {
    const generation = sessionRef.current.generation;
    setBusy(true);
    setError("");
    try {
      const result = await ensureStaffCashierBarcodeApi(user.userGUID, storeCode);
      if (!isCurrentSession(sessionRef, generation)) return;
      queryClient.setQueryData(queryKey(actorGuid, storeCode, user.userGUID, generation), result);
    } catch {
      if (isCurrentSession(sessionRef, generation)) setError(t("staffBarcode.errors.unavailable"));
    } finally {
      if (isCurrentSession(sessionRef, generation)) setBusy(false);
    }
    });
  }, [actorGuid, queryClient, sessionRef, storeCode, t, user]);

  const printCode = useCallback(async () => {
    if (!user || !pendingLoaded || restoreFailed) return;
    await runStaffBarcodeActionExclusive(actionGateRef.current, async () => {
    const generation = sessionRef.current.generation;
    const persist = async (next: StaffBarcodeBatch) => {
      // 旧作用域仍写回自己的安全记录，但绝不再污染新账号/新分店界面。
      await saveStaffBarcodeBatch(staffBarcodeQueueSecureStorage, next, operationScope);
      if (isCurrentSession(sessionRef, generation)) setOperation(next);
    };
    setBusy(true);
    setError("");
    try {
      const initial = operation ?? createStaffBarcodeBatch({
        actorGuid,
        storeCode,
        targets: [{
          userGuid: user.userGUID,
          employeeName: user.fullName || user.username,
          username: user.username,
          active: user.status === 1,
        }],
      });
      await persist(initial);
      const next = await processNextStaffBarcodeTarget(
        initial,
        dependencies(queryClient, actorGuid, generation, () => isCurrentSession(sessionRef, generation)),
        persist
      );
      if (!isCurrentSession(sessionRef, generation)) return;
      const target = next.targets[0];
      if (target.status === "done") {
        await clearStaffBarcodeBatch(staffBarcodeQueueSecureStorage, actorGuid, storeCode, operationScope);
        if (isCurrentSession(sessionRef, generation)) setOperation(null);
      } else if (target.status === "obsolete") {
        await clearStaffBarcodeBatch(staffBarcodeQueueSecureStorage, actorGuid, storeCode, operationScope);
        if (!isCurrentSession(sessionRef, generation)) return;
        setOperation(null);
        await barcodeQuery.refetch();
        if (isCurrentSession(sessionRef, generation)) setError(t("staffBarcode.errors.obsolete"));
      } else if (target.status === "uncertain") {
        setError(t("staffBarcode.errors.uncertain"));
      } else if (target.status === "failed" && target.pendingConfirmation?.phase === "printed") {
        setError(t("staffBarcode.confirmOnlyHint"));
      } else {
        setError(t("staffBarcode.errors.printFailed"));
      }
    } catch {
      if (isCurrentSession(sessionRef, generation)) setError(t("staffBarcode.errors.printFailed"));
    } finally {
      if (isCurrentSession(sessionRef, generation)) setBusy(false);
    }
    });
  }, [actorGuid, barcodeQuery, operation, operationScope, pendingLoaded, queryClient, restoreFailed, sessionRef, storeCode, t, user]);

  const resolveUncertain = useCallback(async (choice: "printed" | "notPrinted") => {
    if (!operation || !user) return;
    await runStaffBarcodeActionExclusive(actionGateRef.current, async () => {
    const generation = sessionRef.current.generation;
    const next = resolveStaffBarcodeUncertain(operation, user.userGUID, choice);
    await saveStaffBarcodeBatch(staffBarcodeQueueSecureStorage, next, operationScope);
    if (isCurrentSession(sessionRef, generation)) {
      setOperation(next);
      setError(choice === "printed" ? t("staffBarcode.confirmOnlyHint") : "");
    }
    });
  }, [operation, operationScope, sessionRef, t, user]);

  const openPrinterSettings = useCallback(() => {
    onDismiss();
    router.navigate("/(shell)/settings");
  }, [onDismiss, router]);

  const currentTarget = operation?.targets[0];
  const confirmationOnly = currentTarget?.status === "failed"
    && currentTarget.pendingConfirmation?.phase === "printed";
  const stackActions = fontScale > 1.2 || width < 380;
  const data = barcodeQuery.isError ? undefined : barcodeQuery.data;
  const canOperate = Boolean(user && user.status === 1);

  return (
    <Portal>
      <Dialog visible={visible} onDismiss={busy ? undefined : onDismiss} testID="staff-barcode-dialog">
        <Dialog.Title>{t("staffBarcode.title")}</Dialog.Title>
        <Dialog.ScrollArea>
          <ScrollView contentContainerStyle={styles.content}>
            <View style={styles.identity}>
              <Text variant="titleMedium">{user?.fullName || user?.username}</Text>
              <Text variant="bodySmall" style={styles.muted}>@{user?.username} · {storeCode}</Text>
            </View>
            {!canOperate ? <Text style={styles.error}>{t("staffBarcode.errors.inactive")}</Text> : null}
            {barcodeQuery.isLoading ? <ActivityIndicator /> : null}
            {barcodeQuery.isError ? <Text style={styles.error}>{t("staffBarcode.errors.unavailable")}</Text> : null}
            {!barcodeQuery.isLoading && !barcodeQuery.isError && !data?.exists ? (
              <Text variant="bodySmall" style={styles.muted}>{t("staffBarcode.emptyDescription")}</Text>
            ) : null}
            {data?.exists && data.barcode ? (
              <View style={styles.codePanel}>
                <QRCode value={data.barcode} size={176} />
                <Text variant="titleMedium" selectable>{data.barcode}</Text>
                <Text variant="bodySmall" style={styles.muted}>{t("staffBarcode.printCount", { count: data.printCount })}</Text>
              </View>
            ) : null}
            <Divider />
            <Text variant="bodyMedium">{t("staffBarcode.printer", {
              printer: printer?.name || t("staffBarcode.noPrinter"),
              status: t(`staffBarcode.printerStatuses.${printerStatus}`, printerStatus),
            })}</Text>
            <Button compact mode="text" icon="cog-outline" onPress={openPrinterSettings} disabled={busy}>
              {t("staffBarcode.actions.printerSettings")}
            </Button>
            {currentTarget?.status === "uncertain" ? (
              <View style={styles.actions}>
                <Text style={styles.error}>{t("staffBarcode.errors.uncertain")}</Text>
                <Button mode="contained-tonal" onPress={() => void resolveUncertain("printed")}>{t("staffBarcode.actions.printed")}</Button>
                <Button mode="outlined" onPress={() => void resolveUncertain("notPrinted")}>{t("staffBarcode.actions.notPrinted")}</Button>
              </View>
            ) : null}
            {confirmationOnly && !error ? <Text style={styles.warning}>{t("staffBarcode.confirmOnlyHint")}</Text> : null}
            {error ? <Text style={styles.error}>{error}</Text> : null}
          </ScrollView>
        </Dialog.ScrollArea>
        <Dialog.Actions style={[styles.dialogActions, stackActions && styles.dialogActionsStacked]}>
          <Button style={stackActions ? styles.stackedButton : undefined} onPress={onDismiss} disabled={busy}>{t("actions.cancel")}</Button>
          {!data?.exists ? <Button style={stackActions ? styles.stackedButton : undefined} onPress={() => void createCode()} disabled={!canOperate || busy || !pendingLoaded || restoreFailed}>{t("staffBarcode.actions.create")}</Button> : null}
          <Button style={stackActions ? styles.stackedButton : undefined} mode="contained" onPress={() => void printCode()} loading={busy} disabled={!canOperate || busy || !pendingLoaded || restoreFailed || currentTarget?.status === "uncertain"}>
            {confirmationOnly ? t("staffBarcode.actions.retryConfirmation") : data?.exists ? t("staffBarcode.actions.print") : t("staffBarcode.actions.createAndPrint")}
          </Button>
        </Dialog.Actions>
      </Dialog>
    </Portal>
  );
}

export function StaffBarcodeBatchDialog({
  actorGuid,
  storeCode,
  users,
  visible,
  onDismiss,
}: {
  actorGuid: string;
  storeCode: string;
  users: StoreUserListItem[];
  visible: boolean;
  onDismiss: () => void;
}) {
  const { t } = useAppTranslation(["userManagement", "common"]);
  const { fontScale, width } = useWindowDimensions();
  const router = useRouter();
  const queryClient = useQueryClient();
  const printer = usePrinterStore((state) => state.savedPrinter);
  const printerStatus = usePrinterStore((state) => state.status);
  const printerHydrated = usePrinterStore((state) => state.hydrated);
  const [batch, setBatch] = useState<StaffBarcodeBatch | null>(null);
  const [busy, setBusy] = useState(false);
  const [pendingLoaded, setPendingLoaded] = useState(false);
  const [restoreFailed, setRestoreFailed] = useState(false);
  const actionGateRef = useRef({ inFlight: false });
  const sessionRef = useOperationSession(
    `${actorGuid}:${storeCode}`,
    visible && Boolean(actorGuid && storeCode)
  );

  useEffect(() => {
    setBatch(null);
    setPendingLoaded(false);
    setRestoreFailed(false);
    setBusy(false);
    if (!visible || !actorGuid || !storeCode) return;
    const generation = sessionRef.current.generation;
    void loadStaffBarcodeBatch(staffBarcodeQueueSecureStorage, actorGuid, storeCode)
      .then((saved) => {
        if (!isCurrentSession(sessionRef, generation)) return;
        setBatch(saved ?? createStaffBarcodeBatch({
          actorGuid,
          storeCode,
          targets: users.map((user) => ({ userGuid: user.userGUID, employeeName: user.fullName || user.username, username: user.username, active: user.status === 1 })),
        }));
        setPendingLoaded(true);
      })
      .catch(() => {
        if (isCurrentSession(sessionRef, generation)) setRestoreFailed(true);
      });
  }, [actorGuid, sessionRef, storeCode, users, visible]);

  useEffect(() => {
    if (!visible || printerHydrated) return;
    void hydrateSavedPrinter().catch(() => undefined);
  }, [printerHydrated, visible]);

  const nextActionable = batch?.targets.find((target) => target.status === "pending" || target.status === "failed");
  const nextConfirmationOnly = nextActionable?.status === "failed"
    && nextActionable.pendingConfirmation?.phase === "printed";
  const uncertain = batch?.targets.find((target) => target.status === "uncertain");
  const runNext = useCallback(async () => {
    if (!batch || uncertain || !pendingLoaded || restoreFailed) return;
    await runStaffBarcodeActionExclusive(actionGateRef.current, async () => {
    const generation = sessionRef.current.generation;
    const persist = async (next: StaffBarcodeBatch) => {
      await saveStaffBarcodeBatch(staffBarcodeQueueSecureStorage, next);
      if (isCurrentSession(sessionRef, generation)) setBatch(next);
    };
    setBusy(true);
    try {
      let current = batch;
      // 正常项串行连续处理；遇到失败或不确定立即暂停，把决定权交还给用户。
      while (isCurrentSession(sessionRef, generation)) {
        const actionable = current.targets.find((target) => target.status === "pending" || target.status === "failed");
        if (!actionable) break;
        const next = await processNextStaffBarcodeTarget(
          current,
          dependencies(queryClient, actorGuid, generation, () => isCurrentSession(sessionRef, generation)),
          persist
        );
        const result = next.targets.find((target) => target.userGuid === actionable.userGuid);
        current = next;
        if (!result || result.status !== "done") break;
      }
    } finally {
      if (isCurrentSession(sessionRef, generation)) setBusy(false);
    }
    });
  }, [actorGuid, batch, pendingLoaded, queryClient, restoreFailed, sessionRef, uncertain]);
  const resolveUncertain = useCallback(async (choice: "printed" | "notPrinted") => {
    if (!batch || !uncertain) return;
    await runStaffBarcodeActionExclusive(actionGateRef.current, async () => {
    const generation = sessionRef.current.generation;
    const next = resolveStaffBarcodeUncertain(batch, uncertain.userGuid, choice);
    await saveStaffBarcodeBatch(staffBarcodeQueueSecureStorage, next);
    if (isCurrentSession(sessionRef, generation)) setBatch(next);
    });
  }, [batch, sessionRef, uncertain]);
  const skip = useCallback(async () => {
    if (!batch || !nextActionable) return;
    await runStaffBarcodeActionExclusive(actionGateRef.current, async () => {
    const generation = sessionRef.current.generation;
    const next = skipStaffBarcodeTarget(batch, nextActionable.userGuid);
    await saveStaffBarcodeBatch(staffBarcodeQueueSecureStorage, next);
    if (isCurrentSession(sessionRef, generation)) setBatch(next);
    });
  }, [batch, nextActionable, sessionRef]);
  const finish = useCallback(async () => {
    if (!pendingLoaded || restoreFailed) return;
    await runStaffBarcodeActionExclusive(actionGateRef.current, async () => {
    const generation = sessionRef.current.generation;
    if (!hasIncompleteStaffBarcodeConfirmation(batch)) {
      await clearStaffBarcodeBatch(staffBarcodeQueueSecureStorage, actorGuid, storeCode);
    }
    if (isCurrentSession(sessionRef, generation)) {
      setBatch(null);
      onDismiss();
    }
    });
  }, [actorGuid, batch, onDismiss, pendingLoaded, restoreFailed, sessionRef, storeCode]);

  const openPrinterSettings = useCallback(() => {
    onDismiss();
    router.navigate("/(shell)/settings");
  }, [onDismiss, router]);

  const done = batch?.targets.filter((target) => target.status === "done").length ?? 0;
  const stackActions = fontScale > 1.2 || width < 380;
  return (
    <Portal>
      <Dialog visible={visible} onDismiss={busy ? undefined : onDismiss} testID="staff-barcode-batch-dialog">
        <Dialog.Title>{t("staffBarcode.batch.title", { count: users.length })}</Dialog.Title>
        <Dialog.ScrollArea>
          <ScrollView contentContainerStyle={styles.content}>
            <Text>{t("staffBarcode.batch.summary", { store: storeCode, done, total: batch?.targets.length ?? users.length })}</Text>
            <Text variant="bodySmall" style={styles.muted}>{t("staffBarcode.printer", { printer: printer?.name || t("staffBarcode.noPrinter"), status: t(`staffBarcode.printerStatuses.${printerStatus}`, printerStatus) })}</Text>
            <Button compact mode="text" icon="cog-outline" onPress={openPrinterSettings} disabled={busy}>{t("staffBarcode.actions.printerSettings")}</Button>
            {restoreFailed ? <Text style={styles.error}>{t("staffBarcode.errors.restore")}</Text> : null}
            {batch?.targets.map((target) => (
              <View key={target.userGuid} style={styles.queueRow}>
                <View style={styles.queueIdentity}><Text>{target.employeeName}</Text><Text variant="bodySmall" style={styles.muted}>@{target.username}</Text></View>
                <Text variant="labelMedium">{target.status === "failed" && target.pendingConfirmation?.phase === "printed"
                  ? t("staffBarcode.batch.statuses.confirmationPending")
                  : t(`staffBarcode.batch.statuses.${target.status}`)}</Text>
              </View>
            ))}
            {uncertain ? (
              <View style={styles.actions}>
                <Text style={styles.error}>{t("staffBarcode.errors.uncertain")}</Text>
                <Button mode="contained-tonal" onPress={() => void resolveUncertain("printed")}>{t("staffBarcode.actions.printed")}</Button>
                <Button mode="outlined" onPress={() => void resolveUncertain("notPrinted")}>{t("staffBarcode.actions.notPrinted")}</Button>
              </View>
            ) : null}
          </ScrollView>
        </Dialog.ScrollArea>
        <Dialog.Actions style={[styles.dialogActions, stackActions && styles.dialogActionsStacked]}>
          <Button style={stackActions ? styles.stackedButton : undefined} onPress={() => void finish()} disabled={busy || !pendingLoaded || restoreFailed}>{t("staffBarcode.actions.end")}</Button>
          {nextActionable && !uncertain ? <Button style={stackActions ? styles.stackedButton : undefined} onPress={() => void skip()} disabled={busy}>{t("staffBarcode.actions.skip")}</Button> : null}
          <Button style={stackActions ? styles.stackedButton : undefined} mode="contained" onPress={() => void runNext()} loading={busy} disabled={busy || !pendingLoaded || restoreFailed || Boolean(uncertain) || !nextActionable}>
            {nextConfirmationOnly ? t("staffBarcode.actions.retryConfirmation") : done ? t("staffBarcode.actions.continue") : t("staffBarcode.actions.start")}
          </Button>
        </Dialog.Actions>
      </Dialog>
    </Portal>
  );
}

const styles = StyleSheet.create({
  actions: { gap: HB_SPACING.sm },
  codePanel: { alignItems: "center", gap: HB_SPACING.sm, paddingVertical: HB_SPACING.sm },
  content: { gap: HB_SPACING.md, paddingBottom: HB_SPACING.sm },
  dialogActions: { flexWrap: "wrap" },
  dialogActionsStacked: { alignItems: "stretch", flexDirection: "column" },
  error: { color: HB_COLORS.danger },
  identity: { gap: 2 },
  muted: { color: HB_COLORS.textSecondary },
  queueIdentity: { flex: 1, minWidth: 0 },
  queueRow: { alignItems: "center", borderColor: HB_COLORS.outlineMuted, borderRadius: HB_RADIUS.control, borderWidth: StyleSheet.hairlineWidth, flexDirection: "row", gap: HB_SPACING.sm, padding: HB_SPACING.sm },
  stackedButton: { alignSelf: "stretch" },
  warning: { color: HB_COLORS.warning },
});
