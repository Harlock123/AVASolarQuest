using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVASolarQuest.Engine;
using AVASolarQuest.Views;
using Controls = AVASolarQuest.Engine.Controls;

namespace AVASolarQuest.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set SOLARQUEST_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avasolarquest-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("SOLARQUEST_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVASolarQuest-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150, bool holdFire = false)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            var move = g.Pilot.Decide(g);
            g.Tick(holdFire ? move with { Fire = false } : move);
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }
    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("ACE", 88400, 11);
        g.HighScores.Insert("AVA", 52600, 8);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 30 && g.State != GameState.HighScores; i++) g.Idle(1);
        Shot(w, c, "high-scores");
    }

    /// <summary>A wave, and the computer at work in it until something worth showing.</summary>
    private static void Fight(int wave, string name, Func<GameEngine, bool> busy, int seed = 3, int seconds = 90)
    {
        var (w, c, g) = Open(seed);
        g.Go(wave);
        g.SetLives(30);
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Play && g.Ship.Alive && g.Ship.Safe <= 0 && busy(g), seconds));
        Shot(w, c, name);
    }

    [AvaloniaFact]
    public void The_fight() => Fight(2, "fight", g => g.Enemies.Count >= 3 && g.Bolts.Count(b => !b.Hostile) >= 2 && g.Bolts.Any(b => b.Hostile));

    [AvaloniaFact]
    public void Survivors() => Fight(3, "survivors", g => g.Pods.Count >= 3 && g.Pods.All(p => p.T > 1) && g.Enemies.Count >= 1, seconds: 120);

    [AvaloniaFact]
    public void A_rescue() => Fight(4, "rescue", g => g.Chain >= 2 && g.Effects.Any(e => e.Kind == EffectKind.Word && e.Age < 0.3), seconds: 200);

    [AvaloniaFact]
    public void A_cruiser() => Fight(6, "cruiser", g => g.Enemies.Any(e => e.Kind == EnemyKind.Cruiser && Field.Distance(e.X, e.Y, g.Ship.X, g.Ship.Y) < 380) && g.Bolts.Count(b => b.Hostile) >= 3, seconds: 160);

    [AvaloniaFact]
    public void Hunters() => Fight(7, "hunters", g => g.Enemies.Count(e => e.Kind == EnemyKind.Hunter) >= 1 && g.Enemies.Count >= 3 && g.Wreckage.Count > 6, seconds: 160);

    [AvaloniaFact]
    public void A_wave_cleared()
    {
        var (w, c, g) = Open(4);
        g.Go(2);
        g.SetLives(20);
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Clear && g.PhaseTime > 0.5, 120));
        Shot(w, c, "cleared");
    }
}
