using System;
using System.ComponentModel;
using System.Windows.Forms;
using laser_gui_test.Data.OpenGL;

namespace laser_gui_test.Controls;

public class OpenGLControl : Control
{
    private IntPtr _hDC;
    private IntPtr _hRC;
    private bool _ready;

    public OpenGLControl()
    {
        SetStyle(ControlStyles.Opaque, true); 
        SetStyle(ControlStyles.UserPaint, true);
        SetStyle(ControlStyles.AllPaintingInWmPaint, true);
        this.DoubleBuffered = false; // We handle double buffering via SwapBuffers
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle = cp.ClassStyle | 0x20; // CS_OWNDC
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        InitializeOpenGL();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        _ready = false;
        
        if (_hRC != IntPtr.Zero)
        {
            GL.wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
            GL.wglDeleteContext(_hRC);
            _hRC = IntPtr.Zero;
        }

        if (_hDC != IntPtr.Zero)
        {
            GL.ReleaseDC(this.Handle, _hDC);
            _hDC = IntPtr.Zero;
        }

        base.OnHandleDestroyed(e);
    }

    private void InitializeOpenGL()
    {
        _hDC = GL.GetDC(this.Handle);

        var pfd = new GL.PIXELFORMATDESCRIPTOR
        {
            nSize = (ushort)System.Runtime.InteropServices.Marshal.SizeOf(typeof(GL.PIXELFORMATDESCRIPTOR)),
            nVersion = 1,
            dwFlags = GL.PFD_DRAW_TO_WINDOW | GL.PFD_SUPPORT_OPENGL | GL.PFD_DOUBLEBUFFER,
            iPixelType = GL.PFD_TYPE_RGBA,
            cColorBits = 32,
            cDepthBits = 24,
            iLayerType = (byte)GL.PFD_MAIN_PLANE
        };

        int format = GL.ChoosePixelFormat(_hDC, ref pfd);
        if (format == 0) return;

        if (!GL.SetPixelFormat(_hDC, format, ref pfd)) return;

        _hRC = GL.wglCreateContext(_hDC);
        if (_hRC == IntPtr.Zero) return;

        GL.wglMakeCurrent(_hDC, _hRC);
        _ready = true;
        
        // Initial setup
        GL.glClearColor(1f, 1f, 1f, 1f); // White BG
        GL.glEnable(GL.GL_BLEND);
        GL.glBlendFunc(GL.GL_SRC_ALPHA, GL.GL_ONE_MINUS_SRC_ALPHA);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_ready)
        {
            GL.wglMakeCurrent(_hDC, _hRC);
            GL.glViewport(0, 0, Width, Height);
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // Don't call base.OnPaint
        if (!_ready) return;

        GL.wglMakeCurrent(_hDC, _hRC);
        
        OnRender();
        
        GL.SwapBuffers(_hDC);
    }

    public virtual void OnRender()
    {
        GL.glClear(GL.GL_COLOR_BUFFER_BIT);
        // Default impl
    }
}
