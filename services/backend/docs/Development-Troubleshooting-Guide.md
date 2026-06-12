# 开发问题排查指南

## 🎯 SqlSugar 导航属性配置错误

### 问题描述
```
SqlClient 异常：在将 varchar 值 'ORD-250902-01' 转换成数据类型 int 时失败
```

### 根本原因
SqlSugar 导航属性配置不正确，导致在生成 SQL 查询时，将字符串类型的外键与整数类型的字段进行比较。

### 解决方案

#### ✅ 正确的导航属性配置

**主表 (YIWU_Order.cs)**:
```csharp
// 一对多关系 - 订单主表到订单明细表
[Navigate(NavigateType.OneToMany, nameof(YIWU_OrderDetail.OrderNo), nameof(OrderNo))]
public List<YIWU_OrderDetail>? OrderDetails { get; set; }
```

**从表 (YIWU_OrderDetail.cs)**:
```csharp
// 多对一关系 - 订单明细表到订单主表
[Navigate(NavigateType.ManyToOne, nameof(OrderNo), nameof(YIWU_Order.OrderNo))]
public YIWU_Order? Order { get; set; }
```

#### ❌ 错误的配置
```csharp
// 错误：缺少明确的外键关系指定
[Navigate(NavigateType.OneToMany, nameof(OrderNo))]
public List<YIWU_OrderDetail>? OrderDetails { get; set; }

[Navigate(NavigateType.ManyToOne, nameof(OrderNo))]
public YIWU_Order? Order { get; set; }
```

### 配置原理
- **第一个参数**: 指定关联实体的外键属性
- **第二个参数**: 指定当前实体的主键属性
- 这样 SqlSugar 就能正确理解两个表之间的关联关系，生成正确的 SQL 查询

## 🎯 AntDesign 组件兼容性问题

### 常见错误模式

#### 1. 属性不存在错误
```
Object of type 'AntDesign.Table' does not have a property matching the name 'ShowSizeChanger'
```

**解决方案**: 参考 `docs/AntDesign-Compatibility-Guide.md`

#### 2. 组件结构错误
```
FormItem must be wrapped in Form component
```

**解决方案**: 确保 FormItem 包装在 Form 组件内，并指定正确的 Model 类型

## 🔍 排查流程

### 1. 编译时错误排查

#### 步骤 1: 检查错误信息
```bash
dotnet build
```

关注错误类型：
- 属性不存在 → 检查组件文档
- 类型不匹配 → 检查数据绑定
- 空引用警告 → 添加空值检查

#### 步骤 2: 定位问题文件
错误信息通常包含文件路径和行号，直接定位问题代码。

#### 步骤 3: 查阅文档
- SqlSugar: [官方文档](https://www.donet5.com/Home/Doc)
- AntDesign: [组件文档](https://antblazor.com/)

### 2. 运行时错误排查

#### 步骤 1: 查看浏览器控制台
```javascript
// 常见错误信息
Unhandled exception rendering component
Object reference not set to an instance of an object
```

#### 步骤 2: 检查数据流
- 验证 API 返回数据
- 检查数据绑定是否正确
- 确认数据不为空

#### 步骤 3: 逐步调试
```csharp
// 添加调试输出
Console.WriteLine($"Data count: {data?.Count ?? 0}");
```

## 🛠️ 常用调试命令

### 编译和构建
```bash
# 清理构建
dotnet clean

# 完整构建
dotnet build

# 运行应用
dotnet run

# 查看详细错误
dotnet build --verbosity detailed
```

### 数据库相关
```bash
# 检查数据库连接
# 在 DatabaseDebug.razor 页面测试连接

# 查看 SQL 日志
# 在服务中启用 SqlSugar 日志记录
```

## 📋 问题检查清单

### SqlSugar 导航属性
- [ ] 导航属性是否正确配置了两个参数
- [ ] 外键字段类型是否匹配
- [ ] 表名和字段名是否正确
- [ ] 是否启用了 SqlSugar 日志来查看生成的 SQL

### AntDesign 组件
- [ ] 组件属性是否在当前版本中存在
- [ ] FormItem 是否包装在 Form 组件内
- [ ] 数据绑定是否正确
- [ ] 是否有空引用的可能

### 一般问题
- [ ] 命名空间是否正确导入
- [ ] 依赖注入是否正确配置
- [ ] 数据模型是否匹配

## 🔧 修复工具

### 1. 自动化检查脚本
```bash
# 检查常见的 AntDesign 兼容性问题
grep -r "ShowSizeChanger\|ShowQuickJumper\|Danger=\"" BlazorApp/Pages/

# 检查导航属性配置
grep -r "Navigate.*nameof" BlazorApp.Shared/Models/
```

### 2. 代码模板
参考 `cursor-rules.md` 中的代码模板，确保新代码符合规范。

## 📚 学习资源

### 官方文档
- [SqlSugar 文档](https://www.donet5.com/Home/Doc)
- [AntDesign Blazor 文档](https://antblazor.com/)
- [.NET Blazor 文档](https://docs.microsoft.com/aspnet/core/blazor/)

### 社区资源
- [GitHub Issues](https://github.com/ant-design-blazor/ant-design-blazor/issues)
- [Stack Overflow](https://stackoverflow.com/questions/tagged/blazor)

## 📝 问题记录模板

### 新问题记录格式
```markdown
## 问题标题

### 错误信息
```
[错误信息内容]
```

### 问题描述
[详细描述问题出现的场景]

### 解决方案
[详细的修复步骤]

### 防范措施
[如何避免类似问题]
```

---

## 📊 Excel导出问题排查

### 问题类型: Excel导出格式异常

#### 症状描述
- Excel导出后列顺序不符合预期
- 图片未显示在期望的列位置
- 列宽设置不合理，内容显示不完整

#### 常见错误信息
```
System.ArgumentException: 指定的列索引超出范围
System.NullReferenceException: 图片对象引用为空
ClosedXML.Excel.XLException: 无法将图片添加到指定位置
```

#### 排查步骤

**1. 检查列索引配置**
```csharp
// 检查代码中的列索引是否正确
worksheet.Cell(row, 1).Value = data; // 确认列号从1开始
```

**2. 验证图片处理逻辑**
```csharp
// 检查图片流是否正确
if (imageStream != null && imageStream.Length > 0)
{
    var picture = worksheet.AddPicture(imageStream, $"Image_{row}");
    // 验证图片定位
    picture.MoveTo(worksheet.Cell(row, imageColumn), 5, 5);
}
```

**3. 检查列宽设置**
```csharp
// 确认列宽设置逻辑
worksheet.Column(imageColumn).Width = 25; // 图片列固定宽度
worksheet.Columns(startCol, endCol).AdjustToContents(); // 其他列自适应
```

#### 解决方案

**问题**: 图片列位置错误
```csharp
// 修复前 - 图片在错误位置
worksheet.Cell(row, 4).Value = "图片占位符";

// 修复后 - 图片移至首列
var imageCell = worksheet.Cell(row, 1);
if (hasImage)
{
    var picture = worksheet.AddPicture(imageStream, $"Image_{row}");
    picture.MoveTo(imageCell, 5, 5);
}
```

**问题**: 列宽设置不当
```csharp
// 修复前 - 固定宽度可能不适合
worksheet.Columns().AdjustToContents();

// 修复后 - 分类设置列宽
worksheet.Column(1).Width = 25; // 图片列固定
worksheet.Columns(2, 4).AdjustToContents(); // 文本列自适应
// 设置最小宽度确保可读性
for (int col = 2; col <= maxCol; col++)
{
    if (worksheet.Column(col).Width < 12)
        worksheet.Column(col).Width = 12;
}
```

#### 防范措施
1. **代码审查**: 检查Excel导出相关的列索引配置
2. **单元测试**: 为Excel导出功能编写专门的测试用例
3. **文档维护**: 及时更新Excel格式变更文档
4. **版本控制**: Excel格式变更需要记录在变更日志中

#### 相关文档
- [Excel导出图片列修复文档](../md_doc/Excel_Export_Image_Column_Fix.md)
- [API变更日志](./API_CHANGELOG.md)

---

### 问题类型: Excel导出性能问题

#### 症状描述
- 大批量数据导出超时
- 内存使用过高导致系统崩溃
- 导出过程中用户界面无响应

#### 排查步骤

**1. 检查数据量和查询效率**
```sql
-- 检查数据量
SELECT COUNT(*) FROM YIWU_Order WHERE OrderId IN (...)

-- 检查查询性能
EXPLAIN SELECT * FROM YIWU_Order_Detail WHERE OrderId IN (...)
```

**2. 监控内存使用**
```csharp
// 在导出过程中监控内存
var beforeMemory = GC.GetTotalMemory(false);
// ... 导出逻辑
var afterMemory = GC.GetTotalMemory(true);
_logger.LogInformation($"内存使用: {(afterMemory - beforeMemory) / 1024 / 1024} MB");
```

**3. 检查图片处理效率**
```csharp
// 优化图片处理
using var imageStream = new MemoryStream(imageBytes);
// 及时释放资源
imageStream?.Dispose();
```

#### 解决方案

**分批处理大数据量**
```csharp
const int batchSize = 100;
for (int i = 0; i < orderIds.Count; i += batchSize)
{
    var batch = orderIds.Skip(i).Take(batchSize);
    // 处理批次数据
    await ProcessBatchAsync(batch);
    
    // 强制垃圾回收
    if (i % (batchSize * 5) == 0)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }
}
```

**异步处理**
```csharp
// 使用异步方法避免阻塞UI
public async Task<byte[]> ExportOrdersAsync(List<int> orderIds)
{
    return await Task.Run(() => ExportOrdersInternal(orderIds));
}
```

---

💡 **提示**: 遇到新问题时，请及时更新此文档，为团队积累排查经验。