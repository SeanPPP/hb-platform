// src/layout/shopBannerCopy.ts
var orderHistoryBannerCopy = {
  titleKey: "shop.orderHistory",
  titleFallback: "\u5386\u53F2\u8BA2\u5355",
  subtitleKey: "shop.ordersBannerSubtitle",
  subtitleFallback: "\u67E5\u770B\u5206\u5E97\u63D0\u4EA4\u8FC7\u7684\u8BA2\u5355\u3001\u72B6\u6001\u3001\u6570\u91CF\u548C\u91D1\u989D\u6C47\u603B\u3002"
};
function resolveShopBannerCopy(pathname) {
  if (pathname.startsWith("/shop/best-sellers")) {
    return {
      titleKey: "shop.bestSellers",
      titleFallback: "\u70ED\u9500\u5546\u54C1",
      subtitleKey: "shop.bestSellersBannerSubtitle",
      subtitleFallback: "\u67E5\u770B\u70ED\u9500\u5546\u54C1\u9500\u91CF\u3001\u9500\u552E\u989D\u548C\u6392\u540D\u6C47\u603B\u3002"
    };
  }
  if (pathname.startsWith("/shop/coming-soon")) {
    return {
      titleKey: "shop.comingSoon",
      titleFallback: "\u5373\u5C06\u4E0A\u65B0",
      subtitleKey: "shop.comingSoonBannerSubtitle",
      subtitleFallback: "\u67E5\u770B\u5373\u5C06\u5230\u8D27\u8D27\u67DC\uFF0C\u5FEB\u901F\u7B5B\u9009\u8865\u8D27\u548C\u65B0\u54C1\u3002"
    };
  }
  return orderHistoryBannerCopy;
}

// src/layout/shopBannerCopy.test.ts
function assertEqual(actual, expected, message) {
  if (actual !== expected) {
    throw new Error(`${message}: expected ${String(expected)}, got ${String(actual)}`);
  }
}
var bestSellersCopy = resolveShopBannerCopy("/shop/best-sellers");
assertEqual(bestSellersCopy.titleKey, "shop.bestSellers", "\u70ED\u9500\u5546\u54C1\u9875\u6807\u9898\u5E94\u4F7F\u7528\u70ED\u9500\u5546\u54C1\u6587\u6848");
assertEqual(
  bestSellersCopy.subtitleKey,
  "shop.bestSellersBannerSubtitle",
  "\u70ED\u9500\u5546\u54C1\u9875\u526F\u6807\u9898\u4E0D\u5E94\u590D\u7528\u5386\u53F2\u8BA2\u5355\u6587\u6848"
);
var comingSoonCopy = resolveShopBannerCopy("/shop/coming-soon");
assertEqual(comingSoonCopy.titleKey, "shop.comingSoon", "\u5373\u5C06\u4E0A\u65B0\u9875\u6807\u9898\u5E94\u4F7F\u7528\u5373\u5C06\u4E0A\u65B0\u6587\u6848");
assertEqual(
  comingSoonCopy.subtitleKey,
  "shop.comingSoonBannerSubtitle",
  "\u5373\u5C06\u4E0A\u65B0\u9875\u526F\u6807\u9898\u4E0D\u5E94\u590D\u7528\u5386\u53F2\u8BA2\u5355\u6587\u6848"
);
var ordersCopy = resolveShopBannerCopy("/shop/orders");
assertEqual(ordersCopy.titleKey, "shop.orderHistory", "\u5386\u53F2\u8BA2\u5355\u9875\u6807\u9898\u5E94\u4FDD\u6301\u5386\u53F2\u8BA2\u5355\u6587\u6848");
assertEqual(ordersCopy.subtitleKey, "shop.ordersBannerSubtitle", "\u5386\u53F2\u8BA2\u5355\u9875\u526F\u6807\u9898\u5E94\u4FDD\u6301\u8BA2\u5355\u6C47\u603B\u6587\u6848");
var orderDetailCopy = resolveShopBannerCopy("/shop/orders/order-1");
assertEqual(orderDetailCopy.titleKey, "shop.orderHistory", "\u5386\u53F2\u8BA2\u5355\u8BE6\u60C5\u9875\u6807\u9898\u5E94\u4FDD\u6301\u5386\u53F2\u8BA2\u5355\u6587\u6848");
assertEqual(orderDetailCopy.subtitleKey, "shop.ordersBannerSubtitle", "\u5386\u53F2\u8BA2\u5355\u8BE6\u60C5\u9875\u526F\u6807\u9898\u5E94\u4FDD\u6301\u8BA2\u5355\u6C47\u603B\u6587\u6848");
console.log("shopBannerCopy.test: ok");
