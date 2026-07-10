// src/pages/Warehouse/StoreOrders/storeOrderFilter.logic.test.ts
import { readFileSync } from "node:fs";
import path from "node:path";
function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}
var pageFile = path.resolve(process.cwd(), "src/pages/Warehouse/StoreOrders/index.tsx");
var source = readFileSync(pageFile, "utf8");
var storePickerStart = source.indexOf("function StorePickerModal");
var storePickerEnd = source.indexOf("function CopyOrderModal");
var storePickerSource = source.slice(storePickerStart, storePickerEnd);
var createStoreHandlerStart = storePickerSource.indexOf("const handleCreateStore = async () => {");
var createStoreCatchStart = storePickerSource.indexOf("} catch (error) {", createStoreHandlerStart);
var createStoreFinallyStart = storePickerSource.indexOf("} finally {", createStoreCatchStart);
var createStoreCatchSource = storePickerSource.slice(createStoreCatchStart, createStoreFinallyStart);
assert(
  source.includes("const storeFilterOptions = useMemo("),
  "\u5206\u5E97\u7B5B\u9009\u9009\u9879\u5E94\u5148\u6784\u5EFA\u7A33\u5B9A\u6392\u5E8F\u7ED3\u679C"
);
assert(
  source.includes("localeCompare(right.name || '', 'zh-Hans-CN', { numeric: true, sensitivity: 'base' })"),
  "\u5206\u5E97\u7B5B\u9009\u9009\u9879\u5E94\u6309\u5206\u5E97\u540D\u79F0\u4F18\u5148\u6392\u5E8F"
);
assert(
  source.includes("label: `${item.code} - ${item.name}`"),
  "\u5206\u5E97\u7B5B\u9009 label \u5E94\u4FDD\u7559 code - name \u4EE5\u652F\u6301\u4EE3\u7801\u548C\u540D\u79F0\u641C\u7D22"
);
assert(source.includes("showSearch"), "\u5206\u5E97\u7B5B\u9009 Select \u5E94\u652F\u6301\u8F93\u5165\u641C\u7D22");
assert(
  source.includes('optionFilterProp="label"'),
  "\u5206\u5E97\u7B5B\u9009 Select \u5E94\u57FA\u4E8E label \u8FC7\u6EE4"
);
assert(
  source.includes("filterOption={filterStoreOption}"),
  "\u5206\u5E97\u7B5B\u9009 Select \u5E94\u4F7F\u7528\u5173\u952E\u5B57\u8FC7\u6EE4\u51FD\u6570"
);
assert(storePickerStart >= 0 && storePickerEnd > storePickerStart, "\u5E94\u80FD\u5B9A\u4F4D\u521B\u5EFA\u8BA2\u5355\u5206\u5E97\u9009\u62E9\u5F39\u7A97");
assert(
  storePickerSource.includes("cashRegisterFilter === 'all' ? {} : { isActive: cashRegisterFilter === 'enabled' }"),
  "\u521B\u5EFA\u8BA2\u5355\u5206\u5E97\u5F39\u7A97\u5E94\u6309\u6536\u94F6\u7CFB\u7EDF\u72B6\u6001\u8FC7\u6EE4\uFF0C\u9ED8\u8BA4\u4E0D\u56FA\u5B9A\u53EA\u67E5\u542F\u7528\u5206\u5E97"
);
assert(
  !storePickerSource.includes("isActive: true"),
  "\u521B\u5EFA\u8BA2\u5355\u5206\u5E97\u5F39\u7A97\u9ED8\u8BA4\u5E94\u663E\u793A\u6240\u6709\u5206\u5E97\uFF0C\u4E0D\u80FD\u56FA\u5B9A isActive: true"
);
assert(
  storePickerSource.includes("title: t('common.index')") && storePickerSource.includes("render: (_value, _record, index) => index + 1"),
  "\u521B\u5EFA\u8BA2\u5355\u5206\u5E97\u8868\u683C\u5E94\u663E\u793A\u5F53\u524D\u5217\u8868\u884C\u53F7"
);
assert(
  storePickerSource.includes("t('storeOrders.storeCashRegisterAll')") && storePickerSource.includes("t('storeOrders.storeCashRegisterEnabled')") && storePickerSource.includes("t('storeOrders.storeCashRegisterDisabled')"),
  "\u521B\u5EFA\u8BA2\u5355\u5206\u5E97\u5F39\u7A97\u5E94\u63D0\u4F9B\u5168\u90E8/\u542F\u7528/\u672A\u542F\u7528\u6536\u94F6\u7CFB\u7EDF\u8FC7\u6EE4\u6309\u94AE"
);
assert(
  source.includes("import { createStore, getNextStoreCode, getStores } from '../../../services/storeService'") && storePickerSource.includes("initialValues={{ isActive: false }}") && storePickerSource.includes("createStore({ ...values, isActive: values.isActive ?? false })"),
  "\u521B\u5EFA\u8BA2\u5355\u5F39\u7A97\u5E94\u652F\u6301\u65B0\u5EFA\u5206\u5E97\u4E14\u9ED8\u8BA4\u4E0D\u542F\u7528\u6536\u94F6\u7CFB\u7EDF"
);
assert(
  storePickerSource.includes("const nextCode = await getNextStoreCode()") && storePickerSource.includes("createForm.setFieldsValue({ storeCode: nextCode })") && storePickerSource.includes("void loadNextStoreCode()"),
  "\u6253\u5F00\u521B\u5EFA\u8BA2\u5355\u5206\u5E97\u8868\u5355\u65F6\u5E94\u4ECE\u540E\u7AEF\u83B7\u53D6\u5EFA\u8BAE\u5206\u5E97\u7F16\u7801\u5E76\u586B\u5165 storeCode"
);
assert(
  storePickerSource.includes("t('storeOrders.regenerateStoreCode')") && storePickerSource.includes("loading={storeCodeLoading}"),
  "\u521B\u5EFA\u8BA2\u5355\u5206\u5E97\u8868\u5355\u5E94\u63D0\u4F9B\u91CD\u65B0\u751F\u6210\u5206\u5E97\u7F16\u7801\u5165\u53E3"
);
assert(
  storePickerSource.includes("getApiErrorCode(error) === 'DUPLICATE_STORE_CODE'") && storePickerSource.includes("t('storeOrders.duplicateStoreCode')") && createStoreCatchStart > createStoreHandlerStart && !createStoreCatchSource.includes("setCreateOpen(false)"),
  "\u521B\u5EFA\u5206\u5E97\u7F16\u7801\u91CD\u590D\u65F6\u5E94\u4FDD\u7559\u8868\u5355\u5E76\u5C55\u793A\u4E13\u7528\u9519\u8BEF\u63D0\u793A"
);
assert(
  storePickerSource.includes("onSelect(created)"),
  "\u65B0\u5EFA\u5206\u5E97\u6210\u529F\u540E\u5E94\u7EE7\u7EED\u9009\u4E2D\u8BE5\u5206\u5E97\u5E76\u521B\u5EFA\u8BA2\u5355"
);
console.log("storeOrderFilter.logic.test: ok");
