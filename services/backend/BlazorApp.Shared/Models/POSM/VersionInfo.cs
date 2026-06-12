using System;
using SqlSugar;

namespace BlazorApp.Service.Models.HBPOSM_POSM;

// 定义VersionInfo类，表示软件版本信息

[SugarTable("VersionInfo"), Tenant("HBPOSM_POSM")] // 添加SqlSugar特性，指定数据库表名/ 添加SqlSugar特性，指定数据库表名
public class VersionInfo
{
    // 版本号，作为主键
    [SugarColumn(ColumnName = "Version", IsPrimaryKey = true)]
    public string Version { get; set; } = string.Empty;

    // 软件平台信息 WIN_POS/PDA_WH/PDA_Store/ios
    [SugarColumn(ColumnName = "SoftName", IsNullable = true)]
    public string SoftName { get; set; } = string.Empty;

    // 发布日期，可能为空
    [SugarColumn(ColumnName = "ReleaseDate", IsNullable = true)]
    public DateTime? ReleaseDate { get; set; }

    // 版本描述
    [SugarColumn(ColumnName = "Description", IsNullable = true)]
    public string Description { get; set; } = string.Empty;

    // 文件名
    [SugarColumn(ColumnName = "FileName", IsNullable = true)]
    public string FileName { get; set; } = string.Empty;

    // 文件大小，单位为字节
    [SugarColumn(ColumnName = "FileSize", IsNullable = true)]
    public long? FileSize { get; set; }

    // 下载来源相对路径
    [SugarColumn(ColumnName = "DownloadFromPath", IsNullable = true)]
    public string DownloadFromPath { get; set; } = string.Empty;

    // 下载目标相对路径
    [SugarColumn(ColumnName = "DownloadToPath", IsNullable = true)]
    public string? DownloadToPath { get; set; }

    // 解压相对路径
    [SugarColumn(ColumnName = "UnzipPath", IsNullable = true)]
    public string? UnzipPath { get; set; }

    // 文件的MD5值，用于完整性校验
    [SugarColumn(ColumnName = "FileMD5", IsNullable = true)]
    public string FileMD5 { get; set; } = string.Empty;

    // 创建者信息
    [SugarColumn(ColumnName = "CreatedBy", IsNullable = true)]
    public string CreatedBy { get; set; } = string.Empty;

    // 创建日期，可能为空
    [SugarColumn(ColumnName = "CreatedDate", IsNullable = true)]
    public DateTime? CreatedDate { get; set; }

    // 修改者信息
    [SugarColumn(ColumnName = "ModifiedBy", IsNullable = true)]
    public string ModifiedBy { get; set; } = string.Empty;

    // 修改日期，可能为空
    [SugarColumn(ColumnName = "ModifiedDate", IsNullable = true)]
    public DateTime? ModifiedDate { get; set; }
}
