using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using Hbpos.Client.Wpf.Services;
using Hbpos.Client.Wpf.ViewModels;
using Hbpos.Client.Wpf.Views.Screens;

namespace Hbpos.Client.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WpfViewLifecycleTestCollection
{
    public const string Name = nameof(WpfViewLifecycleTestCollection);
}

[Collection(WpfViewLifecycleTestCollection.Name)]
public sealed class WpfViewLifecycleTests
{
    [Fact]
    public async Task Views_subscribe_only_while_loaded_and_loaded_cycles_are_idempotent()
    {
        await RunOnStaDispatcherAsync(() =>
        {
            var application = CreateTestApplication();
            try
            {
                VerifySettingsViewLifecycle();
                VerifyTransactionHistoryViewLifecycle();
                VerifyPaymentViewLifecycle();
                VerifyUnloadedViewInstancesAreCollectible();
            }
            finally
            {
                application.Shutdown();
            }
        });
    }

    private static void VerifySettingsViewLifecycle()
    {
        using var first = CreateSettingsViewModel();
        using var second = CreateSettingsViewModel();
        var view = new SettingsView { DataContext = first };

        AssertDirectHandlerCount(first, view, "ViewModel_PropertyChanged", 0);
        RaiseLoaded(view);
        AssertDirectHandlerCount(first, view, "ViewModel_PropertyChanged", 1);
        RaiseLoaded(view);
        AssertDirectHandlerCount(first, view, "ViewModel_PropertyChanged", 1);

        view.DataContext = second;
        AssertDirectHandlerCount(first, view, "ViewModel_PropertyChanged", 0);
        AssertDirectHandlerCount(second, view, "ViewModel_PropertyChanged", 1);

        RaiseUnloaded(view);
        AssertDirectHandlerCount(second, view, "ViewModel_PropertyChanged", 0);
        Assert.Null(ReadPrivateField(view, "_viewModel"));
        RaiseUnloaded(view);
        AssertDirectHandlerCount(second, view, "ViewModel_PropertyChanged", 0);

        RaiseLoaded(view);
        AssertDirectHandlerCount(second, view, "ViewModel_PropertyChanged", 1);
        RaiseUnloaded(view);
        view.DataContext = null;
    }

    private static void VerifyTransactionHistoryViewLifecycle()
    {
        using var first = new TransactionHistoryViewModel();
        using var second = new TransactionHistoryViewModel();
        var view = new TransactionHistoryView { DataContext = first };

        AssertDirectHandlerCount(first, view, "ViewModelPropertyChanged", 0);
        RaiseLoaded(view);
        AssertDirectHandlerCount(first, view, "ViewModelPropertyChanged", 1);
        RaiseLoaded(view);
        AssertDirectHandlerCount(first, view, "ViewModelPropertyChanged", 1);

        first.IsHeldSourceSelected = true;
        var generationBeforeShown = Assert.IsType<long>(ReadPrivateField(first, "_heldLoadGeneration"));
        InvokePrivateMethod(view, "ShowAttachedViewModel");
        var generationAfterFirstShown = Assert.IsType<long>(ReadPrivateField(first, "_heldLoadGeneration"));
        InvokePrivateMethod(view, "ShowAttachedViewModel");
        Assert.Equal(generationBeforeShown + 1, generationAfterFirstShown);
        Assert.Equal(generationAfterFirstShown, Assert.IsType<long>(ReadPrivateField(first, "_heldLoadGeneration")));

        view.DataContext = second;
        AssertDirectHandlerCount(first, view, "ViewModelPropertyChanged", 0);
        AssertDirectHandlerCount(second, view, "ViewModelPropertyChanged", 1);
        Assert.False(Assert.IsType<bool>(ReadPrivateField(first, "_isScreenVisible")));

        InvokePrivateMethod(view, "ShowAttachedViewModel");
        Assert.True(Assert.IsType<bool>(ReadPrivateField(second, "_isScreenVisible")));

        RaiseUnloaded(view);
        AssertDirectHandlerCount(second, view, "ViewModelPropertyChanged", 0);
        Assert.Null(ReadPrivateField(view, "_viewModel"));
        Assert.False(Assert.IsType<bool>(ReadPrivateField(view, "_isScreenShown")));
        Assert.False(Assert.IsType<bool>(ReadPrivateField(second, "_isScreenVisible")));
        RaiseUnloaded(view);
        AssertDirectHandlerCount(second, view, "ViewModelPropertyChanged", 0);

        RaiseLoaded(view);
        AssertDirectHandlerCount(second, view, "ViewModelPropertyChanged", 1);
        RaiseUnloaded(view);
        view.DataContext = null;
    }

    private static void VerifyPaymentViewLifecycle()
    {
        var second = new CountingPropertyChangedSource();
        var view = new PaymentView();
        var releasedViewModel = LoadAndReplacePaymentViewModel(view, second);
        AssertDirectHandlerCount(second, view, "PaymentViewModelPropertyChanged", 1);
        Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
        ForceFullCollection();
        Assert.False(releasedViewModel.IsAlive, "切换 DataContext 后旧 Payment VM 仍被视图持有。");

        RaiseUnloaded(view);
        AssertDirectHandlerCount(second, view, "PaymentViewModelPropertyChanged", 0);
        Assert.Null(ReadPrivateField(view, "_viewModelNotifications"));
        RaiseUnloaded(view);
        AssertDirectHandlerCount(second, view, "PaymentViewModelPropertyChanged", 0);

        RaiseLoaded(view);
        AssertDirectHandlerCount(second, view, "PaymentViewModelPropertyChanged", 1);
        RaiseUnloaded(view);
        view.DataContext = null;
    }

    private static void VerifyUnloadedViewInstancesAreCollectible()
    {
        using var settingsViewModel = CreateSettingsViewModel();
        using var historyViewModel = new TransactionHistoryViewModel();
        var paymentViewModel = new CountingPropertyChangedSource();

        var settingsView = CreateReleasedSettingsView(settingsViewModel);
        var historyView = CreateReleasedHistoryView(historyViewModel);
        var paymentView = CreateReleasedPaymentView(paymentViewModel);

        Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
        ForceFullCollection();

        Assert.False(settingsView.IsAlive, "SettingsView 在 Loaded/Unloaded 循环后仍被 VM 强事件持有。");
        Assert.False(historyView.IsAlive, "TransactionHistoryView 在 Loaded/Unloaded 循环后仍被 VM 强事件持有。");
        Assert.False(paymentView.IsAlive, "PaymentView 在 Loaded/Unloaded 循环后仍被 VM 强事件持有。");

        // 保证三个 VM 在弱引用断言期间仍是 GC 根，避免把 VM 与 View 一起回收造成假阳性。
        GC.KeepAlive(settingsViewModel);
        GC.KeepAlive(historyViewModel);
        GC.KeepAlive(paymentViewModel);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateReleasedSettingsView(SettingsViewModel viewModel)
    {
        var view = new SettingsView { DataContext = viewModel };
        ExerciseLoadedCycles(view);
        AssertDirectHandlerCount(viewModel, view, "ViewModel_PropertyChanged", 0);
        Assert.Null(ReadPrivateField(view, "_viewModel"));
        return new WeakReference(view);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateReleasedHistoryView(TransactionHistoryViewModel viewModel)
    {
        var view = new TransactionHistoryView { DataContext = viewModel };
        ExerciseLoadedCycles(view);
        AssertDirectHandlerCount(viewModel, view, "ViewModelPropertyChanged", 0);
        Assert.Null(ReadPrivateField(view, "_viewModel"));
        return new WeakReference(view);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateReleasedPaymentView(CountingPropertyChangedSource viewModel)
    {
        var view = new PaymentView { DataContext = viewModel };
        ExerciseLoadedCycles(view);
        AssertDirectHandlerCount(viewModel, view, "PaymentViewModelPropertyChanged", 0);
        Assert.Null(ReadPrivateField(view, "_viewModelNotifications"));
        return new WeakReference(view);
    }

    private static void ExerciseLoadedCycles(FrameworkElement view)
    {
        for (var cycle = 0; cycle < 2; cycle++)
        {
            RaiseLoaded(view);
            RaiseUnloaded(view);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference LoadAndReplacePaymentViewModel(
        PaymentView view,
        CountingPropertyChangedSource replacement)
    {
        var previous = new CountingPropertyChangedSource();
        view.DataContext = previous;
        AssertDirectHandlerCount(previous, view, "PaymentViewModelPropertyChanged", 0);
        RaiseLoaded(view);
        AssertDirectHandlerCount(previous, view, "PaymentViewModelPropertyChanged", 1);
        RaiseLoaded(view);
        AssertDirectHandlerCount(previous, view, "PaymentViewModelPropertyChanged", 1);

        // Payment 是缓存页面；仅隐藏而未 Unloaded 时必须继续接收 VM 通知。
        view.Visibility = Visibility.Hidden;
        AssertDirectHandlerCount(previous, view, "PaymentViewModelPropertyChanged", 1);
        view.Visibility = Visibility.Visible;

        var weakReference = new WeakReference(previous);
        view.DataContext = replacement;
        AssertDirectHandlerCount(previous, view, "PaymentViewModelPropertyChanged", 0);
        return weakReference;
    }

    private static SettingsViewModel CreateSettingsViewModel()
    {
        var setupService = DispatchProxy.Create<ICardTerminalSetupService, ThrowingDispatchProxy>();
        return new SettingsViewModel(setupService);
    }

    private static Application CreateTestApplication()
    {
        var application = new Application();
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign3.Defaults.xaml",
                UriKind.Absolute)
        });
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/Hbpos.Client.Wpf;component/Themes/PosTheme.xaml",
                UriKind.Absolute)
        });
        return application;
    }

    private static void RaiseLoaded(FrameworkElement view) =>
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, view));

    private static void RaiseUnloaded(FrameworkElement view) =>
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, view));

    private static object? ReadPrivateField(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return field!.GetValue(instance);
    }

    private static void InvokePrivateMethod(object instance, string methodName)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(instance, null);
    }

    private static void AssertDirectHandlerCount(
        INotifyPropertyChanged source,
        object target,
        string methodName,
        int expected)
    {
        var handlers = ReadPropertyChangedHandlers(source);
        var count = handlers?
            .GetInvocationList()
            .Count(handler =>
                ReferenceEquals(handler.Target, target) &&
                string.Equals(handler.Method.Name, methodName, StringComparison.Ordinal)) ?? 0;
        Assert.Equal(expected, count);
    }

    private static Delegate? ReadPropertyChangedHandlers(INotifyPropertyChanged source)
    {
        for (var type = source.GetType(); type is not null; type = type.BaseType)
        {
            var field = type
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .FirstOrDefault(candidate =>
                    candidate.FieldType == typeof(PropertyChangedEventHandler) &&
                    candidate.Name.Contains("PropertyChanged", StringComparison.OrdinalIgnoreCase));
            if (field is not null)
            {
                return field.GetValue(source) as Delegate;
            }
        }

        throw new InvalidOperationException($"找不到 {source.GetType().FullName} 的 PropertyChanged 事件字段。");
    }

    private static void ForceFullCollection()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
    }

    private static async Task RunOnStaDispatcherAsync(Action action)
    {
        var dispatcherReady = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                dispatcherReady.TrySetResult(dispatcher);
                Dispatcher.Run();
            }
            catch (Exception ex)
            {
                dispatcherReady.TrySetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "Hbpos.Client.Tests.WpfViewLifecycleDispatcher"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        var dispatcher = await dispatcherReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await dispatcher.InvokeAsync(action, DispatcherPriority.Normal).Task;
        }
        finally
        {
            if (!dispatcher.HasShutdownStarted)
            {
                dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            }

            Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "WPF Dispatcher thread did not shut down.");
        }
    }

    private sealed class CountingPropertyChangedSource : INotifyPropertyChanged
    {
        private PropertyChangedEventHandler? _propertyChanged;

        public event PropertyChangedEventHandler? PropertyChanged
        {
            add => _propertyChanged += value;
            remove => _propertyChanged -= value;
        }
    }

    public class ThrowingDispatchProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new NotSupportedException($"测试不应调用 {targetMethod?.Name}。");
    }
}
