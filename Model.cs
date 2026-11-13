using System;
using System.Collections.Generic;

namespace SvdGenerator
{
    /// <summary>
    /// An error in the SVD file or in the way the generator was invoked, reported
    /// to the user as a one-line message rather than as a stack trace.
    /// </summary>
    public sealed class SvdException : Exception
    {
        public SvdException(string message) : base(message) { }
    }

    public enum Access
    {
        ReadWrite,
        ReadOnly,
        WriteOnly,
    }

    public sealed class Field
    {
        public required string Name { get; init; }
        public required string Description { get; init; }
        public required int Offset { get; init; }
        public required int Width { get; init; }

        /// <summary>Access stated on the field itself; null when the SVD leaves it to the register.</summary>
        public required Access? DeclaredAccess { get; init; }
    }

    public sealed class Register
    {
        public required string Name { get; init; }
        public required string Description { get; init; }

        /// <summary>Byte offset from the peripheral base address.</summary>
        public required long Offset { get; init; }

        /// <summary>Size in bits: 8, 16, 32 or 64.</summary>
        public required int Size { get; init; }

        /// <summary>Access of the register, inherited from the cluster, peripheral or device when not stated.</summary>
        public required Access Access { get; init; }

        /// <summary>Reset value as written in the SVD (on the register or the one it derives from).</summary>
        public required string? ResetText { get; init; }

        public required IReadOnlyList<Field> Fields { get; init; }

        public long End => Offset + Size / 8;
    }

    public sealed class Interrupt
    {
        public required string Name { get; init; }
        public required string Description { get; init; }
        public required long Value { get; init; }
    }

    public sealed class Peripheral
    {
        public required string Name { get; init; }
        public required string Description { get; init; }
        public required string? GroupName { get; init; }
        public required long BaseAddress { get; init; }

        /// <summary>The peripheral whose registers this one reuses; null when it defines its own.</summary>
        public Peripheral? Base { get; set; }

        public IReadOnlyList<Register> Registers { get; set; } = Array.Empty<Register>();
        public required IReadOnlyList<Interrupt> Interrupts { get; init; }
    }

    public sealed class Device
    {
        public required string Name { get; init; }
        public required string Version { get; init; }
        public required IReadOnlyList<Peripheral> Peripherals { get; init; }

        /// <summary>File name of the SVD, without its directory.</summary>
        public required string SourceFile { get; init; }

        /// <summary>SHA-256 of the SVD file, lower-case hexadecimal.</summary>
        public required string SourceSha256 { get; init; }

        /// <summary>Copyright and license notice carried by the SVD, one entry per line; empty when there is none.</summary>
        public required IReadOnlyList<string> Notice { get; init; }
    }
}
