using System.Diagnostics;
using System.Runtime.InteropServices;
using Haulix.Core;
using Haulix.Core.Ets2;

namespace Haulix.App;

/// <summary>
/// TruckersMP anti-AFK message (Settings → TruckersMP). While the player is inactive on TruckersMP it opens
/// the chat, types the configured message and sends it, once per interval. Off by default and only after the
/// user accepted the warning: avoiding the server's inactivity kick is against the TruckersMP rules.
/// Keystrokes only reach the game while ETS2 is the foreground window.
/// </summary>
internal sealed class AntiAfk : IDisposable
{
    private readonly HaulixEngine _engine;
    private readonly System.Threading.Timer _timer;
    private DateTime _lastSentUtc = DateTime.MinValue;
    private int _busy;

    public event Action<string>? Sent;

    public AntiAfk(HaulixEngine engine)
    {
        _engine = engine;
        _timer = new System.Threading.Timer(_ => Tick(), null, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(15));
    }

    private void Tick()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            var cfg = _engine.Settings.Load().TruckersMp;
            if (!cfg.AntiAfk || !cfg.RiskAccepted || !TruckersMp.Active()) return;
            var message = string.IsNullOrWhiteSpace(cfg.Message) ? new Haulix.Core.Settings.TruckersMpSettings().Message : cfg.Message.Trim();
            var interval = TimeSpan.FromMinutes(Math.Clamp(cfg.IntervalMinutes, 2, 25));
            var now = DateTime.UtcNow;
            var idleSince = _engine.Notifier.LastActivityUtc;
            // Only while inactive, and at most once per interval (counted from the last input or message).
            if (now - idleSince < interval || now - _lastSentUtc < interval) return;
            if (!GameInForeground()) return;
            SendChat(cfg.ChatKey, message);
            _lastSentUtc = now;
            Sent?.Invoke(message);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Anti-AFK failed: {ex}");
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    private static bool GameInForeground()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;
        GetWindowThreadProcessId(hwnd, out var pid);
        try { return Process.GetProcessById((int)pid).ProcessName.Equals("eurotrucks2", StringComparison.OrdinalIgnoreCase); }
        catch (ArgumentException) { return false; }
    }

    private static void SendChat(string chatKey, string message)
    {
        var text = new string(message.Where(c => !char.IsControl(c)).Take(120).ToArray());
        var key = string.IsNullOrEmpty(chatKey) ? 'Y' : char.ToUpperInvariant(chatKey[0]);
        var vk = (ushort)(VkKeyScan(key) & 0xFF);
        Key(vk);                       // open the chat
        Thread.Sleep(250);
        var inputs = new List<INPUT>();
        foreach (var c in text)
        {
            inputs.Add(Unicode(c, false));
            inputs.Add(Unicode(c, true));
        }
        if (inputs.Count > 0) SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
        Thread.Sleep(120);
        Key(0x0D);                     // Enter: send
    }

    private static void Key(ushort vk)
    {
        var scan = (ushort)MapVirtualKey(vk, 0);
        var down = new INPUT { type = 1, U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = KEYEVENTF_SCANCODE } } };
        var up = new INPUT { type = 1, U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP } } };
        SendInput(1, [down], Marshal.SizeOf<INPUT>());
        Thread.Sleep(40);
        SendInput(1, [up], Marshal.SizeOf<INPUT>());
    }

    private static INPUT Unicode(char c, bool up) => new()
    {
        type = 1,
        U = new InputUnion { ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0) } },
    };

    public void Dispose() => _timer.Dispose();

    private const uint KEYEVENTF_KEYUP = 0x2, KEYEVENTF_UNICODE = 0x4, KEYEVENTF_SCANCODE = 0x8;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion U; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern short VkKeyScan(char ch);
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint mapType);
}
