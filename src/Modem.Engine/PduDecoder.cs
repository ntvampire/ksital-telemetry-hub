using System.Globalization;
using System.Text;
using KsitalTelemetryHub.Core;

namespace KsitalTelemetryHub.Modem.Engine;

public static class PduDecoder
{
    public static DecodedSms Decode(string pduHex)
    {
        if (string.IsNullOrWhiteSpace(pduHex))
            throw new ArgumentException("PDU-строка не может быть пустой.", nameof(pduHex));

        pduHex = pduHex.Trim();
        int offset = 0;

        int smscLen = Convert.ToInt32(pduHex.Substring(offset, 2), 16);
        offset += 2;

        if (smscLen > 0)
        {
            offset += smscLen * 2;
        }

        int firstOctet = Convert.ToInt32(pduHex.Substring(offset, 2), 16);
        offset += 2;
        bool hasUserDataHeader = (firstOctet & 0x40) != 0;

        int senderLen = Convert.ToInt32(pduHex.Substring(offset, 2), 16);
        offset += 2;

        int senderType = Convert.ToInt32(pduHex.Substring(offset, 2), 16);
        offset += 2;

        int senderOctets = (senderLen + 1) / 2;
        string senderHex = pduHex.Substring(offset, senderOctets * 2);
        offset += senderOctets * 2;

        string senderNumber = DecodePhoneNumber(senderHex, senderLen, senderType);

        offset += 2; // TP-PID

        int dcs = Convert.ToInt32(pduHex.Substring(offset, 2), 16);
        offset += 2;

        string sctsHex = pduHex.Substring(offset, 14);
        offset += 14;
        DateTime timestamp = DecodeTimestamp(sctsHex);

        int udl = Convert.ToInt32(pduHex.Substring(offset, 2), 16);
        offset += 2;

        string udHex = pduHex.Substring(offset);
        string text = DecodeUserData(udHex, udl, dcs, hasUserDataHeader);

        return new DecodedSms
        {
            SenderNumber = senderNumber,
            Timestamp = timestamp,
            Text = text,
            RawPdu = pduHex
        };
    }

    private static string DecodePhoneNumber(string hex, int length, int typeOfAddress)
    {
        var sb = new StringBuilder();
        if (typeOfAddress == 0x91)
            sb.Append('+');

        for (int i = 0; i < hex.Length; i += 2)
        {
            char low = hex[i + 1];
            char high = hex[i];

            if (low != 'F' && low != 'f')
                sb.Append(low);
            if (high != 'F' && high != 'f')
                sb.Append(high);
        }

        string result = sb.ToString();
        if (result.StartsWith("+") && result.Length > length + 1)
            result = result.Substring(0, length + 1);
        else if (!result.StartsWith("+") && result.Length > length)
            result = result.Substring(0, length);

        return result;
    }

    private static DateTime DecodeTimestamp(string hex)
    {
        try
        {
            int year = int.Parse($"{hex[1]}{hex[0]}");
            int month = int.Parse($"{hex[3]}{hex[2]}");
            int day = int.Parse($"{hex[5]}{hex[4]}");
            int hour = int.Parse($"{hex[7]}{hex[6]}");
            int minute = int.Parse($"{hex[9]}{hex[8]}");
            int second = int.Parse($"{hex[11]}{hex[10]}");

            int fullYear = 2000 + year;
            return new DateTime(fullYear, month, day, hour, minute, second);
        }
        catch
        {
            return DateTime.UtcNow;
        }
    }

    private static string DecodeUserData(string udHex, int udl, int dcs, bool hasUdhi)
    {
        int textOffset = 0;

        if (hasUdhi && udHex.Length >= 2)
        {
            int udhLength = Convert.ToInt32(udHex.Substring(0, 2), 16);
            textOffset = (udhLength + 1) * 2;
        }

        string payloadHex = udHex.Substring(textOffset);
        bool isUcs2 = (dcs & 0x0C) == 0x08 || dcs == 0x08;

        if (isUcs2)
        {
            byte[] bytes = new byte[payloadHex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(payloadHex.Substring(i * 2, 2), 16);
            }
            return Encoding.BigEndianUnicode.GetString(bytes);
        }

        if ((dcs & 0x0C) == 0x04)
        {
            byte[] bytes = new byte[payloadHex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(payloadHex.Substring(i * 2, 2), 16);
            }
            return Encoding.Latin1.GetString(bytes);
        }

        return "[Неизвестная кодировка]";
    }
}