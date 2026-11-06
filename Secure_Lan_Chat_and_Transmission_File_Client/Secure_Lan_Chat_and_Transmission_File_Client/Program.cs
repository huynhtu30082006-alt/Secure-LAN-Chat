using System;
using System.Windows.Forms;
using Secure_Lan_Chat___Transmission_File_Client.Forms;

namespace Secure_Lan_Chat___Transmission_File_Client
{
    internal static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}