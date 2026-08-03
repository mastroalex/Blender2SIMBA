#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;

namespace M2M.SIMBA.Editor
{
    public static class SIMBABinaryHeaderReader
    {
        public static SIMBABinaryHeader Read(string path)
        {
            using FileStream stream = File.OpenRead(path);
            using BinaryReader reader = new BinaryReader(stream, Encoding.UTF8, false);
            string magic = Encoding.ASCII.GetString(reader.ReadBytes(8));
            int version = reader.ReadInt32();
            if (version != 3)
                throw new InvalidDataException($"SIMBA binary version {version} is not supported. Re-export with the v0.2 Python converter.");

            GeometryType storedType = (GeometryType)reader.ReadInt32();
            if (magic == "SHMSH003" && storedType != GeometryType.ShellMesh)
                throw new InvalidDataException("ShellMesh magic and GeometryType do not match.");
            if (magic == "LINEM003" && storedType != GeometryType.LineMesh)
                throw new InvalidDataException("LineMesh magic and GeometryType do not match.");
            if (magic != "SHMSH003" && magic != "LINEM003")
                throw new InvalidDataException($"Unknown SIMBA magic '{magic}'.");

            SIMBABinaryHeader header = new SIMBABinaryHeader
            {
                SourcePath = path,
                Version = version,
                GeometryType = storedType,
                FrameCount = reader.ReadInt32(),
                ValueCount = reader.ReadInt32(),
                ElementCount = reader.ReadInt32(),
                FramesPerSecond = reader.ReadSingle()
            };

            if (storedType == GeometryType.LineMesh)
                header.FrameStep = reader.ReadInt32();

            int fieldCount = reader.ReadInt32();
            if (header.FrameCount <= 0 || header.ValueCount <= 0 || header.ElementCount <= 0 || fieldCount <= 0)
                throw new InvalidDataException("The SIMBA header contains invalid counts.");

            for (int i = 0; i < fieldCount; i++)
            {
                header.FieldNames.Add(ReadString(reader));
                header.FieldUnits.Add(ReadString(reader));
                reader.ReadSingle(); // global min
                reader.ReadSingle(); // global max
            }
            return header;
        }

        private static string ReadString(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length < 0 || length > 1024 * 1024)
                throw new InvalidDataException("Invalid string length in SIMBA header.");
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
#endif
