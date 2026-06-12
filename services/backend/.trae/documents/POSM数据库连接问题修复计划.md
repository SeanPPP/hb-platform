# POSM 数据库连接问题修复计划

## 问题现象

```
SqlSugar.SqlSugarException: 无法打开登录所请求的数据库 "POSM"。登录失败。
用户 'sa' 登录失败。
```

**发生位置**：`SalesStatisticsJobService.UpdateDailyStatistics` (第 234 行)

## 问题分析

### 用户确认
✅ 使用 **DBeaver 连接成功**，说明：
- 服务器 `hotbargain.top` 可访问
- sa 用户密码正确
- POSM 数据库存在
- 网络连接正常

⚠️ **用户提示**：sa 账户可能有权限问题，只能单独登录

### 根本原因
问题出在 **sa 用户对 POSM 数据库没有访问权限**。

### 当前连接字符串
```json
"HBPOSMConnection": "Server=hotbargain.top;Database=POSM;User Id=REDACTED;Password=REDACTED;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=True"
```

### 可能的原因（按可能性排序）

1. **SqlSugar 连接池/超时问题**
   - SqlSugar 可能有默认的连接超时设置
   - 建议在连接字符串中添加 `Connection Timeout=30` 参数

2. **连接字符串格式不兼容**
   - SqlSugar 对某些 ADO.NET 参数的处理可能不同
   - 建议使用 SqlSugar 推荐的格式

3. **应用程序运行环境的差异**
   - DBeaver 使用 JDBC 驱动
   - 应用程序使用 SqlSugar (ADO.NET)，可能有不同的连接行为

## 修复步骤

### 第 1 步：添加连接超时和重试参数
修改 `appsettings.Development.json` 和 `build_temp/appsettings.json` 中的连接字符串：

```json
"HBPOSMConnection": "Server=hotbargain.top;Database=POSM;User Id=REDACTED;Password=REDACTED;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=True;Connection Timeout=60;Connect Timeout=60;Max Pool Size=100"
```

### 第 2 步：在 POSMSqlSugarContext 中添加错误处理（可选）
在 [POSMSqlSugarContext.cs](file:///D:/DevRepos/HBweb/Backend/BlazorApp.Api/Data/POSMSqlSugarContext.cs) 中添加连接重试逻辑：

```csharp
_db = new SqlSugarClient(
    new ConnectionConfig()
    {
        ConnectionString = connectionString,
        DbType = DbType.SqlServer,
        IsAutoCloseConnection = true,
        InitKeyType = InitKeyType.Attribute,
        MoreSettings = new ConnMoreSettings()
        {
            IsAutoRemoveDataCache = true,
            IsWithNoLockQuery = true,
            // 添加重试配置
        },
        // 添加超时配置
        CommandTimeout = 1800,
    }
);
```

### 第 3 步：验证修复
1. 重新运行定时任务
2. 观察日志中的 SQL 执行情况

## 不需要修改的代码
- ❌ `SalesStatisticsJobService.cs` - 代码本身没有问题
- ❌ `POSMSqlSugarContext.cs` - 基础配置正确

## 待确认信息
如果上述修复无效，需要确认：
1. 应用程序是从哪个配置文件读取 `HBPOSMConnection`？
   - 开发环境：`appsettings.Development.json`
   - 生产环境：`build_temp/appsettings.json`
   - Docker：通过 `CONNECTION_STRING_POSM` 环境变量覆盖

2. 应用程序部署在哪里？
   - 本地开发环境？
   - Docker 容器？
   - 云服务器？
