using API.Classes;
using API.Classes.LDAP;
using API.Classes.Reporting;
using API.Classes.VMware;
using API.Models;
using API.Services.FEB_CMS;
using API.Services.LDAP;
using API.Services.Veeam;
using API.Services.VMware;
using Microsoft.EntityFrameworkCore;
using NLog;
using NLog.Web;

// Early init of NLog to allow startup and exception logging, before host is built
var logger = NLog.LogManager.Setup().LoadConfigurationFromAppSettings().GetCurrentClassLogger();
logger.Debug("init main");

try
{
    //**************** Add services to the container. ****************
    var builder = WebApplication.CreateBuilder(args);

    // 將 資料庫Context 加入服務容器中，以便在應用程式中進行依賴注入 (程式要使用建構子Constructure才能真正注入)
    builder.Services.AddDbContext<MISContext>(options =>
        options.UseSqlServer(builder.Configuration.GetConnectionString("MISContext")));
    builder.Services.AddDbContext<FEB_CMSContext>(options =>
        options.UseSqlServer(builder.Configuration.GetConnectionString("FEB_CMSContext")));

    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    // 將 Config 類別與 appsettings.json 中的 "...Config" 區塊綁定
    builder.Services.Configure<LdapConfig>(builder.Configuration.GetSection("LdapConfig"));
    builder.Services.Configure<VMwareConfig>(builder.Configuration.GetSection("VMwareConfig"));
    builder.Services.Configure<AdAuditConfig>(builder.Configuration.GetSection("AdAuditSettings"));

    // ** 在這裡(DI)註冊我們所有的自訂服務 **
    // AddScoped 的意思是：在同一個 HTTP 請求的生命週期中，所有的要求都會拿到同一個物件。
    builder.Services.AddScoped<IMailSend, MailSend>();
    builder.Services.AddScoped<ILdapService, LdapService>();
    builder.Services.AddScoped<IFebCmsUserService, FebCmsUserService>();
    builder.Services.AddScoped<IVeeamService, VeeamService>();
    builder.Services.AddScoped<IVMwareService, VMwareService>();
    builder.Services.AddScoped<IExcelService, ExcelService>();

    // NLog 基礎設定 NLog: Setup NLog for Dependency injection
    builder.Logging.ClearProviders();
    builder.Host.UseNLog();

    // 獲取生成的 XML 文件的路徑(XML 文檔通常會被生成到項目的輸出目錄，例如 bin/Debug/net6.0/{YourProjectName}.xml)
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    builder.Services.AddSwaggerGen(c => c.IncludeXmlComments(xmlPath));// 配置 Swagger，將生成的 XML 文檔包含進去

    builder.Services.AddHttpClient("NoSSL")
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true // 忽略 SSL 驗證
        });

    builder.Services.AddHttpClient(); // 預設的 SSL 驗證

    /*****************************************************************/
    var app = builder.Build();

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        // 開發環境下的其他配置（例如：詳細錯誤頁面等）
    }
    app.UseSwagger();
    app.UseSwaggerUI();

    //app.UseHttpsRedirection();

    app.UseAuthorization();

    app.MapControllers();

    app.Run();
}
catch (Exception ex)
{
    // 捕獲例外並記錄
    logger.Error(ex, "Program 停止意外");
    throw;
}
finally
{
    // 確保 NLog 資源釋放
    LogManager.Shutdown();
}