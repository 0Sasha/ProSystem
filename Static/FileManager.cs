using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace ProSystem;

internal static class FileManager
{
    public static void ArchiveFiles(string directory, string partFileName, string archName, bool deleteSourceFiles)
    {
        if (string.IsNullOrEmpty(directory)) throw new ArgumentNullException(nameof(directory));
        if (string.IsNullOrEmpty(partFileName)) throw new ArgumentNullException(nameof(partFileName));
        if (string.IsNullOrEmpty(archName)) throw new ArgumentNullException(nameof(archName));
        if (!Directory.Exists(directory)) throw new ArgumentException("Directory does not exist", nameof(directory));

        var paths = Directory.GetFiles(directory).Where(x => x.Contains(partFileName) && !x.Contains(".zip"));
        if (!paths.Any()) throw new ArgumentException("Files are not found", partFileName);

        var newDir = directory + "/" + archName;
        if (File.Exists(newDir)) File.Delete(newDir);
        Directory.CreateDirectory(newDir);
        foreach (var path in paths) File.Copy(path, newDir + "/" + path.Replace(directory, ""));

        if (File.Exists(newDir + ".zip")) File.Delete(newDir + ".zip");
        ZipFile.CreateFromDirectory(newDir, newDir + ".zip", CompressionLevel.SmallestSize, false);
        Directory.Delete(newDir, true);
        if (deleteSourceFiles) foreach (var path in paths) File.Delete(path);
    }
}
