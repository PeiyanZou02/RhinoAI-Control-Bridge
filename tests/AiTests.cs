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

            var sized=new Vendor{Reply=google.Reply};
            Wait(async()=>{using(var client=new AiClient(sized))return await client.Generate(AiProviders.Find("google"),new ProviderSettings{Model="gemini-3-pro-image-preview",Aspect="4:5",Resolution="4K"},"k","p",inputs,1024,589,none);});
            check((string)JObject.Parse(sized.Bodies[0]).SelectToken("generationConfig.imageConfig.aspectRatio")=="4:5"&&(string)JObject.Parse(sized.Bodies[0]).SelectToken("generationConfig.imageConfig.imageSize")=="4K","chosen Gemini aspect ratio and resolution are sent");
            Wait(async()=>{using(var client=new AiClient(sized))return await client.Generate(AiProviders.Find("google"),new ProviderSettings{Resolution="4K"},"k","p",inputs,1024,589,none);});
            check(JObject.Parse(sized.Bodies[1]).SelectToken("generationConfig.imageConfig.imageSize")==null,"a resolution the model cannot take is never sent");
            check(AiProviders.Resolutions(AiProviders.Find("google"),"gemini-2.5-flash-image").SequenceEqual(new[]{"auto"})&&AiProviders.Resolutions(AiProviders.Find("doubao"),"doubao-seedream-4-5-251128").SequenceEqual(new[]{"auto","2K","4K"})&&AiProviders.Aspects(AiProviders.Find("openai")).Length==4&&AiProviders.Aspects(AiProviders.Find(AiProviders.Comfy)).Length==0,"options follow the engine and model");
            check(AiProviders.PixelSize("2K",16/9.0)=="2731x1536"&&AiProviders.PixelSize("1K",1)=="1024x1024","pixel size keeps the resolution's area at any ratio");

            var blocked=new Vendor{Reply=r=>Json("{\"candidates\":[{\"finishReason\":\"IMAGE_SAFETY\",\"content\":{\"parts\":[{\"text\":\"cannot\"}]}}]}")};string message="";
            try{Wait(async()=>{using(var client=new AiClient(blocked))return await client.Generate(AiProviders.Find("google"),new ProviderSettings(),"k","p",inputs,100,100,none);});}catch(InvalidOperationException e){message=e.Message;}
            check(message.Contains("IMAGE_SAFETY")&&message.Contains("cannot"),"a reply without an image explains why");

            var openai=new Vendor{Reply=r=>Json("{\"data\":[{\"b64_json\":\""+b64+"\"}]}")};
            made=Wait(async()=>{using(var client=new AiClient(openai))return await client.Generate(AiProviders.Find("openai"),new ProviderSettings(),"o-key","p",inputs,600,1000,none);});
            check(openai.Requests[0].RequestUri.ToString()=="https://api.openai.com/v1/images/edits"&&openai.Requests[0].Headers.Authorization.ToString()=="Bearer o-key","OpenAI edits endpoint with bearer key");
            check(openai.Bodies[0].Split(new[]{"name=\"image[]\""},StringSplitOptions.None).Length==3&&openai.Bodies[0].Contains("1024x1536")&&openai.Bodies[0].Contains("gpt-image-1"),"OpenAI multipart carries both images, the model and a portrait size");
            check(made.Count==1&&made[0].Bytes.Length==pixels.Length,"OpenAI base64 image decoded");
            check(!openai.Bodies[0].Contains("name=quality")&&!openai.Bodies[0].Contains("name=\"quality\""),"OpenAI quality is left to the vendor on auto");
            Wait(async()=>{using(var client=new AiClient(openai))return await client.Generate(AiProviders.Find("openai"),new ProviderSettings{Aspect="3:2",Resolution="high"},"k","p",inputs,600,1000,none);});
            check(openai.Bodies[1].Contains("1536x1024")&&openai.Bodies[1].Contains("high"),"chosen OpenAI size and quality are sent");

            var doubao=new Vendor{Reply=r=>r.RequestUri.Host=="cdn.example"?new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(new byte[]{0xff,0xd8,0xff,0xe0})}:Json("{\"data\":[{\"url\":\"https://cdn.example/out.jpg\"}]}")};
            made=Wait(async()=>{using(var client=new AiClient(doubao))return await client.Generate(AiProviders.Find("doubao"),new ProviderSettings(),"d-key","p",inputs,1024,589,none);});
            sent=JObject.Parse(doubao.Bodies[0]);
            check(doubao.Requests[0].RequestUri.ToString()=="https://ark.cn-beijing.volces.com/api/v3/images/generations"&&((JArray)sent["image"]).Count==2&&((string)sent["image"][0]).StartsWith("data:image/png;base64,")&&(bool)sent["watermark"]==false,"Doubao request carries data-URL images without a watermark");
            check((string)sent["size"]==AiProviders.PixelSize("1K",1024/589.0),"Doubao auto size follows the exported frame");
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
            check(guide.Contains("MANDATORY MATERIAL ID COLOR BAN")&&guide.IndexOf("MANDATORY MATERIAL ID COLOR BAN")<guide.IndexOf("Image 1 (")&&guide.Contains("never purple wood")&&guide.Contains("FINAL MATERIAL COLOR CHECK BEFORE OUTPUT")&&guide.Contains("never a color to paint"),"ID colors are banned as appearance before the roles and checked again at the end");
            check(guide.Contains("MANDATORY CAMERA AND COMPOSITION LOCK")&&guide.Contains("Use the rendered image as the base canvas")&&guide.Contains("no base, plinth, pedestal")&&guide.Contains("FINAL CAMERA CHECK BEFORE OUTPUT")&&guide.IndexOf("CAMERA AND COMPOSITION LOCK")<guide.IndexOf("MATERIAL ID COLOR BAN"),"scene renders are framed as an in-place edit with a locked camera");
            check(PromptGuide.Build(new List<string>{"shape_lock"},export,config).Contains("Use the shape_lock image as the base canvas")&&!PromptGuide.Build(new List<string>{"reference","placement"},export,config).Contains("CAMERA AND COMPOSITION LOCK"),"the base image follows what is sent, and background blend keeps its own placement lock");
            check(guide.Contains("MATERIAL ASSIGNMENT RULE")&&guide.IndexOf("MATERIAL ASSIGNMENT RULE")>guide.IndexOf("#FF0000 = oak"),"the assignment rule follows the mapping");
            var legend=new List<LayerRule>{new LayerRule{Index=0,Name="wood",Color="#ff1744",Material="dark plywood"},new LayerRule{Index=3,Name="playwood",Color="#651FFF",Material="model plywood"},new LayerRule{Index=4,Name="concrete \"floor\"",Color="#00E676",Material="concrete floor"},new LayerRule{Index=5,Name="unused",Color="#FFFFFF",Material=" "}};
            string mapping=MaterialRules.Prompt(legend,new Dictionary<int,double>{{1,0.184},{4,0.62},{5,0.004}});
            check(mapping.Split('\n').Length==3&&mapping.StartsWith("#651FFF — the VIOLET-PURPLE region, about 62% of the frame, Rhino layer \"playwood\" = model plywood")&&mapping.Contains("#FF1744 — the RED region, about 18% of the frame, Rhino layer \"wood\" = dark plywood")&&mapping.Contains("the GREEN region, under 1% of the frame, Rhino layer \"concrete 'floor'\" = concrete floor"),"mapping names each color, its share and layer, largest region first");
            check(MaterialRules.Prompt(legend,null,"material").Contains("Rhino material \"wood\" = dark plywood")&&!MaterialRules.Prompt(legend,null,"material").Contains("Rhino layer"),"the mapping names materials when regions come from materials");
            var byMaterial=new Config{MaterialIdSource="material"};check(byMaterial.ByMaterial()&&ReferenceEquals(byMaterial.Rules(),byMaterial.Materials)&&!new Config().ByMaterial()&&ReferenceEquals(new Config().Rules(),new Config().Layers)==false,"the Material ID source picks the rule list");
            var guessed=MaterialRules.Guess("m1",3,"Brushed Steel");check(guessed.Id=="m1"&&guessed.Index==3&&guessed.Material=="satin silver metal","a material rule guesses its target from the material name");
            check(MaterialRules.TargetFromName("KVANT / Ivory_enamel")=="ivory enamel"&&MaterialRules.TargetFromName("Site::Walls::Brick-red")=="brick red"&&MaterialRules.TargetFromName("  ")=="neutral matte material","Rhino names become readable target materials");
            check(Raster.ColorName(0x00E5FF)=="cyan"&&Raster.ColorName(0xFFEA00)=="yellow"&&Raster.ColorName(0xFF6D00)=="orange"&&Raster.ColorName(0x2979FF)=="blue"&&Raster.ColorName(0xF500FF)=="magenta"&&Raster.ColorName(0x76FF03)=="lime green"&&Raster.ColorName(0xFF4081)=="pink"&&Raster.ColorName(0x00BFA5)=="teal"&&Raster.ColorName(0xC6A0FF)=="light violet-purple"&&Raster.ColorName(0xFFFFFF)=="white"&&Raster.ColorName(0x8B4513)=="brown"&&Raster.ColorName(0x808080)=="grey","every palette color gets a plain name");
            check(!PromptGuide.Build(new List<string>{"rendered"},export,config).Contains("MATERIAL ID COLOR BAN"),"no ID color rule when material_id is not sent");
            check(!PromptGuide.Build(new List<string>{"rendered"},export,config).Contains("#FF0000"),"material mapping is left out when material_id is not sent");
            var notes=new List<string>();check(PromptGuide.Select(export,new Config{AiChannels=PromptGuide.SceneChannels.ToList()},3,notes).Count==3&&notes.Count==1,"engine image limit is applied and reported");
            check(PromptGuide.Select(export,new Config(),14,null).Count==PromptGuide.DefaultChannels.Length,"default control images used when none were saved");
            string photo=Path.Combine(root,"style photo.jpg");
            using(var big=new System.Drawing.Bitmap(3000,2000))big.Save(photo,System.Drawing.Imaging.ImageFormat.Jpeg);
            var styled=new ExportResult{Directory=root,Files=PromptGuide.SceneChannels.ToDictionary(c=>c,c=>png),Prompt="#FF0000 = oak"};var styleNotes=new List<string>();
            var styleConfig=new Config{AiChannels=PromptGuide.SceneChannels.ToList(),AiReferences=new List<string>{photo,Path.Combine(root,"missing.jpg")}};
            StyleReferences.Prepare(styled,styleConfig,styleNotes);
            check(styled.Files.ContainsKey("style_reference_1")&&!styled.Files.ContainsKey("style_reference_2")&&styleNotes.Count==1&&styleNotes[0].Contains("missing.jpg"),"style photos are copied into the export and a missing one is reported");
            using(var copy=new System.Drawing.Bitmap(styled.Files["style_reference_1"]))check(copy.Width==1536&&copy.Height==1024,"large style photos are scaled down before sending");
            var withStyle=PromptGuide.Select(styled,styleConfig,4,null);
            check(withStyle.Count==4&&withStyle.Last().Key=="style_reference_1","a style reference keeps its place when the engine limit is tight");
            string styleGuide=PromptGuide.Build(withStyle.Select(x=>x.Key).ToList(),styled,styleConfig);
            check(styleGuide.Contains("Image 4 (style_reference_1): STYLE REFERENCE ONLY")&&styleGuide.Contains("STYLE TRANSFER RULE")&&styleGuide.Contains("Never copy their objects"),"prompt tells the engine to borrow the look and nothing else");
            check(!guide.Contains("STYLE TRANSFER RULE"),"no style rule without style photos");
            var styleVendor=new Vendor{Reply=google.Reply};
            Wait(async()=>{using(var client=new AiClient(styleVendor))return await client.Generate(AiProviders.Find("google"),new ProviderSettings(),"k","p",withStyle,1024,589,none);});
            check(JObject.Parse(styleVendor.Bodies[0]).SelectTokens("contents[0].parts[*].inlineData.mimeType").Select(x=>(string)x).SequenceEqual(new[]{"image/png","image/png","image/png","image/jpeg"}),"each image is sent with its real type");

            var gemini=AiProviders.Find("google");
            check(AiProviders.Frame(gemini,new ProviderSettings(),1024,589).SequenceEqual(new[]{16,9})&&AiProviders.Frame(gemini,new ProviderSettings{Aspect="4:5"},1024,589).SequenceEqual(new[]{4,5})&&AiProviders.Frame(AiProviders.Find("openai"),new ProviderSettings(),1024,589).SequenceEqual(new[]{3,2})&&AiProviders.Frame(AiProviders.Find("doubao"),new ProviderSettings(),1024,589)==null&&AiProviders.Frame(AiProviders.Find("doubao"),new ProviderSettings{Aspect="21:9"},1024,589).SequenceEqual(new[]{21,9})&&AiProviders.Frame(AiProviders.Find(AiProviders.Comfy),new ProviderSettings(),1024,589)==null,"the export frame follows the ratio the engine will really draw");
            using(var wide=new System.Drawing.Bitmap(2752,1536))using(var canvas=System.Drawing.Graphics.FromImage(wide))using(var memory=new MemoryStream())
            {
                canvas.Clear(System.Drawing.Color.Red);canvas.FillRectangle(System.Drawing.Brushes.Blue,11,0,2730,1536);wide.Save(memory,System.Drawing.Imaging.ImageFormat.Png);
                var fitted=new AiImage{Bytes=memory.ToArray()}.Fit(2048,1152);
                using(var result=new System.Drawing.Bitmap(new MemoryStream(fitted.Bytes)))check(result.Width==2731&&result.Height==1536&&result.GetPixel(1,700).B==255&&result.GetPixel(2729,700).B==255,"a rounded vendor canvas is trimmed evenly to the exported ratio");
                var same=new AiImage{Bytes=memory.ToArray()};var junk=new AiImage{Bytes=new byte[]{1,2,3}};check(ReferenceEquals(same.Fit(2752,1536),same)&&ReferenceEquals(junk.Fit(16,9),junk),"matching or unreadable results are left untouched");
            }
            check(AiProviders.ModelFrame("auto",1024,589).SequenceEqual(new[]{16,9})&&AiProviders.ModelFrame("auto",900,1200).SequenceEqual(new[]{3,4})&&AiProviders.ModelFrame("21:9",1024,589).SequenceEqual(new[]{21,9})&&AiProviders.ModelFrame("frame",1024,589)==null&&AiProviders.ModelFrame("",1024,589)==null&&AiProviders.ModelFrame("wide",1024,589)==null&&new Config().FrameRatio=="auto"&&AiProviders.ModelRatios.Length==10&&!AiProviders.ModelRatios.Contains("auto"),"the Frame ratio setting snaps exports to a ratio image models draw");
            using(var square=new System.Drawing.Bitmap(64,64))using(var memory=new MemoryStream()){square.Save(memory,System.Drawing.Imaging.ImageFormat.Png);var squareImage=new AiImage{Bytes=memory.ToArray()};check(ReferenceEquals(squareImage.Fit(2048,1152),squareImage),"a deliberately different output format is never cropped");}
            check(AiClient.NearestRatio(683,1024)=="2:3"&&AiClient.NearestRatio(1000,1000)=="1:1","nearest supported aspect ratio");

            // A 20 px square on a 64 px canvas: every outline must be a single pixel wide.
            var raster=new Raster(new Camera{Width=64,Height=64,Left=-1,Right=1,Bottom=-1,Top=1,Near=1,Far=20,Perspective=false});
            Func<double,double,Vertex> at=(x,y)=>new Vertex{X=x,Y=y,Z=5,NZ=1};
            raster.Draw(new Triangle{A=at(-0.3125,-0.3125),B=at(0.3125,-0.3125),C=at(0.3125,0.3125),ObjectId=1,LayerId=1,MaterialId=1,BaseColor=0x999999});
            raster.Draw(new Triangle{A=at(-0.3125,-0.3125),B=at(0.3125,0.3125),C=at(-0.3125,0.3125),ObjectId=1,LayerId=1,MaterialId=1,BaseColor=0x999999});
            var maps=raster.Save(Path.Combine(root,"raster"),new Dictionary<int,int>{{1,0xff0000}});
            using(var edges=new System.Drawing.Bitmap(maps["edges"]))using(var shape=new System.Drawing.Bitmap(maps["shape_lock"]))using(var mask=new System.Drawing.Bitmap(maps["mask"]))
            {
                int run=0,widest=0,dark=0,outside=0;
                for(int x=0;x<64;x++){if(edges.GetPixel(x,32).R>0){run++;widest=Math.Max(widest,run);}else run=0;if(shape.GetPixel(x,32).R<0x40)dark++;if(edges.GetPixel(x,32).R>0&&mask.GetPixel(x,32).R==0)outside++;}
                check(widest==1&&dark==2,"edges and shape_lock outlines are one pixel wide at 64 px");
                check(outside==0,"outline pixels stay on the object, so the silhouette is not enlarged");
            }
            check(Raster.LineWeight(1024,589)==1&&Raster.LineWeight(2048,1178)==1&&Raster.LineWeight(4096,2356)==3,"line weight grows only at very large sizes");
            var old=Newtonsoft.Json.JsonConvert.DeserializeObject<Config>("{\"LongEdge\":1024}");old.Migrate();var kept=Newtonsoft.Json.JsonConvert.DeserializeObject<Config>("{\"LongEdge\":1024,\"SettingsVersion\":1}");kept.Migrate();
            check(old.LongEdge==2048&&kept.LongEdge==1024&&new Config().LongEdge==2048,"old default size moves to 2048 once and a later manual 1024 is kept");

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
