#include "Cosmos.h"

// The loader enters _start with RDI = function table and RSP at the process
// start block (argc, argv, envp, auxv). The stub passes both to main and
// calls Cosmos_Exit with its return value.
__asm__(
    ".globl _start\n"
    "_start:\n"
    "    mov %rsp, %rsi\n"      // rsi = &argc, the process start block
    "    and $-16, %rsp\n"      // keep the stack aligned for the call
    "    call main\n"
    "    mov %eax, %edi\n"      // main returns the exit code
    "    call Cosmos_Exit\n"
    "1:  hlt\n"                 // never reached, the exit longjmps away
    "    jmp 1b\n"
);

int get_file_size(FILE* file) {
    int current_pos = ftell(file);
    fseek(file, 0, SEEK_END);
    int size = ftell(file);
    fseek(file, current_pos, SEEK_SET);
    return size;
}

NO_NAME_MANGLE int main(void*** funcTable, unsigned long* startupStack) {
    COSMOS_INIT();

    char file[100];

    clrscr();

    // startupStack follows the SysV process start layout:
    // [0] argc, then argv pointers, NULL, envp (a NULL here), then auxv pairs
    unsigned long argc = startupStack[0];
    char** argv = (char**)(startupStack + 1);

    printf("argc: %d\n", (int)argc);
    for (unsigned long i = 0; i < argc; i++) {
        printf("argv[%d]: %s\n", (int)i, argv[i]);
    }

    // walk past argv and envp to reach the auxiliary vector and show a few
    // entries, a static PIE uses exactly these to relocate itself
    unsigned long* it = startupStack + 1;
    while (*it != 0) it++;          // argv terminator
    it++;                           // envp, a single NULL for us
    char hex[32];
    while (it[0] != 0) {            // auxv: (type, value) pairs, AT_NULL ends it
        unsigned long type = it[0];
        unsigned long value = it[1];
        if (type == 3) {            // AT_PHDR, program headers in memory
            ultoa(value, hex, 16);
            printf("auxv AT_PHDR: 0x%s\n", hex);
        } else if (type == 6) {     // AT_PAGESZ
            printf("auxv AT_PAGESZ: %d\n", (int)value);
        } else if (type == 9) {     // AT_ENTRY
            ultoa(value, hex, 16);
            printf("auxv AT_ENTRY: 0x%s\n", hex);
        } else if (type == 31) {    // AT_EXECFN, path of the binary
            printf("auxv AT_EXECFN: %s\n", (char*)value);
        }
        it += 2;
    }

    // argv[1] carries the file to read, the old typed prompt stays as a
    // fallback for runs without arguments
    if (argc > 1) {
        strncpy(file, argv[1], 99);
        file[99] = '\0';
        printf("File name (from argv[1]): %s\n", file);
    } else {
        printf("Enter file name (e.g. /mnt/test.txt): ");
        scanf("%s", file);
        printf("File name: %s\n", file);
    }

    FILE* handle = fopen(file, "r+");
    if (handle == NULL) {
        printf("Failed to open file.\n");
        return 1;
    }

    int size = get_file_size(handle);
    printf("File size: %d bytes\n", size);

    char* buffer = (char*)malloc(size + 1);
    if (buffer == NULL) {
        printf("Failed to allocate memory.\n");
        fclose(handle);
        return 2;
    }

    int bytesRead = fread(buffer, 1, size, handle);
    if (bytesRead != size) {
        printf("Failed to read file.\n");
        free(buffer);
        fclose(handle);
        return 3;
    }

    buffer[size] = '\0';
    printf("File contents: %s\n", buffer);

    fclose(handle);
    free(buffer);

    return 42;
}
