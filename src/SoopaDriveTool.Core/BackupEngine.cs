using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace SoopaDriveTool.Core;
public record BackupEntry(string Path,long Length,string Sha256,DateTime LastWriteUtc);
public record BackupManifest(int Version,DateTime CreatedUtc,string Source,List<BackupEntry> Files);
public record BackupJob(string Source,string Repository);
public static partial class BackupEngine
{
    [GeneratedRegex("^[0-9a-f]{64}$")] private static partial Regex HashPattern();
    public static async Task<string> CreateAsync(string source,string repository,IProgress<string>? progress=null,CancellationToken ct=default)
    {
        source=Path.GetFullPath(source); repository=Path.GetFullPath(repository);
        if(!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
        if(IsInside(repository,source)||IsInside(source,repository)) throw new IOException("Source and backup repository must be separate folders, with neither inside the other.");
        RejectLinks(source); RejectExistingAncestorLinks(repository);
        Directory.CreateDirectory(Path.Combine(repository,"objects"));
        var entries=new List<BackupEntry>();
        foreach(var file in EnumerateSafe(source))
        {
            ct.ThrowIfCancellationRequested(); progress?.Report(Path.GetRelativePath(source,file));
            var before=new FileInfo(file); var len=before.Length; var stamp=before.LastWriteTimeUtc;
            var tmp=Path.Combine(repository,"objects",Guid.NewGuid().ToString("N")+".tmp");
            try
            {
                string hash;
                await using(var input=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read,1024*1024,FileOptions.Asynchronous|FileOptions.SequentialScan))
                await using(var output=new FileStream(tmp,FileMode.CreateNew,FileAccess.Write,FileShare.None,1024*1024,true))
                {
                    using var digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    await using(var gzip=new GZipStream(output,CompressionLevel.Fastest,true))
                    {
                        var buffer=new byte[1024*1024];int n;
                        while((n=await input.ReadAsync(buffer,ct))>0){digest.AppendData(buffer,0,n);await gzip.WriteAsync(buffer.AsMemory(0,n),ct);}
                    }
                    hash=Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();
                }
                var after=new FileInfo(file);
                if(after.Length!=len||after.LastWriteTimeUtc!=stamp) throw new IOException($"File changed during backup: {file}");
                var obj=Path.Combine(repository,"objects",hash+".gz");
                if(File.Exists(obj)) File.Delete(tmp); else File.Move(tmp,obj);
                entries.Add(new(Path.GetRelativePath(source,file).Replace('\\','/'),len,hash,stamp));
            }
            finally { if(File.Exists(tmp)) File.Delete(tmp); }
        }
        var manifest=Path.Combine(repository,"snapshots",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N")[..8]+".json");
        await Data.SaveAsync(manifest,new BackupManifest(1,DateTime.UtcNow,source,entries));
        return manifest;
    }
    public static async Task<BackupManifest> LoadAsync(string manifest,CancellationToken ct=default)
    {
        var m=JsonSerializer.Deserialize<BackupManifest>(await File.ReadAllTextAsync(manifest,ct),Data.Json)??throw new IOException("Invalid backup manifest.");
        if(m.Version!=1) throw new IOException("Unsupported backup format.");
        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var f in m.Files)
        {
            ValidateRelative(f.Path);
            if(!names.Add(f.Path)||f.Length<0||!HashPattern().IsMatch(f.Sha256)) throw new IOException("Unsafe or duplicate manifest entry.");
        }
        return m;
    }
    public static async Task VerifyAsync(string manifest,IProgress<string>? progress=null,CancellationToken ct=default)
    {
        var m=await LoadAsync(manifest,ct);var repo=RepositoryFor(manifest);
        RejectExistingAncestorLinks(repo);
        foreach(var f in m.Files){progress?.Report(f.Path);await ReadObject(repo,f,Stream.Null,ct);}
    }
    public static async Task RestoreAsync(string manifest,string destination,IProgress<string>? progress=null,CancellationToken ct=default)
    {
        var m=await LoadAsync(manifest,ct);destination=Path.GetFullPath(destination);var repo=RepositoryFor(manifest);
        if(IsInside(destination,repo)||IsInside(repo,destination)) throw new IOException("Restore outside the backup repository.");
        RejectExistingAncestorLinks(destination);RejectExistingAncestorLinks(repo);
        if(Directory.Exists(destination)&&Directory.EnumerateFileSystemEntries(destination).Any()) throw new IOException("Restore requires a new or empty destination folder.");
        Directory.CreateDirectory(destination);
        foreach(var f in m.Files)
        {
            ct.ThrowIfCancellationRequested();progress?.Report(f.Path);
            var path=Path.GetFullPath(Path.Combine(destination,f.Path.Replace('/',Path.DirectorySeparatorChar)));
            if(!IsInside(path,destination)) throw new IOException("Unsafe restore path.");
            RejectExistingAncestorLinks(path);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp=path+".restoring-"+Guid.NewGuid().ToString("N");
            try
            {
                await using(var output=new FileStream(tmp,FileMode.CreateNew,FileAccess.Write,FileShare.None,1024*1024,true)) await ReadObject(repo,f,output,ct);
                File.Move(tmp,path);File.SetLastWriteTimeUtc(path,f.LastWriteUtc);
            }
            finally{if(File.Exists(tmp))File.Delete(tmp);}
        }
    }
    static async Task ReadObject(string repo,BackupEntry f,Stream output,CancellationToken ct)
    {
        var path=Path.Combine(repo,"objects",f.Sha256+".gz");RejectExistingAncestorLinks(path);
        await using var input=File.OpenRead(path);await using var gzip=new GZipStream(input,CompressionMode.Decompress);
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);var b=new byte[1024*1024];long total=0;int n;
        while((n=await gzip.ReadAsync(b,ct))>0){total=checked(total+n);if(total>f.Length)throw new IOException("Backup object exceeds its declared size.");hash.AppendData(b,0,n);await output.WriteAsync(b.AsMemory(0,n),ct);}
        if(total!=f.Length||Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()!=f.Sha256)throw new IOException($"Backup integrity check failed: {f.Path}");
    }
    static string RepositoryFor(string manifest)=>Directory.GetParent(Path.GetDirectoryName(Path.GetFullPath(manifest))!)!.FullName;
    public static void ValidateRelative(string path)
    {
        if(string.IsNullOrEmpty(path)||Path.IsPathRooted(path)||path.Contains('\\')||path.Contains(':')||path.Split('/').Any(s=>s is "" or "." or ".." || s.EndsWith('.')||s.EndsWith(' ')||s.Any(c=>char.IsControl(c)||"<>\"|?*".Contains(c))||Regex.IsMatch(s,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)",RegexOptions.IgnoreCase)))throw new IOException("Unsafe backup path.");
    }
    public static bool IsInside(string path,string parent)=>Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)||Path.GetFullPath(path).StartsWith(Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    static IEnumerable<string> EnumerateSafe(string root)
    {
        foreach(var item in Directory.EnumerateFileSystemEntries(root)){RejectLinks(item);if(Directory.Exists(item)){foreach(var f in EnumerateSafe(item))yield return f;}else yield return item;}
    }
    static void RejectLinks(string path){if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException($"Links/reparse points are not included: {path}");}
    public static void RejectExistingAncestorLinks(string path)
    {
        for(string? current=Path.GetFullPath(path);current!=null;current=Path.GetDirectoryName(current))if(File.Exists(current)||Directory.Exists(current))RejectLinks(current);
    }
}
