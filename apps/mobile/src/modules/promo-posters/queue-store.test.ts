import assert from "node:assert/strict";
import Module, { createRequire } from "node:module";
import test from "node:test";
import type { PromoPosterQueueItem } from "./types";

type ModuleLoader = (request: string, parent: unknown, isMain: boolean) => unknown;

test("整批 Logo 设置恢复、持久化、部分打印和新批次重置", async () => {
  const item: PromoPosterQueueItem = {
    id: "saved", storeCode: "S1", productName: "Paper", addedAt: "2026-09-21",
    poster: { kind: "special", style: "christmas", size: "A4", productCode: "P1", itemNumber: "HB038-003", title: "PURPLE SHREDDED PAPER", price: 2.5 },
  };
  let saved: unknown = { items: [item], style: "christmas", size: "A4", impose: true, showLogo: false };
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
    assert.equal(store.getState().style, "christmas", "恢复保存的圣诞批次风格");
    assert.equal(store.getState().items.length, 1);
    assert.equal(store.getState().items[0].poster.style, "christmas", "恢复保存的圣诞海报风格");
    store.getState().setShowLogo(true);
    await new Promise<void>((resolve) => setImmediate(resolve));
    assert.equal((saved as { showLogo: boolean }).showLogo, true);
    store.getState().setShowLogo(false);
    store.getState().add({ storeCode: "S1", productName: "Paper", poster: item.poster });
    store.getState().removeMany(["saved"]);
    assert.equal(store.getState().items.length, 1);
    assert.equal(store.getState().showLogo, false, "清理已打印条目不影响期间新加入的批次设置");
    await new Promise<void>((resolve) => setImmediate(resolve));
    assert.equal((saved as { showLogo: boolean }).showLogo, false, "部分打印后 Logo false 真实落盘");
    assert.equal((saved as { items: PromoPosterQueueItem[] }).items[0].poster.style, "christmas", "部分打印后节日海报真实落盘");
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
    store.getState().setStyle("christmas");
    assert.equal(store.getState().style, "christmas", "圣诞风格可保存到当前批次");
    store.getState().add({ storeCode: "S2", productName: "Paper", poster: { ...item.poster, style: "halloween" } });
    assert.equal(store.getState().items.at(-1)?.poster.style, "halloween", "万圣节风格可进入队列并保留");
    store.getState().setStyle("halloween");
    store.getState().setShowLogo(false);
    store.getState().setStyle("christmas");
    assert.equal(store.getState().showLogo, false, "切换节日风格不重置 Logo");
    await new Promise<void>((resolve) => setImmediate(resolve));
    assert.equal((saved as { style: string }).style, "christmas", "节日风格设置真实持久化");
    assert.equal((saved as { items: PromoPosterQueueItem[] }).items.at(-1)?.poster.style, "halloween", "万圣节海报真实持久化");
    store.getState().clear();
    assert.equal(store.getState().showLogo, true, "清空节日队列后恢复 Logo 默认开启");
    await new Promise<void>((resolve) => setImmediate(resolve));
    assert.equal((saved as { showLogo: boolean }).showLogo, true);
    assert.deepEqual((saved as { items: unknown[] }).items, [], "清空节日队列真实落盘为空");
  } finally {
    moduleWithLoader._load = originalLoad;
  }
});
