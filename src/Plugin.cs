using System;
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

[assembly: System.Reflection.AssemblyTitle("Rhino AI Control Bridge")]
[assembly: System.Reflection.AssemblyVersion("0.17.0.0")]
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
        readonly RhinoDoc doc;
        readonly Config config;
        readonly TextBox server=new TextBox(),output=new TextBox(),workflow=new TextBox(),wear=new TextBox(),occlusion=new TextBox();
        readonly ComboBox product=new ComboBox();
        readonly NumericUpDown edge=new NumericUpDown(),padding=new NumericUpDown();
        readonly CheckBox selected=new CheckBox(),tryon=new CheckBox(),autoSync=new CheckBox(),detailPriority=new CheckBox(),adaptiveMask=new CheckBox(),wallpaperAspect=new CheckBox();
        readonly AutoSyncWatcher watcher;
        readonly DataGridView layers=new DataGridView();
        readonly TextBox log=new TextBox();
        readonly ProgressBar progress=new ProgressBar();
        readonly FlowLayoutPanel actions=new FlowLayoutPanel();
        ExportResult last;
        public uint DocumentSerial {get{return doc.RuntimeSerialNumber;}}
        public BridgeWindow(RhinoDoc document)
        {
            doc=document;config=Config.Load();config.Scan(doc);
            Text="Rhino AI · 控制图 / Color Code / 产品佩戴";Width=1040;Height=820;MinimumSize=new Size(860,660);StartPosition=FormStartPosition.CenterScreen;
            Font=new Font("Microsoft YaHei UI",9);BackColor=Color.FromArgb(245,246,248);
            var tabs=new TabControl{Dock=DockStyle.Fill};
            var settings=new TabPage("导出与 ComfyUI");var material=new TabPage("图层材质 · Color Code");var productTab=new TabPage("产品佩戴 · Wallpaper");
            tabs.TabPages.AddRange(new[]{settings,material,productTab});
            var form=Layout();settings.Controls.Add(form);
            server.Text=config.Server;output.Text=config.Output;workflow.Text=config.Workflow;
            edge.Minimum=256;edge.Maximum=4096;edge.Increment=256;edge.Value=config.LongEdge;
            selected.Text="仅导出当前选中对象（产品模式建议开启）";selected.AutoSize=true;selected.Checked=config.SelectedOnly;
            autoSync.Text="场景变化后自动上传图片（不会修改 ComfyUI prompt；默认关闭）";autoSync.AutoSize=true;autoSync.Checked=config.AutoSync;
            Row(form,"ComfyUI 地址",server);Row(form,"导出目录",Browse(output,false,true));Row(form,"API 工作流（可选）",Browse(workflow,true,false));Row(form,"输出最长边",edge);Row(form,"对象范围",selected);Row(form,"自动同步",autoSync);
            Row(form,"说明",new Label{AutoSize=true,MaximumSize=new Size(680,0),Text="每次同步会一起更新 Batch 图片和对应的 Image 编号/用途说明，并保留你自己写的 prompt。不会提交生成任务。\nshape_lock 是主要造型参考；生成请在 ComfyUI 手动点击 Run。"});
            var materialLayout=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2,ColumnCount=1};materialLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,60));materialLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));material.Controls.Add(materialLayout);
            var materialActions=new FlowLayoutPanel{Dock=DockStyle.Fill};materialActions.Controls.Add(Button("重新读取图层",delegate{Read();config.Scan(doc);FillLayers();}));materialActions.Controls.Add(Button("按表格建立图层材质",delegate{Read();ApplyMaterials();}));materialActions.Controls.Add(new Label{Text="只需填写目标材质；图层与编码色由插件自动管理，并自动写入 ComfyUI prompt。",AutoSize=true,Padding=new Padding(6,12,0,0)});materialLayout.Controls.Add(materialActions,0,0);
            layers.Dock=DockStyle.Fill;layers.AllowUserToAddRows=false;layers.AllowUserToDeleteRows=false;layers.RowHeadersVisible=false;layers.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells;layers.DefaultCellStyle.WrapMode=DataGridViewTriState.True;
            layers.Columns.Add(new DataGridViewTextBoxColumn{Name="Layer",HeaderText="Rhino 图层（自动）",ReadOnly=true,Width=240});layers.Columns.Add(new DataGridViewTextBoxColumn{Name="Color",HeaderText="编码色（自动）",ReadOnly=true,Width=150});layers.Columns.Add(new DataGridViewTextBoxColumn{Name="Material",HeaderText="目标材质（只编辑这里）",AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill});materialLayout.Controls.Add(layers,0,1);FillLayers();
            var productForm=Layout();productTab.Controls.Add(productForm);tryon.Text="启用 Wallpaper 产品佩戴模式";tryon.AutoSize=true;tryon.Checked=config.ProductMode;
            wallpaperAspect.Text="按 Wallpaper 原图比例输出（Portrait 自动裁切视口留白）";wallpaperAspect.AutoSize=true;wallpaperAspect.Checked=config.MatchWallpaperAspect;
            detailPriority.Text="细节优先（使用像素放大控制图；同步到 Batch 最多 14 张）";detailPriority.AutoSize=true;detailPriority.Checked=config.DetailPriority;
            adaptiveMask.Text="自动扩大替换区域（覆盖模特原有饰品；填写像素值作为最小值）";adaptiveMask.AutoSize=true;adaptiveMask.Checked=config.AutoEditRegion;
            product.Items.AddRange(new object[]{"ring / 戒指","necklace / 项链","earrings / 耳环","bracelet / 手链","other product / 其他产品"});product.Text=config.Product;
            wear.Multiline=true;wear.Height=180;wear.Text=config.WearInstructions;padding.Minimum=0;padding.Maximum=256;padding.Value=config.MaskPadding;occlusion.Text=config.OcclusionMask;
            Row(productForm,"佩戴参考",tryon);Row(productForm,"画幅比例",wallpaperAspect);Row(productForm,"输入策略",detailPriority);Row(productForm,"产品类型",product);Row(productForm,"佩戴关系与保留要求",wear);Row(productForm,"编辑范围",adaptiveMask);Row(productForm,"最小扩张（像素）",padding);Row(productForm,"遮挡保护蒙版（可选）",Browse(occlusion,false,false));
            Row(productForm,"使用步骤",new Label{AutoSize=true,MaximumSize=new Size(720,0),Text="1. 在当前 Rhino 视口用 Wallpaper 放入人物参考照片。\n2. 移动 / 旋转产品并调整视角，使戒指或项链与照片位置对齐。\n3. 选择产品对象，在导出页开启“仅导出当前选中对象”。\n4. 导出：获得原生对齐参考图、位置合成图、产品蒙版、编辑蒙版、几何控制图和佩戴提示词。\n\n人物遮挡不会自动精确识别。可加入与导出图同尺寸的黑白遮挡蒙版：白色保护人物，黑色不保护。ComfyUI 读取编辑蒙版时用 ImageToMask 的 red 通道。"});
            actions.Dock=DockStyle.Bottom;actions.Height=52;actions.Padding=new Padding(8);actions.Controls.Add(Button("导出控制图",async delegate{await Export(false,false);}));actions.Controls.Add(Button("更新到 ComfyUI",async delegate{await Export(true,false);}));actions.Controls.Add(Button("测试连接",async delegate{try{Read();using(var client=new ComfyClient(config.Server))await client.Check();Log("ComfyUI 连接正常。");}catch(Exception e){Log(e.Message);}}));actions.Controls.Add(Button("查看导出文件夹",delegate{if(last!=null)Open(last.Directory);}));actions.Controls.Add(Button("打开 ComfyUI",delegate{Read();Open(config.Server);}));
            log.Multiline=true;log.ReadOnly=true;log.ScrollBars=ScrollBars.Vertical;log.Dock=DockStyle.Bottom;log.Height=110;progress.Dock=DockStyle.Bottom;progress.Height=8;
            Controls.Add(tabs);Controls.Add(actions);Controls.Add(progress);Controls.Add(log);
            watcher=new AutoSyncWatcher(doc,()=>autoSync.Checked&&actions.Enabled,SyncSettingsSignature,async()=>await Export(true,true));
            autoSync.CheckedChanged+=(s,e)=>{watcher.Reset();Log(autoSync.Checked?"自动同步已开启。修改产品、视角、选择或参考图后自动更新；请手动生成。":"自动同步已关闭。");};
            FormClosing+=(sender,args)=>{if(!actions.Enabled){args.Cancel=true;return;}try{Read();config.Save();}catch(Exception e){RhinoApp.WriteLine(e.Message);}};
            FormClosed+=(s,e)=>watcher.Dispose();
            Log("准备就绪。当前视角所有控制图保持同尺寸。图层材质推测请先检查。");
        }
        new static TableLayoutPanel Layout(){var p=new TableLayoutPanel{Dock=DockStyle.Fill,AutoScroll=true,ColumnCount=2,Padding=new Padding(20)};p.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,240));p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));return p;}
        static void Row(TableLayoutPanel panel,string text,Control control){int row=panel.RowCount++;panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));panel.Controls.Add(new Label{Text=text,AutoSize=true,Padding=new Padding(0,9,0,0)},0,row);control.Dock=DockStyle.Top;control.Margin=new Padding(4,6,4,12);panel.Controls.Add(control,1,row);}
        static Button Button(string text,Action action){var button=new Button{Text=text,AutoSize=true,Height=32,Padding=new Padding(5)};button.Click+=(s,e)=>{try{action();}catch(Exception error){MessageBox.Show(error.Message,"Rhino AI");}};return button;}
        static Control Browse(TextBox text,bool json,bool folder)
        {
            var p=new TableLayoutPanel{ColumnCount=2,Height=36,Dock=DockStyle.Top};p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));p.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,60));text.Dock=DockStyle.Fill;p.Controls.Add(text,0,0);
            p.Controls.Add(Button("…",delegate{if(folder){using(var dlg=new FolderBrowserDialog()){dlg.SelectedPath=text.Text;if(dlg.ShowDialog()==DialogResult.OK)text.Text=dlg.SelectedPath;}}else using(var dlg=new OpenFileDialog{Filter=json?"API JSON|*.json":"Images|*.png;*.jpg;*.jpeg;*.bmp"}){if(dlg.ShowDialog()==DialogResult.OK)text.Text=dlg.FileName;}}),1,0);return p;
        }
        void FillLayers(){layers.Rows.Clear();foreach(var l in config.Layers){int i=layers.Rows.Add(l.Name,l.Color,l.Material);layers.Rows[i].Tag=l;}}
        void Read()
        {
            layers.EndEdit();foreach(DataGridViewRow row in layers.Rows){var l=(LayerRule)row.Tag;l.Material=Convert.ToString(row.Cells[2].Value).Trim();if(string.IsNullOrWhiteSpace(l.Material))throw new ArgumentException("请填写目标材质："+l.Name);}
            if(config.Layers.Select(l=>l.Color.ToUpperInvariant()).Distinct().Count()!=config.Layers.Count)throw new ArgumentException("每个图层的编码色必须唯一。");
            config.Server=server.Text.Trim();config.Output=output.Text.Trim();config.Workflow=workflow.Text.Trim();config.LongEdge=(int)edge.Value;config.SelectedOnly=selected.Checked;config.ProductMode=tryon.Checked;config.MatchWallpaperAspect=wallpaperAspect.Checked;config.DetailPriority=detailPriority.Checked;config.AutoEditRegion=adaptiveMask.Checked;config.Product=product.Text;config.WearInstructions=wear.Text;config.MaskPadding=(int)padding.Value;config.OcclusionMask=occlusion.Text.Trim();config.AutoSync=autoSync.Checked;
        }
        void ApplyColors(){uint undo=doc.BeginUndoRecord("Rhino AI layer color codes");try{foreach(var rule in config.Layers){var layer=doc.Layers.FindId(new Guid(rule.Id));if(layer!=null){layer.Color=ColorTranslator.FromHtml(rule.Color);}}}finally{doc.EndUndoRecord(undo);}doc.Views.Redraw();Log("编码色已应用到图层显示颜色，可用 Undo 撤销。材质定义请在表格中编辑。");}
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
            return string.Join("|",new[]{server.Text,output.Text,workflow.Text,edge.Value.ToString(),selected.Checked.ToString(),tryon.Checked.ToString(),wallpaperAspect.Checked.ToString(),detailPriority.Checked.ToString(),adaptiveMask.Checked.ToString(),product.Text,wear.Text,padding.Value.ToString(),occlusion.Text,AutoSyncWatcher.FileStamp(occlusion.Text),string.Join(";",layers.Rows.Cast<DataGridViewRow>().Select(r=>string.Join("|",r.Cells.Cast<DataGridViewCell>().Select(c=>Convert.ToString(c.Value)))))});
        }
        async Task Export(bool send,bool automatic)
        {
            actions.Enabled=false;try
            {
                if(RhinoDoc.ActiveDoc==null||RhinoDoc.ActiveDoc.RuntimeSerialNumber!=doc.RuntimeSerialNumber)throw new InvalidOperationException("当前文档已切换，请关闭面板并重新运行 RhinoAI。");
                Read();config.Save();Log("读取当前视口和几何…");var snapshot=Exporter.Capture(doc,config);Log("正在生成 "+snapshot.Camera.Width+" × "+snapshot.Camera.Height+" 控制图…");
                last=await Task.Run(()=>Exporter.Render(snapshot,config,p=>{if(!IsDisposed)BeginInvoke((Action)(()=>progress.Value=p));}));
                Log("已导出："+last.Directory);foreach(var warning in last.Warnings)Log(warning);
                if(send){Log("正在更新到 "+config.Server);using(var client=new ComfyClient(config.Server))Log(await client.Send(last,config));}
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
