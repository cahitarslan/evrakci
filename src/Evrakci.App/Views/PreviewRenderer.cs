using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Evrakci.Core.Templates;
using WpfParagraph = System.Windows.Documents.Paragraph;
using WpfTable = System.Windows.Documents.Table;

namespace Evrakci.App.Views;

/// <summary>Turns the engine's <see cref="PreviewDocument"/> into a simplified, highlighted FlowDocument.</summary>
public sealed class PreviewRenderer(FrameworkElement resources, Action<PreviewSegment, string> onSuffixClick)
{
    Brush B(string key) => (Brush)resources.FindResource(key);

    public FlowDocument Render(PreviewDocument doc)
    {
        var flow = new FlowDocument
        {
            FontFamily = new FontFamily("Times New Roman"),
            FontSize = 15,
            PagePadding = new Thickness(56, 44, 56, 56),
            Background = B("PaperBrush"),
            Foreground = B("PaperTextBrush"),
            TextAlignment = TextAlignment.Left,
            ColumnWidth = double.PositiveInfinity,
            IsOptimalParagraphEnabled = true,
        };
        foreach (var section in doc.Sections)
        {
            if (section.Title is not null)
            {
                flow.Blocks.Add(new WpfParagraph(new Run(section.Title.ToUpperInvariant()))
                {
                    FontFamily = (FontFamily)Application.Current.Resources["BrandSans"],
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = B("RemovedTextBrush"),
                    Margin = new Thickness(0, 24, 0, 6),
                    BorderBrush = B("RemovedTextBrush"),
                    BorderThickness = new Thickness(0, 0, 0, 0.5),
                });
            }
            foreach (var block in section.Blocks) flow.Blocks.Add(ToBlock(block));
        }
        return flow;
    }

    Block ToBlock(PreviewBlock block) => block switch
    {
        PreviewParagraph p => ToParagraph(p),
        PreviewTable t => ToTable(t),
        _ => new WpfParagraph(),
    };

    Block ToParagraph(PreviewParagraph p)
    {
        var para = new WpfParagraph
        {
            Margin = new Thickness(0, 0, 0, 4),
            TextAlignment = p.Alignment switch
            {
                PreviewAlignment.Center => TextAlignment.Center,
                PreviewAlignment.Right => TextAlignment.Right,
                PreviewAlignment.Justify => TextAlignment.Justify,
                _ => TextAlignment.Left,
            },
        };
        if (p.Bullet) para.Inlines.Add(new Run("•  "));
        string lastValue = "";
        foreach (var s in p.Segments)
        {
            switch (s.Kind)
            {
                case SegmentKind.Literal:
                    AddText(para.Inlines, s, s.Text);
                    break;
                case SegmentKind.Value:
                    lastValue = s.Text;
                    var v = Styled(new Run(s.Text), s);
                    v.Background = B("ValueHighlightBrush");
                    v.ToolTip = s.Variable;
                    para.Inlines.Add(v);
                    break;
                case SegmentKind.Suffix:
                {
                    var seg = s;
                    var value = lastValue;
                    var link = new Hyperlink(Styled(new Run(s.Text.Length == 0 ? "∅" : s.Text), s))
                    {
                        Background = B(s.Overridden ? "SuffixOverriddenBrush" : "SuffixHighlightBrush"),
                        Foreground = B("PaperTextBrush"),
                        TextDecorations = null,
                        Cursor = Cursors.Hand,
                        ToolTip = s.Overridden
                            ? $"Elle düzeltildi. Otomatik: ’{s.AutoSuffix}'. Değiştirmek için tıklayın."
                            : $"Şablondaki ek: ’{s.TemplateSuffix}'. Düzeltmek için tıklayın.",
                    };
                    link.Click += (_, _) => onSuffixClick(seg, value);
                    para.Inlines.Add(link);
                    break;
                }
                case SegmentKind.Missing:
                    var m = Styled(new Run(s.Text), s);
                    m.Background = B("MissingHighlightBrush");
                    m.Foreground = B("MissingTextBrush");
                    m.ToolTip = "Bu alan henüz girilmedi.";
                    para.Inlines.Add(m);
                    break;
                case SegmentKind.Blank:
                    break;
            }
        }
        if (p.Removed)
        {
            para.Foreground = B("RemovedTextBrush");
            para.TextDecorations = TextDecorations.Strikethrough;
            para.ToolTip = "Boş bırakılan alan nedeniyle bu paragraf belgeden çıkarılacak.";
            foreach (var inline in para.Inlines) inline.Background = null;
        }
        if (para.Inlines.Count == 0) para.Inlines.Add(new Run(""));
        if (!p.InTextBox) return para;
        return new Section(para)
        {
            BorderBrush = B("RemovedTextBrush"),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Padding = new Thickness(10, 0, 0, 0),
            Margin = new Thickness(24, 2, 0, 2),
        };
    }

    static Inline Styled(Inline inline, PreviewSegment s)
    {
        if (s.Bold) inline.FontWeight = FontWeights.Bold;
        if (s.Italic) inline.FontStyle = FontStyles.Italic;
        if (s.Underline) inline.TextDecorations = TextDecorations.Underline;
        return inline;
    }

    static void AddText(InlineCollection inlines, PreviewSegment s, string text)
    {
        var parts = text.Split('\n');
        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0) inlines.Add(new LineBreak());
            if (parts[i].Length > 0) inlines.Add(Styled(new Run(parts[i].Replace("\t", "    ")), s));
        }
    }

    Block ToTable(PreviewTable t)
    {
        var table = new WpfTable { CellSpacing = 0, Margin = new Thickness(0, 6, 0, 8) };
        int cols = t.Rows.Count == 0 ? 1 : t.Rows.Max(r => r.Count);
        for (int i = 0; i < cols; i++) table.Columns.Add(new TableColumn());
        var group = new TableRowGroup();
        foreach (var row in t.Rows)
        {
            var tr = new TableRow();
            foreach (var cell in row)
            {
                var tc = new TableCell
                {
                    BorderBrush = B("RemovedTextBrush"),
                    BorderThickness = new Thickness(0.5),
                    Padding = new Thickness(6, 3, 6, 3),
                };
                foreach (var b in cell) tc.Blocks.Add(ToBlock(b));
                if (tc.Blocks.Count == 0) tc.Blocks.Add(new WpfParagraph());
                tr.Cells.Add(tc);
            }
            group.Rows.Add(tr);
        }
        table.RowGroups.Add(group);
        return table;
    }
}
