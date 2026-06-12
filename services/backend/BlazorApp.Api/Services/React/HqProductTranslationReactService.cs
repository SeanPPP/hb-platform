using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.Models.HqEntities;

namespace BlazorApp.Api.Services.React
{
    public class HqProductTranslationReactService : IHqProductTranslationReactService
    {
        private readonly HqSqlSugarContext _hq;
        private readonly ITranslationService _translationService;
        private readonly ILogger<HqProductTranslationReactService> _logger;

        public HqProductTranslationReactService(
            HqSqlSugarContext hq,
            ITranslationService translationService,
            ILogger<HqProductTranslationReactService> logger
        )
        {
            _hq = hq;
            _translationService = translationService;
            _logger = logger;
        }

        private bool TryNormalizeValidEnglishTranslation(
            string chinese,
            string? english,
            out string normalizedEnglish
        )
        {
            normalizedEnglish = string.Empty;
            if (string.IsNullOrWhiteSpace(english))
            {
                return false;
            }

            normalizedEnglish = english.Trim();
            return !string.Equals(normalizedEnglish, chinese.Trim(), StringComparison.Ordinal)
                && !_translationService.ContainsChinese(normalizedEnglish);
        }

        private string? GetTranslationSource(string? chineseName, string? englishName)
        {
            if (
                !string.IsNullOrWhiteSpace(englishName)
                && _translationService.ContainsChinese(englishName)
            )
            {
                return englishName.Trim();
            }

            if (
                !string.IsNullOrWhiteSpace(chineseName)
                && _translationService.ContainsChinese(chineseName)
            )
            {
                return chineseName.Trim();
            }

            return null;
        }

        public async Task<TranslationResultDto> TranslateNamesByContainersAsync(
            List<string> containerGuids,
            bool overwriteExisting = false
        )
        {
            var result = new TranslationResultDto();
            if (containerGuids == null || !containerGuids.Any())
                return result;

            // 找到容器涉及的商品编码
            var productCodes = await _hq
                .Db.Queryable<CPT_RED_货柜单详情表Store>()
                .Where(d => d.主表GUID != null && containerGuids.Contains(d.主表GUID))
                .Where(d => d.商品编码 != null)
                .Select(d => d.商品编码)
                .Distinct()
                .ToListAsync();

            // 查询字典表中的商品（含中文/英文名）
            var products = await _hq
                .Db.Queryable<CPT_DIC_商品信息字典表>()
                .Where(p => p.商品编码 != null && productCodes.Contains(p.商品编码))
                .Select(p => new
                {
                    p.商品编码,
                    p.中文名称,
                    p.英文名称,
                })
                .ToListAsync();

            // 英文名称栏若仍含中文，优先翻译英文名称本身，避免继续保留污染值。
            var textsToTranslate = products
                .Select(x => GetTranslationSource(x.中文名称, x.英文名称))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .Distinct()
                .ToList();

            if (!textsToTranslate.Any())
                return result;

            var translations = await _translationService.BatchTranslateToEnglishAsync(
                textsToTranslate
            );

            foreach (var row in products)
            {
                try
                {
                    var sourceText = GetTranslationSource(row.中文名称, row.英文名称);
                    if (string.IsNullOrWhiteSpace(sourceText))
                    {
                        result.TotalSkipped++;
                        continue;
                    }

                    if (
                        !translations.TryGetValue(sourceText, out var english)
                        || !TryNormalizeValidEnglishTranslation(sourceText, english, out var normalizedEnglish)
                    )
                    {
                        _logger.LogWarning(
                            "跳过无效 HQ 英文名称译文: ProductCode={ProductCode}, Chinese={Chinese}, English={English}",
                            row.商品编码,
                            sourceText,
                            english
                        );
                        result.TotalSkipped++;
                        continue;
                    }

                    // 覆盖策略：默认不覆盖有效英文
                    if (
                        !overwriteExisting
                        && !string.IsNullOrWhiteSpace(row.英文名称)
                        && !_translationService.ContainsChinese(row.英文名称)
                    )
                    {
                        result.TotalSkipped++;
                        continue;
                    }

                    await _hq
                        .Db.Updateable<CPT_DIC_商品信息字典表>()
                        .SetColumns(p => new CPT_DIC_商品信息字典表 { 英文名称 = normalizedEnglish })
                        .Where(p => p.商品编码 == row.商品编码)
                        .ExecuteCommandAsync();

                    result.TotalTranslated++;

                    if (result.Samples.Count < 10)
                    {
                        result.Samples[sourceText] = normalizedEnglish;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "按商品编码写入英文名失败: {ProductCode}", row.商品编码);
                    result.TotalFailed++;
                }
            }

            // 候选数量统计
            result.TotalCandidates = textsToTranslate.Count;
            return result;
        }

        public async Task<TranslationResultDto> TranslateNamesAllAsync(
            bool overwriteExisting = false
        )
        {
            var result = new TranslationResultDto();

            // 查询所有需要翻译的商品（保留商品编码）
            var products = await _hq
                .Db.Queryable<CPT_DIC_商品信息字典表>()
                .Where(p => p.中文名称 != null && p.中文名称 != "")
                .Select(p => new
                {
                    p.商品编码,
                    p.中文名称,
                    p.英文名称,
                })
                .ToListAsync();

            var textsToTranslate = products
                .Select(x => GetTranslationSource(x.中文名称, x.英文名称))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .Distinct()
                .ToList();

            if (!textsToTranslate.Any())
                return result;

            var translations = await _translationService.BatchTranslateToEnglishAsync(
                textsToTranslate
            );

            foreach (var row in products)
            {
                try
                {
                    var sourceText = GetTranslationSource(row.中文名称, row.英文名称);
                    if (string.IsNullOrWhiteSpace(sourceText))
                    {
                        result.TotalSkipped++;
                        continue;
                    }

                    if (
                        !translations.TryGetValue(sourceText, out var english)
                        || !TryNormalizeValidEnglishTranslation(sourceText, english, out var normalizedEnglish)
                    )
                    {
                        _logger.LogWarning(
                            "跳过无效 HQ 英文名称译文(全量): ProductCode={ProductCode}, Chinese={Chinese}, English={English}",
                            row.商品编码,
                            sourceText,
                            english
                        );
                        result.TotalSkipped++;
                        continue;
                    }

                    if (
                        !overwriteExisting
                        && !string.IsNullOrWhiteSpace(row.英文名称)
                        && !_translationService.ContainsChinese(row.英文名称)
                    )
                    {
                        result.TotalSkipped++;
                        continue;
                    }

                    await _hq
                        .Db.Updateable<CPT_DIC_商品信息字典表>()
                        .SetColumns(p => new CPT_DIC_商品信息字典表 { 英文名称 = normalizedEnglish })
                        .Where(p => p.商品编码 == row.商品编码)
                        .ExecuteCommandAsync();

                    result.TotalTranslated++;

                    if (result.Samples.Count < 10)
                    {
                        result.Samples[sourceText] = normalizedEnglish;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "按商品编码写入英文名失败(全量): {ProductCode}",
                        row.商品编码
                    );
                    result.TotalFailed++;
                }
            }

            result.TotalCandidates = textsToTranslate.Count;
            return result;
        }

        private async Task<TranslationResultDto> TranslateByProductCodesAsync(
            List<string> productCodes,
            bool overwriteExisting
        )
        {
            var result = new TranslationResultDto();

            var products = await _hq
                .Db.Queryable<CPT_DIC_商品信息字典表>()
                .Where(p => p.商品编码 != null && productCodes.Contains(p.商品编码))
                .Select(p => new
                {
                    p.商品编码,
                    p.中文名称,
                    p.英文名称,
                })
                .ToListAsync();

            var textsToTranslate = products
                .Select(x => GetTranslationSource(x.中文名称, x.英文名称))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .Distinct()
                .ToList();

            if (!textsToTranslate.Any())
                return result;

            var translations = await _translationService.BatchTranslateToEnglishAsync(
                textsToTranslate
            );

            foreach (var row in products)
            {
                try
                {
                    var sourceText = GetTranslationSource(row.中文名称, row.英文名称);
                    if (string.IsNullOrWhiteSpace(sourceText))
                    {
                        result.TotalSkipped++;
                        continue;
                    }

                    if (
                        !translations.TryGetValue(sourceText, out var english)
                        || !TryNormalizeValidEnglishTranslation(sourceText, english, out var normalizedEnglish)
                    )
                    {
                        _logger.LogWarning(
                            "跳过无效 HQ 英文名称译文(按容器商品编码): ProductCode={ProductCode}, Chinese={Chinese}, English={English}",
                            row.商品编码,
                            sourceText,
                            english
                        );
                        result.TotalSkipped++;
                        continue;
                    }

                    if (
                        !overwriteExisting
                        && !string.IsNullOrWhiteSpace(row.英文名称)
                        && !_translationService.ContainsChinese(row.英文名称)
                    )
                    {
                        result.TotalSkipped++;
                        continue;
                    }

                    await _hq
                        .Db.Updateable<CPT_DIC_商品信息字典表>()
                        .SetColumns(p => new CPT_DIC_商品信息字典表 { 英文名称 = normalizedEnglish })
                        .Where(p => p.商品编码 == row.商品编码)
                        .ExecuteCommandAsync();

                    result.TotalTranslated++;

                    if (result.Samples.Count < 10)
                    {
                        result.Samples[sourceText] = normalizedEnglish;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "按商品编码写入英文名失败(按容器商品编码): {ProductCode}",
                        row.商品编码
                    );
                    result.TotalFailed++;
                }
            }

            result.TotalCandidates = textsToTranslate.Count;
            return result;
        }

        private async Task<TranslationResultDto> TranslateByChineseNamesAsync(
            List<string> chineseNames,
            bool overwriteExisting
        )
        {
            return await TranslateAndWriteAsync(chineseNames, overwriteExisting);
        }

        private async Task<TranslationResultDto> TranslateAndWriteAsync(
            List<string> chineseNames,
            bool overwriteExisting
        )
        {
            var result = new TranslationResultDto();
            if (chineseNames == null || !chineseNames.Any())
                return result;

            // 过滤：只保留包含中文的文本
            var filtered = chineseNames
                .Where(t => _translationService.ContainsChinese(t))
                .Distinct()
                .ToList();
            result.TotalCandidates = filtered.Count;

            if (!filtered.Any())
                return result;

            // 批量翻译
            var translations = await _translationService.BatchTranslateToEnglishAsync(filtered);

            // 写入：仅回写 英文名称
            foreach (var kvp in translations)
            {
                try
                {
                    var chinese = kvp.Key;
                    var english = kvp.Value;

                    if (!TryNormalizeValidEnglishTranslation(chinese, english, out var normalizedEnglish))
                    {
                        _logger.LogWarning(
                            "跳过无效 HQ 英文名称译文(按中文名): Chinese={Chinese}, English={English}",
                            chinese,
                            english
                        );
                        result.TotalSkipped++;
                        continue;
                    }

                    // 查找所有中文名称匹配的商品行
                    var list = await _hq
                        .Db.Queryable<CPT_DIC_商品信息字典表>()
                        .Where(p => p.中文名称 == chinese)
                        .Select(p => new { p.ID, p.英文名称 })
                        .ToListAsync();

                    if (!list.Any())
                    {
                        result.TotalSkipped++;
                        continue;
                    }

                    foreach (var row in list)
                    {
                        // 覆盖策略：默认不覆盖有效英文
                        if (
                            !overwriteExisting
                            && !string.IsNullOrWhiteSpace(row.英文名称)
                            && !_translationService.ContainsChinese(row.英文名称)
                        )
                        {
                            result.TotalSkipped++;
                            continue;
                        }

                        await _hq
                            .Db.Updateable<CPT_DIC_商品信息字典表>()
                            .SetColumns(p => new CPT_DIC_商品信息字典表 { 英文名称 = normalizedEnglish })
                            .Where(p => p.ID == row.ID)
                            .ExecuteCommandAsync();
                        result.TotalTranslated++;
                    }

                    if (result.Samples.Count < 10)
                    {
                        result.Samples[chinese] = normalizedEnglish;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "翻译写入失败: {Chinese}", kvp.Key);
                    result.TotalFailed++;
                }
            }

            return result;
        }
    }
}
