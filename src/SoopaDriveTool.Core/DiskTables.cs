using System.Buffers.Binary;
using System.Text;
namespace SoopaDriveTool.Core;
public record TablePartition(int Index,long Offset,long Length,Guid Type,Guid Id,string Name,ulong Attributes=0,byte MbrType=7,bool Active=false,bool Logical=false,long EbrOffset=0);
public record DiskTable(string Style,int SectorSize,long Length,Guid Id,uint Signature,List<TablePartition> Partitions,bool RecoveredFromBackup=false,int ExtendedSlot=0);
public static class DiskTables
{
    public static readonly Guid Basic=Guid.Parse("ebd0a0a2-b9e5-4433-87c0-68b6b72699c7"),Efi=Guid.Parse("c12a7328-f81f-11d2-ba4b-00a0c93ec93b"),Recovery=Guid.Parse("de94bba4-06d1-4d40-a16a-bfd50179d6ac"),Reserved=Guid.Parse("e3c9e316-0b5c-4db8-817d-f92df00215ae");
    public static DiskTable Read(Stream stream,int sector=512,bool allowBackup=true)
    {
        if(sector is not (512 or 4096)||stream.Length<sector*64L||stream.Length%sector!=0)throw new IOException("Invalid disk geometry.");byte[] mbr=ReadAt(stream,0,sector);uint signature=BinaryPrimitives.ReadUInt32LittleEndian(mbr.AsSpan(440));bool protective=false;for(int i=0;i<4;i++)protective|=mbr[450+16*i]==0xee;
        bool primary=Encoding.ASCII.GetString(ReadAt(stream,sector,sector),0,8)=="EFI PART",backup=Encoding.ASCII.GetString(ReadAt(stream,stream.Length-sector,sector),0,8)=="EFI PART";
        if(protective||primary||backup)
        {
            try{return ReadGpt(stream,sector,1,signature,false);}catch(IOException)when(allowBackup){return ReadGpt(stream,sector,stream.Length/sector-1,signature,true);}
        }
        if(mbr[510]!=0x55||mbr[511]!=0xaa)throw new IOException("No valid partition table signature.");var parts=new List<TablePartition>();int extendedSlot=0;
        for(int i=0;i<4;i++)
        {
            var e=mbr.AsSpan(446+16*i,16);byte type=e[4];if(type==0)continue;long offset=(long)BinaryPrimitives.ReadUInt32LittleEndian(e[8..])*sector,length=(long)BinaryPrimitives.ReadUInt32LittleEndian(e[12..])*sector;ValidateRange(offset,length,stream.Length,sector);
            if(IsExtended(type))
            {
                if(extendedSlot!=0)throw new IOException("Multiple MBR extended containers are not supported.");extendedSlot=i+1;long cursor=offset;var visited=new HashSet<long>();
                while(true)
                {
                    if(!visited.Add(cursor)||visited.Count>1024||cursor<offset||cursor>offset+length-sector)throw new IOException("Unsafe or cyclic extended-partition chain.");byte[] ebr=ReadAt(stream,cursor,sector);if(ebr[510]!=0x55||ebr[511]!=0xaa)throw new IOException("Invalid extended partition signature.");var first=ebr.AsSpan(446,16);if(first[4]==0||IsExtended(first[4]))throw new IOException("Invalid logical partition entry.");long start=checked(cursor+(long)BinaryPrimitives.ReadUInt32LittleEndian(first[8..])*sector),size=(long)BinaryPrimitives.ReadUInt32LittleEndian(first[12..])*sector;ValidateRange(start,size,stream.Length,sector);if(start<=cursor||start+size>offset+length)throw new IOException("Logical partition escapes its extended container.");parts.Add(new(5+visited.Count-1,start,size,TypeFor(first[4]),Guid.Empty,"Logical partition",0,first[4],false,true,cursor));var next=ebr.AsSpan(462,16);if(next[4]==0)break;if(!IsExtended(next[4]))throw new IOException("Invalid extended partition link.");cursor=checked(offset+(long)BinaryPrimitives.ReadUInt32LittleEndian(next[8..])*sector);
                }
            }
            else parts.Add(new(i+1,offset,length,TypeFor(type),Guid.Empty,"Primary partition",0,type,e[0]==0x80));
        }
        ValidatePartitions(parts,stream.Length,sector);return new("MBR",sector,stream.Length,Guid.Empty,signature,parts,false,extendedSlot);
    }
    static DiskTable ReadGpt(Stream stream,int sector,long lba,uint signature,bool recovered)
    {
        byte[] h=ReadAt(stream,lba*sector,sector);if(Encoding.ASCII.GetString(h,0,8)!="EFI PART")throw new IOException("GPT header is unavailable.");uint width=BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(12));if(width<92||width>sector)throw new IOException("Invalid GPT header length.");uint expected=BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(16));h.AsSpan(16,4).Clear();if(PartitionInspector.Crc32(h.AsSpan(0,(int)width))!=expected)throw new IOException("GPT header checksum failed.");
        long current=ToLong(BinaryPrimitives.ReadUInt64LittleEndian(h.AsSpan(24))),alternate=ToLong(BinaryPrimitives.ReadUInt64LittleEndian(h.AsSpan(32))),first=ToLong(BinaryPrimitives.ReadUInt64LittleEndian(h.AsSpan(40))),last=ToLong(BinaryPrimitives.ReadUInt64LittleEndian(h.AsSpan(48))),table=ToLong(BinaryPrimitives.ReadUInt64LittleEndian(h.AsSpan(72)));uint count=BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(80)),entrySize=BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(84));
        if(current!=lba||alternate<1||alternate>=stream.Length/sector||alternate==current||first<2||last<first||last>=stream.Length/sector-1||entrySize!=128||count<1||count>4096||table<2)throw new IOException("Invalid GPT geometry or unsupported entry width.");int bytes=checked((int)count*128);if(table>stream.Length/sector-(bytes+sector-1)/sector)throw new IOException("GPT array escapes the disk.");byte[] entries=ReadAt(stream,table*sector,bytes);if(PartitionInspector.Crc32(entries)!=BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(88)))throw new IOException("GPT partition-array checksum failed.");
        long arraySectors=(bytes+sector-1)/sector;if(!recovered&&first<table+arraySectors||recovered&&table<=last)throw new IOException("GPT partition array overlaps its usable region.");var parts=new List<TablePartition>();var ids=new HashSet<Guid>();
        for(int i=0;i<count;i++){var e=entries.AsSpan(i*128,128);Guid type=new(e[..16]);if(type==Guid.Empty)continue;Guid id=new(e.Slice(16,16));long start=ToLong(BinaryPrimitives.ReadUInt64LittleEndian(e[32..])),end=ToLong(BinaryPrimitives.ReadUInt64LittleEndian(e[40..]));if(start<first||end<start||end>last||id==Guid.Empty||!ids.Add(id))throw new IOException("Unsafe GPT partition bounds or duplicate identity.");parts.Add(new(i+1,checked(start*sector),checked((end-start+1)*sector),type,id,Encoding.Unicode.GetString(e.Slice(56,72)).TrimEnd('\0'),BinaryPrimitives.ReadUInt64LittleEndian(e[48..])));}
        ValidatePartitions(parts,stream.Length,sector);return new("GPT",sector,stream.Length,new Guid(h.AsSpan(56,16)),signature,parts,recovered);
    }
    static long ToLong(ulong n){if(n>long.MaxValue)throw new IOException("Partition integer exceeds the supported range.");return (long)n;}
    public static void ValidatePartitions(IReadOnlyList<TablePartition> parts,long length,int sector)
    {
        foreach(var p in parts)ValidateRange(p.Offset,p.Length,length,sector);var sorted=parts.OrderBy(p=>p.Offset).ToArray();for(int i=1;i<sorted.Length;i++)if(sorted[i-1].Offset>sorted[i].Offset-sorted[i-1].Length)throw new IOException("Partitions overlap.");if(parts.Select(p=>p.Index).Distinct().Count()!=parts.Count||parts.Any(p=>p.Index<1))throw new IOException("Invalid partition numbers.");
    }
    static void ValidateRange(long offset,long length,long disk,int sector){if(offset<sector||length<sector||offset%sector!=0||length%sector!=0||length>disk||offset>disk-length)throw new IOException("Partition is misaligned or exceeds the disk.");}
    static bool IsExtended(byte type)=>type is 5 or 15 or 133;
    static Guid TypeFor(byte type)=>type==0xef?Efi:type==0x27?Recovery:Basic;
    static byte MbrType(TablePartition p)=>p.Type==Efi?(byte)0xef:p.Type==Recovery?(byte)0x27:p.MbrType;
    public static DiskTable Convert(DiskTable table,string style)
    {
        if(style is not ("GPT" or "MBR")||style==table.Style)throw new IOException("Choose a different basic partition-table style.");
        if(style=="GPT")return table with{Style="GPT",Id=Guid.NewGuid(),Partitions=table.Partitions.Select((p,i)=>p with{Index=i+1,Id=Guid.NewGuid(),Logical=false,EbrOffset=0,Type=TypeFor(p.MbrType)}).ToList(),RecoveredFromBackup=false,ExtendedSlot=0};
        var parts=table.Partitions.Where(p=>p.Type!=Reserved).ToList();if(parts.Count>4)throw new IOException("MBR supports at most four primary entries; prepare logical partitions before conversion.");if(table.Length/table.SectorSize>uint.MaxValue)throw new IOException("This disk exceeds MBR's addressable range at its sector size.");
        return table with{Style="MBR",Id=Guid.Empty,Signature=table.Signature==0?BitConverter.ToUInt32(System.Security.Cryptography.RandomNumberGenerator.GetBytes(4)):table.Signature,Partitions=parts.Select((p,i)=>p with{Index=i+1,Id=Guid.Empty,MbrType=MbrType(p),Logical=false,EbrOffset=0}).ToList(),RecoveredFromBackup=false,ExtendedSlot=0};
    }
    public static DiskTable Move(DiskTable table,int index,long offset)
    {
        var p=table.Partitions.Single(p=>p.Index==index);if(offset%(1024*1024)!=0)throw new IOException("Move destinations must be aligned to 1 MiB.");var parts=table.Partitions.Select(x=>x.Index==index?x with{Offset=offset,EbrOffset=x.Logical?offset-table.SectorSize:x.EbrOffset}:x).ToList();ValidatePartitions(parts,table.Length,table.SectorSize);if(table.Style=="GPT"&&(offset<1024*1024||offset+p.Length>table.Length-1024*1024))throw new IOException("The move overlaps GPT metadata.");return table with{Partitions=parts};
    }
    public static DiskTable SetLogical(DiskTable table,int index,bool logical)
    {
        if(table.Style!="MBR")throw new IOException("Primary/logical conversion applies to MBR disks.");var selected=table.Partitions.Single(p=>p.Index==index);if(selected.Logical==logical)return table;var parts=table.Partitions.ToList();parts.Remove(selected);
        if(logical){long ebr=selected.Offset-table.SectorSize;if(ebr<table.SectorSize||parts.Any(p=>ebr>=p.Offset&&ebr<p.Offset+p.Length))throw new IOException("Logical conversion needs a free sector before the partition. Move it to create a gap first.");if(parts.Count(p=>!p.Logical)>3)throw new IOException("No MBR slot is available for the extended container.");parts.Add(selected with{Logical=true,Active=false,EbrOffset=ebr,Index=Enumerable.Range(5,1024).First(i=>parts.All(p=>p.Index!=i))});}
        else{if(parts.Count(p=>!p.Logical)+(parts.Any(p=>p.Logical)?1:0)>=4)throw new IOException("No free MBR primary slot.");int slot=Enumerable.Range(1,4).First(i=>parts.All(p=>p.Index!=i));parts.Add(selected with{Logical=false,EbrOffset=0,Index=slot});}
        return table with{Partitions=parts,ExtendedSlot=0};
    }
    public static void Write(Stream stream,DiskTable table)
    {
        if(!stream.CanWrite||stream.Length!=table.Length)throw new IOException("Writable disk geometry changed.");ValidatePartitions(table.Partitions,table.Length,table.SectorSize);if(table.Style=="GPT")WriteGpt(stream,table);else if(table.Style=="MBR")WriteMbr(stream,table);else throw new IOException("Unsupported table style.");stream.Flush();
    }
    static void WriteGpt(Stream stream,DiskTable t)
    {
        if(t.Id==Guid.Empty||t.Partitions.Select(p=>p.Id).Distinct().Count()!=t.Partitions.Count)throw new IOException("GPT identities must be nonempty and unique.");
        int count=Math.Max(128,t.Partitions.Count==0?128:t.Partitions.Max(p=>p.Index));if(count>4096)throw new IOException("Too many GPT entries.");int sectors=(count*128+t.SectorSize-1)/t.SectorSize;long last=t.Length/t.SectorSize-1,firstUsable=2+sectors,lastUsable=last-sectors-1;byte[] entries=new byte[sectors*t.SectorSize];
        foreach(var p in t.Partitions){long start=p.Offset/t.SectorSize,end=(p.Offset+p.Length)/t.SectorSize-1;if(start<firstUsable||end>lastUsable||p.Id==Guid.Empty||p.Type==Guid.Empty||p.Name.Length>36)throw new IOException("GPT entry cannot be represented safely.");var e=entries.AsSpan((p.Index-1)*128,128);p.Type.TryWriteBytes(e);p.Id.TryWriteBytes(e[16..]);BinaryPrimitives.WriteUInt64LittleEndian(e[32..],(ulong)start);BinaryPrimitives.WriteUInt64LittleEndian(e[40..],(ulong)end);BinaryPrimitives.WriteUInt64LittleEndian(e[48..],p.Attributes);Encoding.Unicode.GetBytes(p.Name).CopyTo(e[56..]);}
        uint crc=PartitionInspector.Crc32(entries.AsSpan(0,count*128));byte[] Header(long current,long alternate,long array){byte[] h=new byte[t.SectorSize];Encoding.ASCII.GetBytes("EFI PART").CopyTo(h,0);Put32(h,8,0x10000);Put32(h,12,92);Put64(h,24,(ulong)current);Put64(h,32,(ulong)alternate);Put64(h,40,(ulong)firstUsable);Put64(h,48,(ulong)lastUsable);t.Id.TryWriteBytes(h.AsSpan(56));Put64(h,72,(ulong)array);Put32(h,80,(uint)count);Put32(h,84,128);Put32(h,88,crc);Put32(h,16,PartitionInspector.Crc32(h.AsSpan(0,92)));return h;}
        // Complete secondary metadata before committing the primary table/header.
        WriteAt(stream,(last-sectors)*t.SectorSize,entries);WriteAt(stream,last*t.SectorSize,Header(last,1,last-sectors));stream.Flush();WriteAt(stream,2L*t.SectorSize,entries);WriteAt(stream,t.SectorSize,Header(1,last,2));
        byte[] mbr=ReadAt(stream,0,t.SectorSize);mbr.AsSpan(446,64).Clear();Entry(mbr.AsSpan(446,16),0xee,1,(uint)Math.Min(uint.MaxValue,last),false);mbr[510]=0x55;mbr[511]=0xaa;WriteAt(stream,0,mbr);
    }
    static void WriteMbr(Stream stream,DiskTable t)
    {
        long sectors=t.Length/t.SectorSize;if(sectors>uint.MaxValue)throw new IOException("Disk exceeds MBR's addressable range.");var primaries=t.Partitions.Where(p=>!p.Logical).ToList();var logical=t.Partitions.Where(p=>p.Logical).OrderBy(p=>p.Offset).ToList();if(primaries.Count+(logical.Count>0?1:0)>4)throw new IOException("MBR primary slots are exhausted.");
        var pendingEbr=new List<(long Offset,byte[] Bytes)>();
        byte[] mbr=ReadAt(stream,0,t.SectorSize);mbr.AsSpan(446,64).Clear();Put32(mbr,440,t.Signature);var used=new HashSet<int>();foreach(var p in primaries){if(p.Index>4||!used.Add(p.Index))throw new IOException("Invalid MBR primary slot.");Entry(mbr.AsSpan(446+16*(p.Index-1),16),MbrType(p),checked((uint)(p.Offset/t.SectorSize)),checked((uint)(p.Length/t.SectorSize)),p.Active);}
        if(logical.Count>0)
        {
            int slot=t.ExtendedSlot is >=1 and <=4&&!used.Contains(t.ExtendedSlot)?t.ExtendedSlot:Enumerable.Range(1,4).First(i=>!used.Contains(i));long start=logical.Min(p=>p.EbrOffset),end=logical.Max(p=>p.Offset+p.Length);if(start<t.SectorSize||primaries.Any(p=>p.Offset<end&&p.Offset+p.Length>start))throw new IOException("Extended container overlaps a primary partition.");Entry(mbr.AsSpan(446+(slot-1)*16,16),15,(uint)(start/t.SectorSize),(uint)((end-start)/t.SectorSize),false);
            for(int i=0;i<logical.Count;i++){var p=logical[i];if(p.EbrOffset>=p.Offset||p.EbrOffset<start||t.Partitions.Any(x=>p.EbrOffset>=x.Offset&&p.EbrOffset<x.Offset+x.Length))throw new IOException("An EBR would overwrite file data.");byte[] ebr=new byte[t.SectorSize];Entry(ebr.AsSpan(446,16),MbrType(p),(uint)((p.Offset-p.EbrOffset)/t.SectorSize),(uint)(p.Length/t.SectorSize),false);if(i+1<logical.Count){long next=logical[i+1].EbrOffset;Entry(ebr.AsSpan(462,16),15,(uint)((next-start)/t.SectorSize),(uint)((end-next)/t.SectorSize),false);}ebr[510]=0x55;ebr[511]=0xaa;pendingEbr.Add((p.EbrOffset,ebr));}
        }
        foreach(var change in pendingEbr)WriteAt(stream,change.Offset,change.Bytes);
        mbr[510]=0x55;mbr[511]=0xaa;WriteAt(stream,0,mbr);
        // Remove stale GPT signatures only outside all preserved partitions.
        foreach(long offset in new[]{(long)t.SectorSize,t.Length-t.SectorSize})if(!t.Partitions.Any(p=>offset>=p.Offset&&offset<p.Offset+p.Length)){var bytes=ReadAt(stream,offset,t.SectorSize);if(Encoding.ASCII.GetString(bytes,0,8)=="EFI PART"){bytes.AsSpan(0,8).Clear();WriteAt(stream,offset,bytes);}}
    }
    static void Entry(Span<byte> e,byte type,uint start,uint length,bool active){e.Clear();e[0]=active?(byte)0x80:(byte)0;e[1]=0xfe;e[2]=0xff;e[3]=0xff;e[4]=type;e[5]=0xfe;e[6]=0xff;e[7]=0xff;BinaryPrimitives.WriteUInt32LittleEndian(e[8..],start);BinaryPrimitives.WriteUInt32LittleEndian(e[12..],length);}
    static void Put32(byte[] b,int offset,uint n)=>BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(offset),n);
    static void Put64(byte[] b,int offset,ulong n)=>BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(offset),n);
    public static byte[] ReadAt(Stream stream,long offset,int count){if(offset<0||count<0||offset>stream.Length-count)throw new IOException("Read exceeds disk bounds.");byte[] b=new byte[count];stream.Position=offset;stream.ReadExactly(b);return b;}
    public static void WriteAt(Stream stream,long offset,byte[] bytes){if(offset<0||offset>stream.Length-bytes.Length)throw new IOException("Write exceeds disk bounds.");stream.Position=offset;stream.Write(bytes);}
}
