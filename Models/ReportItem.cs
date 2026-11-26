namespace Dezhen.PdfViewer.Models;

/// <summary>op_order_report 表记录（报告 PDF）</summary>
public class ReportItem
{
    public string RegisterId { get; set; } = string.Empty;

    public string ReportNo { get; set; } = string.Empty;

    public string ReportTitle { get; set; } = string.Empty;

    /// <summary>侧边栏展示名：report_title-report_no（无标题时回退）</summary>
    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ReportTitle) && !string.IsNullOrWhiteSpace(ReportNo))
                return $"{ReportTitle}-{ReportNo}";
            if (!string.IsNullOrWhiteSpace(ReportTitle))
                return ReportTitle;
            if (!string.IsNullOrWhiteSpace(ReportNo))
                return ReportNo;
            return FileName;
        }
    }

    /// <summary>report_url 原始值，如 E:\lispdf\20260928\117236.pdf</summary>
    public string ReportUrl { get; set; } = string.Empty;

    /// <summary>文件名（不含路径），用于展示</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>从路径解析出的日期（yyyyMMdd 目录），解析失败为 null</summary>
    public DateTime? ReportDate { get; set; }

    /// <summary>日期展示文本，如 2026-09-28</summary>
    public string DateDisplay { get; set; } = "未知日期";
}
