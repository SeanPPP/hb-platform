using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Controllers
{
    /// <summary>
    /// 翻译功能测试控制器
    /// </summary>
    [ApiController]
    [Route("api/test/[controller]")]
    [Authorize]
    public class TestTranslationController : ControllerBase
    {
        private readonly ILogger<TestTranslationController> _logger;
        private readonly ITranslationService _translationService;

        public TestTranslationController(
            ILogger<TestTranslationController> logger,
            ITranslationService translationService)
        {
            _logger = logger;
            _translationService = translationService;
        }

        /// <summary>
        /// 测试单个文本翻译
        /// </summary>
        /// <param name="text">要翻译的中文文本</param>
        /// <returns>翻译结果</returns>
        [HttpPost("translate")]
        public async Task<IActionResult> TestTranslate([FromBody] string text)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    return BadRequest(new { success = false, message = "文本不能为空" });
                }

                _logger.LogInformation("测试翻译文本: {Text}", text);

                // 检测是否包含中文
                var containsChinese = await _translationService.DetectChineseAsync(text);
                
                // 执行翻译
                var translation = await _translationService.TranslateToEnglishAsync(text);

                var result = new
                {
                    success = true,
                    data = new
                    {
                        originalText = text,
                        translatedText = translation,
                        containsChinese = containsChinese,
                        timestamp = DateTime.Now
                    }
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "翻译测试失败");
                return StatusCode(500, new { success = false, message = "翻译服务异常" });
            }
        }

        /// <summary>
        /// 测试批量翻译
        /// </summary>
        /// <param name="texts">要翻译的中文文本列表</param>
        /// <returns>批量翻译结果</returns>
        [HttpPost("batch-translate")]
        public async Task<IActionResult> TestBatchTranslate([FromBody] List<string> texts)
        {
            try
            {
                if (texts?.Any() != true)
                {
                    return BadRequest(new { success = false, message = "文本列表不能为空" });
                }

                _logger.LogInformation("测试批量翻译，文本数量: {Count}", texts.Count);

                // 执行批量翻译
                var translations = await _translationService.BatchTranslateToEnglishAsync(texts);

                var result = new
                {
                    success = true,
                    data = new
                    {
                        originalTexts = texts,
                        translations = translations,
                        totalCount = texts.Count,
                        translatedCount = translations.Count,
                        timestamp = DateTime.Now
                    }
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量翻译测试失败");
                return StatusCode(500, new { success = false, message = "批量翻译服务异常" });
            }
        }

        /// <summary>
        /// 测试缓存功能
        /// </summary>
        /// <param name="text">要查询缓存的文本</param>
        /// <returns>缓存查询结果</returns>
        [HttpGet("cache/{text}")]
        public async Task<IActionResult> TestCache(string text)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    return BadRequest(new { success = false, message = "文本不能为空" });
                }

                // 查询缓存
                var cached = await _translationService.GetCachedTranslationAsync(text);

                var result = new
                {
                    success = true,
                    data = new
                    {
                        originalText = text,
                        cachedTranslation = cached,
                        isCached = !string.IsNullOrEmpty(cached),
                        timestamp = DateTime.Now
                    }
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "缓存查询测试失败");
                return StatusCode(500, new { success = false, message = "缓存查询服务异常" });
            }
        }

        /// <summary>
        /// 获取翻译服务状态
        /// </summary>
        /// <returns>服务状态信息</returns>
        [HttpGet("status")]
        public IActionResult GetTranslationStatus()
        {
            try
            {
                // 这里可以添加更多状态信息
                var result = new
                {
                    success = true,
                    data = new
                    {
                        serviceName = "TranslationService",
                        status = "Running",
                        supportedProviders = new[] { "kimi", "baidu", "google", "azure", "mock" },
                        timestamp = DateTime.Now
                    }
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取翻译服务状态失败");
                return StatusCode(500, new { success = false, message = "获取服务状态异常" });
            }
        }
    }
}