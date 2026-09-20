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

[assembly: System.Reflection.AssemblyTitle("Rhino AI Control Bridge")]
[assembly: System.Reflection.AssemblyVersion("0.21.0.0")]
[assembly: Guid("66587CA6-F24F-49B2-83C1-8E616089B2C4")]

namespace RhinoAI
{
    public sealed class BridgePlugin : PlugIn { public BridgePlugin(){} }
    public sealed class OpenBridge : Command
    {
        static BridgeWindow window;
        public override string EnglishName {get{return "RhinoAI";}}
        protected override Result RunCommand(RhinoDoc doc,RunMode mode)
        {
            if(window!=null&&!window.IsDisposed&&window.DocumentSerial!=doc.RuntimeSerialNumber){window.Close();window=null;}
            if(window==null||window.IsDisposed)window=new BridgeWindow(doc);
            window.Show();window.BringToFront();return Result.Success;
        }
    }
    public sealed class ExportCommand : Command
    {
        public override string EnglishName {get{return "RhinoAIExport";}}
        protected override Result RunCommand(RhinoDoc doc,RunMode mode)
        {
            try{var c=Config.Load();var result=Exporter.Render(Exporter.Capture(doc,c),c,null);c.Save();RhinoApp.WriteLine("Export: "+result.Directory);return Result.Success;}
            catch(Exception e){RhinoApp.WriteLine(e.Message);return Result.Failure;}
        }
    }
    public sealed class BridgeWindow : Form
    {
        const string Follow="跟随 ComfyUI 当前窗口";
        readonly RhinoDoc doc;
        readonly Config config;
        readonly TextBox server=new TextBox(),output=new TextBox(),workflow=new TextBox(),wear=new TextBox(),occlusion=new TextBox();
        readonly ComboBox product=new ComboBox(),target=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList};
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
            Text="Rhino AI";ClientSize=new Size(Theme.S(920),Theme.S(640));MinimumSize=new Size(Theme.S(760),Theme.S(540));StartPosition=FormStartPosition.CenterScreen;
            Font=Theme.Body();BackColor=Theme.Bg;ForeColor=Theme.Text;
            progress=new ThinProgress{Dock=DockStyle.Fill,Margin=Padding.Empty};connection=new StatusDot{Dock=DockStyle.Fill,Font=Theme.Small(),Margin=Padding.Empty};connection.Set(Theme.Faint,"未连接");

            server.Text=config.Server;output.Text=config.Output;workflow.Text=config.Workflow;
            edge.Minimum=256;edge.Maximum=4096;edge.Increment=256;edge.Value=config.LongEdge;
            target.Items.Add(Follow);if(!string.IsNullOrWhiteSpace(config.TargetWorkflow))target.Items.Add(config.TargetWorkflow);target.SelectedIndex=target.Items.Count-1;
            Check(sceneAspect,"固定宽屏画幅 1024 × 589",config.LockSceneAspect,"普通场景使用固定画幅，Rhino 窗口大小变化不影响输出。");
            Check(selected,"仅导出选中对象",config.SelectedOnly,"产品佩戴模式建议开启。");
            Check(autoSync,"场景变化后自动上传",config.AutoSync,"只上传图片，不修改 prompt，不触发生成，也不会切换 ComfyUI 窗口。");
            Check(tryon,"启用 Wallpaper 产品佩戴模式",config.ProductMode,"用当前视口的 Wallpaper 照片作为人物参考，导出 placement、蒙版和佩戴约束。");
            Check(wallpaperAspect,"按 Wallpaper 原图比例输出",config.MatchWallpaperAspect,"Portrait 参考图会裁掉视口留白，相机与透视保持不变。");
            Check(detailPriority,"细节优先",config.DetailPriority,"产品在画面中很小时使用放大的几何控制图，最多 14 张。");
            Check(adaptiveMask,"自动扩大替换区域",config.AutoEditRegion,"覆盖模特原有饰品；下方像素值作为最小扩张。");

            var sync=Stack();
            var targetRow=Pair(new Field(target,34),Button("刷新",async delegate{await RefreshWorkflows(false);}));
            Row(sync,"目标工作流",targetRow,"点击“更新到 ComfyUI”时，ComfyUI 会切到这个文件，再更新其中的 Batch Images。");
            targetStatus.AutoSize=true;targetStatus.ForeColor=Theme.Muted;targetStatus.Font=Theme.Small();targetStatus.Margin=new Padding(Theme.S(2),Theme.S(2),0,Theme.S(18));targetStatus.Text=" ";Add(sync,targetStatus);
            Add(sync,selected);Add(sync,autoSync);

            var material=new TableLayoutPanel{ColumnCount=1,RowCount=2};material.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));material.RowStyles.Add(new RowStyle(SizeType.AutoSize));material.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            var materialActions=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,Margin=new Padding(0,0,0,Theme.S(10))};
            materialActions.Controls.Add(Tip(Button("重新读取",delegate{Read();config.Scan(doc);FillLayers();}),"从 Rhino 重新读取图层和颜色。"));
            materialActions.Controls.Add(Tip(Button("分配高对比颜色",AssignHighContrastColors),"给每个图层分配易区分的 Material ID 颜色，可 Undo。"));
            materialActions.Controls.Add(Tip(Button("建立图层材质",delegate{Read();ApplyMaterials();}),"按表格中的目标材质建立 Rhino 基础材质，可 Undo。"));
            material.Controls.Add(materialActions,0,0);
            layers.Dock=DockStyle.Fill;layers.AllowUserToAddRows=false;layers.AllowUserToDeleteRows=false;layers.AllowUserToResizeRows=false;layers.RowHeadersVisible=false;layers.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells;Theme.Style(layers);
            layers.Columns.Add(new DataGridViewTextBoxColumn{Name="Layer",HeaderText="图层",ReadOnly=true,Width=Theme.S(220)});layers.Columns.Add(new DataGridViewTextBoxColumn{Name="Color",HeaderText="编码色",ReadOnly=true,Width=Theme.S(130)});layers.Columns.Add(new DataGridViewTextBoxColumn{Name="Material",HeaderText="目标材质",AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill});
            layers.CellPainting+=PaintSwatch;
            var gridCard=new Card{Dock=DockStyle.Fill,Margin=Padding.Empty,Padding=new Padding(Theme.S(2))};gridCard.Controls.Add(layers);material.Controls.Add(gridCard,0,1);FillLayers();

            var productForm=Stack();
            product.Items.AddRange(new object[]{"ring / 戒指","necklace / 项链","earrings / 耳环","bracelet / 手链","other product / 其他产品"});product.Text=config.Product;
            wear.Multiline=true;wear.ScrollBars=ScrollBars.Vertical;wear.Text=config.WearInstructions;padding.Minimum=0;padding.Maximum=256;padding.Value=config.MaskPadding;occlusion.Text=config.OcclusionMask;
            Add(productForm,tryon);Add(productForm,wallpaperAspect);Add(productForm,detailPriority);Add(productForm,adaptiveMask);
            Row(productForm,"产品类型",new Field(product,34),null);Row(productForm,"佩戴关系与保留要求",new Field(wear,96),null);Row(productForm,"最小扩张（像素）",new Field(padding,34),null);
            Row(productForm,"遮挡保护蒙版（可选）",Browse(occlusion,false,false),"与导出图同尺寸的黑白图：白色保护人物，黑色不保护。");

            var settings=Stack();
            Row(settings,"ComfyUI 地址",Pair(new Field(server,34),Button("测试连接",async delegate{await RefreshWorkflows(false);})),null);
            Row(settings,"导出目录",Browse(output,false,true),null);Row(settings,"API 工作流（可选）",Browse(workflow,true,false),"仅用于生成绑定好的 API JSON 文件，不会提交生成。");
            Row(settings,"输出最长边",new Field(edge,34),null);Add(settings,sceneAspect);

            var host=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty};
            foreach(var page in new[]{Page("同步",sync),Page("图层材质",material),Page("产品佩戴",productForm),Page("导出设置",settings)}){pages.Add(page);host.Controls.Add(page);}

            actions.Dock=DockStyle.Fill;actions.Margin=Padding.Empty;actions.Padding=new Padding(Theme.S(24),Theme.S(10),0,0);actions.WrapContents=false;
            actions.Controls.Add(Button("更新到 ComfyUI",async delegate{await Export(true,false);},ButtonKind.Primary));actions.Controls.Add(Button("仅导出",async delegate{await Export(false,false);}));
            actions.Controls.Add(Button("导出文件夹",delegate{if(last!=null)Open(last.Directory);else if(Directory.Exists(output.Text))Open(output.Text);},ButtonKind.Ghost));actions.Controls.Add(Button("打开 ComfyUI",delegate{Read();Open(config.Server);},ButtonKind.Ghost));
            log.Multiline=true;log.ReadOnly=true;log.BorderStyle=BorderStyle.None;log.ScrollBars=ScrollBars.None;log.Dock=DockStyle.Fill;log.BackColor=Theme.Bg;log.ForeColor=Theme.Muted;log.Font=mono;log.TabStop=false;
            var logHost=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty,Padding=new Padding(Theme.S(28),Theme.S(8),Theme.S(8),Theme.S(8))};logHost.Controls.Add(log);

            var main=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,Margin=Padding.Empty};main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            main.RowStyles.Add(new RowStyle(SizeType.Percent,100));main.RowStyles.Add(new RowStyle(SizeType.Absolute,1));main.RowStyles.Add(new RowStyle(SizeType.Absolute,Theme.S(54)));main.RowStyles.Add(new RowStyle(SizeType.Absolute,Theme.S(2)));main.RowStyles.Add(new RowStyle(SizeType.Absolute,Theme.S(92)));
            main.Controls.Add(host,0,0);main.Controls.Add(new Panel{Dock=DockStyle.Fill,BackColor=Theme.Border,Margin=Padding.Empty},0,1);main.Controls.Add(actions,0,2);main.Controls.Add(progress,0,3);main.Controls.Add(logHost,0,4);

            var side=new Panel{Dock=DockStyle.Left,Width=Theme.S(176),BackColor=Theme.Side};
            var menu=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,BackColor=Theme.Side,Padding=new Padding(Theme.S(10),Theme.S(14),Theme.S(10),Theme.S(8))};menu.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            menu.RowStyles.Add(new RowStyle(SizeType.Absolute,Theme.S(46)));menu.Controls.Add(new Label{Text="Rhino AI",Font=Theme.Title(),Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(Theme.S(8),0,0,Theme.S(6)),Margin=Padding.Empty},0,0);
            string[] names={"同步","图层材质","产品佩戴","导出设置"};
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
            autoSync.CheckedChanged+=(s,e)=>{if(watcher!=null)watcher.Reset();Log(autoSync.Checked?"自动同步已开启。":"自动同步已关闭。");};
            FormClosing+=(sender,args)=>{if(!actions.Enabled){args.Cancel=true;return;}try{Read();config.Save();}catch(Exception e){RhinoApp.WriteLine(e.Message);}};
            FormClosed+=(s,e)=>{if(watcher!=null)watcher.Dispose();tips.Dispose();mono.Dispose();};
            Shown+=async(s,e)=>await RefreshWorkflows(true);
            Log("准备就绪。");
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
            button.Click+=(s,e)=>{try{action();}catch(Exception error){MessageBox.Show(error.Message,"Rhino AI");}};return button;
        }
        static Control Pair(Control field,Control button)
        {
            var p=new TableLayoutPanel{ColumnCount=2,RowCount=1,Height=Theme.S(34)};p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));p.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));p.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            field.Dock=DockStyle.Fill;field.Margin=new Padding(0,0,Theme.S(8),0);button.Margin=new Padding(0,Theme.S(1),0,0);p.Controls.Add(field,0,0);p.Controls.Add(button,1,0);return p;
        }
        static Control Browse(TextBox text,bool json,bool folder)
        {
            return Pair(new Field(text,34),Button("浏览",delegate{if(folder){using(var dlg=new FolderBrowserDialog()){dlg.SelectedPath=text.Text;if(dlg.ShowDialog()==DialogResult.OK)text.Text=dlg.SelectedPath;}}else using(var dlg=new OpenFileDialog{Filter=json?"API JSON|*.json":"Images|*.png;*.jpg;*.jpeg;*.bmp"}){if(dlg.ShowDialog()==DialogResult.OK)text.Text=dlg.FileName;}}));
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
                if(IsDisposed)return;FillTargets(names);connection.Set(Theme.Ok,"已连接");targetStatus.Text=ComfyClient.DescribeStatus(status,SelectedTarget());
                if(!quiet)Log("ComfyUI 连接正常，"+names.Count+" 个工作流。");
            }
            catch(Exception e){if(IsDisposed)return;connection.Set(Theme.Bad,"无法连接");targetStatus.Text=" ";if(!quiet)Log(e.Message);}
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
            layers.EndEdit();foreach(DataGridViewRow row in layers.Rows){var l=(LayerRule)row.Tag;l.Material=Convert.ToString(row.Cells[2].Value).Trim();if(string.IsNullOrWhiteSpace(l.Material))throw new ArgumentException("请填写目标材质："+l.Name);}
            config.Server=server.Text.Trim();config.Output=output.Text.Trim();config.Workflow=workflow.Text.Trim();config.TargetWorkflow=SelectedTarget();config.LongEdge=(int)edge.Value;config.LockSceneAspect=sceneAspect.Checked;config.SelectedOnly=selected.Checked;config.ProductMode=tryon.Checked;config.MatchWallpaperAspect=wallpaperAspect.Checked;config.DetailPriority=detailPriority.Checked;config.AutoEditRegion=adaptiveMask.Checked;config.Product=product.Text;config.WearInstructions=wear.Text;config.MaskPadding=(int)padding.Value;config.OcclusionMask=occlusion.Text.Trim();config.AutoSync=autoSync.Checked;
        }
        void ApplyColors(){uint undo=doc.BeginUndoRecord("Rhino AI layer color codes");try{foreach(var rule in config.Layers){var layer=doc.Layers.FindId(new Guid(rule.Id));if(layer!=null){layer.Color=ColorTranslator.FromHtml(rule.Color);}}}finally{doc.EndUndoRecord(undo);}doc.Views.Redraw();Log("编码色已应用到图层显示颜色，可用 Undo 撤销。材质定义请在表格中编辑。");}
        void AssignHighContrastColors()
        {
            int slot=1;foreach(var rule in config.Layers.OrderBy(l=>l.Index))rule.Color=Raster.Hex(Raster.Palette(slot++));
            ApplyColors();config.Scan(doc);FillLayers();config.Save();Log("已按图层分配高对比 Material ID 颜色，并同步更新 HEX 映射。");
        }
        void ApplyMaterials()
        {
            uint undo=doc.BeginUndoRecord("Rhino AI layer materials");
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
            }}finally{doc.EndUndoRecord(undo);}doc.Views.Redraw();Log("已建立图层基础材质并分配，可 Undo。按对象指定的材质保留；复杂纹理请在 Rhino 材质编辑器完善。AI 详细材质仍以表格描述为准。");
        }
        string SyncSettingsSignature()
        {
            return string.Join("|",new[]{server.Text,output.Text,workflow.Text,Convert.ToString(target.SelectedItem),edge.Value.ToString(),sceneAspect.Checked.ToString(),selected.Checked.ToString(),tryon.Checked.ToString(),wallpaperAspect.Checked.ToString(),detailPriority.Checked.ToString(),adaptiveMask.Checked.ToString(),product.Text,wear.Text,padding.Value.ToString(),occlusion.Text,AutoSyncWatcher.FileStamp(occlusion.Text),string.Join(";",layers.Rows.Cast<DataGridViewRow>().Select(r=>string.Join("|",r.Cells.Cast<DataGridViewCell>().Select(c=>Convert.ToString(c.Value)))))});
        }
        async Task Export(bool send,bool automatic)
        {
            actions.Enabled=false;try
            {
                if(RhinoDoc.ActiveDoc==null||RhinoDoc.ActiveDoc.RuntimeSerialNumber!=doc.RuntimeSerialNumber)throw new InvalidOperationException("当前文档已切换，请关闭面板并重新运行 RhinoAI。");
                Read();config.Save();Log("读取当前视口和几何…");var snapshot=Exporter.Capture(doc,config);Log("正在生成 "+snapshot.Camera.Width+" × "+snapshot.Camera.Height+" 控制图…");
                last=await Task.Run(()=>Exporter.Render(snapshot,config,p=>{if(!IsDisposed)BeginInvoke((Action)(()=>progress.Value=p));}));
                Log("已导出："+last.Directory);foreach(var warning in last.Warnings)Log(warning);
                if(send)
                {
                    Log("正在更新到 "+config.Server);
                    // Automatic syncs never pull the ComfyUI window to another file; only an explicit click does.
                    using(var client=new ComfyClient(config.Server))Log(await client.Send(last,config,!automatic));
                    config.Save();connection.Set(Theme.Ok,"已连接");var confirm=ConfirmSync();
                }
            }
            catch(Exception e){Log("未完成："+e.Message+ (last==null?"":"\r\n已导出的文件仍保留："+last.Directory));if(automatic)autoSync.Checked=false;}
            finally{actions.Enabled=true;}
        }
        void Log(string message){log.AppendText(DateTime.Now.ToString("HH:mm:ss")+"  "+message+Environment.NewLine);}
        static string ControlPrompt(bool productMode,string materials)
        {
            string scene=@"[Write your desired result, environment, lighting and style here.]

Use the supplied images as one coordinated control stack. They are not independent style inspirations.

Image 1 (shape_lock) is the PRIMARY GEOMETRY AUTHORITY. Preserve its exact object count, camera, composition, silhouette, openings, gaps, overlaps, part boundaries, relative positions and proportions. Do not merge, simplify, add, remove, bend or redesign any part.
Image 2 (rendered) describes surface volume and curvature only. Ignore its white material, lighting and background.
Image 3 (depth) controls front-to-back order and relative distance.
Image 4 (inverse depth) only confirms the same depth relationships; it must not change the geometry.
Image 5 (edges) controls visible creases, holes and internal part boundaries.
Image 6 (silhouette) controls the exact outer contour and negative spaces.
Image 7 (normal) controls surface orientation and curvature direction.
Image 8 (mask) defines the exact foreground occupancy.
Image 9 (material_id) divides the model into material regions by flat color. Never reproduce these ID colors in the final image; apply the material mapping below to the corresponding regions only.

If any inputs conflict, use this priority for geometry: Image 1 > Images 6 and 5 > Image 8 > Images 3, 4 and 7 > Image 2. Material instructions may change appearance only and must never change geometry.
Change only the requested materials, lighting, environment and background. Keep the CAD form and camera unchanged.";
            string product=@"[Write your desired result, environment, lighting and style here.]

Use the supplied images as one coordinated product-placement control stack. They are not independent style inspirations.

Image 1 (reference) is the AUTHORITY for the person, anatomy, pose, identity, camera, composition, clothing and background. Preserve all pixels outside the editable product area.
Image 2 (shape_lock) is the PRIMARY PRODUCT-GEOMETRY AUTHORITY. Preserve the exact product silhouette, openings, gaps, component count, part boundaries and proportions. Do not merge, simplify, add, remove, bend or redesign any part.
Image 3 (rendered) describes product volume and curvature only. Ignore its white material, lighting and background.
Image 4 (placement) controls the exact product position, scale and rotation relative to the person.
Image 5 (depth) controls front-to-back order within the product.
Image 6 (inverse depth) only confirms the same product-depth relationships.
Image 7 (edges) controls visible creases, holes and internal product boundaries.
Image 8 (silhouette) controls the exact product outer contour and negative spaces.
Image 9 (normal) controls product surface orientation and curvature direction.
Image 10 (mask) defines product occupancy.
Image 11 (material_id) divides the product into material regions by flat color. Never reproduce these ID colors; apply the material mapping below to the matching regions only.
Image 12 (product_mask) confirms the exact product boundary.
Image 13 (inpaint_mask): white is editable and black must be preserved.
If Image 14 is present, it is the occlusion protection mask: white human/body pixels must remain unchanged and in front of the product where appropriate.

If inputs conflict, use this priority: reference outside the edit area > placement > shape_lock > product/mask/silhouette/edges > depth/normal > rendered. Material instructions may change appearance only and must never change product geometry, placement or anatomy.";
            return (productMode?product:scene)+"\r\n\r\nMaterial mapping:\r\n"+(materials??"");
        }
        static void Open(string path){Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}
    }
}
