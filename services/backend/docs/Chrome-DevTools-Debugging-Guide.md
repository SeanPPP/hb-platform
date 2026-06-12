# Chrome DevTools 调试指南 - HB Platform

## 目录
- [DevTools 简介](#devtools-简介)
- [快速开始](#快速开始)
- [元素面板 (Elements)](#元素面板-elements)
- [控制台面板 (Console)](#控制台面板-console)
- [网络面板 (Network)](#网络面板-network)
- [性能分析 (Performance)](#性能分析-performance)
- [应用面板 (Application)](#应用面板-application)
- [常见问题排查](#常见问题排查)
- [最佳实践](#最佳实践)

---

## DevTools 简介

Chrome DevTools 是 Chrome 浏览器内置的开发者工具，提供了强大的调试、分析和优化功能。对于 Blazor WebAssembly 应用，DevTools 是排查前端问题的重要工具。

### 打开 DevTools
- Windows/Linux: `F12` 或 `Ctrl + Shift + I`
- macOS: `Cmd + Option + I`
- 右键页面 → 选择"检查"

---

## 快速开始

### DevTools 面板概览

| 面板 | 用途 | 常用场景 |
|------|------|----------|
| Elements | 查看和编辑 HTML/CSS | UI 布局问题、样式调试 |
| Console | JavaScript 日志和交互 | 错误排查、变量查看 |
| Sources | 调试 JavaScript 代码 | 断点调试、源码查看 |
| Network | 监控网络请求 | API 调试、性能分析 |
| Performance | 性能分析 | 卡顿问题、渲染优化 |
| Application | 本地存储、缓存 | LocalStorage、Cookie 调试 |
| Lighthouse | 网站质量审计 | SEO、性能、可访问性 |

---

## 元素面板 (Elements)

### 1. 检查 HTML 结构

#### 基本操作
1. **选择元素**
   - 点击左上角"选择元素"图标 (Ctrl+Shift+C)
   - 在页面上点击要检查的元素
   - 或在 Elements 面板直接展开 HTML 树

2. **查看元素属性**
   ```html
   <!-- 示例：查看表格行的数据绑定 -->
   <tr data-row-key="123" class="ant-table-row">
       <td>订单编号</td>
       <td>客户名称</td>
   </tr>
   ```

3. **实时编辑 HTML**
   - 双击元素内容可直接编辑
   - 右键 → "Edit as HTML" 进行大段编辑
   - 拖拽元素改变位置

### 2. 调试 CSS 样式

#### 查看计算样式
```css
/* Computed 标签页显示最终应用的样式 */
.ant-table {
    display: table;
    width: 100%;
    border-collapse: separate;
    /* ... */
}
```

#### 样式优先级调试
- ✅ 未被划掉 = 当前生效
- ~~划掉的样式~~ = 被更高优先级覆盖
- `!important` = 最高优先级

#### 实时修改样式
```css
/* 在 Styles 面板直接添加/修改 */
.ant-btn-primary {
    background-color: #667eea !important;  /* 测试新颜色 */
    border-radius: 8px;                     /* 调整圆角 */
}
```

### 3. 响应式设计调试

#### 设备模拟
1. 点击"Toggle device toolbar" (Ctrl+Shift+M)
2. 选择预设设备或自定义尺寸
3. 测试不同屏幕尺寸的布局

#### 常用断点测试
```
- iPhone SE: 375×667
- iPhone 12 Pro: 390×844
- iPad: 768×1024
- iPad Pro: 1024×1366
- Desktop: 1920×1080
```

### 4. Box Model 调试

#### 查看盒模型
- 选中元素后查看 Styles 面板底部
- 蓝色 = 内容区域
- 绿色 = 内边距 (padding)
- 橙色 = 外边距 (margin)
- 灰色 = 边框 (border)

---

## 控制台面板 (Console)

### 1. 查看日志和错误

#### Blazor 常见错误信息
```javascript
// WASM 加载错误
Failed to fetch dynamically imported module: 
    https://example.com/_framework/System.Text.Json.wasm

// JavaScript 互操作错误
Uncaught (in promise): Microsoft.JSInterop.JSException: 
    Object doesn't support property or method 'getElementById'

// API 请求错误
Failed to load resource: the server responded with a status of 401 (Unauthorized)
```

#### 日志级别筛选
- `All`: 显示所有消息
- `Errors`: 只显示错误
- `Warnings`: 警告和错误
- `Info`: 信息、警告和错误
- `Verbose`: 详细日志

### 2. 交互式调试

#### 基本命令
```javascript
// 查看元素
$0                          // 当前选中的元素
$('.ant-table')             // jQuery 风格选择器
document.querySelector()    // 标准 DOM API

// 查看变量（需要在 Blazor 中通过 JSInterop 暴露）
window.blazorApp            // 自定义的全局对象
localStorage.getItem('token')  // 查看本地存储

// 性能测量
console.time('operation')
// ... 执行操作
console.timeEnd('operation')  // 输出: operation: 123.45ms
```

#### Console API
```javascript
// 分组日志
console.group('订单处理');
console.log('步骤1: 验证订单');
console.log('步骤2: 计算金额');
console.groupEnd();

// 表格显示
console.table([
    { id: 1, name: '订单A', amount: 100 },
    { id: 2, name: '订单B', amount: 200 }
]);

// 断言
console.assert(order.amount > 0, '订单金额必须大于0');

// 追踪调用栈
console.trace('追踪函数调用');
```

### 3. 监控网络请求

#### 查看 Fetch/XHR 请求
```javascript
// 在 Console 中捕获所有 fetch 请求
(function() {
    const originalFetch = window.fetch;
    window.fetch = function(...args) {
        console.log('Fetch:', args[0]);
        return originalFetch.apply(this, args)
            .then(response => {
                console.log('Response:', response.status, response.statusText);
                return response;
            });
    };
})();
```

---

## 网络面板 (Network)

### 1. 监控 API 请求

#### 查看请求详情
```
Name: /api/orders?pageIndex=1&pageSize=10
Status: 200 OK
Type: fetch
Size: 12.3 KB
Time: 245ms
```

#### Headers 标签页
```http
Request Headers:
    Authorization: Bearer eyJhbGc...
    Content-Type: application/json
    Accept: application/json

Response Headers:
    Content-Type: application/json; charset=utf-8
    X-Total-Count: 150
```

#### Preview/Response 标签页
```json
{
    "success": true,
    "data": [
        {
            "id": 1,
            "orderNumber": "ORD-2024-001",
            "customerName": "客户A",
            "totalAmount": 1500.00
        }
    ],
    "total": 150
}
```

### 2. 过滤和搜索

#### 常用过滤器
- `Fetch/XHR`: 只显示 API 请求
- `Doc`: 文档资源
- `CSS`: 样式表
- `JS`: JavaScript 文件
- `Img`: 图片资源

#### 搜索功能
- `Ctrl+F`: 搜索请求 URL 或响应内容
- `domain:api.example.com`: 按域名过滤
- `status-code:200`: 按状态码过滤
- `larger-than:100K`: 按大小过滤

### 3. 性能分析

#### 瀑布图分析
```
|--- Queueing (排队): 请求等待发送
|--- Stalled (停滞): 浏览器限制并发
|--- DNS Lookup (DNS查询): 域名解析
|--- Initial Connection (初始连接): TCP 握手
|--- SSL: SSL/TLS 握手
|--- Request Sent (请求发送): 发送请求数据
|--- Waiting (TTFB): 等待首字节
|--- Content Download (内容下载): 接收响应
```

#### 识别性能瓶颈
- **DNS 时间长** → 使用 CDN 或 DNS 预解析
- **连接时间长** → 启用 HTTP/2，减少请求数
- **TTFB 时间长** → 优化后端性能，增加缓存
- **下载时间长** → 压缩资源，使用 CDN

### 4. 模拟网络条件

#### 网络限速
```
Settings → Throttling
- Slow 3G: 400 Kbps, 400ms RTT
- Fast 3G: 1.6 Mbps, 150ms RTT
- Slow 4G: 4 Mbps, 20ms RTT
```

#### 离线模式
- 勾选 "Offline" 模拟断网情况
- 测试应用的离线处理能力

---

## 性能分析 (Performance)

### 1. 录制性能分析

#### 操作步骤
1. 点击 "Record" 按钮（圆形图标）
2. 执行需要分析的操作（如加载页面、滚动、点击）
3. 点击 "Stop" 停止录制
4. 分析生成的性能报告

### 2. 分析帧率 (FPS)

#### 识别卡顿问题
```
理想帧率: 60 FPS (每帧 16.67ms)
卡顿: < 30 FPS
严重卡顿: < 15 FPS
```

#### 查看帧详情
- 绿色条 = 流畅 (60 FPS)
- 黄色条 = 轻微卡顿 (30-60 FPS)
- 红色条 = 严重卡顿 (< 30 FPS)

### 3. 分析主线程活动

#### 常见性能问题
```javascript
// 长任务 (Long Task) - 超过 50ms
function processLargeData(data) {
    // ❌ 阻塞主线程
    for (let i = 0; i < 1000000; i++) {
        // 密集计算
    }
}

// ✅ 优化方案：分批处理
async function processLargeDataOptimized(data) {
    const batchSize = 1000;
    for (let i = 0; i < data.length; i += batchSize) {
        processBatch(data.slice(i, i + batchSize));
        await new Promise(resolve => setTimeout(resolve, 0));
    }
}
```

### 4. 内存泄漏检测

#### 使用 Memory 面板
1. 切换到 Memory 标签
2. 选择 "Heap snapshot"
3. 执行操作前后各拍一次快照
4. 比较两次快照，查找未释放的对象

---

## 应用面板 (Application)

### 1. 本地存储 (Local Storage)

#### 查看存储数据
```javascript
// 查看 JWT Token
Key: token
Value: eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...

// 查看用户信息
Key: userInfo
Value: {"userId":123,"userName":"admin","role":"Admin"}
```

#### 操作
- 双击值可编辑
- 右键 → Delete 删除
- 右键 → Clear All 清空所有

### 2. Session Storage

#### 临时数据存储
```javascript
// 页面状态
Key: currentPage
Value: 1

// 搜索条件
Key: searchCriteria
Value: {"keyword":"订单","status":"pending"}
```

### 3. Cookies

#### 查看 Cookie
```
Name: .AspNetCore.Antiforgery
Value: CfDJ8...
Domain: localhost
Path: /
Expires: Session
Size: 256
HttpOnly: ✓
Secure: ✓
SameSite: Lax
```

#### Cookie 问题排查
- **Cookie 未设置** → 检查后端 Set-Cookie 响应头
- **Cookie 丢失** → 检查过期时间和 SameSite 属性
- **跨域 Cookie** → 确保 SameSite=None 和 Secure=true

### 4. Cache Storage

#### 查看缓存资源
- Service Worker 缓存
- PWA 离线资源
- API 响应缓存

---

## 常见问题排查

### 1. 页面加载缓慢

#### 排查步骤
1. **打开 Network 面板** → 记录页面加载
2. **查看瀑布图** → 找出耗时最长的请求
3. **检查资源大小** → 识别大文件
4. **查看 Timing** → 分析请求各阶段时间

#### 常见原因
- ❌ WASM 文件过大 → 启用 Gzip/Brotli 压缩
- ❌ 未使用 CDN → 静态资源使用 CDN
- ❌ 阻塞资源 → 异步加载 JS，延迟加载图片
- ❌ 过多请求 → 合并请求，使用雪碧图

### 2. API 请求失败

#### 401 Unauthorized
```javascript
// 检查 Authorization header
Request Headers:
    Authorization: Bearer <token>

// 问题排查
1. Token 是否存在？ localStorage.getItem('token')
2. Token 是否过期？ 检查 JWT 过期时间
3. Token 格式是否正确？ 应该是 "Bearer " + token
```

#### 404 Not Found
```javascript
// 检查请求 URL
Request URL: https://api.example.com/api/orders/123

// 问题排查
1. URL 是否正确？ 检查拼写和参数
2. 路由是否配置？ 后端是否有对应的 Controller
3. API 版本是否匹配？ /api/v1/orders vs /api/orders
```

#### 500 Internal Server Error
```javascript
// 查看响应详情
Response:
{
    "type": "https://tools.ietf.org/html/rfc7231#section-6.6.1",
    "title": "An error occurred while processing your request.",
    "status": 500,
    "traceId": "00-abc123..."
}

// 问题排查
1. 查看后端日志 → 找到 traceId 对应的错误
2. 检查请求数据 → 是否符合后端验证规则
3. 数据库连接 → 是否正常
```

### 3. CORS 跨域错误

#### 错误信息
```
Access to fetch at 'https://api.example.com/orders' from origin 'https://app.example.com' 
has been blocked by CORS policy: No 'Access-Control-Allow-Origin' header is present 
on the requested resource.
```

#### 解决方案
```csharp
// 后端配置 CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", builder =>
    {
        builder.WithOrigins("https://app.example.com")
               .AllowAnyMethod()
               .AllowAnyHeader()
               .AllowCredentials();
    });
});
```

### 4. 样式不生效

#### 排查步骤
1. **Elements 面板** → 选中元素
2. **Styles 标签** → 查看样式是否被覆盖
3. **Computed 标签** → 查看最终计算样式
4. **Sources 面板** → 确认 CSS 文件已加载

#### 常见原因
- ❌ 选择器优先级低 → 使用更具体的选择器或 !important
- ❌ 样式被覆盖 → 检查是否有其他样式覆盖
- ❌ CSS 文件未加载 → 检查 Network 面板
- ❌ 缓存问题 → 硬刷新 (Ctrl+Shift+R)

### 5. JavaScript 错误

#### Null Reference Error
```javascript
// 错误
Uncaught TypeError: Cannot read properties of null (reading 'id')
    at OrderList.razor:45

// 排查
1. 检查对象是否存在
2. 使用可选链操作符: order?.id
3. 添加空值检查: if (order != null)
```

#### JSInterop 错误
```javascript
// 错误
Microsoft.JSInterop.JSException: 
    Could not find 'myFunction' ('myFunction' was undefined).

// 排查
1. 确认 JS 函数已定义
2. 检查函数名拼写
3. 确认 JS 文件已加载
4. 查看 Sources 面板确认函数存在
```

---

## 最佳实践

### 1. 开发时常开 DevTools
```javascript
// 设置默认打开 DevTools
// Chrome 快捷方式添加参数:
--auto-open-devtools-for-tabs
```

### 2. 使用 Source Maps
```javascript
// Blazor 发布时启用 Source Maps
<PropertyGroup>
    <BlazorEnableCompression>false</BlazorEnableCompression>
    <BlazorEnableTimeZoneSupport>false</BlazorEnableTimeZoneSupport>
</PropertyGroup>
```

### 3. 保存有用的代码片段
```
Sources → Snippets → 右键 → New Snippet

// 示例：清空所有 localStorage
localStorage.clear();
sessionStorage.clear();
console.log('存储已清空');
```

### 4. 使用 Workspace
```
Sources → Filesystem → Add folder to workspace

好处:
- 直接在 DevTools 中编辑文件
- 保存更改到本地文件系统
- 实时预览效果
```

### 5. 网络请求录制
```
Network → 右键请求 → Copy → Copy as fetch/cURL

// 生成的 fetch 代码
fetch("https://api.example.com/orders", {
  "headers": {
    "authorization": "Bearer token...",
    "content-type": "application/json"
  },
  "body": "{\"pageIndex\":1}",
  "method": "POST"
});
```

### 6. 设备模式快捷键
```
Ctrl+Shift+M: 切换设备模式
Ctrl+Shift+R: 旋转设备
Ctrl+Shift+P: 打开命令面板
```

### 7. 命令面板 (Command Palette)
```
Ctrl+Shift+P 或 Cmd+Shift+P

常用命令:
- Screenshot: 截图
- Coverage: 代码覆盖率
- Rendering: 渲染设置
- Sensors: 模拟传感器
```

---

## 调试工作流示例

### 场景：订单列表加载缓慢

#### 1. 初步分析
```
打开 Network 面板 → 刷新页面 → 查看请求时间
发现: /api/orders 请求耗时 3.5 秒
```

#### 2. 详细检查
```
点击请求 → Timing 标签

分析:
- Waiting (TTFB): 3.2 秒  ← 后端处理慢
- Content Download: 0.3 秒
```

#### 3. 查看响应数据
```
Preview 标签:
{
    "data": [...1000条记录...],
    "total": 10000
}

问题: 一次性返回太多数据
```

#### 4. 解决方案
```csharp
// 实施分页
var orders = await _orderService.GetPagedAsync(
    pageIndex: 1,
    pageSize: 20  // 减少每页数量
);

// 添加缓存
[ResponseCache(Duration = 60)]
public async Task<IActionResult> GetOrders() { ... }

// 优化查询
var orders = await _context.Orders
    .AsNoTracking()  // 不跟踪实体
    .Include(o => o.Customer)
    .Select(o => new OrderDto { ... })  // 只选择需要的字段
    .ToListAsync();
```

#### 5. 验证优化效果
```
再次测试:
- 请求时间: 0.8 秒 (改善 77%)
- 数据量: 2.5 KB (减少 90%)
```

---

## 快速参考

### 常用快捷键

| 操作 | Windows/Linux | macOS |
|------|---------------|-------|
| 打开 DevTools | F12 / Ctrl+Shift+I | Cmd+Opt+I |
| 元素选择器 | Ctrl+Shift+C | Cmd+Shift+C |
| 命令面板 | Ctrl+Shift+P | Cmd+Shift+P |
| 搜索文件 | Ctrl+P | Cmd+P |
| 搜索内容 | Ctrl+Shift+F | Cmd+Opt+F |
| 切换设备模式 | Ctrl+Shift+M | Cmd+Shift+M |
| 硬刷新 | Ctrl+Shift+R | Cmd+Shift+R |
| 控制台 | Ctrl+` | Ctrl+` |

### Console API 速查

| 方法 | 用途 |
|------|------|
| `console.log()` | 普通日志 |
| `console.error()` | 错误日志 |
| `console.warn()` | 警告日志 |
| `console.table()` | 表格显示 |
| `console.time()` | 计时开始 |
| `console.timeEnd()` | 计时结束 |
| `console.group()` | 分组开始 |
| `console.groupEnd()` | 分组结束 |
| `console.clear()` | 清空控制台 |

---

## 扩展阅读

### 官方文档
- [Chrome DevTools 官方文档](https://developer.chrome.com/docs/devtools/)
- [Blazor 调试指南](https://learn.microsoft.com/zh-cn/aspnet/core/blazor/debug)

### 相关工具
- **React DevTools**: React 应用调试
- **Vue DevTools**: Vue 应用调试
- **Redux DevTools**: Redux 状态管理调试
- **Lighthouse**: 网站质量审计工具

---

## 更新日志

### v1.0.0 (2024-10-03)
- 初始版本
- 覆盖主要 DevTools 面板使用
- 提供常见问题排查指南
- 添加 Blazor 特定调试技巧

