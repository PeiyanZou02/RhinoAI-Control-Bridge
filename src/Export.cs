using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace RhinoAI
{
    public sealed class LayerRule
    {
        public string Id,Name,Color,Material,Description; // Description remains only for old settings compatibility.
        public int Index;
    }
    public sealed class Config
    {
        public string Server="http://127.0.0.1:8000";
        public string Output=Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments),"RhinoAI Exports");
        public string Workflow="";
        public string TargetWorkflow=""; // empty = follow the workflow currently shown in ComfyUI
        public string LastTargetRequest="",LastTargetRequestFor="";
        public bool ProductMode=false,SelectedOnly=false;
        public bool MatchWallpaperAspect=true;
        public bool LockSceneAspect=true;
        public int SceneAspectWidth=1024,SceneAspectHeight=589;
        public bool DetailPriority=true;
        public bool AutoEditRegion=true;
        public bool AutoSync=false;
        public string WearInstructions="Blend the Rhino objects into the background photograph at the exact projected location, scale and orientation. Match the photograph's lighting direction, perspective, color and grain. Add natural contact shadows and reflections. Let foreground elements of the photograph occlude the objects where physically appropriate. Keep everything outside the edit region unchanged.";
        public int MaskPadding=12;
        public string OcclusionMask="";
        public int LongEdge=1024;
        public string Style="Photorealistic visualization, physically plausible materials, soft natural lighting, accurate scale, balanced exposure, fine surface detail. Preserve the supplied design.";
        public List<LayerRule> Layers=new List<LayerRule>();
        // AI render page. Lists start null because Json.NET appends saved items to a non-empty default.
        public string AiEngine="google";
        public string AiOutput=Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments),"RhinoAI Renders");
        public string AiPrompt="Photorealistic visualization, physically plausible materials, soft natural lighting, accurate scale, balanced exposure, fine surface detail. Preserve the supplied design.";
        public List<string> AiChannels,AiViews,AiReferences; // AiReferences: local photos whose look the render should borrow
        public Dictionary<string,ProviderSettings> AiProviders=new Dictionary<string,ProviderSettings>();
        public ProviderSettings Provider(string id){ProviderSettings found;if(!AiProviders.TryGetValue(id,out found)){found=new ProviderSettings();AiProviders[id]=found;}return found;}
        public static string Root {get{var location=typeof(Config).Assembly.Location;return !string.IsNullOrEmpty(location)?Path.GetDirectoryName(location):(System.Environment.GetEnvironmentVariable("RHINO_AI_HOME")??AppDomain.CurrentDomain.BaseDirectory);}}
        public static string PathName {get{return Path.Combine(Root,"settings.json");}}
        public static Config Load(){return File.Exists(PathName)?JsonConvert.DeserializeObject<Config>(File.ReadAllText(PathName,Encoding.UTF8)):new Config();}
        public void Save(){File.WriteAllText(PathName,JsonConvert.SerializeObject(this,Formatting.Indented),Encoding.UTF8);}
        public void Scan(RhinoDoc doc)
        {
            foreach(var layer in doc.Layers.Where(l=>!l.IsDeleted))
            {
                var rule=Layers.FirstOrDefault(l=>l.Id==layer.Id.ToString());
                if(rule==null){rule=MaterialRules.Guess(layer);Layers.Add(rule);}
                rule.Index=layer.Index;rule.Name=layer.FullPath;rule.Color=Raster.Hex(layer.Color.ToArgb()&0xffffff);
            }
            var ids=new HashSet<string>(doc.Layers.Where(l=>!l.IsDeleted).Select(l=>l.Id.ToString()));
            Layers=Layers.Where(l=>ids.Contains(l.Id)).ToList();
        }
    }
    public static class MaterialRules
    {
        public static LayerRule Guess(Layer layer)
        {
            string name=layer.FullPath.ToLowerInvariant();string mat="neutral matte material",desc="Neutral warm-grey matte finish, fine uniform texture, low specular reflection; replace this suggestion with the intended material.";
            if(Has(name,"ceramic","porcelain","陶瓷","瓷")){mat="glazed ceramic";desc="Warm ivory glazed ceramic, smooth continuous glaze with subtle handmade surface variation, soft broad highlights, roughness 0.15–0.30; preserve the original sculpted form, edge thickness and openings.";}
            else if(Has(name,"gold","黄金","金色","18k")){mat="18k yellow gold";desc="Polished 18k yellow gold, warm rich gold tone, metallic reflectance, roughness 0.12–0.22, finely controlled highlights and subtle micro-scratches at realistic scale; preserve fine details, edges and engraving.";}
            else if(Has(name,"diamond","gem","钻石","宝石")){mat="faceted gemstone";desc="Clear faceted gemstone with crisp facet boundaries, physically plausible refraction and restrained spectral dispersion, high optical clarity; preserve the exact stone count, cut, size and setting. Do not add extra gemstones.";}
            else if(Has(name,"silver","银","platinum","铂")){mat="polished silver metal";desc="Cool polished silver-colored precious metal, high metallic reflectance, roughness 0.10–0.22, realistic studio reflections, subtle micro-scratches and crisp edges; no plastic appearance.";}
            else if(Has(name,"glass","glazing","玻璃")){mat="clear glass";desc="Clear low-iron glass, subtle edge tint, smooth surface, roughness 0.03–0.10, physically plausible transparency and Fresnel reflections; preserve the exact boundaries and thickness.";}
            else if(Has(name,"wood","timber","木")){mat="natural oak";desc="Natural light oak, warm beige-brown tone, fine longitudinal grain at realistic scale, satin clear finish, roughness 0.40–0.55; align grain with each panel's long direction.";}
            else if(Has(name,"metal","alum","steel","金属","铝","钢","窗框","frame")){mat="satin silver metal";desc="Neutral silver satin metal, fine uniform surface grain, metallic reflectance, roughness 0.25–0.40, balanced highlights and crisp edges; preserve the source shape and scale. Adjust this suggested finish to match the intended metal.";}
            else if(Has(name,"concrete","混凝土","水泥","墙","wall")){mat="off-white microcement";desc="Warm off-white microcement, seamless fine mineral texture, faint tonal variation, matte roughness 0.70–0.85, diffuse reflection, no oversized cracks or repetitive texture.";}
            else if(Has(name,"stone","marble","石","大理石")){mat="light limestone";desc="Warm ivory honed limestone, subtle natural mineral variation and sparse fine pores, roughness 0.50–0.70, restrained reflectivity, realistic slab scale and carefully aligned joints.";}
            else if(Has(name,"plant","grass","tree","绿植","草","树")){mat="vegetation";desc="Natural muted green vegetation with subtle hue variation, fine leaf detail and soft light transmission, realistic leaf scale, no artificial plastic shine.";}
            else if(Has(name,"fabric","cloth","布","软包")){mat="linen fabric";desc="Warm neutral woven linen, fine visible weave at real scale, matte roughness 0.85–0.95, soft diffuse reflection, gentle fabric variation.";}
            return new LayerRule{Id=layer.Id.ToString(),Index=layer.Index,Name=layer.FullPath,Color="",Material=mat,Description=desc};
        }
        static bool Has(string name,params string[] keys){return keys.Any(name.Contains);}
        public static string Prompt(IEnumerable<LayerRule> layers,string style)
        {
            var sb=new StringBuilder();
            foreach(var layer in layers)
            {
                if(!string.IsNullOrWhiteSpace(layer.Material))sb.AppendFormat("{0} = {1}\n",layer.Color.ToUpperInvariant(),layer.Material.Trim());
            }
            return sb.ToString().TrimEnd();
        }
    }
    public sealed class Snapshot
    {
        public Camera Camera;
        public List<Triangle> Triangles=new List<Triangle>();
        public List<object> Objects=new List<object>();
        public List<string> Warnings=new List<string>();
        public Dictionary<int,int> LayerColors=new Dictionary<int,int>();
        public Dictionary<int,string> LayerTargetMaterials=new Dictionary<int,string>();
        public object View;
        public byte[] Wallpaper,Reference,Rendered;
        public string WallpaperPath;
    }
    public sealed class ExportResult
    {
        public string Directory,Prompt,WearPrompt,PlacementConstraint;
        public Dictionary<string,string> Files;
        public List<string> Warnings;
    }
    internal sealed class CanvasLayout
    {
        public int CaptureWidth,CaptureHeight,OutputWidth,OutputHeight;
        public Rectangle Crop;
    }
    public static class Exporter
    {
        public static Snapshot Capture(RhinoDoc doc,Config config)
        {
            var view=doc.Views.ActiveView;if(view==null)throw new InvalidOperationException("Activate a model viewport first.");
            if(view is RhinoPageView)throw new InvalidOperationException("Switch to a model-space viewport before exporting.");
            var vp=view.ActiveViewport;var size=vp.Size;double l,r,b,t,n,f;
            if(config.ProductMode && (!vp.WallpaperVisible || string.IsNullOrEmpty(vp.WallpaperFilename)))throw new InvalidOperationException("Background blend needs a visible Wallpaper in the current viewport. Place the photo with the Wallpaper command and align the objects first.");
            if(!vp.GetFrustum(out l,out r,out b,out t,out n,out f))throw new InvalidOperationException("Cannot read the camera frustum.");
            int longest=Math.Max(size.Width,size.Height),target=Math.Max(256,Math.Min(4096,config.LongEdge));
            int defaultW=Math.Max(1,(int)Math.Round(size.Width*(double)target/longest)),defaultH=Math.Max(1,(int)Math.Round(size.Height*(double)target/longest));
            var layout=new CanvasLayout{CaptureWidth=defaultW,CaptureHeight=defaultH,OutputWidth=defaultW,OutputHeight=defaultH,Crop=new Rectangle(0,0,defaultW,defaultH)};
            if(!config.ProductMode&&config.LockSceneAspect)layout=WallpaperLayout(size.Width,size.Height,Math.Max(1,config.SceneAspectWidth),Math.Max(1,config.SceneAspectHeight),target);
            int wallpaperWidth=0,wallpaperHeight=0;
            if(config.ProductMode&&config.MatchWallpaperAspect)
            {
                try
                {
                    using(var image=Image.FromFile(vp.WallpaperFilename))
                    {
                        wallpaperWidth=image.Width;wallpaperHeight=image.Height;
                        try
                        {
                            if(image.PropertyIdList.Contains(0x0112))
                            {
                                int orientation=BitConverter.ToUInt16(image.GetPropertyItem(0x0112).Value,0);
                                if(orientation>=5&&orientation<=8){int swap=wallpaperWidth;wallpaperWidth=wallpaperHeight;wallpaperHeight=swap;}
                            }
                        }catch(ArgumentException){}
                    }
                    layout=WallpaperLayout(size.Width,size.Height,wallpaperWidth,wallpaperHeight,target);
                }
                catch(Exception e){RhinoApp.WriteLine("Could not read the Wallpaper aspect ratio. Using the viewport ratio: "+e.Message);}
            }
            int w=layout.OutputWidth,h=layout.OutputHeight;
            config.Scan(doc);
            var captureCamera=new Camera{Width=layout.CaptureWidth,Height=layout.CaptureHeight,Left=l,Right=r,Bottom=b,Top=t,Near=n,Far=f,Perspective=vp.IsPerspectiveProjection};
            var s=new Snapshot{Camera=ProductExport.CropCamera(captureCamera,layout.Crop,w,h)};
            s.View=new {name=vp.Name,camera=vp.CameraLocation,target=vp.CameraTarget,up=vp.CameraUp,units=doc.ModelUnitSystem.ToString(),viewportWidth=size.Width,viewportHeight=size.Height,captureWidth=layout.CaptureWidth,captureHeight=layout.CaptureHeight,outputWidth=w,outputHeight=h,sceneAspectLocked=!config.ProductMode&&config.LockSceneAspect,wallpaperWidth=wallpaperWidth,wallpaperHeight=wallpaperHeight,wallpaperCropPixels=new[]{layout.Crop.Left,layout.Crop.Top,layout.Crop.Right,layout.Crop.Bottom},wallpaperAspectMatched=config.ProductMode&&config.MatchWallpaperAspect&&wallpaperWidth>0};
            foreach(var rule in config.Layers){s.LayerColors[rule.Index+1]=Convert.ToInt32(rule.Color.TrimStart('#'),16);s.LayerTargetMaterials[rule.Index+1]=rule.Material;}
            var transform=vp.GetTransform(CoordinateSystem.World,CoordinateSystem.Camera);
            var objects=doc.Objects.GetObjectList(new ObjectEnumeratorSettings{NormalObjects=true,LockedObjects=true,HiddenObjects=false,ReferenceObjects=true});
            var materials=new Dictionary<string,int>();int id=0;
            var selected=new HashSet<Guid>(doc.Objects.GetSelectedObjects(false,false).Select(o=>o.Id));
            if(config.SelectedOnly && selected.Count==0)throw new InvalidOperationException("Select the objects to export, or turn off Export selected objects only.");
            s.Rendered=CropPng(CaptureRendered(view,vp.Id,layout.CaptureWidth,layout.CaptureHeight,config.SelectedOnly?selected:null),layout.Crop,w,h);
            foreach(var obj in objects)
            {
                if(!obj.Visible || !VisibleLayer(doc,obj.Attributes.LayerIndex))continue;
                var clip=obj.Geometry as ClippingPlaneSurface;
                if(clip!=null && clip.ViewportIds().Contains(vp.Id))throw new InvalidOperationException("A clipping plane is active in this viewport. Clipping is not supported yet. Disable it before exporting to avoid wrong control images.");
                if(config.SelectedOnly && !selected.Contains(obj.Id))continue;
                AddObject(doc,obj,Transform.Identity,transform,null,s,materials,ref id,0);
            }
            if(s.Triangles.Count==0)throw new InvalidOperationException("No surfaces to export. Supported: Brep, Extrusion, Mesh, SubD and block instances.");
            s.Warnings.Add("Control images treat transparent surfaces as opaque. basecolor is the flat material color without textures, lighting or PBR channels. Curves, points, annotations and unbaked Grasshopper previews are not included.");
            s.Warnings.Add("Material ID follows Rhino layers strictly: one color per layer. Per-face material overrides are not detected.");
            s.Warnings.Add("Normals are in camera space: R=right, G=up, B=toward camera. Edges come from occlusion, object boundaries, depth and normal changes, not Make2D or Canny.");
            if(!config.ProductMode&&config.LockSceneAspect)s.Warnings.Add("Standard scene locked to the widescreen frame "+config.SceneAspectWidth+" × "+config.SceneAspectHeight+". Window and sidebar size no longer change the output, and all control images share one camera sub-frustum.");
            if(config.ProductMode)
            {
                s.WallpaperPath=vp.WallpaperFilename;
                var capture=new ViewCapture{Width=layout.CaptureWidth,Height=layout.CaptureHeight,DrawAxes=false,DrawGrid=false,DrawGridAxes=false,TransparentBackground=false,ScaleScreenItems=false};
                using(var suppress=new BackgroundOnly(vp.Id))
                {
                    suppress.Enabled=true;
                    try{using(var bitmap=capture.CaptureToBitmap(view))using(var memory=new MemoryStream()){if(bitmap==null)throw new InvalidOperationException("Wallpaper capture failed.");bitmap.Save(memory,ImageFormat.Png);s.Wallpaper=CropPng(memory.ToArray(),layout.Crop,w,h);}}
                    finally{suppress.Enabled=false;}
                }
                if(config.MatchWallpaperAspect&&wallpaperWidth>0)s.Warnings.Add("Wallpaper exported at its original ratio: "+w+" × "+h+". Viewport margins were cropped and all geometry images share the same sub-frustum, so lens and perspective are unchanged.");
                s.Warnings.Add("Foreground occlusion cannot be derived from a single Wallpaper. inpaint_mask is only an expansion of the object mask. Supply an occlusion mask at export size, white for protected foreground, and check the front-to-back order.");
            }
            return s;
        }
        internal static CanvasLayout WallpaperLayout(int viewportWidth,int viewportHeight,int imageWidth,int imageHeight,int longEdge)
        {
            if(viewportWidth<1||viewportHeight<1||imageWidth<1||imageHeight<1)throw new ArgumentOutOfRangeException("Wallpaper and viewport sizes must be positive.");
            int target=Math.Max(256,Math.Min(4096,longEdge));double imageAspect=imageWidth/(double)imageHeight,viewportAspect=viewportWidth/(double)viewportHeight;
            int outputWidth=imageAspect>=1?target:Math.Max(1,(int)Math.Round(target*imageAspect));
            int outputHeight=imageAspect>=1?Math.Max(1,(int)Math.Round(target/imageAspect)):target;
            int captureWidth,captureHeight,left=0,top=0;
            if(imageAspect<=viewportAspect)
            {
                captureHeight=outputHeight;captureWidth=Math.Max(outputWidth,(int)Math.Round(captureHeight*viewportAspect));left=(captureWidth-outputWidth)/2;
            }
            else
            {
                captureWidth=outputWidth;captureHeight=Math.Max(outputHeight,(int)Math.Round(captureWidth/viewportAspect));top=(captureHeight-outputHeight)/2;
            }
            return new CanvasLayout{CaptureWidth=captureWidth,CaptureHeight=captureHeight,OutputWidth=outputWidth,OutputHeight=outputHeight,Crop=new Rectangle(left,top,outputWidth,outputHeight)};
        }
        static byte[] CropPng(byte[] png,Rectangle crop,int width,int height)
        {
            if(crop.Left==0&&crop.Top==0&&crop.Width==width&&crop.Height==height)return png;
            using(var input=new MemoryStream(png))using(var source=new Bitmap(input))using(var output=new Bitmap(width,height,PixelFormat.Format32bppArgb))
            {
                using(var g=Graphics.FromImage(output))
                {
                    g.CompositingMode=CompositingMode.SourceCopy;g.CompositingQuality=CompositingQuality.HighQuality;g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
                    g.DrawImage(source,new Rectangle(0,0,width,height),crop,GraphicsUnit.Pixel);
                }
                using(var memory=new MemoryStream()){output.Save(memory,ImageFormat.Png);return memory.ToArray();}
            }
        }
        static bool VisibleLayer(RhinoDoc doc,int idx)
        {
            Layer layer=doc.Layers[idx];while(layer!=null){if(!layer.IsVisible)return false;if(layer.ParentLayerId==Guid.Empty)break;layer=doc.Layers.FindId(layer.ParentLayerId);}return true;
        }
        static void AddObject(RhinoDoc doc,RhinoObject obj,Transform world,Transform camera,Material inherited,Snapshot s,Dictionary<string,int> materials,ref int id,int nesting)
        {
            if(nesting>32)throw new InvalidOperationException("Block instances are nested deeper than 32 levels.");
            if(obj.IsHidden || !VisibleLayer(doc,obj.Attributes.LayerIndex))return;
            Material mat=obj.Attributes.MaterialSource==ObjectMaterialSource.MaterialFromParent && inherited!=null?inherited:obj.GetMaterial(true);
            var instance=obj as InstanceObject;
            if(instance!=null){foreach(var child in instance.InstanceDefinition.GetObjects())AddObject(doc,child,world*instance.InstanceXform,camera,mat,s,materials,ref id,nesting+1);return;}
            var meshes=new List<Mesh>();var geometry=obj.Geometry;
            var mesh=geometry as Mesh;var brep=geometry as Brep;var ext=geometry as Extrusion;var subd=geometry as SubD;
            if(mesh!=null)meshes.Add(mesh.DuplicateMesh());
            else if(brep!=null)meshes.AddRange(Mesh.CreateFromBrep(brep,MeshingParameters.QualityRenderMesh)??new Mesh[0]);
            else if(ext!=null){using(var eb=ext.ToBrep())meshes.AddRange(Mesh.CreateFromBrep(eb,MeshingParameters.QualityRenderMesh)??new Mesh[0]);}
            else if(subd!=null){var sm=Mesh.CreateFromSubD(subd,3);if(sm!=null)meshes.Add(sm);}
            else return;
            if(meshes.Count==0){s.Warnings.Add("Could not mesh object: "+obj.Id);return;}
            int objectId=++id,layerId=obj.Attributes.LayerIndex+1;string key=mat==null?"default":mat.Id.ToString()+":"+mat.DiffuseColor.ToArgb();
            if(!materials.ContainsKey(key))materials[key]=materials.Count+1;
            int materialId=layerId;
            string targetMaterial;s.LayerTargetMaterials.TryGetValue(layerId,out targetMaterial);
            int materialColor;if(!s.LayerColors.TryGetValue(layerId,out materialColor))materialColor=Raster.Palette(materialId);
            s.Objects.Add(new {id=objectId,rhinoId=obj.Id,layerId=layerId,materialId=materialId,materialName=string.IsNullOrWhiteSpace(targetMaterial)?(mat==null?"Default":mat.Name):targetMaterial,objectColor=Raster.Hex(Raster.Palette(objectId)),materialColor=Raster.Hex(materialColor)});
            foreach(var m in meshes)using(m)
            {
                m.Transform(world);m.Normals.ComputeNormals();m.Transform(camera);m.Normals.ComputeNormals();
                var vertices=new Vertex[m.Vertices.Count];
                for(int i=0;i<vertices.Length;i++){var p=m.Vertices[i];var normal=m.Normals[i];vertices[i]=new Vertex{X=p.X,Y=p.Y,Z=-p.Z,NX=normal.X,NY=normal.Y,NZ=normal.Z};}
                int color=mat==null?0xb0b0b0:mat.DiffuseColor.ToArgb()&0xffffff;
                foreach(var face in m.Faces)
                {
                    s.Triangles.Add(new Triangle{A=vertices[face.A],B=vertices[face.B],C=vertices[face.C],LayerId=layerId,ObjectId=objectId,MaterialId=materialId,BaseColor=color});
                    if(face.IsQuad)s.Triangles.Add(new Triangle{A=vertices[face.A],B=vertices[face.C],C=vertices[face.D],LayerId=layerId,ObjectId=objectId,MaterialId=materialId,BaseColor=color});
                }
            }
        }
        public static ExportResult Render(Snapshot snapshot,Config config,Action<int> progress)
        {
            string dir=Path.Combine(config.Output,DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")+"_"+Guid.NewGuid().ToString("N").Substring(0,6));
            var raster=new Raster(snapshot.Camera);int total=snapshot.Triangles.Count;
            for(int i=0;i<total;i++){raster.Draw(snapshot.Triangles[i]);if(i%2000==0 && progress!=null)progress(i*70/total);}
            var visible=new HashSet<int>(raster.Layers.Where(x=>x>0));
            var visibleColors=snapshot.LayerColors.Where(x=>visible.Contains(x.Key)).ToList();
            if(visibleColors.Any(x=>x.Value==0))throw new InvalidOperationException("Material ID cannot use a pure black layer because black is the background. Change that Rhino layer color.");
            var duplicate=visibleColors.GroupBy(x=>x.Value).FirstOrDefault(g=>g.Count()>1);
            if(duplicate!=null)throw new InvalidOperationException("Several visible Rhino layers share the Material ID color "+Raster.Hex(duplicate.Key)+". Give these layers different colors, or click Assign contrast colors.");
            var files=raster.Save(dir,snapshot.LayerColors);
            if(snapshot.Rendered!=null&&snapshot.Rendered.Length>0){string rendered=Path.Combine(dir,"rendered.png");File.WriteAllBytes(rendered,snapshot.Rendered);files["rendered"]=rendered;}
            var rules=config.Layers.Where(x=>visible.Contains(x.Index+1)).ToList();
            string prompt=MaterialRules.Prompt(rules,null);
            string wear=null,placementConstraint=null;
            if(config.ProductMode)wear=ProductExport.Save(snapshot,raster,config,dir,files,out placementConstraint);
            File.WriteAllText(Path.Combine(dir,"material_prompt.txt"),prompt,Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir,"color_legend.json"),JsonConvert.SerializeObject(rules,Formatting.Indented),Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir,"manifest.json"),JsonConvert.SerializeObject(new{schema="rhino-ai/1",camera=snapshot.Camera,view=snapshot.View,files=files,objects=snapshot.Objects,layers=rules,warnings=snapshot.Warnings,depth=new{format="float32 little-endian, row-major, top-left origin",units="Rhino document units",measurement="linear camera-axis distance",background="NaN",near=raster.Depth.Where(x=>!float.IsInfinity(x)).Min(),far=raster.Depth.Where(x=>!float.IsInfinity(x)).Max()},normal="camera space: +X right, +Y up, +Z toward camera; RGB=(normal+1)/2"},Formatting.Indented),Encoding.UTF8);
            var html=new StringBuilder("<!doctype html><meta charset='utf-8'><title>Rhino to Comfy · Color Code</title><style>body{font:16px system-ui;background:#16191e;color:#eee;margin:32px}img{max-width:100%;max-height:65vh}table{border-collapse:collapse;width:100%}td,th{text-align:left;padding:12px;border-bottom:1px solid #444}pre{white-space:pre-wrap;line-height:1.6}</style><h1>Rhino to Comfy · Color Code</h1><img src='color_code.png'><table><tr><th>ID color</th><th>Rhino layer</th><th>Target material</th></tr>");
            foreach(var rule in rules)html.Append("<tr><td style='border-left:24px solid "+rule.Color+"'>"+rule.Color+"</td><td>"+Escape(rule.Name)+"</td><td>"+Escape(rule.Material)+"</td></tr>");
            html.Append("</table><h2>Color → target material</h2><pre>"+Escape(prompt)+"</pre><h2>Depth / Lines / Normals</h2><img src='depth.png'><img src='lineart.png'><img src='normal.png'>");
            if(config.ProductMode)html.Append("<h2>Background blend · reference / placement / edit mask</h2><img src='reference.png'><img src='placement.png'><img src='inpaint_mask.png'><pre>"+Escape(wear)+"</pre>");
            html.Append("<h2>Export notes</h2><pre>"+Escape(string.Join("\n",snapshot.Warnings))+"</pre>");
            File.WriteAllText(Path.Combine(dir,"preview.html"),html.ToString(),Encoding.UTF8);
            if(progress!=null)progress(100);
            return new ExportResult{Directory=dir,Files=files,Prompt=prompt,WearPrompt=wear,PlacementConstraint=placementConstraint,Warnings=snapshot.Warnings};
        }
        static string Escape(string text){return System.Net.WebUtility.HtmlEncode(text??"");}
        static byte[] CaptureRendered(RhinoView view,Guid viewport,int width,int height,HashSet<Guid> selected)
        {
            ScopeOnly conduit=null;
            try
            {
                if(selected!=null){conduit=new ScopeOnly(viewport,selected);conduit.Enabled=true;view.Redraw();}
                var mode=DisplayModeDescription.GetDisplayMode(DisplayModeDescription.RenderedId);
                using(var bitmap=view.CaptureToBitmap(new Size(width,height),mode))using(var memory=new MemoryStream())
                {if(bitmap==null)throw new InvalidOperationException("Rendered view capture failed.");bitmap.Save(memory,ImageFormat.Png);return memory.ToArray();}
            }
            finally{if(conduit!=null){conduit.Dispose();view.Redraw();}}
        }
    }
    sealed class BackgroundOnly : DisplayConduit,IDisposable
    {
        readonly Guid viewport;
        public BackgroundOnly(Guid id){viewport=id;}
        protected override void ObjectCulling(CullObjectEventArgs e){if(e.Viewport.Id==viewport)e.CullObject=true;}
        public void Dispose(){Enabled=false;}
    }
    sealed class ScopeOnly : DisplayConduit,IDisposable
    {
        readonly Guid viewport;readonly HashSet<Guid> included;
        public ScopeOnly(Guid id,HashSet<Guid> objects){viewport=id;included=objects;}
        protected override void ObjectCulling(CullObjectEventArgs e){if(e.Viewport.Id==viewport&&e.RhinoObject!=null&&!included.Contains(e.RhinoObject.Id))e.CullObject=true;}
        public void Dispose(){Enabled=false;}
    }
}
