using System.Security.Cryptography;
using System.Text;
using SoopaDriveTool.Core;
partial class Program
{
    static async Task MoveChecks(string root)
    {
        foreach(bool right in new[]{true,false})foreach(string crash in new[]{"pending","written","checkpoint","prepared","metadata"})
        {
            using var disk=new MemoryStream(new byte[64*1024*1024]);long start=right?8L<<20:16L<<20,target=right?12L<<20:8L<<20,length=12L<<20;
            byte[] content=RandomNumberGenerator.GetBytes((int)length);Encoding.ASCII.GetBytes("NTFS    ").CopyTo(content,3);BitConverter.GetBytes((uint)(start/512)).CopyTo(content,28);content[510]=0x55;content[511]=0xaa;content.AsSpan(0,512).CopyTo(content.AsSpan(content.Length-512));
            DiskTables.WriteAt(disk,start,content);var table=new DiskTable("GPT",512,disk.Length,Guid.NewGuid(),33,[new(1,start,length,DiskTables.Basic,Guid.NewGuid(),"Payload")]);DiskTables.Write(disk,table);
            string journal=Path.Combine(root,"move-"+right+"-"+crash+".json");bool interrupted=false;
            try{await ResumableMove.MoveAsync(disk,table,1,target,"fixture",journal,fault:phase=>{if(phase==crash){interrupted=true;throw new IOException("Injected power loss");}});}catch(IOException)when(interrupted){}
            if(!interrupted)throw new Exception("Fault point was not reached: "+crash);
            await ResumableMove.MoveAsync(disk,table,1,target,"fixture",journal);
            BitConverter.GetBytes((uint)(target/512)).CopyTo(content,28);BitConverter.GetBytes((uint)(target/512)).CopyTo(content,content.Length-512+28);
            Check(DiskTables.ReadAt(disk,target,(int)length).SequenceEqual(content)&&DiskTables.Read(disk).Partitions.Single().Offset==target,$"Overlap-safe {(right?"right":"left")} move resumes after {crash} without losing data");
        }
    }
}
