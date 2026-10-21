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
    /// ELF64 loader. Loads PT_LOAD segments into one contiguous
    /// NativeMemory block, applies the R_X86_64_RELATIVE / R_X86_64_IRELATIVE
    /// relocations a static-PIE build carries, then jumps to the entry point
    /// with the Cosmos function table (see CosmosCallsImpl).
    /// The ELF must be x86-64 (the kernel runs in long mode) and should be
    /// linked with 'gcc -static-pie' so it can execute from any base address.
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

        // Memory tracking
        private IntPtr LoadBase = IntPtr.Zero;
        private uint TotalMemSize = 0;
        private uint EntryPointOffset = 0;
        private ulong MinVaddr = ulong.MaxValue; // lowest p_vaddr of all PT_LOAD segments
        private ulong MaxVaddr = 0;              // highest p_vaddr + p_memsz

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
            // Reserve the full span the ELF expects, keyed off its linked
            // virtual addresses. Loading every segment at
            // (p_vaddr - MinVaddr) keeps every RIP-relative reference the
            // linker baked in valid, both inside a segment and across
            // segments, no matter where the block actually lands.
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

        private void ExecuteEntryPoint()
        {
            if (LoadBase == IntPtr.Zero)
                throw new ElfException("ELF not loaded");

            byte* entryPoint = (byte*)LoadBase + EntryPointOffset;

            // Build the Cosmos function table the ELF receives as first argument
            void*** calls = CosmosCallsImpl.BuildCalls();

            try
            {
                // Raw call using a function pointer. Under NativeAOT this compiles
                // to a plain indirect call following the SysV AMD64 ABI, so no
                // assembly stubs are needed.
                RawCall(entryPoint, calls);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error executing ELF: {ex.Message}");
            }
        }

        // Raw function call implementation
        private unsafe void RawCall(byte* funcPtr, void*** callList)
        {
            delegate* unmanaged[Cdecl]<void***, void> entryFunc = (delegate* unmanaged[Cdecl]<void***, void>)funcPtr;
            entryFunc(callList);
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
