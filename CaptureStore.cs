using System.Security.Cryptography;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace ClinicCameraCapture;

/// <summary>
/// MVP-only storage: no patient IDs, no WM003 paths, no HIS writes.
/// File bytes are verified before a collision-safe same-volume rename.
/// </summary>
public sealed class CaptureStore
{
    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClinicCameraCapture-MVP", "captures");

    public sealed record SavedCapture(string FilePath, long Bytes, string Sha256, uint Width, uint Height);
    public string OutputFolder { get; }
    public CaptureStore(string? outputFolder = null) => OutputFolder = outputFolder ?? Folder;

    public async Task<SavedCapture> SaveAsync(SoftwareBitmap original, string extension, string? patientNumber = null)
    {
        return await SaveCoreAsync(original, extension, patientNumber);
    }

    private async Task<SavedCapture> SaveCoreAsync(SoftwareBitmap original, string extension, string? patientNumber)
    {
        if (extension is not ("png" or "bmp"))
            throw new ArgumentException("Only lossless PNG and uncompressed BMP are supported.", nameof(extension));
        if (patientNumber is not null) CaptureIdentity.Require(patientNumber);

        // Converting to BGRA8 is a pixel layout conversion; never use JPEG on this path.
        using var pixels = SoftwareBitmap.Convert(original, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);
        using var stream = new InMemoryRandomAccessStream();
        var codec = extension == "png" ? BitmapEncoder.PngEncoderId : BitmapEncoder.BmpEncoderId;
        var encoder = await BitmapEncoder.CreateAsync(codec, stream);
        encoder.SetSoftwareBitmap(pixels);
        await encoder.FlushAsync();
        stream.Seek(0);

        // Decode the encoded file in memory and check that RGB pixel data is identical.
        var decoder = await BitmapDecoder.CreateAsync(stream);
        if (decoder.PixelWidth != pixels.PixelWidth || decoder.PixelHeight != pixels.PixelHeight)
            throw new InvalidDataException("Image dimensions changed during encoding.");
        using (var decoded = await decoder.GetSoftwareBitmapAsync(
                   BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore))
        {
            if (!EqualRgb(pixels, decoded))
                throw new InvalidDataException("Lossless round-trip pixel mismatch.");
        }

        stream.Seek(0);
        if (stream.Size is 0 or > 150_000_000)
            throw new InvalidDataException("Unexpected encoded image size.");
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size);
        var encoded = new byte[(int)stream.Size];
        reader.ReadBytes(encoded);
        if (extension == "bmp" && (encoded.Length < 34 || BitConverter.ToUInt32(encoded, 30) != 0))
            throw new InvalidDataException("BMP was not encoded as uncompressed BI_RGB.");

        Directory.CreateDirectory(OutputFolder);
        var tmp = Path.Combine(OutputFolder, "." + Guid.NewGuid().ToString("N") + ".tmp");
        string? saved = null;
        try
        {
            await using (var file = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             65536, FileOptions.WriteThrough))
            {
                await file.WriteAsync(encoded);
                await file.FlushAsync();
            }

            // Check the disk copy, not just successful return from the encoder.
            var onDisk = await File.ReadAllBytesAsync(tmp);
            if (!encoded.AsSpan().SequenceEqual(onDisk))
                throw new IOException("File verification failed after write.");

            // UUID prevents collision even under simultaneous captures; File.Move never overwrites.
            saved = Path.Combine(OutputFolder, patientNumber is null
                ? "TEST_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N")[..8] + "." + extension
                : CaptureIdentity.Filename(patientNumber, extension));
            File.Move(tmp, saved);
            var sha = Convert.ToHexString(SHA256.HashData(onDisk));
            return new SavedCapture(saved, onDisk.Length, sha, (uint)pixels.PixelWidth, (uint)pixels.PixelHeight);
        }
        finally
        {
            // Never delete a committed photo on an error; only disposable temp files.
            if (File.Exists(tmp))
                File.Delete(tmp);
        }
    }

    private static bool EqualRgb(SoftwareBitmap a, SoftwareBitmap b)
    {
        if (a.PixelWidth != b.PixelWidth || a.PixelHeight != b.PixelHeight) return false;
        int length = checked(a.PixelWidth * a.PixelHeight * 4);
        byte[] first = Bytes(a, length);
        byte[] second = Bytes(b, length);
        for (int i = 0; i < length; i += 4)
            if (first[i] != second[i] || first[i + 1] != second[i + 1] || first[i + 2] != second[i + 2])
                return false;
        return true;
    }

    private static byte[] Bytes(SoftwareBitmap bitmap, int length)
    {
        var buffer = new Windows.Storage.Streams.Buffer((uint)length);
        bitmap.CopyToBuffer(buffer);
        var result = new byte[length];
        using var reader = DataReader.FromBuffer(buffer);
        reader.ReadBytes(result);
        return result;
    }
}
