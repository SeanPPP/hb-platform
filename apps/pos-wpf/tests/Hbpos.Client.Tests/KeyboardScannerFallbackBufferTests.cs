using System.Windows.Input;
using Hbpos.Client.Wpf;
using Hbpos.Client.Wpf.Services;

namespace Hbpos.Client.Tests;

public sealed class KeyboardScannerFallbackBufferTests
{
    [Fact]
    public void Process_CompletesFastScannerInputOnEnter()
    {
        var buffer = new KeyboardScannerFallbackBuffer();
        var now = DateTimeOffset.UtcNow;

        Assert.Null(buffer.Process(Key.D9, now));
        Assert.Null(buffer.Process(Key.D3, now.AddMilliseconds(10)));
        Assert.Null(buffer.Process(Key.D0, now.AddMilliseconds(20)));
        var barcode = buffer.Process(Key.Enter, now.AddMilliseconds(30));

        Assert.Equal("930", barcode);
    }

    [Fact]
    public void Process_DropsSlowInputBeforeEnter()
    {
        var buffer = new KeyboardScannerFallbackBuffer();
        var now = DateTimeOffset.UtcNow;

        Assert.Null(buffer.Process(Key.D9, now));
        Assert.Null(buffer.Process(Key.D3, now.AddMilliseconds(150)));
        var barcode = buffer.Process(Key.Enter, now.AddMilliseconds(160));

        Assert.Null(barcode);
    }

    [Fact]
    public void Process_UsesImeProcessedKeyForScannerCharacters()
    {
        var buffer = new KeyboardScannerFallbackBuffer();
        var now = DateTimeOffset.UtcNow;

        Assert.Null(buffer.Process(Key.ImeProcessed, now, Key.A));
        Assert.Null(buffer.Process(Key.ImeProcessed, now.AddMilliseconds(10), Key.B));
        Assert.Null(buffer.Process(Key.ImeProcessed, now.AddMilliseconds(20), Key.C));
        var barcode = buffer.Process(Key.Enter, now.AddMilliseconds(30));

        Assert.Equal("ABC", barcode);
    }

    [Fact]
    public void DuplicateGuard_suppresses_same_barcode_from_different_input_sources()
    {
        var guard = new ScannerInputDuplicateGuard();
        var now = DateTimeOffset.UtcNow;

        Assert.True(guard.TryAccept("ABC123", "raw", now));
        Assert.False(guard.TryAccept("ABC123", "keyboard-fallback", now.AddMilliseconds(10)));
    }

    [Fact]
    public void DuplicateGuard_allows_repeated_scans_from_same_source_and_delayed_source_changes()
    {
        var guard = new ScannerInputDuplicateGuard();
        var now = DateTimeOffset.UtcNow;

        Assert.True(guard.TryAccept("ABC123", "raw", now));
        Assert.True(guard.TryAccept("ABC123", "raw", now.AddMilliseconds(10)));
        Assert.True(guard.TryAccept("ABC123", "keyboard-fallback", now.AddMilliseconds(250)));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void ShouldBlockKeyboardScannerFallback_blocks_only_visible_text_input_focus(
        bool isTextInputFocused,
        bool isFocusedElementVisible,
        bool expected)
    {
        Assert.Equal(
            expected,
            MainWindow.ShouldBlockKeyboardScannerFallback(isTextInputFocused, isFocusedElementVisible));
    }

    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(false, false, false, false)]
    public void ShouldBlockKeyboardScannerFallback_blocks_when_force_update_overlay_is_active(
        bool isForceUpdateBlocking,
        bool isTextInputFocused,
        bool isFocusedElementVisible,
        bool expected)
    {
        Assert.Equal(
            expected,
            MainWindow.ShouldBlockKeyboardScannerFallback(
                isForceUpdateBlocking,
                isTextInputFocused,
                isFocusedElementVisible));
    }

    [Theory]
    [InlineData(true, 0x00FF, true)]
    [InlineData(true, 0x0100, false)]
    [InlineData(false, 0x00FF, false)]
    public void ShouldBlockKeyboardScannerFallback_blocks_raw_scanner_window_message_when_force_update_overlay_is_active(
        bool isForceUpdateBlocking,
        int messageId,
        bool expected)
    {
        Assert.Equal(
            expected,
            MainWindow.ShouldBlockRawScannerWindowMessage(isForceUpdateBlocking, messageId));
    }

    [Fact]
    public void ProcessRawScannerWindowMessage_marks_handled_and_skips_dispatch_when_force_update_overlay_is_active()
    {
        var handled = false;
        var dispatchCalled = false;

        var result = MainWindow.ProcessRawScannerWindowMessage(
            isForceUpdateBlocking: true,
            messageId: 0x00FF,
            hwnd: IntPtr.Zero,
            wParam: IntPtr.Zero,
            lParam: IntPtr.Zero,
            processWindowMessage: (IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool nextHandled) =>
            {
                dispatchCalled = true;
                return new IntPtr(42);
            },
            handled: ref handled);

        Assert.Equal(IntPtr.Zero, result);
        Assert.True(handled);
        Assert.False(dispatchCalled);
    }

    [Fact]
    public void ProcessRawScannerWindowMessage_dispatches_non_blocked_messages_to_raw_scanner()
    {
        var handled = false;
        var dispatchCalled = false;

        var result = MainWindow.ProcessRawScannerWindowMessage(
            isForceUpdateBlocking: false,
            messageId: 0x00FF,
            hwnd: IntPtr.Zero,
            wParam: IntPtr.Zero,
            lParam: IntPtr.Zero,
            processWindowMessage: (IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool nextHandled) =>
            {
                dispatchCalled = true;
                nextHandled = true;
                return new IntPtr(42);
            },
            handled: ref handled);

        Assert.Equal(new IntPtr(42), result);
        Assert.True(handled);
        Assert.True(dispatchCalled);
    }

    [Theory]
    [InlineData("", "1", "1")]
    [InlineData("12", "3", "123")]
    [InlineData("123", "Back", "12")]
    [InlineData("", "Back", "")]
    [InlineData("123", "Clear", "")]
    [InlineData("123", "A", "123")]
    [InlineData("123", null, "123")]
    public void ApplyCashierBarcodeKeyboardInput_updates_cashier_barcode_buffer(
        string current,
        string? key,
        string expected)
    {
        Assert.Equal(expected, MainWindow.ApplyCashierBarcodeKeyboardInput(current, key));
    }

    [Fact]
    public void VoucherEntryTextBox_disables_ime_for_scanner_input()
    {
        var xamlPath = Path.Combine(
            FindRepoRoot(),
            "apps",
            "pos-wpf",
            "src",
            "Hbpos.Client.Wpf",
            "Views",
            "Screens",
            "PaymentView.xaml");
        var xaml = File.ReadAllText(xamlPath);
        var textBoxStart = xaml.IndexOf("<TextBox x:Name=\"VoucherEntryTextBox\"", StringComparison.Ordinal);

        Assert.True(textBoxStart >= 0);
        var textBoxEnd = xaml.IndexOf("/>", textBoxStart, StringComparison.Ordinal);
        Assert.True(textBoxEnd > textBoxStart);
        var textBoxMarkup = xaml[textBoxStart..textBoxEnd];
        Assert.Contains("InputMethod.IsInputMethodEnabled=\"False\"", textBoxMarkup, StringComparison.Ordinal);
        Assert.Contains("InputMethod.PreferredImeState=\"Off\"", textBoxMarkup, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")) ||
                File.Exists(Path.Combine(current.FullName, ".git")) ||
                File.Exists(Path.Combine(current.FullName, "hb-platform.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to find repository root.");
    }
}
