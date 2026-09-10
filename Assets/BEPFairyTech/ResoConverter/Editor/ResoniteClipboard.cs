using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace BEPFairyTech.ResoConverter
{
    /// <summary>Copies an exported package using the same file format as Windows Explorer.</summary>
    public static class ResoniteClipboard
    {
        public static void CopyPackage(string path)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                !path.EndsWith(".resonitepackage", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("コピーする.resonitepackageファイルを指定してください。", nameof(path));
            path = Path.GetFullPath(path);
            if (!File.Exists(path)) throw new FileNotFoundException("出力ファイルが見つかりません。もう一度変換してください。", path);

#if UNITY_EDITOR_WIN
            // Supply both formats: native file import and clipboard providers exposing text only.
            byte[] files = CreateFileDropData(path);
            byte[] text = Encoding.Unicode.GetBytes(path + "\0");
            IntPtr fileMemory = IntPtr.Zero, textMemory = IntPtr.Zero, owner = IntPtr.Zero;
            bool opened = false;
            try
            {
                fileMemory = Allocate(files);
                textMemory = Allocate(text);
                // A real owner is required by SetClipboardData. Message-only windows stay invisible.
                owner = CreateWindowExW(0, "STATIC", "BEP ResoConverter Clipboard", 0,
                    0, 0, 0, 0, new IntPtr(-3), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                if (owner == IntPtr.Zero) throw NativeError("クリップボードの準備に失敗しました。");
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    if (OpenClipboard(owner)) { opened = true; break; }
                    Thread.Sleep(25);
                }
                if (!opened) throw NativeError("クリップボードが使用中です。少し待ってから再試行してください。");
                if (!EmptyClipboard()) throw NativeError("クリップボードを開けませんでした。");
                if (SetClipboardData(15, fileMemory) == IntPtr.Zero) throw NativeError("ファイルをコピーできませんでした。");
                fileMemory = IntPtr.Zero; // Windows owns successfully transferred global memory.
                if (SetClipboardData(13, textMemory) == IntPtr.Zero) throw NativeError("ファイルのパスをコピーできませんでした。");
                textMemory = IntPtr.Zero;
            }
            finally
            {
                if (opened) CloseClipboard();
                if (owner != IntPtr.Zero) DestroyWindow(owner);
                if (fileMemory != IntPtr.Zero) GlobalFree(fileMemory);
                if (textMemory != IntPtr.Zero) GlobalFree(textMemory);
            }
#else
            throw new PlatformNotSupportedException("成果物のクリップボードコピーはWindowsに対応しています。");
#endif
        }

        internal static byte[] CreateFileDropData(string absolutePath)
        {
            // DROPFILES: DWORD pFiles; POINT pt; BOOL fNC; BOOL fWide, followed by UTF-16 paths.
            byte[] names = Encoding.Unicode.GetBytes(absolutePath + "\0\0");
            byte[] data = new byte[20 + names.Length];
            data[0] = 20;
            data[16] = 1;
            Buffer.BlockCopy(names, 0, data, 20, names.Length);
            return data;
        }

#if UNITY_EDITOR_WIN
        private static Exception NativeError(string message) =>
            new InvalidOperationException(message, new Win32Exception(Marshal.GetLastWin32Error()));

        private static IntPtr Allocate(byte[] data)
        {
            IntPtr memory = GlobalAlloc(0x0042, new UIntPtr((uint)data.Length)); // MOVEABLE | ZEROINIT
            if (memory == IntPtr.Zero) throw NativeError("コピー用のメモリを確保できませんでした。");
            IntPtr pointer = GlobalLock(memory);
            if (pointer == IntPtr.Zero) { GlobalFree(memory); throw NativeError("コピー用のメモリを開けませんでした。"); }
            try { Marshal.Copy(data, 0, pointer, data.Length); }
            catch { GlobalUnlock(memory); GlobalFree(memory); throw; }
            GlobalUnlock(memory);
            return memory;
        }

        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenClipboard(IntPtr owner);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseClipboard();
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EmptyClipboard();
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetClipboardData(uint format, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowExW(uint exStyle, string className, string name, uint style,
            int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(IntPtr window);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalLock(IntPtr memory);
        [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalUnlock(IntPtr memory);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr memory);
#endif
    }
}
