using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SoopaDriveTool.Core;
using SoopaDriveTool.Windows;

partial class Program
{
    // Opt-in visual QA. All disk/partition data is synthetic. The window is
    // offscreen, and no Yes handler, disk inventory, or disk writer is invoked.
    static void PartitionInterfaceRenderChecks(string folder)
    {
        string output=Path.GetFullPath(folder);Directory.CreateDirectory(output);
        Exception? failure=null;
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(35));
        var thread=new Thread(()=>
        {
            App? application=null;
            try
            {
                application=new App{ShutdownMode=ShutdownMode.OnExplicitShutdown};application.InitializeComponent();
                var dispatcher=Dispatcher.CurrentDispatcher;
                dispatcher.BeginInvoke(new Action(async()=>
                {
                    try{await RenderPartitionInterfaceAsync(output,timeout.Token);}
                    catch(Exception ex){failure=ex is TargetInvocationException {InnerException:not null} reflected?reflected.InnerException:ex;}
                    finally
                    {
                        try{application.Shutdown();}
                        catch(Exception ex){failure=failure==null?ex:new AggregateException(failure,ex);}
                        finally{dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);}
                    }
                }));
                Dispatcher.Run();
            }
            catch(Exception ex){failure=ex;}
        }){IsBackground=true,Name="Partition interface offscreen rendering"};
        thread.SetApartmentState(ApartmentState.STA);thread.Start();
        if(!thread.Join(TimeSpan.FromSeconds(45))){timeout.Cancel();throw new TimeoutException("Partition interface visual checks did not finish.");}
        if(failure!=null)ExceptionDispatchInfo.Capture(failure).Throw();
    }

    static async Task RenderPartitionInterfaceAsync(string folder,CancellationToken token)
    {
        const long MiB=1024*1024,GiB=1024*MiB;
        var first=new PartitionInfo{Number=1,Offset=MiB,Size=96*GiB,Letter="R",Label="Projects",FileSystem="NTFS",Free=38*GiB,Encryption="None"};
        var second=first with{Number=2,Offset=160*GiB,Size=256*GiB,Letter="S",Label="Archive",Free=142*GiB};
        var disk=Disk() with{Number=2,Name="External SSD (sample)",UniqueId="VISUAL-QA-SYNTHETIC",Serial="SAMPLE-NOT-A-REAL-DRIVE",NativeIdentity="visual-qa-synthetic",Size=512*GiB,Bus="USB",Health="Healthy",Partitions=[first,second]};
        var original=Preferences.Current;var records=new List<object>();
        try
        {
            foreach(string theme in new[]{"Light","Dark"})
            {
                token.ThrowIfCancellationRequested();
                SetPreferences(original with{Theme=theme,AutoRefreshDisks=false});
                MainWindow? window=null;ContextMenu? menu=null;
                try
                {
                    window=new MainWindow(true,"Disk manager")
                    {
                        Width=1440,Height=1000,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,
                        WindowStartupLocation=WindowStartupLocation.Manual,Left=-32000,Top=-32000,
                        ShowInTaskbar=false,ShowActivated=false
                    };
                    SetField(window,"disks",new List<DiskInfo>{disk});Call(window,"Navigate","Disk manager");
                    window.Show();await InterfaceRenderIdleAsync(token);
                    var root=(FrameworkElement)window.Content;
                    var segment=Descendants(root).OfType<Button>().Single(button=>button.Tag is PartitionMapTarget target&&target.Partition?.Number==second.Number);
                    segment.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await InterfaceRenderIdleAsync(token);
                    RenderInterfaceVisual(root,Path.Combine(folder,theme+"-Map.png"));

                    segment.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Right){RoutedEvent=UIElement.PreviewMouseRightButtonDownEvent});
                    menu=segment.ContextMenu??throw new IOException("The graphical partition has no context menu.");
                    menu.PlacementTarget=segment;menu.Placement=PlacementMode.Bottom;menu.HorizontalOffset=0;menu.VerticalOffset=0;menu.IsOpen=true;
                    await InterfaceRenderIdleAsync(token);menu.ApplyTemplate();menu.UpdateLayout();
                    Check(menu.IsOpen&&menu.ActualWidth>0&&menu.ActualHeight>0,"The "+theme+" partition menu renders its real popup template");
                    RenderInterfaceVisual(menu,Path.Combine(folder,theme+"-ContextMenu.png"));
                    menu.IsOpen=false;await InterfaceRenderIdleAsync(token);

                    var delete=menu.Items.OfType<MenuItem>().Single(item=>Equals(item.Header,"Delete partition"));
                    Check(delete.IsEnabled,"The "+theme+" synthetic partition can queue deletion");
                    delete.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    await InterfaceRenderIdleAsync(token);
                    var queue=GetField<List<DiskOperation>>(window,"queue");
                    Check(queue.Count==1&&queue[0].Kind==OperationKind.DeletePartition&&queue[0].PartitionNumber==second.Number,"Visual QA queues only the selected synthetic partition");
                    Call(window,"ArmApplyConfirmation");await InterfaceRenderIdleAsync(token);
                    var no=GetField<Button>(window,"applyNo");
                    Check(no.IsDefault&&!GetField<Button>(window,"applyYes").IsDefault,"The "+theme+" rendered confirmation defaults to No");
                    var body=GetField<StackPanel>(window,"body");
                    Descendants(root).OfType<ScrollViewer>().Single(view=>ReferenceEquals(view.Content,body)).ScrollToEnd();
                    await InterfaceRenderIdleAsync(token);
                    RenderInterfaceVisual(root,Path.Combine(folder,theme+"-ApplyConfirmation.png"));
                    Check(GetField<List<DiskOperation>>(window,"queue").Count==1&&GetField<object?>(window,"activeRun")==null,"Visual confirmation capture never starts a disk operation");
                    records.Add(new{Theme=theme,Map=theme+"-Map.png",ContextMenu=theme+"-ContextMenu.png",Confirmation=theme+"-ApplyConfirmation.png",NoIsDefault=true,Applied=false});
                    no.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                finally
                {
                    if(menu!=null)menu.IsOpen=false;
                    if(window!=null){SetField(window,"busy",false);window.Close();}
                }
            }
        }
        finally{SetPreferences(original);}
        await File.WriteAllTextAsync(Path.Combine(folder,"interface-render-check.json"),JsonSerializer.Serialize(new{Width=1440,Height=1000,SyntheticData=true,DiskWrites=false,Screens=records},Data.Json),token);
        Console.WriteLine("PARTITION_INTERFACE_RENDERS "+JsonSerializer.Serialize(new{Themes=records.Count,Images=records.Count*3,NoIsDefault=true,DiskWrites=false}));
    }

    static async Task InterfaceRenderIdleAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Delay(60,token);
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
    }

    static void RenderInterfaceVisual(FrameworkElement element,string path)
    {
        element.UpdateLayout();
        int width=checked((int)Math.Ceiling(element.ActualWidth)),height=checked((int)Math.Ceiling(element.ActualHeight));
        if(width<1||height<1)throw new IOException("The interface visual has no arranged size: "+Path.GetFileName(path));
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(element);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(path);encoder.Save(stream);
    }
}
