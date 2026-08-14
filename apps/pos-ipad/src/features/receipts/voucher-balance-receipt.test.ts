import assert from "node:assert/strict";
import test from "node:test";

import {
  PostSyncVoucherLatestBalanceApi,
  VOUCHER_BALANCE_RECOVERY_BATCH_SIZE,
  VoucherBalancePostSyncService,
  VoucherBalanceReceiptRenderer,
  type VoucherBalanceMaterial,
  type VoucherLatestBalanceConfirmation,
} from "./voucher-balance-receipt";

import type { HbposTransport } from "@/core/api";
import { VoucherHbposApi } from "@/features/payments/voucher";

const ORDER_GUID = "018f1b9b-47c5-7c1b-9f8e-39c5cb3b9d01";
const CONFIRMED_AT = "2026-07-31T00:00:00.000Z";

function material(
  overrides: Partial<VoucherBalanceMaterial> = {},
): VoucherBalanceMaterial {
  return {
    attemptId: "voucher-attempt-1",
    orderGuid: ORDER_GUID,
    storeCode: "S001",
    voucherCode: "VC100",
    confirmation: null,
    ...overrides,
  };
}

test("订单同步成功后查询同一礼券并只保存、打印 post-sync 最新余额", async () => {
  const materials = new MemoryMaterials([material()]);
  const queued: {
    jobId: string;
    orderGuid: string;
    receiptBytes: Uint8Array;
  }[] = [];
  const apiCalls: (readonly [string, string])[] = [];
  const service = new VoucherBalancePostSyncService({
    api: {
      async query(storeCode, voucherCode) {
        apiCalls.push([storeCode, voucherCode]);
        return {
          found: true,
          voucher: {
            voucherCode: "VC100",
            storeCode: "S001",
            status: "1",
            remainingAmount: 6.25,
          },
        };
      },
    },
    materials,
    renderer: new VoucherBalanceReceiptRenderer(
      {
        async getFrozenReturnReceiptSettings() {
          return {
            printerId: "printer-1",
            paper: "80mm",
            locale: "zh-CN",
            store: {
              brandName: "Hot Bargain",
              storeName: "测试店",
              address: "",
              phone: "",
              abn: "",
              returnPolicy: "Refunds within 14 days with proof of purchase.",
            },
          };
        },
      },
    ),
    printQueue: {
      async hasPrintJob() {
        return false;
      },
      async enqueuePrintJobOnce(input) {
        queued.push(input);
        return "created";
      },
    },
    nowIso: () => CONFIRMED_AT,
  });

  await service.afterOrderAccepted(ORDER_GUID);

  assert.deepEqual(apiCalls, [["S001", "VC100"]]);
  assert.deepEqual(materials.saved, [
    {
      attemptId: "voucher-attempt-1",
      confirmation: {
        status: "confirmed",
        remainingCents: 625,
        confirmedAtIso: CONFIRMED_AT,
      },
    },
  ]);
  assert.equal(queued.length, 1);
  assert.equal(queued[0]?.jobId, "voucher-balance:voucher-attempt-1");
  assert.equal(queued[0]?.orderGuid, ORDER_GUID);
  const receipt = new TextDecoder().decode(queued[0]?.receiptBytes);
  assert.match(receipt, /Hot Bargain/u);
  assert.match(receipt, /VC100/);
  assert.match(receipt, /AU\$6\.25/);
  assert.equal(
    containsSequence(
      [...(queued[0]?.receiptBytes ?? [])],
      [0xcd, 0xcb, 0xbf, 0xee, 0xd3, 0xeb, 0xcd, 0xcb, 0xbb, 0xf5],
    ),
    true,
    "中文 Return Policy 标题必须使用 GB18030 输出",
  );
  assert.match(receipt, /Refunds within 14 days with proof of purchase\./);
  assert.doesNotMatch(receipt, /AU\$7\.50/);
});

test("礼券余额联抬头依次回退 Brand、Store、Store Code", async () => {
  const render = async (brandName: string, storeName: string) => {
    const renderer = new VoucherBalanceReceiptRenderer({
      async getFrozenReturnReceiptSettings() {
        return {
          printerId: "printer-1",
          paper: "80mm",
          locale: "en",
          store: {
            brandName,
            storeName,
            address: "",
            phone: "",
            abn: "",
            returnPolicy: "",
          },
        };
      },
    });
    const rendered = await renderer.render({
      ...material(),
      confirmation: {
        status: "confirmed",
        remainingCents: 625,
        confirmedAtIso: CONFIRMED_AT,
      },
    });
    assert.ok(rendered);
    return new TextDecoder().decode(rendered.receiptBytes);
  };

  assert.match(await render("Hot Bargain", "Brisbane"), /Hot Bargain/u);
  assert.match(await render("", "Brisbane"), /Brisbane/u);
  assert.match(await render("", ""), /S001/u);
});

test("zh-CN 礼券余额联启用中文模式并使用 GB18030 文本字节", async () => {
  const renderer = new VoucherBalanceReceiptRenderer({
    async getFrozenReturnReceiptSettings() {
      return {
        printerId: "printer-1",
        paper: "80mm",
        locale: "zh-CN",
        store: {
          brandName: "Hot Bargain",
          storeName: "测试店",
          address: "",
          phone: "",
          abn: "",
          returnPolicy: "",
        },
      };
    },
  });

  const rendered = await renderer.render({
    ...material(),
    confirmation: {
      status: "confirmed",
      remainingCents: 625,
      confirmedAtIso: CONFIRMED_AT,
    },
  });
  assert.ok(rendered);
  const bytes = [...rendered.receiptBytes];

  assert.equal(
    containsSequence(bytes, [0x1b, 0x40, 0x1c, 0x26]),
    true,
    "ESC @ 后必须立即进入中文字符模式",
  );
  assert.equal(
    containsSequence(bytes, [0xc0, 0xf1, 0xc8, 0xaf, 0xd3, 0xe0, 0xb6, 0xee]),
    true,
    "礼券余额必须以 GB18030 字节输出",
  );
  assert.equal(
    containsSequence(bytes, [
      0xe7, 0xa4, 0xbc,
      0xe5, 0x88, 0xb8,
      0xe4, 0xbd, 0x99,
      0xe9, 0xa2, 0x9d,
    ]),
    false,
    "不得再输出 UTF-8 中文字节",
  );
});

test("同一礼券多个 tender 只查询一次、只生成一张余额联", async () => {
  const materials = new MemoryMaterials([
    material({ attemptId: "voucher-attempt-2", voucherCode: "vc100" }),
    material({ attemptId: "voucher-attempt-1" }),
  ]);
  let queryCount = 0;
  let queueCount = 0;
  const service = new VoucherBalancePostSyncService({
    api: {
      async query() {
        queryCount += 1;
        return {
          found: true,
          voucher: {
            voucherCode: "VC100",
            storeCode: "S001",
            status: "1",
            remainingAmount: 1,
          },
        };
      },
    },
    materials,
    renderer: {
      async render() {
        return {
          printerId: "printer-1",
          receiptBytes: Uint8Array.of(1),
        };
      },
    },
    printQueue: {
      async hasPrintJob() {
        return false;
      },
      async enqueuePrintJobOnce() {
        queueCount += 1;
        return "created";
      },
    },
    nowIso: () => CONFIRMED_AT,
  });

  await service.afterOrderAccepted(ORDER_GUID);

  assert.equal(queryCount, 1);
  assert.equal(materials.saved.length, 1);
  assert.equal(materials.saved[0]?.attemptId, "voucher-attempt-1");
  assert.equal(queueCount, 1);
});

test("已耐久确认的余额重放时不再次联网，也不会改写原快照", async () => {
  const confirmation: VoucherLatestBalanceConfirmation = {
    status: "confirmed",
    remainingCents: 625,
    confirmedAtIso: CONFIRMED_AT,
  };
  const materials = new MemoryMaterials([
    material({ confirmation }),
  ]);
  let queryCount = 0;
  let renderedBalance = -1;
  const service = new VoucherBalancePostSyncService({
    api: {
      async query() {
        queryCount += 1;
        throw new Error("不得再次查询");
      },
    },
    materials,
    renderer: {
      async render(input) {
        renderedBalance = input.confirmation.remainingCents ?? -1;
        return {
          printerId: "printer-1",
          receiptBytes: Uint8Array.of(1),
        };
      },
    },
    printQueue: {
      async hasPrintJob() {
        return false;
      },
      async enqueuePrintJobOnce() {
        return "created";
      },
    },
    nowIso: () => "2026-07-31T01:00:00.000Z",
  });

  await service.afterOrderAccepted(ORDER_GUID);

  assert.equal(queryCount, 0);
  assert.equal(materials.saved.length, 0);
  assert.equal(renderedBalance, 625);
});

test("余额为零、未找到或身份不一致时落为不可确认且不打印", async (t) => {
  const replies = [
    { found: false },
    {
      found: true,
      voucher: {
        voucherCode: "OTHER",
        storeCode: "S001",
        status: "1",
        remainingAmount: 6.25,
      },
    },
    {
      found: true,
      voucher: {
        voucherCode: "VC100",
        storeCode: "S999",
        status: "1",
        remainingAmount: 6.25,
      },
    },
    {
      found: true,
      voucher: {
        voucherCode: "VC100",
        storeCode: "S001",
        status: "1",
        remainingAmount: 0,
      },
    },
  ] as const;

  for (const [index, reply] of replies.entries()) {
    await t.test(String(index), async () => {
      const materials = new MemoryMaterials([material()]);
      let queueCount = 0;
      const service = new VoucherBalancePostSyncService({
        api: { async query() { return reply; } },
        materials,
        renderer: {
          async render() {
            throw new Error("不可确认或零余额不应渲染");
          },
        },
        printQueue: {
          async hasPrintJob() {
            return false;
          },
          async enqueuePrintJobOnce() {
            queueCount += 1;
            return "created";
          },
        },
        nowIso: () => CONFIRMED_AT,
      });

      await service.afterOrderAccepted(ORDER_GUID);

      assert.equal(queueCount, 0);
      assert.equal(materials.saved.length, 1);
      assert.equal(
        materials.saved[0]?.confirmation.status,
        index === 3 ? "confirmed" : "unavailable",
      );
    });
  }
});

test("服务端核销后真实礼券查询返回 404 时按零可用余额处理且不阻塞订单同步", async () => {
  const materials = new MemoryMaterials([material()]);
  const transport: HbposTransport = {
    async request<T>() {
      return {
        status: 404,
        data: {
          success: false,
          errorCode: "VOUCHER_NOT_FOUND",
        } as T,
      };
    },
  };
  const service = new VoucherBalancePostSyncService({
    api: new PostSyncVoucherLatestBalanceApi(
      new VoucherHbposApi(transport),
    ),
    materials,
    renderer: {
      async render() {
        throw new Error("404 不应生成余额联");
      },
    },
    printQueue: {
      async hasPrintJob() {
        return false;
      },
      async enqueuePrintJobOnce() {
        throw new Error("404 不应进入打印队列");
      },
    },
    nowIso: () => CONFIRMED_AT,
  });

  await service.afterOrderAccepted(ORDER_GUID);

  assert.deepEqual(materials.saved, [
    {
      attemptId: "voucher-attempt-1",
      confirmation: {
        status: "unavailable",
        remainingCents: null,
        confirmedAtIso: CONFIRMED_AT,
      },
    },
  ]);
});

test("全门店礼券响应的门店码为空时仍打印同一礼券的最新余额", async (t) => {
  for (const responseStoreCode of [null, ""] as const) {
    await t.test(String(responseStoreCode), async () => {
      const materials = new MemoryMaterials([material()]);
      let queueCount = 0;
      const service = new VoucherBalancePostSyncService({
        api: {
          async query() {
            return {
              found: true,
              voucher: {
                voucherCode: "VC100",
                storeCode: responseStoreCode,
                status: "1",
                remainingAmount: 6.25,
              },
            };
          },
        },
        materials,
        renderer: {
          async render() {
            return {
              printerId: "printer-1",
              receiptBytes: Uint8Array.of(1),
            };
          },
        },
        printQueue: {
          async hasPrintJob() {
            return false;
          },
          async enqueuePrintJobOnce() {
            queueCount += 1;
            return "created";
          },
        },
        nowIso: () => CONFIRMED_AT,
      });

      await service.afterOrderAccepted(ORDER_GUID);

      assert.equal(
        materials.saved[0]?.confirmation.status,
        "confirmed",
      );
      assert.equal(queueCount, 1);
    });
  }
});

test("无打印机只保留已确认快照，不阻塞同步；配置恢复后补建打印任务", async () => {
  const materials = new MemoryMaterials([material()]);
  let settingsAvailable = false;
  let queueCount = 0;
  const service = new VoucherBalancePostSyncService({
    api: {
      async query() {
        return {
          found: true,
          voucher: {
            voucherCode: "VC100",
            storeCode: "S001",
            status: "1",
            remainingAmount: 6.25,
          },
        };
      },
    },
    materials,
    renderer: new VoucherBalanceReceiptRenderer({
      async getFrozenReturnReceiptSettings() {
        return settingsAvailable
          ? {
              printerId: "printer-1",
              paper: "80mm",
              locale: "zh-CN",
              store: {
                brandName: "",
                storeName: "",
                address: "",
                phone: "",
                abn: "",
                returnPolicy: "",
              },
            }
          : null;
      },
    }),
    printQueue: {
      async hasPrintJob() {
        return false;
      },
      async enqueuePrintJobOnce() {
        queueCount += 1;
        return "created";
      },
    },
    nowIso: () => CONFIRMED_AT,
  });

  await service.afterOrderAccepted(ORDER_GUID);
  assert.equal(queueCount, 0);
  assert.equal(materials.saved.length, 1);

  settingsAvailable = true;
  await service.recoverPendingPrints();
  assert.equal(queueCount, 1);
});

test("同一礼券用于不同已同步订单时，每笔订单各自恢复对应余额联", async () => {
  const confirmation: VoucherLatestBalanceConfirmation = {
    status: "confirmed",
    remainingCents: 625,
    confirmedAtIso: CONFIRMED_AT,
  };
  const materials = new MemoryMaterials([
    material({ confirmation }),
    material({
      attemptId: "voucher-attempt-2",
      orderGuid: "018f1b9b-47c5-7c1b-9f8e-39c5cb3b9d09",
      confirmation,
    }),
  ]);
  const jobs: string[] = [];
  const service = new VoucherBalancePostSyncService({
    api: {
      async query() {
        throw new Error("恢复不应联网");
      },
    },
    materials,
    renderer: {
      async render() {
        return {
          printerId: "printer-1",
          receiptBytes: Uint8Array.of(1),
        };
      },
    },
    printQueue: {
      async hasPrintJob() {
        return false;
      },
      async enqueuePrintJobOnce(input) {
        jobs.push(input.jobId);
        return "created";
      },
    },
    nowIso: () => CONFIRMED_AT,
  });

  await service.recoverPendingPrints();

  assert.deepEqual(jobs, [
    "voucher-balance:voucher-attempt-1",
    "voucher-balance:voucher-attempt-2",
  ]);
});

test("启动恢复会分批补建超过 200 张已确认的礼券余额联", async () => {
  const confirmation: VoucherLatestBalanceConfirmation = {
    status: "confirmed",
    remainingCents: 100,
    confirmedAtIso: CONFIRMED_AT,
  };
  const values = Array.from(
    { length: VOUCHER_BALANCE_RECOVERY_BATCH_SIZE + 1 },
    (_, index) =>
      material({
        attemptId: `voucher-attempt-${String(index).padStart(3, "0")}`,
        orderGuid: `order-${String(index).padStart(3, "0")}`,
        voucherCode: `VC${String(index).padStart(3, "0")}`,
        confirmation,
      }),
  );
  const queued = new Set<string>();
  const requestedLimits: number[] = [];
  const service = new VoucherBalancePostSyncService({
    api: {
      async query() {
        throw new Error("恢复不应联网");
      },
    },
    materials: {
      async listForOrder() {
        return [];
      },
      async listSyncedPendingPrints(
        limit = VOUCHER_BALANCE_RECOVERY_BATCH_SIZE,
      ) {
        requestedLimits.push(limit);
        return values
          .filter(
            (entry) =>
              !queued.has(`voucher-balance:${entry.attemptId}`),
          )
          .slice(0, limit);
      },
      async saveConfirmation() {
        throw new Error("恢复不应改写余额快照");
      },
    },
    renderer: {
      async render() {
        return {
          printerId: "printer-1",
          receiptBytes: Uint8Array.of(1),
        };
      },
    },
    printQueue: {
      async hasPrintJob(jobId) {
        return queued.has(jobId);
      },
      async enqueuePrintJobOnce(input) {
        if (queued.has(input.jobId)) return "existing";
        queued.add(input.jobId);
        return "created";
      },
    },
    nowIso: () => CONFIRMED_AT,
  });

  await service.recoverPendingPrints();

  assert.equal(queued.size, VOUCHER_BALANCE_RECOVERY_BATCH_SIZE + 1);
  assert.deepEqual(requestedLimits, [
    VOUCHER_BALANCE_RECOVERY_BATCH_SIZE,
    VOUCHER_BALANCE_RECOVERY_BATCH_SIZE,
  ]);
});

class MemoryMaterials {
  public readonly saved: {
    attemptId: string;
    confirmation: VoucherLatestBalanceConfirmation;
  }[] = [];

  public constructor(
    private readonly values: VoucherBalanceMaterial[],
  ) {}

  public listForOrder(orderGuid: string): Promise<readonly VoucherBalanceMaterial[]> {
    return Promise.resolve(
      this.values.filter((entry) => entry.orderGuid === orderGuid),
    );
  }

  public listSyncedPendingPrints(
    limit = VOUCHER_BALANCE_RECOVERY_BATCH_SIZE,
  ): Promise<readonly VoucherBalanceMaterial[]> {
    return Promise.resolve(this.values.slice(0, limit));
  }

  public saveConfirmation(
    attemptId: string,
    confirmation: VoucherLatestBalanceConfirmation,
  ): Promise<void> {
    const index = this.values.findIndex(
      (entry) => entry.attemptId === attemptId,
    );
    if (index < 0) throw new Error("missing material");
    this.values[index] = {
      ...this.values[index]!,
      confirmation,
    };
    this.saved.push({ attemptId, confirmation });
    return Promise.resolve();
  }
}

function containsSequence(
  source: readonly number[],
  expected: readonly number[],
): boolean {
  return source.some((_, index) =>
    expected.every((value, offset) => source[index + offset] === value),
  );
}
