using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace AntigravitySakuraMapleOverlay
{
    public enum ThemeModeOption
    {
        Auto = 0,
        Maple = 1,
        Sakura = 2,
        Mixed = 3
    }

    public enum SeasonKind
    {
        Maple = 0,
        Sakura = 1,
        Mixed = 2
    }

    public enum UiLanguage
    {
        ZhTw = 0,
        En = 1
    }

    internal static class LanguagePrefs
    {
        private const string LangFileName = "overlay_lang.txt";

        public static UiLanguage LoadLanguage(string baseDir)
        {
            try
            {
                string path = Path.Combine(baseDir, LangFileName);
                if (File.Exists(path))
                {
                    string raw = File.ReadAllText(path, new UTF8Encoding(false)).Trim();
                    if (string.Equals(raw, "en", StringComparison.OrdinalIgnoreCase))
                    {
                        return UiLanguage.En;
                    }
                    if (string.Equals(raw, "zh", StringComparison.OrdinalIgnoreCase))
                    {
                        return UiLanguage.ZhTw;
                    }
                }
            }
            catch
            {
            }

            try
            {
                if (CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                {
                    return UiLanguage.ZhTw;
                }
            }
            catch
            {
            }
            return UiLanguage.En;
        }

        public static void SaveLanguage(string baseDir, UiLanguage lang)
        {
            try
            {
                string path = Path.Combine(baseDir, LangFileName);
                string val = (lang == UiLanguage.ZhTw) ? "zh" : "en";
                File.WriteAllText(path, val, new UTF8Encoding(false));
            }
            catch
            {
            }
        }
    }

    internal static class UiStrings
    {
        public static string FormatTooltip(UiLanguage lang, SeasonKind season, bool paused)
        {
            string text;
            if (lang == UiLanguage.En)
            {
                string seasonStr = (season == SeasonKind.Sakura)
                    ? "Sakura"
                    : ((season == SeasonKind.Mixed) ? "Mixed" : "Maple");
                string status = paused ? " (Paused)" : "";
                text = string.Format("Antigravity Sakura & Maple - {0}{1}", seasonStr, status);
            }
            else
            {
                string seasonStr = (season == SeasonKind.Sakura)
                    ? "櫻花"
                    : ((season == SeasonKind.Mixed) ? "櫻楓共舞" : "楓葉");
                string status = paused ? " (已暫停)" : "";
                text = string.Format("Antigravity 櫻花＆楓葉特效 - {0}{1}", seasonStr, status);
            }
            if (text.Length > 63)
            {
                text = text.Substring(0, 63);
            }
            return text;
        }
    }

    internal static class NativeMethods
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_TOPMOST = 0x00000008;
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        public const int WM_MOUSEACTIVATE = 0x0021;
        public const int MA_NOACTIVATE = 3;
        public const int WM_NCHITTEST = 0x0084;
        public const int HTTRANSPARENT = -1;

        public const uint GW_HWNDPREV = 3;
        public const uint GW_OWNER = 4;

        public const int SW_HIDE = 0;
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;

        public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

        public static readonly IntPtr HWND_TOP = IntPtr.Zero;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        public static extern IntPtr SetProcessDpiAwarenessContext(IntPtr value);
    }

    internal sealed class ThemeWatcher : IDisposable
    {
        private static readonly Regex ThemeModeRegex = new Regex("\"themeMode\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.Compiled);
        private readonly string configPath;
        private FileSystemWatcher watcher;
        private DateTime lastWriteUtc = DateTime.MinValue;
        private string lastRawThemeMode = "";
        private SeasonKind lastAutoSeason = SeasonKind.Maple;

        public event Action AutoSeasonChanged;

        public ThemeWatcher(string configFilePath)
        {
            this.configPath = configFilePath;
            RefreshFromDisk(false);
            StartWatcher();
        }

        public string LastRawThemeMode
        {
            get { return lastRawThemeMode; }
        }

        public SeasonKind CurrentAutoSeason
        {
            get { return lastAutoSeason; }
        }

        private void StartWatcher()
        {
            try
            {
                string dir = Path.GetDirectoryName(configPath);
                string file = Path.GetFileName(configPath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    watcher = new FileSystemWatcher(dir, file);
                    watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size | NotifyFilters.FileName;
                    watcher.Changed += OnFileEvent;
                    watcher.Created += OnFileEvent;
                    watcher.Renamed += OnRenamedEvent;
                    watcher.EnableRaisingEvents = true;
                }
            }
            catch
            {
                // Fallback polling handles environments where FileSystemWatcher cannot attach
            }
        }

        private void OnFileEvent(object sender, FileSystemEventArgs e)
        {
            RefreshFromDisk(true);
        }

        private void OnRenamedEvent(object sender, RenamedEventArgs e)
        {
            RefreshFromDisk(true);
        }

        public void PollIfChanged()
        {
            try
            {
                if (!File.Exists(configPath))
                {
                    return;
                }
                DateTime wt = File.GetLastWriteTimeUtc(configPath);
                if (wt != lastWriteUtc)
                {
                    RefreshFromDisk(true);
                }
            }
            catch
            {
                // Ignore transient file lock
            }
        }

        public void RefreshFromDisk(bool raiseEvent)
        {
            string rawMode = "";
            try
            {
                if (File.Exists(configPath))
                {
                    lastWriteUtc = File.GetLastWriteTimeUtc(configPath);
                    string text;
                    using (FileStream fs = new FileStream(configPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (StreamReader sr = new StreamReader(fs, Encoding.UTF8))
                    {
                        text = sr.ReadToEnd();
                    }
                    Match m = ThemeModeRegex.Match(text);
                    if (m.Success)
                    {
                        rawMode = m.Groups[1].Value;
                    }
                }
            }
            catch
            {
                // Ignore transient read error during atomic save
            }

            SeasonKind resolved = ResolveSeason(rawMode);
            bool changed = (resolved != lastAutoSeason) || (rawMode != lastRawThemeMode);
            lastRawThemeMode = rawMode;
            lastAutoSeason = resolved;
            if (changed && raiseEvent && AutoSeasonChanged != null)
            {
                AutoSeasonChanged();
            }
        }

        private static SeasonKind ResolveSeason(string rawMode)
        {
            if (string.Equals(rawMode, "THEME_MODE_LIGHT", StringComparison.OrdinalIgnoreCase))
            {
                return SeasonKind.Sakura;
            }
            if (string.Equals(rawMode, "THEME_MODE_DARK", StringComparison.OrdinalIgnoreCase))
            {
                return SeasonKind.Maple;
            }
            if (string.Equals(rawMode, "THEME_MODE_INHERIT", StringComparison.OrdinalIgnoreCase))
            {
                return IsSystemLightTheme() ? SeasonKind.Sakura : SeasonKind.Maple;
            }
            return SeasonKind.Maple;
        }

        private static bool IsSystemLightTheme()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false))
                {
                    if (key != null)
                    {
                        object val = key.GetValue("AppsUseLightTheme");
                        if (val is int)
                        {
                            return ((int)val) == 1;
                        }
                    }
                }
            }
            catch
            {
            }
            return false;
        }

        public void Dispose()
        {
            if (watcher != null)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
                watcher = null;
            }
        }
    }

    internal struct LeafParticle
    {
        public double X;
        public double Y;
        public double Vy;
        public double Vx;
        public double Size;
        public double Angle;
        public double Spin;
        public double Flutter;
        public double FlutterSpeed;
        public double SwayPhase;
        public double SwayFreq;
        public double SwayAmp;
        public int Layer;
        public int Variant;
        public bool IsSakura;
    }

    internal sealed class EffectCanvas : FrameworkElement
    {
        private const int MaxParticles = 55;
        private readonly Random rng = new Random();
        private readonly LeafParticle[] particles = new LeafParticle[MaxParticles];

        private BitmapImage mapleBackdrop;
        private BitmapImage sakuraBackdrop;

        private StreamGeometry sakuraPetalGeo;
        private StreamGeometry mapleLeafGeo;
        private Brush[] sakuraBrushes;
        private Brush[] mapleBrushes;
        private Pen sakuraStroke;
        private Pen mapleStroke;

        private RadialGradientBrush mapleCornerGlow;
        private RadialGradientBrush sakuraCornerGlow;

        private SeasonKind activeSeason = SeasonKind.Maple;
        private double backgroundOpacity = 0.55;
        private int targetParticleCount = 34;
        private bool isPaused = false;
        private bool assetsLoaded = false;

        private double simTime = 0.0;
        private double lastWidth = 1280.0;
        private double lastHeight = 800.0;

        public EffectCanvas(string assetsDir)
        {
            this.IsHitTestVisible = false;
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
            BuildGeometriesAndBrushes();
            LoadBackdrops(assetsDir);
            InitParticles(1280.0, 800.0);
        }

        public BitmapImage MapleBackdrop
        {
            get { return mapleBackdrop; }
        }

        public BitmapImage SakuraBackdrop
        {
            get { return sakuraBackdrop; }
        }

        public StreamGeometry SakuraPetalGeometry
        {
            get { return sakuraPetalGeo; }
        }

        public StreamGeometry MapleLeafGeometry
        {
            get { return mapleLeafGeo; }
        }

        public SeasonKind ActiveSeason
        {
            get { return activeSeason; }
            set
            {
                if (activeSeason != value)
                {
                    activeSeason = value;
                    ReassignParticleSeasons();
                    InvalidateVisual();
                }
            }
        }

        public double BackgroundOpacity
        {
            get { return backgroundOpacity; }
            set
            {
                backgroundOpacity = Math.Max(0.0, Math.Min(1.0, value));
                InvalidateVisual();
            }
        }

        public int TargetParticleCount
        {
            get { return targetParticleCount; }
            set
            {
                targetParticleCount = Math.Max(0, Math.Min(MaxParticles, value));
                InvalidateVisual();
            }
        }

        public bool IsPaused
        {
            get { return isPaused; }
            set
            {
                isPaused = value;
                InvalidateVisual();
            }
        }

        private void LoadBackdrops(string assetsDir)
        {
            if (assetsLoaded)
            {
                return;
            }
            assetsLoaded = true;
            mapleBackdrop = LoadFrozenBitmap(Path.Combine(assetsDir, "maple_bg_dark.png"));
            sakuraBackdrop = LoadFrozenBitmap(Path.Combine(assetsDir, "sakura_bg_light.png"));
        }

        private static BitmapImage LoadFrozenBitmap(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }
            try
            {
                BitmapImage bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        private void BuildGeometriesAndBrushes()
        {
            // Notched Japanese Sakura Petal (normalized around ~[-10..10, -13..13])
            StreamGeometry sgSakura = new StreamGeometry();
            using (StreamGeometryContext ctx = sgSakura.Open())
            {
                ctx.BeginFigure(new Point(0.0, 12.5), true, true);
                ctx.BezierTo(new Point(-6.5, 8.0), new Point(-9.5, -1.5), new Point(-5.2, -10.5), true, true);
                ctx.BezierTo(new Point(-3.0, -12.6), new Point(-1.4, -11.8), new Point(0.0, -8.2), true, true);
                ctx.BezierTo(new Point(1.4, -11.8), new Point(3.0, -12.6), new Point(5.2, -10.5), true, true);
                ctx.BezierTo(new Point(9.5, -1.5), new Point(6.5, 8.0), new Point(0.0, 12.5), true, true);
            }
            sgSakura.Freeze();
            sakuraPetalGeo = sgSakura;

            // 5-Lobed Japanese Maple Leaf (Momiji) with stem (normalized around ~[-15..15, -15..16])
            StreamGeometry sgMaple = new StreamGeometry();
            using (StreamGeometryContext ctx = sgMaple.Open())
            {
                ctx.BeginFigure(new Point(-0.7, 15.5), true, true);
                ctx.LineTo(new Point(-0.7, 8.2), true, false);
                ctx.LineTo(new Point(-5.8, 10.6), true, false);
                ctx.LineTo(new Point(-4.4, 7.6), true, false);
                ctx.LineTo(new Point(-11.2, 8.8), true, false);
                ctx.LineTo(new Point(-6.5, 4.4), true, false);
                ctx.LineTo(new Point(-14.6, 1.6), true, false);
                ctx.LineTo(new Point(-7.8, -0.8), true, false);
                ctx.LineTo(new Point(-11.8, -5.8), true, false);
                ctx.LineTo(new Point(-4.8, -3.8), true, false);
                ctx.LineTo(new Point(-5.4, -11.2), true, false);
                ctx.LineTo(new Point(-2.2, -6.8), true, false);
                ctx.LineTo(new Point(0.0, -15.2), true, false);
                ctx.LineTo(new Point(2.2, -6.8), true, false);
                ctx.LineTo(new Point(5.4, -11.2), true, false);
                ctx.LineTo(new Point(4.8, -3.8), true, false);
                ctx.LineTo(new Point(11.8, -5.8), true, false);
                ctx.LineTo(new Point(7.8, -0.8), true, false);
                ctx.LineTo(new Point(14.6, 1.6), true, false);
                ctx.LineTo(new Point(6.5, 4.4), true, false);
                ctx.LineTo(new Point(11.2, 8.8), true, false);
                ctx.LineTo(new Point(4.4, 7.6), true, false);
                ctx.LineTo(new Point(5.8, 10.6), true, false);
                ctx.LineTo(new Point(0.7, 8.2), true, false);
                ctx.LineTo(new Point(0.7, 15.5), true, false);
            }
            sgMaple.Freeze();
            mapleLeafGeo = sgMaple;

            sakuraBrushes = new Brush[]
            {
                MakeLinearBrush(Color.FromArgb(238, 255, 224, 236), Color.FromArgb(238, 244, 155, 184)),
                MakeLinearBrush(Color.FromArgb(238, 255, 236, 243), Color.FromArgb(238, 234, 125, 164)),
                MakeLinearBrush(Color.FromArgb(238, 255, 244, 248), Color.FromArgb(238, 247, 181, 204))
            };
            mapleBrushes = new Brush[]
            {
                MakeLinearBrush(Color.FromArgb(242, 255, 134, 92), Color.FromArgb(242, 212, 61, 43)),
                MakeLinearBrush(Color.FromArgb(242, 249, 173, 92), Color.FromArgb(242, 222, 88, 51)),
                MakeLinearBrush(Color.FromArgb(242, 232, 79, 65), Color.FromArgb(242, 163, 33, 35)),
                MakeLinearBrush(Color.FromArgb(242, 246, 202, 110), Color.FromArgb(242, 220, 123, 55))
            };

            sakuraStroke = new Pen(new SolidColorBrush(Color.FromArgb(95, 190, 62, 107)), 0.7);
            sakuraStroke.Freeze();
            mapleStroke = new Pen(new SolidColorBrush(Color.FromArgb(95, 92, 24, 16)), 0.7);
            mapleStroke.Freeze();

            mapleCornerGlow = new RadialGradientBrush(
                Color.FromArgb(34, 234, 108, 77),
                Color.FromArgb(0, 243, 174, 109));
            mapleCornerGlow.Freeze();

            sakuraCornerGlow = new RadialGradientBrush(
                Color.FromArgb(32, 244, 166, 190),
                Color.FromArgb(0, 190, 62, 107));
            sakuraCornerGlow.Freeze();
        }

        private static LinearGradientBrush MakeLinearBrush(Color topColor, Color bottomColor)
        {
            LinearGradientBrush b = new LinearGradientBrush(topColor, bottomColor, new Point(0.2, 0.0), new Point(0.8, 1.0));
            b.Freeze();
            return b;
        }

        private void InitParticles(double width, double height)
        {
            for (int i = 0; i < MaxParticles; i++)
            {
                particles[i] = CreateParticle(width, height, true, i);
            }
        }

        private void ReassignParticleSeasons()
        {
            for (int i = 0; i < MaxParticles; i++)
            {
                if (activeSeason == SeasonKind.Sakura)
                {
                    particles[i].IsSakura = true;
                }
                else if (activeSeason == SeasonKind.Maple)
                {
                    particles[i].IsSakura = false;
                }
                else
                {
                    particles[i].IsSakura = (i % 2 == 0);
                }
            }
        }

        private LeafParticle CreateParticle(double width, double height, bool randomY, int index)
        {
            int layer = index % 3; // 0 = far, 1 = mid, 2 = near
            double scaleBase = (layer == 0) ? 0.56 : ((layer == 1) ? 0.80 : 1.05);
            double speedBase = (layer == 0) ? 30.0 : ((layer == 1) ? 48.0 : 68.0);

            LeafParticle p = new LeafParticle();
            p.Layer = layer;
            p.X = rng.NextDouble() * Math.Max(400.0, width);
            p.Y = randomY ? (rng.NextDouble() * Math.Max(300.0, height)) : (-28.0 - rng.NextDouble() * 80.0);
            p.Vy = speedBase * (0.82 + rng.NextDouble() * 0.42);
            p.Vx = -8.0 - rng.NextDouble() * 14.0;
            p.Size = scaleBase * (0.85 + rng.NextDouble() * 0.35);
            p.Angle = rng.NextDouble() * 360.0;
            p.Spin = (rng.NextDouble() - 0.5) * (layer == 2 ? 75.0 : 52.0);
            p.Flutter = rng.NextDouble() * Math.PI * 2.0;
            p.FlutterSpeed = 1.6 + rng.NextDouble() * 2.2;
            p.SwayPhase = rng.NextDouble() * Math.PI * 2.0;
            p.SwayFreq = 0.8 + rng.NextDouble() * 1.1;
            p.SwayAmp = 14.0 + rng.NextDouble() * 24.0;
            p.Variant = rng.Next(12);
            if (activeSeason == SeasonKind.Sakura)
            {
                p.IsSakura = true;
            }
            else if (activeSeason == SeasonKind.Maple)
            {
                p.IsSakura = false;
            }
            else
            {
                p.IsSakura = (index % 2 == 0);
            }
            return p;
        }

        public void StepSimulation(double dt)
        {
            double w = ActualWidth > 10.0 ? ActualWidth : lastWidth;
            double h = ActualHeight > 10.0 ? ActualHeight : lastHeight;
            lastWidth = w;
            lastHeight = h;

            if (dt <= 0.0 || dt > 0.1)
            {
                dt = 0.016;
            }
            simTime += dt;

            double breeze = Math.Sin(simTime * 0.45) * 14.0 + Math.Cos(simTime * 0.19) * 8.0;

            for (int i = 0; i < targetParticleCount; i++)
            {
                LeafParticle p = particles[i];
                double sway = Math.Sin(simTime * p.SwayFreq + p.SwayPhase) * p.SwayAmp;
                p.X += (p.Vx + breeze + sway) * dt;
                p.Y += p.Vy * dt;
                p.Angle += p.Spin * dt;
                p.Flutter += p.FlutterSpeed * dt;

                if (p.Y > h + 36.0)
                {
                    p = CreateParticle(w, h, false, i);
                }
                else
                {
                    if (p.X < -40.0)
                    {
                        p.X = w + 35.0;
                    }
                    else if (p.X > w + 40.0)
                    {
                        p.X = -35.0;
                    }
                }
                particles[i] = p;
            }
        }

        public LeafParticle[] GetParticlesSnapshot()
        {
            LeafParticle[] copy = new LeafParticle[targetParticleCount];
            for (int i = 0; i < targetParticleCount; i++)
            {
                copy[i] = particles[i];
            }
            return copy;
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 1.0 || h <= 1.0)
            {
                return;
            }
            lastWidth = w;
            lastHeight = h;

            if (isPaused)
            {
                return;
            }

            // 1. Ambient seasonal corner glow + 2. Corner botanical artwork
            if (backgroundOpacity > 0.01)
            {
                if (activeSeason == SeasonKind.Maple || activeSeason == SeasonKind.Mixed)
                {
                    double op = (activeSeason == SeasonKind.Mixed) ? (backgroundOpacity * 0.6) : backgroundOpacity;
                    dc.PushOpacity(op);
                    dc.DrawEllipse(mapleCornerGlow, null, new Point(w * 0.94, h * 0.06), w * 0.24, h * 0.26);
                    dc.DrawEllipse(mapleCornerGlow, null, new Point(w * 0.06, h * 0.94), w * 0.24, h * 0.26);
                    if (mapleBackdrop != null)
                    {
                        dc.DrawImage(mapleBackdrop, new Rect(0.0, 0.0, w, h));
                    }
                    dc.Pop();
                }

                if (activeSeason == SeasonKind.Sakura || activeSeason == SeasonKind.Mixed)
                {
                    double op = (activeSeason == SeasonKind.Mixed) ? (backgroundOpacity * 0.6) : backgroundOpacity;
                    dc.PushOpacity(op);
                    dc.DrawEllipse(sakuraCornerGlow, null, new Point(w * 0.94, h * 0.06), w * 0.24, h * 0.26);
                    dc.DrawEllipse(sakuraCornerGlow, null, new Point(w * 0.06, h * 0.94), w * 0.24, h * 0.26);
                    if (sakuraBackdrop != null)
                    {
                        dc.DrawImage(sakuraBackdrop, new Rect(0.0, 0.0, w, h));
                    }
                    dc.Pop();
                }
            }

            // 3. Falling particles (3 parallax layers)
            for (int i = 0; i < targetParticleCount; i++)
            {
                LeafParticle p = particles[i];
                double layerAlpha = (p.Layer == 0) ? 0.56 : ((p.Layer == 1) ? 0.78 : 0.94);

                // Gently attenuate opacity in center reading area so chat text stays effortless to read
                double nx = (p.X / w - 0.5) * 2.0;
                double ny = (p.Y / h - 0.5) * 2.0;
                double ell = Math.Sqrt((nx * nx) / (0.68 * 0.68) + (ny * ny) / (0.64 * 0.64));
                double centerAtten = 1.0;
                if (ell < 1.0)
                {
                    centerAtten = 0.50;
                }
                else if (ell < 1.45)
                {
                    double t = (ell - 1.0) / 0.45;
                    centerAtten = 0.50 + 0.50 * t;
                }

                double finalOpacity = layerAlpha * centerAtten;
                double flutterScale = 0.32 + 0.68 * Math.Abs(Math.Cos(p.Flutter));

                TransformGroup tg = new TransformGroup();
                tg.Children.Add(new ScaleTransform(p.Size * flutterScale, p.Size));
                tg.Children.Add(new RotateTransform(p.Angle));
                tg.Children.Add(new TranslateTransform(p.X, p.Y));

                dc.PushOpacity(finalOpacity);
                dc.PushTransform(tg);

                if (p.IsSakura)
                {
                    Brush b = sakuraBrushes[p.Variant % sakuraBrushes.Length];
                    dc.DrawGeometry(b, sakuraStroke, sakuraPetalGeo);
                }
                else
                {
                    Brush b = mapleBrushes[p.Variant % mapleBrushes.Length];
                    dc.DrawGeometry(b, mapleStroke, mapleLeafGeo);
                }

                dc.Pop();
                dc.Pop();
            }
        }
    }

    internal sealed class OverlayWindow : Window
    {
        private IntPtr hwnd = IntPtr.Zero;

        public OverlayWindow(EffectCanvas canvas)
        {
            this.Title = "Antigravity Sakura & Maple Overlay";
            this.WindowStyle = WindowStyle.None;
            this.AllowsTransparency = true;
            this.Background = Brushes.Transparent;
            this.ResizeMode = ResizeMode.NoResize;
            this.ShowInTaskbar = false;
            this.ShowActivated = false;
            this.Topmost = false;
            this.IsHitTestVisible = false;
            this.Left = -32000;
            this.Top = -32000;
            this.Width = 1;
            this.Height = 1;
            this.Content = canvas;
        }

        public IntPtr Handle
        {
            get { return hwnd; }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            hwnd = new WindowInteropHelper(this).Handle;
            int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            exStyle |= NativeMethods.WS_EX_TRANSPARENT
                | NativeMethods.WS_EX_LAYERED
                | NativeMethods.WS_EX_TOOLWINDOW
                | NativeMethods.WS_EX_NOACTIVATE;
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle);

            HwndSource src = HwndSource.FromHwnd(hwnd);
            if (src != null)
            {
                src.AddHook(WndProc);
            }
        }

        private IntPtr WndProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_MOUSEACTIVATE)
            {
                handled = true;
                return new IntPtr(NativeMethods.MA_NOACTIVATE);
            }
            if (msg == NativeMethods.WM_NCHITTEST)
            {
                handled = true;
                return new IntPtr(NativeMethods.HTTRANSPARENT);
            }
            return IntPtr.Zero;
        }
    }

    internal sealed class WindowTracker
    {
        private readonly OverlayWindow overlay;
        private readonly int selfPid;
        private readonly Dictionary<int, bool> pidCache = new Dictionary<int, bool>();
        private DateTime lastCacheFlush = DateTime.UtcNow;

        private bool isVisible = false;
        private int lastX = int.MinValue;
        private int lastY = int.MinValue;
        private int lastW = int.MinValue;
        private int lastH = int.MinValue;

        public WindowTracker(OverlayWindow overlayWindow)
        {
            this.overlay = overlayWindow;
            this.selfPid = Process.GetCurrentProcess().Id;
        }

        public bool IsOverlayVisible
        {
            get { return isVisible; }
        }

        public void SyncToTarget()
        {
            IntPtr overlayHwnd = overlay.Handle;
            if (overlayHwnd == IntPtr.Zero)
            {
                return;
            }

            if ((DateTime.UtcNow - lastCacheFlush).TotalSeconds >= 3.0)
            {
                pidCache.Clear();
                lastCacheFlush = DateTime.UtcNow;
            }

            IntPtr targetHwnd = IntPtr.Zero;
            NativeMethods.RECT targetRect = new NativeMethods.RECT();

            NativeMethods.EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
            {
                if (hWnd == overlayHwnd)
                {
                    return true;
                }
                if (!NativeMethods.IsWindowVisible(hWnd) || NativeMethods.IsIconic(hWnd))
                {
                    return true;
                }
                if (NativeMethods.GetWindow(hWnd, NativeMethods.GW_OWNER) != IntPtr.Zero)
                {
                    return true;
                }

                StringBuilder sb = new StringBuilder(64);
                NativeMethods.GetClassName(hWnd, sb, sb.Capacity);
                string cls = sb.ToString();
                if (!cls.StartsWith("Chrome_WidgetWin", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                uint pidVal;
                NativeMethods.GetWindowThreadProcessId(hWnd, out pidVal);
                int pid = (int)pidVal;
                if (pid == selfPid || pid == 0)
                {
                    return true;
                }

                if (!IsAntigravityDesktopProcess(pid))
                {
                    return true;
                }

                NativeMethods.RECT r;
                int hr = NativeMethods.DwmGetWindowAttribute(
                    hWnd,
                    NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
                    out r,
                    Marshal.SizeOf(typeof(NativeMethods.RECT)));
                if (hr != 0)
                {
                    if (!NativeMethods.GetWindowRect(hWnd, out r))
                    {
                        return true;
                    }
                }

                int w = r.Right - r.Left;
                int h = r.Bottom - r.Top;
                if (w < 300 || h < 200)
                {
                    return true;
                }

                targetHwnd = hWnd;
                targetRect = r;
                return false;
            }, IntPtr.Zero);

            if (targetHwnd == IntPtr.Zero)
            {
                if (isVisible)
                {
                    NativeMethods.ShowWindow(overlayHwnd, NativeMethods.SW_HIDE);
                    isVisible = false;
                }
                return;
            }

            int width = targetRect.Right - targetRect.Left;
            int height = targetRect.Bottom - targetRect.Top;

            IntPtr prev = NativeMethods.GetWindow(targetHwnd, NativeMethods.GW_HWNDPREV);
            bool zOrderOk = (prev == overlayHwnd);
            bool boundsChanged = (targetRect.Left != lastX) || (targetRect.Top != lastY) || (width != lastW) || (height != lastH);

            if (!isVisible || boundsChanged || !zOrderOk)
            {
                IntPtr insertAfter = NativeMethods.HWND_TOP;
                if (!zOrderOk && prev != IntPtr.Zero)
                {
                    int prevEx = NativeMethods.GetWindowLong(prev, NativeMethods.GWL_EXSTYLE);
                    if ((prevEx & NativeMethods.WS_EX_TOPMOST) == 0)
                    {
                        insertAfter = prev;
                    }
                }

                uint flags = NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW;
                if (zOrderOk)
                {
                    flags |= NativeMethods.SWP_NOZORDER;
                }

                NativeMethods.SetWindowPos(overlayHwnd, insertAfter, targetRect.Left, targetRect.Top, width, height, flags);
                lastX = targetRect.Left;
                lastY = targetRect.Top;
                lastW = width;
                lastH = height;
                isVisible = true;
            }
        }

        private bool IsAntigravityDesktopProcess(int pid)
        {
            bool cached;
            if (pidCache.TryGetValue(pid, out cached))
            {
                return cached;
            }

            bool match = false;
            try
            {
                using (Process p = Process.GetProcessById(pid))
                {
                    if (string.Equals(p.ProcessName, "Antigravity", StringComparison.OrdinalIgnoreCase))
                    {
                        match = true;
                        try
                        {
                            if (p.MainModule != null && !string.IsNullOrEmpty(p.MainModule.FileName))
                            {
                                if (p.MainModule.FileName.IndexOf("Antigravity IDE", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    match = false;
                                }
                            }
                        }
                        catch
                        {
                            // ProcessName already matched "Antigravity" cleanly
                        }
                    }
                }
            }
            catch
            {
                match = false;
            }

            pidCache[pid] = match;
            return match;
        }
    }

    internal static class StartupRegistry
    {
        private const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "AntigravitySakuraMapleOverlay";

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunSubKey, false))
                {
                    if (key != null)
                    {
                        object v = key.GetValue(ValueName);
                        return v != null && !string.IsNullOrEmpty(v.ToString());
                    }
                }
            }
            catch
            {
            }
            return false;
        }

        public static void SetEnabled(bool enable, string exePath)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunSubKey, true))
            {
                if (key == null)
                {
                    return;
                }
                if (enable)
                {
                    key.SetValue(ValueName, "\"" + exePath + "\"", RegistryValueKind.String);
                }
                else
                {
                    if (key.GetValue(ValueName) != null)
                    {
                        key.DeleteValue(ValueName, false);
                    }
                }
            }
        }
    }

    internal sealed class TrayController : IDisposable
    {
        private readonly WinForms.NotifyIcon notifyIcon;
        private readonly EffectCanvas canvas;
        private readonly ThemeWatcher themeWatcher;
        private readonly string baseDir;
        private readonly string exePath;

        private ThemeModeOption selectedMode = ThemeModeOption.Auto;
        private UiLanguage currentLang = UiLanguage.ZhTw;

        private WinForms.ToolStripMenuItem titleItem;
        private WinForms.ToolStripMenuItem itemModeAuto;
        private WinForms.ToolStripMenuItem itemModeSakura;
        private WinForms.ToolStripMenuItem itemModeMaple;
        private WinForms.ToolStripMenuItem itemModeMixed;

        private WinForms.ToolStripMenuItem bgSubmenu;
        private WinForms.ToolStripMenuItem itemBgOff;
        private WinForms.ToolStripMenuItem itemBgSoft;
        private WinForms.ToolStripMenuItem itemBgVivid;

        private WinForms.ToolStripMenuItem denSubmenu;
        private WinForms.ToolStripMenuItem itemDenOff;
        private WinForms.ToolStripMenuItem itemDenLow;
        private WinForms.ToolStripMenuItem itemDenMed;
        private WinForms.ToolStripMenuItem itemDenHigh;

        private WinForms.ToolStripMenuItem itemPause;
        private WinForms.ToolStripMenuItem langSubmenu;
        private WinForms.ToolStripMenuItem itemLangZh;
        private WinForms.ToolStripMenuItem itemLangEn;
        private WinForms.ToolStripMenuItem itemAutoStart;
        private WinForms.ToolStripMenuItem openFolderItem;
        private WinForms.ToolStripMenuItem exitItem;

        public TrayController(EffectCanvas effectCanvas, ThemeWatcher watcher, string appBaseDir, string executablePath)
        {
            this.canvas = effectCanvas;
            this.themeWatcher = watcher;
            this.baseDir = appBaseDir;
            this.exePath = executablePath;
            this.currentLang = LanguagePrefs.LoadLanguage(appBaseDir);

            WinForms.ContextMenuStrip menu = new WinForms.ContextMenuStrip();

            titleItem = new WinForms.ToolStripMenuItem();
            titleItem.Enabled = false;
            menu.Items.Add(titleItem);
            menu.Items.Add(new WinForms.ToolStripSeparator());

            itemModeAuto = new WinForms.ToolStripMenuItem();
            itemModeAuto.Click += delegate { SetMode(ThemeModeOption.Auto); };
            itemModeSakura = new WinForms.ToolStripMenuItem();
            itemModeSakura.Click += delegate { SetMode(ThemeModeOption.Sakura); };
            itemModeMaple = new WinForms.ToolStripMenuItem();
            itemModeMaple.Click += delegate { SetMode(ThemeModeOption.Maple); };
            itemModeMixed = new WinForms.ToolStripMenuItem();
            itemModeMixed.Click += delegate { SetMode(ThemeModeOption.Mixed); };

            menu.Items.Add(itemModeAuto);
            menu.Items.Add(itemModeSakura);
            menu.Items.Add(itemModeMaple);
            menu.Items.Add(itemModeMixed);
            menu.Items.Add(new WinForms.ToolStripSeparator());

            bgSubmenu = new WinForms.ToolStripMenuItem();
            itemBgOff = new WinForms.ToolStripMenuItem();
            itemBgOff.Click += delegate { SetBgOpacity(0.0); };
            itemBgSoft = new WinForms.ToolStripMenuItem();
            itemBgSoft.Click += delegate { SetBgOpacity(0.55); };
            itemBgVivid = new WinForms.ToolStripMenuItem();
            itemBgVivid.Click += delegate { SetBgOpacity(0.85); };
            bgSubmenu.DropDownItems.Add(itemBgOff);
            bgSubmenu.DropDownItems.Add(itemBgSoft);
            bgSubmenu.DropDownItems.Add(itemBgVivid);
            menu.Items.Add(bgSubmenu);

            denSubmenu = new WinForms.ToolStripMenuItem();
            itemDenOff = new WinForms.ToolStripMenuItem();
            itemDenOff.Click += delegate { SetDensity(0); };
            itemDenLow = new WinForms.ToolStripMenuItem();
            itemDenLow.Click += delegate { SetDensity(18); };
            itemDenMed = new WinForms.ToolStripMenuItem();
            itemDenMed.Click += delegate { SetDensity(34); };
            itemDenHigh = new WinForms.ToolStripMenuItem();
            itemDenHigh.Click += delegate { SetDensity(55); };
            denSubmenu.DropDownItems.Add(itemDenOff);
            denSubmenu.DropDownItems.Add(itemDenLow);
            denSubmenu.DropDownItems.Add(itemDenMed);
            denSubmenu.DropDownItems.Add(itemDenHigh);
            menu.Items.Add(denSubmenu);

            itemPause = new WinForms.ToolStripMenuItem();
            itemPause.Click += delegate { TogglePause(); };
            menu.Items.Add(itemPause);
            menu.Items.Add(new WinForms.ToolStripSeparator());

            langSubmenu = new WinForms.ToolStripMenuItem();
            itemLangZh = new WinForms.ToolStripMenuItem("繁體中文");
            itemLangZh.Click += delegate { SetLanguage(UiLanguage.ZhTw); };
            itemLangEn = new WinForms.ToolStripMenuItem("English");
            itemLangEn.Click += delegate { SetLanguage(UiLanguage.En); };
            langSubmenu.DropDownItems.Add(itemLangZh);
            langSubmenu.DropDownItems.Add(itemLangEn);
            menu.Items.Add(langSubmenu);

            itemAutoStart = new WinForms.ToolStripMenuItem();
            itemAutoStart.Click += delegate { ToggleAutoStart(); };
            menu.Items.Add(itemAutoStart);

            openFolderItem = new WinForms.ToolStripMenuItem();
            openFolderItem.Click += delegate
            {
                try
                {
                    Process.Start("explorer.exe", baseDir);
                }
                catch
                {
                }
            };
            menu.Items.Add(openFolderItem);
            menu.Items.Add(new WinForms.ToolStripSeparator());

            exitItem = new WinForms.ToolStripMenuItem();
            exitItem.Click += delegate
            {
                if (Application.Current != null)
                {
                    Application.Current.Shutdown();
                }
            };
            menu.Items.Add(exitItem);

            notifyIcon = new WinForms.NotifyIcon();
            notifyIcon.Icon = LoadTrayIcon(baseDir, executablePath);
            notifyIcon.ContextMenuStrip = menu;
            notifyIcon.DoubleClick += delegate { TogglePause(); };

            ApplyLanguageText();
            SyncCheckStates();
            notifyIcon.Visible = true;
        }

        public ThemeModeOption SelectedMode
        {
            get { return selectedMode; }
        }

        private static Drawing.Icon LoadTrayIcon(string baseDir, string executablePath)
        {
            string icoPath = Path.Combine(baseDir, "assets", "antigravity_sakura_maple.ico");
            try
            {
                if (File.Exists(icoPath))
                {
                    return new Drawing.Icon(icoPath, new Drawing.Size(32, 32));
                }
            }
            catch
            {
            }
            try
            {
                return Drawing.Icon.ExtractAssociatedIcon(executablePath);
            }
            catch
            {
                return Drawing.SystemIcons.Application;
            }
        }

        public void ApplyAutoThemeIfSelected()
        {
            if (selectedMode == ThemeModeOption.Auto)
            {
                canvas.ActiveSeason = themeWatcher.CurrentAutoSeason;
                UpdateTooltip();
            }
        }

        private void SetLanguage(UiLanguage lang)
        {
            currentLang = lang;
            LanguagePrefs.SaveLanguage(baseDir, lang);
            ApplyLanguageText();
            SyncCheckStates();
        }

        private void ApplyLanguageText()
        {
            if (currentLang == UiLanguage.En)
            {
                titleItem.Text = "\uD83C\uDF38\uD83C\uDF41 Antigravity Sakura & Maple";
                itemModeAuto.Text = "Auto-Follow Antigravity Theme";
                itemModeSakura.Text = "Sakura Mode \uD83C\uDF38 (Light)";
                itemModeMaple.Text = "Maple Mode \uD83C\uDF41 (Dark)";
                itemModeMixed.Text = "Mixed Mode \uD83C\uDF38\uD83C\uDF41 (Sakura & Maple)";

                bgSubmenu.Text = "Corner Botanical Backdrop";
                itemBgOff.Text = "Off";
                itemBgSoft.Text = "Subtle (55%)";
                itemBgVivid.Text = "Vivid (85%)";

                denSubmenu.Text = "Falling Leaf / Petal Density";
                itemDenOff.Text = "Off";
                itemDenLow.Text = "Sparse";
                itemDenMed.Text = "Medium";
                itemDenHigh.Text = "Flurry";

                itemPause.Text = "Pause Effects";
                langSubmenu.Text = "Language / \u8a9e\u8a00";
                itemAutoStart.Text = "Run at Windows Startup";
                openFolderItem.Text = "Open Theme Folder";
                exitItem.Text = "Exit";
            }
            else
            {
                titleItem.Text = "\uD83C\uDF38\uD83C\uDF41 Antigravity 櫻花＆楓葉特效";
                itemModeAuto.Text = "自動跟隨 Antigravity 主題";
                itemModeSakura.Text = "櫻花模式 \uD83C\uDF38 (淺色)";
                itemModeMaple.Text = "楓葉模式 \uD83C\uDF41 (深色)";
                itemModeMixed.Text = "櫻楓共舞 \uD83C\uDF38\uD83C\uDF41 (混合)";

                bgSubmenu.Text = "背景花枝裝飾";
                itemBgOff.Text = "關閉";
                itemBgSoft.Text = "淡雅 (55%)";
                itemBgVivid.Text = "鮮明 (85%)";

                denSubmenu.Text = "落葉／落花密度";
                itemDenOff.Text = "關閉";
                itemDenLow.Text = "稀疏";
                itemDenMed.Text = "適中";
                itemDenHigh.Text = "紛飛";

                itemPause.Text = "暫停特效";
                langSubmenu.Text = "語言 / Language";
                itemAutoStart.Text = "開機時自動啟動";
                openFolderItem.Text = "開啟主題資料夾";
                exitItem.Text = "結束";
            }
        }

        private void SetMode(ThemeModeOption mode)
        {
            selectedMode = mode;
            if (mode == ThemeModeOption.Auto)
            {
                themeWatcher.RefreshFromDisk(false);
                canvas.ActiveSeason = themeWatcher.CurrentAutoSeason;
            }
            else if (mode == ThemeModeOption.Maple)
            {
                canvas.ActiveSeason = SeasonKind.Maple;
            }
            else if (mode == ThemeModeOption.Sakura)
            {
                canvas.ActiveSeason = SeasonKind.Sakura;
            }
            else
            {
                canvas.ActiveSeason = SeasonKind.Mixed;
            }
            SyncCheckStates();
        }

        private void SetBgOpacity(double opacity)
        {
            canvas.BackgroundOpacity = opacity;
            SyncCheckStates();
        }

        private void SetDensity(int count)
        {
            canvas.TargetParticleCount = count;
            SyncCheckStates();
        }

        private void TogglePause()
        {
            canvas.IsPaused = !canvas.IsPaused;
            SyncCheckStates();
        }

        private void ToggleAutoStart()
        {
            bool target = !StartupRegistry.IsEnabled();
            try
            {
                StartupRegistry.SetEnabled(target, exePath);
            }
            catch
            {
            }
            SyncCheckStates();
        }

        private void SyncCheckStates()
        {
            itemModeAuto.Checked = (selectedMode == ThemeModeOption.Auto);
            itemModeSakura.Checked = (selectedMode == ThemeModeOption.Sakura);
            itemModeMaple.Checked = (selectedMode == ThemeModeOption.Maple);
            itemModeMixed.Checked = (selectedMode == ThemeModeOption.Mixed);

            double bg = canvas.BackgroundOpacity;
            itemBgOff.Checked = (bg < 0.05);
            itemBgSoft.Checked = (bg >= 0.05 && bg < 0.70);
            itemBgVivid.Checked = (bg >= 0.70);

            int cnt = canvas.TargetParticleCount;
            itemDenOff.Checked = (cnt == 0);
            itemDenLow.Checked = (cnt > 0 && cnt <= 20);
            itemDenMed.Checked = (cnt > 20 && cnt <= 40);
            itemDenHigh.Checked = (cnt > 40);

            itemPause.Checked = canvas.IsPaused;
            itemLangZh.Checked = (currentLang == UiLanguage.ZhTw);
            itemLangEn.Checked = (currentLang == UiLanguage.En);
            itemAutoStart.Checked = StartupRegistry.IsEnabled();
            UpdateTooltip();
        }

        private void UpdateTooltip()
        {
            notifyIcon.Text = UiStrings.FormatTooltip(currentLang, canvas.ActiveSeason, canvas.IsPaused);
        }

        public void Dispose()
        {
            if (notifyIcon != null)
            {
                notifyIcon.Visible = false;
                notifyIcon.Dispose();
            }
        }
    }

    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                NativeMethods.SetProcessDpiAwarenessContext(new IntPtr(-4));
            }
            catch
            {
                try
                {
                    NativeMethods.SetProcessDPIAware();
                }
                catch
                {
                }
            }

            if (args != null && args.Length > 0 && string.Equals(args[0], "--self-test", StringComparison.OrdinalIgnoreCase))
            {
                string reportPath = (args.Length > 1 && !string.IsNullOrEmpty(args[1]))
                    ? args[1]
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "self_test_report.txt");
                return RunSelfTest(reportPath);
            }

            bool createdNew;
            using (Mutex singleInstanceMutex = new Mutex(true, @"Local\AntigravitySakuraMapleOverlay_SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    return 0;
                }

                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string assetsDir = Path.Combine(baseDir, "assets");
                string exePath = Assembly.GetExecutingAssembly().Location;
                string configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".gemini", "config", "config.json");

                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                ThemeWatcher themeWatcher = new ThemeWatcher(configPath);
                EffectCanvas canvas = new EffectCanvas(assetsDir);
                canvas.ActiveSeason = themeWatcher.CurrentAutoSeason;

                OverlayWindow overlay = new OverlayWindow(canvas);
                overlay.Show();
                if (overlay.Handle != IntPtr.Zero)
                {
                    NativeMethods.ShowWindow(overlay.Handle, NativeMethods.SW_HIDE);
                }

                WindowTracker tracker = new WindowTracker(overlay);
                TrayController tray = new TrayController(canvas, themeWatcher, baseDir, exePath);

                themeWatcher.AutoSeasonChanged += delegate
                {
                    if (app.Dispatcher != null)
                    {
                        app.Dispatcher.BeginInvoke((Action)delegate
                        {
                            tray.ApplyAutoThemeIfSelected();
                        });
                    }
                };

                Stopwatch sw = Stopwatch.StartNew();
                double prevSec = 0.0;

                DispatcherTimer renderTimer = new DispatcherTimer(DispatcherPriority.Render);
                renderTimer.Interval = TimeSpan.FromMilliseconds(16);
                renderTimer.Tick += delegate
                {
                    double nowSec = sw.Elapsed.TotalSeconds;
                    double dt = nowSec - prevSec;
                    prevSec = nowSec;

                    if (tracker.IsOverlayVisible && !canvas.IsPaused)
                    {
                        canvas.StepSimulation(dt);
                        canvas.InvalidateVisual();
                    }
                };

                DispatcherTimer trackTimer = new DispatcherTimer(DispatcherPriority.Normal);
                trackTimer.Interval = TimeSpan.FromMilliseconds(33);
                trackTimer.Tick += delegate
                {
                    tracker.SyncToTarget();
                };

                DispatcherTimer pollTimer = new DispatcherTimer(DispatcherPriority.Background);
                pollTimer.Interval = TimeSpan.FromSeconds(2);
                pollTimer.Tick += delegate
                {
                    themeWatcher.PollIfChanged();
                };

                app.Exit += delegate
                {
                    renderTimer.Stop();
                    trackTimer.Stop();
                    pollTimer.Stop();
                    tray.Dispose();
                    themeWatcher.Dispose();
                };

                tracker.SyncToTarget();
                renderTimer.Start();
                trackTimer.Start();
                pollTimer.Start();

                app.Run();
                GC.KeepAlive(singleInstanceMutex);
                return 0;
            }
        }

        private static int RunSelfTest(string reportPath)
        {
            StringBuilder sb = new StringBuilder();
            bool passed = true;

            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string assetsDir = Path.Combine(baseDir, "assets");
                string configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".gemini", "config", "config.json");

                sb.AppendLine("=== AntigravitySakuraMapleOverlay Self-Test ===");
                sb.AppendLine("BaseDir: " + baseDir);

                using (ThemeWatcher watcher = new ThemeWatcher(configPath))
                {
                    sb.AppendLine("Config raw themeMode: " + watcher.LastRawThemeMode);
                    sb.AppendLine("Resolved Auto Season: " + watcher.CurrentAutoSeason);
                    if (string.Equals(watcher.LastRawThemeMode, "THEME_MODE_DARK", StringComparison.OrdinalIgnoreCase) &&
                        watcher.CurrentAutoSeason != SeasonKind.Maple)
                    {
                        sb.AppendLine("FAIL: Expected Maple for THEME_MODE_DARK, got " + watcher.CurrentAutoSeason);
                        passed = false;
                    }
                    else if (string.Equals(watcher.LastRawThemeMode, "THEME_MODE_LIGHT", StringComparison.OrdinalIgnoreCase) &&
                        watcher.CurrentAutoSeason != SeasonKind.Sakura)
                    {
                        sb.AppendLine("FAIL: Expected Sakura for THEME_MODE_LIGHT, got " + watcher.CurrentAutoSeason);
                        passed = false;
                    }
                    else
                    {
                        sb.AppendLine(string.Format("PASS: ThemeWatcher resolved '{0}' -> {1}.",
                            string.IsNullOrEmpty(watcher.LastRawThemeMode) ? "(default)" : watcher.LastRawThemeMode,
                            watcher.CurrentAutoSeason));
                    }
                }

                EffectCanvas canvas = new EffectCanvas(assetsDir);

                if (canvas.SakuraPetalGeometry == null || !canvas.SakuraPetalGeometry.IsFrozen)
                {
                    sb.AppendLine("FAIL: SakuraPetalGeometry missing or not frozen.");
                    passed = false;
                }
                else
                {
                    sb.AppendLine("PASS: SakuraPetalGeometry frozen and valid.");
                }

                if (canvas.MapleLeafGeometry == null || !canvas.MapleLeafGeometry.IsFrozen)
                {
                    sb.AppendLine("FAIL: MapleLeafGeometry missing or not frozen.");
                    passed = false;
                }
                else
                {
                    sb.AppendLine("PASS: MapleLeafGeometry frozen and valid.");
                }

                if (!CheckBackdrop(canvas.MapleBackdrop, "maple_bg_dark.png", sb))
                {
                    passed = false;
                }
                if (!CheckBackdrop(canvas.SakuraBackdrop, "sakura_bg_light.png", sb))
                {
                    passed = false;
                }

                string icoPath = Path.Combine(assetsDir, "antigravity_sakura_maple.ico");
                if (!File.Exists(icoPath))
                {
                    sb.AppendLine("FAIL: Missing icon file " + icoPath);
                    passed = false;
                }
                else
                {
                    using (Drawing.Icon icon = new Drawing.Icon(icoPath, new Drawing.Size(32, 32)))
                    {
                        sb.AppendLine(string.Format("PASS: Icon loaded ({0}x{1}).", icon.Width, icon.Height));
                    }
                }

                SeasonKind[] modes = new SeasonKind[] { SeasonKind.Maple, SeasonKind.Sakura, SeasonKind.Mixed };
                for (int m = 0; m < modes.Length; m++)
                {
                    canvas.ActiveSeason = modes[m];
                    for (int f = 0; f < 120; f++)
                    {
                        canvas.StepSimulation(0.016);
                    }
                    LeafParticle[] snap = canvas.GetParticlesSnapshot();
                    if (snap.Length != 34)
                    {
                        sb.AppendLine(string.Format("FAIL: Mode {0} expected 34 particles, got {1}", modes[m], snap.Length));
                        passed = false;
                    }
                    for (int i = 0; i < snap.Length; i++)
                    {
                        if (double.IsNaN(snap[i].X) || double.IsInfinity(snap[i].X) ||
                            double.IsNaN(snap[i].Y) || double.IsInfinity(snap[i].Y))
                        {
                            sb.AppendLine(string.Format("FAIL: Mode {0} particle {1} non-finite coords.", modes[m], i));
                            passed = false;
                            break;
                        }
                    }
                    sb.AppendLine(string.Format("PASS: Mode {0} simulated 120 frames ({1} particles finite).", modes[m], snap.Length));
                }

                bool autoStart = StartupRegistry.IsEnabled();
                sb.AppendLine("StartupRegistry.IsEnabled(): " + autoStart);
                if (autoStart)
                {
                    sb.AppendLine("FAIL: Startup registry auto-start should be disabled by default.");
                    passed = false;
                }
                else
                {
                    sb.AppendLine("PASS: Startup registry auto-start is disabled by default.");
                }

                UiLanguage detectedLang = LanguagePrefs.LoadLanguage(baseDir);
                sb.AppendLine("Detected UiLanguage: " + detectedLang);
                UiLanguage[] langs = new UiLanguage[] { UiLanguage.ZhTw, UiLanguage.En };
                SeasonKind[] allSeasons = new SeasonKind[] { SeasonKind.Maple, SeasonKind.Sakura, SeasonKind.Mixed };
                bool[] pauseStates = new bool[] { false, true };
                for (int li = 0; li < langs.Length; li++)
                {
                    for (int si = 0; si < allSeasons.Length; si++)
                    {
                        for (int pi = 0; pi < pauseStates.Length; pi++)
                        {
                            string tip = UiStrings.FormatTooltip(langs[li], allSeasons[si], pauseStates[pi]);
                            if (string.IsNullOrEmpty(tip) || tip.Length > 63)
                            {
                                sb.AppendLine(string.Format("FAIL: Invalid tooltip for {0}/{1}/paused={2}: len={3}",
                                    langs[li], allSeasons[si], pauseStates[pi], tip == null ? 0 : tip.Length));
                                passed = false;
                            }
                        }
                    }
                }
                sb.AppendLine("PASS: UiStrings.FormatTooltip valid (<=63 chars) for ZhTw & En.");

                sb.AppendLine(passed ? "RESULT: ALL_PASSED" : "RESULT: FAILED");
            }
            catch (Exception ex)
            {
                passed = false;
                sb.AppendLine("EXCEPTION: " + ex.ToString());
                sb.AppendLine("RESULT: FAILED");
            }

            try
            {
                string dir = Path.GetDirectoryName(reportPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(reportPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch
            {
            }

            return passed ? 0 : 1;
        }

        private static bool CheckBackdrop(BitmapImage bmp, string name, StringBuilder sb)
        {
            if (bmp == null)
            {
                sb.AppendLine("FAIL: Could not load " + name);
                return false;
            }
            int w = bmp.PixelWidth;
            int h = bmp.PixelHeight;
            if (w != 1920 || h != 1080)
            {
                sb.AppendLine(string.Format("FAIL: {0} dimensions {1}x{2} != 1920x1080", name, w, h));
                return false;
            }

            int stride = w * 4;
            byte[] pixels = new byte[h * stride];
            bmp.CopyPixels(pixels, stride, 0);

            int cx = w / 2;
            int cy = h / 2;
            byte centerAlpha = pixels[(cy * stride) + (cx * 4) + 3];

            int cornerVisible = 0;
            for (int y = 0; y < h; y++)
            {
                if (y >= 280 && y < h - 280)
                {
                    continue;
                }
                for (int x = 0; x < w; x++)
                {
                    if (x >= 460 && x < w - 460)
                    {
                        continue;
                    }
                    byte a = pixels[(y * stride) + (x * 4) + 3];
                    if (a > 20)
                    {
                        cornerVisible++;
                    }
                }
            }

            if (centerAlpha != 0 || cornerVisible <= 0)
            {
                sb.AppendLine(string.Format("FAIL: {0} centerAlpha={1}, cornerVisible={2}", name, centerAlpha, cornerVisible));
                return false;
            }

            sb.AppendLine(string.Format("PASS: {0} {1}x{2} centerAlpha={3}, cornerVisiblePx={4}", name, w, h, centerAlpha, cornerVisible));
            return true;
        }
    }
}
