using Cosmos.Core;
using Cosmos.Core.Memory;
using RawExecution.Drivers.ELF;
using Stellib.Drivers.Injection;
using Stellib.Str;
using System;
using System.Collections.Generic;
using System.IO;

namespace Stellib.ELF
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

    public static class ElfNameConv
    {
        // String conversion methods unchanged
        public static string ElfIS(ElfInstructionSet iset)
        {
            switch (iset)
            {
                case ElfInstructionSet.NoSpecific: return "No Specific";
                case ElfInstructionSet.Sparc: return "SPARC";
                case ElfInstructionSet.X86: return "x86";
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

    // Raw memory structure for ELF program header
    public unsafe class Elf32_Phdr
    {
        public uint Type;
        public uint Offset;
        public uint VAddr;
        public uint PAddr;
        public uint FileSize;
        public uint MemSize;
        public uint Flags;
        public uint Align;

        public static Elf32_Phdr FromPointer(byte* ptr)
        {
            Elf32_Phdr phdr = new Elf32_Phdr();
            phdr.Type = *(uint*)ptr;
            phdr.Offset = *(uint*)(ptr + 4);
            phdr.VAddr = *(uint*)(ptr + 8);
            phdr.PAddr = *(uint*)(ptr + 12);
            phdr.FileSize = *(uint*)(ptr + 16);
            phdr.MemSize = *(uint*)(ptr + 20);
            phdr.Flags = *(uint*)(ptr + 24);
            phdr.Align = *(uint*)(ptr + 28);
            return phdr;
        }
    }

    // Raw memory structure for ELF header
    public unsafe class Elf32_Ehdr
    {
        public byte[] Magic = new byte[4];
        public byte Class;
        public byte Data;
        public byte HeaderVersion;
        public byte OSABI;
        public byte[] Unused = new byte[8];
        public ushort Type;
        public ushort Machine;
        public uint ElfVersion;
        public uint Entry;
        public uint PhOff;
        public uint ShOff;
        public uint Flags;
        public ushort EhSize;
        public ushort PhEntSize;
        public ushort PhNum;
        public ushort ShEntSize;
        public ushort ShNum;
        public ushort ShStrNdx;

        public static Elf32_Ehdr FromPointer(byte* ptr)
        {
            Elf32_Ehdr ehdr = new Elf32_Ehdr();

            // Copy magic bytes
            for (int i = 0; i < 4; i++)
                ehdr.Magic[i] = ptr[i];

            ehdr.Class = ptr[4];
            ehdr.Data = ptr[5];
            ehdr.HeaderVersion = ptr[6];
            ehdr.OSABI = ptr[7];

            // Copy unused bytes
            for (int i = 0; i < 8; i++)
                ehdr.Unused[i] = ptr[8 + i];

            ehdr.Type = *(ushort*)(ptr + 16);
            ehdr.Machine = *(ushort*)(ptr + 18);
            ehdr.ElfVersion = *(uint*)(ptr + 20);
            ehdr.Entry = *(uint*)(ptr + 24);
            ehdr.PhOff = *(uint*)(ptr + 28);
            ehdr.ShOff = *(uint*)(ptr + 32);
            ehdr.Flags = *(uint*)(ptr + 36);
            ehdr.EhSize = *(ushort*)(ptr + 40);
            ehdr.PhEntSize = *(ushort*)(ptr + 42);
            ehdr.PhNum = *(ushort*)(ptr + 44);
            ehdr.ShEntSize = *(ushort*)(ptr + 46);
            ehdr.ShNum = *(ushort*)(ptr + 48);
            ehdr.ShStrNdx = *(ushort*)(ptr + 50);

            return ehdr;
        }
    }

    public unsafe class Elf32
    {
        public class ElfException : Exception
        {
            public ElfException(string message) : base(message) { }
        }

        public Elf32_Ehdr Header;
        public byte[] Magic;
        public List<Elf32_Phdr> ProgramHeaders = new List<Elf32_Phdr>();

        private string Path = "";
        private byte[] FileBytes;
        private byte* FileStart;

        // Memory tracking
        private IntPtr LoadBase = IntPtr.Zero;
        private uint TotalMemSize = 0;
        private uint EntryPointOffset = 0;

        public Elf32(string path)
        {
            Path = path;
            LoadElfFile(File.ReadAllBytes(path));
        }

        public Elf32(byte[] bytes)
        {
            LoadElfFile(bytes);
        }

        private void LoadElfFile(byte[] bytes)
        {
            FileBytes = bytes;
            fixed (byte* start = bytes)
            {
                FileStart = start;
                Header = Elf32_Ehdr.FromPointer(start);

                // Verify ELF magic
                Magic = new byte[4];
                for (int i = 0; i < 4; i++)
                    Magic[i] = Header.Magic[i];

                if (Magic[0] != 0x7F || Magic[1] != 'E' || Magic[2] != 'L' || Magic[3] != 'F')
                    throw new ElfException("Invalid ELF magic number");

                // Load program headers
                ProgramHeaders.Clear();
                for (int i = 0; i < Header.PhNum; i++)
                {
                    byte* phdrPtr = start + Header.PhOff + (i * Header.PhEntSize);
                    var phdr = Elf32_Phdr.FromPointer(phdrPtr);
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
            LoadBase = (IntPtr)Heap.Alloc(TotalMemSize);
            if (LoadBase == IntPtr.Zero)
                throw new ElfException("Failed to allocate memory for ELF");

            // Second pass: load segments into memory sequentially
            LoadSegments();

            // Execute the entry point
            ExecuteEntryPoint();
        }

        private void CalculateMemoryRequirements()
        {
            TotalMemSize = 0;

            foreach (var phdr in ProgramHeaders)
            {
                if (phdr.Type != (uint)ElfSegmentType.Load)
                    continue;

                // Apply alignment if specified
                if (phdr.Align > 1)
                {
                    uint mask = phdr.Align - 1;
                    TotalMemSize = (TotalMemSize + mask) & ~mask;
                }

                TotalMemSize += phdr.MemSize;
            }

            // Ensure we have at least some memory
            if (TotalMemSize == 0)
                throw new ElfException("No loadable segments found in ELF");
        }

        private void LoadSegments()
        {
            uint currentOffset = 0;

            foreach (var phdr in ProgramHeaders)
            {
                if (phdr.Type != (uint)ElfSegmentType.Load)
                    continue;

                // Apply alignment if specified
                if (phdr.Align > 1)
                {
                    uint mask = phdr.Align - 1;
                    currentOffset = (currentOffset + mask) & ~mask;
                }

                byte* destAddr = (byte*)LoadBase + currentOffset;

                // Copy file data
                byte* srcAddr = FileStart + phdr.Offset;
                for (uint i = 0; i < phdr.FileSize; i++)
                    destAddr[i] = srcAddr[i];

                // Zero out BSS section (difference between mem and file size)
                if (phdr.MemSize > phdr.FileSize)
                {
                    byte* bssStart = destAddr + phdr.FileSize;
                    uint bssSize = phdr.MemSize - phdr.FileSize;

                    for (uint i = 0; i < bssSize; i++)
                        bssStart[i] = 0;
                }

                // Check if entry point is in this segment
                if (Header.Entry >= phdr.VAddr && Header.Entry < phdr.VAddr + phdr.MemSize)
                {
                    EntryPointOffset = currentOffset + (Header.Entry - phdr.VAddr);
                }

                currentOffset += phdr.MemSize;
            }
        }

        private void ExecuteEntryPoint()
        {
            if (LoadBase == IntPtr.Zero)
                throw new ElfException("ELF not loaded");

            byte* entryPoint = (byte*)LoadBase + EntryPointOffset;

            // Direct function pointer definition without InteropServices
            void*** calls = CosmosCallsImpl.BuildCalls();

            try
            {
                // Raw call using function pointer
                byte* funcPtr = entryPoint;
                RawCall(funcPtr, calls);
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
                Heap.Free((byte*)LoadBase);
                LoadBase = IntPtr.Zero;
            }
        }

        // PrintElfInfo method remains largely unchanged
        public void PrintElfInfo()
        {
            // Implementation unchanged
            Console.WriteLine("ELF Header:");
            Console.WriteLine($"  Magic:                   {Magic[0]:X2} {Magic[1]:X2} {Magic[2]:X2} {Magic[3]:X2}");
            Console.WriteLine($"  Class:                   {(Header.Class == 1 ? "32-bit" : "64-bit")}");
            Console.WriteLine($"  Data:                    {(Header.Data == 1 ? "Little Endian" : "Big Endian")}");
            Console.WriteLine($"  Machine:                 {ElfNameConv.ElfIS((ElfInstructionSet)Header.Machine)}");
            Console.WriteLine($"  Entry Point:             0x{Header.Entry:X8}");
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
            if (Header.Machine != (int)ElfInstructionSet.X86)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\nWARNING: This ELF is not for x86 architecture. It will likely fail with Invalid Opcode.");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\nThis ELF is for x86 architecture and should be compatible.");
                Console.ResetColor();
            }

            Console.WriteLine($"\nTotal memory required for loading: {TotalMemSize} bytes");
        }
    }
}