using System;
using System.IO;
using System.Runtime.InteropServices;

namespace HgAdapter {

    static class Win32 {

        public static DateTime GetFileChangeTime(string path) {
            using(var file = new FileStream(path, FileMode.Open)) {
                var fileInfo = new FILE_BASIC_INFO();
#pragma warning disable 618
                GetFileInformationByHandleEx(file.Handle, 0, out fileInfo, (uint)Marshal.SizeOf(fileInfo));
#pragma warning restore 618
                return DateTime.FromFileTime(fileInfo.ChangeTime);
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetFileInformationByHandleEx(IntPtr hFile, int infoClass, out FILE_BASIC_INFO fileInfo, uint dwBufferSize);

        [StructLayout(LayoutKind.Sequential)]
        struct FILE_BASIC_INFO {
            public long CreationTime;
            public long LastAccessTime;
            public long LastWriteTime;
            public long ChangeTime;
            public uint FileAttributes;
        }
    }

}
