using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using KsitalTelemetryHub.Core;
using KsitalTelemetryHub.Storage.Sqlite;

Console.WriteLine("Запуск генерации тестовой тревоги...");

string candidatePath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\telemetry.db"));
string dbPath = File.Exists(candidatePath) ? candidatePath : "telemetry.db";

using var db = new AppDbContext(dbPath);
await db.Database.EnsureCreatedAsync();

var targetObj = await db.Objects.FirstOrDefaultAsync();
if (targetObj == null)
{
    targetObj = new MonitoredObject
    {
        Name = "Тестовая котельная №1",
        PhoneNumber = "+79991112233",
        District = "Северный участок",
        DeviceType = DeviceType.Ksital,
        DevicePassword = "00000"
    };
    db.Objects.Add(targetObj);
    await db.SaveChangesAsync();
}

var testAlarm = new AlarmEvent
{
    MonitoredObjectId = targetObj.Id,
    Timestamp = DateTime.UtcNow,
    Description = "ТЕСТОВАЯ ТРЕВОГА: Падение давления теплоносителя ниже 1.0 бар!",
    IsAcknowledged = false
};

db.Alarms.Add(testAlarm);
await db.SaveChangesAsync();

Console.WriteLine($"[УСПЕХ] Тревога ID #{testAlarm.Id} создана для объекта '{targetObj.Name}' в БД: {dbPath}");