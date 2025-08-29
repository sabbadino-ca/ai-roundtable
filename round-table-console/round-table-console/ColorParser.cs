using System;
using System.Collections.Generic;

namespace round_table_console;

internal static class ColorParser
{
    private static readonly Dictionary<string, ConsoleColor> _aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = ConsoleColor.Black, ["blk"] = ConsoleColor.Black,
        ["darkblue"] = ConsoleColor.DarkBlue, ["db"] = ConsoleColor.DarkBlue,
        ["darkgreen"] = ConsoleColor.DarkGreen, ["dg"] = ConsoleColor.DarkGreen,
        ["darkcyan"] = ConsoleColor.DarkCyan, ["dc"] = ConsoleColor.DarkCyan,
        ["darkred"] = ConsoleColor.DarkRed, ["dr"] = ConsoleColor.DarkRed,
        ["darkmagenta"] = ConsoleColor.DarkMagenta, ["dm"] = ConsoleColor.DarkMagenta,
        ["darkyellow"] = ConsoleColor.DarkYellow, ["dy"] = ConsoleColor.DarkYellow,
        ["gray"] = ConsoleColor.Gray, ["grey"] = ConsoleColor.Gray,
        ["darkgray"] = ConsoleColor.DarkGray, ["darkgrey"] = ConsoleColor.DarkGray,
        ["blue"] = ConsoleColor.Blue,
        ["green"] = ConsoleColor.Green,
        ["cyan"] = ConsoleColor.Cyan,
        ["red"] = ConsoleColor.Red,
        ["magenta"] = ConsoleColor.Magenta,
        ["yellow"] = ConsoleColor.Yellow,
        ["white"] = ConsoleColor.White
    };

    public static bool TryParse(string? text, out ConsoleColor color)
    {
        color = ConsoleColor.Gray;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();

        if (_aliases.TryGetValue(text, out color)) return true;

        if (Enum.TryParse<ConsoleColor>(text, true, out var enumColor)) { color = enumColor; return true; }

        if (int.TryParse(text, out int n) && n is >= 0 and <= 15) { color = (ConsoleColor)n; return true; }

        if (text.StartsWith("ansi:", StringComparison.OrdinalIgnoreCase) && int.TryParse(text.AsSpan(5), out int ansi))
        {
            color = ansi switch
            {
                30 => ConsoleColor.Black,
                31 => ConsoleColor.DarkRed,
                32 => ConsoleColor.DarkGreen,
                33 => ConsoleColor.DarkYellow,
                34 => ConsoleColor.DarkBlue,
                35 => ConsoleColor.DarkMagenta,
                36 => ConsoleColor.DarkCyan,
                37 => ConsoleColor.Gray,
                90 => ConsoleColor.DarkGray,
                91 => ConsoleColor.Red,
                92 => ConsoleColor.Green,
                93 => ConsoleColor.Yellow,
                94 => ConsoleColor.Blue,
                95 => ConsoleColor.Magenta,
                96 => ConsoleColor.Cyan,
                97 => ConsoleColor.White,
                _ => ConsoleColor.Gray
            };
            return true;
        }

        if (text[0] == '#' && TryParseHex(text, out var r1, out var g1, out var b1))
        {
            color = ClosestConsoleColor(r1, g1, b1);
            return true;
        }

        var parts = text.Split(',');
        if (parts.Length == 3 &&
            byte.TryParse(parts[0], out var r) &&
            byte.TryParse(parts[1], out var g) &&
            byte.TryParse(parts[2], out var b))
        {
            color = ClosestConsoleColor(r, g, b);
            return true;
        }

        return false;
    }

    private static bool TryParseHex(string s, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        if (s.Length == 7 &&
            byte.TryParse(s.AsSpan(1, 2), System.Globalization.NumberStyles.HexNumber, null, out r) &&
            byte.TryParse(s.AsSpan(3, 2), System.Globalization.NumberStyles.HexNumber, null, out g) &&
            byte.TryParse(s.AsSpan(5, 2), System.Globalization.NumberStyles.HexNumber, null, out b))
            return true;
        if (s.Length == 4)
        {
            bool ok =
                byte.TryParse(new string(s[1], 2), System.Globalization.NumberStyles.HexNumber, null, out r) &
                byte.TryParse(new string(s[2], 2), System.Globalization.NumberStyles.HexNumber, null, out g) &
                byte.TryParse(new string(s[3], 2), System.Globalization.NumberStyles.HexNumber, null, out b);
            return ok;
        }
        return false;
    }

    private static ConsoleColor ClosestConsoleColor(byte r, byte g, byte b)
    {
        double bestDistance = double.MaxValue;
        var best = ConsoleColor.Gray;
        foreach (var cc in Enum.GetValues<ConsoleColor>())
        {
            var (cr, cg, cb) = ConsoleColorToRgb(cc);
            var dr = cr - r; var dg = cg - g; var db = cb - b;
            var dist = dr * dr + dg * dg + db * db;
            if (dist < bestDistance) { bestDistance = dist; best = cc; }
        }
        return best;
    }

    private static (byte r, byte g, byte b) ConsoleColorToRgb(ConsoleColor c) => c switch
    {
        ConsoleColor.Black => (0, 0, 0),
        ConsoleColor.DarkBlue => (0, 0, 128),
        ConsoleColor.DarkGreen => (0, 128, 0),
        ConsoleColor.DarkCyan => (0, 128, 128),
        ConsoleColor.DarkRed => (128, 0, 0),
        ConsoleColor.DarkMagenta => (128, 0, 128),
        ConsoleColor.DarkYellow => (128, 128, 0),
        ConsoleColor.Gray => (192, 192, 192),
        ConsoleColor.DarkGray => (128, 128, 128),
        ConsoleColor.Blue => (0, 0, 255),
        ConsoleColor.Green => (0, 255, 0),
        ConsoleColor.Cyan => (0, 255, 255),
        ConsoleColor.Red => (255, 0, 0),
        ConsoleColor.Magenta => (255, 0, 255),
        ConsoleColor.Yellow => (255, 255, 0),
        ConsoleColor.White => (255, 255, 255),
        _ => (192, 192, 192)
    };
}
