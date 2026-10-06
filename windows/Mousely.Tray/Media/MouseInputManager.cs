using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Mousely.Tray.Media
{
    public static class MouseInputManager
    {
        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const uint MOUSEEVENTF_MOVE = 0x0001;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;
        private const uint MOUSEEVENTF_HWHEEL = 0x01000;

        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

        private const byte VK_LWIN = 0x5B;
        private const byte VK_CONTROL = 0x11;
        private const byte VK_TAB = 0x09;
        private const byte VK_D = 0x44;
        private const byte VK_LEFT = 0x25;
        private const byte VK_RIGHT = 0x27;

        public static void Move(int dx, int dy)
        {
            mouse_event(MOUSEEVENTF_MOVE, dx, dy, 0, UIntPtr.Zero);
        }

        public static void LeftClick()
        {
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(12);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
        }

        public static void RightClick()
        {
            mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(12);
            mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
        }

        public static void MiddleClick()
        {
            mouse_event(MOUSEEVENTF_MIDDLEDOWN, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(12);
            mouse_event(MOUSEEVENTF_MIDDLEUP, 0, 0, 0, UIntPtr.Zero);
        }

        public static void MouseDown(string button = "left")
        {
            if (button.Equals("right", StringComparison.OrdinalIgnoreCase))
                mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
            else if (button.Equals("middle", StringComparison.OrdinalIgnoreCase))
                mouse_event(MOUSEEVENTF_MIDDLEDOWN, 0, 0, 0, UIntPtr.Zero);
            else
                mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        }

        public static void MouseUp(string button = "left")
        {
            if (button.Equals("right", StringComparison.OrdinalIgnoreCase))
                mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
            else if (button.Equals("middle", StringComparison.OrdinalIgnoreCase))
                mouse_event(MOUSEEVENTF_MIDDLEUP, 0, 0, 0, UIntPtr.Zero);
            else
                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
        }

        public static void Scroll(int deltaY, int deltaX = 0)
        {
            if (deltaY != 0)
            {
                mouse_event(MOUSEEVENTF_WHEEL, 0, 0, unchecked((uint)deltaY), UIntPtr.Zero);
            }
            if (deltaX != 0)
            {
                mouse_event(MOUSEEVENTF_HWHEEL, 0, 0, unchecked((uint)deltaX), UIntPtr.Zero);
            }
        }

        public static void TriggerGesture(string type)
        {
            Task.Run(() =>
            {
                try
                {
                    switch (type.ToLowerInvariant())
                    {
                        case "task_view":
                            keybd_event(VK_LWIN, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
                            Thread.Sleep(30);
                            keybd_event(VK_TAB, 0, 0, UIntPtr.Zero);
                            Thread.Sleep(30);
                            keybd_event(VK_TAB, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                            Thread.Sleep(30);
                            keybd_event(VK_LWIN, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
                            break;

                        case "show_desktop":
                            keybd_event(VK_LWIN, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
                            Thread.Sleep(30);
                            keybd_event(VK_D, 0, 0, UIntPtr.Zero);
                            Thread.Sleep(30);
                            keybd_event(VK_D, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                            Thread.Sleep(30);
                            keybd_event(VK_LWIN, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
                            break;

                        case "desktop_left":
                            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                            keybd_event(VK_LWIN, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
                            Thread.Sleep(30);
                            keybd_event(VK_LEFT, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
                            Thread.Sleep(30);
                            keybd_event(VK_LEFT, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
                            Thread.Sleep(30);
                            keybd_event(VK_LWIN, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
                            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                            break;

                        case "desktop_right":
                            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                            keybd_event(VK_LWIN, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
                            Thread.Sleep(30);
                            keybd_event(VK_RIGHT, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
                            Thread.Sleep(30);
                            keybd_event(VK_RIGHT, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
                            Thread.Sleep(30);
                            keybd_event(VK_LWIN, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
                            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MouseInputManager] Gesture error: {ex.Message}");
                }
            });
        }
    }
}
