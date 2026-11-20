using System;
using System.Collections.Generic;
using System.IO;
using PhotoImport;

static class Test
{
    static int fails;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) fails++; }

    static int Main()
    {
        string dest = Path.Combine(Path.GetTempPath(), "pi2-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dest);
        var dev = new DemoDevice { FailEvery = 1000 };
        var items = new List<Item>();
        dev.Enumerate(items.Add, delegate { return false; });
        Check(items.Count == 77, "demo phone lists 77 items");

        // an index line written by version 1 must still be recognised
        Item old = items[5];
        File.WriteAllText(Path.Combine(dest, Library.IndexName),
            (old.RelPath + Path.GetFileNameWithoutExtension(old.Name)).ToLowerInvariant() + "|" + old.Size + Environment.NewLine);
        var lib = new Library(dest) { RetryPauseMs = 1 };
        Check(lib.Has(old) && !lib.Has(items[0]), "version 1 index entries are honoured");

        string final;
        Item a = items[0];
        ImportOutcome o = lib.Import(dev, a, delegate { }, delegate { return false; }, out final);
        string expect = Path.Combine(dest, a.Date.ToString("yyyy"), a.Date.ToString("yyyy-MM"), a.Name);
        Check(o == ImportOutcome.Imported && final == expect && File.Exists(expect), "file lands in year\\year-month");
        Check(File.GetLastWriteTime(expect) == a.Date, "file keeps the photo's date");
        Check(new Library(dest).Has(a), "import is remembered in the index");

        File.Delete(Path.Combine(dest, Library.IndexName));
        o = new Library(dest) { RetryPauseMs = 1 }.Import(dev, a, delegate { }, delegate { return false; }, out final);
        Check(o == ImportOutcome.Duplicate && Directory.GetFiles(Path.GetDirectoryName(expect)).Length == 1, "lost index does not duplicate files");

        var flaky = new DemoDevice { FailEvery = 2 };
        flaky.Downloads = 1;
        o = lib.Import(flaky, items[1], delegate { }, delegate { return false; }, out final);
        Check(o == ImportOutcome.Imported && flaky.Downloads == 3, "a failed transfer is retried automatically");

        var dead = new DemoDevice { FailEvery = 1 };
        bool threw = false;
        try { lib.Import(dead, items[2], delegate { }, delegate { return false; }, out final); } catch (IOException) { threw = true; }
        Check(threw && dead.Downloads == Library.Attempts && !lib.Has(items[2]), "gives up after 3 attempts and stays un-imported");

        o = lib.Import(dev, items[3], delegate { }, delegate { return true; }, out final);
        Check(o == ImportOutcome.Cancelled && !lib.Has(items[3]), "cancel leaves the item un-imported");

        lib.CleanStaging();
        Check(!Directory.Exists(Path.Combine(dest, Library.StagingName)), "staging folder removed");
        Check(WpdDevice.FolderDate("202610_b/") == new DateTime(2026, 10, 1) && WpdDevice.FolderDate("DCIM/100APPLE/") == null, "month read from iPhone folder names");

        var heic = new byte[64];
        Array.Copy(new byte[] { 0, 0, 0, 9, (byte)'i', (byte)'r', (byte)'o', (byte)'t', 3 }, 0, heic, 30, 9);
        Check(Orientation.FromHead(heic) == System.Drawing.RotateFlipType.Rotate90FlipNone, "HEIC portrait rotation is read");
        byte[] jpg = { 0xFF, 0xD8, 0xFF, 0xE1, 0, 34, (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0, (byte)'M', (byte)'M', 0, 42, 0, 0, 0, 8,
            0, 1, 0x01, 0x12, 0, 3, 0, 0, 0, 1, 0, 6, 0, 0, 0, 0, 0, 0 };
        Check(Orientation.FromHead(jpg) == System.Drawing.RotateFlipType.Rotate90FlipNone, "JPEG portrait rotation is read");
        Check(Orientation.FromHead(new byte[40]) == System.Drawing.RotateFlipType.RotateNoneFlipNone, "unknown data is left unrotated");

        Directory.Delete(dest, true);
        Console.WriteLine(fails == 0 ? "ALL PASSED" : fails + " FAILED");
        return fails;
    }
}
