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
        // Vendors round their canvas, for example 2752 x 1536 for 16:9. Trim the excess so the result overlays the Rhino frame exactly.
        public AiImage Fit(int width,int height)
        {
            try
            {
                using(var memory=new MemoryStream(Bytes))using(var source=new System.Drawing.Bitmap(memory))
                {
                    double wanted=width/(double)Math.Max(1,height),actual=source.Width/(double)source.Height;
                    // Only rounding differences are trimmed. A workflow that deliberately outputs another format is left alone.
                    if(Math.Abs(actual/wanted-1)<0.002||Math.Abs(actual/wanted-1)>0.05)return this;
                    int w=actual>wanted?(int)Math.Round(source.Height*wanted):source.Width,h=actual>wanted?source.Height:(int)Math.Round(source.Width/wanted);
                    var crop=new System.Drawing.Rectangle((source.Width-w)/2,(source.Height-h)/2,w,h);
                    using(var cut=source.Clone(crop,System.Drawing.Imaging.PixelFormat.Format24bppRgb))using(var output=new MemoryStream())
                    {
                        if(Extension==".jpg"){var codec=System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders().First(c=>c.FormatID==System.Drawing.Imaging.ImageFormat.Jpeg.Guid);using(var options=new System.Drawing.Imaging.EncoderParameters(1)){options.Param[0]=new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality,96L);cut.Save(output,codec,options);}}
                        else cut.Save(output,System.Drawing.Imaging.ImageFormat.Png);
                        return new AiImage{Bytes=output.ToArray()};
                    }
                }
            }
            catch(ArgumentException){return this;} // a format GDI+ cannot read, such as WebP: keep the vendor's file
        }
        public string Extension {get{var b=Bytes;return b!=null&&b.Length>3&&b[0]==0xff&&b[1]==0xd8?".jpg":b!=null&&b.Length>3&&b[0]=='R'&&b[1]=='I'&&b[2]=='F'?".webp":".png";}}
    }
    public static class AiProviders
    {
        // ComfyUI is an engine too: it queues the API workflow from Export settings instead of calling a vendor.
        public const string Comfy="comfyui";
        public static readonly AiProvider[] All=
        {
            new AiProvider{Id="google",Name="Google Gemini (Nano Banana)",BaseUrl="https://generativelanguage.googleapis.com/v1beta",KeyVariable="GEMINI_API_KEY",Models=new[]{"gemini-3-pro-image","gemini-3.1-flash-image","gemini-3.1-flash-lite-image","gemini-3-pro-image-preview","gemini-2.5-flash-image"},MaxImages=14}, // Nano Banana Pro first: the studio-quality one
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
            if(provider.Id=="google")return model.Contains("lite")?new[]{"auto","1K"}:model.Contains("gemini-3")?new[]{"auto","1K","2K","4K"}:new[]{"auto"}; // Flash Lite draws 1K only; Gemini 2.5 image is fixed near 1K
            if(provider.Id=="openai")return new[]{"auto","low","medium","high"}; // GPT Image sizes are fixed; this is its quality level
            if(provider.Id=="doubao")return model.Contains("seedream-4-0")?new[]{"auto","1K","2K","4K"}:new[]{"auto","2K","4K"};
            return new string[0];
        }
        // The frame the engine will really draw, so Rhino can export that exact ratio. Null means any ratio is accepted.
        public static int[] Frame(AiProvider provider,ProviderSettings settings,int width,int height)
        {
            if(provider.Id==Comfy)return null;string aspect=Pick(Aspects(provider),settings.Aspect);
            if(aspect=="auto")
            {
                if(provider.Id=="doubao")return null;
                aspect=provider.Id=="openai"?(width>height*1.15?"3:2":height>width*1.15?"2:3":"1:1"):AiClient.NearestRatio(width,height);
            }
            var parts=aspect.Split(':');return new[]{int.Parse(parts[0]),int.Parse(parts[1])};
        }
        // The Frame ratio setting: "frame" keeps the Rhino frame, "auto" snaps it to the nearest model ratio, "a:b" forces one.
        public static int[] ModelFrame(string choice,int width,int height)
        {
            choice=(choice??"").Trim();if(choice==""||choice=="frame")return null;
            if(choice=="auto")choice=AiClient.NearestRatio(width,height);
            var parts=choice.Split(':');int a,b;
            return parts.Length==2&&int.TryParse(parts[0],out a)&&int.TryParse(parts[1],out b)&&a>0&&b>0?new[]{a,b}:null;
        }
        public static readonly string[] ModelRatios=Ratios.Skip(1).ToArray();
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
            {"rendered","secondary surface-volume and curvature reference; do not copy its temporary CAD display colors, layer tints, white material, lighting or background"},
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
        // Image models tend to read the ID colors as paint. Keep both texts in step with sync-core.mjs.
        // Without an explicit edit framing, image models re-shoot the scene from a nicer angle. Shared with sync-core.mjs.
        public const string CameraLockStart="MANDATORY CAMERA AND COMPOSITION LOCK: this is an in-place EDIT of the Rhino view, not a new photograph. Use ";
        public const string CameraLockEnd=" as the base canvas and repaint its surfaces where they are. The output must overlay the control images pixel for pixel: identical camera position, height, tilt, rotation, lens, framing and crop, with every object, gap and edge at the same pixel location and the same size. Do not re-shoot from another angle, lower or raise the camera, zoom, re-center, rotate or tidy the arrangement, and do not complete, extend or crop the object differently. Do not add anything the geometry does not contain: no base, plinth, pedestal, extra thickness, supports, table, walls, props or people. Areas that are empty in the control images stay plain background or ground.";
        public const string CameraCheck="FINAL CAMERA CHECK BEFORE OUTPUT: overlay the result on shape_lock. If the outline, the object positions or the perspective do not coincide, the image is wrong: redo it from the locked camera.";
        public const string MaterialAssignment="MATERIAL ASSIGNMENT RULE: materials are assigned by region, never guessed from what an object usually is. For each line of the mapping, find the region of that named color in the material_id image, then give exactly those pixels the material written after the equals sign, and nothing else. Do not swap materials between regions, do not move a material to the object where it seems more typical, and do not give two regions the same look unless the mapping names the same material for both. The share of the frame given for each region tells you which region is which: the largest share is the largest surface in the image.";
        public const string MaterialIdBan="MANDATORY MATERIAL ID COLOR BAN: the flat colors in the material_id images are arbitrary index labels, like the numbers on a paint-by-number sheet. They carry ZERO information about the real color, hue, tint, paint, stain, dye, lighting or mood of any surface. The same label colors may also show through as CAD display tints in rendered, shape_lock or other technical images; there too they are labels, never appearance. It is strictly forbidden to reproduce, echo, tint toward or be influenced by any ID color anywhere in the final image, including floors, walls, furniture, backgrounds, reflections and light. A purple ID region mapped to plywood must look like natural plywood, never purple wood; a red ID region mapped to oak must look like natural oak, never red. The color of every surface comes only from the real-world appearance of its mapped target material, the written instructions and any style reference.";
        public const string MaterialIdCheck="FINAL MATERIAL COLOR CHECK BEFORE OUTPUT: for every material region compare its final hue with its ID color. If they resemble each other and the mapped material would not naturally have that hue, the image is wrong: repaint that region in the natural color of its mapped material. No saturated ID color may survive in the output.";
        public const string MaskPrefix="material_mask_";
        public static int MaskOrder(string key){int n;return int.TryParse(key.Substring(MaskPrefix.Length),out n)?n:int.MaxValue;}
        public static string MaskRole(string material){return "MATERIAL MASK for \""+material+"\": white pixels are exactly the surfaces that must be made of "+material+"; black pixels are everything else. It is a selection, never an image to draw: the output must show no white or black mask shapes";}
        public const string MaterialIdLabels="Each region of material_id also carries its target material written on it as a short text label; read that label to know what the region is made of. The labels are metadata: never draw any text, letters or label boxes in the output.";
        public static string Label(int index,string channel){return "Image "+(index+1)+" ("+channel+")";}
        // Images an engine receives, in prompt order. Background blend always sends its own fixed bundle.
        public static List<KeyValuePair<string,string>> Select(ExportResult export,Config config,int max,List<string> notes)
        {
            List<KeyValuePair<string,string>> images;
            if(config.ProductMode)images=ComfyClient.SelectForBatch(export.Files,config).Where(x=>!x.Key.StartsWith(StyleReferences.Prefix)).ToList();
            else
            {
                var wanted=config.AiChannels==null||config.AiChannels.Count==0?DefaultChannels:config.AiChannels.ToArray();
                images=SceneChannels.Where(c=>wanted.Contains(c)&&export.Files.ContainsKey(c)).Select(c=>new KeyValuePair<string,string>(c,export.Files[c])).ToList();
            }
            if(!config.ProductMode&&config.MaterialMasks&&export.MaskMaterials!=null)images.AddRange(export.MaskMaterials.Keys.OrderBy(MaskOrder).Where(export.Files.ContainsKey).Select(k=>new KeyValuePair<string,string>(k,export.Files[k])));
            if(images.Count==0)throw new InvalidOperationException("Tick at least one control image to send.");
            // Style references ride behind the control images and keep their place when the engine's limit is tight.
            var styles=export.Files.Where(x=>x.Key.StartsWith(StyleReferences.Prefix)).OrderBy(x=>x.Key).ToList();max=Math.Max(1,max-styles.Count);
            if(images.Count>max){if(notes!=null)notes.Add("This engine accepts "+max+" images. Left out: "+string.Join(", ",images.Skip(max).Select(x=>x.Key))+".");images=images.Take(max).ToList();}
            images.AddRange(styles);
            return images;
        }
        public static string Build(IList<string> channels,ExportResult export,Config config)
        {
            var lines=new List<string>();string brief=(config.AiPrompt??"").Trim();
            if(brief!="")lines.Add("CREATIVE BRIEF — the look of the finished image:\n"+brief+"\n");
            lines.Add("Use the supplied input images according to the exact roles below.");
            bool ids=channels.Any(c=>c.StartsWith("material_id")),scene=!channels.Contains("reference");
            if(scene)lines.Add(CameraLockStart+(channels.Contains("rendered")?"the rendered image":"the shape_lock image")+CameraLockEnd);
            if(ids)lines.Add(MaterialIdBan+(config.MaterialIdStyle!="flat"?" "+MaterialIdLabels:""));
            if(channels.Contains("reference"))lines.Add("MANDATORY REFERENCE APPEARANCE LOCK: reference alone controls the complete frame's color or monochrome mode, white balance, exposure, brightness, contrast, tonal range, colors and background. Never average, blend or transfer appearance from scale_lock, placement, masks, depth, normals, edges, shape_lock or material_id; they are technical data only.");
            if(channels.Contains("scale_lock"))lines.Add("MANDATORY CLEAN FINAL OUTPUT: return a natural finished photograph only. The scale_lock overlay is invisible metadata. Do not copy, retain, stylize, recolor or redraw any magenta/pink/purple silhouette, rectangle, center crosshair, guide line, marker, diagram, label or measurement graphic. Restore clean reference/placement pixels behind every guide mark while keeping the actual object.");
            for(int i=0;i<channels.Count;i++){string role;if(channels[i].StartsWith(StyleReferences.Prefix)){lines.Add(Label(i,channels[i])+": "+StyleReferences.Role+".");continue;}
                if(channels[i].StartsWith(MaskPrefix)){string material;lines.Add(Label(i,channels[i])+": "+MaskRole(export.MaskMaterials!=null&&export.MaskMaterials.TryGetValue(channels[i],out material)?material:"the material named in the mapping")+".");continue;}lines.Add(Label(i,channels[i])+": "+(Roles.TryGetValue(channels[i],out role)?role:"additional visual reference; use only for the information visibly encoded in this image")+".");}
            if(channels.Contains("reference"))
            {
                lines.Add("The reference image is the mandatory base canvas for the final output. Return an edited version of that same scene, keep its framing and background, and integrate the object at the placement shown; never return an isolated object on a new background.");
                lines.Add("Global-versus-detail rule: reference and placement control the full-frame composition, final object location and real scale in the scene. The *_detail images are magnified digital crops rendered from the IDENTICAL camera projection, lens, orientation and perspective rays; they are not alternate viewpoints. Use them only for fine geometry, curvature, openings, seams and material interfaces. Never infer a new camera, change perspective, enlarge the object or move it in the final full-frame image. The inpaint mask is only a permissible editing neighborhood; its white area is never a scale, shape or bounding-box reference. Geometry conflict priority: full-frame placement for scale/location, then shape_lock_detail or shape_lock > silhouette and edges > masks > depth and normal > rendered. Material ID images govern material regions only and must never alter geometry.");
            }
            else lines.Add("STANDARD RHINO SCENE MODE: preserve the exact full-frame camera, composition, object count, silhouettes, openings, overlaps and relative scale shown by the control images. Use depth, edges, silhouette, normal and mask only as coordinated geometry evidence. Use material_id only to assign the requested materials to its flat-color regions. The technical control images must never appear in the result.");
            if(channels.Any(c=>c.StartsWith(StyleReferences.Prefix)))lines.Add(StyleReferences.Rule+(channels.Contains("reference")?StyleReferences.BlendRule:""));
            if(!string.IsNullOrWhiteSpace(export.PlacementConstraint))lines.Add(export.PlacementConstraint.Trim());
            if(!string.IsNullOrWhiteSpace(export.Prompt)&&channels.Any(c=>c.StartsWith("material_id"))){lines.Add("Material mapping for the material_id image. Each hex value is only a lookup key that identifies a region; it is never a color to paint:\n"+export.Prompt.Trim());lines.Add(MaterialAssignment);}
            string instruction=config.ProductMode?(config.WearInstructions??"").Trim():"";
            if(instruction!="")lines.Add("Rhino user instruction — follow this requested edit exactly:\n"+instruction);
            if(channels.Contains("scale_lock"))lines.Add("FINAL VALIDATION BEFORE OUTPUT: inspect the completed image and remove every technical overlay originating from scale_lock. Compare every pixel outside the object edit region against reference and restore its original brightness, contrast, tonal range and color exactly.");
            if(ids)lines.Add(MaterialIdCheck);
            if(scene)lines.Add(CameraCheck);
            lines.Add("Return exactly one finished image with the same framing and aspect ratio as the inputs.");
            return string.Join("\n",lines);
        }
    }
    // Copies the user's style photos into the export as bounded JPEGs, so every engine gets a small, upright image.
    public static class StyleReferences
    {
        public const string Prefix="style_reference_";
        public const int Max=4,LongEdge=1536;
        // The same wording is used by imageGuide in comfyui/rhino_ai_live/web/sync-core.mjs.
        public const string Role="STYLE REFERENCE ONLY, never a geometry or composition reference; a real photograph chosen by the user for its look";
        public const string Rule="STYLE TRANSFER RULE: study the style_reference photographs and extract only their photographic qualities: light quality, direction and softness, exposure, dynamic range, white balance, color grading, contrast, material realism and surface imperfection, reflections, atmosphere, depth of field, lens character and film or sensor grain. Render the Rhino scene with those qualities so the result reads as a real photograph of the same kind, not a CG image. Never copy their objects, architecture, furniture, people, text, composition, framing, backdrop, plinth or supports, camera height or camera angle. Geometry, camera, object count and material regions come only from the Rhino control images, and the material mapping still decides what each region is made of.";
        public const string BlendRule=" In this background blend the reference photograph still controls the appearance of the whole frame; use the style references only for the realism of the inserted object's materials.";
        public static string Mime(string path){string e=Path.GetExtension(path).ToLowerInvariant();return e==".jpg"||e==".jpeg"?"image/jpeg":e==".webp"?"image/webp":"image/png";}
        public static void Prepare(ExportResult export,Config config,List<string> notes)
        {
            int index=0;
            foreach(var source in (config.AiReferences??new List<string>()).Where(x=>!string.IsNullOrWhiteSpace(x)).Take(Max))
            {
                try
                {
                    using(var image=System.Drawing.Image.FromFile(source))
                    {
                        // Phone photos carry their rotation in EXIF.
                        if(image.PropertyIdList.Contains(0x0112))
                        {
                            int o=BitConverter.ToUInt16(image.GetPropertyItem(0x0112).Value,0);
                            var flip=o==3?System.Drawing.RotateFlipType.Rotate180FlipNone:o==6?System.Drawing.RotateFlipType.Rotate90FlipNone:o==8?System.Drawing.RotateFlipType.Rotate270FlipNone:System.Drawing.RotateFlipType.RotateNoneFlipNone;
                            if(flip!=System.Drawing.RotateFlipType.RotateNoneFlipNone)image.RotateFlip(flip);
                        }
                        double scale=Math.Min(1,LongEdge/(double)Math.Max(image.Width,image.Height));int w=Math.Max(1,(int)Math.Round(image.Width*scale)),h=Math.Max(1,(int)Math.Round(image.Height*scale));
                        string key=Prefix+(++index),path=Path.Combine(export.Directory,key+".jpg");
                        using(var copy=new System.Drawing.Bitmap(w,h,System.Drawing.Imaging.PixelFormat.Format24bppRgb))
                        {
                            using(var g=System.Drawing.Graphics.FromImage(copy)){g.Clear(System.Drawing.Color.White);g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;g.DrawImage(image,0,0,w,h);}
                            var codec=System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders().First(c=>c.FormatID==System.Drawing.Imaging.ImageFormat.Jpeg.Guid);
                            using(var options=new System.Drawing.Imaging.EncoderParameters(1)){options.Param[0]=new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality,92L);copy.Save(path,codec,options);}
                        }
                        export.Files[key]=path;
                    }
                }
                catch(Exception e){if(notes!=null)notes.Add("Style reference skipped: "+source+" ("+(e is FileNotFoundException||e is OutOfMemoryException?"missing or not a JPG, PNG or BMP image":e.Message)+")");}
            }
        }
    }
    // Measures a result against the ID map: a region whose average hue matches its ID color, while its
    // material would not naturally have that hue, is a leak.
    public static class LeakCheck
    {
        public static List<string> Inspect(byte[] image,ExportResult export)
        {
            var found=new List<string>();
            if(image==null||export==null||export.Regions==null||export.RegionColors==null||export.RegionWidth<1||export.RegionHeight<1)return found;
            try
            {
                using(var memory=new MemoryStream(image))using(var bitmap=new System.Drawing.Bitmap(memory))
                {
                    int w=bitmap.Width,h=bitmap.Height,rw=export.RegionWidth,rh=export.RegionHeight;var sums=new Dictionary<int,double[]>();long total=0;
                    var data=bitmap.LockBits(new System.Drawing.Rectangle(0,0,w,h),System.Drawing.Imaging.ImageLockMode.ReadOnly,System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                    try
                    {
                        var row=new byte[data.Stride];int step=Math.Max(1,w/640);
                        for(int y=0;y<h;y+=step)
                        {
                            System.Runtime.InteropServices.Marshal.Copy(IntPtr.Add(data.Scan0,y*data.Stride),row,0,data.Stride);int ry=Math.Min(rh-1,(int)((long)y*rh/h));
                            for(int x=0;x<w;x+=step){int region=export.Regions[ry*rw+Math.Min(rw-1,(int)((long)x*rw/w))];if(region==0)continue;double[] s;if(!sums.TryGetValue(region,out s))sums[region]=s=new double[4];s[0]+=row[x*3+2];s[1]+=row[x*3+1];s[2]+=row[x*3];s[3]++;total++;}
                        }
                    }
                    finally{bitmap.UnlockBits(data);}
                    foreach(var pair in sums.OrderByDescending(p=>p.Value[3]))
                    {
                        int idColor;string material;if(pair.Value[3]<total*0.005||!export.RegionColors.TryGetValue(pair.Key,out idColor))continue;
                        if(export.RegionMaterials==null||!export.RegionMaterials.TryGetValue(pair.Key,out material))material="";
                        double c=pair.Value[3];int mean=Raster.Rgb((int)(pair.Value[0]/c),(int)(pair.Value[1]/c),(int)(pair.Value[2]/c));
                        double hm,sm,vm,hi,si,vi;Hsv(mean,out hm,out sm,out vm);Hsv(idColor,out hi,out si,out vi);
                        double hueGap=Math.Abs(hm-hi);hueGap=Math.Min(hueGap,360-hueGap);
                        if(sm>0.25&&si>0.3&&vm>0.2&&hueGap<28&&!Allows(material,Raster.ColorName(idColor)))found.Add("the \""+material+"\" region came out "+Raster.ColorName(mean)+", like its "+Raster.ColorName(idColor)+" ID color");
                    }
                }
            }
            catch(Exception){}
            return found;
        }
        static readonly Dictionary<string,string[]> Synonyms=new Dictionary<string,string[]>{{"purple",new[]{"violet","lavender","lilac","mauve","plum"}},{"red",new[]{"crimson","scarlet","burgundy","maroon","brick"}},{"blue",new[]{"navy","azure","cobalt","indigo","denim"}},{"green",new[]{"olive","emerald","moss","sage","jade"}},{"yellow",new[]{"gold","lemon","mustard","brass"}},{"orange",new[]{"copper","amber","rust","terracotta"}},{"cyan",new[]{"turquoise","aqua","teal"}},{"magenta",new[]{"fuchsia","pink","rose"}},{"pink",new[]{"rose","blush","salmon","coral"}},{"brown",new[]{"walnut","oak","bronze","leather","wood","timber","chocolate","tan"}}};
        // A material that is naturally that color, such as "red brick" on a red ID, is not a leak.
        public static bool Allows(string material,string colorName)
        {
            string text=(material??"").ToLowerInvariant();
            foreach(var word in (colorName??"").ToLowerInvariant().Split(' ','-'))
            {
                if(word.Length<3||word=="light")continue;if(text.Contains(word))return true;
                string[] more;if(Synonyms.TryGetValue(word,out more)&&more.Any(text.Contains))return true;
            }
            return false;
        }
        static void Hsv(int rgb,out double h,out double s,out double v)
        {
            double r=((rgb>>16)&255)/255.0,g=((rgb>>8)&255)/255.0,b=(rgb&255)/255.0,max=Math.Max(r,Math.Max(g,b)),min=Math.Min(r,Math.Min(g,b)),delta=max-min;
            v=max;s=max<=0?0:delta/max;h=delta<1e-6?0:max==r?60*(((g-b)/delta)%6):max==g?60*((b-r)/delta+2):60*((r-g)/delta+4);if(h<0)h+=360;
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
            // Never ask for a size the model cannot draw: the largest it offers instead.
            var sizes=AiProviders.Resolutions(provider,model).Where(x=>x!="auto").ToArray();if(resolution!="auto"&&sizes.Length>0&&!sizes.Contains(resolution))resolution=sizes.Last();
            HttpRequestMessage request;
            if(provider.Id=="google")
            {
                var parts=new JArray{new JObject{["text"]=prompt}};
                for(int i=0;i<images.Count;i++){parts.Add(new JObject{["text"]=PromptGuide.Label(i,images[i].Key)+":"});parts.Add(new JObject{["inlineData"]=new JObject{["mimeType"]=StyleReferences.Mime(images[i].Value),["data"]=Convert.ToBase64String(File.ReadAllBytes(images[i].Value))}});}
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
                foreach(var item in images){var bytes=new ByteArrayContent(File.ReadAllBytes(item.Value));bytes.Headers.ContentType=new System.Net.Http.Headers.MediaTypeHeaderValue(StyleReferences.Mime(item.Value));form.Add(bytes,"image[]",item.Key+Path.GetExtension(item.Value).ToLowerInvariant());}
                request=new HttpRequestMessage(HttpMethod.Post,root+"/images/edits"){Content=form};
                request.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",key.Trim());
            }
            else if(provider.Id=="doubao")
            {
                var body=new JObject{["model"]=model,["prompt"]=prompt,["image"]=new JArray(images.Select(x=>"data:"+StyleReferences.Mime(x.Value)+";base64,"+Convert.ToBase64String(File.ReadAllBytes(x.Value)))),["size"]=AiProviders.PixelSize(AiProviders.Resolutions(provider,model).Contains(resolution)?resolution:"2K",aspect=="auto"?width/(double)Math.Max(1,height):double.Parse(aspect.Split(':')[0])/double.Parse(aspect.Split(':')[1])),["sequential_image_generation"]="disabled",["response_format"]="b64_json",["watermark"]=false};
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
