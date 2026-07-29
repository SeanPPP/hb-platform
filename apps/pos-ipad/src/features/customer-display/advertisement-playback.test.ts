import assert from "node:assert/strict";
import test from "node:test";

import type {
  CustomerDisplayAdvertisementItem,
  CustomerDisplayAdvertisementResponse,
} from "./advertisement-api";
import {
  CustomerDisplayAdvertisementPlayback,
  type CachedCustomerDisplayAdvertisement,
} from "./advertisement-playback";

test("只缓存当前有效素材，按 sort/id 轮播本地 URI，并在刷新间隔内复用快照", async () => {
  let remoteCalls = 0;
  const published: (
    | Readonly<{ kind: "image" | "video"; localUri: string }>
    | null
  )[] = [];
  const playback = new CustomerDisplayAdvertisementPlayback({
    now: () => new Date("2026-07-28T00:00:00.000Z"),
    remote: {
      async getActive(): Promise<CustomerDisplayAdvertisementResponse> {
        remoteCalls += 1;
        return {
          storeCode: "S001",
          generatedAtIso: "2026-07-28T00:00:00.000Z",
          items: [
            advert("expired", {
              effectiveEndIso: "2026-07-27T23:59:59.000Z",
            }),
            advert("video", { kind: "video", sortOrder: 2 }),
            advert("image", { sortOrder: 1 }),
          ],
        };
      },
    },
    cache: {
      async cache(items) {
        return items.map(
          (item): CachedCustomerDisplayAdvertisement => ({
            ...item,
            localUri: `file:///cache/${item.id}.${item.kind === "image" ? "png" : "mp4"}`,
          }),
        );
      },
    },
    sink: {
      async setAdvert(advertisement) {
        published.push(advertisement);
      },
    },
  });

  assert.equal(await playback.refresh("S001"), "updated");
  assert.equal(await playback.refresh("S001"), "unchanged");
  assert.equal(remoteCalls, 1);
  assert.deepEqual(published, [
    { kind: "image", localUri: "file:///cache/image.png" },
  ]);

  assert.equal(await playback.advance(), true);
  assert.deepEqual(published.at(-1), {
    kind: "video",
    localUri: "file:///cache/video.mp4",
  });
});

test("远端或缓存失败保留最后一个本地快照；门店切换失败不泄漏旧门店广告", async () => {
  let fail = false;
  const published: unknown[] = [];
  const playback = new CustomerDisplayAdvertisementPlayback({
    now: () => new Date("2026-07-28T00:00:00.000Z"),
    remote: {
      async getActive(storeCode) {
        if (fail) throw new Error("offline");
        return {
          storeCode,
          generatedAtIso: "2026-07-28T00:00:00.000Z",
          items: [advert("one")],
        };
      },
    },
    cache: {
      async cache(items) {
        return items.map((item) => ({
          ...item,
          localUri: "file:///cache/one.png",
        }));
      },
    },
    sink: {
      async setAdvert(advertisement) {
        published.push(advertisement);
      },
    },
  });

  assert.equal(await playback.refresh("S001"), "updated");
  fail = true;
  assert.equal(await playback.refresh("S001", true), "retained");
  assert.equal(await playback.refresh("S002", true), "cleared");
  assert.equal(published.at(-1), null);
});

test("开始播放按五分钟刷新、十秒轮播，停止后取消两个计时器", async () => {
  const scheduled: Readonly<{
    intervalMs: number;
    listener(): void;
    cancel(): void;
  }>[] = [];
  let cancellations = 0;
  const playback = new CustomerDisplayAdvertisementPlayback({
    now: () => new Date("2026-07-28T00:00:00.000Z"),
    remote: {
      async getActive(storeCode) {
        return {
          storeCode,
          generatedAtIso: "2026-07-28T00:00:00.000Z",
          items: [],
        };
      },
    },
    cache: { async cache() { return []; } },
    sink: { async setAdvert() {} },
    scheduler: {
      every(intervalMs, listener) {
        const entry = {
          intervalMs,
          listener,
          cancel() {
            cancellations += 1;
          },
        };
        scheduled.push(entry);
        return entry.cancel;
      },
    },
  });

  playback.start("S001");
  assert.deepEqual(
    scheduled.map((entry) => entry.intervalMs),
    [300_000, 10_000],
  );
  playback.stop();
  assert.equal(cancellations, 2);
});

function advert(
  id: string,
  overrides: Partial<CustomerDisplayAdvertisementItem> = {},
): CustomerDisplayAdvertisementItem {
  return {
    id,
    kind: "image",
    remoteUrl: `https://cdn.example.com/${id}.png`,
    objectKey: `ads/${id}.png`,
    originalFileName: `${id}.png`,
    contentType: "image/png",
    fileSize: 1_024,
    effectiveStartIso: "2026-07-27T00:00:00.000Z",
    effectiveEndIso: "2026-07-29T00:00:00.000Z",
    sortOrder: 0,
    ...overrides,
  };
}
