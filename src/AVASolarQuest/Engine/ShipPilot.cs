namespace AVASolarQuest.Engine;

/// <summary>
/// The computer player, for the demonstration (and the tests). First it keeps out of the sun: it looks a second or
/// so ahead along its path, and if that falls into the sun it turns and burns away. Then it fights: the enemy it can
/// bring its gun to bear on soonest, led for where it'll be when the bolt gets there. With nothing to shoot it goes
/// for the survivors, nearest first, before the sun takes them. A shot it can't avoid, it jumps into hyperspace.
/// </summary>
public sealed class ShipPilot
{
    /// <summary>What it is up to, for the probe.</summary>
    public string Doing { get; private set; } = "";

    public const int LookEvery = 6;

    private int _tick;
    private object? _target;

    public Controls Decide(GameEngine g)
    {
        _tick++;
        var s = g.Ship;
        if (g.Phase is not (Phase.Play or Phase.Start or Phase.Clear) || !s.Alive) { Doing = "waiting"; _target = null; return default; }
        // A shot about to hit, that there's no getting out of the way of: jump.
        if (_tick % 3 == 0 && s.HyperIn <= 0 && s.Safe <= 0 && Incoming(g, s) < 0.18) { Doing = "hyperspace"; return new Controls(Hyper: true); }
        // The sun ahead: away from it, whatever else.
        if (Falling(s) is { } away)
        {
            double diff = Diff(away, s.Angle);
            Doing = "escaping the sun";
            return new Controls(Math.Abs(diff) < 0.05 ? 0 : Math.Sign(diff), Math.Abs(diff) < 1.2, Fire: false);
        }
        if (_tick % LookEvery == 0 || !Exists(g, _target)) _target = Choose(g);
        if (_target == null) { Doing = "drifting"; return Cruise(s); }
        var (tx, ty, tvx, tvy, r) = Where(_target);
        double dx = Field.Dx(s.X, tx), dy = Field.Dy(s.Y, ty);
        if (_target is Pod)
        {
            // To the pod: aim where it's going, burn towards it, but not too fast to turn.
            double time = Math.Sqrt(dx * dx + dy * dy) / 200;
            double ax = dx + (tvx - s.Vx) * time, ay = dy + (tvy - s.Vy) * time, want = Math.Atan2(ay, ax), off = Diff(want, s.Angle);
            double closing = -(dx * (tvx - s.Vx) + dy * (tvy - s.Vy)) / Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
            Doing = "to a survivor";
            return new Controls(Math.Abs(off) < 0.06 ? 0 : Math.Sign(off), Math.Abs(off) < 0.4 && closing < 220 && SafeToThrust(s, want));
        }
        // Lead the target for the bolt's flight.
        double t = Math.Sqrt(dx * dx + dy * dy) / GameEngine.BoltSpeed;
        for (int i = 0; i < 3; i++) t = Math.Sqrt(Sq(dx + (tvx - s.Vx * 0.5) * t) + Sq(dy + (tvy - s.Vy * 0.5) * t)) / GameEngine.BoltSpeed;
        dx += (tvx - s.Vx * 0.5) * t;
        dy += (tvy - s.Vy * 0.5) * t;
        double dist = Math.Sqrt(dx * dx + dy * dy), aim = Math.Atan2(dy, dx), d2 = Diff(aim, s.Angle);
        bool lined = Math.Abs(d2) < Math.Atan2(r * 0.8, Math.Max(dist, 1)) + 0.02;
        bool fire = lined && dist < GameEngine.BoltSpeed * GameEngine.BoltLife * 0.9;
        // Never through a survivor.
        if (fire && g.Pods.Any(p => InLine(s, p, dist))) fire = false;
        double speed = Math.Sqrt(s.Vx * s.Vx + s.Vy * s.Vy);
        bool thrust = Math.Abs(d2) < 0.3 && dist > 380 && speed < 160 && SafeToThrust(s, s.Angle);
        Doing = fire ? "firing" : thrust ? "closing in" : "aiming";
        return new Controls(Math.Abs(d2) < 0.03 ? 0 : Math.Sign(d2), thrust, fire);
    }

    private static double Sq(double v) => v * v;

    /// <summary>Whether a pod is near the line a bolt from the nose would take, short of the target.</summary>
    private static bool InLine(Ship s, Pod p, double range)
    {
        double dx = Field.Dx(s.X, p.X), dy = Field.Dy(s.Y, p.Y), fx = Math.Cos(s.Angle), fy = Math.Sin(s.Angle);
        double along = dx * fx + dy * fy, off = Math.Abs(dx * fy - dy * fx);
        return along > 0 && along < range + 30 && off < Pod.Radius + 10;
    }
    private static double Diff(double a, double b) => Math.IEEERemainder(a - b, Math.PI * 2);

    private static bool Exists(GameEngine g, object? t) => t switch
    {
        Enemy e => !e.Dead && g.Enemies.Contains(e),
        Pod p => g.Pods.Contains(p),
        _ => false,
    };

    private static (double X, double Y, double Vx, double Vy, double R) Where(object t) => t switch
    {
        Enemy e => (e.X, e.Y, e.Vx, e.Vy, e.Radius),
        Pod p => (p.X, p.Y, p.Vx, p.Vy, Pod.Radius),
        _ => default,
    };

    /// <summary>
    /// What to go for: the enemy it can hit soonest (the close and the quick ones first); or, with none near, a
    /// survivor, the one nearest falling into the sun first.
    /// </summary>
    private static object? Choose(GameEngine g)
    {
        var s = g.Ship;
        object? best = null;
        double cost = double.MaxValue;
        foreach (var e in g.Enemies)
        {
            double dx = Field.Dx(s.X, e.X), dy = Field.Dy(s.Y, e.Y), d = Math.Sqrt(dx * dx + dy * dy);
            double c = Math.Abs(Diff(Math.Atan2(dy, dx), s.Angle)) / GameEngine.TurnRate + d / GameEngine.BoltSpeed;
            if (e.Kind == EnemyKind.Hunter) c *= 0.5;
            if (d < 180) c *= 0.5;
            if (c < cost) { cost = c; best = e; }
        }
        if (best is Enemy near && Field.Distance(s.X, s.Y, near.X, near.Y) < 420) return best;
        foreach (var p in g.Pods)
        {
            double d = Field.Distance(s.X, s.Y, p.X, p.Y), c = d / 200 + Field.FromSun(p.X, p.Y) / 300 + 1;
            if (Field.FromSun(p.X, p.Y) < Field.SunRadius + 70) continue;
            if (c < cost) { cost = c; best = p; }
        }
        return best;
    }

    /// <summary>
    /// If the ship's course falls into the sun within the next while, coasting: the way to burn to get clear of it
    /// (out from the sun, and along the way it's already going round it).
    /// </summary>
    private static double? Falling(Ship s)
    {
        var me = new Ship { X = s.X, Y = s.Y, Vx = s.Vx, Vy = s.Vy, Angle = s.Angle };
        for (int t = 0; t < 70; t++)
        {
            GameEngine.Move(me, default, Field.Step);
            if (Field.FromSun(me.X, me.Y) < Field.SunRadius + 40)
            {
                double ox = Field.Dx(Field.SunX, s.X), oy = Field.Dy(Field.SunY, s.Y), od = Math.Max(1, Math.Sqrt(ox * ox + oy * oy));
                // Along the orbit, the way it's going round, plus outwards.
                double side = Math.Sign(ox * s.Vy - oy * s.Vx);
                if (side == 0) side = 1;
                double tx = -oy / od * side, ty = ox / od * side;
                return Math.Atan2(ty + oy / od * 0.8, tx + ox / od * 0.8);
            }
        }
        return null;
    }

    /// <summary>Whether a burn this way for a moment keeps clear of the sun.</summary>
    private static bool SafeToThrust(Ship s, double angle)
    {
        var me = new Ship { X = s.X, Y = s.Y, Vx = s.Vx, Vy = s.Vy, Angle = angle };
        for (int t = 0; t < 60; t++)
        {
            GameEngine.Move(me, new Controls(Thrust: t < 20), Field.Step);
            if (Field.FromSun(me.X, me.Y) < Field.SunRadius + 50) return false;
        }
        return true;
    }

    /// <summary>With nothing to do, keep a steady orbit: burn along it if it's too slow to stay up.</summary>
    private static Controls Cruise(Ship s)
    {
        double ox = Field.Dx(Field.SunX, s.X), oy = Field.Dy(Field.SunY, s.Y), r = Math.Max(1, Math.Sqrt(ox * ox + oy * oy));
        double side = Math.Sign(ox * s.Vy - oy * s.Vx);
        if (side == 0) side = 1;
        double want = Math.Atan2(ox / r * side, -oy / r * side), diff = Diff(want, s.Angle);
        double tangential = (-oy * s.Vx + ox * s.Vy) / r * side;
        bool slow = tangential < Field.OrbitSpeed(r) * 0.9;
        return new Controls(Math.Abs(diff) < 0.05 ? 0 : Math.Sign(diff), slow && Math.Abs(diff) < 0.3);
    }

    /// <summary>How soon an enemy bolt will hit the ship if nothing changes (a large number if none will).</summary>
    private static double Incoming(GameEngine g, Ship s)
    {
        double soonest = double.MaxValue;
        foreach (var b in g.Bolts.Where(b => b.Hostile))
        {
            double px = Field.Dx(s.X, b.X), py = Field.Dy(s.Y, b.Y), vx = b.Vx - s.Vx, vy = b.Vy - s.Vy, vv = vx * vx + vy * vy;
            if (vv < 1) continue;
            double t = -(px * vx + py * vy) / vv;
            if (t < 0 || t > 0.5) continue;
            double cx = px + vx * t, cy = py + vy * t;
            if (Math.Sqrt(cx * cx + cy * cy) < Ship.Radius + 2) soonest = Math.Min(soonest, t);
        }
        foreach (var e in g.Enemies.Where(e => e.Kind == EnemyKind.Hunter))
            if (Field.Distance(s.X, s.Y, e.X, e.Y) < e.Radius + Ship.Radius + 20) soonest = 0;
        return soonest;
    }
}
