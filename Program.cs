using Dezhen.PdfViewer.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// PostgreSQL 数据源（连接池）
builder.Services.AddSingleton(sp =>
    new Npgsql.NpgsqlDataSourceBuilder(
            builder.Configuration.GetConnectionString("Default"))
        .Build());

builder.Services.AddScoped<IReportService, ReportService>();

// 启动时按配置对 UNC 共享目录建立凭据连接
builder.Services.AddHostedService<UncShareConnector>();

var app = builder.Build();
app.UsePathBase("/pdfviewerweb"); // 增加这一行，放在最前面！

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseStaticFiles();
app.UseRouting();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
