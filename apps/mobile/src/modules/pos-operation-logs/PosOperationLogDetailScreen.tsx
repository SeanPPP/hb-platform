import { useCallback, useEffect, useRef, useState } from "react";
import { StyleSheet } from "react-native";
import * as Clipboard from "expo-clipboard";
import { useLocalSearchParams, useRouter } from "expo-router";
import { Button, Text } from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import { PosOperationLogDetailView } from "@/components/pos-operation-logs/PosOperationLogDetailView";
import { createProductInsightRequestGate } from "@/modules/product-insights/request-gate";
import { useStores } from "@/modules/shop/use-stores";
import { useAuthStore } from "@/store/auth-store";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { resolveLocalizedErrorMessage } from "@/shared/i18n/error-message";
import { HB_COLORS, HB_SPACING } from "@/shared/theme/tokens";
import { fetchPosOperationLogDetail } from "./api";
import { canViewPosOperationLogs } from "./logic";
import type { PosOperationLogDetail } from "./types";

const first = (value: string | string[] | undefined) =>
  (Array.isArray(value) ? value[0] : value)?.trim() || "";

export function PosOperationLogDetailScreen() {
  const { t } = useAppTranslation("posOperationLogs");
  const router = useRouter();
  const isAuthenticated = useAuthStore((state) => state.isAuthenticated);
  const hasPermission = useAuthStore((state) => state.access.hasPermission);
  const sessionKind = useAuthStore((state) => state.sessionKind);
  const review = useAuthStore((state) => state.iosReviewOfflineGuardActive);
  const params = useLocalSearchParams<{ eventId?: string | string[] }>();
  const eventId = first(params.eventId);
  const goBack = () =>
    router.canGoBack() ? router.back() : router.replace("/(shell)/pos-operation-logs");

  if (review || sessionKind === "iosReview") {
    return <ScreenMessage message={t("messages.reviewUnavailable")} onBack={goBack} />;
  }
  if (!canViewPosOperationLogs(isAuthenticated, hasPermission)) {
    return <ScreenMessage message={t("messages.notAllowed")} onBack={goBack} />;
  }
  if (!eventId) {
    return <ScreenMessage message={t("states.detailNotFound")} onBack={goBack} />;
  }
  return <PosOperationLogDetailContent key={eventId} eventId={eventId} onBack={goBack} />;
}

function ScreenMessage({ message, onBack }: { message: string; onBack: () => void }) {
  const { t } = useAppTranslation("posOperationLogs");
  return (
    <SafeAreaView style={styles.message}>
      <Text accessibilityLiveRegion="polite">{message}</Text>
      <Button onPress={onBack}>{t("actions.back")}</Button>
    </SafeAreaView>
  );
}

function PosOperationLogDetailContent({ eventId, onBack }: { eventId: string; onBack: () => void }) {
  const { t, language } = useAppTranslation("posOperationLogs");
  const router = useRouter();
  const { stores } = useStores();
  const [detail, setDetail] = useState<PosOperationLogDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [copiedKey, setCopiedKey] = useState<string | null>(null);
  const gate = useRef(createProductInsightRequestGate()).current;
  const copiedTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  const load = useCallback(async () => {
    const lease = gate.begin();
    setLoading(true);
    setError(null);
    try {
      const result = await fetchPosOperationLogDetail(eventId, lease.signal);
      if (lease.isCurrent()) setDetail(result);
    } catch (cause) {
      if (!lease.isCurrent()) return;
      const status = (cause as { response?: { status?: number } })?.response?.status;
      // 404 与 403 是明确的业务结论，给出针对性文案而不是泛化的加载失败。
      setError(
        status === 404
          ? t("states.detailNotFound")
          : status === 403
            ? t("states.detailForbidden")
            : resolveLocalizedErrorMessage(cause, {
                t,
                language,
                fallbackKey: "messages.detailFailed",
                allowRawMessageInChinese: false,
              }),
      );
    } finally {
      if (lease.isCurrent()) setLoading(false);
    }
  }, [eventId, gate, language, t]);

  useEffect(() => {
    void load();
    return () => {
      gate.cancel();
      if (copiedTimer.current) clearTimeout(copiedTimer.current);
    };
  }, [gate, load]);

  const storeName = detail
    ? stores.find((store) => store.storeCode === detail.storeCode)?.storeName ?? null
    : null;

  return (
    <PosOperationLogDetailView
      detail={detail}
      loading={loading}
      error={error}
      storeName={storeName}
      copiedKey={copiedKey}
      onCopy={(key, value) => {
        void Clipboard.setStringAsync(value);
        setCopiedKey(key);
        if (copiedTimer.current) clearTimeout(copiedTimer.current);
        copiedTimer.current = setTimeout(() => setCopiedKey(null), 1500);
      }}
      onOpenOrderTimeline={(orderGuid) =>
        router.push({
          pathname: "/(shell)/pos-operation-logs",
          params: { orderGuid },
        })
      }
      onOpenCashierToday={(current) =>
        router.push({
          pathname: "/(shell)/pos-operation-logs",
          params: {
            // 员工编号比姓名更精确，后端 cashierKeyword 对两者都做包含匹配。
            cashier: current.cashierId ?? current.cashierName ?? "",
            storeCode: current.storeCode,
            preset: "today",
          },
        })
      }
      onRetry={() => void load()}
      onBack={onBack}
    />
  );
}

const styles = StyleSheet.create({
  message: {
    flex: 1,
    gap: HB_SPACING.md,
    padding: HB_SPACING.lg,
    justifyContent: "center",
    backgroundColor: HB_COLORS.background,
  },
});
