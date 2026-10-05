using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Mousely.Tray.Media
{
    public static class ShortcutManager
    {
        private const byte VK_CONTROL = 0x11;
        private const byte VK_LWIN = 0x5B;
        private const byte VK_C = 0x43;
        private const byte VK_V = 0x56;

        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        public static void SendCopy()
        {
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(VK_C, 0, 0, UIntPtr.Zero);
            keybd_event(VK_C, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        public static void SendPaste()
        {
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(VK_V, 0, 0, UIntPtr.Zero);
            keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        public static void SendClipboardHistory()
        {
            keybd_event(VK_LWIN, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
            keybd_event(VK_V, 0, 0, UIntPtr.Zero);
            keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_LWIN, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        public static bool LaunchSlot(int slotIndex, string? customCommand = null)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(customCommand))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = customCommand,
                        UseShellExecute = true
                    });
                    return true;
                }

                string target = slotIndex switch
                {
                    1 => "https://www.google.com", // Browser / Web
                    2 => "spotify:",               // Spotify / Music
                    3 => "explorer.exe",           // File Explorer
                    4 => "taskmgr.exe",            // Task Manager
                    5 => "wt.exe",                 // Windows Terminal / PowerShell
                    6 => "calc.exe",               // Calculator
                    7 => "notepad.exe",            // Notepad
                    8 => "ms-settings:",           // Windows Settings
                    _ => "explorer.exe"
                };

                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = target,
                        UseShellExecute = true
                    });
                }
                catch
                {
                    // Fallback for slot 5 (wt.exe fallback to powershell)
                    if (slotIndex == 5)
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "powershell.exe",
                            UseShellExecute = true
                        });
                    }
                    else if (slotIndex == 2)
                    {
                        // Fallback for spotify if protocol not registered
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = "shell:AppsFolder",
                            UseShellExecute = true
                        });
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ShortcutManager] Launch error: {ex.Message}");
                return false;
            }
        }
    }
}
