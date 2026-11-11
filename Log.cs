namespace StockWidgetNative;

internal static class Log
{
    private static readonly object Gate = new();
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "StockWidgetNative");
    private static readonly string FilePath = Path.Combine(DirectoryPath, "widget.log");

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            lock (Gate)
            {
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > 2 * 1024 * 1024)
                {
                    File.Move(FilePath, FilePath + ".old", true);
                }
                File.AppendAllText(
                    FilePath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never take the provider down.
        }
    }

    public static void Error(string message, Exception? ex = null)
    {
        Write(message + (ex is null ? string.Empty : " " + ex));
    }
}
