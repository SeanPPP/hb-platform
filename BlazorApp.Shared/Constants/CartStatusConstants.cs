namespace BlazorApp.Shared.Constants
{
    /// <summary>
    /// 购物车状态常量定义
    /// </summary>
    public static class CartStatusConstants
    {
        /// <summary>
        /// 活跃状态 - 正在编辑的购物车
        /// </summary>
        public const string Active = "Active";
        
        /// <summary>
        /// 已保存状态 - 保存的购物车，可以继续编辑
        /// </summary>
        public const string Save = "Save";
        
        /// <summary>
        /// 已提交状态 - 已Checkout的购物车，转为订单
        /// </summary>
        public const string Submitted = "Submitted";

                
        /// <summary>
        /// 配货中 - 已Checkout的购物车，转为订单
        /// </summary>
        public const string Picking = "Picking";

        /// <summary>
        /// 已发货 - 已Checkout的购物车，转为订单
        /// </summary>
        public const string Shipped = "Shipped";
        
        /// <summary>
        /// 已到货 - 已Checkout的购物车，转为订单
        /// </summary>
        public const string Received = "Received";
        
        /// <summary>
        /// 已删除 - 软删除的购物车，不在正常列表中显示
        /// </summary>
        public const string Deleted = "Deleted";
    }
    
    /// <summary>
    /// 订单号生成器
    /// </summary>
    public static class OrderNumberGenerator
    {
        /// <summary>
        /// 订单号起始序列号（从1000开始）
        /// </summary>
        public const int StartingSequence = 1000;
        
        /// <summary>
        /// 生成订单号（格式：ORD-YYYY-1000）
        /// </summary>
        /// <param name="currentYear">当前年份</param>
        /// <param name="sequenceNumber">顺序号（从1000开始）</param>
        /// <returns>格式化的订单号</returns>
        public static string Generate(int currentYear, int sequenceNumber)
        {
            return $"ORD-{currentYear}-{sequenceNumber:D4}";
        }
        
        /// <summary>
        /// 解析订单号获取年份和顺序号
        /// </summary>
        /// <param name="orderNumber">订单号</param>
        /// <returns>年份和顺序号的元组，解析失败返回null</returns>
        public static (int year, int sequence)? Parse(string orderNumber)
        {
            if (string.IsNullOrEmpty(orderNumber))
                return null;
                
            var parts = orderNumber.Split('-');
            if (parts.Length != 3 || parts[0] != "ORD")
                return null;
                
            if (int.TryParse(parts[1], out int year) && int.TryParse(parts[2], out int sequence))
            {
                return (year, sequence);
            }
            
            return null;
        }
    }
}