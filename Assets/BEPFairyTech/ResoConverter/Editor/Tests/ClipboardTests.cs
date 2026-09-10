using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using NUnit.Framework;

namespace BEPFairyTech.ResoConverter.Tests
{
    public sealed class ClipboardTests
    {
        [Test]
        public void InvalidPackageRequestsAreRejectedBeforeClipboardAccess()
        {
            foreach (string path in new[] { null, "", "  ", "model.fbx", "model.resonitepackage.zip" })
            {
                var error = Assert.Throws<ArgumentException>(() => ResoniteClipboard.CopyPackage(path));
                Assert.That(error.ParamName, Is.EqualTo("path"));
            }
        }

        [Test]
        public void MissingPackageIsRejectedBeforeClipboardAccess()
        {
            string path = Path.Combine(Path.GetTempPath(), "BEP-missing-" + Guid.NewGuid().ToString("N") + ".resonitepackage");
            Assert.That(File.Exists(path), Is.False);
            var error = Assert.Throws<FileNotFoundException>(() => ResoniteClipboard.CopyPackage(path));
            Assert.That(error.FileName, Is.EqualTo(Path.GetFullPath(path)));
        }

        [Test]
        public void FileDropUsesUnicodeHeaderAndDoubleNullListTerminator()
        {
            const string path = "D:\\アバター 出力\\星🌟.resonitepackage";
            byte[] data = ResoniteClipboard.CreateFileDropData(path);
            using (var reader = new BinaryReader(new MemoryStream(data)))
            {
                uint namesOffset = reader.ReadUInt32();
                Assert.That(namesOffset, Is.EqualTo(20));
                Assert.That(reader.ReadInt32(), Is.Zero, "DROPFILES.pt.x");
                Assert.That(reader.ReadInt32(), Is.Zero, "DROPFILES.pt.y");
                Assert.That(reader.ReadInt32(), Is.Zero, "DROPFILES.fNC");
                Assert.That(reader.ReadInt32(), Is.EqualTo(1), "DROPFILES.fWide");
                var names = new UnicodeEncoding(false, false, true).GetString(data, (int)namesOffset, data.Length - (int)namesOffset);
                Assert.That(names, Is.EqualTo(path + "\0\0"));
            }
        }

#if UNITY_EDITOR_WIN
        [Test]
        public void WindowsShellDecodesOneUnicodePackageFromFileDrop()
        {
            const string path = "D:\\日本語 空白\\アバター🌟.resonitepackage";
            byte[] data = ResoniteClipboard.CreateFileDropData(path);
            IntPtr memory = GlobalAlloc(0x0042, new UIntPtr((uint)data.Length));
            Assert.That(memory, Is.Not.EqualTo(IntPtr.Zero));
            try
            {
                IntPtr pointer = GlobalLock(memory);
                Assert.That(pointer, Is.Not.EqualTo(IntPtr.Zero));
                try { Marshal.Copy(data, 0, pointer, data.Length); }
                finally { GlobalUnlock(memory); }

                // Ask the real Windows shell to decode the payload. This handle never enters
                // the clipboard; GlobalFree below releases memory still owned by this test.
                Assert.That(DragQueryFileW(memory, uint.MaxValue, null, 0), Is.EqualTo(1));
                uint length = DragQueryFileW(memory, 0, null, 0);
                Assert.That(length, Is.EqualTo(path.Length));
                var filename = new StringBuilder((int)length + 1);
                Assert.That(DragQueryFileW(memory, 0, filename, (uint)filename.Capacity), Is.EqualTo(length));
                Assert.That(filename.ToString(), Is.EqualTo(path));
            }
            finally { GlobalFree(memory); }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint DragQueryFileW(IntPtr drop, uint fileIndex, StringBuilder filename, uint length);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr memory);
        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalUnlock(IntPtr memory);
        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalFree(IntPtr memory);
#endif
    }
}
