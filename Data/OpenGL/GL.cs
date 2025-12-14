using System;
using System.Runtime.InteropServices;

namespace laser_gui_test.Data.OpenGL;

public static class GL
{
    private const string LibName = "opengl32.dll";
    private const string GdiLib = "gdi32.dll";
    private const string UserLib = "user32.dll";

    // Constants
    public const uint GL_COLOR_BUFFER_BIT = 0x00004000;
    public const uint GL_LINES = 0x0001;
    public const uint GL_TRIANGLES = 0x0004;
    public const uint GL_QUADS = 0x0007;

    public const uint GL_MODELVIEW = 0x1700;
    public const uint GL_PROJECTION = 0x1701;

    public const uint GL_BLEND = 0x0BE2;
    public const uint GL_SRC_ALPHA = 0x0302;
    public const uint GL_ONE_MINUS_SRC_ALPHA = 0x0303;
    
    // Functions
    [DllImport(LibName)] public static extern void glBegin(uint mode);
    [DllImport(LibName)] public static extern void glEnd();
    [DllImport(LibName)] public static extern void glVertex2f(float x, float y);
    [DllImport(LibName)] public static extern void glColor4f(float red, float green, float blue, float alpha);
    [DllImport(LibName)] public static extern void glColor3f(float red, float green, float blue);
    [DllImport(LibName)] public static extern void glMatrixMode(uint mode);
    [DllImport(LibName)] public static extern void glLoadIdentity();
    [DllImport(LibName)] public static extern void glOrtho(double left, double right, double bottom, double top, double zNear, double zFar);
    [DllImport(LibName)] public static extern void glViewport(int x, int y, int width, int height);
    [DllImport(LibName)] public static extern void glClear(uint mask);
    [DllImport(LibName)] public static extern void glClearColor(float red, float green, float blue, float alpha);
    [DllImport(LibName)] public static extern void glEnable(uint cap);
    [DllImport(LibName)] public static extern void glDisable(uint cap);
    [DllImport(LibName)] public static extern void glBlendFunc(uint sfactor, uint dfactor);
    [DllImport(LibName)] public static extern void glLineWidth(float width);
    [DllImport(LibName)] public static extern void glTranslate(double x, double y, double z);
    [DllImport(LibName)] public static extern void glScalef(float x, float y, float z);
    
    // WGL / GDI
    [DllImport(LibName)] public static extern IntPtr wglCreateContext(IntPtr hdc);
    [DllImport(LibName)] public static extern bool wglMakeCurrent(IntPtr hdc, IntPtr hglrc);
    [DllImport(LibName)] public static extern bool wglDeleteContext(IntPtr hglrc);

    [DllImport(UserLib)] public static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport(UserLib)] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    
    [DllImport(GdiLib)] public static extern bool SwapBuffers(IntPtr hdc);
    
    [StructLayout(LayoutKind.Sequential)]
    public struct PIXELFORMATDESCRIPTOR
    {
        public ushort nSize;
        public ushort nVersion;
        public uint dwFlags;
        public byte iPixelType;
        public byte cColorBits;
        public byte cRedBits;
        public byte cRedShift;
        public byte cGreenBits;
        public byte cGreenShift;
        public byte cBlueBits;
        public byte cBlueShift;
        public byte cAlphaBits;
        public byte cAlphaShift;
        public byte cAccumBits;
        public byte cAccumRedBits;
        public byte cAccumGreenBits;
        public byte cAccumBlueBits;
        public byte cAccumAlphaBits;
        public byte cDepthBits;
        public byte cStencilBits;
        public byte cAuxBuffers;
        public byte iLayerType;
        public byte bReserved;
        public uint dwLayerMask;
        public uint dwVisibleMask;
        public uint dwDamageMask;
    }

    [DllImport(GdiLib)] public static extern int ChoosePixelFormat(IntPtr hdc, ref PIXELFORMATDESCRIPTOR ppfd);
    [DllImport(GdiLib)] public static extern bool SetPixelFormat(IntPtr hdc, int iPixelFormat, ref PIXELFORMATDESCRIPTOR ppfd);

    public const byte PFD_TYPE_RGBA = 0;
    public const uint PFD_DOUBLEBUFFER = 1;
    public const uint PFD_DRAW_TO_WINDOW = 4;
    public const uint PFD_SUPPORT_OPENGL = 32;
    public const uint PFD_MAIN_PLANE = 0;

}
