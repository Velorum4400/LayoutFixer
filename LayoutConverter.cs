using System;
using System.Runtime.InteropServices;
using System.Text;

namespace LayoutFixer;

public static class LayoutConverter
{
    private const uint MAPVK_VK_TO_VSC = 0;
    private const uint TO_UNICODE_NO_STATE_CHANGE = 0x0004;

    public static string Convert(string sourceText, KeyboardLayoutInfo source, KeyboardLayoutInfo target)
    {
        if (string.IsNullOrEmpty(sourceText) || source.Handle == target.Handle)
            return sourceText;

        var result = new StringBuilder(sourceText.Length);
        foreach (char character in sourceText)
        {
            if (char.IsWhiteSpace(character))
            {
                result.Append(character);
                continue;
            }

            short key = VkKeyScanEx(character, source.Handle);
            if (key == -1)
            {
                result.Append(character);
                continue;
            }

            uint virtualKey = (byte)(key & 0xff);
            byte modifiers = (byte)((key >> 8) & 0xff);
            var state = new byte[256];
            if ((modifiers & 1) != 0) state[0x10] = 0x80;
            if ((modifiers & 2) != 0) state[0x11] = 0x80;
            if ((modifiers & 4) != 0) state[0x12] = 0x80;

            uint scanCode = MapVirtualKeyEx(virtualKey, MAPVK_VK_TO_VSC, target.Handle);
            var buffer = new StringBuilder(8);
            int count = ToUnicodeEx(virtualKey, scanCode, state, buffer, buffer.Capacity,
                TO_UNICODE_NO_STATE_CHANGE, target.Handle);
            if (count > 0)
                result.Append(buffer.ToString(0, count));
            else
                result.Append(character);
        }
        return result.ToString();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern short VkKeyScanEx(char character, IntPtr keyboardLayout);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyEx(uint code, uint mapType, IntPtr keyboardLayout);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ToUnicodeEx(uint virtualKey, uint scanCode, byte[] keyboardState,
        StringBuilder buffer, int bufferSize, uint flags, IntPtr keyboardLayout);
}
