using System.Windows;
using System.Windows.Controls;
namespace SoopaDriveTool.Windows;
public partial class MainWindow
{
    readonly Dictionary<string,int> workspaceTabs=[];
    void TabWorkspace(int start,params (string Title,int Elements)[] groups)
    {
        var items=body.Children.Cast<UIElement>().Skip(start).ToArray();if(groups.Sum(g=>g.Elements)!=items.Length)throw new InvalidOperationException("Workspace sections do not match the rendered controls.");foreach(var child in items)body.Children.Remove(child);var tabs=new TabControl{BorderThickness=new Thickness(0),Background=System.Windows.Media.Brushes.Transparent,Padding=new Thickness(0,16,0,0)};int index=0;
        foreach(var group in groups){var panel=new StackPanel();for(int i=0;i<group.Elements;i++)panel.Children.Add(items[index++]);tabs.Items.Add(new TabItem{Header=group.Title,Content=panel,Padding=new Thickness(16,10,16,10)});}tabs.SelectedIndex=Math.Clamp(workspaceTabs.GetValueOrDefault(page),0,groups.Length-1);string key=page;tabs.SelectionChanged+=(_,e)=>{if(ReferenceEquals(e.Source,tabs))workspaceTabs[key]=tabs.SelectedIndex;};body.Children.Add(tabs);
    }
}
