## 创建 POSM 商品-供应商映射表并添加同步功能

### 需求
在 POSM 数据库中创建一个商品/本地供应商/中国供应商对应关系表，从主数据库同步数据，并在数据同步页面添加更新按钮。

### 实现步骤

#### 1. 后端 - 创建实体类
**文件**: `BlazorApp.Shared/Models/POSM/PosmProductSupplierMapping.cs`
```csharp
- ProductUUID (主键) - 商品 UUID
- LocalSupplierCode - 本地供应商代码
- ChinaSupplierCode - 中国供应商代码 (可为空)
- LastUpdateTime - 最后更新时间
- IsDeleted - 软删除标记
```

#### 2. 后端 - 添加到 POSMSqlSugarContext
**文件**: `BlazorApp.Api/Data/POSMSqlSugarContext.cs`
- 添加 `SimpleClient<PosmProductSupplierMapping>`

#### 3. 后端 - 创建同步服务
**文件**: `BlazorApp.Api/Services/React/PosmProductSupplierMappingReactService.cs`
- 实现 `IPosmProductSupplierMappingReactService`
- 创建同步方法：从主数据库 Product、HBLocalSupplier、ChinaSupplier 读取数据并写入 POSM

#### 4. 后端 - 创建 API 控制器
**文件**: `BlazorApp.Api/Controllers/React/ReactSyncController.cs`
- 添加 `SyncPosmProductSupplierMappings()` 端点
- 返回 SyncResult

#### 5. 后端 - 注册服务
**文件**: `BlazorApp.Api/Program.cs`
- 注册服务和上下文

#### 6. 前端 - 添加 API 服务
**文件**: `ReactUmi/my-app/src/services/dataSync.ts`
- 添加 `syncPosmProductSupplierMappings()` 函数

#### 7. 前端 - 添加 UI 组件
**文件**: `ReactUmi/my-app/src/pages/SystemSettings/HqDataSync/index.tsx`
- 添加新的同步卡片："商品-供应商映射（POSM 数据库）"
- 包含同步按钮和结果展示

#### 8. 前端 - 添加路由配置
**文件**: `ReactUmi/my-app/.umirc.ts`
- 如需要，添加新页面路由

### 数据同步逻辑
1. 从主数据库读取所有 Product（UUID, LocalSupplierCode）
2. 从主数据库读取 HBLocalSupplier
3. 从主数据库读取 ChinaSupplier（仅当 LocalSupplierCode == "200" 的商品）
4. 构建映射关系表数据
5. 写入 POSM 数据库（使用 Upsert，按 ProductUUID 主键）
6. 记录同步统计信息（新增、更新、错误）

### 技术要点
- 使用 SqlSugar 的 FastestBulkCopy 批量写入
- 处理 LocalSupplierCode == "200" 时关联 ChinaSupplierCode
- 记录最后更新时间用于增量同步
- 返回标准的 SyncResult 格式