## 问题
- 后端插入收银用户时报错：Remark 列不允许为 NULL，当前 Create/Update 将 dto.Remark 原样写入，导致为空时违反约束。

## 方案
- 后端收银用户服务：
  - CreateAsync：将 Remark 赋值改为 dto.Remark ?? string.Empty
  - UpdateAsync：将 entity.Remark 赋值改为 dto.Remark ?? string.Empty
- 可选加固：前端创建/更新时将 remark 未填值传递为空字符串，进一步降低后端适配压力（后端修复后可不改前端）。

## 验证
- 前端创建/编辑在不填备注时成功；数据库列 Remark 保存为空字符串；其它字段正常。