using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Mousely.Tray.Media
{
    public static class ShortcutManager
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

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
            public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

        private const ushort VK_CONTROL = 0x11;
        private const ushort VK_LWIN = 0x5B;
        private const ushort VK_C = 0x43;
        private const ushort VK_V = 0x56;

        private static void SendKey(ushort vk, bool isUp, bool isExtended = false)
        {
            var input = new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = vk,
                        wScan = (ushort)MapVirtualKey(vk, 0),
                        dwFlags = (isUp ? KEYEVENTF_KEYUP : 0) | (isExtended ? KEYEVENTF_EXTENDEDKEY : 0),
                        time = 0,
                        dwExtraInfo = UIntPtr.Zero
                    }
                }
            };
            SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
        }

        public static void SendCopy()
        {
            Task.Run(() =>
            {
                try
                {
                    SendKey(VK_CONTROL, false);
                    Thread.Sleep(45);
                    SendKey(VK_C, false);
                    Thread.Sleep(45);
                    SendKey(VK_C, true);
                    Thread.Sleep(45);
                    SendKey(VK_CONTROL, true);
                    Console.WriteLine("[ShortcutManager] Sent Ctrl+C (Copy)");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ShortcutManager] Copy error: {ex.Message}");
                }
            });
        }

        public static void SendPaste()
        {
            Task.Run(() =>
            {
                try
                {
                    SendKey(VK_CONTROL, false);
                    Thread.Sleep(45);
                    SendKey(VK_V, false);
                    Thread.Sleep(45);
                    SendKey(VK_V, true);
                    Thread.Sleep(45);
                    SendKey(VK_CONTROL, true);
                    Console.WriteLine("[ShortcutManager] Sent Ctrl+V (Paste)");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ShortcutManager] Paste error: {ex.Message}");
                }
            });
        }

        public static void SendClipboardHistory()
        {
            Task.Run(() =>
            {
                try
                {
                    SendKey(VK_LWIN, false, isExtended: true);
                    Thread.Sleep(50);
                    SendKey(VK_V, false);
                    Thread.Sleep(50);
                    SendKey(VK_V, true);
                    Thread.Sleep(50);
                    SendKey(VK_LWIN, true, isExtended: true);
                    Console.WriteLine("[ShortcutManager] Sent Win+V (Clipboard History)");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ShortcutManager] Clipboard history error: {ex.Message}");
                }
            });
        }

        public static bool LaunchSlot(int slotIndex, string? customCommand = null)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(customCommand))
                {
                    StartViaShell(customCommand);
                    return true;
                }

                switch (slotIndex)
                {
                    case 1: // Browser
                        StartViaShell("https://www.google.com");
                        break;

                    case 2: // Spotify
                        string appDataSpotify = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Spotify\Spotify.exe");
                        string localSpotify = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\Spotify.exe");
                        if (File.Exists(appDataSpotify))
                        {
                            StartViaShell(appDataSpotify);
                        }
                        else if (File.Exists(localSpotify))
                        {
                            StartViaShell(localSpotify);
                        }
                        else
                        {
                            StartViaShell("spotify:");
                        }
                        break;

                    case 3: // File Explorer
                        StartViaShell("explorer.exe");
                        break;

                    case 4: // Task Manager
                        StartViaShell("taskmgr.exe");
                        break;

                    case 5: // Terminal / PowerShell
                        try
                        {
                            StartViaShell("wt.exe");
                        }
                        catch
                        {
                            StartViaShell("powershell.exe");
                        }
                        break;

                    case 6: // Calculator
                        StartViaShell("calc.exe");
                        break;

                    case 7: // Notepad
                        StartViaShell("notepad.exe");
                        break;

                    case 8: // Windows Settings
                        StartViaShell("ms-settings:");
                        break;

                    default:
                        StartViaShell("explorer.exe");
                        break;
                }

                Console.WriteLine($"[ShortcutManager] Launched slot {slotIndex}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ShortcutManager] Launch error for slot {slotIndex}: {ex.Message}");
                return false;
            }
        }

        private static void StartViaShell(string target)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c start \"\" \"{target}\"",
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ShortcutManager] StartViaShell failed for '{target}': {ex.Message}");
                // Direct fallback
                Process.Start(new ProcessStartInfo
                {
                    FileName = target,
                    UseShellExecute = true
                });
            }
        }
    }
}
