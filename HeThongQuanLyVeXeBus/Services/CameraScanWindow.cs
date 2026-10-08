using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;

namespace HeThongQuanLyVeXeBus.Services;

/// <summary>Reads the QR printed on a ticket PDF using a camera; ticket validation belongs to the caller.</summary>
public sealed class CameraScanWindow : System.Windows.Window
{
    private readonly ComboBox _cameras = new() { MinWidth = 235, DisplayMemberPath = nameof(CameraDevice.Name) };
    private readonly Button _start = new() { Content = "Bắt đầu / Thử lại", MinWidth = 120, Margin = new Thickness(8, 0, 0, 0) };
    private readonly Button _refresh = new() { Content = "Tìm lại camera", MinWidth = 110, Margin = new Thickness(8, 0, 0, 0) };
    private readonly Image _preview = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private CancellationTokenSource? _captureCancellation;
    private Task? _captureTask;
    private bool _busy;
    private bool _closing;

    public string? ScannedCode { get; private set; }

    public CameraScanWindow()
    {
        Title = "Quét mã QR vé bằng camera";
        Width = 760;
        Height = 650;
        MinWidth = 470;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var layout = new DockPanel { Margin = new Thickness(16) };
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal };
        toolbar.Children.Add(new TextBlock { Text = "Camera:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        toolbar.Children.Add(_cameras);
        toolbar.Children.Add(_start);
        toolbar.Children.Add(_refresh);
        DockPanel.SetDock(toolbar, Dock.Top);
        layout.Children.Add(toolbar);

        var footer = new StackPanel();
        footer.Children.Add(_status);
        var cancel = new Button { Content = "Đóng", MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0), IsCancel = true };
        cancel.Click += (_, _) => Close();
        footer.Children.Add(cancel);
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(footer);

        var previewBorder = new Border { Background = Brushes.Black, Margin = new Thickness(0, 12, 0, 0), Child = _preview };
        layout.Children.Add(previewBorder);
        Content = layout;

        _start.Click += async (_, _) => await StartSelectedAsync();
        _refresh.Click += async (_, _) => await RefreshCamerasAsync();
        Loaded += async (_, _) => await RefreshCamerasAsync();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
                _captureCancellation?.Cancel();
        };
        Closing += (_, _) =>
        {
            _closing = true;
            var cancellation = _captureCancellation;
            var task = _captureTask;
            _captureCancellation = null;
            _captureTask = null;
            if (cancellation is null) return;
            cancellation.Cancel();
            if (task is null)
                cancellation.Dispose();
            else
                _ = task.ContinueWith(_ => cancellation.Dispose(), TaskScheduler.Default);
        };
        SetStatus("Đang tìm camera...");
        UpdateButtons();
    }

    private void SetStatus(string text) => _status.Text = text;

    private void UpdateButtons()
    {
        _start.IsEnabled = !_busy && _cameras.SelectedItem is CameraDevice;
        _refresh.IsEnabled = !_busy;
        _cameras.IsEnabled = !_busy;
    }

    private async Task RefreshCamerasAsync()
    {
        if (_busy || _closing) return;
        _busy = true;
        UpdateButtons();
        SetStatus("Đang tìm camera...");
        try
        {
            await StopCaptureAsync();
            var devices = await Task.Run(EnumerateCameras);
            if (_closing) return;
            _cameras.ItemsSource = devices;
            _cameras.SelectedIndex = devices.Count > 0 ? 0 : -1;
            if (devices.Count == 0)
                SetStatus("Không tìm thấy camera. Hãy kết nối camera, kiểm tra quyền truy cập camera của Windows và nhấn Tìm lại camera.");
            else
                await StartSelectedCoreAsync();
        }
        catch (Exception ex)
        {
            if (!_closing) SetStatus($"Không thể liệt kê camera: {ex.Message}. Kiểm tra quyền truy cập camera của Windows rồi thử lại.");
        }
        finally
        {
            _busy = false;
            if (!_closing) UpdateButtons();
        }
    }

    private async Task StartSelectedAsync()
    {
        if (_busy || _closing) return;
        _busy = true;
        UpdateButtons();
        try
        {
            await StopCaptureAsync();
            if (!_closing) await StartSelectedCoreAsync();
        }
        catch (Exception ex)
        {
            if (!_closing) SetStatus($"Không thể khởi động camera: {ex.Message}. Hãy nhấn Thử lại.");
        }
        finally
        {
            _busy = false;
            if (!_closing) UpdateButtons();
        }
    }

    private Task StartSelectedCoreAsync()
    {
        if (_cameras.SelectedItem is not CameraDevice device)
        {
            SetStatus("Chọn camera trước khi quét.");
            return Task.CompletedTask;
        }

        _preview.Source = null;
        SetStatus($"Đang mở {device.Name}... Hướng mã QR trên vé PDF vào camera.");
        var cancellation = new CancellationTokenSource();
        _captureCancellation = cancellation;
        _captureTask = Task.Run(() => CaptureAsync(device, cancellation.Token));
        // Closing cancels this task without waiting on native camera calls on the UI thread.
        return Task.CompletedTask;
    }

    private async Task StopCaptureAsync()
    {
        var cancellation = _captureCancellation;
        var task = _captureTask;
        _captureCancellation = null;
        _captureTask = null;
        cancellation?.Cancel();
        try
        {
            if (task is not null) await task;
        }
        finally
        {
            cancellation?.Dispose();
            _preview.Source = null;
        }
    }

    private async Task CaptureAsync(CameraDevice device, CancellationToken token)
    {
        try
        {
            // Open, read, decode and dispose on a worker; none of these native calls block WPF's UI thread.
            using var camera = new VideoCapture(device.Index, VideoCaptureAPIs.DSHOW);
            if (!camera.IsOpened())
            {
                await ReportStatusAsync("Không thể mở camera. Camera có thể đang được ứng dụng khác sử dụng hoặc Windows đã chặn quyền truy cập. Chọn camera khác hoặc nhấn Thử lại.", token);
                return;
            }

            camera.FrameWidth = 640;
            camera.FrameHeight = 480;
            using var detector = new QRCodeDetector();
            using var frame = new Mat();
            var failedReads = 0;
            while (!token.IsCancellationRequested)
            {
                if (!camera.Read(frame) || frame.Empty())
                {
                    if (++failedReads >= 20)
                    {
                        await ReportStatusAsync("Camera không trả về hình ảnh. Kiểm tra kết nối/quyền camera rồi nhấn Thử lại.", token);
                        return;
                    }
                    await Task.Delay(100, token);
                    continue;
                }

                failedReads = 0;
                var code = detector.DetectAndDecode(frame, out _);
                if (!string.IsNullOrWhiteSpace(code))
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (!token.IsCancellationRequested && !_closing && IsVisible && ScannedCode is null)
                        {
                            ScannedCode = code.Trim();
                            DialogResult = true;
                        }
                    });
                    return;
                }

                BitmapSource image = BitmapSourceConverter.ToBitmapSource(frame);
                image.Freeze();
                await Dispatcher.InvokeAsync(() =>
                {
                    if (!token.IsCancellationRequested && !_closing && IsVisible)
                    {
                        _preview.Source = image;
                        SetStatus($"Đang quét bằng {device.Name}. Hướng mã QR trên vé PDF vào camera.");
                    }
                });
                await Task.Delay(80, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Camera was changed, hidden, or the dialog was closed.
        }
        catch (Exception ex)
        {
            await ReportStatusAsync($"Lỗi camera: {ex.Message}. Kiểm tra camera và quyền truy cập của Windows rồi nhấn Thử lại.", token);
        }
    }

    private async Task ReportStatusAsync(string text, CancellationToken token)
    {
        if (token.IsCancellationRequested || Dispatcher.HasShutdownStarted) return;
        await Dispatcher.InvokeAsync(() =>
        {
            if (!token.IsCancellationRequested && !_closing && IsVisible) SetStatus(text);
        });
    }

    private sealed record CameraDevice(int Index, string Name);

    // DirectShow's video-input enumeration order matches OpenCV's CAP_DSHOW device indices.
    private static List<CameraDevice> EnumerateCameras()
    {
        var result = new List<CameraDevice>();
        object? enumeratorObject = null;
        IEnumMoniker? monikers = null;
        try
        {
            var enumeratorType = Type.GetTypeFromCLSID(new Guid("62BE5D10-60EB-11D0-BD3B-00A0C911CE86"), throwOnError: true)!;
            enumeratorObject = Activator.CreateInstance(enumeratorType)!;
            var category = new Guid("860BB310-5D01-11D0-BD3B-00A0C911CE86");
            var hr = ((ICreateDevEnum)enumeratorObject).CreateClassEnumerator(ref category, out monikers, 0);
            if (hr == 1) return result; // S_FALSE: no video devices
            Marshal.ThrowExceptionForHR(hr);

            var buffer = new IMoniker[1];
            while (monikers!.Next(1, buffer, IntPtr.Zero) == 0)
            {
                var moniker = buffer[0];
                object? propertyBag = null;
                try
                {
                    var propertyBagId = typeof(IPropertyBag).GUID;
                    moniker.BindToStorage(null!, null!, ref propertyBagId, out propertyBag);
                    var bag = (IPropertyBag)propertyBag;
                    var name = bag.Read("FriendlyName", out var value, IntPtr.Zero) == 0
                        ? value?.ToString() : null;
                    result.Add(new CameraDevice(result.Count, string.IsNullOrWhiteSpace(name) ? $"Camera {result.Count + 1}" : name));
                }
                finally
                {
                    if (propertyBag is not null) Marshal.ReleaseComObject(propertyBag);
                    Marshal.ReleaseComObject(moniker);
                }
            }
        }
        finally
        {
            if (monikers is not null) Marshal.ReleaseComObject(monikers);
            if (enumeratorObject is not null) Marshal.ReleaseComObject(enumeratorObject);
        }
        return result;
    }

    [ComImport, Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICreateDevEnum
    {
        [PreserveSig]
        int CreateClassEnumerator(ref Guid category, out IEnumMoniker monikers, int flags);
    }

    [ComImport, Guid("55272A00-42CB-11CE-8135-00AA004BB851"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyBag
    {
        [PreserveSig]
        int Read([MarshalAs(UnmanagedType.LPWStr)] string property, [MarshalAs(UnmanagedType.Struct)] out object value, IntPtr errorLog);
        [PreserveSig]
        int Write([MarshalAs(UnmanagedType.LPWStr)] string property, [MarshalAs(UnmanagedType.Struct)] ref object value);
    }
}
