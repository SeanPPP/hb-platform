import { useCallback, useState } from "react";
import { useFocusEffect } from "@react-navigation/native";
import {
  buildApiBaseUrl,
  getStoredApiHost,
} from "@/shared/api/config";
import type {
  AttendancePunchVerificationState,
} from "@/modules/attendance/types";

const NETWORK_CHECK_TIMEOUT_MS = 5000;

const DEFAULT_VERIFICATION_STATE: AttendancePunchVerificationState = {
  checkedAt: undefined,
  location: {
    status: "unknown",
    reason: "dependencyMissing",
    permissionStatus: "unavailable",
  },
  network: {
    status: "unknown",
    reason: "unknown",
    verificationStatus: "unknown",
  },
  payload: {
    locationPermissionStatus: "unavailable",
    networkVerificationStatus: "unknown",
  },
};

async function verifyNetworkReachability() {
  const host = await getStoredApiHost();
  const apiBaseUrl = buildApiBaseUrl(host);
  const healthUrl = `${apiBaseUrl.replace(/\/api$/, "")}/health`;
  const controller = new AbortController();
  const timeoutId = setTimeout(
    () => controller.abort(),
    NETWORK_CHECK_TIMEOUT_MS,
  );

  try {
    await fetch(healthUrl, {
      method: "GET",
      headers: { Accept: "application/json" },
      signal: controller.signal,
    });
    return {
      status: "available" as const,
      reason: "captured" as const,
      verificationStatus: "online" as const,
    };
  } catch {
    return {
      status: "unavailable" as const,
      reason: "networkUnreachable" as const,
      verificationStatus: "offline" as const,
    };
  } finally {
    clearTimeout(timeoutId);
  }
}

async function collectVerificationState(): Promise<AttendancePunchVerificationState> {
  const network = await verifyNetworkReachability();
  const checkedAt = new Date().toISOString();

  return {
    checkedAt,
    location: {
      status: "unknown",
      reason: "dependencyMissing",
      permissionStatus: "unavailable",
    },
    network,
    payload: {
      locationPermissionStatus: "unavailable",
      networkVerificationStatus: network.verificationStatus,
    },
  };
}

export function usePunchVerification() {
  const [verification, setVerification] = useState<AttendancePunchVerificationState>(
    DEFAULT_VERIFICATION_STATE,
  );
  const [isRefreshing, setIsRefreshing] = useState(false);

  const refreshVerification = useCallback(async () => {
    setIsRefreshing(true);
    try {
      const nextState = await collectVerificationState();
      setVerification(nextState);
      return nextState;
    } catch {
      const fallbackState: AttendancePunchVerificationState = {
        ...DEFAULT_VERIFICATION_STATE,
        checkedAt: new Date().toISOString(),
      };
      setVerification(fallbackState);
      return fallbackState;
    } finally {
      setIsRefreshing(false);
    }
  }, []);

  useFocusEffect(
    useCallback(() => {
      void refreshVerification();
    }, [refreshVerification]),
  );

  return {
    verification,
    isRefreshing,
    refreshVerification,
  };
}
