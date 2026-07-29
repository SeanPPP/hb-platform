import assert from "node:assert/strict";
import test from "node:test";

import type { CardSyncEvidenceV1 } from "../contracts/payment";

import { ProtectedMaterialIntegrityError } from "./protected-material-integrity-error";
import {
  SqlitePaymentProtectedMaterialReader,
  type PaymentProtectedMaterialBinding,
} from "./sqlite-payment-protected-material";
import type {
  SqliteConnectionPort,
  SqlRunResult,
  SqlValue,
} from "./types";

const binding: PaymentProtectedMaterialBinding = {
  attemptId: "attempt-1",
  orderGuid: "order-1",
  provider: "square",
  operation: "refund",
  amountCents: -500,
};

const evidence: CardSyncEvidenceV1 = {
  version: 1,
  provider: "square",
  operation: "refund",
  processor: "Square",
  txnRef: "txn-1",
  authCode: "AUTH-1",
  cardType: "VISA",
  cardBin: 411111,
  maskedCardNumber: "411111******1111",
  merchantId: "merchant-1",
  responseCode: "00",
  responseText: "APPROVED",
  stan: "123456",
  bankDateTimeIso: "2026-07-28T00:00:00.000Z",
  amountCents: 500,
  refundReference: "refund-1",
};

const identityEncryptor = {
  async encrypt(plaintext: string) {
    return new TextEncoder().encode(plaintext);
  },
  async decrypt(ciphertext: Uint8Array) {
    return new TextDecoder().decode(ciphertext);
  },
};

test("支付受保护 reader：明文 JSON、版本、shape 与双层 binding 损坏均分类为 typed integrity", async () => {
  const cases = [
    {
      expectedCode: "PROTECTED_MATERIAL_JSON_INVALID",
      row: paymentRow(new TextEncoder().encode("{")),
    },
    {
      expectedCode: "PROTECTED_MATERIAL_VERSION_INVALID",
      row: paymentRow(new TextEncoder().encode(JSON.stringify({
        version: 2,
        voucherReservationToken: null,
        cardSyncEvidence: evidence,
      }))),
    },
    {
      expectedCode: "PROTECTED_MATERIAL_SHAPE_INVALID",
      row: paymentRow(new TextEncoder().encode(JSON.stringify({
        version: 1,
        voucherReservationToken: null,
        cardSyncEvidence: {
          ...evidence,
          pan: "4111111111111111",
        },
      }))),
    },
    {
      expectedCode: "PROTECTED_MATERIAL_SHAPE_INVALID",
      row: paymentRow(envelope({
        ...evidence,
        responseText: "{\"pan\":\"4111111111111111\"}",
      })),
    },
    {
      expectedCode: "PROTECTED_MATERIAL_SHAPE_INVALID",
      row: {
        ...paymentRow(null),
        provider_payload_ciphertext: undefined,
      },
    },
    {
      expectedCode: "PROTECTED_MATERIAL_BINDING_MISMATCH",
      row: {
        ...paymentRow(envelope(evidence)),
        order_guid: "another-order",
      },
    },
    {
      expectedCode: "PROTECTED_MATERIAL_BINDING_MISMATCH",
      row: paymentRow(envelope({
        ...evidence,
        amountCents: 499,
      })),
    },
  ] as const;

  for (const item of cases) {
    const reader = new SqlitePaymentProtectedMaterialReader(
      connectionReturning(item.row),
      identityEncryptor,
    );
    await assert.rejects(
      () => reader.read(binding),
      (error: unknown) =>
        error instanceof ProtectedMaterialIntegrityError &&
        error.code === item.expectedCode,
      item.expectedCode,
    );
  }
});

test("支付受保护 reader：无 attempt 或无 evidence 返回 null，DB/decrypt 错误原样透传", async () => {
  assert.equal(
    await new SqlitePaymentProtectedMaterialReader(
      connectionReturning(null),
      identityEncryptor,
    ).read(binding),
    null,
  );
  assert.equal(
    await new SqlitePaymentProtectedMaterialReader(
      connectionReturning(paymentRow(null)),
      identityEncryptor,
    ).read(binding),
    null,
  );
  assert.equal(
    await new SqlitePaymentProtectedMaterialReader(
      connectionReturning(envelopeRow(null)),
      identityEncryptor,
    ).read(binding),
    null,
  );

  const databaseError = new Error("database unavailable");
  const databaseReader = new SqlitePaymentProtectedMaterialReader(
    connectionThrowing(databaseError),
    identityEncryptor,
  );
  await assert.rejects(
    () => databaseReader.read(binding),
    (error: unknown) => error === databaseError,
  );

  const decryptError = new Error("keychain unavailable");
  const decryptReader = new SqlitePaymentProtectedMaterialReader(
    connectionReturning(envelopeRow(evidence)),
    {
      ...identityEncryptor,
      async decrypt() {
        throw decryptError;
      },
    },
  );
  await assert.rejects(
    () => decryptReader.read(binding),
    (error: unknown) => error === decryptError,
  );
});

function paymentRow(ciphertext: Uint8Array | null): Record<string, unknown> {
  return {
    attempt_id: binding.attemptId,
    order_guid: binding.orderGuid,
    provider: binding.provider,
    operation: binding.operation,
    amount_cents: binding.amountCents,
    provider_payload_ciphertext: ciphertext,
  };
}

function envelopeRow(
  cardSyncEvidence: CardSyncEvidenceV1 | null,
): Record<string, unknown> {
  return paymentRow(envelope(cardSyncEvidence));
}

function envelope(
  cardSyncEvidence: CardSyncEvidenceV1 | null,
): Uint8Array {
  return new TextEncoder().encode(JSON.stringify({
    version: 1,
    voucherReservationToken: null,
    cardSyncEvidence,
  }));
}

function connectionReturning(
  row: Record<string, unknown> | null,
): SqliteConnectionPort {
  return connectionWithGetFirst(async () => row);
}

function connectionThrowing(error: Error): SqliteConnectionPort {
  return connectionWithGetFirst(async () => {
    throw error;
  });
}

function connectionWithGetFirst(
  getFirst: () => Promise<Record<string, unknown> | null>,
): SqliteConnectionPort {
  const unsupported = (): Promise<never> =>
    Promise.reject(new Error("unsupported test connection operation"));
  return {
    exec: unsupported,
    run: unsupported as (
      sql: string,
      parameters?: readonly SqlValue[],
    ) => Promise<SqlRunResult>,
    async getFirst<T extends object>() {
      return await getFirst() as T | null;
    },
    getAll: unsupported,
    withExclusiveTransaction: unsupported,
    close: async () => undefined,
  };
}
