using System;
using System.IO;
using Google.Protobuf;
using NUnit.Framework;

namespace BEPFairyTech.ResoConverter.Tests
{
    public sealed class ProtobufRuntimeTests
    {
        [TestCase(1)]
        [TestCase(4096)]
        public void StandaloneRuntimeRoundTripsFloatsAndUnicodeAcrossBuffers(int bufferSize)
        {
            float[] floats = { 0, -0f, 1.8f, -42.25f, float.MaxValue, float.Epsilon, float.PositiveInfinity, float.NaN };
            string[] strings = { "", "blink", new string('A', 160), "まばたき / アバター 🧚", new string('骨', 5000) };
            using (var stream = new MemoryStream())
            {
                using (var writer = new CodedOutputStream(stream, bufferSize, true))
                {
                    foreach (float value in floats) writer.WriteFloat(value);
                    writer.WriteDouble(Math.PI);
                    foreach (string value in strings) writer.WriteString(value);
                    writer.Flush();
                }
                using (var incoming = new ChunkedStream(stream.ToArray(), bufferSize))
                using (var reader = new CodedInputStream(incoming))
                {
                    foreach (float value in floats)
                    {
                        float actual = reader.ReadFloat();
                        if (float.IsNaN(value)) Assert.That(float.IsNaN(actual), Is.True);
                        else Assert.That(actual, Is.EqualTo(value));
                    }
                    Assert.That(reader.ReadDouble(), Is.EqualTo(Math.PI));
                    foreach (string value in strings) Assert.That(reader.ReadString(), Is.EqualTo(value));
                    Assert.That(reader.IsAtEnd, Is.True);
                }
            }
        }

        private sealed class ChunkedStream : MemoryStream
        {
            private readonly int chunkSize;
            internal ChunkedStream(byte[] bytes, int chunkSize) : base(bytes) { this.chunkSize = chunkSize; }
            public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, chunkSize));
        }
    }
}
