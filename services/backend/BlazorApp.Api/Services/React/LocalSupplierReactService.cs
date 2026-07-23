using System.Diagnostics;
using System.Linq;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    public class LocalSupplierReactService : ILocalSuppliersReactService
    {
        private readonly SqlSugarContext _context;
        private readonly HqSqlSugarContext _hQSales;
        private readonly ILogger<LocalSupplierReactService> _logger;

        public LocalSupplierReactService(
            SqlSugarContext context,
            HqSqlSugarContext HQSales,
            ILogger<LocalSupplierReactService> logger
        )
        {
            _context = context;
            _hQSales = HQSales;
            _logger = logger;
        }

        public async Task<PagedResult<LocalSupplierDto>> GetSuppliersAsync(
            int pageIndex,
            int pageSize,
            string? keyword,
            int? status,
            string? sortBy,
            string? sortOrder
        )
        {
            var db = _context.Db;
            var query = db.Queryable<HBLocalSupplier>().Where(x => !x.IsDeleted);
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var k = keyword.Trim();
                query = query.Where(x => x.LocalSupplierCode.Contains(k) || x.Name.Contains(k));
            }
            if (status.HasValue)
            {
                query = query.Where(x => x.Status == status.Value);
            }
            var total = await query.CountAsync();
            var by = (sortBy ?? "name").ToLowerInvariant();
            var ord = (sortOrder ?? "ascend").ToLowerInvariant();
            var dir = ord == "descend" ? "desc" : "asc";
            string orderExpr = by switch
            {
                "localsuppliercode" => $"LocalSupplierCode {dir}",
                "name" => $"Name {dir}",
                "status" => $"Status {dir}",
                "contactperson" => $"ContactPerson {dir}",
                "phone" => $"Phone {dir}",
                "email" => $"Email {dir}",
                "remark" => $"Remark {dir}",
                "imagebaseurl" => $"ImageBaseUrl {dir}",
                "createdat" => $"CreatedAt {dir}",
                "updatedat" => $"UpdatedAt {dir}",
                _ => $"Name asc",
            };
            var items = await query
                .OrderBy(orderExpr)
                .Skip((pageIndex - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new LocalSupplierDto
                {
                    Guid = x.Guid,
                    LocalSupplierCode = x.LocalSupplierCode,
                    Name = x.Name,
                    Status = x.Status,
                    ContactPerson = x.ContactPerson,
                    Phone = x.Phone,
                    Email = x.Email,
                    Remark = x.Remark,
                    ImageBaseUrl = x.ImageBaseUrl,
                    CreatedAt = x.CreatedAt,
                    UpdatedAt = x.UpdatedAt,
                })
                .ToListAsync();
            return new PagedResult<LocalSupplierDto>
            {
                Items = items,
                Total = total,
                Page = pageIndex,
                PageSize = pageSize,
            };
        }

        public async Task<List<LocalSupplierDto>> GetActiveSuppliersAsync()
        {
            var db = _context.Db;
            var items = await db.Queryable<HBLocalSupplier>()
                .Where(x => !x.IsDeleted && x.Status == 1)
                .OrderBy(x => x.Name)
                .Select(x => new LocalSupplierDto
                {
                    Guid = x.Guid,
                    LocalSupplierCode = x.LocalSupplierCode,
                    Name = x.Name,
                    Status = x.Status,
                    ContactPerson = x.ContactPerson,
                    Phone = x.Phone,
                    Email = x.Email,
                    Remark = x.Remark,
                    ImageBaseUrl = x.ImageBaseUrl,
                    CreatedAt = x.CreatedAt,
                    UpdatedAt = x.UpdatedAt,
                })
                .ToListAsync();
            return items;
        }

        public async Task<ApiResponse<LocalSupplierSyncResultDto>> SyncFromDicAsync(
            DateTime? since,
            bool overwrite
        )
        {
            var db = _context.Db;
            var hb = _hQSales.Db;
            var result = new LocalSupplierSyncResultDto();
            var now = DateTime.UtcNow;
            var dicList = await hb.Queryable<DIC_供应商信息表>().ToListAsync();
            var valid = dicList
                .Where(d =>
                    !string.IsNullOrWhiteSpace(d.H供应商编码)
                    && !string.IsNullOrWhiteSpace(d.H供应商名称)
                )
                .ToList();
            result.SkippedCount += dicList.Count - valid.Count;

            var existing = await db.Queryable<HBLocalSupplier>()
                .Where(x => !x.IsDeleted)
                .ToListAsync();
            var map = existing.ToDictionary(x => x.LocalSupplierCode, x => x);

            var toInsert = new List<HBLocalSupplier>();
            var toUpdate = new List<HBLocalSupplier>();

            foreach (var d in valid)
            {
                var code = d.H供应商编码!;
                var name = d.H供应商名称!;
                if (!map.TryGetValue(code, out var e))
                {
                    toInsert.Add(
                        new HBLocalSupplier
                        {
                            Guid = Guid.NewGuid().ToString(),
                            LocalSupplierCode = code,
                            Name = name,
                            Status = 1,
                            ContactPerson = d.H联系人,
                            Email = d.HEMAIL地址,
                            ImageBaseUrl = null,
                            CreatedAt = d.FGC_CreateDate,
                            UpdatedAt = d.FGC_LastModifyDate,
                            CreatedBy = "System",
                            UpdatedBy = "System",
                            IsDeleted = false,
                        }
                    );
                }
                else
                {
                    e.Name = name;
                    e.ContactPerson = d.H联系人;
                    e.Email = d.HEMAIL地址;
                    if (overwrite)
                        e.Status = 1;
                    e.UpdatedAt = d.FGC_LastModifyDate;
                    e.UpdatedBy = "System";
                    toUpdate.Add(e);
                }
            }

            const int batchSize = 5000;
            var sw = Stopwatch.StartNew();
            _logger.LogInformation(
                "LocalSupplier 同步开始: valid={Valid}, skipped={Skipped}",
                valid.Count,
                result.SkippedCount
            );
            await db.Ado.BeginTranAsync();
            try
            {
                if (toInsert.Count > 0)
                {
                    if (
                        db.CurrentConnectionConfig.DbType == DbType.SqlServer
                        && toInsert.Count > batchSize
                    )
                    {
                        for (int i = 0; i < toInsert.Count; i += batchSize)
                        {
                            var slice = toInsert.Skip(i).Take(batchSize).ToList();
                            db.Fastest<HBLocalSupplier>().BulkCopy(slice);
                        }
                    }
                    else
                    {
                        for (int i = 0; i < toInsert.Count; i += batchSize)
                        {
                            var slice = toInsert.Skip(i).Take(batchSize).ToList();
                            await db.Insertable(slice).ExecuteCommandAsync();
                        }
                    }
                }

                if (toUpdate.Count > 0)
                {
                    for (int i = 0; i < toUpdate.Count; i += batchSize)
                    {
                        var slice = toUpdate.Skip(i).Take(batchSize).ToList();
                        await db.Updateable(slice)
                            .UpdateColumns(x => new
                            {
                                x.Name,
                                x.ContactPerson,
                                x.Email,
                                x.Status,
                                x.UpdatedAt,
                                x.UpdatedBy,
                            })
                            .ExecuteCommandAsync();
                    }
                }

                result.CreatedCount += toInsert.Count;
                result.UpdatedCount += toUpdate.Count;
                _logger.LogInformation(
                    "LocalSupplier 同步写入: insert={Insert}, update={Update}",
                    toInsert.Count,
                    toUpdate.Count
                );

                await db.Ado.CommitTranAsync();
                sw.Stop();
                _logger.LogInformation(
                    "LocalSupplier 同步完成: elapsed={Elapsed}ms",
                    sw.ElapsedMilliseconds
                );
                return ApiResponse<LocalSupplierSyncResultDto>.OK(result, "同步完成");
            }
            catch (Exception ex)
            {
                await db.Ado.RollbackTranAsync();
                _logger.LogError(ex, "LocalSupplier 同步失败");
                result.Errors.Add(ex.Message);
                return ApiResponse<LocalSupplierSyncResultDto>.Error(
                    "同步失败",
                    "SYNC_ERROR",
                    result
                );
            }
        }

        public async Task<ApiResponse<LocalSupplierSyncResultDto>> SyncToHqAsync(
            IReadOnlyCollection<string> supplierCodes
        )
        {
            var result = new LocalSupplierSyncResultDto();
            var normalizedCodes = supplierCodes
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (normalizedCodes.Count == 0)
            {
                return ApiResponse<LocalSupplierSyncResultDto>.Error(
                    "请选择要同步的澳洲供应商",
                    "SUPPLIER_REQUIRED",
                    result
                );
            }

            const int maxSupplierCount = 500;
            if (normalizedCodes.Count > maxSupplierCount)
            {
                return ApiResponse<LocalSupplierSyncResultDto>.Error(
                    $"单次最多同步 {maxSupplierCount} 个供应商",
                    "SUPPLIER_LIMIT_EXCEEDED",
                    result
                );
            }

            var localSuppliers = await _context.Db.Queryable<HBLocalSupplier>()
                .Where(x => !x.IsDeleted && normalizedCodes.Contains(x.LocalSupplierCode))
                .ToListAsync();
            result.SkippedCount += normalizedCodes.Count - localSuppliers.Count;

            var validSuppliers = localSuppliers
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x.LocalSupplierCode)
                    && !string.IsNullOrWhiteSpace(x.Name)
                )
                .ToList();
            result.SkippedCount += localSuppliers.Count - validSuppliers.Count;

            if (validSuppliers.Count == 0)
            {
                return ApiResponse<LocalSupplierSyncResultDto>.Error(
                    "没有可同步的澳洲供应商",
                    "SUPPLIER_NOT_FOUND",
                    result
                );
            }

            var hqDb = _hQSales.Db;
            var now = DateTime.UtcNow;
            await hqDb.Ado.BeginTranAsync();
            try
            {
                var validCodes = validSuppliers
                    .Select(x => x.LocalSupplierCode)
                    .ToList();
                var existingRows = await hqDb.Queryable<DIC_供应商信息表>()
                    .Where(x =>
                        x.H供应商编码 != null && validCodes.Contains(x.H供应商编码)
                    )
                    .ToListAsync();
                var existingGroups = existingRows
                    .Where(x => !string.IsNullOrWhiteSpace(x.H供应商编码))
                    .GroupBy(x => x.H供应商编码!, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

                var toInsert = new List<DIC_供应商信息表>();
                var toUpdate = new List<DIC_供应商信息表>();
                foreach (var supplier in validSuppliers)
                {
                    if (!existingGroups.TryGetValue(supplier.LocalSupplierCode, out var matches))
                    {
                        var hGuid =
                            !string.IsNullOrWhiteSpace(supplier.Guid)
                            && supplier.Guid.Length <= 50
                                ? supplier.Guid
                                : Guid.NewGuid().ToString();
                        toInsert.Add(new DIC_供应商信息表
                        {
                            HGUID = hGuid,
                            H供应商编码 = supplier.LocalSupplierCode,
                            H供应商名称 = supplier.Name,
                            H供应商全称 = supplier.Name,
                            H联系人 = supplier.ContactPerson,
                            HEMAIL地址 = supplier.Email,
                            FGC_CreateDate =
                                supplier.CreatedAt == default ? now : supplier.CreatedAt,
                            FGC_LastModifyDate = now,
                        });
                        continue;
                    }

                    // HQ 中代码不唯一时不猜测目标记录，避免一次选择覆盖多条历史数据。
                    if (matches.Count != 1)
                    {
                        result.SkippedCount++;
                        result.Errors.Add(
                            $"供应商 {supplier.LocalSupplierCode} 在 HQ 中存在 {matches.Count} 条记录，已跳过"
                        );
                        continue;
                    }

                    var existing = matches[0];
                    existing.H供应商名称 = supplier.Name;
                    existing.H联系人 = supplier.ContactPerson;
                    existing.HEMAIL地址 = supplier.Email;
                    existing.FGC_LastModifyDate = now;
                    toUpdate.Add(existing);
                }

                if (toInsert.Count > 0)
                {
                    await hqDb.Insertable(toInsert).ExecuteCommandAsync();
                }

                if (toUpdate.Count > 0)
                {
                    await hqDb.Updateable(toUpdate)
                        .UpdateColumns(x => new
                        {
                            x.H供应商名称,
                            x.H联系人,
                            x.HEMAIL地址,
                            x.FGC_LastModifyDate,
                        })
                        .ExecuteCommandAsync();
                }

                result.CreatedCount = toInsert.Count;
                result.UpdatedCount = toUpdate.Count;
                await hqDb.Ado.CommitTranAsync();
                _logger.LogInformation(
                    "澳洲供应商同步到 HQ 完成: requested={Requested}, insert={Insert}, update={Update}, skipped={Skipped}",
                    normalizedCodes.Count,
                    result.CreatedCount,
                    result.UpdatedCount,
                    result.SkippedCount
                );
                return ApiResponse<LocalSupplierSyncResultDto>.OK(result, "同步到 HQ 完成");
            }
            catch (Exception ex)
            {
                await hqDb.Ado.RollbackTranAsync();
                _logger.LogError(ex, "澳洲供应商同步到 HQ 失败");
                result.Errors.Add(ex.Message);
                return ApiResponse<LocalSupplierSyncResultDto>.Error(
                    "同步到 HQ 失败",
                    "HQ_SYNC_ERROR",
                    result
                );
            }
        }

        public async Task<ApiResponse<LocalSupplierDto>> CreateAsync(CreateLocalSupplierDto dto)
        {
            var db = _context.Db;
            await db.Ado.BeginTranAsync();
            try
            {
                var prefix = "SP";
                var maxCode = await db.Queryable<HBLocalSupplier>()
                    .Where(x => x.LocalSupplierCode.StartsWith(prefix))
                    .With(SqlWith.UpdLock)
                    .OrderBy(x => x.LocalSupplierCode, OrderByType.Desc)
                    .Select(x => x.LocalSupplierCode)
                    .FirstAsync();
                var next = 1;
                if (!string.IsNullOrEmpty(maxCode) && maxCode.StartsWith(prefix))
                {
                    var numStr = maxCode.Substring(prefix.Length);
                    if (int.TryParse(numStr, out var n))
                        next = n + 1;
                }
                var attempts = 0;
                while (true)
                {
                    var newCode = $"{prefix}{next:D3}";
                    var now = DateTime.UtcNow;
                    var entity = new HBLocalSupplier
                    {
                        Guid = Guid.NewGuid().ToString(),
                        LocalSupplierCode = newCode,
                        Name = dto.Name,
                        Status = dto.Status,
                        ContactPerson = dto.ContactPerson,
                        Phone = dto.Phone,
                        Email = dto.Email,
                        Remark = dto.Remark,
                        ImageBaseUrl = dto.ImageBaseUrl,
                        CreatedAt = now,
                        UpdatedAt = now,
                        CreatedBy = "System",
                        UpdatedBy = "System",
                        IsDeleted = false,
                    };
                    try
                    {
                        await db.Insertable(entity).ExecuteCommandAsync();
                        await db.Ado.CommitTranAsync();
                        var data = new LocalSupplierDto
                        {
                            Guid = entity.Guid,
                            LocalSupplierCode = entity.LocalSupplierCode,
                            Name = entity.Name,
                            Status = entity.Status,
                            ContactPerson = entity.ContactPerson,
                            Phone = entity.Phone,
                            Email = entity.Email,
                            Remark = entity.Remark,
                            ImageBaseUrl = entity.ImageBaseUrl,
                            CreatedAt = entity.CreatedAt,
                            UpdatedAt = entity.UpdatedAt,
                        };
                        return ApiResponse<LocalSupplierDto>.OK(data, "创建成功");
                    }
                    catch (Microsoft.Data.SqlClient.SqlException sqlEx)
                        when (sqlEx.Number == 2601 || sqlEx.Number == 2627)
                    {
                        attempts++;
                        next++;
                        if (attempts >= 10)
                        {
                            await db.Ado.RollbackTranAsync();
                            _logger.LogError(sqlEx, "LocalSupplier 创建失败");
                            return ApiResponse<LocalSupplierDto>.Error(
                                "创建失败",
                                "CREATE_DUPLICATE_ERROR"
                            );
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                await db.Ado.RollbackTranAsync();
                _logger.LogError(ex, "LocalSupplier 创建失败");
                return ApiResponse<LocalSupplierDto>.Error("创建失败", "CREATE_ERROR");
            }
        }

        public async Task<ApiResponse<LocalSupplierDto>> UpdateAsync(
            string code,
            UpdateLocalSupplierDto dto
        )
        {
            var db = _context.Db;
            var entity = await db.Queryable<HBLocalSupplier>()
                .FirstAsync(x => x.LocalSupplierCode == code && !x.IsDeleted);
            if (entity == null)
                return ApiResponse<LocalSupplierDto>.Error("供应商不存在", "NOT_FOUND");
            entity.Name = dto.Name;
            entity.Status = dto.Status;
            entity.ContactPerson = dto.ContactPerson;
            entity.Phone = dto.Phone;
            entity.Email = dto.Email;
            entity.Remark = dto.Remark;
            entity.ImageBaseUrl = dto.ImageBaseUrl;
            entity.UpdatedAt = DateTime.UtcNow;
            entity.UpdatedBy = "System";
            await db.Updateable(entity).ExecuteCommandAsync();
            var data = new LocalSupplierDto
            {
                Guid = entity.Guid,
                LocalSupplierCode = entity.LocalSupplierCode,
                Name = entity.Name,
                Status = entity.Status,
                ContactPerson = entity.ContactPerson,
                Phone = entity.Phone,
                Email = entity.Email,
                Remark = entity.Remark,
                ImageBaseUrl = entity.ImageBaseUrl,
                CreatedAt = entity.CreatedAt,
                UpdatedAt = entity.UpdatedAt,
            };
            return ApiResponse<LocalSupplierDto>.OK(data, "更新成功");
        }

        public async Task<ApiResponse<bool>> DeleteAsync(string code)
        {
            var db = _context.Db;
            var entity = await db.Queryable<HBLocalSupplier>()
                .FirstAsync(x => x.LocalSupplierCode == code && !x.IsDeleted);
            if (entity == null)
                return ApiResponse<bool>.Error("供应商不存在", "NOT_FOUND");
            entity.IsDeleted = true;
            entity.UpdatedAt = DateTime.UtcNow;
            entity.UpdatedBy = "System";
            await db.Updateable(entity)
                .UpdateColumns(x => new
                {
                    x.IsDeleted,
                    x.UpdatedAt,
                    x.UpdatedBy,
                })
                .ExecuteCommandAsync();
            return ApiResponse<bool>.OK(true, "删除成功");
        }

        public async Task<ApiResponse<bool>> ToggleStatusAsync(string code, int status)
        {
            var db = _context.Db;
            var entity = await db.Queryable<HBLocalSupplier>()
                .FirstAsync(x => x.LocalSupplierCode == code && !x.IsDeleted);
            if (entity == null)
                return ApiResponse<bool>.Error("供应商不存在", "NOT_FOUND");
            entity.Status = status;
            entity.UpdatedAt = DateTime.UtcNow;
            entity.UpdatedBy = "System";
            await db.Updateable(entity)
                .UpdateColumns(x => new
                {
                    x.Status,
                    x.UpdatedAt,
                    x.UpdatedBy,
                })
                .ExecuteCommandAsync();
            return ApiResponse<bool>.OK(true, "状态更新成功");
        }

        public async Task<ApiResponse<bool>> CheckCodeExistsAsync(string code)
        {
            var db = _context.Db;
            var exists = await db.Queryable<HBLocalSupplier>()
                .AnyAsync(x => x.LocalSupplierCode == code && !x.IsDeleted);
            return ApiResponse<bool>.OK(exists);
        }
    }
}
