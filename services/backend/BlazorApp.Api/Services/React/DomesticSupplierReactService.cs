using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    /// <summary>
    /// React 国内供应商服务（独立实现，不委托原服务）
    /// </summary>
    public class DomesticSupplierReactService : IDomesticSupplierReactService
    {
        private readonly SqlSugarContext _context;
        private readonly IMapper _mapper;
        private readonly ILogger<DomesticSupplierReactService> _logger;

        public DomesticSupplierReactService(SqlSugarContext context, IMapper mapper, ILogger<DomesticSupplierReactService> logger)
        {
            _context = context;
            _mapper = mapper;
            _logger = logger;
        }

        /// <summary>
        /// 获取所有启用的供应商列表（用于下拉选择）
        /// </summary>
        public async Task<List<DomesticSupplierDto>> GetActiveSupplierListAsync()
        {
            try
            {
                var suppliers = await _context.ChinaSupplierDb
                    .AsQueryable()
                    .Where(s => s.Status == 1)
                    .OrderBy(s => s.SupplierCode)
                    .ToListAsync();

                return _mapper.Map<List<DomesticSupplierDto>>(suppliers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取启用供应商列表失败");
                throw;
            }
        }

        /// <summary>
        /// 根据供应商编码获取供应商详情
        /// </summary>
        public async Task<DomesticSupplierDto?> GetSupplierByCodeAsync(string supplierCode)
        {
            try
            {
                var supplier = await _context.ChinaSupplierDb
                    .AsQueryable()
                    .Where(s => s.SupplierCode == supplierCode)
                    .FirstAsync();

                return supplier != null ? _mapper.Map<DomesticSupplierDto>(supplier) : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "根据编码获取供应商详情失败: {SupplierCode}", supplierCode);
                throw;
            }
        }
    }
}