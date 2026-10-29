using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
namespace SoopaDriveTool.Core;

// Each bounded packet is compressed before authenticated encryption. The header,
// packet purpose and lengths are authenticated; data packets and manifests cannot
// be substituted for each other. No password or derived key is serialized.
internal static class VaultCodec
{
    static readonly byte[] Magic=Encoding.ASCII.GetBytes("SOOPAV02");
    internal const int MaxPacket=256*1024*1024;
    internal static byte[] Encode(ReadOnlySpan<byte> plain,byte[]? key,string purpose)
    {
        if(plain.Length>MaxPacket)throw new IOException("Backup packet exceeds its size limit.");
        using var compressed=new MemoryStream();using(var zip=new BrotliStream(compressed,CompressionLevel.Fastest,true))zip.Write(plain);
        var bytes=compressed.ToArray();bool zipped=bytes.Length<plain.Length;if(!zipped)bytes=plain.ToArray();
        byte[] header=new byte[30];Magic.CopyTo(header,0);header[8]=(byte)((key!=null?1:0)|(zipped?2:0));header[9]=2;
        BitConverter.TryWriteBytes(header.AsSpan(10),plain.Length);BitConverter.TryWriteBytes(header.AsSpan(14),bytes.Length);
        RandomNumberGenerator.Fill(header.AsSpan(18,12));var aad=header.Concat(Encoding.UTF8.GetBytes(purpose)).ToArray();
        using var packet=new MemoryStream();packet.Write(header);
        if(key!=null){byte[] encrypted=new byte[bytes.Length],tag=new byte[16];using var aes=new AesGcm(key,16);aes.Encrypt(header.AsSpan(18,12),bytes,encrypted,tag,aad);packet.Write(tag);packet.Write(encrypted);CryptographicOperations.ZeroMemory(bytes);}
        else{packet.Write(SHA256.HashData(aad.Concat(bytes).ToArray()));packet.Write(bytes);}
        return packet.ToArray();
    }
    internal static byte[] Decode(byte[] packet,byte[]? key,string purpose)
    {
        if(packet.Length<46||!packet.AsSpan(0,8).SequenceEqual(Magic)||packet[9]!=2||(packet[8]&~3)!=0)throw new IOException("Invalid backup packet header.");
        bool encrypted=(packet[8]&1)!=0,zipped=(packet[8]&2)!=0;int length=BitConverter.ToInt32(packet,10),stored=BitConverter.ToInt32(packet,14),auth=encrypted?16:32;
        if(length<0||length>MaxPacket||stored<0||stored>MaxPacket||packet.Length!=30+auth+stored||encrypted!=(key!=null))throw new IOException("Backup packet encryption/length mismatch.");
        var aad=packet[..30].Concat(Encoding.UTF8.GetBytes(purpose)).ToArray();byte[] bytes=new byte[stored];
        if(encrypted){using var aes=new AesGcm(key!,16);aes.Decrypt(packet.AsSpan(18,12),packet.AsSpan(46),packet.AsSpan(30,16),bytes,aad);}
        else{packet.AsSpan(62).CopyTo(bytes);if(!CryptographicOperations.FixedTimeEquals(packet.AsSpan(30,32),SHA256.HashData(aad.Concat(bytes).ToArray())))throw new IOException("Backup packet checksum failed.");}
        if(!zipped){if(bytes.Length!=length)throw new IOException("Backup packet length mismatch.");return bytes;}
        using var input=new MemoryStream(bytes);using var zip=new BrotliStream(input,CompressionMode.Decompress);byte[] result=new byte[length];zip.ReadExactly(result);if(zip.ReadByte()!=-1)throw new IOException("Compressed backup packet exceeds its declared length.");return result;
    }
}
