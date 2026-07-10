// src/pages/Warehouse/Products/importFromDomesticCompactUi.logic.test.ts
import { readFileSync } from "node:fs";
import path from "node:path";
var modalFile = path.resolve(process.cwd(), "src/pages/Warehouse/Products/ImportFromDomesticModal.tsx");
var compactCssFile = path.resolve(process.cwd(), "src/pages/Warehouse/Products/compact.css");
var packageFile = path.resolve(process.cwd(), "package.json");
function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}
function readCssRule(source, selector) {
  const normalizedSource = source.replace(/\r\n/g, "\n");
  const escapedSelector = selector.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const match = normalizedSource.match(new RegExp(`${escapedSelector}\\s*\\{([^}]*)\\}`, "m"));
  return match?.[1] ?? "";
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
async function main() {
  const failures = [];
  const sourceFailure = await runTest("\u4ECE\u56FD\u5185\u5BFC\u5165\u5F39\u7A97\u4F7F\u7528\u5C40\u90E8\u7D27\u51D1\u8868\u683C\u5951\u7EA6", () => {
    const modalSource = readFileSync(modalFile, "utf8");
    assert(modalSource.includes("import './compact.css'"), "\u5F39\u7A97\u5E94\u5F15\u5165 Products \u5C40\u90E8 compact.css");
    assert(modalSource.includes('wrapClassName="warehouse-import-domestic-modal"'), "Modal \u5E94\u4F7F\u7528\u5C40\u90E8 wrapClassName");
    assert(modalSource.includes('className="warehouse-import-domestic-compact-table"'), "Table \u5E94\u4F7F\u7528\u5C40\u90E8\u7D27\u51D1 class");
    assert(modalSource.includes('size="small"'), "Table \u5E94\u542F\u7528 Ant Design small \u5C3A\u5BF8");
    assert(modalSource.includes("style={{ width: 260 }}"), "\u641C\u7D22\u6846\u5E94\u6536\u7A84\u5230\u7D27\u51D1\u5BBD\u5EA6");
    assert(modalSource.includes("width={36}") && modalSource.includes("height={36}"), "\u5546\u54C1\u56FE\u7247\u5E94\u538B\u7F29\u5230 36px");
    assert(modalSource.includes("columnWidth: 42"), "\u9009\u62E9\u5217\u5E94\u6536\u7A84\u4F46\u4FDD\u7559\u53EF\u70B9\u51FB\u7A7A\u95F4");
    assert(modalSource.includes("scroll={{ x: 1120, y: 560 }}"), "\u8868\u683C\u6EDA\u52A8\u5C3A\u5BF8\u5E94\u5339\u914D\u7D27\u51D1\u5217\u5BBD\u548C\u66F4\u9AD8\u53EF\u89C6\u884C\u6570");
  });
  if (sourceFailure) failures.push(sourceFailure);
  const cssFailure = await runTest("\u4ECE\u56FD\u5185\u5BFC\u5165\u7D27\u51D1\u6837\u5F0F\u53EA\u4F5C\u7528\u4E8E\u5C40\u90E8\u5F39\u7A97", () => {
    const cssSource = readFileSync(compactCssFile, "utf8");
    const cellRule = readCssRule(cssSource, ".warehouse-import-domestic-compact-table .ant-table-thead > tr > th,\n.warehouse-import-domestic-compact-table .ant-table-tbody > tr > td");
    const inputRule = readCssRule(cssSource, ".warehouse-import-domestic-compact-table .ant-input-number");
    const twoLineRule = readCssRule(cssSource, ".warehouse-import-domestic-compact-table .warehouse-import-domestic-two-line");
    const tagRule = readCssRule(cssSource, ".warehouse-import-domestic-compact-table .ant-tag");
    const paginationRule = readCssRule(cssSource, ".warehouse-import-domestic-compact-table .ant-pagination");
    assert(cssSource.includes(".warehouse-import-domestic-modal"), "\u6837\u5F0F\u5FC5\u987B\u9650\u5B9A\u5728\u5F53\u524D\u5BFC\u5165\u5F39\u7A97\u524D\u7F00\u4E0B");
    assert(!/^\s*\.warehouse-import-domestic-two-line\s*\{/m.test(cssSource), "\u4E24\u884C\u6587\u672C\u6837\u5F0F\u4E0D\u5E94\u4F7F\u7528\u88F8\u5168\u5C40\u9009\u62E9\u5668");
    assert(cellRule.includes("padding: 3px 6px !important"), "\u8868\u683C\u5355\u5143\u683C\u5E94\u4F7F\u7528\u7D27\u51D1 padding");
    assert(cellRule.includes("line-height: 1.2"), "\u8868\u683C\u884C\u9AD8\u5E94\u538B\u7F29");
    assert(inputRule.includes("width: 100%"), "\u4EF7\u683C\u8F93\u5165\u6846\u5E94\u4F7F\u7528\u5217\u5185\u81EA\u9002\u5E94\u5BBD\u5EA6");
    assert(inputRule.includes("max-width: 100%"), "\u4EF7\u683C\u8F93\u5165\u6846\u4E0D\u5E94\u8D85\u8FC7\u5217\u5185\u5BB9\u533A");
    assert(inputRule.includes("min-width: 0"), "\u4EF7\u683C\u8F93\u5165\u6846\u5E94\u5141\u8BB8\u5728\u7D27\u51D1\u5217\u5185\u6536\u7F29");
    assert(inputRule.includes("height: 24px"), "\u4EF7\u683C\u8F93\u5165\u6846\u9AD8\u5EA6\u5E94\u538B\u7F29");
    assert(twoLineRule.includes("-webkit-line-clamp: 2"), "\u5546\u54C1\u540D/\u4F9B\u5E94\u5546\u5E94\u5141\u8BB8\u4E24\u884C\u622A\u65AD");
    assert(tagRule.includes("line-height: 18px"), "\u7ED3\u6784\u6807\u7B7E\u5E94\u538B\u7F29\u9AD8\u5EA6");
    assert(paginationRule.includes("margin: 6px 0 0"), "\u5206\u9875\u533A\u5E94\u51CF\u5C11\u9876\u90E8\u95F4\u8DDD");
  });
  if (cssFailure) failures.push(cssFailure);
  const packageFailure = await runTest("\u4ECE\u56FD\u5185\u5BFC\u5165\u7D27\u51D1\u6D4B\u8BD5\u5E94\u63A5\u5165 npm test", () => {
    const packageSource = readFileSync(packageFile, "utf8");
    assert(packageSource.includes("test:warehouse-products"), "package.json \u5E94\u58F0\u660E\u4ED3\u5E93\u5546\u54C1\u6D4B\u8BD5\u811A\u672C");
    assert(packageSource.includes("importFromDomesticCompactUi.logic.test.ts"), "\u4ED3\u5E93\u5546\u54C1\u6D4B\u8BD5\u811A\u672C\u5E94\u5305\u542B\u7D27\u51D1 UI \u6D4B\u8BD5");
    assert(packageSource.includes("importFromDomesticSelection.logic.test.ts"), "\u4ED3\u5E93\u5546\u54C1\u6D4B\u8BD5\u811A\u672C\u5E94\u4FDD\u7559\u5F53\u524D\u9875\u9009\u62E9\u903B\u8F91\u6D4B\u8BD5");
    assert(packageSource.includes("npm run test:warehouse-products"), "npm test \u5E94\u63A5\u5165\u4ED3\u5E93\u5546\u54C1\u6D4B\u8BD5\u811A\u672C");
  });
  if (packageFailure) failures.push(packageFailure);
  if (failures.length > 0) {
    throw new Error(`\u5171\u6709 ${failures.length} \u4E2A\u6D4B\u8BD5\u5931\u8D25
- ${failures.join("\n- ")}`);
  }
  console.log("importFromDomesticCompactUi.logic.test: ok");
}
await main();
