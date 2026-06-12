# 更新前端 HBSalesRecord 2025年导入功能说明

## 背景

后端 `HBSalesRecordStatisticsService` 已更新为同时从 HBSales 和 POSM 数据库读取数据并合并生成统计，前端说明文字需要更新以反映这一变化。

## 实施步骤

### 更新 `d:\Development\cline\blazor\ReactUmi\my-app\src\pages\StatisticsJobTrigger\index.tsx`

修改第 582-584 行的说明信息，更新为：

```typescript
<Tag color="purple">HBSalesRecord 2025年导入（10天/10并发）</Tag>{' '}
从HBSalesRecord和POSM数据库同时导入2025年全年销售数据到中间统计表，按10天分块，最大10并发，自动关联POSM供应商映射，合并新老数据库数据
```

### 更新任务选项标签（可选）

修改第 370 行的任务选项标签，更新为：

```typescript
{ value: 'hb-sales-record-2025', label: 'HBSalesRecord 2025年导入（10天/10并发，合并HBSales+POSM）' }
```

## 修改文件

- `d:\Development\cline\blazor\ReactUmi\my-app\src\pages\StatisticsJobTrigger\index.tsx`

## 预期效果

- 用户在界面上可以看到更新后的说明，清楚了解该功能会同时从 HBSales 和 POSM 数据源读取数据

- 说明文字反映了最新的功能实现（数据合并）

