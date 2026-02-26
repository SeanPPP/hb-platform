# GUID主键迁移指南

## 概述

本指南详细说明了如何将数据库主键从自增整数ID迁移到GUID字符串主键。

## 迁移内容

### 受影响的表
1. **User** - 用户表
2. **Role** - 角色表  
3. **Store** - 分店表
4. **UserRole** - 用户角色关联表
5. **UserStore** - 用户分店关联表
6. **RefreshToken** - 刷新令牌表

### 主要变更
- **BaseEntity** 移除了 `Id` 字段（整数自增主键）
- **所有表** 现在使用各自的GUID字段作为主键：
  - User.UserGUID
  - Role.RoleGUID
  - Store.StoreGUID
  - UserRole.UserRoleGUID
  - UserStore.UserStoreGUID
  - RefreshToken.RefreshTokenGUID

## 迁移方法

### 方法1：自动迁移（推荐）

系统会自动检测是否需要迁移。当检测到旧的整数主键结构时，会自动执行迁移：

```csharp
// 在应用启动时自动执行
var context = serviceProvider.GetRequiredService<SqlSugarContext>();
context.CreateTable(); // 会自动检测并执行迁移
```

### 方法2：手动迁移

#### 1. 检查是否需要迁移
```http
GET /api/v1/migration/check-migration
Authorization: Bearer {admin-token}
```

#### 2. 执行迁移
```http
POST /api/v1/migration/execute-migration
Authorization: Bearer {admin-token}
```

#### 3. 检查表结构状态
```http
GET /api/v1/migration/table-status
Authorization: Bearer {admin-token}
```

### 方法3：强制重新创建（清空数据）

⚠️ **警告：此操作会清空所有数据**

```http
POST /api/v1/migration/force-recreate
Authorization: Bearer {admin-token}
```

## 迁移流程详解

### 自动迁移流程

1. **备份数据** - 将现有数据备份到临时表
2. **删除约束** - 删除外键约束
3. **删除旧表** - 删除原有表结构
4. **创建新表** - 使用GUID主键创建新表结构
5. **迁移数据** - 保持现有GUID值或生成新GUID
6. **清理临时表** - 删除备份表

### 数据保持策略

- **已存在GUID** - 如果记录已有有效GUID值，保持不变
- **新生成GUID** - 如果GUID为空或无效，自动生成新GUID
- **关联关系** - 通过GUID字段维护表间关联关系

## 安全措施

### 事务保护
所有迁移操作都在数据库事务中执行，确保：
- 要么全部成功
- 要么全部回滚
- 不会出现部分迁移状态

### 备份策略
迁移前会自动创建备份表：
- User_Backup
- Role_Backup
- Store_Backup
- UserRole_Backup
- UserStore_Backup
- RefreshToken_Backup

### 验证机制
- 迁移前检查表结构
- 迁移后验证数据完整性
- 支持多次执行（幂等性）

## 索引优化

### 主键索引
- 所有GUID主键字段自动创建聚集索引
- 无需手动创建主键索引

### 唯一索引
保留业务唯一性约束：
- User.Username（唯一）
- User.Email（唯一）
- Role.RoleName（唯一）
- Store.StoreCode（唯一）
- RefreshToken.Token（唯一）

### 关联索引
优化查询性能：
- UserRole(UserGUID, RoleGUID)
- UserStore(UserGUID, StoreGUID)
- RefreshToken(UserGUID)

## 性能影响

### 优势
1. **UUID唯一性** - 全局唯一，支持分布式
2. **更好扩展性** - 避免ID冲突
3. **安全性** - ID不可预测

### 注意事项
1. **存储空间** - GUID占用更多空间（36字符 vs 4字节）
2. **索引性能** - 字符串索引相对较慢
3. **SQL可读性** - GUID不如整数直观

## 回滚方案

如果需要回滚到整数主键：

1. **停止应用服务**
2. **恢复备份表数据**
3. **重新部署旧版本代码**
4. **重新启动服务**

```sql
-- 回滚示例（谨慎使用）
DROP TABLE [User];
SELECT * INTO [User] FROM User_Backup;
-- 重复其他表...
```

## 验证清单

迁移完成后请验证：

- [ ] 所有表都有GUID主键
- [ ] 数据记录数量一致
- [ ] 关联关系正确
- [ ] 应用功能正常
- [ ] 性能可接受
- [ ] 备份表已清理

## 故障排除

### 常见问题

1. **迁移超时**
   - 检查数据量大小
   - 考虑分批迁移

2. **外键约束错误**
   - 确保关联表迁移顺序
   - 检查数据完整性

3. **GUID重复**
   - 检查现有GUID是否有重复
   - 重新生成问题GUID

4. **权限不足**
   - 确保数据库用户有DDL权限
   - 检查表和索引操作权限

### 紧急联系

如遇严重问题，请立即：
1. 停止应用服务
2. 联系数据库管理员
3. 准备数据库备份
4. 记录错误详情

## 相关文件

- `BlazorApp.Shared/Models/BaseEntity.cs` - 基础实体类
- `BlazorApp.Api/Data/MigrationScripts.cs` - 迁移脚本
- `BlazorApp.Api/Data/SqlSugarContext.cs` - 数据库上下文
- `BlazorApp.Api/Controllers/MigrationController.cs` - 迁移API接口