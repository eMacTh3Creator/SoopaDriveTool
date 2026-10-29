using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SoopaDriveTool.Core;
namespace SoopaDriveTool.Windows;
public partial class App:Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);ShutdownMode=ShutdownMode.OnExplicitShutdown;
        try
        {
            Preferences.Load();
            if(e.Args.Length==3&&e.Args[0]=="--schedule-create")
            {
                BackupEngine.RejectExistingAncestorLinks(e.Args[1]);BackupEngine.RejectExistingAncestorLinks(e.Args[2]);
                if(new FileInfo(e.Args[1]).Length>1024*1024||File.Exists(e.Args[2]))throw new IOException("Choose a valid schedule request and a new result filename.");
                var request=JsonSerializer.Deserialize<BackupScheduleRequest>(await File.ReadAllTextAsync(e.Args[1]),Data.Json)??throw new IOException("Invalid schedule request.");
                var job=await BackupScheduleService.CreateAsync(request.Source,request.Repository,request.Options,request.Vss,request.Passphrase,request.SystemAccount,request.Frequency,request.Time);
                await Data.SaveAsync(e.Args[2],new{job.Id,JobPath=BackupScheduleService.JobPath(job)});Shutdown(0);return;
            }
            if(e.Args.Length==2&&e.Args[0]=="--vault-job"){await BackupScheduleService.RunJobAsync(e.Args[1]);Shutdown(0);return;}
            if(e.Args.Length>=2 && e.Args[0]=="--backup-job")
            {
                var job=JsonSerializer.Deserialize<BackupJob>(await File.ReadAllTextAsync(e.Args[1]),Data.Json)??throw new IOException("Invalid job");
                try { var result=await BackupEngine.CreateAsync(job.Source,job.Repository); await BackupEngine.VerifyAsync(result); await Data.SaveAsync(e.Args[1]+".last-run.json",new{Success=true,Manifest=result,Time=DateTime.UtcNow}); }
                catch(Exception ex) { await Data.SaveAsync(e.Args[1]+".last-run.json",new{Success=false,Error=ex.Message,Time=DateTime.UtcNow}); throw; }
                Shutdown(0);return;
            }
            if(e.Args.Length>=2 && e.Args[0]=="--render")
            {
                var w=new MainWindow(true,e.Args.Length>2?e.Args[2]:"Overview");w.Width=1440;w.Height=940;w.WindowStyle=WindowStyle.None;var visual=(FrameworkElement)w.Content;visual.Width=1440;visual.Height=940;visual.Measure(new System.Windows.Size(1440,940));visual.Arrange(new Rect(0,0,1440,940));visual.UpdateLayout();
                w.Content=null;System.Windows.Documents.TextElement.SetForeground(visual,w.Foreground);System.Windows.Documents.TextElement.SetFontSize(visual,13);System.Windows.Documents.TextElement.SetFontFamily(visual,new FontFamily("Segoe UI Variable Text"));
                using var hiddenSource=new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("SoopaDriveTool offscreen render"){Width=1440,Height=940,PositionX=-32000,PositionY=-32000,WindowStyle=unchecked((int)0x80000000)});
                hiddenSource.RootVisual=visual;await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);visual.Measure(new System.Windows.Size(1440,940));visual.Arrange(new Rect(0,0,1440,940));visual.UpdateLayout();
                var bitmap=new RenderTargetBitmap(1440,940,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(e.Args[1]);encoder.Save(stream);w.Close();Shutdown(0);return;
            }
            PendingPlan? pending=null;
            if(e.Args.Length==2&&e.Args[0]=="--resume-plan")
            {
                var path=Path.GetFullPath(e.Args[1]);var directory=Path.Combine(Data.Home,"pending");BackupEngine.RejectExistingAncestorLinks(path);
                if(!BackupEngine.IsInside(path,directory)||!File.Exists(path)||new FileInfo(path).Length>1024*1024)throw new IOException("Invalid pending-plan handoff.");
                pending=JsonSerializer.Deserialize<PendingPlan>(await File.ReadAllTextAsync(path),Data.Json)??throw new IOException("Invalid pending plan.");
                QueuePlanner.Project(pending.Baselines,pending.Operations);File.Delete(path);
            }
            await Preferences.ReconcileActivityAsync();
            ShutdownMode=ShutdownMode.OnMainWindowClose;MainWindow=new MainWindow(restoredPlan:pending);MainWindow.Show();
        }
        catch(Exception ex)
        {
            Directory.CreateDirectory(Data.Home);await File.WriteAllTextAsync(System.IO.Path.Combine(Data.Home,"last-error.txt"),ex.ToString());
            if(e.Args.Length==0)MessageBox.Show(ex.Message,"SoopaDriveTool",MessageBoxButton.OK,MessageBoxImage.Error);Shutdown(1);
        }
    }
}
