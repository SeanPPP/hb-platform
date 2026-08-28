using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Features.ProductWarehouse;

internal interface IProductWarehouseDetectionSlice
{
    Task<List<DetectionResultDto>> DetectAsync(List<DetectionItemDto> items);
}

internal interface IProductWarehouseBatchUpdateSlice
{
    Task<BatchOperationResultDto> BatchUpdateAsync(List<UpdateItemDto> items);
    Task<BatchOperationResultDto> BatchUpdateAsync(List<UpdateItemDto> items, string? updatedBy);
    Task<WarehouseProductBatchUpdateResultDto> BatchUpdateAsync(
        List<UpdateItemDto> items,
        string? updatedBy,
        WarehouseProductBatchUpdateOptionsDto options
    );
}

internal interface IProductWarehouseBatchCreationSlice
{
    Task<BatchOperationResultDto> BatchCreateAsync(List<CreateItemDto> items, bool useTransaction = true);
    Task<BatchOperationResultDto> BatchCreateAsync(List<CreateItemDto> items, bool useTransaction, string? updatedBy);
    Task<BatchOperationResultDto> BatchCreateAsync(
        List<CreateItemDto> items,
        bool useTransaction,
        string? updatedBy,
        string auditSource,
        string? sourceReference,
        Guid? batchGuid
    );
    Task<BatchOperationResultDto> BatchCreateAsync(
        List<CreateItemDto> items,
        bool useTransaction,
        string? updatedBy,
        string auditSource,
        string? sourceReference,
        Guid? batchGuid,
        string? actorUserGuid
    );
}

internal interface IProductWarehouseTableSlice
{
    Task<ReactTableResponseDto<WarehouseProductReactListDto>> GetAntdTableDataAsync(ReactTableRequestDto request);
    ISugarQueryable<ProductWarehouseTableCodeSearchCandidate> BuildWarehouseTextSearchCandidateQuery(string keyword);
    ISugarQueryable<ProductWarehouseTableCodeSearchCandidate> BuildWarehouseCodeSearchCandidateQuery(string keyword);
}

internal interface IProductWarehouseSingleCreationSlice
{
    Task<CreateSingleProductResponseDto> CreateSingleProductAsync(CreateSingleProductRequestDto request);
    Task<CreateSingleProductResponseDto> CreateSingleProductAsync(CreateSingleProductRequestDto request, string? updatedBy);
}

internal interface IProductWarehouseImportSlice
{
    Task<ReactTableResponseDto<DomesticProductNotInWarehouseDto>> GetDomesticProductsNotInWarehouseAsync(
        GetDomesticProductsNotInWarehouseRequestDto request
    );
    Task<ImportFromDomesticResponseDto> ImportFromDomesticAsync(ImportFromDomesticRequestDto request);
    Task<ImportFromDomesticResponseDto> ImportFromDomesticAsync(ImportFromDomesticRequestDto request, string? updatedBy);
    Task<ReactTableResponseDto<NonHotbargainProductNotInWarehouseDto>> GetNonHotbargainProductsNotInWarehouseAsync(
        GetNonHotbargainProductsNotInWarehouseRequestDto request
    );
    Task<ImportFromDomesticResponseDto> ImportNonHotbargainProductsAsync(ImportNonHotbargainRequestDto request);
    Task<ImportFromDomesticResponseDto> ImportNonHotbargainProductsAsync(
        ImportNonHotbargainRequestDto request,
        string? updatedBy
    );
}

internal interface IProductWarehouseUpdateSlice
{
    Task<WarehouseProductFullUpdateResultDto> FullUpdateAsync(string productCode, WarehouseProductFullUpdateDto dto);
    Task<WarehouseProductFullUpdateResultDto> FullUpdateAsync(
        string productCode,
        WarehouseProductFullUpdateDto dto,
        string? updatedBy
    );
    Task<WarehouseProductPatchResultDto?> PatchAsync(string productCode, WarehouseProductPatchDto dto);
    Task<WarehouseProductPatchResultDto?> PatchAsync(
        string productCode,
        WarehouseProductPatchDto dto,
        string? updatedBy
    );
    Task<BatchToggleWarehouseProductsActiveResultDto> BatchToggleActiveAsync(
        BatchToggleWarehouseProductsActiveRequestDto request
    );
    Task<BatchToggleWarehouseProductsActiveResultDto> BatchToggleActiveAsync(
        BatchToggleWarehouseProductsActiveRequestDto request,
        string? updatedBy
    );
}

internal interface IProductWarehouseBarcodePriceSlice
{
    Task<List<BarcodePriceItemDto>> GetBarcodePricesAsync(string productCode);
}

internal interface IProductWarehouseSyncSlice
{
    Task<SyncResult> SyncFromHqAsync();
    Task<SyncResult> SyncFromHqAsync(string? actorUserGuid, string? actorName);
}

internal interface IProductWarehouseMobileSlice
{
    Task<List<WarehouseMobileProductDto>> LookupMobileProductsAsync(string keyword);
    Task<WarehouseMobileProductDto?> GetMobileProductAsync(string productCode);
    Task<WarehouseMobileProductDto?> PatchMobileProductAsync(
        string productCode,
        WarehouseMobileProductPatchDto dto
    );
    Task<WarehouseMobileProductDto?> PatchMobileProductAsync(
        string productCode,
        WarehouseMobileProductPatchDto dto,
        string? updatedBy
    );
    Task<WarehouseMobileProductDto?> SetMobileProductLocationAsync(string productCode, string? locationGuid);
    Task<WarehouseProductLabelPrintDto?> GetMobileProductPrintPayloadAsync(string productCode);
    Task<WarehouseLocationLabelPrintDto?> GetMobileLocationPrintPayloadAsync(string productCode);
}
