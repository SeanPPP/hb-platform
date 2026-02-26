## 问题
- 更新请求的 hGuid 仍为 undefined，导致 PUT /api/react/v1/cash-register-users/undefined
- 原因可能是编辑时记录对象不含 hGuid/HGUID 或在赋值后丢失
- 另有 Intl 缺失键：menu.posAdmin.cashRegisterUsers

## 方案
### 1. 规范化编辑记录的主键
- 在 handleEdit 时计算 const id = record.hGuid ?? record.HGUID
- 将规范化的 hGuid 注入到编辑态：setEditingRecord({ ...record, hGuid: id })
- 在 handleUpdate 前若 hGuid 不存在，提示错误并中止

### 2. 强化 rowKey 与删除/批量删除已完成无需再变更

### 3. 国际化
- 在 locales（zh-CN/en-US）增加 menu.posAdmin.cashRegisterUsers 文本，避免控制台告警（可选）

## 验证
- 打开编辑后更新成功，网络请求 URL 不再出现 undefined
- 控制台不再出现 Intl 缺失提示