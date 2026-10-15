namespace P2.LoadGenerator.Gui;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new LoadGeneratorForm());
    }
}
