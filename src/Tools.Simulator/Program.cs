using System.Text;
using KsitalTelemetryHub.Core;
using KsitalTelemetryHub.Parser.Ksital;
using KsitalTelemetryHub.Storage.Sqlite;

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine("=== Симулятор контроллеров КСИТАЛ запущен ===");
Console.WriteLine("Генерация телеметрии и аварийных событий в telemetry.db. Нажмите Ctrl+C для выхода.\n");

string dbPath = "telemetry.db";

using (var initDb = new AppDbContext(dbPath))
{
    await initDb.Database.EnsureCreatedAsync();
}

var random = new Random();

var objects = new[]
{
    new { Name = "Котельная №1 (Северная)", Phone = "+79161112233", BaseT1 = 70.0, BaseT2 = 50.0 },
    new { Name = "Котельная №2 (Южная)",    Phone = "+79162223344", BaseT1 = 65.0, BaseT2 = 45.0 },
    new { Name = "ИТП Школа №5",            Phone = "+79163334455", BaseT1 = 60.0, BaseT2 = 42.0 }
};

int iteration = 0;

while (true)
{
    iteration++;
    using var db = new AppDbContext(dbPath);

    foreach (var obj in objects)
    {
        // Небольшие реалистичные колебания температуры (±1.5 градуса)
        double t1 = Math.Round(obj.BaseT1 + (random.NextDouble() * 3.0 - 1.5), 1);
        double t2 = Math.Round(obj.BaseT2 + (random.NextDouble() * 2.0 - 1.0), 1);
        double tAir = Math.Round(20.0 + (random.NextDouble() * 2.0 - 1.0), 1);
        double acc = Math.Round(12.2 + random.NextDouble() * 0.6, 1);

        // Раз в 8 циклов моделируем сработку тревоги на одном из объектов
        bool generateAlarm = (iteration % 8 == 0) && (obj.Phone == "+79161112233");
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
                      $"Баланс: 180.50р";
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
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [ТРЕВОГА] {obj.Name}: {report.AlarmDescription}");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [ОТЧЕТ]   {obj.Name}: T1={t1}°C, T2={t2}°C, T3={tAir}°C, 220V=Есть");
        }
        Console.ResetColor();
    }

    Console.WriteLine(new string('-', 60));
    await Task.Delay(4000); // пауза 4 секунды между циклами опроса
}