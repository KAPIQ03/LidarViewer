using System;
using System.Numerics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using LidarViewer.Core.Models;
using LidarViewer.Core.Rendering;
using Silk.NET.OpenGL;

namespace LidarViewer.App.Controls;

public sealed class PointCloudView3D : OpenGlControlBase
{
    private const string Waiting = "OpenGL: czekam na inicjalizację kontekstu...";

    public static readonly StyledProperty<PointCloud?> PointCloudProperty =
        AvaloniaProperty.Register<PointCloudView3D, PointCloud?>(nameof(PointCloud));

    public static readonly StyledProperty<string?> DiagnosticsProperty =
        AvaloniaProperty.Register<PointCloudView3D, string?>(nameof(Diagnostics), Waiting, defaultBindingMode: BindingMode.OneWayToSource);

    public PointCloud? PointCloud
    {
        get => GetValue(PointCloudProperty);
        set => SetValue(PointCloudProperty, value);
    }

    public string? Diagnostics
    {
        get => GetValue(DiagnosticsProperty);
        set => SetValue(DiagnosticsProperty, value);
    }

    public static readonly StyledProperty<ColorSettings?> ColorSettingsProperty =
        AvaloniaProperty.Register<PointCloudView3D, ColorSettings?>(nameof(ColorSettings));

    public ColorSettings? ColorSettings
    {
        get => GetValue(ColorSettingsProperty);
        set => SetValue(ColorSettingsProperty, value);
    }

    /// <summary>
    /// Czy kamera ma dopasowac sie do chmury przy kazdej zmianie. W trybie
    /// live wyłączone, bo kolejne klatki cofałyby obrót ustawiony przez
    /// uzytkownika.
    /// </summary>
    public static readonly StyledProperty<bool> AutoFitCameraProperty =
        AvaloniaProperty.Register<PointCloudView3D, bool>(nameof(AutoFitCamera), true);

    public bool AutoFitCamera
    {
        get => GetValue(AutoFitCameraProperty);
        set => SetValue(AutoFitCameraProperty, value);
    }

    /// <summary>
    /// Numer zbioru danych. Zmiana wymusza ponowne dopasowanie kamery przy
    /// nastepnej chmurze, takze gdy AutoFitCamera jest wylaczone.
    /// </summary>
    public static readonly StyledProperty<int> FitVersionProperty =
        AvaloniaProperty.Register<PointCloudView3D, int>(nameof(FitVersion));

    public int FitVersion
    {
        get => GetValue(FitVersionProperty);
        set => SetValue(FitVersionProperty, value);
    }

    /// <summary>
    /// Tryb "Kamera": widok z miejsca czujnika, obracany myszka (bez orbitowania).
    /// </summary>
    public static readonly StyledProperty<bool> SensorViewProperty =
        AvaloniaProperty.Register<PointCloudView3D, bool>(nameof(SensorView));

    public bool SensorView
    {
        get => GetValue(SensorViewProperty);
        set => SetValue(SensorViewProperty, value);
    }

    public static readonly StyledProperty<double> PointSizeProperty =
        AvaloniaProperty.Register<PointCloudView3D, double>(nameof(PointSize), 2.0);

    /// <summary>Rozmiar punktu w pikselach urzadzenia logicznego.</summary>
    public double PointSize
    {
        get => GetValue(PointSizeProperty);
        set => SetValue(PointSizeProperty, value);
    }

    public static readonly StyledProperty<bool> ShowGridProperty =
        AvaloniaProperty.Register<PointCloudView3D, bool>(nameof(ShowGrid), true);

    /// <summary>Kolowa siatka pomiarowa w plaszczyznie czujnika.</summary>
    public bool ShowGrid
    {
        get => GetValue(ShowGridProperty);
        set => SetValue(ShowGridProperty, value);
    }

    public static readonly StyledProperty<bool> ShowAxesProperty =
        AvaloniaProperty.Register<PointCloudView3D, bool>(nameof(ShowAxes), true);

    /// <summary>Osie ukladu wspolrzednych (X czerwona, Y zielona, Z niebieska).</summary>
    public bool ShowAxes
    {
        get => GetValue(ShowAxesProperty);
        set => SetValue(ShowAxesProperty, value);
    }

    private const string VertexShader = """
        in vec3 aPos;
        in vec4 aColor;
        uniform mat4 uMvp;
        uniform float uPointSize;
        out vec4 vColor;
        void main()
        {
            gl_Position = uMvp * vec4(aPos, 1.0);
            gl_PointSize = uPointSize;
            vColor = aColor;
        }
        """;

    private const string FragmentShader = """
        in vec4 vColor;
        out vec4 FragColor;
        void main()
        {
            FragColor = vColor;
        }
        """;

    private GL? _gl;
    private bool _initOk;
    private uint _program, _vao, _vbo, _colVbo;
    private int _mvpLocation, _pointSizeLocation;
    private bool _colorsDirty = true;
    private bool _cameraFitted;
    private Vector3 _anchor;
    private Action<int, int, byte[]>? _screenshotCallback;
    private uint _guideVao, _guideVbo, _guideColVbo;
    private int _axisVertexCount, _gridVertexCount;

    private PointCloud? _cloud;
    private bool _uploadPending;
    private int _pointCount;
    private readonly OrbitCamera _camera = new();
    private readonly SensorCamera _sensor = new();
    private Point _lastPointer;
    private bool _rotating, _panning;
    private int _viewportHeight = 1;

    private readonly StringBuilder _log = new();
    private bool _firstRenderReported, _firstPointerReported;
    private string? _lastGlError;

    static PointCloudView3D()
    {
        PointCloudProperty.Changed.AddClassHandler<PointCloudView3D>((view, e) =>
        {
            view._cloud = e.NewValue as PointCloud;
            view._uploadPending = true;
            view.InvalidateColors();
            view.RequestNextFrameRendering();
        });

        FitVersionProperty.Changed.AddClassHandler<PointCloudView3D>((view, _) => view._cameraFitted = false);
        SensorViewProperty.Changed.AddClassHandler<PointCloudView3D>((view, _) => view.RequestNextFrameRendering());
        PointSizeProperty.Changed.AddClassHandler<PointCloudView3D>((view, _) => view.RequestNextFrameRendering());
        ShowGridProperty.Changed.AddClassHandler<PointCloudView3D>((view, _) => view.RequestNextFrameRendering());
        ShowAxesProperty.Changed.AddClassHandler<PointCloudView3D>((view, _) => view.RequestNextFrameRendering());

        ColorSettingsProperty.Changed.AddClassHandler<PointCloudView3D>((view, _) => view.InvalidateColors());
    }

    protected override void OnOpenGlInit(GlInterface gl)
    {
        try
        {
            InitGl(gl);
            _initOk = true;
        }
        catch (Exception ex)
        {
            Report("BŁĄD inicjalizacji OpenGL: " + ex);
        }
    }

    private void InitGl(GlInterface gl)
    {
        _gl = GL.GetApi(gl.GetProcAddress);
        bool isEs = gl.ContextInfo.Version.Type == GlProfileType.OpenGLES;
        Report($"Init OK. OpenGL: {_gl.GetStringS(StringName.Version)} | GLSL: {_gl.GetStringS(StringName.ShadingLanguageVersion)}"
               + $" | renderer: {_gl.GetStringS(StringName.Renderer)}");

        _program = BuildProgram(_gl, ShaderHeader(isEs));
        _mvpLocation = _gl.GetUniformLocation(_program, "uMvp");
        _pointSizeLocation = _gl.GetUniformLocation(_program, "uPointSize");

        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        unsafe { _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), null); }
        _gl.EnableVertexAttribArray(0);

        _colVbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _colVbo);
        unsafe { _gl.VertexAttribPointer(1, 4, VertexAttribPointerType.UnsignedByte, true, 4, null); }
        _gl.EnableVertexAttribArray(1);

        // Osie i siatka: osobny VAO, te same atrybuty (pozycja, kolor RGBA).
        _guideVao = _gl.GenVertexArray();
        _guideVbo = _gl.GenBuffer();
        _guideColVbo = _gl.GenBuffer();
        _gl.BindVertexArray(_guideVao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _guideVbo);
        unsafe { _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), null); }
        _gl.EnableVertexAttribArray(0);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _guideColVbo);
        unsafe { _gl.VertexAttribPointer(1, 4, VertexAttribPointerType.UnsignedByte, true, 4, null); }
        _gl.EnableVertexAttribArray(1);
        _gl.BindVertexArray(_vao);

        if (!isEs) _gl.Enable(EnableCap.ProgramPointSize);
        _gl.Enable(EnableCap.DepthTest);

        // Po ponownym utworzeniu kontekstu bufor kolorow jest nowy i pusty,
        // wiec trzeba go wypelnic jeszcze raz.
        _uploadPending = true;
        _colorsDirty = true;
    }

    protected override void OnOpenGlRender(GlInterface glInterface, int fb)
    {
        if (!_initOk || _gl is not { } gl) return;
        try
        {
            RenderFrame(gl, fb);
        }
        catch (Exception ex)
        {
            Report("BŁĄD renderowania: " + ex);
        }
    }

    private unsafe void RenderFrame(GL gl, int fb)
    {
        if (_uploadPending)
            UploadPoints(gl);

        if (_colorsDirty)
            UploadColors(gl);

        double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        int width = Math.Max(1, (int)(Bounds.Width * scale));
        int height = Math.Max(1, (int)(Bounds.Height * scale));
        _viewportHeight = height;

        gl.Viewport(0, 0, (uint)width, (uint)height);
        gl.ClearColor(0.1f, 0.1f, 0.1f, 1f);
        gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        if (_pointCount > 0)
        {
            float aspect = (float)width / height;
            Matrix4x4 mvp = SensorView ? _sensor.ViewProjection(aspect) : _camera.ViewProjection(aspect);

            gl.UseProgram(_program);
            gl.UniformMatrix4(_mvpLocation, 1, false, (float*)&mvp);
            gl.Uniform1(_pointSizeLocation, (float)(Math.Max(PointSize, 1.0) * scale));
            gl.BindVertexArray(_vao);
            gl.DrawArrays(PrimitiveType.Points, 0, (uint)_pointCount);

            gl.BindVertexArray(_guideVao);
            if (ShowGrid && _gridVertexCount > 0)
                gl.DrawArrays(PrimitiveType.Lines, _axisVertexCount, (uint)_gridVertexCount);
            if (ShowAxes && _axisVertexCount > 0)
                gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_axisVertexCount);
            gl.BindVertexArray(_vao);
        }

        if (_screenshotCallback is not null)
            CaptureScreenshot(gl, width, height);

        if (!_firstRenderReported)
        {
            _firstRenderReported = true;
            Report($"Render OK: viewport {width}x{height}, fb={fb}");
        }
        ReportGlError(gl, "po renderowaniu");
    }

    protected override void OnOpenGlDeinit(GlInterface glInterface)
    {
        _initOk = false;
        if (_gl is null) return;
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteBuffer(_colVbo);
        _gl.DeleteBuffer(_guideVbo);
        _gl.DeleteBuffer(_guideColVbo);
        _gl.DeleteVertexArray(_guideVao);
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteProgram(_program);
        _gl.Dispose();
        _gl = null;
    }

    private void InvalidateColors()
    {
        _colorsDirty = true;
        RequestNextFrameRendering();
    }

    /// <summary>
    /// Zleca zrzut widoku. Przy najblizszym renderze piksele sa odczytywane z bufora ramki i przekazywane
    /// (szerokosc, wysokosc, RGBA od gory do dolu) w wywolaniu zwrotnym na watku interfejsu.
    /// </summary>
    public void RequestScreenshot(Action<int, int, byte[]> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _screenshotCallback = callback;
        RequestNextFrameRendering();
    }

    private unsafe void CaptureScreenshot(GL gl, int width, int height)
    {
        Action<int, int, byte[]>? callback = _screenshotCallback;
        _screenshotCallback = null;
        if (callback is null)
            return;

        byte[] raw = new byte[width * height * 4];
        fixed (byte* data = raw)
            gl.ReadPixels(0, 0, (uint)width, (uint)height, Silk.NET.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, data);

        // OpenGL liczy wiersze od dolu, a obraz ma byc od gory; alfa wymuszamy na nieprzezroczysta.
        byte[] rgba = new byte[raw.Length];
        int stride = width * 4;
        for (int row = 0; row < height; row++)
        {
            System.Buffer.BlockCopy(raw, (height - 1 - row) * stride, rgba, row * stride, stride);
        }

        for (int i = 3; i < rgba.Length; i += 4)
            rgba[i] = 255;

        Dispatcher.UIThread.Post(() => callback(width, height, rgba));
    }

    /// <summary>
    /// Przywraca domyslna kamere i dopasowuje ja do aktualnej chmury.
    /// </summary>
    public void ResetView()
    {
        _camera.Reset();
        _sensor.Reset();
        _cameraFitted = false;

        if (_cloud is { Count: > 0 } cloud)
        {
            _anchor = cloud.Center;
            _uploadPending = true;
            _camera.FitTo(Vector3.Zero, cloud.Radius);
            _sensor.FitTo(SensorPosition(cloud), cloud.Radius);
        }

        RequestNextFrameRendering();
    }

    /// <summary>
    /// Pozycja czujnika w ukladzie sceny (przesunietym o _anchor): poczatek ukladu wspolrzednych, jesli
    /// lezy wewnatrz chmury (nagrania lidaru), w przeciwnym razie srodek chmury (np. pliki LAS w ukladzie globalnym).
    /// </summary>
    private Vector3 SensorPosition(PointCloud cloud) =>
        cloud.Center.Length() <= cloud.Radius ? -_anchor : Vector3.Zero;

    /// <summary>
    /// Osie i siatka dla biezacej chmury: jeden bufor, osie pierwsze, potem siatka.
    /// </summary>
    private unsafe void UploadGuides(GL gl, PointCloud cloud)
    {
        var guides = SceneGuides.Build(SensorPosition(cloud), cloud.Radius);
        float[] positions = [.. guides.AxisPositions, .. guides.GridPositions];
        byte[] colors = [.. guides.AxisColors, .. guides.GridColors];
        _axisVertexCount = guides.AxisVertexCount;
        _gridVertexCount = guides.GridVertexCount;

        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _guideVbo);
        fixed (float* data = positions)
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(positions.Length * sizeof(float)), data, BufferUsageARB.DynamicDraw);

        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _guideColVbo);
        fixed (byte* data = colors)
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)colors.Length, data, BufferUsageARB.DynamicDraw);

        // Powrot do VBO chmury, zeby kolejne wywolania nie zmienily atrybutow VAO punktow.
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
    }

    private unsafe void UploadColors(GL gl)
    {
        _colorsDirty = false;

        var cloud = _cloud;
        if (cloud is null || cloud.Count == 0)
            return;

        byte[] rgba = CloudColors.Build(cloud, ColorSettings ?? ColorSettings.Default);

        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _colVbo);
        fixed (byte* data = rgba)
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)rgba.Length, data, BufferUsageARB.DynamicDraw);
    }

    private unsafe void UploadPoints(GL gl)
    {
        _uploadPending = false;
        var cloud = _cloud;
        _pointCount = cloud?.Count ?? 0;
        if (cloud is null || _pointCount == 0) return;

        // Srodek odniesienia zmienia sie tylko przy dopasowaniu kamery. Dzieki temu kolejne
        // klatki sekwencji lub live nie przesuwaja sceny, gdy ich zakres nieco sie rozni.
        bool fit = AutoFitCamera || !_cameraFitted;
        if (fit)
            _anchor = cloud.Center;
        Vector3 center = _anchor;
        var relative = new float[cloud.Positions.Length];
        for (int i = 0; i < relative.Length; i += 3)
        {
            relative[i] = cloud.Positions[i] - center.X;
            relative[i + 1] = cloud.Positions[i + 1] - center.Y;
            relative[i + 2] = cloud.Positions[i + 2] - center.Z;
        }

        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        fixed (float* data = relative)
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(relative.Length * sizeof(float)), data, BufferUsageARB.StaticDraw);
        ReportGlError(gl, "po BufferData");

        // Kamera dopasowuje sie tylko do pierwszej chmury zbioru (live, sekwencja).
        // Kolejne klatki nie cofaja obrotu ustawionego przez uzytkownika.
        if (fit)
        {
            _camera.FitTo(Vector3.Zero, cloud.Radius);
            _sensor.FitTo(SensorPosition(cloud), cloud.Radius);
            _cameraFitted = true;
        }

        UploadGuides(gl, cloud);

        Report($"Wgrano {_pointCount:N0} punktów, promień chmury {cloud.Radius:F1}.");
    }


    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        base.Render(context);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        ReportFirstPointerEvent("naciśnięcie przycisku");
        var props = e.GetCurrentPoint(this).Properties;
        _panning = props.IsRightButtonPressed || (props.IsLeftButtonPressed && e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        _rotating = props.IsLeftButtonPressed && !_panning;
        _lastPointer = e.GetPosition(this);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _rotating = _panning = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_rotating && !_panning) return;

        var p = e.GetPosition(this);
        var d = p - _lastPointer;
        _lastPointer = p;

        if (_rotating && SensorView)
        {
            // "Chwytanie" sceny: obrot proporcjonalny do kata widzenia, wiec punkt pod kursorem podaza za myszka.
            double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
            float radiansPerPixel = _sensor.FovY / Math.Max(_viewportHeight, 1) * (float)scale;
            _sensor.Rotate((float)d.X * radiansPerPixel, (float)d.Y * radiansPerPixel);
        }
        else if (SensorView)
        {
            // Przesuwanie nie ma sensu, gdy kamera stoi w miejscu czujnika.
            return;
        }
        else if (_rotating)
        {
            _camera.Rotate(-(float)d.X * 0.01f, (float)d.Y * 0.01f);
        }
        else
        {
            double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
            float perPixel = _camera.WorldUnitsPerPixel(_viewportHeight) * (float)scale;
            _camera.Pan(-(float)d.X * perPixel, (float)d.Y * perPixel);
        }
        RequestNextFrameRendering();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        ReportFirstPointerEvent("kółko myszy");
        if (SensorView)
            _sensor.Zoom(MathF.Pow(0.9f, (float)e.Delta.Y));
        else
            _camera.Zoom(MathF.Pow(0.9f, (float)e.Delta.Y));
        RequestNextFrameRendering();
    }


    private void Report(string message)
    {
        string text;
        lock (_log)
        {
            _log.AppendLine(message);
            text = _log.ToString();
        }
        Dispatcher.UIThread.Post(() => Diagnostics = text);
    }

    private void ReportFirstPointerEvent(string what)
    {
        if (_firstPointerReported) return;
        _firstPointerReported = true;
        Report($"Mysz: kontrolka odebrała zdarzenie ({what}).");
    }

    private void ReportGlError(GL gl, string where)
    {
        var error = gl.GetError();
        string? text = error == GLEnum.NoError ? null : $"glGetError {where}: {error}";
        if (text != null && text != _lastGlError)
            Report(text);
        _lastGlError = text;
    }


    private static string ShaderHeader(bool isEs) =>
        isEs ? "#version 300 es\nprecision highp float;\n" : "#version 150\n";

    private static uint BuildProgram(GL gl, string header)
    {
        uint vs = Compile(gl, ShaderType.VertexShader, header + VertexShader);
        uint fs = Compile(gl, ShaderType.FragmentShader, header + FragmentShader);
        uint program = gl.CreateProgram();
        gl.AttachShader(program, vs);
        gl.AttachShader(program, fs);
        gl.BindAttribLocation(program, 0, "aPos");
        gl.BindAttribLocation(program, 1, "aColor");
        gl.LinkProgram(program);
        gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int ok);
        if (ok == 0)
            throw new InvalidOperationException("Linkowanie shadera nie powiodło się: " + gl.GetProgramInfoLog(program));
        gl.DeleteShader(vs);
        gl.DeleteShader(fs);
        return program;
    }

    private static uint Compile(GL gl, ShaderType type, string source)
    {
        uint shader = gl.CreateShader(type);
        gl.ShaderSource(shader, source);
        gl.CompileShader(shader);
        gl.GetShader(shader, ShaderParameterName.CompileStatus, out int ok);
        if (ok == 0)
            throw new InvalidOperationException($"Kompilacja shadera {type} nie powiodła się: " + gl.GetShaderInfoLog(shader));
        return shader;
    }
}
