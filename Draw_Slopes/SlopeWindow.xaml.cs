using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

using WpfLine = System.Windows.Shapes.Line;
using WpfEllipse = System.Windows.Shapes.Ellipse;
using WpfColor = System.Windows.Media.Color;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfPoint = System.Windows.Point;
using AcCols = Autodesk.AutoCAD.Colors;
using WinForms = System.Windows.Forms;

namespace Draw_Slopes
{
    public class LayerItem
    {
        public string Name { get; set; } = string.Empty;
        public System.Windows.Media.Brush ColorBrush { get; set; } = System.Windows.Media.Brushes.Transparent;
    }

    public partial class SlopeWindow : Window
    {
        public double Step => double.TryParse(txtStep.Text, out double v) ? v : 2.0;
        public double Ratio => double.TryParse(txtRatio.Text, out double v) ? v : 50.0;
        public double Offset => double.TryParse(txtOffset.Text, out double v) ? v : 0.0;
        public string Method => cmbMethod.SelectedItem?.ToString() ?? "Elastic";
        public bool Is3D => chkIs3D.IsChecked == true;
        public bool IsGroup => chkGroup.IsChecked == true;
        public bool IsBackground => chkWipeout.IsChecked == true;
        public bool AutoOrient => chkAutoOrient.IsChecked == true;
        public bool IsTriangle => chkTriangle.IsChecked == true;

        public string LayerName
        {
            get
            {
                if (chkCreateNewLayer.IsChecked == true) return txtLayer.Text;

                if (cmbLayers.SelectedItem is LayerItem item)
                {
                    return item.Name;
                }
                return "0";
            }
        }

        public string StrokeStyle => cmbLengthType.SelectedItem?.ToString() ?? "Long / 1 Short";

        // >>> ახალი Property დროფდაუნისთვის <<<
        public string CurtainType => cmbCurtainType.SelectedItem?.ToString() ?? "None";

        public int SelectedColorIndex { get; private set; } = 1;
        public int BackgroundHatchColorIndex { get; private set; } = 3;

        public bool IsDeleteRequest { get; private set; } = false;
        public bool IsDrawRequest { get; private set; } = false;

        private WpfColor previewColor = Colors.Red;

        public SlopeWindow()
        {
            InitializeComponent();
            LoadData();

            txtStep.TextChanged += (s, e) => UpdatePreview();
            txtRatio.TextChanged += (s, e) => UpdatePreview();
            txtOffset.TextChanged += (s, e) => UpdatePreview();
            cmbLengthType.SelectionChanged += (s, e) => UpdatePreview();
            chkAutoOrient.Checked += (s, e) => UpdatePreview();
            chkAutoOrient.Unchecked += (s, e) => UpdatePreview();
            chkTriangle.Checked += (s, e) => UpdatePreview();
            chkTriangle.Unchecked += (s, e) => UpdatePreview();
            cmbCurtainType.SelectionChanged += (s, e) => UpdatePreview(); // Curtain Event

            chkCreateNewLayer.Checked += (s, e) => UpdateUI();
            chkCreateNewLayer.Unchecked += (s, e) => UpdateUI();
            chkWipeout.Checked += (s, e) => { UpdateUI(); UpdatePreview(); };
            chkWipeout.Unchecked += (s, e) => { UpdateUI(); UpdatePreview(); };

            this.Closing += SaveData;
            this.Loaded += (s, e) => UpdatePreview();
        }

        private void BtnDraw_Click(object sender, RoutedEventArgs e)
        {
            IsDeleteRequest = false;
            IsDrawRequest = true;
            this.DialogResult = true;
            this.Close();
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            IsDeleteRequest = true;
            IsDrawRequest = false;
            this.DialogResult = true;
            this.Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            IsDeleteRequest = false;
            IsDrawRequest = false;
            this.DialogResult = false;
            this.Close();
        }

        private void LoadData()
        {
            if (cmbMethod.Items.Count == 0)
            {
                cmbMethod.Items.Add("Elastic");
                cmbMethod.Items.Add("Proportional");
                cmbMethod.Items.Add("Perpendicular");
                cmbMethod.Items.Add("Hybrid");
            }

            if (cmbLengthType.Items.Count == 0)
            {
                cmbLengthType.Items.Add("Fixed");
                cmbLengthType.Items.Add("Long / 1 Short");
                cmbLengthType.Items.Add("Long / 2 Shorts");
            }

            // Curtain ვარიანტები
            if (cmbCurtainType.Items.Count == 0)
            {
                cmbCurtainType.Items.Add("None");
                cmbCurtainType.Items.Add("Circle");
                cmbCurtainType.Items.Add("Tick (T-Shape)");
            }

            try
            {
                txtStep.Text = Properties.Settings.Default.txtStep;
                txtOffset.Text = Properties.Settings.Default.txtOffset;
                txtRatio.Text = Properties.Settings.Default.txtShortLength;

                string savedMethod = Properties.Settings.Default.cmbMethod;
                cmbMethod.SelectedItem = string.IsNullOrEmpty(savedMethod) ? "Elastic" : savedMethod;

                string savedLenType = Properties.Settings.Default.cmbLengthType;
                if (savedLenType == "Long/Short Mix" || savedLenType == "Triangle (Tapered)") savedLenType = "Long / 1 Short";
                cmbLengthType.SelectedItem = string.IsNullOrEmpty(savedLenType) ? "Long / 1 Short" : savedLenType;

                cmbCurtainType.SelectedIndex = 0; // Default none

                chkIs3D.IsChecked = Properties.Settings.Default.chkIs3D;
                chkGroup.IsChecked = Properties.Settings.Default.chkGroup;
                chkWipeout.IsChecked = Properties.Settings.Default.chkWipeout;
                chkCreateNewLayer.IsChecked = Properties.Settings.Default.chkCreateNewLayer;
                chkAutoOrient.IsChecked = Properties.Settings.Default.chkAutoOrient;
                // Triangle Load logic can go here if you save it in settings later

                txtLayer.Text = Properties.Settings.Default.txtLayer;

                string cName = Properties.Settings.Default.txtColor;
                if (string.IsNullOrEmpty(cName)) cName = "Red";
                UpdateColorFromName(cName);

                if (int.TryParse(Properties.Settings.Default.hatchColorIdx, out int hIdx))
                {
                    BackgroundHatchColorIndex = hIdx;
                    var acColor = AcCols.Color.FromColorIndex(AcCols.ColorMethod.ByAci, (short)hIdx);
                    borderHatchColor.Background = new SolidColorBrush(WpfColor.FromRgb(acColor.ColorValue.R, acColor.ColorValue.G, acColor.ColorValue.B));
                }
            }
            catch
            {
                cmbMethod.SelectedIndex = 0;
                cmbLengthType.SelectedIndex = 1;
                cmbCurtainType.SelectedIndex = 0;
            }

            LoadLayers();
            UpdateUI();
        }

        private void LoadLayers()
        {
            cmbLayers.Items.Clear();
            var doc = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var lt = (Autodesk.AutoCAD.DatabaseServices.LayerTable)tr.GetObject(doc.Database.LayerTableId, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead);
                foreach (Autodesk.AutoCAD.DatabaseServices.ObjectId id in lt)
                {
                    var ltr = (Autodesk.AutoCAD.DatabaseServices.LayerTableRecord)tr.GetObject(id, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead);
                    if (!ltr.IsErased)
                    {
                        var acColor = ltr.Color;
                        var wpfColor = WpfColor.FromRgb(acColor.ColorValue.R, acColor.ColorValue.G, acColor.ColorValue.B);

                        cmbLayers.Items.Add(new LayerItem
                        {
                            Name = ltr.Name,
                            ColorBrush = new SolidColorBrush(wpfColor)
                        });
                    }
                }
                tr.Commit();
            }

            string savedName = Properties.Settings.Default.txtLayer;
            bool found = false;

            if (!string.IsNullOrEmpty(savedName))
            {
                foreach (LayerItem item in cmbLayers.Items)
                {
                    if (item.Name.Equals(savedName, StringComparison.OrdinalIgnoreCase))
                    {
                        cmbLayers.SelectedItem = item;
                        found = true;
                        break;
                    }
                }
            }

            if (!found && cmbLayers.Items.Count > 0)
            {
                cmbLayers.SelectedIndex = 0;
            }
        }

        private void UpdateUI()
        {
            txtLayer.IsEnabled = chkCreateNewLayer.IsChecked == true;
            cmbLayers.IsEnabled = chkCreateNewLayer.IsChecked == false;
            borderHatchColor.Opacity = chkWipeout.IsChecked == true ? 1.0 : 0.3;
        }

        private void SaveData(object? sender, CancelEventArgs e)
        {
            Properties.Settings.Default.txtStep = txtStep.Text;
            Properties.Settings.Default.txtOffset = txtOffset.Text;
            Properties.Settings.Default.txtShortLength = txtRatio.Text;
            Properties.Settings.Default.cmbMethod = cmbMethod.SelectedItem?.ToString();
            Properties.Settings.Default.cmbLengthType = cmbLengthType.SelectedItem?.ToString();
            Properties.Settings.Default.chkIs3D = chkIs3D.IsChecked == true;
            Properties.Settings.Default.chkGroup = chkGroup.IsChecked == true;
            Properties.Settings.Default.chkWipeout = chkWipeout.IsChecked == true;
            Properties.Settings.Default.chkCreateNewLayer = chkCreateNewLayer.IsChecked == true;
            Properties.Settings.Default.chkAutoOrient = chkAutoOrient.IsChecked == true;

            if (chkCreateNewLayer.IsChecked == true)
            {
                Properties.Settings.Default.txtLayer = txtLayer.Text;
            }
            else
            {
                if (cmbLayers.SelectedItem is LayerItem item)
                {
                    Properties.Settings.Default.txtLayer = item.Name;
                }
            }

            Properties.Settings.Default.txtColor = GetColorName(SelectedColorIndex);
            Properties.Settings.Default.hatchColorIdx = BackgroundHatchColorIndex.ToString();
            Properties.Settings.Default.Save();
        }

        private void BorderColor_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var cd = new Autodesk.AutoCAD.Windows.ColorDialog();
            cd.Color = AcCols.Color.FromColorIndex(AcCols.ColorMethod.ByAci, (short)SelectedColorIndex);

            if (cd.ShowDialog() == WinForms.DialogResult.OK)
            {
                SelectedColorIndex = cd.Color.ColorIndex;
                previewColor = WpfColor.FromRgb(cd.Color.ColorValue.R, cd.Color.ColorValue.G, cd.Color.ColorValue.B);
                borderColor.Background = new SolidColorBrush(previewColor);
                UpdatePreview();
            }
        }

        private void BorderHatchColor_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (chkWipeout.IsChecked != true) return;
            var cd = new Autodesk.AutoCAD.Windows.ColorDialog();
            cd.Color = AcCols.Color.FromColorIndex(AcCols.ColorMethod.ByAci, (short)BackgroundHatchColorIndex);
            if (cd.ShowDialog() == WinForms.DialogResult.OK)
            {
                BackgroundHatchColorIndex = cd.Color.ColorIndex;
                var c = cd.Color.ColorValue;
                borderHatchColor.Background = new SolidColorBrush(WpfColor.FromRgb(c.R, c.G, c.B));
                UpdatePreview();
            }
        }

        private void UpdateColorFromName(string name)
        {
            int idx = 1;
            if (name == "Red") idx = 1;
            else if (name == "Yellow") idx = 2;
            else if (name == "Green") idx = 3;
            else if (name == "Cyan") idx = 4;
            else if (name == "Blue") idx = 5;
            else if (name == "Magenta") idx = 6;
            else int.TryParse(name, out idx);
            SelectedColorIndex = idx;
            var acColor = AcCols.Color.FromColorIndex(AcCols.ColorMethod.ByAci, (short)idx);
            previewColor = WpfColor.FromRgb(acColor.ColorValue.R, acColor.ColorValue.G, acColor.ColorValue.B);
            borderColor.Background = new SolidColorBrush(previewColor);
        }

        private string GetColorName(int idx)
        {
            if (idx == 1) return "Red"; if (idx == 2) return "Yellow"; if (idx == 3) return "Green"; return idx.ToString();
        }

        private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left) this.DragMove();
        }

        private void UpdatePreview()
        {
            previewCanvas.Children.Clear();

            // ახალი, ზუსტი კოორდინატები Canvas-ის საზღვრებისთვის
            double startX = 20;
            double endX = 390;

            // Top (Crest) - გლუვი მრუდი
            WpfPoint p1 = new WpfPoint(startX, 40);
            WpfPoint p2 = new WpfPoint(startX + 120, 10);   // ყველაზე მაღალი წერტილი
            WpfPoint p3 = new WpfPoint(startX + 240, 70);
            WpfPoint p4 = new WpfPoint(endX, 30);

            // Bottom (Toe) - ტალღოვანი მრუდი, მაქსიმალურად ჩამოწეული ქვემოთ 
            WpfPoint b1 = new WpfPoint(startX, 180);
            WpfPoint b2 = new WpfPoint(startX + 130, 100);
            WpfPoint b3 = new WpfPoint(startX + 230, 280); // 280-მდე ჩამოვიყვანეთ (ავსებს 270-იან ძირს)
            WpfPoint b4 = new WpfPoint(endX, 190);

            if (chkWipeout.IsChecked == true)
            {
                Path fillPath = new Path();
                fillPath.Fill = borderHatchColor.Background;
                fillPath.Opacity = 0.4;
                PathGeometry fillGeo = new PathGeometry();
                PathFigure fillFig = new PathFigure { StartPoint = p1 };
                fillFig.Segments.Add(new BezierSegment(p2, p3, p4, true));
                fillFig.Segments.Add(new LineSegment(b4, true));
                fillFig.Segments.Add(new BezierSegment(b3, b2, b1, true));
                fillFig.IsClosed = true;
                fillGeo.Figures.Add(fillFig);
                fillPath.Data = fillGeo;
                previewCanvas.Children.Add(fillPath);
            }

            // Crest - რჩება ჩვეულებრივი (მყარი) მრუდი
            DrawPreviewCurve(p1, p2, p3, p4, System.Windows.Media.Brushes.CornflowerBlue);

            // Toe - ბოლოში ვუმატებთ true-ს, რომ გახდეს წყვეტილი
            DrawPreviewCurve(b1, b2, b3, b4, System.Windows.Media.Brushes.Yellow, true);


            double stepVal = double.TryParse(txtStep.Text, out double s) ? s : 2.0;
            int count = Math.Max(8, Math.Min(40, (int)(22.0 / Math.Max(0.2, stepVal))));
            string style = StrokeStyle;
            string curType = CurtainType;

            for (int i = 0; i <= count; i++)
            {
                double t = (double)i / count;
                WpfPoint topPt = GetPointOnBezier(t, p1, p2, p3, p4);
                WpfPoint botPt = GetPointOnBezier(t, b1, b2, b3, b4);

                bool auto = chkAutoOrient.IsChecked == true;
                WpfPoint start = (auto && t > 0.5) ? botPt : topPt;
                WpfPoint end = (auto && t > 0.5) ? topPt : botPt;

                bool isShort = false;
                if (style == "Long / 1 Short") isShort = (i % 2 != 0);
                else if (style == "Long / 2 Shorts") isShort = (i % 3 != 0);

                double ratio = double.TryParse(txtRatio.Text, out double r) ? r / 100.0 : 0.5;

                double fullLen = Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2));
                double offsetVal = double.TryParse(txtOffset.Text, out double off) ? off * 4 : 0;

                double drawLen = fullLen - offsetVal;
                if (style != "Fixed" && isShort) drawLen *= ratio;
                drawLen = Math.Max(0, drawLen);

                Vector dir = end - start;
                dir.Normalize();
                WpfPoint drawEnd = start + dir * drawLen;

                if (IsTriangle && !isShort)
                {
                    Vector perp = new Vector(-dir.Y, dir.X);
                    double halfW = 4;

                    WpfPoint pL = start + perp * halfW;
                    WpfPoint pR = start - perp * halfW;

                    Polygon poly = new Polygon();
                    poly.Points.Add(pL);
                    poly.Points.Add(pR);
                    poly.Points.Add(drawEnd);
                    poly.Stroke = new SolidColorBrush(previewColor);
                    poly.StrokeThickness = 1.0;
                    previewCanvas.Children.Add(poly);
                }
                else
                {
                    WpfLine ln = new WpfLine
                    {
                        X1 = start.X,
                        Y1 = start.Y,
                        X2 = drawEnd.X,
                        Y2 = drawEnd.Y,
                        Stroke = new SolidColorBrush(previewColor),
                        StrokeThickness = 1.6
                    };
                    previewCanvas.Children.Add(ln);
                }

                if (isShort)
                {
                    if (curType == "Circle")
                    {
                        WpfEllipse dot = new WpfEllipse { Width = 4, Height = 4, Fill = new SolidColorBrush(previewColor) };
                        WpfPoint dotPos = drawEnd + dir * 5;
                        Canvas.SetLeft(dot, dotPos.X - 2);
                        Canvas.SetTop(dot, dotPos.Y - 2);
                        previewCanvas.Children.Add(dot);
                    }
                    else if (curType == "Tick (T-Shape)")
                    {
                        Vector perp = new Vector(-dir.Y, dir.X);
                        double tickHalf = 3;
                        WpfPoint pL = drawEnd + perp * tickHalf;
                        WpfPoint pR = drawEnd - perp * tickHalf;
                        WpfLine tickLn = new WpfLine { X1 = pL.X, Y1 = pL.Y, X2 = pR.X, Y2 = pR.Y, Stroke = new SolidColorBrush(previewColor), StrokeThickness = 1.6 };
                        previewCanvas.Children.Add(tickLn);
                    }
                }
            }
        }

        private void DrawPreviewCurve(WpfPoint st, WpfPoint c1, WpfPoint c2, WpfPoint en, System.Windows.Media.Brush color, bool isDashed = false)
        {
            Path p = new Path { Stroke = color, StrokeThickness = 2 };

            // >>> წყვეტილი ხაზის ლოგიკა <<<
            if (isDashed)
            {
                p.StrokeDashArray = new DoubleCollection { 9, 4 }; // 9 პიქსელი ხაზი, 4 პიქსელი გამოტოვება
            }

            PathGeometry g = new PathGeometry();
            PathFigure f = new PathFigure { StartPoint = st };
            f.Segments.Add(new BezierSegment(c1, c2, en, true));
            g.Figures.Add(f);
            p.Data = g;
            previewCanvas.Children.Add(p);
        }

        private WpfPoint GetPointOnBezier(double t, WpfPoint p0, WpfPoint p1, WpfPoint p2, WpfPoint p3)
        {
            double u = 1 - t;
            double tt = t * t;
            double uu = u * u;
            double uuu = uu * u;
            double ttt = tt * t;

            WpfPoint p = new WpfPoint();
            p.X = uuu * p0.X + 3 * uu * t * p1.X + 3 * u * tt * p2.X + ttt * p3.X;
            p.Y = uuu * p0.Y + 3 * uu * t * p1.Y + 3 * u * tt * p2.Y + ttt * p3.Y;
            return p;
        }
    }
}