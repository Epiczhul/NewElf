# NewElf
THIS IS A HARD-FORK OF:
https://github.com/Guillermo-Santos/StarElf

## Overview

This project allows you to load and execute ELF files within a Cosmos-based C# operating system. It provides a small library with utility functions to interact with the operating system through direct function calls, including handling console input/output and memory management.

**This version targets Cosmos gen3** (NativeAOT, x86-64). The original gen2 (IL2CPU) version lives in the [upstream StarElf repository](https://github.com/Guillermo-Santos/StarElf).

### Example Usage in C

```c
#include "Cosmos.h"

void example_IDT_GATE(void) {
    printf("Interrupt Called!\n");
}

NO_NAME_MANGLE void main(void*** funcTable) {
    debug_serial_init();
    Cosmos_RegisterFunctionTable(funcTable);

    clrscr();

    get_current_idt();
    idt_ptr_t idt = get_idt();
    set_idt_gate(0x80, (unsigned long)example_IDT_GATE, 0x08, 0x8E);
    set_idt(idt);

    printf("IDT: 0x%lx\n", (unsigned long)&idt);

    asm volatile("int $0x80");

    return;
}
```

![The Loader In Action](_ghimgs/cproof.png)

### Key Features

- **ELF64 Loading**: Loads and executes x86-64 ELF binaries (static PIE) inside a Cosmos gen3 kernel, including `R_X86_64_RELATIVE` / `R_X86_64_IRELATIVE` relocation processing.
- **IDT Management**: Functions to set and retrieve the IDT (64-bit gate format, long mode).
- **Console Operations**: Includes functions for writing and reading text (UTF-16/ASCII) and clearing the console screen.
- **Memory Management**: Functions to allocate, free, and manipulate memory (malloc, calloc, realloc) backed by the gen3 heap via `NativeMemory`.
- **Basic I/O Functions**: Support for printing integers, floats, and other data types to the console.
- **File I/O**: fopen/fread/fwrite/fseek/... on top of the gen3 VFS (`System.IO`).

### Repository layout (gen3)

| Path | What it is |
|------|------------|
| `NewElf.Kernel/` | The Cosmos gen3 kernel project (NativeAOT, `net10.0`, `Cosmos.Sdk`) |
| `NewElf.Kernel/Drivers/ELF/Elf64.cs` | The ELF64 loader (ex `Elf32`, ported to 64-bit + relocations) |
| `NewElf.Kernel/Drivers/ELF/ELFTable.cs` | The function table (`CosmosCallsImpl`) handed to every loaded ELF |
| `NewElf.Kernel/Drivers/ELF/TestElfBytes.cs` | The test ELF embedded as a C# byte array (gen3 has no `ManifestResourceStream`) |
| `NewElf.Kernel/Kernel.cs` | Demo kernel: RAM-disk FAT volume, writes the test ELF, loads and runs it |
| `CosmosExec/` | The C side: Stellib (libc subset + Cosmos bindings) and the demo program |
| `CosmosExec/TestElf` | The compiled x86-64 test ELF (`make` output, committed for convenience) |

The `CodeExecutionPlugs` project from the gen2 version is gone: gen3 replaces the IL2CPU plug system with the IL patcher, and the two XSharp assembly stubs (`ExecuteBinary`, `RestoreStack`) are simply not needed anymore, under NativeAOT a call into raw code is a plain `delegate* unmanaged[Cdecl]` indirect call that already follows the SysV AMD64 ABI.

## Getting Started (Cosmos gen3)

1. Install the gen3 toolchain: the [.NET 10 SDK](https://dotnet.microsoft.com/download) plus the Cosmos CLI (`dotnet tool install -g Cosmos.Tools && cosmos install`, or the Windows installer from the [Cosmos releases](https://github.com/CosmosOS/Cosmos/releases)). Verify with `cosmos check`.
2. Open `NewElf.Kernel/` and run the kernel:
   ```console
   $ cd NewElf.Kernel
   $ cosmos run
   ```
   The demo kernel creates a 32 MiB RAM disk, formats it FAT16, mounts it at `/mnt`, drops the embedded test ELF on it, prints the ELF headers, then jumps into the ELF: it asks for a file name (type `/mnt/test.txt`) and dumps the file through the function table.
3. To run on a real disk image instead of the RAM disk, replace the `SetupRamDisk()` call with a mount of `StorageManager.Partitions[0]` and attach the image with `cosmos run --disk disk.img`.

### Building your own ELF programs

```console
$ cd CosmosExec
$ make          # builds TestElf (gcc -m64, static PIE)
$ make embed    # copies it into the kernel project and regenerates TestElfBytes.cs
```

Requirements for ELFs loaded by NewElf on gen3:

- **x86-64**, the kernel runs in long mode, 32-bit ELFs are rejected.
- **Static PIE** (`gcc -static-pie`), so the binary only needs `R_X86_64_RELATIVE` relocations and can run from any base address. Non-PIE binaries crash, since the loader cannot honor their absolute addresses.
- **`-mno-red-zone`**, the loaded code runs with interrupts enabled on the kernel stack.
- Entry point receives `void*** funcTable` as its first argument (see below).

### Function table

Unchanged since gen2: the entry point gets a `void***` with four classes:

| Class | Contents |
|-------|----------|
| 0 | Console: `WriteLine16`, `Write16`, `ReadLine16`, `WriteLine/WriteInt32`, `...UInt32`, `...Int64`, `...UInt64`, `...Float`, `...Double`, `Clear`, `SetCursorPosition` |
| 1 | Heap: `Alloc(uint)`, `Free(void*)` |
| 2 | File: `Open(path, mode)`, `Close`, `Read`, `Write`, `Seek`, `Tell`, `Flush` |
| 3 | Misc: `GetArgs(int* argc, char*** argv)` |

The C wrappers for all of these live in `CosmosExec/Cosmos.c`, a libc subset (malloc, printf, scanf, fopen, ...) on top of them lives in `CosmosExec/Stellar.c`.
