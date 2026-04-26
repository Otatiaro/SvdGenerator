using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

public static class StringExtensions
{
    public const string OffsetType = "std::size_t";
    public const string WidthType = "std::size_t";

    // Names that must not appear unprefixed as field accessors on a
    // register class, either because they are real C++ keywords (and / or)
    // or because they would shadow a method the register class itself
    // defines (value() returns the underlying integer and is part of the
    // contract consumed by opsy::utility::memory).
    public static readonly string[] Keywords = { "and", "or", "value" };

    public static bool IsKeyword(this string str) => Keywords.Contains(str.ToLowerInvariant());

    public static string ToOneLine(this string str)
    {
        if (string.IsNullOrWhiteSpace(str))
            return string.Empty;

        var sb = new StringBuilder();
        var remove = false;

        foreach (var c in str)
        {
            if (c == '\n' || c == '\t' || c == ' ')
            {
                if (!remove)
                    sb.Append(' ');
                remove = true;
            }
            else
            {
                sb.Append(c);
                remove = false;
            }
        }

        return sb.ToString();
    }

    public static string ToIrq(this string str) => $"{str}_IRQn";
    public static string ClassName(this string str) => str.ToLowerInvariant().Replace("__", "_");

    public static string PeripheralName(this string str) => str.ClassName() + "_p";

    public static string ClassName(this fieldType field) => $"{field.name.ClassName()}_f";

    public static string ClassName(this registerType register) => $"{register.name.ClassName()}_r";

    public static string ClassName(this peripheralType peripheral) => $"{peripheral.name.ClassName()}_p";

    public static string FileName(this string str) => $"{str.ToLowerInvariant()}.hpp";

    public static string PeriphName(this peripheralType peripheral) => peripheral.name.ToLowerInvariant();

    public static string FieldName(this fieldType field) => field.name.IsKeyword() ?
            $"_{field.name.ClassName()}" :
            $"{field.name.ClassName()}";

    public static string FieldName(this registerType reg) => reg.name.IsKeyword() ?
            $"_{reg.name.ClassName()}" :
            $"{reg.name.ClassName()}";

    public static string ToType(this int size) => size == 1 ? "bool" : ToInt(size);

    public static string ToHex(this long address) => "0x" + address.ToString("X");

    public static string ToInt(this int size) =>
        size switch
        {
            <= 8 => "uint8_t",
            <= 16 => "uint16_t",
            <= 32 => "uint32_t",
            <= 64 => "uint64_t",
            _ => throw new Exception()
        };

    public static bool ExactType(this int size) => size == 1 || size == 8 || size == 16 || size == 32 || size == 64;

    public static string Range(this int size, int offset = 0)
    {
        var sb = new StringBuilder("0b", size + offset + 2);
        for (var i = 0; i < size; ++i)
            sb.Append('1');
        for (var i = 0; i < offset; ++i)
            sb.Append('0');
        return sb.ToString();
    }

    public static string ToBinary(this long value)
    {
        if (value == 0)
            return "0";

        var values = new List<bool>();

        while (value != 0)
        {
            values.Add((value & 1) != 0);
            value >>= 1;
        }

        var sb = new StringBuilder("0b");
        values.Reverse();
        foreach (var bit in values)
        {
            sb.Append(bit ? '1' : '0');
        }

        return sb.ToString();
    }

    public static long ToValue(this string str) => str.StartsWith("0x", StringComparison.InvariantCultureIgnoreCase) ? long.Parse(str[2..], NumberStyles.AllowHexSpecifier) : long.Parse(str);

    public static string ToNamespace(this device? device) => device!.name.ToUpperInvariant();

    public static string? CommonWithReplace(this IEnumerable<string> names, string replacement)
    {
        string? common = null;
        int? errorPos = null;

        foreach (var name in names)
        {
            if (common is null)
                common = name;
            else
            {
                if (name.Length != common.Length)
                    return null;

                if (errorPos is null)
                    for (var i = 0; i < common.Length; i++)
                        if (name[i] != common[i])
                            errorPos = i;

                if (errorPos is null) // still null, no difference
                    continue;

                if (common[..errorPos.Value] != name[..errorPos.Value] || common[(errorPos.Value + 1)..] != name[(errorPos.Value + 1)..])
                    return null;
            }
        }

        if (common is null) return null;
        if (errorPos is null) return common;

        return common[..errorPos.Value] + replacement + common[(errorPos.Value + 1)..];
    }
}
