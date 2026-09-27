using System;
using System.IO;

namespace ScreenTranslator;

internal static class UserData
{
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ScreenTranslator");

    public static string PathFor(string name)
    {
        Directory.CreateDirectory(DirectoryPath);
        var target = Path.Combine(DirectoryPath, name);
        var old = Path.Combine(AppContext.BaseDirectory, name);
        if (!File.Exists(target) && File.Exists(old))
            File.Copy(old, target);
        return target;
    }

    public static void Write(string name, string content)
    {
        var target = PathFor(name);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, content);
            if (File.Exists(target))
                File.Replace(temporary, target, target + ".bak");
            else
                File.Move(temporary, target);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
