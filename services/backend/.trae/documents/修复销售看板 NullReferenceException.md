## 修复销售看板 NullReferenceException 问题

### 问题根源
1. `Program.cs` 中 `dbContext.CreateTable()` 被注释掉，导致销售统计表未创建
2. 当查询不存在的表时，SqlSugar 内部 ConnectionConfig 变为 null，抛出异常

### 修复步骤

**步骤 1: 取消注释 CreateTable 调用**
- 在 `Program.cs` 第 391 行，取消注释 `dbContext.CreateTable();`
- 确保启动时创建销售统计表

**步骤 2: 添加更详细的错误日志**
- 在 `SqlSugarContext.cs` 构造函数中添加连接字符串验证日志
- 记录数据库连接状态

**步骤 3: 验证连接字符串**
- 检查 `appsettings.json` 中的 `DefaultConnection` 配置
- 确认 SQL Server 正在运行且可访问

### 修改的文件
1. `BlazorApp.Api/Program.cs` - 取消注释数据库初始化
2. `BlazorApp.Api/Data/SqlSugarContext.cs` - 添加连接验证日志

### 预期结果
- 销售统计表将在应用启动时自动创建
- `SalesDashboardReactService` 查询不再抛出 NullReferenceException
- 控制台显示详细的数据库初始化日志