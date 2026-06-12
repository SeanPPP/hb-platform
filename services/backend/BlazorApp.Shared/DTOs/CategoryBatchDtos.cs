using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs
{
    public class CategoryBatchActivateRequest
    {
        [Required]
        public List<string> Ids { get; set; } = new();
        public bool IsActive { get; set; }
    }

    public class CategoryBatchMoveRequest
    {
        [Required]
        public List<string> Ids { get; set; } = new();
        public string? NewParentGUID { get; set; }
    }

    public class CategoryBatchDeleteRequest
    {
        [Required]
        public List<string> Ids { get; set; } = new();
    }

    public class CategoryBatchResult
    {
        public bool Success { get; set; }
        public int SucceededCount { get; set; }
        public int FailedCount { get; set; }
        public string? Message { get; set; }
        public Dictionary<string, string>? Errors { get; set; }
    }
}