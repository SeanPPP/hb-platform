# 代码审查检查清单

## 🎯 目的
确保代码质量，避免常见错误，特别是 SqlSugar 和 AntDesign 相关的兼容性问题。

## ✅ SqlSugar 相关检查

### 导航属性配置
- [ ] 导航属性是否使用了完整的参数配置
  ```csharp
  // ✅ 正确
  [Navigate(NavigateType.OneToMany, nameof(YIWU_OrderDetail.OrderNo), nameof(OrderNo))]
  
  // ❌ 错误
  [Navigate(NavigateType.OneToMany, nameof(OrderNo))]
  ```

- [ ] 外键字段类型是否匹配（避免 varchar 转 int 错误）
- [ ] 导航属性的命名是否符合规范
- [ ] 是否正确配置了一对多和多对一关系

### 数据库操作
- [ ] 是否使用了参数化查询
- [ ] 是否正确处理了异常
- [ ] 是否添加了适当的事务处理
- [ ] 查询性能是否合理（避免 N+1 问题）

## ✅ AntDesign 组件检查

### Table 组件
- [ ] 是否移除了不支持的属性
  ```razor
  <!-- ❌ 避免使用 -->
  ShowSizeChanger="true"
  ShowQuickJumper="true"
  
  <!-- ✅ 使用正确的分页配置 -->
  PageIndex="@currentPage"
  PageSize="@pageSize"
  Total="@totalCount"
  OnPageIndexChange="OnPageChanged"
  OnPageSizeChange="OnPageSizeChanged"
  ```

- [ ] PropertyColumn 是否处理了空值情况
- [ ] 数据绑定是否正确

### Button 组件
- [ ] 危险操作是否使用样式替代 Danger 属性
  ```razor
  <!-- ❌ 避免使用 -->
  <Button Danger>删除</Button>
  
  <!-- ✅ 正确方式 -->
  <Button Style="color: #ff4d4f;">删除</Button>
  ```

### Form 组件
- [ ] FormItem 是否包装在 Form 组件内
- [ ] Form 是否指定了正确的 Model 类型
  ```razor
  <!-- ✅ 正确结构 -->
  <Form Model="@model" TModel="ModelType">
      <FormItem Label="标签">
          <Input @bind-Value="model.Property" />
      </FormItem>
  </Form>
  ```

### MenuItem 组件
- [ ] 是否避免使用不支持的 Danger 属性
- [ ] 危险菜单项是否使用样式实现红色效果

## ✅ 代码质量检查

### 命名规范
- [ ] 类名使用 PascalCase
- [ ] 变量名使用 camelCase
- [ ] 常量使用 UPPER_CASE
- [ ] 文件名与类名一致

### 异常处理
- [ ] 是否有适当的 try-catch 块
- [ ] 异常信息是否对用户友好
- [ ] 是否记录了错误日志
- [ ] 是否避免了吞噬异常

### 性能考虑
- [ ] 是否避免了不必要的数据库查询
- [ ] 是否使用了适当的缓存策略
- [ ] 大数据集是否使用了分页
- [ ] 是否避免了内存泄漏

## ✅ 安全检查

### 数据验证
- [ ] 所有用户输入是否经过验证
- [ ] 是否防止了 SQL 注入
- [ ] 是否验证了文件上传类型和大小
- [ ] 敏感数据是否正确加密

### 授权检查
- [ ] API 端点是否有适当的授权属性
- [ ] 用户权限是否正确验证
- [ ] 敏感操作是否需要额外确认

## ✅ UI/UX 检查

### 响应式设计
- [ ] 页面是否支持移动端适配
- [ ] 表格是否支持横向滚动
- [ ] 弹窗是否在小屏幕上正常显示

### 用户体验
- [ ] 加载状态是否有适当的指示器
- [ ] 错误信息是否友好且具体
- [ ] 成功操作是否有反馈提示
- [ ] 关键操作是否有确认步骤

### 无障碍访问
- [ ] 是否支持键盘导航
- [ ] 是否有适当的 ARIA 标签
- [ ] 颜色对比度是否符合要求
- [ ] 是否支持屏幕阅读器

## ✅ 测试覆盖

### 功能测试
- [ ] 核心功能是否经过测试
- [ ] 边界条件是否考虑
- [ ] 错误情况是否处理
- [ ] 不同用户角色是否测试

### 兼容性测试
- [ ] 不同浏览器是否测试
- [ ] 不同屏幕尺寸是否测试
- [ ] 不同网络条件是否考虑

## 🔍 审查工具

### 自动检查命令
```bash
# 检查 AntDesign 不兼容属性
grep -r "ShowSizeChanger\|ShowQuickJumper\|Danger=\"true\"" BlazorApp/Pages/

# 检查导航属性配置
grep -A 2 -B 2 "Navigate.*nameof.*," BlazorApp.Shared/Models/

# 检查 FormItem 是否在 Form 外使用
grep -B 5 -A 5 "<FormItem" BlazorApp/Pages/ | grep -v "<Form"

# 编译检查
dotnet build --verbosity normal

# 静态代码分析
dotnet analyze
```

### IDE 扩展推荐
- SonarLint（代码质量检查）
- CodeMaid（代码整理）
- Roslynator（C# 代码分析）

## 📋 审查记录模板

### 审查报告格式
```markdown
## 代码审查报告

**审查人**: [姓名]
**审查日期**: [日期]
**涉及文件**: [文件列表]

### 发现的问题
1. [问题描述] - [严重程度: 高/中/低]
   - 位置: [文件:行号]
   - 建议: [修复建议]

### 优点
- [代码的优秀之处]

### 总体评价
- [ ] 通过 - 可以合并
- [ ] 需要修改 - 修复问题后重新审查
- [ ] 拒绝 - 需要重新设计

### 额外建议
[其他改进建议]
```

## 🎯 优先级指南

### 高优先级（必须修复）
- 编译错误
- 运行时异常
- 安全漏洞
- 数据丢失风险

### 中优先级（应该修复）
- 性能问题
- 用户体验问题
- 代码规范问题
- 维护性问题

### 低优先级（可以优化）
- 代码风格
- 注释完善
- 轻微的重构建议

## 📚 参考资源

- [AntDesign Blazor 兼容性指南](./AntDesign-Compatibility-Guide.md)
- [开发问题排查指南](./Development-Troubleshooting-Guide.md)
- [项目架构规范](../cursor-rules.md)

---

💡 **提示**: 代码审查不仅是发现问题，更是团队学习和知识分享的机会。保持建设性的态度，共同提高代码质量。