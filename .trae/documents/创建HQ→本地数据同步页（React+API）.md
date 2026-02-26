## 目标
- 创建 React 专用的数据同步链路：控制器、服务层、映射配置全部“全新且独立实现”
- 前端 React 新建页面与服务，提供两个按钮：
  - HQ `DIC_商品信息字典表 → HBweb.Product`
  - HQ `DIC_商品零售价表 → HBweb.StoreRetailPrice`
- 页面显示同步耗时、开始/结束时间、成功/错误条数与消息；导航入口位于“系统设置”下

## 后端（React 专用）
- 控制器：`BlazorApp.Api/Controllers/React/DataSyncReactController.cs`
  - 路由：`/api/react-sync`
  - 端点：
    - `POST /api/react-sync/products`（全量商品同步）
    - `POST /api/react-sync/store-retail-prices`（按分店并发同步零售价），请求体 `{ selectedStoreCodes?: string[] }`
  - 返回 `ApiResponse<SyncResult>`；权限 `[Authorize(Roles = "Admin")]`
- 服务接口：`BlazorApp.Api/Interfaces/React/IDataSyncReactService.cs`
  - `Task<SyncResult> SyncProductsFromHqAsync()`
  - `Task<SyncResult> SyncStoreRetailPricesFromHqConcurrentAsync(List<string>? storeCodes, int maxConcurrency = 30, int batchSize = 200000)`
- 服务实现：`BlazorApp.Api/Services/React/DataSyncReactService.cs`
  - 依赖：`SqlSugarContext`（本地）、`HqSqlSugarContext`（HQ）、`IMapper`（仅使用“React专用映射”）、`ILogger`
  - 商品同步：事务→可选清空本地 `Product`→HQ 分页读取 50,000/批→映射到 `Product`→`Fastest<Product>().PageSize(10000).BulkCopyAsync`→统计/耗时/重试
  - 零售价同步：选中分店并发→独立 HQ/本地连接→Join 过滤有效数据→映射到 `StoreRetailPrice`→`Fastest<StoreRetailPrice>().PageSize(50000).BulkCopyAsync`→仅清理选中分店数据→统计/耗时/定时进度日志

## React专用映射（全新新增，不复用现有Profile）
- 目录：`BlazorApp.Api/Mappings/Profiles/React/`
  - `ReactProductMappingProfile.cs`：定义 `DIC_商品信息字典表 → HBweb.Product` 的字段映射（商品编码、分类GUID、供应商、货号、条码、名称、类型、中包数、进货价、零售价、是否自动定价、图片、使用状态、是否特殊、仓库分类GUID、创建/更新人与时间等）
  - `ReactStoreRetailPriceMappingProfile.cs`：定义 `DIC_商品零售价表 → HBweb.StoreRetailPrice` 的字段映射（分店代码、商品编码、供应商编码、进货价、零售价、折扣率、是否使用、是否自动定价、创建/更新时间等，忽略导航属性）
- 注册：AutoMapper 会自动扫描同程序集的 `Profile` 类；保留现有 `MappingProfile` 总入口无需改动（或可显式添加）。

## React 服务层
- 新增 `ReactUmi/my-app/src/services/dataSync.ts`
  - `syncProducts()` → `POST /api/react-sync/products`
  - `syncStoreRetailPrices(selectedStoreCodes?: string[])` → `POST /api/react-sync/store-retail-prices`
  - 对价格同步设置较长 `timeout`（30–60 分钟），透出 `SyncResult`

## React 页面
- 新建 `ReactUmi/my-app/src/pages/SystemSettings/HqDataSync/index.tsx`
  - 两个按钮：
    - 同步商品信息（字典→Product）
    - 同步分店零售价（零售价→StoreRetailPrice）
  - 分店多选（使用 `storeService.getActiveStores()`），支持全选/清空
  - 展示 `startTime/endTime/duration/addedCount/errorCount/message`
  - Admin 访问控制；运行中禁用按钮并反馈进度

## React 导航
- 在“系统设置”菜单下新增 `/system/hq-data-sync`，名称“数据同步”
- 更新 `ReactUmi/my-app/src/app.ts` 的 `pathComponentMap`：加入 `'/system/hq-data-sync': { component: 'HqDataSync', icon: 'SettingOutlined', keepAlive: true }`
- 增加国际化文案 `locales/zh-CN.ts` 与 `en-US.ts`

## 性能与健壮性
- 商品 20万：分页拉取+BulkCopy；批次重试与延迟避压；事务保证一致性
- 零售价 700万：按分店并发（默认30）；每分店批量写入并重试；本地分店数据清理与内存回收；前端超时配置

## 验证
- 先选 1–2 分店测试零售价；校验统计与耗时
- 商品同步后，校验规模与字段映射正确
- 全分店同步观察日志与耗时；必要时调整并发与批次

## 交付项
- 后端：React 专用控制器、服务接口与实现、React 专用映射 Profile 两个
- 前端：React 服务、页面、导航映射与国际化文案

如确认，我将按此“控制器/服务/映射全新实现”的方案开始编码。