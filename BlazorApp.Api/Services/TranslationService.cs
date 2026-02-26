using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BlazorApp.Api.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BlazorApp.Api.Services
{
    /// <summary>
    /// 翻译服务实现类
    /// </summary>
    public class TranslationService : ITranslationService
    {
        private readonly ILogger<TranslationService> _logger;
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;
        private readonly Dictionary<string, string> _translationCache;
        private readonly SemaphoreSlim _cacheSemaphore;

        // 中文字符正则表达式
        private static readonly Regex ChineseRegex = new Regex(
            @"[\u4e00-\u9fff]",
            RegexOptions.Compiled
        );

        public TranslationService(
            ILogger<TranslationService> logger,
            IConfiguration configuration,
            HttpClient httpClient
        )
        {
            _logger = logger;
            _configuration = configuration;
            _httpClient = httpClient;
            _translationCache = new Dictionary<string, string>();
            _cacheSemaphore = new SemaphoreSlim(1, 1); //
        }

        /// <summary>
        /// 检测文本是否包含中文字符
        /// </summary>
        public bool ContainsChinese(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return ChineseRegex.IsMatch(text);
        }

        /// <summary>
        /// 异步检测文本是否包含中文字符
        /// </summary>
        public Task<bool> DetectChineseAsync(string text)
        {
            return Task.FromResult(ContainsChinese(text));
        }

        /// <summary>
        /// 翻译中文文本为英文
        /// </summary>
        public async Task<string> TranslateToEnglishAsync(string chineseText)
        {
            if (string.IsNullOrWhiteSpace(chineseText))
                return chineseText;

            // 如果不包含中文，直接返回
            if (!ContainsChinese(chineseText))
                return chineseText;

            try
            {
                // 首先检查缓存
                var cached = await GetCachedTranslationAsync(chineseText);
                if (!string.IsNullOrEmpty(cached))
                {
                    _logger.LogDebug($"从缓存获取翻译: {chineseText} -> {cached}");
                    return cached;
                }

                // 调用翻译API
                var translation = await CallTranslationApiAsync(chineseText);

                if (!string.IsNullOrEmpty(translation))
                {
                    // 缓存翻译结果
                    await CacheTranslationAsync(chineseText, translation);
                    _logger.LogInformation($"翻译完成: {chineseText} -> {translation}");
                    return translation;
                }

                // 如果翻译失败，返回原文
                _logger.LogWarning($"翻译失败，返回原文: {chineseText}");
                return chineseText;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"翻译过程中发生错误: {chineseText}");
                return chineseText; // 翻译失败时返回原文
            }
        }

        /// <summary>
        /// 批量翻译中文文本
        /// </summary>
        public async Task<Dictionary<string, string>> BatchTranslateToEnglishAsync(
            List<string> chineseTexts
        )
        {
            var results = new Dictionary<string, string>();

            if (chineseTexts?.Any() != true)
                return results;

            try
            {
                // 去重和过滤空值，只保留包含中文的文本
                var uniqueTexts = chineseTexts
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Distinct()
                    .ToList();

                if (!uniqueTexts.Any())
                    return results;

                _logger.LogInformation($"开始批量翻译 {uniqueTexts.Count} 个文本");

                // 先从缓存中获取已翻译的内容
                var uncachedTexts = new List<string>();
                foreach (var text in uniqueTexts)
                {
                    if (!ContainsChinese(text))
                    {
                        // 不包含中文的直接返回原文
                        results[text] = text;
                        continue;
                    }

                    var cached = await GetCachedTranslationAsync(text);
                    if (!string.IsNullOrEmpty(cached))
                    {
                        results[text] = cached;
                    }
                    else
                    {
                        uncachedTexts.Add(text);
                    }
                }

                // 如果所有文本都已缓存，直接返回
                if (!uncachedTexts.Any())
                {
                    _logger.LogInformation($"批量翻译完成，{results.Count} 个文本全部来自缓存");
                    return results;
                }

                _logger.LogInformation($"需要翻译 {uncachedTexts.Count} 个未缓存的文本");

                // 批量调用翻译API
                var translations = await CallBatchTranslationApiAsync(uncachedTexts);

                // 将翻译结果添加到总结果中，并缓存
                foreach (var kvp in translations)
                {
                    results[kvp.Key] = kvp.Value;
                    await CacheTranslationAsync(kvp.Key, kvp.Value);
                }

                // 对于未成功翻译的文本，返回原文
                foreach (var text in uncachedTexts)
                {
                    if (!results.ContainsKey(text))
                    {
                        results[text] = text;
                        _logger.LogWarning($"文本翻译失败，返回原文: {text}");
                    }
                }

                _logger.LogInformation(
                    $"批量翻译完成，成功翻译 {translations.Count} 个新文本，总共返回 {results.Count} 个结果"
                );
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量翻译失败");

                // 即使出错也要返回已翻译的内容，未翻译的返回原文
                foreach (
                    var text in chineseTexts.Where(t =>
                        !string.IsNullOrWhiteSpace(t) && !results.ContainsKey(t)
                    )
                )
                {
                    results[text] = text;
                }

                return results;
            }
        }

        /// <summary>
        /// 获取缓存的翻译
        /// </summary>
        public async Task<string?> GetCachedTranslationAsync(string chineseText)
        {
            if (string.IsNullOrWhiteSpace(chineseText))
                return null;

            await _cacheSemaphore.WaitAsync();
            try
            {
                _translationCache.TryGetValue(chineseText, out var translation);
                return translation;
            }
            finally
            {
                _cacheSemaphore.Release();
            }
        }

        /// <summary>
        /// 缓存翻译结果
        /// </summary>
        public async Task CacheTranslationAsync(string chineseText, string englishText)
        {
            if (string.IsNullOrWhiteSpace(chineseText) || string.IsNullOrWhiteSpace(englishText))
                return;

            await _cacheSemaphore.WaitAsync();
            try
            {
                // 限制缓存大小，避免内存溢出
                if (_translationCache.Count >= 10000)
                {
                    // 清理一半的缓存
                    var keysToRemove = _translationCache.Keys.Take(5000).ToList();
                    foreach (var key in keysToRemove)
                    {
                        _translationCache.Remove(key);
                    }
                    _logger.LogInformation("清理翻译缓存，移除 {Count} 个条目", keysToRemove.Count);
                }

                _translationCache[chineseText] = englishText;
            }
            finally
            {
                _cacheSemaphore.Release();
            }
        }

        /// <summary>
        /// 调用翻译API
        /// </summary>
        private async Task<string> CallTranslationApiAsync(string text)
        {
            try
            {
                var provider = _configuration.GetValue<string>("Translation:Provider") ?? "mock";

                return provider.ToLower() switch
                {
                    "kimi" => await CallKimiApiAsync(text),
                    "baidu" => await CallBaiduApiAsync(text),
                    "google" => await CallGoogleApiAsync(text),
                    "azure" => await CallAzureApiAsync(text),
                    _ => await CallMockApiAsync(text),
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "调用翻译API失败: {Text}", text);
                return await CallMockApiAsync(text); // 降级到模拟翻译
            }
        }

        /// <summary>
        /// 批量调用翻译API
        /// </summary>
        private async Task<Dictionary<string, string>> CallBatchTranslationApiAsync(
            List<string> texts
        )
        {
            try
            {
                var provider = _configuration.GetValue<string>("Translation:Provider") ?? "mock";

                return provider.ToLower() switch
                {
                    "kimi" => await CallKimiBatchApiAsync(texts),
                    "baidu" => await CallBaiduBatchApiAsync(texts),
                    "google" => await CallGoogleBatchApiAsync(texts),
                    "azure" => await CallAzureBatchApiAsync(texts),
                    _ => await CallMockBatchApiAsync(texts),
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "调用批量翻译API失败: {Count} 个文本", texts.Count);
                return await CallMockBatchApiAsync(texts); // 降级到模拟翻译
            }
        }

        /// <summary>
        /// 模拟翻译API（开发环境使用）
        /// </summary>
        private async Task<string> CallMockApiAsync(string text)
        {
            await Task.Delay(50); // 模拟网络延时

            // 简单的中英文对照词典
            var mockDictionary = new Dictionary<string, string>
            {
                { "苹果", "Apple" },
                { "香蕉", "Banana" },
                { "橙子", "Orange" },
                { "牛奶", "Milk" },
                { "面包", "Bread" },
                { "鸡蛋", "Egg" },
                { "大米", "Rice" },
                { "面条", "Noodles" },
                { "蔬菜", "Vegetables" },
                { "水果", "Fruits" },
                { "手机", "Mobile Phone" },
                { "电脑", "Computer" },
                { "键盘", "Keyboard" },
                { "鼠标", "Mouse" },
                { "显示器", "Monitor" },
                { "打印机", "Printer" },
                { "扫描仪", "Scanner" },
                { "摄像头", "Camera" },
                { "耳机", "Headphones" },
                { "音响", "Speaker" },
                { "衣服", "Clothes" },
                { "裤子", "Pants" },
                { "鞋子", "Shoes" },
                { "帽子", "Hat" },
                { "包", "Bag" },
                { "手表", "Watch" },
                { "眼镜", "Glasses" },
                { "书", "Book" },
                { "笔", "Pen" },
                { "纸", "Paper" },
            };

            // 尝试完全匹配
            if (mockDictionary.TryGetValue(text, out var translation))
            {
                return translation;
            }

            // 尝试部分匹配
            foreach (var kvp in mockDictionary)
            {
                if (text.Contains(kvp.Key))
                {
                    return text.Replace(kvp.Key, kvp.Value);
                }
            }

            // 如果没有匹配，返回原文
            return text;
        }

        /// <summary>
        /// 批量模拟翻译API（开发环境使用）
        /// </summary>
        private async Task<Dictionary<string, string>> CallMockBatchApiAsync(List<string> texts)
        {
            var results = new Dictionary<string, string>();

            if (!texts.Any())
                return results;

            await Task.Delay(50); // 模拟网络延时

            // 简单的中英文对照词典
            var mockDictionary = new Dictionary<string, string>
            {
                { "苹果", "Apple" },
                { "香蕉", "Banana" },
                { "橙子", "Orange" },
                { "牛奶", "Milk" },
                { "面包", "Bread" },
                { "鸡蛋", "Egg" },
                { "大米", "Rice" },
                { "面条", "Noodles" },
                { "蔬菜", "Vegetables" },
                { "水果", "Fruits" },
                { "手机", "Mobile Phone" },
                { "电脑", "Computer" },
                { "键盘", "Keyboard" },
                { "鼠标", "Mouse" },
                { "显示器", "Monitor" },
                { "打印机", "Printer" },
                { "扫描仪", "Scanner" },
                { "摄像头", "Camera" },
                { "耳机", "Headphones" },
                { "音响", "Speaker" },
                { "衣服", "Clothes" },
                { "裤子", "Pants" },
                { "鞋子", "Shoes" },
                { "帽子", "Hat" },
                { "包", "Bag" },
                { "手表", "Watch" },
                { "眼镜", "Glasses" },
                { "书", "Book" },
                { "笔", "Pen" },
                { "纸", "Paper" },
            };

            foreach (var text in texts)
            {
                // 尝试完全匹配
                if (mockDictionary.TryGetValue(text, out var translation))
                {
                    results[text] = translation;
                    continue;
                }

                // 尝试部分匹配
                var found = false;
                foreach (var kvp in mockDictionary)
                {
                    if (text.Contains(kvp.Key))
                    {
                        results[text] = text.Replace(kvp.Key, kvp.Value);
                        found = true;
                        break;
                    }
                }

                // 如果没有匹配，返回原文
                if (!found)
                {
                    results[text] = text;
                }
            }

            _logger.LogInformation(
                $"模拟批量翻译完成，处理 {texts.Count} 个文本，成功翻译 {results.Count(r => r.Key != r.Value)} 个"
            );
            return results;
        }

        /// <summary>
        /// 调用Kimi翻译API
        /// </summary>
        private async Task<string> CallKimiApiAsync(string text)
        {
            var apiKey = _configuration.GetValue<string>("Translation:Kimi:ApiKey");
            var model =
                _configuration.GetValue<string>("Translation:Kimi:Model") ?? "moonshot-v1-128k";
            var endpoint =
                _configuration.GetValue<string>("Translation:Kimi:Endpoint")
                ?? "https://api.moonshot.cn/v1/chat/completions";

            if (string.IsNullOrEmpty(apiKey))
            {
                _logger.LogWarning("Kimi翻译API配置不完整，降级到模拟翻译");
                return await CallMockApiAsync(text);
            }

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

                var requestBody = new
                {
                    model = model,
                    messages = new[]
                    {
                        new
                        {
                            role = "user",
                            content = $"把这句话翻译成英文，只返回翻译结果，不要其他说明：{text}",
                        },
                    },
                };

                var json = JsonSerializer.Serialize(requestBody);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

                _logger.LogDebug("调用Kimi API翻译: {Text}", text);

                var response = await client.PostAsync(endpoint, httpContent);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError(
                        "Kimi API调用失败，状态码: {StatusCode}, 内容: {Content}",
                        response.StatusCode,
                        await response.Content.ReadAsStringAsync()
                    );
                    return await CallMockApiAsync(text);
                }

                var result = await response.Content.ReadAsStringAsync();
                _logger.LogDebug("Kimi API返回结果: {Result}", result);

                // 解析Kimi API响应
                var jsonDocument = JsonDocument.Parse(result);
                var translation = jsonDocument
                    .RootElement.GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                if (!string.IsNullOrEmpty(translation))
                {
                    // 清理可能的多余格式
                    translation = translation.Trim().Trim('"').Trim();
                    _logger.LogInformation(
                        "Kimi翻译成功: {Chinese} -> {English}",
                        text,
                        translation
                    );
                    return translation;
                }

                _logger.LogWarning("Kimi API返回空翻译结果");
                return await CallMockApiAsync(text);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "调用Kimi API时发生异常: {Text}", text);
                return await CallMockApiAsync(text);
            }
        }

        /// <summary>
        /// 批量调用Kimi翻译API
        /// </summary>
        private async Task<Dictionary<string, string>> CallKimiBatchApiAsync(List<string> texts)
        {
            var results = new Dictionary<string, string>();

            if (!texts.Any())
                return results;

            var apiKey = _configuration.GetValue<string>("Translation:Kimi:ApiKey");
            var model =
                _configuration.GetValue<string>("Translation:Kimi:Model") ?? "moonshot-v1-128k";
            var endpoint =
                _configuration.GetValue<string>("Translation:Kimi:Endpoint")
                ?? "https://api.moonshot.cn/v1/chat/completions";

            if (string.IsNullOrEmpty(apiKey))
            {
                _logger.LogWarning("Kimi翻译API配置不完整，降级到模拟翻译");
                return await CallMockBatchApiAsync(texts);
            }

            try
            {
                // 根据模型类型设置批次大小，避免超过token限制
                var batchSize = model.Contains("128k") ? 100 : 30; // 128k模型可以处理更多文本
                var batches = new List<List<string>>();

                for (int i = 0; i < texts.Count; i += batchSize)
                {
                    batches.Add(texts.Skip(i).Take(batchSize).ToList());
                }

                _logger.LogInformation(
                    "将 {TotalCount} 个文本分为 {BatchCount} 批处理，每批最多 {BatchSize} 个",
                    texts.Count,
                    batches.Count,
                    batchSize
                );

                using var client = new HttpClient();
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

                // 根据配置选择串行或并行处理
                var enableParallel = _configuration.GetValue<bool>(
                    "Translation:Kimi:EnableParallelProcessing",
                    false
                );
                var maxConcurrent = _configuration.GetValue<int>(
                    "Translation:Kimi:MaxConcurrentBatches",
                    1
                );
                var batchDelayMs = _configuration.GetValue<int>(
                    "Translation:Kimi:BatchDelayMs",
                    100
                );

                if (enableParallel && batches.Count > 1)
                {
                    _logger.LogInformation(
                        "使用并行处理，最大并发数: {MaxConcurrent}",
                        maxConcurrent
                    );
                    results = await ProcessBatchesInParallelAsync(
                        client,
                        batches,
                        model,
                        endpoint,
                        maxConcurrent
                    );
                }
                else
                {
                    _logger.LogInformation("使用串行处理");
                    results = await ProcessBatchesSequentiallyAsync(
                        client,
                        batches,
                        model,
                        endpoint,
                        batchDelayMs
                    );
                }

                _logger.LogInformation("Kimi批量翻译完成: 总共 {Count} 个文本", results.Count);
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "调用Kimi批量翻译API时发生异常，文本数量: {Count}",
                    texts.Count
                );
                return await CallMockBatchApiAsync(texts);
            }
        }

        /// <summary>
        /// 处理单个批次的Kimi翻译
        /// </summary>
        private async Task<Dictionary<string, string>> ProcessKimiBatchAsync(
            HttpClient client,
            List<string> texts,
            string model,
            string endpoint
        )
        {
            var results = new Dictionary<string, string>();

            try
            {
                // 构建批量翻译请求，将多个文本组合成一个请求
                var combinedText = string.Join(
                    "\n---\n",
                    texts.Select((text, index) => $"{index + 1}. {text}")
                );
                var prompt =
                    $"请把下面用---分隔的{texts.Count}个中文文本翻译成英文，保持原来的编号格式，每行一个翻译结果：\n{combinedText}";

                var requestBody = new
                {
                    model = model,
                    messages = new[] { new { role = "user", content = prompt } },
                    temperature = 0.3,
                    max_tokens = Math.Max(2000, texts.Count * 50),
                };

                var json = JsonSerializer.Serialize(requestBody);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

                _logger.LogDebug("调用Kimi批量翻译API，批次文本数量: {Count}", texts.Count);

                var response = await client.PostAsync(endpoint, httpContent);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError(
                        "Kimi批量翻译API调用失败，状态码: {StatusCode}, 内容: {Content}",
                        response.StatusCode,
                        await response.Content.ReadAsStringAsync()
                    );

                    // 如果API调用失败，使用模拟翻译
                    return await CallMockBatchApiAsync(texts);
                }

                var result = await response.Content.ReadAsStringAsync();
                _logger.LogDebug("Kimi批量翻译API返回结果: {Result}", result);

                // 解析Kimi API响应
                var jsonDocument = JsonDocument.Parse(result);
                var translationResult = jsonDocument
                    .RootElement.GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                if (!string.IsNullOrEmpty(translationResult))
                {
                    // 解析返回的翻译结果
                    var lines = translationResult.Split(
                        '\n',
                        StringSplitOptions.RemoveEmptyEntries
                    );

                    // 使用编号匹配而不是索引匹配，避免错位
                    foreach (var line in lines)
                    {
                        var trimmedLine = line.Trim();

                        // 提取编号和翻译内容（如 "1. Translation"）
                        var match = System.Text.RegularExpressions.Regex.Match(
                            trimmedLine,
                            @"^(\d+)\.\s*(.+)$"
                        );
                        if (match.Success)
                        {
                            var numberStr = match.Groups[1].Value;
                            var translation = match.Groups[2].Value.Trim().Trim('"').Trim();

                            // 解析编号
                            if (
                                int.TryParse(numberStr, out var number)
                                && number > 0
                                && number <= texts.Count
                            )
                            {
                                var index = number - 1; // 编号从1开始，索引从0开始
                                var originalText = texts[index];

                                if (
                                    !string.IsNullOrEmpty(translation)
                                    && translation != originalText
                                )
                                {
                                    results[originalText] = translation;
                                    _logger.LogDebug(
                                        "翻译匹配成功: [{Number}] {Chinese} → {English}",
                                        number,
                                        originalText,
                                        translation
                                    );
                                }
                                else
                                {
                                    results[originalText] = originalText;
                                    _logger.LogWarning(
                                        "翻译无效或与原文相同: [{Number}] {Chinese}",
                                        number,
                                        originalText
                                    );
                                }
                            }
                            else
                            {
                                _logger.LogWarning(
                                    "编号超出范围或无效: {Number}, 总数: {Count}",
                                    number,
                                    texts.Count
                                );
                            }
                        }
                        else
                        {
                            _logger.LogDebug("无法解析编号格式的行: {Line}", trimmedLine);
                        }
                    }

                    // 处理未翻译的文本（Kimi可能跳过某些翻译）
                    foreach (var text in texts)
                    {
                        if (!results.ContainsKey(text))
                        {
                            results[text] = text; // 返回原文
                            _logger.LogWarning("文本未被翻译（Kimi跳过）: {Chinese}", text);
                        }
                    }

                    _logger.LogInformation("Kimi批次翻译成功: {Count} 个文本", results.Count);
                    return results;
                }

                _logger.LogWarning("Kimi批量翻译API返回空结果");
                return await CallMockBatchApiAsync(texts);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理Kimi批次翻译时发生异常，文本数量: {Count}", texts.Count);
                return await CallMockBatchApiAsync(texts);
            }
        }

        /// <summary>
        /// 调用百度翻译API
        /// </summary>
        private async Task<string> CallBaiduApiAsync(string text)
        {
            var appId = _configuration.GetValue<string>("Translation:Baidu:AppId");
            var secretKey = _configuration.GetValue<string>("Translation:Baidu:SecretKey");

            if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(secretKey))
            {
                _logger.LogWarning("百度翻译API配置不完整，降级到模拟翻译");
                return await CallMockApiAsync(text);
            }

            // 实现百度翻译API调用逻辑
            // 这里暂时返回模拟结果
            return await CallMockApiAsync(text);
        }

        /// <summary>
        /// 调用Google翻译API
        /// </summary>
        private async Task<string> CallGoogleApiAsync(string text)
        {
            var apiKey = _configuration.GetValue<string>("Translation:Google:ApiKey");

            if (string.IsNullOrEmpty(apiKey))
            {
                _logger.LogWarning("Google翻译API配置不完整，降级到模拟翻译");
                return await CallMockApiAsync(text);
            }

            // 实现Google翻译API调用逻辑
            // 这里暂时返回模拟结果
            return await CallMockApiAsync(text);
        }

        /// <summary>
        /// 调用Azure翻译API
        /// </summary>
        private async Task<string> CallAzureApiAsync(string text)
        {
            var key = _configuration.GetValue<string>("Translation:Azure:Key");
            var region = _configuration.GetValue<string>("Translation:Azure:Region");
            var endpoint = _configuration.GetValue<string>("Translation:Azure:Endpoint");

            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(region))
            {
                _logger.LogWarning("Azure翻译API配置不完整，降级到模拟翻译");
                return await CallMockApiAsync(text);
            }

            // 实现Azure翻译API调用逻辑
            // 这里暂时返回模拟结果
            return await CallMockApiAsync(text);
        }

        /// <summary>
        /// 批量调用百度翻译API
        /// </summary>
        private async Task<Dictionary<string, string>> CallBaiduBatchApiAsync(List<string> texts)
        {
            var appId = _configuration.GetValue<string>("Translation:Baidu:AppId");
            var secretKey = _configuration.GetValue<string>("Translation:Baidu:SecretKey");

            if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(secretKey))
            {
                _logger.LogWarning("百度翻译API配置不完整，降级到模拟翻译");
                return await CallMockBatchApiAsync(texts);
            }

            // 实现百度批量翻译API调用逻辑
            // 这里暂时返回模拟结果
            return await CallMockBatchApiAsync(texts);
        }

        /// <summary>
        /// 批量调用Google翻译API
        /// </summary>
        private async Task<Dictionary<string, string>> CallGoogleBatchApiAsync(List<string> texts)
        {
            var apiKey = _configuration.GetValue<string>("Translation:Google:ApiKey");

            if (string.IsNullOrEmpty(apiKey))
            {
                _logger.LogWarning("Google翻译API配置不完整，降级到模拟翻译");
                return await CallMockBatchApiAsync(texts);
            }

            // 实现Google批量翻译API调用逻辑
            // 这里暂时返回模拟结果
            return await CallMockBatchApiAsync(texts);
        }

        /// <summary>
        /// 批量调用Azure翻译API
        /// </summary>
        private async Task<Dictionary<string, string>> CallAzureBatchApiAsync(List<string> texts)
        {
            var key = _configuration.GetValue<string>("Translation:Azure:Key");
            var region = _configuration.GetValue<string>("Translation:Azure:Region");
            var endpoint = _configuration.GetValue<string>("Translation:Azure:Endpoint");

            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(region))
            {
                _logger.LogWarning("Azure翻译API配置不完整，降级到模拟翻译");
                return await CallMockBatchApiAsync(texts);
            }

            // 实现Azure批量翻译API调用逻辑
            // 这里暂时返回模拟结果
            return await CallMockBatchApiAsync(texts);
        }

        /// <summary>
        /// 串行处理批次
        /// </summary>
        private async Task<Dictionary<string, string>> ProcessBatchesSequentiallyAsync(
            HttpClient client,
            List<List<string>> batches,
            string model,
            string endpoint,
            int batchDelayMs
        )
        {
            var results = new Dictionary<string, string>();

            foreach (var batch in batches)
            {
                var batchResults = await ProcessKimiBatchAsync(client, batch, model, endpoint);
                foreach (var kvp in batchResults)
                {
                    results[kvp.Key] = kvp.Value;
                }

                // 批次间添加延时，避免API限流
                if (batches.Count > 1)
                {
                    await Task.Delay(batchDelayMs);
                }
            }

            return results;
        }

        /// <summary>
        /// 并行处理批次（带并发控制）
        /// </summary>
        private async Task<Dictionary<string, string>> ProcessBatchesInParallelAsync(
            HttpClient client,
            List<List<string>> batches,
            string model,
            string endpoint,
            int maxConcurrent
        )
        {
            var results = new Dictionary<string, string>();

            // 使用信号量控制并发数
            using var semaphore = new SemaphoreSlim(maxConcurrent, maxConcurrent);

            var tasks = batches.Select(async batch =>
            {
                await semaphore.WaitAsync();
                try
                {
                    return await ProcessKimiBatchAsync(client, batch, model, endpoint);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var allBatchResults = await Task.WhenAll(tasks);

            // 合并所有批次结果
            foreach (var batchResults in allBatchResults)
            {
                foreach (var kvp in batchResults)
                {
                    results[kvp.Key] = kvp.Value;
                }
            }

            return results;
        }
    }
}
