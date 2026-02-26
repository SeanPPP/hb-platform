# 翻译服务使用文档

## 概述

翻译服务（TranslationService）是HB Platform多店铺管理系统的一个核心组件，用于自动翻译中文产品信息为英文，提升国际化用户体验。

## 功能特性

### 1. 中文文本检测
- **同步检测**: `ContainsChinese(string text)`
- **异步检测**: `DetectChineseAsync(string text)`
- 使用正则表达式检测Unicode范围内的中文字符

### 2. 单个文本翻译
- **方法**: `TranslateToEnglishAsync(string chineseText)`
- 支持多种翻译API（Kimi、百度、Google、Azure）
- 自动缓存翻译结果
- 翻译失败时降级到模拟翻译

### 3. 批量文本翻译
- **方法**: `BatchTranslateToEnglishAsync(List<string> chineseTexts)`
- 自动去重和过滤空值
- 支持API限流保护
- 返回Dictionary<string, string>格式

### 4. 翻译缓存管理
- **获取缓存**: `GetCachedTranslationAsync(string chineseText)`
- **设置缓存**: `CacheTranslationAsync(string chineseText, string englishText)`
- 内存缓存，最大10000条记录
- 线程安全的缓存操作

## 配置说明

在`appsettings.json`中配置翻译服务：

```json
{
  "Translation": {
    "Provider": "kimi",  // 可选值: kimi, baidu, google, azure, mock
    "Kimi": {
      "ApiKey": "your-kimi-api-key",
      "Model": "moonshot-v1-8k",
      "Endpoint": "https://api.moonshot.cn/v1/chat/completions"
    },
    "Baidu": {
      "AppId": "your-baidu-app-id",
      "SecretKey": "your-baidu-secret-key"
    },
    "Google": {
      "ApiKey": "your-google-api-key"
    },
    "Azure": {
      "Key": "your-azure-key",
      "Region": "your-azure-region",
      "Endpoint": "your-azure-endpoint"
    }
  }
}
```

## 在ContainerService中的应用

翻译服务已集成到`ContainerService`的`GetFilteredContainerProductsAsync`方法中：

1. **自动检测**: 扫描产品数据中的中文字段
2. **批量翻译**: 一次性翻译所有中文内容
3. **结果应用**: 将英文翻译添加到备注字段中
4. **错误处理**: 翻译失败时不影响主要功能

### 翻译字段包括：
- 商品名称 (`商品信息.商品名称`)
- 商品规格 (`商品信息.商品规格`)
- 备注信息 (`备注`)

### 翻译结果格式：
```
[EN] Name: Apple; Spec: Red color, 100g
```

## API端点

### 1. 检测中文文本
```http
POST /api/translation/detect-chinese
Content-Type: application/json

"苹果"
```

### 2. 翻译单个文本
```http
POST /api/translation/translate
Content-Type: application/json

{
  "text": "苹果"
}
```

### 3. 批量翻译
```http
POST /api/translation/batch-translate
Content-Type: application/json

{
  "texts": ["苹果", "香蕉", "橙子"]
}
```

### 4. 获取缓存翻译
```http
GET /api/translation/cached/{text}
```

## 使用示例

### 基本使用
```csharp
// 注入翻译服务
private readonly ITranslationService _translationService;

// 检测中文
bool containsChinese = _translationService.ContainsChinese("苹果");

// 单个翻译
string translation = await _translationService.TranslateToEnglishAsync("苹果");

// 批量翻译
var texts = new List<string> { "苹果", "香蕉", "橙子" };
var translations = await _translationService.BatchTranslateToEnglishAsync(texts);
```

### 错误处理
```csharp
try
{
    var translation = await _translationService.TranslateToEnglishAsync("苹果");
    // 使用翻译结果
}
catch (Exception ex)
{
    _logger.LogError(ex, "翻译失败");
    // 降级处理，使用原文
}
```

## 性能优化

1. **缓存机制**: 翻译结果自动缓存，避免重复翻译
2. **批量处理**: 使用批量翻译减少API调用次数
3. **限流保护**: 翻译间隔100ms，避免API限制
4. **降级策略**: API失败时使用模拟翻译

## 模拟翻译词典

开发环境中使用的模拟翻译词典包含常用词汇：

- 水果类: 苹果→Apple, 香蕉→Banana, 橙子→Orange
- 食品类: 牛奶→Milk, 面包→Bread, 鸡蛋→Egg
- 电子产品: 手机→Mobile Phone, 电脑→Computer
- 服装类: 衣服→Clothes, 裤子→Pants, 鞋子→Shoes
- 办公用品: 书→Book, 笔→Pen, 纸→Paper

## 扩展性

翻译服务设计为可扩展的架构：

1. **多提供商支持**: 轻松添加新的翻译API提供商
2. **配置驱动**: 通过配置文件切换翻译提供商
3. **接口抽象**: 实现了ITranslationService接口，易于替换
4. **缓存抽象**: 可扩展为Redis等外部缓存

## 注意事项

1. **API密钥安全**: 生产环境中请妥善保管API密钥
2. **成本控制**: 监控翻译API的使用量和成本
3. **限流处理**: 注意各翻译服务商的API限制
4. **错误处理**: 翻译失败时要有合理的降级策略
5. **缓存管理**: 定期清理缓存，避免内存溢出

## 日志记录

翻译服务会记录以下日志：

- **Information**: 成功翻译的统计信息
- **Warning**: 翻译失败但有降级方案
- **Error**: 严重错误，如API调用异常
- **Debug**: 详细的翻译过程信息（仅开发环境）

## 测试

使用`BlazorApp.Api.Test.TranslationServiceTest`类进行功能测试：

```bash
# 编译并运行测试
dotnet run --project BlazorApp.Api.Test
```

测试包括：
- 中文检测功能测试
- 单个翻译功能测试  
- 批量翻译功能测试
- 缓存功能测试