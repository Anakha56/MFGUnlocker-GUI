using System;
using System.IO;
using System.Windows.Forms;
using MFGUnlockerGUI.Core;
using MFGUnlockerGUI.UI;

namespace MFGUnlockerGUI
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => Report(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => LogError(e.ExceptionObject as Exception);

            AppPaths paths;
            try { paths = AppPaths.Default; }
            catch (Exception ex)
            {
                MessageBox.Show("Something went wrong: " + ex.Message, "MFGUnlocker GUI");
                return;
            }
            Application.Run(new MainForm(paths));
        }

        private static void Report(Exception ex)
        {
            LogError(ex);
            try { MessageBox.Show("Something went wrong: " + (ex != null ? ex.Message : "unknown error"), "MFGUnlocker GUI"); } catch { }
        }

        /// <summary>Appends to %LOCALAPPDATA%\MFGUnlockerGUI\error.log. Never throws.</summary>
        public static void LogError(Exception ex)
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MFGUnlockerGUI");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "error.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + (ex != null ? ex.ToString() : "unknown error") + Environment.NewLine + Environment.NewLine);
            }
            catch { }
        }
    }
}
