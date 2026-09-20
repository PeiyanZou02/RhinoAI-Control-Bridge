using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace RhinoAI
{
    public static class SyncTests
    {
        sealed class Recorder : HttpMessageHandler
        {
            public readonly List<string> Calls=new List<string>();public JObject Published;public bool FailUpload;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
            {
                string path=request.RequestUri.AbsolutePath;Calls.Add(request.Method+" "+path);
                if(request.Method==HttpMethod.Get&&path=="/system_stats")return Reply("{}");
                if(request.Method==HttpMethod.Post&&path=="/upload/image")
                {
                    if(FailUpload)return new HttpResponseMessage(HttpStatusCode.InternalServerError){Content=new StringContent("simulated upload failure")};
                    var parts=(MultipartFormDataContent)request.Content;
                    string filename=parts.First(x=>x.Headers.ContentDisposition.Name.Trim('"')=="image").Headers.ContentDisposition.FileName.Trim('"');
                    string folder=await parts.First(x=>x.Headers.ContentDisposition.Name.Trim('"')=="subfolder").ReadAsStringAsync();
                    return Reply(new JObject{["name"]="server_"+filename,["subfolder"]=folder}.ToString());
                }
                if(request.Method==HttpMethod.Get&&path=="/userdata")return Reply("[{\"path\":\"old.json\",\"modified\":1},{\"path\":\"sub\\\\new.json\",\"modified\":9},{\"path\":\"notes.txt\",\"modified\":5}]");
                if(request.Method==HttpMethod.Get&&path=="/userdata/rhino_ai_frontend_status.json")return Reply("{\"version\":26,\"state\":\"bound\",\"active\":\"CHOGA.json\",\"images\":9}");
                if(request.Method==HttpMethod.Post&&path=="/userdata/rhino_ai_latest.json"){Published=JObject.Parse(await request.Content.ReadAsStringAsync());return Reply("{}");}
                throw new Exception("Forbidden/unexpected request: "+request.Method+" "+path);
            }
            static HttpResponseMessage Reply(string body){return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,"application/json")};}
        }
        public static string Run(string root,string png)
        {
            Directory.CreateDirectory(root);int count=0;
            Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception(name);count++;System.Console.WriteLine("PASS: "+name);};
            var export=new ExportResult{Directory=root,Files=new Dictionary<string,string>{{"depth",png},{"normal",png}},Prompt="material",WearPrompt="wear",PlacementConstraint="MANDATORY REFERENCE APPEARANCE LOCK. MANDATORY CLEAN FINAL OUTPUT. FULL-FRAME PLACEMENT LOCK test"};
            var config=new Config{Workflow="",ProductMode=true,WearInstructions="Replace the original earring at the aligned location."};var recorder=new Recorder();
            Task.Run(async()=>{using(var client=new ComfyClient("http://localhost:8000",recorder))await client.Send(export,config);}).GetAwaiter().GetResult();
            check(recorder.Calls.Count==4,"only health check, two uploads and manifest publish");
            check(recorder.Calls.All(x=>!x.Contains("/prompt")&&!x.Contains("/queue")),"no generation or queue requests");
            check(recorder.Calls.Last()=="POST /userdata/rhino_ai_latest.json","publish only after all uploads");
            check(((string)recorder.Published["images"]["depth"]).Contains("server_"),"server-returned filename used");
            check((string)recorder.Published["prompts"]["color_materials"]=="material","color-to-material mapping published with image batch");
            check(((string)recorder.Published["prompts"]["user_prompt"]).Contains("Replace the original earring"),"Rhino user instruction published with image batch");
            check(((string)recorder.Published["prompts"]["placement_constraint"]).Contains("FULL-FRAME PLACEMENT LOCK"),"numeric full-frame placement constraint published");
            check(((string)recorder.Published["prompts"]["placement_constraint"]).Contains("MANDATORY CLEAN FINAL OUTPUT"),"technical scale-lock overlays are explicitly banned from final output");
            check(((string)recorder.Published["prompts"]["placement_constraint"]).Contains("MANDATORY REFERENCE APPEARANCE LOCK"),"reference appearance remains authoritative over technical inputs");
            check(recorder.Published["prompts"]["positive_prompt"]==null&&recorder.Published["prompts"]["wear_prompt"]==null,"no unrelated automatic style prompt published");
            check(!recorder.Published.ToString().Contains("api_key"),"no API credentials transmitted");
            check(recorder.Published["target"]==null,"no target published when following the active ComfyUI window");
            var targeted=new Recorder();var targetConfig=new Config{Workflow="",TargetWorkflow="CHOGA.json"};
            Func<bool,string> publish=switchWindow=>{Task.Run(async()=>{using(var client=new ComfyClient("http://localhost:8000",targeted))await client.Send(export,targetConfig,switchWindow);}).GetAwaiter().GetResult();return (string)targeted.Published["target"]["request"];};
            string first=publish(true);check((string)targeted.Published["target"]["workflow"]=="CHOGA.json"&&first.Length==32,"target workflow and request id published");
            check(publish(false)==first,"automatic sync repeats the request id, so ComfyUI is not pulled back to the target");
            check(publish(true)!=first,"explicit update issues a new switch request");
            targetConfig.TargetWorkflow="other.json";check(publish(false)!=first&&(string)targeted.Published["target"]["workflow"]=="other.json","changing the target always issues a new request");
            check(targeted.Calls.All(x=>!x.Contains("/prompt")&&!x.Contains("/queue")),"target publishing uses no generation or queue requests");
            var browser=new Recorder();List<string> listed=null;JObject status=null;
            Task.Run(async()=>{using(var client=new ComfyClient("http://localhost:8000",browser)){listed=await client.ListWorkflows();status=await client.FrontendStatus();}}).GetAwaiter().GetResult();
            check(listed.Count==2&&listed[0]=="sub/new.json"&&listed[1]=="old.json","workflows listed newest first, json only, forward slashes");
            check(browser.Calls.All(x=>x.StartsWith("GET ")),"browsing workflows is read-only");
            check(ComfyClient.DescribeStatus(status,"CHOGA.json").Contains("9 images connected"),"bound status described");
            check(ComfyClient.DescribeStatus(status,"xxx.json").Contains("waiting to switch to xxx.json"),"mismatched window described as waiting");
            check(ComfyClient.DescribeStatus(new JObject{["version"]=21,["state"]="bound"},"").Contains("outdated"),"outdated ComfyUI extension reported");
            var failed=new Recorder{FailUpload=true};bool rejected=false;
            try{Task.Run(async()=>{using(var client=new ComfyClient("http://localhost:8000",failed))await client.Send(export,config);}).GetAwaiter().GetResult();}catch{rejected=true;}
            check(rejected&&failed.Published==null,"upload failure leaves previous live manifest intact");
            var many=Enumerable.Range(0,20).ToDictionary(i=>i<12?new[]{"reference","placement","placement_detail","rendered_detail","shape_lock_detail","material_id_detail","normal_detail","edges_detail","depth_detail","object_mask","inpaint_mask","occlusion_mask"}[i]:"extra_"+i,i=>png);
            var limited=ComfyClient.SelectForBatch(many,new Config{ProductMode=true,DetailPriority=true});
            check(limited.Count==10&&limited.ContainsKey("shape_lock_detail")&&!limited.ContainsKey("placement_detail")&&!limited.ContainsKey("rendered_detail")&&!limited.Keys.Any(x=>x.StartsWith("extra_")),"detail-priority profile excludes enlarged photo composites and stays below 14");
            string photo=Path.Combine(root,"style.jpg");using(var bmp=new System.Drawing.Bitmap(8,8))bmp.Save(photo,System.Drawing.Imaging.ImageFormat.Jpeg);
            var styledExport=new ExportResult{Directory=root,Files=new Dictionary<string,string>{{"depth",png}},Prompt="material"};
            StyleReferences.Prepare(styledExport,new Config{AiReferences=new List<string>{photo}},null);var styled=new Recorder();
            Task.Run(async()=>{using(var client=new ComfyClient("http://localhost:8000",styled))await client.Send(styledExport,new Config{Workflow=""});}).GetAwaiter().GetResult();
            check(((string)styled.Published["images"]["style_reference_1"]).EndsWith("style_reference_1.jpg")&&styled.Published["images"].Children().Last().Path.EndsWith("style_reference_1"),"style reference photos are uploaded behind the controls and published");
            check(styled.Calls.All(x=>!x.Contains("/prompt")&&!x.Contains("/queue")),"style references do not start a generation");
            var crowded=Enumerable.Range(0,14).ToDictionary(i=>new[]{"reference","placement","scale_lock","shape_lock","rendered","depth","edges","silhouette","normal","mask","material_id","object_mask","inpaint_mask","occlusion_mask"}[i],i=>png);crowded["style_reference_1"]=photo;
            check(!ComfyClient.SelectForBatch(crowded,new Config{ProductMode=true,DetailPriority=false}).ContainsKey("style_reference_1"),"a full 14-image batch leaves the style photos out instead of failing");
            check(ComfyClient.DescribeStatus(new JObject{["version"]=25,["state"]="bound"},"").Contains("outdated"),"extension without style reference support is reported as outdated");
            return count+" sync checks passed";
        }
    }
}
