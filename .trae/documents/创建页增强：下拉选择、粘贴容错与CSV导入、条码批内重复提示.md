## 目标
- 分店/供应商选择器改为下拉选择，加载选项并支持搜索。
- 明细粘贴容错增强：自动识别中间空列、灵活列数；新增CSV文件导入。
- 条码检测增加“批内重复数”单独列，提示本次导入内的重复情况。

## 改动文件
- ReactUmi/my-app/src/pages/PosAdmin/LocalSupplierInvoiceCreate/index.tsx

## 具体实现
- 下拉选择：
  - 引入 `getActiveStores` 与 `getActiveLocalSuppliers`，在 `useEffect` 中加载为 `Select` 的 `options`。
  - 将 `storeCode`、`supplierCode` 输入框替换为 `Select`，启用 `showSearch` 与 `filterOption`。
- 粘贴容错：
  - 改进 `parsePasted`：
    - 支持多分隔符：制表符、逗号；
    - 允许行中间空列（连续分隔符），按索引映射到【货号｜名称/条码｜数量｜价格】，空列自动跳过但保留列位；
    - 数量强制整数、价格>0，否则标记为错误并保留行用于提示。
- CSV导入：
  - 添加文件选择按钮（`<input type="file" accept=".csv">`）；
  - 使用轻量CSV解析函数（支持引号与逗号转义），读取为二维数组后复用 `parsePasted` 的映射逻辑。
- 批内重复数：
  - 在表格新增列“批内重复数”，基于当前 `items` 的 `nameOrBarcode`（作为条码）统计重复次数；
  - 显示数值与提示 Tag（>2 高亮）。
- UI与交互：
  - 在粘贴卡片内新增“导入CSV”按钮，解析后填充 `items`；
  - 结果列保持分离：`供应商+货号检测结果` 与 `条码匹配结果`；新增“批内重复数”。

## 验证
- 下拉选择正常加载并能搜索选中。
- 粘贴包含空列或不规则列时仍能解析为行；错误行提示。
- CSV 文件导入可解析并显示到明细表。
- 条码检测结果列与“批内重复数”列正确显示；重复数>2时高亮提示。

## 影响范围
- 仅前端创建页，复用现有服务与API；不改动后端接口。