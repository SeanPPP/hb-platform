# Expo 分店用户 POS 终端授权设计

## 目标

在 Expo 的现有“用户管理”流程中，为有权限的店长提供按“用户 + 分店”修改 POS 终端业务权限的能力，并完整复用后端已经上线的全局权限继承、分店覆盖和恢复继承模型。

成功标准：店长只能管理自己可管理分店中的普通用户；可以查看当前生效权限、保存分店级覆盖、恢复全局权限继承；管理员保持相同入口并可看到后端允许其分配的完整权限集合。移动端对管理员也采用“只编辑普通用户”的保守边界，避免高权限账号的隐式全权限语义造成误导。

## 范围

本次只新增 Expo 移动端入口、授权页面、API 封装、状态逻辑、国际化和测试。后端权限表、控制器、DTO、导航模型和 Web 页面不修改；不增加角色分配、菜单授权、后台系统权限或跨分店批量授权。

## 入口与访问控制

- 在现有用户卡片操作区增加“POS 授权”按钮，进入独立全屏页面。
- 入口仅在当前账号拥有 `Users.ManagePosTerminalPermissions`，且目标用户属于当前选中的可管理分店时显示。
- `storeGuid` 必须来自当前选中 `Store` 的 `storeGUID`；不得用 `storeCode` 代替。若选中分店缺少 `storeGUID`，授权入口禁用并提示刷新分店资料或重新登录，页面不得发起请求。
- 页面接收 `userGuid`、`storeGuid`、员工显示名和分店显示名；真正授权边界仍由后端校验，路由参数不能扩大权限。
- 当前分店是授权上下文，页面内不提供分店切换。用户要修改另一分店时，必须返回用户列表切换分店后重新进入。
- 店长和管理员在移动端都只修改普通用户，不能修改自己或高权限账号。高权限角色按后端别名对齐：`Admin`、`管理员`、`SuperAdmin`、`超级管理员`、`StoreManager`、`店长`、`经理`、`WarehouseManager`、`仓库经理`、`仓库管理员`、`WarehouseAdmin`。入口根据用户列表的 `roleNames` 和当前账号 `userGUID` 预先隐藏；后端 403 仍是最终授权边界。

## 页面与交互

采用 React Native Paper 的独立全屏页面，保持现有企业工具风格：低动效、中等信息密度、清晰的保存与恢复边界。

- 顶部固定显示返回按钮、员工名称、分店名称，以及“继承全局权限”或“分店单独覆盖”状态标签。
- 权限按后端返回的 `group` 分组；每项显示 `name`、`description` 和开关，分组标题提供“全选/清空”。
- 页面初始选择值取 `effectivePermissionCodes` 与 `assignablePermissions` 的交集。因此继承模式下看到的是当前真实生效权限，第一次保存会创建分店覆盖。
- 保存时只提交当前页面可分配权限的选中代码，调用现有 PUT。保存成功后使用响应重新初始化页面，避免本地状态与服务端不一致。
- 覆盖模式显示“恢复继承”操作；二次确认后调用现有 DELETE，并使用返回的继承状态重新初始化页面。
- 未产生修改时禁用保存。加载、保存、恢复期间禁用相关操作，防止重复请求。
- 离开存在未保存修改的页面时弹出确认，允许继续编辑或放弃修改。

## 数据与接口

移动端新增与后端 DTO 对齐的类型：

- 可分配权限：`code`、`name`、`group`、`description`。
- 授权响应：`mode`、`assignablePermissions`、`inheritedPermissionCodes`、`overriddenPermissionCodes`、`grantedPermissionCodes`、`effectivePermissionCodes`。
- 更新请求：`grantedPermissionCodes: string[]`。

复用现有后端接口，不新增 API。移动端 `apiClient` 的 `baseURL` 已包含 `/api`，因此调用路径必须是以下形式，禁止再次拼接 `/api`：

- `GET /Users/guid/{userGuid}/stores/{storeGuid}/pos-terminal-permissions`
- `PUT /Users/guid/{userGuid}/stores/{storeGuid}/pos-terminal-permissions`
- `DELETE /Users/guid/{userGuid}/stores/{storeGuid}/pos-terminal-permissions`

API 层负责路径参数编码、统一解包和响应规范化；React Query hook 负责查询、保存、恢复以及成功后的缓存更新。选中状态和脏状态保留在页面本地，不写入全局 auth store。

## 错误与边界

- 401：沿用现有会话失效处理。
- 403：显示“无权管理该用户或分店”，禁用编辑并提供返回用户列表操作。
- 404：显示用户、分店或关联关系已变化，允许返回并刷新列表。
- 网络或其他错误：保留本地草稿，显示可重试错误；保存失败不得覆盖当前选择。
- 空权限集合允许保存，语义为该分店下拒绝所有可分配 POS 业务权限。
- 后端返回未知权限代码时忽略其开关展示；提交只能包含 `assignablePermissions` 中的代码。

## 测试与验收

- 单元测试覆盖响应规范化、有效权限与可分配权限求交集、分组全选/清空、脏状态以及提交代码白名单。
- API 测试覆盖 GET/PUT/DELETE 路径编码、请求体、响应解包和错误透传，并断言实际请求不会形成重复的 `/api/api` 前缀。
- 访问控制测试覆盖：无管理权限不显示入口、只读分店不显示入口、可管理分店显示入口、缺少 `storeGUID` 时禁用入口且不以 `storeCode` 降级。
- 目标用户测试覆盖：普通用户可进入，当前账号自己和上述高权限角色不显示入口；同时覆盖列表角色信息滞后时后端返回 403 的页面状态。
- 页面测试覆盖：继承状态初始化、保存覆盖、恢复继承确认、空权限保存、失败保留草稿和防重复提交。
- TypeScript 检查和现有 mobile 测试必须通过；在 iOS 与 Android 至少各做一次人工 smoke test，核对长权限列表滚动、返回未保存确认和窄屏布局。

## 实施约束

- 关键业务逻辑添加中文注释。
- 不向现有 800 行用户页继续塞入授权状态机；权限 API、纯逻辑和授权页面分别保持单一职责。
- 不复制 Web 页面组件，只复用其稳定业务语义：以 `effectivePermissionCodes` 初始化、仅提交可分配代码、DELETE 恢复继承。
- 不新增依赖，沿用 Expo Router、React Query、React Native Paper 和现有 API client。
