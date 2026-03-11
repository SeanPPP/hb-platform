using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Service.Models.HBPOSM_POSM;
using Microsoft.Extensions.Logging;

namespace BlazorApp.Api.Services
{
    /// <summary>
    /// 版本管理服务实现
    /// </summary>
    public class VersionInfoService : IVersionInfoService
    {
        private readonly POSMSqlSugarContext _context;
        private readonly ILogger<VersionInfoService> _logger;

        public VersionInfoService(POSMSqlSugarContext context, ILogger<VersionInfoService> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// 获取版本列表（分页）
        /// </summary>
        public async Task<ApiResponse<PagedResult<VersionInfoDto>>> GetVersionsAsync(VersionInfoQueryDto query)
        {
            try
            {
                var db = _context.VersionInfoDb;
                var queryable = db.AsQueryable();

                // 搜索条件
                if (!string.IsNullOrEmpty(query.Keyword))
                {
                    queryable = queryable.Where(v =>
                        (v.Version != null && v.Version.Contains(query.Keyword)) ||
                        (v.Description != null && v.Description.Contains(query.Keyword)) ||
                        (v.FileName != null && v.FileName.Contains(query.Keyword))
                    );
                }

                // 软件平台筛选
                if (!string.IsNullOrEmpty(query.SoftName))
                {
                    queryable = queryable.Where(v => v.SoftName == query.SoftName);
                }

                // 排序
                queryable = query.SortBy.ToLower() switch
                {
                    "version" => query.SortDescending
                        ? queryable.OrderByDescending(v => v.Version)
                        : queryable.OrderBy(v => v.Version),
                    "softname" => query.SortDescending
                        ? queryable.OrderByDescending(v => v.SoftName)
                        : queryable.OrderBy(v => v.SoftName),
                    "releasedate" => query.SortDescending
                        ? queryable.OrderByDescending(v => v.ReleaseDate)
                        : queryable.OrderBy(v => v.ReleaseDate),
                    "createddate" => query.SortDescending
                        ? queryable.OrderByDescending(v => v.CreatedDate)
                        : queryable.OrderBy(v => v.CreatedDate),
                    "modifieddate" => query.SortDescending
                        ? queryable.OrderByDescending(v => v.ModifiedDate)
                        : queryable.OrderBy(v => v.ModifiedDate),
                    _ => query.SortDescending
                        ? queryable.OrderByDescending(v => v.ReleaseDate)
                        : queryable.OrderBy(v => v.ReleaseDate),
                };

                // 分页
                var totalCount = await queryable.CountAsync();
                var skip = (query.PageNumber - 1) * query.PageSize;
                var versions = await queryable
                    .Skip(skip)
                    .Take(query.PageSize)
                    .ToListAsync();

                // 转换为DTO
                var versionDtos = versions.Select(MapToDto).ToList();

                var pagedResult = new PagedResult<VersionInfoDto>
                {
                    Items = versionDtos,
                    Total = totalCount,
                    Page = query.PageNumber,
                    PageSize = query.PageSize
                };

                return ApiResponse<PagedResult<VersionInfoDto>>.OK(pagedResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取版本列表失败");
                return ApiResponse<PagedResult<VersionInfoDto>>.Error("获取版本列表失败", "GET_VERSIONS_FAILED");
            }
        }

        /// <summary>
        /// 根据版本号获取版本详情
        /// </summary>
        public async Task<ApiResponse<VersionInfoDto>> GetVersionByAsync(string version)
        {
            try
            {
                var db = _context.VersionInfoDb;
                var versionInfo = await db.GetByIdAsync(version);

                if (versionInfo == null)
                {
                    return ApiResponse<VersionInfoDto>.Error("版本不存在", "VERSION_NOT_FOUND");
                }

                return ApiResponse<VersionInfoDto>.OK(MapToDto(versionInfo));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取版本详情失败，Version: {Version}", version);
                return ApiResponse<VersionInfoDto>.Error("获取版本详情失败", "GET_VERSION_FAILED");
            }
        }

        /// <summary>
        /// 创建版本
        /// </summary>
        public async Task<ApiResponse<VersionInfoDto>> CreateVersionAsync(CreateVersionInfoDto dto, string createdBy)
        {
            try
            {
                var db = _context.VersionInfoDb;

                // 检查版本号是否已存在
                var existingVersion = await db.GetByIdAsync(dto.Version);
                if (existingVersion != null)
                {
                    return ApiResponse<VersionInfoDto>.Error("版本号已存在", "VERSION_ALREADY_EXISTS");
                }

                _logger.LogInformation(
                    "=== 创建版本调试 ===\nFileName: {FileName}\nDownloadFromPath: {DownloadFromPath}\n====================",
                    dto.FileName,
                    dto.DownloadFromPath
                );

                var versionInfo = new VersionInfo
                {
                    Version = dto.Version,
                    SoftName = dto.SoftName,
                    ReleaseDate = dto.ReleaseDate,
                    Description = dto.Description,
                    FileName = dto.FileName,
                    FileSize = dto.FileSize,
                    DownloadFromPath = dto.DownloadFromPath,
                    DownloadToPath = dto.DownloadToPath,
                    UnzipPath = dto.UnzipPath,
                    FileMD5 = dto.FileMD5,
                    CreatedBy = createdBy,
                    CreatedDate = DateTime.Now,
                    ModifiedBy = createdBy,
                    ModifiedDate = DateTime.Now
                };

                await db.InsertAsync(versionInfo);

                _logger.LogInformation(
                    "=== 创建版本成功 ===\nVersion: {Version}\nSoftName: {SoftName}\nSavedFileName: {FileName}\nSavedDownloadPath: {DownloadFromPath}\n====================",
                    dto.Version,
                    dto.SoftName,
                    versionInfo.FileName,
                    versionInfo.DownloadFromPath
                );

                return ApiResponse<VersionInfoDto>.OK(MapToDto(versionInfo));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建版本失败，Version: {Version}", dto.Version);
                return ApiResponse<VersionInfoDto>.Error("创建版本失败", "CREATE_VERSION_FAILED");
            }
        }

        /// <summary>
        /// 更新版本
        /// </summary>
        public async Task<ApiResponse<VersionInfoDto>> UpdateVersionAsync(string version, UpdateVersionInfoDto dto, string modifiedBy)
        {
            try
            {
                var db = _context.VersionInfoDb;
                var versionInfo = await db.GetByIdAsync(version);

                if (versionInfo == null)
                {
                    return ApiResponse<VersionInfoDto>.Error("版本不存在", "VERSION_NOT_FOUND");
                }

                // 更新字段
                if (!string.IsNullOrEmpty(dto.SoftName))
                {
                    versionInfo.SoftName = dto.SoftName;
                }
                if (dto.ReleaseDate.HasValue)
                {
                    versionInfo.ReleaseDate = dto.ReleaseDate;
                }
                if (dto.Description != null)
                {
                    versionInfo.Description = dto.Description;
                }
                if (dto.FileName != null)
                {
                    versionInfo.FileName = dto.FileName;
                }
                if (dto.FileSize.HasValue)
                {
                    versionInfo.FileSize = dto.FileSize;
                }
                if (dto.DownloadFromPath != null)
                {
                    versionInfo.DownloadFromPath = dto.DownloadFromPath;
                }
                if (dto.DownloadToPath != null)
                {
                    versionInfo.DownloadToPath = dto.DownloadToPath;
                }
                if (dto.UnzipPath != null)
                {
                    versionInfo.UnzipPath = dto.UnzipPath;
                }
                if (dto.FileMD5 != null)
                {
                    versionInfo.FileMD5 = dto.FileMD5;
                }

                versionInfo.ModifiedBy = modifiedBy;
                versionInfo.ModifiedDate = DateTime.Now;

                await db.UpdateAsync(versionInfo);

                _logger.LogInformation("更新版本成功，Version: {Version}", version);

                return ApiResponse<VersionInfoDto>.OK(MapToDto(versionInfo));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新版本失败，Version: {Version}", version);
                return ApiResponse<VersionInfoDto>.Error("更新版本失败", "UPDATE_VERSION_FAILED");
            }
        }

        /// <summary>
        /// 删除版本
        /// </summary>
        public async Task<ApiResponse<bool>> DeleteVersionAsync(string version)
        {
            try
            {
                var db = _context.VersionInfoDb;
                var versionInfo = await db.GetByIdAsync(version);

                if (versionInfo == null)
                {
                    return ApiResponse<bool>.Error("版本不存在", "VERSION_NOT_FOUND");
                }

                await db.DeleteAsync(versionInfo);

                _logger.LogInformation("删除版本成功，Version: {Version}", version);

                return ApiResponse<bool>.OK(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除版本失败，Version: {Version}", version);
                return ApiResponse<bool>.Error("删除版本失败", "DELETE_VERSION_FAILED");
            }
        }

        /// <summary>
        /// 将VersionInfo实体映射为VersionInfoDto
        /// </summary>
        private static VersionInfoDto MapToDto(VersionInfo versionInfo)
        {
            return new VersionInfoDto
            {
                Version = versionInfo.Version,
                SoftName = versionInfo.SoftName,
                ReleaseDate = versionInfo.ReleaseDate,
                Description = versionInfo.Description,
                FileName = versionInfo.FileName,
                FileSize = versionInfo.FileSize,
                DownloadFromPath = versionInfo.DownloadFromPath,
                DownloadToPath = versionInfo.DownloadToPath,
                UnzipPath = versionInfo.UnzipPath,
                FileMD5 = versionInfo.FileMD5,
                CreatedBy = versionInfo.CreatedBy,
                CreatedDate = versionInfo.CreatedDate,
                ModifiedBy = versionInfo.ModifiedBy,
                ModifiedDate = versionInfo.ModifiedDate
            };
        }
    }
}
