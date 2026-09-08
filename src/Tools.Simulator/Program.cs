using System.Text;
using Microsoft.EntityFrameworkCore;
using KsitalTelemetryHub.Core;
using KsitalTelemetryHub.Parser.Ksital;
using KsitalTelemetryHub.Storage.Sqlite;

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine("=== Симулятор контроллеров КСИТАЛ запущен ===");
Console.WriteLine("Инициализация участков и генерация телеметрии в telemetry.db. Выход: Ctrl+C\n");

string dbPath = "telemetry.db";

// 1. Предварительное создание БД и регистрация объектов с участками
using (var initDb = new AppDbContext(dbPath))
{
    await initDb.Database.EnsureCreatedAsync();

    // Список объектов с привязкой к районам/участкам
    var predefinedObjects = new[]
    {
        new { Name = "Котельная №1 (Северная)", Phone = "+79161112233", District = "Северный район теплосетей" },
        new { Name = "Котельная №4 (Промзона)",  Phone = "+79161115566", District = "Северный район теплосетей" },
        new { Name = "Котельная №2 (Южная)",    Phone = "+79162223344", District = "Южный эксплуатационный участок" },
        new { Name = "ИТП Школа №5",            Phone = "+79163334455", District = "Западный сектор ИТП" }
    };

    foreach (var pObj in predefinedObjects)
    {
        var existing = await initDb.Objects.FirstOrDefaultAsync(o => o.PhoneNumber == pObj.Phone);
        if (existing == null)
        {
            initDb.Objects.Add(new MonitoredObject
            {
                Name = pObj.Name,
                PhoneNumber = pObj.Phone,
                District = pObj.District,
                Description = "Тестовый объект симулятора"
            });
        }
        else
        {
            // Обновляем участок, если он был задан дефолтным
            existing.District = pObj.District;
        }
    }
    await initDb.SaveChangesAsync();
}

// 2. Параметры генерации для каждого объекта
var simTargets = new[]
{
    new { Name = "Котельная №1 (Северная)", Phone = "+79161112233", District = "Северный район", BaseT1 = 72.0, BaseT2 = 52.0 },
    new { Name = "Котельная №4 (Промзона)",  Phone = "+79161115566", District = "Северный район", BaseT1 = 80.0, BaseT2 = 58.0 },
    new { Name = "Котельная №2 (Южная)",    Phone = "+79162223344", District = "Южный участок",  BaseT1 = 66.0, BaseT2 = 46.0 },
    new { Name = "ИТП Школа №5",            Phone = "+79163334455", District = "Западный сектор",BaseT1 = 60.0, BaseT2 = 41.0 }
};

var random = new Random();
int iteration = 0;

while (true)
{
    iteration++;
    using var db = new AppDbContext(dbPath);

    foreach (var obj in simTargets)
    {
        // Небольшие колебания температуры вокруг базовой
        double t1 = Math.Round(obj.BaseT1 + (random.NextDouble() * 3.0 - 1.5), 1);
        double t2 = Math.Round(obj.BaseT2 + (random.NextDouble() * 2.0 - 1.0), 1);
        double tAir = Math.Round(21.0 + (random.NextDouble() * 2.0 - 1.0), 1);
        double acc = Math.Round(12.3 + random.NextDouble() * 0.5, 1);

        // Раз в 10 циклов моделируем тревогу на случайном объекте
        bool generateAlarm = (iteration % 10 == 0) && (obj.Phone == "+79161112233" || obj.Phone == "+79163334455");
        string smsText;

        if (generateAlarm)
        {
            smsText = random.Next(0, 2) == 0
                ? $"{obj.Name}\nТревога! Вскрытие двери З1"
                : $"{obj.Name}\nТревога! Загазованность котельной З2";
        }
        else
        {
            // Формируем стандартный отчет КСИТАЛ
            smsText = $"{obj.Name}\n" +
                      $"220V: Есть\n" +
                      $"Асс: {acc:F1}V\n" +
                      $"T1=+{t1:F1} T2=+{t2:F1} T3=+{tAir:F1}\n" +
                      $"З1:Норма З2:Норма З3:Норма\n" +
                      $"Баланс: 230.50р";
        }

        var incomingSms = new DecodedSms
        {
            SenderNumber = obj.Phone,
            Timestamp = DateTime.UtcNow,
            Text = smsText
        };

        var report = KsitalMessageParser.Parse(incomingSms);
        await db.SaveReportAsync(report);

        if (report.IsAlarm)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{obj.District}] [ТРЕВОГА] {obj.Name}: {report.AlarmDescription}");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{obj.District}] [ОТЧЕТ]   {obj.Name}: T1={t1:F1}°C, T2={t2:F1}°C, T3={tAir:F1}°C");
        }
        Console.ResetColor();
    }

    Console.WriteLine(new string('-', 70));
    await Task.Delay(3500); // пауза 3.5 секунды между циклом опроса
}