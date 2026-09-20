using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RhinoAI
{
    // Monochrome palette and the few owner-drawn controls the panel needs.
    // Colour is reserved for connection state only.
    public static class Theme
    {
        public static readonly Color Bg=Color.FromArgb(250,250,249),Side=Color.FromArgb(244,244,242),Surface=Color.White;
        public static readonly Color Border=Color.FromArgb(229,228,224),BorderStrong=Color.FromArgb(198,197,192);
        public static readonly Color Text=Color.FromArgb(31,31,30),Muted=Color.FromArgb(107,106,102),Faint=Color.FromArgb(160,159,154);
        public static readonly Color Hover=Color.FromArgb(240,239,236),Pressed=Color.FromArgb(230,229,225),PrimaryHover=Color.FromArgb(60,60,58);
        public static readonly Color Ok=Color.FromArgb(47,138,87),Bad=Color.FromArgb(196,64,58);
        public static float Scale=1f;
        public static int S(int px){return (int)Math.Round(px*Scale);}
        public static Font Body(){return new Font("Segoe UI",9f);}
        public static Font Small(){return new Font("Segoe UI",8.25f);}
        public static Font Strong(){return new Font("Segoe UI Semibold",9f);}
        public static Font Title(){return new Font("Segoe UI Semibold",12.5f);}
        public static Font Mono(){var f=new Font("Cascadia Mono",8.25f);if(f.Name=="Cascadia Mono")return f;f.Dispose();return new Font("Consolas",8.5f);}
        public static GraphicsPath Round(RectangleF r,float radius)
        {
            float d=radius*2;var path=new GraphicsPath();
            path.AddArc(r.X,r.Y,d,d,180,90);path.AddArc(r.Right-d,r.Y,d,d,270,90);path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);path.AddArc(r.X,r.Bottom-d,d,d,90,90);path.CloseFigure();return path;
        }
        public static void Style(DataGridView grid)
        {
            grid.BackgroundColor=Surface;grid.BorderStyle=BorderStyle.None;grid.GridColor=Border;grid.EnableHeadersVisualStyles=false;
            grid.CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal;grid.ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.None;
            grid.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing;grid.ColumnHeadersHeight=S(34);
            grid.ColumnHeadersDefaultCellStyle=new DataGridViewCellStyle{BackColor=Surface,ForeColor=Muted,SelectionBackColor=Surface,SelectionForeColor=Muted,Font=Small(),Padding=new Padding(S(8),0,0,0),Alignment=DataGridViewContentAlignment.MiddleLeft};
            grid.DefaultCellStyle=new DataGridViewCellStyle{BackColor=Surface,ForeColor=Text,SelectionBackColor=Hover,SelectionForeColor=Text,Font=Body(),Padding=new Padding(S(8),S(6),S(8),S(6)),WrapMode=DataGridViewTriState.True};
            grid.RowTemplate.MinimumHeight=S(34);
        }
        public static CheckBox Style(CheckBox box){box.AutoSize=true;box.FlatStyle=FlatStyle.Flat;box.FlatAppearance.BorderColor=BorderStrong;box.FlatAppearance.CheckedBackColor=Surface;box.FlatAppearance.MouseOverBackColor=Hover;box.ForeColor=Text;box.Padding=new Padding(0,S(2),0,S(2));return box;}
    }

    public enum ButtonKind{Primary,Secondary,Ghost,Nav}

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
        public override Size GetPreferredSize(Size proposed)
        {
            var text=TextRenderer.MeasureText(Text,Font);return new Size(text.Width+Theme.S(Kind==ButtonKind.Primary?36:26),Theme.S(32));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Parent==null?Theme.Bg:Parent.BackColor);
            Color fill,text=Theme.Text,border=Color.Empty;
            if(Kind==ButtonKind.Primary){fill=down?Color.Black:hover?Theme.PrimaryHover:Theme.Text;text=Color.White;}
            else if(Kind==ButtonKind.Secondary){fill=down?Theme.Pressed:hover?Theme.Hover:Theme.Surface;border=Theme.BorderStrong;}
            else if(Kind==ButtonKind.Nav){fill=selected?Theme.Pressed:hover?Theme.Hover:Color.Empty;text=selected?Theme.Text:Theme.Muted;}
            else fill=down?Theme.Pressed:hover?Theme.Hover:Color.Empty;
            if(!Enabled){text=Theme.Faint;if(Kind==ButtonKind.Primary)fill=Theme.BorderStrong;}
            var r=new RectangleF(0.5f,0.5f,Width-1f,Height-1f);
            using(var path=Theme.Round(r,Theme.S(6)))
            {
                if(!fill.IsEmpty)using(var brush=new SolidBrush(fill))g.FillPath(brush,path);
                if(!border.IsEmpty)using(var pen=new Pen(border))g.DrawPath(pen,path);
                if(Focused&&ShowFocusCues)using(var pen=new Pen(Theme.Text))g.DrawPath(pen,path);
            }
            var flags=TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|(Kind==ButtonKind.Nav?TextFormatFlags.Left:TextFormatFlags.HorizontalCenter);
            var area=Kind==ButtonKind.Nav?new Rectangle(Theme.S(12),0,Width-Theme.S(16),Height):ClientRectangle;
            TextRenderer.DrawText(g,Text,Font,area,text,flags);
        }
    }

    // Rounded, bordered host for a borderless input control.
    public sealed class Field : Panel
    {
        readonly Control inner;
        public Field(Control control,int height)
        {
            inner=control;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
            BackColor=Theme.Surface;Height=Theme.S(height);Padding=new Padding(Theme.S(9),Theme.S(7),Theme.S(7),Theme.S(5));
            var text=control as TextBoxBase;if(text!=null)text.BorderStyle=BorderStyle.None;
            var number=control as NumericUpDown;if(number!=null)number.BorderStyle=BorderStyle.None;
            var combo=control as ComboBox;if(combo!=null){combo.FlatStyle=FlatStyle.Flat;Padding=new Padding(Theme.S(5),Theme.S(4),Theme.S(3),Theme.S(3));}
            control.BackColor=Theme.Surface;control.ForeColor=Theme.Text;control.Dock=DockStyle.Fill;Controls.Add(control);
            control.GotFocus+=(s,e)=>Invalidate();control.LostFocus+=(s,e)=>Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Parent==null?Theme.Bg:Parent.BackColor);
            using(var path=Theme.Round(new RectangleF(0.5f,0.5f,Width-1f,Height-1f),Theme.S(6)))
            {
                using(var brush=new SolidBrush(Theme.Surface))g.FillPath(brush,path);
                using(var pen=new Pen(inner.ContainsFocus?Theme.Text:Theme.BorderStrong))g.DrawPath(pen,path);
            }
        }
    }

    public sealed class Card : Panel
    {
        public Card(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);BackColor=Theme.Surface;Padding=new Padding(Theme.S(1));}
        protected override void OnPaint(PaintEventArgs e)
        {
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Parent==null?Theme.Bg:Parent.BackColor);
            using(var path=Theme.Round(new RectangleF(0.5f,0.5f,Width-1f,Height-1f),Theme.S(8)))
            {
                using(var brush=new SolidBrush(Theme.Surface))g.FillPath(brush,path);
                using(var pen=new Pen(Theme.Border))g.DrawPath(pen,path);
            }
        }
    }

    public sealed class StatusDot : Control
    {
        Color dot=Theme.Faint;
        public StatusDot(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.SupportsTransparentBackColor,true);Height=Theme.S(28);ForeColor=Theme.Muted;}
        public void Set(Color color,string text){dot=color;Text=text;Invalidate();}
        protected override void OnPaint(PaintEventArgs e)
        {
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Parent==null?Theme.Side:Parent.BackColor);
            int d=Theme.S(8);using(var brush=new SolidBrush(dot))g.FillEllipse(brush,Theme.S(12),(Height-d)/2f,d,d);
            TextRenderer.DrawText(g,Text,Font,new Rectangle(Theme.S(28),0,Width-Theme.S(30),Height),ForeColor,TextFormatFlags.VerticalCenter|TextFormatFlags.Left|TextFormatFlags.EndEllipsis);
        }
    }

    public sealed class ThinProgress : Control
    {
        int value;
        public ThinProgress(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);Height=Theme.S(2);}
        public int Value{get{return value;}set{this.value=Math.Max(0,Math.Min(100,value));Invalidate();}}
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Border);
            if(value>0&&value<100)using(var brush=new SolidBrush(Theme.Text))e.Graphics.FillRectangle(brush,0,0,Width*value/100f,Height);
        }
    }
}
