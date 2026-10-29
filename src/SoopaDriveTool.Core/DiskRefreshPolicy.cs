namespace SoopaDriveTool.Core;

public static class DiskRefreshPolicy
{
    public static string? QueueProblem(IEnumerable<DiskInfo> current,IEnumerable<DiskInfo> baselines)
    {
        var connected=current.ToLookup(d=>d.Number);
        foreach(var expected in baselines)
        {
            var matches=connected[expected.Number].Take(2).ToList();
            if(matches.Count==0)return $"Disk {expected.Number} is no longer connected. Clear the queue and review the connected disks.";
            if(matches.Count!=1)return $"Disk {expected.Number} could not be identified reliably. Clear the queue and refresh.";
            var actual=matches[0];
            if(!string.IsNullOrWhiteSpace(expected.InventoryWarning)||!string.IsNullOrWhiteSpace(actual.InventoryWarning)||expected.InventoryWarning!=actual.InventoryWarning)
                return $"Disk {expected.Number} information is incomplete or changed. Clear the queue and refresh before preparing more changes.";
            try{QueuePlanner.AssertUnchanged(expected,actual);}
            catch(InvalidOperationException)
            {
                string change=expected.StableIdentity!=actual.StableIdentity||expected.NativeIdentity.Length>0&&expected.NativeIdentity!=actual.NativeIdentity?"identity":
                    expected.Protected!=actual.Protected?"system protection":expected.ReadOnly!=actual.ReadOnly?"write-protection state":expected.Offline!=actual.Offline?"online state":"partition layout";
                return $"Disk {expected.Number} {change} changed. Clear the queue and review this disk again.";
            }
        }
        return null;
    }

    public static DiskInfo? RetainSelection(IEnumerable<DiskInfo> items,DiskInfo? selected)
    {
        if(selected==null)return null;
        var matches=items.Where(d=>d.Number==selected.Number&&d.Identity==selected.Identity&&d.Layout==selected.Layout&&d.Protected==selected.Protected&&d.InventoryWarning==selected.InventoryWarning).Take(2).ToList();
        return matches.Count==1?matches[0]:null;
    }
}
