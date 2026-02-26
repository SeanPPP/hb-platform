## 原因
- 后端返回的字段为 HGUID（PascalCase），前端类型与使用处为 hGuid（camelCase）。
- 在编辑、删除、批量删除和表格 rowKey 处读取 hGuid，导致 undefined，URL 拼接成 /undefined。

## 方案
### 1. 前端容错读取键名
- 在页面 index.tsx：
  - 将 Table 的 rowKey 改为 (r) => r.hGuid ?? r.HGUID。
  - 更新/删除/批量删除调用处使用 r.hGuid ?? r.HGUID。

### 2. 服务层统一映射（可选增强）
- 在 cashRegisterUser.ts 的 getGrid/getByHGuid 返回后，将 HGUID 映射为 hGuid，保证全站一致：
  - 列表：res.data.items = items.map(i => ({ ...i, hGuid: i.hGuid ?? i.HGUID }))。
  - 详情：res.data = { ...data, hGuid: data.hGuid ?? data.HGUID }。

### 3. 后端全局 CamelCase（备选）
- 可在 ASP.NET Core 的 System.Text.Json 采用 CamelCase 命名策略，统一输出为 camelCase，减少前端适配工作；本次不修改后端，仅作为后续优化建议。

## 验证
- 列表能正常选择行；编辑和删除请求的 hGuid 不再为 undefined；批量删除 URL 与过滤正常。