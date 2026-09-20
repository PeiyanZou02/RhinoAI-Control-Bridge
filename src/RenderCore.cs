using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Rhino.Geometry;

namespace RhinoAI
{
    // The rasterizer consumes an immutable camera/mesh snapshot. No Rhino document writes.
    public sealed class Vertex
    {
        public double X,Y,Z,NX,NY,NZ;
        public Vertex Mix(Vertex b,double t) { return new Vertex { X=X+(b.X-X)*t,Y=Y+(b.Y-Y)*t,Z=Z+(b.Z-Z)*t,NX=NX+(b.NX-NX)*t,NY=NY+(b.NY-NY)*t,NZ=NZ+(b.NZ-NZ)*t }; }
    }
    public sealed class Triangle
    {
        public Vertex A,B,C;
        public int ObjectId,LayerId,MaterialId,BaseColor;
    }
    public sealed class Camera
    {
        public int Width,Height;
        public double Left,Right,Bottom,Top,Near,Far;
        public bool Perspective;
    }
    public sealed class Raster
    {
        public readonly Camera Camera;
        public readonly float[] Depth,NX,NY,NZ;
        public readonly int[] Objects,Layers,Materials,Colors;
        public Raster(Camera camera)
        {
            Camera=camera; int n=camera.Width*camera.Height;
            Depth=Enumerable.Repeat(float.PositiveInfinity,n).ToArray();
            NX=new float[n]; NY=new float[n]; NZ=new float[n];
            Objects=new int[n]; Layers=new int[n]; Materials=new int[n]; Colors=new int[n];
        }
        static List<Vertex> Clip(List<Vertex> p,double z,bool lower)
        {
            var result=new List<Vertex>(); if(p.Count==0)return result;
            Vertex a=p[p.Count-1]; bool ina=lower?a.Z>=z:a.Z<=z;
            foreach(var b in p) { bool inb=lower?b.Z>=z:b.Z<=z;
                if(ina!=inb)result.Add(a.Mix(b,(z-a.Z)/(b.Z-a.Z)));
                if(inb)result.Add(b); a=b; ina=inb;
            } return result;
        }
        public void Draw(Triangle t)
        {
            var p=Clip(Clip(new List<Vertex>{t.A,t.B,t.C},Camera.Near,true),Camera.Far,false);
            for(int i=1;i+1<p.Count;i++)DrawClipped(p[0],p[i],p[i+1],t);
        }
        void Project(Vertex v,out double x,out double y)
        {
            double k=Camera.Perspective?Camera.Near/v.Z:1;
            x=(v.X*k-Camera.Left)/(Camera.Right-Camera.Left)*Camera.Width;
            y=(Camera.Top-v.Y*k)/(Camera.Top-Camera.Bottom)*Camera.Height;
        }
        static double Edge(double ax,double ay,double bx,double by,double x,double y) {return (bx-ax)*(y-ay)-(by-ay)*(x-ax);}
        void DrawClipped(Vertex a,Vertex b,Vertex c,Triangle tri)
        {
            double ax,ay,bx,by,cx,cy; Project(a,out ax,out ay); Project(b,out bx,out by); Project(c,out cx,out cy);
            double area=Edge(ax,ay,bx,by,cx,cy); if(Math.Abs(area)<1e-10 || double.IsNaN(area))return;
            int w=Camera.Width,h=Camera.Height;
            double minx=Math.Max(0,Math.Floor(Math.Min(ax,Math.Min(bx,cx))));
            double maxx=Math.Min(w-1,Math.Ceiling(Math.Max(ax,Math.Max(bx,cx))));
            double miny=Math.Max(0,Math.Floor(Math.Min(ay,Math.Min(by,cy))));
            double maxy=Math.Min(h-1,Math.Ceiling(Math.Max(ay,Math.Max(by,cy))));
            if(minx>maxx || miny>maxy)return;
            for(int y=(int)miny;y<=(int)maxy;y++)for(int x=(int)minx;x<=(int)maxx;x++)
            {
                double u=Edge(bx,by,cx,cy,x+0.5,y+0.5)/area, v=Edge(cx,cy,ax,ay,x+0.5,y+0.5)/area, q=1-u-v;
                if(u< -1e-9 || v< -1e-9 || q< -1e-9)continue;
                if(Camera.Perspective) {u/=a.Z;v/=b.Z;q/=c.Z;double s=u+v+q;u/=s;v/=s;q/=s;}
                double z=u*a.Z+v*b.Z+q*c.Z;int idx=y*w+x;if(z>=Depth[idx])continue;
                Depth[idx]=(float)z; Objects[idx]=tri.ObjectId; Layers[idx]=tri.LayerId; Materials[idx]=tri.MaterialId;Colors[idx]=tri.BaseColor;
                double nx=u*a.NX+v*b.NX+q*c.NX,ny=u*a.NY+v*b.NY+q*c.NY,nz=u*a.NZ+v*b.NZ+q*c.NZ;
                double len=Math.Sqrt(nx*nx+ny*ny+nz*nz);if(len>1e-12){nx/=len;ny/=len;nz/=len;}
                NX[idx]=(float)nx;NY[idx]=(float)ny;NZ[idx]=(float)nz;
            }
        }
        public static int Rgb(int r,int g,int b){return (r<<16)|(g<<8)|b;}
        public static string Hex(int color){return "#"+color.ToString("X6");}
        public static void SavePng(string path,int width,int height,int[] pixels)
        {
            using(var bitmap=new Bitmap(width,height,PixelFormat.Format32bppRgb))
            {
                var data=bitmap.LockBits(new Rectangle(0,0,width,height),ImageLockMode.WriteOnly,PixelFormat.Format32bppRgb);
                try { for(int y=0;y<height;y++)Marshal.Copy(pixels,y*width,IntPtr.Add(data.Scan0,y*data.Stride),width); }
                finally {bitmap.UnlockBits(data);} bitmap.Save(path,ImageFormat.Png);
            }
        }
        public Dictionary<string,string> Save(string directory,Dictionary<int,int> layerColors)
        {
            Directory.CreateDirectory(directory);
            var visible=Depth.Where(d=>!float.IsInfinity(d)).ToArray();
            if(visible.Length==0)throw new InvalidOperationException("No visible mesh surfaces in the current view.");
            double near=visible.Min(),far=visible.Max(),range=Math.Max(1e-8,far-near);
            int n=Depth.Length,w=Camera.Width,h=Camera.Height;
            var maps=new Dictionary<string,int[]>();
            foreach(var name in new[]{"shape_lock","depth","depth_inverse","normal","mask","color_code","material_id","object_id","basecolor","lineart","edges","silhouette"})maps[name]=new int[n];
            for(int i=0;i<n;i++)
            {
                bool hit=Objects[i]!=0;int d=hit?Math.Max(1,Math.Min(255,1+(int)Math.Round(254*(1-(Depth[i]-near)/range)))):0;
                maps["depth"][i]=Rgb(d,d,d);int inv=hit?256-d:0;maps["depth_inverse"][i]=Rgb(inv,inv,inv);
                maps["normal"][i]=hit?Rgb(Encode(NX[i]),Encode(NY[i]),Encode(NZ[i])):0;
                maps["mask"][i]=hit?0xffffff:0;
                maps["color_code"][i]=hit?layerColors[Layers[i]]:0;
                int materialColor;
                maps["material_id"][i]=hit?(layerColors.TryGetValue(Materials[i],out materialColor)?materialColor:Palette(Materials[i])):0;
                maps["object_id"][i]=hit?Palette(Objects[i]):0;
                maps["basecolor"][i]=hit?Colors[i]:0;
                bool edge=false,sil=false;int x=i%w,y=i/w;
                foreach(int off in new[]{-1,1,-w,w})
                {
                    int j=i+off;if((off==-1&&x==0)||(off==1&&x==w-1)||(off==-w&&y==0)||(off==w&&y==h-1)){if(hit){edge=true;sil=true;}continue;}
                    bool other=Objects[j]!=0;
                    if(hit!=other){edge=true;sil=true;continue;}
                    if(!hit)continue;
                    double dot=NX[i]*NX[j]+NY[i]*NY[j]+NZ[i]*NZ[j];
                    if(Objects[i]!=Objects[j] || Layers[i]!=Layers[j] || dot<0.78 || Math.Abs(Depth[i]-Depth[j])>Math.Max(range*0.015,Depth[i]*0.002))edge=true;
                }
                maps["lineart"][i]=edge?0:0xffffff;maps["edges"][i]=edge?0xffffff:0;maps["silhouette"][i]=sil?0xffffff:0;
            }
            // One human-readable geometry guide is more reliable for multimodal
            // image models than asking them to infer CAD form from raw maps alone.
            // It preserves the exact camera and silhouette while exposing concavity,
            // overlap and sharp transitions with directional clay shading + ink edges.
            for(int i=0;i<n;i++)
            {
                if(Objects[i]==0){maps["shape_lock"][i]=0xf7f7f7;continue;}
                int x=i%w,y=i/w;bool ink=maps["edges"][i]!=0;
                if(!ink)
                {
                    for(int yy=Math.Max(0,y-1);yy<=Math.Min(h-1,y+1)&&!ink;yy++)
                    for(int xx=Math.Max(0,x-1);xx<=Math.Min(w-1,x+1);xx++)
                        if(maps["edges"][yy*w+xx]!=0){ink=true;break;}
                }
                if(ink){maps["shape_lock"][i]=0x20242a;continue;}
                double light=Math.Max(0,Math.Min(1,-0.36*NX[i]+0.48*NY[i]+0.80*NZ[i]));
                int shade=Math.Max(105,Math.Min(232,(int)Math.Round(112+120*light)));
                maps["shape_lock"][i]=Rgb(shade,shade,shade);
            }
            var files=new Dictionary<string,string>();
            foreach(var m in maps){string path=Path.Combine(directory,m.Key+".png");SavePng(path,w,h,m.Value);files[m.Key]=path;}
            using(var writer=new BinaryWriter(File.Create(Path.Combine(directory,"depth_linear.f32"))))foreach(float d in Depth)writer.Write(float.IsInfinity(d)?float.NaN:d);
            return files;
        }
        static int Encode(float n){return Math.Max(0,Math.Min(255,(int)Math.Round((n+1)*127.5)));}
        public static int Palette(int id)
        {
            // Saturated, widely separated colors remain readable on the black ID-map
            // background, including on very thin parts.
            int[] first={0,0xff1744,0x00e5ff,0xffea00,0x651fff,0x00e676,0xff6d00,0x2979ff,0xf500ff,0x76ff03,0xff4081,0x00bfa5,0xc6a0ff,0xffffff,0x8b4513};
            if(id<first.Length)return first[id];
            // Odd multiplication permutes the 24-bit integer space, so IDs remain unique.
            int c=(int)(((long)id*0x9e3779)&0xffffff);
            while(c==0 || first.Contains(c))c=(c+1)&0xffffff;
            return c;
        }
    }
}
