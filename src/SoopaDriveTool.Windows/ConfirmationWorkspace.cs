using System.Windows;
using System.Windows.Controls;
namespace SoopaDriveTool.Windows;
public partial class MainWindow
{
    Window CreateConfirmationDialog(string heading,string message)
    {
        var dialog=new Window{Title=heading,Owner=this,Width=560,SizeToContent=SizeToContent.Height,MaxHeight=650,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize};
        var no=Btn("No",()=>dialog.DialogResult=false);no.IsDefault=true;no.IsCancel=true;
        var yes=Btn("Yes",()=>dialog.DialogResult=true,true);
        var answers=new WrapPanel();answers.Children.Add(no);answers.Children.Add(yes);
        dialog.Content=new Border{Padding=new Thickness(26),Child=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=Stack(T("Are you sure?",23,Ink,true),T(message,13,Muted,false,new Thickness(0,16,0,20)),answers)}};
        dialog.Loaded+=(_,_)=>no.Focus();
        return dialog;
    }
    bool ConfirmAction(string heading,string message)=>CreateConfirmationDialog(heading,message).ShowDialog()==true;
}
