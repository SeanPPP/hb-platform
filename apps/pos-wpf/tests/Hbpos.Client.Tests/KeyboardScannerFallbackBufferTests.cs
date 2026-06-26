using System.Windows.Input;
using Hbpos.Client.Wpf;

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
}
