using System;
using System.Windows.Forms;
using StoreSim;
using StoreSimGUI;

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