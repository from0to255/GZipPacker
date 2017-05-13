using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace GZipTest2
{
    internal class Program
    {
        private const long Magic = 0xDEADDEAF;

        private static int Main(string[] args)
        {
            try
            {
                if (args == null)
                    throw new ArgumentNullException("args");
                if (args.Length < 1)
                    throw new ArgumentException("Invalid arguments: mode is required");

                if (string.Equals(args[0], "compress", StringComparison.OrdinalIgnoreCase))
                {
                    if (args.Length != 3)
                        throw new ArgumentException("Please enter 3 parameters.");
                    Compressor(args[1], args[2]);
                }
                else if (string.Equals(args[0], "decompress", StringComparison.OrdinalIgnoreCase))
                {
                    if (args.Length != 3)
                        throw new ArgumentException("Please enter 3 parameters.");
                    Decompressor(args[1], args[2]);
                }
                else
                    throw new ArgumentException("Invalid arguments: unknown mode");
                return 0;
            }
            catch (AggregateException aex)
            {
                foreach (Exception ex in aex.InnerExceptions)
                    Console.WriteLine(ex.Message);
                return 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return 1;
            }
        }

        private static long _srcLen = long.MaxValue;
        private static long _srcRb;

        private static void ShowReadProgress()
        {
            var srcPos = Interlocked.Read(ref _srcRb);
            Console.SetCursorPosition(0, Console.CursorTop);
            Console.Write("Read progress: {0:0.0}%", srcPos * 100.0 / _srcLen);
        }

        private static void Compressor(string inFile, string outFile)
        {
            if (inFile == null) throw new ArgumentNullException("inFile");
            if (outFile == null) throw new ArgumentNullException("outFile");

            long fileSize = new FileInfo(inFile).Length;
            int blockSize = Math.Max(1024, (int) (fileSize >= 16*1024*1024 ? 1024*1024 : fileSize/16));

            using (var reader = new FileStream(inFile, FileMode.Open, FileAccess.Read, FileShare.None))
            using (var writer = new BinaryWriter(new FileStream(outFile, FileMode.Create, FileAccess.Write, FileShare.None)))
            {
                _srcLen = reader.Length;
                ShowReadProgress();

                writer.Write(Magic);
                long offsetPos = writer.BaseStream.Position;
                writer.Write(0L);

                var packedSizes = new List<int>();

                var processor = new Processor(
                    Environment.ProcessorCount,
                    srcData =>
                        {
                            srcData.Capacity = blockSize;
                            var res = reader.Read(srcData.Data, 0, srcData.Capacity);
                            Interlocked.Add(ref _srcRb, res);
                            return res;
                        },
                    (srcData, srcDataSize, dstData) =>
                        {
                            do
                            {
                                try
                                {
                                    dstData.Capacity = srcData.Capacity;
                                    using (var memoryStream = new MemoryStream(dstData.Data, 0, dstData.Capacity))
                                    {
                                        memoryStream.SetLength(0);
                                        using (var gzipStream = new GZipStream(memoryStream, CompressionMode.Compress, true))
                                            gzipStream.Write(srcData.Data, 0, srcDataSize);
                                        return (int) memoryStream.Length;
                                    }
                                }
                                catch (NotSupportedException)
                                {
                                    dstData.Capacity += (dstData.Capacity >> 3);
                                }
                            }
                            while (true);
                        },
                    (dstData, dstDataSize) =>
                        {
                            writer.Write(dstData.Data, 0, dstDataSize);
                            packedSizes.Add(dstDataSize);
                        });

                Console.CancelKeyPress += (sender, args) =>
                    {
                        processor.CancelRequest();
                        args.Cancel = true;
                    };
                processor.Run();
                while (!processor.WaitForAll(1000))
                {
                    ShowReadProgress();
                }
                ShowReadProgress();

                long tabPos = writer.BaseStream.Position;
                writer.Write(packedSizes.Count);
                foreach (int size in packedSizes)
                    writer.Write(size);
                writer.BaseStream.Position = offsetPos;
                writer.Write(tabPos);
            }
        }

        private static void Decompressor(string inFile, string outFile)
        {
            if (inFile == null) throw new ArgumentNullException("inFile");
            if (outFile == null) throw new ArgumentNullException("outFile");

            using (var reader = new BinaryReader(new FileStream(inFile, FileMode.Open, FileAccess.Read, FileShare.None)))
            {
                _srcLen = reader.BaseStream.Length;
                ShowReadProgress();

                var packedSizes = new List<int>();
                if (reader.ReadInt64() != Magic)
                    throw new InvalidDataException("Unknown file format");
                _srcRb += sizeof (Int64);
                long tabPos = reader.ReadInt64();
                _srcRb += sizeof(Int64);
                long dataPos = reader.BaseStream.Position;
                reader.BaseStream.Position = tabPos;
                for (int n = reader.ReadInt32(); n-- > 0;)
                {
                    packedSizes.Add(reader.ReadInt32());
                    _srcRb += sizeof(Int32);
                }
                _srcRb += sizeof(Int32);
                if (reader.BaseStream.Position != reader.BaseStream.Length)
                    throw new InvalidDataException();
                reader.BaseStream.Position = dataPos;
                ShowReadProgress();

                using (var writer = new StreamWriter(new FileStream(outFile, FileMode.Create, FileAccess.Write, FileShare.None)))
                {
                    int blockCounter = 0;
                    var myClass = new Processor(
                        Environment.ProcessorCount,
                        (srcData) =>
                            {
                                var blockSize = 0; 
                                if (blockCounter < packedSizes.Count) 
                                    blockSize = packedSizes[blockCounter++];
                                srcData.Capacity = blockSize;
                                var res = reader.BaseStream.Read(srcData.Data, 0, blockSize);
                                Interlocked.Add(ref _srcRb, res);
                                return res;
                            },
                        (srcData, srcDataSize, dstData) =>
                            {
                                dstData.Capacity = BitConverter.ToInt32(srcData.Data, srcDataSize - 4);
                                using (var ms = new MemoryStream(srcData.Data, 0, srcDataSize))
                                    using (var gzipStream = new GZipStream(ms, CompressionMode.Decompress, true))
                                    {
                                        return gzipStream.Read(dstData.Data, 0, dstData.Capacity);
                                    }
                            },
                        (dstData, dstDataSize) =>
                            {
                                writer.BaseStream.Write(dstData.Data, 0, dstDataSize);
                            });

                    Console.CancelKeyPress += (sender, args) =>
                        {
                            myClass.CancelRequest();
                            args.Cancel = true;
                        };
                    myClass.Run();
                    while (!myClass.WaitForAll(1000))
                    {
                        ShowReadProgress();
                    }
                    ShowReadProgress();
                }
            }
        }
    }
}