import assert from "node:assert/strict";
import test from "node:test";

import {
  CatalogMaintenancePresenter,
  type CatalogMaintenancePort,
} from "./catalog-maintenance-presenter";

class MemoryCatalogMaintenancePort implements CatalogMaintenancePort {
  public readonly storeCodes: string[] = [];
  public hold: Promise<void> | null = null;
  public failure: unknown = null;

  public async downloadAndActivate(input: Readonly<{ storeCode: string }>) {
    this.storeCodes.push(input.storeCode);
    await this.hold;
    if (this.failure) throw this.failure;
    return { snapshotId: "catalog-20260728-1", itemCount: 128 };
  }
}

test("刷新只把已认证的固定门店传给窄 Port，并展示快照结果", async () => {
  const port = new MemoryCatalogMaintenancePort();
  const presenter = new CatalogMaintenancePresenter({
    authenticatedStoreCode: "BNE-01",
    port,
  });

  await presenter.refresh();

  assert.deepEqual(port.storeCodes, ["BNE-01"]);
  assert.deepEqual(presenter.getState(), {
    kind: "success",
    snapshotId: "catalog-20260728-1",
    itemCount: 128,
  });
});

test("重复点击刷新单飞，只发起一次下载", async () => {
  let release!: () => void;
  const port = new MemoryCatalogMaintenancePort();
  port.hold = new Promise<void>((resolve) => { release = resolve; });
  const presenter = new CatalogMaintenancePresenter({
    authenticatedStoreCode: "BNE-01",
    port,
  });

  const first = presenter.refresh();
  const second = presenter.refresh();
  assert.equal(first, second);
  assert.deepEqual(presenter.getState(), { kind: "downloading" });
  await Promise.resolve();
  assert.deepEqual(port.storeCodes, ["BNE-01"]);

  release();
  await first;
  assert.equal(presenter.getState().kind, "success");
});

test("底层异常被收敛为稳定安全错误码，不泄漏 HTTP 或凭据", async () => {
  const port = new MemoryCatalogMaintenancePort();
  port.failure = new Error("GET https://api.example.test failed; bearer secret-token");
  const presenter = new CatalogMaintenancePresenter({
    authenticatedStoreCode: "BNE-01",
    port,
  });

  await presenter.refresh();

  assert.deepEqual(presenter.getState(), {
    kind: "failed",
    errorCode: "catalog-refresh-failed",
  });
});
