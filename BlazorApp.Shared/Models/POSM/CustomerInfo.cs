using System;
using SqlSugar;

namespace BlazorApp.Shared.Models.POSM;

/// <summary>
/// 顾客信息实体类
/// </summary>
[SugarTable("CustomerInfo"), Tenant("HBPOSM")]
public class CustomerInfo
{
    /// <summary>
    /// 主键ID
    /// </summary>
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int ID { get; set;   }

    /// <summary>
    /// 顾客代码
    /// </summary>
    [SugarColumn(ColumnName = "CustomerCode", IsNullable = false)]
    public string? CustomerCode { get; set; } 



     /// <summary>
    /// 顾客姓
    /// </summary>
    [SugarColumn(ColumnName = "LastName", IsNullable = true)]
    public string? LastName { get; set; }   

    /// <summary>
    /// 顾客名称
    /// </summary>
    [SugarColumn(ColumnName = "FirstName", IsNullable = true)]
    public string? FirstName { get; set; }   

    /// <summary>
    /// 顾客电话
    /// </summary>
    [SugarColumn(ColumnName = "CustomerPhone", IsNullable = true)]
    public string? CustomerPhone { get; set; } 

    /// <summary>
    /// 顾客邮箱
    /// </summary>
    [SugarColumn(ColumnName = "CustomerEmail", IsNullable = true)]
    public string? CustomerEmail { get; set; } 

    /// <summary>
    /// 顾客邮编
    /// </summary>
    [SugarColumn(ColumnName = "CustomerPostCode", IsNullable = true)]
    public string? CustomerPostCode { get; set; } 

    /// <summary>
    /// 备注 分期订单号
    /// </summary>
    [SugarColumn(ColumnName = "Remark", IsNullable = true)]
    public string? Remark { get; set; } 

    /// <summary>
    /// 创建时间
    /// </summary>
    [SugarColumn(ColumnName = "CreateTime", IsNullable = true)]
    public DateTime? CreateTime { get; set; }   

    /// <summary>
    /// 更新时间
    /// </summary>
    [SugarColumn(ColumnName = "UpdateTime", IsNullable = true)]
    public DateTime? UpdateTime { get; set; } 

    /// <summary>
    /// 是否删除
    /// </summary>
    [SugarColumn(ColumnName = "IsDelete", IsNullable = true)]
    public bool? IsDelete { get; set; } 

    /// <summary>
    /// 创建人
    /// </summary>
    [SugarColumn(ColumnName = "CreateUser", IsNullable = true)]
    public string? CreateUser { get; set; }

    /// <summary>
    /// 更新人
    /// </summary>
    [SugarColumn(ColumnName = "UpdateUser", IsNullable = true)]
    public string? UpdateUser { get; set; } 
    
}
