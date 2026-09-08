using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;
using KsitalTelemetryHub.Core;

namespace KsitalTelemetryHub.Modem.Engine;

public class GsmModemClient : IDisposable
{
    private readonly string _portName;
    private readonly int _baudRate;
    private SerialPort? _serialPort;
    private readonly object _lock = new();

    public bool IsConnected => _serialPort != null && _serialPort.IsOpen;

    public GsmModemClient(string portName, int baudRate = 115200)
    {
        _portName = portName;
        _baudRate = baudRate;
    }

    /// <summary>
    /// Открывает COM-порт и инициализирует модем в PDU-режиме.
    /// </summary>
    public void Connect()
    {
        lock (_lock)
        {
            if (IsConnected) return;

            _serialPort = new SerialPort(_portName, _baudRate, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = 3000,
                WriteTimeout = 3000,
                NewLine = "\r\n",
                Encoding = Encoding.ASCII
            };

            _serialPort.Open();

            // Проверка связи и сброс эха
            SendCommand("AT");
            SendCommand("ATE0"); // Выключаем эхо команд для упрощения парсинга ответов

            // Включаем режим PDU (0 - PDU, 1 - Text)
            string pduResponse = SendCommand("AT+CMGF=0");
            if (!pduResponse.Contains("OK"))
            {
                throw new InvalidOperationException($"Модем отказался перейти в PDU-режим: {pduResponse}");
            }
        }
    }

    /// <summary>
    /// Опрашивает модем на наличие входящих SMS, декодирует их и удаляет из памяти модема.
    /// </summary>
    public List<DecodedSms> FetchAndPurgeSms()
    {
        lock (_lock)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Модем не подключен. Сначала вызовите Connect().");

            var messages = new List<DecodedSms>();

            // AT+CMGL=4 возвращает все сообщения в PDU-режиме
            string response = SendCommand("AT+CMGL=4", timeoutMs: 5000);

            // Регулярное выражение для поиска строк +CMGL: <index>,<status>,[<alpha>],<length>\r\n<PDU_HEX>
            var regex = new Regex(@"\+CMGL:\s*(?<idx>\d+),\s*\d+,\s*.*?,?\s*\d+\r?\n(?<pdu>[0-9A-Fa-f]+)", RegexOptions.Multiline);
            var matches = regex.Matches(response);

            foreach (Match match in matches)
            {
                string idx = match.Groups["idx"].Value;
                string pdu = match.Groups["pdu"].Value.Trim();

                try
                {
                    // Декодируем сообщение через написанный PduDecoder
                    var decoded = PduDecoder.Decode(pdu);
                    messages.Add(decoded);

                    // Удаляем прочитанное SMS по его индексу, чтобы память не забивалась
                    SendCommand($"AT+CMGD={idx}");
                }
                catch
                {
                    // При поврежденном PDU можно залогировать и продолжить
                }
            }

            return messages;
        }
    }

    /// <summary>
    /// Отправляет низкоуровневую AT-команду и ждет завершения ответа (OK или ERROR).
    /// </summary>
    public string SendCommand(string command, int timeoutMs = 3000)
    {
        if (_serialPort == null || !_serialPort.IsOpen)
            throw new InvalidOperationException("COM-порт закрыт.");

        _serialPort.DiscardInBuffer();
        _serialPort.WriteLine(command);

        var sb = new StringBuilder();
        var startTime = DateTime.UtcNow;

        while ((DateTime.UtcNow - startTime).TotalMilliseconds < timeoutMs)
        {
            if (_serialPort.BytesToRead > 0)
            {
                string chunk = _serialPort.ReadExisting();
                sb.Append(chunk);

                string current = sb.ToString();
                if (current.Contains("OK\r\n") || current.Contains("ERROR\r\n"))
                {
                    break;
                }
            }
            Thread.Sleep(50);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Возвращает список всех доступных в Windows COM-портов.
    /// </summary>
    public static string[] GetAvailablePorts()
    {
        return SerialPort.GetPortNames();
    }

    public void Disconnect()
    {
        lock (_lock)
        {
            if (_serialPort != null)
            {
                if (_serialPort.IsOpen)
                {
                    _serialPort.Close();
                }
                _serialPort.Dispose();
                _serialPort = null;
            }
        }
    }

    public void Dispose()
    {
        Disconnect();
        GC.SuppressFinalize(this);
    }
}