#pragma once
#include <windows.h>
#include <winioctl.h>
#include <objbase.h>
#include <filesystem>
#include <fstream>
#include <string>
#include <stdexcept>
#include <vector>
namespace recoveryonce {
namespace fs=std::filesystem;
static constexpr wchar_t ServiceName[]=L"SoopaDriveToolRecoveryOnce";
static constexpr wchar_t FileName[]=L"SoopaDriveToolRecoveryOnce.exe";
inline void Need(bool value,const char* message){if(!value)throw std::runtime_error(message);}
struct Key{HKEY h=nullptr;~Key(){if(h)RegCloseKey(h);}};
inline std::wstring GuidText(const GUID& guid){wchar_t text[40];Need(StringFromGUID2(guid,text,40)>0,"Cannot encode recovery partition identity");return text;}
inline GUID ParseGuid(const std::wstring& text){GUID guid{};Need(SUCCEEDED(CLSIDFromString(text.c_str(),&guid)),"Invalid expected Windows partition identity");return guid;}
inline void NoLinks(fs::path path){for(path=fs::absolute(path);!path.empty();path=path.parent_path()){DWORD a=GetFileAttributesW(path.c_str());Need(a==INVALID_FILE_ATTRIBUTES||(a&FILE_ATTRIBUTE_REPARSE_POINT)==0,"Recovery registration paths cannot contain links");if(path==path.parent_path())break;}}
inline std::wstring WindowsRoot(){wchar_t path[MAX_PATH];Need(GetWindowsDirectoryW(path,MAX_PATH)>0,"Cannot locate Windows");return path;}
inline GUID Partition(const fs::path& windows){wchar_t root[MAX_PATH];Need(GetVolumePathNameW(windows.c_str(),root,MAX_PATH)!=FALSE,"Cannot bind Windows recovery to a volume");std::wstring name=L"\\\\.\\";name+=root;while(name.back()==L'\\')name.pop_back();HANDLE h=CreateFileW(name.c_str(),GENERIC_READ,FILE_SHARE_READ|FILE_SHARE_WRITE,nullptr,OPEN_EXISTING,0,nullptr);Need(h!=INVALID_HANDLE_VALUE,"Cannot open the Windows volume");PARTITION_INFORMATION_EX info{};DWORD got=0;BOOL ok=DeviceIoControl(h,IOCTL_DISK_GET_PARTITION_INFO_EX,nullptr,0,&info,sizeof(info),&got,nullptr);CloseHandle(h);Need(ok&&got>=sizeof(info)&&info.PartitionStyle==PARTITION_STYLE_GPT,"Recovery registration needs a GPT Windows volume");return info.Gpt.PartitionId;}
inline void Privilege(const wchar_t* name){HANDLE token=nullptr;Need(OpenProcessToken(GetCurrentProcess(),TOKEN_ADJUST_PRIVILEGES|TOKEN_QUERY,&token)!=FALSE,"Cannot open recovery privileges");TOKEN_PRIVILEGES p{};p.PrivilegeCount=1;BOOL found=LookupPrivilegeValueW(nullptr,name,&p.Privileges[0].Luid);p.Privileges[0].Attributes=SE_PRIVILEGE_ENABLED;SetLastError(0);BOOL ok=found&&AdjustTokenPrivileges(token,FALSE,&p,0,nullptr,nullptr);DWORD error=GetLastError();CloseHandle(token);Need(ok&&error==ERROR_SUCCESS,"Recovery registration requires backup/restore privileges");}
inline void Stage(const fs::path& windows,const GUID& expected,const fs::path& host)
{
    NoLinks(windows);NoLinks(host);Need(fs::exists(windows/L"System32"/L"Config"/L"SYSTEM"),"Missing target SYSTEM hive");Need(IsEqualGUID(Partition(windows),expected),"Target Windows partition changed");
    // WinPE is RAM-backed. Normal Windows must never stage this in its own hive.
    HKEY pe=nullptr;bool winpe=RegOpenKeyExW(HKEY_LOCAL_MACHINE,L"SYSTEM\\CurrentControlSet\\Control\\MiniNT",0,KEY_READ,&pe)==ERROR_SUCCESS;if(pe)RegCloseKey(pe);
    if(!winpe)Need(!IsEqualGUID(Partition(WindowsRoot()),expected),"Cannot stage recovery registration in the running Windows installation");
    fs::path destination=windows/L"System32"/FileName;NoLinks(destination);Need(CopyFileW(host.c_str(),destination.c_str(),FALSE)!=FALSE,"Cannot copy the one time recovery helper");
    Privilege(SE_BACKUP_NAME);Privilege(SE_RESTORE_NAME);std::wstring mount=L"SoopaRecoveryOnceStage_"+std::to_wstring(GetCurrentProcessId());Need(RegLoadKeyW(HKEY_LOCAL_MACHINE,mount.c_str(),(windows/L"System32"/L"Config"/L"SYSTEM").c_str())==ERROR_SUCCESS,"Cannot open offline recovery registration hive");
    try{
        Key root;Need(RegOpenKeyExW(HKEY_LOCAL_MACHINE,mount.c_str(),0,KEY_READ|KEY_WRITE,&root.h)==ERROR_SUCCESS,"Cannot inspect offline control sets");bool created=false;
        for(DWORD i=0;;i++){wchar_t name[256];DWORD n=256;LONG result=RegEnumKeyExW(root.h,i,name,&n,nullptr,nullptr,nullptr,nullptr);if(result==ERROR_NO_MORE_ITEMS)break;Need(result==ERROR_SUCCESS,"Cannot enumerate offline control sets");std::wstring control(name,n);if(control.size()!=13||control.rfind(L"ControlSet",0)!=0)continue;Key service;Need(RegCreateKeyExW(root.h,(control+L"\\Services\\"+ServiceName).c_str(),0,nullptr,0,KEY_WRITE,nullptr,&service.h,nullptr)==ERROR_SUCCESS,"Cannot stage recovery startup service");
            auto number=[&](const wchar_t* key,DWORD value){Need(RegSetValueExW(service.h,key,0,REG_DWORD,reinterpret_cast<const BYTE*>(&value),sizeof(value))==ERROR_SUCCESS,"Cannot set recovery startup value");};
            auto text=[&](const wchar_t* key,const std::wstring& value,DWORD type=REG_SZ){Need(RegSetValueExW(service.h,key,0,type,reinterpret_cast<const BYTE*>(value.c_str()),static_cast<DWORD>((value.size()+1)*sizeof(wchar_t)))==ERROR_SUCCESS,"Cannot set recovery startup text");};
            number(L"Type",SERVICE_WIN32_OWN_PROCESS);number(L"Start",SERVICE_AUTO_START);number(L"ErrorControl",SERVICE_ERROR_NORMAL);text(L"ObjectName",L"LocalSystem");text(L"DisplayName",L"SoopaDriveTool one time Windows recovery registration");text(L"Description",L"Registers the restored WinRE image on first startup, then removes this service.");text(L"ImagePath",L"\"%SystemRoot%\\System32\\SoopaDriveToolRecoveryOnce.exe\" --first-boot-winre "+GuidText(expected),REG_EXPAND_SZ);created=true;
        }
        Need(created,"The target has no valid Windows control set");RegFlushKey(root.h);RegCloseKey(root.h);root.h=nullptr;Need(RegUnLoadKeyW(HKEY_LOCAL_MACHINE,mount.c_str())==ERROR_SUCCESS,"Cannot close staged recovery hive");
    }catch(...){RegUnLoadKeyW(HKEY_LOCAL_MACHINE,mount.c_str());throw;}
}
inline DWORD Enable(){std::wstring exe=(fs::path(WindowsRoot())/L"System32"/L"reagentc.exe").wstring(),command=L"\""+exe+L"\" /enable";STARTUPINFOW start{sizeof(start)};PROCESS_INFORMATION p{};Need(CreateProcessW(exe.c_str(),command.data(),nullptr,nullptr,FALSE,CREATE_NO_WINDOW,nullptr,nullptr,&start,&p)!=FALSE,"Cannot start Windows recovery registration");CloseHandle(p.hThread);DWORD wait=WaitForSingleObject(p.hProcess,180000),code=ERROR_TIMEOUT;if(wait==WAIT_OBJECT_0)GetExitCodeProcess(p.hProcess,&code);else TerminateProcess(p.hProcess,ERROR_TIMEOUT);CloseHandle(p.hProcess);return code;}
inline std::wstring expectedGuid;
inline SERVICE_STATUS_HANDLE statusHandle=nullptr;
inline void State(DWORD state,DWORD error=0){SERVICE_STATUS status{};status.dwServiceType=SERVICE_WIN32_OWN_PROCESS;status.dwCurrentState=state;status.dwWin32ExitCode=error;SetServiceStatus(statusHandle,&status);}
inline void WINAPI Handler(DWORD){ }
inline void WINAPI Service(DWORD,LPWSTR*)
{
    statusHandle=RegisterServiceCtrlHandlerW(ServiceName,Handler);if(!statusHandle)return;State(SERVICE_RUNNING);DWORD code=ERROR_INVALID_DATA;bool success=false;
    try{
        GUID expected=ParseGuid(expectedGuid);Need(IsEqualGUID(Partition(WindowsRoot()),expected),"The running Windows partition differs from the staged recovery target");
        wchar_t module[MAX_PATH];Need(GetModuleFileNameW(nullptr,module,MAX_PATH)>0&&fs::path(module).filename()==FileName,"Unexpected one time recovery executable");
        for(int attempt=0;attempt<3;attempt++){code=Enable();if(code==0){success=true;break;}if(attempt<2)Sleep(5000);}
        if(success){SC_HANDLE manager=OpenSCManagerW(nullptr,nullptr,SC_MANAGER_CONNECT);Need(manager!=nullptr,"Cannot open service manager for cleanup");SC_HANDLE service=OpenServiceW(manager,ServiceName,DELETE);if(service){DeleteService(service);CloseServiceHandle(service);}CloseServiceHandle(manager);MoveFileExW(module,nullptr,MOVEFILE_DELAY_UNTIL_REBOOT);}
    }catch(...){success=false;code=ERROR_INVALID_DATA;}
    try{auto log=fs::path(WindowsRoot())/L"System32"/L"SoopaDriveToolRecoveryOnce.json";NoLinks(log);std::ofstream out(log);SYSTEMTIME now{};GetSystemTime(&now);out<<"{\"Version\":1,\"Succeeded\":"<<(success?"true":"false")<<",\"ExitCode\":"<<code<<",\"ServiceRemovesItselfOnSuccess\":true,\"CompletedUtc\":\""<<now.wYear<<'-'<<now.wMonth<<'-'<<now.wDay<<'T'<<now.wHour<<':'<<now.wMinute<<':'<<now.wSecond<<"Z\"}";}catch(...){ }
    State(SERVICE_STOPPED,success?0:code);
}
inline int Run(const std::wstring& expected){ParseGuid(expected);expectedGuid=expected;SERVICE_TABLE_ENTRYW table[]={{const_cast<LPWSTR>(ServiceName),Service},{nullptr,nullptr}};return StartServiceCtrlDispatcherW(table)?0:1;}
}
