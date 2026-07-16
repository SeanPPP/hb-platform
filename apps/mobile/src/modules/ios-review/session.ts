import { iosReviewDataStore, resetReviewData } from "./data-store";
import { resetIosReviewAppRouteState } from "./app-routes";

export type AuthSessionKind = "account" | "device" | "iosReview";

export interface IosReviewMarkerStorage {
  getItemAsync(key: string): Promise<string | null>;
  setItemAsync(key: string, value: string): Promise<void>;
  deleteItemAsync(key: string): Promise<void>;
}

export interface IosReviewSession {
  kind: "iosReview";
  active: true;
}

export const IOS_REVIEW_SESSION_MARKER_KEY = "ios-review-session-active";
const IOS_REVIEW_SESSION_MARKER_VALUE = "1";

let authenticatedSessionActive = false;
let reviewBuildGateEnabled = false;
let preAuthBlocked = false;
let lastNotifiedGuardState = false;
const listeners = new Set<(active: boolean) => void>();

async function getDefaultStorage(): Promise<IosReviewMarkerStorage> {
  // 动态加载原生桥接，确保 Node 纯逻辑测试不会初始化 React Native 运行时。
  const secureStore = await import("expo-secure-store");
  return secureStore;
}

function notifyGuardState() {
  const nextActive = isIosReviewSessionActive();
  if (lastNotifiedGuardState === nextActive) return;
  lastNotifiedGuardState = nextActive;
  listeners.forEach((listener) => listener(nextActive));
}

export function isIosReviewSessionActive() {
  // 审核 production 构建在用户明确选择普通认证前也必须 fail-closed。
  return authenticatedSessionActive || preAuthBlocked;
}

export function isIosReviewAuthenticatedSessionActive() {
  return authenticatedSessionActive;
}

export function configureIosReviewBuildGate(enabled: boolean) {
  reviewBuildGateEnabled = enabled;
  preAuthBlocked = enabled && !authenticatedSessionActive;
  notifyGuardState();
}

export function beginStandardAuthentication() {
  if (authenticatedSessionActive) {
    return false;
  }
  preAuthBlocked = false;
  notifyGuardState();
  return true;
}

export function subscribeIosReviewSession(listener: (active: boolean) => void) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export async function setIosReviewSessionActive(
  storage?: IosReviewMarkerStorage
) {
  const markerStorage = storage ?? (await getDefaultStorage());
  await markerStorage.setItemAsync(
    IOS_REVIEW_SESSION_MARKER_KEY,
    IOS_REVIEW_SESSION_MARKER_VALUE
  );
  authenticatedSessionActive = true;
  preAuthBlocked = false;
  notifyGuardState();
}

export async function restoreIosReviewSession(
  storage?: IosReviewMarkerStorage
) {
  const markerStorage = storage ?? (await getDefaultStorage());
  const marker = await markerStorage.getItemAsync(IOS_REVIEW_SESSION_MARKER_KEY);
  resetReviewData();
  resetIosReviewAppRouteState(iosReviewDataStore);
  authenticatedSessionActive = marker === IOS_REVIEW_SESSION_MARKER_VALUE;
  preAuthBlocked = reviewBuildGateEnabled && !authenticatedSessionActive;
  notifyGuardState();
  return authenticatedSessionActive;
}

export async function clearIosReviewSession(storage?: IosReviewMarkerStorage) {
  const markerStorage = storage ?? (await getDefaultStorage());
  try {
    await markerStorage.deleteItemAsync(IOS_REVIEW_SESSION_MARKER_KEY);
  } finally {
    resetReviewData();
    resetIosReviewAppRouteState(iosReviewDataStore);
    authenticatedSessionActive = false;
    preAuthBlocked = reviewBuildGateEnabled;
    notifyGuardState();
  }
}
