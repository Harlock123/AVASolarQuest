namespace AVASolarQuest.Engine;

/// <summary>The controls: turning (-1 left, 1 right), thrust, fire, and the hyperspace jump.</summary>
public readonly record struct Controls(int Turn = 0, bool Thrust = false, bool Fire = false, bool Hyper = false);

/// <summary>
/// Space: the whole screen, wrapping round at the edges, with the sun in the middle of it pulling on everything.
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    /// <summary>The sun: where it is, how big, and how hard it pulls.</summary>
    public const double SunX = Width / 2, SunY = Height / 2 + 20, SunRadius = 34, Pull = 3.4e6;

    /// <summary>The shortest way from one coordinate to another, round the wrap if that's shorter.</summary>
    public static double Delta(double from, double to, double size)
    {
        double d = to - from;
        if (d > size / 2) d -= size;
        else if (d < -size / 2) d += size;
        return d;
    }

    public static double Dx(double from, double to) => Delta(from, to, Width);
    public static double Dy(double from, double to) => Delta(from, to, Height);
    public static double Distance(double ax, double ay, double bx, double by) => Math.Sqrt(Dx(ax, bx) * Dx(ax, bx) + Dy(ay, by) * Dy(ay, by));
    public static double WrapX(double x) => (x % Width + Width) % Width;
    public static double WrapY(double y) => (y % Height + Height) % Height;

    /// <summary>How far a point is from the middle of the sun.</summary>
    public static double FromSun(double x, double y) => Distance(x, y, SunX, SunY);

    /// <summary>
    /// The sun's pull on something at (x, y): towards it, falling off with the square of the distance (but not
    /// without limit close in).
    /// </summary>
    public static (double Ax, double Ay) Gravity(double x, double y, double strength = 1)
    {
        double dx = Dx(x, SunX), dy = Dy(y, SunY), d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy)), r = Math.Max(d, 60);
        double a = Pull * strength / (r * r);
        return (dx / d * a, dy / d * a);
    }

    /// <summary>The speed of a circular orbit round the sun at this distance.</summary>
    public static double OrbitSpeed(double r) => Math.Sqrt(Pull / r);
}

/// <summary>The player's ship: turns, thrusts and coasts, with the sun always pulling.</summary>
public sealed class Ship
{
    public double X, Y, Vx, Vy, Angle = -Math.PI / 2;
    public bool Alive = true, Thrusting;
    public double Safe, FireIn, HyperIn, Flash;
    public const double Radius = 12;
}

public sealed class Bolt
{
    public double X, Y, Vx, Vy, Life;
    /// <summary>Fired by the enemy, at the ship.</summary>
    public bool Hostile;
}

public enum EnemyKind
{
    /// <summary>A dart that hunts the ship and fires when it has it in front of it.</summary>
    Fighter,
    /// <summary>A saucer that circles the sun, firing at the ship as it passes.</summary>
    Orbiter,
    /// <summary>A big, slow cruiser: three hits, a spread of three shots, and two crew to rescue.</summary>
    Cruiser,
    /// <summary>A spiked hunter, quick, that tries to ram.</summary>
    Hunter,
}

public sealed class Enemy
{
    public EnemyKind Kind;
    public double X, Y, Vx, Vy, Angle, T, FireIn, Hit;
    /// <summary>For an orbiter: how far out it circles, and which way round.</summary>
    public double Orbit, Way = 1;
    public int Hits = 1;
    public bool Dead;

    public double Radius => Kind switch { EnemyKind.Cruiser => 24, EnemyKind.Orbiter => 16, _ => 13 };

    public int Points => Kind switch { EnemyKind.Fighter => 200, EnemyKind.Orbiter => 300, EnemyKind.Hunter => 400, _ => 500 };

    /// <summary>How many of the crew get out in pods when it's shot down.</summary>
    public int Crew => Kind == EnemyKind.Cruiser ? 2 : 1;
}

/// <summary>A survivor in an escape pod, drifting, slowly falling into the sun unless the ship picks him up.</summary>
public sealed class Pod
{
    public double X, Y, Vx, Vy, T;
    public const double Radius = 9;
}

/// <summary>A broken-off line, spinning away.</summary>
public sealed class Debris
{
    public double X, Y, Vx, Vy, Angle, Spin, Length, Age, Life = 1;
    public uint Colour;
}

public enum EffectKind { Score, Burst, Word, Flare }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.8, Size = 1;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}
