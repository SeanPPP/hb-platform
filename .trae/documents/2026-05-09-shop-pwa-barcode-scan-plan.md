# 订货前台 PWA 条码扫码实现计划

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 在 `react-vite-admin-phase1` 的 `/shop` 订货前台实现同时覆盖 Android 与 iOS 的可安装 PWA 壳层，并在首页提供外接扫码枪无焦点按条码查询商品后直接加入购物车的能力。

**Architecture:** 前端使用 `vite-plugin-pwa` 生成 `manifest` 和 `service worker`，并补齐 iOS 所需的 Apple 图标与元信息；缓存仍限定在应用壳与静态资源，不做完整离线订货。扫码能力封装为独立 Hook 和首页扫描条组件，基于键盘事件缓冲识别外接扫码枪输入，通过专用扫码查询接口先按条码解析商品，再调用购物车接口直加；提示音通过 Web Audio 在用户首次交互后解锁，兼容 iOS Safari 的限制。

**Tech Stack:** React 18, Vite 5, TypeScript, Ant Design 5, Zustand, `vite-plugin-pwa`, ASP.NET Core, SqlSugar

***

## Summary

* 交付一个“可安装但业务仍在线”的 PWA 版本订货前台，并把 Android Chrome 与 iOS Safari 都纳入正式兼容范围。

* 在 `/shop` 首页顶部增加固定扫描条，页面无输入框聚焦时可直接接收扫码枪输入。

* 每次成功扫码后，按条码解析商品；默认加购数量优先取 `minOrderQuantity`，若为空或 `<= 0` 则固定加 `1`。

* 若同一条码返回多个可选商品，不直接加购，改为弹出候选列表由用户选择后再加入购物车。

* 扫码结果需要带有不同提示声音，至少区分成功、需选择、未命中、失败或阻止四类反馈。

* 若未选择分店、条码未命中、接口失败或命中异常，首页扫描条直接反馈明确状态，不跳转页面。

* 后端扫码查询接口按“条码优先”返回候选结果，避免条码与货号冲突时返回错误商品。

## Current State Analysis

* `package.json` 仅包含基础 React/Vite/Antd 依赖，当前没有任何 PWA 相关依赖。

* `vite.config.ts` 目前只配置 React 插件和 `/api`、`/hangfire` 代理，没有 `manifest`、`service worker`、离线缓存或安装配置。

* `index.html` 只有基础标题和视口设置，没有 `theme-color`、移动端安装相关 meta，也没有图标资源目录；当前状态下对 iOS 安装支持不足。

* `src/App.tsx` 已将 `/shop` 作为独立前台壳层路由，适合作为 PWA 的主要入口。

* `src/layout/ShopLayout.tsx` 已提供分店选择、购物车入口、搜索框和前台导航，但没有扫码入口或全局扫码监听。

* `src/pages/ShopHome/index.tsx` 已能加载商品列表、刷新购物车、刷新动态数据，并支持调用 `addStoreOrderCartItem()` 加购。

* `src/services/storeOrderService.ts` 已有 `batchLookupStoreOrderProducts()`，适合现有粘贴/批量编码补货场景；但它每个输入码只返回单个 `product`，不满足本次“多个结果用户选择”的扫码需求。

* `src/types/storeOrder.ts` 中 `StoreOrderProductItem` 已含 `barcode`、`itemNumber`、`productCode` 字段，足够承载扫码结果。

* 前端仓库中没有现成的 `*.test.ts(x)` 或 `*.spec.ts(x)` 测试基建；后端工作树中也未发现现成测试工程，本次以构建验证和手工回归为主。

* 当前仓库没有 `apple-touch-icon`、PNG 安装图标或 iOS 音频解锁逻辑，若不补齐，PWA 在 iPhone/iPad 上只能算“勉强可打开”，不能算正式支持。

* 后端 `BlazorApp.Api\Services\React\StoreOrderReactService.cs` 的 `BatchLookupProductsAsync()` 当前命中优先级是 `itemNumber -> barcode -> productCode`，与本次“按照条码”要求冲突；同时该接口只返回单个命中结果，无法支撑“多个结果用户选择”，因此需要新增或扩展专用扫码查询能力。

## Proposed Changes

### 1. PWA 基础壳层

**Files**

* Modify: `d:\DevRepos\react-vite-admin-phase1\package.json`

* Modify: `d:\DevRepos\react-vite-admin-phase1\vite.config.ts`

* Modify: `d:\DevRepos\react-vite-admin-phase1\index.html`

* Modify: `d:\DevRepos\react-vite-admin-phase1\src\main.tsx`

* Create: `d:\DevRepos\react-vite-admin-phase1\public\pwa\icon-192.png`

* Create: `d:\DevRepos\react-vite-admin-phase1\public\pwa\icon-512.png`

* Create: `d:\DevRepos\react-vite-admin-phase1\public\pwa\icon-maskable-512.png`

* Create: `d:\DevRepos\react-vite-admin-phase1\public\pwa\apple-touch-icon.png`

**What / Why / How**

* 在 `package.json` 增加 `vite-plugin-pwa`，避免手写 SW 注册与资源注入逻辑。

* 在 `vite.config.ts` 挂载 `VitePWA`：

  * `registerType: 'autoUpdate'`

  * `includeAssets` 包含 Android 与 iOS 安装图标

  * `manifest` 中定义 `name`、`short_name`、`theme_color`、`background_color`、`display: 'standalone'`

  * `scope` 与 `start_url` 以 `/shop` 为主入口

  * `workbox` 仅缓存应用壳、静态资源、字体和图片；对 `/api/**` 使用 `NetworkOnly`，避免误缓存业务数据

  * `navigateFallback` 指向主入口 HTML，保证安装后刷新仍能回到 SPA

* 在 `index.html` 增加 `theme-color`、`apple-mobile-web-app-capable`、`apple-mobile-web-app-status-bar-style`、`apple-mobile-web-app-title`、`apple-touch-icon` 等 iOS/Android 安装相关元信息。

* 在 `src/main.tsx` 注册虚拟 PWA 更新脚本，确保 SW 在生产环境自动注册并可接收新版本。

* 新建成套 PNG 图标资源，确保 Android 安装与 iOS“添加到主屏幕”都能使用稳定图标，而不是只依赖 SVG。

### 2. 首页扫描条与无焦点扫码捕获

**Files**

* Create: `d:\DevRepos\react-vite-admin-phase1\src\hooks\useBarcodeScanner.ts`

* Create: `d:\DevRepos\react-vite-admin-phase1\src\components\ShopScanBar.tsx`

* Create: `d:\DevRepos\react-vite-admin-phase1\src\components\ShopScanResultPicker.tsx`

* Modify: `d:\DevRepos\react-vite-admin-phase1\src\pages\ShopHome\index.tsx`

* Modify: `d:\DevRepos\react-vite-admin-phase1\src\styles\global.css`

**What / Why / How**

* 新增 `useBarcodeScanner.ts`，统一封装扫码枪键盘输入识别：

  * 仅在启用状态下监听 `window` 的 `keydown`

  * 对普通字符持续入缓冲区

  * 遇到 `Enter` 立即完成一次扫码

  * 若扫码枪未追加 `Enter`，则在短间隔静默后自动提交缓冲区

  * 使用最小长度过滤空码和误触

  * 当当前聚焦元素是 `input`、`textarea`、`select`、`contenteditable` 或 Antd 输入容器时暂停采集，避免干扰正常手输

* 新增 `ShopScanBar.tsx`，作为首页顶部固定扫描条 UI：

  * 显示当前状态：Ready / Scanning / Added / Not Found / Error / Store Required

  * 显示最近一次扫码值、最近命中商品名、最近加购数量

  * 提供显式的“Pause / Resume”控制，便于门店临时关闭全局扫码监听

  * 保留手动输入并提交按钮作为调试兜底，但默认不要求聚焦输入框

  * 增加“Enable Sound”或等价的首次交互开关，用于解锁 iOS Safari 的音频上下文

* 新增 `ShopScanResultPicker.tsx`，用于多结果场景下的候选商品选择：

  * 以弹窗或抽屉列出同一条码返回的多个商品

  * 每条候选展示商品图、名称、货号、条码、价格、最小订量和当前购物车数量

  * 用户选择某一项后再触发加购

* 在 `ShopHome/index.tsx` 中挂载扫描条与业务编排：

  * 仅首页启用该能力，不扩散到 `/shop/orders` 等页面

  * 复用现有 `selectedStore`、`setCart`、`refreshCart()`、`refreshDynamicData()`

  * 扫码成功时，调用“按条码查候选商品”服务：

    * 0 个结果：提示未命中并播放未命中提示音

    * 1 个结果：直接按 `product.minOrderQuantity > 0 ? minOrderQuantity : 1` 加购，并播放成功提示音

    * 多个结果：打开候选选择器，等待用户确认后加购，并播放“待选择”提示音

  * 加购成功后刷新购物车摘要与当前页商品动态数据，让卡片 `In Cart` 与右上角购物车同步更新

  * 条码未命中时显示错误态，不自动跳转搜索页

  * 未选择分店时先提示并阻止加购，避免产生无归属购物车

* 在首页业务层内增加“结果提示音”编排，避免纯视觉反馈被连续扫码覆盖。

* 在 `global.css` 中补齐扫描条、状态标签、最近结果反馈、移动端安全区与粘性布局样式，保证 PWA 全屏和手机浏览器下可用。

### 3. 订货服务层扫码查询与提示音封装

**Files**

* Modify: `d:\DevRepos\react-vite-admin-phase1\src\services\storeOrderService.ts`

* Modify: `d:\DevRepos\react-vite-admin-phase1\src\types\storeOrder.ts`

* Create: `d:\DevRepos\react-vite-admin-phase1\src\utils\scanFeedback.ts`

**What / Why / How**

* 在 `storeOrderService.ts` 增加专用扫码查询封装，例如 `lookupStoreOrderProductsByBarcode(barcode: string)`：

  * 前端只传一个条码

  * 返回候选商品列表，而不是单个商品

  * 与现有 `batchLookupStoreOrderProducts()` 分开，避免影响仓库订货粘贴等既有调用

* 在 `types/storeOrder.ts` 中补充扫码候选 DTO、扫码处理状态枚举与结果联合类型，保持首页组件类型清晰。

* 新增 `scanFeedback.ts`，基于 Web Audio API 生成轻量提示音，不依赖静态音频文件：

  * 成功：短促上扬音

  * 多结果待选择：双击提示音

  * 未命中：较低提示音

  * 失败或未选分店：错误提示音

* 为兼容 iOS Safari，音频模块需支持“首次用户手势解锁”：

  * 首次进入首页时允许用户点一次扫描条或“Enable Sound”按钮

  * 解锁成功后才播放后续查询结果提示音

  * 若用户不解锁或浏览器拒绝播放，功能仍可用，只降级为视觉提示

### 4. 后端扫码查询接口与条码优先级修正

**Files**

* Modify: `d:\DevRepos\HBweb-worktrees\Backend\publish-master\BlazorApp.Api\Services\React\StoreOrderReactService.cs`

* Modify: `d:\DevRepos\HBweb-worktrees\Backend\publish-master\BlazorApp.Api\Controllers\React\ReactStoreOrderController.cs`

* Modify: `d:\DevRepos\HBweb-worktrees\Backend\publish-master\BlazorApp.Api\Interfaces\React\IStoreOrderReactService.cs`

* Modify: `d:\DevRepos\HBweb-worktrees\Backend\publish-master\BlazorApp.Shared\DTOs\StoreOrderDtos.cs`

**What / Why / How**

* 为扫码场景新增专用接口，例如 `POST /api/react/v1/store-order/products/scan-lookup`：

  * 入参只接收一个 `barcode`

  * 返回 `items: StoreOrderProductDto[]`

  * 查询逻辑优先按 `barcode` 精确匹配

  * 若条码无结果，再根据需要决定是否补充同值 `itemNumber` / `productCode` 结果，并在返回中保留原始命中类型，便于前端展示

* 保留现有 `products/batch-lookup` 不动，避免影响仓库明细页已有粘贴导入能力。

* 在 `StoreOrderDtos.cs` 中补充扫码请求/响应 DTO，不复用“单 product”结构。

* 在 `IStoreOrderReactService.cs` 与 `ReactStoreOrderController.cs` 中新增对应服务签名与控制器路由。

* 若实际数据允许一条码对应多个商品，则后端直接全部返回；前端负责选择，不在服务端强行截断。

### 5. 前台交互细节与容错规则

**Files**

* Modify: `d:\DevRepos\react-vite-admin-phase1\src\pages\ShopHome\index.tsx`

* Modify: `d:\DevRepos\react-vite-admin-phase1\src\components\ShopCartSummary.tsx`

* Optional Modify: `d:\DevRepos\react-vite-admin-phase1\src\layout\ShopLayout.tsx`

**What / Why / How**

* 首页扫码成功后的提示信息要足够短平快，适合扫码枪连续操作：

  * 成功：显示商品名 + `+最终加购数`

  * 多结果：显示 `Multiple matches`

  * 未命中：显示 `Barcode not found`

  * 缺分店：显示 `Select store first`

* 最终加购数规则固定为：

  * `minOrderQuantity > 0` 时按其值加购

  * `minOrderQuantity` 为空、`0` 或负数时固定加 `1`

* 如需更显著反馈，可在 `ShopCartSummary.tsx` 或首页局部增加短时高亮，让连续扫码时能感知购物车数量变化。

* 若 `ShopLayout.tsx` 中现有搜索框和扫描条存在视觉竞争，可仅调整顶部间距和响应式布局，不改变现有搜索行为。

* 扫描条不拦截普通搜索框输入；当用户主动聚焦搜索框或数量输入框时，扫码监听自动暂停，焦点离开后恢复。

## Assumptions & Decisions

* 用户已确认本次 PWA 范围是“可安装壳层”，不做完整离线订货、不做离线购物车同步。

* 用户已确认本次 PWA 需正式支持 Android 与 iOS，两端都需要纳入安装与扫码枪使用验收。

* 用户已确认扫码入口固定在 `/shop` 首页，不新增独立扫码页，也不在整个 `shop` 壳层全局开启。

* 用户已确认扫码后默认按 `minOrderQuantity` 加购，但当 `minOrderQuantity` 为空或 `<= 0` 时固定加 `1`。

* 用户已明确扫码查询语义必须“按照条码”，因此计划包含后端同步修正，不只做前端兼容。

* 用户已确认多个结果时由用户手动选择，不接受服务端或前端直接自动选第一项。

* 用户已要求查询结果带不同提示声音，因此计划加入纯前端音频反馈，不额外引入静态音频素材管理。

* 用户已确认扫码方式仅包含“外接扫码枪模拟键盘输入”，不包含手机摄像头扫码。

* 默认扫码枪行为按“键盘模拟 + Enter 结尾”优先处理；若设备不发送 `Enter`，前端再用短间隔静默自动提交作为兜底。

* 本仓库与对应后端工作树目前都缺少现成测试工程，本次不补重型自动化测试基建，优先保证构建通过和手工验收闭环。

## Verification Steps

### Frontend

* 安装依赖并确认新增 PWA 依赖解析正常。

* 执行 `npm run build`，确认 Vite 构建通过，产物中生成 `manifest` 与 `service worker`。

* 本地启动前台后验证：

  * `/shop` 可正常打开，原有商品浏览、搜索、购物车、提交订单不回归

  * 浏览器地址栏出现安装入口，安装后以独立窗口打开

  * Android Chrome 安装后可独立窗口打开并正常使用扫码枪

  * iOS Safari 通过“添加到主屏幕”安装后可独立打开并正常使用扫码枪

  * 首页无输入框聚焦时，扫码枪可直接录入条码

  * 单结果扫码命中后，购物车数量和金额立即刷新

  * 多结果扫码时弹出候选选择器，用户选中后成功加购

  * 同一条码连续扫描可连续累加

  * 未选分店时阻止加购并提示

  * 条码不存在时显示失败态且不崩溃

  * 成功、未命中、多结果、失败或阻止四类结果的提示音可明显区分

  * iOS 首次完成音频解锁后，后续扫码结果提示音可正常播放

  * 聚焦搜索框后，扫码不会劫持正常输入

### Backend

* 构建或至少编译发布工作树对应后端项目，确认 `StoreOrderReactService` 改动无编译错误。

* 用同一组“条码值与货号值可能冲突”的样本请求新的扫码接口：

  * 变更后确认条码命中结果优先来自 `barcode`

  * 若同条码对应多个商品，接口返回完整候选集

  * 确认现有 `/products/batch-lookup` 行为不回归，仓库粘贴导入仍可正常使用

### Acceptance Criteria

* 用户可以把 `/shop` 安装成类似 App 的入口。

* Android 与 iOS 都能以各自系统支持的方式将 `/shop` 安装到主屏幕并进入独立壳层。

* 用户进入首页后，无需点击任何输入框即可直接扫码。

* 每扫一次，系统按条码找到商品；单结果直接加购，多结果先让用户选择。

* 加购数量遵循 `minOrderQuantity > 0 ? minOrderQuantity : 1`。

* 前台与后端对“按照条码”的语义保持一致，不因货号同值而误命中。

* 不同查询结果具备清晰且可区分的声音反馈。

* 扫码能力范围仅限外接扫码枪键盘模式，不要求摄像头扫码。
