#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;

namespace M2M.SIMBA.Editor
{
    /// <summary>
    /// Reads metadata from SIMBA shell/line binary files without loading
    /// animation payloads.
    ///
    /// Supported formats:
    /// - v3: static topology, legacy layout
    /// - v4: explicit topology-mode field
    /// </summary>
    public static class SIMBABinaryHeaderReader
    {
        private static readonly byte[] ShellV3Magic =
            Encoding.ASCII.GetBytes("SHMSH003");

        private static readonly byte[] ShellV4Magic =
            Encoding.ASCII.GetBytes("SHMSH004");

        private static readonly byte[] LineV3Magic =
            Encoding.ASCII.GetBytes("LNMSH003");

        private static readonly byte[] LegacyLineV3Magic =
            Encoding.ASCII.GetBytes("LINEM003");

        private static readonly byte[] LineV4Magic =
            Encoding.ASCII.GetBytes("LNMSH004");

        private static readonly byte[] LegacyLineV4Magic =
            Encoding.ASCII.GetBytes("LINEM004");

        public static SIMBABinaryHeader Read(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException(
                    "A binary path is required.", nameof(path));

            if (!File.Exists(path))
                throw new FileNotFoundException(
                    "SIMBA binary file not found.", path);

            using FileStream stream = File.OpenRead(path);
            using BinaryReader reader =
                new BinaryReader(stream, Encoding.UTF8, false);

            byte[] magic = reader.ReadBytes(8);
            if (magic.Length != 8)
                throw new EndOfStreamException(
                    "The file is too short to contain a SIMBA header.");

            bool shell;
            bool explicitTopology;

            if (Matches(magic, ShellV3Magic))
            {
                shell = true;
                explicitTopology = false;
            }
            else if (Matches(magic, ShellV4Magic))
            {
                shell = true;
                explicitTopology = true;
            }
            else if (Matches(magic, LineV3Magic) ||
                     Matches(magic, LegacyLineV3Magic))
            {
                shell = false;
                explicitTopology = false;
            }
            else if (Matches(magic, LineV4Magic) ||
                     Matches(magic, LegacyLineV4Magic))
            {
                shell = false;
                explicitTopology = true;
            }
            else
            {
                throw new InvalidDataException(
                    $"Unsupported SIMBA magic '{Encoding.ASCII.GetString(magic)}'.");
            }

            SIMBABinaryHeader header = new SIMBABinaryHeader
            {
                SourcePath = Path.GetFullPath(path),
                Version = reader.ReadInt32(),
                GeometryType = (GeometryType)reader.ReadInt32()
            };

            if (explicitTopology)
            {
                int rawMode = reader.ReadInt32();
                if (!Enum.IsDefined(typeof(SIMBATopologyMode), rawMode))
                    throw new InvalidDataException(
                        $"Invalid topology mode {rawMode}.");

                header.TopologyMode =
                    (SIMBATopologyMode)rawMode;
            }
            else
            {
                header.TopologyMode =
                    SIMBATopologyMode.Static;
            }

            header.FrameCount = reader.ReadInt32();
            header.ValueCount = reader.ReadInt32();
            header.ElementCount = reader.ReadInt32();
            header.FramesPerSecond = reader.ReadSingle();

            if (explicitTopology)
                header.FrameStep = reader.ReadInt32();
            else
                header.FrameStep = 1;

            int fieldCount = reader.ReadInt32();

            ValidateHeader(header, fieldCount, shell);

            for (int i = 0; i < fieldCount; i++)
            {
                header.FieldNames.Add(ReadString(reader));
                header.FieldUnits.Add(ReadString(reader));

                // Global minimum and maximum are part of field metadata.
                reader.ReadSingle();
                reader.ReadSingle();
            }

            return header;
        }

        private static void ValidateHeader(
            SIMBABinaryHeader header,
            int fieldCount,
            bool shellMagic)
        {
            if (header.Version < 3 || header.Version > 4)
                throw new InvalidDataException(
                    $"Unsupported SIMBA version {header.Version}.");

            if (header.FrameCount <= 0)
                throw new InvalidDataException(
                    "Frame count must be positive.");

            if (header.ValueCount <= 0)
                throw new InvalidDataException(
                    "Value count must be positive.");

            if (header.ElementCount < 0)
                throw new InvalidDataException(
                    "Element count cannot be negative.");

            if (!(header.FramesPerSecond > 0f) ||
                float.IsNaN(header.FramesPerSecond) ||
                float.IsInfinity(header.FramesPerSecond))
            {
                throw new InvalidDataException(
                    "Frames per second must be finite and positive.");
            }

            if (fieldCount < 0 || fieldCount > 4096)
                throw new InvalidDataException(
                    $"Invalid field count {fieldCount}.");

            GeometryType expected =
                shellMagic
                    ? GeometryType.ShellMesh
                    : GeometryType.LineMesh;

            if (header.GeometryType != expected)
            {
                throw new InvalidDataException(
                    $"Magic identifies {expected}, but the header " +
                    $"contains {header.GeometryType}.");
            }

        }

        private static string ReadString(BinaryReader reader)
        {
            int byteCount = reader.ReadInt32();

            if (byteCount < 0 || byteCount > 1024 * 1024)
                throw new InvalidDataException(
                    $"Invalid UTF-8 string length {byteCount}.");

            byte[] bytes = reader.ReadBytes(byteCount);
            if (bytes.Length != byteCount)
                throw new EndOfStreamException(
                    "Unexpected end of file while reading a string.");

            return Encoding.UTF8.GetString(bytes);
        }

        private static bool Matches(
            byte[] value,
            byte[] expected)
        {
            if (value.Length != expected.Length)
                return false;

            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] != expected[i])
                    return false;
            }

            return true;
        }
    }
}
#endif
