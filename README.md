# IT 自動化 API (IT-Automation-API1)

這是一個 .NET 8 Web API 專案，作為 IT 自動化任務的後端服務中心。它提供了一套 RESTful API 端點，用於管理和查詢內部的 IT 基礎設施，包括 Active Directory、VMware vCenter 和 Veeam 備份。

此專案採用了客製化的 DPAPI 組態架構，以確保「正式環境」的 secrets（如資料庫連線字串、服務帳號密碼）**不會**以明碼形式出現。

## 目錄

- [專案架構](#專案架構)
- [核心功能](#核心功能)
- [技術棧](#技術棧)
- [安裝與設定](#安裝與設定)
- [安全組態設定 (DPAPI 方案)](#安全組態設定-dpapi-方案)
- [API 端點](#api-端點)
- [開發指南](#開發指南)
- [部署](#部署)
- [故障排除](#故障排除)
- [相關專案](#相關專案)

## 專案架構

### API (主專案)

* .NET 8.0 Web API 專案 (`net8.0-windows`)
* 負責處理所有 HTTP 請求、日誌記錄 (NLog) 和依賴注入 (DI)
* 啟動時 (`Program.cs`) 判斷環境，並動態載入「明碼」或「加密」設定檔
* 使用 Windows DPAPI 進行正式環境的設定檔加解密

### 輔助工具（需自行建立）

> ⚠️ **注意：** 基於資安考量，DPAPI 加解密相關的輔助工具**未包含在此**。
> 
> 此專案使用 Windows DPAPI 來保護正式環境的機密設定。您需要自行建立以下兩個輔助專案（或使用替代方案）：

#### 1. DPAPI 加解密函式庫

**用途：** 提供 DPAPI 加密和解密功能的可重用類別庫

**建立方式：**
```bash
# 建立類別庫專案
dotnet new classlib -n Utils.DpapiProvider -f net8.0-windows
cd Utils.DpapiProvider

# 安裝必要套件
dotnet add package System.Security.Cryptography.ProtectedData
```

**核心程式碼範例：**
```csharp
using System.Security.Cryptography;
using System.Text;

namespace Utils.DpapiProvider
{
    public static class DpapiProvider
    {
        // 加密字串
        public static string Encrypt(string plainText)
        {
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            byte[] encryptedBytes = ProtectedData.Protect(
                plainBytes,
                null,
                DataProtectionScope.LocalMachine
            );
            return Convert.ToBase64String(encryptedBytes);
        }

        // 解密字串
        public static string Decrypt(string encryptedText)
        {
            byte[] encryptedBytes = Convert.FromBase64String(encryptedText);
            byte[] plainBytes = ProtectedData.Unprotect(
                encryptedBytes,
                null,
                DataProtectionScope.LocalMachine
            );
            return Encoding.UTF8.GetString(plainBytes);
        }
    }
}
```

#### 2. 設定檔加密工具

**用途：** 將明碼的 JSON 設定檔加密成 `secrets.prod.enc`

**建立方式：**
```bash
# 建立主控台應用程式
dotnet new console -n Utils.EncryptTool -f net8.0-windows
cd Utils.EncryptTool

# 參考 Utils.DpapiProvider 專案
dotnet add reference ../Utils.DpapiProvider/Utils.DpapiProvider.csproj
```

**核心程式碼範例：**
```csharp
using Utils.DpapiProvider;

string inputFile = "secrets.dev.json";
string outputFile = "secrets.prod.enc";

if (!File.Exists(inputFile))
{
    Console.WriteLine($"找不到輸入檔案: {inputFile}");
    return;
}

string plainJson = File.ReadAllText(inputFile);
string encrypted = DpapiProvider.Encrypt(plainJson);
File.WriteAllText(outputFile, encrypted);

Console.WriteLine($"加密完成！已產生: {outputFile}");
```

#### 替代方案

如果您不想使用 DPAPI，可以考慮：
1. **Azure Key Vault** - 雲端金鑰管理服務
2. **User Secrets** - .NET 內建的開發環境 secrets 管理（僅適用於開發環境）
3. **環境變數** - 直接從系統環境變數讀取敏感設定
4. **Docker Secrets** - 容器化環境的 secrets 管理

若使用替代方案，您需要修改 `Program.cs` 中的設定載入邏輯。

## 核心功能

### Active Directory (LDAP) 服務 (`ADController`)

* **使用者驗證：** 使用者帳號/密碼驗證 (`AuthenticateLdapUserAsync`)
* **密碼到期通知：** 自動檢查 AD 帳號密碼到期日並透過 Email 發送通知 (`CheckPasswordStatusAndNotifyAsync`)
* **使用者同步：** 將 AD 使用者帳號（包含群組隸屬）同步至 `LdapUserHistory` 資料庫 (`SyncUsersToDatabaseAsync`)
* **稽核報表：** 產生 AD 帳號清查稽核報表 (Excel) (`GenerateAuditReportDataAsync`)
* **使用者管理：** 建立 (`CreateLdapUserAsync`) 和更新 (`UpdateLdapUserAsync`) AD 使用者（未正式啟用）
* **FEB CMS 整合：** 自動同步 AD 使用者狀態（離職/職稱）至 `FEB_CMS` 資料庫 (`SyncUsersFromAdAsync`)

### VMware 服務 (`VMwareController`)

* **虛擬機查詢：** 透過 vCenter API 獲取指定環境（正式/測試）的虛擬機 (VM) 列表及其電源狀態 (`GetVmsAsync`)

### Veeam 服務 (`VeeamController`)

* **備份日誌查詢：** 從資料庫讀取 Veeam 備份工作階段的日誌 (`GetBackupSessionsAsync`)

### 通用服務 (`Classes/`)

* **郵件服務 (`MailSend`)：** 透過 SMTP (Mail Relay) 發送郵件
* **Excel 服務 (`ExcelService`)：** 使用 EPPlus.Free 產生 Excel 報表

## 技術棧

* **框架：** ASP.NET Core 8.0 (Windows)
* **語言：** C# (.NET 8.0)
* **資料庫：** 
  - Microsoft SQL Server (Entity Framework Core 9.0)
  - SQLite (開發環境測試用)
* **目錄服務：** Novell LDAP (Active Directory 整合)
* **日誌記錄：** NLog 6.0
* **文件產生：** EPPlus.Free (Excel)
* **郵件服務：** NETCore.MailKit、MimeKit
* **安全性：** Windows DPAPI (Data Protection API)
* **API 文件：** Swagger/OpenAPI (Swashbuckle.AspNetCore)
* **虛擬化管理：** VMware vCenter REST API
* **其他套件：** 
  - Microsoft.Build
  - System.Drawing.Common

## 安裝與設定

### 1. 複製專案

```bash
git clone <repository-url>
cd API
```

### 2. 還原 NuGet 套件

```bash
dotnet restore
```

### 3. 設定開發環境組態

1. 複製範本檔案：
   ```bash
   copy appsettings.Development_參考.json secrets.dev.json
   ```

2. 編輯 `secrets.dev.json`，填入您的開發環境設定：
   - 資料庫連線字串
   - LDAP/AD 設定和憑證
   - VMware vCenter 設定和憑證
   - SMTP 郵件伺服器設定

> ⚠️ **重要：** `secrets.dev.json` 以及 `appsettings.Development_參考.json`，請改成**正確**的名稱。

### 4. 資料庫遷移（如需要）

```bash
dotnet ef database update --context MISContext
dotnet ef database update --context FEB_CMSContext
```

### 5. 執行應用程式

```bash
dotnet run
```

應用程式將在 `https://localhost:5001` 或 `http://localhost:5000` 啟動。

### 6. 存取 Swagger UI

開啟瀏覽器並導航至：
```
https://localhost:5001/swagger
```

## 安全組態設定 (DPAPI 方案)

本專案**不使用**標準的 `appsettings.json` 來儲存 secrets。所有組態（包含連線字串、LDAP 密碼、VMware 密碼等）都透過環境判斷來載入：

### 1. 開發環境 (Development)

* **觸發方式：** `ASPNETCORE_ENVIRONMENT` 環境變數**未設定**或設定為 `Development`
* **載入檔案：** `secrets.dev.json` (明碼)
* **安全性：** 此檔案被 `.gitignore` 忽略，**永遠不會**被 Commit
* **用途：** 僅供本機開發使用

### 2. 正式環境 (Production)

* **觸發方式：** `ASPNETCORE_ENVIRONMENT` 環境變數設定為 `Production`
* **載入檔案：** `secrets.prod.enc` (使用 DPAPI `LocalMachine` 加密)
* **載入邏輯：** 
  1. `Program.cs` 在啟動時讀取 `secrets.prod.enc` 檔案
  2. 呼叫 `Utils.DpapiProvider.Decrypt()` 在記憶體中解密
  3. 將解密後的 JSON 載入為應用程式組態
* **安全性：** 
  - 加密檔案使用 Windows DPAPI LocalMachine 範圍加密
  - 僅能在加密的同一台機器上解密
  - Secrets 永不以明碼存在於磁碟

### 3. 產生正式環境加密檔

如果您已建立 `Utils.EncryptTool` 工具（請參考[專案架構](#專案架構)章節），可以使用它來產生加密檔：

**步驟：**

1. 準備明碼設定檔 `secrets.dev.json`（根據 `appsettings.json` 填入實際設定）

2. 在正式環境的伺服器上執行加密工具：
   ```bash
   cd Utils.EncryptTool
   dotnet run
   ```

3. 工具會讀取 `secrets.dev.json` 並產生 `secrets.prod.enc`

4. 將 `secrets.prod.enc` 複製到 API 專案的根目錄

**重要提醒：**
- ⚠️ 加密檔必須在**正式環境的伺服器上**產生（因為 DPAPI LocalMachine 綁定特定機器）
- ⚠️ 如果更換伺服器，需要重新產生加密檔

**手動替代方案：**

如果不想建立加密工具，您也可以：
1. 使用環境變數來設定所有敏感資訊
2. 修改 `Program.cs`，移除 DPAPI 相關程式碼，改用其他設定來源
3. 在正式環境直接使用明碼的 `appsettings.Production.json`（**不建議**，安全性較低）

## API 端點

### Active Directory (AD) 端點

| 方法 | 端點 | 說明 |
|------|------|------|
| POST | `/api/AD/authenticate` | 驗證 LDAP 使用者帳號密碼 |
| POST | `/api/AD/check-password-expiry` | 檢查密碼到期狀態並發送通知 |
| POST | `/api/AD/sync-users` | 同步 AD 使用者至資料庫 |
| GET  | `/api/AD/audit-report` | 產生 AD 稽核報表 (Excel) |
| POST | `/api/AD/sync-to-febcms` | 同步使用者狀態至 FEB CMS |

### VMware 端點

| 方法 | 端點 | 說明 |
|------|------|------|
| GET  | `/api/VMware/vms` | 取得虛擬機列表及狀態 |

### Veeam 端點

| 方法 | 端點 | 說明 |
|------|------|------|
| GET  | `/api/Veeam/backup-sessions` | 取得備份工作階段日誌 |

> 📖 **完整的 API 文件請參考 Swagger UI：** `https://localhost:5001/swagger`

## 開發指南

### 專案結構

```
API/
├── Classes/              # 共用類別（郵件、Excel、LDAP 等）
├── Controllers/          # API 控制器
│   ├── ADController.cs
│   ├── VMwareController.cs
│   ├── VeeamController.cs
│   └── WMIController.cs
├── DataModels/           # 資料庫 Entity Framework 模型
├── Models/               # 設定模型和 DTO
├── Services/             # 業務邏輯服務層
│   ├── FEB_CMS/
│   ├── LDAP/
│   ├── Veeam/
│   └── VMware/
├── Logs/                 # NLog 日誌輸出目錄
├── Program.cs            # 應用程式進入點
├── nlog.config           # NLog 設定檔
├── appsettings.json      # 基本設定（不含 secrets）
└── secrets.dev.json      # 開發環境 secrets
```

### 依賴注入 (DI)

本專案使用 ASP.NET Core 內建的 DI 容器，所有服務都在 `Program.cs` 中註冊：

```csharp
// 服務註冊範例
builder.Services.AddScoped<ILdapService, LdapService>();
builder.Services.AddScoped<IVMwareService, VMwareService>();
builder.Services.AddScoped<IMailSend, MailSend>();
```

### 日誌記錄

使用 NLog 進行日誌記錄，設定檔位於 `nlog.config`：

### 新增 API 端點

1. 在 `Controllers/` 建立新的控制器
2. 在 `Services/` 建立對應的服務介面和實作
3. 在 `Program.cs` 註冊服務到 DI 容器
4. 使用 XML 註解來產生 Swagger 文件

## 部署

### Windows Server / IIS 部署

1. **發佈專案：**
   ```bash
   dotnet publish -c Release -o ./publish
   ```

2. **設定環境變數：**
   - 在 IIS 應用程式集區或系統環境變數中設定：
     ```
     ASPNETCORE_ENVIRONMENT=Production
     ```

3. **準備加密設定檔：**
   - 在正式伺服器上執行 `Utils.EncryptTool` 產生 `secrets.prod.enc`
   - 將 `secrets.prod.enc` 放置於發佈目錄

4. **設定 IIS：**
   - 安裝 .NET 8.0 Hosting Bundle
   - 建立應用程式集區（無受控碼）
   - 建立網站並指向發佈目錄

### Windows 服務部署

可以將 API 註冊為 Windows 服務，實現開機自動啟動。相關批次檔位於 `批次檔/` 目錄。

## 故障排除

### 常見問題

#### 1. 無法載入 secrets.prod.enc

**錯誤訊息：** `找不到正式環境的加密設定檔`

**解決方案：**
- 確認 `secrets.prod.enc` 檔案存在於執行目錄
- 確認 `ASPNETCORE_ENVIRONMENT` 環境變數設定正確

#### 2. 解密失敗

**錯誤訊息：** `解密 secrets.prod.enc 失敗！`

**解決方案：**
- 確認加密檔案是在同一台機器上產生的（DPAPI LocalMachine 限制）
- 如果更換伺服器，需要重新產生加密檔案

#### 3. LDAP 連線失敗

**錯誤訊息：** `LDAP 伺服器連線逾時`

**解決方案：**
- 檢查網路連線和防火牆設定
- 確認 LDAP 伺服器位址和連接埠正確
- 驗證服務帳號憑證是否有效

#### 4. 資料庫連線失敗

**錯誤訊息：** `無法連線至 SQL Server`

**解決方案：**
- 檢查連線字串格式
- 確認 SQL Server 服務正在執行
- 驗證資料庫使用者權限

#### 5. NLog 日誌未產生

**解決方案：**
- 確認 `Logs/` 目錄存在且有寫入權限
- 檢查 `nlog.config` 設定是否正確
- 確認應用程式有檔案系統寫入權限

### 日誌位置

* **應用程式日誌：** `Logs/` 目錄
* **IIS 日誌：** `C:\inetpub\logs\LogFiles\`
* **Windows 事件檢視器：** 應用程式日誌

## 附錄：DPAPI 加密方案說明

### 為什麼使用 DPAPI？

**Windows Data Protection API (DPAPI)** 是 Windows 內建的加密 API，具有以下優點：

* ✅ **無需管理金鑰：** 由作業系統自動管理加密金鑰
* ✅ **機器綁定：** 使用 `LocalMachine` 範圍時，只能在加密的同一台機器上解密
* ✅ **免費且內建：** 不需額外安裝或訂閱服務
* ✅ **簡單易用：** 程式碼實作簡單

### 限制與注意事項

* ⚠️ **僅限 Windows：** 無法在 Linux 或 macOS 上使用
* ⚠️ **機器綁定：** 更換伺服器時必須重新加密設定檔
* ⚠️ **無法版本控制：** 加密檔無法提交到 Git（不同機器無法解密）

### 相關資源

* [Microsoft Docs: Data Protection API](https://docs.microsoft.com/zh-tw/dotnet/standard/security/how-to-use-data-protection)
* [System.Security.Cryptography.ProtectedData](https://docs.microsoft.com/zh-tw/dotnet/api/system.security.cryptography.protecteddata)

## 授權

內部專案，不提供使用。

---