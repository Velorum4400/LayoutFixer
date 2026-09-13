using System.IO;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace LayoutFixer;

public static class AppAssets
{
    public static Icon GetIcon()
    {
        try
        {
            Icon? icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (icon != null)
                return (Icon)icon.Clone();
        }
        catch { }

        return (Icon)SystemIcons.Application.Clone();
    }

    public static Image? GetLogo()
    {
        try
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using Stream? stream = assembly.GetManifestResourceStream(
                "LayoutFixer.Assets.LayoutFixerLogo.png");

            if (stream == null)
                return null;

            using var image = Image.FromStream(stream);
            return new Bitmap(image);
        }
        catch
        {
            return null;
        }
    }
}
