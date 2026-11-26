using Dezhen.PdfViewer.Models;

namespace Dezhen.PdfViewer.Models;

/// <summary>首页视图模型</summary>
public class IndexViewModel
{
    public string RegisterId { get; set; } = string.Empty;
    public string ReportNo { get; set; } = string.Empty;
    /// <summary>报告类型：3=检查(pacs)、4=检验(lis)，默认 4</summary>
    public int ReportType { get; set; } = 4;

    /// <summary>是否为检查报告（pacs）</summary>
    public bool IsPacs => ReportType == 3;

    /// <summary>页面标题</summary>
    public string Title => IsPacs ? "检查报告浏览" : "检验报告浏览";

    public List<ReportItem> Reports { get; set; } = new();
    public string? ErrorMessage { get; set; }
}
