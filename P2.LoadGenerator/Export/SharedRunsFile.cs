namespace P2.LoadGenerator.Export;

/// <summary>
/// Locates the shared <c>SampleData/runs.json</c> at the repo root — the same file
/// P3.Dashboard reads — so the console app and the GUI both export to it without each
/// hard-coding how many folders their own build output sits below the repo root.
/// </summary>
public static class SharedRunsFile
{
    public const string DataFolderName = "SampleData";
    public const string FileName = "runs.json";

    /// <summary>
    /// Walks up from <paramref name="startDirectory"/> (default: the app's build output
    /// folder) to the first directory containing a <c>SampleData</c> folder and returns
    /// that folder's runs.json path. Falls back to runs.json in the start directory when
    /// no such folder exists (e.g. the app was copied somewhere outside the repo).
    /// </summary>
    public static string ResolvePath(string? startDirectory = null)
    {
        var start = Path.GetFullPath(startDirectory ?? AppContext.BaseDirectory);

        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            var dataFolder = Path.Combine(dir.FullName, DataFolderName);
            if (Directory.Exists(dataFolder))
            {
                return Path.Combine(dataFolder, FileName);
            }
        }

        return Path.Combine(start, FileName);
    }
}
