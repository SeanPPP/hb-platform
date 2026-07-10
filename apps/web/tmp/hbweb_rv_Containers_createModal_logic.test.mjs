// src/pages/Warehouse/Containers/Containers.createModal.logic.test.ts
import { readFileSync } from "node:fs";
import path from "node:path";
function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
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
  const defaultExchangeRateFailure = await runTest("\u65B0\u5EFA\u8D27\u67DC\u9ED8\u8BA4\u6C47\u7387\u5E94\u4E3A 4.7", () => {
    assert(
      pageSource.includes("initialValues={{ \u6C47\u7387: 4.7 }}"),
      "\u65B0\u5EFA\u8D27\u67DC\u8868\u5355\u9ED8\u8BA4\u6C47\u7387\u5E94\u8BBE\u7F6E\u4E3A 4.7"
    );
  });
  if (defaultExchangeRateFailure) failures.push(defaultExchangeRateFailure);
  const estimatedArrivalFailure = await runTest("\u88C5\u67DC\u65E5\u671F\u5E94\u81EA\u52A8\u5E26\u51FA\u56DB\u5468\u540E\u7684\u5DE5\u4F5C\u65E5\u9884\u8BA1\u5230\u5CB8\u65E5\u671F", () => {
    assert(
      pageSource.includes("getEstimatedArrivalDate") && pageSource.includes("loadingDate.add(4, 'week')"),
      "\u9875\u9762\u5E94\u6709\u6309\u88C5\u67DC\u65E5\u671F\u52A0\u56DB\u5468\u8BA1\u7B97\u9884\u8BA1\u5230\u5CB8\u65E5\u671F\u7684 helper"
    );
    assert(
      pageSource.includes("if (estimatedArrival.day() === 6)") && pageSource.includes("estimatedArrival = estimatedArrival.add(2, 'day')") && pageSource.includes("if (estimatedArrival.day() === 0)") && pageSource.includes("estimatedArrival = estimatedArrival.add(1, 'day')"),
      "\u9884\u8BA1\u5230\u5CB8\u65E5\u671F\u843D\u5728\u5468\u516D\u5E94\u987A\u5EF6\u5230\u5468\u4E00\uFF0C\u843D\u5728\u5468\u65E5\u5E94\u987A\u5EF6\u5230\u5468\u4E00"
    );
    assert(
      pageSource.includes("handleLoadingDateChange") && pageSource.includes("form.setFieldsValue({ \u9884\u8BA1\u5230\u5CB8\u65E5\u671F: getEstimatedArrivalDate(value) })") && pageSource.includes("onChange={handleLoadingDateChange}"),
      "\u88C5\u67DC\u65E5\u671F DatePicker \u6539\u52A8\u65F6\u5E94\u81EA\u52A8\u5199\u5165\u9884\u8BA1\u5230\u5CB8\u65E5\u671F"
    );
  });
  if (estimatedArrivalFailure) failures.push(estimatedArrivalFailure);
  if (failures.length > 0) {
    throw new Error(`\u5171\u6709 ${failures.length} \u4E2A\u6D4B\u8BD5\u5931\u8D25
- ${failures.join("\n- ")}`);
  }
  console.log("Containers.createModal.logic.test: ok");
}
await main();
