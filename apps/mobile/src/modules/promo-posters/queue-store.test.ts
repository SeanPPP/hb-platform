import assert from "node:assert/strict";
import Module, { createRequire } from "node:module";
import test from "node:test";
import type { PromoPosterQueueItem } from "./types";

type ModuleLoader = (request: string, parent: unknown, isMain: boolean) => unknown;

test("整批 Logo 设置恢复、持久化、部分打印和新批次重置", async () => {
  const item: PromoPosterQueueItem = {
    id: "saved", storeCode: "S1", productName: "Paper", addedAt: "2026-09-21",
    poster: { kind: "special", style: "classic", size: "A4", productCode: "P1", itemNumber: "HB038-003", title: "PURPLE SHREDDED PAPER", price: 2.5 },
  };
  let saved: unknown = { items: [item], style: "classic", size: "A4", impose: true, showLogo: false };
  const moduleWithLoader = Module as unknown as { _load: ModuleLoader };
  const originalLoad = moduleWithLoader._load;
  const loadModule = createRequire(__filename);
  moduleWithLoader._load = function mockedLoad(request, parent, isMain) {
    if (request === "@/shared/storage/async-storage") {
      return { AppAsyncStorage: {
        getObject: async () => saved,
        setObject: async (_key: string, value: unknown) => { saved = value; },
      } };
    }
    return originalLoad.call(this, request, parent, isMain);
  };
  try {
    const { usePromoPosterQueueStore: store, hydratePromoPosterQueue } = loadModule("./queue-store") as typeof import("./queue-store");
    assert.equal(store.getState().showLogo, true);
    await hydratePromoPosterQueue();
    assert.equal(store.getState().showLogo, false, "恢复未完成批次的设置");
    assert.equal(store.getState().items.length, 1);
    store.getState().setShowLogo(true);
    await new Promise<void>((resolve) => setImmediate(resolve));
    assert.equal((saved as { showLogo: boolean }).showLogo, true);
    store.getState().setShowLogo(false);
    store.getState().add({ storeCode: "S1", productName: "Paper", poster: item.poster });
    store.getState().removeMany(["saved"]);
    assert.equal(store.getState().items.length, 1);
    assert.equal(store.getState().showLogo, false, "清理已打印条目不影响期间新加入的批次设置");
    const last = store.getState().items[0];
    store.getState().remove(last.id);
    assert.equal(store.getState().showLogo, true, "移除最后一张后新批次默认开启");
    store.getState().restore(last, 0);
    store.getState().setShowLogo(false);
    assert.equal(store.getState().showLogo, false, "撤销移除可恢复原批次设置");
    store.getState().clear();
    assert.equal(store.getState().showLogo, true);
    store.getState().add({ storeCode: "S1", productName: "Paper", poster: item.poster });
    store.getState().setShowLogo(false);
    store.getState().removeMany(store.getState().items.map((entry) => entry.id));
    assert.equal(store.getState().showLogo, true, "完成整批后恢复默认");
    store.getState().add({ storeCode: "S1", productName: "Paper", poster: item.poster });
    store.getState().setShowLogo(false);
    store.getState().replaceAll({ storeCode: "S2", productName: "Paper", poster: item.poster });
    assert.equal(store.getState().items[0].storeCode, "S2");
    assert.equal(store.getState().showLogo, true, "跨店替换清空旧批次后恢复默认");
    await new Promise<void>((resolve) => setImmediate(resolve));
    assert.equal((saved as { showLogo: boolean }).showLogo, true);
  } finally {
    moduleWithLoader._load = originalLoad;
  }
});
