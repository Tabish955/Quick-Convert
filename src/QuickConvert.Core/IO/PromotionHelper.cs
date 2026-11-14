namespace QuickConvert.Core.IO;

/// <summary>
/// Additive helpers for temp-then-promote. Files and folders are only ever moved into place once
/// complete. Folders are never merged or deleted (D4); "overwrite" applies to files only.
/// If your tree's OutputPromotion.Promote already covers PromoteFile, call that instead.
/// </summary>
public static class PromotionHelper
{
    public static string PromoteFile(string tempFile, string desiredPath, bool overwrite)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(desiredPath))!);
        if (overwrite)
        {
            File.Move(tempFile, desiredPath, overwrite: true);
            return desiredPath;
        }

        for (var attempt = 0; attempt < 20; attempt++)
        {
            var target = CollisionNamer.NextAvailableFile(desiredPath, p => File.Exists(p) || Directory.Exists(p));
            try
            {
                File.Move(tempFile, target, overwrite: false);
                return target;
            }
            catch (IOException) when (File.Exists(target)) { /* lost a race, number again */ }
        }
        throw new IOException("Could not find a free output file name.");
    }

    public static string PromoteDirectory(string tempDir, string desiredPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(desiredPath))!);
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var target = CollisionNamer.NextAvailableDirectory(desiredPath, p => Directory.Exists(p) || File.Exists(p));
            try
            {
                Directory.Move(tempDir, target);
                return target;
            }
            catch (IOException) when (Directory.Exists(target)) { /* lost a race, number again */ }
        }
        throw new IOException("Could not find a free output folder name.");
    }
}
