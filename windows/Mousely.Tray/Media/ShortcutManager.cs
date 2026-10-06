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
        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

        private const byte VK_SHIFT = 0x10;
        private const byte VK_CONTROL = 0x11;
        private const byte VK_LWIN = 0x5B;
        private const byte VK_DELETE = 0x2E;
        private const byte VK_A = 0x41;
        private const byte VK_C = 0x43;
        private const byte VK_F = 0x46;
        private const byte VK_S = 0x53;
        private const byte VK_V = 0x56;
        private const byte VK_X = 0x58;
        private const byte VK_Z = 0x5A;

        private static void SendCtrlKey(byte vkKey, string name)
        {
            Task.Run(() =>
            {
                try
                {
                    keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                    Thread.Sleep(30);
                    keybd_event(vkKey, 0, 0, UIntPtr.Zero);
                    Thread.Sleep(30);
                    keybd_event(vkKey, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    Thread.Sleep(30);
                    keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    Console.WriteLine($"[ShortcutManager] Sent Ctrl+{name}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ShortcutManager] Error sending Ctrl+{name}: {ex.Message}");
                }
            });
        }

        public static void SendCut() => SendCtrlKey(VK_X, "X (Cut)");
        public static void SendCopy() => SendCtrlKey(VK_C, "C (Copy)");
        public static void SendPaste() => SendCtrlKey(VK_V, "V (Paste)");
        public static void SendUndo() => SendCtrlKey(VK_Z, "Z (Undo)");
        public static void SendSelectAll() => SendCtrlKey(VK_A, "A (Select All)");
        public static void SendFind() => SendCtrlKey(VK_F, "F (Find)");
        public static void SendSave() => SendCtrlKey(VK_S, "S (Save)");

        public static void SendDelete()
        {
            Task.Run(() =>
            {
                try
                {
                    keybd_event(VK_DELETE, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
                    Thread.Sleep(30);
                    keybd_event(VK_DELETE, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
                    Console.WriteLine("[ShortcutManager] Sent Del (Delete)");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ShortcutManager] Delete error: {ex.Message}");
                }
            });
        }

        public static void SendClipboardHistory()
        {
            Task.Run(() =>
            {
                try
                {
                    keybd_event(VK_LWIN, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
                    Thread.Sleep(35);
                    keybd_event(VK_V, 0, 0, UIntPtr.Zero);
                    Thread.Sleep(35);
                    keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    Thread.Sleep(35);
                    keybd_event(VK_LWIN, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
                    Console.WriteLine("[ShortcutManager] Sent Win+V (Clipboard History)");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ShortcutManager] Clipboard history error: {ex.Message}");
                }
            });
        }

        public static void SendScreenshot()
        {
            Task.Run(() =>
            {
                try
                {
                    keybd_event(VK_LWIN, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
                    keybd_event(VK_SHIFT, 0, 0, UIntPtr.Zero);
                    Thread.Sleep(35);
                    keybd_event(VK_S, 0, 0, UIntPtr.Zero);
                    Thread.Sleep(35);
                    keybd_event(VK_S, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    Thread.Sleep(35);
                    keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    keybd_event(VK_LWIN, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
                    Console.WriteLine("[ShortcutManager] Sent Win+Shift+S (Screenshot)");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ShortcutManager] Screenshot error: {ex.Message}");
                }

                try
                {
                    StartViaShell("ms-screenclip:");
                }
                catch { }
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
