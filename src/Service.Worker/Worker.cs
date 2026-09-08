using KsitalTelemetryHub.Modem.Engine;
using KsitalTelemetryHub.Parser.Ksital;
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
            // Автоматически создает файл БД и все таблицы, если их нет
            await initDb.Database.EnsureCreatedAsync(stoppingToken);
        }

        _logger.LogInformation("Запуск сервиса мониторинга КСИТАЛ. Порт: {Port}, Скорость: {Baud}", _portName, _baudRate);

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
                    _logger.LogInformation("Модем успешно подключен и переведен в PDU-режим.");
                }

                // 2. Вычитка накопившихся SMS
                var messages = modem.FetchAndPurgeSms();

                if (messages.Count > 0)
                {
                    _logger.LogInformation("Получено новых SMS: {Count}", messages.Count);

                    using var db = new AppDbContext(_dbPath);

                    foreach (var sms in messages)
                    {
                        _logger.LogInformation("Обработка SMS от {Phone}: \"{Text}\"", sms.SenderNumber, sms.Text);

                        // Парсинг параметров КСИТАЛ
                        var report = KsitalMessageParser.Parse(sms);

                        // Сохранение в SQLite
                        await db.SaveReportAsync(report, stoppingToken);

                        if (report.IsAlarm)
                        {
                            _logger.LogWarning("!!! ТРЕВОГА по объекту {Obj} ({Phone}): {Desc}", 
                                report.DeviceName, report.SenderPhone, report.AlarmDescription);
                        }
                        else
                        {
                            _logger.LogInformation("Отчет сохранен: Объектов={Obj}, T1={T1}, T2={T2}, 220V={Pwr}",
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
}