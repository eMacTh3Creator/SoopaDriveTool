using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace SoopaDriveTool.Core;
public static class Data
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public static string Home => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SoopaDriveTool");
    public static string Size(long b) => b >= 1L<<40 ? $"{b / (double)(1L<<40):0.##} TiB" : b >= 1L<<30 ? $"{b / (double)(1L<<30):0.##} GiB" : $"{b / (double)(1L<<20):0.##} MiB";
    public static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
    public static async Task SaveAsync<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(value, Json)); File.Move(tmp,path,true); }
        finally { if(File.Exists(tmp)) File.Delete(tmp); }
    }
}
public record PartitionInfo
{
    public int Number { get; init; }
    public long Offset { get; init; }
    public long Size { get; init; }
    public string Letter { get; init; } = "";
    public string Label { get; init; } = "";
    public string FileSystem { get; init; } = "";
    public long Free { get; init; }
    public string Type { get; init; } = "";
    public bool IsBoot { get; init; }
    public bool IsSystem { get; init; }
    public string Encryption { get; init; } = "Unknown";
    public bool Protected => IsBoot || IsSystem || Type.Contains("Recovery",StringComparison.OrdinalIgnoreCase) || Type.Contains("Reserved",StringComparison.OrdinalIgnoreCase) || Type.Contains("Extended",StringComparison.OrdinalIgnoreCase) || Type.Contains("System",StringComparison.OrdinalIgnoreCase);
    public override string ToString()=>Display;
    public string Display => $"{(Number<0?"[Pending] ":"")}{(Letter.Length>0?Letter+": ":"")}{(Label.Length>0?Label:Type)} · {Data.Size(Size)} · {FileSystem}";
}
public record DiskInfo
{
    public int Number { get; init; }
    public string Name { get; init; } = "";
    public string UniqueId { get; init; } = "";
    public string Serial { get; init; } = "";
    public string NativeIdentity { get; init; } = "";
    public long Size { get; init; }
    public int SectorSize { get; init; } = 512;
    public string Style { get; init; } = "";
    public string Bus { get; init; } = "";
    public string Health { get; init; } = "Unknown";
    public bool IsBoot { get; init; }
    public bool IsSystem { get; init; }
    public bool Offline { get; init; }
    public bool ReadOnly { get; init; }
    public List<PartitionInfo> Partitions { get; init; } = [];
    public bool Protected => IsBoot || IsSystem || Partitions.Any(p=>p.IsBoot||p.IsSystem);
    public string StableIdentity => Data.Hash($"{UniqueId}|{Serial}|{Size}|{SectorSize}");
    public string Identity => Data.Hash($"{UniqueId}|{Serial}|{Size}|{SectorSize}|{NativeIdentity}");
    public string Layout => Data.Hash(string.Join(";",Partitions.OrderBy(p=>p.Number).Select(p=>$"{p.Number}:{p.Offset}:{p.Size}:{p.Letter}:{p.FileSystem}:{p.Label}:{p.Type}"))+"|"+Style+"|"+Offline+"|"+ReadOnly);
    public override string ToString()=>Display;
    public string Display => $"Disk {Number} · {Name} · {Data.Size(Size)}";
}
public enum OperationKind { InitializeGpt, InitializeMbr, CreatePartition, ResizePartition, FormatPartition, DeletePartition, SetLabel, SetLetter, RemoveLetter, Offline, Online, ReadOnly, ReadWrite, ReTrim, CheckFileSystem, BackupImage, RestoreImage, CloneDisk, WipeDisk, QuickWipeDisk, MovePartition, ConvertTable, SetPartitionKind, SplitPartition, MergePartition, ChangeFileSystem, ChangeClusterSize, RedistributeSpace, ConvertDynamic }
public enum CloneMode { ByteExact, FileSystem, WindowsUefi, WindowsLiveUefi, ByteExactGrow }
public enum WipeMode { Zero, Random, ThreePass }
public record DiskOperation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public OperationKind Kind { get; init; }
    public int DiskNumber { get; init; }
    public string Identity { get; init; } = "";
    public string Layout { get; init; } = "";
    public int PartitionNumber { get; init; }
    public long Bytes { get; init; }
    public long Offset { get; init; }
    public int TargetPartitionNumber { get; init; }
    public int AllocationUnitSize { get; init; }
    public CloneMode CloneMode { get; init; }
    public WipeMode WipeMode { get; init; }
    public string Workspace { get; init; } = "";
    public string Value { get; init; } = "";
    public string FileSystem { get; init; } = "NTFS";
    public int TargetDiskNumber { get; init; } = -1;
    public string TargetIdentity { get; init; } = "";
    public string TargetLayout { get; init; } = "";
    public bool Destructive => Kind is OperationKind.FormatPartition or OperationKind.DeletePartition or OperationKind.RestoreImage or OperationKind.CloneDisk or OperationKind.WipeDisk or OperationKind.QuickWipeDisk or OperationKind.MovePartition or OperationKind.ConvertTable or OperationKind.SetPartitionKind or OperationKind.MergePartition or OperationKind.ChangeFileSystem or OperationKind.ChangeClusterSize or OperationKind.RedistributeSpace or OperationKind.ConvertDynamic;
    public string Description => $"{(Kind==OperationKind.CloneDisk?CloneMode+" clone":Kind==OperationKind.WipeDisk?WipeMode+" overwrite":Kind.ToString())} · Disk {DiskNumber}"+(PartitionNumber!=0?$" / {(PartitionNumber<0?"pending partition "+(-PartitionNumber):"partition "+PartitionNumber)}":"")+(Bytes>0?$" · {Data.Size(Bytes)}":"")+(Value.Length>0?$" · {Value}":"")+(TargetDiskNumber>=0?$" → Disk {TargetDiskNumber}":"");
    public static DiskOperation For(DiskInfo disk,OperationKind kind,PartitionInfo? partition=null) => new(){Kind=kind,DiskNumber=disk.Number,Identity=disk.Identity,Layout=disk.Layout,PartitionNumber=partition?.Number??0};
}
public static class OperationPolicy
{
    public static void Validate(DiskOperation op,DiskInfo disk,DiskInfo? target=null)
    {
        if(!Enum.IsDefined(op.Kind)) throw new InvalidOperationException("Unknown operation.");
        if(disk.Number!=op.DiskNumber || disk.Identity!=op.Identity || disk.Layout!=op.Layout) throw new InvalidOperationException("The disk identity or layout changed. Refresh and prepare the operation again.");
        bool liveRead=op.Kind==OperationKind.CloneDisk&&op.CloneMode==CloneMode.WindowsLiveUefi;
        if(disk.Protected&&!liveRead) throw new InvalidOperationException("Changes to the running system/boot disk are blocked in this release.");
        if(string.IsNullOrWhiteSpace(disk.UniqueId) || disk.Size<=0) throw new InvalidOperationException("The disk cannot be identified safely.");
        if(disk.ReadOnly && op.Kind is not (OperationKind.ReadWrite or OperationKind.BackupImage or OperationKind.CheckFileSystem or OperationKind.CloneDisk)) throw new InvalidOperationException("Disk is read-only.");
        var raw=op.Kind is OperationKind.BackupImage or OperationKind.RestoreImage or OperationKind.WipeDisk or OperationKind.MovePartition or OperationKind.ConvertTable or OperationKind.SetPartitionKind || op.Kind==OperationKind.CloneDisk&&op.CloneMode is CloneMode.ByteExact or CloneMode.ByteExactGrow;
        if(raw && !disk.Offline) throw new InvalidOperationException("Take the data disk offline before raw imaging, cloning or wiping.");
        if(!raw && disk.Offline && op.Kind is not (OperationKind.Online or OperationKind.ReadOnly or OperationKind.ReadWrite)) throw new InvalidOperationException("Bring this data disk online first.");
        if(op.Kind is OperationKind.ResizePartition or OperationKind.FormatPartition or OperationKind.DeletePartition or OperationKind.SetLabel or OperationKind.SetLetter or OperationKind.RemoveLetter or OperationKind.ReTrim or OperationKind.CheckFileSystem or OperationKind.MovePartition or OperationKind.SetPartitionKind or OperationKind.SplitPartition or OperationKind.MergePartition or OperationKind.ChangeFileSystem or OperationKind.ChangeClusterSize or OperationKind.RedistributeSpace)
            if(op.PartitionNumber==0)throw new InvalidOperationException("Select a data partition for this operation.");
        if(op.PartitionNumber!=0)
        {
            var p=disk.Partitions.SingleOrDefault(p=>p.Number==op.PartitionNumber) ?? throw new InvalidOperationException("Partition no longer exists.");
            if(p.Protected&&!liveRead) throw new InvalidOperationException("EFI, reserved and recovery partitions are protected.");
            if(p.Encryption!="None" && op.Kind is not OperationKind.CheckFileSystem) throw new InvalidOperationException("BitLocker is enabled or its status is unknown. Partition changes are blocked.");
        }
        if(disk.Style=="Dynamic"&&op.Kind!=OperationKind.ConvertDynamic)throw new InvalidOperationException("Use the reviewed simple-volume conversion workflow or Windows Storage tools for dynamic disks.");
        if(op.Kind==OperationKind.ConvertDynamic&&op.Value is not ("Basic" or "Dynamic"))throw new InvalidOperationException("Choose Basic or Dynamic.");
        if(op.Kind==OperationKind.MovePartition&&(op.Offset<1024*1024||op.Offset%(1024*1024)!=0))throw new InvalidOperationException("Choose a 1 MiB-aligned move destination.");
        if(op.Kind==OperationKind.ConvertTable&&op.Value is not ("GPT" or "MBR"))throw new InvalidOperationException("Choose GPT or MBR.");
        if(op.Kind==OperationKind.SetPartitionKind&&op.Value is not ("Primary" or "Logical"))throw new InvalidOperationException("Choose Primary or Logical.");
        if(op.Kind==OperationKind.MergePartition&&(op.TargetPartitionNumber==op.PartitionNumber||!disk.Partitions.Any(p=>p.Number==op.TargetPartitionNumber&&!p.Protected&&p.Encryption=="None")))throw new InvalidOperationException("Choose a different adjacent data partition to merge.");
        if(op.Kind==OperationKind.ChangeFileSystem&&op.FileSystem is not ("NTFS" or "exFAT" or "FAT32"))throw new InvalidOperationException("Unsupported destination filesystem.");
        if(op.Kind is OperationKind.InitializeGpt or OperationKind.InitializeMbr)
            if(disk.Style!="RAW" || disk.Partitions.Count!=0) throw new InvalidOperationException("Initialization requires a new RAW disk.");
        if(op.Kind is OperationKind.CreatePartition or OperationKind.ResizePartition)
            if(op.Bytes<16L*1024*1024 || op.Bytes>disk.Size) throw new InvalidOperationException("Choose a valid size of at least 16 MiB.");
        if(op.Kind==OperationKind.CreatePartition&&(op.Offset<0||op.Offset>0&&op.Offset!=QueuePlanner.Allocate(disk,op.Bytes)))throw new InvalidOperationException("The planned partition offset is not a valid free allocation.");
        if(op.Kind is OperationKind.CreatePartition or OperationKind.ResizePartition)if(op.Bytes%(1024*1024)!=0)throw new InvalidOperationException("Partition sizes must be whole MiB values.");
        if(op.Kind==OperationKind.CreatePartition && disk.Style is not ("GPT" or "MBR")) throw new InvalidOperationException("Initialize this disk before creating partitions.");
        if(op.Kind==OperationKind.SetLetter && (op.Value.Length!=1 || op.Value[0]<'D'||op.Value[0]>'Z')) throw new InvalidOperationException("Choose an unused letter D–Z.");
        if(op.Kind is OperationKind.FormatPartition or OperationKind.CreatePartition)
            if(op.FileSystem is not ("NTFS" or "exFAT" or "FAT32")) throw new InvalidOperationException("Unsupported filesystem.");
        if(op.Kind is OperationKind.SetLabel or OperationKind.CreatePartition or OperationKind.FormatPartition)
            if(op.Value.Length>11 || op.Value.Any(c=>char.IsControl(c)||"\\/:*?\"<>|".Contains(c))) throw new InvalidOperationException("Use a label of up to 11 characters without filesystem punctuation.");
        if(op.Kind==OperationKind.WipeDisk && !Enum.IsDefined(op.WipeMode))throw new InvalidOperationException("Unknown wipe mode.");
        if(op.Kind==OperationKind.CloneDisk)
        {
            if(!Enum.IsDefined(op.CloneMode))throw new InvalidOperationException("Unknown clone mode.");
            if(target is null || target.Number!=op.TargetDiskNumber || target.Identity!=op.TargetIdentity || target.Layout!=op.TargetLayout || target.Number==disk.Number || target.Protected || target.ReadOnly)throw new InvalidOperationException("Clone target must be a different, unchanged, writable data disk.");
            if(op.CloneMode is CloneMode.ByteExact or CloneMode.ByteExactGrow)
            {
                if(!target.Offline)throw new InvalidOperationException("Take the byte-clone target offline first.");
                if((op.CloneMode==CloneMode.ByteExact?target.Size!=disk.Size:target.Size<disk.Size)||target.SectorSize!=disk.SectorSize)throw new InvalidOperationException("Byte-exact clones require equal disk sizes and logical sector sizes.");
            }
            else
            {
                var p=disk.Partitions.SingleOrDefault(p=>p.Number==op.PartitionNumber)??throw new InvalidOperationException("Select the source NTFS volume.");
                if(liveRead&&(!disk.IsBoot||!p.IsBoot))throw new InvalidOperationException("Live Windows mode must select the currently running Windows volume.");
                if(disk.Offline||p.Protected&&!liveRead||p.FileSystem!="NTFS"||p.Encryption!="None"||p.Letter.Length!=1)throw new InvalidOperationException("Filesystem-aware cloning requires a mounted, unencrypted NTFS data volume or offline Windows installation.");
                long overhead=op.CloneMode is CloneMode.WindowsUefi or CloneMode.WindowsLiveUefi?1536L*1024*1024:128L*1024*1024;
                if(target.Size<p.Size-p.Free+overhead)throw new InvalidOperationException("The destination does not have enough capacity for the used data and layout overhead.");
            }
        }
        if(op.Kind is OperationKind.BackupImage or OperationKind.RestoreImage)
        {
            if(!Path.IsPathFullyQualified(op.Value) || op.Value.StartsWith(@"\\") || op.Value.StartsWith(@"\\.\")) throw new InvalidOperationException("Select a local image file path.");
        }
    }
}
