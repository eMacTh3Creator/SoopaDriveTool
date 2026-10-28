using System.Net;
using System.Text;
using System.Text.Json;
namespace SoopaDriveTool.Core;
public record SmartField(string Section,string Name,string Value);
public record SmartAttribute(string Id,string Name,string Current,string Worst,string Threshold,string Raw,string Failed);
public class SmartReport
{
    public DateTime CollectedUtc {get;init;}=DateTime.UtcNow;
    public string ApplicationVersion {get;init;}="0.2.1";
    public int WindowsDiskNumber {get;init;}
    public string DevicePath {get;init;}="";
    public string Model {get;init;}="";
    public string Serial {get;init;}="";
    public string Firmware {get;init;}="";
    public string Protocol {get;init;}="Unknown";
    public string Overall {get;init;}="Unavailable";
    public int ExitStatus {get;init;}
    public string Coverage {get;init;}="";
    public List<string> Warnings {get;init;}=[];
    public List<SmartAttribute> Attributes {get;init;}=[];
    public List<SmartField> Fields {get;init;}=[];
    public string OriginalText {get;init;}="";
    public JsonElement Raw {get;init;}
    public static SmartReport Parse(string json,int exitStatus,int diskNumber,string windowsSerial="")
    {
        using var document=JsonDocument.Parse(json);var root=document.RootElement;
        string Text(JsonElement e,string key)=>e.TryGetProperty(key,out var v)?v.ValueKind==JsonValueKind.String?v.GetString()??"":v.ToString():"";
        string model=Text(root,"model_name");if(model.Length==0)model=Text(root,"product");
        var serial=Text(root,"serial_number");var warnings=new List<string>();
        string[] bits=["Command syntax or engine error","Device open/identity failed","One or more SMART/log commands were unsupported or failed","SMART reports a failing device","A current prefailure attribute crossed its threshold","An attribute crossed its threshold in the past","The device error log contains errors","The device self-test log contains failures"];
        for(int i=0;i<8;i++)if((exitStatus&(1<<i))!=0)warnings.Add(bits[i]);
        string protocol=root.TryGetProperty("device",out var device)?Text(device,"protocol"):"Unknown";
        bool hasAttributes=root.TryGetProperty("ata_smart_attributes",out var ata)&&ata.TryGetProperty("table",out var table)&&table.ValueKind==JsonValueKind.Array;
        bool hasNvme=root.TryGetProperty("nvme_smart_health_information_log",out _);bool hasScsi=root.EnumerateObject().Any(p=>p.Name.StartsWith("scsi_",StringComparison.Ordinal));
        bool? passed=null;if(root.TryGetProperty("smart_status",out var status)&&status.TryGetProperty("passed",out var good)&&good.ValueKind is JsonValueKind.True or JsonValueKind.False)passed=good.GetBoolean();
        string overall=passed==false||(exitStatus&8)!=0?"FAILING":passed==true?(exitStatus&(16|32|64|128))!=0?"Warnings present":"PASSED":"Unavailable";
        if(!hasAttributes&&!hasNvme&&!hasScsi){warnings.Add("This drive/controller/VM did not expose detailed SMART data. No health pass is inferred.");if(overall!="FAILING")overall="Unavailable";}
        if(!string.IsNullOrWhiteSpace(windowsSerial)&&serial.Length>0&&!Normalize(serial).Equals(Normalize(windowsSerial),StringComparison.OrdinalIgnoreCase))warnings.Add("SMART serial differs from Windows inventory; verify the physical label/USB bridge before using this report for resale.");
        var attributes=new List<SmartAttribute>();
        if(hasAttributes)foreach(var a in ata.GetProperty("table").EnumerateArray())attributes.Add(new(Text(a,"id"),Text(a,"name"),Text(a,"value"),Text(a,"worst"),Text(a,"thresh"),a.TryGetProperty("raw",out var raw)?Text(raw,"string"):"",Text(a,"when_failed")));
        var fields=new List<SmartField>();Flatten(root,"",fields);
        if((exitStatus&3)!=0&&overall!="FAILING")overall="Unavailable";else if((exitStatus&4)!=0&&overall=="PASSED")overall="Partial report";
        string original=root.TryGetProperty("smartctl",out var tool)&&tool.TryGetProperty("output",out var output)&&output.ValueKind==JsonValueKind.Array?string.Join(Environment.NewLine,output.EnumerateArray().Select(e=>e.ToString())):"";
        return new(){WindowsDiskNumber=diskNumber,DevicePath="/dev/pd"+diskNumber,Model=model,Serial=serial,Firmware=Text(root,"firmware_version"),Protocol=protocol,Overall=overall,ExitStatus=exitStatus,Coverage=hasNvme?"NVMe health, error/self-test logs and all additional fields returned by smartctl -x":hasAttributes?"ATA normalized/raw attributes, logs and all additional fields returned by smartctl -x":hasScsi?"SCSI/SAS counters, logs and all additional fields returned by smartctl -x":"Detailed SMART unavailable",Warnings=warnings,Attributes=attributes,Fields=fields,OriginalText=original,Raw=root.Clone()};
    }
    static string Normalize(string s)=>new(s.Where(char.IsLetterOrDigit).ToArray());
    static void Flatten(JsonElement element,string path,List<SmartField> fields)
    {
        if(path.StartsWith("smartctl.output",StringComparison.Ordinal))return;
        if(element.ValueKind==JsonValueKind.Object){foreach(var p in element.EnumerateObject())Flatten(p.Value,path.Length==0?p.Name:path+"."+p.Name,fields);}
        else if(element.ValueKind==JsonValueKind.Array){int i=0;foreach(var e in element.EnumerateArray())Flatten(e,path+"["+i+++ "]",fields);}
        else{int split=path.IndexOf('.');fields.Add(new(split<0?"Identity":path[..split],path,element.ToString()));}
    }
    public string Csv()=>"Section,Field,Value\r\n"+string.Join("\r\n",Fields.Select(f=>string.Join(',',new[]{f.Section,f.Name,f.Value}.Select(Cell))));
    static string Cell(string s){if(s.TrimStart().StartsWith('=')||s.TrimStart().StartsWith('+')||s.TrimStart().StartsWith('@')||s.TrimStart().StartsWith('-'))s="'"+s;return "\""+s.Replace("\"","\"\"")+"\"";}
    public string Html()
    {
        string E(string? v)=>WebUtility.HtmlEncode(v??"");var b=new StringBuilder("<!doctype html><html lang='en'><meta charset='utf-8'><meta name='viewport' content='width=device-width'><title>SoopaDriveTool SMART report</title><style>body{font:14px Segoe UI,Arial;color:#15253f;max-width:1100px;margin:40px auto;padding:20px}h1{color:#5355e8}table{border-collapse:collapse;width:100%;font-size:12px}td,th{padding:7px;border:1px solid #dce2ec;text-align:left;overflow-wrap:anywhere}th{background:#eef0fa}pre{white-space:pre-wrap;overflow-wrap:anywhere;background:#f4f6fa;padding:18px}.warning{background:#fff2db;padding:14px} @media print{body{margin:0}}</style><h1>SoopaDriveTool · Drive condition report</h1>");
        b.Append($"<p>Collected UTC: {CollectedUtc:O} · Windows disk {WindowsDiskNumber} · Device {E(DevicePath)}</p><h2>{E(Model)} · {E(Overall)}</h2><p>Serial: {E(Serial)} · Firmware: {E(Firmware)} · Protocol: {E(Protocol)} · Exit bitfield: {ExitStatus}</p><p>{E(Coverage)}</p><p>This report records device-reported condition at collection time. It is not a warranty, certification, erasure certificate or prediction of remaining life. Some controllers and USB bridges hide data.</p>");
        foreach(var w in Warnings)b.Append("<p class='warning'>"+E(w)+"</p>");
        if(Attributes.Count>0){b.Append("<h2>ATA attributes</h2><table><tr><th>ID</th><th>Name</th><th>Current</th><th>Worst</th><th>Threshold</th><th>Raw</th><th>Failed</th></tr>");foreach(var a in Attributes)b.Append("<tr>"+string.Concat(new[]{a.Id,a.Name,a.Current,a.Worst,a.Threshold,a.Raw,a.Failed}.Select(v=>"<td>"+E(v)+"</td>"))+"</tr>");b.Append("</table>");}
        b.Append("<h2>All reported fields</h2><table><tr><th>Section</th><th>Field</th><th>Value</th></tr>");foreach(var f in Fields)b.Append($"<tr><td>{E(f.Section)}</td><td>{E(f.Name)}</td><td>{E(f.Value)}</td></tr>");b.Append("</table><h2>Original smartctl output</h2><pre>"+E(OriginalText)+"</pre><details><summary>Complete original JSON</summary><pre>"+E(Raw.GetRawText())+"</pre></details></html>");return b.ToString();
    }
}
