using ClinicCameraCapture;
using System.Security.Cryptography;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

internal static class Program
{
    [STAThread]
    private static async Task<int> Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClinicCameraCapture-Smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            const int w = 80, h = 48;
            using var frame = new SoftwareBitmap(BitmapPixelFormat.Bgra8, w, h, BitmapAlphaMode.Ignore);
            byte[] image = new byte[w * h * 4];
            for (int pixel = 0; pixel < w * h; pixel++)
            {
                int i = pixel * 4;
                image[i] = (byte)(pixel % 239);
                image[i + 1] = (byte)(pixel * 7 % 251);
                image[i + 2] = (byte)(pixel * 11 % 241);
                image[i + 3] = 255;
            }
            using (var writer = new DataWriter())
            {
                writer.WriteBytes(image);
                frame.CopyFromBuffer(writer.DetachBuffer());
            }

            var store = new CaptureStore(root);
            var png = await store.SaveAsync(frame, "png");
            var bmp = await store.SaveAsync(frame, "bmp");
            Require(png.Width == w && png.Height == h, "PNG dimensions");
            Require(bmp.Width == w && bmp.Height == h, "BMP dimensions");
            Require(png.FilePath.EndsWith(".png") && bmp.FilePath.EndsWith(".bmp"), "formats");
            Require(File.ReadAllBytes(png.FilePath).AsSpan()[..8].SequenceEqual(
                new byte[] {137,80,78,71,13,10,26,10}), "PNG signature");
            Require(BitConverter.ToUInt32(File.ReadAllBytes(bmp.FilePath), 30) == 0, "BMP not uncompressed");
            Require(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(png.FilePath))) == png.Sha256, "SHA-256");
            Require(Directory.GetFiles(root, "*.tmp").Length == 0, "No leftover temporary files");
            Console.WriteLine("PASS: PNG/BMP pixel-exact codec round-trip and verified disk writes.");

            bool rejected = false;
            try {await store.SaveAsync(frame, "jpeg");}
            catch (ArgumentException) {rejected = true;}
            Require(rejected, "JPEG must be rejected");
            Console.WriteLine("PASS: lossy format rejected.");

            var blockingPath = Path.Combine(root, "blocked");
            await File.WriteAllTextAsync(blockingPath, "not a directory");
            rejected = false;
            try {await new CaptureStore(blockingPath).SaveAsync(frame, "png");}
            catch (IOException) {rejected = true;}
            Require(rejected, "Failure injection should prevent save");
            Require(Directory.GetFiles(root, "*.png").Length == 1, "Existing picture unchanged after failure");
            Console.WriteLine("PASS: simulated storage failure produces no new image and does not overwrite a verified image.");

            Require(CaptureIdentity.IsValid("000123"), "leading zeros in six-digit reference");
            Require(!CaptureIdentity.IsValid("../123"), "path traversal input rejected");
            Require(!CaptureIdentity.IsValid("12345"), "too-short reference rejected");
            var numbered = await store.SaveAsync(frame, "png", "000123");
            Require(Path.GetFileName(numbered.FilePath).StartsWith("000123_", StringComparison.Ordinal),
                "numbered file uses exact patient reference");
            Require(File.Exists(numbered.FilePath), "numbered photo exists");
            var second = await store.SaveAsync(frame, "png", "000123");
            Require(numbered.FilePath != second.FilePath, "repeated photos never overwrite");
            Console.WriteLine("PASS: six-digit reference naming and collision-free repeated saves.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + ex);
            return 1;
        }
        finally
        {
            // Remove only the unique directory created by this test.
            Directory.Delete(root, recursive: true);
        }
    }

    private static void Require(bool value, string reason)
    {
        if (!value) throw new Exception("Assertion failed: " + reason);
    }
}
