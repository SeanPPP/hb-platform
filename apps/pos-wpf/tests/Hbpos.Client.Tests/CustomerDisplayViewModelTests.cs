using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.ViewModels;
using Hbpos.Client.Wpf.Views.Screens;
using Hbpos.Contracts.Advertisements;

namespace Hbpos.Client.Tests;

public sealed class CustomerDisplayViewModelTests
{
    [Fact]
    public void LoadLines_calculates_item_quantity_and_sku_count()
    {
        var viewModel = new CustomerDisplayViewModel();

        viewModel.LoadLines(
            [
                new CustomerDisplayLine("Milk", "SKU-001", 2m, 3m, 6m),
                new CustomerDisplayLine("Bread", "SKU-002", 1.5m, 4m, 6m)
            ],
            subtotal: 35.94m,
            savingsAmount: 3.96m);

        Assert.Equal(3.5m, viewModel.TotalItemQuantity);
        Assert.Equal(2, viewModel.SkuCount);
        Assert.Equal(31.98m, viewModel.TotalToPay);
        Assert.Equal(2.91m, viewModel.TaxAmount);
    }

    [Fact]
    public void CustomerDisplayView_keeps_promotion_on_right_when_cart_has_lines()
    {
        var (_, codeBehind) = ReadCustomerDisplayViewFiles();

        Assert.DoesNotContain("UsesCompactPromotionLayout", codeBehind);
        Assert.DoesNotContain("Grid.SetColumnSpan(CartPanel, 2)", codeBehind);
        Assert.DoesNotContain("PromotionBannerRow.Height = new GridLength(154)", codeBehind);
    }

    [Fact]
    public void CustomerDisplayView_scales_advertisement_media_inside_promotion_panel()
    {
        var (xaml, _) = ReadCustomerDisplayViewFiles();

        Assert.Equal(2, xaml.Split("Stretch=\"Uniform\"", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("Stretch=\"UniformToFill\"", xaml);
    }

    [Fact]
    public void CustomerDisplayView_hides_advertisement_title_when_media_is_available()
    {
        var (_, codeBehind) = ReadCustomerDisplayViewFiles();

        Assert.Contains("PromotionSubtitleText.Visibility = Visibility.Collapsed;", codeBehind);
        Assert.DoesNotContain(
            "PromotionSubtitleText.Visibility = hasAdvertisement ? Visibility.Visible : Visibility.Collapsed;",
            codeBehind);
    }

    [Fact]
    public void CustomerDisplayView_hides_promotion_badge_when_advertisement_media_is_available()
    {
        var (xaml, codeBehind) = ReadCustomerDisplayViewFiles();

        Assert.Contains("Text=\"{loc:Loc customer.promotionTitle}\"", xaml);
        Assert.Contains(
            "PromotionTextPanel.Visibility = hasAdvertisement ? Visibility.Collapsed : Visibility.Visible;",
            codeBehind);
    }

    [Fact]
    public void CustomerDisplayView_hides_fallback_background_when_advertisement_media_is_available()
    {
        var (xaml, codeBehind) = ReadCustomerDisplayViewFiles();
        var fallbackBackgroundIndex = xaml.IndexOf("x:Name=\"PromotionFallbackBackground\"", StringComparison.Ordinal);
        var imageIndex = xaml.IndexOf("x:Name=\"AdvertisementImage\"", StringComparison.Ordinal);
        var videoIndex = xaml.IndexOf("x:Name=\"AdvertisementVideo\"", StringComparison.Ordinal);
        var dimOverlayIndex = xaml.IndexOf("Opacity=\"0.18\" Fill=\"#FF000000\"", StringComparison.Ordinal);

        Assert.Contains("x:Name=\"PromotionFallbackBackground\"", xaml);
        Assert.Contains("PromotionFallbackBackground.Visibility = hasAdvertisement ? Visibility.Collapsed : Visibility.Visible;", codeBehind);
        Assert.True(fallbackBackgroundIndex < imageIndex);
        Assert.True(fallbackBackgroundIndex < videoIndex);
        Assert.True(dimOverlayIndex > imageIndex);
        Assert.True(dimOverlayIndex > videoIndex);
    }

    [Fact]
    public void LoadAdvertisements_filters_unplayable_items_and_marks_idle_visible_when_cart_is_empty()
    {
        var viewModel = new CustomerDisplayViewModel();

        viewModel.LoadAdvertisements(
            [
                CreateAdvertisement("ad-image", "image", "https://cdn.example.com/ad-image.png"),
                CreateAdvertisement("ad-video", "video", "https://cdn.example.com/ad-video.mp4"),
                CreateAdvertisement("ad-empty", "image", string.Empty),
                CreateAdvertisement("ad-audio", "audio", "https://cdn.example.com/ad-audio.mp3")
            ]);

        Assert.True(viewModel.IsAdvertisementAvailable);
        Assert.True(viewModel.IsIdleAdvertisementVisible);
        Assert.Equal("ad-image", viewModel.CurrentAdvertisement?.Id);
    }

    [Fact]
    public void LoadAdvertisements_filters_expired_items()
    {
        var now = new DateTimeOffset(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);
        var viewModel = new CustomerDisplayViewModel { UtcNow = () => now };

        viewModel.LoadAdvertisements(
            [
                CreateAdvertisement("ad-expired", "image", "https://cdn.example.com/ad-expired.png", now.AddMinutes(-10), now.AddMinutes(-1)),
                CreateAdvertisement("ad-active", "image", "https://cdn.example.com/ad-active.png", now.AddMinutes(-1), now.AddMinutes(10))
            ]);

        Assert.True(viewModel.IsAdvertisementAvailable);
        Assert.Equal("ad-active", viewModel.CurrentAdvertisement?.Id);
    }

    [Fact]
    public void AdvanceAdvertisement_removes_expired_items_before_selecting_next_advertisement()
    {
        var now = new DateTimeOffset(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);
        var viewModel = new CustomerDisplayViewModel { UtcNow = () => now };
        viewModel.LoadAdvertisements(
            [
                CreateAdvertisement("ad-first", "image", "https://cdn.example.com/ad-first.png", now.AddMinutes(-1), now.AddMinutes(1)),
                CreateAdvertisement("ad-second", "image", "https://cdn.example.com/ad-second.png", now.AddMinutes(-1), now.AddMinutes(1))
            ]);
        now = now.AddMinutes(2);

        viewModel.AdvanceAdvertisement();

        Assert.False(viewModel.IsAdvertisementAvailable);
        Assert.Null(viewModel.CurrentAdvertisement);
    }

    [Fact]
    public void AdvanceAdvertisement_skips_expired_item_and_keeps_active_item()
    {
        var now = new DateTimeOffset(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);
        var viewModel = new CustomerDisplayViewModel { UtcNow = () => now };
        viewModel.LoadAdvertisements(
            [
                CreateAdvertisement("ad-current", "image", "https://cdn.example.com/ad-current.png", now.AddMinutes(-1), now.AddMinutes(1)),
                CreateAdvertisement("ad-expired-next", "image", "https://cdn.example.com/ad-expired-next.png", now.AddMinutes(-1), now.AddMinutes(1)),
                CreateAdvertisement("ad-active-next", "image", "https://cdn.example.com/ad-active-next.png", now.AddMinutes(-1), now.AddMinutes(10))
            ]);
        now = now.AddMinutes(2);

        viewModel.AdvanceAdvertisement();

        Assert.True(viewModel.IsAdvertisementAvailable);
        Assert.Equal("ad-active-next", viewModel.CurrentAdvertisement?.Id);
    }

    [Fact]
    public void AdvanceAdvertisement_with_single_item_raises_change_notifications_for_restart()
    {
        var viewModel = new CustomerDisplayViewModel();
        var changedProperties = new List<string>();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.PropertyName))
            {
                changedProperties.Add(e.PropertyName);
            }
        };
        viewModel.LoadAdvertisements([CreateAdvertisement("ad-image", "image", "https://cdn.example.com/ad-image.png")]);
        changedProperties.Clear();

        viewModel.AdvanceAdvertisement();

        Assert.Equal("ad-image", viewModel.CurrentAdvertisement?.Id);
        Assert.Equal(2, changedProperties.Count(name => name == nameof(CustomerDisplayViewModel.CurrentAdvertisement)));
    }

    [Fact]
    public void SkipCurrentAdvertisement_removes_failed_item_and_falls_back_when_last_item_is_skipped()
    {
        var viewModel = new CustomerDisplayViewModel();
        viewModel.LoadAdvertisements([CreateAdvertisement("ad-image", "image", "https://cdn.example.com/ad-image.png")]);

        viewModel.SkipCurrentAdvertisement();

        Assert.False(viewModel.IsAdvertisementAvailable);
        Assert.Null(viewModel.CurrentAdvertisement);
        Assert.False(viewModel.IsIdleAdvertisementVisible);
    }

    private static AdvertisementPlaybackItemDto CreateAdvertisement(string id, string mediaType, string mediaUrl)
    {
        var now = DateTimeOffset.UtcNow;
        return CreateAdvertisement(id, mediaType, mediaUrl, now.AddMinutes(-5), now.AddMinutes(5));
    }

    private static AdvertisementPlaybackItemDto CreateAdvertisement(
        string id,
        string mediaType,
        string mediaUrl,
        DateTimeOffset effectiveStart,
        DateTimeOffset effectiveEnd)
    {
        return new AdvertisementPlaybackItemDto(
            id,
            $"Ad {id}",
            $"Description {id}",
            mediaType,
            mediaUrl,
            null,
            $"object/{id}",
            $"{id}.dat",
            "application/octet-stream",
            1024,
            effectiveStart,
            effectiveEnd,
            1);
    }

    private static string FindRepoRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var current = new DirectoryInfo(start);
            while (current is not null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, ".git")) ||
                    File.Exists(Path.Combine(current.FullName, ".git")) ||
                    File.Exists(Path.Combine(current.FullName, "hb-platform.sln")) ||
                    File.Exists(Path.Combine(current.FullName, "hb-platform.slnx")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }

        throw new DirectoryNotFoundException("Unable to find repository root.");
    }

    private static (string Xaml, string CodeBehind) ReadCustomerDisplayViewFiles()
    {
        var viewPath = Path.Combine(
            FindRepoRoot(),
            "apps",
            "pos-wpf",
            "src",
            "Hbpos.Client.Wpf",
            "Views",
            "Screens",
            "CustomerDisplayView.xaml");

        return (File.ReadAllText(viewPath), File.ReadAllText(viewPath + ".cs"));
    }
}
