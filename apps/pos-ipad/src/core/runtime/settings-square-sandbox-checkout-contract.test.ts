import assert from "node:assert/strict";
import test from "node:test";

import { SquarePaymentAdapter } from "../../features/payments/square/square-payment-adapter";
import { SETTINGS_SQUARE_SANDBOX_CHECKOUT_DEVICES } from "@hb/pos-domain/features/settings/settings-square-setup";
import type {
  HbposTransport,
  HbposTransportRequest,
  HbposTransportResponse,
} from "../api/hbpos-api";
import type { PaymentAttempt } from "../contracts";
import { PosPublicRuntimeConfigurationStore } from "../security/pos-public-runtime-configuration";
import { InMemorySecureStore } from "../security/secure-storage";

import { createPaymentConfigurationSources } from "./payment-runtime-config";

test("Sandbox 官方终端保存并重载后原样进入真实 checkout 请求", async () => {
  const secureStore = new InMemorySecureStore();
  const currentRuntime = new PosPublicRuntimeConfigurationStore(
    secureStore,
    ["https://pos.example.test"],
  );
  const officialDeviceId = SETTINGS_SQUARE_SANDBOX_CHECKOUT_DEVICES[0].id;
  await currentRuntime.savePayments({
    provider: "square",
    square: {
      environment: "Sandbox",
      deviceId: officialDeviceId,
      locationId: "LOC-1",
    },
    linkly: null,
  });

  const nextRuntime = new PosPublicRuntimeConfigurationStore(
    secureStore,
    ["https://pos.example.test"],
  );
  const reloaded = await nextRuntime.load();
  const configuration = createPaymentConfigurationSources(
    reloaded.payments,
  ).square;
  const transport = new CheckoutTransport();
  const adapter = new SquarePaymentAdapter(
    transport,
    async () => {
      const value = await configuration.load();
      if (!value) throw new Error("Square configuration was not reloaded.");
      return value;
    },
  );

  const result = await adapter.submit(paymentAttempt());

  assert.equal(result.state, "Pending");
  assert.deepEqual(transport.requests, [
    {
      method: "POST",
      url: "/api/v1/square/checkouts",
      data: {
        environment: "Sandbox",
        idempotencyKey: "sandbox-checkout-idempotency-1",
        deviceId: officialDeviceId,
        locationId: "LOC-1",
        amountMoney: { amount: 100, currency: "AUD" },
        referenceId: "sandbox-order-1",
        note: "HB POS iPad sandbox-order-1",
      },
    },
  ]);
});

class CheckoutTransport implements HbposTransport {
  public readonly requests: HbposTransportRequest[] = [];

  public async request<T>(
    request: HbposTransportRequest,
  ): Promise<HbposTransportResponse<T>> {
    this.requests.push(request);
    return {
      status: 200,
      data: {
        success: true,
        data: {
          checkoutId: "sandbox-checkout-1",
          environment: "Sandbox",
          status: "PENDING",
          deviceId: SETTINGS_SQUARE_SANDBOX_CHECKOUT_DEVICES[0].id,
          locationId: "LOC-1",
          amountMoney: { amount: 100, currency: "AUD" },
          paymentIds: [],
          cancelReason: null,
          updatedAt: "2026-08-02T00:00:00.000Z",
        },
      } as T,
    };
  }
}

function paymentAttempt(): PaymentAttempt {
  return {
    attemptId: "sandbox-attempt-1",
    idempotencyKey: "sandbox-checkout-idempotency-1",
    orderGuid: "sandbox-order-1",
    provider: "square",
    operation: "purchase",
    amount: { currency: "AUD", cents: 100 },
    state: "Submitted",
    references: {
      checkoutId: null,
      paymentId: null,
      sessionId: null,
      txnRef: null,
      rfn: null,
      voucherReservationToken: null,
    },
    createdAtIso: "2026-08-02T00:00:00.000Z",
    updatedAtIso: "2026-08-02T00:00:00.000Z",
    lastErrorCode: null,
  };
}
