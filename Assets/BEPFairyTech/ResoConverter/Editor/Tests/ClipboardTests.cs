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
        public void RelativePackagePathBecomesPlainAbsoluteText()
        {
            string directory = Path.Combine("Temp", "BEP-clipboard-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "model.ResonitePackage");
            try
            {
                File.WriteAllBytes(path, new byte[] { 1 });
                Assert.That(Path.IsPathRooted(path), Is.False);
                byte[] data = ResoniteClipboard.CreatePackagePathTextData(path);
                string decoded = new UnicodeEncoding(false, false, true).GetString(data);
                Assert.That(decoded, Is.EqualTo(Path.GetFullPath(path) + "\0"),
                    "Clipboard text must be exactly the absolute path with one NUL, without quotes, BOM or a URI prefix.");
            }
            finally { File.Delete(path); Directory.Delete(directory); }
        }

        [Test]
        public void UnicodePackagePathRoundTripsThroughNullTerminatedTextReader()
        {
            string path = Path.Combine(Path.GetTempPath(), "BEP-日本語 空白🌟-" + Guid.NewGuid().ToString("N") + ".resonitepackage");
            IntPtr memory = IntPtr.Zero;
            try
            {
                File.WriteAllBytes(path, new byte[] { 1 });
                string expected = Path.GetFullPath(path);
                byte[] data = ResoniteClipboard.CreatePackagePathTextData(path);
                Assert.That(data.Length, Is.EqualTo((expected.Length + 1) * 2));
                Assert.That(data[data.Length - 2], Is.Zero);
                Assert.That(data[data.Length - 1], Is.Zero);
                // Read the native Unicode representation without changing the system clipboard.
                memory = Marshal.AllocHGlobal(data.Length);
                Marshal.Copy(data, 0, memory, data.Length);
                Assert.That(Marshal.PtrToStringUni(memory), Is.EqualTo(expected));
            }
            finally
            {
                if (memory != IntPtr.Zero) Marshal.FreeHGlobal(memory);
                File.Delete(path);
            }
        }
    }
}
