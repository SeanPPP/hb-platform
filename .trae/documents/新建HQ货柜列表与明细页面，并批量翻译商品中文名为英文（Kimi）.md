## 目标
- 在 ReactUmi 前端新增 HQ 货柜列表与明细页面，并将入口归入“货柜管理”菜单。
- 在后端 React 目录新增控制器与服务层，提供 HQ 货柜数据与“商品英文名称批量翻译（只回写英文名）”。

## 后端（React 目录）
- 控制器：`BlazorApp.Api/Controllers/React`
  - `ReactHqContainerController`（`api/react/v1/hq-containers`）：列表与详情。
  - `ReactHqProductTranslationController`（`api/react/v1/hq-products/translate-names`）：批量英文名翻译。
- 服务：`BlazorApp.Api/Services/React`
  - `HqContainerReactService`：查询 `CPT_RED_货柜单主表Store` 与 `CPT_RED_货柜单详情表Store`，联查商品字典表补充 `中文名称/英文名称`。
  - `HqProductTranslationReactService`：筛选需翻译商品并调用 `TranslationService.BatchTranslateToEnglishAsync`，只更新 `英文名称`。
- 接口：`BlazorApp.Api/Interfaces/React` 增加 `IHqContainerReactService`、`IHqProductTranslationReactService`。
- 参考代码：
  - 详情导航：`BlazorApp.Shared/Models/HqEntities/CPT_RED_货柜单详情表.cs:36-41`。
  - HQ上下文 SimpleClient：`BlazorApp.Api/Data/HqSqlSugarContext.cs:73-76`。
  - 商品字典字段：`BlazorApp.Shared/Models/HqEntities/CPT_DIC_商品信息字典表_HQ.cs:32-36`。
  - 翻译服务：`BlazorApp.Api/Services/TranslationService.cs`（Kimi）。

## 前端（ReactUmi/my-app）
- 页面与路由
  - 新增 `src/pages/HqContainerList/index.tsx`（`/hq/containers`）。
  - 新增 `src/pages/HqContainerDetail/index.tsx`（`/hq/containers/:id`）。
- 菜单导航（归入“货柜管理”）
  - 在 `.umirc.ts` 的路由配置中，找到“货柜管理”分组（现有 `ContainerList`/`ContainerDetail` 所在位置），在该分组下追加：
    - `{ path: '/hq/containers', name: 'HQ货柜列表', component: '@/pages/HqContainerList' }`
    - `{ path: '/hq/containers/:id', name: 'HQ货柜明细', component: '@/pages/HqContainerDetail', hideInMenu: true }`
- 服务层
  - `src/services/hqContainer.ts`：`postHqContainersList`、`getHqContainerDetail` 调用 `api/react/v1/hq-containers`。
  - `src/services/hqProductTranslation.ts`：`postTranslateNamesByContainers`、`postTranslateNamesAll` 调用 `api/react/v1/hq-products/translate-names`。
- 页面交互
  - 列表页：AntD Table + Form，分页/筛选，点击进入详情。
  - 详情页：展示主表与明细（含 `中文名称/英文名称`）；按钮“翻译当前货柜商品英文名”，调用容器范围翻译接口并提示统计结果。

## 翻译策略
- 触发：`中文名称` 非空且含中文（`ContainsChinese`）。
- 写入：仅当 `英文名称` 为空或仍为中文；只更新 `英文名称` 字段（保持“只回写 英文名称 其它不变”）。
- Kimi 配置：`Translation:Provider='kimi'` 并设置 `Translation:Kimi:{ApiKey, Model, Endpoint}`。

## 验证
- 后端 `.http` 脚本验证列表、详情、翻译接口。
- 前端 Umi 路由与菜单验证；页面显示英文名更新与统计反馈。

## 变更范围
- 后端 React 目录：新增 2 控制器、2 服务、2 接口、DTO 与映射扩展。
- 前端 ReactUmi：新增 2 页面与 2 服务文件，更新 `.umirc.ts` 路由与“货柜管理”菜单分组。

## 确认点
- “HQ货柜列表/明细”显示名称与菜单顺序是否与现有“货柜管理”一致。
- 翻译覆盖策略是否保留默认不覆盖有效英文（可选参数开启覆盖）。