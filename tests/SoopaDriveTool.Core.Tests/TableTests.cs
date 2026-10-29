using SoopaDriveTool.Core;
using System.Security.Cryptography;
partial class Program
{
    static async Task TableChecks(string root)
    {
        const long mib=1024*1024;string path=Path.Combine(root,"tables.img");await using var stream=new FileStream(path,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None);stream.SetLength(32*mib);
        var part=new TablePartition(1,mib,8*mib,DiskTables.Basic,Guid.NewGuid(),"Archive");var table=new DiskTable("GPT",512,stream.Length,Guid.NewGuid(),1234,[part]);DiskTables.Write(stream,table);var read=DiskTables.Read(stream);Check(read.Style=="GPT"&&read.Partitions.Single().Id==part.Id&&read.Partitions[0].Name=="Archive","GPT writer produces checksummed primary/backup tables with stable partition identities");
        byte[] data=RandomNumberGenerator.GetBytes((int)mib);DiskTables.WriteAt(stream,part.Offset,data);string hash=Convert.ToHexString(SHA256.HashData(data));
        DiskTables.WriteAt(stream,512,new byte[512]);var recovered=DiskTables.Read(stream);Check(recovered.RecoveredFromBackup&&recovered.Partitions.Single().Offset==part.Offset,"A damaged primary GPT is recovered from its independently validated backup");DiskTables.Write(stream,recovered);Check(!DiskTables.Read(stream).RecoveredFromBackup,"Backup GPT reconstruction restores a valid primary header and table");
        var mbr=DiskTables.Convert(read,"MBR");DiskTables.Write(stream,mbr);Check(DiskTables.Read(stream).Style=="MBR"&&Convert.ToHexString(SHA256.HashData(DiskTables.ReadAt(stream,part.Offset,data.Length)))==hash,"Nonempty GPT-to-MBR conversion preserves data sectors");
        var back=DiskTables.Convert(DiskTables.Read(stream),"GPT");DiskTables.Write(stream,back);Check(DiskTables.Read(stream).Style=="GPT"&&Convert.ToHexString(SHA256.HashData(DiskTables.ReadAt(stream,part.Offset,data.Length)))==hash,"Nonempty MBR-to-GPT conversion preserves data sectors");
        var moved=DiskTables.Move(back,1,12*mib);Check(moved.Partitions[0].Offset==12*mib&&moved.Partitions[0].Id==back.Partitions[0].Id,"Partition move plans retain the unique volume identity");
        await Reject(()=>Task.Run(()=>DiskTables.Move(back,1,stream.Length-mib)),"Move planning rejects a destination outside the disk");
        TablePartition a=new TablePartition(1,2*mib,4*mib,DiskTables.Basic,Guid.Empty,"A"),b=new TablePartition(2,10*mib,4*mib,DiskTables.Basic,Guid.Empty,"B");var primary=new DiskTable("MBR",512,stream.Length,Guid.Empty,4321,[a,b]);var logical=DiskTables.SetLogical(primary,2,true);DiskTables.Write(stream,logical);var parsed=DiskTables.Read(stream);Check(parsed.Partitions.Count==2&&parsed.Partitions.Single(p=>p.Logical).Offset==b.Offset,"Primary-to-logical conversion constructs a real bounded EBR chain");
        var restored=DiskTables.SetLogical(parsed,parsed.Partitions.Single(p=>p.Logical).Index,false);DiskTables.Write(stream,restored);Check(DiskTables.Read(stream).Partitions.All(p=>!p.Logical),"Logical-to-primary conversion removes the extended-container dependency");
        var duplicate=back with{Partitions=[back.Partitions[0],back.Partitions[0] with{Index=2,Offset=16*mib}]};var before=DiskTables.ReadAt(stream,0,1024*1024);await Reject(()=>Task.Run(()=>DiskTables.Write(stream,duplicate)),"Duplicate GPT identifiers are rejected before metadata writes");Check(DiskTables.ReadAt(stream,0,before.Length).SequenceEqual(before),"Rejected table edits preserve existing disk metadata");
        byte[] ebr=DiskTables.ReadAt(stream,logical.Partitions.Single(p=>p.Logical).EbrOffset,512);DiskTables.Write(stream,logical);ebr[466]=15;System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(ebr.AsSpan(470),0);DiskTables.WriteAt(stream,logical.Partitions.Single(p=>p.Logical).EbrOffset,ebr);await Reject(()=>Task.Run(()=>DiskTables.Read(stream)),"Cyclic extended-partition links are rejected without following unbounded offsets");
    }
}
