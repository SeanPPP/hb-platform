from __future__ import annotations

from pathlib import Path
from typing import Iterable

from reportlab.graphics import renderPDF
from reportlab.graphics.barcode import qr
from reportlab.graphics.shapes import Drawing
from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER, TA_LEFT
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.utils import ImageReader
from reportlab.lib.units import mm
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfgen import canvas
from reportlab.platypus import Paragraph


ROOT = Path("/Users/sean/DEV/hb-platform")
OUTPUT = ROOT / "output/pdf/HB-Supplier-Order-Extension-Guide-ZH-EN.pdf"
ICON = (
    ROOT
    / "apps/supplier-order-safari-extension/xcode/HB Supplier Order Safari/"
    "HB Supplier Order Safari/Assets.xcassets/AppIcon.appiconset/"
    "universal-icon-1024@1x.png"
)

EDGE_URL = "https://microsoftedge.microsoft.com/addons/detail/eeggjfaljfdkoanlaonfiodmljkmpfhn"
IOS_URL = "https://apps.apple.com/au/app/hb-supplier-order/id6803740010"
SHOP_URL = "https://hotbargain.vip/shop"
SUPPORT_URL = "https://hotbargain.vip/support/hb-supplier-order"
MICROSOFT_SOURCE = "https://support.microsoft.com/en-us/edge/add-turn-off-or-remove-extensions-in-microsoft-edge"
APPLE_SOURCE = "https://support.apple.com/guide/iphone/get-extensions-iphab0432bf6/ios"
APPLE_UNLISTED_SOURCE = "https://developer.apple.com/support/unlisted-app-distribution"

PAGE_W, PAGE_H = A4
MARGIN_X = 16 * mm
CONTENT_W = PAGE_W - 2 * MARGIN_X
ORANGE = colors.HexColor("#F15A2B")
ORANGE_DARK = colors.HexColor("#C63F17")
ORANGE_PALE = colors.HexColor("#FFF2EB")
NAVY = colors.HexColor("#09233A")
NAVY_2 = colors.HexColor("#173A55")
BLUE = colors.HexColor("#2467A5")
BLUE_PALE = colors.HexColor("#EEF6FC")
INK = colors.HexColor("#17222D")
MUTED = colors.HexColor("#5A6975")
LINE = colors.HexColor("#DDE4E9")
PAPER = colors.HexColor("#F7F9FA")
WHITE = colors.white
GREEN = colors.HexColor("#16835B")
GREEN_PALE = colors.HexColor("#EAF7F1")


pdfmetrics.registerFont(
    TTFont("HB-CJK", "/System/Library/Fonts/Supplemental/Arial Unicode.ttf")
)
pdfmetrics.registerFont(
    TTFont("HB-Sans", "/System/Library/Fonts/HelveticaNeue.ttc", subfontIndex=0)
)
pdfmetrics.registerFont(
    TTFont("HB-Sans-Bold", "/System/Library/Fonts/HelveticaNeue.ttc", subfontIndex=1)
)
pdfmetrics.registerFontFamily(
    "HB-Sans",
    normal="HB-Sans",
    bold="HB-Sans-Bold",
)


def style(
    name: str,
    *,
    size: float = 9.4,
    leading: float | None = None,
    color=INK,
    font: str = "HB-CJK",
    align: int = TA_LEFT,
    space_after: float = 0,
) -> ParagraphStyle:
    return ParagraphStyle(
        name,
        fontName=font,
        fontSize=size,
        leading=leading or size * 1.48,
        textColor=color,
        alignment=align,
        wordWrap="CJK",
        allowWidows=0,
        allowOrphans=0,
        spaceAfter=space_after,
    )


S_BODY = style("body", size=9.2, leading=13.7)
S_BODY_SMALL = style("body-small", size=8.1, leading=11.8, color=MUTED)
S_BODY_WHITE = style("body-white", size=9.2, leading=13.5, color=WHITE)
S_NOTE = style("note", size=8.4, leading=12.3, color=NAVY_2)
S_STEP_TITLE = style("step-title", size=10.2, leading=13.1, color=NAVY)
S_CARD_TITLE = style("card-title", size=11.2, leading=14.2, color=NAVY)
S_QR_TITLE = style("qr-title", size=9.2, leading=11.5, color=NAVY, align=TA_CENTER)
S_QR_URL = style("qr-url", size=6.7, leading=8.2, color=MUTED, font="HB-Sans", align=TA_CENTER)
S_CENTER = style("center", size=10.5, leading=14.4, color=INK, align=TA_CENTER)


def paragraph(
    c: canvas.Canvas,
    text: str,
    x: float,
    y_top: float,
    width: float,
    pstyle: ParagraphStyle = S_BODY,
    max_height: float = 1000,
) -> float:
    p = Paragraph(text, pstyle)
    _, height = p.wrap(width, max_height)
    p.drawOn(c, x, y_top - height)
    return height


def rounded_card(
    c: canvas.Canvas,
    x: float,
    y: float,
    width: float,
    height: float,
    *,
    fill=WHITE,
    stroke=LINE,
    radius: float = 10,
    shadow: bool = False,
) -> None:
    if shadow:
        c.setFillColor(colors.Color(0.04, 0.13, 0.20, alpha=0.07))
        c.roundRect(x + 2, y - 3, width, height, radius, stroke=0, fill=1)
    c.setFillColor(fill)
    c.setStrokeColor(stroke)
    c.setLineWidth(0.8)
    c.roundRect(x, y, width, height, radius, stroke=1, fill=1)


def draw_logo(c: canvas.Canvas, x: float, y: float, size: float) -> None:
    c.drawImage(
        ImageReader(str(ICON)),
        x,
        y,
        width=size,
        height=size,
        preserveAspectRatio=True,
        mask="auto",
    )


def page_header(c: canvas.Canvas, number: int, zh: str, en: str) -> None:
    c.setFillColor(NAVY)
    c.rect(0, PAGE_H - 18 * mm, PAGE_W, 18 * mm, stroke=0, fill=1)
    draw_logo(c, MARGIN_X, PAGE_H - 14.8 * mm, 10.5 * mm)
    c.setFillColor(WHITE)
    c.setFont("HB-CJK", 12.4)
    c.drawString(MARGIN_X + 14 * mm, PAGE_H - 9.1 * mm, zh)
    c.setFont("HB-Sans", 8.3)
    c.setFillColor(colors.HexColor("#C9D6DF"))
    c.drawString(MARGIN_X + 14 * mm, PAGE_H - 13.2 * mm, en)
    c.setFillColor(ORANGE)
    c.roundRect(PAGE_W - MARGIN_X - 17 * mm, PAGE_H - 12.9 * mm, 17 * mm, 7.5 * mm, 3.75 * mm, stroke=0, fill=1)
    c.setFillColor(WHITE)
    c.setFont("HB-Sans-Bold", 8.3)
    c.drawCentredString(PAGE_W - MARGIN_X - 8.5 * mm, PAGE_H - 10.1 * mm, f"{number}/7")


def page_footer(c: canvas.Canvas, number: int) -> None:
    c.setStrokeColor(LINE)
    c.setLineWidth(0.6)
    c.line(MARGIN_X, 12 * mm, PAGE_W - MARGIN_X, 12 * mm)
    c.setFillColor(MUTED)
    c.setFont("HB-CJK", 6.9)
    c.drawString(MARGIN_X, 7.7 * mm, "HB Supplier Order  |  内部使用指南 / Internal User Guide")
    c.setFont("HB-Sans", 6.9)
    c.drawRightString(PAGE_W - MARGIN_X, 7.7 * mm, f"Updated 03 Sep 2026   ·   {number}")


def language_label(c: canvas.Canvas, x: float, y: float, label: str, accent) -> None:
    c.setFillColor(accent)
    c.roundRect(x, y - 12, 52, 16, 8, stroke=0, fill=1)
    c.setFillColor(WHITE)
    c.setFont("HB-CJK" if label == "中文" else "HB-Sans-Bold", 7.8)
    c.drawCentredString(x + 26, y - 7.7, label)


def step_item(
    c: canvas.Canvas,
    *,
    x: float,
    y_top: float,
    width: float,
    number: int,
    title: str,
    body: str,
    accent=ORANGE,
    compact: bool = False,
) -> float:
    circle = 18 if compact else 21
    c.setFillColor(accent)
    c.circle(x + circle / 2, y_top - circle / 2, circle / 2, stroke=0, fill=1)
    c.setFillColor(WHITE)
    c.setFont("HB-Sans-Bold", 8.6 if compact else 9.3)
    c.drawCentredString(x + circle / 2, y_top - circle / 2 - 3.0, str(number))
    text_x = x + circle + 9
    text_w = width - circle - 9
    title_h = paragraph(c, title, text_x, y_top + 1, text_w, S_STEP_TITLE)
    body_y = y_top - title_h - 3
    body_style = S_BODY_SMALL if compact else S_BODY
    body_h = paragraph(c, body, text_x, body_y, text_w, body_style)
    return max(circle, title_h + 3 + body_h)


def bilingual_steps(
    c: canvas.Canvas,
    zh_steps: Iterable[tuple[str, str]],
    en_steps: Iterable[tuple[str, str]],
    *,
    y_top: float,
    bottom: float,
    accent=ORANGE,
    compact: bool = False,
) -> float:
    gap = 9 * mm
    col_w = (CONTENT_W - gap) / 2
    left_x = MARGIN_X
    right_x = MARGIN_X + col_w + gap
    language_label(c, left_x, y_top + 3, "中文", accent)
    language_label(c, right_x, y_top + 3, "ENGLISH", accent)
    y_l = y_top - 22
    y_r = y_top - 22
    for index, ((zt, zb), (et, eb)) in enumerate(zip(zh_steps, en_steps), 1):
        h_l = step_item(c, x=left_x, y_top=y_l, width=col_w, number=index, title=zt, body=zb, accent=accent, compact=compact)
        h_r = step_item(c, x=right_x, y_top=y_r, width=col_w, number=index, title=et, body=eb, accent=accent, compact=compact)
        h = max(h_l, h_r)
        y_l -= h + (14 if compact else 17)
        y_r -= h + (14 if compact else 17)
        if y_l < bottom or y_r < bottom:
            raise RuntimeError(f"Bilingual steps overflow at step {index}")
    return min(y_l, y_r)


def draw_qr(c: canvas.Canvas, url: str, x: float, y: float, size: float) -> None:
    widget = qr.QrCodeWidget(url)
    x1, y1, x2, y2 = widget.getBounds()
    scale = size / max(x2 - x1, y2 - y1)
    drawing = Drawing(size, size, transform=[scale, 0, 0, scale, 0, 0])
    drawing.add(widget)
    renderPDF.draw(drawing, c, x, y)
    c.linkURL(url, (x, y, x + size, y + size), relative=0, thickness=0)


def link_text(c: canvas.Canvas, label: str, url: str, x: float, y: float, size: float = 7.3) -> None:
    c.setFillColor(BLUE)
    c.setFont("HB-Sans", size)
    c.drawString(x, y, label)
    width = pdfmetrics.stringWidth(label, "HB-Sans", size)
    c.linkURL(url, (x, y - 2, x + width, y + size + 1), relative=0, thickness=0)


def cover(c: canvas.Canvas) -> None:
    c.setFillColor(NAVY)
    c.rect(0, 0, PAGE_W, PAGE_H, stroke=0, fill=1)
    c.setFillColor(NAVY_2)
    c.circle(PAGE_W + 20 * mm, PAGE_H - 15 * mm, 76 * mm, stroke=0, fill=1)
    c.setFillColor(ORANGE)
    c.circle(PAGE_W - 12 * mm, PAGE_H - 28 * mm, 38 * mm, stroke=0, fill=1)
    c.setFillColor(colors.HexColor("#0E2D46"))
    c.circle(-12 * mm, 10 * mm, 58 * mm, stroke=0, fill=1)

    draw_logo(c, MARGIN_X, PAGE_H - 52 * mm, 28 * mm)
    c.setFillColor(WHITE)
    c.setFont("HB-Sans-Bold", 25)
    c.drawString(MARGIN_X, PAGE_H - 69 * mm, "HB Supplier Order")
    c.setFillColor(ORANGE)
    c.rect(MARGIN_X, PAGE_H - 76 * mm, 20 * mm, 2.2 * mm, stroke=0, fill=1)
    c.setFillColor(WHITE)
    c.setFont("HB-CJK", 21)
    c.drawString(MARGIN_X, PAGE_H - 91 * mm, "扩展安装与使用指南")
    c.setFont("HB-Sans", 16)
    c.setFillColor(colors.HexColor("#D5E0E8"))
    c.drawString(MARGIN_X, PAGE_H - 100 * mm, "Extension Installation & User Guide")
    c.setFont("HB-CJK", 10)
    c.setFillColor(colors.HexColor("#AFC2CF"))
    c.drawString(MARGIN_X, PAGE_H - 111 * mm, "桌面 Microsoft Edge  ·  iPhone / iPad Safari")
    c.setFont("HB-Sans", 8.4)
    c.drawString(MARGIN_X, PAGE_H - 118 * mm, "For authorised Hot Bargain employees and purchasing personnel")

    panel_x = MARGIN_X
    panel_y = 30 * mm
    panel_w = CONTENT_W
    panel_h = 91 * mm
    c.setFillColor(colors.HexColor("#FFFFFF"))
    c.roundRect(panel_x, panel_y, panel_w, panel_h, 13, stroke=0, fill=1)
    c.setFillColor(NAVY)
    c.setFont("HB-CJK", 11.5)
    c.drawString(panel_x + 9 * mm, panel_y + panel_h - 12 * mm, "扫码或点击进入  /  Scan or click")

    qr_size = 29 * mm
    card_gap = 5 * mm
    inner_x = panel_x + 7 * mm
    card_w = (panel_w - 14 * mm - 2 * card_gap) / 3
    cards = [
        ("桌面 Edge", "Desktop Edge", EDGE_URL),
        ("iPhone / iPad", "App Store", IOS_URL),
        ("HB SHOP", "Sign in / 登录", SHOP_URL),
    ]
    for idx, (zh, en, url) in enumerate(cards):
        x = inner_x + idx * (card_w + card_gap)
        y = panel_y + 10 * mm
        rounded_card(c, x, y, card_w, 57 * mm, fill=PAPER, stroke=LINE, radius=8)
        draw_qr(c, url, x + (card_w - qr_size) / 2, y + 20 * mm, qr_size)
        paragraph(c, f"{zh}<br/><font name='HB-Sans'>{en}</font>", x + 3 * mm, y + 17 * mm, card_w - 6 * mm, S_QR_TITLE)
        c.linkURL(url, (x, y, x + card_w, y + 57 * mm), relative=0, thickness=0)

    c.setFillColor(colors.HexColor("#AFC2CF"))
    c.setFont("HB-Sans", 7.2)
    c.drawString(MARGIN_X, 18 * mm, "Version 1.0  ·  Updated 03 September 2026  ·  Internal use")
    c.setFont("HB-CJK", 7.2)
    c.drawRightString(PAGE_W - MARGIN_X, 18 * mm, "仅限内部授权用户")


def page_two(c: canvas.Canvas) -> None:
    page_header(c, 2, "开始前", "BEFORE YOU START")
    page_footer(c, 2)
    y = PAGE_H - 27 * mm
    c.setFillColor(NAVY)
    c.setFont("HB-CJK", 17)
    c.drawString(MARGIN_X, y, "一次登录，两个浏览器入口")
    c.setFont("HB-Sans", 10.5)
    c.setFillColor(MUTED)
    c.drawString(MARGIN_X, y - 6 * mm, "One HB SHOP sign-in, then open the assistant from Edge or Safari")

    callout_y = y - 31 * mm
    rounded_card(c, MARGIN_X, callout_y, CONTENT_W, 20 * mm, fill=GREEN_PALE, stroke=colors.HexColor("#BFE4D4"), radius=10)
    c.setFillColor(GREEN)
    c.circle(MARGIN_X + 9 * mm, callout_y + 10 * mm, 4.6 * mm, stroke=0, fill=1)
    c.setFillColor(WHITE)
    c.setFont("HB-Sans-Bold", 11)
    c.drawCentredString(MARGIN_X + 9 * mm, callout_y + 8.4 * mm, "1")
    paragraph(
        c,
        "<font color='#09233A'>先在 <b>hotbargain.vip/shop</b> 登录。</font> 扩展会复用该网站会话，不会要求第二次输入用户名或密码。<br/>"
        "<font name='HB-Sans'><font color='#09233A'><b>Sign in at hotbargain.vip/shop first.</b></font> The extension reuses that website session and does not ask for a second username or password.</font>",
        MARGIN_X + 17 * mm,
        callout_y + 15.5 * mm,
        CONTENT_W - 23 * mm,
        S_NOTE,
    )
    c.linkURL(SHOP_URL, (MARGIN_X, callout_y, MARGIN_X + CONTENT_W, callout_y + 20 * mm), relative=0, thickness=0)

    flow_y = callout_y - 13 * mm
    c.setFillColor(NAVY)
    c.setFont("HB-CJK", 12.5)
    c.drawString(MARGIN_X, flow_y, "6 步快速流程  /  6-step quick flow")
    flow_y -= 8 * mm
    boxes = [
        ("下载", "Download"),
        ("启用扩展", "Enable"),
        ("登录 HB SHOP", "Sign in"),
        ("选择门店", "Select store"),
        ("允许供应商网站", "Allow website"),
        ("打开助手", "Open assistant"),
    ]
    box_gap = 4 * mm
    box_w = (CONTENT_W - 2 * box_gap) / 3
    box_h = 23 * mm
    for i, (zh, en) in enumerate(boxes):
        row, col = divmod(i, 3)
        x = MARGIN_X + col * (box_w + box_gap)
        top = flow_y - row * (box_h + 5 * mm)
        bottom = top - box_h
        rounded_card(c, x, bottom, box_w, box_h, fill=WHITE, stroke=LINE, radius=9, shadow=True)
        c.setFillColor(ORANGE if i < 3 else BLUE)
        c.circle(x + 10 * mm, bottom + box_h / 2, 5 * mm, stroke=0, fill=1)
        c.setFillColor(WHITE)
        c.setFont("HB-Sans-Bold", 9.5)
        c.drawCentredString(x + 10 * mm, bottom + box_h / 2 - 3.2, str(i + 1))
        paragraph(c, f"{zh}<br/><font name='HB-Sans' color='#5A6975'>{en}</font>", x + 19 * mm, bottom + 16.6 * mm, box_w - 23 * mm, S_CENTER)

    info_y = flow_y - 2 * box_h - 17 * mm
    gap = 6 * mm
    col_w = (CONTENT_W - gap) / 2
    rounded_card(c, MARGIN_X, info_y - 45 * mm, col_w, 45 * mm, fill=ORANGE_PALE, stroke=colors.HexColor("#F7C7B4"), radius=10)
    rounded_card(c, MARGIN_X + col_w + gap, info_y - 45 * mm, col_w, 45 * mm, fill=BLUE_PALE, stroke=colors.HexColor("#C6DDF0"), radius=10)
    paragraph(c, "准备事项 / Requirements", MARGIN_X + 6 * mm, info_y - 6 * mm, col_w - 12 * mm, S_CARD_TITLE)
    paragraph(
        c,
        "• 已获授权的 Hot Bargain 账号<br/>• 桌面 Microsoft Edge，或 iOS/iPadOS 16.4 及以上<br/>• 稳定网络连接<br/>• 有权查看的门店",
        MARGIN_X + 6 * mm,
        info_y - 14 * mm,
        col_w - 12 * mm,
        S_BODY,
    )
    paragraph(c, "Access & scope / 使用范围", MARGIN_X + col_w + gap + 6 * mm, info_y - 6 * mm, col_w - 12 * mm, S_CARD_TITLE)
    paragraph(
        c,
        "<font name='HB-Sans'>• Authorised Hot Bargain account<br/>• Desktop Edge, or iOS/iPadOS 16.4+<br/>• Select only an authorised store<br/>• Grant access only to the supplier site you are using</font>",
        MARGIN_X + col_w + gap + 6 * mm,
        info_y - 14 * mm,
        col_w - 12 * mm,
        S_BODY,
    )


def page_three(c: canvas.Canvas) -> None:
    page_header(c, 3, "桌面 Edge - 安装", "DESKTOP EDGE - INSTALL")
    page_footer(c, 3)
    top = PAGE_H - 29 * mm
    zh = [
        ("打开专用下载链接", "在 Microsoft Edge 中扫描第 1 页二维码，或点击 Edge Add-ons 专用链接。该扩展为隐藏发布，通常无法通过商店搜索找到。"),
        ("点击“获取”", "在扩展详情页选择“获取”（Get）。"),
        ("检查权限", "阅读 Edge 显示的权限说明；确认后点击“添加扩展”（Add extension）。"),
        ("确认已启用", "Edge 显示安装成功后，打开“扩展”菜单确认 HB Supplier Order 已开启。也可访问 edge://extensions 检查。"),
        ("可选：固定到工具栏", "若希望一键打开，在“扩展”菜单中把 HB Supplier Order 显示或固定到工具栏。不同 Edge 版本的文字可能略有差异。"),
    ]
    en = [
        ("Open the direct download link", "In Microsoft Edge, scan the Edge QR code on page 1 or click the dedicated Edge Add-ons link. The listing is hidden, so store search may not find it."),
        ("Select Get", "On the extension details page, select Get."),
        ("Review permissions", "Read the permissions shown by Edge, then select Add extension to continue."),
        ("Confirm it is enabled", "After Edge confirms installation, open Extensions and make sure HB Supplier Order is on. You can also check edge://extensions."),
        ("Optional: show it on the toolbar", "For one-click access, show or pin HB Supplier Order from the Extensions menu. The exact label may vary by Edge version."),
    ]
    y = bilingual_steps(c, zh, en, y_top=top, bottom=44 * mm, accent=ORANGE, compact=False)

    card_y = 20 * mm
    rounded_card(c, MARGIN_X, card_y, CONTENT_W, 21 * mm, fill=NAVY, stroke=NAVY, radius=9)
    paragraph(
        c,
        "<font color='#FFFFFF'>安装完成标志：</font> Edge 的“扩展”列表中显示 HB Supplier Order 且开关已开启。<br/>"
        "<font name='HB-Sans' color='#C9D6DF'><b>Installation check:</b> HB Supplier Order appears in Edge Extensions and its switch is on.</font>",
        MARGIN_X + 7 * mm,
        card_y + 15 * mm,
        CONTENT_W - 14 * mm,
        S_BODY_WHITE,
    )
    c.linkURL(EDGE_URL, (MARGIN_X, card_y, MARGIN_X + CONTENT_W, card_y + 21 * mm), relative=0, thickness=0)


def page_four(c: canvas.Canvas) -> None:
    page_header(c, 4, "桌面 Edge - 首次使用与日常使用", "DESKTOP EDGE - FIRST & DAILY USE")
    page_footer(c, 4)
    top = PAGE_H - 29 * mm
    zh = [
        ("登录 HB SHOP", "在 Edge 打开 hotbargain.vip/shop，使用你的授权账号登录，并等待页面完全加载。"),
        ("打开订购助手", "点击 HB SHOP 页面中的“供应商下单助手”入口，或点击工具栏/“扩展”菜单里的 HB Supplier Order。Edge 会在侧栏打开助手。"),
        ("选择门店", "选择你有权查看的门店。若门店不正确，历史记录和销售数据也会不正确。"),
        ("进入供应商商品列表", "打开受支持供应商的商品列表页。首次使用某个供应商时，按提示允许扩展访问该网站；授权后刷新页面。"),
        ("查看商品资料", "点击商品旁的 HB 按钮，或从侧栏搜索/选择商品。查看采购记录、销售历史、平均售价与供应商排名。"),
        ("下一次使用", "只要 HB SHOP 会话仍有效，可直接打开扩展。若显示未登录或会话过期，请先回到 /shop 登录，再重试。"),
    ]
    en = [
        ("Sign in to HB SHOP", "Open hotbargain.vip/shop in Edge, sign in with your authorised account, and wait for the page to finish loading."),
        ("Open the ordering assistant", "Use the Supplier Order Assistant entry on HB SHOP, or select HB Supplier Order from the toolbar/Extensions menu. Edge opens the assistant in a side panel."),
        ("Select a store", "Choose a store you are authorised to view. Purchase and sales data depends on the selected store."),
        ("Open a supplier product list", "Go to a supported supplier product-list page. The first time, allow the extension to access that website, then refresh the page."),
        ("Review item information", "Use the HB button beside an item, or find the item in the side panel. Review purchase records, sales history, average sale price, and supplier ranking."),
        ("Next time", "If the HB SHOP session is still valid, open the extension directly. If it reports that you are signed out or expired, sign in at /shop and try again."),
    ]
    bilingual_steps(c, zh, en, y_top=top, bottom=51 * mm, accent=BLUE, compact=True)

    strip_y = 20 * mm
    rounded_card(c, MARGIN_X, strip_y, CONTENT_W, 25 * mm, fill=ORANGE_PALE, stroke=colors.HexColor("#F7C7B4"), radius=9)
    paragraph(
        c,
        "<b>重要 / Important</b><br/>扩展只提供信息辅助，不会自动下单、不会修改供应商账号，也不会替你做采购决定。"
        " <font name='HB-Sans'>The extension provides decision support only. It does not place orders automatically, change supplier accounts, or make purchasing decisions.</font>",
        MARGIN_X + 7 * mm,
        strip_y + 18 * mm,
        CONTENT_W - 14 * mm,
        S_NOTE,
    )


def page_five(c: canvas.Canvas) -> None:
    page_header(c, 5, "iPhone / iPad Safari - 安装与启用", "iPHONE / iPAD SAFARI - INSTALL & ENABLE")
    page_footer(c, 5)
    top = PAGE_H - 29 * mm
    zh = [
        ("使用 App Store 专用链接", "扫描第 1 页的 iPhone/iPad 二维码，或点击专用 App Store 链接。HB Supplier Order 是未列出的 App，通常无法通过搜索找到。"),
        ("下载 App", "在 App Store 点击“获取”，完成下载。系统要求 iOS 或 iPadOS 16.4 及以上。"),
        ("打开 App 一次", "启动 HB Supplier Order，查看安装状态、支持与隐私入口。Safari 扩展会随 App 一起安装。"),
        ("进入 Safari 扩展设置", "新版系统：设置 → App → Safari → 扩展。iOS/iPadOS 16.4 或 17：设置 → Safari → 扩展。"),
        ("允许扩展", "选择 HB Supplier Order，然后开启“允许扩展”。如已建立 Safari 描述文件，请在当前使用的描述文件中也启用。"),
        ("允许当前网站", "在 Safari 打开 HB SHOP，点地址栏旁的“页面菜单” → “管理扩展”，开启 HB Supplier Order；出现网站访问提示时选择允许。"),
    ]
    en = [
        ("Use the direct App Store link", "Scan the iPhone/iPad QR code on page 1 or click the direct App Store link. HB Supplier Order is unlisted, so App Store search may not find it."),
        ("Download the app", "Select Get in the App Store and finish installing. iOS or iPadOS 16.4 or later is required."),
        ("Open the app once", "Launch HB Supplier Order to review setup status, support, and privacy links. The Safari extension is installed with the app."),
        ("Open Safari extension settings", "Newer systems: Settings > Apps > Safari > Extensions. iOS/iPadOS 16.4 or 17: Settings > Safari > Extensions."),
        ("Allow the extension", "Select HB Supplier Order and turn on Allow Extension. If you use Safari Profiles, also enable it for the profile you are currently using."),
        ("Allow the current website", "Open HB SHOP in Safari. Tap Page Menu beside the address field > Manage Extensions, turn on HB Supplier Order, and allow website access when asked."),
    ]
    bilingual_steps(c, zh, en, y_top=top, bottom=52 * mm, accent=ORANGE, compact=True)

    y = 20 * mm
    rounded_card(c, MARGIN_X, y, CONTENT_W, 25 * mm, fill=BLUE_PALE, stroke=colors.HexColor("#C6DDF0"), radius=9)
    paragraph(
        c,
        "<b>为什么不能搜索？ / Why search may not work</b><br/>Apple 的“未列出 App”只通过直接链接提供，不出现在分类、推荐、排行榜或搜索结果中。请保存本指南或内部下载链接。"
        " <font name='HB-Sans'>Apple unlisted apps are available only through a direct link and do not appear in categories, recommendations, charts, or search results.</font>",
        MARGIN_X + 7 * mm,
        y + 18 * mm,
        CONTENT_W - 14 * mm,
        S_NOTE,
    )
    c.linkURL(IOS_URL, (MARGIN_X, y, MARGIN_X + CONTENT_W, y + 25 * mm), relative=0, thickness=0)


def page_six(c: canvas.Canvas) -> None:
    page_header(c, 6, "iPhone / iPad Safari - 首次使用与日常使用", "iPHONE / iPAD SAFARI - FIRST & DAILY USE")
    page_footer(c, 6)
    top = PAGE_H - 29 * mm
    zh = [
        ("在 Safari 登录 HB SHOP", "打开 hotbargain.vip/shop，使用授权账号登录，并等待页面完全加载。请在 Safari 中完成这一步。"),
        ("打开助手", "从 HB SHOP 页面入口打开“供应商下单助手”，或点 Safari 的页面菜单/扩展图标并选择 HB Supplier Order。"),
        ("选择授权门店", "在助手中选择你有权查看的门店。Safari 会打开完整助手页面，而不是 Edge 的侧栏。"),
        ("访问供应商网站", "打开受支持供应商的商品列表页。首次访问该网站时，允许 HB Supplier Order 读取和修改当前网页，然后刷新页面。"),
        ("打开商品或完整助手", "点网页中的 HB 商品按钮，或点 Safari 工具栏里的 HB 扩展。使用采购记录、销售记录、平均售价和供应商排名辅助订货。"),
        ("下次快速进入", "网站会话有效时可直接使用。若助手提示未登录、未启用或权限不足，请按第 7 页排查。"),
    ]
    en = [
        ("Sign in to HB SHOP in Safari", "Open hotbargain.vip/shop, sign in with your authorised account, and wait for the page to finish loading. Complete this step in Safari."),
        ("Open the assistant", "Use the Supplier Order Assistant entry on HB SHOP, or open Safari Page Menu/Extensions and select HB Supplier Order."),
        ("Select an authorised store", "Choose a store you are allowed to view. Safari opens the full assistant page rather than the Edge side panel."),
        ("Visit the supplier website", "Open a supported supplier product-list page. The first time, allow HB Supplier Order to read and modify the current website, then refresh the page."),
        ("Open an item or the full assistant", "Use the HB button on the page, or select the HB extension from Safari. Review purchase records, sales records, average sale price, and supplier ranking."),
        ("Fast access next time", "If your website session is valid, open the extension directly. If it says signed out, disabled, or denied, use the checks on page 7."),
    ]
    bilingual_steps(c, zh, en, y_top=top, bottom=49 * mm, accent=BLUE, compact=True)

    y = 19 * mm
    rounded_card(c, MARGIN_X, y, CONTENT_W, 29 * mm, fill=NAVY, stroke=NAVY, radius=9)
    paragraph(
        c,
        "<font color='#FFFFFF'><b>Safari 与 Edge 的界面差异：</b> Safari 打开完整助手页面；Edge 打开浏览器侧栏。核心数据、门店选择和网站权限流程一致。</font><br/>"
        "<font name='HB-Sans' color='#C9D6DF'><b>Interface difference:</b> Safari opens a full assistant page; Edge opens a browser side panel. The core data, store selection, and site-permission flow are the same.</font>",
        MARGIN_X + 7 * mm,
        y + 22 * mm,
        CONTENT_W - 14 * mm,
        S_BODY_WHITE,
    )


def trouble_row(
    c: canvas.Canvas,
    x: float,
    y_top: float,
    width: float,
    title: str,
    solution: str,
    *,
    accent=ORANGE,
) -> float:
    c.setFillColor(accent)
    c.circle(x + 4.3, y_top - 4.3, 4.3, stroke=0, fill=1)
    c.setStrokeColor(WHITE)
    c.setLineWidth(1.4)
    c.line(x + 2.4, y_top - 4.3, x + 6.2, y_top - 4.3)
    c.line(x + 4.3, y_top - 2.4, x + 4.3, y_top - 6.2)
    title_h = paragraph(c, title, x + 13, y_top + 1, width - 13, S_STEP_TITLE)
    body_h = paragraph(c, solution, x + 13, y_top - title_h - 2, width - 13, S_BODY_SMALL)
    return title_h + body_h + 5


def page_seven(c: canvas.Canvas) -> None:
    page_header(c, 7, "快速排查、安全与支持", "TROUBLESHOOTING, SAFETY & SUPPORT")
    page_footer(c, 7)
    top = PAGE_H - 29 * mm
    gap = 9 * mm
    col_w = (CONTENT_W - gap) / 2
    x_l = MARGIN_X
    x_r = MARGIN_X + col_w + gap
    language_label(c, x_l, top + 3, "中文", ORANGE)
    language_label(c, x_r, top + 3, "ENGLISH", ORANGE)
    y_l = top - 22
    y_r = top - 22
    zh = [
        ("找不到 iOS App", "不要在 App Store 搜索。使用第 1 页二维码或直接链接，因为这是未列出的 App。"),
        ("Edge 仍显示“安装”", "刷新 HB SHOP；在 edge://extensions 确认扩展已开启，再从“扩展”菜单打开。"),
        ("Safari 找不到扩展", "确认 App 已安装；在 Safari 扩展设置中开启；若使用 Safari 描述文件，检查当前描述文件。"),
        ("助手提示未登录", "回到 hotbargain.vip/shop 登录并等待页面加载，然后再次打开助手。不要在扩展里重复输入密码。"),
        ("商品页没有 HB 按钮", "确认这是受支持的供应商列表页；允许该网站访问权限，并在授权后刷新页面。"),
        ("数据为空或门店不对", "重新选择授权门店，确认商品编号正确；仍有问题时联系支持。"),
    ]
    en = [
        ("Cannot find the iOS app", "Do not rely on App Store search. Use the page 1 QR code or direct link because the app is unlisted."),
        ("Edge still shows Install", "Refresh HB SHOP, confirm the extension is on at edge://extensions, then open it from the Extensions menu."),
        ("Safari extension is missing", "Confirm the app is installed and Allow Extension is on. If you use Safari Profiles, check the active profile."),
        ("The assistant says signed out", "Return to hotbargain.vip/shop, sign in, wait for the page to load, then reopen the assistant. Do not re-enter the password in the extension."),
        ("No HB button on a product page", "Make sure it is a supported supplier product-list page, allow website access, and refresh after permission is granted."),
        ("No data or wrong store", "Select the authorised store again and confirm the item number. Contact support if the issue continues."),
    ]
    for z, e in zip(zh, en):
        hz = trouble_row(c, x_l, y_l, col_w, z[0], z[1], accent=ORANGE)
        he = trouble_row(c, x_r, y_r, col_w, e[0], e[1], accent=ORANGE)
        h = max(hz, he)
        y_l -= h + 10
        y_r -= h + 10

    support_y = min(y_l, y_r) - 5
    rounded_card(c, MARGIN_X, support_y - 31 * mm, CONTENT_W, 31 * mm, fill=NAVY, stroke=NAVY, radius=10)
    paragraph(
        c,
        "<font color='#FFFFFF'><b>安全 / Safety</b></font><br/>"
        "<font color='#C9D6DF'>仅限授权员工使用。不要共享账号或密码。扩展不会自动下单或修改供应商账号。只为当前使用的供应商网站授予访问权限。</font><br/>"
        "<font name='HB-Sans' color='#C9D6DF'>Authorised staff only. Never share account credentials. The extension does not place orders automatically or change supplier accounts. Grant access only to the supplier site you are using.</font>",
        MARGIN_X + 7 * mm,
        support_y - 7 * mm,
        CONTENT_W - 14 * mm,
        S_BODY_WHITE,
    )

    source_top = support_y - 38 * mm
    c.setFillColor(NAVY)
    c.setFont("HB-CJK", 10.5)
    c.drawString(MARGIN_X, source_top, "支持与参考  /  Support & references")
    source_top -= 5.5 * mm
    link_text(c, "HB support: hotbargain.vip/support/hb-supplier-order", SUPPORT_URL, MARGIN_X, source_top)
    source_top -= 4.6 * mm
    link_text(c, "Microsoft Support: Add, turn off, or remove extensions in Microsoft Edge", MICROSOFT_SOURCE, MARGIN_X, source_top)
    source_top -= 4.6 * mm
    link_text(c, "Apple Support: Get extensions to customize Safari on iPhone", APPLE_SOURCE, MARGIN_X, source_top)
    source_top -= 4.6 * mm
    link_text(c, "Apple Developer: Unlisted App Distribution", APPLE_UNLISTED_SOURCE, MARGIN_X, source_top)


def build() -> None:
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    c = canvas.Canvas(str(OUTPUT), pagesize=A4, pageCompression=1)
    c.setTitle("HB Supplier Order Extension Installation and User Guide - Chinese and English")
    c.setAuthor("Hot Bargain")
    c.setSubject("Desktop Microsoft Edge and iPhone/iPad Safari extension installation and usage")
    c.setKeywords("HB Supplier Order, Microsoft Edge, Safari, extension, installation, user guide")
    for page_fn in (cover, page_two, page_three, page_four, page_five, page_six, page_seven):
        page_fn(c)
        c.showPage()
    c.save()
    print(OUTPUT)


if __name__ == "__main__":
    build()
