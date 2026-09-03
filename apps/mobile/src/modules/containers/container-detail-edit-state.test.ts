import assert from "node:assert/strict";
import {
  applyContainerDetailServerConflicts,
  buildContainerDetailEditForm,
  buildContainerDetailEditPayload,
  isCurrentContainerDetailEditSession,
  reconcileContainerDetailPartialSave,
} from "./container-detail-edit-state";
import type { ContainerDetail, ContainerDetailConcurrentConflict } from "./types";

const detail: ContainerDetail = {
  HGUID: "DETAIL-1",
  商品名称: "旧名称",
  英文名称: "Old name",
  国内价格: 9.9,
  进口价格: 4.1,
  warehouseOEMPrice: 8.8,
  WarehouseOEMPrice: 8.8,
  warehouseIsActive: true,
  商品信息: { 商品名称: "旧名称", 英文名称: "Old name", 零售价格: 8.8 },
  serverFieldTokens: {
    "商品名称": "name-old",
    "国内价格": "domestic-old",
    "贴牌价格": "oem-old",
    IsActive: "active-old",
    "进口价格": "import-old",
  },
};

const conflicts: ContainerDetailConcurrentConflict[] = [
  {
    hguid: "DETAIL-1",
    field: "国内价格",
    code: "CONCURRENT_FIELD_UPDATE",
    message: "服务器已更新",
    serverValue: null,
    submittedValue: 10.5,
    currentServerFieldToken: "domestic-current",
  },
  {
    hguid: "DETAIL-1",
    field: "商品名称",
    code: "CONCURRENT_FIELD_UPDATE",
    message: "服务器已更新",
    serverValue: "服务器名称",
    submittedValue: "我的名称",
    currentServerFieldToken: "name-current",
  },
  {
    hguid: "DETAIL-1",
    field: "贴牌价格",
    code: "CONCURRENT_FIELD_UPDATE",
    message: "服务器已更新",
    serverValue: 12.3,
    submittedValue: 10,
    currentServerFieldToken: "oem-current",
  },
  {
    hguid: "DETAIL-1",
    field: "IsActive",
    code: "CONCURRENT_FIELD_UPDATE",
    message: "服务器已更新",
    serverValue: false,
    submittedValue: true,
    currentServerFieldToken: "active-current",
  },
];

const form = {
  ...buildContainerDetailEditForm(detail),
  domesticPrice: "10.5",
  productName: "我的名称",
  oemPrice: "10",
  isActive: true,
  importPrice: "4.5",
};
const resolved = applyContainerDetailServerConflicts(detail, form, conflicts);

assert.equal(resolved.form.domesticPrice, "", "服务器 null 数值必须恢复为空输入，不能误写为 0");
assert.equal(resolved.detail.国内价格, undefined, "服务器 null 数值必须同步写入编辑基线");
assert.equal(resolved.form.productName, "服务器名称");
assert.equal(resolved.detail.商品名称, "服务器名称");
assert.equal(resolved.detail.商品信息?.商品名称, "服务器名称", "关联商品信息必须与编辑基线同步");
assert.equal(resolved.detail.warehouseOEMPrice, 12.3);
assert.equal(resolved.detail.商品信息?.零售价格, 12.3, "共享商品价格必须同步嵌套商品信息");
assert.equal(resolved.detail.IsActive, false);
assert.equal(resolved.detail.warehouseIsActive, false, "仓库状态基线必须与采用值同步");
assert.equal(resolved.detail.serverFieldTokens?.["国内价格"], "domestic-current");

const payload = buildContainerDetailEditPayload(resolved.detail, resolved.form);
assert.equal(payload.进口价格, 4.5, "未解决的其它本地修改必须保留");
assert.equal(payload.国内价格, undefined, "采用服务器数值后后续保存不得重提该字段");
assert.equal(payload.商品名称, undefined, "采用服务器名称后后续保存不得重提该字段");
assert.equal(payload.贴牌价格, undefined, "采用服务器价格后后续保存不得重提该字段");
assert.equal(payload.IsActive, undefined, "采用服务器状态后后续保存不得重提该字段");

const partialBaseline: ContainerDetail = {
  HGUID: "DETAIL-PARTIAL",
  国内价格: 9.9,
  进口价格: 4.1,
  serverFieldTokens: { "国内价格": "domestic-old", "进口价格": "import-old" },
};
const partialForm = {
  ...buildContainerDetailEditForm(partialBaseline),
  domesticPrice: "10.2",
  importPrice: "4.5",
};
const partialPayload = buildContainerDetailEditPayload(partialBaseline, partialForm);
const partial = reconcileContainerDetailPartialSave({
  baseline: partialBaseline,
  form: partialForm,
  submittedPayload: partialPayload,
  latest: {
    ...partialBaseline,
    国内价格: 10.2,
    serverFieldTokens: { "国内价格": "domestic-new", "进口价格": "import-old" },
  },
  validationErrors: [{
    hguid: "DETAIL-PARTIAL",
    field: "进口价格",
    code: "SET_RELATION_INVALID",
    message: "套装关系不允许该进口价",
  }],
  conflicts: [],
});
assert.equal(partial.form.importPrice, "4.5", "任意字段验证失败都必须保留原始本地输入");
assert.equal(partial.detail.国内价格, 10.2, "同批成功字段必须从服务器最新值更新编辑基线");
assert.equal(partial.detail.serverFieldTokens?.["国内价格"], "domestic-new", "同批成功字段必须使用刷新后的令牌");
const retryPayload = buildContainerDetailEditPayload(partial.detail, partial.form);
assert.equal(retryPayload.国内价格, undefined, "同批成功字段后续保存不能再次提交");
assert.equal(retryPayload.进口价格, 4.5, "失败字段必须仍可重试");
assert.equal(retryPayload.expectedServerFieldTokens?.["进口价格"], "import-old", "失败字段继续使用自己的基线令牌");

assert.equal(isCurrentContainerDetailEditSession({
  expectedSessionId: "session-a",
  expectedHguid: "DETAIL-A",
  currentSessionId: "session-a",
  currentHguid: "DETAIL-A",
}), true, "保存响应只能更新发起保存的当前编辑会话");
assert.equal(isCurrentContainerDetailEditSession({
  expectedSessionId: "session-a",
  expectedHguid: "DETAIL-A",
  currentSessionId: "session-b",
  currentHguid: "DETAIL-B",
}), false, "迟到响应不得污染后来打开的另一行编辑弹窗");

console.log("container-detail-edit-state.test.ts: ok");
