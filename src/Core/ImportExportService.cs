using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;

namespace KsitalTelemetryHub.Core;

public class ImportExportItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string DeviceType { get; set; } = string.Empty;
}

public static class ImportExportService
{
    public static void ExportToExcel(string filePath, IEnumerable<ImportExportItem> targets)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Объекты");

        worksheet.Cell(1, 1).Value = "Название";
        worksheet.Cell(1, 2).Value = "Телефон";
        worksheet.Cell(1, 3).Value = "Тип оборудования";

        int row = 2;
        foreach (var target in targets)
        {
            worksheet.Cell(row, 1).Value = target.Name;
            worksheet.Cell(row, 2).Value = target.PhoneNumber;
            worksheet.Cell(row, 3).Value = target.DeviceType;
            row++;
        }

        worksheet.Columns().AdjustToContents();
        workbook.SaveAs(filePath);
    }

    public static List<ImportExportItem> ImportFromExcel(string filePath)
    {
        var targets = new List<ImportExportItem>();
        
        if (filePath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            var lines = File.ReadAllLines(filePath);
            foreach (var line in lines.Skip(1))
            {
                var parts = line.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3)
                {
                    targets.Add(new ImportExportItem
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = parts[0].Trim(),
                        PhoneNumber = parts[1].Trim(),
                        DeviceType = NormalizeDeviceType(parts[2].Trim())
                    });
                }
            }
            return targets;
        }

        using var workbook = new XLWorkbook(filePath);
        var worksheet = workbook.Worksheet(1);
        var range = worksheet.RangeUsed();
        if (range == null)
        {
            return targets;
        }

        var rows = range.RowsUsed().Skip(1);

        foreach (var row in rows)
        {
            var name = row.Cell(1).GetString().Trim();
            var phone = row.Cell(2).GetString().Trim();
            var type = row.Cell(3).GetString().Trim();

            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(phone))
            {
                targets.Add(new ImportExportItem
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = name,
                    PhoneNumber = phone,
                    DeviceType = NormalizeDeviceType(type)
                });
            }
        }

        return targets;
    }

    private static string NormalizeDeviceType(string rawType)
    {
        if (string.IsNullOrWhiteSpace(rawType)) return "Ksital";

        var t = rawType.ToLowerInvariant();
        if (t.Contains("ksital") || t.Contains("кситал") || t.Contains("кс")) return "Ksital";
        if (t.Contains("ccu") || t.Contains("ццу") || t.Contains("rads")) return "RadsCCU";
        if (t.Contains("oven") || t.Contains("овен") || t.Contains("плк")) return "OvenPLC";

        return "Ksital";
    }
}