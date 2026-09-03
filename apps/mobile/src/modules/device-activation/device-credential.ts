import * as Crypto from "expo-crypto";

function bytesToHex(bytes: Uint8Array) {
  return Array.from(bytes, (value) => value.toString(16).padStart(2, "0")).join("");
}

export async function createMobileDeviceCredential() {
  const bytes = await Crypto.getRandomBytesAsync(32);
  return bytesToHex(bytes);
}

export async function createMobileDeviceCredentialVerifier(credential: string) {
  return (
    await Crypto.digestStringAsync(
      Crypto.CryptoDigestAlgorithm.SHA256,
      credential,
      { encoding: Crypto.CryptoEncoding.HEX },
    )
  ).toLowerCase();
}
