import assert from "node:assert/strict";
import test from "node:test";

import {
  buildCustomerDisplaySnapshot,
  CustomerDisplayPublisher,
} from "./customer-display-publisher";

import type {
  CartSnapshot,
  CustomerDisplaySnapshot,
  DisplayStatus,
  ExternalCustomerDisplayPort,
} from "@/core/contracts";

const advertisementCacheRootUri =
  "file:///cache/customer-display-ads/";

test("客显快照按 WPF 含税总额的 1/11 计算 GST，且只投影冻结购物车白名单", () => {
  const snapshot = buildCustomerDisplaySnapshot(
    7,
    {
      mode: "cart",
      cart: cart(),
      changeCents: 0,
      advert: {
        kind: "image",
        localUri:
          "file:///cache/customer-display-ads/ad-1.jpg",
      },
    },
    advertisementCacheRootUri,
  );

  assert.deepEqual(snapshot, {
    revision: 7,
    mode: "cart",
    items: [
      {
        name: "Tea",
        quantity: "2",
        amount: { currency: "AUD", cents: 1_234 },
      },
    ],
    gst: { currency: "AUD", cents: 112 },
    discount: { currency: "AUD", cents: 100 },
    total: { currency: "AUD", cents: 1_234 },
    change: { currency: "AUD", cents: 0 },
    advert: {
      kind: "image",
      localUri:
        "file:///cache/customer-display-ads/ad-1.jpg",
    },
  });
  assert.doesNotMatch(
    JSON.stringify(snapshot),
    /"(?:cashier|token|customer|provider|authorization|reference)[^"]*":/iu,
  );
});

test("客显只接受本地广告 URI、非负 revision 和安全整数找零", () => {
  for (const localUri of [
    "https://example.com/remote.mp4",
    "file://evil.example/cache/customer-display-ads/ad.mp4",
    "file:///private/customer-data/ad.mp4",
    "file:///cache/customer-display-ads/%2e%2e/secret.mp4",
  ]) {
    assert.throws(
      () =>
        buildCustomerDisplaySnapshot(
          1,
          {
            mode: "idle",
            cart: null,
            changeCents: 0,
            advert: { kind: "video", localUri },
          },
          advertisementCacheRootUri,
        ),
      /local advertisement URI/i,
    );
  }
  assert.throws(
    () =>
      buildCustomerDisplaySnapshot(
        -1,
        {
          mode: "change",
          cart: cart(),
          changeCents: 10,
          advert: null,
        },
        advertisementCacheRootUri,
      ),
    /revision/i,
  );
});

test("发布器去重相同画面，失败不影响主收银，后续 revision 仍严格递增", async () => {
  const display = new FakeDisplay();
  const publisher = new CustomerDisplayPublisher(display, {
    advertisementCacheRootUri,
  });
  const frame = {
    mode: "cart" as const,
    cart: cart(),
    changeCents: 0,
    advert: null,
  };

  const first = await publisher.publish(frame);
  assert.equal(first.status, "published");
  const firstRevision = first.revision;
  assert.deepEqual(await publisher.publish(frame), {
    status: "unchanged",
    revision: firstRevision,
  });

  display.failNextPublish = true;
  assert.deepEqual(
    await publisher.publish({ ...frame, mode: "payment" }),
    {
      status: "failed",
      revision: firstRevision + 1,
      errorCode: "DISPLAY_PUBLISH_FAILED",
    },
  );
  assert.deepEqual(
    await publisher.publish({ ...frame, mode: "success" }),
    {
      status: "published",
      revision: firstRevision + 2,
    },
  );
  assert.deepEqual(
    display.snapshots.map((snapshot) => snapshot.revision),
    [firstRevision, firstRevision + 2],
  );
});

test("同一 JS producer session 重建 Publisher 后继续使用更大的 revision", async () => {
  const display = new FakeDisplay();
  const firstPublisher = new CustomerDisplayPublisher(display, {
    advertisementCacheRootUri,
  });
  const first = await firstPublisher.publish({
    mode: "idle",
    cart: null,
    changeCents: 0,
    advert: null,
  });
  const secondPublisher = new CustomerDisplayPublisher(display, {
    advertisementCacheRootUri,
  });
  const second = await secondPublisher.publish({
    mode: "cart",
    cart: cart(),
    changeCents: 0,
    advert: null,
  });

  assert.equal(first.status, "published");
  assert.equal(second.status, "published");
  assert.ok(second.revision > first.revision);
  assert.deepEqual(
    display.snapshots.map((snapshot) => snapshot.revision),
    [first.revision, second.revision],
  );
});

test("启停和状态读取失败均返回受控结果，不把外屏故障抛给交易流程", async () => {
  const display = new FakeDisplay();
  const publisher = new CustomerDisplayPublisher(display, {
    advertisementCacheRootUri,
  });
  display.failSetEnabled = true;
  display.failGetStatus = true;

  assert.deepEqual(await publisher.setEnabled(true), {
    status: "failed",
    errorCode: "DISPLAY_ENABLE_FAILED",
  });
  assert.equal(await publisher.getStatus(), "failed");
});

class FakeDisplay implements ExternalCustomerDisplayPort {
  public failNextPublish = false;
  public failSetEnabled = false;
  public failGetStatus = false;
  public readonly snapshots: CustomerDisplaySnapshot[] = [];

  public async getStatus(): Promise<DisplayStatus> {
    if (this.failGetStatus) throw new Error("display disconnected");
    return "ready";
  }

  public async setEnabled(): Promise<void> {
    if (this.failSetEnabled) throw new Error("display enable failed");
  }

  public async publish(snapshot: CustomerDisplaySnapshot): Promise<void> {
    if (this.failNextPublish) {
      this.failNextPublish = false;
      throw new Error("display cable removed");
    }
    this.snapshots.push(snapshot);
  }

  public subscribe(): () => void {
    return () => undefined;
  }
}

function cart(): CartSnapshot {
  return {
    revision: 4,
    mode: "sale",
    lines: [
      {
        lineId: "line-1",
        productCode: "P-1",
        itemNumber: "I-1",
        lookupCode: "930000000001",
        displayName: "Tea",
        quantity: "2",
        unitPrice: { currency: "AUD", cents: 667 },
        discount: { currency: "AUD", cents: 100 },
        actualAmount: { currency: "AUD", cents: 1_234 },
        priceSource: "catalog",
        syncProvenance: {
          referenceCode: "must-not-reach-display",
          priceSource: 3,
        },
        kind: "sale",
        returnSourceKey: null,
        originalOrderGuid: null,
        originalOrderDetailGuid: null,
      },
    ],
    subtotal: { currency: "AUD", cents: 1_334 },
    discount: { currency: "AUD", cents: 100 },
    actualAmount: { currency: "AUD", cents: 1_234 },
  };
}
