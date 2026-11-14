using QuickConvert.Core.IO;

namespace QuickConvert.Engines.Docs.Tests;

public class CollisionAndPromotionTests
{
    [Fact]
    public void File_naming_follows_photo_1_png_rule()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\o\photo.png", @"C:\o\photo (1).png" };
        Assert.Equal(@"C:\o\photo (2).png", CollisionNamer.NextAvailableFile(@"C:\o\photo.png", taken.Contains).Replace('/', '\\'));
        Assert.Equal(@"C:\o\free.png", CollisionNamer.NextAvailableFile(@"C:\o\free.png", taken.Contains).Replace('/', '\\'));
    }

    [Fact]
    public void Directory_naming_ignores_dots_in_the_name()
    {
        var p = Path.Combine("o", "archive.v2");
        var next = CollisionNamer.NextAvailableDirectory(p, x => x == p);
        Assert.Equal(p + " (1)", next);
    }

    [Fact]
    public void Entries_with_same_name_are_numbered_case_insensitively_in_the_same_folder()
    {
        var set = new RelativePathSet();
        Assert.Equal("img/Photo.png", set.ReserveFile("img/Photo.png"));
        Assert.Equal("img/photo (1).png", set.ReserveFile("img/photo.png"));
        Assert.Equal("img/PHOTO (2).png", set.ReserveFile("img/PHOTO.png"));
        Assert.Equal("other/photo.png", set.ReserveFile("other/photo.png")); // structure preserved
    }

    [Fact]
    public void A_file_blocking_a_folder_is_a_structure_error()
    {
        var set = new RelativePathSet();
        set.ReserveFile("a");
        Assert.Throws<ArchiveStructureException>(() => set.ReserveFile("a/b.txt"));
    }

    [Fact]
    public void A_folder_blocking_a_file_renames_the_file()
    {
        var set = new RelativePathSet();
        set.ReserveFile("a/b.txt");
        Assert.Equal("a (1)", set.ReserveFile("a"));
    }

    [Fact]
    public void PromoteDirectory_numbers_and_never_merges_or_deletes()
    {
        using var t = new TempDir();
        var existing = t.Sub("folder"); Directory.CreateDirectory(existing);
        File.WriteAllText(Path.Combine(existing, "keep.txt"), "keep");

        var tmp = t.Sub(".tmp"); Directory.CreateDirectory(tmp);
        File.WriteAllText(Path.Combine(tmp, "new.txt"), "new");

        var final = PromotionHelper.PromoteDirectory(tmp, existing);
        Assert.Equal(existing + " (1)", final);
        Assert.True(File.Exists(Path.Combine(existing, "keep.txt")));
        Assert.False(File.Exists(Path.Combine(existing, "new.txt")));
        Assert.True(File.Exists(Path.Combine(final, "new.txt")));
        Assert.False(Directory.Exists(tmp));
    }

    [Fact]
    public void PromoteFile_numbers_unless_overwrite()
    {
        using var t = new TempDir();
        var target = t.Sub("out.md"); File.WriteAllText(target, "old");

        var tmp1 = t.Sub("t1"); File.WriteAllText(tmp1, "one");
        var numbered = PromotionHelper.PromoteFile(tmp1, target, overwrite: false);
        Assert.Equal(t.Sub("out (1).md"), numbered);
        Assert.Equal("old", File.ReadAllText(target));

        var tmp2 = t.Sub("t2"); File.WriteAllText(tmp2, "two");
        Assert.Equal(target, PromotionHelper.PromoteFile(tmp2, target, overwrite: true));
        Assert.Equal("two", File.ReadAllText(target));
    }
}
