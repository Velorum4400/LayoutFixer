using System;
using System.Reflection;

namespace LayoutFixer;

public static class AppInfo
{
    public const string Name = "Layout Fixer";

    public static string Version
    {
        get
        {
            Version? version = Assembly.GetExecutingAssembly().GetName().Version;

            if (version == null)
                return "0.0.0";

            return $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    public static string DisplayName => $"{Name} v{Version}";
}
