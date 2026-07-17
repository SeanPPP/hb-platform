import { useCallback, useState } from "react";
import { useFocusEffect } from "@react-navigation/native";
import {
  buildApiBaseUrl,
  getStoredApiHost,
} from "@/shared/api/config";
import type {
  AttendancePunchVerificationState,
} from "@/modules/attendance/types";
import {
  collectRequiredLocation,
  isRequiredLocationError,
} from "@/modules/attendance/required-location";
import { isIosReviewSessionActive } from "@/modules/ios-review/session";
import { reviewAwareFetch } from "@/modules/ios-review/network";

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

export async function verifyAttendanceNetworkReachability() {
  if (isIosReviewSessionActive()) {
    // 离线 Demo 的业务请求由本地 adapter 处理，无需探测生产 health endpoint。
    return {
      status: "available" as const,
      reason: "captured" as const,
      verificationStatus: "online" as const,
    };
  }
  const host = await getStoredApiHost();
  const apiBaseUrl = buildApiBaseUrl(host);
  const healthUrl = `${apiBaseUrl.replace(/\/api$/, "")}/health`;
  const controller = new AbortController();
  const timeoutId = setTimeout(
    () => controller.abort(),
    NETWORK_CHECK_TIMEOUT_MS,
  );

  try {
    await reviewAwareFetch(healthUrl, {
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
  const [networkResult, locationResult] = await Promise.allSettled([
    verifyAttendanceNetworkReachability(),
    collectRequiredLocation(),
  ]);
  const checkedAt = new Date().toISOString();
  const network =
    networkResult.status === "fulfilled"
      ? networkResult.value
      : {
          status: "unavailable" as const,
          reason: "networkUnreachable" as const,
          verificationStatus: "offline" as const,
        };

  if (locationResult.status === "fulfilled") {
    const location = locationResult.value;
    return {
      checkedAt,
      location: {
        status: "available",
        reason: "captured",
        permissionStatus: location.locationPermissionStatus,
        latitude: location.locationLatitude,
        longitude: location.locationLongitude,
        accuracy: location.locationAccuracy,
      },
      network,
      payload: {
        ...location,
        networkVerificationStatus: network.verificationStatus,
      },
    };
  }

  const denied = isRequiredLocationError(locationResult.reason);

  return {
    checkedAt,
    location: {
      status: denied ? "permissionDenied" : "unavailable",
      reason: denied ? "permissionDenied" : "unknown",
      permissionStatus: denied ? "denied" : "unavailable",
    },
    network,
    payload: {
      locationPermissionStatus: denied ? "denied" : "unavailable",
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
