namespace AVASolarQuest.Engine;

public sealed partial class GameEngine
{
    public const double TurnRate = 4.4, ThrustPower = 360, Drag = 0.25, MaxSpeed = 460;
    public const double BoltSpeed = 640, BoltLife = 0.8, FireGap = 0.15, BoltPull = 0.35;
    public const int MaxBolts = 4;
    public const double HyperGap = 1.5, SafeSeconds = 2.5, StartSeconds = 2, DyingSeconds = 2.4, ClearSeconds = 3.2;
    public const double StartOrbit = 260, PodDrag = 0.05, PodLife = 40, PodGrace = 0.6;
    public const int ExtraLifeEvery = 10000, RescuePoints = 500, MaxChain = 5;

    /// <summary>The wave: 1 for the first.</summary>
    public int Wave { get; private set; } = 1;
    public int Score { get; private set; }
    public int Lives { get; private set; }
    public Ship Ship { get; } = new();
    public List<Bolt> Bolts { get; } = [];
    public List<Enemy> Enemies { get; } = [];
    public List<Pod> Pods { get; } = [];
    public List<Debris> Wreckage { get; } = [];
    public ShipPilot Pilot { get; } = new();
    public string Cause { get; private set; } = "";
    public int Bonus { get; private set; }
    /// <summary>The rescue chain: each pod picked up in a row is worth one more times the last, up to five.</summary>
    public int Chain { get; private set; }
    public int Rescued { get; private set; }
    /// <summary>Enemies still to come in this wave.</summary>
    public int ToCome => _queue.Count;
    /// <summary>For tests: the enemy holds still and holds its fire, and no more come in.</summary>
    internal bool Calm { get; set; }
    /// <summary>For the probe: how the survivors were lost.</summary>
    internal int LostToSun, LostToShots, LostToAge;

    private int _nextExtra;
    private double _enemyIn, _thrustSound;
    private readonly Queue<EnemyKind> _queue = new();

    public int EnemiesPerWave => Math.Min(14, 4 + Wave);
    public int MaxEnemies => Math.Min(8, 3 + (Wave + 1) / 2);
    public double EnemySpeed => Math.Min(240, 140 + 12 * (Wave - 1));
    public double EnemyFireGap => Math.Max(0.7, 1.8 - 0.12 * (Wave - 1));
    public double EnemyShotSpeed => Math.Min(460, 340 + 15 * (Wave - 1));

    private void NewGame()
    {
        _rng = new Random(_seed);
        Score = 0;
        Lives = 3;
        Wave = 1;
        _nextExtra = ExtraLifeEvery;
        Wreckage.Clear();
        Pods.Clear();
        Chain = 0;
        PlaceShip();
        SetPhase(Phase.Start);
    }

    /// <summary>The ship in orbit below the sun, going round it, safe for a moment.</summary>
    private void PlaceShip()
    {
        var s = Ship;
        s.X = Field.SunX;
        s.Y = Field.SunY + StartOrbit;
        // Field y grows downwards: moving right from below the sun goes round it anticlockwise on the screen.
        s.Vx = Field.OrbitSpeed(StartOrbit);
        s.Vy = 0;
        s.Angle = 0;
        s.Alive = true;
        s.Thrusting = false;
        s.Safe = SafeSeconds;
        s.FireIn = s.HyperIn = 0;
    }

    /// <summary>
    /// A wave: its enemy queued to come in from the edges a few at a time; fighters always, orbiters from the
    /// second, cruisers from the third, hunters from the fourth.
    /// </summary>
    private void BeginWave(int wave)
    {
        Wave = wave;
        Enemies.Clear();
        Bolts.Clear();
        _queue.Clear();
        Rescued = 0;
        var kinds = new List<EnemyKind>();
        int n = EnemiesPerWave;
        if (wave >= 2) kinds.AddRange(Enumerable.Repeat(EnemyKind.Orbiter, Math.Min(3, 1 + wave / 3)));
        if (wave >= 3) kinds.AddRange(Enumerable.Repeat(EnemyKind.Cruiser, Math.Min(3, wave / 3)));
        if (wave >= 4) kinds.AddRange(Enumerable.Repeat(EnemyKind.Hunter, Math.Min(3, (wave - 2) / 2)));
        while (kinds.Count < n) kinds.Add(EnemyKind.Fighter);
        foreach (var k in kinds.Take(n).OrderBy(_ => _rng.Next())) _queue.Enqueue(k);
        _enemyIn = 0.5;
        SetPhase(Phase.Start);
        Say($"WAVE {wave}", Pods.Count > 0 ? "PICK UP THE SURVIVORS" : null, StartSeconds + 0.3);
        Play(Sfx.WaveStart);
        Music(true);
    }

    private void Simulate(double dt, Controls c)
    {
        MoveDebris(dt);
        switch (Phase)
        {
            case Phase.Start:
                Fly(dt, c);
                MovePods(dt);
                Pickups();
                if (PhaseTime >= StartSeconds) SetPhase(Phase.Play);
                break;
            case Phase.Play:
                Fly(dt, c);
                Fire(c);
                Reinforce(dt);
                MoveEnemies(dt);
                MoveBolts(dt);
                MovePods(dt);
                Hits();
                Pickups();
                if (Phase == Phase.Play && Enemies.Count == 0 && _queue.Count == 0) Clear();
                break;
            case Phase.Dying:
                MoveEnemies(dt);
                MoveBolts(dt);
                MovePods(dt);
                if (PhaseTime >= DyingSeconds)
                {
                    if (Lives <= 0) { GameOver(); return; }
                    Respawn();
                    SetPhase(Phase.Play);
                }
                break;
            case Phase.Clear:
                Fly(dt, c);
                Fire(c);
                MoveBolts(dt);
                MovePods(dt);
                Pickups();
                if (PhaseTime >= 1.4 && PhaseTime - dt < 1.4 && Bonus > 0)
                {
                    Score += Bonus;
                    CheckExtra();
                }
                if (PhaseTime >= ClearSeconds) BeginWave(Wave + 1);
                break;
        }
        PhaseTime += dt;
    }

    // ───────────────────────────── the ship ─────────────────────────────

    /// <summary>One step of flying: turning, thrust, a little drag, the sun's pull, and the wrap at the edges.</summary>
    public static void Move(Ship s, Controls c, double dt)
    {
        s.Angle = Math.IEEERemainder(s.Angle + c.Turn * TurnRate * dt, Math.PI * 2);
        s.Thrusting = c.Thrust;
        if (c.Thrust)
        {
            s.Vx += Math.Cos(s.Angle) * ThrustPower * dt;
            s.Vy += Math.Sin(s.Angle) * ThrustPower * dt;
        }
        var (gx, gy) = Field.Gravity(s.X, s.Y);
        s.Vx += gx * dt;
        s.Vy += gy * dt;
        s.Vx *= 1 - Drag * dt;
        s.Vy *= 1 - Drag * dt;
        double v = Math.Sqrt(s.Vx * s.Vx + s.Vy * s.Vy);
        if (v > MaxSpeed) { s.Vx *= MaxSpeed / v; s.Vy *= MaxSpeed / v; }
        s.X = Field.WrapX(s.X + s.Vx * dt);
        s.Y = Field.WrapY(s.Y + s.Vy * dt);
    }

    private void Fly(double dt, Controls c)
    {
        var s = Ship;
        if (!s.Alive) return;
        s.Safe = Math.Max(0, s.Safe - dt);
        s.FireIn -= dt;
        s.HyperIn -= dt;
        s.Flash = Math.Max(0, s.Flash - dt);
        Move(s, c, dt);
        if (c.Thrust && (_thrustSound -= dt) <= 0) { _thrustSound = 0.25; Play(Sfx.Thrust); }
        if (c.Hyper && s.HyperIn <= 0) Hyperspace();
        if (Field.FromSun(s.X, s.Y) < Field.SunRadius + Ship.Radius * 0.5 && Phase == Phase.Play) { Burn(); return; }
    }

    /// <summary>
    /// Gone, and back somewhere else at random: never into the sun, but maybe somewhere no better. The ship
    /// comes out still, with a flash, safe for a split second.
    /// </summary>
    private void Hyperspace()
    {
        var s = Ship;
        s.HyperIn = HyperGap;
        Sparks(s.X, s.Y, 0xFF8AF0FF, 8, 0.5);
        for (int i = 0; i < 20; i++)
        {
            double x = _rng.NextDouble() * Field.Width, y = _rng.NextDouble() * Field.Height;
            if (Field.FromSun(x, y) < 180) continue;
            s.X = x;
            s.Y = y;
            break;
        }
        s.Vx = s.Vy = 0;
        s.Flash = 0.4;
        s.Safe = Math.Max(s.Safe, 0.3);
        Play(Sfx.Hyperspace);
    }

    /// <summary>Bolts from the nose, bent a little by the sun as they fly.</summary>
    private void Fire(Controls c)
    {
        var s = Ship;
        if (!c.Fire || s.FireIn > 0 || !s.Alive || Bolts.Count(b => !b.Hostile) >= MaxBolts) return;
        s.FireIn = FireGap;
        double fx = Math.Cos(s.Angle), fy = Math.Sin(s.Angle);
        Bolts.Add(new Bolt { X = Field.WrapX(s.X + fx * 16), Y = Field.WrapY(s.Y + fy * 16), Vx = fx * BoltSpeed + s.Vx * 0.5, Vy = fy * BoltSpeed + s.Vy * 0.5, Life = BoltLife });
        Play(Sfx.Fire);
    }

    /// <summary>Back where it started, in orbit below the sun.</summary>
    private void Respawn()
    {
        PlaceShip();
        Bolts.RemoveAll(b => b.Hostile);
        Chain = 0;
    }

    // ───────────────────────────── the enemy ─────────────────────────────

    /// <summary>The wave's enemy comes in from the edges, a few at a time.</summary>
    private void Reinforce(double dt)
    {
        if (Calm || _queue.Count == 0 || Enemies.Count >= MaxEnemies || (_enemyIn -= dt) > 0) return;
        _enemyIn = Math.Max(0.8, 1.6 - 0.06 * Wave);
        var kind = _queue.Dequeue();
        double x, y;
        do
        {
            if (_rng.Next(2) == 0) { x = _rng.NextDouble() * Field.Width; y = _rng.Next(2) == 0 ? 2 : Field.Height - 2; }
            else { x = _rng.Next(2) == 0 ? 2 : Field.Width - 2; y = _rng.NextDouble() * Field.Height; }
        } while (Field.Distance(x, y, Ship.X, Ship.Y) < 260);
        var e = new Enemy
        {
            Kind = kind, X = x, Y = y, Angle = Math.Atan2(Field.Dy(y, Field.SunY), Field.Dx(x, Field.SunX)),
            FireIn = EnemyFireGap * (0.6 + _rng.NextDouble() * 0.6), Hits = kind == EnemyKind.Cruiser ? 3 : 1,
            Orbit = 170 + _rng.NextDouble() * 140, Way = _rng.Next(2) == 0 ? 1 : -1,
        };
        Enemies.Add(e);
    }

    private void MoveEnemies(double dt)
    {
        var s = Ship;
        foreach (var e in Enemies)
        {
            e.T += dt;
            e.Hit = Math.Max(0, e.Hit - dt);
            if (Calm) continue;
            // Where it wants to go, as a velocity.
            double wx, wy, speed = EnemySpeed * (e.Kind switch { EnemyKind.Hunter => 1.25, EnemyKind.Cruiser => 0.55, EnemyKind.Orbiter => 0.9, _ => 1 });
            double dx = Field.Dx(e.X, s.X), dy = Field.Dy(e.Y, s.Y), d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
            switch (e.Kind)
            {
                case EnemyKind.Orbiter:
                {
                    // Round the sun at its distance: along the circle, and in or out to keep on it.
                    double sx = Field.Dx(Field.SunX, e.X), sy = Field.Dy(Field.SunY, e.Y), r = Math.Max(1, Math.Sqrt(sx * sx + sy * sy));
                    double tx = -sy / r * e.Way, ty = sx / r * e.Way, err = (e.Orbit - r) / 60;
                    wx = tx + sx / r * err;
                    wy = ty + sy / r * err;
                    break;
                }
                case EnemyKind.Hunter:
                    // Straight at the ship, a little ahead of it.
                    wx = dx + s.Vx * 0.4;
                    wy = dy + s.Vy * 0.4;
                    break;
                default:
                {
                    // Close to a firing distance, and circle there; the cruiser keeps further off.
                    double keep = e.Kind == EnemyKind.Cruiser ? 330 : 270, side = Math.Sin(e.T * 0.7 + e.Orbit) > 0 ? 1 : -1;
                    double radial = Math.Clamp((d - keep) / 120, -1, 1);
                    wx = dx / d * radial - dy / d * side * 0.7;
                    wy = dy / d * radial + dx / d * side * 0.7;
                    break;
                }
            }
            // Away from the sun when close to it.
            double hx = Field.Dx(Field.SunX, e.X), hy = Field.Dy(Field.SunY, e.Y), hd = Math.Max(1, Math.Sqrt(hx * hx + hy * hy));
            if (hd < 140)
            {
                double push = (140 - hd) / 40, len = Math.Max(1e-6, Math.Sqrt(wx * wx + wy * wy));
                wx = wx / len + hx / hd * push;
                wy = wy / len + hy / hd * push;
            }
            double wl = Math.Max(1e-6, Math.Sqrt(wx * wx + wy * wy));
            e.Vx += (wx / wl * speed - e.Vx) * 1.6 * dt;
            e.Vy += (wy / wl * speed - e.Vy) * 1.6 * dt;
            e.X = Field.WrapX(e.X + e.Vx * dt);
            e.Y = Field.WrapY(e.Y + e.Vy * dt);
            // It faces the ship to fire (the hunter faces where it's going).
            double face = e.Kind == EnemyKind.Hunter ? Math.Atan2(e.Vy, e.Vx) : Math.Atan2(dy, dx);
            e.Angle += Math.Clamp(Math.IEEERemainder(face - e.Angle, Math.PI * 2), -3 * dt, 3 * dt);
            if (Field.FromSun(e.X, e.Y) < Field.SunRadius + e.Radius * 0.5)
            {
                // Into the sun: gone, for nothing.
                e.Dead = true;
                Flare(e.X, e.Y);
                continue;
            }
            Shoot(e, dt, d);
        }
        Enemies.RemoveAll(e => e.Dead);
    }

    private void Shoot(Enemy e, double dt, double d)
    {
        var s = Ship;
        e.FireIn -= dt;
        if (e.Kind == EnemyKind.Hunter || e.FireIn > 0 || !s.Alive || s.Safe > 0 || Phase != Phase.Play || d > 420) return;
        double a = Math.Atan2(Field.Dy(e.Y, s.Y), Field.Dx(e.X, s.X));
        if (Math.Abs(Math.IEEERemainder(a - e.Angle, Math.PI * 2)) > 0.4) return;
        e.FireIn = EnemyFireGap * (e.Kind == EnemyKind.Cruiser ? 1.3 : 1);
        double spread = Math.Max(0.02, 0.12 - 0.015 * Wave);
        foreach (double off in e.Kind == EnemyKind.Cruiser ? new[] { -0.22, 0, 0.22 } : new[] { 0.0 })
        {
            double aa = a + off + (_rng.NextDouble() - 0.5) * spread;
            Bolts.Add(new Bolt { X = e.X, Y = e.Y, Vx = Math.Cos(aa) * EnemyShotSpeed, Vy = Math.Sin(aa) * EnemyShotSpeed, Life = 1.6, Hostile = true });
        }
        Play(Sfx.EnemyFire);
    }

    private void MoveBolts(double dt)
    {
        foreach (var b in Bolts)
        {
            b.Life -= dt;
            var (gx, gy) = Field.Gravity(b.X, b.Y, BoltPull);
            b.Vx += gx * dt;
            b.Vy += gy * dt;
            b.X = Field.WrapX(b.X + b.Vx * dt);
            b.Y = Field.WrapY(b.Y + b.Vy * dt);
            if (Field.FromSun(b.X, b.Y) < Field.SunRadius) b.Life = 0;
        }
        Bolts.RemoveAll(b => b.Life <= 0);
    }

    // ───────────────────────────── the survivors ─────────────────────────────

    /// <summary>The pods drift under the sun's pull, slowly losing their way round it, until they fall in.</summary>
    private void MovePods(double dt)
    {
        foreach (var p in Pods.ToList())
        {
            p.T += dt;
            var (gx, gy) = Field.Gravity(p.X, p.Y);
            p.Vx += gx * dt;
            p.Vy += gy * dt;
            p.Vx *= 1 - PodDrag * dt;
            p.Vy *= 1 - PodDrag * dt;
            p.X = Field.WrapX(p.X + p.Vx * dt);
            p.Y = Field.WrapY(p.Y + p.Vy * dt);
            if (Field.FromSun(p.X, p.Y) < Field.SunRadius || p.T > PodLife)
            {
                if (p.T > PodLife) LostToAge++; else LostToSun++;
                Pods.Remove(p);
                Flare(p.X, p.Y);
                Lose();
            }
        }
    }

    /// <summary>A survivor lost: the chain is broken.</summary>
    private void Lose()
    {
        Chain = 0;
        Play(Sfx.Lost);
    }

    /// <summary>The ship flying through a pod picks the survivor up: worth more each one in a row.</summary>
    private void Pickups()
    {
        var s = Ship;
        if (!s.Alive) return;
        foreach (var p in Pods.Where(p => Field.Distance(p.X, p.Y, s.X, s.Y) < Pod.Radius + Ship.Radius).ToList())
        {
            Pods.Remove(p);
            Chain = Math.Min(MaxChain, Chain + 1);
            Rescued++;
            Award(RescuePoints * Chain, p.X, p.Y);
            if (Chain > 1) Effects.Add(new Effect { Kind = EffectKind.Word, X = p.X, Y = p.Y - 30, Text = $"x{Chain}", Life = 0.9, Colour = 0xFF8AFFB0 });
            Play(Sfx.Pickup);
        }
    }

    // ───────────────────────────── hits ─────────────────────────────

    private void Hits()
    {
        foreach (var b in Bolts.Where(b => !b.Hostile && b.Life > 0).ToList())
        {
            var e = Enemies.FirstOrDefault(e => !e.Dead && Field.Distance(b.X, b.Y, e.X, e.Y) < e.Radius + 3);
            if (e != null)
            {
                b.Life = 0;
                e.Hit = 0.15;
                if (--e.Hits > 0) { Sparks(b.X, b.Y, Colour(e.Kind), 4, 0.4); Play(Sfx.Clang); continue; }
                Destroy(e);
                continue;
            }
            // Shooting a survivor (once he's clear of the wreck): he's gone, and so is the chain.
            var p = Pods.FirstOrDefault(p => p.T > PodGrace && Field.Distance(b.X, b.Y, p.X, p.Y) < Pod.Radius + 2);
            if (p != null)
            {
                b.Life = 0;
                Pods.Remove(p);
                LostToShots++;
                Sparks(p.X, p.Y, 0xFFFFFFFF, 6, 0.5);
                Lose();
            }
        }
        Enemies.RemoveAll(e => e.Dead);
        Bolts.RemoveAll(b => b.Life <= 0);
        var s = Ship;
        if (!s.Alive || s.Safe > 0) return;
        foreach (var b in Bolts.Where(b => b.Hostile))
            if (Field.Distance(b.X, b.Y, s.X, s.Y) < Ship.Radius)
            {
                b.Life = 0;
                Kill("shot");
                return;
            }
        foreach (var e in Enemies)
            if (Field.Distance(e.X, e.Y, s.X, s.Y) < e.Radius + Ship.Radius - 3)
            {
                Destroy(e);
                Kill("rammed");
                return;
            }
    }

    /// <summary>An enemy shot down: its crew get out in pods, flung clear of the wreck.</summary>
    private void Destroy(Enemy e)
    {
        e.Dead = true;
        Award(e.Points, e.X, e.Y);
        Shatter(e.X, e.Y, Colour(e.Kind), 12, 1.1);
        Effects.Add(new Effect { Kind = EffectKind.Burst, X = e.X, Y = e.Y, Life = 0.5, Size = e.Radius / 12, Colour = Colour(e.Kind) });
        Play(Sfx.Blast);
        for (int i = 0; i < e.Crew; i++)
        {
            // Into an orbit round the sun (the way the wreck was going round), that slowly decays.
            double ox = Field.Dx(Field.SunX, e.X), oy = Field.Dy(Field.SunY, e.Y), r = Math.Max(60, Math.Sqrt(ox * ox + oy * oy));
            double side = Math.Sign(ox * e.Vy - oy * e.Vx);
            if (side == 0) side = 1;
            double v = Field.OrbitSpeed(r) * (0.85 + _rng.NextDouble() * 0.2), a = _rng.NextDouble() * Math.PI * 2;
            Pods.Add(new Pod { X = e.X, Y = e.Y, Vx = -oy / r * side * v + Math.Cos(a) * 20, Vy = ox / r * side * v + Math.Sin(a) * 20 });
        }
    }

    public static uint Colour(EnemyKind kind) => kind switch
    {
        EnemyKind.Fighter => 0xFF6AE8FF, EnemyKind.Orbiter => 0xFFFFE04A, EnemyKind.Cruiser => 0xFFFF6AD8, _ => 0xFFFF5A3A,
    };

    public const uint ShipColour = 0xFF8AFF8A, PodColour = 0xFFF0F0FF;

    private void Burn()
    {
        Flare(Ship.X, Ship.Y);
        Kill("the sun");
        Play(Sfx.Burned);
    }

    private void Kill(string cause)
    {
        var s = Ship;
        Cause = cause;
        s.Alive = false;
        s.Thrusting = false;
        Shatter(s.X, s.Y, ShipColour, 16, 1.6);
        Effects.Add(new Effect { Kind = EffectKind.Burst, X = s.X, Y = s.Y, Life = 0.7, Size = 2, Colour = 0xFFFFFFFF });
        Lives--;
        Chain = 0;
        Play(Sfx.Death);
        SetPhase(Phase.Dying);
        if (Lives <= 0) Say("GAME OVER", null, DyingSeconds);
    }

    private void Flare(double x, double y) => Effects.Add(new Effect { Kind = EffectKind.Flare, X = x, Y = y, Life = 0.6, Colour = 0xFFFFC04A });

    private void Clear()
    {
        Bonus = Rescued * 100 * Wave;
        Bolts.RemoveAll(b => b.Hostile);
        Play(Sfx.WaveClear);
        Say($"WAVE {Wave} CLEARED", Rescued > 0 ? $"{Rescued} RESCUED - BONUS {Bonus}" : "NOBODY RESCUED", ClearSeconds);
        SetPhase(Phase.Clear);
    }

    private void Award(int points, double x, double y)
    {
        Score += points;
        Effects.Add(new Effect { Kind = EffectKind.Score, X = x, Y = y - 14, Text = points.ToString(), Life = 0.7, Colour = 0xFFFFE14A });
        CheckExtra();
    }

    private void CheckExtra()
    {
        while (Score >= _nextExtra)
        {
            _nextExtra += ExtraLifeEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    private void Shatter(double x, double y, uint colour, int n, double life)
    {
        for (int i = 0; i < n; i++)
        {
            double a = _rng.NextDouble() * Math.PI * 2, v = 40 + _rng.NextDouble() * 170;
            Wreckage.Add(new Debris
            {
                X = x, Y = y, Vx = Math.Cos(a) * v, Vy = Math.Sin(a) * v, Angle = _rng.NextDouble() * 6, Spin = (_rng.NextDouble() - 0.5) * 12,
                Length = 6 + _rng.NextDouble() * 12, Life = life * (0.5 + _rng.NextDouble() * 0.5), Colour = colour,
            });
        }
    }

    private void Sparks(double x, double y, uint colour, int n, double life) => Shatter(x, y, colour, n, life * 0.6);

    private void MoveDebris(double dt)
    {
        foreach (var d in Wreckage)
        {
            d.Age += dt;
            d.X += d.Vx * dt;
            d.Y += d.Vy * dt;
            d.Angle += d.Spin * dt;
        }
        Wreckage.RemoveAll(d => d.Age >= d.Life);
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Straight into a wave, past its number.</summary>
    internal void Go(int wave = 1)
    {
        StartGame();
        if (wave != 1) BeginWave(wave);
        SetPhase(Phase.Play);
        Ship.Safe = 0;
        Message = SubMessage = null;
    }

    internal Enemy PutEnemy(EnemyKind kind, double x, double y)
    {
        var e = new Enemy { Kind = kind, X = x, Y = y, Hits = kind == EnemyKind.Cruiser ? 3 : 1, FireIn = 0, Orbit = Field.FromSun(x, y) };
        Enemies.Add(e);
        return e;
    }

    internal Pod PutPod(double x, double y, double vx = 0, double vy = 0)
    {
        var p = new Pod { X = x, Y = y, Vx = vx, Vy = vy };
        Pods.Add(p);
        return p;
    }

    /// <summary>The ship placed somewhere, still.</summary>
    internal void PutShip(double x, double y, double angle = -Math.PI / 2)
    {
        var s = Ship;
        s.X = x;
        s.Y = y;
        s.Vx = s.Vy = 0;
        s.Angle = angle;
        s.Alive = true;
        s.Safe = 0;
    }

    internal void NoMoreEnemies() => _queue.Clear();
    internal void SetLives(int n) => Lives = n;
    internal void SetScore(int n) => Score = n;
}
