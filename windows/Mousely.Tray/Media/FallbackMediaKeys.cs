using System;
using System.Runtime.InteropServices;

namespace Mousely.Tray.Media
{
    public static class FallbackMediaKeys
    {
        private const byte VK_MEDIA_NEXT_TRACK = 0xB0;
        private const byte VK_MEDIA_PREV_TRACK = 0xB1;
        private const byte VK_MEDIA_STOP = 0xB2;
        private const byte VK_MEDIA_PLAY_PAUSE = 0xB3;
        private const byte VK_VOLUME_MUTE = 0xAD;
        private const byte VK_VOLUME_DOWN = 0xAE;
        private const byte VK_VOLUME_UP = 0xAF;

        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private static void PressKey(byte vk)
        {
            keybd_event(vk, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
            keybd_event(vk, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        public static void PlayPause() => PressKey(VK_MEDIA_PLAY_PAUSE);
        public static void NextTrack() => PressKey(VK_MEDIA_NEXT_TRACK);
        public static void PreviousTrack() => PressKey(VK_MEDIA_PREV_TRACK);
        public static void Stop() => PressKey(VK_MEDIA_STOP);
        public static void ToggleMute() => PressKey(VK_VOLUME_MUTE);
    }
}
