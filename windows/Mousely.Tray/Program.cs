using System;
using System.Threading;
using System.Windows.Forms;

namespace Mousely.Tray
{
    internal static class Program
    {
        private const string MutexName = "MouselyMediaRemoteSingleInstanceMutex";
        private static Mutex? _mutex;

        [STAThread]
        static void Main()
        {
            _mutex = new Mutex(true, MutexName, out bool createdNew);

            if (!createdNew)
            {
                MessageBox.Show("Mousely is already running in your system tray!", "Mousely", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                Application.Run(new AppTrayContext());
            }
            finally
            {
                _mutex?.ReleaseMutex();
            }
        }
    }
}
