# 前端设计规范 - HB Platform多店铺订单管理系统

## 目录
- [设计原则](#设计原则)
- [UI组件规范](#ui组件规范)
- [响应式设计](#响应式设计)
- [色彩系统](#色彩系统)
- [排版规范](#排版规范)
- [交互规范](#交互规范)
- [无障碍访问](#无障碍访问)

---

## 设计原则

### 核心理念
**"简洁直观、高效专业、移动优先"**

### 设计特色
- 企业级B2B设计语言
- 移动优先响应式布局
- 数据密集型界面优化
- 多店铺协同工作流
- 无障碍访问支持

### 设计目标
1. **效率优先**: 减少操作步骤，提高工作效率
2. **清晰明了**: 信息层级分明，重点突出
3. **一致性**: 统一的交互模式和视觉语言
4. **容错性**: 友好的错误提示和恢复机制
5. **适应性**: 支持多设备、多场景使用

---

## UI组件规范

### 1. 按钮 (Buttons)

#### 主要按钮 (Primary Button)
```razor
<Button Type="@ButtonType.Primary" OnClick="HandleSave">
    保存订单
</Button>
```

**使用场景**:
- 页面主要操作（如保存、提交、确认）
- 每个区域最多使用1个主要按钮

**样式规范**:
- 背景色: `#667eea` (品牌主色)
- 文字: 动词+名词结构
- 高度: 32px (默认), 40px (大), 24px (小)
- 圆角: 8px
- 最小宽度: 80px

#### 次要按钮 (Default Button)
```razor
<Button Type="@ButtonType.Default" OnClick="HandleCancel">
    取消
</Button>
```

**使用场景**:
- 次要操作（如取消、重置）
- 与主要按钮配合使用

#### 危险按钮 (Danger Button)
```razor
<Button Type="@ButtonType.Primary" Danger OnClick="HandleDelete">
    删除订单
</Button>
```

**使用场景**:
- 删除、清空等不可逆操作
- 需要二次确认的危险操作

#### 文本按钮 (Text Button)
```razor
<Button Type="@ButtonType.Text" OnClick="HandleView">
    查看详情
</Button>
```

**使用场景**:
- 表格内操作列
- 次要链接操作

### 2. 表单 (Forms)

#### 表单布局
```razor
<Form Model="@model" 
      LabelCol="new ColLayoutParam { Span = 6 }"
      WrapperCol="new ColLayoutParam { Span = 18 }">
    
    <FormItem Label="订单编号" Required>
        <Input @bind-Value="@model.OrderNumber" Placeholder="请输入订单编号" />
    </FormItem>
    
    <FormItem Label="客户名称" Required>
        <Select @bind-Value="@model.CustomerId" 
                Placeholder="请选择客户"
                DataSource="@customers"
                LabelName="@nameof(Customer.Name)"
                ValueName="@nameof(Customer.Id)" />
    </FormItem>
    
</Form>
```

**布局规范**:
- 标签宽度: 6栏 (25%)
- 输入宽度: 18栏 (75%)
- 移动端: 标签和输入各占100%
- 表单间距: 24px

**验证规范**:
- 实时验证: 失焦时触发
- 错误提示: 显示在输入框下方
- 必填标识: 红色星号 `*`
- 验证图标: 输入框右侧显示

#### 表单元素规范

**输入框 (Input)**
- 默认高度: 32px
- 占位文本: `请输入{字段名称}`
- 最大长度提示: 接近限制时显示剩余字符
- 禁用状态: 灰色背景，不可编辑

**选择器 (Select)**
- 默认高度: 32px
- 占位文本: `请选择{字段名称}`
- 搜索功能: 选项超过10个时启用
- 清除按钮: 显示清除图标

**日期选择器 (DatePicker)**
- 格式: `YYYY-MM-DD`
- 快捷选择: 今天、昨天、本周、本月
- 范围选择: 使用 `RangePicker`

**数字输入框 (InputNumber)**
- 步长: 根据业务场景设置
- 最大/最小值: 必须设置合理范围
- 精度: 金额2位小数，数量整数

### 3. 表格 (Tables)

#### 标准表格
```razor
<Table TItem="OrderDto"
       DataSource="@orders"
       Loading="@loading"
       @bind-PageIndex="@pageIndex"
       @bind-PageSize="@pageSize"
       Total="@total"
       OnChange="@HandleTableChange">
    
    <Column Title="订单编号" @bind-Field="@context.OrderNumber" Fixed="left" Width="150" />
    <Column Title="客户名称" @bind-Field="@context.CustomerName" Width="200" />
    <Column Title="订单金额" @bind-Field="@context.TotalAmount" Width="120">
        <span class="amount-text">¥@context.TotalAmount.ToString("N2")</span>
    </Column>
    <Column Title="状态" @bind-Field="@context.Status" Width="100">
        <Tag Color="@GetStatusColor(context.Status)">@GetStatusText(context.Status)</Tag>
    </Column>
    <Column Title="操作" Fixed="right" Width="200">
        <Button Type="@ButtonType.Text" OnClick="() => HandleView(context.Id)">查看</Button>
        <Button Type="@ButtonType.Text" OnClick="() => HandleEdit(context.Id)">编辑</Button>
        <Button Type="@ButtonType.Text" Danger OnClick="() => HandleDelete(context.Id)">删除</Button>
    </Column>
    
</Table>
```

**表格规范**:
- 固定列: 首列和操作列固定
- 最小列宽: 80px
- 操作列宽: 根据操作数量调整（3个操作约200px）
- 空状态: 显示友好的空状态提示
- 加载状态: 显示骨架屏或加载动画

**单元格规范**:
- 文本对齐: 左对齐（默认）、右对齐（数字）、居中（状态）
- 文本溢出: 超出显示省略号，hover显示完整内容
- 金额格式: 千分位分隔，2位小数
- 日期格式: `YYYY-MM-DD HH:mm`

### 4. 卡片 (Cards)

#### 信息卡片
```razor
<Card Title="订单信息" Extra="@extraTemplate">
    <Body>
        <Descriptions Column="2">
            <DescriptionsItem Title="订单编号">@order.OrderNumber</DescriptionsItem>
            <DescriptionsItem Title="客户名称">@order.CustomerName</DescriptionsItem>
            <DescriptionsItem Title="创建时间">@order.CreateTime.ToString("yyyy-MM-dd HH:mm")</DescriptionsItem>
            <DescriptionsItem Title="订单状态">
                <Tag Color="@GetStatusColor(order.Status)">@GetStatusText(order.Status)</Tag>
            </DescriptionsItem>
        </Descriptions>
    </Body>
</Card>
```

**卡片规范**:
- 内边距: 24px
- 圆角: 8px
- 阴影: `0 1px 2px rgba(0,0,0,0.05)`
- 间距: 卡片之间16px

### 5. 模态框 (Modals)

#### 标准模态框
```razor
<Modal Title="编辑订单"
       Visible="@visible"
       OnOk="@HandleOk"
       OnCancel="@HandleCancel"
       Width="800">
    <Form Model="@model">
        <!-- 表单内容 -->
    </Form>
</Modal>
```

**模态框规范**:
- 默认宽度: 520px
- 大型表单: 800px - 1000px
- 确认对话框: 416px
- 最大高度: 视口高度的80%
- 背景遮罩: 半透明黑色 (rgba(0,0,0,0.45))

### 6. 消息提示 (Messages & Notifications)

#### 消息提示
```csharp
// 成功提示
await MessageService.Success("保存成功");

// 错误提示
await MessageService.Error("保存失败，请重试");

// 警告提示
await MessageService.Warning("库存不足，请注意");

// 信息提示
await MessageService.Info("数据正在同步中");
```

#### 通知框
```csharp
// 成功通知
await NotificationService.Success(new NotificationConfig
{
    Message = "订单创建成功",
    Description = $"订单编号: {orderNumber}",
    Duration = 3
});

// 错误通知
await NotificationService.Error(new NotificationConfig
{
    Message = "同步失败",
    Description = "部分商品数据同步失败，请查看日志",
    Duration = 0 // 不自动关闭
});
```

**提示规范**:
- 成功: 3秒后自动关闭
- 错误: 5秒后自动关闭或需手动关闭
- 位置: 页面顶部居中（Message）或右上角（Notification）
- 最多显示: 3个提示同时存在

---

## 响应式设计

### 断点系统
```css
/* 手机 (Mobile) */
@media (max-width: 576px) { }

/* 平板 (Tablet) */
@media (min-width: 577px) and (max-width: 768px) { }

/* 小屏桌面 (Desktop) */
@media (min-width: 769px) and (max-width: 1024px) { }

/* 大屏桌面 (Large Desktop) */
@media (min-width: 1025px) and (max-width: 1440px) { }

/* 超大屏 (Extra Large) */
@media (min-width: 1441px) { }
```

### 布局适配

#### Grid布局
```razor
<Row Gutter="16">
    <AntDesign.Col Xs="24" Sm="12" Md="8" Lg="6" Xl="4">
        <Card>内容1</Card>
    </AntDesign.Col>
    <AntDesign.Col Xs="24" Sm="12" Md="8" Lg="6" Xl="4">
        <Card>内容2</Card>
    </AntDesign.Col>
</Row>
```

#### 移动端适配规则
1. **导航**: 折叠为抽屉式菜单
2. **表格**: 横向滚动或卡片式展示
3. **表单**: 标签和输入框垂直排列
4. **按钮**: 增大触摸区域（最小44×44px）
5. **间距**: 适当减小以节省空间

---

## 色彩系统

### 品牌色
```css
/* 主品牌色 - 科技紫蓝 */
--brand-primary: #667eea;
--brand-primary-hover: #5568d3;
--brand-primary-active: #4c5fc4;
--brand-primary-light: rgba(102, 126, 234, 0.1);

/* 功能色 */
--brand-success: #10b981;      /* 成功 - 绿色 */
--brand-warning: #f59e0b;      /* 警告 - 橙色 */
--brand-error: #ef4444;        /* 错误 - 红色 */
--brand-info: #3b82f6;         /* 信息 - 蓝色 */
```

### 中性色
```css
/* 文字色 */
--text-primary: #1f2937;       /* 主要文字 */
--text-secondary: #6b7280;     /* 次要文字 */
--text-tertiary: #9ca3af;      /* 辅助文字 */
--text-disabled: #d1d5db;      /* 禁用文字 */

/* 背景色 */
--bg-primary: #ffffff;         /* 主背景 */
--bg-secondary: #f9fafb;       /* 次背景 */
--bg-tertiary: #f3f4f6;        /* 辅助背景 */
--bg-disabled: #e5e7eb;        /* 禁用背景 */

/* 边框色 */
--border-primary: #e5e7eb;     /* 主要边框 */
--border-secondary: #d1d5db;   /* 次要边框 */
```

### 状态色使用规范

| 状态 | 颜色 | 使用场景 |
|------|------|----------|
| 待处理 | `#6b7280` (灰色) | 新订单、待审核 |
| 进行中 | `#3b82f6` (蓝色) | 处理中、进行中 |
| 成功 | `#10b981` (绿色) | 已完成、已发货 |
| 警告 | `#f59e0b` (橙色) | 库存低、即将过期 |
| 错误 | `#ef4444` (红色) | 已取消、失败 |

---

## 排版规范

### 字体系统
```css
/* 中文字体 */
--font-family: "PingFang SC", "Microsoft YaHei", "Helvetica Neue", 
               Helvetica, Arial, sans-serif;

/* 数字字体 */
--font-family-number: "SF Pro Display", "PingFang SC", 
                      "Microsoft YaHei", sans-serif;

/* 等宽字体 (代码、订单号) */
--font-family-code: "SF Mono", Monaco, Consolas, 
                    "Courier New", monospace;
```

### 字号系统 (12点网格)
```css
--font-size-12: 12px;  /* 辅助文字 */
--font-size-14: 14px;  /* 正文文字 (默认) */
--font-size-16: 16px;  /* 小标题 */
--font-size-18: 18px;  /* 标题 */
--font-size-20: 20px;  /* 大标题 */
--font-size-24: 24px;  /* 页面标题 */
--font-size-30: 30px;  /* 主标题 */
```

### 行高规范
- 正文: 1.5 (21px)
- 标题: 1.25 - 1.35
- 密集文本: 1.3 (表格等)

### 字重规范
```css
--font-weight-normal: 400;     /* 正文 */
--font-weight-medium: 500;     /* 强调 */
--font-weight-semibold: 600;   /* 小标题 */
--font-weight-bold: 700;       /* 标题 */
```

---

## 交互规范

### 1. 加载状态

#### 全局加载
```razor
<Spin Spinning="@loading">
    <div class="content">
        <!-- 内容区域 -->
    </div>
</Spin>
```

#### 按钮加载
```razor
<Button Type="@ButtonType.Primary" Loading="@submitting" OnClick="HandleSubmit">
    保存
</Button>
```

#### 骨架屏
```razor
<Skeleton Active Paragraph="@(new SkeletonParagraphProps { Rows = 4 })" />
```

**加载规范**:
- 即时反馈: 操作后立即显示加载状态
- 超时提示: 超过5秒显示"加载中，请稍候"
- 失败重试: 提供重试按钮

### 2. 操作反馈

#### 成功反馈
- 显示成功消息提示
- 自动刷新数据或跳转
- 3秒后自动关闭提示

#### 错误反馈
- 显示具体错误原因
- 提供解决建议
- 提供重试或联系支持选项

#### 确认操作
```razor
<Popconfirm Title="确定要删除这条记录吗？"
            OnConfirm="HandleDelete"
            OkText="确定"
            CancelText="取消">
    <Button Danger>删除</Button>
</Popconfirm>
```

**确认规范**:
- 危险操作: 必须二次确认
- 不可逆操作: 明确说明后果
- 批量操作: 显示影响范围

### 3. 动画效果

#### 页面过渡
- 淡入淡出: 300ms
- 滑动: 250ms
- 缩放: 200ms

#### 按钮交互
- Hover: 150ms
- Active: 100ms
- 涟漪效果: 400ms

#### 列表动画
- 新增项: 从顶部滑入
- 删除项: 淡出并收缩
- 重排: 平滑移动

---

## 无障碍访问

### 1. 键盘导航
- Tab: 焦点顺序应符合视觉顺序
- Enter: 确认操作
- Esc: 关闭对话框/取消操作
- 方向键: 在列表/菜单中导航

### 2. 屏幕阅读器
- 所有图标按钮添加 `aria-label`
- 表单输入添加正确的 `label`
- 动态内容添加 `aria-live` 区域
- 重要提示添加 `role="alert"`

### 3. 色彩对比度
- 正文文字: 对比度 ≥ 4.5:1
- 大字体 (18px+): 对比度 ≥ 3:1
- 图形/图标: 对比度 ≥ 3:1

### 4. 触摸目标
- 最小尺寸: 44×44px
- 间距: 至少8px
- 移动端按钮: 适当增大

---

## 最佳实践

### 1. 性能优化
- 虚拟滚动: 长列表使用虚拟滚动
- 懒加载: 图片和组件按需加载
- 防抖节流: 搜索输入使用防抖
- 分页加载: 大数据分页或无限滚动

### 2. 数据展示
- 大数字: 使用千分位分隔
- 金额: 统一使用¥符号和2位小数
- 日期: 统一格式，相对时间（今天、昨天）
- 状态: 使用Tag组件，颜色区分

### 3. 错误处理
- 网络错误: 显示重试按钮
- 验证错误: 精确定位到错误字段
- 权限错误: 引导用户联系管理员
- 系统错误: 记录日志，显示友好提示

### 4. 数据安全
- 敏感数据: 部分遮蔽显示
- 操作日志: 记录关键操作
- 权限控制: 根据角色显示功能
- 数据导出: 添加水印和时间戳

---

## 组件清单

### 基础组件
- ✅ Button (按钮)
- ✅ Icon (图标)
- ✅ Typography (排版)

### 布局组件
- ✅ Grid (栅格)
- ✅ Layout (布局)
- ✅ Space (间距)
- ✅ Divider (分割线)

### 导航组件
- ✅ Menu (菜单)
- ✅ Breadcrumb (面包屑)
- ✅ Pagination (分页)
- ✅ Steps (步骤条)

### 数据录入
- ✅ Form (表单)
- ✅ Input (输入框)
- ✅ Select (选择器)
- ✅ DatePicker (日期选择器)
- ✅ Upload (上传)
- ✅ Switch (开关)
- ✅ Checkbox (多选框)
- ✅ Radio (单选框)

### 数据展示
- ✅ Table (表格)
- ✅ Card (卡片)
- ✅ Descriptions (描述列表)
- ✅ Tag (标签)
- ✅ Badge (徽标)
- ✅ Statistic (统计数值)

### 反馈组件
- ✅ Modal (对话框)
- ✅ Message (消息提示)
- ✅ Notification (通知)
- ✅ Popconfirm (气泡确认框)
- ✅ Spin (加载中)
- ✅ Skeleton (骨架屏)

---

## 设计资源

### 设计工具
- Figma: UI设计和原型
- Adobe XD: 交互设计
- Ant Design: 组件库参考

### 图标资源
- Ant Design Icons
- Font Awesome
- Material Icons

### 颜色工具
- Coolors: 配色方案
- Adobe Color: 色轮工具
- Contrast Checker: 对比度检查

---

## 更新日志

### v1.0.0 (2024-10-03)
- 初始版本
- 建立基础设计规范
- 定义组件使用规范
- 制定响应式设计策略

