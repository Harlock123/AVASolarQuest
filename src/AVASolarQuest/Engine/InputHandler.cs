using Avalonia.Input;

namespace AVASolarQuest.Engine;

/// <summary>
/// Keyboard and gamepad boiled down to the controls: turning, thrust, fire and hyperspace.
/// </summary>
public sealed class InputHandler : IDisposable
{
    private readonly List<Key> _held = [];
    private readonly ControllerManager _pad = new(0);

    public void OnKeyDown(Key key)
    {
        _held.Remove(key);
        _held.Add(key);
    }

    public void OnKeyUp(Key key) => _held.Remove(key);
    public bool Has(Key key) => _held.Contains(key);
    private bool Any(params Key[] keys) => keys.Any(_held.Contains);

    public void Update() => _pad.Update();

    public Controls Controls
    {
        get
        {
            int turn = (Any(Key.Right, Key.D, Key.NumPad6) ? 1 : 0) - (Any(Key.Left, Key.A, Key.NumPad4) ? 1 : 0);
            if (turn == 0) turn = _pad.DPadLeft || _pad.LeftStickX < -0.4f ? -1 : _pad.DPadRight || _pad.LeftStickX > 0.4f ? 1 : 0;
            bool thrust = Any(Key.Up, Key.W, Key.NumPad8) || _pad.DPadUp || _pad.LeftStickY < -0.5f || _pad.ButtonB || _pad.LeftTrigger > 0.5f;
            bool fire = Any(Key.Space, Key.Z, Key.LeftCtrl, Key.RightCtrl) || _pad.ButtonA || _pad.RightTrigger > 0.5f;
            bool hyper = Any(Key.Down, Key.S, Key.NumPad2, Key.X, Key.LeftShift, Key.RightShift) || _pad.DPadDown || _pad.ButtonX || _pad.ButtonY;
            return new Controls(turn, thrust, fire, hyper);
        }
    }

    public bool Start => Any(Key.Enter, Key.D1, Key.NumPad1) || _pad.ButtonStart;

    public bool PadUp => _pad.DPadUp || _pad.LeftStickY < -0.5f;
    public bool PadDown => _pad.DPadDown || _pad.LeftStickY > 0.5f;
    public bool PadLeft => _pad.DPadLeft || _pad.LeftStickX < -0.5f;
    public bool PadRight => _pad.DPadRight || _pad.LeftStickX > 0.5f;
    public bool PadAccept => _pad.ButtonA || _pad.ButtonX || _pad.ButtonB;
    public bool PadDone => _pad.ButtonStart;
    public bool PadPause => _pad.ButtonStart;
    public bool PadBack => _pad.ButtonBack;

    public void Dispose() => _pad.Dispose();
}
