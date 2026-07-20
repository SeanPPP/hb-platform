import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  Alert,
  FlatList,
  Image,
  KeyboardAvoidingView,
  Platform,
  StyleSheet,
  View,
} from "react-native";
import { type Href, useLocalSearchParams, useNavigation, useRouter } from "expo-router";
import {
  usePreventRemove,
  type NavigationAction,
} from "@react-navigation/native";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Button,
  Card,
  Chip,
  IconButton,
  Searchbar,
  Snackbar,
  Text,
  TextInput,
} from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import { EmptyState } from "@/components/ui/EmptyState";
import { LoadingOverlay } from "@/components/ui/LoadingOverlay";
import { useStores } from "@/modules/shop/use-stores";
import { resolveLocalizedErrorMessage } from "@/shared/i18n/error-message";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import {
  fetchActivePreorders,
  fetchPreorderActivation,
  readPreorderErrorCode,
  savePreorderDraft,
  submitPreorder,
} from "./api";
import { formatBrisbaneBusinessDate } from "./business-date";
import {
  buildDraftItems,
  createPackCounts,
  normalizePackCount,
  serializePackCounts,
  summarizePreorder,
  type PreorderPackCounts,
} from "./order-state";
import { drainLatestDraft, isDraftContextCurrent } from "./draft-drain";
import {
  mergePreorderDraftCacheDetail,
  preorderActivationQueryKey,
} from "./draft-cache";
import { resolveDraftConflict, type DraftConflictChoice } from "./draft-conflict";
import { getPreorderActivationReadOnlyReason, isEditablePreorderOrderStatus } from "./availability";
import {
  applySubmitResultToContext,
  createPreorderActiveMutationEpoch,
  createPreorderSubmissionId,
  createPreorderSubmissionContext,
  isPreorderSubmissionContextCurrent,
  preparePreorderSubmission,
  reconcilePreorderSubmitFailure,
  refreshActivePreordersSingleFlight,
  settleSuccessfulPreorderSubmission,
  type PreorderDraftSaveRequestResult,
  type PreorderSubmissionContext,
} from "./submission-state";
import {
  createPreorderSubmissionTelemetry,
  type PreorderSubmissionTelemetry,
} from "./submission-observability";
import { preorderActiveQueryKey } from "./use-preorder-gate";
import type {
  ActivePreordersResult,
  PreorderActivationDetail,
  PreorderActivationItem,
} from "./types";

type SaveStatus = "idle" | "saving" | "saved" | "error";

interface DraftSaveSnapshot {
  contextKey: string;
  activationGuid: string;
  storeCode: string;
  queryKey: ReturnType<typeof preorderActivationQueryKey>;
  fingerprint: string;
  items: ReturnType<typeof buildDraftItems>;
}

interface DraftDrainContext {
  key: string;
  inFlight: { current: Promise<boolean> | null };
  currentSave: { current: Promise<PreorderDraftSaveRequestResult> | null };
}

function firstParam(value: string | string[] | undefined) {
  return Array.isArray(value) ? value[0] : value;
}

function formatDateTime(value: string, language: string) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value || "--";
  return new Intl.DateTimeFormat(language === "en" ? "en-AU" : "zh-CN", {
    dateStyle: "medium",
    timeStyle: "short",
    timeZone: "Australia/Brisbane",
  }).format(date);
}

function formatMoney(value: number, language: string) {
  return new Intl.NumberFormat(language === "en" ? "en-AU" : "zh-CN", {
    style: "currency",
    currency: "AUD",
  }).format(value);
}

export function PreorderDetailScreen() {
  const router = useRouter();
  const navigation = useNavigation();
  const params = useLocalSearchParams<{ activationGuid?: string | string[]; storeCode?: string | string[] }>();
  const activationGuid = firstParam(params.activationGuid)?.trim() || "";
  const routeStoreCode = firstParam(params.storeCode)?.trim() || "";
  const { selectedStore, selectedStoreCode } = useStores();
  const storeCode = routeStoreCode || selectedStoreCode || "";
  const draftContextKey = `${activationGuid}:${storeCode}`;
  const { t, language } = useAppTranslation(["preorder", "common"]);
  const queryClient = useQueryClient();
  const [packCounts, setPackCounts] = useState<PreorderPackCounts>({});
  const [draftRevision, setDraftRevision] = useState(0);
  const [searchText, setSearchText] = useState("");
  const [saveStatus, setSaveStatus] = useState<SaveStatus>("idle");
  const [activationClock, setActivationClock] = useState(() => Date.now());
  const [snackbarMessage, setSnackbarMessage] = useState("");
  const [submitPending, setSubmitPending] = useState(false);
  const [leaveSavePending, setLeaveSavePending] = useState(false);
  const [allowRemove, setAllowRemove] = useState(false);
  const loadedKeyRef = useRef("");
  const detailRef = useRef<PreorderActivationDetail | undefined>(undefined);
  const packCountsRef = useRef(packCounts);
  const draftRevisionRef = useRef(draftRevision);
  const lastSavedFingerprintRef = useRef<string | null>(null);
  const lastAttemptedFingerprintRef = useRef("");
  const saveTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const autosaveSuspendedRef = useRef(false);
  const activeDraftContextKeyRef = useRef(draftContextKey);
  const draftDrainContextRef = useRef<DraftDrainContext>({
    key: draftContextKey,
    inFlight: { current: null },
    currentSave: { current: null },
  });
  const leaveSaveInFlightRef = useRef(false);
  const pendingRemoveActionRef = useRef<NavigationAction | null>(null);
  const lastDraftSaveErrorCodeRef = useRef<string | null>(null);
  const persistLatestDraftRef = useRef<() => Promise<boolean>>(async () => false);
  activeDraftContextKeyRef.current = draftContextKey;
  if (draftDrainContextRef.current.key !== draftContextKey) {
    // 新批次使用独立的单飞保存链；旧请求只能完成旧上下文，不能读取或写入新批次。
    draftDrainContextRef.current = {
      key: draftContextKey,
      inFlight: { current: null },
      currentSave: { current: null },
    };
  }

  const detailQuery = useQuery({
    queryKey: ["preorder", "activation", activationGuid, storeCode],
    enabled: Boolean(activationGuid && storeCode),
    queryFn: () => fetchPreorderActivation(activationGuid, storeCode),
    staleTime: 5_000,
    retry: 1,
  });
  const detail = detailQuery.data;
  detailRef.current = detail;
  packCountsRef.current = packCounts;
  draftRevisionRef.current = draftRevision;

  useEffect(() => {
    if (!detail) return;
    const now = Date.now();
    const nextBoundary = [Date.parse(detail.startAtUtc), Date.parse(detail.endAtUtc)]
      .filter((value) => Number.isFinite(value) && value > now)
      .sort((left, right) => left - right)[0];
    if (nextBoundary === undefined) return;
    const timer = setTimeout(
      () => setActivationClock(Date.now()),
      Math.min(2_147_483_647, Math.max(0, nextBoundary - now + 25))
    );
    return () => clearTimeout(timer);
  }, [activationClock, detail]);

  useEffect(() => {
    if (saveTimerRef.current) {
      clearTimeout(saveTimerRef.current);
      saveTimerRef.current = null;
    }
    pendingRemoveActionRef.current = null;
    lastDraftSaveErrorCodeRef.current = null;
    leaveSaveInFlightRef.current = false;
    autosaveSuspendedRef.current = false;
    setLeaveSavePending(false);
    setSubmitPending(false);
    setAllowRemove(false);
  }, [draftContextKey]);

  useEffect(() => {
    if (!detail) return;
    const loadKey = `${storeCode}:${activationGuid}`;
    if (loadedKeyRef.current === loadKey) return;

    // 只在进入新批次时用服务器草稿初始化，避免后台刷新覆盖用户尚未保存的输入。
    const initialCounts = createPackCounts(detail.items);
    const initialItems = buildDraftItems(detail.items, initialCounts);
    loadedKeyRef.current = loadKey;
    setPackCounts(initialCounts);
    packCountsRef.current = initialCounts;
    setDraftRevision(detail.draftRevision);
    draftRevisionRef.current = detail.draftRevision;
    lastSavedFingerprintRef.current = serializePackCounts(initialItems);
    lastAttemptedFingerprintRef.current = "";
    setSaveStatus("idle");
  }, [activationGuid, detail, storeCode]);

  const getErrorMessage = useCallback((error: unknown, fallbackKey: string) => {
    const code = readPreorderErrorCode(error);
    if (code === "PREORDER_DRAFT_CONFLICT") return t("errors.draftConflict");
    if (code === "PREORDER_NOT_ACTIVE") return t("errors.notActive");
    if (code === "PREORDER_REQUIRED") return t("errors.required");
    return resolveLocalizedErrorMessage(error, { language, t, fallbackKey });
  }, [language, t]);

  const applyDraftConflictChoice = useCallback((
    serverDetail: PreorderActivationDetail,
    choice: DraftConflictChoice
  ) => {
    if (!isDraftContextCurrent(activeDraftContextKeyRef.current, draftContextKey)) return;
    const recovered = resolveDraftConflict(serverDetail, packCountsRef.current, choice);
    detailRef.current = recovered.detail;
    queryClient.setQueryData(
      ["preorder", "activation", activationGuid, storeCode],
      recovered.detail
    );
    draftRevisionRef.current = recovered.draftRevision;
    setDraftRevision(recovered.draftRevision);
    lastAttemptedFingerprintRef.current = "";
    // local 选择没有服务器已保存指纹，置空后共享 drain 必须真正执行一次保存。
    lastSavedFingerprintRef.current = recovered.savedFingerprint ?? null;

    if (!recovered.shouldRetry) {
      packCountsRef.current = recovered.packCounts;
      setPackCounts(recovered.packCounts);
      setSaveStatus("saved");
      return;
    }

    // 保留本地输入时只推进到服务器最新 revision，再由同一保存链串行重试。
    setSaveStatus("idle");
    setTimeout(() => void persistLatestDraftRef.current(), 0);
  }, [activationGuid, draftContextKey, queryClient, storeCode]);

  const presentDraftConflict = useCallback((
    serverDetail: PreorderActivationDetail,
    source: "save" | "submit" = "save"
  ) => {
    if (!isEditablePreorderOrderStatus(serverDetail.orderStatus)) {
      // 另一设备已经提交时直接采用最终事实，不能继续覆盖已完成订单。
      applyDraftConflictChoice(serverDetail, "server");
      setSnackbarMessage(t("conflict.alreadySubmitted"));
      return;
    }

    if (leaveSaveInFlightRef.current) {
      // 离页时由统一未保存提示接管；这里只推进 revision，避免连续弹出两个对话框。
      detailRef.current = serverDetail;
      queryClient.setQueryData(
        ["preorder", "activation", activationGuid, storeCode],
        serverDetail
      );
      draftRevisionRef.current = serverDetail.draftRevision;
      setDraftRevision(serverDetail.draftRevision);
      lastAttemptedFingerprintRef.current = "";
      return;
    }

    Alert.alert(
      t("conflict.title"),
      source === "submit" ? t("conflict.submitMessage") : t("conflict.message"),
      [
        {
          text: t("conflict.useServer"),
          onPress: () => applyDraftConflictChoice(serverDetail, "server"),
        },
        {
          text: t("conflict.keepLocal"),
          onPress: () => applyDraftConflictChoice(serverDetail, "local"),
        },
      ],
      { cancelable: false }
    );
  }, [activationGuid, applyDraftConflictChoice, queryClient, storeCode, t]);

  const recoverDraftConflict = useCallback(async (snapshotContextKey: string) => {
    try {
      const serverDetail = await fetchPreorderActivation(activationGuid, storeCode);
      if (!isDraftContextCurrent(activeDraftContextKeyRef.current, snapshotContextKey)) return null;
      if (!autosaveSuspendedRef.current) presentDraftConflict(serverDetail);
      return serverDetail;
    } catch {
      setSnackbarMessage(t("errors.conflictRefreshFailed"));
      return null;
    }
  }, [activationGuid, presentDraftConflict, storeCode, t]);

  const persistLatestDraft = useCallback(() => {
    const contextKey = draftContextKey;
    const drainContext = draftDrainContextRef.current.key === contextKey
      ? draftDrainContextRef.current
      : {
          key: contextKey,
          inFlight: { current: null },
          currentSave: { current: null },
        };
    draftDrainContextRef.current = drainContext;
    return drainLatestDraft(
      drainContext.inFlight,
      () => {
        if (autosaveSuspendedRef.current) return null;
        if (!isDraftContextCurrent(activeDraftContextKeyRef.current, contextKey)) return null;
        const currentDetail = detailRef.current;
        if (!currentDetail || !activationGuid || !storeCode) return null;
        const items = buildDraftItems(currentDetail.items, packCountsRef.current);
        return {
          contextKey,
          activationGuid,
          storeCode,
          queryKey: preorderActivationQueryKey(activationGuid, storeCode),
          items,
          fingerprint: serializePackCounts(items),
        } satisfies DraftSaveSnapshot;
      },
      (snapshot) => snapshot.fingerprint === lastSavedFingerprintRef.current,
      async (snapshot) => {
        const {
          contextKey: snapshotContextKey,
          items,
          fingerprint,
        } = snapshot;
        if (!isDraftContextCurrent(activeDraftContextKeyRef.current, snapshotContextKey)) return false;
        lastAttemptedFingerprintRef.current = fingerprint;
        lastDraftSaveErrorCodeRef.current = null;
        setSaveStatus("saving");
        const saveRequest = (async (): Promise<PreorderDraftSaveRequestResult> => {
          try {
            const response = await savePreorderDraft(snapshot.activationGuid, {
              storeCode: snapshot.storeCode,
              expectedDraftRevision: draftRevisionRef.current,
              items,
            });
            let cachedDetail = response;
            queryClient.setQueryData<PreorderActivationDetail>(snapshot.queryKey, (current) => {
              cachedDetail = mergePreorderDraftCacheDetail(current, response);
              return cachedDetail;
            });
            if (!isDraftContextCurrent(activeDraftContextKeyRef.current, snapshotContextKey)) {
              return { kind: "failed" };
            }
            if (cachedDetail !== response) {
              // 当前缓存已知更高版本，本次旧响应不能再把 refs 或“已保存”指纹降级。
              draftRevisionRef.current = cachedDetail.draftRevision;
              setDraftRevision(cachedDetail.draftRevision);
              lastDraftSaveErrorCodeRef.current = "PREORDER_DRAFT_CONFLICT";
              setSaveStatus("error");
              setSnackbarMessage(t("errors.draftConflict"));
              if (!autosaveSuspendedRef.current) presentDraftConflict(cachedDetail);
              return { kind: "draft-conflict", detail: cachedDetail };
            }
            // 草稿版本只采用后端响应；如果保存期间又有输入，下一轮会继续保存最新快照。
            draftRevisionRef.current = response.draftRevision;
            setDraftRevision(response.draftRevision);
            lastSavedFingerprintRef.current = fingerprint;
            setSaveStatus("saved");
            return { kind: "saved" };
          } catch (error) {
            if (!isDraftContextCurrent(activeDraftContextKeyRef.current, snapshotContextKey)) {
              return { kind: "failed" };
            }
            const errorCode = readPreorderErrorCode(error) ?? null;
            lastDraftSaveErrorCodeRef.current = errorCode;
            setSaveStatus("error");
            setSnackbarMessage(getErrorMessage(error, "errors.saveFailed"));
            if (errorCode === "PREORDER_DRAFT_CONFLICT") {
              const conflictDetail = await recoverDraftConflict(snapshotContextKey);
              if (conflictDetail) return { kind: "draft-conflict", detail: conflictDetail };
            }
            return { kind: "failed" };
          }
        })();
        drainContext.currentSave.current = saveRequest;
        try {
          return (await saveRequest).kind === "saved";
        } finally {
          if (drainContext.currentSave.current === saveRequest) {
            drainContext.currentSave.current = null;
          }
        }
      }
    );
  }, [activationGuid, draftContextKey, getErrorMessage, presentDraftConflict, queryClient, recoverDraftConflict, storeCode, t]);
  persistLatestDraftRef.current = persistLatestDraft;

  const draftItems = useMemo(
    () => buildDraftItems(detail?.items ?? [], packCounts),
    [detail?.items, packCounts]
  );
  const draftFingerprint = useMemo(() => serializePackCounts(draftItems), [draftItems]);

  const isSubmitted = !isEditablePreorderOrderStatus(detail?.orderStatus);
  const returnedForRevision = detail?.orderStatus === "ReturnedForRevision";
  const activationReadOnlyReason = getPreorderActivationReadOnlyReason(detail, activationClock);
  const isEditable = Boolean(detail && !isSubmitted && !activationReadOnlyReason);
  const estimatedArrivalDate = formatBrisbaneBusinessDate(detail?.estimatedArrivalDate);

  useEffect(() => {
    if (!isEditable || autosaveSuspendedRef.current) return;
    if (
      draftFingerprint === lastSavedFingerprintRef.current ||
      draftFingerprint === lastAttemptedFingerprintRef.current
    ) {
      return;
    }

    if (saveTimerRef.current) clearTimeout(saveTimerRef.current);
    saveTimerRef.current = setTimeout(() => {
      saveTimerRef.current = null;
      void persistLatestDraft();
    }, 500);
    return () => {
      if (saveTimerRef.current) {
        clearTimeout(saveTimerRef.current);
        saveTimerRef.current = null;
      }
    };
  }, [draftFingerprint, isEditable, persistLatestDraft]);

  const summary = useMemo(
    () => summarizePreorder(detail?.items ?? [], packCounts),
    [detail?.items, packCounts]
  );
  const filteredItems = useMemo(() => {
    const keyword = searchText.trim().toLowerCase();
    if (!keyword) return detail?.items ?? [];
    return (detail?.items ?? []).filter((item) =>
      [item.itemNumber, item.productCode, item.productName]
        .some((value) => value.toLowerCase().includes(keyword))
    );
  }, [detail?.items, searchText]);
  const hasUnsavedDraft = Boolean(
    detail && !isSubmitted && draftFingerprint !== lastSavedFingerprintRef.current
  );

  const showLeaveFailureAlert = useCallback(() => {
    const notActive = lastDraftSaveErrorCodeRef.current === "PREORDER_NOT_ACTIVE";
    Alert.alert(
      notActive ? t("leave.notActiveTitle") : t("leave.unsavedTitle"),
      notActive ? t("leave.notActiveMessage") : t("leave.unsavedMessage"),
      [
        {
          text: t("leave.stay"),
          style: "cancel",
          onPress: () => {
            pendingRemoveActionRef.current = null;
          },
        },
        {
          text: t("leave.discard"),
          style: "destructive",
          onPress: () => setAllowRemove(true),
        },
      ],
      {
        cancelable: true,
        onDismiss: () => {
          pendingRemoveActionRef.current = null;
        },
      }
    );
  }, [t]);

  usePreventRemove((hasUnsavedDraft || submitPending) && !allowRemove, ({ data }) => {
    if (submitPending) {
      Alert.alert(t("submit.pendingTitle"), t("submit.pendingMessage"));
      return;
    }
    if (leaveSaveInFlightRef.current) return;
    if (saveTimerRef.current) {
      clearTimeout(saveTimerRef.current);
      saveTimerRef.current = null;
    }
    pendingRemoveActionRef.current = data.action;
    leaveSaveInFlightRef.current = true;
    setLeaveSavePending(true);
    // 离页动作必须等共享 drain 清空，并再次确认最新指纹已经成功保存。
    void persistLatestDraft()
      .then((saved) => {
        const latestDetail = detailRef.current;
        const latestFingerprint = latestDetail
          ? serializePackCounts(buildDraftItems(latestDetail.items, packCountsRef.current))
          : "";
        if (saved && latestFingerprint === lastSavedFingerprintRef.current) {
          setAllowRemove(true);
          return;
        }
        leaveSaveInFlightRef.current = false;
        setLeaveSavePending(false);
        // 网络、冲突或批次失效都必须允许明确放弃离开，避免返回键永久被锁死。
        showLeaveFailureAlert();
      })
      .catch(() => {
        leaveSaveInFlightRef.current = false;
        setLeaveSavePending(false);
        showLeaveFailureAlert();
      });
  });

  useEffect(() => {
    if (!allowRemove || !pendingRemoveActionRef.current) return;
    const action = pendingRemoveActionRef.current;
    pendingRemoveActionRef.current = null;
    navigation.dispatch(action);
  }, [allowRemove, navigation]);

  const updatePackCount = useCallback((itemGuid: string, nextValue: string | number) => {
    if (!isEditable || leaveSaveInFlightRef.current) return;
    setPackCounts((current) => ({
      ...current,
      [itemGuid]: normalizePackCount(nextValue),
    }));
    setSaveStatus("idle");
  }, [isEditable]);

  const showSubmissionSuccess = useCallback((submitContext: PreorderSubmissionContext) => {
    if (!isPreorderSubmissionContextCurrent(activeDraftContextKeyRef.current, submitContext)) return;
    const cachedActive = queryClient.getQueryData<ActivePreordersResult>(
      preorderActiveQueryKey(submitContext.storeCode)
    );
    const next = cachedActive?.activations.find(
      (item) => item.activationGuid !== submitContext.activationGuid
    );
    Alert.alert(
      t("submit.successTitle"),
      next ? t("submit.nextMessage", { name: next.templateName }) : t("submit.completedMessage"),
      [{
        text: next ? t("submit.nextAction") : t("submit.doneAction"),
        onPress: () => {
          if (!isPreorderSubmissionContextCurrent(activeDraftContextKeyRef.current, submitContext)) return;
          if (next) {
            router.replace({
              pathname: "/preorders/[activationGuid]",
              params: { activationGuid: next.activationGuid, storeCode: submitContext.storeCode },
            } as unknown as Href);
          } else {
            router.replace("/preorders" as Href);
          }
        },
      }],
      { cancelable: false }
    );
  }, [queryClient, router, t]);

  const refreshAfterSubmission = useCallback((
    submitContext: PreorderSubmissionContext,
    telemetry: PreorderSubmissionTelemetry
  ) => {
    const activeQueryKey = preorderActiveQueryKey(submitContext.storeCode);
    const mutationEpoch = createPreorderActiveMutationEpoch();
    // detail 已由 POST 终态覆盖；这里只标记过期，避免成功路径再发 detail GET。
    void queryClient.invalidateQueries({
      queryKey: preorderActivationQueryKey(submitContext.activationGuid, submitContext.storeCode),
      refetchType: "none",
    });
    // active 刷新期间 gate 必须保持 fail-closed；请求成功后由真实响应决定是否解锁。
    queryClient.setQueryData<ActivePreordersResult>(activeQueryKey, (current) => ({
      storeCode: submitContext.storeCode,
      normalOrderBlocked: true,
      activations: current?.activations ?? [],
    }));
    const refresh = refreshActivePreordersSingleFlight(submitContext.storeCode, mutationEpoch, async () => {
      // 先取消 POST 前的同 key GET，再重写 fail-closed 缓存并开启全新的查询 lane。
      await queryClient.cancelQueries({ queryKey: activeQueryKey, exact: true });
      queryClient.setQueryData<ActivePreordersResult>(activeQueryKey, (current) => ({
        storeCode: submitContext.storeCode,
        normalOrderBlocked: true,
        activations: current?.activations ?? [],
      }));
      telemetry.activeGet();
      return queryClient.fetchQuery({
        queryKey: activeQueryKey,
        queryFn: ({ signal }) => fetchActivePreorders(submitContext.storeCode, signal),
        staleTime: 0,
      });
    });
    return refresh.then(
      (result) => {
        telemetry.backgroundActiveRefreshFinish("success");
        return result;
      },
      (error) => {
        telemetry.backgroundActiveRefreshFinish("error");
        throw error;
      }
    );
  }, [queryClient]);

  const finishConfirmedSubmission = useCallback((
    submitContext: PreorderSubmissionContext,
    terminalDetail: PreorderActivationDetail,
    telemetry: PreorderSubmissionTelemetry
  ) => {
    settleSuccessfulPreorderSubmission({
      writeTerminalCache: () => {
        detailRef.current = terminalDetail;
        queryClient.setQueryData(
          preorderActivationQueryKey(submitContext.activationGuid, submitContext.storeCode),
          terminalDetail
        );
      },
      finishSubmitPending: () => setSubmitPending(false),
      showSuccess: () => {
        showSubmissionSuccess(submitContext);
        telemetry.successFeedback();
      },
      refreshInBackground: () => refreshAfterSubmission(submitContext, telemetry),
    });
  }, [queryClient, refreshAfterSubmission, showSubmissionSuccess]);

  const performSubmit = useCallback(async (confirmNoDemand: boolean) => {
    if (!detail || !isEditable || submitPending) return;
    const frozenItems = buildDraftItems(detail.items, packCountsRef.current);
    const submissionId = createPreorderSubmissionId();
    const initialRequestBody = {
      storeCode,
      expectedDraftRevision: draftRevisionRef.current,
      confirmNoDemand,
      items: frozenItems,
    };
    const telemetry = createPreorderSubmissionTelemetry({
      submissionId,
      itemCount: frozenItems.length,
      requestBody: initialRequestBody,
      hasInFlightSave: Boolean(draftDrainContextRef.current.currentSave.current),
    });
    telemetry.confirm();
    setSubmitPending(true);
    let submitContext: PreorderSubmissionContext | null = null;
    let submissionConfirmed = false;
    let postStarted = false;
    let postFinished = false;
    try {
      telemetry.waitSaveStart();
      const prepared = await preparePreorderSubmission({
        cancelScheduledAutosave: () => {
          if (saveTimerRef.current) clearTimeout(saveTimerRef.current);
          saveTimerRef.current = null;
        },
        suspendAutosaveRef: autosaveSuspendedRef,
        // 只等待当前 HTTP PUT（含冲突 detail GET），不能等待会因 suspend 返回 false 的完整 drain。
        inFlightSave: draftDrainContextRef.current.currentSave.current,
        readLatest: () => ({
          revision: draftRevisionRef.current,
          items: frozenItems,
        }),
      });
      telemetry.waitSaveEnd();
      if (prepared.kind === "draft-conflict") {
        // PUT 已经取得服务器 detail；提交复用该事实协调，到此终止，不发 POST/第二次 GET。
        presentDraftConflict(prepared.detail, "submit");
        return;
      }
      if (prepared.kind === "save-failed") return;
      const requestBody = {
        storeCode,
        expectedDraftRevision: prepared.revision,
        confirmNoDemand,
        items: prepared.items,
      };
      telemetry.updateRequestBody(requestBody);
      submitContext = createPreorderSubmissionContext(
        draftContextKey,
        activationGuid,
        storeCode,
        detail,
        prepared.items
      );
      if (!isPreorderSubmissionContextCurrent(activeDraftContextKeyRef.current, submitContext)) return;
      telemetry.postStart();
      postStarted = true;
      const submitted = await submitPreorder(
        submitContext.activationGuid,
        requestBody,
        submissionId
      );
      telemetry.postEnd("success");
      postFinished = true;
      const submittedUpdate = applySubmitResultToContext(submitContext, submitted);
      if (!isPreorderSubmissionContextCurrent(activeDraftContextKeyRef.current, submitContext)) {
        // 页面已切换时只允许按 A 的明确 key 更新 A，绝不能借用 B 的 ref 或显示 A 的提示。
        queryClient.setQueryData(submittedUpdate.queryKey, submittedUpdate.detail);
        return;
      }
      submissionConfirmed = true;
      finishConfirmedSubmission(submitContext, submittedUpdate.detail, telemetry);
    } catch (error) {
      if (postStarted && !postFinished) {
        telemetry.postEnd("error");
        postFinished = true;
      }
      const failedSubmitContext = submitContext;
      if (!failedSubmitContext) {
        setSnackbarMessage(getErrorMessage(error, "errors.submitFailed"));
        return;
      }
      if (!isPreorderSubmissionContextCurrent(activeDraftContextKeyRef.current, failedSubmitContext)) return;
      telemetry.detailGet();
      const submitFailure = await reconcilePreorderSubmitFailure(
        () => fetchPreorderActivation(failedSubmitContext.activationGuid, failedSubmitContext.storeCode)
      );
      if (!isPreorderSubmissionContextCurrent(activeDraftContextKeyRef.current, failedSubmitContext)) return;
      if (submitFailure.kind === "submitted") {
        // POST 可能已成功但响应丢失；GET 到已响应状态后必须按提交成功处理。
        submissionConfirmed = true;
        finishConfirmedSubmission(failedSubmitContext, submitFailure.detail, telemetry);
        return;
      }
      if (
        readPreorderErrorCode(error) === "PREORDER_DRAFT_CONFLICT" &&
        submitFailure.kind === "draft-conflict"
      ) {
        // 本次提交到此结束；只协调草稿并推进 revision，绝不在用户未确认时自动重提。
        presentDraftConflict(submitFailure.detail, "submit");
        return;
      }
      setSnackbarMessage(getErrorMessage(error, "errors.submitFailed"));
    } finally {
      if (!submissionConfirmed) autosaveSuspendedRef.current = false;
      if (!submitContext || isPreorderSubmissionContextCurrent(activeDraftContextKeyRef.current, submitContext)) {
        setSubmitPending(false);
      }
    }
  }, [activationGuid, detail, draftContextKey, finishConfirmedSubmission, getErrorMessage, isEditable, presentDraftConflict, queryClient, storeCode, submitPending]);

  const confirmSubmit = useCallback(() => {
    const noDemand = summary.totalQuantity === 0;
    Alert.alert(
      noDemand ? t("submit.noDemandTitle") : t("submit.confirmTitle"),
      noDemand ? t("submit.noDemandMessage") : t("submit.confirmMessage", {
        sku: summary.selectedSkuCount,
        quantity: summary.totalQuantity,
      }),
      [
        { text: t("common:actions.cancel"), style: "cancel" },
        {
          text: noDemand ? t("submit.noDemandAction") : t("common:actions.submit"),
          style: noDemand ? "destructive" : "default",
          onPress: () => void performSubmit(noDemand),
        },
      ]
    );
  }, [performSubmit, summary.selectedSkuCount, summary.totalQuantity, t]);

  const renderItem = useCallback(({ item }: { item: PreorderActivationItem }) => {
    const packCount = packCounts[item.activationItemGuid] ?? 0;
    const orderedQuantity = packCount * item.minimumOrderQuantity;
    return (
      <Card mode="outlined" style={styles.itemCard}>
        <Card.Content style={styles.itemContent}>
          <View style={styles.productRow}>
            {item.productImage ? (
              <Image
                source={{ uri: item.productImage }}
                style={styles.productImage}
                resizeMode="contain"
                accessible={false}
              />
            ) : (
              <View style={styles.imagePlaceholder}>
                <Text variant="labelSmall" style={styles.placeholderText}>{t("detail.noImage")}</Text>
              </View>
            )}
            <View style={styles.productInfo}>
              <Text variant="titleSmall" numberOfLines={2} style={styles.productName}>
                {item.productName || item.productCode}
              </Text>
              <Text variant="bodySmall" style={styles.sku}>{t("detail.sku", { value: item.itemNumber || item.productCode })}</Text>
              <View style={styles.priceRow}>
                <Text variant="labelSmall" style={styles.price}>{t("detail.importPrice", { value: formatMoney(item.importPrice, language) })}</Text>
                <Text variant="labelSmall" style={styles.price}>{t("detail.retailPrice", { value: formatMoney(item.retailPrice, language) })}</Text>
              </View>
              <Text variant="labelMedium" style={styles.moq}>{t("detail.moq", { value: item.minimumOrderQuantity })}</Text>
            </View>
          </View>
          <View style={styles.quantityRow}>
            <IconButton
              icon="minus"
              accessibilityLabel={t("detail.decreasePack", { name: item.productName || item.itemNumber })}
              mode="contained-tonal"
              size={18}
              disabled={!isEditable || submitPending || leaveSavePending || packCount <= 0}
              onPress={() => updatePackCount(item.activationItemGuid, packCount - 1)}
              style={styles.quantityButton}
            />
            <TextInput
              mode="outlined"
              dense
              label={t("detail.packCount")}
              value={String(packCount)}
              keyboardType="number-pad"
              accessibilityLabel={t("detail.packCountFor", { name: item.productName || item.itemNumber })}
              selectTextOnFocus
              disabled={!isEditable || submitPending || leaveSavePending}
              onChangeText={(value) => updatePackCount(item.activationItemGuid, value)}
              style={styles.quantityInput}
            />
            <IconButton
              icon="plus"
              accessibilityLabel={t("detail.increasePack", { name: item.productName || item.itemNumber })}
              mode="contained"
              size={18}
              disabled={!isEditable || submitPending || leaveSavePending}
              onPress={() => updatePackCount(item.activationItemGuid, packCount + 1)}
              style={styles.quantityButton}
            />
            <View style={styles.quantityTotal}>
              <Text variant="labelSmall" style={styles.quantityTotalLabel}>{t("detail.totalQuantity")}</Text>
              <Text variant="titleMedium" style={styles.quantityTotalValue}>{orderedQuantity}</Text>
            </View>
          </View>
        </Card.Content>
      </Card>
    );
  }, [isEditable, language, leaveSavePending, packCounts, submitPending, t, updatePackCount]);

  if (!activationGuid || !storeCode) {
    return (
      <SafeAreaView style={styles.container}>
        <EmptyState
          title={t("detail.missingContextTitle")}
          description={t("detail.missingContextDescription")}
          actionLabel={t("detail.backToList")}
          onAction={() => router.replace("/preorders" as Href)}
        />
      </SafeAreaView>
    );
  }

  if (detailQuery.isError && !detail) {
    return (
      <SafeAreaView style={styles.container}>
        <EmptyState
          title={t("detail.loadFailedTitle")}
          description={t("detail.loadFailedDescription")}
          actionLabel={t("common:actions.retry")}
          onAction={() => void detailQuery.refetch()}
        />
      </SafeAreaView>
    );
  }

  return (
    <SafeAreaView edges={["top", "bottom", "left", "right"]} style={styles.container}>
      <KeyboardAvoidingView
        behavior={Platform.OS === "ios" ? "padding" : undefined}
        style={styles.keyboardAvoiding}
      >
        <View style={styles.header}>
          <IconButton
            icon="arrow-left"
            accessibilityLabel={t("common:actions.back")}
            disabled={submitPending || leaveSavePending}
            onPress={() => router.back()}
            style={styles.headerButton}
          />
          <View style={styles.headerText}>
            <Text variant="titleMedium" numberOfLines={1} style={styles.title}>
              {detail?.templateName || t("detail.title")}
            </Text>
            <Text variant="bodySmall" style={styles.subtitle}>
              {detail
                ? t("detail.period", { period: detail.periodNumber })
                : selectedStore?.storeCode === storeCode
                  ? selectedStore.storeName
                  : storeCode}
            </Text>
          </View>
          <Chip compact icon="storefront-outline">{storeCode}</Chip>
        </View>

        {detail ? (
          <View style={styles.metaBar}>
            <View style={styles.scheduleInfo}>
              <Text variant="bodySmall" style={styles.deadline}>
                {t("detail.deadline", { value: formatDateTime(detail.endAtUtc, language) })}
              </Text>
              {estimatedArrivalDate ? (
                <Text variant="labelSmall" style={styles.estimatedArrival}>
                  {t("detail.estimatedArrival", { value: estimatedArrivalDate })}
                </Text>
              ) : null}
            </View>
            <Text variant="labelSmall" style={saveStatus === "error" ? styles.saveError : styles.saveState}>
              {isSubmitted
                ? t("save.submitted")
                : activationReadOnlyReason
                  ? t("save.readOnly")
                  : saveStatus === "saving"
                    ? t("save.saving")
                    : saveStatus === "saved"
                      ? t("save.saved")
                      : saveStatus === "error"
                        ? t("save.failed")
                        : t("save.auto")}
            </Text>
          </View>
        ) : null}

        {detail && !isSubmitted && activationReadOnlyReason ? (
          <View style={styles.readOnlyBanner}>
            <Text variant="labelMedium" style={styles.readOnlyTitle}>{t("detail.readOnlyTitle")}</Text>
            <Text variant="bodySmall" style={styles.readOnlyDescription}>
              {t(`detail.readOnlyReason.${activationReadOnlyReason}`)}
            </Text>
          </View>
        ) : null}

        {returnedForRevision ? (
          <View style={styles.readOnlyBanner}>
            <Text variant="labelMedium" style={styles.readOnlyTitle}>{t("detail.returnedTitle")}</Text>
            <Text variant="bodySmall" style={styles.readOnlyDescription}>
              {detail.warehouseNotes || t("detail.returnedDescription")}
            </Text>
          </View>
        ) : null}

        <Searchbar
          placeholder={t("detail.searchPlaceholder")}
          value={searchText}
          onChangeText={setSearchText}
          style={styles.searchbar}
        />

        <FlatList
          data={filteredItems}
          keyExtractor={(item) => item.activationItemGuid}
          renderItem={renderItem}
          keyboardShouldPersistTaps="handled"
          contentContainerStyle={styles.listContent}
          ListEmptyComponent={detail && !detail.items.length ? (
            <EmptyState title={t("detail.noItemsTitle")} description={t("detail.noItemsDescription")} />
          ) : null}
        />

        {detail ? (
          <View style={styles.summaryBar}>
            <View style={styles.summaryTextWrap}>
              <Text variant="labelMedium" style={styles.summaryTitle}>
                {t("summary.line", {
                  sku: summary.selectedSkuCount,
                  packs: summary.totalPackCount,
                  quantity: summary.totalQuantity,
                })}
              </Text>
              <Text variant="bodySmall" style={styles.summaryAmount}>
                {t("summary.importAmount", { value: formatMoney(summary.totalImportAmount, language) })}
              </Text>
            </View>
            {saveStatus === "error" && isEditable ? (
              <Button mode="outlined" onPress={() => void persistLatestDraft()} contentStyle={styles.summaryButtonContent}>
                {t("save.retry")}
              </Button>
            ) : null}
            <Button
              mode="contained"
              icon="check"
              disabled={!isEditable || submitPending || leaveSavePending}
              loading={submitPending}
              onPress={confirmSubmit}
              contentStyle={styles.summaryButtonContent}
            >
              {isSubmitted ? t("submit.submitted") : t("submit.action")}
            </Button>
          </View>
        ) : null}
      </KeyboardAvoidingView>
      {detailQuery.isLoading ? <LoadingOverlay /> : null}
      <Snackbar visible={Boolean(snackbarMessage)} onDismiss={() => setSnackbarMessage("")} duration={3500}>
        {snackbarMessage}
      </Snackbar>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: "#F6FAFE" },
  keyboardAvoiding: { flex: 1 },
  header: {
    minHeight: 64,
    flexDirection: "row",
    alignItems: "center",
    gap: 6,
    paddingHorizontal: 8,
    borderBottomWidth: StyleSheet.hairlineWidth,
    borderBottomColor: "#D9E0E8",
    backgroundColor: "#FFFFFF",
  },
  headerButton: { width: 44, height: 44, margin: 0 },
  headerText: { flex: 1 },
  title: { color: "#0F172A", fontWeight: "700" },
  subtitle: { color: "#667085" },
  metaBar: {
    minHeight: 46,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 8,
    paddingHorizontal: 12,
    paddingVertical: 8,
    backgroundColor: "#FFF7E6",
    borderBottomWidth: StyleSheet.hairlineWidth,
    borderBottomColor: "#F5B041",
  },
  scheduleInfo: { flex: 1, gap: 2 },
  deadline: { color: "#7A3E00", fontWeight: "600" },
  estimatedArrival: { color: "#667085", fontWeight: "600" },
  saveState: { color: "#247A3C" },
  saveError: { color: "#C62828" },
  readOnlyBanner: {
    paddingHorizontal: 12,
    paddingVertical: 10,
    gap: 2,
    backgroundColor: "#FFF4E5",
    borderBottomWidth: StyleSheet.hairlineWidth,
    borderBottomColor: "#E9A23B",
  },
  readOnlyTitle: { color: "#7A3E00", fontWeight: "700" },
  readOnlyDescription: { color: "#7A3E00" },
  searchbar: { marginHorizontal: 12, marginTop: 10, backgroundColor: "#FFFFFF" },
  listContent: { flexGrow: 1, padding: 12, paddingBottom: 18, gap: 10 },
  itemCard: { backgroundColor: "#FFFFFF", borderColor: "#D9E0E8" },
  itemContent: { gap: 12 },
  productRow: { flexDirection: "row", gap: 12 },
  productImage: { width: 84, height: 84, borderRadius: 8, backgroundColor: "#F5F7FA" },
  imagePlaceholder: {
    width: 84,
    height: 84,
    alignItems: "center",
    justifyContent: "center",
    borderRadius: 8,
    backgroundColor: "#EEF2F6",
  },
  placeholderText: { color: "#7A8494" },
  productInfo: { flex: 1, gap: 3 },
  productName: { color: "#172033", fontWeight: "700" },
  sku: { color: "#667085" },
  priceRow: { flexDirection: "row", flexWrap: "wrap", gap: 8 },
  price: { color: "#475467" },
  moq: { color: "#8A4B08", fontWeight: "700" },
  quantityRow: { minHeight: 52, flexDirection: "row", alignItems: "center", gap: 6 },
  quantityButton: { width: 44, height: 44, margin: 0 },
  quantityInput: { width: 88, minHeight: 48, backgroundColor: "#FFFFFF", textAlign: "center" },
  quantityTotal: { flex: 1, alignItems: "flex-end" },
  quantityTotalLabel: { color: "#667085" },
  quantityTotalValue: { color: "#0F4C81", fontWeight: "700" },
  summaryBar: {
    minHeight: 74,
    flexDirection: "row",
    alignItems: "center",
    gap: 8,
    paddingHorizontal: 12,
    paddingVertical: 10,
    backgroundColor: "#FFFFFF",
    borderTopWidth: StyleSheet.hairlineWidth,
    borderTopColor: "#C8D1DC",
  },
  summaryTextWrap: { flex: 1, gap: 2 },
  summaryTitle: { color: "#172033", fontWeight: "700" },
  summaryAmount: { color: "#475467" },
  summaryButtonContent: { minHeight: 44 },
});
