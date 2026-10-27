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

        // ---- state of the currently running ELF ----
        private static ulong ElfStart;
        private static ulong ElfEnd;
        private static bool ElfActive;
        private static bool InHandler;
        private static int ExitCode;
        private static byte* JmpBuf;

        // ---- native bits, hand assembled once and reused ----
        private static byte* SidtThunk;
        private static byte* SetjmpThunk;
        private static byte* LongjmpThunk;
        private static byte* EnterThunk;
        private static byte* StubMemory;
        private static bool Initialized;

        // gates we patched: (vector, gate address, original offset qword)
        private static readonly List<(byte Vector, nint Gate, ulong Original)> Patched = new();

        // where the original gate offset sits inside each stub: right at the
        // movabs before the final jmp, so its position depends on whether the
        // stub loads CR2 for that vector
        private static readonly int[] StubOrigOffset = new int[GuardedVectors.Length];

        /// <summary>
        /// Marks an ELF as running and points the guarded exception gates at
        /// the ELF checking stubs. Must be paired with <see cref="EndElf"/>.
        /// </summary>
        public static void BeginElf(ulong start, ulong end)
        {
            EnsureInitialized();

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
        /// entry of the function table and from the fault handler.
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
        /// Builds the small machine code blobs once: the sidt thunk, the
        /// setjmp/longjmp pair, the stack switching enter thunk and one
        /// exception stub per guarded vector.
        /// </summary>
        private static void EnsureInitialized()
        {
            if (Initialized)
                return;

            // sidt [rcx]; ret
            SidtThunk = (byte*)NativeMemory.Alloc(8);
            SidtThunk[0] = 0x0F;
            SidtThunk[1] = 0x01;
            SidtThunk[2] = 0x09;
            SidtThunk[3] = 0xC3;

            // setjmp: stow the callee saved registers and the return address.
            // rsp goes in as it will be after setjmp returns (entry rsp + 8,
            // same trick glibc uses), so the jump back lands with the stack
            // exactly where the caller expects it.
            // setjmp(rdi = buf)
            SetjmpThunk = (byte*)NativeMemory.Alloc(48);
            byte* s = SetjmpThunk;
            int i = 0;
            s[i] = 0x48; s[i + 1] = 0x89; s[i + 2] = 0x1F; i += 3;          // mov [rdi], rbx
            s[i] = 0x48; s[i + 1] = 0x89; s[i + 2] = 0x6F; s[i + 3] = 0x08; i += 4;   // mov [rdi+8], rbp
            s[i] = 0x48; s[i + 1] = 0x8D; s[i + 2] = 0x44; s[i + 3] = 0x24; s[i + 4] = 0x08; i += 5;   // lea rax, [rsp+8]
            s[i] = 0x48; s[i + 1] = 0x89; s[i + 2] = 0x47; s[i + 3] = 0x10; i += 4;   // mov [rdi+16], rax
            s[i] = 0x4C; s[i + 1] = 0x89; s[i + 2] = 0x67; s[i + 3] = 0x18; i += 4;   // mov [rdi+24], r12
            s[i] = 0x4C; s[i + 1] = 0x89; s[i + 2] = 0x6F; s[i + 3] = 0x20; i += 4;   // mov [rdi+32], r13
            s[i] = 0x4C; s[i + 1] = 0x89; s[i + 2] = 0x77; s[i + 3] = 0x28; i += 4;   // mov [rdi+40], r14
            s[i] = 0x4C; s[i + 1] = 0x89; s[i + 2] = 0x7F; s[i + 3] = 0x30; i += 4;   // mov [rdi+48], r15
            s[i] = 0x48; s[i + 1] = 0x8B; s[i + 2] = 0x04; s[i + 3] = 0x24; i += 4;   // mov rax, [rsp]
            s[i] = 0x48; s[i + 1] = 0x89; s[i + 2] = 0x47; s[i + 3] = 0x38; i += 4;   // mov [rdi+56], rax
            s[i] = 0x31; s[i + 1] = 0xC0; i += 2;                            // xor eax, eax
            s[i] = 0xC3;                                                     // ret

            // longjmp: put the code in eax, restore what setjmp saved.
            // rbx and rbp ride through rax and rcx: load them into scratch
            // first, copy them into place after rdi gave up the buffer.
            // longjmp(rdi = buf, esi = code)
            LongjmpThunk = (byte*)NativeMemory.Alloc(64);
            byte* l = LongjmpThunk;
            i = 0;
            l[i] = 0x48; l[i + 1] = 0x8B; l[i + 2] = 0x07; i += 3;          // mov rax, [rdi] (saved rbx)
            l[i] = 0x48; l[i + 1] = 0x8B; l[i + 2] = 0x4F; l[i + 3] = 0x08; i += 4;   // mov rcx, [rdi+8] (saved rbp)
            l[i] = 0x48; l[i + 1] = 0x8B; l[i + 2] = 0x57; l[i + 3] = 0x10; i += 4;   // mov rdx, [rdi+16] (saved rsp)
            l[i] = 0x4C; l[i + 1] = 0x8B; l[i + 2] = 0x67; l[i + 3] = 0x18; i += 4;   // mov r12, [rdi+24]
            l[i] = 0x4C; l[i + 1] = 0x8B; l[i + 2] = 0x6F; l[i + 3] = 0x20; i += 4;   // mov r13, [rdi+32]
            l[i] = 0x4C; l[i + 1] = 0x8B; l[i + 2] = 0x77; l[i + 3] = 0x28; i += 4;   // mov r14, [rdi+40]
            l[i] = 0x4C; l[i + 1] = 0x8B; l[i + 2] = 0x7F; l[i + 3] = 0x30; i += 4;   // mov r15, [rdi+48]
            l[i] = 0x48; l[i + 1] = 0x8B; l[i + 2] = 0x7F; l[i + 3] = 0x38; i += 4;   // mov rdi, [rdi+56] (target rip)
            l[i] = 0x48; l[i + 1] = 0x89; l[i + 2] = 0xC3; i += 3;          // mov rbx, rax
            l[i] = 0x48; l[i + 1] = 0x89; l[i + 2] = 0xCD; i += 3;          // mov rbp, rcx
            l[i] = 0x89; l[i + 1] = 0xF0; i += 2;                            // mov eax, esi
            l[i] = 0x85; l[i + 1] = 0xF6; i += 2;                            // test esi, esi
            l[i] = 0x75; l[i + 1] = 0x05; i += 2;                            // jnz restore
            l[i] = 0xB8; l[i + 1] = 0x01; l[i + 2] = 0x00; l[i + 3] = 0x00; l[i + 4] = 0x00; i += 5;   // mov eax, 1
            // restore:
            l[i] = 0x48; l[i + 1] = 0x89; l[i + 2] = 0xD4; i += 3;           // mov rsp, rdx
            // 3E is the notrack prefix: the landing spot is mid method, so
            // where IBT is enforced (a running .NET host for one) the plain
            // indirect jmp would fault. glibc does the same in longjmp.
            l[i] = 0x3E; l[i + 1] = 0xFF; l[i + 2] = 0xE7; i += 3;           // notrack jmp rdi

            // enter: pop the return address, switch to the ELF stack, jump
            // enter(rdi = calls, rsi = entry, rdx = stackTop)
            EnterThunk = (byte*)NativeMemory.Alloc(16);
            EnterThunk[0] = 0x58;                              // pop rax
            EnterThunk[1] = 0x48; EnterThunk[2] = 0x89; EnterThunk[3] = 0xD4;   // mov rsp, rdx
            // notrack jmp: the ELF entry has no ENDBR64 for IBT to like
            EnterThunk[4] = 0x3E; EnterThunk[5] = 0xFF; EnterThunk[6] = 0xE6;   // notrack jmp rsi

            // one exception stub per guarded vector
            StubMemory = (byte*)NativeMemory.Alloc((nuint)(GuardedVectors.Length * StubSize));
            for (int v = 0; v < GuardedVectors.Length; v++)
                BuildStub(StubMemory + v * StubSize, GuardedVectors[v].Vector);

            Initialized = true;
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
        /// Writes one exception stub: stash every GPR, call the managed
        /// handler with the frame, then either return to the original gate
        /// (kernel fault) or never come back (ELF fault, handler longjmps).
        /// The original gate offset is patched in per run by BeginElf, it
        /// lives in the last movabs before the jmp.
        /// </summary>
        private static void BuildStub(byte* p, byte vector)
        {
            int i = 0;

            p[i++] = 0x54;                                     // push rsp (faulting RSP, pushed first)
            p[i++] = 0x50; p[i++] = 0x51; p[i++] = 0x52; p[i++] = 0x53;   // push rax, rcx, rdx, rbx
            p[i++] = 0x55; p[i++] = 0x56; p[i++] = 0x57;                  // push rbp, rsi, rdi
            for (byte r = 0; r < 8; r++)
            {
                p[i++] = 0x41; p[i++] = (byte)(0x50 + r);      // push r8 .. r15
            }

            p[i] = 0x48; p[i + 1] = 0x89; p[i + 2] = 0xE5; i += 3;   // mov rbp, rsp (keep the frame)

            if (vector == 14)
            {
                p[i] = 0x0F; p[i + 1] = 0x20; p[i + 2] = 0xD0; i += 3;   // mov rax, cr2
                p[i] = 0x48; p[i + 1] = 0x89; p[i + 2] = 0xC2; i += 3;   // mov rdx, rax (third handler arg)
            }
            else
            {
                p[i] = 0x48; p[i + 1] = 0x31; p[i + 2] = 0xD2; i += 3;   // xor rdx, rdx
            }

            p[i++] = 0xBE;                                     // mov esi, vector
            *(int*)(p + i) = vector;
            i += 4;

            p[i] = 0x48; p[i + 1] = 0x89; p[i + 2] = 0xE7; i += 3;   // mov rdi, rsp (frame pointer)
            // managed code assumes a 16 byte aligned stack and the pushes
            // left RSP arbitrary, so straighten it out for the call. RDI
            // keeps the unaligned frame pointer for the frame offsets.
            p[i] = 0x48; p[i + 1] = 0x83; p[i + 2] = 0xE4; p[i + 3] = 0xF0; i += 4;   // and rsp, -16

            p[i] = 0x48; p[i + 1] = 0xB8; i += 2;              // movabs rax, FaultHandler
            *(ulong*)(p + i) = (ulong)(delegate* unmanaged[Cdecl]<void*, int, ulong, void>)&FaultHandler;
            i += 8;

            p[i] = 0xFF; p[i + 1] = 0xD0; i += 2;              // call rax

            p[i] = 0x48; p[i + 1] = 0x89; p[i + 2] = 0xEC; i += 3;   // mov rsp, rbp
            // add rsp, 128 (drop the pushed GPRs, the CPU frame is on top again)
            p[i] = 0x48; p[i + 1] = 0x81; p[i + 2] = 0xC4; p[i + 3] = 0x80; p[i + 4] = 0x00; p[i + 5] = 0x00; p[i + 6] = 0x00; i += 7;

            p[i] = 0x48; p[i + 1] = 0xB8; i += 2;              // movabs rax, original gate
            StubOrigOffset[StubIndex(vector)] = i;             // imm64 patched by BeginElf
            i += 8;

            p[i] = 0x3E; p[i + 1] = 0xFF; p[i + 2] = 0xE0; i += 3;   // notrack jmp rax
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
