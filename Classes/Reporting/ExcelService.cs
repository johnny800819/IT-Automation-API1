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
                PopulateWorksheet(mainWorksheet, usersWithDisplayName);

                // 3. 如果有 "無名稱" 的使用者，才建立第二個工作表
                if (usersWithBlankDisplayName.Any())
                {
                    var blankNameWorksheet = package.Workbook.Worksheets.Add("名稱空白帳號");
                    PopulateWorksheet(blankNameWorksheet, usersWithBlankDisplayName);
                }

                _logger.LogInformation("Excel 報表內容生成完畢。");
                return package.GetAsByteArray();
            }
        }

        /// <summary>
        /// 私有輔助方法，用於填充工作表內容並套用所有格式
        /// </summary>
        private void PopulateWorksheet(ExcelWorksheet worksheet, List<AdAuditReportItem> data)
        {
            // --- 1. 設定表頭 ---
            worksheet.Cells[1, 1].Value = "帳號";
            worksheet.Cells[1, 2].Value = "特權帳號";
            worksheet.Cells[1, 3].Value = "名稱";
            worksheet.Cells[1, 4].Value = "目前狀態";
            worksheet.Cells[1, 5].Value = "本次建議處置";

            // --- 2. 填入資料內容 ---
            int row = 2;
            foreach (var item in data)
            {
                worksheet.Cells[row, 1].Value = item.SamAccountName;
                worksheet.Cells[row, 2].Value = item.IsPrivileged ? "■" : "□"; // 讓非特權也顯示符號
                worksheet.Cells[row, 3].Value = item.DisplayName;
                worksheet.Cells[row, 4].Value = item.IsEnabled ? "■啟用 □停用" : "□啟用 ■停用"; // 簡化格式
                worksheet.Cells[row, 5].Value = "■保留 □刪除"; // 簡化格式
                row++;
            }

            // --- 3. 套用所有格式 ---
            // 取得目前有資料的範圍
            var dataRange = worksheet.Cells[1, 1, row - 1, 5];

            // 字體設定: 標楷體, 12大小
            dataRange.Style.Font.Name = "標楷體";
            dataRange.Style.Font.Size = 12;

            // 格線設定: 為所有儲存格加上細格線
            dataRange.Style.Border.Top.Style = ExcelBorderStyle.Thin;
            dataRange.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
            dataRange.Style.Border.Left.Style = ExcelBorderStyle.Thin;
            dataRange.Style.Border.Right.Style = ExcelBorderStyle.Thin;

            // 置中設定: "特權帳號", "目前狀態", "本次建議處置" 三個欄位置中
            worksheet.Cells[1, 2, row - 1, 2].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            worksheet.Cells[1, 4, row - 1, 5].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center; // D欄到E欄

            // 符號放大: 稍微增加特定欄位的字體大小，讓符號更突出
            worksheet.Cells[1, 2, row - 1, 2].Style.Font.Size = 14;
            worksheet.Cells[1, 4, row - 1, 5].Style.Font.Size = 14;

            // 自動調整欄寬 ---
            worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();
        }
    }
}