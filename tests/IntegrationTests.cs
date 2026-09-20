using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace RhinoAI
{
    public static class IntegrationTests
    {
        static int passed;
        static void Assert(bool condition,string name){if(!condition)throw new Exception("FAIL: "+name);passed++;System.Console.WriteLine("PASS: "+name);}
        static Triangle Tri(double z,int id){return new Triangle{A=new Vertex{X=-0.7,Y=-0.7,Z=z,NZ=1},B=new Vertex{X=0.7,Y=-0.7,Z=z,NZ=1},C=new Vertex{X=0,Y=0.7,Z=z,NZ=1},ObjectId=id,LayerId=1,MaterialId=1,BaseColor=0x999999};}
        public static string Run(RhinoDoc doc,string root)
        {
            passed=0;Directory.CreateDirectory(root);
            var camera=new Camera{Width=64,Height=64,Left=-1,Right=1,Bottom=-1,Top=1,Near=1,Far=20,Perspective=false};
            var r=new Raster(camera);r.Draw(Tri(5,1));r.Draw(Tri(3,2));Assert(r.Objects[32*64+32]==2,"nearest surface wins");Assert(Math.Abs(r.Depth[32*64+32]-3)<1e-6,"linear camera depth");
            r.Draw(Tri(0.5,3));r.Draw(Tri(25,4));Assert(r.Objects[32*64+32]==2,"near and far clipping");
            var crossing=Tri(2,5);crossing.A.Z=0.2;r.Draw(crossing);Assert(r.Objects.Any(x=>x==5),"triangle crossing near plane is clipped, not discarded");
            var persp=new Raster(new Camera{Width=64,Height=64,Left=-1,Right=1,Bottom=-1,Top=1,Near=1,Far=20,Perspective=true});persp.Draw(Tri(3,1));Assert(persp.Objects[32*64+32]==1,"perspective projection");
            var fullCamera=new Camera{Width=1000,Height=500,Left=-1,Right=1,Bottom=-0.5,Top=0.5,Near=1,Far=100,Perspective=true};var cropRect=new Rectangle(250,100,500,250);var cropCamera=ProductExport.CropCamera(fullCamera,cropRect);
            double px=0.2,py=0.1,pz=5,projectionScale=fullCamera.Near/pz,fullX=(px*projectionScale-fullCamera.Left)/(fullCamera.Right-fullCamera.Left)*fullCamera.Width,fullY=(fullCamera.Top-py*projectionScale)/(fullCamera.Top-fullCamera.Bottom)*fullCamera.Height;
            double detailX=(px*projectionScale-cropCamera.Left)/(cropCamera.Right-cropCamera.Left)*cropCamera.Width,detailY=(cropCamera.Top-py*projectionScale)/(cropCamera.Top-cropCamera.Bottom)*cropCamera.Height;
            Assert(cropCamera.Perspective&&Math.Abs(detailX-(fullX-cropRect.Left)*fullCamera.Width/cropRect.Width)<1e-9&&Math.Abs(detailY-(fullY-cropRect.Top)*fullCamera.Height/cropRect.Height)<1e-9,"detail crop preserves identical perspective rays");
            var files=r.Save(Path.Combine(root,"synthetic"),new Dictionary<int,int>{{1,0xff0000}});Assert(files.Count==12&&files.ContainsKey("shape_lock"),"twelve aligned raster channels including shape lock");
            using(var map=new Bitmap(files["color_code"]))Assert((map.GetPixel(32,32).ToArgb()&0xffffff)==0xff0000,"color code exact RGB");
            using(var map=new Bitmap(files["mask"]))Assert(map.GetPixel(0,0).R==0&&map.GetPixel(32,32).R==255,"binary foreground mask");
            bool[] seed=new bool[25];seed[12]=true;var expanded=ProductExport.Dilate(seed,5,5,1);Assert(expanded.Count(x=>x)==9,"mask dilation radius");Assert(ProductExport.Dilate(seed,5,5,0).SequenceEqual(seed),"zero padding identity");
            Assert(ProductExport.EffectivePadding(new Config{MaskPadding=12,AutoEditRegion=true},1024,589,38,40)>=80,"adaptive edit mask expands tiny product replacement area");Assert(ProductExport.EffectivePadding(new Config{MaskPadding=12,AutoEditRegion=false},1024,589,38,40)==12,"manual edit mask keeps requested padding");
            bool[] tiny=new bool[21*21];tiny[10*21+10]=true;var edit=ProductExport.EditRegion(tiny,21,21,10,10,11,11,8);Assert(edit[10*21+10]&&edit[10*21+2]&&!edit[2*21+2],"edit region is a smooth ellipse, not a square dilation");
            var export=new ExportResult{Prompt="gold material",WearPrompt="ring on finger",Files=files};
            var graph=JObject.Parse("{\"1\":{\"class_type\":\"LoadImage\",\"inputs\":{\"image\":\"old.png\"},\"_meta\":{\"title\":\"RHINO:depth\"}},\"2\":{\"class_type\":\"CLIPTextEncode\",\"inputs\":{\"text\":\"{{color_materials}}\"}}}");
            var bound=ComfyClient.Bind(graph,new Dictionary<string,string>{{"depth","job/depth.png"}},export);Assert((string)bound["1"]["inputs"]["image"]=="job/depth.png","LoadImage title binding");Assert((string)bound["2"]["inputs"]["text"]=="gold material","only color materials bind into optional API JSON");Assert((string)graph["1"]["inputs"]["image"]=="old.png","workflow original unchanged");
            bool rejected=false;try{ComfyClient.Bind(JObject.Parse("{\"nodes\":[]}"),new Dictionary<string,string>(),export);}catch(InvalidOperationException){rejected=true;}Assert(rejected,"reject UI workflow instead of silently submitting it");
            var vp=doc.Views.ActiveView.ActiveViewport;var box=doc.Objects.First(o=>o.Visible&&o.Geometry is Brep).Geometry.GetBoundingBox(true);double l,rr,b,t,n,f;vp.GetFrustum(out l,out rr,out b,out t,out n,out f);var transform=vp.GetTransform(CoordinateSystem.World,CoordinateSystem.Camera);double error=0;
            foreach(var point in box.GetCorners()){var q=point;q.Transform(transform);double k=vp.IsPerspectiveProjection?n/-q.Z:1;double x=(q.X*k-l)/(rr-l)*vp.Size.Width,y=(t-q.Y*k)/(t-b)*vp.Size.Height;var pixel=vp.WorldToClient(point);error=Math.Max(error,Math.Max(Math.Abs(x-pixel.X),Math.Abs(y-pixel.Y)));}Assert(error<0.01,"camera projection agrees with Rhino WorldToClient (<0.01 px)");
            var config=new Config{Output=Path.Combine(root,"scene"),LongEdge=1024};var snapshot=Exporter.Capture(doc,config);Assert(snapshot.Triangles.All(x=>x.MaterialId==x.LayerId),"material ID is assigned strictly by Rhino layer");Assert(snapshot.Rendered!=null&&snapshot.Rendered.Length>0,"Rendered display mode captured at the active camera");var result=Exporter.Render(snapshot,config,null);Assert(result.Files.Count==13&&result.Files.ContainsKey("rendered")&&result.Files.ContainsKey("shape_lock"),"current Rhino scene export includes rendered + shape-lock guides");Assert(File.Exists(Path.Combine(result.Directory,"material_prompt.txt")),"material prompt and legend export");
            // Native wallpaper capture is verified without saving or changing geometry. Restore in finally.
            string old=vp.WallpaperFilename;bool gray=vp.WallpaperGrayscale,visible=vp.WallpaperVisible,modified=doc.Modified;
            string pattern=Path.Combine(root,"wallpaper-test.png");
            using(var bmp=new Bitmap(600,400))using(var g=Graphics.FromImage(bmp)){g.Clear(Color.FromArgb(24,98,166));g.FillRectangle(Brushes.Orange,0,0,300,200);g.FillRectangle(Brushes.Lime,300,200,300,200);bmp.Save(pattern,ImageFormat.Png);}
            try
            {
                vp.SetWallpaper(pattern,false,true);config.ProductMode=true;config.LongEdge=512;
                var productSnapshot=Exporter.Capture(doc,config);var productResult=Exporter.Render(productSnapshot,config,null);
                Assert(productResult.Files.ContainsKey("reference")&&productResult.Files.ContainsKey("placement")&&productResult.Files.ContainsKey("inpaint_mask"),"wallpaper try-on bundle");
                using(var bg=new Bitmap(productResult.Files["reference"]))
                {
                    Assert(bg.Width==productSnapshot.Camera.Width&&bg.Height==productSnapshot.Camera.Height,"reference alignment dimensions");
                    int clean=0;for(int y=0;y<bg.Height;y++)for(int x=0;x<bg.Width;x++){var p=bg.GetPixel(x,y);if(p.ToArgb()==Color.Orange.ToArgb()||p.ToArgb()==Color.Lime.ToArgb()||p.ToArgb()==Color.FromArgb(24,98,166).ToArgb()||p.ToArgb()==Color.White.ToArgb())clean++;}
                    Assert(clean>bg.Width*bg.Height*0.98,"native background capture excludes geometry");
                    bg.Save(Path.Combine(root,"wallpaper-captured.png"));
                }
                Assert(File.Exists(Path.Combine(productResult.Directory,"tryon.json")),"structured try-on metadata");
            }
            finally{vp.SetWallpaper(old??"",gray,visible);doc.Modified=modified;doc.Views.Redraw();}
            var send=Task.Run(async()=>{using(var client=new ComfyClient("http://127.0.0.1:8000"))return await client.Send(result,config);}).GetAwaiter().GetResult();
            Assert(File.Exists(Path.Combine(result.Directory,"comfy_sync.json")),"real ComfyUI image update manifest");
            Assert(!File.Exists(Path.Combine(result.Directory,"comfy_job.json")),"sync does not create a generation job");
            System.Console.WriteLine(send);System.Console.WriteLine("RESULT_DIRECTORY="+result.Directory);
            File.WriteAllText(Path.Combine(root,"test-results.txt"),passed+" checks passed\n"+send+"\n"+result.Directory);
            return passed+" checks passed. "+result.Directory;
        }
    }
}
