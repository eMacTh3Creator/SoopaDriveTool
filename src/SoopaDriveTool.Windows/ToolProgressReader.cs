using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SoopaDriveTool.Core;
namespace SoopaDriveTool.Windows;
// Tools use both CR updates and newline records; a percentage may cross pipe reads.
internal sealed class ToolProgressReader(bool wim,string phase,IProgress<DriveProgress>? progress,double start,double span)
{
    readonly StringBuilder line=new();double last=-1;string lastScan="";
    internal void Append(string chunk)
    {
        foreach(char c in chunk)
        {
            if(c is '\r' or '\n'){if(line.Length>0){Parse(line.ToString());line.Clear();}}
            else if(line.Length<8192)line.Append(c);
        }
    }
    void Parse(string text)
    {
        if(wim&&Regex.IsMatch(text,@"\d+ files, \d+ directories\)"))
        {if(text!=lastScan){lastScan=text;progress?.Report(new(phase,null,text.Trim()));}return;}
        var match=Regex.Match(text,wim?@"\((\d+(?:\.\d+)?)%\)":@"\[[=\s]*(\d+(?:\.\d+)?)%");
        if(!match.Success||!double.TryParse(match.Groups[1].Value,NumberStyles.Float,CultureInfo.InvariantCulture,out var pct)||pct<0||pct>100)return;
        double fraction=pct/100;
        if(wim)
        {
            if(text.StartsWith("Archiving file data:",StringComparison.Ordinal))fraction*=.9;
            else if(text.StartsWith("Writing integrity table:",StringComparison.Ordinal))fraction=.9+.1*fraction;
            else if(text.StartsWith("Verifying integrity of",StringComparison.Ordinal))fraction*=.3;
            else if(text.StartsWith("Verifying file data:",StringComparison.Ordinal))fraction=.3+.7*fraction;
            else return;
        }
        double value=start+span*fraction;
        if(value<last+.05)return;
        last=value;progress?.Report(new(phase,value,wim?text.Trim():$"{phase}: {pct:0.0}%"));
    }
}
