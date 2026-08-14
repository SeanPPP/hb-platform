# WPF 客显折扣与底部统计可读性 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 WPF Customer Display 商品行中显示折扣率、划线原价和高亮折后价，并放大底部统计信息。

**Architecture:** 继续由 `CustomerDisplayOrchestrator.LoadFromCart` 把购物车最终定价结果映射到只读 `CustomerDisplayLine`，客显不重新计算折扣。XAML 根据 `HasDiscount` 切换单行和双行价格层级，底部统计只调整既有网格尺寸和字号。

**Tech Stack:** .NET 8、WPF XAML、CommunityToolkit.Mvvm、xUnit

## Global Constraints

- 保留现有深色高对比主题、三列底部布局和右侧付款状态卡。
- `DESIGN_VARIANCE: 3`、`MOTION_INTENSITY: 1`、`VISUAL_DENSITY: 7`。
- 不新增依赖，不改变购物车折扣计算、订单总额、GST、支付、广告或持久化逻辑。
- 只修改本计划列出的客显文件；不得暂存或覆盖工作区中其他用户改动。
- 所有测试和构建使用新的仓库内 `.artifacts/customer-display-discount` 输出目录，并允许首次恢复依赖。

## File Map

- `apps/pos-wpf/src/Hbpos.Client.Wpf/Models/CustomerDisplayModels.cs`：客显行只读展示字段。
- `apps/pos-wpf/src/Hbpos.Client.Wpf/Services/CustomerDisplayOrchestrator.cs`：从 `CartLine` 映射最终折扣数据。
- `apps/pos-wpf/src/Hbpos.Client.Wpf/Views/Screens/CustomerDisplayView.xaml`：商品行折扣层级和底部统计字号。
- `apps/pos-wpf/tests/Hbpos.Client.Tests/CustomerDisplayOrchestratorTests.cs`：折扣数据映射回归测试。
- `apps/pos-wpf/tests/Hbpos.Client.Tests/CustomerDisplayViewModelTests.cs`：客显 XAML 结构与字号回归测试。

---

### Task 1: 保留购物车最终折扣展示数据

**Files:**
- Modify: `apps/pos-wpf/src/Hbpos.Client.Wpf/Models/CustomerDisplayModels.cs`
- Modify: `apps/pos-wpf/src/Hbpos.Client.Wpf/Services/CustomerDisplayOrchestrator.cs:85-106`
- Test: `apps/pos-wpf/tests/Hbpos.Client.Tests/CustomerDisplayOrchestratorTests.cs`

**Interfaces:**
- Consumes: `CartLine.GrossAmount`、`CartLine.ActualAmount`、`CartLine.HasDiscount`、`CartLine.DiscountRateText`。
- Produces: `CustomerDisplayLine.GrossAmount : decimal`、`HasDiscount : bool`、`DiscountRateText : string`，并保留现有五参数构造函数。

- [ ] **Step 1: 写入失败的折扣映射测试**

在 `CustomerDisplayOrchestratorTests.cs` 添加 `using Hbpos.Contracts.Catalog;`，并加入：

```csharp
[Fact]
public void LoadFromCart_preserves_customer_facing_discount_values()
{
    var orchestrator = new CustomerDisplayOrchestrator(new FakeCustomerDisplayWindowService());
    var cart = new PosCartService();
    cart.AddItem(CreateItem("SKU-DISCOUNT", "Discounted Item", "930000000001", 10m));
    cart.AddItem(CreateItem("SKU-REGULAR", "Regular Item", "930000000002", 5m));
    cart.SetLineDiscountPercent(cart.Lines.Single(line => line.LookupCode == "930000000001"), 10m);
    var customerDisplay = new CustomerDisplayViewModel();

    orchestrator.LoadFromCart(
        customerDisplay,
        CreateSession(),
        cart,
        refreshAdvertisements: false);

    var discounted = customerDisplay.Lines.Single(line => line.LookupCode == "930000000001");
    Assert.Equal(10m, discounted.GrossAmount);
    Assert.Equal(9m, discounted.ActualAmount);
    Assert.True(discounted.HasDiscount);
    Assert.Equal("-10%", discounted.DiscountRateText);

    var regular = customerDisplay.Lines.Single(line => line.LookupCode == "930000000002");
    Assert.Equal(5m, regular.GrossAmount);
    Assert.Equal(5m, regular.ActualAmount);
    Assert.False(regular.HasDiscount);
    Assert.Empty(regular.DiscountRateText);
}

private static SellableItemDto CreateItem(
    string productCode,
    string displayName,
    string lookupCode,
    decimal price)
{
    return new SellableItemDto(
        StoreCode: "S001",
        ProductCode: productCode,
        ReferenceCode: null,
        DisplayName: displayName,
        LookupCode: lookupCode,
        ItemNumber: productCode,
        Barcode: lookupCode,
        RetailPrice: price,
        PriceSource: PriceSourceKind.StoreRetailPrice,
        PriceSourceLabel: "StoreRetailPrice",
        QuantityFactor: 1m,
        UpdatedAt: DateTimeOffset.UtcNow,
        ProductImage: null);
}
```

- [ ] **Step 2: 运行测试并确认按预期失败**

```powershell
dotnet test apps/pos-wpf/tests/Hbpos.Client.Tests/Hbpos.Client.Tests.csproj --filter "FullyQualifiedName=Hbpos.Client.Tests.CustomerDisplayOrchestratorTests.LoadFromCart_preserves_customer_facing_discount_values" --artifacts-path .artifacts/customer-display-discount
```

Expected: FAIL，编译器指出 `CustomerDisplayLine` 尚无 `GrossAmount`、`HasDiscount` 或 `DiscountRateText`。

- [ ] **Step 3: 添加最小展示字段和映射**

保持现有 record 构造参数不变，在 `CustomerDisplayLine` 主体添加：

```csharp
public decimal GrossAmount { get; init; } = ActualAmount;

public bool HasDiscount { get; init; }

public string DiscountRateText { get; init; } = string.Empty;
```

将 `LoadFromCart` 中每行映射改为：

```csharp
var lines = cart.Lines.Select(line => new CustomerDisplayLine(
    line.DisplayName,
    line.LookupCode,
    line.Quantity,
    line.UnitPrice,
    line.ActualAmount)
{
    GrossAmount = line.GrossAmount,
    HasDiscount = line.HasDiscount,
    DiscountRateText = line.DiscountRateText
});
```

- [ ] **Step 4: 重新运行测试并确认通过**

Run: 使用 Step 2 的同一命令。

Expected: PASS，1 个测试通过。

- [ ] **Step 5: 检查并提交数据映射改动**

```powershell
git diff --check -- apps/pos-wpf/src/Hbpos.Client.Wpf/Models/CustomerDisplayModels.cs apps/pos-wpf/src/Hbpos.Client.Wpf/Services/CustomerDisplayOrchestrator.cs apps/pos-wpf/tests/Hbpos.Client.Tests/CustomerDisplayOrchestratorTests.cs
git add -- apps/pos-wpf/src/Hbpos.Client.Wpf/Models/CustomerDisplayModels.cs apps/pos-wpf/src/Hbpos.Client.Wpf/Services/CustomerDisplayOrchestrator.cs apps/pos-wpf/tests/Hbpos.Client.Tests/CustomerDisplayOrchestratorTests.cs
git commit -m "显示客显商品折扣数据"
```

### Task 2: 在商品行呈现折扣层级

**Files:**
- Modify: `apps/pos-wpf/src/Hbpos.Client.Wpf/Views/Screens/CustomerDisplayView.xaml:71-142`
- Test: `apps/pos-wpf/tests/Hbpos.Client.Tests/CustomerDisplayViewModelTests.cs`

**Interfaces:**
- Consumes: Task 1 产生的 `CustomerDisplayLine.GrossAmount`、`HasDiscount`、`DiscountRateText`。
- Produces: Price 下方折扣率，以及 Total 中仅折扣行可见的划线原价和高亮折后价。

- [ ] **Step 1: 写入失败的商品行视觉结构测试**

在 `CustomerDisplayViewModelTests.cs` 添加：

```csharp
[Fact]
public void CustomerDisplayView_shows_discount_rate_and_original_total_for_discounted_lines()
{
    var (xaml, _) = ReadCustomerDisplayViewFiles();

    Assert.Contains("Text=\"{Binding DiscountRateText}\"", xaml);
    Assert.Contains("Text=\"{Binding GrossAmount, StringFormat={}{0:C2}}\"", xaml);
    Assert.Contains("TextDecorations=\"Strikethrough\"", xaml);
    Assert.Contains("<DataTrigger Binding=\"{Binding HasDiscount}\" Value=\"True\">", xaml);
}
```

- [ ] **Step 2: 运行测试并确认按预期失败**

```powershell
dotnet test apps/pos-wpf/tests/Hbpos.Client.Tests/Hbpos.Client.Tests.csproj --filter "FullyQualifiedName=Hbpos.Client.Tests.CustomerDisplayViewModelTests.CustomerDisplayView_shows_discount_rate_and_original_total_for_discounted_lines" --artifacts-path .artifacts/customer-display-discount
```

Expected: FAIL，XAML 尚无 `DiscountRateText` 和 `GrossAmount` 绑定。

- [ ] **Step 3: 实现 Price 和 Total 的折扣层级**

- 将 `LineDataGrid.RowHeight` 从 `64` 调整为 `72`。
- 将 Price 单一 `TextBlock` 改为垂直 `StackPanel`：第一行保留 `UnitPrice`，第二行绑定 `DiscountRateText`，字号 12、加粗、青绿色；默认折叠，并由 `HasDiscount=True` 的 `DataTrigger` 显示。
- 将 Total 单一 `TextBlock` 改为垂直 `StackPanel`：第一行绑定 `GrossAmount`，字号 13、弱化色、`TextDecorations="Strikethrough"`；默认折叠，并由相同触发器显示。第二行继续绑定 `ActualAmount`，字号 19、加粗；默认白色，折扣行由触发器改为青绿色。
- 保持 Price 宽 116、Total 宽 126、商品名称和数量列宽不变。

- [ ] **Step 4: 重新运行测试并确认通过**

Run: 使用 Step 2 的同一命令。

Expected: PASS，1 个测试通过。

- [ ] **Step 5: 检查并提交商品行改动**

```powershell
git diff --check -- apps/pos-wpf/src/Hbpos.Client.Wpf/Views/Screens/CustomerDisplayView.xaml apps/pos-wpf/tests/Hbpos.Client.Tests/CustomerDisplayViewModelTests.cs
git add -- apps/pos-wpf/src/Hbpos.Client.Wpf/Views/Screens/CustomerDisplayView.xaml apps/pos-wpf/tests/Hbpos.Client.Tests/CustomerDisplayViewModelTests.cs
git commit -m "展示客显商品折扣层级"
```

### Task 3: 放大底部统计并完成验证

**Files:**
- Modify: `apps/pos-wpf/src/Hbpos.Client.Wpf/Views/Screens/CustomerDisplayView.xaml:47-51,221-302`
- Test: `apps/pos-wpf/tests/Hbpos.Client.Tests/CustomerDisplayViewModelTests.cs`

**Interfaces:**
- Consumes: 现有 `TotalItemQuantity`、`SkuCount`、`Subtotal`、`TaxAmount`、`SavingsAmount`、`TotalToPay` 绑定。
- Produces: 152 高的统计区，以及 15、14、28、16、62 分级字号。

- [ ] **Step 1: 写入失败的统计可读性测试**

在测试文件顶部添加 `using System.Xml.Linq;`，并加入：

```csharp
[Fact]
public void CustomerDisplayView_enlarges_summary_statistics_for_distance_reading()
{
    var (xaml, _) = ReadCustomerDisplayViewFiles();
    var document = XDocument.Parse(xaml);
    XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

    var summaryRow = document
        .Descendants(presentation + "RowDefinition")
        .Single(element => element.Attribute(x + "Name")?.Value == "SummaryRow");
    var summaryPanel = document
        .Descendants(presentation + "Border")
        .Single(element => element.Attribute(x + "Name")?.Value == "SummaryPanel");

    Assert.Equal("152", summaryRow.Attribute("Height")?.Value);
    Assert.Equal("24,16", summaryPanel.Attribute("Padding")?.Value);
    Assert.Equal("15", FindBoundTextBlock(document, presentation, "TotalItemQuantity").Attribute("FontSize")?.Value);
    Assert.Equal("15", FindBoundTextBlock(document, presentation, "SkuCount").Attribute("FontSize")?.Value);
    Assert.Equal("28", FindBoundTextBlock(document, presentation, "Subtotal").Attribute("FontSize")?.Value);
    Assert.Equal("28", FindBoundTextBlock(document, presentation, "TaxAmount").Attribute("FontSize")?.Value);
    Assert.Equal("28", FindBoundTextBlock(document, presentation, "SavingsAmount").Attribute("FontSize")?.Value);
    Assert.Equal("62", FindBoundTextBlock(document, presentation, "TotalToPay").Attribute("FontSize")?.Value);
}

private static XElement FindBoundTextBlock(XDocument document, XNamespace presentation, string propertyName)
{
    return document
        .Descendants(presentation + "TextBlock")
        .Single(element => element.Attribute("Text")?.Value.Contains(
            $"Binding {propertyName}",
            StringComparison.Ordinal) == true);
}
```

- [ ] **Step 2: 运行测试并确认按预期失败**

```powershell
dotnet test apps/pos-wpf/tests/Hbpos.Client.Tests/Hbpos.Client.Tests.csproj --filter "FullyQualifiedName=Hbpos.Client.Tests.CustomerDisplayViewModelTests.CustomerDisplayView_enlarges_summary_statistics_for_distance_reading" --artifacts-path .artifacts/customer-display-discount
```

Expected: FAIL，当前 SummaryRow 为 132，统计金额字号为 22。

- [ ] **Step 3: 放大统计区**

- `SummaryRow.Height`：132 改为 152。
- `SummaryPanel.Padding`：`20,12` 改为 `24,16`。
- Item Quantity 和 SKU Count 的标签及绑定值字号：12 改为 15。
- Subtotal、GST、Savings 标签字号：12 改为 14；金额字号：22 改为 28；对应 Viewbox `MaxHeight`：30 改为 38。
- Total To Pay 标签字号：14 改为 16；金额字号：58 改为 62；Viewbox `MaxHeight`：62 改为 68。
- 付款状态主文字字号：16 改为 18；说明文字显式设为 15。
- 保留三列结构、现有颜色、圆角和金额向下缩放行为。

- [ ] **Step 4: 运行定向测试和客显测试集**

```powershell
dotnet test apps/pos-wpf/tests/Hbpos.Client.Tests/Hbpos.Client.Tests.csproj --filter "FullyQualifiedName~CustomerDisplay" --artifacts-path .artifacts/customer-display-discount
```

Expected: 所有匹配 CustomerDisplay 的测试通过，0 失败。

- [ ] **Step 5: 构建 WPF 项目**

```powershell
dotnet build apps/pos-wpf/src/Hbpos.Client.Wpf/Hbpos.Client.Wpf.csproj --artifacts-path .artifacts/customer-display-discount
```

Expected: Build succeeded，0 errors。

- [ ] **Step 6: 完成范围和影响复核**

```powershell
git diff --check -- apps/pos-wpf/src/Hbpos.Client.Wpf/Models/CustomerDisplayModels.cs apps/pos-wpf/src/Hbpos.Client.Wpf/Services/CustomerDisplayOrchestrator.cs apps/pos-wpf/src/Hbpos.Client.Wpf/Views/Screens/CustomerDisplayView.xaml apps/pos-wpf/tests/Hbpos.Client.Tests/CustomerDisplayOrchestratorTests.cs apps/pos-wpf/tests/Hbpos.Client.Tests/CustomerDisplayViewModelTests.cs
git status --short
```

随后运行 GitNexus `detect_changes(scope: "all")`。若连接仍为 `Transport closed`，记录该限制，并用 codebase-memory `detect_changes`、定向测试、构建和上述精确文件差异复核替代。

- [ ] **Step 7: 提交统计区改动**

```powershell
git add -- apps/pos-wpf/src/Hbpos.Client.Wpf/Views/Screens/CustomerDisplayView.xaml apps/pos-wpf/tests/Hbpos.Client.Tests/CustomerDisplayViewModelTests.cs
git commit -m "放大客显底部统计信息"
```
