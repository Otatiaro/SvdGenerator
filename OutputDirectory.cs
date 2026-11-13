using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SvdGenerator
{
    /// <summary>
    /// Brings a directory in line with a set of generated files, or checks that it
    /// already is. Only files the generator wrote are ever replaced or removed:
    /// a memory.hpp, a CMakeLists.txt or a linker script sitting next to the
    /// headers are left alone.
    /// </summary>
    public static class OutputDirectory
    {
        public sealed record Difference(string File, string What);

        /// <summary>
        /// Lists what separates the directory from the generated files; empty when
        /// it is up to date. Line endings are not compared, so a checkout that
        /// converted them still matches.
        /// </summary>
        public static List<Difference> Compare(string directory, IReadOnlyDictionary<string, string> files)
        {
            var differences = new List<Difference>();
            var existing = Existing(directory);

            foreach (var (name, content) in files)
            {
                if (!existing.TryGetValue(name, out var path))
                    differences.Add(new Difference(name, "missing"));
                else if (Read(path) != content)
                    differences.Add(new Difference(name, "differs"));
            }

            foreach (var (name, path) in existing)
            {
                if (!files.ContainsKey(name) && IsGenerated(name, Read(path)))
                    differences.Add(new Difference(name, "no longer generated"));
            }

            return differences.OrderBy(d => d.File, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// Writes the generated files and removes the generated files that are no
        /// longer produced. Everything is checked before anything is touched.
        /// </summary>
        /// <exception cref="SvdException">A file to write exists and was not generated (unless forced).</exception>
        public static void Write(string directory, IReadOnlyDictionary<string, string> files, bool force)
        {
            var existing = Existing(directory);
            var stale = new List<string>();
            var unchanged = new HashSet<string>(StringComparer.Ordinal);

            foreach (var (name, path) in existing)
            {
                var content = Read(path);
                if (files.TryGetValue(name, out var generated))
                {
                    if (!force && !IsGenerated(name, content))
                        throw new SvdException($"Refusing to overwrite '{path}': it was not written by SvdGenerator. Move it away, or pass --force.");
                    if (content == generated && !File.ReadAllText(path).Contains('\r'))
                        unchanged.Add(name);
                }
                else if (IsGenerated(name, content))
                {
                    stale.Add(path);
                }
            }

            Directory.CreateDirectory(directory);
            foreach (var path in stale)
                File.Delete(path);

            // An up-to-date file is not rewritten: its timestamp stays put and
            // the build system does not recompile what includes it.
            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            foreach (var (name, content) in files)
            {
                if (!unchanged.Contains(name))
                    File.WriteAllText(Path.Combine(directory, name), content, encoding);
            }
        }

        // Files of the directory by name. File systems that ignore case are
        // handled by the caller always using the generated spelling.
        static Dictionary<string, string> Existing(string directory)
        {
            var existing = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!Directory.Exists(directory))
                return existing;
            foreach (var path in Directory.EnumerateFiles(directory))
                existing[Path.GetFileName(path)] = path;
            return existing;
        }

        static string Read(string path) => File.ReadAllText(path).Replace("\r\n", "\n");

        /// <summary>
        /// True for a file this generator wrote: it starts with the banner, or it
        /// has the shape of the headers written before the banner existed.
        /// </summary>
        public static bool IsGenerated(string name, string content)
        {
            if (name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                return content.StartsWith(Emitter.MarkdownBannerStart, StringComparison.Ordinal);
            if (!name.EndsWith(".hpp", StringComparison.OrdinalIgnoreCase))
                return false;
            if (content.StartsWith(Emitter.BannerStart, StringComparison.Ordinal))
                return true;

            const string legacyPeripheral = "#pragma once\n\n#if __has_include(\"memory.hpp\")\n#include \"memory.hpp\"\n#else\n#include <utility/memory.hpp>\n#endif\n\nnamespace ";
            const string legacyInterrupts = "#pragma once\n\n#include <cstddef>\n\nconstexpr std::size_t irq_count = ";
            return content.StartsWith(legacyPeripheral, StringComparison.Ordinal)
                || content.StartsWith(legacyInterrupts, StringComparison.Ordinal);
        }
    }
}
