// Aliases first
using Win32 = Microsoft.Win32;                     // WPF dialogs
using WinForms = System.Windows.Forms;             // WinForms types (Pdfium viewer host)
using PdfSharpDocument = PdfSharp.Pdf.PdfDocument; // avoid PdfDocument ambiguity
using PdfiumDoc = PdfiumViewer.PdfDocument;        // avoid PdfDocument ambiguity
using WpfMessageBox = System.Windows.MessageBox;
using IOPath = System.IO.Path;

using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;                        // Color
using PdfiumViewer;                                // PdfViewer control
using System.Windows.Forms.Integration;            // WindowsFormsHost
using PdfSharp.Drawing;
using PdfSharp.Pdf.IO;




namespace PDFEditor
{
    public partial class MainWindow : Window
    {
        private PdfViewer _viewer;
        private string? _currentFilePath;
        private string? _workingCopyPath;

        // app-level text color (used even if ColorPreview is not present)
        private Color _currentTextColor = Colors.Black;

        public MainWindow()
        {
            InitializeComponent();

            // --- Optional UI wiring (only if controls exist in XAML) ---
            var cmb = FindName("CmbFont") as ComboBox;
            if (cmb != null)
            {
                cmb.ItemsSource = Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(n => n).ToList();
                cmb.SelectedItem = "Arial";
            }

            // BtnPickColor_Click: after choosing color, update preview
            var colorEl = FindName("ColorPreview") as FrameworkElement;
            if (colorEl is Border b1)
                b1.Background = new SolidColorBrush(_currentTextColor);
            else if (colorEl is System.Windows.Shapes.Rectangle r1)
                r1.Fill = new SolidColorBrush(_currentTextColor);



            // --- Pdfium viewer host ---
            _viewer = new PdfViewer
            {
                Dock = WinForms.DockStyle.Fill,
                ShowToolbar = false
            };
            var panel = new WinForms.Panel { Dock = WinForms.DockStyle.Fill };
            panel.Controls.Add(_viewer);
            ((WindowsFormsHost)FindName("WinFormsHost")!).Child = panel;

            CreateNewWorkingDoc();
        }

        // If you hooked a button to this:
        private void BtnPickColor_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new WinForms.ColorDialog();
            if (dlg.ShowDialog() == WinForms.DialogResult.OK)
            {
                _currentTextColor = Color.FromArgb(dlg.Color.A, dlg.Color.R, dlg.Color.G, dlg.Color.B);

                // BtnPickColor_Click: after choosing color, update preview
                var colorEl2 = FindName("ColorPreview") as FrameworkElement;
                if (colorEl2 is Border b2)
                    b2.Background = new SolidColorBrush(_currentTextColor);
                else if (colorEl2 is System.Windows.Shapes.Rectangle r2)
                    r2.Fill = new SolidColorBrush(_currentTextColor);


            }
        }

        private void NewPdf_Click(object sender, RoutedEventArgs e)
        {
            _currentFilePath = null;
            CreateNewWorkingDoc();
        }

        private void OpenPdf_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new Win32.OpenFileDialog { Filter = "PDF files (*.pdf)|*.pdf", Title = "Open PDF" };
            if (ofd.ShowDialog() == true)
            {
                _currentFilePath = ofd.FileName;
                _workingCopyPath = CopyToTemp(_currentFilePath);
                LoadInViewer(_workingCopyPath);
            }
        }

        private void AddText_Click(object sender, RoutedEventArgs e)
        {
            if (_workingCopyPath == null || !File.Exists(_workingCopyPath))
                CreateNewWorkingDoc();

            string text = GetText("TxtContent", "Hello from PDFEditor!");
            int pageNum = GetInt("TxtPage", 1, min: 1);
            double posX = GetDouble("TxtX", 36);
            double posY = GetDouble("TxtY", 36);
            double sizePt = GetDouble("TxtSize", 16);
            string fontName = GetFontName();
            var style = GetFontStyle();

            // release Pdfium lock
            if (_viewer.Document != null) { _viewer.Document.Dispose(); _viewer.Document = null; }

            using (var doc = PdfReader.Open(_workingCopyPath!, PdfDocumentOpenMode.Modify))
            {
                while (doc.PageCount < pageNum) doc.AddPage();
                var page = doc.Pages[pageNum - 1];

                var font = new XFont(fontName, sizePt, style);
                var xc = XColor.FromArgb(_currentTextColor.A, _currentTextColor.R, _currentTextColor.G, _currentTextColor.B);
                var brush = new XSolidBrush(xc);

                using (var gfx = XGraphics.FromPdfPage(page))
                {
                    gfx.DrawString(text, font, brush, new XPoint(posX, posY));
                }

                doc.Save(_workingCopyPath!);
            }

            LoadInViewer(_workingCopyPath!);
        }

        private void SaveAs_Click(object sender, RoutedEventArgs e)
        {
            if (_workingCopyPath == null || !File.Exists(_workingCopyPath))
            {
                WpfMessageBox.Show("Nothing to save yet.");
                return;
            }

            var sfd = new Win32.SaveFileDialog { Filter = "PDF files (*.pdf)|*.pdf", Title = "Save PDF As" };
            if (sfd.ShowDialog() == true)
            {
                File.Copy(_workingCopyPath!, sfd.FileName, overwrite: true);
                _currentFilePath = sfd.FileName;
                WpfMessageBox.Show("Saved.", "PDFEditor");
            }
        }

        // ---------- helpers ----------
        private void CreateNewWorkingDoc()
        {
            var tempPath = IOPath.Combine(IOPath.GetTempPath(), $"PDFEditor_{Guid.NewGuid():N}.pdf");

            using (var doc = new PdfSharpDocument())
            {
                doc.Info.Title = "New Document";
                doc.AddPage();
                doc.Save(tempPath);
            }
            _workingCopyPath = tempPath;
            LoadInViewer(_workingCopyPath);
        }

        private string CopyToTemp(string sourcePath)
        {
            var tempPath = IOPath.Combine(Path.GetTempPath(), $"PDFEditor_{Guid.NewGuid():N}.pdf");
            File.Copy(sourcePath, tempPath, overwrite: true);
            return tempPath;
        }

        private void LoadInViewer(string path)
        {
            _viewer.Document?.Dispose();
            var bytes = File.ReadAllBytes(path);
            var ms = new MemoryStream(bytes); // owned by Pdfium
            _viewer.Document = PdfiumDoc.Load(ms);
        }

        protected override void OnClosed(EventArgs e)
        {
            _viewer.Document?.Dispose();
            base.OnClosed(e);
        }

        // ---------- UI getters that tolerate missing controls ----------
        private string GetText(string name, string fallback)
            => (FindName(name) as TextBox)?.Text is string s && !string.IsNullOrWhiteSpace(s) ? s : fallback;

        private int GetInt(string name, int fallback, int min = int.MinValue, int max = int.MaxValue)
        {
            var tb = FindName(name) as TextBox;
            return (tb != null && int.TryParse(tb.Text, out int v)) ? Math.Clamp(v, min, max) : fallback;
        }

        private double GetDouble(string name, double fallback)
        {
            var tb = FindName(name) as TextBox;
            return (tb != null && double.TryParse(tb.Text, out double v)) ? v : fallback;
        }

        private string GetFontName()
        {
            var cmb = FindName("CmbFont") as ComboBox;
            return (cmb?.SelectedItem as string) ?? "Arial";
        }

        private XFontStyleEx GetFontStyle()
        {
            bool bold = (FindName("ChkBold") as CheckBox)?.IsChecked == true;
            bool italic = (FindName("ChkItalic") as CheckBox)?.IsChecked == true;

            XFontStyleEx style = XFontStyleEx.Regular;
            if (bold) style |= XFontStyleEx.Bold;
            if (italic) style |= XFontStyleEx.Italic;
            return style;
        }
    }
}
