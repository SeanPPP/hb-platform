## 问题定位
- 错误: "You declared cell type 'numeric' as a string that is not mapped to a known object"
- 原因: 当前 Handsontable 版本未注册数值单元格类型或使用的 React 包版本与核心包不匹配（@handsontable/react 已被迁移到 @handsontable/react-wrapper）。

## 解决方案
### 1. 依赖与包使用
- 将 React 包切换为官方推荐的 `@handsontable/react-wrapper`，保持 `handsontable` 主包同版本。
- 保留 `licenseKey: 'non-commercial-and-evaluation'`。

### 2. 注册数值类型
- 在页面入口或全局初始化处注册 `numeric` 单元格类型：
```ts
import Handsontable from 'handsontable';
import { NumericCellType } from 'handsontable/cellTypes/numericType';
Handsontable.cellTypes.registerCellType(NumericCellType);
```
- 或改为对象引用: `columns: [{ data: 'setPurchasePrice', type: Handsontable.cellTypes.getCellType('numeric') }]`（注册后可用）。

### 3. 列定义修正
- 保持 columns:
```ts
columns: [
  { data: 'supplierName' },
  { data: 'itemNumber' },
  { data: 'barcode' },
  { data: 'setItemNumber' },
  { data: 'setBarcode' },
  { data: 'setPurchasePrice', type: 'numeric', numericFormat: { pattern: '0.00' } },
  { data: 'setRetailPrice', type: 'numeric', numericFormat: { pattern: '0.00' } },
  { data: 'updatedAt' },
  { data: 'updatedBy' },
]
```
- 若注册不成功，临时降级为文本类型，并在批量更新时进行 `Number()` 转换以保证后端正确性。

### 4. 验证与兼容
- 启动开发服务，确认页面加载无错误，数值列可编辑与格式化。
- 验证排序/过滤/分页仍正常触发后端刷新，批量操作使用选择的行集合。

### 5. 可选增强
- 为价格列添加非负数校验（afterChange 验证 + message 提示）。
- 日期列增加格式化渲染器（例如 YYYY-MM-DD）。

## 实施与测试
- 更新依赖、注册 numeric 类型、调整列定义并运行本地服务进行全量验证；如遇版本不兼容，回滚到 Handsontable 官方建议的匹配版本对（react-wrapper + core）。