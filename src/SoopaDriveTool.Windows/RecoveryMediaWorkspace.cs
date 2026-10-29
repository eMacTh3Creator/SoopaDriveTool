using System.Windows.Controls;
using Microsoft.Win32;
using SoopaDriveTool.Core;
namespace SoopaDriveTool.Windows;
public partial class MainWindow
{
    void RecoveryMedia()
    {
        Note("Build architecture-matched recovery media with your installed Microsoft Windows ADK and WinPE add-on. The native recovery host restores protected raw and Windows images, clones disks, performs verified overwrites, and supports eligible ATA secure erase.");
        var adk=new TextBox{Text=RecoveryMediaService.DefaultAdk};var drivers=new TextBox{IsReadOnly=true};var iso=new TextBox{IsReadOnly=true};var list=new List<string>();var panel=Stack(T("Create a recovery ISO",20,Ink,true),T("Windows ADK folder",12,Muted),adk,Btn("Choose ADK folder…",()=>adk.Text=PickFolder("Choose Assessment and Deployment Kit folder")??adk.Text),T("Optional storage and network driver folders",12,Muted),drivers,Btn("Add driver folder…",()=>{var folder=PickFolder("Select extracted signed driver INF folder for this architecture");if(folder!=null){list.Add(folder);drivers.Text=string.Join("; ",list);}}),Btn("Clear driver folders",()=>{list.Clear();drivers.Text="";}),iso,Btn("Choose ISO output…",()=>{var save=new SaveFileDialog{Filter="Bootable recovery ISO|*.iso",FileName="SoopaDriveTool-Recovery.iso"};if(save.ShowDialog(this)==true)iso.Text=save.FileName;}),Btn("Build and verify ISO",async()=>await RunTaskWithActivity("Building Windows PE recovery media",(p,ct)=>RecoveryMediaService.BuildIsoAsync(iso.Text,adk.Text,list,p,ct)),true));body.Children.Add(Card(panel));
        var target=DiskPicker(true,"USB");body.Children.Add(Card(Stack(T("Write ISO to USB",20,Ink,true),T("Select the completed ISO above, then select a USB data disk. This replaces all its content and verifies every copied file. Only USB disks of at most 256 GiB are accepted.",12,Muted),target,Btn("Review USB creation…",async()=>{if(target.SelectedItem is not DiskInfo disk)throw new IOException("Select a USB data disk.");if(Prompt("Review recovery USB","All data on "+disk.Display+" is destroyed. Type exactly DISK "+disk.Number)!="DISK "+disk.Number)return;await RunTaskWithActivity("Creating recovery USB",(p,ct)=>RecoveryMediaService.WriteUsbAsync(iso.Text,disk,p),false);}))));
    }
}
