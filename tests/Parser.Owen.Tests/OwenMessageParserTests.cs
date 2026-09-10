using System;
using KsitalTelemetryHub.Core;
using KsitalTelemetryHub.Parser.Owen;
using Xunit;

namespace Parser.Owen.Tests;

public class OwenMessageParserTests
{
    [Fact]
    public void Parse_NormalReport_ExtractsTemperaturesAndVoltages()
    {
        var parser = new OwenMessageParser();
        string sms = "OWEN PLC: T1=75.4C T2=52.1C T3=21.0C 220V=1 VBAT=12.4V ALARM=0 STATUS=WORK";

        var report = parser.Parse(sms);

        Assert.Equal(75.4, report.Temperatures["T1"]);
        Assert.Equal(52.1, report.Temperatures["T2"]);
        Assert.Equal(21.0, report.Temperatures["T3"]);
        Assert.Equal(12.4, report.BatteryVoltage);
        Assert.Equal(PowerState.Normal, report.MainPower);
        Assert.False(report.IsAlarm);
    }

    [Fact]
    public void Parse_AlarmAndPowerOff_SetsAlarmFlags()
    {
        var parser = new OwenMessageParser();
        string sms = "OWEN ALARM: T1=98.5C T2=70.2C 220V=0 VBAT=11.8V ERR=ПЕРЕГРЕВ КОТЛА";

        var report = parser.Parse(sms);

        Assert.Equal(98.5, report.Temperatures["T1"]);
        Assert.Equal(PowerState.Off, report.MainPower);
        Assert.True(report.IsAlarm);
        Assert.Contains("ПЕРЕГРЕВ КОТЛА", report.AlarmDescription);
        Assert.Contains("220V", report.AlarmDescription);
    }
}