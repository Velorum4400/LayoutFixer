using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace LayoutFixer;

public static class HotkeyDefinition
{
    public const int MaxKeys = 3;

    public static Keys Normalize(Keys key)
    {
        key &= Keys.KeyCode;

        return key switch
        {
            Keys.LControlKey or Keys.RControlKey or Keys.ControlKey => Keys.ControlKey,
            Keys.LShiftKey or Keys.RShiftKey or Keys.ShiftKey => Keys.ShiftKey,
            Keys.LMenu or Keys.RMenu or Keys.Menu => Keys.Menu,
            Keys.RWin or Keys.LWin => Keys.LWin,
            _ => key
        };
    }

    public static HashSet<Keys> Parse(string? value)
    {
        var result = new HashSet<Keys>();

        if (string.IsNullOrWhiteSpace(value))
            return result;

        foreach (string part in value.Split(
            '+',
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries))
        {
            if (TryParseToken(part, out Keys key))
                result.Add(Normalize(key));
        }

        return result;
    }

    public static bool IsValid(string? value)
    {
        int count = Parse(value).Count;
        return count >= 1 && count <= MaxKeys;
    }

    public static string Format(IEnumerable<Keys> keys)
    {
        var normalized = keys
            .Select(Normalize)
            .Where(k => k != Keys.None)
            .Distinct()
            .Take(MaxKeys)
            .ToList();

        var ordered = new List<Keys>();

        AddIfPresent(Keys.ControlKey);
        AddIfPresent(Keys.ShiftKey);
        AddIfPresent(Keys.Menu);
        AddIfPresent(Keys.LWin);

        foreach (Keys key in normalized)
        {
            if (!IsModifier(key))
                ordered.Add(key);
        }

        return string.Join("+", ordered.Select(ToDisplayName));

        void AddIfPresent(Keys key)
        {
            if (normalized.Contains(key))
                ordered.Add(key);
        }
    }

    public static bool IsModifier(Keys key)
    {
        key = Normalize(key);

        return key is Keys.ControlKey
            or Keys.ShiftKey
            or Keys.Menu
            or Keys.LWin;
    }

    public static string ToDisplayName(Keys key)
    {
        key = Normalize(key);

        return key switch
        {
            Keys.ControlKey => "Ctrl",
            Keys.ShiftKey => "Shift",
            Keys.Menu => "Alt",
            Keys.LWin => "Win",
            Keys.Escape => "Esc",
            Keys.Return => "Enter",
            Keys.Space => "Space",
            Keys.Back => "Backspace",
            Keys.Delete => "Delete",
            Keys.Insert => "Insert",
            Keys.Prior => "PageUp",
            Keys.Next => "PageDown",
            Keys.Left => "Left",
            Keys.Right => "Right",
            Keys.Up => "Up",
            Keys.Down => "Down",
            Keys.D0 => "0",
            Keys.D1 => "1",
            Keys.D2 => "2",
            Keys.D3 => "3",
            Keys.D4 => "4",
            Keys.D5 => "5",
            Keys.D6 => "6",
            Keys.D7 => "7",
            Keys.D8 => "8",
            Keys.D9 => "9",
            Keys.Oemplus => "PlusKey",
            Keys.OemMinus => "Minus",
            Keys.Oemcomma => "Comma",
            Keys.OemPeriod => "Period",
            _ => key.ToString()
        };
    }

    private static bool TryParseToken(string token, out Keys key)
    {
        switch (token.Trim().ToLowerInvariant())
        {
            case "ctrl":
            case "control":
                key = Keys.ControlKey;
                return true;

            case "shift":
                key = Keys.ShiftKey;
                return true;

            case "alt":
                key = Keys.Menu;
                return true;

            case "win":
            case "windows":
                key = Keys.LWin;
                return true;

            case "esc":
                key = Keys.Escape;
                return true;

            case "enter":
                key = Keys.Return;
                return true;

            case "space":
                key = Keys.Space;
                return true;

            case "backspace":
                key = Keys.Back;
                return true;

            case "pageup":
                key = Keys.Prior;
                return true;

            case "pagedown":
                key = Keys.Next;
                return true;

            case "pluskey":
                key = Keys.Oemplus;
                return true;

            case "minus":
                key = Keys.OemMinus;
                return true;

            case "comma":
                key = Keys.Oemcomma;
                return true;

            case "period":
                key = Keys.OemPeriod;
                return true;
        }

        if (token.Length == 1)
        {
            char c = token[0];

            if (char.IsLetter(c))
            {
                key = (Keys)Enum.Parse(
                    typeof(Keys),
                    char.ToUpperInvariant(c).ToString());
                return true;
            }

            if (char.IsDigit(c))
            {
                key = (Keys)((int)Keys.D0 + (c - '0'));
                return true;
            }
        }

        return Enum.TryParse(token, true, out key);
    }
}
