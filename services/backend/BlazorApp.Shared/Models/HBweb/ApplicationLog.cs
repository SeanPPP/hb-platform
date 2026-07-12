using SqlSugar;

namespace BlazorApp.Shared.Models.HBweb
{
    /// <summary>
    /// 多项目中心应用日志，记录后端、Web、移动端、收银端的错误与异常。
    /// </summary>
    public class ApplicationLog : BlazorApp.Shared.Models.BaseEntity
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = false)]
        public Guid Id { get; set; } = Guid.NewGuid();

        [SugarColumn(IsNullable = false)]
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        [SugarColumn(Length = 80, IsNullable = false)]
        public string ProjectCode { get; set; } = string.Empty;

        [SugarColumn(Length = 120, IsNullable = true)]
        public string? ProjectName { get; set; }

        [SugarColumn(Length = 60, IsNullable = false)]
        public string Environment { get; set; } = string.Empty;

        [SugarColumn(Length = 60, IsNullable = false)]
        public string SourceType { get; set; } = string.Empty;

        [SugarColumn(Length = 120, IsNullable = true)]
        public string? ServiceName { get; set; }

        [SugarColumn(Length = 120, IsNullable = true)]
        public string? InstanceId { get; set; }

        [SugarColumn(IsNullable = true)]
        public Guid? ClientEventId { get; set; }

        [SugarColumn(Length = 80, IsNullable = true)]
        public string? StoreCode { get; set; }

        [SugarColumn(Length = 120, IsNullable = true)]
        public string? DeviceCode { get; set; }

        [SugarColumn(Length = 60, IsNullable = true)]
        public string? AppVersion { get; set; }

        [SugarColumn(Length = 30, IsNullable = false)]
        public string Level { get; set; } = string.Empty;

        [SugarColumn(Length = 240, IsNullable = true)]
        public string? Category { get; set; }

        [SugarColumn(Length = 80, IsNullable = true)]
        public string? EventId { get; set; }

        [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = false)]
        public string Message { get; set; } = string.Empty;

        [SugarColumn(Length = 240, IsNullable = true)]
        public string? ExceptionType { get; set; }

        [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)]
        public string? ExceptionMessage { get; set; }

        [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)]
        public string? StackTrace { get; set; }

        [SugarColumn(Length = 500, IsNullable = true)]
        public string? RequestPath { get; set; }

        [SugarColumn(Length = 20, IsNullable = true)]
        public string? RequestMethod { get; set; }

        [SugarColumn(IsNullable = true)]
        public int? StatusCode { get; set; }

        [SugarColumn(Length = 120, IsNullable = true)]
        public string? TraceId { get; set; }

        [SugarColumn(Length = 120, IsNullable = true)]
        public string? UserId { get; set; }

        [SugarColumn(Length = 120, IsNullable = true)]
        public string? UserName { get; set; }

        [SugarColumn(Length = 80, IsNullable = true)]
        public string? ClientIp { get; set; }

        [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)]
        public string? PropertiesJson { get; set; }
    }
}
