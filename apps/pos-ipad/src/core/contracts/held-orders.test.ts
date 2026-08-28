import assert from "node:assert/strict";
import test from "node:test";

import type { HoldCartCommand } from "./held-orders";
import type { PricingCartStateSnapshot } from "@hb/pos-domain/core/contracts/pricing-cart-state";

const pricingState = {} as PricingCartStateSnapshot;

test("本地挂单合同只接受 V1 加密 wrapper", () => {
  const v1: HoldCartCommand["payload"] = {
    version: 1,
    pricingState,
  };

  const v2: HoldCartCommand["payload"] = {
    // 本地 SQLite 的 payload_version、加解密和 validator 都固定为 V1。
    // @ts-expect-error V2 是远程 SharedSaleCart 合同，不能穿透本地 wrapper。
    version: 2,
    pricingState,
  };

  assert.equal(v1.version, 1);
  void v2;
});
