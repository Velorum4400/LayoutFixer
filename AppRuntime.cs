using System;
using System.IO;

namespace LayoutFixer;

public static class AppRuntime
{
    public const string PortableMarkerFileName = "portable.mode";

    public static string ExecutableDirectory => AppContext.BaseDirectory;

    public static bool IsPortable =>
        File.Exists(Path.Combine(ExecutableDirectory, PortableMarkerFileName));

    public static string DataDirectory => IsPortable
        ? Path.Combine(ExecutableDirectory, "data")
        : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LayoutFixer");

    public static string GetDataPath(string fileName) =>
        Path.Combine(DataDirectory, fileName);
}
