using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NTTool
{
    // ╔══════════════════════════════════════════════════════════════╗
    //   GFX HELPERS
    // ╚══════════════════════════════════════════════════════════════╝
    static class G
    {
        public static GraphicsPath Round(Rectangle r, int rad)
        {
            int d = rad * 2; var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }
        public static void FillRound(Graphics g, Color c, Rectangle r, int rad)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(c)) using (var p = Round(r, rad)) g.FillPath(b, p);
        }
        public static void DrawRound(Graphics g, Color c, Rectangle r, int rad, float w = 1f)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(c, w)) using (var p = Round(r, rad)) g.DrawPath(pen, p);
        }
        public static void FillRoundGrad(Graphics g, Color c1, Color c2, Rectangle r, int rad, LinearGradientMode m = LinearGradientMode.Vertical)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var br = new LinearGradientBrush(r, c1, c2, m))
            using (var p = Round(r, rad)) g.FillPath(br, p);
        }
        public static Color Blend(Color a, Color b, float t) =>
            Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t),
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
    }

    // ╔══════════════════════════════════════════════════════════════╗
    //   TWEAK MODEL
    // ╚══════════════════════════════════════════════════════════════╝
    public class Tweak
    {
        public string Name, Info, Tag, Category;
        public bool Risky, Enabled, Applied;
        public Action<bool> Apply;
    }

    // ╔══════════════════════════════════════════════════════════════╗
    //   WHEEL FILTER — intercepta scroll globalmente para o painel
    // ╚══════════════════════════════════════════════════════════════╝
    class WheelFilter : IMessageFilter
    {
        readonly Action<int> _cb;
        readonly Func<bool> _inBounds;
        const int WM_MOUSEWHEEL = 0x020A;
        public WheelFilter(Action<int> cb, Func<bool> inBounds) { _cb = cb; _inBounds = inBounds; }
        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != WM_MOUSEWHEEL || !_inBounds()) return false;
            _cb((short)(((uint)m.WParam.ToInt64() >> 16) & 0xFFFF));
            return true;
        }
    }

    // ╔══════════════════════════════════════════════════════════════╗
    //   DARK SCROLL PANEL — inner-panel offset, zero flicker
    // ╚══════════════════════════════════════════════════════════════╝
    public class DarkScrollPanel : Control
    {
        public readonly Panel Inner;

        int _scroll;
        bool _sbDrag, _sbHover;
        int _sbDY, _sbDS;
        bool _pageDrag;
        int _pageDY, _pageDS;

        const int SB_W = 5, SB_M = 3, PAD_X = 14, PAD_T = 12;

        static readonly Color C_BG = Color.FromArgb(10, 10, 16);
        static readonly Color C_TRACK = Color.FromArgb(18, 18, 30);
        static readonly Color C_THUMB = Color.FromArgb(55, 55, 85);
        static readonly Color C_THUMBH = Color.FromArgb(88, 88, 130);

        WheelFilter _filter;

        public DarkScrollPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = C_BG;

            Inner = new Panel { BackColor = Color.Transparent, Left = PAD_X, Top = PAD_T };
            SetDB(Inner);
            Controls.Add(Inner);
        }

        static void SetDB(Control c) =>
            typeof(Control).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(c, true, null);

        // ── Win32: clip children so parent doesn't overdraw them ─
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x02000000; return cp; } // WS_EX_COMPOSITED
        }

        // ── Scroll ────────────────────────────────────────────────
        int MaxScroll => Math.Max(0, Inner.Height + PAD_T * 2 - Height);

        public void ScrollBy(int delta) => ScrollTo(_scroll + delta);
        public void ScrollTo(int v)
        {
            _scroll = Math.Max(0, Math.Min(v, MaxScroll));
            Inner.Top = PAD_T - _scroll;
            // Only repaint scrollbar strip — inner moves without invalidation
            Invalidate(new Rectangle(Width - SB_W - SB_M - 3, 0, SB_W + SB_M + 6, Height));
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            Inner.Width = Width - SB_W - SB_M * 2 - PAD_X - 2;
            Inner.Top = PAD_T - _scroll;
        }

        public void InstallFilter() => Application.AddMessageFilter(_filter = new WheelFilter(d => ScrollBy(-d / 3), () => ClientRectangle.Contains(PointToClient(Cursor.Position))));
        public void RemoveFilter() => Application.RemoveMessageFilter(_filter);

        // ── Mouse ─────────────────────────────────────────────────
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); if (e.Button != MouseButtons.Left) return;
            var th = ThumbRect();
            if (!th.IsEmpty && th.Contains(e.Location))
            { _sbDrag = true; _sbDY = e.Y; _sbDS = _scroll; Capture = true; return; }
            _pageDrag = true; _pageDY = e.Y; _pageDS = _scroll; Cursor = Cursors.SizeNS; Capture = true;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_sbDrag) { DragSB(e.Y); return; }
            if (_pageDrag) { ScrollTo(_pageDS + (_pageDY - e.Y)); return; }
            bool h = !ThumbRect().IsEmpty && ThumbRect().Contains(e.Location);
            if (h != _sbHover) { _sbHover = h; Invalidate(); }
        }
        protected override void OnMouseUp(MouseEventArgs e)
        { base.OnMouseUp(e); _sbDrag = false; _pageDrag = false; Capture = false; Cursor = Cursors.Default; }

        void DragSB(int my)
        {
            int tk = Height - SB_M * 2, th = ThH(tk), rng = tk - th;
            if (rng > 0) ScrollTo(_sbDS + (int)((my - _sbDY) * (float)MaxScroll / rng));
        }

        // Hook drag/scroll — SKIP buttons so clicks work normally.
        // Drag only activates after DRAG_THRESHOLD pixels.
        const int DRAG_THRESHOLD = 6;

        public void HookChildren() => HookCtrl(Inner);
        void HookCtrl(Control c)
        {
            bool isBtn = c is Button || c is EnableBtn || c is ApplyBtn || c is WinBtn;

            if (c != Inner && !isBtn)
            {
                int pendingY = -1, pendingS = 0;
                bool pending = false;

                c.MouseDown += (s, e) =>
                {
                    if (e.Button != MouseButtons.Left) return;
                    var pt = PointToClient(((Control)s).PointToScreen(e.Location));
                    pending = true; pendingY = pt.Y; pendingS = _scroll;
                };
                c.MouseMove += (s, e) =>
                {
                    var pt = PointToClient(((Control)s).PointToScreen(e.Location));
                    if (pending && Math.Abs(pt.Y - pendingY) >= DRAG_THRESHOLD)
                    {
                        pending = false;
                        _pageDrag = true; _pageDY = pendingY; _pageDS = pendingS;
                        Cursor = Cursors.SizeNS; Capture = true;
                    }
                    if (!_pageDrag && !_sbDrag) return;
                    if (_sbDrag) { DragSB(pt.Y); return; }
                    if (_pageDrag) ScrollTo(_pageDS + (_pageDY - pt.Y));
                };
                c.MouseUp += (s, e) =>
                {
                    pending = false;
                    _sbDrag = false; _pageDrag = false; Capture = false; Cursor = Cursors.Default;
                };
            }
            foreach (Control ch in c.Controls) HookCtrl(ch);
            c.ControlAdded += (s, ev) => HookCtrl(ev.Control);
        }

        // ── Scrollbar geometry ────────────────────────────────────
        int ThH(int track) => MaxScroll == 0 ? track : Math.Max(32, (int)((float)Height / (Inner.Height + PAD_T * 2) * track));
        Rectangle ThumbRect()
        {
            if (MaxScroll == 0) return Rectangle.Empty;
            int tk = Height - SB_M * 2, th = ThH(tk);
            int ty = SB_M + (int)((float)(tk - th) * _scroll / MaxScroll);
            return new Rectangle(Width - SB_W - SB_M, ty, SB_W, th);
        }

        // ── Paint — only the scrollbar strip ─────────────────────
        protected override void OnPaint(PaintEventArgs e)
        {
            var sb = new Rectangle(Width - SB_W - SB_M - 1, 0, SB_W + SB_M + 3, Height);
            e.Graphics.FillRectangle(new SolidBrush(C_BG), sb);
            G.FillRound(e.Graphics, C_TRACK, new Rectangle(Width - SB_W - SB_M, SB_M, SB_W, Height - SB_M * 2), 3);
            var th = ThumbRect();
            if (!th.IsEmpty)
                G.FillRound(e.Graphics, (_sbDrag || _sbHover) ? C_THUMBH : C_THUMB, th, 3);
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Fill the left/top/bottom areas that Inner doesn't cover
            e.Graphics.Clear(C_BG);
        }
    }

    // ╔══════════════════════════════════════════════════════════════╗
    //   TAG CHIP
    // ╚══════════════════════════════════════════════════════════════╝
    static class TagColors
    {
        public static Color Bg(string cat, bool risky)
        {
            if (risky) return Color.FromArgb(50, 32, 10, 2);
            switch (cat)
            {
                case "SISTEMA": return Color.FromArgb(50, 12, 35, 90);
                case "JOGO": return Color.FromArgb(50, 10, 55, 28);
                case "REDE": return Color.FromArgb(50, 45, 18, 80);
                default: return Color.FromArgb(40, 40, 40, 60);
            }
        }
        public static Color Text(string cat, bool risky)
        {
            if (risky) return Color.FromArgb(220, 100, 20);
            switch (cat)
            {
                case "SISTEMA": return Color.FromArgb(80, 140, 255);
                case "JOGO": return Color.FromArgb(50, 200, 110);
                case "REDE": return Color.FromArgb(150, 100, 255);
                default: return Color.FromArgb(140, 140, 170);
            }
        }
        public static Color Accent(string cat, bool risky, bool applied)
        {
            if (applied) return Color.FromArgb(46, 175, 90);
            if (risky) return Color.FromArgb(190, 85, 10);
            switch (cat)
            {
                case "SISTEMA": return Color.FromArgb(60, 120, 230);
                case "JOGO": return Color.FromArgb(38, 170, 90);
                case "REDE": return Color.FromArgb(120, 70, 220);
                default: return Color.FromArgb(60, 60, 90);
            }
        }
        public static Color Border(string cat, bool risky, bool enabled, bool applied)
        {
            if (applied) return Color.FromArgb(36, 140, 72);
            if (risky) return Color.FromArgb(120, 58, 8);
            if (enabled)
                switch (cat)
                {
                    case "SISTEMA": return Color.FromArgb(42, 85, 180);
                    case "JOGO": return Color.FromArgb(30, 120, 62);
                    case "REDE": return Color.FromArgb(90, 48, 175);
                }
            return Color.FromArgb(28, 28, 44);
        }
    }

    // ╔══════════════════════════════════════════════════════════════╗
    //   ENABLE BUTTON
    // ╚══════════════════════════════════════════════════════════════╝
    public class EnableBtn : Control
    {
        public bool IsOn { get; private set; }
        public event Action<bool> Changed;

        static readonly Color C_OFF_BG = Color.FromArgb(22, 22, 36);
        static readonly Color C_OFF_BOR = Color.FromArgb(42, 42, 65);
        static readonly Color C_OFF_TXT = Color.FromArgb(82, 82, 115);
        static readonly Color C_OFF_HOV = Color.FromArgb(30, 30, 48);
        static readonly Color C_ON_BG1 = Color.FromArgb(16, 68, 38);
        static readonly Color C_ON_BG2 = Color.FromArgb(10, 52, 28);
        static readonly Color C_ON_BOR = Color.FromArgb(40, 148, 82);
        static readonly Color C_ON_TXT = Color.FromArgb(72, 210, 120);
        bool _hover;

        public EnableBtn()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            Size = new Size(90, 29); Cursor = Cursors.Hand; BackColor = Color.Transparent;
        }
        public void SetState(bool v) { IsOn = v; Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); }
        protected override void OnClick(EventArgs e)
        { base.OnClick(e); IsOn = !IsOn; Invalidate(); Changed?.Invoke(IsOn); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (IsOn)
                G.FillRoundGrad(g, C_ON_BG1, C_ON_BG2, r, 6);
            else
                G.FillRound(g, _hover ? C_OFF_HOV : C_OFF_BG, r, 6);
            G.DrawRound(g, IsOn ? C_ON_BOR : C_OFF_BOR, r, 6, 1.3f);

            // Icon dot
            if (IsOn)
            {
                int cx = 16, cy = Height / 2;
                using (var b = new SolidBrush(C_ON_TXT)) g.FillEllipse(b, cx - 3, cy - 3, 6, 6);
            }
            string lbl = IsOn ? "Ativado" : "Ativar";
            TextRenderer.DrawText(g, lbl, new Font("Segoe UI", 7.5f, FontStyle.Bold),
                IsOn ? new Rectangle(10, 0, Width - 12, Height) : r,
                IsOn ? C_ON_TXT : C_OFF_TXT,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    // ╔══════════════════════════════════════════════════════════════╗
    //   APPLY BUTTON
    // ╚══════════════════════════════════════════════════════════════╝
    public class ApplyBtn : Control
    {
        public enum St { Off, Ready, Done }
        public St State { get; private set; } = St.Off;

        static readonly Color[] BG1 = { Color.FromArgb(18, 18, 32), Color.FromArgb(14, 60, 118), Color.FromArgb(10, 48, 28) };
        static readonly Color[] BG2 = { Color.FromArgb(14, 14, 26), Color.FromArgb(10, 44, 90), Color.FromArgb(8, 36, 20) };
        static readonly Color[] BOR = { Color.FromArgb(34, 34, 52), Color.FromArgb(40, 126, 220), Color.FromArgb(34, 126, 68) };
        static readonly Color[] TXT = { Color.FromArgb(46, 46, 72), Color.FromArgb(115, 185, 255), Color.FromArgb(68, 196, 108) };
        static readonly Color HOV = Color.FromArgb(18, 78, 150);
        bool _hover;
        public event EventHandler Clicked;

        public ApplyBtn()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            Size = new Size(90, 29); Cursor = Cursors.Default; BackColor = Color.Transparent;
        }
        public void SetState(St s) { State = s; Cursor = s == St.Ready ? Cursors.Hand : Cursors.Default; Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); }
        protected override void OnClick(EventArgs e)
        { base.OnClick(e); if (State == St.Ready) Clicked?.Invoke(this, EventArgs.Empty); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1); int i = (int)State;
            if (State == St.Ready && _hover)
                G.FillRoundGrad(g, HOV, Color.FromArgb(12, 56, 110), r, 6);
            else
                G.FillRoundGrad(g, BG1[i], BG2[i], r, 6);
            G.DrawRound(g, BOR[i], r, 6, 1.3f);
            string[] lbl = { "Aplicar", "Aplicar", "\u2713 Aplicado" };
            TextRenderer.DrawText(g, lbl[i], new Font("Segoe UI", 7.5f, FontStyle.Bold), r, TXT[i],
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    // ╔══════════════════════════════════════════════════════════════╗
    //   TWEAK ROW CARD — with hover glow
    // ╚══════════════════════════════════════════════════════════════╝
    public class TweakRow : Panel
    {
        public Tweak Ref;
        Form1 _owner;
        EnableBtn _btnE;
        ApplyBtn _btnA;
        bool _hover;

        static readonly Color C_IDLE = Color.FromArgb(16, 16, 26);
        static readonly Color C_HOV = Color.FromArgb(20, 20, 34);

        public TweakRow(Tweak t, Form1 f)
        {
            Ref = t; _owner = f;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Height = 100;

            // Tag label
            Controls.Add(new Label
            {
                Text = t.Risky ? t.Tag + "  \u26A0 RISCO" : t.Tag,
                Font = new Font("Segoe UI", 6.2f, FontStyle.Bold),
                ForeColor = TagColors.Text(t.Category, t.Risky),
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(18, 11)
            });
            // Name
            Controls.Add(new Label
            {
                Text = t.Name,
                Font = new Font("Segoe UI Semibold", 9.5f),
                ForeColor = t.Risky ? Color.FromArgb(210, 118, 14) : Color.FromArgb(218, 218, 235),
                BackColor = Color.Transparent,
                AutoSize = false,
                Height = 20,
                Location = new Point(18, 28)
            });
            // Info
            Controls.Add(new Label
            {
                Text = t.Info,
                Font = new Font("Segoe UI", 7f),
                ForeColor = Color.FromArgb(78, 78, 110),
                BackColor = Color.Transparent,
                AutoSize = false,
                Height = 28,
                Location = new Point(18, 51),
                TextAlign = ContentAlignment.TopLeft
            });

            _btnE = new EnableBtn(); _btnE.Changed += OnToggle;
            _btnA = new ApplyBtn(); _btnA.Clicked += OnApply;
            Controls.Add(_btnE); Controls.Add(_btnA);

            Paint += Draw;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e); if (_btnE == null) return;
            _btnA.Location = new Point(Width - 100, 28);
            _btnE.Location = new Point(Width - 200, 28);
            foreach (Control c in Controls)
                if (c is Label lb && lb.Location.Y >= 24 && c != _btnE && c != _btnA)
                    lb.Width = Width - 224;
        }

        void Draw(object s, PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(1, 1, Width - 3, Height - 3);

            // Card background
            Color bg;
            if (Ref.Applied) bg = Color.FromArgb(10, 24, 14);
            else if (Ref.Enabled) bg = Ref.Risky ? Color.FromArgb(28, 14, 4) : Color.FromArgb(12, 22, 16);
            else if (Ref.Risky) bg = Color.FromArgb(22, 12, 4);
            else bg = _hover ? C_HOV : C_IDLE;

            G.FillRound(g, bg, r, 9);

            // Hover glow border
            if (_hover && !Ref.Applied)
            {
                var glow = TagColors.Border(Ref.Category, Ref.Risky, Ref.Enabled, false);
                G.DrawRound(g, Color.FromArgb(60, glow.R, glow.G, glow.B), r, 9, 2f);
            }

            // Main border
            G.DrawRound(g, TagColors.Border(Ref.Category, Ref.Risky, Ref.Enabled, Ref.Applied), r, 9, 1.2f);

            // Left accent bar
            Color acc = TagColors.Accent(Ref.Category, Ref.Risky, Ref.Applied);
            if (!Ref.Enabled && !Ref.Applied && !Ref.Risky) acc = Color.FromArgb(36, 36, 58);
            using (var bp = G.Round(new Rectangle(2, 14, 4, Height - 28), 2))
            {
                using (var lb = new LinearGradientBrush(new Rectangle(2, 14, 4, Height - 28),
                    Color.FromArgb(180, acc), Color.FromArgb(60, acc), LinearGradientMode.Vertical))
                    g.FillPath(lb, bp);
            }
        }

        void OnToggle(bool on)
        {
            Ref.Enabled = on;
            _btnA.SetState(on && !Ref.Applied ? ApplyBtn.St.Ready
                         : !on && !Ref.Applied ? ApplyBtn.St.Off
                         : ApplyBtn.St.Done);
            Invalidate();
        }

        void OnApply(object s, EventArgs e)
        {
            if (!_owner.IsAdmin())
            { MessageBox.Show("Execute o NT Tool como Administrador.", "Permissao necessaria", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (Ref.Risky && MessageBox.Show(
                $"'{Ref.Name}' e um tweak de risco e pode instabilizar o sistema.\n\nDeseja continuar mesmo assim?",
                "Atencao — Tweak de Risco", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try
            {
                Ref.Apply(true); Ref.Applied = true;
                _btnA.SetState(ApplyBtn.St.Done); Invalidate();
                _owner.SetStatus($"\u2713  '{Ref.Name}' aplicado com sucesso.");
            }
            catch (Exception ex) { _owner.SetStatus($"\u2717  Erro: {ex.Message}"); }
        }

        public void Reset()
        {
            _btnE.SetState(false); _btnA.SetState(ApplyBtn.St.Off);
            Ref.Enabled = false; Ref.Applied = false;
            try { Ref.Apply(false); } catch { }
            Invalidate();
        }
    }

    // ╔══════════════════════════════════════════════════════════════╗
    //   SECTION HEADER CONTROL — com linha decorativa
    // ╚══════════════════════════════════════════════════════════════╝
    public class SectionHeader : Control
    {
        readonly string _cat;
        public SectionHeader(string title)
        {
            _cat = title;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent; Height = 30;
            Paint += Draw;
        }
        void Draw(object s, PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            Color accent = TagColors.Text(_cat, false);
            // Fundo pill
            var pill = new Rectangle(0, 6, 70, 18);
            G.FillRound(g, Color.FromArgb(30, accent.R, accent.G, accent.B), pill, 5);
            using (var f = new Font("Segoe UI", 7.2f, FontStyle.Bold))
                TextRenderer.DrawText(g, _cat, f, pill, accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            // Linha decorativa
            int lx = 76;
            using (var br = new LinearGradientBrush(
                new Rectangle(lx, Height / 2, Width - lx, 1),
                Color.FromArgb(60, accent.R, accent.G, accent.B),
                Color.FromArgb(0, accent.R, accent.G, accent.B),
                LinearGradientMode.Horizontal))
                g.FillRectangle(br, lx, Height / 2, Width - lx, 1);
        }
    }

    // ╔══════════════════════════════════════════════════════════════╗
    //   ANIMATED HEADER
    // ╚══════════════════════════════════════════════════════════════╝
    public class AnimHeader : Control
    {
        float _t;
        Timer _tmr = new Timer();
        Random _rnd = new Random();

        struct Pt { public float X, Y, SX, SY, Sz, A; }
        readonly List<Pt> _pts = new List<Pt>();

        WinBtn _btnClose, _btnMin;
        public event EventHandler CloseClicked, MinClicked;

        public AnimHeader()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            Height = 78;

            for (int i = 0; i < 18; i++) _pts.Add(NewPt(true));

            _btnClose = new WinBtn(WinBtn.T.Close); _btnClose.Click += (s, e) => CloseClicked?.Invoke(this, e);
            _btnMin = new WinBtn(WinBtn.T.Min); _btnMin.Click += (s, e) => MinClicked?.Invoke(this, e);
            Controls.Add(_btnClose); Controls.Add(_btnMin);

            _tmr.Interval = 28;
            _tmr.Tick += (s, e) =>
            {
                _t += 0.016f;
                for (int i = 0; i < _pts.Count; i++)
                {
                    var p = _pts[i]; p.X += p.SX; p.Y += p.SY; p.A -= 0.0028f; _pts[i] = p;
                    if (p.A <= 0 || p.X < -10 || p.X > Width + 10) _pts[i] = NewPt(false);
                }
                Invalidate();
            };
            _tmr.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _tmr?.Dispose(); }
            base.Dispose(disposing);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e); if (_btnClose == null) return;
            _btnClose.Location = new Point(Width - 44, (Height - 32) / 2);
            _btnMin.Location = new Point(Width - 82, (Height - 32) / 2);
        }

        Pt NewPt(bool rY) => new Pt
        {
            X = (float)(_rnd.NextDouble() * (Width > 0 ? Width : 1000)),
            Y = rY ? (float)(_rnd.NextDouble() * Height) : Height + 2,
            SX = (float)(_rnd.NextDouble() * 0.7 - 0.15),
            SY = (float)(-_rnd.NextDouble() * 0.55 - 0.08),
            Sz = (float)(_rnd.NextDouble() * 3.2 + 0.8),
            A = (float)(_rnd.NextDouble() * 0.55 + 0.08)
        };

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width, Height);

            // Animated gradient background
            float h1 = (_t * 16f) % 360f, h2 = (_t * 16f + 140f) % 360f, h3 = (_t * 16f + 260f) % 360f;
            using (var br = new LinearGradientBrush(r, Hsv(h1, .72f, .20f), Hsv(h2, .62f, .14f), LinearGradientMode.Horizontal))
                g.FillRectangle(br, r);

            // Second gradient overlay for depth
            using (var br2 = new LinearGradientBrush(r, Color.FromArgb(0, 0, 0, 0), Hsv(h3, .50f, .12f), LinearGradientMode.Vertical))
                g.FillRectangle(br2, r);


            // Bottom fade to app background
            using (var fd = new LinearGradientBrush(new Rectangle(0, Height - 28, Width, 28),
                Color.FromArgb(0, 0, 0, 0), Color.FromArgb(10, 10, 16), LinearGradientMode.Vertical))
                g.FillRectangle(fd, 0, Height - 28, Width, 28);

            // Particles
            foreach (var p in _pts)
            {
                int a = (int)(p.A * 210); if (a < 4) continue;
                Color pc = a > 130 ? Color.FromArgb(a, 160, 220, 255) : Color.FromArgb(a, 200, 200, 255);
                using (var pb = new SolidBrush(pc)) g.FillEllipse(pb, p.X, p.Y, p.Sz, p.Sz);
            }

            // Glow behind text
            for (int glo = 3; glo >= 1; glo--)
            {
                using (var gb = new SolidBrush(Color.FromArgb(glo * 8, 100, 180, 255)))
                    g.FillEllipse(gb, 8, 8, 50 + glo * 10, 50 + glo * 10);
            }

            // ⚡ Logo
            using (var f = new Font("Segoe UI", 24f, FontStyle.Bold))
                g.DrawString("\u26A1", f, new SolidBrush(Color.FromArgb(255, 244, 72)), new PointF(14f, 10f));

            // Title
            using (var f = new Font("Segoe UI", 15f, FontStyle.Bold))
            {
                // shadow
                g.DrawString("NT Tool", f, new SolidBrush(Color.FromArgb(50, 0, 0, 0)), new PointF(62f, 14f));
                g.DrawString("NT Tool", f, new SolidBrush(Color.FromArgb(235, 235, 248)), new PointF(60f, 12f));
            }

            // Subtitle
            using (var f = new Font("Segoe UI", 7.5f))
                g.DrawString("Otimizador de Performance para Windows", f,
                    new SolidBrush(Color.FromArgb(105, 115, 145)), new PointF(62f, 42f));

            // Version pill
            var vr = new Rectangle(Width - 140, Height - 24, 52, 14);
            G.FillRound(g, Color.FromArgb(40, 255, 255, 255), vr, 4);
            using (var f = new Font("Segoe UI", 6f, FontStyle.Bold))
                TextRenderer.DrawText(g, "v 2.0", f, vr, Color.FromArgb(160, 170, 200),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        static Color Hsv(float h, float s, float v)
        {
            int hi = (int)(h / 60) % 6;
            float f = h / 60f - (float)Math.Floor(h / 60f), p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
            float r, gg, b;
            switch (hi) { case 0: r = v; gg = t; b = p; break; case 1: r = q; gg = v; b = p; break; case 2: r = p; gg = v; b = t; break; case 3: r = p; gg = q; b = v; break; case 4: r = t; gg = p; b = v; break; default: r = v; gg = p; b = q; break; }
            return Color.FromArgb((int)(r * 255), (int)(gg * 255), (int)(b * 255));
        }
    }

    // ╔══════════════════════════════════════════════════════════════╗
    //   WINDOW CONTROL BUTTON
    // ╚══════════════════════════════════════════════════════════════╝
    public class WinBtn : Control
    {
        public enum T { Close, Min }
        T _t; bool _h, _press;

        public WinBtn(T t)
        {
            _t = t;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            Size = new Size(32, 32); Cursor = Cursors.Hand; BackColor = Color.Transparent;
        }
        protected override void OnMouseEnter(EventArgs e) { _h = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { _h = false; _press = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { _press = true; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { _press = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(1, 1, Width - 3, Height - 3);

            if (_h)
            {
                int alpha = _press ? 200 : 140;
                Color bgc = _t == T.Close ? Color.FromArgb(alpha, 178, 38, 38) : Color.FromArgb(alpha, 60, 60, 100);
                G.FillRound(g, bgc, r, 8);
            }

            float ic = _press ? 0.9f : 1f;
            Color iconCol = _h ? (_t == T.Close ? Color.FromArgb(255, 108, 108) : Color.FromArgb(195, 200, 230)) : Color.FromArgb(80, 84, 120);
            using (var pen = new Pen(iconCol, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                int cx = Width / 2, cy = Height / 2, s = (int)(5 * ic);
                if (_t == T.Close) { g.DrawLine(pen, cx - s, cy - s, cx + s, cy + s); g.DrawLine(pen, cx + s, cy - s, cx - s, cy + s); }
                else g.DrawLine(pen, cx - s, cy + 1, cx + s, cy + 1);
            }
        }
    }

    // ╔══════════════════════════════════════════════════════════════╗
    //   STATUS BAR LABEL with animated message
    // ╚══════════════════════════════════════════════════════════════╝
    public class StatusLabel : Control
    {
        string _msg = "Todos os tweaks desativados.";
        Color _msgColor = Color.FromArgb(72, 72, 108);
        Timer _fadeTimer = new Timer();
        float _fade = 1f;
        bool _fading;

        public StatusLabel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            _fadeTimer.Interval = 30;
            _fadeTimer.Tick += (s, e) =>
            {
                if (!_fading) return;
                _fade = Math.Max(0, _fade - 0.06f);
                if (_fade <= 0) { _fading = false; _fadeTimer.Stop(); }
                Invalidate();
            };
        }

        public void SetMsg(string msg, Color? col = null)
        {
            _msg = msg; _msgColor = col ?? Color.FromArgb(72, 72, 108);
            _fade = 1f; _fading = false; _fadeTimer.Stop();
            Invalidate();
            // Auto fade after 4 seconds
            var t = new System.Windows.Forms.Timer { Interval = 4000 };
            t.Tick += (s, e2) => { t.Stop(); t.Dispose(); _fading = true; _fadeTimer.Start(); };
            t.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            int alpha = (int)(_msgColor.A * _fade);
            if (alpha < 1) return;
            var col = Color.FromArgb(Math.Max(1, alpha), _msgColor.R, _msgColor.G, _msgColor.B);
            using (var f = new Font("Segoe UI", 7.5f))
                TextRenderer.DrawText(g, _msg, f, ClientRectangle, col,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
    }

    // ╔══════════════════════════════════════════════════════════════╗
    //   FORM 1
    // ╚══════════════════════════════════════════════════════════════╝
    public partial class Form1 : Form
    {
        // ── P/Invoke ─────────────────────────────────────────────
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int s);
        [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr h, ref MARGINS m);
        [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint a, uint p, ref bool v, uint f);
        [StructLayout(LayoutKind.Sequential)] struct MARGINS { public int L, R, T, B; }

        const int WM_NC = 0x84, WM_DBL = 0xA3;
        const int HTCAP = 2, HTL = 10, HTR = 11, HTT = 12, HTTL = 13, HTTR = 14, HTB = 15, HTBL = 16, HTBR = 17, HTC = 1;
        const int DWM_CORNER = 33, DWM_ROUND = 2, RB = 7, DRAG_H = 118;

        // ── Palette ───────────────────────────────────────────────
        static readonly Color C_BG = Color.FromArgb(10, 10, 16);
        static readonly Color C_BAR = Color.FromArgb(13, 13, 20);
        static readonly Color C_SEP = Color.FromArgb(24, 24, 38);
        static readonly Color C_HINT = Color.FromArgb(48, 86, 175);
        static readonly Color C_ADMIN = Color.FromArgb(40, 180, 90);
        static readonly Color C_NOADM = Color.FromArgb(192, 58, 58);
        static readonly Color C_RED = Color.FromArgb(168, 35, 35);
        static readonly Color C_REDH = Color.FromArgb(200, 52, 52);

        // ── State ─────────────────────────────────────────────────
        readonly List<Tweak> _tweaks = new List<Tweak>();
        readonly List<TweakRow> _rows = new List<TweakRow>();
        DarkScrollPanel _scroll;
        StatusLabel _status;
        Button _btnReset;
        Panel _colL, _colR;

        const int FW = 960, FH = 640, ROW_GAP = 108;

        // ── Shadow via CreateParams ───────────────────────────────
        protected override CreateParams CreateParams
        { get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; return cp; } }

        // ── Borderless resize + drag ──────────────────────────────
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NC)
            {
                int lp = m.LParam.ToInt32();
                var pt = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                bool L = pt.X < RB, R = pt.X >= Width - RB, T = pt.Y < RB, B = pt.Y >= Height - RB;
                if (T && L) { m.Result = (IntPtr)HTTL; return; }
                if (T && R) { m.Result = (IntPtr)HTTR; return; }
                if (B && L) { m.Result = (IntPtr)HTBL; return; }
                if (B && R) { m.Result = (IntPtr)HTBR; return; }
                if (L) { m.Result = (IntPtr)HTL; return; }
                if (R) { m.Result = (IntPtr)HTR; return; }
                if (T) { m.Result = (IntPtr)HTT; return; }
                if (B) { m.Result = (IntPtr)HTB; return; }
                if (pt.Y < DRAG_H) { m.Result = (IntPtr)HTCAP; return; }
                m.Result = (IntPtr)HTC; return;
            }
            if (m.Msg == WM_DBL) { m.Result = IntPtr.Zero; return; }
            base.WndProc(ref m);
        }

        public Form1()
        {
            try { InitializeComponent(); DefineTweaks(); BuildUI(); }
            catch (Exception ex) { MessageBox.Show("Erro de inicializacao:\n" + ex, "NT Tool", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            try { int p = DWM_ROUND; DwmSetWindowAttribute(Handle, DWM_CORNER, ref p, sizeof(int)); } catch { }
            try { var mg = new MARGINS { L = 1, R = 1, T = 1, B = 1 }; DwmExtendFrameIntoClientArea(Handle, ref mg); } catch { }
            _scroll.InstallFilter();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        { base.OnFormClosing(e); _scroll?.RemoveFilter(); }

        // ═══════════════════════════════════════════════════════════
        //  TWEAKS — pesquisados e baseados no Felipe Undocumenteds
        // ═══════════════════════════════════════════════════════════
        void DefineTweaks()
        {
            // ── SISTEMA ──────────────────────────────────────────
            Add("VisualFX", "SISTEMA", false, "SISTEMA",
                "Remove animacoes, sombras e efeitos visuais do Windows.",
                on => Reg(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", on ? 2 : 0));

            Add("Remover Sombras", "SISTEMA", false, "SISTEMA",
                "Desativa sombras de janelas e cursor via SPI_SETDROPSHADOW. Menos trabalho para o DWM.",
                on => {
                    bool val = !on;
                    SystemParametersInfo(0x1025, 0, ref val, 0x0002);
                    Reg(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ListviewShadow", on ? 0 : 1);
                });

            Add("Remover Transparencias DWM", "SISTEMA", false, "SISTEMA",
                "Desativa Aero Glass e transparencia da taskbar. Reduz uso de GPU pelo compositor DWM.",
                on => {
                    Reg(Registry.CurrentUser, @"Software\Microsoft\Windows\DWM", "EnableBlurBehind", on ? 0 : 1);
                    Reg(Registry.CurrentUser, @"Software\Microsoft\Windows\DWM", "ColorizationOpaqueBlend", on ? 1 : 0);
                    Reg(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", on ? 0 : 1);
                });

            Add("Desativar Performance Counters", "SISTEMA", false, "SISTEMA",
                "Para o PerfHost (PDH) que coleta metricas em background. Elimina I/O e interrupcoes desnecessarias.",
                on => Reg(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\PerfHost", "Start", on ? 4 : 2));

            Add("Priority AudioSvchost", "SISTEMA", false, "SISTEMA",
                "Baixa prioridade do svchost via IFEO. Reduz interferencia no game loop sem afetar o audio.",
                on => {
                    const string p = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\svchost.exe\PerfOptions";
                    if (on) Reg(Registry.LocalMachine, p, "CpuPriorityClass", 1);
                    else try { Registry.LocalMachine.DeleteSubKeyTree(p, false); } catch { }
                });

            Add("Scheduler Homogeneo", "SISTEMA", true, "SISTEMA",
                "Win32PrioritySeparation=0x26: CPU distribuida igual entre processos. Estabiliza frametimes.",
                on => Reg(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", on ? 0x26 : 0x02));

            Add("Desativar UWP / Store", "SISTEMA", true, "SISTEMA",
                "Bloqueia apps UWP. CUIDADO: impede Calculadora, Fotos, Configuracoes e Xbox de abrirem.",
                on => Reg(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\WindowsStore", "DisableStoreApps", on ? 1 : 0));

            // ── JOGO ─────────────────────────────────────────────
            Add("Fullscreen + Desativar GameDVR", "JOGO", false, "JOGO",
                "Remove buffer Xbox que drena VRAM. Ativa fullscreen exclusivo. Elimina stutters e input lag.",
                on => {
                    Reg(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", on ? 0 : 1);
                    Reg(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "GameDVR_Enabled", on ? 0 : 1);
                    Reg(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", on ? 0 : 1);
                });

            Add("SMBS Driver — GTA / RDR", "JOGO", false, "JOGO",
                "Desativa o driver mssmbios. Necessario para GTA V e RDR2 que travam em alguns sistemas.",
                on => Reg(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\mssmbios", "Start", on ? 4 : 3));

            Add("Desativar DEP (Anticheat)", "JOGO", true, "JOGO",
                "bcdedit /set nx AlwaysOff. Fix para anticheats (GamersClub, Faceit). Requer reinicio. RISCO.",
                on => RunCmd("bcdedit", on ? "/set nx AlwaysOff" : "/set nx OptIn"));

            Add("Desativar 30+ Drivers", "JOGO", true, "JOGO",
                "Para 30+ servicos desnecessarios: telemetria, fax, WMP, Superfetch, busca. Libera CPU/RAM.",
                on => {
                    string[] drivers = {
                        "DiagTrack","dmwappushservice","WMPNetworkSvc","SysMain","MapsBroker",
                        "RetailDemo","wisvc","WbioSrvc","icssvc","SharedAccess","lmhosts",
                        "Fax","TapiSrv","Browser","RemoteRegistry","SSDPSRV","upnphost",
                        "XblAuthManager","XblGameSave","XboxNetApiSvc","XboxGipSvc",
                        "TabletInputService","WSearch","WerSvc","AJRouter","bthserv",
                        "EntAppSvc","PhoneSvc","PrintNotify","RpcLocator","SCardSvr",
                        "ScDeviceEnum","SensorDataService","SensrSvc","ShellHWDetection"
                    };
                    int val = on ? 4 : 3;
                    foreach (var d in drivers)
                        try { Reg(Registry.LocalMachine, $@"SYSTEM\CurrentControlSet\Services\{d}", "Start", val); } catch { }
                });

            // ── REDE ─────────────────────────────────────────────
            Add("Desativar AutoTuning TCP", "REDE", false, "REDE",
                "autotuninglevel=disabled. Evita bufferbloat e estabiliza ping online. Pode reduzir download.",
                on => RunCmd("netsh", $"interface tcp set global autotuninglevel={(on ? "disabled" : "normal")}"));

            Add("Desativar Adapter Offloads", "REDE", false, "REDE",
                "Desativa Chimney, RSS e NetDMA. Processamento de pacotes na CPU: latencia mais consistente.",
                on => {
                    string st = on ? "disabled" : "enabled";
                    RunCmd("netsh", $"int tcp set global chimney={st}");
                    RunCmd("netsh", $"int tcp set global rss={st}");
                    RunCmd("netsh", $"int tcp set global netdma={st}");
                });
        }

        void Add(string name, string tag, bool risky, string cat, string info, Action<bool> apply)
            => _tweaks.Add(new Tweak { Name = name, Tag = tag, Risky = risky, Category = cat, Info = info, Apply = apply });

        // ═══════════════════════════════════════════════════════════
        //  BUILD UI
        // ═══════════════════════════════════════════════════════════
        void BuildUI()
        {
            Text = "NT Tool";
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            ClientSize = new Size(FW, FH);
            MinimumSize = new Size(720, 480);
            BackColor = C_BG;
            Font = new Font("Segoe UI", 9f);
            StartPosition = FormStartPosition.CenterScreen;

            // Custom border
            Paint += (s, e) =>
            {
                using (var p = new Pen(Color.FromArgb(38, 38, 60), 1f))
                    e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            };

            // ── Header ───────────────────────────────────────────
            var hdr = new AnimHeader { Dock = DockStyle.Top };
            hdr.CloseClicked += (s, e) => Application.Exit();
            hdr.MinClicked += (s, e) => WindowState = FormWindowState.Minimized;

            // ── Toolbar ──────────────────────────────────────────
            var bar = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = C_BAR };
            bar.Paint += (s, e) =>
            {
                using (var p = new Pen(C_SEP)) { e.Graphics.DrawLine(p, 0, 0, bar.Width, 0); e.Graphics.DrawLine(p, 0, 39, bar.Width, 39); }
            };

            bool adm = IsAdmin();
            // Admin status pill
            // Admin pill — wider to fit full warning text
            int pillW = adm ? 140 : 310;
            var admPill = new Panel { Size = new Size(pillW, 24), Location = new Point(14, 8), BackColor = Color.Transparent };
            admPill.Paint += (s, e) =>
            {
                G.FillRound(e.Graphics, adm ? Color.FromArgb(20, 40, 25) : Color.FromArgb(40, 18, 18), new Rectangle(0, 0, admPill.Width - 1, admPill.Height - 1), 5);
                G.DrawRound(e.Graphics, adm ? Color.FromArgb(30, 120, 58) : Color.FromArgb(120, 38, 38), new Rectangle(0, 0, admPill.Width - 1, admPill.Height - 1), 5, 1f);
                // Status dot
                int cy = admPill.Height / 2;
                e.Graphics.FillEllipse(new SolidBrush(adm ? C_ADMIN : C_NOADM), 8, cy - 4, 8, 8);
                string txt = adm ? "Administrador" : "Sem Privilegios  —  Execute como Administrador";
                TextRenderer.DrawText(e.Graphics, txt,
                    new Font("Segoe UI", 7.2f, FontStyle.Bold),
                    new Rectangle(22, 0, admPill.Width - 26, admPill.Height),
                    adm ? C_ADMIN : C_NOADM,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
            bar.Controls.Add(admPill);

            _btnReset = MakeBtn("Resetar Tudo", C_RED, C_REDH, 112, 26);
            _btnReset.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            _btnReset.Click += (s, e) =>
            {
                if (MessageBox.Show("Reverter todos os tweaks para o padrao do Windows?", "Resetar Tudo",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                foreach (var r in _rows) r.Reset();
                SetStatus("Todos os tweaks revertidos ao padrao.", null);
            };
            bar.Controls.Add(_btnReset);
            bar.Resize += (s, e) => _btnReset.Location = new Point(bar.Width - 124, 7);
            _btnReset.Location = new Point(FW - 124, 7);

            // ── Status bar ───────────────────────────────────────
            var sbar = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = C_BAR };
            sbar.Paint += (s, e) => { using (var p = new Pen(C_SEP)) e.Graphics.DrawLine(p, 0, 0, sbar.Width, 0); };

            var hintLbl = new Label
            {
                Text = " \u25B6  1. Ativar   \u2192   2. Aplicar",
                Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                ForeColor = C_HINT,
                AutoSize = true,
                Location = new Point(12, 8),
                BackColor = Color.Transparent
            };

            _status = new StatusLabel { Location = new Point(190, 0), Height = 30, AutoSize = false };
            _status.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;

            sbar.Controls.Add(hintLbl);
            sbar.Controls.Add(_status);
            sbar.Resize += (s, e) => _status.Width = sbar.Width - 200;
            _status.Width = FW - 200;

            // ── Scroll body ───────────────────────────────────────
            _scroll = new DarkScrollPanel { Dock = DockStyle.Fill };

            int cw = (FW - 48) / 2;

            // ── Left column — SISTEMA ─────────────────────────────
            _colL = new Panel { BackColor = Color.Transparent, Width = cw, Location = new Point(0, 0) };
            var shSistema = new SectionHeader("SISTEMA") { Width = cw - 4, Location = new Point(0, 2) };
            _colL.Controls.Add(shSistema);
            int ly = 34;
            foreach (var t in _tweaks)
            {
                if (t.Category != "SISTEMA") continue;
                var row = new TweakRow(t, this) { Width = cw - 4, Location = new Point(0, ly) };
                _rows.Add(row); _colL.Controls.Add(row); ly += ROW_GAP;
            }
            _colL.Height = ly + 8;

            // ── Right column — JOGO + REDE ────────────────────────
            _colR = new Panel { BackColor = Color.Transparent, Width = cw, Location = new Point(cw + 16, 0) };
            var shJogo = new SectionHeader("JOGO") { Width = cw - 4, Location = new Point(0, 2) };
            _colR.Controls.Add(shJogo);
            int ry = 34;
            foreach (var t in _tweaks)
            {
                if (t.Category != "JOGO") continue;
                var row = new TweakRow(t, this) { Width = cw - 4, Location = new Point(0, ry) };
                _rows.Add(row); _colR.Controls.Add(row); ry += ROW_GAP;
            }
            var shRede = new SectionHeader("REDE") { Width = cw - 4, Location = new Point(0, ry + 8) };
            _colR.Controls.Add(shRede); ry += 40;
            foreach (var t in _tweaks)
            {
                if (t.Category != "REDE") continue;
                var row = new TweakRow(t, this) { Width = cw - 4, Location = new Point(0, ry) };
                _rows.Add(row); _colR.Controls.Add(row); ry += ROW_GAP;
            }
            _colR.Height = ry + 8;

            // Set inner height and place columns
            _scroll.Inner.Height = Math.Max(_colL.Height, _colR.Height) + 24;
            _scroll.Inner.Controls.Add(_colL);
            _scroll.Inner.Controls.Add(_colR);

            // Hook drag/scroll on all inner children
            _scroll.HookChildren();

            // Responsive resize
            _scroll.Resize += (s, e) => Reposition();

            Controls.Add(_scroll);
            Controls.Add(bar);
            Controls.Add(hdr);
            Controls.Add(sbar);
        }

        void Reposition()
        {
            if (_colL == null || _scroll == null) return;
            int cw = (_scroll.Inner.Width - 16) / 2;
            _colL.Width = cw; _colL.Left = 0;
            _colR.Width = cw; _colR.Left = cw + 16;
            foreach (var row in _rows) { row.Width = row.Parent.Width - 4; row.PerformLayout(); }
        }

        // ── UI factory helpers ────────────────────────────────────
        Button MakeBtn(string txt, Color bg, Color hov, int w, int h)
        {
            var b = new Button
            {
                Text = txt,
                Size = new Size(w, h),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(240, 240, 250),
                BackColor = bg,
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = hov;
            return b;
        }

        // ── Public helpers ────────────────────────────────────────
        public void SetStatus(string msg, Color? col = null)
        {
            bool success = msg.StartsWith("\u2713");
            bool error = msg.StartsWith("\u2717");
            Color c = col ?? (success ? Color.FromArgb(52, 185, 92) : error ? Color.FromArgb(200, 60, 60) : Color.FromArgb(72, 72, 108));

            if (_status.InvokeRequired)
                _status.Invoke(new Action(() => _status.SetMsg(msg, c)));
            else
                _status.SetMsg(msg, c);
        }

        public bool IsAdmin()
        {
            try { using (var id = WindowsIdentity.GetCurrent()) return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); }
            catch { return false; }
        }

        // ── Registry / cmd helpers ────────────────────────────────
        static void Reg(RegistryKey root, string path, string name, int val)
        {
            using (var k = root.CreateSubKey(path)) k?.SetValue(name, val, RegistryValueKind.DWord);
        }

        static void RunCmd(string exe, string args)
        {
            try
            {
                var p = new Process();
                p.StartInfo = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true };
                p.Start(); p.WaitForExit();
            }
            catch { }
        }

    }
}