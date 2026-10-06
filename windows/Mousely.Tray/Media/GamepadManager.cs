using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace Mousely.Tray.Media
{
    public static class GamepadManager
    {
        // ================= WIN32 SENDINPUT HARDWARE SCANCODES =================
        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)]
            public MOUSEINPUT mi;
            [FieldOffset(0)]
            public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private const uint INPUT_KEYBOARD = 1;
        private const uint INPUT_MOUSE = 0;
        private const uint KEYEVENTF_KEYDOWN = 0x0000;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_SCANCODE = 0x0008;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint MOUSEEVENTF_MOVE = 0x0001;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        // Hardware Scan Codes (Set 1 Scan Codes for DirectX / RawInput compatibility)
        private const ushort SC_ESCAPE = 0x01;
        private const ushort SC_TAB    = 0x0F;
        private const ushort SC_Q      = 0x10;
        private const ushort SC_W      = 0x11;
        private const ushort SC_E      = 0x12;
        private const ushort SC_RETURN = 0x1C;
        private const ushort SC_A      = 0x1E;
        private const ushort SC_S      = 0x1F;
        private const ushort SC_D      = 0x20;
        private const ushort SC_LSHIFT = 0x2A;
        private const ushort SC_C      = 0x2E;
        private const ushort SC_SPACE  = 0x39;
        private const ushort SC_UP     = 0x48;
        private const ushort SC_LEFT   = 0x4B;
        private const ushort SC_RIGHT  = 0x4D;
        private const ushort SC_DOWN   = 0x50;

        // ViGEm Emulation Client & Controller
        private static ViGEmClient? _vigemClient;
        private static IXbox360Controller? _x360Controller;
        private static bool _useViGEm = false;
        private static bool _initialized = false;
        private static readonly object _syncLock = new();

        // Keyboard Fallback State Tracking
        private static bool _kbGas = false;
        private static bool _kbBrake = false;
        private static bool _kbSteerLeft = false;
        private static bool _kbSteerRight = false;
        private static bool _kbA = false;
        private static bool _kbB = false;
        private static bool _kbX = false;
        private static bool _kbY = false;
        private static bool _kbLB = false;
        private static bool _kbRB = false;
        private static bool _kbSelect = false;
        private static bool _kbStart = false;
        private static bool _kbDpadUp = false;
        private static bool _kbDpadDown = false;
        private static bool _kbDpadLeft = false;
        private static bool _kbDpadRight = false;

        // Watchdog & PWM Steering Engine
        private static long _lastFrameTimestamp = 0;
        private static readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private static System.Threading.Timer? _watchdogTimer;

        public static bool IsViGEmActive => _useViGEm;

        public static void Initialize()
        {
            if (_initialized) return;

            lock (_syncLock)
            {
                if (_initialized) return;

                try
                {
                    _vigemClient = new ViGEmClient();
                    _x360Controller = _vigemClient.CreateXbox360Controller();
                    _x360Controller.Connect();
                    _useViGEm = true;
                    Console.WriteLine("[GamepadManager] ViGEmBus driver found! Xbox 360 controller connected.");
                }
                catch (Exception ex)
                {
                    _useViGEm = false;
                    _vigemClient = null;
                    _x360Controller = null;
                    Console.WriteLine($"[GamepadManager] ViGEm driver not installed ({ex.Message}). Operating in DirectX SendInput hardware mode.");
                }

                // Watchdog: auto-release stuck keys if no UDP frame arrives for 200ms
                _watchdogTimer = new System.Threading.Timer(WatchdogCheck, null, 200, 200);

                _initialized = true;
            }
        }

        public static void ProcessGamepadFrame(short stickX, short stickY, byte lt, byte rt, ushort buttonMask)
        {
            if (!_initialized)
            {
                Initialize();
            }

            lock (_syncLock)
            {
                _lastFrameTimestamp = _stopwatch.ElapsedMilliseconds;

                bool btnA = (buttonMask & (1 << 0)) != 0;
                bool btnB = (buttonMask & (1 << 1)) != 0;
                bool btnX = (buttonMask & (1 << 2)) != 0;
                bool btnY = (buttonMask & (1 << 3)) != 0;
                bool btnDpadUp = (buttonMask & (1 << 4)) != 0;
                bool btnDpadDown = (buttonMask & (1 << 5)) != 0;
                bool btnDpadLeft = (buttonMask & (1 << 6)) != 0;
                bool btnDpadRight = (buttonMask & (1 << 7)) != 0;
                bool btnLB = (buttonMask & (1 << 8)) != 0;
                bool btnRB = (buttonMask & (1 << 9)) != 0;
                bool btnSelect = (buttonMask & (1 << 10)) != 0;
                bool btnStart = (buttonMask & (1 << 11)) != 0;
                bool btnL3 = (buttonMask & (1 << 12)) != 0;

                // ================= TIER 1: VIGEM XBOX 360 CONTROLLER =================
                if (_useViGEm && _x360Controller != null)
                {
                    try
                    {
                        _x360Controller.SetAxisValue(Xbox360Axis.LeftThumbX, stickX);
                        _x360Controller.SetAxisValue(Xbox360Axis.LeftThumbY, stickY);
                        _x360Controller.SetSliderValue(Xbox360Slider.LeftTrigger, lt);
                        _x360Controller.SetSliderValue(Xbox360Slider.RightTrigger, rt);

                        _x360Controller.SetButtonState(Xbox360Button.A, btnA);
                        _x360Controller.SetButtonState(Xbox360Button.B, btnB);
                        _x360Controller.SetButtonState(Xbox360Button.X, btnX);
                        _x360Controller.SetButtonState(Xbox360Button.Y, btnY);

                        _x360Controller.SetButtonState(Xbox360Button.Up, btnDpadUp);
                        _x360Controller.SetButtonState(Xbox360Button.Down, btnDpadDown);
                        _x360Controller.SetButtonState(Xbox360Button.Left, btnDpadLeft);
                        _x360Controller.SetButtonState(Xbox360Button.Right, btnDpadRight);

                        _x360Controller.SetButtonState(Xbox360Button.LeftShoulder, btnLB);
                        _x360Controller.SetButtonState(Xbox360Button.RightShoulder, btnRB);
                        _x360Controller.SetButtonState(Xbox360Button.Back, btnSelect);
                        _x360Controller.SetButtonState(Xbox360Button.Start, btnStart);
                        _x360Controller.SetButtonState(Xbox360Button.LeftThumb, btnL3);

                        _x360Controller.SubmitReport();
                        return;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[GamepadManager] ViGEm communication error ({ex.Message}), falling back to SendInput.");
                        _useViGEm = false;
                    }
                }

                // ================= TIER 2: HARDWARE DIRECTINPUT SENDINPUT =================
                // Throttle & Brake: RT (Gas = W) and LT (Brake = S)
                bool wantGas = rt > 35 || stickY > 10000;
                bool wantBrake = lt > 35 || stickY < -10000;
                UpdateKey(ref _kbGas, wantGas, SC_W, false);
                UpdateKey(ref _kbBrake, wantBrake, SC_S, false);

                // Steering: Stick X or Gyro Angle with Analog PWM
                // Deadzone ±3500 (~10%)
                bool wantSteerLeft = false;
                bool wantSteerRight = false;

                if (stickX < -3500)
                {
                    float ratio = Math.Min(1.0f, (-stickX - 3500f) / 26000f);
                    if (ratio >= 0.85f)
                    {
                        wantSteerLeft = true;
                    }
                    else
                    {
                        // PWM cycle (80ms total window)
                        long cycleMs = _lastFrameTimestamp % 80;
                        float onTimeMs = ratio * 80f;
                        wantSteerLeft = cycleMs < onTimeMs;
                    }
                }
                else if (stickX > 3500)
                {
                    float ratio = Math.Min(1.0f, (stickX - 3500f) / 26000f);
                    if (ratio >= 0.85f)
                    {
                        wantSteerRight = true;
                    }
                    else
                    {
                        long cycleMs = _lastFrameTimestamp % 80;
                        float onTimeMs = ratio * 80f;
                        wantSteerRight = cycleMs < onTimeMs;
                    }
                }

                UpdateKey(ref _kbSteerLeft, wantSteerLeft, SC_A, false);
                UpdateKey(ref _kbSteerRight, wantSteerRight, SC_D, false);

                // ABXY Buttons:
                // A = Space (Handbrake / Nitro)
                // B = Escape (Menu / Back)
                // X = Left Shift (Boost / Clutch)
                // Y = C (Camera View)
                UpdateKey(ref _kbA, btnA, SC_SPACE, false);
                UpdateKey(ref _kbB, btnB, SC_ESCAPE, false);
                UpdateKey(ref _kbX, btnX, SC_LSHIFT, false);
                UpdateKey(ref _kbY, btnY, SC_C, false);

                // Bumpers: LB = Q, RB = E
                UpdateKey(ref _kbLB, btnLB, SC_Q, false);
                UpdateKey(ref _kbRB, btnRB, SC_E, false);

                // Select = Tab, Start = Return
                UpdateKey(ref _kbSelect, btnSelect, SC_TAB, false);
                UpdateKey(ref _kbStart, btnStart, SC_RETURN, false);

                // D-Pad: Arrow keys (Up, Down, Left, Right)
                UpdateKey(ref _kbDpadUp, btnDpadUp, SC_UP, true);
                UpdateKey(ref _kbDpadDown, btnDpadDown, SC_DOWN, true);
                UpdateKey(ref _kbDpadLeft, btnDpadLeft, SC_LEFT, true);
                UpdateKey(ref _kbDpadRight, btnDpadRight, SC_RIGHT, true);
            }
        }

        private static void UpdateKey(ref bool currentState, bool desiredState, ushort scanCode, bool isExtended)
        {
            if (desiredState != currentState)
            {
                currentState = desiredState;
                SendHardwareKey(scanCode, desiredState, isExtended);
            }
        }

        private static void SendHardwareKey(ushort scanCode, bool isDown, bool isExtended)
        {
            uint flags = KEYEVENTF_SCANCODE;
            if (!isDown) flags |= KEYEVENTF_KEYUP;
            if (isExtended) flags |= KEYEVENTF_EXTENDEDKEY;

            INPUT[] inputs = new INPUT[1];
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].u.ki.wVk = 0;
            inputs[0].u.ki.wScan = scanCode;
            inputs[0].u.ki.dwFlags = flags;
            inputs[0].u.ki.time = 0;
            inputs[0].u.ki.dwExtraInfo = IntPtr.Zero;

            SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        private static void WatchdogCheck(object? state)
        {
            lock (_syncLock)
            {
                if (!_initialized) return;

                long elapsedSinceLast = _stopwatch.ElapsedMilliseconds - _lastFrameTimestamp;
                if (elapsedSinceLast > 250)
                {
                    // No frames received recently, release all pressed keys to avoid stuck keys
                    ReleaseAllKeys();
                }
            }
        }

        private static void ReleaseAllKeys()
        {
            if (_kbGas) { UpdateKey(ref _kbGas, false, SC_W, false); }
            if (_kbBrake) { UpdateKey(ref _kbBrake, false, SC_S, false); }
            if (_kbSteerLeft) { UpdateKey(ref _kbSteerLeft, false, SC_A, false); }
            if (_kbSteerRight) { UpdateKey(ref _kbSteerRight, false, SC_D, false); }
            if (_kbA) { UpdateKey(ref _kbA, false, SC_SPACE, false); }
            if (_kbB) { UpdateKey(ref _kbB, false, SC_ESCAPE, false); }
            if (_kbX) { UpdateKey(ref _kbX, false, SC_LSHIFT, false); }
            if (_kbY) { UpdateKey(ref _kbY, false, SC_C, false); }
            if (_kbLB) { UpdateKey(ref _kbLB, false, SC_Q, false); }
            if (_kbRB) { UpdateKey(ref _kbRB, false, SC_E, false); }
            if (_kbSelect) { UpdateKey(ref _kbSelect, false, SC_TAB, false); }
            if (_kbStart) { UpdateKey(ref _kbStart, false, SC_RETURN, false); }
            if (_kbDpadUp) { UpdateKey(ref _kbDpadUp, false, SC_UP, true); }
            if (_kbDpadDown) { UpdateKey(ref _kbDpadDown, false, SC_DOWN, true); }
            if (_kbDpadLeft) { UpdateKey(ref _kbDpadLeft, false, SC_LEFT, true); }
            if (_kbDpadRight) { UpdateKey(ref _kbDpadRight, false, SC_RIGHT, true); }
        }

        public static void Shutdown()
        {
            lock (_syncLock)
            {
                _watchdogTimer?.Dispose();
                _watchdogTimer = null;

                ReleaseAllKeys();

                try
                {
                    if (_x360Controller != null)
                    {
                        _x360Controller.Disconnect();
                        _x360Controller = null;
                    }
                    if (_vigemClient != null)
                    {
                        _vigemClient.Dispose();
                        _vigemClient = null;
                    }
                }
                catch { }
                _initialized = false;
            }
        }
    }
}
