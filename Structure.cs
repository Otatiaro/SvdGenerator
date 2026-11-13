using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SvdGenerator
{
    /// <summary>
    /// Decides the shape of the generated API from what the SVD describes:
    /// which peripherals share a C++ type, what the types and their headers are
    /// called, which register names lose a redundant prefix, and which runs of
    /// numbered registers become an array.
    /// </summary>
    public static class Structure
    {
        public static List<PeripheralType> Build(Device device)
        {
            var owners = device.Peripherals.Where(p => p.Base == null).ToList();
            var shaped = owners.ToDictionary(p => p, p => DetectBlocks(Normalize(StripPrefixes(p))));

            // Peripherals with the same registers are one type. The SVD does not
            // always say so with derivedFrom: the STM32H523 file describes every
            // GPIO port and every timer in full.
            var groups = new List<List<Peripheral>>();
            var signatures = new Dictionary<Peripheral, string>();
            foreach (var owner in owners)
            {
                signatures[owner] = shaped[owner].Count == 0 ? $"empty:{owner.Name}" : Signature(shaped[owner]);
                var group = groups.FirstOrDefault(g => signatures[g[0]] == signatures[owner] && g.Any(o => AreRelated(o, owner)));
                if (group != null) group.Add(owner);
                else groups.Add(new List<Peripheral> { owner });
            }

            var drafts = groups.Select(g => new Draft(g, MeaningfulGroup(g), Family(g))).ToList();
            var used = new HashSet<string>(StringComparer.Ordinal);
            var types = new List<PeripheralType>();

            foreach (var draft in drafts)
            {
                // The group's name when the group is one type, the common part of
                // the peripherals' names otherwise; and when several types would
                // get the same name (the timers), each keeps the name of its
                // first peripheral.
                var candidate = draft.Group != null && drafts.Count(d => d.Group == draft.Group) == 1 ? draft.Group : draft.Family;
                if (drafts.Count(d => Naming.Stem(Candidate(d, drafts)) == Naming.Stem(candidate)) > 1)
                    candidate = draft.Owners[0].Name;

                var className = Naming.PeripheralClass(candidate);
                for (var n = 2; !used.Add(className); n++)
                    className = Naming.PeripheralClass($"{candidate}_{n}");

                var members = shaped[draft.Owners[0]];
                var resets = ReconcileResets(members, draft.Owners.Select(o => shaped[o]).ToList());

                var instances = device.Peripherals
                    .Where(p => draft.Owners.Contains(p) || (p.Base != null && draft.Owners.Contains(p.Base)))
                    .OrderBy(p => p.Name, NaturalComparer.Instance)
                    .ToList();

                types.Add(new PeripheralType
                {
                    ClassName = className,
                    FileKey = FileKey(draft, drafts),
                    Description = draft.Owners[0].Description,
                    Members = members,
                    Instances = instances,
                    InstanceResets = instances.ToDictionary(
                        i => i,
                        i => (IReadOnlyList<(Register, long)>)resets[draft.Owners.IndexOf(i.Base ?? i)]),
                });
            }

            return types;
        }

        sealed record Draft(List<Peripheral> Owners, string? Group, string Family);

        static string Candidate(Draft draft, List<Draft> drafts) =>
            draft.Group != null && drafts.Count(d => d.Group == draft.Group) == 1 ? draft.Group : draft.Family;

        // The header of a type: its group's, else its family's. A lone peripheral
        // named like the members of a family goes with them: on a device where
        // GPIOA to GPIOH share a type and GPIOI has its own, both are in gpio.hpp.
        static string FileKey(Draft draft, List<Draft> drafts)
        {
            if (draft.Group != null)
                return draft.Group;

            static bool IsLettered(string name, string family) =>
                name.Length == family.Length + 1 && name.StartsWith(family, StringComparison.OrdinalIgnoreCase) && char.IsAsciiLetter(name[^1]);

            if (draft.Owners.Count == 1)
            {
                var name = draft.Owners[0].Name;
                foreach (var other in drafts)
                {
                    if (other != draft && other.Group == null && other.Owners.Count >= 2
                        && IsLettered(name, other.Family) && other.Owners.All(o => IsLettered(o.Name, other.Family)))
                        return other.Family;
                }
            }

            return draft.Family;
        }

        // ── Names ──────────────────────────────────────────────────────────

        static string TrimDigits(string name) => name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9').TrimEnd('_');

        static string CommonPrefix(IEnumerable<string> names)
        {
            string? prefix = null;
            foreach (var name in names)
            {
                if (prefix == null) { prefix = name; continue; }
                var n = 0;
                while (n < prefix.Length && n < name.Length && char.ToUpperInvariant(prefix[n]) == char.ToUpperInvariant(name[n])) n++;
                prefix = prefix[..n];
            }
            return prefix ?? string.Empty;
        }

        // A groupName that only repeats the peripheral's own name groups nothing.
        static string? MeaningfulGroup(Peripheral p) =>
            string.IsNullOrEmpty(p.GroupName) || string.Equals(p.GroupName, p.Name, StringComparison.OrdinalIgnoreCase) ? null : p.GroupName;

        static string? MeaningfulGroup(List<Peripheral> owners)
        {
            var group = MeaningfulGroup(owners[0]);
            return group != null && owners.All(o => MeaningfulGroup(o) == group) ? group : null;
        }

        // What the names of the peripherals have in common: GPIO for GPIOA to
        // GPIOI, TIM for TIM2 and TIM5, USART for USART1 alone.
        static string Family(List<Peripheral> owners)
        {
            var first = TrimDigits(owners[0].Name);
            if (first.Length == 0) first = owners[0].Name;
            if (owners.Count == 1)
                return first;

            var common = TrimDigits(CommonPrefix(owners.Select(o => o.Name)));
            return common.Length < 2 ? first : common;
        }

        // Two peripherals with the same registers are the same type when the
        // SVD puts them in one group or their names show it (TIM2 and TIM5,
        // GPIOA and GPIOB). Without that, two unrelated one-register
        // peripherals that happen to look alike would be merged.
        static bool AreRelated(Peripheral a, Peripheral b)
        {
            var group = MeaningfulGroup(a);
            if (group != null && group == MeaningfulGroup(b))
                return true;

            var common = CommonPrefix(new[] { a.Name, b.Name }).Length;
            var shorter = Math.Min(a.Name.Length, b.Name.Length);
            return common >= Math.Min(3, shorter - 1) && common > 0;
        }

        /// <summary>Orders names with their numbers as numbers: uart4 before uart12.</summary>
        public sealed class NaturalComparer : IComparer<string>
        {
            public static readonly NaturalComparer Instance = new();

            public int Compare(string? x, string? y)
            {
                if (x == null || y == null) return string.CompareOrdinal(x, y);
                int i = 0, j = 0;
                while (i < x.Length && j < y.Length)
                {
                    if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
                    {
                        int si = i, sj = j;
                        while (i < x.Length && char.IsAsciiDigit(x[i])) i++;
                        while (j < y.Length && char.IsAsciiDigit(y[j])) j++;
                        var a = x[si..i].TrimStart('0');
                        var b = y[sj..j].TrimStart('0');
                        if (a.Length != b.Length) return a.Length.CompareTo(b.Length);
                        var c = string.CompareOrdinal(a, b);
                        if (c != 0) return c;
                    }
                    else
                    {
                        if (x[i] != y[j]) return x[i].CompareTo(y[j]);
                        i++; j++;
                    }
                }
                return (x.Length - i).CompareTo(y.Length - j);
            }
        }

        // ── Register names ─────────────────────────────────────────────────

        // RCC_AHB1ENR in peripheral RCC is ahb1enr: the peripheral is already
        // named by the object the register is a member of. The prefix is the
        // peripheral's name or its group's, with or without the instance number
        // (TIM1_CR1, GPIO_MODER in GPIOC, GPDMA_C0CR in GPDMA1).
        static List<Member> StripPrefixes(Peripheral peripheral)
        {
            var prefixes = new[] { peripheral.Name, peripheral.GroupName, TrimDigits(peripheral.Name), TrimDigits(peripheral.GroupName ?? string.Empty) }
                .Where(p => !string.IsNullOrEmpty(p))
                .Select(p => p + "_")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(p => p!.Length)
                .ToList();

            string Strip(string name)
            {
                foreach (var prefix in prefixes)
                {
                    if (name.Length > prefix!.Length && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !char.IsAsciiDigit(name[prefix.Length]))
                        return name[prefix.Length..];
                }
                return name;
            }

            var stripped = peripheral.Members.Select(m => Strip(m.Name)).ToList();

            // Two registers must not end up with one name.
            var clash = stripped.GroupBy(Naming.Stem).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();

            return peripheral.Members
                .Select((m, i) => clash.Contains(Naming.Stem(stripped[i])) ? m : Rename(m, stripped[i]))
                .ToList();
        }

        static Member Rename(Member member, string name) => member switch
        {
            Register r => r.With(name: name),
            Cluster c => c.With(name: name),
            _ => member,
        };

        // ── Clusters that cannot be a C++ struct ───────────────────────────

        static long AlignUp(long value, int alignment) => (value + alignment - 1) / alignment * alignment;

        // A cluster becomes a nested class. That only reproduces the hardware
        // layout when the class can sit at the cluster's address and has the
        // cluster's size; a cluster that cannot is dissolved into registers
        // named <cluster>_<register>.
        static List<Member> Normalize(IReadOnlyList<Member> members)
        {
            var result = new List<Member>();
            foreach (var member in members)
            {
                if (member is not Cluster cluster)
                {
                    result.Add(member);
                    continue;
                }

                var inner = cluster.With(members: Normalize(cluster.Members));
                if (inner.Members.Count == 0)
                    continue;

                var alignment = inner.Alignment;
                var fits = cluster.Offset % alignment == 0;
                if (inner.IsArray)
                {
                    fits = fits && inner.Stride % alignment == 0 && inner.Span <= inner.Stride;
                }
                else
                {
                    var end = cluster.Offset + inner.Span;
                    var paddedEnd = AlignUp(end, alignment);
                    fits = fits && !members.Any(m => m != member && m.Offset >= end && m.Offset < paddedEnd);
                }

                if (fits) result.Add(inner);
                else result.AddRange(Dissolve(inner));
            }
            return result;
        }

        static IEnumerable<Member> Dissolve(Cluster cluster)
        {
            for (var i = 0; i < cluster.Count; i++)
            {
                var prefix = cluster.IsArray ? $"{cluster.Name}{i}_" : $"{cluster.Name}_";
                foreach (var member in cluster.Members)
                {
                    var offset = cluster.Offset + i * cluster.ElementSize + member.Offset;
                    yield return member switch
                    {
                        Register r => r.With(name: prefix + r.Name, offset: offset),
                        Cluster c => c.With(name: prefix + c.Name, offset: offset),
                        _ => member,
                    };
                }
            }
        }

        // ── Numbered registers as an array ─────────────────────────────────

        sealed record Split(string Prefix, int Index, string Suffix);

        // Every way to read a register name as <prefix><index><suffix>, with a
        // prefix and a suffix that are not empty: C0TR1 is (C, 0, TR1).
        static IEnumerable<Split> Splits(string name)
        {
            for (var i = 1; i < name.Length; i++)
            {
                if (!char.IsAsciiDigit(name[i]) || char.IsAsciiDigit(name[i - 1]))
                    continue;
                var end = i;
                while (end < name.Length && char.IsAsciiDigit(name[end])) end++;
                if (end == name.Length || end - i > 3)
                    continue;
                var suffix = name[end..].TrimStart('_');
                var prefix = name[..i].TrimEnd('_');
                if (suffix.Length == 0 || prefix.Length == 0 || char.IsAsciiDigit(suffix[0]))
                    continue;
                yield return new Split(prefix, int.Parse(name[i..end]), suffix);
            }
        }

        // Vendors that do not use <cluster> spell a repeated block of registers
        // as numbered names: C0CR, C0SAR, C1CR, C1SAR, ... (ST's DMA channels).
        // When the blocks are numbered from 0, sit at a constant distance and
        // hold the same registers, they become an array of one class:
        // c[1].sar. A register or a field that only some of the blocks have
        // (the 2D-addressing registers of the last GPDMA channels) is given to
        // all of them, as the vendor's own C headers do.
        static List<Member> DetectBlocks(List<Member> members)
        {
            var families = new Dictionary<string, List<(Split Split, Register Register)>>(StringComparer.OrdinalIgnoreCase);
            foreach (var register in members.OfType<Register>().Where(r => !r.IsArray))
            {
                foreach (var split in Splits(register.Name))
                {
                    if (!families.TryGetValue(split.Prefix, out var list))
                        families[split.Prefix] = list = new List<(Split, Register)>();
                    list.Add((split, register));
                }
            }

            var taken = new HashSet<Register>();
            var result = new List<Member>(members);

            foreach (var family in families.Values.OrderByDescending(f => f.Count).ThenBy(f => f[0].Split.Prefix, StringComparer.Ordinal))
            {
                if (family.Any(f => taken.Contains(f.Register)))
                    continue;
                var block = TryBlock(family, result);
                if (block == null)
                    continue;

                foreach (var (_, register) in family)
                {
                    taken.Add(register);
                    result.Remove(register);
                }
                result.Add(block);
            }

            return result;
        }

        static Cluster? TryBlock(List<(Split Split, Register Register)> family, List<Member> siblings)
        {
            var prefix = family[0].Split.Prefix;
            var indices = family.Select(f => f.Split.Index).Distinct().OrderBy(i => i).ToList();
            var count = indices.Count;
            if (count < 2 || indices[0] != 0 || indices[^1] != count - 1)
                return null;

            var bySuffix = family.GroupBy(f => f.Split.Suffix, StringComparer.OrdinalIgnoreCase).ToList();
            if (bySuffix.Count < 2 || bySuffix.Any(g => g.Select(f => f.Split.Index).Distinct().Count() != g.Count()))
                return null;
            if (bySuffix.Select(g => Naming.Stem(g.Key)).Distinct().Count() != bySuffix.Count)
                return null;

            // The distance between two blocks, from a register present in two of them.
            var reference = bySuffix.Select(g => g.OrderBy(f => f.Split.Index).ToList()).FirstOrDefault(g => g.Count >= 2);
            if (reference == null)
                return null;
            var span = reference[1].Register.Offset - reference[0].Register.Offset;
            var steps = reference[1].Split.Index - reference[0].Split.Index;
            if (span <= 0 || span % steps != 0)
                return null;
            var stride = span / steps;

            // Every register must be where its index puts it.
            var relative = new List<(string Suffix, long Offset, List<Register> Registers)>();
            foreach (var group in bySuffix)
            {
                var offsets = group.Select(f => f.Register.Offset - f.Split.Index * stride).Distinct().ToList();
                if (offsets.Count != 1)
                    return null;
                relative.Add((group.Key, offsets[0], group.Select(f => f.Register).ToList()));
            }

            var start = relative.Min(r => r.Offset);
            var merged = new List<Register>();
            foreach (var (suffix, offset, registers) in relative.OrderBy(r => r.Offset))
            {
                var register = Merge(registers, suffix, offset - start);
                if (register == null)
                    return null;
                merged.Add(register);
            }

            // One block must fit in the distance to the next, without two of
            // its registers overlapping.
            for (var i = 0; i < merged.Count; i++)
            {
                var limit = i + 1 < merged.Count ? merged[i + 1].Offset : stride;
                if (merged[i].End > limit)
                    return null;
            }

            var alignment = merged.Max(r => r.Alignment);
            if (stride % alignment != 0 || start % alignment != 0)
                return null;

            // Nothing else may live inside the array, or already have its name.
            var inFamily = family.Select(f => f.Register).ToHashSet();
            var end = start + count * stride;
            foreach (var sibling in siblings)
            {
                if (sibling is Register r && inFamily.Contains(r))
                    continue;
                if (sibling.Offset < end && sibling.End > start)
                    return null;
                if (Naming.Stem(sibling.Name) == Naming.Stem(prefix))
                    return null;
            }

            return new Cluster
            {
                Name = prefix,
                Description = $"Registers {prefix}x, x = 0 to {count - 1}",
                Offset = start,
                Count = count,
                IsArray = true,
                Stride = stride,
                Members = merged,
            };
        }

        // One register standing for several of the same name: the union of
        // their fields. Null when they disagree on a size or on a field.
        static Register? Merge(List<Register> registers, string name, long offset)
        {
            var first = registers[0];
            if (registers.Any(r => r.Size != first.Size))
                return null;

            var fields = new Dictionary<string, Field>(StringComparer.Ordinal);
            foreach (var field in registers.SelectMany(r => r.Fields))
            {
                if (!fields.TryGetValue(field.Name, out var known))
                    fields[field.Name] = field;
                else if (known.Offset != field.Offset || known.Width != field.Width || known.Access != field.Access)
                    return null;
            }

            var ordered = fields.Values.OrderBy(f => f.Offset).ToList();
            for (var i = 1; i < ordered.Count; i++)
                if (ordered[i - 1].Offset + ordered[i - 1].Width > ordered[i].Offset)
                    return null;

            var access = ordered.Count == 0
                ? (registers.All(r => r.Access == first.Access) ? first.Access : Access.ReadWrite)
                : ordered.All(f => f.Access == Access.ReadOnly) ? Access.ReadOnly
                : ordered.All(f => f.Access == Access.WriteOnly) ? Access.WriteOnly
                : Access.ReadWrite;

            return new Register
            {
                Name = name,
                Description = Naming.CommonWithReplace(registers.Select(r => r.Description), "x") ?? first.Description,
                Offset = offset,
                Size = first.Size,
                Access = access,
                Reset = registers.All(r => r.Reset == first.Reset) ? first.Reset : null,
                Fields = ordered,
            };
        }

        // ── Same structure, same type ──────────────────────────────────────

        // Everything that shows in the generated class, and nothing else:
        // descriptions and reset values do not make two peripherals differ.
        static string Signature(IReadOnlyList<Member> members)
        {
            var sb = new StringBuilder();
            foreach (var member in members.OrderBy(m => m.Offset).ThenBy(m => m.Name, StringComparer.Ordinal))
            {
                switch (member)
                {
                    case Register r:
                        sb.Append($"R:{Naming.Stem(r.Name)}@{r.Offset}/{r.Size}/{r.Access}/{r.Count}[");
                        foreach (var f in r.Fields.OrderBy(f => f.Offset).ThenBy(f => f.Name, StringComparer.Ordinal))
                        {
                            sb.Append($"{Naming.Stem(f.Name)}@{f.Offset}/{f.Width}/{f.Access}(");
                            foreach (var v in f.Values)
                                sb.Append($"{v.Name}={v.Value},");
                            sb.Append(')');
                        }
                        sb.Append(']');
                        break;
                    case Cluster c:
                        sb.Append($"C:{Naming.Stem(c.Name)}@{c.Offset}/{c.Count}/{c.Stride}{{{Signature(c.Members)}}}");
                        break;
                }
                sb.Append(';');
            }
            return sb.ToString();
        }

        static void Registers(IReadOnlyList<Member> members, string path, List<(string Path, Register Register)> into)
        {
            foreach (var member in members.OrderBy(m => m.Offset).ThenBy(m => m.Name, StringComparer.Ordinal))
            {
                if (member is Register r) into.Add(($"{path}{Naming.Stem(r.Name)}", r));
                else if (member is Cluster c) Registers(c.Members, $"{path}{Naming.Stem(c.Name)}.", into);
            }
        }

        // A reset value stays on the register class when every peripheral of the
        // type agrees on it. Otherwise it leaves the class, and each peripheral
        // gets its own list (GPIOA and GPIOB do not reset alike).
        static List<List<(Register, long)>> ReconcileResets(IReadOnlyList<Member> representative, List<List<Member>> all)
        {
            var perOwner = all.Select(_ => new List<(Register, long)>()).ToList();
            if (all.Count < 2)
                return perOwner;

            var flat = all.Select(members =>
            {
                var list = new List<(string Path, Register Register)>();
                Registers(members, string.Empty, list);
                return list.ToDictionary(e => e.Path, e => e.Register);
            }).ToList();

            var target = new List<(string Path, Register Register)>();
            Registers(representative, string.Empty, target);

            foreach (var (path, register) in target)
            {
                var resets = flat.Select(f => f.TryGetValue(path, out var r) ? r.Reset : null).ToList();
                if (resets.All(r => r == resets[0]))
                    continue;

                register.Reset = null;
                if (path.Contains('.') || register.IsArray)
                    continue;
                for (var i = 0; i < all.Count; i++)
                    if (resets[i] != null)
                        perOwner[i].Add((register, resets[i]!.Value));
            }

            return perOwner;
        }
    }
}
