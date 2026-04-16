using API.Classes;
using API.Classes.LDAP;
using API.Classes.Reporting;
using API.Classes.VMware;
using API.Models.MIS;
using API.Models.FEB_CMS;
using API.Models.AppAudit;
using API.Services.FEB_CMS;
using API.Services.LDAP;
using API.Services.Veeam;
using API.Services.VMware;
using API.Services.AppAudit;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using NLog;
using NLog.Web;
using System.Text;

// Early init of NLog to allow startup and exception logging, before host is built
var logger = NLog.LogManager.Setup().LoadConfigurationFromAppSettings().GetCurrentClassLogger();
logger.Debug("init main");

try
{
    //**************** Add services to the container. ****************
    var builder = WebApplication.CreateBuilder(args);

    // [組態] 開發環境載入 secrets.dev.json（含 DB 密碼等機密，不進 Git）
    if (builder.Environment.IsDevelopment())
    {
        logger.Debug("開發環境：載入 secrets.dev.json");
        builder.Configuration.AddJsonFile("secrets.dev.json", optional: true, reloadOnChange: true);
    }
    // [組態] 正式環境：機密由 Windows 系統環境變數提供，IIS 重啟後生效


    // [資料庫] 使用 EF Core 連接 MISContext 與 FEB_CMSContext（Scoped 生命週期）
    builder.Services.AddDbContext<MISContext>(options =>
        options.UseSqlServer(builder.Configuration.GetConnectionString("MISContext")));
    builder.Services.AddDbContext<FEB_CMSContext>(options =>
        options.UseSqlServer(builder.Configuration.GetConnectionString("FEB_CMSContext")));

    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    // [強型別設定] 將 appsettings.json 各 Section 綁定至 IOptions<T> 供注入使用
    builder.Services.Configure<LdapConfig>(builder.Configuration.GetSection("LdapConfig"));
    builder.Services.Configure<VMwareConfig>(builder.Configuration.GetSection("VMwareConfig"));
    builder.Services.Configure<AdAuditConfig>(builder.Configuration.GetSection("AdAuditSettings"));
    builder.Services.Configure<AppAuditSettings>(builder.Configuration.GetSection("AppAuditSettings")); // 應用系統稽核（組態驅動）

    // [DI 服務] AddScoped = 每個 HTTP 請求取得同一個實例，請求結束後釋放
    builder.Services.AddScoped<IMailSend, MailSend>();
    builder.Services.AddScoped<ILdapService, LdapService>();
    builder.Services.AddScoped<IFebCmsUserService, FebCmsUserService>();
    builder.Services.AddScoped<API.Services.Database.IDatabaseAuditService, API.Services.Database.DatabaseAuditService>();
    builder.Services.AddScoped<API.Services.VMware.IVmAuditService, API.Services.VMware.VmAuditService>();
    builder.Services.AddScoped<IAppAuditService, AppAuditService>();
    builder.Services.AddScoped<IVeeamService, VeeamService>();
    builder.Services.AddScoped<IVMwareService, VMwareService>();
    builder.Services.AddScoped<IExcelService, ExcelService>();

    // [日誌] 使用 NLog 取代 .NET 內建日誌
    builder.Logging.ClearProviders();
    builder.Host.UseNLog();

    // [Swagger] 載入 XML 文件以顯示 API 說明（需在專案設定啟用 XML 輸出）
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    builder.Services.AddSwaggerGen(c => c.IncludeXmlComments(xmlPath));

    // [HTTP Client] NoSSL = 略過憑證驗證（用於內網服務）；預設 = 有 SSL 驗證
    builder.Services.AddHttpClient("NoSSL")
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
        });
    builder.Services.AddHttpClient();

    /*****************************************************************/
    var app = builder.Build();

    // [中介軟體] 開發環境也啟用 Swagger（不限環境）
    app.UseSwagger();
    app.UseSwaggerUI();

    //app.UseHttpsRedirection();

    app.UseAuthorization();
    app.MapControllers();
    app.Run();
}
catch (Exception ex)
{
    // 捕捉啟動階段的致命錯誤
    logger.Error(ex, "Program 啟動失敗");
    throw;
}
finally
{
    // 確保 NLog 緩衝區完整寫出後關閉
    LogManager.Shutdown();
}
