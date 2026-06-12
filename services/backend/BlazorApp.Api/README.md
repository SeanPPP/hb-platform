# HB Platform API 文档

## 项目简介

HB Platform是一个基于Blazor Server的多店铺订单管理系统，提供完整的B2B电商解决方案。本API采用RESTful设计风格，支持JWT认证，使用SQL Server数据库。

## 技术栈

- **后端框架**: ASP.NET Core 8.0
- **ORM框架**: SqlSugar
- **数据库**: SQL Server
- **认证方式**: JWT (JSON Web Token)
- **API风格**: RESTful
- **文档格式**: JSON

## 环境要求

- .NET 8.0 SDK
- SQL Server 2019+
- Visual Studio 2022 或 VS Code

## 快速开始

### 1. 环境配置

#### 数据库连接配置
在 `appsettings.json` 中配置数据库连接：

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=hotbargain.top;Database=HBweb;User Id=REDACTED;Password=REDACTED;TrustServerCertificate=true;"
  }
}
```

#### JWT配置
```json
{
  "Jwt": {
    "Key": "中华人民共和国万岁中国人民万岁",
    "Issuer": "BlazorApp",
    "Audience": "BlazorAppUsers",
    "ExpireMinutes": 60
  }
}
```

### 2. 数据库初始化

首次运行时会自动创建数据库表结构：

```bash
# 启动API服务
dotnet run --project BlazorApp.Api
```

### 3. API基础信息

- **基础URL**: `https://localhost:7001/api`
- **认证方式**: Bearer Token
- **内容类型**: `application/json`
- **字符编码**: UTF-8

## 认证与授权

### JWT Token获取

**POST** `/api/auth/login`

```json
{
  "username": "admin",
  "password": "password123"
}
```

**响应示例**:
```json
{
  "success": true,
  "data": {
    "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
    "refreshToken": "refresh_token_here",
    "expiresIn": 3600,
    "user": {
      "userId": 1,
      "username": "admin",
      "email": "admin@example.com",
      "roles": ["Admin"]
    }
  }
}
```

### Token刷新

**POST** `/api/auth/refresh`

```json
{
  "refreshToken": "your_refresh_token_here"
}
```

## API接口文档

### 用户管理

#### 获取用户列表
- **URL**: `GET /api/users`
- **权限**: Admin, Manager
- **参数**:
  - `page` (int): 页码，默认1
  - `pageSize` (int): 每页数量，默认20
  - `search` (string): 搜索关键词
  - `roleId` (int): 角色筛选

**响应示例**:
```json
{
  "success": true,
  "data": {
    "items": [
      {
        "userId": 1,
        "username": "admin",
        "email": "admin@example.com",
        "phone": "13800138000",
        "userGUID": "550e8400-e29b-41d4-a716-446655440000",
        "isActive": true,
        "createdAt": "2024-01-01T00:00:00Z",
        "roles": ["Admin"]
      }
    ],
    "total": 100,
    "page": 1,
    "pageSize": 20,
    "totalPages": 5
  }
}
```

#### 创建用户
- **URL**: `POST /api/users`
- **权限**: Admin

```json
{
  "username": "newuser",
  "email": "newuser@example.com",
  "password": "password123",
  "phone": "13800138001",
  "roleIds": [1, 2]
}
```

#### 更新用户
- **URL**: `PUT /api/users/{id}`
- **权限**: Admin

#### 删除用户
- **URL**: `DELETE /api/users/{id}`
- **权限**: Admin

### 角色管理

#### 获取角色列表
- **URL**: `GET /api/roles`
- **权限**: Admin

#### 创建角色
- **URL**: `POST /api/roles`
- **权限**: Admin

```json
{
  "roleName": "Manager",
  "description": "店铺管理员",
  "permissions": ["order:read", "order:write"]
}
```

### 店铺管理

#### 获取店铺列表
- **URL**: `GET /api/stores`
- **权限**: Admin, Manager

#### 创建店铺
- **URL**: `POST /api/stores`
- **权限**: Admin

```json
{
  "storeName": "旗舰店",
  "storeCode": "HQ001",
  "description": "主要销售渠道",
  "address": "北京市朝阳区",
  "contactPhone": "010-12345678"
}
```

### 订单管理

#### 获取订单列表
- **URL**: `GET /api/orders`
- **权限**: Admin, Manager, User
- **参数**:
  - `page` (int): 页码
  - `pageSize` (int): 每页数量
  - `status` (string): 订单状态
  - `storeId` (int): 店铺ID
  - `startDate` (string): 开始日期
  - `endDate` (string): 结束日期

#### 获取订单详情
- **URL**: `GET /api/orders/{id}`
- **权限**: Admin, Manager, User

#### 创建订单
- **URL**: `POST /api/orders`
- **权限**: Manager, User

```json
{
  "storeId": 1,
  "customerName": "张三",
  "customerPhone": "13800138000",
  "items": [
    {
      "productId": 1,
      "quantity": 2,
      "unitPrice": 99.99
    }
  ],
  "totalAmount": 199.98,
  "notes": "客户要求尽快发货"
}
```

#### 更新订单状态
- **URL**: `PUT /api/orders/{id}/status`
- **权限**: Manager, User

```json
{
  "status": "Shipped",
  "trackingNumber": "SF1234567890"
}
```

### 商品管理

#### 获取商品列表
- **URL**: `GET /api/products`
- **权限**: Admin, Manager, User

#### 创建商品
- **URL**: `POST /api/products`
- **权限**: Manager

```json
{
  "productName": "iPhone 15",
  "productCode": "IP15-001",
  "category": "电子产品",
  "price": 5999.00,
  "stock": 100,
  "description": "最新款iPhone"
}
```

### 客户管理

#### 获取客户列表
- **URL**: `GET /api/customers`
- **权限**: Admin, Manager

#### 创建客户
- **URL**: `POST /api/customers`
- **权限**: Manager

```json
{
  "customerName": "李四",
  "phone": "13900139000",
  "email": "lisi@example.com",
  "address": "上海市浦东新区",
  "company": "ABC公司"
}
```

## 错误处理

### 标准错误响应格式

```json
{
  "success": false,
  "message": "错误描述",
  "errorCode": "ERROR_CODE",
  "details": {
    "field": "具体错误信息"
  }
}
```

### 常见HTTP状态码

- **200**: 请求成功
- **201**: 创建成功
- **400**: 请求参数错误
- **401**: 未认证
- **403**: 权限不足
- **404**: 资源不存在
- **409**: 资源冲突
- **500**: 服务器内部错误

### 错误代码说明

| 错误代码 | 说明 |
|---------|------|
| `AUTH_FAILED` | 认证失败 |
| `INVALID_TOKEN` | 无效的Token |
| `TOKEN_EXPIRED` | Token已过期 |
| `PERMISSION_DENIED` | 权限不足 |
| `VALIDATION_ERROR` | 参数验证失败 |
| `RESOURCE_NOT_FOUND` | 资源不存在 |
| `DUPLICATE_RESOURCE` | 资源重复 |

## 数据模型

### 用户模型 (User)
```json
{
  "userId": 1,
  "username": "admin",
  "email": "admin@example.com",
  "phone": "13800138000",
  "userGUID": "550e8400-e29b-41d4-a716-446655440000",
  "isActive": true,
  "createdAt": "2024-01-01T00:00:00Z",
  "updatedAt": "2024-01-01T00:00:00Z"
}
```

### 角色模型 (Role)
```json
{
  "roleId": 1,
  "roleName": "Admin",
  "description": "系统管理员",
  "roleGUID": "550e8400-e29b-41d4-a716-446655440001",
  "createdAt": "2024-01-01T00:00:00Z"
}
```

### 店铺模型 (Store)
```json
{
  "storeId": 1,
  "storeName": "旗舰店",
  "storeCode": "HQ001",
  "description": "主要销售渠道",
  "address": "北京市朝阳区",
  "contactPhone": "010-12345678",
  "storeGUID": "550e8400-e29b-41d4-a716-446655440002",
  "isActive": true,
  "createdAt": "2024-01-01T00:00:00Z"
}
```

### 订单模型 (Order)
```json
{
  "orderId": 1,
  "orderNumber": "ORD20240101001",
  "storeId": 1,
  "customerName": "张三",
  "customerPhone": "13800138000",
  "totalAmount": 199.98,
  "status": "Pending",
  "orderGUID": "550e8400-e29b-41d4-a716-446655440003",
  "createdAt": "2024-01-01T00:00:00Z",
  "updatedAt": "2024-01-01T00:00:00Z"
}
```

## 开发指南

### 本地开发环境

1. **克隆项目**
```bash
git clone <repository-url>
cd blazor
```

2. **安装依赖**
```bash
dotnet restore
```

3. **配置数据库**
- 确保SQL Server服务运行
- 更新 `appsettings.json` 中的连接字符串

4. **运行项目**
```bash
dotnet run --project BlazorApp.Api
```

### API测试

推荐使用以下工具进行API测试：

- **Postman**: 功能强大的API测试工具
- **Swagger UI**: 项目内置的API文档界面
- **curl**: 命令行工具

### 示例请求

```bash
# 登录获取Token
curl -X POST https://localhost:7001/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"password123"}'

# 使用Token访问API
curl -X GET https://localhost:7001/api/users \
  -H "Authorization: Bearer YOUR_TOKEN_HERE"
```

## 部署指南

### 生产环境配置

1. **环境变量**
```bash
export ASPNETCORE_ENVIRONMENT=Production
export ConnectionString=REDACTED
```

2. **SSL证书**
- 配置HTTPS证书
- 启用强制HTTPS重定向

3. **数据库备份**
- 配置定期数据库备份
- 设置数据库日志备份

### Docker部署

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["BlazorApp.Api/BlazorApp.Api.csproj", "BlazorApp.Api/"]
RUN dotnet restore "BlazorApp.Api/BlazorApp.Api.csproj"
COPY . .
WORKDIR "/src/BlazorApp.Api"
RUN dotnet build "BlazorApp.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "BlazorApp.Api.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BlazorApp.Api.dll"]
```

## 监控与日志

### 日志配置

项目使用结构化日志记录，支持以下日志级别：

- **Information**: 一般信息
- **Warning**: 警告信息
- **Error**: 错误信息
- **Critical**: 严重错误

### 性能监控

- 使用Application Insights进行性能监控
- 配置数据库查询性能监控
- 设置API响应时间告警

## 安全指南

### 安全最佳实践

1. **认证安全**
   - 使用强密码策略
   - 实施账户锁定机制
   - 定期更换JWT密钥

2. **数据安全**
   - 所有敏感数据加密存储
   - 使用参数化查询防止SQL注入
   - 实施输入验证和清理

3. **网络安全**
   - 启用HTTPS
   - 配置CORS策略
   - 实施速率限制

### 安全配置

```json
{
  "Security": {
    "RequireHttps": true,
    "RateLimit": {
      "RequestsPerMinute": 100
    },
    "Cors": {
      "AllowedOrigins": ["https://yourdomain.com"],
      "AllowedMethods": ["GET", "POST", "PUT", "DELETE"]
    }
  }
}
```

## 常见问题

### Q: 如何处理Token过期？
A: 使用刷新Token机制，当访问Token过期时，使用刷新Token获取新的访问Token。

### Q: 如何添加新的API端点？
A: 在对应的Controller中添加新的Action方法，确保添加适当的授权属性。

### Q: 如何处理数据库连接问题？
A: 检查连接字符串配置，确保SQL Server服务运行，网络连接正常。

### Q: 如何调试API问题？
A: 查看控制台日志输出，使用Swagger UI进行API测试，检查数据库连接状态。

## 更新日志

### v1.0.0 (2024-01-01)
- 初始版本发布
- 实现用户认证和授权
- 实现基础的CRUD操作
- 支持多店铺管理

## 联系方式

- **技术支持**: support@hbplatform.com
- **文档更新**: 2024-01-01
- **API版本**: v1.0.0

---

© 2024 HB Platform. All rights reserved. 