using System;
using System.Collections.Generic;

namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 货柜明细创建新商品 job 状态常量。
    /// </summary>
    public static class ContainerProductCreationJobStatusConstants
    {
        public const string Queued = "Queued";
        public const string Running = "Running";
        public const string Succeeded = "Succeeded";
        public const string Failed = "Failed";
    }

    /// <summary>
    /// 货柜明细创建新商品 job 请求。
    /// </summary>
    public class ContainerProductCreationJobRequestDto
    {
        public string OperationId { get; set; } = string.Empty;
        public string ContainerGuid { get; set; } = string.Empty;
        public List<string> DetailHguids { get; set; } = new();
    }

    /// <summary>
    /// 货柜创建新商品逐项结果。
    /// </summary>
    public class ContainerProductCreationResultItemDto
    {
        public string? ProductCode { get; set; }
        public string? ItemNumber { get; set; }
        public string? DetailHguid { get; set; }
        public string? ReasonCode { get; set; }
        public string? Message { get; set; }
    }

    /// <summary>
    /// 货柜创建新商品执行结果。
    /// </summary>
    public class ContainerProductCreationResultDto
    {
        public int CreatedCount { get; set; }
        public int SkippedCount { get; set; }
        public int FailedCount { get; set; }
        public List<ContainerProductCreationResultItemDto> Created { get; set; } = new();
        public List<ContainerProductCreationResultItemDto> Skipped { get; set; } = new();
        public List<ContainerProductCreationResultItemDto> Errors { get; set; } = new();
    }

    /// <summary>
    /// 货柜创建新商品 job 快照。
    /// </summary>
    public class ContainerProductCreationJobDto
    {
        public string JobId { get; set; } = string.Empty;
        public string Status { get; set; } = ContainerProductCreationJobStatusConstants.Queued;
        public string? OperationId { get; set; }
        public string? Message { get; set; }
        public bool IsDuplicateRequest { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public ContainerProductCreationResultDto Result { get; set; } = new();
    }
}
