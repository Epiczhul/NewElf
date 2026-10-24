#include "Cosmos.h"

/// ############################# READ THIS NOTE ############################ ///
/// Before you add your own function, for the love of god please know that          ///
/// C# uses UTF-16 encoding for strings. This means that if you pass a string       ///
/// to a function, it will be in UTF-16 encoding. If you want to use ASCII,         ///
/// you need to convert it to UTF-16 first.                                         ///
/// The kernel is NativeAOT compiled for x86-64, so calls through the function      ///
/// table follow the SysV AMD64 ABI and arguments are passed in normal order.       ///
/// ############################# READ THIS NOTE ############################ ///

void*** funcTable = 0;
char lastEncoding = 0; // 0 = None, 1 = ASCII, 2 = UTF-16
FUNC_ void Cosmos_RegisterFunctionTable(void*** table) {
    funcTable = table;
}

/// CLASS 0 (Console)

// INDEX 0
FUNC_ void Console_WriteLine16(char* str) 
{
    #ifdef PREVENT_ENCODING_MANGLING
    if (lastEncoding == 0) lastEncoding = 2;
    else if (lastEncoding == 1) {
        lastEncoding = 2; // just switch encodings, flushing recursively overflowed the stack
    }
    #endif
    void (*print)(char*) = (void (*)(char*)) funcTable[0][0];

    print(str);
}

// INDEX 1
FUNC_ void Console_Write16(char* str) 
{
    #ifdef PREVENT_ENCODING_MANGLING
    if (lastEncoding == 0) lastEncoding = 2;
    else if (lastEncoding == 1) {
        lastEncoding = 2; // just switch encodings, flushing recursively overflowed the stack
    }
    #endif
    void (*print)(char*) = (void (*)(char*)) funcTable[0][1];

    print(str);
}

// INDEX 2
FUNC_ char* Console_ReadLine16(void) 
{
    #ifdef PREVENT_ENCODING_MANGLING
    if (lastEncoding == 0) lastEncoding = 2;
    else if (lastEncoding == 1) {
        lastEncoding = 2; // just switch encodings, flushing recursively overflowed the stack
    }
    #endif
    char* (*read)(void) = (char* (*)(void)) funcTable[0][2];

    return read();
}

// INDEX 3
FUNC_ void Console_WriteLineInt32(int num) 
{
    #ifdef PREVENT_ENCODING_MANGLING
    if (lastEncoding == 0) lastEncoding = 2;
    else if (lastEncoding == 1) {
        lastEncoding = 2; // just switch encodings, flushing recursively overflowed the stack
    }
    #endif
    void (*print)(int) = (void (*)(int)) funcTable[0][3];

    print(num);
}

// INDEX 4
FUNC_ void Console_WriteInt32(int num) 
{
    #ifdef PREVENT_ENCODING_MANGLING
    if (lastEncoding == 0) lastEncoding = 2;
    else if (lastEncoding == 1) {
        lastEncoding = 2; // just switch encodings, flushing recursively overflowed the stack
    }
    #endif
    void (*print)(int) = (void (*)(int)) funcTable[0][4];

    print(num);
}

// INDEX 5
FUNC_ void Console_WriteLineUInt32(unsigned int num) 
{
    #ifdef PREVENT_ENCODING_MANGLING
    if (lastEncoding == 0) lastEncoding = 2;
    else if (lastEncoding == 1) {
        lastEncoding = 2; // just switch encodings, flushing recursively overflowed the stack
    }
    #endif
    void (*print)(unsigned int) = (void (*)(unsigned int)) funcTable[0][5];

    print(num);
}

// INDEX 6
FUNC_ void Console_WriteUInt32(unsigned int num) 
{
    #ifdef PREVENT_ENCODING_MANGLING
    if (lastEncoding == 0) lastEncoding = 2;
    else if (lastEncoding == 1) {
        lastEncoding = 2; // just switch encodings, flushing recursively overflowed the stack
    }
    #endif
    void (*print)(unsigned int) = (void (*)(unsigned int)) funcTable[0][6];

    print(num);
}

// INDEX 7
FUNC_ void Console_WriteLineInt64(long long num) 
{
    #ifdef PREVENT_ENCODING_MANGLING
    if (lastEncoding == 0) lastEncoding = 2;
    else if (lastEncoding == 1) {
        lastEncoding = 2; // just switch encodings, flushing recursively overflowed the stack
    }
    #endif
    void (*print)(long long) = (void (*)(long long)) funcTable[0][7];

    print(num);
}

// INDEX 8
FUNC_ void Console_WriteInt64(long long num) 
{
    #ifdef PREVENT_ENCODING_MANGLING
    if (lastEncoding == 0) lastEncoding = 2;
    else if (lastEncoding == 1) {
        lastEncoding = 2; // just switch encodings, flushing recursively overflowed the stack
    }
    #endif
    void (*print)(long long) = (void (*)(long long)) funcTable[0][8];

    print(num);
}

// INDEX 9
FUNC_ void Console_WriteLineUInt64(unsigned long long num) 
{
    #ifdef PREVENT_ENCODING_MANGLING
    if (lastEncoding == 0) lastEncoding = 2;
    else if (lastEncoding == 1) {
        lastEncoding = 2; // just switch encodings, flushing recursively overflowed the stack
    }
    #endif
    void (*print)(unsigned long long) = (void (*)(unsigned long long)) funcTable[0][9];

    print(num);
}

// INDEX 10
FUNC_ void Console_WriteUInt64(unsigned long long num) 
{
    #ifdef PREVENT_ENCODING_MANGLING
    if (lastEncoding == 0) lastEncoding = 2;
    else if (lastEncoding == 1) {
        lastEncoding = 2; // just switch encodings, flushing recursively overflowed the stack
    }
    #endif
    void (*print)(unsigned long long) = (void (*)(unsigned long long)) funcTable[0][10];

    print(num);
}

// INDEX 11 (float)
FUNC_ void Console_WriteLineFloat(float num) 
{
    #ifdef PREVENT_ENCODING_MANGLING
    if (lastEncoding == 0) lastEncoding = 2;
    else if (lastEncoding == 1) {
        lastEncoding = 2; // just switch encodings, flushing recursively overflowed the stack
    }
    #endif
    void (*print)(float) = (void (*)(float)) funcTable[0][11];

    print(num);
}

// INDEX 12 (float)
FUNC_ void Console_WriteFloat(float num) 
{
    #ifdef PREVENT_ENCODING_MANGLING
    if (lastEncoding == 0) lastEncoding = 2;
    else if (lastEncoding == 1) {
        lastEncoding = 2; // just switch encodings, flushing recursively overflowed the stack
    }
    #endif
    void (*print)(float) = (void (*)(float)) funcTable[0][12];

    print(num);
}

// INDEX 13 (double)
FUNC_ void Console_WriteLineDouble(double num) 
{
    #ifdef PREVENT_ENCODING_MANGLING
    if (lastEncoding == 0) lastEncoding = 2;
    else if (lastEncoding == 1) {
        lastEncoding = 2; // just switch encodings, flushing recursively overflowed the stack
    }
    #endif
    void (*print)(double) = (void (*)(double)) funcTable[0][13];

    print(num);
}

// INDEX 14 (double)
FUNC_ void Console_WriteDouble(double num) 
{
    #ifdef PREVENT_ENCODING_MANGLING
    if (lastEncoding == 0) lastEncoding = 2;
    else if (lastEncoding == 1) {
        lastEncoding = 2; // just switch encodings, flushing recursively overflowed the stack
    }
    #endif
    void (*print)(double) = (void (*)(double)) funcTable[0][14];

    print(num);
}

// INDEX 15
FUNC_ void Console_Clear(void) 
{
    #ifdef PREVENT_ENCODING_MANGLING
    if (lastEncoding == 0) lastEncoding = 2;
    else if (lastEncoding == 1) {
        lastEncoding = 2; // just switch encodings, flushing recursively overflowed the stack
    }
    #endif
    void (*clear)(void) = (void (*)(void)) funcTable[0][15];

    clear();
}

// INDEX 16
FUNC_ void Console_SetCursorPosition(int x, int y) 
{
    void (*setCursor)(int, int) = (void (*)(int, int)) funcTable[0][16];

    setCursor(x, y);
}

/// END CLASS 0 (Console)

// CLASS 1 (Heap)

// INDEX 0
FUNC_ void* Heap_Alloc(unsigned int size) 
{
    void* (*alloc)(unsigned int) = (void* (*)(unsigned int)) funcTable[1][0];

    return alloc(size);
}

// INDEX 1
FUNC_ void Heap_Free(void* ptr) 
{
    void (*free)(void*) = (void (*)(void*)) funcTable[1][1];

    free(ptr);
}

/// END CLASS 1 (Heap)

// CLASS 2 (File)

// INDEX 0
FUNC_ FILE* File_Open(char* filename, char* mode) 
{
    FILE* (*open)(char*, char*) = (FILE* (*)(char*, char*)) funcTable[2][0];

    char* utf16 = ascii_to_utf16(filename);
    char* utf16mode = ascii_to_utf16(mode);

    return open(utf16, utf16mode);
}

// INDEX 1
FUNC_ int File_Close(FILE* file) 
{
    int (*close)(FILE*) = (int (*)(FILE*)) funcTable[2][1];

    return close(file);
}

// INDEX 2
FUNC_ int File_Read(void* buffer, unsigned int size, unsigned int nmemb, FILE* stream) 
{
    int (*read)(void*, unsigned int, unsigned int, FILE*) = (int (*)(void*, unsigned int, unsigned int, FILE*)) funcTable[2][2];
    return read(buffer, size, nmemb, stream);
}

// INDEX 3
FUNC_ int File_Write(void* buffer, unsigned int size, unsigned int nmemb, FILE* stream) 
{
    int (*write)(void*, unsigned int, unsigned int, FILE*) = (int (*)(void*, unsigned int, unsigned int, FILE*)) funcTable[2][3];
    return write(buffer, size, nmemb, stream);
}

// INDEX 4
FUNC_ int File_Seek(FILE* stream, int offset, int whence) 
{
    int (*seek)(FILE*, int, int) = (int (*)(FILE*, int, int)) funcTable[2][4];
    return seek(stream, offset, whence);
}

// INDEX 5
FUNC_ int File_Tell(FILE* stream) 
{
    int (*tell)(FILE*) = (int (*)(FILE*)) funcTable[2][5];
    return tell(stream);
}

// INDEX 6
FUNC_ int File_Flush(FILE* stream) 
{
    int (*flush)(FILE*) = (int (*)(FILE*)) funcTable[2][6];
    return flush(stream);
}

// CLASS 3 (MISC)

FUNC_ void Cosmos_Get_Program_Arguments(int* argc, char*** argv)
{
    void (*getArgs)(int*, char***) = (void (*)(int*, char***)) funcTable[3][0];

    if (getArgs) {
        getArgs(argc, argv);
    } else {
        *argc = 0;
        *argv = NULL;
    }
}

/// NO CLASS FUNCS

FUNC_ void Console_WriteLineA(char* str)
{
    #ifdef PREVENT_ENCODING_MANGLING
    if (lastEncoding == 0) lastEncoding = 1;
    else if (lastEncoding == 2) {
        lastEncoding = 1; // just switch encodings, flushing recursively overflowed the stack
    }
    #endif
    char* utf16 = ascii_to_utf16(str);
    Console_WriteLine16(utf16);
    Heap_Free(utf16);
    return;
}

FUNC_ void Console_WriteA(char* str)
{
    #ifdef PREVENT_ENCODING_MANGLING
    if (lastEncoding == 0) lastEncoding = 1;
    else if (lastEncoding == 2) {
        lastEncoding = 1; // just switch encodings, flushing recursively overflowed the stack
    }
    #endif
    char* utf16 = ascii_to_utf16(str);
    Console_Write16(utf16);
    Heap_Free(utf16);
    return;
}

FUNC_ char* Console_ReadLineA(void) 
{
    #ifdef PREVENT_ENCODING_MANGLING
    if (lastEncoding == 0) lastEncoding = 1;
    else if (lastEncoding == 2) {
        lastEncoding = 1; // just switch encodings, flushing recursively overflowed the stack
    }
    #endif
    char* utf16 = Console_ReadLine16();
    return utf16_to_ascii(utf16);
}