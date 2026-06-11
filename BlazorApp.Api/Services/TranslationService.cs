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

        private bool IsValidEnglishTranslation(string sourceText, string? translatedText)
        {
            var normalizedSource = sourceText.Trim();
            var normalizedTranslation = translatedText?.Trim();

            // 只把真正的英文译文视为有效结果，避免把原中文或含中文结果写入缓存。
            return !string.IsNullOrEmpty(normalizedTranslation)
                && !string.Equals(normalizedTranslation, normalizedSource, StringComparison.Ordinal)
                && !ContainsChinese(normalizedTranslation);
        }

        private sealed class TranslationProviderConfigurationException : InvalidOperationException
        {
            public TranslationProviderConfigurationException(string message)
                : base(message) { }
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

                if (IsValidEnglishTranslation(chineseText, translation))
                {
                    // 只有有效英文译文才写入缓存，避免后续批量翻译读到原中文。
                    await CacheTranslationAsync(chineseText, translation);
                    _logger.LogInformation($"翻译完成: {chineseText} -> {translation}");
                    return translation;
                }

                if (!string.IsNullOrWhiteSpace(translation))
                {
                    _logger.LogWarning($"翻译结果不是有效英文，返回原文且不缓存: {chineseText} -> {translation}");
                    return translation;
                }

                // 如果翻译失败，返回原文
                _logger.LogWarning($"翻译失败，返回原文: {chineseText}");
                return chineseText;
            }
            catch (TranslationProviderConfigurationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"翻译过程中发生错误: {chineseText}");
                var provider = _configuration.GetValue<string>("Translation:Provider") ?? "mock";
                if (!string.Equals(provider, "mock", StringComparison.OrdinalIgnoreCase))
                {
                    throw;
                }

                return chineseText; // 翻译失败时返回原文
            }
        }

        /// <summary>
        /// 翻译文本为指定目标语言。
        /// </summary>
        public async Task<string> TranslateAsync(string text, string targetLanguage)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            var normalizedTargetLanguage = targetLanguage?.Trim().ToLowerInvariant();
            if (normalizedTargetLanguage is "en" or "en-us" or "en-au")
            {
                return await TranslateToEnglishAsync(text);
            }

            if (normalizedTargetLanguage is not ("zh" or "zh-cn"))
            {
                throw new ArgumentException($"不支持的目标语言: {targetLanguage}");
            }

            return await CallTranslationApiAsync(text, "zh");
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

                // 将翻译结果添加到总结果中；只有有效英文结果才进入缓存。
                foreach (var kvp in translations)
                {
                    results[kvp.Key] = kvp.Value;

                    if (IsValidEnglishTranslation(kvp.Key, kvp.Value))
                    {
                        await CacheTranslationAsync(kvp.Key, kvp.Value);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "跳过无效英文翻译缓存: {ChineseText} -> {EnglishText}",
                            kvp.Key,
                            kvp.Value
                        );
                    }
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
                    "批量翻译完成，有效翻译 {TranslatedCount} 个新文本，总共返回 {ResultCount} 个结果",
                    translations.Count(kvp => IsValidEnglishTranslation(kvp.Key, kvp.Value)),
                    results.Count
                );
                return results;
            }
            catch (TranslationProviderConfigurationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量翻译失败");
                var provider = _configuration.GetValue<string>("Translation:Provider") ?? "mock";
                if (!string.Equals(provider, "mock", StringComparison.OrdinalIgnoreCase))
                {
                    throw;
                }

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
                if (
                    !string.IsNullOrEmpty(translation)
                    && !IsValidEnglishTranslation(chineseText, translation)
                )
                {
                    // 旧版本可能缓存了原中文或含中文结果，读取时清理，确保后续重新调用真实翻译。
                    _translationCache.Remove(chineseText);
                    _logger.LogWarning(
                        "清理无效英文翻译缓存: {ChineseText} -> {EnglishText}",
                        chineseText,
                        translation
                    );
                    return null;
                }

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
            return await CallTranslationApiAsync(text, "en");
        }

        /// <summary>
        /// 调用翻译API并指定目标语言。
        /// </summary>
        private async Task<string> CallTranslationApiAsync(string text, string targetLanguage)
        {
            var provider = _configuration.GetValue<string>("Translation:Provider") ?? "mock";
            try
            {
                return provider.ToLower() switch
                {
                    "deepseek" => await CallDeepSeekApiAsync(text, targetLanguage),
                    "kimi" => await CallKimiApiAsync(text, targetLanguage),
                    "baidu" when targetLanguage == "en" => await CallBaiduApiAsync(text),
                    "google" when targetLanguage == "en" => await CallGoogleApiAsync(text),
                    "azure" when targetLanguage == "en" => await CallAzureApiAsync(text),
                    _ => await CallMockApiAsync(text, targetLanguage),
                };
            }
            catch (TranslationProviderConfigurationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "调用翻译API失败: {Text}", text);
                if (!string.Equals(provider, "mock", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"{GetProviderDisplayName(provider)} 翻译调用失败。", ex);
                }

                return await CallMockApiAsync(text, targetLanguage); // 降级到模拟翻译
            }
        }

        /// <summary>
        /// 批量调用翻译API
        /// </summary>
        private async Task<Dictionary<string, string>> CallBatchTranslationApiAsync(
            List<string> texts
        )
        {
            var provider = _configuration.GetValue<string>("Translation:Provider") ?? "mock";
            try
            {
                return provider.ToLower() switch
                {
                    "deepseek" => await CallDeepSeekBatchApiAsync(texts),
                    "kimi" => await CallKimiBatchApiAsync(texts),
                    "baidu" => await CallBaiduBatchApiAsync(texts),
                    "google" => await CallGoogleBatchApiAsync(texts),
                    "azure" => await CallAzureBatchApiAsync(texts),
                    _ => await CallMockBatchApiAsync(texts),
                };
            }
            catch (TranslationProviderConfigurationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "调用批量翻译API失败: {Count} 个文本", texts.Count);
                if (!string.Equals(provider, "mock", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"{GetProviderDisplayName(provider)} 批量翻译调用失败。", ex);
                }

                return await CallMockBatchApiAsync(texts); // 降级到模拟翻译
            }
        }

        /// <summary>
        /// 模拟翻译API（开发环境使用）
        /// </summary>
        private async Task<string> CallMockApiAsync(string text)
        {
            return await CallMockApiAsync(text, "en");
        }

        private static string GetProviderDisplayName(string provider)
        {
            return provider.Equals("deepseek", StringComparison.OrdinalIgnoreCase)
                ? "DeepSeek"
                : provider;
        }

        /// <summary>
        /// 模拟翻译API（开发环境使用），支持中英双向。
        /// </summary>
        private async Task<string> CallMockApiAsync(string text, string targetLanguage)
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
                { "您好", "Hello" },
                { "附件", "attachment" },
                { "请查收", "please find attached" },
                { "谢谢", "Thank you" },
                { "发票", "invoice" },
            };

            if (targetLanguage == "zh")
            {
                var reversedDictionary = mockDictionary
                    .GroupBy(item => item.Value, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        item => item.Key,
                        item => item.First().Key,
                        StringComparer.OrdinalIgnoreCase
                    );
                if (reversedDictionary.TryGetValue(text, out var chineseTranslation))
                {
                    return chineseTranslation;
                }

                foreach (var kvp in reversedDictionary)
                {
                    if (text.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        return Regex.Replace(
                            text,
                            Regex.Escape(kvp.Key),
                            kvp.Value,
                            RegexOptions.IgnoreCase
                        );
                    }
                }

                return text;
            }

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

        private static string? ExtractChatCompletionContent(string result)
        {
            // DeepSeek 与 Kimi 都兼容 chat/completions 响应结构，统一从首个 choice 读取译文。
            using var jsonDocument = JsonDocument.Parse(result);
            return jsonDocument
                .RootElement.GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
        }

        private static void SetBearerToken(HttpClient client, string apiKey)
        {
            // 每次调用前刷新 Authorization，确保配置变更或测试注入时不会沿用旧 token。
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        }

        /// <summary>
        /// 调用DeepSeek翻译API并指定目标语言
        /// </summary>
        private async Task<string> CallDeepSeekApiAsync(string text, string targetLanguage)
        {
            var apiKey = _configuration.GetValue<string>("Translation:DeepSeek:ApiKey");
            var model =
                _configuration.GetValue<string>("Translation:DeepSeek:Model") ?? "deepseek-v4-flash";
            var endpoint =
                _configuration.GetValue<string>("Translation:DeepSeek:Endpoint")
                ?? "https://api.deepseek.com/chat/completions";

            if (string.IsNullOrEmpty(apiKey))
            {
                throw new TranslationProviderConfigurationException(
                    "DeepSeek ApiKey 未配置，请通过环境变量 Translation__DeepSeek__ApiKey 注入。"
                );
            }

            try
            {
                SetBearerToken(_httpClient, apiKey);

                var requestBody = new
                {
                    model = model,
                    messages = new[]
                    {
                        new
                        {
                            role = "user",
                            content =
                                targetLanguage == "zh"
                                    ? $"把这段邮件内容翻译成中文，只返回翻译结果，不要其他说明：{text}"
                                    : $"把这段邮件内容翻译成英文，只返回翻译结果，不要其他说明：{text}",
                        },
                    },
                    temperature = 0.1,
                    max_tokens = 1000,
                    stream = false,
                };

                var json = JsonSerializer.Serialize(requestBody);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

                _logger.LogDebug("调用DeepSeek API翻译: {Text}", text);

                var response = await _httpClient.PostAsync(endpoint, httpContent);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError(
                        "DeepSeek API调用失败，状态码: {StatusCode}, 内容: {Content}",
                        response.StatusCode,
                        errorContent
                    );
                    throw new InvalidOperationException(
                        $"DeepSeek API 调用失败，状态码: {response.StatusCode}"
                    );
                }

                var result = await response.Content.ReadAsStringAsync();
                _logger.LogDebug("DeepSeek API返回结果: {Result}", result);

                var translation = ExtractChatCompletionContent(result);

                if (!string.IsNullOrEmpty(translation))
                {
                    translation = translation.Trim().Trim('"').Trim();
                    _logger.LogInformation(
                        "DeepSeek翻译成功: {SourceText} -> {Translation}",
                        text,
                        translation
                    );
                    return translation;
                }

                _logger.LogWarning("DeepSeek API返回空翻译结果");
                throw new InvalidOperationException("DeepSeek API 返回空翻译结果。");
            }
            catch (TranslationProviderConfigurationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "调用DeepSeek API时发生异常: {Text}", text);
                throw new InvalidOperationException("DeepSeek 翻译调用失败。", ex);
            }
        }

        /// <summary>
        /// 批量调用DeepSeek翻译API
        /// </summary>
        private async Task<Dictionary<string, string>> CallDeepSeekBatchApiAsync(List<string> texts)
        {
            var results = new Dictionary<string, string>();

            if (!texts.Any())
                return results;

            var apiKey = _configuration.GetValue<string>("Translation:DeepSeek:ApiKey");
            var model =
                _configuration.GetValue<string>("Translation:DeepSeek:Model") ?? "deepseek-v4-flash";
            var endpoint =
                _configuration.GetValue<string>("Translation:DeepSeek:Endpoint")
                ?? "https://api.deepseek.com/chat/completions";

            if (string.IsNullOrEmpty(apiKey))
            {
                throw new TranslationProviderConfigurationException(
                    "DeepSeek ApiKey 未配置，请通过环境变量 Translation__DeepSeek__ApiKey 注入。"
                );
            }

            try
            {
                var batchSize = Math.Max(
                    1,
                    _configuration.GetValue<int>("Translation:DeepSeek:BatchSize", 100)
                );
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

                SetBearerToken(_httpClient, apiKey);

                var enableParallel = _configuration.GetValue<bool>(
                    "Translation:DeepSeek:EnableParallelProcessing",
                    false
                );
                var maxConcurrent = Math.Max(
                    1,
                    _configuration.GetValue<int>("Translation:DeepSeek:MaxConcurrentBatches", 1)
                );
                var batchDelayMs = Math.Max(
                    0,
                    _configuration.GetValue<int>("Translation:DeepSeek:BatchDelayMs", 100)
                );

                if (enableParallel && batches.Count > 1)
                {
                    _logger.LogInformation(
                        "使用DeepSeek并行处理，最大并发数: {MaxConcurrent}",
                        maxConcurrent
                    );
                    results = await ProcessBatchesInParallelAsync(
                        _httpClient,
                        batches,
                        model,
                        endpoint,
                        maxConcurrent,
                        ProcessDeepSeekBatchAsync
                    );
                }
                else
                {
                    _logger.LogInformation("使用DeepSeek串行处理");
                    results = await ProcessBatchesSequentiallyAsync(
                        _httpClient,
                        batches,
                        model,
                        endpoint,
                        batchDelayMs,
                        ProcessDeepSeekBatchAsync
                    );
                }

                _logger.LogInformation(
                    "DeepSeek批量翻译完成: 有效翻译 {TranslatedCount} 个文本，返回 {ResultCount} 个结果",
                    results.Count(kvp => IsValidEnglishTranslation(kvp.Key, kvp.Value)),
                    results.Count
                );
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "调用DeepSeek批量翻译API时发生异常，文本数量: {Count}",
                    texts.Count
                );
                throw new InvalidOperationException("DeepSeek 批量翻译调用失败。", ex);
            }
        }

        /// <summary>
        /// 处理单个批次的DeepSeek翻译
        /// </summary>
        private async Task<Dictionary<string, string>> ProcessDeepSeekBatchAsync(
            HttpClient client,
            List<string> texts,
            string model,
            string endpoint
        )
        {
            var results = new Dictionary<string, string>();

            try
            {
                // 批量提示词强制编号输出，避免模型省略或重排商品名称。
                var combinedText = string.Join(
                    "\n---\n",
                    texts.Select((text, index) => $"{index + 1}. {text}")
                );
                var prompt =
                    $"请把下面用---分隔的{texts.Count}个中文商品名称翻译成英文商品名称。保留数字、规格、单位、颜色和型号；不要返回中文，不要返回原文，不要添加解释；必须逐行按“编号. 英文结果”的格式返回：\n{combinedText}";

                var requestBody = new
                {
                    model = model,
                    messages = new[] { new { role = "user", content = prompt } },
                    temperature = 0.1,
                    max_tokens = Math.Max(2000, texts.Count * 50),
                    stream = false,
                };

                var json = JsonSerializer.Serialize(requestBody);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

                _logger.LogDebug("调用DeepSeek批量翻译API，批次文本数量: {Count}", texts.Count);

                var response = await client.PostAsync(endpoint, httpContent);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError(
                        "DeepSeek批量翻译API调用失败，状态码: {StatusCode}, 内容: {Content}",
                        response.StatusCode,
                        errorContent
                    );
                    throw new InvalidOperationException(
                        $"DeepSeek 批量翻译 API 调用失败，状态码: {response.StatusCode}"
                    );
                }

                var result = await response.Content.ReadAsStringAsync();
                _logger.LogDebug("DeepSeek批量翻译API返回结果: {Result}", result);

                var translationResult = ExtractChatCompletionContent(result);

                if (!string.IsNullOrEmpty(translationResult))
                {
                    var lines = translationResult.Split(
                        '\n',
                        StringSplitOptions.RemoveEmptyEntries
                    );

                    foreach (var line in lines)
                    {
                        var trimmedLine = line.Trim();
                        var match = System.Text.RegularExpressions.Regex.Match(
                            trimmedLine,
                            @"^(\d+)\.\s*(.+)$"
                        );
                        if (match.Success)
                        {
                            var numberStr = match.Groups[1].Value;
                            var translation = match.Groups[2].Value.Trim().Trim('"').Trim();

                            if (
                                int.TryParse(numberStr, out var number)
                                && number > 0
                                && number <= texts.Count
                            )
                            {
                                var originalText = texts[number - 1];

                                if (IsValidEnglishTranslation(originalText, translation))
                                {
                                    results[originalText] = translation;
                                    _logger.LogDebug(
                                        "DeepSeek翻译匹配成功: [{Number}] {Chinese} → {English}",
                                        number,
                                        originalText,
                                        translation
                                    );
                                }
                                else
                                {
                                    results[originalText] = originalText;
                                    _logger.LogWarning(
                                        "DeepSeek返回无效英文译文，保留原文: [{Number}] {Chinese} -> {English}",
                                        number,
                                        originalText,
                                        translation
                                    );
                                }
                            }
                            else
                            {
                                _logger.LogWarning(
                                    "DeepSeek编号超出范围或无效: {Number}, 总数: {Count}",
                                    number,
                                    texts.Count
                                );
                            }
                        }
                        else
                        {
                            _logger.LogDebug("无法解析DeepSeek编号格式的行: {Line}", trimmedLine);
                        }
                    }

                    foreach (var text in texts)
                    {
                        if (!results.ContainsKey(text))
                        {
                            results[text] = text;
                            _logger.LogWarning("文本未被翻译（DeepSeek跳过）: {Chinese}", text);
                        }
                    }

                    _logger.LogInformation(
                        "DeepSeek批次翻译完成: 有效翻译 {TranslatedCount} 个文本，返回 {ResultCount} 个结果",
                        results.Count(kvp => IsValidEnglishTranslation(kvp.Key, kvp.Value)),
                        results.Count
                    );
                    return results;
                }

                _logger.LogWarning("DeepSeek批量翻译API返回空结果");
                throw new InvalidOperationException("DeepSeek 批量翻译 API 返回空结果。");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理DeepSeek批次翻译时发生异常，文本数量: {Count}", texts.Count);
                throw new InvalidOperationException("处理 DeepSeek 批量翻译结果失败。", ex);
            }
        }

        /// <summary>
        /// 调用Kimi翻译API
        /// </summary>
        private async Task<string> CallKimiApiAsync(string text)
        {
            return await CallKimiApiAsync(text, "en");
        }

        /// <summary>
        /// 调用Kimi翻译API并指定目标语言
        /// </summary>
        private async Task<string> CallKimiApiAsync(string text, string targetLanguage)
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
                return await CallMockApiAsync(text, targetLanguage);
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
                            content =
                                targetLanguage == "zh"
                                    ? $"把这段邮件内容翻译成中文，只返回翻译结果，不要其他说明：{text}"
                                    : $"把这段邮件内容翻译成英文，只返回翻译结果，不要其他说明：{text}",
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
                    return await CallMockApiAsync(text, targetLanguage);
                }

                var result = await response.Content.ReadAsStringAsync();
                _logger.LogDebug("Kimi API返回结果: {Result}", result);

                var translation = ExtractChatCompletionContent(result);

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
                return await CallMockApiAsync(text, targetLanguage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "调用Kimi API时发生异常: {Text}", text);
                return await CallMockApiAsync(text, targetLanguage);
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
                        maxConcurrent,
                        ProcessKimiBatchAsync
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
                        batchDelayMs,
                        ProcessKimiBatchAsync
                    );
                }

                _logger.LogInformation(
                    "Kimi批量翻译完成: 有效翻译 {TranslatedCount} 个文本，返回 {ResultCount} 个结果",
                    results.Count(kvp => IsValidEnglishTranslation(kvp.Key, kvp.Value)),
                    results.Count
                );
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
                    $"请把下面用---分隔的{texts.Count}个中文商品名称翻译成英文商品名称。保留数字、规格、单位、颜色和型号；不要返回中文，不要返回原文，不要添加解释；必须逐行按“编号. 英文结果”的格式返回：\n{combinedText}";

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

                var translationResult = ExtractChatCompletionContent(result);

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

                                if (IsValidEnglishTranslation(originalText, translation))
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
                                        "Kimi返回无效英文译文，保留原文: [{Number}] {Chinese} -> {English}",
                                        number,
                                        originalText,
                                        translation
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
            int batchDelayMs,
            Func<HttpClient, List<string>, string, string, Task<Dictionary<string, string>>> processBatchAsync
        )
        {
            var results = new Dictionary<string, string>();

            foreach (var batch in batches)
            {
                var batchResults = await processBatchAsync(client, batch, model, endpoint);
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
            int maxConcurrent,
            Func<HttpClient, List<string>, string, string, Task<Dictionary<string, string>>> processBatchAsync
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
                    return await processBatchAsync(client, batch, model, endpoint);
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
