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
            Require(CaptureIdentity.IsValid("12345"), "short reference accepted and padded");
            Require(CaptureIdentity.Require("90") == "000090", "six-digit left padding");
            var numbered = await store.SaveAsync(frame, "png", "000123", captureDate: new DateOnly(2026, 10, 23));
            Require(Path.GetFileName(numbered.FilePath).StartsWith("000123_", StringComparison.Ordinal),
                "numbered file uses exact patient reference");
            Require(Path.GetDirectoryName(numbered.FilePath) == Path.Combine(root, "000123_20261023"),
                "patient 000123 photo stored only in matching subfolder");
            Require(File.Exists(numbered.FilePath), "numbered photo exists");
            var second = await store.SaveAsync(frame, "png", "000123", captureDate: new DateOnly(2026, 10, 23));
            Require(numbered.FilePath != second.FilePath, "repeated photos never overwrite");
            Console.WriteLine("PASS: six-digit reference naming and collision-free repeated saves.");

            // Requested regression: short decimal IDs must zero-pad to six digits;
            // sequential still photos are named 000090_01.png ... 000090_99.png.
            Require(CaptureIdentity.IsValid("90"), "accept short numeric patient reference");
            var first90 = await store.SaveAsync(frame, "png", "90", captureDate: new DateOnly(2026, 10, 23));
            var patient90Folder = Path.Combine(root, "000090_20261023");
            Require(Path.GetDirectoryName(first90.FilePath) == patient90Folder,
                "photo must go in the six-digit patient subfolder");
            Require(Directory.Exists(patient90Folder), "patient subfolder created on first photo");
            Require(Path.GetFileName(first90.FilePath) == "000090_01.png", "first numbered photo");
            var second90 = await store.SaveAsync(frame, "bmp", "000090", captureDate: new DateOnly(2026, 10, 23));
            Require(Path.GetFileName(second90.FilePath) == "000090_02.bmp", "sequence shared across image formats");
            Require(Path.GetDirectoryName(second90.FilePath) == patient90Folder,
                "same patient photo format shares same folder");
            Require(!File.Exists(Path.Combine(root, "000090_01.png")),
                "photo must not be accidentally saved at the configured root");
            Require(store.PatientFolder("90", new DateOnly(2026, 10, 23)) == patient90Folder,
                "video and photo share the same normalized folder resolver");
            Console.WriteLine("PASS: 6-digit zero padding, predictable 01-99 photo filenames.");

            var batch = new PhotoBatch();
            batch.Add("90", first90.FilePath);
            batch.Add("000090", second90.FilePath);
            batch.Add("91", "different-patient-file.png");
            Require(batch.For("90").Count == 2, "same normalized reference grouped together");
            Require(batch.For("000090")[1] == second90.FilePath, "second photo accessible in current batch");
            Require(batch.For("91").Count == 1 && batch.For("90").Count == 2,
                "switching patient reference must not mix batches");
            Console.WriteLine("PASS: in-run per-patient gallery grouping and switching.");

            await File.WriteAllBytesAsync(Path.Combine(patient90Folder, "000090_03.jpg"), new byte[] {1});
            var fourth = await store.SaveAsync(frame, "png", "90", captureDate: new DateOnly(2026, 10, 23));
            Require(Path.GetFileName(fourth.FilePath) == "000090_04.png",
                "sequence cannot reuse an occupied number in another extension");
            for (int n = 5; n <= 99; n++)
                await File.WriteAllBytesAsync(Path.Combine(patient90Folder, $"000090_{n:00}.png"), new byte[] {1});
            bool full = false;
            try { await store.SaveAsync(frame, "png", "90", captureDate: new DateOnly(2026, 10, 23)); }
            catch (IOException) { full = true; }
            Require(full, "01-99 full must refuse without overwrite");
            Require(File.Exists(first90.FilePath), "01 must still exist after exhaustion");
            Console.WriteLine("PASS: cross-extension collision detection and safe 99 limit.");

            // Separate patient photos and recordings must never share a leaf folder.
            var first91 = await store.SaveAsync(frame, "png", "91", captureDate: new DateOnly(2026, 10, 23));
            Require(Path.GetFileName(first91.FilePath) == "000091_01.png",
                "different patient sequence starts at 01 independently");
            Require(Path.GetDirectoryName(first91.FilePath) == Path.Combine(root, "000091_20261023"),
                "different patient uses another folder");
            Require(store.PatientFolder("000091", new DateOnly(2026, 10, 23)) != store.PatientFolder("90", new DateOnly(2026, 10, 23)),
                "folder normalization isolates separate patients");
            var alternative = new CaptureStore(Path.Combine(root, "alternate-root"));
            var first90Elsewhere = await alternative.SaveAsync(frame, "png", "90", captureDate: new DateOnly(2026, 10, 23));
            Require(Path.GetFileName(first90Elsewhere.FilePath) == "000090_01.png" &&
                    Path.GetDirectoryName(first90Elsewhere.FilePath) == Path.Combine(root, "alternate-root", "000090_20261023"),
                "different configured root creates independent patient folder and numbering");
            Require(File.Exists(first90.FilePath), "new saves never relocate or delete existing photos");
            Console.WriteLine("PASS: per-patient subfolders, independent sequence and configured root.");

            // Date is the local capture date, not the app start date: new day
            // creates a distinct folder with its own numbering, same day reuses.
            var nextDay = new DateOnly(2026, 10, 24);
            var nextDayFirst = await store.SaveAsync(frame, "png", "90", captureDate: nextDay);
            Require(Path.GetDirectoryName(nextDayFirst.FilePath) == Path.Combine(root, "000090_20261024"),
                "following date uses separate patient plus date folder");
            Require(Path.GetFileName(nextDayFirst.FilePath) == "000090_01.png",
                "numbering restarts for same patient on a different date");
            var newSession = new CaptureStore(root);
            var nextDaySecond = await newSession.SaveAsync(frame, "bmp", "000090", captureDate: nextDay);
            Require(Path.GetDirectoryName(nextDaySecond.FilePath) == Path.GetDirectoryName(nextDayFirst.FilePath),
                "second app instance reuses folder on same date");
            Require(Path.GetFileName(nextDaySecond.FilePath) == "000090_02.bmp",
                "second app instance continues numbering rather than replacing prior file");
            Require(File.Exists(first90.FilePath) && File.Exists(nextDayFirst.FilePath),
                "new date does not delete or overwrite earlier date");
            Console.WriteLine("PASS: patient+date folders, next-day rollover, and cross-session numbering.");

            var customOutput = OutputFolderPolicy.Normalize(Path.Combine(root, "custom"));
            Require(Path.IsPathFullyQualified(customOutput), "custom folder must be absolute");
            // Pure normalization only: never create/read/modify either location.
            Require(OutputFolderPolicy.Normalize(@"D:\WM003\IMAGES") == @"D:\WM003\IMAGES",
                "production directory path no longer blocked by app policy");
            Require(OutputFolderPolicy.Normalize(@"\\fake-server\share\images") == @"\\fake-server\share\images",
                "UNC network share path accepted without connecting to it");
            Require(OutputFolderPolicy.Normalize(@"C:\") == @"C:\",
                "drive root path accepted without writing to it");
            Require(OutputFolderPolicy.Normalize(@".\test-captures") == Path.GetFullPath(@".\test-captures"),
                "relative path resolves to an absolute destination");
            Require(OutputFolderPolicy.Normalize(Path.Combine(root, "custom"))
                    == Path.GetFullPath(Path.Combine(root, "custom")),
                "custom test folder accepted without creating it");
            bool invalid = false;
            try { OutputFolderPolicy.Normalize(" "); }
            catch (ArgumentException) { invalid = true; }
            Require(invalid, "blank output path must still fail");
            Console.WriteLine("PASS: unrestricted folder selection including UNC, roots and HIS-looking paths.");
            await PatientDbfSmoke.RunAsync(root);
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
