using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using SoopaDriveTool.Core;
namespace SoopaDriveTool.Windows;
public record AppPreferences(string Theme="System",bool WaitingFacts=true,bool Compact=false,bool AutoRefreshDisks=false,int DiskRefreshSeconds=10);
public static class Preferences
{
    static string PathName=>Path.Combine(Data.Home,"settings.json");
    public static AppPreferences Current{get;private set;}=new();
    public static bool Dark{get;private set;}
    public static Brush Surface=>new SolidColorBrush((Color)ColorConverter.ConvertFromString(Dark?"#1D293D":"#FFFFFF"));
    public static void Load(){try{if(File.Exists(PathName))Current=JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(PathName),Data.Json)??new();}catch(IOException){}catch(JsonException){}Current=Current with{DiskRefreshSeconds=NormalizeRefreshSeconds(Current.DiskRefreshSeconds)};Apply();}
    public static int NormalizeRefreshSeconds(int seconds)=>seconds is 5 or 10 or 30 or 60?seconds:10;
    public static async Task SaveAsync(AppPreferences p){if(NormalizeRefreshSeconds(p.DiskRefreshSeconds)!=p.DiskRefreshSeconds)throw new IOException("Choose a disk refresh interval of 5, 10, 30, or 60 seconds.");if(p.Theme is not ("System" or "Light" or "Dark"))throw new IOException("Unknown theme.");await Data.SaveAsync(PathName,p);Current=p;Apply();}
    static void Apply()
    {
        bool systemDark=false;using(var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))systemDark=key?.GetValue("AppsUseLightTheme") is int value&&value==0;Dark=Current.Theme=="Dark"||Current.Theme=="System"&&systemDark;
        var resources=Application.Current.Resources;foreach(var pair in new Dictionary<string,string>{{"Ink",Dark?"#E8EEFA":"#15253F"},{"Muted",Dark?"#B1BDD2":"#69778F"},{"Accent",Dark?"#A3A2FF":"#5355E8"},{"Surface",Dark?"#1D293D":"#FFFFFF"},{"Canvas",Dark?"#131D2D":"#F4F6FA"},{"Outline",Dark?"#3B4962":"#DCE2EC"},{"Selection",Dark?"#354266":"#EDEFFD"}})resources[pair.Key]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(pair.Value));
    }
    public static string Palette(string hex)=>!Dark?hex:hex.ToUpperInvariant() switch{"#15253F"=>"#E8EEFA","#69778F"=>"#B1BDD2","#5355E8"=>"#A3A2FF","#F4F6FA"=>"#131D2D","#E5E9F1" or "#DCE2EC"=>"#3B4962","#EDEFFD"=>"#2C3857","#51558D"=>"#C3C6F8","#E6EAF2"=>"#38465C","#E4F7F1"=>"#1A493E","#188A71"=>"#78DDC1","#FFF2DD"=>"#4B3A23","#9C6C20"=>"#EFC984",_=>hex};
    public static async Task ReconcileActivityAsync()
    {
        string folder=Path.Combine(Data.Home,"activity");if(!Directory.Exists(folder))return;foreach(string file in Directory.GetFiles(folder,"*.json"))
        {try{if(new FileInfo(file).Length>8*1024*1024)continue;var run=JsonSerializer.Deserialize<RunActivity>(await File.ReadAllTextAsync(file),Data.Json);if(run?.State!="Running"||run.CompletedUtc!=null)continue;run.State="Interrupted";foreach(var step in run.Steps){if(step.State=="Running"){step.State="Indeterminate";step.Message="The app stopped before completion was recorded. Inspect the actual disk and recovery checkpoints before continuing.";}else if(step.State=="Pending")step.State="Not started";}run.Events.Add(new(DateTime.UtcNow,run.CurrentStep,"Interrupted",null,"Detected an unfinished run at startup. Disk writes are never resumed automatically."));await Data.SaveAsync(file,run);}catch(Exception e)when(e is IOException or JsonException){}}
    }
}
