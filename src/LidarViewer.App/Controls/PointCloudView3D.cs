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

/// <summary>
/// Widok 3D chmury punktów: kontrolka OpenGL (Silk.NET) z kamerą orbitalną <see cref="OrbitCamera"/>.
///
/// Przepływ danych i rysowania:
///  1. PointCloud (StyledProperty) trafia z bindingu; zapamiętujemy ją i oznaczamy bufor jako "brudny".
///  2. W najbliższej klatce (wątek z kontekstem GL) pozycje są wgrywane do VBO. Od współrzędnych
///     odejmujemy środek chmury, żeby 32-bitowy float nie tracił precyzji przy dużych wartościach;
///     kamera patrzy więc na (0,0,0), a pozycje w VBO są względem środka.
///  3. Każda klatka: viewport, czyszczenie, macierz kamery (uniform uMvp), rysowanie GL_POINTS.
///
/// Sterowanie: lewy przycisk - obrót, prawy przycisk (lub Shift + lewy) - przesuwanie, kółko - zoom.
/// Wszystkie błędy (kompilacja shadera, błędy GL) trafiają do właściwości Diagnostics, by były widoczne w oknie.
/// </summary>
public sealed class PointCloudView3D : OpenGlControlBase
{
    private const string Waiting = "OpenGL: czekam na inicjalizację kontekstu...";

    /// <summary>Chmura do wyświetlenia (null = pusty widok).</summary>
    public static readonly StyledProperty<PointCloud?> PointCloudProperty =
        AvaloniaProperty.Register<PointCloudView3D, PointCloud?>(nameof(PointCloud));

    /// <summary>
    /// Dziennik diagnostyczny wiązany w kierunku widok -> ViewModel. Domyślna wartość to tekst
    /// oczekiwania, bo przy OneWayToSource wartość początkowa kontrolki trafia do ViewModelu.
    /// </summary>
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

    // Shadery. Nagłówek wersji dobieramy w czasie działania (patrz ShaderHeader).
    // Wierzchołkowy: pozycja * macierz kamery, rozmiar punktu z uniformu.
    // Lokalizację atrybutu aPos ustawiamy w kodzie (glBindAttribLocation).
    private const string VertexShader = """
        in vec3 aPos;
        uniform mat4 uMvp;
        uniform float uPointSize;
        void main()
        {
            gl_Position = uMvp * vec4(aPos, 1.0);
            gl_PointSize = uPointSize;
        }
        """;

    private const string FragmentShader = """
        uniform vec4 uColor;
        out vec4 FragColor;
        void main()
        {
            FragColor = uColor;
        }
        """;

    // Zasoby OpenGL (tworzone w OnOpenGlInit).
    private GL? _gl;
    private bool _initOk;
    private uint _program, _vao, _vbo;
    private int _mvpLocation, _pointSizeLocation, _colorLocation;

    // Dane i stan widoku.
    private PointCloud? _cloud;      // chmura przekazana z UI
    private bool _uploadPending;
    private int _pointCount;
    private readonly OrbitCamera _camera = new();
    private Point _lastPointer;
    private bool _rotating, _panning;
    private int _viewportHeight = 1;

    // Dziennik diagnostyczny.
    private readonly StringBuilder _log = new();
    private bool _firstRenderReported, _firstPointerReported;
    private string? _lastGlError;

    static PointCloudView3D()
    {
        // Nowa chmura => wgraj do GPU w najbliższej klatce.
        PointCloudProperty.Changed.AddClassHandler<PointCloudView3D>((view, e) =>
        {
            view._cloud = e.NewValue as PointCloud;
            view._uploadPending = true;
            view.RequestNextFrameRendering();
        });
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
            // Wyjątek z callbacka OpenGL zostałby połknięty przez Avalonię - pokazujemy go w oknie.
            Report("BŁĄD inicjalizacji OpenGL: " + ex);
        }
    }

    private void InitGl(GlInterface gl)
    {
        // Silk.NET ładuje funkcje GL przez kontekst utworzony przez Avalonię.
        _gl = GL.GetApi(gl.GetProcAddress);
        bool isEs = gl.ContextInfo.Version.Type == GlProfileType.OpenGLES;
        Report($"Init OK. OpenGL: {_gl.GetStringS(StringName.Version)} | GLSL: {_gl.GetStringS(StringName.ShadingLanguageVersion)}"
               + $" | renderer: {_gl.GetStringS(StringName.Renderer)}");

        _program = BuildProgram(_gl, ShaderHeader(isEs));
        _mvpLocation = _gl.GetUniformLocation(_program, "uMvp");
        _pointSizeLocation = _gl.GetUniformLocation(_program, "uPointSize");
        _colorLocation = _gl.GetUniformLocation(_program, "uColor");

        // VAO jest wymagane w profilu core; atrybut 0 = pozycja (3 floaty na wierzchołek).
        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        unsafe { _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), null); }
        _gl.EnableVertexAttribArray(0);

        // Rozmiar punktu z shadera (GL_PROGRAM_POINT_SIZE) istnieje tylko w desktopowym OpenGL;
        // w OpenGL ES jest zawsze włączony i próba włączenia dałaby błąd.
        if (!isEs) _gl.Enable(EnableCap.ProgramPointSize);
        _gl.Enable(EnableCap.DepthTest);

        // Po (ponownym) utworzeniu kontekstu bufor jest pusty - dane trzeba wgrać od nowa.
        _uploadPending = true;
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

        // Rozmiar w pikselach fizycznych = rozmiar logiczny * skala ekranu (Retina).
        double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        int width = Math.Max(1, (int)(Bounds.Width * scale));
        int height = Math.Max(1, (int)(Bounds.Height * scale));
        _viewportHeight = height;

        gl.Viewport(0, 0, (uint)width, (uint)height);
        gl.ClearColor(0.1f, 0.1f, 0.1f, 1f);
        gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        if (_pointCount > 0)
        {
            // System.Numerics trzyma macierze wierszowo (v * M); zapis do uniformu bez transpozycji
            // daje w GLSL (uMvp * v) ten sam wynik.
            Matrix4x4 mvp = _camera.ViewProjection((float)width / height);

            gl.UseProgram(_program);
            gl.UniformMatrix4(_mvpLocation, 1, false, (float*)&mvp);
            gl.Uniform1(_pointSizeLocation, (float)(2.0 * scale));
            gl.Uniform4(_colorLocation, 0.2f, 0.9f, 0.3f, 1f); // jednolity zielony (Faza 1)
            gl.BindVertexArray(_vao);
            gl.DrawArrays(PrimitiveType.Points, 0, (uint)_pointCount);
        }

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
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteProgram(_program);
        _gl.Dispose();
        _gl = null;
    }

    /// <summary>
    /// Wgrywa pozycje chmury do VBO (względem jej środka) i dopasowuje kamerę do rozmiaru chmury.
    /// </summary>
    private unsafe void UploadPoints(GL gl)
    {
        _uploadPending = false;
        var cloud = _cloud;
        _pointCount = cloud?.Count ?? 0;
        if (cloud is null || _pointCount == 0) return;

        Vector3 center = cloud.Center;
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

        _camera.FitTo(Vector3.Zero, cloud.Radius);
        Report($"Wgrano {_pointCount:N0} punktów, promień chmury {cloud.Radius:F1}.");
    }

    // --- Sterowanie kamerą ---

    /// <summary>
    /// Kontrolka OpenGL sama niczego nie rysuje w drzewie wizualnym Avalonii, więc bez zawartości
    /// nie bierze udziału w testowaniu trafień i nie dostaje zdarzeń myszy. Przezroczysty prostokąt
    /// sprawia, że obszar widoku jest "klikalny" (ten sam zabieg jest w widoku 2D).
    /// </summary>
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

        if (_rotating)
        {
            _camera.Rotate(-(float)d.X * 0.01f, (float)d.Y * 0.01f);
        }
        else
        {
            // Przesunięcie o d pikseli = d * (jednostki świata na piksel); tu liczone w pikselach logicznych.
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
        _camera.Zoom(MathF.Pow(0.9f, (float)e.Delta.Y));
        RequestNextFrameRendering();
    }

    // --- Diagnostyka ---

    /// <summary>Dopisuje wiersz do dziennika i publikuje go w Diagnostics na wątku UI.</summary>
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

    /// <summary>Raz zapisuje w dzienniku, że kontrolka odebrała zdarzenie myszy (diagnostyka hit testingu).</summary>
    private void ReportFirstPointerEvent(string what)
    {
        if (_firstPointerReported) return;
        _firstPointerReported = true;
        Report($"Mysz: kontrolka odebrała zdarzenie ({what}).");
    }

    /// <summary>Zgłasza błąd glGetError tylko gdy jest nowy, żeby nie zalewać dziennika.</summary>
    private void ReportGlError(GL gl, string where)
    {
        var error = gl.GetError();
        string? text = error == GLEnum.NoError ? null : $"glGetError {where}: {error}";
        if (text != null && text != _lastGlError)
            Report(text);
        _lastGlError = text;
    }

    // --- Shadery: kompilacja ---

    /// <summary>
    /// Nagłówek GLSL zależny od kontekstu: OpenGL ES (np. ANGLE na Windows) wymaga "300 es" i precyzji,
    /// desktopowy OpenGL 3.2+ (w tym macOS core) używa wersji 150.
    /// </summary>
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
