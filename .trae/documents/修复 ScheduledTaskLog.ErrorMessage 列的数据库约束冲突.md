## 修复 ScheduledTaskLog 模型的数据库约束问题

### 发现的问题

1. **ErrorMessage** - 缺少 `IsNullable = true`（导致当前错误）
2. **TaskParameters** - 缺少 `IsNullable = true`（潜在问题）
3. **StartedAt** - 类型定义为 `DateTime?` 但约束为 `NOT NULL`（逻辑不一致）

### 修改内容

**文件**: `BlazorApp.Shared/Models/HBweb/ScheduledTaskLog.cs`

```csharp
// 1. 修复 ErrorMessage (当前错误)
// 修改前
[SugarColumn(ColumnDataType = "nvarchar(max)")]
public string? ErrorMessage { get; set; }
// 修改后
[SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)]
public string? ErrorMessage { get; set; }

// 2. 修复 TaskParameters (防止潜在问题)
// 修改前
[SugarColumn(ColumnDataType = "nvarchar(max)")]
public string? TaskParameters { get; set; }
// 修改后
[SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)]
public string? TaskParameters { get; set; }

// 3. 修复 StartedAt 类型定义
// 修改前
[SugarColumn(IsNullable = false)]
public DateTime? StartedAt { get; set; }
// 修改后
[SugarColumn(IsNullable = false)]
public DateTime StartedAt { get; set; } = DateTime.UtcNow;
```

### 预期效果
- ✅ ErrorMessage 和 TaskParameters 明确允许 NULL
- ✅ StartedAt 类型从 `DateTime?` 改为 `DateTime`（非空），符合 NOT NULL 约束
- ✅ 模型定义与数据库约束一致
- ✅ 任务日志插入不再报错