using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Direct2D1;
using Vortice.DCommon;
using Vortice.Mathematics;

namespace MonitorMirror;

public enum WarpMode
{
    None,
    Stretch,
    Fill
}

internal sealed class MirrorEngine : IDisposable
{
    private readonly object _lock = new();
    private Screen _mainScreen;
    private Screen _secondaryScreen;
    private float _scale;
    private WarpMode _warp;

    private Thread? _thread;
    private volatile bool _running;
    private volatile bool _visible = true;
    private bool _windowShown;

    private Form? _window;
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDXGIOutputDuplication? _duplication;
    private IDXGISwapChain1? _swapChain;
    private ID2D1DeviceContext? _d2dContext;
    private ID2D1Factory1? _d2dFactory;
    private ID2D1Device? _d2dDevice;
    private ID3D11Texture2D? _copyTexture;
    private ID2D1Bitmap1? _copyBitmap;
    private uint _copyWidth, _copyHeight;

    public bool IsRunning => _running;

    public bool Visible
    {
        get => _visible;
        set => _visible = value;
    }

    public MirrorEngine(Screen mainScreen, Screen secondaryScreen, float scale, WarpMode warp)
    {
        _mainScreen = mainScreen;
        _secondaryScreen = secondaryScreen;
        _scale = scale;
        _warp = warp;
    }

    public void UpdateSettings(float scale, WarpMode warp)
    {
        lock (_lock)
        {
            _scale = scale;
            _warp = warp;
        }
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _thread = new Thread(RunLoop) { IsBackground = true };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _thread?.Join(2000);
        _thread = null;
    }

    private void RunLoop()
    {
        try
        {
            InitializeGraphics();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to start mirroring:\n{ex.Message}", "Monitor Mirror", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _running = false;
            return;
        }

        _windowShown = _visible;
        if (_windowShown) _window!.Show();
        else _window!.Hide();

        while (_running)
        {
            Application.DoEvents();
            if (!_running) break;

            bool wantVisible = _visible;
            if (wantVisible != _windowShown)
            {
                if (wantVisible)
                {
                    RenderFrame();
                    _window.Show();
                }
                else
                {
                    _window.Hide();
                }
                _windowShown = wantVisible;
                continue;
            }

            if (_windowShown)
                RenderFrame();
            else
                Thread.Sleep(15);
        }

        CleanupGraphics();
    }

    private void InitializeGraphics()
    {
        _window = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Bounds = _secondaryScreen.Bounds,
            TopMost = true,
            BackColor = System.Drawing.Color.Black,
            ShowInTaskbar = false,
            Text = "Monitor Mirror Output"
        };
        _window.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) _running = false;
        };
        _window.FormClosed += (_, _) => _running = false;

        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory2>();

        IDXGIAdapter1? targetAdapter = null;
        IDXGIOutput1? targetOutput = null;

        for (uint a = 0; factory.EnumAdapters1(a, out var adapter).Success; a++)
        {
            for (uint o = 0; adapter.EnumOutputs(o, out var output).Success; o++)
            {
                var desc = output.Description;
                if (string.Equals(desc.DeviceName, _mainScreen.DeviceName, StringComparison.OrdinalIgnoreCase))
                {
                    targetAdapter = adapter;
                    targetOutput = output.QueryInterface<IDXGIOutput1>();
                    break;
                }
                output.Dispose();
            }
            if (targetOutput != null) break;
            adapter.Dispose();
        }

        if (targetAdapter == null || targetOutput == null)
            throw new InvalidOperationException($"Could not find DXGI output for monitor {_mainScreen.DeviceName}");

        D3D11.D3D11CreateDevice(
            targetAdapter,
            DriverType.Unknown,
            DeviceCreationFlags.BgraSupport,
            new[] { Vortice.Direct3D.FeatureLevel.Level_11_1, Vortice.Direct3D.FeatureLevel.Level_11_0, Vortice.Direct3D.FeatureLevel.Level_10_1, Vortice.Direct3D.FeatureLevel.Level_10_0 },
            out _device!).CheckError();
        _context = _device!.ImmediateContext;

        _duplication = targetOutput.DuplicateOutput(_device);

        var swapChainDesc = new SwapChainDescription1
        {
            Width = (uint)_secondaryScreen.Bounds.Width,
            Height = (uint)_secondaryScreen.Bounds.Height,
            Format = Format.B8G8R8A8_UNorm,
            Stereo = false,
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = 2,
            Scaling = Vortice.DXGI.Scaling.Stretch,
            SwapEffect = SwapEffect.FlipDiscard,
            AlphaMode = Vortice.DXGI.AlphaMode.Ignore
        };

        _swapChain = factory.CreateSwapChainForHwnd(_device, _window.Handle, swapChainDesc);

        _d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>(Vortice.Direct2D1.FactoryType.SingleThreaded);
        using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
        _d2dDevice = _d2dFactory.CreateDevice(dxgiDevice);
        _d2dContext = _d2dDevice.CreateDeviceContext(DeviceContextOptions.None);

        using var backBuffer = _swapChain.GetBuffer<IDXGISurface>(0);
        var bitmapProps = new BitmapProperties1(
            new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Ignore),
            96, 96,
            BitmapOptions.Target | BitmapOptions.CannotDraw);
        using var targetBitmap = _d2dContext.CreateBitmapFromDxgiSurface(backBuffer, bitmapProps);
        _d2dContext.Target = targetBitmap;

        targetOutput.Dispose();
        targetAdapter.Dispose();
    }

    private (RectangleF src, RectangleF dest) ComputeRects(uint texW, uint texH, float scale, WarpMode warp)
    {
        float destW = _secondaryScreen.Bounds.Width;
        float destH = _secondaryScreen.Bounds.Height;

        float cropW = texW * scale;
        float cropH = texH * scale;
        float cropX = (texW - cropW) / 2f;
        float cropY = (texH - cropH) / 2f;

        switch (warp)
        {
            case WarpMode.Stretch:
                return (new RectangleF(cropX, cropY, cropW, cropH), new RectangleF(0, 0, destW, destH));

            case WarpMode.None:
            {
                float factor = Math.Min(destW / cropW, destH / cropH);
                float drawW = cropW * factor;
                float drawH = cropH * factor;
                float dx = (destW - drawW) / 2f;
                float dy = (destH - drawH) / 2f;
                return (new RectangleF(cropX, cropY, cropW, cropH), new RectangleF(dx, dy, drawW, drawH));
            }

            case WarpMode.Fill:
            default:
            {
                float srcAspect = cropW / cropH;
                float destAspect = destW / destH;
                float finalW = cropW, finalH = cropH, finalX = cropX, finalY = cropY;
                if (srcAspect > destAspect)
                {
                    finalW = cropH * destAspect;
                    finalX = cropX + (cropW - finalW) / 2f;
                }
                else
                {
                    finalH = cropW / destAspect;
                    finalY = cropY + (cropH - finalH) / 2f;
                }
                return (new RectangleF(finalX, finalY, finalW, finalH), new RectangleF(0, 0, destW, destH));
            }
        }
    }

    private void RenderFrame()
    {
        var result = _duplication!.AcquireNextFrame(500, out var frameInfo, out var desktopResource);
        if (result.Failure)
        {
            desktopResource?.Dispose();
            return;
        }

        try
        {
            using var texture = desktopResource!.QueryInterface<ID3D11Texture2D>();
            var desc = texture.Description;

            if (_copyTexture == null || desc.Width != _copyWidth || desc.Height != _copyHeight)
            {
                _copyBitmap?.Dispose();
                _copyTexture?.Dispose();
                _copyWidth = desc.Width;
                _copyHeight = desc.Height;

                _copyTexture = _device!.CreateTexture2D(new Texture2DDescription
                {
                    Width = desc.Width,
                    Height = desc.Height,
                    Format = desc.Format,
                    MipLevels = 1,
                    ArraySize = 1,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.ShaderResource,
                    CPUAccessFlags = CpuAccessFlags.None
                });
                using var copySurface = _copyTexture.QueryInterface<IDXGISurface>();
                var copyBitmapProps = new BitmapProperties1(
                    new Vortice.DCommon.PixelFormat(desc.Format, Vortice.DCommon.AlphaMode.Ignore),
                    96, 96,
                    BitmapOptions.None);
                _copyBitmap = _d2dContext!.CreateBitmapFromDxgiSurface(copySurface, copyBitmapProps);
            }

            _context!.CopyResource(_copyTexture, texture);

            float scale;
            WarpMode warp;
            lock (_lock) { scale = _scale; warp = _warp; }
            var (srcRect, destRect) = ComputeRects(desc.Width, desc.Height, scale, warp);

            _d2dContext!.BeginDraw();
            _d2dContext.Clear(Colors.Black);
            _d2dContext.DrawBitmap(_copyBitmap, destRect, 1.0f, BitmapInterpolationMode.Linear, srcRect);
            _d2dContext.EndDraw();

            _swapChain!.Present(1, PresentFlags.None);
        }
        finally
        {
            desktopResource!.Dispose();
            _duplication.ReleaseFrame();
        }
    }

    private void CleanupGraphics()
    {
        _copyBitmap?.Dispose();
        _copyTexture?.Dispose();
        _duplication?.Dispose();
        _swapChain?.Dispose();
        _d2dContext?.Dispose();
        _d2dDevice?.Dispose();
        _d2dFactory?.Dispose();
        _context?.Dispose();
        _device?.Dispose();
        _window?.Dispose();

        _copyBitmap = null;
        _copyTexture = null;
        _duplication = null;
        _swapChain = null;
        _d2dContext = null;
        _d2dDevice = null;
        _d2dFactory = null;
        _context = null;
        _device = null;
        _window = null;
    }

    public void Dispose()
    {
        Stop();
    }
}
