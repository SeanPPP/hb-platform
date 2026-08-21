# HB Supplier Order 扩展

Chrome / Edge / macOS Safari Manifest V3 扩展，在已配置的供应商列表页为每个商品注入“上次订货日期/数量、至今销量”按钮，点击后在 Chrome/Edge 侧栏或 Safari 浮动助手窗口查看该商品 12 个月内最多 6 次采购周期（订货/销售混排）。

## 目录

- `src/`：扩展源码（不含构建产物与凭据）
- `src/lib/`：可测试的纯逻辑模块（版本、transform、profile 校验、微批、节点状态、分页/混排、握手、鉴权判定、single-flight refresh、i18n）
- `src/background/service-worker.js`：统一请求、令牌存储、single-flight refresh、消息路由、动态内容脚本注册
- `src/content/shop-bridge.js`：`/shop` 页面桥接（PING/OPEN）
- `src/content/list.js`：供应商列表页注入
- `src/sidepanel/`：侧栏 UI
- `test/`：Node 原生测试
- `build.mjs`：三浏览器构建脚本；Safari 后台由 esbuild 打成 classic service worker
- `dist/chrome`、`dist/edge`、`dist/safari`：同版本构建产物
- `../supplier-order-safari-extension/`：Safari Web Extension 的 Xcode 宿主项目

## 环境要求

- Node.js 20+（测试使用原生 `node:test`，构建仅依赖 esbuild）

## 本地加载

先构建：

```bash
npm ci
npm test
npm run build
```

### Chrome

1. 打开 `chrome://extensions`。
2. 打开右上角“开发者模式”。
3. 点击“加载已解压的扩展程序”，选择 `dist/chrome`。

### Edge

1. 打开 `edge://extensions`。
2. 打开左下角“开发人员模式”。
3. 点击“加载解压缩的扩展”，选择 `dist/edge`。

### Safari

Safari 使用相邻的独立 Xcode 宿主项目，构建、签名和启用步骤见 `../supplier-order-safari-extension/README.md`。Safari 16.4+ 没有 Chrome Side Panel API，因此会打开并复用一个 440×800 的浮动助手窗口，其余业务功能和权限流程保持一致。

加载后：

1. 点击扩展图标打开侧栏，在“后端接口”中选择“远端 /”或“本地 5002”；也可以填写自定义 HTTPS origin。
2. 登录 HB 账号。
3. 若当前用户返回了 `stores`，选择门店；否则手动输入门店编码并保存。
4. 在“供应商”列表对 DATS origin 点击“授权”（需用户手势，仅申请该供应商 origin 的可选权限）。
5. 打开 `https://www.dats.com.au/` 的列表页，商品卡片下方会出现按钮；点击按钮在侧栏定位到该 `supplierCode/itemNumber`。

后端接口快捷设置：

- “远端 /”恢复到构建时的 `HB_API_ORIGIN`，正式构建默认为 `https://hotbargain.vip`。
- “本地 5002”使用 `http://localhost:5002`，首次选择时浏览器会请求 localhost 可选权限。
- 自定义地址只接受 HTTPS origin；本机调试额外允许 `localhost` 或 `127.0.0.1` HTTP，不能包含路径、查询、片段或用户名密码。
- 接口地址发生变化时，扩展会清除当前 access/refresh token 和供应商配置缓存，必须在新环境重新登录，避免跨环境传递凭据。

## 权限边界

- `host_permissions` 仅包含 HB API 正式源；`/shop` 桥接脚本仅注入 HB Web 正式源。
- `optional_host_permissions` 默认覆盖 HTTPS 供应商；TXK 因现站仅提供 HTTP，额外只允许精确的 `http://txkorders.inzantsales.com/*`，仍需在侧栏由用户逐供应商授权。
- 配置只解释声明式 selector / attribute / text，以及内置固定 transform（包括 GFA 的下划线转斜线、TXK 的固定 SKU 前缀提取）；绝不 `eval` / `Function` / 后台任意正则 / 远程 JS。
- access token 存 `chrome.storage.session`，refresh token 存 `chrome.storage.local`，不保存密码。

## 三浏览器构建

`build.mjs` 生成 `dist/chrome`、`dist/edge` 与 `dist/safari`，三个 manifest 的 `version` 相同（当前 1.2.0）。Web 与 API 同源时只需设置 API 源；分离部署时分别设置：

```bash
HB_API_ORIGIN=https://staging.example.com npm run build
HB_WEB_ORIGIN=https://staff.example.com HB_API_ORIGIN=https://api.example.com npm run build
```

构建会做 manifest JSON 语法校验，并在最后校验三个包版本一致；Safari manifest 额外锁定最低 Safari 16.4。

## HB 后端配置

参考 `services/backend/BlazorApp.Api/appsettings.BrowserExtension.example.json` 配置最新版、最低支持版、三个浏览器商店链接和声明式供应商 profile。部署时也可使用 ASP.NET Core 环境变量，例如：

```bash
BrowserExtension__LatestVersion=1.2.0
BrowserExtension__MinimumVersion=1.1.0
BrowserExtension__ChromeStoreUrl=https://chromewebstore.google.com/detail/...
BrowserExtension__EdgeStoreUrl=https://microsoftedge.microsoft.com/addons/detail/...
BrowserExtension__SafariStoreUrl=https://apps.apple.com/app/...
```

紧急停用内置 DATS 时将 `UseBuiltInDatsProfile` 设为 `false`；停用其余内置供应商时将 `UseBuiltInSupplierProfiles` 设为 `false`。单个供应商也可在 `SupplierProfiles` 中用相同 `SupplierCode` 配置 `Enabled: false` 覆盖。每次变更后递增 `ConfigVersion`，扩展下一次同步配置后会移除对应域名脚本，无需发新版。

## 发布

### Chrome Unlisted

1. 从构建目录内部打包，确保 `manifest.json` 位于 zip 根目录：`cd dist/chrome && zip -r ../hb-supplier-order-chrome.zip .`。
2. 在 Chrome Web Store 开发者后台创建/选择项目，上传 zip。
3. “可见性”选择“不公开（Unlisted）”，提交审核。
4. 审核通过后按后台给出的 Unlisted 链接分发。

### Edge Hidden

1. 从 `dist/edge` 目录内部打包，确保 `manifest.json` 位于 zip 根目录。
2. 在 Microsoft Partner Center 的扩展页面上传包。
3. 可见性选择“隐藏（Hidden）”，按需配置组织内分发或隐藏链接。

### Safari

使用 `../supplier-order-safari-extension` 的 Xcode 工程配置 Apple 开发团队并归档。Safari 16.4–18.3 通过 Mac App Store 分发；Developer ID 签名并公证的扩展要求 Safari 18.4+。正式链接生成前，后端 `SafariStoreUrl` 保持空值。

正式图标源稿位于 `assets/icon-master.svg`，浏览器所需的 16/32/48/128px PNG 位于 `src/icons/`，构建时会复制到三个浏览器产物；本仓库不包含任何凭据或商店密钥。

## 版本与更新策略

- 普通供应商（仅新增 `supplierCode/displayName/origins/选择器` 等声明式配置）通过服务端 `GET /api/react/v1/browser-extension/supplier-profiles` 下发，**无需发布新版**；当前后端默认目录包含 DATS、Brazco、Malmar、Meteor Party、Yatsal、Windragon、MNB、PJ SAS、Jemark、GFA、TXK 和 Boom Up，扩展离线回退仍只保留 DATS。
- 需要新增特殊逻辑（新的解析行为、交互或 API 契约变化）时，才需要修改扩展代码并发布新版。
- 三个浏览器包始终保持同一版本号（`build.mjs` 会校验）。

## 自动更新与补丁回滚

- 商店安装版由 Chrome Web Store / Edge Add-ons 自动推送更新，无需用户操作。
- 补丁流程：修改代码 → `npm test` 全绿 → `npm run build` 重新生成三包 → Safari 项目同步 Resources → 递增 `package.json` 版本 → 上传对应商店审核。
- 商店版不能降低版本号。回滚流程是恢复上一版本代码，递增一个更高的补丁版本，重新测试、构建并提交审核；本地加载版可直接重新加载旧构建目录。

## 测试

```bash
npm test
```

覆盖：semver/版本状态、profile 校验与安全 transform、微批去重与分页上限、动态节点状态纯逻辑、分页/筛选/混排、握手校验、鉴权判定、single-flight refresh、i18n。
