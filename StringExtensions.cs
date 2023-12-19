using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

public static class StringExtensions
{
    public const string OffsetType = "std::size_t";
    public const string WidthType = "std::size_t";
    public static readonly string[] Keywords = new [] {"and", "or"};

    public static bool IsKeyword(this string str)
    {
        return Keywords.Contains(str.ToLowerInvariant());
    }

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

    public static string ToIrq(this string str)
    {
        return $"{str}_IRQn";
    }
    public static string ClassName(this string str)
    {
        return str.ToLowerInvariant().Replace("__", "_");
    }

    public static string PeripheralName(this string str)
    {
        return str.ClassName() + "_p";
    }

    public static string ClassName(this fieldType field)
    {
        return $"{field.name.ClassName()}_f";
    }

    public static string ClassName(this registerType register)
    {
        return $"{register.name.ClassName()}_r";
    }

    public static string ClassName(this peripheralType peripheral)
    {
        return $"{peripheral.name.ClassName()}_p";
    }

    public static string FileName(this string str)
    {
        return $"{str.ToLowerInvariant()}.hpp";
    }

    public static string PeriphName(this peripheralType peripheral)
    {
        return peripheral.name.ToLowerInvariant();
    }

    public static string FieldName(this fieldType field)
    {

        return field.name.IsKeyword() ? 
            $"_{field.name.ClassName()}": 
            $"{field.name.ClassName()}";
    }

    public static string FieldName(this registerType reg)
    {

        return reg.name.IsKeyword() ?
            $"_{reg.name.ClassName()}" :
            $"{reg.name.ClassName()}";
    }

    public static string ToType(this int size)
    {
        return size == 1 ? "bool" : ToInt(size);
    }

    public static string ToHex(this long address)
    {
        return "0x" + address.ToString("X");
    }

    public static string ToInt(this int size)
    {
        if (size <= 8)
            return "uint8_t";
        if (size <= 16)
            return "uint16_t";
        if (size <= 32)
            return "uint32_t";
        if (size <= 64)
            return "uint64_t";
        throw new Exception();
    }

    public static bool ExactType(this int size)
    {
        return size == 1 || size == 8 || size == 16 || size == 32 || size == 64;
    }

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

    public static long ToValue(this string str)
    {
        return str.ToLowerInvariant().StartsWith("0x") ? long.Parse(str.Substring(2), NumberStyles.AllowHexSpecifier) : long.Parse(str);
    }

    public static string ToNamespace(this device device)
    {
        return device.name.ToUpperInvariant();
    }
}