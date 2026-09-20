using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Rhino;

namespace RhinoAI
{
    // Runs on Rhino's UI thread. The renderer consumes a snapshot in the background.
    public sealed class AutoSyncWatcher : IDisposable
    {
        readonly RhinoDoc doc; readonly Func<bool> enabled; readonly Func<string> settings; readonly Func<Task> sync;
        readonly Timer timer=new Timer{Interval=500};
        long revision;string observed="",sent="";DateTime changed=DateTime.UtcNow;bool running;
        public AutoSyncWatcher(RhinoDoc document,Func<bool> isEnabled,Func<string> signature,Func<Task> synchronize)
        {
            doc=document;enabled=isEnabled;settings=signature;sync=synchronize;
            RhinoDoc.AddRhinoObject+=Changed;RhinoDoc.DeleteRhinoObject+=Changed;RhinoDoc.UndeleteRhinoObject+=Changed;RhinoDoc.ReplaceRhinoObject+=Changed;RhinoDoc.ModifyObjectAttributes+=Changed;RhinoDoc.LayerTableEvent+=Changed;RhinoDoc.MaterialTableEvent+=Changed;
            timer.Tick+=Tick;timer.Start();
        }
        void Changed(object sender,EventArgs args){revision++;}
        public void Reset(){sent="";observed="";changed=DateTime.UtcNow;}
        public static string FileStamp(string path){try{return File.Exists(path)?File.GetLastWriteTimeUtc(path).Ticks+":"+new FileInfo(path).Length:"missing";}catch{return "unavailable";}}
        string Signature()
        {
            var vp=doc.Views.ActiveView.ActiveViewport;double l,r,b,t,n,f;vp.GetFrustum(out l,out r,out b,out t,out n,out f);
            return revision+"|"+vp.Id+"|"+vp.CameraLocation+"|"+vp.CameraDirection+"|"+vp.CameraUp+"|"+vp.Size+"|"+string.Join(",",new[]{l,r,b,t,n,f})+"|"+vp.WallpaperFilename+"|"+vp.WallpaperVisible+"|"+vp.WallpaperGrayscale+"|"+FileStamp(vp.WallpaperFilename)+"|"+string.Join(",",doc.Objects.GetSelectedObjects(false,false).Select(o=>o.Id).OrderBy(id=>id))+"|"+settings();
        }
        async void Tick(object sender,EventArgs args)
        {
            if(running||!enabled()||RhinoDoc.ActiveDoc==null||RhinoDoc.ActiveDoc.RuntimeSerialNumber!=doc.RuntimeSerialNumber||doc.Views.ActiveView==null||RhinoApp.InCommand>0)return;
            string current=Signature();if(current!=observed){observed=current;changed=DateTime.UtcNow;return;}
            if(current==sent||(DateTime.UtcNow-changed).TotalMilliseconds<1800)return;
            running=true;try{await sync();sent=current;}catch(Exception e){RhinoApp.WriteLine("自动同步："+e.Message);sent=current;}finally{running=false;}
        }
        public void Dispose(){timer.Stop();timer.Dispose();RhinoDoc.AddRhinoObject-=Changed;RhinoDoc.DeleteRhinoObject-=Changed;RhinoDoc.UndeleteRhinoObject-=Changed;RhinoDoc.ReplaceRhinoObject-=Changed;RhinoDoc.ModifyObjectAttributes-=Changed;RhinoDoc.LayerTableEvent-=Changed;RhinoDoc.MaterialTableEvent-=Changed;}
    }
}
