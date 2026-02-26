## 目标
- 显式注册 Handsontable 的 numeric 单元格类型，恢复价格列为原生数值类型，保持免费许可。

## 依赖确认
- 保持已安装：`handsontable` 与 `@handsontable/react-wrapper`
- 页面继续使用：`licenseKey: 'non-commercial-and-evaluation'`

## 实施步骤
### 1. 注册 numeric 类型（一次性）
- 在 `ReactUmi/my-app/src/pages/MultiCodeSets/index.tsx` 顶部添加：
  - `import Handsontable from 'handsontable'`
  - `import { NumericCellType } from 'handsontable/cellTypes/numericType'`
  - `Handsontable.cellTypes.registerCellType(NumericCellType)`
- 或建立 `src/handsontable/init.ts` 进行注册，并在页面首次引入该模块。

### 2. 列定义切换为 numeric
- 将价格列改为：
```ts
{ data: 'setPurchasePrice', type: 'numeric', numericFormat: { pattern: '0.00' }, className: 'htRight' }
{ data: 'setRetailPrice',   type: 'numeric', numericFormat: { pattern: '0.00' }, className: 'htRight' }
```
- 移除文本类型 validator（由 numeric 类型接管校验显示）。

### 3. HotColumn 声明
- 与 columns 保持一致：价格两列可使用 `<HotColumn data="..." />`（推荐统一用 columns 控制类型），如需则设置 `<HotColumn type="numeric" />`。
- 确保不再保留任何 `type='text'` 与价格列冲突。

### 4. 批量更新数值转换
- 仍在批量更新前做安全转换：
```ts
setPurchasePrice: typeof x.setPurchasePrice === 'number' ? x.setPurchasePrice : Number(x.setPurchasePrice)
```
- 确保后端接收数值，不受格式化影响。

### 5. 验证
- 打开 `/pos-admin/multi-code-sets`：
  - 页面不再出现 “numeric 未映射” 错误
  - 价格列右对齐、数值格式化为两位小数
  - 编辑、排序/过滤/分页正常刷新，批量更新价格成功

## 不影响项
- 后端接口与路由/菜单不变；仅改动页面实现与类型注册。