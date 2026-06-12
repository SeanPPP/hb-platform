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
}