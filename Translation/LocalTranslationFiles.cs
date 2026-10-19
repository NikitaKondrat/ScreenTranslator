using System;
using System.IO;

namespace ScreenTranslator;

public static class LocalTranslationFiles
{
    public const string RuntimeVersion = "b11669";
    public const string ModelFileName = "Hy-MT2-1.8B-Q4_K_M.gguf";
    public static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ScreenTranslator", "LocalTranslation");
    public static string RuntimeDirectory => Path.Combine(DirectoryPath, "llama-" + RuntimeVersion);
    public static string DefaultServerPath => Path.Combine(RuntimeDirectory, "llama-server.exe");
    public static string DefaultModelPath => Path.Combine(DirectoryPath, ModelFileName);

    public static string ServerPath(Settings settings) => string.IsNullOrWhiteSpace(settings.LocalServerPath)
        ? DefaultServerPath : settings.LocalServerPath.Trim();
    public static string ModelPath(Settings settings) => string.IsNullOrWhiteSpace(settings.LocalModelPath)
        ? DefaultModelPath : settings.LocalModelPath.Trim();

    public static bool IsInstalled(Settings settings) =>
        File.Exists(ServerPath(settings)) && File.Exists(ModelPath(settings));
}
