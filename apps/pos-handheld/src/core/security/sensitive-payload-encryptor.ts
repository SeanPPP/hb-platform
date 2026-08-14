import { xchacha20poly1305 } from "@noble/ciphers/chacha.js";

import type { SecureStorePort } from "./secure-storage";

const PAYLOAD_KEY = "hbpos.handheld.sensitive-payload-key.v1";
const FORMAT_VERSION = 1;
const KEY_BYTES = 32;
const NONCE_BYTES = 24;
const TAG_BYTES = 16;
const AAD = new TextEncoder().encode("hbpos.handheld.sensitive-payload.v1");
const THIS_DEVICE_ONLY = { requireThisDeviceOnly: true } as const;

export interface RandomBytesPort {
  getRandomBytes(length: number): Promise<Uint8Array>;
}

/**
 * SQLCipher 之外的字段级认证加密。
 *
 * Key 仅保存在本机 Keychain；每条记录使用新的 192-bit nonce，并把格式版本
 * 作为固定 AAD 绑定。解密失败只返回统一错误，不泄露密钥、明文或 tag 细节。
 */
export class SensitivePayloadEncryptor {
  private keyPromise: Promise<Uint8Array> | null = null;

  public constructor(
    private readonly secureStore: SecureStorePort,
    private readonly random: RandomBytesPort,
  ) {}

  public async encrypt(plaintext: string): Promise<Uint8Array> {
    const [key, nonce] = await Promise.all([
      this.getOrCreateKey(),
      this.random.getRandomBytes(NONCE_BYTES),
    ]);
    assertLength(nonce, NONCE_BYTES, "payload nonce");

    const message = new TextEncoder().encode(plaintext);
    const sealed = xchacha20poly1305(key, nonce, AAD).encrypt(message);
    const output = new Uint8Array(1 + NONCE_BYTES + sealed.length);
    output[0] = FORMAT_VERSION;
    output.set(nonce, 1);
    output.set(sealed, 1 + NONCE_BYTES);
    return output;
  }

  public async decrypt(ciphertext: Uint8Array): Promise<string> {
    if (
      !(ciphertext instanceof Uint8Array) ||
      ciphertext.length < 1 + NONCE_BYTES + TAG_BYTES ||
      ciphertext[0] !== FORMAT_VERSION
    ) {
      throw new Error("Sensitive payload ciphertext is invalid.");
    }

    try {
      const key = await this.getOrCreateKey();
      const nonce = ciphertext.slice(1, 1 + NONCE_BYTES);
      const sealed = ciphertext.slice(1 + NONCE_BYTES);
      const plaintext = xchacha20poly1305(key, nonce, AAD).decrypt(sealed);
      return new TextDecoder("utf-8", { fatal: true }).decode(plaintext);
    } catch {
      throw new Error("Sensitive payload ciphertext is invalid.");
    }
  }

  private getOrCreateKey(): Promise<Uint8Array> {
    if (this.keyPromise) {
      return this.keyPromise;
    }

    const loading = this.loadOrCreateKey().catch((error: unknown) => {
      if (this.keyPromise === loading) {
        this.keyPromise = null;
      }
      throw error;
    });
    this.keyPromise = loading;
    return loading;
  }

  private async loadOrCreateKey(): Promise<Uint8Array> {
    const stored = await this.secureStore.get(PAYLOAD_KEY);
    if (stored !== null) {
      return parseKey(stored);
    }

    const key = await this.random.getRandomBytes(KEY_BYTES);
    assertLength(key, KEY_BYTES, "payload key");
    await this.secureStore.set(
      PAYLOAD_KEY,
      encodeHex(key),
      THIS_DEVICE_ONLY,
    );
    return key.slice();
  }
}

function parseKey(value: string): Uint8Array {
  if (!/^[0-9a-f]{64}$/.test(value)) {
    throw new Error("Stored sensitive payload key is invalid.");
  }
  const key = new Uint8Array(KEY_BYTES);
  for (let index = 0; index < KEY_BYTES; index += 1) {
    key[index] = Number.parseInt(value.slice(index * 2, index * 2 + 2), 16);
  }
  return key;
}

function encodeHex(value: Uint8Array): string {
  return Array.from(value, (byte) => byte.toString(16).padStart(2, "0")).join("");
}

function assertLength(
  value: Uint8Array,
  expected: number,
  label: string,
): void {
  if (!(value instanceof Uint8Array) || value.length !== expected) {
    throw new Error(`Secure random ${label} must contain ${expected} bytes.`);
  }
}
