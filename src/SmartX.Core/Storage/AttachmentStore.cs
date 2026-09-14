using System.Security.Cryptography;
using SmartX.Core.Domain;

namespace SmartX.Core.Storage;

public sealed class AttachmentStore
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".json", ".yaml", ".yml", ".toml", ".ini", ".cfg", ".conf", ".xml", ".csv",

        ".log", ".txt", ".ndjson",

        ".jpg", ".jpeg", ".png", ".webp", ".gif",

        ".zip", ".pcap"
    };

    private readonly string _root;

    public AttachmentStore(string rootDirectory, long maxBytes = 25L * 1024 * 1024)
    {
        _root = rootDirectory;
        MaxBytes = maxBytes;
        Directory.CreateDirectory(_root);
    }

    public long MaxBytes { get; }

    public string RootDirectory => _root;

    public static bool IsAllowed(string fileName)
        => AllowedExtensions.Contains(Path.GetExtension(fileName));

    public static string ClassifyKind(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" => "DeploymentPhoto",
        ".log" or ".txt" or ".ndjson" => "HardwareLog",
        ".pcap" => "PacketCapture",
        ".zip" => "Archive",
        _ => "Configuration"
    };

    public async Task<AttachmentRecord> SaveAsync(
        string macAddress,
        string fileName,
        string contentType,
        Stream source,
        CancellationToken cancellationToken = default)
    {
        var safeName = Path.GetFileName(fileName);

        if (string.IsNullOrWhiteSpace(safeName))
        {
            throw new ArgumentException("The upload has no usable file name.", nameof(fileName));
        }

        if (!IsAllowed(safeName))
        {
            throw new InvalidOperationException(
                $"'{Path.GetExtension(safeName)}' is not an accepted attachment type for a sensor profile.");
        }

        var nodeFolder = Path.Combine(_root, Sanitise(macAddress));
        Directory.CreateDirectory(nodeFolder);

        var id = Guid.NewGuid().ToString("N");
        var storedPath = Path.Combine(nodeFolder, $"{id}{Path.GetExtension(safeName)}");

        long written = 0;
        // the fingerprint is worked out on the same pass, so the bytes are only ever read once
        using var sha = SHA256.Create();

        // copied through in small chunks so a large photo never has to sit in memory in one piece
        var buffer = new byte[80 * 1024];

        await using (var destination = new FileStream(
            storedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: buffer.Length, useAsync: true))
        {
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                written += read;

                if (written > MaxBytes)
                {
                    destination.Close();
                    TryDelete(storedPath);
                    throw new InvalidOperationException(
                        $"Attachment exceeds the {MaxBytes / (1024 * 1024)} MB per-file limit.");
                }

                sha.TransformBlock(buffer, 0, read, null, 0);
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
        }

        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

        return new AttachmentRecord
        {
            Id = id,
            MacAddress = macAddress,
            FileName = safeName,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            SizeBytes = written,
            Kind = ClassifyKind(safeName),
            StoredPath = storedPath,
            Checksum = Convert.ToHexString(sha.Hash ?? Array.Empty<byte>()),
            UploadedUtc = DateTimeOffset.UtcNow
        };
    }

    public bool TryOpen(AttachmentRecord record, out Stream stream)
    {
        stream = Stream.Null;

        if (!File.Exists(record.StoredPath))
        {
            return false;
        }

        stream = new FileStream(record.StoredPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 80 * 1024, useAsync: true);
        return true;
    }

    public void Delete(AttachmentRecord record) => TryDelete(record.StoredPath);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
    }

    private static string Sanitise(string value)
    {
        Span<char> buffer = stackalloc char[value.Length];
        var invalid = Path.GetInvalidFileNameChars();

        for (var i = 0; i < value.Length; i++)
        {
            buffer[i] = Array.IndexOf(invalid, value[i]) >= 0 || value[i] == ':' ? '-' : value[i];
        }

        return new string(buffer);
    }
}
