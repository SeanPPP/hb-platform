## 修复步骤

### 1. 修改 `index.tsx` 中的 `formatDateRange` 函数

**文件**: `d:\Development\cline\blazor\ReactUmi\my-app\src\pages\SalesDashboard\index.tsx`

**修改内容**:
- 为 `formatDateRange` 函数添加明确的返回类型注解
- 导入 `DateRange` 类型（从 `@/services/salesDashboard`）

**修改位置**: 第 64-70 行

```typescript
// 修改前
const formatDateRange = (range: DateRange) => ({
  startDate: range.startDate.format('YYYY-MM-DD'),
  endDate: range.endDate.format('YYYY-MM-DD'),
  compareStartDate: range.compareStartDate?.format('YYYY-MM-DD'),
  compareEndDate: range.compareEndDate?.format('YYYY-MM-DD'),
  compareMode: range.compareMode === 'by-date' ? 'ByDate' : 'ByWeek',
});

// 修改后
const formatDateRange = (range: DateRange): DateRange => ({
  startDate: range.startDate.format('YYYY-MM-DD'),
  endDate: range.endDate.format('YYYY-MM-DD'),
  compareStartDate: range.compareStartDate?.format('YYYY-MM-DD'),
  compareEndDate: range.compareEndDate?.format('YYYY-MM-DD'),
  compareMode: range.compareMode === 'by-date' ? 'ByDate' : 'ByWeek',
});
```

**注意**: 这里存在类型名称冲突，需要调整导入：
- 从 `DateRangeFilterBar` 导入的类型重命名为 `UIDateRange`
- 从 `salesDashboard` 导入的类型使用 `DateRange`（API 类型）

### 2. 更新导入语句

**修改位置**: 第 8 行

```typescript
// 修改前
import DateRangeFilterBar, { type DateRange } from './DateRangeFilterBar';

// 修改后
import DateRangeFilterBar, { type DateRange as UIDateRange } from './DateRangeFilterBar';
import { type DateRange } from '@/services/salesDashboard';
```

### 3. 更新状态类型定义

**修改位置**: 第 42 行

```typescript
// 修改前
const [dateRange, setDateRange] = useState<DateRange>({

// 修改后
const [dateRange, setDateRange] = useState<UIDateRange>({
```

### 4. 更新所有使用 DateRange 类型的函数参数

**修改位置**: 第 72, 120, 137 行的函数签名

```typescript
// 修改前
const loadDashboardData = async (targetDateRange: DateRange) => {
// 修改前
const loadSupplierStoreSales = async (
  targetDateRange: DateRange,
  supplierCode: string,
) => {
// 修改前
const loadStoreSupplierSales = async (
  targetDateRange: DateRange,
  branchCode: string,
) => {

// 修改后
const loadDashboardData = async (targetDateRange: UIDateRange) => {
// 修改后
const loadSupplierStoreSales = async (
  targetDateRange: UIDateRange,
  supplierCode: string,
) => {
// 修改后
const loadStoreSupplierSales = async (
  targetDateRange: UIDateRange,
  branchCode: string,
) => {
```

---

**预期结果**: 修复后，TypeScript 类型检查将通过，所有 CompareMode 类型错误将消失。