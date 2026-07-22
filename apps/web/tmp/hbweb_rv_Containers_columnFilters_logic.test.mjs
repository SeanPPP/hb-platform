// src/pages/Warehouse/Containers/Containers.columnFilters.logic.test.ts
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
var serviceFile = path.resolve(process.cwd(), "src/services/containerService.ts");
var typeFile = path.resolve(process.cwd(), "src/types/container.ts");
var pageSource = readFileSync(pageFile, "utf8");
var serviceSource = readFileSync(serviceFile, "utf8");
var typeSource = readFileSync(typeFile, "utf8");
var normalizedPageSource = pageSource.replace(/\r\n/g, "\n");
async function main() {
  const failures = [];
  const stateFailure = await runTest("\u9875\u9762\u5E94\u7EF4\u62A4\u5217\u5934\u8FC7\u6EE4\u72B6\u6001\u5E76\u901A\u8FC7\u670D\u52A1\u7AEF\u67E5\u8BE2\u5E94\u7528", () => {
    assert(
      pageSource.includes("const [columnFilters, setColumnFilters] = useState<ContainerColumnFilters>({})"),
      "\u9875\u9762\u5E94\u7EF4\u62A4\u53D7\u63A7 columnFilters \u72B6\u6001"
    );
    assert(
      pageSource.includes("const activeColumnFilters = options.columnFilters ?? columnFilters") && pageSource.includes("...activeColumnFilters") && pageSource.includes("void requestFirstPage({ columnFilters: nextFilters })"),
      "\u5217\u5934\u8FC7\u6EE4\u5E94\u968F getContainerList \u8BF7\u6C42\u53D1\u9001\u5230\u670D\u52A1\u7AEF\uFF0C\u800C\u4E0D\u662F\u53EA\u8FC7\u6EE4\u5F53\u524D\u9875 dataSource"
    );
  });
  if (stateFailure) failures.push(stateFailure);
  const requestMappingFailure = await runTest("\u524D\u7AEF\u8BF7\u6C42\u7C7B\u578B\u548C\u670D\u52A1\u6620\u5C04\u5E94\u5305\u542B\u5168\u90E8\u5217\u5934\u8FC7\u6EE4\u5B57\u6BB5", () => {
    const requiredTypeFields = [
      "containerNumberFilter?: string",
      "loadingDateStart?: string",
      "estimatedArrivalDateEnd?: string",
      "actualArrivalDateEnd?: string",
      "totalPiecesMin?: number",
      "totalAmountMax?: number",
      "totalVolumeMax?: number",
      "statuses?: number[]"
    ];
    requiredTypeFields.forEach((field) => assert(typeSource.includes(field), `ContainerQueryRequest \u7F3A\u5C11 ${field}`));
    const requiredRequestFields = [
      "ContainerNumberFilter: query.containerNumberFilter",
      "LoadingDateStart: query.loadingDateStart",
      "EstimatedArrivalDateEnd: query.estimatedArrivalDateEnd",
      "ActualArrivalDateEnd: query.actualArrivalDateEnd",
      "TotalPiecesMin: query.totalPiecesMin",
      "TotalAmountMax: query.totalAmountMax",
      "TotalVolumeMax: query.totalVolumeMax",
      "Statuses: query.statuses"
    ];
    requiredRequestFields.forEach((field) => assert(serviceSource.includes(field), `getContainerList \u8BF7\u6C42\u4F53\u7F3A\u5C11 ${field}`));
  });
  if (requestMappingFailure) failures.push(requestMappingFailure);
  const columnFailure = await runTest("\u5168\u90E8\u4E1A\u52A1\u5217\u5E94\u914D\u7F6E\u5217\u5934\u8FC7\u6EE4\u63A7\u4EF6", () => {
    const expectedMarkers = [
      "...textFilterProps('containerNumberFilter'",
      "...dateRangeFilterProps('loadingDateStart', 'loadingDateEnd')",
      "...dateRangeFilterProps('estimatedArrivalDateStart', 'estimatedArrivalDateEnd')",
      "...dateRangeFilterProps('actualArrivalDateStart', 'actualArrivalDateEnd')",
      "...numberRangeFilterProps('totalPiecesMin', 'totalPiecesMax')",
      "...numberRangeFilterProps('totalAmountMin', 'totalAmountMax')",
      "...numberRangeFilterProps('totalVolumeMin', 'totalVolumeMax')",
      "filterDropdown: makeStatusFilterDropdown",
      "filtered: Boolean(columnFilters.statuses?.length)"
    ];
    expectedMarkers.forEach((marker) => assert(pageSource.includes(marker), `\u4E1A\u52A1\u5217\u7F3A\u5C11\u8FC7\u6EE4\u914D\u7F6E\uFF1A${marker}`));
  });
  if (columnFailure) failures.push(columnFailure);
  const remarkColumnFailure = await runTest("\u8D27\u67DC\u5217\u8868\u5E94\u663E\u793A\u5907\u6CE8\u5217", () => {
    const columnsStart = normalizedPageSource.indexOf("const columns: ColumnsType<ContainerMain> = [");
    const columnsEnd = normalizedPageSource.indexOf("]\n\n  return", columnsStart);
    assert(columnsStart >= 0 && columnsEnd > columnsStart, "\u65E0\u6CD5\u5B9A\u4F4D\u8D27\u67DC\u5217\u8868 columns \u5B9A\u4E49");
    const columnsSource = normalizedPageSource.slice(columnsStart, columnsEnd);
    assert(columnsSource.includes("title: t('containers.fields.remark')"), "\u8D27\u67DC\u5217\u8868\u5217\u5B9A\u4E49\u7F3A\u5C11\u5907\u6CE8\u6807\u9898");
    assert(columnsSource.includes("dataIndex: '\u5907\u6CE8'"), "\u8D27\u67DC\u5217\u8868\u5217\u5B9A\u4E49\u7F3A\u5C11\u5907\u6CE8\u5B57\u6BB5");
  });
  if (remarkColumnFailure) failures.push(remarkColumnFailure);
  const resetFailure = await runTest("\u9876\u90E8\u91CD\u7F6E\u5E94\u540C\u6B65\u6E05\u7A7A\u5217\u5934\u8FC7\u6EE4", () => {
    assert(
      pageSource.includes("setColumnFilters({})") && pageSource.includes("columnFilters: {}") && pageSource.includes("\u9876\u90E8\u91CD\u7F6E\u540C\u65F6\u6E05\u7A7A\u5217\u5934\u8FC7\u6EE4"),
      "\u9876\u90E8\u91CD\u7F6E\u5E94\u6E05\u7A7A\u5217\u5934\u72B6\u6001\uFF0C\u5E76\u7528\u7A7A columnFilters \u7ACB\u5373\u5237\u65B0\u670D\u52A1\u7AEF\u5217\u8868"
    );
  });
  if (resetFailure) failures.push(resetFailure);
  if (failures.length > 0) {
    throw new Error(`\u5171\u6709 ${failures.length} \u4E2A\u6D4B\u8BD5\u5931\u8D25
- ${failures.join("\n- ")}`);
  }
  console.log("Containers.columnFilters.logic.test: ok");
}
await main();
