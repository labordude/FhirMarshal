using System.IO.Compression;
using System.Text.Json;

namespace FhirMarshal.Models;

public interface IBundle
{
    /// <summary>
    ///     Gets the total count of items in the bundle.
    /// </summary>
    /// <returns>The total count of items.</returns>
    int Count { get; set; }

    /// <summary>
    ///     Gets the next item in the bundle.
    /// </summary>
    /// <returns>A dictionary representing the next item and an error if one occurs.</returns>
    (Dictionary<string, JsonElement> Item, Exception? Error) Next();

    /// <summary>
    ///     Closes the bundle, releasing any resources.
    /// </summary>
    void Close();
}

public enum BundleType
{
    Ndjson,
    Fhir,
    SingleResource,
    UnknownBundleType,
}

public class BundleFile : Stream
{
    private readonly string _filePath;
    private FileStream _fileStream;
    private StreamReader _reader;

    public BundleFile(string filePath)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));

        _fileStream = new FileStream(_filePath, FileMode.Open, FileAccess.Read);

        if (IsGzipFile())
        {
            Gzr = new GZipStream(_fileStream, CompressionMode.Decompress, true);
            _reader = new StreamReader(Gzr);
        }
        else
        {
            _reader = new StreamReader(_fileStream);
        }
    }

    public FileStream? File => _fileStream;
    public GZipStream? Gzr { get; private set; }

    // Implementing other Stream methods
    public override bool CanRead => _reader.BaseStream.CanRead;
    public override bool CanSeek => _reader.BaseStream.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _reader.BaseStream.Length;

    public override long Position
    {
        get => _reader.BaseStream.Position;
        set => _reader.BaseStream.Position = value;
    }

    private bool IsGzipFile()
    {
        var buffer = new byte[2];
        _fileStream.ReadExactly(buffer, 0, 2);
        _fileStream.Seek(0, SeekOrigin.Begin);

        return buffer[0] == 0x1F && buffer[1] == 0x8B;
    }

    public string ReadLine()
    {
        return _reader?.ReadLine() ?? string.Empty;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return _reader.BaseStream.Read(buffer, offset, count);
    }

    public override void Flush()
    {
        _reader.BaseStream.Flush();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        return _reader.BaseStream.Seek(offset, origin);
    }

    public override void SetLength(long value)
    {
        _reader.BaseStream.SetLength(value);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException("Write is not supported.");
    }

    public void Rewind()
    {
        if (_fileStream == null)
            throw new Exception("File is not initialized");

        _fileStream.Seek(0, SeekOrigin.Begin);

        if (Gzr != null)
        {
            Gzr?.Dispose();
            Gzr = new GZipStream(_fileStream, CompressionMode.Decompress);
            _reader = new StreamReader(Gzr);
        }
        else
        {
            _reader = new StreamReader(_fileStream);
        }
    }
}
