using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace ScreenPowerPro.Core.Capture;

// Interop for creating IDirect3DDevice from DXGI
[ComImport]
[Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[System.Runtime.InteropServices.ComVisible(true)]
interface IDirect3DDxgiInterfaceAccess
{
    IntPtr GetInterface([In] ref Guid iid);
}

public class WindowCaptureService : IDisposable
{
    // Interop to get GraphicsCaptureItem from HWND
    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [System.Runtime.InteropServices.ComVisible(true)]
    interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow(
            [In] IntPtr window,
            [In] ref Guid iid);

        IntPtr CreateForMonitor(
            [In] IntPtr monitor,
            [In] ref Guid iid);
    }

    [DllImport("combase.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern IntPtr RoGetActivationFactory(
        [MarshalAs(UnmanagedType.HString)] string activatableClassId,
        [In] ref Guid iid);

    [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    private static extern uint CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(
        IntPtr pAdapter,
        int driverType,
        IntPtr software,
        uint flags,
        IntPtr pFeatureLevels,
        uint featureLevels,
        uint sdkVersion,
        out IntPtr ppDevice,
        out int pFeatureLevel,
        out IntPtr ppImmediateContext);

    private GraphicsCaptureItem? _captureItem;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;
    private IDirect3DDevice? _device;
    private Stream? _outputStream;
    private bool _isRunning = false;
    private int _width;
    private int _height;

    public int Width => _width;
    public int Height => _height;

    public WindowCaptureService()
    {
    }

    private GraphicsCaptureItem CreateItemForWindow(IntPtr hWnd)
    {
        // 1. Try modern WindowId approach (Windows App SDK / Windows 10/11)
        try
        {
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            var winUiId = new Windows.UI.WindowId { Value = windowId.Value };
            var item = GraphicsCaptureItem.TryCreateFromWindowId(winUiId);
            if (item != null) return item;
        }
        catch { }

        // 2. Fallback to COM activation factory
        Guid interopGuid = typeof(IGraphicsCaptureItemInterop).GUID;
        IntPtr factoryPtr = RoGetActivationFactory("Windows.Graphics.Capture.GraphicsCaptureItem", ref interopGuid);
        var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factoryPtr);
        Guid itemGuid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
        IntPtr itemPtr = interop.CreateForWindow(hWnd, ref itemGuid);
        Marshal.Release(factoryPtr);

        var captureItem = MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPtr);
        Marshal.Release(itemPtr);
        return captureItem;
    }

    private IDirect3DDevice CreateD3DDevice()
    {
        // 1 = D3D_DRIVER_TYPE_HARDWARE, 0x20 = D3D11_CREATE_DEVICE_BGRA_SUPPORT
        int hr = D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out IntPtr d3dDevice, out _, out IntPtr context);
        if (hr != 0) throw new Exception("Failed to create D3D11 device");

        Guid dxgiDeviceGuid = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
        Marshal.QueryInterface(d3dDevice, in dxgiDeviceGuid, out IntPtr dxgiDevice);

        uint hr2 = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out IntPtr inspectableDevice);
        if (hr2 != 0) throw new Exception("Failed to create WinRT D3D11 device");

        var device = MarshalInterface<IDirect3DDevice>.FromAbi(inspectableDevice);

        Marshal.Release(inspectableDevice);
        Marshal.Release(dxgiDevice);
        Marshal.Release(context);
        Marshal.Release(d3dDevice);

        return device;
    }

    public (int Width, int Height) PrepareCapture(IntPtr targetHwnd)
    {
        _device ??= CreateD3DDevice();
        _captureItem ??= CreateItemForWindow(targetHwnd);
        _width = _captureItem.Size.Width;
        _height = _captureItem.Size.Height;
        return (_width, _height);
    }

    public void StartCapture(Stream outputStream, bool captureCursor = false)
    {
        if (_isRunning) throw new InvalidOperationException("Capture is already running.");
        if (_captureItem == null || _device == null) throw new InvalidOperationException("PrepareCapture must be called before StartCapture(Stream).");

        _outputStream = outputStream;

        _captureItem.Closed += (s, e) => { StopCapture(); };

        _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _device,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            1,
            _captureItem.Size);

        _framePool.FrameArrived += OnFrameArrived;

        _session = _framePool.CreateCaptureSession(_captureItem);
#pragma warning disable CA1416
        if (Windows.Foundation.Metadata.ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsCursorCaptureEnabled"))
        {
            _session.IsCursorCaptureEnabled = captureCursor;
        }
        if (Windows.Foundation.Metadata.ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsBorderRequired"))
        {
            _session.IsBorderRequired = true;
        }
#pragma warning restore CA1416
        _session.StartCapture();

        _isRunning = true;
    }

    public void StartCapture(IntPtr targetHwnd, Stream outputStream, bool captureCursor = false)
    {
        PrepareCapture(targetHwnd);
        StartCapture(outputStream, captureCursor);
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        using var frame = sender.TryGetNextFrame();
        if (frame == null) return;

        try
        {
            var surface = frame.Surface;
            using var softwareBitmap = Windows.Graphics.Imaging.SoftwareBitmap.CreateCopyFromSurfaceAsync(surface).GetAwaiter().GetResult();

            using var buffer = softwareBitmap.LockBuffer(Windows.Graphics.Imaging.BitmapBufferAccessMode.Read);
            using var reference = buffer.CreateReference();
            var byteAccess = reference.As<IMemoryBufferByteAccess>();
            byteAccess.GetBuffer(out IntPtr dataPtr, out uint capacity);

            // Write directly to FFmpeg stdin stream (rawvideo, bgra)
            if (_outputStream != null && _outputStream.CanWrite)
            {
                unsafe
                {
                    var span = new ReadOnlySpan<byte>(dataPtr.ToPointer(), (int)capacity);
                    _outputStream.Write(span);
                }
            }
        }
        catch
        {
            // Ignore dropped frames
        }
    }

    public void StopCapture()
    {
        if (!_isRunning) return;
        _isRunning = false;

        _session?.Dispose();
        _session = null;

        _framePool?.Dispose();
        _framePool = null;

        _device?.Dispose();
        _device = null;

        _captureItem = null;
    }

    public void Dispose()
    {
        StopCapture();
    }
}

[ComImport]
[Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
unsafe interface IMemoryBufferByteAccess
{
    void GetBuffer(out IntPtr buffer, out uint capacity);
}
