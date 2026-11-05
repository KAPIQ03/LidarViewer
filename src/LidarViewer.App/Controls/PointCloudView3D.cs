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

    private GL? _gl;
    private bool _initOk;
    private uint _program, _vao, _vbo;
    private int _mvpLocation, _pointSizeLocation, _colorLocation;

    private PointCloud? _cloud;
    private bool _uploadPending;
    private int _pointCount;
    private readonly OrbitCamera _camera = new();
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
        _colorLocation = _gl.GetUniformLocation(_program, "uColor");

        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        unsafe { _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), null); }
        _gl.EnableVertexAttribArray(0);

        if (!isEs) _gl.Enable(EnableCap.ProgramPointSize);
        _gl.Enable(EnableCap.DepthTest);

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

        double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        int width = Math.Max(1, (int)(Bounds.Width * scale));
        int height = Math.Max(1, (int)(Bounds.Height * scale));
        _viewportHeight = height;

        gl.Viewport(0, 0, (uint)width, (uint)height);
        gl.ClearColor(0.1f, 0.1f, 0.1f, 1f);
        gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        if (_pointCount > 0)
        {
            Matrix4x4 mvp = _camera.ViewProjection((float)width / height);

            gl.UseProgram(_program);
            gl.UniformMatrix4(_mvpLocation, 1, false, (float*)&mvp);
            gl.Uniform1(_pointSizeLocation, (float)(2.0 * scale));
            gl.Uniform4(_colorLocation, 0.2f, 0.9f, 0.3f, 1f);
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
