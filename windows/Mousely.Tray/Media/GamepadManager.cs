using System;
using System.Runtime.InteropServices;
using System.Threading;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace Mousely.Tray.Media
{
    public static class GamepadManager
    {
        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

        // Virtual Key Codes for Fallback
        private const byte VK_W = 0x57;
        private const byte VK_S = 0x53;
        private const byte VK_A = 0x41;
        private const byte VK_D = 0x44;
        private const byte VK_SPACE = 0x20;
        private const byte VK_ESCAPE = 0x1B;
        private const byte VK_SHIFT = 0x10;
        private const byte VK_C = 0x43;
        private const byte VK_Q = 0x51;
        private const byte VK_E = 0x45;
        private const byte VK_RETURN = 0x0D;
        private const byte VK_TAB = 0x09;
        private const byte VK_UP = 0x26;
        private const byte VK_DOWN = 0x28;
        private const byte VK_LEFT = 0x25;
        private const byte VK_RIGHT = 0x27;

        // ViGEm Emulation Client & Controller
        private static ViGEmClient? _vigemClient;
        private static IXbox360Controller? _x360Controller;
        private static bool _useViGEm = false;
        private static bool _initialized = false;
        private static readonly object _syncLock = new();

        // Keyboard Fallback State Tracking to prevent repeated key downs
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
                    Console.WriteLine("[GamepadManager] ViGEmBus detected! Virtual Xbox 360 Gamepad connected successfully.");
                }
                catch (Exception ex)
                {
                    _useViGEm = false;
                    _vigemClient = null;
                    _x360Controller = null;
                    Console.WriteLine($"[GamepadManager] ViGEm driver not installed or unavailable ({ex.Message}). Operating in ultra-fast Keyboard/Direct fallback mode.");
                }

                _initialized = true;
            }
        }

        public static void ProcessGamepadFrame(short stickX, short stickY, byte lt, byte rt, ushort buttonMask)
        {
            if (!_initialized)
            {
                Initialize();
            }

            // Buttons bitmask definitions:
            // bit 0: A
            // bit 1: B
            // bit 2: X
            // bit 3: Y
            // bit 4: DPAD_UP
            // bit 5: DPAD_DOWN
            // bit 6: DPAD_LEFT
            // bit 7: DPAD_RIGHT
            // bit 8: LB
            // bit 9: RB
            // bit 10: SELECT
            // bit 11: START
            // bit 12: L3 (Thumbstick Click)
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
                    Console.WriteLine($"[GamepadManager] ViGEm error: {ex.Message}. Falling back to keyboard input.");
                    _useViGEm = false;
                }
            }

            // ================= FAST KEYBOARD FALLBACK =================
            // Throttle & Brake (RT / LT or Y-Axis)
            bool wantGas = rt > 40 || stickY > 12000;
            bool wantBrake = lt > 40 || stickY < -12000;
            UpdateKey(ref _kbGas, wantGas, VK_W);
            UpdateKey(ref _kbBrake, wantBrake, VK_S);

            // Steering Left / Right (Stick X / Gyro Angle)
            bool wantLeft = stickX < -5000;
            bool wantRight = stickX > 5000;
            UpdateKey(ref _kbSteerLeft, wantLeft, VK_A);
            UpdateKey(ref _kbSteerRight, wantRight, VK_D);

            // ABXY buttons
            UpdateKey(ref _kbA, btnA, VK_SPACE);    // Space = Handbrake / Accept
            UpdateKey(ref _kbB, btnB, VK_ESCAPE);   // Esc = Cancel / Menu
            UpdateKey(ref _kbX, btnX, VK_SHIFT);    // Shift = Nitro / Boost
            UpdateKey(ref _kbY, btnY, VK_C);        // C = Camera View
            
            // Bumpers
            UpdateKey(ref _kbLB, btnLB, VK_Q);      // Q = Shift Down
            UpdateKey(ref _kbRB, btnRB, VK_E);      // E = Shift Up

            // Select & Start
            UpdateKey(ref _kbSelect, btnSelect, VK_TAB);
            UpdateKey(ref _kbStart, btnStart, VK_RETURN);

            // D-Pad
            UpdateKey(ref _kbDpadUp, btnDpadUp, VK_UP);
            UpdateKey(ref _kbDpadDown, btnDpadDown, VK_DOWN);
            UpdateKey(ref _kbDpadLeft, btnDpadLeft, VK_LEFT);
            UpdateKey(ref _kbDpadRight, btnDpadRight, VK_RIGHT);
        }

        private static void UpdateKey(ref bool currentState, bool desiredState, byte vkCode)
        {
            if (desiredState != currentState)
            {
                currentState = desiredState;
                if (desiredState)
                {
                    keybd_event(vkCode, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
                }
                else
                {
                    keybd_event(vkCode, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
                }
            }
        }

        public static void Shutdown()
        {
            lock (_syncLock)
            {
                // Release any held fallback keys
                if (_kbGas) keybd_event(VK_W, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
                if (_kbBrake) keybd_event(VK_S, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
                if (_kbSteerLeft) keybd_event(VK_A, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
                if (_kbSteerRight) keybd_event(VK_D, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
                if (_kbA) keybd_event(VK_SPACE, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
                if (_kbB) keybd_event(VK_ESCAPE, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);

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
