using BlazorApp.Api.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BlazorApp.Api.Controllers
{
    /// <summary>
    /// 翻译服务控制器
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class TranslationController : ControllerBase
    {
        private readonly ITranslationService _translationService;
        private readonly ILogger<TranslationController> _logger;

        public TranslationController(
            ITranslationService translationService,
            ILogger<TranslationController> logger
        )
        {
            _translationService = translationService;
            _logger = logger;
        }

        /// <summary>
        /// 检测文本是否包含中文
        /// </summary>
        /// <param name="text">要检测的文本</param>
        /// <returns>检测结果</returns>
        [HttpPost("detect-chinese")]
        public IActionResult DetectChinese([FromBody] string text)
        {
            try
            {
                var containsChinese = _translationService.ContainsChinese(text);
                return Ok(new { success = true, data = new { text, containsChinese } });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检测中文文本失败: {Text}", text);
                return StatusCode(500, new { success = false, message = "检测失败" });
            }
        }

        /// <summary>
        /// 翻译中文文本为英文
        /// </summary>
        /// <param name="request">翻译请求</param>
        /// <returns>翻译结果</returns>
        [HttpPost("translate")]
        public async Task<IActionResult> TranslateToEnglish([FromBody] TranslateRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Text))
                {
                    return BadRequest(new { success = false, message = "文本不能为空" });
                }

                var translation = await _translationService.TranslateToEnglishAsync(request.Text);

                return Ok(
                    new
                    {
                        success = true,
                        data = new
                        {
                            originalText = request.Text,
                            translatedText = translation,
                            containsChinese = _translationService.ContainsChinese(request.Text),
                        },
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "翻译文本失败: {Text}", request.Text);
                return StatusCode(500, new { success = false, message = "翻译失败，请稍后重试" });
            }
        }

        /// <summary>
        /// 批量翻译中文文本
        /// </summary>
        /// <param name="request">批量翻译请求</param>
        /// <returns>批量翻译结果</returns>
        [HttpPost("batch-translate")]
        public async Task<IActionResult> BatchTranslate([FromBody] BatchTranslateRequest request)
        {
            try
            {
                if (request.Texts == null || !request.Texts.Any())
                {
                    return BadRequest(new { success = false, message = "文本列表不能为空" });
                }

                if (request.Texts.Count > 100)
                {
                    return BadRequest(
                        new { success = false, message = "批量翻译最多支持100个文本" }
                    );
                }

                var translations = await _translationService.BatchTranslateToEnglishAsync(
                    request.Texts
                );

                return Ok(
                    new
                    {
                        success = true,
                        data = new { count = translations.Count, translations = translations },
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量翻译失败");
                return StatusCode(
                    500,
                    new { success = false, message = "批量翻译失败，请稍后重试" }
                );
            }
        }

        /// <summary>
        /// 获取缓存的翻译
        /// </summary>
        /// <param name="text">中文文本</param>
        /// <returns>缓存的翻译结果</returns>
        [HttpGet("cached/{text}")]
        public async Task<IActionResult> GetCachedTranslation(string text)
        {
            try
            {
                var cached = await _translationService.GetCachedTranslationAsync(text);

                return Ok(
                    new
                    {
                        success = true,
                        data = new
                        {
                            text,
                            cachedTranslation = cached,
                            hasCached = !string.IsNullOrEmpty(cached),
                        },
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取缓存翻译失败: {Text}", text);
                return StatusCode(500, new { success = false, message = "获取缓存失败" });
            }
        }
    }

    /// <summary>
    /// 翻译请求DTO
    /// </summary>
    public class TranslateRequest
    {
        /// <summary>
        /// 要翻译的文本
        /// </summary>
        public string Text { get; set; } = string.Empty;
    }

    /// <summary>
    /// 批量翻译请求DTO
    /// </summary>
    public class BatchTranslateRequest
    {
        /// <summary>
        /// 要翻译的文本列表
        /// </summary>
        public List<string> Texts { get; set; } = new List<string>();
    }
}
