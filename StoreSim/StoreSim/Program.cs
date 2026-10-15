using StoreSim;
using StoreSimGUI;
using System;
using System.Windows.Forms;
using static System.Net.Mime.MediaTypeNames;

namespace StoreSimGUI
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            var store = new Store();
            Application.Run(new LoginForm(store));
        }
    }
}