#pragma once
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <bcrypt.h>
#include <wincrypt.h>
#include <winioctl.h>
#include <ntddscsi.h>
#include <shlwapi.h>
#include <sddl.h>
#include <json.hpp>
#include <brotli/decode.h>
#include <vector>
#include <string>
#include <filesystem>
#include <fstream>
#include <sstream>
#include <functional>
#include <stdexcept>
#include <memory>
#include <algorithm>
namespace sdt {
using json=nlohmann::json;using bytes=std::vector<unsigned char>;namespace fs=std::filesystem;
inline void Require(bool ok,const char* message){if(!ok)throw std::runtime_error(message);}
inline void Win(bool ok,const char* phase){if(!ok)throw std::runtime_error(std::string(phase)+" (Windows error "+std::to_string(GetLastError())+")");}
inline void Nt(NTSTATUS status,const char* phase){if(status<0)throw std::runtime_error(std::string(phase)+" (crypto status "+std::to_string(static_cast<unsigned long>(status))+")");}
inline std::string Utf8(const std::wstring& text){if(text.empty())return {};int n=WideCharToMultiByte(CP_UTF8,WC_ERR_INVALID_CHARS,text.data(),static_cast<int>(text.size()),nullptr,0,nullptr,nullptr);Require(n>0,"Invalid Unicode");std::string r(static_cast<size_t>(n),0);WideCharToMultiByte(CP_UTF8,WC_ERR_INVALID_CHARS,text.data(),static_cast<int>(text.size()),r.data(),n,nullptr,nullptr);return r;}
inline std::wstring Wide(const std::string& text){if(text.empty())return {};int n=MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,text.data(),static_cast<int>(text.size()),nullptr,0);Require(n>0,"Invalid UTF-8");std::wstring r(static_cast<size_t>(n),0);MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,text.data(),static_cast<int>(text.size()),r.data(),n);return r;}
struct Handle{HANDLE h=INVALID_HANDLE_VALUE;explicit Handle(HANDLE value):h(value){}~Handle(){if(h!=INVALID_HANDLE_VALUE&&h)CloseHandle(h);}Handle(const Handle&)=delete;Handle& operator=(const Handle&)=delete;};
inline std::shared_ptr<Handle> File(const std::wstring& name,bool write=false,DWORD creation=OPEN_EXISTING){auto h=std::make_shared<Handle>(CreateFileW(name.c_str(),write?GENERIC_READ|GENERIC_WRITE:GENERIC_READ,FILE_SHARE_READ|FILE_SHARE_WRITE,nullptr,creation,FILE_ATTRIBUTE_NORMAL,nullptr));Win(h->h!=INVALID_HANDLE_VALUE,"Open selected source or target");return h;}
inline void Seek(HANDLE h,long long offset){LARGE_INTEGER p{};p.QuadPart=offset;Win(SetFilePointerEx(h,p,nullptr,FILE_BEGIN)!=FALSE,"Seek");}
inline bytes Read(HANDLE h,size_t count){Require(count<=0x10000000,"Read is too large");bytes data(count);DWORD n=0;Win(ReadFile(h,data.data(),static_cast<DWORD>(count),&n,nullptr)!=FALSE,"Read");Require(n==count,"Unexpected end of input");return data;}
inline void Write(HANDLE h,const bytes& data){DWORD n=0;Win(WriteFile(h,data.data(),static_cast<DWORD>(data.size()),&n,nullptr)!=FALSE,"Write");Require(n==data.size(),"Incomplete write");}
inline bytes Load(const fs::path& path,size_t limit){auto handle=File(path.wstring());LARGE_INTEGER size{};Win(GetFileSizeEx(handle->h,&size)!=FALSE,"Read file size");Require(size.QuadPart>=0&&static_cast<unsigned long long>(size.QuadPart)<=limit,"Input exceeds size limit");return Read(handle->h,static_cast<size_t>(size.QuadPart));}
inline bytes Base64(const std::string& text){DWORD n=0;Win(CryptStringToBinaryA(text.c_str(),static_cast<DWORD>(text.size()),CRYPT_STRING_BASE64,nullptr,&n,nullptr,nullptr)!=FALSE,"Decode base64");bytes b(n);Win(CryptStringToBinaryA(text.c_str(),static_cast<DWORD>(text.size()),CRYPT_STRING_BASE64,b.data(),&n,nullptr,nullptr)!=FALSE,"Decode base64");b.resize(n);return b;}
inline std::string Hex(const bytes& data){static constexpr char letters[]="0123456789abcdef";std::string r;r.reserve(data.size()*2);for(auto c:data){r+=letters[c>>4];r+=letters[c&15];}return r;}
struct Algorithm{BCRYPT_ALG_HANDLE h=nullptr;Algorithm(LPCWSTR name,ULONG flags=0){Nt(BCryptOpenAlgorithmProvider(&h,name,nullptr,flags),"Open crypto provider");}~Algorithm(){if(h)BCryptCloseAlgorithmProvider(h,0);}};
struct Hash{Algorithm algorithm;BCRYPT_HASH_HANDLE handle=nullptr;explicit Hash(const bytes& key={}):algorithm(BCRYPT_SHA256_ALGORITHM,key.empty()?0:BCRYPT_ALG_HANDLE_HMAC_FLAG){Nt(BCryptCreateHash(algorithm.h,&handle,nullptr,0,key.empty()?nullptr:const_cast<PUCHAR>(key.data()),static_cast<ULONG>(key.size()),0),"Create SHA-256");}~Hash(){if(handle)BCryptDestroyHash(handle);}void Add(const bytes& data){Nt(BCryptHashData(handle,const_cast<PUCHAR>(data.data()),static_cast<ULONG>(data.size()),0),"Hash data");}bytes Finish(){bytes out(32);Nt(BCryptFinishHash(handle,out.data(),32,0),"Finish hash");return out;}};
inline bytes Digest(const bytes& data,const bytes& key={}){Hash h(key);h.Add(data);return h.Finish();}
inline unsigned long U32(const bytes& p,size_t at){Require(at<=p.size()-4,"Invalid packet integer");unsigned long n=0;memcpy(&n,p.data()+at,4);return n;}
inline bytes Decode(const bytes& packet,const bytes& key,const std::string& purpose,size_t limit)
{
    Require(packet.size()>=46&&memcmp(packet.data(),"SOOPAV02",8)==0&&packet[9]==2&&(packet[8]&~3)==0,"Invalid protected packet header");bool encrypted=(packet[8]&1)!=0,compressed=(packet[8]&2)!=0;Require(encrypted==!key.empty(),"Packet encryption does not match the repository");size_t plain=U32(packet,10),stored=U32(packet,14),overhead=encrypted?46:62;Require(plain<=limit&&stored<=limit&&packet.size()==overhead+stored,"Invalid protected packet lengths");bytes aad(packet.begin(),packet.begin()+30);aad.insert(aad.end(),purpose.begin(),purpose.end());bytes payload(packet.begin()+static_cast<ptrdiff_t>(overhead),packet.end());
    if(encrypted)
    {
        Algorithm aes(BCRYPT_AES_ALGORITHM);Nt(BCryptSetProperty(aes.h,BCRYPT_CHAINING_MODE,reinterpret_cast<PUCHAR>(const_cast<wchar_t*>(BCRYPT_CHAIN_MODE_GCM)),sizeof(BCRYPT_CHAIN_MODE_GCM),0),"Select AES-GCM");BCRYPT_KEY_HANDLE raw=nullptr;Nt(BCryptGenerateSymmetricKey(aes.h,&raw,nullptr,0,const_cast<PUCHAR>(key.data()),static_cast<ULONG>(key.size()),0),"Open repository key");struct End{BCRYPT_KEY_HANDLE h;~End(){BCryptDestroyKey(h);}} end{raw};BCRYPT_AUTHENTICATED_CIPHER_MODE_INFO info;BCRYPT_INIT_AUTH_MODE_INFO(info);info.pbNonce=const_cast<PUCHAR>(packet.data()+18);info.cbNonce=12;info.pbTag=const_cast<PUCHAR>(packet.data()+30);info.cbTag=16;info.pbAuthData=aad.data();info.cbAuthData=static_cast<ULONG>(aad.size());bytes out(stored);ULONG got=0;Nt(BCryptDecrypt(raw,payload.data(),static_cast<ULONG>(payload.size()),&info,nullptr,0,out.data(),static_cast<ULONG>(out.size()),&got,0),"Authenticate image data (wrong password or damaged packet)");Require(got==stored,"Invalid decrypted length");payload=std::move(out);
    }
    else{bytes joined=aad;joined.insert(joined.end(),payload.begin(),payload.end());bytes sum=Digest(joined);Require(std::equal(sum.begin(),sum.end(),packet.begin()+30),"Packet checksum mismatch");}
    if(!compressed){Require(payload.size()==plain,"Plain packet length mismatch");return payload;}bytes decoded(plain);size_t got=plain;Require(BrotliDecoderDecompress(payload.size(),payload.data(),&got,decoded.data())==BROTLI_DECODER_RESULT_SUCCESS&&got==plain,"Compressed image object failed validation");return decoded;
}
inline void NoLinks(fs::path path){path=fs::absolute(path);for(auto parent=path;!parent.empty();parent=parent.parent_path()){DWORD attr=GetFileAttributesW(parent.c_str());if(attr!=INVALID_FILE_ATTRIBUTES)Require((attr&FILE_ATTRIBUTE_REPARSE_POINT)==0,"Repository paths may not traverse reparse points");if(parent==parent.parent_path())break;}}
inline bool SnapshotId(const std::string& id){if(id.size()!=52||id[8]!='T'||id[18]!='Z'||id[19]!='-')return false;for(size_t i=0;i<id.size();i++){if(i==8||i==18||i==19)continue;if(i<19){if(id[i]<'0'||id[i]>'9')return false;}else if(!((id[i]>='0'&&id[i]<='9')||(id[i]>='a'&&id[i]<='f')))return false;}return true;}
struct Store
{
    fs::path root;bytes key;std::shared_ptr<Handle> gate;
    explicit Store(const fs::path& folder,std::wstring password):root(fs::absolute(folder))
    {
        NoLinks(root);gate=std::make_shared<Handle>(CreateFileW((root/L"vault.lock").c_str(),GENERIC_READ|GENERIC_WRITE,0,nullptr,OPEN_EXISTING,0,nullptr));Win(gate->h!=INVALID_HANDLE_VALUE,"Open exclusive repository lock");auto config=json::parse(Load(root/L"vault.json",16384));Require(config.at("Version")==2,"Unsupported repository version");if(config.at("Encrypted").get<bool>()){int rounds=config.at("Iterations");Require(rounds>=600000&&rounds<=2000000,"Invalid key derivation settings");bytes salt=Base64(config.at("Salt"));Require(salt.size()==32,"Invalid repository salt");std::string utf8=Utf8(password);bytes wrapping(32);Algorithm sha(BCRYPT_SHA256_ALGORITHM,BCRYPT_ALG_HANDLE_HMAC_FLAG);Nt(BCryptDeriveKeyPBKDF2(sha.h,reinterpret_cast<PUCHAR>(utf8.data()),static_cast<ULONG>(utf8.size()),salt.data(),static_cast<ULONG>(salt.size()),static_cast<ULONGLONG>(rounds),wrapping.data(),32,0),"Derive repository wrapping key");SecureZeroMemory(utf8.data(),utf8.size());try{key=Decode(Base64(config.at("KeyCheck")),wrapping,"repository-key",32);Require(key.size()==32,"Invalid repository key");}catch(...){SecureZeroMemory(wrapping.data(),wrapping.size());SecureZeroMemory(password.data(),password.size()*sizeof(wchar_t));throw;}SecureZeroMemory(wrapping.data(),wrapping.size());}SecureZeroMemory(password.data(),password.size()*sizeof(wchar_t));
    }
    ~Store(){if(!key.empty())SecureZeroMemory(key.data(),key.size());}
    json Manifest(const std::string& id){Require(SnapshotId(id),"Invalid restore point identifier");auto path=root/L"snapshots"/Wide(id+".svm");NoLinks(path);auto result=json::parse(Decode(Load(path,0x10000080),key,"manifest:"+id,0x10000000));Require(result.at("Version")==2&&result.at("Entries").is_array()&&result.at("Entries").size()<=1000000,"Invalid image manifest");return result;}
    bytes Chunk(const std::string& id)
    {
        Require(id.size()==117&&id[52]=='/'&&SnapshotId(id.substr(0,52)),"Invalid object reference");std::string name=id.substr(53);Require(name.find_first_not_of("0123456789abcdef")==std::string::npos,"Invalid object name");auto path=root/L"objects"/Wide(id+".svo");NoLinks(path);bytes data=Decode(Load(path,4*1024*1024+128),key,"data:"+id,4*1024*1024);bytes hash=Digest(data);Require(Hex(key.empty()?hash:Digest(hash,key))==name,"Backup object content identity mismatch");return data;
    }
    json Entry(const json& manifest,const std::string& name){json found;size_t matches=0;for(auto& entry:manifest.at("Entries"))if(entry.at("Path")==name&&entry.at("Kind")=="File"){found=entry;matches++;}Require(matches==1,"Missing or duplicate image resource");Require(found.at("Resources").size()==1&&found.at("Resources")[0].at("Name")=="","Image contains unsupported streams");return found;}
    std::string Export(const json& entry,const std::function<void(const bytes&)>& write,const std::function<void(double)>& progress={})
    {
        auto resource=entry.at("Resources")[0];long long size=resource.at("Length"),done=0;Require(size>=0&&size==entry.at("Length"),"Invalid image length");Hash hash;for(const auto& id:resource.at("Chunks")){bytes data=Chunk(id.get<std::string>());Require(data.size()<=static_cast<unsigned long long>(size-done),"Image exceeds its declared length");hash.Add(data);write(data);done+=static_cast<long long>(data.size());if(progress)progress(size==0?100:100.0*static_cast<double>(done)/static_cast<double>(size));}std::string sum=Hex(hash.Finish());Require(done==size&&sum==resource.at("Sha256").get<std::string>(),"Image resource SHA-256 failed");return sum;
    }
    json Metadata(const json& manifest){auto entry=Entry(manifest,"disk.json");Require(entry.at("Length").get<long long>()<1048576,"Oversized image metadata");bytes data;Export(entry,[&](const bytes& chunk){data.insert(data.end(),chunk.begin(),chunk.end());});return json::parse(data);}
};
} // namespace sdt
