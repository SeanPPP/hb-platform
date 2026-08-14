import * as Crypto from "expo-crypto";

export function createExpoInstallationId(): string {
  return Crypto.randomUUID();
}
