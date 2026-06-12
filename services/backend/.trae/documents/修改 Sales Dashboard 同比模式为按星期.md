# 修改 Sales Dashboard 同比模式为按星期

## 需求
1. 同比模式默认改为按星期
2. 按星期模式应该是"去年相同星期几的日期"

## 修改计划

### 1. 修改 index.tsx
- 将默认 `compareMode` 从 `'by-date'` 改为 `'by-week'`（第48行）

### 2. 修改 DateRangeFilterBar.tsx
- 修改 `calculateCompareStartDate` 函数（第86-91行）
  - `by-week` 模式：计算去年同一周的相同星期几的日期
  - `by-date` 模式：保持原逻辑，直接减一年
- 修改 `calculateCompareEndDate` 函数（第93-98行）
  - 同样的修改逻辑

### 3. 验证后端 API
- 检查后端是否正确处理 `compareMode` 参数
- 如需要，确保后端按星期和按日期的逻辑正确