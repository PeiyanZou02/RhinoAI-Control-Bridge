using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RhinoAI
{
    // The Meridian design system, light theme: its neutral scale, type styles and the
    // few owner-drawn controls the panel needs. Colour is reserved for meaning: the
    // connection state and the focus ring.
    public static class Theme
    {
        // background-100 / background-200 / gray scale
        public static readonly Color Bg=Color.FromArgb(0xff,0xff,0xff),Side=Color.FromArgb(0xfa,0xfa,0xfa),Surface=Color.FromArgb(0xff,0xff,0xff);
        public static readonly Color Hover=Color.FromArgb(0xf5,0xf5,0xf5),Pressed=Color.FromArgb(0xef,0xef,0xef);          // surface-hover, surface-active
        public static readonly Color Border=Color.FromArgb(0xe3,0xe3,0xe3),BorderStrong=Color.FromArgb(0xd0,0xd0,0xd0);    // border, border-strong
        public static readonly Color Control=Color.FromArgb(0x8f,0x8f,0x8f);                                                // border-control (3:1 edges)
        public static readonly Color Text=Color.FromArgb(0x17,0x17,0x17),Muted=Color.FromArgb(0x66,0x66,0x66),Faint=Color.FromArgb(0xb5,0xb5,0xb5); // ink, ink-muted, ink-disabled
        public static readonly Color Primary=Color.FromArgb(0x17,0x17,0x17),PrimaryHover=Color.FromArgb(0x38,0x38,0x38);
        public static readonly Color Focus=Color.FromArgb(0x01,0x72,0xdc);                                                  // focus-ring (blue-700)
        public static readonly Color Ok=Color.FromArgb(0x03,0xa1,0x4a),Warn=Color.FromArgb(0xf9,0xad,0x26),Bad=Color.FromArgb(0xd7,0x33,0x37); // green/amber/red-700
        public static float Scale=1f;
        public static int S(int px){return (int)Math.Round(px*Scale);}
        // TWK Everett and Geist Mono when installed, else the system fallbacks the design system names.
        static Font Face(int px,params string[] families)
        {
            foreach(var family in families){var f=new Font(family,S(px),FontStyle.Regular,GraphicsUnit.Pixel);if(f.Name==family)return f;f.Dispose();}
            return new Font(FontFamily.GenericSansSerif,S(px),FontStyle.Regular,GraphicsUnit.Pixel);
        }
        static Font Sans(int px){return Face(px,"TWK Everett","Segoe UI");}
        static Font Medium(int px){return Face(px,"TWK Everett Medium","Segoe UI Semibold");}
        public static Font Body(){return Sans(14);}     // label-14
        public static Font Small(){return Sans(13);}    // label-13, helper text
        public static Font Label(){return Medium(13);}  // md-label above a field
        public static Font Strong(){return Medium(14);} // button-14
        public static Font Title(){return Medium(24);}  // heading-24: a section
        public static Font Brand(){return Medium(20);}  // heading-20: the product name
        public static Font Mono(){return Face(13,"Geist Mono","Cascadia Mono","Consolas");} // label-13-mono
        public static GraphicsPath Round(RectangleF r,float radius)
        {
            float d=radius*2;var path=new GraphicsPath();
            path.AddArc(r.X,r.Y,d,d,180,90);path.AddArc(r.Right-d,r.Y,d,d,270,90);path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);path.AddArc(r.X,r.Bottom-d,d,d,90,90);path.CloseFigure();return path;
        }
        // A 2px focus-ring outline, drawn just inside the control since nothing can paint outside it.
        public static void FocusRing(Graphics g,Rectangle bounds,float radius)
        {
            using(var path=Round(new RectangleF(bounds.X+1f,bounds.Y+1f,bounds.Width-2f,bounds.Height-2f),radius))using(var pen=new Pen(Focus,2f))g.DrawPath(pen,path);
        }
        public static void Style(DataGridView grid)
        {
            grid.BackgroundColor=Surface;grid.BorderStyle=BorderStyle.None;grid.GridColor=Border;grid.EnableHeadersVisualStyles=false;
            grid.CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal;grid.ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.None;
            grid.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing;grid.ColumnHeadersHeight=S(40);
            grid.ColumnHeadersDefaultCellStyle=new DataGridViewCellStyle{BackColor=Side,ForeColor=Muted,SelectionBackColor=Side,SelectionForeColor=Muted,Font=Label(),Padding=new Padding(S(12),0,0,0),Alignment=DataGridViewContentAlignment.MiddleLeft};
            grid.DefaultCellStyle=new DataGridViewCellStyle{BackColor=Surface,ForeColor=Text,SelectionBackColor=Hover,SelectionForeColor=Text,Font=Body(),Padding=new Padding(S(12),S(8),S(12),S(8)),WrapMode=DataGridViewTriState.True};
            grid.RowTemplate.MinimumHeight=S(40);
        }
        public static CheckBox Style(CheckBox box){box.AutoSize=true;box.ForeColor=Text;box.Font=Body();return box;}
    }

    public enum ButtonKind{Primary,Secondary,Ghost,Nav}

    // Meridian Button (primary / secondary / tertiary) and its Menu item, used for the side navigation.
    public sealed class FlatButton : Button
    {
        bool hover,down,selected;
        public ButtonKind Kind=ButtonKind.Secondary;
        public bool Selected{get{return selected;}set{selected=value;Invalidate();}}
        public FlatButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);FlatStyle=FlatStyle.Flat;Cursor=Cursors.Hand;}
        protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){hover=false;down=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnMouseDown(MouseEventArgs e){down=true;Invalidate();base.OnMouseDown(e);}
        protected override void OnMouseUp(MouseEventArgs e){down=false;Invalidate();base.OnMouseUp(e);}
        protected override void OnEnabledChanged(EventArgs e){Invalidate();base.OnEnabledChanged(e);}
        protected override void OnGotFocus(EventArgs e){Invalidate();base.OnGotFocus(e);}
        protected override void OnLostFocus(EventArgs e){Invalidate();base.OnLostFocus(e);}
        public override Size GetPreferredSize(Size proposed)
        {
            var text=TextRenderer.MeasureText(Text,Font);return new Size(text.Width+Theme.S(22),Theme.S(Kind==ButtonKind.Nav?36:32)); // size sm: 32 high, 10px sides
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Parent==null?Theme.Bg:Parent.BackColor);
            Color fill,text=Theme.Text,border=Color.Empty;
            if(Kind==ButtonKind.Primary){fill=hover||down?Theme.PrimaryHover:Theme.Primary;text=Theme.Surface;}
            else if(Kind==ButtonKind.Secondary){fill=down?Theme.Pressed:hover?Theme.Hover:Theme.Surface;border=Theme.Control;}
            else if(Kind==ButtonKind.Nav){fill=selected?Theme.Pressed:hover?Theme.Hover:Color.Empty;text=selected?Theme.Text:Theme.Muted;}
            else fill=down?Theme.Pressed:hover?Theme.Hover:Color.Empty;
            if(!Enabled){fill=Theme.Hover;text=Theme.Faint;border=Kind==ButtonKind.Secondary?Theme.Border:Color.Empty;}
            var r=new RectangleF(0.5f,0.5f,Width-1f,Height-1f);
            using(var path=Theme.Round(r,Theme.S(6)))
            {
                if(!fill.IsEmpty)using(var brush=new SolidBrush(fill))g.FillPath(brush,path);
                if(!border.IsEmpty)using(var pen=new Pen(border))g.DrawPath(pen,path);
            }
            if(Focused&&ShowFocusCues)Theme.FocusRing(g,ClientRectangle,Theme.S(6));
            var flags=TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|(Kind==ButtonKind.Nav?TextFormatFlags.Left:TextFormatFlags.HorizontalCenter);
            var area=Kind==ButtonKind.Nav?new Rectangle(Theme.S(10),0,Width-Theme.S(14),Height):ClientRectangle;
            TextRenderer.DrawText(g,Text,Font,area,text,flags);
        }
    }

    // Meridian Input / Select / Textarea: a border-control edge, ink-muted on hover, the focus ring when active.
    public sealed class Field : Panel
    {
        readonly Control inner;bool hover;
        public Field(Control control,int height)
        {
            inner=control;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
            BackColor=Theme.Surface;Height=Theme.S(height);Padding=new Padding(Theme.S(12),Theme.S(6),Theme.S(10),Theme.S(5));
            var text=control as TextBoxBase;if(text!=null)text.BorderStyle=BorderStyle.None;
            var number=control as NumericUpDown;if(number!=null)number.BorderStyle=BorderStyle.None;
            var combo=control as ComboBox;if(combo!=null){combo.FlatStyle=FlatStyle.Flat;Padding=new Padding(Theme.S(8),Theme.S(4),Theme.S(4),Theme.S(3));}
            if(control is ListBox)Padding=new Padding(Theme.S(6),Theme.S(6),Theme.S(6),Theme.S(6));
            control.BackColor=Theme.Surface;control.ForeColor=Theme.Text;control.Dock=DockStyle.Fill;Controls.Add(control);
            control.GotFocus+=(s,e)=>Invalidate();control.LostFocus+=(s,e)=>Invalidate();
            control.MouseEnter+=(s,e)=>{hover=true;Invalidate();};control.MouseLeave+=(s,e)=>{hover=false;Invalidate();};
        }
        protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnPaint(PaintEventArgs e)
        {
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Parent==null?Theme.Bg:Parent.BackColor);
            using(var path=Theme.Round(new RectangleF(0.5f,0.5f,Width-1f,Height-1f),Theme.S(6)))
            {
                using(var brush=new SolidBrush(inner.Enabled?Theme.Surface:Theme.Side))g.FillPath(brush,path);
                using(var pen=new Pen(!inner.Enabled?Theme.Border:hover?Theme.Muted:Theme.Control))g.DrawPath(pen,path);
            }
            if(inner.ContainsFocus)Theme.FocusRing(g,ClientRectangle,Theme.S(6));
        }
    }

    // Meridian Checkbox: a 16px box with a 4px radius, filled primary when checked.
    public sealed class FlatCheck : CheckBox
    {
        bool hover;
        public FlatCheck(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);Cursor=Cursors.Hand;}
        protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnGotFocus(EventArgs e){Invalidate();base.OnGotFocus(e);}
        protected override void OnLostFocus(EventArgs e){Invalidate();base.OnLostFocus(e);}
        public override Size GetPreferredSize(Size proposed){var text=TextRenderer.MeasureText(Text,Font);return new Size(text.Width+Theme.S(30),Math.Max(Theme.S(20),text.Height)+Padding.Vertical);}
        protected override void OnPaint(PaintEventArgs e)
        {
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Parent==null?Theme.Bg:Parent.BackColor);
            int size=Theme.S(16);var box=new Rectangle(0,(Height-size)/2,size,size);
            Draw(g,box,Checked,Enabled,hover,Focused&&ShowFocusCues);
            TextRenderer.DrawText(g,Text,Font,new Rectangle(size+Theme.S(8),0,Width-size-Theme.S(8),Height),Enabled?Theme.Text:Theme.Faint,TextFormatFlags.VerticalCenter|TextFormatFlags.Left|TextFormatFlags.EndEllipsis);
        }
        public static void Draw(Graphics g,Rectangle box,bool on,bool enabled,bool hover,bool focused)
        {
            using(var path=Theme.Round(new RectangleF(box.X+0.5f,box.Y+0.5f,box.Width-1f,box.Height-1f),Theme.S(4)))
            {
                using(var brush=new SolidBrush(!enabled?Theme.Hover:on?Theme.Primary:Theme.Surface))g.FillPath(brush,path);
                using(var pen=new Pen(!enabled?Theme.Border:on?Theme.Primary:hover?Theme.Muted:Theme.Control))g.DrawPath(pen,path);
            }
            if(on)using(var pen=new Pen(enabled?Theme.Surface:Theme.Faint,Math.Max(1.5f,Theme.S(2))){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round})
            {
                float x=box.X,y=box.Y,w=box.Width,h=box.Height;
                g.DrawLines(pen,new[]{new PointF(x+w*0.24f,y+h*0.52f),new PointF(x+w*0.43f,y+h*0.70f),new PointF(x+w*0.77f,y+h*0.32f)});
            }
            if(focused)Theme.FocusRing(g,new Rectangle(box.X-2,box.Y-2,box.Width+4,box.Height+4),Theme.S(5));
        }
    }

    // A CheckedListBox whose rows are Meridian checkboxes instead of the native ones.
    public sealed class CheckList : CheckedListBox
    {
        public CheckList(){BorderStyle=BorderStyle.None;CheckOnClick=true;IntegralHeight=false;Font=Theme.Body();}
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if(e.Index<0)return;var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            bool selected=(e.State&DrawItemState.Selected)!=0;
            using(var brush=new SolidBrush(selected?Theme.Hover:BackColor))g.FillRectangle(brush,e.Bounds);
            int size=Theme.S(16);var box=new Rectangle(e.Bounds.X+Theme.S(4),e.Bounds.Y+(e.Bounds.Height-size)/2,size,size);
            FlatCheck.Draw(g,box,GetItemChecked(e.Index),Enabled,selected,selected&&Focused);
            TextRenderer.DrawText(g,GetItemText(Items[e.Index]),Font,new Rectangle(box.Right+Theme.S(8),e.Bounds.Y,e.Bounds.Right-box.Right-Theme.S(8),e.Bounds.Height),Enabled?Theme.Text:Theme.Faint,TextFormatFlags.VerticalCenter|TextFormatFlags.Left|TextFormatFlags.EndEllipsis);
        }
    }

    // Meridian Card: surface, radius-lg, the material-medium ring.
    public sealed class Card : Panel
    {
        public Card(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);BackColor=Theme.Surface;Padding=new Padding(Theme.S(1));}
        protected override void OnPaint(PaintEventArgs e)
        {
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Parent==null?Theme.Bg:Parent.BackColor);
            using(var path=Theme.Round(new RectangleF(0.5f,0.5f,Width-1f,Height-1f),Theme.S(12)))
            {
                using(var brush=new SolidBrush(Theme.Surface))g.FillPath(brush,path);
                using(var pen=new Pen(Theme.Border))g.DrawPath(pen,path);
            }
        }
    }

    // Meridian StatusDot: a 10px dot and a word.
    public sealed class StatusDot : Control
    {
        Color dot=Theme.Control;
        public StatusDot(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.SupportsTransparentBackColor,true);Height=Theme.S(36);ForeColor=Theme.Text;}
        public void Set(Color color,string text){dot=color;Text=text;Invalidate();}
        protected override void OnPaint(PaintEventArgs e)
        {
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Parent==null?Theme.Side:Parent.BackColor);
            int d=Theme.S(10);using(var brush=new SolidBrush(dot))g.FillEllipse(brush,Theme.S(10),(Height-d)/2f,d,d);
            TextRenderer.DrawText(g,Text,Font,new Rectangle(Theme.S(28),0,Width-Theme.S(30),Height),ForeColor,TextFormatFlags.VerticalCenter|TextFormatFlags.Left|TextFormatFlags.EndEllipsis);
        }
    }

    // Meridian Progress: an 8px pill on gray-200, filled primary. Hidden while idle.
    public sealed class ThinProgress : Control
    {
        int value;
        public ThinProgress(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);Height=Theme.S(8);}
        public int Value{get{return value;}set{this.value=Math.Max(0,Math.Min(100,value));Invalidate();}}
        protected override void OnPaint(PaintEventArgs e)
        {
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Parent==null?Theme.Bg:Parent.BackColor);
            if(value<=0||value>=100)return;
            var track=new RectangleF(Theme.S(24),0,Math.Max(1,Width-Theme.S(48)),Height);
            using(var path=Theme.Round(track,Height/2f))using(var brush=new SolidBrush(Theme.Pressed))g.FillPath(brush,path);
            using(var path=Theme.Round(new RectangleF(track.X,0,Math.Max(Height,track.Width*value/100f),Height),Height/2f))using(var brush=new SolidBrush(Theme.Primary))g.FillPath(brush,path);
        }
    }
}
