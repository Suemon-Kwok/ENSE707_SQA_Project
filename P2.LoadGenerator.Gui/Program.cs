namespace P2.LoadGenerator.Gui;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var form = new LoadGeneratorForm();

        // `--smoke <exportPath>`: CI check that the form opens, runs and exports, then exits
        // with 0 (pass) or 1 (fail, details written to <exportPath>.error.txt).
        if (args is ["--smoke", var exportPath])
        {
            var exitCode = 1;
            form.Shown += async (_, _) =>
            {
                try
                {
                    await form.RunSmokeTestAsync(exportPath);
                    exitCode = 0;
                }
                catch (Exception ex)
                {
                    File.WriteAllText(exportPath + ".error.txt", ex.ToString());
                }
                finally
                {
                    form.Close();
                }
            };
            Application.Run(form);
            return exitCode;
        }

        Application.Run(form);
        return 0;
    }
}
