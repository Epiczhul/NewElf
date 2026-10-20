using System;
using System.Runtime.InteropServices;

namespace NewElf.Str
{
    public unsafe class UString
    {
        private char* internalBuffer;
        private int length;

        public static char* ConvertManaged(string str)
        {
            char* buffer = (char*)NativeMemory.Alloc((nuint)((str.Length + 1) * 2));
            for (int i = 0; i < str.Length; i++)
            {
                buffer[i] = str[i];
            }
            buffer[str.Length] = '\0';
            return buffer;
        }
    }
}
