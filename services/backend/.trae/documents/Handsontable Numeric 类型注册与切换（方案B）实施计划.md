## 目标
- 注册 Handsontable 的 numeric 单元格类型，恢复价格列为原生数值型并格式化显示，两位小数。
- 保持免费许可：`licenseKey: 'non-commercial-and-evaluation'`。

## 修改文件
- `ReactUmi/my-app/src/pages/MultiCodeSets/index.tsx`

## 实施步骤
1) 引入并注册 numeric 类型
- 在文件顶部添加：
```ts
import Handsontable from 'handsontable';
import { NumericCellType } from 'handsontable/cellTypes/numericType';
Handsontable.cellTypes.registerCellType(NumericCellType);
```

2) 列定义切换为 numeric
- 将价格列改为：
```ts
{ data: 'setPurchasePrice', type: 'numeric', numericFormat: { pattern: '0.00' }, className: 'htRight' }
{ data: 'setRetailPrice',   type: 'numeric', numericFormat: { pattern: '0.00' }, className: 'htRight' }
```
- 移除此前针对价格列的 `type: 'text'` 与 `validator`。

3) HotColumn 保持一致
- 使用 `<HotColumn data="..." />`（不再显式声明冲突类型）；如需 `<HotColumn type="numeric" />`，确保仅在完成注册后使用。

4) 批量更新安全转换
- 在批量更新价格时，仍做 `Number(...)` 转换保证后端接收为数值：
```ts
setPurchasePrice: typeof x.setPurchasePrice === 'number' ? x.setPurchasePrice : Number(x.setPurchasePrice)
```

5) 许可确认
- 组件上保留：`licenseKey="non-commercial-and-evaluation"`。

## 验证
- 访问 `/pos-admin/multi-code-sets`：不再出现 “numeric 未映射” 报错；价格列右对齐、两位小数显示；编辑、排序/过滤/分页与批量更新均正常。