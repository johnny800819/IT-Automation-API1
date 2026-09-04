using API.Models.MIS;
using System.Linq;
using API.DataModels.LDAP;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Drawing;

namespace API.Classes.Reporting
{
    public class ExcelService : IExcelService
    {
        private readonly ILogger<ExcelService> _logger;

        public ExcelService(ILogger<ExcelService> logger)
        {
            _logger = logger;
        }

        /// <inheritdoc/>
        public byte[] CreateAdAuditReport(IEnumerable<AdAuditReportItem> data)
        {
            _logger.LogInformation("開始使用 EPPlus.Free 生成 AD 稽核報表...");

            using (var package = new ExcelPackage())
            {
                _logger.LogInformation("正在對 {DataCount} 筆報表資料進行 C# LINQ 穩定排序...", data.Count());
                // 在程式中實現這種「穩定排序」的技巧，是從最次要的鍵開始，反向操作到最主要的鍵。所以，程式碼的排序順序必須是 D->B->A。
                var sortedData = data
                    // 步驟一 (最次要): 先按「目前狀態」(D欄) 降序排列 (啟用在前)
                    .OrderByDescending(u => u.IsEnabled)
                    // 步驟二: 再按「特權帳號」(B欄) 降序排列 (特權在前)
                    .ThenByDescending(u => u.IsPrivileged)
                    // 步驟三 (最主要): 最後按「帳號」(A欄) 升序排列 (A-Z)
                    .ThenBy(u => u.SamAccountName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // 1. 將資料分割為 "有名稱" 和 "無名稱" 兩個列表
                var usersWithDisplayName = sortedData.Where(u => !string.IsNullOrWhiteSpace(u.DisplayName)).ToList();
                var usersWithBlankDisplayName = sortedData.Where(u => string.IsNullOrWhiteSpace(u.DisplayName)).ToList();

                // 2. 建立主要的工作表
                var mainWorksheet = package.Workbook.Worksheets.Add("AD帳號清查");
                PopulateAdWorksheet(mainWorksheet, usersWithDisplayName);

                // 3. 如果有 "無名稱" 的使用者，才建立第二個工作表
                if (usersWithBlankDisplayName.Any())
                {
                    var blankNameWorksheet = package.Workbook.Worksheets.Add("名稱空白帳號");
                    PopulateAdWorksheet(blankNameWorksheet, usersWithBlankDisplayName);
                }

                _logger.LogInformation("Excel 報表內容生成完畢。");
                return package.GetAsByteArray();
            }
        }

        /// <summary>
        /// 私有輔助方法，用於填充 AD 帳號工作表內容並套用所有格式
        /// </summary>
        private void PopulateAdWorksheet(ExcelWorksheet worksheet, List<AdAuditReportItem> data)
        {
            // --- 1. 設定大標題 (第一列) ---
            worksheet.Cells[1, 1].Value = "Active Directory 帳號清查作業";
            worksheet.Cells[1, 1, 1, 5].Merge = true; // 合併 A1 到 E1
            worksheet.Cells[1, 1].Style.Font.Size = 20;
            worksheet.Cells[1, 1].Style.Font.Bold = true;
            worksheet.Cells[1, 1].Style.Font.Name = "標楷體";
            worksheet.Cells[1, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            worksheet.Cells[1, 1].Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            worksheet.Row(1).Height = 35;

            // --- 2. 設定欄位標頭 (第二列) ---
            worksheet.Cells[2, 1].Value = "帳號";
            worksheet.Cells[2, 2].Value = "特權帳號";
            worksheet.Cells[2, 3].Value = "名稱";
            worksheet.Cells[2, 4].Value = "目前狀態";
            worksheet.Cells[2, 5].Value = "本次建議處置";

            // --- 3. 填入資料內容 (從第三列開始) ---
            int row = 3;
            foreach (var item in data)
            {
                worksheet.Cells[row, 1].Value = item.SamAccountName;
                worksheet.Cells[row, 2].Value = item.IsPrivileged ? "■" : "□";
                worksheet.Cells[row, 3].Value = item.DisplayName;
                worksheet.Cells[row, 4].Value = item.IsEnabled ? "[啟用]" : "[停用]";
                worksheet.Cells[row, 5].Value = "■保留 □刪除";
                row++;
            }

            if (row <= 3) return;

            // --- 4. 套用所有格式 ---
            var dataRange = worksheet.Cells[2, 1, row - 1, 5];

            // 字體設定: 標楷體, 12大小
            dataRange.Style.Font.Name = "標楷體";
            dataRange.Style.Font.Size = 12;

            // 格線設定
            dataRange.Style.Border.Top.Style = ExcelBorderStyle.Thin;
            dataRange.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
            dataRange.Style.Border.Left.Style = ExcelBorderStyle.Thin;
            dataRange.Style.Border.Right.Style = ExcelBorderStyle.Thin;

            // 欄頭粗體底色 (第二列)
            worksheet.Cells[2, 1, 2, 5].Style.Font.Bold = true;
            worksheet.Cells[2, 1, 2, 5].Style.Fill.PatternType = ExcelFillStyle.Solid;
            worksheet.Cells[2, 1, 2, 5].Style.Fill.BackgroundColor.SetColor(Color.LightGray);
            worksheet.Cells[2, 1, 2, 5].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            // 置中設定: 特權帳號 (B)、目前狀態 (D)、本次建議處置 (E)
            worksheet.Cells[2, 2, row - 1, 2].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            worksheet.Cells[2, 4, row - 1, 5].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            // 狀態顏色: [啟用]=深綠, [停用]=深紅
            for (int i = 3; i < row; i++)
            {
                var statusCell = worksheet.Cells[i, 4];
                if (statusCell.Text.Contains("啟用"))
                {
                    statusCell.Style.Font.Color.SetColor(Color.DarkGreen);
                    statusCell.Style.Font.Bold = true;
                }
                else if (statusCell.Text.Contains("停用"))
                {
                    statusCell.Style.Font.Color.SetColor(Color.DarkRed);
                    statusCell.Style.Font.Bold = true;
                }
            }

            // 自動調整欄寬並稍微加寬
            worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();
            for (int i = 1; i <= 5; i++)
            {
                worksheet.Column(i).Width += 3;
            }
        }

        /// <inheritdoc/>
        public byte[] CreateDbAuditReport(IDictionary<string, IEnumerable<AuditDbAccountHistory>> dataMap)
        {
            _logger.LogInformation("開始產生資料庫 (DB) 多分頁稽核報表...");

            using (var package = new ExcelPackage())
            {
                foreach (var entry in dataMap)
                {
                    string sheetName = entry.Key;
                    
                    // 直接使用 Controller 傳入的已排序資料
                    var sortedData = entry.Value.ToList();

                    // 建立工作表
                    var worksheet = package.Workbook.Worksheets.Add(sheetName);
                    PopulateDbWorksheet(worksheet, sortedData);
                }

                _logger.LogInformation("Excel 報表內容生成完畢。");
                return package.GetAsByteArray();
            }
        }

        /// <summary>
        /// 私有輔助方法，用於填充資料庫帳號工作表內容並套用所有格式
        /// </summary>
        private void PopulateDbWorksheet(ExcelWorksheet worksheet, List<AuditDbAccountHistory> data)
        {
            // --- 1. 設定大標題 (第一列) ---
            worksheet.Cells[1, 1].Value = $"資料庫帳號清查作業\r\n{worksheet.Name}";
            worksheet.Cells[1, 1, 1, 6].Merge = true; // 合併 A1 到 F1
            worksheet.Cells[1, 1].Style.Font.Size = 20;
            worksheet.Cells[1, 1].Style.Font.Bold = true;
            worksheet.Cells[1, 1].Style.Font.Name = "標楷體";
            worksheet.Cells[1, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            worksheet.Cells[1, 1].Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            worksheet.Cells[1, 1].Style.WrapText = true; // 啟用自動換行以支援 \r\n
            worksheet.Row(1).Height = 60; // 增加標題列高度以容納兩行文字

            // --- 2. 設定欄位標頭 (第二列) ---
            worksheet.Cells[2, 1].Value = "帳號名稱";
            worksheet.Cells[2, 2].Value = "帳號狀態";
            worksheet.Cells[2, 3].Value = "伺服器角色";
            worksheet.Cells[2, 4].Value = "帳號使用說明";
            worksheet.Cells[2, 5].Value = "使用/承辦人";
            worksheet.Cells[2, 6].Value = "清查後保留/刪除/停用";

            // --- 3. 填入資料內容 (從第三列開始) ---
            int row = 3;
            foreach (var item in data)
            {
                worksheet.Cells[row, 1].Value = item.AccountName;
                worksheet.Cells[row, 2].Value = item.Status;
                worksheet.Cells[row, 3].Value = item.ServerRole;
                worksheet.Cells[row, 4].Value = item.AccountDescription;
                worksheet.Cells[row, 5].Value = item.Assignee;
                // 優先輸出資料庫中人工填寫的清查決策，若無則顯示預設 checkbox 文字供稽核人員手動勾選
                worksheet.Cells[row, 6].Value = !string.IsNullOrWhiteSpace(item.ActionDecision)
                    ? item.ActionDecision
                    : "■保留帳號 □刪除帳號 □停用帳號";
                row++;
            }

            // --- 4. 套用所有格式 ---
            // 資料範圍 (從第二列標頭到資料結束)
            var dataRange = worksheet.Cells[2, 1, row - 1, 6];

            // 字體設定: 標楷體, 12大小
            dataRange.Style.Font.Name = "標楷體";
            dataRange.Style.Font.Size = 12;

            // 格線設定: 為所有儲存格加上細格線
            dataRange.Style.Border.Top.Style = ExcelBorderStyle.Thin;
            dataRange.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
            dataRange.Style.Border.Left.Style = ExcelBorderStyle.Thin;
            dataRange.Style.Border.Right.Style = ExcelBorderStyle.Thin;

            // 置中設定: 帳號狀態 (B欄)、清查後保留/刪除 (F欄)
            worksheet.Cells[2, 2, row - 1, 2].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            worksheet.Cells[2, 6, row - 1, 6].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            // 欄頭粗體底色 (第二列)
            worksheet.Cells[2, 1, 2, 6].Style.Font.Bold = true;
            worksheet.Cells[2, 1, 2, 6].Style.Fill.PatternType = ExcelFillStyle.Solid;
            worksheet.Cells[2, 1, 2, 6].Style.Fill.BackgroundColor.SetColor(Color.LightGray);

            // 狀態欄位顏色: 啟用=深綠, 停用=深紅
            for (int i = 3; i < row; i++)
            {
                var statusCell = worksheet.Cells[i, 2];
                if (statusCell.Text.Contains("啟用"))
                {
                    statusCell.Style.Font.Color.SetColor(Color.DarkGreen);
                    statusCell.Style.Font.Bold = true;
                }
                else if (statusCell.Text.Contains("停用"))
                {
                    statusCell.Style.Font.Color.SetColor(Color.DarkRed);
                    statusCell.Style.Font.Bold = true;
                }
            }

            // 自動調整欄寬
            worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();
        }

        /// <inheritdoc/>
        public byte[] CreateVmAuditReport(IEnumerable<AuditVmAccountHistory> data)
        {
            _logger.LogInformation("開始產生虛擬機 (VM) 本機帳號稽核報表...");

            using (var package = new ExcelPackage())
            {
                // 排序邏輯：按 1. 狀態(倒序讓啟用在前) 2. IP 升序 3. 名稱 升序
                var sortedData = data.OrderByDescending(x => x.Status ?? "")
                                     .ThenBy(x => x.VirtualServerIP ?? "")
                                     .ThenBy(x => x.LocalAccountName ?? "")
                                     .ToList();

                // 建立工作表
                var worksheet = package.Workbook.Worksheets.Add("本機帳戶清單");
                PopulateVmWorksheet(worksheet, sortedData);

                _logger.LogInformation("Excel 報表內容生成完畢。");
                return package.GetAsByteArray();
            }
        }

        /// <summary>
        /// 私有輔助方法，用於填充虛擬機本機帳號工作表內容並套用所有格式
        /// </summary>
        private void PopulateVmWorksheet(ExcelWorksheet worksheet, List<AuditVmAccountHistory> data)
        {
            // --- 1. 設定大標題 (第一列) ---
            worksheet.Cells[1, 1].Value = "機房維運伺服器本機帳戶盤點清冊";
            worksheet.Cells[1, 1, 1, 4].Merge = true; // 合併 A1 到 D1
            worksheet.Cells[1, 1].Style.Font.Size = 20;
            worksheet.Cells[1, 1].Style.Font.Bold = true;
            worksheet.Cells[1, 1].Style.Font.Name = "標楷體";
            worksheet.Cells[1, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            worksheet.Cells[1, 1].Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            worksheet.Row(1).Height = 35;

            // --- 2. 設定欄位標頭 (第二列) ---
            worksheet.Cells[2, 1].Value = "虛擬伺服器 IP";
            worksheet.Cells[2, 2].Value = "本機使用者帳戶";
            worksheet.Cells[2, 3].Value = "狀態";
            worksheet.Cells[2, 4].Value = "設定";

            // --- 3. 填入資料內容 (從第三列開始) ---
            int row = 3;
            foreach (var item in data)
            {
                worksheet.Cells[row, 1].Value = item.VirtualServerIP;
                worksheet.Cells[row, 2].Value = item.LocalAccountName;
                worksheet.Cells[row, 3].Value = item.Status;
                worksheet.Cells[row, 4].Value = item.Setting;
                row++;
            }

            if (row <= 3) return; 

            // --- 4. 套用所有格式 ---
            var dataRange = worksheet.Cells[2, 1, row - 1, 4];

            // 字體設定: 標楷體, 12大小
            dataRange.Style.Font.Name = "標楷體";
            dataRange.Style.Font.Size = 12;

            // 格線設定
            dataRange.Style.Border.Top.Style = ExcelBorderStyle.Thin;
            dataRange.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
            dataRange.Style.Border.Left.Style = ExcelBorderStyle.Thin;
            dataRange.Style.Border.Right.Style = ExcelBorderStyle.Thin;

            // 欄頭粗體底色與置中 (第二列)
            worksheet.Cells[2, 1, 2, 4].Style.Fill.PatternType = ExcelFillStyle.Solid;
            worksheet.Cells[2, 1, 2, 4].Style.Fill.BackgroundColor.SetColor(Color.LightGray);
            worksheet.Cells[2, 1, 2, 4].Style.Font.Bold = true;
            worksheet.Cells[2, 1, 2, 4].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            // IP (A欄) 置左、狀態 (C欄) 與 設定 (D欄) 置中
            worksheet.Cells[2, 1, row - 1, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
            worksheet.Cells[2, 3, row - 1, 4].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            // 狀態顏色：[啟用]=深綠, [停用]=深紅
            for (int i = 3; i < row; i++)
            {
                var statusCell = worksheet.Cells[i, 3];
                if (statusCell.Text.Contains("啟用"))
                {
                    statusCell.Style.Font.Color.SetColor(Color.DarkGreen);
                }
                else if (statusCell.Text.Contains("停用"))
                {
                    statusCell.Style.Font.Color.SetColor(Color.DarkRed);
                }

                // 設定欄 (D) 內容靠左
                worksheet.Cells[i, 4].Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
            }

            // 自動調整欄寬並稍微加寬一點點
            worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();
            for (int i = 1; i <= 4; i++)
            {
                worksheet.Column(i).Width += 3;
            }
        }
        /// <inheritdoc/>
        public byte[] CreateAppAuditReport(string systemTitle, IDictionary<string, IEnumerable<AuditAppAccountHistory>> dataMap)
        {
            _logger.LogInformation("開始產生應用系統 ({SystemTitle}) 帳號稽核報表...", systemTitle);

            using (var package = new ExcelPackage())
            {
                foreach (var entry in dataMap)
                {
                    string sheetName = entry.Key;
                    // Controller 層已經完成排序與過濾，此處直接使用
                    var sortedData = entry.Value.ToList();

                    var worksheet = package.Workbook.Worksheets.Add(sheetName);
                    PopulateAppWorksheet(worksheet, systemTitle, sortedData);
                }

                _logger.LogInformation("Excel 報表內容生成完畢。");
                return package.GetAsByteArray();
            }
        }

        /// <summary>
        /// 私有辔助方法，用於填充應用系統帳號工作表內容並套用的格式
        /// 報表格式：組室(A) / 帳號(B) / 姓名(C) / 清查結果(D)
        /// </summary>
        private void PopulateAppWorksheet(ExcelWorksheet worksheet, string systemTitle, List<AuditAppAccountHistory> data)
        {
            // --- 1. 設定大標題 (第一列) ---
            worksheet.Cells[1, 1].Value = $"{systemTitle}帳號清查作業\n({worksheet.Name})";
            worksheet.Cells[1, 1, 1, 4].Merge = true; // 合併 A1 到 D1
            worksheet.Cells[1, 1].Style.Font.Size = 16; // 稍微調小字體以適應換行
            worksheet.Cells[1, 1].Style.Font.Bold = true;
            worksheet.Cells[1, 1].Style.Font.Name = "標楷體";
            worksheet.Cells[1, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            worksheet.Cells[1, 1].Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            worksheet.Cells[1, 1].Style.WrapText = true; // 啟動自動換行
            worksheet.Row(1).Height = 60; // 增加高度以容納兩行文字

            // --- 2. 設定欄位標頭 (第二列) ---
            worksheet.Cells[2, 1].Value = "組室";
            worksheet.Cells[2, 2].Value = "帳號";
            worksheet.Cells[2, 3].Value = "姓名";
            worksheet.Cells[2, 4].Value = "清查結果";

            // --- 3. 填入資料內容 (從第三列開始) ---
            int row = 3;
            foreach (var item in data)
            {
                worksheet.Cells[row, 1].Value = item.Department;
                worksheet.Cells[row, 2].Value = item.AccountName;
                worksheet.Cells[row, 3].Value = item.RealName;
                // 優先顯示人工填寫的決策，若為空則顯示預設勾選方塊文字
                worksheet.Cells[row, 4].Value = !string.IsNullOrWhiteSpace(item.ActionDecision)
                    ? item.ActionDecision
                    : "■續用 □刪除";
                row++;
            }

            if (row <= 3) return;

            // --- 4. 套用所有格式 ---
            var dataRange = worksheet.Cells[2, 1, row - 1, 4];

            // 字體設定: 標楷體, 12大小
            dataRange.Style.Font.Name = "標楷體";
            dataRange.Style.Font.Size = 12;

            // 格線設定
            dataRange.Style.Border.Top.Style    = ExcelBorderStyle.Thin;
            dataRange.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
            dataRange.Style.Border.Left.Style   = ExcelBorderStyle.Thin;
            dataRange.Style.Border.Right.Style  = ExcelBorderStyle.Thin;

            // 欄頭粗體底色與置中 (第二列)
            worksheet.Cells[2, 1, 2, 4].Style.Font.Bold = true;
            worksheet.Cells[2, 1, 2, 4].Style.Fill.PatternType = ExcelFillStyle.Solid;
            worksheet.Cells[2, 1, 2, 4].Style.Fill.BackgroundColor.SetColor(Color.LightGray);
            worksheet.Cells[2, 1, 2, 4].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            // 內容對齊設定: 組室(A)、帳號(B)、姓名(C) 靠左; 清查結果(D) 置中
            worksheet.Cells[3, 1, row - 1, 3].Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
            worksheet.Cells[3, 4, row - 1, 4].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            // 自動調整欄寬並稍微加寬
            worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();
            for (int i = 1; i <= 4; i++)
            {
                worksheet.Column(i).Width += 3;
            }
        }
    }
}