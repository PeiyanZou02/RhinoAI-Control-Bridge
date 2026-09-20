using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace RhinoAI
{
    public static class ProductExport
    {
        // Separable square dilation: the white area is editable, the black area is protected.
        public static bool[] Dilate(bool[] source,int w,int h,int radius)
        {
            var temp=new bool[source.Length];var result=new bool[source.Length];
            for(int y=0;y<h;y++)
            {
                int count=0;for(int x=0;x<=Math.Min(radius,w-1);x++)if(source[y*w+x])count++;
                for(int x=0;x<w;x++){temp[y*w+x]=count>0;int remove=x-radius,add=x+radius+1;if(remove>=0&&source[y*w+remove])count--;if(add<w&&source[y*w+add])count++;}
            }
            for(int x=0;x<w;x++)
            {
                int count=0;for(int y=0;y<=Math.Min(radius,h-1);y++)if(temp[y*w+x])count++;
                for(int y=0;y<h;y++){result[y*w+x]=count>0;int remove=y-radius,add=y+radius+1;if(remove>=0&&temp[remove*w+x])count--;if(add<h&&temp[add*w+x])count++;}
            }return result;
        }
        public static string Save(Snapshot snapshot,Raster raster,Config config,string dir,Dictionary<string,string> files,out string placementConstraint)
        {
            int w=snapshot.Camera.Width,h=snapshot.Camera.Height;
            if(snapshot.Wallpaper==null)throw new InvalidOperationException("未捕获 Wallpaper。");
            bool[] product=raster.Objects.Select(x=>x>0).ToArray();
            var indices=Enumerable.Range(0,product.Length).Where(i=>product[i]).ToArray();
            int x0=indices.Min(i=>i%w),x1=indices.Max(i=>i%w),y0=indices.Min(i=>i/w),y1=indices.Max(i=>i/w);
            Rectangle detailRect=DetailRect(x0,y0,x1+1,y1+1,w,h);
            int effectivePadding=EffectivePadding(config,w,h,x1-x0+1,y1-y0+1);
            double nx0=x0/(double)w,ny0=y0/(double)h,nx1=(x1+1)/(double)w,ny1=(y1+1)/(double)h,zoom=w/(double)detailRect.Width;
            placementConstraint=string.Format(System.Globalization.CultureInfo.InvariantCulture,"FULL-FRAME PLACEMENT LOCK: the full placement and scale_lock images are the absolute authority for final product position, pixel size, rotation and silhouette. On the {0}x{1} full canvas, copy the product bounding box exactly: left={2}px, top={3}px, right={4}px, bottom={5}px; normalized box=({6:F6},{7:F6})-({8:F6},{9:F6}); normalized center=({10:F6},{11:F6}); normalized width={12:F6}, height={13:F6}; exact silhouette area={14} pixels ({15:F6} of the full canvas). The final product silhouette must touch the same left, right, top and bottom extrema shown in scale_lock and must not extend beyond that box. The magenta silhouette, rectangle and crosshair are annotations only and must not appear in the final image. Technical detail images are approximately {16:F2}x magnified digital crops for surface detail only. Never use their apparent object size, never enlarge the product, and never move it outside this full-frame placement box.",w,h,x0,y0,x1+1,y1+1,nx0,ny0,nx1,ny1,(nx0+nx1)*0.5,(ny0+ny1)*0.5,nx1-nx0,ny1-ny0,indices.Length,indices.Length/(double)(w*h),zoom);
            string reference=Path.Combine(dir,"reference.png");File.WriteAllBytes(reference,snapshot.Wallpaper);files["reference"]=reference;
            if(File.Exists(snapshot.WallpaperPath))using(var original=Image.FromFile(snapshot.WallpaperPath))original.Save(Path.Combine(dir,"reference_original.png"),ImageFormat.Png);
            bool[] mask=EditRegion(product,w,h,x0,y0,x1+1,y1+1,effectivePadding);
            if(!string.IsNullOrWhiteSpace(config.OcclusionMask))using(var protect=new Bitmap(config.OcclusionMask))
            {
                if(protect.Width!=w||protect.Height!=h)throw new InvalidOperationException("遮挡蒙版必须与导出图尺寸一致："+w+" × "+h+"。白色保护，黑色不保护。");
                for(int y=0;y<h;y++)for(int x=0;x<w;x++)if(protect.GetPixel(x,y).GetBrightness()>0.5f)mask[y*w+x]=false;
                string target=Path.Combine(dir,"occlusion_mask.png");protect.Save(target,ImageFormat.Png);files["occlusion_mask"]=target;
            }
            string maskPath=Path.Combine(dir,"inpaint_mask.png");Raster.SavePng(maskPath,w,h,mask.Select(x=>x?0xffffff:0).ToArray());files["inpaint_mask"]=maskPath;
            files["product_mask"]=files["mask"];
            // Exact pixel-space placement guide. This is a flat-color composite, not a beauty render.
            using(var background=new Bitmap(reference))using(var composite=new Bitmap(w,h,PixelFormat.Format32bppArgb))
            {
                using(var g=Graphics.FromImage(composite))g.DrawImageUnscaled(background,0,0);
                for(int y=0;y<h;y++)for(int x=0;x<w;x++){int i=y*w+x;if(product[i])composite.SetPixel(x,y,Color.FromArgb(unchecked((int)0xff000000)|raster.Colors[i]));}
                string path=Path.Combine(dir,"placement.png");composite.Save(path,ImageFormat.Png);files["placement"]=path;
            }
            using(var background=new Bitmap(reference))using(var scaleLock=new Bitmap(w,h,PixelFormat.Format32bppArgb))
            {
                using(var g=Graphics.FromImage(scaleLock)){g.DrawImageUnscaled(background,0,0);using(var veil=new SolidBrush(Color.FromArgb(72,0,0,0)))g.FillRectangle(veil,0,0,w,h);}
                for(int y=0;y<h;y++)for(int x=0;x<w;x++){int i=y*w+x;if(product[i])scaleLock.SetPixel(x,y,Color.Magenta);}
                using(var g=Graphics.FromImage(scaleLock))using(var pen=new Pen(Color.Magenta,2))
                {
                    g.DrawRectangle(pen,x0,y0,Math.Max(1,x1-x0),Math.Max(1,y1-y0));float cx=(x0+x1)*0.5f,cy=(y0+y1)*0.5f;
                    g.DrawLine(pen,cx-8,cy,cx+8,cy);g.DrawLine(pen,cx,cy-8,cx,cy+8);
                }
                string path=Path.Combine(dir,"scale_lock.png");scaleLock.Save(path,ImageFormat.Png);files["scale_lock"]=path;
            }
            SaveCrop(files["placement"],Path.Combine(dir,"placement_detail.png"),detailRect,w,h);files["placement_detail"]=Path.Combine(dir,"placement_detail.png");
            if(files.ContainsKey("rendered")){SaveCrop(files["rendered"],Path.Combine(dir,"rendered_detail.png"),detailRect,w,h);files["rendered_detail"]=Path.Combine(dir,"rendered_detail.png");}
            var detailCamera=CropCamera(snapshot.Camera,detailRect);
            var detailRaster=new Raster(detailCamera);foreach(var triangle in snapshot.Triangles)detailRaster.Draw(triangle);
            string detailDirectory=Path.Combine(dir,"detail_raster");var detailFiles=detailRaster.Save(detailDirectory,snapshot.LayerColors);
            foreach(var channel in new[]{"shape_lock","material_id","normal","edges","depth"})
            {
                string key=channel+"_detail",target=Path.Combine(dir,key+".png");File.Copy(detailFiles[channel],target,true);files[key]=target;
            }
            string prompt="\nPRODUCT TRY-ON / 产品佩戴合成\nProduct: "+config.Product+".\n"+config.WearInstructions+"\nUse reference.png as the aligned base photograph. placement.png indicates the exact projected product placement; its flat materials are only a guide. product_mask isolates the product silhouette; inpaint_mask white means editable and black means preserve. Keep pixels outside the edit mask unchanged using masked compositing in the workflow. Depth, normal and edge maps describe only the exported product geometry, not the human body. Do not infer whole-scene depth from the product-only depth image. Respect the visible jewelry outline and design. Handle natural skin/jewelry occlusion and subtle contact shadows.\n";
            File.WriteAllText(Path.Combine(dir,"wear_prompt.txt"),prompt,Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir,"tryon.json"),JsonConvert.SerializeObject(new{schema="rhino-ai-tryon/1",product=config.Product,width=w,height=h,reference="reference.png",referenceAlignment="Rhino native viewport background capture at export dimensions; no guessed crop",placement="placement.png",scaleLock="scale_lock.png",placementDetail="placement_detail.png",detailCropPixels=new[]{detailRect.Left,detailRect.Top,detailRect.Right,detailRect.Bottom},detailCamera=detailCamera,detailProjection="Exact sub-frustum/digital crop of the original camera; identical camera location, orientation, near plane, lens and perspective rays; no dolly, lens change or re-framing",productMask="mask.png",editMask="inpaint_mask.png",requestedMaskPadding=config.MaskPadding,effectiveMaskPadding=effectivePadding,automaticEditRegion=config.AutoEditRegion,maskConvention="white=edit; black=preserve; use ImageToMask red channel, NOT LoadImage alpha MASK output",occlusionMask=string.IsNullOrWhiteSpace(config.OcclusionMask)?null:"occlusion_mask.png",boundsPixels=new[]{x0,y0,x1+1,y1+1},boundsNormalized=new[]{x0/(double)w,y0/(double)h,(x1+1)/(double)w,(y1+1)/(double)h},camera=snapshot.Camera,prompt=prompt},Formatting.Indented),Encoding.UTF8);
            return prompt;
        }
        internal static Rectangle DetailRect(int x0,int y0,int x1,int y1,int width,int height)
        {
            double scale=Math.Max((x1-x0)/(double)width,(y1-y0)/(double)height)*1.6;
            scale=Math.Max(0.08,Math.Min(1.0,scale));int w=Math.Max(2,(int)Math.Ceiling(width*scale)),h=Math.Max(2,(int)Math.Ceiling(height*scale));
            double cx=(x0+x1)*0.5,cy=(y0+y1)*0.5;int left=(int)Math.Round(cx-w*0.5),top=(int)Math.Round(cy-h*0.5);
            left=Math.Max(0,Math.Min(width-w,left));top=Math.Max(0,Math.Min(height-h,top));return new Rectangle(left,top,w,h);
        }
        internal static int EffectivePadding(Config config,int width,int height,int productWidth,int productHeight)
        {
            int requested=Math.Max(0,Math.Min(256,config.MaskPadding));if(!config.AutoEditRegion)return requested;
            int longest=Math.Max(width,height),productLongest=Math.Max(productWidth,productHeight);
            int adaptive=productLongest<longest*0.18?(int)Math.Round(longest*0.08):requested;
            return Math.Max(requested,Math.Min(256,adaptive));
        }
        internal static bool[] EditRegion(bool[] product,int width,int height,int x0,int y0,int x1,int y1,int padding)
        {
            if(padding<=0)return (bool[])product.Clone();
            var result=new bool[product.Length];double cx=(x0+x1-1)*0.5,cy=(y0+y1-1)*0.5;
            double rx=Math.Max(1,(x1-x0)*0.5+padding),ry=Math.Max(1,(y1-y0)*0.5+padding);
            int left=Math.Max(0,(int)Math.Floor(cx-rx)),right=Math.Min(width-1,(int)Math.Ceiling(cx+rx));
            int top=Math.Max(0,(int)Math.Floor(cy-ry)),bottom=Math.Min(height-1,(int)Math.Ceiling(cy+ry));
            for(int y=top;y<=bottom;y++)for(int x=left;x<=right;x++)
            {double dx=(x-cx)/rx,dy=(y-cy)/ry;if(dx*dx+dy*dy<=1)result[y*width+x]=true;}
            for(int i=0;i<product.Length;i++)if(product[i])result[i]=true;
            return result;
        }
        internal static Camera CropCamera(Camera source,Rectangle crop)
        {
            return CropCamera(source,crop,source.Width,source.Height);
        }
        internal static Camera CropCamera(Camera source,Rectangle crop,int outputWidth,int outputHeight)
        {
            double sx0=crop.Left/(double)source.Width,sx1=crop.Right/(double)source.Width,sy0=crop.Top/(double)source.Height,sy1=crop.Bottom/(double)source.Height;
            return new Camera{Width=outputWidth,Height=outputHeight,Left=source.Left+(source.Right-source.Left)*sx0,Right=source.Left+(source.Right-source.Left)*sx1,Top=source.Top-(source.Top-source.Bottom)*sy0,Bottom=source.Top-(source.Top-source.Bottom)*sy1,Near=source.Near,Far=source.Far,Perspective=source.Perspective};
        }
        static void SaveCrop(string source,string target,Rectangle crop,int width,int height)
        {
            using(var image=new Bitmap(source))using(var output=new Bitmap(width,height,PixelFormat.Format32bppArgb))using(var g=Graphics.FromImage(output))
            {g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;g.DrawImage(image,new Rectangle(0,0,width,height),crop,GraphicsUnit.Pixel);output.Save(target,ImageFormat.Png);}
        }
    }
}
