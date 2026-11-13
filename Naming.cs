using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SvdGenerator
{
    /// <summary>
    /// Turns SVD names, numbers and descriptions into C++ identifiers, literals and comments.
    /// </summary>
    public static class Naming
    {
        public const string OffsetType = "std::size_t";
        public const string WidthType = "std::size_t";

        // Names that must not appear unprefixed as accessors or members. Two
        // categories:
        //  - Real C++ reserved keywords. SVD authors do reuse them as field
        //    names (RP2040 has fields named INT and SIGNED, SAMD21 has INT
        //    too), and emitting `auto int() const` is a syntax error.
        //  - Identifiers the register class itself defines (value() returns
        //    the underlying integer and is part of the contract consumed by
        //    mmio::memory).
        private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
        {
            // C++ reserved keywords (C++23 set, identifier-shaped only).
            "alignas", "alignof", "and", "and_eq", "asm", "auto",
            "bitand", "bitor", "bool", "break", "case", "catch",
            "char", "char8_t", "char16_t", "char32_t", "class", "compl",
            "concept", "const", "consteval", "constexpr", "constinit",
            "const_cast", "continue", "co_await", "co_return", "co_yield",
            "decltype", "default", "delete", "do", "double", "dynamic_cast",
            "else", "enum", "explicit", "export", "extern", "false",
            "float", "for", "friend", "goto", "if", "inline",
            "int", "long", "mutable", "namespace", "new", "noexcept",
            "not", "not_eq", "nullptr", "operator", "or", "or_eq",
            "private", "protected", "public", "register", "reinterpret_cast",
            "requires", "return", "short", "signed", "sizeof", "static",
            "static_assert", "static_cast", "struct", "switch", "template",
            "this", "thread_local", "throw", "true", "try", "typedef",
            "typeid", "typename", "union", "unsigned", "using", "virtual",
            "void", "volatile", "wchar_t", "while", "xor", "xor_eq",
            // Register-class-internal identifiers we emit ourselves.
            "value",
        };

        public static bool IsKeyword(string identifier) => Keywords.Contains(identifier);

        /// <summary>
        /// Lower-case C++ identifier stem for an SVD name: anything that is not a
        /// letter, a digit or an underscore becomes an underscore, and runs of
        /// underscores collapse (a double underscore is reserved in C++).
        /// The result is meant to receive a suffix (_p, _r, _f), so it may start with a digit.
        /// </summary>
        public static string Stem(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (var c in name.ToLowerInvariant())
            {
                var valid = c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_';
                var next = valid ? c : '_';
                if (next == '_' && sb.Length > 0 && sb[^1] == '_')
                    continue;
                sb.Append(next);
            }
            return sb.Length == 0 ? "_" : sb.ToString();
        }

        /// <summary>A type name: the stem and its kind suffix, prefixed when the stem starts with a digit.</summary>
        public static string TypeName(string name, string suffix)
        {
            var stem = Stem(name);
            return char.IsAsciiDigit(stem[0]) ? $"_{stem}{suffix}" : $"{stem}{suffix}";
        }

        public static string PeripheralClass(string name) => TypeName(name, "_p");
        public static string RegisterClass(string name) => TypeName(name, "_r");
        public static string FieldClass(string name) => TypeName(name, "_f");

        /// <summary>
        /// A member, accessor or variable name: the stem, prefixed with an
        /// underscore when it would be a keyword or start with a digit.
        /// </summary>
        public static string Member(string name)
        {
            var stem = Stem(name);
            return IsKeyword(stem) || char.IsAsciiDigit(stem[0]) ? $"_{stem}" : stem;
        }

        public static string FileName(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (var c in name.ToLowerInvariant())
                sb.Append(c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' ? c : '_');
            return sb.Length == 0 ? "_.hpp" : $"{sb}.hpp";
        }

        public static string Namespace(string deviceName)
        {
            var sb = new StringBuilder(deviceName.Length);
            foreach (var c in deviceName.ToUpperInvariant())
            {
                var valid = c is (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_';
                var next = valid ? c : '_';
                if (next == '_' && sb.Length > 0 && sb[^1] == '_')
                    continue;
                sb.Append(next);
            }
            if (sb.Length == 0 || char.IsAsciiDigit(sb[0]))
                sb.Insert(0, '_');
            return sb.ToString();
        }

        public static string Irq(string name) => $"{name}_IRQn";

        /// <summary>Text of a description on one line, safe inside a block comment.</summary>
        public static string OneLine(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var sb = new StringBuilder();
            var remove = false;

            foreach (var c in text)
            {
                if (c == '\n' || c == '\r' || c == '\t' || c == ' ')
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

            return sb.ToString().Replace("*/", "* /");
        }

        /// <summary>C++ type holding a value of this many bits; bool for a single bit.</summary>
        public static string ValueType(int bits) => bits == 1 ? "bool" : IntType(bits);

        public static string IntType(int bits) =>
            bits switch
            {
                <= 8 => "uint8_t",
                <= 16 => "uint16_t",
                <= 32 => "uint32_t",
                <= 64 => "uint64_t",
                _ => throw new SvdException($"no integer type holds {bits} bits")
            };

        /// <summary>True when a value of this many bits fills its C++ type exactly.</summary>
        public static bool IsExactType(int bits) => bits == 1 || bits == 8 || bits == 16 || bits == 32 || bits == 64;

        public static string Hex(long value) => "0x" + value.ToString("X", CultureInfo.InvariantCulture);

        public static string Ones(int count) => "0b" + new string('1', count);

        public static string Binary(long value) => value == 0 ? "0" : "0b" + Convert.ToString(value, 2);

        /// <summary>Parses an SVD number: decimal, 0x hexadecimal or # binary.</summary>
        public static long Number(string text, string what)
        {
            var s = text.Trim();
            bool ok;
            long value;
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                ok = ulong.TryParse(s.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var u);
                value = unchecked((long)u);
            }
            else if (s.StartsWith('#') || s.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
            {
                var digits = s[(s[0] == '#' ? 1 : 2)..];
                ok = digits.Length is > 0 and <= 64;
                value = 0;
                foreach (var c in digits)
                {
                    if (c != '0' && c != '1')
                        ok = false;
                    value = (value << 1) | (c == '1' ? 1L : 0L);
                }
            }
            else
            {
                ok = long.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
            }

            if (!ok)
                throw new SvdException($"{what}: '{text}' is not a number");
            return value;
        }

        /// <summary>
        /// The common form of names that are identical but for one character, with
        /// that character replaced (c0cr, c1cr, c2cr give cxcr); null when the names
        /// differ in length or in more than one place.
        /// </summary>
        public static string? CommonWithReplace(IEnumerable<string> names, string replacement)
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
}
