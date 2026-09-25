using System;
using System.IO;

namespace LayoutFixer;

public static class AppRuntime
{
    public static string DataDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LayoutFixer");

    public static string GetDataPath(string fileName) =>
        Path.Combine(DataDirectory, fileName);
}
