# 🛒 购物车同步系统 - 使用指南

## 概述

本系统实现了一个完整的本地购物车与服务器数据库同步的解决方案，支持在线/离线模式，确保用户在任何情况下都能正常使用购物车功能。

## 🏗️ 架构设计

### 数据库模型
```
Cart (购物车表)
├── CartGUID (主键)
├── UserGUID (用户GUID)
├── StoreGUID (门店GUID)  
├── CartStatus (状态: Active/Inactive/Checkout)
├── TotalAmount (总金额)
├── TotalQuantity (总数量)
├── LastModified (最后修改时间)
└── ExpiresAt (过期时间)

CartItem (购物车项表)
├── CartItemGUID (主键)
├── CartGUID (购物车GUID)
├── ProductGUID (商品GUID)
├── ProductCode (商品代码)
├── ProductName (商品名称)
├── UnitPrice (单价)
├── Quantity (数量)
├── TotalPrice (总价)
├── AddedAt (添加时间)
└── LastUpdated (更新时间)
```

### 服务架构
```
Frontend (Blazor WebAssembly)
├── HybridShoppingCartService (混合购物车服务)
├── CartServiceClient (API客户端)
└── LocalStorageService (本地存储)

Backend (ASP.NET Core Web API)
├── CartController (购物车控制器)
├── CartService (购物车业务服务)
└── SqlSugarContext (数据访问)
```

## 🚀 功能特性

### ✅ 已实现功能

1. **混合存储模式**
   - 本地存储：浏览器localStorage
   - 服务器存储：SQL数据库
   - 自动同步机制

2. **离线支持**
   - 离线状态下正常使用
   - 数据本地缓存
   - 上线后自动同步

3. **完整的CRUD操作**
   - 添加商品到购物车
   - 更新商品数量
   - 移除商品
   - 清空购物车
   - 批量操作

4. **用户认证集成**
   - JWT认证支持
   - 用户购物车隔离
   - 多门店支持

5. **数据同步策略**
   - 强制同步
   - 后台定时同步
   - 冲突解决机制

## 📖 使用示例

### 前端使用示例

#### 1. 基础购物车操作

```csharp
@page "/products"
@inject IShoppingCartService CartService
@inject IMessageService MessageService

<div class="product-grid">
    @foreach (var product in products)
    {
        <div class="product-card">
            <h3>@product.ProductName</h3>
            <p>价格: ¥@product.RetailPrice</p>
            
            @if (cartQuantities.ContainsKey(product.ProductCode))
            {
                <div class="quantity-controls">
                    <button @onclick="() => UpdateQuantity(product.ProductCode, cartQuantities[product.ProductCode] - 1)">
                        -
                    </button>
                    <span>@cartQuantities[product.ProductCode]</span>
                    <button @onclick="() => UpdateQuantity(product.ProductCode, cartQuantities[product.ProductCode] + 1)">
                        +
                    </button>
                </div>
            }
            else
            {
                <button @onclick="() => AddToCart(product)" class="add-to-cart-btn">
                    加入购物车
                </button>
            }
        </div>
    }
</div>

@code {
    private List<ProductDto> products = new();
    private Dictionary<string, int> cartQuantities = new();

    protected override async Task OnInitializedAsync()
    {
        // 加载商品列表
        await LoadProducts();
        
        // 加载购物车状态
        await LoadCartQuantities();
        
        // 监听购物车变化
        CartService.CartChanged += OnCartChanged;
    }

    private async Task AddToCart(ProductDto product)
    {
        try
        {
            await CartService.AddToCartAsync(product, 1);
            await MessageService.Success("商品已添加到购物车");
        }
        catch (Exception ex)
        {
            await MessageService.Error("添加失败：" + ex.Message);
        }
    }

    private async Task UpdateQuantity(string productCode, int newQuantity)
    {
        if (newQuantity <= 0)
        {
            await CartService.RemoveFromCartAsync(productCode);
        }
        else
        {
            await CartService.UpdateQuantityAsync(productCode, newQuantity);
        }
    }

    private async Task LoadCartQuantities()
    {
        var items = await CartService.GetCartItemsAsync();
        cartQuantities = items.ToDictionary(x => x.ProductCode, x => x.Quantity);
        StateHasChanged();
    }

    private void OnCartChanged(CartSummary summary)
    {
        InvokeAsync(async () =>
        {
            await LoadCartQuantities();
        });
    }

    public void Dispose()
    {
        CartService.CartChanged -= OnCartChanged;
    }
}
```

#### 2. 购物车页面

```csharp
@page "/cart"
@inject IShoppingCartService CartService
@inject NavigationManager Navigation

<div class="cart-container">
    <h2>购物车 (@cartSummary.UniqueItems 种商品)</h2>
    
    @if (cartSummary.Items?.Any() == true)
    {
        <div class="cart-items">
            @foreach (var item in cartSummary.Items)
            {
                <div class="cart-item">
                    <img src="@item.ProductImage" alt="@item.ProductName" />
                    <div class="item-info">
                        <h4>@item.ProductName</h4>
                        <p>单价: ¥@item.UnitPrice</p>
                    </div>
                    <div class="quantity-controls">
                        <button @onclick="() => UpdateQuantity(item.ProductCode, item.Quantity - 1)">-</button>
                        <span>@item.Quantity</span>
                        <button @onclick="() => UpdateQuantity(item.ProductCode, item.Quantity + 1)">+</button>
                    </div>
                    <div class="item-total">
                        ¥@item.TotalPrice
                    </div>
                    <button @onclick="() => RemoveItem(item.ProductCode)" class="remove-btn">
                        删除
                    </button>
                </div>
            }
        </div>
        
        <div class="cart-summary">
            <p>总计: <strong>¥@cartSummary.TotalAmount</strong></p>
            <p>共 @cartSummary.TotalItems 件商品</p>
            
            <div class="cart-actions">
                <button @onclick="ClearCart" class="clear-btn">清空购物车</button>
                <button @onclick="Checkout" class="checkout-btn">去结算</button>
            </div>
        </div>
    }
    else
    {
        <div class="empty-cart">
            <p>购物车为空</p>
            <button @onclick="() => Navigation.NavigateTo(\"/products\")">
                继续购物
            </button>
        </div>
    }
</div>

@code {
    private CartSummary cartSummary = new();

    protected override async Task OnInitializedAsync()
    {
        await LoadCartSummary();
        CartService.CartChanged += OnCartChanged;
    }

    private async Task LoadCartSummary()
    {
        cartSummary = await CartService.GetCartSummaryAsync();
        StateHasChanged();
    }

    private async Task UpdateQuantity(string productCode, int newQuantity)
    {
        if (newQuantity <= 0)
        {
            await RemoveItem(productCode);
        }
        else
        {
            await CartService.UpdateQuantityAsync(productCode, newQuantity);
        }
    }

    private async Task RemoveItem(string productCode)
    {
        await CartService.RemoveFromCartAsync(productCode);
    }

    private async Task ClearCart()
    {
        await CartService.ClearCartAsync();
    }

    private async Task Checkout()
    {
        // 实现结算逻辑
        Navigation.NavigateTo("/checkout");
    }

    private void OnCartChanged(CartSummary summary)
    {
        InvokeAsync(async () =>
        {
            await LoadCartSummary();
        });
    }

    public void Dispose()
    {
        CartService.CartChanged -= OnCartChanged;
    }
}
```

#### 3. 购物车同步管理

```csharp
@page "/cart-sync"
@inject IShoppingCartService CartService
@inject IMessageService MessageService
@inject IAuthServiceNew AuthService

<div class="sync-container">
    <h3>购物车同步</h3>
    
    <div class="sync-status">
        <p>同步状态: @(needsSync ? "需要同步" : "已同步")</p>
        <p>最后同步时间: @lastSyncTime</p>
        <p>连接状态: @(isOnline ? "在线" : "离线")</p>
    </div>
    
    <div class="sync-actions">
        <button @onclick="ForceSyncToServer" disabled="@(!isOnline || !isAuthenticated)">
            强制同步到服务器
        </button>
        <button @onclick="PullFromServer" disabled="@(!isOnline || !isAuthenticated)">
            从服务器拉取
        </button>
        <button @onclick="CheckSyncStatus">
            检查同步状态
        </button>
    </div>
</div>

@code {
    private bool needsSync = false;
    private string lastSyncTime = "";
    private bool isOnline = true;
    private bool isAuthenticated = false;

    protected override async Task OnInitializedAsync()
    {
        isAuthenticated = await AuthService.IsAuthenticatedAsync();
        await CheckSyncStatus();
    }

    private async Task ForceSyncToServer()
    {
        try
        {
            // 注意：这需要 HybridShoppingCartService 的强制同步方法
            var hybridService = CartService as HybridShoppingCartService;
            if (hybridService != null)
            {
                var success = await hybridService.ForceSyncToServerAsync();
                if (success)
                {
                    await MessageService.Success("同步成功");
                }
                else
                {
                    await MessageService.Error("同步失败");
                }
            }
            
            await CheckSyncStatus();
        }
        catch (Exception ex)
        {
            await MessageService.Error("同步错误：" + ex.Message);
        }
    }

    private async Task PullFromServer()
    {
        try
        {
            var hybridService = CartService as HybridShoppingCartService;
            if (hybridService != null)
            {
                var success = await hybridService.PullFromServerAsync();
                if (success)
                {
                    await MessageService.Success("拉取成功");
                }
                else
                {
                    await MessageService.Error("拉取失败");
                }
            }
            
            await CheckSyncStatus();
        }
        catch (Exception ex)
        {
            await MessageService.Error("拉取错误：" + ex.Message);
        }
    }

    private async Task CheckSyncStatus()
    {
        try
        {
            var hybridService = CartService as HybridShoppingCartService;
            if (hybridService != null)
            {
                needsSync = await hybridService.NeedsSyncAsync();
            }
            
            // 获取最后同步时间（需要从LocalStorage获取）
            // lastSyncTime = await GetLastSyncTime();
            
            StateHasChanged();
        }
        catch (Exception ex)
        {
            await MessageService.Error("检查状态失败：" + ex.Message);
        }
    }
}
```

### 后端API使用示例

#### 1. API接口测试

```http
### 获取购物车
GET {{baseUrl}}/api/v1/cart?storeGuid={{storeGuid}}
Authorization: Bearer {{jwt_token}}

### 添加商品到购物车
POST {{baseUrl}}/api/v1/cart/add
Authorization: Bearer {{jwt_token}}
Content-Type: application/json

{
  "productGUID": "product-guid-123",
  "quantity": 2,
  "storeGUID": "store-guid-123"
}

### 更新购物车项数量
PUT {{baseUrl}}/api/v1/cart/update-quantity
Authorization: Bearer {{jwt_token}}
Content-Type: application/json

{
  "cartItemGUID": "cart-item-guid-123",
  "quantity": 3
}

### 同步本地购物车
POST {{baseUrl}}/api/v1/cart/sync
Authorization: Bearer {{jwt_token}}
Content-Type: application/json

{
  "storeGUID": "store-guid-123",
  "localCartItems": [
    {
      "productCode": "P001",
      "productName": "测试商品1",
      "unitPrice": 99.99,
      "quantity": 2,
      "volume": 0.001,
      "weight": 0.1,
      "minOrderQuantity": 1,
      "addedAt": "2024-01-01T10:00:00Z"
    }
  ]
}

### 将购物车转换为订单
POST {{baseUrl}}/api/v1/cart/checkout
Authorization: Bearer {{jwt_token}}
Content-Type: application/json

{
  "storeGuid": "store-guid-123"
}
```

## 🔧 配置说明

### 1. 前端配置（Program.cs）

```csharp
// 已经配置的服务
builder.Services.AddScoped<IShoppingCartService, HybridShoppingCartService>();
builder.Services.AddHttpClient<ICartServiceClient, CartServiceClient>();
```

### 2. 后端配置（Program.cs）

```csharp
// 已经配置的服务
builder.Services.AddScoped<ICartService, CartService>();
builder.Services.AddAutoMapper(typeof(Program).Assembly);
```

### 3. 数据库迁移

```csharp
// 需要在数据库中创建Cart和CartItem表
// 系统会自动通过SqlSugar的CodeFirst功能创建表结构
```

## 📝 最佳实践

### 1. 错误处理
```csharp
public async Task AddToCartWithErrorHandling(ProductDto product)
{
    try
    {
        await CartService.AddToCartAsync(product, 1);
    }
    catch (UnauthorizedAccessException)
    {
        // 用户未登录
        Navigation.NavigateTo("/login");
    }
    catch (ProductNotFoundException)
    {
        // 商品不存在
        await MessageService.Error("商品不存在或已下架");
    }
    catch (NetworkException)
    {
        // 网络错误，数据已保存到本地
        await MessageService.Warning("网络连接异常，数据已保存到本地，将在连接恢复后自动同步");
    }
    catch (Exception ex)
    {
        // 其他错误
        Logger.LogError(ex, "添加商品到购物车失败");
        await MessageService.Error("操作失败，请稍后重试");
    }
}
```

### 2. 性能优化
```csharp
// 使用防抖处理频繁的数量更新
private Timer? _quantityUpdateTimer;

private async Task UpdateQuantityWithDebounce(string productCode, int quantity)
{
    _quantityUpdateTimer?.Dispose();
    _quantityUpdateTimer = new Timer(async _ =>
    {
        await CartService.UpdateQuantityAsync(productCode, quantity);
    }, null, 500, Timeout.Infinite); // 500ms 防抖
}
```

### 3. 用户体验优化
```csharp
// 显示加载状态
private bool isLoading = false;

private async Task AddToCartWithLoading(ProductDto product)
{
    if (isLoading) return;
    
    isLoading = true;
    StateHasChanged();
    
    try
    {
        await CartService.AddToCartAsync(product, 1);
        await MessageService.Success("已添加到购物车");
    }
    finally
    {
        isLoading = false;
        StateHasChanged();
    }
}
```

## 🐛 故障排除

### 常见问题

1. **购物车数据不同步**
   - 检查网络连接状态
   - 检查用户认证状态
   - 手动触发强制同步

2. **本地数据丢失**
   - 检查浏览器localStorage配额
   - 检查是否在无痕模式下使用

3. **API调用失败**
   - 检查JWT令牌是否过期
   - 检查API服务器状态
   - 检查跨域配置

### 调试技巧

```csharp
// 启用购物车调试日志
public async Task DebugCartState()
{
    var items = await CartService.GetCartItemsAsync();
    var summary = await CartService.GetCartSummaryAsync();
    
    Console.WriteLine($"购物车商品数: {items.Count}");
    Console.WriteLine($"总金额: {summary.TotalAmount}");
    Console.WriteLine($"需要同步: {await (CartService as HybridShoppingCartService)?.NeedsSyncAsync()}");
}
```

## 🚀 扩展建议

### 1. 添加购物车分享功能
### 2. 实现购物车商品推荐
### 3. 支持购物车模板保存
### 4. 添加购物车数据分析
### 5. 实现多设备购物车同步

---

## 📞 支持

如有问题，请参考：
1. 检查编译错误和配置
2. 查看数据库连接状态  
3. 验证用户认证流程
4. 测试API接口可用性

完整的购物车同步系统已成功实现，支持离线使用和自动同步功能！🎉