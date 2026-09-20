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
            Uri uri;if(!Uri.TryCreate(server.TrimEnd('/')+"/",UriKind.Absolute,out uri)||(uri.Scheme!="http"&&uri.Scheme!="https"))throw new ArgumentException("ComfyUI 地址必须以 http:// 或 https:// 开头。");
            client=new HttpClient(handler){BaseAddress=uri,Timeout=TimeSpan.FromSeconds(90)};
        }
        public void Dispose(){client.Dispose();}
        async Task<JObject> Json(HttpResponseMessage response)
        {
            using(response){string text=await response.Content.ReadAsStringAsync();if(!response.IsSuccessStatusCode)throw new InvalidOperationException("ComfyUI HTTP "+(int)response.StatusCode+": "+text);return JObject.Parse(text);}
        }
        public async Task Check(){await Json(await client.GetAsync("system_stats"));}
        public async Task<string> Upload(string path,string folder)
        {
            using(var form=new MultipartFormDataContent())using(var stream=File.OpenRead(path))
            {
                var bytes=new StreamContent(stream);bytes.Headers.ContentType=new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
                form.Add(bytes,"image",Path.GetFileName(path));form.Add(new StringContent("input"),"type");form.Add(new StringContent("false"),"overwrite");form.Add(new StringContent(folder),"subfolder");
                JObject data=await Json(await client.PostAsync("upload/image",form));
                string name=(string)data["name"],sub=(string)data["subfolder"];
                if(string.IsNullOrEmpty(name))throw new InvalidOperationException("ComfyUI 上传响应没有文件名。");
                return string.IsNullOrEmpty(sub)?name:sub.TrimEnd('/')+"/"+name;
            }
        }
        public static JObject Bind(JObject graph,Dictionary<string,string> images,ExportResult export)
        {
            if(graph["nodes"]!=null)throw new InvalidOperationException("这是界面格式工作流，请在 ComfyUI 导出 API 格式 JSON。");
            if(graph["prompt"] is JObject)graph=(JObject)graph["prompt"];
            graph=(JObject)graph.DeepClone();
            foreach(var property in graph.Properties())
            {
                var node=property.Value as JObject;if(node==null || node["class_type"]==null || !(node["inputs"] is JObject))throw new InvalidOperationException("工作流不是有效的 ComfyUI API 格式。");
                var inputs=(JObject)node["inputs"];string title=(string)node.SelectToken("_meta.title")??"";
                if((string)node["class_type"]=="LoadImage" && title.StartsWith("RHINO:",StringComparison.OrdinalIgnoreCase))
                {
                    string channel=title.Substring(6).Trim().ToLowerInvariant();if(!images.ContainsKey(channel))throw new InvalidOperationException("工作流需要未导出的通道："+channel);inputs["image"]=images[channel];
                }
                foreach(var value in inputs.Descendants().OfType<JValue>().ToList())
                {
                    if(value.Type!=JTokenType.String)continue;string text=(string)value;
                    foreach(var image in images)text=text.Replace("{{"+image.Key+"}}",image.Value);
                    text=text.Replace("{{color_materials}}",export.Prompt??"").Replace("{{material_prompt}}",export.Prompt??"").Replace("{{positive_prompt}}","").Replace("{{wear_prompt}}","");
                    if(text.Contains("{{"))throw new InvalidOperationException("工作流有未匹配占位符："+text);
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
        public async Task<string> Send(ExportResult export,Config config)
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
            string userPrompt=config.ProductMode?string.Join("\n",new[]{string.IsNullOrWhiteSpace(config.Product)?null:"Product: "+config.Product,config.WearInstructions}.Where(x=>!string.IsNullOrWhiteSpace(x))):"";
            var manifest=new JObject{["schema"]="rhino-ai-live/1",["revision"]=Guid.NewGuid().ToString("N"),["images"]=JObject.FromObject(uploaded),["prompts"]=new JObject{["color_materials"]=export.Prompt??"",["user_prompt"]=userPrompt,["placement_constraint"]=export.PlacementConstraint??""},["input_profile"]=config.ProductMode?(config.DetailPriority?"detail_priority":"full_frame"):"scene"};
            // This is ComfyUI's user-file endpoint, not its execution endpoint.
            using(var response=await client.PostAsync("userdata/"+Uri.EscapeDataString("rhino_ai_latest.json")+"?overwrite=true",new StringContent(manifest.ToString(),Encoding.UTF8,"application/json")))
            {if(!response.IsSuccessStatusCode)throw new InvalidOperationException("图片已上传，但同步清单发布失败："+(int)response.StatusCode+" "+await response.Content.ReadAsStringAsync());}
            File.WriteAllText(Path.Combine(export.Directory,"comfy_sync.json"),manifest.ToString(),Encoding.UTF8);
            return "图片与提示词已上传。请以 ComfyUI 画布底部的“Rhino：图片已接入 Batch”状态为准；没有 Rhino 按钮说明该窗口尚未加载同步扩展。生成由你手动点击 Run。";
        }
        public static Dictionary<string,string> SelectForBatch(Dictionary<string,string> files,Config config)
        {
            string[] scene={"shape_lock","rendered","depth","depth_inverse","edges","silhouette","normal","mask","material_id"};
            string[] full={"reference","placement","scale_lock","shape_lock","rendered","depth","edges","silhouette","normal","mask","material_id","product_mask","inpaint_mask","occlusion_mask"};
            string[] detail={"reference","placement","scale_lock","shape_lock_detail","material_id_detail","normal_detail","edges_detail","depth_detail","product_mask","inpaint_mask","occlusion_mask"};
            var order=config.ProductMode?(config.DetailPriority&&files.ContainsKey("shape_lock_detail")?detail:full):scene;
            var selected=new Dictionary<string,string>();foreach(var key in order)if(files.ContainsKey(key))selected[key]=files[key];
            if(selected.Count==0)foreach(var file in files.Take(14))selected[file.Key]=file.Value;
            if(selected.Count>14)throw new InvalidOperationException("同步图片超过 Nano Banana 的 14 张上限。");
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
