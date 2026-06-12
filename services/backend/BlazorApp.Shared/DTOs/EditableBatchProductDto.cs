using System;
using System.Collections.Generic;
using System.Linq;

namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 可编辑的批量商品DTO
    /// </summary>
    public class EditableBatchProductDto : BatchProductInputDto
    {
        /// <summary>
        /// 原始数据备份
        /// </summary>
        private readonly BatchProductInputDto _originalData;
        
        /// <summary>
        /// 已更改的字段集合
        /// </summary>
        public HashSet<string> ChangedFields { get; } = new HashSet<string>();
        
        /// <summary>
        /// 验证错误字典
        /// </summary>
        public Dictionary<string, string> ValidationErrors { get; } = new Dictionary<string, string>();
        
        /// <summary>
        /// 是否有更改
        /// </summary>
        public bool HasChanges => ChangedFields.Any();
        
        /// <summary>
        /// 是否有错误
        /// </summary>
        public bool HasErrors => ValidationErrors.Any();
        
        /// <summary>
        /// 构造函数
        /// </summary>
        public EditableBatchProductDto(BatchProductInputDto originalProduct)
        {
            _originalData = originalProduct ?? throw new ArgumentNullException(nameof(originalProduct));
            
            // 复制所有属性
            HBProductNo = originalProduct.HBProductNo;
            ProductName = originalProduct.ProductName;
            EnglishProductName = originalProduct.EnglishProductName;
            Barcode = originalProduct.Barcode;
            DomesticPrice = originalProduct.DomesticPrice;
            OEMPrice = originalProduct.OEMPrice;
            PackingQuantity = originalProduct.PackingQuantity;
            UnitVolume = originalProduct.UnitVolume;
            MiddlePackQuantity = originalProduct.MiddlePackQuantity;
            ProductImage = originalProduct.ProductImage;
        }
        
        /// <summary>
        /// 标记字段已更改
        /// </summary>
        public void MarkFieldAsChanged(string fieldName)
        {
            ChangedFields.Add(fieldName);
        }
        
        /// <summary>
        /// 重置更改
        /// </summary>
        public void ResetChanges()
        {
            // 恢复所有字段到原始值
            HBProductNo = _originalData.HBProductNo;
            ProductName = _originalData.ProductName;
            EnglishProductName = _originalData.EnglishProductName;
            Barcode = _originalData.Barcode;
            DomesticPrice = _originalData.DomesticPrice;
            OEMPrice = _originalData.OEMPrice;
            PackingQuantity = _originalData.PackingQuantity;
            UnitVolume = _originalData.UnitVolume;
            MiddlePackQuantity = _originalData.MiddlePackQuantity;
            ProductImage = _originalData.ProductImage;
            
            ChangedFields.Clear();
            ValidationErrors.Clear();
        }
        
        /// <summary>
        /// 提交更改
        /// </summary>
        public void CommitChanges()
        {
            ChangedFields.Clear();
            ValidationErrors.Clear();
        }
    }
}
