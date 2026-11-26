using Dezhen.PdfViewer.Models;

namespace Dezhen.PdfViewer.Services;

public interface IReportService
{
    /// <summary>按就诊号（必填）、报告号（可空）、报告类型查询报告记录</summary>
    Task<List<ReportItem>> GetReportsAsync(string registerId, string? reportNo, int reportType);

    /// <summary>查询单条报告并返回存在的物理文件全路径；未找到返回 null</summary>
    /// <param name="allowedRoot">本地实际允许访问的根目录，如 D:\lispdf</param>
    /// <param name="sourceRoots">report_url 中可能出现的源根前缀（如 E:\lispdf），命中后映射到 allowedRoot</param>
    Task<ReportItem?> FindReportFileAsync(string registerId, string? reportNo, int reportType, string allowedRoot, IReadOnlyList<string> sourceRoots);
}

public class ReportService : IReportService
{
    private readonly Npgsql.NpgsqlDataSource _dataSource;
    private readonly ILogger<ReportService> _logger;

    public ReportService(Npgsql.NpgsqlDataSource dataSource, ILogger<ReportService> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    public async Task<List<ReportItem>> GetReportsAsync(string registerId, string? reportNo, int reportType)
    {
        // report_no 为空时展示该就诊号下所有有效报告（report_status in (1,3)）
        // 报告类型：3=检查(pacs)、4=检验(lis)，通过 report_type 列过滤
        const string sql = """
            SELECT register_id, report_no, report_url, report_title
            FROM public.op_order_report
            WHERE register_id = @register_id
              AND report_type = @report_type
              AND (@report_no IS NOT NULL OR report_status IN (1, 3))
              AND (@report_no IS NULL OR report_no = @report_no)
            ORDER BY report_no ASC
            """;

        var list = new List<ReportItem>();

        // register_id 列为 bigint，必须按数字类型传参
        if (!long.TryParse(registerId.Trim(), out var registerIdValue))
            return list;

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.Add(new Npgsql.NpgsqlParameter("register_id", NpgsqlTypes.NpgsqlDbType.Bigint) { Value = registerIdValue });
        cmd.Parameters.Add(new Npgsql.NpgsqlParameter("report_type", NpgsqlTypes.NpgsqlDbType.Integer) { Value = reportType });
        // 显式指定 report_no 类型，避免 DBNull 时无法推断（42P08）
        cmd.Parameters.Add(new Npgsql.NpgsqlParameter("report_no", NpgsqlTypes.NpgsqlDbType.Text)
        {
            Value = string.IsNullOrWhiteSpace(reportNo) ? DBNull.Value : reportNo.Trim()
        });

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var url = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
            var title = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
            var no = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            list.Add(new ReportItem
            {
                RegisterId = reader.GetInt64(0).ToString(),
                ReportNo = no,
                ReportTitle = title,
                ReportUrl = url,
                FileName = SafeFileName(url),
                ReportDate = ParseDateFromPath(url),
                DateDisplay = ParseDateFromPath(url)?.ToString("yyyy-MM-dd") ?? "未知日期"
            });
        }
        return list;
    }

    public async Task<ReportItem?> FindReportFileAsync(string registerId, string? reportNo, int reportType, string allowedRoot, IReadOnlyList<string> sourceRoots)
    {
        var list = await GetReportsAsync(registerId, reportNo, reportType);
        if (list.Count == 0) return null;

        // report_no 为空时取第一条
        foreach (var item in list)
        {
            var fullPath = ResolveSafePath(item.ReportUrl, allowedRoot, sourceRoots);
            if (fullPath != null && File.Exists(fullPath))
            {
                item.ReportUrl = fullPath; // 已解析为本地安全物理路径
                return item;
            }
        }
        return list[0]; // 有记录但文件不存在，返回记录用于提示
    }

    /// <summary>
    /// 解析 report_url 到本地安全物理路径（跨平台：Windows 盘符源根 / Linux 挂载点）：
    /// 1) 命中源根前缀（如 E:\lispdf）→ 映射到本地根（Windows: D:\lispdf；Linux: /mnt/lispdf）
    /// 2) 相对路径 → 拼到本地根
    /// 3) 绝对路径无映射 → 必须已在本地根内
    /// 全程校验结果必须在本地根内（防目录穿越）。
    /// </summary>
    private static string? ResolveSafePath(string reportUrl, string allowedRoot, IReadOnlyList<string> sourceRoots)
    {
        if (string.IsNullOrWhiteSpace(reportUrl)) return null;
        try
        {
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var root = Path.GetFullPath(allowedRoot);

            // 统一用 '/' 规范化源路径与源根（兼容 Windows 反斜杠在 Linux 上的处理）
            var urlNorm = reportUrl.Trim().Replace('\\', '/').TrimEnd('/');
            var sourceRootsNorm = sourceRoots
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim().Replace('\\', '/').TrimEnd('/'))
                .Where(s => s.Length > 0)
                .ToList();

            // 1) 源根前缀映射
            foreach (var src in sourceRootsNorm)
            {
                if (urlNorm.StartsWith(src, StringComparison.OrdinalIgnoreCase))
                {
                    var relative = urlNorm.Substring(src.Length).TrimStart('/');
                    var mapped = Path.GetFullPath(Path.Combine(root, relative));
                    return EnsureWithin(mapped, root, comparison) ? mapped : null;
                }
            }

            // 2) 相对路径
            if (!Path.IsPathRooted(urlNorm))
            {
                var mapped = Path.GetFullPath(Path.Combine(root, urlNorm));
                return EnsureWithin(mapped, root, comparison) ? mapped : null;
            }

            // 3) 绝对路径且无映射
            var full = Path.GetFullPath(urlNorm);
            return EnsureWithin(full, root, comparison) ? full : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool EnsureWithin(string path, string root, StringComparison comparison)
    {
        var p = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var r = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return p.StartsWith(r, comparison);
    }

    private static string SafeFileName(string url) => url.Replace('\\', '/').Split('/').LastOrDefault() ?? url;

    /// <summary>从路径目录段解析日期，如 E:\lispdf\20260928\117236.pdf -> 2026-09-28</summary>
    private static DateTime? ParseDateFromPath(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var segments = url.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = segments.Length - 2; i >= 0; i--)
        {
            var seg = segments[i];
            if (seg.Length == 8 && long.TryParse(seg, out _))
            {
                if (DateTime.TryParseExact(seg, "yyyyMMdd", null,
                        System.Globalization.DateTimeStyles.None, out var d))
                    return d;
            }
        }
        return null;
    }
}
