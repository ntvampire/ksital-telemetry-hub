using System.Collections.Generic;

namespace KsitalTelemetryHub.Core;

public record CommandTemplate(string Title, string Pattern, string Description);

public static class DeviceCommandBuilder
{
    public static IReadOnlyList<CommandTemplate> GetTemplates(DeviceType type) => type switch
    {
        DeviceType.Ksital => new[]
        {
            new CommandTemplate("Запрос состояния (Kak dela)", "?", "Запрос стандартного отчета о температуре и 220V"),
            new CommandTemplate("Запрос температур", "Temp? {PASS}", "Запрос текущих показаний всех термодатчиков"),
            new CommandTemplate("Включить реле 1", "N1=1 {PASS}", "Замыкание исполнительного реле 1"),
            new CommandTemplate("Выключить реле 1", "N1=0 {PASS}", "Размыкание исполнительного реле 1"),
            new CommandTemplate("Включить реле 2", "N2=1 {PASS}", "Замыкание исполнительного реле 2"),
            new CommandTemplate("Выключить реле 2", "N2=0 {PASS}", "Размыкание исполнительного реле 2")
        },
        DeviceType.Ccu825 => new[]
        {
            new CommandTemplate("Запрос состояния", "{PASS} State", "Полный отчет о входах, выходах, температуре и батарее"),
            new CommandTemplate("Баланс SIM-карты", "{PASS} Balance", "Запрос баланса SIM контроллера"),
            new CommandTemplate("Включить выход Out1", "{PASS} Out1=1", "Включение управляющего выхода 1"),
            new CommandTemplate("Выключить выход Out1", "{PASS} Out1=0", "Выключение управляющего выхода 1"),
            new CommandTemplate("Перезагрузка CCU", "{PASS} Reboot", "Программная перезагрузка контроллера")
        },
        DeviceType.OwenPlc => new[]
        {
            new CommandTemplate("Запрос отчета", "PASS:{PASS} CMD:STATUS", "Запрос текущего технологического цикла ПЛК"),
            new CommandTemplate("Запрос аварий", "PASS:{PASS} CMD:ALARMS", "Выгрузка активных флагов неисправности"),
            new CommandTemplate("Сброс аварии", "PASS:{PASS} CMD:RESET", "Дистанционный сброс аварийных блокировок"),
            new CommandTemplate("Пуск контура", "PASS:{PASS} CMD:START", "Команда пуска регулирования"),
            new CommandTemplate("Стоп контура", "PASS:{PASS} CMD:STOP", "Команда останова контура")
        },
        _ => System.Array.Empty<CommandTemplate>()
    };

    public static string BuildPayload(string pattern, string password)
    {
        return pattern.Replace("{PASS}", password ?? string.Empty).Trim();
    }
}