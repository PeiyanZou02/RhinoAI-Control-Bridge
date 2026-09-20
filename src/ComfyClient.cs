using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RhinoAI
{
    public sealed class ComfyClient : IDisposable
    {
        readonly HttpClient client;
        public ComfyClient(string server) : this(server,new HttpClientHandler{UseProxy=false}) {}
        public ComfyClient(string server,HttpMessageHandler handler)
        {
            Uri uri;if(!Uri.TryCreate(server.TrimEnd('/')+"/",UriKind.Absolute,out uri)||(uri.Scheme!="http"&&uri.Scheme!="https"))throw new ArgumentException("The ComfyUI address must start with http:// or https://.");
            client=new HttpClient(handler){BaseAddress=uri,Timeout=TimeSpan.FromSeconds(90)};
        }
        public void Dispose(){client.Dispose();}
        public TimeSpan Timeout{set{client.Timeout=value;}}
        async Task<JObject> Json(HttpResponseMessage response)
        {
            using(response){string text=await response.Content.ReadAsStringAsync();if(!response.IsSuccessStatusCode)throw new InvalidOperationException("ComfyUI HTTP "+(int)response.StatusCode+": "+text);return JObject.Parse(text);}
        }
        public async Task Check(){await Json(await client.GetAsync("system_stats"));}
        // Saved ComfyUI workflows, newest first. Paths are relative to the user's workflows folder.
        public async Task<List<string>> ListWorkflows()
        {
            using(var response=await client.GetAsync("userdata?dir=workflows&recurse=true&split=false&full_info=true"))
            {
                if(response.StatusCode==System.Net.HttpStatusCode.NotFound)return new List<string>();
                string text=await response.Content.ReadAsStringAsync();
                if(!response.IsSuccessStatusCode)throw new InvalidOperationException("ComfyUI HTTP "+(int)response.StatusCode+": "+text);
                return JArray.Parse(text).OfType<JObject>().Where(x=>((string)x["path"]??"").EndsWith(".json",StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(x=>(long?)x["modified"]??0).Select(x=>((string)x["path"]).Replace('\\','/')).ToList();
            }
        }
        // What the ComfyUI window last reported about itself; null when the extension has never run.
        public async Task<JObject> FrontendStatus()
        {
            using(var response=await client.GetAsync("userdata/rhino_ai_frontend_status.json"))
            {
                if(!response.IsSuccessStatusCode)return null;
                try{return JObject.Parse(await response.Content.ReadAsStringAsync());}catch(JsonException){return null;}
            }
        }
        public static string DescribeStatus(JObject status,string target)
        {
            if(status==null)return "The ComfyUI window has not loaded the sync extension";
            if(((int?)status["version"]??0)<25)return "The ComfyUI sync extension is outdated: run install-comfy-extension.ps1 again, then press F5 in ComfyUI";
            string active=(string)status["active"],state=(string)status["state"];
            string where=string.IsNullOrEmpty(active)?"ComfyUI shows an unsaved workflow":"ComfyUI shows "+active;
            if(state=="error")return where+" · sync failed: "+(string)status["message"];
            if(state=="waiting_target"||(!string.IsNullOrEmpty(target)&&!string.IsNullOrEmpty(active)&&active!=target))return where+" · waiting to switch to "+target;
            if(state=="bound")return where+" · "+(int?)status["images"]+" images connected";
            return where+" · no Batch connected yet";
        }
        public async Task<string> Upload(string path,string folder)
        {
            using(var form=new MultipartFormDataContent())using(var stream=File.OpenRead(path))
            {
                var bytes=new StreamContent(stream);bytes.Headers.ContentType=new System.Net.Http.Headers.MediaTypeHeaderValue(StyleReferences.Mime(path));
                form.Add(bytes,"image",Path.GetFileName(path));form.Add(new StringContent("input"),"type");form.Add(new StringContent("false"),"overwrite");form.Add(new StringContent(folder),"subfolder");
                JObject data=await Json(await client.PostAsync("upload/image",form));
                string name=(string)data["name"],sub=(string)data["subfolder"];
                if(string.IsNullOrEmpty(name))throw new InvalidOperationException("The ComfyUI upload response has no file name.");
                return string.IsNullOrEmpty(sub)?name:sub.TrimEnd('/')+"/"+name;
            }
        }
        public static JObject Bind(JObject graph,Dictionary<string,string> images,ExportResult export){return Bind(graph,images,export,"","");}
        public static JObject Bind(JObject graph,Dictionary<string,string> images,ExportResult export,string positive,string full)
        {
            if(graph["nodes"]!=null)throw new InvalidOperationException("This is a UI-format workflow. Export it from ComfyUI in API format.");
            if(graph["prompt"] is JObject)graph=(JObject)graph["prompt"];
            graph=(JObject)graph.DeepClone();
            foreach(var property in graph.Properties())
            {
                var node=property.Value as JObject;if(node==null || node["class_type"]==null || !(node["inputs"] is JObject))throw new InvalidOperationException("The workflow is not valid ComfyUI API format.");
                var inputs=(JObject)node["inputs"];string title=(string)node.SelectToken("_meta.title")??"";
                if((string)node["class_type"]=="LoadImage" && title.StartsWith("RHINO:",StringComparison.OrdinalIgnoreCase))
                {
                    string channel=title.Substring(6).Trim().ToLowerInvariant();if(!images.ContainsKey(channel))throw new InvalidOperationException("The workflow needs a channel that was not exported: "+channel);inputs["image"]=images[channel];
                }
                foreach(var value in inputs.Descendants().OfType<JValue>().ToList())
                {
                    if(value.Type!=JTokenType.String)continue;string text=(string)value;
                    foreach(var image in images)text=text.Replace("{{"+image.Key+"}}",image.Value);
                    text=text.Replace("{{color_materials}}",export.Prompt??"").Replace("{{material_prompt}}",export.Prompt??"").Replace("{{positive_prompt}}",positive??"").Replace("{{full_prompt}}",full??"").Replace("{{wear_prompt}}","");
                    if(text.Contains("{{"))throw new InvalidOperationException("The workflow has an unmatched placeholder: "+text);
                    value.Value=text;
                }
            }
            return graph;
        }
        public static JObject PreviewGraph(Dictionary<string,string> images)
        {
            var graph=new JObject();int id=1;
            foreach(var image in images)
            {
                string source=(id++).ToString(),preview=(id++).ToString();
                graph[source]=new JObject{["class_type"]="LoadImage",["inputs"]=new JObject{["image"]=image.Value},["_meta"]=new JObject{["title"]="RHINO:"+image.Key}};
                graph[preview]=new JObject{["class_type"]="PreviewImage",["inputs"]=new JObject{["images"]=new JArray(source,0)},["_meta"]=new JObject{["title"]=image.Key}};
            } return graph;
        }
        // Upload immutable batches, then publish one complete manifest. Never execute a workflow.
        public Task<string> Send(ExportResult export,Config config){return Send(export,config,true);}
        public async Task<string> Send(ExportResult export,Config config,bool switchWindow)
        {
            await Check();JObject template=null;
            var selected=SelectForBatch(export.Files,config);
            if(!string.IsNullOrWhiteSpace(config.Workflow))
            {
                template=JObject.Parse(File.ReadAllText(config.Workflow,Encoding.UTF8));
                // Validate bindings before uploading anything.
                Bind(template,selected.ToDictionary(k=>k.Key,k=>Path.GetFileName(k.Value)),export);
            }
            var uploaded=new Dictionary<string,string>();string folder="rhino_ai/"+Path.GetFileName(export.Directory);
            foreach(var file in selected)uploaded[file.Key]=await Upload(file.Value,folder);
            File.WriteAllText(Path.Combine(export.Directory,"comfy_uploads.json"),JsonConvert.SerializeObject(uploaded,Formatting.Indented),Encoding.UTF8);
            JObject graph=template==null?PreviewGraph(uploaded):Bind(template,uploaded,export);
            File.WriteAllText(Path.Combine(export.Directory,"comfy_bound_api.json"),graph.ToString(),Encoding.UTF8);
            // Also provide a drag-and-drop UI workflow for inspecting all uploaded channels.
            File.WriteAllText(Path.Combine(export.Directory,"comfy_preview.workflow.json"),PreviewUi(uploaded).ToString(),Encoding.UTF8);
            string userPrompt=config.ProductMode?(config.WearInstructions??"").Trim():"";
            var manifest=new JObject{["schema"]="rhino-ai-live/1",["revision"]=Guid.NewGuid().ToString("N"),["images"]=JObject.FromObject(uploaded),["prompts"]=new JObject{["color_materials"]=export.Prompt??"",["user_prompt"]=userPrompt,["placement_constraint"]=export.PlacementConstraint??""},["input_profile"]=config.ProductMode?(config.DetailPriority?"detail_priority":"full_frame"):"scene"};
            // An empty target follows whichever workflow the ComfyUI window shows. A new request id per
            // publish lets the window switch once, without fighting the user afterwards.
            if(!string.IsNullOrWhiteSpace(config.TargetWorkflow))
            {
                string name=config.TargetWorkflow.Trim();
                // Automatic syncs repeat the previous request id, so the window is not pulled back to the target.
                bool reuse=!switchWindow&&config.LastTargetRequestFor==name&&!string.IsNullOrEmpty(config.LastTargetRequest);
                if(!reuse){config.LastTargetRequest=Guid.NewGuid().ToString("N");config.LastTargetRequestFor=name;}
                manifest["target"]=new JObject{["workflow"]=name,["request"]=config.LastTargetRequest};
            }
            // This is ComfyUI's user-file endpoint, not its execution endpoint.
            using(var response=await client.PostAsync("userdata/"+Uri.EscapeDataString("rhino_ai_latest.json")+"?overwrite=true",new StringContent(manifest.ToString(),Encoding.UTF8,"application/json")))
            {if(!response.IsSuccessStatusCode)throw new InvalidOperationException("Images uploaded, but publishing the sync manifest failed: "+(int)response.StatusCode+" "+await response.Content.ReadAsStringAsync());}
            File.WriteAllText(Path.Combine(export.Directory,"comfy_sync.json"),manifest.ToString(),Encoding.UTF8);
            return "Images and prompt data uploaded"+(string.IsNullOrWhiteSpace(config.TargetWorkflow)?"":", target "+config.TargetWorkflow.Trim())+".";
        }
        // The one place that runs a workflow: batch rendering with ComfyUI chosen as the engine. Send never does.
        public async Task<List<AiImage>> Generate(ExportResult export,Config config,System.Threading.CancellationToken token)
        {
            if(string.IsNullOrWhiteSpace(config.Workflow))throw new InvalidOperationException("The ComfyUI engine needs an API workflow. Choose one on the Export settings page.");
            await Check();var template=JObject.Parse(File.ReadAllText(config.Workflow,Encoding.UTF8));var selected=SelectForBatch(export.Files,config);
            string positive=(config.AiPrompt??"").Trim(),full=PromptGuide.Build(selected.Keys.ToList(),export,config);
            Bind(template,selected.ToDictionary(k=>k.Key,k=>Path.GetFileName(k.Value)),export,positive,full);
            var uploaded=new Dictionary<string,string>();string folder="rhino_ai/"+Path.GetFileName(export.Directory);
            foreach(var file in selected){token.ThrowIfCancellationRequested();uploaded[file.Key]=await Upload(file.Value,folder);}
            var graph=Bind(template,uploaded,export,positive,full);
            File.WriteAllText(Path.Combine(export.Directory,"comfy_job.json"),graph.ToString(),Encoding.UTF8);
            var queued=await Json(await client.PostAsync("prompt",new StringContent(new JObject{["prompt"]=graph,["client_id"]="rhino2comfy"}.ToString(),Encoding.UTF8,"application/json"),token));
            string id=(string)queued["prompt_id"];if(string.IsNullOrEmpty(id))throw new InvalidOperationException("ComfyUI did not queue the workflow: "+queued.ToString(Formatting.None));
            var deadline=DateTime.UtcNow.AddMinutes(30);JObject entry=null;
            while(entry==null)
            {
                if(DateTime.UtcNow>deadline)throw new TimeoutException("ComfyUI did not finish within 30 minutes.");
                await Task.Delay(PollMilliseconds,token);
                entry=(await Json(await client.GetAsync("history/"+id,token)))[id] as JObject;
                if(entry!=null&&(string)entry.SelectToken("status.status_str")!="error"&&entry.SelectToken("status.completed")!=null&&!(bool)entry.SelectToken("status.completed"))entry=null;
            }
            if((string)entry.SelectToken("status.status_str")=="error")
            {
                var failure=entry.SelectTokens("status.messages[*]").OfType<JArray>().FirstOrDefault(m=>(string)m[0]=="execution_error");
                throw new InvalidOperationException("ComfyUI workflow failed: "+(failure==null?"see the ComfyUI console":(string)failure[1]["node_type"]+" · "+(string)failure[1]["exception_message"]));
            }
            var found=entry.SelectTokens("outputs.*.images[*]").OfType<JObject>().ToList();
            // Saved outputs win over temporary previews.
            if(found.Any(x=>(string)x["type"]=="output"))found=found.Where(x=>(string)x["type"]=="output").ToList();
            var results=new List<AiImage>();
            foreach(var image in found)using(var response=await client.GetAsync("view?filename="+Uri.EscapeDataString((string)image["filename"]??"")+"&subfolder="+Uri.EscapeDataString((string)image["subfolder"]??"")+"&type="+Uri.EscapeDataString((string)image["type"]??"output"),token))
            {if(!response.IsSuccessStatusCode)throw new InvalidOperationException("ComfyUI HTTP "+(int)response.StatusCode+" while downloading "+(string)image["filename"]);results.Add(new AiImage{Bytes=await response.Content.ReadAsByteArrayAsync()});}
            if(results.Count==0)throw new InvalidOperationException("The ComfyUI workflow finished without an image output. Add a Save Image node.");
            return results;
        }
        public int PollMilliseconds=1500;
        public static Dictionary<string,string> SelectForBatch(Dictionary<string,string> files,Config config)
        {
            string[] scene={"shape_lock","rendered","depth","depth_inverse","edges","silhouette","normal","mask","material_id"};
            string[] full={"reference","placement","scale_lock","shape_lock","rendered","depth","edges","silhouette","normal","mask","material_id","object_mask","inpaint_mask","occlusion_mask"};
            string[] detail={"reference","placement","scale_lock","shape_lock_detail","material_id_detail","normal_detail","edges_detail","depth_detail","object_mask","inpaint_mask","occlusion_mask"};
            var order=config.ProductMode?(config.DetailPriority&&files.ContainsKey("shape_lock_detail")?detail:full):scene;
            var selected=new Dictionary<string,string>();foreach(var key in order)if(files.ContainsKey(key))selected[key]=files[key];
            if(selected.Count==0)foreach(var file in files.Take(14))selected[file.Key]=file.Value;
            if(selected.Count>14)throw new InvalidOperationException("More than 14 images selected. Multi-image models such as Nano Banana accept at most 14.");
            // Style photos travel behind the controls, in the slots that are left.
            foreach(var style in files.Where(x=>x.Key.StartsWith(StyleReferences.Prefix)).OrderBy(x=>x.Key))if(selected.Count<14)selected[style.Key]=style.Value;
            return selected;
        }
        public static JObject PreviewUi(Dictionary<string,string> images)
        {
            var nodes=new JArray();var links=new JArray();int id=1,link=1,row=0;
            foreach(var image in images)
            {
                int source=id++,preview=id++,connection=link++;
                nodes.Add(new JObject{["id"]=source,["type"]="LoadImage",["pos"]=new JArray(40,row*380),["size"]=new JArray(300,320),["flags"]=new JObject(),["order"]=source,["mode"]=0,["inputs"]=new JArray(),["outputs"]=new JArray(new JObject{["name"]="IMAGE",["type"]="IMAGE",["links"]=new JArray(connection),["slot_index"]=0},new JObject{["name"]="MASK",["type"]="MASK",["links"]=null}),["properties"]=new JObject{["Node name for S&R"]="LoadImage"},["widgets_values"]=new JArray(image.Value,"image"),["title"]="RHINO:"+image.Key});
                nodes.Add(new JObject{["id"]=preview,["type"]="PreviewImage",["pos"]=new JArray(400,row*380),["size"]=new JArray(350,320),["flags"]=new JObject(),["order"]=preview,["mode"]=0,["inputs"]=new JArray(new JObject{["name"]="images",["type"]="IMAGE",["link"]=connection}),["outputs"]=new JArray(),["properties"]=new JObject{["Node name for S&R"]="PreviewImage"},["widgets_values"]=new JArray(),["title"]=image.Key});
                links.Add(new JArray(connection,source,0,preview,0,"IMAGE"));row++;
            }
            return new JObject{["last_node_id"]=id-1,["last_link_id"]=link-1,["nodes"]=nodes,["links"]=links,["groups"]=new JArray(),["config"]=new JObject(),["extra"]=new JObject(),["version"]=0.4};
        }
    }
}
