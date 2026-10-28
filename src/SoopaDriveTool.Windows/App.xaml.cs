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
                var bitmap=new RenderTargetBitmap(1440,940,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(e.Args[1]);encoder.Save(stream);w.Close();Shutdown(0);return;
            }
            ShutdownMode=ShutdownMode.OnMainWindowClose;MainWindow=new MainWindow();MainWindow.Show();
        }
        catch(Exception ex)
        {
            Directory.CreateDirectory(Data.Home);await File.WriteAllTextAsync(System.IO.Path.Combine(Data.Home,"last-error.txt"),ex.ToString());
            if(e.Args.Length==0)MessageBox.Show(ex.Message,"SoopaDriveTool",MessageBoxButton.OK,MessageBoxImage.Error);Shutdown(1);
        }
    }
}
