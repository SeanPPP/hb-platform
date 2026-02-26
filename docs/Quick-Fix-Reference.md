# 快速修复参考手册

## 🚀 常见问题速查表

### SqlSugar 导航属性错误

#### 错误信息
```
在将 varchar 值 'XXX' 转换成数据类型 int 时失败
```

#### 快速修复
```csharp
// 一对多关系
[Navigate(NavigateType.OneToMany, nameof(子表.外键字段), nameof(主键字段))]
public List<子表>? 子表集合 { get; set; }

// 多对一关系  
[Navigate(NavigateType.ManyToOne, nameof(外键字段), nameof(父表.主键字段))]
public 父表? 父表对象 { get; set; }
```

### AntDesign Table 组件

#### 错误信息
```
Object of type 'AntDesign.Table' does not have a property matching the name 'ShowSizeChanger'
```

#### 快速修复
```razor
<!-- 删除不支持的属性 -->
<Table TItem="Model"
       DataSource="@data"
       PageIndex="@currentPage"
       PageSize="@pageSize" 
       Total="@totalCount"
       OnPageIndexChange="OnPageChanged"
       OnPageSizeChange="OnPageSizeChanged">
```

### AntDesign Button Danger

#### 错误信息
```
Object of type 'AntDesign.Button' does not have a property matching the name 'Danger'
```

#### 快速修复
```razor
<!-- 使用样式替代 -->
<Button Type="@ButtonType.Link" Style="color: #ff4d4f;">
    删除
</Button>
```

### Form 结构错误

#### 错误信息
```
FormItem must be wrapped in Form component
```

#### 快速修复
```razor
<Form Model="@model" TModel="ModelType">
    <FormItem Label="标签">
        <Input @bind-Value="model.Property" />
    </FormItem>
</Form>
```

## 🔧 一键修复脚本

### PowerShell 脚本 (Windows)

创建 `fix-antdesign.ps1`:
```powershell
# 修复 Table 组件不兼容属性
Get-ChildItem -Path "BlazorApp\Pages" -Recurse -Include "*.razor" | 
ForEach-Object {
    $content = Get-Content $_.FullName -Raw
    $content = $content -replace 'ShowSizeChanger="true"', ''
    $content = $content -replace 'ShowQuickJumper="true"', ''
    Set-Content $_.FullName $content
}

# 修复 Button Danger 属性
Get-ChildItem -Path "BlazorApp\Pages" -Recurse -Include "*.razor" | 
ForEach-Object {
    $content = Get-Content $_.FullName -Raw
    $content = $content -replace 'Danger>', 'Style="color: #ff4d4f;">'
    $content = $content -replace 'Danger ', 'Style="color: #ff4d4f;" '
    Set-Content $_.FullName $content
}

Write-Host "AntDesign 兼容性修复完成！"
```

### Bash 脚本 (Linux/Mac)

创建 `fix-antdesign.sh`:
```bash
#!/bin/bash

# 修复 Table 组件不兼容属性
find BlazorApp/Pages -name "*.razor" -exec sed -i 's/ShowSizeChanger="true"//g' {} \;
find BlazorApp/Pages -name "*.razor" -exec sed -i 's/ShowQuickJumper="true"//g' {} \;

# 修复 Button Danger 属性
find BlazorApp/Pages -name "*.razor" -exec sed -i 's/Danger>/Style="color: #ff4d4f;">/g' {} \;
find BlazorApp/Pages -name "*.razor" -exec sed -i 's/Danger /Style="color: #ff4d4f;" /g' {} \;

echo "AntDesign 兼容性修复完成！"
```

## 📋 检查命令速查

### 编译检查
```bash
# 快速编译检查
dotnet build --no-restore

# 详细编译信息
dotnet build --verbosity detailed

# 清理重新编译
dotnet clean && dotnet build
```

### 代码搜索
```bash
# 查找不兼容的 AntDesign 属性
grep -r "ShowSizeChanger\|ShowQuickJumper\|Danger=\"" BlazorApp/Pages/

# 查找可能的导航属性问题
grep -r "Navigate.*nameof.*[^,])" BlazorApp.Shared/Models/

# 查找 FormItem 在 Form 外使用
grep -B 3 -A 3 "<FormItem" BlazorApp/Pages/ | grep -v "<Form"
```

### 运行时检查
```bash
# 启动应用
dotnet run

# 查看实时日志
dotnet run --environment Development | grep -i error
```

## 🎯 快速诊断流程

### 1. 编译错误 (30秒内解决)
```
1. 复制错误信息
2. 在本文档搜索关键词
3. 应用对应的快速修复
4. 重新编译验证
```

### 2. 运行时错误 (2分钟内解决)
```
1. 查看浏览器控制台错误
2. 定位错误文件和行号
3. 检查数据绑定和空值
4. 应用相应修复方案
```

### 3. UI 显示问题 (5分钟内解决)
```
1. 检查组件属性是否正确
2. 验证数据源是否有值
3. 检查样式是否正确应用
4. 测试响应式布局
```

## 🔍 常用代码片段

### SqlSugar 服务注册
```csharp
// Program.cs 或 Startup.cs
services.AddSingleton<ISqlSugarClient>(provider =>
{
    var config = new ConnectionConfig
    {
        ConnectionString = connectionString,
        DbType = DbType.SqlServer,
        IsAutoCloseConnection = true,
        // 启用日志查看生成的SQL
        AopEvents = new AopEvents
        {
            OnLogExecuting = (sql, pars) =>
            {
                Console.WriteLine($"SQL: {sql}");
            }
        }
    };
    return new SqlSugarClient(config);
});
```

### AntDesign 标准 Table 模板
```razor
<Table @ref="table"
       TItem="ModelType"
       DataSource="@dataSource"
       Loading="@loading"
       Bordered="true"
       Size="@TableSize.Small"
       ScrollX="1200px"
       PageIndex="@currentPage"
       PageSize="@pageSize"
       Total="@totalCount"
       OnPageIndexChange="OnPageChanged"
       OnPageSizeChange="OnPageSizeChanged">
    
    <Column Title="列标题" DataIndex="PropertyName" />
    
    <ActionColumn Title="操作">
        <Template>
            <Space>
                <SpaceItem>
                    <Button Size="@ButtonSize.Small" Type="@ButtonType.Link"
                            OnClick="() => Edit(context)">
                        编辑
                    </Button>
                </SpaceItem>
                <SpaceItem>
                    <Popconfirm Title="确定要删除吗？"
                               OnConfirm="() => Delete(context)">
                        <Button Size="@ButtonSize.Small" Type="@ButtonType.Link"
                                Style="color: #ff4d4f;">
                            删除
                        </Button>
                    </Popconfirm>
                </SpaceItem>
            </Space>
        </Template>
    </ActionColumn>
</Table>
```

### 标准 Form 模板
```razor
<Form @ref="form"
      Model="@model"
      TModel="ModelType"
      OnFinish="OnSubmit">
    
    <FormItem Label="标签" Required>
        <Input @bind-Value="@context.Property" />
    </FormItem>
    
    <FormItem>
        <Button Type="@ButtonType.Primary" HtmlType="submit" Loading="@loading">
            提交
        </Button>
    </FormItem>
</Form>
```

## 📞 紧急联系清单

### 当遇到无法解决的问题时

1. **查阅官方文档**
   - [AntDesign Blazor](https://antblazor.com/)
   - [SqlSugar](https://www.donet5.com/Home/Doc)

2. **搜索已知问题**
   - GitHub Issues
   - Stack Overflow
   - 项目内部文档

3. **寻求帮助**
   - 团队技术负责人
   - 架构师
   - 社区论坛

## 🎓 学习建议

### 防止类似问题的最佳实践

1. **保持文档更新** - 及时记录新发现的问题
2. **定期代码审查** - 使用检查清单防止问题
3. **版本管理** - 谨慎升级依赖包版本
4. **测试驱动** - 先写测试再开发功能
5. **持续学习** - 关注技术栈的更新动态

---

💡 **提示**: 将此文档收藏到浏览器书签，遇到问题时快速查阅！