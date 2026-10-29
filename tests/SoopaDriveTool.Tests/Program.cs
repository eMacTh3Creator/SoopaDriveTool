using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.Json;
using SoopaDriveTool.Core;
using SoopaDriveTool.Windows;
class ImmediateProgress(Action<string> callback):IProgress<string>{public void Report(string value)=>callback(value);}
partial class Program
{
    static int passed;
    static void Check(bool value,string name){if(!value)throw new Exception("FAIL: "+name);passed++;Console.WriteLine("PASS: "+name);}
    static void Reject(Action action,string name){try{action();}catch{Check(true,name);return;}throw new Exception("FAIL (not rejected): "+name);}
    static async Task RejectAsync(Func<Task> action,string name){try{await action();}catch{Check(true,name);return;}throw new Exception("FAIL (not rejected): "+name);}
    static DiskInfo Disk()=>new(){Number=2,UniqueId="test-disk",Serial="serial",Size=512L*1024*1024,Style="GPT",Health="Healthy",Partitions=[new(){Number=1,Offset=1024*1024,Size=96L*1024*1024,Encryption="None",FileSystem="NTFS",Letter="S"}]};
    static async Task<int> Main(string[] args)
    {
        var root=Path.Combine(Path.GetTempPath(),"SoopaDriveToolTests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var d=Disk();var op=DiskOperation.For(d,OperationKind.SetLabel,d.Partitions[0]) with{Value="Archive"};OperationPolicy.Validate(op,d);Check(true,"Valid data partition operation");
            Reject(()=>OperationPolicy.Validate(op,d with{IsBoot=true}),"Boot disk is protected");Reject(()=>OperationPolicy.Validate(op,d with{UniqueId="replacement"}),"Disk replacement rejected");Reject(()=>OperationPolicy.Validate(op,d with{Partitions=[]}),"Layout changes rejected");
            var encrypted=d with{Partitions=[d.Partitions[0] with{Encryption="BitLocker"}]};Reject(()=>OperationPolicy.Validate(op with{Layout=encrypted.Layout},encrypted),"BitLocker changes rejected");
            var unknown=d with{Partitions=[d.Partitions[0] with{Encryption="Unknown"}]};Reject(()=>OperationPolicy.Validate(op,unknown),"Unknown encryption rejected");
            Reject(()=>OperationPolicy.Validate(DiskOperation.For(d,OperationKind.CloneDisk),d),"Online raw operations rejected");
            Reject(()=>OperationPolicy.Validate(DiskOperation.For(d,OperationKind.SetLetter,d.Partitions[0]) with{Value="C"},d),"System drive letter rejected");
            Reject(()=>OperationPolicy.Validate(op with{Kind=(OperationKind)999},d),"Unknown operations rejected");Reject(()=>OperationPolicy.Validate(op with{PartitionNumber=0},d),"Missing partition selection rejected");
            Reject(()=>OperationPolicy.Validate(op with{Value="bad'\nlabel"},d),"Control characters rejected");
            var offline=d with{Offline=true};var target=offline with{Number=3,UniqueId="target",Size=offline.Size*2};var clone=DiskOperation.For(offline,OperationKind.CloneDisk) with{TargetDiskNumber=target.Number,TargetIdentity=target.Identity,TargetLayout=target.Layout};Reject(()=>OperationPolicy.Validate(clone,offline,target),"Mismatched clone capacity rejected");
            var source=Path.Combine(root,"source");var repo=Path.Combine(root,"repo");Directory.CreateDirectory(Path.Combine(source,"nested"));await File.WriteAllTextAsync(Path.Combine(source,"hello.txt"),"SoopaDriveTool backup integrity");await File.WriteAllBytesAsync(Path.Combine(source,"nested","data.bin"),Enumerable.Range(0,65536).Select(i=>(byte)i).ToArray());
            var m=await BackupEngine.CreateAsync(source,repo);await BackupEngine.VerifyAsync(m);Check(true,"Snapshot SHA-256 verification");
            var restore=Path.Combine(root,"restore");await BackupEngine.RestoreAsync(m,restore);Check(File.ReadAllText(Path.Combine(restore,"hello.txt"))=="SoopaDriveTool backup integrity","File restore content");Check(File.ReadAllBytes(Path.Combine(restore,"nested","data.bin")).SequenceEqual(File.ReadAllBytes(Path.Combine(source,"nested","data.bin"))),"Binary restore content");
            var objects=Directory.GetFiles(Path.Combine(repo,"objects")).Length;await BackupEngine.CreateAsync(source,repo);Check(Directory.GetFiles(Path.Combine(repo,"objects")).Length==objects,"Unchanged content deduplicated");
            await File.WriteAllTextAsync(Path.Combine(source,"hello.txt"),"new version");var m2=await BackupEngine.CreateAsync(source,repo);Check(Directory.GetFiles(Path.Combine(repo,"objects")).Length==objects+1,"New content produces one new object");await BackupEngine.VerifyAsync(m);Check(true,"Earlier snapshot remains verifiable");
            await RejectAsync(()=>BackupEngine.RestoreAsync(m,restore),"Existing restore files protected");await RejectAsync(()=>BackupEngine.CreateAsync(source,Path.Combine(source,"backup")),"Nested repository rejected");
            foreach(var path in new[]{"../evil","/root","a/../b","a\\b","C:/evil","a:stream","CON.txt","file.","a//b"})Reject(()=>BackupEngine.ValidateRelative(path),"Unsafe path rejected: "+path);
            var corrupt=Path.Combine(repo,"objects",(await BackupEngine.LoadAsync(m)).Files[0].Sha256+".gz");await File.WriteAllTextAsync(corrupt,"corrupt");await RejectAsync(()=>BackupEngine.VerifyAsync(m),"Corrupt compressed object rejected");
            var malicious=Path.Combine(repo,"snapshots","malicious.json");await Data.SaveAsync(malicious,new BackupManifest(1,DateTime.UtcNow,"",[new("../escape",0,new string('a',64),DateTime.UtcNow)]));await RejectAsync(()=>BackupEngine.RestoreAsync(malicious,Path.Combine(root,"unsafe")),"Malicious manifest rejected before restore");
            var b=new byte[8*1024*1024];using var image=new MemoryStream(b);b[510]=0x55;b[511]=0xaa;b[450]=0x07;BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(454),2048);BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(458),4096);var table=PartitionInspector.Inspect(image);Check(table.Count==1&&table[0].Offset==1048576,"MBR partition decode");b[511]=0;Reject(()=>PartitionInspector.Inspect(image),"Invalid MBR signature rejected");b[511]=0xaa;BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(458),uint.MaxValue);Reject(()=>PartitionInspector.Inspect(image),"Out-of-range MBR rejected");
            Check(PartitionInspector.Crc32(Encoding.ASCII.GetBytes("123456789"))==0xcbf43926,"CRC32 reference vector");
            var gpt=new byte[8*1024*1024];gpt[510]=0x55;gpt[511]=0xaa;gpt[450]=0xee;BinaryPrimitives.WriteUInt32LittleEndian(gpt.AsSpan(454),1);BinaryPrimitives.WriteUInt32LittleEndian(gpt.AsSpan(458),(uint)(gpt.Length/512-1));
            var header=gpt.AsSpan(512,512);Encoding.ASCII.GetBytes("EFI PART").CopyTo(header);BinaryPrimitives.WriteUInt32LittleEndian(header[8..],0x10000);BinaryPrimitives.WriteUInt32LittleEndian(header[12..],92);BinaryPrimitives.WriteUInt64LittleEndian(header[24..],1);BinaryPrimitives.WriteUInt64LittleEndian(header[32..],(ulong)(gpt.Length/512-1));BinaryPrimitives.WriteUInt64LittleEndian(header[40..],34);BinaryPrimitives.WriteUInt64LittleEndian(header[48..],(ulong)(gpt.Length/512-34));BinaryPrimitives.WriteUInt64LittleEndian(header[72..],2);BinaryPrimitives.WriteUInt32LittleEndian(header[80..],128);BinaryPrimitives.WriteUInt32LittleEndian(header[84..],128);
            var entry=gpt.AsSpan(1024,128);Guid.Parse("ebd0a0a2-b9e5-4433-87c0-68b6b72699c7").TryWriteBytes(entry);Guid.NewGuid().TryWriteBytes(entry[16..]);BinaryPrimitives.WriteUInt64LittleEndian(entry[32..],2048);BinaryPrimitives.WriteUInt64LittleEndian(entry[40..],4095);Encoding.Unicode.GetBytes("Test data").CopyTo(entry[56..]);BinaryPrimitives.WriteUInt32LittleEndian(header[88..],PartitionInspector.Crc32(gpt.AsSpan(1024,128*128)));BinaryPrimitives.WriteUInt32LittleEndian(header[16..],PartitionInspector.Crc32(header[..92]));
            using var gptStream=new MemoryStream(gpt);Check(PartitionInspector.Inspect(gptStream).Single().Name=="Test data","Valid GPT header/table CRC and partition decode");gpt[512+20]^=1;Reject(()=>PartitionInspector.Inspect(gptStream),"Corrupt GPT header CRC rejected");gpt[512+20]^=1;gpt[1024+56]^=1;Reject(()=>PartitionInspector.Inspect(gptStream),"Corrupt GPT table CRC rejected");

            var xml="<Results><Profile><TimeSpans><TimeSpan><Duration>2</Duration></TimeSpan></TimeSpans></Profile><TimeSpan><TestTimeSeconds>2</TestTimeSeconds><Thread><Target><BytesCount>2000000</BytesCount><IOCount>500</IOCount></Target></Thread><Latency><AverageTotalMilliseconds>0.5</AverageTotalMilliseconds><Bucket><Percentile>99</Percentile><TotalMilliseconds>1.2</TotalMilliseconds></Bucket></Latency></TimeSpan></Results>";var result=BenchmarkService.Parse(xml,"test","Read");Check(result.MBps==1&&result.Iops==250&&result.P99Ms==1.2,"DiskSpd unit conversion and p99 parsing");
            V02CoreTests();
            if(args.Contains("--native-store-only")){await NativeStoreCheck(root);Console.WriteLine($"ALL {passed} CHECKS PASSED");return 0;}
            if(args.Contains("--metadata-only")){await MetadataAclCheck(root);Console.WriteLine($"ALL {passed} CHECKS PASSED");return 0;}
            if(args.Length==2&&args[0]=="--build-recovery-iso"){if(Environment.MachineName!="SDTLAB")throw new Exception("Spare only");await RecoveryMediaService.BuildIsoAsync(args[1],RecoveryMediaService.DefaultAdk,[],new CollectProgress([]));Console.WriteLine("RECOVERY ISO BUILT");return 0;}
            if(args.Contains("--browse-only")){await V10MigrationChecks(root,true);Console.WriteLine($"ALL {passed} CHECKS PASSED");return 0;}
            if(args.Length==2&&args[0]=="--prepare-pe-fixture"){await PreparePeFixture(args[1]);return 0;}
            if(args.Length==2&&args[0]=="--capture-windows-fixture"){await CaptureWindowsFixture(args[1]);return 0;}
            if(args.Length==2&&args[0]=="--dynamic-fixture"){await DynamicFixture(args[1]);Console.WriteLine($"ALL {passed} CHECKS PASSED");return 0;}
            if(args.Contains("--migration-only")){await V10MigrationChecks(root);Console.WriteLine($"ALL {passed} CHECKS PASSED");return 0;}
            if(args.Contains("--advanced-only")){await V10AdvancedChecks(root);Console.WriteLine($"ALL {passed} CHECKS PASSED");return 0;}
            if(args.Contains("--snapshot-only")){await V10SnapshotChecks(root);Console.WriteLine($"ALL {passed} CHECKS PASSED");return 0;}
            if(args.Contains("--uefi-only")){await UefiLayoutChecks(root,Disk(),false);Console.WriteLine($"ALL {passed} CHECKS PASSED");return 0;}
            if(args.Contains("--benchmark-only"))await BenchmarkChecks(root);
            if(args.Contains("--integration"))await Integration(root);
            Console.WriteLine($"ALL {passed} CHECKS PASSED");return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        finally{try{Directory.Delete(root,true);}catch{}}
    }
    static async Task BenchmarkChecks(string root)
    {
            var benchmarkFolder=Path.Combine(root,"benchmark");Directory.CreateDirectory(benchmarkFolder);var benchmark=await BenchmarkService.RunAsync(benchmarkFolder,64,1,1,"Real world",false,false,null,CancellationToken.None);Check(benchmark.Results.Count==4&&benchmark.Results.All(x=>x.MBps>0&&x.Iops>0),"Live native DiskSpd real-world workloads");Check(!Directory.GetDirectories(benchmarkFolder,".soopadrivetool-*").Any(),"Benchmark temporary data removed");
            using(var stop=new CancellationTokenSource()){try{await BenchmarkService.RunAsync(benchmarkFolder,64,5,1,"Real world",false,false,new ImmediateProgress(s=>{if(s.Contains("pass"))stop.CancelAfter(250);}),stop.Token);throw new Exception("Benchmark did not cancel");}catch(OperationCanceledException){Check(true,"Running benchmark can be cancelled");}}
            Check(!Directory.GetDirectories(benchmarkFolder,".soopadrivetool-*").Any(),"Cancelled benchmark releases file handles and removes temporary data");
    }
    static async Task Integration(string root)
    {
        if(!StorageService.IsAdmin)throw new Exception("Integration tests require administrator.");
        var before=await StorageService.InventoryAsync();Check(before.Count>0,"Live storage inventory");var boot=before.First(d=>d.Protected);await RejectAsync(()=>StorageService.ExecuteAsync(DiskOperation.For(boot,OperationKind.Offline)),"Backend blocks system disk mutations");
        var files=new[]{Path.Combine(root,"source.vhd"),Path.Combine(root,"target.vhd")};var diskNumbers=new List<int>();
        try
        {
            foreach(var file in files)
            {
                var script=Path.Combine(root,Guid.NewGuid()+".txt");await File.WriteAllTextAsync(script,$"create vdisk file=\"{file}\" maximum=256 type=expandable\r\nselect vdisk file=\"{file}\"\r\nattach vdisk\r\nexit\r\n");
                var output=await StorageService.PowerShellAsync($"diskpart.exe /s '{script.Replace("'","''")}'; if($LASTEXITCODE -ne 0) {{ throw 'Diskpart failed' }}; (Get-DiskImage -ImagePath '{file.Replace("'","''")}' | Get-Disk).Number");
                var number=int.Parse(output.Split('\n').Last().Trim());diskNumbers.Add(number);var d=(await StorageService.InventoryAsync()).Single(d=>d.Number==number);Check(!d.Protected&&d.Bus=="File Backed Virtual","Disposable VHD identity");
            }
            async Task<DiskInfo> Current(int index=0)=>(await StorageService.InventoryAsync()).Single(d=>d.Number==diskNumbers[index]);
            async Task Apply(OperationKind kind,int index=0,Func<DiskOperation,DiskOperation>? edit=null,bool partition=false){var d=await Current(index);var o=DiskOperation.For(d,kind,partition?d.Partitions.Last(p=>!p.Protected):null);await StorageService.ExecuteAsync(edit==null?o:edit(o));}
            await Apply(OperationKind.InitializeGpt);Check((await Current()).Style=="GPT","Initialize GPT through backend");
            await Apply(OperationKind.CreatePartition,edit:o=>o with{Bytes=96L*1024*1024,Value="TESTDATA"});var disk=await Current();var p=disk.Partitions.Last(p=>p.Letter.Length>0);Check(p.FileSystem=="NTFS"&&p.Label=="TESTDATA","Create + format NTFS partition");Check(p.Offset%1048576==0,"1 MiB partition alignment");
            var payload=Encoding.UTF8.GetBytes("SoopaDriveTool disposable VHD round trip");var content=Path.Combine(p.Letter+@":\","roundtrip.txt");await File.WriteAllBytesAsync(content,payload);
            await Apply(OperationKind.ResizePartition,edit:o=>o with{Bytes=128L*1024*1024},partition:true);Check((await Current()).Partitions.Last(p=>p.Letter.Length>0).Size==128L*1024*1024,"Resize partition to 128 MiB");Check(File.ReadAllBytes(content).SequenceEqual(payload),"Resize preserves file content");
            await Apply(OperationKind.SetLabel,edit:o=>o with{Value="SOOPATEST"},partition:true);Check((await Current()).Partitions.Any(p=>p.Label=="SOOPATEST"),"Volume rename");
            await Apply(OperationKind.CheckFileSystem,partition:true);Check(true,"Filesystem scan");
            await BenchmarkChecks(root);
            await Apply(OperationKind.Offline);await Apply(OperationKind.Offline,index:1);
            var image=Path.Combine(root,"disk.sdi");await Apply(OperationKind.BackupImage,edit:o=>o with{Value=image});Check(File.Exists(image)&&File.Exists(image+".json"),"Verified full raw disk image");
            disk=await Current();var target=await Current(1);await StorageService.ExecuteAsync(DiskOperation.For(disk,OperationKind.CloneDisk) with{TargetDiskNumber=target.Number,TargetIdentity=target.Identity,TargetLayout=target.Layout});Check(true,"Raw clone and full target read-back verification");
            await Apply(OperationKind.WipeDisk);Check(true,"Zero-overwrite and full read-back verification");await Apply(OperationKind.RestoreImage,edit:o=>o with{Value=image});Check(true,"Raw image restore and full read-back verification");await StorageService.PowerShellAsync("Update-HostStorageCache");await Apply(OperationKind.Online);
            disk=await Current();p=disk.Partitions.Last(p=>p.Letter.Length>0);Check(File.ReadAllBytes(Path.Combine(p.Letter+@":\","roundtrip.txt")).SequenceEqual(payload),"Image restore recovers original file bytes");
            await Apply(OperationKind.DeletePartition,partition:true);Check((await Current()).Partitions.All(p=>p.Protected),"Delete data partition");
            await Apply(OperationKind.CreatePartition,edit:o=>o with{Bytes=64L*1024*1024,Value="EXFATTEST",FileSystem="exFAT"});Check((await Current()).Partitions.Any(p=>p.FileSystem=="exFAT"),"Create exFAT partition");
            await V02Integration(root,await Current(),await Current(1));
        }
        finally
        {
            foreach(var file in files)try{await StorageService.PowerShellAsync($"Dismount-DiskImage -ImagePath '{file.Replace("'","''")}' -ErrorAction SilentlyContinue");}catch{}
            Check((await StorageService.InventoryAsync()).Count==before.Count,"Disposable VHDs detached; original disk count restored");
        }
    }
}
