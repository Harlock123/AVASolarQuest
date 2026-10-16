using AVASolarQuest.Engine;

namespace AVASolarQuest.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avasolarquest-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A wave in play with nobody about and none to come but one far-off, still enemy, so it isn't over; calm unless asked.</summary>
    private static GameEngine Game(int wave = 1, bool calm = true, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = calm };
        g.Go(wave);
        g.NoMoreEnemies();
        g.Enemies.Clear();
        var far = g.PutEnemy(EnemyKind.Fighter, 40, 60);
        far.FireIn = 999;
        return g;
    }

    private static readonly Controls FireButton = new(Fire: true);

    /// <summary>A place well away from the sun, where its pull is small.</summary>
    private const double FarX = 120, FarY = 640;

    // ───────────────────────────── the sun ─────────────────────────────

    [Fact]
    public void The_sun_pulls_towards_itself_harder_the_closer_and_space_wraps()
    {
        var (ax, ay) = Field.Gravity(Field.SunX + 200, Field.SunY);
        Assert.True(ax < 0);
        Assert.Equal(0, ay, 6);
        var (nx, _) = Field.Gravity(Field.SunX + 100, Field.SunY);
        Assert.Equal(4, nx / ax, 3);
        Assert.Equal(-20, Field.Dx(10, 1014));
        Assert.Equal(5, Field.WrapX(1029));
        // A circular orbit's speed holds the ship at its distance: once round and it's still about as far out.
        var g = Game();
        g.Ship.X = Field.SunX;
        g.Ship.Y = Field.SunY + 260;
        g.Ship.Vx = Field.OrbitSpeed(260) / (1 - GameEngine.Drag * 0);
        g.Ship.Vy = 0;
        g.Idle(120);
        Assert.InRange(Field.FromSun(g.Ship.X, g.Ship.Y), 200, 280);
    }

    [Fact]
    public void A_ship_left_alone_falls_into_the_sun_and_burns()
    {
        var g = Game();
        g.PutShip(Field.SunX + 150, Field.SunY);
        int t = 0;
        while (g.Phase == Phase.Play && t++ < 600) g.Idle(1);
        Assert.Equal("the sun", g.Cause);
        Assert.Equal(2, g.Lives);
    }

    [Fact]
    public void The_new_ship_starts_in_orbit_below_the_sun()
    {
        var g = Game();
        Assert.Equal(GameEngine.StartOrbit, Field.FromSun(g.Ship.X, g.Ship.Y), 1);
        Assert.Equal(Field.OrbitSpeed(GameEngine.StartOrbit), g.Ship.Vx, 1);
    }

    // ───────────────────────────── the ship ─────────────────────────────

    [Fact]
    public void Turning_and_thrust()
    {
        var g = Game();
        g.PutShip(FarX, FarY, 0);
        g.Run(30, new Controls(Turn: 1));
        Assert.Equal(GameEngine.TurnRate * 0.5, g.Ship.Angle, 3);
        g.PutShip(FarX, FarY, 0);
        g.Run(30, new Controls(Thrust: true));
        Assert.InRange(g.Ship.Vx, GameEngine.ThrustPower * 0.4, GameEngine.ThrustPower * 0.55);
    }

    [Fact]
    public void Bolts_fly_from_the_nose_four_at_most_and_bend_towards_the_sun()
    {
        var g = Game();
        g.PutShip(FarX, FarY, -Math.PI / 2);
        g.Run(1, FireButton);
        var b = Assert.Single(g.Bolts);
        Assert.True(b.Vy < -GameEngine.BoltSpeed * 0.9);
        int most = 0;
        for (int i = 0; i < 40; i++) { g.Run(1, FireButton); most = Math.Max(most, g.Bolts.Count); }
        Assert.Equal(GameEngine.MaxBolts, most);
        // One fired past the sun curves in towards it.
        var h = Game();
        h.PutShip(Field.SunX - 300, Field.SunY - 80, 0);
        h.Run(1, FireButton);
        double vy = h.Bolts[0].Vy;
        h.Idle(20);
        Assert.True(h.Bolts[0].Vy > vy + 5);
    }

    [Fact]
    public void Hyperspace_puts_the_ship_somewhere_else_still_and_never_in_the_sun()
    {
        var g = Game();
        for (int i = 0; i < 20; i++)
        {
            g.PutShip(FarX, FarY);
            g.Ship.Vx = 100;
            g.Ship.HyperIn = 0;
            g.Run(1, new Controls(Hyper: true));
            Assert.True(Field.FromSun(g.Ship.X, g.Ship.Y) >= 180);
            Assert.Equal((0, 0), (Math.Round(g.Ship.Vx), Math.Round(g.Ship.Vy)));
        }
        // Not again straight away.
        double x = g.Ship.X;
        g.Run(1, new Controls(Hyper: true));
        Assert.Equal(x, g.Ship.X, 0);
    }

    // ───────────────────────────── the enemy ─────────────────────────────

    [Theory]
    [InlineData(EnemyKind.Fighter, 1, 200, 1)]
    [InlineData(EnemyKind.Orbiter, 1, 300, 1)]
    [InlineData(EnemyKind.Hunter, 1, 400, 1)]
    [InlineData(EnemyKind.Cruiser, 3, 500, 2)]
    public void Each_kind_takes_its_hits_is_worth_its_points_and_leaves_its_crew_in_pods(EnemyKind kind, int hits, int points, int crew)
    {
        var g = Game();
        g.PutShip(FarX, FarY, 0);
        var e = g.PutEnemy(kind, FarX + 200, FarY);
        for (int i = 0; i < hits; i++) { g.Run(1, FireButton); g.Idle(25); }
        Assert.True(e.Dead);
        Assert.Equal(points, g.Score);
        Assert.Equal(crew, g.Pods.Count);
    }

    [Fact]
    public void Enemies_fire_when_they_face_the_ship_and_a_bolt_costs_a_life()
    {
        var g = Game(calm: false);
        g.SetLives(3);
        g.PutShip(FarX, FarY);
        var e = g.PutEnemy(EnemyKind.Fighter, FarX + 250, FarY);
        e.Angle = Math.PI;
        int t = 0;
        while (g.Phase == Phase.Play && t++ < 600) g.Idle(1);
        Assert.Equal("shot", g.Cause);
        Assert.Equal(2, g.Lives);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 2);
        Assert.True(g.Ship.Alive && g.Ship.Safe > 0);
    }

    [Fact]
    public void An_orbiter_circles_the_sun_and_a_hunter_rams()
    {
        var g = Game(calm: false);
        g.PutShip(FarX, FarY);
        g.Ship.Safe = 1000;
        var o = g.PutEnemy(EnemyKind.Orbiter, Field.SunX + 220, Field.SunY);
        o.FireIn = 999;
        g.Idle(300);
        Assert.InRange(Field.FromSun(o.X, o.Y), 160, 290);
        var h = Game(calm: false);
        h.PutShip(FarX, FarY);
        h.PutEnemy(EnemyKind.Hunter, FarX + 260, FarY);
        int t = 0;
        while (h.Phase == Phase.Play && t++ < 600) { h.Ship.Vx = h.Ship.Vy = 0; h.Idle(1); }
        Assert.Equal("rammed", h.Cause);
    }

    [Fact]
    public void Each_wave_brings_more_enemies_of_more_kinds()
    {
        var one = Game(1);
        var six = Game(6);
        Assert.True(six.EnemiesPerWave > one.EnemiesPerWave);
        Assert.True(six.MaxEnemies > one.MaxEnemies);
        Assert.True(six.EnemyFireGap < one.EnemyFireGap);
        var g = new GameEngine(3, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(5);
        g.Ship.Safe = 1000;
        var seen = new HashSet<EnemyKind>();
        for (int i = 0; i < 60 * 40 && g.Phase == Phase.Play; i++)
        {
            g.Ship.X = FarX;
            g.Ship.Y = FarY;
            g.Ship.Vx = g.Ship.Vy = 0;
            g.Idle(1);
            foreach (var e in g.Enemies) seen.Add(e.Kind);
            // Out of the way as they come, so the rest can.
            g.Enemies.Clear();
        }
        Assert.Equal(4, seen.Count);
    }

    // ───────────────────────────── the survivors ─────────────────────────────

    [Fact]
    public void Survivors_drift_round_the_sun_and_fall_in_unless_picked_up()
    {
        var g = Game();
        g.PutShip(FarX, FarY);
        var p = g.PutPod(Field.SunX + 200, Field.SunY, 0, -Field.OrbitSpeed(200));
        g.Idle(60);
        Assert.InRange(Field.FromSun(p.X, p.Y), 120, 260);
        int t = 0;
        while (g.Pods.Contains(p) && t++ < 60 * 60) { g.Ship.X = FarX; g.Ship.Y = FarY; g.Idle(1); }
        Assert.DoesNotContain(p, g.Pods);
        Assert.True(g.LostToSun + g.LostToAge == 1);
    }

    [Fact]
    public void Picking_survivors_up_in_a_row_is_worth_more_each_time_and_losing_one_breaks_the_chain()
    {
        var g = Game();
        g.PutShip(FarX, FarY);
        int[] expect = [500, 1000, 1500, 2000, 2500, 2500];
        foreach (int points in expect)
        {
            int before = g.Score;
            g.PutPod(FarX + 2, FarY);
            g.Idle(1);
            Assert.Equal(points, g.Score - before);
        }
        Assert.Equal(GameEngine.MaxChain, g.Chain);
        // Shooting one (once it's clear of its wreck) breaks it.
        g.PutPod(FarX + 120, FarY).T = 5;
        g.PutShip(FarX, FarY, 0);
        g.Run(1, FireButton);
        g.Idle(20);
        Assert.Equal(0, g.Chain);
    }

    [Fact]
    public void A_wave_cleared_pays_for_the_survivors_rescued_and_they_carry_on_drifting_into_the_next()
    {
        var g = Game(2);
        g.PutShip(FarX, FarY, 0);
        g.PutPod(FarX + 2, FarY);
        g.Idle(1);
        g.Enemies.Clear();
        var e = g.PutEnemy(EnemyKind.Fighter, FarX + 200, FarY);
        g.Run(1, FireButton);
        g.Idle(25);
        Assert.Equal(Phase.Clear, g.Phase);
        Assert.Equal(1 * 100 * 2, g.Bonus);
        Assert.Single(g.Pods);
        int score = g.Score;
        g.Idle((int)(GameEngine.ClearSeconds * 60) + 2);
        Assert.Equal(3, g.Wave);
        Assert.True(g.Score >= score + 200);
    }

    // ───────────────────────────── the whole game ─────────────────────────────

    [Fact]
    public void An_extra_ship_every_10000()
    {
        var g = Game();
        int lives = g.Lives;
        g.SetScore(GameEngine.ExtraLifeEvery - 50);
        g.PutShip(FarX, FarY);
        g.PutPod(FarX + 2, FarY);
        g.Idle(1);
        Assert.Equal(lives + 1, g.Lives);
    }

    [Fact]
    public void The_last_ship_ends_the_game_and_a_high_score_asks_for_initials()
    {
        var g = Game();
        g.SetLives(1);
        g.SetScore(99999);
        g.PutShip(Field.SunX + 20, Field.SunY);
        g.Idle(2);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 2);
        Assert.Equal(GameState.GameOver, g.State);
        g.Idle(60 * 4);
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (char ch in "SOL") g.Initials!.Type(ch);
        g.Initials!.Finish();
        g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal(("SOL", 99999), (g.HighScores.Entries[0].Initials, g.HighScores.Entries[0].Score));
    }

    [Fact]
    public void The_title_runs_a_demonstration_then_the_high_scores()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        var waves = new List<int>();
        for (int round = 0; round < 2; round++)
        {
            g.Idle((int)(GameEngine.TitleIdleSeconds * 60) + 2);
            Assert.Equal(GameState.Attract, g.State);
            waves.Add(g.Wave);
            for (int i = 0; i < (int)(GameEngine.AttractSeconds * 60) + 2 && g.State == GameState.Attract; i++) g.Idle(1);
            Assert.Equal(GameState.HighScores, g.State);
            g.Idle((int)(GameEngine.HighScoreSeconds * 60) + 2);
            Assert.Equal(GameState.Title, g.State);
        }
        Assert.Equal(new[] { 1, 2 }, waves);
    }

    [Fact]
    public void Enter_starts_a_game()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Tick(default, start: true);
        Assert.Equal(GameState.Playing, g.State);
        Assert.Equal((1, 3), (g.Wave, g.Lives));
    }

    [Fact]
    public void The_computer_player_clears_the_first_wave_and_rescues_someone()
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(1);
        g.SetLives(99);
        int rescued = 0;
        for (int i = 0; i < 60 * 90 && g.Wave < 3 && g.State == GameState.Playing; i++)
        {
            int had = g.Rescued;
            g.Tick(g.Pilot.Decide(g));
            if (g.Rescued > had) rescued++;
        }
        Assert.True(g.Wave >= 2, $"still on wave {g.Wave}");
        Assert.True(rescued > 0);
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        string Play()
        {
            var g = new GameEngine(3, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.Go(3);
            for (int i = 0; i < 60 * 30; i++) g.Tick(g.Pilot.Decide(g));
            return $"{g.Score} {g.Wave} {g.Lives} {g.Ship.X:0.00} {g.Ship.Y:0.00} {g.Enemies.Count} {g.Pods.Count} {g.Phase}";
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void Pausing_stops_everything()
    {
        var g = Game(calm: false);
        g.Idle(30);
        double x = g.Ship.X;
        g.Pause();
        g.Idle(120);
        Assert.Equal(x, g.Ship.X);
        g.Resume();
        Assert.Equal(GameState.Playing, g.State);
    }
}
