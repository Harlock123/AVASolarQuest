using AVASolarQuest.Engine;

namespace AVASolarQuest.Tests;

/// <summary>How the computer player gets on, wave by wave (set SOLARQUEST_PROBE to run it, for tuning: "1" for the pilot, "lazy" for one that just spins and fires).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_plays()
    {
        string? mode = Environment.GetEnvironmentVariable("SOLARQUEST_PROBE");
        if (mode is null) return;
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "solarquest-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.StartGame();
            g.SetLives(99);
            var deaths = new Dictionary<string, int>();
            var waves = new List<string>();
            var doing = new Dictionary<string, int>();
            var last = g.Phase;
            int wave = 1, t = 0, rescued = 0, lost = 0, pods = 0;
            double start = 0;
            for (; t < 60 * 900 && g.Wave <= 10 && g.State == GameState.Playing; t++)
            {
                var c = mode == "lazy" ? new Controls(Turn: 1, Fire: true, Thrust: t % 300 < 30) : g.Pilot.Decide(g);
                int before = g.Pods.Count, had = g.Rescued;
                g.Tick(c);
                if (g.Phase == Phase.Play) doing[g.Pilot.Doing] = doing.GetValueOrDefault(g.Pilot.Doing) + 1;
                if (g.Phase == Phase.Dying && last != Phase.Dying) deaths[$"W{g.Wave} {g.Cause}"] = deaths.GetValueOrDefault($"W{g.Wave} {g.Cause}") + 1;
                if (g.Rescued > had) rescued += g.Rescued - had;
                if (g.Pods.Count > before) pods += g.Pods.Count - before;
                if (g.Wave != wave) { waves.Add($"W{wave}:{(t - start) / 60:0}s"); wave = g.Wave; start = t; }
                last = g.Phase;
            }
            lost = pods - rescued - g.Pods.Count;
            output.WriteLine($"seed {seed}: wave {g.Wave} after {t / 60:0}s score {g.Score} | {string.Join(" ", waves)}");
            output.WriteLine("   deaths " + string.Join(", ", deaths.OrderBy(d => d.Key).Select(d => $"{d.Key} x{d.Value}")));
            output.WriteLine($"   survivors: {pods} out, {rescued} rescued, {lost} lost (sun {g.LostToSun}, shot {g.LostToShots}, age {g.LostToAge}); lives {g.Lives}");
            output.WriteLine("   doing " + string.Join(", ", doing.OrderByDescending(d => d.Value).Select(d => $"{d.Key} {d.Value / 60}s")));
        }
    }
}
