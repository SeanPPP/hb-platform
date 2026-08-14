namespace Hbpos.Contracts.Stores;

/// <summary>
/// 当前设备门店的小票资料。仅 Address 与 ReturnPolicy 允许 CR/LF/TAB；
/// 其余字段（StoreCode/StoreName/BrandName/Phone/Abn）拒绝任何控制字符
/// （含 DEL）。所有字段均拒绝其他不可打印控制字符；空字符串与 null 均为有效值。
/// </summary>
public sealed record StoreReceiptProfileDto(
    string StoreCode,
    string StoreName,
    string? BrandName,
    string? Address,
    string? Phone,
    string? Abn,
    string? ReturnPolicy);
