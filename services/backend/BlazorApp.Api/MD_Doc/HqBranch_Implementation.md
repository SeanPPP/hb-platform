# HQ分店管理系统实现说明

## 概述

本文档描述了HQ分店管理系统的实现，该系统连接到独立的HQ数据库（HOT_HQ_CLOUD），用于管理分店信息。

## 数据库连接

### 连接字符串配置

在 `appsettings.json` 和 `appsettings.Development.json` 中添加了新的连接字符串：

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=hotbargain.top;Database=HBweb;User Id=REDACTED;Password=REDACTED;TrustServerCertificate=true;",
    "StoreHzgHQConnection": "Server=hotbargain.store;Database=HOT_HQ_CLOUD;User ID=sa;Password=REDACTED;TrustServerCertificate=True;MultipleActiveResultSets=True"
  }
}
```

## 数据模型

### HqBranch 实体

```csharp
public class HqBranch : BaseEntity
{
    public string BranchCode { get; set; }        // H分店代码
    public string BranchName { get; set; }        // H分店名称
    public string? BusinessNumber { get; set; }   // H商业编号
    public string? Phone { get; set; }            // H电话
    public string? ManagerName { get; set; }      // H店经理
    public string? ManagerPhone { get; set; }     // H店经理电话
    public string? Address { get; set; }          // H分店地址
    public bool IsActive { get; set; }            // 是否激活
    public string? Remarks { get; set; }          // 备注
}
```

### 表结构

数据表名称：`DIC_分店信息表`

字段说明：
- `Id`: 主键，自增
- `BranchCode`: 分店代码（唯一索引）
- `BranchName`: 分店名称
- `BusinessNumber`: 商业编号
- `Phone`: 分店电话
- `ManagerName`: 店经理姓名
- `ManagerPhone`: 店经理电话
- `Address`: 分店地址
- `IsActive`: 是否激活
- `Remarks`: 备注
- `CreatedAt`: 创建时间
- `UpdatedAt`: 更新时间
- `CreatedBy`: 创建者
- `UpdatedBy`: 更新者
- `IsDeleted`: 软删除标记

## 架构组件

### 1. 数据访问层

#### HqSqlSugarContext
- 专门用于HQ数据库的SqlSugar上下文
- 独立的连接配置
- 只连接现有数据库，不创建或修改表结构

### 2. 服务层

#### IHqBranchService / HqBranchService
提供以下功能：
- 获取所有分店
- 根据ID获取分店
- 根据分店代码获取分店
- 创建分店
- 更新分店
- 删除分店（软删除）
- 搜索分店
- 获取活跃分店
- 检查分店代码是否存在

### 3. 控制器层

#### HqBranchController
RESTful API端点：
- `GET /api/v1/hqbranch` - 获取所有分店
- `GET /api/v1/hqbranch/{id}` - 根据ID获取分店
- `GET /api/v1/hqbranch/code/{branchCode}` - 根据代码获取分店
- `POST /api/v1/hqbranch` - 创建分店
- `PUT /api/v1/hqbranch/{id}` - 更新分店
- `DELETE /api/v1/hqbranch/{id}` - 删除分店
- `GET /api/v1/hqbranch/search` - 搜索分店
- `GET /api/v1/hqbranch/active` - 获取活跃分店
- `GET /api/v1/hqbranch/check-code/{branchCode}` - 检查代码是否存在

## 数据传输对象 (DTOs)

### HqBranchDto
完整的分店信息DTO，用于数据传输和显示。

### CreateHqBranchDto
创建分店时的请求DTO，包含必要的验证规则。

### UpdateHqBranchDto
更新分店时的请求DTO，继承自CreateHqBranchDto并包含ID。

### SearchHqBranchDto
搜索分店时的请求DTO，支持关键词搜索和分页。

## 安全性

### 权限控制
- 查看分店：所有已认证用户
- 创建/更新分店：Admin 和 Manager 角色
- 删除分店：仅 Admin 角色

### 数据验证
- 分店代码唯一性验证
- 字段长度和格式验证
- 电话号码格式验证

## 索引优化

### 唯一索引
- `IX_HqBranch_BranchCode`: 分店代码唯一索引

### 普通索引
- `IX_HqBranch_BranchName`: 分店名称索引
- `IX_HqBranch_IsActive`: 激活状态索引
- `IX_HqBranch_CreatedAt`: 创建时间索引
- `IX_HqBranch_ManagerName`: 店经理姓名索引

## 初始化和部署

### 数据库连接检查
在 `Program.cs` 中自动执行：
1. 检查HQ数据库连接状态
2. 检查现有分店相关表
3. 输出连接检查日志

### 服务注册
在 `Program.cs` 中注册：
```csharp
builder.Services.AddSingleton<HqSqlSugarContext>();
builder.Services.AddScoped<IHqBranchService, HqBranchService>();
```

## 测试

### API测试
使用 `test_hq_connection.http` 文件测试所有API端点：
- 基本CRUD操作
- 搜索功能
- 代码验证
- 权限控制

### 数据库连接测试
确保HQ数据库连接正常，能够正确访问现有表结构。

## 日志记录

所有数据库操作和业务逻辑都包含详细的日志记录：
- 成功操作的信息日志
- 错误操作的错误日志
- SQL语句的调试日志

## 错误处理

### 业务异常
- 分店代码重复
- 分店不存在
- 数据验证失败

### 系统异常
- 数据库连接失败
- SQL执行异常
- 网络超时

所有异常都会被适当处理并返回友好的错误信息给客户端。

## 扩展性

系统设计考虑了扩展性：
1. 独立的数据库上下文，不影响主系统
2. 清晰的分层架构，便于维护
3. 完整的DTO体系，便于API版本控制
4. 灵活的权限控制，支持角色扩展

## 性能优化

1. 使用索引优化查询性能
2. 支持软删除避免硬删除的性能问题
3. 分页查询支持大数据量处理
4. 连接池管理数据库连接