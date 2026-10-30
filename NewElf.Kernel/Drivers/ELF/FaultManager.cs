using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NewElf.Drivers.ELF
{
    /// <summary>
    /// Fault isolation for loaded ELFs. While an ELF runs, the #UD, #GP and #PF
    /// gates point at small machine code stubs that check if the faulting RIP
    /// is inside the ELF. If it is, a register dump is printed and the run is
    /// unwound back into the kernel with a setjmp/longjmp pair. Faults anywhere
    /// else go to the original Cosmos gate. The exit call of the function table
    /// uses the same longjmp to leave with an exit code.
    /// Run the ELF on the main kernel thread, longjmp breaks across thread switches.
    /// The machine code is built from fixed byte templates and verified before
    /// any gate is touched. If a check fails, isolation stays off.
    /// </summary>
    public static unsafe class FaultManager
    {
        // the vectors we guard: (vector, has error code, human name)
        private static readonly (byte Vector, bool HasError, string Name)[] GuardedVectors =
        {
            (6, false, "Invalid Opcode (#UD)"),
            (13, true, "General Protection Fault (#GP)"),
            (14, true, "Page Fault (#PF)")
        };

        // stub frame layout: the stub pushes 16 GPRs (128 bytes) below the
        // CPU frame. rsp is pushed first, r15 last, so from the stub RSP:
        // +0 r15, +8 r14, +16 r13, +24 r12, +32 r11, +40 r10, +48 r9,
        // +56 r8, +64 rdi, +72 rsi, +80 rbp, +88 rbx, +96 rdx, +104 rcx,
        // +112 rax, +120 rsp (as captured at stub entry).
        private const int OffSavedRsp = 120;
        // CPU frame right behind the pushes. Vectors without an error code:
        // +128 RIP, +136 CS, +144 RFLAGS. Vectors with one: the CPU pushes
        // the error code last, so +128 is the error code and RIP moves up.
        private const int OffRipNoError = 128;
        private const int OffErrorCode = 128;
        private const int OffRipWithError = 136;

        // setjmp buffer: rbx, rbp, rsp, r12, r13, r14, r15, rip
        private const int JmpBufSize = 64;

        private static readonly int StubSize = 128; // one aligned slot per stub

        // ---- machine code templates ----
        // These bytes are what ends up in executable memory. EnsureInitialized
        // checks the hole offsets against the opcodes around them before copying.

        // sidt [rcx]; ret
        private static readonly byte[] SidtThunkBytes = { 0x0F, 0x01, 0x09, 0xC3 };

        // setjmp(rdi = buf): saves the callee saved registers and the return
        // address. rsp is stored as it will be after the return (entry rsp + 8).
        private static readonly byte[] SetjmpThunkBytes =
        {
            0x48, 0x89, 0x1F,                   // mov [rdi], rbx
            0x48, 0x89, 0x6F, 0x08,             // mov [rdi+8], rbp
            0x48, 0x8D, 0x44, 0x24, 0x08,       // lea rax, [rsp+8]
            0x48, 0x89, 0x47, 0x10,             // mov [rdi+16], rax
            0x4C, 0x89, 0x67, 0x18,             // mov [rdi+24], r12
            0x4C, 0x89, 0x6F, 0x20,             // mov [rdi+32], r13
            0x4C, 0x89, 0x77, 0x28,             // mov [rdi+40], r14
            0x4C, 0x89, 0x7F, 0x30,             // mov [rdi+48], r15
            0x48, 0x8B, 0x04, 0x24,             // mov rax, [rsp]
            0x48, 0x89, 0x47, 0x38,             // mov [rdi+56], rax
            0x31, 0xC0,                         // xor eax, eax
            0xC3                                // ret
        };

        // longjmp(rdi = buf, esi = code): puts the code in eax and restores
        // what setjmp saved. rbx and rbp go through rax and rcx first because
        // rdi is still the buffer pointer.
        private static readonly byte[] LongjmpThunkBytes =
        {
            0x48, 0x8B, 0x07,                   // mov rax, [rdi] (saved rbx)
            0x48, 0x8B, 0x4F, 0x08,             // mov rcx, [rdi+8] (saved rbp)
            0x48, 0x8B, 0x57, 0x10,             // mov rdx, [rdi+16] (saved rsp)
            0x4C, 0x8B, 0x67, 0x18,             // mov r12, [rdi+24]
            0x4C, 0x8B, 0x6F, 0x20,             // mov r13, [rdi+32]
            0x4C, 0x8B, 0x77, 0x28,             // mov r14, [rdi+40]
            0x4C, 0x8B, 0x7F, 0x30,             // mov r15, [rdi+48]
            0x48, 0x8B, 0x7F, 0x38,             // mov rdi, [rdi+56] (target rip)
            0x48, 0x89, 0xC3,                   // mov rbx, rax
            0x48, 0x89, 0xCD,                   // mov rbp, rcx
            0x89, 0xF0,                         // mov eax, esi
            0x85, 0xF6,                         // test esi, esi
            0x75, 0x05,                         // jnz restore
            0xB8, 0x01, 0x00, 0x00, 0x00,       // mov eax, 1
            // restore:
            0x48, 0x89, 0xD4,                   // mov rsp, rdx
            // 3E is the notrack prefix, the landing spot is in the middle of a method
            0x3E, 0xFF, 0xE7                    // notrack jmp rdi
        };

        // enter(rdi = calls, rsi = entry, rdx = stackTop): pops the return
        // address, switches to the ELF stack and jumps
        private static readonly byte[] EnterThunkBytes =
        {
            0x58,                               // pop rax
            0x48, 0x89, 0xD4,                   // mov rsp, rdx
            // notrack jmp, the ELF entry has no ENDBR64
            0x3E, 0xFF, 0xE6                    // notrack jmp rsi
        };

        // exception stub: pushes every GPR, calls the managed handler with the
        // frame, then jumps to the original gate (kernel fault) or never comes
        // back (ELF fault, the handler longjmps). It has three holes: the vector
        // (imm32), the handler and the original gate (imm64). BeginElf fills
        // the gate hole on every run.
        private static readonly byte[] StubTemplatePlain =
        {
            0x54,                               // push rsp (faulting RSP, pushed first)
            0x50, 0x51, 0x52, 0x53,             // push rax, rcx, rdx, rbx
            0x55, 0x56, 0x57,                   // push rbp, rsi, rdi
            0x41, 0x50, 0x41, 0x51, 0x41, 0x52, 0x41, 0x53,   // push r8, r9, r10, r11
            0x41, 0x54, 0x41, 0x55, 0x41, 0x56, 0x41, 0x57,   // push r12, r13, r14, r15
            0x48, 0x89, 0xE5,                   // mov rbp, rsp (keep the frame)
            0x48, 0x31, 0xD2,                   // xor rdx, rdx (no CR2 without #PF)
            0xBE, 0x00, 0x00, 0x00, 0x00,       // mov esi, <vector>
            0x48, 0x89, 0xE7,                   // mov rdi, rsp (frame pointer)
            // align the stack for managed code, RDI keeps the frame pointer
            0x48, 0x83, 0xE4, 0xF0,             // and rsp, -16
            0x48, 0xB8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,   // movabs rax, <FaultHandler>
            0xFF, 0xD0,                         // call rax
            0x48, 0x89, 0xEC,                   // mov rsp, rbp
            // add rsp, 128 (drop the pushed GPRs, the CPU frame is on top again)
            0x48, 0x81, 0xC4, 0x80, 0x00, 0x00, 0x00,
            0x48, 0xB8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,   // movabs rax, <original gate>
            0x3E, 0xFF, 0xE0                    // notrack jmp rax
        };

        // same stub for #PF, it passes CR2 in RDX as the third argument
        private static readonly byte[] StubTemplateWithCr2 =
        {
            0x54,                               // push rsp (faulting RSP, pushed first)
            0x50, 0x51, 0x52, 0x53,             // push rax, rcx, rdx, rbx
            0x55, 0x56, 0x57,                   // push rbp, rsi, rdi
            0x41, 0x50, 0x41, 0x51, 0x41, 0x52, 0x41, 0x53,   // push r8, r9, r10, r11
            0x41, 0x54, 0x41, 0x55, 0x41, 0x56, 0x41, 0x57,   // push r12, r13, r14, r15
            0x48, 0x89, 0xE5,                   // mov rbp, rsp (keep the frame)
            0x0F, 0x20, 0xD0,                   // mov rax, cr2
            0x48, 0x89, 0xC2,                   // mov rdx, rax (third handler arg)
            0xBE, 0x00, 0x00, 0x00, 0x00,       // mov esi, <vector>
            0x48, 0x89, 0xE7,                   // mov rdi, rsp (frame pointer)
            0x48, 0x83, 0xE4, 0xF0,             // and rsp, -16
            0x48, 0xB8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,   // movabs rax, <FaultHandler>
            0xFF, 0xD0,                         // call rax
            0x48, 0x89, 0xEC,                   // mov rsp, rbp
            0x48, 0x81, 0xC4, 0x80, 0x00, 0x00, 0x00,   // add rsp, 128
            0x48, 0xB8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,   // movabs rax, <original gate>
            0x3E, 0xFF, 0xE0                    // notrack jmp rax
        };

        // hole offsets inside the templates, checked against the opcodes at init
        private const int StubPrefixLen = 27;   // pushes plus mov rbp, rsp
        private const int PlainVariantLen = 3;  // xor rdx, rdx
        private const int Cr2VariantLen = 6;    // mov rax, cr2 + mov rdx, rax
        private const int PlainVectorHole = StubPrefixLen + PlainVariantLen + 1;   // imm32 behind the BE
        private const int Cr2VectorHole = StubPrefixLen + Cr2VariantLen + 1;
        private const int PlainHandlerHole = PlainVectorHole + 4 + 7 + 2;   // imm64 behind mov rdi and and rsp, movabs is 2 bytes
        private const int Cr2HandlerHole = Cr2VectorHole + 4 + 7 + 2;
        private const int PlainGateHole = PlainHandlerHole + 8 + 2 + 3 + 7 + 2;   // imm64 behind call, mov rsp and add rsp
        private const int Cr2GateHole = Cr2HandlerHole + 8 + 2 + 3 + 7 + 2;

        // ---- state of the currently running ELF ----
        private static ulong ElfStart;
        private static ulong ElfEnd;
        private static bool ElfActive;
        private static bool InHandler;
        private static int ExitCode;
        private static byte* JmpBuf;

        // ---- native bits, built once from the templates and reused ----
        private static byte* SidtThunk;
        private static byte* SetjmpThunk;
        private static byte* LongjmpThunk;
        private static byte* EnterThunk;
        private static byte* StubMemory;
        private static bool Initialized;
        private static bool BlobsValid;

        // gates we patched: (vector, gate address, original offset qword)
        private static readonly List<(byte Vector, nint Gate, ulong Original)> Patched = new();

        // where the vector, handler and gate values sit inside each built stub
        private static readonly int[] StubVectorHole = new int[GuardedVectors.Length];
        private static readonly int[] StubHandlerHole = new int[GuardedVectors.Length];
        private static readonly int[] StubOrigOffset = new int[GuardedVectors.Length];

        /// <summary>
        /// Marks an ELF as running and points the guarded exception gates at
        /// the ELF checking stubs. Must be paired with <see cref="EndElf"/>.
        /// </summary>
        public static void BeginElf(ulong start, ulong end)
        {
            EnsureInitialized();

            if (!BlobsValid)
            {
                Console.WriteLine("FaultManager: machine code blobs failed verification, fault isolation is off.");
                return;
            }

            ElfStart = start;
            ElfEnd = end;
            ElfActive = true;
            Patched.Clear();

            // read the live IDT with sidt
            IdtRegister idtr = default;
            ((delegate* unmanaged[Cdecl]<IdtRegister*, void>)SidtThunk)(&idtr);

            if (idtr.Base == 0 || idtr.Limit < 255 * 16)
            {
                Console.WriteLine("FaultManager: no live IDT found, fault isolation is off.");
                return;
            }

            for (int i = 0; i < GuardedVectors.Length; i++)
            {
                var guarded = GuardedVectors[i];
                byte* gate = (byte*)idtr.Base + guarded.Vector * 16;

                // only patch present gates: those are the ones we can chain
                // back into, a missing gate would turn every fault fatal again
                if ((gate[5] & 0x80) == 0)
                    continue;

                byte* stub = StubMemory + i * StubSize;

                // bake the original gate offset into the stub, it jumps there
                // once the handler says the fault is not inside the ELF
                ulong original = ReadGateOffset(gate);
                *(ulong*)(stub + StubOrigOffset[i]) = original;

                Patched.Add((guarded.Vector, (nint)gate, original));
                WriteGateOffset(gate, (ulong)stub);
            }
        }

        /// <summary>
        /// Restores the original exception gates and forgets the ELF range.
        /// </summary>
        public static void EndElf()
        {
            foreach (var patched in Patched)
                WriteGateOffset((byte*)patched.Gate, patched.Original);

            Patched.Clear();
            ElfActive = false;
            ElfStart = 0;
            ElfEnd = 0;
        }

        /// <summary>
        /// Enters the ELF entry point and returns its exit code. The call
        /// only comes back through <see cref="RequestExit"/> or a caught
        /// fault, both longjmp to the setjmp below. setjmp returning a
        /// nonzero value marks the jump back, matching the C convention.
        /// </summary>
        public static int Run(void*** calls, byte* entryPoint, byte* stackTop)
        {
            JmpBuf = (byte*)NativeMemory.Alloc(JmpBufSize);

            delegate* unmanaged[Cdecl]<byte*, int> setjmpFn = (delegate* unmanaged[Cdecl]<byte*, int>)SetjmpThunk;
            delegate* unmanaged[Cdecl]<void***, byte*, byte*, void> enterFn =
                (delegate* unmanaged[Cdecl]<void***, byte*, byte*, void>)EnterThunk;

            if (setjmpFn(JmpBuf) == 0)
            {
                // pops the return address pushed by this call, switches RSP
                // to the process start block and jumps to the entry point.
                // RDI keeps the calls table, at entry RSP points at argc
                // exactly like a fresh process on Linux.
                enterFn(calls, entryPoint, stackTop);

                // only reachable when the ELF returns instead of exiting
                RequestExit(0);
            }

            NativeMemory.Free(JmpBuf);
            JmpBuf = null;
            return ExitCode;
        }

        /// <summary>
        /// Leaves the running ELF with an exit code. Called from the exit
        /// entry of the function table and from the fault handler. Without a
        /// pending run it only records the code and returns.
        /// </summary>
        public static void RequestExit(int code)
        {
            ExitCode = code;

            if (JmpBuf != null)
                ((delegate* unmanaged[Cdecl]<byte*, int, void>)LongjmpThunk)(JmpBuf, code);
        }

        /// <summary>
        /// Managed half of the exception stubs. Runs with interrupts masked
        /// on the interrupted stack. Faults inside the ELF print a report
        /// and longjmp out with 128 + vector as exit code, mirroring the
        /// Unix "terminated by signal" convention. Everything else returns
        /// and lets the stub chain into the original Cosmos gate.
        /// </summary>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void FaultHandler(void* frame, int vector, ulong cr2)
        {
            // a fault while writing the report would recurse, bail out and
            // let the original gate handle whatever is going on
            if (InHandler || !ElfActive || JmpBuf == null)
                return;

            bool hasError = vector == 13 || vector == 14;
            ulong rip = *(ulong*)((byte*)frame + (hasError ? OffRipWithError : OffRipNoError));

            // not our ELF: return, the stub restores the registers and jumps
            // into the original Cosmos gate for the usual panic
            if (rip < ElfStart || rip >= ElfEnd)
                return;

            InHandler = true;

            string name = "";
            foreach (var guarded in GuardedVectors)
            {
                if (guarded.Vector == vector)
                    name = guarded.Name;
            }

            ulong errorCode = hasError ? *(ulong*)((byte*)frame + OffErrorCode) : 0;

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("");
            Console.WriteLine("========================================");
            Console.WriteLine("NewElf: the loaded ELF faulted, killing it");
            Console.WriteLine("========================================");
            Console.ResetColor();
            Console.WriteLine($"Vector:    {vector} ({name})");
            Console.WriteLine($"RIP:       0x{rip:X16}");
            Console.WriteLine($"RSP:       0x{*(ulong*)((byte*)frame + OffSavedRsp):X16}");
            if (hasError)
                Console.WriteLine($"ErrorCode: 0x{errorCode:X}");
            if (vector == 14)
                Console.WriteLine($"CR2:       0x{cr2:X16}");
            Console.WriteLine($"RAX: 0x{*(ulong*)((byte*)frame + 112):X16}  RBX: 0x{*(ulong*)((byte*)frame + 88):X16}");
            Console.WriteLine($"RCX: 0x{*(ulong*)((byte*)frame + 104):X16}  RDX: 0x{*(ulong*)((byte*)frame + 96):X16}");
            Console.WriteLine($"RSI: 0x{*(ulong*)((byte*)frame + 72):X16}  RDI: 0x{*(ulong*)((byte*)frame + 64):X16}");
            Console.WriteLine($"RBP: 0x{*(ulong*)((byte*)frame + 80):X16}  R8:  0x{*(ulong*)((byte*)frame + 56):X16}");

            ExitCode = 128 + vector;
            InHandler = false;
            RequestExit(ExitCode);
        }

        // The GC never runs across the longjmp: between setjmp and the jump
        // back the only live locals in Run are native pointers and ints, and
        // the jump restores every callee saved register from the buffer.

        /// <summary>
        /// Builds the machine code from the templates and verifies it: hole
        /// offsets, copies and the written values are all read back. On any
        /// mismatch <see cref="BlobsValid"/> stays false.
        /// </summary>
        private static void EnsureInitialized()
        {
            if (Initialized)
                return;

            Initialized = true;
            bool ok = true;

            // check the templates first
            ok &= TemplateMarkersOk(StubTemplatePlain, PlainVectorHole, PlainHandlerHole, PlainGateHole);
            ok &= TemplateMarkersOk(StubTemplateWithCr2, Cr2VectorHole, Cr2HandlerHole, Cr2GateHole);

            // the fixed thunks are plain copies
            SidtThunk = CopyBlob(SidtThunkBytes, 8);
            SetjmpThunk = CopyBlob(SetjmpThunkBytes, 48);
            LongjmpThunk = CopyBlob(LongjmpThunkBytes, 64);
            EnterThunk = CopyBlob(EnterThunkBytes, 16);
            ok &= BlobMatches(SidtThunk, SidtThunkBytes);
            ok &= BlobMatches(SetjmpThunk, SetjmpThunkBytes);
            ok &= BlobMatches(LongjmpThunk, LongjmpThunkBytes);
            ok &= BlobMatches(EnterThunk, EnterThunkBytes);

            // one exception stub per guarded vector
            StubMemory = (byte*)NativeMemory.Alloc((nuint)(GuardedVectors.Length * StubSize));
            for (int v = 0; v < GuardedVectors.Length && ok; v++)
            {
                byte vector = GuardedVectors[v].Vector;
                BuildStub(StubMemory + v * StubSize, vector);
                ok &= StubSlotOk(StubMemory + v * StubSize, v, vector);
            }

            if (!ok)
            {
                Console.WriteLine("FaultManager: machine code verification failed, keeping the original exception gates.");
                BlobsValid = false;
                return;
            }

            BlobsValid = true;
        }

        private static int StubIndex(byte vector)
        {
            for (int i = 0; i < GuardedVectors.Length; i++)
            {
                if (GuardedVectors[i].Vector == vector)
                    return i;
            }

            return 0;
        }

        /// <summary>
        /// Copies one stub template into its slot, writes the vector and the
        /// handler in and records where the gate hole is. The gate stays zero
        /// until BeginElf fills it.
        /// </summary>
        private static void BuildStub(byte* p, byte vector)
        {
            bool withCr2 = vector == 14;
            byte[] template = withCr2 ? StubTemplateWithCr2 : StubTemplatePlain;
            int index = StubIndex(vector);

            for (int i = 0; i < template.Length; i++)
                p[i] = template[i];

            *(int*)(p + (withCr2 ? Cr2VectorHole : PlainVectorHole)) = vector;
            *(ulong*)(p + (withCr2 ? Cr2HandlerHole : PlainHandlerHole)) =
                (ulong)(delegate* unmanaged[Cdecl]<void*, int, ulong, void>)&FaultHandler;

            StubVectorHole[index] = withCr2 ? Cr2VectorHole : PlainVectorHole;
            StubHandlerHole[index] = withCr2 ? Cr2HandlerHole : PlainHandlerHole;
            StubOrigOffset[index] = withCr2 ? Cr2GateHole : PlainGateHole;
        }

        // checks that the holes sit behind the right opcodes and the template fits
        private static bool TemplateMarkersOk(byte[] t, int vectorHole, int handlerHole, int gateHole)
        {
            return t.Length <= StubSize
                && t[vectorHole - 1] == 0xBE
                && t[handlerHole - 2] == 0x48 && t[handlerHole - 1] == 0xB8
                && t[handlerHole + 8] == 0xFF && t[handlerHole + 9] == 0xD0
                && t[gateHole - 2] == 0x48 && t[gateHole - 1] == 0xB8
                && t[gateHole + 8] == 0x3E && t[gateHole + 9] == 0xFF && t[gateHole + 10] == 0xE0;
        }

        // checks a built stub slot by reading the values and opcodes back
        private static bool StubSlotOk(byte* p, int index, byte vector)
        {
            return p[StubVectorHole[index] - 1] == 0xBE
                && *(int*)(p + StubVectorHole[index]) == vector
                && p[StubHandlerHole[index] - 2] == 0x48 && p[StubHandlerHole[index] - 1] == 0xB8
                && *(ulong*)(p + StubHandlerHole[index]) ==
                    (ulong)(delegate* unmanaged[Cdecl]<void*, int, ulong, void>)&FaultHandler
                && p[StubHandlerHole[index] + 8] == 0xFF && p[StubHandlerHole[index] + 9] == 0xD0
                && p[StubOrigOffset[index] - 2] == 0x48 && p[StubOrigOffset[index] - 1] == 0xB8
                && p[StubOrigOffset[index] + 8] == 0x3E && p[StubOrigOffset[index] + 9] == 0xFF
                && p[StubOrigOffset[index] + 10] == 0xE0;
        }

        private static byte* CopyBlob(byte[] golden, nuint size)
        {
            byte* p = (byte*)NativeMemory.Alloc(size);
            for (int i = 0; i < golden.Length; i++)
                p[i] = golden[i];

            return p;
        }

        private static bool BlobMatches(byte* p, byte[] golden)
        {
            for (int i = 0; i < golden.Length; i++)
            {
                if (p[i] != golden[i])
                    return false;
            }

            return true;
        }

        // 16 byte IDT gate in long mode: offset low, selector, ist, type,
        // offset mid, offset high, reserved.

        private static ulong ReadGateOffset(byte* gate)
        {
            return *(ushort*)gate
                 | ((ulong)(*(ushort*)(gate + 6)) << 16)
                 | ((ulong)(*(uint*)(gate + 8)) << 32);
        }

        private static void WriteGateOffset(byte* gate, ulong address)
        {
            *(ushort*)gate = (ushort)(address & 0xFFFF);
            *(ushort*)(gate + 6) = (ushort)((address >> 16) & 0xFFFF);
            *(uint*)(gate + 8) = (uint)(address >> 32);
        }

        // memory image as sidt writes it: 2 byte limit, 8 byte base
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct IdtRegister
        {
            public ushort Limit;
            public ulong Base;
        }
    }
}
