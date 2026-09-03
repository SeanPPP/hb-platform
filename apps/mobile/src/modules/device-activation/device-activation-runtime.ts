import { Platform } from "react-native";
import { getStoredApiHost } from "@/shared/api/config";
import { DeviceStorage } from "@/modules/device/storage";
import {
  commitMobileDeviceActivationApi,
  previewMobileDeviceActivationApi,
} from "./device-activation-api";
import { DeviceAccountStorage } from "./device-account-storage-runtime";
import {
  commitMobileDeviceActivation,
  recoverPendingMobileDeviceActivation,
} from "./device-activation-operation";
import {
  createMobileDeviceCredential,
  createMobileDeviceCredentialVerifier,
} from "./device-credential";
import { resolveActivationHardwareId } from "./device-activation-intent";
import type {
  MobileDeviceActivationMode,
  MobileDeviceSystem,
  PendingMobileDeviceActivation,
} from "./types";

export function getCurrentMobileDeviceSystem(): MobileDeviceSystem {
  return Platform.OS === "ios" ? "iOS" : "Android";
}

export async function previewMobileDeviceActivation(
  activationCode: string,
  mode: MobileDeviceActivationMode,
) {
  const currentBinding =
    mode === "rebind" ? await DeviceAccountStorage.loadBinding() : null;
  return previewMobileDeviceActivationApi({
    activationCode,
    deviceSystem: getCurrentMobileDeviceSystem(),
  }, currentBinding?.apiHost);
}

const operationDependencies = {
  savePending: DeviceAccountStorage.savePending,
  clearPending: DeviceAccountStorage.clearPending,
  saveBinding: DeviceAccountStorage.saveBinding,
  saveLegacyDeviceSession: DeviceStorage.setSession,
  commit: commitMobileDeviceActivationApi,
};

export async function activateMobileDeviceAccount(
  mode: MobileDeviceActivationMode,
  activationCode: string,
) {
  const [installationHardwareId, selectedApiHost, currentBinding, credential] = await Promise.all([
    DeviceStorage.getInstallationId(),
    getStoredApiHost(),
    DeviceAccountStorage.loadBinding(),
    createMobileDeviceCredential(),
  ]);
  const apiHost =
    mode === "rebind" && currentBinding
      ? currentBinding.apiHost
      : selectedApiHost;
  const activationHardwareId = resolveActivationHardwareId(
    mode,
    installationHardwareId,
    currentBinding,
  );
  const pending: PendingMobileDeviceActivation = {
    version: 1,
    mode,
    activationCode,
    apiHost,
    hardwareId: activationHardwareId,
    deviceSystem: getCurrentMobileDeviceSystem(),
    credential,
    credentialVerifier: await createMobileDeviceCredentialVerifier(credential),
    ...(mode === "rebind" && currentBinding
      ? {
          currentHardwareId: currentBinding.hardwareId,
          currentCredential: currentBinding.credential,
        }
      : {}),
  };

  return commitMobileDeviceActivation(pending, operationDependencies);
}

export async function recoverStoredMobileDeviceActivation() {
  const pending = await DeviceAccountStorage.loadPending();
  if (!pending) {
    return null;
  }
  return recoverPendingMobileDeviceActivation(
    pending,
    operationDependencies,
  );
}
