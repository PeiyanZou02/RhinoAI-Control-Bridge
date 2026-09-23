using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Rhino;
using Rhino.Commands;
using Rhino.PlugIns;
using Newtonsoft.Json.Linq;

[assembly: System.Reflection.AssemblyTitle("Rhino to Comfy")]
[assembly: System.Reflection.AssemblyVersion("0.40.0.0")]
[assembly: Guid("66587CA6-F24F-49B2-83C1-8E616089B2C4")]

namespace RhinoAI
{
    public sealed class BridgePlugin : PlugIn
    {
        public BridgePlugin(){}
        // Load with Rhino so the commands are always registered. Load-on-demand relies on a cached
        // command list that went stale when the commands were renamed, leaving them "unknown".
        public override PlugInLoadTime LoadTime {get{return PlugInLoadTime.AtStartup;}}
    }
    public sealed class OpenBridge : Command
    {
        static BridgeWindow window;
        public override string EnglishName {get{return "Rhino2Comfy";}}
        protected override Result RunCommand(RhinoDoc doc,RunMode mode)
        {
            if(window!=null&&!window.IsDisposed&&window.DocumentSerial!=doc.RuntimeSerialNumber){window.Close();window=null;}
            if(window==null||window.IsDisposed)window=new BridgeWindow(doc);
            // Owned by the Rhino main window: it stays above Rhino instead of hiding behind it and gets no taskbar button of its own.
            if(!window.Visible)window.Show(new RhinoOwner());window.Activate();return Result.Success;
        }
    }
    sealed class RhinoOwner : IWin32Window { public IntPtr Handle {get{return RhinoApp.MainWindowHandle();}} }
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
        const string Follow="Follow the current ComfyUI window",CurrentView="(Current viewport)",FrameAsIs="Locked frame or viewport, unchanged",FrameAuto="auto: nearest ratio image models draw";
        readonly RhinoDoc doc;
        readonly Config config;
        readonly TextBox server=new TextBox(),output=new TextBox(),workflow=new TextBox(),wear=new TextBox(),occlusion=new TextBox();
        readonly ComboBox target=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList};
        readonly NumericUpDown edge=new NumericUpDown(),padding=new NumericUpDown();
        readonly CheckBox selected=new FlatCheck(),tryon=new FlatCheck(),autoSync=new FlatCheck(),syncStyle=new FlatCheck(),detailPriority=new FlatCheck(),adaptiveMask=new FlatCheck(),wallpaperAspect=new FlatCheck(),sceneAspect=new FlatCheck(),labeledIds=new FlatCheck(),materialMasks=new FlatCheck(),retryLeak=new FlatCheck();
        readonly ComboBox engine=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList},model=new ComboBox(),frameRatio=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList},aspect=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList},resolution=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList};
        readonly TextBox apiKey=new TextBox{UseSystemPasswordChar=true},apiUrl=new TextBox(),aiPrompt=new TextBox(),aiOutput=new TextBox();
        readonly CheckedListBox views=new CheckList(),channels=new CheckList();
        readonly ListBox references=new ListBox{BorderStyle=BorderStyle.None,IntegralHeight=false,SelectionMode=SelectionMode.MultiExtended,HorizontalScrollbar=true};
        FlatButton renderButton,cancelButton;
        CancellationTokenSource cancel;
        AiProvider shown;
        readonly AutoSyncWatcher watcher;
        readonly DataGridView layers=new DataGridView();
        readonly ComboBox idSource=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList};
        FlatButton createMaterials;
        readonly TextBox log=new TextBox();
        readonly FlowLayoutPanel actions=new FlowLayoutPanel(),aiActions=new FlowLayoutPanel();
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
            Text="Rhino to Comfy";ClientSize=new Size(Theme.S(1000),Theme.S(720));MinimumSize=new Size(Theme.S(820),Theme.S(600));StartPosition=FormStartPosition.CenterScreen;ShowInTaskbar=false;
            Font=Theme.Body();BackColor=Theme.Bg;ForeColor=Theme.Text;
            progress=new ThinProgress{Dock=DockStyle.Fill,Margin=Padding.Empty};connection=new StatusDot{Dock=DockStyle.Fill,Font=Theme.Body(),Margin=Padding.Empty};connection.Set(Theme.Faint,"Not connected");

            server.Text=config.Server;output.Text=config.Output;workflow.Text=config.Workflow;
            edge.Minimum=256;edge.Maximum=4096;edge.Increment=256;edge.Value=config.LongEdge;
            target.Items.Add(Follow);if(!string.IsNullOrWhiteSpace(config.TargetWorkflow))target.Items.Add(config.TargetWorkflow);target.SelectedIndex=target.Items.Count-1;
            Check(sceneAspect,"Lock widescreen frame 1024 × 589",config.LockSceneAspect,"Standard scenes use a fixed frame, so resizing the Rhino window does not change the output.");
            Check(selected,"Export selected objects only",config.SelectedOnly,"Recommended for background blend, so only the objects to insert are exported.");
            Check(autoSync,"Upload automatically when the scene changes",config.AutoSync,"Uploads images only. It never edits your prompt, starts a generation or switches the ComfyUI window.");
            Check(syncStyle,"Send the style reference photos",config.SyncReferences,"Adds the photos listed under Style reference photos on the AI render page to the Batch Images, and tells the model to borrow only their look.");
            Check(labeledIds,"Write the material name on each region and soften the ID colors",config.MaterialIdStyle!="flat","The model reads a name on the region far more reliably than a hex code, and a pastel color leaks only a faint tint. Off: the raw flat colors.");
            Check(materialMasks,"Also send one mask per material",config.MaterialMasks,"A white-on-black mask per material, up to 12, sent after the other images. A mask has no color to leak. Costs one image per material.");
            Check(retryLeak,"Check the result for ID color leaks and retry once",config.AiRetryOnLeak,"After each render the average hue of every region is compared with its ID color. A match is logged, the image is kept as _rejected, and one corrected attempt follows.");
            Check(tryon,"Blend into the Wallpaper background",config.ProductMode,"Uses the viewport Wallpaper photo as the base image and exports placement, masks and blend constraints for the Rhino objects.");
            Check(wallpaperAspect,"Match the Wallpaper aspect ratio",config.MatchWallpaperAspect,"Crops the empty viewport margins. Camera and perspective stay unchanged.");
            Check(detailPriority,"Detail priority",config.DetailPriority,"Adds magnified geometry images when the objects are small in the frame. At most 14 images.");
            Check(adaptiveMask,"Expand the edit region automatically",config.AutoEditRegion,"Grows the editable area so existing content under the objects can be replaced. The pixel value below is the minimum.");

            var sync=Stack();
            var targetRow=Pair(new Field(target,32),Button("Refresh",async delegate{await RefreshWorkflows(false);}));
            Row(sync,"Target workflow",targetRow,"Update to ComfyUI switches ComfyUI to this file, then updates its Batch Images.");
            targetStatus.AutoSize=true;targetStatus.ForeColor=Theme.Muted;targetStatus.Font=Theme.Small();targetStatus.Margin=new Padding(0,Theme.S(8),0,Theme.S(24));targetStatus.Text=" ";Add(sync,targetStatus);
            Add(sync,selected);Add(sync,autoSync);Add(sync,syncStyle);

            var material=new TableLayoutPanel{ColumnCount=1,RowCount=4};material.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));material.RowStyles.Add(new RowStyle(SizeType.AutoSize));material.RowStyles.Add(new RowStyle(SizeType.AutoSize));material.RowStyles.Add(new RowStyle(SizeType.AutoSize));material.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            var idOptions=new FlowLayoutPanel{FlowDirection=FlowDirection.TopDown,AutoSize=true,WrapContents=false,Margin=new Padding(0,0,0,Theme.S(8))};idOptions.Controls.Add(labeledIds);idOptions.Controls.Add(materialMasks);material.Controls.Add(idOptions,0,1);
            idSource.Items.Add("Rhino layers");idSource.Items.Add("Rhino materials");idSource.SelectedIndex=config.ByMaterial()?1:0;
            var sourceRow=new TableLayoutPanel{ColumnCount=2,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Anchor=AnchorStyles.Left|AnchorStyles.Top,Margin=new Padding(0,0,0,Theme.S(16))};sourceRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));sourceRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            sourceRow.Controls.Add(new Label{Text="Material ID regions from",AutoSize=true,ForeColor=Theme.Muted,Font=Theme.Label(),Margin=new Padding(0,Theme.S(9),Theme.S(12),0)},0,0);
            var sourceField=new Field(idSource,32){Width=Theme.S(240),Margin=Padding.Empty};sourceRow.Controls.Add(sourceField,1,0);Tip(sourceRow,"Rhino layers: one ID color per layer, taken from the layer display color. Rhino materials: one ID color per render material the objects use, so parts on one layer with different materials stay separate.");
            material.Controls.Add(sourceRow,0,0);
            idSource.SelectedIndexChanged+=(s,e)=>{ReadGrid(false);config.MaterialIdSource=idSource.SelectedIndex==1?"material":"layer";if(doc!=null)config.Scan(doc);FillLayers();};
            var materialActions=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,Margin=new Padding(0,0,0,Theme.S(16))};
            materialActions.Controls.Add(Tip(Button("Reload from Rhino",delegate{Read();config.Scan(doc);FillLayers();}),"Reads the layers, materials and colors from Rhino again."));
            materialActions.Controls.Add(Tip(Button("Assign contrast colors",AssignHighContrastColors),"Gives every row a distinct Material ID color. For layers this changes the layer display color, with undo; for materials only the export color changes."));
            materialActions.Controls.Add(Tip(Button("Names as targets",delegate{ReadGrid(false);foreach(var rule in config.Rules())rule.Material=MaterialRules.TargetFromName(rule.Name);FillLayers();config.Save();Log("Target materials filled from the "+(config.ByMaterial()?"material":"layer")+" names. Edit any row that needs a fuller description.");}),"Writes every Rhino name into Target material, cleaned up: library prefixes and parent layers dropped, underscores to spaces. Replaces what is there."));
            createMaterials=Button("Create layer materials",delegate{Read();ApplyMaterials();});materialActions.Controls.Add(Tip(createMaterials,"Creates basic Rhino materials from the target materials in the table. Undo is supported. Only for layer regions."));
            material.Controls.Add(materialActions,0,2);
            layers.Dock=DockStyle.Fill;layers.AllowUserToAddRows=false;layers.AllowUserToDeleteRows=false;layers.AllowUserToResizeRows=false;layers.RowHeadersVisible=false;layers.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells;Theme.Style(layers);
            layers.Columns.Add(new DataGridViewTextBoxColumn{Name="Layer",HeaderText="Layer",ReadOnly=true,Width=Theme.S(220)});layers.Columns.Add(new DataGridViewTextBoxColumn{Name="Color",HeaderText="ID color",ReadOnly=true,Width=Theme.S(130)});layers.Columns.Add(new DataGridViewTextBoxColumn{Name="Material",HeaderText="Target material",AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill});
            layers.CellPainting+=PaintSwatch;
            var gridCard=new Card{Dock=DockStyle.Fill,Margin=Padding.Empty,Padding=new Padding(Theme.S(2))};gridCard.Controls.Add(layers);material.Controls.Add(gridCard,0,3);FillLayers();

            var blend=Stack();
            wear.Multiline=true;wear.ScrollBars=ScrollBars.Vertical;wear.Text=config.WearInstructions;padding.Minimum=0;padding.Maximum=256;padding.Value=config.MaskPadding;occlusion.Text=config.OcclusionMask;
            Add(blend,tryon);Add(blend,wallpaperAspect);Add(blend,detailPriority);Add(blend,adaptiveMask);
            Row(blend,"Blend instructions",new Field(wear,96),null);Row(blend,"Minimum expansion (px)",new Field(padding,32),null);
            Row(blend,"Occlusion mask (optional)",Browse(occlusion,false,false),"Black and white image at export size. White areas of the photo stay untouched and in front.");

            var settings=Stack();
            Row(settings,"ComfyUI address",Pair(new Field(server,32),Button("Test connection",async delegate{await RefreshWorkflows(false);})),null);
            Row(settings,"Export folder",Browse(output,false,true),null);Row(settings,"API workflow (optional)",Browse(workflow,true,false),"Only used to write a bound API JSON file. Nothing is queued.");
            Row(settings,"Longest edge (px)",new Field(edge,32),null);Add(settings,sceneAspect);
            frameRatio.Items.Add(FrameAsIs);frameRatio.Items.Add(FrameAuto);frameRatio.Items.AddRange(AiProviders.ModelRatios);
            frameRatio.SelectedItem=config.FrameRatio=="frame"||string.IsNullOrWhiteSpace(config.FrameRatio)?FrameAsIs:config.FrameRatio=="auto"?FrameAuto:frameRatio.Items.Contains(config.FrameRatio)?config.FrameRatio:FrameAuto;
            Row(settings,"Frame ratio",new Field(frameRatio,32),"Image models such as Nano Banana draw only a few fixed ratios. Exporting one of them keeps the model from stretching or recomposing the view: auto turns the 1024 × 589 frame into 16:9. Camera and perspective stay the same. Set the model node in ComfyUI to the same ratio, or to auto. Background blend keeps the Wallpaper ratio.");

            var ai=Stack();
            foreach(var provider in AiProviders.All)engine.Items.Add(provider);
            Row(ai,"Engine",new Field(engine,32),"The tool that renders the ticked views. A vendor API renders directly, without ComfyUI. The ComfyUI engine queues the API workflow from Export settings.");
            Row(ai,"API key",new Field(apiKey,32),"Saved encrypted for your Windows account and sent only to the API address below. Leave empty to use the vendor's environment variable, such as GEMINI_API_KEY.");
            Row(ai,"Model",new Field(model,32),"Pick a suggestion or type any image model the vendor offers. Gemini: gemini-3-pro-image (Nano Banana Pro) for the best quality and 4K; gemini-3.1-flash-image (Nano Banana 2) is faster and cheaper.");
            Row(ai,"Aspect ratio",new Field(aspect,32),"auto follows the exported frame. The list shows what the chosen engine accepts.");
            Row(ai,"Resolution",new Field(resolution,32),"Output size. The choices follow the engine and model: Gemini 3 and Seedream take 1K, 2K or 4K, Gemini 2.5 is fixed, and GPT Image takes a quality level instead. auto follows Longest edge on the Export settings page.");
            model.TextChanged+=(s,e)=>FillOptions(false);
            Row(ai,"API address",new Field(apiUrl,32),"Change only when you use a proxy or a compatible gateway.");
            
            channels.MultiColumn=true;channels.ColumnWidth=Theme.S(150);
            Row(ai,"Named views to render",new Field(views,132),"Every ticked view is restored in the active viewport, exported and rendered in turn. Your current view comes back afterwards.");
            var viewActions=new FlowLayoutPanel{AutoSize=true,Margin=new Padding(0,Theme.S(8),0,0)};
            viewActions.Controls.Add(Button("Reload views",delegate{FillViews(TickedViews());}));viewActions.Controls.Add(Button("All",delegate{for(int i=1;i<views.Items.Count;i++)views.SetItemChecked(i,true);},ButtonKind.Ghost));viewActions.Controls.Add(Button("None",delegate{for(int i=0;i<views.Items.Count;i++)views.SetItemChecked(i,false);},ButtonKind.Ghost));Add(ai,viewActions);
            Row(ai,"Control images to send",new Field(channels,72),"Fewer images are faster and cheaper. Background blend always sends its own reference, placement and mask set.");
            Add(ai,retryLeak);
            aiPrompt.Multiline=true;aiPrompt.ScrollBars=ScrollBars.Vertical;aiPrompt.Text=config.AiPrompt;aiOutput.Text=config.AiOutput;
            Row(ai,"Prompt",new Field(aiPrompt,96),"The look you want. Image roles and the layer material mapping are added automatically.");
            Row(ai,"Style reference photos (optional)",new Field(references,76),"Real photographs whose look you want: lighting, color grading, material realism and atmosphere. The engine borrows only the style. Geometry and camera still come from Rhino. Up to "+StyleReferences.Max+" images, sent with every view.");
            var referenceActions=new FlowLayoutPanel{AutoSize=true,Margin=new Padding(0,Theme.S(8),0,0)};
            referenceActions.Controls.Add(Button("Add photos",AddReferences));referenceActions.Controls.Add(Button("Remove",delegate{foreach(var item in references.SelectedItems.Cast<object>().ToList())references.Items.Remove(item);},ButtonKind.Ghost));referenceActions.Controls.Add(Button("Clear",delegate{references.Items.Clear();},ButtonKind.Ghost));Add(ai,referenceActions);
            foreach(var path in config.AiReferences??new List<string>())references.Items.Add(path);
            Row(ai,"Render folder",Browse(aiOutput,false,true),"Finished images are saved here, named after their view.");
            
            renderButton=Button("Render ticked views",async delegate{await RenderViews();},ButtonKind.Primary);cancelButton=Button("Cancel",delegate{if(cancel!=null)cancel.Cancel();});cancelButton.Enabled=false;
            aiActions.Controls.Add(renderButton);aiActions.Controls.Add(cancelButton);aiActions.Controls.Add(Button("Render folder",delegate{if(Directory.Exists(aiOutput.Text))Open(aiOutput.Text);},ButtonKind.Ghost));
            foreach(var channel in PromptGuide.SceneChannels)channels.Items.Add(channel,(config.AiChannels==null||config.AiChannels.Count==0?PromptGuide.DefaultChannels:config.AiChannels.ToArray()).Contains(channel));
            FillViews(config.AiViews??new List<string>{CurrentView});
            engine.SelectedIndexChanged+=(s,e)=>ShowEngine();engine.SelectedItem=AiProviders.Find(config.AiEngine);

            var host=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty};
            foreach(var page in new[]{Page("Sync",sync),Page("AI render",ai),Page("Layer materials",material),Page("Background blend",blend),Page("Export settings",settings)}){pages.Add(page);host.Controls.Add(page);}

            // The AI render page swaps in its own bar, so Render ticked views is always in sight.
            foreach(var bar in new[]{actions,aiActions}){bar.Dock=DockStyle.Fill;bar.Margin=Padding.Empty;bar.Padding=new Padding(Theme.S(24),Theme.S(12),0,0);bar.WrapContents=false;}
            var bars=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty};bars.Controls.Add(actions);bars.Controls.Add(aiActions);
            actions.Controls.Add(Button("Update to ComfyUI",async delegate{await Export(true,false);},ButtonKind.Primary));actions.Controls.Add(Button("Export only",async delegate{await Export(false,false);}));
            actions.Controls.Add(Tip(Button("Export folder",delegate{if(last!=null)Open(last.Directory);else if(Directory.Exists(output.Text))Open(output.Text);},ButtonKind.Ghost),"Opens the latest export, or the export folder."));actions.Controls.Add(Tip(Button("Change export folder",ChangeExportFolder,ButtonKind.Ghost),"Chooses where exports are written. The same setting as Export folder on the Export settings page."));actions.Controls.Add(Button("Open ComfyUI",delegate{Read();Open(config.Server);},ButtonKind.Ghost));
            log.Multiline=true;log.ReadOnly=true;log.BorderStyle=BorderStyle.None;log.ScrollBars=ScrollBars.None;log.Dock=DockStyle.Fill;log.BackColor=Theme.Bg;log.ForeColor=Theme.Muted;log.Font=mono;log.TabStop=false;
            var logHost=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty,Padding=new Padding(Theme.S(24),Theme.S(12),Theme.S(24),Theme.S(12))};logHost.Controls.Add(log);

            var main=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,Margin=Padding.Empty};main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            main.RowStyles.Add(new RowStyle(SizeType.Percent,100));main.RowStyles.Add(new RowStyle(SizeType.Absolute,1));main.RowStyles.Add(new RowStyle(SizeType.Absolute,Theme.S(56)));main.RowStyles.Add(new RowStyle(SizeType.Absolute,Theme.S(8)));main.RowStyles.Add(new RowStyle(SizeType.Absolute,Theme.S(92)));
            main.Controls.Add(host,0,0);main.Controls.Add(new Panel{Dock=DockStyle.Fill,BackColor=Theme.Border,Margin=Padding.Empty},0,1);main.Controls.Add(bars,0,2);main.Controls.Add(progress,0,3);main.Controls.Add(logHost,0,4);

            var side=new Panel{Dock=DockStyle.Left,Width=Theme.S(216),BackColor=Theme.Side};
            var menu=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,BackColor=Theme.Side,Padding=new Padding(Theme.S(12),Theme.S(16),Theme.S(12),Theme.S(12))};menu.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            menu.RowStyles.Add(new RowStyle(SizeType.Absolute,Theme.S(52)));menu.Controls.Add(new Label{Text="Rhino to Comfy",Font=Theme.Brand(),ForeColor=Theme.Text,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(Theme.S(10),0,0,Theme.S(12)),Margin=Padding.Empty},0,0);
            string[] names={"Sync","AI render","Layer materials","Background blend","Export settings"};
            for(int i=0;i<names.Length;i++)
            {
                int index=i;var item=new FlatButton{Text=names[i],Kind=ButtonKind.Nav,Dock=DockStyle.Fill,Margin=new Padding(0,Theme.S(1),0,Theme.S(1)),Font=Theme.Body()};item.Click+=(s,e)=>Go(index);
                nav.Add(item);menu.RowStyles.Add(new RowStyle(SizeType.Absolute,Theme.S(38)));menu.Controls.Add(item,0,i+1);
            }
            menu.RowStyles.Add(new RowStyle(SizeType.Percent,100));menu.Controls.Add(new Panel{Margin=Padding.Empty},0,names.Length+1);
            menu.RowStyles.Add(new RowStyle(SizeType.Absolute,Theme.S(36)));menu.Controls.Add(connection,0,names.Length+2);
            side.Controls.Add(menu);side.Controls.Add(new Panel{Dock=DockStyle.Right,Width=1,BackColor=Theme.Border});
            Controls.Add(main);Controls.Add(side);Go(0);

            if(doc!=null)watcher=new AutoSyncWatcher(doc,()=>autoSync.Checked&&actions.Enabled,SyncSettingsSignature,async()=>await Export(true,true));
            autoSync.CheckedChanged+=(s,e)=>{if(watcher!=null)watcher.Reset();Log(autoSync.Checked?"Auto sync on.":"Auto sync off.");};
            FormClosing+=(sender,args)=>{if(!actions.Enabled){args.Cancel=true;return;}try{Read();config.Save();}catch(Exception e){RhinoApp.WriteLine(e.Message);}};
            FormClosed+=(s,e)=>{if(watcher!=null)watcher.Dispose();tips.Dispose();mono.Dispose();};
            Shown+=async(s,e)=>await RefreshWorkflows(true);
            // Views saved in Rhino while the panel is open appear as soon as the panel gets focus again.
            Activated+=(s,e)=>{if(actions.Enabled)FillViews(TickedViews());};
            Log("Ready.");
        }
        void Go(int index){for(int i=0;i<pages.Count;i++){pages[i].Visible=i==index;nav[i].Selected=i==index;}aiActions.Visible=index==1;actions.Visible=index!=1;}
        static Panel Page(string title,Control body)
        {
            var page=new Panel{Dock=DockStyle.Fill,Visible=false,Padding=new Padding(Theme.S(24),Theme.S(20),Theme.S(24),Theme.S(16))};body.Dock=DockStyle.Fill;page.Controls.Add(body);
            page.Controls.Add(new Label{Text=title,Font=Theme.Title(),Dock=DockStyle.Top,Height=Theme.S(56),ForeColor=Theme.Text});return page;
        }
        static TableLayoutPanel Stack(){var p=new TableLayoutPanel{AutoScroll=true,ColumnCount=1,Padding=new Padding(0,0,Theme.S(12),0)};p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));return p;}
        static void Add(TableLayoutPanel panel,Control control){int row=panel.RowCount++;panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));panel.Controls.Add(control,0,row);}
        void Row(TableLayoutPanel panel,string text,Control control,string tip)
        {
            // md-field: a label-13 medium label, space-2 to its control, space-4 between fields.
            var label=new Label{Text=text,AutoSize=true,ForeColor=Theme.Muted,Font=Theme.Label(),Margin=new Padding(0,panel.RowCount==0?0:Theme.S(16),0,Theme.S(8))};Add(panel,label);
            control.Dock=DockStyle.Top;control.Margin=new Padding(0,0,0,0);Add(panel,control);if(tip!=null){tips.SetToolTip(label,tip);Tip(control,tip);}
        }
        Control Tip(Control control,string text){tips.SetToolTip(control,text);foreach(Control child in control.Controls)Tip(child,text);return control;}
        void Check(CheckBox box,string text,bool value,string tip){box.Text=text;box.Checked=value;Theme.Style(box);box.Margin=new Padding(0,Theme.S(6),0,Theme.S(6));tips.SetToolTip(box,tip);}
        static FlatButton Button(string text,Action action,ButtonKind kind=ButtonKind.Secondary)
        {
            var button=new FlatButton{Text=text,Kind=kind,AutoSize=true,Font=Theme.Strong(),Margin=new Padding(0,0,Theme.S(8),0)};
            button.Click+=(s,e)=>{try{action();}catch(Exception error){MessageBox.Show(error.Message,"Rhino to Comfy");}};return button;
        }
        static Control Pair(Control field,Control button)
        {
            var p=new TableLayoutPanel{ColumnCount=2,RowCount=1,Height=Theme.S(32)};p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));p.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));p.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            field.Dock=DockStyle.Fill;field.Margin=new Padding(0,0,Theme.S(8),0);button.Margin=new Padding(0,0,0,0);p.Controls.Add(field,0,0);p.Controls.Add(button,1,0);return p;
        }
        static Control Browse(TextBox text,bool json,bool folder)
        {
            return Pair(new Field(text,32),Button("Browse",delegate{if(folder){using(var dlg=new FolderBrowserDialog()){dlg.SelectedPath=text.Text;if(dlg.ShowDialog()==DialogResult.OK)text.Text=dlg.SelectedPath;}}else using(var dlg=new OpenFileDialog{Filter=json?"API JSON|*.json":"Images|*.png;*.jpg;*.jpeg;*.bmp"}){if(dlg.ShowDialog()==DialogResult.OK)text.Text=dlg.FileName;}}));
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
        void FillLayers()
        {
            bool byMaterial=config.ByMaterial();layers.Columns[0].HeaderText=byMaterial?"Rhino material":"Layer";if(createMaterials!=null)createMaterials.Enabled=!byMaterial;
            layers.Rows.Clear();foreach(var l in config.Rules()){int i=layers.Rows.Add(l.Name,l.Color,l.Material);layers.Rows[i].Tag=l;}
        }
        // The grid edits whichever rule list is shown. Strict reads refuse blank targets; a mode switch keeps them.
        void ReadGrid(bool strict)
        {
            layers.EndEdit();foreach(DataGridViewRow row in layers.Rows){var l=(LayerRule)row.Tag;l.Material=Convert.ToString(row.Cells[2].Value).Trim();if(strict&&string.IsNullOrWhiteSpace(l.Material))throw new ArgumentException("Enter a target material for "+(config.ByMaterial()?"material":"layer")+": "+l.Name);}
        }
        void Read()
        {
            ReadGrid(true);config.MaterialIdSource=idSource.SelectedIndex==1?"material":"layer";config.MaterialIdStyle=labeledIds.Checked?"labeled":"flat";config.MaterialMasks=materialMasks.Checked;config.AiRetryOnLeak=retryLeak.Checked;
            config.Server=server.Text.Trim();config.Output=output.Text.Trim();config.Workflow=workflow.Text.Trim();config.TargetWorkflow=SelectedTarget();config.LongEdge=(int)edge.Value;config.LockSceneAspect=sceneAspect.Checked;config.SelectedOnly=selected.Checked;config.ProductMode=tryon.Checked;config.MatchWallpaperAspect=wallpaperAspect.Checked;config.DetailPriority=detailPriority.Checked;config.AutoEditRegion=adaptiveMask.Checked;config.WearInstructions=wear.Text;config.MaskPadding=(int)padding.Value;config.OcclusionMask=occlusion.Text.Trim();config.AutoSync=autoSync.Checked;config.SyncReferences=syncStyle.Checked;
            string chosen=Convert.ToString(frameRatio.SelectedItem);config.FrameRatio=chosen==FrameAsIs?"frame":chosen==FrameAuto?"auto":chosen;
            StoreEngine();config.AiEngine=shown.Id;config.AiPrompt=aiPrompt.Text;config.AiOutput=aiOutput.Text.Trim();config.AiChannels=channels.CheckedItems.Cast<string>().ToList();config.AiViews=TickedViews();config.AiReferences=references.Items.Cast<string>().ToList();
        }
        void AddReferences()
        {
            using(var dlg=new OpenFileDialog{Filter="Photos|*.jpg;*.jpeg;*.png;*.bmp",Multiselect=true,Title="Style reference photos"})
            {
                if(dlg.ShowDialog(this)!=DialogResult.OK)return;
                foreach(var path in dlg.FileNames)if(!references.Items.Contains(path)&&references.Items.Count<StyleReferences.Max)references.Items.Add(path);
                if(dlg.FileNames.Any(path=>!references.Items.Contains(path)))Log("At most "+StyleReferences.Max+" style reference photos are used.");
            }
        }
        List<string> TickedViews(){return views.CheckedItems.Cast<string>().ToList();}
        void FillViews(List<string> ticked)
        {
            var names=new List<string>{CurrentView};
            if(doc!=null)for(int i=0;i<doc.NamedViews.Count;i++){string name=doc.NamedViews[i].Name;if(!string.IsNullOrEmpty(name)&&!names.Contains(name))names.Add(name);}
            if(names.SequenceEqual(views.Items.Cast<string>()))return;
            views.BeginUpdate();views.Items.Clear();foreach(var name in names)views.Items.Add(name,ticked.Contains(name));views.EndUpdate();
        }
        // Each vendor keeps its own key, model and address, so switching engines loses nothing.
        void StoreEngine()
        {
            if(shown==null||shown.Id==AiProviders.Comfy)return;
            var settings=config.Provider(shown.Id);string key=apiKey.Text.Trim();
            if(key!=Secret.Reveal(settings.Key))settings.Key=Secret.Protect(key);
            settings.Aspect=aspect.Text==""?"auto":aspect.Text;settings.Resolution=resolution.Text==""?"auto":resolution.Text;
            settings.Model=model.Text.Trim();settings.BaseUrl=apiUrl.Text.Trim()==shown.BaseUrl?"":apiUrl.Text.Trim();if(settings.BaseUrl=="")apiUrl.Text=shown.BaseUrl;
        }
        void ShowEngine()
        {
            StoreEngine();shown=(AiProvider)engine.SelectedItem;bool api=shown.Id!=AiProviders.Comfy;var settings=api?config.Provider(shown.Id):new ProviderSettings();
            apiKey.Enabled=model.Enabled=apiUrl.Enabled=api;apiKey.Text=Secret.Reveal(settings.Key);apiUrl.Text=settings.BaseUrl==""?shown.BaseUrl:settings.BaseUrl;
            model.Items.Clear();model.Items.AddRange(shown.Models);model.Text=settings.Model==""?shown.Models.FirstOrDefault()??"":settings.Model;
            FillOptions(true);
        }
        // Offer only what the chosen engine and model accept. A model change keeps the current choice when it is still valid.
        void FillOptions(bool saved)
        {
            if(shown==null)return;var settings=shown.Id==AiProviders.Comfy?new ProviderSettings():config.Provider(shown.Id);
            foreach(var pair in new[]{Tuple.Create(aspect,AiProviders.Aspects(shown),settings.Aspect),Tuple.Create(resolution,AiProviders.Resolutions(shown,model.Text),settings.Resolution)})
            {
                var box=pair.Item1;string wanted=saved||box.SelectedItem==null?pair.Item3:Convert.ToString(box.SelectedItem);
                box.BeginUpdate();box.Items.Clear();box.Items.AddRange(pair.Item2);box.EndUpdate();box.Enabled=pair.Item2.Length>1;
                if(pair.Item2.Length>0)box.SelectedItem=AiProviders.Pick(pair.Item2,wanted);
            }
        }
        // Vendors draw only a few fixed ratios. Exporting the Rhino frame in the very ratio the engine will use keeps the
        // result from being stretched or recomposed. Only this capture is affected; the saved frame setting stays as it is.
        Snapshot CaptureForEngine(AiProvider provider,ProviderSettings settings,Size viewport,bool announce)
        {
            bool locked=config.LockSceneAspect;
            var frame=provider==null||config.ProductMode?null:AiProviders.Frame(provider,settings,locked?config.SceneAspectWidth:viewport.Width,locked?config.SceneAspectHeight:viewport.Height);
            if(frame!=null&&announce)Log("Frame set to "+frame[0]+":"+frame[1]+" for "+provider.Name+", so the result lines up with the Rhino view.");
            // Engines that accept any size fall back to the Frame ratio setting, like Update to ComfyUI.
            return Exporter.Capture(doc,config,frame);
        }
        static string FileName(string text){var invalid=Path.GetInvalidFileNameChars();string clean=new string((text??"").Select(c=>invalid.Contains(c)?'_':c).ToArray()).Trim().TrimEnd('.');return clean==""?"view":clean;}
        async Task RenderViews()
        {
            actions.Enabled=false;renderButton.Enabled=false;cancelButton.Enabled=true;cancel=new CancellationTokenSource();var token=cancel.Token;
            Rhino.Display.RhinoView view=null;Rhino.DocObjects.ViewportInfo saved=null;string savedName=null;int done=0,failed=0;
            try
            {
                if(RhinoDoc.ActiveDoc==null||RhinoDoc.ActiveDoc.RuntimeSerialNumber!=doc.RuntimeSerialNumber)throw new InvalidOperationException("The active document changed. Close this panel and run Rhino2Comfy again.");
                Read();config.Save();var names=TickedViews();if(names.Count==0)throw new InvalidOperationException("Tick at least one view to render.");
                bool api=shown.Id!=AiProviders.Comfy;var settings=api?config.Provider(shown.Id):null;string key=api?apiKey.Text.Trim():"";
                if(api&&key=="")key=Environment.GetEnvironmentVariable(shown.KeyVariable)??"";
                if(api&&key=="")throw new InvalidOperationException("Enter the "+shown.Name+" API key, or set the "+shown.KeyVariable+" environment variable.");
                view=doc.Views.ActiveView;if(view==null||view is Rhino.Display.RhinoPageView)throw new InvalidOperationException("Activate a model viewport first.");
                var vp=view.ActiveViewport;saved=new Rhino.DocObjects.ViewportInfo(vp);savedName=vp.Name;Directory.CreateDirectory(config.AiOutput);
                Log("Rendering "+names.Count+" view(s) with "+shown.Name+"…");
                for(int i=0;i<names.Count;i++)
                {
                    token.ThrowIfCancellationRequested();string name=names[i];int step=i;
                    try
                    {
                        if(name!=CurrentView)
                        {
                            int index=doc.NamedViews.FindByName(name);if(index<0)throw new InvalidOperationException("the named view no longer exists");
                            if(!doc.NamedViews.Restore(index,vp))throw new InvalidOperationException("Rhino could not restore the named view");view.Redraw();
                        }
                        string label=name==CurrentView?savedName:name;Log("["+(i+1)+"/"+names.Count+"] "+label+": exporting control images…");
                        var snapshot=CaptureForEngine(api?shown:null,settings,vp.Size,i==0);
                        last=await Task.Run(()=>Exporter.Render(snapshot,config,p=>{if(!IsDisposed)BeginInvoke((Action)(()=>progress.Value=Math.Min(99,(step*100+p/2)/names.Count)));}));
                        token.ThrowIfCancellationRequested();List<AiImage> images;
                        var skipped=new List<string>();StyleReferences.Prepare(last,config,skipped);foreach(var note in skipped)Log(note);
                        if(api)
                        {
                            var notes=new List<string>();var inputs=PromptGuide.Select(last,config,shown.MaxImages,notes);foreach(var note in notes)Log(note);
                            string prompt=PromptGuide.Build(inputs.Select(x=>x.Key).ToList(),last,config);File.WriteAllText(Path.Combine(last.Directory,"ai_prompt.txt"),prompt,System.Text.Encoding.UTF8);
                            Log("["+(i+1)+"/"+names.Count+"] "+label+": sending "+inputs.Count+" images to "+shown.Name+"…");
                            using(var client=new AiClient())images=await client.Generate(shown,settings,key,prompt,inputs,snapshot.Camera.Width,snapshot.Camera.Height,token);
                            if(config.AiRetryOnLeak&&!config.ProductMode&&images.Count>0)
                            {
                                var leaks=LeakCheck.Inspect(images[0].Bytes,last);
                                if(leaks.Count>0)
                                {
                                    string rejected=Path.Combine(config.AiOutput,FileName(label)+"_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+"_rejected"+images[0].Extension);File.WriteAllBytes(rejected,images[0].Bytes);
                                    Log("["+(i+1)+"/"+names.Count+"] "+label+": ID color leak, "+string.Join("; ",leaks)+". Kept as "+Path.GetFileName(rejected)+", retrying once with a correction…");
                                    string corrected=prompt+"\nREJECTED PREVIOUS ATTEMPT: "+string.Join("; ",leaks)+". Those regions were painted in their ID label colors, which is forbidden. Repaint each of them in the natural color of its mapped material and keep everything else exactly as specified.";
                                    token.ThrowIfCancellationRequested();
                                    using(var client=new AiClient())images=await client.Generate(shown,settings,key,corrected,inputs,snapshot.Camera.Width,snapshot.Camera.Height,token);
                                    var again=images.Count>0?LeakCheck.Inspect(images[0].Bytes,last):new List<string>();
                                    Log("["+(i+1)+"/"+names.Count+"] "+label+(again.Count==0?": the retry passed the ID color check.":": the retry still shows "+string.Join("; ",again)+". Consider a clearer material name or the per-material masks."));
                                }
                            }
                        }
                        else
                        {
                            Log("["+(i+1)+"/"+names.Count+"] "+label+": queued in ComfyUI…");
                            using(var client=new ComfyClient(config.Server)){client.Timeout=TimeSpan.FromMinutes(5);images=await client.Generate(last,config,token);}
                        }
                        images=images.Select(image=>image.Fit(snapshot.Camera.Width,snapshot.Camera.Height)).ToList();
                        string stamp=DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        for(int n=0;n<images.Count;n++){string path=Path.Combine(config.AiOutput,FileName(label)+"_"+stamp+(n==0?"":"_"+(n+1))+images[n].Extension);File.WriteAllBytes(path,images[n].Bytes);Log("Saved: "+path);}
                        done++;
                    }
                    catch(OperationCanceledException){throw;}
                    catch(Exception e){failed++;Log("["+(i+1)+"/"+names.Count+"] "+name+" not rendered: "+e.Message);}
                }
                Log("Finished: "+done+" rendered"+(failed>0?", "+failed+" failed":"")+". "+config.AiOutput);
            }
            catch(OperationCanceledException){Log("Cancelled after "+done+" view(s).");}
            catch(Exception e){Log("Not completed: "+e.Message);}
            finally
            {
                try{if(saved!=null&&view!=null&&view.Document!=null){view.ActiveViewport.SetViewProjection(saved,true);view.ActiveViewport.Name=savedName;view.Redraw();}}catch(Exception e){RhinoApp.WriteLine(e.Message);}
                if(cancel!=null){cancel.Dispose();cancel=null;}progress.Value=0;cancelButton.Enabled=false;renderButton.Enabled=true;actions.Enabled=true;
            }
        }
        void ApplyColors(){uint undo=doc.BeginUndoRecord("Rhino to Comfy layer color codes");try{foreach(var rule in config.Layers){var layer=doc.Layers.FindId(new Guid(rule.Id));if(layer!=null){layer.Color=ColorTranslator.FromHtml(rule.Color);}}}finally{doc.EndUndoRecord(undo);}doc.Views.Redraw();Log("ID colors applied to the layer display colors. Undo is supported.");}
        void AssignHighContrastColors()
        {
            int slot=1;foreach(var rule in config.Rules().OrderBy(l=>l.Index))rule.Color=Raster.Hex(Raster.Palette(slot++));
            if(config.ByMaterial()){FillLayers();config.Save();Log("Contrast Material ID colors assigned to the materials. Rhino materials are unchanged.");return;}
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
            return string.Join("|",new[]{server.Text,output.Text,workflow.Text,Convert.ToString(target.SelectedItem),edge.Value.ToString(),idSource.SelectedIndex.ToString(),labeledIds.Checked.ToString(),materialMasks.Checked.ToString(),sceneAspect.Checked.ToString(),Convert.ToString(frameRatio.SelectedItem),selected.Checked.ToString(),syncStyle.Checked.ToString(),string.Join(";",references.Items.Cast<string>().Select(p=>p+AutoSyncWatcher.FileStamp(p))),tryon.Checked.ToString(),wallpaperAspect.Checked.ToString(),detailPriority.Checked.ToString(),adaptiveMask.Checked.ToString(),wear.Text,padding.Value.ToString(),occlusion.Text,AutoSyncWatcher.FileStamp(occlusion.Text),string.Join(";",layers.Rows.Cast<DataGridViewRow>().Select(r=>string.Join("|",r.Cells.Cast<DataGridViewCell>().Select(c=>Convert.ToString(c.Value)))))});
        }
        async Task Export(bool send,bool automatic)
        {
            actions.Enabled=false;try
            {
                if(RhinoDoc.ActiveDoc==null||RhinoDoc.ActiveDoc.RuntimeSerialNumber!=doc.RuntimeSerialNumber)throw new InvalidOperationException("The active document changed. Close this panel and run Rhino2Comfy again.");
                Read();config.Save();Log("Reading viewport and geometry…");var snapshot=Exporter.Capture(doc,config);Log("Rendering "+snapshot.Camera.Width+" × "+snapshot.Camera.Height+" control images…");
                last=await Task.Run(()=>Exporter.Render(snapshot,config,p=>{if(!IsDisposed)BeginInvoke((Action)(()=>progress.Value=p));}));
                Log("Exported: "+last.Directory);foreach(var warning in last.Warnings)Log(warning);
                if(config.SyncReferences){var skipped=new List<string>();StyleReferences.Prepare(last,config,skipped);foreach(var note in skipped)Log(note);}
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
        void ChangeExportFolder()
        {
            using(var dlg=new FolderBrowserDialog{Description="Folder for Rhino to Comfy exports",ShowNewFolderButton=true})
            {
                if(Directory.Exists(output.Text))dlg.SelectedPath=output.Text;if(dlg.ShowDialog(this)!=DialogResult.OK)return;
                // Forget the previous export so Export folder opens the new location.
                output.Text=dlg.SelectedPath;config.Output=dlg.SelectedPath;config.Save();last=null;Log("Export folder: "+dlg.SelectedPath);
            }
        }
        void Log(string message){log.AppendText(DateTime.Now.ToString("HH:mm:ss")+"  "+message+Environment.NewLine);}
        static void Open(string path){Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}
    }
}
