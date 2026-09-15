using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hbpos.Client.Wpf.Localization;
using Hbpos.Client.Wpf.ViewModels;
using Hbpos.Client.Wpf.Views.Screens;

namespace Hbpos.Client.Tests;

[Collection(WpfViewLifecycleTestCollection.Name)]
public sealed class ManualCardPaymentViewRuntimeTests(PaymentViewRuntimeStaTestHost host)
{
    [Theory]
    [InlineData(1366, 688, "en-US")]
    [InlineData(1366, 688, "zh-CN")]
    [InlineData(1024, 768, "en-US")]
    [InlineData(1024, 768, "zh-CN")]
    public Task Manual_confirmation_is_visible_and_requires_explicit_success(int width, int height, string culture) =>
        host.RunAsync(async _ =>
        {
            await using var fixture = await PaymentFlowTestFixture.CreateAsync(new ScriptedLinklyCloudApi(false));
            var localization = new LocalizationService();
            localization.SetCulture(culture);
            LocalizationResourceProvider.Instance.Configure(localization);
            using var vm = new PaymentViewModel(fixture.CreateSaleCart(10.03m), fixture.Workflow, fixture.Session, localization,
                paymentMethodSettingsService: new MutablePaymentMethodSettingsService(new(UseManualCard: true)));
            var view = new PaymentView { DataContext = vm };
            // 与 App.xaml 使用同一配色；共享离屏宿主只加载控件模板，缺少主题画刷会使勾选图标透明。
            view.Resources.MergedDictionaries.Add(new MaterialDesignThemes.Wpf.BundledTheme
            {
                BaseTheme = MaterialDesignThemes.Wpf.BaseTheme.Light,
                PrimaryColor = MaterialDesignColors.PrimaryColor.Blue,
                SecondaryColor = MaterialDesignColors.SecondaryColor.Amber
            });
            try
            {
                PaymentViewRuntimeStaTestHost.Realize(view, width, height);
                var entry = Find<Button>(view, "ManualCardPaymentButton");
                AssertInBounds(view, entry, width, height);
                Assert.True(entry.IsEnabled);
                entry.Command.Execute(null);
                PaymentViewRuntimeStaTestHost.Realize(view, width, height);
                var confirm = Find<Button>(view, "ManualCardConfirmButton");
                var cancel = Find<Button>(view, "ManualCardCancelButton");
                var check = Find<CheckBox>(view, "ManualCardSuccessCheckBox");
                AssertInBounds(view, confirm, width, height);
                AssertInBounds(view, cancel, width, height);
                AssertInBounds(view, check, width, height);
                Assert.False(entry.IsEnabled);
                Assert.False(confirm.IsEnabled);
                check.IsChecked = true;
                PaymentViewRuntimeStaTestHost.Realize(view, width, height);
                Assert.True(confirm.IsEnabled);
                var confirmText = Assert.IsType<TextBlock>(confirm.Content);
                Assert.Equal(localization.T("payment.manualCard.confirm"), confirmText.Text);
                Assert.Equal(Colors.White, Assert.IsType<SolidColorBrush>(confirmText.Foreground).Color);

                // 设置输出目录时保留真实 WPF 渲染，便于人工检查字体、触摸按钮与金额层级。
                var output = Environment.GetEnvironmentVariable("HBPOS_MANUAL_CARD_SCREENSHOTS");
                if (!string.IsNullOrWhiteSpace(output))
                {
                    Directory.CreateDirectory(output);
                    var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(view);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(output, $"manual-card-{width}-{height}-{culture}.png"));
                    encoder.Save(stream);
                }
                cancel.Command.Execute(null);
                Assert.Empty(await fixture.OrderRepository.GetRecentOrdersAsync());
                Assert.Equal(0, fixture.CloudApi.SendCount);
            }
            finally
            {
                view.DataContext = null;
                localization.SetCulture("en-US");
            }
        });

    private static T Find<T>(DependencyObject view, string id) where T : FrameworkElement =>
        Assert.Single(PaymentViewRuntimeStaTestHost.FindVisualDescendants<T>(view)
            .Where(element => AutomationProperties.GetAutomationId(element) == id));

    private static void AssertInBounds(FrameworkElement view, FrameworkElement element, int width, int height)
    {
        // 无桌面宿主的离屏测试以祖先 Visibility 判断，不能使用依赖 PresentationSource 的 IsVisible。
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is UIElement ui) Assert.Equal(Visibility.Visible, ui.Visibility);
        }
        var bounds = element.TransformToAncestor(view).TransformBounds(new Rect(element.RenderSize));
        Assert.True(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= width + 1 && bounds.Bottom <= height + 1,
            $"{AutomationProperties.GetAutomationId(element)} outside viewport: {bounds}");
        Assert.True(element.ActualHeight >= 48);
    }
}
