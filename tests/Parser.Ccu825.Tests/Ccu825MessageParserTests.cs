using System;
using KsitalTelemetryHub.Core;
using KsitalTelemetryHub.Parser.Ccu825;
using Xunit;

namespace Parser.Ccu825.Tests;

public class Ccu825MessageParserTests
{
    [Fact]
    public void Parse_StandardReport_ReturnsExpectedValues()
    {
        var parser = new Ccu825MessageParser();
        string sms = "State: Out1=0 Out2=1 In1=NORM In2=ALARM T1=+22.5C T2=+65.0C T3=+18.2C Vbat=4.15V 220V=NORM";

        var report = parser.Parse(sms);

        Assert.Equal(22.5, report.Temperatures["T1"]);
        Assert.Equal(65.0, report.Temperatures["T2"]);
        Assert.Equal(18.2, report.Temperatures["T3"]);
        Assert.Equal(4.15, report.BatteryVoltage);
        Assert.Equal(PowerState.Normal, report.MainPower);
        Assert.True(report.IsAlarm);
        Assert.Contains("In2", report.AlarmDescription);
    }

    [Fact]
    public void Parse_PowerFailure_SetsAlarm()
    {
        var parser = new Ccu825MessageParser();
        string sms = "T1=+10.0C 220V=ALARM Vbat=3.8V In1=NORM";

        var report = parser.Parse(sms);

        Assert.Equal(PowerState.Off, report.MainPower);
        Assert.True(report.IsAlarm);
        Assert.Contains("220V", report.AlarmDescription);
    }
}