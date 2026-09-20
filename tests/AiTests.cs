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
    public static class AiTests
    {
        sealed class Vendor : HttpMessageHandler
        {
            public readonly List<HttpRequestMessage> Requests=new List<HttpRequestMessage>();public readonly List<string> Bodies=new List<string>();
            public Func<HttpRequestMessage,HttpResponseMessage> Reply;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
            {
                Requests.Add(request);Bodies.Add(request.Content==null?"":await request.Content.ReadAsStringAsync());return Reply(request);
            }
        }
        static HttpResponseMessage Json(string body,HttpStatusCode code=HttpStatusCode.OK){return new HttpResponseMessage(code){Content=new StringContent(body,Encoding.UTF8,"application/json")};}
        static T Wait<T>(Func<Task<T>> work){return Task.Run(work).GetAwaiter().GetResult();}
        public static string Run(string root,string png)
        {
            Directory.CreateDirectory(root);int count=0;
            Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception(name);count++;System.Console.WriteLine("PASS: "+name);};
            byte[] pixels=File.ReadAllBytes(png);string b64=Convert.ToBase64String(pixels);
            var inputs=new List<KeyValuePair<string,string>>{new KeyValuePair<string,string>("rendered",png),new KeyValuePair<string,string>("material_id",png)};
            var none=CancellationToken.None;

            var google=new Vendor{Reply=r=>Json("{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"done\"},{\"inlineData\":{\"mimeType\":\"image/png\",\"data\":\""+b64+"\"}}]}}]}")};
            var made=Wait(async()=>{using(var client=new AiClient(google))return await client.Generate(AiProviders.Find("google"),new ProviderSettings(),"g-key","make it real",inputs,1024,589,none);});
            var sent=JObject.Parse(google.Bodies[0]);
            check(google.Requests[0].RequestUri.ToString()=="https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash-image:generateContent","Gemini default endpoint and model");
            check(google.Requests[0].Headers.GetValues("x-goog-api-key").Single()=="g-key"&&!google.Requests[0].RequestUri.ToString().Contains("g-key"),"Gemini key travels in the header, never the URL");
            check(sent.SelectTokens("contents[0].parts[*].inlineData").Count()==2&&(string)sent.SelectToken("contents[0].parts[0].text")=="make it real","Gemini receives the prompt and every control image");
            check((string)sent.SelectToken("generationConfig.imageConfig.aspectRatio")=="16:9"&&sent.SelectToken("generationConfig.imageConfig.imageSize")==null,"Gemini aspect ratio follows the export, no size for 2.5");
            check(made.Count==1&&made[0].Bytes.SequenceEqual(pixels)&&made[0].Extension==".png","Gemini inline image decoded");
            var pro=new Vendor{Reply=google.Reply};
            Wait(async()=>{using(var client=new AiClient(pro))return await client.Generate(AiProviders.Find("google"),new ProviderSettings{Model="gemini-3-pro-image-preview",BaseUrl="https://proxy.example/v1beta/"},"k","p",inputs,2048,2048,none);});
            check(pro.Requests[0].RequestUri.ToString().StartsWith("https://proxy.example/v1beta/models/gemini-3-pro-image-preview")&&(string)JObject.Parse(pro.Bodies[0]).SelectToken("generationConfig.imageConfig.imageSize")=="2K","custom address, custom model and Gemini 3 output size");

            var blocked=new Vendor{Reply=r=>Json("{\"candidates\":[{\"finishReason\":\"IMAGE_SAFETY\",\"content\":{\"parts\":[{\"text\":\"cannot\"}]}}]}")};string message="";
            try{Wait(async()=>{using(var client=new AiClient(blocked))return await client.Generate(AiProviders.Find("google"),new ProviderSettings(),"k","p",inputs,100,100,none);});}catch(InvalidOperationException e){message=e.Message;}
            check(message.Contains("IMAGE_SAFETY")&&message.Contains("cannot"),"a reply without an image explains why");

            var openai=new Vendor{Reply=r=>Json("{\"data\":[{\"b64_json\":\""+b64+"\"}]}")};
            made=Wait(async()=>{using(var client=new AiClient(openai))return await client.Generate(AiProviders.Find("openai"),new ProviderSettings(),"o-key","p",inputs,600,1000,none);});
            check(openai.Requests[0].RequestUri.ToString()=="https://api.openai.com/v1/images/edits"&&openai.Requests[0].Headers.Authorization.ToString()=="Bearer o-key","OpenAI edits endpoint with bearer key");
            check(openai.Bodies[0].Split(new[]{"name=\"image[]\""},StringSplitOptions.None).Length==3&&openai.Bodies[0].Contains("1024x1536")&&openai.Bodies[0].Contains("gpt-image-1"),"OpenAI multipart carries both images, the model and a portrait size");
            check(made.Count==1&&made[0].Bytes.Length==pixels.Length,"OpenAI base64 image decoded");

            var doubao=new Vendor{Reply=r=>r.RequestUri.Host=="cdn.example"?new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(new byte[]{0xff,0xd8,0xff,0xe0})}:Json("{\"data\":[{\"url\":\"https://cdn.example/out.jpg\"}]}")};
            made=Wait(async()=>{using(var client=new AiClient(doubao))return await client.Generate(AiProviders.Find("doubao"),new ProviderSettings(),"d-key","p",inputs,1024,589,none);});
            sent=JObject.Parse(doubao.Bodies[0]);
            check(doubao.Requests[0].RequestUri.ToString()=="https://ark.cn-beijing.volces.com/api/v3/images/generations"&&((JArray)sent["image"]).Count==2&&((string)sent["image"][0]).StartsWith("data:image/png;base64,")&&(bool)sent["watermark"]==false,"Doubao request carries data-URL images without a watermark");
            check(made.Count==1&&made[0].Extension==".jpg"&&doubao.Requests[1].Headers.Authorization==null,"Doubao URL result downloaded without forwarding the key");

            var denied=new Vendor{Reply=r=>Json("{\"error\":{\"message\":\"Incorrect API key provided\"}}",HttpStatusCode.Unauthorized)};message="";
            try{Wait(async()=>{using(var client=new AiClient(denied))return await client.Generate(AiProviders.Find("openai"),new ProviderSettings(),"secret-key","p",inputs,100,100,none);});}catch(InvalidOperationException e){message=e.Message;}
            check(message.Contains("401")&&message.Contains("Incorrect API key")&&!message.Contains("secret-key"),"vendor error is readable and never echoes the key");
            bool refused=false;try{Wait(async()=>{using(var client=new AiClient(denied))return await client.Generate(AiProviders.Find("openai"),new ProviderSettings{BaseUrl="http://plain.example/v1"},"k","p",inputs,100,100,none);});}catch(ArgumentException){refused=true;}
            check(refused&&denied.Requests.Count==1,"a key is never sent over plain http");
            refused=false;try{Wait(async()=>{using(var client=new AiClient(denied))return await client.Generate(AiProviders.Find("google"),new ProviderSettings(),"","p",inputs,100,100,none);});}catch(InvalidOperationException e){refused=e.Message.Contains("GEMINI_API_KEY");}
            check(refused,"missing key names the environment variable");

            string stored=Secret.Protect("sk-test-123");
            check(stored!=""&&!stored.Contains("sk-test-123")&&Secret.Reveal(stored)=="sk-test-123"&&Secret.Reveal("dpapi:broken")==""&&Secret.Protect("")=="","API key round-trips through protected storage");
            var saved=Newtonsoft.Json.JsonConvert.SerializeObject(new Config{AiProviders={{"google",new ProviderSettings{Key=stored}}}});
            check(!saved.Contains("sk-test-123"),"settings never hold the plain key");
            var reloaded=Newtonsoft.Json.JsonConvert.DeserializeObject<Config>(Newtonsoft.Json.JsonConvert.SerializeObject(new Config{AiChannels=new List<string>{"depth"},AiViews=new List<string>{"A","B"}}));
            check(reloaded.AiChannels.Count==1&&reloaded.AiViews.Count==2,"ticked channels and views reload without duplicates");

            var export=new ExportResult{Directory=root,Files=PromptGuide.SceneChannels.ToDictionary(c=>c,c=>png),Prompt="#FF0000 = oak"};
            var config=new Config{AiPrompt="Dusk exterior",AiChannels=new List<string>{"material_id","rendered"}};
            var chosen=PromptGuide.Select(export,config,14,null);
            check(chosen.Select(x=>x.Key).SequenceEqual(new[]{"rendered","material_id"}),"ticked control images are sent in a stable order");
            string guide=PromptGuide.Build(chosen.Select(x=>x.Key).ToList(),export,config);
            check(guide.StartsWith("CREATIVE BRIEF")&&guide.Contains("Dusk exterior")&&guide.Contains("Image 2 (material_id)")&&guide.Contains("#FF0000 = oak")&&guide.Contains("STANDARD RHINO SCENE MODE"),"prompt joins the brief, image roles and material mapping");
            check(!PromptGuide.Build(new List<string>{"rendered"},export,config).Contains("#FF0000"),"material mapping is left out when material_id is not sent");
            var notes=new List<string>();check(PromptGuide.Select(export,new Config{AiChannels=PromptGuide.SceneChannels.ToList()},3,notes).Count==3&&notes.Count==1,"engine image limit is applied and reported");
            check(PromptGuide.Select(export,new Config(),14,null).Count==PromptGuide.DefaultChannels.Length,"default control images used when none were saved");
            check(AiClient.NearestRatio(683,1024)=="2:3"&&AiClient.NearestRatio(1000,1000)=="1:1","nearest supported aspect ratio");

            string workflow=Path.Combine(root,"job.api.json");
            File.WriteAllText(workflow,"{\"1\":{\"class_type\":\"LoadImage\",\"inputs\":{\"image\":\"x.png\"},\"_meta\":{\"title\":\"RHINO:depth\"}},\"2\":{\"class_type\":\"Text\",\"inputs\":{\"a\":\"{{positive_prompt}}\",\"b\":\"{{full_prompt}}\"}}}");
            int polls=0;JObject queued=null;
            var comfy=new Vendor();comfy.Reply=r=>
            {
                string path=r.RequestUri.AbsolutePath;
                if(path=="/system_stats")return Json("{}");
                if(path=="/upload/image")return Json("{\"name\":\"up.png\",\"subfolder\":\"rhino_ai/job\"}");
                if(path=="/prompt"){queued=JObject.Parse(comfy.Bodies.Last());return Json("{\"prompt_id\":\"abc\"}");}
                if(path=="/history/abc")return Json(++polls<2?"{}":"{\"abc\":{\"status\":{\"status_str\":\"success\",\"completed\":true},\"outputs\":{\"9\":{\"images\":[{\"filename\":\"tmp.png\",\"subfolder\":\"\",\"type\":\"temp\"},{\"filename\":\"out.png\",\"subfolder\":\"\",\"type\":\"output\"}]}}}}");
                if(path=="/view"&&r.RequestUri.Query.Contains("filename=out.png"))return new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(pixels)};
                throw new Exception("unexpected request: "+r.RequestUri);
            };
            var comfyConfig=new Config{Workflow=workflow,AiPrompt="Dusk exterior"};
            made=Wait(async()=>{using(var client=new ComfyClient("http://localhost:8000",comfy){PollMilliseconds=1})return await client.Generate(export,comfyConfig,none);});
            check(made.Count==1&&made[0].Bytes.SequenceEqual(pixels)&&polls==2,"ComfyUI engine queues, waits and downloads the saved output only");
            check((string)queued["prompt"]["1"]["inputs"]["image"]=="rhino_ai/job/up.png"&&(string)queued["prompt"]["2"]["inputs"]["a"]=="Dusk exterior"&&((string)queued["prompt"]["2"]["inputs"]["b"]).Contains("Image 1 (shape_lock)"),"queued workflow is bound to the uploads and both prompt placeholders");
            var broken=new Vendor();broken.Reply=r=>r.RequestUri.AbsolutePath=="/prompt"?Json("{\"prompt_id\":\"abc\"}"):r.RequestUri.AbsolutePath=="/history/abc"?Json("{\"abc\":{\"status\":{\"status_str\":\"error\",\"completed\":false,\"messages\":[[\"execution_error\",{\"node_type\":\"KSampler\",\"exception_message\":\"out of memory\"}]]},\"outputs\":{}}}"):comfy.Reply(r);message="";
            try{Wait(async()=>{using(var client=new ComfyClient("http://localhost:8000",broken){PollMilliseconds=1})return await client.Generate(export,comfyConfig,none);});}catch(InvalidOperationException e){message=e.Message;}
            check(message.Contains("KSampler")&&message.Contains("out of memory"),"ComfyUI execution error is reported");
            return count+" AI checks passed";
        }
    }
}
