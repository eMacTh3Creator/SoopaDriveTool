using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace SoopaDriveTool.Core;

public record VaultInput(string Name,long Length,Func<Stream> Open);
public record VaultOptions(bool Encrypted=false,string[]? Exclude=null,string Policy="Incremental",int KeepLatest=0,bool PreserveMetadata=true);
public record VaultConfig(int Version,bool Encrypted,string Salt,int Iterations,string KeyCheck);
public record VaultResource(string Name,long Length,List<string> Chunks,string Sha256);
public record VaultMetadata(string? Security=null,int Attributes=0,DateTime? CreatedUtc=null,string? LinkTarget=null,bool DirectoryLink=false,string? HardlinkId=null,string? ExtendedAttributes=null);
public record VaultEntry(string Path,string Kind,long Length,DateTime LastWriteUtc,List<VaultResource> Resources,VaultMetadata? Metadata=null);
public record VaultManifest(int Version,DateTime CreatedUtc,string Source,string Policy,string? BaseSnapshot,List<VaultEntry> Entries,VaultMetadata? RootMetadata=null);
public record VaultSummary(string Id,DateTime CreatedUtc,string Policy,int Files,long Bytes);
public record VaultPruneResult(int SnapshotsRemoved,int ObjectsRemoved,long BytesReclaimed);
public interface IVaultMetadata
{
    VaultMetadata Read(string path,bool directory,string root);
    IEnumerable<(string Name,string Path)> Streams(string path);
    void Restore(string path,VaultMetadata metadata,bool directory);
    void MakeLink(string path,VaultMetadata metadata,string destination);
    void MakeHardlink(string path,string existing);
}
public sealed class VaultBackup:IDisposable
{
    public const int ChunkBytes=4*1024*1024;
    readonly string root;readonly byte[]? key;readonly FileStream gate;readonly IVaultMetadata? metadata;bool disposed;
    public VaultConfig Config{get;private set;}
    VaultBackup(string root,VaultConfig config,byte[]? key,FileStream gate,IVaultMetadata? metadata){this.root=root;Config=config;this.key=key;this.gate=gate;this.metadata=metadata;}
    public static async Task<VaultBackup> OpenAsync(string directory,string? password=null,bool create=false,IVaultMetadata? metadata=null,CancellationToken ct=default)
    {
        directory=Path.GetFullPath(directory);BackupEngine.RejectExistingAncestorLinks(directory);
        if(!create&&!Directory.Exists(directory))throw new DirectoryNotFoundException(directory);Directory.CreateDirectory(directory);
        var lockPath=Path.Combine(directory,"vault.lock");BackupEngine.RejectExistingAncestorLinks(lockPath);
        FileStream gate;try{gate=new(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}catch(IOException e){throw new IOException("The repository is already open by another operation. Retry when it finishes.",e);}
        byte[]? key=null;
        try
        {
            string path=Path.Combine(directory,"vault.json");VaultConfig config;
            if(File.Exists(path))
            {
                BackupEngine.RejectExistingAncestorLinks(path);if(new FileInfo(path).Length>16384)throw new IOException("Invalid repository configuration.");
                config=JsonSerializer.Deserialize<VaultConfig>(await File.ReadAllTextAsync(path,ct),Data.Json)??throw new IOException("Missing repository configuration.");
                if(config.Version!=2||config.Iterations<600000||config.Iterations>2000000)throw new IOException("Unsupported or unsafe repository configuration.");
                if(config.Encrypted){if(string.IsNullOrEmpty(password))throw new UnauthorizedAccessException("Enter the repository passphrase.");var salt=Convert.FromBase64String(config.Salt);if(salt.Length!=32)throw new IOException("Invalid repository salt.");var wrapping=Derive(password,salt,config.Iterations);try{key=VaultCodec.Decode(Convert.FromBase64String(config.KeyCheck),wrapping,"repository-key");if(key.Length!=32)throw new CryptographicException();}catch(CryptographicException e){throw new UnauthorizedAccessException("Incorrect passphrase or damaged repository key check.",e);}finally{CryptographicOperations.ZeroMemory(wrapping);}}
            }
            else
            {
                if(!create)throw new IOException("Not a SoopaDriveTool protected backup repository.");
                if(Directory.EnumerateFileSystemEntries(directory).Any(p=>p!=lockPath))throw new IOException("Initialize a protected repository in an empty folder.");
                bool encrypted=!string.IsNullOrEmpty(password);if(encrypted&&password!.Length<12)throw new IOException("Use a passphrase of at least 12 characters.");
                var salt=RandomNumberGenerator.GetBytes(32);if(encrypted)key=RandomNumberGenerator.GetBytes(32);
                byte[]? wrapping=encrypted?Derive(password!,salt,600000):null;
                try{config=new(2,encrypted,Convert.ToBase64String(salt),600000,encrypted?Convert.ToBase64String(VaultCodec.Encode(key!,wrapping,"repository-key")):"");}
                finally{if(wrapping!=null)CryptographicOperations.ZeroMemory(wrapping);}
                await Data.SaveAsync(path,config);
            }
            foreach(var child in new[]{"objects","snapshots"}){var p=Path.Combine(directory,child);BackupEngine.RejectExistingAncestorLinks(p);Directory.CreateDirectory(p);}
            return new(directory,config,key,gate,metadata);
        }
        catch{if(key!=null)CryptographicOperations.ZeroMemory(key);gate.Dispose();throw;}
    }
    static byte[] Derive(string password,byte[] salt,int rounds)=>Rfc2898DeriveBytes.Pbkdf2(password,salt,rounds,HashAlgorithmName.SHA256,32);
    string ObjectId(byte[] bytes){var hash=SHA256.HashData(bytes);return Convert.ToHexString(key==null?hash:HMACSHA256.HashData(key,hash)).ToLowerInvariant();}
    static void ValidateId(string id){var bits=id.Split('/');if(bits.Length!=2||!Regex.IsMatch(bits[0],"^[0-9]{8}T[0-9]{9}Z-[a-f0-9]{32}$",RegexOptions.CultureInvariant)||!Regex.IsMatch(bits[1],"^[0-9a-f]{64}$",RegexOptions.CultureInvariant))throw new IOException("Invalid backup object identifier.");}
    string ObjectPath(string id){ValidateId(id);var p=Path.Combine(root,"objects",id.Replace('/',Path.DirectorySeparatorChar)+".svo");BackupEngine.RejectExistingAncestorLinks(p);return p;}
    string SnapshotPath(string id){if(!Regex.IsMatch(id,"^[0-9]{8}T[0-9]{9}Z-[a-f0-9]{32}$",RegexOptions.CultureInvariant))throw new IOException("Invalid snapshot identifier.");var p=Path.Combine(root,"snapshots",id+".svm");BackupEngine.RejectExistingAncestorLinks(p);return p;}
    public async Task<string> CreateAsync(string source,VaultOptions? options=null,IProgress<string>? progress=null,CancellationToken ct=default,string? sourceDisplay=null,IProgress<DriveProgress>? activity=null)
    {
        ObjectDisposedException.ThrowIf(disposed,this);options??=new(Encrypted:Config.Encrypted);if(options.Policy is not ("Full" or "Incremental" or "Differential"))throw new IOException("Choose full, incremental, or differential backup.");
        if(options.Encrypted!=Config.Encrypted)throw new IOException("Backup encryption must match this repository.");source=Path.GetFullPath(source);if(!Directory.Exists(source))throw new DirectoryNotFoundException(source);
        if(BackupEngine.IsInside(source,root)||BackupEngine.IsInside(root,source))throw new IOException("Source and repository must be separate trees.");
        BackupEngine.RejectExistingAncestorLinks(source);var exclusions=(options.Exclude??[]).Select(Glob).ToArray();var entries=new List<VaultEntry>();var old=await ListAsync(ct);string? parent=options.Policy=="Full"?null:options.Policy=="Differential"?old.LastOrDefault(x=>x.Policy=="Full")?.Id:old.LastOrDefault()?.Id;
        string policy=parent==null?"Full":options.Policy;var created=DateTime.UtcNow;var id=created.ToString("yyyyMMdd'T'HHmmssfff'Z'")+"-"+Guid.NewGuid().ToString("N");
        var reusable=new Dictionary<string,string>();if(parent!=null)foreach(var e in (await LoadAsync(parent,ct)).Entries)foreach(var resource in e.Resources)foreach(var chunk in resource.Chunks)reusable[chunk.Split('/')[1]]=chunk;
        // Full generations write their complete data. Incrementals reuse the last
        // restore point; differentials reuse only their full base. Manifests retain
        // a complete resource map so pruning a base does not break later points.
        bool Include(string path){var rel=Path.GetRelativePath(source,path).Replace('\\','/');return !exclusions.Any(x=>x.IsMatch(rel)||Ancestors(rel).Any(x.IsMatch));}
        activity?.Report(new("Scanning backup source",null,"Enumerating selected files, folders and alternate streams."));
        var planned=Enumerate(source,Include).ToList();long total=0;
        foreach(var path in planned){var attr=File.GetAttributes(path);if((attr&(FileAttributes.Directory|FileAttributes.ReparsePoint))==0){total=checked(total+new FileInfo(path).Length);if(options.PreserveMetadata&&metadata!=null)foreach(var stream in metadata.Streams(path))total=checked(total+new FileInfo(stream.Path).Length);}}
        long done=0;var timer=System.Diagnostics.Stopwatch.StartNew();void Tick(int count){done+=count;double speed=done/Math.Max(.001,timer.Elapsed.TotalSeconds);activity?.Report(new("Writing backup objects",total==0?100:100.0*done/total,$"Read {Data.Size(done)} / {Data.Size(total)} · {Data.Size((long)speed)}/s",done,total,speed));}
        foreach(var path in planned)
        {
            ct.ThrowIfCancellationRequested();var relative=Path.GetRelativePath(source,path).Replace('\\','/');BackupEngine.ValidateRelative(relative);if(exclusions.Any(x=>x.IsMatch(relative)||Ancestors(relative).Any(x.IsMatch)))continue;
            var attr=File.GetAttributes(path);bool directory=(attr&FileAttributes.Directory)!=0;VaultMetadata? meta=options.PreserveMetadata?metadata?.Read(path,directory,source):null;
            if((attr&FileAttributes.ReparsePoint)!=0){if(meta?.LinkTarget==null)throw new IOException("Link metadata is unavailable: "+relative);entries.Add(new(relative,"Link",0,Directory.GetLastWriteTimeUtc(path),[],meta));continue;}
            progress?.Report(relative);
            if(directory){entries.Add(new(relative,"Directory",0,Directory.GetLastWriteTimeUtc(path),[],meta));continue;}
            var info=new FileInfo(path);long length=info.Length;var stamp=info.LastWriteTimeUtc;var resources=new List<VaultResource>{await StoreResource(path,"",id,reusable,ct,Tick)};
            if(options.PreserveMetadata&&metadata!=null)foreach(var stream in metadata.Streams(path)){ValidateStream(stream.Name);resources.Add(await StoreResource(stream.Path,stream.Name,id,reusable,ct,Tick));}
            info.Refresh();if(info.Length!=length||info.LastWriteTimeUtc!=stamp)throw new IOException("Source changed during backup: "+relative);
            entries.Add(new(relative,"File",length,stamp,resources,meta));
        }
        var manifest=new VaultManifest(2,created,sourceDisplay??source,policy,parent,entries,options.PreserveMetadata?metadata?.Read(source,true,source):null);
        activity?.Report(new("Committing restore point",100,"All selected resources are stored; writing the authenticated manifest."));
        await AtomicWrite(SnapshotPath(id),VaultCodec.Encode(JsonSerializer.SerializeToUtf8Bytes(manifest,Data.Json),key,"manifest:"+id),ct);
        if(options.KeepLatest>0)await PruneAsync(options.KeepLatest,ct);return id;
    }
    public async Task<string> CreateStreamsAsync(IEnumerable<VaultInput> inputs,string display,string policy="Incremental",IProgress<DriveProgress>? progress=null,CancellationToken ct=default)
    {
        ObjectDisposedException.ThrowIf(disposed,this);if(policy is not ("Full" or "Incremental" or "Differential"))throw new IOException("Invalid image backup policy.");
        var list=inputs.ToList();if(list.Count==0||list.Any(x=>x.Length<0)||list.Select(x=>x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=list.Count)throw new IOException("Invalid image resource list.");foreach(var item in list)BackupEngine.ValidateRelative(item.Name);
        var old=await ListAsync(ct);string? parent=policy=="Full"?null:policy=="Differential"?old.LastOrDefault(x=>x.Policy=="Full")?.Id:old.LastOrDefault()?.Id;
        var reusable=new Dictionary<string,string>();if(parent!=null)foreach(var e in (await LoadAsync(parent,ct)).Entries)foreach(var r in e.Resources)foreach(var c in r.Chunks)reusable[c.Split('/')[1]]=c;
        var created=DateTime.UtcNow;string id=created.ToString("yyyyMMdd'T'HHmmssfff'Z'")+"-"+Guid.NewGuid().ToString("N");var entries=new List<VaultEntry>();long total=list.Sum(x=>x.Length),done=0;var clock=System.Diagnostics.Stopwatch.StartNew();
        foreach(var item in list){ct.ThrowIfCancellationRequested();await using var input=item.Open();var resource=await StoreStream(input,"",id,reusable,ct,n=>{done+=n;progress?.Report(new("Writing protected image",total==0?100:100.0*done/total,item.Name+" · "+Data.Size(done)+" / "+Data.Size(total),done,total,done/Math.Max(.001,clock.Elapsed.TotalSeconds)));});if(resource.Length!=item.Length)throw new IOException("Image resource length changed during capture.");entries.Add(new(item.Name,"File",item.Length,created,[resource]));}
        var manifest=new VaultManifest(2,created,display,parent==null?"Full":policy,parent,entries);await AtomicWrite(SnapshotPath(id),VaultCodec.Encode(JsonSerializer.SerializeToUtf8Bytes(manifest,Data.Json),key,"manifest:"+id),ct);return id;
    }
    public async Task ExportFileAsync(string id,string name,Stream output,CancellationToken ct=default,IProgress<DriveProgress>? progress=null)
    {
        var m=await LoadAsync(id,ct);var file=m.Entries.SingleOrDefault(e=>e.Path==name&&e.Kind=="File")??throw new IOException("Image resource is missing.");long done=0;
        await RestoreResource(file.Resources.Single(r=>r.Name==""),output,ct,n=>{done+=n;progress?.Report(new("Reading protected image",file.Length==0?100:100.0*done/file.Length,Data.Size(done)+" / "+Data.Size(file.Length),done,file.Length));});
    }
    static IEnumerable<string> Ancestors(string path){for(int i=path.LastIndexOf('/');i>=0;i=path.LastIndexOf('/',i-1)){yield return path[..i];if(i==0)break;}}
    static Regex Glob(string pattern)
    {
        if(string.IsNullOrWhiteSpace(pattern)||pattern.Length>512)throw new IOException("Invalid backup exclusion.");pattern=pattern.Replace('\\','/').Trim('/');
        var s=new StringBuilder("^");for(int i=0;i<pattern.Length;i++){char c=pattern[i];if(c=='*'&&i+1<pattern.Length&&pattern[i+1]=='*'){s.Append(".*");i++;}else if(c=='*')s.Append("[^/]*");else if(c=='?')s.Append("[^/]");else s.Append(Regex.Escape(c.ToString()));}s.Append("$");return new(s.ToString(),RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromSeconds(1));
    }
    static IEnumerable<string> Enumerate(string root,Func<string,bool> include){foreach(var p in Directory.EnumerateFileSystemEntries(root).Order(StringComparer.OrdinalIgnoreCase)){if(!include(p))continue;yield return p;if(Directory.Exists(p)&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)==0)foreach(var child in Enumerate(p,include))yield return child;}}
    async Task<VaultResource> StoreResource(string path,string name,string generation,Dictionary<string,string> reusable,CancellationToken ct,Action<int> tick)
    {
        await using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,ChunkBytes,FileOptions.Asynchronous|FileOptions.SequentialScan);return await StoreStream(input,name,generation,reusable,ct,tick);
    }
    async Task<VaultResource> StoreStream(Stream input,string name,string generation,Dictionary<string,string> reusable,CancellationToken ct,Action<int> tick)
    {
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);var chunks=new List<string>();long length=0;
        byte[] buffer=new byte[ChunkBytes];int n;
        while((n=await input.ReadAtLeastAsync(buffer,buffer.Length,false,ct))>0)
        {
            ct.ThrowIfCancellationRequested();var bytes=buffer[..n];length+=n;hash.AppendData(bytes);tick(n);string content=ObjectId(bytes);
            if(reusable.TryGetValue(content,out var reference)){await ReadChunk(reference,ct);chunks.Add(reference);continue;}
            string referenceId=generation+"/"+content,file=ObjectPath(referenceId);Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            if(!File.Exists(file))await AtomicWrite(file,VaultCodec.Encode(bytes,key,"data:"+referenceId),ct);else await ReadChunk(referenceId,ct);chunks.Add(referenceId);
        }
        return new(name,length,chunks,Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }
    async Task<byte[]> ReadChunk(string id,CancellationToken ct){string path=ObjectPath(id);if(new FileInfo(path).Length>ChunkBytes+128)throw new IOException("Oversized backup object.");var bytes=VaultCodec.Decode(await File.ReadAllBytesAsync(path,ct),key,"data:"+id);if(ObjectId(bytes)!=id.Split('/')[1])throw new IOException("Backup object identity/checksum mismatch.");return bytes;}
    public async Task<VaultManifest> LoadAsync(string id,CancellationToken ct=default)
    {
        var p=SnapshotPath(id);if(new FileInfo(p).Length>VaultCodec.MaxPacket+128)throw new IOException("Oversized backup manifest.");var m=JsonSerializer.Deserialize<VaultManifest>(VaultCodec.Decode(await File.ReadAllBytesAsync(p,ct),key,"manifest:"+id),Data.Json)??throw new IOException("Invalid backup manifest.");
        if(m.Version!=2||m.Entries==null||m.Entries.Count>1000000)throw new IOException("Unsupported backup manifest.");var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var byName=m.Entries.ToDictionary(e=>e.Path,StringComparer.OrdinalIgnoreCase);
        foreach(var e in m.Entries){BackupEngine.ValidateRelative(e.Path);if(!names.Add(e.Path)||e.Kind is not ("File" or "Directory" or "Link")||e.Length<0)throw new IOException("Invalid/duplicate backup entry.");var streams=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(var resource in e.Resources){if(!streams.Add(resource.Name)||resource.Length<0||!Regex.IsMatch(resource.Sha256,"^[a-f0-9]{64}$"))throw new IOException("Invalid backup resource.");if(resource.Name!="")ValidateStream(resource.Name);foreach(var chunk in resource.Chunks)ValidateId(chunk);}if(e.Kind=="File"&&e.Resources.Count(x=>x.Name=="")!=1)throw new IOException("Missing file data resource.");if(e.Kind=="Link"){if(e.Metadata?.LinkTarget==null)throw new IOException("Missing link target.");BackupEngine.ValidateRelative(e.Metadata.LinkTarget);}}
        foreach(var e in m.Entries)foreach(var ancestor in Ancestors(e.Path))if(byName.TryGetValue(ancestor,out var parent)&&parent.Kind is "File" or "Link")throw new IOException("A backup path traverses a file or reparse point.");return m;
    }
    static void ValidateStream(string name){if(name.Length is 0 or >255||name.Any(c=>char.IsControl(c)||"\\/:*?\"<>|".Contains(c)))throw new IOException("Unsafe alternate-stream name.");}
    public async Task<List<VaultSummary>> ListAsync(CancellationToken ct=default){var rows=new List<VaultSummary>();foreach(var f in Directory.GetFiles(Path.Combine(root,"snapshots"),"*.svm")){string id=Path.GetFileNameWithoutExtension(f);var m=await LoadAsync(id,ct);rows.Add(new(id,m.CreatedUtc,m.Policy,m.Entries.Count(x=>x.Kind=="File"),m.Entries.Sum(x=>x.Length)));}return rows.OrderBy(x=>x.CreatedUtc).ToList();}
    public async Task VerifyAsync(string id,IProgress<string>? progress=null,CancellationToken ct=default,IProgress<DriveProgress>? activity=null)
    {var m=await LoadAsync(id,ct);long total=m.Entries.Sum(e=>e.Resources.Sum(r=>r.Length)),done=0;var timer=System.Diagnostics.Stopwatch.StartNew();foreach(var e in m.Entries){progress?.Report(e.Path);foreach(var resource in e.Resources)await RestoreResource(resource,Stream.Null,ct,n=>{done+=n;double speed=done/Math.Max(.001,timer.Elapsed.TotalSeconds);activity?.Report(new("Verifying backup resources",total==0?100:100.0*done/total,e.Path+" · "+Data.Size(done)+" / "+Data.Size(total),done,total,speed));});}}
    async Task RestoreResource(VaultResource resource,Stream output,CancellationToken ct,Action<int>? tick=null){using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);long done=0;foreach(var chunk in resource.Chunks){var bytes=await ReadChunk(chunk,ct);done=checked(done+bytes.Length);if(done>resource.Length)throw new IOException("Backup resource exceeds its declared length.");hash.AppendData(bytes);await output.WriteAsync(bytes,ct);tick?.Invoke(bytes.Length);}if(done!=resource.Length||Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()!=resource.Sha256)throw new IOException("Backup resource SHA-256 mismatch.");}
    public async Task RestoreAsync(string id,string destination,IEnumerable<string>? selected=null,IProgress<string>? progress=null,CancellationToken ct=default)
    {
        var m=await LoadAsync(id,ct);destination=Path.GetFullPath(destination);BackupEngine.RejectExistingAncestorLinks(destination);if(BackupEngine.IsInside(destination,root)||BackupEngine.IsInside(root,destination))throw new IOException("Restore outside the repository.");if(Directory.Exists(destination)&&Directory.EnumerateFileSystemEntries(destination).Any())throw new IOException("Restore to a new or empty folder.");
        var choices=selected?.ToHashSet(StringComparer.OrdinalIgnoreCase);if(choices!=null)foreach(var s in choices){BackupEngine.ValidateRelative(s);if(!m.Entries.Any(e=>e.Path.Equals(s,StringComparison.OrdinalIgnoreCase)))throw new IOException("Selected path is not in this snapshot.");}
        var entries=m.Entries.Where(e=>choices==null||choices.Any(s=>e.Path.Equals(s,StringComparison.OrdinalIgnoreCase)||e.Path.StartsWith(s+"/",StringComparison.OrdinalIgnoreCase)||e.Kind=="Directory"&&s.StartsWith(e.Path+"/",StringComparison.OrdinalIgnoreCase))).ToList();Directory.CreateDirectory(destination);var hardlinks=new Dictionary<string,(string Path,VaultEntry Entry)>();
        foreach(var e in entries.Where(x=>x.Kind!="Link"))
        {
            ct.ThrowIfCancellationRequested();progress?.Report(e.Path);var path=Path.Combine(destination,e.Path.Replace('/',Path.DirectorySeparatorChar));BackupEngine.RejectExistingAncestorLinks(path);
            if(e.Kind=="Directory"){Directory.CreateDirectory(path);continue;}Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if(e.Metadata?.HardlinkId is string group&&hardlinks.TryGetValue(group,out var existing)&&metadata!=null){foreach(var resource in e.Resources)await RestoreResource(resource,Stream.Null,ct);if(JsonSerializer.Serialize(e.Resources,Data.Json)!=JsonSerializer.Serialize(existing.Entry.Resources,Data.Json)||e.Metadata.Security!=existing.Entry.Metadata?.Security)throw new IOException("Inconsistent data or permissions in a hardlink group.");metadata.MakeHardlink(path,existing.Path);}
            else
            {
                string tmp=path+".restore-"+Guid.NewGuid().ToString("N");try{await using(var output=new FileStream(tmp,FileMode.CreateNew,FileAccess.Write,FileShare.None))await RestoreResource(e.Resources.Single(x=>x.Name==""),output,ct);File.Move(tmp,path);}finally{if(File.Exists(tmp))File.Delete(tmp);}
                foreach(var resource in e.Resources.Where(x=>x.Name!="")){if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Alternate streams require an NTFS Windows destination.");await using var output=new FileStream(path+":"+resource.Name,FileMode.CreateNew,FileAccess.Write,FileShare.None);await RestoreResource(resource,output,ct);}if(e.Metadata?.HardlinkId is string key)hardlinks[key]=(path,e);
            }
            File.SetLastWriteTimeUtc(path,e.LastWriteUtc);
        }
        foreach(var e in entries.Where(x=>x.Kind=="Link")){if(metadata==null)throw new PlatformNotSupportedException("Restore links on Windows with metadata support.");var path=Path.Combine(destination,e.Path.Replace('/',Path.DirectorySeparatorChar));BackupEngine.RejectExistingAncestorLinks(path);Directory.CreateDirectory(Path.GetDirectoryName(path)!);metadata.MakeLink(path,e.Metadata!,destination);}
        // Apply parent security before child security so inheritance propagation
        // cannot overwrite a restored child DACL after its own restoration.
        if(m.RootMetadata!=null)metadata?.Restore(destination,m.RootMetadata,true);
        foreach(var e in entries.Where(x=>x.Kind=="Directory").OrderBy(x=>x.Path.Length)){var p=Path.Combine(destination,e.Path.Replace('/',Path.DirectorySeparatorChar));if(e.Metadata!=null)metadata?.Restore(p,e.Metadata,true);}
        foreach(var e in entries.Where(x=>x.Kind=="File")){var p=Path.Combine(destination,e.Path.Replace('/',Path.DirectorySeparatorChar));if(e.Metadata!=null)metadata?.Restore(p,e.Metadata,false);}
        foreach(var e in entries.Where(x=>x.Kind=="Directory").OrderByDescending(x=>x.Path.Length))Directory.SetLastWriteTimeUtc(Path.Combine(destination,e.Path.Replace('/',Path.DirectorySeparatorChar)),e.LastWriteUtc);
    }
    public async Task<VaultPruneResult> PruneAsync(int keepLatest,CancellationToken ct=default)
    {
        if(keepLatest<1)throw new IOException("Keep at least one restore point.");var snapshots=await ListAsync(ct);var keep=snapshots.TakeLast(keepLatest).ToList();var used=new HashSet<string>();foreach(var s in keep)foreach(var e in (await LoadAsync(s.Id,ct)).Entries)foreach(var resource in e.Resources)foreach(var chunk in resource.Chunks)used.Add(chunk);
        int removed=0;long bytes=0;foreach(var s in snapshots.Except(keep)){ct.ThrowIfCancellationRequested();File.Delete(SnapshotPath(s.Id));}
        foreach(var p in Directory.GetFiles(Path.Combine(root,"objects"),"*.svo",SearchOption.AllDirectories)){var id=Path.GetRelativePath(Path.Combine(root,"objects"),p).Replace('\\','/')[..^4];ValidateId(id);if(!used.Contains(id)){ct.ThrowIfCancellationRequested();BackupEngine.RejectExistingAncestorLinks(p);bytes+=new FileInfo(p).Length;File.Delete(p);removed++;}}
        return new(snapshots.Count-keep.Count,removed,bytes);
    }
    static async Task AtomicWrite(string path,byte[] bytes,CancellationToken ct){string tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{await using(var f=new FileStream(tmp,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,FileOptions.Asynchronous|FileOptions.WriteThrough)){await f.WriteAsync(bytes,ct);f.Flush(true);}File.Move(tmp,path,false);}finally{if(File.Exists(tmp))File.Delete(tmp);}}
    public async Task ChangePassphraseAsync(string password,CancellationToken ct=default)
    {
        if(!Config.Encrypted||key==null)throw new IOException("Passphrase changes require an encrypted repository.");if(password.Length<12)throw new IOException("Use a passphrase of at least 12 characters.");
        byte[] salt=RandomNumberGenerator.GetBytes(32),wrapping=Derive(password,salt,600000);
        try{var replacement=Config with{Salt=Convert.ToBase64String(salt),KeyCheck=Convert.ToBase64String(VaultCodec.Encode(key,wrapping,"repository-key"))};ct.ThrowIfCancellationRequested();await Data.SaveAsync(Path.Combine(root,"vault.json"),replacement);Config=replacement;}
        finally{CryptographicOperations.ZeroMemory(wrapping);}
    }
    public void Dispose(){if(disposed)return;disposed=true;if(key!=null)CryptographicOperations.ZeroMemory(key);gate.Dispose();}
}
