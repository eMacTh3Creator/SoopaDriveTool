using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SoopaDriveTool.Core;
namespace SoopaDriveTool.Windows;
internal record PartitionMapTarget(DiskInfo Disk,PartitionInfo? Partition,long Offset,long Length);
public partial class MainWindow
{
    bool partitionMenuOpen;
    readonly Dictionary<string,PartitionInfo> partitionSelections=[];
    static string PartitionSelectionKey(DiskInfo d)=>d.Number+":"+d.Identity;
    void OpenPartition(DiskInfo disk,PartitionInfo partition)
    {
        diskSelections["Disk manager:"]=disk;partitionSelections[PartitionSelectionKey(disk)]=partition;Navigate("Disk manager");
    }
    Border DiskCard(DiskInfo d,Action<PartitionInfo>? select=null,Func<PartitionInfo,ContextMenu>? menu=null,PartitionInfo? selected=null)
    {
        var panel=Stack();var head=new Grid();head.ColumnDefinitions.Add(new());head.ColumnDefinitions.Add(new(){Width=GridLength.Auto});head.Children.Add(Stack(T($"Disk {d.Number}   {d.Name}",15,Ink,true),T($"{Data.Size(d.Size)}  ·  {d.Bus}  ·  {d.Style}  ·  {(d.Offline?"Offline":"Online")}"+(d.Protected?"  ·  System protected":""),12,Muted,false,new Thickness(0,5,0,0))));
        var badge=new Border{ToolTip="Windows storage-provider status. Read full SMART for device attributes and logs.",Background=d.Health=="Healthy"?B("#E4F7F1"):B("#FFF2DD"),CornerRadius=new CornerRadius(6),Padding=new Thickness(10,6,10,6),VerticalAlignment=VerticalAlignment.Top,Child=T(d.Health,11,d.Health=="Healthy"?B("#188A71"):B("#9C6C20"),true)};Grid.SetColumn(badge,1);head.Children.Add(badge);panel.Children.Add(head);
        var map=new Grid{Height=82,Margin=new Thickness(0,18,0,0)};long cursor=0;int column=0;string[] colors=["#5355E8","#269F89","#367AB6","#7968C2"];
        foreach(var part in d.Partitions.OrderBy(p=>p.Offset))
        {
            if(part.Offset>cursor)Segment(null,cursor,part.Offset-cursor);
            Segment(part,part.Offset,part.Size);cursor=part.Offset+part.Size;
        }
        if(cursor<d.Size)Segment(null,cursor,d.Size-cursor);
        void Segment(PartitionInfo? part,long offset,long length)
        {
            double fraction=d.Size>0?(double)length/d.Size:0;map.ColumnDefinitions.Add(new(){Width=new GridLength(Math.Max(.035,fraction),GridUnitType.Star)});
            string label=part==null?"Unallocated":(part.Letter.Length>0?part.Letter+": ":"")+(part.Label.Length>0?part.Label:part.Type);
            string description=$"Disk {d.Number} · "+(part==null?"unallocated space":$"partition {part.Number} · {label} · {part.FileSystem}")+$" · {Data.Size(length)} · Offset {Data.Size(offset)}";
            var button=new Button{Tag=new PartitionMapTarget(d,part,offset,length),Background=part==null?B("#E6EAF2"):B(colors[(int)(Math.Abs((long)part.Number)%colors.Length)]),Foreground=part==null?Muted:Brushes.White,BorderBrush=Brushes.Transparent,BorderThickness=new Thickness(3),Margin=new Thickness(0,0,5,0),Padding=new Thickness(9,9,5,9),HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Center,ToolTip=description,Content=T(fraction<.045?"":label+"\n"+Data.Size(length),11,part==null?Muted:Brushes.White,true),Focusable=true,Cursor=Cursors.Hand};
            System.Windows.Automation.AutomationProperties.SetName(button,description);
            if(part!=null)
            {
                button.Click+=(_,_)=>{if(select!=null)select(part);else OpenPartition(d,part);};
                if(menu!=null)
                {
                    button.ContextMenu=menu(part);
                    button.PreviewMouseRightButtonDown+=(_,_)=>select?.Invoke(part);
                    button.ContextMenuOpening+=(_,_)=>{select?.Invoke(part);UpdatePartitionMenu(button.ContextMenu,d,part);};
                }
            }
            else if(select!=null)
            {
                button.ContextMenu=BuildFreeSpaceMenu(d,offset,length);
                button.Click+=(_,_)=>{status.Text=$"Unallocated space · {Data.Size(length)}. Right-click to create a partition.";};
            }
            else button.Click+=(_,_)=>{diskSelections["Disk manager:"]=d;Navigate("Disk manager");};
            Grid.SetColumn(button,column++);map.Children.Add(button);
        }
        if(d.InventoryWarning.Length>0)panel.Children.Add(T(d.InventoryWarning,12,B("#9C6C20"),false,new Thickness(0,12,0,0)));
        panel.Children.Add(map);panel.Children.Add(T(select==null?"Click a partition to open it in Disk manager.":"Click to select a partition. Right-click for actions. Small partitions have a minimum visible width.",11,Muted,false,new Thickness(0,9,0,0)));
        var card=Card(panel);HighlightPartition(card,selected);return card;
    }
    void HighlightPartition(DependencyObject root,PartitionInfo? selected)
    {
        foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if(child is Button {Tag:PartitionMapTarget target} button)
            {
                bool active=selected!=null&&target.Partition is PartitionInfo p&&p.Number==selected.Number&&p.Offset==selected.Offset&&p.Size==selected.Size;
                button.BorderBrush=active?Ink:Brushes.Transparent;
                System.Windows.Automation.AutomationProperties.SetItemStatus(button,active?"Selected":"");
            }
            HighlightPartition(child,selected);
        }
    }
    ContextMenu TrackPartitionMenu(ContextMenu menu)
    {
        menu.Opened+=(_,_)=>partitionMenuOpen=true;menu.Closed+=(_,_)=>partitionMenuOpen=false;return menu;
    }
}
