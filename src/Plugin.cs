using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Rhino;
using Rhino.Commands;
using Rhino.PlugIns;
using Newtonsoft.Json.Linq;

[assembly: System.Reflection.AssemblyTitle("Rhino to Comfy")]
[assembly: System.Reflection.AssemblyVersion("0.23.0.0")]
[assembly: Guid("66587CA6-F24F-49B2-83C1-8E616089B2C4")]

namespace RhinoAI
{
    public sealed class BridgePlugin : PlugIn { public BridgePlugin(){} }
    public sealed class OpenBridge : Command
    {
        static BridgeWindow window;
        public override string EnglishName {get{return "Rhino2Comfy";}}
        protected override Result RunCommand(RhinoDoc doc,RunMode mode)
        {
            if(window!=null&&!window.IsDisposed&&window.DocumentSerial!=doc.RuntimeSerialNumber){window.Close();window=null;}
            if(window==null||window.IsDisposed)window=new BridgeWindow(doc);
            window.Show();window.BringToFront();return Result.Success;
        }
    }
    public sealed class ExportCommand : Command
    {
        public override string EnglishName {get{return "Rhino2ComfyExport";}}
        protected override Result RunCommand(RhinoDoc doc,RunMode mode)
        {
            try{var c=Config.Load();var result=Exporter.Render(Exporter.Capture(doc,c),c,null);c.Save();RhinoApp.WriteLine("Export: "+result.Directory);return Result.Success;}
            catch(Exception e){RhinoApp.WriteLine(e.Message);return Result.Failure;}
        }
    }
    public sealed class BridgeWindow : Form
    {
        const string Follow="Follow the current ComfyUI window";
        readonly RhinoDoc doc;
        readonly Config config;
        readonly TextBox server=new TextBox(),output=new TextBox(),workflow=new TextBox(),wear=new TextBox(),occlusion=new TextBox();
        readonly ComboBox target=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList};
        readonly NumericUpDown edge=new NumericUpDown(),padding=new NumericUpDown();
        readonly CheckBox selected=new CheckBox(),tryon=new CheckBox(),autoSync=new CheckBox(),detailPriority=new CheckBox(),adaptiveMask=new CheckBox(),wallpaperAspect=new CheckBox(),sceneAspect=new CheckBox();
        readonly AutoSyncWatcher watcher;
        readonly DataGridView layers=new DataGridView();
        readonly TextBox log=new TextBox();
        readonly FlowLayoutPanel actions=new FlowLayoutPanel();
        readonly Label targetStatus=new Label();
        readonly ToolTip tips=new ToolTip{InitialDelay=350,ReshowDelay=100,AutoPopDelay=12000};
        readonly List<FlatButton> nav=new List<FlatButton>();
        readonly List<Control> pages=new List<Control>();
        readonly ThinProgress progress;
        readonly StatusDot connection;
        readonly Font mono;
        ExportResult last;
        public uint DocumentSerial {get{return doc==null?0:doc.RuntimeSerialNumber;}}
        public BridgeWindow(RhinoDoc document)
        {
            doc=document;config=Config.Load();if(doc!=null)config.Scan(doc);
            Theme.Scale=DeviceDpi/96f;mono=Theme.Mono();
            Text="Rhino to Comfy";ClientSize=new Size(Theme.S(920),Theme.S(640));MinimumSize=new Size(Theme.S(760),Theme.S(540));StartPosition=FormStartPosition.CenterScreen;
            Font=Theme.Body();BackColor=Theme.Bg;ForeColor=Theme.Text;
            progress=new ThinProgress{Dock=DockStyle.Fill,Margin=Padding.Empty};connection=new StatusDot{Dock=DockStyle.Fill,Font=Theme.Small(),Margin=Padding.Empty};connection.Set(Theme.Faint,"Not connected");

            server.Text=config.Server;output.Text=config.Output;workflow.Text=config.Workflow;
            edge.Minimum=256;edge.Maximum=4096;edge.Increment=256;edge.Value=config.LongEdge;
            target.Items.Add(Follow);if(!string.IsNullOrWhiteSpace(config.TargetWorkflow))target.Items.Add(config.TargetWorkflow);target.SelectedIndex=target.Items.Count-1;
            Check(sceneAspect,"Lock widescreen frame 1024 × 589",config.LockSceneAspect,"Standard scenes use a fixed frame, so resizing the Rhino window does not change the output.");
            Check(selected,"Export selected objects only",config.SelectedOnly,"Recommended for background blend, so only the objects to insert are exported.");
            Check(autoSync,"Upload automatically when the scene changes",config.AutoSync,"Uploads images only. It never edits your prompt, starts a generation or switches the ComfyUI window.");
            Check(tryon,"Blend into the Wallpaper background",config.ProductMode,"Uses the viewport Wallpaper photo as the base image and exports placement, masks and blend constraints for the Rhino objects.");
            Check(wallpaperAspect,"Match the Wallpaper aspect ratio",config.MatchWallpaperAspect,"Crops the empty viewport margins. Camera and perspective stay unchanged.");
            Check(detailPriority,"Detail priority",config.DetailPriority,"Adds magnified geometry images when the objects are small in the frame. At most 14 images.");
            Check(adaptiveMask,"Expand the edit region automatically",config.AutoEditRegion,"Grows the editable area so existing content under the objects can be replaced. The pixel value below is the minimum.");

            var sync=Stack();
            var targetRow=Pair(new Field(target,34),Button("Refresh",async delegate{await RefreshWorkflows(false);}));
            Row(sync,"Target workflow",targetRow,"Update to ComfyUI switches ComfyUI to this file, then updates its Batch Images.");
            targetStatus.AutoSize=true;targetStatus.ForeColor=Theme.Muted;targetStatus.Font=Theme.Small();targetStatus.Margin=new Padding(Theme.S(2),Theme.S(2),0,Theme.S(18));targetStatus.Text=" ";Add(sync,targetStatus);
            Add(sync,selected);Add(sync,autoSync);

            var material=new TableLayoutPanel{ColumnCount=1,RowCount=2};material.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));material.RowStyles.Add(new RowStyle(SizeType.AutoSize));material.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            var materialActions=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,Margin=new Padding(0,0,0,Theme.S(10))};
            materialActions.Controls.Add(Tip(Button("Reload layers",delegate{Read();config.Scan(doc);FillLayers();}),"Reads layers and colors from Rhino again."));
            materialActions.Controls.Add(Tip(Button("Assign contrast colors",AssignHighContrastColors),"Gives every layer a distinct Material ID color. Undo is supported."));
            materialActions.Controls.Add(Tip(Button("Create layer materials",delegate{Read();ApplyMaterials();}),"Creates basic Rhino materials from the target materials in the table. Undo is supported."));
            material.Controls.Add(materialActions,0,0);
            layers.Dock=DockStyle.Fill;layers.AllowUserToAddRows=false;layers.AllowUserToDeleteRows=false;layers.AllowUserToResizeRows=false;layers.RowHeadersVisible=false;layers.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells;Theme.Style(layers);
            layers.Columns.Add(new DataGridViewTextBoxColumn{Name="Layer",HeaderText="Layer",ReadOnly=true,Width=Theme.S(220)});layers.Columns.Add(new DataGridViewTextBoxColumn{Name="Color",HeaderText="ID color",ReadOnly=true,Width=Theme.S(130)});layers.Columns.Add(new DataGridViewTextBoxColumn{Name="Material",HeaderText="Target material",AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill});
            layers.CellPainting+=PaintSwatch;
            var gridCard=new Card{Dock=DockStyle.Fill,Margin=Padding.Empty,Padding=new Padding(Theme.S(2))};gridCard.Controls.Add(layers);material.Controls.Add(gridCard,0,1);FillLayers();

            var blend=Stack();
            wear.Multiline=true;wear.ScrollBars=ScrollBars.Vertical;wear.Text=config.WearInstructions;padding.Minimum=0;padding.Maximum=256;padding.Value=config.MaskPadding;occlusion.Text=config.OcclusionMask;
            Add(blend,tryon);Add(blend,wallpaperAspect);Add(blend,detailPriority);Add(blend,adaptiveMask);
            Row(blend,"Blend instructions",new Field(wear,96),null);Row(blend,"Minimum expansion (px)",new Field(padding,34),null);
            Row(blend,"Occlusion mask (optional)",Browse(occlusion,false,false),"Black and white image at export size. White areas of the photo stay untouched and in front.");

            var settings=Stack();
            Row(settings,"ComfyUI address",Pair(new Field(server,34),Button("Test connection",async delegate{await RefreshWorkflows(false);})),null);
            Row(settings,"Export folder",Browse(output,false,true),null);Row(settings,"API workflow (optional)",Browse(workflow,true,false),"Only used to write a bound API JSON file. Nothing is queued.");
            Row(settings,"Longest edge (px)",new Field(edge,34),null);Add(settings,sceneAspect);

            var host=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty};
            foreach(var page in new[]{Page("Sync",sync),Page("Layer materials",material),Page("Background blend",blend),Page("Export settings",settings)}){pages.Add(page);host.Controls.Add(page);}

            actions.Dock=DockStyle.Fill;actions.Margin=Padding.Empty;actions.Padding=new Padding(Theme.S(24),Theme.S(10),0,0);actions.WrapContents=false;
            actions.Controls.Add(Button("Update to ComfyUI",async delegate{await Export(true,false);},ButtonKind.Primary));actions.Controls.Add(Button("Export only",async delegate{await Export(false,false);}));
            actions.Controls.Add(Button("Export folder",delegate{if(last!=null)Open(last.Directory);else if(Directory.Exists(output.Text))Open(output.Text);},ButtonKind.Ghost));actions.Controls.Add(Button("Open ComfyUI",delegate{Read();Open(config.Server);},ButtonKind.Ghost));
            log.Multiline=true;log.ReadOnly=true;log.BorderStyle=BorderStyle.None;log.ScrollBars=ScrollBars.None;log.Dock=DockStyle.Fill;log.BackColor=Theme.Bg;log.ForeColor=Theme.Muted;log.Font=mono;log.TabStop=false;
            var logHost=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty,Padding=new Padding(Theme.S(28),Theme.S(8),Theme.S(8),Theme.S(8))};logHost.Controls.Add(log);

            var main=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,Margin=Padding.Empty};main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            main.RowStyles.Add(new RowStyle(SizeType.Percent,100));main.RowStyles.Add(new RowStyle(SizeType.Absolute,1));main.RowStyles.Add(new RowStyle(SizeType.Absolute,Theme.S(54)));main.RowStyles.Add(new RowStyle(SizeType.Absolute,Theme.S(2)));main.RowStyles.Add(new RowStyle(SizeType.Absolute,Theme.S(92)));
            main.Controls.Add(host,0,0);main.Controls.Add(new Panel{Dock=DockStyle.Fill,BackColor=Theme.Border,Margin=Padding.Empty},0,1);main.Controls.Add(actions,0,2);main.Controls.Add(progress,0,3);main.Controls.Add(logHost,0,4);

            var side=new Panel{Dock=DockStyle.Left,Width=Theme.S(176),BackColor=Theme.Side};
            var menu=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,BackColor=Theme.Side,Padding=new Padding(Theme.S(10),Theme.S(14),Theme.S(10),Theme.S(8))};menu.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            menu.RowStyles.Add(new RowStyle(SizeType.Absolute,Theme.S(46)));menu.Controls.Add(new Label{Text="Rhino to Comfy",Font=Theme.Title(),Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(Theme.S(8),0,0,Theme.S(6)),Margin=Padding.Empty},0,0);
            string[] names={"Sync","Layer materials","Background blend","Export settings"};
            for(int i=0;i<names.Length;i++)
            {
                int index=i;var item=new FlatButton{Text=names[i],Kind=ButtonKind.Nav,Dock=DockStyle.Fill,Margin=new Padding(0,Theme.S(1),0,Theme.S(1)),Font=Theme.Body()};item.Click+=(s,e)=>Go(index);
                nav.Add(item);menu.RowStyles.Add(new RowStyle(SizeType.Absolute,Theme.S(34)));menu.Controls.Add(item,0,i+1);
            }
            menu.RowStyles.Add(new RowStyle(SizeType.Percent,100));menu.Controls.Add(new Panel{Margin=Padding.Empty},0,names.Length+1);
            menu.RowStyles.Add(new RowStyle(SizeType.Absolute,Theme.S(28)));menu.Controls.Add(connection,0,names.Length+2);
            side.Controls.Add(menu);side.Controls.Add(new Panel{Dock=DockStyle.Right,Width=1,BackColor=Theme.Border});
            Controls.Add(main);Controls.Add(side);Go(0);

            if(doc!=null)watcher=new AutoSyncWatcher(doc,()=>autoSync.Checked&&actions.Enabled,SyncSettingsSignature,async()=>await Export(true,true));
            autoSync.CheckedChanged+=(s,e)=>{if(watcher!=null)watcher.Reset();Log(autoSync.Checked?"Auto sync on.":"Auto sync off.");};
            FormClosing+=(sender,args)=>{if(!actions.Enabled){args.Cancel=true;return;}try{Read();config.Save();}catch(Exception e){RhinoApp.WriteLine(e.Message);}};
            FormClosed+=(s,e)=>{if(watcher!=null)watcher.Dispose();tips.Dispose();mono.Dispose();};
            Shown+=async(s,e)=>await RefreshWorkflows(true);
            Log("Ready.");
        }
        void Go(int index){for(int i=0;i<pages.Count;i++){pages[i].Visible=i==index;nav[i].Selected=i==index;}}
        static Panel Page(string title,Control body)
        {
            var page=new Panel{Dock=DockStyle.Fill,Visible=false,Padding=new Padding(Theme.S(28),Theme.S(18),Theme.S(20),Theme.S(12))};body.Dock=DockStyle.Fill;page.Controls.Add(body);
            page.Controls.Add(new Label{Text=title,Font=Theme.Title(),Dock=DockStyle.Top,Height=Theme.S(46),ForeColor=Theme.Text});return page;
        }
        static TableLayoutPanel Stack(){var p=new TableLayoutPanel{AutoScroll=true,ColumnCount=1,Padding=new Padding(0,0,Theme.S(12),0)};p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));return p;}
        static void Add(TableLayoutPanel panel,Control control){int row=panel.RowCount++;panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));panel.Controls.Add(control,0,row);}
        void Row(TableLayoutPanel panel,string text,Control control,string tip)
        {
            var label=new Label{Text=text,AutoSize=true,ForeColor=Theme.Muted,Font=Theme.Small(),Margin=new Padding(Theme.S(2),Theme.S(6),0,Theme.S(4))};Add(panel,label);
            control.Dock=DockStyle.Top;control.Margin=new Padding(0,0,0,Theme.S(8));Add(panel,control);if(tip!=null){tips.SetToolTip(label,tip);Tip(control,tip);}
        }
        Control Tip(Control control,string text){tips.SetToolTip(control,text);foreach(Control child in control.Controls)Tip(child,text);return control;}
        void Check(CheckBox box,string text,bool value,string tip){box.Text=text;box.Checked=value;Theme.Style(box);box.Margin=new Padding(0,Theme.S(3),0,Theme.S(3));tips.SetToolTip(box,tip);}
        static FlatButton Button(string text,Action action,ButtonKind kind=ButtonKind.Secondary)
        {
            var button=new FlatButton{Text=text,Kind=kind,AutoSize=true,Font=kind==ButtonKind.Primary?Theme.Strong():Theme.Body(),Margin=new Padding(0,0,Theme.S(8),0)};
            button.Click+=(s,e)=>{try{action();}catch(Exception error){MessageBox.Show(error.Message,"Rhino to Comfy");}};return button;
        }
        static Control Pair(Control field,Control button)
        {
            var p=new TableLayoutPanel{ColumnCount=2,RowCount=1,Height=Theme.S(34)};p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));p.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));p.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            field.Dock=DockStyle.Fill;field.Margin=new Padding(0,0,Theme.S(8),0);button.Margin=new Padding(0,Theme.S(1),0,0);p.Controls.Add(field,0,0);p.Controls.Add(button,1,0);return p;
        }
        static Control Browse(TextBox text,bool json,bool folder)
        {
            return Pair(new Field(text,34),Button("Browse",delegate{if(folder){using(var dlg=new FolderBrowserDialog()){dlg.SelectedPath=text.Text;if(dlg.ShowDialog()==DialogResult.OK)text.Text=dlg.SelectedPath;}}else using(var dlg=new OpenFileDialog{Filter=json?"API JSON|*.json":"Images|*.png;*.jpg;*.jpeg;*.bmp"}){if(dlg.ShowDialog()==DialogResult.OK)text.Text=dlg.FileName;}}));
        }
        void PaintSwatch(object sender,DataGridViewCellPaintingEventArgs e)
        {
            if(e.RowIndex<0||e.ColumnIndex!=1)return;
            e.Paint(e.CellBounds,DataGridViewPaintParts.Background|DataGridViewPaintParts.SelectionBackground|DataGridViewPaintParts.Border);
            string hex=Convert.ToString(e.Value);Color color=Color.Empty;try{color=ColorTranslator.FromHtml(hex);}catch(Exception){}
            int size=Theme.S(14);var box=new Rectangle(e.CellBounds.X+Theme.S(10),e.CellBounds.Y+(e.CellBounds.Height-size)/2,size,size);
            if(!color.IsEmpty)using(var brush=new SolidBrush(color))e.Graphics.FillRectangle(brush,box);
            using(var pen=new Pen(Theme.Border))e.Graphics.DrawRectangle(pen,box);
            TextRenderer.DrawText(e.Graphics,hex,mono,new Rectangle(box.Right+Theme.S(8),e.CellBounds.Y,e.CellBounds.Width-size-Theme.S(20),e.CellBounds.Height),Theme.Text,TextFormatFlags.VerticalCenter|TextFormatFlags.Left);
            e.Handled=true;
        }
        string SelectedTarget(){return target.SelectedIndex>0?Convert.ToString(target.SelectedItem):"";}
        void FillTargets(List<string> names)
        {
            string current=SelectedTarget();target.BeginUpdate();target.Items.Clear();target.Items.Add(Follow);
            foreach(var name in names)target.Items.Add(name);
            // Keep a saved target visible even if ComfyUI no longer lists it, so nothing changes silently.
            if(current!=""&&!names.Contains(current))target.Items.Add(current);
            target.SelectedIndex=current==""?0:target.Items.IndexOf(current);target.EndUpdate();
        }
        async Task RefreshWorkflows(bool quiet)
        {
            try
            {
                List<string> names;JObject status;
                using(var client=new ComfyClient(server.Text.Trim())){client.Timeout=TimeSpan.FromSeconds(6);names=await client.ListWorkflows();status=await client.FrontendStatus();}
                if(IsDisposed)return;FillTargets(names);connection.Set(Theme.Ok,"Connected");targetStatus.Text=ComfyClient.DescribeStatus(status,SelectedTarget());
                if(!quiet)Log("ComfyUI connected, "+names.Count+" workflows.");
            }
            catch(Exception e){if(IsDisposed)return;connection.Set(Theme.Bad,"Cannot connect");targetStatus.Text=" ";if(!quiet)Log(e.Message);}
        }
        // The ComfyUI window polls every two seconds; wait for it to report the switch and the binding.
        async Task ConfirmSync()
        {
            try
            {
                string wanted=config.TargetWorkflow,text=null;
                for(int attempt=0;attempt<4;attempt++)
                {
                    await Task.Delay(2000);if(IsDisposed)return;JObject status;
                    using(var client=new ComfyClient(config.Server)){client.Timeout=TimeSpan.FromSeconds(6);status=await client.FrontendStatus();}
                    if(IsDisposed)return;text=ComfyClient.DescribeStatus(status,wanted);
                    if(status!=null&&(string)status["state"]=="bound"&&(wanted==""||(string)status["active"]==wanted))break;
                }
                targetStatus.Text=text;Log(text);
            }
            catch(Exception e){if(!IsDisposed)Log(e.Message);}
        }
        void FillLayers(){layers.Rows.Clear();foreach(var l in config.Layers){int i=layers.Rows.Add(l.Name,l.Color,l.Material);layers.Rows[i].Tag=l;}}
        void Read()
        {
            layers.EndEdit();foreach(DataGridViewRow row in layers.Rows){var l=(LayerRule)row.Tag;l.Material=Convert.ToString(row.Cells[2].Value).Trim();if(string.IsNullOrWhiteSpace(l.Material))throw new ArgumentException("Enter a target material for layer: "+l.Name);}
            config.Server=server.Text.Trim();config.Output=output.Text.Trim();config.Workflow=workflow.Text.Trim();config.TargetWorkflow=SelectedTarget();config.LongEdge=(int)edge.Value;config.LockSceneAspect=sceneAspect.Checked;config.SelectedOnly=selected.Checked;config.ProductMode=tryon.Checked;config.MatchWallpaperAspect=wallpaperAspect.Checked;config.DetailPriority=detailPriority.Checked;config.AutoEditRegion=adaptiveMask.Checked;config.WearInstructions=wear.Text;config.MaskPadding=(int)padding.Value;config.OcclusionMask=occlusion.Text.Trim();config.AutoSync=autoSync.Checked;
        }
        void ApplyColors(){uint undo=doc.BeginUndoRecord("Rhino to Comfy layer color codes");try{foreach(var rule in config.Layers){var layer=doc.Layers.FindId(new Guid(rule.Id));if(layer!=null){layer.Color=ColorTranslator.FromHtml(rule.Color);}}}finally{doc.EndUndoRecord(undo);}doc.Views.Redraw();Log("ID colors applied to the layer display colors. Undo is supported.");}
        void AssignHighContrastColors()
        {
            int slot=1;foreach(var rule in config.Layers.OrderBy(l=>l.Index))rule.Color=Raster.Hex(Raster.Palette(slot++));
            ApplyColors();config.Scan(doc);FillLayers();config.Save();Log("Contrast Material ID colors assigned and HEX mapping updated.");
        }
        void ApplyMaterials()
        {
            uint undo=doc.BeginUndoRecord("Rhino to Comfy layer materials");
            try{foreach(var rule in config.Layers)
            {
                var layer=doc.Layers.FindId(new Guid(rule.Id));if(layer==null)continue;
                string name="RhinoAI::"+rule.Id;var found=doc.Materials.FirstOrDefault(m=>m.Name==name);
                var mat=new Rhino.DocObjects.Material{Name=name,DiffuseColor=Color.FromArgb(195,190,182),Shine=25};
                string hint=rule.Material.ToLowerInvariant();
                if(hint.Contains("gold")||hint.Contains("黄金")){mat.DiffuseColor=Color.FromArgb(212,175,55);mat.Shine=180;mat.Reflectivity=0.8;}
                else if(hint.Contains("metal")||hint.Contains("金属")){mat.DiffuseColor=Color.FromArgb(190,194,200);mat.Shine=130;mat.Reflectivity=0.65;}
                else if(hint.Contains("glass")||hint.Contains("gem")||hint.Contains("玻璃")||hint.Contains("宝石")){mat.DiffuseColor=Color.FromArgb(220,235,240);mat.Transparency=0.85;mat.Shine=220;mat.IndexOfRefraction=hint.Contains("gem")?2.42:1.5;}
                else if(hint.Contains("ceramic")||hint.Contains("陶瓷")){mat.DiffuseColor=Color.FromArgb(238,231,214);mat.Shine=110;}
                else if(hint.Contains("oak")||hint.Contains("木")){mat.DiffuseColor=Color.FromArgb(179,139,90);mat.Shine=35;}
                int idx=found==null?doc.Materials.Add(mat):found.MaterialIndex;if(found!=null)doc.Materials.Modify(mat,idx,true);layer.RenderMaterialIndex=idx;
            }}finally{doc.EndUndoRecord(undo);}doc.Views.Redraw();Log("Basic layer materials created and assigned. Undo is supported. Per-object materials are kept, and the table text still drives the AI material description.");
        }
        string SyncSettingsSignature()
        {
            return string.Join("|",new[]{server.Text,output.Text,workflow.Text,Convert.ToString(target.SelectedItem),edge.Value.ToString(),sceneAspect.Checked.ToString(),selected.Checked.ToString(),tryon.Checked.ToString(),wallpaperAspect.Checked.ToString(),detailPriority.Checked.ToString(),adaptiveMask.Checked.ToString(),wear.Text,padding.Value.ToString(),occlusion.Text,AutoSyncWatcher.FileStamp(occlusion.Text),string.Join(";",layers.Rows.Cast<DataGridViewRow>().Select(r=>string.Join("|",r.Cells.Cast<DataGridViewCell>().Select(c=>Convert.ToString(c.Value)))))});
        }
        async Task Export(bool send,bool automatic)
        {
            actions.Enabled=false;try
            {
                if(RhinoDoc.ActiveDoc==null||RhinoDoc.ActiveDoc.RuntimeSerialNumber!=doc.RuntimeSerialNumber)throw new InvalidOperationException("The active document changed. Close this panel and run Rhino2Comfy again.");
                Read();config.Save();Log("Reading viewport and geometry…");var snapshot=Exporter.Capture(doc,config);Log("Rendering "+snapshot.Camera.Width+" × "+snapshot.Camera.Height+" control images…");
                last=await Task.Run(()=>Exporter.Render(snapshot,config,p=>{if(!IsDisposed)BeginInvoke((Action)(()=>progress.Value=p));}));
                Log("Exported: "+last.Directory);foreach(var warning in last.Warnings)Log(warning);
                if(send)
                {
                    Log("Updating "+config.Server);
                    // Automatic syncs never pull the ComfyUI window to another file; only an explicit click does.
                    using(var client=new ComfyClient(config.Server))Log(await client.Send(last,config,!automatic));
                    config.Save();connection.Set(Theme.Ok,"Connected");var confirm=ConfirmSync();
                }
            }
            catch(Exception e){Log("Not completed: "+e.Message+ (last==null?"":"\r\nExported files were kept: "+last.Directory));if(automatic)autoSync.Checked=false;}
            finally{actions.Enabled=true;}
        }
        void Log(string message){log.AppendText(DateTime.Now.ToString("HH:mm:ss")+"  "+message+Environment.NewLine);}
        static void Open(string path){Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}
    }
}
