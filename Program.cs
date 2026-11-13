using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SvdGenerator
{
    public static class Program
    {
        public const int ExitOk = 0;
        public const int ExitError = 1;
        public const int ExitRefused = 2;
        public const int ExitOutOfDate = 3;

        public static int Main(string[] args) => Run(args, Console.Out, Console.Error);

        public static int Run(string[] args, TextWriter output, TextWriter error)
        {
            if (args.Length == 1 && (args[0] == "--version" || args[0] == "-v"))
            {
                output.WriteLine($"SvdGenerator {InformationalVersion}");
                return ExitOk;
            }

            if (args.Any(a => a == "--help" || a == "-h"))
            {
                PrintUsage(output);
                return ExitOk;
            }

            var check = false;
            var force = false;
            var list = false;
            var positional = new List<string>();
            foreach (var arg in args)
            {
                switch (arg)
                {
                    case "--check": check = true; break;
                    case "--force": force = true; break;
                    case "--list": list = true; break;
                    default:
                        if (arg.StartsWith("--", StringComparison.Ordinal))
                        {
                            error.WriteLine($"Unknown option {arg}");
                            PrintUsage(error);
                            return ExitError;
                        }
                        positional.Add(arg);
                        break;
                }
            }

            // --list names the files without writing them, so it can do without a
            // directory; with one, it gives their paths in it.
            var expected = list ? positional.Count is 1 or 2 : positional.Count == 2;
            if (!expected || (list && (check || force)))
            {
                if (list && (check || force))
                    error.WriteLine("--list writes nothing: it does not go with --check or --force");
                PrintUsage(error);
                return ExitError;
            }

            var input = positional[0];
            var directory = positional.Count > 1 ? positional[1] : string.Empty;

            try
            {
                // Everything is generated in memory first: a file that cannot be
                // read or converted leaves the output directory as it was.
                var files = Emitter.Emit(SvdReader.Read(input), GeneratorVersion);

                if (list)
                {
                    // One per line, with forward slashes whatever the platform:
                    // the list is meant for a build system.
                    var prefix = directory.Length == 0 ? string.Empty : directory.Replace('\\', '/').TrimEnd('/') + "/";
                    foreach (var name in files.Keys)
                        output.Write($"{prefix}{name}\n");
                    return ExitOk;
                }

                if (check)
                {
                    var differences = OutputDirectory.Compare(directory, files);
                    if (differences.Count == 0)
                        return ExitOk;

                    error.WriteLine($"'{directory}' is not what SvdGenerator {GeneratorVersion} generates from {input}:");
                    foreach (var difference in differences)
                        error.WriteLine($"  {difference.File}: {difference.What}");
                    return ExitOutOfDate;
                }

                try
                {
                    OutputDirectory.Write(directory, files, force);
                }
                catch (SvdException e)
                {
                    error.WriteLine(e.Message);
                    return ExitRefused;
                }
                return ExitOk;
            }
            catch (SvdException e)
            {
                error.WriteLine(e.Message);
                return ExitError;
            }
        }

        static string InformationalVersion =>
            typeof(Program).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? "unknown";

        /// <summary>
        /// The version written in the generated files: the build's version without
        /// the source revision the SDK appends, so that two builds of one release
        /// produce the same bytes.
        /// </summary>
        public static string GeneratorVersion => InformationalVersion.Split('+')[0];

        static void PrintUsage(TextWriter w)
        {
            w.WriteLine("Usage: SvdGenerator <input.svd> <output_dir> [--check] [--force]");
            w.WriteLine("       SvdGenerator <input.svd> [<output_dir>] --list");
            w.WriteLine();
            w.WriteLine("Generates typed C++ peripheral headers for ARM Cortex-M targets");
            w.WriteLine("from a CMSIS-SVD file.");
            w.WriteLine();
            w.WriteLine("Files already in <output_dir> are replaced or removed only when");
            w.WriteLine("SvdGenerator wrote them; anything else is left alone.");
            w.WriteLine();
            w.WriteLine("Options:");
            w.WriteLine("  --check        Write nothing; exit with 3 when <output_dir> is not");
            w.WriteLine("                 what would be generated.");
            w.WriteLine("  --force        Overwrite a file of the same name that SvdGenerator");
            w.WriteLine("                 did not write.");
            w.WriteLine("  --list         Write nothing; print the files that would be generated,");
            w.WriteLine("                 one per line, for a build system that wants them as");
            w.WriteLine("                 the outputs of a rule.");
            w.WriteLine("  -h, --help     Show this help and exit.");
            w.WriteLine("  -v, --version  Show version and exit.");
            w.WriteLine();
            w.WriteLine("Exit codes: 0 success, 1 error, 2 refused to overwrite, 3 out of date.");
        }
    }
}
