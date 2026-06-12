## 实施范围
- 前端：在“供应商管理”页新增“新建供应商”弹窗与创建流程；不输入编码，提交后端生成。
- 后端：在创建服务中按 `SP###`（三位零填充）规则自动生成唯一编码，事务内完成，并发安全；控制器返回编码供前端提示。

## 前端实现
- 页面 `src/pages/PosAdmin/SupplierManagement/index.tsx`
  - 工具栏新增按钮“新建供应商”。
  - 弹窗表单（Form vertical）：
    - 字段：`name`（必填 <=128）、`status`（Switch 默认启用）、`contactPerson`（<=64）、`phone`（<=32）、`email`（<=128 格式校验）、`remark`（<=256）
    - 不显示编码输入框（后端生成）。
  - 交互：提交调用服务 `createLocalSupplier(dto)` → 成功后 `message.success("创建成功：编码 SPxxx")` → 关闭弹窗并 `load()` 刷新列表（保留当前 `sortBy/sortOrder` 与分页）。
- 服务 `src/services/localSupplier.ts`
  - 新增方法 `createLocalSupplier(data)` → `POST /api/react/v1/local-suppliers`
  - 可选：唯一性校验 `checkCodeExists(code)` 暂不使用（后端自动生成）。

## 后端实现
- 服务层 `BlazorApp.Api/Services/React/LocalSupplierReactService.cs`
  - 在 `CreateAsync` 内生成编码：
    1. 开启事务：`await db.Ado.BeginTranAsync()`
    2. 查询最大编码：`var max = await db.Queryable<HBLocalSupplier>().Where(x => x.LocalSupplierCode.StartsWith("SP") && !x.IsDeleted).MaxAsync(x => x.LocalSupplierCode)`
    3. 解析数字：`var n = int.TryParse(max?.Substring(2), out var v) ? v : 0; var next = $"SP{(n + 1).ToString("D3")}"`
    4. 构造实体（其余字段来自 DTO，`LocalSupplierCode=next`），插入 → 提交事务
    5. 并发冲突处理：捕获唯一索引异常时回滚并重试一次（重新查询+生成）；仍失败则返回 `ApiResponse.Error("编码生成冲突","CODE_CONFLICT")`
  - 返回：`ApiResponse<LocalSupplierDto>` 包含生成编码与其它字段。
- 控制器 `BlazorApp.Api/Controllers/React/LocalSuppliersController.cs`
  - 保持 `POST /api/react/v1/local-suppliers` 接口，返回 `{ success, data, message }`。
- 上下文与索引（已完成）：
  - `SqlSugarContext` 已注册 `HBLocalSupplier` 与 CodeFirst 初始化；唯一索引 `IX_LocalSupplier_Code_Unique` 与普通索引（Name/Status）。

## 验证
- 连续创建两次：编码依次为 `SP001`、`SP002`；列表刷新显示新增记录。
- 并发创建：不出现重复编码；冲突时返回错误消息。
- 保持服务端排序与点击列排序联动；隔行色与内部滚动体验一致。

## 风险与注意
- 超过 `SP999` 后需扩展位数（改为四位或以上零填充），当前先按三位实现。
- 保持后端 JSON 序列化为 camelCase（`localSupplierCode`）；前端列字段使用 `localSupplierCode`。
- 前后端共同进行长度与格式校验（特别是 `email`）。

## 交付
- 完成上述前后端改动与联调，提交可运行版本；用户可在“供应商管理”页使用“新建供应商”并看到生成的 `SP` 编码与列表数据。