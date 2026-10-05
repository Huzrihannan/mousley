using System;
using System.Runtime.InteropServices;

namespace Mousely.Tray.Audio
{
    public class CoreAudioVolume : IDisposable
    {
        private IMMDeviceEnumerator? _deviceEnumerator;
        private IMMDevice? _defaultDevice;
        private IAudioEndpointVolume? _endpointVolume;
        private VolumeNotificationCallback? _callback;
        private bool _isDisposed;

        public event Action<float, bool>? VolumeChanged;

        public CoreAudioVolume()
        {
            InitializeAudioEndpoint();
        }

        private void InitializeAudioEndpoint()
        {
            try
            {
                _deviceEnumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                // 0 = eRender, 1 = eMultimedia
                int hr = _deviceEnumerator.GetDefaultAudioEndpoint(0, 1, out _defaultDevice);
                if (hr != 0 || _defaultDevice == null)
                {
                    Console.WriteLine($"[CoreAudio] Failed to get default audio endpoint (hr={hr})");
                    return;
                }

                Guid iid = typeof(IAudioEndpointVolume).GUID;
                hr = _defaultDevice.Activate(ref iid, 1 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out var obj);
                if (hr != 0 || obj == null)
                {
                    Console.WriteLine($"[CoreAudio] Failed to activate IAudioEndpointVolume (hr={hr})");
                    return;
                }

                _endpointVolume = (IAudioEndpointVolume)obj;
                _callback = new VolumeNotificationCallback(this);
                _endpointVolume.RegisterControlChangeNotify(_callback);
                Console.WriteLine("[CoreAudio] Initialized CoreAudio volume listener successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CoreAudio] Initialization error: {ex.Message}");
            }
        }

        public float GetMasterVolume()
        {
            if (_endpointVolume == null) return 0.5f;
            try
            {
                _endpointVolume.GetMasterVolumeLevelScalar(out float level);
                return level;
            }
            catch
            {
                return 0.5f;
            }
        }

        public void SetMasterVolume(float level)
        {
            if (_endpointVolume == null) return;
            try
            {
                level = Math.Clamp(level, 0.0f, 1.0f);
                Guid context = Guid.Empty;
                _endpointVolume.SetMasterVolumeLevelScalar(level, ref context);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CoreAudio] SetMasterVolume error: {ex.Message}");
            }
        }

        public bool GetMute()
        {
            if (_endpointVolume == null) return false;
            try
            {
                _endpointVolume.GetMute(out bool isMuted);
                return isMuted;
            }
            catch
            {
                return false;
            }
        }

        public void SetMute(bool isMuted)
        {
            if (_endpointVolume == null) return;
            try
            {
                Guid context = Guid.Empty;
                _endpointVolume.SetMute(isMuted, ref context);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CoreAudio] SetMute error: {ex.Message}");
            }
        }

        public void ToggleMute()
        {
            SetMute(!GetMute());
        }

        internal void OnVolumeNotify(float volume, bool isMuted)
        {
            VolumeChanged?.Invoke(volume, isMuted);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            try
            {
                if (_endpointVolume != null && _callback != null)
                {
                    _endpointVolume.UnregisterControlChangeNotify(_callback);
                }
            }
            catch { }
        }

        // --- COM Interop Definitions ---

        [ComImport]
        [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorComObject { }

        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(int dataFlow, int dwStateMask, out IntPtr ppDevices);
            [PreserveSig]
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppEndpoint);
        }

        [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig]
            int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        }

        [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            [PreserveSig]
            int RegisterControlChangeNotify(IAudioEndpointVolumeCallback pNotify);
            [PreserveSig]
            int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback pNotify);
            [PreserveSig]
            int GetChannelCount(out uint pnChannelCount);
            [PreserveSig]
            int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
            [PreserveSig]
            int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
            [PreserveSig]
            int GetMasterVolumeLevel(out float pfLevelDB);
            [PreserveSig]
            int GetMasterVolumeLevelScalar(out float pfLevel);
            [PreserveSig]
            int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
            [PreserveSig]
            int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
            [PreserveSig]
            int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
            [PreserveSig]
            int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
            [PreserveSig]
            int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
            [PreserveSig]
            int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
        }

        [Guid("657804FA-D6AD-4496-8A60-522725206673"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolumeCallback
        {
            [PreserveSig]
            int OnNotify(IntPtr pNotify);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AUDIO_VOLUME_NOTIFICATION_DATA
        {
            public Guid guidEventContext;
            [MarshalAs(UnmanagedType.Bool)]
            public bool bMuted;
            public float fMasterVolume;
            public uint nChannels;
        }

        private class VolumeNotificationCallback : IAudioEndpointVolumeCallback
        {
            private readonly CoreAudioVolume _parent;

            public VolumeNotificationCallback(CoreAudioVolume parent)
            {
                _parent = parent;
            }

            public int OnNotify(IntPtr pNotify)
            {
                if (pNotify != IntPtr.Zero)
                {
                    try
                    {
                        var data = Marshal.PtrToStructure<AUDIO_VOLUME_NOTIFICATION_DATA>(pNotify);
                        _parent.OnVolumeNotify(data.fMasterVolume, data.bMuted);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[CoreAudio] Notification marshal error: {ex.Message}");
                    }
                }
                return 0; // S_OK
            }
        }
    }
}
