using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using SoopaDriveTool.Core;
namespace SoopaDriveTool.Windows;

internal static class WindowsRecovery
{
    internal record RecoveryVolume(long Offset,string Root);
    internal record Registration(long Offset,string RelativePath);
    internal static Registration? ReadRegistration(string xml)
    {
        using var reader=XmlReader.Create(new StringReader(xml),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=1024*1024});
        var node=XDocument.Load(reader).Root?.Element("WinreLocation");
        if(node==null||!long.TryParse((string?)node.Attribute("offset"),out var offset)||offset<=0)return null;
        var path=((string?)node.Attribute("path")??"").TrimStart('\\').Replace('\\','/');
        BackupEngine.ValidateRelative(path);
        return new(offset,path.Replace('/', '\\'));
    }
    internal static long ReserveBytes(long imageBytes)=>Math.Max(1024L*1024*1024,checked(((imageBytes+300L*1024*1024+1048575)/1048576)*1048576));
    internal static async Task<string?> FindAsync(DiskInfo source,PartitionInfo windows)
    {
        var request=Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(source,Data.Json)));
        var json=await StorageService.PowerShellAsync("$r=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('"+request+"'))|ConvertFrom-Json; "+"""
        $d=Get-Disk -Number $r.Number
        if($d.IsBoot -or $d.IsSystem -or [string]$d.UniqueId -cne $r.UniqueId -or [string]$d.SerialNumber -cne $r.Serial -or [long]$d.Size -ne $r.Size){throw 'Recovery source changed or is the running system'}
        $volumes=@(Get-Partition -DiskNumber $d.Number | Where-Object GptType -eq '{DE94BBA4-06D1-4D40-A16A-BFD50179D6AC}' | ForEach-Object {
          $p=$_; $v=$p|Get-Volume -ErrorAction Stop
          if($v){[pscustomobject]@{Offset=[long]$p.Offset;Root=[string]$v.Path}}
        })
        ConvertTo-Json -InputObject $volumes -Compress
        """);
        var volumes=JsonSerializer.Deserialize<List<RecoveryVolume>>(json,Data.Json)??[];
        foreach(var v in volumes)if(!Regex.IsMatch(v.Root,@"^\\\\\?\\Volume\{[0-9a-fA-F-]{36}\}\\$"))throw new IOException("Invalid source recovery volume path.");
        var root=windows.Letter+@":\";
        var config=Path.Combine(root,@"Windows\System32\Recovery\ReAgent.xml");
        Registration? registration=null;
        if(File.Exists(config)){BackupEngine.RejectExistingAncestorLinks(config);registration=ReadRegistration(await File.ReadAllTextAsync(config));}
        string? Existing(string path){if(!File.Exists(path))return null;BackupEngine.RejectExistingAncestorLinks(path);return path;}
        // Only inspect the selected source disk. Never use the host's recovery image.
        if(registration!=null)
        {
            var registered=volumes.Where(v=>v.Offset==registration.Offset).Select(v=>Existing(Path.Combine(v.Root,registration.RelativePath,"Winre.wim"))).Where(p=>p!=null).ToArray();
            if(registered.Length==1)return registered[0];
        }
        var staged=Existing(Path.Combine(root,@"Windows\System32\Recovery\Winre.wim"));if(staged!=null)return staged;
        var candidates=volumes.Select(v=>Existing(Path.Combine(v.Root,@"Recovery\WindowsRE\Winre.wim"))).Append(Existing(Path.Combine(root,@"Recovery\WindowsRE\Winre.wim"))).Where(p=>p!=null).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(candidates.Length>1)throw new IOException("Multiple source recovery images found; repair the source Windows RE registration before cloning.");
        return candidates.SingleOrDefault();
    }
}
