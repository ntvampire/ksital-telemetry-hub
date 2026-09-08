using Xunit;
using KsitalTelemetryHub.Modem.Engine;

namespace KsitalTelemetryHub.Modem.Engine.Tests;

public class PduDecoderTests
{
    [Fact]
    public void Should_Decode_Russian_Sms_In_UCS2()
    {
        // Тестовая зашифрованная строка реальной СМС от КСИТАЛ
        string pdu = "07919710000000F0040B919761214365F700084290801100002020041A0441043804420430043B003A002004220031003D002B00360035002E0030";

        var result = PduDecoder.Decode(pdu);

        // Проверяем: определился ли номер и расшифровался ли русский текст
        Assert.Equal("+79161234567", result.SenderNumber);
        Assert.Contains("Кситал: Т1=+65.0", result.Text);
    }
}