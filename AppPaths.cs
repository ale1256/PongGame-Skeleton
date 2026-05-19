namespace TheAdventure;

// Centralizează căile folosite de joc 
public static class AppPaths
{
// Folderul aplicației sub LocalApplicationData
    private const string AppFolderName = "TheAdventure-PongPlus";

    public static string GetDataDirectory()
    {
// Pe Windows/macOS/Linux, LocalApplicationData e locul “corect” pentru datele aplicației.
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(baseDir))
        {
            baseDir = AppContext.BaseDirectory;
        }

        return Path.Combine(baseDir, AppFolderName);
    }

    public static string GetSaveFilePath()
    {
// Fișierul de BestRally, MatchesFinished 
        return Path.Combine(GetDataDirectory(), "save.json");
    }
}
