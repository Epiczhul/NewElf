using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using NewElf.Drivers.ELF;

namespace NewElf.ELF
{
    public enum ElfInstructionSet
    {
        NoSpecific = 0x0,
        Sparc = 0x2,
        X86 = 0x3,
        Mips = 0x8,
        PowerPC = 0x14,
        Arm = 0x28,
        SuperH = 0x2A,
        IA64 = 0x32,
        X86_64 = 0x3E,
        AArch64 = 0xB7,
        RiscV = 0xF3
    }

    public enum ElfFlags
    {
        None = 0x0,
        Executable = 0x1,
        Writeable = 0x2,
        Readable = 0x4
    }

    public enum ElfSegmentType
    {
        Null = 0x0,
        Load = 0x1,
        Dynamic = 0x2,
        Interp = 0x3,
        Note = 0x4,
        Shlib = 0x5,
        Phdr = 0x6,
        Other = 0x7
    }

    // x86-64 relocation types (SysV AMD64 ABI)
    internal enum ElfX86_64RelocType
    {
        R_X86_64_NONE = 0,
        R_X86_64_RELATIVE = 8,
        R_X86_64_IRELATIVE = 37
    }

    public static class ElfNameConv
    {
        public static string ElfIS(ElfInstructionSet iset)
        {
            switch (iset)
            {
                case ElfInstructionSet.NoSpecific: return "No Specific";
                case ElfInstructionSet.Sparc: return "SPARC";
                case ElfInstructionSet.X86: return "x86 (32 bit, not executable here)";
                case ElfInstructionSet.Mips: return "MIPS";
                case ElfInstructionSet.PowerPC: return "PowerPC";
                case ElfInstructionSet.Arm: return "ARM";
                case ElfInstructionSet.SuperH: return "SuperH";
                case ElfInstructionSet.IA64: return "IA-64";
                case ElfInstructionSet.X86_64: return "x86-64";
                case ElfInstructionSet.AArch64: return "AArch64";
                case ElfInstructionSet.RiscV: return "RISC-V";
                default: return "Unknown";
            }
        }

        public static string ElfF(ElfFlags flags)
        {
            string result = "";
            if (flags == ElfFlags.None) return "None";
            if ((flags & ElfFlags.Executable) != 0) result += "Executable ";
            if ((flags & ElfFlags.Writeable) != 0) result += "Writeable ";
            if ((flags & ElfFlags.Readable) != 0) result += "Readable ";
            return result.Trim();
        }

        public static string ElfST(ElfSegmentType type)
        {
            switch (type)
            {
                case ElfSegmentType.Null: return "Null";
                case ElfSegmentType.Load: return "Load";
                case ElfSegmentType.Dynamic: return "Dynamic";
                case ElfSegmentType.Interp: return "Interp";
                case ElfSegmentType.Note: return "Note";
                case ElfSegmentType.Shlib: return "Shlib";
                case ElfSegmentType.Phdr: return "Phdr";
                case ElfSegmentType.Other: return "Other";
                default: return "Unknown";
            }
        }
    }

    // Raw memory structure for the ELF64 program header
    public unsafe class Elf64_Phdr
    {
        public uint Type;            // p_type
        public uint Flags;           // p_flags
        public ulong Offset;         // p_offset
        public ulong VAddr;          // p_vaddr (as linked)
        public ulong PAddr;          // p_paddr
        public ulong FileSize;       // p_filesz
        public ulong MemSize;        // p_memsz
        public ulong Align;          // p_align
        public ulong LoadOffset;     // offset inside the allocated block where this segment was loaded

        public static Elf64_Phdr FromPointer(byte* ptr)
        {
            Elf64_Phdr phdr = new Elf64_Phdr();
            phdr.Type = *(uint*)ptr;                  // 0x00
            phdr.Flags = *(uint*)(ptr + 4);           // 0x04
            phdr.Offset = *(ulong*)(ptr + 8);         // 0x08
            phdr.VAddr = *(ulong*)(ptr + 16);         // 0x10
            phdr.PAddr = *(ulong*)(ptr + 24);         // 0x18
            phdr.FileSize = *(ulong*)(ptr + 32);      // 0x20
            phdr.MemSize = *(ulong*)(ptr + 40);       // 0x28
            phdr.Align = *(ulong*)(ptr + 48);         // 0x30
            return phdr;
        }
    }

    // Raw memory structure for the ELF64 header
    public unsafe class Elf64_Ehdr
    {
        public byte[] Magic = new byte[4];
        public byte Class;
        public byte Data;
        public byte HeaderVersion;
        public byte OSABI;
        public byte[] Unused = new byte[8];
        public ushort Type;          // e_type
        public ushort Machine;       // e_machine
        public uint ElfVersion;      // e_version
        public ulong Entry;          // e_entry
        public ulong PhOff;          // e_phoff
        public ulong ShOff;          // e_shoff
        public uint Flags;           // e_flags
        public ushort EhSize;        // e_ehsize
        public ushort PhEntSize;     // e_phentsize
        public ushort PhNum;         // e_phnum
        public ushort ShEntSize;     // e_shentsize
        public ushort ShNum;         // e_shnum
        public ushort ShStrNdx;      // e_shstrndx

        public static Elf64_Ehdr FromPointer(byte* ptr)
        {
            Elf64_Ehdr ehdr = new Elf64_Ehdr();

            for (int i = 0; i < 4; i++)
                ehdr.Magic[i] = ptr[i];

            ehdr.Class = ptr[4];
            ehdr.Data = ptr[5];
            ehdr.HeaderVersion = ptr[6];
            ehdr.OSABI = ptr[7];

            for (int i = 0; i < 8; i++)
                ehdr.Unused[i] = ptr[8 + i];

            ehdr.Type = *(ushort*)(ptr + 16);
            ehdr.Machine = *(ushort*)(ptr + 18);
            ehdr.ElfVersion = *(uint*)(ptr + 20);
            ehdr.Entry = *(ulong*)(ptr + 24);
            ehdr.PhOff = *(ulong*)(ptr + 32);
            ehdr.ShOff = *(ulong*)(ptr + 40);
            ehdr.Flags = *(uint*)(ptr + 48);
            ehdr.EhSize = *(ushort*)(ptr + 52);
            ehdr.PhEntSize = *(ushort*)(ptr + 54);
            ehdr.PhNum = *(ushort*)(ptr + 56);
            ehdr.ShEntSize = *(ushort*)(ptr + 58);
            ehdr.ShNum = *(ushort*)(ptr + 60);
            ehdr.ShStrNdx = *(ushort*)(ptr + 62);

            return ehdr;
        }
    }

    /// <summary>
    /// ELF64 loader. Copies the PT_LOAD segments into one NativeMemory block,
    /// applies the relocations of a static PIE build and enters the entry
    /// point with the function table in RDI and a SysV process start block on
    /// its stack. The run ends through the exit call or a caught CPU fault.
    /// The ELF has to be x86-64 and linked with 'gcc -static-pie'.
    /// </summary>
    public unsafe class Elf64
    {
        public class ElfException : Exception
        {
            public ElfException(string message) : base(message) { }
        }

        public Elf64_Ehdr Header = null!;
        public byte[] Magic = null!;
        public List<Elf64_Phdr> ProgramHeaders = new List<Elf64_Phdr>();

        private string Path = "";
        private byte* FileStart;
        private byte[] FileBytes = null!;

        // loaded block and its size
        private IntPtr LoadBase = IntPtr.Zero;
        private uint TotalMemSize = 0;
        private uint EntryPointOffset = 0;
        private ulong MinVaddr = ulong.MaxValue; // lowest p_vaddr of all PT_LOAD segments
        private ulong MaxVaddr = 0;              // highest p_vaddr + p_memsz

        // stack block for the ELF, freed after the run
        private IntPtr StackBlock = IntPtr.Zero;

        /// <summary>
        /// Exit code of the last run, or 128 + vector if a fault killed the ELF.
        /// </summary>
        public int ExitCode { get; private set; } = -1;

        // SysV auxiliary vector tags
        private const ulong AT_NULL = 0;
        private const ulong AT_PHDR = 3;
        private const ulong AT_PHENT = 4;
        private const ulong AT_PHNUM = 5;
        private const ulong AT_PAGESZ = 6;
        private const ulong AT_BASE = 7;
        private const ulong AT_FLAGS = 8;
        private const ulong AT_ENTRY = 9;
        private const ulong AT_UID = 11;
        private const ulong AT_EUID = 12;
        private const ulong AT_GID = 13;
        private const ulong AT_EGID = 14;
        private const ulong AT_HWCAP = 16;
        private const ulong AT_CLKTCK = 17;
        private const ulong AT_SECURE = 23;
        private const ulong AT_RANDOM = 25;
        private const ulong AT_EXECFN = 31;
        private const ulong AT_PLATFORM = 15;

        // stack handed to the ELF, 64 KiB is plenty for a small C runtime
        private const int StartupStackSize = 64 * 1024;

        public Elf64(string path)
        {
            Path = path;
            LoadElfFile(File.ReadAllBytes(path));
        }

        public Elf64(byte[] bytes)
        {
            LoadElfFile(bytes);
        }

        private void LoadElfFile(byte[] bytes)
        {
            FileBytes = bytes;
            fixed (byte* start = bytes)
            {
                FileStart = start;
                Header = Elf64_Ehdr.FromPointer(start);

                // Verify ELF magic
                Magic = new byte[4];
                for (int i = 0; i < 4; i++)
                    Magic[i] = Header.Magic[i];

                if (Magic[0] != 0x7F || Magic[1] != 'E' || Magic[2] != 'L' || Magic[3] != 'F')
                    throw new ElfException("Invalid ELF magic number");

                if (Header.Class != 2)
                    throw new ElfException("Not a 64-bit ELF. The kernel runs in long mode: rebuild the binary as x86-64 (gcc -m64 -static-pie).");

                if ((ElfInstructionSet)Header.Machine != ElfInstructionSet.X86_64)
                    throw new ElfException("ELF is not for x86-64.");

                // Load program headers
                ProgramHeaders.Clear();
                for (int i = 0; i < Header.PhNum; i++)
                {
                    byte* phdrPtr = start + Header.PhOff + (i * Header.PhEntSize);
                    var phdr = Elf64_Phdr.FromPointer(phdrPtr);
                    ProgramHeaders.Add(phdr);
                }
            }
        }

        public void LoadAndExecuteElf()
        {
            if (LoadBase != IntPtr.Zero)
                FreeElf(); // Clean up previous allocation if any

            // First pass: calculate total memory needed
            CalculateMemoryRequirements();

            // Allocate memory in one contiguous block
            LoadBase = (IntPtr)NativeMemory.Alloc((nuint)TotalMemSize);
            if (LoadBase == IntPtr.Zero)
                throw new ElfException("Failed to allocate memory for ELF");

            // Second pass: load segments into memory sequentially
            LoadSegments();

            // Apply the relocations a static-PIE build carries
            ApplyRelocations();

            // Execute the entry point
            ExecuteEntryPoint();
        }

        private void CalculateMemoryRequirements()
        {
            // Reserve the whole address range of the ELF and place every segment at
            // (p_vaddr - MinVaddr), so RIP-relative references between segments
            // still work wherever the block ends up.
            MinVaddr = ulong.MaxValue;
            MaxVaddr = 0;

            foreach (var phdr in ProgramHeaders)
            {
                if (phdr.Type != (uint)ElfSegmentType.Load)
                    continue;

                if (phdr.VAddr < MinVaddr)
                    MinVaddr = phdr.VAddr;

                ulong end = phdr.VAddr + phdr.MemSize;
                if (end > MaxVaddr)
                    MaxVaddr = end;
            }

            // Ensure we have at least some memory
            if (MinVaddr == ulong.MaxValue || MaxVaddr <= MinVaddr)
                throw new ElfException("No loadable segments found in ELF");

            TotalMemSize = (uint)(MaxVaddr - MinVaddr);
        }

        private void LoadSegments()
        {
            foreach (var phdr in ProgramHeaders)
            {
                if (phdr.Type != (uint)ElfSegmentType.Load)
                    continue;

                phdr.LoadOffset = (uint)(phdr.VAddr - MinVaddr);
                byte* destAddr = (byte*)LoadBase + phdr.LoadOffset;

                // Copy file data
                byte* srcAddr = FileStart + phdr.Offset;
                for (ulong i = 0; i < phdr.FileSize; i++)
                    destAddr[i] = srcAddr[i];

                // Zero out BSS section (difference between mem and file size)
                if (phdr.MemSize > phdr.FileSize)
                {
                    byte* bssStart = destAddr + phdr.FileSize;
                    ulong bssSize = phdr.MemSize - phdr.FileSize;

                    for (ulong i = 0; i < bssSize; i++)
                        bssStart[i] = 0;
                }

                // Check if entry point is in this segment
                if (Header.Entry >= phdr.VAddr && Header.Entry < phdr.VAddr + phdr.MemSize)
                {
                    EntryPointOffset = (uint)(phdr.LoadOffset + (Header.Entry - phdr.VAddr));
                }
            }
        }

        /// <summary>
        /// Translates a virtual address (as found in relocation offsets, the
        /// dynamic table, ...) into an offset inside the loaded block.
        /// </summary>
        private long VaddrToOffset(ulong vaddr)
        {
            if (vaddr >= MinVaddr && vaddr < MaxVaddr)
                return (long)(vaddr - MinVaddr);
            return -1;
        }

        /// <summary>
        /// Walks PT_DYNAMIC and applies R_X86_64_RELATIVE and R_X86_64_IRELATIVE
        /// relocations, baking the actual load base into every absolute address.
        /// This is what makes a 'gcc -static-pie' binary run from heap memory.
        /// </summary>
        private void ApplyRelocations()
        {
            Elf64_Phdr? dynamic = null;
            foreach (var phdr in ProgramHeaders)
            {
                if (phdr.Type == (uint)ElfSegmentType.Dynamic)
                {
                    dynamic = phdr;
                    break;
                }
            }

            if (dynamic == null)
                return; // no dynamic segment: nothing to relocate

            byte* dynStart = FileStart + dynamic.Offset;
            ulong dynCount = dynamic.FileSize / 16; // Elf64_Dyn is {long tag; ulong val} = 16 bytes

            ulong relaAddr = ulong.MaxValue; // DT_RELA: vaddr of .rela.dyn
            ulong relaSize = 0;              // DT_RELASZ: total size in bytes
            ulong relaEnt = 24;              // DT_RELAENT: entry size

            for (ulong i = 0; i < dynCount; i++)
            {
                long tag = *(long*)(dynStart + i * 16);
                ulong val = *(ulong*)(dynStart + i * 16 + 8);

                if (tag == 0) break; // DT_NULL

                switch (tag)
                {
                    case 7: relaAddr = val; break;   // DT_RELA
                    case 8: relaSize = val; break;   // DT_RELASZ
                    case 9: relaEnt = val; break;    // DT_RELAENT
                }
            }

            if (relaAddr == ulong.MaxValue || relaSize == 0)
                return;

            if (relaEnt != 24)
                throw new ElfException($"Unsupported DT_RELAENT {relaEnt} (expected 24)");

            long relaOffset = VaddrToOffset(relaAddr);
            if (relaOffset < 0)
                throw new ElfException("DT_RELA points outside of the loaded segments");

            byte* relaStart = FileStart + relaOffset;
            ulong entryCount = relaSize / 24; // Elf64_Rela {ulong r_offset; ulong r_info; long r_addend} = 24 bytes

            for (ulong i = 0; i < entryCount; i++)
            {
                ulong rOffset = *(ulong*)(relaStart + i * 24);
                ulong rInfo = *(ulong*)(relaStart + i * 24 + 8);
                long rAddend = *(long*)(relaStart + i * 24 + 16);

                var type = (ElfX86_64RelocType)(rInfo & 0xFFFFFFFF);
                long targetOffset = VaddrToOffset(rOffset);
                if (targetOffset < 0)
                    continue; // relocation into a segment we did not load

                byte* target = (byte*)LoadBase + targetOffset;

                switch (type)
                {
                    case ElfX86_64RelocType.R_X86_64_RELATIVE:
                        // *(base + r_offset) = base + r_addend
                        *(ulong*)target = (ulong)((byte*)LoadBase + rAddend);
                        break;

                    case ElfX86_64RelocType.R_X86_64_IRELATIVE:
                        // call the resolver function, store its result
                        delegate* unmanaged[Cdecl]<ulong> resolver = (delegate* unmanaged[Cdecl]<ulong>)((byte*)LoadBase + rAddend);
                        *(ulong*)target = resolver();
                        break;

                    case ElfX86_64RelocType.R_X86_64_NONE:
                        break;

                    default:
                        throw new ElfException($"Unsupported relocation type {(uint)type} at +0x{rOffset:X}. Link the ELF with 'gcc -static-pie' so it only needs RELATIVE relocations.");
                }
            }
        }

        /// <summary>
        /// Builds the start block and runs the ELF through <see cref="FaultManager"/>.
        /// <see cref="ExitCode"/> is set when it comes back.
        /// </summary>
        private void ExecuteEntryPoint()
        {
            if (LoadBase == IntPtr.Zero)
                throw new ElfException("ELF not loaded");

            byte* entryPoint = (byte*)LoadBase + EntryPointOffset;

            // Build the Cosmos function table. The ELF gets it in RDI (the
            // first SysV argument register), the process start block waits
            // on the stack its entry point finds at RSP.
            void*** calls = CosmosCallsImpl.BuildCalls();

            byte* stackTop = BuildStartupStack();

            // A GC during the run would walk the ELF stack and miss the
            // references held by the frozen kernel frames, so pause it.
            // The 64 MiB budget has to cover everything the ELF allocates.
            bool noGc = GC.TryStartNoGCRegion(64 * 1024 * 1024);
            if (!noGc)
                Console.WriteLine("NewElf: warning, could not pause the GC for the ELF run.");

            try
            {
                FaultManager.BeginElf((ulong)LoadBase, (ulong)LoadBase + TotalMemSize);
                ExitCode = FaultManager.Run(calls, entryPoint, stackTop);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error executing ELF: {ex.Message}");
            }
            finally
            {
                FaultManager.EndElf();

                if (StackBlock != IntPtr.Zero)
                {
                    NativeMemory.Free((void*)StackBlock);
                    StackBlock = IntPtr.Zero;
                }

                if (noGc)
                {
                    // the region can end on its own when the budget runs out
                    try { GC.EndNoGCRegion(); }
                    catch (InvalidOperationException) { }
                }
            }
        }

        /// <summary>
        /// Builds the SysV process start block the ELF finds on its stack:
        /// argc, argv, NULL, envp (just a NULL), the auxiliary vector and the
        /// strings they point at. A static PIE reads AT_PHDR and friends
        /// from here, so the layout has to match Linux.
        /// </summary>
        private byte* BuildStartupStack()
        {
            byte* stack = (byte*)NativeMemory.Alloc(StartupStackSize);
            StackBlock = (IntPtr)stack;

            string[] args = CosmosCallsImpl.ProgramArgs;
            if (args.Length == 0)
                args = new[] { Path };

            byte* cur = stack + StartupStackSize;

            // 16 bytes of randomness for AT_RANDOM, stack canary seed and such
            cur -= 16;
            cur = AlignDown(cur, 16);
            byte* randomPtr = cur;
            byte[] randomBytes = new byte[16];
            Random.Shared.NextBytes(randomBytes);
            for (int i = 0; i < 16; i++)
                randomPtr[i] = randomBytes[i];

            byte* platformPtr = WriteAsciiDown(ref cur, "x86_64");
            byte* execFnPtr = WriteAsciiDown(ref cur, Path);

            byte** argPtrs = stackalloc byte*[args.Length];
            for (int i = 0; i < args.Length; i++)
                argPtrs[i] = WriteAsciiDown(ref cur, args[i]);

            ulong[] types =
            {
                AT_PHDR, AT_PHENT, AT_PHNUM, AT_PAGESZ, AT_BASE, AT_FLAGS, AT_ENTRY,
                AT_UID, AT_EUID, AT_GID, AT_EGID, AT_HWCAP, AT_CLKTCK, AT_SECURE,
                AT_PLATFORM, AT_RANDOM, AT_EXECFN, AT_NULL
            };
            ulong[] values =
            {
                ProgramHeaderAddress(), Header.PhEntSize, Header.PhNum, 4096, 0, 0, EntryAddress(),
                0, 0, 0, 0, 0, 100, 0,
                (ulong)platformPtr, (ulong)randomPtr, (ulong)execFnPtr, AT_NULL
            };

            // argc, argv + NULL, envp NULL and one 16 byte pair per auxv entry
            ulong metaSize = 8 + 8 * ((ulong)args.Length + 1) + 8 + 16 * (ulong)types.Length;

            byte* blockEnd = AlignDown(cur, 8);
            byte* blockStart = blockEnd - (long)metaSize;
            // push the whole block down until argc sits on a 16 byte boundary
            blockEnd -= (long)((ulong)blockStart & 0xF);
            blockStart = blockEnd - (long)metaSize;

            byte* p = blockEnd;
            for (int i = types.Length - 1; i >= 0; i--)
            {
                p -= 16;
                *(ulong*)p = types[i];
                *(ulong*)(p + 8) = values[i];
            }

            // envp: a single NULL, the kernel does not pass an environment
            p -= 8;
            *(ulong*)p = 0;

            // argv: NULL terminator first (it sits above the pointers), then
            // the pointers, then argc on top. Written top down, read bottom up.
            p -= 8;
            *(ulong*)p = 0;
            for (int i = args.Length - 1; i >= 0; i--)
            {
                p -= 8;
                *(byte**)p = argPtrs[i];
            }
            p -= 8;
            *(ulong*)p = (ulong)args.Length;

            return p;
        }

        /// <summary>
        /// Writes an ASCII copy of a string below the current cursor and
        /// returns the address of the NUL terminated copy.
        /// </summary>
        private byte* WriteAsciiDown(ref byte* cur, string s)
        {
            cur -= s.Length + 1;
            for (int i = 0; i < s.Length; i++)
                cur[i] = (byte)s[i];
            cur[s.Length] = 0;
            return cur;
        }

        private static byte* AlignDown(byte* p, ulong alignment)
            => (byte*)((ulong)p & ~(alignment - 1));

        /// <summary>
        /// Address of the program header table inside the loaded image. The
        /// table normally sits in the first PT_LOAD segment (file offset 0),
        /// so its address is that segment base plus the offset difference.
        /// </summary>
        private ulong ProgramHeaderAddress()
        {
            foreach (var phdr in ProgramHeaders)
            {
                if (phdr.Type != (uint)ElfSegmentType.Load)
                    continue;

                if (Header.PhOff >= phdr.Offset && Header.PhOff < phdr.Offset + phdr.FileSize)
                    return (ulong)LoadBase + phdr.LoadOffset + (Header.PhOff - phdr.Offset);
            }

            // not inside any loaded segment: fall back to the flat layout
            return (ulong)LoadBase + Header.PhOff;
        }

        /// <summary>
        /// Actual address of the entry point inside the loaded image, the
        /// AT_ENTRY value a static PIE cross checks against its own phdrs.
        /// </summary>
        private ulong EntryAddress()
        {
            if (Header.Entry >= MinVaddr && Header.Entry < MaxVaddr)
                return (ulong)LoadBase + (Header.Entry - MinVaddr);

            return (ulong)LoadBase + EntryPointOffset;
        }

        public void ReloadElf()
        {
            if (string.IsNullOrEmpty(Path))
                throw new ElfException("Path is not set. Cannot reload ELF.");

            if (!File.Exists(Path))
                throw new ElfException("File does not exist.");

            // Free current allocation
            FreeElf();

            // Reload and execute
            LoadElfFile(File.ReadAllBytes(Path));
            LoadAndExecuteElf();
        }

        public void FreeElf()
        {
            if (LoadBase != IntPtr.Zero)
            {
                NativeMemory.Free((void*)LoadBase);
                LoadBase = IntPtr.Zero;
            }
        }

        public void PrintElfInfo()
        {
            Console.WriteLine("ELF64 Header:");
            Console.WriteLine($"  Magic:                   {Magic[0]:X2} {Magic[1]:X2} {Magic[2]:X2} {Magic[3]:X2}");
            Console.WriteLine($"  Class:                   {(Header.Class == 1 ? "32-bit" : "64-bit")}");
            Console.WriteLine($"  Data:                    {(Header.Data == 1 ? "Little Endian" : "Big Endian")}");
            Console.WriteLine($"  Machine:                 {ElfNameConv.ElfIS((ElfInstructionSet)Header.Machine)}");
            Console.WriteLine($"  Entry Point:             0x{Header.Entry:X16}");
            Console.WriteLine($"  Program Header Count:    {Header.PhNum}");

            Console.WriteLine("\nProgram Headers:");
            Console.WriteLine("  Type      Offset    VAddr     PAddr     FileSize  MemSize   Flags     Align");
            Console.WriteLine("  --------- --------- --------- --------- --------- --------- --------- ---------");

            foreach (var phdr in ProgramHeaders)
            {
                Console.WriteLine(
                    $"  {ElfNameConv.ElfST((ElfSegmentType)phdr.Type),-9} " +
                    $"0x{phdr.Offset:X8} " +
                    $"0x{phdr.VAddr:X8} " +
                    $"0x{phdr.PAddr:X8} " +
                    $"0x{phdr.FileSize:X8} " +
                    $"0x{phdr.MemSize:X8} " +
                    $"{ElfNameConv.ElfF((ElfFlags)phdr.Flags),-9} " +
                    $"0x{phdr.Align:X8}"
                );
            }

            // Architecture compatibility check
            if (Header.Machine != (int)ElfInstructionSet.X86_64)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\nWARNING: This ELF is not for x86-64. It will likely fail with Invalid Opcode.");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\nThis ELF is for x86-64 and should be compatible.");
                Console.ResetColor();
            }

            Console.WriteLine($"\nTotal memory required for loading: {TotalMemSize} bytes");
        }
    }
}
