using System;
using System.Collections.Generic;
using System.Linq;

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

    /// <summary>One named value of a field (CMSIS-SVD enumeratedValue).</summary>
    public sealed record EnumValue(string Name, string Description, long Value);

    public sealed class Field
    {
        public required string Name { get; init; }
        public required string Description { get; init; }
        public required int Offset { get; init; }
        public required int Width { get; init; }

        /// <summary>Access of the field: its own when the SVD states one, otherwise the register's.</summary>
        public required Access Access { get; init; }

        public IReadOnlyList<EnumValue> Values { get; init; } = Array.Empty<EnumValue>();
    }

    /// <summary>
    /// Something that occupies bytes in a peripheral or a cluster: a register or a
    /// cluster, alone or as an array.
    /// </summary>
    public abstract class Member
    {
        public required string Name { get; init; }
        public required string Description { get; init; }

        /// <summary>Byte offset from the start of the enclosing peripheral or cluster.</summary>
        public required long Offset { get; init; }

        /// <summary>Number of elements; 1 when the member is not an array.</summary>
        public int Count { get; init; } = 1;

        public bool IsArray { get; init; }

        /// <summary>Bytes of one element, padding included for an array of clusters.</summary>
        public abstract long ElementSize { get; }

        /// <summary>Alignment the C++ type of the member will have.</summary>
        public abstract int Alignment { get; }

        public long End => Offset + Count * ElementSize;
    }

    public sealed class Register : Member
    {
        /// <summary>Size in bits: 8, 16, 32 or 64.</summary>
        public required int Size { get; init; }

        /// <summary>
        /// Access of the register as a whole. With fields, it is what they have
        /// in common: read-only when none can be written, write-only when none
        /// can be read. Without fields, it is the access stated on the register,
        /// or inherited from the cluster, peripheral or device.
        /// </summary>
        public required Access Access { get; init; }

        /// <summary>Reset value; null when unknown, or when it is not the same for everything the register stands for.</summary>
        public long? Reset { get; set; }

        public required IReadOnlyList<Field> Fields { get; init; }

        public override long ElementSize => Size / 8;
        public override int Alignment => Size / 8;

        public Register With(string? name = null, long? offset = null, int? count = null, bool? isArray = null,
            string? description = null, IReadOnlyList<Field>? fields = null, Access? access = null) => new()
            {
                Name = name ?? Name,
                Description = description ?? Description,
                Offset = offset ?? Offset,
                Count = count ?? Count,
                IsArray = isArray ?? IsArray,
                Size = Size,
                Access = access ?? Access,
                Reset = Reset,
                Fields = fields ?? Fields,
            };
    }

    public sealed class Cluster : Member
    {
        public required IReadOnlyList<Member> Members { get; init; }

        /// <summary>Byte distance between two elements of an array of clusters; 0 otherwise.</summary>
        public long Stride { get; init; }

        /// <summary>Bytes from the start of the cluster to the end of its last member.</summary>
        public long Span => Members.Count == 0 ? 0 : Members.Max(m => m.End);

        public override long ElementSize => IsArray ? Stride : Span;
        public override int Alignment => Members.Count == 0 ? 1 : Members.Max(m => m.Alignment);

        public Cluster With(string? name = null, long? offset = null, IReadOnlyList<Member>? members = null) => new()
        {
            Name = name ?? Name,
            Description = Description,
            Offset = offset ?? Offset,
            Count = Count,
            IsArray = IsArray,
            Stride = Stride,
            Members = members ?? Members,
        };
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

        /// <summary>Registers and clusters as the SVD gives them; empty for a peripheral that reuses another's.</summary>
        public IReadOnlyList<Member> Members { get; set; } = Array.Empty<Member>();

        public required IReadOnlyList<Interrupt> Interrupts { get; init; }
    }

    /// <summary>
    /// A C++ peripheral class and the peripherals that are instances of it.
    /// Peripherals share a type when their registers have the same structure,
    /// whether or not the SVD derives one from the other.
    /// </summary>
    public sealed class PeripheralType
    {
        public required string ClassName { get; init; }

        /// <summary>Name of the header the type goes into, before it is made a file name.</summary>
        public required string FileKey { get; init; }

        public required string Description { get; init; }
        public required IReadOnlyList<Member> Members { get; init; }
        public required IReadOnlyList<Peripheral> Instances { get; init; }

        /// <summary>
        /// For each instance, the reset values of the top-level registers whose
        /// reset value is not the same in every instance, by register name.
        /// </summary>
        public required IReadOnlyDictionary<Peripheral, IReadOnlyList<(Register Register, long Reset)>> InstanceResets { get; init; }
    }

    public sealed class Cpu
    {
        public required string Name { get; init; }
        public required string? Revision { get; init; }
        public required long? NvicPriorityBits { get; init; }
        public required bool? HasMpu { get; init; }
        public required bool? HasFpu { get; init; }
    }

    public sealed class Device
    {
        public required string Name { get; init; }
        public required string Version { get; init; }
        public required Cpu? Cpu { get; init; }
        public required IReadOnlyList<Peripheral> Peripherals { get; init; }

        /// <summary>File name of the SVD, without its directory.</summary>
        public required string SourceFile { get; init; }

        /// <summary>SHA-256 of the SVD file, lower-case hexadecimal.</summary>
        public required string SourceSha256 { get; init; }

        /// <summary>Copyright and license notice carried by the SVD, one entry per line; empty when there is none.</summary>
        public required IReadOnlyList<string> Notice { get; init; }
    }
}
