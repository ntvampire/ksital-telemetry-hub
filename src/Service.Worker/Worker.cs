using Microsoft.EntityFrameworkCore;
using KsitalTelemetryHub.Core;
using KsitalTelemetryHub.Modem.Engine;
using KsitalTelemetryHub.Parser.Ksital;
using KsitalTelemetryHub.Parser.Ccu825;
using KsitalTelemetryHub.Parser.Owen;
using KsitalTelemetryHub.Storage.Sqlite;

namespace KsitalTelemetryHub.Service.Worker;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IConfiguration _configuration;
    private readonly string _portName;
    private readonly int _baudRate;
    private readonly int _pollIntervalSec;
    private readonly string _dbPath;

    public Worker(ILogger<Worker> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;

        _portName = _configuration.GetValue<string>("ModemSettings:PortName") ?? "COM3";
        _baudRate = _configuration.GetValue<int>("ModemSettings:BaudRate", 115200);
        _pollIntervalSec = _configuration.GetValue<int>("ModemSettings:PollIntervalSeconds", 10);
        _dbPath = _configuration.GetValue<string>("DatabaseSettings:DbPath") ?? "telemetry.db";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Инициализация базы данных SQLite ({DbPath})...", _dbPath);
        using (var initDb = new AppDbContext(_dbPath))
        {
            await initDb.Database.EnsureCreatedAsync(stoppingToken);
        }

        _logger.LogInformation("Запуск сервиса мониторинга телеметрии. Порт: {Port}, Скорость: {Baud}", _portName, _baudRate);

        GsmModemClient? modem = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 1. Контроль подключения к модему
                if (modem == null || !modem.IsConnected)
                {
                    _logger.LogInformation("Попытка подключения к модему на {Port}...", _portName);
                    modem?.Dispose();
                    modem = new GsmModemClient(_portName, _baudRate);
                    modem.Connect();
                    _logger.LogInformation("Модем успешно подключен.");
                }

                using var db = new AppDbContext(_dbPath);

                // 2. Отправка очереди исходящих команд оператора
                await ProcessOutgoingCommandsAsync(modem, db, stoppingToken);

                // 3. Вычитка входящих SMS
                var messages = modem.FetchAndPurgeSms();

                if (messages.Count > 0)
                {
                    _logger.LogInformation("Получено новых SMS: {Count}", messages.Count);

                    foreach (var sms in messages)
                    {
                        _logger.LogInformation("Обработка SMS от {Phone}: \"{Text}\"", sms.SenderNumber, sms.Text);

                        // Определение типа контроллера по номеру телефона в базе
                        var obj = await db.Objects.FirstOrDefaultAsync(o => o.PhoneNumber == sms.SenderNumber, stoppingToken);
                        var devType = obj?.DeviceType ?? DeviceType.Ksital;

                        KsitalReport report = devType switch
                        {
                            DeviceType.Ccu825 => new Ccu825MessageParser().Parse(sms.Text, sms.Timestamp),
                            DeviceType.OwenPlc => new OwenMessageParser().Parse(sms.Text, sms.Timestamp),
                            _ => KsitalMessageParser.Parse(sms)
                        };

                        report.SenderPhone = sms.SenderNumber;
                        if (obj != null) report.DeviceName = obj.Name;

                        // Сохранение отчета в базу
                        await db.SaveReportAsync(report, stoppingToken);

                        if (report.IsAlarm)
                        {
                            _logger.LogWarning("!!! ТРЕВОГА по объекту {Obj} ({Phone}): {Desc}", 
                                report.DeviceName, report.SenderPhone, report.AlarmDescription);
                        }
                        else
                        {
                            _logger.LogInformation("Отчет сохранен: [{Dev}] Объектов={Obj}, T1={T1}, T2={T2}, 220V={Pwr}",
                                devType,
                                report.DeviceName,
                                report.Temperatures.GetValueOrDefault("T1"),
                                report.Temperatures.GetValueOrDefault("T2"),
                                report.MainPower);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка опроса модема или обработки данных. Повторная попытка через {Sec} сек...", _pollIntervalSec);
                modem?.Dispose();
                modem = null;
            }

            await Task.Delay(TimeSpan.FromSeconds(_pollIntervalSec), stoppingToken);
        }

        modem?.Dispose();
        _logger.LogInformation("Сервис мониторинга остановлен.");
    }

    private async Task ProcessOutgoingCommandsAsync(GsmModemClient modem, AppDbContext db, CancellationToken ct)
    {
        try
        {
            var pendingCommands = await db.OutgoingCommands
                .Where(c => c.Status == CommandStatus.Pending)
                .OrderBy(c => c.CreatedAt)
                .Take(5)
                .ToListAsync(ct);

            foreach (var cmd in pendingCommands)
            {
                if (ct.IsCancellationRequested) break;

                _logger.LogInformation("Отправка SMS-команды #{Id} на {Phone}: \"{Payload}\"", cmd.Id, cmd.PhoneNumber, cmd.RawPayload);

                bool success = modem.SendSms(cmd.PhoneNumber, cmd.RawPayload);

                if (success)
                {
                    cmd.Status = CommandStatus.Sent;
                    cmd.SentAt = DateTime.UtcNow;
                    cmd.ErrorMessage = null;
                    _logger.LogInformation("Команда #{Id} успешно отправлена", cmd.Id);
                }
                else
                {
                    cmd.Status = CommandStatus.Failed;
                    cmd.ErrorMessage = "Ошибка отправки через модем (таймаут или сбой сети)";
                    _logger.LogWarning("Сбой отправки команды #{Id}", cmd.Id);
                }

                await db.SaveChangesAsync(ct);
                await Task.Delay(1000, ct); // Пауза для стабилизации GSM-тракта
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка обработки очереди OutgoingCommands");
        }
    }
}