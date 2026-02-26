## UserProfile 国际化实施计划

### 1. 提取待翻译字符串
从 `src/pages/UserProfile/index.tsx` 中提取以下字符串：

**通用/导航：**
- "个人中心" -> `userProfile.title`
- "基本信息" -> `userProfile.basicInfo`
- "修改密码" -> `userProfile.changePassword`

**表单标签：**
- "用户名" -> `user.username` (复用)
- "角色" -> `user.roles` (复用)
- "创建时间" -> `user.createdAt` (复用)
- "全名" -> `user.fullName` (复用)
- "邮箱" -> `user.email` (复用)
- "当前密码" -> `userProfile.currentPassword`
- "新密码" -> `userProfile.newPassword`
- "确认新密码" -> `userProfile.confirmNewPassword`

**Placeholder/提示：**
- "请输入全名" -> `user.form.fullName.placeholder` (复用)
- "请输入邮箱" -> `user.form.email.placeholder` (复用)
- "请输入当前密码" -> `userProfile.form.currentPassword.placeholder`
- "请输入新密码" -> `userProfile.form.newPassword.placeholder`
- "请再次输入新密码" -> `userProfile.form.confirmNewPassword.placeholder`

**校验消息：**
- "最长50个字符" -> `validation.maxLength` (复用)
- "请输入邮箱" -> `validation.required` (复用)
- "邮箱格式不正确" -> `validation.email` (复用)
- "请输入当前密码" -> `validation.required` (复用)
- "请输入新密码" -> `validation.required` (复用)
- "密码长度至少6位" -> `validation.minLength` (复用)
- "请确认新密码" -> `validation.required` (复用)
- "两次输入的密码不一致" -> `validation.passwordMismatch` (复用)

**操作反馈：**
- "保存修改" -> `common.save` (复用)
- "修改密码" -> `userProfile.action.changePassword`
- "获取用户信息失败" -> `userProfile.message.fetchFailed`
- "个人信息更新成功" -> `userProfile.message.updateSuccess`
- "更新失败" -> `userProfile.message.updateFailed`
- "密码修改成功，请重新登录" -> `userProfile.message.passwordChangeSuccess`
- "密码修改失败" -> `userProfile.message.passwordChangeFailed`

### 2. 更新语言文件
- **zh-CN.ts**: 添加上述 `userProfile.*` 相关的中文翻译。
- **en-US.ts**: 添加上述 `userProfile.*` 相关的英文翻译。

### 3. 组件代码改造
- 引入 `useIntl` 钩子。
- 使用 `intl.formatMessage({ id: '...' })` 替换所有硬编码中文。
- 替换 JSX 中的文本为 `<FormattedMessage id="..." />` 或 `intl.formatMessage`。

### 4. 路由与菜单国际化
- 确认路由配置中的 `name: 'profile'` 对应的国际化键 `menu.profile` 是否已存在或需要添加。

---
**实施步骤：**
1.  在 `zh-CN.ts` 和 `en-US.ts` 中添加新的翻译键。
2.  修改 `UserProfile/index.tsx`，引入 `useIntl` 并替换文本。
3.  验证页面显示。