import { type Href, usePathname, useRouter } from "expo-router";
import {
  useCallback,
  useEffect,
  useState,
  useSyncExternalStore,
} from "react";
import { useTranslation } from "react-i18next";
import {
  Pressable,
  StyleSheet,
  Text,
  View,
} from "react-native";

import {
  resolveAppUpdateCopy,
  type AppUpdateCopyKey,
} from "./app-update-copy";
import type {
  AppUpdateOrchestrator,
  AppUpdatePresentation,
} from "./app-update-orchestrator";

import { usePosRuntime } from "@/core/runtime/pos-runtime-context";
import { posColors } from "@/ui/theme";

const HIDDEN_PRESENTATION: AppUpdatePresentation = Object.freeze({
  key: "runtime-unavailable",
  kind: "none",
  requirement: null,
  phase: "hidden",
  blocking: false,
  releaseMessage: null,
  appStoreUrl: null,
});

const RECOVERY_ACCESS_PATHS = new Set([
  "/registration",
  "/settings",
  "/sync-history",
]);

export function AppUpdateGateBridge() {
  const runtime = usePosRuntime();
  const { i18n } = useTranslation();
  const pathname = usePathname();
  const router = useRouter();
  const updates = runtime.services?.appUpdates ?? null;
  const presentation = useUpdatePresentation(updates);
  const copy = resolveAppUpdateCopy(
    i18n.resolvedLanguage ?? i18n.language,
  );
  const [dismissedKey, setDismissedKey] = useState<string | null>(null);
  const [working, setWorking] = useState(false);
  const [errorKey, setErrorKey] =
    useState<AppUpdateCopyKey | null>(null);

  useEffect(() => {
    if (!updates || presentation.phase !== "waiting-for-safe") {
      return undefined;
    }
    const check = () => {
      void updates.refreshSafety().catch(() => {
        // 未能证明交易安全时继续保留恢复页面，下一轮再核验。
      });
    };
    const timer = setInterval(check, 1_000);
    return () => clearInterval(timer);
  }, [presentation.key, presentation.phase, updates]);

  useEffect(() => {
    setWorking(false);
    setErrorKey(null);
  }, [presentation.key]);

  const performUpdate = useCallback(async () => {
    if (!updates || working) return;
    setWorking(true);
    setErrorKey(null);
    try {
      const result = await updates.performSelectedUpdate();
      if (result.action === "open-app-store") {
        return;
      }
      if (result.action === "blocked") {
        setErrorKey("error.notSafe");
        return;
      }
      if (
        result.action !== "ota" ||
        result.result.state !== "reloaded"
      ) {
        setErrorKey("error.unavailable");
      }
    } catch {
      setErrorKey("error.unavailable");
    } finally {
      setWorking(false);
    }
  }, [updates, working]);

  if (
    presentation.phase === "hidden" ||
    presentation.phase === "unchecked" ||
    presentation.phase === "waiting-for-safe"
  ) {
    return null;
  }
  if (
    presentation.requirement === "optional" &&
    dismissedKey === presentation.key
  ) {
    return null;
  }

  const required = presentation.requirement === "required";
  const preserveRecoveryAccess =
    required && RECOVERY_ACCESS_PATHS.has(normalizePathname(pathname));
  const titleKey: AppUpdateCopyKey =
    presentation.kind === "native"
      ? required
        ? "required.nativeTitle"
        : "optional.nativeTitle"
      : required
        ? "required.otaTitle"
        : "optional.otaTitle";
  const actionKey: AppUpdateCopyKey =
    presentation.kind === "native"
      ? "action.openStore"
      : "action.installOta";

  return (
    <View
      accessibilityViewIsModal={required && !preserveRecoveryAccess}
      pointerEvents={
        preserveRecoveryAccess
          ? "box-none"
          : required
            ? "auto"
            : "box-none"
      }
      style={
        preserveRecoveryAccess
          ? styles.recoveryAccessLayer
          : required
          ? styles.blockingLayer
          : styles.optionalLayer
      }
      testID={
        preserveRecoveryAccess
          ? "app-update-recovery-access"
          : required
          ? "app-update-blocking-gate"
          : "app-update-optional-prompt"
      }
    >
      <View
        accessibilityRole="alert"
        style={[
          styles.card,
          preserveRecoveryAccess
            ? styles.recoveryAccessCard
            : required
              ? styles.requiredCard
              : styles.optionalCard,
        ]}
      >
        <View style={styles.accent} />
        <Text style={styles.eyebrow}>
          {copy[required ? "eyebrow.required" : "eyebrow.optional"]}
        </Text>
        <Text style={styles.title}>{copy[titleKey]}</Text>
        <Text style={styles.body}>
          {presentation.releaseMessage ??
            copy[required ? "required.body" : "optional.body"]}
        </Text>
        {errorKey ? (
          <Text accessibilityRole="alert" style={styles.error}>
            {copy[errorKey]}
          </Text>
        ) : null}
        <View style={styles.actions}>
          {required && !preserveRecoveryAccess ? (
            <>
              <Pressable
                accessibilityRole="button"
                onPress={() => router.push("/settings" as Href)}
                style={styles.secondaryButton}
                testID="app-update-settings-entry"
              >
                <Text style={styles.secondaryButtonText}>
                  {copy["action.settings"]}
                </Text>
              </Pressable>
              <Pressable
                accessibilityRole="button"
                onPress={() => router.push("/sync-history" as Href)}
                style={styles.secondaryButton}
                testID="app-update-support-entry"
              >
                <Text style={styles.secondaryButtonText}>
                  {copy["action.support"]}
                </Text>
              </Pressable>
            </>
          ) : null}
          {!required ? (
            <Pressable
              accessibilityRole="button"
              onPress={() => setDismissedKey(presentation.key)}
              style={styles.secondaryButton}
              testID="app-update-dismiss"
            >
              <Text style={styles.secondaryButtonText}>
                {copy["action.later"]}
              </Text>
            </Pressable>
          ) : null}
          <Pressable
            accessibilityRole="button"
            disabled={working}
            onPress={performUpdate}
            style={[
              styles.primaryButton,
              working ? styles.disabledButton : null,
            ]}
            testID="app-update-action"
          >
            <Text style={styles.primaryButtonText}>
              {copy[working ? "action.working" : actionKey]}
            </Text>
          </Pressable>
        </View>
      </View>
    </View>
  );
}

function normalizePathname(pathname: string): string {
  const normalized = pathname.replace(/\/+$/u, "");
  return normalized || "/";
}

function useUpdatePresentation(
  updates: AppUpdateOrchestrator | null,
): AppUpdatePresentation {
  const subscribe = useCallback(
    (listener: () => void) =>
      updates?.subscribePresentation(listener) ?? (() => undefined),
    [updates],
  );
  const getSnapshot = useCallback(
    () => updates?.getPresentation() ?? HIDDEN_PRESENTATION,
    [updates],
  );
  return useSyncExternalStore(subscribe, getSnapshot, getSnapshot);
}

const styles = StyleSheet.create({
  blockingLayer: {
    ...StyleSheet.absoluteFillObject,
    alignItems: "center",
    backgroundColor: posColors.canvas,
    elevation: 50,
    justifyContent: "center",
    paddingHorizontal: 48,
    zIndex: 1_000,
  },
  optionalLayer: {
    ...StyleSheet.absoluteFillObject,
    alignItems: "flex-end",
    justifyContent: "flex-start",
    paddingHorizontal: 28,
    paddingTop: 24,
    zIndex: 900,
  },
  recoveryAccessLayer: {
    ...StyleSheet.absoluteFillObject,
    alignItems: "flex-end",
    justifyContent: "flex-start",
    paddingHorizontal: 28,
    paddingTop: 24,
    zIndex: 900,
  },
  card: {
    backgroundColor: posColors.surface,
    borderColor: posColors.border,
    borderWidth: 1,
    elevation: 12,
    maxWidth: 560,
    overflow: "hidden",
    paddingBottom: 24,
    paddingHorizontal: 28,
    paddingTop: 26,
    shadowColor: posColors.ink,
    shadowOffset: { width: 0, height: 8 },
    shadowOpacity: 0.16,
    shadowRadius: 20,
    width: "100%",
  },
  requiredCard: {
    borderColor: posColors.orange,
    minHeight: 320,
    justifyContent: "center",
  },
  optionalCard: {
    maxWidth: 480,
  },
  recoveryAccessCard: {
    maxWidth: 420,
    minHeight: 0,
  },
  accent: {
    backgroundColor: posColors.orange,
    height: 5,
    left: 0,
    position: "absolute",
    right: 0,
    top: 0,
  },
  eyebrow: {
    color: posColors.orange,
    fontSize: 13,
    fontWeight: "800",
    letterSpacing: 1.3,
    marginBottom: 10,
  },
  title: {
    color: posColors.ink,
    fontSize: 28,
    fontWeight: "800",
    lineHeight: 34,
  },
  body: {
    color: posColors.mutedInk,
    fontSize: 17,
    lineHeight: 25,
    marginTop: 14,
  },
  error: {
    backgroundColor: posColors.redSoft,
    color: posColors.red,
    fontSize: 15,
    lineHeight: 21,
    marginTop: 16,
    paddingHorizontal: 14,
    paddingVertical: 11,
  },
  actions: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 12,
    justifyContent: "flex-end",
    marginTop: 24,
  },
  primaryButton: {
    alignItems: "center",
    backgroundColor: posColors.orange,
    justifyContent: "center",
    minHeight: 48,
    minWidth: 150,
    paddingHorizontal: 22,
  },
  secondaryButton: {
    alignItems: "center",
    borderColor: posColors.border,
    borderWidth: 1,
    justifyContent: "center",
    minHeight: 48,
    minWidth: 96,
    paddingHorizontal: 20,
  },
  disabledButton: {
    opacity: 0.58,
  },
  primaryButtonText: {
    color: "#FFFFFF",
    fontSize: 16,
    fontWeight: "800",
  },
  secondaryButtonText: {
    color: posColors.ink,
    fontSize: 16,
    fontWeight: "700",
  },
});
