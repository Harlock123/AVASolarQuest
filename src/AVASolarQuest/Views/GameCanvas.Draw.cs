using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVASolarQuest.Engine;
using Effect = AVASolarQuest.Engine.Effect;

namespace AVASolarQuest.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVASolarQuest/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Fire = 0xFFFF8A2A, Cyan = 0xFF6AE0FF;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);



    /// <summary>A line as a vector display draws it: a soft glow under a bright core.</summary>
    private static void Glow(DrawingContext dc, Point a, Point b, uint colour, double alpha = 1, double width = 2)
    {
        if (alpha <= 0.01) return;
        dc.DrawLine(Pen(Alpha(colour, 0.22 * alpha), width * 3.2), a, b);
        dc.DrawLine(Pen(Alpha(colour, alpha), width), a, b);
    }

    /// <summary>A closed outline of points given about (0, 0), turned and placed.</summary>
    private static void Shape(DrawingContext dc, double x, double y, double angle, double k, uint colour, double alpha, params (double X, double Y)[] pts)
    {
        double c = Math.Cos(angle), s = Math.Sin(angle);
        Point At((double X, double Y) p) => new(x + (p.X * c - p.Y * s) * k, y + (p.X * s + p.Y * c) * k);
        for (int i = 0; i < pts.Length; i++) Glow(dc, At(pts[i]), At(pts[(i + 1) % pts.Length]), colour, alpha);
    }

    private bool InGame => Engine.State is GameState.Playing or GameState.Attract or GameState.Paused or GameState.GameOver;

    /// <summary>Draws something wherever it shows: near an edge, its other half shows at the far side too, as space wraps.</summary>
    private static void Wrapped(double x, double y, double r, Action<double, double> draw)
    {
        draw(x, y);
        if (x < r) draw(x + Field.Width, y);
        if (x > Field.Width - r) draw(x - Field.Width, y);
        if (y < r) draw(x, y + Field.Height);
        if (y > Field.Height - r) draw(x, y - Field.Height);
    }

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF020208), new Rect(0, 0, Field.Width, Field.Height));
        DrawStars(dc);
        DrawSun(dc, Field.SunX, Field.SunY, 1);
        if (InGame)
        {
            foreach (var p in g.Pods) Wrapped(p.X, p.Y, 20, (x, y) => DrawPod(dc, x, y, p.T, Field.FromSun(p.X, p.Y) < 110, 1));
            foreach (var e in g.Enemies) Wrapped(e.X, e.Y, e.Radius + 8, (x, y) => DrawEnemy(dc, e.Kind, x, y, e.Angle, e.T, e.Hit > 0, 1));
            foreach (var d in g.Wreckage)
            {
                double a = Math.Clamp(1 - d.Age / d.Life, 0, 1), c = Math.Cos(d.Angle) * d.Length / 2, s = Math.Sin(d.Angle) * d.Length / 2;
                Glow(dc, new Point(d.X - c, d.Y - s), new Point(d.X + c, d.Y + s), d.Colour, a, 1.6);
            }
            foreach (var b in g.Bolts)
            {
                if (b.Hostile)
                {
                    dc.DrawEllipse(Brush(0x50FF5A3A), null, new Point(b.X, b.Y), 6, 6);
                    dc.DrawEllipse(Brush(0xFFFFB08A), null, new Point(b.X, b.Y), 2.6, 2.6);
                    continue;
                }
                double l = Math.Max(1, Math.Sqrt(b.Vx * b.Vx + b.Vy * b.Vy));
                Glow(dc, new Point(b.X - b.Vx / l * 10, b.Y - b.Vy / l * 10), new Point(b.X, b.Y), 0xFFE8FFE8, 1, 2.2);
            }
            DrawShip(dc, g.Ship);
        }
        foreach (var e in g.Effects) DrawEffect(dc, e);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(Field.Width / 2, 300), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(Field.Width / 2, Field.Height - 16), 24, Yellow, center: true);
    }

    private void DrawStars(DrawingContext dc)
    {
        for (int i = 0; i < 170; i++)
        {
            uint h = Hash(i, 5, 3);
            double x = h % 1024, y = (h >> 10) % 768, tw = 0.3 + 0.4 * Math.Abs(Math.Sin(_clock * (0.6 + (h >> 20) % 5 * 0.3) + i));
            dc.DrawEllipse(Brush(Alpha(0xFFDDE4FF, tw)), null, new Point(x, y), (h >> 6) % 7 == 0 ? 1.4 : 0.8, (h >> 6) % 7 == 0 ? 1.4 : 0.8);
        }
    }

    /// <summary>
    /// The sun: a glow spreading out from it, rays flickering round its rim, a hot white core, and faint rings
    /// further out marking how its pull falls away.
    /// </summary>
    private void DrawSun(DrawingContext dc, double x, double y, double k)
    {
        var c = new Point(x, y);
        foreach (double ring in new[] { 120.0, 240, 360 })
            for (int i = 0; i < 48; i += 2)
            {
                double a0 = i * Math.PI / 24 + _clock * 0.05, a1 = a0 + Math.PI / 30;
                dc.DrawLine(Pen(Alpha(0xFFFFB04A, 0.08 * k), 1), new Point(x + Math.Cos(a0) * ring * k, y + Math.Sin(a0) * ring * k), new Point(x + Math.Cos(a1) * ring * k, y + Math.Sin(a1) * ring * k));
            }
        for (int i = 6; i >= 1; i--)
            dc.DrawEllipse(Brush(Alpha(0xFFFF8A2A, 0.05 + 0.02 * (6 - i))), null, c, Field.SunRadius * k * (1 + i * 0.45), Field.SunRadius * k * (1 + i * 0.45));
        for (int i = 0; i < 16; i++)
        {
            double a = i * Math.PI / 8 + _clock * 0.3, len = (14 + 10 * Math.Abs(Math.Sin(_clock * 3 + i * 1.7))) * k;
            Glow(dc, new Point(x + Math.Cos(a) * Field.SunRadius * k, y + Math.Sin(a) * Field.SunRadius * k),
                new Point(x + Math.Cos(a) * (Field.SunRadius * k + len), y + Math.Sin(a) * (Field.SunRadius * k + len)), 0xFFFFC04A, 0.7, 2);
        }
        dc.DrawEllipse(Brush(0xFFFFB03A), null, c, Field.SunRadius * k, Field.SunRadius * k);
        dc.DrawEllipse(Brush(0xFFFFE07A), null, c, Field.SunRadius * k * 0.75, Field.SunRadius * k * 0.75);
        dc.DrawEllipse(Brush(0xFFFFFFE0), null, c, Field.SunRadius * k * 0.42, Field.SunRadius * k * 0.42);
        dc.DrawEllipse(null, Pen(Alpha(0xFFFFE07A, 0.9), 2), c, Field.SunRadius * k + 2, Field.SunRadius * k + 2);
    }

    // ───────────────────────────── the ship ─────────────────────────────

    private void DrawShip(DrawingContext dc, Ship s)
    {
        if (!s.Alive) return;
        if (s.Flash > 0) dc.DrawEllipse(null, Pen(Alpha(0xFF8AF0FF, s.Flash * 2), 2), new Point(s.X, s.Y), 40 * (0.4 - s.Flash) + 14, 40 * (0.4 - s.Flash) + 14);
        if (s.Safe > 0 && (int)(_clock * 10) % 2 == 0) return;
        Wrapped(s.X, s.Y, 30, (x, y) => DrawShipAt(dc, x, y, s.Angle, s.Thrusting, 1.2));
    }

    private void DrawShipAt(DrawingContext dc, double x, double y, double angle, bool thrust, double k)
    {
        Shape(dc, x, y, angle, k, GameEngine.ShipColour, 1, (16, 0), (-10, -11), (-5, -4), (-5, 4), (-10, 11));
        Shape(dc, x, y, angle, k, Alpha(GameEngine.ShipColour, 0.7), 0.7, (6, 0), (0, -3), (0, 3));
        if (thrust && (int)(_clock * 24) % 2 == 0)
        {
            double c = Math.Cos(angle), s = Math.Sin(angle);
            Point At(double fx, double fy) => new(x + (fx * c - fy * s) * k, y + (fx * s + fy * c) * k);
            Glow(dc, At(-6, -3), At(-18 - Math.Sin(_clock * 40) * 3, 0), 0xFFFFB04A, 1, 1.8);
            Glow(dc, At(-6, 3), At(-18 - Math.Sin(_clock * 40) * 3, 0), 0xFFFFB04A, 1, 1.8);
        }
    }

    // ───────────────────────────── the enemy, and the survivors ─────────────────────────────

    /// <summary>An enemy in glowing lines: the fighter a swept dart, the orbiter a saucer with lights running round, the cruiser a long hull with fins, the hunter a spinning spiked star.</summary>
    private static void DrawEnemy(DrawingContext dc, EnemyKind kind, double x, double y, double angle, double t, bool hit, double k)
    {
        uint col = hit ? 0xFFFFFFFF : GameEngine.Colour(kind);
        switch (kind)
        {
            case EnemyKind.Fighter:
                Shape(dc, x, y, angle, k, col, 1, (14, 0), (-4, -5), (-12, -12), (-8, 0), (-12, 12), (-4, 5));
                Glow(dc, new Point(x, y), new Point(x + Math.Cos(angle) * 6 * k, y + Math.Sin(angle) * 6 * k), col, 0.7, 1.4);
                break;
            case EnemyKind.Orbiter:
            {
                Shape(dc, x, y, 0, k, col, 1, (-17, 0), (-8, -6), (8, -6), (17, 0), (8, 6), (-8, 6));
                Shape(dc, x, y, 0, k, col, 0.8, (-6, -6), (-3, -11), (3, -11), (6, -6));
                for (int i = 0; i < 4; i++)
                {
                    double a = t * 4 + i * Math.PI / 2;
                    dc.DrawEllipse(Brush(0xFFFFFFFF), null, new Point(x + Math.Cos(a) * 12 * k, y + Math.Sin(a) * 3 * k + 1), 1.6, 1.6);
                }
                break;
            }
            case EnemyKind.Cruiser:
                Shape(dc, x, y, angle, k, col, 1, (26, 0), (14, -8), (-16, -8), (-24, -16), (-20, -4), (-20, 4), (-24, 16), (-16, 8), (14, 8));
                Shape(dc, x, y, angle, k, col, 0.7, (6, -4), (10, 0), (6, 4), (-4, 4), (-4, -4));
                Glow(dc, new Point(x - Math.Cos(angle) * 20 * k, y - Math.Sin(angle) * 20 * k), new Point(x - Math.Cos(angle) * (26 + Math.Sin(t * 20) * 3) * k, y - Math.Sin(angle) * (26 + Math.Sin(t * 20) * 3) * k), 0xFFFFB04A, 0.8, 2);
                break;
            default:
            {
                var pts = new (double, double)[12];
                for (int i = 0; i < 12; i++)
                {
                    double a = i * Math.PI / 6, r = i % 2 == 0 ? 15 : 6;
                    pts[i] = (Math.Cos(a) * r, Math.Sin(a) * r);
                }
                Shape(dc, x, y, t * 5, k, col, 1, pts);
                break;
            }
        }
    }

    /// <summary>A survivor in a pod: a little capsule with someone inside waving, and a beacon (red when the sun is close).</summary>
    private void DrawPod(DrawingContext dc, double x, double y, double t, bool danger, double k)
    {
        uint col = GameEngine.PodColour;
        dc.DrawRectangle(null, Pen(Alpha(col, 0.25), 6), new Rect(x - 8 * k, y - 10 * k, 16 * k, 20 * k), 7 * k, 7 * k);
        dc.DrawRectangle(null, Pen(col, 1.4), new Rect(x - 8 * k, y - 10 * k, 16 * k, 20 * k), 7 * k, 7 * k);
        // The figure: head, body, legs, and arms waving.
        dc.DrawEllipse(null, Pen(0xFF8AFFB0, 1.2), new Point(x, y - 5 * k), 2 * k, 2 * k);
        dc.DrawLine(Pen(0xFF8AFFB0, 1.2), new Point(x, y - 3 * k), new Point(x, y + 3 * k));
        dc.DrawLine(Pen(0xFF8AFFB0, 1.2), new Point(x, y + 3 * k), new Point(x - 2.5 * k, y + 7 * k));
        dc.DrawLine(Pen(0xFF8AFFB0, 1.2), new Point(x, y + 3 * k), new Point(x + 2.5 * k, y + 7 * k));
        double wave = Math.Sin(t * 10) * 3 * k;
        dc.DrawLine(Pen(0xFF8AFFB0, 1.2), new Point(x, y - 1 * k), new Point(x - 4 * k, y - 4 * k - wave));
        dc.DrawLine(Pen(0xFF8AFFB0, 1.2), new Point(x, y - 1 * k), new Point(x + 4 * k, y - 4 * k + wave));
        if ((int)(t * (danger ? 8 : 3)) % 2 == 0) dc.DrawEllipse(Brush(danger ? 0xFFFF4A3A : 0xFF8AFFB0), null, new Point(x, y - 12 * k), 1.8 * k, 1.8 * k);
    }

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double t = e.Age / e.Life;
        var c = new Point(e.X, e.Y);
        switch (e.Kind)
        {
            case EffectKind.Burst:
            {
                double r = (6 + 30 * t) * e.Size;
                dc.DrawEllipse(null, Pen(Alpha(e.Colour, 1 - t), 2.5), c, r, r);
                break;
            }
            case EffectKind.Flare:
            {
                double r = 6 + 40 * t;
                dc.DrawEllipse(Brush(Alpha(0xFFFFD07A, 0.6 * (1 - t))), null, c, r, r);
                dc.DrawEllipse(null, Pen(Alpha(0xFFFF8A2A, 1 - t), 2), c, r * 1.3, r * 1.3);
                break;
            }
            case EffectKind.Score:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 24), 20, e.Colour, center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 30), 30, e.Colour, center: true);
                break;
        }
    }

    // ───────────────────────────── the score bar, and the screens ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        Text(dc, "SCORE", new Point(16, 16), 22, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(16, 46), 36, Yellow);
        Text(dc, "HI", new Point(Field.Width - 16, 16), 22, Dim, right: true);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(Field.Width - 16, 46), 30, Dim, right: true);
        if (!InGame) return;
        Text(dc, $"WAVE {g.Wave}", new Point(512, 18), 26, 0xFFFFC04A, center: true);
        if (g.Chain > 0) Text(dc, $"RESCUE CHAIN x{g.Chain}", new Point(512, 48), 22, 0xFF8AFFB0, center: true);
        int spare = Math.Max(0, g.Lives - (g.Ship.Alive ? 1 : 0));
        for (int i = 0; i < Math.Min(spare, 6); i++) DrawShipAt(dc, 210 + i * 28, 40, -Math.PI / 2, false, 0.7);
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.Width / 2, cy = 220;
        var (w, h) = (Math.Max(msg.Length * 26, (g.SubMessage?.Length ?? 0) * 15) + 60, g.SubMessage != null ? 106.0 : 70.0);
        dc.FillRectangle(Brush(0xB0080610), new Rect(cx - w / 2, cy - 46, w, h), 6);
        dc.DrawRectangle(null, Pen(Alpha(0xFFFFC04A, 0.5), 1.2), new Rect(cx - w / 2, cy - 46, w, h), 6);
        Text(dc, msg, new Point(cx, cy - 12), msg.Length > 14 ? 42 : 54, g.Phase == Phase.Dying ? Red : Yellow, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 34), 26, White, center: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        string title = "AVA SOLAR QUEST";
        uint[] hues = [0xFFFFE07A, 0xFFFFB04A, 0xFFFF8A2A, 0xFF8AFF8A];
        for (int i = 0; i < title.Length; i++)
            Text(dc, title[i].ToString(), new Point(cx - title.Length * 25 + 25 + i * 50, 92), 96, hues[(i + (int)(_clock * 3)) % hues.Length], center: true);
        Text(dc, "AFTER THE 1981 VECTOR ARCADE CLASSIC", new Point(cx, 152), 26, Dim, center: true);
        // A ship going round the sun.
        double a = _clock * 0.7;
        DrawShipAt(dc, cx + Math.Cos(a) * 160, Field.SunY + Math.Sin(a) * 60, a + Math.PI / 2, true, 1.2);
        Text(dc, "THE SUN PULLS ON EVERYTHING - SHOOT THE ENEMY, THEN RESCUE ITS CREW BEFORE THEY FALL IN", new Point(cx, 500), 21, White, center: true);
        (string Name, string Points, Action<double> Draw)[] legend =
        [
            ("FIGHTER", "200", x => DrawEnemy(dc, EnemyKind.Fighter, x, 556, -Math.PI / 2, _clock, false, 1.3)),
            ("ORBITER", "300", x => DrawEnemy(dc, EnemyKind.Orbiter, x, 556, 0, _clock, false, 1.3)),
            ("HUNTER", "400", x => DrawEnemy(dc, EnemyKind.Hunter, x, 556, 0, _clock, false, 1.3)),
            ("CRUISER", "500", x => DrawEnemy(dc, EnemyKind.Cruiser, x, 556, -Math.PI / 2, _clock, false, 1)),
            ("SURVIVOR", "500 x CHAIN", x => DrawPod(dc, x, 556, _clock, false, 1.4)),
        ];
        for (int i = 0; i < legend.Length; i++)
        {
            double x = cx - 360 + i * 180;
            legend[i].Draw(x);
            Text(dc, legend[i].Name, new Point(x, 600), 20, Dim, center: true);
            Text(dc, legend[i].Points, new Point(x, 624), 20, Yellow, center: true);
        }
        if ((int)(_clock * 2) % 2 == 0)
            Text(dc, "PRESS ENTER OR 1 TO PLAY", new Point(cx, 672), 40, Yellow, center: true);
        Text(dc, "ARROWS: TURN AND THRUST    SPACE: FIRE    DOWN OR SHIFT: HYPERSPACE", new Point(cx, 714), 21, Dim, center: true);
        Text(dc, $"P: PAUSE    M: SOUND    EXTRA SHIP EVERY {GameEngine.ExtraLifeEvery}    GAMEPAD: STICK, A, X", new Point(cx, 740), 20, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xC0020208), new Rect(0, 0, Field.Width, Field.Height));
        Text(dc, "SOLAR CHAMPIONS", new Point(cx, 140), 68, 0xFFFFC04A, center: true);
        Text(dc, "RANK NAME    SCORE    WAVE", new Point(cx, 204), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 248 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 710), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xC0020208), new Rect(0, 0, Field.Width, Field.Height));
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
