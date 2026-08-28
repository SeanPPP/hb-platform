import assert from "node:assert/strict";
import test from "node:test";

import type { RemoteHistoryReprintPort } from "@hb/pos-domain/features/remote-history/remote-history-presenter";
import {
  createHbposRemoteHistoryPresenterFactory,
  resolveRemoteHistoryPresenterFactory,
} from "./remote-history-runtime";

import type {
  HbposTransport,
  HbposTransportRequest,
} from "@/core/api/hbpos-api";

test("runtime resolver 只接受结构完整的 feature-local factory", () => {
  const factory = { createPresenter() {} };
  assert.equal(
    resolveRemoteHistoryPresenterFactory({ remoteHistory: factory }),
    factory,
  );
  assert.equal(resolveRemoteHistoryPresenterFactory({}), null);
  assert.equal(
    resolveRemoteHistoryPresenterFactory({
      remoteHistory: { createPresenter: "not-a-function" },
    }),
    null,
  );
});

test("factory 将可信身份和在线门禁交给 presenter，并通过 Hbpos adapter 请求", async () => {
  const requests: HbposTransportRequest[] = [];
  const transport: HbposTransport = {
    async request<T>(request: HbposTransportRequest) {
      requests.push(request);
      return {
        status: 200,
        data: {
          success: true,
          data: {
            orders: [],
          },
        } as T,
      };
    },
  };
  let trustedSessionResolutions = 0;
  const presenter = createHbposRemoteHistoryPresenterFactory(transport, () => {
    trustedSessionResolutions += 1;
    return {
      trustedStoreCode: "S1",
      currentDeviceCode: "IPAD-1",
      permissionCodes: ["Permissions.PosTerminal.History.View"],
    };
  }).createPresenter({ online: true });

  await presenter.refresh();

  assert.equal(trustedSessionResolutions, 1);
  assert.equal(presenter.state.kind, "empty");
  assert.equal(requests[0]?.params?.storeCode, "S1");
  presenter.destroy();
});

test("factory 仅注入按可信会话绑定的窄重打 port，不暴露打印内容或设备参数", async () => {
  const reprintCalls: string[] = [];
  const reprintPort: RemoteHistoryReprintPort = {
    canReprint: () => true,
    async reprintExistingOrder(orderGuid) {
      reprintCalls.push(orderGuid);
    },
  };
  let reprintPortResolutions = 0;
  const transport: HbposTransport = {
    async request<T>() {
      return {
        status: 200,
        data: { success: true, data: { orders: [] } } as T,
      };
    },
  };
  const presenter = createHbposRemoteHistoryPresenterFactory(
    transport,
    () => ({
      trustedStoreCode: "S1",
      currentDeviceCode: "IPAD-1",
      permissionCodes: [
        "Permissions.PosTerminal.History.View",
        "Permissions.PosTerminal.History.Reprint",
      ],
    }),
    () => {
      reprintPortResolutions += 1;
      return reprintPort;
    },
  ).createPresenter({ online: true });

  await presenter.reprintSelected();

  assert.deepEqual(reprintCalls, []);
  assert.equal(reprintPortResolutions, 1);
  assert.equal(presenter.state.reprint.kind, "unavailable");
  presenter.destroy();
});
