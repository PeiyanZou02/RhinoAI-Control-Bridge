using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RhinoAI
{
    public sealed class AiProvider
    {
        public string Id,Name,BaseUrl,KeyVariable;
        public string[] Models;
        public int MaxImages;
        public override string ToString(){return Name;}
    }
    public sealed class ProviderSettings
    {
        public string BaseUrl="",Model="";
        public string Aspect="auto",Resolution="auto"; // auto follows the exported frame
        public string Key=""; // protected by Secret, never the plain key
    }
    public sealed class AiImage
    {
        public byte[] Bytes;
        public string Extension {get{var b=Bytes;return b!=null&&b.Length>3&&b[0]==0xff&&b[1]==0xd8?".jpg":b!=null&&b.Length>3&&b[0]=='R'&&b[1]=='I'&&b[2]=='F'?".webp":".png";}}
    }
    public static class AiProviders
    {
        // ComfyUI is an engine too: it queues the API workflow from Export settings instead of calling a vendor.
        public const string Comfy="comfyui";
        public static readonly AiProvider[] All=
        {
            new AiProvider{Id="google",Name="Google Gemini (Nano Banana)",BaseUrl="https://generativelanguage.googleapis.com/v1beta",KeyVariable="GEMINI_API_KEY",Models=new[]{"gemini-2.5-flash-image","gemini-3-pro-image-preview"},MaxImages=14},
            new AiProvider{Id="openai",Name="OpenAI (GPT Image)",BaseUrl="https://api.openai.com/v1",KeyVariable="OPENAI_API_KEY",Models=new[]{"gpt-image-1","gpt-image-1-mini","gpt-image-1.5"},MaxImages=16},
            new AiProvider{Id="doubao",Name="Volcengine Doubao (Seedream)",BaseUrl="https://ark.cn-beijing.volces.com/api/v3",KeyVariable="ARK_API_KEY",Models=new[]{"doubao-seedream-4-0-250828","doubao-seedream-4-5-251128"},MaxImages=10},
            new AiProvider{Id=Comfy,Name="ComfyUI (queue the API workflow)",BaseUrl="",KeyVariable="",Models=new string[0],MaxImages=14}
        };
        public static AiProvider Find(string id){return All.FirstOrDefault(p=>p.Id==id)??All[0];}
        static readonly string[] Ratios={"auto","1:1","2:3","3:2","3:4","4:3","4:5","5:4","9:16","16:9","21:9"};
        // What each vendor and model really accepts. "auto" is always first and follows the exported frame.
        public static string[] Aspects(AiProvider provider)
        {
            if(provider.Id==Comfy)return new string[0];
            return provider.Id=="openai"?new[]{"auto","1:1","3:2","2:3"}:Ratios;
        }
        public static string[] Resolutions(AiProvider provider,string model)
        {
            model=(model??"").ToLowerInvariant();
            if(provider.Id=="google")return model.Contains("gemini-3")?new[]{"auto","1K","2K","4K"}:new[]{"auto"}; // Gemini 2.5 image is fixed near 1K
            if(provider.Id=="openai")return new[]{"auto","low","medium","high"}; // GPT Image sizes are fixed; this is its quality level
            if(provider.Id=="doubao")return model.Contains("seedream-4-0")?new[]{"auto","1K","2K","4K"}:new[]{"auto","2K","4K"};
            return new string[0];
        }
        public static string Pick(string[] options,string wanted){return options.Length==0?"":options.Contains(wanted)?wanted:options[0];}
        // Pixel size with the area of a square of that resolution, the way Seedream defines 1K, 2K and 4K.
        public static string PixelSize(string resolution,double ratio)
        {
            double edge=resolution=="1K"?1024:resolution=="4K"?4096:2048;ratio=Math.Max(1/16.0,Math.Min(16,ratio));
            return (int)Math.Round(edge*Math.Sqrt(ratio))+"x"+(int)Math.Round(edge/Math.Sqrt(ratio));
        }
    }
    // Keys rest in settings.json under Windows DPAPI, readable only by the same Windows user.
    public static class Secret
    {
        public static string Protect(string value)
        {
            if(string.IsNullOrEmpty(value))return "";
            try{return "dpapi:"+Convert.ToBase64String(Dpapi(Encoding.UTF8.GetBytes(value),true));}
            catch(Exception){return "plain:"+Convert.ToBase64String(Encoding.UTF8.GetBytes(value));}
        }
        public static string Reveal(string stored)
        {
            try
            {
                if(string.IsNullOrEmpty(stored))return "";
                if(stored.StartsWith("dpapi:"))return Encoding.UTF8.GetString(Dpapi(Convert.FromBase64String(stored.Substring(6)),false));
                if(stored.StartsWith("plain:"))return Encoding.UTF8.GetString(Convert.FromBase64String(stored.Substring(6)));
            }
            catch(Exception){}
            return ""; // copied from another user or machine: ask for the key again
        }
        // Kept out of line so a runtime without DPAPI fails inside the try blocks above.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static byte[] Dpapi(byte[] data,bool protect)
        {
            return protect?System.Security.Cryptography.ProtectedData.Protect(data,null,System.Security.Cryptography.DataProtectionScope.CurrentUser):System.Security.Cryptography.ProtectedData.Unprotect(data,null,System.Security.Cryptography.DataProtectionScope.CurrentUser);
        }
    }
    // C# twin of imageGuide in comfyui/rhino_ai_live/web/sync-core.mjs, for engines that receive the prompt directly.
    public static class PromptGuide
    {
        public static readonly string[] SceneChannels={"shape_lock","rendered","depth","depth_inverse","edges","silhouette","normal","mask","material_id"};
        public static readonly string[] DefaultChannels={"shape_lock","rendered","depth","material_id"};
        static readonly Dictionary<string,string> Roles=new Dictionary<string,string>
        {
            {"reference","MANDATORY PRIMARY BASE IMAGE AND SOLE APPEARANCE AUTHORITY; edit directly into this photograph while preserving its scene content, people, camera, composition, perspective, background and all unedited pixels. Preserve its exact color or monochrome mode, white balance, exposure, brightness, contrast, tonal range and colors. Never replace, recolor, dim or brighten the scene, and never output the object by itself"},
            {"shape_lock_detail","PRIMARY HIGH-RESOLUTION OBJECT GEOMETRY reference; preserve its fine openings, thin parts, seams, gaps, component count, local silhouette and proportions exactly, while the full placement image controls global scale and position"},
            {"material_id_detail","HIGH-RESOLUTION MATERIAL REGION AND INTERFACE reference; preserve every local material boundary, adjacency, seam, overlap, occlusion and front-to-back relationship. Never merge regions, swap materials or allow material bleeding"},
            {"normal_detail","high-resolution local surface-shape, orientation and fine-curvature reference"},
            {"edges_detail","high-resolution local hole, crease, thin-edge and internal part-boundary reference"},
            {"depth_detail","high-resolution local front-to-back order and relative depth reference"},
            {"shape_lock","PRIMARY geometry reference; preserve the exact object count, silhouette, openings, gaps, overlaps, part boundaries, proportions and camera"},
            {"rendered","secondary surface-volume and curvature reference; do not copy its temporary white material, lighting or background"},
            {"placement","ABSOLUTE FULL-FRAME authority for final object position, pixel size, rotation and silhouette bounds. Copy the object at exactly the scale and location shown in this complete canvas; do not enlarge, shrink or move it based on any detail image"},
            {"scale_lock","TECHNICAL MEASUREMENT INPUT ONLY, never an appearance reference. Its magenta silhouette, bounding rectangle and center crosshair encode only the exact final object extrema and center on the complete canvas. Treat every magenta pixel and every thin box/crosshair line as invisible metadata. The final object must touch the same left/right/top/bottom bounds without exceeding them, but the output must contain none of these marks. Reconstruct clean photograph pixels behind the marks from reference and placement"},
            {"depth","front-to-back order and relative spatial-depth reference"},
            {"depth_inverse","confirmation of the same depth relationships only"},
            {"edges","visible crease, hole and internal part-boundary reference"},
            {"silhouette","exact outer contour and negative-space reference"},
            {"normal","surface shape, orientation and curvature-direction reference"},
            {"mask","exact foreground occupancy reference"},
            {"material_id","MATERIAL REGION AND INTERFACE reference; each flat color identifies one separate Rhino layer/material region. Preserve every exact region boundary, adjacency, contact seam, overlap, occlusion and front-to-back stacking relationship. Use depth, edges and normal to determine which material region is in front at every connection. Never merge regions, swap their materials, let one material bleed across a boundary, erase a seam, or reproduce the flat ID colors in the final image"},
            {"object_mask","exact object-region and object-boundary reference"},
            {"inpaint_mask","EDITABLE NEIGHBORHOOD reference; white marks where existing content may be removed and local pixels may be regenerated, while black must be preserved. The white region is NOT the new object silhouette or size: never scale the object to fill it; final object scale and bounds come only from the full-frame placement image"},
            {"occlusion_mask","occlusion-protection reference; white protected foreground pixels of the photograph must remain unchanged and stay in front where appropriate"}
        };
        public static string Label(int index,string channel){return "Image "+(index+1)+" ("+channel+")";}
        // Images an engine receives, in prompt order. Background blend always sends its own fixed bundle.
        public static List<KeyValuePair<string,string>> Select(ExportResult export,Config config,int max,List<string> notes)
        {
            List<KeyValuePair<string,string>> images;
            if(config.ProductMode)images=ComfyClient.SelectForBatch(export.Files,config).ToList();
            else
            {
                var wanted=config.AiChannels==null||config.AiChannels.Count==0?DefaultChannels:config.AiChannels.ToArray();
                images=SceneChannels.Where(c=>wanted.Contains(c)&&export.Files.ContainsKey(c)).Select(c=>new KeyValuePair<string,string>(c,export.Files[c])).ToList();
            }
            if(images.Count==0)throw new InvalidOperationException("Tick at least one control image to send.");
            if(images.Count>max){if(notes!=null)notes.Add("This engine accepts "+max+" images. Left out: "+string.Join(", ",images.Skip(max).Select(x=>x.Key))+".");images=images.Take(max).ToList();}
            return images;
        }
        public static string Build(IList<string> channels,ExportResult export,Config config)
        {
            var lines=new List<string>();string brief=(config.AiPrompt??"").Trim();
            if(brief!="")lines.Add("CREATIVE BRIEF — the look of the finished image:\n"+brief+"\n");
            lines.Add("Use the supplied input images according to the exact roles below.");
            if(channels.Contains("reference"))lines.Add("MANDATORY REFERENCE APPEARANCE LOCK: reference alone controls the complete frame's color or monochrome mode, white balance, exposure, brightness, contrast, tonal range, colors and background. Never average, blend or transfer appearance from scale_lock, placement, masks, depth, normals, edges, shape_lock or material_id; they are technical data only.");
            if(channels.Contains("scale_lock"))lines.Add("MANDATORY CLEAN FINAL OUTPUT: return a natural finished photograph only. The scale_lock overlay is invisible metadata. Do not copy, retain, stylize, recolor or redraw any magenta/pink/purple silhouette, rectangle, center crosshair, guide line, marker, diagram, label or measurement graphic. Restore clean reference/placement pixels behind every guide mark while keeping the actual object.");
            for(int i=0;i<channels.Count;i++){string role;lines.Add(Label(i,channels[i])+": "+(Roles.TryGetValue(channels[i],out role)?role:"additional visual reference; use only for the information visibly encoded in this image")+".");}
            if(channels.Contains("reference"))
            {
                lines.Add("The reference image is the mandatory base canvas for the final output. Return an edited version of that same scene, keep its framing and background, and integrate the object at the placement shown; never return an isolated object on a new background.");
                lines.Add("Global-versus-detail rule: reference and placement control the full-frame composition, final object location and real scale in the scene. The *_detail images are magnified digital crops rendered from the IDENTICAL camera projection, lens, orientation and perspective rays; they are not alternate viewpoints. Use them only for fine geometry, curvature, openings, seams and material interfaces. Never infer a new camera, change perspective, enlarge the object or move it in the final full-frame image. The inpaint mask is only a permissible editing neighborhood; its white area is never a scale, shape or bounding-box reference. Geometry conflict priority: full-frame placement for scale/location, then shape_lock_detail or shape_lock > silhouette and edges > masks > depth and normal > rendered. Material ID images govern material regions only and must never alter geometry.");
            }
            else lines.Add("STANDARD RHINO SCENE MODE: preserve the exact full-frame camera, composition, object count, silhouettes, openings, overlaps and relative scale shown by the control images. Use depth, edges, silhouette, normal and mask only as coordinated geometry evidence. Use material_id only to assign the requested materials to its flat-color regions. The technical control images must never appear in the result.");
            if(!string.IsNullOrWhiteSpace(export.PlacementConstraint))lines.Add(export.PlacementConstraint.Trim());
            if(!string.IsNullOrWhiteSpace(export.Prompt)&&channels.Any(c=>c.StartsWith("material_id")))lines.Add("Material mapping for the material_id image:\n"+export.Prompt.Trim());
            string instruction=config.ProductMode?(config.WearInstructions??"").Trim():"";
            if(instruction!="")lines.Add("Rhino user instruction — follow this requested edit exactly:\n"+instruction);
            if(channels.Contains("scale_lock"))lines.Add("FINAL VALIDATION BEFORE OUTPUT: inspect the completed image and remove every technical overlay originating from scale_lock. Compare every pixel outside the object edit region against reference and restore its original brightness, contrast, tonal range and color exactly.");
            lines.Add("Return exactly one finished image with the same framing and aspect ratio as the inputs.");
            return string.Join("\n",lines);
        }
    }
    // Direct vendor calls. The key goes only to the address shown in the panel, over the request header.
    public sealed class AiClient : IDisposable
    {
        static readonly string[] GeminiRatios={"1:1","2:3","3:2","3:4","4:3","4:5","5:4","9:16","16:9","21:9"};
        readonly HttpClient client;
        public AiClient() : this(new HttpClientHandler()) {}
        public AiClient(HttpMessageHandler handler)
        {
            ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
            client=new HttpClient(handler){Timeout=TimeSpan.FromMinutes(8)};
        }
        public void Dispose(){client.Dispose();}
        public static string NearestRatio(int width,int height)
        {
            double wanted=Math.Log(width/(double)Math.Max(1,height));
            return GeminiRatios.OrderBy(r=>{var p=r.Split(':');return Math.Abs(Math.Log(double.Parse(p[0])/double.Parse(p[1]))-wanted);}).First();
        }
        public async Task<List<AiImage>> Generate(AiProvider provider,ProviderSettings settings,string key,string prompt,IList<KeyValuePair<string,string>> images,int width,int height,CancellationToken token)
        {
            if(string.IsNullOrWhiteSpace(key))throw new InvalidOperationException("Enter the "+provider.Name+" API key, or set the "+provider.KeyVariable+" environment variable.");
            string root=(string.IsNullOrWhiteSpace(settings.BaseUrl)?provider.BaseUrl:settings.BaseUrl).Trim().TrimEnd('/'),model=string.IsNullOrWhiteSpace(settings.Model)?provider.Models[0]:settings.Model.Trim();
            Uri uri;if(!Uri.TryCreate(root,UriKind.Absolute,out uri)||(uri.Scheme!="https"&&!uri.IsLoopback))throw new ArgumentException("The API address must start with https://. If you pasted the key there, move it to API key and clear the address to restore the default.");
            string aspect=AiProviders.Pick(AiProviders.Aspects(provider),settings.Aspect),resolution=AiProviders.Pick(AiProviders.Resolutions(provider,model),settings.Resolution);
            if(resolution=="auto"&&provider.Id!="openai")resolution=Math.Max(width,height)<=1024?"1K":Math.Max(width,height)<=2048?"2K":"4K";
            HttpRequestMessage request;
            if(provider.Id=="google")
            {
                var parts=new JArray{new JObject{["text"]=prompt}};
                for(int i=0;i<images.Count;i++){parts.Add(new JObject{["text"]=PromptGuide.Label(i,images[i].Key)+":"});parts.Add(new JObject{["inlineData"]=new JObject{["mimeType"]="image/png",["data"]=Convert.ToBase64String(File.ReadAllBytes(images[i].Value))}});}
                var image=new JObject{["aspectRatio"]=aspect=="auto"?NearestRatio(width,height):aspect};
                // Only the Gemini 3 image models take an output size.
                if(model.ToLowerInvariant().Contains("gemini-3"))image["imageSize"]=resolution;
                var body=new JObject{["contents"]=new JArray(new JObject{["role"]="user",["parts"]=parts}),["generationConfig"]=new JObject{["responseModalities"]=new JArray("TEXT","IMAGE"),["imageConfig"]=image}};
                request=new HttpRequestMessage(HttpMethod.Post,root+"/models/"+Uri.EscapeDataString(model)+":generateContent"){Content=new StringContent(body.ToString(Formatting.None),Encoding.UTF8,"application/json")};
                request.Headers.Add("x-goog-api-key",key.Trim());
            }
            else if(provider.Id=="openai")
            {
                var form=new MultipartFormDataContent();form.Add(new StringContent(model),"model");form.Add(new StringContent(prompt),"prompt");
                form.Add(new StringContent(aspect=="1:1"?"1024x1024":aspect=="3:2"?"1536x1024":aspect=="2:3"?"1024x1536":width>height*1.15?"1536x1024":height>width*1.15?"1024x1536":"1024x1024"),"size");
                if(resolution!="auto")form.Add(new StringContent(resolution),"quality");
                foreach(var item in images){var bytes=new ByteArrayContent(File.ReadAllBytes(item.Value));bytes.Headers.ContentType=new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");form.Add(bytes,"image[]",item.Key+".png");}
                request=new HttpRequestMessage(HttpMethod.Post,root+"/images/edits"){Content=form};
                request.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",key.Trim());
            }
            else if(provider.Id=="doubao")
            {
                var body=new JObject{["model"]=model,["prompt"]=prompt,["image"]=new JArray(images.Select(x=>"data:image/png;base64,"+Convert.ToBase64String(File.ReadAllBytes(x.Value)))),["size"]=AiProviders.PixelSize(AiProviders.Resolutions(provider,model).Contains(resolution)?resolution:"2K",aspect=="auto"?width/(double)Math.Max(1,height):double.Parse(aspect.Split(':')[0])/double.Parse(aspect.Split(':')[1])),["sequential_image_generation"]="disabled",["response_format"]="b64_json",["watermark"]=false};
                request=new HttpRequestMessage(HttpMethod.Post,root+"/images/generations"){Content=new StringContent(body.ToString(Formatting.None),Encoding.UTF8,"application/json")};
                request.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",key.Trim());
            }
            else throw new InvalidOperationException("Unknown AI engine: "+provider.Id);
            using(request)using(var response=await client.SendAsync(request,token))
            {
                string text=await response.Content.ReadAsStringAsync();JObject data=null;
                try{data=JObject.Parse(text);}catch(JsonException){}
                if(!response.IsSuccessStatusCode)throw new InvalidOperationException(provider.Name+" HTTP "+(int)response.StatusCode+": "+Short(data==null?text:((string)data.SelectToken("error.message")??text)));
                if(data==null)throw new InvalidOperationException(provider.Name+" returned an unreadable reply.");
                var results=new List<AiImage>();
                if(provider.Id=="google")
                {
                    foreach(var part in data.SelectTokens("candidates[*].content.parts[*]").OfType<JObject>()){string b64=(string)(part.SelectToken("inlineData.data")??part.SelectToken("inline_data.data"));if(!string.IsNullOrEmpty(b64))results.Add(new AiImage{Bytes=Convert.FromBase64String(b64)});}
                    if(results.Count==0)throw new InvalidOperationException(provider.Name+" returned no image"+Reason((string)data.SelectToken("promptFeedback.blockReason")??(string)data.SelectToken("candidates[0].finishReason"),string.Join(" ",data.SelectTokens("candidates[*].content.parts[*].text").Select(x=>(string)x))));
                }
                else
                {
                    foreach(var item in data.SelectTokens("data[*]").OfType<JObject>())
                    {
                        string b64=(string)item["b64_json"],url=(string)item["url"];
                        if(!string.IsNullOrEmpty(b64))results.Add(new AiImage{Bytes=Convert.FromBase64String(b64)});
                        else if(!string.IsNullOrEmpty(url))using(var download=await client.GetAsync(url,token)){download.EnsureSuccessStatusCode();results.Add(new AiImage{Bytes=await download.Content.ReadAsByteArrayAsync()});}
                    }
                    if(results.Count==0)throw new InvalidOperationException(provider.Name+" returned no image"+Reason((string)data.SelectToken("error.code"),(string)data.SelectToken("error.message")));
                }
                return results;
            }
        }
        static string Reason(string code,string text){string detail=((code??"")+" "+(text??"")).Trim();return detail==""?".":": "+Short(detail);}
        static string Short(string text){text=(text??"").Trim();return text.Length>600?text.Substring(0,600)+"…":text;}
    }
}
