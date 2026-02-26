using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BlazorApp.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class CategoriesController : ControllerBase
    {
        private readonly ILogger<CategoriesController> _logger;
        private readonly IWarehouseCategoryService _categoryService;

        public CategoriesController(
            ILogger<CategoriesController> logger,
            IWarehouseCategoryService categoryService
        )
        {
            _logger = logger;
            _categoryService = categoryService;
        }

        [HttpGet]
        public async Task<IActionResult> GetCategories()
        {
            try
            {
                _logger.LogInformation("开始获取商品分类数据");

                var categories = await _categoryService.GetAllAsync();

                // 构建分类树
                var categoryTree = BuildCategoryTree(categories);

                _logger.LogInformation(
                    "成功获取商品分类数据，共 {Count} 个根分类",
                    categoryTree.Count
                );

                return Ok(
                    new ApiResponse<List<WarehouseCategoryDto>>
                    {
                        Success = true,
                        Data = categoryTree,
                        Message = "获取分类成功",
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取分类失败");
                return StatusCode(
                    500,
                    new ApiResponse<List<WarehouseCategoryDto>>
                    {
                        Success = false,
                        Message = "获取分类失败，请稍后重试",
                    }
                );
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateCategory([FromBody] CreateWarehouseCategoryDto dto)
        {
            try
            {
                var created = await _categoryService.CreateAsync(dto);
                return Ok(
                    new ApiResponse<WarehouseCategoryDto>
                    {
                        Success = true,
                        Data = created,
                        Message = "创建分类成功",
                    }
                );
            }
            catch (ValidationException ex)
            {
                return BadRequest(
                    new ApiResponse<WarehouseCategoryDto> { Success = false, Message = ex.Message }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建分类失败");
                return StatusCode(
                    500,
                    new ApiResponse<WarehouseCategoryDto>
                    {
                        Success = false,
                        Message = "创建分类失败，请稍后重试",
                    }
                );
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateCategory(
            string id,
            [FromBody] UpdateWarehouseCategoryDto dto
        )
        {
            try
            {
                dto.CategoryGUID = id;
                var updated = await _categoryService.UpdateAsync(dto);
                return Ok(
                    new ApiResponse<WarehouseCategoryDto>
                    {
                        Success = true,
                        Data = updated,
                        Message = "更新分类成功",
                    }
                );
            }
            catch (ValidationException ex)
            {
                return BadRequest(
                    new ApiResponse<WarehouseCategoryDto> { Success = false, Message = ex.Message }
                );
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(
                    new ApiResponse<WarehouseCategoryDto> { Success = false, Message = ex.Message }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新分类失败");
                return StatusCode(
                    500,
                    new ApiResponse<WarehouseCategoryDto>
                    {
                        Success = false,
                        Message = "更新分类失败，请稍后重试",
                    }
                );
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteCategory(string id)
        {
            try
            {
                var result = await _categoryService.DeleteAsync(id);
                return Ok(
                    new ApiResponse<bool>
                    {
                        Success = result,
                        Data = result,
                        Message = result ? "删除分类成功" : "删除分类失败",
                    }
                );
            }
            catch (ValidationException ex)
            {
                return BadRequest(new ApiResponse<bool> { Success = false, Message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除分类失败");
                return StatusCode(
                    500,
                    new ApiResponse<bool> { Success = false, Message = "删除分类失败，请稍后重试" }
                );
            }
        }

        [HttpPost("batch/activate")]
        public async Task<IActionResult> BatchActivate(
            [FromBody] CategoryBatchActivateRequest request
        )
        {
            var result = new CategoryBatchResult
            {
                Success = true,
                Errors = new Dictionary<string, string>(),
            };
            int success = 0,
                failed = 0;

            foreach (var id in request.Ids.Distinct())
            {
                try
                {
                    var existing = await _categoryService.GetByIdAsync(id);
                    var update = new UpdateWarehouseCategoryDto
                    {
                        CategoryGUID = id,
                        ParentGUID = existing.ParentGUID,
                        CategoryName = existing.CategoryName,
                        ChineseName = existing.ChineseName,
                        IsActive = request.IsActive,
                        SortOrder = existing.SortOrder,
                        Remarks = existing.Remarks,
                    };
                    await _categoryService.UpdateAsync(update);
                    success++;
                }
                catch (Exception ex)
                {
                    failed++;
                    result.Success = false;
                    result.Errors![id] = ex.Message;
                }
            }

            result.SucceededCount = success;
            result.FailedCount = failed;
            result.Message = failed == 0 ? "批量更新启用状态成功" : "部分分类更新失败";
            return Ok(
                new ApiResponse<CategoryBatchResult>
                {
                    Success = true,
                    Data = result,
                    Message = result.Message,
                }
            );
        }

        [HttpPost("batch/move")]
        public async Task<IActionResult> BatchMove([FromBody] CategoryBatchMoveRequest request)
        {
            var result = new CategoryBatchResult
            {
                Success = true,
                Errors = new Dictionary<string, string>(),
            };
            int success = 0,
                failed = 0;

            foreach (var id in request.Ids.Distinct())
            {
                try
                {
                    var existing = await _categoryService.GetByIdAsync(id);
                    var update = new UpdateWarehouseCategoryDto
                    {
                        CategoryGUID = id,
                        ParentGUID = request.NewParentGUID,
                        CategoryName = existing.CategoryName,
                        ChineseName = existing.ChineseName,
                        IsActive = existing.IsActive,
                        SortOrder = existing.SortOrder,
                        Remarks = existing.Remarks,
                    };
                    await _categoryService.UpdateAsync(update);
                    success++;
                }
                catch (Exception ex)
                {
                    failed++;
                    result.Success = false;
                    result.Errors![id] = ex.Message;
                }
            }

            result.SucceededCount = success;
            result.FailedCount = failed;
            result.Message = failed == 0 ? "批量移动分类成功" : "部分分类移动失败";
            return Ok(
                new ApiResponse<CategoryBatchResult>
                {
                    Success = true,
                    Data = result,
                    Message = result.Message,
                }
            );
        }

        [HttpPost("batch/delete")]
        public async Task<IActionResult> BatchDelete([FromBody] CategoryBatchDeleteRequest request)
        {
            var result = new CategoryBatchResult
            {
                Success = true,
                Errors = new Dictionary<string, string>(),
            };
            int success = 0,
                failed = 0;

            foreach (var id in request.Ids.Distinct())
            {
                try
                {
                    var ok = await _categoryService.DeleteAsync(id);
                    if (ok)
                        success++;
                    else
                    {
                        failed++;
                        result.Success = false;
                        result.Errors![id] = "删除失败";
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    result.Success = false;
                    result.Errors![id] = ex.Message;
                }
            }

            result.SucceededCount = success;
            result.FailedCount = failed;
            result.Message = failed == 0 ? "批量删除分类成功" : "部分分类删除失败";
            return Ok(
                new ApiResponse<CategoryBatchResult>
                {
                    Success = true,
                    Data = result,
                    Message = result.Message,
                }
            );
        }

        [HttpGet("test")]
        public IActionResult TestMapping()
        {
            try
            {
                _logger.LogInformation("测试AutoMapper映射配置");

                // 创建一个测试分类
                var testCategory = new WarehouseCategory
                {
                    CategoryGUID = Guid.NewGuid().ToString(),
                    CategoryName = "测试分类",
                    ChineseName = "Test Category",
                    IsActive = true,
                    SortOrder = 1,
                    Remarks = "测试分类",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                };

                // 测试映射
                var mapper = HttpContext.RequestServices.GetRequiredService<IMapper>();
                var mappedDto = mapper.Map<WarehouseCategoryDto>(testCategory);

                return Ok(
                    new ApiResponse<WarehouseCategoryDto>
                    {
                        Success = true,
                        Data = mappedDto,
                        Message = "映射测试成功",
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "映射测试失败");
                return StatusCode(
                    500,
                    new ApiResponse<WarehouseCategoryDto>
                    {
                        Success = false,
                        Message = $"映射测试失败: {ex.Message}",
                    }
                );
            }
        }

        private List<WarehouseCategoryDto> BuildCategoryTree(List<WarehouseCategoryDto> categories)
        {
            var categoryDict = categories.ToDictionary(c => c.CategoryGUID);
            var rootCategories = new List<WarehouseCategoryDto>();

            foreach (var category in categories)
            {
                if (string.IsNullOrEmpty(category.ParentGUID))
                {
                    // 根分类
                    rootCategories.Add(category);
                }
                else if (categoryDict.ContainsKey(category.ParentGUID))
                {
                    // 子分类
                    var parent = categoryDict[category.ParentGUID];
                    parent.Children.Add(category);
                }
            }

            return rootCategories;
        }
    }
}
