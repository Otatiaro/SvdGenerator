using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SvdGenerator.Tests
{
    /// <summary>Builds small SVD documents for the tests, and runs the generator on them.</summary>
    public static class Svd
    {
        public const string Irq = "<interrupt><name>I</name><description>d</description><value>3</value></interrupt>";

        public static string Field(string name, int offset, int width, string extra = "") =>
            $"<field><name>{name}</name><description>{name} field</description><bitOffset>{offset}</bitOffset><bitWidth>{width}</bitWidth>{extra}</field>";

        public static string Register(string name, int offset, string fields = "", string? size = "32", string extra = "", string derivedFrom = "")
        {
            var sizeTag = size == null ? "" : $"<size>{size}</size>";
            var fieldsTag = fields.Length == 0 ? "" : $"<fields>{fields}</fields>";
            var derived = derivedFrom.Length == 0 ? "" : $" derivedFrom=\"{derivedFrom}\"";
            return $"<register{derived}><name>{name}</name><description>{name} register</description><addressOffset>0x{offset:X}</addressOffset>{sizeTag}{extra}{fieldsTag}</register>";
        }

        public static string Cluster(string name, int offset, string content, string extra = "") =>
            $"<cluster>{extra}<name>{name}</name><description>{name} cluster</description><addressOffset>0x{offset:X}</addressOffset>{content}</cluster>";

        public static string Peripheral(string name, long baseAddress, string registers = "", string extra = "", string derivedFrom = "")
        {
            var derived = derivedFrom.Length == 0 ? "" : $" derivedFrom=\"{derivedFrom}\"";
            var registersTag = registers.Length == 0 ? "" : $"<registers>{registers}</registers>";
            return $"<peripheral{derived}><name>{name}</name><description>{name} peripheral</description><baseAddress>0x{baseAddress:X}</baseAddress>{extra}{registersTag}</peripheral>";
        }

        public static string Device(string peripherals, string name = "DEV", string extra = "", string prolog = "") =>
            $"<?xml version=\"1.0\" encoding=\"utf-8\"?>\n{prolog}<device schemaVersion=\"1.1\"><name>{name}</name><version>1.0</version><description>d</description>{extra}" +
            $"<addressUnitBits>8</addressUnitBits><width>32</width><peripherals>{peripherals}</peripherals></device>";

        /// <summary>A register with one 1-bit field, the smallest thing that generates.</summary>
        public static string SimpleRegister(string name = "R", int offset = 0) => Register(name, offset, Field("F", 0, 1));

        public static SortedDictionary<string, string> Generate(string svd, string version = "1.2.3") =>
            Emitter.Emit(SvdReader.Parse(svd, "test.svd", "0123abcd"), version);

        /// <summary>The single peripheral header of a one-peripheral device.</summary>
        public static string GenerateOne(string peripherals, string file = "p.hpp") => Generate(Device(peripherals))[file];

        public static string[] Lines(string content) => content.Split('\n');

        /// <summary>The lines of a header that declare a register member or padding.</summary>
        public static List<string> Members(string content) =>
            Lines(content).Select(l => l.Trim()).Where(l => l.StartsWith("opsy::utility::", StringComparison.Ordinal)).ToList();
    }

    /// <summary>A directory under the system's temporary directory, removed with the test.</summary>
    public sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SvdGenerator.Tests." + Guid.NewGuid().ToString("N"));

        public TempDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public string File(string name) => System.IO.Path.Combine(Path, name);

        public string Write(string name, string content)
        {
            System.IO.File.WriteAllText(File(name), content);
            return File(name);
        }

        public List<string> Names() => Directory.EnumerateFiles(Path).Select(f => System.IO.Path.GetFileName(f)!).OrderBy(n => n, StringComparer.Ordinal).ToList();

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
