# 数据同步实现方案

我将根据您的要求实现 `Location`（货位）和 `ProductLocation`（商品货位关联）的数据同步功能。

## 1. 创建映射配置文件
为 `Location` 同步创建一个新的 AutoMapper 配置文件。
- **文件:** `BlazorApp.Api/Mappings/Profiles/React/ReactLocationProfile.cs`
- **映射关系:** `CPT_DIC_货位编码信息表` -> `Location`
  - `HGUID` -> `LocationGuid`
  - `货位类型` -> `LocationType`
  - `货位编码` -> `LocationCode`
  - `货位条形码` -> `LocationBarcode`
  - `状态` -> `Status`

## 2. 更新服务接口
在 `IDataSyncReactService` 中添加新的方法签名。
- **文件:** `BlazorApp.Api/Interfaces/React/IDataSyncReactService.cs`
- **新增方法:**
  - `Task<SyncResult> SyncLocationsFromHqAsync(int hqBatchSize = 50000, int writePageSize = 10000);`
  - `Task<SyncResult> SyncProductLocationsFromHqAsync(int hqBatchSize = 50000, int writePageSize = 10000);`

## 3. 更新服务实现
在 `DataSyncReactService` 中实现具体的同步逻辑。
- **文件:** `BlazorApp.Api/Services/React/DataSyncReactService.cs`

### 3.1 `SyncLocationsFromHqAsync` (货位同步)
- 清空本地 `Location` 表。
- 从 HQ 数据库获取 `CPT_DIC_货位编码信息表`。
- 使用 AutoMapper 映射为 `Location` 实体。
- 批量写入本地数据库。
- 使用事务确保数据一致性。

### 3.2 `SyncProductLocationsFromHqAsync` (商品货位关联同步)
- 清空本地 `ProductLocation` 表。
- **预加载:** 将 `CPT_DIC_货位编码信息表` 的 `货位编码` 到 `HGUID` 的映射关系加载到内存字典中，用于后续快速查找 `LocationGuid`。
- **同步源 1 (`CPT_RED_货位存货信息表`):**
  - 分批获取数据。
  - 转换为 `ProductLocation` 实体:
    - `Guid` = 源数据的 `HGUID`
    - `ProductCode` = 源数据的 `商品编码`
    - `LocationGuid` = 通过 `货位编码` 在字典中查找对应的 `HGUID`
  - 批量写入本地数据库。
- **同步源 2 (`CPT_RED_货位配货信息表`):**
  - 执行与上述相同的处理流程。
- 全程使用事务包裹。

## 4. 验证
- 确保代码编译通过。
