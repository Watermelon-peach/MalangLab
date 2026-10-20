using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// A1 실험 — 투명 + 항상 위 + 클릭 통과를 Win32 API 직접 호출(P/Invoke)로 구현.
///
/// 투명 창은 에디터에서 확인되지 않고 빌드에서만 보인다. 그래서 에디터에서는
/// 창을 건드리지 않고(에디터 창에 SetWindowLong을 걸면 에디터가 망가진다),
/// 빌드에서 F1~F8로 각 기능을 켜고 끄며 되는 조합을 찾도록 만들었다.
///
/// 통과 기준: 말랑이(공) 위만 잡히고 나머지는 뒤 창이 눌림.
/// </summary>
public class OverlayWindow : MonoBehaviour
{
    public enum TransparencyMode
    {
        /// <summary>투명 처리 안 함 — 창이 떠 있다는 걸 확인하는 기준선.</summary>
        None,

        /// <summary>
        /// 빈 블러 리전을 주면 블러 없이 픽셀 단위 알파만 켜진다. 테두리 없는
        /// 창에서 쓸 수 있는 방식이고, D3D로 그리는 창에도 통한다. 기본값.
        /// </summary>
        DwmBlurBehind,

        /// <summary>
        /// 창 프레임을 클라이언트 영역까지 확장해서 알파 0을 통과시킨다.
        /// 단 확장할 프레임이 있어야 하므로 WS_POPUP + 테두리 제거와는 상성이
        /// 나쁘다. 비교용으로 남겨둔다 — F3으로 테두리를 켜고 보면 차이가 보인다.
        /// </summary>
        DwmExtendFrame,

        /// <summary>
        /// 특정 색 하나를 크로마키로 뚫는다. 알파와 무관하게 동작하므로
        /// 알파 경로가 의심될 때의 판별 장치. 경계가 각져서 말랑이에는 아쉽다.
        /// </summary>
        LayeredColorKey,
    }

    public enum WindowPlacement
    {
        /// <summary>작은 창을 주 모니터 가운데. 창 경계가 보여서 판정이 쉽다.</summary>
        SmallCentered,

        /// <summary>주 모니터 전체. 모니터 하나를 정확히 덮는다.</summary>
        FullPrimaryMonitor,
    }

    [Header("창 설정 (빌드에서 F1~F8로 토글)")]
    [SerializeField] TransparencyMode transparency = TransparencyMode.DwmBlurBehind;
    [SerializeField] WindowPlacement placement = WindowPlacement.SmallCentered;
    [SerializeField] bool alwaysOnTop = true;
    [SerializeField] bool borderless = true;
    [SerializeField] bool clickThrough = true;

    [Tooltip("알트탭 목록과 작업표시줄에서 창을 숨긴다. 상주형 오버레이에 맞는 동작.")]
    [SerializeField] bool toolWindow = false;

    [Tooltip("클릭 통과에 WS_EX_LAYERED를 함께 건다. Windows는 이게 있어야 "
           + "히트 테스트에서 창을 빼준다. 끄면 통과가 안 되는 게 정상.")]
    [SerializeField] bool useLayeredForClickThrough = true;

    [SerializeField] Vector2Int smallSize = new Vector2Int(900, 700);

    [Header("클릭 통과 히트 테스트")]
    [Tooltip("이 레이어의 콜라이더 위에서만 클릭을 받는다.")]
    [SerializeField] LayerMask hitLayers = ~0;

    [Header("표시")]
    [SerializeField] bool showHud = true;
    [SerializeField] Color colorKey = new Color32(255, 0, 255, 255);

    // ── 상태 ──────────────────────────────────────────────────────────────
    IntPtr _hwnd = IntPtr.Zero;
    IntPtr _originalStyle, _originalExStyle;
    bool _styleCached;

    bool _clickThroughActive;
    bool _cursorOnBlob;
    Vector2 _cursorUnityPos;
    Transform _hoveredBlob;
    Renderer _hoveredRenderer;
    Color _hoveredOriginalColor;
    int _clickCount;
    string _lastError = "";
    string _winRect = "";
    long _exStyleNow;

    Camera _cam;

    // ── Win32 ─────────────────────────────────────────────────────────────
    const int GWL_STYLE = -16;
    const int GWL_EXSTYLE = -20;

    const long WS_POPUP = 0x80000000L;
    const long WS_VISIBLE = 0x10000000L;
    const long WS_CAPTION = 0x00C00000L;
    const long WS_THICKFRAME = 0x00040000L;
    const long WS_SYSMENU = 0x00080000L;
    const long WS_MINIMIZEBOX = 0x00020000L;
    const long WS_MAXIMIZEBOX = 0x00010000L;

    const long WS_EX_LAYERED = 0x00080000L;
    const long WS_EX_TRANSPARENT = 0x00000020L;
    const long WS_EX_TOOLWINDOW = 0x00000080L;

    static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

    const uint SWP_NOSIZE = 0x0001;
    const uint SWP_NOMOVE = 0x0002;
    const uint SWP_NOACTIVATE = 0x0010;
    const uint SWP_FRAMECHANGED = 0x0020;

    const uint LWA_COLORKEY = 0x00000001;
    const uint LWA_ALPHA = 0x00000002;

    const uint DWM_BB_ENABLE = 0x00000001;
    const uint DWM_BB_BLURREGION = 0x00000002;

    const uint MONITOR_DEFAULTTOPRIMARY = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    struct MARGINS { public int left, right, top, bottom; }

    // fEnable은 Win32 BOOL(4바이트)이다. C# bool을 그대로 쓰면 마셜링이
    // 애매해지므로 int로 받는다.
    [StructLayout(LayoutKind.Sequential)]
    struct DWM_BLURBEHIND
    {
        public uint dwFlags;
        public int fEnable;
        public IntPtr hRgnBlur;
        public int fTransitionOnMaximized;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    // x64 전용. Unity Windows 플레이어는 x64라 SetWindowLongPtr로 충분하다.
    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtrW")]
    static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongPtrW")]
    static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
                                    int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint dwFlags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("dwmapi.dll")]
    static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

    [DllImport("dwmapi.dll")]
    static extern int DwmEnableBlurBehindWindow(IntPtr hWnd, ref DWM_BLURBEHIND pBlurBehind);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateRectRgn(int x1, int y1, int x2, int y2);

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr hObject);

    // ── 생명주기 ──────────────────────────────────────────────────────────
    void Awake()
    {
        _cam = Camera.main;
        if (_cam == null) _cam = GetComponentInChildren<Camera>();
        if (_cam == null) _cam = FindFirstObjectByType<Camera>();

        // 오버레이는 포커스를 잃어도 계속 돌아야 한다.
        Application.runInBackground = true;
    }

    void Start()
    {
#if !UNITY_EDITOR && UNITY_STANDALONE_WIN
        _hwnd = FindOwnWindow();
        if (_hwnd == IntPtr.Zero)
        {
            _lastError = "자기 창(HWND)을 찾지 못했다.";
            return;
        }

        _originalStyle = GetWindowLongPtr(_hwnd, GWL_STYLE);
        _originalExStyle = GetWindowLongPtr(_hwnd, GWL_EXSTYLE);
        _styleCached = true;

        ApplyAll();
#endif
    }

    void OnDestroy() => RestoreStyles();

    void Update()
    {
        ReadToggleKeys();
        UpdateHitTest();
        UpdateClickThroughState();
        UpdateHoverFeedback();
        CountClicks();
    }

    // ── 토글 키 ───────────────────────────────────────────────────────────
    void ReadToggleKeys()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        bool dirty = false;

        if (kb[Key.F1].wasPressedThisFrame)
        {
            transparency = (TransparencyMode)(((int)transparency + 1) % 4);
            dirty = true;
        }
        if (kb[Key.F2].wasPressedThisFrame) { alwaysOnTop = !alwaysOnTop; dirty = true; }
        if (kb[Key.F3].wasPressedThisFrame) { borderless = !borderless; dirty = true; }
        if (kb[Key.F4].wasPressedThisFrame) { clickThrough = !clickThrough; }
        if (kb[Key.F5].wasPressedThisFrame) { toolWindow = !toolWindow; dirty = true; }
        if (kb[Key.F6].wasPressedThisFrame) { showHud = !showHud; }
        if (kb[Key.F7].wasPressedThisFrame)
        {
            placement = placement == WindowPlacement.SmallCentered
                ? WindowPlacement.FullPrimaryMonitor
                : WindowPlacement.SmallCentered;
            dirty = true;
        }

        if (kb[Key.F8].wasPressedThisFrame)
        {
            useLayeredForClickThrough = !useLayeredForClickThrough;
            dirty = true;
        }

        if (kb[Key.Escape].wasPressedThisFrame) Application.Quit();

        if (dirty) ApplyAll();
    }

    // ── 창 조작 ───────────────────────────────────────────────────────────
    /// <summary>
    /// 순서가 중요하다. SetWindowLong으로 확장 스타일을 건드리면
    /// SetLayeredWindowAttributes로 걸어둔 색 키가 날아간다. 그래서
    /// 스타일을 먼저 전부 확정하고 투명 처리를 맨 마지막에 한다.
    /// (처음엔 거꾸로 해서 색 키가 통째로 무시됐다.)
    /// </summary>
    void ApplyAll()
    {
        if (_hwnd == IntPtr.Zero) return;

        _lastError = "";
        ApplyFrameStyle();
        ApplyWindowPlacement();
        ApplyCameraClearColor();
        _clickThroughActive = false;
        ApplyExStyle();        // 스타일 확정
        ApplyTransparency();   // 그 다음 투명 처리
        ApplyAlwaysOnTop();
    }

    void ApplyFrameStyle()
    {
        long style = _originalStyle.ToInt64();
        if (borderless)
        {
            style &= ~(WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX);
            style |= WS_POPUP | WS_VISIBLE;
        }
        SetWindowLongPtr(_hwnd, GWL_STYLE, new IntPtr(style));
        SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
                     SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }

    /// <summary>
    /// 창 위치·크기를 직접 정한다. 이걸 Unity에 맡겨두면 기본 해상도와
    /// 테두리 제거가 맞물려 창이 모니터 경계를 넘어가는 일이 생긴다.
    /// 주 모니터를 기준으로 계산해서 옆 모니터로 새지 않게 한다.
    /// </summary>
    void ApplyWindowPlacement()
    {
        var mon = MonitorFromWindow(_hwnd, MONITOR_DEFAULTTOPRIMARY);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
        if (!GetMonitorInfo(mon, ref mi))
        {
            _lastError = "GetMonitorInfo 실패 — 창 위치를 정하지 못했다.";
            return;
        }

        int mx = mi.rcMonitor.left;
        int my = mi.rcMonitor.top;
        int mw = mi.rcMonitor.right - mi.rcMonitor.left;
        int mh = mi.rcMonitor.bottom - mi.rcMonitor.top;

        int x, y, w, h;
        if (placement == WindowPlacement.FullPrimaryMonitor)
        {
            x = mx; y = my; w = mw; h = mh;
        }
        else
        {
            w = Mathf.Min(smallSize.x, mw);
            h = Mathf.Min(smallSize.y, mh);
            x = mx + (mw - w) / 2;
            y = my + (mh - h) / 2;
        }

        SetWindowPos(_hwnd, IntPtr.Zero, x, y, w, h, SWP_NOACTIVATE | SWP_FRAMECHANGED);

        if (GetWindowRect(_hwnd, out RECT r))
            _winRect = $"{r.left},{r.top} {r.right - r.left}x{r.bottom - r.top}";
    }

    /// <summary>
    /// 방식마다 카메라가 클리어할 색이 다르다. DWM 계열은 알파 0인 픽셀을
    /// 통과시키므로 투명 검정이어야 하고, 색 키는 그 색 자체를 뚫으므로
    /// 불투명 키 색이어야 한다. 섞이면 뚫을 게 없어서 그냥 검은 화면이 된다.
    /// </summary>
    void ApplyCameraClearColor()
    {
        if (_cam == null) return;
        _cam.clearFlags = CameraClearFlags.SolidColor;

        switch (transparency)
        {
            case TransparencyMode.DwmBlurBehind:
            case TransparencyMode.DwmExtendFrame:
                _cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                break;
            case TransparencyMode.LayeredColorKey:
                _cam.backgroundColor = new Color(colorKey.r, colorKey.g, colorKey.b, 1f);
                break;
            default:
                _cam.backgroundColor = new Color(0.15f, 0.15f, 0.18f, 1f);
                break;
        }
    }

    void ApplyTransparency()
    {
        // 앞선 방식의 흔적을 지운다 — 섞이면 왜 안 되는지 알 수 없다.
        var zero = new MARGINS { left = 0, right = 0, top = 0, bottom = 0 };
        DwmExtendFrameIntoClientArea(_hwnd, ref zero);

        var off = new DWM_BLURBEHIND
        {
            dwFlags = DWM_BB_ENABLE,
            fEnable = 0,
            hRgnBlur = IntPtr.Zero,
            fTransitionOnMaximized = 0,
        };
        DwmEnableBlurBehindWindow(_hwnd, ref off);

        // 색 키 모드를 빠져나올 때 레이어드 속성이 남는다. WS_EX_LAYERED 비트를
        // 떼는 것만으로는 지워지지 않아서, 한 번 색 키를 거친 창은 그 뒤로
        // DWM 합성이 이상해진다(공 뒤가 비쳐 보이는 증상).
        // 비트를 떼기 전에 불투명 기본값으로 되돌려 놓는다.
        if (transparency != TransparencyMode.LayeredColorKey)
        {
            long exNow = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
            if ((exNow & WS_EX_LAYERED) != 0)
            {
                SetLayeredWindowAttributes(_hwnd, 0, 255, LWA_ALPHA);
                SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr(exNow & ~WS_EX_LAYERED));
            }
        }

        switch (transparency)
        {
            case TransparencyMode.None:
                break;

            case TransparencyMode.DwmBlurBehind:
            {
                // 빈 리전(너비·높이가 음수)을 주면 블러는 적용되지 않고
                // 픽셀 단위 알파 합성만 켜진다. 이게 목적이다.
                var rgn = CreateRectRgn(0, 0, -1, -1);
                var bb = new DWM_BLURBEHIND
                {
                    dwFlags = DWM_BB_ENABLE | DWM_BB_BLURREGION,
                    fEnable = 1,
                    hRgnBlur = rgn,
                    fTransitionOnMaximized = 0,
                };
                int hr = DwmEnableBlurBehindWindow(_hwnd, ref bb);
                DeleteObject(rgn);
                if (hr != 0) _lastError = $"DwmEnableBlurBehindWindow 실패 (hr=0x{hr:X8})";
                break;
            }

            case TransparencyMode.DwmExtendFrame:
            {
                var glass = new MARGINS { left = -1, right = -1, top = -1, bottom = -1 };
                int hr = DwmExtendFrameIntoClientArea(_hwnd, ref glass);
                if (hr != 0) _lastError = $"DwmExtendFrameIntoClientArea 실패 (hr=0x{hr:X8})";
                else if (borderless) _lastError = "참고: 테두리가 없으면 확장할 프레임이 없다 (F3으로 비교)";
                break;
            }

            case TransparencyMode.LayeredColorKey:
                ReapplyColorKey();
                break;
        }
    }

    /// <summary>
    /// 색 키를 다시 건다. SetWindowLong(GWL_EXSTYLE)이 호출되면 날아가므로
    /// 확장 스타일을 건드린 뒤에는 반드시 다시 불러야 한다.
    /// </summary>
    void ReapplyColorKey()
    {
        if (_hwnd == IntPtr.Zero) return;
        if (transparency != TransparencyMode.LayeredColorKey) return;

        uint key = (uint)((Mathf.RoundToInt(colorKey.b * 255) << 16)
                        | (Mathf.RoundToInt(colorKey.g * 255) << 8)
                        |  Mathf.RoundToInt(colorKey.r * 255));
        if (!SetLayeredWindowAttributes(_hwnd, key, 255, LWA_COLORKEY))
            _lastError = $"SetLayeredWindowAttributes 실패 (err={Marshal.GetLastWin32Error()})";
    }

    void ApplyAlwaysOnTop()
    {
        SetWindowPos(_hwnd, alwaysOnTop ? HWND_TOPMOST : HWND_NOTOPMOST,
                     0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>
    /// 현재 플래그 조합을 확장 스타일에 반영한다.
    ///
    /// 클릭 통과에는 WS_EX_TRANSPARENT만 쓴다. WS_EX_LAYERED를 같이 걸면
    /// DWM 합성과 충돌해서 투명이 깨진다 — 처음에 둘을 함께 걸어서 물렸다.
    /// LAYERED는 색 키 모드에서만 필요하다.
    /// </summary>
    void ApplyExStyle()
    {
        if (_hwnd == IntPtr.Zero) return;

        long ex = _originalExStyle.ToInt64();
        bool layeredForClickThrough = false;

        if (_clickThroughActive)
        {
            ex |= WS_EX_TRANSPARENT;

            // Windows에서 마우스 통과는 WS_EX_TRANSPARENT 하나로는 안 된다.
            // WS_EX_LAYERED와 함께 걸어야 히트 테스트에서 빠진다.
            if (useLayeredForClickThrough)
            {
                ex |= WS_EX_LAYERED;
                layeredForClickThrough = true;
            }
        }

        if (toolWindow) ex |= WS_EX_TOOLWINDOW;
        if (transparency == TransparencyMode.LayeredColorKey) ex |= WS_EX_LAYERED;

        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr(ex));
        _exStyleNow = ex;

        // LAYERED를 걸었으면 속성을 한 번 넣어줘야 창이 정상적으로 합성된다.
        // 알파 255 = 불투명이라 DWM의 픽셀 단위 알파를 덮지 않는다.
        if (layeredForClickThrough && transparency != TransparencyMode.LayeredColorKey)
            SetLayeredWindowAttributes(_hwnd, 0, 255, LWA_ALPHA);

        // 확장 스타일을 바꿨으니 색 키를 다시 건다.
        ReapplyColorKey();
    }

    // ── 히트 테스트 + 클릭 통과 ───────────────────────────────────────────
    /// <summary>
    /// 커서 아래가 말랑이인지 본다.
    ///
    /// 중요: Input.mousePosition이 아니라 GetCursorPos를 쓴다. 클릭 통과가 켜진
    /// 창은 마우스 메시지를 아예 받지 못해서 Unity가 커서 위치를 모른다.
    /// 그래서 OS에 직접 물어봐야 한다 — 이게 이 실험의 핵심 함정이다.
    /// </summary>
    void UpdateHitTest()
    {
        _cursorOnBlob = false;
        _hoveredBlob = null;

        if (_cam == null) return;

        float px, py;
        int clientH;

#if !UNITY_EDITOR && UNITY_STANDALONE_WIN
        if (_hwnd == IntPtr.Zero) return;
        if (!GetCursorPos(out POINT pt)) return;
        if (!GetClientRect(_hwnd, out RECT rc)) return;
        if (!ScreenToClient(_hwnd, ref pt)) return;

        clientH = rc.bottom - rc.top;
        px = pt.x;
        py = clientH - pt.y;   // Win32는 좌상단 원점, Unity는 좌하단 원점

        if (px < 0 || py < 0 || px > (rc.right - rc.left) || py > clientH) return;
#else
        var m = Mouse.current;
        if (m == null) return;
        var mp = m.position.ReadValue();
        clientH = Screen.height;
        px = mp.x;
        py = mp.y;
        if (px < 0 || py < 0 || px > Screen.width || py > clientH) return;
#endif

        _cursorUnityPos = new Vector2(px, py);

        var ray = _cam.ScreenPointToRay(new Vector3(px, py, 0f));
        if (Physics.Raycast(ray, out RaycastHit hit, 1000f, hitLayers))
        {
            _cursorOnBlob = true;
            _hoveredBlob = hit.transform;
        }
    }

    /// <summary>
    /// 커서가 말랑이 위면 클릭을 받고, 아니면 통과시킨다.
    ///
    /// Windows는 창 단위로만 클릭 통과를 켜고 끌 수 있어서 매 프레임 토글해야
    /// 한다. 버튼을 눌렀는지와 무관하게 커서 위치만 보고 미리 바꿔두는 게
    /// 요점이다. 클릭이 들어온 다음에 바꾸면 이미 뒤 창이 눌린 뒤다.
    /// 그래도 한 프레임의 지연은 남는다 — 60fps면 16ms.
    /// </summary>
    void UpdateClickThroughState()
    {
        if (_hwnd == IntPtr.Zero) return;

        bool shouldPassThrough = clickThrough && !_cursorOnBlob;
        if (shouldPassThrough == _clickThroughActive) return;

        _clickThroughActive = shouldPassThrough;
        ApplyExStyle();
    }

    void UpdateHoverFeedback()
    {
        Renderer r = _hoveredBlob != null ? _hoveredBlob.GetComponent<Renderer>() : null;

        if (!ReferenceEquals(r, _hoveredRenderer))
        {
            if (_hoveredRenderer != null)
                _hoveredRenderer.material.color = _hoveredOriginalColor;

            _hoveredRenderer = r;
            if (_hoveredRenderer != null)
                _hoveredOriginalColor = _hoveredRenderer.material.color;
        }

        if (_hoveredRenderer != null)
            _hoveredRenderer.material.color =
                Color.Lerp(_hoveredOriginalColor, Color.white, 0.45f);
    }

    /// <summary>
    /// 클릭이 실제로 Unity까지 들어오는지 센다. 클릭 통과가 제대로 꺼져야만
    /// 이 숫자가 올라간다 — 통과 기준을 눈으로 확인하는 장치.
    /// </summary>
    void CountClicks()
    {
        var m = Mouse.current;
        if (m != null && m.leftButton.wasPressedThisFrame) _clickCount++;
    }

    // ── 정리 ──────────────────────────────────────────────────────────────
    void RestoreStyles()
    {
        if (_hwnd == IntPtr.Zero || !_styleCached) return;
        SetWindowLongPtr(_hwnd, GWL_STYLE, _originalStyle);
        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, _originalExStyle);
        SetWindowPos(_hwnd, HWND_NOTOPMOST, 0, 0, 0, 0,
                     SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }

    /// <summary>
    /// 자기 프로세스의 보이는 최상위 창을 찾는다. 창 제목으로 찾는 방식
    /// (FindWindow + productName)은 제목이 바뀌면 깨지므로 프로세스 ID로 맞춘다.
    /// </summary>
    static IntPtr FindOwnWindow()
    {
        uint self = (uint)Process.GetCurrentProcess().Id;
        IntPtr found = IntPtr.Zero;

        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == self && IsWindowVisible(hWnd))
            {
                found = hWnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);

        return found;
    }

    // ── 화면 표시 ─────────────────────────────────────────────────────────
    // 빌드에서 인스펙터를 볼 수 없으니 상태를 화면에 띄운다.
    // 기본 GUI 폰트에 한글 글리프가 없어 네모로 깨지므로 영문으로 적는다.
    void OnGUI()
    {
        if (!showHud) return;

        // IMGUI는 URP 렌더링이 끝난 뒤 백버퍼에 직접 그려지므로 프리멀티플라이
        // 패스를 거치지 않는다. 반투명으로 두면 DWM이 보정 안 된 알파를 받아
        // "왜 여기만 이상하지"로 시간을 버린다. HUD는 계측 장치이니 불투명으로.
        var box = new Rect(16, 16, 490, 370);
        GUI.color = new Color(0.04f, 0.04f, 0.06f, 1f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = Color.white;

        var st = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = false };
        st.normal.textColor = Color.white;

        GUILayout.BeginArea(new Rect(box.x + 12, box.y + 10, box.width - 24, box.height - 20));

        var head = new GUIStyle(st) { fontSize = 17, fontStyle = FontStyle.Bold };
        GUILayout.Label("A1 - Overlay experiment", head);
        GUILayout.Space(4);

        GUILayout.Label($"F1 Transparency  : {transparency}", st);
        GUILayout.Label($"F2 Always on top : {alwaysOnTop}", st);
        GUILayout.Label($"F3 Borderless    : {borderless}", st);
        GUILayout.Label($"F4 Click-through : {clickThrough}", st);
        GUILayout.Label($"F5 Tool window   : {toolWindow}", st);
        GUILayout.Label($"F7 Placement     : {placement}", st);
        GUILayout.Label($"F8 Layered for CT: {useLayeredForClickThrough}", st);
        GUILayout.Label("F6 Hide HUD       ESC Quit", st);
        GUILayout.Space(8);

        // 창에서 플래그를 직접 읽어온다. 우리가 걸었다고 믿는 것과 실제가
        // 다를 수 있어서, 눈으로 확인할 수 있어야 한다.
        long exLive = 0;
        if (_hwnd != IntPtr.Zero) exLive = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
        string flags = $"LAYERED={((exLive & WS_EX_LAYERED) != 0 ? "Y" : "n")} "
                     + $"TRANSPARENT={((exLive & WS_EX_TRANSPARENT) != 0 ? "Y" : "n")} "
                     + $"TOOLWINDOW={((exLive & WS_EX_TOOLWINDOW) != 0 ? "Y" : "n")}";

        GUILayout.Label($"exStyle (live)   : {flags}", st);
        GUILayout.Label($"window {_winRect}   screen {Screen.width}x{Screen.height}", st);
        GUILayout.Label($"cursor {_cursorUnityPos.x:F0},{_cursorUnityPos.y:F0}   "
                      + $"on blob: {(_cursorOnBlob ? "YES" : "no")}   "
                      + $"passing: {(_clickThroughActive ? "YES" : "no")}", st);

        // 통과 기준을 눈으로 확인하는 핵심 숫자이므로 크고 눈에 띄게.
        var big = new GUIStyle(st) { fontSize = 20, fontStyle = FontStyle.Bold };
        big.normal.textColor = _clickCount > 0
            ? new Color(0.5f, 1f, 0.6f)
            : new Color(1f, 0.85f, 0.4f);
        GUILayout.Space(4);
        GUILayout.Label($"CLICKS ON BLOB: {_clickCount}", big);

        if (!string.IsNullOrEmpty(_lastError))
        {
            var err = new GUIStyle(st) { wordWrap = true };
            err.normal.textColor = new Color(1f, 0.6f, 0.4f);
            GUILayout.Label(_lastError, err);
        }

        GUILayout.EndArea();
    }
}
