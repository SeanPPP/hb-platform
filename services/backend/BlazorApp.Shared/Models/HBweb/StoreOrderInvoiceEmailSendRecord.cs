using SqlSugar;

namespace BlazorApp.Shared.Models.HBweb
{
    /// <summary>
    /// 分店订货发票邮件成功发送记录。
    /// </summary>
    [SugarTable("StoreOrderInvoiceEmailSendRecord")]
    public class StoreOrderInvoiceEmailSendRecord
    {
        [SugarColumn(IsPrimaryKey = true, Length = 50, IsNullable = false)]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// 对应订货单 GUID。
        /// </summary>
        [SugarColumn(Length = 50, IsNullable = false)]
        public string StoreOrderUuid { get; set; } = string.Empty;

        /// <summary>
        /// 成功发送到的收件邮箱。
        /// </summary>
        [SugarColumn(Length = 200, IsNullable = false)]
        public string ToEmail { get; set; } = string.Empty;

        /// <summary>
        /// 邮件实际成功发送时间（UTC）。
        /// </summary>
        [SugarColumn(IsNullable = false)]
        public DateTime SentAtUtc { get; set; }

        /// <summary>
        /// 对应后台发送任务 ID。
        /// </summary>
        [SugarColumn(Length = 50, IsNullable = false)]
        public string JobId { get; set; } = string.Empty;

        /// <summary>
        /// 本地记录创建时间（UTC）。
        /// </summary>
        [SugarColumn(IsNullable = false)]
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
