using System.Threading.Tasks;

namespace BlazorApp.Api.Interfaces
{
    /// <summary>
    /// 翻译服务接口
    /// </summary>
    public interface ITranslationService
    {
        /// <summary>
        /// 检测文本是否为中文
        /// </summary>
        /// <param name="text">要检测的文本</param>
        /// <returns>如果包含中文字符返回true</returns>
        bool ContainsChinese(string text);

        /// <summary>
        /// 异步检测文本是否包含中文字符
        /// </summary>
        /// <param name="text">要检测的文本</param>
        /// <returns>如果包含中文返回true，否则返回false</returns>
        Task<bool> DetectChineseAsync(string text);

        /// <summary>
        /// 将中文文本翻译为英文
        /// </summary>
        /// <param name="chineseText">中文文本</param>
        /// <returns>翻译后的英文文本</returns>
        Task<string> TranslateToEnglishAsync(string chineseText);

        /// <summary>
        /// 批量翻译中文文本为英文
        /// </summary>
        /// <param name="chineseTexts">中文文本列表</param>
        /// <returns>翻译后的英文文本列表</returns>
        Task<Dictionary<string, string>> BatchTranslateToEnglishAsync(List<string> chineseTexts);

        /// <summary>
        /// 获取缓存的翻译结果
        /// </summary>
        /// <param name="chineseText">中文文本</param>
        /// <returns>缓存的英文翻译，如果没有缓存返回null</returns>
        Task<string?> GetCachedTranslationAsync(string chineseText);

        /// <summary>
        /// 缓存翻译结果
        /// </summary>
        /// <param name="chineseText">中文文本</param>
        /// <param name="englishText">英文翻译</param>
        Task CacheTranslationAsync(string chineseText, string englishText);
    }
}