import assert from "node:assert/strict";
import test from "node:test";

import { InMemorySecureStore } from "./secure-storage";
import {
  SensitivePayloadEncryptor,
  type RandomBytesPort,
} from "./sensitive-payload-encryptor";

test("字段级加密可跨实例恢复，Key 只写入本机 Keychain", async () => {
  const secureStore = new InMemorySecureStore();
  const random = new DeterministicRandom();
  const first = new SensitivePayloadEncryptor(secureStore, random);
  const ciphertext = await first.encrypt("voucher-secret");

  assert.equal(new TextDecoder().decode(ciphertext).includes("voucher-secret"), false);
  assert.equal(secureStore.lastWriteOptions?.requireThisDeviceOnly, true);

  const reopened = new SensitivePayloadEncryptor(
    secureStore,
    new DeterministicRandom(90),
  );
  assert.equal(await reopened.decrypt(ciphertext), "voucher-secret");
});

test("每条记录使用不同 nonce，相同明文不会生成相同密文", async () => {
  const encryptor = new SensitivePayloadEncryptor(
    new InMemorySecureStore(),
    new DeterministicRandom(),
  );

  const first = await encryptor.encrypt("receipt");
  const second = await encryptor.encrypt("receipt");

  assert.notDeepEqual(first, second);
  assert.equal(await encryptor.decrypt(first), "receipt");
  assert.equal(await encryptor.decrypt(second), "receipt");
});

test("密文被篡改、格式未知或 Key 非法时统一拒绝", async () => {
  const secureStore = new InMemorySecureStore();
  const encryptor = new SensitivePayloadEncryptor(
    secureStore,
    new DeterministicRandom(),
  );
  const ciphertext = await encryptor.encrypt("receipt");
  const tampered = ciphertext.slice();
  tampered[tampered.length - 1] =
    (tampered[tampered.length - 1] ?? 0) ^ 1;

  await assert.rejects(
    () => encryptor.decrypt(tampered),
    /Sensitive payload ciphertext is invalid/,
  );
  await assert.rejects(
    () => encryptor.decrypt(Uint8Array.from([2, ...ciphertext.slice(1)])),
    /Sensitive payload ciphertext is invalid/,
  );

  const invalidStore = new InMemorySecureStore();
  await invalidStore.set(
    "hbpos.handheld.sensitive-payload-key.v1",
    "not-a-key",
    { requireThisDeviceOnly: true },
  );
  await assert.rejects(
    () =>
      new SensitivePayloadEncryptor(
        invalidStore,
        new DeterministicRandom(),
      ).encrypt("receipt"),
    /Stored sensitive payload key is invalid/,
  );
});

test("并发首用共享一次 Key 创建，不产生不可恢复的竞态", async () => {
  const secureStore = new InMemorySecureStore();
  const random = new DeterministicRandom();
  const encryptor = new SensitivePayloadEncryptor(secureStore, random);

  const [first, second] = await Promise.all([
    encryptor.encrypt("first"),
    encryptor.encrypt("second"),
  ]);

  assert.equal(random.requested.filter((length) => length === 32).length, 1);
  assert.equal(await encryptor.decrypt(first), "first");
  assert.equal(await encryptor.decrypt(second), "second");
});

class DeterministicRandom implements RandomBytesPort {
  public readonly requested: number[] = [];
  private next: number;

  public constructor(seed = 1) {
    this.next = seed;
  }

  public async getRandomBytes(length: number): Promise<Uint8Array> {
    this.requested.push(length);
    const result = new Uint8Array(length);
    for (let index = 0; index < length; index += 1) {
      result[index] = this.next % 256;
      this.next += 1;
    }
    return result;
  }
}
