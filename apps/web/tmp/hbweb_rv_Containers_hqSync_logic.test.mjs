// src/pages/Warehouse/Containers/Containers.hqSync.logic.test.ts
import { readFileSync } from "node:fs";
import path from "node:path";
function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}
function assertEqual(actual, expected, message) {
  if (actual !== expected) {
    throw new Error(`${message}\u3002Expected: ${String(expected)}, received: ${String(actual)}`);
  }
}
async function runTest(name, execute) {
  try {
    await execute();
    console.log(`ok - ${name}`);
    return null;
  } catch (error) {
    const reason = error instanceof Error ? error.message : String(error);
    console.error(`not ok - ${name}`);
    console.error(reason);
    return `${name}: ${reason}`;
  }
}
var pageFile = path.resolve(process.cwd(), "src/pages/Warehouse/Containers/index.tsx");
var pageSource = readFileSync(pageFile, "utf8");
async function main() {
  const failures = [];
  const successRefreshFailure = await runTest("\u540C\u6B65\u6210\u529F\u540E\u624D\u63D0\u793A\u6210\u529F\u5E76\u5237\u65B0\u7B2C\u4E00\u9875", () => {
    assert(
      pageSource.includes("if (success) {") && pageSource.includes("message.success(msg)") && pageSource.includes("await loadData(1, pageSize)"),
      "\u9875\u9762\u5E94\u663E\u5F0F\u533A\u5206\u6210\u529F\u5206\u652F\uFF0C\u5E76\u5728\u6210\u529F\u540E\u63D0\u793A\u6210\u529F\u4E14\u5237\u65B0\u7B2C\u4E00\u9875"
    );
    assert(
      pageSource.includes("const success = result.isSuccess ?? result.IsSuccess ?? true"),
      "\u9875\u9762\u5E94\u57FA\u4E8E\u540C\u6B65\u7ED3\u679C\u4E2D\u7684 success \u5B57\u6BB5\u5224\u65AD\u662F\u5426\u6210\u529F"
    );
  });
  if (successRefreshFailure) failures.push(successRefreshFailure);
  const errorHandlingFailure = await runTest("\u540C\u6B65\u5931\u8D25\u65F6\u53EA\u5C55\u793A error.message \u4E14\u4E0D\u5237\u65B0", () => {
    assert(
      pageSource.includes("const errorMessage = error instanceof Error ? error.message : t('containers.messages.syncFailed')") && pageSource.includes("message.error(errorMessage)"),
      "\u9875\u9762\u5931\u8D25\u5206\u652F\u5E94\u4F18\u5148\u5C55\u793A error.message\uFF0C\u5E76\u4E3A\u975E Error \u5F02\u5E38\u4FDD\u7559\u515C\u5E95\u6587\u6848"
    );
    const loadDataCount = pageSource.split("await loadData(1, pageSize)").length - 1;
    assertEqual(loadDataCount, 2, "\u9875\u9762\u6E90\u7801\u4E2D\u5237\u65B0\u7B2C\u4E00\u9875\u7684\u8C03\u7528\u6B21\u6570\u5E94\u4FDD\u6301\u4E3A\u521B\u5EFA\u6210\u529F\u4E00\u6B21\u3001\u540C\u6B65\u6210\u529F\u4E00\u6B21");
  });
  if (errorHandlingFailure) failures.push(errorHandlingFailure);
  const loadingGuardFailure = await runTest("\u540C\u6B65\u6309\u94AE\u5E94\u4FDD\u7559 loading \u4E0E disabled \u884C\u4E3A", () => {
    assert(
      pageSource.includes("loading={syncing}") && pageSource.includes("disabled={pushing}"),
      "\u540C\u6B65\u6309\u94AE\u5E94\u7EE7\u7EED\u4FDD\u7559 loading \u548C disabled \u63A7\u5236"
    );
  });
  if (loadingGuardFailure) failures.push(loadingGuardFailure);
  const inlineStatusFailure = await runTest("\u72B6\u6001\u5217\u5E94\u652F\u6301\u884C\u5185\u56DB\u6001\u4E0B\u62C9\u66F4\u65B0", () => {
    assert(
      pageSource.includes("handleContainerStatusChange") && pageSource.includes("updateContainer(record.hguid") && pageSource.includes("{ \u72B6\u6001: nextStatus }"),
      "\u9875\u9762\u5E94\u901A\u8FC7\u884C\u5185 handler \u8C03\u7528 updateContainer \u66F4\u65B0\u5F53\u524D\u8D27\u67DC\u72B6\u6001"
    );
    assert(
      pageSource.includes("containerStatusOptions") && pageSource.includes("statusUpdatingKeys") && pageSource.includes("onChange={(nextStatus) => void handleContainerStatusChange(record, nextStatus)}"),
      "\u72B6\u6001\u5217\u5E94\u4F7F\u7528\u56DB\u6001\u4E0B\u62C9\uFF0C\u5E76\u5728\u884C\u7EA7\u66F4\u65B0\u4E2D\u7981\u7528\u5F53\u524D\u72B6\u6001\u63A7\u4EF6"
    );
    assert(
      pageSource.includes("CONTAINER_STATUS_SELECT_WIDTH") && pageSource.includes("style={{ width: CONTAINER_STATUS_SELECT_WIDTH }}") && pageSource.includes("popupMatchSelectWidth={CONTAINER_STATUS_SELECT_WIDTH}"),
      "\u72B6\u6001\u5217\u4E0B\u62C9\u9009\u62E9\u6846\u548C\u5F39\u51FA\u5C42\u5E94\u4F7F\u7528\u540C\u4E00\u5BBD\u5EA6\uFF0C\u907F\u514D\u63A7\u4EF6\u5927\u5C0F\u4E0D\u5339\u914D"
    );
    assert(
      pageSource.includes("if (record.\u72B6\u6001 === nextStatus || statusUpdatingKeys.includes(recordKey))") && pageSource.includes("setContainers((items) => items.map((item) => (itemKeyOf(item) === recordKey ? { ...item, \u72B6\u6001: previousStatus } : item)))"),
      "\u72B6\u6001\u66F4\u65B0\u5E94\u8DF3\u8FC7\u76F8\u540C\u72B6\u6001\u548C\u5FD9\u788C\u884C\uFF0C\u5E76\u5728\u5931\u8D25\u65F6\u56DE\u6EDA\u539F\u72B6\u6001"
    );
  });
  if (inlineStatusFailure) failures.push(inlineStatusFailure);
  const weekDateColorFailure = await runTest("\u4E09\u5217\u65E5\u671F\u5E94\u6309\u540C\u5E74\u540C ISO \u5468\u4F7F\u7528\u4E00\u81F4\u989C\u8272", () => {
    assert(
      pageSource.includes("import isoWeek from 'dayjs/plugin/isoWeek'") && pageSource.includes("dayjs.extend(isoWeek)"),
      "\u9875\u9762\u5E94\u542F\u7528 dayjs isoWeek \u63D2\u4EF6\uFF0C\u6309 ISO \u5468\u8BA1\u7B97\u540C\u5E74\u540C\u5468"
    );
    assert(
      pageSource.includes("containerDateWeekColors") && pageSource.includes("getContainerDateWeekKey") && pageSource.includes("renderContainerWeekDate"),
      "\u9875\u9762\u5E94\u63D0\u4F9B\u65E5\u671F\u5468 key\u3001\u7A33\u5B9A\u8C03\u8272\u677F\u548C\u5468\u65E5\u671F\u6E32\u67D3 helper"
    );
    assert(
      pageSource.includes("return `${date.isoWeekYear()}-W${String(date.isoWeek()).padStart(2, '0')}`"),
      "\u65E5\u671F\u5468 key \u5E94\u540C\u65F6\u5305\u542B ISO week-year \u548C\u4E24\u4F4D week\uFF0C\u907F\u514D\u8DE8\u5E74\u540C\u5468\u6DF7\u8272"
    );
    const weekDateRenderCount = pageSource.split("render: renderContainerWeekDate").length - 1;
    assertEqual(weekDateRenderCount, 3, "\u88C5\u67DC\u65E5\u671F\u3001\u9884\u8BA1\u5230\u5CB8\u65E5\u671F\u3001\u5B9E\u9645\u5230\u8D27\u65E5\u671F\u4E09\u5217\u90FD\u5E94\u4F7F\u7528\u540C\u5468\u914D\u8272\u6E32\u67D3");
    assert(
      pageSource.includes("if (!value) return '--'") && pageSource.includes("if (!weekKey) return formatDate(value)"),
      "\u7A7A\u65E5\u671F\u5E94\u7EE7\u7EED\u663E\u793A --\uFF0C\u65E0\u6548\u65E5\u671F\u5E94\u4FDD\u7559\u666E\u901A\u65E5\u671F\u683C\u5F0F\u515C\u5E95"
    );
  });
  if (weekDateColorFailure) failures.push(weekDateColorFailure);
  if (failures.length > 0) {
    throw new Error(`\u5171\u6709 ${failures.length} \u4E2A\u6D4B\u8BD5\u5931\u8D25
- ${failures.join("\n- ")}`);
  }
  console.log("Containers.hqSync.logic.test: ok");
}
await main();
