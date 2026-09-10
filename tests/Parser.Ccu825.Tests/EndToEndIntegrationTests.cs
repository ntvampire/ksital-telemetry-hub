using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using KsitalTelemetryHub.Core;
using KsitalTelemetryHub.Parser.Ccu825;
using KsitalTelemetryHub.Parser.Owen;
using KsitalTelemetryHub.Storage.Sqlite;
using Xunit;

namespace KsitalTelemetryHub.Tests;

public class EndToEndIntegrationTests : IDisposable
{
    private readonly string _dbPath;

    public EndToEndIntegrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"e2e_test_{Guid.NewGuid():N}.db");
        using var db = new AppDbContext(_dbPath);
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    [Fact]
    public async Task FullCycle_Ccu825_CommandQueue_And_IncomingTelemetry_SavesCorrectly()
    {
        using var db = new AppDbContext(_dbPath);

        // 1. Создаем объект CCU-825
        var ccuObj = new MonitoredObject
        {
            Name = "Котельная №4 (CCU-825)",
            PhoneNumber = "+79110001122",
            District = "Северный участок",
            DeviceType = DeviceType.Ccu825,
            DevicePassword = "pass"
        };
        db.Objects.Add(ccuObj);
        await db.SaveChangesAsync();

        // 2. Диспетчер выбирает первый шаблон команды для CCU-825
        var templates = DeviceCommandBuilder.GetTemplates(ccuObj.DeviceType);
        Assert.NotEmpty(templates);

        var firstTemplate = templates[0];
        string payload = DeviceCommandBuilder.BuildPayload(firstTemplate.Pattern, ccuObj.DevicePassword);

        var outgoingCmd = new OutgoingCommand
        {
            MonitoredObjectId = ccuObj.Id,
            PhoneNumber = ccuObj.PhoneNumber,
            RawPayload = payload,
            Description = firstTemplate.Title,
            CreatedAt = DateTime.UtcNow,
            Status = CommandStatus.Pending
        };
        db.OutgoingCommands.Add(outgoingCmd);
        await db.SaveChangesAsync();

        Assert.False(string.IsNullOrWhiteSpace(outgoingCmd.RawPayload));
        Assert.StartsWith("pass", outgoingCmd.RawPayload);

        // 3. Эмуляция отправки модемом
        outgoingCmd.Status = CommandStatus.Sent;
        outgoingCmd.SentAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // 4. Эмуляция получения входящего SMS-ответа от CCU-825
        string incomingSms = "State: Out1=0 Out2=1 In1=NORM In2=ALARM T1=+23.5C T2=+68.0C T3=+19.0C Vbat=4.12V 220V=NORM";

        var parser = new Ccu825MessageParser();
        var report = parser.Parse(incomingSms, DateTime.UtcNow);

        // 5. Сохранение телеметрии через контекст
        await db.SaveReportAsync(ccuObj.PhoneNumber, report);

        // 6. Проверки результатов в БД
        var savedObj = await db.Objects
            .Include(o => o.TelemetryRecords).ThenInclude(t => t.Temperatures)
            .FirstAsync(o => o.Id == ccuObj.Id);

        var lastTelemetry = savedObj.TelemetryRecords.OrderByDescending(t => t.Timestamp).FirstOrDefault();
        Assert.NotNull(lastTelemetry);
        Assert.Equal(PowerState.Normal, lastTelemetry.MainPower);
        Assert.Equal(4.12, lastTelemetry.BatteryVoltage);

        var t2 = lastTelemetry.Temperatures.FirstOrDefault(t => t.SensorCode == "T2");
        Assert.NotNull(t2);
        Assert.Equal(68.0, t2.Value);

        // Проверка фиксации тревоги по шлейфу In2
        Assert.True(report.IsAlarm);
        Assert.Contains("In2", report.AlarmDescription);
    }

    [Fact]
    public async Task FullCycle_OwenPlc_AlarmResponse_RegistersAlarmInDb()
    {
        using var db = new AppDbContext(_dbPath);

        // 1. Создаем объект ОВЕН ПЛК
        var owenObj = new MonitoredObject
        {
            Name = "ЦТП-2 (ОВЕН)",
            PhoneNumber = "+79229998877",
            District = "Западный участок",
            DeviceType = DeviceType.OwenPlc,
            DevicePassword = "123"
        };
        db.Objects.Add(owenObj);
        await db.SaveChangesAsync();

        // 2. Диспетчер выбирает первый шаблон команды для ОВЕН ПЛК
        var templates = DeviceCommandBuilder.GetTemplates(owenObj.DeviceType);
        Assert.NotEmpty(templates);

        var firstTemplate = templates[0];
        string payload = DeviceCommandBuilder.BuildPayload(firstTemplate.Pattern, owenObj.DevicePassword);

        var outgoingCmd = new OutgoingCommand
        {
            MonitoredObjectId = owenObj.Id,
            PhoneNumber = owenObj.PhoneNumber,
            RawPayload = payload,
            Description = firstTemplate.Title,
            CreatedAt = DateTime.UtcNow,
            Status = CommandStatus.Pending
        };
        db.OutgoingCommands.Add(outgoingCmd);
        await db.SaveChangesAsync();

        Assert.False(string.IsNullOrWhiteSpace(outgoingCmd.RawPayload));

        // 3. Эмуляция аварийного ответа от модема ПМ01 (пропадание 220V и перегрев)
        string incomingSms = "OWEN ALARM: T1=95.0C T2=72.5C 220V=0 VBAT=11.9V ERR=ПЕРЕГРЕВ КОТЛА";

        var parser = new OwenMessageParser();
        var report = parser.Parse(incomingSms, DateTime.UtcNow);

        await db.SaveReportAsync(owenObj.PhoneNumber, report);

        // 4. Проверка записи аварии
        var savedObj = await db.Objects
            .Include(o => o.TelemetryRecords)
            .FirstAsync(o => o.Id == owenObj.Id);

        var lastTelemetry = savedObj.TelemetryRecords.Last();
        Assert.Equal(PowerState.Off, lastTelemetry.MainPower);

        var alarms = await db.Alarms.Where(a => a.MonitoredObjectId == owenObj.Id).ToListAsync();
        Assert.NotEmpty(alarms);
        Assert.Contains(alarms, a => a.Description.Contains("220V"));
    }
}