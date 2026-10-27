using System;
using System.IO;
using Cosmos.Kernel.HAL.Devices.Storage;
using Cosmos.Kernel.System.FileSystem;
using Cosmos.Kernel.System.FileSystem.Fat;
using Cosmos.Kernel.System.Storage;
using NewElf.Drivers.ELF;
using NewElf.ELF;
using Sys = Cosmos.Kernel.System;

namespace NewElf.Kernel;

/// <summary>
/// NewElf kernel.
/// Sets up a FAT volume, drops the embedded test ELF on it, then loads and
/// executes it through the <see cref="Elf64"/> loader and the function table
/// built by <see cref="CosmosCallsImpl"/>.
/// </summary>
public unsafe class NewElfKernel : Sys.Kernel
{
    private const string FatDriverName = "ramfat";
    private const string MountPoint = "/mnt";
    private const string ElfPath = "/mnt/elfs/TestElf";

    protected override void BeforeRun()
    {
        SetupRamDisk();

        // write the test ELF and a text file to the disk, then run the ELF
        File.WriteAllText("/mnt/test.txt", "Haii!! :3");
        Directory.CreateDirectory("/mnt/elfs");
        File.WriteAllBytes(ElfPath, TestElfBytes.TestElf);

        // argv[0] is the binary, argv[1] hands the demo a file to read so
        // it does not sit around waiting for typed input
        ElfArgs.SetArgs(new[] { ElfPath, "/mnt/test.txt" });

        Elf64 elf = new(ElfPath);
        elf.PrintElfInfo();
        elf.LoadAndExecuteElf();
        elf.FreeElf();

        Console.WriteLine("COSMOS: Control Regained.");
        Console.WriteLine("COSMOS: ELF exit code: " + elf.ExitCode);
    }

    protected override void Run()
    {
    }

    /// <summary>
    /// Mounts a RAM backed FAT volume at <see cref="MountPoint"/> so the demo
    /// works without a disk image.
    /// </summary>
    private static void SetupRamDisk()
    {
        // 512 byte blocks * 32768 blocks = 32 MiB RAM disk.
        RamBlockDevice ramDisk = new("RAMDISK", 512, 32768);
        FatFileSystemType fat = new(ramDisk);

        if (!VfsManager.RegisterFileSystem(FatDriverName, fat))
        {
            Console.WriteLine("VFS: failed to register the FAT driver.");
            return;
        }

        if (!VfsManager.TryFormat(FatDriverName, "", new FatFormatOptions { Type = FatType.Fat16 }))
        {
            Console.WriteLine("VFS: failed to format the RAM disk.");
            return;
        }

        if (!VfsManager.TryMount(FatDriverName, "", MountFlags.None, MountPoint, out _))
        {
            Console.WriteLine("VFS: failed to mount the RAM disk at " + MountPoint + ".");
        }
    }
}

/// <summary>
/// Minimal RAM backed block device, the storage seam the FAT driver mounts on.
/// </summary>
internal sealed class RamBlockDevice : IBlockDevice
{
    private readonly byte[] _storage;

    public RamBlockDevice(string name, ulong blockSize, ulong blockCount)
    {
        Name = name;
        BlockSize = blockSize;
        BlockCount = blockCount;
        _storage = new byte[blockSize * blockCount];
    }

    public string Name { get; }
    public ulong BlockSize { get; }
    public ulong BlockCount { get; }

    public void ReadBlock(ulong blockNo, ulong blockCount, Span<byte> data)
        => _storage.AsSpan(checked((int)(blockNo * BlockSize)), checked((int)(blockCount * BlockSize))).CopyTo(data);

    public void WriteBlock(ulong blockNo, ulong blockCount, ReadOnlySpan<byte> data)
        => data.Slice(0, checked((int)(blockCount * BlockSize))).CopyTo(_storage.AsSpan(checked((int)(blockNo * BlockSize))));

    public void Flush()
    {
    }
}
