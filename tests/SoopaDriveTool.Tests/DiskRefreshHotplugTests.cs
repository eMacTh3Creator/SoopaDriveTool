using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using SoopaDriveTool.Core;
using SoopaDriveTool.Windows;

partial class Program
{
    // Opt-in only: this attaches an owned, empty VHD on the spare VM. It does
    // not show a window, initialize/format a disk, or write an existing disk.
    static void DiskRefreshHotplugChecks()
    {
        if(!string.Equals(Environment.MachineName,"SDTLAB",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Disk refresh hotplug checks require the spare SDTLAB VM.");
        if(!StorageService.IsAdmin)throw new UnauthorizedAccessException("The spare-VM hotplug fixture requires administrator access.");
        Exception? failure=null;
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(150));
        var thread=new Thread(()=>
        {
            App? application=null;
            try
            {
                application=new App{ShutdownMode=ShutdownMode.OnExplicitShutdown};application.InitializeComponent();
                var dispatcher=Dispatcher.CurrentDispatcher;
                dispatcher.BeginInvoke(new Action(async()=>
                {
                    try{await DiskRefreshHotplugScenarioAsync(timeout.Token);}
                    catch(Exception ex){failure=ex is TargetInvocationException {InnerException:not null} reflection?reflection.InnerException:ex;}
                    finally
                    {
                        // The fixture starts Dispatcher.Run directly, without
                        // Application.Run. Shut down that loop explicitly.
                        try{application.Shutdown();}
                        catch(Exception ex){failure=failure==null?ex:new AggregateException(failure,ex);}
                        finally{dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);}
                    }
                }));
                Dispatcher.Run();
            }
            catch(Exception ex){failure=ex;}
        }){IsBackground=true,Name="Spare-VM automatic disk refresh hotplug"};
        thread.SetApartmentState(ApartmentState.STA);thread.Start();
        if(!thread.Join(TimeSpan.FromSeconds(190)))
        {
            timeout.Cancel();
            throw new TimeoutException("Spare-VM disk refresh test did not complete its bounded cleanup.");
        }
        if(failure!=null)ExceptionDispatchInfo.Capture(failure).Throw();
    }

    static async Task DiskRefreshHotplugScenarioAsync(CancellationToken token)
    {
        var elapsed=Stopwatch.StartNew();
        string folder=Path.Combine(Path.GetTempPath(),"SoopaDriveTool-RefreshHotplug-"+Guid.NewGuid().ToString("N"));
        string image=Path.Combine(folder,"owned-refresh-test.vhd"),script=Path.Combine(folder,"create-owned-vhd.txt");
        string preferencesPath=Path.Combine(Data.Home,"settings.json");
        byte[]? preferencesBytes=File.Exists(preferencesPath)?await File.ReadAllBytesAsync(preferencesPath,token):null;
        Preferences.Load();var originalPreferences=Preferences.Current;
        MainWindow? window=null;Exception? scenarioFailure=null;bool safeToDelete=false,attachedObserved=false,removedObserved=false;int baselineCount=0,number=-1;
        try
        {
            await Preferences.SaveAsync(originalPreferences with{AutoRefreshDisks=true,DiskRefreshSeconds=5});
            window=new MainWindow(false,"About");
            // No Show() or Loaded event: the production DispatcherTimer is the
            // only entry point that may populate this window's disk inventory.
            Call(window,"ConfigureDiskRefresh");
            Check(GetField<DispatcherTimer>(window,"diskRefreshTimer").IsEnabled,"Live automatic refresh starts without showing the spare-VM test window");
            await WaitForInventoryAsync(window,()=>GetField<DateTime?>(window,"inventoryUpdated").HasValue&&GetField<List<DiskInfo>>(window,"disks").Count>0,"the initial timer inventory",TimeSpan.FromSeconds(90),token);
            var baseline=GetField<List<DiskInfo>>(window,"disks").ToList();baselineCount=baseline.Count;
            Check(baseline.Count>0,"A DispatcherTimer tick populates the live disk inventory");

            if(Directory.Exists(folder))throw new IOException("The owned hotplug fixture directory already exists.");
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(script,$"create vdisk file=\"{image}\" maximum=128 type=expandable\r\nselect vdisk file=\"{image}\"\r\nattach vdisk\r\nexit\r\n",token);
            string created=await StorageService.PowerShellAsync($"diskpart.exe /s '{HotplugQuote(script)}' | Out-Null; if($LASTEXITCODE -ne 0){{throw 'Owned VHD creation failed.'}}; $image=Get-DiskImage -ImagePath '{HotplugQuote(image)}'; if(!$image.Attached){{throw 'Owned VHD did not attach.'}}; $disk=$image | Get-Disk; [pscustomobject]@{{Number=[int]$disk.Number;Size=[long]$disk.Size;IsBoot=[bool]$disk.IsBoot;IsSystem=[bool]$disk.IsSystem;Bus=[string]$disk.BusType}} | ConvertTo-Json -Compress",token);
            var attachment=JsonSerializer.Deserialize<RefreshHotplugAttachment>(created,Data.Json)??throw new IOException("Missing owned VHD attachment information.");
            number=attachment.Number;
            Check(File.Exists(image)&&attachment.Size==128L*1024*1024&&!attachment.IsBoot&&!attachment.IsSystem&&attachment.Bus=="File Backed Virtual"&&!baseline.Any(d=>d.Number==number),"Get-DiskImage binds the separate empty 128 MiB VHD to its new disk number");
            await WaitForInventoryAsync(window,()=>GetField<List<DiskInfo>>(window,"disks").Any(d=>d.Number==number&&d.Size==attachment.Size&&d.Bus=="File Backed Virtual"&&!d.Protected),"the attached owned VHD",TimeSpan.FromSeconds(90),token);
            attachedObserved=true;
            Check(true,"Automatic disk refresh discovers the newly attached VHD without manual Refresh");

            await StorageService.PowerShellAsync($"Dismount-DiskImage -ImagePath '{HotplugQuote(image)}' -ErrorAction Stop; if((Get-DiskImage -ImagePath '{HotplugQuote(image)}').Attached){{throw 'Owned VHD is still attached.'}}",token);
            await WaitForInventoryAsync(window,()=>!GetField<List<DiskInfo>>(window,"disks").Any(d=>d.Number==number),"removal of the owned VHD",TimeSpan.FromSeconds(90),token);
            removedObserved=true;
            Check(true,"Automatic disk refresh removes the detached VHD without manual Refresh");
        }
        catch(Exception ex){scenarioFailure=ex;}
        finally
        {
            try
            {
                if(window!=null)
                {
                    GetField<DispatcherTimer>(window,"diskRefreshTimer").Stop();
                    window.Close();
                }
            }
            catch(Exception ex){scenarioFailure=scenarioFailure==null?ex:new AggregateException(scenarioFailure,ex);}
            try
            {
                if(File.Exists(image))
                {
                    using var cleanupTimeout=new CancellationTokenSource(TimeSpan.FromSeconds(25));
                    await StorageService.PowerShellAsync($"$owned=Get-DiskImage -ImagePath '{HotplugQuote(image)}' -ErrorAction Stop; if(!$owned){{throw 'Cannot verify the owned VHD; preserve its fixture files.'}}; if($owned.Attached){{Dismount-DiskImage -ImagePath '{HotplugQuote(image)}' -ErrorAction Stop}}; $owned=Get-DiskImage -ImagePath '{HotplugQuote(image)}' -ErrorAction Stop; if(!$owned -or $owned.Attached){{throw 'Owned VHD detachment is unverified; preserve its fixture files.'}}",cleanupTimeout.Token);
                }
                safeToDelete=true;
            }
            catch(Exception ex){scenarioFailure=scenarioFailure==null?ex:new AggregateException(scenarioFailure,ex);}
            try
            {
                await Preferences.SaveAsync(originalPreferences);
                if(preferencesBytes!=null)await File.WriteAllBytesAsync(preferencesPath,preferencesBytes);
                else if(File.Exists(preferencesPath))File.Delete(preferencesPath);
                if(safeToDelete&&Directory.Exists(folder))Directory.Delete(folder,true);
            }
            catch(Exception ex){scenarioFailure=scenarioFailure==null?ex:new AggregateException(scenarioFailure,ex);}
        }
        if(scenarioFailure!=null)ExceptionDispatchInfo.Capture(scenarioFailure).Throw();
        Check(safeToDelete&&!Directory.Exists(folder)&&Preferences.Current==originalPreferences,"Hotplug test detaches only its own VHD, removes its fixture, and restores preferences");
        Console.WriteLine("REFRESH_HOTPLUG "+JsonSerializer.Serialize(new{BaselineDisks=baselineCount,FixtureDisk=number,FixtureBytes=128L*1024*1024,AutomaticAttachmentObserved=attachedObserved,AutomaticRemovalObserved=removedObserved,PreferencesRestored=true,ElapsedSeconds=Math.Round(elapsed.Elapsed.TotalSeconds,1)}));
    }

    static async Task WaitForInventoryAsync(MainWindow window,Func<bool> condition,string expected,TimeSpan limit,CancellationToken token)
    {
        var elapsed=Stopwatch.StartNew();
        while(!condition())
        {
            token.ThrowIfCancellationRequested();
            if(elapsed.Elapsed>limit)throw new TimeoutException("Timed out waiting for "+expected+". "+GetField<System.Windows.Controls.TextBlock>(window,"status").Text);
            await Task.Delay(250,token);
        }
    }

    static string HotplugQuote(string value)=>value.Replace("'","''");
    sealed record RefreshHotplugAttachment(int Number,long Size,bool IsBoot,bool IsSystem,string Bus);
}
