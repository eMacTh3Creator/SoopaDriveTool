#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <objbase.h>
#include <oleauto.h>
#include <vss.h>
#include <vswriter.h>
#include <vsbackup.h>
#include <wrl/client.h>
#include <string>
#include <vector>
#include <iostream>
#include <sstream>
#include <stdexcept>
#include <iomanip>
#include "RecoveryFirstBoot.hpp"
using Microsoft::WRL::ComPtr;
static void Check(HRESULT hr,const char* phase){if(FAILED(hr)){std::ostringstream s;s<<phase<<" failed (0x"<<std::hex<<(unsigned long)hr<<")";throw std::runtime_error(s.str());}}
static std::string Utf8(const std::wstring& s){if(s.empty())return "";int n=WideCharToMultiByte(CP_UTF8,WC_ERR_INVALID_CHARS,s.data(),(int)s.size(),nullptr,0,nullptr,nullptr);if(!n)throw std::runtime_error("Invalid Unicode");std::string r(n,0);WideCharToMultiByte(CP_UTF8,WC_ERR_INVALID_CHARS,s.data(),(int)s.size(),r.data(),n,nullptr,nullptr);return r;}
static std::string Json(const std::wstring& s){std::string r="\"";for(unsigned char c:Utf8(s)){if(c=='\\'||c=='\"'){r+='\\';r+=(char)c;}else if(c<32){char b[7];sprintf_s(b,"\\u%04x",c);r+=b;}else r+=(char)c;}return r+'\"';}
static std::wstring Guid(GUID id){wchar_t b[40];if(!StringFromGUID2(id,b,40))throw std::runtime_error("GUID encoding failed");return b;}
static void Privilege(const wchar_t* name){HANDLE raw=nullptr;if(!OpenProcessToken(GetCurrentProcess(),TOKEN_ADJUST_PRIVILEGES|TOKEN_QUERY,&raw))throw std::runtime_error("Administrator privileges are required");TOKEN_PRIVILEGES t{};t.PrivilegeCount=1;if(!LookupPrivilegeValueW(nullptr,name,&t.Privileges[0].Luid)){CloseHandle(raw);throw std::runtime_error("Privilege is unavailable");}t.Privileges[0].Attributes=SE_PRIVILEGE_ENABLED;SetLastError(ERROR_SUCCESS);BOOL ok=AdjustTokenPrivileges(raw,FALSE,&t,0,nullptr,nullptr);DWORD err=GetLastError();CloseHandle(raw);if(!ok||err!=ERROR_SUCCESS)throw std::runtime_error("Required backup/restore privilege is unavailable");}
static void Wait(ComPtr<IVssAsync> task,const char* phase){Check(task->Wait(180000),phase);HRESULT result=S_OK;Check(task->QueryStatus(&result,nullptr),phase);if(result==VSS_S_ASYNC_PENDING){task->Cancel();throw std::runtime_error("VSS request timed out");}Check(result,phase);if(result!=S_OK&&result!=VSS_S_ASYNC_FINISHED)throw std::runtime_error("VSS operation was cancelled or did not complete");}
static std::string Writers(IVssBackupComponents* backup)
{
    ComPtr<IVssAsync> task;Check(backup->GatherWriterStatus(&task),"Gather writer status");Wait(task,"Writer status");UINT count=0;Check(backup->GetWriterStatusCount(&count),"Writer count");std::string result="[";
    try{for(UINT i=0;i<count;i++){VSS_ID instance{},writer{};BSTR name=nullptr;VSS_WRITER_STATE state{};HRESULT failure=S_OK;Check(backup->GetWriterStatus(i,&instance,&writer,&name,&state,&failure),"Read writer status");std::wstring label=name?name:L"";SysFreeString(name);if(FAILED(failure)||state>=VSS_WS_FAILED_AT_IDENTIFY)throw std::runtime_error("VSS writer failed: "+Utf8(label));if(i)result+=",";result+="{\"Name\":"+Json(label)+",\"State\":"+std::to_string((int)state)+",\"Succeeded\":true}";}}catch(...){backup->FreeWriterStatus();throw;}
    Check(backup->FreeWriterStatus(),"Release writer status");return result+"]";
}
struct SnapshotOwner
{
    ComPtr<IVssBackupComponents> backup;VSS_ID set=GUID_NULL;bool completed=false;
    ~SnapshotOwner(){if(backup){if(!completed)backup->AbortBackup();if(set!=GUID_NULL){LONG removed=0;VSS_ID pending{};backup->DeleteSnapshots(set,VSS_OBJECT_SNAPSHOT_SET,TRUE,&removed,&pending);}}}
};
static int Snapshot(int argc,wchar_t** argv)
{
    if(argc<3)throw std::runtime_error("Specify one or more volume GUID roots");Privilege(SE_BACKUP_NAME);Privilege(SE_RESTORE_NAME);
    Check(CoInitializeEx(nullptr,COINIT_MULTITHREADED),"Initialize COM");struct End{~End(){CoUninitialize();}} end;
    HRESULT security=CoInitializeSecurity(nullptr,-1,nullptr,nullptr,RPC_C_AUTHN_LEVEL_PKT_PRIVACY,RPC_C_IMP_LEVEL_IMPERSONATE,nullptr,EOAC_NONE,nullptr);if(security!=RPC_E_TOO_LATE)Check(security,"Initialize COM security");
    SnapshotOwner owner;Check(CreateVssBackupComponents(&owner.backup),"Create VSS requester");auto backup=owner.backup.Get();Check(backup->InitializeForBackup(),"Initialize backup");Check(backup->SetContext(VSS_CTX_BACKUP),"Set backup context");Check(backup->SetBackupState(FALSE,TRUE,VSS_BT_COPY,FALSE),"Set copy-backup state");
    ComPtr<IVssAsync> task;Check(backup->GatherWriterMetadata(&task),"Gather writer metadata");Wait(task,"Writer metadata");task.Reset();Check(backup->StartSnapshotSet(&owner.set),"Start snapshot set");
    std::vector<std::pair<std::wstring,VSS_ID>> volumes;
    for(int i=2;i<argc;i++){std::wstring volume=argv[i];if(volume.rfind(L"\\\\?\\Volume{",0)!=0||volume.back()!=L'\\')throw std::runtime_error("A canonical local volume GUID root is required");BOOL supported=FALSE;Check(backup->IsVolumeSupported(GUID_NULL,const_cast<wchar_t*>(volume.c_str()),&supported),"Check VSS volume");if(!supported)throw std::runtime_error("The selected volume cannot be snapshotted");VSS_ID id{};Check(backup->AddToSnapshotSet(const_cast<wchar_t*>(volume.c_str()),GUID_NULL,&id),"Add volume");volumes.push_back({volume,id});}
    Check(backup->PrepareForBackup(&task),"Prepare backup");Wait(task,"Prepare writers");task.Reset();Writers(backup);
    Check(backup->DoSnapshotSet(&task),"Create snapshot");Wait(task,"Freeze/snapshot/thaw");task.Reset();auto writers=Writers(backup);
    std::string json="{\"SnapshotSetId\":"+Json(Guid(owner.set))+",\"Volumes\":[";
    for(size_t i=0;i<volumes.size();i++){VSS_SNAPSHOT_PROP prop{};Check(backup->GetSnapshotProperties(volumes[i].second,&prop),"Read snapshot properties");if(i)json+=",";json+="{\"Volume\":"+Json(volumes[i].first)+",\"SnapshotId\":"+Json(Guid(volumes[i].second))+",\"Device\":"+Json(prop.m_pwszSnapshotDeviceObject)+"}";VssFreeSnapshotProperties(&prop);}
    json+="],\"Writers\":"+writers+"}";std::cout<<json<<std::endl;
    std::string command;if(!std::getline(std::cin,command)||command!="complete")return 2;
    Check(backup->BackupComplete(&task),"Complete backup");Wait(task,"Complete writer backup");task.Reset();writers=Writers(backup);owner.completed=true;Check(backup->FreeWriterMetadata(),"Release writer metadata");
    std::cout<<"{\"Completed\":true,\"Writers\":"<<writers<<"}"<<std::endl;return 0;
}
int wmain(int argc,wchar_t** argv)
{
    try{if(argc==3&&std::wstring(argv[1])==L"--first-boot-winre")return recoveryonce::Run(argv[2]);if(argc==4&&std::wstring(argv[1])==L"stage-recovery"){wchar_t module[MAX_PATH];if(!GetModuleFileNameW(nullptr,module,MAX_PATH))throw std::runtime_error("Native helper path is unavailable");recoveryonce::Stage(argv[2],recoveryonce::ParseGuid(argv[3]),module);std::cout<<"One time Windows recovery registration staged\n";return 0;}if(argc>=2&&std::wstring(argv[1])==L"vss-session")return Snapshot(argc,argv);if(argc==2&&std::wstring(argv[1])==L"version"){std::cout<<"SoopaDriveTool native engine 1.0\n";return 0;}throw std::runtime_error("Unknown native command");}
    catch(const std::exception& e){std::cerr<<e.what()<<std::endl;return 1;}
}
