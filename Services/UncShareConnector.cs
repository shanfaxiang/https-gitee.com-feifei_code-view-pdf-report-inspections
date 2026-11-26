using System.Runtime.InteropServices;

namespace Dezhen.PdfViewer.Services;

/// <summary>
/// 启动时按配置（Pdf:Roots + Pdf:Credentials）对 UNC 共享目录建立凭据连接，
/// 使程序无需在部署机手动执行 net use 即可访问远程共享。
/// 仅在 Windows 上生效；凭据 key 与 Roots key（报告类型 3/4）一一对应。
/// </summary>
public class UncShareConnector : IHostedService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<UncShareConnector> _logger;

    public UncShareConnector(IConfiguration configuration, ILogger<UncShareConnector> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            _logger.LogWarning("当前操作系统非 Windows，跳过 UNC 共享凭据连接");
            return Task.CompletedTask;
        }

        var roots = _configuration.GetSection("Pdf:Roots").GetChildren();
        foreach (var root in roots)
        {
            var path = root["AllowedRoot"];
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith(@"\\", StringComparison.Ordinal))
                continue; // 仅处理 UNC 路径，本地磁盘无需凭据

            var cred = _configuration.GetSection($"Pdf:Credentials:{root.Key}");
            var user = cred["User"];
            var password = cred["Password"];
            if (string.IsNullOrWhiteSpace(user))
            {
                _logger.LogInformation("类型 {Type} 的共享 {Path} 未配置凭据，跳过连接", root.Key, path);
                continue;
            }

            var result = NativeWNet.Connect(path, password, user);
            _logger.LogInformation("UNC 共享连接 {Path}（账号 {User}）：{Result}", path, user, result);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal static class NativeWNet
{
    private const int RESOURCETYPE_DISK = 1;
    private const int ERROR_SUCCESS = 0;
    private const int ERROR_ALREADY_ASSIGNED = 85;
    private const int ERROR_SESSION_CREDENTIAL_CONFLICT = 1219;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NETRESOURCE
    {
        public int dwScope;
        public int dwType;
        public int dwDisplayType;
        public int dwUsage;
        public string? lpLocalName;
        public string lpRemoteName;
        public string? lpComment;
        public string? lpProvider;
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetAddConnection2(ref NETRESOURCE resource, string? password, string? username, int flags);

    /// <summary>建立到 UNC 共享的凭据连接，返回结果描述</summary>
    public static string Connect(string uncPath, string? password, string? username)
    {
        var resource = new NETRESOURCE
        {
            dwType = RESOURCETYPE_DISK,
            lpRemoteName = uncPath.TrimEnd('\\')
        };

        var rc = WNetAddConnection2(ref resource, password, username, 0);
        return rc switch
        {
            ERROR_SUCCESS => "连接成功",
            ERROR_ALREADY_ASSIGNED => "已存在连接，可正常访问",
            ERROR_SESSION_CREDENTIAL_CONFLICT => "已存在其他凭据的会话，将沿用现有连接",
            _ => $"连接失败（错误码 {rc}）"
        };
    }
}
