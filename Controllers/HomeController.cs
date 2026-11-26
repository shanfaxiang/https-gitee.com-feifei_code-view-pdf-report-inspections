using Dezhen.PdfViewer.Models;
using Dezhen.PdfViewer.Services;
using Microsoft.AspNetCore.Mvc;

namespace Dezhen.PdfViewer.Controllers;

public class HomeController : Controller
{
    // report_type：3 = 检查（PACS），4 = 检验（LIS）
    private const int ReportTypePacs = 3;
    private const int ReportTypeLis = 4;

    private readonly IReportService _reportService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<HomeController> _logger;

    public HomeController(IReportService reportService, IConfiguration configuration, ILogger<HomeController> logger)
    {
        _reportService = reportService;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// 按报告类型获取 PDF 根目录与源根映射（统一读取 Pdf:Roots:{reportType} 配置）。
    /// 例如生产环境：LIS(4) -> \\172.16.25.16\lispdf，PACS(3) -> \\172.16.25.37\pacspdf
    /// </summary>
    private (string Root, IReadOnlyList<string> Sources) GetPdfRoot(int reportType)
    {
        var section = _configuration.GetSection($"Pdf:Roots:{reportType}");
        var root = section["AllowedRoot"];
        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidOperationException($"缺少配置 Pdf:Roots:{reportType}:AllowedRoot");

        var sources = section.GetSection("SourceRoots").Get<string[]>() ?? Array.Empty<string>();
        return (root, sources);
    }

    /// <summary>解析 report_type，必须为 pacs（检查）或 lis（检验），映射为内部类型 3/4</summary>
    private static bool TryParseReportType(string? raw, out int reportType)
    {
        reportType = 0;
        switch ((raw ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "pacs":
                reportType = ReportTypePacs; // 3
                return true;
            case "lis":
                reportType = ReportTypeLis;  // 4
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// 主页：/Home/Index?register_id=xxx&report_no=yyy&report_type=lis
    /// report_no 非必填；report_type 必填：pacs=检查、lis=检验
    /// </summary>
    public async Task<IActionResult> Index(string? register_id, string? report_no, string? report_type)
    {
        var vm = new IndexViewModel
        {
            RegisterId = register_id?.Trim() ?? string.Empty,
            ReportNo = report_no?.Trim() ?? string.Empty
        };

        // report_type 必填，且必须为 pacs 或 lis
        if (!TryParseReportType(report_type, out var reportType))
        {
            vm.ErrorMessage = "缺少或非法的必填参数 report_type，取值：pacs=检查报告、lis=检验报告。示例：/Home/Index?register_id=xxx&report_type=lis";
            return View(vm);
        }
        vm.ReportType = reportType;

        if (string.IsNullOrEmpty(vm.RegisterId))
        {
            vm.ErrorMessage = "缺少必填参数 register_id（就诊号），请通过 URL 传入，例如：/Home/Index?report_type=lis&register_id=117236&report_no=R20260928001";
            return View(vm);
        }

        try
        {
            vm.Reports = await _reportService.GetReportsAsync(vm.RegisterId,
                string.IsNullOrEmpty(vm.ReportNo) ? null : vm.ReportNo, reportType);

            if (vm.Reports.Count == 0)
                vm.ErrorMessage = $"未找到就诊号 {vm.RegisterId} 对应的报告记录。";
        }
        catch (Npgsql.NpgsqlException ex)
        {
            _logger.LogError(ex, "查询报告列表失败 register_id={RegisterId} report_type={ReportType}", vm.RegisterId, reportType);
            vm.ErrorMessage = "查询数据库失败，请稍后重试或联系管理员。";
            if (HttpContext.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment())
                vm.ErrorMessage += $"<br/><b>原因：</b>{ex.Message}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询报告列表失败 register_id={RegisterId} report_type={ReportType}", vm.RegisterId, reportType);
            vm.ErrorMessage = "查询数据库失败，请稍后重试或联系管理员。";
            if (HttpContext.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment())
                vm.ErrorMessage += $"<br/><b>原因：</b>{ex.Message}";
        }

        return View(vm);
    }

    /// <summary>PDF 流式输出：/Home/Pdf?register_id=xxx&report_no=yyy&report_type=4</summary>
    public async Task<IActionResult> Pdf(string? register_id, string? report_no, string? report_type)
    {
        if (string.IsNullOrEmpty(register_id))
            return BadRequest("缺少 register_id 参数");

        if (!TryParseReportType(report_type, out var reportType))
            return BadRequest("report_type 参数缺失或非法，取值：pacs=检查报告、lis=检验报告");

        var (root, sources) = GetPdfRoot(reportType);

        try
        {
            var item = await _reportService.FindReportFileAsync(register_id.Trim(),
                string.IsNullOrEmpty(report_no) ? null : report_no.Trim(), reportType, root, sources);

            if (item == null)
                return NotFound("未找到对应的报告记录");

            var path = item.ReportUrl;
            if (!System.IO.File.Exists(path))
                return NotFound($"报告文件不存在：{item.FileName}");

            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 64 * 1024, useAsync: true);
            return File(stream, "application/pdf", enableRangeProcessing: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "读取PDF失败 register_id={RegisterId} report_no={ReportNo}", register_id, report_no);
            return StatusCode(500, "读取报告文件失败");
        }
    }

    public IActionResult Error() => View();
}
